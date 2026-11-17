// Dry instrument rendering with the CURRENT sounds (06-audio-dsp.reference.mjs), per spatial source,
// plus the Classic main-reverb send and drum-room send, so only the room differs between versions.
import * as R from '../../../Docs/06-audio-dsp.reference.mjs';
import { convolve } from './lib/fft.mjs';

const SR = 48000;
export const noiseTable = (() => { const r = R.mulberry32(1), n = new Float32Array(2 * SR); for (let i = 0; i < n.length; i++) n[i] = Math.fround(r() * 2 - 1); return n; })();

// Unity positions (spec 05 json: rig homes 'all' mode, head (0,1.2,0)); see room.md §5.1.
export const POS = {
  pianoLo: [-0.42, 0.77, 0.50], pianoMid: [0, 0.77, 0.50], pianoHi: [0.42, 0.77, 0.50],
  harp: [-0.82, 0.8, 0.128],
  snare: [0.5, 0.72, -0.26], tom1: [0.74, 0.86, -0.24], tom2: [0.62, 0.85, -0.55], floor: [0.22, 0.62, -0.64],
  kick: [0.9, 0.29, -0.38], hat: [0.56, 0.8, 0.16], crash: [0.78, 1.02, 0.13], ride: [0.47, 1.04, -0.74],
  drumRoom: [0, 0, 0], // the Classic 'drum:room' source is never positioned (spec 06 §10)
};
export const GROUP_OF = { pianoLo: 0, pianoMid: 0, pianoHi: 0, harp: 1, snare: 2, tom1: 2, tom2: 2, floor: 2, kick: 2, hat: 2, crash: 2, ride: 2 };

const addAt = (dst, src, at, gain = 1) => { const n = Math.min(src.length, dst.length - at); for (let i = 0; i < n; i++) dst[at + i] += src[i] * gain; };

function bodyIR(modes, seconds, seed) { const ir = R.makeBodyImpulse(SR, modes, seconds, R.mulberry32(seed)); return { ir, scale: R.convolverScale(ir, SR) }; }

// ---------- piano (PianoSynth.play + playBuffer + Voice.release), jitter 0
const pianoCache = new Map();
const pianoKey = (m) => { if (!pianoCache.has(m)) pianoCache.set(m, R.renderPianoKey(m, SR, R.mulberry32(1000 + m))); return pianoCache.get(m); };
// notes: [{midi, vel, t, off}] -> {src: {pianoLo, pianoMid, pianoHi}, send}
export function renderPiano(notes, n) {
  const src = { pianoLo: new Float32Array(n), pianoMid: new Float32Array(n), pianoHi: new Float32Array(n) };
  const send = new Float32Array(n), board = new Float32Array(n);
  for (const nt of notes) {
    const vel = R.clamp(nt.vel, 0.05, 1), f = R.mtof(nt.midi);
    const level = 0.08 + 0.8 * vel * vel, cutoff = R.clamp(f * 1.5 + 300 + Math.pow(vel, 1.6) * 11000, 500, 16000);
    const name = nt.midi < 52 ? 'pianoLo' : nt.midi < 76 ? 'pianoMid' : 'pianoHi';
    const buf = pianoKey(nt.midi), t0 = Math.round(nt.t * SR), tr = Math.round(nt.off * SR);
    const lp = new R.Biquad('lowpass', cutoff, 0.4, 0, SR);
    const res = 0.6 + 0.4 * vel;
    for (let i = 0; i < buf.length && t0 + i < n; i++) {
      const k = t0 + i;
      let g = level;
      if (k >= tr) { if (k >= tr + 0.5 * SR) break; g *= Math.exp(-(k - tr) / (0.06 * SR)); }
      const y = lp.tick(buf[i]) * g;
      src[name][k] += y; send[k] += y * 0.22; board[k] += y * res;
    }
    addAt(src[name], R.noiseVoice(Math.round(0.05 * SR), SR, noiseTable, { bp: R.clamp(f * 1.6 + 900, 600, 3200), q: 1.1, peak: 0.05 + 0.3 * vel * vel, decay: 0.007 + 0.004 * vel }), t0);
    if (nt.thud) addAt(src[name], R.noiseVoice(Math.round(0.1 * SR), SR, noiseTable, { bp: 260, q: 0.8, peak: 0.028, decay: 0.03 }), tr);
  }
  const b = bodyIR([95, 142, 187, 236, 298, 372, 450, 560], 0.28, 2);
  const [bo] = convolve(board, b.ir, b.scale * 0.22);
  for (let i = 0; i < n; i++) src.pianoMid[i] += bo[i];
  return { src, send };
}

