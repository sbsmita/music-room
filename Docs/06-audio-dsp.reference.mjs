// 06-audio-dsp.reference.mjs: pure-JS reference DSP for the Unity C# implementation (Docs/06-audio-dsp.md).
// Every function is the reference recipe of one synth, filter, envelope or bus the app uses; each
// was checked against the reference renders in Tests/EditMode/Audio/Reference~ (see the spec).
export const mtof = (m) => 440 * Math.pow(2, (m - 69) / 12);
export const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const f32 = Math.fround;

// The seeded generator used for reproducible tests (the strings synth's rng()).
export function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// ---------------- Biquad: RBJ cookbook coefficients, Direct Form I ----------------
// Q is in dB for lowpass/highpass (alpha = sin w0 / (2 * 10^(Q/20))), linear for bandpass/peaking;
// shelves use S = 1. Parameter values are float32, so f/Q/G are rounded first.
export function biquadCoeffs(type, f, Q, G, sr) {
  f = f32(f); Q = f32(Q); G = f32(G);
  const w0 = (2 * Math.PI * f) / sr, cw = Math.cos(w0), sw = Math.sin(w0);
  const A = Math.pow(10, G / 40);
  let b0, b1, b2, a0, a1, a2;
  if (f >= sr / 2 && (type === 'lowpass')) return [1, 0, 0, 0, 0];
  switch (type) {
    case 'lowpass': { const al = sw / (2 * Math.pow(10, Q / 20)); b0 = (1 - cw) / 2; b1 = 1 - cw; b2 = (1 - cw) / 2; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; break; }
    case 'highpass': { const al = sw / (2 * Math.pow(10, Q / 20)); b0 = (1 + cw) / 2; b1 = -(1 + cw); b2 = (1 + cw) / 2; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; break; }
    case 'bandpass': { const al = sw / (2 * Q); b0 = al; b1 = 0; b2 = -al; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; break; }
    case 'peaking': { const al = sw / (2 * Q); b0 = 1 + al * A; b1 = -2 * cw; b2 = 1 - al * A; a0 = 1 + al / A; a1 = -2 * cw; a2 = 1 - al / A; break; }
    case 'highshelf': { const al = (sw / 2) * Math.SQRT2; const s = 2 * Math.sqrt(A) * al; b0 = A * (A + 1 + (A - 1) * cw + s); b1 = -2 * A * (A - 1 + (A + 1) * cw); b2 = A * (A + 1 + (A - 1) * cw - s); a0 = A + 1 - (A - 1) * cw + s; a1 = 2 * (A - 1 - (A + 1) * cw); a2 = A + 1 - (A - 1) * cw - s; break; }
    default: throw new Error(type);
  }
  return [b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0];
}
export class Biquad {
  constructor(type, f, Q = 1, G = 0, sr = 48000) { this.c = biquadCoeffs(type, f, Q, G, sr); this.x1 = this.x2 = this.y1 = this.y2 = 0; }
  set(type, f, Q, G, sr) { this.c = biquadCoeffs(type, f, Q, G, sr); }
  tick(x) {
    const [b0, b1, b2, a1, a2] = this.c;
    const y = f32(b0 * x + b1 * this.x1 + b2 * this.x2 - a1 * this.y1 - a2 * this.y2);
    this.x2 = this.x1; this.x1 = x; this.y2 = this.y1; this.y1 = y;
    return y;
  }
}

