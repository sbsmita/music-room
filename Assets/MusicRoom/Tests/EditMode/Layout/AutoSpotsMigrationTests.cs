// The spots an older build saved by itself (Move instruments on and off saved every instrument's and the side table's
// automatic pose, 2026-11-17 TUNE-DECK-FRONT) are cleared once on an install that ran an older build, never on a new one,
// and only once (a spot the player drags afterwards is kept).
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.EditMode.Layout
{
    public class AutoSpotsMigrationTests
    {
        [Test]
        public void AnOldInstallLosesItsSavedSpotsOnce()
        {
            var s = new AppSettings(new MemorySettingsStore());
            s.SetInt(ComfortCodec.KeyMigratedStabilise, 1);   // an earlier build ran here
            s.SetComfort(LayoutMode.Single, InstrumentKind.Drums, "c1;Drums:0.06,0,0,-20;Table:0.1,0,0,0");
            s.SetComfort(LayoutMode.All, InstrumentKind.Piano, "c1;Table:0.1,0,0,0");
            s.SetString(AppSettings.ArrangeKey(LayoutMode.Single, InstrumentKind.Harp), "old");
            LogAssert.ignoreFailingMessages = true;
            Assert.That(ComfortCodec.MigrateAutoSavedSpots(s), Is.EqualTo(3));
            Assert.That(s.GetComfort(LayoutMode.Single, InstrumentKind.Drums), Is.EqualTo(""));
            Assert.That(s.GetComfort(LayoutMode.All, InstrumentKind.Piano), Is.EqualTo(""));
            Assert.IsFalse(s.Store.HasKey(AppSettings.ArrangeKey(LayoutMode.Single, InstrumentKind.Harp)));
            // a deliberate drag afterwards is kept
            s.SetComfort(LayoutMode.Single, InstrumentKind.Drums, "c1;Drums:0.1,0,0,0");
            Assert.That(ComfortCodec.MigrateAutoSavedSpots(s), Is.EqualTo(0));
            Assert.That(s.GetComfort(LayoutMode.Single, InstrumentKind.Drums), Is.EqualTo("c1;Drums:0.1,0,0,0"));
        }

        [Test]
        public void ANewInstallKeepsEverything()
        {
            var s = new AppSettings(new MemorySettingsStore());
            s.SetComfort(LayoutMode.Single, InstrumentKind.Drums, "c1;Drums:0.1,0,0,0");
            Assert.That(ComfortCodec.MigrateAutoSavedSpots(s), Is.EqualTo(0));
            Assert.That(s.GetComfort(LayoutMode.Single, InstrumentKind.Drums), Is.EqualTo("c1;Drums:0.1,0,0,0"));
            Assert.That(s.GetInt(ComfortCodec.KeyMigratedAutoSpots, 0), Is.EqualTo(1));
        }
    }
}
