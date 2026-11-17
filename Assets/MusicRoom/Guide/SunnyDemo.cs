// The ghost hand's motion (Docs/08-ux-lessons-menu-guide.md 3.2) as a pure function of the loop time: the demo reads the same at any frame
// rate, and the tests can check every pose without GameObjects. Values are in Sunny's own Unity frame (spec 08
// section 0: local +z points away from the player, +x is the player's right), so design-frame z offsets appear negated.
// The platform's demo kinds (12-new-instruments/platform.md 8.3; Core DemoKind's comment describes each) are keyframed
// presets at a world anchor (IGuideAnchors: a point and its up, the up's length a radius when it is under 0.95 m), plus
// Custom (TipSpec.Motion keys): EvaluateAnchored poses the hand in the anchor's frame (y up, z toward the player's head,
// x to their right) and writes it in Sunny's frame like the deck walkthrough's press. The tour's six are unchanged.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Guide
{
    /// <summary>One frame of the demo: what SunnyView applies to the hand, ring, ghost panel and arrow.</summary>
    public struct DemoPose
    {
        /// <summary>The demo group and the hand (false for the nod: Sunny nods itself, and outside a step).</summary>
        public bool Visible;
        public Vector3 HandPosition;
        public Quaternion HandRotation;
        /// <summary>Curl amounts k: fingers Euler(-1.5k rad, 0, 0), thumb Euler(-0.9k rad, 0, -1 rad).</summary>
        public float IndexCurl, FingerCurl, ThumbCurl;
        /// <summary>The shared hand material's opacity (0.5; the swipe fades it in and out).</summary>
        public float Opacity;
        public bool Ring;
        public float RingScale, RingOpacity;
        public bool Ghost;
        public float RowOpacity;
        public bool Arrow;
        /// <summary>The hand reaches far from Sunny (the press demo): the view widens the demo mesh's culling bounds.</summary>
        public bool Wide;
    }

    public static class SunnyDemo
    {
        // Geometry (Docs/08-ux-lessons-menu-guide.md 3.1-3.2).
        public static readonly Vector3 HandRoot = new Vector3(0.07f, -0.05f, -0.25f);   // design frame (0.07, -0.05, +0.25)
        public const float Opacity = 0.5f;
        public static readonly Vector3 PalmBox = new Vector3(0.075f, 0.085f, 0.022f);
        public const float FingerRadius = 0.0085f, FingerPivotY = 0.0425f;
        /// <summary>Index, middle, ring, little. Mirrored from the first design (-0.028, -0.009, 0.01, 0.029), which put the index on
        /// the far side from the thumb (spec 08 section 8 item 2): here the index sits next to the thumb.</summary>
        public static readonly float[] FingerX = { 0.028f, 0.009f, -0.01f, -0.029f };
        public static readonly float[] FingerLen = { 0.065f, 0.072f, 0.066f, 0.052f };
        public static readonly Vector3 ThumbPivot = new Vector3(0.044f, -0.015f, -0.004f);
        public const float ThumbLen = 0.05f, ThumbLeanZRad = -1.0f;
        public const float CurlFingerRad = 1.5f, CurlThumbRad = 0.9f;
        /// <summary>Capsule(r, max(0.01, len - 0.017)): the straight part, so the whole capsule is len long.</summary>
        public static float StraightLength(float len) => Mathf.Max(0.01f, len - 0.017f);

        public const float RingInner = 0.028f, RingOuter = 0.036f;
        public const int RingSegments = 40;
        public static readonly Vector3 RingLocal = new Vector3(0f, 0f, -0.04f);           // just off the palm, toward the player

        public static readonly Vector3 GhostPos = new Vector3(0.07f, -0.02f, -0.08f);    // HandRoot + (0, .03, .17)
        public const float GhostW = 0.13f, GhostH = 0.095f, GhostOpacity = 0.6f;
        public static readonly float[] GhostRowY = { 0.0255f, -0.0005f, -0.0265f };        // ph/2 - 0.022 - i * 0.026
        public const float GhostRowHalf = 0.053f;                                         // pw/2 - 0.012
        public static readonly Vector2 RowPlate = new Vector2(0.11f, 0.018f);
        public static readonly Vector3 RowPlatePos = new Vector3(0f, 0.0125f, -0.001f);
        public const float RowOpacityRest = 0.1f;

        public static readonly Vector3 ArrowPos = new Vector3(0.07f, -0.125f, -0.25f);   // HandRoot + (0, -.075, 0)
        public const float DashFrom = 0.15f, DashTo = -0.13f, Dash = 0.01f, DashGap = 0.012f, DashOpacity = 0.8f;
        public const float ConeR = 0.012f, ConeH = 0.03f, ConeX = -0.155f;
        public const int ConeSides = 10;

        // Motion (Docs/08-ux-lessons-menu-guide.md 3.2).
        public const float SweepDist = 0.3f, SweepT = 0.25f;
        public static readonly float[] PokeStarts = { 0.3f, 1.2f };
        public static readonly float[] SweepStarts = { 0.2f, 1.5f };

        /// <summary>Smoothstep ease(u) = u^2 (3 - 2u).</summary>
        public static float Ease(float u) => u * u * (3f - 2f * u);

        static float Clamp01(float u) => u < 0f ? 0f : u > 1f ? 1f : u;

        public static Quaternion FingerRotation(float k) => Quaternion.Euler(-CurlFingerRad * k * Mathf.Rad2Deg, 0f, 0f);

        /// <summary>The thumb leans -1 rad about z (its pivot) and curls about x: Euler order XYZ = Rx * Rz, which
        /// Unity's Euler(x, 0, z) also is.</summary>
        public static Quaternion ThumbRotation(float k) => Quaternion.Euler(-CurlThumbRad * k * Mathf.Rad2Deg, 0f, ThumbLeanZRad * Mathf.Rad2Deg);

        /// <summary>The pose at loop time t = demoT % 3.</summary>
        public static void Evaluate(DemoKind kind, float t, ref DemoPose p)
        {
            p.Visible = kind != DemoKind.None && kind != DemoKind.Nod;
            p.Wide = false;
            p.HandPosition = HandRoot;
            p.HandRotation = Quaternion.identity;
            p.Opacity = Opacity;
            p.Ring = false;
            p.Ghost = false;
            p.Arrow = false;
            p.IndexCurl = p.FingerCurl = p.ThumbCurl = 0f;
            switch (kind)
            {
                case DemoKind.Palm:
                {
                    // Palm up and out (fingers away), turning to face the player over 0.6 s, held 0.5 s with the charging
                    // ring, then turned back; at rest until the loop comes round.
                    float turn;
                    if (t < 0.6f) turn = Ease(Clamp01(t / 0.6f));
                    else if (t < 1.1f) turn = 1f;
                    else if (t < 1.6f) turn = 1f - Ease(Clamp01((t - 1.1f) / 0.5f));
                    else turn = 0f;
                    // design-frame x rotation -pi/2 (1 - turn): mirrored, so +90 degrees here.
                    p.HandRotation = Quaternion.Euler(90f * (1f - turn), 0f, 0f);
                    p.HandPosition.y = HandRoot.y - 0.04f * (1f - turn);
                    if (t >= 0.6f && t < 1.1f)
                    {
                        float k = (t - 0.6f) / 0.5f;
                        p.Ring = true;
                        p.RingScale = 0.5f + 0.5f * k;
                        p.RingOpacity = 0.15f + 0.65f * k;
                    }
                    break;
                }
                case DemoKind.Poke:
                {
                    // Index out, the rest curled, pointing away from the player; two 7 cm pokes at the ghost panel's
                    // second row, which lights as the finger reaches it.
                    p.IndexCurl = 0f;
                    p.FingerCurl = 0.9f;
                    p.ThumbCurl = 0.5f;
                    p.HandRotation = Quaternion.Euler(-90f, 0f, 180f);   // design-frame rotation (pi/2, 0, pi)
                    p.Ghost = true;
                    float push = 0f;
                    for (int i = 0; i < PokeStarts.Length; i++)
                    {
                        float u = (t - PokeStarts[i]) / 0.6f;
                        if (u >= 0f && u < 1f) push = Mathf.Sin(u * Mathf.PI);
                    }
                    p.HandPosition.z = HandRoot.z + 0.07f * push;     // design z - 0.07 push: away from the player
                    p.HandPosition.y = HandRoot.y + 0.03f;
                    p.RowOpacity = RowOpacityRest + 0.55f * Clamp01((push - 0.7f) / 0.3f);
                    break;
                }
                case DemoKind.Swipe:
                {
                    // An open hand, palm to the player, sweeping 30 cm to the left in 0.25 s, twice, along the dotted
                    // arrow; it fades out after a sweep and fades in again before the next.
                    p.Arrow = true;
                    float x = SweepDist / 2f, fade = 1f;
                    if (!Sweep(t, SweepStarts[0], ref x, ref fade) && !Sweep(t, SweepStarts[1], ref x, ref fade))
                        fade = t >= 1.2f && t < 1.5f ? (t - 1.2f) / 0.3f : t < 0.2f ? t / 0.2f : t >= 2.7f ? (t - 2.7f) / 0.3f : 0f;
                    p.HandPosition.x = HandRoot.x + x;
                    p.Opacity = Opacity * fade;
                    break;
                }
            }
        }

        /// <summary>The index tip in the hand's frame (the index pivot plus its length along the fingers' +y).</summary>
        public static Vector3 IndexTip => new Vector3(FingerX[0], FingerPivotY + FingerLen[0], 0f);
        /// <summary>Press: two pokes per loop; the fingertip comes down from this high over the control to its top.</summary>
        public const float PressLift = 0.06f, PressTouch = 0.004f;
        public static readonly float[] PressStarts = { 0.4f, 1.5f };

        /// <summary>The deck walkthrough's demo: the ghost index finger, the other fingers curled, points straight down at a
        /// control (world anchor and up) and presses it twice per loop, the back of the hand toward the player (eye). The
        /// pose is written in Sunny's frame (position, rotation, scale), as the view applies every demo.</summary>
        public static void EvaluatePress(float t, Vector3 anchor, Vector3 up, Vector3 sunnyPos, Quaternion sunnyRot, float sunnyScale, Vector3 eye, ref DemoPose p)
        {
            p.Visible = true;
            p.Wide = true;
            p.Ring = p.Ghost = p.Arrow = false;
            p.IndexCurl = 0f;
            p.FingerCurl = 0.9f;
            p.ThumbCurl = 0.5f;
            float push = 0f;
            for (int i = 0; i < PressStarts.Length; i++)
            {
                float u = (t - PressStarts[i]) / 0.7f;
                if (u >= 0f && u < 1f) push = Mathf.Sin(u * Mathf.PI);
            }
            // Fade in at the start of the loop and out at its end, so a restart never jumps.
            float fade = t < 0.25f ? t / 0.25f : t > 2.7f ? Mathf.Max(0f, (3f - t) / 0.3f) : 1f;
            p.Opacity = Opacity * fade;
            // The hand's fingers (+y) point down the control's -up; its palm (+z) faces away from the player.
            Vector3 toEye = eye - anchor;
            Vector3 away = Vector3.ProjectOnPlane(-toEye, up);
            if (away.sqrMagnitude < 1e-8f) away = Vector3.ProjectOnPlane(sunnyRot * Vector3.forward, up);
            if (away.sqrMagnitude < 1e-8f) away = Vector3.forward;
            Quaternion world = Quaternion.LookRotation(away.normalized, up) * Quaternion.Euler(180f, 0f, 0f);   // +y -> -up, +z -> away
            Vector3 tipTarget = anchor + up * (PressTouch + (PressLift - PressTouch) * (1f - push));
            float s = sunnyScale > 1e-4f ? sunnyScale : 1f;
            Vector3 origin = tipTarget - world * IndexTip * s;   // the hand is drawn at Sunny's scale
            Quaternion inv = Quaternion.Inverse(sunnyRot);
            p.HandPosition = inv * (origin - sunnyPos) / s;
            p.HandRotation = inv * world;
        }

        // ======================================================================================================
        // The platform's anchored presets
        // ======================================================================================================

        /// <summary>The tour's own kinds (posed in front of Sunny by Evaluate); every other kind needs an anchor.</summary>
        public static bool IsTourKind(DemoKind k) => k == DemoKind.Palm || k == DemoKind.Poke || k == DemoKind.Swipe;

        /// <summary>How the hand is held over the anchor: fingers down at it (the back of the hand to the player), the palm
        /// down on it (fingers away from the player), the palm facing it from the player's right, or the index pointing at it.</summary>
        public enum HandHold : byte { FingersDown = 0, PalmDown, PalmToward, Pointing }

        /// <summary>One sampled preset pose in the anchor frame: where the contact point is (the index tip, the palm's face or a
        /// held tool's tip), the hand's shape, how it is held, an extra roll (deg, about the anchor frame's axis), the fade.</summary>
        public struct AnchoredPose
        {
            public Vector3 Offset;
            public GhostHand Hand;
            public HandHold Hold;
            public Vector3 RollAxis;
            public float RollDeg;
            public float Fade;
            /// <summary>Extra curl of every finger (Curl, OpenHand), added to the shape's.</summary>
            public float Curl;
            /// <summary>0..1 from the shape's own curls to Pinch's (the Pinch demo closing).</summary>
            public float PinchBlend;
        }

        /// <summary>A rim radius from the anchor's up length (under 0.95 m), else <paramref name="fallback"/>.</summary>
        public static float RadiusOf(Vector3 up, float fallback = 0.08f)
        {
            float m = up.magnitude;
            return m > 1e-4f && m < 0.95f ? m : fallback;
        }

        static float Bump(float t, float t0, float dur)
        {
            float u = (t - t0) / dur;
            return u >= 0f && u < 1f ? Mathf.Sin(u * Mathf.PI) : 0f;
        }

        static float Ramp(float t, float t0, float t1) => Ease(Clamp01((t - t0) / Mathf.Max(1e-4f, t1 - t0)));

        static float Fade(float t, float loop) => t < 0.25f ? t / 0.25f : t > loop - 0.3f ? Mathf.Max(0f, (loop - t) / 0.3f) : 1f;

        /// <summary>The preset (or Custom keyframes) at loop time t, in the anchor frame (r: the rim radius).</summary>
        public static AnchoredPose Sample(DemoKind kind, float t, float r, DemoMotion motion = null)
        {
            var p = new AnchoredPose { Hand = GhostHand.Point, Hold = HandHold.FingersDown, RollAxis = Vector3.right, Fade = Fade(t, GuideTiming.DemoLoop) };
            switch (kind)
            {
                case DemoKind.Tap:          // fingertips together, two light 2 cm taps
                    p.Hand = GhostHand.Cupped;
                    p.Offset = new Vector3(0f, 0.02f * (1f - Mathf.Max(Bump(t, 0.5f, 0.35f), Bump(t, 1.3f, 0.35f))), 0f);
                    break;
                case DemoKind.Slap:         // an open palm, a 5 cm slap at the near edge, then the centre
                {
                    p.Hand = GhostHand.Open; p.Hold = HandHold.PalmDown;
                    float z = Mathf.Lerp(0.8f * r, 0f, Ramp(t, 0.8f, 1.3f));
                    p.Offset = new Vector3(0f, 0.05f * (1f - Mathf.Max(Bump(t, 0.4f, 0.3f), Bump(t, 1.5f, 0.3f))), z);
                    break;
                }
                case DemoKind.MalletStrike: // a held mallet: two 6 cm strokes
                    p.Hand = GhostHand.Mallet;
                    p.Offset = new Vector3(0f, 0.06f * (1f - Mathf.Max(Bump(t, 0.4f, 0.4f), Bump(t, 1.2f, 0.4f))), 0f);
                    break;
                case DemoKind.Approach:     // the right hand 25 cm toward the anchor and back
                {
                    p.Hand = GhostHand.Open; p.Hold = HandHold.PalmToward;
                    float k = Ramp(t, 0.3f, 1.2f) * (1f - Ramp(t, 1.6f, 2.5f));
                    p.Offset = new Vector3(0.08f + 0.25f * (1f - k), 0f, 0f);
                    break;
                }
                case DemoKind.Circle:       // a pinched stick around the rim, one turn
                {
                    p.Hand = GhostHand.Pinch;
                    float a = Ramp(t, 0.3f, 2.7f) * Mathf.PI * 2f;
                    p.Offset = new Vector3(r * Mathf.Sin(a), 0f, r * Mathf.Cos(a));
                    break;
                }
                case DemoKind.Strike:       // one 4 cm fingertip stroke
                    p.Offset = new Vector3(0f, 0.04f * (1f - Bump(t, 0.6f, 0.45f)), 0f);
                    break;
                case DemoKind.Hold:         // lowered onto the anchor and held 1 s
                    p.Hand = GhostHand.Flat; p.Hold = HandHold.PalmDown;
                    p.Offset = new Vector3(0f, 0.06f * (1f - Ramp(t, 0.3f, 0.8f) + Ramp(t, 1.8f, 2.3f)), 0f);
                    break;
                case DemoKind.PalmRest:     // a flat palm laid on it
                    p.Hand = GhostHand.Flat; p.Hold = HandHold.PalmDown;
                    p.Offset = new Vector3(0f, 0.05f * (1f - Ramp(t, 0.3f, 0.9f) + Ramp(t, 2.3f, 2.7f)), 0f);
                    break;
                case DemoKind.Pinch:        // a pinch at the anchor (a tool pick-up): closes at 0.6 s, held to 1.6 s
                    p.Hand = GhostHand.Open;
                    p.PinchBlend = Ramp(t, 0.3f, 0.6f) * (1f - Ramp(t, 1.6f, 2.0f));
                    p.Offset = new Vector3(0f, 0.01f, 0f);
                    break;
                case DemoKind.Point:        // the index pointing at it from 30 cm, a small push
                    p.Hold = HandHold.Pointing;
                    p.Offset = new Vector3(0f, 0.12f, 0.275f) * (1f - 0.07f * Bump(t, 0.8f, 0.6f));
                    break;
                case DemoKind.OpenTone:     // flat fingers bounce off the edge
                    p.Hand = GhostHand.Flat; p.Hold = HandHold.PalmDown;
                    p.Offset = new Vector3(0f, 0.03f * (1f - Mathf.Max(Bump(t, 0.5f, 0.2f), Bump(t, 1.3f, 0.2f))), 0.7f * r);
                    break;
                case DemoKind.PalmBass:     // a flat palm drops onto the centre
                    p.Hand = GhostHand.Flat; p.Hold = HandHold.PalmDown;
                    p.Offset = new Vector3(0f, 0.08f * (1f - Ramp(t, 0.5f, 0.75f) + Ramp(t, 1.15f, 1.6f)), 0f);
                    break;
                case DemoKind.CuppedSlap:   // cupped fingers snap down at the edge
                    p.Hand = GhostHand.Cupped; p.Hold = HandHold.PalmDown;
                    p.Offset = new Vector3(0f, 0.05f * (1f - Mathf.Max(Bump(t, 0.5f, 0.18f), Bump(t, 1.4f, 0.18f))), 0.8f * r);
                    break;
                case DemoKind.HeelToe:      // the hand rocks heel then fingertips
                    p.Hand = GhostHand.Flat; p.Hold = HandHold.PalmDown;
                    p.Offset = new Vector3(0f, 0.01f, 0f);
                    p.RollAxis = Vector3.right;
                    p.RollDeg = 15f * (Bump(t, 0.4f, 0.6f) - Bump(t, 1.2f, 0.6f));
                    break;
                case DemoKind.Roll:         // two hands alternating quickly (one ghost hand, alternating sides)
                {
                    int n = Mathf.FloorToInt((t - 0.3f) / 0.25f);
                    bool on = t >= 0.3f && t < 2.3f;
                    p.Offset = new Vector3(on ? (n % 2 == 0 ? -0.03f : 0.03f) : 0f, 0.02f * (1f - (on ? Bump(t, 0.3f + n * 0.25f, 0.25f) : 0f)), 0f);
                    break;
                }
                case DemoKind.MalletHold:   // a held mallet strikes and stays down 0.6 s
                    p.Hand = GhostHand.Mallet;
                    p.Offset = new Vector3(0f, 0.06f * (1f - Ramp(t, 0.5f, 0.65f) + Ramp(t, 1.25f, 1.6f)), 0f);
                    break;
                case DemoKind.OpenHand:     // the hand opens, letting a tool go
                    p.Hand = GhostHand.Open;
                    p.Curl = 1f - Ramp(t, 0.8f, 1.4f);
                    p.Offset = new Vector3(0f, 0.02f, 0f);
                    break;
                case DemoKind.Hover:        // an open palm 3 cm above it, still
                    p.Hand = GhostHand.Open; p.Hold = HandHold.PalmDown;
                    p.Offset = new Vector3(0f, 0.03f, 0f);
                    break;
                case DemoKind.Lift:         // the palm rises 12 cm above the anchor
                    p.Hand = GhostHand.Open; p.Hold = HandHold.PalmDown;
                    p.Offset = new Vector3(0f, 0.12f * Ramp(t, 0.3f, 1.8f), 0f);
                    break;
                case DemoKind.Lower:        // the palm sinks 12 cm onto the anchor
                    p.Hand = GhostHand.Open; p.Hold = HandHold.PalmDown;
                    p.Offset = new Vector3(0f, 0.12f * (1f - Ramp(t, 0.3f, 1.8f)), 0f);
                    break;
                case DemoKind.Rock:         // the hand rocks +-15 deg about the forearm
                    p.Hand = GhostHand.Open; p.Hold = HandHold.PalmDown;
                    p.Offset = new Vector3(0f, 0.02f, 0f);
                    p.RollAxis = Vector3.forward;
                    p.RollDeg = 15f * Mathf.Sin((t - 0.3f) / 1.2f * Mathf.PI * 2f) * (t >= 0.3f && t < 2.7f ? 1f : 0f);
                    break;
                case DemoKind.Dip:          // a quick 4 cm dip and back
                    p.Hand = GhostHand.Open; p.Hold = HandHold.PalmDown;
                    p.Offset = new Vector3(0f, 0.06f - 0.04f * Bump(t, 0.8f, 0.4f), 0f);
                    break;
                case DemoKind.Curl:         // the fingers curl a little and open
                    p.Hand = GhostHand.Open; p.Hold = HandHold.PalmDown;
                    p.Offset = new Vector3(0f, 0.03f, 0f);
                    p.Curl = 0.4f * Bump(t, 0.6f, 1.2f);
                    break;
                case DemoKind.FingerCircle: // the index circles the rim 1 cm below the lip, one turn in 1.4 s
                {
                    float a = Ramp(t, 0.2f, 1.6f) + Ramp(t, 1.6f, 3.0f);
                    a *= Mathf.PI * 2f;
                    p.Offset = new Vector3(r * Mathf.Sin(a), -0.01f, r * Mathf.Cos(a));
                    p.Fade = 1f;
                    break;
                }
                // First five's kinds (the tour's tips pose them in the world with SunnyDemoWorld; these presets are their
                // anchored form for any TipSpec): Sweep an open hand slides 24 cm across the anchor, twice; PinchPull a pinch
                // pulls 14 cm to the player's right; Twist a pinched hand turns 40 deg about the anchor's up; Drag the index
                // touches and drags 6 cm toward the player.
                case DemoKind.Sweep:
                {
                    p.Hand = GhostHand.Open;
                    float x = Ramp(t, 0.3f, 0.7f) - Ramp(t, 1.5f, 1.9f);
                    p.Offset = new Vector3(-0.12f + 0.24f * x, 0.005f, 0f);
                    break;
                }
                case DemoKind.PinchPull:
                    p.Hand = GhostHand.Open;
                    p.PinchBlend = Ramp(t, 0.2f, 0.45f) * (1f - Ramp(t, 1.6f, 1.8f));
                    p.Offset = new Vector3(0.14f * Ramp(t, 0.6f, 1.2f) * (1f - Ramp(t, 1.8f, 2.6f)), 0.02f, 0f);
                    break;
                case DemoKind.Twist:
                    p.Hand = GhostHand.Pinch;
                    p.Offset = new Vector3(0f, 0.01f, 0f);
                    p.RollAxis = Vector3.up;
                    p.RollDeg = 40f * Ramp(t, 0.4f, 1.2f) * (1f - Ramp(t, 1.6f, 2.4f));
                    break;
                case DemoKind.Drag:
                    p.Offset = new Vector3(0f, 0.04f * (1f - Ramp(t, 0.3f, 0.5f) + Ramp(t, 1.5f, 1.7f)), 0.06f * Ramp(t, 0.6f, 1.4f));
                    break;
                case DemoKind.Custom:
                    return SampleCustom(motion, t);
                default:                    // the tour's kinds have no anchored form: a still pointing hand
                    p.Offset = new Vector3(0f, 0.05f, 0f);
                    break;
            }
            return p;
        }

        /// <summary>DemoKind.Custom: the TipSpec's keys, smoothstep between them, each key's hand shape held until the next.</summary>
        public static AnchoredPose SampleCustom(DemoMotion motion, float t)
        {
            var p = new AnchoredPose { Hand = GhostHand.Point, Hold = HandHold.FingersDown, RollAxis = Vector3.right, Fade = 1f };
            if (motion?.Keys == null || motion.Keys.Length == 0) return p;
            DemoKey[] keys = motion.Keys;
            float loop = motion.Loop > 0.1f ? motion.Loop : GuideTiming.DemoLoop;
            p.Fade = Fade(t, loop);
            int k = 0;
            while (k + 1 < keys.Length && keys[k + 1].T <= t) k++;
            DemoKey a = keys[k];
            if (t <= keys[0].T || k + 1 >= keys.Length) p.Offset = t <= keys[0].T ? keys[0].Offset : a.Offset;
            else
            {
                DemoKey b = keys[k + 1];
                p.Offset = Vector3.Lerp(a.Offset, b.Offset, Ease(Clamp01((t - a.T) / Mathf.Max(1e-4f, b.T - a.T))));
            }
            p.Hand = t <= keys[0].T ? keys[0].Hand : a.Hand;
            p.Hold = p.Hand == GhostHand.Flat || p.Hand == GhostHand.Open || p.Hand == GhostHand.Cupped ? HandHold.PalmDown : HandHold.FingersDown;
            return p;
        }

        /// <summary>The finger curls of a ghost-hand shape (index, the other three, the thumb).</summary>
        public static void CurlsOf(GhostHand hand, out float index, out float fingers, out float thumb)
        {
            switch (hand)
            {
                case GhostHand.Flat: index = 0f; fingers = 0f; thumb = 0.15f; return;
                case GhostHand.Open: index = 0f; fingers = 0f; thumb = 0f; return;
                case GhostHand.Cupped: index = 0.35f; fingers = 0.35f; thumb = 0.3f; return;
                case GhostHand.Pinch: index = 0.5f; fingers = 0.75f; thumb = 0.75f; return;
                case GhostHand.Fist: index = 1f; fingers = 1f; thumb = 1f; return;
                case GhostHand.Mallet:
                case GhostHand.Stick: index = 0.85f; fingers = 0.9f; thumb = 0.7f; return;
                default: index = 0f; fingers = 0.9f; thumb = 0.5f; return;   // Point
            }
        }

        /// <summary>The hand's contact point in its own frame for a pose: the index tip (as curled) for fingertip, pinch and
        /// pointing holds, a held tool's tip just past the knuckles, or the palm's face.</summary>
        public static Vector3 ContactPoint(in AnchoredPose p, float indexCurl)
        {
            if (p.Hold == HandHold.PalmDown || p.Hold == HandHold.PalmToward) return PalmFace;
            if (p.Hand == GhostHand.Mallet || p.Hand == GhostHand.Stick || p.Hand == GhostHand.Fist) return ToolTip;
            return new Vector3(FingerX[0], FingerPivotY, 0f) + FingerRotation(indexCurl) * new Vector3(0f, FingerLen[0], 0f);
        }

        /// <summary>The palm's face (hand-local -z is the palm side: the fingers curl that way) and a held tool's tip.</summary>
        public static readonly Vector3 PalmFace = new Vector3(0f, 0.01f, -0.011f), ToolTip = new Vector3(0.01f, 0.1f, -0.03f);

        /// <summary>The anchor frame: y along up, z toward the eye (in the plane across up), x = cross(z, y): the player's right.</summary>
        public static void AnchorFrame(Vector3 anchor, Vector3 up, Vector3 eye, out Vector3 x, out Vector3 y, out Vector3 z)
        {
            y = up.sqrMagnitude > 1e-12f ? up.normalized : Vector3.up;
            z = Vector3.ProjectOnPlane(eye - anchor, y);
            if (z.sqrMagnitude < 1e-10f) z = Vector3.ProjectOnPlane(Vector3.back, y);
            if (z.sqrMagnitude < 1e-10f) z = Vector3.ProjectOnPlane(Vector3.forward, y);
            z.Normalize();
            x = Vector3.Cross(z, y);
        }

        /// <summary>The hand's world rotation for a pose in the anchor frame (fingers along hand +y, palm along hand -z).</summary>
        public static Quaternion HandRotationOf(in AnchoredPose p, Vector3 x, Vector3 y, Vector3 z, Vector3 tipWorld, Vector3 anchor)
        {
            Quaternion q;
            switch (p.Hold)
            {
                case HandHold.PalmDown: q = Quaternion.LookRotation(y, -z); break;           // palm on it, fingers away from the player
                case HandHold.PalmToward: q = Quaternion.LookRotation(x, y); break;          // palm facing it from the right, fingers up
                case HandHold.Pointing:
                {
                    Vector3 f = anchor - tipWorld;
                    f = f.sqrMagnitude > 1e-10f ? f.normalized : -y;
                    Vector3 palm = Vector3.ProjectOnPlane(-y, f);
                    if (palm.sqrMagnitude < 1e-8f) palm = Vector3.ProjectOnPlane(-z, f);
                    q = Quaternion.LookRotation(-palm.normalized, f);
                    break;
                }
                default: q = Quaternion.LookRotation(z, -y); break;                           // fingers down, the back of the hand to the player
            }
            if (p.RollDeg != 0f)
            {
                Vector3 axis = p.RollAxis.x * x + p.RollAxis.y * y + p.RollAxis.z * z;
                q = Quaternion.AngleAxis(p.RollDeg, axis) * q;
            }
            return q;
        }

        /// <summary>The platform's demo of <paramref name="kind"/> at loop time t at a world anchor (its up, its rim radius
        /// from the up's length), seen from <paramref name="eye"/>, written in Sunny's frame (position, rotation, scale) as
        /// SunnyView applies every demo.</summary>
        public static void EvaluateAnchored(DemoKind kind, float t, Vector3 anchor, Vector3 up, Vector3 eye, DemoMotion motion,
            Vector3 sunnyPos, Quaternion sunnyRot, float sunnyScale, ref DemoPose p)
        {
            AnchoredPose a = Sample(kind, t, RadiusOf(up), motion);
            AnchorFrame(anchor, up, eye, out Vector3 x, out Vector3 y, out Vector3 z);
            CurlsOf(a.Hand, out float ic, out float fc, out float tc);
            if (a.PinchBlend > 0f)
            {
                CurlsOf(GhostHand.Pinch, out float pi, out float pf, out float pt);
                ic = Mathf.Lerp(ic, pi, a.PinchBlend); fc = Mathf.Lerp(fc, pf, a.PinchBlend); tc = Mathf.Lerp(tc, pt, a.PinchBlend);
            }
            ic = Mathf.Clamp01(ic + a.Curl); fc = Mathf.Clamp01(fc + a.Curl); tc = Mathf.Clamp01(tc + a.Curl * 0.6f);
            p.Visible = true;
            p.Wide = true;
            p.Ring = p.Ghost = p.Arrow = false;
            p.IndexCurl = ic;
            p.FingerCurl = fc;
            p.ThumbCurl = tc;
            p.Opacity = Opacity * a.Fade;
            Vector3 tip = anchor + x * a.Offset.x + y * a.Offset.y + z * a.Offset.z;
            Quaternion world = HandRotationOf(a, x, y, z, tip, anchor);
            float s = sunnyScale > 1e-4f ? sunnyScale : 1f;
            Vector3 origin = tip - world * ContactPoint(a, ic) * s;   // the hand is drawn at Sunny's scale
            Quaternion inv = Quaternion.Inverse(sunnyRot);
            p.HandPosition = inv * (origin - sunnyPos) / s;
            p.HandRotation = inv * world;
        }

        static bool Sweep(float t, float t0, ref float x, ref float fade)
        {
            float u = (t - t0) / SweepT;
            if (u >= 0f && u < 1f)
            {
                x = SweepDist / 2f - SweepDist * Ease(u);
                fade = 1f;
                return true;
            }
            if (u >= 1f && u < 1.6f)
            {
                x = -SweepDist / 2f;
                fade = 1f - (u - 1f) / 0.6f;
                return true;
            }
            return false;
        }

        /// <summary>The nod pose: how far Sunny dips and tilts at loop time t (0 outside the two nods).</summary>
        public static float NodAmount(float t)
        {
            for (int i = 0; i < GuideTiming.NodStarts.Length; i++)
            {
                float u = (t - GuideTiming.NodStarts[i]) / GuideTiming.NodT;
                if (u >= 0f && u < 1f) return Mathf.Sin(u * Mathf.PI);
            }
            return 0f;
        }
    }
}
