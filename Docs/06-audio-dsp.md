# 06 · Audio DSP spec: recipes (Unity C#)

Every sound in VR Music Room is synthesized by our own code. This spec gives each recipe exactly, plus the signal-graph behaviour the C# code has to reproduce. Everything was measured on offline renders at 48 kHz (2026-10-25) of the full voice graphs, with the recipes unmodified.

**Companion files**
- `Docs/06-audio-dsp.reference.mjs`: a dependency-free reference DSP in plain JavaScript (Node): biquads, the band-limited oscillator tables, envelopes, the noise/tone/metal voices, every drum hit, the piano key render, the old-harp Karplus-Strong, the impulse responses, convolver normalization and the measured compressor. Each function reproduces the measured graph render (§12). **Transliterate this file to C#.**
- The measured facts (compressor curve and dynamics, panner law, HRTF levels, oscillator shapes), the reference SNRs (§12), seeds and fingerprints are frozen in the EditMode audio tests (`Tests/EditMode/Audio`).
- `Tests/EditMode/Audio/Reference~/*.wav`: 34 dry reference renders of the synths (mono, float32, 48 kHz, seeded). These are the A/B and null-test targets.

## 0. Ground rules

- **Defaults the user likes:** piano = `PianoSynth` (synthesized; there is no sample set); drums = `DrumSynth` live voices; harp = version `'old'`.
- **Sample rate:** every buffer is rendered at the output rate. The recipes were measured at 48000. The device rate is UNVERIFIED; assume 48000. In Unity use `AudioSettings.outputSampleRate`. Some constants are per sample (the KS `bright`, the room-IR `lp` one-pole, the 32-frame compressor division), so their sound is defined at 48 kHz.
- **Randomness:** the app draws from an unseeded uniform generator. That covers the noise buffer, the per-note jitter, the piano hammer noise, the KS excitation, the IR noise and phases, the fire pops and the gust centre. Any uniform PRNG works at run time. Tests use mulberry32 (below) with the frozen test seeds; with those seeds the C# code must equal the fingerprints.
- **Coordinates:** source-frame positions (right-handed, −z forward) → Unity (x,y,−z).
- **The reference's `Math.round(x)` is `Math.Floor(x + 0.5)`**, not C# banker's `Math.Round`.

```csharp
public sealed class Mulberry32 { // seed 1 → 0.6270739405881613, 0.002735721180215478, ...
    uint a; public Mulberry32(uint seed) { a = seed; }
    public double Next() { unchecked { a += 0x6D2B79F5u; uint t = a; t = (t ^ (t >> 15)) * (t | 1u);
        t ^= t + (t ^ (t >> 7)) * (t | 61u); return (t ^ (t >> 14)) / 4294967296.0; } }
}
static double Mtof(double m) => 440.0 * Math.Pow(2, (m - 69) / 12.0);
```

**Shared white noise**: one buffer of `2*sr` samples, `noise[i] = (float)(R()*2-1)`, made at start. **Every noise voice reads it from index 0** (started at t with no offset) and loops at 2 s, so every hit uses the same grain. The ambience beds loop it from the same start too.

## 1. Signal-graph semantics to emulate (all verified)

### 1.1 Parameter automation (`Envelope`)
- `set(V,T)`: V from T on. Before the first event the param has its default value (measured 1 for gain).
- `linear(V1,T1)`: v = V0 + (V1−V0)(t−T0)/(T1−T0) from the previous event (T0,V0).
- `exponential(V1,T1)`: **v = V0·(V1/V0)^((t−T0)/(T1−T0))**; holds V1 afterwards (measured exactly).
- `setTarget(V1,T0,τ)`: **v = V1 + (V0−V1)·e^(−(t−T0)/τ)**, with V0 the value at T0 (measured exactly). Per sample: `v = V1 + (v−V1)*k`, `k = Math.Exp(-1/(τ*sr))`.
- `cancel(t)` removes events at ≥ t. An earlier `setTarget` keeps running, which is why the ambience ramps are continuous.
- Gain is evaluated per frame at t = frame/sr. Parameter values are float32: `0.4` is stored as `0.4000000059604645`. This matters only for bit-exactness.

### 1.2 Biquad filters (the measured frequency response matched to < 3e-6 dB)
ω0 = 2πf/sr, A = 10^(G/40), αQ = sin ω0/(2Q), **αQdB = sin ω0/(2·10^(Q/20))**, αS = (sin ω0/2)·√2 (S = 1).
- lowpass: b = ((1−cos)/2, 1−cos, (1−cos)/2), a = (1+αQdB, −2cos, 1−αQdB)
- highpass: b = ((1+cos)/2, −(1+cos), (1+cos)/2), a as for lowpass
- bandpass (constant 0 dB peak): b = (αQ, 0, −αQ), a = (1+αQ, −2cos, 1−αQ)
- peaking: b = (1+αQ·A, −2cos, 1−αQ·A), a = (1+αQ/A, −2cos, 1−αQ/A)
- highshelf: b0 = A[(A+1)+(A−1)cos+2√A·αS], b1 = −2A[(A−1)+(A+1)cos], b2 = A[(A+1)+(A−1)cos−2√A·αS], a0 = (A+1)−(A−1)cos+2√A·αS, a1 = 2[(A−1)−(A+1)cos], a2 = (A+1)−(A−1)cos−2√A·αS

