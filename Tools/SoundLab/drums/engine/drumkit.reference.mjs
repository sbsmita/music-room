// drumkit.reference.mjs (v2): the NEW drum kit, a physically modelled acoustic kit as a block-based float32
// reference engine for the Unity C# / Burst port (spec: Docs/10-upgrade/drums.md).
//
// One persistent physical object per piece (kick, snare, hat, tom1, tom2, floor, crash, ride). A hit does
// not start a "voice": it strikes the object, whose modal state keeps ringing, so rolls build up, flams
// interfere, a re-strike meets a moving head, and the worst-case CPU is fixed (all 8 objects ringing).
//
// v2 (after the independent critique, see the spec's "Revision 2" section):
//  - every modal output is DC-free: the half-sample feed-forward term cancels the -1/2 DC gain of the
//    "input before rotation" recurrence (it was a one-directional low-frequency push under every hit);
//  - two stick slots per drum (left and right hand) whose forces are solved together; a third stroke waits
//    for a free slot (pending) instead of overwriting a stick that is still in contact;
//  - the head stiffens under the stick (tension rises with the cone deflection), so contact gets shorter and
//    the force pulse sharper as the stick gets faster (Wagner 2006: 3-6 ms, shorter at forte); the effective
//    tip mass is that of a stick held at the fulcrum (~1/4 of a 50 g stick);
//  - stick speed curve 12 u^1.1 m/s (the old kit's loudness vs u, within ~1 dB) instead of 0.3 * 40^u;
//  - energy transfers that used to be applied as state jumps on the 32-sample control grid (nonlinear
//    spreading, cymbal cascade, clash, choke) are now smooth: draining = a pole-radius change for the next
//    sub-block, injecting = a stochastic force over the next sub-block, noise-band gains ramp per sample;
//  - crash and ride: constant modal density above ~1 kHz (a flat plate's density is constant) and
//    cascade-fed noise bands above ~4 kHz, so the wash is dense; shorter, flatter low-mode decay (less hum).
//
// Port rules followed here: struct-of-arrays Float32Array state, all allocation in constructors, no
// allocation in process(), seeded RNGs (mulberry32 for setup and per-hit draws, xorshift32 per sample),
// control-rate work on a fixed 32-sample grid, sample-accurate events.

const TAU = 2 * Math.PI;
export const SUB = 32; // control sub-block: 1500 Hz at 48 kHz
export const MAX_BLOCK = 4096;
export const PIECES = ['kick', 'snare', 'hat', 'tom1', 'tom2', 'floor', 'crash', 'ride'];
const NOISE_VAR = 1 / 3; // variance of the uniform [-1, 1) noise

// ---------------- RNGs ----------------
export class Rng { // mulberry32 (same as the spec-06 reference)
  constructor(seed) { this.a = seed >>> 0; }
  next() {
    this.a = (this.a + 0x6d2b79f5) >>> 0;
    let t = this.a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  }
  bip() { return this.next() * 2 - 1; }
}
export class Noise { // xorshift32, uniform [-1, 1)
  constructor(seed) { this.s = (seed >>> 0) || 0x9e3779b9; }
  next() { let s = this.s; s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; this.s = s >>> 0; return (s | 0) * 4.656612873077393e-10; }
  fill(a, i0, i1) { for (let i = i0; i < i1; i++) a[i] = this.next(); }
  // one-pole low-passed noise (the stochastic force of the energy transfers: nonlinear forcing lives at the
  // combination frequencies of the low modes, not at 20 kHz; a white force would add a mass-line hiss floor)
  fillLP(a, i0, i1, al) { let y = this.y || 0; for (let i = i0; i < i1; i++) { y += al * (this.next() - y); a[i] = y; } this.y = y; }
}
export const INJ_LP = 4000; // Hz, corner of that low-pass
// the injected energy rate of a resonator at w (rad/sample) driven by the low-passed noise, re white noise
export const injComp = (w, sr) => { const al = 1 - Math.exp((-2 * Math.PI * INJ_LP) / sr), be = 1 - al; return Math.sqrt((1 - 2 * be * Math.cos(w) + be * be) / (al * al)); };

// ---------------- Bessel (setup only) ----------------
export function besselJ(m, x) {
  const N = 160; let s = 0;
  for (let i = 0; i < N; i++) { const t = ((i + 0.5) * Math.PI) / N; s += Math.cos(m * t - x * Math.sin(t)); }
  return s / N;
}
const ZCACHE = new Map();
export function besselZeros(m, count) {
  const key = m + ':' + count;
  if (ZCACHE.has(key)) return ZCACHE.get(key);
  const out = []; let x = Math.max(0.5, m), prev = besselJ(m, x);
  while (out.length < count && x < 80) {
    const nx = x + 0.05, v = besselJ(m, nx);
    if ((prev < 0) !== (v < 0)) { let lo = x, hi = nx; for (let i = 0; i < 40; i++) { const mid = 0.5 * (lo + hi); if ((besselJ(m, lo) < 0) === (besselJ(m, mid) < 0)) lo = mid; else hi = mid; } out.push(0.5 * (lo + hi)); }
    x = nx; prev = v;
  }
  ZCACHE.set(key, out);
  return out;
}
export const J01 = 2.404825557695773;

// ---------------- velocity curves ----------------
// u = the app's stroke value (0.05..1, the unchanged Classic stroke curve); V = physical impact speed (m/s).
// v2: a power curve. Loudness tracks the old kit's loudness-vs-u within ~1 dB (so ordinary VR strokes keep
// their level and tracking jitter is not amplified), the extremes still reach a real pp and a real ff.
export const STICK_V = (u) => Math.max(0.4, 12 * Math.pow(clamp01(u), 1.1));   // 0.4 (u <= .05) .. 12 m/s
export const FINGER_V = (u) => Math.max(0.3, 3.6 * Math.pow(clamp01(u), 1.1)); // fingertip/hand: .. 3.6 m/s
export const CYM_V = (u) => Math.max(0.4, 12 * Math.pow(clamp01(u), 0.9));    // stick on cymbals/hats (glancing VR swings): .. 12 m/s
export const BEATER_V = (u) => 6.3 * Math.pow(clamp01(u), 0.85);              // kick beater: 1.94 (u .25, the nod minimum) .. 6.3 m/s
export const PEDAL_V = (u) => 0.15 + 0.85 * clamp01(u);                       // hat pedal closing speed of the top plate
export const STICK_V_EXPRESSIVE = (u) => 0.3 * Math.pow(40, clamp01(u));      // optional "expressive" setting (v1 curve)
function clamp01(x) { return x < 0 ? 0 : x > 1 ? 1 : x; }

// ---------------- the modal bank (SoA) ----------------
// Mode k: complex state z = zr + i zi (zi = displacement q in m, zr = velocity / omega), pole p = r e^{i w}.
// Update: z <- p (z + input), the input entering the real (velocity) part.
// Output (v2): gOut * Re(z BEFORE this sample's update) + c0 * gOut * input, c0 = 1 - Re(1/(1-p)) ~ 1/2.
// The impulse response is then [c0, Re p, Re p^2, ...]: zero DC gain (a velocity output must have none) and no
// constant high-frequency floor (the sampled velocity jump counts half at its own sample, as a band-limited step
// does). v1 used Re(z after) with no c0 term: DC gain -1/2, i.e. a one-directional push under every hit. The
// per-input feed-forward sums G_ff = sum gOut*g*c0 are precomputed per strike / per sub-block.
class Bank {
  constructor(n) {
    this.n = n; const P = (n + 3) & ~3; this.cap = P; // padded to 4 lanes
    for (const k of ['zr', 'zi', 'cr', 'ci', 'cr0', 'ci0', 'w0', 'rad', 'gOut', 'gIn', 'phi', 'psi', 'gSym', 'gN', 'eW', 'f', 'm', 'ffk']) this[k] = new Float32Array(P);
    this.act = new Uint8Array(P).fill(1); // 0 = the mode is silent (below -120 dBFS) and receives no input: skipped
  }
  setMode(k, fHz, t60, sr) {
    const w = (TAU * fHz) / sr, r = Math.exp(-6.907755278982137 / (t60 * sr));
    this.f[k] = fHz; this.w0[k] = w; this.rad[k] = r;
    this.cr0[k] = this.cr[k] = r * Math.cos(w); this.ci0[k] = this.ci[k] = r * Math.sin(w);
    const pr = r * Math.cos(w), pi = r * Math.sin(w);
    this.ffk[k] = 1 - (1 - pr) / ((1 - pr) * (1 - pr) + pi * pi); // c0 = 1 - Re(1/(1-p))
  }
  setT60(k, t60, sr) { const r = Math.exp(-6.907755278982137 / (t60 * sr)); const s = r / this.rad[k]; this.rad[k] = r; this.cr0[k] *= s; this.ci0[k] *= s; this.cr[k] *= s; this.ci[k] *= s; }
  clear() { this.zr.fill(0); this.zi.fill(0); this.gN.fill(0); this.act.fill(1); }
  ff(g, from = 0, to = this.n) { let s = 0; for (let k = from; k < to; k++) s += this.gOut[k] * g[k] * this.ffk[k]; return s; }
  energy(wArr, from = 0, to = this.n) { let e = 0; const { zr, zi } = this; for (let k = from; k < to; k++) e += wArr[k] * (zr[k] * zr[k] + zi[k] * zi[k]); return e; }
  // scale every base pole frequency by s (tension glide): p0 = rad e^{i w0 s}
  glide(s, from = 0, to = this.n) { const { w0, rad, cr0, ci0 } = this; for (let k = from; k < to; k++) { const w = w0[k] * s; cr0[k] = rad[k] * Math.cos(w); ci0[k] = rad[k] * Math.sin(w); } }
  // the running poles = base poles x an extra per-sample decay factor (smooth damping for one sub-block)
  poles(mul, from = 0, to = this.n) { const { cr, ci, cr0, ci0 } = this; for (let k = from; k < to; k++) { cr[k] = cr0[k] * mul; ci[k] = ci0[k] * mul; } }
  flush(from = 0, to = this.n) { const { zr, zi } = this; for (let k = from; k < to; k++) if (zr[k] * zr[k] + zi[k] * zi[k] < 1e-30) { zr[k] = 0; zi[k] = 0; } } // no subnormals
  // energy culling: a mode whose output is below `floor` (gOut^2 |z|^2, i.e. ~-120 dBFS after the level) and that
  // gets no input is zeroed and skipped until the next stroke (or injection) wakes it
  cull(from, to, floor) { const { zr, zi, gOut, gN, act } = this; let n = 0; for (let k = from; k < to; k++) { if (!act[k]) continue; const e = gOut[k] * gOut[k] * (zr[k] * zr[k] + zi[k] * zi[k]); if (e < floor && gN[k] === 0) { zr[k] = 0; zi[k] = 0; act[k] = 0; } else n++; } return n; }
}

// ---- mode-outer kernels over modes [k0, k1) and samples [i0, i1): out[i] += sum_k gOut_k Re(z_k) ----
// (Burst: one specialised job per variant, float4 lanes over k; the state stays in registers over i.)
function ring0(B, k0, k1, i0, i1, out, aux) {
  const { zr, zi, cr, ci, gOut, psi, act } = B;
  for (let k = k0; k < k1; k++) {
    if (!act[k]) continue;
    let a = zr[k], b = zi[k]; const c = cr[k], s = ci[k], g = gOut[k];
    if (aux) { const p = psi[k]; for (let i = i0; i < i1; i++) { out[i] += g * a; aux[i] += p * b; const na = c * a - s * b; b = s * a + c * b; a = na; } }
    else for (let i = i0; i < i1; i++) { out[i] += g * a; const na = c * a - s * b; b = s * a + c * b; a = na; }
    zr[k] = a; zi[k] = b;
  }
}
function ring1(B, k0, k1, i0, i1, out, u, gu, aux) {
  const { zr, zi, cr, ci, gOut, psi, act } = B;
  for (let k = k0; k < k1; k++) {
    if (!act[k]) continue;
    let a = zr[k], b = zi[k]; const c = cr[k], s = ci[k], g = gOut[k], gi = gu[k];
    if (gi === 0) { // (a mode without this input: plain ring)
      if (aux) { const p = psi[k]; for (let i = i0; i < i1; i++) { out[i] += g * a; aux[i] += p * b; const na = c * a - s * b; b = s * a + c * b; a = na; } }
      else for (let i = i0; i < i1; i++) { out[i] += g * a; const na = c * a - s * b; b = s * a + c * b; a = na; }
    } else if (aux) { const p = psi[k]; for (let i = i0; i < i1; i++) { out[i] += g * a; aux[i] += p * b; const x = a + gi * u[i]; a = c * x - s * b; b = s * x + c * b; } }
    else for (let i = i0; i < i1; i++) { out[i] += g * a; const x = a + gi * u[i]; a = c * x - s * b; b = s * x + c * b; }
    zr[k] = a; zi[k] = b;
  }
}
function ring2(B, k0, k1, i0, i1, out, u, gu, v, gv, aux) {
  const { zr, zi, cr, ci, gOut, psi, act } = B;
  for (let k = k0; k < k1; k++) {
    if (!act[k]) continue;
    let a = zr[k], b = zi[k]; const c = cr[k], s = ci[k], g = gOut[k], gi = gu[k], gj = gv[k];
    if (aux) { const p = psi[k]; for (let i = i0; i < i1; i++) { out[i] += g * a; aux[i] += p * b; const x = a + gi * u[i] + gj * v[i]; a = c * x - s * b; b = s * x + c * b; } }
    else for (let i = i0; i < i1; i++) { out[i] += g * a; const x = a + gi * u[i] + gj * v[i]; a = c * x - s * b; b = s * x + c * b; }
    zr[k] = a; zi[k] = b;
  }
}
function ring4(B, k0, k1, i0, i1, out, u1, g1, u2, g2, u3, g3, u4, g4, aux) { // rare: dense modes during a contact
  const { zr, zi, cr, ci, gOut, psi } = B;
  for (let k = k0; k < k1; k++) {
    let a = zr[k], b = zi[k]; const c = cr[k], s = ci[k], g = gOut[k], h1 = g1[k], h2 = g2[k], h3 = g3[k], h4 = g4[k], p = psi[k];
    for (let i = i0; i < i1; i++) { out[i] += g * a; if (aux) aux[i] += p * b; const x = a + h1 * u1[i] + h2 * u2[i] + h3 * u3[i] + h4 * u4[i]; a = c * x - s * b; b = s * x + c * b; }
    zr[k] = a; zi[k] = b;
  }
}

