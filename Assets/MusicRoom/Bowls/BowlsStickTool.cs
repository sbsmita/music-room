// The puja (the bowl stick) as the platform's hand tool (platform.md 4.6; BUILD-GUIDE 4.2): HandState.Holding is this
// object while a hand holds it, and it owns two tool pointers that Core hands to the bowls' Tick (descriptor Tools =
// Striker | RubStick): the suede end (ToolKind.Striker: a soft strike) and the plain end of the shaft (ToolKind.RubStick:
// the wood that rubs the rim). The two tips are the ends of the segment the hand logic rubs and strikes with. Layout's
// grab pass asks the provider (NearestFree / TryPickUp at the thumb-index midpoint) and the bowls' module moves it in
// FramePhase.StickPoses, before PointerInput reads its pointers. The rules are BowlsPuja's (the drumsticks').
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Bowls
{
    /// <summary>Hand joints for a hand (the app's PointerInput.Frame, or a test's).</summary>
    public interface IBowlsHandFrames
    {
        HandFrame Frame(HandState hand);
    }

    public sealed class BowlsStickTool : IHandTool, IPointerOwner, IHeldBy
    {
        public readonly BowlsPuja Puja = new BowlsPuja();
        /// <summary>The suede end (Striker) and the plain end of the shaft (RubStick): Down while held and tracked.</summary>
        public readonly Pointer Suede, Shaft;
        readonly Pointer[] _pointers;
        readonly IBowlsHandFrames _frames;

        public HandState Hand { get; private set; }
        public HandState Holder => Hand;
        public IReadOnlyList<Pointer> Pointers => _pointers;
        public double LastPlayMs { get; set; } = double.NegativeInfinity;
        public bool Held => Hand != null;
        /// <summary>Raised on pick-up (true) and release (false).</summary>
        public event Action<bool> Changed;

        public BowlsStickTool(IBowlsHandFrames frames)
        {
            _frames = frames;
            Suede = Pointer.CreateTool("bowls-puja-suede", this, ToolKind.Striker);
            Shaft = Pointer.CreateTool("bowls-puja-shaft", this, ToolKind.RubStick);
            _pointers = new[] { Suede, Shaft };
        }

        /// <summary>The thumb-index midpoint when the hand is tracked, else the given tip (the drumsticks' pinch point).</summary>
        public Vector3 PinchPoint(HandState h, Vector3 fallback)
        {
            HandFrame f = _frames?.Frame(h);
            if (f != null && f.isTracked) return (f.position[(int)HandJoint.ThumbTip] + f.position[(int)HandJoint.IndexTip]) * 0.5f;
            return fallback;
        }

        public float NearestFree(Vector3 tip) => Puja.NearestFree(tip);

        public StickPick TryPickUp(HandState h, Vector3 tip)
        {
            if (h == null || h.Holding != null || Held) return StickPick.None;
            BowlsPick r = Puja.TryPickUp(h.Index, false, PinchPoint(h, tip));
            if (r == BowlsPick.Near) return StickPick.Near;
            if (r != BowlsPick.Grabbed) return StickPick.None;
            Hand = h;
            h.Holding = this;
            UpdatePointers(true);
            Changed?.Invoke(true);
            return StickPick.Grabbed;
        }

        /// <summary>Back onto the stand (settling 0.4 s).</summary>
        public void Release()
        {
            if (Hand == null) return;
            if (ReferenceEquals(Hand.Holding, this)) Hand.Holding = null;
            Hand = null;
            Puja.Release();
            Suede.Down = Suede.Active = Shaft.Down = Shaft.Active = false;
            Changed?.Invoke(false);
        }

        /// <summary>FramePhase.StickPoses: the held stick follows its hand (the pinch, Meta's pinch or a fist keep it; open
        /// for 0.25 s lets it go; tracking lost: frozen and silent, dropped after 1.5 s); a released one settles.</summary>
        public void UpdatePoses(float dt, Matrix4x4 rigToWorld)
        {
            Puja.SetRig(rigToWorld);
            if (Hand == null) { Puja.UpdateHeld(dt, false, default, default, default, default, false, false); return; }
            HandFrame f = _frames?.Frame(Hand);
            bool tracked = f != null && f.isTracked && Hand.Connected;
            bool letGo = tracked
                ? Puja.UpdateHeld(dt, true, f.position[(int)HandJoint.Wrist], f.rotation[(int)HandJoint.Wrist], f.position[(int)HandJoint.ThumbTip], f.position[(int)HandJoint.IndexTip], Hand.GrabPinching, Hand.Grasping)
                : Puja.UpdateHeld(dt, false, default, default, default, default, false, false);
            if (letGo || !Puja.Held) { Release(); return; }
            UpdatePointers(tracked);
        }

        void UpdatePointers(bool down)
        {
            Puja.Ends(out Vector3 suede, out Vector3 plain);
            Suede.Next = suede; Shaft.Next = plain;
            Suede.Down = Shaft.Down = down;
        }

        public int ShadowSpheres(Span<Vector4> into) => Puja.ShadowSpheres(into);
    }

    /// <summary>The bowls' IHandToolProvider (Layout's grab pass): the stick only while the bowls are at the seat.</summary>
    public sealed class BowlsToolProvider : IHandToolProvider
    {
        readonly BowlsStickTool _tool;
        readonly Func<bool> _present;
        public BowlsToolProvider(BowlsStickTool tool, Func<bool> present) { _tool = tool; _present = present; }
        public InstrumentKind Owner => InstrumentKind.Bowls;
        public float NearestFree(Vector3 tip) => _present() ? _tool.NearestFree(tip) : float.PositiveInfinity;
        public StickPick TryPickUp(HandState hand, Vector3 tip) => _present() ? _tool.TryPickUp(hand, tip) : StickPick.None;
        public bool AnyHeld => _tool.Held;
        public void ReleaseAll() => _tool.Release();
        public bool Holds(HandState hand) => hand != null && _tool.Held && ReferenceEquals(_tool.Hand, hand);
        public void Release(HandState hand) { if (Holds(hand)) _tool.Release(); }
        public int ShadowSpheres(Span<Vector4> centreRadius) => _tool.ShadowSpheres(centreRadius);
    }
}
