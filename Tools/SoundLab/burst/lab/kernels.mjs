// kernels.mjs: block-based float32 reference kernels for the Unity Burst port (spec 10-upgrade/burst.md).
//
// Every kernel works on flat Float32Array "structs" laid out exactly like the C# structs (SoA inside a group of
// 4 lanes = one float4), processes [from, to) of a block, allocates nothing, and evaluates every operation in the
// CANONICAL order written below. The canonical order is what Unity.Mathematics' managed math.mad gives
// (mad(a,b,c) = a*b + c, two roundings), so these functions are bit-identical to the C# kernels run without Burst
// (Mono / IL2CPP / .NET). With FMA = true, mad(a,b,c) is fused (one rounding), which is what Burst emits for
// math.mad under FloatMode.Fast on ARM64 (fmla); that path matches to a tolerance, not bit-exactly.
import { f32, splitmix32 } from './common.mjs';

export let FMA = false;
export function setFma(on) { FMA = !!on; }
// mad(a, b, c): strict = round(round(a*b) + c); fused = round(a*b + c) (a*b of two float32 is exact in double;
// one double rounding of the sum then the float32 rounding: double rounding can differ from a true fma in rare
// halfway cases, which is inside the stated tolerance).
export const mad = (a, b, c) => (FMA ? f32(a * b + c) : f32(f32(a * b) + c));
export const mul = (a, b) => f32(a * b);
export const add = (a, b) => f32(a + b);
export const sub = (a, b) => f32(a - b);

// ---------------- constants (spec section "Constants") ----------------
export const FLUSH = f32(1e-20);       // |state| below this is set to 0 at block end (denormal guard; FLT_MIN = 1.18e-38)
export const CULL_DB = -110;           // a modal group whose loudest lane is below this (dBFS at the bank output) retires
export const CULL2 = f32(Math.pow(10, CULL_DB / 10)); // compared with |z|^2 * go^2 (energy)

// =====================================================================================================
// MB4: bank of damped complex resonators (modal oscillators), 4 modes per group (one float4 lane each).
// State z = zr + j zi, pole p = pr + j pi = r e^{jw}, r = exp(-1 / (tau sr)), w = 2 pi f / sr.
// Driven:  z <- p z + gi x[n]  (real input), y[n] = go Im(z).  Impulse response: go gi r^n sin(w n) (n >= 1).
// Free:    z <- p z,            y[n] = go Im(z).  Struck with phase phi: z0 = A e^{j phi} -> y = A go r^n sin(w n + phi).
// Group layout (24 floats = 96 bytes = 6 float4): zr, zi, pr, pi, go, gi.
export const MB = { ZR: 0, ZI: 4, PR: 8, PI: 12, GO: 16, GI: 20, STRIDE: 24 };

export function mbAlloc(groups) { return new Float32Array(groups * MB.STRIDE); }
// Main thread: set one mode (double math, rounded once to float32). tau in seconds (amplitude 1/e time).
export function mbSetMode(bank, mode, hz, tau, go, gi, sr) {
  const g = mode >> 2, l = mode & 3, o = g * MB.STRIDE;
  const r = tau > 0 ? Math.exp(-1 / (tau * sr)) : 0;
  const w = (2 * Math.PI * hz) / sr;
  bank[o + MB.PR + l] = f32(r * Math.cos(w));
  bank[o + MB.PI + l] = f32(r * Math.sin(w));
  bank[o + MB.GO + l] = f32(go);
  bank[o + MB.GI + l] = f32(gi);
}
// Set |p| directly (piecewise envelopes): mult = per-sample amplitude factor.
export function mbSetPole(bank, mode, hz, mult, sr) {
  const g = mode >> 2, l = mode & 3, o = g * MB.STRIDE;
  const w = (2 * Math.PI * hz) / sr;
  bank[o + MB.PR + l] = f32(mult * Math.cos(w));
  bank[o + MB.PI + l] = f32(mult * Math.sin(w));
}
// Strike: z += A e^{j phi} (block boundary or a split point).
export function mbStrike(bank, mode, amp, phase) {
  const g = mode >> 2, l = mode & 3, o = g * MB.STRIDE;
  bank[o + MB.ZR + l] = add(bank[o + MB.ZR + l], f32(amp * Math.cos(phase)));
  bank[o + MB.ZI + l] = add(bank[o + MB.ZI + l], f32(amp * Math.sin(phase)));
}

