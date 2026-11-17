# 10-upgrade · Piano: physically-informed modal grand piano (implementation spec for Unity C# / Burst), v2

> **2026-11-11: not in the app.** The modal piano unit built from this spec (`Audio/Sound/Piano`) and its lab copy (`Tools/SoundLab/piano/`) were deleted: the piano plays the classic sound only (the user's choice). This spec is the design record. Backup: `music-room-vr-wip/unity-backups/pre-classic-cleanup/`.

Key: `piano`. Lab: the piano Node lab (Node 26, no packages), vendored in part at `Tools/SoundLab/piano/`.
Renders: `piano-*-old.wav` / `-new.wav` with a listening guide; the `-new` side is in `Tests/EditMode/Audio/Piano/Reference~/renders/`.
Reference engine (transliterate this): `Tools/SoundLab/piano/piano-engine.mjs` (v2). The module wiring it needs is
reproduced in `Tools/SoundLab/piano/app-sim.mjs`. Every number below comes from those files, `verification.json`,
`bench.json`, `test-vectors.json`, `renders-report.json`, `piano-model-tables.json`, or the scripts in `exp/` and
`critic-rerun/` in the lab folder. The first design is kept as `piano-engine-v1.mjs` for comparison.

This replaces `renderPianoKey` (spec 06 §3.1) and the piano play stage (spec 06 §3.2, `PianoSynth.cs`). **The key feel
stays as it is**: the same contacts, the same `StrikeVelocity` law, the same phrase logic (PHRASE_GAP 0.6 s, MAX_RING 6 s)
and the same `IAudioEngine.Piano(midi, vel)` / `Release(h, thud)` calls. Three things are new: two engine calls,
`PianoPedal(depth)` and `PianoKeyUp(midi)`, and three small changes in `PianoAction` (§3.2). Together they turn the
automatic sustain into a **half pedal with step damping**, so real-length sustain does not wash lessons.

---------------------------------------------------------------------------------------------------------------------------

## What changed after the critique (v1 → v2)

| # | critique item (severity) | v2 fix | evidence (lab) |
|---|---|---|---|
| 1 | Automatic-pedal wash in lessons (high) | `PianoAction` sends `PianoPedal(0.85)` (a half pedal) and `PianoKeyUp` at key-up. A released key then decays with damper engagement (1−0.85)² = 0.0225. **Step damp**: a new key damps released, ringing keys within ±2 semitones. Both are on by default. Two new A/B pairs render the app's own automatic sustain. | Previous-pitch wash at 150 ms, dB, old / v1 / **v2**: Für Elise −10.5 / −3.4 / **−10.6**; Ode −18.7 / −5.1 / **−19.2**; C scale −21.3 / −5.9 / **−19.1**; Twinkle −19.7 / −9.0 / **−20.3**; Jingle −20.1 / −10.7 / **−19.7**; Frère Jacques −12.9 / −4.6 / **−13.7**. Mean: old −17.2, **v2 −17.1**. An arpeggio let go still rings +2.5 dB (0.5–1.5 s) / −3.9 dB (1.5–3 s) against the old piano (`exp/wash.mjs`) |
| 2 | Re-strike ordering trap (high) | (a) Events are applied in (frame, enqueue sequence) order, using a stable insertion, never an unstable sort on the frame alone. (b) An engine guard drops a Damp of key k at the same frame as a NoteOn of k, in either order. (c) `PressKey` no longer sends the re-strike release for this engine. The §3.2 text is corrected. | Release→Piano and Piano→Release on the same frame are **sample-identical** to the plain re-strike (`test-vectors.json` `restrikeIdentical: true`). The critic's Jingle Bells test: swap − ab = 0.00 dB on every re-strike, LUFS −24.34 for all three orders (`critic-rerun/music.mjs`) |
| 3 | Mode budget not a hard bound (medium) | New clusters are admitted per block. Quiet non-driving voices are stolen first, then the new notes are scaled to fit MODE_BUDGET (1600). Each new note still gets ≥ N_MIN = 6 modes (low partials first). A hard **MAX_RENDER_MODES = 2000** caps all live modes, including fading ones. | Max modes rendered per block: palms 2000, forearm 2000, all 88 keys 1670, ff two-hand chords 1720 (v1: 2240 / 3258 / 5393 / 1659). Node max block 0.95 / 0.83 / 1.05 / 0.89 ms (§7) |
| 4 | Tonal "woodblock" knock (medium) | The 24 fixed body modes are replaced by **80**: 55 at the soundboard modal density (one per 19.5 Hz, 40 Hz–1.1 kHz, ±30 % jitter, η 2 %), and 25 overlapping 1/12-octave modes (1.15–4.6 kHz, Q 15–25). They form 8 input groups, and each key has its own seeded group weights. | Peak-to-mean in 1/3-octave bands below 1 kHz: 2–5 dB (v1 4–10). Knock spectrum correlation with the neighbouring key: 0.55 (v1 0.93). Levels kept: C4 knock −24.3 / −27.1 dB re tone at pp / ff (v1 −24.1 / −27.5) (`exp/knock-tonal.mjs`, `check-levels.mjs`) |
| 5 | Dull low bass (medium) | TC_MIN_X 1.3 → **1.05** (binds only on A0–B1), FELT_FMIN 600 → **2000 Hz**. Phantom partials are **capped at −6 dB** under their partial at any velocity. | A0 contact pp/mf/ff 7.17 / 4.63 / 3.35 ms (v1 8.88 / 5.67 / 4.06). A0 mf 60-dB extent 1.52 kHz (1.10), energy above 2 kHz −70 dB (−83), centroid 173 Hz (139). C2 mf extent 2.76 kHz (2.52). Phantoms at ff ≤ −6.0 dB. Evenness unchanged (SD 0.23 LU) |
| 6 | Top two octaves pure, no velocity timbre (medium) | The critic's FELT_X / FELT_EXP change was tested and **rejected**: it leaves C6 unchanged and moves C7–C8 partials 2–3 by at most 2 dB (at pp only), because the felt corner is already far above partials 2–3. The real cause is the f² string loss and a velocity-independent null fill. v2 moves part of the treble f² loss to a frequency-proportional loss (partial 1's decay is unchanged), and makes the null fill velocity-dependent above C5. | C6 centroid pp / mf / ff 1065 / 1099 / 1311 Hz (**+23 %**; v1 +3 %), partial 2 at mf −19.8 dB (target ≤ −15), energy above 5 kHz at ff −30.7 dB (old −18: not brighter than today). C7 partial 2 pp / mf / ff −29 / −16 / −15 dB (v1 −31 / −21 / −20). C8 −41 / −33 / −32 (v1 −61 / −53 / −49) |
| 7 | Sympathetic bank never damped in the app (low) | A pedal re-press within **PEDAL_MIN_UP = 0.12 s** of a lift is deferred, so a phrase lift really lifts. | Halo after the phrase lift: −44.1 → −50.2 dB within 0.5 s, the same as a real 150 ms lift (−49.0) (`critic/symgate.mjs`) |
| 8 | Idle cost with the pedal down (low) | The sympathetic bank skips resonators with negligible state while its input block is silent. Ring shifts copy only the written part. | Silent piano with the pedal down: **3.6 µs** per block in Node (v1 142 µs) |
| 9 | Unfiltered attack noise (low) | The attack noise is low-passed at 7 kHz. The damper-felt noise gets its own RNG stream and a 1.5 kHz low-pass. | Energy above 18 kHz −53 dB (v1 −45). The damper-thud component's share above 7 kHz is −57.8 dB (a measurement artefact of the shared RNG made it look like −3.9) |
| 10 | Spec gaps for bit-exact vectors (low) | §8.1 lists every ordering and rounding rule (event order, RNG streams, ramps, admission, culling order). New vectors cover the half pedal, the pedal deferral, both re-strike orders and the hard bound. | `test-vectors.json` |
| + | found in this round: ff loudness uneven in the treble | v1 limited each key's ff attack peak to −2 dBFS, which cut some keys by up to 5.6 dB more than their neighbours (G5 −5.6 dB, G#5 0 dB). v2 keeps only the 11.5 dB ff-range limit plus an outlier guard at +3 dBFS. | ff loudness SD across 88 keys **0.40 LU** (old 1.36). C4 ff now −19.9 LUFS against old −20.0 |

Rejected or kept as is, with reasons:
- **Re-strike on a ringing string** gives −2.3…+4.2 dB of energy in the first 300 ms against a fresh strike (critic
  REPEAT test). This is linear superposition with the string's phase, as on a real string. It is kept, and is the first
  thing to check by ear (§9, item 5a).
- The critic's line-energy test (share of the knock within ±3 % of the body modes) no longer separates tonal from
  noise-like: with 80 modes the windows cover 73 % of the band, and v2 scores 89–100 %. The band peak-to-mean figure
  and the key-to-key correlation above measure the same property directly.
- C7–C8 centroids rise only 3 % and 0.5 % from pp to ff, against the +12 % target. In the top octave the centroid is
  pinned to f0, because the fundamental carries > 95 % of the energy. Moving it 12 % would need partial 2 above
  −10 dB, which is brighter than the current piano (C7 old −12 dB). The velocity change shows instead in partial 2
  (+14 dB at C7, +9 dB at C8 from pp to ff).

---------------------------------------------------------------------------------------------------------------------------

## 0. Decision: real-time modal synthesis, with offline physics at boot

| option | memory | CPU (Quest, est.) | what it can do | verdict |
|---|---|---|---|---|
| Baked multi-velocity notes (int16, 8 layers, 6 s avg) | ≈ 405 MB (2 layers × 4 s ≈ 68 MB) | ≈ 0.05 ms | fixed timbre per layer, no re-strike physics, no sympathetic or pedal interaction | over the 96 MB budget, which also has to hold harp, drums and voice lines |
| Digital waveguide per string | small | ≈ 30–60 ops/sample/string, plus 8–16 all-pass sections for bass dispersion | everything, but per-partial decay and inharmonicity are hard to tune and test | harder to make deterministic and SIMD |
| **Modal (chosen)**: complex one-pole modes per partial and per unison eigenmode | **≈ 1.75 MB** | **≈ 0.12 ms typical; ≤ 0.53 ms worst case per 256-frame block at ratio 0.5** (§7) | exact f_n and T60 per partial, beating and two-stage decay, continuous velocity timbre, re-strike in place, damper and half pedal, sympathetic strings, duplex | chosen |

