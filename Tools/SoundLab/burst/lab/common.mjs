// common.mjs: shared helpers for the burst lab (Node 26, no packages).
// float32 helpers, seeded RNGs (bit-identical to the C# port), WAV writer (24-bit PCM),
// ITU-R BS.1770-4 integrated loudness (K-weighting at 48 kHz, 400 ms blocks, 75 % overlap,
// absolute gate -70 LUFS, relative gate -10 LU) and a 4x oversampled true-peak estimate.
import fs from 'node:fs';

export const f32 = Math.fround;
export const SR = 48000;

// ---------------- RNGs ----------------
// splitmix32 (Vigna / Steele et al.): seeds lanes. Pure uint32 arithmetic, identical in C# (uint ops).
export function splitmix32(state) {
  let s = state >>> 0;
  return () => {
    s = (s + 0x9e3779b9) >>> 0;
    let z = s;
    z = Math.imul(z ^ (z >>> 16), 0x85ebca6b) >>> 0;
    z = Math.imul(z ^ (z >>> 13), 0xc2b2ae35) >>> 0;
    return (z ^ (z >>> 16)) >>> 0;
  };
}
// xorshift32 (Marsaglia 2003, 13/17/5), the engine's existing per-voice generator (Dsp/Noise.cs XorShift32).
export function xorshift32Step(x) {
  x ^= x << 13; x >>>= 0;
  x ^= x >>> 17;
  x ^= x << 5; x >>>= 0;
  return x;
}
// int32 -> [-1, 1): (int)x * 2^-31 (exact in float32: the product of an int32 and a power of two
// rounds once, when the int is converted to float32).
export const bipolar = (x) => f32(f32(x | 0) * f32(1 / 2147483648));
// mulberry32 (the app's seeded generator, mirrored by Core/Mulberry32.cs).
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

// ---------------- WAV ----------------
export function writeWav24(path, channels, sr = SR) {
  const n = channels[0].length, nc = channels.length;
  const bytes = n * nc * 3;
  const b = Buffer.alloc(44 + bytes);
  b.write('RIFF', 0); b.writeUInt32LE(36 + bytes, 4); b.write('WAVE', 8);
  b.write('fmt ', 12); b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(nc, 22);
  b.writeUInt32LE(sr, 24); b.writeUInt32LE(sr * nc * 3, 28); b.writeUInt16LE(nc * 3, 32); b.writeUInt16LE(24, 34);
  b.write('data', 36); b.writeUInt32LE(bytes, 40);
  let o = 44;
  for (let i = 0; i < n; i++) for (let c = 0; c < nc; c++) {
    let v = Math.round(channels[c][i] * 8388607);
    if (v > 8388607) v = 8388607; else if (v < -8388608) v = -8388608;
    b.writeIntLE(v, o, 3); o += 3;
  }
  fs.writeFileSync(path, b);
  return 44 + bytes;
}

