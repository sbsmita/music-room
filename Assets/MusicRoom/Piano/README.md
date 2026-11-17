# MusicRoom.Piano

The slim 88-key digital piano: geometry, note-name labels, the press model (escapement, strike velocity, glissando,
repetition, damper), automatic sustain, the key spring, hover glows, the play zone, desktop mouse and computer-keyboard
play, its `Rig` and its three audio sources, as spec 05 section 2 describes them, with
the deliberate changes of ARCHITECTURE.md.

References: `MusicRoom.Core`, `MusicRoom.Hands`, `MusicRoom.Audio`, `Unity.TextMeshPro` (the Ctrl / Cmd guard of the
keyboard row reads Core's `DesktopInput.Command`, added at integration). No other feature module.
Spec: `Docs/05-instruments.md` section 2 (`piano`, `curves`); ARCHITECTURE.md 15.1.
The piano's reference checks (press model, labels, shots) are reproduced by the tests below.

## Public contract

| What | Where |
|---|---|
| `PianoModule` (Order 30) | builds `World/Piano`, registers the three services below, logs `[Piano] ready: ...` |
| `IInstrument` | Kind `Piano`, TickOrder 0, hand pointers only; `PlaySurfaceAt` = `PianoKeys` when `InPlayZone && OverKeys`; `PlayedParts` = white keys, black keys, labels |
| `IPianoApi` | `W/BW/BTOP/L`, `Root`, `InPlayZone`, `OverKeys`, `TryGetKey` (x, black, label "C".."B"), `PressMidi/ReleaseMidi(midi, presser)`, `PlaybackOn/Off/ReleaseAll`, `SetOnSurface(bool)` / `ResetSurface()` (MR: hides the stand, tableLift 0.092), `HeldKeyCount` |
| `Rig` | `RigId.Piano`, priority 0, `Group` = the piano root, home (0, .72, .45) yaw 0 (all and single), `Zone` = `InPlayZone`, `Handles` = case, stand (unless hidden), music rest: one `List<Renderer>` filled in `Init` and mutated in place by `SetOnSurface` (section 8) |
| Desktop click target | `PianoInstrument.KeysCollider`: one `BoxCollider` "KeysClickTarget" on `Layers.PlaySurface` (29) over the resting keys, centre (0, -.004, .075), size (1.222, .032, .15) piano-local; no renderer |
| Audio | `PianoLo/Mid/Hi` parented to the piano at (-0.42 / 0 / 0.42, 0.05, 0.05); `IAudioEngine.Piano(midi, vel)` per strike (ASAP), one `VoiceHandle` per key, `Release(h, thud)`; for the new piano also `IPianoPedal.PianoPedal(depth)` / `PianoKeyUp(midi)` (ASAP, same ring, call order) |
| Events | `IRecorder.Add(pianoOn{midi, vel})` on every strike, `pianoOff{midi}` when the damper falls (recording or not: lessons and Sunny observe `RecorderEvent`); a fingertip strike stamps its `HandState.LastPlayMs` (grab guards) |
| Contract members added at integration | `IPianoApi.InKeySpace(world)` (pianoKeySpace), `IPianoApi.OnSurface` (stand hidden); `IsPresent` honours `Rig.InScene` |
| `IContactSurface` (10-upgrade/experience.md 2.1) | `PianoInstrument.QueryContact`: the key top under a point at the key's CURRENT depth, for Core's visual finger clamp (E1) and contact shadows (E3); registered in `Setup`. Visual only |

Presser keys: any object (fingertip pointer, computer key, mouse, a lesson's own object). An API presser keeps state only
while it holds a key (a pooled entry, returned on release), so a caller may pass a fresh object per note; fingertips keep
their contact and history for the session. The press path allocates nothing once warm (tested with object pressers).

## Frame and behaviour (spec 05 sections 2.3-2.6)

Piano-local frame: x right, white key tops y = 0, z = back (distance behind the white-key fronts, = -z of the right-handed frame).
Every spec predicate written in right-handed z was converted once (`PianoAction`): `KeyAt`, `OnBlackTop`, `KeyTopAt`, `WithinKey`,
`DepthPerMetre`, `FingerDepth`, `EntryKey`, `InPlayZoneLocal` (|x| < .641, back in (-.30, .21), y in (-.15, .28)),
`OverKeysLocal` (back > -.12, y > -.02).

- Contacts per fingertip: `Track` (history on the piano clock, 12 entries, 0.1 s), `Follow`, `EntryKey` (a press starts
  only by coming down through a key top inset 1.5 mm), `DownSpeed` over 30 ms, `StrikeVelocity`, early fire on sparse
  tracking (sdt >= 25 ms, s >= .55, v >= .5, predicted s >= .9), glissando (`clamp((speed/2)^.8, .2, .9)`, speed > .15),
  repetition (s < .5 lets the finger go), `Leave`.
- Notes: `PressKey` (a new phrase lifts the automatic pedal: nothing held and > 0.6 s since the last release), `FingerUp`
  (damper pending), `ReleaseKey` (keyboard/mouse/playback: damper at once), `Damper` (pianoOff, rings on), `Damp` (one
  damper thud per pedal lift), MAX_RING 6 s, re-strike releases the old voice with its thud.
- (2026-11-11) The new modal piano's half-pedal wiring (`IPianoSink.PedalDepth` / `KeyUp` / `RestrikesInPlace`,
  `AUTO_PEDAL`, `STEP_DAMP`, the step damp) was deleted with the upgraded piano unit: the piano plays the classic sound
  only.
- Key motion: fingered keys take `KeyShape(finger, .9)` (the escapement hitch), driven keys go down in 45 ms, free keys
  spring back (exact damped oscillator, w 54, zeta .85: damper ~44 ms, rest ~100 ms), the damper falls at depth <= .3.
- Hover: a free fingertip within 5 cm above a key top (inset rule) adds a faint steady glow (gain .45, .07 x .024/.016 m,
  strength 1 - h/.05) at the key, kept behind the printed name.
- Desktop (`PianoDesktop`, ARCHITECTURE section 0), run while `!App.IsPresenting` (never gated on `IsHeadset`): a left press
  belongs to the piano only when the nearest `Layers.PlaySurfaceMask` hit (`Physics.Raycast`, triggers included) is its own
  click target; the key is then picked analytically from the 88 resting key boxes. A click presses at .7, a drag across
  keys plays each at .6 (the drag follows the keys alone), Shift/Alt clicks are left to the rig
  drag. The row A W S E D F T G Y H U J K O L P ; ' plays semitones 0..17 above base 60 at .75 (one presser per physical
  key), Z / X move the base within 24..96; with Ctrl or Cmd held nothing new goes down (the keydown guard: shortcuts
  never play), key-ups are never filtered. The camera look is stopped by `DesktopRigFallback.dragBlockMask`
  (= PlaySurfaceMask, set by AppRoot) on the click target; the piano adds no `dragBlocked` hook.
