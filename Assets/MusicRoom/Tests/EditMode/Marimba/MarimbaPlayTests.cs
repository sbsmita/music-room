// MarimbaPlay (the bare-hands interaction, spec 5.3-5.6) against the lab's scripted hands: every case IX1..IX18 of
// interaction.reference.mjs, frame for frame (Reference~/ix-cases.json, exported with the frames by
// the marimba lab's tools/export-ix.mjs, which re-runs each case through the reference), must produce the same events in
// the same order: strikes (bar, vel, u, c to the reference's 4 decimals), mutes, unmutes, rolls and Sunny's signals.
using System;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Audio.Burst;
using MusicRoom.Marimba;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using MusicRoom.Tests.Audio;
using static MusicRoom.Tests.Audio.MarimbaRef;

namespace MusicRoom.Tests.Marimba
{
    /// <summary>Records MarimbaPlay's decisions as the reference's event objects.</summary>
    public sealed class MarimbaEventLog : IMarimbaPlayListener
    {
        public struct E { public double T; public string Type, Why, Signal, Kind; public int Bar; public double Vel, U, C; public bool Dead, Deferred, Finger; public int Owner; }
        public readonly List<E> Events = new List<E>();
        public static string SignalName(MarimbaSignal s) => MarimbaSignals.Name(s);
        public void OnStrike(int bar, double vel, double u, double c, MarimbaPointer by, bool deferred, double now) =>
            Events.Add(new E { T = now, Type = "strike", Bar = bar, Vel = vel, U = u, C = c, Deferred = deferred, Finger = by.Finger, Owner = by.Owner });
        public void OnDamp(int bar, MarimbaDampKind kind, double u, bool dead, int owner, double now) =>
            Events.Add(new E { T = now, Type = "damp", Bar = bar, Kind = kind == MarimbaDampKind.Hand ? "hand" : "mallet", U = u, Dead = dead, Owner = owner });
        public void OnUndamp(int bar, string why, double now) => Events.Add(new E { T = now, Type = "undamp", Bar = bar, Why = why });
        public void OnRoll(int bar, double now) => Events.Add(new E { T = now, Type = "roll", Bar = bar });
        public void OnSignal(MarimbaSignal s, double now) => Events.Add(new E { T = now, Type = "signal", Signal = SignalName(s) });
        public int Count(string type) { int n = 0; foreach (var e in Events) if (e.Type == type) n++; return n; }
    }

    public class MarimbaPlayTests
    {
        public static string Dir => Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Marimba/Reference~");
        static object s_cases;
        static Dictionary<string, object> Cases => Obj(s_cases ??= MarimbaJson.Parse(File.ReadAllText(Path.Combine(Dir, "ix-cases.json"))));

        public static int TipOf(string id)
        {
            string s = id.ToLowerInvariant();
            if (s.StartsWith("thumb")) return 0;
            if (s.StartsWith("index") || s.StartsWith("idx")) return 1;
            if (s.StartsWith("middle") || s.StartsWith("mid")) return 2;
            if (s.StartsWith("ring")) return 3;
            if (s.StartsWith("little") || s.StartsWith("pinky")) return 4;
            return 1;
        }

        static IEnumerable<string> CaseNames()
        {
            foreach (var c in Arr(MarimbaJson.Parse(File.ReadAllText(Path.Combine(Dir, "ix-cases.json"))), "cases")) yield return ((string)Obj(c)["name"]).Split(' ')[0];
        }

