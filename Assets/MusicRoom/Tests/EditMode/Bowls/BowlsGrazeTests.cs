// The headset report after the rub smoothing: "still broken cassette sound in bowl", worst "when played outside the bowl
// or outer area", and "hard to play as the stick is not rotating smoothly". These tests reproduce it at the hand tracker's
// HIGH (fast motion) rate: 60 Hz samples, 72 Hz display frames, 3 mm of fingertip jitter, and the puja held in a jittery
// hand (the wrist and the pinch joints jittering by 3 mm and the wrist's roll by 1.5 degrees per sample), posed through
// BowlsPuja every display frame as the app's StickPoses phase does. Paths on the big bowl, 0.6 turns/s:
//  - a steady rub on the outer wall, 5 mm in (the reference texture);
//  - grazing: the depth swings from a few mm into the wall to 7, 12 or 22 mm outside it, so the finger or the stick
//    touches and leaves the rim a few times a second (the "outer area"; 22 mm is past the sticky band's 15 mm);
//  - the stick's own path: the drawn stick against the same hand without jitter.
// Clicks are measured on the output's second difference (a sample-to-sample jump dominates it): the worst 1 ms window's
// energy over the median window's, and the count of 1 ms windows above 12 x the median, in 2..6 s. "Old" is the build
// before this fix (no pad on the unit, the rub-smoothing contact rules, the raw stick); "new" is the app's.
using System;
using System.Collections.Generic;
using MusicRoom.Bowls;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using static MusicRoom.Tests.Bowls.BowlsSim;

namespace MusicRoom.Tests.Bowls
{
    public class BowlsGrazeTests
    {
        const int Bowl = 0;
        const double From = 2.0, To = 6.0;
        /// <summary>A grazing rub's click score may be this many times the steady rub's.</summary>
        const double Margin = 3.0;
        /// <summary>The click metric's floor: a 1 ms window's second-difference RMS per sample against the signal's RMS (the old
        /// rub hiss sat at about 0.004 to 0.006; the tonal rub noise took it away, BowlsRubNoiseTests).</summary>
        const double ClickFloor = 0.004;

        public struct Score
        {
            public double Max, Rms, StepN, StepHs, StrikeV; public int Clicks, Strikes, Releases, Contacts;
            public override string ToString() => $"max {Max:F1} clicks {Clicks} rms {Rms:F4} strikes {Strikes} (fastest {StrikeV:F2} m/s) releases {Releases} N step {StepN:F2} hs step {StepHs:F3}";
        }

        /// <summary>Which fixes run: Old = the build before (no pad and no tonal rub noise, the rub-smoothing contact rules, the raw stick).</summary>
        public struct Fix
        {
            public bool Pad, Contact, Stick;
            public static Fix Old => new Fix();
            public static Fix New => new Fix { Pad = true, Contact = true, Stick = true };
            public override string ToString() => Pad && Contact && Stick ? "new" : !Pad && !Contact && !Stick ? "old" : $"pad {Pad} contact {Contact} stick {Stick}";
        }

        /// <summary>The worst 1 ms second-difference window over the median one, the windows above 12 x the median, the RMS.</summary>
        public static Score ClickScore(float[] y, double from = From, double to = To)
        {
            int a = (int)(from * SR), b = Math.Min(y.Length, (int)(to * SR)), w = SR / 1000;
            var e = new List<double>((b - a) / w + 1);
            double sum = 0;
            for (int s = a; s + w <= b; s += w)
            {
                double acc = 0;
                for (int n = s; n < s + w; n++) { double d2 = y[n] - 2.0 * y[n - 1] + y[n - 2]; acc += d2 * d2; sum += (double)y[n] * y[n]; }
                e.Add(acc);
            }
            var sorted = new List<double>(e); sorted.Sort();
            // the reference window: the median, floored at ClickFloor x the signal's RMS (a pure tone's own second difference is
            // 60 dB under it; without the old rub hiss a burst far under the sing would otherwise count as a click)
            double rms = Math.Sqrt(sum / Math.Max(1, b - a)), floor = w * (ClickFloor * rms) * (ClickFloor * rms);
            double median = Math.Max(Math.Max(1e-30, floor), sorted[sorted.Count / 2]);
            int clicks = 0; foreach (double v in e) if (v > 12 * median) clicks++;
            return new Score { Max = sorted[sorted.Count - 1] / median, Clicks = clicks, Rms = rms };
        }

