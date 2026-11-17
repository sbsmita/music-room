// E2 of 10-upgrade/experience.md section 4: the DRAWN stick pivots in the hand about the grip so its tip ball never
// sinks into a head or a cymbal, and springs back off a hit. 1:1 with the experience reference engine's (Node
// prototype) stickVisualStep. The drawn angle about the grip is phi = clamp(phiC + max(psi, 0), 0, phiMax) * w:
//   phiC  the clamp angle that lifts the raw tip ball onto the surface under it (ContactMath.LiftAngle with
//         lift = TipRadius - h, h = the tip-ball centre's height over the plane);
//   psi   the bounce: omega += (-wn^2 psi - 2 zeta wn omega) h; psi += omega h in substeps h <= 2 ms, kicked AFTER the frame
//         of a hit by this stick with omega = max(omega, (1 + e) v / L), v = the approach speed the hit test measured (the
//         hit frame itself draws the stick on the head);
//   w     1 - clamp01((phiC - 0.30) / (0.45 - 0.30)): very deep raw dips fade the correction out instead of twisting the
//         stick 26 degrees in the hand; times ContactMath.NormalWeight (no turn within 15 degrees of the normal, where its
//         direction would follow tracking jitter; 1 beyond 30 degrees, so every frame of the vectors is unchanged).
// (max(psi, 0): the grip spring's small undershoot never pushes the tip below the surface while phiC holds it there, the
// invariant the PlayMode test checks; identical to the reference wherever psi >= 0, i.e. every frame of the vectors.)
// Visual only: Root and the tip / shoulder pointers keep the raw pose (DrumMath.StickPose), so the hit test is unchanged.
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Drums
{
    /// <summary>The bounce state of one stick (rad, rad/s; positive lifts the tip along the surface normal).</summary>
    public struct StickVisualState
    {
        public float Psi, Omega;
    }

    public static class StickVisualMath
    {
        /// <summary>Grip (13 cm from the butt) to the tip-ball centre (40 cm): StickSpec.Length - GripFromButt.</summary>
        public const float L = StickSpec.Length - StickSpec.GripFromButt;
        public const float TipRadius = StickSpec.TipRadius;
        /// <summary>In-hand visual restitution: a 1.05 cm tip bounce per m/s of approach, peak 21 ms after the hit.</summary>
        public const float Restitution = 0.25f;
        /// <summary>The grip spring: the return finishes before the next 16th note at 180 bpm (83 ms).</summary>
        public const float OmegaN = 2f * Mathf.PI * 8f, Zeta = 0.8f;
        public const float Substep = 0.002f;
        public const float PhiMax = 0.45f;
        public const float FadeFrom = 0.30f, FadeTo = 0.45f;

        /// <summary>One frame. Returns the drawn angle phi (rad) about <paramref name="axis"/> (normalize((tip - grip) x n);
        /// Vector3.right when there is no surface: the caller keeps its last axis then).</summary>
        public static float Step(ref StickVisualState st, float dt, Vector3 grip, Vector3 tipRaw, bool hasSurface, Vector3 surfacePoint,
            Vector3 n, float hitSpeed, out float phiC, out Vector3 axis)
        {
            Vector3 v = tipRaw - grip;
            phiC = 0f;
            if (hasSurface)
            {
                float pen = TipRadius - Vector3.Dot(n, tipRaw - surfacePoint);
                if (pen > 0f) phiC = ContactMath.LiftAngle(v, n, pen);
            }
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / Substep));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                st.Omega += (-OmegaN * OmegaN * st.Psi - 2f * Zeta * OmegaN * st.Omega) * h;
                st.Psi += st.Omega * h;
            }
            // The bounce starts AFTER the frame that drew the stick on the head (the hit frame shows the contact).
            if (hitSpeed > 0f) st.Omega = Mathf.Max(st.Omega, (1f + Restitution) * hitSpeed / L);
            float w = 1f - Mathf.Clamp01((phiC - FadeFrom) / (FadeTo - FadeFrom));
            // a stick pointing almost straight at the surface: no turn (its direction would follow tracking jitter)
            if (hasSurface) w *= ContactMath.NormalWeight(v, n);
            float phi = Mathf.Min(PhiMax, Mathf.Max(0f, phiC + Mathf.Max(st.Psi, 0f))) * w;
            axis = hasSurface ? ContactMath.LiftAxis(v, n) : Vector3.right;
            return phi;
        }

        /// <summary>The drawn tip for a drawn angle: grip + AngleAxis(phi, axis) (tip - grip).</summary>
        public static Vector3 DrawnTip(Vector3 grip, Vector3 tipRaw, Vector3 axis, float phi) =>
            phi > 0f && axis != Vector3.zero ? grip + Quaternion.AngleAxis(phi * Mathf.Rad2Deg, axis) * (tipRaw - grip) : tipRaw;
    }
}
