# Pure-Python analysis of a mono/stereo 16-bit WAV: Goertzel bins (dB vs strongest) in 0.1 s Hann windows,
# 5 ms RMS envelope roughness, zero-crossing rate. Usage: python3 wavspec.py file.wav [t1 t2 ...]
import sys, wave, struct, math
def load(p):
    w = wave.open(p); n = w.getnframes(); ch = w.getnchannels(); sr = w.getframerate(); sw = w.getsampwidth()
    raw = w.readframes(n)
    if sw == 2: d = struct.unpack('<%dh' % (n * ch), raw); sc = 32768.0
    else: raise SystemExit('16-bit only')
    x = [sum(d[i*ch:(i+1)*ch]) / ch / sc for i in range(n)]
    return x, sr
def goertzel(seg, f, sr):
    k = 2 * math.cos(2 * math.pi * f / sr); s1 = s2 = 0.0
    for v in seg: s0 = v + k * s1 - s2; s2 = s1; s1 = s0
    return s1 * s1 + s2 * s2 - k * s1 * s2
BINS = [196, 300, 400, 550, 700, 900, 1100, 1300, 1550, 1800, 2200, 2700, 3300, 4500, 7000]
def spec(x, sr, t, win=0.1):
    n = int(win * sr); i0 = int(t * sr) - n // 2
    seg = [x[i0 + i] * (0.5 - 0.5 * math.cos(2 * math.pi * i / (n - 1))) for i in range(n)]
    p = {f: goertzel(seg, f, sr) for f in BINS}
    m = max(p.values()) or 1e-30
    return {f: 10 * math.log10(max(p[f], 1e-30) / m) for f in BINS}
def rough(x, sr, t, span=0.5):
    w = int(0.005 * sr); i0 = int((t - span / 2) * sr); r = []
    for j in range(int(span * sr) // w):
        s = x[i0 + j * w: i0 + (j + 1) * w]; r.append(math.sqrt(sum(v * v for v in s) / w))
    zc = sum(1 for i in range(i0 + 1, i0 + int(span * sr)) if (x[i] >= 0) != (x[i - 1] >= 0)) / span
    pk = max(abs(v) for v in x[i0:i0 + int(span * sr)])
    return min(r) / max(max(r), 1e-12), zc, pk
if __name__ == '__main__':
    x, sr = load(sys.argv[1])
    ts = [float(a) for a in sys.argv[2:]] or [i * 0.5 + 0.5 for i in range(int(len(x) / sr * 2) - 1)]
    for t in ts:
        s = spec(x, sr, t); mm, zc, pk = rough(x, sr, t)
        print('%5.1fs pk %.3f env %.2f zc %5d | ' % (t, pk, mm, zc) + ' '.join('%d:%d' % (f, round(s[f])) for f in BINS))