// ---------------- simple biquad (RBJ), used for wires and noise bands ----------------
class Biquad {
  constructor() { this.b0 = 1; this.b1 = this.b2 = this.a1 = this.a2 = 0; this.x1 = this.x2 = this.y1 = this.y2 = 0; }
  set(type, f, q, sr, gainDb = 0) {
    const w = (TAU * Math.min(f, sr * 0.49)) / sr, cw = Math.cos(w), sw = Math.sin(w), al = sw / (2 * q), A = Math.pow(10, gainDb / 40);
    let b0, b1, b2, a0, a1, a2;
    if (type === 'lp') { b0 = (1 - cw) / 2; b1 = 1 - cw; b2 = b0; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; }
    else if (type === 'hp') { b0 = (1 + cw) / 2; b1 = -(1 + cw); b2 = b0; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; }
    else if (type === 'bp') { b0 = al; b1 = 0; b2 = -al; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; }
    else { b0 = 1 + al * A; b1 = -2 * cw; b2 = 1 - al * A; a0 = 1 + al / A; a1 = -2 * cw; a2 = 1 - al / A; }
    this.b0 = b0 / a0; this.b1 = b1 / a0; this.b2 = b2 / a0; this.a1 = a1 / a0; this.a2 = a2 / a0;
    return this;
  }
  tick(x) { const y = this.b0 * x + this.b1 * this.x1 + this.b2 * this.x2 - this.a1 * this.y1 - this.a2 * this.y2; this.x2 = this.x1; this.x1 = x; this.y2 = this.y1; this.y1 = y; return y; }
  clear() { this.x1 = this.x2 = this.y1 = this.y2 = 0; }
}

// ======================================================================================
// MEMBRANE DRUMS
// ======================================================================================
// Data for each drum. Frequencies in Hz, T60 in s, lengths in m, areal density sigma in kg/m^2.
// low: explicit low modes {m, n, f, t60, pb (batter participation), pr (resonant-head participation), rad}.
// stiff: head stiffening under the stick. sigma_t = (y / y2)^2 is the relative tension rise for a cone of
//   apex deflection y; y2 = a sqrt(2 T / (Eh c)) (Eh = Young's modulus x thickness of the film, c = shape).
export const DRUMS = {
  snare: { // 14" x 5.5" snare, coated 10-mil batter, 3-mil snare-side head, wires on. Low modes: Rossing 1992.
    a: 0.178, sigma: 0.36, fIdeal: 190, maxHz: 1250, split: 0.006,
    low: [
      { m: 0, n: 1, f: 183, t60: 0.22, pb: 0.85, pr: 0.75, rad: 1.0 },  // heads in phase (Rossing: 183 Hz)
      { m: 1, n: 1, f: 275, t60: 0.34, pb: 0.75, pr: -0.65, rad: 0.45 }, // (1,1) heads opposite (275 Hz)
      { m: 0, n: 1, f: 330, t60: 0.16, pb: 0.55, pr: -0.9, rad: 0.55 },  // (0,1) heads opposite (330 Hz)
      { m: 1, n: 1, f: 344, t60: 0.30, pb: 0.65, pr: 0.75, rad: 0.7 },  // (1,1) in phase (344 Hz)
      { m: 2, n: 1, f: 405, t60: 0.36, pb: 1.0, pr: 0.2, rad: 0.6 },    // (2,1) batter (405 Hz)
      { m: 0, n: 2, f: 451, t60: 0.22, pb: 1.0, pr: 0.25, rad: 0.8 },   // (0,2) batter (451 Hz)
      { m: 0, n: 2, f: 595, t60: 0.12, pb: 0.25, pr: 1.0, rad: 0.6 },   // (0,2) snare head (595 Hz)
    ],
    t60: { ref: 500, at: 0.30, exp: 0.55, min: 0.035 },
    dense: { n: 36, from: 1250, to: 7500, t60From: 0.09, t60To: 0.018, amp: 1.0, drive: 1.6 }, contactRadius: 0.004,
    rad: { gamma: 1.0, dipole: 0.55 },
    glide: 0.022, // fractional pitch rise of the hardest stroke (12 m/s) at the centre
    nl: { kappa: 14, nu: 1.0, eff: 1.0 },
    stiff: { Eh: 1.1e6, c: 4 },
    stick: { m: 0.011, K: 3.0e7, alpha: 1.5, lam: 0.25 },
    body: [ // shell (maple) and die-cast hoop modes: {f, t60, g, hoop}
      { f: 740, t60: 0.07, g: 0.5, hoop: 0.2 }, { f: 1290, t60: 0.05, g: 0.4, hoop: 0.4 }, { f: 1930, t60: 0.06, g: 0.5, hoop: 1.0 },
      { f: 2660, t60: 0.05, g: 0.45, hoop: 1.0 }, { f: 3550, t60: 0.04, g: 0.3, hoop: 0.9 }, { f: 4620, t60: 0.03, g: 0.18, hoop: 0.8 },
      { f: 5890, t60: 0.022, g: 0.12, hoop: 0.7 },
    ],
    bodyFromHead: 2.4, // shell drive per unit contact force at the centre (rises toward the rim)
    wires: true, slots: 2, symHz: 1e9,
    out: { head: 1.0, body: 4.8, stick: 2.5, wires: 0.0075 },
  },
  tom1: { // 10" x 8" rack tom, batter (0,1) ~165 Hz, resonant head a little higher
    a: 0.127, sigma: 0.36, fIdeal: 172, maxHz: 1300, split: 0.005,
    low: [
      { m: 0, n: 1, f: 165, t60: 0.55, pb: 0.85, pr: 0.6, rad: 1.0 },
      { m: 1, n: 1, f: 247, t60: 1.05, pb: 0.8, pr: -0.5, rad: 0.6 },
      { m: 0, n: 1, f: 229, t60: 0.40, pb: 0.5, pr: -0.85, rad: 0.5 },
      { m: 1, n: 1, f: 268, t60: 0.85, pb: 0.6, pr: 0.7, rad: 0.7 },
      { m: 2, n: 1, f: 336, t60: 0.75, pb: 1.0, pr: 0.2, rad: 0.6 },
      { m: 0, n: 2, f: 390, t60: 0.45, pb: 1.0, pr: 0.3, rad: 0.7 },
    ],
    t60: { ref: 500, at: 0.55, exp: 0.6, min: 0.03 },
    dense: { n: 28, from: 1300, to: 7000, t60From: 0.08, t60To: 0.015, amp: 0.6, drive: 0.6 }, contactRadius: 0.004,
    rad: { gamma: 1.0, dipole: 0.5 },
    glide: 0.035,
    nl: { kappa: 6, nu: 1.2, eff: 1.0 },
    stiff: { Eh: 1.1e6, c: 1.5 },
    stick: { m: 0.011, K: 3.0e7, alpha: 1.5, lam: 0.25 },
    body: [{ f: 980, t60: 0.08, g: 0.4, hoop: 0.3 }, { f: 1620, t60: 0.06, g: 0.35, hoop: 0.6 }, { f: 2480, t60: 0.05, g: 0.35, hoop: 1.0 }, { f: 3460, t60: 0.04, g: 0.3, hoop: 1.0 }, { f: 4700, t60: 0.03, g: 0.2, hoop: 0.8 }],
    bodyFromHead: 7.0,
    wires: false, slots: 2,
    out: { head: 1.0, body: 4.8, stick: 2.5 },
  },
  tom2: { // 12" x 9" rack tom
    a: 0.152, sigma: 0.36, fIdeal: 137, maxHz: 1200, split: 0.005,
    low: [
      { m: 0, n: 1, f: 131, t60: 0.65, pb: 0.85, pr: 0.6, rad: 1.0 },
      { m: 1, n: 1, f: 196, t60: 1.2, pb: 0.8, pr: -0.5, rad: 0.6 },
      { m: 0, n: 1, f: 182, t60: 0.45, pb: 0.5, pr: -0.85, rad: 0.5 },
      { m: 1, n: 1, f: 213, t60: 1.0, pb: 0.6, pr: 0.7, rad: 0.7 },
      { m: 2, n: 1, f: 268, t60: 0.9, pb: 1.0, pr: 0.2, rad: 0.6 },
      { m: 0, n: 2, f: 312, t60: 0.5, pb: 1.0, pr: 0.3, rad: 0.7 },
    ],
    t60: { ref: 450, at: 0.62, exp: 0.6, min: 0.03 },
    dense: { n: 28, from: 1200, to: 6500, t60From: 0.08, t60To: 0.015, amp: 0.6, drive: 0.7 }, contactRadius: 0.004,
    rad: { gamma: 1.0, dipole: 0.5 },
    glide: 0.04,
    nl: { kappa: 6, nu: 1.2, eff: 1.0 },
    stiff: { Eh: 1.1e6, c: 1.5 },
    stick: { m: 0.011, K: 3.0e7, alpha: 1.5, lam: 0.25 },
    body: [{ f: 860, t60: 0.08, g: 0.4, hoop: 0.3 }, { f: 1420, t60: 0.06, g: 0.35, hoop: 0.6 }, { f: 2210, t60: 0.05, g: 0.35, hoop: 1.0 }, { f: 3120, t60: 0.04, g: 0.3, hoop: 1.0 }, { f: 4300, t60: 0.03, g: 0.2, hoop: 0.8 }],
    bodyFromHead: 7.0,
    wires: false, slots: 2,
    out: { head: 1.0, body: 4.8, stick: 2.5 },
  },
  floor: { // 16" x 16" floor tom
    a: 0.203, sigma: 0.36, fIdeal: 103, maxHz: 1000, split: 0.004,
    low: [
      { m: 0, n: 1, f: 98, t60: 0.85, pb: 0.85, pr: 0.6, rad: 1.0 },
      { m: 1, n: 1, f: 147, t60: 1.6, pb: 0.8, pr: -0.5, rad: 0.6 },
      { m: 0, n: 1, f: 137, t60: 0.6, pb: 0.5, pr: -0.85, rad: 0.5 },
      { m: 1, n: 1, f: 160, t60: 1.3, pb: 0.6, pr: 0.7, rad: 0.7 },
      { m: 2, n: 1, f: 201, t60: 1.1, pb: 1.0, pr: 0.2, rad: 0.6 },
      { m: 0, n: 2, f: 236, t60: 0.6, pb: 1.0, pr: 0.3, rad: 0.7 },
    ],
    t60: { ref: 400, at: 0.75, exp: 0.6, min: 0.03 },
    dense: { n: 30, from: 1000, to: 6000, t60From: 0.09, t60To: 0.016, amp: 0.6, drive: 1.2 }, contactRadius: 0.004,
    rad: { gamma: 1.0, dipole: 0.5 },
    glide: 0.05,
    nl: { kappa: 6, nu: 1.2, eff: 1.0 },
    stiff: { Eh: 1.1e6, c: 1.5 },
    stick: { m: 0.011, K: 3.0e7, alpha: 1.5, lam: 0.25 },
    body: [{ f: 640, t60: 0.09, g: 0.4, hoop: 0.3 }, { f: 1080, t60: 0.07, g: 0.35, hoop: 0.6 }, { f: 1710, t60: 0.05, g: 0.35, hoop: 1.0 }, { f: 2480, t60: 0.04, g: 0.3, hoop: 1.0 }, { f: 3500, t60: 0.03, g: 0.2, hoop: 0.8 }],
    bodyFromHead: 7.0,
    wires: false, slots: 2,
    out: { head: 1.0, body: 4.8, stick: 2.5 },
  },
  kick: { // 22" x 16" kick, ported front head, felt beater, a pillow inside
    a: 0.279, sigma: 0.36, fIdeal: 60, maxHz: 600, split: 0.004,
    low: [
      { m: 0, n: 1, f: 56, t60: 0.42, pb: 0.85, pr: 0.6, rad: 1.0 },
      { m: 1, n: 1, f: 82, t60: 0.36, pb: 0.8, pr: -0.5, rad: 0.55 },
      { m: 0, n: 1, f: 91, t60: 0.22, pb: 0.5, pr: -0.85, rad: 0.5 },
      { m: 1, n: 1, f: 99, t60: 0.30, pb: 0.6, pr: 0.7, rad: 0.7 },
      { m: 2, n: 1, f: 121, t60: 0.26, pb: 1.0, pr: 0.2, rad: 0.6 },
      { m: 0, n: 2, f: 142, t60: 0.18, pb: 1.0, pr: 0.3, rad: 0.7 },
    ],
    t60: { ref: 250, at: 0.18, exp: 0.5, min: 0.03 },
    dense: { n: 30, from: 1200, to: 6000, t60From: 0.05, t60To: 0.012, amp: 0.7, drive: 1.2 }, contactRadius: 0.015,
    rad: { gamma: 0.7, dipole: 0.6 },
    glide: 0.055, // Rossing 1992: a full blow (6 mm) is ~10 % sharp; the nod kick's hardest stroke is 6.3 m/s
    nl: { kappa: 4, nu: 1.2, eff: 1.0 },
    stiff: { Eh: 1.1e6, c: 0.25 },
    stick: { m: 0.04, K: 8.0e9, alpha: 2.5, lam: 0.4 }, // felt beater (hammer-felt exponent)
    body: [{ f: 310, t60: 0.08, g: 0.35, hoop: 0.2 }, { f: 540, t60: 0.06, g: 0.3, hoop: 0.4 }, { f: 870, t60: 0.05, g: 0.25, hoop: 0.6 }, { f: 1330, t60: 0.04, g: 0.2, hoop: 0.6 }],
    bodyFromHead: 3.0,
    wires: false, slots: 1, maxR: 0.22, // the beater only lands near the centre
    out: { head: 1.0, body: 4.8, stick: 0.0 },
  },
};
// Stick: a 5A hickory stick's free-free bending modes, damped by the hand (4 modes).
const STICK_MODES = [{ f: 916, t60: 0.05, g: 1.0 }, { f: 1795, t60: 0.04, g: 0.8 }, { f: 2965, t60: 0.03, g: 0.55 }, { f: 4430, t60: 0.02, g: 0.35 }];
// Implements: tip (stick tip), shoulder (stick shaft), finger (bare fingertip/hand), beater (kick felt)
export const IMPLEMENTS = {
  tip: { m: 1.0, K: 1.0, alpha: 1.5, lam: 1.0, stick: 1.0, zr: 1.0 },
  shoulder: { m: 1.6, K: 0.6, alpha: 1.5, lam: 1.0, stick: 1.3, zr: 2.0 },
  finger: { m: 2.6, K: 0.004, alpha: 2.0, lam: 3.0, stick: 0.0, zr: 3.0 },
};
export const FLIGHT = 0.004; // s: a stick that has left the head stays "near" it this long (re-contacts are real)
export const CONTACT_MAX = 0.03; // s: safety limit of one stroke's contact + flight