// ---------- harp, old sound (StringSynth.pluck + karplus), jitter 0
const harpCache = new Map();
const harpKey = (m) => { if (!harpCache.has(m)) harpCache.set(m, R.karplusOld(m, SR, R.mulberry32(2000 + m))); return harpCache.get(m); };
export function renderHarp(plucks, n) {
  const harp = new Float32Array(n), send = new Float32Array(n), box = new Float32Array(n);
  const last = new Map();
  const sorted = [...plucks].sort((a, b) => a.t - b.t);
  // a re-pluck releases the ringing voice of the same string (tau 60 ms)
  const off = sorted.map((p, i) => { const nx = sorted.slice(i + 1).find((q) => q.midi === p.midi); return nx ? nx.t : Infinity; });
  sorted.forEach((p, idx) => {
    const vel = R.clamp(p.vel, 0.05, 1), t0 = Math.round(p.t * SR), tr = off[idx] === Infinity ? Infinity : Math.round(off[idx] * SR);
    addAt(harp, R.noiseVoice(Math.round(0.08 * SR), SR, noiseTable, { lp: 700, peak: 0.02 + 0.05 * vel, decay: 0.012 }), t0);
    const buf = harpKey(p.midi), lp = new R.Biquad('lowpass', 1000 + 5000 * vel, 0.4, 0, SR);
    const level = 0.1 + 0.6 * vel, res = 0.5 + 0.4 * vel;
    for (let i = 0; i < buf.length && t0 + i < n; i++) {
      const k = t0 + i; let g = level;
      if (k >= tr) { if (k >= tr + 0.5 * SR) break; g *= Math.exp(-(k - tr) / (0.06 * SR)); }
      const y = lp.tick(buf[i]) * g;
      harp[k] += y; send[k] += y * 0.34; box[k] += y * res;
    }
    last.set(p.midi, p);
  });
  const b = bodyIR([138, 205, 286, 377, 515, 690], 0.3, 3);
  const [bo] = convolve(box, b.ir, b.scale * 0.24);
  for (let i = 0; i < n; i++) harp[i] += bo[i];
  return { src: { harp }, send };
}

