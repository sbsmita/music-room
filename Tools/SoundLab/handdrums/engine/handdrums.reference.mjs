// handdrums.reference.mjs: a pair of congas (conga 11.75" + tumba 12.5") played with bare hands, as a block-based
// float32 reference engine for the Unity Burst audio graph (Docs/12-new-instruments/handdrums.md). v2.
//
// What it models
//  - Each drum is ONE persistent physical object (a modal bank) that keeps ringing: strokes add energy to its state,
//    so rolls build, flams interfere, and a resting hand damps whatever is ringing.
//  - Modes per drum: the coupled head + air-column modes (B "bass", T "tone", A2), the rawhide head's Bessel modes up
//    to ~1.4 kHz (two orientations for m >= 1, split by uneven tension, slight bending-stiffness stretch), a dense
//    statistical field from there to 8 kHz (120 stratified log-spaced slots, each carrying the sqrt of the number of real
//    head modes it stands for, T60 55 -> 12 ms so neighbouring modes overlap: the slap's crack, noise-like, never
//    "metallic"), four staved-shell ring modes driven through the bearing edge, and two Q ~ 2 hand-cavity modes (the pop).
//  - A stroke is 1-2 contact PATCHES (palm, heel, flat fingers along a line, cupped fingertips, palm base on the rim,
//    finger pads). Each patch is a hand-part mass on a Hunt-Crossley flesh spring F = k d^alpha (1 + lam d'), alpha 2.5
//    for soft parts and 1.5 (Hertz, stiff) for the slap's fingertip pads over the bone, pushing a locally reacting head
//    (static point stiffness Kh in parallel with the wave dashpot Zh). The scalar contact runs per sample in double while
//    the patch is on (<= ~12 ms); its force difference dF drives the modes (open loop).
//    Per-mode input gain = (mode shape averaged over the patch's points x the patch's pressure-profile average
//    3 (sin x - x cos x)/x^3, x = kappa b: a Hertz-like contact) / (modal mass * omega). Big soft patches filter out high
//    modes: no click. A patch centred at or over the bearing edge drives the head with its on-head part.
//  - DC-free by construction: the modes are driven by dF, so each mode's output is its velocity (a zero at DC), then
//    weighted by its radiation (monopole / multipole by ka, times omega for pressure).
//  - A resting hand ("mute", closed tones, the heel in heel-toe) adds per-mode damping R phi^2 / (2 M) from the resting
//    points (as a per-sample radius factor computed once per rest); lifting removes it. Pole changes are applied per
//    32-sample control sub-block (state stays continuous).
//  - Tension glide: the head's stretch energy raises every head mode by s = sqrt(1 + kappa E) (capped), so hard strokes
//    start a little sharp and settle (Rossing 1992: dT ~ amplitude^2, f ~ sqrt(T)). Only the gliding modes are refreshed.
//  - Budget scaling: at block start a group cap from Budget/1200 x Share; the quieter drum's dense, then high head groups
//    are shed first (a -60 dB pole fade over the block, then retired).
//
// Port rules followed: struct-of-arrays Float32Array state, everything allocated in the constructor, no allocation in
// process() (the event scratch is preallocated), seeded RNG (mulberry32) for per-stroke micro-variation in a fixed draw
// order, control work on the absolute 32-sample grid, sample-accurate events, canonical float32 MB4 kernels from
// Tools/SoundLab/burst/lab/kernels.mjs.
import { f32 } from '../../burst/lab/common.mjs';
import { mad, mul, add, MB, FLUSH } from '../../burst/lab/kernels.mjs';

export const SR = 48000;
export const SUB = 32;              // control sub-block (1500 Hz)
export const MAX_SEG = 256;         // longest segment a kernel call sees (Unity block)
export const SLOTS = 4;             // contact patches per drum (2 hands x 2 patches)
export const MAX_PTS = 4;           // sample points per patch
export const SHAPE_N = 65;          // radial shape table points, rho in [0, 1]
export const MAX_M = 24;            // highest angular order m of a listed head mode (and of the dense field's 0..7)
export const GLIDE_EPS = 2e-6;      // a glide change smaller than this (0.0035 cents) does not refresh the poles
export const CULL2 = f32(1e-11);    // group energy |z|^2 go^2 below this (-110 dBFS) retires (as burst.md)
export const FLIGHT_S = 0.003;      // s: a patch that left the head is kept this long (re-contact allowed)
export const CONTACT_MAX_S = 0.04;  // s: safety cap of one patch
export const OUTPUT_TRIM_DB = 7.86;     // dB on every output gain: loudness parity with the piano's mf reference (tools/parity.mjs --write)
export const Y2_REL = 0.03;         // head stiffening deflection scale re the radius (4.4 mm on the conga)
export const POP_MIN = 18, POP_MAX = 28; // samples (0.38-0.58 ms, per stroke): the air pushed out from under the hand: a smooth
                                          // raised-cosine pulse of area POP_K cup V^2 into the hand-cavity modes (Peltola et al. 2007: claps
                                          // = a short pulse through the cavity's resonance). Deterministic energy (a noise burst varied 10 dB).
export let POP_K = 52;            // pop strength (v2: 110 -> 52, cavity Q 4 -> 2: the pop is 7-12 dB under the slap's peak, not its peak)
export function setPopK(v) { POP_K = v; }
const TAU = 2 * Math.PI;

// ---------------------------------------------------------------- RNG (mulberry32, the spec-06 / drums generator)
export class Rng {
  constructor(seed) { this.a = seed >>> 0; }
  next() { this.a = (this.a + 0x6d2b79f5) >>> 0; let t = this.a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }
  bip() { return this.next() * 2 - 1; }
}

// ---------------------------------------------------------------- Bessel (setup only, main thread)
export function besselJ(m, x) { const N = 200; let s = 0; for (let i = 0; i < N; i++) { const t = ((i + 0.5) * Math.PI) / N; s += Math.cos(m * t - x * Math.sin(t)); } return s / N; }
const ZC = new Map();
export function besselZeros(m, count) {
  const key = m + ':' + count; if (ZC.has(key)) return ZC.get(key);
  const out = []; let x = Math.max(0.5, m * 0.9), prev = besselJ(m, x);
  while (out.length < count && x < 90) {
    const nx = x + 0.05, v = besselJ(m, nx);
    if ((prev < 0) !== (v < 0)) { let lo = x, hi = nx; for (let i = 0; i < 44; i++) { const mid = 0.5 * (lo + hi); if ((besselJ(m, lo) < 0) === (besselJ(m, mid) < 0)) lo = mid; else hi = mid; } out.push(0.5 * (lo + hi)); }
    x = nx; prev = v;
  }
  ZC.set(key, out); return out;
}
export const J01 = 2.404825557695773;
export const discAvg = (x) => (x < 1e-6 ? 1 : (2 * besselJ(1, x)) / x); // mean of a plane-wave-like mode over a disc (v1)
// v2: the mode averaged under a Hertz-like (elliptical) contact pressure p ~ sqrt(1 - r^2/b^2), as under soft tissue:
// its Hankel transform is 3 (sin x - x cos x) / x^3 (first zero 4.49, not the uniform disc's 3.83, and smaller side lobes),
// so a fingertip's HF is not notched out at 5-7 kHz on the low-tension tumba. Series below x = 0.05 (1 - x^2/10 + x^4/280).
export const hertzAvg = (x) => (x < 0.05 ? 1 - (x * x) / 10 + (x * x * x * x) / 280 : (3 * (Math.sin(x) - x * Math.cos(x))) / (x * x * x));

