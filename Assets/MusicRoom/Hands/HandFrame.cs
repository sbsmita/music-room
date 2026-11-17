// Hand data model shared by every hand source (Meta runtime, desktop mouse, scripted tests).
// World space is Unity's (left-handed, +Y up, +Z forward), metres, seconds. Verified in spec 02 (02-meta-hands-src).
using System;
using UnityEngine;

namespace MusicRoom.Hands
{
    public enum HandSide { Left = 0, Right = 1 }

    public enum Finger { Thumb = 0, Index = 1, Middle = 2, Ring = 3, Little = 4 }

    /// <summary>OpenXR XrHandJointEXT order. Same integers as OVRSkeleton.BoneId.XRHand_* and
    /// OVRPlugin.BoneId.XRHand_*. (XR Hands differs: XRHandJointID.ToIndex() gives Wrist = 0, Palm = 1.)</summary>
    public enum HandJoint
    {
        Palm = 0, Wrist = 1,
        ThumbMetacarpal = 2, ThumbProximal = 3, ThumbDistal = 4, ThumbTip = 5,
        IndexMetacarpal = 6, IndexProximal = 7, IndexIntermediate = 8, IndexDistal = 9, IndexTip = 10,
        MiddleMetacarpal = 11, MiddleProximal = 12, MiddleIntermediate = 13, MiddleDistal = 14, MiddleTip = 15,
        RingMetacarpal = 16, RingProximal = 17, RingIntermediate = 18, RingDistal = 19, RingTip = 20,
        LittleMetacarpal = 21, LittleProximal = 22, LittleIntermediate = 23, LittleDistal = 24, LittleTip = 25,
    }

    /// <summary>One hand, one frame. Joint rotation axes: +Z toward the fingertip, +Y out of the back of the hand.
    /// Allocated once per hand per source; nothing here allocates per frame.</summary>
    public sealed class HandFrame
    {
        public const int JointCount = 26;
        public static readonly int[] TipJoint = { 5, 10, 15, 20, 25 }; // indexed by Finger

        public readonly HandSide side;
        public HandFrame(HandSide side) { this.side = side; }

        public bool isTracked;         // joint data usable this frame
        public bool isHighConfidence;  // whole-hand confidence is High
        public bool isDominant;
        public bool systemGesture;     // OS menu gesture in progress: ignore this hand's input
        public bool poseInferred;      // pose inferred (wide motion mode), not seen by the cameras
        public float handScale = 1f;
        public float lostSeconds;      // time since tracking was lost (0 while tracked); set by HandInput

        public readonly Vector3[] position = new Vector3[JointCount];
        public readonly Quaternion[] rotation = new Quaternion[JointCount];
        public readonly float[] radius = new float[JointCount];

        public readonly bool[] fingerHighConfidence = new bool[5];
        public readonly float[] pinchStrength = new float[5]; // runtime thumb-to-finger pinch 0..1 (Thumb slot unused)
        public readonly bool[] runtimePinch = new bool[5];

        public bool pointerValid;
        public Pose pointer = Pose.identity; // aim ray: pointer.position along pointer.forward

        public double sampleTime;     // tracker sample time, runtime clock, s (0 = unknown)
        public double displayTime;    // time the pose was predicted for, s (0 = unknown)
        public bool newSample;        // a new tracker sample arrived this frame
        public float sampleInterval;  // smoothed seconds between tracker samples
        public readonly Vector3[] tipVelocity = new Vector3[5]; // world m/s, measured between tracker samples

        public Vector3 Get(HandJoint j) => position[(int)j];
        public Vector3 Tip(Finger f) => position[TipJoint[(int)f]];
        public Pose Wrist => new Pose(position[(int)HandJoint.Wrist], rotation[(int)HandJoint.Wrist]);

        /// <summary>Keeps the last joint positions (consumers that ignore isTracked never see zeros).</summary>
        public void MarkLost()
        {
            isTracked = isHighConfidence = systemGesture = poseInferred = pointerValid = newSample = false;
            for (int k = 0; k < 5; k++)
            {
                fingerHighConfidence[k] = runtimePinch[k] = false;
                pinchStrength[k] = 0f;
                tipVelocity[k] = Vector3.zero;
            }
        }
    }

    public interface IHandSource
    {
        string Name { get; }
        bool IsAvailable { get; }
        void Tick(float deltaTime);
        HandFrame Get(HandSide side);
    }

    /// <summary>Fingertip velocity over the tracker's own sample interval (the learned gap between
    /// pose changes). Feed positions in a space that does not move with the rig (tracking space) so re-placing the
    /// player never reads as hand motion.</summary>
    public sealed class SampleVelocity
    {
        public const double MinGap = 0.004, MaxGap = 0.2; // gap floored at 4 ms; a gap over 0.2 s restarts
        readonly Vector3[] _last = new Vector3[5];
        double _lastT = double.NaN;

        public void Reset() => _lastT = double.NaN;

