// ControlStream reference (platform.md section 6.3): how a continuous instrument (a singing
// bowl's rub) records into the take / loop event stream without flooding it, and how playback rebuilds the gesture.
// Main-thread logic, once per frame; Core's C# ControlEncoder ports it 1:1 (float math in double, the same order).
//
// Encoder (per lane):
//   on      level rises above LEVEL_ON                         -> "<type>On"  {lane, u: pitch, vel: level}
//   control while on, the first frame where |pitch - sent| >= PITCH_DB semitones or |dB(level) - dB(sent)| >= LEVEL_DB,
//           and at least MIN_GAP since the last event; or KEYFRAME since the last event   -> "<type>Ctl" {lane, u, vel, speed}
//   off     level below LEVEL_OFF for OFF_HOLD                 -> "<type>Off" {lane}  (preceded by a final control)
//   speed = seconds since the lane's previous event (the glide length), capped at KEYFRAME.
// Decoder (the unit): each control glides linearly from the current value to the event's value over `speed`; playback
// dispatches a control min(speed, PREROLL) early, so the glide ends at the moment the value was recorded.
import { INSTRUMENTS } from './registry.mjs';

export const C = {
  FRAME: 1 / 90, PITCH_DB: 0.02, LEVEL_DB: 0.5, MIN_GAP: 1 / 30, KEYFRAME: 0.25,
  LEVEL_ON: 0.01, LEVEL_OFF: 0.008, OFF_HOLD: 0.06, PREROLL: 0.05,
};

const dB = (a) => 20 * Math.log10(Math.max(a, 1e-6));

export function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** The test gesture: 10 s at 90 Hz. Silence 0-0.5 s; a swell; a slow glide A3 -> A4 (2-5 s); vibrato 5.5 Hz +-30
 *  cents (5-8 s); a fall and a stop (8-9 s); silence; tracking jitter (seeded, +-1.5 cents sample-and-hold at 30 Hz). */
export function gesture(seed = 1) {
  const rnd = mulberry32(seed);
  const n = Math.round(10 / C.FRAME);
  const out = [];
  let jit = 0;
  for (let i = 0; i < n; i++) {
    const t = i * C.FRAME;
    if (i % 3 === 0) jit = (rnd() * 2 - 1) * 0.015;
    let p = 57, a = 0;
    if (t >= 0.5 && t < 9) {
      a = t < 1.5 ? 0.6 * (t - 0.5) : t < 8 ? 0.6 + 0.2 * Math.sin(2 * Math.PI * 0.3 * (t - 1.5)) : 0.6 * (9 - t) / 1;
      p = t < 2 ? 57 : t < 5 ? 57 + 12 * (t - 2) / 3 : t < 8 ? 69 + 0.3 * Math.sin(2 * Math.PI * 5.5 * (t - 5)) : 69 - 2 * (t - 8);
      p += jit;
    }
    out.push({ t, p, a });
  }
  return out;
}

export function encode(frames, type = 'lane', lane = 0) {
  const ev = [];
  let on = false, lastT = -1, sentP = 0, sentA = 0, below = 0;
  for (const f of frames) {
    if (!on) {
      if (f.a > C.LEVEL_ON) {
        on = true; below = 0; lastT = f.t; sentP = f.p; sentA = f.a;
        ev.push({ t: f.t, type: type + 'On', lane, u: f.p, vel: f.a, speed: 0 });
      }
      continue;
    }
    if (f.a < C.LEVEL_OFF) {
      below += C.FRAME;
      if (below >= C.OFF_HOLD - 1e-9) {
        ev.push({ t: f.t, type: type + 'Ctl', lane, u: f.p, vel: 0, speed: Math.min(f.t - lastT, C.KEYFRAME) });
        ev.push({ t: f.t, type: type + 'Off', lane, u: f.p, vel: 0, speed: 0 });
        on = false; continue;
      }
    } else below = 0;
    const since = f.t - lastT;
    const moved = Math.abs(f.p - sentP) >= C.PITCH_DB || Math.abs(dB(f.a) - dB(sentA)) >= C.LEVEL_DB;
    if ((moved && since >= C.MIN_GAP - 1e-9) || since >= C.KEYFRAME - 1e-9) {
      ev.push({ t: f.t, type: type + 'Ctl', lane, u: f.p, vel: f.a, speed: Math.min(since, C.KEYFRAME) });
      lastT = f.t; sentP = f.p; sentA = f.a;
    }
  }
  return ev;
}

/** What the unit plays back, sampled at the frames' times: each control dispatched min(speed, PREROLL) early and glided
 *  linearly over `speed` from wherever the value was. */
export function decode(events, frames) {
  // build glide segments in dispatch order
  const segs = [];
  let curP = 57, curA = 0;
  const sorted = events.map((e) => ({ ...e, at: e.type.endsWith('Ctl') ? e.t - Math.min(e.speed, C.PREROLL) : e.t }))
    .sort((x, y) => x.at - y.at);
  for (const e of sorted) {
    if (e.type.endsWith('On')) segs.push({ at: e.at, dur: 0, p: e.u, a: e.vel });
    else if (e.type.endsWith('Ctl')) segs.push({ at: e.at, dur: e.speed, p: e.u, a: e.vel });
    else segs.push({ at: e.at, dur: 0, p: curP, a: 0, off: true });
  }
  const out = [];
  let k = 0, fromP = 57, fromA = 0, seg = null;
  for (const f of frames) {
    while (k < segs.length && segs[k].at <= f.t + 1e-9) {
      // start the next glide from the value reached so far
      if (seg) { const v = valueAt(seg, segs[k].at, fromP, fromA); fromP = v.p; fromA = v.a; }
      seg = segs[k++];
    }
    if (!seg) { out.push({ t: f.t, p: curP, a: curA }); continue; }
    const v = valueAt(seg, f.t, fromP, fromA);
    out.push({ t: f.t, p: v.p, a: v.a });
  }
  return out;
}

function valueAt(seg, t, fromP, fromA) {
  if (seg.dur <= 0 || t >= seg.at + seg.dur) return { p: seg.p, a: seg.a };
  const k = Math.max(0, (t - seg.at) / seg.dur);
  return { p: fromP + (seg.p - fromP) * k, a: fromA + (seg.a - fromA) * k };
}

export function measure(frames, rec) {
  let maxC = 0, sum = 0, n = 0, maxDb = 0;
  for (let i = 0; i < frames.length; i++) {
    if (frames[i].a < 0.05) continue;
    const e = Math.abs(rec[i].p - frames[i].p) * 100;
    maxC = Math.max(maxC, e); sum += e * e; n++;
    maxDb = Math.max(maxDb, Math.abs(dB(rec[i].a) - dB(frames[i].a)));
  }
  return { maxCents: maxC, rmsCents: Math.sqrt(sum / Math.max(1, n)), maxDb };
}

export { INSTRUMENTS };
