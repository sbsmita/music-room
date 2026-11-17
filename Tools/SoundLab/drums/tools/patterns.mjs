// Musical content for the A/B renders: the app's own lesson grooves plus standard (public, generic)
// drum patterns. Events: {t, kind, u, pos, zone, impl?, open?} | {t, choke} | {t, hatPedal}.
// Human feel: seeded timing (+-3 ms) and stroke (+-0.04) jitter, identical for old and new.
import { Rng } from '../engine/drumkit.reference.mjs';
import fs from 'node:fs';
const LESSONS = JSON.parse(fs.readFileSync(new URL('../../data/lessons.json', import.meta.url))).lessons;
const human = (evs, seed) => { const r = new Rng(seed); return evs.map((e) => (e.kind ? { ...e, t: Math.max(0, e.t + 0.003 * r.bip()), u: Math.min(1, Math.max(0.05, e.u + 0.04 * r.bip())) } : e)); };

export function rock() { // drums-rock lesson (96 bpm), 4 bars, accents, open hat on "4&" of bar 4, crash on the 5th downbeat
  const L = LESSONS.find((l) => l.id === 'drums-rock'), spb = 60 / L.bpm, ev = [];
  for (const e of L.events) {
    if (e.beat >= 16) break;
    const t = 0.2 + e.beat * spb, down = Number.isInteger(e.beat);
    for (const tg of e.targets) {
      if (tg.kind === 'hat') {
        if (e.beat === 15.5) { ev.push({ t, kind: 'hatOpen', u: 0.7, pos: 0.55, zone: 'top' }); continue; }
        ev.push({ t, kind: 'hat', u: down ? 0.72 : 0.5, pos: 0.55, zone: 'top' });
      } else if (tg.kind === 'snare') ev.push({ t, kind: 'snare', u: 0.84, pos: 0.18 });
      else if (tg.kind === 'kick') ev.push({ t, kind: 'kick', u: 0.8 });
    }
    if (e.beat === 6.5 || e.beat === 14.5) ev.push({ t, kind: 'kick', u: 0.62 }); // the "and" of 3 in bars 2 and 4
  }
  const t5 = 0.2 + 16 * spb;
  ev.push({ t: t5, hatPedal: 'close' }, { t: t5, kind: 'crash', u: 0.85, pos: 0.9, zone: 'edge' }, { t: t5, kind: 'kick', u: 0.85 });
  return { events: human(ev, 101), seconds: t5 + 1.8 };
}
export function jazz() { // swing ride pattern, 132 bpm, 4 bars: ride 1, 2, 2a, 3, 4, 4a; hat pedal on 2 & 4; feathered kick; snare comping
  const spb = 60 / 132, sw = 2 / 3, ev = []; let t0 = 0.2;
  for (let bar = 0; bar < 4; bar++) {
    for (let b = 0; b < 4; b++) {
      const t = t0 + (bar * 4 + b) * spb;
      ev.push({ t, kind: 'ride', u: b % 2 ? 0.66 : 0.52, pos: 0.62, zone: 'bow' });
      if (b % 2) { ev.push({ t: t + sw * spb, kind: 'ride', u: 0.42, pos: 0.62, zone: 'bow' }); ev.push({ t, kind: 'hatClose', u: 0.55 }); ev.push({ t: t + 0.12, hatPedal: 'openSilent' }); }
      ev.push({ t, kind: 'kick', u: 0.3 });
    }
    const tb = t0 + bar * 4 * spb; // comping: ghost notes and a couple of accents (classic "and of 2", "4")
    if (bar === 0) ev.push({ t: tb + (1 + sw) * spb, kind: 'snare', u: 0.3, pos: 0.4 });
    if (bar === 1) { ev.push({ t: tb + (2 + sw) * spb, kind: 'snare', u: 0.55, pos: 0.3 }); ev.push({ t: tb + 3 * spb, kind: 'snare', u: 0.2, pos: 0.45 }); }
    if (bar === 2) ev.push({ t: tb + (0 + sw) * spb, kind: 'snare', u: 0.25, pos: 0.45 }), ev.push({ t: tb + (3 + sw) * spb, kind: 'snare', u: 0.7, pos: 0.25 });
    if (bar === 3) { ev.push({ t: tb + (1 + sw) * spb, kind: 'snare', u: 0.35, pos: 0.4 }); ev.push({ t: tb + (3 + sw) * spb, kind: 'kick', u: 0.62 }); ev.push({ t: tb + (3 + sw) * spb, kind: 'ride', u: 0.8, pos: 0.1, zone: 'bell' }); }
  }
  const tEnd = t0 + 16 * spb;
  ev.push({ t: tEnd, kind: 'ride', u: 0.7, pos: 0.62, zone: 'bow' }, { t: tEnd, kind: 'hatClose', u: 0.55 }, { t: tEnd, kind: 'kick', u: 0.45 });
  return { events: human(ev, 202), seconds: tEnd + 2.4 };
}
export function fill() { // drums-fill-round shape at 96 bpm: 1 bar groove, 1 bar 16th fill snare-tom1-tom2-floor with a crescendo, crash + kick, choke
  const spb = 60 / 96, ev = [], t0 = 0.2;
  for (let i = 0; i < 8; i++) { const t = t0 + i * 0.5 * spb; ev.push({ t, kind: 'hat', u: i % 2 ? 0.48 : 0.7, pos: 0.55, zone: 'top' }); if (i % 4 === 0) ev.push({ t, kind: 'kick', u: 0.8 }); if (i % 4 === 2) ev.push({ t, kind: 'snare', u: 0.82, pos: 0.18 }); }
  const kinds = ['snare', 'snare', 'snare', 'snare', 'tom1', 'tom1', 'tom1', 'tom1', 'tom2', 'tom2', 'tom2', 'tom2', 'floor', 'floor', 'floor', 'floor'];
  kinds.forEach((k, i) => ev.push({ t: t0 + 4 * spb + i * 0.25 * spb, kind: k, u: 0.55 + 0.4 * (i / 15) - (i % 2 ? 0.06 : 0), pos: 0.3 }));
  const tc = t0 + 8 * spb;
  ev.push({ t: tc, kind: 'crash', u: 0.92, pos: 0.92, zone: 'edge' }, { t: tc, kind: 'kick', u: 0.9 }, { t: tc + 1.6 * spb, choke: 'crash' });
  ev.push({ t: tc + 2 * spb, kind: 'snare', u: 0.9, pos: 0.95, zone: 'rim' }, { t: tc + 2 * spb, kind: 'kick', u: 0.85 });
  return { events: human(ev, 303), seconds: tc + 2 * spb + 1.6 };
}
export function roll() { // single-stroke snare roll, soft to loud over 5 s (R/L alternation, the weaker hand ~0.05 softer)
  const ev = [], rate = 13, N = Math.round(5.2 * rate);
  for (let i = 0; i < N; i++) { const x = i / (N - 1); ev.push({ t: 0.2 + i / rate, kind: 'snare', u: 0.08 + 0.84 * Math.pow(x, 1.6) - (i % 2 ? 0.05 : 0), pos: 0.22 + 0.06 * (i % 2) }); }
  ev.push({ t: 0.2 + N / rate + 0.02, kind: 'snare', u: 1.0, pos: 0.12 }, { t: 0.2 + N / rate + 0.02, kind: 'crash', u: 0.9, pos: 0.9, zone: 'edge' }, { t: 0.2 + N / rate + 0.02, kind: 'kick', u: 0.9 });
  return { events: human(ev, 404), seconds: 0.2 + N / rate + 3.2 };
}
// Typical VR strokes at the app's real level: two bars of the rock lesson and two of the swing pattern with every
// stroke squeezed into u .3-.6 (what most players actually hit). Rendered with ONE common gain for old and new,
// so the pair shows the in-app loudness difference instead of hiding it.
export function soft() {
  const sq = (e) => (e.kind ? { ...e, u: 0.3 + 0.3 * e.u } : e);
  const r = rock(), j = jazz(), spbR = 60 / 96, spbJ = 60 / 132;
  const a = r.events.filter((e) => e.t < 0.2 + 8 * spbR - 0.05).map(sq);
  const off = 0.2 + 8 * spbR + 0.3;
  const b = j.events.filter((e) => e.t < 0.2 + 8 * spbJ - 0.05).map((e) => sq({ ...e, t: e.t - 0.2 + off }));
  return { events: [...a, ...b], seconds: off + 8 * spbJ + 1.0 };
}
// single-hit ladders (no human jitter: exact velocities)
const lad = (spec) => { const ev = []; let t = 0.15; for (const [kind, us, st, gap, extra] of spec) for (const u of us) { ev.push({ t, kind, u, ...st }); if (extra) for (const x of extra(t)) ev.push(x); t += gap; } return { events: ev, seconds: t + 0.4 }; };
const U5 = [0.15, 0.35, 0.55, 0.75, 0.95];
export const ladders = {
  kick: () => lad([['kick', [0.25, 0.45, 0.65, 0.85, 1.0], {}, 0.7]]),
  snare: () => lad([['snare', U5, { pos: 0.12 }, 0.5], ['snare', [0.35, 0.65, 0.95], { pos: 0.75 }, 0.55], ['snare', [0.4, 0.7, 0.95], { pos: 0.95, zone: 'rim' }, 0.55], ['snare', [0.4, 0.7, 0.95], { pos: 0.4, zone: 'xstick' }, 0.5]]),
  toms: () => lad([['tom1', [0.3, 0.6, 0.9], { pos: 0.3 }, 0.8], ['tom2', [0.3, 0.6, 0.9], { pos: 0.3 }, 0.9], ['floor', [0.3, 0.6, 0.9], { pos: 0.3 }, 1.05], ['tom1', [0.8], { pos: 0.95, zone: 'rim' }, 0.85], ['floor', [0.8], { pos: 0.95, zone: 'rim' }, 0.9]]),
  hats: () => lad([['hat', U5, { pos: 0.55, zone: 'top' }, 0.35], ['hat', [0.45, 0.85], { pos: 0.92, zone: 'edge' }, 0.4], ['hatOpen', [0.6], { pos: 0.55, zone: 'top', open: 0.3 }, 0.9], ['hatOpen', [0.45, 0.85], { pos: 0.55, zone: 'top' }, 1.4, (t) => [{ t: t + 0.95, hatPedal: 'close' }]], ['hatClose', [0.35, 0.7, 1.0], {}, 0.45]]),
  crash: () => lad([['crash', [0.3, 0.6, 0.95], { pos: 0.9, zone: 'edge' }, 2.2], ['crash', [0.6], { pos: 0.6, zone: 'bow' }, 2.0], ['crash', [0.7], { pos: 0.1, zone: 'bell' }, 1.7], ['crash', [0.9], { pos: 0.9, zone: 'edge' }, 1.4, (t) => [{ t: t + 0.6, choke: 'crash' }]]]),
  ride: () => lad([['ride', [0.3, 0.55, 0.8], { pos: 0.62, zone: 'bow' }, 1.9], ['ride', [0.45, 0.85], { pos: 0.1, zone: 'bell' }, 1.9], ['ride', [0.85], { pos: 0.93, zone: 'edge' }, 2.4]]),
};
