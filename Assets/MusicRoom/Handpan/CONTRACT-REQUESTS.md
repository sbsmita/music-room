# Handpan: contract requests (phase B, 2026-11-01)

Phase B ran in a copy of the instruments base project. Everything the handpan needs from the platform
exists; nothing in Core / Audio / Layout / Lessons had to change for the instrument itself. What follows is what replacing
the **handpan stub** broke in the platform's own tests (they used the stub's `KnockUnit` / `PlaceholderInstrument`
members), plus the planner data. Each item was applied locally in the phase-B copy and the whole chain is green with it.

The exact code of R1, R2 and R4 is one patch against instruments-base (5 files), checked with `patch --dry-run -p1`
there: **`Handpan/Shared~/phase-b-shared.patch`**. Apply: `cd <project> && patch -p1 <
Assets/MusicRoom/Handpan/Shared~/phase-b-shared.patch`. The items below quote the essential lines.

**Merged with the theremin's versions (integrator note):** the layout tests, `InstrumentView` and PointerClaimsTests are
the theremin's R1b / R1 **exactly** (copied byte for byte from the theremin's phase-B copy into this clone; R3 below asks nothing more), and
EightInstrumentsLoadTests is the theremin's R1c plus the bowls' generic `IUnitWorstCase` line (R6: no handpan delta).

---------------------------------------------------------------------------------------------------------------------------

## R1 [required] A knock renders with its own kernel, whatever role slot it sits in

- **File:** `Assets/MusicRoom/Audio/Burst/Units/KnockUnit.cs`, `Assets/MusicRoom/Audio/Burst/Units/UnitSlot.cs`
- **Why:** `UnitHooks.Render` dispatches on `u->Kernel`, which `SoundUnit.Register` sets to the role. A test that puts a
  `KnockUnit` into the handpan's slot (IdleSkipTests does, three times) now reaches `HandpanDsp.RenderUnit` with a
  `KnockState*` read as a `HandpanState*` (it happened to read `Ready = 0` and render silence; it could as well crash).
  `Register` already takes a `kernel`: the knock should use its own.
- **Change:**
  ```csharp
  // KnockUnit.cs, constructor
  Register(ctx, role, outputs, _s, 128, 128, kernel: Kernel, idleSkip: idleSkip);
  // KnockUnit.cs, new member
  /// <summary>The knock's own render hook (UnitHooks.Render), whatever role slot it sits in.</summary>
  public const UnitId Kernel = (UnitId)200;
  // UnitSlot.cs, UnitHooks.Render's switch, after `case UnitId.Bowls`
  case KnockUnit.Kernel: KnockDsp.Render(u); break;
  ```
  The stubs' own `Render<X>` partials (`=> KnockDsp.Render(u)`) become unused and harmless; each instrument deletes its
  stub file anyway.
- **Test:** `IdleSkipTests` (3 cases) and `UnitShareTests` green with the handpan's real unit in the role.
- **Fallback until applied:** IdleSkipTests' knock in the handpan's slot renders silence (2 cases fail on the cast, R2).

## R2 [required] IdleSkipTests: the knock is added to an engine without the handpan role

- **File:** `Assets/MusicRoom/Tests/EditMode/Audio/Platform/IdleSkipTests.cs`
- **Why:** `(KnockUnit)e.Unit(UnitId.Handpan)` is an InvalidCastException once the role creates `HandpanUnit`.
- **Change (3 places, in `ASilentUnitIsSkipped...` and `ARunningFadeAndPendingEvents...`):**
  ```csharp
  using var e = new OfflineEngine(Array.Empty<UnitId>(), null, sources: false);
  var knock = e.Add(ctx => new KnockUnit(ctx, UnitId.Handpan, Out, 294f));   // a knock in the handpan's slot
  ```
  (the same for `f` / `k2`). With R1 the knock renders with its own kernel.
- **Test:** IdleSkipTests 4/4.
- **Fallback:** two InvalidCastExceptions.

## R3 [no delta] The Layout tests: the theremin's R1b exactly