The expensive physics runs **once at boot on a worker thread** (≈ 0.42 s in Node). It covers the nonlinear hammer
on an ideal-string waveguide, the eigen-analysis of the unison strings coupled through the bridge, and a synthesized
soundboard. The audio thread then runs only independent damped complex oscillators: 2 multiplies and 2 FMAs per mode
per sample, SIMD across modes. Modes are culled below −114 dBFS, MODE_BUDGET (1600) is the soft bound and
MAX_RENDER_MODES (2000) the hard one.

---------------------------------------------------------------------------------------------------------------------------

## 1. Signal flow

```
PianoAction (main thread) ── Piano / Release / PianoPedal(depth) / PianoKeyUp ──► PianoEngine command ring (SPSC, FIFO)
audio thread, once per DSP block (first piano source callback of the block):
   drain the ring → pending events (frame, seq)          frame = max(blockStart, round(when·sr)) as SynthSource.Drain
   due events: stable order by (frame, seq); re-strike guard; cluster admission (budget)
   per key voice k (88 slots, one voice per key, re-strike in place):
      drive f[t] = blended hammer-force pulse (table rows j, j+1 by log vh)          ≤ 640 samples, then 0
      each mode m: z_m ← p_m · z_m + A_m · f[t]       (complex; p_m = e^{(−σ_m + iω_m)/sr} · e^{−D σ_D,m/sr})
      out_reg(k) += Re z_m                              (reg = lo < 52 ≤ mid < 76 ≤ hi, as today)
      damper engagement D ∈ [0,1] ramps in 32 steps of 32 frames (21 ms): key down 0; released under pedal
      depth d: (1−d)²; damped 1
   shared, after the voices:
      mix = lo + mid + hi
      attack noise: mid += env[t] · LP7k(w[t] − 0.92 w[t−1])          (xorshift32 stream A)
      body bank (80 modes, 8 input groups): knock, keybed thump and damper thud, per-key group weights → mid
      sympathetic bank (336 resonators) driven by mix; gates per key from pedal depth / key down / undamped → mid
three buses ─► SynthSource PianoLo / PianoMid / PianoHi (Meta XR spatializer, reverb send as today) ─► master
```

What each part stands for:

| part | physics | source of the numbers |
|---|---|---|
| inharmonic partials | stiff string: f_n = n f0 √(1 + B n²) | Fletcher, Blackham & Stratton 1962; B ranges from Cheng, Dixon & Mauch 2015 and the 2021 Acta Acustica review |
| stretch tuning | octaves tuned to coinciding partials (2:1 above A4, 4:2 below), from B | derived (aural tuning practice): A0 −6.0 ¢, C8 +18.5 ¢ |
| 1/2/3 strings per key | A0–F#1 one string (10 keys), G1–C3 two (18), C#3–C8 three | Cheng, Dixon & Mauch 2015 |
| unison detune, beating, double decay | eigenmodes of S strings coupled through one bridge admittance: in-phase mode fast (prompt), others slow (aftersound) | Weinreich 1977 and his KTH lecture (prompt ≈ 8 dB/s, aftersound < ¼); spread 0.7–1.6 ¢ (Kirk 1959, from memory, UNVERIFIED) |
| polarisation (monochords) | vertical (strong bridge coupling) + horizontal (weak) modes | Weinreich 1977 |
| hammer | mass on nonlinear felt F = K δ^p against an ideal-string waveguide with agraffe and bridge reflections, per string | Chaigne & Askenfelt 1994 (C4: M_h 2.97 g, p 2.5, K 4.5·10⁹; calibrating K to a 2.1 ms contact gives 4.8·10⁹) |
| bass contact | target ≥ 1.05 × the rigid-felt minimum (the agraffe round trip) | Askenfelt & Jansson 1990 (≈ 4 ms in the bass); v1's 1.3 gave 1.4–2.2 × the measured times (critique) |
| velocity → brightness | shorter, sharper pulse at higher speed, plus a felt-surface low-pass that opens with speed, plus (C5 and up) a null fill that rises 5 dB per doubling of hammer speed | Askenfelt & Jansson 1990 (contact shortens with level); Conklin, KTH lecture ("in the treble, all of the partials are affected" by hammer hardness); the shape is calibrated here |
| strike-position comb | sin(nπβ), β ≈ 1/8 | Chaigne & Askenfelt 1994 |
| string loss | σ = b1 + b2 f² (+ treble: a share χ of b2 f² moved to χ b2 f1 f) plus bridge loss 2 f0 Z G(f) | b2 = 2.4·10⁻⁷ s matches the C2/C4 strings of Chaigne & Askenfelt 1994 / Bensa et al. 2003 (b3 = 6.25·10⁻⁹ s with ω); a much smaller b3 for their C7 string is quoted from memory (UNVERIFIED), hence χ |
| soundboard (strings) | 64 seeded board modes for decay ripple and pitch pull; radiation +6 dB/oct 180 Hz–1.8 kHz, HP 85 Hz, LP 8 kHz; seeded phase per frequency | Giordano 1998 (impedance ~10³ kg/s); Wogram (irregular decays); shape synthesized |
| soundboard (knock body) | modal density 1 per 19.5 Hz and loss factor ≈ 2 % below 1.1 kHz; above that, overlapping modes | Ege & Boutillon 2013 (arXiv 1210.5688), via the critique |
| phantom partials, longitudinal mode | tension modulation: f_j + f_k (≤ −6 dB re the partial at any velocity) and f_L ≈ 17 f0, ∝ v² | Conklin 1996 / 1999; Bank & Sujbert 2005; levels assumed, cap from the critique |
| damper, half pedal | felt contact raises each mode's damping; engagement D scales σ_D = (6.91/T60_damped)·√n | grand damper range; half-pedal law assumed (tuned against the app's lesson wash, §6.7) |
| knock, keybed, damper thud | body modes driven by the hammer force, the key bottoming and the damper | Askenfelt (KTH: pp key bottom ≈ 20 ms after string contact, mf together, f before) |
| sympathetic resonance, duplex | undamped strings respond through the bridge; rear segments at 3·f0 and 4·f0 | standard pedal physics; levels calibrated (pedal halo ≈ −23 dB re direct over 2 s) |

---------------------------------------------------------------------------------------------------------------------------

## 2. Boot-time model (C#, double precision, on a worker; result stored as float32)

Run it once per sample rate in `OfflineRenderQueue` (it replaces the 88 piano key renders). Order matters only for
the seeded random draws, which are listed exactly. `interp(T, m)` is log-linear between anchors (linear where marked),
clamped at the ends.

### 2.1 Register tables (`T` in piano-engine.mjs)

| table | anchors [midi, value] | meaning |
|---|---|---|
| B | [21,1.8e-4] [33,1.25e-4] [38,1.1e-4] [48,1.6e-4] [60,3.6e-4] [72,9.0e-4] [84,2.6e-3] [96,7.5e-3] [108,2.0e-2] | inharmonicity |
| Z | [21,18] [36,8] [48,4] [60,2.06] [72,1.6] [84,1.3] [96,1.1] [108,0.9] | string wave impedance per string, kg/s |
| MH | [21,9.0e-3] [30,7.0e-3] [31,4.9e-3] [36,4.9e-3] [48,3.6e-3] [49,3.4e-3] [60,2.97e-3] [72,1.6e-3] [84,0.95e-3] [96,0.6e-3] [108,0.32e-3] | effective hammer mass **per string**, kg |
| P (linear) | [21,2.3] [36,2.3] [60,2.5] [96,3.0] [108,3.0] | felt exponent |
| TC | [21,4.0e-3] [36,3.2e-3] [48,2.6e-3] [60,2.1e-3] [72,1.5e-3] [84,1.0e-3] [96,0.6e-3] [108,0.3e-3] | contact-time target at 2 m/s, s |
| BETA (linear) | [21,0.122] [72,0.12] [108,0.09] | strike point / speaking length |
| DAMP | [21,0.9] [36,0.55] [48,0.38] [60,0.26] [72,0.18] [88,0.13] | damped T60 of partial 1, s (keys > 88 have no damper) |
| FELTX (linear) | [21,9] [108,9] | felt-surface corner at 2 m/s, × f0 |
| FELTEXP (linear) | [21,1.0] [108,1.0] | its velocity exponent |
| **NULLDB** (linear) | [21,12] [72,12] [84,9] [108,8] | null fill at 2 m/s, dB below the local hull |
| **NULLSLOPE** (linear) | [21,0] [72,0] [84,5] [108,5] | null-fill rise per doubling of hammer speed, dB |
| **LOSSLIN** (linear) | [21,0] [72,0] [84,0.55] [96,0.8] [108,0.9] | treble share χ of the f² loss moved to a loss ∝ f |
| **KNOCKDB** (linear) | [21,−1] [60,1.2] [84,−1.6] [96,−3.1] [108,−4.5] | knock level by register, dB re KNOCK_GAIN × voicing gain |

`S(m) = m ≤ 30 ? 1 : m ≤ 48 ? 2 : 3`.

### 2.2 Stretch tuning

```
c[69] = 0
for m = 70..108: B = B(m−12);  c[m] = c[m−1] + 1200·log2(√(1+4B)/√(1+B)) / 12
for m = 68..21:  c[m] = c[m+1] − 1200·log2(√(1+16·B(m)) / √(1+4·B(m+12))) / 12
f0(m) = 440·2^((m−69)/12) · 2^(c[m]/1200)
```
Expected: c[21] = −6.019489, c[33] = −4.321864, c[48] = −3.080346, c[60] = −1.746535, c[72] = 0.2185924,
c[84] = 1.80558, c[96] = 6.133818, c[108] = 18.52018.

### 2.3 Shared soundboard (`buildBoard(77)`)

