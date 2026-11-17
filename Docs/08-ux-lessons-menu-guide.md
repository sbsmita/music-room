# 08 · UX spec: palm menu, lessons, Sunny, recorder, layout modes, start flow

**Sources**
- Unity packages live under `Library/PackageCache/`:
  - **ISDK** = `com.meta.xr.sdk.interaction@61177ebcfd9a/Runtime`
  - **Core** = `com.meta.xr.sdk.core@4c2eac90db2d`
  - **MXA** = `com.meta.xr.sdk.audio@3292c33a8423`
  - **UGUI** = `com.unity.ugui@c0ef89c047f6`

**Builds on 05-instruments.md**
- `HandState`, `Pointer`, `HandInput.Palm|GraspAmount|IndexTip` (05 §1)
- `HoverGlow` (05 §6)
- the grab dwell (05 §8)
- the `Piano`/`Drums`/`Harp`/`NodKick` APIs (05 §11)
- its §0 conventions

**Data files**
- `lessons.json` (`Assets/MusicRoom/Lessons/Resources/`): all 29 lessons expanded (beat, len, time, duration, due, targets, hint per event): 13 piano + 9 drums + 7 harp, 826 events, 966 targets.
- `guide-lines.json` (`Assets/MusicRoom/Guide/Resources/`): Sunny's 22 lines with seconds, steps, praise, timings, ROBOT, chirps, eSpeak NG settings; drift-checked by `GuideLinesTests`.
- `Guide/Resources/Voice/`: the lines as 22.05 kHz mono WAV, rendered with eSpeak NG (§3.5); lengths equal the voice manifest.
- The UX visual data (Unity values): panel styling (`panel`), row rectangles per screen (`panel.screens`), summon ring, and Sunny's face, bubble, light and demo geometry (`sunny`).

## 0. Extra conventions
- **Facing rule.** Every object that faces the player (panel, Sunny, bubble, rings) has local **+z pointing away from the player**. +x is to the player's right, and the visible side is −z. The source frame uses the opposite (+z toward the player). Every offset here is already converted.
- **Bounds.** Growing a box by 0.12 m on every side is `bounds.Expand(0.24f)`.
- **Colours** are sRGB. The project is in Gamma (ProjectSettings.asset:50). If it moves to Linear, convert raw vector arrays with `Color.linear`.
- **Pipeline.** Built-in is active (GraphicsSettings.asset:40), so `Camera.Render()` and plain unlit shaders work.
- **Time.** `NowMs` and `dt` follow 05 §0. The audio clock is `AudioSettings.dspTime`.

## 1. Palm menu: `MenuPanel`

### 1.1 Summon gesture
Every frame, before pokes, check the hands in order. The first hand that meets all of these is *charging*:
- it is tracked, not pinching, and holds no stick;
- in Unity only: `OVRHand.IsSystemGestureInProgress` is false (Core Scripts/Util/OVRHand.cs:147). The system gesture is a palm-toward-face pinch, so it overlaps this pose;
- `NowMs − LastPlayMs ≥ 400`;
- `Palm(h, out c, out n)` succeeds (n points out of the palm, 05 §1.5) and `c.y ≥ head.y − 0.45`;
- `GraspAmount ≥ 0.15`;
- with `d = head − c`: `0.08 ≤ |d| ≤ 0.8` and `dot(n, d/|d|) ≥ cos 40° = 0.7660`.

Charging rules:
- **Reset.** A different charging hand (including none) resets `T = 0` and clears `done`.
- **No ring** while no hand is charging, or once `done` is set.
- **Charging.** `T += dt`, `k = min(1, T/0.5)`. The ring sits at `c + 0.03n`, faces the head, and has scale `0.5+0.5k` and opacity `0.15+0.65k`.
- **Fire.** At `T ≥ 0.5`: set `done`, hide the ring, `Toggle(hand)`.

A held palm toggles once. The same gesture hides the panel again. While the panel itself is held (it is the instrument of rig `lessons`), neither the summon nor the pokes run. On desktop, M toggles the panel, and it opens along the look direction.

### 1.2 Open, close, home
```text
dir = flattened camera forward (fallback +z); dist = 0.5
if hand && Palm(hand, out c, _): v = c - head; v.y = 0; if |v| > 0.05: dir = v/|v|; dist = clamp(|v| + 0.35, 0.30, 0.55)
pos = head + dir*dist; pos.y = head.y - 0.12
if piano visible: l = piano.InverseTransformPoint(pos)
  if |l.x| < 0.8 && -0.2 < l.y < 0.75 && -0.01 < l.z < 0.9: l.z = -0.01; pos.xz = piano.TransformPoint(l).xz   // at the key fronts
rotation = Euler(0, Atan2(pos.x - head.x, pos.z - head.z)*Rad2Deg, 0)
```
**Show.** Set `shown`, scale 1, activate, raise `open`. The grab zone is the world AABB of the face and back plate, plus 0.12 m.

**Clear-out** (after `open` and after a drag). If any panel mesh box intersects an instrument part box, step s = 0.02…0.45 m by 0.02. At each s, try in order:
1. toward the eyes by s, but only while the panel stays ≥ 0.3 m from them;
2. up by s;
3. toward the eyes and up, by s/2 each.

The first free pose wins, and the panel turns to face the player again. If none is free, it stays and a warning is logged.

**Hide** takes effect at once: clear `shown`, empty the zone, clear hover, set every poke state to `armed = touching = false`, raise `close`. The visual then fades over 150 ms and deactivates.

**Animation.** `animK` moves toward its target by `dt/0.15`. With `k = 1−(1−animK)²`: scale `0.9+0.1k`, opacity `k`.

**Hidden** at launch and on `OVRManager.HMDUnmounted` (Core Scripts/OVRManager.cs:310), and at session start and end. A re-arrangement poses an open panel at its seat-relative home `(0, 1.03, 0.47)`, yaw 0.

