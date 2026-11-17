// The summon gesture (spec 08 section 1.1): an open hand above the waist, its palm
// within 40 degrees of facing the head and 8-80 cm from it, held for half a second, toggles the panel once. A pinching,
// playing (400 ms) or stick-holding hand never counts. On Quest the OS raises SystemGestureInProgress as soon as an open
// palm faces the headset (the system menu icon shows), i.e. for this very pose, so the flag alone must not block it
// (it did, and the menu never opened on device); only a pinch during it (the user opening the system menu) does.
// Pure logic: the module samples the hands, tests feed samples directly.
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Menu
{
    /// <summary>One hand as the gesture sees it this frame.</summary>
    public struct SummonSample
    {
        /// <summary>A tracked hand (never the desktop mouse hand), not pinching, holding nothing, not pinching into the
        /// system menu, not played for 400 ms.</summary>
        public bool Eligible;
        public bool HasPalm;
        public Vector3 Center, Normal;   // palm centre and the direction out of the palm
        public float Grasp;              // mean wrist distance of the middle/ring/little tips (NaN untracked)

        public static SummonSample From(PointerInput input, HandState h, double nowMs)
        {
            var s = new SummonSample { Grasp = float.NaN };
            // Only a tracked hand charges (the desktop mouse hand is a stand-in; M summons there).
            if (input == null || h == null || h.IsMouse || !h.Connected || h.Pinching || h.Holding != null) return s;
            if (nowMs - h.LastPlayMs < MenuConstants.SummonQuietMs) return s;
            HandFrame f = input.Frame(h);
            if (f == null || !f.isTracked || SystemPinch(f)) return s;
            s.Eligible = true;
            s.HasPalm = input.TryPalm(h, out s.Center, out s.Normal);
            s.Grasp = input.GraspAmount(h);
            return s;
        }

        /// <summary>From a raw frame (tests): what From() reads through PointerInput.</summary>
        public static SummonSample FromFrame(HandFrame f, bool pinching = false, bool holding = false, bool playedRecently = false)
        {
            var s = new SummonSample { Grasp = float.NaN };
            if (f == null || !f.isTracked || SystemPinch(f) || pinching || holding || playedRecently) return s;
            s.Eligible = true;
            s.HasPalm = HandMath.Palm(f, out s.Center, out s.Normal);
            s.Grasp = HandMath.GraspAmount(f);
            return s;
        }

        /// <summary>The OS system gesture with the fingers closing into a pinch. HandInput freezes its pinch state while
        /// the flag is up, so the gap is read from the frame here.</summary>
        static bool SystemPinch(HandFrame f) => f.systemGesture && HandMath.PinchGap(f) <= HandMath.PinchEndGap;
    }

    public sealed class SummonGesture
    {
        public const float WaistBelowEyes = 0.45f;
        public const float RingOffset = 0.03f;

        /// <summary>The charging hand (0 left, 1 right) or -1.</summary>
        public int Hand { get; private set; } = -1;
        public float T { get; private set; }
        public bool Done { get; private set; }

        public bool RingVisible { get; private set; }
        public Vector3 RingPosition { get; private set; }
        public Quaternion RingRotation { get; private set; } = Quaternion.identity;
        public float RingScale { get; private set; } = 0.5f;
        public float RingOpacity { get; private set; } = 0.15f;

        /// <summary>Does this sample count as charging (all the rules but the hold time)?</summary>
        public static bool Qualifies(in SummonSample s, Vector3 head)
        {
            if (!s.Eligible || !s.HasPalm) return false;
            if (s.Center.y < head.y - WaistBelowEyes) return false;
            if (float.IsNaN(s.Grasp) || s.Grasp < MenuConstants.SummonMinGrasp) return false;
            Vector3 d = head - s.Center;
            float dist = d.magnitude;
            if (dist < MenuConstants.SummonMinDist || dist > MenuConstants.SummonMaxDist) return false;
            return Vector3.Dot(s.Normal, d / dist) >= MenuConstants.SummonCos;
        }

        /// <summary>One frame: the first qualifying hand charges; a different one (or none) resets; at 0.5 s it fires
        /// once. Returns the hand that fired this frame, else -1.</summary>
        public int Step(in SummonSample left, in SummonSample right, Vector3 head, float dt)
        {
            int charging = Qualifies(left, head) ? 0 : Qualifies(right, head) ? 1 : -1;
            if (charging != Hand)
            {
                Hand = charging;
                T = 0f;
                Done = false;
            }
            if (charging < 0 || Done)
            {
                RingVisible = false;
                return -1;
            }
            T += dt;
            float k = Mathf.Min(1f, T / MenuConstants.SummonSeconds);
            Vector3 c = charging == 0 ? left.Center : right.Center;
            Vector3 n = charging == 0 ? left.Normal : right.Normal;
            RingVisible = true;
            RingPosition = c + RingOffset * n;
            Vector3 look = head - RingPosition;
            RingRotation = look.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(look) : Quaternion.identity;
            RingScale = 0.5f + 0.5f * k;
            RingOpacity = 0.15f + 0.65f * k;
            if (T >= MenuConstants.SummonSeconds)
            {
                Done = true;
                RingVisible = false;
                return charging;
            }
            return -1;
        }

        /// <summary>Not evaluated this frame (the panel is held: neither the summon nor the pokes run):
        /// the ring goes away, the charge is kept.</summary>
        public void Pause() => RingVisible = false;

        /// <summary>Forget the charge (no hand charging).</summary>
        public void Reset()
        {
            Hand = -1;
            T = 0f;
            Done = false;
            RingVisible = false;
        }
    }
}
