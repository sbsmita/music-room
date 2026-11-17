import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from wavspec import load
x, sr = load(sys.argv[1]); a, b = int(2*sr), int(6*sr); w = sr//1000
e = []
for s in range(a, b-w, w):
    e.append((sum((x[n]-2*x[n-1]+x[n-2])**2 for n in range(s, s+w)), s/sr))
v = sorted(t[0] for t in e); med = v[len(v)//2]
big = [t for t in e if t[0] > 12*med]
print(os.path.basename(sys.argv[1]), 'median d2 %.3g (rms %.2g)' % (med, math.sqrt(med/w)), 'max/med %.1f' % (v[-1]/med), 'clicks', len(big))
print('  click times', ' '.join('%.3f' % t[1] for t in big[:30]))
print('  click d2 rms', ' '.join('%.2g' % math.sqrt(t[0]/w) for t in big[:12]))