### 1.3 Geometry and rendering
**Face.** 0.32 × 0.24 m on a vertical cylinder of radius 0.8. The cylinder's axis is on the player's side, so the edges sit 15.9 mm nearer the player. Build the mesh (`curvedPlane`):
- for `i = 0..32`: `u = i/32`, `a = (u−0.5)·0.4`;
- vertices `(0.8 sin a, ±0.12, −0.8(1−cos a))`, with uv `(u, 0|1)` and normal `(−sin a, 0, −cos a)`;
- triangles `(b,b+1,b+2)` and `(b+1,b+3,b+2)` with `b = 2i` (Unity winding);
- add a MeshCollider for desktop clicks.

**Back plate.** The same curve, 0.3483 × 0.2688 m, 4 mm behind the face.

**Texture.** 1024 × 768 px (3200 px/m). Recommended setup:
- a uGUI Canvas (Screen Space – Camera) on a `PanelUI` layer;
- a *disabled* orthographic camera renders it into a 1024 × 768 ARGB32 RenderTexture with mips and aniso 4;
- call `Render()` only when the panel is dirty: on change, plus about 120 ms of redraws for hover fades;
- park the canvas at y −500 and leave `PanelUI` out of the XR camera's culling mask;
- text: TextMeshPro with LiberationSans SDF (UGUI `Package Resources/TMP Essential Resources.unitypackage`);
- face material: unlit, `Blend One OneMinusSrcAlpha`, ZWrite Off, output `tex·fade`. Draw the background at alpha 1.

**ISDK alternative:** `CanvasRenderTexture` (Scripts/Unity/UnityCanvas/CanvasRenderTexture.cs:36) + `CanvasCylinder` (CanvasCylinder.cs:29, `_cylinder` :48 → `Cylinder._radius` Shapes/Cylinder.cs:35); it renders every frame (:281-342). Keep the §1.4 poke math either way. Styling: data `panel`.

### 1.4 Pokes
Constants: ARM 0.003 m, HOVER 0.010 m, DEBOUNCE 250 ms, SLOP 0.008 m, BEHIND −0.06 m, edge margin 0.01 m.

Pokers are each tracked hand's `Pointers[0]` (the index tip only; see §8). The mouse is handled separately.
```csharp
const float R = 0.8f, W = 0.32f, H = 0.24f, CW = 1024, CH = 768;
static void ToSurface(Vector3 l, out float u, out float y, out float z) {   // l = panel.InverseTransformPoint(tip)
  float dz = R + l.z; u = Mathf.Atan2(l.x, dz) * R; y = l.y; z = R - Mathf.Sqrt(l.x * l.x + dz * dz); }  // z > 0: player side
foreach (var p in pokers) { var st = State(p);                              // armed, touching, moved, y0, scroll0, row
  if (!p.Active) { st.armed = st.touching = false; continue; }
  ToSurface(transform.InverseTransformPoint(p.Tip), out var u, out var y, out var z);
  if (Mathf.Abs(u) >= W/2 + 0.01f || Mathf.Abs(y) >= H/2 + 0.01f) { st.armed = st.touching = false; continue; }
  float px = (u / W + 0.5f) * CW, py = (0.5f - y / H) * CH;
  if (st.touching) {
    if (z > ARM) { st.touching = false; st.armed = true; if (st.row >= 0 && rows[st.row].scrolls && !st.moved) Fire(st.row); }
    else if (st.row >= 0 && rows[st.row].scrolls) { float dy = y - st.y0;
      if (!st.moved && Mathf.Abs(dy) > SLOP) st.moved = true; if (st.moved) SetScroll(st.scroll0 + dy / H * CH); }
  } else if (st.armed && z <= 0 && z > BEHIND && p.WasActive) {
    st.armed = false; st.touching = true; st.y0 = y; st.scroll0 = scroll; st.moved = false;
    st.row = RowAt(px, py); if (st.row >= 0 && !rows[st.row].scrolls) Fire(st.row);
  } else if (!st.armed && z > ARM) st.armed = true;
  if (hover < 0 && z > -0.005f && z < HOVER) hover = RowAt(px, py); }
void Fire(int i) { if (NowMs - rows[i].firedAt < 250) return; rows[i].firedAt = NowMs; Press(rows[i]); }
```
- **Debounce** is per row *slot*. The 24 slots are reused across screens, so the debounce also covers a screen change under the finger.
- **Hover highlight** fades in or out over 120 ms.
- **Hover glow**. For a tip with `−0.005 < z < 0.01` that lies over the face, add a HoverGlow spec:
  - position: the surface point, 7 mm out, along the surface normal;
  - size: 0.045 × 0.045;
  - strength: `1 − max(0,z)/0.01`.
- **Desktop.** Raycast the collider, then `px = uv.x·1024`, `py = (1−uv.y)·768` → `RowAt` → `Fire`.

### 1.5 Screens (rectangles: data `panel.screens`)
**Navigation.**
- Back goes to the parent screen: lessons→home, courses→lessons, lesson→courses, instruments→home, recordings→home, take→recordings.
- Back from `lesson` also stops the lesson.
- `Go(s)` resets scroll, the Delete confirm and hover; refreshes the takes on recordings; raises `screen`. `Go(same)` only redraws.
- Any lesson start shows `lesson` (`Tutorial.OnStart`). `lesson` with an idle tutorial falls back to `courses`.

Slot 0 of every sub-screen is the back row, which is the whole header.

