// A recording brings its instruments, in mixed reality (2026-11-11 headset test, "Play in my room": a take of the piano and
// the drums played from One alone drums was silent on the piano), with the whole app in one of MRUK's recorded rooms:
//   - the cause: off the seat in mixed reality an instrument is hidden (its group inactive) and its sources sat under it, so
//     they were inactive too and nothing the take sent the piano was heard. Now a hidden instrument's sources wait on the
//     audio host where they were, active (heard, rendering on the audio thread), and go back under it when it shows;
//   - a piano + drums take brings both in front of the player (My set, sliding in), both sound, the piano's sources are back
//     on the piano; when it ends One alone drums comes back exactly and the piano is hidden (and still audible) again.
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Layout;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.MR
{
    public class MrPlaybackLayoutTests : MrTestRig
    {
        protected override bool UseContractLayout => false;
        protected override bool TableOnEnter => false;
        protected override void Prepare(MemorySettingsStore settings)
        {
            settings.SetInt(AppSettings.KeyTourDone, 1);
            settings.SetString(AppSettings.KeyLayoutMode, "single");
            settings.SetString(AppSettings.KeyCurrentInstrument, "drums");
        }

        static IEnumerator Seconds(float s)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < s) yield return null;
        }

        static readonly SourceId[] PianoSources = { SourceId.PianoLo, SourceId.PianoMid, SourceId.PianoHi };

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator ATakeBringsItsInstrumentsInMixedRealityAndAHiddenOneIsStillHeard()
        {
            var layout = App.Get<ILayoutService>() as LayoutModule;
            Assert.That(layout, Is.Not.Null, "the build's Layout module");
            yield return EnterRoom("MeshBedroom3");
            yield return WaitFor(() => !layout.AnyMoving && !layout.Settling, 5f);
            Assert.IsTrue(Mr.Active);
            Assert.That(layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(layout.Current, Is.EqualTo(InstrumentKind.Drums));
            var engine = App.Get<IAudioEngine>() as AudioEngine;
            var presence = App.Get<IAudioPresence>() as AudioPresence;
            Assert.That(engine, Is.Not.Null);
            Assert.That(presence, Is.Not.Null);
            Rig piano = Rig(RigId.Piano), drums = Rig(RigId.Drums);
            Assert.That(layout.PlaceOf(InstrumentKind.Piano), Is.EqualTo(InstrumentPlace.Hidden), "off the seat in mixed reality: hidden");
            Assert.IsFalse(piano.Group.gameObject.activeInHierarchy, "its group is inactive");
            yield return null;

            // 1. The hidden piano is still heard: its sources wait on the audio host, active, rendering.
            foreach (SourceId id in PianoSources)
            {
                SynthSource s = engine.Source(id);
                Debug.Log($"[PlayLayout] mixed reality, piano hidden: {id} active={s.gameObject.activeInHierarchy} under the piano={s.transform.IsChildOf(piano.Group)}");
                Assert.IsTrue(s.gameObject.activeInHierarchy, id + " is active (heard) while the piano is hidden");
                Assert.IsFalse(s.transform.IsChildOf(piano.Group), id + " waits on the audio host");
            }
            Assert.IsTrue(presence.IsParked(InstrumentKind.Piano, 1));
            SynthSource mid = engine.Source(SourceId.PianoMid);
            long b0 = mid.BlocksRendered;
            yield return WaitFor(() => mid.BlocksRendered > b0 + 2, 3f);
            Assert.That(mid.BlocksRendered, Is.GreaterThan(b0 + 2), "it renders on the audio thread");
            Assert.That(() => { for (int i = 0; i < 100; i++) presence.Tick(1f / 72f); },
                Is.Not.AllocatingGCMemory(), "keeping a hidden instrument audible allocates nothing per frame");

            // 2. A piano + drums take: both come in front of the player, both sound.
            Pose drumsOwn = new Pose(drums.Group.position, drums.Group.rotation);
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            var take = new List<TakeEvent>();
            int pianoNotes = 0, drumNotes = 0;
            for (float t = 0.3f; t < 4f; t += 0.4f)
            {
                TakeEvent on = TakeEvent.PianoOn(60 + pianoNotes % 12, 0.8f); on.t = t; take.Add(on); pianoNotes++;
                TakeEvent off = TakeEvent.PianoOff(on.midi); off.t = t + 0.3f; take.Add(off);
                TakeEvent d = TakeEvent.Drum("snare", 0.8f, 0.3f, "head", false); d.t = t + 0.05f; take.Add(d); drumNotes++;
            }
            take.Sort((a, b) => a.t.CompareTo(b.t));
            var rec = (Recorder)App.Get<IRecorder>();
            Assert.IsTrue(rec.PlayEvents(take, 4.4));
            Assert.IsTrue(layout.PlaybackStaged, "the take brought its instruments");
            Assert.That(layout.Mode, Is.EqualTo(LayoutMode.All));
            CollectionAssert.AreEquivalent(new[] { InstrumentKind.Piano, InstrumentKind.Drums }, new List<InstrumentKind>(layout.MySet));
            yield return WaitFor(() => !layout.AnyMoving, 5f);
            Assert.That(layout.PlaceOf(InstrumentKind.Piano), Is.EqualTo(InstrumentPlace.Seat), "the piano slid in");
            Assert.IsTrue(piano.Group.gameObject.activeInHierarchy);
            yield return null;
            foreach (SourceId id in PianoSources)
                Assert.IsTrue(engine.Source(id).transform.IsChildOf(piano.Group), id + " is back on the piano");
            Vector3 head = App.Head.position;
            Vector3 toPiano = piano.Group.position - head;
            toPiano.y = 0f;
            Assert.That(toPiano.magnitude, Is.LessThan(1.6f), "in front of the player");
            yield return WaitFor(() => rec.State == RecorderState.Idle, 10f);
            Assert.That(tap.Count(AudioCall.Piano), Is.EqualTo(pianoNotes), "every piano note sounded");
            Assert.That(tap.Count(AudioCall.Drum), Is.EqualTo(drumNotes), "every drum hit sounded");
            engine.TestTap = null;

            // 3. The end: One alone drums as it was; the piano hidden again, and audible.
            yield return WaitFor(() => !layout.PlaybackStaged, LayoutModule.StageEndGrace + 3f);
            yield return WaitFor(() => !layout.AnyMoving, 5f);
            yield return null;
            Assert.IsFalse(layout.PlaybackStaged);
            Assert.That(layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(layout.Current, Is.EqualTo(InstrumentKind.Drums));
            Assert.That(Vector3.Distance(drums.Group.position, drumsOwn.position), Is.LessThan(1e-3f), "the kit where it stood");
            Assert.That(Quaternion.Angle(drums.Group.rotation, drumsOwn.rotation), Is.LessThan(0.1f));
            Assert.That(layout.PlaceOf(InstrumentKind.Piano), Is.EqualTo(InstrumentPlace.Hidden));
            Assert.IsTrue(mid.gameObject.activeInHierarchy, "the hidden piano is still heard");
            Assert.That(App.Settings.Store.GetString(AppSettings.KeyLayoutMode, ""), Is.EqualTo("single"), "nothing of the stage was saved");
        }
    }
}
#endif