// ---------------------------------------------------------------- the instrument data (sources: handdrums.md §2, §11)
// a = vibrating radius inside the bearing edge (m); sigma = rawhide areal density (kg/m^2); fT = open-tone (coupled
// (0,1)) frequency; memK = the head's own (0,1) / fT (the air column pushes the coupled tone up); fB = coupled bass
// (air column + head, the palm's "bass"); fA2 = second air-column mode. T60s in s. Shell ring modes of the staved barrel.
export const DRUMS = {
  conga: {
    a: 0.146, sigma: 1.35, fT: 261.6, memK: 0.93, fB: 78, fA2: 300, beta: 1.5e-4, split: 0.005,
    t60: { B: 0.30, T: 0.85, A2: 0.22, at: 0.62, ref: 400, exp: 0.9, min: 0.035, max: 0.7 },
    // v2: dense field from maxHz (no double count with the listed modes) to 8 kHz, 120 slots; rad.dense 1.3 -> 0.5 with the
    // per-slot sqrt(real mode count) weight; seed: a representative realization (handdrums.md 2.2), v1 0x5ca1ab1e / 0x7b3a9d21
    maxHz: 1400, dense: { n: 120, from: 1400, to: 8000, t60From: 0.055, t60To: 0.012 },
    shell: [{ f: 272, t60: 0.14, g: 1.0 }, { f: 521, t60: 0.11, g: 0.8 }, { f: 846, t60: 0.08, g: 0.6 }, { f: 1238, t60: 0.06, g: 0.45 }],
    cavity: [{ f: 1700, t60: 0.0026, g: 1.0 }, { f: 2600, t60: 0.0017, g: 0.6 }],
    rad: { B: 1.2, T: 1.0, A2: 0.9, multipole: 0.40, dense: 0.5, shell: 0.06, cavity: 1.0 },     // v2: B 1.55 -> 1.2 (critique M1)
    glideMax: 0.03, seed: 0x4019462e, kappa: 19.413, level: 0.12766,   // kappa, level: tools/calibrate.mjs
  },
  tumba: {
    a: 0.156, sigma: 1.35, fT: 196.0, memK: 0.93, fB: 70, fA2: 250, beta: 1.5e-4, split: 0.005,
    t60: { B: 0.34, T: 1.0, A2: 0.24, at: 0.66, ref: 400, exp: 0.9, min: 0.035, max: 0.75 },
    maxHz: 1300, dense: { n: 120, from: 1300, to: 8000, t60From: 0.06, t60To: 0.013 },
    shell: [{ f: 248, t60: 0.15, g: 1.0 }, { f: 476, t60: 0.12, g: 0.8 }, { f: 772, t60: 0.085, g: 0.6 }, { f: 1130, t60: 0.065, g: 0.45 }],
    cavity: [{ f: 1650, t60: 0.0027, g: 1.0 }, { f: 2500, t60: 0.0018, g: 0.6 }],
    rad: { B: 1.3, T: 1.0, A2: 0.9, multipole: 0.40, dense: 0.5, shell: 0.06, cavity: 1.0 },     // v2: B 2.0 -> 1.3 (critique M1)
    glideMax: 0.035, seed: 0xb7a99083, kappa: 25.824, level: 0.12443,
  },
};
export const DRUM_NAMES = ['conga', 'tumba'];

// Hand parts (the contact patches). m = effective striking mass (kg), k = flesh Hunt-Crossley stiffness (N/m^alpha;
// alpha 2.5: soft tissue is soft at first touch and stiff once compressed, so harder strokes are shorter and brighter),
// lam = its damping (s/m), b = patch radius (m) for the disc average, R = resistance of the part resting on the head
// (N s/m, the mute), cup = drive of the cupped-hand cavity. Vmin/Vmax = physical impact speed range (m/s) for u 0..1.
export const PARTS = {
  palm:    { id: 0, m: 0.18,  k: 6.0e8, alpha: 2.5, lam: 0.8, b: 0.035, R: 55, cup: 1.3,  Vmin: 0.15, Vmax: 3.6, cap: 1.3 },  // v2: m .25 -> .18, k 3e8 -> 6e8
  heel:    { id: 1, m: 0.15,  k: 1.0e9, alpha: 2.5, lam: 0.8, b: 0.022, R: 40, cup: 0.2,  Vmin: 0.15, Vmax: 1.6, cap: 1.3 },
  fingers: { id: 2, m: 0.030, k: 1.0e9, alpha: 2.5, lam: 1.0, b: 0.012, R: 18, cup: 1.2,  Vmin: 0.15, Vmax: 4.5 },  // flat fingers along a line
  tips:    { id: 3, m: 0.005, k: 2.0e7, alpha: 1.5, lam: 1.0, b: 0.007, R: 8,  cup: 1.8,  Vmin: 0.40, Vmax: 6.0 },  // cupped fingertips (slap): Hertz pads over the bone (v2)
  base:    { id: 4, m: 0.10,  k: 1.0e9, alpha: 2.5, lam: 0.8, b: 0.020, R: 30, cup: 0.4,  Vmin: 0.20, Vmax: 3.0 },  // palm base / knuckles at the rim
  pads:    { id: 5, m: 0.03,  k: 5.0e8, alpha: 2.5, lam: 0.8, b: 0.012, R: 14, cup: 0.3,  Vmin: 0.15, Vmax: 1.6 },  // finger pads (toe, touch)
};
export const PART_LIST = ['palm', 'heel', 'fingers', 'tips', 'base', 'pads'];
// The app's stroke value u comes from the measured hand speed v (tools/interaction.mjs): u = ((v - V0g) / (V1 - V0g))^0.7,
// V0g = U_V0.palm for the palm group, U_V0.fingers for the finger group. handSpeed(u, g) inverts it.
export const U_V0 = { palm: 0.30, fingers: 0.25 }, U_V1 = 2.0, U_EXP = 0.7;
export const handSpeed = (u, g = 'palm') => { const c = u < 0 ? 0 : u > 1 ? 1 : u, v0 = U_V0[g]; return v0 + (U_V1 - v0) * Math.pow(c, 1 / U_EXP); };
// V(part, u): the impact speed of that part. Fingers and tips whip faster than the tracked hand (their own range); the
// palm and the heel are tracked well and do not whip, so their speed is capped at cap x the hand speed that gave u
// (v2: v1 drove the palm at 3.6 m/s for a 2 m/s hand, the critique's "+12 dB A bass at u 1").
export const partV = (part, u) => { const P = PARTS[part]; const c = u < 0 ? 0 : u > 1 ? 1 : u; const V = P.Vmin + (P.Vmax - P.Vmin) * Math.pow(c, 1.1); return P.cap ? Math.min(V, P.cap * handSpeed(c, 'palm')) : V; };

