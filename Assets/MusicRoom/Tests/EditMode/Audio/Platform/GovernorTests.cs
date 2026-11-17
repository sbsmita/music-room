// The unit governor (12-new-instruments/platform.md 10.4) against the platform reference's vectors (budgets.governor, copied
// to Tests/EditMode/Layout/Reference~/platform.vectors.json): the reference's two scenes (make-vectors.mjs: busy worst, the
// drums played live at their worst with three loops and the harp at the seat; busy typical, a drum groove with three
// typical loops) replayed with the reference's cost model (20 % fixed + 80 % x share, p99 = 1.15 x the average, the scene's
// overhead and room) through UnitGovernor.Step, the decision the live path takes once a second. Then the live path's
// classes and slots.
using System;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Audio.Synths;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Audio
{
    public unsafe class GovernorTests
    {
        static object s_vectors;

        static object Vectors()
        {
            if (s_vectors != null) return s_vectors;
            string path = Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Layout/Reference~/platform.vectors.json");
            return s_vectors = MiniJson.Parse(File.ReadAllText(path));
        }

        static object At(object o, params string[] keys) { foreach (string k in keys) o = MiniJson.Get(o, k); return o; }
        static double Num(object o, params string[] keys) => (double)At(o, keys);

        /// <summary>A reference scene: (unit id, class, the vectors' ms for it).</summary>
        static void Scene(string name, (string id, byte cls)[] units, bool worst, out byte[] cls, out double[] ms, out double fixedMs)
        {
            object v = Vectors();
            cls = new byte[units.Length];
            ms = new double[units.Length];
            for (int i = 0; i < units.Length; i++)
            {
                cls[i] = units[i].cls;
                ms[i] = Num(v, "budgets", "units", units[i].id, worst ? "audioWorst" : "audioTyp");
            }
            fixedMs = Num(v, "budgets", "scene", "overhead") + Num(v, "budgets", "scene", "roomAudio3") + Num(v, "budgets", "scene", "roomAudioPerGroup");
            Assert.That(Num(v, "budgets", "scene", "governorHigh"), Is.EqualTo(UnitGovernor.HighMs), name + ": the high mark");
            Assert.That(Num(v, "budgets", "scene", "governorLow"), Is.EqualTo(UnitGovernor.LowMs), name + ": the low mark");
        }

        static void Replay(string name, (string id, byte cls)[] units, bool worst)
        {
            Scene(name, units, worst, out byte[] cls, out double[] ms, out double fixedMs);
            var share = new double[units.Length];
            double[] log = UnitGovernor.ModelRun(fixedMs, units.Length, cls, ms, share, 30);
            object want = At(Vectors(), "budgets", "governor", name);
            var wantLog = (List<object>)MiniJson.Get(want, "log");
            Assert.That(log.Length, Is.EqualTo(wantLog.Count));
            for (int s = 0; s < log.Length; s++)
                Assert.That(Math.Round(log[s], 4), Is.EqualTo((double)wantLog[s]).Within(1e-9), $"{name}: p99 at step {s}");
            for (int i = 0; i < units.Length; i++)
                Assert.That(share[i], Is.EqualTo(Num(want, "share", units[i].id)).Within(1e-9), $"{name}: {units[i].id}'s share");
            double p99 = Math.Round(UnitGovernor.ModelAverageMs(fixedMs, units.Length, ms, share) * UnitGovernor.ModelP99Scale, 4);
            Assert.That(p99, Is.EqualTo(Num(want, "p99")).Within(1e-9), name + ": the settled p99");
            Debug.Log($"[GOVERNOR] {name}: p99 {log[0]:F4} -> {p99:F4} ms ({p99 / 5.3333 * 100:F0} % of the block), shares {string.Join(", ", Array.ConvertAll(units, u => u.id))} = {string.Join(", ", share)}");
        }

        [Test]
        public void BusyWorstSettlesAtTheVectorsShares()
        {
            // make-vectors.mjs: drums Focus (live), piano / marimba / bowls Playback (loops), harp AtSeat, each at its worst
            Replay("busyWorst", new[]
            {
                ("drums", UnitClass.Focus), ("piano", UnitClass.Playback), ("marimba", UnitClass.Playback), ("bowls", UnitClass.Playback), ("harp", UnitClass.AtSeat),
            }, worst: true);
        }

        [Test]
        public void BusyTypicalIsLeftAlone()
        {
            Replay("busyTypical", new[]
            {
                ("drums", UnitClass.Focus), ("piano", UnitClass.Playback), ("marimba", UnitClass.Playback), ("bowls", UnitClass.Playback),
            }, worst: false);
        }

        [Test]
        public void TheStepDropsTheLowestClassFirstAndRaisesTheHighestFirst()
        {
            byte[] cls = { UnitClass.Focus, UnitClass.AtSeat, UnitClass.Playback, UnitClass.Tail, UnitClass.Tail };
            double[] share = { 1, 1, 1, 1, 1 };
            Assert.That(UnitGovernor.Step(1.7, 5, cls, share), Is.True);
            Assert.That(share, Is.EqualTo(new[] { 1, 1, 1, 0.85, 0.85 }), "Tail first, all its units together");
            for (int s = 0; s < 3; s++) UnitGovernor.Step(1.7, 5, cls, share);
            Assert.That(share, Is.EqualTo(new[] { 1, 1, 1, 0.4, 0.4 }), "down to the floor (0.85, 0.7, 0.55, 0.4)");
            UnitGovernor.Step(1.7, 5, cls, share);
            Assert.That(share, Is.EqualTo(new[] { 1, 1, 0.85, 0.4, 0.4 }), "then Playback");
            for (int s = 0; s < 10; s++) UnitGovernor.Step(2.5, 5, cls, share);
            Assert.That(share[0], Is.EqualTo(1.0), "Focus never drops");
            Assert.That(share[1], Is.EqualTo(0.4), "AtSeat last");
            Assert.That(UnitGovernor.Step(2.5, 5, cls, share), Is.False, "everything at the floor");
            Assert.That(UnitGovernor.Step(1.3, 5, cls, share), Is.False, "between the marks: no change");
            UnitGovernor.Step(0.9, 5, cls, share);
            Assert.That(share, Is.EqualTo(new[] { 1, 0.45, 0.4, 0.4, 0.4 }), "the highest class below 1 gains first (AtSeat)");
        }

        // ------------------------------------------------------------------ the live path

        sealed class Activity : IInstrumentActivity
        {
            public readonly Dictionary<InstrumentKind, InstrumentPlace> Place = new Dictionary<InstrumentKind, InstrumentPlace>();
            public readonly Dictionary<InstrumentKind, float> Live = new Dictionary<InstrumentKind, float>(), Playback = new Dictionary<InstrumentKind, float>();
            public InstrumentPlace PlaceOf(InstrumentKind k) => Place.TryGetValue(k, out var p) ? p : InstrumentPlace.Seat;
            public float SecondsSinceSounded(InstrumentKind k) => float.MaxValue;
            public float SecondsSinceLive(InstrumentKind k) => Live.TryGetValue(k, out var s) ? s : float.MaxValue;
            public float SecondsSincePlayback(InstrumentKind k) => Playback.TryGetValue(k, out var s) ? s : float.MaxValue;
        }

        [Test]
        public void TheLivePathClassesTheUnitsFromTheRegistryAndWritesTheirSlots()
        {
            using var e = new OfflineEngine(new[] { UnitId.Handpan, UnitId.Marimba, UnitId.Bowls, UnitId.HandDrums }, null, sources: false);
            var reg = InstrumentRegistry.Discover();
            var gov = new UnitGovernor(e.Host);
            gov.Bind(reg);
            Assert.That(gov.Count, Is.EqualTo(4), "the four roles with a unit in this graph");
            var act = new Activity();
            act.Live[InstrumentKind.Marimba] = 0.5f;                                   // played live just now: Focus
            act.Place[InstrumentKind.Handpan] = InstrumentPlace.Display;
            act.Playback[InstrumentKind.Handpan] = 0.3f;                               // a loop plays it: Playback
            act.Place[InstrumentKind.Bowls] = InstrumentPlace.Display;                 // displayed, no dispatch: Tail
            Assert.That(UnitGovernor.Classify(InstrumentKind.Marimba, null, act), Is.EqualTo(UnitClass.Focus));
            Assert.That(UnitGovernor.Classify(InstrumentKind.HandDrums, null, act), Is.EqualTo(UnitClass.AtSeat));
            Assert.That(UnitGovernor.Classify(InstrumentKind.Handpan, null, act), Is.EqualTo(UnitClass.Playback));
            Assert.That(UnitGovernor.Classify(InstrumentKind.Bowls, null, act), Is.EqualTo(UnitClass.Tail));
            act.Place[InstrumentKind.Bowls] = InstrumentPlace.Gliding;
            Assert.That(UnitGovernor.Classify(InstrumentKind.Bowls, null, act), Is.EqualTo(UnitClass.AtSeat), "gliding counts as at the seat");
            act.Place[InstrumentKind.Bowls] = InstrumentPlace.Display;

            for (int s = 0; s < 8; s++) gov.StepNow(2.4, null, act);   // a heavy second, eight times
            Assert.That(e.Host.Unit(UnitId.Marimba)->Share, Is.EqualTo(1f), "Focus keeps its full share");
            Assert.That(e.Host.Unit(UnitId.Marimba)->Class, Is.EqualTo(UnitClass.Focus));
            Assert.That(e.Host.Unit(UnitId.Bowls)->Share, Is.EqualTo(0.4f).Within(1e-6f), "Tail first to the floor");
            Assert.That(e.Host.Unit(UnitId.Handpan)->Share, Is.EqualTo(0.4f).Within(1e-6f), "then Playback");
            Assert.That(e.Host.Unit(UnitId.HandDrums)->Share, Is.EqualTo(1f), "AtSeat last (eight steps: four for Tail, four for Playback)");
            Assert.That(e.Host.Unit(UnitId.Handpan)->Class, Is.EqualTo(UnitClass.Playback));
            // the marimba is no longer played: AtSeat now, and it drops with the hand drums
            act.Live.Clear();
            gov.StepNow(2.4, null, act);
            Assert.That(e.Host.Unit(UnitId.Marimba)->Class, Is.EqualTo(UnitClass.AtSeat));
            Assert.That(e.Host.Unit(UnitId.Marimba)->Share, Is.EqualTo(0.85f).Within(1e-6f));
            Assert.That(e.Host.Unit(UnitId.HandDrums)->Share, Is.EqualTo(0.85f).Within(1e-6f), "AtSeat together");
            // a quiet second raises the highest class below 1 (AtSeat) first
            gov.StepNow(0.5, null, act);
            Assert.That(e.Host.Unit(UnitId.Marimba)->Share, Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(e.Host.Unit(UnitId.HandDrums)->Share, Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(e.Host.Unit(UnitId.Bowls)->Share, Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(() => gov.Tick(null, act), Is.Not.AllocatingGCMemory(), "the frame's governor allocates nothing");
        }
    }
}
