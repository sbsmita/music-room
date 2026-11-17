using System.Collections;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode
{
    public class RigTests
    {
        [UnityTest]
        public IEnumerator CodeOnlyRigFallsBackToTheDesktopCameraWithoutARuntime()
        {
            var rig = RigBootstrap.Ensure();
            Assert.That(rig.Rig, Is.Not.Null);
            Assert.That(rig.Head, Is.Not.Null);
            Assert.That(rig.Head.GetComponent<AudioListener>(), Is.Not.Null);
            float t0 = Time.realtimeSinceStartup;
            while (rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.That(rig.State, Is.EqualTo(XrState.Desktop));
            Assert.IsTrue(rig.Desktop.enabled);
            Assert.That(rig.Head.localPosition, Is.EqualTo(DesktopRigFallback.EyePosition));
            var cam = rig.HeadCamera;
            Assert.That(cam.fieldOfView, Is.EqualTo(DesktopRigFallback.DefaultFov).Within(1e-3f));
            Assert.That(cam.nearClipPlane, Is.EqualTo(0.02f).Within(1e-6f));
            Assert.IsFalse(XrMode.SetPassthrough(rig, true), "passthrough needs the runtime");
            Assert.IsFalse(XrMode.PassthroughOn);
            XrMode.SnapTurn(rig, 45f);
            Assert.That(rig.RigRoot.eulerAngles.y, Is.EqualTo(45f).Within(1e-3f));
            Object.Destroy(rig.gameObject);
            yield return null;
        }

        /// burst.md section 10.1 / experience.md E5: the rig boots without passthrough (no layer, OVRManager's flag off, so the
        /// VR room may run at CPU level 4); the layer is made on demand, hidden; StopPassthrough puts the flag back. Without
        /// a runtime StartPassthrough does nothing (MR simulates it in the editor). The quality ladder waits for XR.
        [UnityTest]
        public IEnumerator PassthroughIsStartedOnlyOnDemandAndTheGovernorWaitsForXr()
        {
            var rig = RigBootstrap.Ensure();
            float t0 = Time.realtimeSinceStartup;
            while (rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.That(rig.Passthrough, Is.Null, "no passthrough layer at start");
            Assert.IsFalse(rig.Manager.isInsightPassthroughEnabled, "passthrough not initialised at start");
            Assert.IsFalse(XrMode.PassthroughStartable(rig), "no runtime");
            Assert.IsFalse(XrMode.StartPassthrough(rig));
            Assert.IsFalse(XrMode.PassthroughStarted);
            Assert.That(rig.Passthrough, Is.Null, "nothing was added without a runtime");
            OVRPassthroughLayer layer = rig.EnsurePassthroughLayer();
            Assert.That(layer, Is.Not.Null);
            Assert.IsTrue(layer.hidden, "made hidden: shown only by XrMode.SetPassthrough");
            Assert.That(rig.EnsurePassthroughLayer(), Is.SameAs(layer), "one layer");
            rig.Manager.isInsightPassthroughEnabled = true;
            XrMode.StopPassthrough(rig);
            Assert.IsFalse(rig.Manager.isInsightPassthroughEnabled, "leaving MR shuts passthrough down");
            Assert.IsTrue(layer.hidden);
            Assert.That(RigBootstrap.PreferredRefreshHz, Is.EqualTo(90f));
            Assert.That(rig.Rung, Is.EqualTo(QualityRung.Hz90), "90 Hz until XR says otherwise");
            Object.Destroy(rig.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MouseHandSourceReportsOneTrackedHandInThePointingPose()
        {
            var camGo = new GameObject("Cam") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(0f, 1.2f, 0f);
            var src = new MouseHandSource(() => cam);
            src.Tick(0.016f);
            yield return null;
            var r = src.Get(HandSide.Right);
            Assert.IsTrue(r.isTracked);
            Assert.IsFalse(src.Get(HandSide.Left).isTracked);
            Assert.That(HandMath.GraspAmount(r), Is.GreaterThan(HandMath.GraspOpen), "pointing pose is not a grasp");
            Assert.That(HandMath.PinchGap(r), Is.GreaterThan(HandMath.PinchEndGap));
            Object.Destroy(camGo);
        }
    }
}
