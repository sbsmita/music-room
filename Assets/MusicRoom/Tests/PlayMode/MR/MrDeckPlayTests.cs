// The deck's PLAY in mixed reality (2026-11-16 headset log: the quick show's saved loop "ended" 1 s after PLAY, twice;
// "few recordings i am unable to play. i click on it but nothing happens"). A playback brings its instruments in front of
// the player (Layout's stage); in mixed reality the side table is put at its new place at once (no glide), in the very
// frame the fingertip presses PLAY. The deck then sat somewhere else under the same fingertip, and a press that was never
// made (STOP) ended the playback it had just started. A fingertip resting on PLAY while the deck moves under it presses
// nothing more: the playback plays on.
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.MR
{
    public class MrDeckPlayTests : MrTestRig
    {
        protected override bool UseContractLayout => false;
        protected override bool TableOnEnter => false;
        protected override void Prepare(MemorySettingsStore settings)
        {
            settings.SetInt(AppSettings.KeyTourDone, 1);
            settings.SetString(AppSettings.KeyLayoutMode, "single");
            settings.SetString(AppSettings.KeyCurrentInstrument, "piano");
        }

        static HandPose IndexAt(Vector3 tip)
        {
            HandPose p = HandPose.OpenFacing(HandSide.Right, tip + Vector3.up * 0.15f, Vector3.down, Vector3.forward).WithPointing().WithIndexTipAt(tip);
            foreach (HandJoint j in new[] { HandJoint.ThumbTip, HandJoint.MiddleTip, HandJoint.RingTip, HandJoint.LittleTip }) p.Position[(int)j] += Vector3.up * 0.3f;
            return p;
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AFingertipRestingOnPlayWhileTheStageMovesTheDeckPressesNothingElse()
        {
            var layout = App.Get<ILayoutService>() as LayoutModule;
            Assert.That(layout, Is.Not.Null);
            yield return EnterRoom("MeshBedroom3");
            yield return WaitFor(() => !layout.AnyMoving && !layout.Settling, 5f);
            Assert.IsTrue(Mr.Active);
            Assert.That(layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(layout.Current, Is.EqualTo(InstrumentKind.Piano));
            LessonsModule lessons = null;
            foreach (IAppModule x in App.Modules) if (x is LessonsModule lm) lessons = lm;
            Assert.That(lessons, Is.Not.Null);
            TapeDeck deck = lessons.Deck;
            var rec = (Recorder)App.Get<IRecorder>();
            Assert.IsTrue(deck.IsPresent, "the deck is on the side table in mixed reality");

            // The deck's own take: the piano and the drums (as the quick show's loop), 4 s long.
            rec.Record();
            double t0 = App.AudioTime;
            for (float t = 0.2f; t < 4f; t += 0.4f)
            {
                rec.Add(TakeEvent.PianoOn(60, 0.7f), t0 + t);
                rec.Add(TakeEvent.PianoOff(60), t0 + t + 0.2);
                rec.Add(TakeEvent.Drum("snare", 0.7f, 0.3f, "head", false), t0 + t + 0.1);
            }
            rec.Stop();
            Assert.That(rec.Take, Is.Not.Null);
            Assert.That(rec.Take.Count, Is.GreaterThan(20));
            yield return null;

            // The player had moved the side table a little (in the log: Move instruments in mixed reality just before the
            // show): the playback's layout puts it at its place for My set, and in mixed reality that is at once.
            Rig table = Rig(RigId.Table);
            Assert.That(table, Is.Not.Null);
            Vector3 home = table.Group.position;
            table.Group.position = home + (deck.Root.TransformPoint(TapeDeck.ButtonLocal(DeckButton.Stop)) - deck.Root.TransformPoint(TapeDeck.ButtonLocal(DeckButton.Play)));
            yield return Frames(2);
            Vector3 deck0 = deck.Root.position;
            Quaternion deckRot0 = deck.Root.rotation;
            Vector3 l = TapeDeck.ButtonLocal(DeckButton.Play);
            Vector3 above = deck.Root.TransformPoint(new Vector3(l.x, 0.15f, l.z));
            Vector3 at = deck.Root.TransformPoint(new Vector3(l.x, 0.08f, l.z));
            int presses0 = deck.Presses;
            int starts = 0, ends = 0;
            System.Action<RecordingPlaybackEvent> onPlayback = e => { if (e.Started) starts++; else ends++; };
            App.Events.Subscribe(onPlayback);
            try
            {
                Fake.SetPose(HandSide.Right, IndexAt(above));
                yield return Frames(3);
                Fake.SetPose(HandSide.Right, IndexAt(at));   // the fingertip presses PLAY and rests there
                yield return Frames(20);
                float moved = Vector3.Distance(deck.Root.position, deck0);
                float turned = Quaternion.Angle(deck.Root.rotation, deckRot0);
                Vector3 local = deck.Root.InverseTransformPoint(at);
                Debug.Log($"[PlayFix] mixed reality: the stage moved the deck {moved:F3} m / {turned:F1} deg; the resting fingertip is now at deck-local ({local.x:F3}, {local.y:F3}, {local.z:F3}); presses {deck.Presses - presses0}, playback starts {starts} ends {ends}, recorder {rec.State}, staged {layout.PlaybackStaged}");
                Fake.SetPose(HandSide.Right, IndexAt(above));
                yield return Frames(3);
                Fake.SetPose(HandSide.Right, IndexAt(above + Vector3.up * 0.4f));
                yield return Frames(10);
                Assert.That(starts, Is.EqualTo(1), "PLAY started the take");
                Assert.That(deck.Presses - presses0, Is.EqualTo(1), "only PLAY was pressed");
                Assert.That(ends, Is.EqualTo(0), "the playback was not ended by a press nobody made");
                Assert.That(rec.State, Is.EqualTo(RecorderState.Playing), "the take plays on");
            }
            finally { App.Events.Unsubscribe(onPlayback); }
            rec.Stop();
        }
    }
}
#endif