class Slot { // one stick (or beater) on one membrane
  constructor(cap) { this.phi = new Float32Array(cap); this.gIn = new Float32Array(cap); this.reset(); }
  reset() { this.on = false; this.touch = false; this.started = false; this.xs = 0; this.vs = 0; this.m = 0.013; this.K = 3e7; this.alpha = 1.5; this.lam = 0.25; this.t = 0; this.fl = 0; this.dPrev = 0; this.xw = 0; this.tauW = 5e-5; this.lk = 0; this.Cw = 0; this.Z = 1; this.G = 0; this.GffL = 0; this.GffD = 0; this.ysP = 0; this.F = 0; this.stickGain = 0; this.bodyDrive = 0; this.rimZone = false; }
}

class Membrane {
  constructor(name, sr, seed) {
    const D = DRUMS[name];
    this.name = name; this.D = D; this.sr = sr; this.dt = 1 / sr;
    const rng = new Rng(seed);
    this.rng = new Rng(seed ^ 0x5bd1e995);
    this.noise = new Noise(seed * 7919 + 13);
    // ---- head modes ----
    const list = [];
    const have = new Set(D.low.map((l) => l.m + ',' + l.n));
    for (const l of D.low) {
      const j = besselZeros(l.m, l.n)[l.n - 1];
      if (l.m === 0) list.push({ ...l, j, orient: 0 });
      else { // two orientations of a degenerate pair, split by uneven tension
        const s = D.split * (0.6 + 0.8 * rng.next());
        list.push({ ...l, j, f: l.f * (1 - s / 2), orient: 0 });
        list.push({ ...l, j, f: l.f * (1 + s / 2), orient: 1, t60: l.t60 * 0.93 });
      }
    }
    this.nLow = list.length; // the measured/coupled low modes carry the large amplitudes that stretch the head
    for (let m = 0; m <= 24; m++) {
      const zs = besselZeros(m, 8);
      for (let n = 1; n <= zs.length; n++) {
        const j = zs[n - 1], f = D.fIdeal * (j / J01) * (1 + 0.012 * rng.bip());
        if (f > D.maxHz || have.has(m + ',' + n)) continue;
        const t60 = Math.max(D.t60.min, D.t60.at * Math.pow(f / D.t60.ref, -D.t60.exp));
        const pr = 0.35 * rng.bip();
        const rad = m === 0 ? 0.9 : D.rad.dipole;
        if (m === 0) list.push({ m, n, j, f, t60, pb: 1, pr, rad, orient: 0 });
        else {
          const s = D.split * (0.6 + 0.8 * rng.next());
          list.push({ m, n, j, f: f * (1 - s / 2), t60, pb: 1, pr, rad, orient: 0 });
          list.push({ m, n, j, f: f * (1 + s / 2), t60: t60 * 0.93, pb: 1, pr: 0.35 * rng.bip(), rad, orient: 1 });
        }
      }
    }
    { // the continuation, sorted by frequency (so the modes that take the sympathetic air force are one range);
      // a drum struck only near the centre (the kick: maxR) drops the modes that cannot be excited there
      let rest = list.splice(this.nLow);
      if (D.maxR) rest = rest.filter((md) => { let mx = 0; for (let q = 0; q <= 20; q++) mx = Math.max(mx, Math.abs(besselJ(md.m, (md.j * D.maxR * q) / 20))); return mx > 1e-3; });
      rest.sort((x, y) => x.f - y.f); list.push(...rest);
    }
    const nMembrane = list.length;
    // dense high-mode field: too many modes to list; random frequencies, shapes and signs (fixed per drum)
    for (let i = 0; i < D.dense.n; i++) {
      const u = (i + rng.next()) / D.dense.n, f = D.dense.from * Math.pow(D.dense.to / D.dense.from, u);
      const j = J01 * (f / D.fIdeal);
      list.push({ m: 1 + Math.floor(rng.next() * 9), n: 1, j, f, t60: D.dense.t60From * Math.pow(D.dense.t60To / D.dense.t60From, u), pb: D.dense.amp * (0.5 + rng.next()), pr: 0.3 * rng.bip(), rad: D.rad.dipole, orient: rng.next() < 0.5 ? 0 : 1, dense: true, ph: rng.next() * TAU, rshape: 0.3 + 0.7 * rng.next(), sg: rng.next() < 0.5 ? -1 : 1 });
    }
    this.modes = list; this.nMembrane = nMembrane;
    { // local wave impedance of the head under the tip and the static compliance of the unlisted modes
      const cw = (TAU * D.fIdeal * D.a) / J01, T = D.sigma * cw * cw; this.T = T;
      this.Zloc = TAU * D.contactRadius * Math.sqrt(T * D.sigma);
      this.Cres = Math.max(0.2, Math.log(cw / (TAU * D.maxHz * D.contactRadius))) / (TAU * T);
      this.y2 = D.a * Math.sqrt((2 * T) / (D.stiff.Eh * D.stiff.c)); // cone deflection that doubles the tension
    }
    const B = (this.head = new Bank(list.length));
    const A = Math.PI * D.a * D.a;
    // radial shape tables J_m(j r) on 65 points, modal masses, weights
    this.shape = new Float32Array(list.length * 65); this.eM = new Float32Array(B.cap);
    for (let k = 0; k < list.length; k++) {
      const md = list[k];
      B.setMode(k, md.f, md.t60, sr);
      B.m[k] = md.m;
      if (!md.dense) {
        for (let q = 0; q <= 64; q++) this.shape[k * 65 + q] = besselJ(md.m, md.j * (q / 64));
        const J1 = besselJ(md.m + 1, md.j);
        md.M = D.sigma * A * J1 * J1 * (md.m === 0 ? 1 : 0.5);
      } else {
        // a smooth random radial profile, vanishing at the rim, ~unit rms
        for (let q = 0; q <= 64; q++) { const r = q / 64; this.shape[k * 65 + q] = Math.sin(Math.PI * (1 - r) * (1 + 3 * md.rshape)) * Math.SQRT2 * Math.sqrt(1 - r * r * 0.5); }
        md.M = (D.sigma * A) / (2 * Math.PI * Math.max(1, md.j / Math.PI)); // ~ sigma A / j : large-j membrane modal mass
      }
      const w = TAU * md.f;
      B.eW[k] = md.j * md.j * md.M * 1e3; // stretch energy weight (k^2 M), scaled
      this.eM[k] = md.M * w * w;          // modal energy weight (M w^2)
      // radiation to the listener: velocity^gamma-ish tilt, monopole vs dipole
      const ka = (w / 343) * D.a;
      const radK = md.m === 0 && !md.dense ? md.rad : md.rad * (0.35 + 0.65 * (ka * ka) / (1 + ka * ka));
      B.gOut[k] = radK * Math.pow(md.f / 200, D.rad.gamma);
      // resonant-head participation: wires (psi) and sympathetic forcing (gSym)
      B.psi[k] = md.pr;
      B.gSym[k] = (this.dt * md.pr) / (md.M * w);
    }
    // the air between the drums couples at low frequency only: the sympathetic force drives modes below symHz
    const symHz = D.symHz ?? SYM_HZ; // (the snare takes it on every mode: its wires feel the whole head's motion)
    this.nSym = 0; while (this.nSym < list.length && list[this.nSym].f < symHz) this.nSym++;
    // the modes above symHz still respond to that (low-frequency) force, but quasi-statically, as springs: their
    // summed static displacement at the wire line is one gain (snare-side head deflection that rattles the wires)
    this.Cqs = 0; for (let k = this.nSym; k < list.length; k++) { this.Cqs += (B.psi[k] * B.gSym[k]) / (this.dt * TAU * list[k].f); B.gSym[k] = 0; }
    this.ffSym = B.ff(B.gSym);
    this.injAl = 1 - Math.exp((-TAU * INJ_LP) / sr); this.injC = new Float32Array(B.cap); for (let k = 0; k < list.length; k++) this.injC[k] = injComp(B.w0[k], sr);
    // ---- body: shell + hoop ----
    // shell+hoop modal mass ~ 0.03 kg; stick modal mass ~ 0.012 kg; both radiate from velocity
    this.body = new Bank(D.body.length); this.gBodyH = new Float32Array(this.body.cap); this.gBodyR = new Float32Array(this.body.cap);
    D.body.forEach((b, i) => { const f = b.f * (1 + 0.01 * rng.bip()); this.body.setMode(i, f, b.t60, sr); this.body.gOut[i] = b.g * (f / 200); const base = this.dt / (0.03 * TAU * f); this.gBodyH[i] = base * (0.15 * b.hoop + 0.85); this.gBodyR[i] = base * (0.85 * b.hoop + 0.15); });
    this.ffBodyH = this.body.ff(this.gBodyH); this.ffBodyR = this.body.ff(this.gBodyR);
    this.stickB = new Bank(STICK_MODES.length);
    STICK_MODES.forEach((s, i) => { const f = s.f * (1 + 0.02 * rng.bip()); this.stickB.setMode(i, f, s.t60, sr); this.stickB.gOut[i] = s.g * (f / 200); this.stickB.gIn[i] = this.dt / (0.012 * TAU * f); });
    this.ffStick = this.stickB.ff(this.stickB.gIn);
    // ---- contact slots (left/right stick) and a pending stroke ----
    this.nSlots = D.slots || 1;
    this.slots = [new Slot(B.cap), new Slot(B.cap)];
    this.c = this.slots[0]; // (alias kept for tools)
    this.pending = { on: false, V: 0, r: 0, zone: 'head', impl: 'tip' };
    this.Gx = 0; this.sepW = 1; // cross compliance between the two slots (m per N within one step)
    this.sig = 0; // relative tension rise under the stick(s), this sample
    // ---- per-sample scratch ----
    for (const k of ['Fh', 'F0', 'F1', 'FbH', 'FbR', 'Fs', 'Fst', 'Nz', 'tmpH', 'tmpB', 'tmpS', 'tmpW', 'yw']) this[k] = new Float32Array(MAX_BLOCK);
    this.ZERO = new Float32Array(MAX_BLOCK); this.ZG = new Float32Array(B.cap);
    this.sym = null; this.symOn = false; // Float32Array set by the kit (sympathetic force), and whether it matters this segment
    this.rimF = new Float32Array(64); this.rimS = new Float32Array(64); this.rimR = 0; this.rimLeft = 0; // rim-pulse ring (body / stick)
    this.glideS = 1; this.kappa = 0; this.idle = true; this.quietSubs = 0; this.drain = 1; this.injOn = false; this.ffN = 0;
    this.gHead = D.out.head; this.gBody = D.out.body; this.gStick = D.out.stick; this.gW = D.out.wires || 0; this.level = 1;
    { const nd = list.length - nMembrane; this.share = new Float32Array(Math.max(1, nd)); let t = 0; for (let k = 0; k < nd; k++) { this.share[k] = 1 / list[nMembrane + k].f; t += this.share[k]; } for (let k = 0; k < nd; k++) this.share[k] /= t; }
    if (D.wires) this.wires = new Wires(sr, seed ^ 0x2545f491);
    this.calibrate();
  }