- The desktop mouse hand's fingertips (`Pointer.FromMouse`) never strike: `PianoAction.Tick` treats them as inactive,
  so a click never fires twice. They may still hover (the glow under the mouse).
- Leaving the room (`OnDisable`, e.g. Layout deactivates the rig): a key still held records its `pianoOff` (a take
  recorded across the layout change never plays back a stuck key), then every presser lets go and every note stops, no thud.

## Contact surface (10-upgrade/experience.md E1 / E3, visual only)

`PianoAction.ContactKey(local, out point, out normal, out drop)`: the key from `KeyAt(x, back)` (the function the contact
logic slides with), back in [0, L], |x - key x| within half the visible width (WW/2 or BW/2) + 2 mm. A point beside a
black key's visible width and below its top is on the white key there (it came down beside the black: never lifted
onto it). The top at the current depth is the key's own transform: pivot (x, 0, L), Euler(-depth MAX_ANGLE, 0, 0), so
point = (x, c top - s zr, L - s top - c zr), normal = (0, c, -s), drop = top - point.y (`KeyTopY(key, back)`).
`QueryContact` returns false when the piano is not present, its rig is held (`IGrabService.HeldRig`) or the point is
outside the play zone or every key; `Tangent` = piano right, `ClipHalf` = WW/2 (11.05 mm) or BW/2 (6.85 mm) while the top
is more than 2 mm down at that point (resting keys share one plane: no clip), about `ClipOrigin` on the key's centre line
(so a press off the middle keeps its shadow on the key: no cut inside it, no spill past its other edge), `Use` Both. The play logic never reads it.

## Rendering (ARCHITECTURE.md 14, 17)

