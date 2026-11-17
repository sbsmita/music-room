# MusicRoom.Hands (foundation, implemented)

Leaf assembly: references only `Oculus.VR` and `Unity.InputSystem`. Everything above it (Core, instruments)
reads hands through `HandInput.Instance` / `IHandSource`.

Public contract (see ARCHITECTURE.md section 4):

- `IHandSource { Name; IsAvailable; Tick(dt); HandFrame Get(HandSide) }`
- `HandFrame`: 26 OpenXR-ordered joints (`HandJoint`), world positions/rotations/radii, confidence, pinch strength,
  pointer pose, tracker timestamps, `tipVelocity[5]` over distinct tracker samples.
- `HandMath`: pinch (15/25 mm), grasp (0.105/0.13 m), palm centre/normal, yaw, pinch point.
- Sources: `MetaHandSource` (OVRPlugin.GetHandState), `MouseHandSource` (editor: LMB poke, RMB pinch, MMB fist, Tab
  switches hand; no letter keys, no Shift), `FakeHandSource` + `HandPose` (tests), `UntrackedHandSource` (device player
  before the runtime is up: never the mouse).
- `HandInput` (execution order -100): picks the source, derives pinch/grasp edges (`Step(dt)`; tests with manual
  frames call it through `App.Step`), builds `MetaHandVisual`s; debug joint spheres in the editor only.
- `RigBootstrap` (-90): the OVRCameraRig, XR-or-desktop state (a late runtime still switches to Xr), runtime knobs
  (refresh rate, SRP foveation through `XRDisplaySubsystem.foveatedRenderingLevel`, perf levels), the VR clear colour at
  start, `Recentered`, `HmdMountChanged`.
- `DesktopRigFallback`: desktop camera (no look-drag on a `dragBlockMask` hit or with Shift / Alt); `XrMode`: passthrough
  toggle (available only once the start-up init completed), background colour, snap turn, rig moves.
- `HandGhost.shader` (`MusicRoom/HandGhost`, `_Alpha`): the hand material template is `MaterialTemplates/HandGhost`.

## 10-upgrade/experience.md (E1, E6)

