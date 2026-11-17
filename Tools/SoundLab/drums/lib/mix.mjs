// The listening mix used IDENTICALLY for the old and the new kit, so an A/B compares only the voices:
// per-piece constant-power pan (drummer's perspective, narrowed), a 0.2 ms interaural delay on the far
// ear, and the app's own drum room (makeRoomImpulse .45 s, convolver-normalized, bus 0.3) fed by the
// per-piece room sends. No compressor, no limiter, no EQ: loudness matching is a single gain.
import { makeRoomImpulse, convolverScale, mulberry32 } from '../../../../Docs/06-audio-dsp.reference.mjs';
import { convolve, integratedLoudness, truePeak, db } from './analysis.mjs';

export const PAN = { kick: 0, snare: -0.12, hat: -0.42, tom1: -0.16, tom2: 0.12, floor: 0.38, crash: -0.34, ride: 0.44 };

let roomIR = null;
export function drumRoomIR(sr = 48000) {
  if (roomIR) return roomIR;
  const chs = makeRoomImpulse(sr, { seconds: 0.45, preDelay: 0.004, fast: 0.3, slow: 0.45, lp: 0.3, early: 0.8 }, mulberry32(6));
  const s = convolverScale(chs, sr);
  roomIR = chs.map((c) => c.map((v) => v * s));
  return roomIR;
}

export function mixKit(bus, room, sr = 48000, { roomBus = 0.3 } = {}) {
  const N = Math.max(...Object.values(bus).map((b) => b.length), room.length);
  const L = new Float32Array(N), R = new Float32Array(N);
  const itd = Math.round(0.0002 * sr);
  for (const [p, b] of Object.entries(bus)) {
    const pan = PAN[p] ?? 0, a = ((pan + 1) * Math.PI) / 4;
    const gl = Math.cos(a), gr = Math.sin(a);
    const dl = pan > 0.05 ? itd : 0, dr = pan < -0.05 ? itd : 0;
    for (let i = 0; i < b.length; i++) {
      if (i + dl < N) L[i + dl] += gl * b[i];
      if (i + dr < N) R[i + dr] += gr * b[i];
    }
  }
  const ir = drumRoomIR(sr);
  const wl = convolve(room, ir[0]), wr = convolve(room, ir[1]);
  for (let i = 0; i < N; i++) { L[i] += roomBus * (wl[i] || 0); R[i] += roomBus * (wr[i] || 0); }
  return [L, R];
}

// One gain to integrated -18 LUFS; if the true peak would exceed -1 dBFS, BOTH files of a pair must use
// the same lower target (see matchPair), so no limiter ever touches the A/B.
export function loudnessGain(ch, target = -18, sr = 48000) {
  const L = integratedLoudness(ch, sr);
  return Math.pow(10, (target - L) / 20);
}
export function matchPair(a, b, sr = 48000, target = -18, ceilingDb = -1.15) {
  const ga = loudnessGain(a, target, sr), gb = loudnessGain(b, target, sr);
  const pa = db(truePeak(a) * ga), pb = db(truePeak(b) * gb);
  const over = Math.max(pa, pb) - ceilingDb;
  const t = over > 0 ? target - over - 0.05 : target;
  const ka = Math.pow(10, (t - target) / 20);
  const scale = (ch, g) => ch.map((c) => c.map((v) => v * g));
  // the -70 LUFS absolute gate makes integrated loudness slightly non-linear in gain for files with silent gaps:
  // re-measure and correct each file to the common target (v2; v1 left one pair 0.3 LU apart)
  const fit = (ch, g) => { for (let it = 0; it < 3; it++) { const L = integratedLoudness(scale(ch, g), sr); g *= Math.pow(10, (t - L) / 20); } return scale(ch, g); };
  return { a: fit(a, ga * ka), b: fit(b, gb * ka), target: t, peakA: pa + (t - target), peakB: pb + (t - target) };
}
