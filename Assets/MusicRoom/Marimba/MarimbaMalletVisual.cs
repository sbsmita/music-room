// The DRAWN mallet (spec 4.4, the E2 sticks of 10-upgrade/experience.md 4): it pivots in the hand about the grip so its
// yarn head never sinks into a bar, and springs back off a hit; during a dead stroke or a mallet rest the drawn head stays
// pressed on the bar. StickVisualMath.Step's rule with the mallet's lever (grip to head centre 0.27 m, head r 17 mm):
//   phi = clamp(phiC + max(psi, 0), 0, phiMax) x w about LiftAxis(head - grip, n), with phiC the angle that lifts the raw
//   head onto the bar top under it (ContactMath.LiftAngle), psi the grip spring (8 Hz, zeta 0.8, 2 ms substeps) kicked after
//   a hit with omega = (1 + e) v / L, and w fading very deep raw dips out (0.30..0.45 rad) and near the normal.
// Visual only: the hit test keeps the raw head (MarimbaMallets), so what sounds is unchanged. No allocation.
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Marimba
{
    public static class MarimbaMalletVisual
    {
        public const float L = MarimbaGeometry.MalletHeadFromButt - MarimbaGeometry.MalletGripFromButt;   // 0.27 m
        public const float HeadRadius = MarimbaGeometry.MalletHeadRadius;
        public const float Restitution = 0.25f, OmegaN = 2f * Mathf.PI * 8f, Zeta = 0.8f, Substep = 0.002f;
        public const float PhiMax = 0.45f, FadeFrom = 0.30f, FadeTo = 0.45f;

        public struct State { public float Psi, Omega; public Vector3 Axis; }

        /// <summary>The bar whose strike footprint holds the point (rig-local x, z), with its top; -1 when over no bar.</summary>
        public static int BarUnder(Vector3 local, float h, out float top)
        {
            top = 0f;
            for (int b = 0; b < MarimbaGeometry.Bars; b++)
            {
                float front = MarimbaGeometry.Natural(b) ? 0f : (float)MarimbaIx.EndReach;
                if (Mathf.Abs(local.x - MarimbaGeometry.X(b)) <= MarimbaGeometry.Width(b) * 0.5f + (float)MarimbaIx.SideReach
                    && local.z >= MarimbaGeometry.NearZ(b) - front && local.z <= MarimbaGeometry.FarZ(b))
                {
                    top = MarimbaGeometry.Top(b, h);
                    return b;
                }
            }
            return -1;
        }

        /// <summary>One frame of one held mallet (world space). surfaceY: the bar top under the raw head (hasSurface false: none);
        /// n: the rig's up; hitSpeed: this mallet's stroke speed this frame (0 when it did not strike); pressed: a dead stroke
        /// or a rest holds the head on the bar (no bounce). Returns the drawn butt, rotation and head.</summary>
        public static void Step(ref State st, float dt, Vector3 grip, Vector3 butt, Quaternion rot, Vector3 head, bool hasSurface,
            Vector3 surfacePoint, Vector3 n, float hitSpeed, bool pressed, out Vector3 drawnButt, out Quaternion drawnRot, out Vector3 drawnHead)
        {
            Vector3 v = head - grip;
            float phiC = 0f;
            if (hasSurface)
            {
                float pen = HeadRadius - Vector3.Dot(n, head - surfacePoint);
                if (pen > 0f) phiC = ContactMath.LiftAngle(v, n, pen);
            }
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / Substep));
            float hs = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                st.Omega += (-OmegaN * OmegaN * st.Psi - 2f * Zeta * OmegaN * st.Omega) * hs;
                st.Psi += st.Omega * hs;
            }
            if (pressed) { st.Psi = 0f; st.Omega = 0f; }
            else if (hitSpeed > 0f) st.Omega = Mathf.Max(st.Omega, (1f + Restitution) * hitSpeed / L);
            float w = 1f - Mathf.Clamp01((phiC - FadeFrom) / (FadeTo - FadeFrom));
            if (hasSurface) w *= ContactMath.NormalWeight(v, n);
            float phi = Mathf.Min(PhiMax, Mathf.Max(0f, phiC + Mathf.Max(st.Psi, 0f))) * w;
            if (hasSurface) st.Axis = ContactMath.LiftAxis(v, n);
            if (phi > 0f && st.Axis != Vector3.zero)
            {
                Quaternion q = Quaternion.AngleAxis(phi * Mathf.Rad2Deg, st.Axis);
                drawnButt = grip + q * (butt - grip);
                drawnHead = grip + q * (head - grip);
                drawnRot = q * rot;
            }
            else { drawnButt = butt; drawnHead = head; drawnRot = rot; }
        }
    }
}