// ---------------- Oscillators: band-limited periodic-wave tables ----------------
// 48 kHz: table size 4096, 2048 max partials, 36 ranges of 400 cents; range r keeps partials
// 1..floor(2048 * 2^(-r/3)) (capped at 2047); every table is scaled by 1/max|range-0 table|.
const N = 4096, HALF = 2048, RANGES = 36;
const SIN = new Float64Array(N);
for (let j = 0; j < N; j++) SIN[j] = Math.sin((2 * Math.PI * j) / N);
const tableCache = new Map();
export const coefOf = {
  sine: (n) => (n === 1 ? 1 : 0),
  square: (n) => (n & 1 ? 4 / (n * Math.PI) : 0),
  triangle: (n) => (n & 1 ? (8 / (Math.PI * Math.PI * n * n)) * (((n - 1) >> 1) & 1 ? -1 : 1) : 0),
};
export function partialsForRange(r) { return Math.min(HALF - 1, Math.floor(f32(Math.pow(2, -(r * 400) / 1200)) * HALF)); }
export function tables(type) {
  if (tableCache.has(type)) return tableCache.get(type);
  const b = coefOf[type];
  const out = [];
  let norm = 1;
  for (let r = 0; r < RANGES; r++) {
    const P = partialsForRange(r);
    const t = new Float64Array(N);
    for (let n = 1; n <= P; n++) { const bn = b(n); if (!bn) continue; for (let j = 0; j < N; j++) t[j] += bn * SIN[(n * j) % N]; }
    if (r === 0) { let m = 0; for (let j = 0; j < N; j++) m = Math.max(m, Math.abs(t[j])); norm = m ? 1 / m : 1; }
    const tf = new Float32Array(N);
    for (let j = 0; j < N; j++) tf[j] = t[j] * norm;
    out.push(tf);
  }
  tableCache.set(type, out);
  return out;
}
// One oscillator from frame 0: freqAt(k) gives Hz at frame k. Returns Float32Array(n).
export function oscillator(type, freqAt, n, sr = 48000) {
  const T = tables(type);
  const out = new Float32Array(n);
  const lowest = sr / 2 / HALF;
  let phase = 0;
  for (let k = 0; k < n; k++) {
    const f = freqAt(k);
    const ratio = f > 0 ? f / lowest : 0.5;
    let pr = 1 + (Math.log2(ratio) * 1200) / 400;
    pr = Math.min(Math.max(pr, 0), RANGES - 1);
    const r1 = Math.floor(pr), r2 = r1 < RANGES - 1 ? r1 + 1 : r1;
    const tf = pr - r1;
    const hi = T[r1], lo = T[r2];
    const i0 = Math.floor(phase), i1 = (i0 + 1) & (N - 1), fr = phase - i0;
    const sh = (1 - fr) * hi[i0] + fr * hi[i1];
    const sl = (1 - fr) * lo[i0] + fr * lo[i1];
    out[k] = (1 - tf) * sh + tf * sl;
    phase += (f * N) / sr;
    phase -= Math.floor(phase / N) * N;
  }
  return out;
}

// ---------------- Envelopes (parameter-automation closed forms) ----------------
// engine.env(): setValueAtTime(max(peak,5e-4), t0); exponentialRampToValueAtTime(5e-4, t0+decay)
export function envAt(t, peak, decay) {
  const P = f32(Math.max(peak, 0.0005));
  if (t < 0) return 1;
  if (t >= decay) return f32(0.0005);
  return P * Math.pow(f32(0.0005) / P, t / decay);
}

// ---------------- The engine's generic voices (start at frame 0, mono) ----------------
// noise: the engine's 2 s white-noise buffer; every noise voice reads it from index 0, looping.
export function noiseVoice(n, sr, noise, { hp = 0, lp = 20000, bp = 0, q = 1, peak, decay }) {
  const out = new Float32Array(n);
  const stop = Math.ceil((decay + 0.05) * sr); // the source stops; filters ring on
  const fb = bp ? new Biquad('bandpass', bp, q, 0, sr) : null;
  const fh = hp ? new Biquad('highpass', hp, 1, 0, sr) : null;
  const fl = lp < 20000 ? new Biquad('lowpass', lp, 1, 0, sr) : null;
  for (let k = 0; k < n; k++) {
    let x = k < stop ? noise[k % noise.length] : 0;
    if (fb) x = fb.tick(x);
    if (fh) x = fh.tick(x);
    if (fl) x = fl.tick(x);
    out[k] = x * envAt(k / sr, peak, decay);
  }
  return out;
}
export function toneVoice(n, sr, { type = 'sine', f0, f1 = f0, slide = 0.05, peak, decay, lp = 20000 }) {
  const F0 = f32(f0), F1 = f32(f1);
  const freqAt = f1 !== f0 ? (k) => { const t = k / sr; return t >= slide ? F1 : F0 * Math.pow(F1 / F0, t / slide); } : () => F0;
  const stop = Math.ceil((decay + 0.05) * sr);
  const osc = oscillator(type, freqAt, Math.min(n, stop), sr);
  const fl = lp < 20000 ? new Biquad('lowpass', lp, 1, 0, sr) : null;
  const out = new Float32Array(n);
  for (let k = 0; k < n; k++) {
    let x = k < osc.length ? osc[k] : 0;
    if (fl) x = fl.tick(x);
    out[k] = x * envAt(k / sr, peak, decay);
  }
  return out;
}
export const METAL = [263, 400, 421, 474, 587, 845];
export function metalVoice(n, sr, { ratio = 1, bp = 9500, hp = 6500, peak, decay }) {
  const stop = Math.ceil((decay + 0.05) * sr);
  const m = Math.min(n, stop);
  const sum = new Float32Array(m);
  for (const f of METAL) { const F = f32(f * ratio); const o = oscillator('square', () => F, m, sr); for (let k = 0; k < m; k++) sum[k] += o[k]; }
  const fb = new Biquad('bandpass', bp, 0.9, 0, sr), fh = new Biquad('highpass', hp, 1, 0, sr);
  const out = new Float32Array(n);
  for (let k = 0; k < n; k++) out[k] = fh.tick(fb.tick(k < m ? sum[k] : 0)) * envAt(k / sr, peak, decay);
  return out;
}

