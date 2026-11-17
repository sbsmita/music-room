// Meta XR Core SDK 207 hand source: reads both hands straight from OVRPlugin, the same call OVRHand makes
// internally (OVRHand.cs:253-333), so there is no OVRHand component, no per-bone GameObject, no execution-order
// coupling, and the tracker's own timestamps are available. Verified to compile and not throw without a runtime (spec 02).
using UnityEngine;

namespace MusicRoom.Hands
{
    public sealed class MetaHandSource : IHandSource
    {
        const int RawGiveUpFrames = 300; // stop asking for un-extrapolated samples if the runtime never gives them

        readonly System.Func<Transform> _trackingSpace;
        readonly HandFrame[] _frames = { new HandFrame(HandSide.Left), new HandFrame(HandSide.Right) };
        readonly OVRPlugin.HandState[] _state = new OVRPlugin.HandState[2]; // predicted for this frame's display time
        readonly OVRPlugin.HandState[] _raw = new OVRPlugin.HandState[2];   // newest tracker sample, not extrapolated
        readonly SampleVelocity[] _velocity = { new SampleVelocity(), new SampleVelocity() };
        readonly Vector3[] _tips = new Vector3[5];
        readonly Vector3[] _vel = new Vector3[5];
        int _rawFailures;

        /// <param name="trackingSpace">OVRCameraRig.trackingSpace (all OVRPlugin poses are relative to it).</param>
        public MetaHandSource(System.Func<Transform> trackingSpace) { _trackingSpace = trackingSpace; }

        public string Name => "Meta";
        // OVRManagerinitialized is set after the skeleton version is applied (OVRManager.cs:2405-2409, 2471). Both checks
        // are managed-only, so this is safe in a macOS editor with no OVRPlugin native library.
        public bool IsAvailable => OVRManager.OVRManagerinitialized && OVRManager.isHmdPresent;
        public HandFrame Get(HandSide side) => _frames[(int)side];

        /// <summary>The raw OVRPlugin state behind Get(side) (same sample), used by MetaHandVisual.</summary>
        public ref OVRPlugin.HandState State(HandSide side) => ref _state[(int)side];

        /// <summary>True while GetUnextrapolatedHandState keeps succeeding (device diagnostics).</summary>
        public bool UsingRawSamples => _rawFailures < RawGiveUpFrames;

        /// <summary>The last Fast Motion Mode hint sent (RequestFastMotion). The runtime has no getter and may ignore the
        /// hint, so this is our own record, not the tracker's state.</summary>
        public bool FastMotionRequested { get; private set; }

        /// <summary>experience.md E4: hint the runtime to track hands in Fast Motion Mode (~60 Hz camera, more jitter) or
        /// the default mode (~30 Hz, low jitter). OVRPlugin.RequestFastMotionMode (ovrp 1.114, OVRPlugin.cs:13045) is
        /// independent of the manifest's com.oculus.handtracking.frequency (LOW since E4). OVRManager sends the same hint
        /// only when its fastMotionModeHandPosesEnabled field changes (OVRManager.cs:3033-3037), which nothing here
        /// touches, so the two never contradict each other. Only call while the runtime is up (RigBootstrap.IsXrActive).</summary>
        public OVRPlugin.Result RequestFastMotion(bool on)
        {
            if (!IsAvailable) return OVRPlugin.Result.Failure_NotInitialized;
            OVRPlugin.Result r = OVRPlugin.RequestFastMotionMode(on);
            if (r >= 0) FastMotionRequested = on;
            return r;
        }

        /// <summary>RequestFastMotion for callers without the Oculus.VR reference (Core's FastMotionDriver): true on
        /// success, the OVRPlugin.Result code in <paramref name="result"/>.</summary>
        public bool TryRequestFastMotion(bool on, out int result)
        {
            OVRPlugin.Result r = RequestFastMotion(on);
            result = (int)r;
            return r >= 0;
        }

        public void Tick(float deltaTime)
        {
            Transform ts = _trackingSpace != null ? _trackingSpace() : null;
            Matrix4x4 toWorld = ts != null ? ts.localToWorldMatrix : Matrix4x4.identity;
            Quaternion rot = ts != null ? ts.rotation : Quaternion.identity;
            for (int h = 0; h < 2; h++) Read(h, toWorld, rot);
        }

