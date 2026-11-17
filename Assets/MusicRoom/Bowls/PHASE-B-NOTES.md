# Singing bowls: phase B notes

Phase A ran in its own copy of the project; phase B moved the bowls' folders into a fresh copy of the instruments base
project and wired them into the platform. Read with `README.md`, `CONTRACT-REQUESTS.md` (R1-R8) and
`../Audio/Sound/Bowls/README.md`.

## 1. What phase B did

| Step | Done |
|---|---|
| Folders | `Bowls/**`, `Audio/Sound/Bowls/**`, `Tests/EditMode/Bowls/**`, `Tests/EditMode/Audio/Bowls/**`, `Tests/PlayMode/Bowls/**` copied in; the stub's asmdef, its `.meta` GUID and the folder `.meta` GUIDs kept; the stub unit deleted; the test asmdefs already list `MusicRoom.Bowls` (phase A's shared edit dropped) |
| Unit | `BowlsUnit`: `CreateBowls` / `RenderBowls` (`BowlsDsp.RenderUnit(u, u->Share)`), `UnitId.Bowls` on `SourceId.Bowls`, idle skip, every call `RecordUnit`, `u->Work`, the work cap from Budget / 1200 × Share, `ILoudnessReference`; the Strict render pointer (R6 accepted); `IUnitWorstCase` for the load test (R8) |
| Descriptor | `BowlsDefinition`: Placeholder false, FrontLeft boxes, the sill spot (3.09, -0.62) yaw 90 (R8 of the integrator: clear of the ivy, see CONTRACT-REQUESTS R1), 7 event types, 14 TipSpecs, 11 features, 13 anchors, 5 / 2 draws, 9,700 / 4,300 triangles, `ShadowSpec.HiddenInMr` |
| Module | `BowlsModule` (Order 37) + `BowlsInstrument`: IInstrument (TickOrder 24; hand frames in the rig's frame; mouse hands skipped), IStowable, IInstrumentPlayback, IGuideAnchors, ILessonTargets, IContactSurface (the stand top: shadows), IInstrumentSurface, the desktop click / circle-drag on `Layers.PlaySurface` colliders, PlayStamp, `InstrumentSignalEvent` from the coach |
| Stick | `BowlsStickTool`: the platform's hand tool (`IHandTool`, `IPointerOwner`, `IHeldBy`), two tool pointers (`Striker` suede, `RubStick` shaft) registered with `RegisterExternal` and moved in `StickPoses`; `BowlsToolProvider : IHandToolProvider` for Layout's grab pass; grip / settle / shaft distance through `HandToolMath`; a holding hand's fingertips do not play |
| Rubs | Recorded through Core's `ControlEncoder` (`BowlsRubEncoder`: level = N / 8, pitch = the rub speed); played back through one `LaneArbiter` per bowl |
| Sunny | `TipSpec`s with `FingerCircle` demos, two two-step demos (Tap then FingerCircle; Tap then OpenHand), the lesson tips renamed `tip_bowls_lesson*` (LessonStart / LessonHit); voices re-metaed from `tip_arrange.wav.meta` (new GUIDs); `VoiceManifests/Bowls.json` in the Guide's shape |
| Lessons | Strike targets hit by a `bowlHit` marker (the tutorial counts Markers when an instrument declares any), rub targets by `bowlSing` at the hold; a tap before a rub is no mistake; 15 s stall: any touch on the target bowl plays an audible 0.7 m/s strike and counts |
| Visuals | The stick has its own renderer (stays in MR); the cushion cloth moved into the Wood atlas (one template less); the stand renderer swaps to a cushions-only mesh on a real table; each bowl's turn is baked into its mesh (axis-aligned bounds inside the seat boxes: otherwise the placement solver pushed the rig 2.9 cm) |

## 2. Final footprint and display spot

- Seat (FrontLeft, home (-0.46, 0, 0.25) yaw 0, PlayCentre (0, -0.016)): stand (0, 0, 0.205, 0.145, 0..0.61); bowls,
  cushions, stick (0, 0, 0.205, 0.145, 0.61..0.72). In the default set piano + drums + bowls the bowls land at
  (-0.460, 0, 0.150) world, slot FrontLeft (to 1e-4 of the plan).
- Display: `window-sill`, Surface, (3.09, 0, -0.62) yaw 90, box (-0.01, 0, 0.255, 0.09, 0.63..0.735). Measured world
  bounds of the displayed bowls: x 3.002..3.177, y 0.629..0.734, z -0.822..-0.368 (the sill: x 3.00..3.20, z -2.23..-0.13;
  the ivy's leaves reach about z -1.58; the near curtain starts at z -0.12).

## 3. Shared files in this clone

Only the theremin's versions of the shared PlayMode tests (adopted as the integrator asked: `Layout/InstrumentView.cs`,
`DisplayArrangeTests`, `GlideTests`, `RaySummonTests`, `SetLayoutTests`, `Core/PointerClaimsTests`,
`Audio/EightInstrumentsLoadTests`), with two local differences in `EightInstrumentsLoadTests`: the theremin branch fenced
with `#if THEREMIN_REAL` (the theremin is still the stub here) and the one `IUnitWorstCase` line (R8). Everything else
outside the bowls' folders equals instruments-base (`diff -rq` checked; `Main.unity` from the `scene` step restored: it
differs only in regenerated file ids; no `MetaXRAcousticMaterialMapping.asset`). R1's vectors and R2 were applied only
for the runs below and reverted.

