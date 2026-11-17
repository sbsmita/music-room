// Analysis kit (no packages): FFT, BS.1770-4 loudness, 4x true peak, spectra, decay, peaks, clicks.

// ---------- FFT (radix-2, in place, complex) ----------
export function fft(re, im, inverse = false) {
  const n = re.length;
  for (let i = 1, j = 0; i < n; i++) {
    let bit = n >> 1;
    for (; j & bit; bit >>= 1) j ^= bit;
    j ^= bit;
    if (i < j) { let t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
  }
  for (let len = 2; len <= n; len <<= 1) {
    const ang = (2 * Math.PI) / len * (inverse ? 1 : -1);
    const wr = Math.cos(ang), wi = Math.sin(ang);
    for (let i = 0; i < n; i += len) {
      let cr = 1, ci = 0;
      for (let k = 0; k < len / 2; k++) {
        const a = i + k, b = a + len / 2;
        const xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
        re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
        const t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
      }
    }
  }
  if (inverse) for (let i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
}
export const nextPow2 = (n) => { let p = 1; while (p < n) p <<= 1; return p; };

// Power spectrum of x[from..from+len) with a Hann window, zero-padded to nfft. Returns Float64Array(nfft/2+1).
export function powerSpectrum(x, from, len, nfft = nextPow2(len)) {
  const re = new Float64Array(nfft), im = new Float64Array(nfft);
  for (let i = 0; i < len && from + i < x.length; i++) {
    const w = 0.5 - 0.5 * Math.cos((2 * Math.PI * (i + 0.5)) / len);
    re[i] = (x[from + i] || 0) * w;
  }
  fft(re, im);
  const p = new Float64Array(nfft / 2 + 1);
  for (let k = 0; k <= nfft / 2; k++) p[k] = re[k] * re[k] + im[k] * im[k];
  return p;
}

// Onset-safe variant (v2): RECTANGULAR window, zero-padded 4x. Used on renders that start with >= 2 ms of
// silence before the hit, so nothing at the onset is suppressed (a Hann/BH window from the onset hides ~10 ms).
export function powerSpectrumRect(x, from, len, nfft = nextPow2(4 * len)) {
  const re = new Float64Array(nfft), im = new Float64Array(nfft);
  for (let i = 0; i < len && from + i < x.length; i++) re[i] = x[from + i] || 0;
  fft(re, im);
  const p = new Float64Array(nfft / 2 + 1);
  for (let k = 0; k <= nfft / 2; k++) p[k] = re[k] * re[k] + im[k] * im[k];
  return p;
}
export function centroidRect(x, from, len, sr = 48000) {
  const p = powerSpectrumRect(x, from, len), nfft = (p.length - 1) * 2; let a = 0, b = 0;
  for (let k = 1; k < p.length; k++) { const f = (k * sr) / nfft; a += f * p[k]; b += p[k]; }
  return b > 0 ? a / b : 0;
}
export function bandLevelsRect(x, from, len, edges, sr = 48000) {
  const p = powerSpectrumRect(x, from, len), nfft = (p.length - 1) * 2, out = [];
  for (let b = 0; b + 1 < edges.length; b++) { let e = 0; for (let k = Math.ceil((edges[b] * nfft) / sr); k < Math.min(p.length, (edges[b + 1] * nfft) / sr); k++) e += p[k]; out.push(10 * Math.log10(e + 1e-30)); }
  return out;
}

// FFT convolution (overlap-add), x long, h short-ish. Returns Float32Array(x.length + h.length - 1).
export function convolve(x, h) {
  const L = 8192, nfft = nextPow2(L + h.length - 1);
  const Hr = new Float64Array(nfft), Hi = new Float64Array(nfft);
  for (let i = 0; i < h.length; i++) Hr[i] = h[i];
  fft(Hr, Hi);
  const out = new Float32Array(x.length + h.length - 1);
  const re = new Float64Array(nfft), im = new Float64Array(nfft);
  for (let s = 0; s < x.length; s += L) {
    re.fill(0); im.fill(0);
    let any = false;
    for (let i = 0; i < L && s + i < x.length; i++) { re[i] = x[s + i]; if (re[i]) any = true; }
    if (!any) continue;
    fft(re, im);
    for (let k = 0; k < nfft; k++) { const a = re[k] * Hr[k] - im[k] * Hi[k]; im[k] = re[k] * Hi[k] + im[k] * Hr[k]; re[k] = a; }
    fft(re, im, true);
    for (let i = 0; i < nfft && s + i < out.length; i++) out[s + i] += re[i];
  }
  return out;
}

// ---------- BS.1770-4 loudness ----------
function biq(x, b, a) {
  const y = new Float64Array(x.length);
  let x1 = 0, x2 = 0, y1 = 0, y2 = 0;
  for (let i = 0; i < x.length; i++) {
    const v = b[0] * x[i] + b[1] * x1 + b[2] * x2 - a[1] * y1 - a[2] * y2;
    x2 = x1; x1 = x[i]; y2 = y1; y1 = v; y[i] = v;
  }
  return y;
}
// K-weighting coefficients at 48 kHz (ITU-R BS.1770-4, Table 1 and 2).
const KS_B = [1.53512485958697, -2.69169618940638, 1.19839281085285], KS_A = [1, -1.69065929318241, 0.73248077421585];
const KH_B = [1, -2, 1], KH_A = [1, -1.99004745483398, 0.99007225036621];
export function kWeight(x) { return biq(biq(x, KS_B, KS_A), KH_B, KH_A); }

// Integrated loudness (LUFS) of 1+ channels (L/R weights 1). Gated per BS.1770-4.
export function integratedLoudness(channels, sr = 48000) {
  const kw = channels.map(kWeight);
  const blk = Math.round(0.4 * sr), hop = Math.round(0.1 * sr);
  const z = [];
  for (let s = 0; s + blk <= kw[0].length; s += hop) {
    let sum = 0;
    for (const c of kw) { let e = 0; for (let i = s; i < s + blk; i++) e += c[i] * c[i]; sum += e / blk; }
    z.push(sum);
  }
  if (!z.length) { // shorter than one block: whole-signal mean square
    let sum = 0; for (const c of kw) { let e = 0; for (let i = 0; i < c.length; i++) e += c[i] * c[i]; sum += e / c.length; }
    return -0.691 + 10 * Math.log10(sum + 1e-30);
  }
  const L = (v) => -0.691 + 10 * Math.log10(v + 1e-30);
  const abs = z.filter((v) => L(v) > -70);
  if (!abs.length) return -Infinity;
  const mAbs = abs.reduce((a, b) => a + b, 0) / abs.length;
  const rel = L(mAbs) - 10;
  const g = abs.filter((v) => L(v) > rel);
  return L(g.reduce((a, b) => a + b, 0) / g.length);
}
// Loudness of a short window (ungated mean square over [from, from+len)), channels summed: used for hit balance.
export function windowLoudness(channels, from, len) {
  let sum = 0;
  for (const c of channels) {
    const kw = kWeight(c.subarray(from, Math.min(c.length, from + len)));
    let e = 0; for (let i = 0; i < kw.length; i++) e += kw[i] * kw[i];
    sum += e / len;
  }
  return -0.691 + 10 * Math.log10(sum + 1e-30);
}

// ---------- 4x oversampled true peak ----------
let tpK = null;
export function truePeak(channels) {
  const P = 4, Z = 12, T = 2 * Z * P + 1;
  if (!tpK) {
    tpK = new Float64Array(T);
    for (let i = 0; i < T; i++) {
      const t = (i - Z * P) / P, s = t === 0 ? 1 : Math.sin(Math.PI * t) / (Math.PI * t);
      const w = 0.42 - 0.5 * Math.cos((2 * Math.PI * i) / (T - 1)) + 0.08 * Math.cos((4 * Math.PI * i) / (T - 1));
      tpK[i] = s * w;
    }
  }
  let peak = 0;
  for (const x of channels) {
    for (let n = 0; n < x.length; n++) {
      peak = Math.max(peak, Math.abs(x[n]));
      if (Math.abs(x[n]) < peak * 0.5 && Math.abs(x[n + 1] || 0) < peak * 0.5) continue; // only near candidate peaks
      for (let ph = 1; ph < P; ph++) {
        let s = 0;
        for (let k = -Z + 1; k <= Z; k++) { const i = n + k; if (i < 0 || i >= x.length) continue; s += x[i] * tpK[(ph - k * P) + Z * P] ; }
        peak = Math.max(peak, Math.abs(s));
      }
    }
  }
  return peak;
}
export const db = (x) => 20 * Math.log10(Math.max(1e-12, x));

// ---------- descriptors ----------
export function rms(x, from = 0, len = x.length - from) {
  let e = 0; const to = Math.min(x.length, from + len);
  for (let i = from; i < to; i++) e += x[i] * x[i];
  return Math.sqrt(e / Math.max(1, to - from));
}
export function peakAbs(x) { let m = 0; for (let i = 0; i < x.length; i++) m = Math.max(m, Math.abs(x[i])); return m; }

// Spectral centroid (Hz) of a window.
export function centroid(x, from, len, sr = 48000) {
  const p = powerSpectrum(x, from, len);
  const nfft = (p.length - 1) * 2;
  let a = 0, b = 0;
  for (let k = 1; k < p.length; k++) { const f = (k * sr) / nfft; a += f * p[k]; b += p[k]; }
  return b > 0 ? a / b : 0;
}
// Centroid track: [{t, c, levelDb}] every hop seconds with a win-second window.
export function centroidTrack(x, sr, win = 0.02, hop = 0.01, until = x.length / sr) {
  const out = [];
  const W = Math.round(win * sr);
  for (let t = 0; t + win <= until; t += hop) {
    const s = Math.round(t * sr);
    out.push({ t: +t.toFixed(3), c: Math.round(centroid(x, s, W, sr)), levelDb: +db(rms(x, s, W)).toFixed(1) });
  }
  return out;
}
// Energy in bands [f0,f1) over a window, dB.
export function bandLevels(x, from, len, edges, sr = 48000) {
  const p = powerSpectrum(x, from, len);
  const nfft = (p.length - 1) * 2;
  const out = [];
  for (let b = 0; b + 1 < edges.length; b++) {
    let e = 0;
    for (let k = Math.ceil((edges[b] * nfft) / sr); k < Math.min(p.length, (edges[b + 1] * nfft) / sr); k++) e += p[k];
    out.push(10 * Math.log10(e + 1e-30));
  }
  return out;
}
// T60 by Schroeder backward integration of a band-limited (biquad bp, 2 passes) signal; fits -5..-25 dB (T20 x3).
export function bandT60(x, sr, fc, q = 1.4) {
  const w = (2 * Math.PI * fc) / sr, al = Math.sin(w) / (2 * q), a0 = 1 + al;
  const b = [al / a0, 0, -al / a0], a = [1, (-2 * Math.cos(w)) / a0, (1 - al) / a0];
  const y = biq(biq(x, b, a), b, a);
  return schroederT60(y, sr);
}
export function schroederT60(y, sr, hi = -5, lo = -25) {
  const n = y.length; const e = new Float64Array(n);
  let s = 0; for (let i = n - 1; i >= 0; i--) { s += y[i] * y[i]; e[i] = s; }
  if (s <= 0) return 0;
  // start at the energy peak region: from the max of the squared envelope
  let i0 = 0, mx = 0; for (let i = 0; i < Math.min(n, sr); i++) if (y[i] * y[i] > mx) { mx = y[i] * y[i]; i0 = i; }
  const E0 = e[i0];
  let a = -1, bI = -1;
  for (let i = i0; i < n; i++) { const L = 10 * Math.log10(e[i] / E0); if (a < 0 && L <= hi) a = i; if (L <= lo) { bI = i; break; } }
  if (a < 0 || bI < 0) return NaN;
  // least squares slope between a and bI
  let sx = 0, sy = 0, sxx = 0, sxy = 0, m = 0;
  for (let i = a; i <= bI; i += 16) { const t = i / sr, L = 10 * Math.log10(e[i] / E0); sx += t; sy += L; sxx += t * t; sxy += t * L; m++; }
  const slope = (m * sxy - sx * sy) / (m * sxx - sx * sx);
  return -60 / slope;
}
// Peaks of the power spectrum (parabolic interpolation), strongest first, above minHz.
export function spectralPeaks(x, from, len, sr, { count = 12, minHz = 30, maxHz = 20000, nfft = 1 << 17, minSepHz = 8 } = {}) {
  const p = powerSpectrum(x, from, len, Math.max(nfft, nextPow2(len)));
  const N = (p.length - 1) * 2, df = sr / N;
  const cands = [];
  for (let k = Math.max(2, Math.floor(minHz / df)); k < Math.min(p.length - 2, maxHz / df); k++) {
    if (p[k] > p[k - 1] && p[k] >= p[k + 1]) {
      const a = 10 * Math.log10(p[k - 1] + 1e-30), b = 10 * Math.log10(p[k] + 1e-30), c = 10 * Math.log10(p[k + 1] + 1e-30);
      const d = (0.5 * (a - c)) / (a - 2 * b + c || 1e-12);
      cands.push({ f: (k + d) * df, db: b - 0.25 * (a - c) * d });
    }
  }
  cands.sort((u, v) => v.db - u.db);
  const out = [];
  for (const c of cands) { if (out.every((o) => Math.abs(o.f - c.f) > minSepHz)) out.push(c); if (out.length >= count) break; }
  return out;
}
// Click detector: largest |second difference| relative to the local rms in a window around it, outside the given onset windows.
export function clickScore(x, sr, onsets = [], guard = 0.004) {
  let worst = 0, at = -1;
  const W = Math.round(0.002 * sr);
  const g = Math.round(guard * sr);
  const isOnset = (i) => onsets.some((o) => i >= o - 8 && i < o + g);
  for (let i = 2; i < x.length; i++) {
    if (isOnset(i)) continue;
    const d2 = Math.abs(x[i] - 2 * x[i - 1] + x[i - 2]);
    if (d2 < 1e-6) continue;
    const r = rms(x, Math.max(0, i - W), 2 * W) + 1e-9;
    const s = d2 / r;
    if (s > worst) { worst = s; at = i; }
  }
  return { score: worst, atSec: at / sr };
}
// Fraction of energy above f Hz (aliasing / hiss indicator).
export function energyAbove(x, from, len, f, sr = 48000) {
  const p = powerSpectrum(x, from, len);
  const N = (p.length - 1) * 2; let a = 0, t = 0;
  for (let k = 1; k < p.length; k++) { t += p[k]; if ((k * sr) / N >= f) a += p[k]; }
  return t > 0 ? a / t : 0;
}
