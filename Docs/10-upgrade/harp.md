# 10-upgrade · Harp: a live, coupled string model (implementation-ready spec for Unity C# + Burst), v2

> **2026-11-11: not in the app.** The physical harp unit built from this spec (`Audio/Sound/Harp`), its path in the harp module and its lab copy (`Tools/SoundLab/harp/`) were deleted: the harp plays the classic sound only (the user's choice). This spec is the design record. Backup: `music-room-vr-wip/unity-backups/pre-classic-cleanup/`.

Key `harp`. Lab code (Node 26, no packages): the harp Node lab, with the parts the tests need vendored at `Tools/SoundLab/harp/`.
A/B renders: `harp-*-old.wav` / `-new.wav` (the `-new` side is in `Tests/EditMode/Audio/Harp/Reference~/renders/`), with a
plain-English listening guide. Reference engine to transliterate: `Tools/SoundLab/harp/harp-engine.mjs` (float32 state,
structure-of-arrays, no allocation in `renderHarp`, seeded RNG). This spec replaces spec 06 §5 (the harp) and the harp rows
of ARCHITECTURE §7.

**v2** is the revision after the independent critique (the lab's `critic/` scripts). Every high and medium issue is fixed;
section 0.1 lists each one with its evidence. The v1 engine, scripts, outputs and spec are kept in the lab's `v1/` folder (runnable).
**Nobody has listened to either version yet:** every judgment here comes from measurement. The user decides by ear (section 11.5).

## 0. What changes, in one paragraph

The old harp plays a pre-rendered Karplus-Strong buffer per string: a noise burst, two-point average, peak-normalised, random
level per string, plus a fixed soundbox convolver. The new harp runs **the 22 strings live as 44 digital waveguides (two
polarisations per string) terminated on one shared bridge**: 8 soundboard/air modes plus a broadband conductance. The
following then come from the physics and need no extra rules:
- sympathetic resonance: octaves and fifths hum, damped strings don't;
- body colour that changes by register, and partials near a body mode decaying faster;
- two-stage decay (a strongly coupled in-plane polarisation plus a longer out-of-plane tail);
- a finger that really stops a ringing string when it catches it, and a palm that damps the highs first;
- even voicing across the strings (the old varies by 12 dB at random).

Only strings that ring, are being plucked, or are a harmonic relative of a ringing string are computed (**active set**); the
rest are frozen. It costs an estimated **29 µs per 256-frame block with one note ringing and 55 µs with all 22 strings
ringing** on a Quest 3S core at CPU level 3 (float4 kernel, section 8), about 42 KiB of memory, and **frees the 12.2 MiB of
baked harp buffers** and the 22 boot render jobs.

### 0.1 The critique and what v2 does about it

| # | sev. | critique (v1) | v2 fix | measured now (v1 → v2) |
|---|---|---|---|---|
| 1 | high | C4–E4 (49 % of lesson notes) plucked at the glow, u ≈ .47–.50: even partials 20–31 dB down, hollow like a clarinet | Lesson play point clamped to u ∈ [.30, **.40**] (was [.30, .70]); calibration re-run at the new points | C4 partials 2–4: −34.9 −11.3 −39.3 → **−7.0 −8.6 −9.6 dB**; D4 −31.5 −13.3 −37.7 → −8.1 −11.8 −13.1; E4 −22.4 −12.2 −31.3 → −7.7 −11.2 −13.8 (`verify-fixes.json` hollow) |
| 2 | high | Pokes (the usual VR input) 2.7 dB quieter than sweeps; calibration on sweeps only; soft plucks 3.8 dB under the old | Full polarisation make-up (`POL_MAKEUP` 1, was ½); calibration on a 70 % poke / 30 % sweep mix; a poke-only lesson A/B (`harp-lesson-jingle`) | poke − sweep **−2.67 → −0.11 dB** (mean of 8 strings); poke − old +0.96 dB; soft poke vs old at 0.2 / 0.3 / 0.6 m/s: −4.4 / −2.6 / −3.8 → **−2.4 / −0.3 / −1.3 dB** |
| 3 | med | Every A/B pair +11–15 dB at 100–160 Hz and −8 to −13 dB at 2–5 kHz: the A/B would judge an EQ change | Radiation chain re-balanced against the old (2nd-order HP 250 Hz, A0/T1 radiation .48/.42, shelf +20 dB 300→3000 Hz, 4th-order LP 7 kHz, coherent pluck corner 1.2 fc, noise layer −3 dB) plus a 5-section allpass "board dispersion" that keeps the crest factor at the old's; `BRIGHT_DB` dev-menu tilt knob | Critic's in-room LTAS, mean of the pairs, new − old: 125 Hz **+13.7 → +4.7**, 3.15 kHz **−10.4 → −4.2**, 4 kHz −10.6 → −3.5, 12.7 kHz +8.3 → −3.2 dB; crest factor vs old −0.7 (mf) / −1.9 (ff) dB |
| 4 | med | CPU estimated at 2.36 GHz (app runs at 1.65 GHz); same cost with one note as with all; disagrees with burst.md (9 µs) | Active set (default); `harpSetMaxActive` budget hook; float4 SoA kernel spec; estimates re-stated at 1.65 GHz with a cycle model cross-checked by burst.md's own WG4 ratio | One note ringing: 9.3 of 22 strings run; Quest float4 **29 µs** (one), 43 µs (lesson), **55 µs** (all ringing); scalar-Burst upper bound 358 / 554 / 672 µs (Node × 2.86), p99 ≤ 806 µs |
| 5 | med | Proposed command kind 3 collides with `SynthCustom.Seed = 3` | Harp kinds **10–13**; the processor handles `Seed` by re-seeding both RNG streams; engine `harpSeed` | TV8: a Seed alone gives max \|out\| = 0 and the harp stays asleep; re-seed 77 then pluck = fresh engine seed 77, bit-exact |
| 6 | med | A 2nd pluck during the 4 ms pre-damp played twice (flam, truncated excitation, DC) | Plucks merge: into a pending pluck (louder wins); during a running excitation an equal/softer one merges, a louder one raises it in place with an exact DC correction; an excitation is never truncated | Critic's case: excitation starts **[0, 24000, 24096] → [0, 24192]** (critic's own `double-pluck.mjs`: one start, identical to a single re-pluck) |
| 7 | med | Touch(up) cancelled a palm damp (+19 dB) | Damping is a small state machine: finger beats palm beats nothing; a lifted finger falls back to the palm; HarpModule re-sends Damp every 0.2 s while a palm rests | RMS 26000–30000: palm only −57.5, with Touch(up) **−57.6 dB** (v1 −58.5 / −39.2) |
| 8 | med | The 4 ms catch pre-damp reached only −3.4 dB; repeated notes varied 1.6–2.6 dB | The catch is a smooth whole-string fade to −30 dB in 4 ms (lazy loop scaling `sgR`/`sgW`, section 5.5): a loss filter cannot do it, because a C3 period (7.6 ms) is longer than the window | True string energy at the new excitation **−3.4 → −29.5 dB**; repeated-note peak spread 2.56 / 1.59 / 2.29 → 1.68 / 1.53 / 1.60 dB (the rest is the intended ±0.3 dB, ±.03 β humanisation) |
| 9 | med | A5–C6 upper partials decayed ~2× faster than the design's own targets (the fixed two-point average): glassy, near-sine | Loss FIR `(1 − w) + w z⁻¹` with `w` fitted per loop when even a = 0 over-damps (G5–C6) | C6 partials 2 / 3 T60 0.63 / 0.30 → **1.07 / 0.60 s** (targets 1.08 / 0.61); C6 partials 2–5: −6.1 −22.4 −23.9 −38.4 → −1.8 −16.7 −11.1 −15.3 dB |

Not taken as proposed (with reasons):
- **#3, a third render `harp-*-newbright`.** The default itself is now within about ±6 dB of the old in every band of every
  piece, so the A/B is not an EQ contest any more. A third file per piece would also break the ~60 MB render budget and
  give the user three-way choices instead of a simple A/B. The tilt knob the critic asked for is there (`BRIGHT_DB`,
  section 3; ±3 dB moves the 2.5–5 kHz band by −1.4/+1.1 dB and the 10–16 kHz band by −2.3/+2.4 dB).
- **#4, "add the harp to burst.md's budget table".** This key may only write `10-upgrade/harp.md`. The row for burst.md's
  owner is in section 8.3.
- **#1, u ≈ .42.** v2 uses .40. The comb puts partial 2 at −12 dB re partial 1 at u = .42 and at −10 dB at .40. The 5th-partial
  null at exactly .40 is blurred by the ±.03 humanisation, and .40 sits only 2 cm lower than .42 on C4.
- **#8, "T60 10–12 ms with 1 ms smoothing".** A loop-filter damping only touches samples that pass the filter: in 4 ms that
  is half of C3's loop, so C3 cannot go below about −3 dB however strong the filter. The distributed fade reaches −29.5 dB on
  every string.
- The critic's `engine-tests.mjs` pre-damp probe reads the raw delay memory and so reports +0.5 dB on v2: v2 keeps the
  fade as a per-loop scale (`true = stored × sgR`) until it bakes it at the pluck. `verify-fixes.mjs` and TV11 measure the
  true value.

## 1. Why: the old sound, measured (dry, direct + body, every string at the reference stroke)

- **Twangy, slightly metallic bass.** The old's bass partials ring almost as long as the fundamental: at C3, partial 8 has
  T60 3.2 s against 4.5 s for the fundamental. The energy above 3 kHz at 0.5–1 s is −37 dB (C3); the new is −68 dB.
- **Clipped treble.** The old C6 buffer stops ringing early (T60 0.77 s, nothing left after 1.4 s); the new rings 1.9 s.
- **Random voicing.** The old's strings vary by 12 dB at random (−20.5 … −32.6 LUFS); the new follows a gentle 3.6 dB tilt.
- **Flat dynamics at the top.** The old's loudness and brightness stop changing above 1.3 m/s; the new spans 17.6 dB with
  brightness rising throughout (section 5.1).
- **No physics.** It cannot damp, re-catch or resonate: the old fades a voice out instead of stopping a string.

An earlier candidate upgrade (a `useNew` switch in the strings synth) kept the KS core and added fixed "hum" sinusoids; it shares those
limits. Choices made for the "the upgrade was worse" history:
- **The same mean loudness as the old harp** at the same stroke, now weighted to pokes (section 5.8).
- **The old's tonal balance, a little warmer:** long-term spectrum within about ±6 dB of the old in every band of every A/B
  piece; 2.5–5 kHz about 3 dB under, 10–16 kHz about 4 dB under (section 10.7). Attack centroid 1.8–2.4 kHz (old 1.6–2.7).
- **No metallic ringing:** the highs die fastest on every string (max high-partial T60 ratio 0.76; old 0.92).
- **The old's crest factor** (no spiky attack that a limiter would catch): within −1.9 … +1.8 dB of the old.
- **A moderate sympathetic halo:** −29 to −17 dB in the tail (section 10.4).
- **No compression, no added reverb:** the room stays the AudioRig's job.

## 2. Signal flow

```
 main thread: HarpModule ── Pluck / Damp / Touch / Hold (+frame) ──► SynthCommand ring (SourceId.Harp) ──┐
 audio thread (Harp SynthSource.OnAudioFilterRead, 256 frames, Burst function pointer):                   ▼
   per frame n:  commands due at n → apply (pending pluck, merge, damping flags, seed …)
     prologue  per string: a pending pluck waits for a running excitation, then (if the string rings and no finger holds
               it) the catch fade sgR = sgW⁻¹ = 10^(−30/20 · smoothstep(t/192)); at t = 192 the scale is baked into both
               delay lines and the pluck starts; palm-hold countdown; damping smoothing
     phase 0  e_i(n) = finger excitation of each exciting string (coherent comb pluck + one period of HF noise)
     phase 1  for each ACTIVE loop j (in-plane j = i, out-of-plane j = 22 + i):
                r  = delay_j[pos_j] · sgR_j                 N_j integer samples
                t  = Thiran1(eta_j)(r)                      fractional part of the period
                d  = Allpass1(c_j)(t)                       stiffness dispersion
                s1 = OnePole(g_j, a_j)(d)                   frequency-dependent loss; damping moves g, a
                s  = (1 − w_j) s1 + w_j s1[n−1]             loss FIR (w = .5 except the top strings)
                x_j = s + w_pol · e_i(n)
     phase 2  F_i = kap_i (x_i + C_OUT x_{22+i})  (active strings);  Ft = Σ F_i
              b_k = BP_k( Σ_i phi_ki F_i )    k = 0..7    unit-peak RBJ band-passes (positive-real)
              vb_i = GBB · HP40(Ft) + Σ_k Y_k phi_ki b_k  bridge velocity seen by string i
     phase 3  delay_j[pos_j] = (x_j − cPol_j · vb_i) · sgW_j ;  pos_j++  (cPol = 1 in-plane, C_OUT out-of-plane)
     output   y = G_OUT · DC20( AP5( LP4_7k( Shelf300→3000( HP2_250(Ft) ) ) ) + Σ_k rho_k b_k + flesh + shh )
   block end: per-string AC level (blocks ≥ 64 frames), active-set update, sleep check
   → mono into the Harp source (Meta XR spatializer, ReverbSendDb +3 as today)
```

Why it is stable: each string sees the bridge as `(1 − q(z))` per round trip, with `q = cPol² kap Y(z)`. `Y` is a sum of
positive-real band-passes plus a constant conductance through a first-order high-pass, which is also positive-real. The loop
filters are passive (|H| < 1: the one-pole with g < 1 and the FIR with w ≤ .5), so the coupled network is passive; freezing a
string removes it from the network, which keeps it passive; the catch fade only scales a loop down. Checked numerically: a 60 s
random workout with every value finite and the peak bounded (section 10.5), the critic's 10 s adversarial test (every string
at vel 1 every 5 ms near the board, touches and damps at audio rate: finite, delay state ≤ 4.7), 0 denormals after 120 s awake.

