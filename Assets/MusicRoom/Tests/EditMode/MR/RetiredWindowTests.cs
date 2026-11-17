// The snowy window of mixed reality was removed (2026-11-13): RetiredWindow.Migrate forgets what an older save kept of it
// (the placed window's pose, its spatial anchor's uuid, the failed-restore count), leaves a clean save and every other
// setting alone, and a second run finds nothing to do.
using MusicRoom.Core;
using MusicRoom.MR;
using NUnit.Framework;

namespace MusicRoom.Tests.MR
{
    public class RetiredWindowTests
    {
        [Test]
        public void AnOldSavesWindowIsForgottenOnce()
        {
            var store = new MemorySettingsStore();
            store.SetString("musicroom.mrWindow", "1.2,1.4,-0.5;0,0.7071,0,0.7071");
            store.SetString("musicroom.mrWindowAnchor", "0123456789abcdef0123456789abcdef");
            store.SetInt("musicroom.mrWindowFailures", 2);
            store.SetString("musicroom.mrPianoAnchor", "fedcba9876543210fedcba9876543210");   // not the window's
            store.SetInt(AppSettings.KeyTourDone, 1);
            var settings = new AppSettings(store);

            Assert.That(RetiredWindow.Migrate(settings, eraseAnchor: false), Is.EqualTo(3));
            foreach (string k in RetiredWindow.Keys) Assert.IsFalse(store.HasKey(k), k + " is gone");
            Assert.That(store.GetString("musicroom.mrPianoAnchor", ""), Is.EqualTo("fedcba9876543210fedcba9876543210"), "other MR settings stay");
            Assert.That(store.GetInt(AppSettings.KeyTourDone, 0), Is.EqualTo(1), "and everything else");
            Assert.That(RetiredWindow.Migrate(settings, eraseAnchor: false), Is.EqualTo(0), "idempotent");
        }

        [Test]
        public void ACleanSaveIsLeftAlone()
        {
            var store = new MemorySettingsStore();
            store.SetInt(AppSettings.KeyTourDone, 1);
            Assert.That(RetiredWindow.Migrate(new AppSettings(store), eraseAnchor: true), Is.EqualTo(0));
            Assert.That(store.GetInt(AppSettings.KeyTourDone, 0), Is.EqualTo(1));
            Assert.That(RetiredWindow.Migrate(null, eraseAnchor: true), Is.EqualTo(0), "no settings: nothing to do");
        }

        [Test]
        public void TheKeysAreTheOnesTheWindowWrote()
        {
            Assert.That(RetiredWindow.Keys, Is.EquivalentTo(new[] { "musicroom.mrWindow", "musicroom.mrWindowAnchor", "musicroom.mrWindowFailures" }));
            Assert.IsFalse(RetiredWindow.TryEraseAnchor(""), "nothing saved: nothing to erase");
            Assert.IsFalse(RetiredWindow.TryEraseAnchor("not a uuid"));
        }
    }
}
