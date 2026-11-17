// Throwing paint by hand (the playback show, round 2): while a recording plays back, a quick forward flick of an open hand
// toward a wall throws a blob of paint that lands where the hand's throw points. This file is the gesture itself, pure
// maths on the hand data the app already has (HandFrame's palm, how open the hand is, the pinch, and Layout's HandBusy:
// playing an instrument or the menu, a stick or a mallet in the hand); Decor/WallPaint.cs reads the hands, flies the blob
// and paints the splat.
// A throw is a flick: the palm speeds up past PeakSpeed and then snaps back (the release), all within Window, having
// travelled at least Travel, forward (away from the head and within ForwardCos of where the player looks), the hand OPEN
// (GraspAmount over OpenMin: fingers out, not a fist, not a pointing hand), no pinch anywhere in the window, never while
// the hand is busy with an instrument or the menu (any sample of the window), tracked all along with high confidence,
// and away from the face. Slow moves, a hand playing a drum or a key (it is busy, and its strokes go down, not forward),
// reaching for the menu (pinch / busy) and waving near the face are not throws. One per hand per Refractory.
// Allocation-free: a ring of samples per hand.
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    /// <summary>One hand at one moment for the throw gesture: time (s), palm centre (world), how open the hand is
    /// (HandMath.GraspAmount: ~0.18 m open, under 0.1 m a fist), a pinch (any of the app's pinches), busy (Layout's HandBusy
    /// or a held tool), tracked with high confidence.</summary>
    public struct ThrowSample
    {
        public float Time;
        public Vector3 Palm;
        public float Open;
        public bool Pinch, Busy, Tracked;
    }

    public sealed class ThrowGesture
    {
        public const int Ring = 32;
        /// <summary>The flick: within Window (s) the palm passes PeakSpeed (m/s), travels Travel (m) and the speed then falls to
        /// under Release x the peak.</summary>
        public const float Window = 0.25f, PeakSpeed = 1.6f, Travel = 0.12f, Release = 0.7f;
        /// <summary>Faster than this (m/s) is not a hand but the tracker jumping (lost and found again).</summary>
        public const float MaxSpeed = 9f;
        /// <summary>Speeds are measured over at least this (s).</summary>
        public const float Span = 0.03f;
        /// <summary>Forward: the throw within acos(ForwardCos) of the look direction (flat), moving away from the head, neither
        /// straight up nor steeply down (a stroke onto a drum or a key).</summary>
        public const float ForwardCos = 0.35f, AwayCos = 0.15f, MaxUp = 0.85f, MaxDown = -0.55f;
        /// <summary>An open hand: GraspAmount at least this (an open hand ~0.18 m, the pointing hand ~0.139, a fist ~0.09).</summary>
        public const float OpenMin = 0.148f;
        /// <summary>The palm at least this far from the head (m), and between these heights about it.</summary>
        public const float FaceClear = 0.22f, Below = 0.85f, Above = 0.45f;
        public const float Refractory = 0.45f;

        readonly ThrowSample[,] _s = new ThrowSample[2, Ring];
        readonly int[] _n = new int[2], _head = new int[2];
        readonly float[] _last = { -10f, -10f };

        /// <summary>Why the last Push said no (tests, the log).</summary>
        public string Why { get; private set; } = "";

        public void Reset()
        {
            _n[0] = _n[1] = 0;
            _head[0] = _head[1] = 0;
        }

        ThrowSample At(int hand, int back) => _s[hand, ((_head[hand] - 1 - back) % Ring + Ring) % Ring];

        /// <summary>Feed one hand's sample (each frame, in time order). True when this sample completes a throw: the palm where
        /// it let go, the direction it throws and its peak speed.</summary>
        public bool Push(int hand, in ThrowSample s, Vector3 head, Vector3 look, out Vector3 origin, out Vector3 dir, out float speed)
        {
            origin = dir = default;
            speed = 0f;
            hand &= 1;
            _s[hand, _head[hand]] = s;
            _head[hand] = (_head[hand] + 1) % Ring;
            if (_n[hand] < Ring) _n[hand]++;
            return Check(hand, head, look, out origin, out dir, out speed);
        }

        bool Check(int hand, Vector3 head, Vector3 look, out Vector3 origin, out Vector3 dir, out float speed)
        {
            origin = dir = default;
            speed = 0f;
            ThrowSample now = At(hand, 0);
            if (now.Time - _last[hand] < Refractory) return No("refractory");
            if (_n[hand] < 4) return No("warming up");
            // the window's samples, newest first: everything tracked, free, unpinched
            int count = 0;
            for (int k = 0; k < _n[hand]; k++)
            {
                ThrowSample a = At(hand, k);
                if (now.Time - a.Time > Window) break;
                if (!a.Tracked) return No("not tracked");
                if (a.Busy) return No("busy with an instrument or the menu");
                if (a.Pinch) return No("pinching");
                count = k + 1;
            }
            if (count < 4) return No("too few samples");
            // speeds over at least Span seconds (a tracker slower than the frames repeats poses: one frame's step would
            // read double), the peak and the speed now
            float peak = 0f, vNow = -1f;
            int peakAt = -1;
            for (int k = 0; k < count; k++)
            {
                ThrowSample a = At(hand, k);
                int j = k + 1;
                while (j < _n[hand] && a.Time - At(hand, j).Time < Span) j++;
                if (j >= _n[hand]) break;
                ThrowSample b = At(hand, j);
                float dt = a.Time - b.Time;
                if (dt <= 1e-4f || dt > Window) break;
                float v = (a.Palm - b.Palm).magnitude / dt;
                if (k == 0) vNow = v;
                if (v > peak) { peak = v; peakAt = k; }
            }
            if (peak < PeakSpeed) return No("slow");
            if (peak > MaxSpeed) return No("a jump in tracking");
            // released: the speed fell after the peak (the snap of a flick)
            if (peakAt < 1 || vNow < 0f || vNow > Release * peak) return No("not released yet");
            ThrowSample first = At(hand, count - 1);
            ThrowSample atPeak = At(hand, peakAt);
            Vector3 travel = atPeak.Palm - first.Palm;
            if (travel.magnitude < Travel) return No("too short");
            ThrowSample s0 = now;
            if (s0.Open < OpenMin || atPeak.Open < OpenMin) return No("not an open hand");
            Vector3 d = travel.normalized;
            if (d.y > MaxUp) return No("straight up");
            if (d.y < MaxDown) return No("down: a stroke, not a throw");
            Vector3 flat = new Vector3(d.x, 0f, d.z), lookFlat = new Vector3(look.x, 0f, look.z);
            if (flat.sqrMagnitude < 1e-4f || lookFlat.sqrMagnitude < 1e-4f) return No("no direction");
            if (Vector3.Dot(flat.normalized, lookFlat.normalized) < ForwardCos) return No("not toward where you look");
            Vector3 away = s0.Palm - head;
            if (away.magnitude < FaceClear) return No("at the face");
            if (Vector3.Dot(d, away.normalized) < AwayCos) return No("not away from the head");
            if (s0.Palm.y < head.y - Below || s0.Palm.y > head.y + Above) return No("too low or high");
            _last[hand] = now.Time;
            origin = s0.Palm;
            dir = d;
            speed = peak;
            Why = "throw";
            return true;
        }

        bool No(string why) { Why = why; return false; }
    }
}
