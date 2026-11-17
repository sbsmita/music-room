// patterns.mjs: the musical content of the renders and lessons (traditional rhythms; no melody, no copyrighted song).
//  - Tumbao (marcha): the basic Afro-Cuban conga pattern, H T S T H T O O per bar (Wikipedia "Tumbao" table, clave-
//    neutral son montuno form), two-drum version with the open tones on the tumba. Traditional; rhythms carry no copyright.
//  - Conga line (comparsa): the "one, two, three, kick" dance rhythm (accent on the and of 4), traditional Cuban carnival.
//  - "Shave and a Haircut" tag: the 1899 rhythm from Charles Hale's "At a Darktown Cakewalk" (public domain since 1975+).
// Events: { t (s), drum 0 conga | 1 tumba, hand 0 L | 1 R, stroke, u }. Strokes: open, mute, slap, slapClosed, bass,
// heel, toe, touch. Human feel (timing +-4 ms, u +-0.05) from a seeded RNG so every render is reproducible.
import { Rng } from '../engine/handdrums.reference.mjs';

export function tumbao({ bpm = 92, bars = 8, t0 = 0.25, seed = 11 } = {}) {
  const r = new Rng(seed), e8 = 60 / bpm / 2, ev = [];
  const jit = (a) => a * r.bip();
  // per bar: [eighth, drum, hand, stroke, u]
  const bar = [[0, 0, 0, 'heel', 0.38], [1, 0, 0, 'toe', 0.32], [2, 0, 1, 'slapClosed', 0.78], [3, 0, 0, 'toe', 0.3],
    [4, 0, 0, 'heel', 0.36], [5, 0, 0, 'toe', 0.3], [6, 1, 1, 'open', 0.74], [7, 1, 1, 'open', 0.7]];
  for (let b = 0; b < bars; b++) {
    let pat = bar;
    if (b % 4 === 3) pat = bar.map((q) => (q[0] === 7 ? [7, 0, 1, 'open', 0.72] : q)); // answer on the conga every 4th bar
    if (b === bars - 1) pat = [[0, 0, 0, 'heel', 0.4], [1, 0, 0, 'toe', 0.34], [2, 0, 1, 'slap', 0.85], [3, 0, 0, 'slap', 0.7],
      [4, 1, 1, 'open', 0.8], [5, 1, 0, 'open', 0.72], [6, 0, 1, 'slap', 0.9], [7, 1, 0, 'bass', 0.9]];
    for (const [i, drum, hand, stroke, u] of pat) ev.push({ t: t0 + (b * 8 + i) * e8 + jit(0.004), drum, hand, stroke, u: Math.min(1, Math.max(0.05, u + jit(0.05))) });
  }
  // final open tone on the tumba, let ring
  ev.push({ t: t0 + bars * 8 * e8, drum: 1, hand: 1, stroke: 'open', u: 0.8 });
  return { ev, seconds: t0 + bars * 8 * e8 + 1.8, bpm };
}

export function congaLine({ bpm = 120, t0 = 0.25, seed = 12 } = {}) {
  const r = new Rng(seed), q = 60 / bpm, ev = [];
  const jit = (a) => a * r.bip();
  const at = (bar, beat) => t0 + (bar * 4 + beat) * q + jit(0.004);
  const hit = (bar, beat, drum, hand, stroke, u) => ev.push({ t: at(bar, beat), drum, hand, stroke, u: Math.min(1, Math.max(0.05, u + jit(0.04))) });
  for (let b = 0; b < 6; b++) {
    // one, two, three ... KICK (the and of four), both hands on the accent
    hit(b, 0, 0, 1, 'open', 0.62); hit(b, 1, 0, 0, 'open', 0.58); hit(b, 2, 0, 1, 'open', 0.62);
    if (b % 2 === 1) hit(b, 3, 0, 0, 'toe', 0.3);
    hit(b, 3.5, 0, 1, 'slap', 0.92); hit(b, 3.5, 1, 0, 'bass', 0.85);
    if (b >= 2) { hit(b, 1.5, 1, 0, 'touch', 0.3); hit(b, 2.5, 1, 0, 'open', 0.5); }      // the tumba joins
  }
  // bar 6: two-hand roll, soft to loud, alternating hands on the tumba (8 strokes per beat at the end)
  const roll0 = at(6, 0), n = 24;
  for (let i = 0; i < n; i++) ev.push({ t: roll0 + i * (2 * q / n) + jit(0.003), drum: 1, hand: i % 2, stroke: 'open', u: 0.25 + 0.65 * (i / (n - 1)) + jit(0.03) });
  hit(6, 2, 0, 1, 'slap', 0.95); hit(6, 2, 1, 0, 'bass', 0.9);
  // bars 7-8: "shave and a haircut ... two bits"
  hit(7, 0, 0, 1, 'open', 0.7); hit(7, 1, 0, 0, 'slap', 0.66); hit(7, 1.5, 0, 1, 'slap', 0.66); hit(7, 2, 1, 1, 'open', 0.7); hit(7, 3, 1, 0, 'open', 0.72);
  hit(8, 1, 0, 1, 'slap', 0.9); hit(8, 2, 1, 0, 'bass', 0.95); hit(8, 2, 1, 1, 'open', 0.9);
  return { ev: ev.sort((a, b) => a.t - b.t), seconds: at(8, 2) + 2.0, bpm };
}

// ladders and the technique tour
export function ladder(stroke, { us = [0.15, 0.35, 0.55, 0.75, 0.95], gap = 0.9, t0 = 0.25 } = {}) {
  const ev = []; let t = t0;
  for (const drum of [0, 1]) for (const u of us) { ev.push({ t, drum, hand: 1, stroke, u }); t += gap; }
  return { ev, seconds: t + 0.8 };
}
export function techniques({ t0 = 0.25 } = {}) {
  const ev = []; let t = t0; const add = (drum, hand, stroke, u, gap, extra = {}) => { ev.push({ t, drum, hand, stroke, u, ...extra }); t += gap; };
  for (const drum of [0, 1]) {
    add(drum, 1, 'open', 0.75, 1.1); add(drum, 1, 'mute', 0.75, 0.8); add(drum, 1, 'slap', 0.8, 1.0); add(drum, 1, 'slapClosed', 0.8, 0.8);
    add(drum, 0, 'bass', 0.75, 1.0); add(drum, 0, 'heel', 0.5, 0.35); add(drum, 0, 'toe', 0.45, 0.8);
    add(drum, 1, 'touch', 0.45, 0.8);
  }
  // flam: left then right 18 ms later, open tones on the conga; then a resting palm muting a ringing tumba
  add(0, 0, 'open', 0.7, 0.018); add(0, 1, 'open', 0.8, 1.0);
  add(1, 1, 'open', 0.85, 0.45); ev.push({ t, drum: 1, hand: 0, rest: 'palm' }); t += 0.8;
  // two-hand roll on the conga, 14 strokes/s, soft to loud
  for (let i = 0; i < 28; i++) add(0, i % 2, 'open', 0.2 + 0.7 * (i / 27), 1 / 14);
  add(0, 1, 'slap', 0.95, 1.2);
  return { ev, seconds: t + 0.6 };
}
