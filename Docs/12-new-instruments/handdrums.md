# 12 · New instruments · handdrums: a pair of congas played with bare hands (implementation-ready spec, v2)

Key: `handdrums`. Target: this Unity project (Unity 6000.3, Meta XR SDK 207), as one new
instrument module (`Assets/MusicRoom/HandDrums`, asmdef `MusicRoom.HandDrums`) and one sound unit on the Burst audio graph
(`Assets/MusicRoom/Audio/Sound/HandDrums`), inside the id block and registry of `platform.md` (InstrumentKind / RigId /
UnitId 6, sources `HandDrumLo` + `HandDrumHi`, carousel 6, persisted id `handdrums`, name "Hand drums").

Nothing in the Unity project or the clones was changed to write this. Every sound comes from our own code: no samples,
impulse responses, third-party assets or services. Published measurements are used only as numbers (section 9).

| Item | Where |
|---|---|
| Reference engine (transliterate this) | `Tools/SoundLab/handdrums/engine/handdrums.reference.mjs` (632 lines, Node 26, no packages; imports the canonical MB4 helpers of `Tools/SoundLab/burst/lab/kernels.mjs`) |
| Bare-hand stroke logic (transliterate this too) | the lab's `tools/interaction.mjs` (`HandDrumInput`, `classify`, `palmLiftTarget`, `stepPalmLift`, `sendStroke`) |
| Head-dent reference | the lab's `tools/visual.mjs` → `out/visual-vectors.json` (in `Tests/EditMode/HandDrums/Reference~/`) |
| Table data (every derived table) | `out/handdrums-tables.json` (version 2, 385 KiB; in `Tests/EditMode/Audio/HandDrums/Reference~/`) |
| Objective checks | `out/verify.json` (50 checks, all pass), `out/parity.json`, `out/sweep.json`, `out/bench.json`, `out/seatcheck.json` |
| Test vectors | `out/test-vectors.json`, `out/vec-scenario-conga.f32`, `out/vec-scenario-tumba.f32` |
| Scripted-hand cases | `out/interaction-cases.json`: 18 cases × 4 app/tracker rate pairs (72/72 pass) and 88 seeded-noise cases (88/88 pass) |
| Lesson pack | `out/handdrums-lessons.json` (3 lessons, unchanged) |
| Renders (10 WAV, 50.8 MB) and the listening guide | `Builds/Listen/handdrums/handdrums-*.wav`, `handdrums-README.txt` |
| The v1 critique's own scripts, re-run on v2 | the lab's `critic/*.mjs` → `critic/out-v2/` (their v1 results stay in `critic/out/`) |
| v1, for comparison | the lab's `v1-snapshot/` (code, outputs, this spec as `handdrums.v1.md`) |
| Run everything (about 55 s on the M5 Pro) | the lab's `run-all.sh` |

**Status.** v2, revised after an independent critique (verdict "ready with fixes"). Built and measured in Node.
**Nobody has listened to it yet.** The renders and the README are the gate.

---------------------------------------------------------------------------------------------------------------------------

## v2. The critique, issue by issue

All high and medium issues are fixed. One of the critic's suggested numbers was not taken as written (the 150 ms approach
window: 100 ms, because 150 ms still let 0.1 m/s placements through); the goal behind it is met. "c5c", "c3b" and so on are the
critic's scripts in `critic/`, re-run unchanged against v2 (`critic/out-v2/`). The critique text this revision worked
from was cut off inside M5 ("The head-dent verte…"); the last two rows are inferred from the critic's scripts that had no
issue text (`c4-room.mjs`, `c7-bench.mjs`), and the head dent is specified in full (4.5) in case that item asked for it.

| # | Sev. | Issue | What changed | Evidence, v1 → v2 |
|---|---|---|---|---|
| H1 | high | Tracking noise and slow placement make phantom strokes (single-frame velocity; V0 0.15; ARM 12 mm) | Velocity = least-squares slope over the last distinct tracker samples spanning ≥ 50 ms (3–6 samples), timed by the sample timestamps. V0 0.25 m/s (palm group 0.30). ARM 20 mm on the 2-sample mean height. A crossing needs an armed group that was fully ≥ ARM within 100 ms, a group mean fall speed ≥ 0.7 V0, and 2 descending samples (3.3) | c5c (5 placements per row, then a 30 s hold, σ 1–3 mm per joint or whole hand): **0 placement strokes and 0 strokes/min in every row** (v1: up to 13 per 5 placements and 174/min). c5 study B: 0 in all 40 rows (v1: 34 rows with false strokes). New C08b (σ 2 mm per joint + 2 mm whole hand, held at −4/0/+6/+12 mm, 60 s, 72/90 Hz app × 60/30 Hz tracker): 0 strokes in 32 runs. New C09b: 0 strokes in 160 slow placements |
| H2 | high | A slap at natural speed reads as an open tone (one-frame look-ahead, 28 mm at 2 m/s); slaps and opens silent under low finger confidence | Classification on the pose interpolated to the first crossing instant t*: a point lands if it is within 6 mm of the band at t*, or within 12 mm and reaches it within 8 ms. A curled-tip test backs the slap up (≥ 30°; flat fingers below 12° never slap). Low finger confidence sends the measured fingers line as an open tone instead of silence | c5 study C, canonical slap, σ 2 mm: **2/3/4 m/s → 40/40, 40/40, 40/40 slap** (v1: 5, 0, 0 of 40). Open at the same speeds 40/40 open. New C03b–d / C01b–d at 72/90 Hz and 60 Hz tracker: slap ≥ 97.5 %, open 100 %. C12 now expects the open tone |
| H3 | high | A dead ring between bass and open where a hard flat hand becomes a quiet "touch" | The palm sends a palm patch wherever it lands inside ρ 1.0 (label bass inside 0.8, open outside); landing flat fingers ride along as a fingers line; touch only below u 0.25 (and for fingers that land only on the bearing edge: a rim knock); a patch at the bearing edge drives the head with its on-head part | c5b: no "touch" for the hand pointing away at 1.5 m/s (v1: ρ 0.70–0.85); 2 for a hand turned 90–120° that lands along the rim. New end-to-end sweep (`tools/sweep.mjs`, 336 strokes through the live logic and the engine): **0 strokes at u ≥ 0.5 more than 6 dB A below the open tone** (worst −5.3 dB, a bass). c5 study A: 1 touch of 384, a rim knock (v1: 91 touch or touch + X) |
| H4 | high | The slap is a click on a dull tone: a pop-dominated peak, nothing above 4 kHz, a crack of 10–15 ringing lines | Dense field 28 → 120 modes from the listed-mode limit to 8 kHz, stratified, each slot weighted by √(real head modes it stands for), T60 55 → 12 ms (overlapping). Hertz-like contact-pressure average 3(sin x − x cos x)/x³. Fingertip pads: Hertz (exponent 1.5), k 2e7, m 5 g: a step-like force onset. Pop: POP_K 110 → 52, cavity Q 4 → 2. S4 back to 8 dB / 1.6× | c3b, conga / tumba slap u .8: **above 4 kHz −18.6 / −18.3 dB** (v1 −34.8 / −37), above 2 kHz −14.1 / −13.4 (v1 −17.6 / −21), pop share of the > 2 kHz energy −18.8 / −18.3 dB. Pop share of the peak −11.6 / −7.0 dB (v1 −5 / −1.5). 2.5–6.3 kHz peak-to-median 6.4–12.9 dB (v1 17–50; decaying noise in the same measure 9.6 median, 12.6 p99). S4 8.5 / 9.6 dB, c50 2.0 / 2.1×. Slap +2.5 / +2.2 dB A over the open tone |
| M1 | med | Parity only through the K-weighted bass; tone strokes 3–4 LU low; tumba bass too strong (+12 dB A at u 1) | Parity phrase = open-open-slap-open (conga, u .55), ±1 LU; open-only phrases ±2 LU. The palm's (and heel's) speed is capped at 1.3× the hand speed that gave u. rad.B 1.55 → 1.2 (conga), 2.0 → 1.3 (tumba), as asked. Palm 0.25 → 0.18 kg, k 3e8 → 6e8 (2.10). B1 on both drums. Trim +3.97 → +7.86 dB | c2: open ×4 **+0.3 / +0.7 LU** (v1 −3.5 / −3.2), slap ×4 +0.2 (v1 −4.3), **bass ×4 +0.2 / +0.1** (v1 +2.1 / +4.1). Tumba bass at u 1: **+3.5 dB A** re the u .8 open (v1 +12). Bass −4.8 / −4.0 dB A re open (B1 −6…+2); S2 still passes (tone partial −0.9 / −1.2 dB under the bass) |
| M2 | med | The renders are far more processed than the app (+4 to +14.5 dB into the master; 6 dB limiting on 7 of 8 files) | Every file: direct path [+ room] → MasterBusV2 at the app's own input gain → one linear gain to −18 LUFS or −1.2 dBTP, whichever comes first. Two `-app` files are the app's output (−0.25 dB) | The master's limiter acts only on the conga-line files (its accents, −2.6 dB, 0.08 % of the time); the leveler at app level −0.4 … −2.6 dB. All files below −1 dBTP (c1). Levels −18.3 … −26.9 LUFS, reported per file (section 8) |
| M3 | med | One landing makes two strokes | A stroke disarms both groups of that hand on that drum; they re-arm only when all its striking points are above ARM (the toe after a heel rest excepted) | c5 study D at a 30 Hz tracker: **bass+bass 0/24** (v1 13/24); c5b slow bass: one stroke each (v1 touch+bass 7/16). New C13b (fingers-first and palm-first, 0.3 / 0.6 m/s, 6 rate pairs): exactly 1 stroke in 24/24 |
| M4 | med | The hand visibly sinks 2–8 cm into the head on bass strokes | `SetPalmLift` is now a required contract request: lift = the palm/heel skin's depth under the head up to 60 mm, fading to 0 at 90 mm, attack instant, release τ 25 ms, only over a head (4.5). Reference `palmLiftTarget` / `stepPalmLift` with vectors. A through-the-lens video check in T-HD-REACH | C02 trace: 10.5 mm lift at the bottom of a 12 mm deep bass, released in about 75 ms |
| M5 | med | Parts of the spec need guessing | The reference velocity IS the C# implementation's (no C#-only addition). Budget scaling is implemented in the reference with a vector (Share 0.4). `Patch` takes the stroke value u (the unit computes V). Channel map: Register in the platform order (Lo, Hi), drum d → channel 1 − d. The back-of-hand axis is `rotation[Palm] · up`. The head dent has a reference (`tools/visual.mjs`) and vectors; F max comes from a table, not a main-thread solve. Command layouts fit 64 bytes (line = 2 ends, ≤ 3 explicit points) | sections 2.12, 2.13, 3, 4.5, 6 |
| – | (c4) | Room wetness was not reported | Reported, per ear and over both ears, with the spec's ErOrigin | DRR over both ears **5.9 dB (conga), 4.5 dB (tumba)**: inside the platform's 6.1 ± 2 dB. Per ear 7.6 / 3.0 and 2.0 / 6.0 dB: the far ear sits low because a lateral source's direct sound is head-shadowed, which the centred piano never is (2.11) |
| – | (c7) | The CPU estimate folded the scalar work into "modal" | The bench now times the scalar parts (× 1.5) apart from the kernels (× 0.10), and also gives a deterministic op-count estimate per block | Quest estimate (2.13): roll 42–53 µs average, 88 µs worst (op count); inhuman adversarial 108–129 µs average, 177 µs op-count worst; its timed worst block is 241–421 µs depending on the shared machine's load. Cap 150 / 250 µs |

Also changed while fixing these (all measured in 2.11): the dense modes' wavenumber now includes the hide's bending
stretch, as the listed modes do; each drum's random draws are a representative realization (the median of 24, not the
best); a patch centred on the bearing edge drives the head with its on-head part; per-stroke landing angle ±0.25 rad
(v1 ±0.2); stroke strength u follows the lead point's current speed once the noise gate has passed.

---------------------------------------------------------------------------------------------------------------------------

## 0. Decisions

1. **Congas, not bongos: a pair (conga 11.75", tumba 12.5") on low basket stands, in front of the seated player.**
   - **Target size against tracking error.** Conga heads are 29.2 / 31.2 cm across. Bongos are 18 / 21 cm. The stroke
     zones that separate the strokes (palm centre, near-edge band, mid-head fingertips) are 5–9 cm wide on a conga, and
     the joint centres jitter by a few millimetres (fingertip error about 1.1 cm, Abdlkarim et al. 2024). On a bongo the
     same zones are 3–5 cm, and the technique is fingertip-only (martillo), which is exactly where hand tracking is weakest.
   - **The requested vocabulary is conga technique.** Open tone, bass, slap, muted (closed) tone and heel-toe (manoteo)
     are the conga's basic strokes (Wikipedia "Conga"). A bongo has no palm bass, and its heel-toe is marginal.
   - **Seated within about 2 ft.** Bongos are played between the knees. In VR that puts the strike plane at lap
     height, 0.55–0.62 m, right on the player's real thighs: a hard stroke would hit the real leg, and hands at the lap
     track poorly (looking straight down, hands occluding each other). Conga heads stand on their own at 0.75 m, 13 cm
     above the knees (`registry.mjs` knees box top 0.62 m).
   - **A big, visible, calm object** that reads instantly from a seat, and two pitched drums that make little tunes.
2. **The physics decides the sound; the classifier decides only the name.** The hand logic measures which hand parts
   land where and how fast, and sends those *contact patches* to the engine. The sound follows from patch size, mass,
   position and speed. The stroke name (open, bass, slap…) is for lessons, Sunny and the recorder. v2 makes this true
   everywhere: no hand position is sent as a quieter part than the one that landed (H3).
3. **Each drum is one persistent modal object.** A stroke adds energy to its state, so rolls build, flams interfere,
   and a resting hand damps whatever is ringing, as on a real drum. Worst-case CPU is fixed by the mode count, not by
   the stroke rate, and the budget scaling sheds the high groups first.
4. **Unit-only role.** There is no classic synth for the hand drums. "Classic sound" never touches them
   (`platform.md` 10.1).
5. **No glowing UI.** Feedback is physical: the head dents under the hand, the fingers stop on the head (E1), the palm
   is lifted onto the skin (4.5), soft contact shadows (E3). Lesson targets are a pale, matte hand-print on the skin.
6. **A noisy tracker must never play the drum.** A stroke needs a real approach from 2 cm up at a real speed; holding,
   hovering or laying a hand down only damps (3.3).

**What it costs (sections 2.12 and 2.13).**
- CPU, Quest Burst estimate: 7–18 µs per 256-frame block for the tumbao, 42–53 µs for a two-hand roll on both drums,
  108–129 µs for an inhuman worst case (op-count worst block 177 µs). The platform cap is 150 / 250 µs.
- Memory: 164 KiB of state and tables, and nothing baked.
- 92 modal groups at most (43 + 49). That is 7.7 % of the graph's default LOD budget of 1200; at Share 0.4 the unit
  keeps 37 groups and its work halves.
- 3 draw calls at the seat (the cap is 6) and 3 stowed.

---------------------------------------------------------------------------------------------------------------------------

## 1. Files (lab)

| File | What it is |
|---|---|
| `engine/handdrums.reference.mjs` | Engine: data (`DRUMS`, `PARTS`), table build (`buildDrum`), `HandDrums` (two `Drum`s), budget scaling, the contact solve, the kernels, rests, glide, control grid, and the canonical strokes (`STROKES`, `RESTS`, `playStroke`) |
| `lib/measure.mjs` | A-weighting (IEC 61672), A- and K-weighted window levels, spectral peak search (on top of `Tools/SoundLab/drums/lib/analysis.mjs`) |
| `tools/calibrate.mjs` | Freezes `kappa` (glide) and `level` per drum into the engine (`--write`) |
| `tools/parity.mjs` | Loudness parity against the piano's mf reference (tone phrase), per-stroke phrases; writes `OUTPUT_TRIM_DB`; the platform's unit checks |
| `tools/strokelab.mjs` | The critique's slap metrics in one fast report (tuning aid; `verify.mjs` uses its measures) |
| `tools/verify.mjs` | The 50 objective checks → `out/verify.json` |
| `tools/sweep.mjs` | The flat-hand sweep through the live logic and the engine → `out/sweep.json` |
| `tools/balance.mjs`, `tools/probe.mjs` | Stroke balance; single-stroke probe (tuning aids) |
| `tools/vectors.mjs` | Test vectors → `out/test-vectors.json`, `out/vec-*.f32` |
| `tools/export-tables.mjs` | Table data → `out/handdrums-tables.json` |
| `tools/visual.mjs` | Head-dent reference → `out/visual-vectors.json` |
| `tools/bench.mjs` | CPU (5 schedules, timed split and op counts), memory → `out/bench.json` |
| `tools/patterns.mjs` | Demo content: tumbao, conga line, ladders, technique tour |
| `tools/render.mjs` | WAV renders → `Builds/Listen/handdrums/`, `out/renders.json` |
| `tools/lessons.mjs` | Lesson pack → `out/handdrums-lessons.json` (with playability checks) |
| `tools/interaction.mjs` | The bare-hand stroke logic (reference), the palm lift, the scripted and seeded-noise cases → `out/interaction-cases.json` |
| `tools/seatcheck.mjs` | The platform seat planner, imported read-only, with this footprint → `out/seatcheck.json` |
| `critic/` | The v1 critique's scripts (unchanged) and their outputs: `out/` (on v1), `out-v2/` (on v2) |

---------------------------------------------------------------------------------------------------------------------------

