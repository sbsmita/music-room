# 10-upgrade · drums: a physically modelled acoustic kit (implementation spec for Unity C# / Burst), revision 2

> **2026-11-11: not in the app.** The modelled kit built from this spec (`Audio/Sound/Drums`, `MusicRoomDrumKit.bytes`) was deleted: the drums play the classic sound only (the user's choice). `Tools/SoundLab/drums/` stays (the hand drums' lab uses its analysis). This spec is the design record. Backup: `music-room-vr-wip/unity-backups/pre-classic-cleanup/`.

Key: `drums`. Replaces the current kit (`drumHit` / `metalVoice` in `../06-audio-dsp.reference.mjs`, implemented as
`Assets/MusicRoom/Audio/Synths/DrumSynth.cs`). Reference engine (transliterate this):
`Tools/SoundLab/drums/engine/drumkit.reference.mjs` (v2, 1178 lines, Node 26, no packages).
Table data (every derived per-mode table): `Tests/EditMode/Audio/Drums/Reference~/kit-tables.json` (`version: 2`).
The v1 engine is kept in the Node lab for comparison as `engine/drumkit.v1.reference.mjs`. Do not implement v1.

Every sound is synthesized by this code. No samples, impulse responses or third-party audio. Published measurements are
used only as numbers (section 14).

---------------------------------------------------------------------------------------------------------------------------

## 0. Summary

**What it is.** Each of the 8 pieces is one persistent physical object that keeps ringing: kick, snare, hat, tom1, tom2,
floor, crash and ride. A hit does not start a voice. It *strikes the object*: a stick (or felt beater, or fingertip) of
known mass and speed pushes into the head, and the head pushes back through its own modes. So:
- rolls build up and flams interfere;
- a re-strike meets a head that is already moving;
- two sticks can be on one drum at the same time (left and right hand), and their forces are solved together;
- a choke is extra damping, and a pedal closing is the hats being clamped;
- worst-case CPU is fixed (all 8 objects ringing), whatever the hit rate.

**What is new against the current kit.** The current kit is synth-style: a 165→46 Hz sine-glide kick, a triangle-plus-
noise snare, near-sine toms, and cymbals made of six square waves (the 808 recipe, the "metallic/buzzy" timbre). Every
repeated hit is identical. The new kit:
- **Membranes.** Bessel modes of the batter head, with the coupled batter/resonant-head pairs measured on a real snare
  (Rossing 1992). Degenerate pairs are split by uneven tension, so the head beats slowly. A dense high-mode field gives the
  crack. Shell, hoop and stick wood modes are included.
- **Contact.** The stick–head contact is a Hunt–Crossley spring with an implicit solve, and the head *stiffens under the
  stick* (its tension rises with the cone deflection). Contact time therefore shortens and the force pulse sharpens as the
  stick gets faster: snare 5.2 ms at 0.4 m/s → 3.9 ms at 10.5 m/s, peak force 2.5 → 91 N. Wagner 2006 measured 3–6 ms,
  shorter at forte, and 3 → >100 N from piano to forte.
- **Glide.** Tension modulation follows the head's own stretch energy: a full stroke is up to 60–90 cents sharp on toms and
  kick and settles in ~100 ms; a soft stroke has almost none.
- **Brightness.** Hard strokes are brighter because the stiffened contact is shorter and because energy spreads from the low
  modes into the high ones at large amplitude.
- **Snare wires.** Four lumped wire groups sit in one-sided contact with the snare-side head and buzz in time with it. The
  air between the drums couples the kick and toms to them: a hard kick buzzes the snare at −32 dB.
- **Cymbals.** Inharmonic plate modes with a constant modal density above ~1 kHz (as a flat plate has), cascade-fed noise
  bands for the modes too dense to list, and a bell subset. The stick is a Hertz spring on the plate's resistive impedance.
  A nonlinear energy cascade carries energy up the bands, so the 3–11 kHz wash builds for ~100–300 ms after a hard strike.
  The tail is a dense wash, not a few humming partials.
- **Hi-hat.** Openness (0..1) sets both the damping and the plate gap. A half-open hat clashes. Closing the pedal clamps an
  open hat by 76 dB within 150 ms.
- **Micro-variation.** Seeded strike angle and position plus ±3 % speed: 8 identical strokes spread by 0.95 dB, and
  consecutive ones correlate ~0.6–0.7.

**Budget.** No baked audio at all. State is about 0.3 MB, tables 0.56 MB as JSON. CPU, Node 26 on the dev Mac: the worst
case a seated player can produce (both sticks and the kick landing on one sample every 120 ms, everything else ringing) is
2.1 µs/sample, 0.54 ms per 256-sample block on average and 0.78 ms worst block; the lesson grooves run at 0.21 ms. Estimated
Quest 3S with Burst: about the same in milliseconds (ratio 0.6–1.2, section 9). That is 0.3–0.95 ms of the ~2 ms graph.

**Status.** Built and measured in Node. **Nothing has been auditioned by ear**: the user must judge the A/B files
(section 13).

---------------------------------------------------------------------------------------------------------------------------

## 0.1 Revision 2: what changed after the independent critique

The critique ("better-with-fixes") found three high and three medium issues. All six are fixed. The critic's own scripts
were re-run against v2; the acceptance targets are now automated in `tools/acceptance.mjs`
(26 tests, all pass, `out/acceptance.json`).

| # | issue (critic) | fix | before (v1) → after (v2), critic's scripts |
|---|---|---|---|
| H1 | one-directional low-frequency push under every snare/tom hit: the "input before rotation" recurrence has DC gain −½ | every modal output is now the state **before** the update plus c₀·input (§4.1). Zero DC, **and** no constant high-frequency floor. The critic's suggested form (post-update output + ½·input) also zeroes DC but doubles the high-frequency floor; with the stochastic forcing of §4.6 that made a 16 kHz hiss at −35 dB on toms (now −42 dB) | snare u.84 energy < 60 Hz −6.9 → −22.1 dB; mean/rms −0.17 → 0.00; tom1 −3.6 → −21.5 dB; pedal chick −26.6 → −56.6 dB (c4). Old kit −21 / −25 / −50 |
| H2 | metallic crash tail: 20 strongest partials hold 64–84 % of the 0.3–9 kHz energy; low-mid hum 11–21 dB over the wash after 1 s | crash and ride: constant modal density 0.06 /Hz from 1 kHz to 3–3.4 kHz, cascade-fed noise bands above (the critic's option b), flatter and shorter decay (crash T60 at 300 Hz 6 → 3.6 s, exponent .62 → .28; ride 11 → 6 s), steeper radiation tilt below 1 kHz | crash u.95, 0.6–1.6 s: top-20 share 2–5 kHz 66 → 21 %, 5–9 kHz 64 → 18 %; flatness −15.2 → −5.5 dB; hum − wash at 1–3 s +11.6 → −1.2 dB (c6, c13). Ride bow u.6: 2–5 kHz 76 → 30 %. Open hat 2–5 kHz 73 → 41 % (c16) |
| H3 | dynamic range 2–2.7× the old kit's, grooves quieter in the app | stick speed `12·u^1.1` m/s (the critic's curve), cymbals `12·u^0.9`, kick beater `6.3·u^0.85`; kit levels re-balanced on the lesson grooves | snare range u.15→.95 27.2 → 19.7 dB (old 18.1); crash 26 → 16.9 (old 15.4); at app level through the master compressor: rock −1.8 → −0.5 dB, fill −2.4 → −2.1, jazz −7.5 → −0.6 dB re old (c15); typical-stroke groove −0.56 LU (`drums-soft-groove-applevel`) |
| M1 | two sticks on one drum: the second stroke overwrote the first contact (force 26 → 2.6 N in one sample, +7…+11 dB) | two contact slots per membrane (the kick has one) solved together with a 2×2 Newton; a third stroke waits (≤ a few ms) for a stick to leave | force step 90 % → 3–4 % of Fmax; two u.8 strokes 1/3/6 ms apart: snare +8.9/+9.7/+4.4 → +2.2/+4.0/+3.6 dB, tom1 +7.2/+10.9/+11.2 → +2.2/+3.3/+3.2 dB (c14, c14h) |
| M2 | contact linear and long: 6.1 ms snare, 8.6 ms tom at every speed (Wagner: 3–6 ms, shorter as speed rises) | head stiffening under the stick (§4.4) and an effective tip mass of 11 g (a ~50 g stick held at the fulcrum) | snare 6.0 ms at every speed → 5.15 / 4.96 / 4.52 / 3.96 ms at 0.4 / 2 / 5 / 10.5 m/s; tom1 7.3 → 4.4 ms |
| M3 | worst audio block underestimated by ~30 % | new benchmark with the adversarial schedules, worst run reported (§9); mode culling, a sympathetic-force frequency limit and a fused contact pass pay for the new features | v1 claimed 583 µs, critic measured 761 µs; v2 measured: all-8-same-sample 579 µs average / 1035 µs worst, seated-player worst 539 / 781 µs, grooves 211–220 µs |

