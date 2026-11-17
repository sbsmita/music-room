# Hand drums: phase B (wired into the platform)

Phase B ran in a copy of the instruments base project (Assets fingerprint `c2a848bb…`). Phase A's
folders came over from the phase-A copy (read-only), keeping the stub's `MusicRoom.HandDrums.asmdef.meta` (GUID
`80ddf04dbf358463e8cf48571c528dcb`), `HandDrums.meta` (`e635e33fbd2844c4a80a60e500361691`) and
`Audio/Sound/HandDrums.meta` (`b488c65f98ded4b50884e18921f46aae`), and the stub's own file metas
(`HandDrumsDefinition`, `HandDrumsModule`, `HandDrumsFixture`, the lesson pack). Phase A's two test asmdefs were dropped:
the tests compile into `MusicRoom.Tests.EditMode` / `.PlayMode`. The stub unit (`HandDrumsStubUnit.cs`, its
`IHandDrumsSound`) is deleted; `HandDrumsDescriptor.cs` (phase A's plain values) became `HandDrumsDefinition`.

## What phase B did

| Part | Done |
|---|---|
| Descriptor | Real values (`Placeholder = false`): seat, display spot moved clear of the ivy (2.42, -1.94), final boxes, portrait + icon, audio (per-source send override -4 / ER +2 dB: phase A's request 5 needs no shared change), lessons, events `handDrum` (Onset, Kind) and `handDrumMute` (State, Kind, not sampled: a sampled value is kept per type, not per hand and drum), tips as `TipSpec`, features, anchors, budgets |
| Module, instrument | `HandDrumsModule` (Order 34) builds `HandDrumsInstrument` and parents the sources; every interface of BUILD-GUIDE 4.2 that applies |
| Unit | `HandDrumsHooks.cs` (the two partials); `Register(..., idleSkip: true)`; Share from `UnitSlot.Share`; `UnitSlot.Work`; `RecordUnit` on every public method; `ILoudnessReference` |
| Recording | The mute relabel through `IRecorder.Add(ev, atAudioTime)` (README "How a stroke is made"); rests and lifts as `handDrumMute` |
| Playback, lessons | `IInstrumentPlayback`, `ILessonTargets` with the hand-print cue (`HandDrumsCue`, built in phase B) |
| Sunny | Tips with the platform's presets (OpenTone, PalmBass, CuppedSlap, HeelToe, Roll, PalmRest) and two Custom motions; the lesson tip on `TipTrigger.LessonStart` with `TipSuccess.LessonHit` (phase A's request 7 needs nothing shared); lines and the voice manifest in the platform's shapes (request 8 settled); the heel-toe line shortened to 3.15 s |
| Stowing | The display set (`HandDrumsGeometry.BuildDisplay`: 4 768 triangles, the same 3 draws) |
| Model | The stand's feet moved 5 cm toward the player (the tilted drum's weight well inside them; the back foot within the footprint) |
| Palm lift | Found by reflection: wired as soon as `MetaHandVisual.SetPalmLift` exists (request R1) |
| Lab | `tools/voice.mjs` writes the platform's shapes into this clone; `tools/lessons.mjs` adds `hand`, `meter`, `unit`, `credit` and writes the pack here |

Phase A's requests 1 (zones: the platform keeps no zone list), 2 (the tail rule: written as this folder's own test,
`ThePitchedSkinTailRuleHoldsOnBothDrums`), 4 (seat numbers: in the descriptor, the planner's data in R3), 5 (room send:
descriptor override), 6 (demo hook: the platform's presets), 7 (triggers), 8 (line shapes) and 9 (registration) are
settled. `CONTRACT-REQUESTS.md` holds what is left.

## Device checks (ARCHITECTURE 19 additions, handdrums.md 6.3)

T-HD-CPU (the unit's block cost under the governor; the dents' main-thread cost), T-HD-LAT, T-HD-RATE, T-HD-CLASS,
T-HD-JITTER, T-HD-REACH (with review item 13's additions: open tones at the near edge, which sits at 0.712 m, the
open-tone palm 8.3 cm over the knee box top; if anyone touches a knee, raise LiftMin to 0 or reduce the tilt to 10
degrees; record through the lens that the drawn palm stays on the skin with R1 applied), T-HD-MR (the drums on the
floor as the lead; the real hands), T-HD-LISTEN (the listening checklist in `Builds/Listen/handdrums/`,
including the pitch drop on hard basses and `handdrums-glide-ab.wav`; the designed glide is the default).

## The chain (2026-11-01, this clone, with CONTRACT-REQUESTS R1-R6 applied locally)

compile, render-setup, bake, scene: clean. `test-edit` 1032 / 1032 (the base's 962 + 70 hand-drum tests); `test-play`
409 / 409; `test-play --gpu` 445 / 445; `test-perf` 16 / 16 (the GPU and perf runs under the GPU lock). The
conformance suite's six cases for the hand drums pass. Then the shared files were restored to `instruments-base`'s bytes:
with them, every hand-drum test passes except `FastMotionDriverTests.TheHandDrumsLiveOnsetsAskForFastMotionLikeDrumHits`
(R2) and the palm-lift check is skipped (R1); `StowPlanTests`, `SetLayoutTests`, `AudioEngineTests` and
`EightInstrumentsLoadTests` need R3-R6.
