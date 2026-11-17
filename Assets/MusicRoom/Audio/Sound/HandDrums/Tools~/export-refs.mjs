// Writes the reference data the hand-drum EditMode tests read (Tests/EditMode/Audio/HandDrums/Reference~ and
// Tests/EditMode/HandDrums/Reference~), from the hand-drum lab outputs when present (else the frozen copies stay), and the Node DRY renders of the
// spec's demo phrases (handdrums.md 8) per drum for the Unity null test (Tests/EditMode/Audio/HandDrums/Reference~/dry/<name>.f32:
// conga then tumba, float32, `frames` samples each).
//
//   node export-refs.mjs
//
// demo-calls.json lists every engine call the lab's render.mjs renderEvents() makes, at its exact sample, in post order,
// so the C# test replays the same calls through IHandDrumSound (Stroke / RestCanonical / Lift) with the same doubles.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { HandDrums, SR, STROKES, RESTS } from '../../../../../../Tools/SoundLab/handdrums/engine/handdrums.reference.mjs';
import { tumbao, congaLine, ladder, techniques } from '../../../../../../Tools/SoundLab/handdrums/tools/patterns.mjs';
import { fnv1a32, hex } from '../../../../../../Tools/SoundLab/burst/lab/common.mjs';
import { renderEvents } from '../../../../../../Tools/SoundLab/handdrums/tools/render.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const LAB = fileURLToPath(new URL('../../../../../../Tools/SoundLab/handdrums/', import.meta.url));
const AUD = path.join(here, '../../../../Tests/EditMode/Audio/HandDrums/Reference~');
const INT = path.join(here, '../../../../Tests/EditMode/HandDrums/Reference~');
const DRY = path.join(AUD, 'dry');
for (const d of [AUD, INT, DRY]) fs.mkdirSync(d, { recursive: true });
// The lab's own test-vector outputs (out/*) are copied when present; without them the Reference~ copies stay as they are
// (frozen goldens: the hand-drum reference engine in Tools/SoundLab reproduces the demo renders below bit for bit).
const cp = (from, to) => {
  if (!fs.existsSync(from)) { console.log('kept', path.relative(path.join(here, '../../../..'), to), '(no lab output to copy)'); return; }
  fs.copyFileSync(from, to); console.log('copied', path.basename(from), '->', path.relative(path.join(here, '../../../..'), to));
};

cp(path.join(LAB, 'out/test-vectors.json'), path.join(AUD, 'test-vectors.json'));
cp(path.join(LAB, 'out/vec-scenario-conga.f32'), path.join(AUD, 'vec-scenario-conga.f32'));
cp(path.join(LAB, 'out/vec-scenario-tumba.f32'), path.join(AUD, 'vec-scenario-tumba.f32'));
cp(path.join(LAB, 'out/renders.json'), path.join(AUD, 'renders.json'));
cp(path.join(LAB, 'out/verify.json'), path.join(AUD, 'verify.json'));
cp(path.join(LAB, 'out/handdrums-tables.json'), path.join(AUD, 'handdrums-tables.json'));
cp(path.join(LAB, 'out/interaction-cases.json'), path.join(INT, 'interaction-cases.json'));
cp(path.join(LAB, 'out/sweep.json'), path.join(INT, 'sweep.json'));
cp(path.join(LAB, 'out/visual-vectors.json'), path.join(INT, 'visual-vectors.json'));

