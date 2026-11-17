// AudioSchedule, the ambient "schedule at" scope (ENGINE-API.md section 10): its stamping rules, nesting and reset;
// today's synth (a drum recipe staged through SynthKit) and a sound unit (the Example unit in the piano role, through
// SoundUnit.Stage and the graph) both start on the scope's exact sample when rendered offline; the Ui source (Sunny,
// the metronome) is never stamped; and stamping allocates nothing.
using System;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Audio.Synths;
using NUnit.Framework;

namespace MusicRoom.Tests.Audio
{
    public class ScheduleScopeTests
    {
        [TearDown]
        public void TearDown() => AudioSchedule.Reset();

        [Test]
        public void StampRulesNestingAndReset()
        {
            Assert.IsFalse(AudioSchedule.Active);
            Assert.That(AudioSchedule.Stamp(0), Is.EqualTo(0.0), "outside a scope: unchanged");
            using (AudioSchedule.Begin(10.5))
            {
                Assert.IsTrue(AudioSchedule.Active);
                Assert.That(AudioSchedule.Stamp(0), Is.EqualTo(10.5), "ASAP -> the scope's time");
                Assert.That(AudioSchedule.Stamp(-0.004), Is.EqualTo(10.504).Within(1e-12), "relative -> after the scope's time");
                Assert.That(AudioSchedule.Stamp(3.25), Is.EqualTo(3.25), "an absolute time is kept");
                using (AudioSchedule.Begin(20.0)) Assert.That(AudioSchedule.Stamp(0), Is.EqualTo(20.0), "the inner scope wins");
                Assert.That(AudioSchedule.At, Is.EqualTo(10.5), "and the outer one comes back");
            }
            Assert.IsFalse(AudioSchedule.Active);
            using (AudioSchedule.Begin(0)) Assert.IsFalse(AudioSchedule.Active, "a scope at 0 changes nothing");
            AudioSchedule.Begin(5.0);
            AudioSchedule.Reset();
            Assert.IsFalse(AudioSchedule.Active, "Reset closes a scope left open");
        }

        static int FirstSound(float[] y, double threshold = 1e-9)
        {
            for (int i = 0; i < y.Length; i++) if (Math.Abs(y[i]) > threshold) return i;
            return -1;
        }

        [Test]
        public void TodaysDrumStartsOnTheScopesSample()
        {
            using var e = new OfflineEngine(roles: null, factory: null, sources: true);
            var kit = new SynthKit(e.Sources, 1) { Tap = new AudioTestTap() };
            var drums = new DrumSynth(kit);
            const long target = 256 * 3 + 77;            // inside block 3, the hit staged before block 0
            float[][] y = e.RenderSources(256 * 8, new[] { SourceId.Snare, SourceId.Ui }, t =>
            {
                if (t != 0) return;
                using (AudioSchedule.Begin(target / (double)OfflineEngine.Sr))
                {
                    drums.Hit(DrumKind.Snare, 0.8f, DrumStrike.Default, 0);   // the instrument asked for ASAP
                    kit.Noise(kit.S(SourceId.Ui), 0, hp: 2000, peak: 0.05f, decay: 0.01f);
                    kit.Flush();
                }
            });
            int first = FirstSound(y[0]);
            Assert.That(first, Is.GreaterThanOrEqualTo((int)target), "nothing before the scheduled sample");
            Assert.That(first, Is.LessThan((int)target + 8), $"the snare starts on sample {target} (first sound at {first})");
            foreach (var c in kit.Tap.Commands)
            {
                if (c.Source == SourceId.Ui) Assert.That(c.Command.When, Is.EqualTo(0.0), "the Ui source is never stamped");
                else Assert.That(c.Command.When, Is.GreaterThanOrEqualTo(target / (double)OfflineEngine.Sr - 1e-9), $"{c.Source} stamped");
            }
            Assert.That(FirstSound(y[1]), Is.LessThan(256), "the Ui noise played at once");
        }

        [Test]
        public unsafe void AUnitsNoteStartsOnTheScopesSample()
        {
            ExampleUnit unit = null;
            using var e = new OfflineEngine(new[] { UnitId.Piano }, (role, ctx) => unit = new ExampleUnit(ctx, UnitId.Piano), sources: false);
            const long target = 256 * 5 + 131;
            float[][] y = e.RenderGraph(256 * 8, new[] { SourceId.PianoMid }, t =>
            {
                if (t != 0) return;
                using (AudioSchedule.Begin(target / (double)OfflineEngine.Sr)) unit.Piano(60, 0.8f, 0);
            });
            Assert.That(unit.Notes, Is.EqualTo(1));
            Assert.That(unit.State->LastOffset, Is.EqualTo((int)(target % 256)), "the event lands at its offset in block 5");
            int first = FirstSound(y[0]);
            Assert.That(first, Is.EqualTo((int)target).Or.EqualTo((int)target + 1), $"the note sounds from sample {target} (first {first})");
        }

        [Test]
        public void StampingAllocatesNothing()
        {
            double sum = 0;
            void Work()
            {
                for (int i = 0; i < 1000; i++)
                    using (AudioSchedule.Begin(1.0 + i)) sum += AudioSchedule.Stamp(0) + AudioSchedule.Stamp(-0.001);
            }
            Work();
            Assert.That(() => Work(), UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(UnityEngine.TestTools.Constraints.Is.Not));
            Assert.That(sum, Is.GreaterThan(0));
        }

        [Test]
        public void SecondsAndSamplesRoundTripLateInALongSession()
        {
            // The looper turns its sample grid into seconds (samples / sr) and the graph back (round(When x sr)): exact for
            // every sample up to days of session time, so pass k of a loop always lands on anchor + k x pass.
            const int Sr = OfflineEngine.Sr;
            var rng = new System.Random(11);
            long pass = (long)Math.Round(16 * 60.0 / 70.0 * Sr);   // the longest loop: 4 bars at 70 BPM
            for (int i = 0; i < 20000; i++)
            {
                long anchor = (long)(rng.NextDouble() * 3.0 * 86400 * Sr);   // up to 3 days of dspTime
                long k = rng.Next(0, 5000);
                long sample = anchor + k * pass + rng.Next(0, (int)pass);
                double when = sample / (double)Sr;
                using (AudioSchedule.Begin(when))
                {
                    double stamped = AudioSchedule.Stamp(0);
                    Assert.That((long)Math.Round(stamped * Sr), Is.EqualTo(sample));
                    Assert.That((long)Math.Round(AudioSchedule.Stamp(-0.004) * Sr), Is.EqualTo(sample + 192), "a relative When stays sample-exact");
                }
            }
        }
    }
}