// ---------------- Drum hit (DrumSynth.drum + voices), dry: what reaches the piece's source ----------------
// Returns { direct, room, wires } (room = signal into the drum-room convolver input, before it).
export function drumHit(kind, vel, strike, n, sr, noise, level = 1.7) {
  vel = clamp(vel, 0.05, 1);
  const v2 = vel * vel;
  const pos = clamp(strike.pos ?? 0.3, 0, 1), zone = strike.zone || 'head';
  const dest = new Float32Array(n);
  const add = (buf) => { for (let k = 0; k < n; k++) dest[k] += buf[k]; };
  const tv = (o) => add(toneVoice(n, sr, o));
  const nv = (o) => add(noiseVoice(n, sr, noise, o));
  const mv = (o) => add(metalVoice(n, sr, { ...o, bp: (o.bp || 9500) * (0.85 + 0.3 * vel) }));
  const edge = pos, centre = 1 - pos;
  switch (kind) {
    case 'kick': tv({ f0: 165, f1: 46, slide: 0.09, peak: 1.0 * vel, decay: 0.42 + 0.1 * vel, lp: 2500 }); nv({ bp: 3200, q: 0.7, peak: 0.34 * v2, decay: 0.02 }); break;
    case 'snare':
      if (zone === 'rim') { nv({ bp: 3400, q: 1.4, peak: 0.9 * vel, decay: 0.05 }); tv({ f0: 1180, f1: 950, slide: 0.012, peak: 0.55 * vel, decay: 0.11 }); tv({ type: 'triangle', f0: 195, f1: 150, slide: 0.05, peak: 0.3 * vel, decay: 0.16 }); nv({ hp: 1400, lp: 9000, peak: 0.4 * Math.pow(vel, 1.3), decay: 0.18 }); break; }
      tv({ type: 'triangle', f0: 195, f1: 150, slide: 0.05, peak: 0.5 * vel * (0.7 + 0.3 * centre), decay: 0.18 + 0.06 * edge });
      tv({ f0: 330, f1: 280, slide: 0.04, peak: 0.3 * vel * (0.5 + 0.9 * edge), decay: 0.1 + 0.1 * edge });
      nv({ hp: 1400, lp: 9000, peak: 0.66 * Math.pow(vel, 1.3) * (1 - 0.35 * edge), decay: 0.2 + 0.08 * vel + 0.06 * edge }); break;
    case 'hat': mv({ bp: 8500, hp: 5000, peak: 1.3 * vel, decay: 0.08 + 0.03 * vel + 0.05 * edge }); nv({ hp: 7000, peak: 0.75 * vel * (1 + 0.3 * edge), decay: 0.07 + 0.05 * edge }); break;
    case 'hatOpen': mv({ bp: 8500, hp: 5000, peak: 1.05 * vel, decay: 0.42 + 0.15 * edge }); nv({ hp: 7000, peak: 0.6 * vel, decay: 0.35 + 0.15 * edge }); break;
    case 'hatClose': mv({ bp: 7000, hp: 4500, peak: 0.6 * vel, decay: 0.05 }); nv({ hp: 6000, peak: 0.45 * vel, decay: 0.035 }); break;
    case 'tom1': case 'tom2': case 'floor': {
      const T = kind === 'tom1' ? { f0: 210, f1: 140, slide: 0.12, decay: 0.62, bp: 2500, rim: 980, peak: 0.9 } : kind === 'tom2' ? { f0: 165, f1: 105, slide: 0.14, decay: 0.78, bp: 2200, rim: 860, peak: 0.9 } : { f0: 115, f1: 72, slide: 0.16, decay: 1.0, bp: 1800, rim: 720, peak: 1.0 };
      if (zone === 'rim') { nv({ bp: 3000, q: 1.4, peak: 0.8 * vel, decay: 0.05 }); tv({ f0: T.rim, f1: T.rim * 0.8, slide: 0.012, peak: 0.5 * vel, decay: 0.12 }); tv({ f0: T.f0, f1: T.f1, slide: T.slide, peak: 0.45 * vel, decay: T.decay * 0.8 }); break; }
      tv({ f0: T.f0, f1: T.f1, slide: T.slide, peak: T.peak * vel * (0.75 + 0.25 * centre), decay: T.decay * (1 + 0.3 * edge) });
      nv({ bp: T.bp, q: 0.6, peak: 0.26 * vel * (0.6 + 0.8 * edge), decay: 0.03 }); break;
    }
    case 'crash':
      if (zone === 'bell') { tv({ f0: 2400, peak: 0.6 * vel, decay: 0.7 }); mv({ ratio: 1.9, bp: 5200, hp: 2800, peak: 0.5 * vel, decay: 0.6 }); nv({ bp: 5500, q: 0.5, hp: 2800, peak: 0.3 * vel, decay: 0.5 }); break; }
      nv({ bp: 5500, q: 0.4, hp: 2800, peak: 0.95 * vel * (0.75 + 0.25 * edge), decay: (1.6 + 0.6 * vel) * (0.8 + 0.3 * edge) });
      mv({ ratio: 1.7, bp: 6000, hp: 3000, peak: 0.4 * vel, decay: 1.2 }); break;
    case 'ride':
      if (zone === 'bell') { tv({ f0: 2900, peak: 0.9 * vel, decay: 0.8 }); tv({ f0: 1850, peak: 0.4 * vel, decay: 0.5 }); mv({ ratio: 1.3, bp: 4800, hp: 2600, peak: 0.7 * vel, decay: 0.7 }); nv({ hp: 6000, peak: 0.12 * vel, decay: 0.3 }); break; }
      if (zone === 'edge') { nv({ bp: 6000, q: 0.5, hp: 3000, peak: 0.7 * vel, decay: 1.4 }); mv({ ratio: 1.3, bp: 7000, hp: 4000, peak: 0.6 * vel, decay: 1.2 }); tv({ f0: 2900, peak: 0.25 * vel, decay: 0.5 }); break; }
      mv({ ratio: 1.3, bp: 7000, hp: 4000, peak: 0.8 * vel, decay: 1.1 }); tv({ f0: 2900, peak: 0.45 * vel, decay: 0.6 }); nv({ hp: 6000, peak: 0.3 * vel, decay: 0.6 }); break;
  }
  const direct = new Float32Array(n);
  for (let k = 0; k < n; k++) direct[k] = dest[k] * level;
  let wires = null;
  if (kind === 'kick' || kind.startsWith('tom') || kind === 'floor') {
    // engine.noiseVoice(t + 0.004, ...) straight to 'drum:snare' (no level, no room send)
    const d0 = Math.round(0.004 * sr);
    const w = noiseVoice(n - d0, sr, noise, { hp: 2800, lp: 9000, peak: 0.07 * vel, decay: 0.08 });
    wires = new Float32Array(n);
    for (let k = 0; k < n - d0; k++) wires[k + d0] = w[k];
  }
  return { direct, wires };
}