## 2. Sound

### 2.1 The instruments (sources in section 9)

| | Conga (left, `HandDrumHi`) | Tumba (right, `HandDrumLo`) | Source |
|---|---|---|---|
| Head | 11.75" (29.8 cm) rawhide; vibrating radius a = 0.146 m | 12.5" (31.8 cm); a = 0.156 m | WIKI: conga 11.5–12", tumba 12–12.5" |
| Shell | staved ash barrel, 0.762 m tall, belly Ø 0.335 m, bottom opening Ø 0.215 m | 0.762 m, belly Ø 0.36 m, bottom Ø 0.235 m | WIKI: ~75 cm |
| Rawhide areal density σ | 1.35 kg/m² (about 1.1 mm of hide at 1.2 g/cm³) | 1.35 kg/m² | est. |
| Open tone (coupled (0,1)) | **261.6 Hz (C4)** | **196.0 Hz (G3)**: a perfect fourth below, a common pair tuning | WIKI tunings; R00: a measured open tone ~240 Hz |
| The head's own (0,1) | 0.93 × the tone = 243 Hz: the air column pushes the coupled tone up | 182 Hz | design, see 2.2 |
| Tension T = σc², wave speed c | 11.6 kN/m, 92.8 m/s | 7.45 kN/m, 74.3 m/s | derived |
| Bass (head + air column) | **78 Hz** | **70 Hz** | R00: bass ~70 Hz; DJ: djembe Helmholtz ~75 Hz |
| Second air mode A2 | 300 Hz | 250 Hz | R00: a "280 Hz harmonic" in the bass stroke |

The tone pitches sit on C and G, so the drums agree with the room's C-major lessons.


### 2.2 Modes (all tables in `handdrums-tables.json` `drums.<name>.modes`)

Each drum is one bank of modes in MB4 groups (`burst.md` 4): conga 170 modes in 43 groups (cavity group 42), tumba 194 in
49 (cavity group 48). The kinds, in bank order:

