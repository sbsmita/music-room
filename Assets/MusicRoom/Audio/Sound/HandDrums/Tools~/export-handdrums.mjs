// Exports the identity of the Node reference hand drums (Tools/SoundLab/handdrums/engine/handdrums.reference.mjs, v2) as a
// binary table for the Unity unit (Assets/MusicRoom/Audio/Sound/HandDrums/HandDrumsTables.cs reads it). Every value is
// written at the precision the reference HOLDS it: Float32Array entries as float32 bits, JS numbers and Float64Array
// entries as float64, so the C# unit starts from the bit-identical tables of buildDrum() (handdrums.md 2.2: "the port
// loads the exported table; it does not re-draw"). out/handdrums-tables.json (version 2) is the same identity at 9
// significant digits; the EditMode test HandDrumsTableTests checks this table against it and against the FNVs of
// out/test-vectors.json.
//
//   node export-handdrums.mjs [out.bytes]      (default: ../Resources/MusicRoomHandDrums.bytes)
//
// Format "HDT1": uint32 record count, then records: uint8 name length, ASCII name, uint8 type ('f' float32, 'd' float64,
// 'i' int32, 'u' uint32), uint32 count, little-endian values. Names are "<drum>.<field>" (drum conga | tumba) or "k.<name>".
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as E from '../../../../../../Tools/SoundLab/handdrums/engine/handdrums.reference.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const outPath = process.argv[2] ?? path.join(here, '../Resources/MusicRoomHandDrums.bytes');
const recs = [];
const put = (name, type, values) => recs.push({ name, type, values: Array.from(values) });
const f = (n, a) => put(n, 'f', a);
const d = (n, a) => put(n, 'd', Array.isArray(a) || ArrayBuffer.isView(a) ? a : [a]);
const i = (n, a) => put(n, 'i', Array.isArray(a) || ArrayBuffer.isView(a) ? a : [a]);
const u = (n, a) => put(n, 'u', [a >>> 0]);

// constants the unit hard-codes (checked by the tests against these)
d('k.OUTPUT_TRIM_DB', E.OUTPUT_TRIM_DB); d('k.POP_K', E.POP_K); d('k.Y2_REL', E.Y2_REL); d('k.GLIDE_EPS', E.GLIDE_EPS);
d('k.CULL2', E.CULL2); d('k.FLIGHT_S', E.FLIGHT_S); d('k.CONTACT_MAX_S', E.CONTACT_MAX_S); d('k.BASS_PADS_U', E.BASS_PADS_U);
i('k.POP_MIN', E.POP_MIN); i('k.POP_MAX', E.POP_MAX); i('k.CAP_MIN', E.CAP_MIN); d('k.CORE_HZ', E.CORE_HZ);
d('k.LOUDER_HYST', E.LOUDER_HYST); d('k.FADE_DB', E.FADE_DB); i('k.SUB', E.SUB); i('k.SLOTS', E.SLOTS); i('k.SHAPE_N', E.SHAPE_N); i('k.MAX_M', E.MAX_M);
d('k.U_V0', [E.U_V0.palm, E.U_V0.fingers]); d('k.U_V1', E.U_V1); d('k.U_EXP', E.U_EXP);
for (const p of E.PART_LIST) { const P = E.PARTS[p]; d('part.' + p, [P.m, P.k, P.alpha, P.lam, P.b, P.R, P.cup, P.Vmin, P.Vmax, P.cap ?? 0]); }

for (const nm of E.DRUM_NAMES) {
  const t = E.buildDrum(nm), k = nm + '.', n = t.n, nPad = t.nPad;
  i(k + 'n', n); i(k + 'nPad', nPad); i(k + 'G', t.G); i(k + 'gCav', t.gCav);
  d(k + 'a', t.a); d(k + 'sigma', t.sigma); d(k + 'T', t.T); d(k + 'cm', t.cm); d(k + 'f01m', t.f01m);
  d(k + 'kappa', t.kappa); d(k + 'level', t.level); d(k + 'glideMax', t.D.glideMax); u(k + 'seed', t.D.seed);
  f(k + 'shape', t.shape); f(k + 'avgTab', t.avgTab); f(k + 'go', t.go);
  // the Drum constructor's output gains: outGain = level 10^(trim/20), bank go = f32(go outGain) (0 for padding)
  const outGain = t.level * Math.pow(10, E.OUTPUT_TRIM_DB / 20); d(k + 'outGain', outGain);
  f(k + 'goBank', Float32Array.from({ length: nPad }, (_, q) => (q < n ? Math.fround(t.go[q] * outGain) : 0)));
  // the Drum constructor's cos / sin of w0 (Math.cos, Math.sin in double), so no libm difference reaches the poles
  const cr0 = new Float64Array(nPad), ci0 = new Float64Array(nPad);
  for (let q = 0; q < n; q++) { cr0[q] = Math.cos(t.w0[q]); ci0[q] = Math.sin(t.w0[q]); }
  for (const [a, v] of [['fHz', t.fHz], ['r0', t.r0], ['w0', t.w0], ['cr0', cr0], ['ci0', ci0], ['Mk', t.Mk], ['om', t.om], ['kap', t.kap], ['eW', t.eW], ['cPh', t.cPh], ['sPh', t.sPh], ['radW', t.radW]]) d(k + a, v);
  i(k + 'kind', t.kind); i(k + 'mm', t.mm); i(k + 'orient', t.orient); i(k + 'glideOn', t.glideOn);
  i(k + 'glideList', t.glideList); i(k + 'gClass', t.gClass); i(k + 'shedOrder', t.shedOrder);
  d(k + 't60', t.modes.map((m) => m.t60)); d(k + 'nReal', t.modes.map((m) => m.nReal ?? 0)); i(k + 'modeN', t.modes.map((m) => m.n ?? 0));
}

let size = 8;
for (const r of recs) size += 1 + r.name.length + 1 + 4 + r.values.length * (r.type === 'd' ? 8 : 4);
const b = Buffer.alloc(size);
b.write('HDT1', 0, 'ascii'); b.writeUInt32LE(recs.length, 4);
let p = 8;
for (const r of recs) {
  b.writeUInt8(r.name.length, p++); b.write(r.name, p, 'ascii'); p += r.name.length;
  b.write(r.type, p++, 'ascii'); b.writeUInt32LE(r.values.length, p); p += 4;
  for (const v of r.values) {
    if (r.type === 'f') { b.writeFloatLE(v, p); p += 4; }
    else if (r.type === 'd') { b.writeDoubleLE(v, p); p += 8; }
    else if (r.type === 'i') { b.writeInt32LE(v, p); p += 4; }
    else { b.writeUInt32LE(v >>> 0, p); p += 4; }
  }
}
fs.writeFileSync(outPath, b);
console.log(`[export-handdrums] ${recs.length} records, ${size} bytes -> ${outPath}`);