  // Strike-point displacement weights and input gains for (r, theta) into slot S.
  setStrike(S, r, theta, pbScale = 1) {
    const B = this.head, L = this.modes, q = Math.min(64, Math.max(0, r * 64)), q0 = Math.floor(q), fr = q - q0, q1 = Math.min(64, q0 + 1);
    for (let k = 0; k < L.length; k++) {
      const md = L[k];
      const radial = (1 - fr) * this.shape[k * 65 + q0] + fr * this.shape[k * 65 + q1];
      const ang = md.m === 0 && !md.dense ? 1 : md.dense ? md.sg * (0.75 + 0.25 * Math.cos(md.m * theta + md.ph)) : md.orient ? Math.sin(md.m * theta) : Math.cos(md.m * theta);
      const phi = md.pb * pbScale * radial * ang;
      S.phi[k] = md.dense ? 0 : phi;
      S.gIn[k] = ((this.dt * phi) / (md.M * TAU * md.f)) * (md.dense ? this.D.dense.drive : 1);
    }
  }
  // displacement at the strike point of slot S now (and the one-step prediction without new force)
  ysOf(S) { const B = this.head; let s = 0; for (let k = 0; k < this.nMembrane; k++) s += S.phi[k] * B.zi[k]; return s; }
  ys() { return this.ysOf(this.slots[0]); }
  predict(S) { // next-step displacement at S with no new force (incl. the stiffening term)
    const { zr, zi, cr, ci, w0 } = this.head, phi = S.phi; let a = 0, b = 0;
    for (let k = 0; k < this.nMembrane; k++) { a += phi[k] * (ci[k] * zr[k] + cr[k] * zi[k]); b += phi[k] * ci[k] * w0[k] * zi[k]; }
    return a - this.sig * b;
  }

  // A strike. V m/s; r 0 centre .. 1 rim; zone 'head' | 'rim' (rimshot) | 'xstick' (cross-stick); impl key.
  strike(V, r, zone, impl = 'tip') {
    // choose a slot: a free one, else one whose stick is only in flight (left the head), else wait (pending)
    let S = null;
    for (let s = 0; s < this.nSlots; s++) if (!this.slots[s].on) { S = this.slots[s]; break; }
    if (!S) for (let s = 0; s < this.nSlots; s++) if (!this.slots[s].touch) { S = this.slots[s]; break; }
    if (!S) { // both sticks are on the head: this stroke starts the moment one leaves (<= a few ms)
      const P = this.pending;
      if (P.on) { P.V = Math.sqrt(P.V * P.V + V * V); P.r = r; P.zone = zone; P.impl = impl; } // two waiting strokes add their energy
      else { P.on = true; P.V = V; P.r = r; P.zone = zone; P.impl = impl; }
      this.idle = false; this.quietSubs = 0;
      return;
    }
    this.startStroke(S, V, r, zone, impl);
  }
  startStroke(S, V, r, zone, impl) {
    const D = this.D, I = IMPLEMENTS[impl] || IMPLEMENTS.tip, R = this.rng;
    const theta = R.next() * TAU;
    const rr = Math.min(0.97, Math.max(0, r + 0.02 * R.bip()));
    const Vj = V * (1 + 0.03 * R.bip());
    const st = this.name === 'kick' ? { m: D.stick.m, K: D.stick.K, alpha: D.stick.alpha, lam: D.stick.lam } : { m: D.stick.m * I.m, K: D.stick.K * I.K, alpha: I.alpha, lam: D.stick.lam * I.lam };
    let headShare = 1, rimJ = 0, rimL = 0;
    if (zone === 'rim') { headShare = 0.6; rimJ = 0.4 * st.m * Vj * 1.7 * (0.016 / D.stick.m); rimL = Math.max(3, Math.round(0.0003 * this.sr)); }
    if (zone === 'xstick') { headShare = 0.2; rimJ = 0.25 * 0.03 * 0.6 * Vj * 1.6; rimL = Math.max(4, Math.round(0.00035 * this.sr)); }
    const rHead = zone === 'rim' ? 0.92 : zone === 'xstick' ? 0.35 : rr;
    this.setStrike(S, rHead, theta); this.head.act.fill(1);
    S.on = true; S.touch = true; S.started = false; S.t = 0; S.fl = 0; S.F = 0;
    S.m = st.m * headShare; S.K = st.K * (1 + 0.08 * R.bip()); S.alpha = st.alpha; S.lam = st.lam;
    S.xs = this.ysOf(S); S.vs = Vj * (zone === 'xstick' ? 0.25 : 1); S.dPrev = 0; S.xw = 0;
    S.Z = this.Zloc * (this.name === 'kick' ? 1 : I.zr); S.tauW = S.Z * this.Cres; S.lk = Math.exp(-this.dt / S.tauW); S.Cw = (S.tauW / S.Z) * (1 - S.lk);
    { const B = this.head; let G = 0; for (let k = 0; k < this.nMembrane; k++) G += S.phi[k] * S.gIn[k] * B.ci[k]; S.G = G; }
    S.GffL = this.head.ff(S.gIn, 0, this.nMembrane); S.GffD = this.head.ff(S.gIn, this.nMembrane, this.head.n);
    S.ysP = this.predict(S);
    { // cross compliance with the other stick (if it is on the head)
      const O = this.slots[0] === S ? this.slots[1] : this.slots[0]; const B = this.head; let g = 0;
      if (O.on) for (let k = 0; k < this.nMembrane; k++) g += S.phi[k] * O.gIn[k] * B.ci[k];
      this.Gx = g;
      // how separate the two cones are: 1 - |normalised cross compliance| (0 = the same spot, 1 = unrelated)
      this.sepW = O.on && S.G > 0 && O.G > 0 ? Math.max(0, 1 - Math.abs(g) / Math.sqrt(S.G * O.G)) : 1;
    }
    S.stickGain = (this.name === 'kick' ? 0 : I.stick) * (zone === 'xstick' ? 0.28 : zone === 'rim' ? 0.13 : 1) * (0.8 + 0.4 * R.next());
    S.bodyDrive = D.bodyFromHead * (1 + 6 * Math.pow(rHead, 4)); S.rimZone = zone !== 'head';
    if (rimL > 0) { // rim pulse (half-sine) into the hoop/body and the stick, added into a small ring
      for (let i = 0; i < rimL; i++) { const F = (rimJ / (rimL * this.dt)) * (Math.PI / 2) * Math.sin((Math.PI * (i + 0.5)) / rimL); const j = (this.rimR + i) & 63; this.rimF[j] += F; this.rimS[j] += F * S.stickGain; }
      this.rimLeft = Math.max(this.rimLeft, rimL);
    }
    this.idle = false; this.quietSubs = 0;
    if (zone === 'xstick' && this.wires) this.wires.kick(0.5);
  }

  calibrate() { // kappa for the tension glide: E right after the hardest centre stroke -> D.glide (and the nl reference)
    this.kappa = 0; this.EmRef = 0;
    const tmp = new Float32Array(SUB);
    this.strike(this.name === 'kick' ? BEATER_V(1) : STICK_V(1), this.name === 'kick' ? 0.12 : 0.02, 'head', 'tip'); // the drum's own hardest stroke
    let Emax = 0, Em = 0;
    // free vibration only (after the stick has left): that is the amplitude whose stretch raises the tension
    for (let s = 0; s < 60; s++) {
      tmp.fill(0); this.renderSeg(tmp, 0, SUB);
      Em = Math.max(Em, this.head.energy(this.eM, 0, this.nMembrane)); // nl reference: whole stroke
      if (!this.slots[0].touch) Emax = Math.max(Emax, this.head.energy(this.head.eW, 0, this.nLow)); // glide: free vibration
    }
    this.kappa = Emax > 0 ? (Math.pow(1 + this.D.glide, 2) - 1) / Emax : 0;
    this.EmRef = Em || 1;
    this.reset();
  }
  reset() {
    this.head.clear(); this.body.clear(); this.stickB.clear(); for (const S of this.slots) S.reset(); this.pending.on = false;
    this.idle = true; this.glideS = 1; this.head.glide(1); this.drain = 1; this.head.poles(1); this.injOn = false; this.ffN = 0; this.sig = 0;
    this.rimF.fill(0); this.rimS.fill(0); this.rimLeft = 0; if (this.wires) this.wires.reset();
  }
  anyOn() { return this.slots[0].on || this.slots[1].on || this.pending.on; }

  // Render [i0, i1) (within one control sub-block) into out (adds).
  renderSeg(out, i0, i1) {
    let i = i0;
    while (i < i1) {
      i = this.renderPart(out, i, i1);
      if (this.startPendingNow) { this.startPendingNow = false; const P = this.pending; P.on = false; let S = this.slots[0].touch ? this.slots[1] : this.slots[0]; if (this.nSlots === 1) S = this.slots[0]; this.startStroke(S, P.V, P.r, P.zone, P.impl); }
    }
  }
  renderPart(out, i0, i1) {
    const B = this.head, nM = this.nMembrane, n = B.n;
    const H = this.tmpH, Bo = this.tmpB, St = this.tmpS, W = this.yw, F0 = this.F0, F1 = this.F1, FbH = this.FbH, FbR = this.FbR, Fs = this.Fs, Fst = this.Fst, Nz = this.Nz;
    for (let i = i0; i < i1; i++) { H[i] = 0; Bo[i] = 0; St[i] = 0; W[i] = 0; F0[i] = 0; F1[i] = 0; FbH[i] = 0; FbR[i] = 0; Fs[i] = 0; Fst[i] = 0; }
    const sym = this.symOn ? this.sym : null, aux = this.wires ? W : null;
    if (sym) B.act.fill(1, 0, this.nSym);
    // --- 1. listed modes: the contact (sample-outer) while a stick is on, then mode-outer ---
    let iE = i0, end = i1;
    const hadContact = this.slots[0].on || this.slots[1].on;
    if (hadContact) { iE = this.contactLoop(i0, i1); if (this.startPendingNow) end = iE; }
    if (iE < end) {
      if (sym) { const ns = Math.min(this.nSym, nM); ring1(B, 0, ns, iE, end, H, sym, B.gSym, aux); for (let i = iE; i < end; i++) H[i] += this.ffSymL * sym[i]; ring0(B, ns, nM, iE, end, H, aux); }
      else ring0(B, 0, nM, iE, end, H, aux);
    }
    if (sym && aux) for (let i = i0; i < end; i++) W[i] += this.Cqs * sym[i];
    // --- 2. dense modes (no contact feedback: they are the residual); stick force, injection ---
    if (this.injOn) this.noise.fillLP(Nz, i0, end, this.injAl);
    const dsym = sym && this.nSym > nM ? sym : null; // (the snare: the dense field takes the air force too)
    if (hadContact) {
      const S0 = this.slots[0], S1 = this.slots[1];
      ring4(B, nM, n, i0, end, H, F0, S0.gIn, F1, S1.gIn, this.injOn ? Nz : this.ZERO, B.gN, dsym || this.ZERO, dsym ? B.gSym : this.ZG, aux);
      for (let i = i0; i < end; i++) H[i] += S0.GffD * F0[i] + S1.GffD * F1[i];
    } else if (this.injOn && dsym) ring2(B, nM, n, i0, end, H, Nz, B.gN, dsym, B.gSym, aux);
    else if (this.injOn) ring1(B, nM, n, i0, end, H, Nz, B.gN, aux);
    else if (dsym) ring1(B, nM, n, i0, end, H, dsym, B.gSym, aux);
    else ring0(B, nM, n, i0, end, H, aux);
    if (this.injOn) for (let i = i0; i < end; i++) H[i] += this.ffN * Nz[i];
    if (dsym) for (let i = i0; i < end; i++) H[i] += this.ffSymD * dsym[i];
    // --- 3. rim pulse ring into the body (hoop) and the stick ---
    if (this.rimLeft > 0) for (let i = i0; i < end && this.rimLeft > 0; i++) { const j = this.rimR; FbR[i] += this.rimF[j]; Fs[i] += this.rimF[j]; Fst[i] += this.rimS[j]; this.rimF[j] = 0; this.rimS[j] = 0; this.rimR = (j + 1) & 63; this.rimLeft--; }
    // --- 4. body (shell + hoop) and stick modes, both DC-free ---
    ring2(this.body, 0, this.body.n, i0, end, Bo, FbH, this.gBodyH, FbR, this.gBodyR, null);
    for (let i = i0; i < end; i++) Bo[i] += this.ffBodyH * FbH[i] + this.ffBodyR * FbR[i];
    ring1(this.stickB, 0, this.stickB.n, i0, end, St, Fst, this.stickB.gIn, null);
    for (let i = i0; i < end; i++) St[i] += this.ffStick * Fst[i];
    // --- 5. wires ---
    if (this.wires) this.wires.run(i0, end, W, this.tmpW);
    const gH = this.gHead * this.level, gB = this.gBody * this.level, gSt = this.gStick * this.level, gW = this.gW * this.level;
    for (let j = i0; j < end; j++) { out[j] += gH * H[j] + gB * Bo[j] + gSt * St[j] + (this.wires ? gW * this.tmpW[j] : 0); this.Fh[j] = F0[j] + F1[j]; Fs[j] += F0[j] + F1[j]; }
    return end;
  }