| Screen | Header (title · note) | Rows: id label · note | Body / footer |
|---|---|---|---|
| home | "Music Room" · "Full room" / "Piano alone" | `menu` Lessons · "29 courses"<br>`menu` Instruments · status<br>`menu` Recordings · "N saved" (or "Saving off")<br>`guide` "Show me around" · "Sunny", or "Hide Sunny" · "Stop the tour" | "Show your palm to hide" |
| lessons | ‹ Lessons · "Poke an instrument" | `inst` Piano, Drums, Harp · "N courses" (on = single mode with that instrument current) | – |
| courses | ‹ instrument · "Courses" (+ "  ·  drag to scroll") | list (§1.6) of `course`: title · listNote ("15 notes", "8 beats") | – |
| lesson | ‹ title · instrument | `stop` Stop | progress bar; countdown digit + "Get ready", or the hint |
| lesson (done) | ‹ title · instrument | `replay` Replay<br>`courses` Courses | "Lesson complete!"<br>"N notes  ·  M mistake(s)  ·  T.T s"<br>progress bar |
| instruments | ‹ Instruments · status | `layout` All instruments<br>`layout` Piano, Drums, Harp · "Here now" / "Full room" / "Alone"<br>`lock` Lock instruments · "On: they stay put" / "Off: free to move" | unlocked: "To move one: pinch and hold its stand, case or frame"<br>locked, single: "Sweep a hand sideways to turn the page"<br>locked, all: "Pick one to play it alone, in front of you" |
| recordings | ‹ Recordings · "N take(s)" / "N takes  ·  drag to scroll" / "Saving off" | list of `take`: name · "20 Oct, 14:32" | empty: "No recordings yet" / "Press REC on the tape deck; each take is kept on this device." / "Nothing leaves the headset."<br>no storage: "Saving is off" / "This device gives the room no storage, so takes stay on the tape deck only." |
| take | ‹ take name | `play` "Play" / "Playing…" (on while this take plays; note "Loading…" until loaded)<br>`tstop` Stop<br>`delete` "Delete" / "Delete, sure?" | "date  ·  m:ss  ·  N event(s)"; while confirming, the RED footer "Poke Delete again to remove it from this device" |

**Actions**
- `guide`: if Sunny is present, Dismiss it and keep the panel open. Otherwise hide the panel and `Sunny.Start()`.
- `inst`: set the instrument, then `Go(courses)`.
- `course`: in single mode with another instrument current, `Layout.Set(single, lesson.instrument)` first. Then `Tutorial.Start(lesson)`.
- `stop`, `courses`: `Tutorial.Stop()`, then `Go(courses)`.
- `replay`: `Tutorial.Start(lesson)`.
- `layout`: `Layout.Set(mode, inst)`.
- `lock`: toggle `Layout.Locked`.
- `take`: open the take screen and load its record asynchronously.
- `play`: once the record is loaded, `Recorder.PlayEvents(events, seconds)`.
- `tstop`: `Recorder.Stop()`.
- `delete`: the first poke asks for confirmation. The second stops any saved-take playback, removes the take, and goes back to recordings if the take is still shown.

**Relayout and redraw** on every press, and when any of these change: the tutorial version, the recorder state or its external flag, Sunny's presence, the layout, or the library.

**Proposed for Unity** (to be agreed with 03-interaction-sdk-and-mruk.md, which asks for the scene permission on "Play in my room" and offers a "Set up my room" row). There is no landing page, so mixed reality needs a menu entry: a fifth home row `room`.
- In the VR room it reads "Play in my room" · "Passthrough"; in mixed reality, "Music room" · "Virtual room".
- The home rows then become h 100 with gap 12, starting at y 132.
- In MR without a Space Setup (03: `NoRooms`), add a sixth row `setup`, "Set up my room" · "Space Setup", which calls `MrScene.RunSpaceSetupThenReloadAsync()`. Six rows are h 84 with gap 10 from y 132, ending at 686.

### 1.6 Lists
- **Geometry.** The viewport is y 130..750 (620 px), with a pitch of 130 and rows 120 high. `maxScroll = max(0, n·130 − 630)`.
- **Placement.** Row i sits at `130 + 130i − scroll`, clipped to the viewport. A row with less than 30 px visible is not laid out.
- **Scrolling** (when `maxScroll > 0`). Every row scrolls: a touch fires on release, unless it moved more than SLOP. A drag sets `scroll = scroll0 + dy/0.24·768` (the content follows the finger), clamped to 0..maxScroll.
- **Thumb.** Top at `130 + scroll/(620+maxScroll)·620`; height `620²/(620+maxScroll)`.
- **Course lists.** Piano (13) has maxScroll 1060, drums (9) 540, harp (7) 280, so all three scroll.

### 1.7 Moving the panel
- **Grab target.** While the panel is shown and the pinch is inside its zone, rig `lessons` is the target. It beats every instrument and stays grabbable when the instruments are locked.
- **Dwell.** Same as 05 §8: a 0.3 s still pinch, and no play for 1000 ms.
- **While held:**
  - it follows the pinch;
  - it turns with wrist yaw about its own origin;
  - it lifts within −0.45 to +0.6 m of its height at first grab, after a 0.03 m dead band;
  - a second hand on it starts a two-hand twist; a second-hand pinch in the air, pulled 0.12 m sideways, turns it 45°.
- **No collision constraint.** On release it runs the clear-out. It shows the floor ring, but no cage.

## 2. Lessons: `Tutorial` (data `lessons.json`)

### 2.1 Data
**Token syntax**. A tune is space-separated `NOTES[:len]` tokens. `+` joins a chord; `len` is in beats (default 1, and `a/b` is allowed).

**Targets.**
- Piano `{midi}`, with C4 = 60.
- Drums `{kind}`: snare, tom1, tom2, floor, hat, crash, ride or kick. The kick target is the hand pad.
- Harp `{i}`, from 0 (C3) to 21 (C6). `midi = 48 + 12⌊i/7⌋ + [0,2,4,5,7,9,11][i%7]` (05 §5.1).

**lessons.json.** It has `counts`, `tutorial {SPEED, TRAVEL, leadSeconds}` and `lessons[]` in lesson-table order.
- A lesson is `{id, instrument, title, bpm, secondsPerBeat, leadSeconds, beats, seconds, eventCount, notes, listNote, events[]}`.
- An event is `{k, beat, len, time, duration, due, hint, targets[]}`.
- A target is `{midi, note}` for piano, `{kind, label}` for drums, or `{i, midi, note, color: red|blue|null}` for harp.

Load it with `JsonUtility`, using one `LessonTarget` class that has all six fields.

### 2.2 Clock, states, hits
**Constants.**
- SPEED 0.18 m/s.
- TRAVEL: piano 0.35, drums 0.35, harp 0.30 m.
- `lead = TRAVEL/SPEED` = 1.9444 s (harp 1.6667 s).
- `due_k = lead + beat_k·60/bpm`.
- EARLY 0.15 s, COUNTDOWN 3 s, pool of 64 blocks, HIT_FADE 0.3 s, WRONG_FLASH 0.3 s.

