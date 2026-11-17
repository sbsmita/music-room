# Handpan (module `MusicRoom.Handpan`)

A 9-note handpan for bare hands, played seated: the ding D3 in the middle and A3 C4 D4 E4 F4 G4 A4 C5 around it (D
"Celtic minor": all white keys, so it plays along with the room's C-major piano, harp, lessons and loops). Tap a note
with a fingertip (the dimple's centre is pure, its edge richer, the field's edge a tick), tap the steel between the notes
for a tak, slap the ding with a flat hand for the bass boom, rest a finger or a palm to quiet a note. It stands on a low
three-legged walnut stand in front of the knees, rim 0.74 m up, tilted 28 degrees toward the player. Design:
`Docs/12-new-instruments/handpan.md` (revision 2); lab: the handpan reference (Node).

**Status: phase B (2026-11-01): wired into the platform.** The module registers the instrument with the registry
(`HandpanDefinition`: the real footprint, the wall-bracket display, events, tips, features, anchors, budgets), the unit
owns the unit-only role `UnitId.Handpan`, Layout brings it to the seat and hangs it on its bracket, the recorder, the
looper, the tutorial and Sunny drive it. The conformance suite passes for it. `PHASE-B-NOTES.md` records what changed in
phase B; `CONTRACT-REQUESTS.md` lists the platform tests that assumed the stub (one patch in `Shared~/`) and the planner data.

## Files

| File | What |
|---|---|
| `HandpanSpec.cs` | The instrument as data (handpan-layout.mjs): shell, the 9 fields (ellipses in the cap's geodesic map), placement, stand, lesson constants |
| `HandpanMath.cs` | Pure geometry in double: map coordinates, `HitField`, `TakNeighbours`, `Relief` (+ gradient), `CapPoint`, `ToMap`, `ContactOf`, the placement transform (`HandpanPlace`), the velocity curves, Helmholtz |
| `HandpanPlay.cs` | The bare-hand play logic, handpan-interaction.mjs line for line, allocation-free: strikes from least-squares slopes over distinct pose samples (45 / 70 ms windows, no sample interval), arming, debounce, the tak, palm slaps, flat-hand landing, finger and palm mutes (0.10 s placed, 0.40 s after sounding), tracking loss, teleports, the lift hint |
| `HandpanHandFeed.cs` | What the module feeds `HandpanPlay` from the app's real input: `Pointer.Tip`, the frame clock, `HandMath.Palm`, the flags; never `HandFrame.sampleInterval` (review items 8 and 15) |
| `HandpanGeometry.cs` | Procedural meshes, two levels: at the seat the shell (top cap 40 x 128 with the relief, bottom cap, the gu's rolled lip, dark inner surfaces), the rubber edge band and the walnut cradle tripod (three legs with elbows, arms up to three felt-padded rests under the shell): 15,288 triangles, 3 draws; on display a lighter shell (no lip, no inside), the band and the wall bracket (plate + two hooks) in the band's mesh: 3,292 triangles, 2 draws |
| `HandpanTextures.cs` | Generated textures: the steel atlas 2048 x 1024 (colour and normal: hammer dents, tone rings, burnished dimples, heat tint; metallic/smoothness at 1024 x 512) and the stand atlas (walnut + felt). `ITextureBakeProvider` (names `Handpan/*`; `Tools/unity.sh bake` writes them to the shared Generated folder; the smoothness map with its alpha) |
| `HandpanModel.cs` | The scene objects (3 draws: steel, rubber, stand), `Config` Seat / Wall / Surface (`SetConfig`: the display meshes, the stand hidden, the collider off) and the glide blend (`BeginMove`: the shell eases between the bracket and the stand in 0.85 s, the stand shows once the rig is clear of the wall), `HandpanMaterials` (templates `Handpan/Steel`, `Handpan/Stand`), `HandpanMotion` (the settle on the felt, 12 Hz, zeta 0.5), `HandpanContactSurface` (E1 finger stop and E3 shadows, at the seat only), the desktop collider (32 x 64, layer 29, faces up) |
| `HandpanLessonPath.cs` | The lesson block path (handpan.md 8.1): in along the line of sight, never closer than 0.26 m to the eyes or 0.16 m to their plane |
| `HandpanTips.cs` | Sunny's 9 tips as the platform's `TipSpec`s, the features (`FeatureSpec`, isolation passive with its reason), the signals, the anchors list, `HandpanAnchors` and `HandpanTipCounters` (the triggers' counters) |
| `HandpanDefinition.cs` | The registry descriptor (Kind / Rig / Unit 5, source 18, carousel 4, Order 33, TickOrder 21): seat Front (0, 0, 0.38) with its two boxes, the wall-bracket display, portrait, audio caps, lessons, shadow, events (`handpan` Onset by Index, not Held; `handpanMute` Release by Index), tips, features, anchors, draws and triangles |
| `HandpanModule.cs` | `HandpanModule` (Order 33) and `HandpanInstrument` (IInstrument, IHandpanApi, IInstrumentPlayback, ILessonTargets with the Travel cap, IGuideAnchors, IStowable, IContactSurface): the real hands -> `HandpanPlay` -> the unit, the settle, the recorder (`TakeEventHandpan`), `PlayStamp`, Sunny's signals; the desktop click; playback; the glide; `HandpanZone` |
| `IHandpanApi.cs` | The instrument's own API (in this assembly: nobody else needs it in Core) |
| `Resources/LessonPacks/handpan.json` | The lesson pack (LessonFile schema): First notes (a 13.3 s win, 14 notes on D3 A3 C4 D4), Ode to Joy, Twinkle Twinkle |
| `Resources/Voice/tip_handpan*.wav`, `Resources/VoiceManifests/Handpan.json`, `Resources/GuideLines/Handpan.json` | Sunny's 9 lines, rendered by the lab's voice export (the tip_arrange / deck_guide pipeline: eSpeak NG Annie, 22.05 kHz mono, robot colour added by the app) |
| `Resources/MaterialTemplates/Handpan/*.mat` | Written by `Tools/unity.sh render-setup` from `HandpanMaterials` |
| `README.md`, `PHASE-B-NOTES.md`, `CONTRACT-REQUESTS.md` | This, what phase B did, the shared-test requests |
| `Shared~/` | Not compiled: the patch of CONTRACT-REQUESTS R1, R2 and R4 against instruments-base (the shared layout tests' versions) |

The sound unit is in `Assets/MusicRoom/Audio/Sound/Handpan/` (its own README).

## Tests

| Where | Count | What |
|---|---|---|
| `Tests/EditMode/Audio/Handpan` | 26 | DSP vectors bit-exact (exact path) and within 1e-5 (Burst): tables of both voices, strikeParams, shapes, TV1-TV12; the six listening phrases nulled against the Node WAVs and written to `Builds/Listen/handpan/`; block cost; zero allocation; the soft clip; the graph hook (`RenderUnit`, chunked blocks, Reset, the 3 dB trim); the float-rounding probe; on the platform's graph (`HandpanPlatformUnitTests`): the role creates `HandpanUnit`, idle skip and the first sample after it, Share 0.4 = 42 % of the work, the test tap |
| `Tests/EditMode/Handpan` | 52 | the 34 scripted-hand cases (each against the Node replay exactly and the spec's expectation within 1 frame / 0.005), 30 Hz vs 90 Hz, velocity curves, zero allocation, the lesson block paths, geometry; the lesson pack's rules; Sunny's lines (clip, length, words, brands, manifest), the tips' captions = the lines, features and anchors; each tip's words against the real behaviour; the baked smoothness alpha |
| `Tests/PlayMode/Handpan` | 7 + 1 GPU | the real input path (FakeHandSource -> PointerInput -> HandpanHandFeed -> HandpanPlay): a tap reads 0.635 at 90 Hz and with 30 Hz poses; untracked hands never strike; zero allocation. The module in the booted app (`HandpanModuleTests`): the desktop click plays once; live strikes record `handpan` onsets (not Held) and a resting finger a `handpanMute`; playback of a tip, a tak, a mute and its lift reaches the unit; Sunny's signals; the source under the rig; on display a resting hand's damps are lifted and nothing plays. `HandpanFixture` for the conformance suite (an index stroke onto A3). GPU: `HandpanShotsTests` (below; also the draws 3 / 2 and the slap settle) |
| Platform suites | | `InstrumentConformanceTests` (6 cases for the handpan), `RegistryTests`, `RegistryLintTests`, `SunnyCoverageTests`, `LessonPackTests`, `LoudnessParityTests` (-23.53 LUFS, +0.62 LU), `StowPlanTests`, `GenericTutorialPlayTests` (now on the real handpan) |

`TEST_FILTER=Handpan Tools/unity.sh test-edit`, `TEST_FILTER='Handpan|InstrumentConformanceTests' Tools/unity.sh test-play`,
and with the GPU lock `TEST_FILTER=HandpanShots Tools/unity.sh test-play --gpu`.

## Pictures

`Builds/Shots/handpan/phase-b/` (`HandpanShotsTests`, the whole app, Layout placing
it): on display `display-room`, `display-close`, `display-side` (the bracket's hooks), `display-wall-wide` (the spot
between the bookcase and the lamp), `display-from-seat`; the menu `portrait-handpan`, `portrait-menu-grid-canvas`,
`portrait-menu-grid-seated`; the glide from the wall to the seat in piano + harp + handpan `glide-midway`,
`glide-midway-close`; at the seat in that set `set-seat`, `set-seat-wide`, `set-three-quarter`, `set-behind`,
`set-handpan-side` (the cradle), `set-finger-on-a3`. Phase A's close-ups (the dimples, the gu, the finger's shadow) are
in the folder above.

## Sunny (handpan.md 8.2, revised)

| Tip | Line | Covers | Trigger -> success |
|---|---|---|---|
| `tip_handpan_arrive` (P2) | "Tap the round notes gently with your fingertips." | `handpan.strike` | first arrival -> a `handpan` strike |
| `tip_handpan_lift` (P2) | "Tap, then lift quickly so the note rings." | `handpan.lift` | the play logic's hint (3rd note muted by its own finger within 0.5 s) -> notes ringing free |
| `tip_handpan_ding` | "The deep note in the middle is the ding." | `handpan.ding` | 5 ring strikes, no ding -> a ding strike |
| `tip_handpan_gentle` | "Gentle taps sound sweeter, hard taps brighter." | `handpan.gentle` | 3 strikes > 0.9 in 10 s -> soft strikes |
| `tip_handpan_spot` | "Tap near a dimple's edge for a richer note." | `handpan.spot` | 10 centre-only strikes -> an edge strike |
| `tip_handpan_mute` | "Rest your hand on the steel to quiet it." | `handpan.mute` | 4 fields ringing for 3 s (2nd time) -> a `handpanMute` (demo `PalmRest`) |
| `tip_handpan_slap` | "Slap the middle with a flat hand for a boom." | `handpan.slap` | 20 strikes, no slap -> a ding slap |
| `tip_handpan_tak` | "Tap between the notes for a soft click." | `handpan.tak` | 30 strikes, no tak -> a tak |
| `tip_handpan_loop` | "Play the handpan over your loop. It's in tune." | `handpan.loop` | a loop plays, handpan unplayed -> a strike during the loop |

`handpan.isolation` (harmonic isolation) is Passive: an advanced technique that emerges from the finger rest. Every line
is at most 10 words and 3.55 s with the lead. Changes from the spec: the arrival line names the notes and the fingertips
(the spec's "round steel drum" read as the whole instrument); "gentle" no longer claims soft taps ring longer (the model's
decay does not depend on velocity; review item 11) and was shortened to fit 3.6 s; "mute" says "hand", because a resting
finger mutes as well as a palm. `HandpanTipTruthTests` checks each line against the play logic or the sound.

Anchors (`HandpanAnchors`, the platform's `IGuideAnchors` ids): `handpan.field.<i>` (dimple apex, up = the field normal),
`handpan.above.<i>` (8 cm above it), `handpan.edge.<i>` (the dimple edge, u 0.35), `handpan.tak` (the steel between A3
and C4).

## Deviations from handpan.md revision 2 (each with its evidence)

1. **Placement 0.74 m (was 0.72)**, review item 9: the critique's `c4-geometry.mjs` sweep at (0, 0.74, 0.38), 28 degrees:
   the near fields' edge clears a 95th-percentile thigh (+0.4 cm instead of -1.6 cm), the sweet spot by 4.5 cm (was 2.5),
   and the far fields are closer to the shoulder (edge points 0.594 m, was 0.599). The lab's `PLACE`, the interaction
   vectors, the lesson paths and `geometry.json` were regenerated: same events, every acceptance criterion passes, every
   block start still >= 0.26 m from the eyes and >= 0.16 m in front of their plane.
2. **A cradle tripod instead of the ring stand** (phase B, to fit the planner's knee box): the near legs splay to the
   sides (feet at x +-0.30 m, 0.13 m ahead of the rig origin), the far leg goes back (0.30 m), and arms rise from elbows
   at knee-box height to three felt-padded rests under the shell, so nothing of the stand below 0.62 m is nearer the
   player than 0.06 m ahead of the rig origin (the knee box + its 3 cm gap); the space under the near rim stays free.
3. **No shimmer** (review item 12): feedback is the settle on the felt, the finger stop and the contact shadows.
4. **A safety soft clip above 0.95 full scale** (review item 16), in the Node reference too: no vector or render changes.
5. **The play logic's contact leak fixed** (found while porting): a finger resting (damping) that rose less than 12 mm and
   came down again replaced its contact without releasing the damp, so that hand's slot stayed held on the field. Both the
   C# and the Node reference now release first; all 34 vectors are unchanged.
6. **The tak's "down 40 dB within 80 ms"** is measured as the spec says (5 ms RMS re its peak): -41.8 dB at 80 ms.
7. **Loudness: the graph path is 3 dB below the lab's** (phase B; `Audio/Sound/Handpan/README.md`): the platform's mf
   phrase parity with the piano (+0.62 LU) instead of the lab's single-stroke match with the harp.
8. **A display configuration** (phase B): on the left wall the shell hangs upright on a two-hook bracket, its notes facing
   the room, with a lighter mesh and no stand (2 draws, 3,292 triangles); the menu portrait is taken there.

## Review items the revision never saw (critiques/handpan-unaddressed.md)

| # | Item | Done |
|---|---|---|
| 8 (medium) | `sampleDt` ambiguity | Fixed at the source: `HandpanPlay` has no sample-interval input; `HandpanHandFeed` feeds `Pointer.Tip` + the frame clock + `HandMath.Palm`. Proven on the real input path (PlayMode): a 0.6 m/s tap reads 0.635 with the predicted pose at 90 Hz and with 30 Hz repeated poses alike; EditMode `predicted-90hz` vs `tracker-30hz` within 0.05 |
| 9 (medium) | near fields too low | Fixed: placement 0.74 m (above), legs away from the knees; MR's ring over the real lap stays a device check (PHASE-B-NOTES, device 8) |
| 10 (low) | Fast Motion Mode off | Kept off by default (not in the descriptor's traits), with the evidence for it: at 30 Hz the hard taps read lower but none is missed (lab `hands-acceptance` 8), while at 2 mm jitter (FMM adds jitter) 5 % of soft taps are missed and slow placements give 11 false strikes per 100. Device check T-HP-FEEL / T-HP-VEL to run twice, FMM off and on (PHASE-B-NOTES); turning it on is one trait |
| 11 (low) | "Softer taps ring longer" false | Fixed: "Gentle taps sound sweeter, hard taps brighter." (the model: centroid 362 -> 382 Hz soft -> hard, tested); all 9 lines rendered, seconds and lineSeconds recorded, each < 3.6 s; `tip_handpan_lift` exists |
| 12 (low) | 23 Hz shimmer | Dropped (no code): a 3 mrad tilt at 23 Hz reads as flicker (the harp's E8 precedent replaced a flicker with a blur), the eye cannot see 147-523 Hz vibration of ~50 um, and it would need a custom Lit variant. The physical feedback stays |
| 13 (low) | the ding's missing D4-octave path | Not built, with evidence: measured on the unit, the ding at the apex (vel 0.6, 10-100 ms) has octave -3.4, fifth -18.4, 4 f0 -48 dB re f0, as the critic found. Adding the path changes the ding's colour before the user has listened to either voice (the user rejected a brighter "upgrade" once); the measured voice already couples the octave pairs harder (-18 dB). Ready to add as a design option after the A/B if the ding sounds dull: drive each field's SYM octave lane from its SYM fundamental (one-way), calibrate in lanePeak to about -15 dB, check with TV6 |
| 14 (low) | decays 15-30 % long | Handled as a listening choice, not a silent change: the lab can shorten the fundamental's T60 reference per voice (`T60_F0_REF`, variant `calm-shorter` at 4.8 s: three-mode means 2.97 s overall, low 3.68, mid 2.92, high 2.56, against the published 2.9 / 3.2 / 3.0 / 2.5), rendered as `handpan-singles-shorter.wav` next to the A/B; the shipped tables are unchanged. The spec's 3.4 should read "at the top of the published range", not "within" |
| 15 (low) | contract details | Hover: no glow; AcceptsStickPointers false; TickOrder 21 (after the harp); PlaySurfaceAt the top cap -> PlaySurface.Instrument; desktop collider 32 x 64 on layer 29, one strike per click (`HandpanModuleTests`); hand data through `HandpanHandFeed` (HandInput's HandFrame flags, HandMath.Palm). In `HandpanModule.cs` |
| 16 (low) | +11.5 dBFS relies on the limiter | Fixed: the soft clip above 0.95 FS; the worst case (9 fields ff + a ding slap) peaks at 1.000, every vector and render unchanged |

## Open for the user

- **The tone A/B**: `handpan-singles.wav` (calm, default) vs `handpan-singles-measured.wav`; one line in
  `Audio/Sound/Handpan/HandpanVoiceChoice.cs`. Optional third file: `handpan-singles-shorter.wav` (shorter decays).
- Nobody has heard the handpan on the headset yet; the device checks are in PHASE-B-NOTES.md.
