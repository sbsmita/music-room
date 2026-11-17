// room-engine.mjs (v2, revision after the independent critique): reference DSP for the Unity "RoomEngine" +
// "MasterBusV2" (spec 10-upgrade/room.md). Block-based, float32 state, every buffer allocated in constructors or
// setRoom (main thread), no allocation in process(), SIMD-friendly layouts (3 bands interleaved in stride-4 rings,
// 32 FDN lines = 8 x float4), seeded RNG, deterministic.
// Coordinates: Unity (x right, y up, z forward). Distances in metres, time in seconds unless noted.
// v1 is kept in v1-snapshot/room-engine.mjs. What changed and why: room.md §R (revision 2).
import { mulberry32, biquadCoeffs } from '../../../Docs/06-audio-dsp.reference.mjs';
export { mulberry32 };
const f32 = Math.fround;

// ---------------------------------------------------------------- constants (sources in room.md §5.1)
export const SR = 48000;
export const C_SOUND = 343.0;            // m/s at 20 °C
export const HEAD_RADIUS = 0.0875;       // m, Woodworth / Brown & Duda spherical head
export const XO_LO = 300, XO_HI = 2400;  // Hz, complementary 3-band split (low | mid | high)
export const BAND_F = [200, 1000, 4500]; // Hz, representative frequency of each band (head shadow, air, Fresnel)
export const OCT = [125, 250, 500, 1000, 2000, 4000];
export const HRTF_FRONT_DB = -3.0;       // our head model + this = about -5 dB per ear for frontal white noise (spec 06 §1.4)
export const AIR_DB_PER_KM = [0.4, 1.3, 2.7, 4.7, 9.9, 29.7]; // ISO 9613-1, 20 °C, 50 % RH (rounded)
export const T_TARGET_1K = 0.45;         // s, furnished living room (Perkin 0.5; Kylliainen 0.5; Burgess 0.33)
// v2: source directivity factor Q toward the player, per band (low | mid | high). Instruments are close to omni in the
// bass and beam more as the wavelength shrinks (trend: Meyer 2009; Otondo & Rindel 2004). Power pattern
// P(theta) = ((1 + cos theta)/2)^(Q - 1): on-axis 1, sphere mean 1/Q. Used by the ER taps AND the late calibration,
// so early and late are consistent (critique issue 1). Axis = source -> head (the direct is always on-axis).
export const SRC_Q = [1.0, 2.0, 2.5];
// v2: ER origin = the body's acoustic centre, as an offset (m, world y) from the group's HRTF point. Harp: the soundbox
// runs down to the floor; kit: kick and floor tom sit low. The piano case sits at key height (spec 05 §2.2), so 0.
export const ER_CENTRE_DY = [0.0, -0.35, -0.2];
export const LATE_EARLY_TRIM_DB = 0.0;   // extra late trim for ER/late overlap; 0 with the long lines (room.md §R1)
export const LATE_LF_DB = 0.0;           // extra low-band late gain (boundary loading / modal build-up), dB

// Distance law: inverse, ref 0.6, rolloff 1.1 (= Unity Logarithmic min 0.6, Rolloff Scale 1.1).
export const distGain = (d) => 0.6 / (0.6 + 1.1 * (Math.max(d, 0.6) - 0.6));
export const dirAmp = (cosT, Q) => Math.pow(Math.max(0, 0.5 * (1 + cosT)), 0.5 * (Q - 1)); // amplitude of P(theta)

// ---------------------------------------------------------------- materials (alpha = absorption, s = scattering)
// Octave bands 125..4000 Hz. "meta:" = Meta XR Audio SDK 85 preset (MetaXRAcousticMaterialProperties.cs);
// "classic:" = standard tabulated value; "est:" = our estimate (see room.md §4.2).
export const MAT = {
  plasterLath: { a: [0.14, 0.10, 0.06, 0.05, 0.04, 0.03], s: [0.10, 0.11, 0.12, 0.13, 0.14, 0.15], src: 'classic: gypsum/lime plaster on lath; s = meta GypsumBoard' },
  gypsum:      { a: [0.29, 0.10, 0.05, 0.04, 0.07, 0.09], s: [0.10, 0.11, 0.12, 0.13, 0.14, 0.15], src: 'meta GypsumBoard (= classic 1/2" board on studs)' },
  woodFloor:   { a: [0.15, 0.11, 0.10, 0.07, 0.06, 0.07], s: [0.10, 0.10, 0.10, 0.10, 0.10, 0.15], src: 'meta WoodFloor (= classic wood floor on joists)' },
  rug:         { a: [0.02, 0.06, 0.14, 0.37, 0.48, 0.63], s: [0.10, 0.15, 0.20, 0.25, 0.35, 0.50], src: 'meta CarpetHeavy' },
  glass:       { a: [0.35, 0.25, 0.18, 0.12, 0.07, 0.05], s: [0.05, 0.05, 0.05, 0.05, 0.05, 0.05], src: 'meta Glass (= classic large pane)' },
  brick:       { a: [0.02, 0.02, 0.03, 0.04, 0.05, 0.07], s: [0.20, 0.25, 0.30, 0.35, 0.40, 0.45], src: 'meta Brick' },
  firebox:     { a: [0.30, 0.40, 0.50, 0.50, 0.50, 0.50], s: [0.50, 0.50, 0.50, 0.50, 0.50, 0.50], src: 'est: open firebox + flue' },
  books:       { a: [0.10, 0.15, 0.25, 0.30, 0.30, 0.30], s: [0.30, 0.40, 0.50, 0.60, 0.70, 0.70], src: 'est: filled bookshelf' },
  curtain:     { a: [0.07, 0.31, 0.49, 0.75, 0.70, 0.60], s: [0.10, 0.15, 0.20, 0.30, 0.40, 0.50], src: 'meta Curtain (only if the Room module adds curtains)' },
};
export const MAT_KEYS = Object.keys(MAT);
// Objects: equivalent absorption area in m2 (sabins) per octave.
export const OBJECTS = [
  { name: 'loveseat (2-seat upholstered)', A: [0.35, 0.75, 1.25, 1.55, 1.65, 1.65], src: 'est: ~2.5 upholstered seat-equivalents' },
  { name: 'bench cushion', A: [0.02, 0.04, 0.08, 0.12, 0.14, 0.14], src: 'est' },
  { name: 'seated player', A: [0.15, 0.25, 0.40, 0.45, 0.45, 0.45], src: 'est: clothed seated adult' },
  { name: 'drum kit', A: [0.30, 0.25, 0.15, 0.10, 0.10, 0.10], src: 'est: heads and shells' },
  { name: 'piano, harp, tables, lamp, 6 plants', A: [0.20, 0.20, 0.20, 0.20, 0.25, 0.25], src: 'est' },
];
// Unseen soft furnishings / openings that a lived-in room has (curtains, cushions, doorway): spectral shape,
// scaled so Eyring T(1 kHz) = T_TARGET_1K. Reported in room.md as the gap between the visible room and real rooms.
export const RESIDUAL_SHAPE = [0.25, 0.45, 0.75, 0.95, 1.0, 1.0];

