// The ghost hand's world-anchored demos for the first five minutes (Docs/11-first-five/sunny-tour.md 7.3): the hand
// plays AT the target (the key, the snare, the strings, a menu row, a deck button, a held instrument) while Sunny stays
// where the player looks. Pure functions of the loop time (3 s) like SunnyDemo, posed in world space and written into
// Sunny's frame (position, rotation, scale) exactly as SunnyDemo.EvaluatePress does, so the merged demo mesh and its draw
// budget are unchanged.
//
// The hand's own frame (SunnyMeshes, SunnyDemo): +y along the fingers, -z the palm's normal, +x toward the thumb.
//   Press    index out, the rest curled; two presses onto the anchor along -up (keys, rows, deck buttons, strings)
//   Strike   fingers together, two quick taps 6 cm down onto the anchor (a drum head)
//   Sweep    an open hand, fingers into the string plane, sliding across it along Along (the harp)
//   Pinch    thumb and index close on the anchor at 0.6 s and hold to 1.6 s (a stick, the hi-hat stand, a case)
//   Point    the index points at the anchor from in front of Sunny, then pinches at 1.0 s (a far instrument)
//   PinchPull a pinch in the air in front of Sunny, then a 14 cm pull to the side (turning the seat)
//   Lift     a pinched hand at the anchor rises 6 cm and comes back (setting the height)
//   Twist    a pinched hand at the anchor rolls 40 degrees and back (turning an instrument)
//   Hold     a flat hand settles on the anchor for a second (stopping a cymbal)
//   PalmRest a flat palm settles onto the strings for a second (quieting the harp)
//   Drag     the index touches the anchor and drags 6 cm down along -Along (a list)
// Every demo fades in over its first 0.25 s and out over its last 0.3 s, so a restart never jumps.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Guide
{
    /// <summary>Where a world demo plays: the point, the surface's up (out of a key, a head, a menu face), and a direction
    /// along it (the harp's string row, a list's up).</summary>
    public struct DemoAnchor
    {
        public bool Has;
        public Vector3 Point, Up, Along;
        public static DemoAnchor At(Vector3 point, Vector3 up, Vector3 along = default) =>
            new DemoAnchor { Has = true, Point = point, Up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up, Along = along };
    }

    public static class SunnyDemoWorld
    {
        public const float Loop = 3f;
        public const float StrikeLift = 0.06f, PinchApproach = 0.05f, LiftHeight = 0.06f, TwistDeg = 40f, PullDistance = 0.14f;
        public const float SweepHalf = 0.12f, SweepTime = 0.4f, DragDistance = 0.06f;
        public static readonly float[] StrikeStarts = { 0.3f, 0.9f };
        public static readonly float[] SweepStarts = { 0.3f, 1.5f };

        /// <summary>The index tip and the middle fingertip in the hand's frame.</summary>
        public static Vector3 IndexTip => SunnyDemo.IndexTip;
        public static Vector3 MiddleTip => new Vector3(SunnyDemo.FingerX[1], SunnyDemo.FingerPivotY + SunnyDemo.FingerLen[1], 0f);
        /// <summary>Where the thumb and index meet in a pinch (between the two tips).</summary>
        public static readonly Vector3 PinchPoint = new Vector3(0.036f, 0.085f, -0.012f);

        /// <summary>True for the kinds this class poses (the others are SunnyDemo's, in Sunny's own frame).</summary>
        public static bool IsWorld(DemoKind k) =>
            k == DemoKind.Press || k == DemoKind.Strike || k == DemoKind.Sweep || k == DemoKind.Pinch || k == DemoKind.Point
            || k == DemoKind.PinchPull || k == DemoKind.Lift || k == DemoKind.Twist || k == DemoKind.Hold || k == DemoKind.PalmRest
            || k == DemoKind.Drag;

        /// <summary>The pose at loop time t. sunnyPos / sunnyRot / sunnyScale: Sunny's frame this frame; eye: the player's.</summary>
        public static void Evaluate(DemoKind kind, float t, in DemoAnchor a, Vector3 sunnyPos, Quaternion sunnyRot, float sunnyScale, Vector3 eye, ref DemoPose p)
        {
            p.Visible = true;
            p.Wide = true;
            p.Ring = p.Ghost = p.Arrow = false;
            float fade = t < 0.25f ? t / 0.25f : t > 2.7f ? Mathf.Max(0f, (3f - t) / 0.3f) : 1f;
            p.Opacity = SunnyDemo.Opacity * fade;
            Vector3 up = a.Up.sqrMagnitude > 1e-8f ? a.Up.normalized : Vector3.up;
            Vector3 away = Vector3.ProjectOnPlane(a.Point - eye, up);
            if (away.sqrMagnitude < 1e-8f) away = Vector3.ProjectOnPlane(sunnyRot * Vector3.forward, up);
            if (away.sqrMagnitude < 1e-8f) away = Vector3.forward;
            away.Normalize();
            Vector3 side = Vector3.Cross(up, away);   // the player's right, in the surface
            Vector3 front = sunnyPos + sunnyRot * (SunnyDemo.HandRoot * Mathf.Max(1e-4f, sunnyScale));   // the hand's usual spot by Sunny
            switch (kind)
            {
                case DemoKind.Press:
                {
                    // SunnyDemo.EvaluatePress's motion; on a face that stands up (a menu row, the start panel) the hand
                    // comes in palm down like a finger on a lift button, else the back of the hand faces the player.
                    float push = Taps(t, SunnyDemo.PressStarts, 0.7f);
                    Vector3 palm = Mathf.Abs(up.y) < 0.5f ? Vector3.ProjectOnPlane(Vector3.down, up) : away;
                    if (palm.sqrMagnitude < 1e-8f) palm = away;
                    Vector3 tip = a.Point + up * (SunnyDemo.PressTouch + (SunnyDemo.PressLift - SunnyDemo.PressTouch) * (1f - push));
                    Curls(ref p, 0f, 0.9f, 0.5f);
                    Place(ref p, tip, IndexTip, Frame(-up, palm.normalized), sunnyPos, sunnyRot, sunnyScale);
                    break;
                }
                case DemoKind.Strike:
                {
                    float push = Taps(t, StrikeStarts, 0.25f);
                    Vector3 tip = a.Point + up * (0.004f + (StrikeLift - 0.004f) * (1f - push));
                    Vector3 fingers = (-up * 0.75f + away * 0.66f).normalized;
                    Curls(ref p, 0.1f, 0.15f, 0.35f);
                    Place(ref p, tip, MiddleTip, Frame(fingers, -up), sunnyPos, sunnyRot, sunnyScale);
                    break;
                }
                case DemoKind.Sweep:
                {
                    Vector3 along = a.Along.sqrMagnitude > 1e-8f ? a.Along.normalized : side;
                    float x = -1f, vis = 0f;
                    for (int i = 0; i < SweepStarts.Length; i++)
                    {
                        float u = (t - SweepStarts[i]) / SweepTime;
                        if (u >= -0.5f && u < 1.6f) { x = -1f + 2f * SunnyDemo.Ease(Mathf.Clamp01(u)); vis = u < 0f ? (u + 0.5f) * 2f : u > 1f ? 1f - (u - 1f) / 0.6f : 1f; }
                    }
                    p.Opacity *= vis;
                    // The fingertips brush through the plane of the strings (the anchor), the palm toward the motion.
                    Vector3 toward = -Vector3.ProjectOnPlane(away, along).normalized;
                    Vector3 tip = a.Point + along * (SweepHalf * x) - toward * 0.005f;
                    Vector3 fingers = (-toward * 0.8f - Vector3.up * 0.6f).normalized;
                    Curls(ref p, 0.05f, 0.1f, 0.2f);
                    // The palm half toward the motion, half into the strings: the back of the hand reads from the seat.
                    Place(ref p, tip, MiddleTip, Frame(fingers, (along * 0.5f - toward * 0.85f).normalized), sunnyPos, sunnyRot, sunnyScale);
                    break;
                }
                case DemoKind.Pinch:
                {
                    float close = t < 0.3f ? 0f : t < 0.6f ? SunnyDemo.Ease((t - 0.3f) / 0.3f) : t < 1.6f ? 1f : t < 1.9f ? 1f - SunnyDemo.Ease((t - 1.6f) / 0.3f) : 0f;
                    Vector3 at = a.Point + (up * 0.6f - away * 0.8f) * (PinchApproach * (1f - close));
                    Vector3 fingers = (-up * 0.55f + away * 0.83f).normalized;
                    Curls(ref p, 0.15f + 0.5f * close, 0.3f + 0.6f * close, 0.25f + 0.65f * close);
                    Place(ref p, at, PinchPoint, Frame(fingers, -up), sunnyPos, sunnyRot, sunnyScale);
                    break;
                }
                case DemoKind.Point:
                {
                    float pinch = t < 1.0f ? 0f : t < 1.2f ? SunnyDemo.Ease((t - 1.0f) / 0.2f) : t < 2.0f ? 1f : t < 2.2f ? 1f - SunnyDemo.Ease((t - 2.0f) / 0.2f) : 0f;
                    Vector3 dir = a.Point - front;
                    if (dir.sqrMagnitude < 1e-6f) dir = sunnyRot * Vector3.forward;
                    dir.Normalize();
                    Vector3 down = Vector3.ProjectOnPlane(Vector3.down, dir);
                    Curls(ref p, 0.45f * pinch, 0.9f, 0.5f + 0.4f * pinch);
                    Place(ref p, front + dir * 0.02f, IndexTip, Frame(dir, down.sqrMagnitude > 1e-6f ? down.normalized : -up), sunnyPos, sunnyRot, sunnyScale);
                    break;
                }
                case DemoKind.PinchPull:
                {
                    float close = t < 0.2f ? 0f : t < 0.45f ? SunnyDemo.Ease((t - 0.2f) / 0.25f) : t < 1.6f ? 1f : t < 1.8f ? 1f - SunnyDemo.Ease((t - 1.6f) / 0.2f) : 0f;
                    float pull = t < 0.6f ? 0f : t < 1.2f ? SunnyDemo.Ease((t - 0.6f) / 0.6f) : t < 1.8f ? 1f : 1f - SunnyDemo.Ease(Mathf.Clamp01((t - 1.8f) / 0.8f));
                    Vector3 right = sunnyRot * Vector3.right;
                    Vector3 at = front + right * (PullDistance * pull);
                    Vector3 fwd = sunnyRot * Vector3.forward;
                    Curls(ref p, 0.15f + 0.5f * close, 0.3f + 0.6f * close, 0.25f + 0.65f * close);
                    Place(ref p, at, PinchPoint, Frame(fwd, Vector3.down), sunnyPos, sunnyRot, sunnyScale);
                    break;
                }
                case DemoKind.Lift:
                case DemoKind.Twist:
                {
                    float k = t < 0.4f ? 0f : t < 1.2f ? SunnyDemo.Ease((t - 0.4f) / 0.8f) : t < 1.6f ? 1f : t < 2.4f ? 1f - SunnyDemo.Ease((t - 1.6f) / 0.8f) : 0f;
                    Vector3 fingers = (-up * 0.55f + away * 0.83f).normalized;
                    Quaternion frame = Frame(fingers, -up);
                    Vector3 at = a.Point;
                    if (kind == DemoKind.Lift) at += up * (LiftHeight * k);
                    else frame = Quaternion.AngleAxis(TwistDeg * k, up) * frame;
                    Curls(ref p, 0.65f, 0.9f, 0.9f);
                    Place(ref p, at, PinchPoint, frame, sunnyPos, sunnyRot, sunnyScale);
                    break;
                }
                case DemoKind.Hold:
                case DemoKind.PalmRest:
                {
                    float rest = t < 0.3f ? 0f : t < 0.7f ? SunnyDemo.Ease((t - 0.3f) / 0.4f) : t < 1.7f ? 1f : t < 2.1f ? 1f - SunnyDemo.Ease((t - 1.7f) / 0.4f) : 0f;
                    Curls(ref p, 0.02f, 0.02f, 0.15f);
                    if (kind == DemoKind.Hold)
                    {
                        // Flat fingers come down onto the anchor (a cymbal's edge), palm down.
                        Vector3 tip = a.Point + up * (0.005f + 0.05f * (1f - rest));
                        Place(ref p, tip, MiddleTip, Frame(away, -up), sunnyPos, sunnyRot, sunnyScale);
                    }
                    else
                    {
                        // A flat palm settles against the strings' plane (the anchor), fingers up along them.
                        Vector3 toward = -away;
                        Vector3 palm = a.Point + toward * (0.006f + 0.06f * (1f - rest));
                        Place(ref p, palm, Vector3.zero, Frame(Vector3.up, away), sunnyPos, sunnyRot, sunnyScale);
                    }
                    break;
                }
                case DemoKind.Drag:
                {
                    Vector3 along = a.Along.sqrMagnitude > 1e-8f ? a.Along.normalized : Vector3.up;
                    float touch = t < 0.3f ? 0f : t < 0.5f ? SunnyDemo.Ease((t - 0.3f) / 0.2f) : t < 1.5f ? 1f : t < 1.7f ? 1f - SunnyDemo.Ease((t - 1.5f) / 0.2f) : 0f;
                    float drag = t < 0.6f ? 0f : t < 1.4f ? SunnyDemo.Ease((t - 0.6f) / 0.8f) : t < 1.7f ? 1f : 0f;
                    Vector3 tip = a.Point - along * (DragDistance * drag) + up * (0.004f + 0.04f * (1f - touch));
                    Curls(ref p, 0f, 0.9f, 0.5f);
                    Place(ref p, tip, IndexTip, Frame(-up, away), sunnyPos, sunnyRot, sunnyScale);
                    break;
                }
                default:
                    p.Visible = false;
                    break;
            }
        }

        static float Taps(float t, float[] starts, float dur)
        {
            for (int i = 0; i < starts.Length; i++)
            {
                float u = (t - starts[i]) / dur;
                if (u >= 0f && u < 1f) return Mathf.Sin(u * Mathf.PI);
            }
            return 0f;
        }

        static void Curls(ref DemoPose p, float index, float fingers, float thumb)
        {
            p.IndexCurl = index;
            p.FingerCurl = fingers;
            p.ThumbCurl = thumb;
        }

        /// <summary>The hand's rotation in the world: its fingers (+y) along <paramref name="fingers"/>, its palm (-z) facing
        /// <paramref name="palm"/> (made perpendicular to the fingers).</summary>
        public static Quaternion Frame(Vector3 fingers, Vector3 palm)
        {
            Vector3 f = fingers.sqrMagnitude > 1e-8f ? fingers.normalized : Vector3.up;
            Vector3 back = Vector3.ProjectOnPlane(-palm, f);
            if (back.sqrMagnitude < 1e-8f) back = Vector3.ProjectOnPlane(Vector3.forward, f);
            if (back.sqrMagnitude < 1e-8f) back = Vector3.ProjectOnPlane(Vector3.right, f);
            return Quaternion.LookRotation(back.normalized, f);
        }

        /// <summary>Puts the hand so that its point <paramref name="local"/> (hand frame, at Sunny's scale) sits at
        /// <paramref name="world"/>, and writes the pose in Sunny's frame (the view applies every demo there).</summary>
        static void Place(ref DemoPose p, Vector3 world, Vector3 local, Quaternion hand, Vector3 sunnyPos, Quaternion sunnyRot, float sunnyScale)
        {
            float s = sunnyScale > 1e-4f ? sunnyScale : 1f;
            Vector3 origin = world - hand * local * s;
            Quaternion inv = Quaternion.Inverse(sunnyRot);
            p.HandPosition = inv * (origin - sunnyPos) / s;
            p.HandRotation = inv * hand;
        }
    }
}