        /// <returns>true when <paramref name="t"/> is a new sample (velocity updated or restarted).</returns>
        public bool Push(double t, Vector3[] tips, Vector3[] vel, ref float interval)
        {
            if (double.IsNaN(_lastT) || t < _lastT || t - _lastT > MaxGap)
            {
                Array.Copy(tips, _last, 5);
                for (int k = 0; k < 5; k++) vel[k] = Vector3.zero; // a long pause is a hand at rest
                _lastT = t;
                return true;
            }
            double gap = t - _lastT;
            if (gap <= 0) return false; // same sample as last frame: keep the velocity
            float g = (float)Math.Max(gap, MinGap);
            for (int k = 0; k < 5; k++)
            {
                vel[k] = (tips[k] - _last[k]) / g;
                _last[k] = tips[k];
            }
            interval = interval > 0f ? interval + (g - interval) * 0.25f : g; // EMA, weight 0.25 per new sample
            _lastT = t;
            return true;
        }
    }

    /// <summary>Gesture helpers: pinch and grasp with hysteresis, palm, wrist yaw (spec 05 section 1).</summary>
    public static class HandMath
    {
        public const float PinchStartGap = 0.015f; // thumb-index gap: pinch starts at 15 mm (0.02 - 0.005)
        public const float PinchEndGap = 0.025f;   // and ends at 25 mm (0.02 + 0.005)
        public const float GraspClose = 0.105f;    // mean tip-to-wrist distance: grasp starts below 0.105 m
        public const float GraspOpen = 0.13f;      // and ends at 0.13 m

        public static float PinchGap(HandFrame h) =>
            Vector3.Distance(h.Get(HandJoint.ThumbTip), h.Get(HandJoint.IndexTip));

        public static Vector3 PinchPoint(HandFrame h) =>
            (h.Get(HandJoint.ThumbTip) + h.Get(HandJoint.IndexTip)) * 0.5f;

        public static bool NextPinch(bool pinching, float gap) =>
            pinching ? gap <= PinchEndGap : gap <= PinchStartGap;

        /// <summary>Meta's own index pinch (OVRPlugin HandState PinchStrength[Index] / the IsIndexPinching flag) with
        /// hysteresis: it starts at strength 0.85 and ends below 0.5. The thumb-index rule above compares the two tip
        /// JOINT centres, which sit inside the fingertips: touching tips are ~17 mm apart (the two tip radii, 7.6 + 9.8
        /// mm), so a firm pinch on the headset hovers around the 15 mm start and flickers near 25 mm. Meta's pinch is
        /// computed from the full hand model and does not.</summary>
        public const float MetaPinchStart = 0.85f, MetaPinchEnd = 0.5f;
        /// <summary>While the runtime marks the pinch data unusable (InputStateValid off) the Meta pinch is held this long.</summary>
        public const float MetaPinchHoldSeconds = 0.25f;

        public static bool NextMetaPinch(bool pinching, float strength, bool runtimePinch) =>
            runtimePinch || (pinching ? strength >= MetaPinchEnd : strength >= MetaPinchStart);

        /// <summary>The palm at <paramref name="center"/> with normal <paramref name="normal"/> (out of the palm) faces
        /// <paramref name="point"/>: within acos(<paramref name="minCos"/>) of it and closer than <paramref name="maxDistance"/>.</summary>
        public static bool PalmFacesPoint(Vector3 center, Vector3 normal, Vector3 point, float minCos, float maxDistance)
        {
            Vector3 d = point - center;
            float dist = d.magnitude;
            if (dist < 1e-4f || dist > maxDistance) return false;
            return Vector3.Dot(normal, d / dist) >= minCos;
        }

        /// <summary>Mean distance from the middle, ring and little tips to the wrist (~0.18 m open, &lt;0.1 m fist).</summary>
        public static float GraspAmount(HandFrame h)
        {
            Vector3 w = h.Get(HandJoint.Wrist);
            return (Vector3.Distance(h.Get(HandJoint.MiddleTip), w)
                  + Vector3.Distance(h.Get(HandJoint.RingTip), w)
                  + Vector3.Distance(h.Get(HandJoint.LittleTip), w)) / 3f;
        }

        public static bool NextGrasp(bool grasping, float amount) =>
            grasping ? amount < GraspOpen : amount < GraspClose;

        /// <summary>Palm centre and the direction the palm faces. In the right-handed source frame the normal is
        /// cross(indexProx - wrist, littleProx - wrist) (negated for the left hand); flipping z flips the sign of a
        /// cross product, so the operands are swapped here. Result: the normal points OUT of the palm.</summary>
        public static bool Palm(HandFrame h, out Vector3 center, out Vector3 normal)
        {
            Vector3 w = h.Get(HandJoint.Wrist), a = h.Get(HandJoint.IndexProximal), b = h.Get(HandJoint.LittleProximal);
            center = (w + a + b) / 3f;
            normal = Vector3.Cross(b - w, a - w);
            if (h.side == HandSide.Left) normal = -normal;
            float len = normal.magnitude;
            if (len < 1e-6f) return false;
            normal /= len;
            return true;
        }