// ---------------------------------------------------------------- room geometry (Unity coordinates)
// Walls: 0 x-min (left), 1 x-max (right, window), 2 y-min (floor), 3 y-max (ceiling), 4 z-min (fireplace,
// behind the player), 5 z-max (behind the piano). In-plane (u,v): x walls (z,y), y walls (x,z), z walls (x,y).
// Regions are blended in order (first has priority) with soft edges (v2: Fresnel-zone feather, §5.3).
export const VR_ROOM = {
  min: [-3.2, 0.0, -4.08], max: [3.2, 2.9, 1.12],
  walls: [
    { base: 'plasterLath', regions: [{ u: [-3.23, -2.13], v: [0, 1.9], mat: 'books' }] },
    { base: 'plasterLath', regions: [{ u: [-2.08, -0.28], v: [0.7, 2.4], mat: 'glass' }] },
    { base: 'woodFloor', regions: [{ u: [-2.3, 2.3], v: [-2.15, 1.05], mat: 'rug' }] },
    { base: 'gypsum', regions: [] },
    { base: 'plasterLath', regions: [{ u: [-0.15, 0.75], v: [0, 0.78], mat: 'firebox' }, { u: [-0.53, 1.13], v: [0, 2.9], mat: 'brick' }] },
    { base: 'plasterLath', regions: [] },
  ],
  objects: OBJECTS,
  tTarget1k: T_TARGET_1K,
};
// Mixed reality: the MRUK room box, generic finishes (no furniture known); same target logic, T from volume.
export function mrRoom(size) {
  const [w, h, d] = size;
  const V = w * h * d;
  return {
    min: [-w / 2, 0, -d / 2], max: [w / 2, h, d / 2],
    walls: [0, 1, 2, 3, 4, 5].map((i) => ({ base: i === 2 ? 'woodFloor' : i === 3 ? 'gypsum' : 'plasterLath', regions: [] })),
    objects: [OBJECTS[2]],
    tTarget1k: Math.min(0.6, Math.max(0.35, 0.3 + 0.0015 * V)),
  };
}

const AXIS_UV = [[2, 1], [2, 1], [0, 2], [0, 2], [0, 1], [0, 1]]; // wall -> (u axis, v axis)
export function wallMaterial(room, wall, u, v) { // hard lookup (inventory, Meta export); the taps use softWallFactor
  const w = room.walls[wall];
  for (const r of w.regions) if (u >= r.u[0] && u <= r.u[1] && v >= r.v[0] && v <= r.v[1]) return r.mat;
  return w.base;
}
const toBands3 = (o) => [(o[0] + o[1]) / 2, (o[2] + o[3]) / 2, (o[4] + o[5]) / 2];

// Region table for the taps (main thread, at setRoom): rect edges that touch or pass the wall boundary become
// +-Infinity so they are never feathered (the bookshelf stands on the floor, the brick reaches the ceiling).
export function prepareRegions(room) {
  return room.walls.map((w, wall) => {
    const [ua, va] = AXIS_UV[wall];
    const uLo = room.min[ua], uHi = room.max[ua], vLo = room.min[va], vHi = room.max[va];
    return {
      base: MAT_KEYS.indexOf(w.base),
      rects: w.regions.map((r) => ({
        u0: r.u[0] <= uLo + 1e-6 ? -Infinity : r.u[0], u1: r.u[1] >= uHi - 1e-6 ? Infinity : r.u[1],
        v0: r.v[0] <= vLo + 1e-6 ? -Infinity : r.v[0], v1: r.v[1] >= vHi - 1e-6 ? Infinity : r.v[1], mat: MAT_KEYS.indexOf(r.mat),
      })),
    };
  });
}
// smooth 0..1 membership of x in [a, b] with half-width h around each edge (0.5 exactly on the edge)
const sstep = (t) => (t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t));
const softIn = (x, a, b, h) => sstep((x - a + h) / (2 * h)) * sstep((b + h - x) / (2 * h));

// ---------------------------------------------------------------- statistical room acoustics (Eyring)
// Returns per-octave T (s), 3-band T, areas, the residual absorption and the inventory, for the spec.
export function roomAcoustics(room) {
  const L = [0, 1, 2].map((a) => room.max[a] - room.min[a]);
  const V = L[0] * L[1] * L[2];
  const wallSize = [[L[2], L[1]], [L[2], L[1]], [L[0], L[2]], [L[0], L[2]], [L[0], L[1]], [L[0], L[1]]];
  const area = {}; let S = 0;
  for (let w = 0; w < 6; w++) {
    const [su, sv] = wallSize[w]; let rest = su * sv; S += rest;
    const claimed = [];
    for (const r of room.walls[w].regions) {
      // region area minus overlaps with earlier regions (rects here are nested or disjoint)
      let a = (r.u[1] - r.u[0]) * (r.v[1] - r.v[0]);
      for (const c of claimed) {
        const ou = Math.max(0, Math.min(r.u[1], c.u[1]) - Math.max(r.u[0], c.u[0]));
        const ov = Math.max(0, Math.min(r.v[1], c.v[1]) - Math.max(r.v[0], c.v[0]));
        a -= ou * ov;
      }
      claimed.push(r); area[r.mat] = (area[r.mat] || 0) + a; rest -= a;
    }
    area[room.walls[w].base] = (area[room.walls[w].base] || 0) + rest;
  }
  const m = AIR_DB_PER_KM.map((x) => x / 4342.9);
  const surfA = OCT.map((_, b) => Object.entries(area).reduce((s, [k, a]) => s + a * MAT[k].a[b], 0));
  const surfS = OCT.map((_, b) => Object.entries(area).reduce((s, [k, a]) => s + a * MAT[k].s[b], 0) / S); // area-mean scattering
  const objA = OCT.map((_, b) => room.objects.reduce((s, o) => s + o.A[b], 0));
  const eyring = (Aextra) => OCT.map((_, b) => {
    const abar = Math.min(0.95, (surfA[b] + objA[b] + Aextra[b]) / S);
    return 0.161 * V / (-S * Math.log(1 - abar) + 4 * m[b] * V);
  });
  const tVisible = eyring([0, 0, 0, 0, 0, 0]);
  // Solve the residual scale k so T(1 kHz) = target (bisection; T falls monotonically with k).
  let lo = 0, hi = 200;
  for (let i = 0; i < 60; i++) { const k = (lo + hi) / 2; const t = eyring(RESIDUAL_SHAPE.map((s) => s * k))[3]; if (t > room.tTarget1k) lo = k; else hi = k; }
  const k = tVisible[3] <= room.tTarget1k ? 0 : (lo + hi) / 2;
  const resA = RESIDUAL_SHAPE.map((s) => s * k);
  const T = eyring(resA);
  const Aeff = OCT.map((_, b) => 0.161 * V / T[b]); // Eyring-equivalent absorption area incl. air
  const abarEy = Aeff.map((A) => 1 - Math.exp(-A / S)); // Eyring mean absorption that reproduces T
  return { V, S, dims: L, area, surfA, surfS, objA, resA, residualScale: k, airM: m, tVisible, T, T3: toBands3(T), Aeff, Aeff3: toBands3(Aeff), abarEy, abarEy3: toBands3(abarEy), surfS3: toBands3(surfS) };
}

