# Envelope roughness: rms of the second difference of consecutive 5 ms RMS values over their mean, per 0.1 s window;
# median and p90 over [t0, t1]. A swell or a slow warble scores near 0, broadband noise about 0.3 or more.
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from wavspec import load
def rough(x, sr, t0, t1):
    w = sr // 200; e = []
    for j in range(int(t0 * sr), int(t1 * sr) - w, w):
        e.append(math.sqrt(sum(v * v for v in x[j:j + w]) / w))
    r = []
    for k in range(0, len(e) - 20, 10):
        s = e[k:k + 20]; m = sum(s) / 20
        if m <= 0: continue
        d2 = [s[j + 1] - 2 * s[j] + s[j - 1] for j in range(1, 19)]
        r.append(math.sqrt(sum(v * v for v in d2) / 18) / m)
    r.sort()
    return r[len(r) // 2], r[int(len(r) * 0.9)]
if __name__ == '__main__':
    x, sr = load(sys.argv[1]); t0, t1 = float(sys.argv[2]), float(sys.argv[3])
    a, b = rough(x, sr, t0, t1)
    print('%s %.1f-%.1f s: roughness median %.3f p90 %.3f' % (os.path.basename(sys.argv[1]), t0, t1, a, b))