**Trap: lowpass/highpass Q is in dB.** |H(f0)| = Q dB, so the linear Q is 10^(Q/20). The app's values are: the default Q = 1 → 1.1220, a slightly resonant +1 dB bump (noise/tone voice hp/lp, metal hp, fire 420 Hz, rumble 260 Hz, gust hiss); Q 0.4 → 1.0471 (playBuffer, harp new); Q 0.3 → 1.0351 (snow hush); Q 0.7 → 1.0839 (robot high-pass). None of these is a Butterworth 0.707. Implementation: Direct Form I, double coefficients, output rounded to float (matches the measured output to ~1e-8). The defaults are type lowpass, f 350, Q 1, gain 0.

```csharp
public struct Biquad { double b0,b1,b2,a1,a2,x1,x2,y1,y2;
  public float Tick(float x){ float y=(float)(b0*x+b1*x1+b2*x2-a1*y1-a2*y2); x2=x1;x1=x;y2=y1;y1=y; return y; } }
```

### 1.3 Oscillators
- The phase starts at 0, so sine, square and triangle all start at 0 and rise (measured). Each output sample reads the table at `phase`, then `phase += f*N/sr`.
- Coefficients: sine b1 = 1; square b_n = 4/(nπ) for odd n; triangle b_n = 8/(π²n²)·(−1)^((n−1)/2) for odd n. The waveform is Σ b_n sin(nθ).
- **Band-limited periodic-wave tables:** N = 4096 for 24 k < sr ≤ 88.2 k (verified at 48 k; the other-rate breakpoints are UNVERIFIED); 36 ranges; range r keeps partials 1..min(2047, ⌊2^(−r/3)·2048⌋). Every table is scaled by 1/max|range-0 table|, so **the square's plateau is about ±0.84 and its peak ±0.99, not ±1** (measured mean|x| 0.8434 at 263 Hz). For a frequency f: `pr = clamp(1 + 3*log2(f/(sr/2/2048)), 0, 35)`, r1 = ⌊pr⌋, r2 = min(r1+1, 35); sample = (1−(pr−r1))·T[r1](phase) + (pr−r1)·T[r2](phase), each table read with linear interpolation. Re-select the tables per frame while the frequency ramps. The reference `oscillator()` matches the measured oscillators at 94–121 dB SNR. A PolyBLEP square scaled by 0.848 is an acceptable approximation, but it is not sample-exact.
- `detune` multiplies the frequency by 2^(cents/1200).
- Table build in C#: Σ over kept partials of `b_n * SIN[(n*j) % 4096]`, about 20 M multiply-adds per waveform. Do it once at load, or generate in the editor and ship as data.

### 1.4 Other nodes
- **Buffer player:** the playback rate uses linear interpolation (measured 0, 0.5, 1, 1.5 at rate 0.5) and is set once per 128-frame block. A looping buffer wraps at its end.
- **Delay line:** fractional delays use linear interpolation (2.25 frames → 0.75/0.25 at frames 2 and 3). Inside a feedback cycle the delay is clamped to ≥ 128 frames (one render quantum); the robot comb's 240 frames are not affected.
- **Convolver:** normalization is on by default (measured). The IR is multiplied by `scale = (1/max(rms,1.25e-4)) * GC * (44100/sr)`, with rms taken over all channels and samples. **GC = 10^(−58/20) = 0.0012589254** (measured; the rounded 0.00125 is 1.00714 times too small). The effective IR therefore always has rms 0.0012589·44100/sr (1.15664e-3 at 48 k), so any scaling inside the IR generators is irrelevant. A mono input with a stereo IR gives a stereo output (L = x∗hL, R = x∗hR). There is no latency.
- **Distance panner:** HRTF, inverse law, refDistance 0.6, rolloffFactor 1.1, maxDistance 30, omni cones. **Gain = 0.6/(0.6 + 1.1·(max(d,0.6) − 0.6))**. Measured: 1 m 0.5769, 2 m 0.2804, 5 m 0.1103. The inverse law does not clamp d at maxDistance (40 m → 0.01365). The measured HRTF level for white noise at ≤ 0.6 m: front −5.0 dB per ear, 90° right L −13.4 / R −1.3 dB, behind −8.9 dB. **Calibrate the Meta HRTF's front loudness to −5 dB per ear.**
  - Unity: Meta XR Audio's own distance attenuation is unused (`P_USEINVSQR`/`P_NEAR`/`P_FAR` are marked "[UNUSED]", Library/PackageCache/com.meta.xr.sdk.audio@3292c33a8423/Runtime/scripts/MetaXRAudioSource.cs:171-182), so the law comes from the AudioSource. Use `rolloffMode = AudioRolloffMode.Custom` and `SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve)` with the gain above and `maxDistance` 30. These APIs are documented in the editor's `Contents/Resources/Scripting/Managed/UnityEngine/UnityEngine.AudioModule.xml` at lines 2253, 2724 and 2768. That the curve's x axis is d/maxDistance is UNVERIFIED.
  - The listener follows the camera position, forward and up through `setTarget(…, τ 0.02 s)` every frame. Source positions are set directly.