// Mode kinds
export const K_B = 0, K_T = 1, K_A2 = 2, K_MEM = 3, K_DENSE = 4, K_SHELL = 5, K_CAV = 6;

// ---------------------------------------------------------------- one drum: tables (main thread, double maths)
export function buildDrum(name, sr = SR, overrides = {}) {
  const D = { ...DRUMS[name], ...overrides };
  const rng = new Rng(D.seed);
  const a = D.a, sig = D.sigma;
  const f01m = D.fT * D.memK;                         // the head's own (0,1)
  const cm = (TAU * f01m * a) / J01;                  // membrane wave speed (m/s)
  const T = sig * cm * cm;                            // tension (N/m)
  const modes = [];
  const add0 = (o) => modes.push({ m: 0, orient: 0, j: J01, ...o });
  const M01 = sig * Math.PI * a * a * besselJ(1, J01) ** 2;
  // coupled head + air modes: (0,1) head shape; the air column adds mass (mu = air mass / head modal mass)
  add0({ kind: K_B, f: D.fB, t60: D.t60.B, M: M01 * 3.2, rad: D.rad.B });
  add0({ kind: K_T, f: D.fT, t60: D.t60.T, M: M01 * 1.15, rad: D.rad.T });
  add0({ kind: K_A2, f: D.fA2, t60: D.t60.A2, M: M01 * 2.2, rad: D.rad.A2 });
  // the head's Bessel modes (except (0,1), which is T/B)
  const list = [];
  for (let m = 0; m <= 24; m++) {
    const zs = besselZeros(m, 10);
    for (let n = 0; n < zs.length; n++) {
      if (m === 0 && n === 0) continue;
      const j = zs[n];
      const f = f01m * (j / J01) * Math.sqrt(1 + D.beta * j * j);
      if (f > D.maxHz) continue;
      list.push({ m, n: n + 1, j, f });
    }
  }
  list.sort((p, q) => p.f - q.f);
  const t60Law = (f) => Math.min(D.t60.max, Math.max(D.t60.min, D.t60.at * Math.pow(f / D.t60.ref, -D.t60.exp)));
  for (const L of list) {
    const ka = (TAU * L.f / 343) * a;
    const eps = L.m === 0 ? 1 : 0.5;
    const M = sig * Math.PI * a * a * besselJ(L.m + 1, L.j) ** 2 * eps;
    // radiation (amplitude, toward the player ~37 deg off the head axis): monopole (0,n) by its net volume re (0,1);
    // multipoles by (ka/2)^m saturating at 1, times the lobe factor
    let rad;
    if (L.m === 0) { const vol = (besselJ(1, L.j) / L.j) / (besselJ(1, J01) / J01); rad = vol; }
    else { const x = Math.pow(ka / 2, L.m); rad = D.rad.multipole * x / Math.sqrt(1 + x * x); }
    const orients = L.m === 0 ? [0] : [0, 1];
    for (const o of orients) {
      const sp = L.m === 0 ? 1 : 1 + (o === 0 ? -1 : 1) * D.split * (0.6 + 0.8 * rng.next()) * 0.5;
      modes.push({ kind: K_MEM, m: L.m, n: L.n, orient: o, j: L.j, f: L.f * sp, t60: t60Law(L.f) * (o === 1 ? 0.93 : 1), M, rad });
    }
  }
  // dense statistical field (too many to list; the crack). Random radial profile + sign; M as a mode of that density.
  const dn = D.dense;
  // the head wavenumber at f with the same thick-hide stretch as the listed modes (f = f01m (j/J01) sqrt(1 + beta j^2)):
  // j^2 = (sqrt(1 + 4 beta X^2) - 1) / (2 beta), X = J01 f / f01m (v1 used X: the stiff hide's HF waves are longer)
  const jOf = (f) => { const X = J01 * f / f01m; return D.beta > 0 ? Math.sqrt((Math.sqrt(1 + 4 * D.beta * X * X) - 1) / (2 * D.beta)) : X; };
  for (let i = 0; i < dn.n; i++) {
    // stratified: one mode per log-spaced slot, jittered within +-40 % of the slot (v1: +-2 % of f, wider than a slot: modes
    // clustered into peaks and left gaps, the "separate ringing lines" of the critique)
    const fr = i / (dn.n - 1), f = dn.from * Math.pow(dn.to / dn.from, (i + 0.4 * rng.bip()) / (dn.n - 1));
    const t60 = dn.t60From * Math.pow(dn.t60To / dn.t60From, fr);
    const jEq = jOf(f);
    const M = (sig * Math.PI * a * a) / (2 * Math.max(1, jEq / Math.PI));
    const rs = rng.next(), sg = rng.next() < 0.5 ? -1 : 1, md = Math.floor(rng.next() * 8), ph = rng.next() * TAU;
    const ka = (TAU * f / 343) * a;
    // v2: each slot stands for every real head mode in it. A circular membrane has N(j) ~ j^2/4 modes below wavenumber j
    // (Weyl), so a log-spaced slot holds (j_hi^2 - j_lo^2)/4 of them: ~1 at 1.2 kHz, ~25 at 8 kHz. Their energies add
    // (random phases), so the slot's amplitude carries sqrt of that count (v1 had one real mode's worth per slot: the
    // field fell ~12 dB/octave above 2 kHz and the slap had no crack).
    const sLo = dn.from * Math.pow(dn.to / dn.from, (i - 0.5) / (dn.n - 1)), sHi = dn.from * Math.pow(dn.to / dn.from, (i + 0.5) / (dn.n - 1));
    const nReal = Math.max(1, (jOf(sHi) ** 2 - jOf(sLo) ** 2) / 4);
    modes.push({ kind: K_DENSE, m: md, orient: 0, j: jEq, f, t60, M, rad: D.rad.dense * Math.min(1, ka / 3) * Math.sqrt(nReal), rs, sg, ph, nReal });
  }
  // shell ring modes (staved barrel), driven through the bearing edge: M = 0.5 kg-equivalent per mode
  for (const s of D.shell) modes.push({ kind: K_SHELL, m: 0, orient: 0, j: 0, f: s.f * (1 + 0.01 * rng.bip()), t60: s.t60, M: 0.5, rad: D.rad.shell * s.g });
  // the cupped-hand cavity modes get their own (last) group: they are struck by the slap's air "pop", not by dF
  while (modes.length % 4) modes.push({ kind: -1, m: 0, orient: 0, j: 0, f: 1000, t60: 0.001, M: 1, rad: 0 });
  const gCav = modes.length >> 2;
  for (const c of D.cavity) modes.push({ kind: K_CAV, m: 0, orient: 0, j: 0, f: c.f, t60: c.t60, M: 1, rad: D.rad.cavity * c.g });

  const n = modes.length, nPad = (n + 3) & ~3, G = nPad >> 2;
  // tables
  const shape = new Float32Array(nPad * SHAPE_N);
  const fHz = new Float64Array(nPad), r0 = new Float64Array(nPad), w0 = new Float64Array(nPad), Mk = new Float64Array(nPad), om = new Float64Array(nPad);
  const kap = new Float64Array(nPad), eW = new Float64Array(nPad), kind = new Int32Array(nPad).fill(-1), mm = new Int32Array(nPad), orient = new Int32Array(nPad);
  const dPh = new Float64Array(nPad), go = new Float32Array(nPad), radW = new Float64Array(nPad);
  const glideOn = new Uint8Array(nPad);
  for (let k = 0; k < n; k++) {
    const md = modes[k];
    fHz[k] = md.f; r0[k] = Math.exp(-6.907755278982137 / (md.t60 * sr)); w0[k] = (TAU * md.f) / sr; Mk[k] = md.M; om[k] = TAU * md.f;
    kind[k] = md.kind; mm[k] = md.m; orient[k] = md.orient; dPh[k] = md.ph || 0;
    kap[k] = md.j > 0 ? md.j / a : 0;
    radW[k] = md.rad;
    for (let i = 0; i < SHAPE_N; i++) {
      const rho = i / (SHAPE_N - 1);
      let v = 0;
      if (md.kind === K_B || md.kind === K_T || md.kind === K_A2) v = besselJ(0, J01 * rho);
      else if (md.kind === K_MEM) v = besselJ(md.m, md.j * rho);
      else if (md.kind === K_DENSE) v = md.sg * Math.sin(Math.PI * (1 - rho) * (1 + 3 * md.rs)) * Math.SQRT2 * Math.sqrt(1 - (rho * rho) / 2);
      else if (md.kind === K_SHELL) v = rimShare(rho);
      shape[k * SHAPE_N + i] = f32(v);
    }
    // glide weight (strain energy per |velocity|^2): (j/a)^2 * N / omega^2, N = M / sigma; head modes below 800 Hz
    if ((md.kind <= K_A2 || md.kind === K_MEM) && md.f < 800) { glideOn[k] = 1; eW[k] = (J01 * J01 / (a * a)) * (md.kind === K_MEM ? (md.j / J01) ** 2 : 1) * (md.M / sig) / (om[k] * om[k]); }
  }
  // disc-average factor per hand part and mode (setup only: the audio thread looks it up): 2 J1(kappa b)/(kappa b)
  const avgTab = new Float32Array(PART_LIST.length * nPad);
  for (let pi = 0; pi < PART_LIST.length; pi++) for (let k = 0; k < n; k++) {
    const md = modes[k]; avgTab[pi * nPad + k] = f32(md.kind === K_SHELL || md.kind === K_CAV || md.kind < 0 ? 1 : hertzAvg(kap[k] * PARTS[PART_LIST[pi]].b));
  }
  // output gain (far-field pressure per unit modal velocity, re the tone mode). Monopoles (B, T, A2, (0,n)): pressure ~
  // omega x net volume velocity. Every head mode is SUBSONIC (membrane wave speed cm ~ 90 m/s < 343 m/s), so a mode with
  // nodal lines radiates only from its uncancelled edge strip, of width ~ 1/k_mode ~ 1/omega: its pressure per unit
  // velocity does not grow with omega (Cremer-Heckl below coincidence). Multipoles and the dense field: rad, no omega.
  // Shell and cavity: small monopoles, omega x rad.
  for (let k = 0; k < n; k++) {
    const md = modes[k];
    const wFac = (md.kind === K_MEM && md.m > 0) || md.kind === K_DENSE ? 1 : om[k] / (TAU * D.fT);
    go[k] = f32(radW[k] * wFac);
  }
  // the modes refreshed at a glide-only control step: the gliding modes, then the cavity (its per-stroke detune)
  const gl = []; for (let k = 0; k < n; k++) if (glideOn[k] || kind[k] === K_CAV) gl.push(k);
  const glideList = Int32Array.from(gl);
  const cPh = Float64Array.from(dPh, Math.cos), sPh = Float64Array.from(dPh, Math.sin);
  // group classes for the budget: 0 core (B, T, A2, head modes < CORE_HZ, shell, cavity), 1 head (>= CORE_HZ), 2 dense;
  // a mixed group takes its most essential class. Shed order: dense groups highest first, then head groups highest first.
  const gClass = new Uint8Array(G).fill(3);
  for (let k = 0; k < n; k++) {
    const kd = kind[k]; if (kd < 0) continue;
    const c = kd === K_DENSE ? 2 : kd === K_MEM && fHz[k] >= CORE_HZ ? 1 : 0;
    if (c < gClass[k >> 2]) gClass[k >> 2] = c;
  }
  const so = []; for (let g = G - 1; g >= 0; g--) if (gClass[g] === 2) so.push(g); for (let g = G - 1; g >= 0; g--) if (gClass[g] === 1) so.push(g);
  const shedOrder = Int32Array.from(so);
  return {
    name, D, a, sigma: sig, T, cm, f01m, modes, n, nPad, G, gCav, shape, avgTab, fHz, r0, w0, Mk, om, kap, eW, kind, mm, orient, dPh, cPh, sPh, go, radW, glideOn, glideList, gClass, shedOrder,
    kappa: D.kappa ?? 0, level: D.level ?? 1,
  };
}
// share of a force near the rim that reaches the bearing edge (and the shell): 0 inside rho .7, 1 at the rim
export function rimShare(rho) { const x = (rho - 0.7) / 0.3; const c = x < 0 ? 0 : x > 1 ? 1 : x; return c * c * (3 - 2 * c); }

