# 05 · Instruments spec: instruments, input layer, play zones, grab guards

**Sources.** Package roots, under `Library/PackageCache/`:
- `XRH:` = `com.unity.xr.hands@3e241be20642/`
- `ISDK:` = `com.meta.xr.sdk.interaction@61177ebcfd9a/`
- `CORE:` = `com.meta.xr.sdk.core@4c2eac90db2d/`

**Instrument data.** The data sets named in this spec are authoritative: generated from the reference instrument model, with `*_unity` values and their source-frame twins. They hold the 88 keys, 22 strings, neck centreline, rig homes, drum pieces and stands, stick rest poses, construction data (`piano.build`, `drums.build`, `sticks_build`, `harp.build`) and sampled curves (`curves`); their values are compiled into `PianoSpec`, `DrumSpec` and `HarpGeometry` and checked by the EditMode tests. `[src …]` marks the same value in the right-handed source frame (−z forward) the reference model is authored in.

## 0. Conventions
- **Coordinate conversion.**
  - Positions: source (x, y, z) → Unity (x, y, −z). Quaternions: (x, y, z, w) → (−x, −y, z, w).
  - Euler: angles about x and y flip sign; about z they keep it. The source frame's XYZ order equals `Quaternion.Euler` when y = 0; otherwise use `AngleAxis(−a,right)*AngleAxis(−b,up)*AngleAxis(c,forward)`.
  - Heading: `yaw_src = atan2(−f.x, −f.z)` → `yaw_u = atan2(f.x, f.z) = −yaw_src`.
- **The mirror flips cross products and triangle winding.** Re-derived crosses are in §1.5 and §4. Build meshes natively, or reverse the index order.
- **Unchanged under the mirror:** anything using only local (x, y), `hypot(x, z)` or a consistently negated z (drum heads, harp plane, every radius test).
- **Piano `back`:** piano-local Unity z, the distance behind the white-key fronts (= −z_src).
- **Time.** `NowMs = Time.unscaledTimeAsDouble*1000`, sampled once per frame, is the app's millisecond clock. `dt = min(unscaledDeltaTime, .05)`. Only the harp schedules sound on the audio clock.
- **No colliders.** Hit tests are analytic in local frames via `InverseTransformPoint`, which includes scale (page turns scale rigs .85-1).

## 1. Input layer

### 1.1 Frame order; keep it
1. `Sticks.UpdatePoses(dt)`.
2. `Input.Update(dt)`.
3. For piano, drums, harp, deck, switch panel and menu (skipped while AR is placing or the rig is `grabbed`): `Tick(pts, dt)` then `Hover(pts, glow)`. The drums get **all** pointers, sticks included; the rest get hand pointers only.
4. Tutorial.
5. `Sticks.Hover`.
6. `HoverGlow.Update`.
7. `UpdateGrab()` (§8).
8. `Sticks.UpdateReleases`.
9. `NodKick.Tick`.
10. Everything else.
11. `Input.EndFrame()`, which clears the `PinchStart/End` and `GraspStart/End` flags.

### 1.2 Data model
```csharp
public interface IPointerOwner { IReadOnlyList<Pointer> Pointers { get; } double LastPlayMs { get; set; } }
public sealed class HandState : IPointerOwner {
  public int Index; public bool Connected; public Handedness Handedness;
  public bool Pinching, PinchStart, PinchEnd; public float PinchFor;      // s pinched
  public bool Grasping, GraspStart, GraspEnd; public Stick Holding;        // null = free
  public double LastPlayMs { get; set; } = double.NegativeInfinity;
  public List<Pointer> Pointers = new(5);   // [IndexTip, MiddleTip, RingTip, LittleTip, ThumbTip]
  IReadOnlyList<Pointer> IPointerOwner.Pointers => Pointers; }
public sealed class Pointer {
  public string Id; public IPointerOwner Owner; public XRHandJointID Joint;
  public Vector3 Tip, Prev, Next, Vel; public float Speed, SampleDt; public double SampleT;
  public bool Active, WasActive, External, Down, Stick, Shoulder;
  public double[] DrumLastHit = new double[9]; public sbyte[] DrumArm = new sbyte[9]; } // -1 = unset
```
- `Pointers[0]` (the index tip) is the pinch and grab pointer everywhere.
- Hand slots 0/1 aren't left/right; handedness comes from the tracked hand.
- Controller, mouse and keyboard pointers are out of scope; keep mouse and keyboard as Editor test input.

### 1.3 `Update(dt)`: speed over the learned sample interval
```csharp
foreach (var h in hands) { h.PinchFor = h.Pinching ? h.PinchFor + dt : 0; if (h.Connected) UpdateGrasp(h); }
foreach (var p in pointers) {
  p.WasActive = p.Active; p.Prev = p.Tip; p.Active = false;
  if (p.External) { if (!p.Down) continue; p.Tip = p.Next; p.Active = true; }
  else { var h = (HandState)p.Owner; if (!h.Connected || h.Holding != null) continue;   // holding = muted
         if (!TryJointWorld(h, p.Joint, out p.Tip)) continue; p.Active = true; }
  if (p.WasActive && (p.Tip - p.Prev).sqrMagnitude > 1e-10f) {                          // new sample
    double gap = Math.Max(nowS - p.SampleT, 0.004);
    if (gap < 0.2) p.SampleDt = p.SampleDt > 0 ? p.SampleDt + 0.25f*((float)gap - p.SampleDt) : (float)gap;
    p.SampleT = nowS; p.Vel = (p.Tip - p.Prev) / Mathf.Max(p.SampleDt > 0 ? p.SampleDt : dt, 1e-3f);
  } else { if (!p.WasActive) { p.SampleT = nowS; p.Prev = p.Tip; } p.Vel = Vector3.zero; }
  p.Speed = p.Vel.magnitude; }
```
- Instruments measure speeds over `sdt = max(SampleDt or dt, 1e-3)`.
- `ResetMotion()` deactivates every pointer. Call it after a snap turn, re-arrangement or rig teleport, so the jump never reads as a stroke.

### 1.4 Pinch, grasp, index tip, palm
- **Pinch.** Computed by the app from raw joints, before the instrument loop.
  - With `d = |thumbTip − indexTip|`: start when `!pinching && d ≤ .015`; end when `pinching && d > .025`.
  - One-frame edge flags; `Pinching` is not cleared on disconnect.
  - Use this rule. **Don't** use the runtime pinch (`OVRHand.GetFingerIsPinching`, CORE:Scripts/Util/OVRHand.cs:342; `IHand.GetFingerIsPinching`, ISDK:Runtime/Scripts/Input/Hands/IHand.cs:71): every guard was tuned against 15/25 mm.