### 1.5 Master compressor (`DynamicsCompressor`)
The master uses threshold −14, knee 18, ratio 4, attack 0.003, release 0.18. Its behaviour (measured): **6 ms look-ahead (288 frames, added latency)**, **automatic makeup (1/curve(1.0))^0.6 = +2.137 dB**, a **linked stereo** detector (max |x| over channels), a static curve that is identity below −14 dBFS, the soft knee `linThr + (1−e^(−k(x−linThr)))/k` (k = 1.51254 by bisection so the slope at the knee end is 1/ratio), and slope 1/4 above +4 dBFS. The envelope rate is updated once per 32 frames. Attack: `rate = 1 − (0.25/max(0.5,maxDiffDb))^(1/(attack*sr))`. Release is adaptive (a 4th-order polynomial over the 0..−12 dB compression difference, release zones 0.09/0.16/0.42/0.98, 5 dB spacing). There is a 2.5 ms detector release, a `sin(π/2·gain)` warp, and a start-up transient: just after creation the gain is about 14 dB below its resting level, reaching −1.8 dB by 50 ms and +2.14 dB by 200 ms. Measured steady gain for a sine at peak input: −12 dBFS +2.07, −6 +1.08, 0 −1.34, +6 −5.37, +12 −9.75 dB. After a 0 dB burst the release returns to +2.14 dB within 150 ms. **`makeCompressor()` in the reference file reproduces the measured compressor exactly** (impulse, step and static tests, independent of chunk size); translate it line for line. It holds the polynomial constants and the `KAtSlope` bisection. Drum transients do drive this compressor, so keep it.

### 1.6 Master chain and routing
```
voices → [panner per source name: HRTF, inverse law] ─┐
non-spatial voices (page-turn swish) ─────────────────┼→ master in (gain 0.9) → compressor → highshelf 7500 Hz +1.5 dB (S=1) → out
reverb sends (mono) → main reverb convolver (stereo IR) → gain 0.26 ─┘   (the reverb return is head-locked, not spatialized)
```

## 2. Generic voices: the building blocks

All start at time t and are mono. `env`: gain = P·(0.0005/P)^((t−t0)/decay) for t0 ≤ t < t0+decay, then 0.0005, with P = max(peak, 0.0005). The attack is instant, and **decay is the time to reach the absolute level 0.0005**, so a quiet voice dies sooner in dB terms. The source stops at t0 + decay + 0.05 s; the filters ring on.

```csharp
static double Env(double t, double peak, double decay){ double P=Math.Max(peak,5e-4); return t>=decay?5e-4:P*Math.Pow(5e-4/P,t/decay); }
// noiseVoice(t,{hp=0,lp=20000,bp=0,q=1,peak,decay,send=0,dest}) — chain order: bandpass(bp,q linear) → highpass(hp, Q=1 dB)
//   → lowpass(lp, Q=1 dB; only if lp<20000) → gain env → dest (default master); env gain × send → main reverb.
// toneVoice(t,{type='sine',f0,f1=f0,slide=0.05,peak,decay,send=0,lp=20000,dest}) — frequency f0, and if f1≠f0
//   exponential ramp to f1 over `slide` → [lowpass(lp, Q=1 dB)] → env gain → dest; × send → main reverb.
// metalVoice(t,{ratio=1,bp=9500,hp=6500,peak,decay,send=0,dest}) — six squares at {263,400,421,474,587,845}×ratio,
//   summed → bandpass(bp, Q 0.9) → highpass(hp, Q=1 dB) → env gain → dest; × send → main reverb.
```
Reverb sends are taken from the voice gain, **before** any drum level or choke gain.

## 3. Piano (`PianoSynth`; the default)