  // The stick-head contact, sample-outer, over the listed modes (the dense ones have phi = 0: they are part of
  // the residual and are driven afterwards with the same forces). Returns the first sample it did not process.
  contactLoop(i0, i1) {
    const B = this.head, { zr, zi, cr, ci, gOut, psi, gSym, w0 } = B, nM = this.nMembrane, dt = this.dt;
    const S0 = this.slots[0], S1 = this.slots[1], sym = this.symOn ? this.sym : null;
    const H = this.tmpH, W = this.yw, F0a = this.F0, F1a = this.F1, FbH = this.FbH, FbR = this.FbR, Fst = this.Fst;
    const maxT = Math.round(CONTACT_MAX * this.sr), flMax = Math.round(FLIGHT * this.sr), y2i = 1 / (this.y2 * this.y2);
    let i = i0;
    for (; i < i1; i++) {
      if (!S0.on && !S1.on) break;
      // --- predictions and the force solve (one or two sticks) ---
      let d00 = -1, d01 = -1, C00 = 0, C11 = 0, Cw0 = 0, Cw1 = 0, xwP0 = 0, xwP1 = 0, xsP0 = 0, xsP1 = 0, lk0 = 0, lk1 = 0;
      if (S0.on) { lk0 = S0.lk; Cw0 = S0.Cw; xwP0 = S0.xw * lk0; xsP0 = S0.xs + S0.vs * dt; d00 = xsP0 - S0.ysP - xwP0; C00 = (dt * dt) / S0.m + S0.G + Cw0; }
      if (S1.on) { lk1 = S1.lk; Cw1 = S1.Cw; xwP1 = S1.xw * lk1; xsP1 = S1.xs + S1.vs * dt; d01 = xsP1 - S1.ysP - xwP1; C11 = (dt * dt) / S1.m + S1.G + Cw1; }
      let F0 = 0, F1 = 0;
      const t0 = S0.on && d00 > 0, t1 = S1.on && d01 > 0;
      const KD0 = t0 ? S0.K * Math.max(0, 1 + S0.lam * ((d00 - S0.dPrev) / dt)) : 0, KD1 = t1 ? S1.K * Math.max(0, 1 + S1.lam * ((d01 - S1.dPrev) / dt)) : 0;
      const Cx = t0 && t1 ? this.Gx : 0;
      for (let it = 0; it < 6 && (t0 || t1); it++) { // Newton on g_s(F) = F_s - KD_s d_s^alpha, d_s = d0_s - C_ss F_s - Cx F_t
        let g0 = 0, g1 = 0, j00 = 1, j01 = 0, j10 = 0, j11 = 1;
        if (t0) { const d = Math.max(0, d00 - C00 * F0 - Cx * F1), al = S0.alpha, pw = al === 1.5 ? Math.sqrt(d) : al === 2 ? d : d * Math.sqrt(d); g0 = F0 - KD0 * d * pw; const dd = KD0 * al * pw; j00 = 1 + dd * C00; j01 = dd * Cx; }
        if (t1) { const d = Math.max(0, d01 - C11 * F1 - Cx * F0), al = S1.alpha, pw = al === 1.5 ? Math.sqrt(d) : al === 2 ? d : d * Math.sqrt(d); g1 = F1 - KD1 * d * pw; const dd = KD1 * al * pw; j11 = 1 + dd * C11; j10 = dd * Cx; }
        const det = j00 * j11 - j01 * j10;
        const n0 = t0 ? F0 - (j11 * g0 - j01 * g1) / det : 0, n1 = t1 ? F1 - (j00 * g1 - j10 * g0) / det : 0;
        F0 = t0 ? Math.min(d00 / C00, Math.max(0, n0)) : 0; F1 = t1 ? Math.min(d01 / C11, Math.max(0, n1)) : 0;
      }
      // --- stick states ---
      if (S0.on) this.advance(S0, F0, d00 - C00 * F0 - Cx * F1, xsP0, xwP0, Cw0, t0, maxT, flMax);
      if (S1.on) this.advance(S1, F1, d01 - C11 * F1 - Cx * F0, xsP1, xwP1, Cw1, t1, maxT, flMax);
      // --- the fused mode pass: update, output, wires, and the next-step predictions at both tips ---
      const uu = sym ? sym[i] : 0, sg = this.sig;
      let o = 0, w = 0, a0 = 0, b0 = 0, a1 = 0, b1 = 0;
      const p0 = S0.phi, p1 = S1.phi, g0 = S0.gIn, g1 = S1.gIn;
      if (S1.on || F1 !== 0) {
        for (let k = 0; k < nM; k++) {
          const b = zi[k], a = zr[k], x = a + g0[k] * F0 + g1[k] * F1 + gSym[k] * uu - w0[k] * sg * b;
          o += gOut[k] * a; w += psi[k] * b;
          const c = cr[k], s = ci[k], na = c * x - s * b, nb = s * x + c * b;
          zr[k] = na; zi[k] = nb;
          const pr = s * na + c * nb, sb = s * w0[k] * nb;
          a0 += p0[k] * pr; b0 += p0[k] * sb; a1 += p1[k] * pr; b1 += p1[k] * sb;
        }
      } else {
        for (let k = 0; k < nM; k++) {
          const b = zi[k], a = zr[k], x = a + g0[k] * F0 + gSym[k] * uu - w0[k] * sg * b;
          o += gOut[k] * a; w += psi[k] * b;
          const c = cr[k], s = ci[k], na = c * x - s * b, nb = s * x + c * b;
          zr[k] = na; zi[k] = nb;
          a0 += p0[k] * (s * na + c * nb); b0 += p0[k] * s * w0[k] * nb;
        }
      }
      H[i] += o + S0.GffL * F0 + S1.GffL * F1 + this.ffSymL * uu; W[i] = w;
      F0a[i] = F0; F1a[i] = F1;
      const fb0 = F0 * S0.bodyDrive, fb1 = F1 * S1.bodyDrive;
      if (S0.rimZone) FbR[i] += fb0; else FbH[i] += fb0;
      if (S1.rimZone) FbR[i] += fb1; else FbH[i] += fb1;
      Fst[i] += F0 * S0.stickGain + F1 * S1.stickGain;
      // --- the head stiffens under the stick(s): relative tension rise from the cone deflection(s) ---
      // (two sticks close together make one cone, far apart two: the larger cone + half the smaller)
      let ya = 0, yb = 0;
      if (t0) { const y = S0.ysP + S0.G * F0 + Cx * F1; ya = y * y; }
      if (t1) { const y = S1.ysP + S1.G * F1 + Cx * F0; yb = y * y; }
      this.sig = Math.min(6, (Math.max(ya, yb) + this.sepW * Math.min(ya, yb)) * y2i);
      S0.ysP = a0 - this.sig * b0; S1.ysP = a1 - this.sig * b1;
      if (this.pending.on && (!S0.touch || (this.nSlots > 1 && !S1.touch))) { this.startPendingNow = true; return i + 1; }
    }
    if (!S0.on && !S1.on) this.sig = 0;
    return i;
  }
  advance(S, F, d, xsP, xwP, Cw, touching, maxT, flMax) {
    const dt = this.dt;
    S.xw = xwP + Cw * F;
    S.vs -= (F / S.m) * dt; S.xs = xsP - (F * dt * dt) / S.m; S.dPrev = d; S.t++; S.F = F;
    if (touching) { S.started = true; S.touch = true; S.fl = 0; }
    else if (S.started) { S.touch = false; if (S.vs < 0 && ++S.fl > flMax) S.on = false; } // flying away; may re-touch
    if (S.t > maxT) { S.on = false; S.touch = false; }
  }

  control(glideOnly = false) { // once per sub-block
    const B = this.head, nM = this.nMembrane;
    const contact = this.slots[0].on || this.slots[1].on;
    let polesDirty = false;
    if (this.kappa > 0 && !contact) { // tension modulation, from the free vibration of the low modes
      const E = B.energy(B.eW, 0, this.nLow);
      const s = Math.min(1 + 1.2 * this.D.glide, Math.sqrt(1 + this.kappa * E));
      if (Math.abs(s - this.glideS) > 2e-6) { this.glideS = s; B.glide(s); polesDirty = true; }
    }
    if (glideOnly) { if (polesDirty) B.poles(1); return; }
    // large-amplitude coupling: energy flows from the listed head modes into the dense field, smoothly over
    // the next sub-block: the listed modes get a little extra damping, the dense modes a stochastic force
    const nl = this.D.nl;
    let drain = 1; this.injOn = false; B.gN.fill(0, nM, B.n);
    if (nl && this.EmRef > 0 && !contact) {
      const E = B.energy(this.eM, 0, nM) / this.EmRef;
      if (E > 1e-9) {
        const frac = Math.min(0.3, nl.kappa * Math.pow(E, nl.nu) * (SUB / this.sr));
        if (frac > 1e-7) {
          drain = Math.pow(1 - frac, 1 / (2 * SUB));
          const dE = frac * E * this.EmRef * nl.eff, den = SUB * NOISE_VAR;
          for (let k = nM; k < B.n; k++) { B.gN[k] = Math.sqrt((dE * this.share[k - nM]) / (this.eM[k] * den)) * this.injC[k]; B.act[k] = 1; }
          this.ffN = B.ff(B.gN, nM, B.n); this.injOn = true;
        }
      }
    }
    if (polesDirty || drain !== this.drain) { B.poles(drain, 0, nM); if (polesDirty) B.poles(1, nM, B.n); this.drain = drain; }
    B.flush(); this.body.flush(); this.stickB.flush();
    if (!contact) B.cull(this.symOn ? this.nSym : 0, B.n, this.cullFloor);
    if (!this.anyOn()) { // idle detection: output power estimate below -110 dB re a full hit
      const e = B.energy(B.gOut);
      if (e < 1e-22 && this.rimLeft === 0 && (!this.wires || this.wires.quiet())) { if (++this.quietSubs > 8) { this.reset(); } } else this.quietSubs = 0;
    }
  }
  get cullFloor() { const g = this.level * this.gHead; return CULL / (g * g + 1e-30); }
  get ffSymL() { return this._ffSymL ?? (this._ffSymL = this.head.ff(this.head.gSym, 0, this.nMembrane)); }
  get ffSymD() { return this._ffSymD ?? (this._ffSymD = this.head.ff(this.head.gSym, this.nMembrane, this.head.n)); }
}

