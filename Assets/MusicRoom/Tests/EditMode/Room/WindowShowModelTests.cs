// The snowy window's playback show, its numbers (Room/Decor/WindowShowModel.cs): nothing before a playback starts; an onset
// blows a gust (fast rise, louder = stronger, calm again) and lifts the northern lights (louder = brighter, the note's band
// flares and turns to its colour); the end lets everything fall to exactly 0 with the clocks back at 0; a note sent ahead
// waits; the palette stays in the night's hues; the flakes' swirl is exactly nothing at rest; a frame allocates nothing.
using MusicRoom.Core;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using UnityEngine.TestTools.Utils;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Room
{
    public class WindowShowModelTests
    {
        const float Dt = 1f / 72f;

        static PlaybackNoteEvent Piano(int midi, float vel, double when = 0) =>
            new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = vel, When = when, Lane = PlaybackLanes.Take, Event = TakeEvent.PianoOn(midi, vel) };

        static PlaybackNoteEvent Drum(string kind, float vel) =>
            new PlaybackNoteEvent { Kind = InstrumentKind.Drums, Role = EventRole.Onset, Level = vel, Lane = PlaybackLanes.Take, Event = TakeEvent.Drum(kind, vel, 0.3f, "head", false) };

        static void Run(WindowShowModel m, ref double now, float seconds)
        {
            int n = (int)System.Math.Ceiling(seconds / Dt);
            for (int i = 0; i < n; i++) { now += Dt; m.Tick(Dt, now); }
        }

        static void AssertRest(WindowShowModel m)
        {
            Assert.IsTrue(m.Resting, "resting");
            Assert.That(m.Swirl, Is.EqualTo(Vector4.zero), "no swirl");
            Assert.That(m.AuroraParams, Is.EqualTo(Vector4.zero), "no lights");
            Assert.That(m.SnowTint, Is.EqualTo(Vector4.zero), "no tint");
            for (int b = 0; b < WindowShowModel.Bands; b++) Assert.That(m.Band(b).w, Is.EqualTo(0f), "band " + b + " dark");
            Assert.That(m.Phase, Is.EqualTo(0f));
            Assert.That(m.AuroraTime, Is.EqualTo(0f));
        }

        [Test]
        public void NothingHappensWithoutAPlayback()
        {
            var m = new WindowShowModel();
            double now = 10;
            AssertRest(m);
            m.Note(Piano(60, 1f), now);   // a note outside a playback (live play never sends one; even so)
            Run(m, ref now, 1f);
            AssertRest(m);
            Assert.That(m.Notes, Is.EqualTo(0));
        }

        [Test]
        public void AnOnsetBlowsAGustThatCalmsAndLouderBlowsHarder()
        {
            var soft = new WindowShowModel();
            var loud = new WindowShowModel();
            double a = 10, b = 10;
            soft.Start(); loud.Start();
            soft.Note(Piano(60, 0.2f), a);
            loud.Note(Piano(60, 1f), b);
            Run(soft, ref a, 0.12f);
            Run(loud, ref b, 0.12f);
            Assert.That(soft.Gust, Is.GreaterThan(0.8f * (WindowShowModel.GustFloor + 0.65f * 0.2f)), "fast rise");
            Assert.That(loud.Gust, Is.GreaterThan(0.85f), "a loud note blows a full gust");
            Assert.That(loud.Gust, Is.GreaterThan(soft.Gust + 0.3f), "louder blows harder");
            float peak = loud.Gust;
            Run(loud, ref b, 1.5f);
            Assert.That(loud.Gust, Is.LessThan(0.3f * peak), "and calms");
            Assert.That(loud.Phase, Is.GreaterThan(0f), "the eddies turned");
            Run(loud, ref b, 6f);
            Assert.That(loud.Gust, Is.EqualTo(0f), "calm, exactly");
            Assert.That(loud.Swirl.x, Is.EqualTo(0f));
            Assert.IsFalse(loud.Resting, "the playback still runs: the lights wait for the next note");
        }

        [Test]
        public void TheLightsRiseLouderIsBrighterAndTheNotesBandFlaresInItsColour()
        {
            var soft = new WindowShowModel();
            var loud = new WindowShowModel();
            double a = 10, b = 10;
            soft.Start(); loud.Start();
            for (int i = 0; i < 8; i++)
            {
                soft.Note(Piano(96, 0.25f), a); loud.Note(Piano(96, 1f), b);   // a high note: the top curtain
                Run(soft, ref a, 0.25f); Run(loud, ref b, 0.25f);
            }
            Assert.That(soft.Aurora, Is.GreaterThan(0.4f), "the lights come up for soft playing too");
            Assert.That(loud.Aurora, Is.GreaterThan(soft.Aurora + 0.2f), "louder is brighter");
            Assert.That(loud.Band(2).w, Is.GreaterThan(loud.Band(0).w + 0.3f), "the high note's curtain flares");
            Color want = WindowShowModel.Palette(0.05f + 0.75f * ((96 - 21) / 87f)).linear;
            Vector4 c = loud.Band(2);
            Assert.That(new Vector3(c.x, c.y, c.z), Is.EqualTo(new Vector3(want.r, want.g, want.b)).Using(new Vector3EqualityComparer(0.02f)), "in the note's colour");
            Assert.That(loud.AuroraParams.y, Is.GreaterThan(1f), "the lights' clock runs");
            Assert.That(loud.SnowTint.y, Is.GreaterThan(0f), "the snow catches their light");
        }

        [Test]
        public void TheEndLetsEverythingFallToExactlyZero()
        {
            var m = new WindowShowModel();
            double now = 10;
            m.Start();
            for (int i = 0; i < 20; i++) { m.Note(Drum(i % 2 == 0 ? "kick" : "crash", 0.9f), now); Run(m, ref now, 0.2f); }
            Assert.That(m.Aurora, Is.GreaterThan(0.7f));
            Assert.That(m.Gust, Is.GreaterThan(0.3f));
            m.Stop();
            m.Note(Drum("snare", 1f), now);   // nothing counts after the end
            Run(m, ref now, 1f);
            Assert.That(m.Aurora, Is.GreaterThan(0f).And.LessThan(0.6f), "fading");
            Run(m, ref now, 8f);
            AssertRest(m);
            // a later playback starts from the same rest
            m.Start();
            m.Note(Piano(40, 0.7f), now);
            Run(m, ref now, 0.5f);
            Assert.That(m.Band(0).w, Is.GreaterThan(0.3f), "a low note: the low curtain");
        }

        [Test]
        public void ASilenceInThePlaybackLetsTheLightsDownAndANoteBringsThemBack()
        {
            var m = new WindowShowModel();
            double now = 10;
            m.Start();
            m.Note(Piano(60, 0.8f), now);
            Run(m, ref now, 1f);
            Assert.That(m.Aurora, Is.GreaterThan(0.4f));
            Run(m, ref now, WindowShowModel.Silence + 6f);
            Assert.That(m.Aurora, Is.EqualTo(0f), "a long silence: dark again");
            m.Note(Piano(60, 0.8f), now);
            Run(m, ref now, 1f);
            Assert.That(m.Aurora, Is.GreaterThan(0.4f), "and back with the next note");
        }

        [Test]
        public void ANoteSentAheadWaitsUntilItSounds()
        {
            var m = new WindowShowModel();
            double now = 10;
            m.Start();
            m.Note(Piano(60, 1f, when: now + 0.3), now);
            Assert.That(m.PendingCount, Is.EqualTo(1));
            Run(m, ref now, 0.2f);
            Assert.That(m.Gust, Is.EqualTo(0f), "not yet");
            Run(m, ref now, 0.25f);
            Assert.That(m.Gust, Is.GreaterThan(0.5f), "now");
            Assert.That(m.PendingCount, Is.EqualTo(0));
            m.Note(Piano(60, 1f, when: now + 0.3), now);
            m.Stop();
            Assert.That(m.PendingCount, Is.EqualTo(0), "the end drops what waits");
        }

        [Test]
        public void TheInstrumentAndThePitchPickTheCurtainAndTheShade()
        {
            WindowShowModel.Voice(Piano(30, 1f), out int lowBand, out float lowHue);
            WindowShowModel.Voice(Piano(100, 1f), out int highBand, out float highHue);
            Assert.That(lowBand, Is.EqualTo(0));
            Assert.That(highBand, Is.EqualTo(2));
            Assert.That(highHue, Is.GreaterThan(lowHue + 0.5f), "low notes green, high notes violet");
            WindowShowModel.Voice(Drum("kick", 1f), out int kick, out _);
            WindowShowModel.Voice(Drum("snare", 1f), out int snare, out _);
            WindowShowModel.Voice(Drum("crash", 1f), out int crash, out float crashHue);
            Assert.That(new[] { kick, snare, crash }, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(crashHue, Is.EqualTo(1f), "the crash: the touch of pink");
            var harp = new PlaybackNoteEvent { Kind = InstrumentKind.Harp, Role = EventRole.Onset, Level = 1f, Event = TakeEvent.Harp(21, 1f, 0.5f, 0.2f, "through") };
            WindowShowModel.Voice(harp, out int hb, out float hh);
            Assert.That(hb, Is.EqualTo(2));
            Assert.That(hh, Is.EqualTo(1f));
        }

        [Test]
        public void ThePaletteIsTheNights()
        {
            for (float t = 0f; t <= 1.0001f; t += 0.05f)
            {
                Color c = WindowShowModel.Palette(t);
                Color.RGBToHSV(c, out float h, out float s, out float v);
                float deg = h * 360f;
                Assert.That(deg >= 140f && deg <= 330f, Is.True, $"t {t:F2}: hue {deg:F0} is green .. teal .. violet .. pink, no warm colours");
                Assert.That(s, Is.GreaterThan(0.4f), "saturated");
                Assert.That(v, Is.GreaterThan(0.85f), "bright light");
            }
        }

        [Test]
        public void TheSwirlIsExactlyNothingAtRestAndBoundedInAGust()
        {
            var seed = new Vector4(0.3f, 0.8f, 0.4f, 0.6f);
            var xy = new Vector2(0.4f, -0.2f);
            Assert.That(WindowShowModel.SwirlOffset(xy, 3f, seed, Vector4.zero), Is.EqualTo(xy), "at rest: the plain snowfall");
            var m = new WindowShowModel();
            double now = 10;
            m.Start();
            m.Note(Piano(60, 1f), now);
            float most = 0f;
            for (int i = 0; i < 200; i++)
            {
                now += Dt; m.Tick(Dt, now);
                for (float d = 0.1f; d < 15f; d += 2f)
                    most = Mathf.Max(most, (WindowShowModel.SwirlOffset(xy, d, seed, m.Swirl) - xy).magnitude);
            }
            Assert.That(most, Is.GreaterThan(0.1f), "a gust moves the flakes visibly");
            Assert.That(most, Is.LessThan(2.5f), "but they stay in the field");
        }

        [Test]
        public void AFrameAllocatesNothing()
        {
            var m = new WindowShowModel();
            double now = 10;
            m.Start();
            var on = Piano(64, 0.8f);
            var drum = Drum("snare", 0.9f);
            Vector4 sink = default;
            // warm every path the measured frames take (first uses, not a frame's cost)
            for (int i = 0; i < 100; i++)
            {
                m.Note(on, now);
                if (i % 7 == 0) { drum.When = now + 0.05; m.Note(drum, now); }
                now += Dt;
                m.Tick(Dt, now);
                sink += m.Swirl + m.AuroraParams + m.Band(0) + m.Band(1) + m.Band(2) + m.SnowTint;
            }
            Assert.That(() =>
            {
                for (int i = 0; i < 500; i++)
                {
                    if (i % 5 == 0) m.Note(on, now);
                    if (i % 7 == 0) { drum.When = now + 0.05; m.Note(drum, now); }
                    now += Dt;
                    m.Tick(Dt, now);
                    sink += m.Swirl + m.AuroraParams + m.Band(0) + m.Band(1) + m.Band(2) + m.SnowTint;
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.That(sink.x, Is.GreaterThan(0f));
        }
    }
}