// ---------------------------------------------------------------- spherical head (direct path in renders, ER band gains)
// Brown & Duda 1998: H(w,theta) = (1 + j a w/(2 w0)) / (1 + j w/(2 w0)), w0 = c/a,
// a(theta) = (1 + amin/2) + (1 - amin/2) cos(theta/thetaMin * pi), amin 0.1, thetaMin 150 deg; theta from the ear axis.
export const bdAlpha = (theta) => (1 + 0.05) + (1 - 0.05) * Math.cos((theta / (150 * Math.PI / 180)) * Math.PI);
export function bdMag(alpha, f) { const x = (2 * Math.PI * f) / (2 * C_SOUND / HEAD_RADIUS); return Math.sqrt((1 + alpha * alpha * x * x) / (1 + x * x)); }
export function bdCoefs(alpha, sr) { // bilinear transform: y = b0 x + b1 x1 - a1 y1
  const w0x2 = 2 * C_SOUND / HEAD_RADIUS, K = 2 * sr, den = w0x2 + K;
  return [(w0x2 + alpha * K) / den, (w0x2 - alpha * K) / den, (w0x2 - K) / den];
}
export const woodworthItd = (lat) => (HEAD_RADIUS / C_SOUND) * (lat + Math.sin(lat)); // s, lat in [-pi/2, pi/2]
// Diffuse-field average of |H|^2 per band (uniform directions; theta pdf sin/2), relative to frontal |H|^2.
export function diffuseHeadGain3() {
  return BAND_F.map((f) => {
    let s = 0, w = 0;
    for (let i = 0; i < 1800; i++) { const th = (i + 0.5) * Math.PI / 1800, p = Math.sin(th); s += p * bdMag(bdAlpha(th), f) ** 2; w += p; }
    return (s / w) / bdMag(bdAlpha(Math.PI / 2), f) ** 2;
  });
}
export const frontHeadGain3 = () => BAND_F.map((f) => bdMag(bdAlpha(Math.PI / 2), f) ** 2); // frontal |H|^2 per band

// ---------------------------------------------------------------- one-pole helper
export const onePoleK = (fc, sr) => 1 - Math.exp(-2 * Math.PI * fc / sr);
// causal octave band filter for the late calibration (main thread): 2 x Butterworth HP at f/sqrt2, 2 x LP at f sqrt2
export function bw2(type, f, sr) {
  const w = 2 * Math.PI * f / sr, c = Math.cos(w), al = Math.sin(w) / (2 * Math.SQRT1_2), a0 = 1 + al;
  const b = type === 'lp' ? [(1 - c) / 2, 1 - c, (1 - c) / 2] : [(1 + c) / 2, -(1 + c), (1 + c) / 2];
  return { b: b.map((v) => v / a0), a: [1, -2 * c / a0, (1 - al) / a0] };
}
export function octaveCausal(x, f, sr) {
  const hp = bw2('hp', f / Math.SQRT2, sr), lp = bw2('lp', Math.min(f * Math.SQRT2, sr * 0.45), sr);
  let y = Float64Array.from(x);
  for (const q of [hp, hp, lp, lp]) { let x1 = 0, x2 = 0, y1 = 0, y2 = 0; for (let i = 0; i < y.length; i++) { const v = y[i], o = q.b[0] * v + q.b[1] * x1 + q.b[2] * x2 - q.a[1] * y1 - q.a[2] * y2; x2 = x1; x1 = v; y2 = y1; y1 = o; y[i] = o; } }
  return y;
}
export const DC_HZ = 15;                 // v2: floor for the ER high-pass (a room returns no DC; critique 06d)
// v2: 2nd-order high-pass (two cascaded 1st-order sections) on the late input. Below ~60 Hz this room has single, widely spaced modes (first axial mode
// 26.8 Hz, about 7 modes below 63 Hz), not the dense diffuse field an FDN implies; without it the matched renders came
// out +3 to +4 dB at 63 Hz against dry (boom). Also the late path's DC blocker.
export const LATE_HP_HZ = 50;
// v2: the image-source ER are high-passed (1st order) at the room's Schroeder frequency f_s = 2000 sqrt(T/V): below it
// the field is modal and a low-order point-source image series is not a valid model (it digs deterministic floor-
// bounce notches at 100-160 Hz, critique issue 3). The diffuse late field carries the room's bass there.
export const schroederHz = (T1k, V) => 2000 * Math.sqrt(T1k / V);
export const IDLE_EPS = 1e-7;            // input below this counts as silence (idle skip, denormal safety)

// ---------------------------------------------------------------- image sources (Allen & Berkley 1979)
// For order <= maxOrder: per axis x' = b0 + (1-2q) (x - b0) + 2 m L, q in {0,1}; reflections off the low wall
// |m - q|, off the high wall |m|. Materials come from the folded crossing points (position-dependent).
export function enumerateImages(maxOrder) {
  const ax = [];
  for (let m = -maxOrder; m <= maxOrder; m++) for (let q = 0; q <= 1; q++) { const o = Math.abs(m - q) + Math.abs(m); if (o <= maxOrder) ax.push([m, q, o]); }
  const list = [];
  for (const a of ax) for (const b of ax) for (const c of ax) {
    const o = a[2] + b[2] + c[2];
    if (o >= 1 && o <= maxOrder) list.push([a[0], a[1], b[0], b[1], c[0], c[1], o]);
  }
  list.sort((p, q) => p[6] - q[6] || p[0] - q[0] || p[1] - q[1] || p[2] - q[2] || p[3] - q[3] || p[4] - q[4] || p[5] - q[5]);
  return list; // 24 for order 2, 62 for order 3
}
const foldCoord = (x, b0, L) => { let u = (x - b0) % (2 * L); if (u < 0) u += 2 * L; return b0 + (u > L ? 2 * L - u : u); };

// Band reflection factor (pressure, specular part) per material index: sqrt((1-alpha)(1-s)).
function bounceTable() {
  const t = new Float32Array(MAT_KEYS.length * 3);
  MAT_KEYS.forEach((k, i) => { const a3 = toBands3(MAT[k].a), s3 = toBands3(MAT[k].s); for (let b = 0; b < 3; b++) t[i * 3 + b] = Math.sqrt((1 - a3[b]) * (1 - s3[b])); });
  return t;
}
export const JUMP_SAMPLES = 8; // v2: a tap delay change above this per block crossfades instead of ramping (no chirp)
export const XF_BLOCKS = 2;    // crossfade length in blocks (10.7 ms at 256 frames)

