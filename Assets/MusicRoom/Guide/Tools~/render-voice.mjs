// Renders Sunny's table lines (sunny-lines.mjs) with the project's own voice pipeline and writes everything the
// app and the tests read (Docs/11-first-five/sunny-tour.md section 5.4, route B; the same route the existing
// tip_arrange and deck_guide_* clips took). Since the platform merge (Docs/12-new-instruments/BUILD-GUIDE.md 7.2) the lines
// are a module line file like every instrument's:
//   - Guide/Resources/Voice/<id>.wav (22.05 kHz mono, 16-bit) and, for a new clip, its .meta (the import settings of
//     every voice clip: Decompress On Load, Preload, Force To Mono, Vorbis 0.7; Editor/GuideVoiceImport.cs guards them);
//   - Guide/Resources/GuideLines/FirstFive.json ({"lines":[{"id","text","spoken"}]}) and
//     Guide/Resources/VoiceManifests/FirstFive.json ({"<id>": {"file","seconds","rate"}}), merged after the Guide's 38;
//   - the changed lines of the Guide's own 38 (CHANGED) in place in Voice/manifest.json and guide-lines.json;
//   - Guide/FirstRun/FirstFiveLines.cs (the ids, generated: never edit it by hand).
// A line is rendered when its WAV is missing or its words changed (or with --all); the others keep their clips.
// The voice: espeak-voice.mjs render() next to this file (eSpeak NG en-us+Annie, 150 wpm, pitch 55, range 70, then
// silence trim to 30 ms, peak 0.89, 5 ms fades). The robot colour is added in the app.
//
//   node Assets/MusicRoom/Guide/Tools~/render-voice.mjs [projectDir] [--all]
//
// Needs espeak-ng on PATH (brew install espeak-ng) or ESPEAK=/path/to/espeak-ng. No network, no other service.
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync, copyFileSync, existsSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { randomBytes, createHash } from 'node:crypto';

const args = process.argv.slice(2).filter(a => !a.startsWith('--'));
const ALL = process.argv.includes('--all');
const HERE = dirname(fileURLToPath(import.meta.url));
const PROJ = args[0] || join(HERE, '..', '..', '..', '..');
const GUIDE = join(PROJ, 'Assets', 'MusicRoom', 'Guide');
const VOICE_DIR = join(GUIDE, 'Resources', 'Voice');
const LEAD = 0.14;

const { render, VOICE } = await import(pathToFileURL(join(HERE, 'espeak-voice.mjs')).href);
const table = await import(pathToFileURL(join(HERE, 'sunny-lines.mjs')).href);
const { LINES, SPOKEN, CHANGED } = table;

const manifestPath = join(VOICE_DIR, 'manifest.json');
const linesPath = join(GUIDE, 'Resources', 'guide-lines.json');
const ffLinesPath = join(GUIDE, 'Resources', 'GuideLines', 'FirstFive.json');
const ffManifestPath = join(GUIDE, 'Resources', 'VoiceManifests', 'FirstFive.json');
const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
const guide = JSON.parse(readFileSync(linesPath, 'utf8'));
const base = guide.lines.slice(0, 38).map(l => l.id);   // the Guide's own 38, in their slot order
const oldLines = existsSync(ffLinesPath) ? JSON.parse(readFileSync(ffLinesPath, 'utf8')).lines : [];
const oldManifest = existsSync(ffManifestPath) ? JSON.parse(readFileSync(ffManifestPath, 'utf8')) : {};
const oldText = Object.fromEntries(oldLines.map(l => [l.id, l]));
const metaTemplate = readFileSync(join(VOICE_DIR, 'nod_vr.wav.meta'), 'utf8');

