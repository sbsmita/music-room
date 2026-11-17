# Singing bowls: contract requests (phase B, for the integrator)

Phase B ran in a fresh copy of the instruments base project. Outside the bowls' own folders (`Bowls/**`,
`Audio/Sound/Bowls/**`, `Tests/EditMode/Bowls/**`, `Tests/EditMode/Audio/Bowls/**`, `Tests/PlayMode/Bowls/**`) the copy
carries only the theremin's versions of the shared PlayMode tests (R3, adopted as the integrator asked) plus R8's one line.
R1's vectors were applied locally for the runs and reverted; R2 was tried locally (it cannot be fixed by an override,
see there) and reverted. Each request lists its exact code. R1's planner diff and its `platform.vectors.json` were prepared with the bowls lab (Node).

| # | Kind | One line |
|---|---|---|
| R1 | required | The display spot moves to the near half of the sill, and the play centre to (0, -0.016): planner data + vectors |
| R2 | required | `UnitShareTests` runs the phase-0 knock in the Bowls slot, whose hook is now the real bowls |
| R3 | required | The shared PlayMode tests that assumed stubs: adopt the theremin's R1 / R1b / R1c as they are |
| R4 | sign-off | R6 of phase A (the Strict render pointer) with the conformance cost and allocation checks |
| R5 | optional | Sunny's B6 lesson re-demo shows the instrument's arrival demo (a tap) for a rub target too |
| R6 | informational | Live rubs record with `lane` = the bowl's index (the looper's key); playback replaces it |
| R7 | informational | Done by the platform, nothing to apply (send row, demos, lanes, tail rule as our own test) |
| R8 | required | The delta on top of the theremin's load test: a generic `IUnitWorstCase` hook (one interface, one line) |

## R1 [required] The display spot and the play centre (planner data and vectors)
- **File:** the Node stow plan, `Tools/SoundLab/platform/registry.mjs`; then `node make-vectors.mjs` and copy
  `platform.vectors.json` to `Assets/MusicRoom/Tests/EditMode/Layout/Reference~/platform.vectors.json`.
- **Why:** the integrator's R8: the spot must be clear of all decor. The old spot (3.08, -1.62) put the row of bowls
  (0.51 m along the sill) into the trailing ivy at the far end of the sill (the ivy at z -1.83, leaves to about -1.58).
  The middle of the sill (z about -1.1) is clear of the ivy but sits behind the marimba's tubes for the ray summon: a
  ray 3 degrees off picks the marimba there (`make-vectors` "bowls ok/marimba/marimba"). The near half of the sill,
  **(3.09, -0.62) yaw 90**, is valid in the stow plan, 0.7 m clear of the ivy and 7 cm from the near curtain, and every
  instrument's rays stay `ok/ok/ok`. The play centre (0, -0.016) is phase A's (the big bowl moved nearest, review item
  15; the boxes are unchanged).
- **Change:**
  ```diff
  --- stow plan (Node planner)
  -  bowls:     { id: 'window-sill', mount: 'surface', pose: { x: 3.08, z: -1.62, yaw: 90 }, note: 'the three bowls on the inside sill on their cushions, striker and stick beside them (the stand hidden)',
  +  bowls:     { id: 'window-sill', mount: 'surface', pose: { x: 3.09, z: -0.62, yaw: 90 }, note: 'the three bowls in a row on the near half of the inside sill on their cushions, the stick in front (the stand hidden), clear of the ivy at the far end',
                  stowBoxes: [ { cx: -0.01, cz: 0, hx: 0.255, hz: 0.09, y0: 0.63, y1: 0.735 } ] },
  --- Tools/SoundLab/platform/registry.mjs (bowls)
  -    prefs: ['FrontLeft', 'FrontRight', 'Left', 'Right', 'Front'], reachMax: 0.62, playCentre: { x: 0.006, z: -0.011 },
  +    prefs: ['FrontLeft', 'FrontRight', 'Left', 'Right', 'Front'], reachMax: 0.62, playCentre: { x: 0, z: -0.016 },
  ```
  `make-vectors` with both: "stow plan ok: true; ... bowls ok/ok/ok ...; self-checks: all pass". The vectors differ
  from today's only in the bowls' spot, play centre and the reach figures that follow from it.
- **Test:** `StowPlanTests.TheLiveDescriptorsSpotsAreClearOfEverything` (fails on "bowls x" without the new vectors, passes
  with them), `SeatPlannerTests`, `SetLayoutTests`, `DisplayArrangeTests` (the bowls slide along the sill and stay on it).
- **Fallback until applied:** the descriptor already carries the new spot; only `StowPlanTests` fails (the vectors still
  say 3.08, -1.62).