// ---------------- Piano: per-key offline render (PianoSynth.render) ----------------
export function normalize(d, peak, sr) {
  let m = 0;
  for (let i = 0; i < d.length; i++) m = Math.max(m, Math.abs(d[i]));
  if (m > 0) for (let i = 0; i < d.length; i++) d[i] *= peak / m;
  const fade = Math.min(d.length, Math.floor(sr * 0.05));
  for (let i = 0; i < fade; i++) d[d.length - 1 - i] *= i / fade;
}
export function renderPianoKey(midi, sr, rand) {
  const f = mtof(midi), u = (midi - 21) / 87;
  const seconds = clamp(7 - u * 5.6, 1.1, 7);
  const n = Math.floor(sr * seconds);
  const acc = new Float64Array(n);
  const B = 0.00008 + 0.0007 * u * u;
  const nyq = Math.min(sr * 0.45, 15000);
  const detune = 1.2 + 2.5 * u;
  for (let k = 1; k <= 12; k++) {
    const fk = f * k * Math.sqrt(1 + B * k * k);
    if (fk > nyq) break;
    const amp = f32(Math.pow(k, -(0.9 + 0.75 * u)) * (k === 1 ? 1 : 0.9));
    const tau = seconds / 2.9 / (1 + 0.5 * (k - 1));
    const tEnd = Math.min(seconds, 0.12 + tau * 5.5);
    const a2 = f32(amp * 0.45), a3 = f32(Math.max(amp * 0.001, 1e-5)), a0 = f32(0.0001);
    for (const cents of [-detune / 2, detune / 2]) {
      const fr = f32(fk) * Math.pow(2, f32(cents) / 1200);
      const osc = oscillator('sine', () => fr, n, sr);
      for (let i = 0; i < n; i++) {
        const t = i / sr;
        const g = t < 0.004 ? a0 * Math.pow(amp / a0, t / 0.004) : t < 0.12 ? amp * Math.pow(a2 / amp, (t - 0.004) / 0.116) : t < tEnd ? a2 * Math.pow(a3 / a2, (t - 0.12) / (tEnd - 0.12)) : a3;
        acc[i] += osc[i] * g;
      }
    }
  }
  // Hammer thump: 30 ms of fresh white noise -> bandpass -> exponential decay.
  const nn = Math.floor(sr * 0.03);
  const nz = new Float32Array(nn);
  for (let i = 0; i < nn; i++) nz[i] = rand() * 2 - 1;
  const bpf = new Biquad('bandpass', clamp(f * 2.5, 300, 4000), 0.8, 0, sr);
  const g0 = f32(0.5 - 0.3 * u);
  for (let i = 0; i < n; i++) {
    const x = bpf.tick(i < nn ? nz[i] : 0);
    const t = i / sr;
    const g = t < 0.025 ? g0 * Math.pow(f32(0.001) / g0, t / 0.025) : f32(0.001);
    acc[i] += x * g;
  }
  const d = new Float32Array(n);
  for (let i = 0; i < n; i++) d[i] = acc[i] * 0.5; // the render's out gain (0.5); normalize removes it
  normalize(d, 0.9, sr);
  return d;
}