const tmp = mkdtempSync(join(tmpdir(), 'sunny-firstfive-'));
const rendered = {};
try {
  for (const [id, caption] of [...Object.entries(CHANGED), ...Object.entries(LINES)]) {
    if (base.includes(id) && !(id in CHANGED)) throw new Error(`${id} is one of the Guide's 38: list it in CHANGED`);
    const spoken = SPOKEN[id] || caption;
    const wavPath = join(VOICE_DIR, `${id}.wav`);
    const prev = id in CHANGED ? guide.lines.find(l => l.id === id) : oldText[id];
    const prevSeconds = id in CHANGED ? manifest[id]?.seconds : oldManifest[id]?.seconds;
    const same = prev && prev.text === caption && (prev.spoken || prev.text) === spoken && existsSync(wavPath) && prevSeconds > 0;
    if (same && !ALL) { rendered[id] = { caption, spoken, seconds: prevSeconds, rate: 22050 }; continue; }
    const wav = join(tmp, `${id}.wav`);
    const { rate, seconds } = render(spoken, wav, VOICE);
    if (rate !== 22050) throw new Error(`${id}: ${rate} Hz`);
    copyFileSync(wav, wavPath);
    const meta = join(VOICE_DIR, `${id}.wav.meta`);
    if (!existsSync(meta)) writeFileSync(meta, metaTemplate.replace(/^guid: [0-9a-f]{32}$/m, `guid: ${randomBytes(16).toString('hex')}`));
    rendered[id] = { caption, spoken, seconds: +seconds.toFixed(3), rate };
    const words = caption.split(/\s+/).length;
    console.log(`rendered ${id.padEnd(18)} ${seconds.toFixed(3).padStart(6)} s ${String(words).padStart(2)} w  ${caption}`);
  }
} finally {
  rmSync(tmp, { recursive: true, force: true });
}

// The Guide's own 38: in place, the changed ones re-measured.
const outManifest = {};
for (const id of base) outManifest[id] = rendered[id] ? { file: `${id}.wav`, seconds: rendered[id].seconds, rate: 22050 } : manifest[id];
writeFileSync(manifestPath, JSON.stringify(outManifest, null, 1) + '\n');
const lineOf = (id, file) => {
  const r = rendered[id];
  return { id, text: r.caption, spoken: r.spoken, file, seconds: r.seconds, lineSeconds: +(r.seconds + LEAD).toFixed(4) };
};
guide.lines = guide.lines.slice(0, 38).map(l => (rendered[l.id] ? lineOf(l.id, l.file) : l));
guide.lineCount = guide.lines.length;
const sha = createHash('sha1').update(readFileSync(join(HERE, 'sunny-lines.mjs'))).digest('hex');
guide.sourceSha1['sunny-lines.mjs'] = sha;
for (const f of ['Guide/Tools~/sunny-lines.mjs', 'Guide/Tools~/render-voice.mjs', 'Guide/Tools~/espeak-voice.mjs'])
  if (!guide.generatedFrom.includes(f)) guide.generatedFrom.push(f);
guide.unityLines = (guide.unityLines || []).filter(s => !s.startsWith('first five:'));
guide.unityLines.push('first five: intro_vr / intro_desk / swipe_skip reworded (sunny-tour.md 5.1); the first-run script and every ' +
  'contextual tip are the module line file GuideLines/FirstFive.json (+ VoiceManifests/FirstFive.json). Table: Guide/Tools~/sunny-lines.mjs; ' +
  'rendered by Guide/Tools~/render-voice.mjs with Guide/Tools~/espeak-voice.mjs render().');
writeFileSync(linesPath, JSON.stringify(guide, null, 1) + '\n');

// The module line file and its voice manifest (BUILD-GUIDE.md 7.2), in table order.
const ids = Object.keys(LINES);
writeFileSync(ffLinesPath, JSON.stringify({ lines: ids.map(id => ({ id, text: rendered[id].caption, spoken: rendered[id].spoken })) }, null, 1) + '\n');
const ffManifest = {};
for (const id of ids) ffManifest[id] = { file: `${id}.wav`, seconds: rendered[id].seconds, rate: 22050 };
writeFileSync(ffManifestPath, JSON.stringify(ffManifest, null, 1) + '\n');

// The generated C# id list.
const cs = s => '"' + s.replace(/\\/g, '\\\\').replace(/"/g, '\\"') + '"';
const head = `// GENERATED by Guide/Tools~/render-voice.mjs from Guide/Tools~/sunny-lines.mjs (sha1 ${sha}). Do not edit.\n`;
writeFileSync(join(GUIDE, 'FirstRun', 'FirstFiveLines.cs'), head +
`// Sunny's first-five-minutes lines (Docs/11-first-five/sunny-tour.md 5.3): the first-run script and the tips, in
// the order of their module line file (Resources/GuideLines/FirstFive.json, captions; Resources/VoiceManifests/
// FirstFive.json, clip lengths), which the Guide merges after its own 38 (GuideLines.Merged; GuideVoice's voice slots).
namespace MusicRoom.Guide
{
    public static class FirstFiveLines
    {
        public static readonly string[] Ids =
        {
${ids.map(id => '            ' + cs(id) + ',').join('\n')}
        };
    }
}
`);
for (const id of Object.keys(CHANGED)) console.log(`changed ${id}: ${rendered[id].seconds}`);
console.log(`${ids.length} lines in GuideLines/FirstFive.json; ${guide.lineCount} in guide-lines.json`);