## R2 [required] UnitShareTests runs the knock in the Bowls slot
- **File:** `Assets/MusicRoom/Tests/EditMode/Audio/Platform/UnitShareTests.cs`
- **Why:** its three cases cast `e.Unit(UnitId.Bowls)` to `KnockUnit`. The bowls' real unit now fills that slot
  (`InvalidCastException`), and the slot's render hook is `UnitHooks.RenderBowls` (the bowls' DSP), so a `KnockUnit`
  forced into the slot with `SoundUnits.Override` does not render as a knock either (tried locally: 0 work). Every slot
  that can still host the knock will go the same way when its instrument lands.
- **Change** (either):
  - (a, recommended) keep the knock's cap rule as a plain test and drop the three engine runs; each real unit has its own
    share test (the bowls': `BowlsPlatformUnitTests.AShareOfPointFourAtLeastHalvesTheBowlsWork`, 49 % of the work at
    Share 0.4, and `TheLodBudgetScalesTheBowlsToo`, 66 % at Budget 600):
    ```csharp
    [Test]
    public void TheKnocksVoiceCapFollowsTheShare()
    {
        Assert.That(KnockDsp.VoiceCap(0.4f), Is.EqualTo(2));
        Assert.That(KnockDsp.VoiceCap(1f), Is.EqualTo(6));
        Assert.That(KnockDsp.VoiceCap(0.01f), Is.EqualTo(1), "at least one voice");
    }
    ```
  - (b) until the last stub lands, run the three cases on a slot whose hook is still the knock: replace `UnitId.Bowls`
    by `UnitId.Handpan` and `SourceId.Bowls` by `SourceId.Handpan` in `Out` and `Work()`.
- **Test:** `UnitShareTests` itself.
- **Fallback until applied:** its three cases fail (`InvalidCastException`); nothing else depends on them.

## R3 [required] The shared PlayMode tests that assumed stubs: the theremin's versions, unchanged
- **Files:** `Tests/PlayMode/Layout/InstrumentView.cs` (new), `DisplayArrangeTests.cs`, `GlideTests.cs`,
  `RaySummonTests.cs`, `SetLayoutTests.cs`, `Tests/PlayMode/Core/PointerClaimsTests.cs`,
  `Tests/PlayMode/Audio/EightInstrumentsLoadTests.cs`.
- **Change:** exactly the theremin's R1, R1b and R1c (`Theremin/CONTRACT-REQUESTS.md`), as the
  integrator asked; the bowls need nothing more in these files except R8's one line in `EightInstrumentsLoadTests`.
  The bowls' own earlier edit (a local `Inst` helper in three of the layout tests) is withdrawn.
- **Test:** in the bowls' phase-B copy with those files copied byte for byte from the theremin's (only `EightInstrumentsLoadTests`' theremin
  branch is fenced off locally with `#if THEREMIN_REAL`, because in this clone the theremin is still the stub and has no
  `IThereminSound.Control`): see `PHASE-B-NOTES.md` section 4 for the runs.
- **Fallback until applied:** with instruments-base's versions, 6 layout cases fail with the real bowls (NullReference /
  "Bowls stub loaded") and the load test casts the bowls unit to `KnockUnit`.