## 3. Constants (every one, with its source)

| constant | value | source / reason |
|---|---|---|
| strings, pitches | 22, C major C3 (48)…C6 (84), `midi = 48 + 12⌊i/7⌋ + [0,2,4,5,7,9,11][i%7]` | spec 05 §5.1 |
| LEN (m) | 1.1369 … 0.215 (table §4) | `Docs/05-instruments.md` (harp strings, `len`) |
| board position of string i | `xb = .045 + .82 t`, t = i/21 | spec 05 §5.1 BOARD_RANGE |
| MATERIAL | C3 D3 wound (copper on nylon core), E3…G5 gut, A5 B5 C6 nylon | lever-harp stringing practice |
| TENSION | 135 N (C3) → 80 N (C6), linear | design choice inside the usual 60–160 N lever-harp range; only sets Z and B |
| RHO | gut 1300, nylon 1140 kg/m³ | gut ≈ 1.3 g/cm³ (Bell & Firth, Acustica 60, 1986); nylon 6.6 handbook value |
| E | gut 5 GPa, nylon 5 GPa, wound core 4 GPa | order of magnitude; B ≤ 1.3e-4 everywhere, a sub-cent effect |
| B | `π³ E d_core⁴ / (64 T L²)` | Fletcher, JASA 36 (1964) stiff-string formula |
| T60_C3, T60_OCT | 7.0 s, 18 semitones per halving | design target (bass longer than the old); UNSW harp page: "fundamentals up to 200 Hz decay very slowly" |
| T60_MAT | wound 1.25, gut 1.0, nylon 1.12 (3-tap smoothed) | metal winding has low internal loss; gut is the lossiest |
| OUT_T60 | **1.4** (v1 1.7) | weakly coupled polarisation rings longer (two-slope decays, UNSW; Weinreich, JASA 62, 1977). v1's 1.7 made pokes ring 10–27 dB louder than the old at 2–3 s |
| KHF | wound 1.1e-6, gut 1.6e-6, nylon 0.9e-6 s⁻¹Hz⁻²; out-of-plane × 0.85 | `σ(f) = 6.908/T60(f0) + KHF (f² − f0²)`: highs die fastest ("intensity decreases fastest from around 5 kHz", UNSW) |
| FIR_W | 0.5, fitted down for G5–C6 (w = .28 … .10) | loss FIR `(1 − w) + w z⁻¹`; w = .5 is the two-point average; section 4 step 2 |
| VAR_T60, VAR_FC | ±4 %, ±3 % fixed per string (mulberry32 seed 7) | no two real strings are identical |
| DETUNE_HZ | out-of-plane loop +0.12…0.34 Hz (seed 11) | polarisation splitting gives slow beats |
| GBB | 1.5e-4 | broadband bridge conductance; sets the sympathy level (section 10.4) |
| C_OUT | 0.18 | out-of-plane (parallel to the board) coupling, force and injection |
| MODES (f, Q, Y, rho, m) | (150,16,2.0e-3,**.48**,air) (205,22,2.5e-3,**.42**,1) (290,26,1.25e-3,.40,1) (427,28,1.75e-3,.35,2) (537,30,1.5e-3,.30,3) (662,30,1.5e-3,.26,3) (771,30,1.25e-3,.22,4) (1030,22,1.0e-3,.18,5) | A0/T1 = 112/146 Hz in a 29 L concert soundbox (Le Carrou et al., Camac Atlantide), scaled by √(29/16) for the app's ~16 L box (spec 05 §5.1); 202 and 427 Hz measured on small harps, 537/587/662/771 Hz from the plate model (UNSW); double resonances near 500 and 700 Hz (Waltham & Kotlicki). Q design values 15–30. v2 lowers the A0/T1 radiation (v1 .80/.70): C3–D3 boomed against the old |
| mode shapes | `phi_k(xb) = sin(m π xb)`; air mode `0.6 + 0.4 sin(π xb)` | half-waves along the board (reciprocity: the same phi for drive and pickup) |
| MODE_COMP, MODE_COMP_MAX | 0.5, 6e-4 | how much of a body mode's load on a string is given back in g |
| HF path | **HP 250 Hz 2nd order (Q .7) → shelf zero 300 / pole 3000 Hz (+20 dB) → LP 7 kHz 4th-order Butterworth → 5 allpasses → DC 20 Hz** | below T1 a small box radiates the broadband board motion poorly (the bass fundamentals still radiate through A0/T1); above it the radiated pressure follows the board acceleration (≈ +6 dB/oct over 300–3000 Hz); no hiss above 10 kHz. Tuned with `tune-eq.mjs` so every A/B piece's long-term spectrum is within about ±6 dB of the old's |
| OUT_AP | allpasses [f, Q] = [900, .8] [1500, .8] [2400, .8] [3600, .8] [5200, .8] | the dense plate modes above ~1 kHz scramble the phase of the radiated partials, so a plucked string's in-phase pulse does not reach the ear as a spike train. Spectrum untouched; crest factor 5–7 dB too high without it, within ±2 dB of the old with it |
| BRIGHT_DB | 0 (dev menu −6…+6) | tilt knob: multiplies the shelf pole by 10^(dB/20), i.e. moves the top gain by that many dB; design-time coefficient, recomputed on the main thread |
| VEL_DB | 18 dB amplitude range over vel 0…1 | realistic dynamics |
| harpVelocity(speed) | `clamp(ln(speed/0.10)/ln 22, 0, 1)` (0.1 m/s → 0, 2.2 m/s → 1) | release speeds up to 5 m/s, displacements up to 8 mm (Chadefaux, Le Carrou et al., JASA 131, 2012) |
| FC_LO, FC_HI, FC_TREBLE | 800 Hz, 7000 Hz, × (1 + .4 t) | release corner `fc = 800 (7000/800)^vel`: the pad pluck is warm, a strong pluck brighter |
| FC_COHERENT | **1.2** (v1 .7) | the coherent part's corner is 1.2 fc; v1's .7 left 2–5 kHz 10 dB under the old |
| REL_ORDER | 4: `S(t) = 1 − e^{−x}(1 + x + x²/2 + x³/6)`, x = t/τ, `τ = .0693 sr/(1.2 fc)` | −24 dB/oct release; the 2nd-order version left a periodic HF kink |
| FRIC, FRIC_HP | **0.15** (v1 .35) · √(fc/3000), high-pass 1800 Hz (two 1st-order), exactly one period, 0.3 ms fade-in | stochastic fine structure of the release (skin texture, slip): random-phase highs; lowered so the top is not hissy |
| POL_FLOOR | **0.30** (v1 .15) | every pluck excites both polarisations (rolling release: Chadefaux 2012). Pokes and sweeps now sustain alike |
| POL_MAKEUP | **1.0** (v1 .5) | the full across/through bridge-force level difference is made up (pokes as loud as sweeps) |
| BETA_MIN | 0.06 | pluck position limits |
| humanisation | β ± 0.03, level ± 0.3 dB, fc ± 3 % (engine rng, 3 draws per pluck) | pad width (1–2 cm), tracking noise |
| FLESH | 0.012 × level × (0.5 + vel): 1300 Hz band-pass noise, 4 ms decay | finger-pad contact |
| PALM_T60, PALM_A, PALM_HOLD | 0.10 s, loss pole ≥ 0.6, 0.35 s per call | a hand on the strings stops them in about 0.1 s (old: τ 18 ms ≈ T60 0.124 s), skin taking the highs first |
| TOUCH_T60 | 0.03 s | a finger holding a string |
| TOUCH_PRE, PRE_DB | 4 ms, −30 dB | catch of an untouched ringing string before its re-pluck: whole-string smoothstep fade (section 5.5) |
| DAMP_SMOOTH | τ 2 ms | damping engages and releases without clicks |
| SHH | band-pass 2200 Hz Q .8, level 0.012·√hands, 4 ms rise, −60 dB in 60 ms | the old "shh", same role |
| RINGING | 1e-6 (catch); 1e-8 (shh "moving") | block AC mean square of the string's two loops |
| ACTIVE, ACT_OFF, ACT_REL, REL_NMAX, REL_TOL | on, 1e-9, 1e-6, 6, 0.5 % | active set (section 5.7). ACT_OFF ≈ −100 dBFS of radiated output |
| SLEEP | AC loop mean square < 1e-8 **and** output peak < 1e-5 for 48 blocks → all state zeroed | idle cost ≈ 0 |
| G_OUT | **0.224556** | calibrate.mjs (section 5.8) |
| VOICING (dB) | −1.99 −1.97 −0.30 −0.36 −0.13 0.02 −0.09 0 −0.65 −0.17 −0.30 0.02 −0.14 −0.39 −0.67 −1.01 −1.51 −2.47 −3.23 −4.48 −6.02 −7.72 | calibrate.mjs: even voicing with a −0.1 dB per semitone tilt (top 3.6 dB softer). The large top-string trims compensate the radiation shelf |