// acc: Float32Array(4 * frames), lane-interleaved (acc[4n + l]); groups are added in ascending order.
export function mbRenderDriven(bank, g, exc, acc, from, to) {
  const o = g * MB.STRIDE;
  for (let l = 0; l < 4; l++) {
    let zr = bank[o + MB.ZR + l], zi = bank[o + MB.ZI + l];
    const pr = bank[o + MB.PR + l], pi = bank[o + MB.PI + l], npi = -pi;
    const go = bank[o + MB.GO + l], gi = bank[o + MB.GI + l];
    for (let n = from; n < to; n++) {
      const x = exc[n];
      const nr = mad(pr, zr, mad(npi, zi, mul(gi, x)));
      const ni = mad(pr, zi, mul(pi, zr));
      zr = nr; zi = ni;
      acc[4 * n + l] = mad(go, ni, acc[4 * n + l]);
    }
    bank[o + MB.ZR + l] = zr; bank[o + MB.ZI + l] = zi;
  }
}
export function mbRenderFree(bank, g, acc, from, to) {
  const o = g * MB.STRIDE;
  for (let l = 0; l < 4; l++) {
    let zr = bank[o + MB.ZR + l], zi = bank[o + MB.ZI + l];
    const pr = bank[o + MB.PR + l], pi = bank[o + MB.PI + l], npi = -pi;
    const go = bank[o + MB.GO + l];
    for (let n = from; n < to; n++) {
      const nr = mad(pr, zr, mul(npi, zi));
      const ni = mad(pr, zi, mul(pi, zr));
      zr = nr; zi = ni;
      acc[4 * n + l] = mad(go, ni, acc[4 * n + l]);
    }
    bank[o + MB.ZR + l] = zr; bank[o + MB.ZI + l] = zi;
  }
}
// out[n] += csum(acc[n]) with Unity.Mathematics csum order (x + y) + (z + w); clears acc.
export function accResolve(acc, out, from, to) {
  for (let n = from; n < to; n++) {
    const s = add(add(acc[4 * n], acc[4 * n + 1]), add(acc[4 * n + 2], acc[4 * n + 3]));
    out[n] = add(out[n], s);
    acc[4 * n] = acc[4 * n + 1] = acc[4 * n + 2] = acc[4 * n + 3] = 0;
  }
}
// Block end: flush tiny states to 0 and return the group's loudest-lane energy |z|^2 go^2 (for culling and LOD).
export function mbEndBlock(bank, g) {
  const o = g * MB.STRIDE;
  let e = 0;
  for (let l = 0; l < 4; l++) {
    let zr = bank[o + MB.ZR + l], zi = bank[o + MB.ZI + l];
    if (Math.abs(zr) < FLUSH) zr = 0;
    if (Math.abs(zi) < FLUSH) zi = 0;
    bank[o + MB.ZR + l] = zr; bank[o + MB.ZI + l] = zi;
    const go = bank[o + MB.GO + l];
    const el = mul(add(mul(zr, zr), mul(zi, zi)), mul(go, go));
    if (el > e) e = el;
  }
  return e;
}