// One group (instrument): input ring (3 bands interleaved, stride 4), up to MAXI taps for 2 ears.
// Tap layout per image (stride 8 floats): dL, dR (samples), gL0, gL1, gL2, gR0, gR1, gR2.
// Work arrays hold 2 x MAXI taps so a crossfade block can run the old and the new tap set together.
export class ImageSourceER {
  constructor(sr = SR, maxOrder = 2, ringLen = 4096) {
    this.sr = sr; this.images = enumerateImages(maxOrder); this.maxI = this.images.length;
    this.ring = new Float32Array(ringLen * 4); this.ringLen = ringLen; this.mask = ringLen - 1; this.w = 0;
    this.kLo = onePoleK(XO_LO, sr); this.kHi = onePoleK(XO_HI, sr); this.kDc = onePoleK(DC_HZ, sr); this.lpA = 0; this.lpB = 0; this.dc = 0;
    this.cur = new Float32Array(this.maxI * 8); this.tgt = new Float32Array(this.maxI * 8);
    this.wc = new Float32Array(this.maxI * 16); this.winc = new Float32Array(this.maxI * 16);
    this.count = 0; this.fresh = true; this.bounce = bounceTable(); this.regions = null; this.regionsOf = null;
    this.old = new Float32Array(this.maxI * 8); this.oldCount = 0; this.xf = 0; // crossfade: blocks done, 0 = none
    this.idle = ringLen; this.zeroed = true; this.enabled = true;
    this.stats = { crossfades: 0, skippedBlocks: 0 }; this.srcQ = SRC_Q;
  }
  setHighpass(hz) { this.kDc = onePoleK(Math.max(DC_HZ, hz), this.sr); this.hpHz = hz; } // RoomEngine sets f_s at setRoom
  // Block start (audio thread, from the latest pose) or main thread: compute target taps for source src (the group's
  // HRTF point; the acoustic-centre offset is added by the caller), head pose (pos, right).
  update(room, src, headPos, right, gainScale = 1) {
    if (this.regionsOf !== room) { this.regions = prepareRegions(room); this.regionsOf = room; }
    const b0 = room.min, L = [0, 1, 2].map((a) => room.max[a] - room.min[a]);
    const clampIn = (p) => [0, 1, 2].map((a) => Math.min(room.max[a] - 0.05, Math.max(room.min[a] + 0.05, p[a])));
    const s = clampIn(src), h = clampIn(headPos);
    // source axis (toward the head) for the directivity
    let ax = [h[0] - s[0], h[1] - s[1], h[2] - s[2]]; const al = Math.hypot(...ax) || 1; ax = ax.map((v) => v / al);
    const gH = Math.pow(10, HRTF_FRONT_DB / 20), sr = this.sr, bt = this.bounce;
    let n = 0;
    for (const im of this.images) {
      const sg = [1 - 2 * im[1], 1 - 2 * im[3], 1 - 2 * im[5]];
      const I = [0, 1, 2].map((a) => b0[a] + sg[a] * (s[a] - b0[a]) + 2 * im[2 * a] * L[a]);
      const D = [I[0] - h[0], I[1] - h[1], I[2] - h[2]];
      const d = Math.hypot(D[0], D[1], D[2]);
      const R = [1, 1, 1];
      for (let a = 0; a < 3; a++) {
        if (D[a] === 0) continue;
        const cell = Math.floor((I[a] - b0[a]) / L[a]);
        const k0 = cell > 0 ? 1 : cell + 1, k1 = cell > 0 ? cell : 0; // crossing plane indices k: plane at b0 + k L
        for (let k = k0; k <= k1; k++) {
          const t = (b0[a] + k * L[a] - h[a]) / D[a];
          const P = [h[0] + t * D[0], h[1] + t * D[1], h[2] + t * D[2]];
          const wall = 2 * a + (((k % 2) + 2) % 2 === 0 ? 0 : 1);
          const [ua, va] = AXIS_UV[wall];
          const u = foldCoord(P[ua], b0[ua], L[ua]), v = foldCoord(P[va], b0[va], L[va]);
          const reg = this.regions[wall], ft = d * t * (1 - t); // Fresnel: r_F = sqrt(lambda d1 d2 / (d1 + d2))
          for (let b = 0; b < 3; b++) {
            const hw = Math.max(0.02, Math.sqrt((C_SOUND / BAND_F[b]) * ft));
            let rem = 1, fct = 0;
            for (const r of reg.rects) { const wgt = softIn(u, r.u0, r.u1, hw) * softIn(v, r.v0, r.v1, hw); fct += rem * wgt * bt[r.mat * 3 + b]; rem *= 1 - wgt; }
            R[b] *= fct + rem * bt[reg.base * 3 + b];
          }
        }
      }
      const ux = D[0] / d, uy = D[1] / d, uz = D[2] / d;
      // emission direction at the real source = the image's ray toward the head, mirrored back (sign per axis)
      const cosT = -(sg[0] * ux * ax[0] + sg[1] * uy * ax[1] + sg[2] * uz * ax[2]);
      const cr = Math.max(-1, Math.min(1, ux * right[0] + uy * right[1] + uz * right[2]));
      const lat = Math.asin(cr), itd = woodworthItd(lat);
      const thR = Math.acos(cr), thL = Math.PI - thR;
      const g = distGain(d) * gH * gainScale;
      const o = n * 8;
      this.tgt[o] = (d / C_SOUND + itd / 2) * sr; this.tgt[o + 1] = (d / C_SOUND - itd / 2) * sr;
      for (let b = 0; b < 3; b++) {
        const air = Math.pow(10, -(AIR_DB_PER_KM[2 * b + 1] / 1000) * d / 20), dir = dirAmp(cosT, this.srcQ[b]);
        this.tgt[o + 2 + b] = g * R[b] * air * dir * bdMag(bdAlpha(thL), BAND_F[b]);
        this.tgt[o + 5 + b] = g * R[b] * air * dir * bdMag(bdAlpha(thR), BAND_F[b]);
      }
      n++;
    }
    this.count = n;
    if (this.fresh) { this.cur.set(this.tgt); this.fresh = false; this.xf = 0; return; }
    if (this.xf > 0) return; // a crossfade is running: the new set keeps ramping toward tgt (normal small steps)
    let jump = 0; for (let t = 0; t < n; t++) jump = Math.max(jump, Math.abs(this.tgt[t * 8] - this.cur[t * 8]), Math.abs(this.tgt[t * 8 + 1] - this.cur[t * 8 + 1]));
    if (jump > JUMP_SAMPLES) { // freeze what is audible as the old set; the new set starts at its target, silent
      this.old.set(this.cur.subarray(0, n * 8)); this.oldCount = n; this.cur.set(this.tgt.subarray(0, n * 8)); this.xf = 1; this.stats.crossfades++;
    }
  }
  // Audio thread: add this group's early reflections for x[0..n) into outL/outR. Normal block: taps ramp cur -> tgt.
  // Crossfade (delay jump, XF_BLOCKS blocks): the old taps fade out at their old delays while the new taps fade in.
  process(x, outL, outR, n) {
    let peak = 0; for (let i = 0; i < n; i++) { const a = Math.abs(x[i]); if (a > peak) peak = a; }
    this.idle = peak < IDLE_EPS ? Math.min(this.idle + n, 1 << 30) : 0;
    if (!this.enabled || this.idle >= this.ringLen) { // idle skip: everything in the ring is below IDLE_EPS
      if (!this.zeroed) { this.ring.fill(0); this.lpA = this.lpB = this.dc = 0; this.zeroed = true; }
      this.cur.set(this.tgt.subarray(0, this.count * 8), 0); this.xf = 0; this.stats.skippedBlocks++;
      return;
    }
    this.zeroed = false;
    const ring = this.ring, mask = this.mask, cur = this.cur, tgt = this.tgt, wc = this.wc, inc = this.winc, cnt = this.count;
    const inv = 1 / n;
    let nt = cnt;
    if (this.xf > 0) { // taps [0, oc): old set, fixed delays, gains x (1 - p) ; taps [oc, oc + cnt): new set x p
      const p0 = (this.xf - 1) / XF_BLOCKS, p1 = this.xf / XF_BLOCKS, old = this.old, oc = this.oldCount;
      for (let t = 0; t < oc; t++) { const o = t * 8; wc[o] = old[o]; wc[o + 1] = old[o + 1]; inc[o] = 0; inc[o + 1] = 0;
        for (let k = 2; k < 8; k++) { wc[o + k] = old[o + k] * (1 - p0); inc[o + k] = (old[o + k] * (1 - p1) - wc[o + k]) * inv; } }
      for (let t = 0; t < cnt; t++) { const o = t * 8, q = (oc + t) * 8; wc[q] = cur[o]; wc[q + 1] = cur[o + 1]; inc[q] = (tgt[o] - cur[o]) * inv; inc[q + 1] = (tgt[o + 1] - cur[o + 1]) * inv;
        for (let k = 2; k < 8; k++) { wc[q + k] = cur[o + k] * p0; inc[q + k] = (tgt[o + k] * p1 - wc[q + k]) * inv; } }
      nt = oc + cnt; this.xf = this.xf === XF_BLOCKS ? 0 : this.xf + 1;
    } else {
      for (let j = 0; j < cnt * 8; j++) { wc[j] = cur[j]; inc[j] = (tgt[j] - cur[j]) * inv; }
    }
    let lpA = this.lpA, lpB = this.lpB, dc = this.dc, w = this.w;
    const kLo = this.kLo, kHi = this.kHi, kDc = this.kDc;
    for (let i = 0; i < n; i++) {
      let v = x[i];
      dc += kDc * (v - dc); v -= dc;
      lpA += kLo * (v - lpA); const r = v - lpA; lpB += kHi * (r - lpB);
      const o4 = w * 4; ring[o4] = lpA; ring[o4 + 1] = lpB; ring[o4 + 2] = r - lpB;
      let yl = 0, yr = 0;
      for (let t = 0; t < nt; t++) {
        const o = t * 8;
        for (let k = 0; k < 8; k++) wc[o + k] += inc[o + k];
        // left ear
        let d = wc[o], di = Math.floor(d), fr = d - di;
        let p0 = ((w - di) & mask) * 4, p1 = ((w - di - 1) & mask) * 4;
        yl += wc[o + 2] * (ring[p0] + fr * (ring[p1] - ring[p0])) + wc[o + 3] * (ring[p0 + 1] + fr * (ring[p1 + 1] - ring[p0 + 1])) + wc[o + 4] * (ring[p0 + 2] + fr * (ring[p1 + 2] - ring[p0 + 2]));
        d = wc[o + 1]; di = Math.floor(d); fr = d - di;
        p0 = ((w - di) & mask) * 4; p1 = ((w - di - 1) & mask) * 4;
        yr += wc[o + 5] * (ring[p0] + fr * (ring[p1] - ring[p0])) + wc[o + 6] * (ring[p0 + 1] + fr * (ring[p1 + 1] - ring[p0 + 1])) + wc[o + 7] * (ring[p0 + 2] + fr * (ring[p1 + 2] - ring[p0 + 2]));
      }
      outL[i] += yl; outR[i] += yr;
      w = (w + 1) & mask;
    }
    this.lpA = f32(lpA); this.lpB = f32(lpB); this.dc = f32(dc); this.w = w;
    cur.set(tgt.subarray(0, cnt * 8), 0); // land exactly on target (no drift)
  }
}

