// A save made before the theremin was removed (2026-11-10): RetiredInstruments.Migrate on boot falls back sensibly
// (the piano for One alone, My set without it, the default set when nothing is left), deletes its own settings, and
// leaves a clean save alone; the id no longer parses anywhere; comfort offsets under its rig name are skipped.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;

namespace MusicRoom.Tests.Core
{
    public class RetiredInstrumentsTests
    {
        const string Old = "theremin";

        static AppSettings Settings(MemorySettingsStore store) => new AppSettings(store) { Registry = InstrumentRegistry.Discover() };

        [Test]
        public void ItsIdParsesAsNothingAndTheRegistryHasNoTraceOfIt()
        {
            Assert.IsFalse(InstrumentIds.TryParse(Old, out _), "the id block");
            Assert.IsFalse(InstrumentIds.TryParse(null, out _), "a null id is no retired slot");
            Assert.IsNull(InstrumentIds.Of((InstrumentKind)8), "8 is retired");
            Assert.IsFalse(InstrumentIds.IsPlayable((InstrumentKind)8));
            InstrumentRegistry reg = InstrumentRegistry.Discover();
            Assert.IsFalse(reg.TryParse(Old, out _), "the registry");
            Assert.IsNull(reg.Get((InstrumentKind)8));
            Assert.That(reg.All.Count, Is.EqualTo(7), "seven instruments");
            foreach (InstrumentDescriptor d in reg.Every)
            {
                Assert.That(d.Id, Does.Not.Contain(Old));
                if (d.Events != null) foreach (EventTypeSpec e in d.Events) Assert.That(e.Type, Does.Not.StartWith(Old), d.Id);
                if (d.Tips != null) foreach (TipSpec t in d.Tips) Assert.That(t.Id, Does.Not.Contain(Old), d.Id);
            }
            Assert.That(reg.OwnerOfEvent(Old + "On"), Is.EqualTo(InstrumentKind.Table), "an old take's event has no owner");
            Assert.IsTrue(RetiredInstruments.IsRetired(Old));
            Assert.IsFalse(RetiredInstruments.IsRetired("handpan"));
        }

        [Test]
        public void ItWasPlayedAloneAndInMySet()
        {
            var store = new MemorySettingsStore();
            store.SetString(AppSettings.KeyLayoutMode, "single");
            store.SetString(AppSettings.KeyCurrentInstrument, Old);
            store.SetString(AppSettings.KeySet, "handpan," + Old + ",bowls");
            store.SetInt("musicroom." + Old + ".tone", 2);
            store.SetInt("musicroom." + Old + ".help", 0);
            store.SetInt("musicroom." + Old + ".hand", 1);
            store.SetInt("musicroom." + Old + ".notes", 1);
            store.SetString(AppSettings.KeyComfortPrefix + "single." + Old, "c1;Piano:0.1,0,0,0");
            store.SetString(AppSettings.KeyStowPrefix + Old, "-1.6,0.9,0");
            store.SetString(AppSettings.KeyStowPrefix + "handpan", "-3.0,-0.5,-90");
            AppSettings s = Settings(store);
            Assert.That(RetiredInstruments.Migrate(s), Is.EqualTo(8));
            Assert.That(store.GetString(AppSettings.KeyCurrentInstrument, ""), Is.EqualTo("piano"), "One alone falls back to the piano");
            Assert.That(s.CurrentInstrument, Is.EqualTo(InstrumentKind.Piano));
            Assert.That(store.GetString(AppSettings.KeySet, ""), Is.EqualTo("handpan,bowls"), "My set keeps the others, in order");
            foreach (string k in new[] { "tone", "help", "hand", "notes" }) Assert.IsFalse(store.HasKey("musicroom." + Old + "." + k), k);
            Assert.IsFalse(store.HasKey(AppSettings.KeyComfortPrefix + "single." + Old));
            Assert.IsFalse(store.HasKey(AppSettings.KeyStowPrefix + Old));
            Assert.IsTrue(store.HasKey(AppSettings.KeyStowPrefix + "handpan"), "another instrument's spot stays");
            Assert.That(store.GetString(AppSettings.KeyLayoutMode, ""), Is.EqualTo("single"), "the mode stays");
            Assert.That(RetiredInstruments.Migrate(s), Is.EqualTo(0), "once is enough");
        }

        [Test]
        public void ASetOfOnlyItFallsBackToTheDefaultSetAndACleanSaveIsLeftAlone()
        {
            var store = new MemorySettingsStore();
            store.SetString(AppSettings.KeySet, Old);
            AppSettings s = Settings(store);
            Assert.That(RetiredInstruments.Migrate(s), Is.EqualTo(1));
            Assert.That(store.GetString(AppSettings.KeySet, ""), Is.EqualTo(MySetRules.Default));

            var clean = new MemorySettingsStore();
            clean.SetString(AppSettings.KeyCurrentInstrument, "harp");
            clean.SetString(AppSettings.KeySet, "piano,drums,harp");
            Assert.That(RetiredInstruments.Migrate(Settings(clean)), Is.EqualTo(0));
            Assert.That(clean.GetString(AppSettings.KeyCurrentInstrument, ""), Is.EqualTo("harp"));
            Assert.That(clean.GetString(AppSettings.KeySet, ""), Is.EqualTo("piano,drums,harp"));
            Assert.That(RetiredInstruments.Migrate(Settings(new MemorySettingsStore())), Is.EqualTo(0), "a fresh install");
        }

        [Test]
        public void ComfortOffsetsUnderItsRigNameAreSkipped()
        {
            var back = new Dictionary<RigId, ComfortOffset>();
            Assert.That(ComfortCodec.Decode("c1;Theremin:0.1,0.2,0,10;Harp:0.05,0,0,0", back), Is.EqualTo(1));
            Assert.IsTrue(back.ContainsKey(RigId.Harp));
            Assert.That(back.Count, Is.EqualTo(1));
        }
    }
}