        /// <summary>The calls in [From, To): strikes, releases, contacts, and the largest frame-to-frame step of the force and
        /// of the height sent to the unit (a contact's first frame counts its whole force).</summary>
        static void Count(SimResult r, ref Score s)
        {
            SimCall prev = null;
            foreach (var c in r.Calls)
            {
                if (c.C == "release") prev = null;
                if (c.T < From || c.T >= To) { if (c.C == "contact") prev = c; continue; }
                if (c.C == "strike") { s.Strikes++; s.StrikeV = Math.Max(s.StrikeV, c.V); } else if (c.C == "release") s.Releases++;
                else if (c.C == "contact")
                {
                    s.Contacts++;
                    if (prev != null) { s.StepN = Math.Max(s.StepN, Math.Abs(c.N - prev.N)); s.StepHs = Math.Max(s.StepHs, Math.Abs(c.Hs - prev.Hs)); }
                    else s.StepN = Math.Max(s.StepN, c.N);
                    prev = c;
                }
            }
        }

        static SimResult Sim(Func<double, SimHand[]> hands, Fix fx, uint seed, double jitter, Action<double, BowlsHandInput[]> before = null, Action<double, BowlsHands, SimResult> onFrame = null)
            => Simulate(new Opts
            {
                Layout = Placement.App, Seconds = 6.6, HandsAt = hands, Hz = 72, TrackerHz = 60, Jitter = jitter, Seed = seed, KeepAudio = true,
                Pad = fx.Pad, SmoothContact = fx.Contact, TonalNoise = fx.Pad, BeforeFrame = before, OnFrame = onFrame,
            });

        /// <summary>Grazing the outer edge: the depth (m into the wall) swings between inMm and outMm (mm) at hz from 1 s.</summary>
        static Func<double, double> Graze(double inMm, double outMm, double hz) => t =>
        {
            if (t < 1.0) return inMm * 1e-3;
            double s = 0.5 + 0.5 * Math.Cos(2 * Math.PI * hz * (t - 1.0));
            return (outMm + (inMm - outMm) * s) * 1e-3;
        };

        static readonly Func<double, double> Steady = _ => 0.005;

        // ------------------------------------------------------------------------------------------ a finger

        /// <summary>A finger circling the big bowl at 0.6 turns/s, 8 mm under the lip, its depth into the wall over time.</summary>
        static Func<double, double3?> FingerCircle(Func<double, double> depth, double rps = 0.6)
        {
            var g = Placed(Placement.App)[Bowl]; double A0 = Approach(Placement.App, Bowl);
            return t =>
            {
                if (t > 6.4) return null;
                double tt = Math.Max(0, t - 0.4), a = A0 + 2 * Math.PI * rps * tt;
                double rr = g.R + TIP_R - depth(t);
                if (t < 0.4) rr += Math.Min(0.03, (0.4 - t) * 0.1);
                return new double3(g.C.x + rr * Math.Cos(a), g.C.y + g.H - 0.008, g.C.z + rr * Math.Sin(a));
            };
        }

        public static Score RunFinger(Func<double, double> depth, Fix fx, uint seed = 7, double jitter = 0.003)
        {
            var r = Sim(Hand(FingerCircle(depth)), fx, seed, jitter);
            Score s = ClickScore(r.Audio.ToArray());
            Count(r, ref s);
            r.Rig?.Dispose();
            return s;
        }

        // ------------------------------------------------------------------------------------------ the stick in a jittery hand

        /// <summary>The shaft's rub point circling the big bowl's outer wall at 0.6 turns/s, 15 mm under the lip.</summary>
        static Func<double, double3> StickCircle(Func<double, double> depth, double rps = 0.6)
        {
            var g = Placed(Placement.App)[Bowl]; double A0 = Approach(Placement.App, Bowl);
            return t =>
            {
                double tt = Math.Max(0, t - 0.4), a = A0 + 2 * Math.PI * rps * tt;
                double rr = g.R + BowlsHands.IX.PUJA_R - depth(t);
                if (t < 0.4) rr += Math.Min(0.03, (0.4 - t) * 0.1);
                return new double3(g.C.x + rr * Math.Cos(a), g.C.y + g.H - 0.015, g.C.z + rr * Math.Sin(a));
            };
        }