```
r = Mulberry32(77); for j = 0..63: f_j = 52·1.06^j·(1 + 0.04(r()−0.5)); Q_j = 20 + 30 r(); a_j = 0.25 + 0.5 r()   // draw order f, Q, a
L(f) = Σ_j a_j / (1 + i·Q_j (f/f_j − f_j/f))
Y(f) = G0/√(1+(f/G_FC)²) · (1 + L(f))                              // bridge admittance, s/kg; G0 = 1.15e-4, G_FC = 1500
R(f) = HP2(f) · LP1(f) · TILT(f) · (1 + (RAD_RIPPLE/3)·L(f))
   HP2 = −x²/(1 − x² + i x/RAD_Q), x = f/RAD_HP (85 Hz, Q 0.6);  LP1 = 1/(1 + i f/RAD_LP) (8000)
   TILT = (1 + i f/RAD_F1)/(1 + i f/RAD_F2)  (180, 1800 Hz)
phase(f) = RAD_PHASE·π·(2·vn(f) − 1),  vn(f): x = f/4, g = ⌊x⌋, t = x−g, vn = (1−t)·M32(9000+g).first + t·M32(9001+g).first
```
**Body bank (v2)**, r2 = Mulberry32(78), 80 modes, mode index i = 0..79, group(i) = i mod 8:
```
low band, j = 0..54 (i = j):   draw order f, eta, mag, sign
   f = 40 + 19.5·(j + 0.6(r2()−0.5));  eta = 0.02·(0.8 + 0.4 r2());  T60 = min(1.0, 2.2/(eta·f))
   amp = 0.25·(0.5 + r2())·|R(f)|·(r2() < 0.5 ? −1 : 1)
high band, j = 0..24 (i = 55 + j):   draw order f, Q, mag, sign
   f = 1150·2^((j + 0.5(r2()−0.5))/12);  Q = 15 + 10 r2();  T60 = 2.2 Q / f;  spacing = f(2^{1/12} − 1)
   amp = 0.25·(0.5 + r2())·|R(f)|·√(Q·0.02·spacing/19.5)·(r2() < 0.5 ? −1 : 1)
pole p = e^{(−6.907755/T60 + i2πf)/sr}, input gain amp
```
The high-band amplitude gives each broad mode the energy of the real modes it stands for (spacing/19.5 of them).

### 2.4 Per key (`buildKey`), m = 21..108, u = (m−21)/87, rng = Mulberry32(4000 + m)

1. f0, B, S, β, Z, mh, p from 2.1/2.2; Zeff = Z.
2. Unison and hammer factors, **draw order exact** (spread = 0.7 + 0.9u):
   - S = 1: detune [0], hammer [1].
   - S = 2: a = spread·(0.8 + 0.4 rng()); detune [−a/2, +a/2]; hammer [1 + 0.05(2rng()−1), 1 + 0.05(2rng()−1)].
   - S = 3: a = spread·(0.8 + 0.4 rng()); detune [−a/2, a·0.3·(2rng()−1), +a/2]; hammer 3 × (1 + 0.05(2rng()−1)).
   - then polDet = 0.25·(2rng()−1) ¢; then, if m ≤ 55, the longitudinal draw (step 6).
3. Losses: b1 = 0.10 + 0.30u; b2law = 2.4e-7·(1 + 0.6(1−u)); χ = LOSSLIN(m); f1 = f0√(1+B);
   **b2 = b2law·(1−χ), bL = χ·b2law·f1** (partial 1 keeps σ = b1 + b2law f1²); f_max = min(0.45 sr, 6000 + 10000u).
4. For n = 1..96 while f_n = n f0 √(1+Bn²) ≤ f_max:
   - σs = b1 + b2 f_n² + bL f_n; Y = Y(f_n); c = −2 f0 Z · Y; λ(¢) = −σs + i·2π f_n 2^(¢/1200).
   - S = 1: M = [[λ(0)+c, 0.10c], [0.10c, λ(polDet)+0.12c]], e = (1, 0.10), o = (1, 0.40).
     S ≥ 2: M = diag(λ(detune_s)) + c·11ᵀ, e = hammer factors, o = 1.
   - Eigen-solve (2×2 closed form; 3×3: Durand–Kerner on det(M − sI − μI), s = mean diagonal,
     scale = max(1e-9, |A00|+|A11|+|A22|+3|A01|), start roots (0.4+0.9i, −0.65+0.31i, 0.13−0.77i)·scale,
     ≤ 200 iterations, stop when every step < 1e-13·scale; v_k = 1/(M_kk − c − μ)). 2×2 eigenvector: (b, μ−a) or (μ−d, b),
     whichever has the larger |.|₁.
   - Per eigenmode: W = (oᵀv)(vᵀe)/(vᵀv) (bilinear); P_n = R(f_n)·e^{i·phase(f_n)}·sin(nπβ)/√(1+Bn²)/S;
     A = P_n·W; σ = −Re μ; f = Im μ/2π; energy = |A|²/(2 max(σ, 1e-3)).
   - Keep the highest-energy modes (stable sort, descending): all for n ≤ 6, 2 for n ≤ 16, then 1.
   - Damper: σ_D = (6.907755/DAMP(m))·√n (0 for m > 88).
   - `partial[n] = {f, σ, σB = S·2 f0 Z·Re Y, σ_D, A}` of the strongest mode.
5. Phantom partials (m ≤ 64): w = min(1, (64−m)/12); for mm = 2..14 (stop if mm > number of partials):
   j = ⌊mm/2⌋, k = mm − j; f = f_j + f_k; σ = σ_j + σ_k; rel = w·0.10·min(1.5, mm/10);
   A = A_mm · 2.2·rel · e^{i(phase(f) − phase(f_mm))}; σ_D = max(σ_j, σ_mm) + σ_D(mm);
   **vcap = min(3, 0.5/(2.2·rel))** (never above −6 dB re partial mm).
6. Longitudinal (m ≤ 55): f_L = 17·f_ET·(1 + 0.01(2rng()−1)); A = A_near·3.0·0.10·min(1, (56−m)/10);
   σ = 6.907755/0.7; σ_D = 6.907755/0.08; vcap = 3.
7. Felt stiffness: t_min = contact time at 2 m/s with K = 1e19; **target = max(TC(m), 1.05·t_min)**; bisection on log₁₀K in
   [5, 17], 40 steps. **Or load `Kfelt` from `piano-model-tables.json`** (the test vectors pin it).
8. Hammer pulse table: 12 rows, v_j = 0.4·20^(j/11) m/s; each row = `hammerPulse(v_j)` (2.5).
9. Excitation correction (2.6), per row and mode.
10. Float32 SoA: p = e^{(−σ + i2πf)/sr} → (pr, pi); A → (ar, ai); dmStep = e^{−σ_D/sr/32}; **dmLog = −σ_D/sr/32**;
    **vcap** (0 for transverse modes); freq; σ; σ_D; pn (partial index; phantoms keep mm, longitudinal 0).
11. **Body weights**: r = Mulberry32(7000 + m); for g = 0..7: a_g = 0.4 + 0.6 r(); s_g = r() < 0.5 ? −1 : 1 (draw order a, s);
    w_g = a_g s_g / √(Σa²/8).

### 2.5 Hammer pulse (`hammerPulse`), per string, at 4 × sr

```
dt = 1/(4 sr); nNear = max(1, round(β/f0/dt)); nFar = max(1, round((1−β)/f0/dt)); maxN = ceil(0.012/dt)
circular lOut, rOut (length max(nNear, nFar)+2); xh = 0, vh = v, ys = 0; a = dt²/mh + dt/(2Z)
for k: lin = k ≥ nNear ? −0.995·lOut[k−nNear] : 0;  rin = k ≥ nFar ? −0.99·rOut[k−nFar] : 0
       d0 = xh + dt·vh − ys − dt·(lin + rin)
       if d0 > 0: solve F = K·(d0 − aF)^p on [0, d0/a] (safeguarded Newton, start min(d0/a, K d0^p), ≤ 60 it, 1e-12 rel)
       else if already in contact: append 0, stop
       vs = lin + rin + F/(2Z); lOut[k] = rin + F/(2Z); rOut[k] = lin + F/(2Z)
       ys += dt·vs; vh −= dt·F/mh; xh += dt·vh; append F
decimate: out[⌊i/4⌋] += F_i/4                                    // force in N, ≤ 640 samples at 48 kHz
```

### 2.6 Excitation correction (`excitationCorrection`)

For row j (speed v_j) and mode frequency f: F(f) = |Σ_k pulse_j[k] e^{−i2πfk/sr}|; hull(f) = max F over a 1/24-octave grid
(20 Hz … 0.45 sr) within [f/1.26, f·1.26];
`nullDb = max(0, NULLDB(m) − NULLSLOPE(m)·log2(v_j/2))`; `fc = clamp(FELTX(m)·f0, 2000, 12000)·(v_j/2)^FELTEXP(m)`;
`corr = min(30, max(F, hull·10^(−nullDb/20)) / F / √(1 + (f/fc)²))`.

### 2.7 Voicing (`voiceKeys`)

1. E(vh) = K-weighted energy of the strings over the first 0.4 s at speed vh: each mode is run through the blended pulse
   (with its corr and velScale), then Σ over partial groups of Σ_a Σ_b ½ Re[z_a z̄_b (1 − (p_a p̄_b)^T)/(1 − p_a p̄_b)] × |K(f)|²,
   where groups are the modes with the same pn (velocity modes alone).
2. gain = √(1/E(2.0)) · 0.1 · 0.183/0.3; every A is multiplied by gain.
3. knock = 0.04·gain·10^(KNOCKDB(m)/20); knockArea = knock·Σ pulse(2.0).
4. ffRangeDb = 10 log10(E(6)/E(2)); ffPeak = max |Σ Re z| over the first 60 ms at 6 m/s (strings only);
   **ffCutDb = min(0, 11.5 − ffRangeDb, 20 log10(1.41/ffPeak))** (v1 used 0.79, which made ff uneven by up to 6.8 dB).
   At note-on `ffGain(vh) = 10^(ffCutDb·x/20)`, x = clamp(ln(vh/2)/ln 3, 0, 1).

### 2.8 Shared banks (`buildBanks`)

- Body: the 80 modes of 2.3 → (pr, pi, g, grp).
- Sympathetic: for each key the first min(4, max(1, ⌊2000/f0⌋)) partials: f, σ, σ_D, gin = 0.15·2σB/sr.
- Duplex (m ≥ 64), rng = Mulberry32(6000+m), **two draws always made**: f = 3.0·f0·2^(7(0.7+0.6 rng())/1200) and
  4.0·f0·2^(−9(0.7+0.6 rng())/1200); skip if f > min(10000, 0.42 sr); σ = 6.907755/1.4; gin = 0.018·2σ/sr; σ_D = 0.
- Pad to a multiple of 16 with silent resonators (key −1, gain 0). Result: **336 resonators** at 48 kHz.

---------------------------------------------------------------------------------------------------------------------------

## 3. Runtime (audio thread; Burst job or Burst-compiled static functions)

### 3.1 Data layout

