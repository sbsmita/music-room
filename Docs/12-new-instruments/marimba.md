# 12-new-instruments · Marimba: a physically modelled wooden marimba played with two mallets (implementation-ready spec)

Key `marimba`. Lab, in Node 26 with no packages: the marimba lab (Node prototype; its vectors are frozen in
`Tests/EditMode/Marimba/Reference~/` and `Tests/EditMode/Audio/Marimba/Reference~/`). Renders are
`Builds/Listen/marimba/marimba-*.wav`, with a plain-English guide in `marimba-README.txt`.

The marimba plugs into the many-instrument platform of `12-new-instruments/platform.md`. That spec owns the ids, the
registry, display, seat, tools, recorder, lessons, Sunny, mixed reality and the audio roles. This spec fills in the
marimba's own descriptor, module and sound unit against those contracts. The sound unit sits on the Burst engine of
`10-upgrade/burst.md`, plugged in through `10-upgrade/ENGINE-API.md`.

Everything was built and measured in Node. **Nobody has listened to it yet.** Section 10 lists what the user should
judge by ear.

**Revision 2 (after an independent critique, the lab's `critic/`).** The critic drove `MarimbaPlay` with
realistic VR strokes (no bar stops the hand, so strokes overshoot; tracking jitters, and the mallet's lever magnifies it)
and found the interaction and the first lesson weak while the sound engine was sound. This revision rewrites the stroke,
dead-stroke, rest and hand-mute rules for overshoot and jitter (5.3, 5.4), re-voices the fingertip (2.4), makes the first
lesson's accompaniment follow the player (7), points every sharp at its reachable tip (5.2), plans a sticking that never
crosses the hands (7), replaces the 4× Euler contact solve with a cheaper and more accurate 2nd-order one (2.4), trims the
lateral sources' room send (3.1, request R1), and fixes the spec's contradictions. Section 14 lists every critic issue,
the fix and the before/after numbers. The critic's own scripts were re-run on the revised code.

---------------------------------------------------------------------------------------------------------------------------

## 0. Decisions

1. **A marimba, not a xylophone.**
   - The room is calm, warm and set at night. The user asked for a warm wooden tone, not a glassy or metallic one.
   - A marimba's deep undercut bars are tuned 1:4:10. The overtones (two octaves, and three octaves plus a major third,
     above the note) blend into the note, and the resonators reinforce only the fundamental.
   - A xylophone is tuned 1:3 (a twelfth). Its tubes reinforce the 3rd partial as well, and it is played hard in a
     high register: bright, short and clacky.
   - A beginner gets a big, forgiving sound from the marimba's low and middle registers.
   - The model keeps the xylophone case as data. `tuneArch(3, 6)` converges, and the 1:3 tube coupling is the same
     mechanism.
2. **Range C3–C6: 37 bars.** The 22 naturals sit in front; the 15 sharps sit behind, 22 mm higher.
   - This is the harp's range.
   - **All 24 piano and harp lessons** of `lessons.json` (G3–G5, including Für Elise's G#4 and D#5) fit
     unchanged. `lessons.mjs` checks this: 24/24.
   - The lesson notes C4–A4 sit straight in front of the player.
   - The natural row is 1.136 m wide, about the piano's 1.23 m. The mallets add 27 cm of lever, so every natural bar is
     in the comfortable seated zone (5.5).
3. **The sound is a physical model, split the way Unity needs it.**
   - **Main thread, per strike.** It solves the yarn mallet's contact with the bar in double precision: a Hunt-Crossley
     spring against the bar's modes, kick-drift-kick (2nd order) at 1–2× oversampling, at most 12 ms. It costs 2–12 µs
     per strike in Node for the app's mallets and fingertips (median 6 µs; 2.4). It writes a strike record:
     - the radiated sound during contact;
     - the soft wood knock;
     - the exact complex modal states at the moment the mallet leaves.
   - **Audio thread.** The bars ring as free MB4 modal banks on the engine's own kernels (`mbRenderFree`,
     `accResolve`, `mbEndBlock`). Strike records are injected sample-accurately. Damping (a resting hand, a mallet held
     down) swaps pole tables.
   - **The physics in the model:**
     - an undercut-bar finite-element model, tuned the way a maker tunes;
     - four bending modes and two torsional modes per bar;
     - a quarter-wave tube under every bar, solved with the fundamental as one coupled pair;
     - wood, cord and radiation losses;
     - a Hertzian mallet whose contact gets shorter and brighter as the stroke gets faster.
4. **Interaction is today's drumsticks, with mallets.** It runs on the platform's tool system (`IHandToolProvider`,
   `ToolKind.Mallet`).
   - **Pick-up:** a pinch, or a closing fist, within 12 cm of a shaft.
   - **Holding:** the same grip math; let go after 0.25 s open; rides out 1.5 s of tracking loss.
   - **Striking:** the mallet head is a 17 mm sphere pointer that strikes the bar's top plane. Fingertips play too, a
     little softer and duller than a mallet (an ordinary tap sits 2 LU under an ordinary mallet stroke).
   - **Built for VR strokes:** nothing stops the hand at the bar, so a stroke overshoots and turns around under the
     plane, and tracking jitters. The rules use hold bands with a lower limit, stillness over 60 ms, two-sample
     hysteresis and the approach speed (5.3, 5.4); `tools/ix-realistic.mjs` checks them against 2 160 overshooting,
     jittered strokes.
   - **Techniques:**
     - a mallet held down on the bar for a moment after a stroke is a dead stroke (on after Sunny's tip, off in lessons);
     - a mallet lowered slowly onto a bar, or a resting hand (two or more fingertips), mutes it;
     - alternating strokes make a roll.
   - **Feedback is physical only:** the bar dips on its cord, the mallet head rebounds, and contact shadows show where
     it will land. Nothing glows (platform 3.3).
5. **Three sources, one per octave (Lo C3–B3, Mid C4–B4, Hi C5–C6).** Every bar is one real point source.
   - Panning a bar between two spaced sources makes a phantom image. The far ear's head shadow costs the phantom about
     4 dB of direct sound at the centre: seat DRR +1.2 dB against +5.7 dB for a real centre source (5.2). That is
     exactly where the lessons are played.
   - **This needs a third source id, `MarimbaMid`, from the platform's id block (contract request R1).**
   - The two lateral sources send 2 dB less to the room (Lo, Hi −2 dB; Mid 0 dB), so every source meets the seat-clarity
     rule: DRR +4.8 / +5.7 / +4.3 dB against the required +4.1 (3.1; also R1).
6. **Cost on a Quest 3S at CPU level 3 (estimates from the lab, burst.md ratios):**
   - audio thread: about 20 µs per 5.33 ms block (53 µs worst) for a seated player's worst playing;
   - 32 µs average and 66 µs worst with all 37 bars ringing;
   - the platform's cap is 200 / 300 µs.
   - main thread: at most about 50–60 µs per strike (Node worst 12–15 µs × 4), under the 80 µs device check M2.
   - Memory: 151 KiB of audio state, 28 KiB of tables, no baked audio. Draws: 5 at the seat, 3 on display.
7. **Verification: 23/23 objective sound checks (Section 3), 8/8 realistic-interaction checks (9.2), the lesson balance
   (7), and the platform's conformance numbers (3.1).**
   - **Loudness:** the reference phrase is within −0.14 LU of the new piano.
   - **Dynamic range:** 13.4 dB against the piano's 14.5 dB; the fingertip's is 13.1 dB.

## 1. Files and how to run

| File | What |
|---|---|
| `design.mjs` | The physical design. Covers the FE undercut bar (`lib/fe-bar.mjs`, `lib/linalg.mjs`), per-bar geometry, modes, damping, radiation, tubes, the coupled pair, layout, source assignment and the MB4 lane table. Writes `out/design-report.json` |
| `marimba-engine.mjs` | **The reference to transliterate.** Contains `buildModel`, `contactSolve` (main thread), `dampSolve`, `MarimbaUnit` (strike, damp, undamp, silence, setBudget, and `render` on the audio thread) and `renderScore` |
| `interaction.reference.mjs` | The bare-hands play logic (`MarimbaPlay`, revision 2) and the accompaniment rule `accompanyVel`. Writes `out/interaction-vectors.json` (cases IX1–IX18) |
| `tools/ix-realistic.mjs` → `out/ix-realistic.json` | `MarimbaPlay` under realistic VR strokes (overshoot, pauses, 0–8 mm head jitter): 8 acceptance checks (9.2) |
| `tools/lesson-balance.mjs` → `out/lesson-balance.json` | The first lesson's melody against its accompaniment, fingertip and mallet, at ordinary and soft gesture speeds (7) |
| `tools/os-accuracy.mjs` | The contact solve's accuracy and cost per mallet at oversampling 1 / 2 / 4 against 16 (2.4) |
| `verify.mjs` → `out/verify.json` | The 23 objective checks |
| `tools/parity.mjs` → `out/parity.json`, `tools/share-check.mjs` | Platform conformance: seat DRR per source with the room sends, the piano's own sources in the same harness, loudness parity on the Ode to Joy reference phrase, ff true peak through MasterBusV2; the `Share` effect on `Work` and single-stroke peaks |
| `critic/` (read-only here) and `out/critic-baseline/`, `out/critic-c3-before-after.json` | The critic's scripts, their outputs before this revision, and the interaction numbers before and after |
| `bench.mjs` → `out/bench.json` | CPU per block (7 schedules), main-thread cost per strike, memory |
| `test-vectors.mjs` → `out/test-vectors.json` | Expected values TV1–TV8. Deterministic: identical MD5 over repeated runs |
| `tools/calibrate.mjs` → `out/calibration.json` | Gain (against the new piano), per-bar voicing, and the fingertip's per-bar voicing |
| `tools/calibrate-coupling.mjs` | Fits `KAPPA_G` to the C3 decay target |
| `tools/export-tables.mjs` → `out/marimba-tables.json` | All table data in one 94 KB JSON: a TextAsset, or the EditMode reference for `MarimbaDesign.Build` |
| `lessons.mjs` → `out/lessons-marimba.json` | The lesson pack, copied in as `Marimba/Resources/LessonPacks/marimba.json` |
| `render.mjs` → `Builds/Listen/marimba/marimba-*.wav` + `out/renders.json` | Listening files (Section 10) |
| `tools/probe.mjs`, `evolve.mjs`, `loudness-map.mjs`, `render-check.mjs` | Development probes |

**Run, in about 40 s on the M5 Pro:**

```sh
# from the marimba lab folder
node design.mjs            # --retune re-runs the arch tuning and checks it reproduces the stored ARCH (2.6 s)
node tools/calibrate.mjs
node verify.mjs
node tools/parity.mjs
node test-vectors.mjs
node tools/export-tables.mjs
node interaction.reference.mjs
node tools/ix-realistic.mjs
node lessons.mjs
node tools/lesson-balance.mjs
node render.mjs
node bench.mjs
node critic/c1-renders.mjs # ... c2 to c7: the critic's checks, re-run on this revision
```

The lab imports other labs read-only:
- `tools/calibrate.mjs` and `tools/parity.mjs` use the new piano (`Tools/SoundLab/piano`);
- the room renders use `Tools/SoundLab/room/room-engine.mjs`;
- the kernels and helpers are `Tools/SoundLab/burst/lab/*.mjs`, so the marimba runs on the engine's own canonical arithmetic.

---------------------------------------------------------------------------------------------------------------------------

## 2. Sound model

### 2.1 Signal flow

```
 MAIN THREAD (MarimbaPlay -> IMarimbaSound on the unit, reached by audio.ActiveUnit(UnitId.Marimba))
   Strike(bar, vel, u, c, mallet, when)
     contactSolve (double, kick-drift-kick at 48 or 96 kHz by mallet, <= 12 ms): mallet mass m, F = K d^p (1 + mu d'),
       against bar modes b1..b4, t1, t2
       -> STRIKE RECORD (slot = counter & 31): seg[len] float32 = G_bar (sum_m c_m q'_m + knock), nh, inj[8] complex
     Stage(GraphCommand{ Op = Strike, Id = slot, When, Flags = LateDrop }); Commit
   Damp(bar, Hand|Mallet, u, c, when) -> dampSolve -> DAMP RECORD (pole table pr/pi[8]); Undamp(bar); Silence
 AUDIO THREAD (UnitHooks.RenderMarimba -> MarimbaDsp.Render, Burst)
   due events (At, Seq): Strike -> a player plays seg from At, plus an "inject" action at At + nh; Damp/Undamp -> pole actions at At
   per active bar: 2 MB4 groups (lanes e+ e- b2 b3 | b4 t1 t2 -), split at its actions:
      mbRenderFree(g0) + mbRenderFree(g1) -> accResolve -> barOut -> Out[source(bar)] += barOut     (Lo / Mid / Hi)
      inject: z_lane += inj_lane;  poles: (pr, pi) <- the record or the undamped table;  mbEndBlock -> flush, energy, retire
   players: Out[source(bar)] += seg (contact sound + knock, at most 1024 samples per strike)
   -> SourceId.MarimbaLo / MarimbaMid / MarimbaHi (Meta spatializer, room-unit tap, bus) like every instrument source
```

**Why the contact runs on the main thread.**
- It needs `pow()` and a stiff nonlinear solve for up to 1152 sub-steps (12 ms at 96 kHz): 2–12 µs per strike in Node
  for the app's mallets and fingertips (2.4; C# double is similar). That is scalar work that cannot be vectorised, and on
  the audio thread a two-mallet flam would land in one block.
- On the main thread it runs once per strike, in deterministic double precision, and only float32 results cross over.
- The bar's later evolution is linear, so injecting the solved states into a bar that is already ringing is exact
  superposition. A roll on a ringing bar is right.
- The only approximation is that the nonlinear contact force ignores the bar's existing motion. That motion is at most
  0.05–0.3 mm, against 0.5–2 mm of yarn compression (Section 11).

**Why there is no click at the handoff.**
- The segment is the bar's exact output during the contact, at 48 kHz.
- The injected state is the analytic modal state at the segment's last sample: `z = j·c·V`, with V the complex modal
  velocity. The free kernel therefore continues the same waveform.
- Measured: the second difference at the handoff is 0.09–0.71 of the local maximum (check 3.12).

**Why there is no low-frequency push** (drums critic H1).
- Every mode radiates its velocity (`c·q'`), not its displacement. Velocity integrates to zero, because the bar ends at
  rest. The knock is band-passed noise.
- Measured below 30 Hz: −62 to −73 dB re total. The mean is at most 1e-4 of the RMS (3.13).
- For comparison, the platform's limit is −20 dB below 60 Hz, and the drums' v2 fix reached −22 dB.

### 2.2 The bar (`design.mjs`, `lib/fe-bar.mjs`)

**The FE model.**
- Free-free Euler–Bernoulli beam, 72 Hermite cubic elements, consistent mass.
- Thickness: `h(ξ) = h0·(1 − depth·(1 − s²)^shape)` for |s| < 1, with `s = (ξ − ½)/(width/2)` and ξ = x/L.
- Checked against theory: a uniform bar gives 1 : 2.7565 : 5.4039 : 8.933 : 13.344 : 18.638, with nodes at 0.2242 L and
  end/centre −1.645. Fletcher and Rossing give 1 : 2.756 : 5.404 : 8.933 with nodes at 0.224 L.

**Tuning, as a maker tunes** (Bork 1995; Bretos, Santamaría and Moral 1997 and 1999; Beaton and Scavone 2021). Newton
iteration solves two arch parameters for two ratios:

| Profile | Used | width | depth | shape | FE ratios | mode-1 nodes | mass / uncut |
|---|---|---|---|---|---|---|---|
| `lo` | C3–C5 | 0.5474 | 0.6691 | 0.4 (a pointed arch; a parabola cannot reach 1:4:10) | 1 : 4.026 : 9.981 : 18.12 : 27.76 | 0.188 / 0.812 | 0.701 |
| `hi` | C6 | 0.3878 | 0.5207 | 0.5 | 1 : 4.000 : 9.189 : 14.40 : 20.82 | 0.211 / 0.789 | 0.841 |

- Between C5 and C6 the profile, shapes and mass blend linearly (`hiBlend`).
- **Shipped ratios:** exactly 1 : 4 : r3.
  - r3 = 10.0 up to C5 (midi 72), easing to 9.2 at C6. Fletcher and Rossing give about 10 in the low register; Scavone
    and CCRMA give 9.2. In practice the top octave's third partial cannot be tuned.
  - r4 = 16 → 14: the untuned 4th mode, the FE value lowered for shear (UNVERIFIED).
- Undercut bars move the mode-1 node toward the ends (0.188 L against 0.224 L for a uniform bar). The cord passes there
  (`cordXi`).

**Mode shapes.**
- ψ_m(ξ) are mass-normalised (∫(h/h0)/massRel·ψ² dξ = 1). They are stored at 73 nodes per profile
  (`marimba-tables.json` `profiles.*.shapes`) and interpolated linearly.
- Physical shape: `φ_m = ψ_m/√M_bar`.
- Mode 2 is antisymmetric (zero at the centre). Modes 1 and 3 are symmetric, with centre antinodes: ψ1 = −2.23 and
  ψ3 = +3.38 at the centre, ψ1 = +1.88 at the ends (`lo`).

**Torsional modes t1 and t2.**
- Ratios 6.2 → 2.7 × f1 from C3 to C6, with t2 = 2.05·t1 (modelled, UNVERIFIED).
- Shape: `(c·b/2)·cos(nπξ)/√(ρI_p L/2)`.
- They are excited only off the centre line (c ≠ 0, across the width) and lose energy in shear (η 0.012).

**Geometry** (visual = acoustic for width and thickness):

| Quantity | C3 → C6 |
|---|---|
| Length L | 0.40 → 0.22 m, geometric taper |
| Width | 52 → 40 mm |
| Thickness h0 | 22 → 18 mm |
| Bar mass | 337 → 140 g |

- Density is 1050 kg/m³ (rosewood-like).
- The lengths are about 15–20 % shorter than a rosewood bar of this thickness would need. This is a deliberate
  seated-reach compromise and is invisible in VR. The sound uses only ξ = x/L, the masses and the tuned frequencies
  (Section 11).

**Losses per mode** (α in s⁻¹; T60 = 6.908/α):
- **Wood:** `π·f·η(f)`, with `η(f) = 0.0040·(1 + f/6000)`. Rosewood and padauk loss factors are 0.004–0.01 (Bork
  1995; Wegst 2006).
- **The cord** at the mode-1 nodes: `3.0·ψ_m(cordXi)²`. This is zero for mode 1 by construction and damps the
  overtones, as on a real instrument.
- **Radiation:** `π·f·η_rad`, with `η_rad = ρ0·c0·σ/(ρ_s·ω)`.
  - σ is a dipole strip (kb)²/(2 + (kb)²), times the length-wise cancellation of a free-free elastic mode,
    (kL)²/((kL)² + π²). Such a mode has zero net volume velocity.
  - This cancellation is why a bare bar's fundamental radiates weakly, and why marimbas need tubes.
- **Output weight per mode:** `c = √(2·α_rad)`. The pressure follows the radiated power of an energy-normalised mode.

### 2.3 The resonators and the coupled fundamental

**Tubes.**
- A quarter-wave tube, closed at the bottom, centred under each bar, its mouth 25 mm below the bar.
- Inner radius 28 → 19 mm, capped at 0.48 × the bar pitch so neighbouring tubes never touch.
- Tuning: `ft = f1·2^(sharp/1200)`. `sharp` is 0 up to C5, then rises 5 cents per semitone to +60 cents at C6.
  - Source: Stevens, "The Acoustics of Resonators". The best tone has the tube slightly above the bar; the top
    octave's tubes may sit more than a half step high and act "more like reflectors".
- Length: `L_eff = c/(4·ft)`, acoustic length `L_eff − 0.85·a`.
  - The 0.85 a end correction includes the bar over the mouth (0.61 a unflanged; UNVERIFIED).
  - That gives 632 mm (C3) → 63 mm (C6).

**Tube losses.** `1/Q = 2(ka)²/π + 2α_wall/k + 1/150 + 1/30`. The four terms are:
- radiation, with the mouth flanged by the bar;
- the Kirchhoff wall loss, `α_wall = 3.0e-5·√f/a` Np/m (Benade);
- the stopper and seams;
- the air pumped through the gap between bar and mouth (half of that loss is radiated).

That gives Q = 18.8 (C3) → 7.4 (C6). The tube radiates `c_t = √(2·α_rad,t)`.

**The coupled pair.**
- In coupled-mode form, `d/dt[a_b, a_t] = [[s_b, jg], [jg, s_t]]·[a_b, a_t]`, with s = −α + jω, energy-normalised
  amplitudes, and `g = KAPPA_G·ω`.
- `KAPPA_G = 0.0069` is fitted by `calibrate-coupling.mjs`, so that C3's bar-like mode has T60 = 2.0 s. That is the
  decay a marimba patent states at tenor C (US 4,411,187).
- At C3 this is 0.57 of critical coupling. The pair stays almost degenerate (130.83 and 130.67 Hz), so there is no
  beating.
- The treble is weakly coupled: its tubes are sharp and lossy (Stevens).
- The eigenpair (λ±, W, W⁻¹) becomes two MB4 lanes:
  - **e+**, bar-like: T60 2.01 s at C3, 0.33 s at C6;
  - **e−**, tube-like: T60 0.34 s at C3, 0.015 s at C6.
- Injection gains: `K_k = (c_b·W_bk + c_t·W_tk)·(W⁻¹)_{k,b}`, and at the handoff `z_k = j·K_k·V1`.
- Without the tube the C3 fundamental would ring 4.09 s, and C6 0.40 s. With it, 2.01 s and 0.33 s: the "louder attack,
  faster decay" of the literature.
- The tube "bloom" (the tube-like mode filling in within about 50 ms in the bass) comes out of the physics.

### 2.4 The mallet (`MALLETS`, `contactSolve`)

| Kind | Head mass | p | Contact on a rigid target at 1 m/s | μ (s/m) | Knock | Impact speed from vel |
|---|---|---|---|---|---|---|
| `medium` yarn (**the pair in the app**) | 18.5 g | 2.3 | 1.1 ms | 0.28 | 1.0 | 0.15 + 4.20·vel^1.6 m/s |
| `soft` yarn | 20 g | 2.5 | 1.8 ms | 0.35 | 0.6 | same |
| `hard` rubber | 22 g | 1.7 | 0.45 ms | 0.12 | 2.0 | same |
| `finger` (no mallet) | 20 g | 1.8 | 1.8 ms | 0.6 | 0.4 | 0.10 + 2.30·vel^1.6 m/s, then the per-bar fingertip voicing below |

**Contact stiffness.**
- `K` follows from the Hertz contact time: `Tc = (2·d_max/v)·I_p`, with `I_p = ∫₀¹ du/√(1 − u^{p+1})` and
  `d_max = ((p+1)·m·v²/(2K))^{1/(p+1)}`.
- The resulting values are:
  - medium 4.895e9;
  - soft 4.556e9;
  - hard 5.601e8;
  - finger 2.545e7 (SI units, F = K·d^p).

**Integration: kick-drift-kick (revision 2).** Each sub-step of dt = 1/(OS·48 kHz):
1. half kick with the force at the step's start: `yd −= F·dt/(2m)`, and each mode `ΔQ = −j·φ·F·dt/(2ω)`;
2. drift: `y += yd·dt`, and every mode turns by its exact damped rotation `Q ← Q·e^{s·dt}`;
3. the new force from the new state, `Fn = A(1 + μ·Δv)/(1 + A·μ·c)` with `A = K·d^p` and `c = (dt/2)(1/m + Σφ²)`: the
   Hunt-Crossley term then uses the end-of-step relative velocity, which the second half kick itself changes;
4. the other half kick with `Fn`.

- **Why:** the old semi-implicit Euler at 4× oversampling was first order. Against an OS 16 reference its worst mode
  error was 0.25–0.38 dB, and the fingertip's long contact made it the costliest stroke (46–100 µs in Node, critic c6).
  The new scheme is 2nd order, so `OS_BY = { medium 2, hard 2, soft 1, finger 1 }` is enough: the worst mode is within
  0.04 / 0.08 / 0.04 / 0.06 dB of OS 16 and the loudness within 0.01 LU (`tools/os-accuracy.mjs`).
- **Cost, Node M5, warm, median per strike over C3–C6 and vel 0.05–1:** medium 6.0 µs (worst bar 12.0), fingertip 6.7
  (14.6), hard 2.9 (4.3), soft 6.4 (20.3; renders only). The Quest estimate is about 4× (M2).
- For the three mallets the change against the revision-1 records is at most 0.4 dB on any lane (C6 soft b4, the old
  scheme's own error), so their sound is unchanged; everything was recalibrated (`calibrate.mjs`) and the test vectors
  regenerated (9.1). The fingertip changed on purpose (below).

**Measured at C4.**
- Contact time: 1.98 ms (vel 0.1), 1.29 (0.3), 0.94 (0.6), 0.77 (0.9), 0.73 ms (1.0). Peak force rises from 9 to 361 N.
- This matches the literature: heavier, softer mallets stay longer and damp the highs, and harder strokes bring out
  higher partials (Fletcher and Rossing, ch. 19, quoted in Kumar 2018).

**The fingertip (revision 2).** The critic measured a fingertip tap at −56 to −32 LUFS, 9–27 dB under the first
lesson's own accompaniment, and falling 10 dB from E4 to C5 because its 4–6 ms contact low-passed even the fundamental.
- **Physics:** a fingertip pad is now about as soft as soft yarn but lighter and lossier (20 g, p 1.8, 1.8 ms at 1 m/s,
  μ 0.6): contact 1.6–3.3 ms. Its speed map has the mallets' curve (γ 1.6), so its dynamic range (vel 0.3 → 0.9) is
  13.1 dB against the mallet's 13.4.
- **Voicing:** `fingerVoiceDb[37]` (`calibrate.mjs`, step 4) puts an ordinary 0.6 m/s tap 2 LU under an ordinary 1.2 m/s
  mallet stroke on every bar: +4.1 to +4.9 dB from C3 to C4, rising to +9.4 dB at C5 and the +14 dB cap from F5.
  Above F#5 the cap binds and a fingertip falls 3–9 LU under the mallet: a fingertip barely sounds a top-octave bar, and
  its knock dominates.
- **Measured (check 3.23):** −2.0 LU on every bar C3–F5, −3.3 at F#5; E4 taps at 0.25 / 0.4 / 0.6 / 0.9 m/s give
  −37.2 / −30.7 / −25.9 / −21.6 LUFS (critic c7; were −56.2 / −45.4 / −38.0 / −31.9).
- It is still the dullest implement (centroid 280 Hz at C4 against soft yarn's 336, check 3.18).

**Strike position.** u (along the bar, 0 = near end) and c (across, −1..1) enter only through φ_m(u, c).

**The knock** (the wood "tock").
- It stands in for the dense unmodelled high bar modes.
- Seeded xorshift32 noise is shaped by the force envelope, then a 1.5 ms tail, then an RBJ band-pass at
  2200·(f1/261.6)^0.3 Hz with Q 1.1. Level: `0.90·G·knock·√(F_max/20)·0.05`.
- It sits −24 dB re the contact sound for medium mf, −22 dB for hard ff, and −24 dB for a fingertip.
- Seed: `hash(unitSeed, strikeCounter)` (`seedOf`). It is deterministic and re-seeded by `SeedAll`.

### 2.5 Damping (`dampSolve`)

The added decay per lane is normalised to each mode's most sensitive spot. The poles are then recomputed in double.

| Kind | Added α (s⁻¹) | Measured |
|---|---|---|
| Hand: palm or fingers flat, spanning u ± 0.12 | `35·mean ψ²(span)/max` | C4 down 57 dB within 150 ms, −40 dB at about 105 ms. No click: a pole change only bends the envelope |
| Mallet held down (dead stroke), a point at u | `60·ψ²(u)/max` | Detected 200 ms after the stroke (5.4); C4 is at −40 dB 55 ms after the mute (255 ms after the stroke), and 50 dB under the free note 100 ms after the mute |

- **The coupled pair** gets the added decay weighted by its bar participation `partBar`: 0.91 for e+ and 0.09 for e−
  at C3.
- **Torsion** gets the full A for a hand, and `A·(c² + 0.25)` for a mallet.
- **`Undamp`** restores the stored undamped poles.

### 2.6 Constants (every one; `design.mjs` `D`, `marimba-engine.mjs` `M`)

| Constant | Value | Source or reason |
|---|---|---|
| Range | midi 48–84, bar i = midi − 48 | Section 0 |
| `LEN` | 0.40 → 0.22 m, geometric | Seated reach (5.5) |
| `WID`, `THK` | 52 → 40 mm, 22 → 18 mm, linear | Graded like concert marimbas |
| `GAP` | 6 mm between naturals | Smaller than the 34 mm head, so a mallet never falls between bars |
| `ROW_GAP`, `ACC_RAISE` | 25 mm, 22 mm | Two-row layout |
| `RHO` | 1050 kg/m³ | Rosewood 0.8–1.1 g/cm³ (Wegst 2006) |
| `R2`, `R3`, `R4` | 4; 10 → 9.2 (from C5); 16 → 14 | 2.2 |
| `T1`, `T2/T1` | 6.2 → 2.7; 2.05 | Modelled (UNVERIFIED) |
| `ETA0`, `F_ETA`, `ETA_TORS` | 0.0040, 6000 Hz, 0.012 | Wood loss factors (Bork 1995) |
| `CORD_ALPHA` | 3.0 s⁻¹ | Mode-2 T60 = 0.24–0.38 × mode 1 |
| `TUBE_A` | 28 → 19 mm | ≤ 0.48 × the bar pitch |
| `END_CORR`, `WALL_K`, `Q_EXTRA`, `Q_GAP`, `GAP_RAD`, `RAD_FLANGE` | 0.85 a, 3.0e-5, 150, 30, 0.5, 2 | 2.3 (UNVERIFIED parts marked there) |
| `TUBE_SHARP` | 0 up to C5, then +5 cents per semitone | Stevens |
| `KAPPA_G` | 0.0069 | Fitted so the C3 T60 is 2.0 s |
| `MAX_HZ` | 19.2 kHz (0.4 sr) | No aliasing. The highest live mode is 14.65 kHz (C6 b4) |
| `SRC_X`, `SRC_SPLIT` | −0.37 / 0 / +0.38 m; midi < 60 → Lo, < 72 → Mid, else Hi | One point source per bar (0.5) |
| `GAIN` | 0.952676 | `calibrate.mjs`: C4 at vel .6 matches the new piano's C4 at vel .6 (first 0.4 s, −21.7 LUFS dry) |
| `voiceDb[37]` | −1.42 … +1.66 dB | Even register: flat C4–C5, −1.5 dB at C3, −2.5 dB at C6 |
| `fingerVoiceDb[37]` | +4.1 … +14.0 dB (cap) | A 0.6 m/s fingertip tap 2 LU under a 1.2 m/s mallet stroke on every bar (2.4) |
| `OS_BY`, `MAX_CONTACT_S` | medium 2, hard 2, soft 1, finger 1; 12 ms | Contact solve, kick-drift-kick (2.4) |
| `SEG_MAX`, `KNOCK_TAIL` | 1024, 192 samples | Strike record |
| `STRIKE_SLOTS`, `DAMP_SLOTS`, `PLAYERS`, `PENDING` | 32, 64, 16, 128 | 32 slots cover over 1 s of the fastest playing (30 strokes/s), against at most 112 ms that one record is in use |
| `MAX_ACTIVE_BASE` | 24 bars at Budget 1200 | Scaled by `Budget/1200·Share`, clamped 8..37; the quietest bar fades over one block |
| `HAND_ALPHA`, `MALLET_REST_ALPHA`, `HAND_SPAN` | 35, 60 s⁻¹, ±0.12 | 2.5 |

### 2.7 Per-bar table (`out/marimba-tables.json` has everything to 10 digits)

- x, z: the bar centre, instrument-local (4.1).
- top: the bar top at the default H = 0.70.
- T60 fund: the coupled bar-like mode.
- voice: per-bar gain in dB; finger: the fingertip's extra per-bar voicing in dB (2.4).
- lanes live: out of 8.
- source: the SourceId the bar plays on.

| i | bar | f1 Hz | L mm | width mm | h0 mm | mass g | x mm | z mm | top m | tube r mm | tube acoustic L mm | tube sharp c | Q tube | T60 fund (s) | T60 no tube | voice dB | finger dB | lanes live | source |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | C3 | 130.81 | 400 | 52.0 | 22.0 | 337 | -542.0 | 0.0 | 0.700 | 28.0 | 632 | 0 | 18.8 | 2.01 | 4.09 | -0.71 | 4.9 | 7 | Lo |
| 1 | C#3 | 138.59 | 393 | 51.7 | 21.9 | 327 | -513.2 | 421.7 | 0.722 | 27.7 | 595 | 0 | 18.8 | 1.90 | 3.86 | -0.65 | 4.7 | 7 | Lo |
| 2 | D3 | 146.83 | 387 | 51.3 | 21.8 | 318 | -484.3 | 0.0 | 0.700 | 27.4 | 561 | 0 | 18.8 | 1.79 | 3.63 | -0.56 | 4.6 | 7 | Lo |
| 3 | D#3 | 155.56 | 381 | 51.0 | 21.7 | 309 | -455.8 | 408.7 | 0.722 | 27.1 | 528 | 0 | 18.7 | 1.69 | 3.42 | -0.46 | 4.4 | 7 | Lo |
| 4 | E3 | 164.81 | 374 | 50.7 | 21.6 | 301 | -427.3 | 0.0 | 0.700 | 26.8 | 497 | 0 | 18.6 | 1.60 | 3.22 | -0.38 | 4.3 | 7 | Lo |
| 5 | F3 | 174.61 | 368 | 50.3 | 21.4 | 292 | -370.8 | 0.0 | 0.700 | 26.5 | 469 | 0 | 18.5 | 1.51 | 3.03 | -0.35 | 4.2 | 7 | Lo |
| 6 | F#3 | 185.00 | 362 | 50.0 | 21.3 | 284 | -342.8 | 390.1 | 0.722 | 26.2 | 442 | 0 | 18.4 | 1.43 | 2.85 | -0.28 | 4.2 | 7 | Lo |
| 7 | G3 | 196.00 | 356 | 49.7 | 21.2 | 276 | -314.8 | 0.0 | 0.700 | 26.0 | 415 | 0 | 18.3 | 1.35 | 2.68 | -0.20 | 4.1 | 7 | Lo |
| 8 | G#3 | 207.65 | 350 | 49.3 | 21.1 | 268 | -287.2 | 378.2 | 0.722 | 25.7 | 391 | 0 | 18.2 | 1.28 | 2.53 | -0.17 | 4.1 | 7 | Lo |
| 9 | A3 | 220.00 | 344 | 49.0 | 21.0 | 261 | -259.5 | 0.0 | 0.700 | 25.4 | 368 | 0 | 18.1 | 1.21 | 2.38 | -0.13 | 4.1 | 7 | Lo |
| 10 | A#3 | 233.08 | 339 | 48.7 | 20.9 | 253 | -232.2 | 366.6 | 0.722 | 25.1 | 346 | 0 | 17.9 | 1.15 | 2.23 | -0.09 | 4.2 | 7 | Lo |
| 11 | B3 | 246.94 | 333 | 48.3 | 20.8 | 246 | -204.8 | 0.0 | 0.700 | 24.9 | 326 | 0 | 17.7 | 1.09 | 2.10 | -0.06 | 4.2 | 7 | Lo |
| 12 | C4 | 261.63 | 328 | 48.0 | 20.7 | 239 | -150.7 | 0.0 | 0.700 | 24.6 | 306 | 0 | 17.5 | 1.03 | 1.97 | -0.09 | 4.3 | 7 | Mid |
| 13 | C#4 | 277.18 | 322 | 47.7 | 20.6 | 232 | -123.8 | 350.0 | 0.722 | 24.3 | 289 | 0 | 17.3 | 0.98 | 1.85 | -0.20 | 4.4 | 7 | Mid |
| 14 | D4 | 293.66 | 317 | 47.3 | 20.4 | 226 | -97.0 | 0.0 | 0.700 | 24.1 | 271 | 0 | 17.1 | 0.93 | 1.74 | -0.35 | 4.6 | 7 | Mid |
| 15 | D#4 | 311.13 | 312 | 47.0 | 20.3 | 219 | -70.5 | 339.4 | 0.722 | 23.8 | 256 | 0 | 16.8 | 0.88 | 1.63 | -0.52 | 4.8 | 7 | Mid |
| 16 | E4 | 329.63 | 307 | 46.7 | 20.2 | 213 | -44.0 | 0.0 | 0.700 | 23.6 | 240 | 0 | 16.5 | 0.84 | 1.53 | -0.65 | 5.0 | 7 | Mid |
| 17 | F4 | 349.23 | 302 | 46.3 | 20.1 | 207 | 8.5 | 0.0 | 0.700 | 23.3 | 226 | 0 | 16.2 | 0.79 | 1.44 | -0.75 | 5.4 | 7 | Mid |
| 18 | F#4 | 369.99 | 297 | 46.0 | 20.0 | 201 | 34.5 | 324.1 | 0.722 | 23.1 | 212 | 0 | 15.9 | 0.76 | 1.35 | -0.84 | 5.7 | 7 | Mid |
| 19 | G4 | 392.00 | 292 | 45.7 | 19.9 | 195 | 60.5 | 0.0 | 0.700 | 22.8 | 199 | 0 | 15.5 | 0.72 | 1.26 | -0.93 | 6.2 | 7 | Mid |
| 20 | G#4 | 415.30 | 287 | 45.3 | 19.8 | 189 | 86.2 | 314.4 | 0.722 | 22.6 | 187 | 0 | 15.2 | 0.68 | 1.18 | -1.03 | 6.7 | 7 | Mid |
| 21 | A4 | 440.00 | 282 | 45.0 | 19.7 | 184 | 111.8 | 0.0 | 0.700 | 22.3 | 176 | 0 | 14.8 | 0.65 | 1.11 | -1.15 | 7.3 | 7 | Mid |
| 22 | A#4 | 466.16 | 278 | 44.7 | 19.6 | 178 | 137.2 | 304.9 | 0.722 | 22.1 | 165 | 0 | 14.4 | 0.62 | 1.04 | -1.27 | 7.9 | 7 | Mid |
| 23 | B4 | 493.88 | 273 | 44.3 | 19.4 | 173 | 162.5 | 0.0 | 0.700 | 21.9 | 155 | 0 | 14.0 | 0.59 | 0.97 | -1.36 | 8.6 | 7 | Mid |
| 24 | C5 | 523.25 | 269 | 44.0 | 19.3 | 168 | 212.7 | 0.0 | 0.700 | 21.6 | 146 | 0 | 13.5 | 0.56 | 0.91 | -1.42 | 9.4 | 7 | Hi |
| 25 | C#5 | 554.37 | 264 | 43.7 | 19.2 | 166 | 237.5 | 291.3 | 0.722 | 21.4 | 136 | 5 | 13.0 | 0.54 | 0.85 | -1.34 | 10.1 | 7 | Hi |
| 26 | D5 | 587.33 | 260 | 43.3 | 19.1 | 164 | 262.3 | 0.0 | 0.700 | 21.2 | 127 | 10 | 12.5 | 0.51 | 0.79 | -1.17 | 11.0 | 7 | Hi |
| 27 | D#5 | 622.25 | 255 | 43.0 | 19.0 | 161 | 286.8 | 282.6 | 0.722 | 20.9 | 119 | 15 | 12.0 | 0.50 | 0.74 | -0.97 | 11.9 | 7 | Hi |
| 28 | E5 | 659.26 | 251 | 42.7 | 18.9 | 159 | 311.3 | 0.0 | 0.700 | 20.7 | 111 | 20 | 11.5 | 0.48 | 0.69 | -0.70 | 12.9 | 7 | Hi |
| 29 | F5 | 698.46 | 247 | 42.3 | 18.8 | 157 | 359.8 | 0.0 | 0.700 | 20.5 | 104 | 25 | 11.0 | 0.46 | 0.65 | -0.41 | 14.0 | 7 | Hi |
| 30 | F#5 | 739.99 | 243 | 42.0 | 18.7 | 154 | 383.8 | 270.1 | 0.722 | 20.3 | 97 | 30 | 10.5 | 0.44 | 0.60 | -0.21 | 14.0 | 7 | Hi |
| 31 | G5 | 783.99 | 239 | 41.7 | 18.6 | 152 | 407.8 | 0.0 | 0.700 | 20.1 | 90 | 35 | 9.9 | 0.42 | 0.56 | 0.07 | 14.0 | 7 | Hi |
| 32 | G#5 | 830.61 | 235 | 41.3 | 18.4 | 150 | 431.5 | 262.1 | 0.722 | 19.8 | 84 | 40 | 9.4 | 0.40 | 0.53 | 0.52 | 14.0 | 7 | Hi |
| 33 | A5 | 880.00 | 231 | 41.0 | 18.3 | 147 | 455.2 | 0.0 | 0.700 | 19.6 | 78 | 45 | 8.9 | 0.39 | 0.49 | 0.91 | 14.0 | 7 | Hi |
| 34 | A#5 | 932.33 | 227 | 40.7 | 18.2 | 145 | 478.5 | 254.3 | 0.722 | 19.4 | 73 | 50 | 8.4 | 0.37 | 0.46 | 1.18 | 14.0 | 7 | Hi |
| 35 | B5 | 987.77 | 224 | 40.3 | 18.1 | 142 | 501.8 | 0.0 | 0.700 | 19.2 | 68 | 55 | 7.9 | 0.35 | 0.43 | 1.43 | 14.0 | 7 | Hi |
| 36 | C6 | 1046.50 | 220 | 40.0 | 18.0 | 140 | 548.0 | 0.0 | 0.700 | 19.0 | 63 | 60 | 7.4 | 0.33 | 0.40 | 1.66 | 14.0 | 7 | Hi |

**Lane examples (frequency in Hz / T60 in s):**

| Bar | e+ | e− | b2 | b3 | b4 | t1 | t2 |
|---|---|---|---|---|---|---|---|
| C3 | 130.8 / 2.01 | 130.7 / 0.345 | 523.3 / 0.759 | 1308.1 / 0.257 | 2093 / 0.151 | 811 / 0.221 | 1663 / 0.107 |
| C4 | 261.6 / 1.03 | 261.9 / 0.158 | 1046.5 / 0.349 | 2616.3 / 0.118 | 4012 / 0.071 | 1229.5 / 0.145 | 2520 / 0.071 |
| C6 | 1046.2 / 0.328 | 1082.5 / 0.015 (the sharp tube's own short ring) | 4186 / 0.068 | 9628 / 0.021 | 14651 / 0.011 | 2826 / 0.063 | 5792 / 0.031 |

---------------------------------------------------------------------------------------------------------------------------

## 3. Objective verification (`node verify.mjs` → `out/verify.json`; 23/23 pass)

The model is consistent with the literature on these points (sources in Section 12):
- the tuning ratios;
- decays that shorten with pitch;
- a resonator that shortens the fundamental while making it louder;
- upper partials that die first;
- brighter tone from harder and faster strokes;
- stronger partials whose antinode lies at the strike point;
- heavier, softer mallets that damp the highs.

| # | Check | Result |
|---|---|---|
| 3.1 | FE arch reaches 1:4:10 within 1 % | 1 : 4.026 : 9.981 (`lo`); a uniform beam reproduces theory to 4 digits |
| 3.2 | Fundamentals within 3 cents of A440 equal temperament (measured from renders) | +0.28 … −0.60 cents (C3 … C6) |
| 3.3 | Measured 2nd partial within 1.5 % of 4.0 | 3.999–4.001. 3rd partial: 9.998–10.000 up to C5, 9.603 (F#5), 9.203 (C6) |
| 3.4 | C3 fundamental T60 between 1.6 and 2.6 s (the patent's ~2 s at tenor C) | 2.01 s |
| 3.5 | Fundamental T60 falls monotonically with pitch | 2.01, 1.43, 1.03, 0.755, 0.56, 0.439, 0.328 s (C3, F#3 … C6) |
| 3.6 | The resonator shortens the fundamental in every register | 4.09 … 0.40 s without the tube; 2.01 … 0.33 s with it |
| 3.7 | Upper partials die first: measured p2 < 0.5 × p1 everywhere; design b4 < b3 < b2 < e+ on all 37 bars | p2/p1 = 0.38 (C3) → 0.24 (C6) |
| 3.8 | The spectrum darkens as the note rings | Centroid at 20 / 60 / 150 / 400 ms: C3 664 → 231 → 142 → 131 Hz; C4 333 → 262 Hz |
| 3.9 | Louder AND brighter with speed; contact shortens | C4 at vel .1/.3/.6/.9/1: −39.7/−29.9/−21.7/−16.5/−15.1 LUFS; centroid 269/351/485/651/716 Hz; contact 1.98/1.29/0.94/0.77/0.73 ms |
| 3.10 | Dynamic range vel .3 → .9 within 2 dB of the new piano | 13.44 dB vs 14.48 dB |
| 3.11 | Register loudness within ±2 dB of the voicing tilt | 37/37 bars. C3 −23.16, C4 −21.70, C5 −21.61, C6 −24.19 LUFS |
| 3.12 | No step at the contact handoff (2nd difference ≤ 1.2 × the local maximum) | 0.09–0.71, over six bar / mallet / velocity cases |
| 3.13 | No low-frequency push (drums critic H1) | Below 30 Hz: −61.7 … −73.2 dB; mean/RMS ≤ 1.0e-4 |
| 3.14 | No aliasing | Highest live mode 14.65 kHz (< 19.2). Above 20 kHz: −149 dB (C6, hard, edge) |
| 3.15 | No subnormal state; every bar retires to exact zero | 37 ff strokes, then 30 s of silence: 0 subnormals, 24 retired (13 capped), the last second all zero |
| 3.16 | 60 s random workout: finite, peak < 2 | 709 events (all mallets, damps, undamps): peak 1.84, 0 players dropped |
| 3.17 | A centre strike has no 4th partial; a node strike has a weak fundamental | u .5: p2 −137 dB re p1. u .19 (the cord node): p1 40 dB below u .45 |
| 3.18 | Mallets order brightness and contact: finger < soft < medium < hard | Centroid 280/336/485/1095 Hz; contact 2.06/1.56/0.94/0.44 ms; −23.2/−21.9/−21.7/−18.4 LUFS (all at vel .6) |
| 3.19 | A resting hand mutes 25–60 dB within 150 ms (quick, but not a cut) | 56.6 dB |
| 3.20 | Dead stroke, muted at the 200 ms detection (5.4): −40 dB within 120 ms of the mute and 350 ms of the stroke; ≥ 25 dB under the free note 100 ms after the mute | −40 dB 55 ms after the mute (255 ms after the stroke); −81.3 vs −30.9 dB |
| 3.21 | A roll sustains without running away (−3 … +6 dB) | 12 strokes/s on G4: −0.67 dB, first 250 ms → 0.8–1.3 s |
| 3.22 | No metallic tail: after 0.3 s everything but the fundamental is ≥ 12 dB down (hard ff) | −30.2 (C3), −38.7, −47.3, −51.3, −53.7 dB (C5) |
| 3.23 | The fingertip (critic H2b): a 0.6 m/s tap within 4 LU of a 1.2 m/s mallet stroke on every bar G3–F#5 and never louder; its dynamic range within 2 dB of the mallet's | −1.98 … −3.25 LU (−2.0 on C3–F5); range 13.13 vs 13.43 dB. Above F#5 the +14 dB cap binds: −4.5 (G5) … −8.9 LU (C6) |

### 3.1 Platform conformance (platform.md 10.3; `tools/parity.mjs`, `bench.mjs`)

| Rule | Limit | Marimba |
|---|---|---|
| CPU, Quest Burst estimate | ≤ 0.20 ms average / 0.30 ms worst per block | 0.032 / 0.066 ms with all 37 bars ringing; 0.020 / 0.053 seated worst; Node ≤ 10× the cap ✓ (381 µs average) |
| Budget scaling | reads `Budget/1200·Share` | The active-bar cap `round(24·Budget/1200·Share)` clamped 8..37; `Work` = live lanes rendered. With 30 bars ringing, Share 0.4 lowers the steady Work from 168 to 70 lanes (**−58 %**; `tools/share-check.mjs`). TV6 shows the cap at work |
| No low-frequency push | < 60 Hz ≤ −20 dB re the hit; mean/RMS ≤ 0.01 | ≤ −26.8 dB below 60 Hz with a 4th-order low-pass (dominated by its skirt at the 131 Hz fundamental); true sub-30 Hz content −57 … −66 dB; mean/RMS ≤ 1e-4 |
| No metallic tails | Wood rule: the top 20 partials ≤ 35 % of 0.3–9 kHz after 0.5 s. Metal rule: no partial above 3 kHz outlives 0.8 × the fundamental's T60 | The wood rule does not fit a **tuned** idiophone, whose tail is its own fundamental (100 % in band for D#4 and up). We meet the metal rule (worst ratio 0.232, G5 b2) and 3.22. **Request R2:** read the marimba's tail rule as metal rule + 3.22 |
| Not over-wet | DRR at the seat within ±2 dB of the piano's +6.1 dB (≥ +4.1) | **Pass with the room sends of request R1 (Lo −2 dB, Mid 0, Hi −2 dB): Lo +4.79, Mid +5.67, Hi +4.32 dB** (mean of the ears; `out/parity.json`). At send 0 the lateral sources measured +2.79 / +2.32 (critic: fail): they sit 0.88 m away and 0.37 m to the side, so the far ear's head shadow takes their direct sound down to −1.7 dB. For fairness, the piano's own PianoLo / PianoHi (±0.42 m, 0.78 m) measure +4.08 dB in the same harness, at the rule's edge; PianoMid +6.08 |
| Loudness parity | the mf reference phrase (Ode bar 1, vel 0.55, dry) within ±2 LU of the piano | −24.14 vs −24.01 LUFS: **−0.14 LU** |
| ff true peak through MasterBusV2 | ≤ −1 dBTP | −1.00 dBTP for C3 + C4 + C5 together, hard mallets at vel 1, room on (the master's own ceiling; limiter gain reduction 2.9 dB). With the app's medium mallets the same chord peaks at −3.76 dBTP with no limiting, and a single vel-1 stroke at −2.0 dBTP dry, before distance and HRTF. The hard rubber mallet (renders only, not in the app) peaks at +4.8 dBTP dry, and the master holds it |
| Clicks | step ≤ the signal's own maximum + 1 % at onset, steal, release, resume | Handoff 3.12; cap fade = a linear ramp over one block; damp = a pole change (continuous); Silence fades over one block; resume = `Reset` zeroes the state before a silent block |
| Denormals, late onsets, allocation, determinism | – | 3.15; `LateDrop` on Strike only; 0 B (preallocated records and scratch); TV vectors bit-exact managed, fused ≤ 2.9e-6 |
| Memory | ≤ 2 MiB arena, 4 MiB baked | 151 KiB, 0 baked |

---------------------------------------------------------------------------------------------------------------------------

## 4. Visual spec (module `MusicRoom.Marimba`, `Assets/MusicRoom/Marimba`)

### 4.1 Frame, dimensions, coordinates and the descriptor's seat

**Instrument-local frame.**
- The origin is on the floor under the natural row's centre line, at the middle of the natural row (between E4 and F4).
- Unity axes: +x is the player's right, +z points away from the player (the facing rule), y is up.

**Natural bars.**
- Centres at z = 0; tops at H = 0.70 m (lift range 0.69–0.82).
- Near ends at z = −0.200 (C3) … −0.110 (C6).
- x runs from −0.568 to +0.568: the **natural row is 1.136 m wide**.

**Accidental bars.**
- Tops at H + 0.022.
- Near ends at z = 0.225 (C#3) … 0.141 (A#5); far ends at 0.618 … 0.368.
- Each sits centred over the gap between its two naturals. There is no sharp between E–F or B–C.

**Footprint.**
- Bars: 1.136 m × 0.818 m at the bass end, narrowing to 0.48 m deep at the treble end.
- Frame: **1.24 m wide**, 0.86 m deep at the bass end and 0.55 m at the treble end (z −0.24 … +0.64); with the mallet
  tray in front, z −0.26 … +0.64.
- Height to the sharps' tops: 0.722 m.

**Descriptor (`MarimbaDefinition`, replaces the provisional `registry.mjs` entry).**
- Ids from platform.md 2: Kind 7, Rig 7, Unit 7, Module 35, TickOrder 23, id `marimba`, carousel 5.
- Traits: `Playable | Pitched | UsesTools | FastMotion | HasStand`. Tools: `ToolMask.Mallet`; sticks do not play the
  marimba.
- **Seat**:
  - `CanonicalSlot Front`; `Prefs` Front, Left, Right; `Priority` 3rd (after piano and drums, as in the platform).
  - `ReachMax 0.80`; `PlayCentre (0, 0.05)`.
  - Home All = HomeSingle = seat-frame **(0, 0, 0.50) yaw 0**: the natural centre line 0.50 m ahead of the eyes, the
    natural near ends 0.30–0.39 m ahead.
  - `Boxes`, both with y 0–0.745 (they replace the platform's provisional 0.60 × 0.24 box, which was centred at cz 0):
    - A: naturals, their tubes, the front rails and the tray, `cx 0, cz −0.005, hx 0.62, hz 0.255` (z −0.26 … +0.25);
    - B: sharps, their tubes and the back rails, `cx −0.03, cz 0.43, hx 0.58, hz 0.215`.
  - `LiftMin −0.01`, `LiftMax +0.12` (H 0.69–0.82; the legs telescope like the piano's `FitStand`; the C3 tube needs
    H ≥ 0.69).
- **Stow**:
  - `window-near`, floor, (2.71, −0.80, 90): "Under the window".
  - The bar tops are 0.69–0.72 m, which meets the platform's "nothing taller than 0.85 m before the glass" rule. The
    platform's wish of bars "below the sill line" (0.63 m) cannot be met: the C3 tube needs 0.69 m (request R3).
  - The mallets lie on their tray, not on the bars: resting mallets would mute bars.
- **Audio**: `Unit 7`; `CapAvgMs 0.20`, `CapWorstMs 0.30`; `ArenaMiB 0.15`; `BakedMiB 0`; `ErOrigin` rig-local
  (0, 0.55, 0.15).
- **Lessons**: `Travel 0.30`, `LeadSeconds 1.667`, `Shape Bar`, `Notation Grand`, `Pack "marimba"`, `CountNoun "notes"`.
- **Mr**: `OnTable false` (a floor-only lead).
- **Shadow**: `(0, 0.2)`, `W 1.30`, `D 0.95`, `Opacity 0.45`.
- **Budgets**: `DrawsAtSeat 5`, `DrawsStowed 3`, `TrisAtSeat ≈ 14 k`, `TrisStowed ≈ 5 k`.
- **Events**: see 5.6. **Features and Tips**: see 8.

### 4.2 Procedural geometry and materials (built in code, outward winding as in `MeshKit`)

| Part | Geometry | Material template |
|---|---|---|
| **Bars** (37, one mesh) | See "Bar geometry" below | `Marimba/Rosewood` (URP Lit, smoothness 0.55, baked grain) |
| **Cord** (2 per row) | Dark red nylon, r 2.5 mm: a polyline through the cord holes and down around each post | In the Metal mesh (atlas strip) |
| **Posts** | Brass pins, r 2 mm, 14 mm high, 2 per gap per row, on the rails | `Marimba/Metal` |
| **Rails** | 4 straight hardwood rails (35 × 22 mm) under the node lines: natural front and back, sharp front and back, from end board to end board | `Marimba/FrameWood` (an atlas: dark walnut stain + felt + mallet shaft + yarn) |
| **End boards** | Bass 0.86 × 0.30 × 0.03 m, treble 0.55 × 0.22 × 0.03 m; arched top edge, rounded corners | FrameWood |
| **Legs** | 4 turned legs, r 22 mm; a stretcher bar between the end boards at 0.20 m | FrameWood |
| **Resonators** | 37 tubes centred under each bar: r 28 → 19 mm, wall 1 mm, top 25 mm under the bar. Every tube is drawn at its acoustic length (C3 632 mm, its bottom 21 mm above the floor at H 0.70) except the ten shorter than 120 mm (D#5 and E5–C6, 63–119 mm): these are drawn 120 mm long with an inner stopper at the acoustic length (the common "cosmetic tube" design), so the treble bottoms line up at 0.54–0.56 m. A thin brass rim at each mouth | `Marimba/Metal` (dark bronze, metallic 0.8, smoothness 0.55, brushed stripes baked) |
| **Mallet tray** | A felt-lined walnut tray, 0.82 × 0.05 m, 15 mm deep, centred at x 0, z −0.235, y H − 0.04, hung from the front natural rail on two brass brackets at x ±0.30. The two mallets lie end to end in one groove, heads outward, butts 20 mm apart: each mallet is 0.387 m from butt to head edge, so the heads (to x ±0.402) rest inside the tray's ends (±0.41) and the shafts never overlap | FrameWood |
| **Mallets** (2) | Rattan shaft, r 4.5 mm × 0.37 m to the head centre; yarn head an oblate sphere, 34 × 31 mm, dark grey-green wool | FrameWood atlas (shaft and yarn regions; smoothness from the albedo alpha: yarn 0.2, shaft 0.45) |

**Bar geometry.**
- Each bar is an extruded profile with a slightly crowned top (0.6 mm) and 2 mm rounded edges.
- Underneath is the arch from `profiles.lo/hi`, in 16 segments. It is visible from the side and in MR.
- Cord holes are drilled across at `cordXi` and `1 − cordXi`.
- 120 vertices per bar, 4440 in all.

**Textures** (baked by `ITextureBakeProvider`; generated, no assets):
- rosewood grain, 512 × 128: deep red-brown #5a2a1c to #8a4a30 with dark streaks along x, seeded;
- brushed bronze, 256 × 64;
- the FrameWood atlas, 512 × 256:
  - walnut;
  - felt #3a3430;
  - shaft #c9a77a;
  - yarn #4a5248 with fibre noise.

### 4.3 Draw calls: 5 at the seat, 3 on display (the platform allows ≤ 6 and ≤ 3)

| Draw | At the seat | Moves? | On display |
|---|---|---|---|
| 1 | All 37 bars (one dynamic mesh) | Only the vertex range of a moving bar is rewritten (`Mesh.SetVertexBufferData`, start + count) | Bars (static) |
| 2 | Frame wood: rails, end boards, legs, stretcher, tray | Static (rig transform) | Frame + both mallets, combined (one material) |
| 3 | Metal: 37 tubes, posts, cord, mouth rims | Static | Metal |
| 4 | Left mallet (FrameWood material, its own transform) | Yes | (in draw 2) |
| 5 | Right mallet | Yes | (in draw 2) |

**Roles of the parts.**
- `Rig.Handles` are hidden renderers with live bounds (the drums pattern): the end boards, the rails and the tubes.
- `PlayedParts` are the bars and the mallets.
- Desktop clicks use one thin box collider per bar, on `Layers.PlaySurface`.

**Triangles.**
- At the seat: about 14 k (bars 7.4 k, tubes 3.5 k, frame 2 k, mallets 0.6 k).
- On display: about 5 k (the display LOD has no arch and 8-sided tubes).

### 4.4 What moves when played (physical feedback; no glow, no hover glow)

- **The struck bar dips on its cord and rocks back.**
  - The vertical offset is `d = 1.6 mm·vel·sin-ramp(1 frame)` on the hit frame. It then returns on a spring
    `ωn = 2π·11 Hz, ζ = 0.55`, in 2 ms substeps.
  - A hit off the centre line adds a roll about the bar's long axis of `0.6°·c·vel`, on the same spring.
  - Real bars move about 0.2 mm, so this is a gentle, readable exaggeration of a real motion. Vertices are written only
    while the bar moves.
- **The mallet head rebounds** like the E2 sticks (experience.md). The drawn head is clamped onto the bar top and never
  goes through it. A grip spring is kicked by the measured approach speed. During a dead stroke or a mallet rest, the
  drawn head stays pressed on the bar.
- **Contact shadows (E3) are the aiming cue.** The mallet heads are shadow spheres (`IHandToolProvider.ShadowSpheres`)
  over the bar tops (`IContactSurface`). They replace a hover glow. A free mallet on the tray has no glow either.
  Sunny's arrival tip demonstrates the pinch.
- **Fingertips** touching a bar are lifted onto its surface (E1).
- **The lesson target** (`ILessonTargets.ShowTarget`) lifts the target bar `3 mm·k` on its cord. There is no tint:
  URP Lit templates have no per-vertex colour. The falling block lands on it.

### 4.5 Mixed reality

- The marimba is a floor-only lead (platform 9.3). It stands on the real floor in front of the player, and the others
  are placed around it (`PlaceAroundLead`).
- Its tubes and frame are opaque and occlude passthrough.
- With the E6 `PassthroughCutout` look, the real hands draw over the virtual mallets' grips, so a mallet appears to be
  held.
- **Known visual compromise.**
  - A seated player's knees reach 0.45–0.55 m ahead of the eyes, at a height of 0.55–0.60 m, about ±0.12 m from the
    centre line. The natural tubes hang at 0.50 m ahead of the eyes (their front 0.476 m).
  - The tubes in the knee span, B3–C5 (x −0.21 … +0.21), end at 0.33–0.51 m (B3 0.33, C4 0.35, E4 0.42, G4 0.46,
    A4 0.48, C5 0.51 m), all below the top of a knee, so they can intersect the knees. The treble's cosmetic tubes end at
    0.54–0.56 m and are outside the knee span.
  - This is invisible in VR (no legs are drawn). In MR the player can move the marimba 10 cm further away in arrange
    mode, which clears 0.55 m knees (Section 11).
- Audio: the room unit's `SetMixedReality` applies, as for every source.

---------------------------------------------------------------------------------------------------------------------------

## 5. Interaction spec (bare hands; `MarimbaPlay`, reference `interaction.reference.mjs`, constants `IX`)

### 5.1 The mallets (the drumsticks' behaviour, on the platform's tool system)

**At rest.**
- Two mallets lie end to end on the tray (4.2), parallel to x, heads outward: the left mallet's butt at x = −0.010, its
  grip at x = −0.110 and its head centre at x = −0.380; the right mallet mirrored. Each hand finds its own, 11 cm either
  side of the centre.

**The provider.**
- `MalletToolProvider : IHandToolProvider`, with `Owner = Marimba`.
- `NearestFree(tip)` is the distance to the nearest free mallet's shaft, or +∞ when the marimba is not at the seat.
- `TryPickUp` follows `DrumSticks.TryGrab`:
  - the pinch point is the thumb-index midpoint, or the index tip when untracked;
  - `Grabbed` within 0.12 m of a shaft; `Near` within the 0.24 m dead zone, where the pinch is consumed and never grabs
    the rig.
- `AnyHeld`, `ReleaseAll`, and `ShadowSpheres` (the head centres, r 17 mm).
- Layout calls `TryPickUp` only on the nearest provider (platform 4.6).

**The pinch.**
- The grab pinch is thumb-index OR Meta's own index pinch (strength 0.85 on, 0.5 off; u-arrange `HandState.GrabPinch*`).
  The OpenXR tip joint centres are about 17 mm apart when the tips touch, so the 15 mm thumb-index rule alone starts
  late and flickers.
- A closing fist also picks up while the hand is not playing.

**The grip.**
- Same as `StickGrip`: grip = the thumb-index midpoint, forward = wrist → grip, drooped −0.22 rad (12.6°) toward the
  palm.
- The butt is 0.10 m behind the grip. The head centre is 0.27 m beyond it: the same lever as a stick, so the mallets
  feel the same. Smoothing 1 (snap).
- The marimba must not reference `Drums`, so it needs `HandToolMath` in Core (request R4). Fallback: a byte-for-byte
  copy in `MarimbaMath`.

**Release.**
- When the hand stays open for more than 0.25 s (gap over 5 cm and not grasping), the mallet goes back to the tray with
  a 0.4 s settle.
- Mallets are also released and laid on the tray by `SetStowed(true)`.

**Tracking loss.**
- The mallet freezes and its pointer goes silent (`Down` false). It snaps back, with no blend, when tracking returns,
  and drops after 1.5 s.
- A mute held by a lost hand is released after 0.5 s, so a bar is never stuck damped.

**Fast Motion Mode.** The marimba has the `FastMotion` trait, so a held mallet requests FMM (platform 4.6).

### 5.2 Hit zones and sizes

**Pointers.**
- **Mallet:** the head centre, a sphere of r = 17 mm. Its strike plane is the bar top + 17 mm.
- **Fingertips:** all five tips, with the plane at the bar top + 8 mm (the pad radius). The OpenXR tip joint sits about
  7.6 mm inside the pad.

**A bar's strike footprint.**
- Its rectangle, plus 4 mm on the long sides (`SIDE_REACH`). The gaps between naturals are 6 mm.
- A sharp's footprint also reaches 10 mm in front of its near end (`END_REACH`, into the 25 mm row gap): a crossing at
  u ∈ [−0.03, 0) plays at u 0 instead of missing. A 34 mm head centred there already overlaps the sharp's near edge,
  which is 22 mm higher than the natural behind it, so the sharp is the bar it would touch first (IX4b).
- A crossing inside a gap goes to the bar whose footprint contains it (IX5 lands in C4's side reach).
- The rows never overlap in z (a 25 mm gap).
- The sharps are 22 mm higher, so a stroke from above crosses their plane first.

**Target sizes.**
- Naturals are 40–52 mm wide and 220–400 mm long; the sharps likewise. That is 1.7–2.2 × a piano white key's width.
- The warm spot is the bar centre ± 0.1 L.
- **Strike a sharp at its very tip (u ≤ 0.06, outside the cord node at u ≈ 0.19) or at its centre** (revision 2). The
  span u 0.1–0.2 that revision 1 recommended holds the node: F#4 measures −8.5 LU at u .12 and −14.1 LU at u .19 against
  an E4 centre stroke, and aiming at u .12 with 11 mm of along-bar error gives a median of −6.9 LU, worst decile −15.0
  (critic c2). The tip loses about 1–2.5 LU (u .02–.05), and aiming at u .04 gives a median of −2.3 LU.
- The bass sharps' centres (C#3–A#3) are out of seated reach (0.62–0.73 m shoulder to grip), but every sharp's tip is
  within 0.44–0.56 m (5.5), so the tip is the advice everywhere: the guide anchor `bar.<note>.tip` (u 0.04, 5.6) and
  Sunny's `tip_marimba_back` (8).
- A strike at the node (u ≈ 0.19 or 0.81) is thin, as on a real marimba: the fundamental is 40 dB down.

**Single source per bar.** C3–B3 play on MarimbaLo (x −0.37), C4–B4 on MarimbaMid (x 0), and C5–C6 on MarimbaHi
(x +0.38). The sources sit at bar height − 0.03 and z + 0.10, and follow the rig.

### 5.3 Stroke detection and velocity (per bar × pointer, each frame; the `DrumKit.Tick` pattern)

In VR nothing stops the hand at the bar, so a stroke decelerates and turns around under the strike plane, and tracking
jitters; the mallet's 0.27 m lever magnifies the grip's jitter at the head (about 6 mm at the head for 2 mm across the
95 mm wrist-to-grip vector). Every rule below is written and tested for that (`tools/ix-realistic.mjs`, 9.2).

1. **Arm.** A pointer arms for a bar once it is more than **20 mm** (mallet; the drums' tested `ArmAbove`) or 12 mm
   (fingertip) above that bar's plane on **two consecutive tracked samples**. Any pass through the plane disarms.
2. **Appearing.** A pointer that appears (new, or tracking back) starts disarmed for every bar it is not already clearly
   above (IX10, and a mallet that arrives resting on a bar).
3. **Stroke.** A stroke needs `prev.y > plane ≥ tip.y`, with `prev.y − plane ≤ 0.30 m` for a mallet or `0.10 m` for a
   finger.
4. **Crossing point.** It is interpolated on the plane and must lie inside a footprint (5.2). From it:
   - `u = (X.z − (z_bar − L/2))/L`, with 0 at the near end and 1 at the far end, clamped to 0..1;
   - `c = (X.x − x_bar)/(w/2)`, clamped.
5. **Speed.** `vy = max(crossing-sample speed, approach speed)`, over the tracker's own sample interval (spec 02
   `SampleVelocity`). The approach speed is the fastest two-sample downward speed in the last 50 ms (a ring of 8 heights
   per pointer). Players slow down at the drawn bar top, so the crossing sample alone reads a landing stroke as slow or
   silent; two-sample spans halve the jitter's effect. Under 3 mm head jitter the median velocity of a 1.5 m/s stroke
   reads 0.62–0.65 against the ideal 0.607 (revision 1: 0.58–0.60, with a wider low tail).
6. **Tracking jumps** (replaces the 8 m/s guard, which threw away real full strokes). A sample is a jump, never a
   stroke, when it moves more than `max(0.15 m, 14 m/s × dt)` or follows a low-confidence sample
   (`HandFrame.isHighConfidence` false). IX9 (a 30 cm jump) is silent; IX14 (9 m/s, 10 cm per sample) plays at vel 1.
7. **Velocity.** `vel = clamp(((vy − v0)/(v1 − v0))^0.7, 0.05, 1)`: vel 1 at and above v1, with no upper limit.
   - Mallets: v0 0.25, v1 2.8 m/s. Fingers: v0 0.12, v1 1.5 m/s (the drums' finger curve).
   - Below v0 a mallet's crossing is silent: a **lowering**, which may become a rest (5.4).
8. **Fingertips of one hand.**
   - One stroke per hand per sample: the fastest tip; within 10 %, the index; then the tip nearer its bar's centre line.
   - **The index first.** A non-index tip's stroke waits (at most 25 ms) while the same hand's index tip is descending
     within 15 mm above a different bar. If the index strikes in that time, it wins and the other stroke is dropped;
     otherwise the waiting stroke sounds with its own velocity and strike point. With an 11 mm aim error (Abdlkarim 2024)
     and the middle tip 4–8 mm lower than the index, the neighbouring bar sounded on 13–24 % of taps; now on 2 %, the
     floor set by the index's own aim error (critic c3).
9. **Debounce.**
   - 3 ms per bar (one frame).
   - 45 ms per owner: a mallet, or all five fingertips of one hand.
   - Two mallets on one bar are independent (rolls).
   - A strike needs two consecutive tracked samples (`Active && WasActive`). Tracking that returns below the plane is
     silent (IX10).
10. **Output.**
   - The sound: `IMarimbaSound.Strike(bar, vel, u, c, mallet, when: 0)`, with `TestTap?.RecordUnit(...)`.
   - The recorder: `TakeEvent` type `marimba` (5.6).
   - `PlayStamp.Mark(hand, Marimba)`.
11. **Latency and the predictive option.**
   - The stroke fires on the crossing frame: about 63–71 ms from fingertip to ear (burst.md 12). An index-first wait adds
     at most 25 ms, only when two tips of a hand are over different bars.
   - Optional: a predictive strike that posts `When = predicted crossing` one frame early. Off by default; judge it on
     the device (M7).

### 5.4 Techniques

| Technique | Detection | Sends |
|---|---|---|
| **Stroke** | 5.3 | `Strike` |
| **Dead stroke** (only when enabled: below) | After a sounding mallet stroke, the raw head stays inside the **band** from plane − 25 mm to plane + 10 mm and inside the footprint for **200 ms without a break** (a break = 3 consecutive samples outside), then is **still**: its height changed by less than 0.15 m/s × 60 ms over the last 60 ms, on 2 consecutive samples (IX3). An overshoot deeper than 25 mm is a stroke, never a dead stroke (IX13) | `Damp(bar, Mallet, u)`, signal `marimba.deadStroke`. `Undamp` when the head is above plane + 20 mm or off the bar on 3 consecutive samples |
| **Mallet rest** (a mallet lowered onto a ringing bar slower than v0) | A silent crossing (5.3 step 7), then the same band, unbroken, for 150 ms, then still (IX2) | `Damp(Mallet)`; `Undamp` on the same lift rule |
| **Hand mute** | A hand holding nothing: **≥ 2 fingertips** within 12 mm of a bar's plane, inside its footprint, slower than 0.3 m/s, for **120 ms** (IX8). The fingertip that sounded the bar does not count until it has risen above plane + 20 mm, so a tap-and-stay never chokes its own note (IX15). The 1-tip rule is gone | `Damp(bar, Hand, mean u)`, signal `marimba.mute`. The mute holds while any of the hand's tips stays within 20 mm above the plane; `Undamp` 30 ms after the last one leaves. A flat hand across several bars mutes all of them |
| **Roll** | ≥ 4 strokes on one bar from alternating owners, each within 200 ms of the previous (IX6) | Strikes only: the sound sustains by physics (3.21). Publishes `InstrumentSignalEvent{Marimba, "marimba.roll"}` |
| **Re-strike of a muted bar** | A new stroke by the muting mallet | First `Undamp`, then `Strike` |
| **Strike position / edge** | u and c of the crossing | Timbre by physics (3.17) |
| **Fingers** | Fingertip strikes (5.3 step 8) | `mallet = finger`: duller than soft yarn; an ordinary 0.6 m/s tap is 2 LU under an ordinary 1.2 m/s mallet stroke (2.4, 3.23) |

**When dead strokes are on.** A beginner's careful strokes pause near the bar, so dead strokes start **off**: the stored
setting `musicroom.marimba.deadStroke` (PlayerPrefs, default 0) turns on when `tip_marimba_dead` is shown (8), and
`MarimbaPlay.DeadStrokes` is false during any lesson whatever the setting (IX3b). The mallet rest and the hand mute are
always on: they are deliberate gestures (a slow lowering, two or more fingertips).

**Measured under realistic strokes** (`tools/ix-realistic.mjs`, 9.2; critic c3 re-run in 14):
- ordinary strokes, 0.6–4 m/s with 2–7 cm overshoot, 0–100 ms pause at the bottom and 0–6 mm head jitter (2 160
  strokes): no dead stroke with a pause up to 60 ms; 2 (0.09 %) with a 100 ms pause and a slow 0.4 m/s return; none
  missed, no double hits (revision 1, critic c3: 25 % of jitter-free strokes muted, 67 % with a 60 ms pause);
- deliberate dead strokes (a stroke, then 0.3–0.45 s held 0–15 mm into the bar): 96–100 % at 0–4 mm head jitter and
  0–10 mm press, 94–100 % at 6 mm or a 15 mm press, muted 200–211 ms after the stroke;
- a mallet resting on a bar for 60 s at 2–6 mm head jitter: no strikes after it settles, one mute, never toggled
  (revision 1: up to 25 phantom strikes and 10 mute toggles in 20 s at 6 mm).

### 5.5 Comfort for a seated player

**Height.**
- Natural tops are at 0.70 m (lift range 0.69–0.82).
- The mallets angle down about 35°, so the hands stay at or a little above seated elbow height. That is 0.64–0.75 m:
  a seat of 0.43–0.50 m plus 0.18–0.29 m (ARRANGE-NOTES). The shoulders stay low and the wrists neutral.

**Reach** (head at (0, 1.2, 0), shoulders at (±0.19, 1.05, 0.05)).
- Natural centres are 0.57–0.67 m from the nearer shoulder. With the mallet's lever the hand works at 0.30–0.40 m, with
  the elbow bent.
- Every sharp's tip (u 0.04) is 0.44–0.56 m from the nearer shoulder to the grip with the mallet at 35° (critic c5),
  inside the 0.61 m (2 ft) envelope.
- The bass sharps' centres C#3–A#3 need 0.62–0.73 m: play them at their tip (5.2), which is what the guide anchor and
  Sunny point at.
- The extreme bars, C3 and C6, sit at x = ±0.55 m.

**Fatigue.**
- Rolls in lessons are at most 1.25 s (Twinkle's half notes at 96 BPM).
- The first lesson needs one mallet or a single fingertip; the Mary lesson's suggested sticking never crosses the hands (7).
- No stroke has to start more than 30 cm above a bar.

**Arrange mode and grabs** silence the marimba: platform conformance item 17, plus Core skipping `Tick` while the rig is
held.

### 5.6 Recorder, playback, lessons, signals, desktop

**Take events** (the platform's starting set plus one State type, all prefixed with the id; `IInstrumentRegistry` roles):

| Type | Role, key | Fields |
|---|---|---|
| `marimba` | Onset, Midi | `midi` (bar = midi − 48), `vel`, `pos` = u (along the bar), `u` = c (across), `mode` `"mallet"` \| `"finger"`, `kind` = the mallet name (`"medium"`) |
| `marimbaDamp` | Release, Midi | `midi`, `mode` `"hand"` \| `"mallet"`, `pos` = u |
| `marimbaUndamp` | State, Midi | `midi`. A played-back mute must end, or the next playback stroke would sound dead |

**Playback** (`IInstrumentPlayback`).
- `Play(e)` drives the sound and the bar dip. Playback events are not re-recorded.
- `ReleaseAll()` undamps every bar. Struck notes simply decay.
- `CloseLane()` does nothing: there is no continuous lane.

**Lesson accompaniment** (`ILessonAccompany`, contract request R6). `Play(int[] midi, float vel)` strikes the marimba's
own bars through `IMarimbaSound.Strike(bar, v, 0.45, 0, Medium)` with `v = min(vel, max(0.05, 0.35 × the last live
onset's vel))` (`accompanyVel`, 7): no bar moves, no `TakeEvent`, never scored. `Release()` does nothing (the bars
ring out).

**Lesson targets** (`ILessonTargets`).

| Method | Behaviour |
|---|---|
| `Validate` | 0 ≤ i ≤ 36 and midi = 48 + i |
| `TryGetLanding` | The bar's top-centre pose; `approach` = up; `size` = (width, min(L, 0.12)) |
| `Matches` | `e.type == "marimba"` and `e.midi == t.midi`. Any `mode` counts |
| `ShowTarget` | The 3 mm lift of 4.4 |
| `Hint` | "Play E4", "Play E4 (right)", "Roll G4" |

**Signals** (`InstrumentSignalEvent{Marimba, …}`; emitted by `MarimbaPlay` unless noted; the reference emits them as
`signal` events in `out/interaction-vectors.json`).

| Signal | When | Feeds |
|---|---|---|
| `marimba.pickup`, `marimba.release` | A mallet is picked up / put back (`MalletToolProvider`) | `tip_marimba_drop` trigger and success, `tip_marimba_mallets` success |
| `marimba.roll` | The 4th stroke of a roll (5.4) | `tip_marimba_roll` success |
| `marimba.deadStroke` | A dead stroke mutes a bar | `tip_marimba_dead` success |
| `marimba.mute` | A hand mute starts | (diagnostic) |
| `marimba.backRow` | The first stroke on a sharp in the session | `tip_marimba_back` success |
| `marimba.fingerOnly` | Once per session: 6 fingertip strokes within 20 s while no mallet has been held in the session (IX18) | `tip_marimba_mallets` trigger |
| `marimba.offCentre` | Once per session: of the last 10 strokes on naturals, 6 or more at u < 0.3 or u > 0.7 | `tip_marimba_centre` trigger |

**Guide anchors** (`IGuideAnchors`).
- `tray.left` and `tray.right`: the mallet grips.
- `bar.<note>`: a top centre, for example `bar.C4`.
- `bar.<note>.tip`: the bar's nearest tip, at u 0.04 (revision 1's `bar.<note>.front` at u .12 pointed at the cord node's quiet span; 5.2).
- `bars.middle`: the F4 top.

**Desktop** (`!App.IsPresenting`).
- A left click on a bar's PlaySurface collider plays it at vel 0.7, with u and c from the hit point.
- Shift+click mutes it with a hand.
- The mouse hand's pointers never strike.

### 5.7 Tracking loss (summary)

| State | Behaviour |
|---|---|
| Held mallet | Freeze, pointers silent, snap back on return, drop after 1.5 s |
| Mallet mute (dead stroke or rest) | Held 0.5 s, then `Undamp` |
| Hand mute | The fingertips leave the list, so `Undamp` follows 30 ms later. An untracked hand never mutes |
| First tracked sample | Re-establishes `prev` only and disarms every bar the pointer is not clearly above. No stroke (IX10) |
| Low-confidence sample | The next sample is treated as a tracking jump: no stroke (5.3 step 6) |
| Headset off / pause | Onsets more than 100 ms late are dropped by the engine (`LateDrop`). Dampers are never late-dropped |

---------------------------------------------------------------------------------------------------------------------------

## 6. Unity integration (against platform.md; the marimba agent changes no shared file)

### 6.1 Folders and contracts

**Folders.**
- **Module:** `Assets/MusicRoom/Marimba/**`. It contains:
  - asmdef `MusicRoom.Marimba` (references Core, Hands, Audio);
  - `MarimbaDefinition`, `MarimbaModule` (Order 35), `MarimbaInstrument` (`IInstrument`, `IStowable`,
    `IInstrumentPlayback`, `ILessonTargets`, `ILessonAccompany` (R6), `IGuideAnchors`, `IContactSurface`);
  - `MalletToolProvider`, `Mallets`, `MarimbaPlay`, `MarimbaMath`, `MarimbaBuilder` (meshes), `MarimbaTextures`
    (bake + material templates `Marimba/*`);
  - `Resources/LessonPacks/marimba.json`;
  - `Resources/Voice/tip_marimba_*.wav`, `Resources/VoiceManifests/Marimba.json`, `Resources/GuideLines/Marimba.json`;
  - `README.md`, `CONTRACT-REQUESTS.md`.
- **Sound unit:** `Assets/MusicRoom/Audio/Sound/Marimba/**`. It contains `MarimbaUnit`, `MarimbaDsp`, `MarimbaDesign`,
  `MarimbaContact`, `IMarimbaSound` and `MarimbaState`.
- **Tests:** `Tests/EditMode/Audio/Marimba`, `Tests/EditMode/Marimba`, `Tests/PlayMode/Marimba` (with
  `IMarimbaFixture`).

**The unit's own API**, reached as `audio.ActiveUnit(UnitId.Marimba) as IMarimbaSound` (platform 10.1: no new
`IAudioEngine` methods):

```csharp
public enum MarimbaMallet : byte { Medium, Soft, Hard, Finger }
public enum MarimbaDampKind : byte { Hand, Mallet }
public interface IMarimbaSound {
    void Strike(int bar, float vel, float u, float c, MarimbaMallet mallet, double when = 0);   // u along 0..1, c across -1..1
    void Damp(int bar, MarimbaDampKind kind, float u, float c, double when = 0);
    void Undamp(int bar, double when = 0);
    void Silence();
    float BarLevel(int bar);   // block RMS (Volatile.Read), for visuals and tests
}
```

**Classic sound.** This is a unit-only role (platform 10.1). The Classic switch never suspends it, and
`OnPathChanged` needs nothing, because the unit uses no shared banks or one-shots.

### 6.2 The unit (`MarimbaUnit : SoundUnit, IMarimbaSound`) and the DSP (`MarimbaDsp.Render`)

**Boot**, on `ctx.StartWorker`.
- `MarimbaDesign.Build(48000)` builds the two FE profiles from the **stored** arch parameters `ARCH` (`design.mjs`; the
  converged output of the tuning, to full double precision: `lo` width 0.5474, depth 0.6690670628700355, shape 0.4; `hi`
  0.3877574834020015, 0.5206638604881906, 0.5). That is two 146 × 146 generalized eigenproblems, 0.14 s in Node
  including warm-up, then the bars, tubes, pairs, lanes and normalisers.
- It does **not** re-run the Newton tuning (`tuneArch`): revision 1's reference did, which costs about 2.6 s in Node
  (critic: the "0.3 s" claim was wrong). The EditMode test `MarimbaDesignTests.Retune` runs it once and asserts it
  reproduces `ARCH` exactly (`node design.mjs --retune` does the same in the lab).
- Alternatively, load `marimba-tables.json` and assert equality with the built tables in EditMode.
- The lane tables are written into the state before `Ready`. Strikes that arrive before `Ready` are dropped; they are
  first-frame edge cases.

**Registration.**
`Register(ctx, UnitId.Marimba, new[] { SourceId.MarimbaLo, SourceId.MarimbaMid, SourceId.MarimbaHi }, _s, ringCapacity: 256, pendingCapacity: 256, idleSkip: true)`.
Idle-skip is safe: the unit outputs nothing once every bar has retired and no player is live.

**Main-thread calls.**

| Call | What it does |
|---|---|
| `Strike` | `slot = counter++ & 31`; `MarimbaContact.Solve(design, ref records[slot], bar, vel, u, c, mallet, seed)` (doubles, kick-drift-kick at `OS_BY[mallet]`, preallocated `double[8]` scratch, no allocation; 2–15 µs in Node, 2.4); then `Post(GraphCommand{ Op = 1, Id = slot, When = when, Flags = LateDrop })`. The record is written before `Commit`, and the ring's release store publishes it (ENGINE-API 3.3) |
| `Damp` | `slot = dampCounter++ & 63`; `MarimbaContact.DampPoles` writes 8 pr/pi floats; then `Post(Op = 2, Id = slot)` |
| `Undamp` | `Op = 3, N = bar` |
| `Silence` | `Op = 4` |
| `Seed(uint)` | Re-seeds the main-thread knock hash |

Every public method calls `TestTap?.RecordUnit(Id, op, bar, vel, when)`.

**`MarimbaState`** (one `Alloc`, 64-byte aligned):

| Field | Size |
|---|---|
| `ModalGroup groups[74]` | 7104 B |
| `float p0r[296], p0i[296]` (undamped poles); `byte live[296]`; `byte src[37]` | 2.7 KB |
| `byte active[37], fading[37]`; `float energy[37], level[37]` | 0.4 KB |
| `MarimbaStrikeRecord strikes[32]` { `int bar, len, nh`; `float seg[1024]`; `float injR[8], injI[8]` } | 133 KB |
| `MarimbaDampRecord damps[64]` { `int bar`; `float pr[8], pi[8]` } | 4.6 KB |
| `Player players[16]` { `int on, rec, bar; long start` } | 0.3 KB |
| `Action actions[128]` { `long at; int seq, kind, bar, id` }, kept sorted on insert; kinds `Inject`, `Poles`, `Undamp` | 3 KB |
| `float4 acc[256]; float barOut[256]` | 5 KB |
| **Total** | **≈ 151 KiB** |

**`Render(u)`.** The order is canonical; it is `MarimbaUnit.render` in the reference.

0. Read `Budget` and `Share` once.
1. If `Flags & Reset` is set, zero all groups, players and actions.
2. Handle due events in `(At, Seq)` order, with `at = max(At, BlockStart)`:
   - **Strike:** take a free player (otherwise count `DroppedPlayers`) and insert an `Inject` action at `at + nh`.
   - **Damp:** insert a `Poles` action at `at`.
   - **Undamp:** insert an `Undamp` action at `at`.
   - **Silence:** mark every active bar as fading, and clear the players and actions.
3. Activate every bar with an action before the block end. Then, while `nActive > clamp(round(24·Budget/1200·Share), 8, 37)`,
   mark the quietest bar that has no action this block as fading (`Capped`).
4. For each active bar, in ascending order:
   - render both groups with `ModalFree`, in segments split at its actions;
   - apply each action at its offset: Inject is `z += inj` on the live lanes; Poles and Undamp copy a pole table;
   - `AccResolve` into `barOut`. Let `ch = Out[src[bar]]`;
   - **if fading:** `ch[n] = mad(barOut[n], 1 − n·(1/frames), ch[n])`, then zero the bar and deactivate it;
   - **otherwise:** `ch[n] += barOut[n]`;
   - run `ModalEndBlock` on both groups, giving the energy (their max) and the level;
   - retire (zero) the bar if `energy < 1e-11` and no action or player is pending for it.
5. For each live player, add `ch[n] += seg[j]` over the overlap. Free it once `start + len` is at or before the block
   end.
6. Set `Work` to the number of lanes rendered.

**Denormals.** Handled by the engine's flush at block end (`ModalEndBlock`), retirement at −110 dBFS, and float32
records. 0 subnormals were measured (3.15).

**FloatMode Fast.** The Node fused-math estimate is within 4.8e-7 … 2.9e-6 of strict (TV4–TV7); the test tolerance is
1e-5.

### 6.3 Contract requests (`Marimba/CONTRACT-REQUESTS.md`, for the platform agent)

| # | Request | Why |
|---|---|---|
| **R1** | A third source **`SourceId.MarimbaMid`** in the id block, appended after the others, and three `AudioSourceTable` rows (spatial, acoustics on, reach 0.7, ER 0, pools 0, the marimba's room group) with **room sends MarimbaLo −2 dB, MarimbaMid 0 dB, MarimbaHi −2 dB** (revision 2; the table is per source, as the drums' −3 dB). The marimba then has Lo, Mid, Hi; the seat maximum of 13 instrument sources becomes 14 | One true point source per bar (0.5): seat DRR +5.7 dB instead of +1.2 dB for a centre phantom image. The lateral sources' −2 dB sends bring their DRR from +2.8 / +2.3 to +4.8 / +4.3 dB, inside the platform's ±2 dB of the piano's +6.1 (3.1); the piano's own laterals measure +4.1 in the same harness. Fallback with two sources: hard split at E4/F4 (an image jump inside the lesson range); re-run `tools/parity.mjs` for their sends |
| **R2** | The marimba's "metallic tail" conformance uses the metal rule plus check 3.22 | The wood rule's top-20 share is 100 % for any single tuned note (its fundamental) |
| **R3** | `window-near` display height: bar tops 0.69–0.72 m, not below the 0.63 m sill | The C3 tube is 632 mm. The 0.85 m view rule is met |
| **R4** | `Core/Util/HandToolMath.cs`: `StickGrip`, `StickPose`, `SegmentDistance`, `SettleEase`, moved from `DrumMath` (Drums keeps forwarding calls) | Mallets must not reference the Drums module |
| **R5** (optional) | Guide demo kind `MalletHold` (a held mallet: one stroke, held down 0.6 s) | For `tip_marimba_dead`. Fallback: `MalletStrike` |
| **R6** (revision 2) | **Lesson packs on the generic path:** (a) a pack lesson may carry `options` (names of `LessonOptions` fields, sunny-tour 7.4) that `Tutorial` applies when it starts the lesson from the menu, and an `accompany` array (sunny-tour 2.5); (b) a new optional instrument interface `ILessonAccompany { void Play(int[] midi, float vel); void Release(); }`. When the lesson's instrument implements it, `Accompany` plays through it; otherwise through `IAudioEngine.Piano` / `Release` as today. Wait mode fires each chord on its beat's hit, as for the piano | The platform's accompaniment is defined only through the piano, and pack options were not defined at all (critic). The marimba's first lesson needs its own low bars to answer, at a level that follows the player (7). Fallback without R6: the pack sets no options and no accompaniment; the lesson stays winnable through B6 alone |

---------------------------------------------------------------------------------------------------------------------------

## 7. Lessons (pack `Resources/LessonPacks/marimba.json` = `out/lessons-marimba.json`)

**Schema.** The `LessonFile` schema of `lessons.json`, with `tutorial = { SPEED 0.18, TRAVEL { marimba: 0.30 }, leadSeconds { marimba: 1.667 } }`.

**Target fields.**
- `i`: 0 = C3 … 36 = C6, chromatic.
- `midi`, `note`.
- `kind`: `"roll"` or null.
- `label`: `"R"`, `"L"` or null, the suggested mallet.

**Hints.** "Play E4", "Play E4 (right)", "Roll G4".

**Sources.** The tunes are copied verbatim from the verified source `lessons-src.mjs` of the tutorials audit (MARY and
TWINKLE_FULL; citations in `tutorials-audit.md`) and from sunny-tour.md 2.5 (the Ode answering phrase). All are public
domain.

| id | Title | BPM | Notes | Music s / with lead + countdown | Bars | What it teaches |
|---|---|---|---|---|---|---|
| `marimba-first` | First notes | 80 | 15 | 12.0 / **16.7** | C4–G4 (five adjacent naturals) | **The guaranteed 30-second win** |
| `marimba-mary` | Mary Had a Little Lamb | 100 | 26 | 19.2 / 23.9 | C4 D4 E4 G4 | Two mallets, a suggested sticking that never crosses the hands (`label`, below) |
| `marimba-twinkle-roll` | Twinkle Twinkle (rolled) | 96 | 42 | 30.0 / 34.7 | C4–A4 | Rolls on the six half notes (`kind: "roll"`). The first stroke scores, so the lesson stays winnable |

**Why `marimba-first` is a guaranteed win.**
- The tune is `E4 E4 F4 G4 G4 F4 E4 D4 C4 C4 D4 E4 D4:1.5 C4:0.5 C4:2`: Beethoven's Ode, the answering phrase, which
  ends resolved on C.
- One target at a time, 12 s of music: the platform's pack rule asks for 12–21 s.
- Five big neighbouring bars sit straight in front of the player.
- Wait mode freezes time.
- After 8 s Sunny demonstrates again; after 15 s any bar plays the note (the platform's B6). That is the lesson's only
  forgiveness rule: revision 1's "Forgiving after 2 misses" and `AnyImplement` do not exist in the lesson system.
- A fingertip counts: `Matches` accepts any `mode` (5.6), so the player does not have to pick up a mallet first.
- `options: ["Accompany", "QuietMistakes"]`: the piano first-run pattern (sunny-tour 2.5 and 7.4), applied by request R6
  (a). Wrong notes sound, with no red flash and no mistake count.
- `accompany` (sunny-tour 2.5 style): the marimba's own low C3+G3 and G3+B3 answer each phrase, unscored; the final
  chord is C3 E3 G3. It plays through `ILessonAccompany` (R6 (b), 5.6) at `min(written vel, max(0.05, 0.35 × the last
  live onset's vel))` (`accompanyVel`; written vel 0.28 / 0.30).
- **Why the accompaniment follows the player (critic H2c).** At a fixed vel 0.28 it was −29.3 LUFS, louder than the
  revision-1 fingertip melody itself (−56 to −32). `tools/lesson-balance.mjs` renders the whole lesson as two stems:

| Played with | Melody | Accompaniment (R6 rule) | Margin | Margin at the old fixed vel 0.28 (same fingertip) |
|---|---|---|---|---|
| Fingertip 0.25 m/s (softest) | −38.5 LUFS | −41.9 | 3.4 LU | −7.6 |
| Fingertip 0.4 / 0.6 / 0.9 m/s | −32.2 / −27.5 / −23.2 | −39.2 / −36.3 / −33.0 | 7.0 / 8.8 / 9.8 LU | −1.3 / 3.5 / 7.7 |
| Mallet 0.5 m/s (softest) | −36.0 | −41.8 | 5.8 LU | −5.0 |
| Mallet 0.8 / 1.2 / 2.0 m/s | −30.0 / −25.4 / −20.1 | −38.9 / −35.9 / −31.6 | 8.9 / 10.4 / 11.5 LU | 0.9 / 5.5 / 10.9 |

Pass: at least 6 LU under the melody for ordinary taps and strokes, at least 3 LU for the softest.
`Builds/Listen/marimba/marimba-first-lesson.wav` is the fingertip case at 0.6 m/s.

**The Mary sticking (critic: plain R L R L crossed the hands on 7 of 26 notes; crossed hands block each other from the
headset's cameras).** `lessons.mjs` plans it with a small dynamic programme over (left bar, right bar, last hand): the left
mallet never sits to the right of the right one; a hand repeat costs 1 (2 on a repeated note, which alternates as marimba
players do); travel costs 0.5 per metre. Result `R L L L R L R L R L R R L L L L L R L R L L R R L L`: 0 crossings, 10 hand
repeats (no sticking without crossings has fewer), 0 repeated notes on one hand. The hints read "Play E4 (right)".

**Pack rules checked.**
- 3 lessons.
- Each event has one target.
- Shortest gap ≥ 0.375 s; same-target repeats ≥ 0.6 s.
- Every lesson is under 1 minute.
- Public domain, sources named.

**Reuse.** All 15 piano lessons and all 9 harp lessons play on the marimba unchanged, with `i = midi − 48`. The pack
may add them later as marimba ids.

---------------------------------------------------------------------------------------------------------------------------

## 8. Sunny (tips as `TipSpec` data in `MarimbaDefinition`; lines ≤ 10 words; ids prefixed `tip_marimba_`)

| Tip id | Covers | Trigger | Line (caption = spoken) | Words | Demo @ anchor | Success | Class |
|---|---|---|---|---|---|---|---|
| `tip_marimba_arrive` | `marimba.play`, `marimba.mallets` | `FirstArrival` | "Pinch a mallet from the front tray to pick it up." | 10 | Pinch @ `tray.right` | The first live `marimba` Onset | P2 |
| `tip_marimba_mallets` | `marimba.mallets` | `Signal marimba.fingerOnly` (5.6: 6 fingertip strokes within 20 s, no mallet held in the session) | "Mallets sound fuller. Pinch one from the tray." | 8 | Pinch @ `tray.right` | `Signal marimba.pickup` | P3 |
| `tip_marimba_drop` | `marimba.release` | `Signal marimba.pickup` (the first) | "Open your hand to put a mallet back." | 8 | OpenHand @ Sunny | `Signal marimba.release` | P2 |
| `tip_marimba_centre` | `marimba.position` | `Signal marimba.offCentre` (5.6: of the last 10 strokes on naturals, ≥ 6 at u < 0.3 or > 0.7) | "Strike near the middle of a bar for warmth." | 9 | MalletStrike @ `bar.C4` | 3 strokes on naturals at u 0.35–0.65 | P3 |
| `tip_marimba_roll` | `marimba.roll` | The first `kind: "roll"` target (in a lesson), or 30 strokes and never a roll | "For long notes, alternate both mallets quickly." | 7 | MalletStrike (twice as fast) @ `bar.G4` | `Signal marimba.roll` | P2 in a lesson / P3 |
| `tip_marimba_dead` | `marimba.deadStroke` | 40 strokes after the first roll, outside a lesson. **Showing it turns dead strokes on** (`musicroom.marimba.deadStroke` = 1, 5.4) | "Press the mallet down and hold for a short note." | 10 | MalletHold (R5) @ `bar.E4` | `Signal marimba.deadStroke` | P3 |
| `tip_marimba_damp` | `marimba.handMute` | Bars left ringing ≥ 2 s in silence, the 2nd time | "Rest your hand on a bar to quiet it." | 8 | PalmRest @ the ringing bar | `marimbaDamp` (`mode` hand) | P3 |
| `tip_marimba_back` | `marimba.sharps` | 20 strokes, never on the back row (no `marimba.backRow` yet) | "Sharps are the back row. Strike their nearest tip." | 9 | MalletStrike @ `bar.F#4.tip` (u 0.04, 5.2) | `Signal marimba.backRow` | P3 |
| `tip_marimba_lesson` | `lessons.marimba` | The first marimba lesson starts | "Strike each bar as its block lands." | 7 | MalletStrike @ the target bar | A hit | P2 |

**Rendering.** Lines follow sunny-tour.md 5.4, route B (eSpeak NG `en-us+Annie`, 150 wpm, pitch 55, the existing
`render()`). They ship as `Resources/Voice/tip_marimba_*.wav` (22.05 kHz mono) plus the manifest and line files. All
are ≤ 10 words; check that each clip is ≤ 4.0 s.

**Feature tags** (`[Feature]`, for `SunnyCoverageTests`):
- `marimba.play` on `MarimbaInstrument`;
- `marimba.mallets` and `marimba.release` on `MalletToolProvider`;
- `marimba.roll`, `marimba.deadStroke`, `marimba.handMute`, `marimba.position` and `marimba.sharps` on `MarimbaPlay`.

---------------------------------------------------------------------------------------------------------------------------

## 9. Tests for the Unity build

### 9.1 EditMode: the sound (`Tests/EditMode/Audio/Marimba`, vectors copied to `Reference~/`)

**Tolerances.**
- Burst off: bit-exact FNV-1a.
- Burst on: 1e-5 absolute per sample.
- The main-thread solve is double: float32 results within 1 ulp, or 1e-6 relative.

| Test | Setup | Expected (`out/test-vectors.json`) |
|---|---|---|
| TV1 Design | `MarimbaDesign.Build(48000)` from the stored `ARCH` | Per bar: f1, L, width, h0, mass, x, z, source; tube (a, ft, L, Q, g, cb, ct); pair K and partBar; 7 live lanes with float32 poles. Spot values: C4 e+ `pr 0.99927455, pi 0.034232136` (261.60181 Hz, α 6.6871014); b2 `0.99022371 / 0.13650252`; C4 barGain 0.943289987; sources C3 → 0 (Lo), C4 → 1 (Mid), C6 → 2 (Hi). Mallet K: medium 4.895285e9, soft 4.556464e9, hard 5.600626e8, finger 2.545417e7 (finger speed at vel .6: 1.1157103 m/s). FE `lo`: 1 : 4.026 : 9.981, nodes 0.1883 |
| TV2 Strike records (kick-drift-kick, `OS_BY`) | C4, vel .6, u .45, c 0, medium, seed 1 | `nh 46, len 238`; contact 0.9375 ms; Fmax 138.4009 N; speed 2.004775; seg FNV `0xe900d239`; seg[0..2] = 1.5996130e-4, 1.4115610e-3, 4.3672267e-3; injR[0] 0.23800384, injI[0] 0.25563180 |
| | C3, vel 1, u .06, c .5, hard, seed 7 | `nh 17, len 209`; 0.3333 ms; Fmax 919.1997 N; FNV `0x41d09c3e` |
| | C6, vel .2, u .5, c −.3, soft, seed 3 | `nh 125, len 317`; 2.5833 ms; FNV `0x31734d62` |
| | F#3, vel .7, finger, seed 9 | `nh 89, len 281`; 1.8333 ms; Fmax 51.0918 N; FNV `0xf253f734` |
| TV3 Damp poles | C4 hand at u .5; C4 mallet at u .45, c .3 | 8 pr/pi each; hand pr[0] = 0.99859953 |
| TV4 Single C4 | 8 blocks | Block FNVs Lo/Mid/Hi (block 1, the first that sounds: `0x1f116dc5/0x1de39ca9/0x1f116dc5`, i.e. only Mid sounds); sum 5.25286982; Σx² 27.2239463; peak 0.272022843 |
| TV5 Roll + damp + undamp | G4 at 0, 40 and 80 ms; hand damp at 100 ms; undamp at 150 ms; hard re-strike at 160 ms; 48 blocks | Block FNVs; sum 4.0520713; peak 0.730481744 |
| TV6 Budget 400 | 12 bars struck within 22 ms; cap 8 | `capped 4`; peak 1.75440311; block FNVs |
| TV7 Two mallets on the same sample | C5 at u .4 and u .6, plus G5 hard | Peak 1.35875142; block FNVs |
| TV8 Retire and reset | C6 mf; reset after one block | C6's last non-zero sample is 26623 (0.55 s); every state is exactly 0 after `Reset` |
| Determinism | Two renders | Identical (the vectors file's MD5 is stable: `afedb124…4a368f`) |
| Retune | `MarimbaDesign.TuneArch` from the start values | Reproduces `ARCH` exactly (`node design.mjs --retune`, 2.6 s in Node; EditMode only, never at boot) |
| Physics checks 3.2–3.22 | Run the `verify.mjs` checks on `OfflineEngine` renders | The same thresholds |
| Null test | `RenderGraph` of the velocity-ladder score vs the Node render | Residual ≤ −80 dB with Burst off, ≤ −60 dB with it on (`NullTest.BestGain`) |
| Perf | `OfflineEngine`, the 37-bar glissando schedule, 400 blocks, Burst on | A `[PERF] marimba` line; device acceptance in 9.3 |
| Allocation | `Strike` and `Damp` on the main thread; `Tick` | `Is.Not.AllocatingGCMemory()` |
| Platform conformance | `LoudnessParityTests`, `UnitShareTests` | Within ±2 LU of the piano (Node: −0.14); `Share` 0.4 lowers the steady `Work` by ≥ 50 % with ≥ 20 bars ringing (Node: 168 → 70 lanes) |

### 9.2 EditMode and PlayMode: interaction with scripted hands (`out/interaction-vectors.json`, `FakeHandSource`)

| Case | Script | Expected |
|---|---|---|
| IX1 | Mallet straight down at 1.5 m/s on C4, u .45 | One strike: C4, vel 0.6071, u 0.45, c 0 |
| IX2 | Mallet lowered at 0.2 m/s onto E4 and held | No strike; `Damp(mallet)` 156 ms after the silent crossing (band, 150 ms, still); `Undamp` on lift |
| IX3 | G4 at 2.5 m/s, head held 10 mm under the plane for 0.4 s (dead strokes on) | Strike at vel 0.9161; `Damp(mallet, dead)` 200 ms later, signal `marimba.deadStroke`; `Undamp` on lift |
| IX3b | IX3 with `DeadStrokes` false (a lesson; before `tip_marimba_dead`) | The strike only |
| IX4 | F#4 at its tip, u .04, at 1.8 m/s | Strike: F#4, vel 0.7058, u 0.04; signal `marimba.backRow` |
| IX4b | F#4 struck 6 mm in front of its near end | Strike: F#4, u 0 (`END_REACH`) |
| IX5 | Into the C4–D4 gap (c 1.12) | Strike: C4, c 1 |
| IX6 | Two mallets alternating on A4, 10/s for 1 s | 10 strikes at vel 0.501; `roll` and `marimba.roll` at the 4th |
| IX7 | Index fingertip at 0.6 m/s on C5 | Strike: C5, vel 0.4775, `mode` finger |
| IX8 | Three fingertips of a free hand rest on D4 for 0.3 s | `Damp(hand)` at 122 ms, signal `marimba.mute`; `Undamp` 30 ms after the lift |
| IX9 | A 30 cm jump through C4 in one sample | Nothing (a tracking jump) |
| IX10 | Tracking lost, then back below the plane | Nothing |
| IX11 | A sideways sweep 2 cm under the plane | Nothing |
| IX12 | 2.8 m/s on C4 | vel 1 |
| IX13 | A soft VR stroke on E4: 0.6 m/s, decelerating to 5 cm under the plane, a 60 ms pause, back at 0.6 m/s (dead strokes on) | One strike (vel 0.249), no damp |
| IX14 | A full stroke at 9 m/s (10 cm per 90 Hz sample) on C4 | One strike at vel 1 (revision 1's 8 m/s guard dropped it) |
| IX15 | Fingertip tap-and-stay on E4: the index taps at 0.6 m/s and stays 4 mm under the plane, the middle tip resting on the same bar | One strike (vel 0.4775), no damp |
| IX16 | The middle tip 6 mm lower than the index and over D4, the index over E4; the hand comes down at 0.6 m/s | One strike: E4 (the middle tip waited one sample, then was dropped); D4 never sounds |
| IX17 | A mallet lowered onto G4 at 0.2 m/s, then 20 s at rest with 6 mm AR(1) head jitter (seeded) | No strike; one `Damp(mallet)` at 422 ms; no toggling |
| IX18 | Six fingertip strokes on C4–G4 in 2.2 s, no mallet ever held | Six strikes; signal `marimba.fingerOnly` with the 6th |
| Realistic strokes | `tools/ix-realistic.mjs` (seeded), all 8 checks: ordinary strokes (2 160), deliberate dead strokes, resting mallets 60 s, hard strokes 4–10 m/s at 72 / 90 Hz, fingertip tap-and-stay (1–3 tips), flat-hand mutes 0.08–0.6 m/s, the neighbouring fingertip (400 taps × 4), a sharp's tip with 11 mm aim error | The pass marks of 5.4 and `out/ix-realistic.json`. Implement as a PlayMode test with `FakeHandSource` driving the same seeded paths |
| Pick-up | As `DrumsPlayTests`, through `MalletToolProvider` | `None` at 35 cm; `Near` at 15 cm, and consumed; `Grabbed` at 2 cm. Meta's pinch at strength 0.86 grabs. Open hand for 0.25 s releases; 0.4 s settle; tracking loss drops after 1.5 s |
| Tools | A hand holding a drumstick; a stick pointer over the bars | No pick-up; no strike (`Tools` mask = Mallet) |
| Recorder and looper | Play, record, play back | `marimba` / `marimbaDamp` / `marimbaUndamp` with the 5.6 fields; playback events are not re-recorded; `ReleaseAll` undamps |
| Lessons | `marimba-first` with a scripted fingertip | Completes; the accompaniment plays through `ILessonAccompany` at `accompanyVel` (0.35 × the last live vel), is not scored and not recorded; no bar moves |
| Sticking | `marimba-mary` labels | No crossing (`handCrossings` 0 in the pack) |
| Visual | The hit frame | The bar's vertices dip ≤ 1.6 mm·vel and return within 0.25 s; the drawn head never goes below the bar top |
| Conformance | `InstrumentConformanceTests` items 1–20 | All pass, including draws ≤ 5 at the seat and ≤ 3 on display (GPU run) |

### 9.3 On-device checks (add to ARCHITECTURE section 19)

| # | Check | Pass |
|---|---|---|
| M1 | `[PERF]` and `DspLoadMeter` with the marimba glissando plus T-STRESS (burst.md 13) | Unit average ≤ 60 µs, max ≤ 150 µs at CPU level 3; graph p99 still ≤ 1.2 ms |
| M2 | Main thread, 30 strokes/s for 10 s, mallets and fingertips | `MarimbaContact.Solve` ≤ 80 µs each (C# IL2CPP; Node worst bars 12 µs mallet, 15 µs fingertip, so about 50–60 µs expected); 0 GC allocations |
| M3 | Mallet pick-up with Meta's pinch | 9 of 10 testers pick up first try; no accidental rig grabs |
| M4 | Stroke detection at the real tracker rate (LOW, and in FMM) | No double hits on 12 strokes/s rolls; no missed mf strokes; even velocity (log vy). **Log the mallet head's height jitter (RMS) with the mallet resting on a bar and held still in the air**; pass: no phantom strike in 60 s of resting. If the RMS is above 6 mm, re-run `tools/ix-realistic.mjs` with it |
| M5 | Dead stroke, mallet rest and hand mute | Testers' ordinary strokes are never muted; a stroke held on the bar for about 0.3 s is; no stuck mutes after tracking loss. Log the raw head depth while "pressing" (tune `BAND_BELOW`, 25 mm) and the bottom pause of ordinary strokes (tune `DEAD_WINDOW_MS`, 200 ms) |
| M5b | Fingertips | A tap-and-stay rings; a flat hand mutes; tapping with the index never sounds the neighbouring bar in 20 taps per tester |
| M6 | Reach and comfort, seated, small and tall testers | Every natural is easy; the back-row near ends are reachable without standing |
| M7 | Latency: a filmed tap vs the audio onset (T-LAT) | Report it; try the predictive strike if the total is over 75 ms |
| M8 | By ear, on the headset speakers | Warm, wooden, no glassy ring, no click, the low notes bloom; level against the piano. **The top octave:** there is very little above 6 kHz (−48 to −56 dB re total in the dry renders); check it does not sound dull or electronic (critic). The fingertip against the accompaniment in the first lesson |
| M9 | Mixed reality | Stands on the floor; the knees vs the tubes are acceptable; the mallets in real hands look right with the cutout |

---------------------------------------------------------------------------------------------------------------------------

## 10. Renders (`Builds/Listen/marimba/`; 48 kHz stereo 24-bit; −18 LUFS each, true peak −1.9 … −7.9 dBTP; 59.4 MB)

| File | s | Content |
|---|---|---|
| `marimba-velocity-ladder.wav` | 27.2 | C3, C4, C5, C6, each at vel .15/.35/.55/.75/.95; one gain for the whole file |
| `marimba-range-chromatic.wav` | 17.6 | All 37 bars at mf |
| `marimba-techniques.wav` | 41.1 | Strike position (0:00), mallets incl. the re-voiced fingertip (0:07), dead strokes muted at the 200 ms detection (0:12), rolls (0:17), hand mutes (0:26), centre line vs edge (0:29), sharps at their tip (u .04), at revision 1's u .15 and at the centre (0:32) |
| `marimba-amazing-grace.wav` | 36.6 | Amazing Grace (NEW BRITAIN, 1829) an octave up, long notes rolled, 76 BPM, humanised strokes |
| `marimba-canon-pattern.wav` | 32.0 | Pachelbel's Canon progression (about 1680) as two-mallet broken chords, two passes |
| `marimba-first-lesson.wav` | 15.4 | New: the first lesson (the Ode's answering phrase, 80 BPM) tapped with a fingertip at 0.6 m/s, humanised, with its accompaniment at the R6 rule (7) |
| `marimba-amazing-grace-room.wav` | 36.6 | The same take as `amazing-grace` from the seat: the three sources' direct paths with the head model, room lab v2 `natural` with the R1 sends (Lo / Hi −2 dB), MasterBusV2 (no gain reduction) |

- The dry files are dual mono; the app spatializes the three sources.
- The critic's own meter (`critic/c1-renders.mjs`, re-run): all seven at −18.0 LUFS, true peak −1.93 … −7.93 dBTP,
  below 30 Hz −62.6 … −65.5 dB re total, 59.4 MB.
- **Long-term spectra** sit close to the new piano's. For the ladder: 2 kHz −16 dB and 4 kHz −32 dB re total, against
  the piano's −13 and −30 dB.
- The higher, softer pieces are darker (`out/render-check.json`).
- `marimba-README.txt` tells the listener what to hear, and what would be wrong.

---------------------------------------------------------------------------------------------------------------------------

## 11. Risks, open points, UNVERIFIED

**Nobody has listened yet.** The user decides by ear (Section 10; check M8).

**The coupled-tube regime.**
- `KAPPA_G` is fitted to one published decay (about 2 s at C3, a patent statement).
- The tube and gap losses (`Q_GAP`, `GAP_RAD`, `END_CORR`) are physically argued but not measured.
- A full piston estimate of the coupling is about 6× stronger. That would split the low fundamentals into an audible
  beat, but real resonator gaps leak, and Stevens reports no beating.
- If the bass sounds too late (the bloom) or too dry:
  1. raise `KAPPA_G` toward critical (about 0.012 at C3);
  2. re-run `calibrate-coupling.mjs` and `calibrate.mjs`;
  3. listen for beating.

**Compact bars.** The visual lengths are 15–20 % short for real rosewood at this thickness. The sound uses normalised
shapes and the tuned frequencies, so nothing audible changes. The torsional ratios are modelled.

**Contact physics.**
- The contact force ignores motion already in the bar: at most 0.3 mm, against 0.5–2 mm of yarn compression.
- Two mallets landing in the same millisecond on one bar give two independent records (TV7), not a joint solve.
- Rolls stay physically superposed and bounded (3.21).

**Mallet constants** are design values that follow published trends: a few ms of contact for soft yarn, under 1 ms for
hard rubber, shorter at speed. They are not measurements of a specific mallet. The fingertip pad (20 g, 1.8 ms) and its
per-bar voicing (+4 to +14 dB) are a playability choice: a real fingertip on a real marimba is quieter and duller, and
the top octave still is (2.4).

**The VR stroke rules (5.3, 5.4) are tuned on a model of VR strokes, not on testers.**
- The model (the critic's): uniform deceleration to 2–7 cm under the plane, 0–150 ms pauses, AR(1) jitter up to 6 mm at
  the head. Real overshoot depths, pauses, press depths and jitter come from M4/M5.
- Two cases stay ambiguous by nature: a soft stroke that pauses about 0.1 s at 2 cm and comes back slowly (0.09 % become
  dead strokes, and only once dead strokes are on), and a "dead stroke" pressed more than 25 mm into the bar (treated as an
  ordinary stroke). `BAND_BELOW` and `DEAD_WINDOW_MS` are the device-tuning knobs.
- The approach speed reads a little high under jitter (median +0.03 … +0.05 vel at 3 mm head jitter, about +0.6 dB).
- A mallet lowered onto a bar under 4–6 mm head jitter can tick once on arrival (vel 0.05–0.35): the jitter turns the
  last millimetres of a 0.2 m/s lowering into a fast crossing. A real mallet set down at 0.2 m/s ticks too. After it
  settles, no strike in 60 s.

**Lesson integration depends on R6.** Without it the pack sets no options and no accompaniment; the first lesson stays
winnable through the platform's B6 alone, and is quieter.

**Requests R1–R6 (6.3).**
- Without R1 (`MarimbaMid` and the per-source sends), the fallback hard split at E4/F4 moves the image between
  neighbouring lesson notes, and the lateral sources stay 1.3–1.8 dB wetter than the rule.
- R3 is a cosmetic conflict with the platform's display line.

**Layout and MR.**
- The platform's planner says piano + marimba cannot share a set, because both need the front. The marimba then
  replaces the piano: a platform behaviour, not a marimba bug.
- In MR the knees may meet the tubes (4.5).

**Strike-record contention.**
- The 32 strike slots assume a record is consumed within 112 ms: the engine's 100 ms late-drop window plus at most
  12 ms of contact.
- This holds unless the audio thread stalls for more than 1 s while the main thread keeps striking. A stalled record
  would then be overwritten: a wrong sound, but no crash.

## 12. Sources (numbers used as numbers; no recordings or samples)

| Topic | Source |
|---|---|
| Beam modes, marimba tuning, how partials evolve, resonators, mallets | N. H. Fletcher, T. D. Rossing, *The Physics of Musical Instruments*, 2nd ed., Springer 1998, section 2.15 (free-free bar 1 : 2.756 : 5.404 : 8.933, nodes 0.224 L) and ch. 19 (marimba 1:4:10, xylophone 1:3, the first three partials of a marimba bar over time on p. 539 "1:4:11", heavier mallets damp the highs) |
| Marimba and xylophone ratios | G. P. Scavone, CCRMA course notes "Percussion" (https://ccrma.stanford.edu/courses/150-2001/percussion.html): marimba 2nd partial 4.0, 3rd about 9.2; xylophone 3.0; uniform bar 1 : 2.76 : 5.40 : 8.90 |
| Overview | T. D. Rossing, *Science of Percussion Instruments*, World Scientific 2000, the chapter on marimbas, xylophones and resonators |
| Practical tuning | I. Bork, "Practical tuning of xylophone bars and resonators", *Applied Acoustics* 46 (1995) 103–127 |
| FE tuning | J. Bretos, C. Santamaría, J. A. Moral, *JASA* 102 (1997) 3815 and *Applied Acoustics* 56 (1999) 141–156; D. Beaton, G. Scavone, "Three-dimensional tuning of idiophone bar modes via finite element analysis", *JASA* 149 (2021) |
| Bar physics | B. H. Suits, "Basic physics of xylophone and marimba bars", *Am. J. Phys.* 69 (2001) 743–750 |
| Time-domain models | A. Chaigne, V. Doutaut, "Numerical simulations of xylophones. I", *JASA* 101 (1997) 539–557; V. Doutaut, D. Matignon, A. Chaigne, "… II. Time-domain modeling of the resonator and of the radiated sound pressure", *JASA* 104 (1998) 1633–1647 |
| Resonator practice | L. H. Stevens, "The Acoustics of Resonators" (*Percussive Notes* essay, malletech.com): a tube slightly above the bar is best; tubes get sharper toward the top, where they are "more like reflectors"; a sharp tube shortens the decay, a flat one is thin; a bigger diameter strengthens the fundamental; moving the tubes away decouples them |
| Decay span | US Patent 4,411,187 (composite marimba bars): "two seconds to 1/50 second from tenor C … to C7"; xylophone "one second to 1/50 second" |
| The resonator's role | Percussion Clinic Adelaide, "Marimbas: exploring the depths" (resonators amplify the fundamental, more so at low frequencies) |
| Secondary | T. Kumar, *Übekabine eines Marimbaphons*, MSc thesis, TU Graz 2018, section 2 (quotes Fletcher and Rossing p. 539, on mallets and partials) |
| Wood | U. G. K. Wegst, "Wood for sound", *Am. J. Botany* 93 (2006) 1439–1448 |
| Air columns | A. H. Benade, *Fundamentals of Musical Acoustics* (1976): wall loss 3e-5·√f/a; open-end reflection (ka)²/2 (Levine and Schwinger 1948) |
| Project | `12-new-instruments/platform.md`; `10-upgrade/burst.md`, `ENGINE-API.md`, `drums.md` (the critics' lessons), `harp.md`, `room.md`; `02-meta-hands.md`; u-arrange `ARRANGE-NOTES.md` (Meta's pinch, 17 mm tip joints, seated heights); `11-first-five/sunny-tour.md`, `lessons.json`, `tutorials-audit.md` |

## 13. ARCHITECTURE section 21 entry (draft, for step 4 of platform.md 13)

> **New instrument: marimba (12-new-instruments/marimba.md).**
>
> - **Module.** `MusicRoom.Marimba` (Order 35, TickOrder 23, Kind / Rig / Unit 7, carousel 5) with `MarimbaDefinition`
>   (seat Front at (0, 0, .50); display `window-near`), `MarimbaInstrument` (all of platform 3.3),
>   `MalletToolProvider` (`ToolKind.Mallet`, FMM) and `MarimbaPlay` (stroke, dead stroke, mallet rest, hand mute,
>   roll). Visuals: the bar dip, mallet rebound and contact shadows; 5 draws at the seat, 3 on display.
>   Strokes are detected for VR (overshoot, jitter): hold bands, 60 ms stillness, two-sample hysteresis, the approach
>   speed, the index fingertip first; dead strokes are off until Sunny's tip and in lessons.
> - **Audio.** `Audio/Sound/Marimba`: a main-thread kick-drift-kick contact solve (2–15 µs in Node) plus MB4 free banks;
>   151 KiB; about 20–66 µs per block at level 3; unit-only; sources MarimbaLo / MarimbaMid / MarimbaHi with room sends
>   −2 / 0 / −2 dB (request R1).
> - **Content.** Lesson pack `marimba.json` (3 lessons; the first accompanied through `ILessonAccompany`, request R6;
>   Mary's sticking never crosses the hands); Sunny: 9 tips and 9 lines.
> - **Tests.** TV1–TV8, IX1–IX18 plus the realistic-stroke suite, conformance 1–20, device checks M1–M9 (M5b).

---------------------------------------------------------------------------------------------------------------------------

## 14. Revision 2: the critique and what changed

The critic (the lab's `critic/`, verdict "ready with fixes") judged the sound engine in good shape and found the
problems in the bare-hands interaction, the first lesson and the spec. Their scripts were re-run on this revision; their
earlier outputs are kept in `out/critic-baseline/`, and `out/critic-c3-before-after.json` holds the interaction numbers
side by side. Nothing was run on a headset or in Unity, and nobody has listened.

| # | Severity | Critic issue | Fix | Before → after (measured) |
|---|---|---|---|---|
| 1 | High | Ordinary VR strokes become dead strokes (any depth below the plane counted as "down"; single-frame speed); spec text vs IX3 | One rule (5.4): raw head in the band plane −25 … +10 mm for 200 ms unbroken (3-sample breaks), then still over a 60 ms window on 2 samples; mallet rest the same band and 150 ms, only after a silent crossing; dead strokes off in lessons and until `tip_marimba_dead` (`musicroom.marimba.deadStroke`). IX3 (10 mm under the plane) is inside the band; IX13 added | Critic c3, mean dead-stroke share over ordinary strokes: 44 % → 0 %; jitter-free 25 % → 0; with a 60 ms pause 67 % → 0; 4 mm jitter 33 % → 0. Own suite (2 160 strokes, up to 100 ms pause and 6 mm jitter): 0 with pauses ≤ 60 ms, 0.09 % with 100 ms. Deliberate holds of 0.3–0.45 s: 94–100 % |
| 2 | High | Fingertip taps choke themselves, are buried under the accompaniment, and a lower neighbouring tip sounds the wrong bar | (a) the tip that sounded a bar never counts toward its hand mute until it rises 20 mm; hand mutes need ≥ 2 tips for 120 ms; (b) fingertip re-modelled (20 g, 1.8 ms pad) with the mallets' speed curve and a per-bar voicing, 2 LU under a mallet (2.4); (c) accompaniment at 0.35 × the player's last vel (7); (d) one stroke per hand per sample (faster, then the index), and a non-index tip waits up to 25 ms for a descending index over another bar | Tap-and-stay self-mutes 5 of 8 → 0 of 8. E4 taps 0.25 / 0.6 / 0.9 m/s: −56.2 / −38.0 / −31.9 → −37.2 / −25.9 / −21.6 LUFS; the lesson's accompaniment margin at 0.6 m/s: 3.5 → 8.8 LU (−7.6 → +3.4 LU at the softest tap). Wrong bar with the middle tip 4 / 8 mm lower: 13 / 24 % → 2.0 / 1.8 % (the index's own aim error sets the floor) |
| 3 | Medium | The sharps' advice and guide anchor (u .12) point at the cord node's quiet span | Anchor `bar.<note>.tip` at u 0.04; 5.2 rewritten (tip u ≤ 0.06 or centre); `END_REACH` 10 mm in front of a sharp's near end; new line "Sharps are the back row. Strike their nearest tip." (9 words; the critic counted 8); IX4, IX4b | At the anchor: −8.5 → −1 … −2.5 LU (F#4); aiming with 11 mm error: median −6.9 → −2.3 LU. Every sharp's tip is within 0.44–0.56 m of the shoulder (critic c5). Aiming at u .04 with 11 mm error: 2.5 % of strokes miss the bar (14 % without `END_REACH`) |
| 4 | Medium | The 8 m/s glitch guard throws away full strokes | No upper speed limit (vel 1 at and above v1); a jump is a sample moving more than max(0.15 m, 14 m/s × dt) or following a low-confidence sample; IX14 | 4–10 m/s strokes at 72 and 90 Hz: all sound once at vel 1. IX9 still silent |
| 5 | Medium | `ARM_ABOVE` 12 mm lets a resting mallet re-arm and play under lever jitter | 20 mm for mallets (12 mm fingertips) on 2 consecutive samples; an appearing pointer starts disarmed for every bar it is not clearly above; mallet mutes lift on 3 samples above 20 mm; M4 logs the resting head jitter | Critic ghost test, 20 s at 6 mm jitter: 25 phantom strikes and 10 mute toggles (12 mm) → 0 and 0. Own 60 s rest at 2–6 mm: 0 phantom strikes, 1 mute, no toggles |
| 6 | Medium | Mary's R L R L sticking crosses the hands on 7 of 26 notes | A dynamic-programme sticking that never crosses (7) | 7 → 0 crossings |
| 7 | Medium | Fingertip contact solves cost 46–100 µs (spec: 4–38) | Kick-drift-kick, 2nd order, with the Hunt-Crossley term solved at the step's end; `OS_BY` medium 2, hard 2, soft 1, finger 1 (2.4) | Fingertip 46–100 → 4.3–14.6 µs median per strike (Node); every mallet 2–15 µs; worst mode error vs OS 16: 0.25–0.38 dB → ≤ 0.08 dB |
| 8 | Medium | The lesson pack uses options that do not exist (`AnyImplement`, "Forgiving after 2 misses"), never turns `Accompany` on, and has no path for a marimba accompaniment | `options: ["Accompany", "QuietMistakes"]`; forgiveness from B6 only; `Matches` already takes any mode; contract request R6 (pack options and `ILessonAccompany`) | The pack and 5.6 / 7 / 6.3 agree; `tools/lesson-balance.mjs` passes |
| 9 | Spec | The tray drawing does not add up (shafts overlap, heads 11 cm off the tray) | A 0.82 m tray, mallets end to end, heads outward, butts 20 mm apart; grips at ±0.11 m; box A widened (4.1, 4.2, 5.1) | Heads end at ±0.402 inside the tray's ±0.41 |
| 10 | Spec | The short-tube statement contradicts the knee section | Ten tubes (D#5, E5–C6) are drawn 120 mm long with an inner stopper; the knee section lists the tubes in the knee span (B3–C5) with their computed bottoms (4.2, 4.5) | Consistent with `design.mjs` |
| 11 | Spec | The Sunny triggers `fingerOnly` and `offCentre` are undefined | Defined in 5.6 (signal table) and emitted by the reference (IX18) | – |
| 12 | Spec | The boot re-tunes the bars (about 4.5 s in Node), not "0.3 s" | The tuned arch is stored (`ARCH`); boot runs two FE solves; the retune is an EditMode test (6.2) | Boot 2.9 s → 0.14 s in Node; `--retune` reproduces `ARCH` exactly; TV unchanged by this step |
| 13 | Spec | The Lo / Hi sources fail the seat-clarity rule (+2.8 / +2.3 dB against +4.1), with no contract request | Room sends Lo −2 dB, Mid 0, Hi −2 dB in the `AudioSourceTable` rows of R1; the room render and parity use them | +2.79 / +2.32 → +4.79 / +4.32 dB (Mid +5.67); the piano's own laterals +4.08 in the same harness |
| – | Note | The top octave has very little above 6 kHz and could sound dull | No change: nothing measures wrong, and brightening the top would fight the warm-wood brief. Added to M8 and the README as a listening check | – |

**Found while fixing (not in the critique).** A stroke that decelerates to a stop at the drawn bar top crosses the plane
slowly or only through jitter, so the crossing sample alone read it as silent: at 2 mm jitter, up to one in eight
deliberate dead strokes held at the plane never sounded at all. The approach speed (5.3 step 5) fixed it (now 96–100 %).

**Where this revision departs from the critic's proposed numbers, and why.**
- Band top +10 mm, not +6: with +6 mm a head held at the plane broke the band under 4–6 mm of jitter, and deliberate dead
  strokes fired only 77–95 % of the time.
- Fingertip: physics plus per-bar voicing, not "+8 dB": the revision-1 fingertip fell 9.8 dB from E4 to C5 (its contact
  low-passed the fundamental), so one gain could not fix the lesson range.
- Accompaniment: 0.35 × the last vel, not 0.5 or 0.12-for-fingertips: with the fingertip re-voiced, one rule serves both;
  0.5 left only 1.8–4.2 LU at the softest gestures.
- Neighbouring tip: "keep the faster tip" alone cannot separate two tips moving down together (the critic's own test
  case), so the index-first wait was added.
