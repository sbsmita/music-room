// The piano's contact surface (10-upgrade/experience.md section 2.1; E1 / E3, visual only): the key under a fingertip by
// KeyAt widened by 2 mm, back in [0, L], its top at the key's CURRENT depth (the hinge rotation of PianoKeysView), and
// the white key beside a black one when the tip is below the black's top outside its visible width.
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Piano;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Piano
{
    public class PianoContactTests
    {
        sealed class Sink : IPianoSink
        {
            public VoiceHandle NoteOn(int midi, float vel) => default;
            public void NoteOff(int midi, VoiceHandle voice, bool thud) { }
            public void Record(in TakeEvent ev) { }
        }

        PianoAction P;

        [SetUp]
        public void SetUp() => P = new PianoAction(new Clock(), new Sink());

        [Test]
        public void ARestingWhiteKeyIsThePlaneOfTheKeyTops()
        {
            PianoKey c4 = P.KeyByMidi(60);
            var key = P.ContactKey(new Vector3(c4.X + 0.003f, 0.01f, 0.04f), out Vector3 point, out Vector3 normal, out float drop);
            Assert.That(key, Is.SameAs(c4));
            Assert.That(point.y, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(normal, Is.EqualTo(Vector3.up));
            Assert.That(drop, Is.EqualTo(0f).Within(1e-7f));
            Assert.That(P.ContactKey(new Vector3(c4.X, 0.01f, -0.002f), out _, out _, out _), Is.Null, "in front of the keys");
            Assert.That(P.ContactKey(new Vector3(c4.X, 0.01f, PianoSpec.L + 0.002f), out _, out _, out _), Is.Null, "behind them (the panel)");
            Assert.That(P.ContactKey(new Vector3(PianoSpec.X0 - 0.01f, 0.01f, 0.04f), out _, out _, out _), Is.Null, "beside the keyboard");
        }

        [Test]
        public void APressedKeyIsTiltedAboutItsHinge()
        {
            PianoKey e4 = P.KeyByMidi(64);
            e4.Depth = 1f;   // on the felt: 10 mm down at the front
            var key = P.ContactKey(new Vector3(e4.X, 0f, 0f), out Vector3 point, out Vector3 normal, out float drop);
            Assert.That(key, Is.SameAs(e4));
            Assert.That(drop, Is.EqualTo(PianoSpec.TRAVEL).Within(2e-5f), "10 mm at the front edge");
            float a = PianoSpec.MaxAngleWhite;
            Assert.That(normal.y, Is.EqualTo(Mathf.Cos(a)).Within(1e-6f));
            Assert.That(normal.z, Is.EqualTo(-Mathf.Sin(a)).Within(1e-6f), "the top faces a little toward the player");
            P.ContactKey(new Vector3(e4.X, 0f, 0.1f), out _, out _, out float dropBack);
            Assert.That(dropBack, Is.EqualTo(0.05f * Mathf.Sin(a)).Within(2e-5f), "a third of the way from the hinge");
            Assert.That(PianoAction.KeyTopY(e4, 0f), Is.EqualTo(-drop).Within(1e-6f));
        }

        [Test]
        public void BlackKeysAndTheWhiteBesideThem()
        {
            PianoKey cs4 = P.KeyByMidi(61), c4 = P.KeyByMidi(60);
            float back = 0.12f;
            var onTop = P.ContactKey(new Vector3(cs4.X, PianoSpec.BTOP + 0.005f, back), out Vector3 p1, out _, out _);
            Assert.That(onTop, Is.SameAs(cs4), "over the black key's top");
            Assert.That(p1.y, Is.EqualTo(PianoSpec.BTOP).Within(1e-6f));
            float side = cs4.X - (PianoSpec.BW / 2f + 0.001f);   // 1 mm beside its visible width, KeyAt's 1.5 mm margin
            Assert.That(P.KeyAt(side, back), Is.SameAs(cs4), "the contact logic's KeyAt still says black here");
            Assert.That(P.ContactKey(new Vector3(side, PianoSpec.BTOP + 0.004f, back), out _, out _, out _), Is.SameAs(cs4), "above its top: the black");
            Assert.That(P.ContactKey(new Vector3(side, 0.003f, back), out _, out _, out _), Is.SameAs(c4), "down on the white beside it: the white");
            Assert.That(P.ContactKey(new Vector3(cs4.X, 0.003f, back), out _, out _, out _), Is.SameAs(cs4), "inside the black itself: lifted onto it");
        }
    }
}
