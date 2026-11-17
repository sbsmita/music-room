// platform.md 12.1 DisplayedSilenceTests: a displayed instrument uses no CPU. With the default set at the seat, a stub on
// display is not present (never ticked), its unit idle-skips (IdleBlocks grows) and its sources pause after 1 s of silence;
// a playback dispatch's Wake resumes them at once, and a bring resumes them when it starts.
using System.Collections;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode
{
    public class DisplayedSilenceTests
    {
        TestBoot _boot;

        [UnityTearDown]
        public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); _boot = null; }

        static unsafe long IdleBlocks(SoundUnit u) => u.Slot->IdleBlocks;
        static unsafe int Work(SoundUnit u) => u.Slot->Work;

        [UnityTest]
        public IEnumerator ADisplayedStubIsSilentAndWakesForPlayback()
        {
            _boot = new TestBoot(headless: true, simulateHeadset: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            yield return _boot.Start();
            App app = App.Current;
            ILayoutService layout = app.Get<ILayoutService>();
            var engine = app.Get<IAudioEngine>() as AudioEngine;
            IAudioPresence presence = app.Get<IAudioPresence>();
            Assert.IsNotNull(engine); Assert.IsNotNull(presence);
            Assert.That(layout.MySet.ToArray(), Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp }), "the default set");
            Assert.That(layout.PlaceOf(InstrumentKind.Handpan), Is.EqualTo(InstrumentPlace.Display));
            IInstrument stub = app.Instruments.First(i => i.Kind == InstrumentKind.Handpan);
            Assert.IsFalse(stub.IsPresent, "a displayed instrument is not ticked (App.TickInstruments skips it)");

            SoundUnit unit = engine.Unit(UnitId.Handpan);
            Assert.IsNotNull(unit, "the stub's unit");
            long idle0 = IdleBlocks(unit);
            float t0 = Time.realtimeSinceStartup;
            while (!presence.Paused(InstrumentKind.Handpan) && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Assert.IsTrue(presence.Paused(InstrumentKind.Handpan), "its sources pause after 1 s of silence");
            Assert.That(IdleBlocks(unit), Is.GreaterThan(idle0), "its unit idle-skips its render");
            Assert.That(Work(unit), Is.EqualTo(0), "no work while silent");
            Assert.IsFalse(presence.Paused(InstrumentKind.Piano), "at-seat sources never pause");

            presence.Wake(InstrumentKind.Handpan);
            Assert.IsFalse(presence.Paused(InstrumentKind.Handpan), "a playback dispatch's Wake resumes them at once");
            IInstrumentPlayback pb = app.GetAll<IInstrumentPlayback>().First(p => p.Kind == InstrumentKind.Handpan);
            Assert.IsTrue(pb.Play(new TakeEvent { type = "handpan", i = 1, vel = 0.7f, lane = PlaybackLanes.Take }), "it plays back in place");
            yield return null;
            Assert.That(layout.PlaceOf(InstrumentKind.Handpan), Is.EqualTo(InstrumentPlace.Display), "a take never brings anything");
        }
    }
}
