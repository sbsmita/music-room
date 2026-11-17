# Hand drums: the sound unit (conga + tumba)

The hand-drum reference engine in Burst (`Tools/SoundLab/handdrums/engine/handdrums.reference.mjs`, v2; spec
`Docs/12-new-instruments/handdrums.md` sections 2.2–2.13). Two persistent modal drums (conga 170 modes in 43 MB4
groups, tumba 194 in 49) driven by hand-part contact patches through their force difference (DC-free), the cupped-hand
air pop into a Q≈2 cavity group, rests that damp, the tension glide, and budget scaling that sheds the quieter drum's
crack first. Unit-only role: no Classic synth.

Phase B: **registered** (`HandDrumsHooks.cs`: `SoundUnits.CreateHandDrums` -> `new HandDrumsUnit(ctx)`,
`UnitHooks.RenderHandDrums` -> `HandDrumsDsp.Render`): role `UnitId.HandDrums`, outputs `HandDrumLo` (channel 0, the
tumba) and `HandDrumHi` (channel 1, the conga), idle skip on (the modes are culled below 1e-11, so the tail reaches exact
silence: a bass at 0.01 s is silent from 1.39 s and the graph skips the unit; the first stroke after a skip sounds on the
same sample as in a fresh engine), the governor's `UnitSlot.Share` x the state's own share (1) scales the caps (a 1.2 s
roll on both drums: Share 0.4 does 47 % of the work), `UnitSlot.Work` written each block, every public method on the
test tap (`RecordUnit`), `ILoudnessReference` (open, open, slap, open on the conga at u 0.55, 108 bpm: +0.01 LU from the
piano's mf reference through the platform's `LoudnessParityTests`). The graph renders the standalone host's samples
bit for bit (`HandDrumsPlatformTests`).

## Files

| File | What |
|---|---|
| `HandDrumsState.cs` | `IHandDrumSound` (the module's API), `HandPart` / `PatchShape` / `HandStroke` / `HandRest`, the constants (`HandDrumsConst`, PARTS), the unmanaged layouts (`HandDrumsState`, `HandDrumsDrum`, `HandDrumsPatch`) |
| `HandDrumsTables.cs` | The binary identity table (`Resources/MusicRoomHandDrums.bytes`, "HDT1", 0.16 MB) and `HandDrumsBuilder`, which lays it out in unmanaged memory (one arena per drum) |
| `HandDrumsDsp.cs` | The audio thread: `HandDrumsDsp.Render(UnitSlot*)` (the hook body), the contact solve, the canonical strokes and rests, rests / lifts, budget masks and fades, the control step, the poles; MB4 free (4 groups interleaved) and driven (4 groups x up to 4 patches) loops; `HandDrumsBurst.RenderFast` / `RenderStrict` entries for tests |
| `HandDrumsSound.cs` | `HandDrumsSound` (IHandDrumSound -> GraphCommands over a sink) and `HandDrumsUnit : SoundUnit, ILoudnessReference` |
| `HandDrumsHooks.cs` | The platform's two partials (`CreateHandDrums`, `RenderHandDrums`) |
| `IUnitWorstCase.cs` | A copy of the bowls' load-test hook (CONTRACT-REQUESTS R6: deleted when it moves to `Audio/Burst/Units`); `HandDrumsUnit.DriveWorst` plays the live maximum (every hand on every drum at its 40 ms window) |
| `HandDrumsOffline.cs` | A standalone host that does for the unit what `AudioGraph.RenderUnits` / `CollectUnit` do (When rules, LateDrop, (At, Seq) order) |
| `Tools~/export-handdrums.mjs` | Writes the table from the lab (`node export-handdrums.mjs`) |
| `Tools~/export-refs.mjs` | Copies the lab's vectors into the tests' `Reference~` folders and writes the Node dry renders of the demo phrases (`Tests/EditMode/Audio/HandDrums/Reference~/dry/`) |

The table holds every value at the precision the reference holds it (float32 tables as float32 bits, doubles as
doubles, plus the constructor's cos / sin of w0, outGain and the bank's gains), so no libm difference reaches the
poles: the app loads it and never re-draws the dense field (spec 2.2, "Which random drum").

## Commands (`GraphCommand.Op`, 64 bytes, the unit's own ring)

| Op | Fields | Sent by |
|---|---|---|
| 1 Patch | `Target` drum; `N` = hand \| part<<4 \| shape<<8 \| count<<12; `A` u; Points: `B..G` = ρ0 θ0 ρ1 θ1 ρ2 θ2; Line: `B` ρ0 `C` θ0 `D` ρ1 `E` θ1 (4 points at k/3); `When` + the patch's delay; **LateDrop** | `Patch` (live strokes) |
| 2 Rest | `Target` drum; `N` = hand \| npts<<4 \| part0<<8 \| part1<<12 \| part2<<16; `A..F` ρ0 θ0 ρ1 θ1 ρ2 θ2 | `Rest` |
| 3 Lift | `Target` drum; `N` hand (never LateDrop) | `Lift` |
| 4 Reset | `N` 1 = reseed both drums from `Seed` | `HandDrumsSound.Reset`, `HandDrumsUnit.Seed` |
| 5 StrokePatch | `Target` drum; `N` = hand \| kind<<4 \| patchIndex<<8; `A:B` u as a double, `C:D` θ offset as a double, `E` ρ offset; `When` + the patch's delay; **LateDrop** | `Stroke` (canonical geometry, expanded on the audio thread in double, exactly as the reference's playStroke) |
| 6 RestCanon | `Target` drum; `N` = hand \| kind<<4; `C:D` θ offset (double) | `RestCanonical`; `Stroke(Mute / SlapClosed)` adds its rest after 12 ms for 0.25 s |

