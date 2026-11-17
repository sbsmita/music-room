// The ControlStream encoder (Core/Recording/ControlEncoder.cs) against the platform's reference vectors
// (Tools/SoundLab/platform/control-decimator.mjs, platform.vectors.json "control"; platform.md 6.3 and 12.1): the reference
// test gesture (seed 1, 90 Hz, 10 s) is transcribed here line for line, encoded with the C# encoder (frameDt 1/90), and must
// give the same 242 events (the first 12 and the last 3 exactly, compared after the vectors' 1e-4 rounding), the same
// events per section, and, decoded with the reference decode() / measure() transcribed below, the same reconstruction error
// (1.7365 cents rms, 8.672 cents max, 0.4103 dB) to 1e-4.
using System;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Core
{
    public class ControlStreamTests
    {
        const double Frame = 1.0 / 90.0;
        const double Preroll = 0.05;

        // ---- the vectors file (JsonUtility reads the part it needs) ----
        [Serializable] class VectorFile { public ControlVectors control; }
        [Serializable] class ControlVectors { public int gestureSeed, frames, events; public ErrorVector error; public EventVector[] first, last; public Sections sections; }
        [Serializable] class ErrorVector { public double maxCents, rmsCents, maxDb; }
        [Serializable] class EventVector { public double t, u, vel, speed; public string type; public int lane; }
        [Serializable] class Sections { public int swell, glide, vibrato, fall; }

        static ControlVectors Load()
        {
            string path = Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Layout/Reference~/platform.vectors.json");
            Assert.That(File.Exists(path), Is.True, path);
            return JsonUtility.FromJson<VectorFile>(File.ReadAllText(path)).control;
        }

        // ---- control-decimator.mjs, transcribed (double maths, the same order) ----
        struct GestureFrame { public double T, P, A; }
        struct Ev { public double T, U, Vel, Speed; public string Type; public int Lane; }

        /// <summary>mulberry32 (JS Math.imul and &gt;&gt;&gt; on 32-bit unsigned words).</summary>
        sealed class Mulberry
        {
            uint _a;
            public Mulberry(uint seed) { _a = seed; }
            public double Next()
            {
                _a += 0x6D2B79F5u;
                uint t = _a;
                t = unchecked((t ^ (t >> 15)) * (t | 1u));
                t ^= unchecked(t + (t ^ (t >> 7)) * (t | 61u));
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        static List<GestureFrame> Gesture(uint seed)
        {
            var rnd = new Mulberry(seed);
            int n = (int)Math.Round(10 / Frame);
            var output = new List<GestureFrame>(n);
            double jit = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i * Frame;
                if (i % 3 == 0) jit = (rnd.Next() * 2 - 1) * 0.015;
                double p = 57, a = 0;
                if (t >= 0.5 && t < 9)
                {
                    a = t < 1.5 ? 0.6 * (t - 0.5) : t < 8 ? 0.6 + 0.2 * Math.Sin(2 * Math.PI * 0.3 * (t - 1.5)) : 0.6 * (9 - t) / 1;
                    p = t < 2 ? 57 : t < 5 ? 57 + 12 * (t - 2) / 3 : t < 8 ? 69 + 0.3 * Math.Sin(2 * Math.PI * 5.5 * (t - 5)) : 69 - 2 * (t - 8);
                    p += jit;
                }
                output.Add(new GestureFrame { T = t, P = p, A = a });
            }
            return output;
        }

        /// <summary>The C# encoder over the gesture (the reference's encode()).</summary>
        static List<Ev> Encode(List<GestureFrame> frames)
        {
            var enc = new ControlEncoder("lane");
            var buffer = new ControlEvent[2];
            var list = new List<Ev>(256);
            foreach (GestureFrame f in frames)
            {
                int n = enc.Frame(f.T, f.P, f.A, Frame, buffer);
                for (int k = 0; k < n; k++)
                {
                    ControlEvent e = buffer[k];
                    TakeEvent te = enc.ToTakeEvent(e);
                    list.Add(new Ev { T = e.T, Type = te.type, Lane = e.Lane, U = e.U, Vel = e.Vel, Speed = e.Speed });
                }
            }
            return list;
        }

        struct Seg { public double At, Dur, P, A; }

        static double Db(double a) => 20 * Math.Log10(Math.Max(a, 1e-6));

        static List<(double p, double a)> Decode(List<Ev> events, List<GestureFrame> frames)
        {
            double curP = 57, curA = 0;
            // Each control dispatched min(speed, PREROLL) early; a stable sort by dispatch time (JS sort is stable).
            var order = new List<(double at, int i)>(events.Count);
            for (int i = 0; i < events.Count; i++)
            {
                Ev e = events[i];
                order.Add((e.Type.EndsWith("Ctl", StringComparison.Ordinal) ? e.T - Math.Min(e.Speed, Preroll) : e.T, i));
            }
            order.Sort((x, y) => x.at != y.at ? x.at.CompareTo(y.at) : x.i.CompareTo(y.i));
            var segs = new List<Seg>(events.Count);
            foreach (var (at, i) in order)
            {
                Ev e = events[i];
                if (e.Type.EndsWith("On", StringComparison.Ordinal)) segs.Add(new Seg { At = at, Dur = 0, P = e.U, A = e.Vel });
                else if (e.Type.EndsWith("Ctl", StringComparison.Ordinal)) segs.Add(new Seg { At = at, Dur = e.Speed, P = e.U, A = e.Vel });
                else segs.Add(new Seg { At = at, Dur = 0, P = curP, A = 0 });
            }
            var output = new List<(double, double)>(frames.Count);
            int k = 0;
            double fromP = 57, fromA = 0;
            bool has = false;
            Seg seg = default;
            foreach (GestureFrame f in frames)
            {
                while (k < segs.Count && segs[k].At <= f.T + 1e-9)
                {
                    if (has) { ValueAt(seg, segs[k].At, fromP, fromA, out double vp, out double va); fromP = vp; fromA = va; }
                    seg = segs[k++];
                    has = true;
                }
                if (!has) { output.Add((curP, curA)); continue; }
                ValueAt(seg, f.T, fromP, fromA, out double p, out double a);
                output.Add((p, a));
            }
            return output;
        }

        static void ValueAt(in Seg seg, double t, double fromP, double fromA, out double p, out double a)
        {
            if (seg.Dur <= 0 || t >= seg.At + seg.Dur) { p = seg.P; a = seg.A; return; }
            double k = Math.Max(0, (t - seg.At) / seg.Dur);
            p = fromP + (seg.P - fromP) * k;
            a = fromA + (seg.A - fromA) * k;
        }

        static void Measure(List<GestureFrame> frames, List<(double p, double a)> rec, out double maxCents, out double rmsCents, out double maxDb)
        {
            maxCents = 0; maxDb = 0;
            double sum = 0;
            int n = 0;
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].A < 0.05) continue;
                double e = Math.Abs(rec[i].p - frames[i].P) * 100;
                maxCents = Math.Max(maxCents, e);
                sum += e * e;
                n++;
                maxDb = Math.Max(maxDb, Math.Abs(Db(rec[i].a) - Db(frames[i].A)));
            }
            rmsCents = Math.Sqrt(sum / Math.Max(1, n));
        }

        static double R4(double v) => Math.Floor(v * 1e4 + 0.5) / 1e4;   // geometry.mjs r4: JS Math.round (halves up)

        static void AssertEvent(Ev got, EventVector want, string what)
        {
            Assert.That(got.Type, Is.EqualTo(want.type), what + " type");
            Assert.That(got.Lane, Is.EqualTo(want.lane), what + " lane");
            Assert.That(R4(got.T), Is.EqualTo(want.t).Within(1e-4), what + " t");
            Assert.That(R4(got.U), Is.EqualTo(want.u).Within(1e-4), what + " u");
            Assert.That(R4(got.Vel), Is.EqualTo(want.vel).Within(1e-4), what + " vel");
            Assert.That(R4(got.Speed), Is.EqualTo(want.speed).Within(1e-4), what + " speed");
        }

        [Test]
        public void TheReferenceGestureEncodesToTheVectorEvents()
        {
            ControlVectors v = Load();
            Assert.That(v.gestureSeed, Is.EqualTo(1));
            List<GestureFrame> g = Gesture((uint)v.gestureSeed);
            Assert.That(g.Count, Is.EqualTo(v.frames), "900 frames at 90 Hz");
            List<Ev> ev = Encode(g);
            Assert.That(ev.Count, Is.EqualTo(v.events), "242 events");
            Assert.That(v.events, Is.EqualTo(242));
            for (int i = 0; i < v.first.Length; i++) AssertEvent(ev[i], v.first[i], "event " + i);
            for (int i = 0; i < v.last.Length; i++) AssertEvent(ev[ev.Count - v.last.Length + i], v.last[i], "last event " + i);
            int swell = 0, glide = 0, vibrato = 0, fall = 0;
            foreach (Ev e in ev)
            {
                if (e.T < 2) swell++;
                else if (e.T < 5) glide++;
                else if (e.T < 8) vibrato++;
                else fall++;
            }
            Assert.That(new[] { swell, glide, vibrato, fall }, Is.EqualTo(new[] { v.sections.swell, v.sections.glide, v.sections.vibrato, v.sections.fall }), "swell 30, glide 90, vibrato 89, fall 33");
            Assert.That(new[] { swell, glide, vibrato, fall }, Is.EqualTo(new[] { 30, 90, 89, 33 }));
        }

        [Test]
        public void TheDecodedStreamIsWithinTheMeasuredError()
        {
            ControlVectors v = Load();
            List<GestureFrame> g = Gesture(1);
            List<Ev> ev = Encode(g);
            Measure(g, Decode(ev, g), out double maxCents, out double rmsCents, out double maxDb);
            Assert.That(rmsCents, Is.EqualTo(v.error.rmsCents).Within(1e-4), "1.7365 cents rms");
            Assert.That(maxCents, Is.EqualTo(v.error.maxCents).Within(1e-4), "8.672 cents max (the vibrato peaks)");
            Assert.That(maxDb, Is.EqualTo(v.error.maxDb).Within(1e-4), "0.4103 dB");
            Assert.That(v.error.rmsCents, Is.EqualTo(1.7365).Within(1e-9));
            Assert.That(v.error.maxCents, Is.EqualTo(8.672).Within(1e-9));
            Assert.That(v.error.maxDb, Is.EqualTo(0.4103).Within(1e-9));
        }

        [Test]
        public void ControlsLeadByTheirGlideAtMostFiftyMillisecondsAndAStillHandPostsOnlyKeyframes()
        {
            Assert.That(ControlEncoder.DispatchLead(0.0333), Is.EqualTo(0.0333).Within(1e-12));
            Assert.That(ControlEncoder.DispatchLead(0.25), Is.EqualTo(0.05).Within(1e-12));
            Assert.That(ControlEncoder.DispatchLead(-1), Is.EqualTo(0));
            // A still hand at a steady level: On, then one keyframe every 0.25 s (4 per second), nothing else.
            var enc = new ControlEncoder("lane");
            var buffer = new ControlEvent[2];
            int ctl = 0, on = 0;
            for (int i = 0; i < 900; i++)
            {
                int n = enc.Frame(i * Frame, 60.0, 0.5, Frame, buffer);
                for (int k = 0; k < n; k++) { if (buffer[k].Kind == ControlEventKind.On) on++; else if (buffer[k].Kind == ControlEventKind.Ctl) ctl++; }
            }
            Assert.That(on, Is.EqualTo(1));
            Assert.That(ctl, Is.InRange(39, 40), "10 s of keyframes at 4 per second");
            // Close ends a sounding lane with a final level-0 Ctl and the Off; a silent lane posts nothing.
            Assert.That(enc.Close(10.0, buffer), Is.EqualTo(2));
            Assert.That(buffer[0].Kind, Is.EqualTo(ControlEventKind.Ctl));
            Assert.That(buffer[0].Vel, Is.EqualTo(0));
            Assert.That(buffer[1].Kind, Is.EqualTo(ControlEventKind.Off));
            Assert.That(enc.Close(10.1, buffer), Is.EqualTo(0));
            Assert.That(enc.ToTakeEvent(buffer[1]).type, Is.EqualTo("laneOff"));
        }
    }
}