// ---------------- Loudness (BS.1770-4) ----------------
// K-weighting biquads for 48 kHz from BS.1770-4 Annex 1 (Tables 1 and 2).
const KW = [
  { b: [1.53512485958697, -2.69169618940638, 1.19839281085285], a: [-1.69065929318241, 0.73248077421585] },
  { b: [1.0, -2.0, 1.0], a: [-1.99004745483398, 0.99007225036621] },
];
function kWeight(x) {
  const y = new Float64Array(x.length);
  let s = Float64Array.from(x);
  for (const { b, a } of KW) {
    let x1 = 0, x2 = 0, y1 = 0, y2 = 0;
    for (let i = 0; i < s.length; i++) {
      const v = b[0] * s[i] + b[1] * x1 + b[2] * x2 - a[0] * y1 - a[1] * y2;
      x2 = x1; x1 = s[i]; y2 = y1; y1 = v; y[i] = v;
    }
    s = Float64Array.from(y);
  }
  return s;
}
export function integratedLufs(channels, sr = SR) {
  if (sr !== 48000) throw new Error('K-weighting coefficients are for 48 kHz');
  const zs = channels.map(kWeight);
  const block = Math.round(0.4 * sr), hop = Math.round(0.1 * sr);
  const L = [];
  for (let s = 0; s + block <= zs[0].length; s += hop) {
    let sum = 0;
    for (const z of zs) { let e = 0; for (let i = s; i < s + block; i++) e += z[i] * z[i]; sum += e / block; }
    L.push(sum);
  }
  const lk = (p) => -0.691 + 10 * Math.log10(p);
  const abs = L.filter((p) => lk(p) > -70);
  if (!abs.length) return -Infinity;
  const mean = (a) => a.reduce((x, y) => x + y, 0) / a.length;
  const gate = lk(mean(abs)) - 10;
  const rel = abs.filter((p) => lk(p) > gate);
  return lk(mean(rel));
}
// True-peak estimate: 4x oversampling with a 48-tap windowed-sinc interpolator (BS.1770-4 Annex 2 style).
export function truePeakDb(channels) {
  const taps = 12, os = 4;
  const h = [];
  for (let p = 0; p < os; p++) {
    const row = [];
    for (let k = -taps; k < taps; k++) {
      const t = k + p / os; const x = Math.PI * t;
      const sinc = t === 0 ? 1 : Math.sin(x) / x;
      const w = 0.5 + 0.5 * Math.cos((Math.PI * t) / taps);
      row.push(sinc * w);
    }
    h.push(row);
  }
  let peak = 0;
  for (const c of channels) for (let i = taps; i < c.length - taps; i++) {
    for (let p = 0; p < os; p++) {
      let v = 0; const row = h[p];
      for (let k = 0; k < row.length; k++) v += row[k] * c[i + taps - k]; // row[k] = sinc((k - taps) + p/os)
      const a = Math.abs(v); if (a > peak) peak = a;
    }
  }
  for (const c of channels) for (let i = 0; i < c.length; i++) { const a = Math.abs(c[i]); if (a > peak) peak = a; }
  return 20 * Math.log10(peak || 1e-12);
}
// Loudness-match a set of renders: every render goes to the same target (default -18 LUFS); if any one would
// then have a true peak above -1 dBTP, the common target is lowered for ALL of them by the excess.
export function loudnessMatch(renders, target = -18, ceilingDb = -1) {
  const info = renders.map((chs) => ({ lufs: integratedLufs(chs), tp: truePeakDb(chs) }));
  let common = target;
  for (const i of info) { const tpAt = i.tp + (common - i.lufs); if (tpAt > ceilingDb) common -= tpAt - ceilingDb + 0.05; }
  const out = renders.map((chs, k) => {
    const g = Math.pow(10, (common - info[k].lufs) / 20);
    return chs.map((c) => Float32Array.from(c, (v) => v * g));
  });
  const after = out.map((chs) => ({ lufs: integratedLufs(chs), tp: truePeakDb(chs) }));
  return { out, target: common, before: info, after };
}

export function rmsDb(a, from = 0, to = a.length) { let e = 0; for (let i = from; i < to; i++) e += a[i] * a[i]; return 10 * Math.log10(e / Math.max(1, to - from) + 1e-30); }
export function residualDb(a, b) {
  let e = 0, r = 0; const n = Math.min(a.length, b.length);
  for (let i = 0; i < n; i++) { e += a[i] * a[i]; const d = a[i] - b[i]; r += d * d; }
  return 10 * Math.log10((r + 1e-30) / (e + 1e-30));
}
// FNV-1a over the float32 bit patterns: a compact fingerprint for bit-exact test vectors.
export function fnv1a32(f32arr) {
  const u = new Uint32Array(f32arr.buffer, f32arr.byteOffset, f32arr.length);
  let h = 0x811c9dc5;
  for (let i = 0; i < u.length; i++) {
    let w = u[i];
    for (let k = 0; k < 4; k++) { h ^= w & 0xff; h = Math.imul(h, 0x01000193) >>> 0; w >>>= 8; }
  }
  return h >>> 0;
}
export const hex = (u) => '0x' + (u >>> 0).toString(16).padStart(8, '0');