- `ContactMath` (pure, allocation-free, 1:1 with the experience reference engine, a Node prototype): `PenetrationLift`
  (LiftMax 25 mm, LiftFade 40 mm), `SmoothLift` (instant attack, ReleaseTau 25 ms), `LiftAngle`, `LiftAxis`
  (normalize(v x n)), `FingerRotation` (ThetaMax 30 degrees, times `NormalWeight`: no turn within 15 degrees of the
  normal, all of it beyond 30, so a straight poke at the menu is not bent 13-30 degrees sideways in a direction set by
  tracking jitter; the vectors' fingers and the 35-degree test finger are untouched), `RotateAbout`, `ClampStep` (the
  reference fingerClampStep), `PivotJoint` (the proximal bones 3, 7, 12, 17, 22). Drums' stick visual uses LiftAngle /
  LiftAxis and the same NormalWeight. `MetaHandVisual.DelayedJointWorld(joint)`: the MR cut-out's delayed joint in world
  space (Core's contact shadows follow it in MR).
- `MetaHandVisual`: `enum Look { Ghost, Skin, PassthroughCutout }`, `look` (Skin by default), `cutoutLatency` 0.039 s,
  `SetLook(look)` (switches the SkinnedMeshRenderer material AND `OVRMeshRenderer.SetMaterial`, which otherwise restores
  its original material every frame), `SetContact(finger, lift, worldNormal)` / `ClearContacts()` (valid for one frame,
  set by Core's ContactVisuals), `ContactLift(finger)`, `UsingDelayedPose`. E1: in `LateUpdate`, after OVRSkeleton
  rewrote every bone from this frame's data, each lifted finger's proximal bone turns by theta about normalize(v x n) so
  its tip rises by the lift; skipped when the skeleton kept last frame's pose (`IsDataValid` false: nothing accumulates)
  and for the cut-out. MetaHandSource's arrays are never modified. E6: while `look == PassthroughCutout`, `Update` reads
  `OVRPlugin.GetHandStateAtTime(displayTime - cutoutLatency)` and the root pose and `GetSkeletonPoseData` use that past
  pose (the current one when the call fails; it gives up after 300 failures and logs both time bases once,
  `[Hands] cutout ...`). The play logic keeps the predicted pose. New `Create` overload with the three look materials.
- `HandInput`: `skinMaterial`, `cutoutMaterial` (AppRoot loads the templates when unset), `Look`, `SetLook(look)` (both
  visuals; visuals built later start with it). The editor's `DebugHandVisual` now draws a joint sphere and a bone cylinder
  per joint (2 x (26 + 24) renderers, editor only) in the lit HandSkin material, and follows the E1 clamp
  (`SetContact(side, finger, lift, normal)`, re-placed in `LateUpdate`; `JointPosition(side, joint)` = the drawn joint),
  so the editor and the GPU tests show what the headset's mesh does.
- `HandSkin.shader` (`MusicRoom/HandSkin`, template `HandSkin`, queue 3000): the VR hand lit by the room,
  `c = albedo (SampleSH(N) + sum L.color atten saturate((N.L + w)/(1 + w))) + rim`, wrap 0.5, albedo (0.86, .78, .70),
  rim `_RimColor` x 0.35 at power 2.5, alpha `_Alpha` x 0.92; main light + `#pragma multi_compile _ _ADDITIONAL_LIGHTS`
  loop; depth-only pass first; stereo instancing.
- `HandPassthrough.shader` (`MusicRoom/HandPassthrough`, template `HandPassthrough`, queue 3999): pass 1 `ZWrite On,
  ColorMask 0`, pass 2 `ZWrite Off, ZTest LEqual, Blend Zero OneMinusSrcAlpha, Zero OneMinusSrcAlpha` outputting
  (0, 0, 0, `_Alpha` smoothstep(0.05, 0.35, N.V)), vertices pushed 1.5 mm along the normal (`_Dilate`).
- Tests: EditMode `Tests/EditMode/Hands/ContactMathTests` (the fingerClamp vectors: index lift 3.6 mm / theta 2.5445
  degrees / corrected tip, the lift-of-penetration table, the 6-frame release, the thumb 16.545 degrees, AngleAxis =
  Rodrigues, the 30 degree cap); PlayMode `Tests/PlayMode/MR/HandLookMrTests` (MR keeps the Skin look and the clamp;
  "Real hands" switches to PassthroughCutout and disables the clamp, shadows stay at A 0.35; exit restores Skin and the clamp).
- Not testable without a headset (no OVRPlugin mesh in the editor): the bone turn on the real skinned hand, the skin
  shading, the cut-out and its latency. Device checks: experience.md sections 3 and 8 (ARCH 19).

- **Drawn-hand feel (2026-11-04, headset report "fingers wrong", "lag or jump").** The drawn turn of E1 is capped at
  `ContactMath.DrawnThetaMax` (20 degrees; the reference engine's 30 stays for the vectors) and eased per finger with
  `ContactMath.SmoothTheta` (attack 20 ms, release 50 ms) instead of snapping in one frame, in `MetaHandVisual`
  (`ContactTheta(finger)`), the editor's `DebugHandVisual` (eased on the app frame time passed to `Draw(source, dt)`, so
  manual-frame tests ease the same way; `ContactTheta(side, finger)`) and Core's finger shadows (the cap). Only a finger
  whose own tip touches turns (as before); the turn is forgotten while the pose is invalid or the cut-out shows. The
  confidence fade dims to `lowConfidenceAlpha` only after `lowConfidenceDelay` (0.3 s) of low confidence
  (`MetaHandVisual.FadeTarget`): Quest flips confidence for a frame or two whenever a finger touches a key, and the hand
  flickered to 40 %. Tests: EditMode `Hands/HandFeelTests` (3).

## 10-upgrade (experience.md E4, E5; burst.md section 10.1)

- **E4 Fast Motion Mode only while drumming.** The manifest asks for the low-jitter default tracking
  (`QuestSetup`: `HandTrackingFrequency.LOW`, was HIGH: an ARCHITECTURE section 0 change). `MetaHandSource.RequestFastMotion(bool)`
  (`OVRPlugin.RequestFastMotionMode`, a hint with no getter) plus `FastMotionRequested` (our own record) and
  `TryRequestFastMotion(bool, out int)` for callers without the Oculus.VR reference. The policy is Core's
  `FastMotionPolicy` / `FastMotionDriver` (App step 18). `OVRManager.fastMotionModeHandPosesEnabled` is never touched
  (OVRManager sends its own hint only when that field changes). Device: `adb logcat -e FMM`, `[Hands] FMM request`, and
  the dev-build `[Hands] sampleInterval L .. ms R .. ms` line every 10 s (about 16.7 ms with FMM, 33 ms without).
- **E5 90 Hz with a quality governor.** `RigBootstrap.PreferredRefreshHz = 90` (`FallbackRefreshHz` 72). `RefreshGovernor`
  (plain C#, EditMode-tested against the reference vectors) walks `QualityRung` 0 (90 Hz, SRP foveation 0.5) -> 1 (90 Hz,
  1.0) -> 2 (72 Hz, 0.5) -> 3 (72 Hz, 1.0): a 2 s window is bad when its p95 GPU time exceeds 90 % of the frame budget or
  more than 1 % of its frames were dropped, two bad windows in a row take the next rung, it never steps up within a
  session (one retry of rung 0 10 s after a resume), the first 10 s and 3 s after MR enter / exit, a recentre or a rung
  change are not judged, and a system drop to 72 Hz (thermal, `OVRManager.DisplayRefreshRateChanged`) moves to the
  matching 72 Hz rung. GPU time: `OVRPlugin.GetPerfMetricsFloat(App_GpuTime_Float)` (values under 0.5 taken as seconds),
  else `FrameTimingManager` (`PlayerSettings.enableFrameTimingStats`, set by `QuestSetup`); dropped frames:
  `Compositor_DroppedFrameCount_Int` deltas. `RigBootstrap.Rung`, `QualityChanged`, `Governor`, `GovernorEnabled`. Log:
  `[RIG] quality rung N: 90 Hz fov 0.5 -> 90 Hz fov 1.0 (gpu p95 10.6 ms, dropped 0)`. `sharpenType = Automatic`
  (`RigBootstrap.SharpenType`; UNVERIFIED under OpenXR: check the key letters by eye, None if no difference).
  Dynamic resolution stays OVRManager's (`DynamicResolutionMin/Max` 0.7 / 1.6, written by SceneBuilder); Unity OpenXR's
  Automatic Viewport Dynamic Resolution feature is pinned off by `QuestSetup`. Dev builds log
  `[RIG] dynres viewport .. x eye .. = .. (session p95 ..)` once a second: when p95 never exceeds 1.3 on the headset, set
  `DynamicResolutionMax = 1.3` (about 100 MB less eye buffer).
- **Passthrough on demand.** The scene has no `OVRPassthroughLayer` and `isInsightPassthroughEnabled` is false (so the VR
  room can run at CPU level 4, which Meta allows only without passthrough). `XrMode.StartPassthrough` (MR Enter) adds the
  hidden layer (`RigBootstrap.EnsurePassthroughLayer`) and sets the flag; `PassthroughPending` covers the request until
  OVRManager picks it up; `PassthroughStartable`; `StopPassthrough` (MR exit) hides it and shuts passthrough down. Log:
  `[RIG] passthrough ready X s after the start request` (the MR entry delay to measure on the headset).

- Since the first headset test: Meta's own index pinch beside the thumb-index rule (`HandMath.NextMetaPinch`: strength
  0.85 on, below 0.5 off, or IsIndexPinching; held 0.25 s while InputStateValid is off), exposed as
  `HandInput.metaPinching` and the grab pinch `grabPinching / grabPinchStart / grabPinchEnd` (either one), held like the
  other pinch state while untracked or during the system gesture; `HandMath.PalmFacesPoint`; `FakeHandSource.SetMetaPinch`,
  `SetInputValid`, `SetPointer` for tests. The palm-summon hotfix in `MetaHandVisual` is unchanged.

## "Real hands" (2026-11-02)

In "Play in my room" the hands are the drawn Skin hands by default, as in "Play here"; the menu's home page there has a
"Real hands" row that switches to the passthrough cut-out and back. The choice is Core's `IHandLook`
(`ContactVisuals.RealHandsInMr`, saved as `AppSettings.MrRealHands`, `musicroom.mrRealHands`; a save without it reads as
the drawn hands). `CutoutActive` (mixed reality and real hands) is what turns the finger clamp, the hand drums' palm lift
and the delayed-pose shadows off; with the drawn hands they all work in both rooms. Leaving mixed reality always shows
the Skin hands. Tests: `MR/HandLookMrTests`, `MR/MrInFrontTests`, EditMode `ContactShadowTests`, `ComfortCodecTests`.
