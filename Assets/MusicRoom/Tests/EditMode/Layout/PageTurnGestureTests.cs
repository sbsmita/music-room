// PageTurn from scripted palm samples, the cases of the menu reference check, section 3b: a hand 35 cm
// ahead of the head, ticked every 16 ms; between sweeps it comes to rest where it started (6 still samples 200 ms
// apart re-arm the detector).
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Layout
{
    public class PageTurnGestureTests
    {
        static readonly Vector3 Head = new Vector3(0f, 1.2f, 0f);
        static readonly Vector3 Fwd = Vector3.forward, Right = Vector3.right;

        PageTurnGesture _g;
        SweepHand[] _hands;
        double _now;

        [SetUp]
        public void SetUp()
        {
            _g = new PageTurnGesture();
            _now = 5000;
            _hands = new SweepHand[2];
            _hands[1] = new SweepHand
            {
                Connected = true, PalmOk = true, LastPlayMs = double.NegativeInfinity, Open = 0.18f,
                PalmCentre = Head + new Vector3(0f, -0.15f, 0f) + Fwd * 0.35f + Right * 0.12f,
                PalmNormal = Vector3.forward,   // vertical: facing away from the player
            };
        }

        int Tick(float dx, int n, double dtMs = 16, float dy = 0f)
        {
            int fired = 0;
            for (int i = 0; i < n; i++)
            {
                _hands[1].PalmCentre += Right * dx + Vector3.up * dy;
                _now += dtMs;
                fired += _g.Update((float)(dtMs / 1000.0), false, _now, Head, Fwd, _hands);
            }
            return fired;
        }

        /// <summary>menu-check back(h, dx): a second later the hand comes back and rests (re-arms the detector).</summary>
        void Back(float dx)
        {
            _now += 1000;
            _hands[1].PalmCentre += Right * dx;
            Tick(0f, 6, 200);
        }

        [Test]
        public void AFastLeftSweepTurnsForwardAndARightOneBack()
        {
            Assert.That(Tick(-0.008f, 30), Is.Zero, "0.5 m/s: too slow");
            Back(0.24f);
            Assert.That(Tick(-0.03f, 3), Is.Zero, "fast but only 9 cm");
            Back(0.09f);
            Assert.That(Tick(-0.03f, 8), Is.EqualTo(1), "1.9 m/s over 24 cm to the left: next");
            Assert.That(_g.LastDir, Is.EqualTo(1));
            Assert.That(Tick(-0.03f, 2), Is.Zero, "disarmed until the hand slows (and the cooldown)");
            Back(0.24f);
            Assert.That(Tick(0.03f, 8), Is.EqualTo(-1), "to the right: previous");
            Assert.That(_g.Count, Is.EqualTo(2));
        }

        [Test]
        public void ThePaperSaysTwentyCentimetresInZeroPointTwelveSecondsTurns()
        {
            // spec 08 section 9: "a 0.2 m left sweep in 0.12 s with a vertical palm gives +1".
            Tick(0f, 3);
            int fired = 0;
            for (int i = 0; i < 8; i++) fired += Tick(-0.025f, 1, 15);
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void ARepeatWithinTheCooldownGivesNothing()
        {
            Assert.That(Tick(-0.03f, 8), Is.EqualTo(1));
            // Re-armed quickly (one slow sample) but still within 0.6 s of the turn.
            Tick(0f, 2, 16);
            Assert.IsTrue(_g.Armed(1));
            Assert.That(Tick(0.03f, 8), Is.Zero, "cooldown");
        }

        [Test]
        public void OnlyAnOpenVerticalIdleHandTurns()
        {
            Back(0f);
            _hands[1].Pinching = true;
            Assert.That(Tick(-0.03f, 8), Is.Zero, "pinching");
            _hands[1].Pinching = false;
            Back(0.24f);
            _hands[1].Holding = true;
            Assert.That(Tick(-0.03f, 8), Is.Zero, "holding a stick");
            _hands[1].Holding = false;
            Back(0.24f);
            _hands[1].LastPlayMs = _now + 200;
            Assert.That(Tick(-0.03f, 8), Is.Zero, "played within 400 ms");
            _hands[1].LastPlayMs = double.NegativeInfinity;
            Back(0.24f);
            {
                int f = 0;
                for (int i = 0; i < 8; i++) { _hands[1].PalmCentre -= Right * 0.03f; _now += 16; f += _g.Update(0.016f, true, _now, Head, Fwd, _hands); }
                Assert.That(f, Is.Zero, "a rig held");
            }
            Back(0.24f);
            _hands[1].PalmNormal = new Vector3(0f, 0.9f, 0.43f).normalized;
            Assert.That(Tick(-0.03f, 8), Is.Zero, "palm flat");
            _hands[1].PalmNormal = Vector3.forward;
            Back(0.24f);
            _hands[1].Open = 0.09f;
            Assert.That(Tick(-0.03f, 8), Is.Zero, "a fist");
            _hands[1].Open = 0.18f;
            Back(0.24f);
            Assert.That(Tick(-0.03f, 8, 16, 0.03f), Is.Zero, "diagonal: not mostly across");
            Back(0.24f);
            _hands[1].Connected = false;
            Assert.That(Tick(-0.03f, 8), Is.Zero, "untracked");
            _hands[1].Connected = true;
            Back(0.24f);
            Assert.That(Tick(-0.03f, 8), Is.EqualTo(1), "and after all that a clean sweep still turns");
        }

        [Test]
        public void AJumpIsNotASweep()
        {
            Tick(0f, 3);
            _hands[1].PalmCentre -= Right * 0.5f;   // tracking came back half a metre away
            Assert.That(Tick(0f, 1), Is.Zero);
            Assert.That(_g.SampleCount(1), Is.EqualTo(1), "the history restarted at the jump");
        }

        [Test]
        public void TheSweepIsMeasuredInTheHeadsFrame()
        {
            // Facing +x the player's right is -z; a sweep along -z is to the player's right: previous.
            Vector3 fwd = Vector3.right;
            int fired = 0;
            for (int i = 0; i < 8; i++)
            {
                _hands[1].PalmCentre += new Vector3(0f, 0f, -0.03f);
                _now += 16;
                fired += _g.Update(0.016f, false, _now, Head, fwd, _hands);
            }
            Assert.That(fired, Is.EqualTo(-1));
        }
    }
}
