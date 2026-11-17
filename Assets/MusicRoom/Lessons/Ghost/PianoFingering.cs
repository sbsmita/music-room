// A reasonable fingering for the ghost hands at the piano (pure numbers, EditMode-tested). The notes come in time order,
// read ahead of the playback; notes struck together (within ChordWindow) are fingered as one chord.
//   - Each hand has five fingers at natural places under its palm, Span apart (a right hand's thumb lowest, a left hand's
//     highest). A group of notes goes to one hand, or is split low part / high part between the left and right hand.
//   - Within a hand every way of putting the notes under fingers in order is tried (at most C(5, n) = 10) and scored: how
//     far the fingers stretch from their places, how far the palm moves (more when it played a moment ago), a finger
//     used again at once on another key, a thumb on a black key. A finger still holding a key cannot take another, and
//     the palm must stay within reach of the keys it holds.
//   - The left hand prefers notes below middle C and the right hand those above (a soft preference: a melody running a
//     little past the middle stays in its hand); the hands never cross.
//   - Too many notes for ten fingers: the extra ones are left to the keys alone (they still go down by themselves).
// Output: strokes on the two hands' GhostTracks (finger, key point, the palm's planned x and forward shift).
using System;
using UnityEngine;

namespace MusicRoom.Lessons.Ghost
{
    public sealed class PianoFingering
    {
        public const int Left = 0, Right = 1;
        /// <summary>The fingers' natural spacing under the palm (m; a white key is 0.0235).</summary>
        public const float Span = 0.021f;
        /// <summary>How far a fingertip may reach from its natural place (m).</summary>
        public const float MaxStretch = 2f * Span;
        /// <summary>Notes this close together (s) are one chord.</summary>
        public const double ChordWindow = 0.035;
        /// <summary>The palm moves to its next place over at most PalmMove (s), arriving PalmLead before the notes; a finger
        /// travels to its key over at most FingerTravel and is on it FingerLead before the key sounds, and lifts over FingerLift.</summary>
        public const double PalmMove = 0.32, PalmLead = 0.03, FingerTravel = 0.26, FingerLead = 0.025, FingerLift = 0.12;
        /// <summary>A pressed key's dip under the fingertip (m: the key's travel at a fingertip's place).</summary>
        public const float KeyDip = 0.008f;
        /// <summary>The palm moves forward by this over black keys (m).</summary>
        public const float BlackForward = 0.03f;
        /// <summary>The hands' palms stay at least this far apart (m).</summary>
        public const float MinGap = 0.075f;
        public const int MaxGroup = 16;

        public readonly GhostTrack[] Hands = { new GhostTrack(), new GhostTrack() };

        /// <summary>The hands' soft boundary (half a white key below middle C: middle C is the right hand's) and each
        /// hand's first home (piano-local x).</summary>
        public float SplitX, HomeLeft, HomeRight;

        struct Pending { public double On, Off; public int Midi; public Vector3 Point; public bool Black; public float Vel; }
        readonly Pending[] _group = new Pending[MaxGroup];
        int _groupN;
        readonly float[] _palm = new float[2];
        readonly double[] _lastOn = { double.NegativeInfinity, double.NegativeInfinity };
        readonly int[] _lastFinger = { -1, -1 };
        readonly int[] _lastFingerKey = new int[10];
        readonly double[] _lastFingerOn = new double[10];
        readonly bool[] _used = new bool[2];
        // scratch (no allocation per note)
        readonly int[] _combo = new int[5], _bestL = new int[5], _bestR = new int[5], _keepL = new int[5], _keepR = new int[5], _pick = new int[5];
        readonly int[] _order = new int[MaxGroup];

        /// <summary>Notes that got no finger (too many at once), for the tests.</summary>
        public int Unfingered { get; private set; }

