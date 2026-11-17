// AudioPresence and the UnitGovernor in a running app (12-new-instruments/platform.md 10.1-10.4): the
// Audio module with one instrument (the handpan, its real module and unit) and a stand-in layout (registered first, so App.Get finds
// it) that keeps the handpan on display. A displayed instrument's sources pause once it has sounded nothing for 1 s, and
// play again on Wake (a playback dispatch ahead), at a bring (InstrumentMoveEvent Leaving), or as soon as its unit sounds;
// an at-seat instrument never pauses. The governor classes a displayed instrument's unit Tail, or Playback after a
// dispatch. (Every instrument plays in Meta's room: the classic sound only since 2026-11-10.)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Handpan;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Audio
{
    public class AudioPresenceTests
    {
        /// <summary>A layout that keeps the classic set at the seat and the handpan where the test says.</summary>
        sealed class StandInLayout : AppModule, ILayoutService
        {
            public InstrumentPlace HandpanPlace = InstrumentPlace.Display;
            public override int Order => 5;
            public override void Init(App app) => app.Register<ILayoutService>(this);
            public LayoutMode Mode => LayoutMode.All;
            public InstrumentKind Current => InstrumentKind.Piano;
            public bool IsTurning => false;
            public bool FirstArrangeDone => true;
            public SeatFrame Seat => default;
            public void Set(LayoutMode mode, InstrumentKind instrument) { }
            public void Next(int direction) { }
            public void ArrangeAroundPlayer(bool force, bool movePiano = true) { }
            public void PlaceAroundPiano() { }
            public void RestoreHomes() { }
            public Pose Anchor => Pose.identity;
            public bool IsInScene(InstrumentKind k) => PlaceOf(k) == InstrumentPlace.Seat;
            public InstrumentPlace PlaceOf(InstrumentKind k) => k == InstrumentKind.Handpan ? HandpanPlace : InstrumentPlace.Seat;
        }

        TestBoot _boot;
        StandInLayout _layout;
        AudioEngine _engine;
        AudioModule _module;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot();
            _layout = new StandInLayout();
            _boot.Options.ModuleFilter = t => t == typeof(AudioModule) || t == typeof(HandpanModule);
            _boot.Options.ExtraModules.Add(_layout);
            yield return _boot.Start();
            _engine = (AudioEngine)App.Current.Get<IAudioEngine>();
            _module = App.Current.Modules.OfType<AudioModule>().First();
            Assert.That(App.Current.Get<ILayoutService>(), Is.SameAs(_layout));
            Assert.That(App.Current.Registry.IsLoaded(InstrumentKind.Handpan), Is.True, "the handpan registered its rig");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        static IEnumerator Wait(float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        static IEnumerator Until(Func<bool> done, float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < timeout) yield return null;
        }

        AudioSource Handpan => _engine.Source(SourceId.Handpan).GetComponent<AudioSource>();
        static unsafe byte SlotClass(AudioGraphHost h, UnitId id) => h.Unit(id)->Class;
        static unsafe float SlotShare(AudioGraphHost h, UnitId id) => h.Unit(id)->Share;
        IHandpanSound Pan => (IHandpanSound)_engine.ActiveUnit(UnitId.Handpan);
        /// <summary>A short sound on the handpan's unit: a note, then 0.12 s later a palm resting on every field (the struck one
        /// and the ones it set ringing sympathetically stop within about 0.05 s), so it rings out as quickly as the stub's knock
        /// did; the palm stays (a lifted palm would let nothing ring again: it is silent).</summary>
        IEnumerator Note(int field)
        {
            Pan.Strike(field, 0.8f, 0.35f, 0.2f, HandpanTech.Tip, 0);
            yield return Wait(0.12f);
            for (int f = 0; f < 9; f++) Pan.Damp(f, 2, 1f, 0f, 0f, HandpanDampKind.Palm, 0);
        }

        [UnityTest]
        public IEnumerator ADisplayedInstrumentPausesAfterASecondOfSilenceAndWakesAhead()
        {
            var p = _module.Presence;
            Assert.That(App.Current.Get<IAudioPresence>(), Is.SameAs(p), "registered for the playback dispatch");
            Assert.That(Handpan.isActiveAndEnabled, Is.True, "the handpan's source follows its rig");
            yield return Until(() => p.Paused(InstrumentKind.Handpan), 3f);
            Assert.That(p.Paused(InstrumentKind.Handpan), Is.True, "displayed and silent for 1 s: paused");
            Assert.That(Handpan.isPlaying, Is.False, "AudioSource.Pause");
            var synth = _engine.Source(SourceId.Handpan);
            Assert.That(synth.PresencePaused, Is.True);
            long blocks = synth.BlocksRendered, skipped = synth.PausedBlocks;
            yield return Wait(0.3f);
            long called = synth.BlocksRendered - blocks, idle = synth.PausedBlocks - skipped;
            Debug.Log($"[PRESENCE] a paused source: Unity called its filter for {called} blocks in 0.3 s, the SynthSource skipped its work in {idle} " +
                      "(whether Meta's spatializer still runs for a paused source is UNVERIFIED: device item 53)");
            Assert.That(idle, Is.EqualTo(called).Within(1), "while paused and silent the source does no work of its own");
            Assert.That(_engine.Source(SourceId.Guide).BlocksRendered, Is.GreaterThan(0), "the Guide source never pauses: the graph keeps running");

            // Wake (the playback dispatch, 0.25 s or more ahead): at once, and held for a second
            p.Wake(InstrumentKind.Handpan);
            Assert.That(Handpan.isPlaying, Is.True, "Wake resumes at once");
            Assert.That(_engine.Source(SourceId.Handpan).PresencePaused, Is.False);
            Assert.That(p.Paused(InstrumentKind.Handpan), Is.False);
            yield return Wait(0.6f);
            Assert.That(p.Paused(InstrumentKind.Handpan), Is.False, "held awake after a Wake");
            yield return Until(() => p.Paused(InstrumentKind.Handpan), 3f);
            Assert.That(p.Paused(InstrumentKind.Handpan), Is.True, "and paused again once silent");

            // a note without a Wake (a late dispatch): the unit's output resumes the sources within a frame or two
            yield return Note(1);
            yield return Until(() => !p.Paused(InstrumentKind.Handpan), 1f);
            Assert.That(p.Paused(InstrumentKind.Handpan), Is.False, "sounding: resumed");
            Assert.That(Handpan.isPlaying, Is.True);
            yield return Until(() => p.Paused(InstrumentKind.Handpan), 5f);
            Assert.That(p.Paused(InstrumentKind.Handpan), Is.True, "rang out, silent for 1 s: paused again");

            // a bring glide starts: resumed at its start
            App.Current.Events.Publish(new InstrumentMoveEvent { Kind = InstrumentKind.Handpan, Phase = MovePhase.Leaving, Reason = BringReason.Menu });
            Assert.That(Handpan.isPlaying, Is.True, "InstrumentMoveEvent Leaving resumes the sources");
            // at the seat it never pauses
            _layout.HandpanPlace = InstrumentPlace.Seat;
            yield return Wait(1.6f);
            Assert.That(p.Paused(InstrumentKind.Handpan), Is.False, "at-seat sources never pause");
            Assert.That(Handpan.isPlaying, Is.True);
            Assert.That(p.Pauses, Is.GreaterThanOrEqualTo(3));
        }

        [UnityTest]
        public IEnumerator ASoundingDisplayedInstrumentIsClassedByTheGovernor()
        {
            Assert.IsNull(_engine.Unit(UnitId.Room), "no room unit");
            var gov = _module.Governor;
            Assert.That(gov, Is.Not.Null);
            int units = 0;
            foreach (var role in SoundUnits.LiveRoles) if (_engine.Unit(role) != null) units++;
            Assert.That(gov.Count, Is.EqualTo(units), "every unit is governed (the piano, drums and harp have none)");
            yield return Wait(0.2f);
            int idx = -1;
            for (int i = 0; i < gov.Count; i++) if (gov.Role(i) == UnitId.Handpan) idx = i;
            Assert.That(idx, Is.GreaterThanOrEqualTo(0));
            Assert.That(gov.ClassOf(idx), Is.EqualTo(UnitClass.Tail), "displayed: Tail");

            yield return Note(2);
            yield return Wait(0.3f);

            _module.Presence.Wake(InstrumentKind.Handpan);   // a dispatch: Playback for 2 s
            yield return null;
            yield return null;
            Assert.That(gov.ClassOf(idx), Is.EqualTo(UnitClass.Playback));
            Assert.That(SlotClass(_engine.Graph, UnitId.Handpan), Is.EqualTo(UnitClass.Playback), "the slot carries the class");
            _layout.HandpanPlace = InstrumentPlace.Seat;
            yield return null;
            yield return null;
            Assert.That(gov.ClassOf(idx), Is.EqualTo(UnitClass.AtSeat));
            Assert.That(SlotShare(_engine.Graph, UnitId.Handpan), Is.EqualTo(1f), "an idle editor: full shares");
        }
    }
}