### 3.1 Per-key buffer, rendered offline (render)
For midi 21..108: u = (midi−21)/87, seconds = clamp(7−5.6u, 1.1, 7) (so 1.4–7 s), n = ⌊sr·seconds⌋. Inharmonicity B = 8e-5 + 7e-4·u²; partials stop at min(0.45·sr, 15000) Hz; the unison detune is 1.2 + 2.5u cents (±half on two strings).
```csharp
double f=Mtof(midi), u=(midi-21)/87.0, seconds=Clamp(7-5.6*u,1.1,7), B=8e-5+7e-4*u*u, nyq=Math.Min(0.45*sr,15000), det=1.2+2.5*u;
int n=(int)Math.Floor(sr*seconds); var acc = new double[n];
for (int k=1;k<=12;k++){ double fk=f*k*Math.Sqrt(1+B*k*k); if(fk>nyq) break;
  double amp=Math.Pow(k,-(0.9+0.75*u))*(k==1?1:0.9), tau=seconds/2.9/(1+0.5*(k-1));
  double tEnd=Math.Min(seconds,0.12+5.5*tau), a2=amp*0.45, a3=Math.Max(amp*0.001,1e-5);
  foreach (double c in new[]{-det/2,det/2}){ double w=2*Math.PI*fk*Math.Pow(2,c/1200)/sr;   // sine, phase 0
    for(int i=0;i<n;i++){ double t=(double)i/sr;
      double g = t<0.004 ? 1e-4*Math.Pow(amp/1e-4,t/0.004)          // exp ramps: 1e-4 → amp @4 ms
               : t<0.12  ? amp*Math.Pow(0.45,(t-0.004)/0.116)        // → 0.45·amp @120 ms
               : t<tEnd  ? a2*Math.Pow(a3/a2,(t-0.12)/(tEnd-0.12)) : a3;  // → 1e-3·amp @tEnd, hold
      acc[i]+=g*Math.Sin(w*i); } } }
// Hammer thump: m=⌊0.03·sr⌋ fresh noise samples (float)(R()*2-1) → bandpass(clamp(2.5f,300,4000), Q 0.8)
//   × [g0=0.5−0.3u exp-ramp → 0.001 @25 ms, then hold] ; the filter keeps running on zeros after m.
int m=(int)Math.Floor(sr*0.03); var nz=new float[m]; for(int i=0;i<m;i++) nz[i]=(float)(R()*2-1);
var bp=Biquad.BandPass(Clamp(2.5*f,300,4000),0.8,sr); double g0=0.5-0.3*u;
for(int i=0;i<n;i++){ float x=bp.Tick(i<m?nz[i]:0f); double t=(double)i/sr; acc[i]+=x*(t<0.025?g0*Math.Pow(0.001/g0,t/0.025):0.001); }
var d=new float[n]; for(int i=0;i<n;i++) d[i]=(float)(acc[i]*0.5); Normalize(d,0.9,sr);
static void Normalize(float[] d,double peak,int sr){ double m=0; foreach(var v in d)m=Math.Max(m,Math.Abs(v));
  if(m>0) for(int i=0;i<d.Length;i++) d[i]=(float)(d[i]*(peak/m));
  int fade=Math.Min(d.Length,(int)Math.Floor(sr*0.05)); for(int i=0;i<fade;i++){int j=d.Length-1-i; d[j]=(float)(d[j]*((double)i/fade));} }
```
The reference matches the measured render at 78–105 dB SNR. For speed, use per-segment geometric multipliers and a rotating phasor in place of the per-sample `Pow`/`Sin`; that is exact to about 1e-9. Render order (precompute): 3 lanes, middle-out 60, 61, 59, 62, 58, … **Keys 105–108 are never precomputed** (d ≤ 44) and render on first press. Memory is about 71 MB of float32 for 88 keys at 48 kHz.

### 3.2 Play stage (play, through `playBuffer`)
- vel = clamp(vel, 0.05, 1); level = 0.08 + 0.8·vel²; cutoff = clamp(1.5f + 300 + vel^1.6·11000, 500, 16000).
- Jitter scales by `PIANO_JITTER` = 0.25. The draw order is rate, cutoff, level: `rate = 1+(R()−0.5)·0.0032` (±2.8 ¢), `cut = cutoff·(1+(R()−0.5)·0.1·0.25)`, `lvl = level·(1+(R()−0.5)·0.12·0.25)`.
- Graph: buffer@rate → lowpass(cut, Q 0.4 dB) → gain lvl → source by register (`midi<52` 'piano-lo', `<76` 'piano-mid', else 'piano-hi'). The same lvl output also goes × 0.22 → main reverb, and × (0.6+0.4·vel) → **soundboard** convolver, whose output × 0.22 → 'piano-mid'.
- Soundboard IR = `makeBodyImpulse([95,142,187,236,298,372,450,560], 0.28)` (§6), normalized.
- **Hammer knock**, on every press, at the same source, with no reverb: noiseVoice(bp clamp(1.6f+900, 600, 3200), q 1.1, peak 0.05+0.3·vel², decay 0.007+0.004·vel).
- **Release**: `cancel(tt); set(g.value,tt); setTarget(0,tt,0.06)`, source stop at tt+0.5. The first release also fires `onRelease` = **damper thud**: noiseVoice(now, bp 260, q 0.8, peak 0.028, decay 0.03) at the note's source.
- When releases happen (`Docs/05-instruments.md` §2.5): key-up only marks the note sustained (automatic pedal). A new phrase lifts the pedal: no key held and more than 0.6 s since the last release (`PHRASE_GAP`). Every sustained note is then released, with one damper thud for the whole lift. Re-striking a ringing key releases its old voice first, which gives that key's thud.
- Velocity in (`Docs/05-instruments.md` §2.3): x = clamp(ln(vy/0.07)/ln(1.5/0.07), 0, 1); level = 0.88·10^(x−1); vel = clamp(√(max(0,level−0.08)/0.8), 0.1, 1). Desktop keys use 0.75.

## 4. Drums (`DrumSynth`; live voices, the default)