// local head reaction for a patch at radius rho with patch radius b (main or audio thread, double)
export function headReaction(tab, rho, b) {
  const a = tab.a, T = tab.T, sig = tab.sigma;
  const dRim = Math.max(0.25 * b, a * (1 - Math.min(rho, 1)));
  const Kh = (TAU * T) / Math.log(1 + dRim / b);
  const Zh = TAU * b * Math.sqrt(T * sig);
  // the head stiffens under the hand: pushing a cone of depth y into it stretches it, so the local stiffness grows as
  // Kh (1 + (y/y2)^2); y2 = Y2_REL * a (the drums engine's stiffening idea, drums.md §4.4, as a local spring)
  return { Kh, Zh, y2: Y2_REL * a };
}

// ---------------------------------------------------------------- budget scaling (platform 10.3; handdrums.md 2.13)
// At block start: cap = clamp(round(Gtot x Budget/1200 x Share), CAP_MIN, Gtot) groups for the two drums together. The
// groups beyond the cap are taken from the SHED ORDER: the quieter drum's dense groups (highest first), then its head
// groups >= CORE_HZ (highest first), then the louder drum's dense, then its head groups. Core groups (B, T, A2, head modes
// below CORE_HZ, shell, cavity) are never shed. A shed group that is ringing fades out over that block (its pole radius
// x FADE^(1/n), -60 dB at the block end), then is zeroed and retired; a shed group is not activated by new patches.
// "Quieter" = the drum with the smaller output energy at its last control step, with a 2x (3 dB) hysteresis.
export const CAP_MIN = 12, CORE_HZ = 700, LOUDER_HYST = 2, FADE_DB = -60;
// output channels (ENGINE-API Register order = the platform's source order): channel 0 HandDrumLo = the tumba (drum 1),
// channel 1 HandDrumHi = the conga (drum 0). The reference's process(outs) keeps outs[d] by DRUM index; the port writes
// drum d into u->Channel(CHANNEL_OF_DRUM[d]).
export const SOURCES = ['HandDrumLo', 'HandDrumHi'], CHANNEL_OF_DRUM = [1, 0];

