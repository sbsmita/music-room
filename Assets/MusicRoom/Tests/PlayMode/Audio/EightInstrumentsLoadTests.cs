// All the instruments at their worst at once (seven since 2026-11-10, when one was retired; the class keeps its name;
// 12-new-instruments/platform.md 11 and 12.1; category "Performance": Tools/unity.sh test-perf). Offline on the main
// thread (best of 3, with the UnitGovernor stepping once a second on the measured p99 under the busy-worst classes): the
// unit-only roles' real units at their declared worst (the bowls, handpan, marimba and hand drums through IUnitWorstCase;
// any role still a phase-0 stub knocks all six voices every 120 ms) in one graph. Live on the booted app's audio thread
// with the app's own governor: the same units plus the classic three on today's synths through IAudioEngine (two palms,
// all eight pieces every 120 ms, a 22-string glissando every 4 blocks). The governor keeps p99 at or under 2.1 ms.
// (Until 2026-11-11 the offline half also drove the upgraded piano, drum, harp and room units; they were deleted, the
// classic sound being the only one.)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using NUnit.Framework;
using Unity.Burst;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.PlayMode.Audio
{
    [Category("Performance")]
    public unsafe class EightInstrumentsLoadTests
    {
        const int Sr = 48000, B = 256;
        const double P99LimitMicros = 2100;
        static readonly UnitId[] Stubs = { UnitId.Handpan, UnitId.HandDrums, UnitId.Marimba, UnitId.Bowls };
        static readonly DrumKind[] Pieces = { DrumKind.Kick, DrumKind.Snare, DrumKind.Hat, DrumKind.Tom1, DrumKind.Tom2, DrumKind.Floor, DrumKind.Crash, DrumKind.Ride };
        static readonly DrumZone[] Zones = { DrumZone.Head, DrumZone.Head, DrumZone.Top, DrumZone.Head, DrumZone.Head, DrumZone.Head, DrumZone.Edge, DrumZone.Bow };
        TestBoot _boot;

        [UnityTearDown]
        public IEnumerator TearDown() { SoundUnits.Override = null; if (_boot != null) yield return _boot.Stop(); _boot = null; }

        static int Every(double seconds) => Math.Max(1, (int)Math.Round(seconds * Sr / B));

        /// <summary>Block b's events for the unit-only roles (the app's only units).</summary>
        static void DriveUnits(long b, Func<double, double> W, SoundUnit[] knocks)
        {
            if (b % Every(0.12) == 2)
                foreach (var u in knocks)
                    if (u is KnockUnit k) for (int v = 0; v < KnockState.Voices; v++) k.Knock(v, 0.9f, W(v * 0.0003));
            // any unit's declared worst case through the generic hook (the bowls: three rubbed bowls, restruck every 0.5 s;
            // the handpan, the marimba and the hand drums: their own live maxima)
            foreach (var u in knocks)
                if (u is IUnitWorstCase w) w.DriveWorst(b, W(0));
        }

        /// <summary>The classic three's load of Drive through IAudioEngine (today's synths), as soon as possible.</summary>
        static void DriveClassic(long b, IAudioEngine e, VoiceHandle[] harpVoices)
        {
            if (b % Every(0.35) == 0)
            {
                int o = (int)(b / Every(0.35) % 3) * 3;
                for (int k = 0; k < 9; k++) { e.Piano(24 + o + k, 1f); e.Piano(45 + o + k, 1f); }
            }
            if (b % Every(0.12) == 0)
                for (int p = 0; p < Pieces.Length; p++) e.Drum(Pieces[p], 0.85f, new DrumStrike { Pos = 0.3f, Zone = Zones[p] });
            if (b % 4 == 0)
                for (int s = 0; s < 22; s++)
                    harpVoices[s] = e.Pluck(s, 0.8f, 0, new PluckOptions { U = 0.36f, Speed = 1f, Mode = PluckMode.Across, Ud = 1f, Uz = 0.2f });
            if (b % Every(0.2) == 1)
                for (int s = 8; s < 12; s++) e.Damp(harpVoices[s], 4);
        }

        /// <summary>The busy-worst classes (platform.md 10.4): the drums live, the piano and three of the new on loops, the
        /// rest at the seat or ringing on display.</summary>
        sealed class BusyWorst : IInstrumentActivity
        {
            public InstrumentPlace PlaceOf(InstrumentKind k) => k == InstrumentKind.Drums || k == InstrumentKind.Harp ? InstrumentPlace.Seat : InstrumentPlace.Display;
            public float SecondsSinceSounded(InstrumentKind k) => 0f;
            public float SecondsSinceLive(InstrumentKind k) => k == InstrumentKind.Drums ? 0f : float.MaxValue;
            public float SecondsSincePlayback(InstrumentKind k) => k == InstrumentKind.Piano || k == InstrumentKind.Marimba || k == InstrumentKind.Bowls || k == InstrumentKind.Handpan ? 0f : float.MaxValue;
        }

        static (double avg, double p99, double max, double share, int steps) Run(int blocks)
        {
            using var host = new AudioGraphHost(GraphConfig.QuestDefault(Sr)) { AdaptiveBudget = false };
            long clock = 0;
            var ctx = new SoundContext(host, null, Sr, 1, () => clock / (double)Sr, true);
            SoundUnits.Override = null;
            var units = new List<SoundUnit>();
            foreach (var role in SoundUnits.Roles) { var u = SoundUnits.Create(role, ctx); Assert.IsNotNull(u, role + " has its unit"); units.Add(u); }
            var knocks = new SoundUnit[Stubs.Length];
            for (int i = 0; i < Stubs.Length; i++) knocks[i] = units.Find(u => u.Id == Stubs[i]);
            var gov = new UnitGovernor(host);
            gov.Bind(InstrumentRegistry.Discover());
            var act = new BusyWorst();
            var t = new double[blocks];
            var window = new List<double>(256);
            int second = Every(1.0);
            for (long b = -64; b < blocks; b++)
            {
                long blk = b + 64;
                double when0 = clock / (double)Sr;
                DriveUnits(blk, dt => when0 + dt, knocks);
                long t0 = Stopwatch.GetTimestamp();
                host.RenderOffline(clock, B);
                double us = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;
                if (b >= 0)
                {
                    t[b] = us;
                    window.Add(us);
                    if (window.Count == second)
                    {
                        window.Sort();
                        gov.StepNow(window[(int)(0.99 * (window.Count - 1))] / 1000.0, null, act);
                        window.Clear();
                    }
                }
                clock += B;
            }
            double shareMin = 1;
            for (int i = 0; i < gov.Count; i++) shareMin = Math.Min(shareMin, gov.ShareOf(i));
            int steps = gov.Changes;
            foreach (var u in units) u.Dispose();
            Array.Sort(t);
            double sum = 0;
            foreach (double v in t) sum += v;
            return (sum / blocks, t[(int)(0.99 * (blocks - 1))], t[blocks - 1], shareMin, steps);
        }

        [Test]
        public void AllEightUnitsAtTheirWorstInOneGraph()
        {
            Run(200);   // warm-up
            var best = (avg: double.MaxValue, p99: 0.0, max: 0.0, share: 1.0, steps: 0);
            for (int r = 0; r < 3; r++)
            {
                var x = Run(1500);
                if (x.avg < best.avg) best = x;
            }
            Debug.Log($"[PERF] all units (burst={BurstCompiler.IsEnabled}, editor, offline, best of 3; the unit-only roles' real units " +
                      $"at their declared worst): bowls, handpan, marimba, hand drums worst cases, per 256-frame block: avg {best.avg:F1} us, p99 {best.p99:F1} us, worst {best.max:F1} us; " +
                      $"governor (busy-worst classes) {best.steps} share changes, lowest share {best.share:F2}; limit p99 {P99LimitMicros / 1000:F1} ms");
            if (BurstCompiler.IsEnabled) Assert.That(best.p99, Is.LessThanOrEqualTo(P99LimitMicros), "p99 within the governor's 2.1 ms");
        }

        [UnityTest]
        public IEnumerator AllEightUnitsLiveOnTheAudioThreadWithTheGovernor()
        {
            _boot = new TestBoot();
            _boot.Options.ModuleFilter = ty => ty == typeof(AudioModule);
            yield return _boot.Start();
            var engine = (AudioEngine)App.Current.Get<IAudioEngine>();
            var module = App.Current.Get<AudioModule>();
            // the app's sound: the piano, drums and harp on today's synths (no unit, no room unit), the unit-only roles' units
            Assert.IsNull(engine.Unit(UnitId.Piano)); Assert.IsNull(engine.Unit(UnitId.Room));
            float t0 = Time.realtimeSinceStartup;
            while (engine.RenderSeconds < 0 && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            Assert.That(engine.RenderSeconds, Is.GreaterThanOrEqualTo(0.0), "today's piano and harp buffers rendered");
            IAudioEngine api = engine;
            var knocks = new SoundUnit[Stubs.Length];
            for (int i = 0; i < Stubs.Length; i++) { knocks[i] = engine.ActiveUnit(Stubs[i]); Assert.IsNotNull(knocks[i], Stubs[i] + " plays (unit-only: never classic)"); }
            var voices = new VoiceHandle[22];
            float start = Time.realtimeSinceStartup;
            long lastBlock = -1;
            bool reset = false;
            while (Time.realtimeSinceStartup - start < 6f)
            {
                if (!reset && Time.realtimeSinceStartup - start > 1f) { engine.Graph.Load.ResetPeaks(); reset = true; }
                long blk = (long)(engine.Now * Sr / B);
                for (long b = Math.Max(lastBlock + 1, blk - 8); b <= blk; b++) { DriveClassic(b, api, voices); DriveUnits(b, _ => 0, knocks); }
                lastBlock = blk;
                yield return null;
            }
            var g = engine.Graph.Load.Read();
            var gov = module.Governor;
            double shareMin = 1;
            for (int i = 0; i < gov.Count; i++) shareMin = Math.Min(shareMin, gov.ShareOf(i));
            int work = 0;
            foreach (var k in knocks) work += k.Work;
            int units = 0;
            foreach (var role in SoundUnits.LiveRoles) if (engine.Unit(role) != null) units++;
            Debug.Log($"[PERF] all instruments live (editor audio thread; the classic three on today's synths at the offline test's load, the unit-only roles' real units at their worst): " +
                      $"graph block avg {g.AvgMicros:F1} us p99 {g.P99Micros:F0} max {g.MaxMicros:F1} over {g.Blocks} blocks, over budget {g.OverBudget}; " +
                      $"synth sources avg {engine.SumAvgBlockMicros:F0} us, worst {engine.SumMaxBlockMicros:F0} us, stolen {engine.TotalStolenVoices}, dropped {engine.TotalDroppedCommands}; " +
                      $"governor {gov.Count} units, {gov.Steps} steps, last p99 {gov.LastP99Ms:F3} ms, lowest share {shareMin:F2}; new roles' work {work}");
            Assert.That(g.Blocks, Is.GreaterThan(100));
            Assert.That(gov.Count, Is.EqualTo(units), "every unit is governed (the classic three have none)");
            Assert.That(gov.Steps, Is.GreaterThan(2), "the governor stepped once a second");
            Assert.That(g.P99Micros, Is.LessThanOrEqualTo(P99LimitMicros), "the graph block's p99 within 2.1 ms");
        }
    }
}