        /// <summary>A hand holding the puja (as on the headset): BowlsPuja poses it every display frame from the wrist and the
        /// pinch joints, which carry per-tracker-sample jitter; the shaft 3.5 cm from its plain end meets the path, the stick
        /// leaning out 20 degrees, suede end up. After the hand logic the drawn stick goes onto the rim (BowlsPuja.SetRim, as
        /// BowlsInstrument does) and is recorded.</summary>
        public sealed class PujaHand
        {
            public readonly BowlsPuja Puja = new BowlsPuja();
            /// <summary>Per display frame: the drawn stick's plain end and its axis; while the shaft rubs, the drawn shaft point and
            /// the surface point it is held to.</summary>
            public readonly List<Vector3> Drawn = new List<Vector3>(), Axis = new List<Vector3>(), DrawnShaft = new List<Vector3>(), Surface = new List<Vector3>();
            public double RimStepMax;
            readonly Func<double, double3> _path;
            readonly double _jit, _roll;
            readonly uint _seed;
            readonly bool _rim;
            double _lastT = -1;
            static readonly Matrix4x4 Rig = Matrix4x4.Translate(BowlsLayout.HomeSeat);

            public PujaHand(Func<double, double3> path, double jitter, double rollDeg, uint seed, bool smooth)
            {
                _path = path; _jit = jitter; _roll = rollDeg; _seed = seed; _rim = smooth;
                Puja.Smooth = smooth;
                Puja.SetRig(Rig);
                Puja.TryPickUp(0, false, Puja.GripWorld);
            }

            // the jitter of tracker sample k (the same for every display frame that shows it)
            double Rnd(long k, int j)
            {
                unchecked
                {
                    uint x = _seed * 2654435761u ^ (uint)k * 40503u ^ (uint)j * 2246822519u;
                    x ^= x >> 15; x *= 2246822519u; x ^= x >> 13; x *= 3266489917u; x ^= x >> 16;
                    return x / 4294967296.0 - 0.5;
                }
            }
            Vector3 J(long k, int j) => new Vector3((float)(2 * _jit * Rnd(k, j)), (float)(2 * _jit * Rnd(k, j + 1)), (float)(2 * _jit * Rnd(k, j + 2)));

            void Joints(double t, out Vector3 wrist, out Quaternion wr, out Vector3 thumb, out Vector3 index)
            {
                var g = Placed(Placement.App)[Bowl];
                double3 p = _path(t);
                Vector3 P = new Vector3((float)p.x, (float)p.y, (float)p.z);
                Vector3 o = new Vector3((float)(p.x - g.C.x), 0, (float)(p.z - g.C.z)).normalized;
                float lean = 20f * Mathf.Deg2Rad;
                Vector3 axis = (Vector3.up * Mathf.Cos(lean) + o * Mathf.Sin(lean)).normalized;   // plain -> suede
                Vector3 u0 = (o - axis * Vector3.Dot(o, axis)).normalized;
                float c = Mathf.Cos(BowlsPuja.TiltRad), s = Mathf.Sin(BowlsPuja.TiltRad);
                Vector3 fwd = axis * c - u0 * s, up = axis * s + u0 * c;                           // HandToolMath.Grip gives axis back
                Vector3 grip = P + axis * (BowlsLayout.PujaLength - BowlsLayout.PujaGrip - 0.035f);
                Vector3 side = Vector3.Cross(fwd, up).normalized;
                long k = (long)Math.Floor(t * 60);
                wr = Quaternion.LookRotation(fwd, up);
                if (_roll > 0) wr = Quaternion.AngleAxis((float)(2 * _roll * Rnd(k, 10)), fwd) * Quaternion.AngleAxis((float)(2 * _roll * Rnd(k, 11)), side) * wr;
                wrist = grip - fwd * 0.09f + J(k, 0);
                thumb = grip + side * 0.0085f + J(k, 3);
                index = grip - side * 0.0085f + J(k, 6);
            }

            /// <summary>Opts.HandsAt: a tracked hand that holds the tool (its ends are set per frame).</summary>
            public SimHand[] At(double t) => new[] { new SimHand { HasTool = true } };