Times follow `GraphCommand.When` (0 ASAP, > 0 absolute, < 0 relative); a patch's delay is added (absolute: later;
relative: further). Drum indices are 0 = conga, 1 = tumba everywhere; only the render maps drum d to `u->Channel(1 - d)`
(the platform's source order `HandDrumLo, HandDrumHi`).

## Verified (EditMode, `Tests/EditMode/Audio/HandDrums/`, 28 tests)

- **Bit-exact with Node through the Burst Strict entry**: the 3 s test-vector scenario's FNVs (conga `0x86845898`, tumba
  `0x19d1f530`), 0 samples different; the Share 0.4 scenario's FNVs, cap 37, 9 fades, Work 44 666 / 54 560; the six
  contact probes' dF FNVs; the open tone's per-mode gains and the RNG after it.
- **Burst FloatMode.Fast (the graph's mode)**: first samples within 2.3e-8, every window rms and peak within 0.0001 dB,
  null residual −114.7 / −116.9 dB over 3 s. (The editor's managed Mono path evaluates float expressions in double before
  rounding: −147 dB, not bit-exact, so the Strict entry is the bit-exact check.)
- **The demo phrases** (ladders, techniques, tumbao, conga line) against the Node dry renders: −110 … −120 dB per drum;
  the listening WAVs made the lab's way (direct path, MasterBusV2 at the app's gain, one gain) against the Node WAVs:
  −112 … −114 dB, every 1/3 octave within 0.00 dB, the same LUFS.
- Stroke character S1–S4, H1–H3 as the lab (conga S1 11.4 dB, S4 8.5 dB / 2.00x, H1 −18.6 dB, H2 9.6 / 12.0 dB,
  H3 −11.6 / −10.4 dB; tumba 12.2, 9.6 / 2.11x, −18.3, 9.2 / 12.9, −7.0 / −6.2), T1 65.6 vs 9.6 dB, T3 79.7 dB, G1/G2,
  F1, the busy passage (click score 2.11), no subnormals and no active group 12 s after a stroke, no energy below 60 Hz
  (≤ −45.7 dB), |mean|/rms ≤ 0.0013, budget scaling on the slap roll (Work 43.6 %, ≤ 37 groups, click 0.02, core groups
  never shed), loudness parity with **the real Unity piano** (tone phrase 0.00 LU; open x4 +0.33 / +0.66 LU), no
  allocation in the API or the render, determinism, the channel map.
- The glide's s(t) (review item 12): conga u .8 peak 32 c at 7 ms, u 1 51 c at 6 ms (34 c at 20 ms, 9 c at 50 ms, 1 c at
  100 ms); tumba u 1 60 c at 7 ms.

## Cost (PlayMode, `Tests/PlayMode/HandDrums/HandDrumsLoadTests.cs`, `Tools/unity.sh test-perf`)

Editor, Burst Fast, Apple M5 Pro, µs per 256-frame block (average / p99 / median-run worst), and the Quest projection at
the project's measured ratio for the new sounds (x4; README: "the Quest 3S about 3-4x slower"):

| Schedule | Editor | Quest x4 (cap 150 avg / 250 worst) |
|---|---|---|
| both drums ringing | 3.5 / 16.7 / 19.3 | 14 / 67 / 77 |
| tumbao | 2.5 / 19.8 / 30.9 | 10 / 79 / 124 |
| two-hand roll, both drums, 16 slaps/s each | 15.4 / 37.0 / 44.8 | 62 / 148 / 179 |
| live max (every hand on every drum at its 40 ms window) | 26.6 / 37.6 / 49.6 | 106 / 150 / 198 |
| adversarial (every 12 ms; unreachable from live play) | 58.8 / 107.5 / 122.4 | 235 / 430 / 490 |
| adversarial at Share 0.4 | 46.7 / 91.8 / 111.5 | 187 / 367 / 446 |

The adversarial case is dominated by scalar work (patch set-up and the double contact solve), which Share does not
shed. The stroke logic's 40 ms window per hand per drum makes it unreachable live; only overlapping loop layers could
approach it. T-HD-CPU on the device replaces these projections.

Memory: 191 KiB of unmanaged state and tables (both drums), 0.16 MB table asset, nothing baked.
