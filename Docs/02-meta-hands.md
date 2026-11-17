# 02 · Hands: reading and rendering hand tracking (Meta XR Core SDK 207)

Scope: per-frame hand data, the `IHandSource` abstraction (Meta implementation plus an editor mouse fallback), and
drawing and fading the hands. Paths are relative to these roots under
`Library/PackageCache/`: **core** `com.meta.xr.sdk.core@4c2eac90db2d`
(207.0.1), **isdk** `com.meta.xr.sdk.interaction@61177ebcfd9a`, **isdk-ovr**
`com.meta.xr.sdk.interaction.ovr@817fd81b9adf`, **xrhands** `com.unity.xr.hands@3e241be20642` (1.7.3).

Code:
- The C# sketch (8 files plus 2 tests; the shipping version is in `Assets/MusicRoom/Hands/`). It compiles in
  Unity 6000.3.27f1 with both `activeInputHandler` 0 and 2, and its 5 PlayMode tests pass in batchmode. All of
  this ran on a scratch copy of the project.
- A joint table: all 26 joints with their OpenXR, OVR, XR Hands and legacy names, radius, and left/right rest
  pose (§3).

---

## 0. Decisions

1. **Read hands from `OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref state)` directly**, the call
   `OVRHand` makes every `Update` (core `Scripts/Util/OVRHand.cs:213-215, 253-333`). One struct holds the joint
   positions, timestamps and pinch/aim/confidence flags. It needs no GameObjects, no execution-order coupling, and
   allocates nothing after the first call. Do not read data through `OVRHand` or XR Hands.
2. **Use the OpenXR hand skeleton** (26 joints in `XrHandJointEXT` order). `handSkeletonVersion: 1` is already in
   `Assets/Resources/OculusRuntimeSettings.asset`. Joint positions exist only with this skeleton.
3. **Poses are in `OVRCameraRig.trackingSpace`.** Convert with `FromFlippedZVector3f()`/`FromFlippedZQuatf()`,
   then `trackingSpace.localToWorldMatrix`.
4. **Draw the runtime hand mesh** with `OVRMesh` + an `OVRSkeleton` subclass + `OVRMeshRenderer`. Our
   `MetaHandVisual` implements their three public provider interfaces, so there is no `OVRHand` and no
   `OVRHandPrefab`. Fade with `_Alpha` through a `MaterialPropertyBlock`.
5. **Pinch and grasp thresholds**: pinch starts at a 15 mm thumb-to-index gap and ends at 25 mm; grasp starts below
   0.105 m and ends above 0.13 m. The runtime pinch flags and strengths are extra data.
6. **Measure velocity between distinct tracker samples** (`SampleTimeStamp`), from
   `GetUnextrapolatedHandState` positions, in tracking space.
7. **Project config**: Hands Only, and Hand Tracking Frequency HIGH (Fast Motion Mode) for the drums.

## 1. Prerequisites (project settings)

| Setting | Now | Needed | Source |
|---|---|---|---|
| `OVRProjectConfig.handTrackingSupport` (`Assets/Oculus/OculusProjectConfig.asset`) | `0` ControllersOnly | `2` HandsOnly | enum core `Editor/OVRProjectConfig.cs:64-69`. Writes manifest `uses-feature oculus.software.handtracking required=true` and `uses-permission com.oculus.permission.HAND_TRACKING` (`Editor/OVRManifestPreprocessor.cs:697-721`) |
| `OVRProjectConfig.handTrackingFrequency` | `0` LOW | `1` HIGH | enum `OVRProjectConfig.cs:74-91` (HIGH and MAX "track hands at high frequency", Fast Motion Mode). Writes `meta-data com.oculus.handtracking.frequency` (`OVRManifestPreprocessor.cs:723-731`) |
| `OVRRuntimeSettings.handSkeletonVersion` | `1` OpenXR | keep | applied by `OVRManager.InitOVRManager` (`Scripts/OVRManager.cs:2405-2409`) before `OVRManagerinitialized = true` (`:2471`) |
| `OVRManager` in scene | n/a | yes (it is on `Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab`) | without it the skeleton version is never pushed to the plugin |
| `OVRManager.controllerDrivenHandPosesType` | None | keep None | `OVRManager.cs:1958` |
| `wideMotionModeHandPosesEnabled` / `fastMotionModeHandPosesEnabled` | false | keep false (see §6) | `OVRManager.cs:1967, 1974` |

Editor-script form (assembly `Oculus.VR.Editor`; `CachedProjectConfig` `OVRProjectConfig.cs:260`, `CommitProjectConfig` `:441`):

```csharp
var c = OVRProjectConfig.CachedProjectConfig;
c.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.HandsOnly;
c.handTrackingFrequency = OVRProjectConfig.HandTrackingFrequency.HIGH;
OVRProjectConfig.CommitProjectConfig(c);
```

XR loader: the OVRPlugin hand APIs work with the **Oculus XR Plugin** and with **Unity OpenXR + "Meta XR Feature"**.
That feature requests `XR_EXT_hand_tracking`, `XR_FB_hand_tracking_aim`, `XR_FB_hand_tracking_mesh`,
`XR_FB_hand_tracking_capsules`, `XR_FB_hand_tracking_confidence`, `XR_META_hand_tracking_wide_motion_mode`
and others (core `Scripts/OpenXRFeatures/MetaXRFeature.cs:42-140`). XR Hands works only with the OpenXR loader
(its only provider is `xrhands Runtime/OpenXR/OpenXRHandProvider.cs`). This spec does not depend on the loader
choice.

## 2. OVRHand (question 1)

`OVRHand` (core `Scripts/Util/OVRHand.cs:37-43`) implements the skeleton, skeleton-renderer, mesh and mesh-renderer
data-provider interfaces.

