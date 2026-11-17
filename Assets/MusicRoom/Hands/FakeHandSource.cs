// Scripted hand source for tests and replays: the test sets each hand's joints (world space) and advances a
// scripted clock; velocities come from the same SampleVelocity the headset path uses, so pinch/grasp hysteresis,
// strike speeds and hover logic run on exactly the data they see on the device.
using UnityEngine;

namespace MusicRoom.Hands
{
    /// <summary>A full 26-joint pose in world space with builders for the gestures the app cares about.</summary>
    public struct HandPose
    {
        public Vector3[] Position;
        public Quaternion[] Rotation;

        /// <summary>Open hand from the baked rest pose: wrist at <paramref name="wrist"/> with rotation
        /// <paramref name="wristRot"/> (+Z along the fingers, +Y out of the back of the hand).</summary>
        public static HandPose Open(HandSide side, Vector3 wrist, Quaternion wristRot)
        {
            var p = new HandPose { Position = new Vector3[HandFrame.JointCount], Rotation = new Quaternion[HandFrame.JointCount] };
            var local = new Vector3[HandFrame.JointCount];
            HandRestPose.Fill(side, local);
            for (int i = 0; i < HandFrame.JointCount; i++)
            {
                p.Position[i] = wrist + wristRot * local[i];
                p.Rotation[i] = wristRot;
            }
            return p;
        }

        /// <summary>Open hand, palm facing <paramref name="palmNormal"/> (out of the palm), fingers along <paramref name="fingers"/>.</summary>
        public static HandPose OpenFacing(HandSide side, Vector3 wrist, Vector3 fingers, Vector3 palmNormal)
            => Open(side, wrist, Quaternion.LookRotation(fingers, -palmNormal));

        public HandPose Translated(Vector3 delta)
        {
            var p = Clone();
            for (int i = 0; i < HandFrame.JointCount; i++) p.Position[i] += delta;
            return p;
        }

        /// <summary>Moves the whole hand so the index tip lands at <paramref name="world"/>.</summary>
        public HandPose WithIndexTipAt(Vector3 world) => Translated(world - Position[(int)HandJoint.IndexTip]);

        /// <summary>Thumb tip onto the index tip (gap 0 &lt; 15 mm: a pinch).</summary>
        public HandPose WithPinch(float gap = 0f)
        {
            var p = Clone();
            Vector3 index = p.Position[(int)HandJoint.IndexTip];
            Vector3 thumb = p.Position[(int)HandJoint.ThumbTip];
            Vector3 dir = (thumb - index).sqrMagnitude > 1e-10f ? (thumb - index).normalized : Vector3.right;
            p.Position[(int)HandJoint.ThumbTip] = index + dir * gap;
            return p;
        }

        /// <summary>Curls middle, ring and little fingers (and the index) into a fist: grasp amount about 0.09 m.</summary>
        public HandPose WithFist()
        {
            var p = Clone();
            Quaternion rot = p.Rotation[(int)HandJoint.Wrist];
            Vector3 wrist = p.Position[(int)HandJoint.Wrist];
            var local = new Vector3[HandFrame.JointCount];
            for (int i = 0; i < HandFrame.JointCount; i++) local[i] = Quaternion.Inverse(rot) * (p.Position[i] - wrist);
            HandRestPose.Curl(local, Finger.Index, HandRestPose.FistCurl);
            HandRestPose.Curl(local, Finger.Middle, HandRestPose.FistCurl);
            HandRestPose.Curl(local, Finger.Ring, HandRestPose.FistCurl);
            HandRestPose.Curl(local, Finger.Little, HandRestPose.FistCurl);
            for (int i = 0; i < HandFrame.JointCount; i++) p.Position[i] = wrist + rot * local[i];
            return p;
        }

        /// <summary>Pointing pose (middle/ring/little half-curled), the mouse hand's shape: grasp about 0.139 m.</summary>
        public HandPose WithPointing()
        {
            var p = Clone();
            Quaternion rot = p.Rotation[(int)HandJoint.Wrist];
            Vector3 wrist = p.Position[(int)HandJoint.Wrist];
            var local = new Vector3[HandFrame.JointCount];
            for (int i = 0; i < HandFrame.JointCount; i++) local[i] = Quaternion.Inverse(rot) * (p.Position[i] - wrist);
            HandRestPose.Curl(local, Finger.Middle, HandRestPose.PointCurl);
            HandRestPose.Curl(local, Finger.Ring, HandRestPose.PointCurl);
            HandRestPose.Curl(local, Finger.Little, HandRestPose.PointCurl);
            for (int i = 0; i < HandFrame.JointCount; i++) p.Position[i] = wrist + rot * local[i];
            return p;
        }

        public HandPose Clone()
        {
            var p = new HandPose { Position = (Vector3[])Position.Clone(), Rotation = (Quaternion[])Rotation.Clone() };
            return p;
        }
    }