Also fixed (from the critic's other measurements):
- **Control-grid artefacts.** v1 applied energy transfers as state jumps every 32 samples (kick u1: |second difference| at
  grid phase 1 was 13 dB above the median; 33 % of the kick's > 6 kHz energy was grid-locked, c5/c5b). All transfers are now
  smooth (§4.6, §5.3): kick 13.0 → 1.3 dB, floor 9.6 → 0.5 dB.
- **Subnormals.** 604 subnormal states per minute of abuse → 0 (per-sub-block flush, c8).
- **Weak bells and pedal chick.** Ride and crash bell strokes sat 4–9 dB under the old kit's; the jazz "chick" on 2 and 4
  sat 14 dB under. Now: ride bell +1.2 dB over the bow, chick level with the old kit in the jazz groove.
- **Onset-safe analysis.** The lab's centroid/profile tools used Hann windows from the onset, which hid the first ~10 ms (and
  the push). `tools/tune.mjs` now renders every hit with 2 ms of silence first and uses rectangular, zero-padded windows.

Not changed, with reasons:
- **Same-sample unison on the snare is +6.5 dB** (two u.8 sticks landing on the same sample at r .2 and .25; the critic's
  rule of thumb is ≤ +6). Two sticks are two independent impulses at different spots (the strike angle is random), on a head
  that stiffens with deflection; there is no force step (4 % of Fmax), and 1–30 ms spacings are +2.2…+4.9 dB.
- **Kick contact stays long** (14–18 ms): a 40 g felt beater on a 22" head at 688 N/m tension has a mass–spring half
  period of ~16 ms. It was not flagged; it is listed in §15.

---------------------------------------------------------------------------------------------------------------------------

## 1. Files

**Lab:** the drums Node lab. Vendored at `Tools/SoundLab/drums/`: `engine/drumkit.reference.mjs`, `lib/analysis.mjs`,
`lib/mix.mjs`, `tools/patterns.mjs`. Its data (`kit-tables.json`, `test-vectors.json`, `acceptance.json`) is in
`Tests/EditMode/Audio/Drums/Reference~/`.

| file | what it is |
|---|---|
| `engine/drumkit.reference.mjs` | the new kit (v2): RNGs, Bessel helpers, velocity curves, `Bank` (SoA modal bank) and its kernels, `Slot`, `Membrane`, `Wires`, `Cymbal`, `DrumKit` (events, sub-block grid, sympathetic coupling) |
| `engine/drumkit.v1.reference.mjs` | v1, for comparison only |
| `engine/oldkit.mjs` | the CURRENT kit through the spec-06 reference `drumHit()`: ×1.7 level, wire rattle, choke τ 12 ms plus skin noise, pedal = choke + `hatClose` .7 |
| `lib/wav.mjs`, `lib/analysis.mjs`, `lib/mix.mjs` | 24-bit WAV I/O; FFT, BS.1770-4 loudness, 4× true peak, spectra (Hann and onset-safe rectangular), T60, peaks, click score; the shared listening mix (pan, 0.2 ms ITD, the app's own drum room) and exact pair loudness matching |
| `tools/patterns.mjs` | A/B musical content: the app's `drums-rock` lesson, a swing ride pattern, the `drums-fill-round` shape, a snare roll, the typical-stroke groove `soft()` and hit ladders |
| `tools/render-ab.mjs` → `out/render-ab.json` | writes every `drums-*-old/new.wav` |
| `tools/acceptance.mjs` → `out/acceptance.json` | the 26 acceptance tests of revision 2 (the EditMode test numbers, §11) |
| `tools/verify.mjs` → `out/verify.json`, `out/test-vectors.json` | objective checks and test vectors |
| `tools/checks.mjs` → `out/checks.json` | hat openness, pedal, sympathy and choke behaviour |
| `tools/bench.mjs` → `out/bench.json` | CPU, five schedules (§9) |
| `tools/calibrate.mjs` | kit balance → `LEVELS` |
| `tools/ladder-check.mjs`, `tools/groove-balance.mjs` | loudness vs u against the legacy kit; per-piece loudness in each groove, old/new |
| `tools/force.mjs` | contact probe (Fmax, rise, contact time first-to-last contact, number of contacts) |
| `tools/export-tables.mjs` → `out/kit-tables.json` | table data for the C# kit |
| `tools/tune.mjs`, `compare-hits.mjs`, `components.mjs`, `probe*.mjs` | tuning probes (onset-safe) |
| `critic/` | the independent critic's scripts (read-only here; they import the v2 engine directly) |

**Run** (in the drums lab): `node tools/render-ab.mjs && node tools/acceptance.mjs && node tools/verify.mjs && node tools/checks.mjs && node tools/bench.mjs && node tools/export-tables.mjs`
(about 3 minutes on the M5 Pro; the bench is most of it).

**Renders:** `drums-*.wav`, 22 files, 59.9 MB (57.1 MiB), plus a listening guide (section 13). The `-new` renders are in
`Tests/EditMode/Audio/Drums/Reference~/renders/`.

---------------------------------------------------------------------------------------------------------------------------

## 2. Unity architecture

### 2.1 Where it runs

- **`MusicRoom.Audio.Drums.DrumKitDsp`** is a new class. It owns the 8 objects in Burst-friendly SoA `NativeArray<float>`
  state and renders **one 256-frame block for all 8 pieces at once**: kick → snare → toms → hat → crash → ride.
  - The sub-block grid is 32 frames on absolute sample time.
  - Events are sample-accurate inside the block. Split the block at every event, as the reference `DrumKit.process` does.
    A membrane additionally splits its own segment where a waiting stroke starts (`renderSeg` → `renderPart`).
- **`DrumPieceProcessor : ISynthProcessor`**, one on each of the sources `Kick, Snare, Hat, Tom1, Tom2, Floor, Crash, Ride`
  (`ARCHITECTURE.md` §7.2).
  - The first processor called in a DSP block (compare `SynthSource.BlocksRendered` / the absolute sample) runs
    `DrumKitDsp.Render(blockStart, 256)`. Every processor then copies its piece's mono block into its mix.
  - All `OnAudioFilterRead` callbacks run on the one mixer thread, so no locks are needed. Assert the thread id in
    development builds.
  - If Unity ever calls the sources with different frame counts, render `max(frames)` and keep a ring of 2 blocks.
- **Room send.** Each piece's source keeps its `sendTarget = DrumRoom` with the current sends (kick .35, crash/ride .2,
  others .5) and the drum-room bus 0.3. The A/B renders used exactly these sends and the app's 0.45 s drum room.
- **Spatialisation is unchanged**: one HRTF source per piece. The engine's output per piece is mono. Kick = shell position.
- **The master is unchanged** (the master compressor at −14 dB). At the app's level the new grooves reach the compressor at the
  old kit's loudness within 0.5–2.1 dB and with similar gain reduction (5th percentile −2.4 vs −2.8 dB in rock, c15); the
  crest factor is ~2 dB higher. Keep `KitTrimDb` (start 0) as the one knob.
- **Legacy kit.** Keep `DrumSynth` behind `DrumEngine.UseModelled` (default `true`) for on-device A/B and for the spec-06
  null tests. Remove the legacy snare-wire noise voice from the modelled path: the physical sympathetic coupling replaces it.

### 2.2 Commands (main → audio)

Add `SynthOp.Custom` kinds, staged into the kit's own `SpscRing(256)`. The kit processes them at the command's exact
sample.

| Custom kind | from | fields |
|---|---|---|
| 3 `DrumStrike` | `IAudioEngine.Drum(kind, vel, strike, when)` | `Index = (int)DrumKind`, `Vel = u` (clamped .05..1), `Strike.Pos`, `Strike.Zone`, `Strike.Open`, `Strike.Openness`, `Strike.Implement` |
| 4 `DrumChoke` | `IAudioEngine.Choke(kind)` | `Index` (hat kinds map to Hat) |
| 5 `HatOpenness` | `IDrumsApi.SetHatOpen(open)` | `Level = 0` or `1`, or a continuous value (optional, below); a silent state change |

`Choke(Hat)` + `Drum(HatClose, .7)` (what the kit input sends when the pedal closes) keeps working. `HatClose` itself damps the
plates, so the choke is redundant but harmless.

The kit input collapses duplicates within 3 ms per piece and 45 ms per hand, so at most two strokes (two hands) overlap on one
drum: exactly the two contact slots. The pending stroke (§4.3) covers anything else.

### 2.3 Optional contract additions (backward compatible)

`DrumStrike` gains:
- `public byte Implement` — 0 Tip (default), 1 Shoulder, 2 Finger;
- `public float Openness` — 0 = use the hat state, 0 < x < 1 = half-open.

`DrumZone` appends `CrossStick = 6` (the existing values are unchanged).

Suggested Drums-module mapping:
- a stick's shoulder pointer → `Shoulder` (it already only strikes cymbals);
- the shoulder crossing the snare rim zone (rr > .9 r) → `Zone.CrossStick`;
- a fingertip pointer → `Finger`;
- the hat stand pinch → `Openness` 1. A continuous pinch → openness is optional.

Without these additions the kit still plays everything except cross-stick and half-open, using
`Tip` / `Shoulder` (crash: shoulder unless bell) / `Finger`, as the reference `DrumKit.apply` does.

**Settings switch (optional):** "Expressive dynamics" sets `DrumKit.stickCurve = cymCurve = STICK_V_EXPRESSIVE`
(`0.3·40^u`, the v1 curve: 27 dB of snare range instead of 20). Default off.

---------------------------------------------------------------------------------------------------------------------------

## 3. Velocity curves

The app's stroke value `u` (0.05..1) stays exactly as today, so lessons, recorder, `AudioTestTap` and tests are unchanged:
- sticks: `u = clamp(((vy − .3)/2.7)^.7, .05, 1)`, from tip speed in m/s;
- fingertips: v0/v1 = .12/1.5;
- nod kick: `u = clamp(.25 + .75(r − 1.6)/4.9, .25, 1)`, from the downward pitch rate in rad/s.

The audio maps `u` to a physical impact speed **V** (v2):

| implement | V(u) (m/s) | range | why |
|---|---|---|---|
| stick on a drum (tip / shoulder) | `max(0.4, 12·u^1.1)` | 0.4 … 12 | loudness vs u tracks the legacy kit within ~2 dB (table below), so ordinary VR strokes keep their level and hand-tracking velocity jitter is not amplified; the ends still reach a real pp and ff (Dahl 2011: tip speeds to ~12 m/s) |
| stick on hats and cymbals | `max(0.4, 12·u^0.9)` | 0.4 … 12 | VR cymbal swings are glancing, so the downward tip speed under-reads them; without it soft cymbal strokes sat 3 dB under the legacy kit |
| fingertip / hand | `max(0.3, 3.6·u^1.1)` | 0.3 … 3.6 | a hand cannot strike as fast as a stick |
| kick beater (nod) | `6.3·u^0.85` | 1.94 (u .25, the nod minimum) … 6.3 | a beater travels ~15 cm; the legacy kick's 12 dB range |
| hat pedal close | `0.15 + 0.85 u` | 0.15 … 1.0 | the top plate's closing speed |
| expressive (setting) | `0.3·40^u` | 0.36 … 12 | the v1 curve |

Loudness vs u, single dry hits (0.4 s K-weighted window, 1 s for cymbals), dB re the u .95 stroke (`tools/ladder-check.mjs`):

| piece | u .15 | .35 | .55 | .75 | .95 | range new / legacy |
|---|---|---|---|---|---|---|
| snare | −19.7 (legacy −18.1) | −12.0 (−10.2) | −7.6 (−5.7) | −3.6 (−2.5) | 0 | 19.7 / 18.1 |
| tom1 | −16.0 (−14.8) | −8.0 (−8.1) | −4.4 (−4.4) | −1.6 (−1.9) | 0 | 16.0 / 14.8 |
| floor | −15.3 (−14.9) | −7.9 (−8.1) | −4.3 (−4.4) | −1.9 (−1.9) | 0 | 15.3 / 14.9 |
| hat | −14.8 (−14.7) | −7.9 (−7.9) | −4.3 (−4.3) | −1.9 (−1.8) | 0 | 14.8 / 14.7 |
| crash | −16.9 (−15.4) | −9.2 (−8.5) | −5.1 (−4.7) | −2.2 (−2.1) | 0 | 16.9 / 15.4 |
| ride | −15.1 (−14.2) | −8.2 (−7.6) | −4.5 (−4.1) | −2.0 (−1.8) | 0 | 15.1 / 14.2 |
| kick (u .25 … 1) | −10.8 (−11.9) | −6.2 (−6.9) | −3.3 (−3.8) | −1.3 (−1.4) | 0 | 10.8 / 11.9 |

Snare by VR tip speed (`out/verify.json` `velocityCurves`; 0.4 s K-weighted loudness, onset-safe centroid, force probe):

| VR tip speed (m/s) | 0.35 | 0.5 | 0.8 | 1.2 | 1.8 | 2.4 | 3.0 |
|---|---|---|---|---|---|---|---|
| u | .061 | .162 | .307 | .463 | .663 | .839 | 1 |
| V (m/s) | 0.56 | 1.62 | 3.28 | 5.15 | 7.63 | 9.89 | 12.0 |
| snare loudness (dB, rel.) | −43.6 | −34.1 | −28.4 | −24.4 | −20.5 | −16.8 | −14.6 |
| first-50 ms centroid (Hz) | 1990 | 2091 | 2145 | 2298 | 2582 | 2801 | 2971 |
| contact time (ms) | 5.1 | 5.0 | 4.8 | 4.5 | 4.2 | 4.0 | 3.8 |
| peak force (N) | 3.5 | 10.3 | 22.1 | 37.4 | 60.1 | 83.2 | 106 |

| nod (rad/s) | 1.7 | 2.5 | 3.5 | 4.5 | 5.5 | 6.5 |
|---|---|---|---|---|---|---|
| u | .265 | .388 | .541 | .694 | .847 | 1 |
| beater V (m/s) | 2.04 | 2.82 | 3.74 | 4.62 | 5.47 | 6.30 |
| kick loudness (dB, rel.) | −22.7 | −19.8 | −17.1 | −15.2 | −13.6 | −12.4 |

---------------------------------------------------------------------------------------------------------------------------

## 4. Membrane drums (kick, snare, tom1, tom2, floor)

### 4.1 The modal bank: state, recurrence, output (v2)

Each mode k has a complex state z = (zr, zi) and a pole p = r·e^{iω}: ω = 2πf/sr, r = exp(−6.907755/(T60·sr)).
`zi` is the modal displacement q (m); `zr` is the velocity divided by ω.

The input is applied **before** the rotation (this semi-implicit form keeps the stiff stick contact stable). The output
taps the state **before** the update and adds a c₀-weighted feed-through of the input:
```
out += gOut·zr                     // the state BEFORE this sample's update (v1 used the state after it)
aux += psi·zi                      // snare only: snare-side-head displacement at the wire line (before the update)
x   = zr + Σ_inputs g·input − w0·σ·zi     // inputs: stick force(s), sympathetic force, stochastic force; σ: §4.4
zr' = cr·x − ci·zi ;  zi' = ci·x + cr·zi  // cr = r cos ω, ci = r sin ω (times the per-sub-block decay factor, §4.6)
out += Σ_inputs (Σ_k gOut_k·g_k·c0_k)·input      // one scalar per input and sample: the G_ff sums
c0_k = 1 − Re(1/(1 − p_k)) = 1 − (1 − pr)/((1 − pr)² + pi²)                  (Bank.ffk)
```
c0 is ½ for a lightly damped mode. For a heavily damped low mode it is smaller (even negative), because it also cancels the
DC that the complex pole's damping adds to the "velocity" state (the complex one-pole lacks the −q/τ part of a damped
oscillator's velocity): the kick's 56 Hz mode (T60 0.42 s, Q ≈ 11) has c0 = −5.86. Per piece: kick −5.86…0.48, snare
−0.64…0.49, toms −0.53…0.49, crash 0.43…0.50, ride and hat 0.47…0.50. Use the exported values; do not hard-code ½.
The impulse response per mode is then `[c0, Re p, Re p², …]·g`: its DC gain is exactly zero (a velocity output must have
none: the impulse moves the head and the head comes back) and, for a lightly damped mode (c0 ≈ ½), it has no constant
high-frequency floor (the velocity jump counts half at its own sample, as a band-limited step does). For the heavily damped
low modes the c0 term is a scaled copy of the input; that is harmless for the smooth stick force and the 900 Hz low-passed
sympathetic force; the stochastic force (§4.6, §5.3) drives only the dense and plate modes, whose c0 is 0.38–0.50 (a
floor of at most a quarter of the ½ that the critic's form would add), and it is low-passed at 4 kHz. v1 tapped Re(z) after the update with no feed-through: DC gain
−½·g per mode, i.e. a negative copy of the 5–9 ms contact pulse under every hit, the "woof" the critic measured. The
critic's suggested patch (post-update tap + ½·input) also removes the DC but doubles the high-frequency floor, which turns
the stochastic forcing of §4.6 into hiss; do not use it.

The G_ff sums are per input: `S.GffL` (listed modes) and `S.GffD` (dense) per stick slot, computed at the strike;
`ffSymL` / `ffSymD` for the sympathetic force (constant); `ffN` for the stochastic force (per sub-block); body and stick
banks likewise (`ffBodyH`, `ffBodyR`, `ffStick`). The tables carry `c0` per mode.

The input gain for a velocity impulse is `gIn = dt·φ(r,θ)/(M·2πf)`, because Δq̇ = φ·F·dt/M. The modal mass is
`M = σ·πa²·J_{m+1}(j_mn)²·ε_m` (ε = 1 for m = 0, ½ otherwise).

### 4.2 Mode list (all derived tables are in `kit-tables.json` `pieces.<drum>.modes`)

1. **Low modes** `DRUMS.<drum>.low`, the coupled batter/resonant pairs `{m, n, f, t60, pb, pr, rad}` (unchanged from v1).
   - m ≥ 1 entries become two orientations (cos, sin), split by `split·(0.6 + 0.8·rng)`; the second's T60 is ×0.93.
   - Snare values: **Rossing 1992 (Physics Today 45(3), Fig. 5)**: 183 Hz (0,1) heads in phase; 275 (1,1) opposite; 344
     (1,1) in phase; 405 (2,1) batter; 451 (0,2) batter; 595 (0,2) snare head; plus 330 Hz (0,1) out of phase (Fig. 4).
   - Toms and kick: (0,1) pair at f01 and ×1.39; (1,1) pair at ×1.50 and ×1.62; (2,1) ×2.04; (0,2) ×2.37 (Rossing's
     air-loading trend). Tunings: tom1 165 Hz, tom2 131 Hz, floor 98 Hz, kick 56 Hz.
2. **Ideal Bessel continuation.** Every (m ≤ 24, n ≤ 8) zero j_mn not in `low` gets `f = fIdeal·(j_mn/2.404826)·(1 +
   0.012·rng.bip())`, up to `maxHz`, `T60 = max(min, at·(f/ref)^−exp)`, `pr = 0.35·rng.bip()`, `rad = 0.9` (m = 0) or
   `dipole`. RNG draws are unchanged from v1. **v2:** after generation the continuation is **sorted by frequency** (so the
   modes that take the sympathetic force are one range, §6.2), and a drum with `maxR` (the kick, `maxR = 0.22`: the beater
   only lands near the centre) drops continuation modes with `max_{r≤maxR}|J_m(j r)| < 1e-3`, which can never be excited
   above −60 dB (38 modes).
3. **Dense field.** `dense.n` modes, log-spaced `dense.from`…`dense.to`, T60 `t60From`…`t60To`, random radial profile
   `sin(π(1 − r)(1 + 3·rshape))·√2·√(1 − r²/2)`, random sign, `M = σA/(2π·max(1, j/π))`. They are the modes too many to
   list (the crack). They have φ = 0 in the contact sum (they are part of the residual, §4.4) but take the stick force with
   gain ×`dense.drive`, and the stochastic force of §4.6.

| drum | head modes | nLow | listed (nMembrane) | dense | sym modes (nSym) | dense.drive (v1) |
|---|---|---|---|---|---|---|
| kick | 124 | 9 | 94 | 30 | 94 (no sympathy input) | 1.2 (4.0) |
| snare | 95 | 10 | 59 | 36 | 95 (all) | 1.6 (2.0) |
| tom1 | 105 | 9 | 77 | 28 | 22 | 0.6 (2.5) |
| tom2 | 133 | 9 | 105 | 28 | 35 | 0.7 (2.5) |
| floor | 160 | 9 | 130 | 30 | 62 | 1.2 (2.5) |

Plus body (4–7 modes), stick (4) and wires (4 lumped groups, snare only). The dense drives are lower than v1 because the
stiffened contact (§4.4) is sharper; measured onset-safe at the same impact speed, v2's toms are darker above 1 kHz than
v1's (tom1 2 kHz octave −7 vs 0 dB re max) and the kick's beater click is 8–10 dB lower (1 k / 2 k octaves −18/−23 vs
−10/−12 dB).

**Radiation weight:** `gOut = radK·(f/200)^γ`, `radK = rad` for m = 0, else `rad·(0.35 + 0.65·(ka)²/(1 + (ka)²))`,
ka = (2πf/343)·a. γ = 1 (kick 0.7). **Stretch weight** (glide): `eW = j²·M·1e3`. **Energy weight**: `eM = M·ω²`.

### 4.3 The strike and the two stick slots (`Membrane.strike`, `startStroke`)

Each membrane has `nSlots` contact slots (`Slot`: the snare and toms 2, the kick 1) and one pending stroke.
- **Slot choice:** a free slot; else a slot whose stick has left the head and is only in flight (§4.4); else the stroke
  becomes **pending** and starts on the sample a stick leaves the head (at most a few ms later: inaudible as timing). Two
  pending strokes merge: V = √(V₁² + V₂²).
- Per stroke, from the piece RNG, in this order (unchanged): θ = 2π·R; r += 0.02·(2R − 1) clamped [0, .97]; V ×= 1 + 0.03·
  (2R − 1); K ×= 1 + 0.08·(2R − 1); stick gain ×= 0.8 + 0.4R.
- **Per-slot state:** `phi[]` and `gIn[]` (whole bank), `on, touch, started, xs, vs, m, K, alpha, lam, t, fl, dPrev, xw,
  tauW, lk, Cw, Z, G, GffL, GffD, ysP, F, stickGain, bodyDrive, rimZone`.
- **At the strike:** `G = Σ_{k<nM} φ_k·gIn_k·ci_k` (the head's one-step compliance at the tip); if the other slot is on,
  the cross compliance `Gx = Σ φ_s·gIn_t·ci` and the separation weight `sepW = max(0, 1 − |Gx|/√(G_s G_t))` (0 = the two
  sticks on the same spot); `xs` = the head displacement at the tip; `vs = V`; `lk = e^{−dt/τ}`, `Cw = τ/Z·(1 − lk)` with
  τ = Z·Cres (residual, §4.4); `ysP` = the next-step prediction (`predict`). A stroke sets every mode active (§4.9).

Shape at the strike point (65-point radial tables `shape65`, linear interpolation):

| mode | φ |
|---|---|
| m = 0 | `pb·J0(j r)` |
| m ≥ 1 | `pb·J_m(j r)·(cos mθ | sin mθ)` |
| dense | `pb·shape(r)·sg·(0.75 + 0.25·cos(mθ + ph))` (φ = 0 in the contact sum, gIn kept ×`dense.drive`) |

Zones:

| zone | head share of stick mass | rim impulse J | rim contact | head strike radius |
|---|---|---|---|---|
| head | 1 | 0 | — | r |
| rim (rimshot) | 0.6 | 0.4·m·V·1.7·(0.016/m_stick) | half-sine 0.3 ms | 0.92 |
| xstick (cross-stick) | 0.2 | 0.25·0.03·0.6·V·1.6 | half-sine 0.35 ms | 0.35, stick speed ×0.25 |

- Rim impulse force `F = J/(L·dt)·(π/2)·sin(π(i + .5)/L)`, **added into a 64-entry ring** (`rimF` into the hoop/body,
  `rimS = F·stickGain` into the stick modes), so overlapping rim strokes add instead of cutting each other off.
- Body drive per slot: `bodyFromHead·(1 + 6r⁴)` per newton. The body bank has two inputs: head-zone forces through
  `gInHead = dt/(0.03·2πf)·(0.15·hoop + 0.85)`, rim/cross-stick forces and the rim ring through
  `gInRim = dt/(0.03·2πf)·(0.85·hoop + 0.15)`.
- Stick modes: input = Σ_slots F·stickGain + rim ring, gain `dt/(0.012·2πf)`. stickGain = implement.stick ×
  (xstick .28 | rim .13 | head 1); the kick has 0.

### 4.4 The stick–head contact (sample-outer while a stick is on)

The stick is a mass m on a Hunt–Crossley spring F = K·δ^α·(1 + λ·δ̇). The head under the tip is the listed modes **plus the
residual** of the unlisted ones: a dashpot `Z = 2πb√(Tσ)` relaxing into the spring `Cres = ln(c/(2π·maxHz·b))/(2πT)`
(τ = Z·Cres = 39–69 µs), T = σc², c = 2π·fIdeal·a/2.404826.

**Head stiffening (v2).** A stick pushing the head down makes a cone; stretching it raises the tension. The relative
tension rise is `σ = (y/y2)²` for a cone of apex deflection y, with `y2 = a·√(2T/(Eh·c))` (Eh = Young's modulus × film
thickness, 1.1e6 N/m for 10-mil PET; c = shape factor). A tension rise of σ raises every listed mode's stiffness by (1 + σ),
which enters the update as an internal force: `x = … − w0_k·σ·zi_k` (w0 = ω·dt). σ is computed every sample from the solved
tip displacement(s) and used on the next sample; capped at 6. Two sticks: `σ = (max(y₀², y₁²) + sepW·min(y₀², y₁²))/y2²`.

| | m (kg) | K (N/m^α) | α | λ (s/m) | b (m) | stiffening c → y2 |
|---|---|---|---|---|---|---|
| tip (snare, toms) | 0.011 | 3.0e7 | 1.5 | 0.25 | 0.004 | snare c 4 → 6.4 mm; toms c 1.5 → 4.8 / 5.5 / 7.3 mm |
| shoulder | ×1.6 | ×0.6 | 1.5 | ×1 | ×2 | |
| finger | ×2.6 | ×0.004 | 2.0 | ×3 | ×3 | |
| kick felt beater | 0.04 | 8.0e9 | 2.5 | 0.4 | 0.015 | c 0.25 → 19.7 mm |

- **m = 11 g**: the effective tip mass of a ~50 g stick (Wagner's sticks: 53 and 56 g) rotating about a fulcrum ~1/3 from the
  butt is ≈ 0.25 of its mass. v1 used 16 g.
- **c** was tuned so that contact times and peak forces match Wagner 2006 (3–6 ms, shorter at forte; ~3 N piano to
  >100 N forte at the centre) and Dahl 2011 (5–8 ms mf on toms). A shape factor > 1 is plausible because a point-loaded
  membrane's strain concentrates near the load.

Per sample (`contactLoop`, both slots; a slot not on is skipped):
```
for each slot s on:  xwP = xw·lk;  xsP = xs + vs·dt;  d0 = xsP − ysP − xwP;  C = dt²/m + G + Cw
touching_s = d0_s > 0;   KD_s = K·max(0, 1 + λ(d0 − dPrev)/dt)
Newton, 6 iterations from F = 0 (2×2 when both touch, Cx = Gx; else 1-D):
   d_s = max(0, d0_s − C_s F_s − Cx F_t);   g_s = F_s − KD_s·d_s^α;   ∂g_s/∂F_s = 1 + KD_s α d_s^(α−1) C_s,  ∂g_s/∂F_t = KD_s α d_s^(α−1) Cx
   F ← F − J⁻¹g, clamped to [0, d0_s/C_s]
advance each slot: xw = xwP + Cw F; vs −= F/m·dt; xs = xsP − F dt²/m; dPrev = d; t++
   touching → started, touch, fl = 0;  else if started → touch = false, and if vs < 0: ++fl > FLIGHT → off (re-contact allowed)
   t > CONTACT_MAX → off
fused pass over the listed modes k < nMembrane (ONE pass: update, output, wires, next-step predictions at both tips):
   a = zr, b = zi;  out += gOut·a;  w += psi·b
   x = a + gIn0·F0 + gIn1·F1 + gSym·u − w0·σ·b;   zr = cr·x − ci·b;  zi = ci·x + cr·b
   A_s += φ_s·(ci·zr + cr·zi);   B_s += φ_s·ci·w0·zi          (per slot that is on)
H = out + GffL0·F0 + GffL1·F1 + ffSymL·u;   F0, F1 stored per sample (dense, body, stick drives below)
σ_next from the solved tip displacements y_s = ysP_s + G_s F_s + Cx F_t (touching slots)
ysP_s = A_s − σ_next·B_s
if a stroke is pending and a stick left the head: end the segment here (the pending stroke starts on the next sample)
```
The contact starts at xs = current ys and vs = V. Re-contacts (Wagner's double contacts on loose heads at forte) are
allowed during the flight window `FLIGHT = 4 ms`. The Newton solve for α = 1.5 uses d·√d, for 2 uses d², for 2.5 uses d²·√d.
After the loop the dense modes are driven mode-outer with F0[i]·gIn0 + F1[i]·gIn1 (they have no feedback on the contact).

Measured (`tools/force.mjs`, r .1, contact = first to last sample with F > 0):

| | V 0.4 | 2 | 5 | 10.5 m/s |
|---|---|---|---|---|
| snare Fmax (N) / contact (ms) | 2.5 / 5.15 | 12.7 / 4.96 | 37.0 / 4.52 | 88.4 / 3.96 |
| tom1 | 1.6 / 7.29 | 9.8 / 6.44 | 31.8 / 5.27 | 84.6 / 4.40 |
| tom2 | 1.5 / 8.02 | 8.9 / 7.19 | 28.6 / 5.94 | 75.7 / 4.83 |
| floor | 1.5 / 8.98 | 8.2 / 8.38 | 24.9 / 7.15 | 65.0 / 5.96 |
| kick (V 1.25 / 3.2 / 6.3) | 7.7 / 18.4 | 24.3 / 16.4 | 58.7 / 13.9 | |

### 4.5 Tension modulation (pitch glide)

Once per sub-block, and **not while a stick is on**:
- `E = Σ_{k<nLow} eW_k |z_k|²`;
- `s = min(1 + 1.2·glide, √(1 + κE))`;
- every head pole's base becomes `cr0 = r cos(ω0 s)`, `ci0 = r sin(ω0 s)` (then ×the drain factor of §4.6).

κ is calibrated so that the free vibration right after each drum's hardest stroke gives `glide` (`kit-tables.json`
`kappa`). Physics (Rossing 1992): ΔT ∝ amplitude², f ∝ √(T + ΔT), a full bass-drum blow ~10 % sharp; Wagner 2006: the
(0,1) mode +3 Hz (+37 cents) at forte. **v2 maxima** (the new velocity curve puts ordinary strokes at higher V, so the tom
maxima were lowered): kick .055, floor .05, tom2 .04, tom1 .035, snare .022.

Glide factor of the engine (cents; peak right after the contact / at 30 ms; `glideS`, stroke at r .08):

| | u .3 | u .5 | u .7 | u 1 |
|---|---|---|---|---|
| kick | 10 / 8 | 26 / 18 | 47 / 32 | 87 / 57 |
| floor | 12 / 8 | 29 / 21 | 50 / 35 | 79 / 54 |
| tom1 | 7 / 4 | 20 / 11 | 34 / 20 | 60 / 34 |
| snare | 4 / 1 | 10 / 3 | 18 / 4 | 30 / 7 |

(`verify.json` `glide` measures early 8–48 ms against late 250–650 ms spectral peaks; with a 40 ms window that estimate is
±20 cents on the floor tom and kick.) The glide decays in ~100 ms: natural, not a synth "pew".

### 4.6 Nonlinear spreading (hard strokes are brighter), smooth (v2)

Once per sub-block, not while a stick is on:
- `E = Σ_{k<nMembrane} eM_k|z_k|² / EmRef` (EmRef = the whole-stroke energy maximum of the hardest stroke);
- `frac = min(0.3, κ_nl·E^ν·SUB/sr)`;
- **drain:** for the next sub-block the listed modes' poles are multiplied by `(1 − frac)^(1/(2·SUB))` per sample
  (`Bank.poles`: `cr = cr0·mul`, `ci = ci0·mul`): the energy falls by `frac` over the sub-block, smoothly;
- **inject:** the dense modes get a stochastic force over the next sub-block: input `n[i]` (low-passed noise, below) with
  per-mode gain `gN_k = √(ΔE_k/(eM_k·SUB·⅓))·injC_k`, `ΔE_k = frac·E·EmRef·share_k`, share ∝ 1/f normalised. The expected
  energy injected equals the drained energy.
- **The noise** is xorshift uniform through a one-pole low-pass at `INJ_LP = 4 kHz` (`Noise.fillLP`); `injC_k =
  √((1 − 2β cos ω_k + β²)/α²)` (α = 1 − e^{−2π·4000/sr}, β = 1 − α) compensates its spectrum at each mode. A white force
  would add a mass-line hiss floor; nonlinear forcing lives at the combination frequencies of the low modes.

| | κ_nl | ν |
|---|---|---|
| snare | 14 | 1.0 |
| toms | 6 | 1.2 |
| kick | 4 | 1.2 |

v1 applied the drain as a state scale and the injection as random-phase state kicks on the 32-sample grid: a 1.5 kHz
periodic structure (critic c5/c5b). v2's grid excess: kick 13.0 → 1.3 dB, floor 9.6 → 0.5 dB, snare 2.4 → 1.3 dB.

### 4.7 Body (shell + hoop) and stick

`DRUMS.<drum>.body` lists `{f, t60, g, hoop}` (maple shell plus die-cast or steel hoop modes, ×(1 ± 1 %)): gOut = g·f/200;
inputs: §4.3. Stick modes: 5A hickory free-free bending (332·{1, 2.76, 5.40, 8.93} Hz; the hand damps the fundamental):
**916, 1795, 2965, 4430 Hz**, T60 .05/.04/.03/.02 s, g 1/.8/.55/.35, ×(1 ± 2 %).

Piece mix (pre-level): `out = gHead·head + gBody·body + gStick·stick + gWires·wires`:

| | head | body | stick | wires |
|---|---|---|---|---|
| snare | 1 | 4.8 | 2.5 | 0.0075 |
| toms | 1 | 4.8 | 2.5 | — |
| kick | 1 | 4.8 | 0 | — |

Component balance, K-weighted, u .8 (`tools/components.mjs`): snare centre: head −19.9, wires −20.8, body −42.4, stick
−42.6 dB; rimshot: body (hoop) −22.6, head −25.8, wires −24.6, stick −33.6; cross-stick: body −29.6, stick −31.1, wires
−34.8, head −35.1; tom1: head −15.3, body −34.2, stick −40.3; tom1 rimshot: head −17.3, body −17.5.

### 4.8 Snare wires (`Wires`)

Unchanged from v1. Four lumped groups, fw = 150/172/196/223 Hz, preload 4 µm, ζ = 0.12, one-sided contact spring
ωc = 2π·2600 against the snare-side head displacement h = Σψ·zi (ψ = pr):
```
pen = h − w;  ac = pen > 0 ? ωc²·pen : 0
a = −ωw²(w + pre) − 2ζωw·v + ac;   v += a dt;  w += v dt
ex += |ac − acPrev|                                   // collisions
vh = Δh/dt;  ah = Δvh/dt                              // head acceleration (strand micro-rattle, linear)
drive = ex·2.5e-6 + |ah|·0.06e-3
sm += (drive − sm)·(1 − e^{−1/(0.0003·sr)})
n = noise()·sm
y = lp9500(pk5400(+3 dB, Q1.2)(pk2900(+4 dB, Q.9)(hp900(Q.6)(hp1000(Q.7)(n)))))  +  0.5·Σ bp{4400, 5900, 7700}(Q5)(n)
```
For drums whose sympathetic input is limited to low modes (§6.2), `W += Cqs·sym` adds the quasi-static deflection of the
modes above the limit (`Cqs = Σ psi·gSym/(dt·2πf)`, exported); the snare takes the force on all modes, so its Cqs = 0.

### 4.9 Idle and energy culling (v2)

- **Flush** (every sub-block): states with |z|² < 1e-30 are zeroed (no subnormals; the critic counted 604/min in v1, now 0).
- **Cull** (every sub-block, no stick on): a mode whose output power `gOut²|z|²` is below `CULL/(level·gHead)²`
  (`CULL = 1e-12`, i.e. −120 dBFS after the level) and that receives no input is zeroed and marked inactive (`Bank.act`);
  the kernels skip it. Modes that take the sympathetic force are not culled while it is on (and are re-activated when it
  comes on); a stroke or an injection re-activates.
- **Idle:** no stick, no pending, no rim pulse, `Σ gOut|z|² < 1e-22` and quiet wires for 8 sub-blocks: reset and skip the
  piece until it is struck or woken by sympathetic force above `SYM_WAKE = 1e-7` N.

---------------------------------------------------------------------------------------------------------------------------

## 5. Cymbals (crash, ride, hat)

### 5.1 Plate modes (`kit-tables.json` `pieces.<cym>.modes`, fixed identity per cymbal), v2 layout

- **Dome:** `nDome` modes, `f = fLo + (fKnee − fLo)·u^dExp`, u = (i + .15 + .7R)/nDome: the sparse low (m,0) modes.
- **Plate:** a **constant density** `dens` (modes/Hz) from `fKnee` to `fTop`, `f = fKnee + (fTop − fKnee)·(i + .15 + .7R)/N`.
  A flat plate's modal density (A/2)·√(ρh/D) is constant: 0.05–0.10 /Hz for these cymbals; Wilbur & Rossing observed ~300
  modes in a 16" cymbal.
- **Noise bands** above `fTop` to 19.5 kHz (`C.noise`): the modes too dense to list, at the same density (`noiseDens`),
  fed by the strike and by the cascade (§5.3), T60 from the same law at the band centre.
- **Labels** (unchanged): nodal diameters `m = max(2, round(2 + 1.4√(f/fLo)(0.8 + 0.4R)))`, nodal circles n ∈ {1..3} with
  probability min(.7, f/4000), phase 2πR, amplitude e^{0.35(2R−1)}.
- **Bell subset:** `bell.f × ratios` ×(1 ± .6 %), own T60s, gain `bell.g`, modal mass `BELL_MASS = 0.4` × the plate's (the
  bell is small and thick; v1's bell strokes sat 4–9 dB under the legacy kit's).
- **T60** `max(min, at·(f/ref)^−exp·(0.85 + 0.3R))` (noise bands without the random factor).
- **Radial participation** (unchanged): bell modes `(1 − r/rb)^.5` inside the bell, `0.12e^{−4(r−rb)}` outside; plate modes
  inside the bell `0.25 r/rb`; outside, x = (r − rb)/(1 − rb): n = 0 → `0.25 + 0.75·x^{0.45·min(m,6)}`; n ≥ 1 →
  `1.1·sin((n + .5)πx)·√x`. Angular factor `cos(m(θ − ph))`.
- **Modal mass** `M = ρh·πR²/4`, ρ = 8600. `gIn = dt·φ/(M·2πf)`. `gOut = a·(f/1000)^γ` (γLow below 1 kHz), ×`bell.g`.

| | R (m) | h (m) | dome + plate + bell | fLo / fKnee / fTop (Hz) | dens (/Hz) | T60 at/ref/exp/min | γ / γLow | Z (N·s/m) | noise bands (Hz) |
|---|---|---|---|---|---|---|---|---|---|
| crash 18" thin | .2286 | .0008 | 36 + 120 + 6 | 165 / 1000 / 3000 | .06 | 3.6/300/.28/.35 (v1 6.0/300/.62) | 2.0 / 2.5 | 50 | 3.0–3.8, –4.8, –6.0, –7.5, –9.4, –12, –15.5, –19.5 k |
| ride 20" medium | .254 | .0012 | 44 + 138 + 7 | 210 / 1100 / 3400 | .06 | 6.0/300/.30/.45 (v1 11/300/.70) | 1.8 / 2.3 | 85 | 3.4–4.3, –5.4, –6.8, –8.6, –11, –14.5, –19.5 k |
| hat 14" | .178 | .0009 | 28 + 140 + 4 | 380 / 1200 / 4000 | .05 | open 2.2/500/.2/.25; closed .20/500/.5/.03 | 2.15 | 60 | 4–5, –6.3, –8, –10, –12.5, –15.5, –19.5 k |

| bell | f (Hz) | ratios | T60 (s) | gain (v1) |
|---|---|---|---|---|
| crash | 1050 | 1, 2.06, 3.21, 4.42, 5.7, 7.02 | 3.2 … 1.1 | 1.6 (0.8) |
| ride | 920 | 1, 2.03, 3.17, 4.35, 5.62, 6.98, 8.4 | 6.5 … 1.9 | 2.2 (1.0) |
| hat | 980 | 1, 2.1, 3.3, 4.6 | 1.2 … .6 | .5 |

Explicit cascade bands (`edges`, the last ends at fTop): crash 0–420–900–1500–2200–3000 Hz; ride 0–450–950–1600–2400–
3400; hat 0–900–1800–2800–4000.

### 5.2 Stick on plate: the contact pulse (precomputed per hit, ≤ 256 samples)

Unchanged physics: a Hertz spring (K = 1.0e9 N/m^1.5 ×(tip 1 | shoulder .35 | finger .002) ×(1 ± 8 %)) on the plate's
resistive point impedance Z, stick mass 0.016 × implement m, 8 substeps per sample, F averaged per output sample.
**v2:** the pulse is **added into a 512-entry ring** (`pring`) read one sample per output sample, so a stroke during another
stroke's pulse adds instead of truncating it; `Gff = Σ gOut·gIn·c0` per strike. The noise bands are excited directly:
`e_b += N_v·½·(dt/M)²·|F̂(ω_b)|²·φrms²/Eref`, N_v = dens × bandwidth, F̂ the pulse's DFT at the band centre, φrms .6
(.15 in the bell). Measured in the first 50 ms this gives the same spectral density per Hz as the explicit modes below fTop
(24–26 dB/Hz on both sides of the hat's 4 kHz boundary).

### 5.3 Energy cascade (the wash builds after the strike), smooth (v2)

Once per sub-block, `dtc = SUB/sr`:
```
E_b = Σ_{k∈b} ω_k²|z_k|² / Eref            (Eref = plate energy after a 10 m/s edge stroke)
frac_b = min(0.5, κ·E_b^ν·dtc);  if frac_b < 0.05·bandLoss_b: no transfer (cut-off: < 5 % of the band's own decay)
keep_b = 1 − frac_b;   inj[b+1] += up1·frac_b·E_b;   inj[b+2] += up2·frac_b·E_b
hat clash (below): keep_0, keep_1 ×= (1 − f);  inj[nE + j] += share_j·f·E_low   (clashTo)
poles for the next sub-block: cr = cr0·keep_b^(1/(2·SUB))·(choke ? chokeF_k : 1), same for ci      (drain + choke, per sample)
injection into explicit band b ≥ 1: a stochastic force over the next sub-block, gN_k = √(2·inj_b·Eref/N_b/(eW_k·SUB·⅓))·injC_k,
   applied to a random HALF of the band's 4-mode lane groups each sub-block (one LCG bit per group): same expected energy,
   half the cost
noise bands: e_j += inj[nE + j];  j → j+1 by rate κN·e_j^ν, upN of it arrives;  e *= exp(−13.8155·dtc/T60_j)
noise-band gain: target = g·√(e·Eref)/ω_c·(1/√2)·norm·(1 + sh); the gain RAMPS per sample to its target by the next grid point
```
`bandLoss_b = 1 − exp(−13.8155·dtc/T60(band centre))`. The stochastic force is the same low-passed noise as §4.6 (its own
xorshift, `injNoise`).

| | κ | ν | up1 | up2 | κN | upN |
|---|---|---|---|---|---|---|
| crash | 20 | .42 | .66 | .14 | 8 | .85 |
| ride | 6 | .42 | .66 | .14 | 2.4 | .85 |
| hat | 12 | .5 | .62 | .15 | 6 | .85 |

Rationale: the energy cascade from low to high modes in thin plates struck hard is wave turbulence (Touzé, Ducceschi,
Bilbao); its rate grows with amplitude. Rossing describes a cymbal sound whose low strike modes develop a high-frequency
"shimmer" that builds after the strike.

Measured (crash edge, band level per sample in windows 0–10 / 10–30 / 30–100 / 100–300 / 300–1000 ms, dB re the maximum,
`verify.json` `cymbalEvolution`):
- u 1: 2.8–5.6 kHz −14 / −11 / −5 / 0 / −2; 5.6–11 kHz −13 / −10 / −6 / −5 / −11: the wash builds 8–14 dB over
  100–300 ms;
- 177–354 Hz −29 / −30 / −26 / −27 / −33: the low end never dominates;
- the first-20 ms centroid falls from 5.8 kHz to 3.2 kHz at 1 s (u 1) and to 2.7 kHz (u .5): hard strokes stay brighter.

The legacy crash is a static square-wave/noise burst.

### 5.4 Hand choke (crash, ride, hat), smooth (v2)

For 0.3 s: every mode's pole gets the per-sample factor `chokeF_k = exp(−1/(τ_k·sr))`, `τ_k = 0.035·(f/500)^−0.25` s
(v1 scaled the states every sub-block); noise bands `e *= exp(−2dtc/0.02)` per sub-block (gain ramps); plus the skin on
metal: noise → lp 1100 Hz (Q .7), envelope `e^{−t/12 ms}(1 − e^{−t/2 ms})`, 50 ms, gain 3e-5 (pre-level).

Measured (`out/checks.json`, crash u .8): −10.7 dB at 20 ms, −21.7 at 50 ms, −55 at 150 ms; click score 0.96 (v1 1.4). A
new strike cancels the choke.

### 5.5 Hi-hat: openness, clash, pedal

**Openness** h ∈ [0, 1] (unchanged): each mode's T60 = `exp((1 − s)·ln T60closed + s·ln T60open)` with s = h^0.6, applied as
`setT60` (pole radius); noise bands likewise between `t60c` and `t60`; the gap is `0.012·h²` m; h ramps toward its target
in 4 ms when closing and 50 ms when opening. `Hat` (closed stroke) forces h = 0; `HatOpen` uses `Strike.Openness`
(0 < o < 1) if given, else the current target, else 1.

**Clash** (the bottom plate): `amp = 0.01·√(E_0 + E_1)` m; `over = max(0, amp − gap)/1 mm`; `f = min(0.4, 400·over·dtc)`;
bands 0–1 drain by (1 − f); **v2:** the sizzle goes by frequency, `clashTo = [[1, .2], [2, .3], [3, .3], [4, .15]]` = noise
bands 5–6.3, 6.3–8, 8–10, 10–12.5 kHz.

**Pedal chick** (`HatClose`, u): target h = 0; a plate-on-plate pulse (m .25 kg, K 2e8, at r .97, coupling **1.6** (v1 0.4),
V = 0.15 + 0.85u); noise bands ×0.2 (ramped); an air puff: noise → bp 1300 Hz Q .9, `e^{−t/6 ms}(1 − e^{−t/0.8 ms})`,
30 ms, gain **2.4e-4**·V (v1 6e-5). v1's chick sat 14 dB under the legacy kit's in the jazz groove; now level.

Measured (`out/checks.json`, u .7):

| | T60 4 kHz | T60 8 kHz | energy above 8 kHz |
|---|---|---|---|
| closed | .07 s | .05 s | 36 % |
| half (.3) | .28 s | .26 s | 48 % |
| open | 1.52 s | 1.29 s | 49 % |

A pedal close on an open hat drops it 76 dB within 150 ms. Open hat 0.3–1 s: the 20 strongest lines hold 41 % of the
2–5 kHz energy (v1 73 %, legacy 84 %), 12 % of 5–9 kHz.

---------------------------------------------------------------------------------------------------------------------------

## 6. The kit

### 6.1 Zone mapping (`DrumKit.apply`; `pos` = rr/r from the stick contact)

- **kick**: beater at r = 0.12 + 0.1(pos − .2), V = BEATER_V.
- **snare/toms**: V = finger ? FINGER_V : stickCurve; zone rim (rr > .9r) → rimshot; xstick → cross-stick; else head at r = pos.
- **hat**: V = finger ? FINGER_V : cymCurve; r = edge ? .96 (shoulder) : .55 + .3(pos − .5).
- **crash/ride**: V = finger ? FINGER_V : cymCurve; bell → r = min(.15, pos); edge → max(.9, pos); bow → clamp(pos, .3, .85);
  the crash uses the stick shoulder except on the bell.

### 6.2 Sympathetic coupling (air in the room and the shared stands)

Each source piece's output (post-level) goes through a one-pole low-pass at 900 Hz and is delayed **64 samples
(1.33 ms ≈ 0.45 m of air)**. Scaled by `symScale·gain` (**symScale 0.21**, v1 0.09) it is applied as force through
`gSym = dt·pr/(M·2πf)`.
- **v2: frequency limit.** The air couples the drums at low frequency only: on the toms only modes below `SYM_HZ = 700 Hz`
  take the force (a contiguous range after the sort of §4.2; 22/35/62 modes), and their quasi-static remainder is `Cqs`
  (§4.8). The snare takes it on every mode (`symHz` 1e9), because its wires feel the whole head's motion.
- **v2: gate.** Per segment, if the largest sympathetic force into a drum is below `SYM_WAKE = 1e-7` N it is ignored (a
  cheaper kernel), and an idle drum stays asleep.

| from → to | gain (v1) |
|---|---|
| kick → snare | 1.0 |
| tom1 → snare | .4 (.5) |
| tom2 → snare | .4 (.5) |
| floor → snare | .45 (.6) |
| kick → tom1 / tom2 / floor | .25 / .25 / .35 |
| snare → tom1 / tom2 / floor | .12 / .12 / .10 |

Measured: a kick at u .9 makes the snare's wires sound at −32.5 dB against a snare stroke, a floor-tom stroke at −33.4 dB
(v1 −30.5 / −35.2). A u 1 kick puts the snare at −26 dB and the toms at −37…−44 dB re their own u .5 stroke. In grooves the
toms ring at −42…−44 dB under the snare. T60s are unchanged by the coupling (c8), and a 30 s abuse run is stable.

### 6.3 Levels (`LEVELS`, output gain per piece; physical units → app scale)

| snare | kick | tom1 | tom2 | floor | hat | crash | ride |
|---|---|---|---|---|---|---|---|
| 103.4 | 353 | 246.8 | 226.2 | 218.7 | 856.9 | 1075 | 1810 |

**Calibration** (`tools/calibrate.mjs`): the new snare's 0.4 s K-weighted loudness at u .8 equals the OLD snare's at vel .8
(−17.58 dB re full scale, dry); every other piece sits at its target relative to the snare. **v2 targets** (dB re snare,
0.4 s window, 1 s for cymbals): kick +3.5 (v1 +1.9), tom1/tom2 +2.5 (−1), floor +2 (−1.5), hat +3 (+3), crash +5 (+3),
ride +5.5 (+2). They were raised after `tools/groove-balance.mjs` showed v1's grooves sitting 2–4 dB (toms 6 dB) under the
legacy kit's at the app's level. They stay well under the legacy kit's tom- and cymbal-heavy balance.

Resulting single hits (dB re snare, u .8): hatOpen +7.7 (1 s, the open hat sustains), pedal chick −2.8, ride bell +6.7,
rimshot −1.3, cross-stick −8.7, snare off-centre (pos .75) −1.2.

In grooves (integrated, dry, re snare; `verify.json` `balanceInGrooveDbReSnare`):

| | old | new |
|---|---|---|
| rock | kick +4.2, hat +2.7, crash +8.4 | kick +2.8, hat +2.8, crash +6.9 |
| jazz | kick +2.6, hat −2.9, ride +14.1 | kick +1.9, hat −3.0, ride +13.5 |
| fill | kick +5.3, hat +2.8, toms +7.0/+8.3/+10.1, crash +10.2 | kick +4.8, hat +2.8, toms +3.6/+4.9/+5.2, crash +10.7 |

Whole groove, dry sum, new re old: typical-stroke groove −0.6, rock −0.4, jazz −0.6, fill −2.2 dB (the fill's toms are the
gap: the legacy toms are long sine booms). If the user wants the toms or cymbals more prominent, raise `LEVELS` by up to
3 dB; do not change the physics.

---------------------------------------------------------------------------------------------------------------------------

## 7. RNG, seeding, determinism

- **`Rng` = mulberry32**, identical to spec 06 (setup and per-hit draws). **`Noise` = xorshift32** (`s ^= s<<13; s ^= s>>>17;
  s ^= s<<5; return (int)s · 4.656612873077393e-10`), per sample. `Noise.fillLP` keeps its one-pole state `y`.
- **Kit seed → piece seeds:** `s = s·2654435761 + 0x9e3779b9` (uint), in `PIECES` order.
  - Membrane: `rng = Rng(seed ^ 0x5bd1e995)`; `noise = Noise(seed·7919 + 13)` (stochastic force); wires `Noise(seed ^ 0x2545f491)`.
  - Cymbal: `rng = Rng(seed ^ 0x68e31da4)`; `noise = Noise(seed·2654435761 + 1)` (noise bands, skin, air; also the LCG
    lane-group pick `s = s·1664525 + 1013904223`, bit 31); `injNoise = Noise(seed·40503 + 7)` (stochastic force).
- **The C# kit does not re-derive the instruments.** It loads `kit-tables.json` (the identity of `DrumKit(48000, 1)`) and
  starts each piece's RNGs from the exported `rngState` / `noiseState` / `injNoiseState` (states after construction and
  calibration). In the app, seed from the session (`SeedAll`); in tests, use kit seed 1.
- **Node determinism:** two runs are bit-identical (`out/test-vectors.json` `identicalAcrossRuns: true`).

---------------------------------------------------------------------------------------------------------------------------

## 8. Data layout and Burst notes

- **State per bank: SoA `float` arrays padded to a multiple of 4.** Hot, per sample: `zr, zi, cr, ci, gOut`, plus the input
  gains in use (`gIn` per slot, `gSym`, `gN`) and `psi` (wires). Per sub-block: `cr0, ci0` (base poles), `act`. Cold: `w0,
  rad, phi, eW, eM, f, m, c0, injC, chokeF, shape65`.
- **Kernels (mode-outer, float4 lanes over modes, state in registers over samples).** One specialised job per variant:
  `ring0` (free), `ring1` (one input), `ring2` (two inputs), `ring4` (dense modes during a contact). Every variant taps the
  output **before** the update:
  ```
  for (g = 0; g < n/4; g++) { if (!act4[g]) continue; float4 a = zr4[g], b = zi4[g], c = cr4[g], s = ci4[g], go = gOut4[g], gi = gU4[g];
    for (i = i0; i < i1; i++) { acc4[i] += go*a; float4 x = a + gi*u[i]; a = c*x - s*b; b = s*x + c*b; } }   // + Gff·u[i] once per sample
  ```
  Interleave 2–4 lane groups in the inner loop to hide the FMA latency (the recurrence is latency-bound, §9).
- **Activity in lane groups.** Cull and activate per group of 4 modes (`act4 = any of the 4`); the cymbal injection already
  picks whole lane groups.
- **The contact phase** (while a stick is on, ≤ ~6 ms per stroke, 14–18 ms on the kick) is sample-outer over the listed modes
  only, in **one fused pass** per sample (update, output, wires, both tips' predictions); the force solve is scalar (1-D or
  2×2 Newton). The dense, body and stick banks are then driven mode-outer with the stored forces.
- **Control work every 32 samples**: glide (sincos, or the small-angle rotation), drain factors and pole refresh, injection
  gains, cascade, choke, hat openness, flush, cull, idle. Keep control on the absolute 32-sample grid: events split segments
  but do not move the grid. The noise-band gain ramps need the samples left to the grid (`gridLeft`).
- **No allocation, no managed objects, no data-dependent branches inside the inner loops.** All scratch is allocated at
  init (reference: MAX_BLOCK 4096; Unity: 256 + 1 block slack).
- **Denormals:** the per-sub-block flush (§4.9) zeroes tiny states; also enable flush-to-zero where available.
- **Memory** (about 1 140 modes): state ≈ 18 floats × 1 300 × 4 B ≈ 94 KB; membrane shape tables 617 × 65 × 4 B ≈ 160 KB;
  slots 2 × 2 arrays per membrane ≈ 13 KB; scratch ≈ 12 × 256 × 4 B × 8 ≈ 100 KB; kit tables 0.56 MB as JSON (~0.2 MB as
  binary floats); **baked audio 0 MB**. Total about 0.4 MB, which leaves the 96 MB budget to piano, harp and voice.

---------------------------------------------------------------------------------------------------------------------------

## 9. CPU

Measured in Node 26 on an Apple M5 Pro (`tools/bench.mjs`, `out/bench.json`). Each schedule runs 5 times; the table gives
the **best run's** ns/sample and average block, and the **median run's worst block** (the absolute maxima include
interference: other agents were running Unity on this machine).

| schedule | what | ns/sample | avg µs / 256 | worst block µs (median run) |
|---|---|---|---|---|
| per piece | one piece re-struck every 120 ms, u .85 (kick wakes the snare and toms) | kick 853, snare 650, tom1 751, tom2 755, floor 752, hat 319, crash 318, ride 340 | 81–218 | 137–342 |
| all8 | all 8 re-struck every 120 ms, staggered 10 ms (v1's case) | 2 142 | 548 | 701 |
| **vrWorst** | every 120 ms both sticks on two drums **and** the kick on the same sample, hat/crash/ride re-struck: what a seated player can do | 2 107 | 539 | **781** |
| allSameSample | the critic's adversarial case: all 8 on the same sample every 60 ms at u 1 (5 contacts at once) | 2 262 | 579 | 1 035 |
| rock lesson, looped | | 859 | 220 | 504 |
| fill + crash, looped | | 825 | 211 | 462 |

v1 for comparison: all8 1 951 ns/sample, 500 µs average; its worst block was 583 µs by its own bench and 761 µs by the
critic's. v2 adds the second slot, the stiffening term, the smooth transfers, denser cymbals and the hat's noise bands, and
pays for them with culling, the sympathetic frequency limit, the kick's mode drop, the fused contact pass and the
half-lane-group injection.

**Node → Quest 3S Burst ratio** (assumed, UNVERIFIED on device):
- The raw V8 modal kernel here costs **1.28 ns per mode-sample** (scalar, latency-bound).
- Burst NEON on an A78-class Quest core (~2.0–2.4 GHz, 4-cycle FMA latency): one lane group of 4 modes per dependent chain
  ≈ 7–8 cycles per sample → 0.75–0.95 ns per mode-sample; with 2–4 lane groups interleaved, 0.3–0.5 ns. **Modal kernels:
  ratio 0.3–0.7.**
- Scalar parts (contact Newton with sqrt, biquads in wires and noise bands, control): native code on a slower core, **ratio
  1.5–2.0**. They are about 35 % of the time.
- **Combined ratio 0.6–1.2; plan with 1.2.** Seated-player worst case: **0.65 ms average, 0.95 ms worst block**; the
  adversarial all-same-sample case 0.69 / 1.24 ms; lesson grooves **0.26 ms**. That fits the ~2 ms whole-graph budget, at
  up to half of it in the worst case.

**Reductions, if the device measurement says so** (in this order):
1. Interleave lane groups in the Burst kernels (no sound change; the largest single gain).
2. **Cymbal bank at half rate** below 4 kHz, upsampled with a 97-tap half-band kernel (crash/ride/hat ~ −40 %).
3. **Per-piece mode caps:** floor maxHz 1000 → 800, tom2 1200 → 1000 (−~40 modes). Re-run `verify.mjs` `modes` and
   `compare-hits.mjs`.
4. Raise `CULL` to 1e-11 (−110 dBFS).

---------------------------------------------------------------------------------------------------------------------------

## 10. Verification (all numbers from `out/verify.json`, `out/checks.json`, `out/acceptance.json` and the critic's scripts)

### 10.1 Profiles against the old kit (onset-safe)

`tools/compare-hits.mjs`: octave levels re max over 0.4 s (1 s for cymbals), u/vel ≈ .8, rendered with 2 ms of silence
first and a rectangular window from 0. Octaves 31.5 … 16 k:

| piece | old | new |
|---|---|---|
| kick | −18 −4 0 −14 −29 −39 −38 −37 −35 −43 (a sine) | −7 0 −15 −16 −26 −18 −23 −32 −43 −57 (body + soft knock) |
| snare | −20 −16 −1 0 −12 −13 −5 −3 0 −10, c50 3.2 kHz (noise) | −32 −23 −8 −3 −14 −10 0 −3 −5 −15, c50 2.7 kHz |
| snare rimshot | c50 2.5 kHz | −33 −24 −14 −12 −7 −3 0 −6 −8 −18, c50 2.4 kHz |
| tom1 | −28 −24 −3 0 −27 −39 −36 −36 −36 −43 (a sine) | −32 −23 −6 0 −4 −4 −7 −20 −27 −42 |
| floor | −24 −3 0 −26 −36 −45 −39 −41 −42 −49 | −31 −16 0 −9 −9 −11 −13 −21 −28 −42 |
| closed hat | … −7 0 −3, c50 10.4 kHz (six squares) | −71 −63 −55 −49 −30 −18 −14 −7 0 −6, c50 8.6 kHz |
| open hat | c50 10.4 kHz, T60 0.46 s | … −18 −13 −7 −2 0 0, c50 9.6 kHz, T60 1.9 → 1.3 s |
| crash | … −10 −1 0 −4, c50 7.5 kHz | −58 −53 −39 −23 −14 −5 −1 0 −3 −6, c50 5.6 kHz |
| ride bow | c50 6.0 kHz | −66 −56 −47 −24 −15 −6 −3 0 0 −2, c50 6.9 kHz |
| ride bell | c50 3.3 kHz | −61 −53 −44 −34 −24 −2 −2 0 −5 −18, c50 3.1 kHz |

Attack (critic c11, rectangular 0–50 ms after a 2 ms pre-roll; centroid / energy above 2 kHz): snare u .2 / .5 / .84 / 1:
1946 / 2119 / 2492 / 2501 Hz, 30 / 32 / 38 / 38 % (old 1830–3399 Hz, 24–47 %); tom1 u .3 / .8: 328 / 506 Hz, 1.3 / 5.5 %;
floor 219 / 387 Hz.

### 10.2 Modal frequencies

Soft stroke u .25 at r .45, peaks from 10–910 ms (cents re design): kick 56 0, 82 +12, 91 −1, 99 +16, 121 +6, 142 +1;
snare 183 0, 275 +3, 330 +8, 344 +3, 405 −9, 595 +11 (the 451 Hz (0,2) sits on its nodal circle at r .436); tom1 165 0,
229 0, 247 +6, 268 +5, 336 −4, 390 +1; tom2 131 0, 182 −1, 196 −1, 213 −8, 268 +6, 312 0; floor 98 0, 137 0, 147 +1,
160 +4, 201 −2, 236 0.

### 10.3 Decay (T60, s, per octave 63…8 k, u .7)

| piece | 63 | 125 | 250 | 500 | 1k | 2k | 4k | 8k |
|---|---|---|---|---|---|---|---|---|
| kick | .42 | .39 | .22 | .17 | .16 | .21 | .21 | .18 |
| snare | .20 | .22 | .23 | .30 | .22 | .10 | .18 | .19 |
| tom1 | .72 | .71 | .92 | .62 | .42 | .32 | .37 | .40 |
| tom2 | .66 | .66 | .83 | .56 | .38 | .34 | .35 | .37 |
| floor | .85 | .88 | .98 | .75 | .51 | .42 | .38 | .40 |
| closed hat | | | | .07 | .06 | .09 | .06 | .06 |
| open hat | | | 2.06 | 2.02 | 1.86 | 1.77 | 1.54 | 1.28 |
| crash edge | 2.2 | 2.5 | 2.5 | 2.2 | 2.2 | 2.2 | 2.0 | 1.7 |
| ride bow | 5.5 | 5.7 | 5.6 | 4.9 | 4.3 | 3.4 | 3.0 | 2.3 |
| ride bell | 6.4 | 6.4 | 6.4 | 6.5 | 6.5 | 5.1 | 3.7 | 2.4 |

The membranes' T60 above 2 kHz is the very-low-level dense-field tail. Crash hum vs wash (critic c13, 177–1414 Hz minus
2.8–11.3 kHz, windows 0–.3 / .3–1 / 1–3 s): u .95 −10.1 / −12.4 / −1.2 dB; u .3 −6.2 / −4.5 / +5.9 (a soft crash is darker,
as a real one is); ride bow u .6 −10.7 / −7.1 / +3.0.

### 10.4 Clicks, aliasing, sanity

For every `drums-*-new.wav`: no NaN or Inf; DC below 2e-4; energy above 20 kHz below 0.005 %. Click score (largest |Δ²x| /
local rms outside the 6 ms after each onset): 0.12–7.3 against the old files' 0.12–7.4. The score saturates at ~6–8 on
bright noise (white noise scores ~8), so it is high wherever a hat or ride rings; the worst spots are 47–60 ms into a ride or
open-hat wash, with no event and no discontinuity. The choke scores 0.96. Grid artefacts (critic c5): no piece above 2.8 dB
at any grid phase; the remaining "1.5 kHz lines" up to 6 dB are seed-dependent statistics of the metric on noise (they move
between seeds 7/3/11) or real modes that happen to sit there (the hat's 2997.8 Hz).

### 10.5 Micro-variation

8 identical strokes (snare, u .7, pos .2, 0.5 s apart): loudness spread **0.95 dB**, peak spread 2.0 dB, centroid spread
320 Hz; consecutive 50 ms waveform correlation **0.59–0.71**. The old kit is identical every time.

### 10.6 Crest factor and the −18 LUFS rule

PLR (true peak minus integrated loudness), critic's 8× meter, old / new: rock 22.6 / 23.0; jazz 18.2 / 19.7; fill 19.8 /
21.9; snare ladder 24.2 / 25.2; tom ladder 17.5 / 26.7 (the old toms are sines); crash ladder 15.7 / 18.5 dB. **Neither kit can reach −18 LUFS with a true peak under −1 dBFS without a limiter** (the
old rock groove alone would peak above 0 dBFS). The rule forbids a limiter (it would change the sound being judged), so each
pair is matched at the highest common loudness that keeps the louder-peaking file at −1.2 dBTP on the lab's 4× meter
(−1.13 or lower on the critic's 8× meter). That is −19.7 … −28.0 LUFS. Old and new of every pair are within 0.00 LU
(exact matching with re-measure, `lib/mix.mjs`).

---------------------------------------------------------------------------------------------------------------------------

## 11. Test vectors and EditMode tests

**Test vectors** (`Tests/EditMode/Audio/Drums/Reference~/test-vectors.json`). Setup: `DrumKit(48000,
seed 1)` with the `kit-tables.json` identities and exported RNG states, rendered in 256-frame blocks for 3 s. Events
(sample = round(t·48000)):

| t (s) | event |
|---|---|
| 0 | snare .8 pos .2 |
| 0.25 | kick .8 |
| 0.5 | hat .7 pos .55 top |
| 0.75 | crash .9 pos .9 edge |
| 1.0 | ride .6 pos .62 bow |
| 1.25 | tom1 .7 pos .3 |
| 1.5 | choke crash |
| 1.75 | hatOpen .7 pos .55 top |
| 2.25 | hatClose .7 |
| 2.5 | snare .9 pos .95 rim |

Expected values (per piece output, after LEVELS):

| piece | first non-zero sample | first 4 samples | rms over [0–.05, .05–.1, .1–.25, .25–.5, .5–1, 1–2, 2–3] s | peak |
|---|---|---|---|---|
| snare | 0 | −0.0170138, −0.0681767, −0.185658, −0.325367 | .29766, .035986, .0053085, .0026513, 3.5285e-6, .0012318, .064709 | 2.2816 |
| kick | 12000 | −0.0197657, −0.0673149, −0.114733, −0.134448 | 0, 0, 0, .40124, .0038928, 7.2233e-7, 0 | 2.0208 |
| hat | 24000 | 0.045915, 0.163513, 0.272049, 0.328674 | 0, 0, 0, 0, .10157, .17693, .057594 | 2.3240 |
| crash | 36000 | 0.0470086, 0.197029, 0.388468, 0.544651 | 0, 0, 0, 0, .24028, .089685, 0 | 1.5902 |
| ride | 48000 | 0.0355058, 0.143345, 0.255656, 0.318474 | 0, 0, 0, 0, 0, .13369, .012804 | 1.2117 |
| tom1 | 64 (sympathy from the snare) | −2.44172e-8, −2.01415e-7, … | 7.7664e-4, 4.0427e-4, 1.5865e-4, 1.5849e-3, 8.3063e-5, .10225, 5.7904e-4 | 2.2293 |
| tom2 / floor | 64 | (see file) | (see file) | .0072385 / .0064914 |

The first output sample of a stroke is now c₀·Σ gOut·gIn·F (half the first velocity jump), so the first samples are smaller
than v1's (snare −0.017 vs −0.048), and there is no longer a one-signed run under the contact (mean/rms over 0–60 ms
0.00 vs v1's −0.17).

**Tolerances.** C# `float` against JS double-inside, Float32-stored: first 8 samples within 1e-4 absolute; window rms within
±0.3 dB where the window is above −80 dB re the piece peak; peak within ±0.3 dB. Any structural bug (wrong mode order, a
missed semi-implicit step, the output tapped after instead of before the update, a sign) shows up as whole decibels.

**EditMode tests to write** (the acceptance numbers are `out/acceptance.json`):
1. `Rng`/`Noise` sequences from seed 1; `Noise.fillLP` with α of 4 kHz.
2. Loading `kit-tables.json`: mode counts as §4.2/§5.1; first snare frequencies 183, 273.9614, 276.0386, 330, 343.0495,
   344.9505; first crash frequencies 168.6198, 174.0719, 191.4426, 205.6349; every mode's `c0` recomputed from its pole
   matches the exported value within 1e-5 (kick fundamental −5.863).
3. The test vectors above.
4. **DC-free output (A1):** snare u .84: |mean(0–60 ms)|/rms < 0.05 and energy below 60 Hz < −15 dB re the total; tom1 u .8
   < −10 dB; pedal chick < −45 dB; rectangular windows from 2 ms before the onset (measured −22.1 / 0.00 / −21.5 / −56.6).
5. **Two sticks (A2):** two u .8 strokes 1/3/6 ms apart ≤ +6 dB re one stroke (snare +2.3/+4.0/+3.6, tom1 +2.2/+3.3/+3.2);
   a second stroke 1 ms into the first: no sample-to-sample force step above 20 % of Fmax (3–4 %).
6. **Velocity (A3):** snare L(u .35) − L(u .95) within ±3 dB of the legacy kit (+1.3).
7. **Contact (A4):** snare at 0.5 m/s 3–6 ms (5.15), at 10.5 m/s 3–6 ms and shorter (3.94), Fmax 60–160 N (91); force
   probe table §4.4 ±10 %.
8. **Crash wash (A5):** u .95 edge, 0.6–1.6 s: 20 strongest lines < 40 % of 2–5 kHz and of 5–9 kHz (22.8 / 20.4 %),
   flatness > −8 dB (−5.7 / −6.1); hum minus wash at 1–3 s < +6 dB (−1.2).
9. **Grid (A6):** kick/floor/snare u 1: the largest grid-phase |Δ²x| < 3 dB over the median (1.3 / 0.5 / 1.3).
10. **Choke (A7):** crash drops > 40 dB 150 ms after the hand (53.4), click score < 3 (1.1).
11. **Glide:** the §4.5 engine table ±15 c. **T60** per octave §10.3 ±15 %.
12. **Hat:** pedal-close drop ≥ 60 dB; half-open energy above 8 kHz > closed.
13. **Sympathy:** kick u .9 → snare bus −32.5 ± 3 dB re a snare stroke.
14. **Balance** §6.3 single-hit table ±1 dB.
15. **Micro-variation:** loudness spread < 2 dB, consecutive correlation < 0.9.
16. **Allocation:** `Is.Not.AllocatingGCMemory()` around `DrumKitDsp.Render` with events. **Determinism:** two renders are
    bit-identical.

---------------------------------------------------------------------------------------------------------------------------

## 12. Verifying on the headset

1. **Development build** with `DrumEngine.UseModelled` toggled from the palm menu's debug page, for A/B against the legacy
   kit on the device itself.
2. **CPU.** A `DrumBench` debug action plays the `vrWorst` schedule (§9) and then `allSameSample` for 60 s each, and logs the
   `DrumKitDsp.Render` average and maximum µs per block (`ProfilerMarker` plus `Stopwatch` ticks) and
   `SynthSource.MaxBlockMicros` for the 8 sources. Pass: `vrWorst` average < 0.8 ms and max < 1.2 ms per 256-frame block.
   Repeat after 10 minutes of play (thermal state). Check logcat for audio underruns. Use the measured ratio to replace the
   assumed one in §9.
3. **Sound.** Record the master on device (Unity `AudioRenderer` in a dev build, or a 10 s WAV from a debug ring tap on
   `MasterBus` via `adb pull`); play `patterns.rock()` from a scripted event list. Compare against
   `Tests/EditMode/Audio/Drums/Reference~/renders/drums-rock-new.wav`, dry before HRTF: octave band levels within ±2 dB and per-piece peak timing.
4. **Listening checklist** in the headset, seated, sticks: ghost notes (wires sizzle, no head boom, **no thud under the
   snare**); backbeat (fat plus crack, no ringing "boing"); toms (pitched, a short glide on hard hits only); kick by nod
   (thump plus a soft knock); closed/open/half hat and the pedal chick; crash bloom and choke (hand rest); **crash tail:
   a fading wash, no gong hum**; ride ping over the wash and a ride bell that cuts through; both sticks on one drum at once
   (no click); rimshot and cross-stick if the contract additions are in.
5. **Compressor.** Watch the `MasterBus` gain reduction meter on dense fills. If it pumps more than with the legacy kit,
   trim `KitTrimDb` −2.

---------------------------------------------------------------------------------------------------------------------------

## 13. A/B renders (`drums-*.wav`; the `-new` side is in `Tests/EditMode/Audio/Drums/Reference~/renders/`)

All files are 48 kHz stereo 24-bit. Old = the current kit exactly, through the spec-06 reference DSP. New = this engine.
Both go through the same listening mix: pan (drummer's perspective: hat L, floor/ride R), 0.2 ms far-ear delay, the app's
drum room (0.45 s `makeRoomImpulse`, seed 6, convolver-normalised) with the app's sends, bus 0.3; no compressor, no EQ.
Each pair is matched to one integrated loudness (§10.6), except the app-level pair, which has one common gain.

| pair (`drums-<name>-old/new.wav`) | content | s | LUFS (both) | dBTP old / new |
|---|---|---|---|---|
| `rock` | app lesson `drums-rock` (96 bpm, 4 bars), accented 8th hats, kick on "3&", open hat on "4&" closed by the pedal, crash + kick on the 5th downbeat | 12.0 | −24.14 | −1.54 / −1.14 |
| `jazz-ride` | swing ride 132 bpm (1, 2, 2a, 3, 4, 4a), hat pedal on 2 & 4, feathered kick, snare comping, ride bell | 9.9 | −20.90 | −2.66 / −1.19 |
| `fill-crash-choke` | `drums-fill-round` shape: a groove bar, 16th fill snare→tom1→tom2→floor crescendo, crash + kick, hand choke, rimshot | 8.1 | −23.07 | −3.29 / −1.18 |
| `snare-roll` | single-stroke roll, 13 strokes/s, soft to loud over 5 s, weaker left hand, ending accent + crash | 8.6 | −23.42 | −2.76 / −1.29 |
| `soft-groove-applevel` | **new:** 2 bars of the rock lesson + 2 of the swing pattern with every stroke squeezed into u .3–.6 (typical VR strokes); **one common gain**, so the in-app loudness difference is audible | 10.1 | old −22.14, new −22.69 (−0.56 LU) | −2.54 / −1.15 |
| `hits-kick` | kick ladder u .25 .45 .65 .85 1 | 4.1 | −21.89 | −5.27 / −1.20 |
| `hits-snare` | centre ×5 velocities, off-centre ×3, rimshot ×3, cross-stick ×3 (old: rim at .6 vel; it has no cross-stick) | 7.8 | −26.37 | −2.21 / −1.19 |
| `hits-toms` | tom1, tom2, floor ×3 velocities, tom1 and floor rimshots | 10.6 | −27.95 | −10.42 / −1.20 |
| `hits-hats` | closed ×5, edge ×2, half-open (new only), open ×2 closed by the pedal, pedal chicks ×3 | 8.2 | −27.96 | −1.13 / −1.84 |
| `hits-crash` | edge u .3/.6/.95, bow, bell, edge + hand choke | 12.3 | −19.67 | −3.96 / −1.20 |
| `hits-ride` | bow ×3, bell ×2, edge (crashing the ride) | 12.5 | −20.76 | −1.17 / −3.44 |

(dBTP measured with the critic's 8× meter.) Human feel: seeded timing ±3 ms and stroke ±0.04, identical for old and new. Total 59.9 MB (57.1 MiB, 22 files). The demo music is the
app's lessons and generic patterns; none of it is copyrighted.

**What to listen for:** the snare's wires and crack against the old noise burst, **with no thud underneath**; toms with
overtones and a real decay against sine "boops"; cymbals that bloom into a wash and fade without a gong hum, against
square-wave fizz; the hat's chick and sizzle; hard strokes brighter, not only louder; no two strokes alike.
**Risks to check by ear:** the open hat now sustains longer and brighter than v1's; the toms are a little quieter in the
fill than the legacy sine booms; the kick is a damped, pillowed kick, not the old long boom.

---------------------------------------------------------------------------------------------------------------------------

## 14. Sources (numbers used)

- **[R92]** T. D. Rossing, "Acoustics of drums", *Physics Today* 45(3), 40–47 (1992). Read in full.
  - Ideal membrane ratios 1, 1.594, 2.136, 2.296, 2.653, 2.916, 3.156, 3.501, 3.600, 3.652, 4.060, 4.154.
  - Snare modal analysis: (0,1) 183 Hz heads in phase; (1,1) 275 Hz heads opposite and 344 Hz in phase; (2,1) 405; (0,2)
    451 batter, 595 snare head; 330 Hz in Fig. 4.
  - Timpani (m,1) ratios 1 : 1.5 : 2 : 2.44 : 2.90; air loading lowers the (m,1) family and enclosed-air stiffness raises
    (0,n). Bass drum: batter tuned above the carry head; a full-blow amplitude of 6 mm ≈ 10 % upward shift; ΔT ∝
    amplitude², f ∝ √(T + ΔT).
- **[RB92]** T. D. Rossing, I. Bork, H. Zhao, D. O. Fystrom, "Acoustics of snare drums", *JASA* 92(1), 84–94 (1992). Not
  read directly.
- **[W06]** A. Wagner, *Analysis of drumbeats: interaction between drummer, drumstick and instrument*, MSc thesis, KTH
  Stockholm (2006), https://www.speech.kth.se/publications/masterprojects/2006/AndreasWagner.pdf. Read here (text
  extracted with a small zlib parser). Used: contact 3–6 ms between drumstick and membrane, shorter with increasing dynamic
  level; at forte the force pulse alone would end after ≈ 3.5 ms; peak force from ≈ 3 N (piano) to slightly over 100 N
  (forte) at the centre, a factor of ~33; the (0,1) mode rises ≈ 3 Hz (138 Hz head) at forte, attributed to the
  stroke-induced tension rise; double contacts with the reflected wave at forte on a loose head; higher tension → shorter
  contact; sticks of 53 and 56 g, 40.4 cm.
- **[D11]** S. Dahl, "Striking movements: a survey of motion analysis of percussionists", *Acoust. Sci. & Tech.* 32(5),
  168–173 (2011), doi 10.1250/ast.32.168. Read in full. Contact durations 5–8 ms for a mezzoforte stroke at the centre of a
  tom or snare; an accent ≈ 2× the striking velocity; tip speeds to ~12 m/s.
- **[WR96]** C. Wilbur, T. D. Rossing, ASA abstract 5pMU8 (1996): ~300 modes observed in a 16" cymbal. Abstract only.
- **[P]** R. Perrin, G. M. Swallowe, S. A. Zietlow, T. R. Moore, "The normal modes of cymbals", *Proc. IoA*: mode Q ~10³ or
  greater. Search snippet only.
- **[CH]** L. Cremer, M. Heckl, *Structure-Borne Sound*: plate point impedance Z = 8√(Dρh), D = Eh³/12(1−ν²), plate modal
  density n(f) = (A/2)√(ρh/D) (used to check `dens`: 0.05–0.10 /Hz for these cymbals), coincidence and radiation efficiency.
- **[J]** K. L. Johnson, *Contact Mechanics* (Hertz contact, F ∝ δ^1.5) and K. H. Hunt, F. R. E. Crossley, *J. Appl. Mech.*
  42 (1975), the dissipative contact term.
- **[AMB]** F. Avanzini, R. Marogna, B. Bank, "Efficient synthesis of tension modulation in strings and membranes based on
  energy estimation", *JASA* 131(1) (2012): driving the pitch from a modal energy estimate.
- **[WT]** C. Touzé, M. Ducceschi, S. Bilbao et al., wave turbulence in vibrating plates (*Physica D* 2014 and related): the
  low→high energy cascade whose rate grows with amplitude. Concept only.
- **Material data:** PET film ρ 1390 kg/m³, E ≈ 4–5 GPa, 10-mil (0.254 mm) heads with coating ≈ 0.36 kg/m² (Eh ≈ 1.1e6 N/m);
  B20 bronze ρ 8600 kg/m³, E ≈ 1.0e11 Pa; hickory E ≈ 1.5e10 Pa, ρ ≈ 830 kg/m³; free-free beam ratios 1 : 2.756 : 5.404 :
  8.933 (Euler–Bernoulli).
- **[BS1770]** ITU-R BS.1770-4: K-weighting, gating, true peak.
- **[T] tuned here:** every gain, the dense-field drives, the nonlinear constants, the stiffening shape factors, the wire
  filter, the sympathy gains, the cymbal densities/decay laws/cascade constants and LEVELS. Each was set against a
  measurement in `tools/` or `critic/` (octave profile, component balance, contact probe, line share, hum/wash, PLR,
  in-groove balance).

---------------------------------------------------------------------------------------------------------------------------

## 15. Risks, open points, UNVERIFIED

1. **Not heard by anyone yet.** An earlier "audio upgrade" attempt was judged worse. The A/B files are the gate: if the user
   prefers the old kit, keep `UseModelled = false`. Nothing else depends on this engine.
2. **The Node → Quest ratio is assumed** (0.6–1.2, planned at 1.2). Run §12 step 2 before any other audio work lands on top.
   §9 lists the reductions in order.
3. **C# float against the Node reference:** the test-vector tolerances are estimates. Very long ride modes (T60 ~6 s) and the
   2×2 Newton accumulate rounding; if a ride window drifts more than 0.3 dB, compare octave levels instead.
4. **Kick contact is long** (14–18 ms) and its 1 k–2 kHz knock is soft. A harder, clickier kick would need a lighter beater
   (`DRUMS.kick.stick.m`) or a higher `stiff.c`; not tuned by ear.
5. **Open hat** sustains ~1.5 s at 4–8 kHz and is +7.7 dB re the snare over 1 s. If it washes over grooves, lower
   `CYMBALS.hat.t60.at` (2.2 → 1.8).
6. **Cymbal mode sets are statistical**, not measured from a specific cymbal; their identity is fixed by `kit-tables.json`.
   A soft crash (u .3) still ends with a low-mid tail (+5.9 dB hum over wash at 1–3 s), as real soft crashes do.
7. **Contract additions** (§2.3) need the Drums module: `Implement`, `Openness`, `Zone.CrossStick`. Without them, cross-stick
   and half-open are unreachable in VR (the A/B renders show them anyway).
8. **Sympathetic wake** raises the average CPU in grooves (the snare and toms run while the kick plays). The worst case is
   unchanged.
9. **Same-sample unison on the snare is +6.5 dB** over one stroke (§0.1). If two-hand unison hits sound too loud in VR,
   halve each stroke's V when two strokes land within 1 ms (a one-line change in `DrumKit.apply`).