// ---------------- Harp, old sound: Karplus-Strong (StringSynth.karplus), verbatim ----------------
export function karplusOld(midi, sr, rand) {
  const f = mtof(midi);
  const seconds = clamp(4.5 - (midi - 48) * 0.08, 0.7, 4.5);
  const bright = clamp(0.3 + (midi - 48) * 0.009, 0.3, 0.62);
  const pick = 0.32;
  const n = Math.floor(sr * seconds);
  const out = new Float32Array(n);
  const D = sr / f - 0.5, Ni = Math.floor(D), frac = D - Ni, L = Ni + 2;
  for (let i = 0; i < L; i++) out[i] = rand() * 2 - 1;
  const pp = Math.max(1, Math.round(L * pick));
  for (let i = L - 1; i >= pp; i--) out[i] -= out[i - pp] * 0.9;
  let lp = 0;
  for (let i = 0; i < L; i++) { lp += bright * (out[i] - lp); out[i] = lp; }
  const g = Math.pow(0.001, 1 / (seconds * f));
  for (let i = L; i < n; i++) {
    const a0 = out[i - Ni] + out[i - Ni - 1];
    const a1 = out[i - Ni - 1] + out[i - Ni - 2];
    out[i] = g * 0.5 * ((1 - frac) * a0 + frac * a1);
  }
  normalize(out, 0.8, sr);
  return out;
}