## R4 [sign-off] The Strict render pointer (phase A's R6), with the conformance checks
- **File:** none (the bowls' own `Audio/Sound/Bowls/BowlsDsp.cs`).
- **Why / evidence:** accepted by the integrator provided the conformance cost and allocation checks pass with it. They
  do: `InstrumentConformanceTests` (all six cases for the bowls, including `TickAllocatesNothing` and `ItsUnit`) pass
  with `RenderBowls` going through the Strict function pointer; the bowls' own `BowlsPerfTests` and
  `BowlsUnitTests.RenderAndApiAllocateNothing` pass; the perf numbers are in `PHASE-B-NOTES.md` section 4.
- **Fallback:** with Burst off the unit renders inline (managed).

## R5 [optional] The lesson re-demo for a rub target
- **File:** `Assets/MusicRoom/Guide/GuideTips.cs` (`RedemoKind`), `Assets/MusicRoom/Guide/GuideModule.cs` (`Redemo`)
- **Why:** Sunny's B6 re-demo (8 s waiting) uses the instrument's arrival tip's demo, a tap, for every target; on the
  rub lesson's rub targets a circling finger would be the right move. Generic rule (names no instrument): prefer the
  instrument's `LessonStart` tip whose `TriggerArg` is the running lesson.
- **Change:**
  ```csharp
        public DemoKind RedemoKind(InstrumentKind kind, string lessonId = null)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                TipEntry e = _entries[i];
                if (lessonId == null || e.Owner == null || e.Owner.Kind != kind || e.Spec.Trigger != TipTrigger.LessonStart) continue;
                if (string.Equals(e.Spec.TriggerArg, lessonId, StringComparison.Ordinal) && e.Spec.Demo != DemoKind.None && e.Spec.Demo != DemoKind.Custom) return e.Spec.Demo;
            }
            /* the existing loop over FirstArrival tips, unchanged */
        }
  ```
  with `GuideModule.Redemo` passing `_app.Get<ITutorial>()?.LessonId`. For the bowls: `tip_bowls_lesson_rub`
  (FingerCircle) on `bowls-sing`, `tip_bowls_lesson` (Tap) on `bowls-first`.
- **Test:** a GuideTipsPlayTests case: a lesson whose LessonStart tip demos FingerCircle re-demos FingerCircle.
- **Fallback until applied:** the re-demo taps at the waited bowl's rim; at 15 s any touch on the target bowl counts.

## R6 [informational] The live lane key of a rub
- Live rub events carry `lane` = the bowl's index (0..2) instead of 0: the looper keys open lanes by `lane`, so two
  bowls rubbed at once must be two lanes (with one lane 0 the second rub's Off is dropped from the layer). Playback
  replaces the lane with the layer's (1..6) or the take's (7), and the bowl travels in `i`. The looper's own closes
  (window end, mute) carry no `i`: they carry no zone either, and the bowls close every bowl that lane holds. Nothing to
  apply; if the platform prefers a different key for multi-voice continuous instruments, `BowlsRubEncoder(bowl, R,
  lane)` takes it as a parameter.

## R7 [informational] Already done by the platform (nothing to apply)
- `AudioSourceTable` Bowls send -1 dB (phase A R1), the MR drop: in phase 0's table.
- `DemoKind.FingerCircle` (phase A R2), `ThenDemo` / `ThenAnchor`, `TipTrigger.LessonStart`, `TipSuccess.LessonHit`,
  `LaneArbiter`, `ControlEncoder`, `HandToolMath`, `ShadowSpec.HiddenInMr`, idle skip, Share / Work: used as declared.
- The "no low-frequency push" variant (phase A R3) is the bowls' own test (`BowlsTailRuleTests`): energy below 60 Hz
  -38.0 / -43.6 / -45.1 dB re the hit (limit -20); mean / rms over 400 ms windows 0.0052 / 0.0016 / 0.0034 (limit 0.01).
  Over 200 ms windows the big bowl reads up to 0.0137 (a window edge on its decaying 196 Hz tone with beating partials,
  not a push), so a platform version of the rule should read 400 ms windows (or after a 20 Hz high-pass) for
  fundamentals under 250 Hz.
- `bowlDamp` is a sampled State (phase A R5); the Strict pointer and `u->Share` (phase A R6, R7) are in the unit.
- No decor move is needed (R1 moves the spot instead), and no Decor list on `IRoomFurniture` is needed by the bowls.

## R8 [required] A generic worst-case hook for EightInstrumentsLoadTests (the delta on the theremin's R1c)
- **Files:** `Assets/MusicRoom/Audio/Burst/Units/IUnitWorstCase.cs` (new; today it sits in the bowls' folder,
  `Audio/Sound/Bowls/IUnitWorstCase.cs`, same assembly and namespace: move it), and
  `Assets/MusicRoom/Tests/PlayMode/Audio/EightInstrumentsLoadTests.cs` (the theremin's version).
- **Why:** the load test should drive each real unit at its declared worst without a branch per instrument. A unit that
  implements the hook drives itself; the theremin's branch can later become the theremin unit's own `DriveWorst`.
  The bowls' worst: all three bowls rubbed (4 N, 0.4 m/s, the outer wall: the LaneArbiter lets one stick own a bowl, so
  three contacts is the most), all three struck again every 0.5 s.
- **Change:**
  ```csharp
  // Audio/Burst/Units/IUnitWorstCase.cs
  namespace MusicRoom.Audio.Burst
  {
      public interface IUnitWorstCase
      {
          /// <summary>Block `block`'s commands of the worst case; when = absolute seconds offline, 0 = as soon as possible.</summary>
          void DriveWorst(long block, double when);
      }
  }
  ```
  ```diff
  // EightInstrumentsLoadTests.Drive, after the theremin's branch:
  +            // any unit's declared worst case through the generic hook (the bowls: three rubbed bowls, restruck every 0.5 s)
  +            foreach (var u in knocks)
  +                if (u is IUnitWorstCase w) w.DriveWorst(b, W(0));
  ```
  (`BowlsUnit` implements it: `Audio/Sound/Bowls/BowlsUnit.cs`, `DriveWorst`.)
- **Test:** `EightInstrumentsLoadTests` (test-perf), run in the bowls' phase-B copy: see `PHASE-B-NOTES.md` section 4.
- **Fallback until applied:** the interface lives in the bowls' folder (it compiles there); without the test line the load
  test leaves the bowls silent (no cast, no failure).