            /// <summary>Opts.BeforeFrame: the stick follows the hand this display frame (StickPoses) and the hand logic reads its ends.</summary>
            public void Before(double t, BowlsHandInput[] frames)
            {
                float dt = _lastT < 0 ? 1 / 72f : (float)(t - _lastT); _lastT = t;
                Joints(t, out Vector3 wrist, out Quaternion wr, out Vector3 thumb, out Vector3 index);
                Puja.UpdateHeld(dt, true, wrist, wr, thumb, index, true, false);
                Puja.Ends(out Vector3 suede, out Vector3 plain);
                frames[0].HasTool = true; frames[0].Tracked = true;
                frames[0].ToolA = new double3(suede.x, suede.y, suede.z); frames[0].ToolB = new double3(plain.x, plain.y, plain.z);
            }

            /// <summary>Opts.OnFrame: the drawn stick onto the rim (BowlsInstrument.StickOnRim), recorded.</summary>
            public void After(double t, BowlsHands ix, SimResult res)
            {
                bool on = ix.ShaftOnRim(0, out double3 sp, out double3 surf);
                Vector3 before = Puja.RimOffset;
                Puja.SetRim(_rim && on, on ? new Vector3((float)(surf.x - sp.x), (float)(surf.y - sp.y), (float)(surf.z - sp.z)) : Vector3.zero, 1 / 72f);
                RimStepMax = Math.Max(RimStepMax, (Puja.RimOffset - before).magnitude);
                Matrix4x4 m = Puja.DrawnPoseWorld;
                Drawn.Add(m.MultiplyPoint3x4(new Vector3(0.004f, 0, 0)));
                Axis.Add(m.MultiplyVector(Vector3.right));
                if (on && t >= From && t < To)
                {
                    DrawnShaft.Add(new Vector3((float)sp.x, (float)sp.y, (float)sp.z) + Puja.RimOffset);
                    Surface.Add(new Vector3((float)surf.x, (float)surf.y, (float)surf.z));
                }
            }
        }

        public static Score RunStick(Func<double, double> depth, Fix fx, double jitter = 0.003, double roll = 1.5, uint seed = 5, PujaHand keep = null)
        {
            var ph = keep ?? new PujaHand(StickCircle(depth), jitter, roll, seed, fx.Stick);
            var r = Sim(ph.At, fx, seed, 0, ph.Before, ph.After);
            Score s = ClickScore(r.Audio.ToArray());
            Count(r, ref s);
            r.Rig?.Dispose();
            return s;
        }

        /// <summary>RMS of a path's second difference from frame `from` (its shake: per display frame^2).</summary>
        public static double Shake(List<Vector3> p, int from)
        {
            double acc = 0; int n = 0;
            for (int k = Math.Max(2, from); k < p.Count; k++) { Vector3 d = p[k] - 2 * p[k - 1] + p[k - 2]; acc += d.sqrMagnitude; n++; }
            return Math.Sqrt(acc / Math.Max(1, n));
        }

        static double MeanGap(List<Vector3> a, List<Vector3> b, int from)
        {
            double acc = 0; int n = 0;
            for (int k = from; k < Math.Min(a.Count, b.Count); k++) { acc += (a[k] - b[k]).magnitude; n++; }
            return acc / Math.Max(1, n);
        }

        // ------------------------------------------------------------------------------------------ the tests

