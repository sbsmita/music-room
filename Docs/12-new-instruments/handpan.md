# 12-new-instruments · Handpan: a physically modelled handpan for bare hands (implementation-ready spec for Unity C# + Burst)

Key `handpan`. Lab (Node 26, no packages): the handpan lab (Node prototype; its vectors are frozen in
`Tests/EditMode/Handpan/Reference~/` and `Tests/EditMode/Audio/Handpan/Reference~/`). Renders:
`Builds/Listen/handpan/handpan-*.wav`, guide `handpan-README.txt`.

**Status: revision 2** (2026-11-01, after the independent critique; section R). Every number below was measured with
the lab scripts or is marked UNVERIFIED (a device-only fact, with its test). Nobody has listened to the renders yet. The
user decides by ear, including the choice between the two voices "calm" and "measured" (sections 3.6, 9.4).

| Script (handpan lab) | What it does | Output |
|---|---|---|
| `handpan-layout.mjs` | Shell, tone fields, scale, contact geometry, tak neighbours, seat placement (shared by sound, hands and visuals) | |
| `handpan-engine.mjs` | The sound unit: design tables for both voices (main thread) and the block renderer (audio thread) on the burst lab's MB4 kernels | |
| `calibrate.mjs` | Partial levels at the dimple, strike-position changes, loudness of the reference stroke | `out/calibration.json` |
| `verify.mjs` | Objective checks against the published measurements, artefacts, determinism | `out/verify.json` |
| `sound-gates.mjs` | **New.** The revision-2 sound gates (ping, top partials, tak, AM), with the critic's methods; `node sound-gates.mjs calm` / `measured` | `out/sound-gates-*.json` |
| `bench.mjs` | CPU per 256-frame block, lane-samples, Quest estimate, memory | `out/bench.json` |
| `render.mjs` | The 8 listening renders (dry, the A/B, and through the room lab's room + master) | `Builds/Listen/handpan/handpan-*.wav`, `out/render-report.json` |
| `test-vectors.mjs` | DSP test vectors, the full lane table per voice, raw float32 renders | `out/test-vectors.json`, `out/handpan-tables*.json`, `out/vec-*.f32` |
| `gen-cs.mjs` | Both voices' lane tables as C# | `out/HandpanTables.g.cs` |
| `handpan-interaction.mjs`, `interaction-tests.mjs` | Bare-hand play logic and 34 scripted-hand cases | `out/interaction-vectors.json` |
| `hands-acceptance.mjs` | **New.** Monte-Carlo acceptance under tracker jitter (the critic's scenarios and pass criteria) | `out/hands-acceptance.json` |
| `geometry.mjs` | Placement, reach, field sizes and gaps for the seated player | `out/geometry.json` |
| `lessons.mjs` | Three lessons in the `lessons.json` lesson schema, and the lesson-block paths | `out/handpan-lessons.json` |

The critic's scripts are in `critic/` and import the lab read-only; they were re-run against revision 2. Their
revision-1 outputs are kept in `critic/out/before-revision2/`.

---------------------------------------------------------------------------------------------------------------------------

## R. Revision 2: what the critique found and what changed

The critique's verdict was "ready with fixes": 4 high and 4 medium issues were visible to me. My copy of the critique
ended inside the 8th issue (the `sampleDt` one); anything after it was not visible and is not addressed here.

| # | Issue (severity) | Change | Before → after (script) |
|---|---|---|---|
| H1 | Lesson blocks start inside or in front of the body (high) | The harp's line-of-sight rule plus a forward clamp (8.1); EditMode test | A3 / C4 starts at z −0.027 m (behind the eyes' plane), ding 0.235 m from the eyes → every start z ≥ 0.16 m, ≥ 0.26 m from the eyes (`lessons.mjs`) |
| H2 | A tap left on the steel mutes its own note after 0.10 s (high) | A finger that sounded the note mutes it only after 0.40 s still, and only between −8 and +6 mm; Sunny tip `tip_handpan_lift` | Press of 0.08–0.4 s at the stroke bottom muted 100 % → 0 % for any press ≤ 0.3 s; a press 12 mm deep never mutes; a still shallow press mutes after 0.40 s by design (`hands-acceptance.mjs` 3, `critic/c5` 3) |
| H3 | Play logic breaks under tracker jitter (high) | Speeds are least-squares slopes over the distinct samples of the last 45 ms (loudness) and 70 ms (decision); V0 0.08 → 0.12 m/s; 2 falling samples; arming on 2 samples above 10 mm; rest = stays within 4 mm of where it settled; 9 jitter cases in the vectors | At 1 mm jitter: velocity SD 0.095 / 0.086 → 0.044 / 0.031; spurious strikes in slow placements 81 → 0.33 per 100; mute engaged 4 → 100 %; hovering ≥ 6 mm: 0 false strikes (also at 2 mm) (`hands-acceptance.mjs` 1, 2, 4) |
| H4 | Inharmonic "ping" on the ding, louder than its fifth (high) | Upper modes all above 7 f0 (4.62 / 5.35 / 6.10 removed); upper level −15 → −21 dB, the ding's −14 dB more; ding cavity drive −3 dB; two new gates | Ding 4.62 f0 at −7.8 dB re f0 (30 ms), above the fifth on 8 of 12 critic strokes → strongest inharmonic partial ≤ −21.5 dB re f0 and ≥ 7.1 dB under the fifth on 135 of 135 strokes; top 3 partials at 10 ms harmonic on 135 of 135 (`sound-gates.mjs` 1, 2; `critic/c7`, `c3` D') |
| M1 | AM wrong in kind; spec 3.6 claim false (medium) | Section 3.6 corrected with the critic's measurements; a second voice, "measured", for an A/B by ear | Calm: 1 of 54 partials beat ≥ 1 dB at ≥ 2 Hz (unchanged). Measured: 16 of 54; the octave pairs 1.25–2.66 dB at 2.35–5.95 Hz (`sound-gates.mjs` 4) |
| M2 | The tak is a dry pitched "tonk"; no field or cavity answers (medium) | The tak also pulses its two nearest fields through their boundary and pushes the cavity; shell 6 → 8 groups (32 modes), every shell T60 ≤ 0.12 s | Neighbours' f0 ≤ −97 dBFS → −26.7 … −34.9 dB re the tak's peak; cavity −113 dBFS → −28 … −31 dB re peak; shell down 40 dB at 120 → 80 ms; bins within 15 dB of max 0.11 → 0.16 (`sound-gates.mjs` 3, `critic/c3` E') |
| M3 | Palms-down fingertip taps delayed 11–33 ms by the flat-hand deferral (medium) | A hand defers its fingertip strikes only while it is landing flat: palm within 2 cm and approaching, or ≥ 3 fingertips within 1.5 cm | 11 / 33 / 22 ms → 0 ms; all 9 trailing-palm slaps still one slap (`critic/c10`, `hands-acceptance.mjs` 5, 6) |
| M4 | `sampleDt` ambiguous; the wrong reading makes taps 3× too soft (medium) | No sample-interval input at all: speeds come from distinct pose samples and the frame clock (6.2) | The 30 Hz and the 90 Hz poses give the same strike; constant-speed strokes read identically (`hands-acceptance.mjs` 8) |

**Where I changed the critic's suggestion, and why (evidence in the scripts named):**
- **H1.** The harp rule alone fails the critic's own test: the near fields' line of sight runs back toward the chest, so
  A3 and C4 would start at z 0.10 m and D4 / E4 at 0.14 m (`lessons.mjs` column `harpRuleOnlyStartZ`). A forward clamp at
  0.16 m and an eye gap of 0.26 m (not 0.25) give the test a 1 cm margin.
- **H3.** A 3-sample slope with V0 0.12 still gave a velocity SD of 0.054 for 0.3 m/s taps and 1.3 spurious strikes per
  100 placements at 1 mm. The two windows (45 ms loudness, 70 ms decision) give 0.044 and 0.33 (sweep in the lab). Cost:
  a fast 1 m/s minimum-jerk tap now reads 0.84 instead of 0.92.
- **H3.** A rest anchor restarts only after two consecutive samples leave the 4 mm sphere (one jittery sample is not a
  move), and a lone teleport sample is held back rather than resetting the history. Otherwise two-sample arming loses
  the stroke after a glitch (case `teleport`) or after the hand arrives from far away (case `repeat-6hz`).
- **H3 (side effect found).** With the palm facing down, the palm passes 1.7 cm above the steel at the bottom of a deep
  tap, inside the 2 cm palm-rest band, so the tap muted itself. The palm-rest band is now 1 cm.
- **M1.** The critic's "measured" settings were tried first: fundamentals offset σ 4 cents, detunes σ 8 cents clamped to
  −35…0, coupling −20 / −15 dB. They failed the critic's acceptance: the ding octave swung 7.9 dB at 0.4 Hz and A3's
  octave 5.2 dB at 0.5 Hz. Cause: a field's exactly harmonic 2 f0 (the nonlinear pump) sits a few cents from its octave
  partner's fundamental. Also, the ding's octave detune drew −0.4 cents.
  - Shipped instead: fundamentals on equal temperament, octaves 14–26 cents flat and fifths 8–28 cents flat (inside
    Alon & Murphy's measured ratios). The octave pair sets the sympathetic coupling (−18 dB); the rest stays at −26 dB.
  - With these, every octave pair beats at 1.25–2.66 dB and 2.35–5.95 Hz.
- **M2.** The suggested edge projection (u, v at ρ 0.95) makes the neighbours ring their fifths: J0 is 0.06 there, but
  the fifth's shape is 0.46. Their fundamentals came out 52 dB under the tak's peak. Tak pulses now use a
  boundary-averaged shape (T1 0.55, T2 0.12, T3 0.10, as a palm averages the field) with the critic's techK raised from
  0.12 to 0.17. "Down 40 dB within 80 ms" is measured on the tak's own sound (shell + cavity). The neighbours' hum, at
  −27 to −35 dB, takes longer by design (the critic's whole-signal measure reads 230 ms).
- **M3.** A fixed 15 ms window would split slow slaps: a palm trailing 30 mm at 0.4 m/s lands 45 ms after the fingers
  (critic c5 case 5). The hold lasts while the hand is landing flat, at most 60 ms.
- **Also changed** (from the critic's `c4-geometry.mjs` sweep; the issue text was not in my copy): the rim centre moved
  from 0.68 to 0.72 m.
  - Before: the far fields' outer points were 0.613 m from the shoulder, past the 0.60 m reach, and the near sweet spot
    was 1.5 cm inside a 95th-percentile male thigh.
  - After: 0.599 m and +2.5 cm.
  - Cost: the ding top is 4 cm higher (0.83 m), and the far fields face the eyes at 55° instead of 52°.
- **Also corrected:** the soft-spring glide reaches 6 cents flat (u² is capped at 1.5), not 4 (the critic's c2 I measured
  −5.2 cents on an ff ding slap).

---------------------------------------------------------------------------------------------------------------------------

## 0. Decisions in one page

1. **Scale: D "Celtic minor" 9 = ding D3 + A3 C4 D4 E4 F4 G4 A4 C5** (section 1). All nine notes are white keys, so
   anything played on it fits the room's C-major piano and harp lessons and loops. It holds the whole C4 to A4 hexachord,
   so Ode to Joy and Twinkle Twinkle play note for note. It has one semitone (E4 F4), so random tapping sounds good.
2. **Sound: a live modal model on the engine's MB4 kernel,** 54 groups (216 lanes), as a new sound unit (role `Handpan`).
   - Per tone field: the three tuned modes 1 : 2 : 3, and nine weak, fast upper modes at 7.5–17.8 f0 for a faint attack
     sheen. There is also a nonlinear pump (the shell's 1:2 internal resonance) and sympathetic lanes coupled through
     the shell.
   - A Helmholtz cavity mode (91 Hz).
   - A 32-mode shell bank for the tak, which also rings the two nearest fields.
   - The drive is the derivative of a smooth finger or palm contact force, so the output has no DC and no low-frequency
     push. Strike position works through the mode shapes, and resting fingers and palms damp the modes under them.
   - Two voices, "calm" and "measured", share every lane. Only the tuning of the octaves and fifths and the octave-pair
     coupling differ, and the user picks one by ear (3.6).
3. **Calibrated to published measurements** (section 3):
   - Octave and fifth levels at the dimple: −2.41 / −14.34 dB (Alon 2015 Table 4.1 medians: −2.7 / −14.4).
   - Strike-position gains: +6.4 / +6.8 dB (Alon 4.3: +5 / +7).
   - Decay: a 3.4 s mean of the three main modes (Alon: 2.9–3.3 s).
   - The 1:2:3 tuning (Morrison & Rossing 2007), and the cavity at 91 Hz (Hang: 85 Hz).
   - **New:** the three strongest partials 10 ms after a stroke are harmonic on every field, strike position and velocity
     (Alon & Murphy 2015 Table 2), and no inharmonic partial comes within 21.5 dB of the fundamental at 30 ms.
4. **CPU:** Quest 3S core at CPU level 3:
   - one note ringing: 8.1 µs per block;
   - a lesson or groove: 18–20 µs;
   - 10 fingers ff on every field: 34.8 µs mean, 54 µs max;
   - idle: 0.
   That is at most 2.7 % of the graph's 2 ms target, on top of burst.md 10.3's 0.89 ms worst case. Both voices cost the
   same. State is 42 KiB at 256 frames.
5. **Hands** (section 6):
   - **Strikes:** a fingertip strikes when its pad crosses the steel. Speeds are least-squares slopes over the distinct
     pose samples of the last 45 ms (loudness, full at 1 m/s) and 70 ms (the decision, ≥ 0.12 m/s). They are timed by
     the frame clock, with no sample-interval input.
   - **Arming and debounce:** a pad arms after two samples above 10 mm. Debounce also works per hand per field (40 ms)
     and per field (3 ms).
   - **Strike position:** a fingertip at the dimple centre gives a pure tone; toward the dimple edge the octave or the
     fifth comes forward; at the field edge you get a tick. A flat-hand palm slap on the ding gives a bass boom, and a
     tap between fields is a tak.
   - **Mutes:** a finger placed without sounding mutes after 0.10 s still. A finger that sounded the note must stay still
     near the surface for 0.40 s, so press-and-hold no longer kills the note. A resting palm mutes.
   - **Tracking loss:** no new strike, and held damping is released after 0.3 s.
   - **Jitter:** at 1 mm of tracker jitter, loudness scatters by SD 0.03–0.04, and slow placements give 0.3 false strikes
     per 100.
6. **Placement:** on a low three-legged walnut stand in front of the knees.
   - The rim centre is at (0, 0.72, 0.38) in the seat frame, tilted 28° toward the player.
   - Every point of every field is within 0.60 m of the shoulder (field centres ≤ 0.563 m). The ding top is at 0.83 m.
   - The near sweet spot is 2.5 cm above a 95th-percentile male thigh.
   - The rig is a new `RigId.Handpan` in Single mode. All mode is an open question for the layout owner (section 11).
7. **Visuals:** a dark nitrided-steel shell with hammered fields, a rubber edge band and a walnut stand, in 3 draws
   (4 with the VR floor shadow). Feedback is physical only:
   - fingers stop on the steel (E1), with contact shadows (E3);
   - the instrument settles by under a millimetre on its felt ring;
   - the reflections on a ringing field shimmer, physically bounded.
   Nothing glows.
8. **Lessons:** "First notes" (an 8-tap win in 12 s, a 21 s worst case), "Ode to Joy" and "Twinkle Twinkle". Blocks come
   in along the line of sight and never start closer than 0.26 m to the eyes or 0.16 m to the eyes' plane. There are 9
   Sunny tips of 10 words or fewer.

---------------------------------------------------------------------------------------------------------------------------

## 1. The scale, and why

| Field i | 0 (ding) | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|---|
| Note (MIDI) | D3 (50) | A3 (57) | C4 (60) | D4 (62) | E4 (64) | F4 (65) | G4 (67) | A4 (69) | C5 (72) |
| f0 (Hz, A4 = 440) | 146.83 | 220.00 | 261.63 | 293.66 | 329.63 | 349.23 | 392.00 | 440.00 | 523.25 |
| Position (from above, player at the bottom) | centre | near left | near right | left | right | far left | far right | far, left of centre | far, right of centre |
| Azimuth (°, 0 = toward the player, 90 = right) | – | 337.5 | 22.5 | 292.5 | 67.5 | 247.5 | 112.5 | 202.5 | 157.5 |

- **It plays along.** The room's lessons, the piano labels, the C-major harp (22 white strings) and the looper's layers
  are all in C major or A minor. D Celtic minor uses only D E F G A C (D Dorian without the 6th), which is a subset of C
  major and of A natural minor. There are no accidentals, so nothing the player taps can clash with a loop built on the
  piano or the harp.
- **It holds the lesson melodies.** C4 to A4 is complete. Ode to Joy (C4 to G4) and Twinkle Twinkle (C4 to A4) play
  note for note, identical to the audited piano lessons (checked by `lessons.mjs`). A typical "Kurd" scale has B♭ and
  would not fit.
- **It is easy to sound good on.** There is one semitone (E4 F4). The ding (D) and A3 form the tonic fifth. The scale's
  coincident partials make "Marshall pairs" (ding octave = D4, A3 octave = A4, C4 octave = C5, ding fifth = A4, A3
  fifth = E4's octave), which ring each other sympathetically: the handpan's halo (section 2.6).
- **It is a real, common handpan scale.** The 9-note ding-plus-8 layout matches the instruments measured by Alon
  (2015) and the Hang (7–9 notes around a central note; Morrison & Rossing 2007). The ding sits a fifth below the lowest
  ring note. Alon's Instrument 3 (D3 A3 …) has the same fifth.
- **The zigzag layout.** The scale climbs left, right, left, right from the player toward the far side, as on most
  handpans. A stepwise melody therefore alternates hands. Ode to Joy has 0 same-side steps (section 8.1).

---------------------------------------------------------------------------------------------------------------------------

## 2. Sound model (implements `handpan-engine.mjs`)

### 2.1 Signal flow (per 256-frame block; events split it sample-accurately)

```
 main thread: HandpanModule -> IHandpanSound.Strike / Damp (GraphCommand on the unit's own ring; When honours AudioSchedule)
              a tak posts 3 strikes at one When: Strike(9) + Strike(n1, Tak, gain w1) + Strike(n2, Tak, gain w2)   (2.4)
 audio thread, UnitHooks.RenderHandpan(u):   split at each due event (At, Seq) and at each pending-hit start (2.4)
   per segment:
     for field i = 0..8:
       e_i[n]  = dF/dn of the field's contact pulse (two raised cosines; 2.3)                 (0 when no pulse)
       G0  [T1 T2 T3 U0]   driven by e_i (or free)  -> y0 = lane T1, tuned = (T1 + T2) + T3 -> out
       NL  [T2 T3 4f0 5f0] driven by d/dn(u^2 + u^3 + u^4 + u^5), u = y0 / uRef_i (2.5)       -> tuned += lanes -> out
       G1  [U1..U4], G2 [U5..U8]  driven by e_i (or free)                                     -> out
       bus += tuned ;  cav += cw_i * y0 ;  if shellAmp_i != 0: shell += sa_i * e_i, cav += cr_i * e_i
     tak pulses (strikes between the fields) -> takX ;  shell += takX ;  cav += 0.15 * takX
     shell bank (8 groups, 32 modes)   driven by shell                                        -> out
     cavity (1 lane, 91.35 Hz)         driven by cav                                          -> out
     for field j: SYM [T1 T2 T3 4f0] driven by bus - tuned_j  (one-way coupling, 2.6)         -> out
   block end: flush < 1e-20, energy, retire < -110 dBFS (unless in contact), field levels, damping and glide poles
 -> mono channel 0 -> SourceId.Handpan (Meta spatializer, reverb send, room unit tap)
```

Why it is stable: every lane is a passive damped resonator. The nonlinear and sympathetic drives are feed-forward
(nothing feeds back into the bus), so the network is a cascade of stable filters.
- A 70 s run with every field struck has no NaN or Inf, 0 subnormal states and 0 active groups at the end.
- The worst case (all 9 fields ff + a ding slap in 4 ms) peaks at +11.3 dBFS, which is finite and bounded (section 3.8).

### 2.2 Lanes and design table (`designHandpan(sr, seed, voice)`, main thread, double maths rounded once; or ship `HandpanTables.g.cs`)

Group `g = 5·i + q` for field i (q: 0 G0, 1 G1, 2 G2, 3 NL, 4 SYM); 45–52 shell; 53 cavity. Lane k = 4g + l. Every lane
is `mbSetMode(f, τ = T60/6.907755, go, gi)`: `r = exp(−1/(τ·sr))`, `pr = (float)(r cos w)`, `pi = (float)(r sin w)`.

| Lanes | Frequency | T60 | go (output) | gi (input) |
|---|---|---|---|---|
| T1 (0,1) fundamental | f0 | `5.5·(146.832/f0)^0.35` s (×1.08 ding): 5.94 (D3) … 3.53 s (C5) | `RAD[0]=1 · w_i` | 1 |
| T2 (1,1)a octave | `2 f0 · 2^(d2/1200)` | 0.78 × T1 | `0.85 · w_i` | `0.80 · (f0/220)^0.45` (cap 2.5) |
| T3 (1,1)b compound fifth | `3 f0 · 2^(d3/1200)` | 0.58 × T1 | `0.55 · w_i` | `0.35 · (f0/220)^1.2` (cap 2.5) |
| U0..U8 upper | `f0 · {7.50, 8.16, 9.40, 10.6, 11.9, 13.2, 14.6, 16.1, 17.8} · (1 ± 0.02)` | `clamp(0.32·(800/f)^0.45, 0.05, 0.38)` s | `0.6 · w_i` | `10^((−21 − 1.4k + (ding ? −14 : 0))/20)` |
| NL lanes T2', T3', 4f0, 5f0 | T2, T3, 4 f0, 5 f0 | T2, T3, 0.55·T3, 0.42·T3 | as T2, T3, U | simulated so that the forced partial peaks at −14 / −22 / −28 / −34 dB re the fundamental at u = 1 (`nlGi`) |
| SYM lanes T1 T2 T3 4f0 | as the field's own | as own | as own | simulated so that the chosen partner partial within 2 % gives −26 dB (octave "Marshall pairs": −21 dB calm, −18 dB measured) (`symGi`) |
| shell 32 modes | log-spaced 520 Hz…6.5 kHz with jitter | `clamp(0.16·(1000/f)^0.55, 0.025, 0.12)` s | `0.22·(1000/f)^0.35 · ±1 · OUT_GAIN` | 1 |
| cavity | `c/2π · √(S/(V·L_eff))` = 91.35 Hz (V 25.7 L, gu Ø 9 cm, neck 1.2 cm + 1.7a) | 0.38 s | `OUT_GAIN` | 1 |

- `w_i = OUT_GAIN · 10^((−0.32·(midi − 57) + (ding ? 5.0 : 0))/20)`, with `OUT_GAIN = 0.063`. That is a −0.32 dB per
  semitone voicing tilt, and the ding 5 dB up so it carries as the bass note.
- **Tuning of the octave and fifth, per voice** (seeded; seed 2026; the draws below come from `mulberry32(seed·131 +
  7919 i)`, and the measured voice draws from its own stream `+ 4099`, so both voices share every other table):
  - *calm*: Gaussian σ 3.5 cents, |d| clamped to [2, 7], random sign; fundamentals on equal temperament.
  - *measured*: octave `clamp(−18 + 4g, −26, −14)`, fifth `clamp(−15 + 6g, −28, −8)` cents; fundamentals on equal
    temperament. That gives octave ratios 1.976–1.984 and fifth ratios 2.957–2.986, inside Alon & Murphy's measured
    1.96–1.99 and 2.94–2.98.
  - Drawn: D3 −14.0 / −23.4, A3 −20.4 / −8.0, C4 −19.7 / −20.5, D4 −16.8 / −13.0, E4 −14.0 / −11.5, F4 −19.1 / −24.8,
    G4 −14.0 / −10.5, A4 −17.8 / −14.0, C5 −16.6 / −9.5 cents.
- **Why `go` has no f/f0 factor:** a lane driven by the force derivative and read through `Im z` already gives the
  modal acceleration (≈ radiated pressure of a small source). A first draft also weighted `go` by f/f0, which counted
  frequency twice: octaves read +5 dB and fifths −5 dB re f0 on D3 (calibration log, section 3.2).
- **Why NL and SYM gains are simulated, not closed-form:** a resonator driven by a decaying, slightly detuned partial
  never reaches steady state. The closed-form resonance gain overestimated the response by 15–25 dB (first verify run:
  the 4f0 lane at −50 dB instead of −25). `lanePeak` simulates each lane in double for 2.5 s on the exact drive and
  scales `gi` to the target. This is deterministic, so ship the table.
- **Mode-frequency sources.**
  - The 1 : 2 : 3 tuning: Morrison & Rossing 2007, and Alon 2015 Table 4.1.
  - The upper modes start at the non-harmonic (2,1)a/b modes of the Hang's G3 note, 7.50 and 8.16 f0 (1418, 1543 Hz
    over 189 Hz; Morrison & Rossing 2007, Fig. 5). The others fill the region above them with a seeded ±2 % jitter, so
    that no sparse set rings like a bell. All are below 9.4 kHz.
  - Revision 1 also had fill modes at 4.62, 5.35 and 6.10 f0. Nothing measured sits there: on every field Alon & Murphy
    measured, the strongest partials after the onset are harmonic. On the ding they made a bell-like ping, so they were
    removed.

### 2.3 The contact pulse (main thread computes; audio thread runs a rotation)

```
vel in [0,1] -> V = 0.25 + 2.75 vel^1.3  (m/s impact speed)
Tc = TC · V^-0.3, TC = 1.10 ms (fingertip, tak) or 7.5 ms (palm)  -> fingertip 1.6 ms (pp) .. 0.79 ms (ff); palm 11 .. 5.4 ms
W1 = max(8, round(Tc·sr)), W2 = max(6, round(0.68·W1))
impulse I = m·V (m = 1 fingertip, 4.2 palm, 0.9 tak), A1 = 2·0.62·I/W1·48, A2 = 2·0.38·I/W2·48; then A1, A2 x gain (2.4)
F[n] = A1/2 (1 - cos 2πn/W1) [n < W1] + A2/2 (1 - cos 2πn/W2) [n < W2]   (n = 1, 2, ... after the hit's sample)
e[n] = F[n] - F[n-1]   (zero DC; F returns to exactly 0 at max(W1, W2), where the pulse ends)
```

- Two overlapping raised cosines (soft pad, then bone) avoid the deep spectral nulls of a single pulse. A pure half
  sine or Hann pulse made the fifth's level swing with velocity at specific notes.
- **Audio thread (per sample, float32, exactly this order):**
  1. `n++`;
  2. `(c, s) ← (c·C − s·S, s·C + c·S)` for both rotations;
  3. `F = n < W1 ? (0.5·A1)·(1 − c1) : 0`; `if (n < W2) F += (0.5·A2)·(1 − c2)`;
  4. `dst[k] += F − Fp`; `Fp = F`;
  5. `if (n ≥ max(W1, W2))` the pulse is off.
- Contact times: Wessel, Morrison & Rossing (2008) note that longer contact damps more and softer notes have fewer
  components. The 0.8–1.6 ms fingertip range and the V^−0.3 stiffening are design values (UNVERIFIED as a contact
  measurement; section 11).

### 2.4 Strikes: shapes, techniques, overlaps, the tak

At pulse start, the input gains of G0, G1 and G2 (12 lanes) are set to `gi = (float)((double)baseGi · shape · techK)`.
`techK` is 0.17 for a Tak-technique pulse (`TAK_FIELD_K`) and 1 otherwise. NL and SYM `gi` never change.

Shapes (field-normalised coordinates: u along the radial long axis, + toward the rim; v across, + clockwise from
above; both ÷ semi-axes; double maths, rounded once per value):

```
r2 = min(1, u² + v²)
T1:  j0p(r2) = 1 + r2(−1.446 + r2(0.5227 + r2(−0.0840 + r2·0.00227)))         J0(2.405 r), zero at the rim
T2:  ua = u − U_OFF (−0.18);  ra = ua² + v²;  ra < 1 ? ua·j1p(ra) : 0            the octave's nodal line, 0.18 inward
T3:  vb = v − V_OFF (0.12);   rb = u² + vb²;  rb < 1 ? vb·j1p(rb) : 0            the fifth's nodal line, 0.12 across
     j1p(r2) = (1.916 + r2(−3.517 + r2(2.152 + r2(−0.658 + r2(0.1208 − r2·0.0148)))))/0.5819   J1(3.832 r)/r, peak 1
Uk:  (0.35 + 0.65 r2)(1 − r2)·4 · (cos(m_k θ)·cosβ_k − sin(m_k θ)·sinβ_k), cos/sin(mθ) from powers of (u + jv)/√r2
     m = {0,1,2,1,2,3,0,2,3}; β_k seeded (tables UpperCosB/UpperSinB)
Slap: T1 0.55, T2 0.10, T3 0.08, Uk 0.15·cosβ_k (a palm averages the shapes over the field)
Tak:  T1 0.55, T2 0.12, T3 0.10, Uk 0.10·cosβ_k (a tak's neighbour pulse enters through the field's whole boundary)
```

- The nodal-line offsets model the asymmetric field (an ellipse on a curved shell, the dimple not at the centroid).
  This is why a strike at the dimple apex still gives an octave at −2.5 dB and a fifth at −14 dB, as Alon measured with
  a mechanical exciter at the apex ("D" sweet spot). Achong: the apex is the best single impact point for all modes.
- **Sweet spots** (Alon 2015 Fig. 2.15):
  - D = the dimple apex (0, 0);
  - O = the dimple edge on the long axis (0.35, 0) brings the octave up;
  - P = the dimple edge on the short axis (0, 0.35) brings the fifth up;
  - the field edge (ρ > 0.85) leaves the tuned modes weak, and the shell's tick dominates.
- **Shell and cavity weights per strike:**
  - `sa = 0.12 + 0.88·r2²` (edge strikes excite the shell more); 0 for a Tak-technique pulse.
  - `cr` = 1.6 for a palm slap on the ding, 0.56 (= 1.6·0.35) for a slap on a ring field, 0.225 for a fingertip on the
    ding, and 0 otherwise (including Tak pulses): the quasi-static volume push of a large-area hit.
  - Cavity drive from every field's fundamental: `cw` = 0.025 (ding; 0.035 in revision 1), 0.010 (ring fields).
  - A field pulse with `sa = 0` adds nothing to the shell or cavity and does not wake the shell groups.
- **The tak** (a fingertip or palm on the steel between the fields, map point X, Z). The main thread posts three
  strikes at the same `when`, in this order (`hpTak`; C# `HandpanModule.StrikeTak`):
  1. `Strike(9, vel, 0, 0, Tak)`: a shell pulse (one of 4 shell-only slots, which add up; with all 4 busy, slot 0 is
     overwritten). The pulses are summed into `takX`; then `shell += takX` and `cav = mad(0.15, takX, cav)` (`CAV_TAK`).
  2. `Strike(n1, vel, 0, 0, Tak, gain w1)` and 3. `Strike(n2, vel, 0, 0, Tak, gain w2)`: the two nearest fields
     (`takNeighbours`, below). Each is an ordinary field pulse with the Tak shapes and techK; `gain` scales A1 and A2 on
     the main thread (`(float)(A1·w)`).
  - `takNeighbours(X, Z)`: for each field, `d` = the distance from the point to the field's edge along the ray from the
    field centre (`|p − c|·(1 − 1/ρ)`, 0 when ρ ≤ 1). Keep the two smallest (ties: lower field index) with
    `w = exp(−d / 0.025)`. Double maths, no allocation (two locals).
  - Examples (TV12): A3|C4 gap (0, −0.19) → A3 w 0.599, C4 w 0.500; next to the ding (0, −0.095) → ding 0.607, A3 0.342.
- **Overlapping hits on one field.** While a field's pulse is in contact (≤ 1.6 ms for a fingertip), a new hit waits:
  it is *pending* and starts on the sample after the pulse ends. Two pending hits merge, and the louder wins.
  `segmentSplit` cuts the segment exactly there (TV5: the second hit starts at frame 1048 = 1000 + 48).

### 2.5 The nonlinear bloom (the shell's 1:2 internal resonance)

```
inv = (float)(1/uRef_i);  per sample: u = clamp(y0·inv, ±1.6); uu = u·u; p = uu·mad(u, mad(u, mad(u, 1, 1), 1), 1); x = p − prev; prev = p
```

- The NL group is driven by x whenever max(u²) in the segment > (3e-4)², and runs free otherwise.
- `uRef_i` = the fundamental's output for a vel-1 fingertip at the apex (computed from the pulse's DFT at f0).
- Physics: a curved note area has a quadratic restoring force. It moves energy from the fundamental into the octave
  and up the 1:2:4 cascade (steelpans: Monteil, Thomas & Touzé 2015; Achong 1996). Loud notes therefore grow
  harmonics and bloom, and soft notes stay "two components in an octave relation" (Wessel, Morrison & Rossing 2008).
  Measured in section 3.3.
- **Soft-spring glide:** the fundamental's pole is rotated by `dw = GlideW_i · min(u², 1.5)` at each block end:
  `c = (float)(1 − 0.5 dw²)`, `pr' = pr·c − pi·dw`, `pi' = pi·c + pr·dw`. GlideW is −4 cents at u = 1, so a hard hit
  starts up to 6 cents flat (u² ≤ 1.5; the critic measured −5.2 cents on an ff ding slap) and settles as it decays
  (steelpans start below pitch, Achong 1996 via Alon 2.1.4).

### 2.6 Sympathetic ringing through the shell

`bus[n] = Σ_i tuned_i[n]`, the radiated tuned and NL partials of every field. Field j's SYM lanes are driven by
`bus − tuned_j` (itself excluded) whenever any field rendered. The response is strong only where another field's
partial lies near one of j's modes, that is, in the scale's coincidences (calm voice):

| Field (lane) | Partner partial (cents off) |
|---|---|
| D4 f0 | ding octave (+2.0 c), Marshall pair |
| A4 f0 | A3 octave (−2.9 c), Marshall pair |
| C5 f0 | C4 octave, Marshall pair |
| D4 fifth (881 Hz) | A3 4f0 (0.0 c) |
| A3 fifth | E4 octave (−1.0 c) |

- **Choosing the partner.** Each lane is scaled to its strongest partner within 2 %. In the measured voice
  (`SYM_PREFER_MARSHALL`), a lane that has an octave partner is scaled to that one, so the pair's coupling is set by the
  pair that beats.
- Others: `out/handpan-tables*.json` `fields[].symPartner`.
- This is the measured behaviour of the Hang: driving the F3 ding at its octave (348 Hz) drives the F4 note strongly,
  and more amplitude couples more notes (Morrison & Rossing 2007 Fig. 7; Alon 2.1.5). It is also the steelpan's
  sympathetic pair (Achong 1996).
- Beating: section 3.6.

### 2.7 Damping (resting fingers and palms)

- Each field keeps a damping rate per mode (12: T1 T2 T3 U0..U8) from 4 slots: 0 left fingers, 1 right fingers,
  2 left palm, 3 right palm. Slot sources:
  - **Finger** at (u, v): `d_m = 55 · strength · (0.03 + 0.97 · min(1, |shape_m|/norm_m)²)` s⁻¹ (norm 1 for T1..T3,
    1.3 for U). A finger on a mode's nodal line leaves that mode ringing.
  - **Palm:** `d_m = 140 · cover` for all modes (T60 ≈ 0.05 s).
  - **Shell touch** (field 9, the global slot): `1.2 · strength` on every field, posted while a palm rests anywhere.
- At each block end, per field and mode: target = Σ slots + global; `dCur += (target − dCur)·0.5` (snap within 0.05;
  scalar maths in double, stored as float).
- If anything changed, every lane of the field (G0, G1, G2, NL, SYM: they are the same physical modes) gets its pole
  `p = P0 · (float)(1 − dCur/sr)`. The f0 lane of G0 also gets the glide rotation.
- Lane-to-mode map (`LaneMode`): G0 [0 1 2 3], G1 [4..7], G2 [8..11], NL [1 2 1 2], SYM [0 1 2 1].
- A pole change mid-note is click-free: the state is continuous. Measured step ratio 0.01 at a palm damp, the same as
  without one (section 3.7).

### 2.8 Budget hook, culling, Reset, output

- **Budget.** Read `u->Graph->Budget` at block start. Below 720 (0.6 × 1200) skip the SYM groups. Below 480 also skip
  NL and let G2 run only while struck (TV8). The handpan never takes the engine's LOD: its lanes are its own state.
- **Retire** a group when its loudest lane is below −110 dBFS (`ModalEndBlock` energy < 1e-11) and its field has no
  pulse or pending hit; zero its state.
- **Reset** (`UnitFlags.Reset`, or command Op 3): restore the bank, gains and poles from the table; clear pulses,
  pending hits, tak slots, NL history, damping and glide.
- **Output:** mono into channel 0, added (the engine zeroes it).
  - At mf (vel 0.6, dimple) a single stroke is −25.8 LUFS over 3 s: the mean of the 8 ring fields, −25.3…−26.5. That
    is the harp's reference stroke level (−25.7 LUFS, harp.md 5.9). The ding is −23.5.
  - The peak at vel 1 on a single note is −1.9 dBFS (A4).
- **Published levels (audio → main):** `Level[i] = √(Σ T1..T3 energy)` per field each block, read with
  `Volatile.Read` for the shimmer (7.4) and Sunny.

### 2.9 Constants (K in `handpan-engine.mjs`; `HandpanTables.g.cs` carries them all)

| Constant | Value | Source or reason |
|---|---|---|
| T60 of f0 | 5.5·(146.8/f0)^0.35 s, ding ×1.08 | Alon 2015 Table 4.2 and Appendix F: three-mode means 2.9 s (all), 3.3 s (instruments 1–2), max 5.9 s; low notes ring longest |
| T60 octave / fifth | ×0.78 / ×0.58 | Highs die first (the critics' metallic-tail rule); Alon's per-mode values scatter 1.2–5.9 s |
| Octave / fifth input | 0.80·(f0/220)^0.45, 0.35·(f0/220)^1.2 | Calibrated to Alon Table 4.1 at the apex: medians −2.7 / −14.4 dB re f0 (8 fields) |
| Nodal offsets U_OFF / V_OFF | −0.18 / +0.12 | Calibrated: octave at the apex −2.4 dB; Alon 4.3 O and P spot gains +5 / +7 dB |
| Radiation T1 / T2 / T3 / U | 1 / 0.85 / 0.55 / 0.6 | Morrison & Rossing 2007: fundamental and second-mode intensity monopole-like, third mode dipole |
| Upper modes | 7.50 … 17.8 f0; input −21 − 1.4k dB, ding −14 dB more | Revision 2: gates 3.9 (no inharmonic partial within 20 dB of f0 or above the fifth at 30 ms) |
| Contact | 1.10 ms·V^−0.3 (fingertip), 7.5 ms (palm), mix 0.62/0.38 at W2 = 0.68 W1 | Design (2.3); Wessel 2008 for the direction |
| Impact speed | 0.25 + 2.75 vel^1.3 m/s | Gives a ~21–24 dB pp→ff range (harp 17.6, old kit 18): wide but not the critic's 27 dB |
| NL targets | −14, −22, −28, −34 dB at u = 1 | Design: a mild bloom at ff, inaudible at pp (3.3) |
| SYM targets | −26 dB; octave pairs −21 dB (calm) / −18 dB (measured) | Design: a halo 18–40 dB under the note (3.6); Morrison & Rossing Fig. 7 shows strong coupling, unquantified |
| Detune | calm σ 3.5 c, \|d\| ∈ [2, 7] c; measured octave −14…−26, fifth −8…−28 c | 3.6 |
| Shell | 32 modes 520–6500 Hz, T60 ≤ 0.12 s | Revision 2: the tak is a click (down 40 dB in 80 ms), not a pitch |
| Tak | neighbours' techK 0.17, fall-off 25 mm, cavity push 0.15 | Revision 2: neighbours' f0 at −25…−35 dB re the tak's peak (3.5) |
| Cavity | 91.35 Hz, T60 0.38 s; ding drive 0.025 | Helmholtz from this shell (2.2); the Hang's measured 85 Hz (Wessel 2008); Q ≈ 15 for a large port |
| Damping | finger 55/s, palm 140/s, shell 1.2/s, smoothing 0.5/block | Palm T60 50 ms (a hand on steel), finger 0.2–0.3 s on the modes it touches |
| OUT_GAIN, tilt, ding | 0.063, −0.32 dB/st, +5 dB | Matched to the harp's reference stroke loudness; the ding carries |

---------------------------------------------------------------------------------------------------------------------------

## 3. Objective verification (`node verify.mjs`, `node calibrate.mjs`, `node sound-gates.mjs calm|measured`; full numbers in `out/*.json`)

### 3.1 Mode frequencies and ratios (calm, mf at the dimple edge, 2¹⁸-point FFT over 0.3–5.8 s)

| Field | D3 | A3 | C4 | D4 | E4 | F4 | G4 | A4 | C5 |
|---|---|---|---|---|---|---|---|---|---|
| f0 measured (Hz) | 146.832 | 219.999 | 261.625 | 293.685 | 329.625 | 349.225 | 391.995 | 439.993 | 523.249 |
| f2/f1 | 2.0025 | 1.9966 | 2.0034 | 2.0080 | 2.0039 | 1.9977 | 1.9925 | 2.0081 | 2.0081 |
| f3/f1 | 3.0035 | 3.0043 | 2.9879 | 2.9963 | 2.9966 | 3.0053 | 3.0122 | 3.0035 | 2.9937 |

- The fundamentals land within 0.12 cents of equal temperament, and the octaves and fifths within ±7 cents of
  harmonic. In the measured voice the octaves are 14–26 and the fifths 8–28 cents flat (2.2).
- References: Morrison & Rossing 2007 (the Hang's G3 note): 189 / 390 / 593 Hz = 1 : 2.06 : 3.14. Alon 2015 Table 4.1
  (8 fields of a pre-tuning handpan): octave 1.96–1.99, fifth 2.92–2.99.
- The upper modes are 7.4–18.2 f0, all ≤ 9.4 kHz (Nyquist 24 kHz: no aliasing). Their T60s are 0.11–0.28 s, at most
  4.8 % of the field's fundamental T60.

### 3.2 Partial levels at the dimple and strike position (one 2048-sample frame 10 ms after onset: Alon's method)

| | Octave re f0 (dB) | Fifth re f0 (dB) | O spot: octave gain | P spot: fifth gain |
|---|---|---|---|---|
| **Ours, ring median (A3…C5)** | **−2.41** (−3.20…−2.13) | **−14.3** (−16.0…−13.4) | **+6.4** | **+6.8** |
| Alon 2015 (Table 4.1 at "D"; 4.3) | median −2.7 (−22.4…+1.8) | median −14.4 (−22.1…−7.3) | ≈ +5 | ≈ +7 |
| Ours, ding | −3.46 | −17.9 | +6.4 | +6.9 |

- Per position on A3:
  - centre: octave −3.0, fifth −15.1;
  - O spot: octave +3.4;
  - P spot: fifth −8.6;
  - dimple edge (0.35, 0.35): octave +2.6, fifth −9.0, the "full" sound;
  - field edge (u 0.92): f0 −19.9 dB re centre, with energy above 2.2 kHz −21 dB vs −35 dB at the centre (the tick).
- The fundamental's decay does not depend on position (Alon 4.3): it is the same pole by construction.

### 3.3 Spectral evolution and dynamics

A3 at the dimple apex, levels re the fundamental at t = 0.08 / 0.25 / 0.5 / 1 / 2 s:

| vel | Octave | Fifth | 4 f0 (NL) | Centroid (Hz) |
|---|---|---|---|---|
| 0.3 | −3.4 −2.8 −3.1 −5.5 −8.0 | −16.9 −18.7 −21.4 −28.0 −34.4 | −66 … −77 | 294 299 294 268 250 |
| 0.6 | −2.7 −1.7 −1.6 −4.1 −6.7 | −16.2 −18.1 −20.2 −26.9 −33.4 | −46 −41 −43 −51 −65 | 302 312 312 282 259 |
| 1.0 | −1.7 **+0.3 +1.4** +0.3 −3.6 | −17.6 −16.0 −18.3 −26.5 −32.4 | −30 −27 −31 −40 −54 | 312 338 349 334 287 |

- At ff the octave of A3 swells for ~0.5 s (the bloom), and harmonics up to 4–5 f0 appear (30 dB under f0). At pp the
  sound is the fundamental plus its octave, with everything else 60 dB down. This is Wessel's description: soft = two
  components in an octave relation; loud = more components.
- The highs decay first. Energy above 2 kHz at 0.5 s is −95 / −90 dB (A3 / E4) re total; C5's −51 dB is its harmonic
  4 f0 = 2093 Hz. The largest upper-mode T60 is ≤ 5 % of the fundamental's on every field: no metallic tail.

| Velocity ladder (dimple edge) | 0.08 | 0.2 | 0.35 | 0.5 | 0.65 | 0.8 | 1.0 | Range |
|---|---|---|---|---|---|---|---|---|
| A3 LUFS | −39.7 | −34.8 | −30.2 | −26.8 | −24.1 | −21.8 | −19.1 | **20.7 dB** |
| A4 LUFS | −42.3 | −36.6 | −31.2 | −27.2 | −23.8 | −21.4 | −18.7 | **23.6 dB** |

### 3.4 Decay (T60 from narrowband envelopes, 0.25 s to −45 dB)

| | D3 | A3 | C4 | D4 | E4 | F4 | G4 | A4 | C5 |
|---|---|---|---|---|---|---|---|---|---|
| f0 / oct / fifth (s) | 5.94 / 4.73 / 3.48 | 4.77 / 3.65 / 2.76 | 4.49 / 3.57 / 2.72 | 4.14 / 3.37 / 2.53 | 4.15 / 3.25 / 2.40 | 4.06 / 3.15 / 2.31 | 3.90 / 3.04 / 2.26 | 3.80 / 2.92 / 2.17 | 3.51 / 2.76 / 2.04 |

- The mean of the three is 3.4 s (2.04–5.94 s). Alon Table 4.2: 2.9 s over 4 instruments, 3.3 s for instruments 1–2,
  0.9–5.9 s, and lower notes longer (low 3.2, high 2.5 s).
- Measured equals design within 0.18 s; the fit residuals are ≤ 0.6 dB rms.

### 3.5 Techniques

| | Result |
|---|---|
| Ding fingertip, vel 0.7 | Cavity 91 Hz at **−24.0 dB** re the ding's f0 (critic c3 F': −16.1 dB envelope maxima): the Hang's "small peak at 85 Hz" (Wessel 2008); −20.1 LUFS |
| Ding palm slap, vel 0.7 | Cavity **−6.3 dB** re f0, centroid 130 Hz: the bass "dum"; −20.3 LUFS (as loud as a tip, much darker) |
| Palm slap on A3 | −29.6 LUFS, centroid 183 Hz: a muffled thump (the palm stays in practice, which mutes it) |
| Tak between A3 and C4, vel 0.7 | −31.4 LUFS, centroid 970 Hz: a short, dry click. The shell part falls 40 dB in 80 ms and 60 dB in 0.2 s; A3 and C4 hum at −27…−35 dB re the tak's peak; the cavity answers at −28…−31 dB (`sound-gates.mjs` 3, 8 strokes at 4 points) |

### 3.6 Sympathetic ringing and beating: the two voices

| Struck: ding (vel 0.7), calm | Sympathetic f0 peak re the total at that frequency | Halo share of the output |
|---|---|---|
| D4 (ding octave, Marshall pair) | −20.2 dB | −18.6 dB |
| A4 (A3 octave, Marshall pair) | −21.3 dB | −36.5 dB |
| A3 (no partner for its f0) | −31.8 dB | −40.6 dB |

**Correction of revision 1.** Revision 1 claimed "no amplitude minimum deeper than 0.5 dB within 4.5 s". The critic's
leakage-safe demodulator (80 ms Blackman, self-tested: 5.36 dB read for a 5.7 dB synthetic beat at 3.3 Hz, 0.01 dB
for a lone partial) shows otherwise:
- the published 3–9 Hz beating is essentially absent;
- instead, several partials swell slowly, by 2.4–5.7 dB peak-to-peak at 0.35–1.2 Hz. The ding's octave swings ±2 dB
  over a 2.6 s period.

The cause: coupled partials 2–7 cents apart, and the nonlinear pump's exactly harmonic 2 f0 against the 2-cent-sharp
octave lane. Alon & Murphy (SMC 2015, four instruments from three makers) measured AM at 3.1–8.6 Hz, caused by "a slight
mismatch in tuning of signature partials on separate note-fields". Adding it raised the median similarity of their
resynthesis from 5.1 to 6.75 out of 10.

**The A/B (the user chooses by ear; files in section 10).** Same lanes, same CPU, same attack and tak; only the tuning
and the octave-pair coupling differ (2.2, 2.6). Measured with the critic's method (`sound-gates.mjs` 4; vel 0.6, the
render's stroke point):

| | calm | measured |
|---|---|---|
| Partials (9 fields × 3 × 2 velocities) beating ≥ 1 dB at ≥ 2 Hz | 1 of 54 | 16 of 54 |
| Slow swells ≥ 2 dB below 2 Hz | 14 of 54 (ding octave 4.3 dB at 0.4 Hz) | 2 of 54 (the ding's weak fifth, 6 dB at 0.4 Hz, both velocities) |
| Ding octave / D4 f0 | 4.3 dB @ 0.4 Hz / 2.8 dB @ 0.4 Hz | **1.6 dB @ 2.35 Hz / 2.0 dB @ 2.35 Hz** |
| A3 octave / A4 f0 | 2.4 dB @ 0.7 Hz / 3.1 dB @ 0.8 Hz | **2.7 dB @ 5.1 Hz / 1.3 dB @ 5.15 Hz** |
| C4 octave / C5 f0 | 2.6 dB @ 0.95 Hz / 1.4 dB @ 0.9 Hz | **1.6 dB @ 5.9 Hz / 1.25 dB @ 5.95 Hz** |
| Every other gate (3.9) | pass | pass |

- The measured voice meets the critique's acceptance (1–3 dB at 2–8 Hz on the octave pairs).
- Its remaining slow swell is on the ding's fifth, which is 17–22 dB under the ding's fundamental. It is A4's sympathetic
  fundamental against the ding's harmonic 3 f0, 2 cents apart: equal temperament's fifth.
- `HandpanTables.Default` is `Calm` until the user picks; the other voice's 216-lane block can then be dropped.

### 3.7 Damping

| | Result |
|---|---|
| A3 rings, fingertip rests at 0.5 s (dimple edge) | −17.1 dB at 1 s re undamped. The field's own modes reach T60 ≈ 0.2 s; what remains is the sympathetic halo of the other fields. |
| Palm rests at 0.5 s | −19.1 dB at 0.8 s; the field's T60 is 0.05 s |
| Palm rests 120 ms, then lifts | −17.3 dB at 1 s: a 120 ms palm already stops the note (as on steel) |
| Harmonic isolation: fingertip on the octave antinode = the fifth's nodal line, 0.9 s | Field's own f0 −189 dB, octave −240 dB, **fifth −7.7 dB**: the fifth sings alone (Alon 2.1.6, a known handpan technique) |
| Click at a damp onset | Second-difference ratio 0.01, identical to no damp |

### 3.8 Artefacts and stability

| Check | Result |
|---|---|
| Onset click (largest 2nd difference / local RMS, vel 1) | 0.03 (steady state 0.01): the raised-cosine contact has zero slope at the start |
| DC (mean / peak) | −1.9e-6 |
| Low-frequency push: energy < 45 Hz in the first 85 ms | −96.3 dB (fingertip), −50.4 dB (ding palm slap) re total. The drums critic's issue (H1) cannot occur: the drive is a derivative. |
| Aliasing | The highest mode is 9.4 kHz; resonators are band-limited; no nonlinearity runs at audio rate on the output |
| Denormals | 0 subnormal states over a 70 s tail with every field struck; all groups retired at the end (TV9) |
| NaN / Inf | 0 |
| Worst-case peak (9 fields ff + ding slap within 4 ms) | +11.3 dBFS. A single ff note peaks at −1.9 dBFS. The worst case needs 10 fingers landing within 4 ms, and the master bus limiter takes it (UNVERIFIED in-app headroom: section 9.3 step 4). |
| Determinism | Identical re-render (bit-exact). Fused (Burst-like FMA) vs strict: max \|Δ\| 1.1e-6 (TV1), within the 1e-5 tolerance |

### 3.9 Revision-2 sound gates (`sound-gates.mjs`; both voices pass all of them)

| Gate | Method | Result |
|---|---|---|
| No inharmonic partial within 20 dB of f0 at 30 ms | Every lane's complex amplitude read from the engine state 30 ms after the onset (exact); upper and shell modes; 9 fields × 5 positions (apex, O, P, (0.35, 0.35), the render's (0.3, 0.2)) × vel 0.3 / 0.6 / 1 | Worst −21.5 dB (A3, 8.27 f0, ff at (0.35, 0.35)); per field D3 −26.6 … C5 −31.4 |
| No inharmonic partial above the fifth at 30 ms | Same | Worst 7.1 dB under the fifth |
| The critic's spectral version (c7) | 20 ms Blackman at +30 ms, upper modes | Worst −21.1 dB; 5.7 dB under the fifth. Critic's c7: no stroke with the ping above the fifth (revision 1: 8 of 12) |
| Top 3 partials at 10 ms within 3 % of 1–4 f0 | 4096-point Hann from +10 ms (c2 A's frame), zero-padded ×4 with parabolic peaks | 135 of 135 strokes. (c2 A's own table reads the ding's f0 at "1.04": that is its 11.7 Hz bin spacing, 8 % of 147 Hz.) |
| Tak neighbours | The critic's c3 E' metrics, the f0 envelope read after the click | −26.7 … −34.9 dB re the tak's peak (4 points, vel 0.45 / 0.6) |
| Tak down 40 dB within 80 ms | 5 ms RMS of the shell + cavity part (field 9 alone) | 80 ms at every point |
| Measured voice: 1–3 dB AM at 2–8 Hz on the octave pairs | The critic's c3 B' demodulator | 1.25–2.66 dB at 2.35–5.95 Hz (3.6) |

---------------------------------------------------------------------------------------------------------------------------

## 4. CPU and memory (`node bench.mjs`; Apple M5 Pro, Node 26.7; Quest = Node × 0.10, burst.md section 10.2)

| Scenario (256-frame blocks) | Node mean / p99 / max (µs) | Lanes (full-block equivalent) mean / max | **Quest 3S level 3 mean / max (µs)** | Model 0.48 ns × lane-samples × 1.3 |
|---|---|---|---|---|
| One note ringing (A3 mf) | 80.9 / 128.7 / 146.4 | 48 / 88 | **8.1 / 14.6** | 7.7 / 14.1 |
| Lesson: a note every 0.6 s, 20 s | 182.7 / 249 / 380 | 102 / 144 | **18.3 / 38.0** | 16.3 / 23.0 |
| Groove: slaps, taks (with neighbours) and notes at 8/s | 197.6 / 246 / 344 | 116 / 140 | **19.8 / 34.4** | 18.5 / 22.4 |
| Worst: every field ff every 60 ms (10-finger roll) | 348.1 / 380 / 523 | 216 / 216 | **34.8 / 52.3** | 34.5 / 34.5 |
| Palm mutes while ringing | 97.9 / 149 / 156 | 61 / 88 | 9.8 / 15.6 | 9.7 / 14.1 |
| Idle | 1.1 | 0 | 0.1 | 0 |
| Measured voice: worst / groove | 348.3 / 381 / 543; 197.0 / 234 / 266 | 216; 116 / 140 | 34.8 / 54.3; 19.7 / 26.6 | 34.5; 18.5 / 22.4 |

- **Row for burst.md section 10.3 (for its owner):** "Handpan unit, worst 216 lanes driven + unit work: ≈ 35 µs mean,
  54 µs max at level 3; a lesson ≈ 18 µs." With the 0.89 ms worst case of today's instruments, the total stays ≤ 0.95 ms
  against the 2 ms target. Revision 2 adds 8 shell lanes and the tak's two neighbour pulses: +2 µs on the worst case.
- The handpan is played alone or with the looper's playback of other instruments, so its worst case and the piano's
  rarely coincide. The machine was shared with other lab runs; one earlier run read up to 85 µs max with the same lane
  counts.
- The unit work outside the kernels, per active field and sample:
  - the pulse rotation (≈ 12 flops);
  - the NL polynomial (≈ 8);
  - the bus, cavity and shell accumulations (≈ 6).
- **Memory:**
  - bank 54 × 96 B = 5.1 KiB; poles 1.7 KiB; gains 0.8 KiB;
  - pulses, pending hits, tak slots and damping: 5 KiB;
  - scratch (exc 9 × N, tuned 9 × N, 8 × N singles, acc 4 × N floats) = 30 KiB at N = 256 (120 KiB at 1024).
  - **Total ≈ 42 KiB at MaxFrames 256.** No baked audio: 0 MiB of the 96 MB budget. The generated C# (both voices)
    is 41 KB of source.

---------------------------------------------------------------------------------------------------------------------------

## 5. Unity integration

### 5.1 Files (new)

| File | Content |
|---|---|
| `Assets/MusicRoom/Audio/Sound/Handpan/HandpanTables.g.cs` | Generated by `gen-cs.mjs`: `HandpanVoice`, `HandpanTables` (constants, LaneMode, upper-mode tables, `Default`) and per voice `HandpanTablesCalm` / `HandpanTablesMeasured` (every lane's pole, go, gi, plus uRef and GlideW) |
| `…/Handpan/HandpanDsp.cs` | `[BurstCompile]` static `Render(UnitSlot* u)`: section 2 exactly (segment, split, endBlock); `UnitHooks.RenderHandpan` |
| `…/Handpan/HandpanState.cs` | The unmanaged state (5.3) |
| `…/Handpan/HandpanUnit.cs` | `SoundUnit, IHandpanSound`: Register, Strike, Damp, ReleaseAll, Level, `SoundUnits.CreateHandpan` (loads `HandpanTables.Default`) |
| `…/Handpan/IHandpanSound.cs` | The unit-specific API (5.2) |
| `Assets/MusicRoom/Handpan/` (new module, asmdef `MusicRoom.Handpan`) | `HandpanModule` (Rig, IInstrument, IHandpanApi, IContactSurface, `StrikeTak`); `HandpanSpec` (layout constants = `handpan-layout.mjs`); `HandpanMath` (`contactOf`, `hitField`, `relief`, `fieldCoords`, `takNeighbours`, velocity curves, the slope and rest-anchor helpers); `HandpanPlay` (`handpan-interaction.mjs`); `HandpanGeometry` (mesh, section 7); `HandpanMotion` (settle, shimmer) |
| Tests | `Tests/EditMode/Audio/Handpan/` (DSP vectors, section 9.1); `Tests/EditMode/Handpan/` and `Tests/PlayMode/Handpan/` (interaction cases, 9.2; lesson blocks, 8.1); `Reference~/` holds `test-vectors.json`, `interaction-vectors.json`, `handpan-lessons.json`, `vec-*.f32` |

### 5.2 API

```csharp
namespace MusicRoom.Audio.Burst
{
    public enum HandpanTech : byte { Tip = 0, Slap = 1, Tak = 2 }
    public enum HandpanDampKind : byte { Finger = 0, Palm = 1 }
    public interface IHandpanSound
    {
        /// field 0..8 (0 = ding), 9 = between the fields (the tak's shell pulse). u, v: field-normalised (2.4).
        /// gain scales the contact force on the main thread (1 for every player strike; a tak's neighbour pulses w < 1).
        /// when: SynthCommand rules.
        void Strike(int field, float vel, float u, float v, HandpanTech tech, double when, float gain = 1f);
        /// slot 0 L fingers, 1 R fingers, 2 L palm, 3 R palm; field 9 = the whole shell; strength 0 releases the slot.
        void Damp(int field, int slot, float strength, float u, float v, HandpanDampKind kind, double when);
        void ReleaseAll(double when);
        /// per field: sqrt of the tuned modes' energy, last block (audio -> main, Volatile.Read)
        float Level(int field);
    }
}
```

`HandpanModule.StrikeTak(float vel, float X, float Z, double when)` (main thread):
1. `Strike(9, vel, 0, 0, Tak, when)`;
2. then for each of `HandpanMath.TakNeighbours(X, Z)`: `Strike(n, vel, 0, 0, Tak, when, w)`.

The three commands share `when` and are posted in this order (TV3 lists the exact payloads).

Command payload (64-byte `GraphCommand`, the unit's own ring, capacity 256):

| Op | When | Fields |
|---|---|---|
| 1 Strike | the caller's `when` (AudioSchedule-stamped); `Flags = LateDrop` | `N` field; `Target` tech; `A` = (float)(A1·gain); `Seed` = `BitConverter.SingleToInt32Bits((float)(A2·gain))`; `B` u; `C` v; `D` c1; `E` s1; `F` c2; `G` s2; `Id` = W1 \| W2 << 16 |
| 2 Damp | the caller's `when`, never LateDrop | `N` field (9 = shell); `Target` slot; `A` strength; `B` u; `C` v; `D` kind |
| 3 Reset | 0 | |

`Strike` computes the pulse on the main thread (`strikeParams`, double maths, rounded once): no transcendental runs on
the audio thread. A gain of exactly 1 leaves A1 and A2 untouched (bit-exact with revision 1's strikes).

### 5.3 State (one `Alloc`, 64-byte aligned; Node names in brackets)

- `ModalGroup Bank[54]` [bank]; `float P0r[54*4], P0i[54*4]` (from the table); `float Gi[54*4]` [gi]; `byte Active[54]`.
- `float Pulse[9*16], Pend[9*16]`; `byte PendOn[9]`; `float PendUV[18]`; `byte PendTech[9]`; `float ShellAmp[9], CavRes[9]`.
- `float Tak[4*16]`; `float NlPrev[9], U2[9]`; `float DSlot[9*4*12], DCur[9*12], DGlobal[4]`; `byte GlideOn[9]`; `float Level[9]`.
- Scratch: `float Exc[9*MaxFrames], Tuned[9*MaxFrames], Y0, Bus, Xs, ShellX, CavX, TakX, Xnl [MaxFrames]`; `float4 Acc[MaxFrames]`.
- Pulse slot layout: `[on, n, W1, W2, A1, A2, c1, s1, c2, s2, C1, S1, C2, S2, Fprev, -]`.

### 5.4 Order of operations (bit-exact managed; 1e-5 with Burst Fast)

1. Due events in (At, Seq) order; apply each at its offset; split at every pending start (`segmentSplit`).
2. Segment: per field 0..8:
   - pulse;
   - G0 render into a zeroed `Acc`, then `y0 = Acc[n].x` and `tuned = (x + y) + z`; `AccResolve`;
   - NL, if allowed: xnl, render, `tuned += (x + y) + (z + w)`, resolve;
   - G1, then G2: render, resolve;
   - `bus = mad(1, tuned, bus)`; `cav = mad(cw, y0, cav)`; if the field has a pulse and `ShellAmp ≠ 0`:
     `shell = mad(sa, e, shell)`, `cav = mad(cr, e, cav)`.
3. Tak pulses: if any slot is on, zero `TakX`, run slots 0..3 into it (each adds), then per sample
   `shell = shell + TakX` and `cav = mad(0.15, TakX, cav)`.
4. Then shell groups 45..52, cavity 53, and SYM groups field 0..8 with `x = own ? bus − tuned_j : bus`.
5. Every group renders into a zeroed `Acc` and resolves into the channel **immediately** (canonical order).
6. Block end:
   - `ModalEndBlock` on every active group in ascending order: retire, then levels;
   - damping and glide in double, rounded once on store.
7. A segment of zero length renders nothing. Late events play at the block start.

### 5.5 Shared files that must change (minimal; integrator)

| File | Change | Why ENGINE-API section 4.4 cannot cover it |
|---|---|---|
| `Audio/Burst/Units/UnitSlot.cs` | `UnitId.Handpan` (append before `Count`); `static partial void RenderHandpan(UnitSlot* u)` and its `case` in `UnitHooks.Render` | A new role, not an extra call on an existing role |
| `Audio/Burst/Units/SoundUnit.cs` | `static partial void CreateHandpan(SoundContext ctx, ref SoundUnit unit)` and the call in the creation order (after Harp, before Room) | Same |
| `Audio/Engine/IAudioEngine.cs` | `SourceId.Handpan` (append before `Count`) | A new 3D source at the instrument |
| `Audio/Engine/AudioRig.cs` | The `"Synth Handpan"` source: spatialized like `Harp`; reverb send +3 dB like the harp; follows `IHandpanApi.SoundOrigin` (the shell centre + 6 cm up) each frame | |
| `Audio/Engine/AudioEngine.cs` | Classic sound: a role with no classic synth (Handpan) is **never suspended**. Add `SoundUnit.HasClassic` (virtual, default true; Handpan false) and skip `OnPathChanged(false)` / suspension for it. | Otherwise switching to Classic would silence the handpan |
| Room unit (`Audio/Sound/Room`) | Tap `SourceId.Handpan`, as its own ER group (4th; ≈ +1 ImageSourceER) or mapped to the piano's front group. The renders used a dedicated group at the handpan's position. | Group membership is the room owner's table (room.md 5.6) |
| `Core/Contracts/Instruments.cs`, `Rigs.cs` | `InstrumentKind.Handpan`, `RigId.Handpan`, `PlaySurface.Handpan`; `IHandpanApi` (5.6) | New instrument |
| `Core/Contracts/Lessons.cs` | `TakeEventType.Handpan = "handpan"`, `HandpanDamp = "handpanDamp"`; `TakeEvent.Handpan(i, vel, u, v, tech)` (fields `i`, `vel`, `u`, `pos` = v, `mode` = "tip"/"slap"/"tak"; **for a tak `i` = 9, `u` = X, `pos` = Z in map metres**, and playback recomputes the neighbours), `TakeEvent.HandpanDamp(i, slot, strength, u, v, kind)` (`vel` = strength, `zone` = slot as "0".."3", `mode` = kind) | No struct change: existing fields reused, interned strings |
| `Settings` | `musicroom.current` accepts `"handpan"` | |
| `Lessons` (Tutorial, LessonHints, PlaybackWiring, Looper's host) | Instrument `"handpan"`: targets `{i}`; a disc block on the line of sight (8.1); TRAVEL 0.30 (lead 1.667 s); hints "Tap E4" / "Tap the ding"; dispatch `handpan` events to `IHandpanApi.PlaybackStrike` inside `ScheduleAt` | |
| `Guide` | 9 tips, 9 lines, one new demo kind `Slap` (8.2) | |
| `Menu` | The Instruments screen lists Handpan among "one alone" | |
| `Layout` | Register the rig; Single home; All-mode policy (section 11, open question 1) | |

Other new instruments in this batch need the same appends. The integrator assigns the enum values in one pass, and
nothing in this spec depends on the numeric value.

### 5.6 `IHandpanApi` (Core)

```csharp
public interface IHandpanApi {
    Transform Root { get; }  int FieldCount { get; }  /* 9 */  int MidiOf(int field);  /* static table */
    Vector3 FieldCentre(int field);  /* world, on the steel: the dimple apex */  Vector3 FieldNormal(int field);
    Vector3 SoundOrigin { get; }
    void PlaybackStrike(int field, float vel, float u, float v, string tech);   // recorder / looper / Sunny demos; field 9: u, v = X, Z
    void PlaybackDamp(int field, int slot, float strength, float u, float v, string kind);
    void PlaybackReleaseAll();
    float FieldLevel(int field);   // from the unit (0 when no unit)
}
```

Recorder: every live strike and damp calls `IRecorder.Add(TakeEvent.Handpan(...))` and stamps
`owner.LastPlayMs = Clock.NowMs`, recording or not, so Sunny and the lessons observe it (ARCHITECTURE section 8).

---------------------------------------------------------------------------------------------------------------------------

## 6. Bare-hand interaction (implements `handpan-interaction.mjs` exactly; constants `IK`)

### 6.1 Contact geometry

- **Surface.** The top shell is a spherical cap: rim Ø 0.53 m, height 0.115 m, sphere radius 0.3629 m, apex-to-rim
  arc 0.2974 m. On it sits the relief: the ding's raised dome (+10 mm, quartic falloff), each field's −2 mm depression
  and its concave dimple (−4 to −7 mm, radius 0.38–0.42 of the semi-axes).
- **Map.** A point's geodesic polar coordinates on the cap give the map (X, Z) = (s sin az, −s cos az). Each field is
  an ellipse in the map, long axis radial: field length `0.150·(220/f0)^0.45` m, width 0.80 × that; the ding is a
  circle Ø 0.165 m. The ring centres are at s = 0.180 m.
- **Pad height.** `h = |p − c| − R − relief(X, Z) − r_pad`, where `r_pad` is the fingertip joint's capsule radius ×
  handScale (index 7.6 mm). Hand-tracking fact: the tip joint centres are ~17 mm apart when the tips touch (spec 02),
  so the pad surface is ~8 mm out from the joint.

| Zone | Size | Rule |
|---|---|---|
| Ding (field 0) | Ø 16.5 cm | `hitField`: the smallest normalised radius < 1 + 6 mm / semi-short-axis |
| A3, C4 (near) | 15.0 × 12.0, 13.9 × 11.1 cm | |
| D4, E4 | 13.2 × 10.5, 12.5 × 10.0 cm | |
| F4, G4 | 12.2 × 9.7, 11.6 × 9.3 cm | |
| A4, C5 (far) | 11.0 × 8.8, 10.2 × 8.1 cm | |
| Gaps between neighbours | 2.2–5.3 cm (to the ding 2.2–4.7 cm) | Interstitial steel = tak (field 9) |
| Off the instrument | beyond the rim arc + 1 cm, or under the rim plane − 2 cm | Ignored |

- The smallest field is 8.1 cm across, against fingertip tracking error of ≈ 0.5–1 cm (UNVERIFIED on Quest 3S; T-AIM in
  9.3; Abdlkarim et al. 2024 measured 1.1 cm absolute fingertip error on Quest 2).
- The 6 mm edge margin keeps a stroke that lands just off a field's drawn edge on that field.

### 6.2 Inputs, samples and speeds (revision 2: no sample interval)

- **What to feed `HandpanPlay.Step` each frame:**
  - `t` = the frame clock (`Clock.NowS`);
  - each fingertip's world position = `Pointer.Tip` (the predicted pose for this frame), with its capsule radius;
  - the palm joint position and normal;
  - the hand flags (tracked, high confidence, inferred, system gesture).
  Do **not** use `HandFrame.sampleInterval` or `sampleTime`. That is the tracker interval (about 33 ms at the LOW rate),
  while the predicted pose moves every frame, so reading it would make every speed about 3 times too low.
- **Distinct samples.** A pose that moved more than 10 µm since the pointer's last sample (`NEW_SAMPLE_M`, the
  `PointerInput` rule) is a new sample, stamped with `t`. A pose that repeats (a 30 Hz tracker without prediction)
  adds nothing. Each pointer and the palm keep their last 8 samples (t, h, X, Z, position) in a fixed ring.
- **Speeds** are least-squares slopes over the samples within a window ending at the newest sample, in double:
  `slope = Σ(t_k − t̄)(q_k − q̄) / Σ(t_k − t̄)²`.
  - A window takes at least 3 samples if they lie within 0.1 s (`MIN_SAMPLES`, `MAX_WIN_S`); with fewer than 2, the
    slope is 0.
  - Loudness and tangential speed use 45 ms (`VEL_WIN_S`: 5 samples at 90 Hz, 4 at 72 Hz, 3 at 30 Hz). The strike
    decision uses 70 ms (`GATE_WIN_S`: 7, 6, 3).
  - `vn = −slope(h)`, `vt = |(slope(X), slope(Z))|`.
- **Teleports.** A sample more than 12 cm from the pointer's last one is held back, with no state change. If the next
  sample lies within 12 cm of the held one, the hand really moved: the history restarts from the held sample, and the
  pointer is disarmed until it re-arms. Otherwise the held sample was a glitch and is dropped.

### 6.3 Fingertip strikes

- **Arm.** A pointer arms when its pad is > 10 mm above the steel on **2 consecutive samples**, while on the top shell,
  after the hand has been tracked with high confidence for > 2 frames.
- **Strike.** On the sample where the pad crosses from h > 0 to h ≤ 0 (previous sample at most 8 cm above):
  1. Take the crossing point by linear interpolation in the map, and look up the field there.
  2. It **sounds** when all of these hold:
     - the pointer was armed and the point is on a field or the steel between fields;
     - `−slope(h, 70 ms) ≥ V0 = 0.12 m/s`;
     - h fell on the last 2 sample steps;
     - `vt ≤ 2.5·vn` (else it is a brush).
  3. Velocity `vel = clamp(((max(vn, 0.12) − 0.12)/0.88)^0.75, 0.06, 1)` with `vn = −slope(h, 45 ms)`. Checkpoints:
     0.15 → 0.08, 0.2 → 0.17, 0.3 → 0.30, 0.6 → 0.63, ≥ 1.0 m/s → 1.
  4. A tak is ×0.8. Its event carries the map point (`u` = X, `v` = Z) and the two neighbours with their weights, and
     the module calls `StrikeTak` (5.2).
  5. Any crossing disarms, sounding or not, and opens a **contact** (for the mutes, 6.4) that records whether it sounded.
- **Debounce:**
  - per field 3 ms (duplicates in a frame);
  - **per hand per field 40 ms** (two fingers of one hand landing together are one stroke);
  - per pointer: must re-arm.
  - Different hands are independent (flams); the unit merges hits inside one 1–2 ms contact (2.4).
- **Tracker rate.** The predicted pose at 90 Hz and a 30 Hz pose that repeats for 3 frames give the same strike.
  Constant-speed strokes read the same velocity. A fast minimum-jerk tap shows only 2 samples on its way down at 30 Hz,
  so it reads up to 0.22 lower there, never higher (`hands-acceptance.mjs` 8).
- **Latency.** `Strike(…, when = 0)` plays at the next block. A predictive strike (burst.md section 12, lever 3) is
  allowed with a 1-frame look-ahead; it is off by default (false triggers on a gentle instrument cost more than 7 ms).

### 6.4 Techniques (classification order: palm first, then fingertips)

| Technique | Detection | Sound |
|---|---|---|
| **Fingertip tap** | 6.3 | `Tip` at the crossing (u, v): centre pure, dimple edge full, field edge tick |
| **Tak** | A fingertip (or palm) crossing on the steel between fields | `StrikeTak(vel·0.8, X, Z)`: the shell click + the two nearest fields' hum |
| **Palm slap** | The palm (centre of wrist, index-proximal and little-proximal; normal out of the palm) faces the steel (`dot(n_palm, −n_surface) > 0.6`), its surface (capsule radius 23 mm) crosses from > 12 mm to ≤ 12 mm, palm `vn ≥ 0.35` m/s (45 ms slope), and it was armed (> 22 mm above) | `Strike(field under the palm centre, palmVel(vn), u, v, Slap)`: the ding gives the bass boom. Velocity `((vn − 0.25)/1.35)^0.75` |
| **Flat-hand landing** (revision 2) | A hand is landing flat while its palm faces the steel, approaches at > 0.25 m/s, and either its surface is within 2 cm or ≥ 3 of its fingertips are within 1.5 cm. Its fingertip strikes then wait (at most 60 ms). A slap drops them, and fingertip crossings of the slapping hand within 60 ms after its slap belong to the slap. Otherwise they play on the frame the hand stops landing flat. | Palms-down taps (palm joint ≥ 4 cm above the index joint): 0 ms added. Trailing-palm slaps 0.4–0.9 m/s, lag 10–30 mm: one slap, 0 tips |
| **Finger mute** (revision 2) | The contact's pad stays within 4 mm of where it settled (the running mean of its samples since it settled; two consecutive samples beyond 4 mm restart it). A pad placed without sounding: below +6 mm for **0.10 s**. A pad that **sounded the note**: between −8 and +6 mm for **0.40 s**; a press deeper than 8 mm never mutes. | `Damp(field, hand, 1, u, v, Finger)` with `afterStrike` set for the second kind; one slot per hand per field (the first finger sets it, the last lifted releases it) |
| **Palm mute** | A facing palm within **1 cm** of the steel (revision 1: 2 cm; a palms-down tap's palm passes 1.7 cm above), staying within 6 mm of where it settled for 0.12 s | `Damp(each field within 7.5 cm of the palm (linear to 0 at 11 cm), 2 + hand, cover, Palm)` + `Damp(9, 2 + hand, 1)` (shell touch) |
| **Release** | Finger pad > 12 mm; palm > 4 cm, or turned away (facing < 0.5), or off the shell | `Damp(…, strength 0)` |
| **Harmonic isolation** | Emerges: a finger resting on one mode's nodal line leaves that mode ringing (3.7). Deliberate: place the finger slowly (0.10 s rule) | |
| **Lift hint** (revision 2) | The 3rd time a sounded note is muted by its own finger within 0.5 s of the strike, emit one `hint tip_handpan_lift` (once per session) | Sunny tip (8.2) |

### 6.5 Tracking loss, system gesture, inferred poses

- Untracked, low confidence, `poseInferred` or `systemGesture` all mean: **no new strikes** from that hand, and every
  pointer is disarmed with its history and any held sample dropped.
- Damping held by that hand is **released after 0.30 s**, so a note muted by a hand that vanished does not stay muted.
- On return, the hand must be tracked with high confidence for > 2 frames, and each pad must rise above 10 mm on two
  samples before it can strike. A finger that comes back already under the steel is silent (case `tracking-loss`).
- The desktop mouse hand never strikes; a desktop click on a field plays vel 0.75 at the hit point.

### 6.6 Comfort for a seated player (`geometry.mjs`, `critic/c4-geometry.mjs`)

| Field | D3 | A3/C4 | D4/E4 | F4/G4 | A4/C5 |
|---|---|---|---|---|---|
| Height (m) | 0.827 | 0.703 | 0.746 | 0.807 | 0.850 |
| Shoulder to field centre (m) | 0.45 | 0.43 | 0.44 | 0.50 | **0.56** |
| Angle between the field normal and the eyes | 13° | 36° | 43° | 51° | 55° |

- Every point of every field a player strikes is within the 0.60 m "two feet" reach (0.599 m at the far fields' outer
  points; centres ≤ 0.563 m). Revision 1 (rim centre at 0.68 m) had 0.613 m at those points.
- Alternatives (geometry.mjs): a flat placement would put the far fields at 0.66 m, and a real lap hold at 0.54 m but
  against the belly (the near rim 4 cm from the eyes' plane).
- The near rim clears the body by ~3 cm (rim at z 0.146 m). Against seated thigh heights (seat + thigh clearance:
  0.56 / 0.61 / 0.66 m for a 5th-percentile female, the median and a 95th-percentile male):
  - the near fields' centres are at 0.703 m, their sweet spot (u 0.35) at 0.685 m: +2.5 cm over the tallest thigh;
  - their outer edges (u 1) are at 0.644 m;
  - the near rim band (0.60–0.62 m) sits at thigh height in front of the belly, as on a lap-held handpan, and is
    never struck.
  An overshooting tap on a field (1–2 cm, and E1 shows the finger stopped) does not reach the leg. Arrange mode can
  lift the rig by −0.10…+0.15 m (UNVERIFIED comfort: 9.3 step 6).
- Hands play palms-down at elbow height or a little above (the ding top 16 cm over the elbow, as on a lap), with the
  wrist moving only. Lessons alternate hands (section 8.1).
- No reach across the body. Every note is played by its side's hand; the ding by either.

### 6.7 Acceptance under tracker jitter (`hands-acceptance.mjs`; the critic's scenarios: seeded AR(1), ρ 0.6 per 90 Hz frame, σ per axis)

| Scenario | 0.5 mm | **1 mm (criterion)** | 2 mm |
|---|---|---|---|
| A3 taps, 4.2 cm minimum-jerk, peak 0.3 / 0.6 / 1.0 m/s: velocity SD (missed) | 0.022 / 0.015 / 0.013 (0 %) | **0.044 / 0.031 / 0.027 (0 %)** ≤ 0.05 | 0.082 / 0.059 / 0.055 (5 % of 0.3 m/s taps missed) |
| E4 lowered at 0.05 m/s (a deliberate mute): spurious strikes per 100; mute engaged | 0; 100 % | **0.33; 100 %** (≤ 2; ≥ 95 %) | 11; 95 % |
| Tap then press 0.03–0.3 s (bottom −12 or −4 mm): muted | – | **0 %** (also with no jitter) | – |
| Tap then press 0.45 / 0.6 s still at −4 mm; at −12 mm | – | 100 % (deliberate); 0 % (also with no jitter) | – |
| Hovering 10 s above A3 at 6 / 8 / 12 mm: false strikes | | **0 / 0 / 0** | 0 / 0 / 0 (3 mm jitter: 1 / 1 / 0) |
| Flat-hand slaps 0.4–0.9 m/s, palm 10–30 mm behind the fingers | 1 slap, 0 tips in all 9 | | |
| Palms-down taps, palm joint 4 / 5.5 / 7 cm above the index joint: added delay | 0 / 0 / 0 ms, no palm damp (palm 3 cm above, i.e. its surface 1.5 cm from the steel: 11–33 ms, a flat landing) | | |
| Palm placed on the ding at 0.04 m/s: palm mute engaged; strikes per 100 | | **100 %; 0** | 100 %; 0 |

No published frame-to-frame jitter figure exists for Quest hand tracking (Abdlkarim et al. 2024: 1.1 cm absolute
fingertip error on Quest 2, 45 ms delay); on-device check 9.3 step 2 measures it.

### 6.8 Mixed reality

- The stand stands on the real floor in front of the knees, at the same seat-relative pose as in VR (Single mode).
- E6 shows the real hands over the steel (the passthrough cut-out), and the fingers stop on it (E1).
- One hard shadow map: the shell and the stand cast onto the real floor, and the contact shadows (E3) show on the steel.
- The room unit's MR room (`SetMixedReality`) colours its sound like the other instruments.
- The bottom shell overlaps the space of the real thighs (lowest point 0.596 m), as a lap instrument does. Rendered on
  top of passthrough, it reads as resting in front of the lap.
- If the player sits on the floor (head < 0.9 m), the Layout's fitSeat moves the rig down with the head as for the
  piano.

---------------------------------------------------------------------------------------------------------------------------

## 7. Visual spec

### 7.1 Dimensions

| Part | Size |
|---|---|
| Shell | Two caps, rim Ø 0.530 m. Top: height 0.115 m, R 0.363 m. Bottom: 0.105 m, R 0.387 m. Steel 1.2 mm. |
| Ding | Ø 0.165 m field, dome +10 mm, concave dimple Ø 0.069 m, 4 mm deep |
| Ring fields | Ellipses (6.1), field depression 2 mm, concave dimple 0.38 × semi-axes, depth `4 + 3·(L/0.15)` mm |
| Gu (port) | Ø 0.090 m on the bottom apex, inward-rolled lip 12 mm |
| Edge band | 22 mm tall rubber protector over the seam |
| Stand | Walnut, three legs, felt-lined ring Ø 0.30 m (centre 0.654 m up, tilted 28° with the instrument: it meets the bottom cap 3 cm above the gu), feet spread Ø 0.46 m forward of the knees |
| Footprint | x ±0.265 m, z 0.146…0.614 m in the seat frame (the stand's feet z 0.20…0.62) |

### 7.2 Procedural geometry (`HandpanGeometry`, built once in `Init`; one `Mesh` per material)

- **Top cap.** A polar grid in the map: 64 rings × 192 segments, vertex spacing ≤ 6 mm at the fields.
  - Vertices are displaced by `relief(X, Z)` along the cap normal, with analytic normals from the relief's gradient.
  - The field boundary has a subtle 0.5 mm raised shoulder line, the "tone ring" a hammer leaves.
  - Uses `capPoint` from `handpan-layout.mjs`.
- **Bottom cap.** 32 × 128, with the gu hole and the rolled lip (a torus segment, 12 × 64); the inside is dark.
- **Edge band.** A lathe of 192 × 6.
- **Stand.** 3 legs (8-sided tapered cylinders), the ring (a torus 8 × 48) and the felt (a flat ring).
- **Totals:** ≈ 24.6 k + 8 k + 2.3 k + 2.5 k ≈ **37 k triangles** (budget 300 k for the scene).
- **Contact surface (E1/E3).** `IContactSurface.QueryContact` is the cap + relief: Point = the foot of the query on
  the relieved surface, Normal = the cap normal, `Use = Both` on the top shell, Shadow only on the edge band.
  Allocation-free.

### 7.3 Materials (URP Lit, SRP Batcher compatible, no MaterialPropertyBlock; textures baked by the build, ASTC)

| Draw | Material | Look |
|---|---|---|
| 1 | **Nitrided steel** (`MusicRoom/HandpanSteel`, a Lit variant with a 10-float uniform array for the shimmer) | Base colour dark bronze-brown `#3a2f27` (linear 0.042/0.028/0.021), metallic 0.85, smoothness 0.55. A 1024² baked normal map of hammer dents (Worley cells 3–6 mm, depth by cell, denser in the fields). Smoothness +0.08 in the dimples (burnished by playing) and −0.1 in a soft ring at each field edge. A faint heat tint (warmer brown, +5 % red) around the dimples. Optional engraved note letters on the shoulder below each field (darker patina, 40 % contrast, 9 mm letters: real makers engrave), tied to the piano's note-label setting. |
| 2 | **Edge band** | Black-brown rubber `#1d1a18`, smoothness 0.25 |
| 3 | **Stand** | Walnut (the room's wood atlas, oil finish, smoothness 0.45) with an undyed felt ring (the kick beater's felt material) in one atlas |
| (4) | VR floor contact blob (multiply quad, shared with the other rigs' blobs) | |

Draws: **3** (4 with the blob) ≤ 6. There are no transparent materials and no shadow casters in VR. In MR the shell
and the stand cast.

### 7.4 What moves when played (physical feedback; nothing glows)

1. **Fingers stop on the steel** (E1 clamp through `IContactSurface`), and soft contact shadows show under each pad
   (E3).
2. **The instrument settles on its felt ring:** a vertical spring-damper at 12 Hz, ζ 0.5, whose impulse is the
   stroke's momentum.
   - Peak dip: 0.3 mm for a ff fingertip, 1.5 mm for a palm slap.
   - Written only while it moves (≤ 10 frames); the stand stays still.
   - For scale: a 4.6 kg shell on a felt ring deflecting ≈ 1 mm under its weight has that order of compliance.
3. **Shimmer:** the struck field's reflections tremble, with the normal tilted by `θ = 0.003·min(1, Level(i)/L_ref)`
   rad, animated at an incommensurate 23 Hz, masked per field by a vertex colour channel.
   - This is the real effect of a vibrating steel surface: ~50 µm at the fundamental gives ~3 mrad of tilt, so a
     highlight moves ~3 mm at 1 m.
   - Uniforms are written only while any field is above −60 dB.
4. **No colour change, emission, outline or scale pulse.** Lesson targets use the tutorial's own disc blocks
   (section 8.1), like the drums.

### 7.5 Placement and rig

- `RigId.Handpan`, `Rig.Group` on the floor. HomeSingle = seat (0, 0, 0.38), yaw 0. The instrument's rim centre is
  0.72 m up with a 28° tilt (`PLACE`), carried by the stand.
- `Rig.Handles` are the edge band and the stand (never the steel): a pinch on a field is play, `PlaySurface.Handpan`.
- Zone: the group's box + 0.12 m. Arrange lift: −0.10…+0.15 m.
- All mode: open question 1 (section 11).

---------------------------------------------------------------------------------------------------------------------------

## 8. Lessons and Sunny

### 8.1 Lessons (`out/handpan-lessons.json`, exactly the `lessons.json` lesson schema; targets `{i, midi, note}`)

| id | Title | bpm | Notes | Length (s) | With countdown + lead | Fields | Shortest gap | Worst case (2 s wait per note) |
|---|---|---|---|---|---|---|---|---|
| `handpan-first` | First notes | 72 | 8 | 7.5 | **12.2 s** | D3 A3 C4 | 0.83 s | **20.7 s**: a guaranteed 30-second win |
| `handpan-ode` | Ode to Joy | 84 | 30 | 22.9 | 27.5 | C4 D4 E4 F4 G4 | 0.36 s (the dotted figure) | 64.7 s |
| `handpan-twinkle` | Twinkle Twinkle | 84 | 42 | 34.3 | 39.0 | C4 D4 E4 F4 G4 A4 | 0.71 s | 88.7 s |

- **First notes.** Its tune is `D3 A3 D3 C4 D3 A3 C4 D3:2`: the ding and the two nearest fields, alternating hands. It
  is an original exercise (no melody), dedicated to the public domain.
- **Ode to Joy.** Beethoven, Symphony No. 9 (1824), public domain. The audited lesson melody (`ODE_8`), which
  `lessons.mjs` checks note for note and rhythm for rhythm against `piano-ode`. It has 0 same-side steps: every step
  changes hands.
- **Twinkle Twinkle.** "Ah ! vous dirai-je, maman" (1761), public domain. Identical to the audited `piano-twinkle`.
- Hints: "Tap the ding", "Tap E4". The tutorial's TRAVEL for the handpan is 0.30 m (lead 1.667 s, like the harp).
- **Blocks (revision 2).** A disc 60 % of the dimple's width, oriented to the field normal. It comes in along the
  player's line of sight, as the harp's capsules do (Tutorial.cs), with a forward clamp:

  ```
  apex = FieldCentre(i) (the dimple apex);  dir = normalize(eyes - apex);  fwd = horizontal direction eyes -> instrument
  zf(p) = dot(p - eyes, fwd)                (distance in front of the eyes' plane)
  room  = max(0, min(TRAVEL, |eyes - apex| - 0.26, dot(dir, fwd) < 0 ? (zf(apex) - 0.16) / -dot(dir, fwd) : inf))
  pos   = apex + dir * min(dist, room)      (dist = remaining time x SPEED, as for every instrument)
  ```

  From the seat a block always sits over its own field while it approaches, and it waits at its start until its
  distance comes inside `room`. Revision 1 moved it 0.30 m along the field normal, which tilts 53° toward the player
  on the near fields: the A3 and C4 blocks started behind the eyes' plane at chest height.

| Field | D3 | A3 / C4 | D4 / E4 | F4 / G4 | A4 / C5 |
|---|---|---|---|---|---|
| Travel `room` (m) | 0.234 | 0.135 | 0.255 | 0.30 | 0.30 |
| Start: distance to the eyes / in front of the eyes' plane (m) | 0.26 / 0.17 | 0.41 / 0.16 | 0.31 / 0.16 | 0.29 / 0.20 | 0.30 / 0.25 |
| The harp rule alone: in front of the eyes' plane (m) | 0.17 | **0.10** | **0.14** | 0.20 | 0.25 |

  **EditMode test `HandpanBlockStartsInFrontOfThePlayer`:** for every field, at the seat pose, the start point is
  ≥ 0.25 m from the eyes and > 0.15 m in front of their plane (`handpan-lessons.json` `tutorial.blockPaths` holds the
  expected points within 1 mm).
- Done cadence (tutorials-audit B6b): a ding + A3 + A4 roll.

### 8.2 Sunny tips (sunny-tour.md section 3; lines ≤ 10 words, no brand names)

| Tip id | Title | Trigger | Line `text` (words) | Demo @ anchor | Success | Class |
|---|---|---|---|---|---|---|
| `tip_handpan` | Play the handpan | Handpan in the scene, gaze on it 1.5 s, never played | "Tap the round steel drum gently with one finger." (9) | Strike @ 8 cm above A3's dimple | `handpan` strike | P2 the first time it appears, else P3 |
| `tip_handpan_lift` (new) | Let it ring | `HandpanPlay` emits `hint tip_handpan_lift`: the 3rd sounded note muted by its own finger within 0.5 s | "Tap, then lift quickly so the note rings." (8) | Strike with a quick lift @ C4 | 3 strikes in a row not muted within 0.5 s | P2 (a note that dies at once reads as a broken instrument) |
| `tip_handpan_ding` | The ding | 5 ring strikes and the ding never struck | "The deep note in the middle is the ding." (9) | Strike @ the ding | Ding strike | P3 |
| `tip_handpan_gentle` | Gentle taps | 3 strikes at vel > 0.9 within 10 s | "Softer taps ring longer and sound sweeter." (7) | Strike (slow) @ C4 | 3 strikes with vel < 0.6 | P3 |
| `tip_handpan_spot` | Sweet spot | 10 strikes, all with ρ < 0.15 (dimple centre) | "Tap near a dimple's edge for a richer note." (9) | Strike @ (u 0.35) of A3 | A strike with 0.25 < ρ < 0.6 | P3 |
| `tip_handpan_mute` | Quiet it | ≥ 4 fields ringing above −40 dB for 3 s, the 2nd time | "Rest your palm on the steel to quiet it." (8) | PalmRest @ the ding | `handpanDamp` palm | P3 |
| `tip_handpan_slap` | Bass slap | 20 strikes, never a slap | "Slap the middle with a flat hand for a boom." (9) | **Slap** (new) @ the ding | Slap on the ding | P3 |
| `tip_handpan_tak` | Tak | 30 strikes, never a tak | "Tap between the notes for a soft click." (8) | Strike @ the steel between A3 and C4 | Tak | P3 |
| `tip_handpan_loop` | Play over a loop | A loop is playing, handpan current, never played during a loop | "Play the handpan over your loop. It's in tune." (9) | none (Sunny faces the handpan) | A strike during a loop | P3 |

- **New demo kind `Slap`:** an open hand, fingers together, palm down; one 5 cm drop at t0 = 0.4 s and a 0.3 s rest,
  anchored 6 cm above the ding.
- The `spoken` text equals the `text`, except "handpan", which is spoken "hand pan".
- Feature ids (FeatureCatalog): `handpan.strike`, `handpan.lift`, `handpan.ding`, `handpan.gentle` (Passive: a
  technique hint), `handpan.spot`, `handpan.mute`, `handpan.slap`, `handpan.tak`, `handpan.loop`.
- `[Feature]` attributes go on `HandpanPlay`'s classifiers; the menu row is covered by `tip_instruments`.
- Poke Sunny to skip works as for every tip (sunny-tour.md section 4); a skip marks the tip seen.

---------------------------------------------------------------------------------------------------------------------------

## 9. Tests for the Unity build

### 9.1 DSP test vectors (EditMode, `OfflineEngine(UnitId.Handpan)`, 256-frame blocks; `Reference~/test-vectors.json`; calm unless noted)

| Vector | Setup | Expected (strict; Burst within 1e-5 per sample) |
|---|---|---|
| Table | `HandpanTablesCalm` lanes (pr, pi, go, gi) | FNV-1a `0x710fee98` (measured: `0xe1762336`). Samples: lane 0 pr 0.999791086, pi 0.0192186646, go 0.144990832; lane 1 gi 0.666913688; lane 3 (ding U0) gi 0.0177827943; NL lane 12 gi 0.00456708996; SYM lane 16 gi 0.000159110903; cavity lane 212 pr 0.999549866 pi 0.011953 |
| strikeParams | vel 0.5 Tip | W1 48, W2 33, A1 1.69489038, A2 1.51098728, c1 0.991444886, s1 0.130526185; vel 0.7 Slap: W1 293, W2 199 |
| Shapes | A3 (0.35, 0) | T1 0.830554843, T2 0.987335563, T3 −0.303806454; `j0p(0.25)` 0.669865131, `j1p(0.2304)` 2.08318162 |
| **TV1** | A3 vel 0.6 (0.35, 0.2) Tip at frame 100, 96000 frames | Frames 100…103: 0, 0.000325642643, 0.00158181367, 0.00424718065; [1000] 0.263439298; [24000] −0.127631262; peak 0.333536297; FNV `0x54b36893`; rms 0–4800 −17.865 dB, 48000–96000 −36.663 dB; frames 0–99 exactly 0; fused vs strict ≤ 1.11e-6 |
| TV2 | Ding vel 0.8 Slap at 0, 48000 | FNV `0x092874c6`; peak 0.742330074; rms 0–2400 −8.943 dB |
| TV3 | `StrikeTak(0.6, 0, −0.19)` at 37, 24000 | Posts 3 commands at 37: (N 9, A 1.98268664), (N 1, A 1.18792534 = A1·0.599149303), (N 2, A 0.992079258 = A1·0.500371177), all T Tak, W1 45, W2 31; FNV `0x919ef5f8`; rms 14400–24000 −48.29 dB (the neighbours' hum). Strike(9) alone: FNV `0x599d9379`, rms 14400–24000 −100.59 dB |
| TV4 | E4 vel 0.7, palm Damp slot 3 at 24000 | rms 23040–24000 −21.917 dB, 26400–28800 −43.236 dB; FNV `0xc66a2fce` |
| TV5 | C4 hits at 1000 (vel 0.5) and 1010 (vel 0.9) | The second starts at 1048; FNV `0xc0a3f911`; a third hit at 1012 (vel 0.3) merges: same FNV, `merged` 1 |
| TV6 | Ding vel 0.7 | SYM energy dB at frame 24064: D4 −34.11, A4 −49.92, A3 −61.41, D3 −300 (no self-drive) |
| TV7 | Reset at 12000 | Every sample after it 0, 0 active groups |
| TV8 | Budget 400 | FNV `0x48b2338f` |
| TV9 | Every field struck, 60 s | 0 active groups, 0 subnormals |
| TV10 | Block 64 vs 256 | Differ by ≤ 2.3e-4 (block-end pole updates): the device and the tests use 256 |
| TV11 | Measured voice, TV1's strike | Frames 100…103: 0, 0.000324917608, 0.00157822727, 0.00423730118; peak 0.332865745; FNV `0xaa1cd487`; rms 48000–96000 −37.414 dB |
| TV12 | `HandpanMath.TakNeighbours` | (0, −0.19): A3 d 0.0128061 w 0.599149, C4 0.0173101 / 0.500371; (−0.0905, −0.0375): ding 0.539, D4 0.524; (0.18, 0): E4 0.452, G4 0.388; (0, 0.18): A4 0.353, C5 0.309; (0, −0.095): ding 0.607, A3 0.342 |
| Null | Node render vs Unity offline render of `handpan-singles` (dry) | `NullTest.ResidualDb` ≤ −70 dB (Burst) |
| Perf | Worst case of section 4, Stopwatch over 400 blocks, Burst on | Log `[PERF] handpan avg/max µs`; editor value ≤ 0.5 × the Node value |

### 9.2 Interaction cases for scripted hands (`Reference~/interaction-vectors.json`: 34 cases, 90 Hz frames; both hands' 5 tips, radii, palm centre and normal, flags; no sample interval)

| Case | Expected events (time s, field, velocity) |
|---|---|
| `tap-A3` | 0.1556 strike A3 v0.635 tip |
| `soft-tap-ding` (0.15 m/s) | 0.400 strike ding v0.079 |
| `too-slow` (0.05 m/s placement) | No strike; 1.1778 damp E4 finger slot 1; 1.6667 release |
| `hover-jitter` (±3 mm at +5 mm after a strike) | One strike, no retrigger |
| `repeat-6hz` | 6 strikes 0.133 s apart, v0.731 |
| `two-fingers-one-hand` (22 ms apart) | 1 strike (D4) |
| `two-hands-flam` (11 ms apart) | 2 strikes (G4) |
| `tak-between` | Strike field 9 v0.508 tak, u/v = map point, neighbours A3 0.599149 / C4 0.500371 |
| `ding-slap` (flat hand 0.9 m/s) | Exactly 1 slap (ding v0.578), 0 fingertip strikes |
| `palm-rest` | 0.5333 damp ding palm slot 3 + shell; 1.3889 releases |
| `tracking-loss` (finger placed slowly, then lost) | 0.5667 damp; release 0.30 s after the loss; no strike on return under the steel |
| `brush` (tangential 0.9 m/s) | None |
| `outside-rim` | None |
| `field-edge` (C5, ρ 0.95) | Strike C5 |
| `teleport` (a 20 cm one-frame glitch mid-stroke) | 1 strike (A4) |
| `predicted-90hz` / `tracker-30hz` | 1 strike F4 v0.635 each |
| `press-0.15s` | 1 strike C4, no damp |
| `press-0.5s` (still at −4 mm) | 1 strike; 0.5667 damp C4 `afterStrike`; release on lifting |
| `press-deep-0.5s` (−15 mm) | 1 strike, no damp |
| `lift-hint` | 3 strikes, 3 `afterStrike` damps, `hint tip_handpan_lift` at 2.5 s (once) |
| `palms-down-tap-0.3` / `-0.6` / `-1` | 1 strike each at the crossing frame (delay 0): v0.287 / 0.589 / 0.836 |
| `flat-slap-lag30` (palm 30 mm behind the fingers, 0.4 m/s) | 1 slap, 0 fingertip strikes |
| `tap-A3-jitter-{0.5,1,2}mm` | 1 strike each (v0.589 / 0.589 / 0.591) |
| `too-slow-jitter-{0.5,1,2}mm` | No strike; damp at 0.7778 / 0.7889 / none (this seed at 2 mm) |
| `hover-6mm-jitter-{0.5,1,2}mm` | None |

- EditMode: feed the frames to `HandpanPlay.Step` and compare events (time ±1 frame, velocity ±0.005, same fields,
  slots, flags and neighbours).
- PlayMode: the same cases through `FakeHandSource` → `PointerInput` → the module, asserting the recorder's `handpan`
  events, and 0 B GC per frame in `Tick` (`Is.Not.AllocatingGCMemory()`).
- EditMode `HandpanBlockStartsInFrontOfThePlayer` (8.1).

### 9.3 On-device checks (development build)

1. **Feel (T-HP-FEEL).** Tap all 9 fields at soft, medium and hard; tap between fields; slap the ding; rest a palm;
   tap and leave the finger down. Expect:
   - a strike for every intentional tap, none from hovering;
   - slaps that never double with fingertips;
   - a deliberate mute within ~0.1 s;
   - a tap-and-hold that keeps ringing for at least 0.4 s.
   Log `[Handpan] strike f v vn` lines.
2. **Velocity range and jitter (T-HP-VEL).** Ask a first-time user to play "softly" and "firmly". Expect a logged vn of
   0.2–0.4 m/s and 0.6–1.0 m/s; if firm taps rarely exceed 0.7 m/s, lower `V1` to 0.85. Also log the SD of the 45 ms
   slope of a resting index (2 s still, 3 times). Above 0.05 m/s, lengthen `VEL_WIN_S` to 0.056 s (lab: SD −22 %, ff
   taps read 0.72 instead of 0.84).
3. **Aim (T-AIM).** Tap A4 and C5 (the smallest, farthest fields) 20 times each. Expect ≥ 95 % on the intended field.
   Log the crossing ρ.
4. **Headroom.** A 10-finger ff roll plus a ding slap. Expect the master limiter's GR ≤ 6 dB in the `[Audio]` line and
   no crackle.
5. **CPU (T-STRESS + handpan).** The worst case of section 4 with passthrough on. Expect DspLoadMeter p99 ≤ 1.2 ms
   (unchanged ± 0.06 ms), 0 gaps.
6. **Comfort.** Three seated testers (short, medium, tall). Expect no thigh contact on near-field taps and no
   shoulder strain on A4 and C5. Log the shoulder-to-field reach from the hand poses.
7. **Lesson blocks.** Run "First notes" seated: every block visible over its field from its first frame, none at the
   chin or chest.
8. **MR.** Passthrough hands visible over the steel, shadows on the floor, the stand not inside the chair (UNVERIFIED:
   the chair is not tracked).
9. **Classic sound.** Toggle it while a note rings. Expect the handpan to keep playing (not suspended) and no click.
10. **Looper.** Record 2 bars of the evening groove at 90 bpm and loop 20 passes. Expect no drift (sample-accurate
    `ScheduleAt`), taks with their neighbours, and damps replayed.
11. **Listening (9.4).**

### 9.4 Listening (the user decides)

`Builds/Listen/handpan/handpan-README.txt` explains each render. Ask:
- **The A/B:** calm or measured (`handpan-singles.wav` vs `handpan-singles-measured.wav`). Then set
  `HandpanTables.Default`, and re-render the pieces in that voice if it is "measured".
- Clicks, tuning, bell-like rings, boom, and whether the taks sound dry or pitched.

---------------------------------------------------------------------------------------------------------------------------

## 10. Renders (`Builds/Listen/handpan/`, 48 kHz stereo 24-bit, −18 LUFS integrated per file, true peak ≤ −1 dBTP)

| File | s | LUFS | dBTP | What |
|---|---|---|---|---|
| `handpan-singles.wav` | 22.6 | −18.0 | −5.2 | The ding and the 8 ring notes at mf, 2.2 s apart: the **calm** voice |
| `handpan-singles-measured.wav` | 22.6 | −18.0 | −5.3 | The same notes in the **measured** voice (the A/B, 3.6) |
| `handpan-velocity-ladder.wav` | 24.6 | −18.0 | −3.0 | A3 then the ding, vel 0.08…1.0, 1.5 s apart (whole-file gain: the dynamics are kept) |
| `handpan-techniques.wav` | 30.5 | −18.0 | −1.1 | Centre / octave spot / fifth spot / edge, taks (between A3 and C4, then by the ding), ding tip and slap, a muted slap, finger and palm mutes, harmonic isolation, a roll |
| `handpan-ode-to-joy.wav` | 25.8 | −18.0 | −4.2 | Beethoven, public domain; the lesson melody with soft ding accents |
| `handpan-evening-groove.wav` | 27.2 | −18.0 | −6.3 | An original 4-bar ding-tak pattern (public domain dedication), 88 bpm |
| `handpan-ode-to-joy-room.wav` | 25.8 | −18.0 | −3.8 | In the VR room (room lab v2 "natural") and MasterBusV2, from the seat: limiter 0 dB, leveller −0.3 dB |
| `handpan-evening-groove-room.wav` | 27.2 | −18.0 | −6.3 | Same; leveller −1.0 dB |

- Total 59.4 MB (< 60 MB). Each file is matched to −18 LUFS as a whole, never note by note (the critics' loudness
  fairness). The A/B pair has identical notes and timing, and its two files differ in gain by 0.1 dB.
- The dry files are dual mono. The room files use the room lab's direct path (distance, Woodworth ITD, Brown–Duda head
  shadow) and its engine, with the handpan as the front ER group at its seat position (0.72 m rim).
- Revision 2 re-rendered everything: no ping, the new tak, the new placement (room files), the A/B.

---------------------------------------------------------------------------------------------------------------------------

## 11. Risks, open questions, UNVERIFIED

1. **All-mode layout (open).** The piano spans the front at 0.45 m and the kit and harp take the sides, so there is no
   free spot within reach for a 0.53 m instrument.
   - Proposal: the handpan (and this batch's other new instruments) live in Single mode, reached by the page-turn swipe
     and the Instruments list.
   - The layout owner (u-arrange) decides whether All mode becomes a choice of three instruments.
   - Footprint for that decision: x ±0.265, z 0.146…0.614 m (stand z 0.20…0.62).
2. **The voice (open, the user's).** Calm or measured (3.6, 9.4). Both pass every gate; only the beating differs.
3. **Contact times** (0.8–1.6 ms fingertip, 5–11 ms palm) are design values tuned to reproduce Alon's partial levels,
   not a measured finger force (no published fingertip–handpan contact measurement was found). Device and ear check:
   9.3 step 1.
4. **Sympathetic, nonlinear and tak-neighbour levels** are design targets: Morrison & Rossing show the coupling but give
   no levels, and Alon & Murphy show that interstitial strikes ring the fields but give no level.
   - The halo is 18–40 dB under the note; the bloom is at ff only.
   - The tak's neighbours sit at −27 to −35 dB re its peak.
5. **Hand-tracking jitter and velocity** (6.7): the windows and V0 assume about 1 mm of frame-to-frame jitter and a
   tracker that reports gentle 0.3–1 m/s taps. Neither is published for Quest 3S (9.3 step 2). At 2 mm, 5 % of soft taps
   are missed and slow placements give 11 false strikes per 100.
   - Fast Motion Mode (E4) is requested only while drumming: tapping the handpan is gentle, and FMM adds jitter (Meta),
     so leave it off here.
6. **Soft taps are quieter than in revision 1** (V0 0.08 → 0.12 m/s): a 0.15 m/s tap plays at vel 0.08 (was 0.15), and
   a fast 1 m/s tap reads 0.84 (was 1.0). If testers find the instrument hard to play softly, lower V0 to 0.10. That
   raises false strikes in slow placements at 1 mm from 0.3 to 1.7 per 100, still inside the criterion (lab sweep).
7. **Thigh clearance** of the near fields (+2.5 cm over a 95th-percentile thigh) and reach depend on body size (9.3
   step 6). Arrange mode lifts the rig.
8. **Worst-case peak** +11.3 dBFS if 10 fingers land within 4 ms: the master limiter handles it (9.3 step 4); a
   per-unit soft clip is not added (it would break the null tests).
9. **Burst version** of the unit's own loop (pulses, NL polynomial, bus, tak sum) is new code. It is all float4-friendly
   scalar work outside the MB4 kernels. T-KERNEL-style timing: 9.1 Perf.
10. **Shared-file changes** (5.5) are unavoidable for a new role and source. They are listed so the integrator can apply
    them once for the whole batch of new instruments.

---------------------------------------------------------------------------------------------------------------------------

## 12. Sources (numbers used as numbers; no recordings or samples were used)

- Morrison, A., Rossing, T. D. (2007). "Modes of vibration and sound radiation from the HANG." *Archives of
  Acoustics* 32(3), 551–560 ([PDF](https://acoustics.ippt.pan.pl/index.php/aa/article/download/709/627)).
  - The G3 central note's (0,1), (1,1)a and (1,1)b modes at 189, 390 and 593 Hz (≈ 1:2:3); the (2,1)a/b modes at 1418
    and 1543 Hz (7.50, 8.16 f0: the lowest upper modes used here).
  - D4's second resonance at 604 Hz; 7–9 notes around a central note coupled to the cavity; the gu.
  - Coupling: at 348 Hz the F4 note is strongly driven; it grows with amplitude.
  - Intensity: monopole at the fundamental and the 2nd mode, dipole at the 3rd.
- Rossing, T. D., Morrison, A., Hansen, U., Rohner, F., Schärer, S. (2007). "Acoustics of the HANG: a hand-played
  steel instrument." Proc. ISMA 2007, Barcelona ([abstract](https://www.hangblog.org/acoustics-of-the-hang-a-hand-played-steel-instrument)).
- Wessel, D., Morrison, A., Rossing, T. D. (2008). "Sound of the Hang." *Proc. Meetings on Acoustics* 4, 035002,
  doi 10.1121/1.3068630 ([ASA press summary](https://acoustics.org/pressroom/httpdocs/155th/wessel.htm)).
  - A cavity peak at 85 Hz (near F2) in every note's spectrum.
  - Soft notes: two components an octave apart. Loud notes: inharmonic higher components and amplitude modulation
    "due to beating"; they ring longer.
  - Longer contact damps more. "Many beginning players have difficulty striking the instrument and pulling away
    quickly" (the reason for the 0.40 s rule and `tip_handpan_lift`).
- Alon, E. (2015). *Analysis and synthesis of the handpan sound.* MSc thesis, University of York
  ([etheses 12260](https://etheses.whiterose.ac.uk/12260/)).
  - Table 4.1: peak levels and ratios of 8 fields (the medians used in 3.2).
  - Table 4.2 and Appendix F: T60s (means 2.1–3.3 s per instrument, 0.9–5.9 s).
  - Section 4.3: the D/O/P sweet spots, octave +5 dB at O, fifth +7 dB at P, and the fundamental's decay independent
    of position.
  - Sections 2.1.4–2.1.6: steelpan nonlinear coupling and sympathetic pairs, harmonic isolation, percussive
    (interstitial) strikes, Helmholtz activation.
- Alon, E., Murphy, D. (2015). "Analysis and resynthesis of the handpan sound." Proc. SMC 2015
  ([zenodo 851153](https://zenodo.org/records/851153)).
  - Table 2: the three strongest partials just after the onset are harmonic on all 8 measured fields (undamped D3:
    152, 298, 592 Hz). This is the 3.9 gate.
  - Table 4: AM at 3.1–8.6 Hz from a slight mistuning of partials on separate fields. Adding AM raised the resynthesis's
    median similarity from 5.1 to 6.75 out of 10.
  - Octave ratios 1.96–1.99 and fifth ratios 2.94–2.98: the measured voice's ranges.
  - Section 3.2: every field and the port had to be covered to isolate an interstitial strike, i.e. a tak rings the
    fields.
- Abdlkarim, D., et al. (2024). "A methodological framework to assess the accuracy of virtual reality hand-tracking
  systems." *Behavior Research Methods* ([PMC10830632](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC10830632/)):
  Quest 2 fingertip error 1.1 cm, delay 45 ms (6.7).
- Morrison, A., Rossing, T. D. (2009). "The extraordinary sound of the hang." *Physics Today* 62(3), 66
  ([link](https://physicstoday.aip.org/quick-study/the-extraordinary-sound-of-the-hang)): 8–9 notes, the centre note
  "usually an octave below the lowest of the circumferential notes", 1:2:3.
- Monteil, M., Thomas, O., Touzé, C. (2015). "Identification of mode couplings in nonlinear vibrations of the
  steelpan." *Applied Acoustics* 89, 1–15, doi 10.1016/j.apacoust.2014.08.008; and ICSV 2012 / ENOC 2014
  ([PDF](https://perso.ensta-paris.fr/~touze/PDF/ENOC2014_ThomasMonteilTouze.pdf)): 1:2 and 1:2:4 internal resonances,
  and the energy cascade from the fundamental.
- Achong, A. (1996). "The steelpan as a system of non-linear mode-localized oscillators, I." *J. Sound Vib.* 197(4),
  471–487 (via Alon 2.1.4): sympathetic pairs, the soft-spring pitch rise after a hard strike, and the apex as the best
  single impact point.
- Fletcher, N. H., Rossing, T. D. (1998). *The Physics of Musical Instruments*, Springer: the Helmholtz resonator and
  end corrections, and the clamped-plate Bessel zeros 2.405 and 3.832 used for the mode shapes.
- Seated anthropometry for thigh clearance (critic's `c4-geometry.mjs`: Pheasant, *Bodyspace*): thigh clearance
  0.13 / 0.155–0.17 / 0.19 m (5th female / median / 95th male) over a 0.43–0.48 m seat.
- ITU-R BS.1770-4 (loudness and true peak). Internal: `10-upgrade/burst.md`, `ENGINE-API.md` (including section 10,
  AudioSchedule), `room.md`, `harp.md`, `drums.md`, `experience.md`, `02-meta-hands.md`, `11-first-five/*`, the app's
  `Lessons/Tutorial.cs` (the harp's line-of-sight blocks), `Hands/HandFrame.cs` and `Core/Input/PointerInput.cs`.
