// Sunny's voice renderer: eSpeak NG (open source, offline; https://github.com/espeak-ng/espeak-ng) -> 16-bit WAV ->
// silence trimmed to 30 ms either side, peak normalised to 0.89 (about -1 dBFS), 5 ms fades in and out so a cut never
// clicks. The voice is plain and clear on purpose: the robot colour is added live in the app (GuideVoice), so the
// recordings stay as intelligible as possible. Only the rendered audio ships; eSpeak NG itself does not.
// Used by render-voice.mjs:  import { render, VOICE } from './espeak-voice.mjs';
// Needs espeak-ng on PATH (brew install espeak-ng) or ESPEAK=/path/to/espeak-ng.
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';

const ESPEAK = process.env.ESPEAK || 'espeak-ng';
// en-us with the smoother "Annie" variant, 150 words a minute, a little above mid pitch and a
// wide pitch range so it sounds friendly and expressive rather than flat. The robot character
// comes from a light effect in the app, not from a flat delivery.
export const VOICE = { voice: 'en-us+Annie', speed: 150, pitch: 55, range: 70, gap: 0, amplitude: 120 };
const PEAK = 0.89; // about -1 dBFS
const PAD = 0.03; // s of silence kept before and after the words

function readWav(buf) {
  if (buf.toString('ascii', 0, 4) !== 'RIFF' || buf.toString('ascii', 8, 12) !== 'WAVE') throw new Error('not a WAV');
  let off = 12, rate = 22050, bits = 16, channels = 1, data = null;
  while (off + 8 <= buf.length) {
    const id = buf.toString('ascii', off, off + 4);
    let size = buf.readUInt32LE(off + 4);
    const body = off + 8;
    if (id === 'fmt ') {
      channels = buf.readUInt16LE(body + 2);
      rate = buf.readUInt32LE(body + 4);
      bits = buf.readUInt16LE(body + 14);
    } else if (id === 'data') {
      // eSpeak NG writing to a file it cannot seek may leave the size at 0 or 0xffffffff.
      if (size === 0 || size > buf.length - body) size = buf.length - body;
      data = buf.subarray(body, body + size);
      break;
    }
    off = body + size + (size & 1);
  }
  if (!data || bits !== 16 || channels !== 1) throw new Error(`unexpected WAV (${bits} bit, ${channels} ch)`);
  const n = data.length >> 1;
  const s = new Float32Array(n);
  for (let i = 0; i < n; i++) s[i] = data.readInt16LE(i * 2) / 32768;
  return { rate, s };
}

function writeWav(path, rate, s) {
  const buf = Buffer.alloc(44 + s.length * 2);
  buf.write('RIFF', 0, 'ascii');
  buf.writeUInt32LE(36 + s.length * 2, 4);
  buf.write('WAVE', 8, 'ascii');
  buf.write('fmt ', 12, 'ascii');
  buf.writeUInt32LE(16, 16);
  buf.writeUInt16LE(1, 20);
  buf.writeUInt16LE(1, 22);
  buf.writeUInt32LE(rate, 24);
  buf.writeUInt32LE(rate * 2, 28);
  buf.writeUInt16LE(2, 32);
  buf.writeUInt16LE(16, 34);
  buf.write('data', 36, 'ascii');
  buf.writeUInt32LE(s.length * 2, 40);
  for (let i = 0; i < s.length; i++) buf.writeInt16LE(Math.max(-32768, Math.min(32767, Math.round(s[i] * 32767))), 44 + i * 2);
  writeFileSync(path, buf);
}

// Cut the leading and trailing silence (keeping PAD), scale to PEAK, and fade the ends in and out
// over 5 ms so a cut never clicks.
function tidy(rate, s) {
  let peak = 0;
  for (let i = 0; i < s.length; i++) peak = Math.max(peak, Math.abs(s[i]));
  if (peak === 0) throw new Error('silent render');
  const floor = peak * 0.02;
  let a = 0, b = s.length - 1;
  while (a < b && Math.abs(s[a]) < floor) a++;
  while (b > a && Math.abs(s[b]) < floor) b--;
  const pad = Math.round(PAD * rate);
  a = Math.max(0, a - pad);
  b = Math.min(s.length - 1, b + pad);
  const out = s.slice(a, b + 1);
  const k = PEAK / peak;
  for (let i = 0; i < out.length; i++) out[i] *= k;
  const fade = Math.min(out.length >> 1, Math.round(0.005 * rate));
  for (let i = 0; i < fade; i++) {
    out[i] *= i / fade;
    out[out.length - 1 - i] *= i / fade;
  }
  return out;
}

export function render(text, wavPath, v = VOICE) {
  execFileSync(ESPEAK, ['-v', v.voice, '-s', String(v.speed), '-p', String(v.pitch), '-P', String(v.range), '-g', String(v.gap), '-a', String(v.amplitude), '-w', wavPath, text]);
  const { rate, s } = readWav(readFileSync(wavPath));
  const out = tidy(rate, s);
  writeWav(wavPath, rate, out);
  return { rate, seconds: out.length / rate };
}