        [Test]
        public void Constants_AreTheReferenceRevision2()
        {
            var k = Obj(Cases, "constants");
            void C(string key, double v) => Assert.That(v, Is.EqualTo(Num(k, key)), key);
            C("HEAD_R", MarimbaIx.HeadR); C("FINGER_R", MarimbaIx.FingerR); C("SIDE_REACH", MarimbaIx.SideReach); C("END_REACH", MarimbaIx.EndReach);
            C("ARM_ABOVE", MarimbaIx.ArmAbove); C("ARM_ABOVE_FINGER", MarimbaIx.ArmAboveFinger); C("ARM_SAMPLES", MarimbaIx.ArmSamples);
            C("MAX_PREV_MALLET", MarimbaIx.MaxPrevMallet); C("MAX_PREV_FINGER", MarimbaIx.MaxPrevFinger);
            C("V0_MALLET", MarimbaIx.V0Mallet); C("V1_MALLET", MarimbaIx.V1Mallet); C("V0_FINGER", MarimbaIx.V0Finger); C("V1_FINGER", MarimbaIx.V1Finger);
            C("VEL_EXP", MarimbaIx.VelExp); C("VEL_MIN", MarimbaIx.VelMin); C("APPROACH_MS", MarimbaIx.ApproachMs); C("JUMP_M", MarimbaIx.JumpM); C("JUMP_V", MarimbaIx.JumpV);
            C("SAME_BAR_MS", MarimbaIx.SameBarMs); C("OWNER_MS", MarimbaIx.OwnerMs); C("BAND_BELOW", MarimbaIx.BandBelow); C("BAND_ABOVE", MarimbaIx.BandAbove);
            C("DEAD_WINDOW_MS", MarimbaIx.DeadWindowMs); C("REST_MS", MarimbaIx.RestMs); C("STILL_WIN_MS", MarimbaIx.StillWinMs); C("DEAD_STILL", MarimbaIx.DeadStill);
            C("STILL_SAMPLES", MarimbaIx.StillSamples); C("BREAK_SAMPLES", MarimbaIx.BreakSamples); C("DEAD_LIFT", MarimbaIx.DeadLift); C("LIFT_SAMPLES", MarimbaIx.LiftSamples);
            C("HAND_BAND", MarimbaIx.HandBand); C("HAND_STILL", MarimbaIx.HandStill); C("HAND_MS", MarimbaIx.HandMs); C("HAND_MIN_TIPS", MarimbaIx.HandMinTips);
            C("HAND_LIFT", MarimbaIx.HandLift); C("HAND_LIFT_MS", MarimbaIx.HandLiftMs); C("PLAY_CLEAR", MarimbaIx.PlayClear); C("DEFER_ABOVE", MarimbaIx.DeferAbove);
            C("DEFER_MS", MarimbaIx.DeferMs); C("TIE", MarimbaIx.Tie); C("ROLL_MIN", MarimbaIx.RollMin); C("ROLL_GAP_MS", MarimbaIx.RollGapMs); C("LOST_UNDAMP_MS", MarimbaIx.LostUndampMs);
            C("FINGER_ONLY_N", MarimbaIx.FingerOnlyN); C("FINGER_ONLY_MS", MarimbaIx.FingerOnlyMs); C("OFFCENTRE_N", MarimbaIx.OffCentreN); C("OFFCENTRE_SHARE", MarimbaIx.OffCentreShare);
            C("CENTRE_LO", MarimbaIx.CentreLo); C("CENTRE_HI", MarimbaIx.CentreHi); C("ACC_RATIO", MarimbaIx.AccRatio); C("ACC_MIN", MarimbaIx.AccMin);
            // the bars the reference plays on are the sound unit's tables (makeBars = design.mjs layout at H 0.70)
            var bars = Arr(Cases, "bars");
            for (int i = 0; i < bars.Count; i++)
            {
                var b = Obj(bars[i]);
                Assert.That(MarimbaTables.X[i], Is.EqualTo(Num(b, "x")), "x"); Assert.That(MarimbaTables.Z[i], Is.EqualTo(Num(b, "z")), "z");
                Assert.That(MarimbaTables.Length[i], Is.EqualTo(Num(b, "L")), "L"); Assert.That(MarimbaTables.Width[i], Is.EqualTo(Num(b, "w")), "w");
                Assert.That(MarimbaTables.YTop[i], Is.EqualTo(Num(b, "yTop")), "yTop");
            }
            // the accompaniment rule (interaction-vectors.json examples)
            Assert.That(MarimbaIx.AccompanyVel(0.28, 0.6), Is.EqualTo(0.21).Within(1e-12));
            Assert.That(MarimbaIx.AccompanyVel(0.28, 0.4), Is.EqualTo(0.14).Within(1e-12));
            Assert.That(MarimbaIx.AccompanyVel(0.28, 0.2), Is.EqualTo(0.07).Within(1e-12));
            Assert.That(MarimbaIx.AccompanyVel(0.3, 0.1), Is.EqualTo(0.05).Within(1e-12));
        }

        /// <summary>Runs one exported case through MarimbaPlay as the reference's simulate() does.</summary>
        public static MarimbaEventLog Run(Dictionary<string, object> c) => Run(c, false);

