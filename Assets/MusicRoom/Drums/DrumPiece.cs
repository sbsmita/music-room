// Runtime state of one kit piece (plus the hi-hat fields). The group's local
// y = 0 plane is the strike surface; every hit test runs in the group frame (InverseTransformPoint includes the flash
// scale and any page-turn scale of the kit).
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Drums
{
    public sealed class DrumPiece
    {
        public readonly PieceSpec Spec;
        public readonly Transform Group;
        public int Id => Spec.Id;
        public string Kind => Spec.Kind;
        public DrumPieceType Type => Spec.Type;
        public float R => Spec.R;
        public bool Pad => Spec.Pad;
        public bool IsCymbal => Spec.Type == DrumPieceType.Cymbal;
        public bool IsHat => Spec.HiHat;

        /// <summary>The top cymbal mesh (only it rocks); the batter head for drums.</summary>
        public Transform Top;
        /// <summary>The hi-hat's lower cymbal (null on every other piece).</summary>
        public Transform Lower;

        public float Flash;            // 1 on a hit, -6/s (diagnostics)
        /// <summary>E7, cymbals: the plate's rocking (CymbalMotion); written to Top only while Tilt.Moving.</summary>
        public CymbalTilt Tilt;
        /// <summary>E7, batter heads (not the pad): the head's give under a hit (null on cymbals and the pad).</summary>
        public HeadGive Give;
        public float LowerY = DrumSpec.HatClosedY;   // hi-hat lower plate height as last written
        public double LastHitMs = double.NegativeInfinity;
        public bool Choked;
        public bool Open;              // hi-hat only: closed by default, the way a foot on the pedal keeps it
        public int Hits;               // diagnostics / tests

        public DrumPiece(in PieceSpec spec, Transform group)
        {
            Spec = spec;
            Group = group;
        }

        public DrumPieceInfo Info => new DrumPieceInfo { Id = Spec.Id, Kind = Spec.Kind, Type = Spec.Type, Radius = Spec.R, Pad = Spec.Pad, Transform = Group };
    }
}