        /// <param name="splitX">Piano-local x of middle C.</param>
        public PianoFingering(float splitX, float homeLeft, float homeRight)
        {
            SplitX = splitX - 0.5f * 0.0235f;
            HomeLeft = homeLeft;
            HomeRight = homeRight;
            Clear();
        }

        public void Clear()
        {
            Hands[0].Clear();
            Hands[1].Clear();
            _groupN = 0;
            _palm[0] = HomeLeft;
            _palm[1] = HomeRight;
            _lastOn[0] = _lastOn[1] = double.NegativeInfinity;
            _lastFinger[0] = _lastFinger[1] = -1;
            _used[0] = _used[1] = false;
            for (int i = 0; i < 10; i++) { _lastFingerKey[i] = -1; _lastFingerOn[i] = double.NegativeInfinity; }
            Unfingered = 0;
        }

        /// <summary>Finger f's natural x offset from the palm (hand h): the right hand's thumb lowest, the left's highest.</summary>
        public static float Natural(int hand, int f) => (hand == Right ? 1f : -1f) * (f - 2) * Span;

        /// <summary>A key goes down at <paramref name="on"/> (point: its strike point, piano-local).</summary>
        public void Note(double on, int midi, Vector3 point, bool black, float vel)
        {
            if (_groupN > 0 && on - _group[0].On > ChordWindow) Close();
            if (_groupN == MaxGroup) { Unfingered++; return; }
            _group[_groupN++] = new Pending { On = on, Off = double.PositiveInfinity, Midi = midi, Point = point, Black = black, Vel = vel };
        }

        /// <summary>The key comes up at <paramref name="off"/>.</summary>
        public void Release(double off, int midi)
        {
            for (int i = _groupN - 1; i >= 0; i--)
                if (_group[i].Midi == midi && double.IsPositiveInfinity(_group[i].Off)) { _group[i].Off = Math.Max(off, _group[i].On); return; }
            if (!Hands[Right].Release(midi, off)) Hands[Left].Release(midi, off);
        }

        /// <summary>Everything read ahead up to <paramref name="horizon"/> is known: a chord that started before it closes.</summary>
        public void Flush(double horizon)
        {
            if (_groupN > 0 && horizon - _group[0].On > ChordWindow) Close();
        }

        /// <summary>Playback stopped at t: keys up, nothing more planned.</summary>
        public void Stop(double t)
        {
            _groupN = 0;
            for (int h = 0; h < 2; h++) { Hands[h].DropAfter(t); Hands[h].ReleaseAll(t); }
        }

