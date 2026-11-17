# The singing bowls' sound unit (`Audio/Sound/Bowls`, assembly `MusicRoom.Audio`)

The Node bowls reference engine (`bowls-engine.mjs`, revision 2, and `bowls-design.mjs`) in Burst C#: three bowls, 8 split (n,0)
mode pairs each (16 modes = 4 MB4 groups with a quadrature output gain), a stick-slip friction contact per bowl owned by
one lane at a time (0 live, 1..3 playback), strike pulses, touch damping, rub noise, the 25 Hz output high-pass, the work
cap. Spec: `Docs/12-new-instruments/bowls.md` sections 2, 3.4-3.6, 8-11.

| File | What |
|---|---|
| `BowlsDesign.cs` | `BowlsConst` (every constant), `BowlsDesign.Bowls(sr)` (double maths rounded once, V8's `Math.hypot`), `Build(state, ...)` |
| `BowlsState.cs` | `BowlState` × 3 + the pulse table (cos/sin π/K, K 2..600) + the render scratch: one zeroed 64-byte aligned allocation (≈ 40 KB with the scratch; the state proper ≈ 6 KB) |
| `BowlsDsp.cs` | The render: `Render` (plain C#), `RenderFast`, `RenderStrict`, `RenderStrictFp` (+ `CompileStrictPointer`), `RenderUnit(UnitSlot*, share)` (the `UnitHooks.RenderBowls` body: Reset, readiness, the work cap from Budget / 1200 × Share, chunks of ≤ 1024 frames, the Strict pointer, `u->Work`) |
| `BowlsCommands.cs` | The main thread's command builders (double maths, as the reference computes before posting) |
| `IBowlsSound.cs` | `IBowlsSound` (bowls.md 3.4), `IBowlsCommandSink`, `BowlsSoundCore` (commands + stats + the 0.5 s contact-lost window) |
| `BowlsUnit.cs` | The `SoundUnit` the platform creates (`SoundUnits.CreateBowls`): `UnitId.Bowls` on `SourceId.Bowls`, idle skip, every public call recorded (`RecordUnit`), `ILoudnessReference` (four taps at 108 bpm); `UnitHooks.RenderBowls` → `BowlsDsp.RenderUnit(u, u->Share)` |

## The soft pad (the app's unit only)

`BowlsDesign.SetPad(state, sr, on)` (`BowlsUnit` turns it on; `BowlsRig` and the reference vectors leave it off, so they
stay bit-exact): on an outer contact the force the bowl receives is the stick's dynamic force blended `PAD_MIX` 0.3
toward N, low-passed at `PAD_HZ` 1.2 kHz (one-pole, `PadF`), and below `PAD_N` 5 N the stick is pressed at 5 N with the
bowl's forces scaled by N / 5 N. A light, grazing or releasing contact on a loud bowl no longer bounces the stick off the
wall with on / off friction and hard re-impacts (the headset's "broken cassette"). The lost-contact statistic still reads
the stick's own force. Cost: a few multiply-adds per contact sample.

## The tonal rub noise (the app's unit only)

`BowlsDesign.SetRubNoise(state, sr, on)` (`BowlsUnit` turns it on; `BowlsRig` and the reference vectors leave it off, so
they stay bit-exact): the rub noise (xorshift, band-pass 1.8 kHz Q 0.5, low-pass 5 kHz) is scaled by `RUB_NOISE_SCALE`
0.5 and capped at `RUB_NOISE_REL` 0.12 x the bowl's own tone envelope (`ToneEnv`, a one-pole of |modal output|,
`TONE_TAU` 20 ms). The reference's noise follows the friction power alone, so a rub that had not (yet) made the bowl sing,
or a fast or deep one, was all hiss: on the headset the noise bins sat 3 to 18 dB under the fundamental per 10 Hz bin
(the "broken cassette"). Now the friction noise sits at least 30 dB (typically 55 to 60 dB) under the fundamental per bin
and swells with the sing. Strikes are bit-identical (the noise only runs on a contact). Cost: three multiply-adds per
contact sample.

## Commands (`GraphCommand`, `Target` = bowl 0..2)

The main thread computes in double every value the reference computes in double on its way into the unit, so the audio
thread sees float32 only and stays bit-exact:

| Op | Payload |
|---|---|
| 1 Strike (`LateDrop`) | A, B = the two half-sine peaks (0.6 I over T, 0.4 I over 0.72 T); N, Id = their lengths K (samples); C, D = cos, sin of the angle; E = height factor |
| 2 Contact | A = N (newtons); B = v_f (m/s); C, D = cos, sin of the angle; E = Ω (rad/s); F = height factor; G = cos and `Seed` = the float bits of sin of the target's per-sample rotation; N = surface \| lane << 8; Id = ramp samples (0 = default) |
| 3 Release | N = lane |
| 4 Damp | N = level 0..3 |
| 5 Pickup | C, D = cos, sin of the listener angle; N = snap |
| 6 Seed | Seed |
| 7 Silence | — |

(bowls.md 2.10 lists the reference's fields; the layout above carries the same information precomputed.)

## Results (phase A)

- **Vectors.** TV1-TV16 (`out/test-vectors.json`): **bit-exact** (FNV-1a over every sample, the stats, the retire block)
  under Burst `FloatMode.Strict` and through the Strict function pointer called from a `FloatMode.Fast` entry (the graph's
  situation), and under the standalone Mono. In the Unity editor the plain managed path differs by ≤ 7e-4 of the peak
  (linear vectors) because the editor's Mono keeps float intermediates in higher precision; `FloatMode.Fast` likewise
  (≤ 7e-4 linear; the chaotic contact vector TV9 up to 3.3 dB block RMS).
- **Design.** Every table value equals the reference's fingerprint (poles, gi, ω, oc/os, Y0t, outScale, rad).
- **Renders.** All 12 demo phrases rendered through the unit null against the Node renders at **−125.8 to −129.3 dB**
  (one 24-bit LSB at most); the room piece's dry channel is identical sample for sample
  (`Builds/Listen/bowls/bowls-compare.txt`).
- **Physics.** The contact phasors stay within **1.3e-6** of unit magnitude over a 10-minute rub (review item 10) and the
  bowl still sings; damping T60 6.91 / 0.86 / 0.26 s (design 7.0 / 0.81 / 0.28); the inside 20 dB under the outside; one
  light finger keeps 98-99 % of the song; the tap-then-rub sweep 0 / 576 dead spots; no subnormal state; the mf phrase
  −25.13 LUFS (−1.12 LU against the piano), rubs +1.48 / +5.05 LU.
- **Cost** (editor, Burst, M5 Pro, per 256-frame block; Strict pointer / Fast): idle 0.6 / 0.8 µs; three ringing 6.2 /
  5.8 µs; one rubbed + two ringing 14.3 / 13.2 µs; two rubbed + one ringing 22.3 / 19.3 µs; **worst (three rubbed, chatter)
  27.8 µs avg, 40.5 µs p99** / 21.9 avg, 25.8 p99. Quest 3S estimate ×3-4: ≈ 0.1-0.16 ms worst p99, under the 0.25 ms cap.
- **Allocation.** None in the render (managed and Strict) or in the main-thread API (tested).