- **Files:** `Tests/PlayMode/Layout/InstrumentView.cs` (new), `DisplayArrangeTests.cs`, `GlideTests.cs`,
  `RaySummonTests.cs`, `SetLayoutTests.cs`: the theremin's versions, unchanged. With them and the real handpan (the other
  four new instruments still stubs here) all four suites are green; so is the theremin's PointerClaimsTests (R1).
- **Nothing to add for the handpan.** (My own first version was the same helper in `Tests/PlayMode/Core/`; it is gone.)

## R4 [required] Two tests written on the handpan stub now run on the real handpan

- **Files:** `Assets/MusicRoom/Tests/PlayMode/Audio/AudioPresenceTests.cs`, `Tests/PlayMode/Lessons/GenericTutorialPlayTests.cs`
- **Why:** AudioPresenceTests cast `ActiveUnit(UnitId.Handpan)` to `KnockUnit` (2 failures); GenericTutorialPlayTests take
  the handpan's `ILessonTargets` as a `PlaceholderInstrument` (5 failures).
- **Change, AudioPresenceTests:** the knock becomes a short handpan note: a strike, and 0.12 s later a palm resting on
  every field (a handpan note rings for seconds; the test needs it silent within 5 s, as the knock was):
  ```csharp
  IHandpanSound Pan => (IHandpanSound)_engine.ActiveUnit(UnitId.Handpan);
  IEnumerator Note(int field)
  {
      Pan.Strike(field, 0.8f, 0.35f, 0.2f, HandpanTech.Tip, 0);
      yield return Wait(0.12f);
      for (int f = 0; f < 9; f++) Pan.Damp(f, 2, 1f, 0f, 0f, HandpanDampKind.Palm, 0);
  }
  // Knock.Knock(0, 0.8f) -> yield return Note(1);   Knock.Knock(2, 0.8f) -> yield return Note(2);
  ```
