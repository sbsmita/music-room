// The kit's behaviour, case by case (spec 05 section 3): the strike test with arm / disarm, the crossing point, the rim,
// the finger and stick limits, the 3 ms and 45 ms windows, the hand-on-cymbal choke and its 60 ms .. 3 s window, the
// hi-hat stand pinch and pedal "chick", the visuals (flash, E7 cymbal tilt and head give, lower hi-hat), hover glows, play surfaces and the
// playback API. The kit sits at the origin, so world points are kit-local; every stroke has an exact sample interval.
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Drums;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Drums
{
    public class DrumKitTests
    {
        const float Dt = 1f / 72f;

        [Test]
        public void FingerStrikeThroughTheSnareHeadSoundsOnceAndRecords()
        {
            using var k = new KitFixture();
            Pointer p = k.Right.Pointers[0];
            // 1 cm down in one 1/72 s sample: vy = .72 m/s -> ((.72 - .12) / 1.38)^.7.
            KitFixture.Stroke(p, k.At(DrumSpec.SnareId, 0.05f, 0.006f, 0f), k.At(DrumSpec.SnareId, 0.05f, -0.004f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(1));
            var call = k.Host.AudioFake.Drums[0];
            float want = Mathf.Pow((0.72f - 0.12f) / 1.38f, 0.7f);
            Assert.That(call.Kind, Is.EqualTo(DrumKind.Snare));
            Assert.That(call.Vel, Is.EqualTo(want).Within(1e-3f));
            Assert.That(call.Strike.Zone, Is.EqualTo(DrumZone.Head));
            Assert.That(call.Strike.Pos, Is.EqualTo(0.05f / 0.18f).Within(1e-4f));
            Assert.That(call.Strike.Open, Is.False);
            var ev = k.Host.RecorderFake.Events[0];
            Assert.That(ev.type, Is.EqualTo(TakeEventType.Drum));
            Assert.That(ev.kind, Is.EqualTo("snare"));
            Assert.That(ev.zone, Is.EqualTo("head"));
            Assert.That(ev.vel, Is.EqualTo(want).Within(1e-3f));
            Assert.That(k.Right.LastPlayMs, Is.EqualTo(k.Host.NowMs), "a drumming hand is playing (grab guard)");
            for (int i = 0; i < 5; i++) Assert.That(k.Right.Pointers[i].DrumLastHit[0], Is.EqualTo(k.Host.NowMs), "every fingertip of the hand is stamped");
            // The flash decays in the same frame (hit, then flash -= 6 dt). E7: the drum no longer scales; its
            // head gives under the finger instead, at the crossing point (.05, 0), on the hit frame.
            DrumPiece snare = k.Piece(DrumSpec.SnareId);
            Assert.That(snare.Flash, Is.EqualTo(1f - 6f * Dt).Within(1e-5f));
            Assert.That(snare.Group.localScale, Is.EqualTo(Vector3.one), "FlashScale 0: no breathing drum");
            Assert.IsTrue(snare.Give.Active, "the head gives");
            Assert.That(snare.Give.CurrentDepth, Is.EqualTo(HeadGive.Dimple(0f, 0f, want, 0.18f)).Within(2e-4f), "about 4 mm x vel at the strike point");
            // Parked below the head the next frame: no second hit.
            KitFixture.Rest(p, k.At(DrumSpec.SnareId, 0.05f, -0.004f, 0f));
            k.Host.NowMs += 100;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(1));
        }

        [Test]
        public void ThePathsCrossingPointDecidesTheZoneNotWhereItEnded()
        {
            using var k = new KitFixture();
            Pointer p = k.Right.Pointers[0];
            // A fast slanted stroke ending outside the head: it crossed at rr = .17 (rim, .9 r = .162).
            KitFixture.Stroke(p, k.At(DrumSpec.SnareId, 0.16f, 0.02f, 0f), k.At(DrumSpec.SnareId, 0.20f, -0.06f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(1));
            Assert.That(k.Host.AudioFake.Drums[0].Strike.Zone, Is.EqualTo(DrumZone.Rim));
            Assert.That(k.Host.AudioFake.Drums[0].Strike.Pos, Is.EqualTo(0.17f / 0.18f).Within(1e-4f), "x = .16 + .04 * .25");
        }

        [Test]
        public void AnyPassDisarmsUntilTheTipRisesTwoCentimetres()
        {
            using var k = new KitFixture();
            Pointer p = k.Right.Pointers[0];
            int id = DrumSpec.Tom1Id;
            Assert.That(p.DrumArm[id], Is.EqualTo(-1), "unset counts as armed");
            KitFixture.Stroke(p, k.At(id, 0f, 0.01f, 0f), k.At(id, 0f, -0.01f, 0f), Dt);       // 1.44 m/s: sounds
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(1));
            Assert.That(p.DrumArm[id], Is.EqualTo(0));
            k.Host.NowMs += 100;
            KitFixture.Stroke(p, k.At(id, 0f, -0.01f, 0f), k.At(id, 0f, 0.015f, 0f), Dt);      // up, but only 1.5 cm
            k.Kit.Tick(k.Pointers, Dt);
            k.Host.NowMs += 100;
            KitFixture.Stroke(p, k.At(id, 0f, 0.015f, 0f), k.At(id, 0f, -0.01f, 0f), Dt);      // down again: disarmed
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(1), "jitter just below / above the head cannot retrigger");
            k.Host.NowMs += 100;
            KitFixture.Stroke(p, k.At(id, 0f, -0.01f, 0f), k.At(id, 0f, 0.03f, 0f), Dt);       // clearly above: re-armed
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(p.DrumArm[id], Is.EqualTo(1));
            k.Host.NowMs += 100;
            KitFixture.Stroke(p, k.At(id, 0f, 0.03f, 0f), k.At(id, 0f, -0.01f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(2));
        }

        [Test]
        public void ASlowPassIsSilentButStillDisarms()
        {
            using var k = new KitFixture();
            Pointer p = k.Right.Pointers[0];
            int id = DrumSpec.FloorId;
            KitFixture.Stroke(p, k.At(id, 0f, 0.001f, 0f), k.At(id, 0f, -0.0005f, 0f), Dt);    // .108 m/s < .12
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(0), "a gently placed finger is silent");
            Assert.That(p.DrumArm[id], Is.EqualTo(0));
            k.Host.NowMs += 100;
            KitFixture.Stroke(p, k.At(id, 0f, 0.015f, 0f), k.At(id, 0f, -0.01f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(0), "it must rise clear of the head before it strikes");
        }

        [Test]
        public void FingersStartWithinTenCentimetresSticksWithinThirtyFive()
        {
            using var k = new KitFixture();
            Pointer finger = k.Right.Pointers[0];
            Pointer stick = k.Sticks[0].TipPointer;
            // (Paths chosen clear of the other pieces: a stroke down onto tom 2 from high up passes through the ride.)
            KitFixture.Stroke(finger, k.At(DrumSpec.Tom2Id, 0f, 0.11f, 0f), k.At(DrumSpec.Tom2Id, 0f, -0.01f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(0), "finger from 11 cm above: not a stroke");
            KitFixture.Idle(finger);
            k.Host.NowMs += 100;
            KitFixture.Stroke(stick, k.At(DrumSpec.FloorId, 0f, 0.30f, 0f), k.At(DrumSpec.FloorId, 0f, -0.02f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(1), "a stick from 30 cm above strikes");
            Assert.That(k.Host.AudioFake.Drums[0].Kind, Is.EqualTo(DrumKind.Floor));
            Assert.That(k.Host.AudioFake.Drums[0].Vel, Is.EqualTo(1f), "23 m/s is past the stick's full stroke");
            k.Host.NowMs += 100;
            KitFixture.Stroke(stick, k.At(DrumSpec.SnareId, 0f, 0.36f, 0f), k.At(DrumSpec.SnareId, 0f, -0.02f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(1), "a stick from 36 cm above: not a stroke");
        }

        [Test]
        public void ADrumAnswersItsRimThreeCentimetresOutThePadAndCymbalsOnlyTheirPlate()
        {
            using var k = new KitFixture();
            Pointer p = k.Right.Pointers[0];
            KitFixture.Stroke(p, k.At(DrumSpec.SnareId, 0.205f, 0.01f, 0f), k.At(DrumSpec.SnareId, 0.205f, -0.01f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(1));
            Assert.That(k.Host.AudioFake.Drums[0].Strike.Zone, Is.EqualTo(DrumZone.Rim));
            Assert.That(k.Host.AudioFake.Drums[0].Strike.Pos, Is.EqualTo(1f));

            int n = 1;
            void Try(int id, float x, bool sounds, string what)
            {
                Pointer q = k.Left.Pointers[n % 5];
                n++;
                k.Host.NowMs += 100;
                KitFixture.Stroke(q, k.At(id, x, 0.01f, 0f), k.At(id, x, -0.01f, 0f), Dt);
                int before = k.Host.AudioFake.Drums.Count;
                k.Kit.Tick(k.Pointers, Dt);
                KitFixture.Idle(q);
                Assert.That(k.Host.AudioFake.Drums.Count - before, Is.EqualTo(sounds ? 1 : 0), what);
            }
            Try(DrumSpec.SnareId, 0.215f, false, "snare 3.5 cm past the head");
            Try(DrumSpec.KickPadId, 0.105f, false, "the pad has no rim");
            Try(DrumSpec.KickPadId, 0.095f, true, "the pad's face");
            Try(DrumSpec.CrashId, 0.215f, false, "past the crash's edge");
            Try(DrumSpec.CrashId, 0.20f, true, "the crash's edge");
            Assert.That(k.Host.AudioFake.Drums[k.Host.AudioFake.Drums.Count - 1].Strike.Zone, Is.EqualTo(DrumZone.Edge));
            Assert.That(k.Host.AudioFake.Drums[1].Kind, Is.EqualTo(DrumKind.Kick), "the pad plays the kick");
        }

        [Test]
        public void AStickShoulderStrikesCymbalsOnly()
        {
            using var k = new KitFixture();
            Pointer shoulder = k.Sticks[0].ShoulderPointer;
            KitFixture.Stroke(shoulder, k.At(DrumSpec.SnareId, 0f, 0.05f, 0f), k.At(DrumSpec.SnareId, 0f, -0.02f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(0));
            k.Host.NowMs += 100;
            KitFixture.Stroke(shoulder, k.At(DrumSpec.CrashId, 0.1f, 0.05f, 0f), k.At(DrumSpec.CrashId, 0.1f, -0.02f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(1));
            Assert.That(k.Host.AudioFake.Drums[0].Kind, Is.EqualTo(DrumKind.Crash));
            Assert.That(k.Host.AudioFake.Drums[0].Strike.Zone, Is.EqualTo(DrumZone.Bow));
            Assert.That(k.Sticks[0].LastPlayMs, Is.EqualTo(k.Host.NowMs), "stick hits stamp the stick owner");
            Assert.That(k.Sticks[0].TipPointer.DrumLastHit[DrumSpec.CrashId], Is.EqualTo(k.Host.NowMs), "and its tip's window");
            Assert.That(double.IsNegativeInfinity(k.Right.LastPlayMs), "not the hand");
        }

        [Test]
        public void ThreeMillisecondPieceWindowAndFortyFiveMillisecondHandWindow()
        {
            using var k = new KitFixture();
            int id = DrumSpec.SnareId;
            Vector3 up = k.At(id, 0f, 0.01f, 0f), down = k.At(id, 0f, -0.01f, 0f);
            double t0 = k.Host.NowMs;
            // Two fingers of the right hand and one of the left cross in the same frame: one hit.
            KitFixture.Stroke(k.Right.Pointers[0], up, down, Dt);
            KitFixture.Stroke(k.Right.Pointers[1], up, down, Dt);
            KitFixture.Stroke(k.Left.Pointers[0], up, down, Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(1));
            k.IdleAll();
            Vector3 up2 = k.At(id, 0.05f, 0.01f, 0f), down2 = k.At(id, 0.05f, -0.01f, 0f);
            // 10 ms later the other hand's fresh finger flams.
            k.Host.NowMs = t0 + 10;
            KitFixture.Stroke(k.Left.Pointers[1], up2, down2, Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(2), "the other hand can play a flam");
            k.IdleAll();
            // 30 ms after its hit the right hand's other fingers cannot retrigger; 50 ms after, they can.
            k.Host.NowMs = t0 + 30;
            KitFixture.Stroke(k.Right.Pointers[2], up2, down2, Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(2), "45 ms per hand");
            k.IdleAll();
            k.Host.NowMs = t0 + 50;
            KitFixture.Stroke(k.Right.Pointers[3], up2, down2, Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(3));
        }

        [Test]
        public void AStillHandOnARingingCymbalChokesItAfterSixtyMilliseconds()
        {
            using var k = new KitFixture();
            Pointer p = k.Right.Pointers[0];
            int id = DrumSpec.CrashId;
            KitFixture.Stroke(p, k.At(id, 0.1f, 0.01f, 0f), k.At(id, 0.1f, -0.01f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            double t0 = k.Host.NowMs;
            DrumPiece crash = k.Piece(id);
            Assert.IsTrue(crash.Tilt.Moving, "the struck cymbal rocks");
            KitFixture.Rest(p, k.At(id, 0.1f, -0.01f, 0f));
            k.Host.NowMs = t0 + 50;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Chokes.Count, Is.EqualTo(0), "the stroke itself is not a choke (60 ms)");
            k.Host.NowMs = t0 + 70;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Chokes, Is.EqualTo(new[] { DrumKind.Crash }));
            Assert.IsTrue(crash.Choked);
            float held = crash.Tilt.MagnitudeDeg;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(crash.Tilt.MagnitudeDeg, Is.LessThan(held), "the hand on the plate damps its rocking (tau 30 ms)");
            Assert.That(k.Right.LastPlayMs, Is.EqualTo(t0 + 70));
            var last = k.Host.RecorderFake.Events[k.Host.RecorderFake.Events.Count - 1];
            Assert.That(last.type, Is.EqualTo(TakeEventType.DrumChoke));
            Assert.That(last.kind, Is.EqualTo("crash"));
            k.Host.NowMs = t0 + 90;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Chokes.Count, Is.EqualTo(1), "once per ring");
        }

        [Test]
        public void NoChokeAfterThreeSecondsByAStickByAMovingOrALiftingHand()
        {
            using var k = new KitFixture();
            Pointer p = k.Right.Pointers[0];
            int id = DrumSpec.RideId;
            KitFixture.Stroke(p, k.At(id, 0.1f, 0.01f, 0f), k.At(id, 0.1f, -0.01f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            double t0 = k.Host.NowMs;
            KitFixture.Idle(p);

            Pointer stick = k.Sticks[0].TipPointer;
            KitFixture.Rest(stick, k.At(id, 0.05f, -0.005f, 0f));
            k.Host.NowMs = t0 + 100;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Chokes.Count, Is.EqualTo(0), "a stick resting on the plate does not choke it");
            KitFixture.Idle(stick);

            Pointer q = k.Left.Pointers[0];
            KitFixture.Stroke(q, k.At(id, 0.05f, 0.01f, 0f), k.At(id, 0.05f, 0.01f, 0.007f), Dt);   // .5 m/s sideways, on the plate
            k.Host.NowMs = t0 + 200;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Chokes.Count, Is.EqualTo(0), "speed over .35 m/s");
            KitFixture.Stroke(q, k.At(id, 0.05f, 0.01f, 0f), k.At(id, 0.05f, 0.015f, 0f), 1f);     // slow but rising 5 mm
            k.Host.NowMs = t0 + 300;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Chokes.Count, Is.EqualTo(0), "|dy| over 4 mm");
            KitFixture.Rest(q, k.At(id, 0.23f, 0.0f, 0f));
            k.Host.NowMs = t0 + 400;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Chokes.Count, Is.EqualTo(0), "off the plate");
            KitFixture.Rest(q, k.At(id, 0.05f, 0.0f, 0f));
            k.Host.NowMs = t0 + 3001;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Chokes.Count, Is.EqualTo(0), "after 3 s it has stopped ringing anyway");
        }

        [Test]
        public void PinchingTheHiHatStandOpensItAndClosingChokesWithTheChick()
        {
            using var k = new KitFixture();
            Pointer index = k.Right.Pointers[0];
            KitFixture.Rest(index, new Vector3(-0.42f, 0.5f, 0.36f));   // kit-local: on the hat's pole
            k.Right.PinchStart = k.Right.GrabPinchStart = true;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.IsTrue(k.Kit.HatOpen);
            Assert.That(k.Host.AudioFake.Drums.Count + k.Host.AudioFake.Chokes.Count, Is.EqualTo(0), "opening is silent");
            Assert.That(k.Host.RecorderFake.Events[0].type, Is.EqualTo(TakeEventType.Hat));
            Assert.IsTrue(k.Host.RecorderFake.Events[0].open);
            Assert.That(k.Right.LastPlayMs, Is.EqualTo(k.Host.NowMs));
            k.Right.PinchStart = k.Right.GrabPinchStart = false;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.IsTrue(k.Kit.HatOpen, "a held pinch is not a second toggle");
            k.Right.PinchStart = k.Right.GrabPinchStart = true;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.IsFalse(k.Kit.HatOpen);
            Assert.That(k.Host.AudioFake.Chokes, Is.EqualTo(new[] { DrumKind.Hat }));
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(1));
            Assert.That(k.Host.AudioFake.Drums[0].Kind, Is.EqualTo(DrumKind.HatClose));
            Assert.That(k.Host.AudioFake.Drums[0].Vel, Is.EqualTo(0.7f));
            Assert.That(k.Host.AudioFake.Drums[0].Strike.Pos, Is.EqualTo(0.3f));
            Assert.IsFalse(k.Host.RecorderFake.Events[1].open);

            // Not a toggle: above the zone, a middle finger, a hand holding a stick.
            KitFixture.Rest(index, new Vector3(-0.42f, 0.75f, 0.36f));
            k.Kit.Tick(k.Pointers, Dt);
            KitFixture.Rest(index, new Vector3(-0.42f, 0.5f, 0.36f));
            k.Right.Holding = k.Sticks[0];
            k.Kit.Tick(k.Pointers, Dt);
            k.Right.Holding = null;
            KitFixture.Idle(index);
            KitFixture.Rest(k.Right.Pointers[1], new Vector3(-0.42f, 0.5f, 0.36f));
            k.Kit.Tick(k.Pointers, Dt);
            Assert.IsFalse(k.Kit.HatOpen);
            Assert.That(k.Host.RecorderFake.Events.Count, Is.EqualTo(2));
        }

        [Test]
        public void AnOpenHatRingsOpenAndItsLowerPlateEasesAtEighteenPerSecond()
        {
            using var k = new KitFixture();
            DrumPiece hat = k.Piece(DrumSpec.HatId);
            Assert.That(hat.Lower.localPosition.y, Is.EqualTo(-0.016f).Within(1e-6f), "closed by default");
            k.Kit.SetHatOpen(true, null);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(hat.Lower.localPosition.y, Is.EqualTo(-0.016f + (-0.05f + 0.016f) * Mathf.Min(1f, 18f * Dt)).Within(1e-6f));
            for (int i = 0; i < 60; i++) k.Kit.Tick(k.Pointers, Dt);
            Assert.That(hat.Lower.localPosition.y, Is.EqualTo(-0.05f).Within(1e-5f));

            Pointer p = k.Right.Pointers[0];
            k.Host.NowMs += 100;
            KitFixture.Stroke(p, k.At(DrumSpec.HatId, 0.05f, 0.01f, 0f), k.At(DrumSpec.HatId, 0.05f, -0.01f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            var call = k.Host.AudioFake.Drums[0];
            Assert.That(call.Kind, Is.EqualTo(DrumKind.HatOpen));
            Assert.IsTrue(call.Strike.Open);
            Assert.That(call.Strike.Zone, Is.EqualTo(DrumZone.Top));
            var ev = k.Host.RecorderFake.Events[k.Host.RecorderFake.Events.Count - 1];
            Assert.That(ev.kind, Is.EqualTo("hat"), "recorded as the hat, with its open state");
            Assert.IsTrue(ev.open);
        }

        [Test]
        public void CymbalsTipDownWhereStruckAndDrumsDoNotScale()
        {
            using var k = new KitFixture();
            Pointer p = k.Right.Pointers[0];
            // A finger on the ride's +x side: the struck edge goes down (tz < 0), the plate alone turns.
            KitFixture.Stroke(p, k.At(DrumSpec.RideId, 0.1f, 0.01f, 0f), k.At(DrumSpec.RideId, 0.1f, -0.01f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            KitFixture.Idle(p);
            DrumPiece ride = k.Piece(DrumSpec.RideId);
            float vel = k.Host.AudioFake.Drums[0].Vel;
            var want = new CymbalTilt();
            CymbalMotion.Strike(ref want, CymbalMotion.Ride, 0.1f, 0f, vel);
            CymbalMotion.Step(ref want, CymbalMotion.Ride, Dt, false);
            Assert.That(ride.Tilt.Tz, Is.EqualTo(want.Tz).Within(1e-6f), "the reference cymbalStrike + cymbalStep");
            Assert.That(ride.Tilt.Tz, Is.LessThan(0f), "+x struck: Euler(0, 0, tz < 0) sends +x down");
            Assert.That(Quaternion.Angle(ride.Top.localRotation, want.Rotation), Is.LessThan(1e-3f), "only the plate turns");
            Assert.That(ride.Group.localScale, Is.EqualTo(Vector3.one), "cymbals do not scale");
            float peak = 0f;
            for (int i = 0; i < 72; i++) { k.Kit.Tick(k.Pointers, Dt); peak = Mathf.Max(peak, ride.Tilt.MagnitudeDeg); }
            Assert.That(peak, Is.GreaterThan(0.2f).And.LessThan(CymbalMotion.Ride.MaxTiltDeg * CymbalMotion.MaxScale), "rocks visibly, within 1.5 x maxTilt");
            for (int i = 0; i < 72 * 30; i++) k.Kit.Tick(k.Pointers, Dt);
            Assert.IsFalse(ride.Tilt.Moving, "settles");
            Assert.That(ride.Top.localRotation, Is.EqualTo(Quaternion.identity), "back at rest, and no longer written");
            DrumPiece snare = k.Piece(DrumSpec.SnareId);
            Assert.That(snare.Group.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void HeadsGiveForThreeFramesAndEveryKickSwingsTheBeater()
        {
            using var k = new KitFixture();
            Pointer p = k.Right.Pointers[0];
            DrumPiece tom = k.Piece(DrumSpec.Tom1Id);
            Mesh head = tom.Give.Mesh;
            Assert.That(head.vertexCount, Is.EqualTo(1 + 6 * 48), "a ringed disc: 6 rings x 48 segments + the centre");
            Vector3[] rest = head.vertices;
            KitFixture.Stroke(p, k.At(DrumSpec.Tom1Id, -0.04f, 0.01f, 0.03f), k.At(DrumSpec.Tom1Id, -0.04f, -0.01f, 0.03f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            KitFixture.Idle(p);
            Assert.IsTrue(tom.Give.Active);
            Vector3[] dent = head.vertices;
            int deepest = 0;
            for (int i = 1; i < dent.Length; i++) if (rest[i].y - dent[i].y > rest[deepest].y - dent[deepest].y) deepest = i;
            Vector3 at = rest[deepest];
            Assert.That(new Vector2(at.x, at.z), Is.EqualTo(new Vector2(-0.04f, 0.03f)).Using(new Vec2Near(0.03f)), "the dent is where the finger crossed");
            k.Kit.Tick(k.Pointers, Dt);
            Assert.IsTrue(tom.Give.Active, "frame 2");
            k.Kit.Tick(k.Pointers, Dt);
            Assert.IsTrue(tom.Give.Active, "frame 3");
            k.Kit.Tick(k.Pointers, Dt);
            Assert.IsFalse(tom.Give.Active, "flat again after three frames");
            Assert.That(head.vertices, Is.EqualTo(rest), "the rest vertices are back");

            // The pad plays the kick: the beater is on the kick head at once, then swings back (critically damped).
            KickBeater beater = k.Kit.Beater;
            Assert.That(beater.AngleDeg, Is.EqualTo(KickBeater.RestDeg));
            KitFixture.Stroke(p, k.At(DrumSpec.KickPadId, 0f, 0.01f, 0f), k.At(DrumSpec.KickPadId, 0f, -0.01f, 0f), Dt);
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(k.Host.AudioFake.Drums[k.Host.AudioFake.Drums.Count - 1].Kind, Is.EqualTo(DrumKind.Kick));
            Assert.That(beater.AngleDeg, Is.EqualTo(KickBeater.ContactDeg), "the hit frame shows the ball on the head");
            Assert.IsTrue(k.Piece(DrumSpec.KickId).Give.Active, "the kick's head gives under the ball");
            Assert.That(beater.Strikes, Is.EqualTo(1));
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(beater.AngleDeg, Is.EqualTo(KickBeater.AngleAt(Dt)).Within(1e-4f));
            for (int i = 0; i < 40; i++) k.Kit.Tick(k.Pointers, Dt);
            Assert.That(beater.AngleDeg, Is.EqualTo(KickBeater.RestDeg), "back at rest");
            Assert.IsFalse(beater.Moving);
            // The nod kick (DrumsModule) is a forced Hit on the kick shell without a strike point: it swings it too.
            k.Kit.Hit(k.Piece(DrumSpec.KickId), 0.8f, null, true, new DrumStrike { Pos = 0.2f, Zone = DrumZone.Head });
            Assert.That(beater.Strikes, Is.EqualTo(2));
            Assert.That(beater.AngleDeg, Is.EqualTo(KickBeater.ContactDeg));
        }

        [Test]
        public void AKickAfterTheKitsTickShowsItsFirstPoseOnOneFrameOnly()
        {
            // The nod (FramePhase.NodKick), playback and a desktop click strike after DrumKit.Tick: their t = 0 pose must not
            // be held for a second frame (the heads and the beater age at the start of the next Tick).
            using var k = new KitFixture();
            DrumPiece kick = k.Piece(DrumSpec.KickId);
            KickBeater beater = k.Kit.Beater;
            k.Kit.Tick(k.Pointers, Dt);
            k.Kit.Hit(kick, 0.8f, null, true, new DrumStrike { Pos = 0.2f, Zone = DrumZone.Head });
            float d0 = kick.Give.CurrentDepth;
            Assert.That(d0, Is.GreaterThan(0.0005f), "the kick's head gives under the ball at once");
            Assert.That(beater.AngleDeg, Is.EqualTo(KickBeater.ContactDeg), "and the ball is on the head");
            k.Kit.Tick(k.Pointers, Dt);
            Assert.That(kick.Give.CurrentDepth, Is.EqualTo(d0 * Mathf.Exp(-Dt / HeadGive.Tau)).Within(1e-6f), "the next frame is aged one frame");
            Assert.That(beater.AngleDeg, Is.EqualTo(KickBeater.AngleAt(Dt)).Within(1e-4f), "the beater swings back on the next frame");
            k.Kit.Tick(k.Pointers, Dt);
            Assert.IsTrue(kick.Give.Active, "frame 3");
            k.Kit.Tick(k.Pointers, Dt);
            Assert.IsFalse(kick.Give.Active, "three frames in all, then flat");
        }

        sealed class Vec2Near : System.Collections.Generic.IEqualityComparer<Vector2>
        {
            readonly float _tol;
            public Vec2Near(float tol) { _tol = tol; }
            public bool Equals(Vector2 a, Vector2 b) => (a - b).magnitude <= _tol;
            public int GetHashCode(Vector2 v) => 0;
        }

        [Test]
        public void HoverPutsANineCentimetreDiscUnderTheFingertip()
        {
            using var k = new KitFixture();
            var glow = new HoverGlowPool(new MaterialLibrary(), null, true);
            Pointer p = k.Right.Pointers[0];
            DrumPiece snare = k.Piece(DrumSpec.SnareId);
            KitFixture.Rest(p, k.At(DrumSpec.SnareId, 0.1f, 0.05f, 0f));
            k.Kit.Hover(k.Pointers, glow);
            Assert.That(glow.Pending, Is.EqualTo(1));
            var s = glow.Spec(0);
            Assert.That(Vector3.Distance(s.Position, k.At(DrumSpec.SnareId, 0.1f, 0.002f, 0f)), Is.LessThan(1e-5f));
            Assert.That(Vector3.Angle(s.Normal, snare.Group.up), Is.LessThan(1e-3f));
            Assert.That(s.Length, Is.EqualTo(0.09f));
            Assert.That(s.Width, Is.EqualTo(0.09f));
            Assert.That(s.Strength, Is.EqualTo(0.5f).Within(1e-4f));
            glow.Update(0f);

            KitFixture.Rest(p, k.At(DrumSpec.SnareId, 0.19f, 0.02f, 0f));     // near the edge: pulled to r - .03
            k.Kit.Hover(k.Pointers, glow);
            Assert.That(Vector3.Distance(glow.Spec(0).Position, k.At(DrumSpec.SnareId, 0.15f, 0.002f, 0f)), Is.LessThan(1e-5f));
            glow.Update(0f);

            foreach (var y in new[] { 0.11f, -0.006f })
            {
                KitFixture.Rest(p, k.At(DrumSpec.SnareId, 0f, y, 0f));
                k.Kit.Hover(k.Pointers, glow);
                Assert.That(glow.Pending, Is.EqualTo(0), $"y {y}");
            }
            KitFixture.Rest(p, k.At(DrumSpec.SnareId, 0.21f, 0.02f, 0f));
            k.Kit.Hover(k.Pointers, glow);
            Assert.That(glow.Pending, Is.EqualTo(0), "beyond r + .02");
            KitFixture.Idle(p);

            Pointer shoulder = k.Sticks[0].ShoulderPointer;
            KitFixture.Rest(shoulder, k.At(DrumSpec.SnareId, 0f, 0.05f, 0f));
            k.Kit.Hover(k.Pointers, glow);
            Assert.That(glow.Pending, Is.EqualTo(0), "shoulders hover only over cymbals");
            KitFixture.Rest(shoulder, k.At(DrumSpec.CrashId, 0f, 0.05f, 0f));
            k.Kit.Hover(k.Pointers, glow);
            Assert.That(glow.Pending, Is.EqualTo(1));
        }

        [Test]
        public void PlaySurfacesAreTheHeadsPlatesAndTheHatStandSpot()
        {
            using var k = new KitFixture();
            Assert.That(k.Kit.PlaySurfaceAt(k.At(DrumSpec.SnareId, 0f, 0.2f, 0f)), Is.EqualTo(PlaySurface.Drums));
            Assert.That(k.Kit.PlaySurfaceAt(k.At(DrumSpec.SnareId, 0.215f, 0f, 0f)), Is.EqualTo(PlaySurface.Drums), "r + .04");
            Assert.That(k.Kit.PlaySurfaceAt(k.At(DrumSpec.CrashId, 0f, -0.1f, 0f)), Is.EqualTo(PlaySurface.Drums), "12 cm under a plate");
            Assert.That(k.Kit.PlaySurfaceAt(new Vector3(-0.42f, 0.5f, 0.36f)), Is.EqualTo(PlaySurface.HatStand));
            Assert.That(k.Kit.PlaySurfaceAt(new Vector3(0f, 0.2f, 0.3f)), Is.EqualTo(PlaySurface.None), "the snare's stand");
            Assert.That(k.Kit.PlaySurfaceAt(new Vector3(1.5f, 1f, 0f)), Is.EqualTo(PlaySurface.None));
            k.Root.SetActive(false);
            Assert.That(k.Kit.PlaySurfaceAt(k.At(DrumSpec.SnareId, 0f, 0.05f, 0f)), Is.EqualTo(PlaySurface.None), "not in the scene");
            Assert.IsFalse(k.Kit.IsPresent);
        }

        [Test]
        public void PlaybackIsForcedUsesTheFirstPieceOfAKindAndIsNotRecordedTwice()
        {
            using var k = new KitFixture();
            k.Kit.PlaybackHit("kick", 0.5f, new DrumStrikeInfo { Pos = 0.2f, Zone = "head" });
            k.Kit.PlaybackHit("kick", 0.5f, new DrumStrikeInfo { Pos = 0.2f, Zone = "head" });
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(2), "forced: no 3 ms window");
            Assert.That(k.Piece(DrumSpec.KickId).Hits, Is.EqualTo(2), "the kick shell, not the pad");
            Assert.That(k.Piece(DrumSpec.KickPadId).Hits, Is.EqualTo(0));
            Assert.That(k.Host.AudioFake.Drums[0].Strike.Pos, Is.EqualTo(0.2f));
            k.Kit.PlaybackHit("hat", 0.6f, new DrumStrikeInfo { Pos = 0.3f, Zone = "top", Open = true });
            Assert.That(k.Host.AudioFake.Drums[2].Kind, Is.EqualTo(DrumKind.HatOpen));
            k.Kit.PlaybackHit("snare", 0.6f, new DrumStrikeInfo { Pos = float.NaN, Zone = null });
            Assert.That(k.Host.AudioFake.Drums[3].Strike.Pos, Is.EqualTo(0.3f), "missing pos -> .3");
            Assert.That(k.Host.AudioFake.Drums[3].Strike.Zone, Is.EqualTo(DrumZone.Head), "missing zone -> head");
            k.Kit.PlaybackHit("cowbell", 0.6f, DrumStrikeInfo.Default);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(4));
            int events = k.Host.RecorderFake.Events.Count;
            Assert.That(events, Is.EqualTo(4), "playback hits reach the observers (DuringPlayback) like a live hit");
            k.Kit.PlaybackChoke("crash");
            Assert.That(k.Host.AudioFake.Chokes, Is.EqualTo(new[] { DrumKind.Crash }));
            Assert.IsTrue(k.Piece(DrumSpec.CrashId).Choked);
            k.Kit.SetHatOpen(true, null, playback: true);
            k.Kit.SetHatOpen(false, null, playback: true);
            Assert.That(k.Host.RecorderFake.Events.Count, Is.EqualTo(events), "chokes and hat moves from playback are not recorded");
            Assert.That(k.Host.AudioFake.Chokes[1], Is.EqualTo(DrumKind.Hat));
        }

        [Test]
        public void ADesktopClickPlaysTheNearestHeadOrPlateAtPointSevenFive()
        {
            using var k = new KitFixture();
            DrumPiece snare = k.Piece(DrumSpec.SnareId);
            Vector3 target = k.At(DrumSpec.SnareId, 0.17f, 0f, 0f);
            var down = new Ray(target + Vector3.up, Vector3.down);
            Assert.That(k.Kit.DesktopHit(down), Is.SameAs(snare));
            var call = k.Host.AudioFake.Drums[0];
            Assert.That(call.Vel, Is.EqualTo(0.75f));
            Assert.That(call.Strike.Zone, Is.EqualTo(DrumZone.Rim), "where it was clicked shapes the tone");
            Assert.That(call.Strike.Pos, Is.EqualTo(0.17f / 0.18f).Within(2e-3f));
            Assert.That(k.Host.RecorderFake.Events[0].kind, Is.EqualTo("snare"));
            Assert.IsNull(k.Kit.DesktopHit(new Ray(target - Vector3.up * 0.3f, Vector3.up)), "a drum head answers from above only");
            k.Host.NowMs += 100;
            Vector3 plate = k.At(DrumSpec.CrashId, 0.04f, 0f, 0f);
            Assert.That(k.Kit.DesktopHit(new Ray(plate - Vector3.up * 0.3f, Vector3.up)), Is.SameAs(k.Piece(DrumSpec.CrashId)), "a cymbal from either side");
            Assert.That(k.Host.AudioFake.Drums[1].Strike.Zone, Is.EqualTo(DrumZone.Bell));
            Assert.IsNull(k.Kit.DesktopHit(new Ray(new Vector3(3f, 2f, 3f), Vector3.down)), "nothing there");

            // ARCHITECTURE.md section 0: the nearest PlaySurface collider of any module wins. A collider on another layer
            // (room, furniture) does not take the click; another module's play surface in front of the snare does.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.transform.position = target + Vector3.up * 0.5f;
                wall.transform.localScale = new Vector3(0.3f, 0.02f, 0.3f);
                k.Host.NowMs += 100;
                Assert.That(k.Kit.DesktopHit(down), Is.SameAs(snare), "a Default-layer collider is not a click target");
                Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(3));
                wall.layer = Layers.PlaySurface;
                k.Host.NowMs += 100;
                Assert.IsNull(k.Kit.DesktopHit(down), "another module's play surface is nearer: its click");
                Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(3));
            }
            finally { Object.DestroyImmediate(wall); }
        }

        [Test]
        public void TheDesktopMouseHandNeverStrikesChokesOrPinchesTheHat()
        {
            // ARCHITECTURE.md section 0: the mouse hand is a visual stand-in; the click path plays the desktop, so its
            // fingertips passing through a head must not fire a second time.
            using var k = new KitFixture();
            k.Right.IsMouse = true;
            Pointer p = k.Right.Pointers[0];
            Assert.IsTrue(p.FromMouse);
            KitFixture.Stroke(p, k.At(DrumSpec.SnareId, 0.05f, 0.03f, 0f), k.At(DrumSpec.SnareId, 0.05f, -0.004f, 0f), Dt);
            k.Tick(Dt);
            Assert.That(k.Host.AudioFake.Drums.Count, Is.EqualTo(0), "no strike");

            k.Kit.PlaybackHit("crash", 0.8f, DrumStrikeInfo.Default);
            k.IdleAll();
            Vector3 onPlate = k.At(DrumSpec.CrashId, 0.08f, 0.005f, 0f);
            for (int i = 0; i < 8; i++) { KitFixture.Rest(p, onPlate); k.Tick(Dt); }
            Assert.That(k.Host.AudioFake.Chokes.Count, Is.EqualTo(0), "no choke");

            k.IdleAll();
            k.Right.PinchStart = k.Right.GrabPinchStart = true;
            KitFixture.Rest(p, k.Root.transform.TransformPoint(new Vector3(-0.42f, 0.5f, 0.36f)));
            k.Tick(Dt);
            Assert.IsFalse(k.Kit.HatOpen, "no hat-stand pinch");

            k.Right.IsMouse = false;
            k.Kit.Tick(k.Pointers, Dt);
            Assert.IsTrue(k.Kit.HatOpen, "the same pinch from a real hand opens it");
        }

        [Test]
        public void TryPickUpReportsGrabbedNearAndNoneAsLayoutReadsThem()
        {
            // IDrumsApi.TryPickUp (ARCHITECTURE.md 15.2) is the sticks' grab: Layout calls it and owns the consumption.
            using var k = new KitFixture();
            IDrumsApi api = k.Kit;
            Vector3 grip = k.Sticks[0].GripWorld;
            Vector3 far = grip + Vector3.up * 0.35f, near = grip + Vector3.up * 0.15f, on = grip + Vector3.up * 0.02f;
            k.TrackHandAt(k.Right, far, Quaternion.identity, pinched: true);
            Assert.That(api.TryPickUp(k.Right, far), Is.EqualTo(StickPick.None), "35 cm: nothing near");
            k.TrackHandAt(k.Right, near, Quaternion.identity, pinched: true);
            Assert.That(api.TryPickUp(k.Right, near), Is.EqualTo(StickPick.Near), "15 cm: inside the .24 m dead zone");
            Assert.IsNull(k.Right.Holding);
            Assert.IsFalse(k.Right.PinchStart, "Drums never touches the pinch flags");
            k.TrackHandAt(k.Right, on, Quaternion.identity, pinched: true);
            Assert.That(api.TryPickUp(k.Right, on), Is.EqualTo(StickPick.Grabbed));
            Assert.That(k.Right.Holding, Is.SameAs(k.Sticks[0]));
            Assert.That(api.TryPickUp(k.Right, on), Is.EqualTo(StickPick.None), "a hand holding a stick picks up no other");
            api.ReleaseAllSticks();
            k.Root.SetActive(false);
            Assert.That(api.TryPickUp(k.Right, on), Is.EqualTo(StickPick.None), "the kit out of the scene");
        }

        [Test]
        public void EveryHandPointerFartherThanReadsOnlyFingertips()
        {
            using var k = new KitFixture();
            Vector3 pad = k.Piece(DrumSpec.KickPadId).Group.position;
            Assert.IsTrue(k.Kit.EveryHandPointerFartherThan(0.2f, DrumSpec.KickPadId), "no hands");
            KitFixture.Rest(k.Right.Pointers[0], pad + new Vector3(0.1f, 0f, 0f));
            Assert.IsFalse(k.Kit.EveryHandPointerFartherThan(0.2f, DrumSpec.KickPadId));
            Assert.IsTrue(k.Kit.EveryHandPointerFartherThan(0.05f, DrumSpec.KickPadId));
            KitFixture.Idle(k.Right.Pointers[0]);
            KitFixture.Rest(k.Sticks[0].TipPointer, pad);
            Assert.IsTrue(k.Kit.EveryHandPointerFartherThan(0.2f, DrumSpec.KickPadId), "a stick on the pad is not a hand");
            Assert.IsTrue(k.Kit.EveryHandPointerFartherThan(0.2f, 99), "unknown piece");
        }
    }
}
