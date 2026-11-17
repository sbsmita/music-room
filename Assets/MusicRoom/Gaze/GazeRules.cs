// Look and pinch: the rules, plain C# (EditMode-tested, no Unity objects, allocation-free).
//   Dwell      a target counts once the head ray has stayed on it DwellSeconds with the head steady (not mid fast turn);
//              a look away shorter than LoseGrace keeps it (a row edge, a jittery head).
//   Field      a target counts only while its centre is within MaxAngleDeg of the view centre, so a highlight never pops
//              in the periphery (the head ray itself is the view centre).
//   Hand       a pinch chooses only from a free hand: tracked, not the mouse, holding nothing, not busy with an instrument
//              or the menu (Layout's HandBusy: playing in the last second, on a play surface or the menu, the ray summon's,
//              Move instruments'), neither fingertip claimed this frame (PointerClaims: an instrument's zone, the menu's
//              grab zone, Sunny), not the system gesture, not just played.
//   Tap        the choice fires on the pinch's release: within MaxTapSeconds of its start, the pinch point moved less than
//              MaxTapMove, the hand free throughout, and the head still on the same target. A longer or moving pinch is
//              someone else's gesture (the seat swivel, the two-hand reset, the ray summon's hold).
//   Gate       only in the headset, setting on, not while moving instruments, MR placing, a rig held, or Sunny teaching
//              a pinch.
using UnityEngine;

namespace MusicRoom.Gaze
{
    public static class GazeRules
    {
        public const float DwellSeconds = 0.35f;
        public const float LoseGrace = 0.15f;
        public const float MaxAngleDeg = 22f;
        public const float MaxHeadSpeedDeg = 75f;
        public const float HeadSpeedTau = 0.08f;
        public const float MaxRayLength = 6f;
        public const float MaxTapSeconds = 0.6f;
        public const float MaxTapMove = 0.06f;
        public const float FadeSeconds = 0.15f;
        /// <summary>The highlight has shown this long before Sunny is asked for the tip.</summary>
        public const float TipAfter = 0.4f, TipEvery = 3f;
        public const string TipId = "tip_look_pinch";
        public const string FeatureId = "gaze.pinch";

        /// <summary>The point lies within maxDeg of the view centre (forward from the eye).</summary>
        public static bool InView(Vector3 eye, Vector3 forward, Vector3 point, float maxDeg = MaxAngleDeg)
        {
            Vector3 d = point - eye;
            float m = d.magnitude;
            if (m < 1e-4f) return true;
            float f = forward.magnitude;
            if (f < 1e-6f) return false;
            return Vector3.Dot(d, forward) / (m * f) >= Mathf.Cos(maxDeg * Mathf.Deg2Rad);
        }

        /// <summary>The head's angular speed in degrees per second between two forward vectors.</summary>
        public static float AngularSpeed(Vector3 prevForward, Vector3 forward, float dt)
        {
            if (dt <= 1e-5f || prevForward.sqrMagnitude < 1e-8f || forward.sqrMagnitude < 1e-8f) return 0f;
            return Vector3.Angle(prevForward, forward) / dt;
        }

        /// <summary>One-pole smoothing of the head speed (time constant HeadSpeedTau).</summary>
        public static float Smooth(float current, float target, float dt) =>
            dt <= 0f ? current : current + (target - current) * (1f - Mathf.Exp(-dt / HeadSpeedTau));

        public static bool HeadSteady(float smoothedSpeedDeg) => smoothedSpeedDeg <= MaxHeadSpeedDeg;

        /// <summary>The feature may run at all this frame.</summary>
        public static bool Enabled(bool settingOn, bool presenting, bool arranging, bool mrPlacing, bool rigHeld, bool teachingPinch) =>
            settingOn && presenting && !arranging && !mrPlacing && !rigHeld && !teachingPinch;

        /// <summary>A hand whose pinch may be a look-and-pinch choice.</summary>
        public static bool HandFree(bool connected, bool mouse, bool holding, bool busy, bool indexClaimed, bool thumbClaimed,
            bool systemGesture, bool playedRecently) =>
            connected && !mouse && !holding && !busy && !indexClaimed && !thumbClaimed && !systemGesture && !playedRecently;

        /// <summary>A target key: slot (0 the menu, 1 + kind an instrument) and a sub key (the menu's row key).</summary>
        public static long Key(int slot, int sub) => ((long)(slot + 1) << 32) | (uint)sub;
        public static int SlotOf(long key) => key == 0 ? -1 : (int)(key >> 32) - 1;
        public static int SubOf(long key) => (int)(uint)(key & 0xffffffffL);
    }

    /// <summary>The dwell over target keys (0 = nothing).</summary>
    public sealed class GazeDwell
    {
        public long Committed { get; private set; }
        public long Pending { get; private set; }
        public float PendingSeconds { get; private set; }
        /// <summary>Seconds the committed target has been held.</summary>
        public float Held { get; private set; }
        float _lost;

        public void Reset()
        {
            Committed = Pending = 0;
            PendingSeconds = Held = _lost = 0f;
        }

        /// <summary>One frame: <paramref name="raw"/> is what the head ray is on now (0: nothing that counts).</summary>
        public long Step(long raw, float dt, bool headSteady)
        {
            if (raw != 0 && raw == Committed)
            {
                _lost = 0f;
                Pending = raw;
                PendingSeconds = 0f;
                Held += dt;
                return Committed;
            }
            if (Committed != 0)
            {
                _lost += dt;
                if (_lost >= GazeRules.LoseGrace) { Committed = 0; Held = 0f; }
                else Held += dt;
            }
            if (raw == 0) { Pending = 0; PendingSeconds = 0f; return Committed; }
            if (raw != Pending) { Pending = raw; PendingSeconds = 0f; }
            else PendingSeconds += dt;
            if (PendingSeconds >= GazeRules.DwellSeconds - 1e-4f && headSteady)
            {
                Committed = raw;
                _lost = 0f;
                Held = 0f;
                PendingSeconds = 0f;
            }
            return Committed;
        }
    }

    /// <summary>One pinch that may become a choice (started on a committed target).</summary>
    public struct GazeTap
    {
        public bool Active, Cancelled;
        public int Hand;
        public float Seconds;
        public Vector3 Start;
        public long Key;

        public void Begin(int hand, Vector3 at, long key)
        {
            Active = true;
            Cancelled = false;
            Hand = hand;
            Seconds = 0f;
            Start = at;
            Key = key;
        }

        /// <summary>A frame while it is held: too long, moved too far or the hand became busy cancels it.</summary>
        public void Step(float dt, Vector3 at, bool handFree)
        {
            if (!Active) return;
            Seconds += dt;
            if (!handFree || Seconds > GazeRules.MaxTapSeconds || (at - Start).sqrMagnitude > GazeRules.MaxTapMove * GazeRules.MaxTapMove)
                Cancelled = true;
        }

        /// <summary>The release: true when it is a choice of <see cref="Key"/> (the head is on <paramref name="lookingAt"/> now).</summary>
        public bool Release(long lookingAt)
        {
            bool ok = Active && !Cancelled && Seconds <= GazeRules.MaxTapSeconds && lookingAt == Key && Key != 0;
            Active = false;
            return ok;
        }

        public void Cancel() { Active = false; Cancelled = true; }
    }
}
