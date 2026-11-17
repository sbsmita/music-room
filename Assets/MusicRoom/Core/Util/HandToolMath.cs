// Held-tool geometry shared by every tool (platform.md 4.6; marimba R4): the kit's sticks, the marimba's mallets, the
// bowls' stick use the same grip rule. Moved from Drums/DrumMath (which forwards with the stick's constants, so the kit's
// numbers and tests are unchanged). Allocation-free.
using UnityEngine;

namespace MusicRoom.Core
{
    public static class HandToolMath
    {
        /// <summary>Distance from a point to the segment a-b (the stick shaft distance).</summary>
        public static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0f ? Vector3.Dot(ab, p - a) / len2 : 0f;
            t = Mathf.Clamp01(t);
            return Vector3.Distance(a + ab * t, p);
        }

        /// <summary>The stick grip in the mirror-safe form of spec 05 section 4: the grip is the thumb/index midpoint,
        /// forward runs from the wrist to it, up is the back of the hand, and the tool droops by <paramref name="tiltRad"/>
        /// (the kit's -0.22 rad) toward the palm. Returns the tool axis (butt to tip) and the grip point.</summary>
        public static void Grip(Vector3 wrist, Quaternion wristRot, Vector3 thumbTip, Vector3 indexTip, float tiltRad, out Vector3 grip, out Vector3 axis)
        {
            grip = (thumbTip + indexTip) * 0.5f;
            Vector3 fwd = grip - wrist;
            fwd = fwd.sqrMagnitude > 1e-12f ? fwd.normalized : wristRot * Vector3.forward;
            Vector3 up = wristRot * Vector3.up;                      // dorsal: out of the back of the hand
            Vector3 uP = up - fwd * Vector3.Dot(up, fwd);
            if (uP.sqrMagnitude < 1e-12f) uP = wristRot * Vector3.up; // degenerate (fingers along the back normal)
            uP.Normalize();
            // Rotating fwd about side = fwd x up by the tilt gives fwd cos + uP sin: no cross product, so the right-handed -> Unity
            // mirror cannot flip the droop's direction.
            float c = Mathf.Cos(tiltRad), s = Mathf.Sin(tiltRad);
            axis = (fwd * c + uP * s).normalized;
        }

        /// <summary>World pose of a held tool from its grip and axis: butt = grip - axis * gripFromButt, +y along the axis,
        /// tip = butt + axis * length, and a point at <paramref name="pointFromButt"/> along it (the stick's shoulder).</summary>
        public static void Pose(Vector3 grip, Vector3 axis, float gripFromButt, float length, float pointFromButt,
            out Vector3 butt, out Quaternion rotation, out Vector3 tip, out Vector3 point)
        {
            butt = grip - axis * gripFromButt;
            rotation = Quaternion.FromToRotation(Vector3.up, axis);
            tip = butt + axis * length;
            point = butt + axis * pointFromButt;
        }

        /// <summary>The settle easing k = 1 - (1 - settle)^3 (each frame lerps toward the rest pose).</summary>
        public static float SettleEase(float settle)
        {
            float u = 1f - settle;
            return 1f - u * u * u;
        }
    }
}
