// The drawn hand on the tape deck (headset report 2026-11-10: "the hands are not properly appearing on tape deck"). The
// deck was no contact surface (10-upgrade/experience.md 2.1), unlike the piano's keys and the menu's face: a fingertip
// pressing a peg or a 6 mm cap went on into the deck (a real finger meets no resistance) and the drawn finger was hidden
// inside the deck's body. These boot the app headless with scripted hands (manual frames) and check that a finger pressing
// REC, a loop-strip cap and the bare top is lifted onto it (E1, the drawn tip above the surface instead of in it) with its
// contact shadows (E3), while the press itself is unchanged (REC records exactly once from the raw joints); and that with
// the clamp off the raw tip is indeed inside (the reported fault).
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Lessons
{
    public class DeckHandContactTests
    {
        const float Dt = 1f / 90f;
        TestBoot _boot;
        App _app;
        TapeDeck _deck;
        IRecorder _recorder;

        static bool DeckAndFriends(System.Type t) => t.Name == "AudioModule" || t.Name == "LessonsModule" || t.Name == "RoomModule";

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: true, manualFrames: true);
            _boot.Hands.SetTracked(HandSide.Left, false);
            _boot.Hands.SetTracked(HandSide.Right, false);
            _boot.Options.ModuleFilter = DeckAndFriends;
            yield return _boot.Start();
            _app = _boot.App;
            foreach (var m in _app.Modules) if (m is LessonsModule lm) _deck = lm.Deck;
            Assert.That(_deck, Is.Not.Null);
            _recorder = _app.Get<IRecorder>();
            _boot.Step(Dt, 3);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        // A pressing hand: fingers forward over the deck (from the player's side, deck -z) and 35 degrees down, palm down.
        HandPose IndexAt(Vector3 tip)
        {
            Transform d = _deck.Root;
            Quaternion rot = Quaternion.LookRotation(d.TransformDirection(new Vector3(0f, -0.7f, 1f).normalized), d.TransformDirection(new Vector3(0f, 1f, 0.7f).normalized));
            return HandPose.Open(HandSide.Right, Vector3.zero, rot).WithPointing().WithIndexTipAt(tip);
        }

        /// The surface top over a control (its current depth) or the deck top, deck-local y.
        static float TopOf(DeckButton b, TapeDeck deck) =>
            deck.PegLocalPosition(b).y + (TapeDeck.ButtonLocal(b).y == TapeDeck.PegY ? TapeDeck.PegH : TapeDeck.CapH) * 0.5f;

        Vector3 Over(DeckButton b, float aboveTop) => _deck.Root.TransformPoint(new Vector3(TapeDeck.ButtonLocal(b).x, TopOf(b, _deck) + aboveTop, TapeDeck.ButtonLocal(b).z));

        float TipRadius => _app.HandInput.Get(HandSide.Right).radius[(int)HandJoint.IndexTip];

        /// Lower the index from 3 cm over the control's top to <paramref name="into"/> metres past where its pad touches,
        /// 4 mm per 90 Hz frame, then hold.
        void PressInto(DeckButton b, float into)
        {
            _boot.Hands.SetTracked(HandSide.Right, true);
            float r = TipRadius;
            for (float y = 0.03f; y >= r - into - 1e-4f; y -= 0.004f)
            {
                _boot.Hands.SetPose(HandSide.Right, IndexAt(Over(b, y)));
                _boot.Step(Dt);
            }
            _boot.Hands.SetPose(HandSide.Right, IndexAt(Over(b, r - into)));
            _boot.Step(Dt, 4);
        }

        /// The index tip as the hands draw it: the raw tip turned about the knuckle by this frame's lift (the drawn hands'
        /// 20 degree cap), as MetaHandVisual and the editor's debug hands do.
        Vector3 DrawnTip()
        {
            HandFrame f = _app.HandInput.Get(HandSide.Right);
            Vector3 tip = f.position[(int)HandJoint.IndexTip], pivot = f.position[ContactMath.PivotJoint[(int)Finger.Index]];
            float lift = _app.Contacts.Lift(HandSide.Right, Finger.Index);
            Vector3 n = _app.Contacts.LiftNormal(HandSide.Right, Finger.Index);
            if (lift > 0f && ContactMath.FingerRotation(pivot, tip, n, lift, ContactMath.DrawnThetaMax, out float theta, out Vector3 axis))
                tip = ContactMath.RotateAbout(tip, pivot, axis, theta);
            return tip;
        }

        float HeightOverTop(Vector3 world, DeckButton b) => _deck.Root.InverseTransformPoint(world).y - TopOf(b, _deck);

        [UnityTest]
        public IEnumerator AFingerPressingRecRestsOnThePegAndRecStillRecordsOnce()
        {
            Assert.That(_app.Get<IContactSurface>(), Is.Not.Null);
            bool registered = false;
            foreach (IContactSurface s in _app.GetAll<IContactSurface>()) if (ReferenceEquals(s, _deck)) registered = true;
            Assert.IsTrue(registered, "the deck is a contact surface, like the keys and the menu");
            Assert.That(_recorder.State, Is.EqualTo(RecorderState.Idle));

            PressInto(DeckButton.Rec, 0.015f);
            Assert.That(_recorder.State, Is.EqualTo(RecorderState.Recording), "REC pressed: the play logic reads the raw joints, as before");
            Assert.That(_deck.PegDepth(DeckButton.Rec), Is.GreaterThan(0.5f), "the peg is down");
            float lift = _app.Contacts.Lift(HandSide.Right, Finger.Index);
            float raw = HeightOverTop(_app.HandInput.Get(HandSide.Right).position[(int)HandJoint.IndexTip], DeckButton.Rec);
            float drawn = HeightOverTop(DrawnTip(), DeckButton.Rec);
            Debug.Log($"[DeckHands] REC: raw tip {raw * 1000f:F1} mm over the sunk peg top, drawn {drawn * 1000f:F1} mm, lift {lift * 1000f:F1} mm, radius {TipRadius * 1000f:F1} mm, blobs {_app.Contacts.ShadowCount}");
            Assert.That(raw, Is.LessThan(TipRadius - 0.006f), "the raw pad is well inside the peg (what the drawn hand showed before)");
            Assert.That(lift, Is.GreaterThan(0.005f), "the drawn finger is lifted onto the peg");
            Assert.That(drawn, Is.GreaterThan(TipRadius - 0.002f), "the drawn pad rests on the peg top: the whole finger shows");
            Assert.That(Vector3.Dot(_app.Contacts.LiftNormal(HandSide.Right, Finger.Index), _deck.Root.up), Is.GreaterThan(0.99f), "along the deck's up");
            Assert.That(_app.Contacts.ShadowCount, Is.GreaterThanOrEqualTo(2), "the tip and its distal joint shadow the deck");

            // Held there: REC is not pressed again (one press per entry), the finger stays on the peg.
            _boot.Step(Dt, 20);
            Assert.That(_recorder.State, Is.EqualTo(RecorderState.Recording));
            Assert.That(HeightOverTop(DrawnTip(), DeckButton.Rec), Is.GreaterThan(TipRadius - 0.002f));

            // The clamp off (the old deck): the drawn finger would be the raw one, inside the peg.
            _app.Contacts.ClampEnabled = false;
            _boot.Step(Dt, 2);
            Assert.That(_app.Contacts.Lift(HandSide.Right, Finger.Index), Is.EqualTo(0f));
            Assert.That(HeightOverTop(DrawnTip(), DeckButton.Rec), Is.LessThan(TipRadius - 0.006f), "without the clamp the fingertip is hidden in the deck");
            _app.Contacts.ClampEnabled = true;
            yield return null;
        }

        [UnityTest]
        public IEnumerator AFingerOnALoopStripCapAndOnTheBareTopIsLiftedToo()
        {
            // CLICK: a 6 mm lit cap on the loop strip's front row.
            bool click = _app.Get<ILooper>() is Looper l0 && l0.Metronome;
            PressInto(DeckButton.Click, 0.010f);
            float drawn = HeightOverTop(DrawnTip(), DeckButton.Click);
            Debug.Log($"[DeckHands] CLICK cap: drawn tip {drawn * 1000f:F1} mm over it, lift {_app.Contacts.Lift(HandSide.Right, Finger.Index) * 1000f:F1} mm");
            Assert.That(_app.Contacts.Lift(HandSide.Right, Finger.Index), Is.GreaterThan(0.004f));
            Assert.That(drawn, Is.GreaterThan(TipRadius - 0.002f), "the drawn pad rests on the cap");
            if (_app.Get<ILooper>() is Looper l1) Assert.That(l1.Metronome, Is.Not.EqualTo(click), "CLICK still toggles from the raw joints");

            // Lift off, then the bare ebony top between the transport row and the reels (no control there).
            _boot.Hands.SetPose(HandSide.Right, IndexAt(Over(DeckButton.Play, 0.08f)));
            _boot.Step(Dt, 30);
            Assert.That(_app.Contacts.Lift(HandSide.Right, Finger.Index), Is.EqualTo(0f), "released (25 ms) once the finger is up");
            float r = TipRadius;
            Vector3 bare = _deck.Root.TransformPoint(new Vector3(0.0f, TapeDeck.TopY + r - 0.010f, -0.02f));
            _boot.Hands.SetPose(HandSide.Right, IndexAt(bare + _deck.Root.up * 0.03f));
            _boot.Step(Dt, 2);
            _boot.Hands.SetPose(HandSide.Right, IndexAt(bare));
            _boot.Step(Dt, 4);
            float top = _deck.Root.InverseTransformPoint(DrawnTip()).y - TapeDeck.TopY;
            Debug.Log($"[DeckHands] bare top: drawn tip {top * 1000f:F1} mm over it");
            Assert.That(_app.Contacts.Lift(HandSide.Right, Finger.Index), Is.GreaterThan(0.005f));
            Assert.That(top, Is.GreaterThan(r - 0.002f), "the drawn pad rests on the deck top");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheDeckAnswersOnlyOverItsFootprintAndNotWhileTheTableIsHeld()
        {
            Transform d = _deck.Root;
            Vector3 overRec = d.TransformPoint(new Vector3(TapeDeck.ButtonX[0], TapeDeck.PegY + TapeDeck.PegH * 0.5f + 0.004f, TapeDeck.PegZ));
            Assert.IsTrue(_deck.QueryContact(overRec, 0.06f, 0.04f, out ContactHit hit));
            Assert.That(hit.Height, Is.EqualTo(0.004f).Within(1e-4f), "the peg's top, not the deck's");
            Assert.That(hit.Use, Is.EqualTo(ContactUse.Both));
            Assert.That(Vector3.Distance(hit.Point, overRec - d.up * 0.004f), Is.LessThan(1e-4f));
            Vector3 beside = d.TransformPoint(new Vector3(0.3f, 0.08f, 0f));
            Assert.IsFalse(_deck.QueryContact(beside, 0.06f, 0.04f, out _), "off the deck");
            Vector3 high = d.TransformPoint(new Vector3(0f, 0.2f, 0f));
            Assert.IsFalse(_deck.QueryContact(high, 0.06f, 0.04f, out _), "too far above");
            Vector3 tape = d.TransformPoint(new Vector3(TapeDeck.ReelLocal[0].x + 0.03f, TapeDeck.TapeTopY + 0.01f, TapeDeck.ReelLocal[0].z));
            Assert.IsTrue(_deck.QueryContact(tape, 0.06f, 0.04f, out hit));
            Assert.That(hit.Height, Is.EqualTo(0.01f).Within(1e-4f), "on the tape");
            var held = _deck.Held;
            _deck.Held = () => true;
            Assert.IsFalse(_deck.QueryContact(overRec, 0.06f, 0.04f, out _), "the table is being moved");
            _deck.Held = held;
            yield return null;
        }
    }
}