- **Grasp**.
  - `g` = the mean wrist distance to the tracked middle/ring/little tips (null if the wrist or every tip is untracked).
  - `Grasping = Grasping ? g < .13 : g < .105`, with one-frame edge flags. An open hand reads about .18; a fist under .10.
- **`IndexTip(h, out p)`** returns the world index tip even while the hand holds a stick.
- **`Palm(h, out c, out n)`** (used by the menu and page turn).
  - `c = (wrist + indexProximal + littleProximal)/3`.
  - Source frame: `n = (ip−w)×(lp−w)`, negated for the **left** hand (palmar). Unity's `Cross` gives dorsal, so negate for the **right** hand.
  - Fail if `|n| < 1e-6` or a joint is untracked.

### 1.5 Joints and Unity sources

| joint | `XRHandJointID` (XRH:Runtime/Enums.cs:51-200) | ISDK `HandJointId` (ISDK:Runtime/OpenXR/OpenXRHandPrimitives.cs:218-379) | use |
|---|---|---|---|
| index tip | IndexTip | HandIndexTip | pointer 0, pinch, stick grip, hi-hat pinch |
| middle / ring / little tip | MiddleTip / RingTip / LittleTip | HandMiddleTip / HandRingTip / HandPinkyTip | pointers 1-3, grasp |
| thumb tip | ThumbTip | HandThumbTip | pointer 4, pinch, stick grip |
| wrist | Wrist | HandWristRoot | grasp, stick fwd/up, rig-turn yaw |
| index / little proximal phalanx | IndexProximal / LittleProximal | HandIndex1 / HandPinky1 | palm |

**Joint axes.** OpenXR joints point −Z toward the fingertip and +Y out of the back of the hand (right-handed). In Unity:
- `(0,0,−1)_src` → `rot*forward`
- `(0,1,0)_src` → `rot*up`
- `(1,0,0)_src` → `rot*right`

The ISDK constants agree (Distal = forward, Dorsal = up; OpenXRHandPrimitives.cs:57-112), and XR Hands converts OpenXR to Unity space (XRH:Documentation~/hand-data/xr-hand-data-model.md:19). **UNVERIFIED on device.**

**XR Hands calls.**
- `subsystem.leftHand/rightHand` (XRHandSubsystem.cs:74/97); `XRHand.isTracked` (XRHand.cs:52) → `Connected`; `GetJoint` (:30) + `TryGetPose` (XRHandJoint.cs:106) → joint visible; `handedness` (:45).
- Poses are XR-Origin-relative: world = `cameraOffset.TransformPoint(pos)` (the rig parents the hands to the camera offset).
- `updatedHands` fires Dynamic then BeforeRender (XRHandSubsystem.cs:216-249). Run logic once from the Dynamic update; use BeforeRender for visuals only.
- **UNVERIFIED** that Dynamic runs before `Update`: give the orchestrator a late `[DefaultExecutionOrder]`.

**ISDK alternative:** `IHand.GetJointPose` returns world poses (IHand.cs:104), valid when `IsTrackedDataValid` (:177).

## 2. Piano

### 2.1 Frame, constants, keys
- **Rig home:** Unity (0, .72, .45), yaw 0 [src (0, .72, −.45)].
- **Local frame:** x right; white tops at y 0; z = `back`; the stand reaches the floor at y −.72.

| constant | value |
|---|---|
| W / WW (pitch / visible width) | .0235 / .0221 |
| L / H (length front→hinge / thickness) | .15 / .02 |
| BW / BL / BTOP (black width / hinge→black front, so fronts at back .055 / top above white) | .0137 / .095 / .012 |
| TRAVEL; MAX_ANGLE | .01; white .01/L = .0667 rad, black .084 rad (8 mm) |
| LEVER_MIN | .06 |
| S_ESC / S_RESET / S_DAMPER (depth 0 rest .. 1 felt: fire / finger lets go / damper, 70 % up) | .9 / .5 / .3 |
| VEL_WINDOW / MIN_PRESS_SPEED / LOUD_SPEED | .03 s / .07 m/s / 1.5 m/s |
| FAST_PRESS_SPEED / EARLY_S / SPARSE_DT | .5 m/s / .55 / .025 s |
| START_INSET / HOLD_MARGIN | .0015 / .004 m |
| RISE_OMEGA / RISE_ZETA / PRESS_TIME | 54 rad/s / .85 / .045 s |
| DARKEN | white .16, black .3 |
| PHRASE_GAP / MAX_RING | .6 s / 6 s |
| HITCH, HITCH_IN, HITCH_OUT, FELT | .06, .06, .025, .94 |

**Keys**.
- MIDI 21-108 (A0-C8): 52 white, 36 black. `x0 = −52W/2 = −.611`.
- White key k: `x0 + (k+.5)W`. Black: `x0 + whitesBefore·W + off[pc]`, where `off = {1:−.0028, 3:+.0028, 6:−.004, 8:0, 10:+.004}`.
- Middle C sits at x −.05875. Table: data `piano.keys`.

**Key transform**.
- Pivot at the hinge `(x, 0, back .15)` [src (x, 0, −L)].
- `localRotation = Euler(−θ°, 0, 0)` with `θ = depth·MAX_ANGLE` [src rot.x = +θ]: the front goes down.
- Pivot-space meshes: white x ±WW/2, y −.02..0, z −.15..0, 2.2 mm rounding; black y −.004..0.012, z −.095..0, 2.5 mm rounding.

**Side shading** (grey vertex colour):
- `t = (y−ymin)/(ymax−ymin)`, `side = |n.x|`, `front = max(0, −n.z_u)`, `down = max(0, −n.y)`.
- `c = clamp(1 + (.55t−.55)·side + (.18t−.18)·front·(1−side) − .6·down, .3, 1)`.

**Tint and materials.**
- Per-key tint `1 − DARKEN·depth` multiplies the key and its label.
- Ivory #f4efe3 (roughness .3), ebony #0e0e10 (roughness .24).
- Standard and URP Lit ignore vertex colours: use a Shader Graph or bake.