        bool Busy(int hand, int f, double t)
        {
            GhostTrack tr = Hands[hand];
            for (int i = 0; i < tr.Count; i++)
            {
                ref GhostStroke s = ref tr[i];
                if (s.Finger == f && s.On <= t && s.Off > t - 0.01) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- one group

        void Close()
        {
            int n = _groupN;
            _groupN = 0;
            if (n == 0) return;
            for (int i = 0; i < n; i++) _order[i] = i;
            // by pitch (x), low first
            for (int i = 1; i < n; i++)
            {
                int k = _order[i], j = i - 1;
                while (j >= 0 && _group[_order[j]].Point.x > _group[k].Point.x) { _order[j + 1] = _order[j]; j--; }
                _order[j + 1] = k;
            }
            double on = _group[_order[0]].On;
            for (int i = 1; i < n; i++) on = Math.Min(on, _group[_order[i]].On);

            // Options: everything in the left hand, everything in the right, or split after the s-th lowest note.
            float bestCost = float.PositiveInfinity;
            int bestSplit = -1;   // notes [0, split) left, [split, n) right
            float bestPalmL = 0f, bestPalmR = 0f;
            for (int split = 0; split <= n; split++)
            {
                int nl = split, nr = n - split;
                if (nl > 5 || nr > 5) continue;
                float cost = 0f, pl = _palm[Left], pr = _palm[Right];
                if (nl > 0) { cost += HandCost(Left, 0, nl, on, out pl, _combo); if (float.IsInfinity(cost)) continue; Array.Copy(_combo, _bestL, 5); }
                if (nr > 0) { cost += HandCost(Right, split, nr, on, out pr, _combo); if (float.IsInfinity(cost)) continue; Array.Copy(_combo, _bestR, 5); }
                // the hands never cross (a hand that is not playing now steps aside: its palm is moved, see Apply)
                if (pl > pr - MinGap)
                {
                    bool leftIdle = nl == 0 && !Holding(Left, on), rightIdle = nr == 0 && !Holding(Right, on);
                    if (!leftIdle && !rightIdle) continue;
                    cost += 0.5f;
                }
                if (cost < bestCost) { bestCost = cost; bestSplit = split; bestPalmL = pl; bestPalmR = pr; Array.Copy(_bestL, _keepL, 5); Array.Copy(_bestR, _keepR, 5); }
            }
            if (bestSplit < 0)
            {
                // Nothing fits (a cluster wider than two hands, or every finger holding): finger what one free finger can.
                for (int i = 0; i < n; i++) Single(_group[_order[i]]);
                return;
            }
            if (bestSplit > 0) Apply(Left, 0, bestSplit, _keepL, bestPalmL, on);
            if (bestSplit < n) Apply(Right, bestSplit, n - bestSplit, _keepR, bestPalmR, on);
            // a hand pushed aside by the other (it was idle there): it moves away at this moment
            if (bestSplit == n && _palm[Right] < bestPalmL + MinGap && _used[Right]) MovePalm(Right, bestPalmL + MinGap, on);
            if (bestSplit == 0 && _palm[Left] > bestPalmR - MinGap && _used[Left]) MovePalm(Left, bestPalmR - MinGap, on);
        }

        bool Holding(int hand, double t)
        {
            for (int f = 0; f < 5; f++) if (Busy(hand, f, t)) return true;
            return false;
        }

        /// <summary>The best way to put notes _order[from .. from+m) under hand h's fingers: its cost (+inf when none fits),
        /// the palm x and the fingers (combo[i] for the i-th lowest note).</summary>
        float HandCost(int h, int from, int m, double on, out float palm, int[] combo)
        {
            palm = _palm[h];
            float best = float.PositiveInfinity, bestPalm = palm;
            // reach of the keys still held by this hand: the palm stays within MaxStretch of each
            float lo = float.NegativeInfinity, hi = float.PositiveInfinity;
            GhostTrack tr = Hands[h];
            bool anyHeld = false;
            for (int i = 0; i < tr.Count; i++)
            {
                ref GhostStroke s = ref tr[i];
                if (s.Finger < 0 || !(s.On <= on && s.Off > on - 0.01)) continue;
                float c = s.Point.x - Natural(h, s.Finger);
                lo = Mathf.Max(lo, c - MaxStretch);
                hi = Mathf.Min(hi, c + MaxStretch);
                anyHeld = true;
            }
            if (lo > hi) return best;
            double since = on - _lastOn[h];
            float moveWeight = 0.5f * (1f + 0.12f / Mathf.Max(0.05f, (float)since));
            // fingers in order: right hand thumb (0) lowest, left hand little (4) lowest
            int total = 1 << 5;
            int[] pick = _pick;
            for (int mask = 0; mask < total; mask++)
            {
                if (BitCount(mask) != m) continue;
                int k = 0;
                if (h == Right) { for (int f = 0; f < 5; f++) if ((mask & (1 << f)) != 0) pick[k++] = f; }
                else { for (int f = 4; f >= 0; f--) if ((mask & (1 << f)) != 0) pick[k++] = f; }
                bool ok = true;
                float mean = 0f;
                for (int i = 0; i < m; i++)
                {
                    if (Busy(h, pick[i], on)) { ok = false; break; }
                    mean += _group[_order[from + i]].Point.x - Natural(h, pick[i]);
                }
                if (!ok) continue;
                float p = Mathf.Clamp(mean / m, lo, hi);
                float cost = 0f;
                for (int i = 0; i < m; i++)
                {
                    ref Pending nt = ref _group[_order[from + i]];
                    float dev = nt.Point.x - p - Natural(h, pick[i]);
                    if (Mathf.Abs(dev) > MaxStretch) { ok = false; break; }
                    cost += dev * dev / (Span * Span);
                    int slot = h * 5 + pick[i];
                    if (_lastFingerKey[slot] >= 0 && _lastFingerKey[slot] != nt.Midi && on - _lastFingerOn[slot] < 0.4) cost += 1.2f;
                    if (pick[i] == 0 && nt.Black) cost += 0.8f;
                    // the side: left below middle C, right above (soft)
                    float wrong = h == Right ? SplitX - nt.Point.x : nt.Point.x - SplitX;
                    if (wrong > 0f) cost += 0.35f * wrong / Span;
                }
                if (!ok) continue;
                cost += moveWeight * Mathf.Abs(p - _palm[h]) / Span * (_used[h] ? 1f : 0.3f);
                if (anyHeld) cost += 0.2f;
                if (cost < best)
                {
                    best = cost;
                    bestPalm = p;
                    for (int i = 0; i < m; i++) combo[i] = pick[i];
                }
            }
            palm = bestPalm;
            return best;
        }

        static int BitCount(int v) { int c = 0; while (v != 0) { c += v & 1; v >>= 1; } return c; }

        void Apply(int h, int from, int m, int[] combo, float palm, double on)
        {
            bool black = false;
            for (int i = 0; i < m; i++) black |= _group[_order[from + i]].Black;
            float z = black ? BlackForward : 0f;
            for (int i = 0; i < m; i++)
            {
                ref Pending nt = ref _group[_order[from + i]];
                int f = combo[i];
                Hands[h].Add(new GhostStroke
                {
                    On = nt.On, Off = nt.Off, Point = nt.Point, Up = Vector3.up, Forward = Vector3.forward, Vel = nt.Vel,
                    Finger = (sbyte)f, Style = StrokeStyle.Key, Key = nt.Midi, PalmX = palm, PalmZ = z,
                });
                _lastFingerKey[h * 5 + f] = nt.Midi;
                _lastFingerOn[h * 5 + f] = nt.On;
            }
            _palm[h] = palm;
            _lastOn[h] = on;
            _used[h] = true;
        }

        void MovePalm(int h, float palm, double on)
        {
            Hands[h].Add(new GhostStroke { On = on, Off = on, Up = Vector3.up, Forward = Vector3.forward, Finger = -1, Style = StrokeStyle.Key, Key = -1, PalmX = palm });
            _palm[h] = palm;
        }

        /// <summary>The fallback for one note: the free finger of either hand nearest to it (no stretch rule).</summary>
        void Single(in Pending nt)
        {
            int bh = -1, bf = -1;
            float bd = float.PositiveInfinity;
            for (int h = 0; h < 2; h++)
                for (int f = 0; f < 5; f++)
                {
                    if (Busy(h, f, nt.On)) continue;
                    float d = Mathf.Abs(nt.Point.x - _palm[h] - Natural(h, f));
                    if (d < bd) { bd = d; bh = h; bf = f; }
                }
            if (bh < 0) { Unfingered++; return; }
            float palm = nt.Point.x - Natural(bh, bf);
            Hands[bh].Add(new GhostStroke
            {
                On = nt.On, Off = nt.Off, Point = nt.Point, Up = Vector3.up, Forward = Vector3.forward, Vel = nt.Vel,
                Finger = (sbyte)bf, Style = StrokeStyle.Key, Key = nt.Midi, PalmX = palm, PalmZ = nt.Black ? BlackForward : 0f,
            });
            _palm[bh] = palm;
            _lastOn[bh] = nt.On;
            _used[bh] = true;
        }
    }
}