// ---------------- snare wires ----------------
// Four wire groups, each a lumped mass on a spring (strainer tension) in one-sided contact with the
// snare-side head. Collisions (changes of the contact force) times strand noise drive a fixed wire
// resonance chain (hp 1.3 k, peaks 3.9 k / 6.6 k, lp 10 k), plus three short coil resonances.
class Wires {
  constructor(sr, seed) {
    this.sr = sr; this.dt = 1 / sr; this.noise = new Noise(seed);
    this.G = 4;
    this.fw = new Float32Array([150, 172, 196, 223]); this.pre = 4e-6; this.zeta = 0.12; this.wc = TAU * 2600;
    this.w = new Float32Array(4); this.v = new Float32Array(4); this.aPrev = new Float32Array(4);
    this.hp1 = new Biquad().set('hp', 1000, 0.7, sr); this.hp2 = new Biquad().set('hp', 900, 0.6, sr);
    this.pk1 = new Biquad().set('peak', 2900, 0.9, sr, 4); this.pk2 = new Biquad().set('peak', 5400, 1.2, sr, 3);
    this.lp = new Biquad().set('lp', 9500, 0.6, sr);
    this.coil = [new Biquad().set('bp', 4400, 5, sr), new Biquad().set('bp', 5900, 5, sr), new Biquad().set('bp', 7700, 5, sr)];
    this.env = 0; this.kickAmt = 0; this.lin = 0.06; this.hPrev = 0; this.vPrev = 0; this.sm = 0; this.aSm = 1 - Math.exp(-1 / (0.0003 * sr));
  }
  reset() { this.w.fill(0); this.v.fill(0); this.aPrev.fill(0); this.env = 0; this.sm = 0; for (const f of [this.hp1, this.hp2, this.pk1, this.pk2, this.lp, ...this.coil]) f.clear(); this.hPrev = this.vPrev = 0; }
  kick(a) { this.kickAmt = a; }
  quiet() { return this.env < 1e-9; }
  // h: snare-head displacement at the wire line (m, positive = away from the batter, toward the wires)
  run(i0, i1, h, out) {
    const dt = this.dt, wc2 = this.wc * this.wc;
    for (let i = i0; i < i1; i++) {
      const hh = h[i];
      let ex = 0;
      for (let g = 0; g < this.G; g++) {
        const ww = TAU * this.fw[g];
        let w = this.w[g], v = this.v[g];
        // the strainer pulls the wire toward -pre (into the head); the head pushes it back (one-sided)
        let ac = 0; const pen = hh - w; if (pen > 0) ac = wc2 * pen;
        const a = -ww * ww * (w + this.pre) - 2 * this.zeta * ww * v + ac;
        v += a * dt; w += v * dt;
        this.w[g] = w; this.v[g] = v;
        const dA = Math.abs(ac - this.aPrev[g]); this.aPrev[g] = ac;
        ex += dA;
      }
      // strand micro-rattle: linear in the head's acceleration (always present, sensitive snares)
      const vh = (hh - this.hPrev) / dt, ah = (vh - this.vPrev) / dt; this.hPrev = hh; this.vPrev = vh;
      const drive = ex * 2.5e-6 + Math.abs(ah) * this.lin * 1e-3;
      this.sm += (drive - this.sm) * this.aSm;              // 0.3 ms smoothing: no single-sample spikes
      this.env = Math.max(this.sm, this.env * 0.9995);
      const n = this.noise.next() * this.sm; // linear: the collisions' own threshold gives the level dependence
      let y = this.hp2.tick(this.hp1.tick(n));
      y = this.lp.tick(this.pk2.tick(this.pk1.tick(y)));
      const c = this.coil[0].tick(n) + this.coil[1].tick(n) + this.coil[2].tick(n);
      out[i] = y + 0.5 * c;
    }
  }
}

// ======================================================================================
// CYMBALS
// ======================================================================================
// Plate modes: nDome modes from fLo to fKnee with spacing widening as u^dExp (the dome / low (m,0) modes),
// then a CONSTANT density `dens` (modes/Hz, a flat plate's density is constant) up to fTop, then noise bands
// (modes too dense to list) up to 19.5 kHz, fed by the strike and by the cascade. The hat keeps the v1 layout
// (n modes fLo..fHi as u^dExp; its tail was already noise-like enough).
export const CYMBALS = {
  crash: { // 18" thin crash
    R: 0.2286, h: 0.0008, nDome: 36, fLo: 165, fKnee: 1000, dExp: 1.35, dens: 0.06, fTop: 3000, rb: 0.17,
    t60: { at: 3.6, ref: 300, exp: 0.28, min: 0.35 },
    edges: [0, 420, 900, 1500, 2200, 1e9], // explicit bands (the last ends at fTop)
    noise: [{ lo: 3000, hi: 3800 }, { lo: 3800, hi: 4800 }, { lo: 4800, hi: 6000 }, { lo: 6000, hi: 7500 }, { lo: 7500, hi: 9400 }, { lo: 9400, hi: 12000 }, { lo: 12000, hi: 15500 }, { lo: 15500, hi: 19500 }],
    noiseDens: 0.06,
    bell: { f: 1050, ratios: [1, 2.06, 3.21, 4.42, 5.7, 7.02], t60: [3.2, 2.6, 2.1, 1.7, 1.4, 1.1], g: 1.6 }, // (v1 0.8: the bell stroke sat 9 dB under the old kit's)
    cascade: { kappa: 20, nu: 0.42, up1: 0.66, up2: 0.14, kappaN: 8, upN: 0.85 },
    gamma: 2.0, gammaLow: 2.5, Z: 50, out: 1.0, stickOut: 0.05,
  },
  ride: { // 20" medium ride
    R: 0.254, h: 0.0012, nDome: 44, fLo: 210, fKnee: 1100, dExp: 1.3, dens: 0.06, fTop: 3400, rb: 0.2,
    t60: { at: 6.0, ref: 300, exp: 0.3, min: 0.45 },
    edges: [0, 450, 950, 1600, 2400, 1e9],
    noise: [{ lo: 3400, hi: 4300 }, { lo: 4300, hi: 5400 }, { lo: 5400, hi: 6800 }, { lo: 6800, hi: 8600 }, { lo: 8600, hi: 11000 }, { lo: 11000, hi: 14500 }, { lo: 14500, hi: 19500 }],
    noiseDens: 0.06,
    bell: { f: 920, ratios: [1, 2.03, 3.17, 4.35, 5.62, 6.98, 8.4], t60: [6.5, 5.2, 4.2, 3.4, 2.8, 2.3, 1.9], g: 2.2 }, // (v1 1.0: a ride bell must cut through)
    cascade: { kappa: 6, nu: 0.42, up1: 0.66, up2: 0.14, kappaN: 2.4, upN: 0.85 },
    gamma: 1.8, gammaLow: 2.3, Z: 85, out: 1.0, stickOut: 0.05,
  },
  hat: { // 14" hi-hats (top plate modelled; the bottom plate adds damping and the clash)
    R: 0.178, h: 0.0009, nDome: 28, fLo: 380, fKnee: 1200, dExp: 1.3, dens: 0.05, fTop: 4000, rb: 0.16,
    t60: { at: 2.2, ref: 500, exp: 0.2, min: 0.25 }, // open (v2: a flatter decay, the open hat's top end sustains)
    t60Closed: { at: 0.20, ref: 500, exp: 0.5, min: 0.03 },
    edges: [0, 900, 1800, 2800, 1e9],
    noise: [{ lo: 4000, hi: 5000 }, { lo: 5000, hi: 6300 }, { lo: 6300, hi: 8000 }, { lo: 8000, hi: 10000 }, { lo: 10000, hi: 12500 }, { lo: 12500, hi: 15500 }, { lo: 15500, hi: 19500 }],
    noiseDens: 0.05,
    bell: { f: 980, ratios: [1, 2.1, 3.3, 4.6], t60: [1.2, 1.0, 0.8, 0.6], g: 0.5 },
    cascade: { kappa: 12, nu: 0.5, up1: 0.62, up2: 0.15, kappaN: 6, upN: 0.85 },
    gamma: 2.15, Z: 60, out: 1.0, stickOut: 0.05,
    gapOpen: 0.012, clash: 400, clashTo: [[1, 0.2], [2, 0.3], [3, 0.3], [4, 0.15]], // [noise band, share]: the sizzle lands at 5-12.5 kHz
  },
};
const BELL_MASS = 0.4; // the bell modes move the small, thick bell: modal mass 0.4 x the plate's (v2: v1's bell strokes sat 4-9 dB under the old kit's)
const t60Law = (L, f) => Math.max(L.min, L.at * Math.pow(f / L.ref, -L.exp));

class Cymbal {
  constructor(name, sr, seed) {
    const C = CYMBALS[name];
    this.name = name; this.C = C; this.sr = sr; this.dt = 1 / sr;
    const rng = new Rng(seed);
    this.rng = new Rng(seed ^ 0x68e31da4); this.noise = new Noise(seed * 2654435761 + 1); this.injNoise = new Noise(seed * 40503 + 7);
    const modes = [];
    const label = (f) => {
      const m = Math.max(2, Math.round(2 + 1.4 * Math.sqrt(f / C.fLo) * (0.8 + 0.4 * rng.next()))); // nodal diameters
      const n = rng.next() < Math.min(0.7, f / 4000) ? 1 + Math.floor(rng.next() * 3) : 0;           // nodal circles
      return { f, m, n, ph: rng.next() * TAU, a: Math.exp(0.35 * rng.bip()), bell: false };
    };
    if (C.fKnee) {
      for (let i = 0; i < C.nDome; i++) { const u = (i + 0.15 + 0.7 * rng.next()) / C.nDome; modes.push(label(C.fLo + (C.fKnee - C.fLo) * Math.pow(u, C.dExp))); }
      const N2 = Math.round(C.dens * (C.fTop - C.fKnee));
      for (let i = 0; i < N2; i++) modes.push(label(C.fKnee + ((C.fTop - C.fKnee) * (i + 0.15 + 0.7 * rng.next())) / N2));
    } else for (let i = 0; i < C.n; i++) { const u = (i + 0.15 + 0.7 * rng.next()) / C.n; modes.push(label(C.fLo + (C.fHi - C.fLo) * Math.pow(u, C.dExp))); }
    this.nPlate = modes.length;
    C.bell.ratios.forEach((q, i) => modes.push({ f: C.bell.f * q * (1 + 0.006 * rng.bip()), m: 0, n: i, ph: 0, a: 1, bell: true, t60: C.bell.t60[i] }));
    this.modes = modes;
    const B = (this.bank = new Bank(modes.length));
    this.Mmode = (8600 * C.h * Math.PI * C.R * C.R) / 4; // modal mass ~ a quarter of the plate mass
    this.t60Open = new Float32Array(modes.length); this.t60Closed = new Float32Array(modes.length); this.chokeF = new Float32Array(B.cap);
    for (let k = 0; k < modes.length; k++) {
      const md = modes[k];
      const t60 = md.bell ? md.t60 : t60Law(C.t60, md.f) * (0.85 + 0.3 * rng.next());
      this.t60Open[k] = t60;
      if (C.t60Closed) this.t60Closed[k] = t60Law(C.t60Closed, md.f);
      B.setMode(k, md.f, t60, sr);
      const w = TAU * md.f;
      B.eW[k] = w * w; // modal energy ~ M w^2 |q|^2 (M uniform)
      const gam = C.gammaLow && md.f < 1000 ? C.gammaLow : C.gamma; // a steeper tilt below 1 kHz: the dome modes radiate less
      B.gOut[k] = md.a * Math.pow(md.f / 1000, gam) * (md.bell ? C.bell.g : 1);
      this.chokeF[k] = Math.exp(-1 / (0.035 * Math.pow(md.f / 500, -0.25) * sr)); // hand on the metal: per-sample decay
    }
    // bands
    this.bandFrom = []; this.bandTo = []; this.bandOf = new Int32Array(B.cap).fill(-1);
    for (let b = 0; b + 1 < C.edges.length; b++) {
      let a = -1, z = -1;
      for (let k = 0; k < this.nPlate; k++) if (modes[k].f >= C.edges[b] && modes[k].f < C.edges[b + 1]) { if (a < 0) a = k; z = k + 1; this.bandOf[k] = b; }
      this.bandFrom.push(a < 0 ? 0 : a); this.bandTo.push(z < 0 ? 0 : z);
    }
    this.nb = this.bandFrom.length;
    // each band's own energy loss per sub-block (at its centre frequency), the reference for the cascade cut-off
    this.bandLoss = this.bandFrom.map((a, b) => { const fc = b + 1 < C.edges.length - 1 ? Math.sqrt(Math.max(C.fLo, C.edges[b]) * C.edges[b + 1]) : (C.fTop || C.fHi); return 1 - Math.exp((-13.815510557964274 * SUB) / (sr * t60Law(C.t60, fc))); });
    // noise bands: energy state, band noise filter (2 x RBJ bandpass), slow shimmer, per-sample gain ramp
    this.nz = C.noise.map((q0) => {
      const fc = Math.sqrt(q0.lo * q0.hi), Q = fc / (q0.hi - q0.lo);
      const q = { ...q0, t60: q0.t60 ?? t60Law(C.t60, fc), t60c: q0.t60c ?? (C.t60Closed ? t60Law(C.t60Closed, fc) : undefined), dens: q0.dens ?? C.noiseDens };
      const b1 = new Biquad().set('bp', fc, Q * 1.2, sr), b2 = new Biquad().set('bp', fc, Q * 1.2, sr);
      // unit-variance normalisation of uniform noise through the two bandpasses (measured at setup)
      const nn = new Noise(12345); let e = 0; for (let i = 0; i < 48000; i++) { const y = b2.tick(b1.tick(nn.next())); if (i > 2000) e += y * y; }
      b1.clear(); b2.clear();
      const norm = 1 / Math.sqrt(e / 46000);
      const Nv = q.dens * (q.hi - q.lo); // virtual mode count in the band (plate modal density)
      return { ...q, fc, w: TAU * fc, b1, b2, norm, Nv, e: 0, sh: 0, shT: 0, g: Math.pow(fc / 1000, C.gamma), amp: 0, tgt: 0 };
    });
    // stick modes (the wood tick on metal)
    this.stickB = new Bank(STICK_MODES.length); this.stickBase = new Float32Array(STICK_MODES.length);
    STICK_MODES.forEach((s, i) => { const f = s.f * (1 + 0.02 * rng.bip()); this.stickB.setMode(i, f, s.t60, sr); this.stickB.gOut[i] = s.g * (f / 200); this.stickBase[i] = this.dt / (0.012 * TAU * f); });
    // per-hit pulse ring and scratch
    this.F = new Float32Array(MAX_BLOCK); this.pulse = new Float32Array(256); this.pring = new Float32Array(512); this.pR = 0; this.pLeft = 0;
    this.tmp = new Float32Array(MAX_BLOCK); this.tmpS = new Float32Array(MAX_BLOCK); this.tmpN = new Float32Array(MAX_BLOCK); this.Nz = new Float32Array(MAX_BLOCK);
    this.Eref = 1; this.idle = true; this.quietSubs = 0; this.choke = 0; this.level = 1; this.Gff = 0; this.ffSt = 0; this.ffN = 0; this.injOn = false; this.gridLeft = SUB;
    this.open = name === 'hat' ? 0 : 1; this.openTarget = this.open; this.openApplied = -1; this.stickGain = 0;
    this.skin = { n: 0, i: 0, g: 0, lp: new Biquad().set('lp', 1100, 0.7, sr) };
    this.air = { n: 0, i: 0, g: 0, bp: new Biquad().set('bp', 1300, 0.9, sr) };
    this.injAl = 1 - Math.exp((-TAU * INJ_LP) / sr); this.injC = new Float32Array(B.cap); for (let k = 0; k < B.n; k++) this.injC[k] = injComp(B.w0[k], sr);
    this.mul = new Float32Array(this.nb + 1); this.Eb = new Float64Array(16); this.inj = new Float64Array(24);
    if (name === 'hat') this.applyOpen(0);
    this.calibrate();
  }

