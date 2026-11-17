# 10-upgrade · experience: what Unity makes possible beyond sound

Key: `experience`. Scope: hands-only, seated upgrades of the app (Unity
6000.3.27f1, Meta XR Core SDK 207.0.1, MRUK 207.0.1, OpenXR 1.16.1, URP 17.3) that make the music room feel more real.
This spec does not change any sound. The contract it extends is `Docs/ARCHITECTURE.md`, called **ARCH** below.

Files of the experience Node lab (all deterministic, Node 26, no packages; the expected values are copied into the EditMode tests):

| File | What it is |
|---|---|
| `experience.reference.mjs` | Reference for every algorithm here (float32 state structs, no per-frame allocation, 1:1 to C#) |
| `make-vectors.mjs` → `experience.vectors.json` | Test vectors with expected values, and Node timings |
| `bench-frame.mjs` | Worst-case cost of every upgrade together in one frame |
| `measure-harp-partials.mjs` → `harp-partials.json` | Partial decay times measured from the CURRENT harp renders (`Tests/EditMode/Audio/Reference~/harp_old_*.wav`) |

Paths in `file:line` form are under the project's `Library/PackageCache/`: **core** =
`com.meta.xr.sdk.core@4c2eac90db2d`, **mruk** = `com.meta.xr.mrutilitykit@751b71eb977a`, **urp** =
`com.unity.render-pipelines.universal@ee0b6bbace10`, **oxr** = `com.unity.xr.openxr@ef0033a586bf`. Every API name below
was checked in these sources (or, for Unity engine types, in `Unity.app/.../Managed/UnityEngine/UnityEngine.CoreModule.xml`).

**No audio renders for this key.** Nothing here changes a sound, so there is no old/new A/B pair and no render files.

---------------------------------------------------------------------------------------------------------------------------

## 0. Decisions (ranked; E1–E8 are the ones to build before Nov 18)

The guiding rule comes from the user's history: realism beats synthetic feedback, and louder or flashier is not better.
So the top upgrades **replace glows and pulses with physical cues**: fingers that rest on keys, sticks that bounce, soft
contact shadows, cymbals that rock, strings that blur.

| # | Upgrade | What the user notices | Frame cost (Quest 3S) | Risk | Effort | Before Nov 18 |
|---|---|---|---|---|---|---|
| **E1** | **Fingers stop on surfaces** (visual contact clamp on keys, drum heads, the menu) | The rendered fingertip rests on the key and rides it down. It no longer sinks 1–3 cm into the keys | CPU ≈ 5 µs | low | 1 d | yes |
| **E2** | **Sticks bounce instead of passing through** (visual stick pivots in the hand, plus a 20 ms rebound) | Sticks hit the head and spring back. Today they pass up to 6 cm through it | CPU < 1 µs | low | 0.5 d | yes |
| **E3** | **Soft contact shadows** under fingertips and stick tips (analytic sphere occlusion, 1 draw) | You can judge finger height before touching: the shadow darkens and tightens as the finger nears the key | GPU ≤ 0.1 ms, CPU ≈ 10 µs | low | 1 d | yes |
| **E4** | **Fast Motion Mode only while drumming** (manifest LOW + `OVRPlugin.RequestFastMotionMode`) | Fast strokes keep tracking (~60 Hz camera) while drumming. Piano and harp keep the low-jitter default | 0 | low | 0.5 d | yes |
| **E5** | **90 Hz with a quality governor** (foveation rung, 72 Hz fallback, dynamic-resolution cap) | Smoother hands. Visual latency −2.8 ms, strike detection wait −1.4 ms on average | uses the 11.1 ms budget | medium (thermal) | 0.5 d | yes |
| **E6** | **Hands that belong in the room**: VR lit "skin" shader; MR **time-aligned passthrough cut-out** (your real hands over the virtual keys) | In VR the hands catch the lamp and the fire. In MR you see your own hands on the keys, not a ghost | GPU ≈ 0.1 ms | medium (MR alignment) | 1 d | yes |
| **E7** | **Drum kit motion**: impulse-driven cymbal rocking with swirl, a 1–2 frame head "give", a kick beater on every kick | Cymbals tip down where struck and rock slowly. The scale pulse is gone. The kick pedal moves | CPU < 1 µs | low | 1 d | yes |
| **E8** | **Harp strings blur like real strings** (envelope of the modal motion, decays exactly as the current harp sound) | A plucked string turns into a soft spindle that narrows as the note dies. No 45 Hz flicker | CPU ≤ 0.09 ms worst case | low | 0.75 d | yes |
| E9 | MR piano remembered with `OVRSpatialAnchor` | "Play in my room" puts the piano back on the same real table next time | 0 | medium (localization) | 1 d + device | stretch |
| E10 | First-use hitch removal with `GraphicsStateCollection` (Vulkan PSO warm-up) | No stutter the first time the fire, MR or the menu appears | load-time | low | 0.5 d + device trace | stretch |
| E11 | Box-projected reflection probe | The cymbals, the chrome and the black piano reflect the room in the right place | GPU ≈ 0.1 ms | low | 0.25 d | optional |
| E12 | Multiview Render Regions + symmetric projection | ~5–10 % GPU headroom (UNVERIFIED) | negative | medium | 0.25 d + device | optional |
| E13 | Microgestures (thumb swipe/tap) | Hands-free instrument switch | 0 | high (false triggers while playing) | 0.5 d | no |
| E14 | Passthrough "evening" grade in MR | The real room dims and warms a little, like the virtual one | 0 (compositor) | low | 0.25 d | optional |

**Rejected:** Wide Motion Mode; Application SpaceWarp; realtime shadow maps in VR; Depth API occlusion; an `OVROverlay`
menu; 120 Hz; strike look-ahead prediction; Interaction SDK hand visuals; late hand re-pose; piano hammer/damper visuals
(the slim digital piano has none). Reasons are in section 13.

Implementation order (one integrator, about 7 dev-days plus two headset sessions): E4 + E5 (constants and two small
classes) → E1 + E2 → E3 → E7 → E8 → E6 → device session 1 → E9/E10 if time allows → device session 2.

---------------------------------------------------------------------------------------------------------------------------

## 1. Facts this spec rests on

| Fact | Value | Source |
|---|---|---|
| Hand-tracking camera rate | default ≈ 30 Hz; Fast Motion Mode (FMM) ≈ 60 Hz ("Hand Tracking Processed FPS = 58.5 Hz / Camera FPS = 60.1"; ≈ 50 Hz on 50 Hz mains) | Meta FMM doc; UploadVR on Hand Tracking 2.4 |
| FMM trade-offs | "may increase jitter, impacting high accuracy interactions like direct touch or typing"; short exposure degrades in low light; not with Multimodal; disables the Dynamic Object Tracker | Meta FMM doc |
| FMM controls | build time `OVRProjectConfig.HandTrackingFrequency` LOW/HIGH/MAX (HIGH = MAX = FMM; manifest `com.oculus.handtracking.frequency`); runtime `OVRPlugin.RequestFastMotionMode(bool)` is a **hint with no getter**; the two are independent | core `Editor/OVRProjectConfig.cs:74-91`, `Editor/OVRManifestPreprocessor.cs:723-731`, `Scripts/OVRPlugin.cs:13045-13051`; Meta FMM doc |
| FMM checks on device | `adb logcat -e FMM` → `HandTrackingService: FMM: enabled: true; active: true`; `adb logcat -e "Camera FPS"`; Settings › System › Advanced › Developer › *Hand Tracking Frequency Override* (High/Low/None) | Meta FMM doc |
| Wide Motion Mode (WMM) | needs Body Tracking Support Required (`com.oculus.software.body_tracking`, runtime permission `com.oculus.permission.BODY_TRACKING`); its poses are inferred from body tracking and "inaccurate when tracking is lost" | Meta WMM doc; core `Editor/OVRManifestPreprocessor.cs:918-932`, `Scripts/OVRPermissionsRequester.cs:91`, `Scripts/OVRManager.cs:1966-1974, 3027-3031` |
| Quest 3S refresh rates | 72, 80, 90, 96, 100, 120 Hz; default 72; above 72 Hz thermal throttling may first drop to 72 Hz | Meta display refresh rate doc |
| Quest 3S eye buffer | 1680 × 1760 at render scale 1.0 (panel 1832 × 1920) | Meta OS render scale doc |
| Hand-tracking latency | Quest 3 photon-to-photon ≈ 70 ms (31.3 ms virtual hand behind the passthrough hand + ≈ 39 ms passthrough latency, OS v63) | Road to VR measurement |
| Past-pose query | `OVRPlugin.GetHandStateAtTime(double time, Hand, ref HandState)` (OpenXR, ovrp ≥ 1.113) | core `Scripts/OVRPlugin.cs:9935-9966` |
| Perf metrics | `OVRPlugin.GetPerfMetricsFloat(PerfMetrics.App_GpuTime_Float)`, `GetPerfMetricsInt(Compositor_DroppedFrameCount_Int)` (`XR_META_performance_metrics` is in the Meta XR feature); Unity's `FrameTimingManager.GetLatestTimings(...).gpuFrameTime` as fallback | core `Scripts/OVRPlugin.cs:737-770, 8797-8842`, `Scripts/OpenXRFeatures/MetaXRFeature.cs` extension list |
| Refresh change event | `OVRManager.DisplayRefreshRateChanged(float from, float to)` | core `Scripts/OVRManager.cs:356, 3059-3066` |
| Dynamic resolution | `OVRManager.enableDynamicResolution` sets `XRSettings.eyeTextureResolutionScale = max` (1.6 on Quest 3-class) and moves `renderViewportScale` ≤ 32 px per frame toward the runtime's recommended resolution | core `Scripts/OVRManager.cs:638-669, 2385-2399, 2691-2730` |
| Sharpening | `OVRManager.instance.sharpenType = OVRPlugin.LayerSharpenType.{None, Normal, Quality, Automatic}` → `OVRPlugin.SetEyeBufferSharpenType` | core `Scripts/OVRManager.cs:584-597`, `Scripts/OVRPlugin.cs:657-663` |
| Spatial anchors (local, no internet) | `OVRSpatialAnchor` + `WhenLocalizedAsync`, `SaveAnchorAsync`, `EraseAnchorAsync`, `LoadUnboundAnchorsAsync(IEnumerable<Guid>, List<UnboundAnchor>)`, `UnboundAnchor.LocalizeAsync(timeout)`, `.TryGetPose(out Pose)` (world space), `.BindTo(anchor)` | core `Scripts/OVRSpatialAnchor.cs:153, 206, 607, 620, 1136, 1034, 991, 1068` |
| Passthrough grading | `OVRPassthroughLayer.SetBrightnessContrastSaturation(b, c, s)` (each in [−1, 1]); `SetColorLut(new OVRPassthroughColorLut(Color[], ColorChannels.Rgb), weight)` | core `Scripts/OVRPassthroughLayer.cs:166, 280`, `Scripts/Util/Passthrough/OVRPassthroughColorLut.cs:103` |
| Microgestures | `OVRPlugin.GetHandTrackingState(Step.Render, hand, ref HandTrackingState)` → `.Microgesture` ∈ {SwipeLeft, SwipeRight, SwipeForward, SwipeBackward, ThumbTap} (`XR_META_hand_tracking_microgestures`) | core `Scripts/OVRPlugin.cs:2019-2028, 9970-9998` |
| PSO warm-up | `UnityEngine.Experimental.Rendering.GraphicsStateCollection`: `BeginTrace`, `EndTrace`, `SaveToFile`, `SendToEditor`, `LoadFromFile`, `WarmUp`, `WarmUpProgressively` | Unity 6000.3 `UnityEngine.CoreModule.xml` |
| URP probe options | `UniversalRenderPipelineAsset.reflectionProbeBoxProjection`, `.reflectionProbeBlending` | urp `Runtime/Data/UniversalRenderPipelineAsset.cs:1413, 1431` |
| Already in the project | MR boundary suppression (`XrMode.SetPassthrough` sets `shouldBoundaryVisibilityBeSuppressed`), dynamic resolution 0.7–1.6, SRP foveation 1.0, CPU/GPU `SustainedHigh`, `m_symmetricProjection 0`, `optimizeMultiviewRenderRegions 0`, `lateLatchingMode 0` | `Assets/MusicRoom/Hands/{XrMode,RigBootstrap}.cs`, `Assets/XR/Settings/OpenXRPackageSettings.asset:395-415` |

**CPU cost convention.** Node timings are on an Apple M5 Pro. The assumed ratios are **Quest 3S IL2CPP main thread = 4 × Node**
(one Cortex-A78-class core at about 2.4 GHz versus an M5 P-core, plus IL2CPP null and bounds checks) and **Burst = 2 × Node**.
These ratios are UNVERIFIED. The device check is the `[PERF]` profiler markers named in each section.

---------------------------------------------------------------------------------------------------------------------------

## 2. Shared plumbing (contract changes, frame order, budgets)

### 2.1 Core contract: contact surfaces (new, `Core/Contracts/Contact.cs`)

```csharp
namespace MusicRoom.Core
{
    [System.Flags] public enum ContactUse : byte { None = 0, Shadow = 1, Clamp = 2, Both = 3 }

    public struct ContactHit
    {
        public Vector3 Point;     // on the surface, world: the foot of the query point along -Normal
        public Vector3 Normal;    // unit, world, out of the surface (key top: the key's tilted up axis)
        public Vector3 Tangent;   // unit, world, in the surface: the shadow clip axis (a key's width); zero = no clip
        public float ClipHalf;    // metres the shadow may extend along Tangent (0 = unlimited)
        public float Height;      // signed: Normal · (query − Point)
        public ContactUse Use;
    }

    /// <summary>Implemented by Piano (keys at their CURRENT depth), Drums (heads, pad, cymbal plates at their CURRENT
    /// tilt) and Menu (the panel face). Registered with app.Register&lt;IContactSurface&gt;(this) in Init.
    /// Must not allocate. Returns false when the instrument is not present, its rig is held, or the point is outside every footprint.</summary>
    public interface IContactSurface
    {
        bool QueryContact(Vector3 world, float above, float below, out ContactHit hit);
    }
}
```

Footprints (implementer rules):
- **Piano**: piano-local `x` within the key under the tip from `KeyAt(x, back)` (white or black, the same function the
  contact logic uses), widened by 2 mm; `back` in [0, key length]. Point = the key's top surface at its current depth
  (the 88-key spring state). `Tangent` = piano right. `ClipHalf` = half the visible key width (`WW/2` = 11.05 mm white,
  `BW/2` = 6.85 mm black) when the key is down by more than 2 mm, otherwise 0 (all keys at rest share one plane, so the
  shadow may spill onto the neighbours). `Use = Both`.
- **Drums**: piece-local `hypot(x, z) ≤ r` (pad: its radius). Plane y = 0 of the piece, rotated by the cymbal tilt of E7
  for cymbals. `Use = Both` for heads and the pad; `Shadow` only for cymbals, since a hand on a cymbal is the choke gesture
  and must not be visually lifted.
- **Menu**: inside the 0.32 × 0.24 m face on the R = 0.8 m cylinder. Normal = toward the player. `Use = Both` (poke limiting).

### 2.2 Core service: `ContactVisuals` (new, `Core/Render/ContactVisuals.cs` + `ContactShadowPool.cs`)

It runs inside `App.Frame` as **step 8a**, after `Tick(StickHover)` (step 7) and before `HoverGlow.Update` (step 8). Key
depths, cymbal tilts and stick poses are final by then. Per frame:
1. For each connected `HandState` that is not mouse-driven, and each finger: query the tip joint (`above` 0.06 m, `below` 0.04 m)
   against every `IContactSurface` and keep the hit with the smallest `Height ≥ −below`. The surface list is cached from
   `app.GetAll<IContactSurface>()` on the first frame and on `LayoutChangedEvent`; never call `GetAll` per frame.
2. `Clamp`: compute the lift (E1) and call `HandInput.Visual(side)?.SetContact(finger, lift, normal)`. Skip it in MR, see E6.
3. `Shadow`: add two blobs per finger (tip sphere and distal-joint sphere, E3). Stick tips come from Drums:
   `IDrumsApi` gains `int StickShadowSpheres(Span<Vector4> centreRadius)`, which returns the visual tip-ball centres from E2.
   Query those too.
4. Upload the shadow mesh (one draw).

### 2.3 Hands changes (foundation)

```csharp
public sealed class MetaHandVisual {
    public enum Look { Ghost, Skin, PassthroughCutout }      // E6; default Skin in VR, PassthroughCutout in MR
    public Look look;  public float cutoutLatency = 0.039f;  // E6
    public void SetContact(Finger finger, float lift, Vector3 worldNormal);   // E1, valid for this frame only
    public void ClearContacts();
}
public sealed class MetaHandSource { public OVRPlugin.Result RequestFastMotion(bool on); public bool FastMotionRequested { get; } }  // E4
public sealed class RigBootstrap   { public QualityRung Rung { get; } public event Action<QualityRung> QualityChanged; }        // E5
```

### 2.4 Budgets

| Item | CPU main thread (Quest, est.) | GPU | Draws | Memory |
|---|---|---|---|---|
| E1 clamp (10 fingers) | 2.6 µs math + ≈ 10 µs queries (12 points × ≈ 12 cheap tests) | 0 | 0 | 80 B |
| E2 sticks (2) | 0.6 µs | 0 | 0 | 16 B |
| E3 shadows (≤ 32 blobs) | ≈ 10 µs (mesh write 128 verts) | ≤ 0.1 ms (small multiply-blended quads) | **+1** | 9 KB mesh |
| E7 cymbals (3) + heads + beater | 0.3 µs + head mesh rewrite (289 verts) on ≤ 3 frames per hit | 0 | 0 | 7 KB |
| E8 harp (22 strings ringing, worst case) | 160 µs at full rate IL2CPP; **80 µs at the specified half rate**; Burst 79 / 40 µs (full / half rate) | +≈0.05 ms (3 string copies, transparent) | 0 (same mesh) | 25 KB |
| Whole set, worst case | **≈ 0.1–0.2 ms** of the 3 ms script budget (ARCH §17) | ≤ 0.25 ms | +1 (≤ 150 total) | < 0.1 MB |

Node measurement of all of it in one frame (`bench-frame.mjs`): 41.5 µs, of which 1.3 µs is everything except the harp.

---------------------------------------------------------------------------------------------------------------------------

## 3. E1 · Fingers stop on surfaces (visual contact clamp)

**Why.** The play logic uses the raw OpenXR tip joint, which sits at the centre of the fingertip end. The skin is about
one joint radius (7.6 mm index, 9.8 mm thumb) beyond it. A key starts to move when the joint reaches its top and bottoms
out 10 mm later. So today the rendered pad is already 7.6 mm inside the key when it starts moving, and 17 mm or more inside
at the bottom, and deeper still when prediction overshoots on a stop. Real fingers never enter keys. This upgrade is visual
only: it never changes which notes sound or how loud.

**Algorithm** (reference `fingerClampStep`, `liftAngle`, `liftAxis`, `fingerPenetrationLift`):
1. Core (step 8a): `h = hit.Height` of the tip joint; `pen = rc − h` with `rc = HandFrame.radius[tip]` (baked capsule radius
   × `HandScale`, ARCH §6.1).
2. Wanted lift `want`: 0 if `pen ≤ 0`; `pen` up to `LiftMax`; then it fades linearly to 0 at `LiftFade`. Deeper than that
   means tracking is wrong, so the penetration is allowed to show.
3. Smoothing per hand×finger: `lift = want ≥ lift ? want : want + (lift − want)·exp(−dt/ReleaseTau)`. Attack is instant;
   release has no pop when the finger slides off a key edge. When the surface vanishes, keep the last normal while the lift decays.
4. Hands (`MetaHandVisual.LateUpdate`, after `OVRSkeleton.Update` has posed the bones at order 0): for each finger with
   `lift > 0`, pivot `P` = the proximal bone (`XRHand_ThumbProximal` 3, `IndexProximal` 7, `MiddleProximal` 12, `RingProximal`
   17, `LittleProximal` 22), `T` = the tip bone (5, 10, 15, 20, 25), `v = T − P`, `x = n·v`, `ρ = |v|`,
   `θ = acos(x/ρ) − acos(min(x + lift, ρ)/ρ)`, capped at `ThetaMax`; axis `a = normalize(v × n)`;
   `bone[P].rotation = Quaternion.AngleAxis(θ·Rad2Deg, a) * bone[P].rotation`. The children (intermediate, distal, tip)
   follow through the bone hierarchy (core `Scripts/Util/OVRSkeleton.cs:666-673`), and the skinned mesh follows its bones at render time.
   Unity's `AngleAxis` applied to a vector equals the right-hand Rodrigues formula of the reference on raw coordinates
   (checked: `Euler(90,0,0)·(0,0,1) = (0,−1,0)` in both), so the numbers match.
5. Never modify `MetaHandSource`'s arrays: input and the cut-out of E6 read them.

| Constant | Value | Source |
|---|---|---|
| `rc` | tip joint radius × HandScale (index 7.6 mm, thumb 9.8 mm) | ARCH §6.1 `BakedRadius` (isdk-ovr `OVRSkeletonData.cs:65-83`) |
| `LiftMax` | 0.025 m | design: deeper than key travel (10 mm) plus a typical prediction overshoot (≈ 1 cm) |
| `LiftFade` | 0.040 m | design: beyond this the hand is clearly elsewhere (tracking error); correcting it would bend fingers unnaturally |
| `ThetaMax` | 30° | design: the visible MCP flex limit before the finger looks broken |
| `ReleaseTau` | 0.025 s | design: about 2 frames at 90 Hz, removes the pop at key edges |

**Data layout.** `struct FingerContact { float Lift; float3 Normal; }` × 10 (hands × fingers) in Core; the visual holds
`float lift[5]; Vector3 normal[5]` per hand, reset by `ClearContacts()` at the start of `ContactVisuals.Update`.

**Test vectors** (`experience.vectors.json` → `fingerClamp`; tolerance 1e-5 m / 1e-4 rad):
- Pivot (0, 0.045, −0.06), tip (0, −0.004, 0.02), plane point (0, −0.008, 0), n (0, 1, 0), rc 0.0076, dt 1/90:
  lift **0.0036**, θ **0.044411 rad (2.5445°)**, axis **(−1, 0, 0)**, corrected tip **(0, −0.0004, 0.022097)**, which is exactly rc above the plane.
- Lift of penetration: −0.002 → 0; 0 → 0; 0.0036 → 0.0036; 0.010 → 0.010; 0.025 → 0.025; 0.030 → **0.016667**; 0.040 → 0; 0.05 → 0.
- Release after the surface disappears (6 frames at 1/90 s): **0.002308, 0.00148, 0.000949, 0.000608, 0.00039, 0.00025**.
- Thumb: pivot (0.03, 0.03, −0.05), tip (0.035, −0.006, 0), plane y = 0, rc 0.0098 → lift **0.0158**, θ **16.5450°**, tip **(0.035813, 0.0098, 0.008131)**.
- EditMode: an `Is.Not.AllocatingGCMemory()` check around `ContactVisuals.Update` with 10 tracked fingers.
- PlayMode (`FakeHandSource`, `manualFrames`): index tip driven 15 mm into a key → `ContactVisuals.Lift(Right, Index)` > 0.005
  while `IPianoApi` still fires exactly one `pianoOn`. This proves the logic is unchanged.

**Device check.** Rest a finger on a white key and press slowly. The pad stays on the key top and rides it down, with no
fingertip visible under the key surface from the seat. Press 3 cm "through" the keybed: the finger bends up to 30° and then
lets go smoothly. Profiler marker `[Core] contact` ≤ 0.05 ms.

**Risk / fallback.** A visible knuckle kink when θ is large (thumb test: 16.5°). If it looks wrong, split θ 60/40 between
the MCP and PIP bones. `ContactVisuals.ClampEnabled = false` turns it off.

---------------------------------------------------------------------------------------------------------------------------

## 4. E2 · Sticks bounce instead of passing through

**Why.** A held stick is rigid in the hand. A stroke at 3 m/s moves the tip 3.3 cm per 90 Hz frame, and with nothing to
stop it the hand keeps going (the overshoot test dips 6 cm). So the drawn stick spears the head. Real sticks never do; they
rebound. The hit test keeps the raw tip and shoulder pointers (spec 05 §3), so the feel and timing do not change.

**Algorithm** (reference `stickVisualStep`): the drawn angle about the grip is `φ = clamp(φC + ψ, 0, φMax) · w`.
- `φC` = the clamp angle that lifts the raw tip ball onto the surface under it (`liftAngle` with `lift = TipRadius − h`, `h`
  = height of the tip-ball centre over the plane). The surface comes from Drums' own pieces (heads, pad, cymbal plates at their E7 tilt).
- `ψ`, `ω` = bounce spring: `ω += (−ωn²ψ − 2ζωnω)·h; ψ += ω·h` in substeps h ≤ 2 ms. **After** the frame of a hit by this
  stick (`Drums.Hit` with this stick's pointer), `ω = max(ω, (1 + e)·v/L)`, where `v` is the approach speed the hit test
  measured (`vy`, spec 05 §3 step 6). The hit frame itself draws the stick on the head.
- `w = 1 − clamp01((φC − 0.30)/(0.45 − 0.30))`: very deep raw dips fade the correction out instead of twisting the stick 26° in the hand.
- Rendering: the stick mesh hangs under a child transform `Visual` of the stick root. Rotate only that child,
  `Quaternion.AngleAxis(φ·Rad2Deg, normalize((tip − grip) × n))`, about the grip point (13 cm from the butt). `Root` and the
  pointers keep the raw pose (`DrumMath.StickPose`), so nothing in the logic reads the visual. Run in Drums'
  `Tick(StickHover)` (ARCH §4 step 7), after the hit test, so contact this frame is known.

| Constant | Value | Source |
|---|---|---|
| `L` | 0.27 m | `StickSpec.Length − GripFromButt` (0.40 − 0.13) |
| `TipRadius` | 0.0062 m | `StickSpec.TipRadius` |
| `e` (in-hand restitution) | 0.25 | design: gives a 1.05 cm tip bounce per m/s of approach (peak 21 ms after the hit, gone in ≈ 80 ms); a free stick would bounce several times more, but a held stick is damped by the grip |
| `ωn`, `ζ` | 2π·8 rad/s, 0.8 | design: the grip spring; the return must finish before the next 16th note at 180 bpm (83 ms) |
| `φMax` | 0.45 rad | design |
| fade | 0.30 → 0.45 rad of `φC` | design (17° to 26° in the hand) |

**State.** `struct StickVisual { float Psi, Omega; }` × 2.

**Test vectors** (`stickVisual`; grip fixed at (0, 0.12, −0.27), head plane y = 0, the raw stick turned about the grip so the
tip follows the given height; dt = 1/90; tolerance 1e-4):
- Overshoot stroke (down at 2.5 m/s to −0.06, hold 30 ms, up at 1.5 m/s). Frame: raw tip y → drawn tip y:
  f3 0.016667 → 0.016667; **f4 −0.011111 (hit, v 2.5) → 0.0062**; f5 −0.038889 → **0.024085**; f6 −0.06 → **0.027165**;
  f7 → 0.023856; f8 → 0.018847; f9 −0.051 → 0.014276; f10 → 0.010842; f12 −0.001 → 0.007203; f13 0.015667 → 0.015947; f14 0.032333 → 0.032333.
- Rebound stroke (down at 2.5 m/s to −0.01, then up at 2 m/s): f4 −0.009111 (hit 2.32) → 0.0062; f5 0.013111 → **0.029882**;
  f6 0.035333 → 0.055576; f7 0.057556 → 0.074986; f9 0.1 → 0.108189; f13 0.1 → 0.100281; f14 0.1 → 0.1.
- PlayMode: a scripted stick stroke through the snare plays exactly one `Drum(Snare)` (unchanged), and the stick's `Visual`
  child tip never goes below `TipRadius` over the head while `φC < 0.30`.

**Device check.** Slow-motion phone video through the lens (240 fps) of a snare roll: no frame shows the stick inside the
head, and each hit shows a small rebound. Then hit "through" deliberately: the stick turns in the hand and then lets go
without a pop.

---------------------------------------------------------------------------------------------------------------------------

## 5. E3 · Soft contact shadows

**Why.** In VR it is hard to see how far a fingertip is above a key, and the user's early complaints were keys firing on
touch and fast runs failing. A real lamp-lit room gives a strong cue: a soft shadow that darkens and tightens as the finger
comes down. This is realistic, functional, and costs one draw.

**Model** (reference `sphereShadow`, `shadowQuadHalf`). Treat each occluder as a sphere of radius `r` whose centre is at
height `h` over the receiving plane. The cosine-weighted ambient occlusion it casts at horizontal distance `ρ` is
`AO = cos α · (r/d)² = r² h / (h² + ρ²)^1.5` (the analytic sphere occlusion of Quilez, "Sphere ambient occlusion"). The
drawn darkness is `D = min(MaxDark, A · AO)` with `h_eff = max(h, 0.8 r)`. Its half-width is `0.766 h`, so it tightens as
the finger lands; at contact (`h = r`) the peak is `A`.
- Blobs: per finger, the tip sphere (r = tip radius) and the distal-joint sphere (r = distal radius), each against the plane
  the tip query returned. Per stick, the tip ball (r 0.0062). At most 22 used (pool of 32); skip `h > HMax`.
- Quad: centred at `Point + Normal·0.0005`, spanned by `Tangent` and `Normal × Tangent` (or any orthonormal pair when
  `Tangent` is zero), half-size `max(2.5 h_eff, 1.5 r)` ≤ 0.08 m. On a pressed key, clip along `Tangent` to `ClipHalf` (section 2.1).
- Shader `MusicRoom/ContactShadow` (Core, template `MaterialTemplates/ContactShadow`): URP `UniversalForward`, stereo-instancing
  macros, Queue `Transparent-20` (2980: after opaque, before additive glows), `Blend DstColor Zero`, `ZWrite Off`,
  `ZTest LEqual`, `Offset -1,-1`. Vertex data: `uv0` = (ρx, ρy) in metres from the blob centre, `uv1` = (h_eff, r, A, clipHalf or 1e3).
  Fragment: `if (abs(uv0.x) > clipHalf) discard; d = min(maxDark, A*r*r*h/pow(h*h + dot(uv0,uv0), 1.5)); return 1 - d;`.
  Precision: `float`.
- One dynamic `Mesh` (`MeshUpdateFlags.DontRecalculateBounds`, fixed bounds 10 m), `SetVertexBufferData` from a
  `NativeArray`; the renderer is off when no blob is active. **No `MaterialPropertyBlock`** (ARCH §14).
- The hover glow stays. Its strength drops to `HoverGlowGainWithShadow = 0.5` while a shadow is shown, so the realistic cue leads.

| Constant | Value | Source |
|---|---|---|
| `A` (VR / MR) | 0.55 / 0.35 | design: the diffuse share of the light in the dim, warm VR room; MR matches the shadow catcher α .32 (ARCH §12) |
| `MaxDark` | 0.6 | design |
| `HMax` | 0.06 m | where peak darkness has fallen to 1.6 % of A |
| quad factor / min / max | 2.5 h, 1.5 r, 0.08 m | at 2.5 h the AO is 5 % of its peak |
| `h_eff` floor | 0.8 r | avoids the singularity when tracking puts the centre at the plane |
| pool | 32 blobs (128 vertices) | 10 tips + 10 distal joints + 2 stick tips |

**Test vectors** (`contactShadow`; r 0.0076, A 0.55; tolerance 1e-5):

| ρ (m) | h (m) | darkness | quad half-size (m) |
|---|---|---|---|
| 0 | 0.0076 | **0.55** | 0.019 |
| 0 | 0.01 | **0.31768** | 0.025 |
| 0.005 | 0.01 | **0.227313** | 0.025 |
| 0 | 0.02 | **0.07942** | 0.05 |
| 0.01 | 0.02 | 0.056828 | 0.05 |
| 0 | 0.04 | 0.019855 | 0.08 |
| 0 | 0.06 | 0.008824 | 0.08 |

Half-width at h = 0.02: **0.015328 m**. PlayMode: `DrawBudgetTests` shows +1 draw with two hands over the keys and 0 with
the hands away; `Is.Not.AllocatingGCMemory()` around the pool update.

**Device check.** With the room lamp only, lower one finger slowly onto C4. A faint wide shadow appears at about 4 cm and
becomes a small dark spot at touch. It sits on the pressed key only, never floating above it. GPU check with the OVR Metrics
Tool overlay (or `adb shell ovrgpuprofiler -m`): app GPU time changes by less than 0.1 ms between hands over the keys and hands in the lap.

---------------------------------------------------------------------------------------------------------------------------

## 6. E4 · Fast Motion Mode only while drumming

**Decision.** Change the manifest to `OVRProjectConfig.HandTrackingFrequency.LOW`; ARCH §0 says HIGH, so this is a contract
change in `QuestSetup.ConfigureAll`. Then request FMM at runtime with `OVRPlugin.RequestFastMotionMode(true)` only while the
player is drumming. Meta warns that FMM "may increase jitter, impacting high accuracy interactions like direct touch". That
describes the piano and the harp, which are 2 of our 3 instruments. Drum strokes are exactly the "fast actions" FMM exists
for: at the default ≈ 30 Hz camera rate a 3 m/s stick moves 10 cm between samples, so prediction has to invent the bottom of every stroke.

**Policy** (reference `fmmFrame`; Core `FastMotionPolicy`, plain C#, ticked in step 18 `EndFrame`; the native call is
`MetaHandSource.RequestFastMotion(bool)`, only while `Rig.IsXrActive`):
- `want = !blocked && (Single mode with Drums current || IDrumsApi.AnyStickHeld || now − lastDrumHit < 4 s)`. `lastDrumHit`
  comes from `RecorderEvent` with type `drum`, `drumChoke` or `hat` and `DuringPlayback == false`.
- Turn on at once when `want` becomes true; never delay the first stroke.
- Turn off after `want` has been false for 5 s, at most one request per second.
- Low-light guard: while on (after 1 s), accumulate hand-seconds tracked vs hand-seconds `!isHighConfidence` in 3
  hand-second blocks. If a block is > 25 % low confidence, set `blocked` for the session, request off, and log
  `[Hands] FMM blocked: low confidence 0.xx (dim room?)`. Meta: FMM uses a short exposure and degrades in low light.
- Keep our own boolean. There is no getter, and the system may ignore the hint.

| Constant | Value | Source |
|---|---|---|
| recent-hit window | 4 s | design: about two bars at 60 bpm |
| off after | 5 s | design: hysteresis so moving between piano and drums in All mode does not flap |
| min gap between requests | 1 s (off only) | design |
| low-confidence block / threshold / grace | 3 hand-s / 25 % / 1 s | design; device-tune |

**Test vectors** (`fmmPolicy`; 90 Hz frames; drum hits at 2, 2.5, 3, 12 s; stick held 15–20 s; from 22 s one of two hands at
low confidence): requests **on at 2.000**, **off at 11.989**, **on at 12.000**, **off at 23.578 (blocked)**; `blocked = true`.
Tolerance ±1 frame.

**Device check.**
1. `adb logcat -e FMM` while drumming shows `FMM: enabled: true; active: true`, and after 9 s at the piano `active: false`.
   `adb logcat -e "Camera FPS"` shows ≈ 60 vs ≈ 30. Our `[Hands] sampleInterval` log shows ≈ 16.7 vs ≈ 33 ms.
2. A/B without building: Developer settings › *Hand Tracking Frequency Override* = High, then Low, then None. Play the same
   fast 16ths on the snare and a slow scale on the piano. Expected: fewer missed or late fast strokes with FMM; steadier
   piano hover with Low.
3. **Fallback.** If logcat shows `enabled: false` while drumming with the LOW manifest (the runtime ignores the hint without
   the manifest flag), set the manifest back to HIGH. Keep the policy anyway: requesting false at the piano is then a harmless hint.

---------------------------------------------------------------------------------------------------------------------------

## 7. E5 · 90 Hz with a quality governor

**Why 90 Hz.** The frame budget is 11.1 ms instead of 13.9 ms. The visual frame time drops by 2.8 ms, and the mean wait
between a strike and its detection drops from 6.9 to 5.6 ms (ARCH §17 latency chain). Hands extrapolated at 90 Hz look
smoother, which matters most at the 30 Hz tracking rate the piano now uses (E4). The judging bar is 60 fps; holding 90 is
a margin, not a gamble, because the governor below falls back automatically. **120 Hz is rejected**: 8.3 ms with five
per-pixel lights and MSAA 4 is not realistic, and hand tracking itself is ≤ 60 Hz.

**Quality ladder** (Hands `RigBootstrap` + plain C# `RefreshGovernor`; reference `governorFrame`):

| Rung | Refresh | SRP foveation level (`XRDisplaySubsystem.foveatedRenderingLevel`) |
|---|---|---|
| 0 (start) | 90 Hz if `displayFrequenciesAvailable` has it, else rung 2 | 0.5 |
| 1 | 90 Hz | 1.0 |
| 2 | 72 Hz | 0.5 |
| 3 | 72 Hz | 1.0 |

- Every frame: `gpuMs = OVRPlugin.GetPerfMetricsFloat(PerfMetrics.App_GpuTime_Float)`. Units are UNVERIFIED: treat values
  < 0.5 as seconds and multiply by 1000. If it returns null, use `FrameTimingManager.CaptureFrameTimings()` +
  `GetLatestTimings(1, buf)[0].gpuFrameTime`, with `PlayerSettings.enableFrameTimingStats = true` in `QuestSetup`.
  `dropped` = delta of `GetPerfMetricsInt(Compositor_DroppedFrameCount_Int)`; ignore negative deltas.
- 64-bin histogram (0–20 ms, 0.3125 ms bins), 2 s windows; ignore the first 10 s and 3 s after MR enter/exit or a recentre.
  A window is **bad** if p95 > 0.90 × (1000/Hz) or dropped > 1 % of its frames. Two bad windows in a row → next rung,
  logged as `[RIG] quality rung N: 90 Hz fov 0.5 -> 90 Hz fov 1.0 (gpu p95 10.6 ms, dropped 0)`.
- Never step back up within a session, so the player never feels quality bouncing. After `OnApplicationPause(false)` and
  10 s, retry rung 0 once.
- `OVRManager.DisplayRefreshRateChanged(from, to)`: if the system dropped to 72 Hz (thermal), go to the matching 72 Hz rung and stay.
- Dynamic resolution stays as built (fast, fine-grained, owned by the runtime); the governor only acts when that is not
  enough. Do **not** also enable Unity OpenXR's *Automatic Viewport Dynamic Resolution* feature (oxr
  `Documentation~/features/automaticdynamicresolution.md`): two controllers would fight.
- Dynamic-resolution cap: the eye buffer is allocated at the maximum scale. At 1.6 that is 2688 × 2816 per eye: 3 colour
  images × 2 eyes × 30.3 MB = 182 MB, plus a Depth16 swapchain ≈ 91 MB, against ≈ 106 MB at 1.0. Log
  `XRSettings.renderViewportScale` once per second in dev builds. If its 95th percentile never exceeds 1.3, set
  `quest3MaxDynamicResolutionScale = 1.3` (saves ≈ 100 MB).
- `OVRManager.instance.sharpenType = OVRPlugin.LayerSharpenType.Automatic`. Verify by eye on the key letters; ARCH §19 #7
  says it is UNVERIFIED under OpenXR. If it makes no visible difference, leave `None`.
- `suggestedCpuPerfLevel`/`GpuPerfLevel` stay `SustainedHigh`.

**Test vectors** (`refreshGovernor`, 20 s at the current Hz): steady 8 ms → stays 90, no bad window; 9.5 + U[0,1) ms
(`mulberry32(7)`) → bad windows at **12.011 and 14.022 s**, switch at **14.022 s** (p95 bin 10.625 ms); 8 ms with
Bernoulli(3/90) drops from 14 s → bad at **16.033** (7 dropped) and **18.044** (2), switch at **18.044 s**. In the C# implementation the
"switch" is one rung down; the vectors test the two-window rule and the thresholds.

**Device check.** Log lines `[RIG] refresh 90 Hz (available: 72/80/90/...)` and `[RIG] quality rung`. Run a 10-minute session
(all instruments, snow, fire, menu, a lesson) with `adb logcat -s VrApi`. The per-second `FPS=90/90` lines must hold;
note GPU% and the rung reached. Repeat in MR. Record the final rung per mode in ARCH §19. Thermal: after 20 minutes the
headset must not have forced 72 Hz; if it did, start at rung 2 permanently.

---------------------------------------------------------------------------------------------------------------------------

## 8. E6 · Hands that belong in the room

### 8.1 VR: lit "skin" hands (`MusicRoom/HandSkin`, Hands folder; template `MaterialTemplates/HandSkin` from Core's provider)
Today's `HandGhost` is unlit (a fixed colour plus rim), so in the dim, warm room the hands look pasted on. Keep the runtime
`OVRMesh` hand; it is the user's own hand size and ships no asset. Keep the depth prepass and the alpha fade. Add URP
lighting so the lamp and the flickering fire light the hands:
`c = albedo · (SampleSH(N) + Σ_lights L.color · atten · saturate((N·L + w)/(1 + w))) + rim`, with `w` = 0.5 wrap for
soft skin, albedo = today's `_Color` (0.86, 0.78, 0.70), rim = today's `_RimColor` × 0.35 at `_RimPower` 2.5, and alpha =
`_Alpha · 0.92`. It includes `Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl`, uses
`#pragma multi_compile _ _ADDITIONAL_LIGHTS` and the stereo macros, and loops `GetAdditionalLightsCount()` (≤ 4, ARCH §14).
The fade keeps the existing `_Alpha` property block (ARCH §14 allows it on this custom shader). Cost ≈ 0.1 ms (hand pixels
only). `MetaHandVisual.look = Skin` in VR; `Ghost` stays as an option.

### 8.2 MR: your real hands over the virtual instruments (`MusicRoom/HandPassthrough`)
In "Play in my room" the virtual piano is drawn over the passthrough, so it hides the player's real fingers, and the ghost
hand replaces them. The fix: draw the tracked hand mesh as a **hole** in the eye buffer. Where the hand is in front of
virtual content, the compositor shows the real hand (passthrough underlay, clear (0,0,0,0), ARCH §12). Where a virtual key
is in front of the finger (a pressed finger under a neighbouring key), depth keeps the key.
- Shader: Queue 3999 (last), pass 1 `ZWrite On, ColorMask 0`; pass 2 `ZWrite Off, ZTest LEqual, Blend Zero OneMinusSrcAlpha,
  Zero OneMinusSrcAlpha`, which outputs `(0, 0, 0, a)` with `a = _Alpha · smoothstep(0.05, 0.35, saturate(dot(N, V)))`. That
  scales the eye buffer's premultiplied colour and alpha by `1 − a`, revealing passthrough, with a soft edge at the silhouette.
  The vertex shader pushes 1.5 mm along the normal (`_Dilate` 0.0015) to cover small tracking mismatch.
- **Time alignment (why this needs Unity).** The passthrough image shows the world ≈ 39 ms late (Road to VR), but the
  tracked hand is predicted for display time. Posed normally, the hole would run ahead of the real hand during motion. So in
  `MetaHandVisual.Update`, while `look == PassthroughCutout`, read
  `OVRPlugin.GetHandStateAtTime(frame.displayTime − cutoutLatency, hand, ref _delayed)` and feed `_delayed` (root pose,
  rotations, translations) to `GetSkeletonPoseData()`. If the call fails, use the current state. The play logic still uses
  the predicted pose, so the sound is not delayed.
- In MR, `ContactVisuals` skips the clamp (E1), because the hole must match the real hand, not a corrected one. Shadows stay (A = 0.35).
- Switch on `MixedRealityEvent`: material and `look` together.

| Constant | Value | Source |
|---|---|---|
| `cutoutLatency` | 0.039 s, device-tune 0.02–0.06 | Quest 3 passthrough latency ≈ 39 ms (Road to VR); UNVERIFIED on 3S; the time base of `RequestedTimeStamp` vs `GetHandStateAtTime` is UNVERIFIED (log both once) |
| `_Dilate` | 0.0015 m | design |
| edge smoothstep | 0.05 → 0.35 of N·V | design |
| wrap `w` / opacity | 0.5 / 0.92 | design (wrap lighting is the usual cheap skin approximation) |

**Tests.** EditMode: the template exists with Queue 3999 and the blend state above (MaterialTemplates test pattern).
PlayMode with `SimulateHeadset` + MR JSON room: `MixedRealityEvent{Active=true}` switches `look` and disables the clamp.

**Device check.** MR, piano on a real table. Wave a hand quickly over the keys: no "window" leads the hand. If one does,
raise `cutoutLatency` in 5 ms steps; if a band trails, lower it. Press a key: the real finger is visible on top of the
virtual key, and the key still moves. VR: the hands warm and darken when the fire is switched on and off.

---------------------------------------------------------------------------------------------------------------------------

## 9. E7 · Drum kit motion

### 9.1 Cymbal rocking (replaces `Wobble` in `DrumKit`; reference `cymbalStrike`, `cymbalStep`)
Today the wobble is a 4.5 Hz Lissajous of at most 4° that starts the same way whatever the strike point. A real cymbal
tips **down where it is struck**, rocks slowly on its felt, and its plane of rocking swirls.
- State per cymbal: `struct CymbalTilt { float Tx, Tz, Wx, Wz; }`. The plate (the top hi-hat cymbal) gets
  `localRotation = rest * Quaternion.Euler(Tx·Rad2Deg, 0, Tz·Rad2Deg)`. In Unity `+Tx` sends local +z down and `+Tz` lifts +x.
- Strike at plate-local (x, z) (the crossing point C of the hit test) with the hit velocity `vel` (0–1):
  `k = maxTilt · 2πf0 · vel / R; Wx += k·z; Wz −= k·x`.
- Motion: `θ'' = −ω0² θ − 2ζω0 θ' + 2Ω J(θ')`, `J(a, b) = (−b, a)`, `ω0 = 2πf0`, `Ω = 2π·swirl`; semi-implicit Euler with substeps ≤ 4 ms.
- Choke (hand on cymbal, spec 05 §3): multiply state by `exp(−dt/0.03)` every frame while choked. Cap |θ| at 1.5 × maxTilt.
- Hi-hat: closed → maxTilt 0 (the clutch clamps it); open → the `hatOpen` row.

| Cymbal | f0 (Hz) | ζ | swirl (Hz) | maxTilt | R (m) |
|---|---|---|---|---|---|
| crash | 1.6 | 0.115 | 0.35 | 8° | 0.21 (`DrumSpec`) |
| ride | 1.2 | 0.10 | 0.25 | 4° | 0.22 |
| hat (open) | 2.5 | 0.20 | 0.5 | 3° | 0.17 |

Sources: gravity alone on a thin disc gives `ω0² = m g h / (m R²/4 + m h²)`. With m ≈ 1.0 kg (a typical published 16″
crash weight), R 0.21 m and bell height h 0.016 m (`DrumSpec.CymbalProfile`), that is 0.61 Hz. The felt washers stiffen it.
f0 = 1.6 Hz is a design value for the brisk rocking of a struck crash on a stand. The ride is heavier (≈ 2.4 kg for a
typical 20″), so it rocks slower and less. ζ is set so a crash settles in ≈ 2–3 s, close to its sound's decay. All are
design values; verify by eye on device.

**Test vectors** (`cymbalTilt`; full-velocity hit on the +z edge; frames of exactly 1/90 s; tolerance 0.02°):
- crash: peak **6.5979° at 0.1444 s**; (frame, tx°, tz°): (5, 3.9213, 0.5095), (9, 5.8110, 1.3141), (14, 6.1816, 2.1333),
  (23, 2.6112, 1.3042), (45, −2.0150, −3.5268), (90, 1.3953, −1.3034), (180, −0.2733, −0.8156), (270, −0.2191, 0.0598).
- ride: peak **3.3778° at 0.1889 s**; (9, 2.4929, 0.4030), (45, −1.1404, −1.3162), (90, −0.0381, 1.8436).
- hat open: peak **2.1990° at 0.0889 s**; (9, 2.0578, 0.6332), (90, 0.0135, 0.0731).
- sign: a hit on the +x edge at vel 0.5 gives tz **−0.4382°** after one frame (the struck edge goes down).

### 9.2 Head "give" (replaces the `FlashScale` pulse; reference `headDimple`)
The `FlashScale` pulse scales the whole drum by 1 + 0.015·flash, which looks synthetic. A real head dents under the stick for 2–5 ms and
then rings at 100 Hz or more, which the eye blurs. Set `FlashScale = 0`. Batter heads get a ringed disc mesh (6 rings × 48
segments = 289 vertices; one more `Mesh` per drum, still one draw each). For ≤ 3 frames after a hit, rewrite the vertices:
`w(d,t) = 0.004 · vel · exp(−d²/(2(0.25R)²)) · exp(−t/0.018)` along −normal, with analytic normals
`n ∝ (−∂w/∂x, 1, −∂w/∂z)`. The 4 mm peak is the order of a point-loaded membrane's static deflection,
`w ≈ F/(2πT)·ln(R/a)`: F ≈ 300 N forte, T ≈ 3 kN/m head tension, R/a ≈ 25 gives ≈ 5 mm (membrane statics, Fletcher & Rossing
ch. 3). Shown for one or two frames only. Vectors (`headDimple`, R 0.18, vel 1): (d 0, t 0) **0.004**; (0, 0.011) **0.002171**;
(0.045, 0) **0.002426**; (0.09, 0) 0.000541; (0, 0.022) 0.001178 m.

### 9.3 Kick beater (new, decorative)
The nod kick has no foot, so seeing the beater hit makes the nod read as a kick. The beater axle is kit-local
(0.12, 0.08, 0.62) (above `PedalPos`), with a shaft of 0.19 m and a felt ball of radius 0.025 m. Its angle θ is measured
from vertical toward +z: rest −30°, contact +16.8° (the ball touches the batter head at z ≈ 0.70, y ≈ 0.26). On every kick
(nod or pad, the frame the sound fires): θ = contact. Then the return is critically damped,
`θ(t) = θr + (θc − θr)(1 + t/τ)e^{−t/τ}`, τ = 0.035 s (90 % back in ≈ 0.13 s). The footboard pitch follows,
`14.3° − 10°·(θ − θr)/(θc − θr)`. It adds 2 draws (shaft + ball merged by material) within the drum budget of 25.

**Device check.** Crash hits at the near edge and at the far edge tip the cymbal toward the struck side and rock slowly.
Hard hits rock visibly more than soft ones. No drum "breathes" in size. The beater strikes on every nod.

---------------------------------------------------------------------------------------------------------------------------

## 10. E8 · Harp strings blur like real strings

**Why.** A plucked harp string vibrates at 131–1047 Hz (strings C3–C6, `HarpGeometry.MidiOf`), far above the 90 Hz display.
The eye integrates it into a blurred spindle. Today's code flips the string to the other side every frame (`phase += .86π`
per frame), which strobes as a 45 Hz shimmer. The physically right image is the **envelope** of the motion: draw it.

**Model** (reference `pluckCoeffs`, `harpTau`, `harpTables`, `harpEnvelope`, `blurAlpha`):
- Modal sum of an ideal plucked string (triangle of height h at pluck position p = the stroke's `AmpU`):
  `y(u, φ) = Σ_{n=1..8} c_n cos(nφ) sin(nπu)`, with `b_n = 2h sin(nπp)/(n²π² p(1−p))` and `c_n = b_n e^{−t/τ_n}`
  (Fletcher & Rossing ch. 2).
- Envelope per segment ring j (17 rings, `HarpGeometry.Segments` 16): `up_j = max_k y(u_j, φ_k)`, `lo_j = min_k y(u_j, φ_k)`
  over 24 phase samples. Constant tables `S[j][n] = sin(nπ j/16)`, `C[k][n] = cos(2πnk/24)` are built once.
  Check: at t = 0 with 64 modes this reproduces the d'Alembert parallelogram `(0,0) (p,h) (1,0) (1−p,−h)` to 5 µm on 3 mm.
  So the upper envelope peaks at p and the lower one at 1 − p, and the blur is asymmetric like a real string's.
- **Decays follow the CURRENT harp sound exactly.** The 06 reference `karplusOld` loop gives each partial
  `τ_n = −1 / (f0 · ln(g · |cos(w/2)| · sqrt(1 − 2·frac(1−frac)(1 − cos w))))` with `w = 2πn f0/sr`,
  `g = 0.001^{1/(seconds·f0)}`, `seconds = clamp(4.5 − (midi−48)·0.08, 0.7, 4.5)`, `D = sr/f0 − 0.5`, `frac = D − ⌊D⌋`.
  Measured from the renders (`harp-partials.json`), this matches to < 1 % for partials 1–4 on C3, G4 and C6. Example: G4 τ1
  measured 0.4045 s vs formula 0.4046 s; C6 0.111 vs 0.1111. If the sound key ships a new harp, the Audio module exposes
  `IAudioEngine.HarpPartialTau(int midi, int n)` (contract request) and the blur reads that instead.
- Drawing: the one strings mesh (ARCH §14, still 1 draw) gets three copies of each string: a core at rest + bend, and two
  edge copies offset by `up_j` and `lo_j` along the swing direction (`AmpD·N + AmpZ·ẑ`, spec 05 §5.6). Vertex alpha:
  core `1 − 0.65 b`, edges `0.45 b`, with `b = clamp01((up − lo)/(4 · stringRadius))`. A sinusoidally moving string spends
  most of its time near its extremes, so the blur's edges are denser than its middle. The strings shader becomes alpha-blended:
  Transparent queue, `ZWrite Off` for the edge copies, core first.
- Keep the finger-held bend (spec 05 §5.6). Drop the pluck flash to 0.25 of today's gain: the blur itself shows the pluck.
- Cost: 1.78 µs per ringing string in Node. Update the envelope **every other frame** for strings plucked more than 0.1 s
  ago, and skip strings whose `up − lo` is under 0.05 mm. Optional Burst `IJobFor` over strings; the data are already SoA.

| Constant | Value | Source |
|---|---|---|
| h | `VibBase + VibVel·vel` = 0.0012 + 0.0028·vel m | existing app constants (`HarpPlay.cs:110`). Real harp plucks release millimetre-scale displacements (Chadefaux et al., JASA 2012); order of magnitude only |
| modes / phases | 8 / 24 | b₈ is 1.6 % of b₁ |
| blur full at | 4 string radii of swing | design |
| core drop / edge alpha | 0.65 / 0.45 | design |

**Test vectors** (`harpBlur`; tolerance 1e-3 mm). τ table (s): midi 48: τ1 0.647430, τ2 0.635685, τ4 0.592672, τ8 0.466357;
midi 67: **0.404631, 0.341117, 0.209465, 0.082139**; midi 84: 0.111069, 0.042989, 0.012360, 0.003106. String G4, vel 0.7
(h 3.16 mm), p 0.35, rings j = 4, 6, 8, 10, 12:
- t = 0: up **2.30009, 3.005582, 2.451156, 1.834295, 1.238179** mm; lo = the mirror (−1.238179 … −2.30009); mid alpha core 0.430982, edge 0.393936.
- t = 0.5 s: up 0.643232, 0.786795, 0.732514, 0.569472, 0.381288 mm; mid alpha core 0.829952, edge 0.117725.
- Closed-form check: max error **5e-6 m** (64 modes, 256 phases).

**Device check.** Pluck a low C and a high C. Each becomes a soft spindle that is widest near where it was plucked. The low
string keeps its kinked shape longer; the high string narrows within half a second. No string shimmers or flickers.
Glissando across all 22 strings: `[Harp] blur` marker ≤ 0.1 ms.

---------------------------------------------------------------------------------------------------------------------------

## 11. E9 · MR piano remembered with a spatial anchor (stretch)

**Value.** The "Reason to Come Back" award criterion: the piano returns to the same real table. **Cost**: 0 ms per frame.
It needs only `USE_ANCHOR_API`, which is already in the merged manifest, stores locally, and needs no internet (the app has
no INTERNET permission).
- **Save.** When the piano lands by the user's pinch (`MrModule.PlacePiano`) or is released after an MR grab: create a
  GameObject at the piano pose, `AddComponent<OVRSpatialAnchor>()`, `await WhenLocalizedAsync()`, `await SaveAnchorAsync()`.
  On success, store `musicroom.mrPianoAnchor` (Guid "N"), `musicroom.mrPianoOnTable` (0/1) and `musicroom.anchorFailures = 0`,
  then `EraseAnchorAsync()` the previous anchor. Debounce: at most one save per placement.
- **Restore** in MR auto-arrange, **before** `TryPlaceOnBestTable`:
  `LoadUnboundAnchorsAsync(new[] { uuid }, _list)` → `LocalizeAsync(3.0)` → `TryGetPose(out pose)` → validate
  (reference `anchorPoseOk`: horizontal distance to the head ≤ 3 m, `up.y ≥ 0.98`, −0.1 ≤ y ≤ 1.6) → `BindTo(new
  OVRSpatialAnchor)`. Then pose the piano, call `SetOnSurface(onTable)` and `ILayoutService.PlaceAroundPiano()`. While MR
  is active, copy the anchor pose to the piano when it moves by more than 2 mm (relocalization).
- **Failure.** Any step fails or times out → the normal table flow, `anchorFailures++`. After 3 failures, erase the anchor and
  clear the key. The same mechanism applies to the manual snow window (`musicroom.mrWindowAnchor` + `musicroom.mrWindowSize` "w,h").
- Vectors (`anchorPose`): (0.3, 0.74, 0.6) up (0,1,0), head (0,1.2,0) → **ok**; z 3.5 → not ok (too far); up (0.26, 0.965, 0) → not ok (tilted); y 1.9 → not ok.
- Device check: place the piano, quit the app, relaunch, choose "Play in my room". The piano appears on the same spot within
  3 s and the auto-placement hint is skipped. Then move the table's position in Space Setup: the old anchor still validates,
  so the piano stays where it was placed (by design).

---------------------------------------------------------------------------------------------------------------------------

## 12. Smaller items

- **E10 PSO warm-up.** Vulkan pipeline creation on first use (fire on, MR enter, menu open, first snow) costs a visible hitch.
  Record once on device in a dev build: `var gsc = new GraphicsStateCollection(); gsc.BeginTrace();`, play every feature,
  `gsc.EndTrace(); gsc.SendToEditor("MusicRoom")` (or `SaveToFile` + `adb pull`). Ship the collection under
  `Resources/` and call `WarmUpProgressively` during Sunny's intro (≤ 1 ms per frame). The namespace is still
  `UnityEngine.Experimental.Rendering`. Verify: no `Compositor_DroppedFrameCount` increase on first fire/MR/menu.
- **E11 Box-projected probe.** `reflectionProbeBoxProjection = true` on `QuestURP.asset` (`render-setup`). Give the room
  probe `boxProjection = true` with box = the room (6.4 × 2.9 × 5.2 m, centre (0, 1.45, −1.48)). Keep probe blending off.
  Cost ≈ 0.1 ms. Check: the window and fire reflections on the cymbals and the black piano sit where they should.
- **E12 MVRR + symmetric projection.** `MetaQuestFeature` `m_symmetricProjection = 1`, `optimizeMultiviewRenderRegions = 1`
  with *Final Pass* mode (oxr `multiviewrenderregions.md`; dynamic resolution needs Final Pass). Measure the GPU delta
  (UNVERIFIED #18); keep only if ≥ 0.3 ms saved with no artifacts at the lens edges.
- **E13 Microgestures** (not before the deadline). The `.Microgesture` of `GetHandTrackingState`, gated on `!IsPlaying(hand, 1 s)`,
  no stick held, and the hand outside every play zone. A thumb swipe would be Next/Prev instrument in Single mode. Risk:
  false triggers when fingers curl over keys.
- **E14 Passthrough evening grade** (MR, optional). While the Fire switch is on in MR,
  `SetBrightnessContrastSaturation(−0.10, 0.05, −0.10)`; off → `DisableColorMap()`. A procedural warm LUT
  (`new OVRPassthroughColorLut(Color[16³], ColorChannels.Rgb)`) is the richer version. Ask the user before making it default.

---------------------------------------------------------------------------------------------------------------------------

## 13. Rejected, with reasons

| Candidate | Why not |
|---|---|
| Wide Motion Mode (`wideMotionModeHandPosesEnabled`, `SetWideMotionMode2HandPosesEnabled`) | Needs Body Tracking Support Required and the runtime `BODY_TRACKING` permission prompt (ARCH: no start-up prompts). Inferred poses are "inaccurate when tracking is lost", so they could never strike. Seated play keeps the hands in view. Value ≈ 0 |
| Application SpaceWarp (`OVRManager.SetSpaceWarp`) | Halves the app rate, so hand input and strike detection run at 45 Hz; motion-vector artifacts on thin strings and sticks. Already off (ARCH §0) |
| Realtime shadow map in VR | One 1024 spot shadow from the lamp ≈ 0.6–1.0 ms GPU plus a second caster pass, for a cue E3 gives at < 0.1 ms. The MR hard shadow stays (ARCH §12) |
| Depth API occlusion (`EnvironmentDepthManager`, Hard/Soft occlusion) | 1–2 ms GPU (UNVERIFIED) plus occlusion keywords in every shader; seated MR has almost no real occluders except the hands, which E6 solves exactly |
| `OVROverlay`/`OVROverlayCanvas` menu | Compositor layers ignore scene depth unless punched through, so the poking finger would be hidden behind the panel. The 1024 × 768 panel already gives ≈ 45 px/° at 0.8 m, above the display's PPD |
| 120 Hz | 8.3 ms budget; tracking ≤ 60 Hz; thermal |
| Strike look-ahead (`GetHandStateAtTime(now + audio latency)`) | Prediction overshoots on stops: phantom notes on fingers that stop short. Latency is better cut with `AudioModule.PreferredDspBuffer = 128` (ARCH §17) |
| Interaction SDK `HandVisual` / `SyntheticHand` | Needs the ISDK hand chain with `LastKnownGoodHand` (spec 03 §1.1); E1 does the poke-limiting ourselves |
| Late hand re-pose in `onBeforeRender` | The display time does not change within a frame, so the gain is limited to a newer tracker sample (≤ one 33 ms tracker period, rarely). Measure first (spec 02 §8) |
| Piano hammer and damper visuals | The piano is the slim digital model the user chose (memory, 2026-10-23); there is no lid and no visible action. Adding one would be unreal |
| `PassthroughCameraAccess` light estimation | Needs the headset camera permission prompt |

---------------------------------------------------------------------------------------------------------------------------

## 14. Comfort checklist (no new code unless marked)

1. Frame-rate stability is the first comfort feature: E5 never lets judder persist beyond two 2 s windows.
2. No world motion besides the existing 45° snap swivel and rig animations; no smooth turning (ARCH §15.5).
3. Tracking loss is visible as a fade, not a freeze (spec 02 §8). E1 releases smoothly, and E6 removes the cut-out when a hand is lost.
4. FMM low-light guard (E4) keeps tracking usable in dim rooms. Add Sunny's existing hint text "More light helps your hands"
   when `FastMotionPolicy.Blocked` is set (**new**, one guide line, text only).
5. Text: the menu panel at 0.8 m with ≥ 30 px TMP; `sharpenType` Automatic (E5).
6. Seated reach: the instruments stay within the seat frame (ARCH §8). Contact shadows (E3) reduce reaching "into" keys.
7. MR: boundary suppressed only while passthrough is on (already in `XrMode.SetPassthrough`).

---------------------------------------------------------------------------------------------------------------------------

## 15. Contract changes and where each piece lives

| Change | Owner (folder) | ARCH section affected |
|---|---|---|
| `IContactSurface`, `ContactHit`, `ContactUse`; `ContactVisuals` (step 8a) + `ContactShadowPool` + `MusicRoom/ContactShadow` shader + template | Core | §4 (new step 8a), §14 (template list, +1 draw) |
| `MetaHandVisual.SetContact/ClearContacts`, `Look`, `cutoutLatency`; shaders `MusicRoom/HandSkin`, `MusicRoom/HandPassthrough` (templates from Core's provider) | Hands (+ Core provider) | §6.1, §14 |
| `MetaHandSource.RequestFastMotion`; `FastMotionPolicy` (Core, step 18) | Hands + Core | §0 (frequency LOW), §19 #1 |
| `RefreshGovernor`, `QualityRung`, `RigBootstrap.PreferredRefreshHz = 90`, `QualityChanged` | Hands | §0, §6.3, §17 |
| `QuestSetup`: `handTrackingFrequency = LOW`, `PlayerSettings.enableFrameTimingStats = true`; optional E11/E12 flags | Editor | §1 `configure` |
| `IDrumsApi.StickShadowSpheres(Span<Vector4>)`; Drums implements `IContactSurface`; stick `Visual` child; `CymbalTilt`, head dimple mesh, kick beater | Drums | §8, §15.2 |
| Piano implements `IContactSurface` (key at current depth, `ClipHalf` rule) | Piano | §15.1 |
| Menu implements `IContactSurface` (panel face) | Menu | §15.6 |
| `StringBlur` (three copies in the strings mesh, alpha shader); optional `IAudioEngine.HarpPartialTau` | Harp (+ Audio) | §7.3, §15.3 |
| `PianoAnchorStore` (E9), cut-out switch on MR enter/exit | MR | §12, §18 (new settings keys) |

New settings keys (ARCH §18): `musicroom.mrPianoAnchor`, `musicroom.mrPianoOnTable`, `musicroom.mrWindowAnchor`,
`musicroom.mrWindowSize`, `musicroom.anchorFailures`.

## 16. Test plan summary

- **EditMode** (`Tests/EditMode/<Module>/`): `ContactMathTests` (E1 vectors), `StickVisualTests` (both frame tables),
  `CymbalTiltTests`, `HeadDimpleTests`, `HarpBlurTests` (τ table, closed-form check, G4 rows), `ContactShadowTests`,
  `RefreshGovernorTests` (three scenarios; C# `Mulberry32(7)` from Audio for the random one), `FastMotionPolicyTests`,
  `AnchorPoseTests`. All expected values come from `experience.vectors.json`, regenerated by
  the experience lab's `node make-vectors.mjs`.
- **PlayMode**: E1 logic-unchanged test, E2 stick stroke, E3 draw count +1 / 0, allocation tests around `ContactVisuals.Update`,
  stick visuals, cymbal and harp updates (`Is.Not.AllocatingGCMemory()`), MR look switch.
- **Device** (in order): E5 log and `VrApi FPS` → E4 logcat FMM → E1/E2 slow-motion through-the-lens video → E3 GPU delta →
  E7 by eye → E8 glissando marker → E6 MR wave test and `cutoutLatency` tuning → E9 relaunch test. Record the results in ARCH §19.

## 17. Sources

- Meta, *Fast Motion Mode* (Unity): https://developers.meta.com/horizon/documentation/unity/fast-motion-mode/
- Meta, *Wide Motion Mode* (Unity): https://developers.meta.com/horizon/documentation/unity/unity-wide-motion-mode/
- Meta, *Display refresh rate* (native): https://developers.meta.com/horizon/documentation/native/android/mobile-display-refresh-rate/
- Meta, *OS render scale* (Quest 3S default 1680 × 1760): https://developers.meta.com/horizon/documentation/unity/os-render-scale/
- Road to VR, Quest 3 vs Vision Pro hand-tracking latency (≈ 70 ms; passthrough ≈ 39 ms): https://www.roadtovr.com/apple-vision-pro-meta-quest-3-hand-tracking-latency-comparison/
- UploadVR, Hand Tracking 2.4 and Fast Motion Mode (default camera rate 30 Hz): https://uploadvr.com/quest-hand-tracking-2-4-improves-fast-motion-mode
- I. Quilez, *Sphere ambient occlusion* (analytic `cos α (r/d)²`): https://iquilezles.org/articles/sphereao/
- N. H. Fletcher, T. D. Rossing, *The Physics of Musical Instruments*, 2nd ed., Springer 1998: ch. 2 (plucked string, modal
  coefficients, parallelogram motion), ch. 3 (membranes).
- D. Chadefaux, J.-L. Le Carrou, B. Fabre et al., "Experimentally based description of harp plucking", J. Acoust. Soc. Am. 131(1), 2012 (order of magnitude of the release displacement only).
- Package sources cited inline (core 207.0.1, mruk 207.0.1, urp 17.3, oxr 1.16.1) and Unity 6000.3 `UnityEngine.CoreModule.xml`.