// ---------------------------------------------------------------- late reverb: 32-line FDN (Jot & Chaigne 1991)
// v2: 32 primes, geometric ~800..4000 samples (16.6..83 ms; total 64048 = 1.33 s of delay, modal density 1.33 /Hz,
// modal overlap 5.1-7.7 > Schroeder's 3). v1 was half as long (critique issue 5; experiments/fdn-v2-sweep.mjs).
export const FDN_N = 32;
export const FDN_LEN_48K = [797, 839, 887, 937, 983, 1039, 1093, 1153, 1213, 1283, 1361, 1423, 1487, 1571, 1657, 1753, 1831, 1933, 2039, 2137, 2251, 2371, 2503, 2633, 2797, 2917, 3083, 3253, 3413, 3607, 3803, 4001];
export const DIFF_LEN_48K = [37, 53, 71, 97]; // short: long Schroeder allpasses ring like combs (+1 dB peakiness)
export const DIFF_G = [0.5, 0.5, 0.5, 0.5];
export const COHERENCE_FC = 400; // Hz: IC(f) = 1 / (1 + (f/fc)^2) for the late field (Menzer & Faller 2009 style)
// v2: output vectors. cL = seeded random signs, cR = cL (elementwise) x Sylvester-Hadamard row 1: exactly orthogonal,
// and no output re-enters a single line (max |H c| / 32 = 0.25, the minimum for N = 32). v1 used Hadamard rows 5/10,
// for which cL^T A = e5^T exactly: a recirculating comb at the line-5 delay (critique issue 5).
export const OUT_SEED = 514, OUT_ROW = 1;
// v2: the high band (2.4 kHz up to Nyquist) decays with T(4 kHz), fitted at 4 kHz: v1 used the 2k/4k mean at 2828 Hz,
// which left the tail 3.6 dB long at 4 kHz and above (a bright, 'tinny' tail risk). FDN_T3 maps the octave T to bands.
export const FIT_F = [177, 707, 4000]; // Hz: geometric centre of 125+250, of 500+1k; 4 kHz
export const fdnT3 = (T) => [(T[0] + T[1]) / 2, (T[2] + T[3]) / 2, T[5]];
// |gL LP_lo + (1 - LP_lo)(gM LP_hi + gH (1 - LP_hi))| in dB at f, one-pole LP(z) = k / (1 - (1-k) z^-1); gains in dB.
export function absorbMagDb(gDb, f, kLo, kHi, sr) {
  const w = 2 * Math.PI * f / sr, c = Math.cos(w), s = -Math.sin(w); // z^-1 = c + j s
  const lp = (k) => { const dr = 1 - (1 - k) * c, di = -(1 - k) * s, m = dr * dr + di * di; return [k * dr / m, -k * di / m]; };
  const [aR, aI] = lp(kLo), [bR, bI] = lp(kHi);
  const [gL, gM, gH] = gDb.map((d) => Math.pow(10, d / 20));
  const inR = gM * bR + gH * (1 - bR), inI = gM * bI - gH * bI;         // gM LPb + gH (1 - LPb)
  const hpR = 1 - aR, hpI = -aI;                                       // 1 - LPa
  const yR = gL * aR + (hpR * inR - hpI * inI), yI = gL * aI + (hpR * inI + hpI * inR);
  return 10 * Math.log10(yR * yR + yI * yI);
}
export const hRow = (r, N = FDN_N) => Array.from({ length: N }, (_, j) => { let x = r & j, c = 0; while (x) { c += x & 1; x >>= 1; } return c & 1 ? -1 : 1; });
const isPrime = (n) => { if (n < 2) return false; for (let i = 2; i * i <= n; i++) if (n % i === 0) return false; return true; };
export const nearestPrime = (n) => { for (let d = 0; ; d++) { if (isPrime(n - d)) return n - d; if (isPrime(n + d)) return n + d; } };
export function outputVectors(seed = OUT_SEED, row = OUT_ROW, N = FDN_N) {
  const r = mulberry32(seed), sc = 1 / Math.sqrt(N), h = hRow(row, N);
  const sL = Array.from({ length: N }, () => (r() < 0.5 ? -1 : 1));
  return { cL: Float32Array.from(sL, (v) => v * sc), cR: Float32Array.from(sL, (v, j) => v * h[j] * sc) };
}

