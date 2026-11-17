// platform.md 12.1 RegistryTests: the registry built by reflection from every IInstrumentDefinition (EditMode, no scene):
// one descriptor per playable kind, unique ids / carousel / event types / tips / features, the id prefixes, the id block's
// RigOf rule, the UnitId / SourceId mapping against the AudioSourceTable, the carousel order of section 4.3, and the
// validation rules (a duplicate is dropped and reported).
using System;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.Core
{
    public class RegistryTests
    {
        static InstrumentRegistry Reg() => InstrumentRegistry.Discover();

        static readonly string[] s_carousel = { "piano", "harp", "bowls", "handpan", "marimba", "handdrums", "drums" };

        [Test]
        public void EveryPlayableKindHasOneDescriptorAndTheBuildIsClean()
        {
            InstrumentRegistry r = Reg();
            Assert.That(r.Errors, Is.Empty, string.Join("\n", r.Errors));
            Assert.That(r.All.Count, Is.EqualTo(7));
            foreach (InstrumentKind k in new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp, InstrumentKind.Handpan,
                         InstrumentKind.HandDrums, InstrumentKind.Marimba, InstrumentKind.Bowls })
            {
                InstrumentDescriptor d = r.Get(k);
                Assert.IsNotNull(d, k.ToString());
                Assert.IsTrue(d.Playable, k.ToString());
                Assert.That(d.Id, Is.EqualTo(InstrumentIds.Of(k)), "the id block's persisted id");
            }
            Assert.IsNotNull(r.Get(InstrumentKind.Table));
            Assert.IsNotNull(r.Get(InstrumentKind.Menu));
            Assert.IsFalse(r.All.Any(d => d.Kind == InstrumentKind.Table || d.Kind == InstrumentKind.Menu), "the built-ins are not playable");
        }

        [Test]
        public void TheCarouselIsSection43sOrder()
        {
            InstrumentRegistry r = Reg();
            Assert.That(r.All.Select(d => d.Id).ToArray(), Is.EqualTo(s_carousel));
            Assert.That(r.All.Select(d => d.Carousel).ToArray(), Is.EqualTo(new[] { 0, 1, 3, 4, 5, 6, 7 }), "carousel index 2 is retired (2026-11-10)");
        }

        [Test]
        public void IdsEventsTipsAndFeaturesAreUniqueAndPrefixed()
        {
            InstrumentRegistry r = Reg();
            var types = new HashSet<string>(); var tips = new HashSet<string>(); var features = new HashSet<string>();
            foreach (InstrumentDescriptor d in r.All)
            {
                bool classic = d.Kind <= InstrumentKind.Harp;
                // The hand drums' types start with "handDrum" (platform.md 3.4), the bowls' with "bowl" (6.1: bowlStrike, bowlRubOn).
                string prefix = d.Kind == InstrumentKind.HandDrums ? "handDrum" : d.Kind == InstrumentKind.Bowls ? "bowl" : d.Id;
                foreach (EventTypeSpec e in d.Events)
                {
                    Assert.IsTrue(types.Add(e.Type), "duplicate event type " + e.Type);
                    if (!classic) Assert.That(e.Type, Does.StartWith(prefix), $"{d.Id}: event types start with its prefix");
                    Assert.That(r.OwnerOfEvent(e.Type), Is.EqualTo(d.Kind));
                    Assert.That(r.RoleOf(e.Type), Is.EqualTo(e.Role));
                }
                foreach (TipSpec t in d.Tips)
                {
                    Assert.IsTrue(tips.Add(t.Id), "duplicate tip " + t.Id);
                    if (!classic) Assert.That(t.Id, Does.StartWith("tip_" + d.Id + "_"), $"{d.Id}: tip ids are tip_<id>_*");
                    foreach (string line in t.Lines) if (!classic) Assert.That(line, Does.StartWith("tip_" + d.Id + "_"), $"{d.Id}: line ids are tip_<id>_*");
                }
                foreach (FeatureSpec f in d.Features)
                {
                    Assert.IsTrue(features.Add(f.Id), "duplicate feature " + f.Id);
                    Assert.That(f.Id, Does.StartWith(d.Id + "."), $"{d.Id}: feature ids are <id>.<feature>");
                }
                if (d.Lessons?.Pack != null) Assert.That(d.Lessons.Pack, Is.EqualTo(d.Id), "the lesson pack file is Resources/LessonPacks/<id>.json");
            }
        }

        [Test]
        public void TheIdBlockHolds()
        {
            InstrumentRegistry r = Reg();
            foreach (InstrumentDescriptor d in r.Every)
            {
                Assert.That((int)d.Rig, Is.EqualTo((int)d.Kind), $"{d.Id}: RigOf(kind) = (RigId)(int)kind");
                Assert.That(InstrumentIds.RigOf(d.Kind), Is.EqualTo(d.Rig));
            }
            Assert.That((int)RigId.Lessons, Is.EqualTo((int)InstrumentKind.Menu), "Lessons = 4 = Menu");
            // platform.md section 2: Order and TickOrder.
            var order = new Dictionary<string, (int module, int tick)>
            {
                ["piano"] = (30, 0), ["drums"] = (31, 10), ["harp"] = (32, 20), ["handpan"] = (33, 21), ["handdrums"] = (34, 22),
                ["marimba"] = (35, 23), ["bowls"] = (37, 24),
            };
            foreach (InstrumentDescriptor d in r.All)
            {
                Assert.That(d.ModuleOrder, Is.EqualTo(order[d.Id].module), d.Id + " module Order");
                Assert.That(d.TickOrder, Is.EqualTo(order[d.Id].tick), d.Id + " TickOrder");
            }
        }

        [Test]
        public void UnitsAndSourcesMatchTheEngine()
        {
            InstrumentRegistry r = Reg();
            Assert.That((int)UnitId.Count, Is.EqualTo(10));
            Assert.That((int)SourceId.Count, Is.EqualTo(25), "18 classic + 7 platform sources");
            var seen = new HashSet<int>();
            foreach (InstrumentDescriptor d in r.All)
            {
                Assert.IsNotNull(d.Audio, d.Id);
                if (d.Kind >= InstrumentKind.Handpan)
                {
                    Assert.That(d.Audio.Unit, Is.EqualTo((byte)(int)d.Kind), $"{d.Id}: its UnitId equals its kind");
                    Assert.IsTrue(SoundUnits.IsUnitOnly((UnitId)d.Audio.Unit), d.Id + " is a unit-only role");
                }
                Assert.That(d.Audio.Sources.Length, Is.GreaterThan(0), d.Id);
                foreach (AudioSourceSpec s in d.Audio.Sources)
                {
                    Assert.That(s.Source, Is.InRange(0, (int)SourceId.Count - 1), d.Id);
                    Assert.IsTrue(seen.Add(s.Source), $"{d.Id}: source {(SourceId)s.Source} belongs to one instrument");
                    if (d.Kind >= InstrumentKind.Handpan) Assert.That(s.Source, Is.GreaterThanOrEqualTo((int)SourceId.Handpan), d.Id + ": a platform source");
                }
            }
            // Every platform source belongs to exactly one new instrument.
            for (int s = (int)SourceId.Handpan; s < (int)SourceId.Count; s++) Assert.IsTrue(seen.Contains(s), ((SourceId)s) + " has an owner");
        }

        [Test]
        public void EveryDescriptorIsComplete()
        {
            foreach (InstrumentDescriptor d in Reg().All)
            {
                Assert.IsNotNull(d.Seat, d.Id);
                Assert.That(d.Seat.Boxes.Count(b => !b.Air), Is.GreaterThan(0), d.Id + " footprint");
                Assert.That(d.Seat.Prefs, Does.Contain(d.Seat.CanonicalSlot), d.Id + ": its canonical slot is one of its preferences");
                Assert.That(d.Seat.ReachMax, Is.GreaterThan(0.3f), d.Id);
                Assert.IsNotNull(d.Stow, d.Id); Assert.That(d.Stow.SpotId, Is.Not.Empty, d.Id); Assert.That(d.Stow.Where, Is.Not.Empty, d.Id);
                Assert.IsNotNull(d.Portrait, d.Id); Assert.That(d.Portrait.Fallback.Length, Is.GreaterThan(0), d.Id + " fallback icon");
                Assert.IsNotNull(d.Lessons, d.Id); Assert.That(d.Lessons.CountNoun, Is.Not.Empty, d.Id);
                Assert.IsNotNull(d.Mr, d.Id); Assert.IsNotNull(d.Shadow, d.Id);
                Assert.That(d.Events.Count(e => e.Role == EventRole.Onset), Is.GreaterThan(0), d.Id + " records an onset");
                Assert.That(d.DrawsAtSeat, Is.GreaterThan(0), d.Id); Assert.That(d.DrawsStowed, Is.GreaterThan(0), d.Id);
                Assert.That(d.Name, Is.Not.Empty); Assert.That(d.AloneName, Is.Not.Empty);
                if (d.Has(InstrumentTraits.Contactless)) Assert.IsTrue(d.Seat.Boxes.Any(b => b.Air), d.Id + ": a contactless instrument declares its air field");
            }
        }

        [Test]
        public void ADuplicateIsDroppedAndReported()
        {
            InstrumentRegistry real = Reg();
            var list = real.All.ToList();
            InstrumentDescriptor dup = new InstrumentDescriptor { Kind = InstrumentKind.Bowls, Rig = RigId.Bowls, Id = "bowls2", Name = "x", Carousel = 9, Traits = InstrumentTraits.Playable };
            list.Add(dup);
            LogAssert.ignoreFailingMessages = true;
            InstrumentRegistry r = InstrumentRegistry.Build(list);
            LogAssert.ignoreFailingMessages = false;
            Assert.That(r.Errors.Count, Is.EqualTo(1), string.Join("\n", r.Errors));
            Assert.That(r.Get(InstrumentKind.Bowls).Id, Is.EqualTo("bowls"), "the later definition is dropped");
            Assert.That(r.All.Count, Is.EqualTo(7));
        }

        [Test]
        public void ParseNextAndFilter()
        {
            InstrumentRegistry r = InstrumentRegistry.Build(Reg().All, k => k <= InstrumentKind.Harp);
            foreach (InstrumentDescriptor d in r.All) r.MarkLoaded(d.Kind);
            Assert.IsTrue(r.TryParse("bowls", out InstrumentKind t)); Assert.That(t, Is.EqualTo(InstrumentKind.Bowls));
            Assert.IsFalse(r.TryParse("theremin", out _), "a retired instrument's id parses as nothing");
            Assert.IsFalse(r.TryParse("kazoo", out _));
            Assert.IsFalse(r.IsLoaded(InstrumentKind.Bowls), "filtered out");
            Assert.That(r.Next(InstrumentKind.Piano, 1), Is.EqualTo(InstrumentKind.Harp));
            Assert.That(r.Next(InstrumentKind.Harp, 1), Is.EqualTo(InstrumentKind.Drums), "skips the unloaded four");
            Assert.That(r.Next(InstrumentKind.Piano, -1), Is.EqualTo(InstrumentKind.Drums));
            Assert.That(r.RoleOf("nobodysType"), Is.EqualTo(EventRole.Onset), "unknown types stay onsets");
            Assert.That(r.OwnerOfEvent("nobodysType"), Is.EqualTo(InstrumentKind.Table));
        }

        [Test]
        public void TheDescriptorsHoldNoUnityObjects()
        {
            // Static data only (platform.md 3.4): every field of every descriptor type is a value, a string, an array or a
            // descriptor class of Core: never a UnityEngine.Object.
            var types = new[] { typeof(InstrumentDescriptor), typeof(SeatSpec), typeof(StowSpec), typeof(PortraitSpec), typeof(AudioSpec),
                typeof(LessonSpec), typeof(MrSpec), typeof(ShadowSpec), typeof(TipSpec), typeof(DemoMotion), typeof(InstrumentOption) };
            foreach (Type t in types)
                foreach (var f in t.GetFields())
                {
                    Type ft = f.FieldType.IsArray ? f.FieldType.GetElementType() : f.FieldType;
                    Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(ft), $"{t.Name}.{f.Name} holds a Unity object");
                }
        }
    }
}
