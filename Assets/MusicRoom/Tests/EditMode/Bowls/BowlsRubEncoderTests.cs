// BowlsRubEncoder (bowls.md 3.7; platform.md 6.3): a rub records through Core's ControlEncoder. The lab's 425 live
// contact frames (rub-encoder.mjs's input) give an On, Controls at no more than 30 Hz with a keyframe at least every
// 0.25 s, a final level-0 Control and the Off; the events decode to the unit's Contact commands with the right glides and
// angles; and a take of the C# hand logic's own rub played back on a playback lane sings like the live rub (lanes.mjs:
// within 1 dB, starting within 0.3 s).
using System;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio.Burst;
using MusicRoom.Bowls;
using MusicRoom.Core;
using MusicRoom.Tests.Audio;
using NUnit.Framework;
using UnityEngine;
using static MusicRoom.Tests.Bowls.BowlsSim;

namespace MusicRoom.Tests.Bowls
{
    public class BowlsRubEncoderTests
    {
        static List<TakeEvent> Encode(IEnumerable<double[]> frames, double R, int bowl)
        {
            var enc = new BowlsRubEncoder(bowl, R); var ev = new List<TakeEvent>();
            double last = double.NaN;
            foreach (var f in frames)
            {
                double dt = double.IsNaN(last) ? 1 / 72.0 : f[0] - last; last = f[0];
                if (f[1] < 0) enc.Release(ev);
                else enc.Frame(new BowlsRubFrame { t = f[0], kind = (int)f[1], N = f[2], vf = f[3], angle = f[4], hs = f[5] }, dt, ev);
            }
            return ev;
        }

        [Test]
        public void TheLabsFramesFollowThePlatformsControlStream()
        {
            var ev = Encode(BowlsRubVectors.Frames, BowlsRubVectors.R, BowlsRubVectors.Bowl);
            Assert.That(ev.First().type, Is.EqualTo(BowlsRubEncoder.On));
            Assert.That(ev.Last().type, Is.EqualTo(BowlsRubEncoder.Off));
            Assert.That(ev[ev.Count - 2].type, Is.EqualTo(BowlsRubEncoder.Ctl)); Assert.That(ev[ev.Count - 2].vel, Is.EqualTo(0f), "a final level-0 Control");
            Assert.That(ev.Count(e => e.type == BowlsRubEncoder.On), Is.EqualTo(1));
            Assert.That(ev.All(e => e.i == BowlsRubVectors.Bowl && e.lane == BowlsRubVectors.Bowl), Is.True, "bowl and live lane key");
            Assert.That(ev.All(e => !string.IsNullOrEmpty(e.zone)), Is.True, "every event of a real rub carries its zone (even the Off)");
            for (int k = 1; k < ev.Count; k++)
            {
                double gap = ev[k].t - ev[k - 1].t;
                if (ev[k].type == BowlsRubEncoder.Ctl && ev[k].vel > 0) Assert.That(gap, Is.GreaterThanOrEqualTo(1 / 30.0 - 1e-4), $"event {k}: at most 30 Hz");
                Assert.That(gap, Is.LessThanOrEqualTo(0.25 + 1 / 60.0), $"event {k}: a keyframe every 0.25 s");
                if (ev[k].type == BowlsRubEncoder.Ctl) Assert.That(ev[k].speed, Is.InRange(0f, 0.2501f));
            }
            double sounding = BowlsRubVectors.Frames.Where(f => f[1] >= 0).Last()[0] - BowlsRubVectors.Frames[0][0];
            Debug.Log($"[BOWLS] rub encoder (ControlEncoder): {ev.Count} events, {ev.Count / sounding:F1} per sounding second (the lab's own: {BowlsRubVectors.Events.Length})");
            Assert.That(ev.Count / sounding, Is.LessThanOrEqualTo(30.0));
        }