export class LateFDN {
  constructor(sr = SR, seed = 7, { lens = FDN_LEN_48K, outSeed = OUT_SEED, outRow = OUT_ROW } = {}) {
    this.sr = sr; const sc = sr / 48000;
    this.len = Int32Array.from(lens.map((l) => (sc === 1 ? l : nearestPrime(Math.round(l * sc)))));
    const N = FDN_N; this.N = N; const sc0 = 1 / Math.sqrt(N);
    this.lines = Array.from(this.len, (l) => new Float32Array(l)); this.pos = new Int32Array(N);
    this.dlen = Int32Array.from(DIFF_LEN_48K.map((l) => Math.round(l * sc)));
    this.diff = Array.from(this.dlen, (l) => new Float32Array(l)); this.dpos = new Int32Array(4);
    const rng = mulberry32(seed);
    this.b = Float32Array.from({ length: N }, () => (rng() < 0.5 ? -sc0 : sc0));
    const ov = outputVectors(outSeed, outRow, N); this.cL = ov.cL; this.cR = ov.cR;
    this.g = new Float32Array(N * 3);   // per line: gLow, gMid, gHigh at [i*3 .. i*3+2]
    this.sA = new Float32Array(N); this.sB = new Float32Array(N); // band-split states per line
    this.sc = sc0;
    this.kLo = onePoleK(XO_LO, sr); this.kHi = onePoleK(XO_HI, sr); this.kDc = onePoleK(LATE_HP_HZ, sr);
    this.kCoh = onePoleK(COHERENCE_FC, sr); this.cC = 0; this.cL0 = 0; this.cR0 = 0;
    this.y = new Float32Array(N); this.f = new Float32Array(N);
    // v2: input gain and 3-band input EQ (per-band calibration), ramped per block; DC blocker; idle skip
    this.inGain = 1; this.inGainCur = 1; this.eqGain = new Float32Array([1, 1, 1]); this.eqCur = new Float32Array([1, 1, 1]);
    this.eA = 0; this.eB = 0; this.dc = 0; this.dc2 = 0;
    this.idle = 1 << 30; this.idleLimit = Math.round(1.5 * 1.0 * sr); this.zeroed = true;
    this.stats = { skippedBlocks: 0 };
  }
  // Per line: the 3 in-loop gains so the loop's magnitude at FIT_F[b] is -60 dB per T3[b] seconds. The split is
  // first order (soft), so the gains are fitted on its analytic response (8 fixed-point iterations, main thread).
  setDecay(T3) {
    for (let i = 0; i < this.N; i++) {
      const tgt = T3.map((T) => (-60 * this.len[i]) / (this.sr * T)); // dB per pass
      const x = tgt.slice();
      for (let it = 0; it < 8; it++) {
        const h = FIT_F.map((f) => absorbMagDb(x, f, this.kLo, this.kHi, this.sr));
        for (let b = 0; b < 3; b++) x[b] += tgt[b] - h[b];
      }
      for (let b = 0; b < 3; b++) this.g[i * 3 + b] = Math.min(0.99999, Math.pow(10, x[b] / 20));
    }
    this.idleLimit = Math.round(1.5 * Math.max(...T3) * this.sr); // 90 dB of decay
  }
  clear() { for (const l of this.lines) l.fill(0); for (const d of this.diff) d.fill(0); this.sA.fill(0); this.sB.fill(0); this.cC = this.cL0 = this.cR0 = 0; this.eA = this.eB = this.dc = this.dc2 = 0; }
  // x: mono late send (sum of groups); adds stereo late reverb into outL/outR.
  process(x, outL, outR, n, raw = null) {
    let peak = 0; for (let i = 0; i < n; i++) { const a = Math.abs(x[i]); if (a > peak) peak = a; }
    this.idle = peak < IDLE_EPS ? Math.min(this.idle + n, 1 << 30) : 0;
    if (this.idle >= this.idleLimit) { // tail is 90 dB down: zero once (no denormals) and skip
      if (!this.zeroed) { this.clear(); this.zeroed = true; }
      this.inGainCur = this.inGain; this.eqCur.set(this.eqGain); this.stats.skippedBlocks++;
      return;
    }
    this.zeroed = false;
    const { lines, pos, len, diff, dpos, dlen, b, cL, cR, g, sA, sB, y, f, N, sc } = this;
    const kLo = this.kLo, kHi = this.kHi, kC = this.kCoh, kDc = this.kDc, inv = 1 / n;
    let ig = this.inGainCur; const dig = (this.inGain - ig) * inv;
    let e0 = this.eqCur[0], e1 = this.eqCur[1], e2 = this.eqCur[2];
    const d0 = (this.eqGain[0] - e0) * inv, d1 = (this.eqGain[1] - e1) * inv, d2 = (this.eqGain[2] - e2) * inv;
    let cC = this.cC, cL0 = this.cL0, cR0 = this.cR0, eA = this.eA, eB = this.eB, dc = this.dc, dc2 = this.dc2;
    for (let i = 0; i < n; i++) {
      ig += dig; e0 += d0; e1 += d1; e2 += d2;
      let u = x[i] * ig;
      dc += kDc * (u - dc); u -= dc; dc2 += kDc * (u - dc2); u -= dc2;     // 2 x 1st-order HP at 50 Hz (and DC blocker)
      eA += kLo * (u - eA); const er = u - eA; eB += kHi * (er - eB);       // 3-band input EQ
      u = e0 * eA + e1 * eB + e2 * (er - eB);
      for (let k = 0; k < 4; k++) { // Schroeder allpass diffusers in series
        const buf = diff[k], p = dpos[k], dd = buf[p], gk = DIFF_G[k];
        const v = u - gk * dd; buf[p] = v; u = dd + gk * v; dpos[k] = p + 1 === dlen[k] ? 0 : p + 1;
      }
      let l0 = 0, r0 = 0;
      for (let k = 0; k < N; k++) {
        const v = lines[k][pos[k]]; y[k] = v; l0 += cL[k] * v; r0 += cR[k] * v;
        let a = sA[k]; a += kLo * (v - a); const r = v - a; let bb = sB[k]; bb += kHi * (r - bb); sA[k] = a; sB[k] = bb;
        f[k] = g[k * 3] * a + g[k * 3 + 1] * bb + g[k * 3 + 2] * (r - bb);
      }
      // N-point fast Walsh-Hadamard transform, scaled 1/sqrt(N) (orthonormal)
      for (let h = 1; h < N; h <<= 1) for (let s0 = 0; s0 < N; s0 += h << 1) for (let k = s0; k < s0 + h; k++) { const p = f[k], q = f[k + h]; f[k] = p + q; f[k + h] = p - q; }
      for (let k = 0; k < N; k++) { lines[k][pos[k]] = sc * f[k] + b[k] * u; pos[k] = pos[k] + 1 === len[k] ? 0 : pos[k] + 1; }
      if (raw) { raw[0][i] = l0; raw[1][i] = r0; }
      // frequency-dependent interaural coherence: common part below fc, independent above
      const cm = (l0 + r0) * Math.SQRT1_2;
      cC += kC * (cm - cC); cL0 += kC * (l0 - cL0); cR0 += kC * (r0 - cR0);
      outL[i] += cC + (l0 - cL0); outR[i] += cC + (r0 - cR0);
    }
    this.cC = cC; this.cL0 = cL0; this.cR0 = cR0; this.eA = f32(eA); this.eB = f32(eB); this.dc = f32(dc); this.dc2 = f32(dc2);
    this.inGainCur = this.inGain; this.eqCur.set(this.eqGain);
  }
}

// ---------------------------------------------------------------- room amount presets (user setting + blind test)
// dB re the physically calibrated level ("natural"). "web" (stored preset name; RoomAmount.Faint) = the Classic room: no early
// reflections and a faint late field (total room energy about -26 dB re direct at the piano, like the Classic IR, but with
// this room's 0.45 s decay instead of the Classic 2 s noise tail). -Infinity = off (and its CPU skipped).
export const ROOM_PRESETS = {
  off: { erDb: -Infinity, lateDb: -Infinity },
  web: { erDb: -Infinity, lateDb: -16.5 },   // late -25.8 dB re direct at the piano (Classic IR -25.6)
  low: { erDb: -3, lateDb: -4 },
  natural: { erDb: 0, lateDb: 0 },
  high: { erDb: 3, lateDb: 3 },
};
const lin = (db) => (db === -Infinity ? 0 : Math.pow(10, db / 20));