// ---------------------------------------------------------------- the engine (two drums)
export class HandDrums {
  constructor(sr = SR, { seed = 1, overrides = {} } = {}) {
    this.sr = sr; this.dt = 1 / sr;
    this.drums = DRUM_NAMES.map((nm, d) => new Drum(buildDrum(nm, sr, overrides[nm] || {}), sr, (seed * 2654435761 + 0x9e3779b9 * (d + 1)) >>> 0));
    this.events = []; this.seq = 0; this.now = 0; // absolute sample of the next block
    this.stats = { strokes: 0, patchesStolen: 0, maxActivePatches: 0, work: 0, blocks: 0, shedGroups: 0 };
    this.gTot = this.drums[0].t.G + this.drums[1].t.G; this.cap = this.gTot; this.louder = 0; this.maskKey = -1;
  }
  // ---- main-thread side: post events (absolute samples). One event = one patch, one rest, or one lift.
  // patch: u in 0..1 is the app's stroke value for THIS patch (already scaled for a second patch); the engine computes the
  // part's impact speed V = partV(part, u). extra.V overrides V (tests only).
  post(ev) { ev.seq = this.seq++; this.events.push(ev); }
  patch(at, drum, hand, part, u, pts, extra = {}) { this.post({ op: 'patch', at, drum, hand, part, V: extra.V ?? partV(part, u), u, pts, ...extra }); }
  rest(at, drum, hand, pts) { this.post({ op: 'rest', at, drum, hand, pts }); }
  lift(at, drum, hand) { this.post({ op: 'lift', at, drum, hand }); }
  reset(at) { this.post({ op: 'reset', at }); }
  // the shed masks for this cap and this louder drum (main or audio thread: cheap, only when either changes)
  masks() {
    const key = this.cap * 2 + this.louder; if (key === this.maskKey) return; this.maskKey = key;
    for (const dr of this.drums) dr.allowed.fill(1);
    let nShed = this.gTot - this.cap; const q = this.drums[1 - this.louder], L = this.drums[this.louder];
    for (const dr of [q, L]) for (let i = 0; i < dr.t.shedOrder.length && nShed > 0; i++, nShed--) dr.allowed[dr.t.shedOrder[i]] = 0;
  }
  // ---- audio-thread side: render one block into outs[0] (conga) and outs[1] (tumba) (added)
  process(outs, n, { budget = 1200, share = 1 } = {}) {
    const b0 = this.now, b1 = b0 + n;
    // budget scaling at block start
    let cap = Math.round(this.gTot * (budget / 1200) * share); if (cap < CAP_MIN) cap = CAP_MIN; if (cap > this.gTot) cap = this.gTot;
    const e0 = this.drums[0].energy, e1 = this.drums[1].energy;
    if (this.louder === 0 && e1 > LOUDER_HYST * e0) this.louder = 1; else if (this.louder === 1 && e0 > LOUDER_HYST * e1) this.louder = 0;
    this.cap = cap; this.masks();
    const fadeMul = Math.exp((FADE_DB / 8.685889638065035) / n);
    for (const dr of this.drums) dr.beginBlock(fadeMul, this.stats);
    // due events (stable sort by at, seq); events in the past play now
    const due = [];
    for (let i = 0; i < this.events.length; i++) if (this.events[i].at < b1) due.push(this.events[i]);
    if (due.length) { this.events = this.events.filter((e) => e.at >= b1); due.sort((p, q) => p.at - q.at || p.seq - q.seq); }
    let work = 0;
    for (let d = 0; d < 2; d++) {
      const dr = this.drums[d]; let cursor = 0;
      for (const e of due) {
        if (e.op !== 'reset' && e.drum !== d) continue;
        const off = Math.max(0, e.at - b0);
        dr.render(outs[d], b0, cursor, off); cursor = off;
        dr.apply(e, b0 + off, this.stats);
      }
      dr.render(outs[d], b0, cursor, n);
      dr.endBlock();
      work += dr.work; dr.work = 0;
      this.stats.maxActivePatches = Math.max(this.stats.maxActivePatches, dr.activePatches());
    }
    this.lastWork = work >> 5; this.stats.work += this.lastWork; this.stats.blocks++;   // Work: group-subblocks (x 32 samples)
    this.now = b1;
  }
}

