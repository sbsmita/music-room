// The pair of mallets (spec 5.1): the drumsticks' behaviour (Drums/DrumSticks.cs) with the marimba's lever.
// They rest end to end on the tray; a pinch (the grab pinch: thumb-index OR Meta's own index pinch, HandState.GrabPinch*;
// the OpenXR tip joints sit ~17 mm apart even when the tips touch, so the 15 mm thumb-index rule alone starts late and
// flickers) or a closing fist while the hand is not playing, within 12 cm of a shaft, picks one up; inside the 24 cm dead
// zone a missed pinch is consumed (Near) and never grabs the rig. A held mallet sits at the thumb-index midpoint, continues
// the line wrist -> grip with the sticks' 12.6 degree droop toward the palm, its butt 0.10 m behind the grip and its head
// centre 0.27 m beyond it (the sticks' lever, so both feel the same). It is let go after the hand stayed open 0.25 s
// (a relaxed grip keeps it), freezes and goes silent while tracking is lost (snaps back, no blend) and drops after 1.5 s,
// then settles onto the tray in 0.4 s. Plain C#: hand frames in, poses and the head pointers out; no Drums reference
// (the grip maths is Core's HandToolMath).
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Marimba
{
    /// <summary>The mallets' grip maths: Core's HandToolMath (marimba request R4) with the mallet's lever and the sticks' droop.</summary>
    public static class MarimbaToolMath
    {
        /// <summary>The droop below the hand's forward line (rad; about 12.6 degrees), as the sticks.</summary>
        public const float TiltRad = -0.22f;

        public static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p) => HandToolMath.SegmentDistance(a, b, p);

        public static void Grip(Vector3 wrist, Quaternion wristRot, Vector3 thumbTip, Vector3 indexTip, out Vector3 grip, out Vector3 axis) =>
            HandToolMath.Grip(wrist, wristRot, thumbTip, indexTip, TiltRad, out grip, out axis);

        /// <summary>A held mallet's pose from its grip and axis: butt = grip - axis * 0.10, +y along the axis, head centre
        /// = butt + axis * 0.37.</summary>
        public static void Pose(Vector3 grip, Vector3 axis, out Vector3 butt, out Quaternion rotation, out Vector3 head) =>
            HandToolMath.Pose(grip, axis, MarimbaGeometry.MalletGripFromButt, MarimbaGeometry.MalletHeadFromButt, MarimbaGeometry.MalletHeadFromButt,
                out butt, out rotation, out head, out _);

        public static float SettleEase(float settle) => HandToolMath.SettleEase(settle);
    }

    /// <summary>Hand joints for a hand state (the app's PointerInput.Frame, or a test fixture).</summary>
    public interface IMarimbaHandFrames
    {
        HandFrame Frame(HandState hand);
    }

    /// <summary>One mallet: a hand tool (HandState.Holding) and the owner of its head pointer.</summary>
    public sealed class MarimbaMalletTool : IHandTool, IPointerOwner, IHeldBy
    {
        public readonly int Side;
        /// <summary>The head pointer (a tool pointer, ToolKind.Mallet, registered with PointerInput). Down while held and tracked.</summary>
        public readonly Pointer HeadPointer;
        readonly Pointer[] _pointers;

        public HandState Hand;
        public float OpenFor, LostFor, Settle = 1f;
        public Vector3 Grip, Axis;
        public bool Smoothed;
        /// <summary>The raw pose (world): what the hit test reads. The drawn mallet may pivot about the grip (MalletVisual).</summary>
        public Vector3 Butt, Head;
        public Quaternion Rotation = Quaternion.identity;
        /// <summary>Where a release started (rig-local), for the settle onto the tray.</summary>
        public Vector3 SettleFromButt;
        public Quaternion SettleFromRot = Quaternion.identity;

        public MarimbaMalletTool(int side)
        {
            Side = side;
            HeadPointer = Pointer.CreateTool(side == 0 ? "marimba-mallet0" : "marimba-mallet1", this, ToolKind.Mallet);
            _pointers = new[] { HeadPointer };
        }

        public bool Held => Hand != null;
        /// <summary>IHeldBy: PlayStamp stamps the hand that holds the mallet too.</summary>
        public HandState Holder => Hand;
        public IReadOnlyList<Pointer> Pointers => _pointers;
        public double LastPlayMs { get; set; } = double.NegativeInfinity;
    }

    public sealed class MarimbaMallets
    {
        public const float GrabRadius = 0.12f, DeadZone = 0.24f, OpenDistance = 0.05f, OpenDwell = 0.25f, LostGrace = 1.5f, SettleRate = 2.5f;
        public const float HeadRadius = MarimbaGeometry.MalletHeadRadius;

        readonly MarimbaMalletTool[] _m = { new MarimbaMalletTool(0), new MarimbaMalletTool(1) };
        readonly IMarimbaHandFrames _frames;

        /// <summary>The instrument's world pose (its root transform): rest poses on the tray are rig-local.</summary>
        public Vector3 RigPosition;
        public Quaternion RigRotation = Quaternion.identity;
        /// <summary>The natural bar tops' height (the tray hangs from the front rail).</summary>
        public float NaturalTopH = MarimbaGeometry.HDefault;
        /// <summary>The marimba is at the seat (playable): otherwise nothing can be picked up.</summary>
        public bool Present = true;
        /// <summary>Raised on pick-up (true) and release (false): the module's marimba.pickup / marimba.release signals.</summary>
        public event Action<MarimbaMalletTool, bool> Changed;
        public int Version { get; private set; }

        public MarimbaMallets(IMarimbaHandFrames frames)
        {
            _frames = frames;
            for (int s = 0; s < 2; s++) PlaceAtRest(_m[s]);
        }

        public MarimbaMalletTool this[int side] => _m[side];
        public int Count => 2;
        public bool AnyHeld => _m[0].Held || _m[1].Held;

        Vector3 ToWorld(Vector3 local) => RigPosition + RigRotation * local;
        Vector3 ToLocal(Vector3 world) => Quaternion.Inverse(RigRotation) * (world - RigPosition);

        /// <summary>A resting mallet's pose (rig-local): butt and rotation (+y along the shaft to the head).</summary>
        public void RestLocal(int side, out Vector3 butt, out Quaternion rot)
        {
            MarimbaGeometry.MalletRest(side, NaturalTopH, out butt, out Vector3 axis);
            rot = Quaternion.FromToRotation(Vector3.up, axis);
        }

        void PlaceAtRest(MarimbaMalletTool m)
        {
            RestLocal(m.Side, out Vector3 b, out Quaternion r);
            SetWorld(m, ToWorld(b), RigRotation * r);
            m.Settle = 1f;
        }

        static void SetWorld(MarimbaMalletTool m, Vector3 butt, Quaternion rot)
        {
            m.Butt = butt; m.Rotation = rot;
            m.Head = butt + rot * Vector3.up * MarimbaGeometry.MalletHeadFromButt;
        }

        /// <summary>The rig moved (arrange mode, a glide): resting mallets move with it.</summary>
        public void SnapRestingToRig() { for (int s = 0; s < 2; s++) if (!_m[s].Held && _m[s].Settle >= 1f) PlaceAtRest(_m[s]); }

        // =============================== pick-up ===============================

        /// <summary>The thumb-index midpoint when the hand is tracked, else the given tip (the drumsticks' pinch point).</summary>
        public Vector3 PinchPoint(HandState h, Vector3 fallback)
        {
            HandFrame f = _frames?.Frame(h);
            if (f != null && f.isTracked) return (f.position[(int)HandJoint.ThumbTip] + f.position[(int)HandJoint.IndexTip]) * 0.5f;
            return fallback;
        }

        public float ShaftDistance(MarimbaMalletTool m, Vector3 point) => MarimbaToolMath.SegmentDistance(m.Butt, m.Head, point);

        /// <summary>The nearest mallet not in a hand, with its shaft distance (null / +inf when none or not at the seat).</summary>
        public MarimbaMalletTool NearestFree(Vector3 point, out float distance)
        {
            MarimbaMalletTool best = null;
            distance = float.PositiveInfinity;
            if (!Present) return null;
            for (int i = 0; i < 2; i++)
            {
                MarimbaMalletTool m = _m[i];
                if (m.Held) continue;
                float d = ShaftDistance(m, point);
                if (d < distance) { distance = d; best = m; }
            }
            return best;
        }

        /// <summary>IHandToolProvider.NearestFree (platform 4.6): metres to the nearest free mallet, +inf if none.</summary>
        public float NearestFree(Vector3 tip) { NearestFree(tip, out float d); return d; }

        /// <summary>The drumsticks' grab rule: Grabbed within 12 cm of a shaft, Near (the pinch is consumed) within 24 cm, else None.</summary>
        public StickPick TryPickUp(HandState h, Vector3 tip)
        {
            if (h == null || h.Holding != null || !Present) return StickPick.None;
            Vector3 point = PinchPoint(h, tip);
            MarimbaMalletTool m = NearestFree(point, out float distance);
            if (m == null) return StickPick.None;
            if (distance > GrabRadius) return distance < DeadZone ? StickPick.Near : StickPick.None;
            m.Hand = h;
            h.Holding = m;
            m.OpenFor = 0f; m.LostFor = 0f; m.Smoothed = false;
            m.HeadPointer.Down = true;
            Version++;
            Changed?.Invoke(m, true);
            return StickPick.Grabbed;
        }

        /// <summary>Back to the tray: it settles there over 0.4 s from where it was let go.</summary>
        public void Release(MarimbaMalletTool m)
        {
            if (m == null || m.Hand == null) return;
            if (ReferenceEquals(m.Hand.Holding, m)) m.Hand.Holding = null;
            m.Hand = null;
            m.HeadPointer.Down = m.HeadPointer.Active = false;
            m.SettleFromButt = ToLocal(m.Butt);
            m.SettleFromRot = Quaternion.Inverse(RigRotation) * m.Rotation;
            m.Settle = 0f;
            Version++;
            Changed?.Invoke(m, false);
        }

        /// <summary>Every held mallet back to the tray at once (stow, arrange mode, the session ended).</summary>
        public void ReleaseAll() { for (int i = 0; i < 2; i++) if (_m[i].Held) Release(_m[i]); }

        /// <summary>Lays both mallets on the tray at once (no settle): SetStowed(true).</summary>
        public void LayOnTray() { ReleaseAll(); for (int i = 0; i < 2; i++) PlaceAtRest(_m[i]); }

        // =============================== per frame ===============================

        /// <summary>Before the pointers are read: held mallets follow their hands (snap, the sticks' Smoothing 1); a lost hand
        /// freezes its mallet, silences its head and drops it after 1.5 s; released mallets settle onto the tray.</summary>
        public void UpdatePoses(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                MarimbaMalletTool m = _m[i];
                if (m.Held)
                {
                    HandFrame f = _frames?.Frame(m.Hand);
                    if (f == null || !f.isTracked)
                    {
                        m.LostFor += dt;
                        m.Smoothed = false;
                        m.HeadPointer.Down = false;
                        if (m.LostFor > LostGrace) Release(m);
                        continue;
                    }
                    m.LostFor = 0f;
                    m.HeadPointer.Down = true;
                    MarimbaToolMath.Grip(f.position[(int)HandJoint.Wrist], f.rotation[(int)HandJoint.Wrist],
                        f.position[(int)HandJoint.ThumbTip], f.position[(int)HandJoint.IndexTip], out m.Grip, out m.Axis);
                    m.Smoothed = true;
                    MarimbaToolMath.Pose(m.Grip, m.Axis, out Vector3 butt, out Quaternion rot, out Vector3 head);
                    m.Butt = butt; m.Rotation = rot; m.Head = head;
                    m.HeadPointer.Next = head;
                    continue;
                }
                if (m.Settle < 1f)
                {
                    m.Settle = Mathf.Min(1f, m.Settle + dt * SettleRate);
                    float k = MarimbaToolMath.SettleEase(m.Settle);
                    RestLocal(i, out Vector3 rb, out Quaternion rr);
                    Vector3 lb = Vector3.Lerp(m.SettleFromButt, rb, k);
                    Quaternion lr = Quaternion.Slerp(m.SettleFromRot, rr, k);
                    m.SettleFromButt = lb; m.SettleFromRot = lr;   // each frame eases on from where it is (the sticks' rule)
                    SetWorld(m, ToWorld(lb), RigRotation * lr);
                    if (m.Settle >= 1f) PlaceAtRest(m);
                }
            }
        }

        /// <summary>After the grab pass: open = the pinch has parted (and Meta's pinch too) AND the fist has opened; either grip
        /// keeps the mallet. Untracked joints say nothing (the tracking-loss grace handles them).</summary>
        public void UpdateReleases(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                MarimbaMalletTool m = _m[i];
                HandState h = m.Hand;
                if (h == null || !h.Connected) continue;
                HandFrame f = _frames?.Frame(h);
                if (f == null || !f.isTracked) continue;
                float gap = Vector3.Distance(f.position[(int)HandJoint.ThumbTip], f.position[(int)HandJoint.IndexTip]);
                m.OpenFor = gap > OpenDistance && !h.Grasping && !h.GrabPinching ? m.OpenFor + dt : 0f;
                if (m.OpenFor > OpenDwell) Release(m);
            }
        }

        /// <summary>IHandToolProvider.ShadowSpheres (E3): the held heads' centres and radius, for the contact shadows.</summary>
        public int ShadowSpheres(Span<Vector4> dst)
        {
            int n = 0;
            for (int i = 0; i < 2 && n < dst.Length; i++)
                if (_m[i].Held && _m[i].HeadPointer.Down) dst[n++] = new Vector4(_m[i].Head.x, _m[i].Head.y, _m[i].Head.z, HeadRadius);
            return n;
        }

        /// <summary>Layout's rule for a pick-up attempt (LayoutModule.Grab: a grab pinch, or a closing fist once the hand stopped
        /// playing). The module calls TryPickUp when this is true (phase B: Layout asks the nearest provider).</summary>
        public static bool WantsPickUp(HandState h, bool playing) => h != null && h.Connected && h.Holding == null && (h.GrabPinchStart || (h.GraspStart && !playing));
    }
}