- 8 renderers / draw calls (budget 10): white keys, black keys (two dynamic meshes: each key's vertices are rewritten on
  the CPU with the spec's exact transform, pivot (x, 0, back L), `Euler(-depth * MAX_ANGLE, 0, 0)`, only when its depth
  changes), labels (one 88-quad mesh), case, panel, grille, music rest, stand. No MaterialPropertyBlock anywhere.
  Booted with every stub module the whole scene draws 9 (`DrawBudgetTests`).
- Vertex upload: one `SetVertexBufferData` per run of dirty keys per mesh (neighbours, and two dirty keys with one clean
  key between them, share a call; keys far apart get one call each), so two hands at A0 and C8 send 2 keys (13.8 KB),
  not the 52-key span (359 KB). The label mesh (12.7 KB in all) goes as one span.
- URP Lit ignores vertex colours: the side shading (shadeKeySides) and per-key darkening (1 - .16/.3 depth) ride in
  uv on a 2 x 2 linear palette (`c * t`, reproduced exactly by bilinear filtering); the label tint rides in vertex colour
  on the `Piano/Label` template (URP Particles/Lit, alpha-tested .02, alpha-blended, no depth write, queue 2990).
- Materials: ivory #f4efe3 smoothness .7, ebony #0e0e10 .76, case #131315 .42, panel palette (#303136, #55575c, #37383d,
  #3b3c41) .58 metal .1, grille texture .45 metal .35, acrylic #0a0a0c .8 (no clear coat in URP Lit), stand wood .38.
  Renderers keep `BlendProbes`: the room's reflection probe and Trilight ambient light them.
- Labels: 19 atlas cells (8 x 3 of 128 px), drawLabel's layout (em 10 / 5.9 mm, x1.5 along the key, sharp .62 raised .35
  cap, digit .75 / .7, shrink to 88 % / 92 %, coral bar .66 of the text under middle C). TextMeshPro (LiberationSans,
  bold) measures the pieces and renders their coverage once into a render texture at start (on `Layers.Hidden`, 5 km from
  the room, a disabled camera, one frame); stickers, edge, inks, the bar and the fill bleed are composited on the CPU, the
  24 cells in parallel on the worker pool. Without a GPU (batchmode -nographics) the atlas has no letters. Each build logs
  `[Piano] label atlas ...: font+layout, glyph render+readback, compose, upload` (editor, first boot with GPU: 35 / 133 /
  6.4 / 1.0 ms; compose was 31 ms serial). It runs once per session inside `PianoModule.Init`, i.e. before the first frame
  while the headset still shows its loading screen; moving it into play time would turn it into a visible stall.
- Textures: the grille and the stand wood are Core's shared set (`SharedTextures.Get(SharedTextures.Grille)` = `Core/grille`,
  `SharedTextures.WoodStand` = `Core/wood_stand`); the piano registers no bake spec. Its runtime-only textures are the
  2 x 2 key shade palette, the 4 x 1 panel palette and the label atlas.

## Constants (PianoSpec)

W .0235, WW .0221, L .15, H .02, BW .0137, BL .095, BTOP .012, x0 -.611, TRAVEL .01, MAX_ANGLE .0667 / .084, LEVER_MIN .06,
S_ESC .9, S_RESET .5, S_DAMPER .3, VEL_WINDOW .03, MIN_PRESS_SPEED .07, LOUD_SPEED 1.5, FAST_PRESS_SPEED .5, EARLY_S .55,
SPARSE_DT .025, START_INSET .0015, HOLD_MARGIN .004, RISE 54 rad/s / .85, PRESS_TIME .045, DARKEN .16 / .3, PHRASE_GAP .6,
MAX_RING 6, HITCH .06 / .06 / .025, FELT .94; body top .038, bottom -.092, rear .29, half width .663, floor -.72.

## Files

