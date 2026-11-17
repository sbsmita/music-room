# Marimba: contract requests to the platform (phase B, final)

## Shared files edited

**None** at hand-back. `diff -rq` of the phase-B copy's Assets against the instruments base lists only the marimba's owned paths:
`Assets/MusicRoom/Marimba/**`, `Assets/MusicRoom/Audio/Sound/Marimba/**`, `Assets/MusicRoom/Tests/EditMode/Marimba/**`,
`Assets/MusicRoom/Tests/PlayMode/Marimba/**`, `Assets/MusicRoom/Tests/EditMode/Audio/Marimba/**`. The stub's
`MusicRoom.Marimba.asmdef` was edited in place (`allowUnsafeCode: true`; its `.meta` GUID f789da014e3ea4fd881312f45b40047d
kept); the folder `.meta`s (28db4bf4..., 96c24125...) are instruments-base's. The phase-A nested test asmdefs are gone:
the tests compile into `MusicRoom.Tests.EditMode` / `.PlayMode`. RB3 (the theremin's and the bowls' shared-test versions
plus one removal) was applied locally to run the chain end to end, then reverted.

## Phase-A requests: where each ended up

| Phase A | Phase B |
|---|---|
| R1 three sources and their sends | Pre-declared (`SourceId.MarimbaMid`, the table's -2 / 0 / -2 dB rows); the descriptor's `AudioSourceSpec`s carry the same sends |
| R2 the metallic-tail rule | The marimba's own test, as BUILD-GUIDE 13 asks: `MarimbaBehaviourTests.Tail_AfterPointThreeSecondsOnlyTheFundamentalRemains` (spec 3.22, 7 bars C3..C6: the strongest other peak 27-73 dB down at 0.3 s, rule 12) |
| R3 display height | Superseded: the spot moved to (2.30, -0.80) |
| R4 `HandToolMath` | Exists in Core; `MarimbaToolMath` forwards to it |
| R5 demo `MalletHold` | Pre-declared; `tip_marimba_dead` uses it |
| R6 `ILessonAccompany`, pack `options` / `accompany` | Pre-declared; implemented by `MarimbaInstrument` |
| R7 the display spot in the window wall | Fixed by the platform: at (2.30, -0.80) yaw 90 the frame's back is at world x 2.92 (wall 3.20) |
| R8 budgets | In the descriptor: draws 5 / 3, tris 18,200 / 3,900 (measured 18,132 / 3,872) |
| R9 unit slots, test tap, link.xml | All exist; `MarimbaUnit` uses `Share`, `Work`, `idleSkip`, `RecordUnit`; `Core/link.xml` already lists `MusicRoom.Marimba` |
| R10 signals, features, triggers | Signals are `InstrumentSignalEvent`s; the free-text triggers are the marimba's own counters publishing `marimba.noRoll`, `marimba.noSharps`, `marimba.ringing`; the dead-stroke tip is `TipTrigger.Manual`. Feature `lessons.marimba` renamed `marimba.lessons` (the prefix rule) |

## RB1 [required] The planner's reference data: the marimba's built footprint

- **File:** `Tools/SoundLab/platform/registry.mjs` (the planner's reference). The integrator then re-runs
  `node make-vectors.mjs` and copies `platform.vectors.json` to `Tests/EditMode/Layout/Reference~/`.
- **Why:** BUILD-GUIDE 5. The descriptor now carries the built model (critique item 11). The bar tops rose from 0.70 to
  0.72 m (`MarimbaGeometry.HSeat`) so that nothing below 0.62 m comes near the knees (tube A3, the nearest, is 3.5 cm beyond
  the knee box). The home moved from z 0.50 to 0.47 so that the frame's back (local z 0.623) stays inside the front wall
  (1.093 m from the eyes against 1.10). The top is split at x 0.42 so the treble end (short bars only) starts at z -0.13.
  Two **air** boxes are the mallets' and hands' field: with the kit at Right, its hi-hat cymbal stood about 7 cm under the
  right hand's grip for the top octave and a crash cymbal over the treble end (the Layout's measured check counted it
  too: `Overlaps3D` 1). With them the marimba never shares a set with the kit.
- **Change:**

```diff
   marimba: {
-    // marimba.md rev 2 section 4.1 (final): home (0, 0.50), boxes A and B, both y 0-0.745 (the spec's own numbers)
-    name: 'Marimba', canonicalSlot: 'Front', home: { x: 0, z: 0.50, yaw: 0 }, single: { x: 0, z: 0.50, yaw: 0 },
+    // the built model (phase B, MarimbaDefinition.SeatBoxes): bar tops 0.72 m, home (0, 0.47); the top above 0.62 m, the
+    // tubes and legs below it 3.5 cm beyond the knee box; the hands' field as two air boxes
+    name: 'Marimba', canonicalSlot: 'Front', home: { x: 0, z: 0.47, yaw: 0 }, single: { x: 0, z: 0.47, yaw: 0 },
     prefs: ['Front', 'Left', 'Right'], reachMax: 0.80, playCentre: { x: 0, z: 0.05 },
     boxes: [
-      { cx: 0, cz: -0.005, hx: 0.62, hz: 0.255, y0: 0, y1: 0.745 },        // A: naturals, tubes, front rails, tray
-      { cx: -0.03, cz: 0.43, hx: 0.58, hz: 0.215, y0: 0, y1: 0.745 },      // B: sharps, tubes, back rails
+      { cx: -0.105, cz: 0.18, hx: 0.525, hz: 0.445, y0: 0.62, y1: 0.76 },  // top x -0.63..0.42: bars, rails, tray, mallets, bass board
+      { cx: 0.525, cz: 0.145, hx: 0.105, hz: 0.275, y0: 0.62, y1: 0.76 },  // top, treble end x 0.42..0.63 (z -0.13..0.42)
+      { cx: 0, cz: 0.36, hx: 0.21, hz: 0.26, y0: 0, y1: 0.62 },            // under the middle: folded tube runs, stretcher
+      { cx: -0.42, cz: 0.30, hx: 0.21, hz: 0.32, y0: 0, y1: 0.62 },        // under the bass half: tubes
+      { cx: 0.42, cz: 0.30, hx: 0.21, hz: 0.32, y0: 0, y1: 0.62 },         // under the treble half: straight tubes
+      { cx: -0.605, cz: 0.215, hx: 0.035, hz: 0.41, y0: 0, y1: 0.62 },     // bass end board and legs
+      { cx: 0.605, cz: 0.15, hx: 0.035, hz: 0.25, y0: 0, y1: 0.62 },       // treble end board and legs
+      { cx: 0, cz: 0.25, hx: 0.64, hz: 0.37, y0: 0.76, y1: 1.00, air: true },   // air: the heads' lift over the bars
+      { cx: 0, cz: -0.24, hx: 0.50, hz: 0.12, y0: 0.76, y1: 1.05, air: true },  // air: the grips in front of the bars
     ],
-    stowFoot: { w: 1.24, d: 0.90 },
+    stowFoot: { w: 1.25, d: 0.88 },
   },
```

- **Test:** `MarimbaFootprintTests` (EditMode). The boxes cover the model's handles and played parts (3 cm), and nothing
  below 0.62 m is within 3 cm of the knees. The back is inside the wall. The live planner gives: alone `Marimba@Front`;
  with the harp `Marimba@Front Harp@Left`; with the kit, or the kit and the harp, does not fit (only the air boxes meet
  the kit's cymbals; the solid boxes clear it); with the hand drums, the handpan, the theremin or the bowls
  `Marimba@Right+5`. `SeatPlannerTests` checks the C# planner on the vectors' own data, so this cannot break it; its "live stubs
  differ from the vectors" line names the marimba until the re-run.
- **Fallback until applied:** none needed in the app (the live planner reads the descriptor); only the reference vectors
  are stale.

## RB2 [optional] The display spot's note

- **File:** the Node stow plan, `SPOTS.marimba.note`.
- **Why:** the bar tops (0.72 m) are above the 0.63 m sill, not below it; the spot itself is right.
- **Change:** `note: 'under the window, the frame along the wall, mallets on the bars'`.
- **Test:** none (text). **Fallback:** none needed.

## RB3 [required] Shared tests that used the marimba as a phase-0 stub: the theremin's and the bowls' versions, plus one removal

- **Base, unchanged:** the theremin's R1, R1b and R1c exactly as in `Theremin/CONTRACT-REQUESTS.md`
  (`Tests/PlayMode/Layout/InstrumentView.cs` + its `.meta`, the `Stub(k)` view in DisplayArrangeTests, GlideTests,
  RaySummonTests, SetLayoutTests; PointerClaimsTests counting onsets from the recorder; EightInstrumentsLoadTests with
  `SoundUnit[]`), and the bowls' R8 (`IUnitWorstCase` and its one generic line in `EightInstrumentsLoadTests.Drive`). With
  them the marimba's lines in `SetLayoutTests` (the default set on display; the menu says No room, the ray brings it alone at
  its Single home) and `DisplayArrangeTests` (a floor instrument slides and is saved) pass on the real marimba.
- **The marimba's worst case:** no test change. `MarimbaUnit` implements `IUnitWorstCase.DriveWorst(long block, double
  when)`: two mallets, 15 strokes a second each (a medium-yarn stroke at vel 0.9 every 6 blocks), walking over all 37 bars
  (`MarimbaLoadTests`' seated worst). The interface is copied, same name, namespace (`MusicRoom.Audio.Burst`) and
  signature, into `Audio/Sound/Marimba/IUnitWorstCase.cs`: delete that copy when the bowls' moves to
  `Audio/Burst/Units` (two copies in one assembly would not compile).
- **The one delta, File:** `Assets/MusicRoom/Tests/PlayMode/Core/FastMotionDriverTests.cs` (touched by neither the theremin
  nor the bowls). **Why:** `TheMarimbasLiveOnsetsAskForFastMotionToo` drives a `PlaceholderInstrument`'s knock; the real
  check is the marimba's own `MarimbaPlatformTests.ItsLiveStrokesAskForFastMotion_APlayedBackOneDoesNot` (a fingertip
  stroke through the scripted hands turns Fast Motion Mode on at once, off after the quiet window; a played-back `marimba`
  onset does not). **Change:**

```diff
@@ -61,9 +61,6 @@
         [UnityTest]
         public IEnumerator TheHandDrumsLiveOnsetsAskForFastMotionLikeDrumHits() => FastMotionStub(InstrumentKind.HandDrums, "handDrum");
 