**States:** `idle → countdown → playing ⇄ waiting → done`. `Stop()` returns to idle. `Start()` stops any running lesson first, then raises `OnStart`.
- **Countdown.** No blocks; `t = 0`; the digit shown is `max(1, ceil(count))`. At `count ≤ 0` the state becomes playing, and spawning starts in the same frame.
- **Playing.** `t` and `elapsed` advance by dt. When `t ≥ due(cur)`, set `t = due(cur)` and switch to waiting.
- **Waiting.** Only `elapsed` advances. Later blocks hover `(due_k − due_cur)·SPEED` above their targets.
- **Spawning.** While playing or waiting, any event with `due − t ≤ lead` gets one block per target. If the pool is full, resume next frame.
- **Done.** When no targets are left. Summary: `{notes = total, mistakes, time = elapsed}`.

**Hits** come from the recorder observer stream (§4.1), so fingers, sticks, the nod, the mouse and the keyboard are all equal. An event is ignored if the state is not playing or waiting, if a recorder playback is running, or if it is not the lesson instrument's `pianoOn`/`drum`/`harp`.

Only `cur`, the earliest unfinished event, can be hit, and only while it is in reach: `waiting || due − t ≤ 0.15`.
- **Hit.** A matching target that has not been hit yet. Its blocks shrink in green. When `cur` is complete, waiting returns to playing and `cur` advances (or the lesson finishes).
- **Mistake.** Anything else played while in reach: `mistakes++`, and `cur.wrongT = 0.3`.
- **Out of reach,** a play does nothing.
- **Missing target.** A target the instrument does not have is skipped and dropped from `total`.

**Hints** (precomputed): "Play C4 + E4", "Pluck C4 (red)" (C strings red, F strings blue), "Hi-hat + Kick". The hint is drawn in white while waiting.

**Layout.** `Layout.Apply()` stops a lesson whose instrument has left.

### 2.3 Blocks and target glow
**Meshes.** Three meshes, one per instrument; only the lesson instrument's is drawn, and it hides along with that instrument.

**Material.** Unlit, transparent, ZWrite Off, drawn after the other transparents. Opacity 0.6, or 0.38 for the drum discs.

**Drawing.** `Graphics.RenderMeshInstanced` with a per-instance `_Color` (`UNITY_DEFINE_INSTANCED_PROP`, `MaterialPropertyBlock.SetVectorArray`), or a pool of 64 renderers.

Positions are local to the target, in Unity values, with `dist = max(0, due − t)·0.18`.

| | Mesh (pivot) | Local position | Scale |
|---|---|---|---|
| piano | unit box, pivot at its bottom face | `(key.x, top + dist + 0.0005, 0.05 white / 0.075 black)`, where top is BTOP for black keys, else 0 | `(0.6·keyW, h, 0.03)·s`, with `h = clamp(len·spb·0.0342, 0.012, 0.06)` |
| drums | unit cylinder (r 1, h 1, 32 sides), pivot at its bottom | `(0, dist + 0.001, 0)` in the piece frame | `(r, 0.01, r)·s`, with `r = min(0.475·pieceR, 0.06)` |
| harp | capsule, r 0.006, straight 0.028 | `P + n̂·min(dist, max(0, |E−P| − 0.25))`, with P = `PlayPoint(i)`, E = the eyes in harp space, and `n̂ = (E−P)/|E−P|`; rotation `FromToRotation(up, b−a)` | `s` |

**Scale `s`.**
- A hit block uses `1 − hitT/0.3`, then is freed at 0.3 s.
- A *live* block (cur, waiting, not yet hit) uses `1 + 0.08·pulse`, with `pulse = 0.5 + 0.5 sin(8·time)`.
- Every other block uses 1.

**Colour.** #46ff7a when hit. Otherwise #ff4646 while `wrongT > 0`. Otherwise `lerp(#58dcff, #d4f6ff, pulse)` when live, and #58dcff for the rest.

**Glow.** Each live block adds a HoverGlow with strength `0.6+0.4·pulse`:
- piano: at `(key.x, top+.001, z)`, axis +z, 0.07 × 0.016 (black key) or 0.024 (white key);
- drums: at `(0, .002, 0)`, a square of side `min(0.24, 1.6·pieceR)`;
- harp: at `PlayPoint(i)`, along the string, normal harp −z, 0.14 × 0.014, plus `Harp.Highlight(i, 0.6+0.4·pulse)`.

## 3. Sunny: `Sunny`

### 3.1 Look (constants: data `sunny`)
**Body.** A sphere, r 0.045, wearing a 512×256 painted face texture; uv u = 0.25 faces the player. The face has 8 states (mood × eyes × mouth). Pre-render them and swap only on change.

**Corona.** A 0.234 m plane, 0.027 m behind the body, drawn additive (`Blend SrcAlpha One`). `t` is Sunny's own clock.
```hlsl
float2 p=(uv-0.5)*2; float r=length(p), a=atan2(p.y,p.x);
float rays=0.55+0.45*sin(a*9+t*0.7)*sin(a*4-t*0.45);
float halo=smoothstep(1.0,0.3,r)*(0.35+0.65*rays), core=smoothstep(0.62,0.3,r);
float al=(halo*0.55+core*0.6)*_Glow; return float4(lerp(float3(1,.55,.12),float3(1,.86,.45),core)*al, al);
```

**Bubble.** A 0.26 × 0.115 m quad at (+0.0832, +0.1163, −0.01), ZTest Always.
- Text: TMP `TextMeshPro/Mobile/Distance Field Overlay` (TMP_SDF-Mobile Overlay.shader).
- Fit: step `fontSize` from 54 down to 30 in steps of 4 (with `ForceMeshUpdate()`) until `textInfo.lineCount ≤ 2`.
- It keeps the last line until hush.

**Light.** A point light #ffb866, range 1.4, intensity `0.7 + 0.2 sin(2.1t)`. Tune it by eye (UNVERIFIED). Optional on Quest.

