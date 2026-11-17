// The ghost hands' maths (Lessons/Ghost): the jointed hand reaches what it should (finger IK), the piano fingering (a hand
// per side, a finger per key in order, chords, held keys, the hands never cross, too many notes), the strike hands' timing
// (on the point at the stroke's moment, lifted between strokes, resting between phrases, a rub circling the rim), and the
// read-ahead the plan comes from (Recorder.Upcoming for a take and for a looping layer: what will be sent, when). A frame
// of the maths allocates nothing.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Lessons;
using MusicRoom.Lessons.Ghost;
using MusicRoom.Piano;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Lessons
{
    public class GhostHandsPlanTests
    {
        static Vector3 Key(int midi) => new Vector3(PianoSpec.KeyX(midi), 0.0065f, PianoSpec.IsBlack(midi) ? 0.077f : 0.03f);

        static PianoFingering NewFingering() => new PianoFingering(PianoSpec.KeyX(60), PianoSpec.KeyX(53), PianoSpec.KeyX(67));

        static void Play(PianoFingering f, double on, double len, params int[] midis)
        {
            foreach (int m in midis) f.Note(on, m, Key(m), PianoSpec.IsBlack(m), 0.8f);
            f.Flush(on + 0.05);
            foreach (int m in midis) f.Release(on + len, m);
        }

        /// <summary>Every finger stroke (hand, finger, key, on, off, palm) in time order.</summary>
        static List<(int hand, GhostStroke s)> Strokes(PianoFingering f)
        {
            var l = new List<(int, GhostStroke)>();
            for (int h = 0; h < 2; h++)
                for (int i = 0; i < f.Hands[h].Count; i++) if (f.Hands[h][i].Finger >= 0) l.Add((h, f.Hands[h][i]));
            l.Sort((a, b) => a.Item2.On.CompareTo(b.Item2.On));
            return l;
        }

        // ------------------------------------------------------------------------------------------------ the hand

        [Test]
        public void EachFingerReachesWhatItCanAndStopsAtItsLength()
        {
            for (int f = 0; f < GhostHandModel.Fingers; f++)
            {
                Vector3 rest = GhostHandModel.Tip(f, GhostHandModel.Curl(f, 0.5f));   // bent enough to reach a little further
                foreach (Vector3 d in new[] { Vector3.zero, new Vector3(0.01f, -0.012f, -0.008f), new Vector3(-0.008f, 0.006f, 0.004f) })
                {
                    Vector3 target = rest + d;
                    Vector3 got = GhostHandModel.Tip(f, GhostHandModel.Solve(f, target));
                    Assert.That((got - target).magnitude, Is.LessThan(0.002f), $"finger {f} reaches {d}");
                }
                // far out of reach: the finger points straight at it, no further than its length
                Vector3 baseP = GhostHandModel.Base[f];
                Vector3 far = baseP + new Vector3(0f, -0.05f, 0.4f);
                FingerPose p = GhostHandModel.Solve(f, far);
                Vector3 tip = GhostHandModel.Tip(f, p);
                Assert.That((tip - baseP).magnitude, Is.EqualTo(GhostHandModel.Reach(f)).Within(0.002f), $"finger {f} at full stretch");
                Assert.That(float.IsNaN(p.A1) || float.IsNaN(p.A2), Is.False);
            }
            // a fist brings the tips toward the palm
            for (int f = 1; f < GhostHandModel.Fingers; f++)
                Assert.That(GhostHandModel.Tip(f, GhostHandModel.Curl(f, 1f)).z, Is.LessThan(GhostHandModel.Tip(f, GhostHandModel.Curl(f, 0f)).z - 0.04f));
        }

        // ------------------------------------------------------------------------------------------------ piano fingering

        [Test]
        public void ARisingScaleIsPlayedByTheRightHandFingerByFingerInOrder()
        {
            var f = NewFingering();
            int[] scale = { 60, 62, 64, 65, 67, 69, 71, 72 };
            for (int i = 0; i < scale.Length; i++) Play(f, 1.0 + 0.3 * i, 0.25, scale[i]);
            List<(int hand, GhostStroke s)> st = Strokes(f);
            Assert.That(st.Count, Is.EqualTo(scale.Length));
            foreach (var x in st) Assert.That(x.hand, Is.EqualTo(PianoFingering.Right), "above middle C: the right hand");
            // the first five notes: thumb to little finger (the hand's natural places)
            for (int i = 0; i < 5; i++) Assert.That(st[i].s.Finger, Is.EqualTo(i), $"note {i}");
            // every key is under a finger within its stretch of the palm planned for it
            foreach (var x in st)
                Assert.That(Mathf.Abs(x.s.Point.x - x.s.PalmX - PianoFingering.Natural(x.hand, x.s.Finger)), Is.LessThanOrEqualTo(PianoFingering.MaxStretch + 1e-4f));
            // past the little finger the hand moves up (it does not stretch forever)
            Assert.That(st[7].s.PalmX, Is.GreaterThan(st[0].s.PalmX + 2f * PianoFingering.Span));
        }

        [Test]
        public void LowNotesGoToTheLeftHandAndHighNotesToTheRight()
        {
            var f = NewFingering();
            Play(f, 1.0, 0.3, 36);
            Play(f, 1.5, 0.3, 84);
            Play(f, 2.0, 0.3, 41);
            List<(int hand, GhostStroke s)> st = Strokes(f);
            Assert.That(st[0].hand, Is.EqualTo(PianoFingering.Left));
            Assert.That(st[1].hand, Is.EqualTo(PianoFingering.Right));
            Assert.That(st[2].hand, Is.EqualTo(PianoFingering.Left));
        }

        [Test]
        public void AChordGetsDistinctFingersInOrderAndAWideOneIsSplitBetweenTheHands()
        {
            var f = NewFingering();
            Play(f, 1.0, 0.5, 60, 64, 67);
            List<(int hand, GhostStroke s)> st = Strokes(f);
            Assert.That(st.Count, Is.EqualTo(3));
            Assert.That(st[0].hand == st[1].hand && st[1].hand == st[2].hand, "a triad fits one hand");
            var byKey = st.ConvertAll(x => x.s);
            byKey.Sort((a, b) => a.Key.CompareTo(b.Key));
            Assert.That(byKey[0].Finger, Is.LessThan(byKey[1].Finger), "right hand: lower key, lower finger");
            Assert.That(byKey[1].Finger, Is.LessThan(byKey[2].Finger));

            var g = NewFingering();
            Play(g, 1.0, 0.5, 36, 43, 48, 64, 67, 72);
            st = Strokes(g);
            Assert.That(st.Count, Is.EqualTo(6));
            foreach (var x in st) Assert.That(x.hand, Is.EqualTo(x.s.Key < 60 ? PianoFingering.Left : PianoFingering.Right), $"key {x.s.Key}");
            var fingers = new HashSet<(int, int)>();
            foreach (var x in st) Assert.IsTrue(fingers.Add((x.hand, x.s.Finger)), "one key a finger");
        }

        [Test]
        public void AFingerHoldingAKeyTakesNoOtherUntilItLetsGo()
        {
            var f = NewFingering();
            f.Note(1.0, 60, Key(60), false, 0.8f);                // held long
            f.Flush(1.1);
            int[] tune = { 62, 64, 65, 64, 62 };
            for (int i = 0; i < tune.Length; i++) Play(f, 1.2 + 0.2 * i, 0.15, tune[i]);
            f.Release(2.5, 60);
            List<(int hand, GhostStroke s)> st = Strokes(f);
            var held = st.Find(x => x.s.Key == 60);
            foreach (var x in st)
            {
                if (x.s.Key == 60) continue;
                bool overlaps = x.s.On < held.s.Off;
                if (overlaps && x.hand == held.hand) Assert.That(x.s.Finger, Is.Not.EqualTo(held.s.Finger), $"key {x.s.Key} while C is held");
            }
            Assert.That(held.s.Off, Is.EqualTo(2.5).Within(1e-9));
        }

        [Test]
        public void TheHandsNeverCrossAndTooManyNotesAreLeftToTheKeys()
        {
            var f = NewFingering();
            var rng = new Mulberry32(7);
            double t = 1.0;
            for (int i = 0; i < 120; i++)
            {
                int m = 36 + (int)(rng.Next() * 48);
                Play(f, t, 0.12, m);
                t += 0.18;
            }
            foreach (var (_, s) in Strokes(f))
            {
                bool hl = f.Hands[0].Palm(s.On, out float pl, out _), hr = f.Hands[1].Palm(s.On, out float pr, out _);
                if (hl && hr) Assert.That(pl, Is.LessThan(pr), $"the left palm is left of the right one when key {s.Key} sounds ({s.On:F2})");
            }
            Assert.That(f.Unfingered, Is.EqualTo(0));

            var g = NewFingering();
            Play(g, 1.0, 0.5, 48, 50, 52, 53, 55, 57, 59, 60, 62, 64, 65, 67);   // twelve at once
            List<(int hand, GhostStroke s)> st = Strokes(g);
            var used = new HashSet<(int, int)>();
            foreach (var x in st) Assert.IsTrue(used.Add((x.hand, x.s.Finger)), "never one finger on two keys");
            Assert.That(st.Count, Is.LessThanOrEqualTo(10));
            Assert.That(g.Unfingered + (12 - st.Count), Is.GreaterThan(0), "the extra notes are left to the keys");
        }

        [Test]
        public void AFingerIsOnItsKeyWhenItSoundsAndHoversWhenIdle()
        {
            var f = NewFingering();
            int[] tune = { 60, 67, 64, 72, 61, 66 };
            for (int i = 0; i < tune.Length; i++) Play(f, 1.0 + 0.4 * i, 0.3, tune[i]);
            foreach (var (hand, s) in Strokes(f))
            {
                GhostTrack tr = f.Hands[hand];
                tr.Palm(s.On, out float px, out float pz);
                var rest = new Vector3(px + PianoFingering.Natural(hand, s.Finger), 0.02f, 0.03f + pz);
                Vector3 at = tr.Finger(s.Finger, s.On, rest, out float pressed);
                Assert.That(Mathf.Abs(at.x - s.Point.x), Is.LessThan(0.001f), $"key {s.Key}: over it when it sounds");
                Assert.That(at.y, Is.LessThan(s.Point.y + 0.001f).And.GreaterThan(s.Point.y - PianoFingering.KeyDip - 0.001f), "on the key top, pressing");
                Assert.That(pressed, Is.GreaterThan(0.2f));
                Vector3 mid = tr.Finger(s.Finger, s.On + 0.15, rest, out float held);
                Assert.That(held, Is.EqualTo(1f).Within(1e-4f), "held down while the key is");
                Assert.That(Mathf.Abs(mid.x - s.Point.x), Is.LessThan(1e-4f));
            }
            // a finger with nothing to play hovers at its place under the palm
            var idleRest = new Vector3(0.1f, 0.02f, 0.03f);
            Assert.That((new GhostTrack().Finger(2, 3.0, idleRest, out float p0) - idleRest).magnitude, Is.LessThan(1e-5f));
            Assert.That(p0, Is.EqualTo(0f));
        }

        // ------------------------------------------------------------------------------------------------ strike hands

        static GhostStroke Hit(double on, Vector3 p, StrokeStyle style = StrokeStyle.Stick, float vel = 0.8f) =>
            new GhostStroke { On = on, Off = on, Point = p, Up = Vector3.up, Forward = Vector3.forward, Vel = vel, Finger = -1, Style = style, Key = -1 };

        [Test]
        public void AStrikeHandIsOnThePointAtTheStrokeAndLiftedBetween()
        {
            var tr = new GhostTrack();
            var a = new Vector3(0f, 0.7f, 0.3f);
            var b = new Vector3(0.3f, 0.85f, 0.4f);
            tr.Add(Hit(1.0, a));
            tr.Add(Hit(1.3, b));
            tr.Add(Hit(1.55, a, StrokeStyle.Stick, 0.4f));
            foreach (double on in new[] { 1.0, 1.3, 1.55 })
            {
                StrikeFrame f = tr.Strike(on);
                Vector3 want = on == 1.3 ? b : a;
                Assert.That((f.Point - want).magnitude, Is.LessThan(0.001f), $"on the point at {on}");
                Assert.That(f.Lift, Is.LessThan(0.001f));
                Assert.That(f.Hit, Is.EqualTo(1f));
            }
            Assert.That(tr.Strike(1.15).Lift, Is.GreaterThan(0.05f), "lifted between strokes");
            Assert.That(tr.Strike(1.15).Point.y, Is.GreaterThan(Mathf.Max(a.y, b.y)));
            // louder strokes lift higher
            Assert.That(GhostTrack.StrokeHeight(StrokeStyle.Stick, 1f, 0.4), Is.GreaterThan(GhostTrack.StrokeHeight(StrokeStyle.Stick, 0.3f, 0.4)));
            // after the phrase: up to rest above the last note
            StrikeFrame r = tr.Strike(3.0);
            Assert.That(r.Rest, Is.EqualTo(1f).Within(1e-4f));
            Assert.That((r.Point - (a + Vector3.up * GhostTrack.RestLift)).magnitude, Is.LessThan(0.001f));
        }

        [Test]
        public void BetweenPhrasesTheHandRestsAndComesDownJustInTime()
        {
            var tr = new GhostTrack();
            var a = new Vector3(0f, 0.7f, 0.3f);
            var b = new Vector3(-0.2f, 0.8f, 0.5f);
            tr.Add(Hit(1.0, a));
            tr.Add(Hit(5.0, b));
            StrikeFrame waiting = tr.Strike(3.0);
            Assert.That(waiting.Rest, Is.EqualTo(1f).Within(1e-4f), "resting between the phrases");
            Assert.That((waiting.Point - (a + Vector3.up * GhostTrack.RestLift)).magnitude, Is.LessThan(0.001f), "above the last note");
            Assert.That((tr.Strike(5.0 - GhostTrack.Approach - 0.01).Point - waiting.Point).magnitude, Is.LessThan(0.001f), "still at rest just before the approach");
            Assert.That((tr.Strike(5.0).Point - b).magnitude, Is.LessThan(0.001f), "on the next note on time");
            // the motion is continuous (no jump between two frames anywhere)
            Vector3 prev = tr.Strike(0.5).Point;
            for (double t = 0.5; t < 6.0; t += 1.0 / 72.0)
            {
                Vector3 p = tr.Strike(t).Point;
                Assert.That((p - prev).magnitude, Is.LessThan(0.06f), $"no jump at {t:F3}");
                prev = p;
            }
            // before the first stroke: waiting at rest above it
            var first = new GhostTrack();
            first.Add(Hit(2.0, b));
            Assert.That((first.Strike(1.0).Point - (b + Vector3.up * GhostTrack.RestLift)).magnitude, Is.LessThan(0.001f));
        }

        [Test]
        public void ARubCirclesTheRimWhileItSoundsThenRests()
        {
            var tr = new GhostTrack();
            var c = new Vector3(0f, 0.8f, 0.4f);
            tr.Add(new GhostStroke { On = 1.0, Off = double.PositiveInfinity, Point = c, Up = Vector3.up, Forward = Vector3.back, Finger = -1, Style = StrokeStyle.Rub, Key = 7, Radius = 0.08f, Spin = 4f });
            float lastAngle = 0f, turned = 0f;
            for (double t = 1.0; t < 2.0; t += 0.05)
            {
                Vector3 p = tr.Strike(t).Point;
                Assert.That((p - c).magnitude, Is.EqualTo(0.08f).Within(1e-4f), "on the rim");
                float a = Mathf.Atan2(p.x - c.x, -(p.z - c.z));
                if (t > 1.0) turned += Mathf.Abs(Mathf.DeltaAngle(lastAngle * Mathf.Rad2Deg, a * Mathf.Rad2Deg));
                lastAngle = a;
            }
            Assert.That(turned, Is.GreaterThan(150f), "it travels round");
            Assert.IsTrue(tr.Release(7, 2.0));
            StrikeFrame after = tr.Strike(3.5);
            Assert.That(after.Rest, Is.EqualTo(1f).Within(1e-4f), "resting after the rub");
        }

        [Test]
        public void PruningKeepsWhatShapesTheMotion()
        {
            var tr = new GhostTrack();
            for (int i = 0; i < 20; i++) tr.Add(Hit(1.0 + 0.1 * i, new Vector3(0.01f * i, 0.7f, 0.3f)));
            Vector3 before = tr.Strike(1.55).Point;
            tr.Prune(1.55);
            Assert.That(tr.Count, Is.EqualTo(1 + 14), "the latest past stroke and the future ones");
            Assert.That((tr.Strike(1.55).Point - before).magnitude, Is.LessThan(1e-6f), "same motion after pruning");
        }

        // ------------------------------------------------------------------------------------------------ the read-ahead

        [Test]
        public void ATakeIsReadAheadWithItsAudioTimes()
        {
            double now = 10;
            var bus = new EventBus();
            var rec = new Recorder(() => now, bus) { Roles = EventRoles.From(InstrumentRegistry.Discover()) };
            rec.SetTarget(TakeEventType.PianoOn, e => { });
            rec.SetTarget(TakeEventType.PianoOff, e => { });
            var events = new TakeEvent[16];
            var when = new double[16];
            Assert.That(rec.Upcoming(now, now + 2, events, when), Is.EqualTo(0), "nothing plays");
            rec.Record();
            rec.Add(TakeEvent.PianoOn(60, 0.8f));
            Assert.That(rec.Upcoming(now, now + 2, events, when), Is.EqualTo(0), "recording is not a playback");
            rec.Stop();
            var take = new List<TakeEvent>();
            TakeEvent on = TakeEvent.PianoOn(64, 0.7f); on.t = 0.2f; take.Add(on);
            TakeEvent off = TakeEvent.PianoOff(64); off.t = 0.6f; take.Add(off);
            TakeEvent late = TakeEvent.PianoOn(67, 0.7f); late.t = 1.5f; take.Add(late);
            Assert.IsTrue(rec.PlayEvents(take, 2.0));
            double start = now;
            int n = rec.Upcoming(now, now + 1.0, events, when);
            Assert.That(n, Is.EqualTo(2));
            Assert.That(events[0].midi, Is.EqualTo(64));
            Assert.That(when[0], Is.EqualTo(start + 0.2).Within(1e-6));
            Assert.That(when[1], Is.EqualTo(start + 0.6).Within(1e-6));
            Assert.That(events[0].lane, Is.EqualTo(PlaybackLanes.Take));
            Assert.That(rec.Upcoming(now + 1.0, now + 2.0, events, when), Is.EqualTo(1), "the next window has the rest");
            rec.Stop();
            Assert.That(rec.Upcoming(now, now + 2, events, when), Is.EqualTo(0), "stopped");
        }

        [Test]
        public void ALoopIsReadAheadAsTheLooperWillSendIt()
        {
            const int Sr = 48000;
            const long Beat90 = 32000, Pass = 8 * Beat90;
            var host = new FakeLoopHost();
            var loop = new Looper(host) { InputLatency = 0, Tidy = false };
            var bus = new EventBus();
            var rec = new Recorder(() => host.T, bus) { Looper = loop, Roles = EventRoles.From(InstrumentRegistry.Discover()) };
            rec.SetTarget(TakeEventType.Drum, e => rec.Add(e));
            host.OnDispatch = e => rec.Dispatch(e);
            void TickTo(long sample)
            {
                double end = sample / (double)Sr;
                while (host.T < end - 1e-12) { host.T = System.Math.Min(end, host.T + 1.0 / 72.0); rec.Update(); }
            }
            rec.Loop();
            long a = loop.ClickStart + 4 * Beat90;
            TickTo(a);
            rec.Add(TakeEvent.Drum("snare", 0.8f, 0.3f, "head", false));
            TickTo(a + 3 * Beat90);
            rec.Add(TakeEvent.Drum("tom1", 0.7f, 0.3f, "head", false));
            TickTo(a + Pass + 1000);
            Assert.That(rec.State, Is.EqualTo(RecorderState.Playing), "looping");
            var events = new TakeEvent[32];
            var when = new double[32];
            double from = host.T, to = host.T + 2.5 * Pass / (double)Sr;
            int n = rec.Upcoming(from, to, events, when);
            var read = new List<(string kind, double when)>();
            for (int i = 0; i < n; i++) { read.Add((events[i].kind, when[i])); Assert.That(events[i].lane, Is.EqualTo(PlaybackLanes.Layer(0))); }
            Assert.That(read.Count, Is.GreaterThanOrEqualTo(4), "two passes of two strokes ahead");
            for (int i = 1; i < read.Count; i++) Assert.That(read[i].when, Is.GreaterThanOrEqualTo(read[i - 1].when), "in time order");
            host.Sent.Clear();
            TickTo((long)System.Math.Round(to * Sr));
            var sent = host.Sent.FindAll(s => s.e.type == TakeEventType.Drum && s.when >= from && s.when < to);
            Assert.That(sent.Count, Is.EqualTo(read.Count), "what was read ahead is what was sent");
            for (int i = 0; i < sent.Count; i++)
            {
                Assert.That(sent[i].e.kind, Is.EqualTo(read[i].kind));
                Assert.That(sent[i].when, Is.EqualTo(read[i].when).Within(1.0 / Sr), "at the same audio time");
            }
            rec.Stop();
            Assert.That(rec.Upcoming(host.T, host.T + 2, events, when), Is.EqualTo(0));
        }

        // ------------------------------------------------------------------------------------------------ allocation

        [Test]
        public void AFrameOfTheMathsAllocatesNothing()
        {
            var f = NewFingering();
            var tr = new GhostTrack();
            for (int i = 0; i < 40; i++) tr.Add(Hit(1.0 + 0.2 * i, new Vector3(0.02f * (i % 5), 0.7f, 0.3f)));
            int m = 60;
            double t = 1.0;
            // warm
            f.Note(t, m, Key(m), false, 0.8f); f.Flush(t + 0.1); f.Release(t + 0.1, m);
            void Frames()
            {
                for (int i = 0; i < 200; i++)
                {
                    t += 1.0 / 72.0;
                    if (i % 6 == 0) { m = 55 + (i % 24); f.Note(t, m, Key(m), PianoSpec.IsBlack(m), 0.8f); f.Flush(t + 0.05); f.Release(t + 0.2, m); }
                    for (int h = 0; h < 2; h++)
                    {
                        f.Hands[h].Palm(t, out float px, out float pz);
                        for (int k = 0; k < 5; k++)
                        {
                            Vector3 tip = f.Hands[h].Finger(k, t, new Vector3(px, 0.02f, 0.03f + pz), out _);
                            GhostHandModel.Solve(k, tip - new Vector3(px, 0.05f, -0.06f));
                        }
                        f.Hands[h].Prune(t - 0.5);
                    }
                    tr.Strike(t);
                    tr.Prune(t - 0.5);
                }
            }
            Frames();   // every path once (first calls compile)
            for (int i = 0; i < 40; i++) tr.Add(Hit(t + 0.2 * i, new Vector3(0.02f * (i % 5), 0.7f, 0.3f)));
            Assert.That(Frames, Is.Not.AllocatingGCMemory());
        }
    }
}
