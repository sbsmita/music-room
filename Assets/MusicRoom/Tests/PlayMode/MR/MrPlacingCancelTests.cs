// Placing never traps the player (the real Layout and menu, one of MRUK's recorded rooms). While MR places the lead no
// instrument plays, so a placing nobody finishes looked like a hang. Since 2026-11-10 nothing the player does with one hand
// starts it (the still pinch in empty air that did is retired: instruments move only with Move instruments, by hand); the
// placer is a tool path (IMixedReality.StartPlacing / BeginPlacing). It ends by itself after MrModule.PlacingTimeout, and
// the headset off, input focus lost (the system menu), a recentre and the menu's summon gesture end it too. Every end
// without a landing brings back the rigs placing hid, and the placing hint stays up for as long as placing lasts.
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
    public class MrPlacingCancelTests : MrTestRig
    {
        protected override bool UseContractLayout => false;
        protected override bool TableOnEnter => false;

        protected override void Prepare(MemorySettingsStore settings) => settings.SetInt(AppSettings.KeyTourDone, 1);

        IMenuPanel Menu => App.Get<IMenuPanel>();
        Transform MenuGroup => Rig(RigId.Lessons) != null && Rig(RigId.Lessons).Group != null ? Rig(RigId.Lessons).Group : Menu.Root;

        static IEnumerator Seconds(float s)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < s) yield return null;
        }

        /// <summary>Placing started the tools' way (the right hand places).</summary>
        IEnumerator PinchAirUntilPlacing(float wait = 3f)
        {
            Mr.BeginPlacing(1);
            yield return WaitFor(() => Mr.IsPlacing, wait);
        }

        void LetGo() => Fake.SetPose(HandSide.Right, RightAt(App.Head.position + new Vector3(0f, 0.35f, 0.15f), false));

        void AssertRestored(string why)
        {
            Assert.IsFalse(Mr.IsPlacing, why + ": placing ended");
            Assert.IsTrue(Rig(RigId.Drums).Group.gameObject.activeSelf, why + ": the drums are back");
            Assert.IsTrue(Rig(RigId.Harp).Group.gameObject.activeSelf, why + ": the harp is back");
            if (Menu.Shown) Assert.IsTrue(MenuGroup.gameObject.activeSelf, why + ": the open menu is visible again");
        }

        [UnityTest]
        public IEnumerator AStillPinchInEmptyAirNeverPlacesAndPlacingTimesOutBringingBackTheRigs()
        {
            yield return EnterRoom("MeshOffice1");
            Mr.PlacingTimeout = 1.5f;
            Vector3 air = App.Head.position + new Vector3(0f, 0.35f, 0.15f);
            Fake.SetPose(HandSide.Right, RightAt(air, false));
            yield return null;
            Fake.SetPose(HandSide.Right, RightAt(air, true));
            yield return Seconds(2f);
            Assert.IsFalse(Mr.IsPlacing, "a still pinch in empty air re-places nothing any more");
            Assert.IsTrue(Rig(RigId.Drums).Group.gameObject.activeSelf, "nothing hides");
            LetGo();
            yield return Seconds(0.2f);
            yield return PinchAirUntilPlacing();
            Assert.IsTrue(Mr.IsPlacing, "the tools' placer starts");
            Assert.IsFalse(Rig(RigId.Drums).Group.gameObject.activeSelf, "the other rigs hide");
            // The placing hint outlasts MrHint.ShowSeconds' rule only when that is longer than the wait; here it must stay
            // up for the whole placing.
            yield return Seconds(0.5f);
            Assert.IsTrue(Mr.HintVisible, "the placing hint stays while placing");
            int cancels = Mr.PlacingCancels;
            yield return WaitFor(() => !Mr.IsPlacing, 4f);
            Assert.That(Mr.PlacingCancels, Is.EqualTo(cancels + 1), "the timeout ended it");
            AssertRestored("the timeout");
        }

        [UnityTest]
        public IEnumerator TheHeadsetOffFocusLossARecentreAndTheSummonGestureEndPlacing()
        {
            yield return EnterRoom("MeshOffice1");
            Mr.PlacingTimeout = 60f;
            string[] paths = { "headset off", "focus lost", "recentre" };
            for (int k = 0; k < paths.Length; k++)
            {
                yield return PinchAirUntilPlacing();
                Assert.IsTrue(Mr.IsPlacing, paths[k] + ": placing started");
                LetGo();
                yield return null;
                int cancels = Mr.PlacingCancels;
                if (k == 0) App.Events.Publish(new HmdMountEvent { Mounted = false });
                else if (k == 1) App.Events.Publish(new InputFocusEvent { Focused = false });
                else App.Events.Publish(new RecenteredEvent());
                Assert.That(Mr.PlacingCancels, Is.EqualTo(cancels + 1), paths[k] + " cancels placing");
                yield return null;
                yield return null;
                AssertRestored(paths[k]);
                if (k == 0) App.Events.Publish(new HmdMountEvent { Mounted = true });
                else if (k == 1) App.Events.Publish(new InputFocusEvent { Focused = true });
                yield return Seconds(0.2f);
            }

            // The summon gesture (an open palm toward the face for 0.5 s) still runs while placing: it ends the placing and
            // opens the menu.
            Menu.Hide();
            yield return Seconds(0.3f);
            yield return PinchAirUntilPlacing();
            Assert.IsTrue(Mr.IsPlacing, "summon: placing started");
            Vector3 head = App.Head.position;
            Vector3 wrist = head + new Vector3(0.05f, -0.17f, 0.3f);
            Fake.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, wrist, Vector3.up, (head - (wrist + new Vector3(0f, 0.07f, 0f))).normalized));
            yield return WaitFor(() => !Mr.IsPlacing, 3f);
            AssertRestored("the summon gesture");
            yield return null;
            Assert.IsTrue(Menu.Shown, "the summon opened the menu");
            Assert.IsTrue(MenuGroup.gameObject.activeSelf, "visible");
        }

        [UnityTest]
        public IEnumerator APinchAtNothingCancelsAndTheMenuComesBackVisible()
        {
            yield return EnterRoom("MeshBedroom3");
            Mr.PlacingTimeout = 60f;
            Menu.Show();
            yield return null;
            yield return PinchAirUntilPlacing();
            Assert.IsTrue(Mr.IsPlacing);
            // Aim at the ceiling (no upward surface): the next pinch has nothing to land on (the head's look is never used).
            App.Rig.Desktop.pitch = -80f;
            Vector3 wrist = App.Head.position + new Vector3(0.15f, -0.3f, 0.2f);
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, wrist + Vector3.up * 3f, 0.05f));
            yield return null;
            yield return null;
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, wrist + Vector3.up * 3f, 0f));
            yield return WaitFor(() => !Mr.IsPlacing, 2f);
            AssertRestored("a pinch at nothing");
            Assert.IsTrue(Menu.Shown && MenuGroup.gameObject.activeSelf, "the menu is open and visible: no invisible menu taking pokes");
            App.Rig.Desktop.pitch = 0f;
        }
    }
}
#endif
