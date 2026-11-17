// The instrument-facing input model (spec 05 section 1.2): hands with five fingertip pointers in the
// tip order [index, middle, ring, little, thumb] plus external pointers (drumstick tip and shoulder).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    public enum Handedness { Left = 0, Right = 1 }

    /// <summary>Index into HandState.Pointers (tip order). Pointers[0] is THE pinch / grab pointer.</summary>
    public enum PointerSlot { Index = 0, Middle = 1, Ring = 2, Little = 3, Thumb = 4 }

    /// <summary>Something a hand can hold (a drumstick). While Holding != null the hand's fingertip pointers are muted.</summary>
    public interface IHandTool { }

    public interface IPointerOwner
    {
        IReadOnlyList<Pointer> Pointers { get; }
        /// <summary>Clock.NowMs of the last play (press, hit, choke, hat toggle, pluck, catch, resting hand). -inf when never.</summary>
        double LastPlayMs { get; set; }
    }

    public sealed class HandState : IPointerOwner
    {
        public readonly Handedness Side;
        public readonly int Index;                 // 0 left, 1 right (== (int)Side)
        public bool Connected;                     // tracked this frame
        public bool Pinching, PinchStart, PinchEnd; public float PinchFor;      // seconds pinched
        /// <summary>The grab pinch (HandInput.grabPinching): the thumb-index rule above OR Meta's own index pinch (pinch
        /// strength with hysteresis .85 / .5, or IsIndexPinching). Layout grabs with it; the other gestures keep Pinching.</summary>
        public bool GrabPinching, GrabPinchStart, GrabPinchEnd; public float GrabPinchFor;
        /// <summary>Meta's index pinch alone (with hysteresis) and its strength 0..1 (0 when the runtime gives none).</summary>
        public bool MetaPinching; public float PinchStrength;
        public bool Grasping, GraspStart, GraspEnd;
        public IHandTool Holding;                  // null = free
        /// <summary>The frames come from the desktop MouseHandSource. Desktop model (ARCHITECTURE section 0): its pointers
        /// never strike, poke or pluck (the modules' desktop click paths own the mouse); hover and gestures may use them.</summary>
        public bool IsMouse;
        public double LastPlayMs { get; set; } = double.NegativeInfinity;
        /// <summary>The instrument this hand played last (PlayStamp.Mark; InstrumentKind.Table when none yet). With LastPlayMs
        /// it is the contactless guard (platform.md 4.6: no contactless instrument within 0.4 s of playing another one).</summary>
        public InstrumentKind LastInstrument = InstrumentKind.Table;
        public readonly Pointer[] Pointers = new Pointer[5];
        IReadOnlyList<Pointer> IPointerOwner.Pointers => Pointers;

        public HandState(Handedness side)
        {
            Side = side;
            Index = (int)side;
            string n = side == Handedness.Left ? "L" : "R";
            // Hand joints in OpenXR order: IndexTip 10, MiddleTip 15, RingTip 20, LittleTip 25, ThumbTip 5.
            Pointers[0] = new Pointer(n + "-index", this, 10);
            Pointers[1] = new Pointer(n + "-middle", this, 15);
            Pointers[2] = new Pointer(n + "-ring", this, 20);
            Pointers[3] = new Pointer(n + "-little", this, 25);
            Pointers[4] = new Pointer(n + "-thumb", this, 5);
        }

        public Pointer IndexPointer => Pointers[0];
        public Pointer ThumbPointer => Pointers[4];
    }

    public sealed class Pointer
    {
        public const int DrumPieceCount = 9;

        public readonly string Id;
        public readonly IPointerOwner Owner;
        /// <summary>HandJoint index for hand pointers, -1 for external pointers.</summary>
        public readonly int Joint;
        /// <summary>Which fingertip (hand pointers), -1 for external ones.</summary>
        public readonly int Finger;

        public Vector3 Tip, Prev, Next, Vel;
        /// <summary>|Vel| in m/s, over SampleDt (the learned gap between pose changes).</summary>
        public float Speed;
        public float SampleDt;
        public double SampleT;
        /// <summary>Fingertip velocity between distinct tracker samples (HandFrame.tipVelocity), hand pointers only.</summary>
        public Vector3 TrackerVel;
        public bool Active, WasActive;
        public bool External, Down, Stick, Shoulder;
        /// <summary>The tool this pointer is (platform.md 4.6): Stick for the kit's sticks, None for fingertips. Core builds
        /// each instrument's pointer list from its ToolMask.</summary>
        public ToolKind Tool;
        /// <summary>A dense, stable index (0..9 the fingertips, 10.. external pointers in registration order) for per-pointer
        /// state tables in instruments: size them PointerInput.MaxSlots.</summary>
        public int Slot = -1;
        /// <summary>Who owns this fingertip this frame (PointerClaims, Core step 3b): the present contact instrument whose zone
        /// contains it, the shown menu, Sunny / another IPointerClaimant, or null. Contactless instruments play only from
        /// unclaimed hands; contact instruments ignore it.</summary>
        public object ClaimedBy;
        /// <summary>Per drum piece: Clock.NowMs of this pointer's last hit (-inf) and arm state (-1 unset, 0 disarmed, 1 armed).</summary>
        public readonly double[] DrumLastHit = new double[DrumPieceCount];
        public readonly sbyte[] DrumArm = new sbyte[DrumPieceCount];

        public Pointer(string id, IPointerOwner owner, int joint, bool external = false)
        {
            Id = id;
            Owner = owner;
            Joint = joint;
            Finger = joint == 5 ? 0 : joint == 10 ? 1 : joint == 15 ? 2 : joint == 20 ? 3 : joint == 25 ? 4 : -1;
            External = external;
            for (int i = 0; i < DrumPieceCount; i++) { DrumLastHit[i] = double.NegativeInfinity; DrumArm[i] = -1; }
        }

        /// <summary>An external pointer (stick tip / shoulder) owned by <paramref name="owner"/>; position comes from Next while Down.</summary>
        public static Pointer CreateExternal(string id, IPointerOwner owner, bool stick, bool shoulder)
            => new Pointer(id, owner, -1, true) { Stick = stick, Shoulder = shoulder, Tool = stick ? ToolKind.Stick : ToolKind.None };

        /// <summary>A tool pointer (mallet head, striker, rub stick): register it with PointerInput.RegisterExternal.</summary>
        public static Pointer CreateTool(string id, IPointerOwner owner, ToolKind tool)
            => new Pointer(id, owner, -1, true) { Tool = tool };

        /// <summary>A fingertip of the desktop mouse hand: instruments skip it for play (see HandState.IsMouse).</summary>
        public bool FromMouse => Owner is HandState h && h.IsMouse;

        public void Deactivate()
        {
            Active = WasActive = false;
            Vel = TrackerVel = Vector3.zero;
            Speed = 0f;
        }
    }
}
