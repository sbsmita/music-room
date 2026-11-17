// An Interaction SDK hand data source fed by the app's own hand pipeline (HandInput -> PointerInput): on Quest these are the
// same OVRPlugin.GetHandState frames every instrument plays from (MetaHandSource), in the editor's tests the FakeHandSource.
// Nothing is read from the runtime twice and the app's hands, visuals and input are untouched. Filled like the SDK's own
// OVR / OpenXR sources: the wrist as Root, the 26 OpenXR joints wrist-local, radii, confidence, the system pointer pose,
// and the runtime's per-finger pinch, with the index pinch = the app's grab pinch (the thumb-index rule OR Meta's own index
// pinch with hysteresis: HandState.GrabPinching), so an ISDK IndexPinchSelector sees exactly the pinch the rest of the app
// sees. Update mode Manual: the Gaze module marks it once per app frame. Allocation-free.
using MusicRoom.Core;
using MusicRoom.Hands;
using Oculus.Interaction.Input;
using UnityEngine;
using IsdkHandedness = Oculus.Interaction.Input.Handedness;

namespace MusicRoom.Gaze
{
    public sealed class MusicRoomHandDataSource : DataSource<HandDataAsset>
    {
        readonly HandDataAsset _data = new HandDataAsset();
        PointerInput _input;
        HandState _hand;

        protected override HandDataAsset DataAsset => _data;

        public void Bind(PointerInput input, HandState hand)
        {
            _input = input;
            _hand = hand;
            _data.Config = new HandDataSourceConfig { Handedness = hand != null && hand.Side == Core.Handedness.Left ? IsdkHandedness.Left : IsdkHandedness.Right };
        }

        protected override void UpdateData()
        {
            bool ok = _input != null && _hand != null && _hand.Connected && !_hand.IsMouse;
            _data.IsDataValid = ok;
            _data.IsConnected = ok;
            _data.IsTracked = ok;
            if (!ok)
            {
                for (int k = 0; k < _data.IsFingerPinching.Length; k++) { _data.IsFingerPinching[k] = false; _data.FingerPinchStrength[k] = 0f; }
                _data.PointerPoseOrigin = PoseOrigin.None;
                return;
            }
            HandFrame f = _input.Frame(_hand);
            Pose root = f.Wrist;
            _data.Root = root;
            _data.RootPoseOrigin = PoseOrigin.RawTrackedPose;
            Quaternion inv = Quaternion.Inverse(root.rotation);
            int n = Mathf.Min(HandFrame.JointCount, _data.JointPoses.Length);
            for (int i = 0; i < n; i++)
            {
                _data.JointPoses[i] = new Pose(inv * (f.position[i] - root.position), inv * f.rotation[i]);
                if (i < _data.JointRadii.Length) _data.JointRadii[i] = f.radius[i];
            }
            _data.IsHighConfidence = f.isHighConfidence;
            int fingers = Mathf.Min(5, _data.IsFingerPinching.Length);
            for (int k = 0; k < fingers; k++)
            {
                _data.IsFingerHighConfidence[k] = f.fingerHighConfidence[k];
                _data.FingerPinchStrength[k] = f.pinchStrength[k];
                _data.IsFingerPinching[k] = f.runtimePinch[k];
            }
            // The index pinch is the app's grab pinch (what the instruments, the menu grab and the swivel all read).
            _data.IsFingerPinching[(int)HandFinger.Index] = _hand.GrabPinching;
            _data.FingerPinchStrength[(int)HandFinger.Index] = Mathf.Max(_hand.PinchStrength, _hand.GrabPinching ? 1f : 0f);
            _data.HandScale = f.handScale;
            _data.PointerPose = f.pointer;
            _data.PointerPoseOrigin = f.pointerValid ? PoseOrigin.RawTrackedPose : PoseOrigin.None;
            _data.IsDominantHand = f.isDominant;
        }
    }
}
