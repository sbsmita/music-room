// The snowy window of mixed reality is gone (2026-11-13, the player: "remove all window things"; the snow falls in the
// whole room now): a save that still keeps a placed window from an older version boots clean (RetiredWindow.Migrate in
// MrModule.Init forgets it), and in a room without windows nothing offers, ghosts or brings back a window, however long
// the player waits with snow on.
#if UNITY_EDITOR
using System.Collections;
using MusicRoom.Core;
using MusicRoom.MR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.MR
{
    public class MrRetiredWindowTests : MrTestRig
    {
        protected override bool TableOnEnter => false;

        protected override void Prepare(MemorySettingsStore settings)
        {
            settings.SetString(RetiredWindow.KeyPose, "0.8,1.4,-2.1;0,0,0,1");
            settings.SetString(RetiredWindow.KeyAnchor, "0123456789abcdef0123456789abcdef");
            settings.SetInt(RetiredWindow.KeyFailures, 1);
        }

        [UnityTest]
        public IEnumerator AnOldSavedWindowIsForgottenAndNoWindowEverComes()
        {
            Assert.That(Mr.RetiredWindowSettings, Is.EqualTo(3), "the old window's three settings, forgotten on boot");
            foreach (string k in RetiredWindow.Keys) Assert.IsFalse(App.Settings.Store.HasKey(k), k);

            App.Get<IAmbienceSwitches>().Set(AmbienceKind.Snow, true);
            Weather.Target = 1f; Weather.Level = 1f;
            yield return EnterRoom("MeshBedroom3");   // a room with no window of its own
            StepFrames(80);                            // 4 s: the old offer came after 2.5 s
            yield return null;
            Assert.That(Mr.Active, Is.True);
            Assert.That(Mr.Hint == null || !Mr.Hint.Contains("window"), Is.True, "no window hint: " + Mr.Hint);
            Transform mrRoot = App.WorldRoot.Find("MixedReality");
            Assert.That(mrRoot, Is.Not.Null);
            Assert.That(MrModuleTests.NamedUnder(mrRoot, "window"), Is.Zero, "no window, ghost, reticle or anchor of one");
            Assert.That(MrModuleTests.NamedUnder(App.WorldRoot, "SnowPortal") + MrModuleTests.NamedUnder(App.WorldRoot, "Snow portal"), Is.Zero, "no snow portal");
            foreach (string k in RetiredWindow.Keys) Assert.IsFalse(App.Settings.Store.HasKey(k), k + " stays gone");
        }
    }
}
#endif