        /// <summary>repeat: the app runs at twice the tracker rate and every second tick repeats the last sample (Fresh
        /// false), as at the build's LOW hand-tracking rate.</summary>
        public static MarimbaEventLog Run(Dictionary<string, object> c, bool repeat)
        {
            var meta = Arr(c, "pointers");
            var owners = new List<string>();
            var ptrs = new MarimbaPointer[meta.Count];
            for (int i = 0; i < meta.Count; i++)
            {
                var m = Obj(meta[i]);
                string id = (string)m["id"], owner = (string)m["owner"];
                if (!owners.Contains(owner)) owners.Add(owner);
                bool finger = (bool)m["finger"];
                int tip = m["tip"] != null ? (int)Num(m, "tip") : TipOf(id);
                ptrs[i] = new MarimbaPointer(id, owners.IndexOf(owner), finger, tip);
            }
            var log = new MarimbaEventLog();
            var play = new MarimbaPlay(log) { DeadStrokes = (bool)c["deadStrokes"] };
            double fps = Num(c, "fps");
            var frames = Arr(c, "frames");
            var known = new MarimbaPointer[ptrs.Length];
            int nKnown = 0;
            var seen = new bool[ptrs.Length];
            for (int k = 0; k < frames.Count; k++)
            {
                var f = Arr(frames[k]);
                for (int i = 0; i < ptrs.Length; i++)
                {
                    object st = f[i];
                    if (st is double) continue;   // 0: absent from this frame (keeps its state)
                    if (!seen[i]) { seen[i] = true; known[nKnown++] = ptrs[i]; }
                    var p = ptrs[i];
                    p.Advance();
                    if (st == null) p.Sample(false, default);
                    else
                    {
                        var a = Arr(st);
                        var pos = new double3(Num(a[0]), Num(a[1]), Num(a[2]));
                        double3 d = pos - p.Prev;
                        p.Sample(true, pos, p.HasPrev ? Math.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z) * fps : 0);
                    }
                }
                play.Tick(known, nKnown, 1 / fps, k * 1000 / fps);
                if (repeat)
                {
                    for (int i = 0; i < nKnown; i++) known[i].Fresh = false;
                    play.Tick(known, nKnown, 1 / fps, (k + 0.5) * 1000 / fps);
                    for (int i = 0; i < nKnown; i++) known[i].Fresh = true;
                }
            }
            return log;
        }

        [TestCaseSource(nameof(CaseNames))]
        public void IX_TheReferenceEventsFrameForFrame(string id)
        {
            Dictionary<string, object> c = null;
            foreach (var o in Arr(Cases, "cases")) if (((string)Obj(o)["name"]).Split(' ')[0] == id) c = Obj(o);
            var got = Run(c).Events;
            var exp = Arr(c, "events");
            string Desc(MarimbaEventLog.E e) => $"{e.Type} t {e.T:F2} bar {e.Bar} vel {e.Vel:F4} u {e.U:F4} c {e.C:F4} {e.Kind} {e.Why} {e.Signal}";
            Assert.That(got.Count, Is.EqualTo(exp.Count), $"{c["name"]}: events [{string.Join("; ", got.ConvertAll(Desc))}]");
            for (int i = 0; i < exp.Count; i++)
            {
                var x = Obj(exp[i]); var g = got[i];
                string where = $"{id} event {i} ({Desc(g)})";
                Assert.That(g.Type, Is.EqualTo((string)x["type"]), where);
                Assert.That(g.T, Is.EqualTo(Num(x, "t")).Within(1e-6), where + " t");
                if (x.ContainsKey("bar")) Assert.That(g.Bar, Is.EqualTo((int)Num(x, "bar")), where + " bar");
                switch (g.Type)
                {
                    case "strike":
                        Assert.That(g.Vel, Is.EqualTo(Num(x, "vel")).Within(5.0001e-5), where + " vel");
                        Assert.That(g.U, Is.EqualTo(Num(x, "u")).Within(5.0001e-5), where + " u");
                        Assert.That(g.C, Is.EqualTo(Num(x, "c")).Within(5.0001e-5), where + " c");
                        Assert.That(g.Finger, Is.EqualTo((bool)x["finger"]), where + " finger");
                        Assert.That(g.Deferred, Is.EqualTo(x.ContainsKey("deferred")), where + " deferred");
                        break;
                    case "damp":
                        Assert.That(g.Kind, Is.EqualTo((string)x["kind"]), where + " kind");
                        Assert.That(g.U, Is.EqualTo(Num(x, "u")).Within(5.0001e-5), where + " u");
                        if (x.ContainsKey("dead")) Assert.That(g.Dead, Is.EqualTo((bool)x["dead"]), where + " dead");
                        break;
                    case "undamp": Assert.That(g.Why, Is.EqualTo((string)x["why"]), where + " why"); break;
                    case "signal": Assert.That(g.Signal, Is.EqualTo((string)x["name"]), where + " signal"); break;
                }
            }
        }