## 4. The chain (2026-11-01, the phase-B copy)

| Step | Result |
|---|---|
| compile | 0 errors |
| render-setup | 50 templates (the bowls' `Bowls/Bronze`, `Bowls/Wood` in `Bowls/Resources/MaterialTemplates/`) |
| bake | 35 textures (none of them the bowls': theirs are generated at start-up) |
| scene | `Main.unity` written (restored afterwards, see section 3) |
| test-edit | 1,100: 1,097 passed; the 3 failures are `UnitShareTests` (the knock in the Bowls slot: R2). With R1's vectors (applied for the run). Without them `StowPlanTests.TheLiveDescriptorsSpotsAreClearOfEverything` fails too ("bowls x 3.08 vs 3.09") |
| test-play | 405 / 405 (with the theremin's shared tests; includes `InstrumentConformanceTests` for the bowls: all six cases, and `BowlsPlayTests`) |
| test-play --gpu (lock) | 439 / 439 (includes `BowlsShotTests`, `DrawBudgetTests`, the layout and portrait shots) |
| test-perf (lock) | 16 / 16. Bowls DSP worst (all three rubbed, chatter) through the Strict pointer avg 29.9 µs, p99 34.7 µs, max 42.4 µs per 256-frame block (Fast 24.6 / 34.1 / 40.9); the descriptor's caps 0.15 / 0.25 ms. Eight instruments with the bowls driven at their worst (R8): avg 437 µs, p99 627 µs, worst 664 µs per block (limit p99 2.1 ms) |

Bowls tests: EditMode 138 (`BowlsPlatformUnitTests`: Share 0.4 = 49 % of the work, Budget 600 = 66 %; `BowlsTailRuleTests`;
the rub encoder: the lab's 425 frames give 144 ControlStream events, 24.5 per sounding second, and a take of a rub plays
back 0.07 dB from the live rub, singing at the same 0.75 s); PlayMode `BowlsPlayTests` (the looper, the desktop, the
stick through Layout's grab pass), the conformance fixture (a side tap on the big bowl), `BowlsShotTests` (GPU),
`BowlsPerfTests`.

Screenshots (`Builds/Shots/bowls/phase-b/`): `seat_in_a_set`,
`seat_view`, `seat_set_overview`, `bowls_close`, `bowls_three_quarter`, `big_bowl_rocking`, `stick_on_the_rim`,
`mr_stand_hidden_stick_held`, `mr_stand_hidden_at_rest`, `display_from_the_seat`, `display_window_sill`,
`display_sill_with_ivy`, `display_window_wall`, `glide_midway`, `glide_midway_from_the_seat`, `menu_portrait` (fill 77 %,
centred, luminance 0.64), `menu_instruments_grid`.

## 5. The phase-A review items (critiques/bowls-unaddressed.md)

| # | Item | What was done | Evidence |
|---|---|---|---|
| 10 | Phasor renormalisation | In the reference since revision 2 and implemented here (block start, both phasors and the pickup); the vectors include it | 10-minute rub: worst \|\|e\| − 1\| **1.3e-6** (< 1e-5), still singing 0.62 m/s (`ContactPhasorsStayUnitOverATenMinuteRub`) |
| 11 | Lesson hits and fallback | A strike target counts a live strike ≥ 0.2 m/s only; 8 s re-demo; 15 s: any touch on the target bowl plays an audible 0.7 m/s strike and counts; description corrected (targets 1.5 or 3 s apart) | `LessonHitRulesFollowReviewItem11`, `LessonPackLoadsAndKeepsTheRules`, pack `format.strike/stall` |
| 12 | Tips against the model | Arrival "Tap a bowl with one fingertip, then lift to let it ring." (P2); rub tip P2 2 s after the 3rd tap; faster "A little faster: about one slow circle a second."; P1 "The singing bowls are by your left knee." after 120 s unplayed; a palm-strike tip; demos anchor the nearest (big) bowl | `BowlsTips`, `BowlsCoach` (`tapped3` test), voices |
| 13 | Lip rubs slow / failing | The hand logic sends a fingertip on top of the lip to the unit as an outer-wall contact (the pad wraps the lip's outer edge); the unit's own lip model and TV7 are unchanged; `tip_bowls_lip` | lip first-timer sings in 0.26-0.68 s on all three bowls (before: big 4.48 s or never, middle 1.09-3.03 s at 0-14 % singing) |
| 14 | Live vs loop on one bowl | The unit's contact lanes (revision 2), implemented; phase B: the platform's `LaneArbiter` per bowl in `BowlsInstrument` | TV13 bit-exact; `LiveHandTakesTheBowlAndTheLoopWaits`; **through the real looper** (`BowlsPlayTests.ALoopedRubYieldsToTheLiveHandAndResumes`): the loop's rub owns the middle bowl on pass 2, a live rub preempts it (1 preempt, 6 loop controls dropped, the loop's Off ignored), the loop takes it back on pass 3 and its own Off frees it |
| 15 | Far rims out of reach | The big bowl moved nearest (inner-back); every far rim ≤ 0.614 m; the whole big rim within 0.478 m; the rub lesson starts on the big bowl | `BowlsLayoutTests`; a search of the pocket shows no layout keeps all far rims under 0.61 m (best 0.614) |
| 16 | Rim-breathing glow | Dropped (nothing glows) | `BowlsShotTests` checks no material emits |
| 17 | Tap debounce | Already per (finger, bowl), 0.12 s, never a contact; kept 0.12 s | double taps 150 / 200 / 250 / 350 ms give 2 strikes each; a 40 ms bounce gives 1 |
| 18 | Rubbed tone nearly pure | A/B pair rendered (`bowls-rub-ab-soft.wav`, `-hard.wav`); README notes the rub is intentionally nearly pure | soft: 2f −55.3, 3f −45.7, (3,0) −51.6 dB; hardest stick tried (Kc 3e6, steep friction, 8 N): −47.2 / −39.6 / −42.3 dB. Six settings tried, none reaches −25..−30 dB: the bowl's resonances filter the friction's harmonics; the user decides by ear |
| 19 | Gaps tight for a hand | ≥ 6 cm is impossible in the 0.41 × 0.29 m pocket (now 62.8 / 32.5 / 45.6 mm, was 42.5 / 28.9 / 30.2); instead a rubbing hand's other tips never play another bowl | full 5-tip hand rubbing the middle bowl by the small one: 0 calls to the small bowl (before: 76-103), the middle sings 100 % |
| 20 | Determinism under Burst | The bowls render through their own Strict function pointer inside the Fast graph: bit-exact (accepted by the integrator given the conformance checks: they pass, CONTRACT-REQUESTS R4) | `StrictFunctionPointerKeepsItsFloatModeInsideAFastEntry`; 16/16 vectors bit-exact that way; Fast would drift (TV9 3.3 dB block RMS) |
| 21 | interaction.mjs ran its cases on import | Already a pure module (cases in interaction-cases.mjs) | importing it leaves `out/interaction-cases.json` byte-identical (sha 7a069f6e) |

## 6. Also found and fixed in phase A

- **The recoil of a hard tap struck the neighbouring bowl** on the new (tighter) layout: the display-time prediction of a
  2 m/s recoil reaches 8 cm. A fingertip that just struck one bowl is silent on the others for 0.15 s.
- **A tracking jump crossing a bowl tangentially** passed the glitch test (it read only the normal speed); it now reads
  the full tip speed. (Both are switchable: `BowlsHands.RecoilGuard`.)
- **Editor Mono is not IEEE-single per operation**: the plain managed render differs from the reference by ≤ 7e-4 of the
  peak in the editor (bit-exact under the standalone MonoBleedingEdge and under Burst Strict). Use Burst Strict for
  bit-exact gates in the editor.

## 7. Open for the device (add to ARCHITECTURE 19 with B1-B12 of bowls.md 11.4)

- "About one slow circle a second" is 0.55 m/s on the big bowl, near its chatter edge at 4 N (it sings at 0.45 m/s, chatters
  from 0.6): check the faster tip on the big bowl by ear (B1 / B5).
- The lip-as-outer contact by ear (does the wine-glass gesture sound natural?).
- The sill spot (3.09, -0.62) in the headset: the displayed marimba stands in front of that part of the sill (its tubes' top 0.745 m, 4.5 cm from the bowls' box); the ray summon picks the bowls at +-3 deg in the planner's model.
- The polished inside of a displayed bowl reflects the white sill (a bright crescent in `display_window_sill.png`): it is the reflection probe, not a hole; check it reads as metal in the headset.
- The rubbed tone's purity (item 18): play `bowls-rub-ab-soft.wav` against `-hard.wav`.

## 8. Tools

- The bowls lab's `unity-check/run.sh` (`./run.sh`, `./run.sh cases`; Node tooling): the offline mono check, bit-exact vectors and the
  interaction cases outside Unity in seconds (needs a compiled `Library/ScriptAssemblies`).
- The lab's `make-lessons-unity.mjs` (the pack), `make-tip-voice.mjs` (lines + voices + lint),
  `export-render-acts.mjs` (the render phrases for `BowlsRenderTests`), `render-rub-ab.mjs` (item 18).
- Test runs: `TEST_FILTER=Bowls Tools/unity.sh test-edit`; with the GPU lock: `MUSICROOM_SHOTS_ROOT=<shots root>
  TEST_FILTER=BowlsShotTests Tools/unity.sh test-play --gpu`, `TEST_FILTER=BowlsPerfTests Tools/unity.sh test-perf`.
