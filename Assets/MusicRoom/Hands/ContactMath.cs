// The contact clamp of 10-upgrade/experience.md section 3 (E1) as pure, allocation-free functions, 1:1 with
// the experience reference engine (liftAngle, liftAxis, fingerPenetrationLift, fingerClampStep): a rotation
// about a pivot joint that lifts an end point onto a plane. Fingers: pivot = the finger's proximal (MCP) joint, end = its
// tip joint. Sticks (Drums, E2) use LiftAngle / LiftAxis about the grip. Visual only: the play logic never reads it.
// Unity's Quaternion.AngleAxis applied to a vector equals the reference's right-hand Rodrigues rotation on the raw
// coordinates (Euler(90, 0, 0) * (0, 0, 1) = (0, -1, 0) in both), so the numbers match the vectors.
using UnityEngine;

namespace MusicRoom.Hands
{
    public static class ContactMath
    {
        /// <summary>Full correction up to 25 mm of penetration (deeper than key travel plus a typical prediction overshoot).</summary>
        public const float LiftMax = 0.025f;
        /// <summary>... fading to none at 40 mm: deeper means tracking is off, so the penetration is allowed to show.</summary>
        public const float LiftFade = 0.040f;
        /// <summary>30 degrees about the MCP joint at most (the visible flex limit before a finger looks broken).</summary>
        public const float ThetaMax = 0.5235988f;
        /// <summary>s: a lift that is no longer needed decays with this time constant (no pop at a key edge).</summary>
        public const float ReleaseTau = 0.025f;
        /// <summary>Lifts below this are zero (the reference's 1e-5 cut).</summary>
        public const float MinLift = 1e-5f;

        /// <summary>The proximal (MCP) joint of each finger, by Finger: the pivot the clamp turns the finger about.</summary>
        public static readonly int[] PivotJoint = { (int)HandJoint.ThumbProximal, (int)HandJoint.IndexProximal, (int)HandJoint.MiddleProximal, (int)HandJoint.RingProximal, (int)HandJoint.LittleProximal };

        /// <summary>The wanted lift for a penetration: 0 when not touching, the penetration up to LiftMax, then a linear fade
        /// to 0 at LiftFade.</summary>
        public static float PenetrationLift(float pen)
        {
            if (pen <= 0f || pen >= LiftFade) return 0f;
            if (pen <= LiftMax) return pen;
            return LiftMax * (LiftFade - pen) / (LiftFade - LiftMax);
        }

        /// <summary>Instant attack, exponential release (ReleaseTau): the lift this frame from last frame's and the wanted one.</summary>
        public static float SmoothLift(float lift, float want, float dt)
        {
            float l = want >= lift ? want : want + (lift - want) * Mathf.Exp(-dt / ReleaseTau);
            return l < MinLift ? 0f : l;
        }

        /// <summary>Angle about LiftAxis(v, n) (positive lifts the end toward n) that raises n . (end - pivot) by
        /// <paramref name="lift"/>; v = end - pivot.</summary>
        public static float LiftAngle(Vector3 v, Vector3 n, float lift)
        {
            float rho = v.magnitude;
            if (rho < 1e-6f || lift <= 0f) return 0f;
            float x = Vector3.Dot(n, v);
            float x1 = Mathf.Min(x + lift, rho);
            return Mathf.Acos(Mathf.Clamp(x / rho, -1f, 1f)) - Mathf.Acos(Mathf.Clamp(x1 / rho, -1f, 1f));
        }

        /// <summary>normalize(v x n): the axis about which a positive angle lifts the end toward n (zero when v is along n).</summary>
        public static Vector3 LiftAxis(Vector3 v, Vector3 n)
        {
            Vector3 a = Vector3.Cross(v, n);
            float l = a.magnitude;
            return l > 1e-12f ? a / l : Vector3.zero;
        }

        /// <summary>The clamp fades out as the finger (or stick) turns toward the surface normal: none within 15 degrees of
        /// it, all of it beyond 30 (sin 15 / sin 30). Near the normal, lifting the end along n takes a large turn that moves it
        /// mostly sideways, in a direction set by sub-degree tracking jitter: a straight poke at the menu drew the fingertip
        /// 2-5 cm off the real poke point and flipped it from side to side.</summary>
        public const float NormalFadeFrom = 0.2588190f, NormalFadeTo = 0.5f;