### 2.2 Body, stand, labels (full data in `piano.build`)
**Body and stand.**
- **Case** (#131315, roughness .58): extruded with 6 mm rounding. Body x ±.6325: front face at back .012 below the keys, keybed y −.032, key well to back .153, top y .038 to back .29, bottom y −.092. End blocks x ±.6125..±.663: 4 mm proud of the keys, tops sweeping down to the front.
- **Top panel:** a plate with a power button and 4 buttons, in vertex greys. **Grille:** at back .256, 3 mm hole pitch. **Music rest:** acrylic #0a0a0c, leaning back 17.19° [src rotateX(−.3)].
- **Stand** (black wood): side panels on feet at x ±.5775, back panel, stretcher; floor at y −.72.
- **Solid extent:** top .038, front −.004, rear .29, bottom −.092, half-width .663.
- **On a real table:** `SetOnSurface(onTable)` hides the stand (`lowerParts`) and `ResetSurface()` restores it. `tableLift` = .092.

**Labels.**
- **Text**: C D E F G A B on white keys, with the octave digit on each C (none on A0, B0); C# D# F# G# A# on black keys. 19 atlas cells.
- **Quads** are parented to the key pivot, the text's top toward the hinge. White: 17 mm wide, 4-28 mm behind the front, y +.0003. Black: 10 mm wide, 4-22 mm behind the black front, y .0123.
- **Art.** White keys: a pale rounded sticker rgb(252,249,241) with ink #2a2420. Black keys: pale ink rgb(240,232,214). Bold sans, em 10 mm (white) / 5.9 mm (black), stretched ×1.5 along the key.
- **Middle C:** a coral rgb(238,132,108) bar, 1.35 mm tall, under the name.
- **Rendering.** Lit, alpha test .02, no depth write, tinted with the key; drawn before every other transparent (queue ≈2990). Bleed the fill colour into clear texels. Labels are never hit targets.

### 2.3 Contact and strike (update, follow)
**Contacts.** Each pointer has `c = {key, s, hist}`.
- **Inactive:** `Leave(c)`, or `FingerUp` on its held key.
- **Active:**
  1. Transform prev and tip into piano space.
  2. `Track`.
  3. `Follow` if it has a key.
  4. Still none: `c.key = EntryKey(prev, tip)`, then `Follow`.
  5. Stamp `key.finger = max(c.s)` for this frame.

**Predicates** (Unity coordinates).
- **Index:** `i = floor((x−x0)/W)`; none outside 0..51. `blacksNear[i]`: the blacks with `|b.x − white_i.x| < W`.
- **`OnBlackTop(b,x,back,in)`:** `|x−b.x| < BW/2−in && back < L−in && back > (L−BL)+in`.
- **`KeyTopAt(x,back,black,in=.0015)`.**
  - Black: the first b in `blacksNear[i]` passing `OnBlackTop`.
  - White: `w_i`, unless `|x−w.x| > WW/2−in`, `back < in`, `back > L−in`, or some b has `OnBlackTop(b,…,−in)`.
- **`EntryKey(a,b)`.** Try (top BTOP, black), then (0, white). If `a.y > top ≥ b.y`, return `KeyTopAt` at the crossing point. A press starts only by coming down through a key top inset 1.5 mm. Arriving from a side, a gap, the front or the case lands on nothing; the finger must lift above the keys first.
- **`WithinKey(k,x,back,m)`.**
  - Black: `|x−k.x| < BW/2+m && back < L+m && back > (L−BL)−m`.
  - White: `|x−k.x| < WW/2+m && −m < back < L+m`.
- **`KeyAt(x,back)`** (slides): if `back > L−BL`, the first black with `|x−b.x| < BW/2+.0015`; otherwise `w_i`.
- **`FingerDepth(k,y,back)`** = `((black?BTOP:0)−y)/(lever·MAX_ANGLE)`, with `lever = max(L − max(back, black ? L−BL : 0), .06)`. S_ESC is 9 mm down at a white key's front, 4.5 mm halfway along, and 7.2 mm at a black key's front.

**`Follow(p,c,a,b,sdt)`.**
1. **Off the key** (`!WithinKey(key,b,.004)`): `next = back ∈ (−.002, L) ? KeyAt(b) : null`.
   - **Glissando:** if `next` is another key of the same colour with `FingerDepth(next,b) > 0`, call `FingerUp(key)` if p struck it, then set `c.key = next` and `c.s = depth`. If p had struck, `c.s ≥ .9` and `Speed > .15`, call `PressKey(next, p, clamp((Speed/2)^.8, .2, .9))`. Return.
   - Otherwise `Leave`.
2. `s = FingerDepth(key,b)`; if `s < 0`, `Leave`.
3. `s0 = FingerDepth(key,a)`; `c.s = s`.
4. **Not yet pressed by p:**
   - If `s0 ≥ .9`, return: still down from an earlier stroke.
   - `v = DownSpeed()`; if `v < .07`, return: a resting finger is silent, though the key still follows it.
   - Fire `PressKey(key, p, StrikeVelocity(v))` if `s ≥ .9`, **or early** on sparse tracking: `sdt ≥ .025 && s ≥ .55 && v ≥ .5 && s + v·sdt·DepthPerMetre ≥ .9`.
5. **Pressed by p, `s < .5`:** `FingerUp`. Repetition: half travel re-arms the key.

`Leave` = `FingerUp` if pressed, then `key = null`, `s = 0`.

**History**, on the piano clock (`clock += dt`).
- **Track.** If the history is empty, or its last entry is more than 1e-4 from `a` on any axis, reset it to `[{now−sdt, a}]`. Append `{now, b}` if b is new. Shift while `count > 2 && (count > 12 || h[1].t < now−.1)`.
- **DownSpeed.**
  1. `t0 = end.t − .03`.
  2. Walk i back from n−2 while `i > 0 && h[i].t > t0`; take `y0 = h[i].y`.
  3. If `h[i].t < t0` and `i+1 < n−1`, interpolate y0 at t0.
  4. Return `(y0−end.y)/span`, or `(h[n−2].y−end.y)/sdt` if the span ≤ 1e-4; 0 if n < 2.

**`StrikeVelocity(v)`**.
- `x = clamp(ln(v/.07)/ln(1.5/.07), 0, 1)`; `level = .88·10^(x−1)`; `vel = clamp(√(max(0, level−.08)/.8), .1, 1)`.
- This inverts the piano synth's `.08 + .8·vel²` level curve.
- Samples: .07 → .10, .2 → .38, .5 → .62, 1.0 → .84, ≥1.5 → 1.

### 2.4 Key motion (animateKeys, spring, keyShape)
**States.** `fingered` = a contact is on the key this frame. `driven` = pressed but not fingered (keyboard, mouse, playback, lesson).

**Per frame:**
1. **Idle** (neither, depth 0, v 0): `Damper` if `damperPending`.
2. **Driven:** `depth = min(1, depth + dt/.045)`, `v = 0`.
3. **Otherwise:** run the spring if moving. If fingered, `d = KeyShape(finger, .9)`; when `d ≥ depth`, set `depth = d`, `v = 0`.
4. **Damper:** if `damperPending`, nobody presses and `depth ≤ .3`.
5. **Write** the transform, tint and label only when depth changed.

**`KeyShape(s,esc)`.**
- `s ≤ 0` → 0.
- Otherwise `d = s`. Inside `(esc−.06, esc+.025)`: `d −= s ≤ esc ? .06·sm((s−esc+.06)/.06) : .06·(1−sm((s−esc)/.025))`, where `sm(t) = t²(3−2t)` clamped.
- If `d > .94`: `d = 1 − .06·exp(−(d−.94)/.06)`.
- The key pauses near .84 while the finger goes .84→.90, catches up by .925, and never passes 1.

**Spring** (exact damped oscillator):
```csharp
float w=54, z=.85f, wd=w*Mathf.Sqrt(1-z*z), e=Mathf.Exp(-z*w*dt), C=Mathf.Cos(wd*dt), S=Mathf.Sin(wd*dt), B=(v0+z*w*x0)/wd;
x=e*(x0*C+B*S); v=e*((B*wd-z*w*x0)*C-(x0*wd+z*w*B)*S); if (x<=0||(x<.001f&&v<.05f)) x=v=0;   // damper ≈44 ms, rest ≈100 ms
```

### 2.5 Notes and automatic sustain
There is no pedal; released notes ring on.
- **`PressKey(k,p,vel)`.**
  1. If no key is held and `now − lastRelease > .6 s`, call `Damp()`: the pedal lifts and every sustained voice whose key isn't held is released. Only the first keeps its damper thud.
  2. Add p to `pressedBy`; clear `damperPending`; add k to `heldKeys`; make k p's key; stamp `owner.LastPlayMs`.
  3. Release k's old voice and drop k from `sustained`.
  4. `voice = audio.Piano(midi, vel)`; record `pianoOn`.
- **`FingerUp`:** remove p. With no presser left, set `damperPending` (the damper falls on the way up).
- **`ReleaseKey`** (keyboard, mouse, playback, lessons): `FingerUp`, then `Damper` at once.
- **`Damper`:** remove k from `heldKeys`; `lastRelease = now`. If k has a voice: record `pianoOff`, set `ringSince`, add k to `sustained`.
- **Each frame:** a pressed key leaves `sustained`; a key ringing longer than 6 s gets `voice.Release()`. These timers use wall time.

### 2.6 Hover, zone, API
- **Hover**, for pointers with no key.
  - Look for a black top if `y > BTOP`, else a white top if `y > 0`. Skip if there is no key, `depth > .02`, the key is pressed, or `h = y−top > .05`.
  - Glow at `(key.x, top+.001, clamp(back, black?.10:.045, .12))`; axis +z, normal +y; size .07 × (.016 black / .024 white); strength `1−max(0,h)/.05`; gain .45; steady.
- **`InPlayZone(world)`**: `|x| < .641`, `back ∈ (−.30, .21)` [src z ∈ (−.21, .30)], `y ∈ (−.15, .28)`.
- **API.**
  - `PressMidi(midi, ptrKey, vel=.75)` / `ReleaseMidi`: one pointer per computer key, so chords hold.
  - `PlaybackOn/Off/ReleaseAll` use a ghost pointer; `ReleaseAll` also `Damp()`s.
  - Desktop: a click presses at .7, a drag glissando at .6.
  - The lessons read `W`, `BW`, `BTOP` and `KeyByMidi(m).x/.black`.

## 3. Drums
Each piece's strike surface is its local y = 0 plane.
- **Rig home:** Unity (.2, 0, −.26), yaw +90° [src (.2, 0, .26), −π/2]. The player's head sits at kit-local (−.26, ·, −.2).
- Placement may shift the kit a few cm to clear the side table.

**Pieces** after the cymbal move:

| id | kind | type | r | depth | pos (Unity) | pitch, Unity ° [src rot.x] |
|---|---|---|---|---|---|---|
| 0 | snare | drum | .18 | .14 | (0, .72, .30) | −5.73 [.10] |
| 1 | tom1 | drum | .15 | .20 | (−.02, .86, .54) | −25.78 [.45] |
| 2 | tom2 | drum | .165 | .22 | (.29, .85, .42) | −25.78 [.45] |
| 3 | floor | drum | .20 | .36 | (.38, .62, .02) | −2.86 [.05] |
| 4 | kick | drum | .28 | .40 | (.12, .29, .70) | −90 [π/2]; head faces the player |
| 5 | kick (pad) | drum | .10 | .03 | (−.27, .60, .20) | −14.32 [.25] |
| 6 | hat | cymbal | .17 | – | (−.42, .80, .36) | −6.88 [.12] |
| 7 | crash | cymbal | .21 | – | (−.39, 1.02, .58) | −20.05 [.35]; was (−.30, 1.00, .56) |
| 8 | ride | cymbal | .22 | – | (.48, 1.04, .27) | −17.19 [.30]; was (.44, .98, .24); leg turn .91 |

- Negative pitch tilts the head toward the player.
- Construction: data `drums.build`, `drums.stands`, `drums.kickExtras_unity`.
- The hi-hat's lower cymbal sits at y −.016 (closed) or −.05 (open): `y += (target−y)·min(1, 18dt)`.

**Hit test**, for each piece × pointer active now and last frame:
1. A shoulder strikes cymbals only.
2. In piece space, if `tip.y > .02 || prev.y > .02`, set `arm = true`.
3. Require `prev.y > 0 ≥ tip.y`, `prev.y ≤ (stick ? .35 : .10)`, and `arm ≠ false` (unset counts as armed).
4. `C = lerp(prev, tip, prev.y/(prev.y−tip.y))`, `rr = hypot(C.x, C.z)`. Skip if `rr > (drum && !pad ? r+.03 : r)`; the extra 3 cm on a drum is its rim.
5. `arm = false`: any pass through disarms, sounding or not.
6. `vy = (prev.y−tip.y)/sdt`, with `(v0, v1) = stick ? (.3, 3.0) : (.12, 1.5)`. Silent if `vy < v0`.
7. `vel = clamp(((vy−v0)/(v1−v0))^.7, .05, 1)` → `Hit(piece, vel, p, false, StrikeAt(piece, rr))`.

Ghost notes come out near .05. Fingers: .3 m/s → .24, .5 → .41, 1 → .73. Sticks: .5 → .16, 1 → .39, 2 → .72.

**`StrikeAt(piece, rr)`**, with `pos = clamp(rr/r, 0, 1)`:
- hat: `pos > .75` → `edge`, else `top`;
- crash/ride: `pos < .22` → `bell`, `> .85` → `edge`, else `bow`;
- drum: `!pad && rr > .9r` → `rim`, else `head`;
- `open` is the hi-hat state.

**`Hit(piece, vel, p, force, strike)`**.
1. Unless forced, drop the hit if `now − piece.lastHit < 3 ms` (a same-frame duplicate).
2. If p: drop it if `now − p.DrumLastHit[id] < 45 ms`. Otherwise stamp `now` on **every pointer of p's owner** (all five fingertips, or a stick's tip and shoulder) and on p. The other hand or stick can still flam.
3. Set `lastHit = now` and `choked = false`; stamp `owner.LastPlayMs`.
4. `audio.Drum(hat && open ? "hatOpen" : kind, vel, strike ?? {.3, head, open})`; record `drum`.
5. `flash = 1`. Cymbals: `wobble = min(1, wobble + .5 + .6vel)`, `phase = 0`.