| Kind | Count (conga / tumba) | Frequencies | T60 | Shape at the head φ(ρ, θ) | Modal mass M |
|---|---|---|---|---|---|
| `B` bass (head + air column, in phase) | 1 | fB | 0.30 / 0.34 s | J0(2.405 ρ) | 3.2 M01 (the air column's mass adds) |
| `T` tone (head (0,1), air against it) | 1 | fT | 0.85 / 1.0 s | J0(2.405 ρ) | 1.15 M01 |
| `A2` second air mode | 1 | fA2 | 0.22 / 0.24 s | J0(2.405 ρ) | 2.2 M01 |
| `MEM` head Bessel modes (m ≤ 24, n ≤ 10, without (0,1)), up to maxHz 1.4 / 1.3 kHz | 41 / 62 | f01m · j_mn/2.405 · √(1 + 1.5e-4 j²) (thick-hide stretch); m ≥ 1 in two orientations split ±0.25…0.75 % (uneven tension) | 0.62 s · (f/400)^−0.9, clamped to 0.035…0.70 (tumba 0.66, max 0.75); the second orientation × 0.93 | J_m(j ρ) · cos or sin (mθ) | σπa² J_{m+1}(j)² ε_m (ε 1 for m = 0, ½ otherwise) |
| `DENSE` statistical field (the slap's crack) | 120 | **v2:** slot i of 120 log-spaced slots from maxHz to 8 kHz, f = from·(8000/from)^((i + 0.4·bip)/119): one mode per slot, jittered within ±40 % of it | 55 → 12 ms (tumba 60 → 13 ms), log-interpolated over i | random profile sin(π(1−ρ)(1+3rs)) √2 √(1−ρ²/2), random sign, × (0.75 + 0.25 cos(mθ+φ)), m 0…7 | σπa² / (2 max(1, j/π)), j from f by the same stretch law (below) |
| `SHELL` staved-barrel ring modes | 4 | 272, 521, 846, 1238 Hz (tumba 248, 476, 772, 1130) × (1 ± 1 %); n = 3…6 ovalling of a 16 mm ash tube | 0.14 → 0.06 s | rimShare(ρ) = smoothstep(0.7, 1, ρ) (forces near the bearing edge) | 0.5 kg |
| pad (to a group boundary) | 0 / 3 | | | 0 | |
| `CAV` cupped-hand cavity (own last group) | 2 | 1.7, 2.6 kHz (tumba 1.65, 2.5); × (1 ± 8 %) per stroke | **v2: 2.6, 1.7 ms (tumba 2.7, 1.8): Q ≈ 2** (v1 Q ≈ 4) | driven only by the air pop (2.6) | 1 |

M01 = σπa²J1(2.405)² (conga 0.0244 kg).

**The dense field, v2 (critique H4).** v1 had 28 modes from 1.25 to 6.5 kHz with 28–100 ms decays: in a 1/3 octave at
4 kHz there were 2–3 separate lines, and the crack measured 18–50 dB peak-to-median (decaying noise reads 5–11 dB). v2:
- **Count and span:** 120 slots from the listed modes' limit (no double count) to 8 kHz.
- **Overlap:** T60 55 → 12 ms gives bandwidths of 40–180 Hz against slot spacings of 20–120 Hz, so neighbouring modes
  overlap (modal overlap about 1.5–2) and the field reads as decaying noise, not lines.
- **Stratified frequencies:** one mode per slot, jittered inside it. v1's ±2 % of f was wider than a slot, so modes
  bunched into peaks and left gaps.
- **Each slot carries the modes it stands for.** A circular membrane has N(j) ≈ j²/4 modes below wavenumber j (Weyl), so
  slot i holds ΔN = (j_hi² − j_lo²)/4 real head modes: 1.3 at 1.4 kHz, 6 at 3 kHz, 21 at 8 kHz on the conga (2.1 / 9.7 /
  31 on the tumba, whose head is bigger and slacker). Their energies add, so the slot's radiation carries √ΔN (2.5).
  v1 gave each slot one real mode's worth, and the field fell about 12 dB per octave above 2 kHz.
- **The stretch in the wavenumber:** j for a dense mode at f solves f = f01m (j/2.405) √(1 + β j²):
  j² = (√(1 + 4β X²) − 1)/(2β), X = 2.405 f / f01m. The stiff hide's high-frequency waves are longer than an ideal
  membrane's. v1 used X, which put the tumba's fingertip average at its first zero near 6 kHz.

**Which random drum.** The draws (splits, dense profiles and frequencies, shell detune) come from `Rng(seed)` =
mulberry32 with the drum's `seed`, in the order of `buildDrum`. The dense field is a statistical stand-in, so its draws
matter: across 24 seeds (final parameters) the slap's > 4 kHz share spans −21.3 … −17.0 dB (tumba −21.6 … −17.1), its
crack peak-to-median max 9.6 … 16.6 dB (10.3 … 17.2) and S4 7.2 … 9.3 dB (8.6 … 12.3). v2 takes, per drum, the seed
**closest to the median** of those four measures (> 4 kHz share, peak-to-median mean and max, S4): conga `0x4019462e`,
tumba `0xb7a99083`, ranked 13th and 16th of 24 by brightness. v1's seeds (`0x5ca1ab1e`, `0x7b3a9d21`) sat 2.2 spreads
from the median on both drums. That is a representative drum, not the best one. The C# unit loads the exported table; it
does not re-draw.

**Group classes (for the budget, 2.13):** 0 core (B, T, A2, head modes below 700 Hz, shell, cavity), 1 head (listed modes
≥ 700 Hz), 2 dense; a mixed group takes its most essential class. Conga `000 11111111 222…(30)…2 00`: 5 core, 8 head,
30 dense groups; tumba 8 core, 12 head, 29 dense. `shedOrder` lists the dense groups highest first, then the head groups
highest first (38 / 41 entries).

**Ratios** (`verify.json` `modes`, conga; the tumba is the same to 0.004):

| Mode | (1,1) | (2,1) | (0,2) | (3,1) | (1,2) | (4,1) | (2,2) | (0,3) |
|---|---|---|---|---|---|---|---|---|
| Hz | 386.9 | 519.8 | 559.7 | 646.4 | 710.6 | 768.8 | 854.1 | 880.4 |
| re the tone | 1.479 | 1.987 | 2.140 | 2.471 | 2.716 | 2.939 | 3.265 | 3.365 |
| re the head's own (0,1) (ideal membrane, R92) | 1.590 (1.594) | 2.136 (2.136) | 2.301 (2.296) | 2.657 (2.653) | 2.921 (2.917) | 3.160 (3.156) | 3.511 (3.501) | 3.619 (3.600) |

So the open tone's strongest overtones fall near a fifth (1.48), an octave (1.99), a major tenth (2.47) and an eleventh
(2.72). Over a C fundamental that is G, C, E and F, which matches a player's report of "C fundamental, G, E and F as the
dominant overtones" (congaplace thread; anecdotal). The air column raising the tone above the head's own (0,1) is the
reverse of the timpani's air loading on the (m,1) family (R92); it comes from the open tube's stiffness on the (0,1).

### 2.3 Contact patches: the hand on the head

A stroke is 1–2 **patches**. Each patch is one hand part, with a mass and a nonlinear flesh spring, landing on a
locally reacting head. It has up to 4 sample points on the head (a point, up to 3 explicit points, or a line of 4).

**Hand parts** (`PARTS`; the effective masses and stiffnesses are estimates, tuned against section 2.11):

| Part | m (kg) | k (N/m^α) | α | λ (s/m) | Patch radius b (m) | Rest R (N·s/m) | Cup | V(u) (m/s) | Used by |
|---|---|---|---|---|---|---|---|---|---|
| `palm` | **0.18** (v1 0.25) | **6.0e8** (v1 3e8) | 2.5 | 0.8 | 0.035 | 55 | 1.3 | 0.15 … 3.6, **capped at 1.3 × hand speed** | bass, edge palm |
| `heel` | 0.15 | 1.0e9 | 2.5 | 0.8 | 0.022 | 40 | 0.2 | 0.15 … 1.6, capped at 1.3 × hand speed | heel |
| `fingers` (flat, a line) | 0.030 | 1.0e9 | 2.5 | 1.0 | 0.012 | 18 | 1.2 | 0.15 … 4.5 | open, mute, the edge palm's fingers |
| `tips` (cupped) | **0.005** (v1 0.006) | **2.0e7** (v1 1e9) | **1.5** (v1 2.5) | 1.0 | 0.007 | 8 | 1.8 | 0.40 … 6.0 | slap |
| `base` (palm base / knuckles at the rim) | 0.10 | 1.0e9 | 2.5 | 0.8 | 0.020 | 30 | 0.4 | 0.20 … 3.0 | slap |
| `pads` (finger pads) | 0.030 | 5.0e8 | 2.5 | 0.8 | 0.012 | 14 | 0.3 | 0.15 … 1.6 | toe, touch, the bass's trailing fingers |

**Speed law.** `partV(part, u) = Vmin + (Vmax − Vmin) · u^1.1`, and for the palm and the heel at most
1.3 · handSpeed(u), where handSpeed(u) = 0.30 + (2.0 − 0.30) · u^(1/0.7) inverts the palm group's u law (3.4). Fingers
and fingertips whip faster than the tracked hand, so they keep their own range. The palm and the heel do not whip and
are tracked well. v1 drove the palm at 3.6 m/s for a 2 m/s hand, the critique's "+12 dB A tumba bass at u 1". Values
(`test-vectors.json` `laws.partV`, u 0 / .25 / .5 / .75 / 1): palm 0.15 / 0.695 / 1.211 / 1.855 / 2.6; tips 0.4 / 1.619 /
3.012 / 4.481 / 6.0.

**Flesh.** The force is a Hunt–Crossley spring: F = k d^α (1 + λ ḋ), with d the flesh compression and F ≥ 0. α is 2.5
for the soft parts: soft tissue is soft at first touch and stiff once compressed, so a harder stroke gives a shorter,
sharper pulse. **v2: the slap's fingertip pads are Hertz (α 1.5) and stiff:** the pads over the distal bone meet the hide
with a step-like force (F ≈ Zh·V within a fraction of a millisecond), whose difference dF is flat to 9 kHz. That onset,
not the pop, is now the crack. Computed as d^α = d^n √d with n = α − ½ (1, 2 or 3; `an` in the slot), no `pow`.

**Head under the patch** (`headReaction`, main or audio thread, double):
- Static point stiffness: Kh = 2πT / ln(1 + d_rim/b), with d_rim = max(¼b, a(1 − ρ)). The head is stiffer near the
  rim.
- Wave dashpot: Zh = 2πb √(Tσ) (drums.md 4.4).
- It stiffens under the hand: Kh (1 + (y/y2)²), with y2 = 0.03 a (4.4 mm on the conga), the drums engine's
  stiffening idea as a local spring.

**Per sample, while the patch is on** (`Drum.contact`, scalar, **double precision**). IEEE + − × ÷ √ are correctly
rounded in C# too, so the managed C# code is bit-identical here.
```
x' = x + v dt                                   (hand, predicted)
solve y' (4 fixed Newton steps from y' = y; g is monotone, so the root is unique):
   d = x' − y';  ḋ = v − (y' − y)/dt;  dⁿ⁻¹ = 1, d or d² (n = α − ½)
   F = k dⁿ⁻¹ d √d (1 + λ ḋ)  (0 if d ≤ 0 or F < 0)
   g(y') = Zh (y' − y)/dt + Kh y' (1 + (y'/y2)²) − F = 0
   g' = Zh/dt + Kh (1 + 3 (y'/y2)²) + α k dⁿ⁻¹ √d (1 + λ ḋ) + k dⁿ⁻¹ d √d λ/dt
F from the final y';  v' = v − F dt/m;  y ← y', x ← x'
dF[n] = f32(F − F_prev)                        (the modal drive, below)
first F > 0: tFirst; the air pop starts if cup > 0 (2.6)
F = 0 and v < 0 for FLIGHT (3 ms) → off; age > 40 ms → off
```
The head is **open loop**: the patch sees the local head (Kh, Zh), not the modal state. A hand is soft and heavy, so the
modes' feedback on the force is small, and two hands' patches stay independent: no 2×2 solve, as the kit needed
(drums.md 4.4). An explicit Euler step for y diverged for fingertips: 730 N against about 50 N.

**Per-mode input gain of a patch** (at the event, from tables): gi_k = ⟨φ_k⟩_pts · A_k(part) / (M_k ω_k), where:
- ⟨φ_k⟩_pts is the mode shape averaged over the patch's points. The shape is the 65-point radial table, linearly
  interpolated, times cos/sin(mθ) (from the Chebyshev recurrence per point, `angles`: two trig calls per point, not per
  mode); ρ ≥ 1 counts as 0 for head modes.
- **v2:** A_k(part) = 3 (sin x − x cos x)/x³, x = κ_k b, the mode averaged under a **Hertz-like (elliptical) contact
  pressure** p ∝ √(1 − r²/b²), as under soft tissue (its Hankel transform; series 1 − x²/10 + x⁴/280 below x = 0.05). Its
  first zero is at 4.49 (the uniform disc's 2J1(x)/x of v1: 3.83) and its side lobes are lower. Precomputed as
  `avgTab[part][k]` on the main thread. A big soft palm still filters the high modes out, which is why a bass never clicks.
- **v2: a patch at the bearing edge.** A measured point with 1 − b/(2a) < ρ < 1 + b/(2a) is moved to ρ = 1 − b/(2a)
  (about the centroid of the patch's on-head part) *before* the per-stroke variation is added. v1 gave a palm centred on
  the rim no head drive at all (−34 dB, a dead spot).
- Shell modes take rimShare(ρ). Cavity modes take `cup`.

Results (`verify.json` `contact`, conga; ρ .15 palm/heel, .75 fingers, .94 base, .5 tips/pads):

| Part | u .2 | u .5 | u .8 | u 1 |
|---|---|---|---|---|
| palm: contact ms / F max N | 10.3 / 40 | 8.8 / 89 | 7.6 / 168 | 6.6 / 241 |
| fingers (open) | 4.4 / 23 | 3.1 / 66 | 2.6 / 121 | 2.5 / 164 |
| tips (slap) | 1.25 / 14 | 1.21 / 32 | 1.17 / 52 | 1.13 / 67 |
| heel | 10.3 / 22 | 8.8 / 50 | 8.0 / 85 | 7.5 / 113 |
| base | 5.8 / 50 | 4.5 / 136 | 3.1 / 244 | 2.8 / 325 |
| pads | 6.6 / 7 | 5.4 / 18 | 4.8 / 30 | 4.6 / 38 |

Hand contacts are longer than sticks (3–6 ms, W06) and shorten as the stroke gets harder, as stick contacts do (W06).
The slap's fingertips are the shortest: their contact is now set by the head's impedance (m/Zh ≈ 0.9 ms), so it hardly
changes with speed. No published hand-on-conga contact times were found (UNVERIFIED; on-device or recorded data would
settle it).

### 2.4 The modal kernel and why it is DC-free

The canonical MB4 recurrence (`burst.md` 5.1), with up to 4 inputs:
```
u   = gi_0 dF_0[n]; u = mad(gi_1, dF_1[n], u); …          (patches in ascending slot order; absent ones skipped)
zr' = mad(pr, zr, mad(−pi, zi, u));   zi' = mad(pr, zi, pi·zr);   acc[n] = mad(go, zi', acc[n])
free (no patch on): the same with u = 0 (DspKernels.ModalFreeList)
```
The modes are driven by the **force difference** dF, not by the force. Each mode's output is then its velocity (Im z
= q̇ with gi = φA/(Mω)), and the whole chain has a zero at DC. No one-sided low-frequency push can exist under any
stroke: this was the kit's critic H1 (drums.md 0.1). The palm's 7–10 ms push, the longest of any instrument here,
produces no "woof".

Measured:
- energy below 30 Hz ≤ −54.1 dB re the stroke (`Q2`); below 60 Hz −40.5 … −78.6 dB (`parity.json`; platform limit −20);
- |mean|/rms 0–0.0011 over the whole hit (platform limit 0.01).

### 2.5 Radiation and output gain

go_k = rad_k · w_k · level · 10^(OUTPUT_TRIM_DB/20), normalised so the tone mode has rad 1:

| Kind | rad | w_k |
|---|---|---|
| T | 1.0 | ω_k/ω_T (a monopole: pressure ∝ ω × volume velocity) |
| B | **1.2 conga / 1.3 tumba** (v1 1.55 / 2.0; critique M1) (the air column radiates from the top and the bottom opening, in phase) | ω/ω_T |
| A2 | 0.9 | ω/ω_T |
| MEM, m = 0 | net volume (J1(j)/j) / (J1(2.405)/2.405), signed | ω/ω_T |
| MEM, m ≥ 1 | 0.40 · (ka/2)^m / √(1 + (ka/2)^{2m}) | **1** |
| DENSE | **v2: 0.5 · min(1, ka/3) · √ΔN** (ΔN the slot's real-mode count, 2.2; v1 1.3 · min(1, ka/3)) | **1** |
| SHELL | 0.06 · g (1, 0.8, 0.6, 0.45) | ω/ω_T |
| CAV | 1.0 · g (1, 0.6) | ω/ω_T |

**Why there is no ω on the multipoles and the dense field.** Every head mode is *subsonic*: the membrane wave speed,
74–93 m/s, is well below 343 m/s. A mode with nodal lines therefore radiates only from its uncancelled edge strip,
about 1/k_mode wide, and its far-field pressure per unit velocity does not grow with ω (Cremer–Heckl, below
coincidence). With ω on every mode, the high modes added in phase at the onset, and an open tone peaked 10 dB over
its body.

**Levels.** `level` (conga 0.12766, tumba 0.12443; `calibrate.mjs`) puts an open tone at u .8 at −26 dB K-weighted over a
0.4 s window (dry, re full scale). `OUTPUT_TRIM_DB` = **+7.86 dB** (v1 +3.97) then sets the tone phrase on the piano's
loudness (2.11, P). The 3.9 dB more trim is what moving parity from the bass to the tone strokes costs (critique M1: "about
+3 dB"). In the float graph the hardest strokes peak at +6.8 dBFS (conga slap, u .95 ladder) and +4.4 dBFS (tumba bass,
u 1; v1 +8.3); the platform's ff rule (true peak through MasterBusV2 ≤ −1 dBTP) holds, because MasterBusV2's limiter does it
(section 8: −2.6 dB at most, on the conga line's accents). Everything after the unit is float. The source table's send
and gain are the platform's (`HandDrumLo/Hi`: −3 dB VR send, reach 0.7, ER +3); see risk 11.

### 2.6 The air pop (the slap's "pop", the skin smack of hard strokes)

When a patch first touches the head, the air between the hand and the skin is pushed out. A hand clap is the same
thing: a short pulse through the resonance of the cavity between the hands (Peltola et al. 2007). Here:
- a raised-cosine pulse of length L = 18…28 samples (per stroke, from the RNG), of area POP_K · cup · V², with
  **POP_K = 52** (v1 110);
- it drives the two `CAV` modes, which have their own group, **Q ≈ 2** (v1 ≈ 4);
- that group's input is the patches' pop streams instead of dF, with gi = cup/(M ω);
- the cavity frequency is × (1 ± 8 %) per stroke, because no two hand shapes are the same.

v2: the pop is 7–12 dB under the slap's peak (v1: −1.4 to −5.9 dB, the peak itself) and 17–19 dB under the slap's
energy above 2 kHz. The crack is the head's own high-frequency field now (2.2, 2.3); the pop is a small smack at the
start. It still grows as V² against the membrane's ≈ V^1.2, so the hardest slaps pop most (u 1: −10.4 / −6.2 dB).

Two earlier versions were rejected in v1:
- a white-noise burst varied ±5 dB in energy from stroke to stroke, because a short noise burst's spectrum at 1.7 kHz
  is random;
- a 0.33 ms pulse into Q ≈ 9 cavities made the slap's peak-to-loudness ratio 31 dB, a click.

The air-pocket explanation of the slap's smack is a hypothesis, and so is its level (section 10).

### 2.7 Rests (muted tones, closed slaps, heel-toe, a hand laid on a ringing drum)

A **rest** event lists up to 3 points (part, ρ, θ) of one hand that stay on one head. Per mode:
```
Δα_k (1/s) = Σ_points R_part · (φ_k(ρ, θ) · A_k(part))² / (2 M_k)        (shell and cavity modes: 0)
dMul_hand[k] = exp(−Δα_k / sr)                                             (v2: once per rest event, double)
```
Each hand keeps its own `dMul` array (1 = no damping). **Lift** sets that hand's array to 1. The poles are refreshed on
the next 32-sample control step: |p| = r0 · dMul_L · dMul_R. v1 took the exponential per mode at every pole refresh. The
state is continuous, so damping in or out never clicks (`Q4`, a lift and re-rest while ringing: click score 2.1; white
noise scores about 8).

Measured:
- fingers left on the head (a closed tone) take the tone down 65.6 dB by 150–180 ms, against 9.6 dB when open (`T1`);
- a palm laid on a ringing conga takes 79.7 dB off in 100 ms (`T3`).

### 2.8 Tension glide

At every 32-sample control step:
- E = Σ eW_k (zr² + zi²) over the B, T and A2 modes and the head modes below 800 Hz, with eW_k = (j_k/a)² (M_k/σ) / ω_k²
  (strain energy per squared modal velocity);
- s = min(1 + glideMax, √(1 + κE)); a change of less than **GLIDE_EPS = 2e-6** (0.0035 cents) is ignored;
- **v2: a glide change refreshes only `glideList`** (the gliding modes and the two cavity modes: 16 / 27 of 170 / 194);
  a rest, a lift or a budget fade refreshes every pole. The rotation by δ = ω0 (s − 1) uses cos ≈ 1 − δ²/2 + δ⁴/24 and
  sin ≈ δ − δ³/6 + δ⁵/120: no transcendental at control rate, and still exact for |δ| < 0.03.

κ (conga 19.413, tumba 25.824; v1 5.467 / 7.089) makes the hardest open or bass stroke (u 1) reach 0.9 × glideMax (3.0 % /
3.5 %) right after its contact (`calibrate.mjs`). κ rose because the capped palm stretches the head less, and the rule
keeps the hardest real stroke's glide. ΔT ∝ amplitude² and f ∝ √T (R92); a bass drum's full blow is about 10 % sharp.

The palm bass stretches the head most, so it glides most: +10.7 cents at 15–45 ms after a u 1 bass, settling within
about 100 ms. An open tone barely glides (0.0 cents; `G1`, `G2`). A rest's pressure could raise the tension too (the
conguero's pitch bend, deslizado). The engine has room for it (`press` on rest points), but it is **off in v1 and v2**.

### 2.9 Per-stroke variation, RNG order (the C# implementation must match it for the vectors)

At each patch event, from that drum's `Rng`, in this order:
1. V × (1 + 0.05 bip);
2. ρ += 0.04 bip (all points);
3. θ += **0.25** bip (all points; v1 0.2);
4. k × (1 + 0.2 bip);
5. pop length = 18 + ⌊11 R⌋;
6. cavity × (1 + 0.08 bip);
7. one more ρ += 0.02 bip **per point**, in point order.

bip = 2R − 1. Rests and lifts draw nothing. Before steps 2 and 7, a measured point at the bearing edge is moved to
1 − b/(2a) (2.3), so strokes at the rim still differ. Live strokes vary on their own (measured geometry and speed); the
draws matter for playback and lessons. Measured: 8 identical open tones spread 1.99 dB in loudness, and the consecutive
attacks (> 600 Hz, first 20 ms) correlate 0.47–0.985 (`V1`). **This statistic is noisy:** across five engine seeds the
highest consecutive correlation is 0.985–0.994 for any variation tried (θ ±0.2…0.3, k ±0.2…0.5), because the attack is
dominated by head modes that a ±6 mm landing barely changes. V1 passes on the reference seed; it is reported, not tuned
further.

### 2.10 The strokes as patches (canonical geometry: playback, lessons, demos)

θ = 0 is the side of the head nearest the player. The live path sends measured geometry instead (section 3).

| Stroke | Patches (part, u scale, delay, points (ρ, θ)) | Rest after 12 ms (for 0.25 s) |
|---|---|---|
| open | fingers, u, 0: a line of 4 points from ρ .97 to .50 at θ | – |
| mute (closed tone) | as open | fingers at (.75, 0), (.60, 0) |
| slap | base, .8u, 0: (.94, 0); tips, u, 0.8 ms: (.58, −.26), (.55, 0), (.58, +.26) | – |
| slapClosed | as slap | tips (.56, 0), base (.94, 0) |
| bass | palm, u, 0: (.10, 0); pads, `BASS_PADS_U` (0.6) u, 4 ms: (.62, π) | – (palm (.15, 0) if muted) |
| heel | heel, u: (.30, 0) | heel (.30, 0) while the hand stays (manoteo) |
| toe | pads, u: a line from ρ .62 to .40 at θ + π | pads (.50, π) while the hand stays |
| touch | pads, u: (.75, 0) | – |

(The engine moves the open line's .97 and the slap base's .94 to 1 − b/(2a) = .959 / .932 on the conga, 2.3.)

**Stroke balance** (`balance.mjs`, u .8, A-weighted 0.4 s rectangular window re the open tone of the same drum, K in
brackets; c50 = spectral centroid of the first 50 ms; PLR = peak re K-weighted loudness):

| | open | slap | bass | mute | closed slap | heel | toe | touch |
|---|---|---|---|---|---|---|---|---|
| conga dB A (K) | 0 (0) | **+2.5** (−0.2) | **−4.8** (−1.2) | −5.6 (−5.7) | −0.1 (−3.6) | −11.3 (−7.0) | −10.2 (−9.3) | −13.3 (−12.8) |
| conga c50 Hz | 299 | **597** | 130 | 297 | 711 | 121 | 264 | 274 |
| conga PLR dB | 15.6 | 22.5 | 19.7 | 21.2 | 26.0 | 20.0 | 14.9 | 16.3 |
| tumba dB A (K) | 0 (0) | **+2.2** (−0.6) | **−4.0** (−0.8) | −5.8 (−6.4) | 0 (−4.4) | −12.5 (−7.7) | −9.2 (−8.1) | −12.0 (−11.4) |
| tumba c50 Hz | 229 | 484 | 103 | 229 | 600 | 91 | 196 | 206 |

v1 → v2: the slap went from +1.7 / +2.3 dB A (but a click) to +2.5 / +2.2 dB A with a real crack; its centroid from
471 / 352 to 597 / 484 Hz. The bass went from −1.8 / +2.9 dB A (+5.2 / +8 K) to −4.8 / −4.0 dB A (−1.2 / −0.8 K): a low,
muffled stroke as Thomann and Wikipedia describe it, a little under the open tone by K-weighting and more by A-weighting,
with its tone partial 1 dB under its fundamental (S2; R00: "almost as loud"). Both weightings are reported, because
K-weighting is nearly flat down to 78 Hz while the ear is not (ISO 226), so A-weighting is the closer guide to perceived
balance. In the critic's own measure (c3, a Blackman-Harris window over 0.4 s from the onset, which weights the tail,
not the attack) the slap reads −1.8 / −0.5 dB A: the slap is louder in its first 50 ms, the open tone in its ring.

**How the bass was brought down (critique M1).** Three changes, all as the critique asked or measured to its goal:
- the palm's (and heel's) speed cap at 1.3 × the hand speed (2.3): −4 to −5 dB A at u .8, −8 dB A at u 1;
- the critic's radiation cut, rad.B 1.55 → 1.2 (conga) and 2.0 → 1.3 (tumba): K-weighted −1.4 / −2.4 dB;
- a lighter, firmer palm (0.25 → 0.18 kg, k 3e8 → 6e8). The 0.25 kg palm's 8.5 ms contact put a spectral null right on
  the tumba's 196 Hz tone (its tone partial was 14 dB under the bass, far from R00's "almost as loud"). At 0.18 kg the
  contact is 7.6 ms and the tone partial sits 1–3 dB under the bass on both drums.
Result: bass ×4 phrases +0.15 / +0.11 LU re the piano (v1 +2.1 / +4.1), B1 on both drums, the u 1 tumba bass +3.5 dB A
re the u .8 open tone (v1 +12). If the bass booms or sounds too thin in the headset, see risk 4.

### 2.11 Verification against published measurements (`verify.json`: 50 / 50 pass)

Sources in section 9. "First set" means a threshold changed once during tuning; the reason is given.

| Id | Check | Measured | Pass rule | Source |
|---|---|---|---|---|
| M1 | Head modes re the head's own (0,1) vs the ideal membrane, worst error | 0.5 % | < 2 % | R92 |
| M2 | (1,1)/tone, conga / tumba (the air-coupled tone) | 1.479 / 1.478 | 1.40–1.594 | R92 trend |
| S1 | Open tone: the tone partial over the next strongest (0–400 ms), conga / tumba | 11.4 / 12.2 dB | ≥ 10 | R00: one pitch "many dB louder" |
| S2 | Bass: strongest partial; the strongest 180–360 Hz partial re it | 78 Hz, −0.9 dB (262 Hz) / 69.9 Hz, −1.2 dB (196 Hz) | < 100 Hz; 0…−8 dB | R00: 70 Hz bass, its 280 Hz "almost as loud" |
| S3 | Slap: the fundamental re the open tone's | −2.6 / −1.9 dB | ±3 dB | R00: the slap shares the tone's peak |
| S4 | Slap: 1–8 kHz energy re open (first 50 ms); c50 ratio | +8.5 dB, 2.00× / +9.6 dB, 2.11× | **≥ 8 dB, ≥ 1.6×** (v1 relaxed to 6 / 1.5; v2 restores the first set) | DJW: slap energy in the higher partials; UBC djembe: slap centroid ≈ 2.4× the tone |
| H1 | **v2** Slap: energy above 4 kHz re all, first 50 ms (rectangular) | −18.6 / −18.3 dB | ≥ −22 (v1 −34.8 / −37) | critique H4; Thomann: bright, cutting |
| H2 | **v2** Slap crack: 2–7 kHz peak-to-median per 1/3 octave, 5–60 ms (Blackman-Harris): band mean, max | 9.6, 12.0 / 9.2, 12.9 dB | mean ≤ 10.5, max ≤ 14 | critique H4 (v1 17–48; the critique asked ≤ 12 dB: met on 11 of 12 bands, the tumba's 6.3 kHz band reads 12.9). Decaying noise in this measure: mean 7.5, max 9.6 median / 12.6 p99, so a single band at 12–13 dB is within what noise does |
| H3 | **v2** Slap: the air pop's share of the peak, u .8 and u 1 | −11.6, −10.4 / −7.0, −6.2 dB | ≤ −6 | critique H4 (v1 −1.4 to −5.9) |
| S5 | Slap: (1,1) and (2,1) re the fundamental, slap minus open | +2.5 / +8.9 dB; +0.3 / +7.7 dB | both > 0 | DJW: M3/M4 dominate the slap |
| S6 | A fingertip tap at the rim vs the centre, 0–200 ms centroid | 279 vs 249 Hz | ≥ 1.1× (first set 1.2×; 10 % is above the centroid JND) | HW14: higher pitch at the rim |
| B1 | Balance re open, A-weighted, u .8, **both drums**: slap +1…+3, bass −6…+2, heel < −4, toe/touch < −6 | table 2.10 | as stated (v1: conga only, slap +1…+6) | design; critique M1, H4 |
| B2 | **v2** Bass at u 1 re the open tone at u .8, A-weighted, conga / tumba | +0.1 / +3.5 dB | ≤ +6 (v1 tumba +12) | critique M1 |
| D | Ladders u .15 → .95: A-weighted range, monotonic; open: attack 1–8 kHz share gain; bass: c50 ratio; slap: share change | open 22.1 dB, +10.5 dB; bass 20.0 dB, c50 100 → 144 Hz; slap 16.6 dB, −1.2 dB | 16–30 dB, monotonic; open ≥ +3 dB; bass c50 ≥ 1.1×; slap ≥ −2 dB (see below) | W06 trend: louder = brighter |
| C1 | Palm contact u .2 / u 1 | 10.3 / 6.6 ms | longer than a stick, shorter when harder | W06 |
| C2 | Slap fingertips at u .8 vs flat fingers | 1.17 vs 2.63 ms | < 2 ms and shorter | design |
| G1, G2 | Glide after a u 1 bass (15–45 ms re 0.5–0.8 s, the T mode alone, heterodyne); grows with force; the open tone glides less | +10.7 c; +0.7 (u .5) < 10.7; open 0.0 | 10–60 c; ordered | R92 |
| T1 | Closed tone vs open: the tone's drop 10–40 ms → 150–180 ms | 65.6 vs 9.6 dB | ≥ open + 15 | technique |
| T2 | Open tone T60 of the tone partial | 0.85 s | design 0.85 ± 20 % | design (no published conga T60 found) |
| T3 | A palm laid on a ringing drum: drop after 100 ms | 79.7 dB | > 30 | technique |
| T4 | No metallic tail: after 0.5 s the tone over the next partial; longest head-partial T60 re the tone's | 25.5 / 39.2 dB; 0.75 / 0.75 | ≥ 12 dB; ≤ 0.8 | platform 10.3, adapted (below) |
| Q1, Q2 | DC (\|mean\|/rms, first 60 ms, worst at u 1); energy below 30 Hz re all (0.5 s) | 0.01; −54.1 dB | < 0.05; < −20 | drums H1 |
| Q3–Q5 | 7 s two-hand passage with rests and lifts: non-finite samples; click score outside onsets; energy above 20 kHz | 0; 2.1; 4.4e-7 | 0; < 6; < 1e-4 | drums 10.4 |
| Q6 | 12 s after a stroke: subnormal states, active groups | 0, 0 | 0, 0 | burst.md 6 |
| Q7 | Two renders bit-identical | yes | yes | burst.md 7 |
| V1 | 8 identical open tones: loudness spread; highest consecutive attack correlation | 1.99 dB; 0.985 (just under) | 0.1–2 dB; < 0.985 (first set 0.97) | drums 10.5; noisy statistic, 2.9 |
| F1 | Two hands, open tones 0 / 1 / 3 / 10 / 25 ms apart, re one | +5.6 / +1.2 / +3.7 / +1.4 / −0.7 dB | −6…+6.5 | drums A2 |
| P | Loudness parity: **tone phrase open-open-slap-open** (conga, u .55, 108 bpm) vs the piano's Ode bar 1 at vel 0.55, dry | −24.15 vs −24.15 LUFS (0.00 LU) | **±1 LU** (v1 ±2, bass-open phrase) | platform 10.3; critique M1 |
| P2 | **v2** Open-only phrases re the piano mf, conga / tumba | +0.33 / +0.66 LU | ±2 LU | critique M1 (v1 −3.5 / −3.2) |
| SW | **v2** Flat hand at 1.5 m/s, palm ρ 0–1.35, curls and pitches, through the live logic and the engine: strokes at u ≥ .5 more than 6 dB A under the open tone | 0 of 336 (worst −5.3 dB, a bass) | 0 | critique H3 |
| BS | **v2** Budget: two-hand slap roll on both drums, Share 0.4 vs 1: Work ratio; active groups ≤ cap; clicks outside onsets | 0.436; 36 ≤ 37; 0.02 | ≤ 0.5; yes; < 6 | platform 10.3 `UnitShareTests` |

**The D rules, changed in v2, and why.** v1 asked every ladder for "louder = brighter" as a ≥ 3 dB (bass ≥ 2 dB) rise of
the attack's 1–8 kHz share. In v1 the slap met it through the pop, which the critique cut (≤ −6 dB of the peak).
- **Open tone:** still ≥ +3 dB, met at +10.5.
- **Bass:** a 7 cm palm passes nothing above about 1 kHz, so a harder bass is "brighter" through more of its tone, not
  through 1–8 kHz (its share moves −26.7 → −28.2 dB while the tone rises). The rule is now its centroid: 100 → 144 Hz.
- **Slap:** the Hertz fingertip makes even a soft slap crisp, so its 1–8 kHz share is −7.6 dB at u .15 and −8.8 at u .95,
  and its c50 691 → 594 Hz (the tone grows faster than the crack). The rule is now "not duller by more than 2 dB".
- **Tried and rejected:** a stiffer-flesh law (exponent 3.5) gave +2 to +5 dB of brightness gain, but its contact
  chatter at about 2.3 kHz rang one dense mode 22 dB above the crack's median: the "ringing line" the critique objected to.
- Whether a soft slap should be duller is a listening question (README, "not sure" list).

**The 1.6 kHz band in the critic's wider crack measure (c3, 1.25–7 kHz).** It reads 19.8 dB on the conga slap (v1 14.8).
The peak is the band's first bin, the leakage of the listed Bessel mode at 1397 Hz (a real, discrete membrane mode,
T60 0.19 s) just below the band edge; the dense field alone reads 6 dB peak-to-median in that band.

**Platform conformance (`platform.md` 10.3).**
- Met: CPU (2.13), budget scaling (`BS`), no low-frequency push, clicks, denormals, LateDrop on onsets only, allocation,
  determinism, memory, loudness parity (`P`), not over-wet (below).
- **Not over-wet.** DRR at the seat, impulse through the direct path and the room unit ('natural', the spec's ErOrigin),
  energy over both ears: **conga 5.9 dB, tumba 4.5 dB**, inside the piano's 6.1 ± 2 dB. Per ear 7.6 / 3.0 dB (conga,
  L / R) and 2.0 / 6.0 dB (tumba): each drum's far ear is low because its direct sound is head-shadowed at high
  frequencies, which the centred piano never is. A programme measure (the tumbao) reads 2.4 / 2.6 dB, because the drums'
  energy sits at 70–260 Hz, where the room's DRR is lowest even for the piano (3.5 dB at 125–250 Hz, room.md).
- **Not met as written: "for wood and skin, the top-20 partials hold ≤ 35 % of the 0.3–9 kHz energy after 0.5 s".**
  The drums measure 90–97 %, because after 0.5 s a pitched skin *is* its tone plus a few partials (R00). The rule
  comes from the crash cymbal's wash (drums H2), where a few partials sticking out means a gong hum. For a pitched skin
  the metallic risk is inharmonic partials outliving the tone, and T4 checks exactly that. **Contract request:**
  T4 replaces the top-20 rule for `Pitched` skins.

**Lessons from the earlier critiques, applied:**
- low-frequency push (2.4);
- metallic tails (T4, the T60 law) and sparse modes (H2: the dense field is now as noise-like as decaying noise, within
  about 2 dB in the critic's measure);
- over-wet room: the room's 'natural' preset only, DRR reported; the `-room` renders keep the dry versions beside them;
- loudness fairness: parity on the tone strokes; every render through the app master at the app's own gain and then one
  plain gain.

### 2.12 The unit on the Burst graph (ENGINE-API)

**Files (`Audio/Sound/HandDrums/`):**
- `HandDrumsUnit.cs` (managed: `SoundUnit`, `IHandDrumSound`);
- `HandDrumsDsp.cs` (Burst: `Render`, budget, contact, kernels, control);
- `HandDrumsState.cs` (struct layouts);
- `HandDrumsTables.cs` (loads the JSON or rebuilds it);
- `README.md`.

Hooks (pre-declared by phase 0):
```csharp
public static unsafe partial class UnitHooks { static partial void RenderHandDrums(UnitSlot* u) => HandDrumsDsp.Render(u); }
public static partial class SoundUnits { static partial void CreateHandDrums(SoundContext ctx, ref SoundUnit unit) => unit = new HandDrumsUnit(ctx); }
```

**Register and the channel map (v2, critique M5).** The unit registers its outputs in the platform's source order:
```csharp
Register(ctx, UnitId.HandDrums, new[] { SourceId.HandDrumLo, SourceId.HandDrumHi }, _s, ringCapacity: 256, pendingCapacity: 256, idleSkip: true);
static readonly int[] ChannelOfDrum = { 1, 0 };   // drum 0 = conga -> channel 1 (HandDrumHi); drum 1 = tumba -> channel 0 (HandDrumLo)
```
Drum indices stay 0 = conga, 1 = tumba everywhere (state, commands, vectors, the module). Only `Render` maps them:
`u->Channel(ChannelOfDrum[d])`. The reference's `process(outs)` writes `outs[d]` by drum index; the exported constants
`SOURCES` and `CHANNEL_OF_DRUM` pin the map (`test-vectors.json` `laws`).

**State** (`Alloc<HandDrumsState>`, zeroed, 64-byte aligned; per drum d = 0 conga, 1 tumba):

| Field | Type, size | Notes |
|---|---|---|
| `Bank[d]` | ModalGroup[G] (43 / 49), 96 B each | zr, zi, pr, pi, go, gi (gi unused: per-patch gains below) |
| `Cr0, Ci0, R0, W0, Mk, Om, Kap, EW, CPh, SPh` | float[nPad] each (doubles in the reference: store as float, compute poles in double at control time) | `Cr0 = cos w0`, `CPh/SPh = cos/sin` of the dense modes' angular phase |
| `Kind, M, Orient, GlideOn` | byte[nPad] | |
| `GlideList` | ushort[16 / 27] | the modes a glide-only refresh touches |
| `GClass` | byte[G]; `ShedOrder` ushort[38 / 41] | budget (2.13) |
| `Shape` | float[nPad × 65] | radial tables |
| `AvgTab` | float[6 × nPad] | per part (Hertz average) |
| `DMul[2]` | float[nPad] × 2 hands | **v2:** per-sample radius factor exp(−Δα/sr) of each hand's rest (1 = none) |
| `Act, Allowed, Fading` | byte[G] each | group activity; budget mask; fading this block |
| `Energy, FadeMul, Cap, Louder, Work` | float, float, int, byte, int | budget state; `Work` = group-subblocks this block (→ `UnitSlot.Work`) |
| `Patch[4]` | `{ on, part, hand, m, k, an (α − ½), alpha, lam, Kh, Zh, iy2 (double), x, v, y, F, Fp (double), flight, age, started, tFirst, tLast, Fmax, cup, V0, popLen, popLeft, popA }` + `Gi[nPad]` float + `DF[256]`, `Pop[256]` float | slots |
| `Scratch` | `Pr, Pt` double[4]; `Cm, Sm` double[4 × 25] | one event's points and their cos/sin(mθ) (no allocation per event) |
| `S, CavMul, Idle, PolesDirty, GlideDirty` | | |
| `Rng[d]` | uint (mulberry32 state) | from `ctx.Seed` (`SeedAll`); tests use the exported seeds |
| `Acc` | float4[256] | lane accumulator |

The total is 45 KiB of state plus 119 KiB of tables (`bench.json` memory: 164 KiB), against the platform's 2 MiB cap.

**Commands** (`GraphCommand`, 64 B, on the unit's own ring, capacity 256, pending 256; `Op` is the unit's sub-op). v2:
the payload carries the stroke value **u** and the points exactly; nothing is left for the unit to guess.

| Op | Fields | From |
|---|---|---|
| 1 `Patch` | `Target` drum; `N` = hand \| part << 4 \| shape << 8 \| count << 12 (shape 0 `Points`, 1 `Line`); `A` **u, 0…1** (the patch's own stroke value, already × its u scale: the unit computes V = `PartV(part, u)`, 2.3); `Points`: `B..G` = ρ0 θ0 ρ1 θ1 ρ2 θ2, count 1–3; `Line`: `B` ρ0, `C` θ0, `D` ρ1, `E` θ1 → 4 points at k/3, k = 0…3; `When` the stroke's time + the patch's delay | `IHandDrumSound.Patch`, `Stroke` (expands `STROKES`); **`CommandFlags.LateDrop`** |
| 2 `Rest` | `Target` drum; `N` = hand \| npts << 4 \| part0 << 8 \| part1 << 12 \| part2 << 16; `A..F` ρ0 θ0 ρ1 θ1 ρ2 θ2 | `Rest` |
| 3 `Lift` | `Target` drum; `N` hand | `Lift` (never LateDrop) |
| 4 `Reset` | | `Seed`, `OnPathChanged` resume (`UnitFlags.Reset`/`Resumed`) |

The canonical slap's tips fan (3 points) is sent as `Points`; the open tone's and the toe's lines as `Line`. The cup is
the part's (the reference's `extra.cup` / `extra.V` overrides are for tests only and have no command field).

**The render** (`HandDrumsDsp.Render(UnitSlot* u)`):
1. **Budget** (2.13): compute `Cap` from `u->Graph->Budget / 1200f × u->Share`, update `Louder`, rebuild the masks if
   either changed; `FadeMul = exp(−6.9078 / Frames)`; in each drum, mark the active groups that are not allowed as fading
   and refresh every pole.
2. For each drum, in drum order: for each due event of that drum in (At, Seq) order, render the segment up to `Offset`,
   then apply the event; render the rest of the block.
3. End of block: zero and retire the fading groups, refresh every pole; write `Work`.

A segment is split at the absolute 32-sample grid. For each piece:
- run `contact()` for every patch that is on (scalar, double) to fill DF and Pop;
- for each active group: the driven-P kernel (P = number of patches on: variants 1, 2 and 4, with the absent gains 0;
  padding with zero gain and zero input is bit-exact, since mad(0, 0, u) = u), using **Pop instead of DF for the
  cavity group**, or `ModalFree` when no patch is on;
- `AccResolve` into `u->Channel(ChannelOfDrum[d])`.

At each grid point, `control()`:
- flush |z| < 1e-20;
- per group energy (also summed into `Energy`); cull below `CULL2` = 1e-11 (−110 dBFS) when no patch is on;
- glide s from E;
- age the free slots;
- refresh every pole if a rest, lift or fade changed them, else only `GlideList` if the glide or the cavity changed;
- idle when nothing is active.

Slot choice: the first free slot, else steal the oldest (`stats.patchesStolen`; 0 in every bench schedule, including
the inhuman one). A new patch activates only the allowed groups. **Idle skip:** registered with `idleSkip: true`. A drum
with nothing active costs nothing, and a fully idle unit is skipped by the graph after 8 blocks (platform 10.2).

**Unit API** (`Audio/Sound/HandDrums/IHandDrumSound.cs`; the module reaches it with
`audio.ActiveUnit(UnitId.HandDrums) as IHandDrumSound`, which is never null for this unit-only role):
```csharp
public enum HandPart : byte { Palm, Heel, Fingers, Tips, Base, Pads }
public enum PatchShape : byte { Points, Line }
public enum HandStroke : byte { Open, Mute, Slap, SlapClosed, Bass, Heel, Toe, Touch }
public struct RestPoint { public HandPart Part; public float Rho, Theta; }
public struct PatchPoint { public float Rho, Theta; }
public interface IHandDrumSound
{
    // u: this patch's stroke value 0..1 (the stroke's u x the patch's u scale); the unit turns it into the part's speed.
    // Points: 1-3 points p0..p2; Line: 4 points from p0 to p1. when = the stroke's time; delay = this patch's delay (s).
    void Patch(int drum, int hand, HandPart part, float u, PatchShape shape, PatchPoint p0, PatchPoint p1, PatchPoint p2, int count, double when, float delay = 0f);
    void Stroke(int drum, int hand, HandStroke kind, float u, float thetaOffset, double when);   // canonical geometry (2.10)
    void Rest(int drum, int hand, RestPoint p0, RestPoint p1, RestPoint p2, int count, double when);
    void Lift(int drum, int hand, double when);
    float Level(int drum);      // audio -> main, Volatile: the drum's output rms of the last block (for the head-motion visual's ringing, optional)
}
```
Every public method calls `TestTap?.RecordUnit(Id, op, drum, vel, when)` (platform 10.1) and allocates nothing.

### 2.13 CPU and budget scaling (`bench.json`; Node 26, Apple M5 Pro, shared with other agents)

**The estimate (v2, the critic's c7 split).** Per block, the timed scalar parts (event application, control steps,
contact solves) × 1.5 (drums.md 9: scalar parts 1.5–2, planned high) plus the rest of the block (the MB4 loops and
overhead) × 0.10 (burst.md 10.2). Node's worst block on a shared machine is mostly interference (GC, JIT, other
agents), so the worst block is also estimated from the block's **operation counts** (mode-samples × 5.2 ns × 0.10 +
patch-samples × 40 ns × 1.5 + control steps × 0.58 µs × 1.5), which is deterministic.

| Schedule | Node avg / p99 µs per 256 frames | Quest, timed: avg / p99 / worst | Quest, op count: avg / worst |
|---|---|---|---|
| ringing (both drums ringing, no hands) | 64 / 208 | 10 / 39 / 52–298 | 22 / 55 |
| tumbao (the lesson pattern) | 38 / 198 | **7** / 44 / 71–242 | 18 / 65 |
| roll (both hands, both drums, 16 slaps/s per drum, 2 patches each) | 254 / 394 | **42** / 84 / 104–278 | 53 / **88** |
| adversarial (both hands on both drums every 12 ms, slaps; beyond human) | 471 / 646 | **123** / 210 / 249–256 | 108 / **177** |
| adversarial at Share 0.4 | 229 / 355 | 92 / 175 / 204–223 | 80 / 149 |

(The timed worst is given for the best and the median of 5 runs: it swings with the machine's load; v2's first bench run
measured 251–421 µs for the adversarial case. The op-count columns do not move.) The critic's own `c7-bench.mjs` on v2
(`critic/out-v2/c7-bench.txt`): roll 44.2 / 89.2 / 95.1 µs (avg / p99 / worst; median-run worst 263), adversarial
128.5 / 219.4 / 241.1 µs (median-run worst 241).

**The platform cap is ≤ 0.15 ms average and ≤ 0.25 ms worst block.** Every human schedule meets it with ≥ 2.8× margin on
the worst block (op count). The inhuman case meets the average (108–129 µs) and the op-count worst (177 µs); its timed
worst block crosses 250 µs in some runs on this shared machine. v2 costs more than v1 (92 groups, not 46) because the dense
field is what makes the slap crack. T-HD-CPU (6.3) replaces all of these estimates. Micro-benchmarks (`bench.json` `micro`): free MB4
about 4.2 ns per mode-sample in Node; 4-input driven about 5.2 ns; contact solve about 40 ns per patch-sample; one
control step about 0.58 µs.

**Budget scaling (implemented in the reference; critique M5).** At block start:
```
cap = clamp(round(Gtot × Budget/1200 × Share), CAP_MIN = 12, Gtot)        Gtot = 43 + 49 = 92
louder: swaps only when the other drum's Energy (last control step) is > LOUDER_HYST = 2× (3 dB) higher
if (cap, louder) changed: Allowed = all; nShed = Gtot − cap;
   walk ShedOrder(quieter) then ShedOrder(louder), clearing Allowed until nShed is used up
   (each ShedOrder: its dense groups highest first, then its head groups ≥ CORE_HZ = 700 Hz highest first; core groups never)
every active group that is not Allowed: Fading for this block: pole radius × FadeMul = 10^(FADE_DB/20/Frames), FADE_DB −60
end of block: Fading groups zeroed and retired; a patch never activates a group that is not Allowed
```
So the quieter drum's crack goes first, then its high head modes, then the louder drum's crack; the bass, tone and the
low head modes always stay. The −60 dB exponential fade over one block never clicks (`BS`: click score 0.02). Measured:
the two-drum slap roll at Share 0.4 keeps at most 36 of 92 groups (cap 37) and does 43.6 % of the work (`UnitShareTests`:
≤ 50 %). Vectors: `test-vectors.json` `scenarioShare04` (the 3 s scenario at Share 0.4: cap 37, 9 groups shed, Work 44 666
against 54 560, FNVs per drum). On-device check T-HD-CPU (6.3).

---------------------------------------------------------------------------------------------------------------------------

## 3. Interaction (bare hands)

Reference: `tools/interaction.mjs` (`HandDrumInput.step`, `drumStep`, `classify`, `vel`, `palmLiftTarget`,
`stepPalmLift`, `sendStroke`). The C# `HandDrumsInput` transliterates it 1:1, with the same constants and the same order of
operations: the reference **is** the C# behaviour (v2: v1's C#-only addition for fingertip velocity is gone). It runs in
`HandDrumsModule.Tick` (`TickOrder` 22) on `HandInput` frames: hand joints from `MetaHandSource`, positions in world.

### 3.1 The head frames and the skin points

**Head frame** per drum:
- centre c = the head's centre;
- normal n points up and tilted 15° toward the player;
- e0 = the in-plane direction toward the player (θ = 0, the near edge), e1 = n × e0.

A point p has height h = n·(p − c), radius ρ = |in-plane offset| / a, and angle θ = atan2(e1·d, e0·d).

**Skin points** per hand: 14 points, in this order (`POINT_IDS`): palm, heel, then per finger (index, middle, ring,
little) knuckle, middle, tip. Each is the joint centre minus its radius along the **back-of-hand axis**
`back = normalize(frame.rotation[(int)HandJoint.Palm] * Vector3.up)` (HandFrame: +Y out of the back of the hand); the
heel is Wrist + 0.38 (Palm − Wrist). The radii are the Meta hand joint radii (`Docs/02-meta-hands.md`) at scale 1, × `handScale`:

| Point | Joint | r (m) | Group | Strikes and arms |
|---|---|---|---|---|
| palm | Palm (0) | 0.0232 | palm | yes |
| heel | Wrist (1) + 0.38 (Palm − Wrist) | 0.0182 | palm | yes |
| knuckle ×4 | Index/Middle/Ring/Little Proximal (7, 12, 17, 22) | 0.0103 / 0.0112 / 0.0099 / 0.0085 | fingers | **no** (they sit at the rim in open tones and stay low in heel-toe; they are used for the stroke's shape) |
| middle ×4 | …Intermediate (8, 13, 18, 23) | 0.0080 / 0.0080 / 0.0076 / 0.0068 | fingers | yes |
| tip ×4 | …Tip (10, 15, 20, 25) | 0.0076 / 0.0076 / 0.0072 / 0.0064 | fingers | yes |

The thumb is not used. Fingertip joint centres sit about 7–8 mm inside the skin, so a "touch" is h ≤ 4 mm *after* the
radius is taken off, not 17 mm between joint centres. Pinches stay Meta's pinch strength (spec 02) and play no part on
the drums.

**Tip curl** (for the slap): the mean over the 4 fingers of asin(−(tip − middle)·back / |tip − middle|), from the joint
centres, in degrees (0 flat; the canonical slap pose 38°).

### 3.2 Constants (`interaction.mjs` `K`; v1 value in brackets where it changed)

| Name | Value | Meaning |
|---|---|---|
| BAND | 4 mm | a skin point this close to the head, or below it, is in contact |
| ARM | **20 mm** [12] | a group arms once all its striking points are this far above the head (or off it, ρ > OFF), on the mean of the last ARM_N = 2 samples' heights. The kit's `DrumSpec.ArmAbove` |
| APPROACH | **100 ms** [–] | a crossing counts only if its group was fully above ARM within this before: a mean approach speed ≥ 16 mm / 100 ms = 0.16 m/s. The critique suggested 150 ms (0.107 m/s): with it and the first version of the other gates, 8 of 320 noisy 0.05–0.1 m/s placements struck; with every gate, 1 of 480 still struck at 120 ms and 0 of 480 at 100 ms. Real soft strokes (≥ 0.25 m/s) need ≤ 64 ms |
| GROUP_V | **0.7** [–] | the crossing group's mean fall speed (all its striking points) must be ≥ 0.7 V0 |
| OFF | ρ 1.25 | beyond this a point is off the head (counts as above ARM) |
| REACH | ρ 1.08 | a crossing beyond this missed the head (the rim or the air): no stroke |
| V0 | **0.25 m/s fingers, 0.30 palm** [0.15] | slowest stroke that sounds (`U_V0` in the engine) |
| V1 | 2.0 m/s | a full stroke |
| velocity | **LS slope over ≥ 50 ms of distinct samples, 3–6 samples** [one frame] | 3.3 step 2 |
| GAP_MIN, GAP_MAX | 4 ms, 0.2 s | `SampleVelocity`'s clamps: a closer sample is spaced to 4 ms; a gap over 0.2 s (or a time going back) restarts the history |
| DESC_N | **2** [–] | the crossing point's height fell over each of its last 2 sample steps |
| LAND_MS, LAND_NEAR | **8 ms, 12 mm** [one frame] | a point lands with the stroke if it is ≤ 12 mm up at t* and reaches the band within 8 ms |
| LAND_FLOOR | **6 mm** | … or is within 6 mm of the band at t* whatever its speed (tracking noise is 2–3 mm) |
| LAND_PALM_MS, LAND_PALM_NEAR | **20 ms, 20 mm** | the palm point's window (one rigid body with the heel) |
| HEEL_TILT, HEEL_TIPS | **15 mm, 25 mm** [8, 15 above the band] | a heel stroke: the palm ≥ 15 mm above the heel and the tips ≥ 25 mm up at t*, the palm not landing |
| CURL_DEG, CURL_MIN | **30°, 12°** | curled tips (≥ 30°) mean a slap; below 12° missing middles are noise, not a cupped hand |
| PALM_MAX, BASS_RHO | **ρ 1.0, 0.8** [0.7] | the palm sounds wherever it lands inside 1.0; labelled bass inside 0.8, open outside |
| TOUCH_U | **0.25** [–] | "touch" (finger pads, quiet) only below this stroke value |
| RIM_RHO | **0.95** | finger points beyond this are on the bearing edge |
| FINGERS_OK | 3 | fingers with `fingerHighConfidence` needed for the finger-shape classes (slap, toe, the bass's pads) |
| VREST | 0.12 m/s | a point slower than this in contact starts resting (velocity: LS over the last REST_VEL_N = 3 samples) |
| REST_MS | **14 ms** [2 frames] | a point rests this long before the drum is damped |
| REST_OUT, LIFT_V | 10 mm, 0.3 m/s | a resting point keeps resting until this high, or until it moves up this fast (jitter immunity) |
| REST_DEPTH | **30 mm** [–] | a point deeper than this under the head is through it, not on it: the turn of a hard stroke's overshoot must not mute the drum (case C18) |
| LIFT_MS | **25 ms** [2 frames] | nothing in contact this long → lift |
| WINDOW | 40 ms | per hand per drum: no second stroke inside it |
| TOE_AFTER, TOE_GRACE | 60 ms, **80 ms** | the toe may follow the heel after 60 ms, also if the heel rest lifted up to 80 ms before (the heel rises as the hand rocks) |
| MUTE_WITHIN | **90 ms** [60] | a rest this soon after an open or slap by the same hand relabels it mute / closed (the rest needs 3 still samples: 28–33 ms at 72/60 Hz, plus REST_MS) |
| LOST_HOLD | 150 ms | a resting hand that loses tracking keeps damping this long, then lifts |
| TRACKED | **25 ms** [2 frames] | a hand must be tracked this long again before it may strike |
| LIFT_MAX, LIFT_FADE, LIFT_TAU, LIFT_RHO | **60 mm, 90 mm, 25 ms, ρ 1.05** | the visual palm lift (4.5) |

All the timers that were frame counts in v1 are milliseconds now, so 72 and 90 Hz behave the same.

### 3.3 Each frame, per hand

1. **Tracking.** The hand needs `isTracked && isHighConfidence` (and not `poseInferred`; Wide Motion Mode stays off,
   spec 02). Otherwise: both groups of both drums are disarmed, the sample history and the previous pose are dropped,
   a rest is held for LOST_HOLD and then lifted, and the palm lift releases.
2. **Samples and velocity.** A frame with `newSample` adds one sample to the hand's ring of the last 6: its time
   (`sampleTime`) and the 14 points' heights over each head. The device's `position[]` is the predicted pose, which moves
   every frame; the sample's heights are taken from that frame (a constant prediction offset does not change a slope).
   A point's **velocity** is the least-squares slope of its height against sample time over the newest n samples, with
   n the smallest of 3…6 whose time span is ≥ 50 ms (at 72 Hz 4 samples, at 60 Hz 4, at 30 Hz 3), and **v = −slope**
   (positive toward the head). Fewer than 3 samples: v = 0, no stroke. The reference gets the same from its scripted
   hands (a repeated pose object = no new sample; `sampleTime` from the tracker clock).
3. **The palm lift** (4.5): the deepest palm or heel skin point under a head it is over (ρ ≤ 1.05), lift =
   `palmLiftTarget(depth)`, smoothed by `stepPalmLift`.
4. Then, per drum, steps 5–9.
5. **Arming.** A point is "above" if ρ > OFF or the mean of its last 2 sample heights is > ARM. Group g (palm = palm +
   heel; fingers = middles + tips) arms when all its points are above, once the hand has been tracked for TRACKED, and
   `armedAt[g]` = the pose time. **After a stroke (lockout)** neither group re-arms until all 10 striking points are
   above; the exception is the toe: while this hand's heel rests here (or lifted within TOE_GRACE), the finger group
   re-arms on its own points.
6. **Crossing.** Over the segment from the previous pose to this one (only when the positions changed), point i
   crosses if: it was h > BAND and is now h ≤ BAND; the crossing radius (interpolated at the crossing) is ≤ REACH;
   v_i ≥ V0(group); its height fell over its last 2 sample steps; its group is armed with now − armedAt ≤ APPROACH; and the
   group's mean v over its striking points ≥ 0.7 V0. The crossing fraction s_i = (h_prev − BAND)/(h_prev − h_now).
   The segment's time base is the time the positions are for: `sampleTime` here, the display time of the predicted pose
   on the device.
7. **Stroke.** If any point crossed: the first crossing (smallest s) defines t* = t_prev + s·(t_now − t_prev).
   - u from the **lead point** (the fastest crossing point): its current speed max(v, LS slope over its last 3 samples)
     (the 50 ms window is the noise gate; a short stroke's 50 ms window reaches back into the still hand before it), then
     u = clamp(((v − V0g)/(V1 − V0g))^0.7, 0.05, 1) with the lead point's group's V0. 0.4 / 1.0 / 2.0 m/s give u
     0.18 / 0.55 / 1.0 (case C17).
   - `classify` (3.4) on the pose at t*.
   - It fires unless this hand struck this drum less than WINDOW before t*; a toe after a heel may fire after TOE_AFTER.
   - **Either way, both groups are disarmed and the lockout starts** (one landing, one stroke).
8. **Rest.**
   - Point i (ρ < 1, h ≥ −REST_DEPTH) is resting while it is in contact (h ≤ BAND) with |v_rest| < VREST, or while it was
     resting and is still h ≤ REST_OUT with v_rest > −LIFT_V. v_rest is the LS slope over its last 3 samples.
   - A rest is sent once points have been resting for REST_MS. It is sent again only when the parts change or a point moves
     by more than ρ 0.07, and that change must also hold for REST_MS. Rest points: palm or heel; the mean of the resting
     finger points (part `tips` if all are tips, else `fingers`).
   - A rest within MUTE_WITHIN of this hand's open or slap relabels that stroke `mute` / `closed` for the recorder and
     the lessons.
   - A lift is sent after LIFT_MS with nothing in contact. A heel rest's lift starts TOE_GRACE.
9. The previous pose (and its time) is replaced only when the positions changed.

### 3.4 Classification (`classify`, on the pose interpolated to t*)

At t* every point has h*, ρ*, θ* (linear along its segment) and a fall speed v* = max(its segment speed, its LS v).
**A point lands** if ρ* < REACH and: h* ≤ BAND + LAND_FLOOR, or h* ≤ LAND_NEAR and it reaches BAND within LAND_MS
((h* − BAND)/v* ≤ 8 ms); the palm point uses LAND_PALM_NEAR and LAND_PALM_MS. (v1 tested h − v·dt ≤ BAND, a whole
frame ahead: 28 mm at 2 m/s, so a slap's cupped middles "landed" and fast slaps became open tones.)

Knuckle ρ = the mean ρ* of the 4 knuckles, knuckle θ likewise; kn = min(0.97, knuckle ρ). A **fingers line** = 4
points from (kn, knuckle θ) to the mean (ρ*, θ*) of the given landing points, at k/3. Explicit points are at most 3, in
finger order. Tested in this order:

| # | Stroke | Condition | Patches sent (part, u scale, delay, shape) |
|---|---|---|---|
| 1 | **toe** | this hand has a heel rest here (or lifted within TOE_GRACE), fingers OK, and ≥ 2 tips (or ≥ 2 middles) land | pads, 1, 0, points: the landing tips (or middles) |
| 2 | **heel** | the heel lands at ρ < 0.75, the palm does not land, palm h* − heel h* ≥ 15 mm, mean tip h* ≥ 25 mm | heel, 1, 0, point |
| 3 | **bass** / **open** | the palm lands at ρ ≤ 1.0. Label bass if ρ ≤ 0.8, else open | palm, 1, 0, point; and if ≥ 2 finger points land: inside 0.8 (fingers OK) pads, 0.6, 4 ms, points (the landing tips); outside 0.8 a fingers line, 1, 0 |
| 4 | **slap** | ≥ 2 tips land, knuckle ρ ≥ 0.7, and (≤ 1 middle lands with tip curl ≥ 12°, or tip curl ≥ 30°) | fingers OK: base, 0.8, 0, point (kn, knuckle θ); tips, 1, 0.8 ms, points (the landing tips). Fingers not OK: **open**, a fingers line to the landing tips (v1: silence) |
| 5 | **open** | ≥ 3 middles + tips land and knuckle ρ ≤ 1.35. Labelled **touch** if fewer than 2 landing finger points are inside ρ 0.95 (fingers along the rim: a rim knock) | a fingers line to the landing tips (or middles), 1, 0. With low finger confidence the same (v1: silence). Knuckles inside 0.7 is the open family on the mid-head (`zone` "open") |
| 6 | **touch** / other | anything else that lands: u < 0.25 → touch: pads at the first landing finger point (else the first landing point). u ≥ 0.25: ≥ 2 finger points → open (touch if < 2 inside 0.95), up to 3 explicit points; 1 finger point → touch; a palm-group point → its own part (bass / heel inside 0.8, open outside) | as stated |

The sound follows the patches; the label never chooses a quieter part than the one that landed (v1's dead ring: a hard
flat palm at ρ 0.70–0.85 was sent as quiet pads). The engine also moves a point at the bearing edge to the on-head part's
centroid (2.3), so a palm on the rim is a muffled edge thump, not silence.

**When.**
- The first stroke of a frame posts `When = 0` (ASAP, the lowest latency).
- A further stroke in the same frame (a flam) posts the relative `When = −(t*_k − t*_first)`, so the flam's spacing
  survives the frame (case C06: 15.0 ms at 72/72, 90/90, 72/60 and 90/60).
- A patch's delay (the bass's pads 4 ms, the slap's tips 0.8 ms) is added to its stroke's `When`.
- Predictive strikes (burst.md 12, lever 3) are off.
- Fast Motion Mode is requested while hand-drumming (descriptor trait `FastMotion`; E4's `FastMotionPolicy`, with its
  low-light guard and `tip_more_light`). At the LOW rate (30 Hz) two cases degrade (3.7).

### 3.5 What the module sends

- On a stroke: one `Patch` per patch (2.12), u × the patch's u scale, the shape and points of 3.4.
  `sendStroke(eng, at, drum, hand, st)` is the reference of this mapping.
- On a rest: up to 3 points (palm or heel; finger centroid; tips centroid).
- The recorder (platform 6.1 types, prefixed `handDrum`):
  - `handDrum` (Onset, Kind) with `kind` "conga" | "tumba", `zone` the stroke ("open", "mute", "slap", "closed",
    "bass", "heel", "toe", "tip" for touch), `pos` the main patch's ρ, `vel` u, `i` the hand (0 L, 1 R), `u` the main
    θ / 2π + 0.5;
  - `handDrumMute` (State, Kind) with `open` = false on a rest and true on the lift, `pos` the rest's first ρ.
  - **Contract request:** the platform's provisional zone list ("open", "slap", "bass", "mute", "tip") gains "closed",
    "heel" and "toe".
- **Playback** (`IInstrumentPlayback.Play`): `handDrum` calls `IHandDrumSound.Stroke(kind, u, θ)` with the canonical
  geometry turned to the recorded θ; `handDrumMute` calls `Rest` with the canonical points of the stroke family, or
  `Lift`. `ReleaseAll` lifts both hands on both drums. Live and playback agree in level: the palm's speed law depends on
  u only (2.3), so a recorded bass replays at the loudness it was played.
- `PlayStamp.Mark(hand, kind)` and `LastPlayMs` on every stroke and rest. Layout's grab guard and the menu summon stay
  quiet while a hand is drumming (`Pointer.IsPlaying`).
- Every frame: `MetaHandVisual.SetPalmLift(side, palmLift[side])` (4.5; VR only).

### 3.6 Zones and their sizes (conga a = 14.6 cm; tumba 15.6 cm)

| Zone (what lands there) | ρ | Width on the conga | Strokes |
|---|---|---|---|
| Centre (palm) | 0–0.8 | Ø 23 cm | bass, heel (heel ρ < 0.75) |
| Edge palm | 0.8–1.0 (palm centre) | a 3 cm ring | open (a palm and fingers line: heavier, more muffled than the fingers alone) |
| Mid-head (cupped tips) | 0.4–0.7 | a 4.4 cm ring | slap tips (knuckles at the rim) |
| Near-edge band (flat fingers, knuckles at the rim) | knuckles 0.7–1.35 | from 4 cm inside to 5 cm outside the rim | open, mute |
| Rim and air | > 1.08 at the crossing | | no stroke (case C14) |

The near-edge band faces the player (θ = 0 ± 60°). The hands' natural positions are θ ≈ −0.35 (left) and +0.35
(right) rad. Any θ works; the sound follows the measured θ. There is no dead zone between them (`SW`).

### 3.7 Scripted-hand and seeded-noise cases (`out/interaction-cases.json`; EditMode on `HandDrumsInput`, PlayMode with `FakeHandSource`)

**Scripted cases** (no noise). Each runs at four app/tracker rate pairs: 72/72, 90/90, 72/60 and 90/60 Hz (the tracker's
pose is held, the same object, between its samples). **72 of 72 pass.** Measured at 72/72:

| Case | Script | Expected (measured) |
|---|---|---|
| C01 open | flat fingers, knuckles on the near rim, palm off the head, 1.2 m/s | one `open`, u 0.65 |
| C02 bass | flat palm at ρ .05, 1.0 m/s | one `bass`, u 0.55 |
| C03 slap | cupped (tips 3–4 cm below the palm plane), knuckles at the rim, 1.6 m/s | one `slap`, u 0.83 |
| C04 mute | as C01, fingers stay 0.3 s | `open` relabelled `mute`, `rest` at 222 ms (60 ms after t*), `lift` at 528 ms |
| C05 heel-toe | heel lands (palm 1 cm up), stays, the hand rocks: tips land 0.35 s later | `heel`, `rest`, `toe` (u 0.48), `lift` |
| C06 flam | left then right open tone, 15 ms apart | two `open`, hands 0 then 1, **t* 15.0 ms apart** (± 3 ms required) |
| C07 roll | alternating hands, 12 strokes/s for 1 s, 5 cm lift | 12 `open`, none lost or doubled |
| C08 jitter | a hand laid at 0.08 m/s, then ±3 mm sinusoidal jitter for 0.8 s | ≤ 2 rests, **no stroke**, no rest re-sent per frame |
| C09 slow placement | a palm laid at 0.08 m/s on a ringing drum | rests (damps), no stroke |
| C10 from below | tracking returns with the hand under the head, then it rises | no stroke (a rest and a lift) |
| C11 lost mid-stroke | the hand is lost for 70 ms during the down-swing and found below the head | nothing |
| C12 low finger confidence | 2 of 4 fingers low: a palm bass, then an open stroke | `bass`, then **`open`** (v2: the measured fingers line; v1 silence), never a slap |
| C13 palm then fingers | the palm lands, the fingers slap down 20 ms later | exactly one `bass` |
| C14 rim miss | a hand comes down on the rim / outside (ρ ≈ 1.2) | nothing |
| C15 too slow | 0.1 m/s touch | rests, no stroke |
| C16 two drums | left bass on the conga, right two opens on the tumba | `bass`@conga, `open` ×2 @tumba |
| C17 speed | open strokes at 0.4 / 1.0 / 2.0 m/s | u 0.18 < 0.55 < 1.0 |
| **C18 overshoot** (v2) | a 2 m/s open tone that goes 40 mm through the head and turns smoothly | one `open`, **no rest, no mute** |

**At the LOW tracker rate (72/30, reported, not required):** 16 of 18. C06's flam spacing reads 21 ms (the 33 ms
samples straddle the stroke's turn) and C07's roll keeps 7 of 12 strokes (each down-swing is 1–2 samples). The module
requests Fast Motion Mode while hand-drumming; T-HD-CLASS measures the real rates.

**Seeded-noise cases** (`statCases`; noise per tracker sample: σ per joint and σ for the whole hand, mulberry32 +
Box–Muller, seeds in the file). **88 of 88 pass.**

| Case | What | Rates | Pass rule | Measured |
|---|---|---|---|---|
| C03b–d | the C03 slap at 2, 3, 4 m/s, σ 2 mm per joint, 40 trials each | 72/72, 90/90, 72/60, 90/60 | ≥ 90 % slap | 97.5–100 % |
| C01b–d | the C01 open tone, the same | same | ≥ 90 % open | 100 % |
| C08b | σ 2 mm per joint + 2 mm whole hand; the hand comes down slowly and is held 60 s with the palm skin at −4, 0, +6, +12 mm, at the centre and at the edge | 72/60, 72/30, 90/60, 90/30 | 0 strokes | 0 in 32 runs (32 minutes held) |
| C09b | 20 placements from 6 cm at 0.05–0.1 m/s, same noise, held 1 s, at the centre and at the edge | same | 0 strokes | 0 in 160 placements |
| C13b | fingers-first (pitch −0.15) and palm-first (+0.15) strokes at 0.3 and 0.6 m/s | 72/30, 72/60, 72/72, 90/90, 90/60, 90/30 | exactly 1 stroke | 24 / 24 |

**The critic's studies, re-run on v2** (`critic/out-v2/`):
- c5c (placements at σ 1–3 mm, per joint and whole hand, then 30 s held at −4 / +6 / +15 mm): 0 strokes in all 36
  rows (v1: 6–13 per 5 placements, up to 174 per minute held).
- c5 B (held at 5 heights, σ 1–5 mm, 20 s): 0 false strokes in 40 rows (v1: false strokes in 34).
- c5 C (canonical strokes at 0.5–4 m/s): at σ 2 mm, open 40/40 at every speed, bass 39–40/40, slap 39–40/40; at σ 4 mm
  (twice the spec) 75–100 %.
- c5 D (60 and 30 Hz trackers): no double strokes (v1: bass+bass 13 of 24 at 30 Hz); u 0.25–1.0.
- c5 A (384 novice strokes): 187 open, 120 bass, 76 slap, 1 touch (a hand landing along the rim at u 0.32) (v1: 91
  touch or touch + X).

### 3.8 Comfort and safety (seated)

- **Heights.** Head centres at 0.75 m, 0.45 m below a seated eye (1.2 m), at or just above elbow height. The heads
  tilt 15° toward the player (the posture of a conga tipped forward between the knees), so the wrist stays neutral and
  the forearm comes down onto the near edge.
- **Reach.** The near edge is 0.43 m (horizontal) from the eyes, so open tones and slaps are played with a bent arm.
  The centre (bass) is 0.57 m from the eyes and 0.60 m from the shoulder, within 2 ft. The far half is never needed.
- **Real legs.** The heads are 13 cm above the knee box. A hard stroke overshoots the virtual head by 2–8 cm, and the
  real thighs are well below that. The shells clear the knee box by 3 cm (§4.3). In VR the drawn hand is lifted back onto
  the skin while it is through it (4.5).
- **Arrange-mode height.** `LiftMin/Max` −0.06 / +0.12 m (the basket legs). In MR or when standing (eye > 1.45 m), the
  first placement lifts by clamp(eye − 1.2 − 0.25, 0, 0.12).
- **Fatigue.** Lessons are under 20 s at tempo, and wait mode removes time pressure.
- **Handedness.** The tumba is on the right, where right-handed players put their open tones. A "swap drums" setting
  (`musicroom.handdrums.swap`) is optional for v1.1.
- **Resting is safe.** A player can rest both hands on the heads (to quiet them, or just to rest the arms) for as long as
  they like without a stroke (C08b: 32 minutes of noisy holds, none), and laying a hand down slowly never plays (C09b).

### 3.9 Mixed reality

- The pair is a floor-only lead (`Mr.OnTable` false). It stands on the real floor in front of the player, at the seat
  frame home, with `IMixedReality.TryGetLeadPose` (platform 9).
- Real hands are shown (E6 PassthroughCutout). The contact shadows (E3) and the head dents (§4.5) carry the contact
  cue that a virtual hand's clamp gives in VR.
- The shells cast shadows (instrument bodies only).
- Real furniture: Layout's placement is checked against the MRUK furniture volumes (an open point, section 10).
- **The hand logic is identical in MR and VR.** The room unit's MR path takes the acoustics.

---------------------------------------------------------------------------------------------------------------------------

---------------------------------------------------------------------------------------------------------------------------

## 4. Visual

### 4.1 Geometry (procedural, `HandDrumsBuilder`; rig-local, rig origin on the floor under the midpoint of the head centres)

| Part | Conga (x −0.185) | Tumba (x +0.19) | Mesh |
|---|---|---|---|
| Head centre | (−0.185, 0.750, 0) | (0.19, 0.755, 0) | |
| Head | Ø 0.292 vibrating, 0.298 to the crown | Ø 0.312 / 0.318 | a radial grid of 12 rings × 48 segments (dynamic, 1 152 tris each) |
| Crown hoop | torus R 0.157, tube 5 mm, 6 mm above the head plane | R 0.167 | 64 × 8 |
| Shell | staved barrel: 12 staves; profile Ø 0.300 (top) → 0.335 (belly at 45 % from the bottom) → 0.235 (bottom); 0.762 tall along the axis | 13 staves; 0.320 → 0.360 → 0.255 | lathe, 2 facets per stave (a slight crease at each joint), 16 rings: about 1 500 tris |
| Axis tilt | 15° toward the player: the axis top is toward −z, the bottom 0.197 m farther away | same | |
| Hardware | 6 lugs (side plates 70 × 18 mm at 12 cm below the rim, tension rods to the hoop), a chrome bottom ring | same | merged |
| Basket stand | a ring under the belly at 0.10 m height, 3 legs to the floor, rubber feet; front legs 0.03 m shorter (the tilt) | same | merged |

Footprint: 0.73 × 0.50 m; boxes in 4.3. Triangles: about 9 k for the pair at the seat, 4 k stowed (heads as one
static ring each).

### 4.2 Materials (templates under `Resources/MaterialTemplates/HandDrums/`)

| Draw | Template | Look |
|---|---|---|
| 1 Shells | URP Lit, `HandDrums/shell` | staved ash, warm amber satin lacquer: base #b07a45, smoothness 0.55, grain along the staves (texture `HandDrums/staves` 512 × 256 from `ProcTex.Fbm`: long-grain streaks, a 1 px darker seam at each stave edge in UV u) |
| 2 Heads | URP Lit, `HandDrums/rawhide` | rawhide: cream-tan #d9c7a1 with fibre mottling (fbm, 2 octaves) and 8 % translucent-looking darker blotches; a darker, smoother band where the hide wraps the bearing edge; **hand wear**: a faint darker oval at the near edge (θ 0 ± 50°) and in the centre; smoothness 0.25 |
| 3 Hardware | URP Lit `LitMetal`, `HandDrums/chrome` | polished chrome (hoops, lugs, rods, bottom rings, basket); the stand legs use the same material with a darker vertex-free UV region of the shared `Core/grille` texture |

Textures are baked by the module's `ITextureBakeProvider` (`GeneratedAssets.Texture("HandDrums/<name>", …)`), with
names prefixed `HandDrums/`. No glow and no emissive.

### 4.3 Placement (seat frame)

| | Value |
|---|---|
| Canonical slot / home (All) | Front, rig (0, 0, 0.57) yaw 0 (head centres 0.57 m ahead of the eyes) |
| Single home | the same |
| Slot preferences | FrontRight, FrontLeft, Right, Left, Front (platform) |
| ReachMax, PlayCentre | 0.62 m; rig-local (0, −0.05) |
| Boxes (rig-local) | heads `{cx .005, cz 0, hx .36, hz .165, y0 .62, y1 .80}` (may pass over the knees); shells + stands `{cx .005, cz .085, hx .36, hz .21, y0 0, y1 .62}` (near face 0.445 m: clears the knees box 0.41 m + 3 cm) |
| LiftMin / LiftMax | −0.06 / +0.12 |
| Stow (platform 4.1) | `window-far`, floor, (2.75, −1.80, 90): under the far end of the window |
| Audio ErOrigin | rig-local (0, 0.40, 0.10): between the heads and the bottom openings |

These replace the platform's provisional numbers (home z 0.62, one box pair). `tools/seatcheck.mjs` re-runs the
platform's planner with them: alone, all 7 pairs and 20 of 21 triples still fit, and the slots are unchanged except
piano + theremin + hand drums, where the drums take Left instead of Front.

Pair slots:

| Partner | piano | harp | theremin | bowls | handpan | marimba | drums |
|---|---|---|---|---|---|---|---|
| Hand drums take | Right | FrontRight | FrontLeft | FrontLeft | FrontLeft | Right | FrontLeft |

### 4.4 Draw calls

**3 at the seat** (shells, heads, hardware) against the cap of 6, and 3 stowed. The lesson print and the contact
shadows belong to the tutorial and E3 draws. In the worst MR set (piano + hand drums + drums) the platform counts 74
with 6 for us, so 3 leaves margin.

### 4.5 What moves when played (physical feedback, no glowing UI)

- **Head dent** (CPU vertex displacement of the head grid, written only while moving; reference `tools/visual.mjs`,
  vectors `out/visual-vectors.json`):
  - **The grid:** per head, a centre vertex plus 12 rings × 48 segments; vertex (r, s) at ρ = r/12 (r = 1…12),
    θ = 2πs/48 in the head frame (3.1). Ring 12 is the bearing edge and never moves.
  - **Depth of one patch:** depth = 6 mm · min(1, (F_max / 400 N)^0.6), with F_max **looked up**, not solved: the
    engine's own contact solve gives F_max per part at u .2 / .5 / .8 / 1 (`FMAX` in `visual.mjs`, the `verify.json`
    contact table; linear in u, and × u/.2 below .2). The main thread never runs the contact solve.
    E.g. palm u .8: 168 N → 3.6 mm; fingers 121 N → 2.9 mm; tips 52 N → 1.8 mm.
  - **Shape:** vertex displacement (into the head, along −n) = min(depth, depth · Σ_points exp(−d²/(2σ²)) / √npts) ·
    (1 − ρ²), σ = b + 15 mm, d the in-plane distance from the vertex to each of the patch's points (the same points as the
    `Patch` command). E.g. a u .8 bass: 3.4 mm at the centre, 1.35 mm at ρ .5; a u .8 open tone: 2.2 mm at ρ .5 on its
    line, 0 across the head.
  - **Time:** each dent starts at full depth at its patch's `When` and recovers as a critically damped spring,
    depth(t) = depth (1 + t/τ) e^(−t/τ), τ 25 ms. Dents add; the grid is written while any is above 0.05 mm, then it is
    static again. Normals are recomputed from the displaced grid (central differences on the polar grid).
  - **A resting hand** keeps a 1.5 mm dent under its rest points (same shape) until the lift.
- **Ringing** (optional): the head grid's centre moves by ±0.3 mm at the tone frequency's visual alias, scaled by
  `Level(drum)`. That is subliminal, but a ringing head is never dead-still. Off in the stowed state.
- **Hands:**
  - E1 finger clamp on the heads (`IContactSurface` with `ContactUse.Both`: drawn fingers stop on the skin);
  - **the palm lift (v2: required, contract request 3; critique M4).** Without it every hard palm stroke shows the hand
    2–8 cm inside the skin, which E1's finger-only clamp (LiftMax 25 mm, fading out by 40 mm) cannot hide. Per hand, per
    frame (`HandDrumInput.palmLift`, reference `palmLiftTarget` / `stepPalmLift`):
    - depth = the largest of −h over the palm and heel skin points, over any head they are over (ρ ≤ 1.05), else 0;
    - target = depth for depth ≤ 60 mm; 60 mm · (90 mm − depth)/30 mm between 60 and 90 mm; 0 beyond 90 mm (a hand
      that deep is going down to the lap, not playing; it must not float);
    - lift = target when target ≥ lift (attack instant), else target + (lift − target) · e^(−dt/25 ms) (release);
    - `MetaHandVisual.SetPalmLift(side, lift)` raises the drawn hand mesh (all its joints) by lift along the head normal.
      Visual only: the hand data and the stroke logic never see it.
    - Vectors: depth −10 / 0 / 20 / 60 / 75 / 90 / 120 mm → 0 / 0 / 20 / 60 / 30 / 0 / 0 mm; the release from 50 mm to 0 over
      one 72 Hz frame → 28.7 mm. The C02 bass (12 mm deep) peaks at 10.5 mm of lift and releases in about 70 ms.
    - **MR keeps the real hands** (E6 PassthroughCutout): no lift there; the contact shadows and the dents carry the cue.
  - E3 contact shadows under the palm and fingers.
- **Lesson target** (`ILessonTargets.ShowTarget`): a matte, slightly darker hand-print decal on the skin in the pale
  warm cue colour:
  - palm print in the centre (bass);
  - finger line at the near edge (open, mute);
  - cupped fingertips (slap);
  - heel crescent (heel);
  - fingertip dots (toe);
  - the drum's near-edge ring for "any".

  k fades it in; there is no glow.
- **The drum itself doesn't move.** A conga is heavy and stands on its basket.

---------------------------------------------------------------------------------------------------------------------------

## 5. Lessons and Sunny

### 5.1 Lesson pack (`Resources/LessonPacks/handdrums.json` ← `out/handdrums-lessons.json`, the `LessonFile` schema)

`LessonSpec`:
- Travel 0.35, LeadSeconds 1.944 (the drums' constants);
- Shape `Disc`, Notation `Percussion`, Pack `handdrums`, CountNoun "strokes".

Targets use the platform's `LessonTarget.zone` field: `{kind: "conga" | "tumba", zone: "any" | "open" | "bass" |
"slap" | "heel" | "toe", label}`. "any" accepts every stroke on that drum. The hand is named in the event's `hint`
("Slap on the left drum, right hand"), because `LessonTarget` has no hand field. Ids carry the `handdrums-` prefix.
The pack is copied as generated.

| Id | Title | bpm | Content | Events | At tempo / with countdown and lead | Why |
|---|---|---|---|---|---|---|
| `handdrums-first` | First taps | 84 | 4 taps on the left drum, 4 on the right, twice; any stroke counts; one target at a time | 16 | 11.4 s / 16.4 s | **the guaranteed 30-second win**: any hand, any stroke, a big drum, wait mode |
| `handdrums-bass-tone` | Bass and tone | 80 | bass, open, bass, open on the conga ×2 bars; then bass, open, open (tumba), open (tumba) ×2 | 16 | 12.0 s / 16.9 s | the two main sounds, then the open tones move right |
| `handdrums-tumbao` | Tumbao | 76 | per bar: heel (L, conga) on 1, slap (R, conga) on 2, heel (L) on 3, open (R, tumba) on 4 and 4-and; ×4 | 20 | 12.6 s / 17.6 s | the classic conga groove, simplified (the toes are left out) |

**Verified:**
- `lessons.mjs` checks the beats fill whole bars, ≤ 1 target per event, the same hand never needs two strokes closer
  than 0.3 s (shortest 0.395 s), and that lesson 1 fits 30 s;
- platform 7.4: ≥ 3 lessons, first a guaranteed win, ≤ 2 targets, gaps ≥ 0.2 s, each under 1 minute.

**Public domain.** Lessons 1–2 are plain pulse patterns. Lesson 3 is the traditional Afro-Cuban tumbao (marcha):
H T S T H T O O in the clave-neutral son-montuno form (WIKI "Tumbao"). Rhythms carry no copyright, and no melody or
lyrics are involved.

**Matching** (`ILessonTargets.Matches`): a live `handDrum` with the target's `kind` and a matching `zone` (an edge-palm
stroke, ρ 0.8–1.0, is an "open"; 3.4):
- "open" also accepts "mute";
- "slap" also accepts "closed";
- "any" accepts everything.

`TryGetLanding` is a disc on the head: at the centre for bass and heel, at ρ 0.75 on the near side for open and slap,
at ρ 0.5 on the far side for toe. The approach is along −n, so blocks fall onto the skin, not into the shell.

### 5.2 Sunny tips (lines ≤ 10 words; ids prefixed `tip_handdrums_`; clips via the sunny-tour 5.4 pipeline)

| Tip id | Class, trigger | Line (words) | Demo @ anchor | Success |
|---|---|---|---|---|
| `tip_handdrums_arrive` | P2, first `InstrumentMoveEvent{Arrived}` | "Hit the drums with your hands." (6) | `Slap` @ `head.conga.edge` | first live `handDrum` |
| `tip_handdrums_open` | P3, ≥ 3 strokes and no open tone yet | "Fingers flat at the edge, then bounce off." (8) | `Slap` (edge only) @ `head.conga.edge` | `handDrum` zone open |
| `tip_handdrums_bass` | P3, ≥ 6 strokes and no bass | "Flat palm in the middle for the deep bass." (9) | `Strike` (palm) @ `head.conga.centre` | zone bass |
| `tip_handdrums_slap` | P3, ≥ 10 strokes and no slap | "Cup your fingers and snap the tips down." (8) | `Slap` (cupped) @ `head.conga.mid` | zone slap |
| `tip_handdrums_mute` | P3, a slap and an open played, no mute or closed yet | "Leave your fingers on the drum to mute it." (9) | `Hold` @ `head.conga.edge` | zone mute or closed |
| `tip_handdrums_heeltoe` | P3, the Tumbao lesson opened, or ≥ 20 strokes and no heel | "Rock your hand: heel, then fingertips, softly." (7) | `Tap` (rocking) @ `head.conga.centre` | a `toe` within 1 s of a `heel` |
| `tip_handdrums_quiet` | P3, a drum ringing ≥ 1 s twice, no rest yet | "Lay a hand on a ringing drum to quiet it." (10) | `Hold` (palm) @ `head.tumba.centre` | `handDrumMute` |
| `tip_handdrums_roll` | P3, ≥ 30 strokes and never 4 alternating strokes under 0.25 s | "Alternate your hands quickly for a roll." (7) | `Tap` (two hands) @ `head.tumba.edge` | 6 alternating strokes in 1.2 s |
| `tip_handdrums_lesson` | P2, first hand-drum lesson block landing | "Play the stroke the hand print shows." (7) | – | the first hit |

- `again` lines reuse the generic "Like this." (sunny-tour 3.1). Poking Sunny skips, as everywhere.
- Every line is ≤ 10 words, plain English, with no brand names and no all-caps labels (so `spoken` = `text`).
  Rendered, each is about 2–3 s (the pipeline measures them; lint ≤ 4.0 s).
- **Anchors** (`IGuideAnchors`): `head.conga.edge` (ρ .85, θ 0), `head.conga.centre`, `head.conga.mid` (ρ .55),
  `head.tumba.edge`, `head.tumba.centre`; up = the head normal.
- **Features** (`Covers`):
  - `handdrums.play`, `handdrums.open`, `handdrums.bass`, `handdrums.slap`, `handdrums.mute`, `handdrums.heeltoe`,
    `handdrums.rest`, `handdrums.roll`, `handdrums.lessons`, each covered;
  - `handdrums.glide` and `handdrums.dent` are Passive ("the head's physical response; nothing to learn").

---------------------------------------------------------------------------------------------------------------------------

## 6. Tests for the Unity build

### 6.1 Test vectors (`out/test-vectors.json`; copy to `Tests/EditMode/Audio/HandDrums/Reference~/`)

Setup: `HandDrums(48000, seed 1)` with the exported tables and seeds, 256-frame blocks, 3 s, these events (sample =
round(t · 48000), strokes via `playStroke`, θ 0):

| t (s) | Event |
|---|---|
| 0.000 | open, conga, R, u .8 |
| 0.250 | bass, tumba, L, .7 |
| 0.500 | slap, conga, R, .9 |
| 0.620 | rest conga L `mute` |
| 0.800 | lift conga L |
| 1.000 | heel, tumba, L, .5 |
| 1.012 | rest tumba L `heel` |
| 1.200 | lift tumba L, then toe, tumba, L, .4 |
| 1.500 | open, conga, L, .7 |
| 1.512 | open, conga, R, .7 |
| 2.000 | rest tumba R `palm` |
| 2.500 | lift tumba R |
| 2.600 | slapClosed, conga, R, 1.0 |

Expected (by **drum** index; the unit writes drum 0 to channel 1 and drum 1 to channel 0, 2.12):

| Drum | First non-zero | First 4 samples | rms per window 0–.05, .05–.1, .1–.25, .25–.5, .5–1, 1–1.5, 1.5–2, 2–2.5, 2.5–3 s | Peak | Strict FNV-1a |
|---|---|---|---|---|---|
| conga | 1 | 6.48688816e-4, 4.29162569e-3, 1.34374397e-2, 2.96441615e-2 | .298678, .184693, .0874168, .0200952, .116315, 4.81124e-6, .184089, .00326254, .0888648 | 1.63335 | `0x86845898` |
| tumba | 12001 | 1.67923536e-5, 1.40797230e-4, 5.09356556e-4, 1.27601577e-3 | 0, 0, 0, .151293, .00725304, .0318013, .00237325, 9.10805e-6, 0 | 0.851051 | `0x19d1f530` |

**The same scenario at Share 0.4** (`scenarioShare04`, budget scaling): cap 37 groups, 9 group fades, Work 44 666
group-subblocks (Share 1: 54 560); FNV conga `0x6382f673`, tumba `0x1d2e7db3`; peaks 1.51762 / 0.829361.

**Tables** (FNV-1a over the float32 arrays):

| | shape | go | avgTab |
|---|---|---|---|
| conga | `0xf1a980c7` | `0x912f4e18` | `0x9a54ef74` |
| tumba | `0x0695585c` | `0x5ce8b57e` | `0x52c9382b` |

The go hashes are of the table's go before the level and trim, as exported. The first conga modes are B 78 Hz (M
0.0779688 kg), T 261.6 Hz (M 0.02802), A2 300 Hz, then (1,1) 386.91775 / 388.65 Hz. Counts: conga 170 modes, 43 groups,
cavity group 42; tumba 194, 49, 48.

**Contact probes** (one patch per part, u .8, conga ρ .5, θ 0):

| Part | V (m/s) | Kh (N/m) | Zh (N·s/m) | F max (N) | Contact samples | FNV(dF[0..1024)) |
|---|---|---|---|---|---|---|
| palm | 1.996754 | 64835.51 | 27.5519 | 182.8939 | 330 | `0xc01db705` |
| heel | 1.284402 | 49941.10 | 17.31834 | 93.17748 | 356 | `0x9b59c3b4` |
| fingers | 3.553206 | 37316.19 | 9.446365 | 106.5153 | 143 | `0x4d481829` |
| tips | 4.781139 | 29988.54 | 5.51038 | 52.10184 | 56 | `0x1697f29c` |
| base | 2.390569 | 47535.38 | 15.74394 | 153.9573 | 250 | `0x1715a1e9` |
| pads | 1.284402 | 37316.19 | 9.446365 | 29.45085 | 231 | `0xa26d6423` |

Also: the canonical open tone's first 4 per-mode gains 0.0083809514, 0.0069534816, 0.0031695233, 0.018262897; the
conga RNG state after it, 2149693180.

**Laws** (`laws`): `partV` per part at u 0 / .25 / .5 / .75 / 1; `handSpeed`; `uOf` per group; `hertzAvg` at x 0, .04,
.06, 1, 2.82, 3.45, 4.49 (1, 0.99984, 0.99964, 0.903506, 0.400188, 0.217980, 0.000495); `palmLiftTarget`;
`stepPalmLift`; `channelOfDrum` [1, 0]; `sources` [HandDrumLo, HandDrumHi].

**Visual** (`out/visual-vectors.json`): dent depths per part and u; 18 vertex displacements (a u .8 bass and a u .8 open
tone); the dent spring at 0 / 10 / 25 / 50 / 100 ms.

**Tolerances:**
- managed (`!BurstCompiler.IsEnabled`): bit-exact FNV, because the contact is double with IEEE ops and the kernels are
  canonical `math.mad` nests;
- Burst `FloatMode.Fast`: first 8 samples within 1e-4 absolute, window rms within ±0.3 dB where the window is above
  −80 dB re the peak, peak ±0.3 dB.

A structural bug (mode order, a missed patch delay, the cavity group fed dF instead of Pop, a wrong RNG order, the
channel map, the rim clamp before the variation) shows as whole decibels.

### 6.2 EditMode tests (`Tests/EditMode/Audio/HandDrums/`, `Tests/EditMode/HandDrums/`)

1. **Tables:**
   - mode counts 170 / 194, groups 43 / 49, cavity group 42 / 48; `GClass`, `ShedOrder` (38 / 41) and `GlideList`
     (16 / 27) as exported;
   - the FNVs of 6.1;
   - each `avgTab` entry equals 3(sin x − x cos x)/x³ (x = κb) within 1e-6;
   - each dense mode's √ΔN within 1e-6 of the exported `nReal`;
   - the M1 ratios.
2. `Rng` mulberry32 from the seeds, and the draw order of 2.9 (`rngAfterOpen`).
3. **Contact probes** (6.1): F max ±0.1 %, contact samples exact (managed) / ±2 (Burst), for α 1.5 (tips) and 2.5.
4. **The scenario vectors** (6.1), at Share 1 and Share 0.4.
5. **DC and low-frequency push:** each stroke at u 1 has energy < 60 Hz ≤ −20 dB (measured ≤ −40.5) and \|mean\|/rms
   ≤ 0.01 over the hit (≤ 0.0011).
6. **Stroke character** (§2.11 S1–S5, H1–H3, run on the C# unit through `OfflineEngine.RenderGraph`): open tone dominance
   ≥ 10 dB, bass partial < 100 Hz, slap fundamental ±3 dB, slap S4 ≥ 8 dB and c50 ≥ 1.6×, > 4 kHz share ≥ −22 dB, crack
   peak-to-median mean ≤ 10.5 dB, pop ≤ −6 dB of the peak.
7. **Rests:** T1, T3; a lift while ringing gives `NullTest.MaxStep` ≤ the signal's own maximum step + 1 %.
8. **Glide:** G1 with the T mode soloed (go = 0 for the rest); a glide-only step touches only `GlideList` poles.
9. **Flams:** F1 table ±1 dB.
10. **Budget scaling** (`UnitShareTests`): the two-drum slap roll at Share 0.4: active groups ≤ 37, `Work` ≤ 50 % of
    Share 1 (lab 43.6 %), click score outside onsets < 6; core groups never shed.
11. **Allocation:** `Is.Not.AllocatingGCMemory()` around the unit API calls, `HandDrumsDsp.Render` and
    `HandDrumsInput.Step`.
12. **Determinism:** two renders bit-identical.
13. **Loudness parity** (`LoudnessParityTests`): the tone phrase within ±1 LU of the piano reference (lab 0.00); the open
    phrases within ±2 LU (lab +0.33 / +0.66).
14. **Lesson pack:** schema load, ids prefixed, zones valid, the checks of 5.1.
15. **Guide data:** every tip line present (manifest, WAV, text), ≤ 12 words (ours ≤ 10), ≤ 4.0 s.
16. **Interaction unit tests on `HandDrumsInput`** (no scene): every case of 3.7 fed as joint arrays (transliterate `poseHand`,
    `strokeKeys`, `runCase` and the seeded noise source to C#; the expected event lists are in `interaction-cases.json`),
    at the four rate pairs; the seeded-noise cases with their seeds; `classify` on the recorded t* poses; the palm-lift
    law and trace.
17. **The sweep** (`sweep.json`): for each of its 336 hand poses, `HandDrumsInput` gives the same stroke kind and patch
    parts.
18. **Channel map:** drum 0 renders into channel 1 (`HandDrumHi`), drum 1 into channel 0.

### 6.3 PlayMode tests and on-device checks

PlayMode (`Tests/PlayMode/HandDrums/`, `FakeHandSource` at 72 and 90 Hz with its tracker at 60 and 72/90 Hz,
`TestBoot`, the drums brought alone):
- the 18 cases through the real module, asserting `AudioTestTap` unit calls and the recorder events;
- C08b and C09b with `FakeHandSource` noise (σ 2 + 2 mm): no `handDrum` events;
- the `Bring` → glide → first tip flow;
- a lesson from countdown to done with scripted strokes;
- tracking loss during a rest (the lift after 150 ms);
- `SetPalmLift` called with the reference trace for C02;
- MR with `MeshLivingRoom2.json` (the pair on the floor, 3 draws, shells cast shadows; no palm lift);
- draws ≤ 6 (`DrawBudgetTests`).

**On the headset** (dev build):

| Check | Procedure | Pass |
|---|---|---|
| T-HD-CPU | 60 s two-hand roll on both drums at the fastest you can, then the adversarial bench schedule from a debug action; `DspLoadMeter` and a `[PERF] HandDrums` line from the unit's own Stopwatch | unit average ≤ 0.15 ms, worst ≤ 0.25 ms per block; 0 dropped commands, 0 stolen patches; at a forced Share 0.4 the worst block halves |
| T-HD-LAT | 240 fps phone video of a palm bass and a fingertip slap, audio from the headset speaker | report contact frame → onset against burst.md 12 (expected 63–71 ms; the 50 ms velocity window adds no latency: the stroke fires at the crossing) |
| T-HD-RATE | log `sampleInterval` while drumming with Fast Motion Mode on and off | FMM ≈ 16.7 ms; if LOW (≈ 33 ms) is ever used, C06/C07 degrade (3.7) |
| T-HD-CLASS | a tester plays 20 of each stroke (open, bass, slap, mute, heel, toe), seated, then 20 more with fast alternation; the module logs each classification | ≥ 90 % right per stroke at a natural speed, ≥ 80 % in fast alternation; no doubles; slaps at full speed are slaps |
| T-HD-JITTER | rest both hands flat on both heads for 60 s in dim light (FastMotion blocked) and in normal light; then hover them 1 cm and 2 cm above for 60 s; then lay them down slowly 10 times | 0 strokes; damping on the whole time while resting |
| T-HD-REACH | 5 seated adults, chair 0.45 m: play the far edge, centre and near edge of both heads; **record through the lens** (Meta casting) during 30 s of hard bass strokes | everyone reaches the centre without leaning; nobody touches a real knee or thigh; **in the video the drawn palm stays on the skin** (no visible sinking) |
| T-HD-MR | MR in a real room: the pair on the floor, in front of a real table and a sofa | no visual intersection with furniture volumes; the real hands read as on the skin (contact shadows) |
| T-HD-LISTEN | in the headset, seated: the tumbao and the conga line from the lesson player, dry and with the room | the README's checklist; bass not boomy, slap a crack (not a click, not a ring), the open tone a clear pitch |

---------------------------------------------------------------------------------------------------------------------------

## 7. Unity integration (inside the platform plan)

**Owned folders** (platform 13.2):
- `Assets/MusicRoom/HandDrums/**`: `HandDrumsModule`, `HandDrumsDefinition`, `HandDrumsInput`, `HandDrumsBuilder`,
  `HandDrumsMotion` (head dents, 4.5), `HandDrumsLessonTargets`, `HandDrumsPlayback`, `HandDrumsGuideAnchors`,
  `Resources`, README, `CONTRACT-REQUESTS.md`;
- `Assets/MusicRoom/Audio/Sound/HandDrums/**`;
- the tests.

**Descriptor** (`HandDrumsDefinition`):

| Field | Value |
|---|---|
| Kind, Rig, Id, Name, AloneName, Family | 6, 6, `handdrums`, "Hand drums", "Hand drums alone", "Percussion" |
| Carousel, ModuleOrder, TickOrder | 6, 34, 22 |
| Traits | Playable \| Pitched \| FastMotion \| HasStand |
| Tools | None |
| Seat | §4.3 |
| Stow | `window-far` |
| Audio | Unit 6; outputs (HandDrumLo, HandDrumHi); CapAvgMs 0.15, CapWorstMs 0.25; ArenaMiB 0.17; BakedMiB 0; ErOrigin (0, 0.40, 0.10) |
| Lessons | §5.1 |
| Mr | OnTable false |
| Events | `handDrum` (Onset, Kind), `handDrumMute` (State, Kind) |
| Tips | §5.2 |
| Features | §5.2 |
| Draws at seat / stowed | 3 / 3 |
| Tris at seat / stowed | 9 k / 4 k |

**Contract requests** (to the platform owner):
1. `zone` values for `handDrum`: add "closed", "heel" and "toe" (3.5). The drums run without it by mapping them to
   "mute", "bass" and "tip".
2. The tail rule (platform 10.3): T4 replaces the top-20 share for `Pitched` skins (2.11).
3. **`MetaHandVisual.SetPalmLift(HandSide side, float liftMetres)` — required (v2).** Raises the drawn hand mesh (all
   joints) along the given surface normal by `liftMetres`, 0…0.06, applied in the visual's LateUpdate after E1's finger
   clamp; the module computes the value (4.5: up to 60 mm, fading to 0 at 90 mm, attack instant, release τ 25 ms) and
   calls it every frame; VR only (MR shows real hands). v1 had it optional at ≤ 25 mm, which hides at most a third of a
   hard bass's 2–8 cm overshoot. Exact code goes to `CONTRACT-REQUESTS.md`.
4. The seat numbers of §4.3 replace the provisional `registry.mjs` entry (`seatcheck.json`: same fit counts).

---------------------------------------------------------------------------------------------------------------------------

## 8. Renders (`Builds/Listen/handdrums/`, 48 kHz stereo 24-bit, 10 files, 50.8 MB)

**The mix (v2, critique M2).**
- Direct path: the room lab's head model (distance law, Woodworth ITD, Brown–Duda shadow), conga at (−0.185, 0.75,
  0.57) and tumba at (0.19, 0.755, 0.57);
- `-room` and `-app` files add our room unit ('natural', drums group, the group point at the spec's ErOrigin);
- every file then goes through the app's MasterBusV2 **at the app's own input gain (0 dB)**, then **one linear gain** to
  −18 LUFS integrated or to −1.2 dBTP, whichever is lower. Nothing else limits or levels.
- The two **`-app` files** are the app's output itself (room on), 0.25 dB down so their true peak is below −1 dBTP (the
  master's ceiling is exactly −1.0).

| File | s | LUFS | dBTP | Set by | Gain after the master | The master at app level: limiter max GR / leveler max GR | Unit peak dBFS | Dry PLR |
|---|---|---|---|---|---|---|---|---|
| `handdrums-ladder-open` (u .15….95, conga then tumba, one gain) | 10.1 | −18.3 | −1.2 | true peak | +7.7 | 0 / −0.5 | −0.9 | 16.9 |
| `handdrums-ladder-bass` | 10.1 | −23.0 | −1.2 | true peak | +3.6 | 0 / −1.3 | 3.3 | 21.4 |
| `handdrums-ladder-slap` | 10.1 | −26.9 | −1.2 | true peak | 0.0 | 0 / −0.4 | 6.8 | 25.3 |
| `handdrums-techniques` (open, mute, slap, closed slap, bass, heel, toe, touch on each drum; flam; a palm quieting a ringing drum; a two-hand roll) | 19.6 | −22.3 | −1.2 | true peak | +2.4 | 0 / −0.5 | 4.4 | 20.9 |
| `handdrums-tumbao` (traditional, 92 bpm, 8 bars, two drums; heel-toe rests held) | 22.9 | −23.4 | −1.2 | true peak | +1.9 | 0 / −1.2 | 4.9 | 22.0 |
| `handdrums-tumbao-room` | 22.9 | −23.1 | −1.2 | true peak | +1.9 | 0 / −1.7 | 4.9 | 22.0 |
| `handdrums-tumbao-app` (the app's output) | 22.9 | −25.2 | −3.3 | app level | −0.25 | 0 / −1.7 | 4.9 | 22.0 |
| `handdrums-congaline` (conga-line rhythm, a 24-stroke roll, "shave and a haircut" 1899 ending) | 19.3 | −23.3 | −1.2 | true peak | −0.2 | −2.6 (0.08 % of the time) / −2.5 | 6.3 | 23.9 |
| `handdrums-congaline-room` | 19.3 | −22.8 | −1.2 | true peak | −0.2 | −2.6 / −2.6 | 6.3 | 23.9 |
| `handdrums-congaline-app` (the app's output) | 19.3 | −22.9 | −1.25 | app level | −0.25 | −2.6 / −2.6 | 6.3 | 23.9 |

Every normalised file is set by its true peak before it reaches −18 LUFS: hand drums are peaky (PLR 17–25 dB), and v2 no
longer squashes the peaks to win loudness (v1: the limiter took 6 dB on 7 of 8 files). The slap ladder, the peakiest, is
the quietest. The app itself plays the drums at −23 to −27 LUFS through this master. The critic's `c1-files.mjs`: all 10
files below −1 dBTP, no clipping, DC ≤ 4e-5. Click scores outside onsets are 0.01–1.7 (white noise scores about 8). The
README tells a non-musician what to listen for and why the levels differ, without the jargon.

**Public domain.**
- The tumbao and the conga-line (comparsa) rhythm are traditional; rhythms carry no copyright.
- "Shave and a Haircut" is the 1899 tag from Charles Hale's "At a Darktown Cakewalk".
- The ladders and the technique tour are not music.
- No melody, no lyrics.

---------------------------------------------------------------------------------------------------------------------------

## 9. Sources (numbers used)

- **[R00]** T. D. Rossing, *Science of Percussion Instruments*, World Scientific (2000), the conga section and its
  Fig. 4.11 (spectra of conga strokes).
  - **Read only as quoted and described in a player forum** (congaplace.com, "Tuning and relationship to song key",
    posts of 13 Jul 2011):
    - the open tone: one peak near 240 Hz, "many decibels louder than all the other frequencies";
    - the slap: "the same fundamental ((0,1) mode) peak at around 240 Hz … nearly as loud";
    - the bass: a 70 Hz fundamental with "the harmonic at 280 Hz almost as loud".
  - The book is out of print, and its preview could not be reached (Books API quota). **UNVERIFIED at the source;**
    the checks S1–S3 rest on this reading.
- **[R92]** T. D. Rossing, "Acoustics of drums", *Physics Today* 45(3), 40–47 (1992), as used in drums.md 14:
  - ideal membrane ratios 1, 1.594, 2.136, 2.296, 2.653, 2.917, 3.156, 3.501, 3.600;
  - air loading of the mode families;
  - ΔT ∝ amplitude², f ∝ √(T + ΔT).
  - The scan has no text layer here, so the numbers come from drums.md, which read it in full.
- **[HW14]** D. Harvey, T. C. Whiteley, "Sound Quality and Striking Position of a Conga Drum", *Journal of Science,
  International School Bangkok* 8(1), 2 (2014) (DOAJ 16110cdf41c247c4ae7d445440086044). From the abstract: "sound of a
  higher pitch is produced at the rim … due to higher modes of vibration becoming dominant". The full text link returned
  404. A student journal.
- **[DJ]** "Acoustic and modal analysis of an African djembe drum" (ResearchGate record 260571485, abstract only):
  - a Helmholtz bass near 75 Hz;
  - shell bell modes near the membrane modes;
  - shell, cavity and membrane coupling.
- **[DJW]** DjembeWeaver, "In search of the perfect slap" (blog, Jan 2014): an informal spectrum. The tone sits near
  400 Hz, and the slap's energy is in the higher partials (M3, M4) rather than M1. **Informal.**
- **[WIKI]** Wikipedia, "Conga":
  - head sizes: tumba 12–12.5", conga 11.5–12", quinto ~11";
  - height ~75 cm;
  - the stroke names (open, muffled, bass, slap, touch, heel-toe/manoteo);
  - pair tunings a perfect fourth apart.

  Wikipedia, "Tumbao": the marcha table H T S T H T O O.
- **[PEL]** L. Peltola, C. Erkut, P. R. Cook, V. Välimäki, "Synthesis of hand clapping sounds", *IEEE Trans. Audio,
  Speech, Lang. Process.* 15(3), 1021–1029 (2007): a clap is a short excitation through the resonance of the cavity
  between the hands. The concept only; the cavity frequencies here (1.65–2.6 kHz) are our estimate.
- **[W06]** A. Wagner, *Analysis of drumbeats*, MSc thesis, KTH (2006), and **[D11]** S. Dahl, *Acoust. Sci. & Tech.*
  32(5) 168–173 (2011), as used in drums.md: stick contact 3–6 ms (5–8 ms mf), shorter at forte. These sticks were the
  scale and the trend for the hand contacts.
- **[CH]** L. Cremer, M. Heckl, *Structure-Borne Sound*: radiation below coincidence (subsonic modes radiate from
  their edges).
- **[HC]** K. H. Hunt, F. R. E. Crossley, *J. Appl. Mech.* 42 (1975): the dissipative contact law. Soft-tissue
  stiffening (an exponential-like stress–strain curve) follows Y. C. Fung, *Biomechanics* (concept; the exponent 2.5
  is ours).
- **Player report** (congaplace, same thread): "with a C fundamental, G, E and F as the dominant overtones". Anecdotal;
  it agrees with 2.2.
- ITU-R BS.1770-4 (K-weighting, true peak); IEC 61672 (A-weighting); ISO 226 (equal loudness), for the balance
  reporting.
- Local: `Docs/10-upgrade/{ENGINE-API,burst,drums,room}.md`, `02-meta-hands.md`,
  `11-first-five/{sunny-tour.md,tutorials-audit.md}`, the lesson set (`lessons.json`), `12-new-instruments/platform.md` and
  `Tools/SoundLab/platform/*.mjs` (read-only).

- **[AB24]** S. Abdlkarim et al., *Behavior Research Methods* 56, 1052 (2024), as cited by the v1 critique (not re-read
  here): Quest fingertip position error about 1.1 cm, delay about 45 ms, MCP angle error 11.5–16.7°, self-occlusion as an
  error source. Used for the noise
  levels of the seeded cases (2 mm per joint + 2 mm whole hand per sample is the critique's test level; 4 mm is reported).
- **[TH]** Thomann, "Congas: first steps" (online guide), as cited by the v1 critique (not re-read here): the slap is "the loudest note of
  the conga", "bright, cutting", "penetrating"; the bass "a low, muffled sound". Informal.
- **[UBC]** UBC wiki, "Djembe", as cited by the v1 critique (not re-read here): slap centroid 700–1000 Hz against a tone at 300–420 Hz
  (about 2.4×). A djembe, not a conga; weak.
- **[WEYL]** H. Weyl's asymptotic law for membrane eigenvalues: a fixed-edge membrane of area A has
  N(k) ≈ A k²/(4π) − L k/(4π) modes below wavenumber k (L the perimeter); for a disc of radius a, N(j) ≈ j²/4 − j/2 with
  j = ka. Used (leading term) for the dense field's per-slot count (2.2).
- **[HZ]** H. Hertz (1882) / K. L. Johnson, *Contact Mechanics* (1985): the elliptical pressure under an elastic contact,
  p ∝ √(1 − r²/b²), and its Hankel transform (the patch average of 2.3).


**Tuned here [T]**, each against a check in 2.11: the flesh constants (v2: the palm's mass and stiffness, the fingertips'
Hertz stiffness), the coupled-mode masses and radiation, the T60 laws, the dense field's level and decays, the cavity
levels and Q, POP_K, Y2_REL, the stroke-logic thresholds of 3.2 (against the seeded-noise cases), and the level and trim.
---------------------------------------------------------------------------------------------------------------------------

## 10. Risks, open points, UNVERIFIED

1. **Not heard by anyone.** The renders and the README are the gate. An earlier sound upgrade was judged worse.
   v2's slap is a different sound from v1's (a crack instead of a click on a tone): it may be heard as better or as
   "papery". The README asks.
2. **Few published conga measurements exist.** S1–S3 rest on a forum reading of one figure (R00), S4/S5 on an informal
   djembe analysis, S6 on a student abstract, H1–H3 on the critique's targets and decaying-noise statistics. No published
   conga T60s, contact times or stroke levels were found. The design values (open-tone T60 0.85 / 1.0 s, bass 0.30 /
   0.34 s, balance targets, the dense field's level and decays, the fingertip's Hertz stiffness) are estimates. A
   10-minute recording session of a real pair would settle them, with one microphone at the player's head and the
   strokes of 2.10.
3. **The slap's brightness hardly changes with force** (2.11, D). The Hertz fingertip makes soft slaps crisp too. If soft
   slaps sound too sharp, lower the tips' k (a trial at 6e6 gave S4 about 7.5 / 8.5 dB: the conga then misses S4), or let
   `Vmin` drop (0.4 → 0.3 m/s).
   A stiffer flesh law that gets brighter with force rang a dense mode (rejected).
4. **Bass balance.** The bass is now −4.8 / −4.0 dB A and −1.2 / −0.8 dB K re the open tone. If it is too thin in the
   headset, raise rad.B back toward 1.55 / 2.0 (S2 stays inside its rule; parity is on the tone strokes, so the tone level
   does not move). If it booms, lower the palm's speed cap (1.3 → 1.15).
5. **Classification on device** (T-HD-CLASS) remains the main interaction risk, now tested against seeded noise rather
   than only clean scripts. The noise model is i.i.d. per sample (the critique's); real tracking error is partly slow
   wander, which the 2-sample arming mean and the 100 ms approach window handle better than white noise, and partly
   orientation error of the back axis (about 1 mm at the skin points), which is not modelled.
6. **Tracking latency** (≈ 30–45 ms, burst.md 12, AB24) makes fast rolls feel late, the same as for the kit. Fast Motion
   Mode helps; at the LOW rate the fastest roll loses strokes (3.7). Predictive strikes are left for v1.1.
7. **Peaky files.** Normalised by true peak, most files sit at −22 to −27 LUFS (the slap ladder lowest). They are honest
   but quieter than −18 in a playlist.
8. **The platform's tail rule** does not fit pitched skins (2.11): contract request 2.
9. **MR furniture.** The pair stands on the real floor in front of the player. Whether Layout's MR placement avoids
   real furniture volumes is a platform-wide open point.
10. **CPU.** The ratios are assumed (burst.md 10.2; drums.md 9), and v2 costs about 2× v1 because of the dense field. The
    inhuman adversarial schedule's timed worst block can cross 250 µs on a loaded machine; budget scaling halves the work
    under load. T-HD-CPU replaces the estimates. If the device is tight, the dense field can drop to 96 slots (the crack's
    overlap falls with it; re-run H2).
11. **Internal headroom.** After the parity trim (+7.86 dB), a hard conga slap peaks at +6.8 dBFS inside the float graph.
    Nothing after the unit is fixed-point in Unity's path as specified (spec 04), but if the device's spatializer path
    shows any clipping (T-HD-LISTEN, a hard slap roll), set the `HandDrumLo/Hi` source gain −6 dB and raise the room send
    to match; parity then moves to the source table, not the unit.
12. **The representative drum.** The dense field's random draws were chosen as the median of 24 realizations (2.2).
    A different seed can make the crack 1–2 dB brighter or darker; the C# unit must load the exported table, not re-draw.
13. **V1 is a noisy statistic** (2.9): it passes on the reference seed at its limit (0.985). It does not measure how
    alike two strokes sound to a listener; T-HD-LISTEN does.