```csharp
// Boot (read-only on the audio thread), one per key:
struct PianoKeyModel { int M; float Gain, Knock, KnockArea, FfCutDb; fixed float BodyW[8];
  NativeSlice<float> Pr, Pi, Ar, Ai, DmStep, DmLog, VCap, Freq; NativeSlice<short> PartialN;   // modes, M ≤ 126
  NativeSlice<float> Pulses /*12 rows, RowLen[12], ≤ 640 each*/, Corr /*12 × M*/; }
// Runtime, one per key (88), fixed capacity, no allocation:
struct PianoVoice { int N, DriveLen, DrivePos, DampLeft, Gen; byte State /*0 KeyDown, 1 HeldByPedal, 2 Damped*/, Kill;
  bool Active; float DFrom, DTo, Level, Level0;
  fixed float Zr[192], Zi[192], Pr[192], Pi[192], Ar[192], Ai[192], Dms[192]; fixed short Id[192], Slot[192]; fixed float Drive[640]; }
struct PianoShared { NativeArray<float> SymZr, SymZi, SymPr, SymPi, SymG, SymPedalScale /*336*/, BodyZr, BodyZi /*80*/,
  BodyIn /*8 × (256+2048)*/, NoiseEnv /*256+2048*/; int RingHi; float Pedal; long LiftFrame; int PedalGen; bool SymDirty;
  fixed byte KeyDown[88]; uint XorA, XorThud; float NzPrev, NzLp; int L, A; float Admit; }
```
Keep each voice's arrays contiguous and 16-float aligned; pad N up to a multiple of 16 with zero modes (A = 0, p = 0)
in the render loop so the inner loop never has a remainder.

### 3.2 API, Piano module wiring, events

**Transport.** `PianoEngine` owns one SPSC ring of `PianoCmd { byte Kind; byte Midi; bool Thud; float Value; double When; }`
(Kind: On = 0, Damp = 1, Pedal = 2, KeyUp = 3; Value = vh or pedal depth). Every piano call from the main thread
goes into this one ring, in call order, so its FIFO order is the enqueue sequence. Do not route piano commands through
the three register sources' rings: they drain independently, so their relative order is lost. At the start of
`Process` the ring is drained into a fixed pending array as (frame, seq), with frame converted by the
`SynthSource.Drain` rule. *(v1 said "Custom kind 3/4"; kind 3 is `SynthCustom.Seed`. Do not use it.)*

| call (IAudioEngine) | engine event | notes |
|---|---|---|
| `Piano(midi, vel, when)` | On(midi, vh = hammerFromVel(vel)) → `VoiceHandle(PianoSource(midi), id = ++gen[midi])` | a ringing key is **re-struck in place** (modes keep their state, the new pulse drives them) |
| `Release(h, thud, when)` | Damp(midi, thud), only if `h.Id == gen[midi]` | stale handles are ignored; keys > 88 have no damper |
| **new** `PianoPedal(float depth, when)` | Pedal(depth) | 1 = fully down, 0 = up; in between = half pedal |
| **new** `PianoKeyUp(int midi, when)` | KeyUp(midi) | the key is back up under the automatic sustain: the voice now follows the pedal depth |
| `SeedAll(seed)` | XorA = seed (0 → 1); XorThud = (seed ^ 0x9E3779B9) or 1 | attack noise and damper-felt noise only |

**PianoAction changes** (all three are in `app-sim.mjs`, mode 'v2'; constants in `PianoSpec`: `AUTO_PEDAL = 0.85f`,
`STEP_DAMP = 2`):
1. `PressKey`: after the phrase check (`Damp()` on a new phrase), call `_sink.PedalDepth(AUTO_PEDAL)`. Then **step damp**:
   for every key s in `_sustained` (in list order) with `s.HasVoice`, `s.PressedBy.Count == 0`, `s != key` and
   `|s.Midi − key.Midi| ≤ STEP_DAMP`: `_sink.NoteOff(s.Midi, s.Voice, thud: false)`, then `s.HasVoice = false`,
   `s.Voice = default`, and remove s from `_sustained` (order-preserving). Then, **for this engine, skip the re-strike
   release** (`if (key.HasVoice) _sink?.NoteOff(...)` becomes `if (key.HasVoice && !_sink.RestrikesInPlace) ...`).
   `RemoveSustained(key)` and `NoteOn` stay as they are.
2. `Damper(key)`: when `key.HasVoice`, after `AddSustained(key)`, call `_sink.KeyUp(key.Midi)`.
3. `Damp()` (the automatic pedal lift): call `_sink.PedalDepth(0)` first, then the releases as today.
`IPianoSink` gains `PedalDepth(float)`, `KeyUp(int)` and `bool RestrikesInPlace`. MAX_RING releases do not touch the
pedal. `PlaybackReleaseAll` keeps calling `Damp()`.

**Event order and guard** (the critique's high-severity trap). Due events (frame < blockEnd) take `at = max(frame,
blockStart)` and are applied in **(at, seq)** order. In C#, insert them in order into the fixed array; never call
`List.Sort` or `Array.Sort` on the frame alone, because those sorts are unstable. Before applying, every **Damp(k)
that shares `at` with an On(k) is dropped**, in either order: that pair is a re-strike, and the re-strike in place
supersedes it. `StatsDroppedDamps` counts them. EditMode test (§8): Release then Piano, and Piano then Release, on the
same key and frame must give output identical to the plain re-strike.

### 3.3 Block order (n = 256)

```
clear lo, mid, hi
drain the command ring; pick due events; order (at, seq); guard
if any On is due: cluster admission (3.5); render voices [pos, at), apply, ... ; Admit = 1
_shared(): mix = lo+mid+hi; attack noise; body bank; sympathetic bank; shift rings (written part only)
_housekeep(): cull, levels, soft budget, L/A
```

**On(midi, vh)** at offset `off`:
```
key = model[midi−21]; v = voice[midi−21]; KeyDown[k] = 1
if !v.Active: v.N = 0; v.Slot[*] = −1
want = key.M − v.N; roomHard = MAX_RENDER_MODES − L
admit = clamp(min(want, roomHard, max(min(N_MIN, want), ceil(Admit·want))), 0, ·)          // Admit from 3.5
corr[i] = lerpRows(Corr, vh)·ffGain(vh)           // rows by x = ln(vh/0.4)/ln(20)·11, clamped to [0, 11]
for i < key.M (model order): s = v.Slot[i]; if s < 0: if added == admit or v.N == 192 → skip; else new slot, z = 0, added++
   sc = VCap[i] > 0 ? min(vh/2, VCap[i]) : 1;  v.Pr/Pi[s] = key.Pr/Pi[i] (free pole);  v.Ar/Ai[s] = key.Ar/Ai[i]·sc·corr[i]
L += added; A += (v.Kill ? v.N : added)
v.DriveLen = blend(Pulses row j, j+1, w)·ext (ext = vh/0.4 below the table, vh/8 above); v.DrivePos = 0
v.Active = true; State = KeyDown; DFrom = DTo = 0; DampLeft = 0; Kill = 0; Gen++; Level0 = −1
body, for each group q:  BodyIn[q][off+i] += key.Knock·ffGain·BodyW[q]·Drive[i]               // knock: same force pulse
      keybed: delay = max(0, 1 − ln(vh)/ln 2.5)·18 ms; 1.5 ms raised cosine, amplitude 0.00007·vh/2·BodyW[q]
NoiseEnv[off+i] += 0.004·(vh/2)²·(0.4 + 0.6(midi−21)/87)·e^{−i/(3 ms)} for i < 9 ms;  RingHi = max(RingHi, written end)
```
**KeyUp(midi)**: KeyDown[k] = 0; if v.Active and State == KeyDown: State = HeldByPedal; if midi ≤ 88: Retarget(v, E(Pedal)).
**Damp(midi, thud)**: if dropped by the guard → count and return. KeyDown[k] = 0. If !Active or midi > 88: if Active,
State = HeldByPedal; return. If State != Damped: State = Damped; Retarget(v, 1). Body: 4 ms raised cosine of area
`(0.35·√min(1, Level/Level0) + (thud ? 0.15 : 0))·key.KnockArea`, sample by sample × (1 + lp) where
`lp += a(rndThud() − lp)`, a = 1 − e^{−2π·1500/sr} (stream XorThud), into every group × BodyW[q].
**Pedal(depth)** at absolute frame F:
```
if depth > Pedal && Pedal <= 0 && F < LiftFrame + round(0.12·sr):     // a pedal can't go back down instantly
    re-queue this event at LiftFrame + round(0.12·sr), tagged gen = PedalGen; return
if tagged && tag != PedalGen: return                                  // a later lift superseded it
if depth == Pedal: return
if depth <= 0 && Pedal > 0: LiftFrame = F; PedalGen++
Pedal = depth; SymDirty = true; for every active voice with State == HeldByPedal: Retarget(v, E(depth))
```
`E(d) = d ≥ 1 ? 0 : d ≤ 0 ? 1 : (1 − d)²` (engagement; E(0.85) = 0.0225).
**Retarget(v, D)**: if Kill return; dNow = DTo − (DTo − DFrom)·DampLeft/32; dd = D − dNow; if |dd| < 1e-6 {DFrom = DTo = D;
return}; for each slot s: `Dms[s] = dd == 1 ? key.DmStep[Id[s]] : (float)exp(dd·key.DmLog[Id[s]])`; DFrom = dNow; DTo = D;
DampLeft = 32. (A negative dd, the pedal pressed deeper, raises the poles back towards the free ones.)

What the half pedal does: with E = 0.0225, a released C4 gains σ = 0.6 s⁻¹ on partial 1 (T60 ≈ 6 s with its own loss).
A0 gains 0.17 s⁻¹ and E6 1.2 s⁻¹. Higher partials gain more (√n), so released notes darken as they fade.

### 3.4 The hot loop (per active voice, per segment [s, e))

```csharp
// Burst: process 16 modes per iteration as 4 independent float4 chains (latency hiding), samples inner.
for (int i = 0; i < v.N; i += 16) {
  float4 r0..r3 = Zr[i..i+15], m0..m3 = Zi[...], a0..a3 = Pr[...], b0..b3 = Pi[...];
  if (driving) { float4 g0..g3 = Ar[...], h0..h3 = Ai[...];
    for (int t = s; t < e; t++) { float f = (p >= 0 && p < DriveLen) ? Drive[p] : 0; p++;
      float4 nr0 = a0*r0 - b0*m0 + g0*f;  m0 = a0*m0 + b0*r0 + h0*f;  r0 = nr0;   // ×4 chains
      acc[t] += r0 + r1 + r2 + r3; } }           // acc: float4[256]; out[t] += csum(acc[t]) at the end
  else for (int t = s; t < e; t++) { float4 nr0 = a0*r0 - b0*m0; m0 = a0*m0 + b0*r0; r0 = nr0; ...; acc[t] += r0+r1+r2+r3; }
  store r, m back }