**Choke**. A hand laid on a ringing cymbal stops it.
- **Applies to** a cymbal not yet choked and hit 60-3000 ms ago.
- **Trigger:** the first non-stick pointer, active now and last frame, with `Speed ≤ .35`; and in piece space `|tip.y| ≤ .03`, `hypot(tip.x, tip.z) ≤ r`, `|prev.y| ≤ .03`, `|Δy| ≤ .004`.
- **Effect:** `choked = true`, `wobble = 0`, `audio.Choke(kind)`, stamp `LastPlayMs`, record `drumChoke`.

**Hi-hat stand pinch**. Pinching the pole under the cymbals opens or closes the hat.
- **Trigger:** a non-stick index-tip pointer on its hand's `PinchStart`, with the hand holding nothing.
- **Zone** (kit-local): `hypot(x−hat.x, z−hat.z) < .08` and `.12 < y < hat.y−.08 = .72`.
- **Effect:** `SetHatOpen(!open, hand)`. That does nothing if unchanged; closing calls `Choke("hat")` and plays `Drum("hatClose", .7)`. It stamps the hand and records `hat`, except during playback.

**Visuals**.
- `flash −= 6dt` (floor 0); drums scale by `1 + .015·flash`.
- Cymbals: `phase += 28dt`; `wobble *= exp(−1.6dt)`; `w = .07·wobble`; the top mesh turns by `Euler(−sin(phase)·w, 0, cos(.9phase)·w)` in degrees [src (+sin, 0, cos)].