// =====================================================================================================
// WG4: 4 digital-waveguide strings per group (Karplus-Strong / Jaffe-Smith extended), one string per lane.
// Loop per lane: x = delay[w - L] -> first-order allpass fractional delay (c) -> one-pole loss filter (b0, a1)
//   -> + excitation e[n] -> written back at w; the bridge pickup y = go * v.
// Group header layout (floats): L[4] (as float; integer valued), c[4], x1[4], y1[4], b0[4], a1[4], s1[4], go[4],
// then 4 delay lines of `len` floats each (len = power of two >= Lmax + 2), write index w shared by the lanes.
export const WG = { L: 0, C: 4, X1: 8, Y1: 12, B0: 16, A1: 20, S1: 24, GO: 28, HDR: 32 };
export function wgAlloc(len) { return { len, mask: len - 1, w: 0, hdr: new Float32Array(WG.HDR), line: new Float32Array(4 * len) }; }
// Main thread: tune lane l to f0 with a frequency-dependent decay. t60 at f0 (s), bright in (0, 1]: one-pole pole a1 = 1 - bright.
export function wgSetString(wg, l, f0, t60, bright, go, sr) {
  const a1 = 1 - bright;                                  // loss filter pole
  const P = sr / f0;                                       // loop period in samples
  const g = Math.pow(10, -3 / (t60 * f0));                 // loop gain at f0 for a 60 dB decay in t60
  const w = (2 * Math.PI * f0) / sr;
  const re = 1 - a1 * Math.cos(w), im = a1 * Math.sin(w);
  const H = (1 - a1) / Math.sqrt(re * re + im * im);       // |one-pole| at f0 with b0 = 1 - a1 (C#: Math.Sqrt)
  const b0 = (1 - a1) * (g / H);
  const lossDelay = Math.atan2(im, re) / w;                 // phase delay of the loss filter at f0
  const rest = P - lossDelay;
  const L = Math.floor(rest - 0.5);                        // integer part; allpass covers d in [0.5, 1.5)
  const d = rest - L;
  const c = (1 - d) / (1 + d);                             // Thiran first-order allpass (low-frequency delay d)
  if (L + 2 > wg.len) throw new Error('delay line too short');
  wg.hdr[WG.L + l] = L; wg.hdr[WG.C + l] = f32(c);
  wg.hdr[WG.B0 + l] = f32(b0); wg.hdr[WG.A1 + l] = f32(a1); wg.hdr[WG.GO + l] = f32(go);
}
// exc4: Float32Array(4 * frames) lane-interleaved excitation; acc: Float32Array(4 * frames).
export function wgRender(wg, exc4, acc, from, to) {
  const h = wg.hdr, line = wg.line, len = wg.len, mask = wg.mask;
  let w = wg.w;
  for (let n = from; n < to; n++) {
    for (let l = 0; l < 4; l++) {
      const base = l * len;
      const L = h[WG.L + l];
      const x = line[base + ((w - L) & mask)];
      const y = mad(h[WG.C + l], sub(x, h[WG.Y1 + l]), h[WG.X1 + l]);
      h[WG.X1 + l] = x; h[WG.Y1 + l] = y;
      const s = mad(h[WG.B0 + l], y, mul(h[WG.A1 + l], h[WG.S1 + l]));
      h[WG.S1 + l] = s;
      const v = add(s, exc4[4 * n + l]);
      line[base + w] = v;
      acc[4 * n + l] = mad(h[WG.GO + l], v, acc[4 * n + l]);
    }
    w = (w + 1) & mask;
  }
  wg.w = w;
}
export function wgEndBlock(wg) {
  const h = wg.hdr;
  for (const k of [WG.X1, WG.Y1, WG.S1]) for (let l = 0; l < 4; l++) if (Math.abs(h[k + l]) < FLUSH) h[k + l] = 0;
  // the delay line itself decays with the loop gain; a lane whose recent |v| < FLUSH everywhere is zeroed by the
  // owner when it retires the string (energy tracked from acc), so lines never hold denormals for long.
}

// =====================================================================================================
// BQ4: 4 independent biquads (TDF-II, float32 state), one per lane, each on its own input lane.
// y = b0 x + z1; z1 = b1 x - a1 y + z2; z2 = b2 x - a2 y.  Layout: b0, b1, b2, na1, na2, z1, z2 (7 float4 = 28 floats).
export const BQ = { B0: 0, B1: 4, B2: 8, NA1: 12, NA2: 16, Z1: 20, Z2: 24, STRIDE: 28 };
export function bqSet(bq, l, c /* [b0,b1,b2,a1,a2] normalized */) {
  bq[BQ.B0 + l] = f32(c[0]); bq[BQ.B1 + l] = f32(c[1]); bq[BQ.B2 + l] = f32(c[2]);
  bq[BQ.NA1 + l] = f32(-c[3]); bq[BQ.NA2 + l] = f32(-c[4]);
}
// io4: Float32Array(4 * frames) lane-interleaved, processed in place.
export function bqRender(bq, io4, from, to) {
  for (let l = 0; l < 4; l++) {
    let z1 = bq[BQ.Z1 + l], z2 = bq[BQ.Z2 + l];
    const b0 = bq[BQ.B0 + l], b1 = bq[BQ.B1 + l], b2 = bq[BQ.B2 + l], na1 = bq[BQ.NA1 + l], na2 = bq[BQ.NA2 + l];
    for (let n = from; n < to; n++) {
      const x = io4[4 * n + l];
      const y = mad(b0, x, z1);
      z1 = mad(b1, x, mad(na1, y, z2));
      z2 = mad(b2, x, mul(na2, y));
      io4[4 * n + l] = y;
    }
    bq[BQ.Z1 + l] = Math.abs(z1) < FLUSH ? 0 : z1; bq[BQ.Z2 + l] = Math.abs(z2) < FLUSH ? 0 : z2;
  }
}