// ---------------- Impulse responses and the convolver's normalization ----------------
export function makeRoomImpulse(sr, { seconds, preDelay, fast, slow, lp, early }, rand) {
  const len = Math.floor(sr * seconds);
  const chs = [new Float32Array(len), new Float32Array(len)];
  const taps = [0.009, 0.014, 0.019, 0.027, 0.034, 0.045, 0.058];
  const gains = [0.7, 0.55, 0.45, 0.38, 0.3, 0.22, 0.15];
  for (let ch = 0; ch < 2; ch++) {
    const d = chs[ch];
    const pd = Math.floor(sr * preDelay);
    let low = 0;
    for (let i = pd; i < len; i++) {
      const t = (i - pd) / sr;
      const n = rand() * 2 - 1;
      low += lp * (n - low);
      const fadeIn = Math.min(1, t / 0.006);
      d[i] = fadeIn * (0.55 * n * Math.exp(-t / (seconds * fast)) + 1.5 * low * Math.exp(-t / (seconds * slow)));
    }
    for (let k = 0; k < taps.length; k++) {
      const at = pd + Math.floor(sr * (taps[k] + (ch ? 0.0011 * (k % 2 ? 1 : -1) : 0)));
      for (let j = 0; j < 3 && at + j < len; j++) d[at + j] += early * gains[k] * (1 - j * 0.3) * (rand() < 0.5 ? -1 : 1);
    }
  }
  return chs;
}
export function makeBodyImpulse(sr, modes, seconds, rand) {
  const len = Math.floor(sr * seconds);
  const d = new Float32Array(len);
  const phases = modes.map(() => rand() * Math.PI * 2);
  for (let i = 0; i < len; i++) {
    const t = i / sr;
    let v = 0;
    for (let k = 0; k < modes.length; k++) v += (Math.sin(2 * Math.PI * modes[k] * t + phases[k]) * Math.exp(-t / (seconds * (0.35 - k * 0.03)))) / (1 + k * 0.5);
    d[i] = v;
  }
  let peak = 0;
  for (let i = 0; i < len; i++) peak = Math.max(peak, Math.abs(d[i]));
  for (let i = 0; i < len; i++) d[i] /= peak * 6;
  return [d];
}
// Convolver normalisation (on by default): the IR is scaled by this before use.
export function convolverScale(chs, sr) {
  let p = 0, len = chs[0].length;
  for (const d of chs) for (let i = 0; i < len; i++) p += d[i] * d[i];
  let power = Math.sqrt(p / (chs.length * len));
  if (!isFinite(power) || power < 0.000125) power = 0.000125;
  let scale = (1 / power) * Math.pow(10, -58 / 20) * (44100 / sr);
  if (chs.length === 4) scale *= 0.5;
  return scale;
}