// ---------------------------------------------------------------- the room engine (groups -> ER + late)
export const GROUPS = ['piano', 'harp', 'drums'];
export class RoomEngine {
  // opts (experiments only; the shipped values are the module constants): centreDy, srcQ, lateTrimDb, lateLfDb
  constructor(sr = SR, { maxOrder = 2, seed = 7, fdnOpts = {}, centreDy = ER_CENTRE_DY, srcQ = SRC_Q, lateTrimDb = LATE_EARLY_TRIM_DB, lateLfDb = LATE_LF_DB, erHp = null } = {}) {
    this.centreDy = centreDy; this.srcQ = srcQ; this.lateTrimDb = lateTrimDb; this.lateLfDb = lateLfDb; this.erHp = erHp;
    this.sr = sr; this.er = GROUPS.map(() => { const e = new ImageSourceER(sr, maxOrder); e.srcQ = srcQ; return e; }); this.fdn = new LateFDN(sr, seed, fdnOpts); this.fdnOpts = fdnOpts; this.seed = seed;
    this.lateIn = new Float32Array(8192); this.erTrim = [1, 1, 1]; this.lateTrim = [1, 1, 1]; this.wet = 1;
    this.lateCur = new Float32Array(3); this.lateCurInit = false; this.groupPos = GROUPS.map(() => null); this.preset = 'natural';
  }
  setRoom(room) {
    this.room = room; this.ac = roomAcoustics(room); this.fdnT3 = fdnT3(this.ac.T); this.fdn.setDecay(this.fdnT3);
    this.erHpHz = this.erHp ?? schroederHz(this.ac.T[3], this.ac.V); for (const e of this.er) e.setHighpass(this.erHpHz);
    this.calibrateLate(); // ER taps crossfade to the new room at the next update (no 'fresh' jump with audio in the ring)
  }
  setAmount(name) {
    const p = ROOM_PRESETS[name]; this.preset = name;
    this.erTrim = [1, 1, 1].map(() => lin(p.erDb)); this.lateTrim = [1, 1, 1].map(() => lin(p.lateDb));
    for (const e of this.er) e.enabled = p.erDb !== -Infinity;
  }
  // v2 late calibration (main thread at setRoom), per OCTAVE against classical diffuse theory at the ear:
  //   E_o = distGain(1)^2 * gHrtf^2 * |H_front(f_o)|^2 * <|H|^2>_diff/front(f_o) * 16 pi / (Q_o A'_o)
  //         * (exp(-13.8 * 0.06 / T_o) - exp(-13.8 * 0.3 / T_o)) * eps_o * trim (* lf for 125/250 Hz),
  // the energy in [60, 300] ms after emission (L, R mean) of the late output of a unit impulse through a causal
  // octave filter (2 x Butterworth HP at f/sqrt2 + 2 x LP at f sqrt2), eps_o = that filter's impulse energy,
  // Q_o = SRC_Q of the octave's band. The 3 input-EQ gains are solved so the mean dB error of each band's two
  // octaves (125+250 | 500+1k | 2k+4k) is zero (4 fixed-point iterations; deterministic).
  calibrateLate() {
    const sr = this.sr, ac = this.ac, T3 = this.fdnT3;
    const ref = distGain(1) ** 2 * Math.pow(10, HRTF_FRONT_DB / 10), trim = Math.pow(10, this.lateTrimDb / 10), lf = Math.pow(10, this.lateLfDb / 10);
    const n = Math.round(0.6 * sr), t1 = Math.round(0.06 * sr), t2 = Math.round(0.3 * sr);
    const dh = OCT.map((f) => { let s2 = 0, w = 0; for (let i = 0; i < 1800; i++) { const th = (i + 0.5) * Math.PI / 1800, pp = Math.sin(th); s2 += pp * bdMag(bdAlpha(th), f) ** 2; w += pp; } return (s2 / w) / bdMag(bdAlpha(Math.PI / 2), f) ** 2; });
    const fh = OCT.map((f) => bdMag(bdAlpha(Math.PI / 2), f) ** 2);
    const imp = new Float64Array(n); imp[0] = 1; const eps = OCT.map((f) => octaveCausal(imp, f, sr).reduce((q, v) => q + v * v, 0));
    const target = OCT.map((f, o) => { const T = ac.T[o], q = this.srcQ[o >> 1]; return ref * fh[o] * dh[o] * 16 * Math.PI / (q * ac.Aeff[o]) * (Math.exp(-13.8155 * 0.06 / T) - Math.exp(-13.8155 * 0.3 / T)) * eps[o] * trim * (o < 2 ? lf : 1); });
    const gEq = [1, 1, 1], errDb = OCT.map(() => 0);
    for (let it = 0; it < 4; it++) {
      const f = new LateFDN(sr, this.seed, this.fdnOpts); f.setDecay(T3); f.eqGain.set(gEq); f.eqCur.set(gEq);
      const x = new Float32Array(n), L = new Float32Array(n), R = new Float32Array(n); x[0] = 1; f.process(x, L, R, n);
      OCT.forEach((fc, o) => { const bl = octaveCausal(L, fc, sr), br = octaveCausal(R, fc, sr); let e = 0; for (let i = t1; i < t2; i++) e += (bl[i] * bl[i] + br[i] * br[i]) / 2; errDb[o] = 10 * Math.log10(e / target[o]); });
      for (let b = 0; b < 3; b++) gEq[b] *= Math.pow(10, -(errDb[2 * b] + errDb[2 * b + 1]) / 2 / 20);
    }
    this.fdn.eqGain.set(gEq.map(f32)); this.fdn.inGain = 1;
    if (this.fdn.zeroed) { this.fdn.eqCur.set(this.fdn.eqGain); this.fdn.inGainCur = 1; } // idle: snap (no audio, no click)
    this.lateEq = gEq.slice(); this.lateTargetOct = target; this.lateErrDbOct = errDb.slice(); this.diffHeadOct = dh; this.frontHeadOct = fh;
    this.diffHead = diffuseHeadGain3(); this.lateCal = gEq[1]; // mid-band input gain (v1 reported one scalar here)
  }
  setListener(pos, right) { this.head = pos; this.right = right; }
  // pos = the group's HRTF point (PianoMid / Harp / kit centroid); the ER origin is that point + ER_CENTRE_DY[g] in y.
  setGroupPosition(g, pos) {
    this.groupPos[g] = pos;
    const p = [pos[0], Math.max(this.room.min[1] + 0.05, pos[1] + this.centreDy[g]), pos[2]];
    this.er[g].update(this.room, p, this.head, this.right, this.erTrim[g] * this.wet);
  }
  // inputs[g]: mono group send for this block (sum of the group's sources). Adds the room into outL/outR.
  process(inputs, outL, outR, n) {
    const li = this.lateIn, inv = 1 / n;
    for (let i = 0; i < n; i++) li[i] = 0;
    if (!this.lateCurInit) { for (let g = 0; g < 3; g++) this.lateCur[g] = this.lateTrim[g] * this.wet; this.lateCurInit = true; }
    for (let g = 0; g < this.er.length; g++) {
      const x = inputs[g]; if (!x) continue;
      this.er[g].process(x, outL, outR, n);
      let t = this.lateCur[g]; const tt = this.lateTrim[g] * this.wet, dt = (tt - t) * inv;
      if (t === 0 && tt === 0) continue;
      for (let i = 0; i < n; i++) { t += dt; li[i] += x[i] * t; }
      this.lateCur[g] = tt;
    }
    this.fdn.process(li, outL, outR, n);
  }
}