| Member | Line | Meaning (verified in source) |
|---|---|---|
| `enum Hand {None=-1, HandLeft, HandRight}`, `HandFinger {Thumb..Pinky, Max}`, `TrackingConfidence {Low=0, High=0x3f800000}` | 49-79 | same values as `OVRPlugin` (`OVRPlugin.cs:1749-1760, 2009-2017`) |
| `[SerializeField] internal Hand HandType` | 81-82 | **internal**: app code cannot set it (see below) |
| `[SerializeField] private Transform _pointerPoseRoot` | 84-85 | parent for the pointer GameObject |
| `bool IsDataValid` | 129 | the `GetHandState` call succeeded, the controller show-state rule passed (`m_showState`, 90, 282-311), and the skeleton version matches (313-317) |
| `bool IsTracked` | 140 | `Status & HandTracked` (257) |
| `bool IsDataHighConfidence` | 134 | `IsTracked && HandConfidence == High` (267) |
| `TrackingConfidence HandConfidence` | 176 | (264) |
| `bool IsSystemGestureInProgress` | 147 | `Status & SystemGestureInProgress` (258). This is the palm-up pinch that opens the OS menu |
| `bool IsPointerPoseValid` | 153 | `Status & InputStateValid` (259) |
| `Transform PointerPose` | 160-170 | hidden `HideAndDontSave` GameObject (187-196) whose **local** pose is set to the tracking-space aim pose (261-262). It is parented to `_pointerPoseRoot` if set, otherwise left at the scene root, so its world pose is wrong once the rig moves. Ray = `position` along `forward` (`Scripts/Util/OVRInputModule.cs:712`) |
| `bool IsDominantHand` | 181 | `Status & DominantHand` (260) |
| `float HandScale` | 171 | (263) |
| `bool PoseSourceInferred` | 185 | `OVRPlugin.GetHandPoseSourceInferred` each Update (226-228) |
| `bool GetFingerIsPinching(HandFinger)` | 342-345 | `IsDataValid && (Pinches & (1<<finger))` |
| `float GetFingerPinchStrength(HandFinger)` | 354-364 | `PinchStrength[finger]`, 0..1 ("1 is a full pinch with the finger touching the thumb", 348-350) |
| `TrackingConfidence GetFingerConfidence(HandFinger)` | 371-381 | `FingerConfidences[finger]` |
| `OVRPlugin.Hand GetHand()` | 617-620 | the handedness |
| update | 198-243 | `Awake` and `Update` call `GetHandState(Step.Render)`. `FixedUpdate` uses `Step.Physics` only when the API is not OpenXR (234-237) |
| explicit `IOVRSkeletonDataProvider.GetSkeletonPoseData()` | 407-421 | gives `RootPose`, `RootScale`, `BoneRotations`, `BoneTranslations`. The Interaction SDK reads it by casting (`isdk-ovr Runtime/Scripts/Input/FromOVRHandDataSource.cs:187-188`) |

**How the hand type is set**: only by serialization (prefab YAML, or `SerializedObject` in an editor script). Meta's
Hand Tracking building block instantiates the prefab under `cameraRig.leftHandAnchor`, then calls the internal
setters: `HandType`, `SetSkeletonType(Hand.AsSkeletonType(version))`, `SetMeshType(...)`
(core `Editor/BuildingBlocks/BlockData/HandTracking/Scripts/HandTrackingBlockData.cs:55-75`). It can because its
assembly is on the `InternalsVisibleTo` list (`Scripts/AssemblyInfo.cs:23-52`); app assemblies are not.

**Adding the components at runtime with `AddComponent`:**
- `OVRHand`: unusable. Its `HandType` stays `None` without reflection, and `Awake` already polls with `None` (198-211).
- `OVRSkeleton` (`Scripts/Util/OVRSkeleton.cs`): `_skeletonType` is `[SerializeField] protected` (393-394), so a
  subclass can set it. `SetSkeletonType` is internal (467). `_dataProvider` is private and assigned in `Awake` by
  `GetComponentsInParent<IOVRSkeletonDataProvider>(true)` matched on `GetSkeletonType() == _skeletonType`
  (510-548). `_updateRootPose` and `_updateRootScale` are private and default to `false` (399-403).
  `_applyBoneTranslations` defaults to `true` (408-409).
- `OVRMesh` (`Scripts/Util/OVRMesh.cs`): `_meshType` is private (58-59), but `Awake` copies it from
  `GetComponent<IOVRMeshDataProvider>()` and then loads the mesh once (88-104). The retry in `Update` exists only in
  the editor (205-213).
- `OVRMeshRenderer` (`Scripts/Util/OVRMeshRenderer.cs`): `Awake` finds the provider, `OVRMesh` and `OVRSkeleton`
  on the same GameObject (91-107). It binds once in `Start` (109-122); the retry in `Update` is editor-only
  (190-197). `public void ForceRebind()` is at 141-145. The default `ConfidenceBehavior.ToggleRenderer` (71) means
  `renderer.enabled = IsDataValid && IsDataHighConfidence` (215-223). The default
  `SystemGestureBehavior.SwapMaterial` (74) resets `sharedMaterial` to the original every frame (235-238), so fade
  with a property block, never by swapping materials.
- **What works, and is tested**: our own `MonoBehaviour` implements `OVRSkeleton.IOVRSkeletonDataProvider`
  (`OVRSkeleton.cs:41-61`), `OVRMesh.IOVRMeshDataProvider` (`OVRMesh.cs:36-39`) and
  `OVRMeshRenderer.IOVRMeshRendererDataProvider` (`OVRMeshRenderer.cs:34-37`), plus
  `sealed class MetaHandSkeleton : OVRSkeleton` that sets `_skeletonType` in `Awake` before `base.Awake()`. All of
  it is added while the GameObject is inactive. The PlayMode test reads, by reflection, the private
  `_dataProvider`, `_meshType`, `_ovrMesh` and `_ovrSkeleton` fields of the three Meta components and asserts that
  they point at our objects. `OVRSkeleton` logs "Found IOVRSkeletonDataProvider reference in LeftHandVisual"
  (`OVRSkeleton.cs:520`).