// =====================================================================================================
// NZ4: 4-lane xorshift32 white noise, lane seeds from splitmix32(seed). out4[4n + l] in [-1, 1).
export function nzSeed(seed) { const s = splitmix32(seed); const st = new Uint32Array(4); for (let l = 0; l < 4; l++) { let v = s(); st[l] = v === 0 ? 0x9e3779b9 : v; } return st; }
export function nzRender(st, out4, from, to) {
  for (let l = 0; l < 4; l++) {
    let x = st[l];
    for (let n = from; n < to; n++) {
      x ^= x << 13; x >>>= 0; x ^= x >>> 17; x ^= x << 5; x >>>= 0;
      out4[4 * n + l] = f32(f32(x | 0) * f32(1 / 2147483648));
    }
    st[l] = x;
  }
}

// =====================================================================================================
// EXP4: per-lane exponential gain g *= m (segment envelopes), applied to io4 in place: y = x g (value AT the sample, then advance).
export function expRender(gm /* Float32Array(8): g[4], m[4] */, io4, from, to) {
  for (let l = 0; l < 4; l++) {
    let g = gm[l]; const m = gm[4 + l];
    for (let n = from; n < to; n++) { io4[4 * n + l] = mul(io4[4 * n + l], g); g = mul(g, m); }
    gm[l] = Math.abs(g) < FLUSH ? 0 : g;
  }
}

// =====================================================================================================
// LOD: global budget over modal groups. Every group has an audibility a = sqrt(energy) * weight (weight: a fixed
// per-group perceptual factor set at note-on, e.g. the A-weighting gain of its mean frequency). When more groups
// are active than the budget, the quietest are faded out over one block (go ramps to 0 -> no click) and retired.
// O(n) selection with a 2 dB histogram from -130 to 0 dB (65 buckets): Burst-friendly, no sort.
export const LOD_BUCKETS = 65, LOD_DB_STEP = 2, LOD_FLOOR_DB = -130;
export function lodBucket(a) {
  if (a <= 0) return 0;
  const db = 20 * Math.log10(a);
  const b = Math.floor((db - LOD_FLOOR_DB) / LOD_DB_STEP);
  return b < 0 ? 0 : b >= LOD_BUCKETS ? LOD_BUCKETS - 1 : b;
}
// Returns the bucket threshold: groups with bucket < thr must go; with bucket == thr keep only `keepAtThr` of them
// (first come, in index order); protected groups (attack < 30 ms) count first and are never dropped.
export function lodThreshold(buckets /* Int32Array(n) */, prot /* Uint8Array(n) */, n, budget) {
  const hist = new Int32Array(LOD_BUCKETS);
  let protCount = 0;
  for (let i = 0; i < n; i++) if (prot[i]) protCount++; else hist[buckets[i]]++;
  let room = budget - protCount;
  if (room < 0) room = 0;
  let thr = LOD_BUCKETS; let keepAtThr = 0;
  for (let b = LOD_BUCKETS - 1; b >= 0; b--) {
    if (hist[b] <= room) { room -= hist[b]; thr = b; keepAtThr = hist[b]; }
    else { thr = b; keepAtThr = room; room = 0; break; }
  }
  return { thr, keepAtThr, protCount };
}