**Per hit** (drum()): vel = clamp(vel,0.05,1), v2 = vel², e = pos = clamp(strike.pos ?? 0.3, 0, 1), c = 1−e, zone = strike.zone || 'head'.
- All layers sum into a per-hit gain `dest` = **level 1.7** (`drumLevel`), which feeds 'drum:'+kind; hatOpen and hatClose use 'drum:hat'.
- `dest` × room send (kick 0.35, crash/ride 0.2, everything else 0.5) → **drum room** convolver → × 0.3 → 'drum:room'.
- Metal layers get bp × (0.85 + 0.3·vel). A layer's own `send` goes to the main reverb before the 1.7.
- **Snare wires**: on kick, tom1, tom2 and floor, add noiseVoice(t+0.004, hp 2800, lp 9000, peak 0.07·vel, decay 0.08) straight to 'drum:snare', without the 1.7 and without a room send.

Layers (T = toneVoice, N = noiseVoice, M = metalVoice; listed as peak, decay):

| kind / zone | layers |
|---|---|
| kick | T sine 165→46 slide .09, 1.0vel, .42+.1vel, lp 2500 · N bp 3200 q .7, .34v2, .02 |
| snare rim | N bp 3400 q 1.4, .9vel, .05 · T sine 1180→950 s .012, .55vel, .11 · T tri 195→150 s .05, .3vel, .16 · N hp 1400 lp 9000, .4vel^1.3, .18, send .3 |
| snare head | T tri 195→150 s .05, .5vel(.7+.3c), .18+.06e · T sine 330→280 s .04, .3vel(.5+.9e), .1+.1e · N hp 1400 lp 9000, .66vel^1.3(1−.35e), .2+.08vel+.06e, send .3 |
| hat (closed) | M bp 8500 hp 5000, 1.3vel, .08+.03vel+.05e · N hp 7000, .75vel(1+.3e), .07+.05e |
| hatOpen | M bp 8500 hp 5000, 1.05vel, .42+.15e, send .1 · N hp 7000, .6vel, .35+.15e |
| hatClose ("chick") | M bp 7000 hp 4500, .6vel, .05 · N hp 6000, .45vel, .035 |
| tom head | T sine f0→f1 slide, P·vel(.75+.25c), D(1+.3e), send .15 · N bp B q .6, .26vel(.6+.8e), .03 |
| tom rim | N bp 3000 q 1.4, .8vel, .05 · T sine R→.8R s .012, .5vel, .12 · T sine f0→f1 slide, .45vel, .8D, send .15 |
| crash (bow, edge) | N bp 5500 q .4 → hp 2800, .95vel(.75+.25e), (1.6+.6vel)(.8+.3e), send .25 · M ×1.7 bp 6000 hp 3000, .4vel, 1.2, send .2 |
| crash bell | T sine 2400, .6vel, .7 · M ×1.9 bp 5200 hp 2800, .5vel, .6, send .15 · N bp 5500 q .5 → hp 2800, .3vel, .5 |
| ride bow | M ×1.3 bp 7000 hp 4000, .8vel, 1.1, send .15 · T sine 2900, .45vel, .6 · N hp 6000, .3vel, .6 |
| ride bell | T sine 2900, .9vel, .8 · T sine 1850, .4vel, .5 · M ×1.3 bp 4800 hp 2600, .7vel, .7, send .1 · N hp 6000, .12vel, .3 |
| ride edge | N bp 6000 q .5 → hp 3000, .7vel, 1.4, send .2 · M ×1.3 bp 7000 hp 4000, .6vel, 1.2, send .15 · T sine 2900, .25vel, .5 |

Toms {f0, f1, slide, D, B, R, P}: tom1 {210, 140, .12, .62, 2500, 980, .9}, tom2 {165, 105, .14, .78, 2200, 860, .9}, floor {115, 72, .16, 1.0, 1800, 720, 1.0}. `drumHit()` in the reference file is this table as code; it matches the measured renders at 79–131 dB SNR.

- **Choke**: for every hit on that cymbal in the last 5 s (crash, ride, hat; hatOpen and hatClose count as hat), the hit's `dest` gets `cancel(now); set(value,now); setTarget(0.0001, now, 0.012)`. This silences the direct sound and the drum-room send; **the main-reverb sends ring on**. Add noiseVoice(now, lp 1500, peak 0.05, decay 0.03) at 'drum:'+kind, the skin on metal, not × 1.7. Closing the hat pedal calls choke('hat'), then drum('hatClose', 0.7) (`Docs/05-instruments.md` §3).
- Strike mapping (`Docs/05-instruments.md` §3): pos = rr/r. Hat: edge if pos > .75, else top. Crash and ride: bell if pos < .22, edge if pos > .85, else bow. Drums: rim if rr > 0.9r (not on the kick pad). vel = clamp(((vy−v0)/(v1−v0))^0.7, 0.05, 1), with v0/v1 = 0.3/3.0 m/s for a stick and 0.12/1.5 for a fingertip.
- **Drum room IR** = `makeRoomImpulse({seconds .45, preDelay .004, fast .3, slow .45, lp .3, early .8})`, normalized.

## 5. Harp (`StringSynth`); strings are C major from 48 to 84 (22 notes)

