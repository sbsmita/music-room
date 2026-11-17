# Handpan: phase B notes (onto instruments-base, wired into the registry)

Phase A (its own copy of the project) built and tested everything that does not need the platform. Phase B
(2026-11-01, a copy of the instruments base project) moved the owned folders in and wired them:

- `Assets/MusicRoom/Handpan/**` (asmdef `MusicRoom.Handpan`: instruments-base's asmdef and `.meta` GUIDs kept, the folder
  `.meta`s kept, `allowUnsafeCode` on), `Assets/MusicRoom/Audio/Sound/Handpan/**`, `Tests/EditMode/Handpan/**`,
  `Tests/PlayMode/Handpan/**`, `Tests/EditMode/Audio/Handpan/**` (incl. `Reference~/`); the screenshots
  `Builds/Shots/handpan/phase-b/`.
- Deleted: `Audio/Sound/Handpan/HandpanStubUnit.cs` (the stub's `IHandpanSound` with Knock / Hush and its partials).
  Replaced: `HandpanDefinition.cs` (class and id block kept), `HandpanModule.cs` (class and `ModuleOrder` 33 kept), the
  lesson pack, `README.md`, `CONTRACT-REQUESTS.md`, `Tests/PlayMode/Handpan/HandpanFixture.cs` (class name kept).

## 1. What was wired

| Contract | The handpan |
|---|---|
| Descriptor | `HandpanDefinition`: seat Front (0, 0, 0.38) yaw 0 = Single, prefs FL FR L R F, priority 6, reach 0.62, lift -0.10..0.15; two seat boxes (below); display `left-wall-bracket` (wall, (-3.06, -0.62), yaw -90, "On the left wall") with its box; portrait (the hanging shell seen from the front, a little left and above); audio unit 5, caps 0.15 / 0.25 ms, arena 0.18 MiB, ER origin and the one source 18 at the shell centre (0, 0.793, 0.028); lessons travel 0.30 / lead 1.67 s / Disc / Treble / "notes"; shadow; events; 9 tips; 10 features; 28 anchors; draws 3 / 2, triangles 16,000 / 3,600 (measured 15,288 / 3,292) |
| Module | `HandpanModule` (Order 33) builds `HandpanInstrument` under `app.WorldRoot` at the seat home and registers Rig (handles = the band and the stand, one list forever), IInstrument (TickOrder 21), IStowable, IInstrumentPlayback, IGuideAnchors, ILessonTargets, IContactSurface, IHandpanApi; the source is parented to the shell (under the rig: conformance 14) at its centre + 6 cm |
| Unit | `HandpanHooks.cs` (the two partials), `HandpanUnit` with idle skip, Share x Budget scaling its caps, `u->Work`, `RecordUnit` on every public method, `ILoudnessReference` |
| Play | `Tick`: `HandpanHandFeed` (the mouse hand is never fed) -> `HandpanPlay` -> `Dispatch`: the unit, the settle, `TakeEventHandpan` -> the recorder, `PlayStamp.Mark`, `HandpanTipCounters` -> `InstrumentSignalEvent`. Only at the seat, present and not blending |
| Recording | `handpan` (Onset, key Index, **not Held**: a struck field rings through a loop boundary; nothing is sent to it at the window end), `handpanMute` (Release, Index; vel = strength, 0 = lifted). Fields: `TakeEventHandpan` |
| Playback | `Play(e)`: the strike (a tak recomputes its neighbours from (u, pos)), the settle when at the seat; mutes / lifts; false for other types. `ReleaseAll` lifts every damp; `CloseLane` has nothing to close (no held notes) |
| Lessons | `TryGetLanding` (the block path of handpan.md 8.1, `approach` a unit vector from the start to the apex), `Travel(t, travel)` = min(travel, the path's room: 0.135 m for the near fields), `Matches` (a live `handpan` onset on the target's field), `ShowTarget` (no visual: the disc block is the cue; the last k per field is kept for tests), `Hint` ("Tap the ding", "Tap A3", ...) |
| Sunny | 9 `TipSpec`s (arrival P2 `Tap` at `handpan.above.1`, success any `handpan` onset; the mute tip `PalmRest`; the slap tip `Slap`; the loop tip no demo), signals published by the instrument (`handpan.lift` from the play logic's hint; `noDing`, `hard`, `centreOnly`, `ringing`, `noSlap`, `noTak`, `loopIdle`; successes `ringFree`, `dingStrike`, `soft`, `edgeStrike`, `slapStrike`, `takStrike`); voice manifests and lines (`tip_handpan_arrive` renamed from `tip_handpan`: the registry wants `tip_<id>_` ids) |
| Stowing and the glide | `SetStowed(true, Wall)`: a resting hand's damps are lifted, the play logic reset, the display configuration (the light shell, the bracket, no stand, the collider off: 2 draws). On `InstrumentMoveEvent` Leaving: from the wall the shell eases down onto the stand over 0.85 s (inside the shortest glide, 0.9 s) and the stand shows once the rig is 35 % of the way (its legs reach 0.30 m behind the rig origin, into the wall at the spot); from the seat on a real glide the shell eases into its hanging pose. A page turn's slide-out is not a glide: `SetStowed` hangs it when it is Displayed |
| Room | the source's room sends come from `AudioSourceTable` (handpan 0 dB); the ER origin is the descriptor's; a displayed handpan that sounds gets an ER group for 2 s (AudioPresenceTests) |
| Desktop | a left click on the top cap's collider (Layers.PlaySurface) strikes vel 0.75 at the hit point, once per press (`Physics.SyncTransforms` first, as the harp); the mouse hand never strikes (conformance 16) |

## 2. Footprint and display (the planner data: CONTRACT-REQUESTS R5)

| Box (rig-local cx, cz, hx, hz, y0..y1) | What |
|---|---|
| (0, 0, 0.272, 0.240, 0.62, 0.88) | the shell and its band, the rim's centre 0.74 m up, tilted 28 degrees toward the player |
| (0, 0.1875, 0.315, 0.1275, 0, 0.62) | the cradle tripod below the rim: z 0.06..0.315, i.e. beyond the knee box (seat-frame z 0.41 + 3 cm) |
| display (0, 0, 0.275, 0.12, 1.25, 1.86) | hanging on the bracket, the box stopping at the planner's 2 cm wall inset (the bracket's plate behind it) |

The stand replaces the platform's set-back placeholder box (0, 0.18, 0.18, 0.12, 0..0.62). The real stand is a three-legged
cradle: the near legs splay to the sides (feet at x +-0.30, z 0.13), the far leg goes back (foot at z 0.30), and the arms
rise from elbows above the knees' height ((+-0.175, 0.618, 0.072): 2 mm under 0.62 m, inside the stand box, which starts at
z 0.06) to three felt-padded rests under the shell. Conformance check 3: handles x -0.313..0.313, y 0.001..0.876,
z -0.242..0.313 inside the boxes within 3 cm.