        /// <summary>The weight of the clamp for v = end - pivot against the unit normal n: 0 within 15 degrees of n (or of -n),
        /// 1 beyond 30 degrees (s = |v x n| / |v|, the sine of the angle).</summary>
        public static float NormalWeight(Vector3 v, Vector3 n)
        {
            float rho = v.magnitude;
            float s = rho > 1e-6f ? Vector3.Cross(v, n).magnitude / rho : 0f;
            return Mathf.Clamp01((s - NormalFadeFrom) / (NormalFadeTo - NormalFadeFrom));
        }

        /// <summary>The finger rotation for a lift (fingerClampStep without the state): theta capped at ThetaMax and faded out
        /// near the normal (NormalWeight), the axis, false when there is nothing to do.</summary>
        public static bool FingerRotation(Vector3 pivot, Vector3 tip, Vector3 n, float lift, out float theta, out Vector3 axis)
        {
            Vector3 v = tip - pivot;
            axis = LiftAxis(v, n);
            theta = Mathf.Min(LiftAngle(v, n, lift), ThetaMax) * NormalWeight(v, n);
            if (axis == Vector3.zero || theta <= 0f) { theta = 0f; return false; }
            return true;
        }

        /// <summary>The drawn hands (MetaHandVisual, DebugHandVisual) turn a finger at most this far (20 degrees): past it a
        /// finger bent back at the knuckle read as broken on the headset ("fingers wrong"), and a deeper miss of the tracker is
        /// better shown than hidden.</summary>
        public const float DrawnThetaMax = 0.3490659f;
        /// <summary>s: the drawn turn eases in with this time constant (no one-frame snap of a finger when the tracker dips
        /// into a key) and out with DrawnReleaseTau (no pop at a key edge).</summary>
        public const float DrawnAttackTau = 0.02f, DrawnReleaseTau = 0.05f;

        /// <summary>The drawn finger's turn this frame (radians) from last frame's and the wanted one: eased in (DrawnAttackTau)
        /// and out (DrawnReleaseTau), capped at DrawnThetaMax, 0 below 0.01 degree.</summary>
        public static float SmoothTheta(float theta, float want, float dt)
        {
            want = Mathf.Clamp(want, 0f, DrawnThetaMax);
            float tau = want > theta ? DrawnAttackTau : DrawnReleaseTau;
            float t = dt > 0f ? want + (theta - want) * Mathf.Exp(-dt / tau) : theta;
            return t < 1.7e-4f ? 0f : t;
        }

        /// <summary>FingerRotation with a lower cap (the drawn hands: DrawnThetaMax).</summary>
        public static bool FingerRotation(Vector3 pivot, Vector3 tip, Vector3 n, float lift, float maxTheta, out float theta, out Vector3 axis)
        {
            if (!FingerRotation(pivot, tip, n, lift, out theta, out axis)) return false;
            theta = Mathf.Min(theta, maxTheta);
            return true;
        }

        /// <summary>A point turned about <paramref name="pivot"/> by theta (radians) about a unit axis.</summary>
        public static Vector3 RotateAbout(Vector3 point, Vector3 pivot, Vector3 axis, float theta) =>
            pivot + Quaternion.AngleAxis(theta * Mathf.Rad2Deg, axis) * (point - pivot);

        /// <summary>One finger, one frame, exactly as the reference fingerClampStep: updates <paramref name="lift"/> (state per
        /// hand x finger) and <paramref name="normal"/> (kept while the surface is gone so the release has a direction) and
        /// returns the drawn tip. hasSurface false = the finger slid off (the lift decays).</summary>
        public static Vector3 ClampStep(ref float lift, ref Vector3 normal, float dt, Vector3 pivot, Vector3 tip, Vector3 surfacePoint,
            Vector3 n, float rc, bool hasSurface, out float theta, out Vector3 axis)
        {
            float want = 0f;
            if (hasSurface) want = PenetrationLift(rc - Vector3.Dot(n, tip - surfacePoint));
            lift = SmoothLift(lift, want, dt);
            if (hasSurface) normal = n;
            if (!FingerRotation(pivot, tip, normal, lift, out theta, out axis)) return tip;
            return RotateAbout(tip, pivot, axis, theta);
        }
    }
}