  // radial participation of mode k at radius r (0 centre .. 1 edge)
  shapeAt(k, r) {
    const md = this.modes[k], rb = this.C.rb;
    if (md.bell) return r < rb ? Math.pow(1 - r / rb, 0.5) : 0.12 * Math.exp(-(r - rb) * 4);
    if (r < rb) return 0.25 * (r / rb);
    const x = (r - rb) / (1 - rb);
    if (md.n === 0) return 0.25 + 0.75 * Math.pow(x, Math.min(md.m, 6) * 0.45);
    return Math.sin((md.n + 0.5) * Math.PI * x) * Math.sqrt(x) * 1.1;
  }

  // Stick on a plate: Hertz spring (wood on bronze) against the plate's resistive impedance Z.
  makePulse(V, m, K) {
    const dt = this.dt / 8, Z = this.C.Z;
    let x = 0, v = V, n = 0; // x: compression; v: stick speed; plate moves at F/Z
    const P = this.pulse; let acc = 0, sub = 0;
    for (let s = 0; s < 256 * 8; s++) {
      const F = x > 0 ? K * x * Math.sqrt(x) : 0;
      v -= (F / m) * dt; x += (v - F / Z) * dt;
      acc += F; if (++sub === 8) { P[n++] = acc / 8; acc = 0; sub = 0; if (n >= 256) break; }
      if (x <= 0 && s > 8) { if (sub) { P[n++] = acc / 8; } break; }
    }
    return n;
  }
  addPulse(n) { for (let i = 0; i < n; i++) this.pring[(this.pR + i) & 511] += this.pulse[i]; this.pLeft = Math.max(this.pLeft, n); }

  strike(V, r, zone, impl = 'tip') {
    const R = this.rng, C = this.C;
    const I = IMPLEMENTS[impl] || IMPLEMENTS.tip;
    const theta = R.next() * TAU;
    const rr = Math.min(0.99, Math.max(0, r + 0.02 * R.bip()));
    const Vj = V * (1 + 0.04 * R.bip());
    const m = 0.016 * I.m, K = 1.0e9 * (impl === 'shoulder' ? 0.35 : impl === 'finger' ? 0.002 : 1) * (1 + 0.08 * R.bip());
    const n = this.makePulse(Vj, m, K); this.addPulse(n);
    const B = this.bank;
    for (let k = 0; k < B.n; k++) {
      const md = this.modes[k];
      const ang = md.bell ? 1 : Math.cos(md.m * (theta - md.ph));
      const phi = this.shapeAt(k, rr) * ang;
      B.gIn[k] = (this.dt * phi) / ((md.bell ? this.Mmode * BELL_MASS : this.Mmode) * TAU * md.f);
    }
    this.Gff = B.ff(B.gIn); B.act.fill(1);
    // direct excitation of the noise bands (modes too dense to list): N_virtual * (dt/M)^2 |F(w)|^2 phi_rms^2
    const phiRms = rr < C.rb ? 0.15 : 0.6;
    for (const q of this.nz) {
      let re = 0, im = 0; for (let i = 0; i < n; i++) { re += this.pulse[i] * Math.cos(q.w * i * this.dt); im -= this.pulse[i] * Math.sin(q.w * i * this.dt); }
      const Fw2 = re * re + im * im;
      q.e += (q.Nv * 0.5 * Math.pow(this.dt / this.Mmode, 2) * Fw2 * phiRms * phiRms) * (1 / this.Eref);
      q.tgt = this.noiseAmp(q);
    }
    this.stickGain = I.stick * (rr < C.rb ? 1.6 : 1) * (0.8 + 0.4 * R.next());
    for (let i = 0; i < 4; i++) this.stickB.gIn[i] = this.stickBase[i] * this.stickGain;
    this.ffSt = this.stickB.ff(this.stickB.gIn);
    this.choke = 0; this.idle = false; this.quietSubs = 0;
  }
  get cullFloor() { const g = this.level * this.C.out; return CULL / (g * g + 1e-30); }
  noiseAmp(q) { return q.e > 1e-14 ? (q.g * Math.sqrt(q.e * this.Eref) / q.w) * (1 / Math.SQRT2) * q.norm * (1 + q.sh) : 0; }

  // hat pedal: plates close (chick) at speed u -> V; open/close changes damping and the gap
  pedalClose(u) {
    const V = PEDAL_V(u);
    this.openTarget = 0;
    const n = this.makePulse(V, 0.25, 2e8); this.addPulse(n);
    const B = this.bank, R = this.rng, th = R.next() * TAU;
    // the plates meet all round the rim at once: a wide contact (1.6 x a stick's coupling; v1's 0.4 left the
    // jazz "chick" on 2 and 4 14 dB under the old kit's)
    for (let k = 0; k < B.n; k++) { const md = this.modes[k]; B.gIn[k] = (this.dt * 1.6 * this.shapeAt(k, 0.97) * (md.bell ? 0.3 : Math.cos(md.m * (th - md.ph)))) / (this.Mmode * TAU * md.f); }
    this.Gff = B.ff(B.gIn); B.act.fill(1);
    for (let i = 0; i < 4; i++) this.stickB.gIn[i] = 0; this.ffSt = 0;
    for (const q of this.nz) { q.e *= 0.2; q.tgt = this.noiseAmp(q); } // the closing plates damp the top octaves at once (ramped)
    this.air = { ...this.air, n: Math.round(0.03 * this.sr), i: 0, g: 2.4e-4 * V };
    this.idle = false; this.quietSubs = 0;
  }
  setOpen(h) { this.openTarget = clamp01(h); if (this.idle) this.applyOpen(this.openTarget), (this.open = this.openTarget); }
  applyOpen(h) { // per-mode damping between closed and open (log interpolation)
    if (this.name !== 'hat' || Math.abs(h - this.openApplied) < 1e-4) return;
    const s = Math.pow(h, 0.6);
    for (let k = 0; k < this.bank.n; k++) this.bank.setT60(k, Math.exp((1 - s) * Math.log(this.t60Closed[k] || 0.05) + s * Math.log(this.t60Open[k])), this.sr);
    this.openApplied = h;
  }
  chokeNow() { this.choke = Math.round(0.3 * this.sr / SUB); this.skin = { ...this.skin, n: Math.round(0.05 * this.sr), i: 0, g: 3e-5 }; this.idle = false; this.quietSubs = 0; }

  calibrate() { // Eref: plate energy after a 10 m/s edge stroke
    this.Eref = 1;
    this.strike(10, 0.9, 'edge');
    const n = this.pLeft; for (let i = 0; i < n; i++) { this.F[i] = this.pring[(this.pR + i) & 511]; this.pring[(this.pR + i) & 511] = 0; }
    this.pLeft = 0; this.tmp.fill(0, 0, n + 8); ring1(this.bank, 0, this.bank.n, 0, n + 8, this.tmp, this.F, this.bank.gIn, null);
    this.Eref = this.bank.energy(this.bank.eW) || 1;
    for (const q of this.nz) { q.e = 0; q.tgt = 0; }
    this.reset();
  }
  reset() { this.bank.clear(); this.stickB.clear(); for (const q of this.nz) { q.e = 0; q.amp = 0; q.tgt = 0; q.b1.clear(); q.b2.clear(); } this.idle = true; this.pring.fill(0); this.pLeft = 0; this.choke = 0; this.injOn = false; this.bank.poles(1); this.F.fill(0, 0, 512); }

  renderSeg(out, i0, i1) {
    const F = this.F, T = this.tmp, S = this.tmpS, Nn = this.tmpN, Nz = this.Nz, B = this.bank;
    for (let i = i0; i < i1; i++) { F[i] = 0; T[i] = 0; S[i] = 0; Nn[i] = 0; }
    let anyF = false;
    for (let i = i0; i < i1 && this.pLeft > 0; i++) { const j = this.pR; F[i] = this.pring[j]; this.pring[j] = 0; this.pR = (j + 1) & 511; this.pLeft--; anyF = true; }
    if (this.injOn) this.injNoise.fillLP(Nz, i0, i1, this.injAl);
    if (anyF && this.injOn) ring2(B, 0, B.n, i0, i1, T, F, B.gIn, Nz, B.gN, null);
    else if (anyF) ring1(B, 0, B.n, i0, i1, T, F, B.gIn, null);
    else if (this.injOn) ring1(B, 0, B.n, i0, i1, T, Nz, B.gN, null);
    else ring0(B, 0, B.n, i0, i1, T, null);
    if (anyF) { for (let i = i0; i < i1; i++) T[i] += this.Gff * F[i]; ring1(this.stickB, 0, 4, i0, i1, S, F, this.stickB.gIn, null); for (let i = i0; i < i1; i++) S[i] += this.ffSt * F[i]; }
    else ring0(this.stickB, 0, 4, i0, i1, S, null);
    if (this.injOn) for (let i = i0; i < i1; i++) T[i] += this.ffN * Nz[i];
    // noise bands: the gain ramps per sample to its target by the end of the sub-block (no steps)
    const left = Math.max(1, this.gridLeft);
    for (const q of this.nz) {
      if (q.amp < 1e-10 && q.tgt < 1e-10) { q.amp = 0; continue; }
      const da = (q.tgt - q.amp) / left; let a = q.amp;
      for (let i = i0; i < i1; i++) { a += da; Nn[i] += a * q.b2.tick(q.b1.tick(this.noise.next())); }
      q.amp = a;
    }
    // choke skin and pedal air puff
    const sk = this.skin; for (let i = i0; i < i1 && sk.i < sk.n; i++, sk.i++) { const e = Math.exp(-sk.i / (0.012 * this.sr)) * (1 - Math.exp(-sk.i / (0.002 * this.sr))); Nn[i] += sk.g * e * sk.lp.tick(this.noise.next()); }
    const ai = this.air; for (let i = i0; i < i1 && ai.i < ai.n; i++, ai.i++) { const e = Math.exp(-ai.i / (0.006 * this.sr)) * (1 - Math.exp(-ai.i / (0.0008 * this.sr))); Nn[i] += ai.g * e * ai.bp.tick(this.noise.next()); }
    const g = this.C.out * this.level;
    for (let i = i0; i < i1; i++) out[i] += g * (T[i] + Nn[i] + this.C.stickOut * S[i]);
  }