**Hover**.
- When `−.005 ≤ y ≤ .1` and `rr ≤ r+.02` (shoulders only over cymbals): a disc at `(x·k, .002, z·k)` with `k = rr > r−.03 ? (r−.03)/rr : 1`.
- Normal: piece up. Size .09 × .09. Strength `1 − y/.1`.

**Nod kick**, `float Tick(dt, active)`. A quick nod of the head plays the kick, since no feet are tracked.
- **Active** = presenting, not AR placing, no rig held, kit visible, enabled. When inactive: reset `lastPitch` and `swing`, return 0.
- `pitch = asin(camFwd.y)`; `rate = (lastPitch − pitch)/dt`, positive when nodding down.
- **`rate > 1.6`:** `swing += dt`; `peak = max(peak, rate)`. If armed, `swing ≤ .16 s` and more than .18 s since the last kick:
  - `vel = clamp(.25 + .75(peak−1.6)/4.9, .25, 1)`;
  - `Hit(the real kick, not the pad, vel, null, force, {.2, head})`; `armed = false`.
- **Otherwise:** `swing = peak = 0`; re-arm once `rate < .8`.
- Expose `event Action<float> Nodded`: the guide counts nods.

## 4. Drumsticks
**Constants**: STICK_LEN .4, GRIP_FROM_BUTT .13, SHOULDER_FROM_BUTT .32, TILT −.22 rad, GRAB_RADIUS .12, HOVER_RADIUS .14, DEAD_ZONE .24, OPEN_DISTANCE .05, OPEN_DWELL .25 s, LOST_GRACE 1.5 s, smoothing 1 (snap).

**Mesh and rest pose.**
- Mesh: data `sticks_build`.
- At rest the two sticks cross in an X through the snare centre, butts just over the near rim, parented to the kit.
- Kit-local Unity rest: butt `(∓.068, .7102, .112)`, tip `(±.0692, .7477, .4859)`; quaternions in data `sticks`.
- Derived from snare-local `(∓.068, .009, −.188)` with `AngleAxis(90,right)*AngleAxis(∓20.05,forward)` [src (∓.068, .009, .188), Euler(−π/2, 0, ∓.35)].

**Pointers.** Each stick's owner holds two external pointers: `stick{i}` (`Stick`) and `stick{i}-shoulder` (`Shoulder`). Only the drums see them.

**Pick-up**. Checked before any rig grab, for a connected free hand with pointer 0 active.
- **Trigger:** `PinchStart`, or `GraspStart` while not playing.
- **Point:** the thumb-index midpoint, or the index tip alone. `d` = its distance to the nearest free stick's shaft segment.
- **`d ≤ .12`, grabbed:** set `Holding`, reparent the stick to the scene keeping its world pose, set the pointers `Down`.
- **`d < .24`, "near":** the pinch is consumed, unless a rig handle is within .02.
- Not possible when the kit isn't in the scene.

**Grip**. Each frame, **before** `Input.Update`; needs the wrist and both tips.
```csharp
grip=(thumbTip+indexTip)/2; fwd=(grip-wrist).normalized; up=wristRot*Vector3.up;            // dorsal
var uP=(up-fwd*Vector3.Dot(up,fwd)).normalized; axis=(fwd*Mathf.Cos(.22f)-uP*Mathf.Sin(.22f)).normalized; // 12.6° droop to the palm
rot=Quaternion.FromToRotation(Vector3.up,axis); pos=grip-axis*.13f; tip.Next=pos+axis*.4f; shoulder.Next=pos+axis*.32f;
```
This is the mirror-safe form of rotating fwd about `fwd×up` by TILT in the source frame.

**Tracking loss.** `lostFor += dt`; the pointers go up, so the stick is silent and frozen. It snaps back when tracking returns, and is released after 1.5 s.

**Release** (after `UpdateGrab`).
- **When:** the hand is connected with both tips tracked.
- **Rule:** `openFor = (|thumb−index| > .05 && !Grasping) ? openFor+dt : 0`; release after `openFor > .25 s`. A pinch or a fist keeps the stick.
- **Release itself:** clear `Holding`; set the pointers `Down = Active = false`; reparent to the kit.
- **Settle** over .4 s: `settle += 2.5dt`, `k = 1−(1−settle)³`, and each frame `pos = Lerp(pos, rest, k)`, `rot = Slerp(rot, rest, k)`.
- `ReleaseAll` runs at session end and when the kit leaves the layout.
- **While held:** the hand's fingertip pointers are muted and it can't grab rigs; its pinch point is the raw index tip.