// ---------- drums (DrumSynth.drum, live voices) with the per-layer main-reverb sends and the drum-room send
const SRC_OF = { kick: 'kick', snare: 'snare', hat: 'hat', hatOpen: 'hat', hatClose: 'hat', tom1: 'tom1', tom2: 'tom2', floor: 'floor', crash: 'crash', ride: 'ride' };
function drumHitSends(kind, vel, strike, len) {
  vel = R.clamp(vel, 0.05, 1);
  const v2 = vel * vel, pos = R.clamp(strike.pos ?? 0.3, 0, 1), zone = strike.zone || 'head';
  const dest = new Float32Array(len), send = new Float32Array(len);
  const add = (buf, s) => { for (let k = 0; k < len; k++) { dest[k] += buf[k]; if (s) send[k] += buf[k] * s; } };
  const tv = (o) => add(R.toneVoice(len, SR, o), o.send);
  const nv = (o) => add(R.noiseVoice(len, SR, noiseTable, o), o.send);
  const mv = (o) => add(R.metalVoice(len, SR, { ...o, bp: (o.bp || 9500) * (0.85 + 0.3 * vel) }), o.send);
  const edge = pos, centre = 1 - pos;
  switch (kind) {
    case 'kick': tv({ f0: 165, f1: 46, slide: 0.09, peak: 1.0 * vel, decay: 0.42 + 0.1 * vel, lp: 2500 }); nv({ bp: 3200, q: 0.7, peak: 0.34 * v2, decay: 0.02 }); break;
    case 'snare':
      tv({ type: 'triangle', f0: 195, f1: 150, slide: 0.05, peak: 0.5 * vel * (0.7 + 0.3 * centre), decay: 0.18 + 0.06 * edge });
      tv({ f0: 330, f1: 280, slide: 0.04, peak: 0.3 * vel * (0.5 + 0.9 * edge), decay: 0.1 + 0.1 * edge });
      nv({ hp: 1400, lp: 9000, peak: 0.66 * Math.pow(vel, 1.3) * (1 - 0.35 * edge), decay: 0.2 + 0.08 * vel + 0.06 * edge, send: 0.3 }); break;
    case 'hat': mv({ bp: 8500, hp: 5000, peak: 1.3 * vel, decay: 0.08 + 0.03 * vel + 0.05 * edge }); nv({ hp: 7000, peak: 0.75 * vel * (1 + 0.3 * edge), decay: 0.07 + 0.05 * edge }); break;
    case 'hatOpen': mv({ bp: 8500, hp: 5000, peak: 1.05 * vel, decay: 0.42 + 0.15 * edge, send: 0.1 }); nv({ hp: 7000, peak: 0.6 * vel, decay: 0.35 + 0.15 * edge }); break;
    case 'tom1': case 'tom2': case 'floor': {
      const T = kind === 'tom1' ? { f0: 210, f1: 140, slide: 0.12, decay: 0.62, bp: 2500, peak: 0.9 } : kind === 'tom2' ? { f0: 165, f1: 105, slide: 0.14, decay: 0.78, bp: 2200, peak: 0.9 } : { f0: 115, f1: 72, slide: 0.16, decay: 1.0, bp: 1800, peak: 1.0 };
      tv({ f0: T.f0, f1: T.f1, slide: T.slide, peak: T.peak * vel * (0.75 + 0.25 * centre), decay: T.decay * (1 + 0.3 * edge), send: 0.15 });
      nv({ bp: T.bp, q: 0.6, peak: 0.26 * vel * (0.6 + 0.8 * edge), decay: 0.03 }); break;
    }
    case 'crash':
      nv({ bp: 5500, q: 0.4, hp: 2800, peak: 0.95 * vel * (0.75 + 0.25 * edge), decay: (1.6 + 0.6 * vel) * (0.8 + 0.3 * edge), send: 0.25 });
      mv({ ratio: 1.7, bp: 6000, hp: 3000, peak: 0.4 * vel, decay: 1.2, send: 0.2 }); break;
    case 'ride':
      mv({ ratio: 1.3, bp: 7000, hp: 4000, peak: 0.8 * vel, decay: 1.1, send: 0.15 }); tv({ f0: 2900, peak: 0.45 * vel, decay: 0.6 }); nv({ hp: 6000, peak: 0.3 * vel, decay: 0.6 }); break;
    default: throw new Error(kind);
  }
  const roomSend = kind === 'kick' ? 0.35 : kind === 'crash' || kind === 'ride' ? 0.2 : 0.5;
  return { dest, send, roomSend };
}
const drumCache = new Map();
// hatRampMs: raised-cosine fade-in on the hi-hat hit (metal + noise layers). The current kit's six square waves start
// in phase and spike to +3.9 dBFS for 0.1 ms (spec 06 §13.6, room.md §12): a kit bug, fixed by the drums owner in the new
// kit. Applied identically to dry, old and new renders so the master limiter does not confound the room comparison.
export const HAT_RAMP_MS = 0.3;
export function renderDrums(hits, n, { hatRampMs = HAT_RAMP_MS } = {}) {
  const names = ['snare', 'tom1', 'tom2', 'floor', 'kick', 'hat', 'crash', 'ride'];
  const src = Object.fromEntries(names.map((k) => [k, new Float32Array(n)]));
  const send = new Float32Array(n), room = new Float32Array(n);
  for (const h of hits) {
    const key = `${h.kind}|${h.vel.toFixed(4)}|${h.pos ?? 0.3}|${hatRampMs}`;
    if (!drumCache.has(key)) {
      const d = drumHitSends(h.kind, h.vel, { pos: h.pos }, Math.round((h.kind === 'crash' ? 3.5 : h.kind === 'ride' ? 2.0 : 1.3) * SR));
      if ((h.kind === 'hat' || h.kind === 'hatOpen') && hatRampMs > 0) { const m = Math.round(hatRampMs / 1000 * SR); for (let k = 0; k < m; k++) { const w = 0.5 - 0.5 * Math.cos(Math.PI * k / m); d.dest[k] *= w; d.send[k] *= w; } }
      drumCache.set(key, d);
    }
    const d = drumCache.get(key), t0 = Math.round(h.t * SR), s = SRC_OF[h.kind];
    addAt(src[s], d.dest, t0, 1.7); addAt(send, d.send, t0); addAt(room, d.dest, t0, 1.7 * d.roomSend);
    if (h.kind === 'kick' || h.kind.startsWith('tom') || h.kind === 'floor') {
      const d0 = Math.round(0.004 * SR);
      addAt(src.snare, R.noiseVoice(Math.round(0.15 * SR), SR, noiseTable, { hp: 2800, lp: 9000, peak: 0.07 * R.clamp(h.vel, 0.05, 1), decay: 0.08 }), t0 + d0);
    }
  }
  return { src, send, room };
}