`PianoModule` (module), `PianoInstrument` (MonoBehaviour: IInstrument, IPianoApi, sink to audio + recorder), `PianoAction`
(all playing logic, plain C#), `PianoSpec` (constants, key table, KeyShape, StrikeVelocity, Spring), `PianoKeysView`
(dynamic key and label meshes), `PianoBody` (case, panel, grille, rest, stand), `PianoMeshBuilder` (rounded box,
beveled extrusion, cylinder, plane), `PianoLabelAtlas`, `PianoTextures` (+ `PianoAssets`: the material template
provider), `PianoDesktop`. `Resources/MaterialTemplates/Piano/Label.mat` is written by `Tools/unity.sh render-setup`.

## Tests

EditMode `Tests/EditMode/Piano`:
- `PianoSpecTests`: key table vs JSON `piano.keys` (x, black, inst, label, atlas cell), constants, travel (10 mm / 8 mm),
  `StrikeVelocity` samples (.07->.10, .2->.38, .5->.62, 1->.84 and the 10 JSON points), `KeyShape` samples (11 JSON
  points, the hitch), spring 90 Hz (damper 44.4 ms, rest 100 ms), label texts and the 19 cells, drawLabel layout, body
  boxes vs `piano.bodyBoxes`, outward faces, key and label transforms at rest and fully down, the vertex upload runs
  (A0 + C8 = two calls of one key each; neighbours one call; what reaches the mesh is what was written), palette = `c * t`,
  no texture of its own (Core's grille / stand wood, `Piano/` template names), no brand names in the sources.
- `PianoActionTests` (the press-model reference checks): 16 silent paths + sliding into a black key's side, every key sounds once, white
  rears, the escapement (white and black), a short touch is silent, velocity monotonic / even dB per doubling /
  repeatable / same at 60-72-90 Hz, fast noisy strokes never double or miss, sparse-tracking early fire, release timing and
  the damper, the key follows the finger (hitch, felt), darkening and hover, chords (adjacent whites under wobble, four
  notes), glissando C D E F G A, glissando velocity at 0.9 m/s (= clamp((v/2)^.8) to 1e-4), a slow slide (0.1 m/s, under
  the .15 gate) and a shallow slide (s .625 < S_ESC) move the contact without striking, automatic sustain (phrase gap,
  one thud), MAX_RING, re-strike, press API, playback ghost, repetition, LastPlayMs, desktop click/drag/keyboard row, the
  Ctrl / Cmd guard (no note or octave change, key-ups still release), the mouse ray, ResetAll (pianoOff for held keys
  only), mouse-hand pointers (`IsMouse`) never strike, API pressers leave no state behind (200 one-note pressers, the
  mouse drag, the playback ghost, ResetAll).

- `PianoContactTests` (10-upgrade E1 / E3): a resting white key is the key-top plane, nothing in front of / behind /
  beside the keys; a key on the felt is tilted about its hinge (10 mm at the front, a third at back .1); black keys and
  the white beside them.

PlayMode `Tests/PlayMode/Piano`:
Every PlayMode fixture boots through `Tests/PlayMode/Core/TestBoot` (fresh `MemorySettingsStore`, temporary `DataPath`)
with only Audio, Lessons (the `IRecorder` stream) and Piano, so no Layout move and no first-launch tour interfere. Sound
is asserted on `AudioTestTap.Calls` (the engine calls the piano made: midi, velocity, ASAP, the source of the handle,
release thud), never on the Audio module's own staged commands.
- `PianoPlayTests` (manual frames, `TestBoot.Step`; FakeHandSource fingers, recorder stream): a scripted finger press
  fires once on PianoMid at the strike velocity, sustains after the lift, the next phrase releases that same voice with
  the thud; hover glow; registration / rig / `Handles` (same list, mutated in place) / zones / audio sources /
  SetOnSurface; press API (register per midi), playback, keyboard row; the desktop click target (layer 29, box size,
  nearest PlaySurface hit owns the press, a Hidden helper does not block, gone with the piano); the desktop look-around
  stops on the keys (`DesktopRigFallback.DragBlocked` through the real head camera, not beside the piano, always with
  Shift / Alt); the press arrives between InputDone and the Instruments phase; leaving the room lets go of everything,
  silently, and records the held key's pianoOff.
- `PianoRealFrameTests`: real Unity frames end to end (one `App.Frame` per yield).
- `PianoClassicPlayTests` (today's piano, the only piano sound since 2026-11-10; real frames, a capture filter after
  each piano SynthSource): a chord, a scale, re-strikes, a top key and the Ode to Joy lesson through the press API reach
  today's voices on the audio thread (no unit handle, a buffer voice per press), no NaN, sane level (the click metric is
  logged); the live call path allocates nothing.
- `PianoAllocTests`: Tick + Hover with presses, damper, sustain and hover, plus a four-key chord from the keyboard row,
  allocate 0 B (Audio + Piano booted only).
- `PianoShotsTests` [Category("Graphics")] (`Tools/unity.sh test-play --gpu`; TestBoot with renderers, the whole app, tour
  marked done, the Room's reflection probe settings and its lamp and fire lights added because the Room stub has none):
  label atlas checks (every cell inked, coral bar size and position, no black clear texels, letter sizes in mm), the
  seat view's C4-C5 key tops lit (luma 0.47 vs background 0.03), and PNGs in `Builds/Shots/piano/`
  (seat view, C4-C5 close-up, middle C close-up, key mid-press x2, side stand, side profile, close seat / seat-wide / rear
  three-quarter, on a table without the stand, the atlas).

## Known gaps / device checks

- Contract requests 1-3 were applied at integration (2026-10-31): `IPianoApi.InKeySpace`, the `SynthSource` start-up
  fix (Audio module), `DesktopInput.Command`.
- The label atlas is rendered at start (first boot ~0.18 s in the editor with a GPU, most of it TMP's and URP's first
  glyph render and the synchronous readback; measure on Quest). It cannot be baked by `Tools/unity.sh bake` (no GPU in
  -nographics), by design (ARCHITECTURE 15.1).
- The music rest's clear coat is approximated by smoothness .8; the case is flat shaded by design.
- The `!App.IsPresenting` gate of `PianoDesktop.Tick` is not covered by a test (no mouse / keyboard injection in PlayMode);
  the logic it guards is (click target, look-around block, keyboard row through `KeyboardFrame`, Ctrl / Cmd).
- Screenshots use the Room stub (floor, moon, Trilight) plus the Room's probe settings, lamp and fire: no walls, so the
  background is dark and the reflections darker than in the full room.
- Device: key feel with real tracker rates (sparse-tracking fire), hover glow legibility, label legibility at 72 Hz with
  foveation, start-up time of the atlas render.

## The many-instrument platform (12-new-instruments/platform.md 3.3, 13.3, 13.5; phase 0)

`PianoPlatform` (`PianoPlatform.cs`, registered by `PianoModule.Init`, `PianoInstrument.Platform`) is a thin forward over
today's piano; nothing else in the module changed.

| Service | Behaviour |
|---|---|
| `IStowable` (Kind Piano) | the piano has nothing to hide on display: `SetStowed(true, mount)` lets every held key go (`PianoAction.ResetAll`, as when its group is disabled) so it rests static; `Stowed` |
| `IInstrumentPlayback` | `pianoOn` -> `PlaybackOn(midi, vel)`, `pianoOff` -> `PlaybackOff(midi)` (Lessons' classic lines, the same calls: tested), `ReleaseAll` -> `PlaybackReleaseAll`, `CloseLane` no-op; other types return false |
| `IGuideAnchors` | `key.<midi>` (21..108): the key's top centre, piano-local (KeyX, 0, L/2) for a white key, (KeyX, BTOP, L - BL/2) for a black one; `keys.middle`: the keyboard's top centre (0, 0, L/2); up = the piano's up. Parsed without allocating. The descriptor lists `keys.middle` and `key.60` |
| `IInstrumentSurface` | `OnSurface` / `SetOnSurface(true)` -> `IPianoApi.SetOnSurface(true)`, `SetOnSurface(false)` -> `ResetSurface()` (mixed reality's real table) |

The sheet-music hook (`PianoMusicRest`, sheet.md 12 item 1), measured from `PianoBody.MusicRest` (EditMode
`PianoMusicRestTests`): `MusicRestLocal` = the board's front face, bottom centre, piano-local (0, 0.03974, 0.23761), up
(0, cos 0.3, sin 0.3), forward into the face (away from the player); `MusicRestLedge` = 0.00655 m, the ledge's top
measured up the face from there (piano-local y 0.046). sheet.md quoted (0, 0.039, 0.2375) and 0.0073: it left out the
board's half thickness (2.5 mm turned by the lean); the page bottom lands on the same ledge top either way.
`MusicRestPose` = that pose in the world, null while the piano is not at the seat (`Rig.InScene` false: out of the layout,
gliding, on display; disabled; or `Stowed`). `PianoDefinition.SheetAnchor` = `MusicRestLocal`.

Conformance check 3 (`Tests/PlayMode/Piano/PianoPlatformTests`): the descriptor's boxes cover `Rig.Handles` (case, stand,
rest) within 3 cm; measured margins: x -0.003 / -0.003 (the case is 1.326 m wide, the box 1.32), y +0.000 / +0.062,
z +0.016 / +0.005. The homes equal the descriptor's.