  control() { // once per sub-block: hat openness, choke, cascade, clash, shimmer, idle -- all applied smoothly
    const C = this.C, B = this.bank, dtc = SUB / this.sr;
    if (this.name === 'hat' && this.open !== this.openTarget) {
      const step = dtc / (this.openTarget < this.open ? 0.004 : 0.05);
      this.open = this.openTarget > this.open ? Math.min(this.openTarget, this.open + step) : Math.max(this.openTarget, this.open - step);
      this.applyOpen(this.open);
    }
    // ---- energy cascade (nonlinear wash): band b drains (extra damping over the next sub-block) and its
    // energy is injected (a stochastic force over the next sub-block) into bands b+1 and b+2 ----
    const cas = C.cascade, Er = this.Eref, Eb = this.Eb, inj = this.inj, nE = this.nb, nzs = this.nz, keep = this.mul;
    for (let b = 0; b < nE; b++) Eb[b] = B.energy(B.eW, this.bandFrom[b], this.bandTo[b]) / Er;
    inj.fill(0);
    for (let b = 0; b < nE; b++) {
      keep[b] = 1;
      const E = Eb[b]; if (E <= 1e-12) continue;
      const frac = Math.min(0.5, cas.kappa * Math.pow(E, cas.nu) * dtc);
      if (frac < 0.05 * this.bandLoss[b]) continue; // the transfer is < 5 % of the band's own decay: off (and cheaper)
      keep[b] = 1 - frac;
      inj[b + 1] += cas.up1 * frac * E; inj[b + 2] += cas.up2 * frac * E;
    }
    // hi-hat clash: top-plate swing above the gap -> sizzle into the top bands, extra loss below
    if (this.name === 'hat') {
      const El = Eb[0] + Eb[1];
      const amp = 0.01 * Math.sqrt(El); // m, edge swing estimate (10 mm at a full-energy hit)
      const gap = C.gapOpen * this.open * this.open; // 12 mm fully open, ~1 mm at a third open
      const over = Math.max(0, amp - gap) / 0.001;
      if (over > 0 && El > 1e-12) {
        const f = Math.min(0.4, C.clash * over * dtc);
        keep[0] *= 1 - f; keep[1] *= 1 - f;
        const dE = f * El; for (const [j, sh] of C.clashTo) inj[nE + j] += sh * dE; // the sizzle lands at 6-16 kHz
      }
    }
    // poles for the next sub-block: base x drain (per band) x hand choke (per mode)
    const chk = this.choke > 0;
    if (chk) this.choke--;
    for (let b = 0; b < nE; b++) keep[b] = Math.pow(keep[b], 1 / (2 * SUB));
    { const { cr, ci, cr0, ci0, gN } = B; const bo = this.bandOf, cf = this.chokeF;
      let any = false;
      for (let k = 0; k < B.n; k++) {
        const b = bo[k]; let mlt = b >= 0 ? keep[b] : 1; if (chk) mlt *= cf[k];
        cr[k] = cr0[k] * mlt; ci[k] = ci0[k] * mlt;
        // injection gains into explicit band b (equal energy share per mode)
        const dE = b >= 1 ? inj[b] : 0;
        if ((k & 3) === 0) this.pick = (this.noise.s = (Math.imul(this.noise.s, 1664525) + 1013904223) >>> 0) >>> 31; // this lane group this time?
        if (dE > 1e-14 && this.pick) { const N = this.bandTo[b] - this.bandFrom[b]; gN[k] = Math.sqrt((2 * dE * Er) / N / (B.eW[k] * SUB * NOISE_VAR)) * this.injC[k]; B.act[k] = 1; any = true; } else gN[k] = 0;
      }
      this.injOn = any; this.ffN = any ? B.ff(gN) : 0;
    }
    for (let b = nE; b < nE + nzs.length; b++) { const dE = inj[b]; if (dE > 0) nzs[b - nE].e += dE; }
    for (let j = 0; j + 1 < nzs.length; j++) { // the noise bands cascade upward too
      const q = nzs[j]; if (q.e <= 1e-12) continue;
      const dE = Math.min(0.5, (cas.kappaN ?? cas.kappa) * Math.pow(q.e, cas.nu) * dtc) * q.e;
      q.e -= dE; nzs[j + 1].e += (cas.upN ?? cas.up1) * dE;
    }
    for (const q of nzs) {
      const t60 = this.name === 'hat' ? Math.exp((1 - Math.pow(this.open, 0.6)) * Math.log(q.t60c) + Math.pow(this.open, 0.6) * Math.log(q.t60)) : q.t60;
      q.e *= Math.exp((-13.815510557964274 * dtc) / t60);
      if (chk) q.e *= Math.exp((-2 * dtc) / 0.02);
      // shimmer: a slow random walk (~8 Hz), +-20 %
      q.shT += (this.rng.bip() * 0.2 - q.shT) * 0.03; q.sh += (q.shT - q.sh) * 0.05;
      q.tgt = this.noiseAmp(q);
    }
    B.flush(); this.stickB.flush(); B.cull(0, B.n, this.cullFloor);
    // idle
    let tot = 0; for (let b = 0; b < nE; b++) tot += Eb[b]; for (const q of nzs) tot += q.e;
    tot += B.energy(B.eW, this.nPlate, B.n) / Er;
    if (this.pLeft === 0 && this.skin.i >= this.skin.n && this.air.i >= this.air.n && tot < 1e-11) { if (++this.quietSubs > 8) { this.reset(); } } else this.quietSubs = 0;
  }
}

// ======================================================================================
// THE KIT
// ======================================================================================
// Kit balance (piece output gains), calibrated in tools/calibrate.mjs so a u = 0.8 stroke lands at the
// target loudness relative to the snare (see the spec); and the sympathetic coupling matrix.
export const LEVELS = { snare: 103.4, kick: 353, tom1: 246.8, tom2: 226.2, floor: 218.7, hat: 856.9, crash: 1075, ride: 1810 };
const SYM = [ // [from, to, gain]: air coupling, low-passed and delayed 1.3 ms
  ['kick', 'snare', 1.0], ['tom1', 'snare', 0.4], ['tom2', 'snare', 0.4], ['floor', 'snare', 0.45],
  ['kick', 'tom1', 0.25], ['kick', 'tom2', 0.25], ['kick', 'floor', 0.35], ['snare', 'tom1', 0.12], ['snare', 'tom2', 0.12], ['snare', 'floor', 0.1],
];
export const SYM_DELAY = 64;
export const SYM_HZ = 700; // the air couples the drums below this: only these head modes take the sympathetic force
export const CULL = 1e-12;  // per-mode output power floor (re full scale, after the level): -120 dBFS
export const SYM_WAKE = 1e-7; // N: below this the coupled drum stays asleep

export class DrumKit {
  constructor(sr = 48000, seed = 1) {
    this.sr = sr; this.t = 0; // absolute sample of the next process() frame
    this.p = {};
    let s = seed >>> 0;
    const next = () => (s = (Math.imul(s, 2654435761) + 0x9e3779b9) >>> 0);
    for (const name of PIECES) this.p[name] = name === 'hat' || name === 'crash' || name === 'ride' ? new Cymbal(name, sr, next()) : new Membrane(name, sr, next());
    for (const name of PIECES) this.p[name].level = LEVELS[name];
    this.out = Object.fromEntries(PIECES.map((n) => [n, new Float32Array(MAX_BLOCK)]));
    this.queue = []; // {at, fn}
    // sympathetic delay lines (1-pole lp at ~900 Hz of each source's output)
    this.symLp = Object.fromEntries(PIECES.map((n) => [n, 0]));
    this.symHist = Object.fromEntries(PIECES.map((n) => [n, new Float32Array(4096)]));
    this.symW = 0;
    this.symIn = Object.fromEntries(['snare', 'tom1', 'tom2', 'floor'].map((n) => [n, new Float32Array(MAX_BLOCK)]));
    for (const n of ['snare', 'tom1', 'tom2', 'floor']) this.p[n].sym = this.symIn[n];
    this.symScale = 0.21;
    this.stickCurve = STICK_V; this.cymCurve = CYM_V; // settings switch 'expressive': both = STICK_V_EXPRESSIVE
  }
  at(sec) { return Math.round(sec * this.sr); }
  // ---- API (mirrors IAudioEngine.Drum / Choke, plus hat openness) ----
  // kind: kick|snare|hat|hatOpen|hatClose|tom1|tom2|floor|crash|ride; u: app stroke 0.05..1;
  // strike: {pos 0..1, zone head|rim|xstick|bell|bow|edge|top, impl tip|shoulder|finger}
  drum(kind, u, strike = {}, atSample = this.t) {
    this.queue.push({ at: Math.max(this.t, atSample), kind, u: Math.min(1, Math.max(0.05, u)), strike });
    this.queue.sort((a, b) => a.at - b.at);
  }
  chokeAt(kind, atSample = this.t) { this.queue.push({ at: Math.max(this.t, atSample), choke: kind }); this.queue.sort((a, b) => a.at - b.at); }
  hatOpenAt(h, atSample = this.t) { this.queue.push({ at: Math.max(this.t, atSample), open: h }); this.queue.sort((a, b) => a.at - b.at); }
  apply(e) {
    if (e.choke) { const c = this.p[e.choke === 'hatOpen' || e.choke === 'hatClose' ? 'hat' : e.choke]; if (c.chokeNow) c.chokeNow(); return; }
    if (e.open !== undefined) { this.p.hat.setOpen(e.open); return; }
    const s = e.strike || {}, pos = s.pos ?? 0.3, impl = s.impl || 'tip';
    const SV = this.stickCurve;
    let zone = s.zone || 'head';
    switch (e.kind) {
      case 'kick': this.p.kick.strike(BEATER_V(e.u), 0.12 + 0.1 * (pos - 0.2), 'head', 'beater'); break;
      case 'snare': case 'tom1': case 'tom2': case 'floor': {
        const V = impl === 'finger' ? FINGER_V(e.u) : SV(e.u);
        this.p[e.kind].strike(V, pos, zone === 'rim' ? 'rim' : zone === 'xstick' ? 'xstick' : 'head', impl); break;
      }
      case 'hat': case 'hatOpen': {
        const hat = this.p.hat;
        if (e.kind === 'hat' && hat.openTarget > 0) { hat.openTarget = 0; hat.open = 0; hat.applyOpen(0); }
        if (e.kind === 'hatOpen') { const h = s.open > 0 && s.open < 1 ? s.open : (hat.openTarget > 0 ? hat.openTarget : 1); hat.openTarget = h; hat.open = h; hat.applyOpen(h); }
        const r = zone === 'edge' ? 0.96 : 0.55 + 0.3 * (pos - 0.5);
        hat.strike(impl === 'finger' ? FINGER_V(e.u) : this.cymCurve(e.u), r, zone, zone === 'edge' ? 'shoulder' : impl); break;
      }
      case 'hatClose': this.p.hat.pedalClose(e.u); break;
      case 'crash': case 'ride': {
        const C = this.p[e.kind];
        const r = zone === 'bell' ? Math.min(0.15, pos) : zone === 'edge' ? Math.max(0.9, pos) : Math.min(0.85, Math.max(0.3, pos));
        const im = impl === 'finger' ? 'finger' : e.kind === 'crash' && zone !== 'bell' ? 'shoulder' : impl;
        C.strike(impl === 'finger' ? FINGER_V(e.u) : this.cymCurve(e.u), r, zone, im); break;
      }
    }
  }
  // Render `frames` samples into this.out[piece][0..frames). Events land on their exact sample.
  process(frames) {
    for (const n of PIECES) this.out[n].fill(0, 0, frames);
    let i = 0;
    while (i < frames) {
      const abs = this.t + i;
      while (this.queue.length && this.queue[0].at <= abs) this.apply(this.queue.shift());
      // next boundary: sub-block grid or next event
      let end = Math.min(frames, i + (SUB - (abs % SUB)));
      if (this.queue.length) end = Math.min(end, Math.max(i + 1, this.queue[0].at - this.t));
      const atGrid = (this.t + end) % SUB === 0;
      this.fillSym(i, end);
      for (const n of PIECES) {
        const P = this.p[n];
        if (P.idle) continue;
        P.gridLeft = SUB - (abs % SUB);
        P.renderSeg(this.out[n], i, end);
        if (atGrid) P.control();
      }
      this.pushSym(i, end);
      i = end;
    }
    this.t += frames;
  }
  fillSym(i0, i1) {
    for (const n of ['snare', 'tom1', 'tom2', 'floor']) { this.symIn[n].fill(0, i0, i1); this.p[n].symOn = false; }
    for (const [from, to, g] of SYM) {
      const H = this.symHist[from], dst = this.symIn[to];
      let mx = 0;
      for (let i = i0; i < i1; i++) { const v = g * this.symScale * H[(this.symW + (i - i0) - SYM_DELAY + 4096) & 4095]; dst[i] += v; mx = Math.max(mx, Math.abs(v)); }
      const P = this.p[to];
      if (mx > SYM_WAKE) { P.symOn = true; if (P.idle) { P.idle = false; P.quietSubs = 0; } } // air from another drum sets this one moving
      // (below SYM_WAKE the sympathetic force is ignored: a cheaper kernel, and an idle drum stays asleep)
    }
  }
  pushSym(i0, i1) {
    const a = 1 - Math.exp((-TAU * 900) / this.sr);
    for (const n of ['kick', 'snare', 'tom1', 'tom2', 'floor']) {
      const H = this.symHist[n], o = this.out[n]; let y = this.symLp[n];
      for (let i = i0; i < i1; i++) { y += a * (o[i] - y); H[(this.symW + (i - i0)) & 4095] = y; }
      this.symLp[n] = y;
    }
    this.symW = (this.symW + (i1 - i0)) & 4095;
  }
}