// ---------------------------------------------------------------- master bus v2 (leveler -> shelf -> true-peak limiter)
// Leveler: Giannoulis, Massberg & Reiss 2012 (JAES 60(6)) feed-forward log-domain, RMS detector, soft knee,
// smooth branching attack/release. Limiter: 4x polyphase true-peak detector, W-sample min-hold + W-sample
// moving average (gain reaches the requirement exactly at the peak), one-pole release; ceiling -1 dBTP.
export const LIM_FIR = (() => { // 48 taps (12 per phase), Kaiser beta 6, cutoff at the input Nyquist
  const L = 48, beta = 6, h = new Float64Array(L);
  const I0 = (x) => { let s = 1, t = 1; for (let k = 1; k < 25; k++) { t *= (x / 2 / k) ** 2; s += t; } return s; };
  for (let i = 0; i < L; i++) { const xx = (i - (L - 1) / 2) / 4; h[i] = (xx === 0 ? 1 : Math.sin(Math.PI * xx) / (Math.PI * xx)) * I0(beta * Math.sqrt(1 - (2 * i / (L - 1) - 1) ** 2)) / I0(beta); }
  const out = new Float32Array(L);
  for (let p = 0; p < 4; p++) { let s = 0; for (let k = p; k < L; k += 4) s += h[k]; for (let k = p; k < L; k += 4) out[k] = h[k] / s; } // each phase sums to 1
  return out;
})();
// v2: trim +1 -> -3 dB (critique issue 2): headroom so forte chords and the kit stay out of the limiter. The headset
// volume makes up the 4 dB; louder is not better.
export const MASTER_DEFAULTS = {
  trimDb: -3, levThrDb: -18, levRatio: 1.6, levKneeDb: 8, levRmsTau: 0.03, levAttack: 0.01, levRelease: 0.25,
  shelfHz: 7500, shelfDb: 1.5, ceilingDbTP: -1, W: 48, relFast: 0.01, relSlow: 0.15, actTau: 0.08, safety: 0.999,
};
export class MasterBusV2 {
  constructor(sr = SR, opts = {}) {
    const o = { ...MASTER_DEFAULTS, ...opts }; this.o = o; this.sr = sr;
    this.trim = Math.pow(10, o.trimDb / 20);
    this.kRms = 1 - Math.exp(-1 / (o.levRmsTau * sr)); this.aA = Math.exp(-1 / (o.levAttack * sr)); this.aR = Math.exp(-1 / (o.levRelease * sr));
    this.ms = 0; this.gs = 0;
    this.sh = biquadCoeffs('highshelf', o.shelfHz, 0, o.shelfDb, sr); this.shS = new Float64Array(8);
    this.ceil = Math.pow(10, o.ceilingDbTP / 20); this.W = o.W;
    this.hist = [new Float32Array(16), new Float32Array(16)]; this.hp = 0; // FIR history (12 needed), circular 16
    this.D = o.W + 5; this.dl = [new Float32Array(128), new Float32Array(128)]; this.dp = 0;
    this.req = new Float32Array(256); this.reqPos = 0; // g_req ring
    this.dqI = new Int32Array(256); this.dqV = new Float32Array(256); this.dqH = 0; this.dqT = 0; this.idx = 0; // sliding-min deque
    this.boxBuf = new Float32Array(256); this.boxSum = 0; this.boxPos = 0; this.boxN = 0;
    this.prevIv = 0; this.g = 1; this.act = 0;
    this.kFast = 1 - Math.exp(-1 / (o.relFast * sr)); this.kSlow = 1 - Math.exp(-1 / (o.relSlow * sr)); this.kAct = 1 - Math.exp(-1 / (o.actTau * sr));
    this.boxBuf.fill(1); this.boxSum = o.W; // start at unity gain
    this.stats = { maxGrDb: 0, levMaxGrDb: 0, safetyHits: 0, frames: 0, limActive1dB: 0, limGrSumDb: 0, levActive1dB: 0 };
  }
  // In-place stereo processing of n frames.
  process(L, R, n) {
    const o = this.o, W = this.W;
    const s = this.sh, st = this.shS, thr = o.levThrDb, ratio = o.levRatio, knee = o.levKneeDb;
    for (let i = 0; i < n; i++) {
      let l = L[i] * this.trim, r = R[i] * this.trim;
      // leveler
      this.ms += this.kRms * (0.5 * (l * l + r * r) - this.ms);
      const xdb = 10 * Math.log10(this.ms + 1e-12);
      const ov = xdb - thr;
      let gc = 0;
      if (2 * ov > knee) gc = (thr + ov / ratio) - xdb;
      else if (2 * ov > -knee) gc = (1 / ratio - 1) * (ov + knee / 2) ** 2 / (2 * knee);
      this.gs = gc < this.gs ? this.aA * this.gs + (1 - this.aA) * gc : this.aR * this.gs + (1 - this.aR) * gc;
      if (this.gs < this.stats.levMaxGrDb) this.stats.levMaxGrDb = this.gs;
      const lg = Math.pow(10, this.gs / 20); l *= lg; r *= lg;
      // high shelf (RBJ cookbook coefficients), DF-I per channel
      { const y = s[0] * l + s[1] * st[0] + s[2] * st[1] - s[3] * st[2] - s[4] * st[3]; st[1] = st[0]; st[0] = l; st[3] = st[2]; st[2] = y; l = y; }
      { const y = s[0] * r + s[1] * st[4] + s[2] * st[5] - s[3] * st[6] - s[4] * st[7]; st[5] = st[4]; st[4] = r; st[7] = st[6]; st[6] = y; r = y; }
      // true-peak estimate for the interval (n-6, n-5) from the 4 polyphase outputs and the two samples
      const hp = this.hp; this.hist[0][hp] = l; this.hist[1][hp] = r;
      let iv = 0;
      for (let c = 0; c < 2; c++) {
        const hst = this.hist[c];
        iv = Math.max(iv, Math.abs(hst[(hp - 6) & 15]), Math.abs(hst[(hp - 5) & 15]));
        for (let p = 0; p < 4; p++) { let acc = 0; for (let k = 0; k < 12; k++) acc += LIM_FIR[4 * k + p] * hst[(hp - k) & 15]; iv = Math.max(iv, Math.abs(acc)); }
      }
      this.hp = (hp + 1) & 15;
      const tp = Math.max(iv, this.prevIv); this.prevIv = iv;
      const req = tp > this.ceil ? this.ceil / tp : 1; // g_req for sample n-6
      // sliding minimum over the last W requirements (monotonic deque, fixed arrays)
      const id = this.idx++;
      while (this.dqT > this.dqH && this.dqV[(this.dqT - 1) & 255] >= req) this.dqT--;
      this.dqI[this.dqT & 255] = id; this.dqV[this.dqT & 255] = req; this.dqT++;
      while (this.dqI[this.dqH & 255] <= id - W) this.dqH++;
      const hmin = this.dqV[this.dqH & 255];
      // moving average of the held minimum over W
      this.boxSum += hmin - this.boxBuf[this.boxPos]; this.boxBuf[this.boxPos] = hmin; this.boxPos = (this.boxPos + 1) % W;
      if (++this.boxN === 4096) { this.boxN = 0; let sum = 0; for (let k = 0; k < W; k++) sum += this.boxBuf[k]; this.boxSum = sum; } // drift guard
      const sbox = this.boxSum / W;
      // program-dependent release: isolated spikes recover in ~10 ms, sustained overs in ~150 ms
      this.act += this.kAct * ((sbox < 0.999 ? 1 : 0) - this.act);
      const kRel = this.kFast + (this.kSlow - this.kFast) * this.act;
      this.g = sbox < this.g ? sbox : this.g + (sbox - this.g) * kRel;
      // delayed audio
      const dp = this.dp, D = this.D;
      this.dl[0][dp] = l; this.dl[1][dp] = r;
      const rd = (dp - D) & 127; this.dp = (dp + 1) & 127;
      let ol = this.dl[0][rd] * this.g, or = this.dl[1][rd] * this.g;
      const grdb = 20 * Math.log10(this.g), stt = this.stats; if (grdb < stt.maxGrDb) stt.maxGrDb = grdb;
      stt.frames++; if (grdb < -1) stt.limActive1dB++; stt.limGrSumDb += grdb; if (this.gs < -1) stt.levActive1dB++;
      const sf = o.safety;
      if (ol > sf || ol < -sf || or > sf || or < -sf) { this.stats.safetyHits++; ol = Math.max(-sf, Math.min(sf, ol)); or = Math.max(-sf, Math.min(sf, or)); }
      L[i] = ol; R[i] = or;
    }
  }
}