class Drum {
  constructor(tab, sr, seed) {
    this.t = tab; this.sr = sr; this.dt = 1 / sr; this.rng = new Rng(seed);
    const { nPad, G } = tab;
    this.bank = new Float32Array(G * MB.STRIDE);       // MB4 groups: zr, zi, pr, pi, go, gi(unused)
    this.cr0 = new Float64Array(nPad); this.ci0 = new Float64Array(nPad);
    for (let k = 0; k < tab.n; k++) { this.cr0[k] = Math.cos(tab.w0[k]); this.ci0[k] = Math.sin(tab.w0[k]); }
    this.outGain = tab.level * Math.pow(10, OUTPUT_TRIM_DB / 20);
    for (let k = 0; k < nPad; k++) { const g = k >> 2, l = k & 3; this.bank[g * MB.STRIDE + MB.GO + l] = k < tab.n ? f32(tab.go[k] * this.outGain) : 0; }
    this.act = new Uint8Array(G); this.allowed = new Uint8Array(G).fill(1); this.fading = new Uint8Array(G); this.anyFading = false; this.fadeMul = 1;
    this.dMul = [new Float64Array(nPad).fill(1), new Float64Array(nPad).fill(1)]; // rest damping per hand: per-sample radius factor exp(-dAlpha/sr)
    this.dAl = new Float64Array(nPad);               // scratch: dAlpha (1/s) while a rest is applied
    this.restOn = [false, false];
    this.s = 1; this.polesDirty = true; this.glideDirty = false; this.cavMul = 1; this.energy = 0; this.work = 0;
    // contact patches
    this.P = [];
    for (let p = 0; p < SLOTS; p++) this.P.push({ on: false, part: 0, hand: 0, m: 0, k: 0, lam: 0, Kh: 0, Zh: 0, x: 0, v: 0, y: 0, yd: 0, F: 0, Fp: 0, flight: 0, age: 0, started: false,
      gi: new Float32Array(nPad), dF: new Float32Array(MAX_SEG), pop: new Float32Array(MAX_SEG), cup: 0, V0: 0, popLeft: 0, popA: 0, Fmax: 0, tFirst: -1, tLast: -1 });
    this.on = new Array(SLOTS).fill(null); this.nOn = 0;
    this.acc = new Float32Array(4 * MAX_SEG);
    // scratch for one event (no allocation per event): points, and cos/sin(m theta) per point for m = 0..MAX_M
    this.pr = new Float64Array(MAX_PTS); this.pt = new Float64Array(MAX_PTS);
    this.cm = new Float64Array(MAX_PTS * (MAX_M + 1)); this.sm = new Float64Array(MAX_PTS * (MAX_M + 1));
    this.idle = true;
    this.updatePoles(true);
  }
  activePatches() { let c = 0; for (const p of this.P) if (p.on) c++; return c; }
  // cos/sin(m theta) for m = 0..MAX_M of point i (Chebyshev recurrence: two trig calls per point)
  angles(i, th) {
    const W = MAX_M + 1, o = i * W, c1 = Math.cos(th), s1 = Math.sin(th), cm = this.cm, sm = this.sm;
    cm[o] = 1; sm[o] = 0; cm[o + 1] = c1; sm[o + 1] = s1;
    for (let m = 2; m <= MAX_M; m++) { cm[o + m] = 2 * c1 * cm[o + m - 1] - cm[o + m - 2]; sm[o + m] = 2 * c1 * sm[o + m - 1] - sm[o + m - 2]; }
  }
  // mode shape of mode k at point i (rho, with angles(i) done): radial table (linear interpolation) x angular factor;
  // rho >= 1 is off the head (0) for head modes
  phiAt(k, i, rho) {
    const t = this.t, kd = t.kind[k];
    if (kd === K_CAV) return 0;
    if (kd === K_SHELL) return rimShare(rho < 1 ? rho : 1);
    if (rho >= 1) return 0;
    const x = rho * (SHAPE_N - 1); let j = Math.floor(x); if (j > SHAPE_N - 2) j = SHAPE_N - 2; const fr = x - j;
    const base = t.shape[k * SHAPE_N + j] * (1 - fr) + t.shape[k * SHAPE_N + j + 1] * fr;
    const m = t.mm[k], o = i * (MAX_M + 1) + m;
    if (kd === K_DENSE) return base * (0.75 + 0.25 * (this.cm[o] * t.cPh[k] - this.sm[o] * t.sPh[k]));   // cos(m th + ph)
    if (kd !== K_MEM || m === 0) return base;
    return base * (t.orient[k] === 0 ? this.cm[o] : this.sm[o]);
  }
  beginBlock(fadeMul, stats) {
    // groups shed by the budget: the ringing ones fade over this block, the rest stay off
    const t = this.t; let any = false;
    for (let g = 0; g < t.G; g++) { if (this.act[g] && !this.allowed[g]) { this.fading[g] = 1; any = true; stats.shedGroups++; } }
    if (any) { this.anyFading = true; this.fadeMul = fadeMul; this.updatePoles(true); }
  }
  endBlock() {
    if (!this.anyFading) return;
    const t = this.t, B = this.bank;
    for (let g = 0; g < t.G; g++) if (this.fading[g]) { const o = g * MB.STRIDE; for (let l = 0; l < 4; l++) { B[o + MB.ZR + l] = 0; B[o + MB.ZI + l] = 0; } this.act[g] = 0; this.fading[g] = 0; }
    this.anyFading = false; this.updatePoles(true);
  }
  apply(e, at, stats) {
    const t = this.t;
    if (e.op === 'reset') { this.bank.fill(0, 0); for (let k = 0; k < t.nPad; k++) { const g = k >> 2, l = k & 3; this.bank[g * MB.STRIDE + MB.GO + l] = k < t.n ? f32(t.go[k] * this.outGain) : 0; } for (const p of this.P) p.on = false; this.dMul[0].fill(1); this.dMul[1].fill(1); this.restOn = [false, false]; this.s = 1; this.cavMul = 1; this.updatePoles(true); this.act.fill(0); this.fading.fill(0); this.anyFading = false; this.idle = true; this.energy = 0; return; }
    if (e.op === 'rest') {
      // dAlpha_k = sum over the points of R (phi_k A_k)^2 / (2 M_k); the per-sample radius factor exp(-dAlpha/sr) is
      // computed here, once per rest (v1 took exp per mode at every pole refresh)
      const da = this.dAl; da.fill(0); const np = Math.min(e.pts.length, 3);
      for (let i = 0; i < np; i++) {
        const q = e.pts[i], P = PARTS[q.part], R = (q.R ?? P.R) * (q.press ?? 1);
        this.angles(0, q.theta);
        for (let k = 0; k < t.n; k++) {
          if (t.kind[k] === K_SHELL || t.kind[k] === K_CAV) continue;
          const ph = this.phiAt(k, 0, q.rho) * this.avg(k, P.id);
          da[k] += (R * ph * ph) / (2 * t.Mk[k]);
        }
      }
      const dm = this.dMul[e.hand]; for (let k = 0; k < t.nPad; k++) dm[k] = da[k] > 0 ? Math.exp(-da[k] / this.sr) : 1;
      this.restOn[e.hand] = true; this.polesDirty = true; return;
    }
    if (e.op === 'lift') { this.dMul[e.hand].fill(1); this.restOn[e.hand] = false; this.polesDirty = true; return; }
    // patch: micro-variation (fixed draw order: V, rho, theta, k, pop length, cavity, then one rho per point), slot,
    // per-mode gains
    const P = PARTS[e.part];
    const r = this.rng;
    const V = e.V * (1 + 0.05 * r.bip());
    const dRho = 0.04 * r.bip(), dTh = 0.25 * r.bip(), kMul = 1 + 0.2 * r.bip();   // v2: theta +-0.25 rad (+-3.6 cm along the edge; v1 +-0.2)
    const popLen = POP_MIN + Math.floor(r.next() * (POP_MAX - POP_MIN + 1)), cavMul = 1 + 0.08 * r.bip(); // the hand's shape differs per stroke
    let slot = -1;
    for (let p = 0; p < SLOTS; p++) if (!this.P[p].on) { slot = p; break; }
    if (slot < 0) { let best = 0; for (let p = 1; p < SLOTS; p++) if (this.P[p].age > this.P[best].age) best = p; slot = best; stats.patchesStolen++; }
    const S = this.P[slot];
    // every sample point also moves a little on its own (fingers never land in exactly the same line): one draw per point
    const np = Math.min(e.pts.length, MAX_PTS); let rhoC = 0;
    // a patch centred at or over the bearing edge drives the head with its on-head part: its points are taken no further
    // out than 1 - b/(2a), about the centroid of that part (v1 gave a palm centred on the rim no head drive at all)
    const rimMax = 1 - (0.5 * P.b) / t.a;
    // (the measured point is clamped first, then the per-stroke variation is added, so strokes at the rim still differ)
    for (let i = 0; i < np; i++) { const q = e.pts[i]; let rr = q.rho > rimMax && q.rho < 1 + (0.5 * P.b) / t.a ? rimMax : q.rho; rr += dRho + 0.02 * r.bip(); if (rr < 0) rr = 0; this.pr[i] = rr; this.pt[i] = q.theta + dTh; rhoC += rr < 1 ? rr : 1; this.angles(i, this.pt[i]); }
    rhoC /= np;
    const { Kh, Zh, y2 } = headReaction(t, rhoC, P.b);
    const cup = (e.cup ?? P.cup);
    if (cup > 0) { this.cavMul = cavMul; this.glideDirty = true; }
    Object.assign(S, { popLen, on: true, started: false, part: P.id, hand: e.hand, m: P.m, k: P.k * kMul, an: Math.round(P.alpha - 0.5), alpha: P.alpha, lam: P.lam, Kh, Zh, iy2: 1 / y2, x: 0, v: V, y: 0, yd: 0, F: 0, Fp: 0, flight: 0, age: 0, Fmax: 0, tFirst: -1, tLast: -1, at, cup, V0: V, popLeft: 0, popA: 0 });
    for (let k = 0; k < t.nPad; k++) {
      if (k >= t.n) { S.gi[k] = 0; continue; }
      let g;
      if (t.kind[k] === K_CAV) g = cup;          // driven by the pop stream (below), not by dF
      else { let sph = 0; for (let i = 0; i < np; i++) sph += this.phiAt(k, i, this.pr[i]); g = (sph / np) * this.avg(k, P.id); }
      S.gi[k] = f32(g / (t.Mk[k] * t.om[k]));
    }
    for (let g = 0; g < t.G; g++) if (this.allowed[g]) this.act[g] = 1;
    this.idle = false;
    stats.strokes++;
  }
  avg(k, partId) { return this.t.avgTab[partId * this.t.nPad + k]; }
  // pole refresh: |p| = r0 dMul_L dMul_R (x the fade factor of a shed group), angle = w0 s (rotation by delta = w0 (s - 1),
  // polynomial cos/sin). full = every mode (a rest, a lift, a fade, the cavity); otherwise only the gliding modes.
  updatePoles(full) {
    const t = this.t, B = this.bank, s1 = this.s - 1, m0 = this.dMul[0], m1 = this.dMul[1];
    const list = full ? null : t.glideList, cnt = full ? t.n : t.glideList.length;
    for (let i = 0; i < cnt; i++) {
      const k = full ? i : list[i], g = k >> 2;
      let R = t.r0[k] * m0[k] * m1[k]; if (this.fading[g]) R *= this.fadeMul;
      let cr = this.cr0[k], ci = this.ci0[k];
      if (t.kind[k] === K_CAV && this.cavMul !== 1) { const w = t.w0[k] * this.cavMul; cr = Math.cos(w); ci = Math.sin(w); }
      if (t.glideOn[k] && s1 !== 0) {
        const dl = t.w0[k] * s1, d2 = dl * dl;
        const cd = 1 - d2 * (0.5 - d2 / 24), sd = dl * (1 - d2 * (1 / 6 - d2 / 120));
        const nr = cr * cd - ci * sd, ni = ci * cd + cr * sd; cr = nr; ci = ni;
      }
      const l = k & 3, o = g * MB.STRIDE;
      B[o + MB.PR + l] = f32(R * cr); B[o + MB.PI + l] = f32(R * ci);
    }
    if (full) this.polesDirty = false;
    this.glideDirty = false;
  }
  // render [i0, i1) of the block that starts at absolute sample b0, adding into out
  render(out, b0, i0, i1) {
    let i = i0;
    while (i < i1) {
      const abs = b0 + i, toGrid = SUB - (abs % SUB), j = Math.min(i1, i + toGrid);
      if (!this.idle) this.segment(out, i, j);
      if ((b0 + j) % SUB === 0) this.control();
      i = j;
    }
  }
  segment(out, i0, i1) {
    const t = this.t, B = this.bank, acc = this.acc, len = i1 - i0, on = this.on;
    // contact patches: scalar, double precision, per sample (ascending slot order)
    let nOn = 0;
    for (let p = 0; p < SLOTS; p++) { const S = this.P[p]; if (!S.on) continue; on[nOn++] = S; this.contact(S, len); }
    for (let n = 0; n < 4 * len; n++) acc[n] = 0;
    let groups = 0;
    for (let g = 0; g < t.G; g++) {
      if (!this.act[g]) continue;
      groups++;
      const o = g * MB.STRIDE;
      for (let l = 0; l < 4; l++) {
        const k = 4 * g + l; if (k >= t.n) continue;
        let zr = B[o + MB.ZR + l], zi = B[o + MB.ZI + l];
        const pr = B[o + MB.PR + l], pi = B[o + MB.PI + l], npi = -pi, go = B[o + MB.GO + l];
        if (nOn === 0) {
          for (let n = 0; n < len; n++) { const nr = mad(pr, zr, mul(npi, zi)); const ni = mad(pr, zi, mul(pi, zr)); zr = nr; zi = ni; acc[4 * n + l] = mad(go, ni, acc[4 * n + l]); }
        } else {
          // driven, up to 4 inputs: u = sum_p gi_p[k] * dF_p[n] (ascending slot order), then MB4 driven step
          // the cavity group is driven by the patches' air-pop streams instead of their force differences
          const cav = g === t.gCav;
          const g0 = on[0].gi[k], d0 = cav ? on[0].pop : on[0].dF, g1 = nOn > 1 ? on[1].gi[k] : 0, d1 = nOn > 1 ? (cav ? on[1].pop : on[1].dF) : null;
          const g2 = nOn > 2 ? on[2].gi[k] : 0, d2 = nOn > 2 ? (cav ? on[2].pop : on[2].dF) : null, g3 = nOn > 3 ? on[3].gi[k] : 0, d3 = nOn > 3 ? (cav ? on[3].pop : on[3].dF) : null;
          for (let n = 0; n < len; n++) {
            let u = mul(g0, d0[n]);
            if (d1) u = mad(g1, d1[n], u);
            if (d2) u = mad(g2, d2[n], u);
            if (d3) u = mad(g3, d3[n], u);
            const nr = mad(pr, zr, mad(npi, zi, u)); const ni = mad(pr, zi, mul(pi, zr)); zr = nr; zi = ni;
            acc[4 * n + l] = mad(go, ni, acc[4 * n + l]);
          }
        }
        B[o + MB.ZR + l] = zr; B[o + MB.ZI + l] = zi;
      }
    }
    this.work += groups * len;
    for (let n = 0; n < len; n++) { const s = add(add(acc[4 * n], acc[4 * n + 1]), add(acc[4 * n + 2], acc[4 * n + 3])); out[i0 + n] = add(out[i0 + n], s); }
  }
  // one patch over len samples: hand mass on a Hunt-Crossley flesh spring against a locally reacting head
  // Per sample: x' = x + v dt (hand, predicted); the head point y' and the force F solve
  //   Zh (y' - y)/dt + Kh y' = F,  F = k d^1.5 (1 + lam (v - (y' - y)/dt)),  d = x' - y'  (F >= 0)
  // implicitly (the small-patch head is stiff: explicit Euler diverges for fingertips) by 4 fixed Newton steps on y'
  // (g(y') is monotone increasing, so the root is unique); then v' = v - F dt / m. Double precision (IEEE + - * / sqrt
  // are correctly rounded in C# too, so the managed port is bit-identical in this part).
  contact(S, len) {
    const dt = this.dt, sr = this.sr, flightMax = Math.round(FLIGHT_S * sr), ageMax = Math.round(CONTACT_MAX_S * sr);
    const zdt = S.Zh * sr;
    for (let n = 0; n < len; n++) {
      let F = 0;
      if (S.on) {
        const xn = S.x + S.v * dt, y0 = S.y;
        let y = y0;
        for (let it = 0; it < 4; it++) {
          const d = xn - y;
          let Fh = 0, dFdy = 0;
          if (d > 0) {
            const sd = Math.sqrt(d), dd = S.v - (y - y0) * sr, damp = 1 + S.lam * dd;
            const dn1 = S.an === 1 ? 1 : S.an === 2 ? d : d * d, pw = dn1 * d * sd;   // d^alpha = d^n sqrt(d), alpha = n + 0.5 (1.5, 2.5, 3.5)
            Fh = S.k * pw * damp;
            if (Fh > 0) dFdy = -(S.alpha * dn1 * sd) * S.k * damp - S.k * pw * S.lam * sr; else Fh = 0;
          }
          const q = y * S.iy2, g = zdt * (y - y0) + S.Kh * y * (1 + q * q) - Fh, gp = zdt + S.Kh * (1 + 3 * q * q) - dFdy;
          y -= g / gp;
        }
        { const d = xn - y; if (d > 0) { const sd = Math.sqrt(d), dn1 = S.an === 1 ? 1 : S.an === 2 ? d : d * d; F = S.k * dn1 * d * sd * (1 + S.lam * (S.v - (y - y0) * sr)); if (F < 0) F = 0; } }
        S.yd = (y - y0) * sr; S.y = y; S.x = xn; S.v -= (F / S.m) * dt;
        if (F > 0) {
          S.started = true; S.flight = 0; S.tLast = S.age; if (F > S.Fmax) S.Fmax = F;
          // first touch of a cupped hand: the trapped air is pushed out (a short pressure pulse, as in a hand clap)
          if (S.tFirst < 0) { S.tFirst = S.age; if (S.cup > 0) { S.popLeft = S.popLen; S.popA = (POP_K * S.cup * S.V0 * S.V0 * 2) / S.popLen; } }
        }
        else if (S.started && S.v < 0) { if (++S.flight > flightMax) S.on = false; }
        if (++S.age > ageMax) S.on = false;
      }
      S.dF[n] = f32(F - S.Fp); S.Fp = F;
      if (S.popLeft > 0) { const i = S.popLen - S.popLeft; S.pop[n] = f32(S.popA * 0.5 * (1 - Math.cos((TAU * (i + 0.5)) / S.popLen))); S.popLeft--; } else S.pop[n] = 0;
    }
  }
  // control sub-block: flush, cull, glide, pole refresh, idle
  control() {
    const t = this.t, B = this.bank;
    let E = 0, Esum = 0, anyAct = false;
    let contactOn = false; for (let p = 0; p < SLOTS; p++) if (this.P[p].on) { contactOn = true; break; }
    for (let g = 0; g < t.G; g++) {
      if (!this.act[g]) continue;
      const o = g * MB.STRIDE; let eg = 0;
      for (let l = 0; l < 4; l++) {
        let zr = B[o + MB.ZR + l], zi = B[o + MB.ZI + l];
        if (Math.abs(zr) < FLUSH) zr = 0; if (Math.abs(zi) < FLUSH) zi = 0;
        B[o + MB.ZR + l] = zr; B[o + MB.ZI + l] = zi;
        const k = 4 * g + l, go = B[o + MB.GO + l];
        const el = (zr * zr + zi * zi) * go * go; if (el > eg) eg = el; Esum += el;
        if (k < t.n && t.glideOn[k]) E += t.eW[k] * (zr * zr + zi * zi);
      }
      if (eg < CULL2 && !contactOn) { for (let l = 0; l < 4; l++) { B[o + MB.ZR + l] = 0; B[o + MB.ZI + l] = 0; } this.act[g] = 0; }
      else anyAct = true;
    }
    const sT = Math.min(1 + t.D.glideMax, Math.sqrt(1 + t.kappa * E));
    if (Math.abs(sT - this.s) > GLIDE_EPS) { this.s = sT; this.glideDirty = true; }
    for (const p of this.P) if (!p.on) p.age = 0;
    if (this.polesDirty) this.updatePoles(true); else if (this.glideDirty) this.updatePoles(false);
    this.energy = Esum;
    if (!anyAct && !contactOn) this.idle = true;
  }
}

