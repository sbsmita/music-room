// The ghost hands' hand (the playback show: "you, earlier" playing the recording back). A jointed hand built from the same
// pieces as Sunny's demo hand (a rounded palm and capsule fingers, Guide/SunnyMeshes), but with three joints a finger so
// a finger can press one piano key while the others hover. Pure maths here (no GameObjects): the bones' poses, forward
// kinematics and a small finger IK, so the EditMode tests check every pose.
//
// The hand frame (a RIGHT hand; a left hand is the mirror image, x -> -x): origin the palm's centre, +z along the fingers,
// +y the back of the hand, +x the little finger's side (the thumb is on -x). Finger f: 0 thumb, 1 index, 2 middle,
// 3 ring, 4 little. Each finger is a chain of three segments from its base (knuckle) along the base's +z; a joint bends
// about the base's +x (positive = toward the palm). The last joint follows the middle one (DipRatio), as real fingers do.
using UnityEngine;

namespace MusicRoom.Lessons.Ghost
{
    /// <summary>A finger's pose: the spread about the knuckle (rad, + toward the little finger's side in the hand frame) and
    /// the bends of its first two joints (rad, + toward the palm); the third bends DipRatio x the second.</summary>
    public struct FingerPose
    {
        public float Yaw, A1, A2;
        public FingerPose(float yaw, float a1, float a2) { Yaw = yaw; A1 = a1; A2 = a2; }
    }

    public static class GhostHandModel
    {
        public const int Fingers = 5, Thumb = 0, Index = 1, Middle = 2, Ring = 3, Little = 4;
        public const float DipRatio = 0.7f;
        /// <summary>The second joint's range (rad): straight .. curled into a fist.</summary>
        public const float A2Max = 1.75f;

        /// <summary>The palm (a rounded box) and the forearm stub behind it (fades out along its length).</summary>
        public static readonly Vector3 PalmSize = new Vector3(0.080f, 0.026f, 0.088f);
        public static readonly Vector3 WristFrom = new Vector3(0f, -0.002f, -0.036f);
        public const float ForearmLength = 0.075f, ForearmRadius0 = 0.023f, ForearmRadius1 = 0.026f;
        /// <summary>The forearm leaves the wrist this much downward (deg), toward the player's elbow.</summary>
        public const float ForearmDroop = 14f;

        /// <summary>Each finger's base (knuckle; the thumb's root inside the palm) in the hand frame.</summary>
        public static readonly Vector3[] Base =
        {
            new Vector3(-0.032f, -0.008f, -0.008f),
            new Vector3(-0.027f, 0.002f, 0.040f),
            new Vector3(-0.009f, 0.003f, 0.044f),
            new Vector3(0.009f, 0.002f, 0.041f),
            new Vector3(0.026f, 0.000f, 0.034f),
        };
        /// <summary>The thumb's base turn: forward and inward (toward -x), its pad facing the fingers.</summary>
        public static readonly Quaternion ThumbBase = Quaternion.Euler(12f, -50f, 55f);
        /// <summary>The fingers' natural spread (rad) at rest.</summary>
        public static readonly float[] RestYaw = { 0f, -0.06f, -0.01f, 0.04f, 0.1f };

        /// <summary>Segment lengths (m), base to tip, and radii at the base and the tip.</summary>
        public static readonly float[,] Length =
        {
            { 0.042f, 0.032f, 0.027f },
            { 0.040f, 0.024f, 0.020f },
            { 0.044f, 0.028f, 0.021f },
            { 0.041f, 0.026f, 0.020f },
            { 0.036f, 0.022f, 0.019f },
        };
        public static readonly float[] Radius0 = { 0.0125f, 0.0092f, 0.0095f, 0.0090f, 0.0080f };
        public static readonly float[] Radius1 = { 0.0095f, 0.0076f, 0.0078f, 0.0074f, 0.0066f };

        /// <summary>A held tool (drumstick, mallet): its grip in the fist and its direction (hand frame), its length beyond
        /// the grip and behind it. The drumstick is the kit's (Drums StickSpec: 40 cm, gripped 13 cm from the butt).</summary>
        public static readonly Vector3 ToolGrip = new Vector3(-0.012f, -0.022f, 0.040f);
        public static readonly Vector3 ToolDir = new Vector3(-0.16f, -0.05f, 1f).normalized;
        public const float StickAhead = 0.27f, StickBehind = 0.13f, StickRadius0 = 0.0075f, StickRadius1 = 0.0045f, StickTip = 0.0062f;
        public const float MalletAhead = 0.30f, MalletBehind = 0.06f, MalletHead = 0.016f;

        /// <summary>Total length of a finger (straight).</summary>
        public static float Reach(int f) => Length[f, 0] + Length[f, 1] + Length[f, 2];

