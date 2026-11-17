# 12 · New instruments · Singing bowls (key `bowls`): implementation-ready spec, revision 2

Three singing bowls on ring cushions on a small walnut stand beside the player's left knee: **big G3, middle C4, small
E4**. Hands only:
- tap with a fingertip;
- strike with the palm;
- **circle a fingertip around the outside of the rim until the bowl sings**;
- rest the palm (or two fingers) on a bowl to quiet it;
- or pick up the puja (the bowl stick), strike with its suede end and rub with its wooden shaft.

Every sound comes from a live physical model on the Burst audio engine (ENGINE-API.md): split (degenerate) mode
pairs, a stick-slip friction contact that travels with the finger, and real touch damping. There are no samples and no
third-party assets.

This revision is written **against `platform.md`** (the shared contract) and replaces revision 1 entirely. The bowls
agent changes **no shared file**: everything is in the bowls folders (platform 13.2), and every shared need is a
`CONTRACT-REQUESTS.md` item (section 3.12).

| | |
|---|---|
| Lab (Node 26, no packages) | the bowls lab (Node prototype; its vectors are frozen in `Tests/EditMode/Audio/Bowls/Reference~/`) |
| Reference engine to transliterate | `bowls-engine.mjs` (float32 SoA state, canonical `mad` order, no allocation in `renderBlock`) and `bowls-design.mjs` (the tables) |
| Interaction reference | `interaction.mjs` (`BowlsHands`), its cases `interaction-cases.mjs` (25 cases, all pass) and the scripted-hand harness `lib/hands-sim.mjs` |
| Recording reference | `rub-encoder.mjs` (`RubEncoder`, `decode`) and `lanes.mjs` |
| Rig geometry | `layout.mjs` (`RIG`: stand, bowls, puja, seat spec), checked by `tools/seat-check.mjs` against the platform's own planner |
| Renders | `Builds/Listen/bowls/bowls-*.wav` (12 files, 59.3 MB) and `bowls-README.txt` |
| Lesson pack | `out/lessons-bowls.json` → `Assets/MusicRoom/Bowls/Resources/LessonPacks/bowls.json` |
| Objective results | `out/verify.json` (35 checks, all pass), `out/verify-inacio.json` (9 checks against the paper's own bowl and puja, all pass), `out/interaction-cases.json` (25 cases, all pass), `out/conformance.json` (platform 10.3), `out/lanes.json`, `out/seat-check.json`, `out/test-vectors.json` (16 vectors), `out/bench.json`, `out/rub-encoder-vectors.json` |

**Nobody has listened yet.** Every judgement in this spec comes from measurement. The user decides by ear (section 12).

---------------------------------------------------------------------------------------------------------------------------

## What changed in revision 2 (the independent critique)

Every high and medium item was fixed. Each fix was confirmed by re-running the critic's own scripts
(the lab's `critic/*.mjs`, untouched) on the revised code, plus new checks of our own.

| Critique item | Fix | Evidence (before → after) |
|---|---|---|
| **H1** The spec ignored `platform.md` | Rewritten against it. **ids:** Kind/Rig/Unit 9, the one `Bowls` source. **Descriptor:** `BowlsDefinition` with every field (3.2), the window-sill display (5.6), the puja as the platform's `RubStick`/`Striker` tool (4.6). **Recording:** `bowlRubOn/Ctl/Off` on lanes through a ControlStream-style encoder (3.7). **Lessons and tips:** a lesson pack with `i`/`mode`/`hold` (7.1), a P2 arrival tip (7.2). **Draws:** 5 at the seat, 3 on display, the shadow from `ShadowSpec` (5.3). **Conformance:** platform 10.3 run in full (section 9). **Shared files:** none changed; S1–S5 of revision 1 are gone (`FollowsClassic` was redundant: the platform's unit-only roles ignore Classic sound) | `tools/conformance.mjs`, `tools/seat-check.mjs` |
| **H2** A gentle or lingering tap gave a silent tick and then choked the bowl | Tap speed is the **peak inward normal speed over the last 3 tracker samples**, and `TAP_SPEED` drops to **0.12 m/s**. A touching tip becomes a rub only when it **moves along the rim** (≥ 0.05 m/s for 0.1 s). A still fingertip **never damps within 0.6 s** of its entry; after that (and 0.3 s still) it is `Damp(1)` only, and the full stop is the palm | **Critic `pokes.mjs`** (30 Hz, 0.3 m/s): touch-instead 57–60 / 60 → **0 / 60** everywhere; measured / peak speed 0.53–0.7 → **0.62–1.32**. **`gentle.mjs`:** a 0.3 m/s poke −56 LUFS → **−38 LUFS**; a 100 ms dwell now rings the same as none. **`hands.mjs` tap-and-hold:** −15 / −56 dB → **0 / 0 dB**. **Ours:** 108 pokes (0.2–0.5 m/s, 1–4 cm, 0–250 ms) give 1 strike each with V ≥ 0.94 × the poke speed, the ring within 0.9 dB |
| **H3** A second fingertip silently stopped a rub | While a bowl is rubbed, the other fingertips on it **do not damp** unless still and ≥ 8 mm deep for > 0.4 s. The rubbing hand's other tips **land silently**. `TOUCH_ALPHA[1]` drops from 3 to **0.8 s⁻¹** | **Critic `probes.mjs` A:** Damp(1) on the big bowl 0.018 → **0.678 m/s**. **`hands.mjs` case 3:** the big and middle bowls with a second tip never sang → **sing after 0.19–0.67 s**. **Ours:** 15 hand shapes (two-finger, flat hand, curled middle, 5 mm jitter) × 3 bowls all sing within 1.1 s |
| **H4** Placement collided with the seated player (knees, thighs) | A new **0.41 × 0.29 m stand, top at 0.61 m, rims at 0.69–0.72 m**, in the pocket left of the knees: between the seat box and the side table behind, the piano's keybed ahead, and the harp's base outside, each ≥ 3.5 cm clear. `SeatSpec` with canonical slot FrontLeft and preferences; HomeSingle = HomeAll (never in front) | **The platform planner** (`tools/seat-check.mjs`): with the bowls, **26 of 28 sets fit**, and the classic three keep today's homes in every set containing them. **Reach:** every near rim is 0.40–0.51 m from the left shoulder, every far rim ≤ 0.62 m |
| **M1** Freehand circles kept lifting off and re-ticking | A rub contact is **sticky**: it holds 15 mm outside the surface (N at its floor) and for a 0.25 s lift hold beyond that. A re-entry within 0.4 s of a release continues with no strike; a tip re-landing within 0.3 s of its own exit gets no tick | **Critic `hands.mjs` case 2:** 5–9 releases and 0–98 % singing → **0 releases, 100 % singing, singing after 0.1–1.6 s**. **Ours:** 9 freehand paths × bowls all sing within 1.54 s with 1 release (the final lift) |
| **M2** Fast taps were point-sampled | A **swept entry test** (previous → current position every 2 mm against the outer wall, lip and inside) on the display-time prediction | **Critic `pokes.mjs`:** 2 m/s × 5 mm at 30 Hz missed 34 / 60 → **0 / 60**. **Ours:** 1–2 m/s at 30 Hz and 3 m/s at 60 Hz, 5–20 mm, **180 / 180** detected. 3 m/s at 30 Hz: 16–20 / 20 (**partly rejected**: those taps fall between two samples and leave no trace in the data, section 4.5) |
| **M3** Tap-then-rub dead spots | The cause was a race: the stick lands on the struck (2,0) antinode of a loud bowl and chatters, and that impact damping kills (2,0) so the friction locks onto the struck (3,0). Fix: **contact tissue loss**, `CONTACT_ALPHA` 1.5 s⁻¹ × (f/f0)^0.5 on pairs n ≥ 3 while a finger is on the rim (skin is lossy; the driven pair keeps its poles). The critic's candidate (no radial dynamics for 150 ms) was tried and only moved the failures (8–23 of 576) | **Sweep** (3 bowls × 48 angles × taps 1 / 1.5 × rubs 0.2 / 0.3 m/s): **13 / 576 → 0 / 576**. **Critic `probes2` G:** 4 / 90 → **0 / 90**; `probes3`: 7 / 288 → **0**; `probes4`: 9 / 1152 → **0**. The rub map is unchanged (8.6). `tip_bowls_tap_rub` ships: a tap-then-rub sings at once, a plain rub after 0.3–1.7 s |
| **M4** Rubbing was 5–10 LU louder than the piano | `OUT_GAIN` −3.5 dB (620 → 413.3) with the fingertip mass 20 → 30 g (+3.5 dB on every strike; the drum spec's finger is 28.6 g): strikes keep their level, rubs are 3.5 dB quieter. mf reference phrase defined (section 9) | mf phrase **−1.12 LU**; 0.3 m/s rub +4.96 → **+1.47 LU**; 0.45 m/s +8.51 → **+5.04 LU** (limits ±2, ≤ +3, ≤ +6) |
| **M5** The room send was too wet | Send **−1 dB VR / −7 dB MR** (request R1; the platform table says +2) | Seat DRR **+6.41 dB** against the piano's +6.08 (same method; target +6.1 ± 2). +2 dB would give +3.41 (fails) |
| **M6** The spec said renormalise the contact phasors; the reference did not | Renormalised at each block start, e ← e (3 − \|e\|²)/2, for the contact and target phasors (and the slewed pickup) | **Critic `probes` F / `probes2` J:** \|e\| − 1 after 60 s 1.7–3.3e-2 (growing) → **2–4e-6, flat over 180 s** |
| Also found by the critic's probes | **Live and loop on one bowl** (`loop.mjs`): contact lanes; the live hand takes the bowl, the loop's lane waits. **Landing rattle** (`probes2` H): it was the rub-noise generator following the impulsive contact force, so it now follows a 10 ms envelope of the friction power. **Lip, chatter, surface switch:** unchanged, within limits | **Lanes:** no interleaving; the handover step equals a plain lift-and-land (1.29 vs 1.32). **Noise:** the tap-then-rub render's worst step ratio 8.5 → 4.0 |

---------------------------------------------------------------------------------------------------------------------------

## 0. Summary

**Sound** (sections 2, 8).
- Each bowl has 8 mode pairs (n,0), n = 2..9: 16 complex modes = 4 MB4 groups.
- Each bowl copies one measured bowl's mode ratios and split pattern:
  - the big bowl, [I06] Table I bowl 1;
  - the middle bowl, the 21 cm bowl of [WSN02];
  - the small bowl, [I06] bowl 2.
- Decays come from Inácio's damping, raised for the cushion: fundamental T60 70 / 53 / 42 s.
- Rubbing is a friction contact solved per sample: a radial stick-tip spring plus a hyperbolic stick-slip law. The
  vibration pattern turns with the finger, which gives **4 beats per revolution**.
- With the paper's own bowl and puja the engine reproduces [I06]'s dynamics (9 / 9).

**Hands** (section 4).
- Fingertips are spheres tested against the outer wall, the lip and the inside.
- A tap is any entry at ≥ 0.12 m/s peak normal speed.
- A touch turns into a rub when the finger moves along the rim.
- Damping is gentle by default: one finger is a slow fade; two fingers or a palm stop the bowl.
- The puja, the one tool, strikes with its suede end and rubs with its shaft.
- 25 scripted cases at a 30 Hz tracker.

**Platform** (section 3).
- ids: Kind/Rig/Unit 9, one `Bowls` source.
- A full `InstrumentDescriptor`.
- Lanes for loops (live + 3 playback).
- Idle skip, and the `Share` work cap (−75 % with three bowls ringing).
- `FloatMode.Strict`, so the vectors are bit-exact under Burst.

**Cost** (section 10).
- About 3.7 µs per ringing bowl and 31 µs per rubbed bowl on Quest (Strict).
- 95 µs worst, with all three rubbed.
- Node 254 µs average, under 10× the platform cap.
- State 6.1 KiB; no baked audio.

**Look** (section 5).
- 5 draws at the seat: 3 hammered-bronze bowls, cushions, the stand with the puja. 3 on display.
- A struck bowl rocks slightly on its cushion. Nothing glows.

---------------------------------------------------------------------------------------------------------------------------

## 1. Decisions

| # | Decision | Why |
|---|---|---|
| D1 | Three bowls G3 / C4 / E4 (a C major triad), 175 / 150 / 125 mm, 0.80 / 0.50 / 0.32 kg | Consonant with the room's C-major harp and lessons. Each matches a measured bowl of that pitch class ([TB11] Tibet 2, 3/4; [I06] bowl 2) |
| D2 | Each bowl copies one measured bowl's ratios and splits | Three different bowls, not one transposed |
| D3 | Modal model (MB4 layout) plus a custom sample-outer contact loop | The friction force depends on the rim velocity at the moving contact every sample |
| D4 | **One unit `UnitId.Bowls` (9), one source `SourceId.Bowls`** at the bowls' centre; the unit's channel is the sum of the three bowls | The platform's id block (platform 2). The bowls sit within 0.2 m of each other at 0.64–0.79 m from the head: one point is honest enough, and the per-bowl pickup orientation still follows the head |
| D5 | Hyperbolic friction law, closed-form implicit solve | No transcendental per sample (burst.md 7); the shape of [I06] eq. 14 |
| D6 | Inner-wall rubbing never sings | [DA16], measured on real bowls |
| D7 | Pressure from penetration, 2.5–8 N | Tracking has no force sensing. The 2.5 N floor stops light-touch chatter at normal speeds |
| D8 | A pickup toward the listener plus a quadrature room fill (FILL 0.35) | The rotating pattern beats at the listener, 9.4 dB peak to trough |
| D9 | A strike is two half-sines (60 % over T, 40 % over 0.72 T) | No single contact-spectrum null can erase a partial |
| D10 | **Fingers first, the puja optional** | Hands-only first-timers touch at once. The platform's `RubStick`/`Striker` tool (one real puja: suede end + wooden shaft) is there for players who want a stick, and a held stick is immune to fingertip occlusion at the far rim |
| D11 | **Contact tissue loss on n ≥ 3** while a finger is on the rim | A fingertip is lossy, more so at high frequency. It removes the (3,0) lock-in after a tap (13 → 0 of 576) and leaves the rub map unchanged |
| D12 | **Gentle hands:** taps from 0.12 m/s, no damping for 0.6 s after a touch, one finger = Damp(1) | First-timers poke slowly and leave the finger on. The quick stop stays physical: a palm, two fingers, or a rubbing stick that stops |
| D13 | **Contact lanes, one stick per bowl** | A bowl has one stick. The live hand takes a looped bowl; the loop's lane waits. No interleaved commands, no chatter |
| D14 | **`FloatMode.Strict`** for the bowls DSP | The platform's determinism gate (1e-5 under Burst). Fused math drifts 1e-4 on long free rings and 1e-3 on a nonlinear contact. Strict costs about 1.3–1.6×, still 0.095 ms worst |

---------------------------------------------------------------------------------------------------------------------------

## 2. The sound model

### 2.1 Signal flow (per bowl, per 256-frame block; split at events to the sample)

```
main thread: BowlsModule (hands, puja, playback) -> IBowlsSound -> GraphCommand on the unit's ring
audio thread: UnitHooks.RenderBowls(u) (FloatMode.Strict)
  block start: groups to render from Budget/1200 x Share (4 / 3 / 2 / 1); slew the listener pickup (<= 0.005 rad/block,
               renormalised) and rebuild goI/goR if it moved; renormalise the contact and target phasors
  for each due event (At, Seq order): render the segment up to it, apply it
  segment renderer:
    asleep                                    -> zeros
    no finger, no strike pulse, no rub noise  -> FREE:   z <- p z;  y = sum goI Im z + goR Re z        (group-outer)
    otherwise                                 -> ACTIVE: per sample (sample-outer):
        e^{j theta_c} -> harmonics e^{j n theta_c} x e^{-j n theta0}  -> mode shapes phi_r, phi_t at the finger
        zp = p z (every rendered mode); rim tangential velocity v_t, radial displacement w_r and velocity at the finger
        OUTER: stick tip x_P, v_P against the wall: F_c = max(0, Kc (x_P + w_r) + Cc (v_P + w'_r))
        LIP / INNER: F_c = N
        friction: implicit closed form -> F_t (hyperbolic law; INNER: constant mu, no pump)
        strike pulses F_k (half-sines by rotation)
        z = zp + gi (phi_t F_t - phi_r F_c) + sum_k w_k F_k;  y = sum goI Im z + goR Re z
        rub noise: xorshift32 -> band-pass 1.8 kHz Q .5 -> low-pass 5 kHz, gain NOISE_K x env(F_c |u|) (10 ms)
  output: 25 Hz 2nd-order high-pass per bowl; the unit's channel = ((bowl0 + bowl1) + bowl2) -> SourceId.Bowls
  block end: zero the groups that faded; flush < 1e-20, energy, retire below -110 dB; stats (sing velocity, (3,0)
             velocity, pump power, levels, (2,0) state, lane, work)
```

### 2.2 The three bowls (computed by `designBowls(48000)`; `node bowls-design.mjs` prints every value)

| | big | middle | small |
|---|---|---|---|
| pitch (the (2,0) pair mean) | G3 195.998 Hz | C4 261.626 Hz | E4 329.628 Hz |
| rim Ø, height, wall, mass | 175 mm, 80 mm, 3.8 mm, 0.80 kg | 150 mm, 68 mm, 3.4 mm, 0.50 kg | 125 mm, 56 mm, 3.1 mm, 0.32 kg |
| modal mass M (0.2 × mass) | 0.160 kg | 0.100 kg | 0.064 kg |
| ratio and split source | [I06] Table I bowl 1 (180 mm, 934 g, 220.1 Hz) | ratios [WSN02] Table 1 (210 mm, 186 Hz); splits = mean of [I06] bowls 1, 2 | [I06] Table I bowl 2 (152 mm, 563 g, 311.2 Hz) |
| (2,0) split, beat | 0.89 Hz (published bowl: 1.0 Hz) | 1.39 Hz | 2.01 Hz (published: 1.9 Hz) |
| T60 (2,0) / (3,0) / (4,0) / (6,0) / (9,0) | 70.3 / 16.9 / 8.9 / 2.3 / 0.76 s | 52.7 / 11.3 / 6.2 / 1.5 / 0.50 s | 41.8 / 10.7 / 5.9 / 1.6 / 0.49 s |
| (3,0) T60 while a finger is on the rim | 2.38 s | 2.16 s | 2.23 s |

Mode frequencies f_A / f_B (Hz) and the ratio of the pair mean:

| n | big | ratio | middle | ratio | small | ratio |
|---|---|---|---|---|---|---|
| 2 | 195.55 / 196.44 | 1 | 260.93 / 262.32 | 1 | 328.62 / 330.63 | 1 |
| 3 | 542.40 / 543.11 | 2.769 | 774.61 / 775.45 | 2.962 | 877.28 / 878.02 | 2.663 |
| 4 | 1011.51 / 1014.90 | 5.169 | 1414.48 / 1418.40 | 5.414 | 1592.68 / 1596.18 | 4.837 |
| 5 | 1591.85 / 1592.12 | 8.122 | 2285.49 / 2291.56 | 8.747 | 2466.36 / 2479.07 | 7.502 |
| 6 | 2275.39 / 2283.94 | 11.631 | 3281.89 / 3292.52 | 12.565 | 3499.89 / 3509.43 | 10.632 |
| 7 | 3051.73 / 3052.88 | 15.573 | 4463.52 / 4465.99 | 17.065* | 4675.28 / 4678.67 | 14.189 |
| 8 | 3897.07 / 3908.74 | 19.913 | 5814.74 / 5826.85 | 22.249* | 5970.06 / 5977.06 | 18.122 |
| 9 | 4843.09 / 4852.79 | 24.735* | 7347.64 / 7362.35 | 28.113* | 7405.15 / 7419.97 | 22.488* |

\* Extrapolated: r_{k+1} = r_k ((k+2)/(k+1))^p, with p fitted on the last two measured pairs.
- Unmeasured splits are set to 0.2 % (ASSUMED).
- Every mode stays below 7.42 kHz, which is 0.31 × Nyquist: no aliasing.
- **Pair orientation.** θ0 (the axis of the A member) is a fixed seeded "identity" per pair in [0, π/n). The B member
  is π/(2n) away ([TB11]).

### 2.3 Constants (every one, with its source; revision-2 changes in bold)

| constant | value | source / reason |
|---|---|---|
| damping, free bowl | ζ = 2e-5 · r^0.68, capped at 1.5e-4 (r = f_n/f_2) | [I06]: "0.002~0.015 % (higher values pertaining to higher-order modes)" |
| SUPPORT (cushion) | ×4 (2,0), ×3 (3,0), ×2 above | DESIGN, within [I06]'s "one order of magnitude, or more, depending on how the bowls are supported" |
| MASS_FRACTION | 0.2 | [I06] modal stiffnesses for bowl 2 → M ≈ 0.11–0.13 kg = 0.2 × 563 g |
| RAD_DB (partial pressure for a hard strike, re (2,0)) | 0, −18.2, −15.6, −16.3, −13.0, −14.5, −16.0, −18.0 | [WSN02] Table 1 for n = 2..6 (wooden mallet); 7..9 continue the trend (ASSUMED) |
| MALLET_MS, MALLET_FLOOR | 0.25 ms, 0.316 | Wooden mallet contact (ASSUMED); RAD is divided by that mallet's two-component spectrum, floored at −10 dB |
| **OUT_GAIN**, F_REF | **413.333** per m of modal displacement (was 620), × f0/261.6 per bowl | DESIGN: −3.5 dB with the fingertip mass +3.5 dB, so strikes keep their level and rubs sit at the piano's loudness (section 9) |
| FILL | 0.35 | DESIGN (D8): the rubbed modulation depth floor, 9.4 dB |
| **TOUCH_ALPHA**, TOUCH_EXP | **0 / 0.8 / 8 / 25 s⁻¹** at f0 (level 1 was 3), × (f/f0)^0.5 | DESIGN: T60 7.0 s (one light finger, never beats the rub's ~2 s⁻¹ pump), 0.81 s (fingers), 0.28 s (palm) |
| **CONTACT_ALPHA** | **1.5 s⁻¹** at f0 × (f/f0)^0.5, pairs n ≥ 3 only, while a contact is on | DESIGN (D11): skin and pulp tissue loss rising with frequency; the rubbed (2,0) pair keeps its poles. Middle (3,0) T60 11.3 → 2.16 s while touched |
| MU_S, MU_INF, VC | 0.85, 0.30, 0.25 m/s; μ(u) = μ∞ + (μs − μ∞)/(1 + u/VC) | The shape of [I06] eq. 14 (soft puja: 0.8 / 0.4 / C 10) |
| EPS | 1e-4 m/s | [I06] regularisation: pseudo-adherence below it |
| KC, CC, M_TIP | 1e5 N/m, 30 N s/m, 0.02 kg | [I06]: Kc 1e5 (soft puja), Cc 50 (softened to 30). The tip mass is ASSUMED. A pulp-like soft contact (Kc 5e3–1e4, 2–3 g) was tried for M3: it fixes the dead spots but kills the big bowl's singing above 0.3 m/s and turns "too fast" from a rattle into silence (`dev/rubmap.mjs`), so it was rejected |
| NORMAL_RAMP, ONSET_RAMP, RELEASE_RAMP | 512, 1440, 1440 samples | Between frames (10.7 ms); a touch builds over 30 ms; the lift is 30 ms |
| **RAMP (command)** | 0 = default, else 64..12000 samples | Playback glides: a decoded `bowlRubCtl` ramps N and v_f over its `speed` (platform 6.3) |
| ANGLE_LOCK | 0.02 s | The contact phasor converges on the tracked angle |
| NOISE_HZ, NOISE_Q, NOISE_LP, NOISE_K | 1800 Hz, 0.5, 5 kHz, 0.018 per N·m/s | DESIGN: skin on metal below 5 kHz; −29.3 dB re the steady tone at 0.3 m/s, 3 N |
| **NOISE_TAU** | **10 ms** | The noise follows a one-pole envelope of the friction power F_c\|u\| (coefficient precomputed), not its impulsive samples |
| **TAP**, **PALM** | **m 0.030 kg** (was 0.020), e 0.6, T_ref 0.9 ms; **m 0.090 kg**, e 0.3, T_ref 3 ms | ASSUMED: the fingertip plus the moving finger (the drum spec's finger is 28.6 g); the palm heavier, softer, darker |
| **SUEDE**, **WOOD** | m 0.040 kg, e 0.4, T_ref 1.6 ms; m 0.040 kg, e 0.7, T_ref 0.35 ms | ASSUMED: the puja's suede-wrapped end (soft, round) and its bare wooden shaft (bright, as wood on bronze) |
| contact time | T = T_ref · (V / 1 m/s)^−0.2, clamped 0.4–12 ms | Hertz scaling |
| impulse | I = (1 + e) m V | Momentum with restitution |
| PULSE_SPLIT, PULSE_SHORT | 0.6, 0.72 | D9 |
| HP_HZ | 25 Hz, 2nd-order Butterworth | Removes the quasi-static deflection under the moving stick |
| CULL2, FLUSH | 1e-11, 1e-20 | burst.md 5.3 |

### 2.4 State, recurrence and output

A mode has state z = q̇/ω + j·q, where q is the modal displacement at the rim antinode (m), and p = r·e^{jωT}.
- **Free**, per sample (canonical order): `zr' = mad(pr, zr, (−pi)·zi)`, `zi' = mad(pr, zi, pi·zr)`.
- **A force** F (N) on the mode for one sample adds `gi·F` to the real part after the rotation, with **gi = T/(M ω)**.
- **Output** per mode: `acc = mad(goR, zr', mad(goI, zi', acc))` in float4 lanes, groups ascending, then
  `y = (a0 + a1) + (a2 + a3)`. The free and active paths use the same accumulation.
  - A group being dropped by the work cap adds `acc = mad(g_n, mad(goR, zr', goI·zi'), acc)` with
    g_n = (frames − 1 − n)·(1/frames), then it is zeroed at block end.

Mode shapes at an angle θ (ψ = θ − θ0, pair n, inextensional):

| | radial φ_r | tangential φ_t |
|---|---|---|
| A | cos nψ | −sin(nψ)/n |
| B | sin nψ | cos(nψ)/n |

**Pole tables.** There are 8 tables per mode: (damp level L = 0..3) × (contact off / on), index L + 4·on. Each
table's extra decay rate is TOUCH_ALPHA[L]·(f/f0)^0.5 + on·(n ≥ 3)·CONTACT_ALPHA·(f/f0)^0.5. A table switch happens
at a `Damp`, at a touch start, and when a contact's release ramp ends. It copies 32 floats; the state carries over,
so there is no click (9.6).

**Tangential admittance.** `Y0t = T · Σ_n (1/n²) / M`: 7.03e-5 / 1.125e-4 / 1.757e-4 m/s per N.

### 2.5 Strikes (fingertip tap, palm, the puja's suede end and wood, the touch-down of a rub)

- **Main thread.** It converts the hand's normal speed V into the impulse I and the contact time T for the kind
  (2.3). The command carries I, T, the strike angle phasor (cos, sin) in the bowl frame, and the height factor s.
- **Audio thread.** It splits the strike into two half-sine slots, (0.6 I over T) and (0.4 I over 0.72 T):
  - K = max(2, round(T·sr));
  - peak A = π·I_k/(2K/sr);
  - sin(πk/K) by rotating (cos π/K, sin π/K), **from a table built at init for K = 2..600**.
- **Per-mode weight:** `w_m = gi_m · s^n · φ_r,m(θ_s)`, where s = 1 − 0.5 · depth/H (ASSUMED shape).
- **Slots.** There are 4 (two overlapping strikes). A strike takes a free slot, else the oldest.
- **Work cap.** Modes of groups not rendered take no weight.

### 2.6 Rubbing: the finger as the stick

Per sample while a contact is on (from `OP_CONTACT` until `OP_RELEASE` plus the 30 ms ramp):
1. **Ramps.** N and v_f move linearly to the latest command's targets: over the command's ramp, 1440 samples on the
   first command of a touch, and 1440 to zero on release.
2. **Rim quantities at the finger**, from the predicted states of the kept groups:
   - v_t = Σ φ_t ω Re zp;
   - w_r = Σ φ_r Im zp;
   - w'_r = Σ φ_r ω Re zp;
   - each scaled by s^n per pair.
3. **Normal force.**
   - OUTER ([I06] eq. 12): F_c = δ > 0 ? max(0, Kc δ + Cc δ') : 0, with δ = x_P + w_r. Symplectic Euler on the tip
     (mass M_TIP, pushed by N). At a touch start, x_P = −w_r and v_P = −w'_r.
   - LIP and INNER: F_c = N.
4. **Friction**, implicit, with the rim's instantaneous response v_t + Y0t·F_t:
   - pseudo-adherence if U0 ≤ EPS + y μs;
   - else the closed-form slip root of x = U0 − y μ(x): x = B ≥ 0 ? −2Cq/(B + q) : (q − B)/2;
   - F_t = s F_c μ(x);
   - INNER: μ = μ∞ (no pump, [DA16]).
5. **Inject**: `zr = mad(gi, φ_t F_t − φ_r F_c, zp_r)`, plus the strike pulses.
6. **Rub noise**: env ← env + k (F_c x − env), with k = 1 − e^{−1/(τ sr)} from the main thread; gain NOISE_K·env.
7. **Contact phasor.** The target rotates by ΩT (Taylor cos/sin). The contact rotates by (Ω + sinΔ/ANGLE_LOCK)·T.
   **Both are renormalised at each block start, e ← e·(3 − |e|²)/2** (float32, `mad` order as `renorm` in the
   reference).

Why it sings: the friction's negative slope is a negative damper on the pair combination whose tangential antinode is
under the finger. It saturates when the rim's tangential velocity reaches about v_f, and turns with the finger; the
radial node then sits under the finger ([I06], [TB11]).

**Lanes** (D13). The contact slot of a bowl is owned by one lane at a time: 0 is live, 1..3 are playback.
- A `Contact` from the owning lane updates it. A `Release` from another lane is ignored.
- A `Contact` from another lane while the slot is on:
  - **lane 0 over a playback lane: preempt.** The stick is re-seated at the live finger's angle (init), its force
    builds from 0 over the onset ramp, and `Preempts` is counted;
  - **otherwise it is dropped**, and `LaneDrops` is counted.
- When the slot is off, any lane may take it. After the live release a waiting playback lane re-seats at its own
  angle with its next command.

### 2.7 Damping by touch

- **The rub contact itself** is physical damping when the finger stops (v_f = 0): pseudo-adherence plus the radial
  dashpot give T60 0.20 s. This is how a player stops a singing bowl with the stick.
- **Other touches** select pole tables. The module decides which (section 4):

| Level | Who sends it | T60 |
|---|---|---|
| 0 | nothing touching (or only the rub contact) | the cushion's |
| 1 | one still fingertip, ≥ 0.6 s after its entry and 0.3 s still; or one deep still extra tip on a rubbed bowl | 7.0 s |
| 2 | two or more such tips | 0.81 s |
| 3 | a palm resting ≥ 0.25 s | 0.28 s |

### 2.8 Pickup and room fill

The output per pair, at the listener's azimuth θL in the bowl frame:

`y_n = rad_n [ cos(nψL) Im zA + sin(nψL) Im zB + FILL (−sin(nψL) Re zA + cos(nψL) Re zB) ]`

- A struck bowl beats with a depth set by where it was struck.
- A rubbed bowl pulses 4 times per revolution, 9.4 dB peak to trough.
- θL comes from the head pose every frame (`OP_PICKUP`, per bowl), slew-limited to 0.005 rad per block and
  renormalised.

### 2.9 Output filter, retirement, the work cap

- **High-pass.** 25 Hz 2nd-order, per bowl, then the sum.
- **Retirement.** A bowl retires (state zeroed, no CPU) when nothing touches it, no pulse runs, its energy is below
  1e-11 and its block peak below 1e-6. A hard palm on the big bowl retires after 120.5 s, a tap on the middle one after
  93.7 s, and TV11 at block 3759. 0 subnormal states in 45,000 blocks.
- **The work cap** (platform 10.3). k = Budget/1200 × Share is read once per block, and groups are rendered by k:

  | k | ≥ 0.75 | ≥ 0.6 | ≥ 0.45 | below |
  |---|---|---|---|---|
  | groups | 4 (n = 2..9) | 3 (n = 2..7) | 2 (n = 2..5) | 1 (n = 2..3) |

  - A dropped group fades over one block, then is zeroed.
  - Work = mode-samples rendered + 24 per contact sample.
  - At Share 0.4: **−75 %** with three bowls ringing, and −30 % for a rubbed bowl (the friction solve is not scaled:
    it is the sound). The rubbed bowl still sings the same (0.625 against 0.625 m/s).

### 2.10 Commands (the unit's own `GraphOp` byte values; `Target` = bowl 0..2)

| Op | Fields | Flags |
|---|---|---|
| 1 Strike | A impulse (N s), B contact time (s), C cos θs, D sin θs, E height factor s | **LateDrop** (the only onset) |
| 2 Contact | A N (newtons), B v_f (m/s, signed), C cos θc, D sin θc, E Ω (rad/s), F height factor, N surface (0 OUTER, 1 LIP, 2 INNER), **Id lane (0..3)**, **G ramp (samples, 0 = default)** | none (a late frame is applied late: harmless, and dropping a Contact would freeze the stick) |
| 3 Release | Id lane | never dropped |
| 4 Damp | N level 0..3 | never dropped |
| 5 Pickup | C cos θL, D sin θL, N snap (1 = no slew) | |
| 6 Seed | Seed | |
| 7 Silence | none (zero the bowl; also `UnitFlags.Reset`) | |

- Events apply in (At, Seq) order; `AudioSchedule` stamps them, so the looper is sample-accurate.
- The `Share` is read from `UnitSlot.Share` (platform 10.2), not from a command.

---------------------------------------------------------------------------------------------------------------------------

## 3. Unity integration (against `platform.md`; the bowls agent changes no shared file)

### 3.1 Ids (platform 2, fixed)

| Field | Value |
|---|---|
| `InstrumentKind` / `RigId` / `UnitId` | 9 / 9 / 9 (`Bowls`) |
| Sources | `SourceId.Bowls`, at the bowls' centre (rig-local (0.006, 0.703, −0.011)) |
| Module, Order | `BowlsModule`, 37 |
| `TickOrder` | 24 |
| Folder / asmdef | `Assets/MusicRoom/Bowls` / `MusicRoom.Bowls` (keep the stub's asmdef file and `.meta` GUID) |
| Persisted id, name | `bowls`, "Singing bowls" |
| Carousel | 3 (piano → harp → theremin → **singing bowls** → handpan) |

### 3.2 The descriptor (`Bowls/BowlsDefinition.cs`, replaces the provisional `registry.mjs` entry)

```csharp
public sealed class BowlsDefinition : IInstrumentDefinition {
  public InstrumentDescriptor Describe() => new InstrumentDescriptor {
    Kind = (InstrumentKind)9, Rig = (RigId)9, Id = "bowls", Name = "Singing bowls", AloneName = "Singing bowls", Family = "metal",
    Carousel = 3, ModuleOrder = 37, TickOrder = 24,
    Traits = InstrumentTraits.Playable | InstrumentTraits.Pitched | InstrumentTraits.Continuous | InstrumentTraits.UsesTools
           | InstrumentTraits.HasStand | InstrumentTraits.TableTop,                    // no FastMotion (4.5)
    Tools = ToolMask.RubStick | ToolMask.Striker,                                      // one puja: shaft = RubStick, suede end = Striker
    Seat = new SeatSpec {
      CanonicalSlot = SeatSlot.FrontLeft, Prefs = new[] { SeatSlot.FrontLeft, SeatSlot.FrontRight, SeatSlot.Left, SeatSlot.Right, SeatSlot.Front },
      Priority = 8, ReachMax = 0.62f, PlayCentre = new Vector2(0.006f, -0.011f),
      Boxes = new[] {
        new FootBox { Cx = 0, Cz = 0, Hx = 0.205f, Hz = 0.145f, Y0 = 0f,    Y1 = 0.61f },   // the stand
        new FootBox { Cx = 0, Cz = 0, Hx = 0.205f, Hz = 0.145f, Y0 = 0.61f, Y1 = 0.72f } }, // bowls, cushions, puja
      LiftMin = -0.03f, LiftMax = 0.06f },
    Stow = new StowSpec { SpotId = "window-sill", Mount = StowMount.Surface, Where = "On the sill",
      Boxes = new[] { new FootBox { Cx = -0.01f, Cz = 0, Hx = 0.255f, Hz = 0.09f, Y0 = 0.63f, Y1 = 0.735f } } },
    Portrait = new PortraitSpec { From = new Vector3(0.42f, 1.05f, -0.62f), At = new Vector3(0f, 0.66f, 0f), Fov = 30f, Fallback = BowlsIcon.Strokes },
    Audio = new AudioSpec { Unit = 9, CapAvgMs = 0.15f, CapWorstMs = 0.25f, ArenaMiB = 0.02f, BakedMiB = 0f, ErOrigin = new Vector3(0f, 0.66f, 0f) },
    Lessons = new LessonSpec { Travel = 0.30f, LeadSeconds = 1.6667f, Shape = LessonBlockShape.Ring, Notation = NotationKind.None, Pack = "bowls", CountNoun = "rings" },
    Mr = new MrSpec { OnTable = true, SurfaceLift = 0f },                              // a real table: the stand hides, the cushions sit on it
    Shadow = new ShadowSpec { Local = new Vector2(0f, 0f), W = 0.46f, D = 0.34f, Opacity = 0.45f },
    Events = new[] {
      new EventTypeSpec("bowlStrike", EventRole.Onset,   EventKey.Index),
      new EventTypeSpec("bowlRubOn",  EventRole.Onset,   EventKey.Lane),
      new EventTypeSpec("bowlRubCtl", EventRole.Control, EventKey.Lane),
      new EventTypeSpec("bowlRubOff", EventRole.Release, EventKey.Lane),
      new EventTypeSpec("bowlDamp",   EventRole.State,   EventKey.Index),               // sampled at layer start (3.7)
      new EventTypeSpec("bowlSing",   EventRole.Marker,  EventKey.Index) },             // lessons and tips only
    Tips = BowlsTips.All,                                                              // section 7.2
    Features = new[] { "bowls.tap", "bowls.palm", "bowls.rub", "bowls.damp", "bowls.puja", "bowls.lessons", "bowls.beat", "bowls.rock" },
    DrawsAtSeat = 5, DrawsStowed = 3, TrisAtSeat = 9800, TrisStowed = 4400,
  };
}
```

- **Rig.** Home All = Home Single = seat-frame **(−0.46, 0, 0.25), yaw 0**. The stand centre is on the floor; rig-local
  +x points toward the knees and +z forward.
- **Handles.** `Rig.Handles` = the stand mesh (never the bowls or the cushions), with live bounds. `PlayedParts` = the
  bowls and the puja.
- **`Zone`.** The stand box grown by 5 cm up to 0.80 m. Pointer claims (platform 4.6) use it; contact play does not.

### 3.3 Folders and classes

- **Module `Assets/MusicRoom/Bowls/**`.**

  | Class | Role |
  |---|---|
  | `BowlsDefinition` | 3.2 |
  | `BowlsModule` | Order 37; registers the Rig; implements `IInstrument`, `IStowable`, `IInstrumentPlayback`, `ILessonTargets`, `IGuideAnchors`, `IContactSurface`, `IInstrumentSurface` |
  | `BowlsHands` | From `interaction.mjs` |
  | `PujaToolProvider` | `IHandToolProvider` |
  | `BowlsRubEncoder` | From `rub-encoder.mjs` |
  | `BowlsGeometry` | Procedural meshes from `layout.mjs` |
  | `BowlsVisuals` | Rocking, the puja's pose |
  | `BowlsTips`, `BowlsIcon` | Tip data; the fallback strokes |

  Plus:
  - `Resources/LessonPacks/bowls.json`;
  - `Resources/Voice/tip_bowls_*.wav`, `Resources/VoiceManifests/Bowls.json`, `Resources/GuideLines/Bowls.json`;
  - `Resources/MaterialTemplates/Bowls/*`;
  - `README.md` (the anchor ids), `CONTRACT-REQUESTS.md`.
- **Sound unit `Assets/MusicRoom/Audio/Sound/Bowls/**`:**
  - `BowlsUnit : SoundUnit, IBowlsSound`;
  - `BowlsDsp` (`[BurstCompile(FloatMode = FloatMode.Strict)]`, `RenderFree` + `RenderActive`, from `bowls-engine.mjs`);
  - `BowlsDesign` (`designBowls`, double maths rounded once; the K = 2..600 pulse table, the slew rotation, the 8
    pole tables, the noise-envelope coefficient);
  - `BowlsState`, `IBowlsSound`, `README.md`.
  - Implement exactly `static partial void CreateBowls(SoundContext ctx, ref SoundUnit unit)` and
    `static partial void RenderBowls(UnitSlot* u)`: the declarations exist (platform 10.2).
- **Tests:**
  - `Tests/EditMode/Audio/Bowls/` (incl. `Reference~/`: `out/test-vectors.json`, `vec-*.f32`, `rub-encoder-vectors.json`);
  - `Tests/EditMode/Bowls/`;
  - `Tests/PlayMode/Bowls/` (with `IBowlsFixture`, platform 12.2 item 7).

### 3.4 The unit (`BowlsUnit`) and its interface

**Registration.**
`Register(ctx, UnitId.Bowls, new[] { SourceId.Bowls }, _s, ringCapacity: 512, pendingCapacity: 256, idleSkip: true)`.
- **Idle skip is safe:** the unit outputs nothing once all three bowls have retired and no contact is on.
- **The first render after a skip** (`UnitFlags.Resumed`) needs nothing, because a retired bowl's state is exactly
  zero.
- **Boot.** `BowlsDesign.Build(48000)` runs on `ctx.StartWorker` (about 1 ms). Strikes before `Ready` are dropped.

```csharp
public enum BowlSurface : byte { Outer = 0, Lip = 1, Inner = 2 }
public enum BowlStrikeKind : byte { Tap = 0, Palm = 1, Suede = 2, Wood = 3 }
public interface IBowlsSound {
    void Strike(int bowl, BowlStrikeKind kind, float speed, float angleRad, float heightS, double when = 0);   // V -> impulse, T (main thread)
    void Contact(int bowl, BowlSurface s, float newtons, float fingerSpeed, float angleRad, float omega, float heightS,
                 int lane = 0, int rampSamples = 0, double when = 0);
    void Release(int bowl, int lane = 0, double when = 0);
    void Damp(int bowl, int level, double when = 0);
    void SetListener(int bowl, float angleRad, bool snap = false);
    void Silence(int bowl);
    // audio -> main (Volatile.Read of the stats, published at block end)
    float SingVelocity(int bowl);   // (2,0) radial velocity amplitude at the antinode, m/s (singing >= 0.15)
    float Mode3Velocity(int bowl);  // (3,0) radial velocity amplitude, m/s (a rub on the wrong pair: > SingVelocity)
    float PumpPower(int bowl);      // mean friction power into the bowl over the last block, W
    float Level(int bowl);          // block RMS of the bowl before the sum
    float ContactLost(int bowl);    // fraction of contact samples with F_c = 0 over the last 0.5 s (the module sums BlockLost / BlockContact)
    int   ContactLane(int bowl);    // -1 when off
    Vector4 Pattern20(int bowl);    // (2,0) zA.re, zA.im, zB.re, zB.im: the visuals
    bool  Awake(int bowl);
}
```

- **The test tap.** Every public method calls `TestTap?.RecordUnit(Id, op, bowl, value, when)` (platform 10.1).
- **Classic sound.** This is a unit-only role: the Classic switch never suspends it and `ClassicSources` is empty.
  Nothing is needed (revision 1's S2 is gone).

### 3.5 State (`BowlsState`, one `Alloc`, 64-byte aligned)

| Per bowl (`BowlState`) | Size |
|---|---|
| `fixed float G[4 × 32]` (ZR, ZI, PR, PI, GOI, GOR, GI, OM per group) | 512 B |
| `Poles[8 × 16 × 2]` (the 8 pole tables) | 1024 B |
| `Rad[16]`, `Oc[8]`, `Os[8]`, `InvN[8]`, `Hx[8]` | 192 B |
| pickup Pc, Ps, Ptc, Pts, Dirty; contact On, Kind, Init, Rel, Lane, N, Ntar, DN, NN, Vf, VfTar, DVf, NVf, Ec, Es, Tc, Ts, Trc, Trs, Omega, Hs, XP, VP, Fc; Level, PoleSet; Keep, FadeHi | 140 B |
| 4 pulse slots (On, K, Len, Amp, Rc, Rs, Sc, Ss, W[16], Start) + Active | 352 B |
| noise Nz, NEnv, NEnvK, Nb[5], Lb[5], Hb[5] and six filter states | 92 B |
| stats (energy, rms, peak, contact / lost / stick counts, block counts, pairAmp[8], singVel, p3, pump, preempts, laneDrops, lane, p2[4], work) | 120 B |
| **Total** | **≈ 2.0 KiB per bowl; 6.1 KiB for the unit** |

The pulse table (cos/sin π/K for K = 2..600) is 4.7 KiB. The per-sample scratch (PHR, PHT, ZPR, ZPI as 4 float4 each,
PF[4]) is on the stack. There is no baked audio.

### 3.6 `Render(u)` (canonical order; `BowlsEngine.renderBlock` in the reference)

0. Read `Budget` and `Share` once: keep = groupsFor(k). For each bowl, if keep < Keep, then FadeHi = Keep; Keep = keep.
1. If `Flags & Reset`: `Silence` every bowl.
2. Per bowl:
   - slew the pickup;
   - rebuild goI/goR if it moved;
   - renormalise the contact phasors if On;
   - split at that bowl's due events, `(At, Seq)` order: render a segment, apply the event (2.10);
   - render the last segment;
   - high-pass; end the block (2.9).
3. Write ((y0 + y1) + y2) into `Out[0]`, and set `Work`.

### 3.7 Recorder, takes, looper (platform 6)

| Type (role, key) | Fields | Written when |
|---|---|---|
| `bowlStrike` (Onset, Index) | `i`, `vel` speed (m/s), `u` angle (rad, bowl frame), `pos` height on the wall (0 lip .. 1 base = 2(1 − s)), `mode` "tap" \| "palm" \| "suede" \| "wood", `midi` the bowl's pitch | every `Strike` the hands or the puja make |
| `bowlRubOn` / `bowlRubCtl` / `bowlRubOff` (Onset / Control / Release, Lane) | `i`, `lane`, `zone` "outer" \| "lip" \| "inner", `u` pressure (N), `vel` v_f (m/s, signed), `pos` height, `midi` the contact angle in **milliradians** (0..6283; a rub has no pitch of its own), `speed` (Ctl: the glide, s) | by `BowlsRubEncoder` (below), once per frame, while the module sends `Contact` |
| `bowlDamp` (**State**, Index) | `i`, `vel` level 0..3 | on every level change |
| `bowlSing` (Marker, Index) | `i`, `u` seconds sung in this rub | each full second of singing (SingVelocity ≥ 0.15 while rubbing), and at the end of a rub |

- **`bowlDamp` is a State, not the platform's suggested Release** (request R5, informational). With a Release key
  `Index`, the looper would give every open `bowlStrike` its Release at each window end, i.e. damp every ringing bowl
  at every loop boundary. A State is sampled at layer start, as the hat is.
- **`BowlsRubEncoder`** follows the platform's ControlStream rules (`rub-encoder.mjs`):
  - On at the first `Contact` of a rub;
  - `Ctl` when |v_f| moved ≥ 0.5 dB (floor 0.03 m/s), its sign changed, N moved ≥ 0.5 N, the angle is ≥ 0.08 rad from
    where the last values predict, or the surface changed, at most every 1/30 s; or a keyframe every 0.25 s;
  - Off at the `Release`, after a final `Ctl` at N 0.

  Its deadbands are the bowl's own (newtons, m/s, rad), not Core's pitch/level ones, so it lives in the Bowls folder
  and Core is untouched.
- **Measured** (`lanes.mjs`): a first-timer rub of 5.9 s gives **140 events (23.8 / s)**, 24 KB in a take.
  - Played back on lane 1, it sings within **0.02 dB** of the live render and starts singing at the same moment.
  - Five loop passes 7 s apart sing within **0.05 dB** of each other.
- **Playback** (`IInstrumentPlayback.Play`):

  | Event | Unit call |
  |---|---|
  | `bowlStrike` | `Strike` |
  | `bowlRubOn` | `Contact` on `e.lane`: angle `midi/1000`, Ω = v_f/R, the default ramp |
  | `bowlRubCtl` | **dispatched min(speed, 0.05 s) early**, with `rampSamples` = speed × 48000 and the angle target moved back by Ω × that lead |
  | `bowlRubOff` | `Release(e.lane)` |
  | `bowlDamp` | `Damp` |
  | `bowlSing` | `false` (a Marker) |

  - `ReleaseAll` releases lanes 1..3 on every bowl.
  - `CloseLane(lane)` releases that lane on the bowl it holds; the module keeps lane → bowl.
- **Displayed bowls play back in place** (platform 6.2). Their unit wakes on the command and their sources resume
  ≥ 250 ms ahead; the bowls on the sill do not animate.

### 3.8 Lanes (platform 6.4)

Live play uses lane 0, and layer k plays on lane 1 + (k mod 3). The unit supports all four lanes, with **one stick per
bowl** (2.6):
- the live hand takes a bowl a loop is rubbing;
- the loop's lane waits, its commands dropped, until the bowl's stick is free;
- two playback lanes on one bowl: the first keeps it.

Measured (`lanes.mjs`) on the critic's `loop.mjs` case (a loop rubs the middle bowl, the live hand rubs it from the
other side for 2 s):
- 1 preempt, 48 dropped commands;
- contact lost 1.7 % (one shared slot gave 3.6 % and interleaved two angles);
- the handover's step with the noise off is 1.29× the signal's own, against 1.32× for the same landing with no lanes
  (a lift, then a touch elsewhere). It is the physical landing of a stick on a singing bowl, not a lane artefact.

### 3.9 The puja (platform 4.6 tools)

**`PujaToolProvider : IHandToolProvider`.**
- `Owner` = Bowls.
- `NearestFree(tip)` measures to the puja's grip while it rests on the stand and the bowls are at the seat; it is +inf
  otherwise.
- `TryPickUp` is the drums' pick-up rule (pinch within 2 cm of the grip; `Near` / `Grabbed`).
- `ReleaseAll` lays it back at rest.
- `ShadowSpheres` gives the suede end and the shaft's lowest point.

**Geometry** (`layout.mjs`).
- 150 mm long, Ø 22 mm turned walnut; its top 35 mm wrapped in suede, Ø 27 mm.
- Grip 85 mm from the suede end.
- At rest it lies along x at rig-local (−0.125, 0.122), on the front-left of the stand top.
- Held, it follows the hand like a drumstick (the grip's pose, drums' `StickPose`). Core's shared `HandToolMath` is
  marimba request R4; until then the module keeps its own copy.

**In play** (`BowlsHands.toolFrame`):
- **The shaft** is tip slot 5, radius 11 mm. It is the point of the shaft, from 35 mm under the suede down to the end,
  that is deepest in the nearest bowl surface.
- **The suede end** is slot 6, radius 13.5 mm; it only strikes.
- **Both use the stick's centre speed** over tracker samples (the deepest point slides along a rigid stick).
- **The shaft rubs like a fingertip** (the same contact model, the same rules), and strikes as `Wood` at ≥ 0.25 m/s.
  **The suede end strikes as `Suede`** at ≥ 0.12 m/s.
- Measured: the shaft circling the middle bowl sings 0.45 s after it lands, with one touch tick; a suede strike at
  ~1 m/s gives one `Suede` strike at V 0.97.

### 3.10 Guide anchors, contact surface, MR surface

- **`IGuideAnchors`.**
  - `bowl.big.near`, `bowl.middle.near`, `bowl.small.near`: the rim point facing the head; up = the outward normal.
  - `bowl.big.rim`, `bowl.middle.rim`, `bowl.small.rim`: the rim centre; up = (0, R, 0), so the platform's `Circle`
    demo takes its radius from it.
  - `puja.grip`.
  - `bowls.lesson.<i>`: where a lesson block lands, i.e. on the rim of bowl i.
- **`IContactSurface`** (E1/E3): the rub band and the outer wall of each bowl, so the drawn fingertip stops on the
  metal while the tracked tip goes deeper (`ContactUse.Both`).
- **`IInstrumentSurface`** (MR, `TableTop`): `SetOnSurface(true)` hides the stand. The cushions sit on the real table
  with `SurfaceLift` 0.

### 3.11 Desktop

- A left click on a bowl strikes it: `Strike(Tap, 0.8)` at the clicked angle and height.
- A left-drag circling a rim is `Contact(Outer, 4 N, v_f = drag angular speed × R)`; button-up is `Release`.
- The mouse hand never plays (`Pointer.FromMouse`, platform 12.2 item 16).

### 3.12 Contract requests (`Bowls/CONTRACT-REQUESTS.md`, for the platform agent / integrator)

| # | Request | Why / evidence |
|---|---|---|
| **R1** | `AudioSourceTable` row `Bowls`: **send −1 dB in VR, −7 dB in MR** (the platform's 10.1 table says +2 / 0.7 / 0). Reach 0.7 and ER 0 unchanged | Seat DRR at −1 dB is +6.41 dB against the piano's +6.08 by the same method (target ±2); at +2 dB it is +3.41 (fails). MR keeps the piano's −6 dB offset, less 1 dB (`tools/conformance.mjs`) |
| **R2** (optional) | Guide demo kind `FingerCircle`: the ghost index circles the anchor's rim, 1 cm below the lip, one turn in 1.4 s | For `tip_bowls_rub`. Fallback: the platform's `Circle` (a pinched stick), which also shows the puja |
| **R3** | Read the "no low-frequency push" mean/rms rule over ≥ 200 ms (or after a 20 Hz high-pass) for fundamentals under 250 Hz | Below 60 Hz the bowls are ≤ −47.8 dB re the hit (limit −20). The big bowl's 100 ms mean/rms is 0.010–0.011, but its sign flips with the window position (+0.011, −0.010 over 20 periods, +0.023 shifted 10 ms, −0.0045 over 200 ms): that is the window edge on a 196 Hz tone with beating partials, not a push |
| **R4** | The platform's `registry.mjs` `bowls` entry: replace the provisional footprint and home by 3.2's (canonical FrontLeft, home (−0.46, 0.25) yaw 0, prefs, reach 0.62, the two boxes) and re-run `make-vectors.mjs` | The planner then fits 26 of 28 sets with the bowls, with the classic three always at today's homes (`out/seat-check.json`) |
| **R5** (informational) | `bowlDamp` is declared State, not Release (3.7) | Descriptor data in our folder; no code change. Otherwise the looper would damp ringing bowls at every window end |

---------------------------------------------------------------------------------------------------------------------------

## 4. Interaction (bare hands and the puja; reference `interaction.mjs`, constants `IX`)

### 4.1 What the hand can do

| Gesture | Detection | Sound call |
|---|---|---|
| **Tap** | A tip enters a bowl surface (the swept test, 4.5) with a **peak inward normal speed over the last 3 tracker samples** ≥ **0.12 m/s** (< 3.5 m/s), from a tip tracked > 2 frames, ≥ 0.12 s after that tip's last tap on that bowl. Not while the same hand is rubbing this bowl | `Strike(Tap, min(V, 3))` at the entry angle and height. The tip is then **touching**: no friction, no damping for 0.6 s |
| **Touch** | An entry under 0.12 m/s | `Strike(Tap, max(V, 0.03))`, the touch-down tick that also seeds a rub (none if the tip left this bowl less than 0.3 s ago) |
| **Rub** | A touching tip that **moves along the rim**: \|v_f\| ≥ 0.05 m/s for 0.1 s, while nobody else rubs this bowl. `rubStart` for lessons and Sunny when the mean \|v_f\| over 0.3 s is ≥ 0.05 m/s | `Contact(surface, N, v_f, θ, Ω, s)` every frame, lane 0 |
| **Rest** | A touching tip still (within 6 mm of where it settled) for 0.3 s, at least 0.6 s after its entry | `Damp(1)`; `Damp(2)` for two or more such tips |
| **Stop a rub** | The rubbing finger stops (\|v_f\| < 0.03 m/s) | `Contact(v_f = 0)`: the stick holds the rim (T60 0.2 s) |
| **Extra tips while rubbing** | Other tips on a rubbed bowl | Nothing, unless still and ≥ 8 mm deep for > 0.4 s (then `Damp(1)` / `Damp(2)`). The rubbing hand's other tips never strike it |
| **Palm** | The palm centre within 2.5 cm above or around the rim (y > 0.4 H), the normal toward the bowl (cos > 0.5) | Arriving at ≥ 0.3 m/s (over tracker samples): `Strike(Palm, min(v, 2))`. Resting ≥ 0.25 s: `Damp(3)` |
| **Lift** | Touch: penetration < −3 mm. Rub: < **−15 mm** and then **0.25 s** (the lift hold: the stick stays on at 2.5 N, turning with the last Ω) | `Release(lane 0)`. A re-entry within 0.4 s of a rub's release on that bowl (under 0.5 m/s) **continues the rub** with no strike |
| **Puja** | Shaft (slot 5) and suede end (slot 6), 3.9 | `Wood` strike ≥ 0.25 m/s; rubs as a fingertip; `Suede` strike ≥ 0.12 m/s |

### 4.2 Zones and sizes (bowl frame: y up from the base, axis at x = z = 0)

- **Outer surface.** r_out(y) = R (0.55 + 0.45 sin(π/2 · y/H)). **Inner:** r_in = r_out − wall.
- **Tip sphere.** The OpenXR fingertip joint centre with radius 7.6 mm × handScale (thumb 9.8 mm; 02-meta-hands).
- **Rub band.** The lip and the top 30 mm of the wall. A touching tip leaves only when it is also 40 mm below the band:
  sliding down the wall is not a lift.
- **Which surface.** The side the tip came from (outside, inside or above), remembered while free.
  - From above inside the lip ring, it is the inside.
  - From above over the lip ring, it is the lip.
  - Otherwise it is the outside.
- **Pressure.** N = clamp(2.5 + 250 N/m · (p − 2 mm), 2.5, 8).
- **Gaps between rims:** 30–43 mm (section 6.1). While a tip rubs one bowl it cannot touch another. Before that, a
  tip grazing a neighbour lands on it like any touch.

### 4.3 Rub speed and angle (per tip, new tracker samples only)

- θ = atan2(z, x) in the bowl frame, unwrapped. ω = Δθ/Δt_sample with an EMA (τ 60 ms).
- v_f = ω · clamp(ρ, R ± 1 cm), with a deadband of 0.03 m/s.
- The unit's phase lock (20 ms) absorbs the jitter. A first-timer's 0.7 turns per second reads v_f within 1.6–2.5 %
  of the truth at 30 Hz.

### 4.4 Velocity mapping and dynamics

| Kind | Speed range | Impulse | Contact time |
|---|---|---|---|
| Tap | V ∈ [0.03, 3] m/s | I = 0.048·V N s | T = 0.9 ms · V^−0.2 |
| Palm | V ∈ [0.3, 2] | I = 0.117·V | T = 3 ms · V^−0.2 |
| Suede | | I = 0.056·V | T = 1.6 ms · V^−0.2 |
| Wood | | I = 0.068·V | T = 0.35 ms · V^−0.2 |

Middle bowl, dry, 3 s window (unchanged by revision 2, because the mass and the gain changes cancel):

| tap V (m/s) | 0.2 | 0.4 | 0.7 | 1.0 | 1.5 | 2.2 | 3.0 |
|---|---|---|---|---|---|---|---|
| loudness (LUFS) | −36.1 | −29.9 | −24.9 | −21.7 | −18.1 | −14.8 | −12.0 |
| peak (dBFS) | −32.3 | −26.1 | −20.9 | −17.6 | −13.9 | −10.4 | −7.7 |

The range is 24.1 dB, with brightness rising with force. A palm at 1 m/s is −18.4 LUFS and dark: (3,0) is −45 dB.

### 4.5 Debounce, tracking, prediction and false triggers

| Situation | Rule | Case (9.3) |
|---|---|---|
| **The entry test** | **Swept:** the segment from the previous frame's position to this frame's, every 2 mm (≤ 24 steps), against every bowl surface | `fast` |
| **Positions** | Unity uses `HandFrame.position[]` (the runtime's display-time prediction) and `tipVelocity[]` (sample speed). The reference emulates the prediction from held samples, p_s + v_s (t − t_s), ≤ 40 ms and only above 0.3 m/s, because i.i.d. lab jitter would otherwise be amplified more than the device's correlated jitter | all |
| A bounce within 0.12 s | One strike (per tip and bowl) | `bounce`, `double taps` |
| Tracking lost < 120 ms during a rub | The contact holds; the unit coasts on Ω | `loss 80 ms` |
| Lost ≥ 120 ms | `Release` (30 ms ramp) | `loss 400 ms` |
| Re-acquired within 0.25 s and 3 cm of the last contact | The rub continues with no strike | `loss 400 ms` |
| A tip that appears inside a bowl | No strike for its first 2 tracked frames | `glitch` |
| An entry faster than 3.5 m/s | No strike (a tracking jump) | `glitch` |
| System gesture, a hand held by Layout, arrange mode, gliding, MR placing | No calls; contacts release (Core skips `Tick`) | |
| `poseInferred` | Never strike (02-meta-hands §6) | |

**3 m/s taps at 30 Hz (M2, partly rejected).** At LOW tracking a 3 m/s tap with 5–10 mm overshoot can lie entirely
between two samples whose difference shows only 0.3–0.7 m/s and no penetration. No algorithm can recover it: 16 and
17 of 20 are detected, 20 of 20 at 20 mm. At 60 Hz all are detected.

The bowls do not set `InstrumentTraits.FastMotion`. Bowl taps are gentle (0.2–1.5 m/s, all detected) and rubs are
slow. If device check B3 shows missed hard taps, set FastMotion; it is descriptor data.

### 4.6 Comfort for a seated player (seat frame; `out/seat-check.json`)

| Bowl | rim centre (x, y, z) | left shoulder: near / far rim | right shoulder: near / far | eye distance | gaze yaw / pitch |
|---|---|---|---|---|---|
| big | (−0.560, 0.715, 0.193) | 0.48 / 0.61 m | 0.76 / 0.92 m | 0.77 m | −71° / −39° |
| middle (the lesson bowl) | (−0.355, 0.703, 0.193) | **0.40 / 0.48 m** | 0.60 / 0.73 m | 0.64 m | −62° / −51° |
| small | (−0.448, 0.691, 0.332) | 0.51 / 0.61 m | 0.74 / 0.85 m | 0.76 m | −53° / −42° |

- **Height.** Rims at 0.69–0.72 m: above the knees (0.62 m), the side table (0.60–0.68 m) and typical armrests. The
  forearm is level and the wrist circles, like stirring a pot.
- **Reach.** Every rim is within 0.62 m (2 ft) of the left shoulder; the big and small bowls' far rims are the
  farthest (0.61 m). The right hand reaches the middle bowl's near rim (0.60 m).
- **Gaze.** The bowls are beside the left knee, 53–71° left and 39–51° down: a head turn, as for a side table.
  - **View.** Seen from 1.2 m the big bowl's near rim shows over the middle bowl.
  - **Sunny's arrival tip** points there (7.2).
- **Two hands.** One bowl per hand works (two contacts). The left hand rubs while the right taps.

### 4.7 Mixed reality

The bowls follow the lead instrument at their planner offset (platform 9). A lead with `Mr.OnTable` may put them on a
real table, and `SetOnSurface(true)` hides the stand. Their shadows show on passthrough, and the hands use the
cutout look. The `Bowls` source gets −7 dB of send in MR (R1).

---------------------------------------------------------------------------------------------------------------------------

## 5. Visual spec (procedural, built in the right-handed source frame (−z forward) and mirrored, ARCHITECTURE 5)

### 5.1 Geometry

- **Bowls.** A lathe of the profile in 4.2: a flat 4 mm concave base, a rounded lip (fillet = wall/2), 64 segments ×
  18 profile points. About 2.3k triangles per bowl.
- **Cushions.** Ring tori: outer Ø 0.62 × the rim Ø, tube Ø 26 mm, 32 × 12 segments, flattened (y × 0.8). The bowl
  base sits 25 mm above the stand top.
- **Stand** (`layout.mjs STAND`). A small walnut table:
  - top 410 (x) × 290 (z) × 22 mm, 6 mm rounded edges, a 3 mm raised rim, **top surface at 0.61 m**;
  - four turned legs Ø 30 mm, stretchers at 0.14 m.
- **Bowl positions on the top** (rig-local x, z):
  - big (−0.100, −0.0575), outer-back;
  - middle (0.105, −0.0575), inner-back, nearest the left shoulder;
  - small (0.012, 0.082), front.

  Rim gaps: big–middle 42.5 mm, big–small 29 mm, middle–small 30 mm.
- **Puja.** 3.9; lying on the front-left of the top.
- **Triangles.** About 9.8k at the seat (bowls 6.5k, cushions 2.3k, stand 0.6k, puja 0.3k); about 4.4k on display (the display LOD: 32 segments, no stand).

### 5.2 Materials (template `.mat` assets in `Resources/MaterialTemplates/Bowls/`; no MaterialPropertyBlock on URP Lit)

- **`Bowls/Bronze`.** Metallic 1, smoothness 0.55 outside and 0.40 inside. The albedo is a 256 × 64 strip baked by
  `TextureBaker`:
  - warm bell bronze #9c7a45 outside;
  - patina #5e4a2c inside;
  - a 6 mm polished band #c9a467 under the outer lip;
  - two engraved rings, 10 and 13 mm under the lip.

  No symbols, script or marks. The normal map is 512², hammer dimples 6–9 mm, world-scaled.
- **`Bowls/Cushion`.** The Room's Fabric template, maroon #5a1f24, with a lighter piping line baked in.
- **`Bowls/Wood`.** The side table's WalnutGloss, plus an atlas strip for the puja's suede (#8a6a4a, smoothness 0.2).

### 5.3 Draw calls: 5 at the seat, 3 on display (the platform allows ≤ 5 and ≤ 3)

| Draw | At the seat | On display (the sill) |
|---|---|---|
| 1–3 | The three bowls (three renderers, one material; the rocking needs separate transforms) | 1: the bowls, one static mesh |
| 4 | Cushions, merged | 2: cushions |
| 5 | Stand + puja: one dynamic `Bowls/Wood` mesh. The stand is static; the puja's vertex range (≈ 300 vertices) is rewritten only while it is held or moving (`Mesh.SetVertexBufferData`, start + count, the marimba's pattern) | 3: the puja (static, lying beside the bowls) |

The contact shadow is the platform's combined rig-shadow mesh from `ShadowSpec` (no draw of ours). The E3 fingertip and
puja shadows come from `ShadowSpheres`.

### 5.4 What moves when played (physical, no glow)

- **The struck bowl rocks on its cushion.** A damped tilt about a horizontal axis perpendicular to the strike:
  θ(t) = A e^{−t/0.25 s} sin(2π · 3.2 Hz · t), with A = min(1.5°, 0.4° × V) for a tap and ×2 for a palm. A rest or a
  palm damps the rocking at once.
- **The fingertip** stays on the metal (the contact clamp) and gets its contact shadow.
- **The cushion** settles 1.5 mm under a resting palm (y scale over 80 ms).
- **The puja** follows the hand (3.9).
- **No hover glow, no glowing cue.** Revision 1's optional "rim highlight breathing" needs a custom shader and is
  dropped: no shared shader work, and a vibrating bowl does not visibly flicker.

### 5.5 Placement (seat frame)

Section 6.

### 5.6 On display (VR): `window-sill`

- **Pose.** (3.08, −1.62, 90), Surface mount.
- **What shows.** The three bowls on their cushions in a row along the inside sill (0.63 m), with the puja lying in
  front of them. **The stand is hidden.**
- **Stowed layout** (rig-local x along the sill):
  - big at −0.165, middle at 0, small at 0.14;
  - the puja at z −0.075.

  The stow box is (cx −0.01, hx 0.255, hz 0.09, y 0.63–0.735), within the platform's provisional (0.30, 0.09,
  0.63–0.76) and clear of the window view rule (< 0.85 m).
- **Silence.** Not ticked, sources paused, the unit idle-skipping (platform 4.1). Takes play back in place (3.7).
- **The portrait** shows the bowls with their stand (platform 5.2).

---------------------------------------------------------------------------------------------------------------------------

## 6. Placement against the platform's seat planner (`tools/seat-check.mjs`)

### 6.1 The pocket and the home

The free space left of the knees is bounded on four sides, each kept at ≥ 3.5 cm (the planner's gap is 3 cm):

| Neighbour | Constraint | Clearance |
|---|---|---|
| The knee box (x ±0.22) | inner edge x ≤ −0.25 | 0.035 m |
| The seat box (to z 0.07) | back edge z ≥ 0.10 | 0.035 m |
| The side table (to z 0.05) | | 0.055 m |
| The piano's keybed box (from z 0.43, y 0.62–0.95) | front edge z ≤ 0.40 | ≥ 0.03 m |
| The harp's base at its home (x −0.72 … at these z) | outer edge x ≥ −0.665 | 0.039 m |
| The torso | | 0.055 m |

That pocket is 0.41 × 0.29 m, and the stand fills it. Home All = Home Single = **(−0.46, 0.25), yaw 0**. The canonical
slot is FrontLeft (bearing −61°).

Revision 1's homes were inside the knee box: the Single home overlapped it entirely, and the All home by 1 cm.

### 6.2 What the planner does with it (the platform's `plan()`, with the real boxes in memory)

- **Sets with the bowls: 26 of 28 fit.** In every fitting set the bowls take their canonical spot at push 0, and the
  piano, drums and harp stay at today's homes.
- One set needs the table's alternate pose: drums + hand drums + bowls (the bowls Left, push 0.1).
- The two that do not fit are piano + marimba + bowls (piano + marimba never fit) and drums + theremin + bowls (the
  theremin's field against the table).
- An intermediate design (a 0.42 × 0.32 stand reaching into the piano's keybed box) made the cost-optimal search move
  the piano to the Right slot. The pocket design avoids that.

---------------------------------------------------------------------------------------------------------------------------

## 7. Lessons and Sunny

### 7.1 The lesson pack (`Resources/LessonPacks/bowls.json` = `out/lessons-bowls.json`, the `LessonFile` schema)

- **`LessonSpec`:** Travel 0.30, LeadSeconds 1.667 (the harp's), Shape `Ring` (the block lands round the rim), Notation
  None, Pack `bowls`, CountNoun "rings".
- **Targets** use the platform's `LessonTarget` fields: `{i, midi, note, label, mode: "strike" | "rub", hold}`.

| Id | Title | bpm | Events | At tempo + lead + countdown | Content (public domain) |
|---|---|---|---|---|---|
| `bowls-first` | First ring | 40 | 7 strikes | 16.5 + 1.67 + 3 = **21.2 s** | A plain pattern: big, middle, small, small, middle, big, middle. One target at a time, 1.5 s apart: **the guaranteed win** (any strike of any strength on the right bowl counts) |
| `bowls-ding` | Ding, dang, dong | 54 | 6 strikes | 10.0 + 1.67 + 3 = 14.7 s | The last bar of Frère Jacques (manuscript c. 1780), twice: C4 G3 C4(half). Checked against `piano-frere` beats 24–31 in `Lessons/Resources/lessons.json` (audited correct, tutorials-audit 2.1) |
| `bowls-sing` | Make it sing | 30 | rub middle (hold 3), rub small (hold 3), strike big | 16.0 + 1.67 + 3 = 20.7 s | A technique lesson (no melody) |

**The generator checks the platform 7.4 rules:**
- ≥ 3 lessons, ids prefixed `bowls-`;
- the first is one target at a time and 12–21 s at tempo;
- ≤ 2 targets per event;
- gaps ≥ 0.2 s, same-target repeats ≥ 0.25 s;
- every lesson under 1 minute;
- hints ≤ 21 characters ("Tap the middle bowl", "Rub the middle bowl").

All pass.

**`ILessonTargets`.**

| Method | Behaviour |
|---|---|
| `Matches` | **strike:** a live `bowlStrike` with `i`, any mode. **rub:** a live `bowlSing` Marker with `i` and `u ≥ hold`, the theremin's pattern. Seconds accumulate while SingVelocity ≥ 0.15 m/s and the module reports rubbing on that bowl; lifting for more than 1 s keeps what was earned |
| `TryGetLanding` | The rim of bowl i, approach −y (the block falls onto the rim) |
| `ShowTarget(t, k)` | **The landed block sinks into the bowl** as k rises with the seconds earned: physical, no gauge, no glow |
| `Hint` | The event's hint |
| `Validate` | `i` in 0..2, `mode` known, `hold` ≤ the event's duration |

The platform's B2/B4/B6 rules apply on the generic path. After 8 s, Sunny re-demos the rub (`tip_bowls_slower` or
`tip_bowls_outside` follows if the module sees chatter or an inner-wall contact); after 15 s any rub on that bowl
counts.

### 7.2 Sunny tips (`TipSpec` data in `BowlsTips`; lines ≤ 10 words; ids `tip_bowls_*`; clips from the sunny-tour 5.4 pipeline)

| Tip id | Class, trigger | Line (words) | Demo @ anchor | Success | Covers |
|---|---|---|---|---|---|
| `tip_bowls_arrive` | **P2, FirstArrival** (`InstrumentMoveEvent{Arrived}`) | "Tap a bowl gently with one fingertip." (7) | `Tap` @ `bowl.middle.near` | EventType `bowlStrike` (live) | `bowls.tap` |
| `tip_bowls_rub` | P3, Signal `bowls.tapped3` (≥ 3 strikes, never rubbed) | "Circle the outside of the rim, slowly, to sing." (8) | `FingerCircle` (R2; fallback `Circle`) @ `bowl.middle.rim` | Signal `bowls.sang` (singing ≥ 1 s) | `bowls.rub` |
| `tip_bowls_slower` | P2, Signal `bowls.chatter` (ContactLost > 0.2 for 1 s while rubbing) | "Slower, and press a little. Let it build." (8) | `Circle` (slow) @ `bowl.middle.rim` | Signal `bowls.sang` | `bowls.rub` |
| `tip_bowls_faster` | P2, Signal `bowls.slow` (rubbing 3 s at a mean \|v_f\| < 0.08 m/s, no song) | "A little faster, like stirring tea." (6) | `Circle` | Signal `bowls.sang` | `bowls.rub` |
| `tip_bowls_outside` | P2, Signal `bowls.inner` (an INNER contact moving for 2 s) | "Rub the outside of the rim, not the inside." (9) | `Circle` @ the bowl's rim | an OUTER `rubStart` | `bowls.rub` |
| `tip_bowls_quiet` | P3, Signal `bowls.ringing` (a bowl left above −30 dBFS for 8 s after playing, the 2nd time) | "Rest your palm on a bowl to quiet it." (8) | `Hold` (palm) @ that bowl's rim | EventType `bowlDamp` (level 3) | `bowls.palm`, `bowls.damp` |
| `tip_bowls_tap_rub` | P3, Signal `bowls.slowStart` (the first successful sing took > 4 s) | "Tap first, then circle: it sings sooner." (7) | `Tap`, then `Circle` | none | `bowls.rub` |
| `tip_bowls_puja` | P3, Signal `bowls.sang` for the 2nd time, the puja never picked up | "Or pinch the stick and circle the rim with it." (9) | `Pinch` @ `puja.grip`, then `Circle` | Signal `bowls.pujaPick` | `bowls.puja` |
| `tip_lesson_bowls` | P2, Signal `bowls.lessonStart` (the first bowls lesson) | "Tap each bowl as its block lands." (7) | `Tap` @ `bowls.lesson.<i>` | the first hit | `bowls.lessons` |
| `tip_lesson_bowls_rub` | P2, Signal `bowls.lessonRub` (the first rub target is live) | "Circle the rim until the block sinks in." (8) | `Circle` @ the target's rim | the first rub hit | `bowls.lessons` |

- **`tip_bowls_tap_rub` ships.** M3 is fixed and its claim is measured: a tap-then-rub sings at once, because the tap's
  ring is kept and grown; a plain rub takes 0.26–0.66 s (first-timer cases) and up to 1.7 s from a very slow touch.
- **Passive features**, needing no tip: `bowls.beat` (the slow beating) and `bowls.rock` (the rocking).
- **Lines** are plain English with no brand words, each about 2–3 s rendered (the pipeline measures; lint ≤ 4.0 s).
  `again` reuses the generic "Like this." Poking Sunny skips, as everywhere.
- **Signals** are published as `InstrumentSignalEvent { Kind = Bowls, Signal = "bowls.<name>" }`.

---------------------------------------------------------------------------------------------------------------------------

## 8. Verification (objective; `node verify.mjs` → out/verify.json: 35 / 35 pass)

### 8.1 Mode frequencies and ratios

- The rendered peaks match the design table within 0.05 Hz on every resolvable member.
- The ratios equal the published ratios of the source bowls (max relative error 0.000):
  - big: 2.769, 5.169, 8.122, 11.631, 15.573, 19.913;
  - middle: 2.962, 5.414, 8.747, 12.565;
  - small: 2.663, 4.837, 7.502, 10.632, 14.189, 18.122.

### 8.2 Beating, decay, attack, evolution

- **Beats.** 0.866 / 1.378 / 1.998 Hz, against splits of 0.890 / 1.393 / 2.013 Hz.
- **Decay.** Within 2.8 % of the design on all 48 modes.
- **The hard reference strike** on the middle bowl: −18.2 / −15.5 / −16.1 / −12.5 dB, against [WSN02]'s −18.2 /
  −15.6 / −16.3 / −13.0.
- **Upper partials fade first.** Middle (4,0) re (2,0): −27.5 dB at 0.1 s, −34.2 at 1 s, −52.5 at 3 s, −77.6 at 6 s.
  There are no metallic tails.

### 8.3 Dynamics and levels

- The tap ladder spans 24.1 dB (4.4).
- **Dry peaks at the mapping caps:**
  - big bowl rubbed at 0.7 m/s, 6 N: −8.5 dBFS;
  - small palm at 2 m/s: −5.0;
  - small tap at 3 m/s: −4.0;
  - all three palms at once: +0.9.

  The listener stage and the master take the last; through the room and MasterBusV2 the ff true peak is −5.3 dBTP
  (section 9).

### 8.4 Damping

| Case | T60 |
|---|---|
| A rubbing stick that stops (v_f = 0) | 0.20 s |
| Damp(1), one light finger | 7.0 s |
| Damp(2) | 0.81 s |
| Damp(3), palm | 0.28 s |

### 8.5 Artefacts

- **Clicks.** Contact start, release, damp switches and a pickup move all stay ≤ +1.6 dB over the local
  second-difference level.
- **Aliasing.** Above 16 kHz: −140 dB (palm), −64 dB (chatter).
- **DC.** −77 dB re peak.
- **Denormals.** 0 subnormal states.
- **Robustness.** An adversarial workout stays finite (peak 2.1).
- **Determinism.** Runs are bit-identical.

### 8.6 Rubbing: the playability map at the app's settings (6 s runs, touch-down 0.15 m/s)

Cells give the steady level (dBFS) / the time (s) to reach −10 dB of it; c = chatter (% of contact lost); f = faint.
The map's speeds and windows are unchanged by revision 2; the levels are 3.5 dB lower (M4).

| bowl, N | 0.03 | 0.1 | 0.2 | 0.3 | 0.45 | 0.6 | 0.8 | 1.1 | 1.5 m/s |
|---|---|---|---|---|---|---|---|---|---|
| big 2.5 N | −44.5 f | −32.9 / 0.4 | −26.2 / 0.9 | −22 / 1.1 | c 24 % | c 31 % | c 41 % | c 49 % | c 49 % |
| big 4 N | −44.5 f | −32.9 / 0.3 | −26.2 / 0.8 | −22.1 / 1.0 | −18.9 / 1.3 | c 30 % | c 32 % | c 57 % | c 47 % |
| big 6 N | −44.4 f | −32.8 / 0.3 | −26.2 / 0.7 | −22.2 / 0.7 | −19 / 1.0 | c 2.4 % | c 51 % | c 29 % | c 48 % |
| middle 2.5 N | −40.1 f | −30.9 / 0.3 | −25 / 0.7 | −21.5 / 0.9 | −18.2 / 1.1 | c 48 % | c 45 % | c 45 % | c 46 % |
| middle 4 N | −39.5 f | −30.7 / 0.2 | −25 / 0.4 | −21.5 / 0.6 | −18.4 / 0.8 | c 9.6 % | c 53 % | c 41 % | c 38 % |
| middle 6 N | −39.2 f | −30.3 / 0.2 | −24.8 / 0.3 | −21.5 / 0.5 | −18.4 / 0.6 | −15.5 / 0.8 | c 25 % | c 57 % | c 39 % |
| small 2.5 N | −40.1 f | −31.8 / 0.1 | −25.6 / 0.3 | −21.9 / 0.5 | −18.2 / 0.7 | c 29 % | c 51 % | c 49 % | c 42 % |
| small 4 N | −40 f | −31.7 / 0.1 | −25.6 / 0.2 | −22 / 0.4 | −18.3 / 0.7 | −15.5 / 0.7 | c 34 % | c 55 % | c 37 % |
| small 6 N | −39.9 f | −31.6 / 0.1 | −25.5 / 0.1 | −21.9 / 0.3 | −18.3 / 0.5 | −15.6 / 0.6 | −13.1 / 0.7 | c 52 % | c 37 % |

- 0.2 and 0.3 m/s sing on every bowl at every pressure within 1.1 s ✓.
- 1.5 m/s never sings cleanly ✓.
- 0.03 m/s stays faint ✓.
- **Other rubbing checks:**
  - 4.03 beats per revolution, depth 9.4 dB;
  - inner −43.4 against outer −22.2 dB, lip −20.8;
  - rub noise −29.3 dB re the tone.

### 8.7 Revision 2 checks

| Check | Result |
|---|---|
| `tap_then_rub_sweep` | 0 / 576 fail (was 13) |
| `phasor_renormalised` | \|e\| − 1 ≤ 4.1e-6 over 60 s (was 3.3e-2) |
| `one_extra_finger_keeps_singing` | Damp(1) keeps 98–99 % of the song |
| `share_scales_work` | −74.6 %, with the step at the change 0.92× the signal's own |
| `tissue_loss_n3_up` | Middle (3,0) T60 11.3 → 2.16 s while touched; the (2,0) poles are unchanged |

### 8.8 The paper's own bowl and puja (`verify-inacio.mjs`: 9 / 9)

| [I06] / [I03] result | Paper | Ours |
|---|---|---|
| Fig. 8, 3 N, 0.3 m/s: saturation | about 7.5 s | 10.2 s |
| Beats at a fixed point | 4 per revolution | 3.98 per revolution |
| Fig. 16, bowl 4, 5 N, 0.3 m/s | about 7 s | 5.8 s, 4.02 per revolution |
| Amplitude against V_T | almost linear | +6.08 dB per doubling |
| Amplitude against F_N | almost independent | 0.17 dB spread |
| Transients | longer with V_T, shorter with F_N | ✓ |
| Chatter onset | rising with F_N | ✓ |

These runs use the paper's own parameters, so revision 2's app constants do not touch them.

---------------------------------------------------------------------------------------------------------------------------

## 9. Platform conformance (platform 10.3; `node tools/conformance.mjs` → out/conformance.json)

| Check | Limit | Bowls |
|---|---|---|
| CPU, Quest Burst estimate | ≤ 0.15 ms avg / 0.25 worst block; Node ≤ 10× the cap | 0.012–0.040 ms typical, **0.095 ms worst** (Strict); Node 254 µs avg / 346 µs p99 worst ✓ |
| Budget scaling | reads Budget/1200 × Share | Share 0.4: Work −75 % (three ringing), −30 % (rubbed; the contact is the sound); click-free ✓ |
| No low-frequency push | < 60 Hz ≤ −20 dB; mean/rms ≤ 0.01 | **≤ −47.8 dB** ✓. Mean/rms over 100 ms is ≤ 0.0113; the big bowl's 0.010–0.011 flips sign with the window: request R3 |
| No metallic tails (metal rule) | no partial > 3 kHz outlives 0.8 × T60(2,0) | the longest above 3 kHz is 1.4–1.6 s, against 42–70 s (ratio ≤ 0.038) ✓ |
| Not over-wet | seat DRR within ±2 dB of the piano's +6.1 | **+6.41 dB at −1 dB send** (the piano measured +6.08 the same way) ✓ (R1) |
| **Loudness parity** | mf reference within ±2 LU of the piano's (Ode bar 1, vel 0.55, dry: −24.01 LUFS) | **The bowls' mf reference phrase: 4 taps E4 E4 C4 G3 at 0.7 m/s, at 108 bpm, the unit's channel, dry: −25.13 LUFS = −1.12 LU** ✓ |
| Rubs (our rule, M4) | 0.3 m/s ≤ +3 LU, 0.45 m/s ≤ +6 LU | middle 0.3 m/s 4 N **+1.47**, 0.45 m/s 4 N **+5.04**, small 0.3 m/s +1.52, big 0.35 m/s +2.41 (big 0.55 m/s 6 N +6.24) ✓ |
| ff true peak through MasterBusV2 | ≤ −1 dBTP | all three tapped at the 3 m/s cap at once: **−5.34 dBTP** (no limiting); three palms at 2 m/s: −5.84 ✓ |
| Clicks (`NullTest.MaxStep`) | ≤ the signal's own max step + 1 % | the step at the event over the own max: release 0.73, damp 0 → 3 0.76, damp 3 → 0 0, pickup move 0.84, Share drop 0.78, lane handover 0.91 ✓. With the rub noise off the handover is 1.02, the same as a plain lift-and-land (1.05): physical (3.8) |
| Denormals | flush < 1e-20; retire < −110 dBFS | ✓ (8.5) |
| Late onsets | `LateDrop` on onsets only | `Strike` only ✓ |
| Allocation | 0 B in Tick, the API and the render | preallocated slots and scratch ✓ (test 11.2) |
| Determinism | seeded; vectors bit-exact managed, 1e-5 under Burst | `FloatMode.Strict`: bit-exact under Burst too (expected, UNVERIFIED: test 11.2.2). Fused math would drift up to 1.1e-4 (free rings) and 1.3e-3 (TV9) |
| Memory | ≤ 2 MiB arena / 4 MiB baked | 6.1 KiB state + 4.7 KiB table; 0 baked ✓ |

---------------------------------------------------------------------------------------------------------------------------

## 10. CPU and memory

Node is measured on an Apple M5 Pro P-core (`node bench.mjs` → out/bench.json). The Quest figures are for the XR2
Gen 2 at CPU level 3 (1.65 GHz), per 256-frame block.

| Scenario | Node avg / p99 (µs) | Quest float4 (fused, est.) | **Quest float4 Strict (shipped, est.)** | Scalar upper bound (Node × 2.86) |
|---|---|---|---|---|
| idle (all asleep) | 4.5 / 6.0 | 1 | **1** | 13 |
| three bowls ringing | 65.6 / 86 | 7.9 | **12** | 188 |
| one rubbed, two ringing | 131 / 721 (first-block JIT) | 29.6 | **39.6** | 374 |
| two rubbed (two hands), one ringing | 181 / 223 | 51.3 | **67.1** | 517 |
| worst: all three rubbed in chatter | 254 / 346 | 73 | **94.6** | 726 |

- **The cycle model.**
  - Free path: MB4 with one extra fma, 2.3 µs per ringing bowl (×1.6 without fused mad).
  - Active path: ≈ 155 cycles per sample, 24 µs per rubbed bowl (×1.3 Strict).
- **Engine total.** The worst case adds about 0.095 ms to the graph. The platform's governor sees `Work`.
- **Device acceptance** (`[PERF]` EditMode):
  - the bowls' worst case ≤ 0.25 ms per block offline with Burst on;
  - average ≤ 0.15 ms;
  - between 0.25 and 0.5 ms: ship and file a vectorisation bug;
  - above that: run the active path only for the bowls with a finger.
- **Memory.** 6.1 KiB of unit state + 4.7 KiB of pulse table; no baked audio. Meshes about 9.8k triangles; textures two
  256 × 64 strips and one 512² normal map (≈ 0.5 MiB).

---------------------------------------------------------------------------------------------------------------------------

## 11. Tests for the Unity build

### 11.1 Test vectors (`node test-vectors.mjs` → out/test-vectors.json and `vec-*.f32`; copy to `Tests/EditMode/Audio/Bowls/Reference~/`)

**Conditions.** 48 kHz, 256-frame blocks, seed 1 (TV10: 77). The channel is the bowl, or `mix` (the unit's one channel).
- Managed and Burst (`FloatMode.Strict`) must both match the FNV-1a over the float32 bits, and the first values,
  exactly.
- The column "fused |Δ|" is for information only: it is the drift that `FloatMode.Fast` would show.

| Vector | Samples | FNV (strict) | First non-zero: first 4 values | Sum / Σx² / peak | fused \|Δ\| | contact / lost / stick, preempt / drops |
|---|---|---|---|---|---|---|
| TV1_tap_medium | 4800 | `0x17dbf306` | 38: 3.67748044e-7, 0.000576838385, 0.00202652067, 0.00426616333 | −2.945954365 / 33.71656709 / 0.13291733 | 6.6e-5 | |
| TV2_palm_large | 4800 | `0xfa02a20d` | 301: 1.57282211e-5, 4.22019075e-5, 7.60245675e-5, 1.16610572e-4 | 2.953658862 / 15.43310984 / 0.0892755538 | 2.6e-5 | |
| TV3_two_taps_overlap | 4800 | `0x0808172b` | 11: 2.95211183e-4, 1.32693769e-3, 3.22317239e-3, 5.79493586e-3 | −2.395664064 / 60.77558665 / 0.202984437 | 4.9e-5 | |
| TV4_damp_palm | 9600 | `0xdf18a4d5` | 1: 6.33312823e-7, 9.93428053e-4, 3.48890224e-3, 7.34077208e-3 | 0.1497028303 / 53.73983503 / 0.203157693 | 1.07e-4 | |
| TV5_pickup_slew | 9600 | `0x660074f5` | as TV4 | 0.7843079963 / 144.700679 / 0.203157693 | 1.07e-4 | |
| TV6_rub_outer | 24000 | `0xd2871d2c` | 1: −2.40672726e-8, 6.75283663e-5, 2.3237303e-4, 4.79066453e-4 | 0.6568126823 / 4.076699891 / 0.0475357324 | 2.5e-5 | 23307 / 536 / 0 |
| TV7_rub_lip_reverse | 24000 | `0x226eb477` | 0: 1.93360385e-8, 3.74254583e-8, 4.07276057e-5, 1.43237907e-4 | −0.4125997472 / 2.29409725 / 0.0244854521 | 1.1e-5 | 21120 / 0 / 0 |
| TV8_rub_inner | 24000 | `0xda7d005d` | 0: 9.98426586e-9, 1.00764694e-8, 4.06753752e-5, 1.4315595e-4 | −0.4243472147 / 2.400064266 / 0.026202511 | 9.9e-6 | 23307 / 0 / 0 |
| TV9_resting_finger | 24000 | `0xcb02b6ab` | as TV4 | 0.05439479804 / 99.73604396 / 0.203157693 | 1.29e-3 | 15840 / 2494 / 11695 |
| TV10_seed77_rub | 12000 | `0xa7eb0fe4` | 1: −2.40762752e-8, 6.7531626e-5, 2.32401988e-4, 4.79142444e-4 | −0.03600089707 / 0.5762115957 / 0.0231981743 | 8.3e-6 | 12000 / 497 / 0 |
| TV11_retire | 1,440,000 | `0x6c53c005` | retires at block **3759** | −2.841399019e-5 / 7.908465784e-5 / 4.10482753e-5 | 2.5e-8 | |
| TV12_suede_wood | 9600 | `0x06840126` | 21: −4.77365596e-8, 7.32079352e-5, 2.75556406e-4, 6.3336629e-4 | −3.786127658 / 40.01159128 / 0.130568415 | 5.3e-5 | |
| TV13_lane_preempt | 24000 | `0x6c0f06fb` | 1: −2.76495342e-8, 5.9844755e-5, 2.07975099e-4, 4.33127774e-4 | −0.3972319374 / 0.9247925207 / 0.0217110366 | 7.8e-6 | 23147 / 1228 / 1, **1 / 20** |
| TV14_glide_ramp | 14400 | `0xc6e1f6ea` | 1: −3.66856554e-8, 9.80197292e-5, 3.44357541e-4, 7.24094163e-4 | 0.4135703114 / 1.772331415 / 0.0340008065 | 7.6e-6 | 13430 / 658 / 0 |
| TV15_share_cap (mix) | 9600 | `0x8494863d` | 11: −1.64867785e-7, 5.51640405e-4, 1.56137371e-3, 3.15105636e-3 | −3.021059227 / 150.8391254 / 0.307743192 | 8.0e-5 | Share 0.4 at block 16, 1 at 30 |
| TV16_mix_three (mix) | 9600 | `0xf133bb7b` | 6: 6.0195789e-6, 3.75432282e-5, 1.08852117e-4, 2.23478273e-4 | −1.133987386 / 15.27081931 / 0.0896377414 | 2.2e-5 | |

**Design-table fingerprints** (`out/test-vectors.json` `design`):
- big: Y0t 7.028226e-5, outScale 309.65006, mode 0 pr 0.99967033 / pi 0.025594948 / gi 1.0597294e-7 / om 1228.6942,
  mode 2 contact-on pr 0.99742019;
- middle: Y0t 1.1245161e-4, outScale 413.333333;
- small: Y0t 1.7570565e-4, outScale 520.767202.

### 11.2 EditMode tests (`Tests/EditMode/Audio/Bowls/`, `Tests/EditMode/Bowls/`; `OfflineEngine(UnitId.Bowls)`)

1. **`BowlsDesignTests`.** The tables equal the fingerprints, including the 8 pole tables. Every mode is < 0.45 sr,
   the ratios are the published lists, and the n = 2 poles are the same with the contact on.
2. **`BowlsVectorTests`.** TV1–TV16 bit-exact, managed and under Burst (Strict).
3. **`BowlsPhysicsTests`** (render and measure, as `verify.mjs`):
   - mode peaks within 0.05 Hz, beat = split ± 0.05 Hz, T60 ± 3 %, the hard strike within 1 dB of [WSN02];
   - the rub map cells at 0.2 / 0.3 m/s sing on every bowl at 2.5 / 4 / 6 N within 2.5 s;
   - 1.5 m/s never sings; the inside is ≥ 20 dB under the outside;
   - 4 ± 0.4 beats per revolution; the damping order;
   - no clicks; zero subnormals over 240 s; the adversarial workout stays finite;
   - **the tap-then-rub sweep (576 runs, all sing within 3 s)**;
   - \|e\| − 1 ≤ 1e-5 over a 60 s rub;
   - Damp(1) keeps ≥ 90 % of the song.
4. **`BowlsUnitTests`.**
   - Reset silences everything and leaves no state.
   - A LateDrop Strike more than 100 ms late is dropped and counted; a late Contact is applied.
   - Lanes: lane 0 preempts lane 1 (TV13); lane 2 over lane 1 is dropped; a Release from a non-owner is ignored.
   - Share 0.4 lowers `Work` ≥ 50 % (−75 % measured) with a step ≤ own + 1 %.
   - Idle skip: after the last bowl retires `ChannelsUsed` is 0, and the first strike after a skip lands on its sample.
   - No allocation in `RenderBlock` or the public API.
5. **`BowlsRubEncoderTests`.** `rub-encoder-vectors.json`: the 425 contact frames give exactly the 140 events
   (types, times and fields to 1e-6), and `decode` gives the listed lane-1 commands.
6. **`LoudnessParityTests`** (the platform's). The mf reference phrase (section 9) is within ±2 LU of the piano's;
   the 0.3 m/s rub ≤ +3 LU and the 0.45 m/s rub ≤ +6 LU.
7. **`BowlsPerfTests`** (`test-perf`). The worst case (all three rubbed, chatter) ≤ 0.25 ms per block with Burst on.
   Log a `[PERF]` line.
8. **`BowlsLessonPackTests`.** The pack loads; `Validate` passes on every target; the 7.4 rules hold.

### 11.3 Interaction cases (PlayMode `BowlsInteractionTests`, `FakeHandSource`; the scripts are in `interaction-cases.mjs`)

All 25 pass in the reference: 30 Hz tracker (36 or 90 where stated), 72 Hz frames, seeded jitter, the unit running.

**Basics** (16):
- **tap**: one strike (V 0.835), no contact.
- **first-timer rub** on each bowl: `rubStart` 0.39 s after the touch; mean v_f within 1.6–2.5 % of the truth; sings
  0.66 / 0.35 / 0.26 s after the touch, 100 % of 2.4–6.4 s; 1 release.
- **rest**: no strike above 0.25 m/s, no contact; Damp(1) 0.83 s after the entry, never 2; 0 after the lift.
- **tap then rub** on each bowl: one strike, then `rubStart` with no second strike; sings to the end.
- **hover**: nothing.
- **loss 80 ms** keeps the rub; **loss 400 ms** releases once and resumes with no strike.
- **bounce**: one strike. **glitch**: no strike.
- **inner wall**: INNER contacts only, no song.
- **palm rest**: Damp 3, then 0; no palm strike.
- **two hands**: the left rubs the small bowl (100 % singing) while the right taps the big one 3 times.

**Gentle** (H2):
- 108 pokes (0.2 / 0.3 / 0.5 m/s × 1 / 2 / 4 cm × 0 / 0.1 / 0.25 s dwell × 4 tracker phases): each exactly one strike
  with V ≥ 0.94 × the poke speed, no contact; the ring after a 250 ms dwell within 0.9 dB of no dwell.
- Tap and hold 0.12 / 0.25 / 0.5 s: one strike, the ring unchanged (0 dB).
- Double taps 150–350 ms apart: two strikes.

**Fast** (M2):
- 1–2 m/s at 30 Hz and 3 m/s at 60 Hz, 5–20 mm, tracker phase swept: 180 / 180.
- 3 m/s at 30 Hz (information only): 16, 17 and 20 of 20.

**Fingers** (H3), 15 runs, 5 mm jitter: index only, two-finger rubs (same depth, 6 mm shallower), a flat hand, a
curled dipping middle finger, on all three bowls. Each sings within 1.1 s; no damping is sent.

**Freehand** (M1), 9 runs: off-centre 8 mm with a 10 mm ellipse; a 15 mm ellipse at 4 mm depth; pressed 15 mm with a
15 mm ellipse, on all three bowls. Each sings within 1.54 s, 100 % of 2.4–6.4 s, with 1 release.

**Puja**:
- the shaft rubbed round the middle bowl sings 0.45 s after it lands, with one tick;
- the suede end gives one `Suede` strike (V 0.97).

**Add for Unity** (platform 12.2): system gesture (no calls), rig held, arrange mode, glide (contacts released),
desktop (a click taps, a circle-drag rubs, the mouse hand never plays), the puja's pick-up and lay-down, and
`IBowlsFixture` producing every declared Onset.

### 11.4 On-device checks (add to ARCHITECTURE §19)

| # | Check | Pass |
|---|---|---|
| B1 | First-timer rub: five people who never played circle the middle bowl after Sunny's tip | ≥ 4 of 5 sing within 5 s |
| B2 | Gentle taps: five people tap each bowl "gently" (Sunny's words) | every tap rings audibly; no choked ring when the finger lingers |
| B3 | Tracker rate (`sampleInterval`) at LOW; hard taps at 2–3 m/s | v_f deadband holds a resting finger at 0; hard taps detected ≥ 90 % (else set `FastMotion`) |
| B4 | Fingertip clamp on the rim (ContactVisuals) | The drawn finger stays on the metal at 2–20 mm of penetration |
| B5 | Chatter cue: rubbing fast and light | Audible rattle; `tip_bowls_slower` fires once |
| B6 | Reach and comfort at the home, seated on a chair and on a sofa | All rims reachable without leaning; no clash with the real knees or armrests |
| B7 | CPU: three bowls rubbed plus the T-STRESS load, passthrough on | `DspLoadMeter` p99 ≤ 2 ms, 0 underruns; the bowls' share ≤ 0.25 ms |
| B8 | Spatial: the bowls sound from beside the left knee; leaning in changes the beating | By ear |
| B9 | MR: the bowls on a real table and on the floor, shadows, cutout hands | Correct placement |
| B10 | Draw calls (Frame Debugger) | 5 at the seat, 3 on the sill |
| B11 | The puja: pick-up by pinch, suede strike, shaft rub, laid back on leave | Feels intentional; sings like a finger |
| B12 | A loop of a rub, and the live hand rubbing the same bowl | The live hand takes it cleanly; the loop resumes after |

---------------------------------------------------------------------------------------------------------------------------

## 12. Renders (`Builds/Listen/bowls/`, see bowls-README.txt)

All files are 48 kHz, stereo 24-bit, −18.0 LUFS integrated, true peak −8.3 to −14.0 dBTP: 12 files, 59.3 MB. They are
dual mono (the unit's one channel, dry), except the room piece. The critic's `measure-renders.mjs` confirms each
file's loudness, true peak, DC (≤ −78 dB), < 60 Hz (≤ −38.7 dB) and step ratios.

| File | s | What |
|---|---|---|
| `bowls-singles.wav` | 16 | One tap per bowl, 4.5 s each: the beating at 0.9 / 1.4 / 2.0 Hz |
| `bowls-strikes-velocity-ladder.wav` | 22 | Taps at 0.3 / 0.7 / 1.2 / 2.2 m/s per bowl, a palm mute between |
| `bowls-tap-palm-low-hard.wav` | 13 | Middle bowl: tap, palm, a low tap, a hard tap |
| `bowls-rub-sing.wav` | 18 | Rub 0.33 m/s for 10.5 s (4 beats per turn), lift, ring-out |
| `bowls-tap-then-rub.wav` | 12 | Big bowl: tap, then the finger circles (worst step ratio 4.0, was 8.5) |
| `bowls-rub-slow-right-fast.wav` | 20 | Too slow (faint), right (sings), too fast and light (rattles) |
| `bowls-damping.wav` | 12 | Revision 2: two fingers (≈ 1 s), a palm (at once), one light finger (a slow fade) |
| `bowls-inside-vs-outside.wav` | 11 | The inside does not sing, the outside does |
| `bowls-piece-plain-hunt.wav` | 28 | Plain hunt on three bells (Stedman, *Tintinnalogia*, 1668) |
| `bowls-piece-evening-bells-room.wav` | 26 | A rubbed E drone under "ding, dang, dong" (Frère Jacques, c. 1780) ×2. From the seat, through the room lab and MasterBusV2: the one Bowls source at the bowls' centre, the ER group at the stand, send −1 dB, the listener turned toward the bowls. No limiting |
| `bowls-first-timer-hands.wav` | 15 | **New.** Scripted hands through `BowlsHands` at 30 Hz: three gentle lingering pokes, then a wobbly two-finger circle that sings |
| `bowls-puja.wav` | 13 | **New.** The puja: suede strike, wooden knock, shaft rub |

Revision 1's dry `bowls-piece-evening-bells.wav` was removed, to stay under 60 MB with the two new files.

What the user should judge, in order:
1. Does the tap sound like metal bowls? The brightness knobs are TAP T_ref 0.9 ms and RAD_DB.
2. Is the wah-wah pleasant? (The split table; FILL for rubbing.)
3. Does the rub start right and sound real, and is the soft onset "shh" acceptable? (MU_S, MU_INF, VC; NOISE_K.)
4. Is the rub now balanced against the taps? (OUT_GAIN with TAP.m.)
5. Is the decay too long for lessons? (SUPPORT ×4 → ×6 shortens the fundamental by a third.)

---------------------------------------------------------------------------------------------------------------------------

## 13. Risks, open points, UNVERIFIED

- **Not listened to.** All judgements are measured.
- **The rub onset hiss.** Before the tone grows, the finger slides at its full speed, so the friction noise (∝ F_c\|u\|)
  is loud next to a quiet ring: up to −10 dB re a soft tap's ring for 0.4 s after a tap-then-rub (`dev/buzz.mjs`). It
  is the noise model, not a rattle: with the noise off the energy above 2 kHz is ≤ −31 dB. It is physical for a stick.
  If the user dislikes it, the knob is NOISE_K.
- **Contact tissue loss** (CONTACT_ALPHA 1.5) is a design value. It is not measured on fingers on bronze; its effect
  on the rubbed tone is a purer song ((3,0) at −54 to −61 dB re (2,0) in the renders).
- **Cushion damping, the finger as a puja, the 20 g tip mass, the s^n height factor, the (7–9,0) partials:** ASSUMED,
  as in revision 1.
- **`FloatMode.Strict` under Burst** is expected to give bit-exact vectors (no FMA contraction). UNVERIFIED until
  `BowlsVectorTests` runs; if Burst still fuses `math.mad`, use the Fast tolerances (block RMS 0.01 dB linear, 2 dB
  nonlinear) and file it against the platform's 1e-5 gate.
- **The display-time prediction** (4.5) is emulated in the lab. The device's `position[]` prediction and jitter
  decide B2/B3.
- **The pocket is tight.** 0.41 × 0.29 m with 30–43 mm rim gaps. A fingertip circling the big bowl passes the middle
  bowl at 42 mm: fine for a rub (it cannot touch another bowl), but a wobbly approach may tick a neighbour. B6 decides;
  the lever is a smaller set (C4/E4/G4 at 150/125/110 mm).
- **3 m/s taps at 30 Hz** (4.5): 80–100 % detected; FastMotion is the device-side option.
- **Platform requests R1–R5** (3.12) are pending the platform agent. Without R1 the bowls sit 2.7 dB wetter than the
  gate allows (+2 dB send).
- **Other agents' footprints** are provisional, so the planner's 26 / 28 will move when they land.

---------------------------------------------------------------------------------------------------------------------------

## 14. Files and how to run (run from the bowls lab folder)

| File | Run | Writes |
|---|---|---|
| `bowls-design.mjs` | `node bowls-design.mjs` | Prints the design table |
| `bowls-engine.mjs` | (module) | The reference unit |
| `layout.mjs` | (module) | The rig: stand, bowls, puja, seat spec |
| `interaction.mjs` | (module) | `BowlsHands`, `PLACED`, `FACING`, `IX` (the critic's scripts import it) |
| `interaction-cases.mjs` | `node interaction-cases.mjs` (≈ 30 s) | `out/interaction-cases.json` |
| `rub-encoder.mjs`, `lanes.mjs` | `node lanes.mjs` | `out/lanes.json`, `out/rub-encoder-vectors.json` |
| `lib/scenarios.mjs`, `lib/hands-sim.mjs`, `lib/analysis.mjs` | (modules) | Scripted play, scripted hands, FFT / T60 |
| `verify.mjs` | `node verify.mjs` (≈ 50 s) | `out/verify.json` |
| `verify-inacio.mjs` | `node verify-inacio.mjs` | `out/verify-inacio.json` |
| `tools/conformance.mjs` | `node tools/conformance.mjs` | `out/conformance.json` |
| `tools/seat-check.mjs` | `node tools/seat-check.mjs` | `out/seat-check.json` (imports `Tools/SoundLab/platform/` read-only) |
| `test-vectors.mjs` | `node test-vectors.mjs` | `out/test-vectors.json`, `out/vec-*.f32` |
| `bench.mjs` | `node bench.mjs` | `out/bench.json` |
| `make-lessons.mjs` | `node make-lessons.mjs` | `out/lessons-bowls.json` |
| `render.mjs` | `node render.mjs` (≈ 8 s) | `Builds/Listen/bowls/bowls-*.wav`, `out/renders.json` |
| `dev/*.mjs` | `tapdead*.mjs`, `rubmap.mjs`, `buzz.mjs`: the M3 / noise investigations; older explorations | stdout |
| `critic/*.mjs` | The independent critic's probes (unchanged), re-runnable on this code | `critic/out-*.json` |

The lab imports `../burst/lab/common.mjs` and `kernels.mjs`, `../room/room-engine.mjs`, `../piano/` (the parity
reference) and `../platform/` (the planner), all read-only.

---------------------------------------------------------------------------------------------------------------------------

## 15. Sources

- **[I06]** O. Inácio, L. L. Henrique, J. Antunes, "The dynamics of Tibetan singing bowls", *Acta Acustica united
  with Acustica* 92(4), 637–653 (2006). Table I, Table II, damping 0.002–0.015 %, eqs. 12–14, sections 5.1–5.4,
  Figs. 8, 12, 13, 15–18.
- **[I03]** O. Inácio, L. Henrique, J. Antunes, "The physics of Tibetan singing bowls", Forum Acusticum, Bilbao 2003.
- **[TB11]** D. Terwagne, J. W. M. Bush, "Tibetan singing bowls", *Nonlinearity* 24, R51–R66 (2011).
- **[WSN02]** C. Wilkerson, S. Serafin, C. Ng, "Physical model synthesis and performance mappings of bowl
  resonators", ICMC 2002, Table 1.
- **[DA16]** V. Debut, J. Antunes, "When singing bowls don't sing", Paper 60 (2016).
- **Music.** F. Stedman, *Tintinnalogia* (1668): plain hunt. Frère Jacques: manuscript c. 1780 (tutorials-audit.md 3).
- **Local.** platform.md (the contract), ENGINE-API.md, burst.md, 02-meta-hands.md (joints, prediction, sample
  timing), sunny-tour.md, the lesson set (`lessons.json`) and tutorials-audit.md, ARCHITECTURE.md, the side table, the platform
  lab (`registry.mjs`, `seat-planner.mjs`, `stow-plan.mjs`, `control-decimator.mjs`), the marimba and hand-drums specs
  (the conformance method), and the critic's scripts.

---------------------------------------------------------------------------------------------------------------------------

## 16. ARCHITECTURE.md section 21 entry (draft, for step 4 of platform.md 13)

> **2026-11-xx, singing bowls (12-new-instruments/bowls.md, revision 2).**
> - `MusicRoom.Bowls` (Kind / Rig / Unit 9, `SourceId.Bowls`, carousel 3).
> - Three physically modelled bowls (split mode pairs, a stick-slip friction contact with lanes, touch and tissue
>   damping) on the Burst graph: `BowlsUnit`, `BowlsDsp` (FloatMode.Strict, idle skip, Share work cap).
> - `BowlsHands`: gentle taps, rub on motion, sticky rubs, the swept and predicted entry test. The puja through
>   `IHandToolProvider`.
> - Displayed on the window sill.
> - Lesson pack `bowls` (3 lessons); 10 Sunny tips.
> - Requests R1–R5.
> - Verification: section 20.x.