Design-time helpers: phase delay of a first-order allpass `D(c,ω) = −[atan2(−sin ω, c + cos ω) − atan2(−c sin ω, 1 + c cos ω)]/ω`;
of the one-pole `atan2(a sin ω, 1 − a cos ω)/ω`; of the loss FIR `atan2(w sin ω, 1 − w + w cos ω)/ω`. One-pole loss
`−ln[(1−a)/√(1 − 2a cos ω + a²)]`; FIR loss `−½ ln(1 − 2w(1−w)(1 − cos ω))`.

## 4. Per-string design (computed by `designHarp(48000)`; implement it, or ship this table)

Per loop, double precision, then stored as float:
1. **Targets.** `σ(f) = 6.908/T60 + KHF (f² − f_l²)`, with `f_l = f0` (in-plane) or `f0 + detune` (out-of-plane).
2. **Loss fit (two-point; Välimäki et al., JAES 44(5) 1996).** `f2 = clamp(6 f_l, 1200, 5000)`, `need = (σ(f2) − σ(f_l))/f_l`.
   With L = one-pole loss + FIR loss: if `L(0,.5,ω2) − L(0,.5,ω0) < need`, keep w = .5 and bisect a ∈ [0, .95]; otherwise
   (the top strings) set a = 0 and bisect w ∈ [0, .5] on the FIR loss alone. Then
   `ln g = −σ(f_l)/f_l + L(a,w,ω0) − ln|1 − q_bb(ω0) − min(.5 Re q_modal(ω0), 6e-4)|`, with g ≤ 0.99995.
   The bridge load is `q(ω) = cPol² kap (GBB·HP40(ω) + Σ_k Y_k phi_ki² BP_k(ω))` (selfLoad in the reference).
3. **Bridge phase.** `bridgeDelay = −arg(1 − q(ω0))/ω0`, and `Dtot = sr/f_l − bridgeDelay`.
4. **Dispersion (3 passes).** Choose c ∈ [−.6, .6] (grid 0.01, then a ternary refine) to minimise
   `Σ_{n=2..nMax} [ΔD_model(n) − ΔD_target(n)]²/n`, with `nMax = clamp(⌊5000/f_l⌋, 2, 12)`,
   `ΔD_target = Dtot/√(1+Bn²) − Dtot`, and the model the sum of the allpass, one-pole, FIR and Thiran phase delays at
   `n f_l √(1+Bn²)` minus their values at f_l.
5. **Integer and fractional delay.** `rest = Dtot − D_ap(c,ω0) − D_lp(a,ω0) − D_fir(w,ω0)`, `Nint = ⌊rest − .5⌋`,
   `Δ = rest − Nint` ∈ [.5, 1.5). Thiran η: start at `(1−Δ)/(1+Δ)`, then 8 Newton steps on `D_ap(η,ω0) = Δ`.
6. **Damping targets.** `gPalm = e^{−6.908/(0.10 f_l)}`, `gTouch = e^{−6.908/(0.03 f_l)}`, `aPalm = max(a, .6)`.
7. **Relatives.** String j is a relative of i when `|n f_i − m f_j| < .005 n f_i` for some n, m ≤ 6 (octaves, fifths,
   fourths, twelfths, double octaves). One 22-bit mask per string; table in test-vectors.json `relatives`.

Result (in-plane loops; out-of-plane values are in `out/test-vectors.json` → `loops[22..43]`; also `out/table-design.md`):

| i | note | mat | L m | T N | d mm | Z kg/s | B | T60 in / out s | N in | eta | disp c | a | fw | g | voicing dB |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | C3 | w | 1.1369 | 135.0 | 0.73 | 0.454 | 3.0e-7 | 8.41 / 11.95 | 364 | -0.14436 | 0.2684 | 0.33099 | 0.5000 | 0.9941929 | -1.99 |
| 1 | D3 | w | 1.1241 | 132.4 | 0.65 | 0.401 | 2.0e-7 | 7.30 / 10.28 | 324 | -0.11883 | 0.2686 | 0.33674 | 0.5000 | 0.9945864 | -1.97 |
| 2 | E3 | g | 1.11 | 129.8 | 0.97 | 0.355 | 1.4e-5 | 6.75 / 8.85 | 285 | -0.17411 | -0.5852 | 0.33856 | 0.5000 | 0.9943505 | -0.30 |
| 3 | F3 | g | 1.0926 | 127.1 | 0.92 | 0.333 | 1.2e-5 | 5.87 / 7.95 | 269 | -0.19623 | -0.5423 | 0.35023 | 0.5000 | 0.9938086 | -0.36 |
| 4 | G3 | g | 1.0703 | 124.5 | 0.83 | 0.297 | 8.2e-6 | 5.36 / 7.51 | 240 | -0.11321 | -0.4522 | 0.32432 | 0.5000 | 0.9940649 | -0.13 |
| 5 | A3 | g | 1.0417 | 121.9 | 0.75 | 0.266 | 5.9e-6 | 4.91 / 7.07 | 214 | -0.07645 | -0.3559 | 0.29825 | 0.5000 | 0.9942181 | 0.02 |
| 6 | B3 | g | 1.0072 | 119.3 | 0.69 | 0.240 | 4.5e-6 | 4.57 / 6.20 | 191 | 0.15269 | -0.2787 | 0.27178 | 0.5000 | 0.9944654 | -0.09 |
| 7 | C4 | g | 0.9678 | 116.7 | 0.67 | 0.230 | 4.4e-6 | 4.32 / 6.38 | 180 | 0.01106 | -0.2431 | 0.25837 | 0.5000 | 0.9944943 | 0.00 |
| 8 | D4 | g | 0.9246 | 114.0 | 0.62 | 0.210 | 3.6e-6 | 4.10 / 5.74 | 160 | -0.09474 | -0.1855 | 0.23123 | 0.5000 | 0.9952127 | -0.65 |
| 9 | E4 | g | 0.8785 | 111.4 | 0.57 | 0.192 | 3.0e-6 | 3.85 / 5.34 | 143 | 0.27706 | -0.1311 | 0.20366 | 0.5000 | 0.9952372 | -0.17 |
| 10 | F4 | g | 0.8303 | 108.8 | 0.56 | 0.188 | 3.2e-6 | 3.57 / 5.17 | 135 | -0.06618 | 0.2714 | 0.18972 | 0.5000 | 0.9951589 | -0.30 |
| 11 | G4 | g | 0.7808 | 106.2 | 0.53 | 0.173 | 2.9e-6 | 3.28 / 4.89 | 120 | 0.30497 | -0.1016 | 0.16153 | 0.5000 | 0.9953791 | 0.02 |
| 12 | A4 | g | 0.7298 | 103.6 | 0.50 | 0.161 | 2.7e-6 | 3.18 / 4.41 | 106 | -0.13308 | -0.0613 | 0.13293 | 0.5000 | 0.9958969 | -0.14 |
| 13 | B4 | g | 0.6758 | 101.0 | 0.47 | 0.151 | 2.6e-6 | 2.89 / 4.18 | 95 | 0.00002 | 0.2728 | 0.10392 | 0.5000 | 0.9960784 | -0.39 |
| 14 | C5 | g | 0.6197 | 98.3 | 0.48 | 0.152 | 3.4e-6 | 2.71 / 3.99 | 89 | -0.09640 | 0.0412 | 0.08925 | 0.5000 | 0.9961383 | -0.67 |
| 15 | D5 | g | 0.5619 | 95.7 | 0.46 | 0.145 | 3.7e-6 | 2.54 / 3.47 | 80 | 0.06323 | 0.5603 | 0.05957 | 0.5000 | 0.9964221 | -1.01 |
| 16 | E5 | g | 0.5033 | 93.1 | 0.46 | 0.140 | 4.4e-6 | 2.34 / 3.25 | 70 | -0.08219 | -0.0478 | 0.02939 | 0.5000 | 0.9966744 | -1.51 |
| 17 | F5 | g | 0.4441 | 90.5 | 0.48 | 0.146 | 7.2e-6 | 2.30 / 3.17 | 66 | -0.08383 | -0.0131 | 0.01409 | 0.5000 | 0.9969118 | -2.47 |
| 18 | G5 | g | 0.3865 | 87.9 | 0.48 | 0.145 | 1.0e-5 | 2.17 / 3.07 | 59 | 0.14333 | -0.0893 | 0.00000 | 0.2811 | 0.9971073 | -3.23 |
| 19 | A5 | n | 0.3303 | 85.2 | 0.53 | 0.147 | 2.1e-5 | 2.20 / 2.86 | 53 | 0.11798 | 0.2724 | 0.00000 | 0.1852 | 0.9975388 | -4.48 |
| 20 | B5 | n | 0.273 | 82.6 | 0.56 | 0.153 | 4.0e-5 | 2.00 / 2.76 | 46 | -0.06796 | -0.1411 | 0.00000 | 0.1229 | 0.9975135 | -6.02 |
| 21 | C6 | n | 0.215 | 80.0 | 0.66 | 0.178 | 1.3e-4 | 2.02 / 2.80 | 43 | -0.19414 | -0.1218 | 0.00000 | 0.1152 | 0.9979942 | -7.72 |