-        [UnityTest]
-        public IEnumerator TheMarimbasLiveOnsetsAskForFastMotionToo() => FastMotionStub(InstrumentKind.Marimba, "marimba");
-
         /// <summary>The stub at the seat in My set with the piano; the piano's live notes never ask for FMM, a fingertip
```

- **Test:** in this clone, with the theremin's files, the bowls' line and the removal: FastMotionDriverTests,
  DisplayArrangeTests, SetLayoutTests, RaySummonTests, GlideTests, PointerClaimsTests and MarimbaPlatformTests 34 / 34;
  the full `test-play` 408 / 408, `test-play --gpu` 442 / 442, `test-perf` 17 / 17. Locally only, the theremin's
  `th.Control(...)` line was commented out (this clone has the theremin stub, whose `IThereminSound` has no `Control`).
- **`UnitShareTests`:** the integrator expects it to fail once a stub's knock is replaced; in this clone it passes
  (EditMode, with the real `MarimbaUnit`). Listed in case the merged build differs.
- **Fallback until applied:** those tests fail with the real marimba (a cast to `PlaceholderInstrument` / `KnockUnit`);
  nothing in the app depends on them.

## Seen, not requested

- The Layout measures each renderer as one box, so the kit's hi-hat stand and cymbal stand count as 0.53 x 0.49 m boxes
  from the floor to the cymbal (tripod legs and pole together), while the kit's descriptor models a thin pole. Nothing
  depends on it for the marimba now (the air boxes keep it away from the kit). Another instrument beside the kit's right
  side would get the same false overlaps.
