// E6 of 10-upgrade/experience.md section 8 with a simulated headset and one of MRUK's recorded rooms (MrTestRig): in
// mixed reality the hands stay the drawn Skin hands by default (the clamp on); with "Real hands" chosen they become the
// passthrough cut-out and the visual finger clamp turns off (the hole must match the real hand), while the contact
// shadows stay at A = 0.35; leaving MR brings back the lit skin hands and the clamp.
#if UNITY_EDITOR
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.MR
{
    public class HandLookMrTests : MrTestRig
    {
        [UnityTest]
        public IEnumerator RealHandsSwitchTheMixedRealityLookAndDisableTheClamp()
        {
            Assert.That(App.HandInput.Look, Is.EqualTo(MetaHandVisual.Look.Skin), "VR: the lit skin hands");
            Assert.IsTrue(App.Contacts.ClampActive);
            Assert.That(App.Contacts.Ambient, Is.EqualTo(ContactVisuals.AmbientVr));
            Assert.That(App.HandInput.skinMaterial, Is.Not.Null, "AppRoot loads the HandSkin template");
            Assert.That(App.HandInput.cutoutMaterial, Is.Not.Null, "and the HandPassthrough template");
            Assert.That(App.HandInput.skinMaterial.shader.name, Is.EqualTo("MusicRoom/HandSkin"));
            Assert.That(App.HandInput.cutoutMaterial.renderQueue, Is.EqualTo(3999));

            yield return EnterRoom("MeshOffice1");
            Assert.That(Mr.Active, Is.True);
            Assert.That(App.HandInput.Look, Is.EqualTo(MetaHandVisual.Look.Skin), "MR: the drawn hands by default");
            Assert.IsTrue(App.Contacts.ClampActive, "the drawn fingers still stop on the keys");
            App.Get<IHandLook>().RealHandsInMr = true;   // the menu's "Real hands"
            Assert.That(App.HandInput.Look, Is.EqualTo(MetaHandVisual.Look.PassthroughCutout), "Real hands: your hands through the hole");
            Assert.IsTrue(App.Contacts.MixedReality);
            Assert.IsFalse(App.Contacts.ClampActive, "the cut-out must match the real hand: no clamp");
            Assert.That(App.Contacts.Ambient, Is.EqualTo(ContactVisuals.AmbientMr));

            // A fingertip 8 mm into a key of the piano on its real table: shadowed, never lifted.
            Transform piano = App.Get<IPianoApi>().Root;
            Quaternion playing = Quaternion.LookRotation(piano.TransformDirection(new Vector3(0f, -0.7f, 1f).normalized), piano.up);
            Fake.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, Vector3.zero, playing).WithPointing()
                .WithIndexTipAt(piano.TransformPoint(new Vector3(0.0118f, -0.008f, 0.05f))));
            yield return null;
            yield return null;
            yield return null;
            Assert.That(App.Contacts.Lift(HandSide.Right, Finger.Index), Is.EqualTo(0f));
            Assert.That(App.Contacts.ShadowCount, Is.GreaterThanOrEqualTo(2), "the shadows stay in MR");
            Assert.That(App.Contacts.Shadows.Get(0).Ambient, Is.EqualTo(ContactVisuals.AmbientMr));

            Mr.Exit();
            Assert.That(App.HandInput.Look, Is.EqualTo(MetaHandVisual.Look.Skin), "back in the music room");
            Assert.IsTrue(App.Contacts.ClampActive);
            yield return null;
            yield return null;
            Transform p2 = App.Get<IPianoApi>().Root;
            Quaternion playing2 = Quaternion.LookRotation(p2.TransformDirection(new Vector3(0f, -0.7f, 1f).normalized), p2.up);
            Fake.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, Vector3.zero, playing2).WithPointing()
                .WithIndexTipAt(p2.TransformPoint(new Vector3(0.0118f, -0.008f, 0.05f))));
            yield return null;
            yield return null;
            Assert.That(App.Contacts.Lift(HandSide.Right, Finger.Index), Is.GreaterThan(0.005f), "VR: the finger is lifted again");
        }
    }
}
#endif