**The display spot is unchanged** (`left-wall-bracket`, (-3.06, -0.62), yaw -90): the shots (`display-wall-wide`,
`display-room`) show it on the plain wall between the bookcase and the floor lamp, clear of the plants (the nearest, on
the bookcase, is about 0.8 m away); the ivy is at the window on the other side of the room. `StowPlanTests` validates it
against the furniture, the other spots and every fitting seat plan.

## 3. Decisions made in phase B

1. **Loudness parity: a 3 dB trim on the graph path** (`HandpanDsp.GraphTrim`). The lab matched `OUT_GAIN` to the harp's
   single reference stroke; the platform's rule is the piano's mf phrase (-24.15 LUFS), and the same four-note phrase on
   the handpan rings on: +3.6 LU untrimmed, +0.62 LU trimmed. The trim follows the soft clip and leaves the reference
   port bit-exact (every vector and listening render unchanged; the renders are matched to -18 LUFS anyway).
2. **The first lesson is 13.3 s** (14 notes: D3 A3 D3 C4 D3 A3 C4 D3 / A3 C4 D4 C4 A3 D3, 72 bpm): LessonPackTests asks
   12-21 s of lesson; phase A's 7.5 s counted the countdown and the lead.
3. **The display mesh level** (18 x 64 top cap, no lip or inside) and the steel's metallic/smoothness map at 1024 x 512
   keep the platform's budgets (25 k / 8 k triangles, 4 / 2 draws, 3 MB of textures).
4. **The baked smoothness alpha:** the first GPU shots showed a mirror-like shell; the bake had dropped the alpha of
   `steel_ms` (`TextureBakeSpec.HasAlpha` was not set). Fixed and guarded by `HandpanBakeTests`.
5. **The desktop collider faces up** (its triangles were wound downward: a mesh collider is hit from its front only).

## 4. Device checks (still open: nobody has played it on a headset)

1. **Feel (T-HP-FEEL)** and **velocity (T-HP-VEL), twice: Fast Motion Mode off and on** (review item 10). Turn FMM on
   (the `FastMotion` trait) only if it wins on missed taps, false strikes while hovering and velocity SD.
2. **Aim (T-AIM)**: 20 taps each on A4 and C5, >= 95 % on the intended field.
3. **Headroom**: the 10-finger ff roll plus a ding slap: no crackle (the soft clip, then the 3 dB trim).
4. **CPU**: the worst case with passthrough: the handpan's `Work` and DspLoadMeter p99.
5. **Comfort, three seated testers**: no thigh contact on the near fields (0.74 m), no shoulder strain on A4 / C5, the
   cradle's near legs clear of the shins; in My set the Right / Left slots (the planner puts it there with the piano).
6. **Lesson blocks**: "First notes" seated, every block over its field from its first frame.
7. **Looper**: 2 bars looped 20 times: a ringing ding is NOT cut at the loop's end; taks with their neighbours.
8. **MR**: the cradle reads as a stand in front of the lap; nothing under the knees.
9. **The glide** on the headset: the shell easing off the bracket and onto the stand, the stand appearing mid-glide.
10. **Listening** (the user): the tone A/B (`HandpanVoiceChoice.cs`, calm is the default) and the loudness against the
    piano and the harp in the room (the 3 dB trim).

## 5. Regenerating anything

| Changed | Run (in the handpan lab, Node) | Copy to |
|---|---|---|
| The sound design (`handpan-engine.mjs` K or a voice) | `node gen-cs.mjs && node test-vectors.mjs && node export-phrases.mjs` (+ `node render.mjs`) | `out/HandpanTables.g.cs` -> `Audio/Sound/Handpan/`; `out/test-vectors.json`, `out/vec-*.f32`, `out/phrases.json`, `out/bench.json` -> `Tests/EditMode/Audio/Handpan/Reference~/` |
| The layout or the play logic | `node interaction-tests.mjs && node replay-vectors.mjs && node lessons.mjs && node export-pack.mjs` | `out/interaction-vectors.json`, `out/interaction-replay.json`, `out/handpan-lessons.json` -> `Tests/EditMode/Handpan/Reference~/` (export-pack writes the pack into this folder) |
| Sunny's lines | edit `voice/handpan-lines.mjs`, `node voice/export-voice.mjs` (writes `Resources/Voice`, `GuideLines`, `VoiceManifests` here), then the tip's `Title` in `HandpanTips.cs` | |
| The textures | `Tools/unity.sh bake` | |