**Hover.** One glow per free stick within .14 of a hand pointer: at the grip point + .014 world up, axis = the stick, normal = up; .12 × .035; strength `1−d/.14`.

## 5. Harp

### 5.1 Geometry (data `harp`, `harp.build`)
- **Plane.** Strings and frame lie in local z = 0 (identical in Unity). The exception is the brass pins at **z −.03 Unity** [src +.03].
- **Rig home:** Unity (−.78, 0, −.2), yaw +83° [src (−.78, 0, .2), −83°]. The strings stand square to the seat, on the player's left.
- **Soundboard and neck.** The soundboard runs from sb0 (−.55, .17) to sb1 (0, 1.10). The neck is a centripetal Catmull-Rom (tension .5; points in data `harp`); its tube radius .04 is scaled by `1−.4·clamp((t−.55)/.4, 0, 1)` along the arc length.
- **Soundbox.** A flat-shaded pentagonal frustum behind the soundboard, from `sb0+.093·out` (circumradius .1) to `sb1+.057·out` (.055), with `out = (.8607, −.509)` and one face toward the strings.
- **Strings** (i = 0..21, t = i/21).
  - Ends: `a = lerp(sb0, sb1, .045+.82t)` (BOARD_RANGE [.045, .865]); `b = neck.GetPoint(.075+.805t)` (NECK_RANGE [.075, .88]), using the curve parameter, not arc length.
  - Pitch: `midi = 48 + 12(i/7) + [0,2,4,5,7,9,11][i%7]`, C major from C3 to C6.
  - Shape: radius `.0016−.0005t`; 5 sides × 16 segments.
  - Colour: C red #c52323, F blue #1f3fa6, others cream #f1e9d6; each string has its own material.
  - String 0 (C3, 1.137 m) is by the pillar; string 21 is C6 (.215 m).
- **`PlayPoint(i)`** = `lerp(a, b, clamp((.95−a.y)/(b.y−a.y), .3, .7))`; lessons use it with `Highlight(i, k)`.
- **`Side(s,x,y)`.** `u = ((P−a)·d)/|d|²` and `dist = (d.x(y−a.y) − d.y(x−a.x))/|d|`, positive toward the pillar. `Across = {d = dist, z, u}`.

### 5.2 Constants
ZONE_Z .012 (sweep catch), POKE_R .009 (poke catch), CATCH_MAX .006 (slip), MIN_LIFT .002, HOLD_SLACK 1.5, PLUCK_SPEED .2 m/s, RETRIGGER_MS 70, REST_Z_OUT .005, REST_Z_IN .03, REST_SPEED .12, REST_DRIFT .01, REST_HOLD_MS 150, REST_REACH .02, PLUCK_GUARD_MS 400, HOVER .05, HOVER_REACH .02, VIB_BASE .0012, VIB_VEL .0028, DAMP_TAU .035. `velocityOf(speed) = clamp((speed−.03)/1.2, .2, 1)`.

### 5.3 Update and resting tips
**State.** Each pointer has a record `{inZone=false, side=1, since=0, x0, y0, held=null, mode=across, ud=1, uz=0, u=.5, speed=0}`. Each hand has a tally `{count, lo, hi, under[5]}`, reset every frame.

**Per hand pointer:**
1. **Leave** if it isn't active now and last frame, or its tip is outside `|z| ≤ .08, .1 ≤ y ≤ 1.5, −.75 ≤ x ≤ .12`. Leaving resets `inZone` and `since`; a held string goes back silently.
2. `moved = |tip−prev|² > 1e-12`; a repeated pose changes nothing.
3. If `Rest(p)`: let a held string go silently (if moved) and skip the pointer.
4. Else, if moved: `Stroke`.

Then: palm damping (§5.5), drop voices past `Until`, and `Animate` every string.

**`Rest(p)`.** A tip settled on the strings; it never plucks.
- **Side:** on first sight, `side = (prev.z ≠ 0 ? prev.z : tip.z) < 0 ? −1 : 1` (the side the hand came in on); `zs = tip.z·side`.
- **Not resting** (`since = 0`, return false) if `zs > .005` (short of the plane, aiming), `zs < −.03`, `Speed ≥ .12`, or no string with `u ∈ [.02, .98]` lies within `|dist| < .02`.
- **Dwell:** restart it at the tip if `since == 0` or the tip drifted more than .01 in x/y. Return true while the dwell is under 150 ms.
- **Resting:** add the nearest string to the hand's tally (`under[count]` if count < 5; `count++`; update lo/hi); return true.

### 5.4 Pluck: catch, bend, slip (Stroke, LetGo, Pluck)
```
Stroke(p,rec,sdt):  first=-1; Delay(f){ if(first<0) first=f; return max(0,f-first)*sdt }  // glissando spread
  after=0; skip=null
  if rec.held: s=rec.held; o0=Across(s,prev); o1=Across(s,tip); skip=s
    was=o0.d*rec.ud+o0.z*rec.uz; d=o1.d*rec.ud+o1.z*rec.uz; off=|o1.d*rec.uz-o1.z*rec.ud|
    reach=(|rec.uz|>|rec.ud| ? POKE_R : ZONE_Z)*HOLD_SLACK
    if d>=CATCH_MAX: f = d>was ? clamp((CATCH_MAX-was)/(d-was),0,1) : 1            // slips off: sounds
                     rec.speed=max((d-was)/sdt,PLUCK_SPEED); LetGo(rec,true,p,velocityOf(rec.speed),Delay(f)); after=f
    elif d<-MIN_LIFT: LetGo(rec,false)                                                // backed off: silent
    elif off>reach || o1.u<.02 || o1.u>.98:                                           // lifted/slid off
                     d>=MIN_LIFT ? LetGo(rec,true,p,velocityOf(rec.speed)*d/CATCH_MAX,Delay(0)) : LetGo(rec,false)
    else: s.bend=max(0,d); return                                                     // held: nothing else caught
  events=[]
  for s in strings, s!=skip:  o0,o1=Across(s,prev),Across(s,tip); dd=o1.d-o0.d; dz=o1.z-o0.z; len=hypot(dd,dz)
    if len<1e-6 || len/sdt<PLUCK_SPEED: continue
    ud=dd/len; uz=dz/len; from=o0.d*ud+o0.z*uz; if !(from<0 && o1.d*ud+o1.z*uz>=-1e-9): continue  // crossed?
    f=-from/len; if f<after: continue
    poke=|uz|>|ud|; off=|o0.d*uz-o0.z*ud|; if off>(poke?POKE_R:ZONE_Z): continue
    u=Side(s,lerp(prev,tip,f)).u; if u<.03||u>.97: continue
    events.add({s,f,u,ud,uz,off,from,to=from+len,speed=len/sdt,mode=poke?through:across})
  keep only the min-off 'through' event (a poke takes one string); sort by f
  for k,e: if e.s.heldBy && e.s.heldBy!=rec: continue
    rec.{mode,ud,uz,u,speed}=e
    if e.to>=CATCH_MAX: Pluck(e.s,velocityOf(e.speed),{u,speed,mode,delay=Delay(clamp((CATCH_MAX-e.from)/(e.to-e.from),e.f,1))},p)
    elif k==last: rec.held=e.s; e.s.heldBy=rec; bendU=e.u; bendD=e.ud; bendZ=e.uz; bend=max(0,e.to); owner.LastPlayMs=now
LetGo(rec,sound,p,vel,delay): s=rec.held; rec.held=null; if s.heldBy==rec: s.heldBy=null; s.bend=0
                              if sound: Pluck(s,vel,{rec.u,rec.speed,rec.mode,delay},p)
```