// ---------------- The master chain's dynamics compressor kernel, behaviourally verified ----------------
// Reproduces the measured reference output exactly (to 0.01 dB) on: a 1e-3 impulse 1000 frames after start
// (gain 0.45801 at frame 1288: the 6 ms pre-delay and the start-up transient), a -40/0/-40 dB
// 1 kHz step (attack and adaptive release), and the static curve from -60 to +12 dBFS.
// chs: arrays of channel samples (linked detector = max |x| over channels). State persists.
export function makeCompressor({ sr = 48000, threshold = -14, knee = 18, ratio = 4, attack = 0.003, release = 0.18, preDelay = 0.006, channels = 2 } = {}) {
  const dB2lin = (d) => Math.pow(10, d / 20), lin2dB = (x) => 20 * Math.log10(x);
  const linThr = dB2lin(threshold);
  const kneeCurve = (x, k) => (x < linThr ? x : linThr + (1 - Math.exp(-k * (x - linThr))) / k);
  const slopeAt = (x, k) => { if (x < linThr) return 1; const x2 = x * 1.001; return (lin2dB(kneeCurve(x2, k)) - lin2dB(kneeCurve(x, k))) / (lin2dB(x2) - lin2dB(x)); };
  const kAtSlope = (desired) => { const x = dB2lin(threshold + knee); let minK = 0.1, maxK = 10000, k = 5; for (let i = 0; i < 15; i++) { if (slopeAt(x, k) < desired) maxK = k; else minK = k; k = Math.sqrt(minK * maxK); } return k; };
  const k = kAtSlope(1 / ratio);
  const kneeThrDb = threshold + knee, kneeThr = dB2lin(kneeThrDb), yKneeThrDb = lin2dB(kneeCurve(kneeThr, k));
  const sat = (x) => (x < kneeThr ? kneeCurve(x, k) : dB2lin(yKneeThrDb + (lin2dB(x) - kneeThrDb) / ratio));
  const makeup = Math.pow(1 / sat(1), 0.6); // +2.137 dB for the app's settings
  const attackFrames = Math.max(0.001, attack) * sr, releaseFrames = release * sr, satReleaseFrames = 0.0025 * sr;
  const y1 = releaseFrames * 0.09, y2 = releaseFrames * 0.16, y3 = releaseFrames * 0.42, y4 = releaseFrames * 0.98;
  const kA = 0.9999999999999998 * y1 + 1.8432219684323923e-16 * y2 - 1.9373394351676423e-16 * y3 + 8.824516011816245e-18 * y4;
  const kB = -1.5788320352845888 * y1 + 2.3305837032074286 * y2 - 0.9141194204840429 * y3 + 0.1623677525612032 * y4;
  const kC = 0.5334142869106424 * y1 - 1.272736789213631 * y2 + 0.9258856042207512 * y3 - 0.18656310191776226 * y4;
  const kD = 0.08783463138207234 * y1 - 0.1694162967925622 * y2 + 0.08588057951595272 * y3 - 0.00429891410546283 * y4;
  const kE = -0.042416883008123074 * y1 + 0.1115693827987602 * y2 - 0.09764676325265872 * y3 + 0.028494263462021576 * y4;
  const MASK = 1023;
  const delay = Array.from({ length: channels }, () => new Float32Array(1024));
  let rd = 0, wr = Math.floor(preDelay * sr), det = 0, gain = 1, maxAtt = -1, frame = 0, rate = 1, sdg = 0;
  function process(chs, n) {
    const outs = chs.map(() => new Float32Array(n));
    for (let i = 0; i < n; i++, frame++) {
      if (frame % 32 === 0) { // the envelope rate is recomputed once per 32-frame division
        if (!isFinite(det)) det = 1;
        sdg = Math.asin(det) / (Math.PI / 2);
        let diff = lin2dB(gain / sdg);
        if (sdg > gain) { // releasing: adaptive release, faster the more it was compressing
          maxAtt = -1;
          if (!isFinite(diff)) diff = -1;
          let x = Math.min(0, Math.max(-12, diff)); x = 0.25 * (x + 12);
          rate = dB2lin(5 / (kA + kB * x + kC * x * x + kD * x * x * x + kE * x * x * x * x));
        } else { // attacking
          if (!isFinite(diff)) diff = 1;
          if (maxAtt === -1 || maxAtt < diff) maxAtt = diff;
          rate = 1 - Math.pow(0.25 / Math.max(0.5, maxAtt), 1 / attackFrames);
        }
      }
      let inp = 0;
      for (let c = 0; c < chs.length; c++) { const v = chs[c][i]; delay[c][wr] = v; inp = Math.max(inp, Math.abs(v)); }
      const att = inp <= 0.0001 ? 1 : sat(inp) / inp;
      const satRate = dB2lin(Math.max(2, -lin2dB(att)) / satReleaseFrames) - 1;
      det += (att - det) * (att > det ? satRate : 1);
      det = Math.min(1, det);
      if (!isFinite(det)) det = 1;
      if (rate < 1) gain += (sdg - gain) * rate; else gain = Math.min(1, gain * rate);
      const total = makeup * Math.sin((Math.PI / 2) * gain);
      for (let c = 0; c < chs.length; c++) outs[c][i] = delay[c][rd] * total;
      rd = (rd + 1) & MASK; wr = (wr + 1) & MASK;
    }
    return outs;
  }
  return { process, k, makeup, preDelayFrames: Math.floor(preDelay * sr) };
}
