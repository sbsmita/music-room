# Marimba: phase B (done) and what is left for the headset

Phase B ran in a copy of the instruments base project (2026-11-01). The phase-A folders were copied in from
the phase-A copy (read-only) and wired into the platform's contracts. No shared file differs from instruments-base
(`CONTRACT-REQUESTS.md`).

## 1. What was wired

| Contract | Where |
|---|---|
| `MarimbaDefinition` (Kind / Rig / Unit 7, Carousel 5, Order 35, TickOrder 23; `Placeholder = false`) | the built footprint (7 solid boxes + 2 air boxes), home z 0.47, display spot, portrait, audio (3 sources with their sends -2 / 0 / -2 dB), lessons, shadow, events, tips, features, anchors, budgets |
| `MarimbaModule` (Order 35) | builds `MarimbaInstrument`, parents the three sources; `StickPoses` -> mallet poses, `StickReleases` -> releases, `EndFrame` |
| `MarimbaInstrument` | `IInstrument`, `IStowable`, `IInstrumentPlayback`, `IGuideAnchors`, `ILessonTargets`, `ILessonAccompany`, `IContactSurface`; the rig (Zone, Handles); the arrange-mode quiet; Sunny's counters (`marimba.noRoll`, `.noSharps`, `.ringing`); the Manual dead-stroke tip (`musicroom.marimba.deadStroke`); desktop click (Shift mutes) |
| `MalletToolProvider : IHandToolProvider` | the two mallets; each head is a `Pointer.CreateTool(..., ToolKind.Mallet)` registered with `Input.RegisterExternal`; `IHeldBy` |
| `MarimbaToolMath` | forwards to Core's `HandToolMath` |
| `MarimbaUnit` (the unit's partials `RenderMarimba` / `CreateMarimba`) | `Share` and `Work` slots, `idleSkip`, `RecordUnit` in every public method, `ILoudnessReference` (Ode bar 1, 108 BPM, vel .55), `IUnitWorstCase.DriveWorst` (the seated worst for the platform's load test; the interface is a copy of the bowls', `Audio/Sound/Marimba/IUnitWorstCase.cs`, until the integrator moves it); the stub unit is deleted |
| Recording | `marimba` onsets are not `Held` (a bar rings through a loop boundary), `marimbaDamp` a Release, `marimbaUndamp` a State, all keyed by midi |
| `PlayStamp` | fingertip strokes and hand mutes mark the hand, mallet strokes the mallet |
| Sunny | `Resources/GuideLines/Marimba.json`, `Resources/VoiceManifests/Marimba.json` (the platform's formats), `Resources/Voice/tip_marimba_*.wav`; the lab's voice tool now writes into this project |

## 2. Repeated tracker samples (the phase-A risk)

`MarimbaPlay` assumes one tick per tracker sample. On the platform a frame can repeat a sample (`PointerInput` moves
`SampleT` only when a tip moves). `MarimbaController.TickPointers` gives every pointer a `Fresh` flag
(`MarimbaController.IsFresh`: a new `SampleT`, or no sample interval known, or 0.75 x the sample interval since the last
fresh one so a still hand still samples); `MarimbaPlay.Tick` skips a pointer that is not fresh (no approach-ring entry, no
stillness or rest check). Tests: `IX_RepeatedTrackerSamplesChangeNothing` (every IX scenario, each sample ticked twice:
the same events, times within half a frame) and `SampleGate_RepeatsAreNotFresh_AStillHandStillSamples`.

## 3. The footprint, the planner and the display

- **Bar tops 0.72 m** (`MarimbaGeometry.HSeat`, spec 0.70): the folded tubes' elbows clear the planner's knee box top
  (0.62 m). The nearest low part (tube A3) is 3.5 cm beyond the knee box.
- **Home z 0.47** (spec 0.50): the frame's back is 1.093 m from the eyes (wall 1.10).
- **The hands' field (air boxes).** Without them the planner seats Marimba@Front with the kit at Right, but the kit's
  hi-hat cymbal (y 0.77-0.79) then stands about 7 cm under the right hand's grip for the top octave and 4 cm above the top
  bars' front ends, and a crash cymbal hangs over the treble end. The Layout's measured check counts the same thing
  (`Overlaps3D` 1: the hi-hat stand against the rail, the treble board, the right mallet). With the air boxes the planner
  keeps them apart: the marimba and the kit never share a set.
- **Verdicts** (`MarimbaFootprintTests`): alone `Marimba@Front`; with the harp `Marimba@Front Harp@Left`; with the kit, or
  kit + harp: does not fit; with the hand drums, the handpan, the theremin or the bowls: `Marimba@Right+5` (they take
  FrontLeft or Left). From the default room (piano, kit, harp) the menu says No room and the ray brings it alone, as the
  stub did.
- **Display spot (2.30, 0, -0.80), yaw 90:** the frame on display spans world x 2.04..2.92 (window wall 3.20), z
  -1.43..-0.17, y 0..0.75 (`MarimbaPlatformTests.OnDisplayAtItsSpot_StowedAndNotPlayed`); `StowPlanTests` keep it clear
  of every other spot.

## 4. Screenshots (`Builds/Shots/marimba/phase-b/`, `MarimbaShotTests`)

On display under the window (`display_from_seat`, `display_close`, `display_window_wall`), mid-glide to the seat
(`glide_midway`), at the seat beside the harp (`seat_view`, `seat_view_wide`, `set_overview`, `set_three_quarter`), a
fingertip on E4 (`fingertip_on_e4`: the bar dips 1.4 mm), the menu portrait (`menu_portrait`: fill 72 %, off-centre 3 %,
luminance 0.62) and the Instruments grid ("With you: Marimba, Harp"). On display the frame is 8 cm short of the sill, 9 cm
from plant P3's leaves, 17 cm from the curtain and 17 cm from the ivy.

## 5. Open decisions for the user (by ear, on the headset)

- `BoundaryCrossfade` on or off (review item 13; off ships).
- The two A/B pairs `marimba-ab-*` (bass bloom, treble knock; A ships).
- Whether the marimba may share a set with the kit at the cost of the hi-hat under the right hand: delete the two air
  boxes in `MarimbaDefinition.SeatBoxes` (and RB1's two lines) to get `Marimba@Front Drums@Right`.
- Device checks M1-M9 (spec 9.3): M2 (contact solve <= 80 us: about 55 us expected), M4 / M5 (log the resting head jitter
  and the press depth; `BAND_BELOW`, `DEAD_WINDOW_MS`), M8 (listen), and the repeated-sample gate at the LOW tracking rate.

## 6. Risks still open

- **Mallet head jitter.** The 37 cm lever magnifies grip jitter about 3x at the head; the rules were tuned for up to 6 mm.
  M4 logs the resting head's RMS on the device; re-run `tools/ix-realistic.mjs` if it is above 6 mm.
- **Cost.** M1 decides (see `MarimbaLoadTests`' `[PERF]` lines against the 0.20 / 0.30 ms caps).