### 5.1 OLD sound, the default: Karplus-Strong (karplus)
seconds = clamp(4.5 − 0.08(midi−48), 0.7, 4.5), bright = clamp(0.3 + 0.009(midi−48), 0.3, 0.62), pick 0.32. **Bit-exact** with float[] storage and double arithmetic; cast before adding.
```csharp
double f=Mtof(midi); int n=(int)Math.Floor(sr*seconds); var o=new float[n];
double D=sr/f-0.5; int Ni=(int)Math.Floor(D); double frac=D-Ni; int L=Ni+2;
for(int i=0;i<L;i++) o[i]=(float)(R()*2-1);                                  // excitation
int pp=Math.Max(1,(int)Math.Floor(L*0.32+0.5));                                 // pick comb
for(int i=L-1;i>=pp;i--) o[i]=(float)(o[i]-o[i-pp]*0.9);
double lp=0; for(int i=0;i<L;i++){ lp+=bright*(o[i]-lp); o[i]=(float)lp; }   // one-pole brightness
double g=Math.Pow(0.001,1/(seconds*f));                                         // −60 dB at `seconds`
for(int i=L;i<n;i++){ double a0=(double)o[i-Ni]+o[i-Ni-1], a1=(double)o[i-Ni-1]+o[i-Ni-2];
  o[i]=(float)(g*0.5*((1-frac)*a0+frac*a1)); }                                  // 2-point average + linear frac delay
Normalize(o,0.8,sr);
```
The level is peak-normalized from a random burst, so the strings' loudness spreads over about 7 dB and changes from session to session (old-harp buffer rms 0.024–0.056). That variation is part of the old sound.

**Play** (pluck):
- Fingertip thud: noiseVoice(t, lp 700, peak 0.02+0.05vel, decay 0.012) → 'harp'.
- playBuffer with jitter 1 (±0.16 % rate, ±5 % cutoff, ±6 % level): cutoff 1000+5000vel, Q 0.4 dB, level 0.1+0.6vel → 'harp'.
- The level gain also feeds × 0.34 → main reverb, and × (0.5+0.4vel) → soundbox `makeBodyImpulse([138,205,286,377,515,690], 0.3)`, whose output × 0.24 → 'harp'.
- Buffers render 1.5 s after start.

**Stops:**
- Re-pluck: `Voice.release` (τ 60 ms, stop +0.5 s).
- Hand damp (`StringVoice.damp`): `setTarget(0,tt,0.018)`, stop at tt+0.25, plus a "shh" noiseVoice(bp 2400, q .7, peak 0.004·√max(1,n), decay .07) → 'harp', where n is the number of strings under the hand.

Velocity: `clamp((speed−0.03)/1.2, 0.2, 1)` (`Docs/05-instruments.md` §5.2).

### 5.2 NEW sound, brief (`useNew(true)`; render, pluckNew)
- Two loops, weights 0.86 and 0.16. The second is 0.8 ¢ sharp, uses seconds × 0.6 and ρ+0.02. Each loop has seeded excitation `rng(midi*7919+1)` / `rng(midi*104729+3)` with its mean removed, a fractional delay, a one-zero lowpass ρ, and an allpass `disp` (dispersion). The loop delay is compensated by the phase delays.
- Material weights wound/gut/nylon: ρ = .5gut+.47wound+.43nylon; disp = −(.72w+.36g+.06n); ring = base × (1.08w+.92g+1.12n).
- Body: three parallel constant-peak bandpasses (112/5/.2, 238/8/.12, 430/9/.08; f/Q/gain).
- Levelled to an RMS of 0.168 over the first 150 ms, measured through the pluck comb at 0.35 with depth .82; tilt −0.07 dB per semitone; peak capped at 0.97.
- Sympathetic hums from the octave below, the octave above and the fifth; 50 ms fade.
- Play: b = clamp(min(u, 1−u), .06, .5), where u is the pluck position from the soundboard (default .35). A comb (delay b/f, × −0.82), lowpass clamp((1300+5200vel)(1.3−.8b)(1+.15nylon), 400, 16000) at Q .4, and gain (0.1+0.6vel)·combGain(.35)/combGain(b)·(1+.01 sin(12.9898·midi+78.233·vel)). Sends: × .3 → reverb; × (.45+.35vel) → boxNew [112,176,248,335,452,610,820]/.42 → × .26.
- Thud: lp 650, peak .018+.045vel, decay .011; nail tick when vel > .35 (hp 3200, lp 9000, peak (.003+.012 nylon)·vel², decay .0025).
- The recipe is plain arithmetic and translates directly. Fingerprints are frozen in the harp audio tests.

## 6. Impulse responses

