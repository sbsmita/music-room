// The lesson packs (platform.md 7.1, 7.4; 12.1 LessonPackTests): every Resources/LessonPacks/<id>.json loads with
// lessons.json in carousel order; every lesson names a registry instrument and carries its id's prefix; lesson ids are
// unique (a clash or an unknown instrument is dropped and logged); every target validates (EditMode: a phase-0 stub's
// targets by the PlaceholderInstrument rule, i in 0..Regions-1, built from the pack and the descriptor alone; a real
// instrument's are checked through its registered ILessonTargets in PlayMode, GenericTutorialPlayTests); and the content
// rules of 7.4 hold (at least 3 lessons for a real instrument, at most 2 targets per event, gaps, repeats, under a minute).
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MusicRoom.Core;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.Lessons
{
    public class LessonPackTests
    {
        static InstrumentRegistry Registry() => InstrumentRegistry.Discover();

        static IEnumerable<Lesson> PackLessons(LessonLibrary lib, InstrumentRegistry reg)
        {
            foreach (Lesson l in lib.Lessons)
                if (reg.TryParse(l.instrument, out InstrumentKind k) && reg.Get(k)?.Lessons?.Pack != null) yield return l;
        }

        [Test]
        public void EveryPackLoadsWithTheClassicLessonsInCarouselOrder()
        {
            InstrumentRegistry reg = Registry();
            LessonLibrary lib = LessonLibrary.LoadFromResources(reg);
            Assert.That(lib.Errors, Is.Empty, string.Join("\n", lib.Errors));
            int packs = 0;
            foreach (InstrumentDescriptor d in reg.All)
            {
                if (d.Lessons?.Pack == null) continue;
                packs++;
                Assert.That(lib.Packs, Does.Contain(LessonLibrary.PackFolder + "/" + d.Lessons.Pack), d.Id + " ships its pack");
                Assert.That(lib.Count(d.Kind), Is.GreaterThanOrEqualTo(1), d.Id + " has lessons");
            }
            Assert.That(packs, Is.GreaterThanOrEqualTo(4), "the four new instruments' packs");
            int classic = LessonLibrary.Parse(Resources.Load<TextAsset>(LessonLibrary.ResourceName).text).Lessons.Count;
            Assert.That(classic, Is.EqualTo(36), "lessons.json (first five: tutorials-audit.md's lessons-proposed.json)");
            int fromPacks = 0;
            foreach (Lesson _ in PackLessons(lib, reg)) fromPacks++;
            Assert.That(lib.Lessons.Count, Is.EqualTo(classic + fromPacks));
            Assert.That(lib.File.counts.lessons, Is.EqualTo(lib.Lessons.Count), "the counts are recomputed");
            Assert.That(lib.File.counts.events, Is.EqualTo(lib.TotalEvents));
            // Carousel order, then file order (lessons.json's own order within the classic three).
            int last = int.MinValue;
            foreach (Lesson l in lib.Lessons)
            {
                Assert.IsTrue(reg.TryParse(l.instrument, out InstrumentKind k));
                int c = reg.Get(k).Carousel;
                Assert.That(c, Is.GreaterThanOrEqualTo(last), l.id + " in carousel order");
                last = c;
            }
            Assert.That(lib.Lessons[0].instrument, Is.EqualTo("piano"), "the carousel starts at the piano");
        }

        [Test]
        public void EveryLessonNamesARegistryInstrumentWithItsPrefixAndIdsAreUnique()
        {
            InstrumentRegistry reg = Registry();
            LessonLibrary lib = LessonLibrary.LoadFromResources(reg);
            var ids = new HashSet<string>();
            foreach (Lesson l in lib.Lessons)
            {
                Assert.IsTrue(reg.TryParse(l.instrument, out InstrumentKind k), l.id + ": " + l.instrument);
                Assert.IsTrue(reg.Get(k).Playable);
                Assert.IsTrue(ids.Add(l.id), "unique: " + l.id);
                if (reg.Get(k).Lessons?.Pack != null) Assert.That(l.id, Does.StartWith(l.instrument + "-"), "platform.md 3.4: a pack's ids carry the instrument's prefix");
            }
        }

        [Test]
        public void AnUnknownInstrumentAndADuplicateIdAreDroppedAndLogged()
        {
            InstrumentRegistry reg = Registry();
            string main = Resources.Load<TextAsset>(LessonLibrary.ResourceName).text;
            const string pack = "{\"lessons\":[{\"id\":\"kazoo-first\",\"instrument\":\"kazoo\",\"bpm\":60,\"events\":[]}," +
                                "{\"id\":\"piano-scale\",\"instrument\":\"piano\",\"bpm\":60,\"events\":[]}," +
                                "{\"id\":\"handpan-extra\",\"instrument\":\"handpan\",\"bpm\":60,\"events\":[]}]}";
            LogAssert.Expect(LogType.Error, new Regex(@"\[Lessons\] .*kazoo.*not in the registry"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Lessons\] .*duplicate lesson id piano-scale"));
            LessonLibrary lib = LessonLibrary.Load(new[] { ("lessons", main), ("LessonPacks/test", pack) }, reg);
            Assert.That(lib.Errors.Count, Is.EqualTo(2));
            Assert.That(lib.Get("kazoo-first"), Is.Null, "an instrument outside the registry: dropped");
            Assert.That(lib.Get("piano-scale").title, Is.Not.Null.And.Not.Empty, "the first piano-scale (lessons.json) stays");
            Assert.That(lib.Get("handpan-extra"), Is.Not.Null);
            Assert.That(lib.Count(InstrumentKind.Handpan), Is.EqualTo(1), "Count works for any kind");
            Assert.That(lib.Count(InstrumentKind.Piano), Is.EqualTo(15));   // first five: lessons-proposed.json
        }

        [Test]
        public void EveryStubTargetValidatesFromThePackAndTheDescriptor()
        {
            InstrumentRegistry reg = Registry();
            LessonLibrary lib = LessonLibrary.LoadFromResources(reg);
            int checkedTargets = 0, stubPacks = 0;
            foreach (InstrumentDescriptor d in reg.All) if (d.Placeholder && d.Lessons?.Pack != null) stubPacks++;
            foreach (Lesson l in PackLessons(lib, reg))
            {
                reg.TryParse(l.instrument, out InstrumentKind k);
                InstrumentDescriptor d = reg.Get(k);
                if (!d.Placeholder) continue;                     // a real instrument: its ILessonTargets in PlayMode
                foreach (LessonEvent e in l.events)
                    foreach (LessonTarget t in e.targets)
                    {
                        Assert.That(t.i, Is.InRange(0, PlaceholderInstrument.Regions - 1), $"{l.id} event {e.k}: the placeholder's regions");
                        checkedTargets++;
                    }
            }
            // since the integration every instrument is real (no stub pack left): the rule then has nothing to check, and the
            // real instruments' targets are checked through their ILessonTargets (GenericTutorialPlayTests, each module's tests)
            if (stubPacks > 0) Assert.That(checkedTargets, Is.GreaterThan(0), stubPacks + " stub pack(s) with no target");
            else Assert.Pass("no stub instrument left: every pack is a real instrument's");
        }

        [Test]
        public void EveryPackKeepsTheContentRules()
        {
            InstrumentRegistry reg = Registry();
            LessonLibrary lib = LessonLibrary.LoadFromResources(reg);
            foreach (InstrumentDescriptor d in reg.All)
            {
                if (d.Lessons?.Pack == null) continue;
                var lessons = new List<Lesson>();
                foreach (Lesson l in lib.Lessons) if (l.instrument == d.Id) lessons.Add(l);
                if (!d.Placeholder) Assert.That(lessons.Count, Is.GreaterThanOrEqualTo(3), d.Id + ": at least 3 lessons");
                Assert.That(lessons.Count, Is.GreaterThanOrEqualTo(1));
                for (int n = 0; n < lessons.Count; n++)
                {
                    Lesson l = lessons[n];
                    Assert.That(l.bpm, Is.GreaterThan(0f), l.id);
                    Assert.That(l.events, Is.Not.Null.And.Not.Empty, l.id);
                    double spb = 60.0 / l.bpm, lead = d.Lessons.LeadSeconds;
                    double end = lead + l.events[l.events.Length - 1].beat * spb;
                    Assert.That(end, Is.LessThan(60.0), l.id + ": under a minute");
                    double prev = double.NegativeInfinity;
                    var lastAt = new Dictionary<string, double>();
                    foreach (LessonEvent e in l.events)
                    {
                        Assert.That(e.targets, Is.Not.Null.And.Not.Empty, $"{l.id} event {e.k}");
                        Assert.That(e.targets.Length, Is.LessThanOrEqualTo(2), $"{l.id} event {e.k}: two hands at most");
                        if (n == 0) Assert.That(e.targets.Length, Is.EqualTo(1), l.id + ": the first lesson is one target at a time");
                        double at = e.beat * spb;
                        Assert.That(at - prev, Is.GreaterThanOrEqualTo(0.2 - 1e-6), $"{l.id} event {e.k}: shortest gap 0.2 s");
                        foreach (LessonTarget t in e.targets)
                        {
                            string key = $"{t.i}|{t.midi}|{t.kind}|{t.zone}|{t.mode}";
                            if (lastAt.TryGetValue(key, out double was)) Assert.That(at - was, Is.GreaterThanOrEqualTo(0.25 - 1e-6), $"{l.id} event {e.k}: a repeat 0.25 s apart");
                            lastAt[key] = at;
                        }
                        prev = at;
                    }
                    double seconds = l.seconds > 0f ? l.seconds : l.beats * spb;
                    if (n == 0) Assert.That(seconds, Is.InRange(12.0 - 1e-6, 21.0 + 1e-6), l.id + ": the first lesson is a 12-21 s win");
                }
            }
        }
    }
}