**`Pluck(s, vel, o, p)`.**
1. Ignore it within 70 ms of the string's last pluck. Otherwise set `lastPluck` and `pluckedBy` (p's hand), and stamp `LastPlayMs`.
2. Release the old voice. `voice = audio.Pluck(midi, vel, audioNow + o.delay, o)`, with `o` defaulting to `{.35, 0, through}`. Record `harp {i, vel, u, speed, mode}`.
3. `ring = HarpRingSeconds(midi)`; `amp = .0012 + .0028·vel`.
4. **Swing direction:** for a finger stroke, `(ampD, ampZ) = (rec.ud, rec.uz)`; otherwise `across` → (1, 0) and `through` → (0, 1).
5. `ampU = clamp(u, .05, .95)`; `damped = false`; `bend = 0`; `flash = 1`.

### 5.5 Palm damping (and Damp)
Two or more tips of one hand resting on two or more strings is a hand on the harp. For each tally with `count ≥ 2 && hi > lo`:
1. Stamp `LastPlayMs`, so the curled fingers aren't read as a grab.
2. Take the span `lo..hi` if 3 or more tips rest (a palm across the strings), else the strings in `under`.
3. Skip strings this hand plucked within 400 ms (its own follow-through).
4. `Damp(s, live)` each remaining string that has a voice; `live` = how many there are, so a whole hand gives a louder "shh".

`Damp`: set `damped = true`; if there is a voice, clear it, call `voice.Damp(n)` and record `harpDamp`.

### 5.6 Visuals (Animate, Paint) and hover
- **Decay:** `amp *= exp(−dt/max(τ, 1e-3))` with `τ = damped ? .035 : ring/6.9`; below 4e-5 it is 0.
- **Glow:** `flash −= 4dt`. Emissive = #58dcff·(.35+.65·targetK) while targeted (this frame or last), else black; then lerp toward #ffc46a by `.8·flash` (`_EmissionColor` via MaterialPropertyBlock).
- **Shape,** while `amp > 0 || bend > 0`.
  - `phase += .86π` **per frame** (a deliberate blur, not time-based); `ring = amp·cos(phase)`.
  - Vertex offset at u (0 at a): `(min(bend,.006)·tri(u,bendU)·bendD + ring·shape·ampD)·N + (…·bendZ + …·ampZ)·ẑ`.
  - `tri` peaks at `clamp(bendU, .02, .98)`; `shape = .5·tri(u, ampU) + .5·sin(πu)`; `N = (−d̂.y, d̂.x, 0)`; `ẑ` = local +z.
  - Upload with `Mesh.SetVertices`, for moving strings only.
- **Hover.** A tip within `|z| ≤ .05`, in the box above, over the nearest string with `u ∈ [.02, .98]` and `|dist| < .02`: a glow at `lerp(a, b, u)` along the string, normal z; .07 × .016; strength `1−|z|/.05`.
- **Desktop:** a click plucks the nearest string within 2 cm (.7, `through`, speed .9).

## 6. Hover glow
- **Pool:** 16 quads; overflow is dropped in call order (instruments, tutorial, sticks).
- **Look:** additive #ffc46a soft dot (alpha 1 at the centre, .55 at 35 %, 0 at the edge); double-sided; no depth write; drawn after the transparents.
- **Orientation:** `a' = normalize(axis − n(axis·n))`; with no axis, x (or z if `|x·n| > .9`). `rot = LookRotation(n, Cross(n, a'))` puts local X along a'.
- **Placement:** `pos + .002n`; scale `(length, width, 1)`.
- **Opacity:** `(.2+.6·clamp01(strength))·(steady ? 1 : .9+.1·sin(7t))·gain`.

## 7. Play zones
A pinch over a playing surface is play, never a grab.
- **`OverDrums(pt)`:** the first piece with piece-local `−.12 ≤ y ≤ .25` and `hypot(x, z) ≤ r+.04`; else `"hat-stand"` inside the hi-hat pinch zone (§3).
- **`OverHarpStrings(pt)`:** harp-local `|z| ≤ .05`, and within .04 (measured in the plane) of a string segment a-b.
- **Piano:** `OverKeys` = `back > −.12` [src z < .12] && `y > −.02`. `PianoKeySpace` = `InPlayZone && y > −.05`.
- **`PlaySurfaceAt`** (only instruments in the scene and visible): `piano` (`InPlayZone && OverKeys`), else `drums`, else `harp`, else null.
- **Table:** `"table"` also counts, within 3.5 cm of a deck button or switch lever.

## 8. Grab guards
**Constants:** PLAY_GUARD_MS 1000, GRAB_DWELL .3 s, GRAB_STILL .025 m, GRAB_SPEED .5 m/s, GRAB_SMOOTH .06 s, SPEED_WINDOW .1 s, HINT_AFTER .05 s, DW_N 48, HANDLE_REACH .07 m, ZONE_PAD .12 m.

**Helpers.**
- **`IsPlaying(h)`** = `NowMs − h.LastPlayMs < 1000`. Stamped by piano presses; drum hits, chokes and hat toggles; harp plucks, catches and resting hands. Stick hits stamp the stick owner, not the hand.
- **`PinchPoint(h)`:** pointer 0's tip if active; else the raw index tip if the hand holds a stick; else null.
- **Dwell**, timed on `PinchFor`.
  - **Start:** `ema = from = at`; clear the history.
  - **Sample:** when `PinchFor` advances, `ema = Lerp(ema, at, 1−exp(−Δt/.06))`. While `PinchFor ≤ .08`, `from` is the running mean of the raw samples. Push `(t, ema)` into a 48-entry ring and return `|ema−from|`.
  - **Speed:** walk back from the newest entry to the first ≥ .1 s older (or the oldest). Return 0 if under .02 s of history, else `|Δp|/Δt`.