        /// <summary>A straight mallet stroke at speed v onto bar b at (u, c), as interaction.reference.mjs strokeFrames.</summary>
        static void Stroke(MarimbaPlay play, MarimbaPointer p, int b, double u, ref int frame, double v = 1.5, double c = 0, double fps = 90)
        {
            double plane = MarimbaTables.YTop[b] + MarimbaIx.HeadR, h = 0.08;
            double3 At(double hh) => new double3(MarimbaTables.X[b] + c * MarimbaTables.Width[b] / 2, plane + hh, MarimbaTables.Z[b] - MarimbaTables.Length[b] / 2 + u * MarimbaTables.Length[b]);
            var arr = new[] { p };
            while (h > -0.02) { p.Advance(); p.Sample(true, At(h)); play.Tick(arr, 1, 1 / fps, frame++ * 1000 / fps); h -= v / fps; }
            while (h < 0.08) { h += 0.6 / fps; p.Advance(); p.Sample(true, At(h)); play.Tick(arr, 1, 1 / fps, frame++ * 1000 / fps); }
        }

        [Test]
        public void Signals_OffCentreAndBackRow_OncePerSession()
        {
            // spec 5.6: offCentre = of the last 10 strokes on naturals, 6 or more at u < .3 or u > .7 (review item 15);
            // backRow = the first stroke on a sharp
            var log = new MarimbaEventLog();
            var play = new MarimbaPlay(log) { DeadStrokes = false };
            var m = new MarimbaPointer("malletR", 0, false, -1);
            int frame = 0, c4 = MarimbaGeometry.BarOf("C4");
            double[] us = { 0.5, 0.2, 0.5, 0.8, 0.5, 0.15, 0.5, 0.85, 0.9, 0.5 };   // 5 of 10 off centre: no signal yet
            foreach (double u in us) Stroke(play, m, c4, u, ref frame);
            Assert.That(log.Events.FindAll(e => e.Signal == "marimba.offCentre").Count, Is.EqualTo(0), "5 of 10");
            Stroke(play, m, c4, 0.1, ref frame);                                      // the window now holds 6 of 10
            Assert.That(log.Events.FindAll(e => e.Signal == "marimba.offCentre").Count, Is.EqualTo(1), "6 of the last 10");
            for (int k = 0; k < 10; k++) Stroke(play, m, c4, 0.05, ref frame);
            Assert.That(log.Events.FindAll(e => e.Signal == "marimba.offCentre").Count, Is.EqualTo(1), "once per session");
            Assert.That(log.Events.FindAll(e => e.Signal == "marimba.backRow").Count, Is.EqualTo(0));
            Stroke(play, m, MarimbaGeometry.BarOf("F#4"), 0.04, ref frame);
            Stroke(play, m, MarimbaGeometry.BarOf("G#4"), 0.5, ref frame);
            Assert.That(log.Events.FindAll(e => e.Signal == "marimba.backRow").Count, Is.EqualTo(1), "the first stroke on a sharp, once");
        }

        [Test]
        public void TrackingLoss_ReleasesAMalletMuteAfterHalfASecondAtAnyFrameRate()
        {
            // review item 19: the lost-hand release counts the frame's real elapsed time (0.5 s at 72 Hz too, not 0.625 s)
            foreach (double fps in new[] { 72.0, 90.0, 120.0 })
            {
                var log = new MarimbaEventLog();
                var play = new MarimbaPlay(log) { DeadStrokes = true };
                var m = new MarimbaPointer("malletR", 0, false, -1);
                var arr = new[] { m };
                int b = MarimbaGeometry.BarOf("G4"), k = 0;
                double plane = MarimbaTables.YTop[b] + MarimbaIx.HeadR;
                double3 At(double hh) => new double3(MarimbaTables.X[b], plane + hh, MarimbaTables.Z[b]);
                for (double h = 0.08; h > -0.01; h -= 2.5 / fps) { m.Advance(); m.Sample(true, At(h)); play.Tick(arr, 1, 1 / fps, k++ * 1000 / fps); }
                for (int i = 0; i < (int)(0.4 * fps); i++) { m.Advance(); m.Sample(true, At(-0.01)); play.Tick(arr, 1, 1 / fps, k++ * 1000 / fps); }
                Assert.That(play.IsDamped(b), Is.True, $"{fps} Hz: the dead stroke muted G4");
                double lostAt = (k - 1) * 1000 / fps, undampAt = -1;   // the last tracked sample
                for (int i = 0; i < (int)fps; i++)
                {
                    m.Advance(); m.Sample(false, default); play.Tick(arr, 1, 1 / fps, k++ * 1000 / fps);
                    if (undampAt < 0 && !play.IsDamped(b)) undampAt = (k - 1) * 1000 / fps;
                }
                Assert.That(undampAt - lostAt, Is.InRange(500.0 - 1e-6, 500.0 + 1000 / fps + 1e-6), $"{fps} Hz: released 0.5 s after the last tracked sample");
            }
        }

