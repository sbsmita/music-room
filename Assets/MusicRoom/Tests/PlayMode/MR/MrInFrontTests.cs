// "Play in my room" opens the instruments in front of the player like the music room (the real Layout module, one of
// MRUK's recorded rooms, MrModule.TableOnEnter off as in the app): the piano on its stand on the real floor, straight
// ahead and within a foot, never on a table across the room, and an anchor an earlier version saved is forgotten, not
// restored. The hands: drawn (Skin) by default, the menu's "Real hands" row switches to the cut-out and back, the choice
// is saved and holds on the next entry, and leaving always brings the drawn hands back.
#if UNITY_EDITOR
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Menu;
using MusicRoom.MR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.MR
{
    public class MrInFrontTests : MrTestRig
    {
        protected override bool UseContractLayout => false;
        protected override bool TableOnEnter => false;
        string _anchor;

        protected override void Prepare(MemorySettingsStore settings)
        {
            _anchor = System.Guid.NewGuid().ToString("N");
            settings.SetString(PianoAnchorStore.KeyAnchor, _anchor);   // an anchor saved by an earlier version
            settings.SetInt(PianoAnchorStore.KeyOnTable, 1);
            settings.SetInt(AppSettings.KeyTourDone, 1);
        }

        LayoutModule RealLayout => App.Get<ILayoutService>() as LayoutModule;

        [UnityTest]
        public IEnumerator EnteringOpensThePianoInFrontOnTheRealFloorAndForgetsAnOldAnchor()
        {
            Assert.That(RealLayout, Is.Not.Null, "the build's Layout module");
            App.Rig.Desktop.yaw = 140f;   // the player turned (the desktop camera drives the head here)
            App.Rig.Desktop.pitch = 10f;
            yield return null;
            yield return EnterRoom("MeshLivingRoom2");
            yield return null;
            Rig piano = Rig(RigId.Piano);
            Assert.IsFalse(Mr.PianoOnTable, "not on a table");
            Assert.IsFalse(Stand.OnSurface, "on its stand");
            Assert.IsFalse(Mr.PlacedByPlayer);
            Assert.IsFalse(App.Settings.Store.HasKey(PianoAnchorStore.KeyAnchor), "the old anchor is forgotten");
            Assert.That(piano.Group.position.y, Is.EqualTo(Mr.FloorHeight + piano.HomeAll.y).Within(0.005f), "on the real floor");
            float reach = RealLayout.ReachOf(piano);
            Vector3 c = piano.Group.position - App.Head.position;
            Vector3 f = App.Head.forward;
            float bearing = Mathf.DeltaAngle(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, Mathf.Atan2(c.x, c.z) * Mathf.Rad2Deg);
            Debug.Log($"[REACH] mixed reality piano: near edge {reach:F3} m, bearing {bearing:F1} deg, floor {Mr.FloorHeight:F3}");
            Assert.That(reach, Is.LessThanOrEqualTo(LayoutModule.ReachBand + 0.005f), "within a foot");
            Assert.That(Mathf.Abs(bearing), Is.LessThan(30f), "in front of the player as they sat");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(piano.Group.eulerAngles.y, 140f)), Is.LessThan(2f), "facing the player");
        }

        [UnityTest]
        public IEnumerator TheHandsAreDrawnByDefaultAndTheMenuRowShowsTheRealOnes()
        {
            IHandLook look = App.Get<IHandLook>();
            Assert.That(look, Is.Not.Null);
            Assert.IsFalse(look.RealHandsInMr, "the default for a save without the key");
            yield return EnterRoom("MeshOffice1");
            Assert.That(App.HandInput.Look, Is.EqualTo(MetaHandVisual.Look.Skin), "drawn hands in my room by default");
            Assert.IsFalse(look.CutoutActive);
            Assert.IsTrue(App.Contacts.ClampActive);

            // The menu's Instruments screen in mixed reality has the switch (not the home page, where a stray poke flipped
            // it); poking it switches to the real hands.
            MenuModule menu = null;
            foreach (var m in App.Modules) if (m is MenuModule mm) menu = mm;
            menu.Show();
            StepFrames(10, 1f / 72f);
            MenuController ctl = menu.Controller;
            for (int i = 0; i < ctl.RowCount; i++) Assert.That(ctl.Row(i).Id, Is.Not.EqualTo(RowKind.Hands), "not on the home page");
            menu.Go(MenuScreen.Instruments);
            StepFrames(10, 1f / 72f);
            int row = -1;
            for (int i = 0; i < ctl.RowCount; i++) if (ctl.Row(i).Id == RowKind.Hands) row = i;
            Assert.That(row, Is.GreaterThanOrEqualTo(0), "a Real hands row in mixed reality");
            Assert.That(ctl.Row(row).Label, Is.EqualTo("Real hands"));
            Assert.That(ctl.Row(row).On, Is.False);
            StepFrames(20, 1f / 72f);   // past the poke debounce
            ctl.Fire(row);
            Assert.IsTrue(look.RealHandsInMr);
            Assert.That(App.HandInput.Look, Is.EqualTo(MetaHandVisual.Look.PassthroughCutout), "the real hands through the cut-out");
            Assert.IsFalse(App.Contacts.ClampActive, "the cut-out must match the real hand");
            Assert.That(App.Settings.Store.GetInt(AppSettings.KeyMrRealHands, 0), Is.EqualTo(1), "saved");
            StepFrames(2, 1f / 72f);
            Assert.That(ctl.Row(row).On, Is.True);
            Assert.That(ctl.Row(row).Note, Is.EqualTo(MenuStyle.RealHandsOnShort), "the bottom-row button's short state");

            // Leaving: the drawn hands; coming back: the choice holds.
            Mr.Exit();
            Assert.That(App.HandInput.Look, Is.EqualTo(MetaHandVisual.Look.Skin), "back in the music room");
            yield return EnterRoom("MeshOffice1");
            Assert.That(App.HandInput.Look, Is.EqualTo(MetaHandVisual.Look.PassthroughCutout), "the saved choice on the next entry");
            look.RealHandsInMr = false;
            Assert.That(App.HandInput.Look, Is.EqualTo(MetaHandVisual.Look.Skin), "and back to the drawn hands");
            Assert.IsTrue(App.Contacts.ClampActive);
        }
    }
}
#endif