        /// <summary>A finger's base frame in the hand frame (position and turn; the thumb has its own turn).</summary>
        public static Matrix4x4 BaseMatrix(int f) => Matrix4x4.TRS(Base[f], f == Thumb ? ThumbBase : Quaternion.identity, Vector3.one);

        /// <summary>Segment k's frame (hand frame) of finger f in pose p: its origin at the joint, its +z along the bone.</summary>
        public static Matrix4x4 Segment(int f, in FingerPose p, int k)
        {
            Matrix4x4 m = BaseMatrix(f) * Matrix4x4.Rotate(Quaternion.Euler(0f, p.Yaw * Mathf.Rad2Deg, 0f)) * Matrix4x4.Rotate(Quaternion.Euler(p.A1 * Mathf.Rad2Deg, 0f, 0f));
            if (k == 0) return m;
            m = m * Matrix4x4.Translate(new Vector3(0f, 0f, Length[f, 0])) * Matrix4x4.Rotate(Quaternion.Euler(p.A2 * Mathf.Rad2Deg, 0f, 0f));
            if (k == 1) return m;
            return m * Matrix4x4.Translate(new Vector3(0f, 0f, Length[f, 1])) * Matrix4x4.Rotate(Quaternion.Euler(DipRatio * p.A2 * Mathf.Rad2Deg, 0f, 0f));
        }

        /// <summary>The fingertip (the pad's end) in the hand frame.</summary>
        public static Vector3 Tip(int f, in FingerPose p) => Segment(f, p, 2).MultiplyPoint3x4(new Vector3(0f, 0f, Length[f, 2]));

        /// <summary>A plain curl: 0 straight .. 1 a fist (the thumb bends less).</summary>
        public static FingerPose Curl(int f, float c)
        {
            c = Mathf.Clamp01(c);
            float yaw = RestYaw[f] * (1f - 0.5f * c);
            return f == Thumb ? new FingerPose(yaw, 0.45f * c, 0.85f * c) : new FingerPose(yaw, 1.25f * c, 1.6f * c);
        }

        /// <summary>The planar chain with the first joint straight: where the tip is (forward u, down d) for a second-joint
        /// bend a2. Its length falls as a2 grows (0 .. A2Max).</summary>
        static Vector2 Chain(int f, float a2)
        {
            float a3 = a2 * (1f + DipRatio);
            return new Vector2(Length[f, 0] + Length[f, 1] * Mathf.Cos(a2) + Length[f, 2] * Mathf.Cos(a3),
                Length[f, 1] * Mathf.Sin(a2) + Length[f, 2] * Mathf.Sin(a3));
        }

        /// <summary>The finger pose that puts finger f's tip at <paramref name="target"/> (hand frame), as near as the finger
        /// reaches: the spread aims the finger's plane at the target, the bends set the distance (a fixed last-joint ratio),
        /// the first joint the direction. Out of reach: the nearest (straight, or curled to the limit). Allocation-free.</summary>
        public static FingerPose Solve(int f, Vector3 target)
        {
            Vector3 p = BaseMatrix(f).inverse.MultiplyPoint3x4(target);
            float maxYaw = f == Thumb ? 1.0f : f == Little || f == Index ? 0.75f : 0.5f;
            float yaw = Mathf.Clamp(Mathf.Atan2(p.x, Mathf.Max(1e-4f, p.z)), -maxYaw, maxYaw);
            float u = Mathf.Sqrt(p.x * p.x + p.z * p.z), d = -p.y;
            float dist = Mathf.Sqrt(u * u + d * d);
            // |Chain(a2)| falls monotonically on 0..A2Max: bisect for the bend that matches the distance.
            float lo = 0f, hi = A2Max;
            if (dist >= Chain(f, 0f).magnitude) hi = 0f;
            else if (dist <= Chain(f, A2Max).magnitude) lo = A2Max;
            else
                for (int i = 0; i < 14; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (Chain(f, mid).magnitude > dist) lo = mid; else hi = mid;
                }
            float a2 = 0.5f * (lo + hi);
            Vector2 s = Chain(f, a2);
            float a1 = Mathf.Atan2(d, u) - Mathf.Atan2(s.y, s.x);
            a1 = Mathf.Clamp(a1, f == Thumb ? -1.1f : -0.6f, 1.7f);
            return new FingerPose(yaw, a1, a2);
        }

        /// <summary>Where a held tool's tip is in the hand frame (stick: the tip ball's centre; mallet: the head's centre).</summary>
        public static Vector3 ToolTip(bool mallet) => ToolGrip + ToolDir * (mallet ? MalletAhead : StickAhead);
    }
}