`kap_i = Z_i / Z_C4` (1.970 … 0.772). The irregular dispersion coefficients are fits to tiny targets; their effect is
measured: partials 1–3 within ±0.41 cents of `n f0 √(1+Bn²)` on every string except C6 (p3 +1.0 cents) and partial 4 within
±0.45 cents except B5/C6 (+1.0/+2.0 cents, on partials 10–15 dB down that decay in 0.4 s) (section 10.1). No loop hits the g cap any more (v1: the
out-of-plane B5/C6).

## 5. The finger: play point, velocity, excitation, polarisation, catch, merges, damping

### 5.1 Velocity curve (replaces `velocityOf` in HarpModule)
`vel = HarpVelocity(speed) = clamp(ln(speed / 0.10) / ln 22, 0, 1)`, with speed the fingertip speed across the string in m/s.
For example: 0.2 m/s (PLUCK_SPEED) → .224; 0.5 → .521; 0.75 → .652; 1.0 → .745; 2.2 → 1.
- The lift-off branch `velocityOf(rec.speed) * d / CATCH_MAX` becomes `HarpVelocity(rec.speed * d / CATCH_MAX)`.
- Amplitude: `A = 10^((−18 (1 − vel) + voicing_i ± .3 dB)/20) · levN · (.35·.65)/(β'(1 − β'))`, with β' = min(β, 1 − β).
- Brightness: `fc = 800 · (7000/800)^vel · (1 + .4 t) · fcVar_i · (1 ± 3 %)`.
- Measured on C4 at u .40, sweeps (out/verify.json `ladder`): loudness range 17.6 dB (old 11.6 dB, flat above 1.3 m/s);
  brightness rises monotonically (old flat above 1.3 m/s):

| speed m/s | 0.15 | 0.25 | 0.4 | 0.6 | 0.9 | 1.3 | 1.8 | 2.4 |
|---|---|---|---|---|---|---|---|---|
| vel new / old | .131/.2 | .296/.2 | .448/.308 | .580/.475 | .711/.725 | .830/1 | .935/1 | 1/1 |
| max momentary LUFS new / old | −36.2/−31.7 | −32.6/−31.7 | −29.4/−28.6 | −26.8/−25.3 | −24.2/−22.3 | −21.9/−20.1 | −19.9/−20.1 | −18.7/−20.1 |
| attack centroid Hz new / old | 1441/1626 | 1551/1626 | 1693/1871 | 1818/2151 | 1919/2432 | 1989/2636 | 2040/2636 | 2069/2636 |

The old floors vel at .2, so below 0.27 m/s it stays at one level; the new keeps getting softer (−2.4 dB under the old for a
0.2 m/s poke, −0.3 dB at 0.3 m/s). C4 sits 1.5 dB under the old at mid speeds because the old's random voicing happens to put
its C4 1.5 dB over its own mean.