**Demo.** An additive cyan ghost hand, charging ring, ghost panel and dotted arrow, all children of Sunny. The finger x values are mirrored (§8).

### 3.2 Placement and demo motion
**Placement.**
- Goal: `head + 0.55·fwd − 0.25·right` at `y = head.y − 0.10`, where fwd is the flattened camera forward and `right = Cross(up, fwd)`.
- Follow with `base = Lerp(base, goal, 1 − e^(−3dt))`. Snap on the first frame; freeze while leaving.
- Pose: `pos = base + (0, 0.012 sin(2π·1.4t), 0)`, `rot = LookRotation(pos − head)`. The audio sources follow `pos`.

**Demo motion.** Demos loop on `t = demoT % 3`, with `ease(u) = u²(3−2u)`.
- **palm:** `Curl(0)`.
  - `turn`: `ease(t/.6)` up to t = 0.6, holds at 1 until 1.1, then `1−ease((t−1.1)/.5)`, and 0 from 1.6.
  - The hand rotates `+90°(1−turn)` about x and sits at y `−0.05 − 0.04(1−turn)`.
  - The ring grows on [0.6, 1.1), with `k = (t−.6)/.5`.
- **poke:** `Curl(.9, idx 0, thumb .5)`, hand `Euler(−90°, 0, 180°)`, ghost panel shown.
  - For t0 ∈ {0.3, 1.2}, `push = sin(π(t−t0)/0.6)` inside the window.
  - The hand sits at z `−0.25 + 0.07·push`, y −0.02.
  - Ghost row opacity: `0.1 + 0.55·clamp01((push−.7)/.3)`.
- **swipe:** `Curl(0)`, arrow shown.
  - For t0 ∈ {0.2, 1.5}, with `u = (t−t0)/0.25`:
    - on [0,1): `X = 0.15 − 0.3·ease(u)`, fade 1;
    - on [1,1.6): `X = −0.15`, fade `1−(u−1)/.6`.
  - Otherwise `X = 0.15`, and fade is `(t−1.2)/.3` on [1.2,1.5), `t/.2` below 0.2, `(t−2.7)/.3` from 2.7, else 0.
  - The hand sits at x `0.07+X` with opacity `0.5·fade`.
- **nod:** the demo hand is hidden; Sunny nods itself. For t0 ∈ {0.2, 0.9}, `n = sin(π(t−t0)/0.4)`: lower `pos.y` by `0.04n`, then rotate local x by `−0.45n` rad.

`Curl(k, idx=k, th=k)` sets each finger to `Euler(−1.5k rad, 0, 0)` (the index uses idx) and the thumb to `Euler(−0.9·th, 0, −1)` rad.

### 3.3 The tour

| | id | Demo | Lines (VR / desktop) | Completes when | Timeout |
|---|---|---|---|---|---|
| a | open | palm | open_vr / open_desk | the panel opens (`open` event, or polled) | – |
| b | poke | poke | poke_vr / poke_desk | `panel.screen` changes from the step's starting screen | – |
| c | close | palm | close_vr / close_desk | the panel closes | – |
| d | swipe | swipe | swipe_vr / swipe_desk | in single mode, `layout.current` changes from the first value seen | 12 s → swipe_skip |
| e | nod | nod | nod_vr / nod_desk | `NodKick.Nodded` fires; or, outside playback, a kick `drum` event on desktop, or in the headset with every non-mouse pointer more than 0.2 m from the pad | 12 s → nod_skip |

**State.**
- States: `idle|intro|step|wait|praise|skip|bye|leave|gone`.
- Fields: `timer`, `queued`, and `quietT`, the seconds since the current line stopped being busy.
- *Quiet* means `quietT ≥ 0.3`.
- `Hold(id, t) = max(t, lineSeconds + 0.45)`, where `lineSeconds = clip/rate + 0.14`.

**Run.**
- **Start.** Set desktop to `!headset`, `done = false`, `step = −1`. Show and snap Sunny, set scale, opacity and glow to 1, mood happy. `Say(intro_vr|intro_desk)`, then intro with `timer = Hold(intro, 3.2)`.
- **intro.** When `timer ≤ 0` and quiet, `BeginStep(0)`.
- **BeginStep(i).** If `i ≥ 5`, Finish. Otherwise store `screen0`, and `layout0` (current if single, else null); zero `nodded` and `demoT`; mood calm. Enter step with `timer = timeout` (or 0), then `Say(line)`.
- **step.** Check completion every frame, plus via the hooks. A timeout runs Timeout.
- **Complete.** End the demo, mood happy, `After(praise)`. **Timeout:** end the demo, `After(skip)`.
- **After(x).** If the line is busy or it is not quiet yet, go to wait with `queued = x`. Otherwise `SayNext(x)`.
- **wait.** Once quiet, `SayNext(queued)`. This is how Sunny finishes the sentence: the demo stops and it smiles at once, but the praise waits for the end of the line plus 0.3 s.
- **SayNext(praise).** `Say(praise_[step%5])`, then praise with `timer = Hold(id, 1.7)`.
- **SayNext(skip).** `Say(step.skip ?? "skip")`, then skip with `timer = Hold(id, 3.2)`.
- **praise, skip.** When `timer ≤ 0` and quiet, `BeginStep(step+1)`.
- **Finish.** `done = true`, end the demo, happy, `Say(bye)`, then bye with `timer = Hold(bye, 2.6)`. When `timer ≤ 0` and quiet, Leave.
- **Leave.** State leave, timer 0.8, `Say(leave)`. With `u = 1 − max(0,timer)/0.8`: scale `1−0.95u`; body, bubble and glow at `1−u`; `pos.y += 0.2u`; light `(1−u)·0.8`. At `timer ≤ 0`, Vanish: hush, hide, light 0, gone.
- **Present** = visible, and the state is not idle, gone or leave.
- **Dismiss** ("Hide Sunny"): `done = true`, end the demo, StopVoice, Leave.
- **Toggle:** Dismiss if present, otherwise Start.
- **Blink:** first after 2–5 s, then every 2.5–6 s, shut for 0.13 s.