    public sealed class FakeHandSource : IHandSource
    {
        readonly HandFrame[] _frames = { new HandFrame(HandSide.Left), new HandFrame(HandSide.Right) };
        readonly SampleVelocity[] _velocity = { new SampleVelocity(), new SampleVelocity() };
        readonly bool[] _tracked = new bool[2];
        readonly bool[] _dirty = new bool[2];
        readonly HandPose[] _pose = new HandPose[2];
        readonly Vector3[] _tips = new Vector3[5];
        readonly Vector3[] _vel = new Vector3[5];

        public string Name => "Fake";
        public bool IsAvailable => true;
        /// <summary>Scripted clock in seconds; advances by deltaTime on every Tick.</summary>
        public double Time { get; private set; }
        /// <summary>When true (default) a repeated SetPose with the same joints still counts as a new tracker sample
        /// (a runtime may repeat poses; native OpenXR predicts every frame). Set false to emulate repeated poses.</summary>
        public bool EveryTickIsASample = true;
        public float HandScale = 1f;
        public bool HighConfidence = true;
        public bool SystemGesture;

        // Meta's own pinch data and the system pointer pose (the headset's HandState.PinchStrength / Pinches / PointerPose).
        readonly float[] _strength = new float[2];
        readonly bool[] _runtimePinch = new bool[2];
        readonly bool[] _inputInvalid = new bool[2];
        readonly bool[] _hasPointer = new bool[2];
        readonly Pose[] _pointerPose = new Pose[2];

        public HandFrame Get(HandSide side) => _frames[(int)side];

        /// <summary>The runtime's index pinch strength (0..1) and its IsIndexPinching flag, as the next Ticks publish them.</summary>
        public void SetMetaPinch(HandSide side, float strength, bool runtimePinch = false)
        {
            _strength[(int)side] = strength;
            _runtimePinch[(int)side] = runtimePinch;
        }

        /// <summary>InputStateValid off: no pinch strength and no pointer pose (as on the headset).</summary>
        public void SetInputValid(HandSide side, bool valid) => _inputInvalid[(int)side] = !valid;

        /// <summary>The system pointer pose (aim ray) to publish; null = the default along the wrist from the index knuckle.</summary>
        public void SetPointer(HandSide side, Pose? pose)
        {
            _hasPointer[(int)side] = pose.HasValue;
            if (pose.HasValue) _pointerPose[(int)side] = pose.Value;
        }

        public void SetTracked(HandSide side, bool tracked) { _tracked[(int)side] = tracked; _dirty[(int)side] = true; }

        /// <summary>Installs the joints the next Tick publishes (the pose is copied).</summary>
        public void SetPose(HandSide side, in HandPose pose)
        {
            _pose[(int)side] = pose.Clone();
            _tracked[(int)side] = true;
            _dirty[(int)side] = true;
        }

        public void Tick(float deltaTime)
        {
            Time += deltaTime;
            for (int h = 0; h < 2; h++)
            {
                HandFrame f = _frames[h];
                if (!_tracked[h] || _pose[h].Position == null)
                {
                    f.MarkLost();
                    _velocity[h].Reset();
                    continue;
                }
                HandPose p = _pose[h];
                f.isTracked = true;
                f.isHighConfidence = HighConfidence;
                f.isDominant = h == 1;
                f.systemGesture = SystemGesture;
                f.poseInferred = false;
                f.handScale = HandScale;
                for (int i = 0; i < HandFrame.JointCount; i++)
                {
                    f.position[i] = p.Position[i];
                    f.rotation[i] = p.Rotation[i];
                    f.radius[i] = HandRestPose.Radius[i] * HandScale;
                }
                bool input = !_inputInvalid[h];
                for (int k = 0; k < 5; k++)
                {
                    f.fingerHighConfidence[k] = HighConfidence;
                    f.runtimePinch[k] = input && k == (int)Finger.Index && _runtimePinch[h];
                    f.pinchStrength[k] = input && k == (int)Finger.Index ? _strength[h] : 0f;
                }
                f.pointerValid = input;
                Vector3 fwd = f.rotation[(int)HandJoint.Wrist] * Vector3.forward;
                f.pointer = _hasPointer[h] ? _pointerPose[h]
                    : new Pose(f.position[(int)HandJoint.IndexProximal], Quaternion.LookRotation(fwd, f.rotation[(int)HandJoint.Wrist] * Vector3.up));
                f.sampleTime = f.displayTime = Time;
                for (int k = 0; k < 5; k++) _tips[k] = f.position[HandFrame.TipJoint[k]];
                bool sample = EveryTickIsASample || _dirty[h];
                if (sample)
                {
                    f.newSample = _velocity[h].Push(Time, _tips, _vel, ref f.sampleInterval);
                    for (int k = 0; k < 5; k++) f.tipVelocity[k] = _vel[k];
                }
                else f.newSample = false;
                _dirty[h] = false;
            }
        }
    }
}