// ---- the demo phrases: a recording engine that logs every call renderEvents() makes, then renders them
const KIND = (k) => (k === 'mute' ? 'open' : k === 'slapClosed' ? 'slap' : k);
function callsOf(ev) {
  const at = (t) => Math.round(t * SR), calls = [];
  for (let i = 0; i < ev.length; i++) {
    const e = ev[i];
    if (e.rest) { calls.push({ op: 'rest', at: at(e.t), drum: e.drum, hand: e.hand, rest: e.rest, theta: Math.PI }); calls.push({ op: 'lift', at: at(e.t + 0.6), drum: e.drum, hand: e.hand }); continue; }
    const theta = e.hand === 0 ? -0.35 : 0.35;
    calls.push({ op: 'stroke', at: at(e.t), drum: e.drum, hand: e.hand, kind: e.stroke, u: e.u, theta });
    if (e.stroke === 'heel' || e.stroke === 'toe') {
      const next = ev.slice(i + 1).find((q) => q.hand === e.hand && !q.rest);
      calls.push({ op: 'rest', at: at(e.t + 0.012), drum: e.drum, hand: e.hand, rest: e.stroke === 'heel' ? 'heel' : 'toe', theta });
      const until = next && next.drum === e.drum ? next.t - 0.002 : e.t + 0.25;
      calls.push({ op: 'lift', at: at(until), drum: e.drum, hand: e.hand });
    }
  }
  return calls;
}
// the toe rest of render.mjs: pads at (0.5, pi), as a canonical rest kind
const REST_PTS = { ...RESTS, toe: [{ part: 'pads', rho: 0.5, theta: Math.PI }] };
function play(eng, c) {
  if (c.op === 'lift') { eng.lift(c.at, c.drum, c.hand); return; }
  if (c.op === 'rest') { eng.rest(c.at, c.drum, c.hand, REST_PTS[c.rest].map((q) => ({ ...q, theta: q.theta + c.theta }))); return; }
  // playStroke, verbatim
  const k = KIND(c.kind);
  for (const p of STROKES[k](c.u)) eng.patch(c.at + Math.round((p.dly || 0) * SR), c.drum, c.hand, p.part, p.u, p.pts.map((q) => ({ rho: q.rho, theta: q.theta + c.theta })));
  const restKind = c.kind === 'mute' ? 'mute' : c.kind === 'slapClosed' ? 'slapClosed' : null;
  if (restKind) { const r0 = c.at + Math.round(0.012 * SR); eng.rest(r0, c.drum, c.hand, RESTS[restKind].map((q) => ({ ...q, theta: q.theta + c.theta }))); eng.lift(r0 + Math.round(0.25 * SR), c.drum, c.hand); }
}
const jobs = [['ladder-open', ladder('open')], ['ladder-bass', ladder('bass')], ['ladder-slap', ladder('slap')], ['techniques', techniques()], ['tumbao', tumbao()], ['congaline', congaLine()]];
const phrases = [];
for (const [name, job] of jobs) {
  const calls = callsOf(job.ev), frames = Math.round(job.seconds * SR);
  const eng = new HandDrums(SR, { seed: 1 });
  for (const c of calls) play(eng, c);
  const o = [new Float32Array(frames), new Float32Array(frames)];
  for (let s = 0; s < frames; s += 256) { const m = Math.min(256, frames - s); eng.process([o[0].subarray(s, s + m), o[1].subarray(s, s + m)], m); }
  const buf = Buffer.alloc(frames * 8);
  Buffer.from(o[0].buffer).copy(buf, 0); Buffer.from(o[1].buffer).copy(buf, frames * 4);
  fs.writeFileSync(path.join(DRY, name + '.f32'), buf);
  // the replayed calls must render exactly what render.mjs renders
  const ref = renderEvents(job.ev, job.seconds);
  if (fnv1a32(ref.conga) !== fnv1a32(o[0]) || fnv1a32(ref.tumba) !== fnv1a32(o[1])) throw new Error(name + ': the call list does not reproduce renderEvents');
  let pk = 0; for (const x of o) for (const v of x) pk = Math.max(pk, Math.abs(v));
  phrases.push({ name, seconds: job.seconds, frames, peak: pk, fnv: [hex(fnv1a32(o[0])), hex(fnv1a32(o[1]))], calls });
  console.log(name.padEnd(12), frames, 'frames', calls.length, 'calls', 'peak', pk.toFixed(3), phrases.at(-1).fnv.join(' '));
}
fs.writeFileSync(path.join(AUD, 'demo-calls.json'), JSON.stringify({ sr: SR, seed: 1, dryDir: 'dry', phrases }));
console.log('demo-calls.json', phrases.length, 'phrases');