- **`GrabTarget(pt, reach=.07)`.**
  - The menu wins if it is shown and its zone (world bounds + .12) contains pt.
  - Otherwise: the rig whose nearest **handle** is within reach. A handle is a visible mesh's local bounds as an oriented box, distance `d² = Σ max(0, |(p−c)·axᵢ|−hᵢ)²`.
  - Played parts are never handles: piano keys; kit heads, cymbal groups, sticks; harp strings; table controls.
  - Ties within .1 mm go to the smaller zone.
- **`GrabCandidate(at)`** returns null when `PlaySurface(at)` is set; or when the piano is visible, `InPlayZone(at)`, and the rig is none, or is the piano with `PianoKeySpace(at)`; or when the rig is locked. Otherwise it returns the rig.

**Per hand per frame** (XR only, not while AR is placing):
1. **Sticks first** (§4). "Grabbed", or "near" with no handle within 2 cm, disarms the hand and ends its turn.
2. **On `PinchStart`:** `armed = !IsPlaying`. Start the dwell, or disarm if there is no point. `grabRig = armed && point && !holding ? GrabCandidate : null` (it drives the hint ring).
3. **Disarm** whenever the hand isn't pinching or is playing.
4. **Dwell running** (armed, `PinchFor < .3`): disarm if there is no point or the sample exceeds .025. From `PinchFor ≥ .05`, the first such hand fills the hint ring to `k = PinchFor/.3`.
5. **Dwell complete** (armed, `PinchFor ≥ .3`, no rig held). Disarm first (it's one-shot), then require:
   - a point with dwell sample ≤ .025 and dwell speed ≤ .5;
   - no play surface under it;
   - `rig = holding ? null : RigNear`, passing the piano rule and the lock.

   Then call `StartGrab(rig, at, h, WristYaw(h, reset))`. With no rig, in AR and away from every zone, the pinch re-places the piano.
6. **Two-hand, step turns, moving and letting go:** placement spec (`Docs/07-world-visuals.md` §8).

## 9. Audio and recorder contract
**Audio** (the engine clamps vel to [.05, 1]):
- **`IVoice Piano(midi, vel)`:** `Release()`, plus an `OnRelease` damper thud.
- **`Drum(kind, vel, Strike{pos, zone, open})`:**
  - kinds: snare, tom1, tom2, floor, kick, hat, hatOpen, hatClose, crash, ride;
  - source `"drum:"+kind` (the hat kinds use hat; the pad plays kick).
- **`Choke(kind)`:** fades the ringing hits, τ 12 ms.
- **`IStringVoice Pluck(midi, vel, double audioTime, PluckOpts{u, speed, mode, delay})`:** `Release()`, `Damp(n)` (τ 18 ms + "shh"), `Until`.
- **`HarpRingSeconds(midi)`** = `clamp(4.5 − .08(midi−48), .7, 4.5)` (current synth).

**Recorder events** go to the observers (lessons, guide), recording or not:
- `pianoOn{midi, vel}`, `pianoOff{midi}`
- `drum{kind, vel, pos, zone, open}`, `drumChoke{kind}`, `hat{open}`
- `harp{i, vel, u, speed, mode}`, `harpDamp{i}`

**Playback**:
- `PlaybackOn/Off`
- `PlaybackHit` (forced, no debounce)
- `PlaybackChoke`
- `SetHatOpen(open, null, playback: true)`
- `PlaybackPluck` (the full stroke)
- `PlaybackDamp`
- `ReleaseAll`

## 10. What depends on the tracking runtime (risks)
1. **Repeated poses.** A hand tracker can repeat joint poses between tracker samples; that is why the input tracks `SampleDt` and `moved`. On a repeated frame `Speed = 0`, so the choke stillness and harp rest tests pass at once. Native OpenXR predicts poses every frame (UNVERIFIED for Quest hands). Keep the code as it is; it is robust either way.
2. **Pinch.** The app's own 15/25 mm rule; `Pinching` survives a disconnect.
3. **Tracking.** connected → `isTracked`; joint visible → `TryGetPose`. A floor-level reference space plus rig parenting → an XR Origin with a floor origin and the camera offset.
4. **Clocks.** The design mixes wall time, the piano's dt clock and audio time. A frame-constant `NowMs` keeps the 3 ms same-frame dedupe meaning the same thing.
5. **Fingertips.** The raw tip joint, with no radius offset. Check that native positions match the ones the guards were tuned on (UNVERIFIED).
6. **Rendering.** Needs:
   - per-key colour → 88 renderers + MaterialPropertyBlock;
   - vertex colours;
   - clearcoat;
   - a text atlas for the labels;
   - additive glows;
   - CPU-deformed strings.

   Hex colours are sRGB.

## 11. Suggested C# surface
`IPlayable { void Tick(IReadOnlyList<Pointer> pts, float dt); void Hover(IReadOnlyList<Pointer> pts, HoverGlow g); }`
- **`Piano : MonoBehaviour, IPlayable`:** `InPlayZone(Vector3)`, `KeyByMidi(int)`, `PressMidi/ReleaseMidi(int, object ptr, float vel=.75f)`, `PlaybackOn(int, float)`, `PlaybackOff(int)`, `PlaybackReleaseAll()`, `SetOnSurface(bool)`, `ResetSurface()`, `W/BW/BTOP/L`.
- **`Drums`:** `Pieces`, `Hat`, `Hit(DrumPiece, float, Pointer, bool force, Strike?)`, `SetHatOpen(bool, HandState, bool playback=false)`, `PlaybackHit(string, float, Strike)`, `PlaybackChoke(string)`.
- **`Harp`:** `Strings`, `PlayPoint(int)` (local), `Highlight(int, float k=1)`, `PlaybackPluck(int, float, PluckOpts?)`, `PlaybackDamp(int)`.
- **`Sticks`:** `TryGrab(HandState, Vector3) → Grabbed/Near/None`, `UpdatePoses`, `UpdateReleases`, `ReleaseAll`, `Hover`.
- **`NodKick`:** `float Tick(float dt, bool active)`, `event Action<float> Nodded`, `Enabled`.
- **`PlayZones`** (static): `OverDrums`, `OverHarpStrings`, `PlaySurfaceAt`.
- **`HandInput`:** `Update`, `EndFrame`, `ResetMotion`, `IndexTip`, `Palm`, `GraspAmount`.

Run everything from one orchestrator `Update`, in §1.1 order.