// ---------------------------------------------------------------- classic strokes as patch sets (canonical geometry)
// theta = 0 is the side of the head nearest the player. rhoEdge = where the knuckle line sits for edge strokes.
// Used by the lesson playback, the recorder playback and the demo renders; live play sends the measured geometry.
export let BASS_PADS_U = 0.6;   // the bass's trailing finger pads land at this x u (tools/interaction.mjs sends the same)
export function setBassPadsU(v) { BASS_PADS_U = v; }
export const STROKES = {
  open:  (u) => [{ part: 'fingers', u, pts: line(0.97, 0.50, 0) }],
  bass:  (u) => [{ part: 'palm', u, pts: [{ rho: 0.10, theta: 0 }] }, { part: 'pads', u: u * BASS_PADS_U, dly: 0.004, pts: [{ rho: 0.62, theta: Math.PI }] }],
  slap:  (u) => [{ part: 'base', u: u * 0.8, pts: [{ rho: 0.94, theta: 0 }] }, { part: 'tips', u, dly: 0.0008, pts: [{ rho: 0.58, theta: -0.26 }, { rho: 0.55, theta: 0 }, { rho: 0.58, theta: 0.26 }] }],
  heel:  (u) => [{ part: 'heel', u, pts: [{ rho: 0.30, theta: 0 }] }],
  toe:   (u) => [{ part: 'pads', u, pts: line(0.62, 0.40, Math.PI) }],
  touch: (u) => [{ part: 'pads', u, pts: [{ rho: 0.75, theta: 0 }] }],
};
export function line(r0, r1, th) { const out = []; for (let i = 0; i < MAX_PTS; i++) out.push({ rho: r0 + (r1 - r0) * (i / (MAX_PTS - 1)), theta: th }); return out; }
// rests: what stays on the head for a muted stroke
export const RESTS = {
  mute:   [{ part: 'fingers', rho: 0.75, theta: 0 }, { part: 'fingers', rho: 0.6, theta: 0 }],
  slapClosed: [{ part: 'tips', rho: 0.56, theta: 0 }, { part: 'base', rho: 0.94, theta: 0 }],
  heel:   [{ part: 'heel', rho: 0.30, theta: 0 }],
  palm:   [{ part: 'palm', rho: 0.15, theta: 0 }],
};
// Schedule a classic stroke on the engine. hand 0 = left, 1 = right. Returns the samples used.
export function playStroke(eng, at, drum, hand, kind, u, { mute = false, muteAfter = 0.012, muteFor = 0.25, theta = 0 } = {}) {
  const sr = eng.sr;
  const k = kind === 'mute' ? 'open' : kind === 'slapClosed' ? 'slap' : kind;
  for (const p of STROKES[k](u)) eng.patch(at + Math.round((p.dly || 0) * sr), drum, hand, p.part, p.u, p.pts.map((q) => ({ rho: q.rho, theta: q.theta + theta })));
  const restKind = kind === 'mute' ? 'mute' : kind === 'slapClosed' ? 'slapClosed' : mute ? (k === 'bass' ? 'palm' : k === 'heel' ? 'heel' : 'mute') : null;
  if (restKind) { const r0 = at + Math.round(muteAfter * sr); eng.rest(r0, drum, hand, RESTS[restKind].map((q) => ({ ...q, theta: q.theta + theta }))); eng.lift(r0 + Math.round(muteFor * sr), drum, hand); }
}
