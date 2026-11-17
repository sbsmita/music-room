// The kit's data and every tuning constant (kit, sticks, nod kick, play zones), verbatim from Docs/05-instruments.md
// sections 3-4 and 7 (drums, sticks, sticks build, curves). Positions are kit-local Unity metres (source-frame z
// negated); tilts keep the source rot.x in radians because the formulas use them (Unity pitch = -tilt in degrees).
// Nothing here allocates per frame.
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Drums
{
    /// <summary>One piece of the kit as the kit table builds it (after the cymbal move).</summary>
    public readonly struct PieceSpec
    {
        public readonly int Id;
        public readonly string Kind;        // recorder / lesson kind ("kick" for the pad too)
        public readonly DrumPieceType Type;
        public readonly float R;            // head / plate radius
        public readonly float Depth;        // shell depth (drums), 0 for cymbals
        public readonly bool Pad;
        public readonly bool HiHat;
        public readonly Vector3 Pos;        // kit-local Unity
        public readonly float TiltRad;      // source rotation.x; Unity localRotation = Euler(-tilt deg, 0, 0)
        public readonly float LegTurnRad;   // stand tripod turn (legTurn)
        public readonly DrumKind Audio;     // what IAudioEngine.Drum plays for a plain hit (the pad plays the kick)
        public readonly SourceId Source;    // the spatial source the hit sounds from

        public PieceSpec(int id, string kind, DrumPieceType type, float r, float depth, bool pad, bool hiHat, Vector3 pos,
            float tiltRad, float legTurnRad, DrumKind audio, SourceId source)
        {
            Id = id; Kind = kind; Type = type; R = r; Depth = depth; Pad = pad; HiHat = hiHat; Pos = pos;
            TiltRad = tiltRad; LegTurnRad = legTurnRad; Audio = audio; Source = source;
        }

        public Quaternion LocalRotation => Quaternion.Euler(-TiltRad * Mathf.Rad2Deg, 0f, 0f);
        public bool IsCymbal => Type == DrumPieceType.Cymbal;
    }

    public static class DrumSpec
    {
        public const int PieceCount = 9;
        public const int SnareId = 0, Tom1Id = 1, Tom2Id = 2, FloorId = 3, KickId = 4, KickPadId = 5, HatId = 6, CrashId = 7, RideId = 8;

        // Piece kinds as the recorder and the lessons spell them (TakeEvent.kind). Interned constants: no allocation.
        public const string Snare = "snare", Tom1 = "tom1", Tom2 = "tom2", Floor = "floor", Kick = "kick", Hat = "hat", Crash = "crash", Ride = "ride";

        public const float DefaultLegTurn = 0.4f;
        public const float RideLegTurn = 0.91f;     // the ride's legs straddle the floor tom

        /// <summary>The smallest gap between any two pieces of the kit as built (shells with their hoops and lugs, heads,
        /// the pad, cymbal plates): the user asked for "a little space" between drums that touched (DrumSpacingTests).</summary>
        public const float MinPieceGap = 0.02f;

        /// <summary>The nine pieces. Ids are the IDrumsApi / spec 05 section 3 ids (0 snare ... 8 ride).
        /// Spacing (2026-11-14): the reference put the rack toms (-.02, .86, .54) and (.29, .85, .42) and the pad
        /// (-.27, .6, .2); built, the small tom's near shell swallowed the snare's far hoop 5.4 cm deep, the big tom's
        /// 2.2 cm, the toms' lugs touched and the pad's body met the snare's bottom hoop. The toms slide back (and a
        /// little apart) and the pad a little left, just enough for a 2 cm gap; heights, tilts, sizes and every other
        /// piece stay as they were.</summary>
        public static readonly PieceSpec[] Pieces =
        {
            new PieceSpec(0, Snare, DrumPieceType.Drum, 0.18f, 0.14f, false, false, new Vector3(0f, 0.72f, 0.30f), 0.10f, DefaultLegTurn, DrumKind.Snare, SourceId.Snare),
            new PieceSpec(1, Tom1, DrumPieceType.Drum, 0.15f, 0.20f, false, false, new Vector3(-0.04f, 0.86f, 0.625f), 0.45f, DefaultLegTurn, DrumKind.Tom1, SourceId.Tom1),
            new PieceSpec(2, Tom2, DrumPieceType.Drum, 0.165f, 0.22f, false, false, new Vector3(0.32f, 0.85f, 0.482f), 0.45f, DefaultLegTurn, DrumKind.Tom2, SourceId.Tom2),
            new PieceSpec(3, Floor, DrumPieceType.Drum, 0.20f, 0.36f, false, false, new Vector3(0.38f, 0.62f, 0.02f), 0.05f, DefaultLegTurn, DrumKind.Floor, SourceId.Floor),
            // The kick's batter head faces the player (rotation.x = pi/2): its shell ends 1.11 m out.
            new PieceSpec(4, Kick, DrumPieceType.Drum, 0.28f, 0.40f, false, false, new Vector3(0.12f, 0.29f, 0.70f), Mathf.PI / 2f, DefaultLegTurn, DrumKind.Kick, SourceId.Kick),
            // Hands can't reach a pedal: the kick also lives on a pad beside the snare.
            new PieceSpec(5, Kick, DrumPieceType.Drum, 0.10f, 0.03f, true, false, new Vector3(-0.29f, 0.60f, 0.19f), 0.25f, DefaultLegTurn, DrumKind.Kick, SourceId.Kick),
            new PieceSpec(6, Hat, DrumPieceType.Cymbal, 0.17f, 0f, false, true, new Vector3(-0.42f, 0.80f, 0.36f), 0.12f, DefaultLegTurn, DrumKind.Hat, SourceId.Hat),
            new PieceSpec(7, Crash, DrumPieceType.Cymbal, 0.21f, 0f, false, false, new Vector3(-0.39f, 1.02f, 0.58f), 0.35f, DefaultLegTurn, DrumKind.Crash, SourceId.Crash),
            new PieceSpec(8, Ride, DrumPieceType.Cymbal, 0.22f, 0f, false, false, new Vector3(0.48f, 1.04f, 0.27f), 0.30f, RideLegTurn, DrumKind.Ride, SourceId.Ride),
        };

        // ---- rig (ARCHITECTURE.md section 8) ----
        public static readonly Vector3 HomeAll = new Vector3(0.2f, 0f, -0.26f);
        public const float HomeYawAll = 90f;
        public static readonly Vector3 HomeSingle = new Vector3(0f, 0f, 0.10f);   // KIT_FORWARD .1
        public const float HomeYawSingle = 0f;
        public const int RigPriority = 1;
        /// <summary>The DrumRoom source (the drum room return) at the kit centre, kit-local.</summary>
        public static readonly Vector3 KitCentre = new Vector3(0.05f, 0.8f, 0.4f);

        // ---- hi-hat ----
        public const float HatClosedY = -0.016f;   // lower cymbal under the top one, closed (foot on the pedal)
        public const float HatOpenY = -0.05f;
        public const float HatEaseRate = 18f;      // y += (target - y) * min(1, 18 dt)
        public const float HatCloseVel = 0.7f;     // the "chick" when the cymbals meet
        // Stand pinch zone: kit-local, around the hat's pole.
        public const float HatStandRadius = 0.08f, HatStandMinY = 0.12f, HatStandBelowHat = 0.08f;

        // ---- hit test ----
        public const float ArmAbove = 0.02f;        // re-arm once clearly above the head
        public const float FingerMaxPrev = 0.10f;   // fingertips start a stroke from close above the head
        public const float StickMaxPrev = 0.35f;    // sticks come from higher up
        public const float RimExtra = 0.03f;        // a drum answers 3 cm past its head: the rim
        public const float RimFraction = 0.9f;      // rr > .9 r is a rim shot (not on the pad)
        // ---- the modelled kit's strike contract (10-upgrade drums.md 2.3; used only while it plays) ----
        /// <summary>What a fingertip strikes with. drums.md 2.3 suggests Finger (a bare fingertip: <= 3.6 m/s, a soft
        /// contact); measured on the reference kit that is 11-29 dB quieter than a stick at the same stroke value (snare
        /// -23.6 dB at u .8) and dull, while the kit's levels and the app-level A/B were calibrated with stick strokes.
        /// Fingertips are this hands-first app's main way to play, so they strike as a stick tip: today's loudness and
        /// feel, the spec's calibration. Set Finger for real hand-drumming.</summary>
        public const DrumImplement FingertipImplement = DrumImplement.Tip;
        public const float FingerV0 = 0.12f, FingerV1 = 1.5f;   // m/s: ghost note .. as loud as the kit goes
        public const float StickV0 = 0.3f, StickV1 = 3.0f;
        public const float VelocityExponent = 0.7f;
        public const float MinVelocity = 0.05f;
        public const double SameFrameMs = 3;        // per-piece duplicate window
        public const double OwnerWindowMs = 45;     // per hand / per stick window
        public const float MinSampleDt = 1e-3f;

        // ---- choke ----
        public const double ChokeAfterMs = 60, ChokeWithinMs = 3000;
        public const float ChokeMaxSpeed = 0.35f, ChokeBand = 0.03f, ChokeStill = 0.004f;

        // ---- strike zones ----
        public const float HatEdgeFrom = 0.75f, BellBelow = 0.22f, EdgeFrom = 0.85f;
        public const float DefaultStrikePos = 0.3f;

        // ---- visuals (experience.md E7) ----
        /// <summary>The hit flash fades at 6 per second (diagnostics; it no longer scales anything).</summary>
        public const float FlashDecay = 6f;
        /// <summary>E7: 0. Scaling the whole drum by 1 + .015 flash reads as synthetic "breathing"; the batter
        /// head's give (HeadGive) shows the hit instead, and the cymbals rock (CymbalMotion) instead of the old wobble.</summary>
        public const float FlashScale = 0f;

        // ---- hover ----
        public const float HoverAbove = 0.1f, HoverBelow = 0.005f, HoverPad = 0.02f, HoverInset = 0.03f, HoverLift = 0.002f, HoverSize = 0.09f;

        // ---- play zones (DRUM_PAD / DRUM_BELOW / DRUM_ABOVE) and the rig zone ----
        public const float ZonePad = 0.04f, ZoneBelow = 0.12f, ZoneAbove = 0.25f;
        public const float RigZonePad = 0.12f;      // every rig zone is its group's box expanded by ZONE_PAD

        // ---- desktop click ----
        public const float DesktopVelocity = 0.75f;
        public const float DesktopRayLength = 3f;
        /// <summary>The click collider: a closed disc of the piece's radius, 4 mm thick about the strike plane, 32 sides
        /// (a click target only; the strike zone still comes from the hit point's radius).</summary>
        public const float ClickThickness = 0.004f;
        public const int ClickSegments = 32;

        // ---- construction (Docs/05-instruments.md, drums build) ----
        public const int ShellSegments = 48, RimRadialSegments = 10, RimTubularSegments = 64;
        /// <summary>E7: the batter head is a ringed disc (HeadGive.Rings x ShellSegments + 1 = 289 vertices) so it can dent.</summary>
        public const int HeadRings = HeadGive.Rings;
        public const float RimOffset = 0.006f, RimTube = 0.007f, HeadLift = 0.001f;
        public const int LugCount = 8;
        public static readonly Vector3 LugSize = new Vector3(0.018f, 0.05f, 0.014f);   // x radial, y up, z tangential
        public const float LugOffset = 0.012f;
        public const float PadBodyHeight = 0.03f, PadRimTube = 0.008f, PadTopRadius = 0.095f;
        public const int PadSegments = 32, PadRimRadialSegments = 8, PadRimTubularSegments = 40;
        public const int CymbalSegments = 64;
        public const float NutRadius = 0.01f, NutHeight = 0.02f, NutY = 0.022f;
        public const int NutSegments = 12;
        // Stands: a pole from the floor to the top, three legs from a hip at .32 to feet .3 out.
        public const float PoleRadius = 0.011f, LegRadius = 0.007f, HipY = 0.32f, FootY = 0.01f, LegReach = 0.3f;
        public const int PoleSegments = 10, LegSegments = 6;
        public const float StandDrumTopFraction = 0.6f, StandPadDrop = 0.04f, StandCymbalDrop = 0.03f;
        // Kick extras: two spurs and a decorative pedal (Unity kit-local).
        public static readonly Vector3 SpurA0 = new Vector3(0.37f, 0.06f, 0.9f), SpurB0 = new Vector3(0.52f, 0f, 0.95f);
        public static readonly Vector3 SpurA1 = new Vector3(-0.13f, 0.06f, 0.9f), SpurB1 = new Vector3(-0.28f, 0f, 0.95f);
        public const float SpurRadius = 0.008f;
        public const int SpurSegments = 6;
        public static readonly Vector3 PedalSize = new Vector3(0.09f, 0.012f, 0.22f);
        public static readonly Vector3 PedalPos = new Vector3(0.12f, 0.02f, 0.54f);
        public const float PedalPitchDeg = 14.3239f;   // source rotation.x = -.25
        // The contact shadow under the kit (Docs/07-world-visuals.md, rigDecor.drumsBlob), kit-local. The Room module draws
        // it (RigShadows, opacity .45); the kit only counts its rectangle in the rig zone's box.
        public static readonly Vector3 BlobPos = new Vector3(0.1f, 0.005f, 0.5f);
        public const float BlobWidth = 1.9f, BlobDepth = 1.6f;

        /// <summary>Cymbal lathe profile (radius, height): bell 16 mm above the strike plane, edge at 0.</summary>
        public static Vector2[] CymbalProfile(float r) => new[]
        {
            new Vector2(0.004f, 0f), new Vector2(0.03f, 0.016f), new Vector2(0.06f, 0.012f), new Vector2(r * 0.6f, 0.006f),
            new Vector2(r, 0f), new Vector2(r, -0.0015f), new Vector2(0.004f, -0.0015f),
        };

        /// <summary>The height of a cymbal plate's DRAWN top at radius rr (the top polyline of CymbalProfile: 0 at the hole, the
        /// 16 mm bell at 3 cm, 12 mm at 6 cm, 6 mm at 0.6 r, 0 at the edge), for the contact visuals (E1 to E3): the hit test
        /// keeps the plate's y = 0 strike plane.</summary>
        public static float CymbalTopY(float r, float rr)
        {
            float r6 = Mathf.Max(0.0601f, r * 0.6f);
            if (rr <= 0.004f || rr >= r) return 0f;
            if (rr <= 0.03f) return 0.016f * (rr - 0.004f) / (0.03f - 0.004f);
            if (rr <= 0.06f) return 0.016f + (0.012f - 0.016f) * (rr - 0.03f) / (0.06f - 0.03f);
            if (rr <= r6) return 0.012f + (0.006f - 0.012f) * (rr - 0.06f) / (r6 - 0.06f);
            return 0.006f * (r - rr) / Mathf.Max(1e-4f, r - r6);
        }

        /// <summary>The drawn top of a piece at radius rr from its centre: the cymbal's plate profile, else the batter head
        /// (or the pad's top) at HeadLift.</summary>
        public static float DrawnTopY(bool cymbal, float r, float rr) => cymbal ? CymbalTopY(r, rr) : HeadLift;

        /// <summary>Top of a piece's stand in the kit frame, untilted.</summary>
        public static Vector3 StandTop(in PieceSpec p)
        {
            float drop = p.IsCymbal ? StandCymbalDrop : p.Pad ? StandPadDrop : p.Depth * StandDrumTopFraction;
            return new Vector3(p.Pos.x, p.Pos.y - drop, p.Pos.z);
        }

        /// <summary>True for the pieces that stand on a tripod (everything but the kick shell).</summary>
        public static bool HasStand(in PieceSpec p) => p.Id != KickId;

        /// <summary>Foot k (0..2) of a stand: a = k 120 deg + legTurn; Unity offset (cos a, 0, -sin a) * .3 (source frame (cos a, 0, sin a)).</summary>
        public static Vector3 LegFoot(Vector3 top, int k, float legTurnRad)
        {
            float a = k / 3f * Mathf.PI * 2f + legTurnRad;
            return new Vector3(top.x + Mathf.Cos(a) * LegReach, FootY, top.z - Mathf.Sin(a) * LegReach);
        }

        public static Vector3 LegHip(Vector3 top) => new Vector3(top.x, HipY, top.z);

        // ---- materials (Docs/05-instruments.md, drums build materials; roughness r -> smoothness 1 - r, ARCHITECTURE.md section 13) ----
        public const string PearlHex = "#eae2cf";        // snare shell, r .3
        public const float PearlSmoothness = 0.7f;
        public const string ShellHex = "#5b0f1f";        // toms, floor, kick: r .2 with a full clear coat (r .1)
        public const float ShellSmoothness = 0.85f;      // no clear coat in URP Lit on Quest: a little extra gloss stands in for it
        public const string ChromeHex = "#d9dde2";       // metalness 1, r .16
        public const float ChromeSmoothness = 0.84f;
        public const string RubberHex = "#1a1a1a";       // r .95
        public const float RubberSmoothness = 0.05f;
        public const float HeadSmoothness = 0.15f;       // drumHead speckle texture, r .85
        public const float HeadTextureRepeat = 2f;
        public const string HeadClearHex = "#c9c2b4";    // resonant heads, r .6, opacity .85
        public const float HeadClearSmoothness = 0.4f, HeadClearOpacity = 0.85f;
        public const float CymbalMetallic = 0.9f;        // roughness from the ring map (smoothness in the map's alpha)
        public const string FeltHex = "#e6dfd0";         // E7 kick beater ball: undyed wool felt
        public const float FeltSmoothness = 0.05f;
        public const int BeaterShaftSegments = 8, BeaterBallWidthSegments = 14, BeaterBallHeightSegments = 10;
    }

    /// <summary>Drumstick constants (Docs/05-instruments.md section 4, sticks build).</summary>
    public static class StickSpec
    {
        public const float Length = 0.4f;
        public const float GripFromButt = 0.13f;
        public const float ShoulderFromButt = Length - 0.08f;   // 0.32
        public const float TiltRad = -0.22f;      // droop below the hand's forward line (about 12.6 degrees)
        public const float GrabRadius = 0.12f;    // pinch point to the nearest point on the shaft
        public const float HoverRadius = 0.14f;
        public const float DeadZone = 0.24f;      // a miss this close does nothing rather than grabbing the kit
        public const float OpenDistance = 0.05f;  // thumb tip to index tip: beyond this the hand is open
        public const float OpenDwell = 0.25f;     // seconds open before the stick is let go
        public const float LostGrace = 1.5f;      // seconds of tracking loss before a held stick drops
        public const float SettleRate = 2.5f;     // settle += 2.5 dt: back on the snare in 0.4 s
        public const float GripReach = 0.02f;     // a near miss still grabs a rig when one of its handles is this close
        // Hover glow.
        public const float HoverLift = 0.014f, HoverLength = 0.12f, HoverWidth = 0.035f;
        // Mesh.
        public const float ShaftRadiusButt = 0.0075f, ShaftRadiusTip = 0.0045f;
        public const int ShaftSegments = 10;
        public const float TipRadius = 0.0062f;
        public const int TipWidthSegments = 10, TipHeightSegments = 8;
        public const float TapeRadius = 0.008f, TapeLength = 0.09f, TapeCentre = 0.08f;
        public const string WoodHex = "#d9b88a";  // r .6
        public const float WoodSmoothness = 0.4f;
        public const string TapeHex = "#222222";  // r .9
        public const float TapeSmoothness = 0.1f;
        // Rest pose: snare-local (-+.068, .009, -.188) Unity (stick 0 on the -x side), source-frame XYZ
        // Euler(-pi/2, 0, -+.35).
        public const float RestSnareX = 0.068f, RestSnareY = 0.009f, RestSnareZ = -0.188f;
        public const float RestRollRad = 0.35f;
    }

    /// <summary>The nod kick.</summary>
    public static class NodSpec
    {
        public const float NodSpeed = 1.6f;     // rad/s of downward pitch that counts as a nod
        public const float NodMax = 6.5f;       // rad/s for a full-loudness kick
        public const float Debounce = 0.18f;    // s between kicks
        public const float Window = 0.16f;      // s a downward swing may last before it is just looking down
        public const float Rearm = 0.5f;        // re-arm once the rate falls below NodSpeed * .5
        public const float MinVelocity = 0.25f;
        public const float StrikePos = 0.2f;    // { pos: 0.2, zone: 'head' }
    }
}