        [Test]
        public void AFingerGrazingTheOuterRimDoesNotCrackle()
        {
            Score oldSteady = RunFinger(Steady, Fix.Old), oldFar = RunFinger(Graze(3, -22, 1.5), Fix.Old);
            Score steady = RunFinger(Steady, Fix.New), graze = RunFinger(Graze(4, -12, 2.0), Fix.New), far = RunFinger(Graze(3, -22, 1.5), Fix.New), hover = RunFinger(Graze(2, -7, 3.0), Fix.New);
            Debug.Log($"[BowlsGraze] finger old: steady {oldSteady} | graze 3/-22 mm {oldFar}");
            Debug.Log($"[BowlsGraze] finger new: steady {steady} | graze 4/-12 mm {graze} | graze 3/-22 mm {far} | hover 2/-7 mm {hover}");
            Assert.That(oldFar.Clicks, Is.GreaterThan(5), "the old build crackles when the finger leaves past the sticky band (reproduced)");
            Assert.That(steady.Rms, Is.GreaterThan(0.6 * oldSteady.Rms), "a steady rub still sings as loud");
            foreach (var (name, s) in new[] { ("graze 4/-12", graze), ("graze 3/-22", far), ("hover 2/-7", hover) })
            {
                Assert.That(s.Clicks, Is.EqualTo(0), name + ": no click windows");
                Assert.That(s.Max, Is.LessThan(Margin * steady.Max), name + ": within the steady rub's texture");
                Assert.That(s.StrikeV, Is.LessThan(BowlsHands.IX.TAP_SPEED), name + ": grazing is not tapping (at most a touch-down tick)");
                Assert.That(s.StepHs, Is.LessThan(0.02), name + ": the height on the wall moves in small steps");
            }
            Assert.That(oldFar.StepHs, Is.GreaterThan(0.04), "the old build stepped the height by the tracker's jitter");
        }

        [Test]
        public void TheStickGrazingTheOuterRimDoesNotCrackle()
        {
            Score oldSteady = RunStick(Steady, Fix.Old), oldFar = RunStick(Graze(3, -22, 1.5), Fix.Old);
            Score steady = RunStick(Steady, Fix.New), graze = RunStick(Graze(4, -12, 2.0), Fix.New), far = RunStick(Graze(3, -22, 1.5), Fix.New), hover = RunStick(Graze(2, -7, 3.0), Fix.New);
            Debug.Log($"[BowlsGraze] stick old: steady {oldSteady} | graze 3/-22 mm {oldFar}");
            Debug.Log($"[BowlsGraze] stick new: steady {steady} | graze 4/-12 mm {graze} | graze 3/-22 mm {far} | hover 2/-7 mm {hover}");
            Assert.That(oldFar.Clicks, Is.GreaterThan(2), "the old build crackles when the stick leaves past the sticky band (reproduced)");
            Assert.That(oldFar.Max, Is.GreaterThan(Margin * oldSteady.Max), "beyond its own steady rub's texture");
            Assert.That(oldFar.StepN, Is.GreaterThan(2.0), "and its force stepped (a release, then a contact at full force)");
            Assert.That(far.StepN, Is.LessThan(1.0), "the new contact swells in and fades out");
            Assert.That(steady.Rms, Is.GreaterThan(0.5 * oldSteady.Rms), "a steady stick rub still sings");
            foreach (var (name, s) in new[] { ("graze 4/-12", graze), ("graze 3/-22", far), ("hover 2/-7", hover) })
            {
                Assert.That(s.Clicks, Is.EqualTo(0), name + ": no click windows");
                Assert.That(s.Max, Is.LessThan(Margin * steady.Max), name + ": within the steady rub's texture");
                Assert.That(s.StrikeV, Is.LessThan(BowlsHands.IX.TAP_SPEED), name + ": grazing is not striking (at most a touch-down tick on a return from 2 cm away)");
                Assert.That(s.StepHs, Is.LessThan(0.02), name + ": the height on the wall moves in small steps");
            }
        }

