// render.mjs: the listening renders -> Builds/Listen/SoundLab/handdrums/handdrums-*.wav,
// out/renders.json. 48 kHz stereo 24-bit. Dry files: the engine's two sources through a direct-path head model (the
// room lab's: distance law, Woodworth ITD, Brown-Duda shadow; the app spatializes for real). Room files: the same direct
// path + our room unit (Tools/SoundLab/room: image-source ER + 32-line FDN, 'natural'), the group point at the spec's ErOrigin.
// v2 (critique M2): every file is direct path [+ room] -> the app's master (MasterBusV2) at the APP'S OWN input gain
// (0 dB: what the app plays), then ONE linear gain to -18 LUFS integrated, or to -1.2 dBTP if that comes first. Nothing
// else limits or levels (v1 pushed +4..+14.5 dB into the master, so its leveler and limiter worked far harder than in the
// app). The two "-app" files are the app's output itself (room on, no gain afterwards), kept for honesty.
import { mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { HandDrums, playStroke, SR, RESTS } from '../engine/handdrums.reference.mjs';
import { tumbao, congaLine, ladder, techniques } from './patterns.mjs';
import { integratedLufs, truePeakDb, writeWav24 } from '../../burst/lab/common.mjs';
import { RoomEngine, MasterBusV2, VR_ROOM, distGain, bdAlpha, bdCoefs, woodworthItd, C_SOUND, HRTF_FRONT_DB, ER_CENTRE_DY } from '../../room/room-engine.mjs';
import { clickScore } from '../../drums/lib/analysis.mjs';

const LISTEN = fileURLToPath(new URL('../../../../Builds/Listen/SoundLab/handdrums', import.meta.url));
mkdirSync(LISTEN, { recursive: true });
const BLOCK = 256;
// seat frame (Unity): head (0, 1.2, 0); conga head centre left, tumba right, 0.57 m ahead (handdrums.md §4.3)
export const HEAD = [0, 1.2, 0];
export const POS = { conga: [-0.185, 0.75, 0.57], tumba: [0.19, 0.755, 0.57] };
const RIGHT = [1, 0, 0];

// ---- the engine, driven by an event list: strokes, sustained heel-toe rests, explicit rests
export function renderEvents(ev, seconds, { seed = 1 } = {}) {
  const eng = new HandDrums(SR, { seed });
  const n = Math.round(seconds * SR);
  const at = (t) => Math.round(t * SR);
  for (let i = 0; i < ev.length; i++) {
    const e = ev[i];
    if (e.rest) { eng.rest(at(e.t), e.drum, e.hand, RESTS[e.rest].map((q) => ({ ...q, theta: q.theta + Math.PI }))); eng.lift(at(e.t + 0.6), e.drum, e.hand); continue; }
    const theta = e.hand === 0 ? -0.35 : 0.35;                  // each hand plays its own side of the near edge
    playStroke(eng, at(e.t), e.drum, e.hand, e.stroke, e.u, { theta });
    if (e.stroke === 'heel' || e.stroke === 'toe') {            // manoteo: the hand stays on the head until it moves again
      const next = ev.slice(i + 1).find((q) => q.hand === e.hand && !q.rest);
      const pts = e.stroke === 'heel' ? RESTS.heel : [{ part: 'pads', rho: 0.5, theta: Math.PI }];
      eng.rest(at(e.t + 0.012), e.drum, e.hand, pts.map((q) => ({ ...q, theta: q.theta + theta })));
      const until = next && next.drum === e.drum ? next.t - 0.002 : e.t + 0.25;
      eng.lift(at(until), e.drum, e.hand);
    }
  }
  const o = [new Float32Array(n), new Float32Array(n)];
  for (let s = 0; s < n; s += BLOCK) { const m = Math.min(BLOCK, n - s); eng.process([o[0].subarray(s, s + m), o[1].subarray(s, s + m)], m); }
  return { conga: o[0], tumba: o[1], eng };
}
// ---- direct path (as the room A/B renderer's directStereo)
export function directStereo(srcs, n) {
  const L = new Float32Array(n), Rr = new Float32Array(n), gH = Math.pow(10, HRTF_FRONT_DB / 20);
  for (const [name, x] of Object.entries(srcs)) {
    const p = POS[name], D = [p[0] - HEAD[0], p[1] - HEAD[1], p[2] - HEAD[2]], d = Math.hypot(...D);
    const cr = Math.max(-1, Math.min(1, (D[0] * RIGHT[0] + D[1] * RIGHT[1] + D[2] * RIGHT[2]) / d));
    const lat = Math.asin(cr), itd = woodworthItd(lat), g = distGain(d) * gH;
    for (const [out, delay, theta] of [[L, (d / C_SOUND + itd / 2) * SR, Math.PI - Math.acos(cr)], [Rr, (d / C_SOUND - itd / 2) * SR, Math.acos(cr)]]) {
      const [b0, b1, a1] = bdCoefs(bdAlpha(theta), SR), di = Math.floor(delay), fr = delay - di;
      let x1 = 0, y1 = 0;
      for (let i = 0; i < n; i++) { const j = i - di, v = (j >= 0 ? x[j] * (1 - fr) : 0) + (j - 1 >= 0 ? x[j - 1] * fr : 0); const y = b0 * v + b1 * x1 - a1 * y1; x1 = v; y1 = y; out[i] += y * g; }
    }
  }
  return [L, Rr];
}
// the spec's Audio.ErOrigin, rig-local (0, 0.40, 0.10), with the rig at the seat home (0, 0, 0.57): the room engine adds
// ER_CENTRE_DY[drums group] to the point it is given, so it is given ErOrigin minus that
export const ER_ORIGIN = [0.0025, 0.40, 0.67];
export function withRoom(srcs, direct, n, preset = 'natural') {
  const eng = new RoomEngine(SR); eng.setRoom(VR_ROOM); eng.setAmount(preset); eng.setListener(HEAD, RIGHT);
  const centre = [ER_ORIGIN[0], ER_ORIGIN[1] - ER_CENTRE_DY[2], ER_ORIGIN[2]];
  const mono = Float32Array.from(srcs.conga, (v, i) => v + srcs.tumba[i]);
  const L = Float32Array.from(direct[0]), Rr = Float32Array.from(direct[1]);
  for (let s = 0; s < n; s += BLOCK) { const m = Math.min(BLOCK, n - s); eng.setGroupPosition(2, centre); eng.process([null, null, mono.subarray(s, s + m)], L.subarray(s, s + m), Rr.subarray(s, s + m), m); }
  return [L, Rr];
}
// the app's master (MasterBusV2: trim, leveler, shelf, true-peak limiter at -1 dBTP), block by block, at the app's own
// input gain (0 dB), then one linear gain (none for an "-app" file). Reports what the master did and what set the gain.
export const TARGET_LUFS = -18, CEILING_DBTP = -1.2, APP_FILE_TRIM_DB = -0.25;
export function appMaster(ch) {
  const l = Float32Array.from(ch[0]), r = Float32Array.from(ch[1]), mb = new MasterBusV2(SR);
  for (let s = 0; s < l.length; s += BLOCK) { const m = Math.min(BLOCK, l.length - s); mb.process(l.subarray(s, s + m), r.subarray(s, s + m), m); }
  return { out: [l, r], st: mb.stats };
}
export function master(ch, { normalise = true } = {}) {
  const app = appMaster(ch), st = app.st, appLufs = integratedLufs(app.out), appTp = truePeakDb(app.out);
  // an "-app" file is the app's output 0.25 dB down: the master's ceiling is exactly -1.0 dBTP, the listening rule is below -1
  const gL = TARGET_LUFS - appLufs, gT = CEILING_DBTP - appTp, gDb = normalise ? Math.min(gL, gT) : APP_FILE_TRIM_DB, g = Math.pow(10, gDb / 20);
  const out = app.out.map((c) => Float32Array.from(c, (v) => v * g));
  return { out, appLufs, appTp, gainDb: gDb, setBy: !normalise ? 'app level (-0.25 dB)' : gT < gL ? 'true peak' : 'loudness', lufs: integratedLufs(out), tp: truePeakDb(out),
    limMaxGrDb: +st.maxGrDb.toFixed(2), limPctOver1dB: +(100 * st.limActive1dB / st.frames).toFixed(2), levMaxGrDb: +st.levMaxGrDb.toFixed(2) };
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const jobs = [
    ['ladder-open', ladder('open')], ['ladder-bass', ladder('bass')], ['ladder-slap', ladder('slap')],
    ['techniques', techniques()], ['tumbao', tumbao()], ['congaline', congaLine()],
  ];
  const rep = { files: {}, totalBytes: 0, note: 'every file = direct path (head model) [+ room unit natural for -room/-app] -> MasterBusV2 at the app input gain (0 dB) -> one linear gain to -18 LUFS or -1.2 dBTP, whichever is lower (none for -app). Ladders: one file, one gain (relative levels kept).' };
  for (const [key, job] of jobs) {
    const { conga, tumba } = renderEvents(job.ev, job.seconds);
    const n = conga.length;
    const dry = directStereo({ conga, tumba }, n);
    const onsets = job.ev.filter((e) => !e.rest).map((e) => Math.round(e.t * SR));
    const ck = clickScore(Float32Array.from(dry[0]), SR, onsets.flatMap((a) => [a, a + 96]), 0.012);
    const unl = { lufs: integratedLufs(dry), tp: truePeakDb(dry) };
    let pk = 0; for (let i = 0; i < n; i++) pk = Math.max(pk, Math.abs(conga[i]), Math.abs(tumba[i]));
    const versions = [[key, dry, true]];
    if (key === 'tumbao' || key === 'congaline') { const wet = withRoom({ conga, tumba }, dry, n); versions.push([`${key}-room`, wet, true], [`${key}-app`, wet, false]); }
    for (const [name, ch, norm] of versions) {
      const m = master(ch, { normalise: norm });
      const path = `${LISTEN}/handdrums-${name}.wav`;
      const bytes = writeWav24(path, m.out);
      rep.totalBytes += bytes;
      rep.files[name] = { path, seconds: +(n / SR).toFixed(2), lufs: +m.lufs.toFixed(2), truePeakDb: +m.tp.toFixed(2), appLevelLufs: +m.appLufs.toFixed(2), appLevelTpDb: +m.appTp.toFixed(2), gainAfterMasterDb: +m.gainDb.toFixed(2), setBy: m.setBy,
        masterAtAppLevel: { limMaxGrDb: m.limMaxGrDb, limPctOver1dB: m.limPctOver1dB, levMaxGrDb: m.levMaxGrDb }, unitPeakDbFS: +(20 * Math.log10(pk)).toFixed(1), dryPlrDb: +(unl.tp - unl.lufs).toFixed(1), clickScore: +ck.score.toFixed(2) };
      console.log(name.padEnd(16), JSON.stringify(rep.files[name]));
    }
  }
  rep.totalMB = +(rep.totalBytes / 1e6).toFixed(1);
  writeFileSync(new URL('../out/renders.json', import.meta.url), JSON.stringify(rep, null, 1));
  console.log('total MB', rep.totalMB);
}