**When it runs.**
- run it once per launch, on the first frame with the HMD mounted and tracked (in the editor desktop mode, on Play);
- never restart it automatically after `done`;
- toggle it from the menu's `guide` row, or with Shift+H on desktop.

### 3.4 Speech and the robot voice
**Saying a line.**
- `Say(idOrText)` sets the caption to `LINES[id]` (or the given text), redraws the bubble, sets `quietT = 0`, and calls `Speak(id)`.
- `Speak` first calls `StopVoice`, so two lines never overlap. It then creates a new `talk {id, at, started, ended, cancelled, failed}`, resets `voicePeak = 0`, and plays the open chirp (always).
- A missing clip leaves the caption only. A clip more than 1.2 s late is dropped. Otherwise the voice starts at `asked + 0.14`.
- Unity preloads and processes every clip, so this is `voice.PlayScheduled(dspTime + 0.14)` with `endT = time + 0.14 + dryLength`.

**Line state.**
- `LineBusy` is false once the line is cancelled or ended. Before that it is true while `time < endT + 0.05` (started), for up to 1.3 s (still loading), or while `time − at < lineSeconds` (caption only).
- `Speaking` = started && !ended && !cancelled.
- A natural end (was speaking, now isn't, and `ended`) plays the close chirp. A line that was cut gets none.

**StopVoice** marks the line cancelled, fades it to 0 within 30 ms, then stops it. It is called from Speak, Dismiss and Vanish, and on `OVRManager.HMDUnmounted`.

**Robot chain** (`buildRobotChain`, `ROBOT`). The DSP is specified and measured in **06 §8**, with the biquads in 06 §1.2: a ring modulator (0.92 dry plus 0.08 × an 80 Hz sine), a 5 ms comb with 0.3 feedback at level 0.12, a 120 Hz high-pass (Q 0.7 in dB), and a 2500 Hz peak (Q 0.8, +2 dB).

The chain is linear, so pre-render each clip through it once at load (ring phase 0), keep the dry length for `endT`, and play the result on Sunny's spatialized AudioSource (`MetaXRAudioSource`, MXA Runtime/scripts/MetaXRAudioSource.cs:43, if the audio spec uses it).

**Mouth**.
- `level` is the RMS of the processed voice over 1024 samples at 48 kHz (21.3 ms).
- `peak = max(level, peak·e^(−1.2dt))`.
- The mouth is shut below 0.01. Otherwise it is open while `level > peak·(open ? 0.2 : 0.35)`.
- Precompute the envelope per clip and index it by playback time.

**Chirps**. The synthesis is in 06 §8 (toneVoice square through a 5 kHz low-pass); the values are also in `guide-lines.json` → `chirps`.
- open: 880→1320 Hz, then 1320→700 Hz;
- close: 1100→560 Hz, then 700→420 Hz, only when a line ends naturally.

Render them once and play them on a second AudioSource, so they never cut the voice.

### 3.5 Voice assets
- **Lines.** 22 lines (`guide-lines.json`). The `SPOKEN` table is empty.
- **Render** (22.05 kHz):
  1. eSpeak NG `-v en-us+Annie -s 150 -p 55 -P 70 -g 0 -a 120`;
  2. trim below 2% of the peak, keeping 30 ms;
  3. normalise the peak to 0.89;
  4. 5 ms fades.
- **Unity import.** Import the voice WAVs with Decompress On Load, Preload Audio Data and Force To Mono, as Vorbis ≈70 or ADPCM.

## 4. Recorder, tape deck, recordings

### 4.1 Events (emitters and playback targets: 05 §9)
Every recorded event also carries `t`, in seconds since Record, on the audio clock.

| type | Fields | Playback |
|---|---|---|
| pianoOn / pianoOff | midi, vel / midi | `Piano.PlaybackOn` / `PlaybackOff` |
| drum | kind, vel, pos (0..1), zone (head\|rim\|bell\|bow\|edge\|top), open | `Drums.PlaybackHit` |
| drumChoke | kind | `Drums.PlaybackChoke` |
| hat | open (not re-recorded during playback) | `Drums.SetHatOpen(open, null, true)` |
| harp | i, vel, u (0..1), speed, mode (through\|across) | `Harp.PlaybackPluck` |
| harpDamp | i | `Harp.PlaybackDamp` |

`releaseAll` calls `Piano.PlaybackReleaseAll()`. Store events in one flat struct, `[Serializable] struct TakeEvent { float t; string type; int midi; float vel; string kind; float pos; string zone; bool open; int i; float u; float speed; string mode; }`, whose field names are the stored take keys.

### 4.2 Recorder, exact semantics
```csharp
public void Add(TakeEvent ev) { foreach (var o in Observers) o(ev);        // lessons and Sunny see every play
  if (state != Recording) return; double t = Now - t0;
  if (events.Count >= 20000 || t > 600) { Stop(); return; } ev.t = (float)t; events.Add(ev); }
public void Record() { Stop(); events = new(); t0 = Now; state = Recording; }
public bool Play() { if (External) Stop(); /* §8 */ if (state == Recording) Stop(); if (take == null || take.Count == 0) return false;
  if (state == Playing) ReleaseAll(); state = Playing; playStart = Now; playIndex = 0; return true; }
public bool PlayEvents(List<TakeEvent> evs, double secs) { if (state == Recording) Stop(); if (evs == null || evs.Count == 0) return false;
  if (state == Playing) ReleaseAll(); saved ??= (take, duration); take = evs; duration = Math.Max(secs, evs[^1].t);
  state = Playing; playStart = Now; playIndex = 0; return true; }          // External => saved != null
public void Stop() { if (saved is { } s) { (take, duration) = s; saved = null; }   // the deck's own take comes back
  if (state == Recording) { take = events; duration = Now - t0; if (take.Count > 0) OnTake?.Invoke(new(take), duration); }
  if (state == Playing) ReleaseAll(); state = Idle; }
public void Update() { if (state != Playing) return; double t = Now - playStart;  // once per frame
  while (playIndex < take.Count && take[playIndex].t <= t) { var e = take[playIndex++]; if (Targets.TryGetValue(e.type, out var h)) h(e); }
  if (playIndex >= take.Count && t >= duration) Stop(); }
```
`Now` = `AudioSettings.dspTime`. Dispatch is quantised to frames.

### 4.3 Tape deck
- **Buttons.** REC → Record, PLAY → Play, STOP → Stop.
- **Pressing**. A peg presses when a tip enters a cylinder of radius 18 mm above it, 60–95 mm over the base. It fires once per entry per pointer.
- **Pegs** sink 5 mm (lerp 18/s) while touched or latched: REC latches while recording, PLAY during any playback.
- **Reels** turn at 3 rad/s while recording and 4 rad/s while playing.
- **Lamp.** #ff2a2a while recording, #2aff5a while playing, otherwise #332222.

### 4.4 Library: `TakeLibrary`
**Storage.** `persistentDataPath/takes/`:
- `index.json`: `{version:1, nextNumber, takes:[{id,name,created,seconds,count}]}`;
- `<id>.json`: `{id, name, created (ms epoch), seconds, events}`.

Write each file to `*.tmp` and then move it into place. Do the IO in `Task.Run` and marshal results back to the main thread. Serialize with Newtonsoft (a direct dependency, 3.2.2) and `DefaultValueHandling.Ignore`; this is lossless, because dropped fields are defaults.

**API** (async, never throws).
- `Available` is null until checked. If the folder isn't writable it becomes false, and the menu shows "Saving off".
- `List()` returns takes newest first.
- `Get(id)` and `Put(take)` return the record or null; `Remove(id)` returns a bool.
- `Version` counts changes, and `Changed` fires on each.

**Ids.** `"take-" + base36(ms) + "-" + base36(rand 0..999999)`.

**Auto-save.** `OnTake` calls `Put({name: $"Take {N} · {FormatLength(s)}", …})`, with U+00B7 between spaces and `N = nextNumber++` (§8).

**Formats.** `FormatLength`: `s = round(sec)`, then `$"{s/60}:{s%60:00}"`. `FormatDate`: local time, `"d MMM, HH:mm"`.

**Saved take.** The take screen's Play calls `PlayEvents(events, seconds)`. Stop, or the end of the take, restores the deck's take exactly. Lessons and Sunny ignore events during any playback.

## 5. Layout modes, page turn, lock, two-hand summon, swivel

### 5.1 Modes
**State** (kept for the session): `mode` is `all` (default) or `single`; `current` is piano (default), drums or harp; `Locked` starts false.

**`Set(mode, inst)`**
- Finishes any turn in progress.
- Applies scene membership and the rig and instrument lists. Removing the kit releases the sticks, removing the piano empties its zone, and a lesson whose instrument left is stopped.
- Re-arranges around the player and raises `layout` if anything changed.

**Single poses** in the seat frame (x right, +z ahead):
- piano: its home (0, 0.72, 0.45), keeping its height;
- drums: (0, 0, 0.10);
- harp: (mx, 0, 0.45) at yaw 180°, where mx is the mean x of the string midpoints (−0.33 fallback).

The world pose is `head.xz + Euler(0, seatYaw, 0)·offset`, with `seatYaw = atan2(fwd.x, fwd.z)`. Placement settles afterwards.

### 5.2 Page-turn sweep
**Qualifying hand.** A tracked hand qualifies when:
- it is open (`GraspAmount ≥ 0.15`);
- its palm is vertical (`|n·up| ≤ 0.6`);
- it is not pinching and holds no stick;
- it has not played for at least 400 ms;
- nothing is held: no grab, no swivel, no MR placing.

Any other hand's history resets.

**Samples.** Record the palm centre in the head frame on flattened axes, `(dot(p−head,right), y, dot(p−head,fwd))`, in a ring of 16. A jump over 0.4 m resets the ring.

**Fire** when all of these hold:
- the hand is armed;
- walking back from the newest sample, within 0.3 s, the first sample with `|Δx| ≥ 0.15` gives the span;
- `|Δx|/span ≥ 1.2 m/s`;
- `|Δx| ≥ 1.5|Δy|` and `|Δx| ≥ 1.5|Δz|`;
- the 0.6 s cooldown is over;
- no other turn fired this frame.

**Direction.** Left (Δx < 0) → +1 (next); right → −1.

**Re-arm.** After a fire, clear the history and disarm until the x speed over the last two samples is below 0.5 m/s. `Layout.Next(dir)` acts only in single mode with no turn running.

### 5.3 Turn animation
- **Next instrument.** `ROW[(i±1) mod 3]`.
- **Incoming rest pose.** The single pose about the current seat, settled with the outgoing instrument ignored.
- **Duration.** 0.35 s, eased as `e = k<.5 ? 2k² : 1−(2−2k)²/2`.
- **Outgoing.** `rest − dir·0.9e·right`, scale `1−0.15e`, opacity `1−e`.
- **Incoming.** `rest + dir·0.9(1−e)·right`, scale `0.85+0.15e`, opacity `e`.
- **Fading.** Clone the materials once per instrument; keep ZWrite on only while opacity > 0.5.
- **End.** Snap to the exact rest poses, apply, update zones and audio, raise `layout`.
- **Sound** (06 §9).
  - A noise burst: band-pass 1400 Hz, Q 0.7, LP 7000, peak 0.09, decay 0.32, send 0.25.
  - At +0.04 s: HP 2500, LP 9000, peak 0.035, decay 0.22, send 0.2.

### 5.4 Lock, two-hand summon, swivel
- **Lock.** When locked, only the menu panel can be grabbed.
- **Two-hand summon**.
  - Runs in the headset with no rig held. In MR, not while placing, and only while the piano is shown.
  - A hand counts if it is pinching, holds no stick, has not played for 1000 ms, and has its index tip outside every zone.
  - Two counted hands for 0.8 s, once: cancel the swivel, `ArrangeAroundPlayer(true)`, reset pointer motion.
  - It re-arms when fewer than two hands count.
- **Swivel**. VR room only.
  - A `PinchStart` with a tracked index tip, no play for 1000 ms, no grab, and outside every zone takes it.
  - Track `camera.InverseTransformPoint(tip).x`. When `|Δx| > 0.14`, snap, re-zero, and reset pointer motion. Pulling right calls `rig.RotateAround(head, up, −45°)`; pulling left turns +45°.
  - It ends on `PinchEnd` or tracking loss.
- **First arrange.** On the first tracked VR frame, `ArrangeAroundPlayer(true)`.

## 6. Start flow and desktop play
**Title card** (desktop only): the room seen from behind the seat, a card "VR Music Room" / "Play piano, drums and harp with your hands", and "Click to start".

**Unity headset:** no landing. Set PlayerSettings `productName` to "VR Music Room" (now "vr-music-room-unity", ProjectSettings.asset:16). Boot straight into the room:
1. optional `OVRScreenFade.FadeIn()` (Core Scripts/Util/OVRScreenFade.cs:28,139);
2. the panel starts hidden;
3. the first tracked frame arranges the room around the player;
4. Sunny starts with the VR lines.

Mixed reality is entered from the menu (§1.5).

**Unity desktop editor** (`!XRSettings.isDeviceActive` or `!OVRManager.isHmdPresent`, Core OVRManager.cs:462): an optional title card until the first click or key; the camera at (0, 1.2, −0.1) looking at (0, 0.95, 0.45), FOV 55, near 0.02, far 40.

| Input | Action |
|---|---|
| drag on empty space | yaw −= dx·k, pitch −= dy·k (radians, k = 0.0028·fov/55 per px; pitch within ±1.45) |
| wheel | `fov += 0.04·deltaY`, 26..90 |
| click / drag | keys, drums, strings, pegs, switches, menu rows |
| A W S E D F T G Y H U J K O L P ; ' | piano semitones 0..17 above base 60, velocity 0.75, one pointer per key (chords hold); Z / X: base −12 / +12 within 24..96 |
| M, Shift+H | toggle the menu, toggle Sunny |
| Shift+drag, Alt+drag | move an instrument on the floor at the hit height; turn it with `yaw0 − Δx·0.012` rad (only the menu while locked) |

**Hint text** (bottom left, 13 px, 60% opacity): "Drag to look around, scroll to zoom. Click or drag across the keys; the A S D F row plays piano, Z/X change octave; sustain is automatic. Click drums; drag across the harp strings. Shift+drag an instrument to move it, Alt+drag to turn it (unless Lock instruments is on). Fire, wind and snow switches sit on the side table. M opens the menu; click a row to choose. Shift+H shows or hides Sunny." `activeInputHandler` is currently 0 (legacy, ProjectSettings.asset:778); follow the setup spec.

## 7. Classes, events, frame order
- **Classes:** `MenuPanel`; `LessonLibrary`, `Tutorial`, `LessonBlocks`; `Sunny`, `SunnyVoice` (clips, Robotize, chirps, RMS), `SunnyView`, `SunnyDemo`; `Recorder` (plain C#), `TakeLibrary`, `TapeDeck`; `RoomLayout`, `PageTurn`, `SeatSwivel`, `TwoHandSummon`; `StartFlow`, `DesktopControls`.
- **Events:** `MenuPanel.Changed(open|close|screen)`, `RoomLayout.Changed(layout|turn)`, `Recorder.Observers`, `TakeLibrary.Changed`, `Tutorial.Version`, `Sunny.Present`.
- **Frame order** (one orchestrator, 05 §1.1): input → instruments not held, the panel included (summon, animation, pokes) → tutorial → HoverGlow → first arrange → MR → grab → listener → nod kick → recorder → decor → Sunny → swivel → two-hand summon → page turn → layout animation → `EndFrame`.

## 8. Deliberate design choices
1. **Index tip only.** If every fingertip poked, a flat hand could fire two rows. Unity pokes with the index tip only, which matches the line "Poke a row with your index finger".
2. **Demo hand.** The finger x values are mirrored, so the index finger sits on the thumb's side.
3. **Take names.** Splitting as `m = floor(s/60)` with `sec = round(s%60)` can give "0:60". Unity uses `FormatLength`.
4. **Take numbers.** Counting only the takes that exist at load would repeat numbers after a delete. Unity persists `nextNumber`, starting from the highest existing number + 1.
5. **Deck PLAY during a saved take.** It plays the deck's own take; it does not restart the saved take.
6. **Hints.** The row is "Lock instruments" (default Off), so the hints in §6 name it that way (never 'Unlock "Move instruments"').
7. **Storage wording.** The storage messages say "This device…".
8. **New menu row** for mixed reality (§1.5).
9. **Sunny's start.** Unity starts Sunny once per launch, not once per XR session.

## 9. Acceptance checks
- **Data:** 29 lessons (13/9/7), 826 events, 966 targets; list notes "15 notes" (piano-scale), "8 beats" (drums-beat).
- **Timing (piano-scale):** waits at due₀ = 1.9444 s; a correct note at `due − 0.14` hits; at `due − 0.16` it is neither hit nor mistake; a wrong note in reach is a mistake with a 0.3 s red flash.
- **Poke:** z +0.02 → −0.005 fires once and holding doesn't repeat; re-entry from +0.002 doesn't fire, from +0.004 it does after 250 ms; from behind (−0.07 → −0.01) never; on a scrolling list a 9 mm drag scrolls 28.8 px without firing, and a still tap fires on release.
- **Summon:** an open palm facing the head from 0.3 m toggles once after 0.5 s; a pinch, fist, palm down or hand below the waist never does.
- **Sunny:** completing mid-line delays praise to ≥ 0.3 s after the line; Hide leaves within 0.8 s and the row reads "Show me around"; an idle swipe step says swipe_skip at 12 s.
- **Recorder:** saves "Take N · m:ss"; after a saved take plays and stops, deck PLAY plays the deck's own take.
- **Page turn:** a 0.2 m left sweep in 0.12 s with a vertical palm gives +1; a fist gives nothing; a repeat within 0.6 s gives nothing.
