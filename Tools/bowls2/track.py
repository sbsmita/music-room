# Per window: fundamental (f0) and mean noise-bin level in dBFS (sine amplitude), and their difference.
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from wavspec import load, goertzel
x, sr = load(sys.argv[1]); f0 = float(sys.argv[2]) if len(sys.argv) > 2 else 196.0
step = float(sys.argv[3]) if len(sys.argv) > 3 else 0.25
NB = [700, 900, 1300, 1800, 2700, 3300]
n = int(0.1 * sr); win = [0.5 - 0.5 * math.cos(2 * math.pi * i / (n - 1)) for i in range(n)]
db = lambda p: 20 * math.log10(max(2 * math.sqrt(p) / (n * 0.5), 1e-9))
t = 0.0; rels = []
while (t + 0.1) * sr < len(x):
    i0 = int(t * sr); seg = [x[i0 + i] * win[i] for i in range(n)]
    pf = goertzel(seg, f0, sr); pn = sum(goertzel(seg, f, sr) for f in NB) / len(NB)
    if pf > 0 and pn > 0:
        print('%6.2f fund %6.1f noise %6.1f rel %6.1f relF %6.1f' % (t, db(pf), db(pn), db(pn) - db(pf), db(pn) - max(db(pf), -50.0)))
    t += step