### 5.2 Play point (lessons)
`PlayPoint(i) = lerp(a, b, clamp((PlayY − a.y)/(b.y − a.y), .30, .40))` (PlayY .95; v1 and today: clamp .30–.70). It moves
the glow (and the lesson capsule's landing) **toward the soundboard** on the strings whose natural point is above u = .40:
C3 by ~28 cm (y ≈ .67 m), G3 ~17 cm, C4 ~10 cm (y ≈ .85 m), E4 ~6 cm, A4 ~1 cm; B4 and up are unchanged. The lesson notes
are G3…C6 (G3 7 times, A3 twice; C4–E4 97 of 198). Physical reason: a pluck at the string's middle cancels the even
partials (comb |sin nπβ|); harpists pluck nearer a third of the string (Le Carrou et al., JASA 127(5), 2010; Waltham). Free
play is untouched: the engine uses the real crossing point `u` as before.

### 5.3 Excitation e_i(n) (phase 0; t = samples since the release, N = sr/f0, M = β'N)
```
p = S(t) − S(t−M) − (M/N)(S(t) − S(t−N))                    zero-mean comb: |H(n f0)| ∝ sin(nπβ)/n (Fletcher & Rossing ch. 2)
e = A·p + Af · min(1, t/(0.0003 sr)) · HP1800²(noise(t))  (t < N)   Af = A·0.15·√(fc/3000)·β'(1−β')/(.35·.65)
    + dcV  while dcN > 0                                      (only after an in-place raise, section 5.6)
S(t) = 0 (t ≤ 0), else 1 − e^{−x}(1 + x + x²/2 + x³/6), x = t/τ, τ = 0.0693·sr/(1.2 fc)
```
The excitation runs until `t > N + 24τ` and is added to both loops: `x_in += win·e`, `x_out += wout·e`. `exAcc += p` each
sample (used by the raise). The noise spans exactly one period with no fade-out, so its circulation is seamless.

### 5.4 Polarisation mix (from the stroke direction)
`cn = ud²/(ud² + uz²)` (ud: across, in the string plane; uz: through it), `win = √(.30 + .70 cn)`, `wout = √(.30 + .70(1−cn))`,
`levN = (1 + √.30·C_OUT)/(win + wout·C_OUT)`: the full bridge-force level difference is made up, so a poke is as loud as a
sweep (measured −0.11 dB mean). A sweep (across) is carried mostly by the in-plane polarisation (pushes on the board normal:
brighter, shorter); a poke (through) mostly by the out-of-plane one (parallel to the board: rounder, a longer tail). With
POL_FLOOR .30 both share enough that their sustain is alike (C4 at 2–3 s: poke −42.5, sweep −43.0, old −45.2 dB re the
attack). PluckOptions gains Ud and Uz (HarpModule `rec.ud/uz`, `e.ud/uz`; otherwise Across → (1, 0), Through → (0, 1)).

### 5.5 Re-pluck and the catch (distributed fade)
A pluck on a ringing string (level > RINGING) that no finger holds is held back 4 ms while the finger "catches" the string:
- every sample, both loops of the string get `sgR = sg`, `sgW = 1/sg`, with `sg = exp(ln(10^(−30/20)) · x²(3 − 2x))`,
  `x = t/192`: the read is scaled by sgR and the write by sgW, which scales **everything stored in the loop** uniformly and
  smoothly (each sample is scaled by sg(read)/sg(write) during its stay). Cost: two multiplies per loop-sample, always on
  (sgR = sgW = 1 otherwise, exact in float).
- at t = 192 the scale is baked into both delay lines (`dl *= sgR`, one pass, ≤ 364 + 363 multiplies), sgR = sgW = 1, and
  the pending pluck starts. The old vibration is at −29.5 dB (true string energy) when the new excitation begins.
- a loss-filter damping cannot do this: in 4 ms only half of C3's loop (7.6 ms) passes the filter.
- This replaces the old `Release(τ 60 ms)` of the previous voice: the harp module must **stop calling Release on re-pluck**.
- A string held by a finger (Touch down) is already damped: its pluck starts at once.

### 5.6 Merges (no double plucks, no truncated excitation)
A Pluck command on string i:
1. **A pluck is already pending** on i (waiting for a running excitation or in the catch): merge; the louder vel and its
   u/ud/uz win.
2. **The string is being excited** (exT ≥ 0, < ~11 ms after the start): an equal or softer pluck merges (dropped). A louder
   one, while exT < N, **raises the running excitation in place**: `exAmp *= 10^(18(vel' − vel)/20)`, `frAmp` likewise, and
   the part already injected at the old amplitude, `(A' − A)·exAcc`, is cancelled by a flat rectangle `dcV = (A' − A)·exAcc/round(N)`
   added for `round(N)` samples (a period-long rectangle in a loop of that period is pure DC: no harmonics); exLen is extended
   to cover it. Later than N (the release tail) the louder pluck waits for the end and then plucks normally.
3. Otherwise it becomes pending: it fires in the prologue of the same sample, or after the catch if the string rings.

So plucks at frames 24000 and 24096 on a ringing C4 give one excitation at 24192, identical to a single re-pluck; two plucks
2 ms apart on a silent string give one excitation. The ring buffer and the 70 ms per-pointer guard can stay as they are.

### 5.7 Damping (palm, finger, hold)
Three flags per string: `touched` (a finger holds it), `dHold > 0` (a palm, 0.35 s per Damp call), `held` (a mute Hold).
Targets: touched → touch (gTouch, a); else palm or hold → palm (gPalm, aPalm); else release. Every command that changes a flag
recomputes the targets, so a finger lifting off (Touch up) under a resting palm falls back to the palm. A Pluck clears touched
and the palm (the finger is now at that string) but not a Hold. Smoothing: `dm += (dT − dm)·(1 − e^{−1/(0.002 sr)})` per
sample (snapped to dT within 1e-6), then `g' = g(1 − dm(1 − gD))`, `a' = a + dm(aD − a)`.
- **Damp(i, hands):** always damps, even a silent string (which then cannot ring in sympathy). The "shh" plays only if the
  string was moving. HarpModule sends Damp when a palm first covers a string and **re-sends it every 0.2 s while the palm
  rests** (PALM_HOLD 0.35 s > 0.2 s: the damping never lapses; a re-send on a silent string makes no sound), never per frame.
- **Touch(i, down/up):** sent when a pointer starts / stops holding a string (`rec.held`).
- **Hold(i, on/off):** mute strings, tests.

### 5.8 Active set, budget hook, sympathy, visuals, sleep
- **Active set.** A string runs while its level > ACT_OFF (1e-9), it is being excited or has a pending pluck, or any of its
  harmonic relatives rings above ACT_REL (1e-6). A pluck activates the string and its relatives at once. Decided at each block
  end (blocks ≥ 64 frames). A frozen string keeps its state (its residual is below −100 dBFS of output), contributes no force,
  and costs nothing; reactivated, it resumes. One C4 pluck runs 9 strings (C3 F3 G3 C4 F4 G4 C5 G5 C6) and, after ~6 s, C4
  alone. Output against all strings running: −40 to −65 dB re the signal on every A/B piece (section 10.6).
- **Budget hook.** `HarpSetMaxActive(n)`: when more strings want to run, the ones being plucked stay and then the loudest;
  the rest freeze. The global adaptive budget (burst.md §5.4) may call it.
- **Sympathy** needs no code: it comes from the shared bridge.
- **Visuals.** Per string the engine publishes `level_i` (block AC mean square of both loops; 0 when frozen). String
  animation `amp_i = VIB_SCALE · √level_i`; calibrate VIB_SCALE so a fresh mf pluck matches today's `VIB_BASE + VIB_VEL·vel`.
- **Sleep.** After TV1 (one mf C4) the harp sleeps at 6.66 s; after a 60 s dense workout, 7.7 s after the last note.
- `HarpRingSeconds(midi)` returns the in-plane T60 from the table (8.41 … 2.02 s).

### 5.9 Calibration (calibrate.mjs, run whenever a timbre constant changes)
1. Pluck every string at 0.75 m/s at its lesson play point (`u = [.4 ×13, .395 .373 .348 .317 .3 .3 .3 .3 .3]`), once as a
   **poke** (ud 0, uz 1) and once as a **sweep** (ud 1, uz 0).
2. Max momentary loudness of each (BS.1770 K-weighting, 400 ms windows, 10 ms hop); mix in power, 70 % poke + 30 % sweep.
3. Level the strings to C4 − 0.1 dB/semitone, over 3 passes; set G_OUT so the 22-string mean equals the old harp's mean at the
   same stroke (−25.71 LUFS dry).

Result: −23.9 … −27.5 LUFS, the intended 3.6 dB tilt (old: −20.5 … −32.6, 12 dB at random); poke − sweep per string
−0.9 … +0.3 dB.

## 6. Unity integration (deltas to ARCHITECTURE §7 and spec 05 §5)

**Audio module.**
- **Processor.** A `HarpProcessor` on the Harp `SynthSource`, the same pattern as `DrumRoomReverb`. `OnAudioFilterRead`
  drains the ring, converts each command's `When` to a frame in the block (the existing sample-exact path), and calls the Burst
  function pointer `HarpDsp.Render(ref HarpState, float* mono, int frames)`. The result is added into the source's mono buffer
  before the spatializer. `ReverbSendDb` stays +3 dB; mixed reality still lowers it 6 dB.
- **Commands.** `SynthCustom` gains **`HarpPluck = 10 {String, Vel, U, Ud, Uz}`, `HarpDamp = 11 {String, Hands}`,
  `HarpTouch = 12 {String, Down}`, `HarpHold = 13 {String, On}`** (3 is `Seed`; 1, 2 are fire/snow). Field mapping on
  `SynthCommand`: VoiceId = string, Level = vel, F0 = u, F1 = ud, Slide = uz, Peak = hands, Q = down/on (1/0).
  **`SynthCustom.Seed` (3)**: HarpProcessor.OnCommand re-seeds `Rng = seed` and `NRng = seed · 2654435761 (uint, wraps)`
  and does nothing else (no wake, no sound). EditMode test: `SeedAll` followed by 1 s of render gives an all-zero harp buffer
  and `Awake == false` (TV8). The 512-entry SPSC ring is enough (a 22-string glissando is 22 commands); the engine's own
  pending queue is 64 (overflow counted as DroppedCommands).
- **IAudioEngine.**
  - `Pluck(int stringIndex, float vel, double when, PluckOptions?)`: PluckOptions gets `Ud, Uz`; vel is the new curve. Returns
    `VoiceHandle(src, id = generation << 5 | string)`.
  - `Damp(h, hands, when)` → HarpDamp. `Release(h, when)` → HarpTouch(down), then (up) 30 ms later (only `ReleaseAll` uses it).
  - New: `void HarpTouch(int stringIndex, bool down, double when = 0)`, `void HarpHold(int stringIndex, bool on, double when = 0)`,
    `float HarpStringLevel(int i)` (a volatile float[22] written once per block), `void HarpSetMaxActive(int n)` (main thread →
    a volatile int read at block start), `float HarpBrightDb { set; }` (recomputes the shelf on the main thread and hands the
    3 floats over through a double-buffered struct swap at block start).
  - `HarpRingSeconds(midi)` → the table.
- **SampleBank and setting.** Remove the 22 `HarpSlot0` render jobs and their 12.2 MiB when `audio.harpSound = "new"`. Keep
  `"old"` selectable (implemented and tested; the A/B fallback). **Ship with "old" as the default until the user picks**, then
  default "new".
- **Desktop.** A click plucks with vel 0.711 (0.9 m/s), mode Through (0, 1).

**Harp module (spec 05 §5).** Changes, all small:
1. `velocityOf` → `HarpVelocity` (5.1).
2. `HarpGeometry.PlayUMax` .7 → **.40** (5.2); the comment in `Core/Contracts/Instruments.cs` (`clamp(..., .3, .7)`) and the
   PlayMode test `HarpInstrumentTests` that expects `PlayPoint(0).y == 0.95` (now ≈ .665 m: compute it from HarpGeometry).
   Lessons, the capsule landing and the glow follow automatically.
3. In `Stroke`, when a pointer starts holding a string (`rec.held = e.s`): `audio.HarpTouch(i, true, when)`; in `LetGo(sound:
   false)`: `HarpTouch(i, false)`. LetGo(sound: true) just plucks.
4. In `Pluck`, drop the `Release` of the old voice; pass `Ud, Uz` from the stroke.
5. Palm damping: call `Damp` for **every** string in the span (not only those with a voice), when the palm first covers it
   and every 0.2 s while it rests (5.7).
6. Visuals: `amp` from `HarpStringLevel` (5.8). Keep `flash`, `bend` and the per-frame phase blur.

Recorder events (`harp{i, vel, u, speed, mode}`) gain optional `ud, uz`; playback falls back to the mode.

## 7. Data layout and the Burst kernel

One `HarpState` struct with fixed-size SoA buffers; loops padded 44 → 48 (12 × float4), strings 22 → 24; allocated once
(`Allocator.Persistent`) in `AudioEngine.Init`.

```csharp
[StructLayout(LayoutKind.Sequential)]
public unsafe struct HarpState {
    public const int NS = 22, NL = 44, NLP = 48, NSP = 24, NM = 8, NAP = 5, QCAP = 64;
    public float* Delay;                 // 6659 floats (26 KiB), loop j at Base[j], ring length N[j]
    public fixed int Base[NLP], N[NLP], Pos[NLP];
    // per loop: coefficients (design table) and state; SgR/SgW = catch scale (1 normally)
    public fixed float Eta[NLP], Disp[NLP], A[NLP], G[NLP], Fw0[NLP], Fw1[NLP], GPalm[NLP], GTouch[NLP], APalm[NLP], CPol[NLP], DG[NLP], DA[NLP];
    public fixed float ThX[NLP], ThY[NLP], DsX[NLP], DsY[NLP], Lp[NLP], X[NLP], Msq[NLP], Msum[NLP], SgR[NLP], SgW[NLP];
    // per string
    public fixed float Kap[NSP], Dm[NSP], DT[NSP], Level[NSP], F[NSP], Vb[NSP];
    public fixed int DHold[NSP], PreT[NSP], DcN[NSP], Rel[NSP]; public fixed byte Touched[NSP], Held[NSP], Pend[NSP], Act[NSP];
    public fixed float ExT[NSP], ExM[NSP], ExN[NSP], ExTau[NSP], ExAmp[NSP], ExVel[NSP], ExWin[NSP], ExWout[NSP], ExLen[NSP], ExE[NSP], ExAcc[NSP], DcV[NSP];
    public fixed float FrAmp[NSP], FrX[NSP], FrY[NSP], FrX2[NSP], FrY2[NSP];
    public fixed float PendVel[NSP], PendU[NSP], PendUd[NSP], PendUz[NSP];
    public fixed int AList[NSP], LList[NLP]; public int AN, LN, MaxActive; public byte Dirty;
    // body: 8 band-passes [b0, b2, a1, a2] (b1 = 0), state [x1, x2, y1, y2], phi[k*24+i], Y*phi, rho
    public fixed float MC[NM * 4], MS[NM * 4], Phi[NM * NSP], YPhi[NM * NSP], Rho[NM], Bk[NM];
    // output: HP2 (5 coef, 4 state), shelf (3, 2), LP4 (2 x 5, 2 x 4), allpasses (NAP x 5, NAP x 4), DC20, bridge HP40,
    // flesh/shh voices; uint Rng, NRng; long Frame; byte Awake; int Quiet, Merged, Dropped
    ...
}
[BurstCompile(FloatPrecision.Standard, FloatMode.Default)]
public static unsafe class HarpDsp { [BurstCompile] public static void Render(ref HarpState s, float* outMono, int frames) { … } }
```
The per-sample body is section 2, verbatim from `renderHarp` (harp-engine.mjs). Kernel rules:
- **Phase 1 and 3** are elementwise over the active loop list `LList[0..LN)` (in-plane loops of the active strings ascending,
  then their out-of-plane loops): process it 4 at a time as float4 lanes with burst.md's WG4 gather/scatter for
  `Delay[Base[j] + Pos[j]]` (lanes past LN masked). Elementwise per loop, so lane grouping does not change results.
- **Phase 2**: `F_i` over `AList`; the 8 dot products `Σ_i phi_ki F_i` and the 24 sums `vb_i = vbb + Σ_k YPhi_ki b_k` as
  float4 over gathered active strings. The reference sums in ascending i; a float4 order differs only by rounding (within
  the section 9 tolerances).
- **Output chain** (HP2, shelf, 2 × LP, 5 × AP): 10 serial recursions per sample. Write it as a skewed cascade (lane k works
  on sample n − k, as burst.md's interleaved BQ4) or run each filter over the whole block from a 256-float Ft buffer; both are
  exact rewrites.
- The excitation and noise run only for strings with `ExT ≥ 0` (a bitmask); `S()` uses `math.exp` (the recursion
  `e^{−(t+1)/τ} = e^{−t/τ}·e^{−1/τ}` is allowed within tolerance). The catch fade's `exp` runs only for strings with `PreT ≥ 0`.
- No managed memory, no branches on denormals (0 subnormal floats after 120 s awake, critic engine-tests).
- RNG: mulberry32 on a uint state. `Rng` (seed) draws 3 per pluck (β, level, fc), then 1 per sample while the flesh or shh
  voice is active. `NRng` (`seed · 2654435761`) draws 1 per sample per string with an active noise layer, ascending strings.

## 8. CPU and memory

### 8.1 Measured in Node (26.7, Apple M5 Pro P-core; `node bench.mjs 20` → out/bench.json; the critic's `critic/bench.mjs` agrees)

| scenario (256-frame blocks) | active strings (mean) | Node µs/block avg / p50 / p99 | active set off |
|---|---|---|---|
| idle (asleep) | 0 | 0.1 / 0.1 / 0.2 | 0 |
| one C4 ringing, re-plucked every 2 s | 9.3 | 125 / 120 / 144 | 210 |
| the harp-jingle lesson as pokes, looped | 16.4 | 194 / 208 / 251 | 210 |
| workout: a random pluck every 4 blocks + damps | 22 | 210 / 210 / 226 | 210 |
| dense: a 22-string glissando every 4 blocks + palms | 22 | 235 / 233 / 280 | 235 |
| dense with `HarpSetMaxActive(8)` | 15 | 171 / 219 / 253 | |

The JS reference is not the shipping code: it pays for `Math.fround`, typed-array bounds checks and list indirection.

### 8.2 Quest 3S estimate at CPU level 3 (1.65 GHz: passthrough is on, burst.md §10.1)
- **Float4 Burst kernel (the target).** Cycle model per sample: 4.5 cycles per active loop (about 20 float4 ops per 4 loops
  on 2 pipes, plus the gather/scatter, from burst.md's WG4 figure of 2.5 cycles per string at 6 V-ops per 4 strings), 4 per
  active string (projections), 10 (modes, 2 BQ4), 25 (output chain, skewed), 30 overhead. **One note 29 µs, lesson 43 µs, all
  22 strings ringing 55 µs per block (1 % of the 5.33 ms block, 2.7 % of the 2 ms target).** Cross-check with burst.md's own
  WG4 Node-to-Quest ratio (6.64 → 1.5 ns, × 0.226) on the Node times above: 28 / 44 / 48–53 µs.
- **Scalar Burst, no SIMD (upper bound).** Node × 2.86 (v1's 2.0 at 2.36 GHz, scaled to 1.65 GHz): 358 / 554 / 602–672 µs
  average, p99 ≤ 806 µs. If the device measures near this, the kernel did not vectorise: check the Burst Inspector.
- Idle: ≈ 0 (asleep), which is most of the time while the piano or drums play.

### 8.3 Row for burst.md §10.3 (for its owner; this key may not edit burst.md)
Replace "22 waveguide strings (6 WG4) | 9" with **"Harp: 44 coupled waveguides, active set, 8 body modes, output chain (harp.md
§8) | 55"** (worst case: all strings ringing; typical 29–43). The total becomes ≈ 932 µs (level 3). The harp's "22 × 5 = 110
groups" in the realistic-scene table no longer applies (it is not modal).

### 8.4 Memory
- Delay lines: 6659 floats = 26.0 KiB; all state ≈ 42 KiB.
- Baked audio: **0** (the old harp needs 12.2 MiB in the SampleBank plus a 0.3 s box IR): **−12.2 MiB** against the ≈ 96 MB
  budget, and 22 fewer boot render jobs.

**Device acceptance:** `HarpProcessor` p99 ≤ 150 µs per block in the dense workout on Quest 3S (development build, level 3,
`SynthSource.MaxBlockMicros`), 0 dropped commands, 0 underruns. Between 150 and 400 µs: ship, but file a vectorisation bug.
Above 400 µs: set `HarpSetMaxActive(10)` by default and file the bug.

## 9. Test vectors (expected values; `out/test-vectors.json` holds the full set)

Conditions: 48 kHz, engine seed 1, 256-frame blocks (TV9 and TV11's depth: 1-frame blocks), commands at absolute frames.
**Tolerances for a float C# implementation:** samples of the first 4800 frames abs ≤ 2e-5; every RMS figure ±0.05 dB; the design
table relative 1e-6 and Nint exact; the sleep frame ±1 block; frame indices and active lists exact. The JS rounds to float32
at every stored value and computes intermediates in double; a C# implementation that computes each expression in double and casts on
store matches to ≤ 1e-6 (a float4 kernel: within the tolerances above).

| vector | commands | expected |
|---|---|---|
| RNG | mulberry32(1) | .627073941, .00273572118, .52744704, .981050967, .968377898; noise seed 2654435761: .546456076, .45955629, .247041697 |
| velocity | HarpVelocity(0.1, .2, .5, .75, 1, 2.2) | 0, .224243824, .520678036, .651852264, .74492186, 1 |
| design | loop 0 / 7 / 21 (in) | Nint 364/180/43; eta −.144360637/.0110592576/−.19413621; disp .26842842/−.243072279/−.121756386; a .330986823/.258373668/0; fw .5/.5/.115164227; g .994192878/.994494313/.997994164; gPalm .589734438/.767941689/.936121158; gTouch .171997301/.414725573/.802491062 |
| design | loop 22 / 29 / 43 (out) | f 131.045332/261.862277/1046.65031; Nint 363/180/43; eta −.0918019387/.122151251/−.200223092; a .293504118/.219304051/0; fw .5/.5/.0961367136; g .99573018/.996234464/.998471168 |
| modes | BP [b0, a1, a2] (b2 = −b0) | 150 Hz: .000613176673, −1.99838836, .998773647; 1030 Hz: .00304566311, −1.97581324, .993908674; rho .48 .42 .40 .35 .30 .26 .22 .18 |
| output | coefficients [b0 b1 b2 a1 a2] | HP250: .976901354 −1.95380271 .976901354 −1.95327952 .954325892; LP7k A: .112881403 .225762807 .112881403 −.702567175 .154092788; B: .150060455 .300120911 .150060455 −.933967395 .534209217; shelf [k 8.43359841, zero .96149116, pole .675231907] (`y = k(x − zero·x1) + pole·y1`); AP900: .863132648 −1.85021826 1 −1.85021826 .863132648 (all five in the JSON); dcHP .99738543; bbHP .994777696; G_OUT .224556 |
| relatives | masks | C3 → {F3 G3 C4 G4 C5 G5}; C4 → {C3 F3 G3 F4 G4 C5 G5 C6}; C6 → {F3 C4 F4 C5 F5 G5} (all 22: `relatives` in the JSON) |
| TV1 | pluck string 7, vel .652, u .5, ud 1, uz 0, frame 0; 96000 frames | out[0..5] = .00015628121, .000462320575, .00066534197, .000840325141, .000297715218, −.00158157595; out[100] −.0103886724; out[1000] −.0445174947; out[4800] .0296984669; out[24000] −.0375472866; RMS 0–4800 −22.266, 0–48000 −28.807, 48000–96000 −47.266 dB; max \|out\| .260094821 |
| TV2 | pluck 0, vel .4, u .65, ud 0, uz 1 (through) at frame 100 | out[100..103] = .0000724263809, .000201844814, .000240046647, .000345007429; RMS 0–4800 −25.964, 0–48000 −31.736, 48000–96000 −43.472 dB |
| TV3 | TV1 + Damp(7, 1) at 24000 | RMS 23520–24000 −32.639; 24480–24960 −41.214; 26400–28800 −66.226; 40000–48000 −66.758 dB (the remainder is the free sympathetic strings) |
| TV4 | TV1 + pluck 7 (vel .8, u .4) at 12000 | catch: the new excitation starts at 12192; out[12191] −.0120225661, out[12192] −.0114061637, out[12300] −.141025662; RMS 12192–14592 −18.109 dB. Held variant (Touch down at 11400, pluck at 12000, no catch): RMS 11400–11500 −27.786, 11900–12000 −47.444, 12000–14400 −18.523 dB |
| TV5 | pluck 0 (C3), vel .652, u .652; AC loop levels after the block ending at 48128, dB re C3 | C4 −32.77, C5 −36.22, C6 −38.05, F3 −40.93, G3 −61.58 |
| TV6 | all 22 strings, vel .8, u .4, frame 0 | RMS 0–48000 −10.452 dB; max \|out\| 2.97620344 (a sample-aligned 22-string attack: never happens from hands) |
| TV7 | TV1, then silence | sleeps at the end of the block ending at frame 319488 (6.656 s) |
| TV8 | Seed 77 at frame 100, nothing else, 24000 frames; then Seed 77 at 0 + pluck 7 (.65, u .4) at 1000 vs a fresh engine seeded 77 | max \|out\| = 0 and asleep; re-seeded output bit-identical to the fresh one; out[1100] .0574774779 |
| TV9 | merges, 1-frame blocks. (a) C4 .65 @0, @24000, @24096; (b) silent C4 .65 @1000 and @1096; (c) .5 @1000, .8 @1096 | (a) excitation starts [0, 24192], merged 1; (b) [1000], merged 1, RMS 1000–6000 −22.277 dB; (c) [1000] (raised in place), merged 1, RMS 1000–6000 −23.465 dB, out[1200] .0611957125 |
| TV10 | pluck 7 .65 @0; Touch(7, down) @23900; Damp(7) @24000; ± Touch(7, up) @24480 | RMS 26000–30000: −57.539 (palm only), −57.637 dB (with the finger lifted) |
| TV11 | pluck 7 .652, u .4 @0 | active strings after block 1 and at 2 s: [0 3 4 7 10 11 14 18 21]; at 6 s: [7]. Re-pluck @12032 (1-frame blocks): true string energy at frame 12223 −29.43 dB re 12032 |
| TV12 | `SetMaxActive(4)`; all 22 strings vel .5, u .4 @0 | active after block 1: all 22 (being plucked); at 0.5 s: [1 2 3 4] |

## 10. Verification (objective; `node verify.mjs` → out/verify.json, `node verify-fixes.mjs` → out/verify-fixes.json)

### 10.1 Every string, the reference stroke (sweep, u = PlayPoint; new / old; dry)

| note | T60 f0 new / old s | early / late T60 (new) s | to −60 dB new / old s | attack centroid new / old Hz | HF>3 kHz at .5–1 s new / old dB | partials 2..6 dB re 1 (new) | tuning p1..p4 cents |
|---|---|---|---|---|---|---|---|
| C3 | 8.93 / 4.47 | 7.11 / 7.77 | 6.46 / 3.65 | 2192 / 2688 | -68.5 / -37.2 | -2.1 -0.4 1.7 -16.5 1.4 | 0 -0.14 0 0.31 |
| D3 | 7.07 / 4.31 | 5.85 / 7.49 | 5.66 / 3.54 | 2168 / 2272 | -78.3 / -40.9 | -4.6 -3.4 -1.4 -18.9 -1.9 | 0.08 0.02 0.13 0.43 |
| E3 | 6.99 / 4.14 | 5.75 / 7.1 | 5.21 / 3.59 | 2084 / 2365 | -86.9 / -47.9 | -0.6 0.3 2 -15 2.5 | 0.08 -0.38 0.04 0.33 |
| F3 | 5.85 / 4.05 | 5.19 / 6.94 | 4.83 / 3.67 | 2032 / 1962 | -98.1 / -60.2 | -2.1 -1.9 1 -16.5 0.9 | 0.14 -0.36 0.03 0.37 |
| G3 | 5.49 / 3.87 | 4.63 / 5.95 | 4.4 / 3.32 | 1954 / 2274 | -94.9 / -62.1 | -5.1 -4.2 -2 -18.8 -2.5 | -0.09 -0.07 0.29 0.45 |
| A3 | 4.3 / 3.69 | 4.49 / 6.41 | 4.5 / 3.31 | 1993 / 2101 | -83.4 / -64.1 | -2.8 -3.8 -0.7 -19.4 -1.1 | 0.17 -0.31 -0.18 0.15 |
| B3 | 4.28 / 3.53 | 4.51 / 3.82 | 3.69 / 3.08 | 1906 / 2480 | -84.4 / -55.4 | -5 -4.6 -2.2 -18.9 -3.2 | 0.18 0.08 -0.09 -0.09 |
| C4 | 3.48 / 3.46 | 3.94 / 4.54 | 3.79 / 3.06 | 1878 / 2306 | -101 / -45.1 | -5.1 -4.3 -2.3 -18.6 -3.7 | 0.2 -0.18 -0.06 0.15 |
| D4 | 2.91 / 3.28 | 3.38 / 4.57 | 3.61 / 2.88 | 1840 / 2271 | -102.7 / -68.9 | -7.6 -7 -5.1 -25.3 -6 | 0.17 -0.36 -0.11 0.05 |
| E4 | 3.3 / 3.06 | 3.6 / 2.8 | 3.05 / 2.61 | 1835 / 2339 | -124.2 / -75.3 | -5.7 -5.6 -3.6 -20 -5 | 0.22 -0.19 -0.16 0.03 |
| F4 | 3.09 / 2.99 | 3.38 / 2.54 | 2.69 / 2.43 | 1863 / 2353 | -100.8 / -62.2 | -6 -5.7 -3.6 -18.6 -6.1 | 0.2 -0.23 -0.16 0.09 |
| G4 | 2.81 / 2.79 | 2.98 / 2.75 | 3.25 / 2.52 | 1783 / 2332 | -104.4 / -60.2 | -6.1 -6.2 -4.7 -24.4 -6.3 | 0.08 -0.22 0.06 0.21 |
| A4 | 3 / 2.46 | 3.04 / 2.46 | 2.39 / 2.1 | 1839 / 1907 | -95.1 / -103.7 | -6.4 -6.2 -4.9 -23.7 -9.6 | 0.1 -0.08 -0.11 0.02 |
| B4 | 2.61 / 2.25 | 2.68 / 2.44 | 2.24 / 2.11 | 1886 / 2033 | -97.2 / -134.2 | -6.3 -7.4 -4.8 -29.2 -8.8 | 0.07 -0.22 -0.01 0.13 |
| C5 | 2.34 / 2.16 | 2.49 / 2.43 | 2.35 / 1.72 | 1786 / 2613 | -99.7 / -107 | -5 -10.2 -5.1 -18.9 -14 | 0.14 -0.21 -0.11 0.03 |
| D5 | 2.4 / 1.92 | 2.51 / 2.15 | 2.17 / 1.85 | 1901 / 1709 | -118.6 / -144.2 | -2.8 -15.5 -5.7 -11.4 -18.6 | 0.1 -0.01 -0.21 -0.24 |
| E5 | 2.23 / 1.65 | 2.3 / 1.94 | 1.92 / 1.55 | 1887 / 1691 | -104.2 / -144.7 | -1.5 -22.9 -7.9 -10.6 -31.3 | 0.14 0.09 0.01 -0.1 |
| F5 | 2.14 / 1.57 | 2.23 / 1.91 | 1.85 / 1.68 | 1900 / 1624 | -109.8 / -145.1 | -0.6 -13.3 -9.2 -12.3 -20.4 | 0.12 0.02 -0.09 -0.06 |
| G5 | 2.05 / 1.31 | 2.13 / 1.64 | 1.79 / 1.27 | 2036 / 1822 | -87.9 / -145.4 | -1.3 -14 -11.8 -10.8 -22.7 | 0.18 -0.01 -0.12 -0.19 |
| A5 | 2.07 / 1.27 | 2.16 / 1.94 | 1.94 / 1.42 | 2112 / 1895 | -85.3 / -138.6 | -1.4 -17.3 -12.4 -12.4 -25.5 | 0.09 -0.22 -0.03 -0.4 |
| B5 | 1.86 / 1.01 | 1.99 / 1.7 | 1.65 / 1.42 | 2362 / 1859 | -84.8 / -141.9 | -1 -15.2 -9.9 -13.1 -24.3 | 0.09 0.1 0.41 0.96 |
| C6 | 1.93 / 0.77 | 2.01 / 1.87 | 1.77 / 1.41 | 2340 / 1717 | -65.3 / -141 | -1.8 -16.7 -11.1 -15.3 -28.4 | 0.05 0.36 1.01 1.97 |

Reading the table:
- **Partial series.** The 5th partial of C4–A4 sits 19–29 dB down: the comb of u = .40 (|sin 2π| = 0), blurred by the ±.03
  humanisation in play. The even partials are all present (v1's hollow C4–E4 is gone).
- **No metallic ringing.** HF above 3 kHz at 0.5–1 s −65 to −124 dB on the new (old −37 at C3); every partial ≥ 4 decays
  faster than the fundamental (max T60 ratio 0.76; old 0.92); 0 inharmonic peaks within 45 dB in the 0.3–1.3 s tail on every
  string.
- **Top octave.** Partial T60s on their targets (C6: 1.93 / 1.07 / 0.60 / 0.37 s against 2.02 / 1.08 / 0.61 / 0.38); v1 had
  0.63 / 0.30 for partials 2–3. A5–C6 still decay their highs 2–5× faster than the fundamental.
- **Attack.** No pitch glide at ff: spread over 0–0.6 s 0.14 / 0.04 / 0.16 cents (C3 / C4 / C6).

### 10.2 Clicks
Designer's scan (6 kHz high-pass, 1 ms frames, impulsive against 15 ms before and 2–8 ms after, above a masking floor 45 dB
under the loudest millisecond of the previous 50 ms; only the first 6 ms of each pluck exempt): **0 clicks in all 9 pieces,
new and old.** Critic's own detector (7 kHz Butterworth, 0.25 ms frames): 0 in every in-room render; the old has 4 HF
bursts at its palm damps. On the dry new output it flags one frame in the damping piece at 8.30 s (the "shh" onset, −50 dB
under the full band, absent in the room render).

### 10.3 Damping
A palm on a ringing C chord (strings 0–8): new −20 dB in 0.04 s, the hand's own strings −40 dB in 0.08 s (coupling off);
overall −40 dB in 0.56 s, because the free strings outside the hand (G4, C5, G5, C6) ring on in sympathy, as on a real harp.
Old: −20 dB in 0.05 s, −40 dB in 0.18 s.

### 10.4 Sympathetic resonance
- AC loop levels after a C3 pluck, dB re C3: at 0.5 s F3 −40, G3 −42, C4 −39, G4 −41, C5 −39, G5 −44, C6 −39; at 2 s C4 −33.
- Radiated, as "others free" minus "others held", dB re the full output: C3 −30 (0.2–1 s) / −29 (1–3 s); C4 −23 / −17;
  G4 −27 / −19. A halo in the tail (1–3 % of its energy), with the slow beats of equal-tempered fifths.

### 10.5 Stability and level
- 60 s of random plucks (any string, u .1–.9, any direction) plus damps: every value finite, peak 2.97; asleep 7.7 s after.
- A 4-string chord at vel 1 on the same frame peaks at 1.79 (old 0.96; v1 1.98). Realistic strums (critic engine-tests,
  4–10 ms per string): +2.1 … +3.2 dB peak over the old at 2.4 m/s, −2.6 dB at 1.2 m/s; the new is louder at ff by design
  (17.6 dB range against 11.6).
- Crest factor (peak − max momentary loudness) against the old: C4 mf −0.7, ff −1.9, C3 ff −0.7, near-board ff −0.2,
  same-frame chord +1.8 dB (without the allpass chain: +5 to +7 dB).

### 10.6 Critique checks (`verify-fixes.mjs`)
Hollow notes, poke parity, Seed, merges, palm/finger, catch depth, top octave: the values in section 0.1. Active set against
all strings running (output difference re the signal / its peak): singles −40.1 dB / −52 dBFS; ladder −52.0; all strings
−48.8; arpeggios −54.8; gliss −52.5; damping −53.3; Greensleeves −65.0; Twinkle −59.2; lesson-jingle −52.4. The budget hook
at an extreme (maxActive 6 on the arpeggios) changes the output by −1.2 dB re the signal: it is an emergency brake, not a
free setting.

### 10.7 A/B renders (`harp-*-old/new.wav`, 48 kHz stereo 24-bit)
The same events through the same room (the app's main reverb IR, seed 4, send .34, return .26, dry centred; no HRTF or
compressor for either). Each pair matched to −18 LUFS integrated (BS.1770-4, re-measured three times because the −70 LUFS
gate can move after scaling) with the true peak ≤ −1.1 dBFS; when a pair needs a lower target, both files share it. 9 pairs,
**59.7 MB**. Critic's meter: 0.00 LU difference in every pair, every true peak ≤ −1.10 dBTP.

| piece (old/new) | s | what to listen for |
|---|---|---|
| `harp-singles` (pair at −18.2 LUFS) | 10.0 | C3, G4, C6 at mf: attack, bass sustain, treble length |
| `harp-ladder-C4` (pair at −19.2 LUFS) | 10.8 | 8 strokes 0.15…2.4 m/s: dynamics and brightness (old flat above 1.3 m/s); each hit re-catches the previous one |
| `harp-allstrings` | 13.4 | all 22 strings at mf, 0.5 s apart: evenness, register colour |
| `harp-arpeggios` | 11.5 | C, Am, F, G arpeggios plus a final rolled C chord |
| `harp-gliss` | 6.8 | glissando up and down: the sympathetic wash |
| `harp-damping` | 9.6 | palm on a chord; two fingers on G4; palm after an upper glissando; a C4 caught by a finger (60 ms) and re-plucked |
| `harp-greensleeves` | 12.6 | traditional (pickup + the second strain, modal, ends on A), long notes poked "through" |
| `harp-twinkle` | 17.6 | the lesson melody an octave up with a left hand |
| `harp-lesson-jingle` | 11.3 | **new:** the app's Jingle Bells lesson (first 16 beats, 112 bpm) as VR pokes at the glowing play point, 0.45–0.65 m/s; the repeated E4s are catches |

Long-term spectrum, new − old (critic's `ltas.mjs`, in room, mean of the 8 pairs it reads): 125 Hz +4.7, 157 Hz +3.3,
3.15 kHz −4.2, 4 kHz −3.5, 12.7 kHz −3.2 dB (v1: +13.7, +11.1, −10.4, −10.6, +8.3). Dry, all 9 pieces (`tune-eq.mjs`):
100–160 Hz +4.0, 200–315 +2.8, 400–630 +2.1, 0.8–1.25k +2.6, 1.6–2k −0.9, 2.5–5k −2.7, 6.3–8k +2.1, 10–16k −3.8 dB. The worst
single band of any piece at 2–5 kHz is −7.6 dB (the ladder: one C4, so the 1/3-octave bands fall between sparse partials);
every other piece is within −5.6 dB there.

Sustain against the old (single notes, dB at 2–3 s re the first 100 ms; poke / sweep / old): C3 −26.0 / −25.1 / −39.8;
C4 −42.5 / −43.0 / −45.2; E4 −47.1 / −45.7 / −51.6; G4 −46.4 / −51.0 / −53.1; C5 −53.8 / −58.0 / −69.9. The lesson's last note
falls 40 dB in 1.95 s (old 1.70 s; v1 2.80 s).

## 11. How to verify on the device

1. **EditMode** (Test Framework): `HarpDesignTests` (section 4 table, loops, modes, output, relatives in test-vectors.json),
   `HarpDspTests` (TV1–TV12 at the section 9 tolerances), `HarpVelocityTests`, `HarpSeedTests` (SeedAll → silent, asleep).
   Keep the existing old-harp bit-exact tests for the "old" setting.
2. **PlayMode, no allocation:** `Is.Not.AllocatingGCMemory()` around `HarpModule.Tick` and the `IAudioEngine` harp calls.
   Update the PlayPoint test (section 6, item 2).
3. **Device performance.** Development build, setting "new", CPU level 3. Run the `bench.mjs` scenarios (one note, lesson,
   dense) through `AudioEngine` for 60 s each; log `[Audio] harp avg/p99/max µs, awake, active strings, merged, dropped`.
   Acceptance in section 8.4.
4. **Capture.** The dev build's `AudioTestTap` writes the Harp source's dry mono to `persistentDataPath/harp-capture.wav` for
   a scripted C3 / G4 / C6 sequence at the reference stroke; `adb pull` it and run
   the lab's `node analyze-wav.mjs harp-capture.wav 48 0.2` (and 67, 84 with their onsets). Expect the section 10.1 rows
   within ±0.5 dB per partial, ±10 % T60, ±1 cent, and 0 clicks.
5. **Ear test.** A dev-menu toggle `audio.harpSound old | new` that switches live (ringing strings finish in their path) and
   the `BRIGHT_DB` slider (−6…+6 dB, default 0). Replay one recorded take with each sound. Loudness parity is designed in
   (section 5.9). The user picks; until then the default stays "old". The harp listening guide says
   what to listen for.

## 12. Risks and open points

1. **Not heard yet.** Every number is objective. The knobs to tune by ear on the headset: `BRIGHT_DB` (tone), GBB/Y
   (sympathy), OUT_T60 and POL_FLOOR (sustain), VEL_DB (dynamics), PRE_DB (how completely a re-pluck stops the old note).
2. **CPU** is a model, not a measurement (section 8.2): the float4 figure assumes Burst vectorises the loop list; the scalar
   bound is 12× higher. Measure first (11.3).
3. **Longer sustain than the old.** Pokes and sweeps now ring alike, but both ring longer than the old in the mid and upper
   range (C5 at 2–3 s: −54 to −58 dB against −70; the old's treble buffers are simply cut short). Lessons wash slightly more:
   the residual under each new note is −13.9 dB against the old's −16.1. OUT_T60 is the knob.
4. **Lower glow** on the bass and middle strings (section 5.2): C4 ~10 cm, G3 ~17 cm lower. Check reach and the lesson capsule
   path on the headset.
5. **Palm damping leaves a halo**: −40 dB in 0.56 s overall (old 0.18 s) because the free strings ring in sympathy. Physically
   right; if it reads as "the palm doesn't work", widen the HarpModule palm span or lower GBB.
6. **Soft end of the dynamics** is 2.4 dB under the old at 0.2 m/s (the old floors vel at .2). VEL_DB is the knob.
7. **Strong top-string trims** (−4.5 … −7.7 dB for A5–C6) compensate the radiation shelf for plucks; their sympathetic ringing
   is not trimmed (it comes through the bridge), so the top strings may shimmer a little more in sympathy than they sound when
   plucked. Measured halo totals are in 10.4.
8. **Simultaneous ff chords** peak about 5 dB above the old (coherent attacks on the same frame; real strums +2 to +3 dB).
9. **The physical parameters** (tension, E, densities) are plausible values, not measurements of a particular harp; only Z
   and B depend on them. Body-mode frequencies are literature values scaled to the app's box; Q and the radiation chain are
   design choices fitted to the old harp's balance.
10. **Sample rate.** Everything is computed from `AudioSettings.outputSampleRate` by the design function, but VOICING and G_OUT
    were calibrated at 48 kHz. Re-run `calibrate.mjs` with `designHarp(sr)` if the device runs at 44.1 kHz.
11. **The critique text this revision answers was truncated after its ninth issue** (the top octave). Any later (low-severity)
    items were not seen.

## 13. Files

- **Engine and design:** `Tools/SoundLab/harp/harp-engine.mjs` (v2, the reference to transliterate). Vendored with it:
  `calibrate.mjs`, `scores.mjs`, `old-harp.mjs`, `lib/dsp.mjs`, `out/test-vectors.json`, `out/calibration.json`.
  - `calibrate.mjs`: play points, voicing and G_OUT (poke-weighted); rewrites the constants; writes `out/calibration.json`.
  - `scores.mjs`: demo content (now with `lesson-jingle`, read from `Tools/SoundLab/data/lessons.json`).
  - `render.mjs`: A/B renders (3-pass loudness matching). `old-harp.mjs`: the current harp (141.3 / 141.5 / 141.6 dB SNR
    against `Tests/EditMode/Audio/Reference~/harp_old_{C3,G4,C6}_v070.wav`). `room.mjs`: the shared room.
- **Checks:** `verify.mjs` (section 10 → out/verify.json, verify.txt), `verify-fixes.mjs` (section 0.1 / 10.6 →
  out/verify-fixes.json), `test-vectors.mjs` (section 9 → out/test-vectors.json), `bench.mjs` (section 8 → out/bench.json),
  `tune-eq.mjs` (LTAS against the old), `analyze-wav.mjs` (device captures); `dev/` (peaks/crest, partials, tails, clarity,
  tables, LTAS detail, critic comparison → out/critic-compare.json).
- **Critique:** `critic/` (the critic's scripts, unchanged; `critic/out/` now holds their v2 results, `critic/out-v2/` a copy);
  `v1/` (v1 engine, scripts, outputs and spec, runnable; `v1/critic/out/` = the critic's scripts re-run on v1, which
  reproduces the critique's numbers).
- **Library:** `lib/wav.mjs`, `lib/dsp.mjs` (FFT, convolution, BS.1770-4, true peak, biquads), `lib/analysis.mjs`.
- **Renders:** `harp-{singles,ladder-C4,allstrings,arpeggios,gliss,damping,greensleeves,twinkle,lesson-jingle}-{old,new}.wav` and a listening guide; the `-new` files are in `Tests/EditMode/Audio/Harp/Reference~/renders/`.

References (numbers only were taken; no recordings, impulse responses or samples were used):
- Chadefaux, Le Carrou, Fabre, Daudet, "Experimentally based description of harp plucking", JASA 131(1) 2012; Chadefaux,
  Le Carrou & Fabre, JASA 133(4) 2013 (the sticking phase of a pluck, ~300 ms).
- Le Carrou et al., JASA 127(5) 2010 (pluck point near a third of the string on the bass); A0/T1 on a concert harp
  (112/146 Hz, 0.029 m³); "Sympathetic string modes in the concert harp", Acta Acustica 95 (2009).
- UNSW Music Acoustics, "Harp acoustics": soundboard modes 202/427 Hz measured, 537–807 Hz modelled, two-slope decays.
- Waltham & Kotlicki (ASA 157th meeting, 1pMU1): soundbox resonances; Waltham (ASA 149th lay paper): plucking near the middle
  reduces even harmonics.
- Bell & Firth, Acustica 60 (1986): gut strings. Fletcher, JASA 36 (1964): stiff strings. Weinreich, JASA 62 (1977).
- Jaffe & Smith, CMJ 7(2) 1983; Välimäki et al., JAES 44(5) 1996; Karjalainen, Välimäki & Tolonen, CMJ 22(3) 1998.
- J. O. Smith, *Physical Audio Signal Processing*: bridge coupling, passivity, allpass dispersion.
- ITU-R BS.1770-4. Meta, "CPU and GPU levels" (Quest 3/3S level table, via burst.md §10.1).