```csharp
// makeBodyImpulse(modes, seconds) — phases first (modes.Length draws), then
d[i]=(float)Σk Math.Sin(2π·modes[k]·t+φk)·Math.Exp(-t/(seconds*(0.35-0.03k)))/(1+0.5k), t=i/sr, len=⌊sr·seconds⌋; then d/=peak*6.
// makeRoomImpulse({seconds,preDelay,fast,slow,lp,early}) — stereo; per channel ch=0,1 (draw order: tail then taps):
pd=⌊sr·preDelay⌋; low=0; for i=pd..len-1: t=(i-pd)/sr; x=R()*2-1; low+=lp*(x-low);
  d[i]=(float)(Math.Min(1,t/0.006)*(0.55*x*Math.Exp(-t/(seconds*fast))+1.5*low*Math.Exp(-t/(seconds*slow))));
taps={.009,.014,.019,.027,.034,.045,.058}, gains={.7,.55,.45,.38,.3,.22,.15}:
  at=pd+⌊sr·(taps[k]+(ch==1 ? 0.0011*(k%2==1?1:-1) : 0))⌋; for j<3 && at+j<len: d[at+j]+=early*gains[k]*(1-0.3j)*(R()<0.5?-1:1);
```
| IR | params | used by → return gain |
|---|---|---|
| main room | seconds 2.0, preDelay .014, fast .32, slow .62, lp .11, early 1 | every send → 0.26 → master |
| drum room | .45, .004, .3, .45, .3, .8 | per-hit room send → 0.3 → 'drum:room' |
| piano board | modes [95…560], 0.28 s | resonance → 0.22 → 'piano-mid' |
| harp box old/new | [138…690]/0.3; [112…820]/0.42 | resonance → 0.24 / 0.26 → 'harp' |

Apply the convolver scale of §1.4; typical values are main ≈ 0.00569 and drum ≈ 0.00433. In C#, use uniformly partitioned FFT convolution (Burst), 96000 taps × 2 for the main room. The body IRs are sums of decaying sinusoids, so they can be done exactly as one 2-pole resonator per mode: h = A·rⁿ·sin(ωn+φ) ↔ (sin φ + r·sin(ω−φ)z⁻¹)/(1−2r cos ω z⁻¹+r²z⁻²). Subtract the same resonator, delayed by len with amplitude A·r^len and phase φ+ω·len, to reproduce the truncation. The IR noise and phases change per session in the app; the effective rms does not (§1.4).

## 7. Ambience

Toggles FIRE, WIND and SNOW are all off by default. The beds are built once at audio start; their LFOs are sines starting at phase 0 and keep running while silent. On/off ramps each bed gain with `cancel(now); setTarget(on?1:0, now, 0.4)`. An LFO wired into a gain or frequency adds to the base value: v(t) = base + depth·sin(2πft).
- **Fire** → 'fire': noise loop → lowpass 420 (Q 1 dB) → gain 0.09 + 0.03·sin(2π·0.17t). **Pops**: a repeating 110 ms timer. While the fire is on, each tick with R() ≤ 0.65 schedules noiseVoice(now + 0.2R(), bp 1600+3500R(), q 2.5, peak 0.05+0.1R(), decay 0.012+0.03R()) into the fire gain. The draw order is test, time, bp, peak, decay. Implement it as a sample counter (5280 frames at 48 k).
- **Wind** → 'wind': rumble = noise → lowpass 260 (Q 1 dB) → gain 0.1 + 0.06·sin(2π·0.07t); whistle = noise → bandpass (650 + 320·sin(2π·0.11t), Q 6) → gain 0.1 + 0.07·sin(2π·0.05t).
- **Snow** → 'snow': hush = noise → lowpass 520 (Q 0.3 dB) → gain 0.07 + 0.025·sin(2π·0.05t).
- **Snow gust**. It is triggered by the snow weather's gust event with the visual gust's own shape (`Docs/07-world-visuals.md`: strength .45–1, attack 1.1–1.9, hold .4–1.6, release 2.2–3.6 s, next gust after the duration plus 1.5–6 s, only while snow level ≥ .15), and plays only if the snow sound is on. dur = a+h+r, f0 = 360+220R(), peak = .025+.05·strength. One noise loop feeds two paths:
  - bandpass Q .7 with frequency linear f0 → f0(1.25+.35s) over a, held for h, linear back to f0 over r; then gain linear .0001 → peak over a, held, → .0001 over r;
  - highpass 6000 (Q 1 dB) → gain with the same shape at peak × 0.12.
  The source stops at t+dur+0.1. Updating the moving bandpass every 32 frames is enough.
- Mixed reality: entering it switches FIRE and WIND off and keeps SNOW. Setting the room mode only stores the mode, so the reverb is unchanged in AR.

## 8. Sunny

- **Chirps** → 'guide', square + lowpass 5000 (Q 1 dB):
  - open, t = now+0.01: T 880→1320 s .04, peak .022, decay .06; then at t+.065: T 1320→700 s .05, peak .018, decay .07.
  - end, t = now+0.02 (only when a line ended naturally): T 1100→560 s .05, .018, .06; then at t+.065: T 700→420 s .05, .015, .07.
- **Robot voice** (ROBOT settings and chain), at 48 k:
```
s[n] = 0.92·x[n] + 0.08·x[n]·sin(2π·80·n/sr)          // ring modulator: a gain with base 0 driven by an 80 Hz sine
d[n] = s[n−240] + 0.3·d[n−240]                         // 5 ms delay inside its feedback loop
y    = Peaking(2500 Hz, Q .8, +2 dB)( HighPass(120 Hz, Q .7 dB)( s + 0.12·d ) ) · 1.0  → 'guide'
```
  - Lines: eSpeak NG renders (voice en-us+Annie, speed 150, pitch 55, range 70, amplitude 120; silence-trimmed, peak 0.89, 5 ms edge fades), shipped with the app. Another step makes them and they are not checked here.
  - Playback: rate 1.0, per-line gain 1. Start = max(now, asked+0.14 s). A line not ready 1.2 s after it was asked is skipped. A cut is `setTarget(0, now, 0.004)` with stop at +0.03.
  - Mouth: RMS of the last 1024 output samples.
  - The chain is linear in x, so a faithful and cheap implementation is to pre-render each line through it (ring phase 0) and play the result on the spatialized 'guide' source.