        [TestCaseSource(nameof(CaseNames))]
        public void IX_RepeatedTrackerSamplesChangeNothing(string id)
        {
            // the phase-B risk: at LOW hand-tracking the app frame may repeat a tracker sample; MarimbaPlay skips a repeat in
            // every rule that counts samples, so each case gives the same events (a time-based decision may land on the
            // repeat half a frame later)
            Dictionary<string, object> c = null;
            foreach (var o in Arr(Cases, "cases")) if (((string)Obj(o)["name"]).Split(' ')[0] == id) c = Obj(o);
            var got = Run(c, true).Events;
            var exp = Arr(c, "events");
            Assert.That(got.Count, Is.EqualTo(exp.Count), $"{c["name"]}: events");
            double half = 500 / Num(c, "fps") + 1e-6;
            for (int i = 0; i < exp.Count; i++)
            {
                var x = Obj(exp[i]); var g = got[i];
                Assert.That(g.Type, Is.EqualTo((string)x["type"]), $"{id} event {i}");
                Assert.That(g.T, Is.InRange(Num(x, "t") - 1e-6, Num(x, "t") + half), $"{id} event {i} time");
                if (x.ContainsKey("bar")) Assert.That(g.Bar, Is.EqualTo((int)Num(x, "bar")), $"{id} event {i} bar");
                if (g.Type == "strike") Assert.That(g.Vel, Is.EqualTo(Num(x, "vel")).Within(5.0001e-5), $"{id} event {i} vel");
            }
        }

        [Test]
        public void SampleGate_RepeatsAreNotFresh_AStillHandStillSamples()
        {
            // MarimbaController.IsFresh: a new SampleT is fresh; the same SampleT is a repeat until the learned interval has
            // passed (a perfectly still hand); no learned interval yet: fresh
            Assert.That(MarimbaController.IsFresh(1.000, 0.990, 1000, 990, 0.033f), Is.True, "a new sample");
            Assert.That(MarimbaController.IsFresh(1.000, 1.000, 1011, 1000, 0.033f), Is.False, "the next app frame repeats it");
            Assert.That(MarimbaController.IsFresh(1.000, 1.000, 1026, 1000, 0.033f), Is.True, "still, but a sample interval later");
            Assert.That(MarimbaController.IsFresh(1.000, 1.000, 1011, 1000, 0f), Is.True, "no interval learned yet");
        }

        [Test]
        public void Tick_AllocatesNothing()
        {
            var ptrs = new[] { new MarimbaPointer("malletL", 0, false, -1), new MarimbaPointer("malletR", 1, false, -1), new MarimbaPointer("indexL", 2, true, 1), new MarimbaPointer("middleL", 2, true, 2) };
            var log = new MarimbaEventLog();
            var play = new MarimbaPlay(log);
            log.Events.Capacity = 1024;
            double3 c4 = new double3(MarimbaTables.X[12], MarimbaTables.YTop[12] + MarimbaIx.HeadR, MarimbaTables.Z[12]);
            void Frame(int k)
            {
                double h = 0.06 * Math.Cos(k * 0.5);
                for (int i = 0; i < ptrs.Length; i++)
                {
                    ptrs[i].Advance();
                    ptrs[i].Sample(true, c4 + new double3(0.05 * i - 0.08, h, 0.01 * i), 0.4);
                }
                play.Tick(ptrs, ptrs.Length, 1 / 90.0, k * 1000 / 90.0);
            }
            for (int k = 0; k < 20; k++) Frame(k);
            int k0 = 20;
            Assert.That(() => { for (int k = 0; k < 120; k++) Frame(k0 + k); }, Is.Not.AllocatingGCMemory());
            Assert.That(log.Count("strike"), Is.GreaterThan(0), "the scripted frames do play");
        }
    }
}
