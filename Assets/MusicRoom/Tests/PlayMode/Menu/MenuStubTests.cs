// Menu module (owned by the Menu module, rule 3; written by the foundation against its stub, kept as the contract
// check): show / hide events and the always-present instrument rule (the core loop ticks a hidden menu so its summon
// gesture and close animation run in its update()).
using System.Collections;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Menu
{
    public class MenuStubTests
    {
        TestBoot _boot;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot();
            MenuBootGuard.Begin();   // the known SynthSource boot race only (CONTRACT-REQUESTS.md 3)
            yield return _boot.Start();
            MenuBootGuard.End();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MenuBootGuard.Stop();
            yield return _boot.Stop();
        }

        [UnityTest]
        public IEnumerator MenuTogglesPublishesEventsAndStaysPresentWhileHidden()
        {
            var app = App.Current;
            int opens = 0, closes = 0;
            app.Events.Subscribe<MenuEvent>(e => { if (e.Change == MenuChange.Open) opens++; else if (e.Change == MenuChange.Close) closes++; });
            var menu = app.Get<IMenuPanel>();
            var inst = (IInstrument)menu;
            Assert.IsFalse(menu.Shown);
            Assert.IsTrue(inst.IsPresent, "a hidden menu is still ticked (summon gesture)");
            menu.Toggle();
            Assert.IsTrue(menu.Shown);
            Assert.That(opens, Is.EqualTo(1));
            Assert.IsTrue(menu.GrabZone.HasValue);
            yield return null;
            menu.Toggle();
            Assert.IsFalse(menu.Shown);
            Assert.That(closes, Is.EqualTo(1));
            Assert.IsFalse(menu.GrabZone.HasValue);
            Assert.IsTrue(inst.IsPresent);
        }
    }
}
