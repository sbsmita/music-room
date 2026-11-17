// HarpPlay unit tests (spec 05 section 5, ARCHITECTURE.md 15.3): the velocity curve, Side(), the catch / bend / slip /
// back-off / lift-off rules, glissando order and delays, the single-string poke, the 70 ms retrigger, resting tips and
// palm damping (tally >= 2, span for 3+, own follow-through skipped), animate decay, deformation, glow, hover, play
// zone, desktop click, voice expiry, recorder events and the LastPlayMs stamps. Expected values come from the geometry
// (string lines crossing a level sweep), not from re-running the harp's own code.
using System;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Harp;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Harp
{
    public class HarpPlayTests
    {
        [Test]
        public void VelocityOfMatchesTheReferenceCurve()
        {
            // Instrument data curves.harpVelocityOf: clamp((speed - .03) / 1.2, .2, 1).
            double[,] curve = { { 0.2, 0.2 }, { 0.3, 0.225 }, { 0.5, 0.3917 }, { 0.8, 0.6417 }, { 1, 0.8083 }, { 1.23, 1 }, { 1.5, 1 } };
            for (int k = 0; k < curve.GetLength(0); k++)
                Assert.That(HarpPlay.VelocityOf(curve[k, 0]), Is.EqualTo((float)curve[k, 1]).Within(1e-4f), "speed " + curve[k, 0]);
            Assert.That(HarpPlay.VelocityOf(0), Is.EqualTo(0.2f));
        }

        [Test]
        public void ConstantsAreTheReferenceValues()
        {
            Assert.That(HarpPlay.ZoneZ, Is.EqualTo(0.012));
            Assert.That(HarpPlay.PokeR, Is.EqualTo(0.009));
            Assert.That(HarpPlay.CatchMax, Is.EqualTo(0.006));
            Assert.That(HarpPlay.MinLift, Is.EqualTo(0.002));
            Assert.That(HarpPlay.HoldSlack, Is.EqualTo(1.5));
            Assert.That(HarpPlay.PluckSpeed, Is.EqualTo(0.2));
            Assert.That(HarpPlay.RetriggerMs, Is.EqualTo(70));
            Assert.That(HarpPlay.RestZOut, Is.EqualTo(0.005f));
            Assert.That(HarpPlay.RestZIn, Is.EqualTo(0.03f));
            Assert.That(HarpPlay.RestSpeed, Is.EqualTo(0.12f));
            Assert.That(HarpPlay.RestDrift, Is.EqualTo(0.01f));
            Assert.That(HarpPlay.RestHoldMs, Is.EqualTo(150));
            Assert.That(HarpPlay.RestReach, Is.EqualTo(0.02));
            Assert.That(HarpPlay.PluckGuardMs, Is.EqualTo(400));
            Assert.That(HarpPlay.HoverZ, Is.EqualTo(0.05f));
            Assert.That(HarpPlay.HoverReach, Is.EqualTo(0.02f));
            Assert.That(HarpPlay.VibBase, Is.EqualTo(0.0012f));
            Assert.That(HarpPlay.VibVel, Is.EqualTo(0.0028f));
            Assert.That(HarpPlay.DampTau, Is.EqualTo(0.035f));
        }

        [Test]
        public void SideIsPositiveTowardThePillarAndUIsAlongTheString()
        {
            var play = new HarpPlay();
            HarpString s = play.Strings[10];
            Vector3 mid = Vector3.Lerp(s.A, s.B, 0.5f) + HarpRig.Normal(10) * 0.01f;
            HarpPlay.Side(s, mid.x, mid.y, out double u, out double dist);
            Assert.That(u, Is.EqualTo(0.5).Within(1e-6));
            Assert.That(dist, Is.EqualTo(0.01).Within(1e-6));
            Assert.That(HarpRig.Normal(10).x, Is.LessThan(-0.99f), "+dist points toward the pillar (-x)");
            HarpPlay.Side(s, s.A.x, s.A.y, out u, out dist);
            Assert.That(u, Is.EqualTo(0.0).Within(1e-9));
            HarpPlay.Side(s, s.B.x, s.B.y, out u, out dist);
            Assert.That(u, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void FastSweepPlucksEachStringOnceInOrderWithGlissandoDelays()
        {
            var h = new HarpRig();
            Pointer p = h.Finger;
            const float y = 0.95f, z = 0.002f, x0 = -0.40f, x1 = -0.25f;
            h.Move(p, new Vector3(x0, y, z), new Vector3(x1, y, z));
            int[] want = { 7, 8, 9, 10, 11 };
            Assert.That(h.Sound.Plucks.Select(c => c.String).ToArray(), Is.EqualTo(want), "strings crossed between x -.40 and -.25 at y .95, in order");
            // Each string is released where the tip is CATCH_MAX past its line: x_k + .006 len / dy along the level move.
            double firstF = -1;
            for (int k = 0; k < want.Length; k++)
            {
                StringLayout s = HarpGeometry.Strings[want[k]];
                double dy = s.B.y - s.A.y, dx = s.B.x - s.A.x, len = Math.Sqrt(dx * dx + dy * dy);
                double xk = s.A.x + dx * (y - s.A.y) / dy;
                double f = (xk + 0.006 * len / dy - x0) / (x1 - x0);
                if (firstF < 0) firstF = f;
                FakeHarpSound.PluckCall c = h.Sound.Plucks[k];
                Assert.That(c.When - h.Sound.PluckBase, Is.EqualTo((f - firstF) * HarpRig.Dt).Within(2e-7), "glissando delay of string " + want[k]);
                Assert.That(c.Options.Mode, Is.EqualTo(PluckMode.Across), "a sweep along the plane");
                Assert.That(c.Options.U, Is.EqualTo((float)((y - s.A.y) / dy)).Within(1e-4f), "pluck position along the string");
                Assert.That(c.Vel, Is.EqualTo(1f), "10 m/s is full velocity");
                Assert.That(c.Options.Delay, Is.EqualTo(0.0), "the delay is folded into when");
                Assert.That(h.Play.Strings[want[k]].HasVoice, Is.True);
                Assert.That(h.Play.Strings[want[k]].Flash, Is.EqualTo(1f).Within(0.06f), "lit at once (one frame of fade)");
                Assert.That(h.Play.Strings[want[k]].AmpD, Is.EqualTo(-1f).Within(1e-3f), "swings along the stroke (toward -dist)");
            }
            Assert.That(h.Sound.Plucks.Select(c => c.When).ToArray(), Is.Ordered, "spread in time, in order");
            Assert.That(h.Sound.Plucks[4].When - h.Sound.Plucks[0].When, Is.GreaterThan(0.005), "a fast sweep is a glissando, not a chord");
            Assert.That(h.Events.Select(e => e.i).ToArray(), Is.EqualTo(want));
            Assert.That(h.Events.All(e => e.type == TakeEventType.Harp && e.mode == "across" && e.vel == 1f), Is.True);
            Assert.That(h.Hand.LastPlayMs, Is.EqualTo(h.Play.NowMs), "plucking stamps the hand (never read as a grab)");
            Assert.That(h.Play.Strings[12].HasVoice, Is.False, "string 12 (x -.248) was not reached");
        }

        [Test]
        public void SlowCrossingsAreIgnoredFastOnesAreCaught()
        {
            // 8 mm off the plane (not resting: zs > REST_Z_OUT), 12 mm across string 10.
            var slow = new HarpRig();
            slow.Move(slow.Finger, HarpRig.OnString(10, 0.95f, 0.004f, 0.008f), HarpRig.OnString(10, 0.95f, -0.008f, 0.008f), 0.08f); // 0.15 m/s
            Assert.That(slow.Sound.Plucks.Count, Is.EqualTo(0), "slower than PLUCK_SPEED: jitter or a hand being placed");
            Assert.That(slow.Play.RecordOf(slow.Finger).Held, Is.Null);
            var fast = new HarpRig();
            fast.Move(fast.Finger, HarpRig.OnString(10, 0.95f, 0.004f, 0.008f), HarpRig.OnString(10, 0.95f, -0.008f, 0.008f), 0.04f); // 0.3 m/s
            Assert.That(fast.Sound.Plucks.Select(c => c.String).ToArray(), Is.EqualTo(new[] { 10 }));
        }

        [Test]
        public void CatchBendsSilentlyThenSlipsAndSounds()
        {
            var h = new HarpRig();
            Pointer p = h.Finger;
            HarpString s = h.Play.Strings[10];
            h.Move(p, HarpRig.OnString(10, 0.95f, 0.006f, 0.001f), HarpRig.OnString(10, 0.95f, -0.003f, 0.001f));
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(0), "the sound starts at the release, never at the touch");
            PointerRecord rec = h.Play.RecordOf(p);
            Assert.That(rec.Held, Is.SameAs(s));
            Assert.That(s.HeldBy, Is.SameAs(rec));
            Assert.That(s.Bend, Is.EqualTo(0.003f).Within(1e-5f));
            Assert.That(s.BendD, Is.EqualTo(-1f).Within(1e-3f));
            Assert.That(h.Hand.LastPlayMs, Is.EqualTo(h.Play.NowMs), "a catch is playing too");
            Assert.That(s.ShapeDirty, Is.True, "the bend is drawn");
            // Pulled on to 9 mm past: slips at CATCH_MAX, f = (.006 - .003) / (.009 - .003) = .5, the first sound (delay 0).
            h.Move(p, HarpRig.OnString(10, 0.95f, -0.003f, 0.001f), HarpRig.OnString(10, 0.95f, -0.009f, 0.001f));
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(1));
            FakeHarpSound.PluckCall c = h.Sound.Plucks[0];
            Assert.That(c.String, Is.EqualTo(10));
            Assert.That(c.When, Is.EqualTo(0.0), "one isolated slip plays ASAP (section 17), not a block late");
            Assert.That(c.Vel, Is.EqualTo(HarpPlay.VelocityOf(0.006 / HarpRig.Dt)).Within(1e-3f), "as fast as it was being pulled");
            Assert.That(rec.Held, Is.Null);
            Assert.That(s.HeldBy, Is.Null);
            Assert.That(s.Bend, Is.EqualTo(0f));
            Assert.That(s.Amp, Is.GreaterThan(0.001f));
        }

        [Test]
        public void BackingOffReturnsTheStringSilently()
        {
            var h = new HarpRig();
            Pointer p = h.Finger;
            h.Move(p, HarpRig.OnString(10, 0.95f, 0.006f, 0.001f), HarpRig.OnString(10, 0.95f, -0.003f, 0.001f));
            Assert.That(h.Play.RecordOf(p).Held, Is.Not.Null);
            h.Move(p, HarpRig.OnString(10, 0.95f, -0.003f, 0.001f), HarpRig.OnString(10, 0.95f, 0.0025f, 0.001f));
            Assert.That(h.Play.RecordOf(p).Held, Is.Null);
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(0));
            Assert.That(h.Play.Strings[10].Bend, Is.EqualTo(0f));
        }

        [Test]
        public void LiftingOffAHeldStringSoundsByHowFarItWasPulled()
        {
            var h = new HarpRig();
            Pointer p = h.Finger;
            h.Move(p, HarpRig.OnString(10, 0.95f, 0.006f, 0.001f), HarpRig.OnString(10, 0.95f, -0.004f, 0.001f));
            float speed = h.Play.RecordOf(p).Speed;
            Assert.That(speed, Is.EqualTo(0.010f / HarpRig.Dt).Within(1e-3f));
            h.Move(p, HarpRig.OnString(10, 0.95f, -0.004f, 0.001f), HarpRig.OnString(10, 0.95f, -0.004f, 0.03f));
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(1));
            Assert.That(h.Sound.Plucks[0].Vel, Is.EqualTo(HarpPlay.VelocityOf(speed) * (0.004f / 0.006f)).Within(1e-3f));
        }

        [Test]
        public void APokeTakesOnlyTheNearestOfTwoStrings()
        {
            // At y 1.0, x -.076 is 4.8 mm from string 20 and 7.6 mm from string 21: both within POKE_R.
            var h = new HarpRig();
            h.Move(h.Finger, new Vector3(-0.076f, 1.0f, 0.02f), new Vector3(-0.076f, 1.0f, -0.02f));
            Assert.That(h.Sound.Plucks.Select(c => c.String).ToArray(), Is.EqualTo(new[] { 20 }));
            Assert.That(h.Sound.Plucks[0].Options.Mode, Is.EqualTo(PluckMode.Through));
            Assert.That(h.Play.Strings[20].AmpZ, Is.EqualTo(-1f).Within(1e-3f), "swings through the plane, along the poke");
            Assert.That(h.Events[0].mode, Is.EqualTo("through"));
        }

        [Test]
        public void TheSameStringNeverSoundsTwiceWithin70ms()
        {
            var h = new HarpRig();
            h.Play.NowMs = 1000;
            Assert.IsTrue(h.Play.PlaybackPluck(5, 0.6f, 0.35f, 0f, PluckMode.Through));
            int first = h.Sound.Plucks[0].Id;
            h.Play.NowMs = 1069;
            Assert.IsFalse(h.Play.PlaybackPluck(5, 0.6f, 0.35f, 0f, PluckMode.Through), "69 ms later");
            h.Play.NowMs = 1071;
            Assert.IsTrue(h.Play.PlaybackPluck(5, 0.6f, 0.35f, 0f, PluckMode.Through), "71 ms later");
            Assert.That(h.Sound.Releases, Is.EqualTo(new[] { first }), "the finger meets the ringing string first: the old voice is released");
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(2));
        }

        [Test]
        public void ARestingTipNeverPlucksAndCountsAfter150ms()
        {
            var h = new HarpRig();
            Pointer p = h.Finger;
            Vector3 at = HarpRig.OnString(9, 0.95f, 0f, 0.001f);
            // Trembling across the string line, slowly: resting, never a stroke.
            for (int k = 0; k < 30; k++)
            {
                Vector3 a = at + HarpRig.Normal(9) * (k % 2 == 0 ? 0.0005f : -0.0005f);
                Vector3 b = at + HarpRig.Normal(9) * (k % 2 == 0 ? -0.0005f : 0.0005f);
                h.Move(p, a, b, HarpRig.Dt); // 1 mm per frame = 0.072 m/s < REST_SPEED
            }
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(0));
            Assert.That(h.Play.RecordOf(p).Since, Is.Not.NaN, "dwelling");
        }

        [Test]
        public void APalmAcrossThreeOrMoreStringsDampsTheWholeSpan()
        {
            var h = new HarpRig();
            foreach (int i in new[] { 8, 9, 10, 11, 12 }) h.Play.PlaybackPluck(i, 0.7f, 0.35f, 0f, PluckMode.Through);
            var tips = new List<Pointer> { h.Hand.Pointers[0], h.Hand.Pointers[1], h.Hand.Pointers[2] };
            HarpRig.Still(tips[0], HarpRig.OnString(8, 0.95f, 0f, 0.002f));
            HarpRig.Still(tips[1], HarpRig.OnString(10, 0.95f, 0f, 0.002f));
            HarpRig.Still(tips[2], HarpRig.OnString(11, 0.95f, 0f, 0.002f));
            double t0 = h.Play.NowMs;
            while (h.Play.NowMs - t0 < 140) { h.Update(tips); Assert.That(h.Sound.DampIds.Count, Is.EqualTo(0), "still settling"); }
            while (h.Play.NowMs - t0 < 170) h.Update(tips);
            // Span 8..11 (the palm between the outer tips); string 12 rings on.
            Assert.That(h.Sound.DampIds.Count, Is.EqualTo(4));
            Assert.That(h.Sound.DampCounts.All(n => n == 4), Is.True, "a whole hand: the shh for 4 strings");
            foreach (int i in new[] { 8, 9, 10, 11 }) { Assert.That(h.Play.Strings[i].HasVoice, Is.False); Assert.That(h.Play.Strings[i].Damped, Is.True); }
            Assert.That(h.Play.Strings[12].HasVoice, Is.True);
            Assert.That(h.Events.Where(e => e.type == TakeEventType.HarpDamp).Select(e => e.i).ToArray(), Is.EqualTo(new[] { 8, 9, 10, 11 }));
            Assert.That(h.Hand.LastPlayMs, Is.EqualTo(h.Play.NowMs), "a resting hand is playing, so its curled fingers are not a grab");
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(5), "resting never plucks");
        }

        [Test]
        public void TwoTipsDampOnlyTheirOwnStrings()
        {
            var h = new HarpRig();
            foreach (int i in new[] { 8, 9, 10 }) h.Play.PlaybackPluck(i, 0.7f, 0.35f, 0f, PluckMode.Through);
            var tips = new List<Pointer> { h.Hand.Pointers[0], h.Hand.Pointers[1] };
            HarpRig.Still(tips[0], HarpRig.OnString(8, 0.95f, 0f, 0.002f));
            HarpRig.Still(tips[1], HarpRig.OnString(10, 0.95f, 0f, 0.002f));
            for (int k = 0; k < 15; k++) h.Update(tips);
            Assert.That(h.Play.Strings[8].HasVoice, Is.False);
            Assert.That(h.Play.Strings[10].HasVoice, Is.False);
            Assert.That(h.Play.Strings[9].HasVoice, Is.True, "two placed fingers hold their own strings");
            Assert.That(h.Sound.DampCounts, Is.EqualTo(new[] { 2, 2 }));
        }

        [Test]
        public void OneTipOrTwoOnTheSameStringDoNotDamp()
        {
            var h = new HarpRig();
            h.Play.PlaybackPluck(8, 0.7f, 0.35f, 0f, PluckMode.Through);
            var tips = new List<Pointer> { h.Hand.Pointers[0], h.Hand.Pointers[1] };
            HarpRig.Still(tips[0], HarpRig.OnString(8, 0.95f, 0f, 0.002f));
            HarpRig.Still(tips[1], HarpRig.OnString(8, 0.90f, 0f, 0.002f));
            for (int k = 0; k < 15; k++) h.Update(tips);
            Assert.That(h.Play.Strings[8].HasVoice, Is.True, "hi <= lo: not a hand on the harp");
        }

        [Test]
        public void TheHandsOwnFollowThroughIsNotDamped()
        {
            var h = new HarpRig();
            foreach (int i in new[] { 8, 10 }) h.Play.PlaybackPluck(i, 0.7f, 0.35f, 0f, PluckMode.Through);
            h.Play.Strings[10].PluckedBy = h.Hand;   // this hand plucked string 10 just now
            var tips = new List<Pointer> { h.Hand.Pointers[0], h.Hand.Pointers[1] };
            HarpRig.Still(tips[0], HarpRig.OnString(8, 0.95f, 0f, 0.002f));
            HarpRig.Still(tips[1], HarpRig.OnString(10, 0.95f, 0f, 0.002f));
            for (int k = 0; k < 15; k++) h.Update(tips);
            Assert.That(h.Play.Strings[8].HasVoice, Is.False);
            Assert.That(h.Play.Strings[10].HasVoice, Is.True, "within PLUCK_GUARD_MS of its own pluck");
            Assert.That(h.Sound.DampCounts, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void AStringAnotherFingerHoldsStaysWithIt()
        {
            var h = new HarpRig();
            Pointer a = h.Hand.Pointers[0], b = h.Other.Pointers[0];
            var both = new List<Pointer> { a, b };
            // a catches string 10 (3 mm past); b sweeps fast across strings 9..11 in the same frame.
            a.Active = a.WasActive = true;
            a.Prev = HarpRig.OnString(10, 0.95f, 0.006f, 0.001f); a.Tip = HarpRig.OnString(10, 0.95f, -0.003f, 0.001f);
            a.SampleDt = HarpRig.Dt; a.Speed = Vector3.Distance(a.Prev, a.Tip) / HarpRig.Dt;
            b.Active = b.WasActive = true;
            b.Prev = new Vector3(-0.345f, 0.90f, 0.003f); b.Tip = new Vector3(-0.265f, 0.90f, 0.003f);
            b.SampleDt = HarpRig.Dt; b.Speed = Vector3.Distance(b.Prev, b.Tip) / HarpRig.Dt;
            h.Update(both);
            Assert.That(h.Play.Strings[10].HeldBy, Is.SameAs(h.Play.RecordOf(a)));
            Assert.That(h.Sound.Plucks.Select(c => c.String).ToArray(), Is.EqualTo(new[] { 9, 11 }), "b plays 9 and 11, not a's string");
        }

        [Test]
        public void LeavingLetsAHeldStringGoSilently()
        {
            var h = new HarpRig();
            Pointer p = h.Finger;
            h.Move(p, HarpRig.OnString(10, 0.95f, 0.006f, 0.001f), HarpRig.OnString(10, 0.95f, -0.003f, 0.001f));
            Assert.That(h.Play.Strings[10].HeldBy, Is.Not.Null);
            p.Active = false; // tracking lost
            h.Update(new List<Pointer> { p });
            Assert.That(h.Play.Strings[10].HeldBy, Is.Null);
            Assert.That(h.Play.Strings[10].Bend, Is.EqualTo(0f));
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(0));
            // Out of the zone box (|z| > .08) counts as leaving too.
            h.Move(p, HarpRig.OnString(10, 0.95f, 0.006f, 0.001f), HarpRig.OnString(10, 0.95f, -0.003f, 0.001f));
            Assert.That(h.Play.Strings[10].HeldBy, Is.Not.Null);
            h.Move(p, HarpRig.OnString(10, 0.95f, -0.003f, 0.001f), HarpRig.OnString(10, 0.95f, -0.003f, 0.09f));
            Assert.That(h.Play.Strings[10].HeldBy, Is.Null);
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(0), "leaving the zone returns it without a sound");
        }

        [Test]
        public void RingingDecaysWithTheNoteAndADampStopsItFast()
        {
            var h = new HarpRig();
            h.Play.PlaybackPluck(0, 1f, 0.5f, 0f, PluckMode.Across);
            HarpString s = h.Play.Strings[0];
            Assert.That(s.Amp, Is.EqualTo(0.004f).Within(1e-7f), "VIB_BASE + VIB_VEL * vel");
            Assert.That(s.Ring, Is.EqualTo(4.5f).Within(1e-6f));
            Assert.That(s.AmpD, Is.EqualTo(1f));
            Assert.That(s.AmpZ, Is.EqualTo(0f));
            Assert.That(s.AmpU, Is.EqualTo(0.5f));
            // E8: the ringing is the modal blur; its fundamental decays with the sound's own partial decay (IHarpSound.PartialTau,
            // the v2 harp's design decay for this test double).
            float tau1 = HarpPartialDecay.V2Tau(0, 1);
            Assert.That(s.BlurTau[0], Is.EqualTo(tau1));
            Assert.IsTrue(s.Blurring);
            h.UpdateEmpty(0.1f);
            Assert.That(s.Amp, Is.EqualTo(0.004f * Mathf.Exp(-0.1f / tau1)).Within(1e-7f), "the fundamental decays as the sound does");
            Assert.That(s.Flash, Is.EqualTo(0.6f).Within(1e-5f), "flash fades 4 per second");
            Assert.That(s.ShapeDirty, Is.True);
            Assert.That(s.Up[8] - s.Lo[8], Is.GreaterThan(0.004f), "a blur, not an alternating bow: both edges at once");
            float a = s.Amp;
            h.Play.Damp(s, 1);
            Assert.That(s.Damped, Is.True);
            h.UpdateEmpty(0.05f);
            Assert.That(s.Amp, Is.EqualTo(a * Mathf.Exp(-0.05f / 0.035f) * Mathf.Exp(-0.05f / tau1)).Within(1e-7f), "damped: tau 35 ms");
            for (int k = 0; k < 60; k++) h.UpdateEmpty(0.05f);
            Assert.That(s.Amp, Is.EqualTo(0f), "below 0.05 mm of swing it stops");
            Assert.IsFalse(s.Blurring);
            Assert.That(s.Moving, Is.False);
        }

        [Test]
        public void DeformationBendsAtTheFingerAndBlursAlongTheStroke()
        {
            var play = new HarpPlay();
            HarpString s = play.Strings[10];
            MeshData m = HarpGeometry.StringMesh(10, out _, out _);
            Vector3[] rest = m.Positions.ToArray(), dst = new Vector3[rest.Length];
            int mid = 8 * 6; // row 8 of 16: u = 0.5
            s.Bend = 0.004f; s.BendU = 0.5f; s.BendD = 1f; s.BendZ = 0f;
            HarpPlay.Deform(s, rest, dst, rest.Length);
            Assert.That(dst[mid].x - rest[mid].x, Is.EqualTo(-0.004f).Within(1e-7f), "mesh -x is the +dist side");
            Assert.That(Vector3.Distance(dst[0], rest[0]), Is.LessThan(1e-7f), "the neck end stays put");
            Assert.That(Vector3.Distance(dst[rest.Length - 1], rest[rest.Length - 1]), Is.LessThan(1e-7f), "the soundboard end stays put");
            Assert.That(dst[4 * 6].x - rest[4 * 6].x, Is.EqualTo(-0.002f).Within(1e-6f), "straight lines from each end to the fingertip");
            s.Bend = 0.02f;
            HarpPlay.Deform(s, rest, dst, rest.Length);
            Assert.That(dst[mid].x - rest[mid].x, Is.EqualTo(-0.006f).Within(1e-7f), "the drawn bend stops at CATCH_MAX");
            // E8: the core stays on the rest line; the two edge copies sit at the envelope, along the stroke (through the plane).
            s.Bend = 0f; s.AmpU = 0.5f; s.AmpD = 0f; s.AmpZ = 1f;
            s.Up[8] = 0.003f; s.Lo[8] = -0.002f;
            HarpPlay.Deform(s, rest, dst, rest.Length);
            Assert.That(Vector3.Distance(dst[mid], rest[mid]), Is.LessThan(1e-7f), "the core does not swing");
            HarpPlay.DeformEdge(s, rest, dst, rest.Length, true);
            Assert.That(dst[mid].z - rest[mid].z, Is.EqualTo(0.003f).Within(1e-7f), "the upper edge through the plane");
            Assert.That(dst[mid].x, Is.EqualTo(rest[mid].x));
            HarpPlay.DeformEdge(s, rest, dst, rest.Length, false);
            Assert.That(dst[mid].z - rest[mid].z, Is.EqualTo(-0.002f).Within(1e-7f), "the lower edge on the other side, in the same frame");
            Assert.That(HarpPlay.RingOf(rest[mid], 1f / s.Length), Is.EqualTo(8), "row 8 of 16 is envelope ring 8");
        }

        [Test]
        public void GlowShowsLessonTargetsAndThePluckFlash()
        {
            var h = new HarpRig();
            HarpString s = h.Play.Strings[3];
            Assert.That(h.Play.Glow(s), Is.EqualTo(new Color(0f, 0f, 0f, 1f)));
            h.Play.Highlight(3, 0.4f);
            h.Play.Highlight(3, 1f);
            h.Play.Highlight(3, 0.2f);
            Assert.That(s.TargetK, Is.EqualTo(1f), "the strongest highlight of the frame");
            Color g = h.Play.Glow(s);
            Assert.That(g.r, Is.EqualTo(HarpPlay.TargetGlow.r).Within(1e-6f));
            Assert.That(g.b, Is.EqualTo(HarpPlay.TargetGlow.b).Within(1e-6f));
            Assert.That(h.Play.Glow(h.Play.Strings[4]).b, Is.EqualTo(0f), "no other string glows");
            h.UpdateEmpty();
            Assert.That(h.Play.Glow(s).b, Is.GreaterThan(0.5f), "targeted this frame or last");
            h.UpdateEmpty();
            Assert.That(h.Play.Glow(s).b, Is.EqualTo(0f), "a frame without a highlight goes dark");
            h.Play.Highlight(3, 0f);
            Assert.That(h.Play.Glow(s).b, Is.EqualTo(0f));
            h.Play.PlaybackPluck(3, 0.5f, 0.35f, 0f, PluckMode.Through);
            Color f = h.Play.Glow(s);
            // E8: the flash at a quarter of the original gain (the blur shows the pluck): .8 x .25 of the way to #ffc46a.
            Assert.That(f.r, Is.EqualTo(HarpPlay.PluckGlow.r * 0.8f * HarpPlay.PluckFlashGain).Within(1e-6f), "warm flash: lerp toward #ffc46a by .8 x .25 flash");
            Assert.That(f.g, Is.EqualTo(HarpPlay.PluckGlow.g * 0.8f * HarpPlay.PluckFlashGain).Within(1e-6f));
            Assert.That(HarpPlay.PluckFlashGain, Is.EqualTo(0.25f));
            // Linear-space colours (the glow mixes in linear): the lesson cue's purple (was #58dcff) and #ffc46a.
            Assert.That(HarpPlay.TargetGlow.g, Is.EqualTo(Mathf.GammaToLinearSpace(LessonCue.Color.g)).Within(1e-6f));
            Assert.IsTrue(LessonCue.IsPurple(HarpPlay.TargetGlow.gamma), "the target string glows purple");
            Assert.That(HarpPlay.PluckGlow.b, Is.EqualTo(Mathf.GammaToLinearSpace(0x6a / 255f)).Within(1e-6f));
        }

        [Test]
        public void HoverPicksTheNearestStringAndBrightensTowardThePlane()
        {
            var play = new HarpPlay();
            Assert.IsTrue(play.HoverPick(HarpRig.OnString(10, 0.95f, 0.005f, 0.02f), out int i, out float u, out float strength));
            Assert.That(i, Is.EqualTo(10));
            StringLayout s = HarpGeometry.Strings[10];
            Assert.That(u, Is.EqualTo((0.95f - s.A.y) / (s.B.y - s.A.y)).Within(1e-4f));
            Assert.That(strength, Is.EqualTo(0.6f).Within(1e-5f), "1 - |z| / HOVER");
            Assert.IsFalse(play.HoverPick(HarpRig.OnString(10, 0.95f, 0f, 0.051f), out _, out _, out _), "beyond 5 cm of the plane");
            Assert.IsFalse(play.HoverPick(new Vector3(0.1f, 0.5f, 0f), out _, out _, out _), "no string within 2 cm");
            Assert.IsFalse(play.HoverPick(new Vector3(-0.3f, 1.52f, 0f), out _, out _, out _), "above the zone box");
        }

        [Test]
        public void ThePlayZoneIsTheStringPlaneOverTheStrings()
        {
            var play = new HarpPlay();
            Assert.IsTrue(play.OverStrings(HarpRig.OnString(10, 0.95f, 0f, 0.04f)), "4 cm in front of a string");
            Assert.IsTrue(play.OverStrings(HarpRig.OnString(10, 0.95f, 0.035f, -0.05f)), "3.5 cm aside, 5 cm behind");
            Assert.IsFalse(play.OverStrings(HarpRig.OnString(10, 0.95f, 0f, 0.051f)), "beyond HARP_PLANE");
            Assert.IsFalse(play.OverStrings(new Vector3(0.12f, 0.3f, 0f)), "more than HARP_REACH from every segment");
            StringLayout s0 = HarpGeometry.Strings[0];
            Assert.IsTrue(play.OverStrings(s0.A - (s0.B - s0.A).normalized * 0.03f), "segment distance: 3 cm past the end");
            Assert.IsFalse(play.OverStrings(s0.A - (s0.B - s0.A).normalized * 0.05f));
        }

        [Test]
        public void ADesktopClickPlucksTheNearestStringWithin2cm()
        {
            var h = new HarpRig();
            int i = h.Play.DesktopDown(HarpRig.OnString(12, 0.9f, 0.01f, 0f));
            Assert.That(i, Is.EqualTo(12));
            FakeHarpSound.PluckCall c = h.Sound.Plucks[0];
            Assert.That(c.Vel, Is.EqualTo(0.7f));
            Assert.That(c.Options.Mode, Is.EqualTo(PluckMode.Through));
            Assert.That(c.Options.Speed, Is.EqualTo(0.9f));
            StringLayout s = HarpGeometry.Strings[12];
            Assert.That(c.Options.U, Is.EqualTo((0.9f - s.A.y) / (s.B.y - s.A.y)).Within(1e-3f), "where it landed");
            Assert.That(h.Play.Strings[12].AmpZ, Is.EqualTo(1f), "a 'through' swing");
            Assert.That(h.Play.DesktopDown(new Vector3(0.1f, 0.5f, 0f)), Is.EqualTo(-1), "nothing within 2 cm");
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(1));
        }

        [Test]
        public void AVoiceIsDroppedOnceItHasPlayedOut()
        {
            var h = new HarpRig();
            h.Play.PlaybackPluck(21, 0.5f, 0.35f, 0f, PluckMode.Through);
            HarpString s = h.Play.Strings[21];
            Assert.That(s.Until, Is.EqualTo(h.Sound.Now + 1.62).Within(1e-6), "ring 1.62 s for C6 from now (an ASAP pluck)");
            h.Sound.Now = s.Until - 0.01;
            h.Play.Update(null, null, Matrix4x4.identity, HarpRig.Dt);
            Assert.That(s.HasVoice, Is.True);
            h.Sound.Now = s.Until + 0.001;
            h.Play.Update(null, null, Matrix4x4.identity, HarpRig.Dt);
            Assert.That(s.HasVoice, Is.False);
            h.Play.Damp(s, 1);
            Assert.That(h.Sound.DampIds.Count, Is.EqualTo(0), "nothing left to damp");
            Assert.That(h.Count(TakeEventType.HarpDamp), Is.EqualTo(0), "harpDamp is recorded only for a ringing voice");
        }

        [Test]
        public void PlaybackReplaysTheWholeStrokeAndRecordsIt()
        {
            var h = new HarpRig();
            h.Play.PlaybackPluck(6, 0.55f, 0.42f, 0.8f, PluckMode.Across);
            FakeHarpSound.PluckCall c = h.Sound.Plucks[0];
            Assert.That(c.Options.U, Is.EqualTo(0.42f));
            Assert.That(c.Options.Speed, Is.EqualTo(0.8f));
            Assert.That(c.Options.Mode, Is.EqualTo(PluckMode.Across));
            Assert.That(c.When, Is.EqualTo(0.0), "playback is live: ASAP");
            TakeEvent e = h.Events[0];
            Assert.That(e.type, Is.EqualTo(TakeEventType.Harp));
            Assert.That(e.i, Is.EqualTo(6));
            Assert.That(e.vel, Is.EqualTo(0.55f));
            Assert.That(e.u, Is.EqualTo(0.42f));
            Assert.That(e.speed, Is.EqualTo(0.8f));
            Assert.That(e.mode, Is.EqualTo("across"));
            Assert.That(h.Play.Strings[6].PluckedBy, Is.Null, "a playback pluck belongs to no hand");
            h.Play.PlaybackDamp(6);
            Assert.That(h.Events[1].type, Is.EqualTo(TakeEventType.HarpDamp));
            Assert.That(h.Events[1].i, Is.EqualTo(6));
            Assert.That(HarpPlay.ParseMode("across"), Is.EqualTo(PluckMode.Across));
            Assert.That(HarpPlay.ParseMode("through"), Is.EqualTo(PluckMode.Through));
            Assert.That(HarpPlay.ParseMode(null), Is.EqualTo(PluckMode.Through), "an old take without a mode");
            Assert.IsFalse(h.Play.PlaybackPluck(22, 0.5f, 0.35f, 0f, PluckMode.Through));
            Assert.IsFalse(h.Play.PlaybackPluck(-1, 0.5f, 0.35f, 0f, PluckMode.Through));
        }

        [Test]
        public void AnIsolatedPokePlaysAsapAndASweepSharesTheBlockAlignedBase()
        {
            // ARCHITECTURE.md 15.3: when = delay > 0 || sweeping ? FrameNow + block + delay : 0.
            var h = new HarpRig();
            Pointer p = h.Finger;
            // A poke straight through string 10's line from the player's side: one sound, ASAP, 'through'.
            h.Move(p, HarpRig.OnString(10, 0.95f, 0f, 0.02f), HarpRig.OnString(10, 0.95f, 0f, -0.01f));
            Assert.That(h.Sound.Plucks.Select(c => c.String).ToArray(), Is.EqualTo(new[] { 10 }));
            Assert.That(h.Sound.Plucks[0].When, Is.EqualTo(0.0), "an isolated poke is live: no block of delay");
            Assert.That(h.Sound.Plucks[0].Options.Mode, Is.EqualTo(PluckMode.Through));
            Assert.That(h.Play.Strings[10].Until, Is.EqualTo(h.Sound.Now + h.Sound.RingSeconds(HarpGeometry.MidiOf(10))).Within(1e-6), "rings from now (Ring is a float)");

            // A fast sweep (5 strings in one move): every member, the first included, on FrameNow + one block + delay.
            var g = new HarpRig();
            g.Move(g.Finger, new Vector3(-0.40f, 0.95f, 0.002f), new Vector3(-0.25f, 0.95f, 0.002f));
            Assert.That(g.Sound.Plucks.Count, Is.EqualTo(5));
            Assert.That(g.Sound.Plucks[0].When, Is.EqualTo(g.Sound.PluckBase), "the first member sets the base");
            Assert.That(g.Sound.Plucks.All(c => c.When >= g.Sound.PluckBase), Is.True);
        }

        [Test]
        public void AGlissandoGoingOnAcrossFramesKeepsTheBaseAndALonePluckLaterIsAsap()
        {
            var h = new HarpRig();
            Pointer p = h.Finger;
            // 2.2 m/s level sweep at 5 mm: one or two strings per frame, every frame.
            h.Sweep(p, new[] { new Vector3(-0.45f, 0.95f, 0f), new Vector3(-0.20f, 0.95f, 0f) }, 0.005f, 2.2f / 72f);
            var plucks = h.Sound.Plucks;
            Assert.That(plucks.Count, Is.GreaterThanOrEqualTo(6));
            Assert.That(plucks.Select(c => c.String).ToArray(), Is.Ordered, "in order");
            for (int k = 1; k < plucks.Count; k++)
            {
                Assert.That(plucks[k].When, Is.GreaterThan(0.0), "member " + k + " of a glissando in progress keeps the base");
                Assert.That(plucks[k].Sounds, Is.GreaterThanOrEqualTo(plucks[k - 1].Sounds), "and sounds after the one before");
            }
            // A while later, one string alone: live again.
            for (int k = 0; k < 20; k++) h.UpdateEmpty();
            h.Sound.Clear();
            h.Move(p, HarpRig.OnString(3, 0.8f, 0f, 0.02f), HarpRig.OnString(3, 0.8f, 0f, -0.01f));
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(1));
            Assert.That(h.Sound.Plucks[0].When, Is.EqualTo(0.0));
        }

        [Test]
        public void TheDesktopMouseHandNeverPlucksRestsOrDamps()
        {
            // ARCHITECTURE.md section 0: MouseHandSource pointers (HandState.IsMouse, Pointer.FromMouse) never play; the
            // harp's own click / drag path does.
            var h = new HarpRig();
            h.Hand.IsMouse = true;
            Assert.IsTrue(h.Finger.FromMouse);
            h.Move(h.Finger, new Vector3(-0.40f, 0.95f, 0.002f), new Vector3(-0.25f, 0.95f, 0.002f));
            h.Move(h.Finger, HarpRig.OnString(10, 0.95f, 0f, 0.02f), HarpRig.OnString(10, 0.95f, 0f, -0.01f));
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(0), "a fast sweep and a poke of the mouse hand are silent");
            Assert.That(h.Play.RecordOf(h.Finger) == null || h.Play.RecordOf(h.Finger).Held == null, Is.True, "nothing caught");
            Assert.That(h.Hand.LastPlayMs, Is.EqualTo(double.NegativeInfinity), "and it never counts as playing");

            // Resting on ringing strings: no damping either.
            foreach (int i in new[] { 8, 9, 10, 11 }) h.Play.PlaybackPluck(i, 0.7f, 0.35f, 0f, PluckMode.Through);
            var tips = new List<Pointer> { h.Hand.Pointers[0], h.Hand.Pointers[1], h.Hand.Pointers[2] };
            HarpRig.Still(tips[0], HarpRig.OnString(8, 0.95f, 0f, 0.002f));
            HarpRig.Still(tips[1], HarpRig.OnString(10, 0.95f, 0f, 0.002f));
            HarpRig.Still(tips[2], HarpRig.OnString(11, 0.95f, 0f, 0.002f));
            for (int k = 0; k < 30; k++) h.Update(tips);
            Assert.That(h.Sound.DampIds.Count, Is.EqualTo(0));
            foreach (int i in new[] { 8, 9, 10, 11 }) Assert.That(h.Play.Strings[i].HasVoice, Is.True);

            // The same hand from the tracker plays as usual.
            h.Hand.IsMouse = false;
            h.Sound.Clear();
            h.Step(1f);
            h.Move(h.Finger, new Vector3(-0.40f, 0.95f, 0.002f), new Vector3(-0.25f, 0.95f, 0.002f));
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(5));
        }

        [Test]
        public void WithoutAnEngineTheLogicStillRuns()
        {
            var play = new HarpPlay { NowMs = 5000 };
            Assert.IsTrue(play.PlaybackPluck(4, 0.6f, 0.35f, 0f, PluckMode.Through));
            Assert.That(play.Strings[4].HasVoice, Is.True, "a logical voice (palm damping and expiry keep working)");
            Assert.That(play.Strings[4].Until, Is.EqualTo(5.0 + (4.5 - 7 * 0.08)).Within(1e-6));
            play.NowMs = 5000 + 4000;
            play.Update(null, null, Matrix4x4.identity, 0.01f);
            Assert.That(play.Strings[4].HasVoice, Is.False);
        }
    }
}