**OVRHandPrefab** (`Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab`) is serialized as:
`OVRHand {HandType: 0}`; `OVRSkeleton {_skeletonType: 0 (legacy HandLeft), _updateRootPose: 0, _updateRootScale: 1}`;
`OVRSkeletonRenderer` (disabled); `OVRMesh {_meshType: 0 (legacy)}`; `OVRMeshRenderer {_confidenceBehavior: 1,
_systemGestureBehavior: 1}`; and a `SkinnedMeshRenderer` with `Materials/BasicHandMaterial.mat`. `OVRHand.OnValidate`
re-syncs the skeleton and mesh types to `HandType` (536-562), but only in the editor. So a runtime `Instantiate` is
broken for the OpenXR skeleton and for the right hand. If it is ever used, fix it in an editor script with
`SerializedObject` (right hand: `HandType=1`, `_skeletonType=5`, `_meshType=5`; plus `_confidenceBehavior=0` for our
own fade).

**Anchors.** `OVRCameraRig.leftHandAnchor`/`rightHandAnchor` (`Scripts/OVRCameraRig.cs:85, 90`) sit under
`trackingSpace` (`:65`, 566-570). Each frame they get the active controller's local pose (375-409). For hands that
is `OVRPlugin.GetNodePose(Node.HandLeft/HandRight)` (`Scripts/OVRInput.cs:1030-1032, 1066-1070`), re-read in
`onBeforeRender` when `OVRManager.LateControllerUpdate` is on (the default, `OVRManager.cs:1942`;
`OVRCameraRig.cs:497-512`). `left/rightHandAnchorDetached` (95-100) carry the Touch controller pose only while a
controller is not held, and are zero otherwise (411-470), so they do not matter for a hands-only app. Our visuals
sit under `trackingSpace` at `RootPose`, so they do not rely on the hand node pose equalling `RootPose` (that
equality is UNVERIFIED; Meta's prefab setup assumes it).

## 3. OVRSkeleton (question 2)

`SkeletonType` (`OVRSkeleton.cs:128-143` = `OVRPlugin.SkeletonType`, `OVRPlugin.cs:2193-2201`): `None=-1`,
`HandLeft=0` and `HandRight=1` ("OVR Hand", legacy), `Body=2`, `FullBody=3`, `XRHandLeft=4` and `XRHandRight=5`
("OpenXR Hand"). The type `OVRHand` reports follows `OVRRuntimeSettings.HandSkeletonVersion` (`OVRHand.cs:383-405`;
helpers in `Scripts/Util/OVREnumExtensions.cs:124-200`).

**OpenXR bones** (`OVRSkeleton.cs:193-223`; values `OVRPlugin.cs:1811-1839`):
`XRHand_Palm 0, XRHand_Wrist 1, XRHand_ThumbMetacarpal 2, ThumbProximal 3, ThumbDistal 4, ThumbTip 5,
IndexMetacarpal 6, IndexProximal 7, IndexIntermediate 8, IndexDistal 9, IndexTip 10, Middle… 11-15, Ring… 16-20,
Little… 21-25`, then `XRHand_Max = XRHand_End = 26`. Fingertips are 5, 10, 15, 20 and 25. The knuckle joints used
for the palm frame are the proximal joints 7, 12, 17 and 22. The metacarpals are 6, 11, 16 and 21.

**Legacy bones** (`OVRPlugin.cs:1778-1806`): `Hand_WristRoot 0, Hand_ForearmStub 1, Hand_Thumb0..3 2-5,
Hand_Index1..3 6-8, Hand_Middle1..3 9-11, Hand_Ring1..3 12-14, Hand_Pinky0..3 15-18, Hand_ThumbTip 19,
Hand_IndexTip 20, Hand_MiddleTip 21, Hand_RingTip 22, Hand_PinkyTip 23`. The legacy skeleton has no palm and no
index, middle or ring metacarpal. Its `GetHandState` path fills only `BoneRotations`, never `BonePositions`
(`OVRPlugin.cs:9752-9822`).

**Counts.** The OpenXR skeleton has 26 bones (`SkeletonConstants.MaxXRHandBones`, `OVRPlugin.cs:2185`), and all of
them are skinnable. The legacy skeleton has 24 bones, 19 of them skinnable (`Hand_MaxSkinnable`). There are 19
capsules (`MaxBoneCapsules`). **Gotcha:** `GetCurrentNumBones()` returns 0 for XRHand types because its switch has
no case for them (`OVRSkeleton.cs:1075-1088`), so use `Bones.Count`.

**Lifecycle.** `Start` calls `Initialize()` if the type is not `None` and the provider is enabled (554-591).
`Initialize()` succeeds once `OVRPlugin.GetSkeleton2` succeeds (593-603). It creates child GameObjects "Bones" and
"BindPoses" and one GameObject per bone, named e.g. `XRHand_IndexTip` (607-765). Then `IsInitialized = true`
(426). Until then it retries every `Update` (853-858). `IsDataValid` and `IsDataHighConfidence` are copied from the
provider every frame (861-886). `FixedUpdate` calls `Update` (946-956).

**World positions.** Use `skeleton.Bones[(int)OVRSkeleton.BoneId.XRHand_IndexTip].Transform.position` (`Bones`
is at 445; `Bones[i].Id` is skeleton bone i, 636-640). It is valid only after that `OVRSkeleton.Update` has run in
the frame. The OpenXR branch poses each bone relative to its parent, and the root bones relative to `RootPose`,
dividing by `RootScale` (920-937). So the skeleton's GameObject must sit at `RootPose` with scale `RootScale`.
This is why the prefab has `_updateRootScale: 1` and sits under the hand anchor. `OVRBone {Id, ParentBoneIndex,
Transform}` is at 1590-1646.

**Joint radius.** `HandState` has no per-joint radius. Capsule radii are in `Skeleton2.BoneCapsules`
(`OVRPlugin.cs:2166-2172`, from `GetSkeleton2`, 10220). The Interaction SDK uses a baked OpenXR table
(`isdk-ovr Runtime/Scripts/Input/OVRSkeletonData.cs:65-83`, `public static Oculus.Interaction.Input.OVRSkeletonData.LeftSkeleton`).
It maps each metacarpal to its proximal capsule and gives 0 for tips and the palm (`HandSkeletonOVR.cs:87-126`,
used at `FromOVRHandDataSource.cs:284`). `MetaHandSource.BakedRadius` copies that table, uses the distal radius for
the tips, and multiplies by `HandScale` (the scaling is my choice; UNVERIFIED that ISDK scales). Fingertip radius
at scale 1: thumb 9.8 mm, index 7.6 mm, middle 7.6 mm, ring 7.2 mm, little 6.4 mm. No play logic reads the joint radius.

## 4. The read path: `OVRPlugin.GetHandState`

`public static bool GetHandState(Step stepId, Hand hand, ref HandState handState)` (`OVRPlugin.cs:9628-9832`).
- `Step.Render = -1`. `Step.Physics = 0` is deprecated: under OpenXR it logs a warning and is turned into
  `Render` (672-676, 9633-9637).
- With the OpenXR skeleton (`HandSkeletonVersion == OpenXR`, a static property at 9623 set by
  `SetHandSkeletonVersion`, 5777-5797) it calls `ovrp_GetHandState3(step, -1 /*current frame*/, hand, …)`. The
  result is unrolled into the caller's arrays, which are reused when they are already sized 26 and 5 (9639-9747),
  so there is no per-frame garbage. It uses a static `cachedHandState3` buffer, so call it on the main thread only.
- It returns `true` whenever the call succeeds, **including when the hand is not tracked**. Always check
  `HandTracked`.

`struct HandState` (`OVRPlugin.cs:2041-2055`):

| Field | Meaning / use |
|---|---|
| `HandStatus Status` | flags (1763-1770): `HandTracked 1<<0` (pose and bones usable), `InputStateValid 1<<1` (pointer pose and pinch usable), `SystemGestureInProgress 1<<6`, `DominantHand 1<<7`, `MenuPressed 1<<8` |
| `Posef RootPose` | hand root (wrist) pose in tracking space |
| `Quatf[26] BoneRotations`, `Vector3f[26] BonePositions` | per joint, tracking space, real size. The Interaction SDK divides by `HandScale` to get a unit skeleton (`FromOVRHandDataSource.cs:270-285`) |
| `HandFingerPinch Pinches` | bit `1<<finger` (2031-2038) |
| `float[5] PinchStrength` | thumb-to-finger, 0..1, index = `HandFinger` |
| `Posef PointerPose` | aim ray in tracking space; +Z forward after the flip |
| `float HandScale` | |
| `TrackingConfidence HandConfidence`, `TrackingConfidence[5] FingerConfidences` | Low or High |
| `double RequestedTimeStamp`, `double SampleTimeStamp` | seconds (see §6) |

Conversion (`OVRCommon.cs:463` gives `(x, y, -z)`; `:650` gives `(-x, -y, z, w)`; `OVRExtensions` is public static):

```csharp
Vector3 p = toWorld.MultiplyPoint3x4(s.BonePositions[i].FromFlippedZVector3f()); // toWorld = trackingSpace.localToWorldMatrix
Quaternion q = trackingSpace.rotation * s.BoneRotations[i].FromFlippedZQuatf();
```

Joint axes in Unity after the flip: `q * Vector3.forward` points toward the fingertip and `q * Vector3.up` points
out of the back of the hand. I checked this on the baked rest pose: in the wrist frame the fingers lie along +Z and
the palm normal comes out at (±0.14, -0.98, -0.11) for both hands (test `RestPosePalmFacesDownAndOpenHandIsOpen`).

Other calls (all `public static`):
- `GetHandPoseSourceInferred(int frameIndex, Hand, out bool)` returns `Result` (13053-13059; OVRHand passes -1).
- `GetUnextrapolatedHandState(Hand, ref HandState)` (13061-13092) gives the newest tracker sample without
  prediction. It needs OpenXR (otherwise it logs a warning and returns false) and ovrp 1.116 or later; the bundled
  wrapper is `OVRP_1_207_0` (116).
- `GetHandStateAtTime(double time, Hand, ref HandState)` (9935-9966) needs OpenXR and ovrp 1.113 or later.
- `GetHandTrackingEnabled()` (9596).
- `OVRPlugin.initialized` (3665-3674) **calls into the native plugin**, and `Plugins/MacOpenXR/` is empty. So check
  availability with the managed-only `OVRManager.OVRManagerinitialized && OVRManager.isHmdPresent`
  (`OVRManager.cs:2156, 462`; `OVRCommon.cs:854-872`).

## 5. XR Hands (question 3)

**Setup.** It needs the Unity OpenXR loader with the OpenXR feature "Hand Tracking Subsystem"
(`UnityEngine.XR.Hands.OpenXR.HandTracking`, `featureId "com.unity.openxr.feature.input.handtracking"`,
`xrhands Runtime/OpenXR/HandTracking.cs:37-56`; `public static XRHandSubsystem subsystem` at 69). Aim and pinch
strength need a second feature, "Meta Hand Tracking Aim" (`MetaHandTrackingAim`,
`"com.unity.openxr.feature.input.metahandtrackingaim"`, `Runtime/OpenXR/MetaHandTrackingAim.cs:40-55`). It creates
the Input System devices `MetaAimHand.left` and `.right` (`Runtime/MetaAim.cs:114, 127, 140`). These carry `aimFlags`:
`MetaAimFlags` Computed, Valid, Index/Middle/Ring/LittlePinching (set only at strength 1.0), `SystemGesture 1<<6`,
`DominantHand 1<<7` and `MenuPressed 1<<8` (`MetaAim.cs:20-86`). They also carry `pinchStrengthIndex…Little`
(201-237) and the aim pose as `devicePosition`/`deviceRotation`. Reading them needs the new Input System active;
the project is currently `activeInputHandler: 0`, which is old-only.

**Access.** Call `SubsystemManager.GetSubsystems(List<XRHandSubsystem>)` and take the one that is `running` (the
pattern in `Samples~/HandVisualizer/Scripts/HandVisualizer.cs:183-200`). The `updatedHands(XRHandSubsystem,
UpdateSuccessFlags, UpdateType)` event fires twice per frame: `Dynamic` near Update and `BeforeRender` near
`onBeforeRender` (`Runtime/XRHandSubsystem.cs:20-40, 178-249`). There are also `trackingAcquired` and
`trackingLost` events (260, 271).

**Data.**
- `XRHand` (`Runtime/XRHand.cs:15-52`): `GetJoint(XRHandJointID)`, `rootPose`, `handedness`, `isTracked`.
- `XRHandJoint` (`Runtime/XRHandJoint.cs:21-180`): `trackingState`, plus `TryGetPose`, `TryGetRadius`,
  `TryGetLinearVelocity` and `TryGetAngularVelocity`. Each returns false unless its `XRHandJointTrackingState` bit
  is set: Radius, Pose, LinearVelocity, AngularVelocity, WillNeverBeValid, HighFidelityPose (`Runtime/Enums.cs:9-45`).

**IDs** (`Runtime/Enums.cs:51-200`): `Invalid 0`, `Wrist 1 (= BeginMarker)`, `Palm 2`, `ThumbMetacarpal 3` … up to
`LittleTip 26`, then `EndMarker 27`. `ToIndex() = id - 1` and `FromIndex(i) = i + 1` (282-302). **Array index 0 is
the Wrist and 1 is the Palm, while OpenXR/OVR order has Palm 0 and Wrist 1.** Swap those two; every other index
matches (`XRHandsSource.cs` does `x = i==0 ? 1 : i==1 ? 0 : i`).

**Space.** "Joint poses are relative to the real-world point chosen by the user's device … transform the pose based
on the XROrigin" (`XRHandJoint.cs:80-104`): `pose.GetTransformedBy(new Pose(origin.position, origin.rotation))`.
With `OVRCameraRig` the origin is `trackingSpace`. UNVERIFIED that this is the same origin as OVRPlugin's tracking
space; on the device, compare wrist positions from both sources.

**Trade-offs:**

| | OVRPlugin (chosen) | XR Hands |
|---|---|---|
| Loader | Oculus XR or OpenXR + Meta XR Feature | OpenXR only |
| Confidence, dominant hand, system gesture | `Status` and confidences | only through `MetaAimHand.aimFlags`; no confidence |
| Aim pose and pinch strength | in `HandState` | needs the Meta Aim feature and the new Input System |
| Per-joint radius | no (baked capsule table) | yes (`TryGetRadius`) |
| Per-joint velocity | no | `TryGetLinearVelocity` if the runtime fills it (UNVERIFIED on Quest) |
| Timestamps, raw samples | `SampleTimeStamp`, `GetUnextrapolatedHandState` | none |
| Hand mesh | runtime mesh through `OVRMesh` | none on Quest (`Runtime/OpenXR/Meshing/IOpenXRHandMeshDataSupplier.cs` is only an interface); the sample ships FBX hands |
| Fast and wide motion modes, microgestures | yes | no (still OVRPlugin) |
| Per-joint validity | no (hand level and finger confidence) | yes |

The sketch's `XRHandsSource.cs` compiles and is an optional drop-in. Do not use it unless the loader becomes
OpenXR and a Meta-specific gap appears.

## 6. Frequency, latency and timestamps (question 4)

- **Tracker rate.** It is set by the manifest `com.oculus.handtracking.frequency` (§1). LOW is the default; HIGH and
  MAX are Fast Motion Mode, "improved tracking of fast movements" (`OVRProjectConfig.cs:74-91`). The exact sample
  rates are UNVERIFIED, so measure them with `HandFrame.sampleInterval`. A runtime switch also exists:
  `OVRManager.fastMotionModeHandPosesEnabled` ("Experimental", `OVRManager.cs:1969-1974`) calls
  `OVRPlugin.RequestFastMotionMode(bool)` (`OVRManager.cs:3033-3037`, `OVRPlugin.cs:13045-13051`). Prefer the
  manifest setting.
- **Wide Motion Mode** (`wideMotionModeHandPosesEnabled` calls `SetWideMotionMode2HandPosesEnabled`;
  `OVRManager.cs:1967, 3027-3031`; `OVRPlugin.cs:5979-5990`) infers hands outside the camera view. Leave it off. If
  it is ever turned on, never trigger notes while `poseInferred` is set.
- **Prediction.** `Step.Render` gives the pose for this frame's display time (`RequestedTimeStamp`).
  `SampleTimeStamp` is, by its name, the capture time of the data behind it (semantics and clock UNVERIFIED; only
  differences are used). Meta re-reads only the anchors before rendering (`OVRCameraRig.cs:497-512`). `OVRSkeleton`
  bones update only in Update and FixedUpdate (848-851, 946-956).
- **Velocity over the tracker's own interval.** The predicted pose moves every frame, so frame deltas include
  extrapolation. `SampleVelocity` (`HandFrame.cs`) therefore updates only when the sample time changes, with
  v = Δp/Δt. It clamps the gap to at least 4 ms, ignores gaps over 0.2 s, keeps an EMA of 0.25 on the interval,
  and uses zero velocity at rest. A gap over 0.2 s, or one that
  goes backwards, restarts it at zero.
  - `MetaHandSource` feeds it the 5 fingertips from `GetUnextrapolatedHandState`, in tracking space, with Δt from
    their `SampleTimeStamp`. After 300 failed calls in a row while tracked, it switches to the predicted state.
  - The result is rotated to world space with `trackingSpace.rotation`. Rig moves never read as hand speed, so no
    `resetMotion()` is needed for velocity.
- **Use**: test contact with `position[]` (predicted, lowest latency) and judge strike strength with
  `tipVelocity[]`. `GetHandStateAtTime(displayTime + audioLatency)` could anticipate strikes (UNVERIFIED).
- **On-device check** (log once a second): `displayTime - sampleTime` (expect tens of ms), `sampleInterval` at
  LOW and at HIGH, `Time.deltaTime`, and whether the raw call succeeds.

## 7. `IHandSource` design (question 5)

```csharp
namespace MusicRoom.Hands {
  public enum HandSide { Left = 0, Right = 1 }
  public enum Finger { Thumb, Index, Middle, Ring, Little }
  public enum HandJoint { Palm, Wrist, ThumbMetacarpal, …, LittleTip }   // 26, OpenXR order = OVR XRHand_* values
  public interface IHandSource { string Name { get; } bool IsAvailable { get; } void Tick(float deltaTime); HandFrame Get(HandSide side); }
}
```

`HandFrame` holds one hand for one frame. It is a class allocated once per hand per source; no garbage per frame.

| Field | Type | Meta source |
|---|---|---|
| `side` | HandSide | fixed |
| `isTracked`, `isHighConfidence`, `isDominant`, `systemGesture`, `poseInferred` | bool | `Status` flags, `HandConfidence`, `GetHandPoseSourceInferred` |
| `handScale` | float | `HandScale` |
| `lostSeconds` | float | set by `HandInput`; the "grace" timer that sticks use |
| `position[26]`, `rotation[26]` | Vector3, Quaternion | world; +Z points distal, +Y dorsal |
| `radius[26]` | float | baked table × scale |
| `fingerHighConfidence[5]`, `pinchStrength[5]`, `runtimePinch[5]` | | `FingerConfidences`, `PinchStrength`, `Pinches` (pinch only while `InputStateValid`) |
| `pointerValid`, `pointer` | bool, Pose | `InputStateValid`, `PointerPose` |
| `sampleTime`, `displayTime`, `newSample`, `sampleInterval`, `tipVelocity[5]` | | §6 |

While a hand is untracked `MarkLost()` clears the flags and velocities but keeps the last positions.

**Sources:**
- `MetaHandSource` (§9). The headset.
- `MouseHandSource`, for the editor and desktop: one synthetic hand in a pointing pose, with its index tip on the
  mouse ray. The tip rests 5 mm above the first collider hit, or at `hoverDistance` (0.45 m; the wheel moves it
  ±2 cm) when nothing is hit. **LMB** pokes: the tip goes 2 cm past the surface in 50 ms (about 0.5 m/s, a real
  press). **RMB** pinches: the thumb tip goes onto the index tip. **MMB or G** makes a fist (grasp about 0.09 m).
  **Shift** drives the left hand. The joints come from the baked rest pose (the joint table's `restLeftWristLocal_m`; the right
  hand mirrors x), with the middle, ring and little fingers half-curled. That leaves the index tip the most forward
  point and the grasp amount at 0.139 m, above `GraspOpen` (tested). Input uses `#if ENABLE_INPUT_SYSTEM`
  (`Mouse.current`), else `Input.*`; both compile. A desktop "pointer that lives on the surface it clicked" is a
  different thing and can stay as a separate path.
- `XRHandsSource` (optional, §5). A scripted or replay source for tests implements the same interface.

**`HandInput`** (`[DefaultExecutionOrder(-100)]`, a singleton). It chooses `Meta` when `IsAvailable`, otherwise
`Mouse`, and calls `Source.Tick` first thing in Update. It then derives pinch and grasp with hysteresis into
`pinching`, `pinchStart`, `pinchEnd`, `grasping`, `graspStart` and `graspEnd`, indexed by `(int)HandSide`. The
edge flags are valid for one frame. It holds the previous state while a hand is untracked or doing the system
gesture. When the Meta source goes live it creates the two `MetaHandVisual`s under `trackingSpace`. Instruments
read `HandInput.Instance.Get(side)`.

**Gesture rules** (`HandMath` in `HandFrame.cs`; rules stated in the right-handed source frame, -z forward):

| Rule | Unity |
|---|---|
| `pinchstart`/`pinchend`: thumb-to-index gap ≤ 0.015 starts, > 0.025 ends | `HandMath.NextPinch(pinching, PinchGap(h))` with `PinchStartGap 0.015`, `PinchEndGap 0.025` |
| `graspAmount`: mean distance of the middle, ring and little tips to the wrist; `GRASP_CLOSE 0.105`, `GRASP_OPEN 0.13` | `HandMath.GraspAmount`, `NextGrasp`. The open rest pose measures 0.1765 m |
| `palm()`: centre of wrist, index proximal and pinky proximal; n = cross(a-w, b-w), negated for the left hand | `HandMath.Palm`. **The operands are swapped**, `Cross(b-w, a-w)`, because flipping z flips the sign of a cross product (tested: the normal comes out of the palm) |
| `handYaw`: wrist -Z, atan2(x, z) | `HandMath.YawDegrees` from `rot * Vector3.forward`. **Deltas change sign** |
| `pinchPoint` (midpoint of thumb and index tips), `pinchGap` | `HandMath.PinchPoint`, `PinchGap` |
| `TIPS` order: index, middle, ring, pinky, thumb | `Finger` order Thumb…Little. Keep the TIPS order in the pointer layer if anything depends on it |
| `joint.visible === false` | `!isTracked` (OVR has no per-joint validity; `fingerHighConfidence` is per finger) |
| `p.vel`, `p.sampleDt` | `tipVelocity[k]`, `sampleInterval` |
| `resetMotion()` after a teleport | not needed for velocity. Code that tests swept contact should reset on its own rig moves or on `OVRCameraRig.TrackingSpaceChanged` (`OVRCameraRig.cs:171`) |

## 8. Rendering and fading

`MetaHandVisual.Create(side, metaSource, rig.trackingSpace, handMaterial)` builds the hand while the GameObject is
inactive: a child of `trackingSpace`, then `SkinnedMeshRenderer` (with `sharedMaterial`), `MetaHandVisual`,
`MetaHandSkeleton`, `OVRMesh` and `OVRMeshRenderer`, then `SetActive(true)`. Every frame, at execution order -80
(after `HandInput` at -100, before the Meta components at 0):

1. When tracked, set the root's local pose from `RootPose` (flipped z) and its `localScale` to `HandScale`. This is
   what the private `_updateRootPose`/`_updateRootScale` would do (`OVRSkeleton.cs:888-897`). It is required
   because the OpenXR bones are relative to `RootPose` and `RootScale` (920-937).
2. `GetSkeletonPoseData()` passes the very arrays `MetaHandSource` read, so mesh and input share one sample. When
   untracked it reports `IsDataValid = false`, so `OVRSkeleton` keeps the last pose (868-875) and the hand freezes
   while it fades out.
3. **Fade.** Target alpha: 0 when untracked or during the system gesture, 1 at high confidence, 0.4 at low, times
   `appAlpha` (e.g. lowered while holding a drumstick). Alpha moves linearly, reaching the target in 0.08 s going in
   and 0.25 s going out. `LateUpdate` writes `_Alpha` through a `MaterialPropertyBlock`.
4. `GetMeshRendererData()` returns `IsDataValid = true`, `IsDataHighConfidence = Alpha > 0.001`. ToggleRenderer then
   turns the renderer off exactly when the hand is invisible.
5. If `OVRMeshRenderer` is unbound while the mesh and skeleton are ready, call `ForceRebind()`. Start order is not
   guaranteed, and the device build has no retry.

Material contract: a float `_Alpha` (`MetaHandVisual.AlphaId`). `Assets/MusicRoom/Hands/HandGhost.shader`
("MusicRoom/HandGhost") draws a depth-only prepass, so fingers do not show through each other, then an
alpha-blended rim-lit fill. It has a URP SubShader and a Built-in SubShader and uses the stereo macros. It imported
cleanly with Built-in active; its look still needs tuning.

On the device `OVRMesh` loads its mesh once, in `Awake`. If `IsInitialized` is still false after about 2 s,
recreate the visual, up to 5 times (UNVERIFIED that this ever happens). The mesh comes from the runtime
(`OVRPlugin.GetMesh`, `OVRMesh.cs:136-146`), so the app ships no hand asset. It never loads in the editor
(`OVRMesh.cs:124-126`), so draw the mouse hand as debug spheres of `radius[i]` at each joint.

Not chosen:
- (a) `OVRHandPrefab` fixed at edit time (§2).
- (b) The Interaction SDK `HandVisual` with `Interaction/OculusHand` (`_Opacity`, `_OutlineOpacity`, `_WristFade`;
  `isdk Runtime/Shaders/OculusHand.shader:24-52`). Its alpha samples `_FingerGlowMask` by UV
  (`OculusHandFillCG.cginc:58-60`), so it is tied to that SDK's own hand mesh (UNVERIFIED on the runtime mesh). If
  that SDK's rig is adopted for UI, use its hands instead of `MetaHandVisual`; never draw two hand meshes.
- Late re-pose: in `onBeforeRender`, re-read `GetHandState` into a copy used only for visuals, then call
  `MetaHandSkeleton.Repose()`. Not in the sketch; measure first.

## 9. C# sketch: `MetaHandSource` (`Assets/MusicRoom/Hands/MetaHandSource.cs`; same code, shorter comments)

```csharp
using UnityEngine;

namespace MusicRoom.Hands
{
    public sealed class MetaHandSource : IHandSource
    {
        // Baked OpenXR capsule radii at HandScale 1 (isdk-ovr OVRSkeletonData.cs:65-83); tips reuse the distal radius.
        static readonly float[] BakedRadius =
        {
            0.02323196f, 0.01822828f,                                              // palm (widest palm capsule), wrist
            0.01838252f, 0.01028295f, 0.009768805f, 0.009768805f,                  // thumb
            0.01029526f, 0.01029526f, 0.008038102f, 0.007636196f, 0.007636196f,    // index
            0.01117394f, 0.01117394f, 0.008030958f, 0.007629411f, 0.007629411f,    // middle
            0.009922137f, 0.009922137f, 0.007611672f, 0.007231089f, 0.007231089f,  // ring
            0.008483353f, 0.008483353f, 0.006764194f, 0.006425985f, 0.006425985f,  // little
        };
        const int RawGiveUpFrames = 300;

        readonly System.Func<Transform> _trackingSpace;
        readonly HandFrame[] _frames = { new HandFrame(HandSide.Left), new HandFrame(HandSide.Right) };
        readonly OVRPlugin.HandState[] _state = new OVRPlugin.HandState[2]; // predicted for this frame's display time
        readonly OVRPlugin.HandState[] _raw = new OVRPlugin.HandState[2];   // newest tracker sample, not extrapolated
        readonly SampleVelocity[] _velocity = { new SampleVelocity(), new SampleVelocity() };
        readonly Vector3[] _tips = new Vector3[5];
        readonly Vector3[] _vel = new Vector3[5];
        int _rawFailures;

        public MetaHandSource(System.Func<Transform> trackingSpace) { _trackingSpace = trackingSpace; }

        public string Name => "Meta";
        public bool IsAvailable => OVRManager.OVRManagerinitialized && OVRManager.isHmdPresent; // managed-only checks
        public HandFrame Get(HandSide side) => _frames[(int)side];
        public ref OVRPlugin.HandState State(HandSide side) => ref _state[(int)side]; // for MetaHandVisual

        public void Tick(float deltaTime)
        {
            Transform ts = _trackingSpace != null ? _trackingSpace() : null;
            Matrix4x4 toWorld = ts != null ? ts.localToWorldMatrix : Matrix4x4.identity;
            Quaternion rot = ts != null ? ts.rotation : Quaternion.identity;
            for (int h = 0; h < 2; h++) Read(h, toWorld, rot);
        }

        void Read(int h, Matrix4x4 toWorld, Quaternion rot)
        {
            HandFrame f = _frames[h];
            OVRPlugin.Hand hand = h == 0 ? OVRPlugin.Hand.HandLeft : OVRPlugin.Hand.HandRight;
            bool ok = OVRPlugin.HandSkeletonVersion == OVRHandSkeletonVersion.OpenXR
                      && OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref _state[h]);
            ref OVRPlugin.HandState s = ref _state[h];
            OVRPlugin.HandStatus status = ok ? s.Status : 0;
            bool tracked = ok && (status & OVRPlugin.HandStatus.HandTracked) != 0
                           && s.BonePositions != null && s.BonePositions.Length == HandFrame.JointCount;
            if (!tracked) { f.MarkLost(); _velocity[h].Reset(); return; }

            f.isTracked = true;
            f.isHighConfidence = s.HandConfidence == OVRPlugin.TrackingConfidence.High;
            f.isDominant = (status & OVRPlugin.HandStatus.DominantHand) != 0;
            f.systemGesture = (status & OVRPlugin.HandStatus.SystemGestureInProgress) != 0;
            f.handScale = s.HandScale > 0f ? s.HandScale : 1f;
            f.poseInferred = OVRPlugin.GetHandPoseSourceInferred(-1, hand, out bool inferred) == OVRPlugin.Result.Success
                             && inferred;

            for (int i = 0; i < HandFrame.JointCount; i++)
            {
                f.position[i] = toWorld.MultiplyPoint3x4(s.BonePositions[i].FromFlippedZVector3f());
                f.rotation[i] = rot * s.BoneRotations[i].FromFlippedZQuatf();
                f.radius[i] = BakedRadius[i] * f.handScale;
            }

            bool input = (status & OVRPlugin.HandStatus.InputStateValid) != 0; // pointer + pinch usable
            for (int k = 0; k < 5; k++)
            {
                f.fingerHighConfidence[k] = s.FingerConfidences[k] == OVRPlugin.TrackingConfidence.High;
                f.pinchStrength[k] = input ? s.PinchStrength[k] : 0f;
                f.runtimePinch[k] = input && ((int)s.Pinches & (1 << k)) != 0;
            }
            f.pointerValid = input;
            if (input)
                f.pointer = new Pose(toWorld.MultiplyPoint3x4(s.PointerPose.Position.FromFlippedZVector3f()),
                                     rot * s.PointerPose.Orientation.FromFlippedZQuatf());

            f.displayTime = s.RequestedTimeStamp;
            f.sampleTime = s.SampleTimeStamp;

            bool raw = false;
            if (_rawFailures < RawGiveUpFrames)
            {
                raw = OVRPlugin.GetUnextrapolatedHandState(hand, ref _raw[h])
                      && (_raw[h].Status & OVRPlugin.HandStatus.HandTracked) != 0
                      && _raw[h].BonePositions != null && _raw[h].BonePositions.Length == HandFrame.JointCount;
                _rawFailures = raw ? 0 : _rawFailures + 1;
            }
            ref OVRPlugin.HandState v = ref (raw ? ref _raw[h] : ref s);
            for (int k = 0; k < 5; k++)
                _tips[k] = v.BonePositions[HandFrame.TipJoint[k]].FromFlippedZVector3f(); // tracking space
            double t = v.SampleTimeStamp > 0 ? v.SampleTimeStamp : Time.unscaledTimeAsDouble;
            f.newSample = _velocity[h].Push(t, _tips, _vel, ref f.sampleInterval);
            for (int k = 0; k < 5; k++) f.tipVelocity[k] = rot * _vel[k];
        }
    }
}
```

Wiring, from a bootstrap or scene builder: put `HandInput` on any GameObject, set `handMaterial` to a material
using `MusicRoom/HandGhost`, and make sure `OVRCameraRig` (with `OVRManager`) is in the scene. Assembly
definition: `MusicRoom.Hands` references `Oculus.VR`, `Unity.XR.Hands` and `Unity.InputSystem`. Drop
`Unity.XR.Hands` together with `XRHandsSource.cs` if XR Hands is not wanted.

## 10. Verification status

**Checked by compile and tests** (clone, batchmode, Unity 6000.3.27f1, macOS editor target; test file
the sketch's `Tests/HandWiringTests.cs`, 5 out of 5 passed with each input-handler setting):
- All 8 files compile against core, xrhands and the Input System. The `ENABLE_INPUT_SYSTEM` branch was compiled
  (confirmed in `MusicRoom.Hands.rsp`), and so was the legacy branch.
- `MetaHandVisual.Create` makes `OVRSkeleton`, `OVRMesh` and `OVRMeshRenderer` adopt our provider, with types
  `XRHandLeft`/`XRHandRight`, parented to the tracking space.
- `HandInput` falls back to the mouse with no runtime, and `MetaHandSource.Tick` does not throw on a macOS editor
  that has no native OVRPlugin.
- Rest pose: the palm normal comes out of the palm; open grasp amount 0.1765 m; pinch gap 0.1148 m; the mouse
  pointing pose has grasp 0.1392 m and its index tip is the most forward point.
- `SampleVelocity`: 1 cm over 16 ms gives 0.625 m/s; a repeated timestamp is not a new sample; a 0.2 s gap restarts.

**UNVERIFIED (needs the headset):**
1. What `RequestedTimeStamp` and `SampleTimeStamp` mean and which clock they use; the real tracker rates at LOW and
   HIGH.
2. Whether `GetUnextrapolatedHandState` succeeds on the current Horizon OS runtime (if not, the code falls back).
3. That `BonePositions` are real size (inferred from the Interaction SDK dividing by `HandScale`), and the
   radius × `HandScale` choice.
4. When `OVRMesh` and `GetSkeleton2` first succeed after launch (hence the `ForceRebind` and recreate safeguards).
5. That `GetNodePose(HandLeft)` equals `RootPose` (relevant only to the prefab-under-anchor setup).
6. That the XR Hands origin equals `trackingSpace`, and whether XR Hands joint velocities are filled on Quest.
7. How `HandGhost` looks, the fade timings, and whether the low-confidence alpha of 0.4 feels right.