        /// <summary>Wrist heading about +Y in Unity degrees (Quaternion.Euler(0, yaw, 0) * Vector3.forward points along
        /// the fingers). Deltas have the opposite sign of the right-handed (source-frame) hand yaw.</summary>
        public static float YawDegrees(HandFrame h)
        {
            Vector3 d = h.rotation[(int)HandJoint.Wrist] * Vector3.forward;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }
    }

    /// <summary>Open LEFT hand in its wrist frame (Unity axes: +Z along the fingers, +Y back of the hand, thumb at +X),
    /// composed from the baked OpenXR skeleton (02-meta-hands-joints.json restLeftWristLocal_m). Right hand: mirror x.
    /// Shared by the mouse source and the scripted test source.</summary>
    public static class HandRestPose
    {
        public static readonly Vector3[] Left =
        {
            new Vector3(0.00212f, -0.00548f, 0.06533f), new Vector3(0f, 0f, 0f),
            new Vector3(0.02803f, -0.01916f, 0.03596f), new Vector3(0.04496f, -0.03431f, 0.05922f),
            new Vector3(0.06691f, -0.04371f, 0.08312f), new Vector3(0.08582f, -0.05140f, 0.09690f),
            new Vector3(0.01872f, -0.01104f, 0.03718f), new Vector3(0.02355f, -0.00732f, 0.09600f),
            new Vector3(0.02202f, -0.01055f, 0.13375f), new Vector3(0.02069f, -0.01277f, 0.15792f),
            new Vector3(0.01794f, -0.01057f, 0.18003f),
            new Vector3(0.00251f, -0.00842f, 0.03502f), new Vector3(0.00173f, -0.00254f, 0.09565f),
            new Vector3(-0.00264f, -0.00702f, 0.13812f), new Vector3(-0.00569f, -0.00979f, 0.16536f),
            new Vector3(-0.00940f, -0.00659f, 0.18986f),
            new Vector3(-0.01499f, -0.00602f, 0.03478f), new Vector3(-0.01747f, -0.00653f, 0.08869f),
            new Vector3(-0.02676f, -0.01088f, 0.12631f), new Vector3(-0.03319f, -0.01417f, 0.15189f),
            new Vector3(-0.03848f, -0.01421f, 0.17569f),
            new Vector3(-0.02300f, -0.00942f, 0.03407f), new Vector3(-0.03505f, -0.01369f, 0.07790f),
            new Vector3(-0.04237f, -0.01812f, 0.10740f), new Vector3(-0.04895f, -0.02097f, 0.12640f),
            new Vector3(-0.05457f, -0.02119f, 0.14763f),
        };

        /// <summary>Capsule radii at HandScale 1 (Interaction SDK OVRSkeletonData.cs:65-83); tips reuse the distal radius.</summary>
        public static readonly float[] Radius =
        {
            0.02323196f, 0.01822828f,                                              // palm, wrist
            0.01838252f, 0.01028295f, 0.009768805f, 0.009768805f,                  // thumb
            0.01029526f, 0.01029526f, 0.008038102f, 0.007636196f, 0.007636196f,    // index
            0.01117394f, 0.01117394f, 0.008030958f, 0.007629411f, 0.007629411f,    // middle
            0.009922137f, 0.009922137f, 0.007611672f, 0.007231089f, 0.007231089f,  // ring
            0.008483353f, 0.008483353f, 0.006764194f, 0.006425985f, 0.006425985f,  // little
        };

        public static readonly Vector3 PointCurl = new Vector3(0f, -0.028f, 0.045f); // tip from proximal: grasp ~0.139 m
        public static readonly Vector3 FistCurl = new Vector3(0f, -0.035f, -0.010f); // grasp ~0.092 m

        /// <summary>Wrist-frame joints for one side (right = mirrored x). Writes into <paramref name="dst"/> (26).</summary>
        public static void Fill(HandSide side, Vector3[] dst)
        {
            for (int i = 0; i < HandFrame.JointCount; i++)
            {
                Vector3 p = Left[i];
                if (side == HandSide.Right) p.x = -p.x;
                dst[i] = p;
            }
        }

        /// <summary>Bends one finger from its proximal joint toward proximal + curl (wrist frame).</summary>
        public static void Curl(Vector3[] pose, Finger finger, Vector3 curl)
        {
            int tipIndex = HandFrame.TipJoint[(int)finger];
            int prox = tipIndex - 3;
            Vector3 p = pose[prox];
            Vector3 t = p + curl;
            Vector3 palmward = new Vector3(0f, -1f, 0f);
            pose[prox + 1] = Vector3.Lerp(p, t, 0.45f) + palmward * 0.010f;
            pose[prox + 2] = Vector3.Lerp(p, t, 0.75f) + palmward * 0.006f;
            pose[tipIndex] = t;
        }
    }
}