## 9. Page-turn swish
At the start of an instrument page-turn (TURN 0.35 s), to the **master** (non-spatial):
- noiseVoice(t, bp 1400 q .7, lp 7000, peak .09, decay .32, send .25);
- noiseVoice(t+.04, hp 2500, lp 9000, peak .035, decay .22, send .2).

## 10. Spatial sources
| name | plays | source-frame position → Unity (x,y,−z) |
|---|---|---|
| piano-lo / piano-mid / piano-hi | notes by register, knock, damper thud; board return → piano-mid | piano local (−0.42 / 0 / +0.42, 0.05, −0.05) → Unity piano local (same x, 0.05, +0.05) |
| harp | strings, thud, shh, soundbox | harp local (−0.33, 0.8, 0) |
| drum:snare, tom1, tom2, floor, kick, hat, crash, ride | each piece (kick = the shell, not the hand pad); snare also gets the wire rattle | each piece group's world position |
| drum:room | drum-room return | **never positioned → world origin (0,0,0)**, about 1.2 m under the seated head (camera starts at (0,1.2,0.1)) |
| fire / wind / snow | ambience | (0.3,0.5,3.85) / (3.1,1.9,1.18) / (3.1,1.55,1.18) → z negated |
| guide | chirps, voice | Sunny's position every frame |
| (master) | swish; main-reverb return | non-spatial stereo |

## 11. Per-voice lifetime summary
| voice | ends |
|---|---|
| piano note | buffer end, or Voice.release τ 60 ms (+0.5 s hard stop) |
| harp note | buffer end; re-pluck (τ 60 ms); damp (τ 18 ms, stop +0.25 s) |
| drum layer | env to 0.0005 at decay, stop at +0.05 s; choke τ 12 ms to 1e-4 |
| one-shots | noise/tone voices stop at decay + 0.05 s |

## 12. Verification, and acceptance tests for the C# implementation
Reference (`Docs/06-audio-dsp.reference.mjs`) vs the measured graph renders of the same voices, with the same seeds and noise:

| item | SNR dB | item | SNR dB |
|---|---|---|---|
| harp old buffers 48/67/84 | **bit-exact** | piano buffers 21/60/108 | 104.6 / 86.2 / 78.4 |
| sine and triangle oscillators | 119–121 | square oscillators | 94–96 |
| kick / snare head / tom1 | 106 / 131 / 123 | hats, cymbals, rims | 79–96 |
| snare-wire rattle | 136 | chirp / swish | 114 / 133 |
| board IR × convolver scale | 130 | compressor (separate tests) | exact |

Tests to write in Unity (EditMode, Test Framework 1.6):
1. `Mulberry32(1)` gives the first five frozen values (§0).
2. The noise buffer from seed 1 matches `noiseBuffer_seed1`.
3. KS-old buffers for all 22 strings with seeds 2000+midi equal `harpOldBuffer` **exactly**.
4. Piano buffers with seeds 1000+midi match `pianoBuffer` to 1e-4.
5. Each IR and its scale match `impulseResponses`.
6. A dry drum hit from noise seed 1 nulls against `Tests/EditMode/Audio/Reference~/drum_*.wav` (residual < −60 dB).
7. The compressor's impulse, step and static figures match the measured compressor figures (§1.5).
8. Panner gain at 1, 2 and 5 m is 0.5769, 0.2804 and 0.1103.

The WAVs are dry: direct sound plus board/box plus wires, with no reverb, drum room, panner or master. They used seed 1 noise, seed 2 board, seed 3 boxes, seed 6 drum room, 1000+midi and 2000+midi for the note renders, and jitter forced to 0. Piano notes release at frame 120064 (2.50133 s, where a release at 2.5 s lands on the 128-frame render quantum); A0 releases at frame 144000 and C7 at frame 67200. Voice.release and the damper thud start at that frame.

## 13. UNVERIFIED and open points
1. The device sample rate (assumed 48 kHz). The 32-frame divisions and the per-sample one-poles change character at other rates.
2. The Meta HRTF's level against the measured HRTF's (front −5.0 dB per ear): calibrate by ear or meter.
3. 'drum:room' sits at the world origin, probably by accident. This implementation keeps it; whether to move it to the kit centre is the user's call.
4. The measured HRTF panner fed by the stereo drum-room return (whether it is downmixed or each ear gets its own channel) is not verified. Unity's handling of a stereo clip on a spatialized source is also not verified.
5. Setting the room mode to AR does not change the sound, although the design says it should.
6. Hard hats and cymbals can still exceed 0 dBFS after the master (dry peaks reach 3.0 before distance, HRTF and compressor gains). Unity clips at the device.
7. The fire-pop timer is a wall-clock 110 ms interval, which jitters; a sample counter is acceptable.