        void Read(int h, Matrix4x4 toWorld, Quaternion rot)
        {
            HandFrame f = _frames[h];
            OVRPlugin.Hand hand = h == 0 ? OVRPlugin.Hand.HandLeft : OVRPlugin.Hand.HandRight;

            // Joint positions only exist with the OpenXR skeleton (HandState3, OVRPlugin.cs:9639-9747). OVRManager
            // applies OVRRuntimeSettings.HandSkeletonVersion at start-up (OVRManager.cs:2405-2409).
            bool ok = OVRPlugin.HandSkeletonVersion == OVRHandSkeletonVersion.OpenXR
                      && OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref _state[h]);
            ref OVRPlugin.HandState s = ref _state[h];
            OVRPlugin.HandStatus status = ok ? s.Status : 0;
            bool tracked = ok && (status & OVRPlugin.HandStatus.HandTracked) != 0
                           && s.BonePositions != null && s.BonePositions.Length == HandFrame.JointCount;
            if (!tracked)
            {
                f.MarkLost();
                _velocity[h].Reset();
                return;
            }

            f.isTracked = true;
            f.isHighConfidence = s.HandConfidence == OVRPlugin.TrackingConfidence.High;
            f.isDominant = (status & OVRPlugin.HandStatus.DominantHand) != 0;
            f.systemGesture = (status & OVRPlugin.HandStatus.SystemGestureInProgress) != 0;
            f.handScale = s.HandScale > 0f ? s.HandScale : 1f;
            f.poseInferred = OVRPlugin.GetHandPoseSourceInferred(-1, hand, out bool inferred) == OVRPlugin.Result.Success
                             && inferred;

            // Tracking space (right-handed OpenXR data) -> Unity: FromFlippedZ* (OVRCommon.cs:463, 650), then the rig.
            for (int i = 0; i < HandFrame.JointCount; i++)
            {
                f.position[i] = toWorld.MultiplyPoint3x4(s.BonePositions[i].FromFlippedZVector3f());
                f.rotation[i] = rot * s.BoneRotations[i].FromFlippedZQuatf();
                f.radius[i] = HandRestPose.Radius[i] * f.handScale;
            }

            // InputStateValid: "the pointer pose and pinch data is usable" (OVRPlugin.cs:1766).
            bool input = (status & OVRPlugin.HandStatus.InputStateValid) != 0;
            for (int k = 0; k < 5; k++)
            {
                f.fingerHighConfidence[k] = s.FingerConfidences[k] == OVRPlugin.TrackingConfidence.High;
                f.pinchStrength[k] = input ? s.PinchStrength[k] : 0f;
                f.runtimePinch[k] = input && ((int)s.Pinches & (1 << k)) != 0;
            }
            f.pointerValid = input;
            if (input)
                f.pointer = new Pose(toWorld.MultiplyPoint3x4(s.PointerPose.Position.FromFlippedZVector3f()),
                                     rot * s.PointerPose.Orientation.FromFlippedZQuatf());

            f.displayTime = s.RequestedTimeStamp;
            f.sampleTime = s.SampleTimeStamp;

            // Velocity between distinct tracker samples, in tracking space. Prefer the newest un-extrapolated
            // sample (OpenXR runtime + ovrp >= 1.116, OVRPlugin.cs:13060-13091); otherwise the predicted pose.
            bool raw = false;
            if (_rawFailures < RawGiveUpFrames)
            {
                raw = OVRPlugin.GetUnextrapolatedHandState(hand, ref _raw[h])
                      && (_raw[h].Status & OVRPlugin.HandStatus.HandTracked) != 0
                      && _raw[h].BonePositions != null && _raw[h].BonePositions.Length == HandFrame.JointCount;
                _rawFailures = raw ? 0 : _rawFailures + 1;
            }
            ref OVRPlugin.HandState v = ref (raw ? ref _raw[h] : ref s);
            for (int k = 0; k < 5; k++)
                _tips[k] = v.BonePositions[HandFrame.TipJoint[k]].FromFlippedZVector3f();
            double t = v.SampleTimeStamp > 0 ? v.SampleTimeStamp : Time.unscaledTimeAsDouble;
            f.newSample = _velocity[h].Push(t, _tips, _vel, ref f.sampleInterval);
            for (int k = 0; k < 5; k++) f.tipVelocity[k] = rot * _vel[k];
        }
    }
}
