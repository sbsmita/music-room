# Fine averaged spectrum (absolute amplitude, dBFS of a sine), 0.1 s Hann windows averaged over [t0, t1].
import sys, math
sys.path.insert(0, __import__('os').path.dirname(__file__))
from wavspec import load, goertzel
x, sr = load(sys.argv[1]); t0, t1 = float(sys.argv[2]), float(sys.argv[3])
fs = [int(f) for f in sys.argv[4].split(',')] if len(sys.argv) > 4 else list(range(150, 5001, 50))
n = int(0.1 * sr); win = [0.5 - 0.5 * math.cos(2 * math.pi * i / (n - 1)) for i in range(n)]
acc = {f: 0.0 for f in fs}; k = 0; t = t0
while t + 0.1 <= t1:
    i0 = int(t * sr); seg = [x[i0 + i] * win[i] for i in range(n)]
    for f in fs: acc[f] += goertzel(seg, f, sr)
    k += 1; t += 0.05
tot = sum(v * v for v in x[int(t0 * sr):int(t1 * sr)]) / (int(t1 * sr) - int(t0 * sr))
print('rms %.4f' % math.sqrt(tot))
for f in fs:
    a = 2 * math.sqrt(acc[f] / k) / (n * 0.5)
    print('%5d %6.1f %s' % (f, 20 * math.log10(max(a, 1e-9)), '#' * max(0, int(100 + 20 * math.log10(max(a, 1e-9))) // 2)))