        [Test]
        public void DecodeMapsTheFieldsAndAimsTheGlide()
        {
            var on = new TakeEvent { type = BowlsRubEncoder.On, i = 1, lane = 3, zone = "lip", u = 0.4f, vel = 0.5f, pos = 0.2f, midi = 1500 };
            Assert.That(BowlsRubEncoder.Decode(on, 0.075, out var c), Is.True);
            Assert.That(c.kind, Is.EqualTo((int)BowlSurface.Lip)); Assert.That(c.N, Is.EqualTo(4.0).Within(1e-6)); Assert.That(c.vf, Is.EqualTo(0.4).Within(1e-6));
            Assert.That(c.omega, Is.EqualTo(0.4 / 0.075).Within(1e-5)); Assert.That(c.heightS, Is.EqualTo(0.9).Within(1e-6)); Assert.That(c.angle, Is.EqualTo(1.5).Within(1e-6));
            Assert.That(c.ramp, Is.EqualTo(0)); Assert.That(c.lane, Is.EqualTo(3)); Assert.That(c.close, Is.False);
            var ctl = on; ctl.type = BowlsRubEncoder.Ctl; ctl.speed = 0.1f;
            BowlsRubEncoder.Decode(ctl, 0.075, out c);
            Assert.That(c.ramp, Is.EqualTo(4800), "glides over speed");
            Assert.That(c.angle, Is.EqualTo(1.5 - 0.4 / 0.075 * 0.05).Within(1e-5), "aims where the finger is when the glide ends (lead 0.05 s)");
            var close = new TakeEvent { type = BowlsRubEncoder.Ctl, lane = 2, vel = 0f, speed = 0.05f };
            BowlsRubEncoder.Decode(close, 0.075, out c);
            Assert.That(c.close, Is.True, "the looper's own close carries no zone: every bowl of its lane");
        }

        [Test]
        public void ATakeOfARubPlaysBackLikeTheLiveRub()
        {
            const int B = 1;
            var frames = new List<double[]>(); var strikes = new List<SimCall>();
            var live = Simulate(new Opts { Seconds = 6.6, HandsAt = Hand(Circle(Placement.Reference, B)), OnCall = (c, t, a) =>
            {
                if (a.I != B) return;
                if (c == "strike") strikes.Add(a);
                else if (c == "contact") frames.Add(new[] { t, (double)(int)a.Surface, a.N, a.Vf, a.Angle, a.Hs });
                else if (c == "release") frames.Add(new[] { t, -1.0, 0, 0, 0, 0 });
            } });
            double R = Placed(Placement.Reference)[B].R;
            var ev = Encode(frames, R, B);
            var cmds = BowlsRubEncoder.Decode(ev, R, 1);
            using var pb = new BowlsRig(1);
            var g = Placed(Placement.Reference);
            for (int b = 0; b < 3; b++) pb.Pickup(b, Math.Atan2(-g[b].C.z, -g[b].C.x), 0, true);
            foreach (var s in strikes) pb.Strike(s.I, s.Kind, s.V, s.Angle, s.Hs, (long)BowlsConst.JsRound(s.T * 48000));
            foreach (var c in cmds)
            {
                long at = (long)BowlsConst.JsRound(c.at * 48000);
                if (c.release) pb.Release(B, at, 1); else pb.Contact(B, c.kind, c.N, c.vf, c.angle, c.omega, c.heightS, 1, c.ramp, at);
            }
            var sv = new List<float>();
            for (int k = 0; k < 7 * 48000 / 256; k++) { pb.RenderBlock(256); unsafe { sv.Add(pb.B(B)->StSingVel); } }
            double MeanDb(Func<int, double> v, double a, double b) { double s = 0; int n = 0; for (int k = (int)(a * 48000 / 256); k < (int)(b * 48000 / 256); k++) { s += v(k); n++; } return 20 * Math.Log10(s / n); }
            double liveDb = MeanDb(k => live.Sv[k][B], 2.5, 6.0), pbDb = MeanDb(k => sv[k], 2.5, 6.0);
            int tl = live.Sv.FindIndex(q => q[B] > 0.15), tp = sv.FindIndex(q => q > 0.15);
            Debug.Log($"[BOWLS] playback through ControlEncoder events: {ev.Count} events, live {liveDb:F2} dB, playback {pbDb:F2} dB, sings at {tl * 256 / 48000.0:F2} / {tp * 256 / 48000.0:F2} s");
            Assert.That(Math.Abs(pbDb - liveDb), Is.LessThanOrEqualTo(1.0), "sings within 1 dB of the live rub");
            Assert.That(Math.Abs(tp - tl) * 256 / 48000.0, Is.LessThanOrEqualTo(0.3), "starts singing at the same moment");
            live.Rig?.Dispose();
        }
    }
}