        [Test]
        public void TheStickInAJitteryHandMovesSmoothlyAndRidesTheRim()
        {
            var cleanOld = new PujaHand(StickCircle(Steady), 0, 0, 5, false); RunStick(Steady, Fix.Old, keep: cleanOld);
            var jitOld = new PujaHand(StickCircle(Steady), 0.003, 1.5, 5, false); RunStick(Steady, Fix.Old, keep: jitOld);
            var cleanNew = new PujaHand(StickCircle(Steady), 0, 0, 5, true); RunStick(Steady, Fix.New, keep: cleanNew);
            var jitNew = new PujaHand(StickCircle(Steady), 0.003, 1.5, 5, true); RunStick(Steady, Fix.New, keep: jitNew);
            int f0 = (int)(From * 72);
            double shOld = Shake(jitOld.Drawn, f0), shNew = Shake(jitNew.Drawn, f0), shClean = Shake(cleanOld.Drawn, f0);
            double axOld = Shake(jitOld.Axis, f0), axNew = Shake(jitNew.Axis, f0);
            double lag = MeanGap(jitNew.Drawn, cleanNew.Drawn, f0), onRim = MeanGap(jitNew.DrawnShaft, jitNew.Surface, 0), offRimOld = MeanGap(jitOld.DrawnShaft, jitOld.Surface, 0);
            Debug.Log($"[BowlsGraze] drawn stick shake (mm / frame^2) at the plain end: clean hand {1e3 * shClean:F3}, jittery hand old {1e3 * shOld:F3} new {1e3 * shNew:F3}; " +
                      $"axis shake (mrad / frame^2) old {1e3 * axOld:F2} new {1e3 * axNew:F2}; new vs the clean hand's stick {1e3 * lag:F1} mm; " +
                      $"drawn shaft to the surface new {1e3 * onRim:F2} mm (old {1e3 * offRimOld:F2} mm), rim ease step max {1e3 * jitNew.RimStepMax:F2} mm / frame");
            Assert.That(shOld, Is.GreaterThan(4 * shClean), "the old stick shook with the hand's jitter (reproduced)");
            Assert.That(shNew, Is.LessThan(0.45 * shOld), "the drawn stick's end moves smoothly");
            Assert.That(axNew, Is.LessThan(0.45 * axOld), "the stick turns smoothly");
            Assert.That(lag, Is.LessThan(0.006), "and still follows the hand (within 6 mm of the jitter-free stick)");
            Assert.That(jitNew.DrawnShaft.Count, Is.GreaterThan(200), "the shaft rubs the rim");
            Assert.That(onRim, Is.LessThan(0.003), "while it rubs, the drawn shaft sits on the bowl's surface");
            Assert.That(jitNew.RimStepMax, Is.LessThan(0.002), "it eases onto the rim (no snap)");
        }

        [Test]
        public void TheSmoothStickStillFollowsAFastStrike()
        {
            // the hand still, then a 12 cm downward swing in 125 ms (peak 1.5 m/s), then still: the filtered stick against the raw one
            var p = new BowlsPuja(); p.SetRig(Matrix4x4.Translate(BowlsLayout.HomeSeat)); p.TryPickUp(0, false, p.GripWorld);
            var raw = new BowlsPuja { Smooth = false }; raw.SetRig(Matrix4x4.Translate(BowlsLayout.HomeSeat)); raw.TryPickUp(0, false, raw.GripWorld);
            double maxLag = 0, vF = 0, vR = 0, endGap = 0; Vector3 lastF = default, lastR = default; const float dt = 1 / 72f;
            for (int k = 0; k < 120; k++)
            {
                double t = k * dt, u = Math.Max(0, Math.Min(1, (t - 0.5) / 0.125));
                float y = (float)(0.06 * (1 - Math.Cos(Math.PI * u)));
                Vector3 grip = new Vector3(-0.3f, 0.9f - y, 0.2f);
                Vector3 wrist = grip + new Vector3(0, 0.02f, -0.09f); Quaternion wr = Quaternion.LookRotation(grip - wrist, Vector3.up);
                Vector3 th = grip + new Vector3(0.0085f, 0, 0), ix = grip - new Vector3(0.0085f, 0, 0);
                p.UpdateHeld(dt, true, wrist, wr, th, ix, true, false); raw.UpdateHeld(dt, true, wrist, wr, th, ix, true, false);
                p.Ends(out Vector3 sf, out _); raw.Ends(out Vector3 sr, out _);
                if (k > 0) { vF = Math.Max(vF, (sf - lastF).magnitude / dt); vR = Math.Max(vR, (sr - lastR).magnitude / dt); }
                lastF = sf; lastR = sr; maxLag = Math.Max(maxLag, (sf - sr).magnitude); endGap = (sf - sr).magnitude;
            }
            Debug.Log($"[BowlsGraze] strike through the smooth stick: lag max {1e3 * maxLag:F1} mm, suede peak speed {vF:F2} m/s (raw {vR:F2}), 1 s later {1e3 * endGap:F2} mm");
            Assert.That(maxLag, Is.LessThan(0.012), "the stick keeps up with a 1.5 m/s strike (under one frame of travel)");
            Assert.That(vF, Is.InRange(0.9 * vR, 1.15 * vR), "the strike speed is kept");
            Assert.That(endGap, Is.LessThan(0.0005), "and it settles where the hand is");
        }
    }
}