```
- Damper and pedal ramp: while `DampLeft > 0`, segments are ≤ 32 frames from the segment start. After each segment:
  `Pr[i] *= Dms[i]; Pi[i] *= Dms[i]; DampLeft--` (if it reaches 0: DFrom = DTo). A re-strike restores the free poles at once.
- `DrivePos += e − s` after every segment.

### 3.5 Cluster admission and housekeeping (the hard bound)

Before the event loop, when the block has any On:
```
R = Σ over distinct struck keys k of (key.M − (voice k active ? N : 0))
while A + R > MODE_BUDGET: steal the quietest voice that is active, not Kill, not driving and not struck this block
                            (ascending k, strictly smaller Level wins); A −= its N; stop if none
Admit = R > 0 ? clamp((MODE_BUDGET − A)/R, 0, 1) : 1
```
Steal = `Dms[*] = e^{−6.907755/0.03/sr/32}`, DampLeft = 32, DFrom = DTo = 1, Kill = 1, State = Damped, `StolenVoices++`.
The voice fades with T60 30 ms and is counted in L until culled.

End of block (`_housekeep`):
- Cull: for each voice, for i in slot order: if not driving and |z|² < (2e-6)² → swap-remove (last slot moves to i;
  update Slot/Id; re-test i). Level = Σ|z|² of the kept modes. The first block after the drive sets Level0. At N = 0 the
  voice becomes inactive (Kill = 0, State = Damped).
- L = Σ N (all active), A = Σ N (active, not Kill). While A > MODE_BUDGET: steal (any non-driving, non-Kill voice).
- Shared banks: zero any state with |z|² < 1e-30 after the block (no subnormals).

Guarantee: notes are only added through `admit ≤ MAX_RENDER_MODES − L`, and housekeeping only removes. So **no block
ever renders more than MAX_RENDER_MODES modes**, whatever the playing (palms, forearm, all 88 keys).

### 3.6 Shared banks and output

- Attack noise (stream XorA): `w = xorshift32()·2⁻³²·2 − 1` (x ^= x<<13; x ^= x>>17; x ^= x<<5, unsigned). Only where
  `env[t] ≠ 0`: `h = w − 0.92·NzPrev; NzPrev = w; NzLp = (float)(NzLp + aLp·(h − NzLp))`, aLp = (float)(1 − e^{−2π·7000/sr});
  `mid[t] += env[t]·NzLp`.
- Body bank: mode i is driven by `BodyIn[i % 8]`. Skip mode i (and zero it) when its |z|² < 1e-24 and its group's ring is
  all zero in [0, n). `z ← p z + g·in[t]`, mid += Re z.
- Sympathetic bank: if SymDirty: `SymPedalScale[i] = (float)exp(−E(Pedal)·σ_D,i/sr)`. Gates per resonator: key < 0 → gain
  0, free pole. Duplex, key > 88, KeyDown[key] or voice[key] active → free pole, gain = (duplex || !voice active) ? gin : 0.
  Otherwise, if E(Pedal) < 1 → gain gin, pole = free·SymPedalScale; else (pedal up) → gain 0, damped pole.
  `z ← p z + gin·mix[t]`, mid += Re z. **Idle skip**: a resonator with |z|² < 1e-20 whose gain is 0, or whose input block
  is silent (max |mix| < 1e-12), is zeroed and skipped. SIMD: 16 resonators per iteration, `mix[t]` broadcast.
- Rings: shift by n only over [0, RingHi), then RingHi = max(0, RingHi − n). In C#, a circular index is fine as long as
  the content is the same.
- Routing: one `PianoEngine` renders all three buses once per DSP block. The three piano `SynthSource`s run on the same
  mixer thread. The first callback of a block (detected by the block's dspTime frame stamp) runs `engine.Process`, and
  each source then adds its own bus. No locks. Reverb sends stay as in ARCHITECTURE §7.2.
- Delete for the piano: the soundboard `ModalResonator` on PianoMid, the hammer knock and damper thud noise voices, and
  the 88 piano SampleBank slots (frees 67.7 MiB).

---------------------------------------------------------------------------------------------------------------------------

## 4. Velocity curve

Unchanged law (`strikeVelocity`, spec 05 §2.3): x = clamp(ln(vy/0.07)/ln(1.5/0.07), 0, 1) → **vh = 0.75·8^x** m/s.
From the legacy `vel`: x = 1 + log10((0.08 + 0.8 vel²)/0.88), then the same (below vel 0.1, x may go to −0.3).

| finger m/s | 0.07 | 0.1 | 0.2 | 0.3 | 0.5 | 0.7 | 1.0 | 1.5 |
|---|---|---|---|---|---|---|---|---|
| hammer m/s | 0.75 | 0.955 | 1.529 | 2.013 | 2.847 | 3.577 | 4.557 | 6.0 |

| legacy vel | 0.1 | 0.2 | 0.38 | 0.5 | 0.62 | 0.75 (desktop) | 0.84 | 1.0 |
|---|---|---|---|---|---|---|---|---|
| hammer m/s | 0.75 | 0.932 | 1.542 | 2.133 | 2.861 | 3.796 | 4.529 | 6.0 |

At C4 the loudness range from vel 0.12 to 1.0 is now 21.5 LU (old 19.5; v1 19.7). The ff end is matched to the old
piano (C4 ff −19.9 against −20.0 LUFS dry, mf −28.7 against −28.7); pp is 1.9 dB softer than the old.
The 2 LU difference comes from removing v1's per-key −2 dBFS peak cut, which had made ff uneven.

---------------------------------------------------------------------------------------------------------------------------

## 5. All runtime constants (`K` in piano-engine.mjs)

| constant | value | what | source / status |
|---|---|---|---|
| BLOCK | 256 | frames per block | ARCHITECTURE §7.1 |
| FINGER_MIN / MAX, VH_MIN / VH_MAX, V_REF | 0.07 / 1.5, 0.75 / 6.0, 2.0 m/s | velocity law | current app; Askenfelt (ff ≈ 5 m/s, hammer ≈ 5 × key) |
| HAM_OS, HAM_TMAX, HAM_NV, HAM_V0/V1 | 4, 12 ms, 12, 0.4/8 m/s | hammer simulation and table | numerical |
| **TC_MIN_X** | **1.05** (v1 1.3) | contact ≥ 1.05 × rigid-felt minimum (binds A0–B1 only) | critique: bass contact 1.4–2.2 × Askenfelt |
| **FELT_FMIN**, FELT_FMAX, CORR_MAX | **2000** (v1 600), 12000 Hz, 30 | felt corner clamp, correction cap | critique (bass) |
| NMAX, FMAX_BASS/TREBLE | 96, 6000/16000 Hz | partial limits (also ≤ 0.45 sr) | cost / audibility |
| DETUNE0/1, HAMMER_UNEVEN | 0.7, 0.9 ¢, 0.05 | unison spread, ±5 % force | Kirk 1959 (UNVERIFIED detail); assumption |
| POL_HH/VH/EXC/OUT/DETUNE | 0.12, 0.10, 0.10, 0.40, 0.25 ¢ | monochord polarisation | Weinreich 1977 (form); values assumed |
| N_FULL, N_TWO | 6, 16 | eigenmodes kept per partial | cost |
| G0, G_FC | 1.15e-4 s/kg, 1500 Hz | board conductance | Giordano 1998 order; calibrated to C4 prompt ≈ 9 dB/s |
| B1 (bass→treble), B2, B2_BASS_X | 0.10→0.40 s⁻¹, 2.4e-7 s, 1.6 | intrinsic loss | Chaigne & Askenfelt 1994 / Bensa et al. 2003 (C4); calibrated |
| RAD_HP/Q/F1/F2/LP/RIPPLE, RAD_PHASE/PH_HZ | 85 Hz/0.6/180/1800/8000/0.5, 1.0·π/4 Hz | radiation and phase | tuned |
| PH_MAXMIDI, PH_MMAX, PH0, **PH_FF_REL** | 64, 14, 0.10, **0.5** | phantom partials, ≤ −6 dB re partial | Conklin 1999; cap from the critique |
| LONG_MAXMIDI/RATIO/REL/T60 | 55, 17, 0.10, 0.7 s | longitudinal mode | Conklin 1996 / Bank & Sujbert 2005 (UNVERIFIED per key) |
| DAMPER_TOP, DAMP_STEPS, DAMP_SUB | 88, 32, 32 | no dampers above E6; ramp 21 ms | typical grand; ramp assumed |
| **PEDAL_EXP, PEDAL_MIN_UP** | **2, 0.12 s** | engagement (1−d)²; shortest pedal lift | assumed; tuned on lessons (6.7) |
| CULL, **MODE_BUDGET, MAX_RENDER_MODES, N_MIN**, KILL_T60 | 2e-6, **1600, 2000, 6**, 30 ms | culling, soft and hard bound, steal fade | cost (§7) |
| **BODY_F0/DF/NLO/ETA/T60MAX** | **40 Hz, 19.5 Hz, 55, 0.02, 1.0 s** | low body band | Ege & Boutillon 2013 (density, loss factor) |
| **BODY_NHI/FHI/HI_OCT/QLO/QHI, BODY_GROUPS, BODY_GAIN** | **25, 1150 Hz, 12, 15, 25, 8, 0.25** | high body band, input groups, level | critique; level calibrated (6.6) |
| KNOCK_GAIN, **KEYBED_GAIN**, KEYBED_MS | 0.04, **0.00007**, 18 ms | knock (× KNOCKDB), keybed | Askenfelt (timing); levels calibrated (6.6) |
| NOISE_GAIN, NOISE_MS, **NOISE_LP** | 0.004, 3 ms, **7000 Hz** | attack noise | calibrated; LP from the critique |
| THUD_REL, DNOISE_REL, **THUD_NOISE_LP, THUD_NOISE_DEPTH** | 0.15, 0.35, **1500 Hz, 1.0** | key thud, damper-felt noise | calibrated |
| SYM_NMAX/FMAX/IN/OUT | 4, 2000 Hz, 0.15, 1.0 | sympathetic bank | pedal halo −22.8 dB re direct over 0–2 s |
| DUP_* | 64, 3.0/4.0, +7/−9 ¢, 1.4 s, 0.018, 10 kHz | duplex | assumed; −35 … −41 dB re the note |
| OUT_GAIN, **FF_PEAK_MAX**, FF_RANGE_DB | 0.183, **1.41 (+3 dBFS)**, 11.5 dB | level, ff guard, ff range | matched to the current app |
| module: **AUTO_PEDAL, STEP_DAMP** | **0.85, 2 semitones** | `PianoSpec` | tuned (6.7) |

Per-key results (all 88 in `piano-model-tables.json`):

| midi | f0 Hz | ¢ stretch | B | S | β | Z | mh g | p | log₁₀K | contact @2 m/s | modes | partials |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 21 A0 | 27.4045 | −6.02 | 1.80e-4 | 1 | 0.122 | 18 | 9.0 | 2.3 | 6.945 | 4.94 ms | 126 | 96 |
| 33 A1 | 54.8629 | −4.32 | 1.25e-4 | 2 | 0.1215 | 9.41 | 4.9 | 2.3 | 7.441 | 3.35 ms | 123 | 93 |
| 48 C3 | 130.580 | −3.08 | 1.60e-4 | 2 | 0.1209 | 4.0 | 3.6 | 2.4 | 8.713 | 2.60 ms | 86 | 56 |
| 60 C4 | 261.362 | −1.75 | 3.60e-4 | 3 | 0.1205 | 2.06 | 2.97 | 2.5 | 9.682 | 2.10 ms | 68 | 33 |
| 72 C5 | 523.317 | +0.22 | 9.00e-4 | 3 | 0.120 | 1.6 | 1.6 | 2.67 | 9.906 | 1.50 ms | 41 | 19 |
| 84 C6 | 1047.59 | +1.81 | 2.60e-3 | 3 | 0.110 | 1.3 | 0.95 | 2.83 | 10.679 | 1.00 ms | 28 | 11 |
| 96 C7 | 2100.43 | +6.13 | 7.50e-3 | 3 | 0.100 | 1.1 | 0.6 | 3.0 | 12.002 | 0.60 ms | 18 | 6 |
| 108 C8 | 4231.03 | +18.52 | 2.00e-2 | 3 | 0.090 | 0.9 | 0.32 | 3.0 | 13.048 | 0.30 ms | 9 | 3 |

---------------------------------------------------------------------------------------------------------------------------

## 6. Objective verification (lab: `analyze.mjs` → `verification.json`, plus `exp/` and `critic-rerun/`)

### 6.1 Partial frequencies vs f_n = n f0 √(1+Bn²) (render 0.3–2.3 s, 2¹⁸-point FFT)

| key | A1 | C3 | C4 | C5 | C6 | C7 |
|---|---|---|---|---|---|---|
| max error n ≤ 12 | 1.45 ¢ | 1.31 ¢ | 1.18 ¢ | 1.36 ¢ | 3.39 ¢ | 3.42 ¢ |
| B re-fitted / set | 1.37/1.25e-4 | 1.60/1.60e-4 | 3.63/3.60e-4 | 9.03/9.0e-4 | 2.60/2.6e-3 | 7.40/7.5e-3 |

### 6.2 Decay per register (fundamental, mf, dB/s; unchanged from v1 by construction: χ keeps partial 1)

| key | A0 | A1 | C3 | C4 | C5 | C6 | C7 |
|---|---|---|---|---|---|---|---|
| prompt / aftersound | −1.8 / −1.9 | −4.9 / +0.3 (beat) | −5.4 / −0.9 | −8.8 / −3.2 | −14.8 / −5.1 | −26.7 / −6.4 | −53.1 / −14.4 |
| target range (prompt) | 1–5 | 2–7 | 3–10 | 6–14 | 10–24 | 18–45 | 30–90 |

Ranges: the trend in Cheng, Dixon & Mauch 2015, Weinreich's C4 example (≈ 8, aftersound < 2), Wogram's T20 of 0.7–3.8 s.

### 6.3 Velocity timbre

C4 (centroid 20–520 ms, Hz), vel 0.12 → 1.0: new 483, 511, 553, 598, 635, 663, 680, 695 (monotonic); old 308–317 flat.

| key | centroid pp / mf / ff, long window (old) | partial 2, 3 at mf, dB re 1 (old) | partial 2 pp → ff |
|---|---|---|---|
| A0 | 123 / 170 / 233 (56) | +17 / +24 (A0 is weak at f0, as real bass) | — |
| C2 | 291 / 443 / 564 (115) | +12 / +17 | — |
| C6 | 1064 / 1093 / 1287 (1525) | −18.3 / −26.6 (+0.4 / −7.8) | −22 → −10 |
| C7 | 2110 / 2144 / 2184 (2457) | −13.1 / −29.3 (−11.8 / −16.8) | −27 → −12 |
| C8 | 4277 / 4292 / 4296 (4715) | −30.7 / −52.3 (−9.8 / −19.7) | −39 → −30 |

The attack window (2–87 ms) shows more of the change: the C6 centroid goes 1077 → 1571 Hz (+46 %) from pp to ff.
The new piano is brighter than the current one from C2 to C5 and still darker in the top octave. The current top
octave has partial 2 at −10 dB and energy above 6 kHz at −21 dB (chromatic file), which suits a sampled piano with a
bright lowpass better than a real treble string.

### 6.4 Even loudness across the keyboard (first 0.4 s, BS.1770)

| | new mf (vel 0.55) | old mf | new ff (vel 1.0) | old ff |
|---|---|---|---|---|
| SD across 88 keys | **0.23 LU** | 1.42 | **0.40 LU** | 1.36 |
| range | −23.21 … −22.34 | −27.63 … −21.52 | −14.67 … −12.67 | −18.90 … −12.86 |
| through a 150 Hz high-pass (headset proxy) | SD 0.44; bass − middle −1.0 dB | SD 3.64; −8.3 dB | | |

### 6.5 Clicks, aliasing, DC, denormals

| check | result |
|---|---|
| highest mode frequency | 14 882 Hz (guard 0.45·sr): no partial can alias |
| energy above 18 kHz | −53 dB re total (v1 −45; the attack noise is now low-passed) |
| damper ramp: HF (> 7 kHz) of the removed signal re the mix | −48.8 dB (critic `artefacts.mjs`) |
| voice steal fade: same measure | −71.3 dB |
| damper-thud component above 7 kHz | −57.8 dB of its own energy |
| DC (mean re peak) | −73 … −89 dB; every source is a zero-mean sinusoid |
| subnormal states / outputs after 90 s of pedal-down ringing | 0 / 0 |
| re-strike, both event orders | identical to the plain re-strike (difference exactly 0) |

### 6.6 Transient levels (first 60 ms, dB re the string tone; `check-levels.mjs`)

| | C2 pp/ff | C4 pp/ff | C6 pp/ff | C7 pp/ff | A7 pp/ff |
|---|---|---|---|---|---|
| knock (v2) | −35.2 / −37.9 | −24.3 / −27.1 | −19.5 / −25.3 | −16.2 / −24.4 | −11.4 / −22.1 |
| knock (v1) | −32.6 / −41.3 | −24.1 / −27.5 | −20.0 / −24.6 | −16.7 / −23.6 | −12.4 / −20.8 |
| keybed | −25.7 / −29.5 | −26.0 / −28.8 | −26.4 / −30.5 | −25.1 / −30.6 | −25.1 / −31.4 |
| attack noise | −58.7 / −47.0 | −56.8 / −43.8 | −54.7 / −43.0 | −52.4 / −42.2 | −52.1 / −42.8 |

Knock character (`exp/knock-tonal.mjs`, knock only, first 85 ms):

| | 1/3-oct peak-to-mean < 1 kHz | 1–3 kHz | spectrum corr. C5 vs C#5 / G5 |
|---|---|---|---|
| v1 (24 modes) | 4–10 dB | 10–13 dB | 0.93 / 0.92 |
| v2 (80 modes, per-key weights) | 2–5 dB (noise-like) | 5–10 dB | 0.55 / 0.58 |

### 6.7 The app's automatic sustain (lessons, keys held 90 %; `exp/wash.mjs`, critic's metrics)

Previous different pitch's fundamental, 150 ms into each new note, re the new one (dB; lower = clearer):

| lesson | old (today) | v1 wiring | v2 step damp only (d = 1) | **v2 default (d 0.85 + step)** | d 0.8 + step | d 0.7 + step |
|---|---|---|---|---|---|---|
| Für Elise | −10.5 | −3.4 | −9.6 | **−10.6** | −11.2 | −12.7 |
| Ode to Joy | −18.7 | −5.1 | −17.6 | **−19.2** | −20.2 | −22.4 |
| C scale | −21.3 | −5.9 | −17.9 | **−19.1** | −19.9 | −21.6 |
| Twinkle | −19.7 | −9.0 | −18.3 | **−20.3** | −21.3 | −23.3 |
| Jingle Bells | −20.1 | −10.7 | −17.4 | **−19.7** | −20.4 | −21.4 |
| Frère Jacques | −12.9 | −4.6 | −13.1 | **−13.7** | −14.5 | −16.7 |
| arpeggio let go: ring re old, 0.5–1.5 s / 1.5–3 s | 0 / 0 | +9.2 / +13.8 | +9.2 / +13.8 | **+2.5 / −3.9** | −1.4 / −14.8 | −9.7 / −24.3 |

d = 0.85 matches today's clarity on average (−17.1 against −17.2 dB). Chords and arpeggios still bloom about as long
as today, and held keys keep the full real sustain. The critic's onset-clarity number (energy after / before each
onset) is 2.6–7.4 dB against 5.7–14.4 for old. It falls because the key held into the next note has real sustain:
the "before" window is the previous note still under the finger. This is not pedal wash.

---------------------------------------------------------------------------------------------------------------------------

## 7. CPU and memory

### 7.1 Measured in Node (Apple M5 Pro, scalar JS, float32 storage; other agents were running Unity on the machine)

| item | Node |
|---|---|
| free mode, ns per mode-sample | 1.29 |
| driven mode (during hammer contact, ≤ 3 blocks) | 1.84 |
| sympathetic bank, 336 resonators | 1.63 ns per resonator-sample → 141 µs per block |
| body bank (80 modes, active) | 48 µs per block |
| silent piano, pedal down | **3.6 µs per block** (v1 142) |
| note-on (pulse blend, correction, mode copy, 8 knock rings) | 16 µs |
| boot (whole model incl. felt calibration) | 0.41–0.43 s |

| scenario (256-frame block) | avg | p99 | max | modes rendered max | steals / trimmed modes |
|---|---|---|---|---|---|
| lesson phrase, mf, pedal | 234 µs | 274 µs | 304 µs | 270 | 0 / 0 |
| pedal down, ff two-hand chords + runs, 8 s | 620 µs | 740 µs | 894 µs | 1720 | 49 / 0 |
| pedal down, ff glissando A0–C8 in 1.8 s | 620 µs | 781 µs | 840 µs | 1816 | 42 / 0 |
| two palms, 18 keys ff every 0.35 s | 597 µs | 809 µs | 953 µs | **2000** | 125 / 4658 |
| forearm on 30 bass keys, ff × 3 | 521 µs | 789 µs | 834 µs | **2000** | 43 / 4049 |
| leaning on all 88 keys | 578 µs | 642 µs | 1052 µs | 1670 | 9 / 3878 |

What the bound costs in sound (`exp/budget-loss.mjs`, residual against an unbounded engine): ff pedalled two-hand
chords −19.7 dB at 1600/2000, −11.3 dB at 1200/1600. The palm-smash case loses most of the older clusters' pedal
ring (−3.3 dB), which is acceptable for noise-making.

### 7.2 Quest 3S estimate

Assumed **Quest-Burst / Node time ratio = 0.5**, with 1.0 as the pessimistic bound. Burst runs 4 independent float4
chains per iteration, so it is throughput-bound: about 0.66 cycles per mode-sample on a Cortex-A78-class core, or
0.28 ns at 2.36 GHz (ratio 0.2). The 0.5 leaves 2.5× for codegen, cache and throttling. Per the critic's source
(UploadVR), the XR2 Gen 2 has 2 performance and 4 efficiency cores. If the mixer thread lands on an efficiency core,
assume 1.0. UNVERIFIED: measure (§9).

| | Node | Quest (×0.5) | Quest (×1.0) |
|---|---|---|---|
| typical (lesson) | 0.23 ms | **0.12 ms** | 0.23 ms |
| worst case p99 / max | 0.81 / 1.05 ms | **0.41 / 0.53 ms** | 0.81 / 1.05 ms |
| theoretical hard bound (2000 driven modes + banks) | 1.13 ms | 0.57 ms | 1.13 ms |

Levers if the device is slower: MODE_BUDGET/MAX_RENDER_MODES 1600/2000 → 1200/1600 (−0.25 ms worst; pedal ring
thinner in ff chords), SYM_NMAX 4 → 2, or the sympathetic bank at half rate (all its resonators are < 10 kHz).

### 7.3 Memory

| data | size |
|---|---|
| mode tables (5548 modes × 9 float + short + byte) | 216 KB |
| excitation correction (66 576 float) | 266 KB |
| hammer pulse table (105 521 float) | 422 KB |
| body bank (80 modes) + 9 input rings (2304 float) + sympathetic (336) | 96 KB |
| 88 voices (192 modes × 7 float + 2 × 192 short + 640-float drive) | 766 KB |
| **total** | **≈ 1.77 MB** (the current piano's SampleBank: 67.7 MiB, all freed) |

---------------------------------------------------------------------------------------------------------------------------

## 8. Test vectors (EditMode; `Tests/EditMode/Audio/Piano/Reference~/test-vectors.json`, raw float32 vectors `vec-*.f32`)

Tolerances: boot constants to 1e-6 relative; listed samples to |err| ≤ 2e-4 × peak; whole vectors SNR ≥ 80 dB against
the `.f32` files. The JS reference keeps state in float32 but computes each step in double. C# float math differs by
about 1e-7 per step.

### 8.1 Ordering and rounding rules (everything a bit-level match depends on)

1. Events: `at = max(frame, blockStart)`, applied in (at, seq) order; seq = command-ring order.
2. Guard: a Damp is dropped when an On of the same key has the same `at`.
3. Admission: computed once per block before the event loop (3.5). Steal scans k = 0..87 and takes the first strict minimum
   of Level. Admitted modes are the first `admit` missing model modes in model order.
4. RNG: Mulberry32 as spec 06. Two xorshift32 streams: A (attack noise, one draw per sample where env ≠ 0, in `_shared`)
   and Thud (seed ^ 0x9E3779B9, one draw per thud sample, in event order). First draws, seed 1: A −0.9998741, −0.9685051,
   0.2328082; Thud seed 2654435768 → −0.3669312, 0.7824196, 0.8004503.
5. Float32 rounding points: voice/bank state after every segment; poles after every ramp step (`Pr *= Dms`); Dms =
   (float)exp(dd·DmLog), or DmStep when dd == 1; NzLp after every sample; SymPedalScale; corr and drive at note-on.
6. Ramps step at the end of every rendered segment while DampLeft > 0. Segments are split at events and limited to 32
   frames from their own start.
7. Cull order: slot order with swap-remove (the last slot moves into the hole and is re-tested). Level sums the kept
   modes in that order.
8. Pedal deferral: re-queued pedal events keep their seq and carry the PedalGen tag (3.3).

### 8.2 Values

- Mulberry32(4060) first three draws: 0.8306885638739914, 0.3529554770793766, 0.20656749629415572.
- `hammerFromFinger`: 0.07 → 0.75, 0.2 → 1.529026, 0.5 → 2.847217, 1.5 → 6.
  `hammerFromVel`: 0.1 → 0.75, 0.38 → 1.542305, 0.62 → 2.860757, 0.84 → 4.528844, 1 → 6.
- `E(d)`: 0 → 1, 0.5 → 0.25, 0.85 → 0.0225, 1 → 0.
- Stretch cents: 2.2.
- Keys:

| midi | f0 | detune ¢ | hammer factors | log₁₀K | M | gain | b2 / bL | ffCutDb | pulse row 5 (1.561124 m/s): len, peak N, Σ, [10], contact |
|---|---|---|---|---|---|---|---|---|---|
| 21 | 27.40455 | 0 | 1 | 6.944521 | 126 | 3.703026e-5 | 3.84e-7 / 0 | −2.439586 | 261, 10.2129, 1166.1, 0.09614324, 5.4375 ms |
| 33 | 54.86287 | ±0.4512966 | 0.9691558, 1.045874 | 7.440974 | 123 | 5.613342e-5 | 3.641379e-7 / 0 | −1.0666 | 179, 8.26293, 584.9073, 0.299305, 3.708333 ms |
| 60 | 261.3618 | −0.6247037, −0.1102311, +0.6247037 | 0.9706567, 1.022332, 0.9766437 | 9.681738 | 68 | 2.183988e-4 | 3.194483e-7 / 0 | 0 | 104, 6.805111, 369.1489, 4.300627, 2.145833 ms |
| 84 | 1047.594 | −0.670204, −0.1794992, +0.670204 | 1.040562, 0.9777418, 0.9683466 | 10.67904 | 28 | 7.841996e-4 | 1.258759e-7 / 1.6138e-4 | −1.89743 | 52, 5.277601, 130.8188, 2.948803, 1.083333 ms |
| 108 | 4231.03 | −0.8817702, 0.2677671, +0.8817702 | 1.044049, 0.9949568, 0.9747232 | 13.04757 | 9 | 4.466738e-3 | 2.4e-8 / 9.229963e-4 | −3.928897 | 15, 5.874601, 44.53568, 2.770775, 0.3125 ms |

  C4 body weights: 0.6486218, −1.349203, −1.063383, −1.209952, 0.7095866, 1.016089, 1.033988, 0.7477274; knock 1.003021e-5.
  C4's first four modes (f Hz, σ, pr, pi, ar, ai, dmStep, dmLog): (261.3841, 0.4523241, 0.9994053, 0.0342081, 8.498943e-5,
  2.68414e-5, 0.9999827, −1.729706e-5), (261.327, 0.4184885, 0.9994063, 0.03420065, 2.687377e-5, −6.102813e-5, …),
  (261.491, 0.3963415, 0.999406, 0.03422213, 2.939462e-5, 3.479359e-5, …), (523.0648, 0.5030808, 0.9976465, 0.06841482,
  −1.687057e-4, 7.673247e-5, 0.9999756, −2.446174e-5). A0 velocity modes: i 112 (54.80822 Hz, vcap 3, pn 2), 113, 114.
  Body modes 0–3 (pr, pi, g, group): (0.9998421, 0.005290462, 0.04679138, 0), (0.9998198, 0.008527539, −0.09736505, 1),
  (0.9998029, 0.01031672, −0.1337355, 2), (0.9997746, 0.01276687, −0.2513108, 3); mode 55: (0.9848921, 0.1501795, 1.078766, 7).
- Renders (mono sum lo+mid+hi, 24 000 frames, events at frame 0 unless given):

| vector | events | peak | rms | y[10] | y[480] | y[1000] | y[10000] | y[23999] |
|---|---|---|---|---|---|---|---|---|
| C4-vh2-strings (sym, body off) | on 60 @ 2 | 0.2371698 | 0.05525931 | −0.02641386 | 0.04842127 | 0.03820643 | 0.05494878 | −0.07807653 |
| C4-vh2-full | on 60 @ 2 | 0.239023 | 0.05539312 | −0.0261706 | 0.04592304 | 0.03132935 | 0.05453078 | −0.07792364 |
| A0-vh6-damp | on 21 @ 6; damp @ 0.25 s | 0.7482986 | 0.1978838 | −0.009926641 | 0.1861975 | 0.01585829 | 0.0489748 | −0.0006463209 |
| C7-vh1 | on 96 @ 1 | 0.08124133 | 0.01305406 | −0.05611692 | 0.02647805 | −0.01261538 | 0.01102407 | −0.008445651 |
| pedal-chord | pedal 1; on 48 55 60 64 @ 3; damp all @ 0.2 s | 0.8312537 | 0.1723002 | 0.267908 | 0.04854544 | 0.1217236 | 0.1491302 | −0.001283874 |
| half-pedal-keyup | pedal 0.85; on 60, 67 @ 2; keyup both @ 0.1 s; @ 0.3 s: pedal 0, damp 60 (thud), pedal 0.85 (deferred to 0.42 s), on 64 @ 1.5 | 0.4548081 | 0.07677661 | −0.03565319 | 0.04782836 | 0.02914726 | 0.02257825 | 0.0458607 |
| restrike-release-first | on 64 @ 2; @ 0.2 s: damp 64 then on 64 @ 3 | 0.3310923 | 0.1089043 | 0.122621 | −0.01126887 | 0.1131171 | 0.2652594 | 0.06910739 |
| restrike-plain | on 64 @ 2; on 64 @ 3 @ 0.2 s | identical to the previous vector | | | | | | |

- Behaviour tests: the two re-strike vectors are identical (also with the damp enqueued after the on). A re-strike keeps
  one voice and adds energy. A stale `Release` is ignored. Keys 89–108 ignore damp and key-up. Leaning on all 88 keys
  (vh 4, pedal down) never has more than MAX_RENDER_MODES modes alive (lab: 1670, 3878 modes trimmed, 12 steals).
  With `MODE_BUDGET = 120` the busy test steals and stays click-free (6.5). 90 s of ringing into silence leaves no
  subnormal state. A silent engine with the pedal down costs < 10 µs per block. Per-key loudness SD ≤ 0.4 LU at mf and
  ≤ 0.6 LU at ff. A pedal lift and re-press in the same frame keeps the pedal up 0.12 s.

---------------------------------------------------------------------------------------------------------------------------

## 9. How to verify on the device

1. EditMode tests of §8 on the Mac (Burst compiled, FloatMode.Default; do not use FloatMode.Fast for the boot model).
2. Development build with `AudioEngine.LatencyProbe` on. Once a second, log `PianoEngine` avg/max µs per block,
   `ActiveModes`, `MaxRendered`, `ActiveVoices`, `StolenVoices`, `TrimmedModes`, `DroppedDamps`, `DeferredPedal`. Targets:
   lesson play ≤ 0.2 ms avg; the scripted scenarios of 7.1 (via `FakeHandSource` or the desktop keyboard), **including
   palms, forearm and all 88 keys**: ≤ 0.6 ms avg, **≤ 1.0 ms max**, 0 underruns in logcat (`AudioFlinger` "underrun").
   Then 10 minutes of busy play to check thermal drift. If the max exceeds 1.0 ms, switch to 1200/1600 (7.2).
3. Boot: model built on the worker in < 1 s (with `Kfelt` from `piano-model-tables.json`), first key ready at once.
4. Record the device output of the A/B score and compare per-piece LUFS with the lab (±1.5 LU after the spatializer).
5. Listening checklist on the headset:
   (a) fast repeated notes: re-strike in place, no clicks, no weak or cut-off repeats;
   (b) the bass damper: a soft 0.6–0.9 s tail, no hard cut;
   (c) **a lesson with the automatic sustain: no smear between scale steps, while arpeggios still bloom**. AUTO_PEDAL is
       the knob: 0.8 drier, 0.9 wetter;
   (d) after a phrase gap the halo really clears;
   (e) the top octave rings without dampers and gets brighter when struck harder;
   (f) the knock: a soft thump, never a pitched "tock";
   (g) ff chords do not hit the master ceiling (`MasterBus` gain reduction < 6 dB);
   (h) a forearm on the keys: no dropouts, no crackle.
6. Keep the old `PianoSynth` behind a dev toggle for one review round so the user can A/B in the headset.

---------------------------------------------------------------------------------------------------------------------------

## 10. A/B renders (`piano-*-old/new.wav`, with a listening guide)

48 kHz, stereo, 24-bit. Old = the current app's piano (spec 06 reference: play stage with lowpass, soundboard convolver,
knock, damper thud, release τ 60 ms). New = this engine. Both get the same notes, touch (legacy vel), ±6 ms seeded chord
spread, register panning (lo −0.35, mid 0, hi +0.35) and the **same room** (the app's main-room IR, seed 5, send 0.22,
return 0.26). There is no master compressor. Both files of a pair have the same BS.1770-4 integrated loudness: −18 LUFS
unless a true peak would pass −1 dBTP, in which case the pair goes to the lower common target. Total 59.9 MB.
Two pieces use the **app's own automatic sustain** (`app-sim.mjs`). Old: today's app (a key-up rings until the phrase
lift; a re-strike releases with a thud). New: the v2 wiring (half pedal 0.85, step damp, key-up, re-strike in place).
The other pieces use a pianist's pedal (full depth).

| piece | s | LUFS (both) | raw old / new LUFS | true peak old / new dBTP | what to listen for |
|---|---|---|---|---|---|
| `piano-lesson-ode-app` (Ode to Joy lesson, app sustain) | 11.1 | −18.0 | −25.8 / −24.6 | −5.3 / −5.3 | clear steps, real sustain, no smear |
| `piano-lesson-elise-app` (Für Elise lesson, app sustain) | 14.7 | −18.0 | −23.5 / −23.2 | −5.3 / −4.0 | the E–D# turns stay clean |
| `piano-fur-elise` (+ Beethoven's left hand, pedal per bar) | 14.7 | −18.0 | −23.0 / −22.3 | −5.2 / −6.1 | phrasing, pedal changes |
| `piano-greensleeves` (traditional, first phrase, pedal per bar) | 16.2 | −18.0 | −23.8 / −22.7 | −1.8 / −3.8 | warmth, no wash |
| `piano-chords` (held, then pedal, then a pedalled staccato C2) | 11.0 | −18.86 | −21.5 / −21.7 | −1.1 / −2.4 | damper vs pedal; sympathetic halo |
| `piano-velocity-ladder-C4` (8 steps pp → ff) | 10.5 | −19.51 | −22.3 / −21.6 | −2.1 / −1.1 | timbre opening with velocity |
| `piano-chromatic` (A0–C8, mf, 0.12 s per note) | 12.2 | −18.0 | −19.6 / −19.9 | −7.8 / −3.3 | evenness, bass growl, treble ring |
| `piano-bass-release` (A1 ff 3 s, A0 1.6 s, released) | 6.2 | −21.52 | −27.1 / −21.9 | −1.1 / −8.2 | damper tail and thud |
| `piano-single-A0` (mf) | 3.0 | −23.13 | −32.9 / −24.3 | −1.1 / −10.4 | low bass |
| `piano-single-C4-ff` | 2.5 | −18.0 | −20.1 / −19.9 | −2.8 / −1.3 | beating, double decay |
| `piano-single-C7` (mf) | 2.0 | −18.0 | −27.0 / −28.3 | −1.9 / −2.7 | treble, knock, duplex |

At the same touch, the new piano is within ±1.2 dB of the old in every musical piece (raw LUFS above). The exception
is the low bass, where the old piano is weak and short.

---------------------------------------------------------------------------------------------------------------------------

## 11. Risks and open points

1. **Brighter than today from C2 to C5; still darker in the top octave.** If the first listen finds it too bright, lower
   FELTX 9 → 6 first, then RAD_F2 1800 → 1200 Hz. If the top octave sounds dull, raise NULLSLOPE above C6 5 → 7, or
   LOSSLIN at C8 0.9 → 0.95.
2. **The half pedal is a design choice, not physics of the app.** AUTO_PEDAL 0.85 and STEP_DAMP 2 were tuned to match
   today's lesson clarity while keeping real sustain. Both are one constant each. Held keys always keep the full sustain.
3. **Re-strike superposition** (critique REPEAT): repeated notes vary by −2…+4 dB in energy depending on the string's
   phase, as a linear string does. If repeats sound uneven on the headset, the fix is a partial reset of the old state
   at re-strike (hammer-felt absorption, e.g. × 0.5 over the contact), which is not implemented.
4. Treble hammer masses are effective values, calibrated so the fundamental dominates.
5. The soundboard, body bank and phase are seeded synthetic models, not measurements of a particular instrument.
6. Phantom, longitudinal, duplex and damper constants, the treble loss share χ and the half-pedal law are informed
   assumptions. Their status is listed in §5.
7. **Quest CPU ratio** (0.5) is analytical. The hard bound guarantees a ceiling (1.13 ms Node-equivalent); measure it (§9).
8. Stretch tuning: from C3 to C6 (the harp's range) it stays within −3.1 … +1.8 ¢ of equal temperament.
9. The sympathetic bank is driven by the radiated mix (not the bridge force), an approximation.
10. A single dry ff note can peak at up to +3 dBFS. Today's piano peaks higher in chords (+13 dBFS two-hand ff), so the
    master bus already handles this. Check item 9.5(g).

## 12. Files

- `Tools/SoundLab/piano/piano-engine.mjs` (v2 reference engine), `piano-engine-v1.mjs` (first design), `app-sim.mjs` (the Piano
  module's automatic sustain: old / v1 / v2 wiring), `render-util.mjs` (offline driver).
- `old-piano.mjs` + `verify-old.mjs`: the current piano (nulls the spec 06 WAVs at 76–103 dB SNR).
- `analyze.mjs` (§6), `check-levels.mjs` (6.6), `bench.mjs` (§7), `testvectors.mjs` (§8), `render-ab.mjs` (§10),
  `dsp-util.mjs` (WAV, FFT, BS.1770-4, true peak, convolution).
- `exp/`: timbre, attack-spec, modeamp, treble-combo, ffcut, knock, knock-tonal, phantom-cap, wash, budget-loss, clicks
  (the design experiments behind §§2, 6).
- `critic-rerun/`: the critic's music, loud and notes scripts re-run against v2, with outputs written here; the critic's
  own folder is untouched.
- Outputs: `verification.json`, `bench.json`, `test-vectors.json`, `vec-*.f32`, `piano-model-tables.json`, `renders-report.json`.

## 13. References (numbers used as numbers; no recordings, IRs or samples were used)

- H. Fletcher, E. D. Blackham, R. Stratton, "Quality of piano tones", JASA 34(6), 749–761 (1962).
- T. Cheng, S. Dixon, M. Mauch, "Modelling the decay of piano sounds", ICASSP 2015.
- Acta Acustica 2021, doi 10.1051/aacus/2021002 (B along the compass).
- G. Weinreich, "Coupled piano strings", JASA 62(6), 1474–1484 (1977), and his KTH lecture.
- A. Chaigne, A. Askenfelt, "Numerical simulations of piano strings I/II", JASA 95, 1112 and 1631 (1994) (C2/C4/C7
  string and hammer parameters; the C7 loss value is quoted from memory, UNVERIFIED).
- J. Bensa, S. Bilbao, R. Kronland-Martinet, J. O. Smith, "The simulation of piano string vibration", JASA 114, 1095 (2003)
  (b1 + b3 ω² loss form).
- A. Askenfelt, E. Jansson, "From touch to string vibrations", JASA 88, 52 (1990), and Askenfelt's KTH lectures
  (stricont, motions, keybott).
- H. A. Conklin, "Design and tone in the mechanoacoustic piano, Parts I–III", JASA 99/100 (1996), his KTH lecture on
  hammers, and "Generation of partials due to nonlinear mixing in a stringed instrument", JASA 105, 536 (1999).
- K. Ege, X. Boutillon, "Vibrational and acoustical characteristics of the piano soundboard", arXiv 1210.5688 (2013)
  (modal density ≈ 1/19.5 Hz, loss factor ≈ 2 %).
- B. Bank, L. Sujbert, "Generation of longitudinal vibrations in piano strings", JASA 117, 2268 (2005).
- W. Wogram, KTH lecture "decay". R. E. Kirk, JASA 31, 1644 (1959) (from memory). A. Stulov, Vienna Talk 2015.
- N. Giordano, "Mechanical impedance of a piano soundboard", JASA 103, 2128 (1998). ITU-R BS.1770-4.
- UploadVR (XR2 Gen 2 core layout), via the critique.