- **Change, GenericTutorialPlayTests:** `_handpan` is the `HandpanInstrument` (`MusicRoom.Handpan`); `Tap(i)` brings the
  index pad down onto field i along its normal at about 0.9 m/s, rests 3 frames and lifts (the real play logic arms
  above 10 mm and strikes on the way down; the stub's one-frame poke through a box does not strike a handpan); the hint
  is `_handpan.Hint(...)` ("Tap the ding": the first target is the ding); the cue checks use
  `HandpanInstrument.CueOf(field)` (the k the tutorial last gave that field: two blocks of the same field can be on their
  way, so "the hit target's cue went off" is `CueOf(target) < 1`); `LiveKnocks` / `PlaybackKnocks` -> `LiveStrikes` /
  `PlaybackStrikes`. Every timing, lesson and B2 / B4 / B6 assertion is unchanged.
- **Test:** AudioPresenceTests 2/2, GenericTutorialPlayTests 5/5.
- **Fallback:** 7 failures.

## R5 [required] Planner reference data: the handpan's real footprint and display box

- **Files:** `Tools/SoundLab/platform/registry.mjs` (`INSTRUMENTS.handpan.boxes`), the Node stow plan
  (`SPOTS.handpan.stowBoxes`); then `node make-vectors.mjs` and copy `platform.vectors.json` to
  `Tests/EditMode/Layout/Reference~/`.
- **Why:** the real stand (a three-legged cradle) replaces the platform's set-back placeholder stand box, and the shell
  and its band are 7 mm wider than the placeholder; on display the shell hangs from 1.25 m (the bracket's hooks), and the
  box stops at the planner's 2 cm wall inset (the bracket's wall plate lies flat on the wall behind it).
- **Change:**
  ```js
  // registry.mjs, handpan.boxes (rig-local cx, cz, hx, hz, y0..y1; the rig at Home (0, 0, 0.38) in the seat frame)
  boxes: [
    { cx: 0, cz: 0,      hx: 0.272, hz: 0.240,  y0: 0.62, y1: 0.88 },   // the shell and its rubber band, tilted 28 deg
    { cx: 0, cz: 0.1875, hx: 0.315, hz: 0.1275, y0: 0,    y1: 0.62 },   // the cradle tripod below the rim: z 0.06..0.315
  ],
  // stow-plan.mjs, SPOTS.handpan (spot unchanged: left-wall-bracket, wall, (-3.06, -0.62), yaw -90)
  stowBoxes: [ { cx: 0, cz: 0, hx: 0.275, hz: 0.12, y0: 1.25, y1: 1.86 } ],
  ```
  Measured against the model (conformance check 3, logged): handles x -0.313..0.313, y 0.001..0.876, z -0.242..0.313;
  boxes x -0.315..0.315, y 0..0.88, z -0.240..0.315. On display (HandpanShotsTests): world x -3.195..-2.944,
  y 1.262..1.849, z -0.889..-0.352 (rig-local z -0.116..0.135: the last 15 mm are the bracket's wall plate).
  Below 0.62 m nothing of the stand is nearer than rig-local z 0.06 (the knee box ends at z 0.03 rig-local, + 3 cm gap):
  the two near legs' elbows are at (+-0.175, 0.618, 0.072), the feet at (+-0.30, 0, 0.13) and (0, 0, 0.30).
- **Test:** `StowPlanTests.TheLiveDescriptorsSpotsAreClearOfEverything` is green with the live descriptor (it validates the
  live boxes against the furniture, the other spots and every fitting seat plan); `SeatPlannerTests` logs the live /
  vector difference until the vectors are regenerated.
- **Fallback:** the vectors keep the placeholder boxes (the planner itself reads the live descriptor).

## R6 [no shared delta] EightInstrumentsLoadTests: the handpan drives itself through the bowls' `IUnitWorstCase`

- **Files:** `Assets/MusicRoom/Tests/PlayMode/Audio/EightInstrumentsLoadTests.cs` = the theremin's R1c plus the bowls' R8
  generic line (`if (u is IUnitWorstCase w) w.DriveWorst(b, W(0));`), nothing handpan-specific. `IUnitWorstCase` is copied
  byte for byte (name, signature `void DriveWorst(long block, double when)`, namespace `MusicRoom.Audio.Burst`) into
  `Audio/Sound/Handpan/IUnitWorstCase.cs` so it compiles here: **delete that copy when the interface moves to
  `Audio/Burst/Units`** (it would clash with the bowls' copy in one assembly).
- **The handpan's side (owned):** `HandpanUnit : IUnitWorstCase`, `DriveWorst`: every field struck full-force at the dimple's
  edge every 60 ms (11 blocks), 2 ms apart: the 10-finger roll of `HandpanPerfTests` / bench.mjs 'worst'.
- In the handpan's phase-B copy the theremin's three `IThereminSound` lines are left out only because the theremin is still a stub here.
- **Test:** test-perf in this clone (EightInstrumentsLoadTests 2/2).
- **Expect:** `UnitShareTests` (it drives the bowls role as a `KnockUnit`) is green here, where the bowls are still a stub;
  it fails once the bowls' real unit lands, as you said: it needs your integration fix, nothing from the handpan.

---------------------------------------------------------------------------------------------------------------------------

## Notes (no change requested)

- **The bake output** `Assets/MusicRoom/Generated/Resources/Generated/Handpan/{steel_base,steel_ms,steel_normal,stand}.png`
  (+ metas) is written by `Tools/unity.sh bake` from `HandpanTextures` (an `ITextureBakeProvider` in the handpan's
  folder). `steel_ms` carries the smoothness in its alpha and is baked with `HasAlpha = true`: without it the importer
  drops the alpha and the steel renders as a mirror (`HandpanBakeTests` guards it).
- **`Assets/Scenes/Main.unity`** differs from instruments-base only by `Tools/unity.sh scene` regenerating it (file ids).
- **Superseded phase-A requests:** the unkeyed onset (the platform's `EventTypeSpec` has `Held`; the handpan's onset is
  not Held), the event fields (TakeEvent fields only, `TakeEventHandpan`), a "Rest" demo (`DemoKind.PalmRest` exists),
  the test asmdef lines (already in instruments-base), the texture bake (above).
- **Managed float maths in this editor is not two-rounding** (phase A, `HandpanFloatProbeTests`): Mono evaluates a float
  expression in double and rounds once at its end; any unit that claims a bit-exact managed path must cast every
  intermediate `(float)(...)`. The handpan does.
