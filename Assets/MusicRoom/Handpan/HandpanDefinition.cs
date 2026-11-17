// The handpan's registry descriptor (BUILD-GUIDE 4.1; handpan.md; the id block of platform.md 2: Kind / Rig / Unit 5,
// SourceId.Handpan 18, Carousel 4, Order 33, TickOrder 21). Static data only.
//   Footprint (rig-local, heights above the floor with the rig at Home (0, 0, 0.38) in the seat frame):
//     shell + band: the rim (0.537 m across with the band) tilted 28 deg about its centre 0.74 m up: x +-0.272, z +-0.240,
//       y 0.62..0.88 (the near rim's lowest point is 0.616 m, as the platform's placeholder rounded it);
//     stand: the cradle tripod below 0.62 m stays beyond the knees (z >= 0.06 rig-local = 0.44 m ahead of the eyes: the
//       planner's knee box ends at 0.41 m, plus its 3 cm gap): feet at (+-0.30, 0.13) and (0, 0.30).
//   Display: on a wall bracket on the left wall (the platform's spot), the shell upright, its top facing the room: 2 draws.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Handpan
{
    public sealed class HandpanDefinition : IInstrumentDefinition
    {
        public const string Onset = HandpanTips.EventStrike, Mute = HandpanTips.EventMute;
        /// <summary>The single source, rig-local at the seat (the shell's centre, 6 cm over the rim plane); the module re-parents
        /// it to the instrument so it follows the shell on display too.</summary>
        public static readonly Vector3 SourceLocal = new Vector3(0f, 0.793f, 0.028f);

        public static readonly FootBox[] SeatBoxes =
        {
            new FootBox(0f, 0f, 0.272f, 0.240f, 0.62f, 0.88f),         // the shell and its band, tilted toward the player
            new FootBox(0f, 0.1875f, 0.315f, 0.1275f, 0f, 0.62f),      // the cradle tripod's legs, beyond the knees
        };
        /// <summary>On the wall bracket (heights above the floor with the rig at Stow.Pose; rig-local z toward the wall): the shell
        /// hanging upright, z -0.12..0.12 (room x -2.94..-3.18). The bracket's wall plate (z 0.12..0.135) lies flat on the wall
        /// inside the planner's 2 cm wall inset (SeatPlanner.RoomInset), so the box stops at the inset.</summary>
        public static readonly FootBox[] DisplayBoxes = { new FootBox(0f, 0f, 0.275f, 0.12f, 1.25f, 1.86f) };

        public InstrumentDescriptor Describe() => new InstrumentDescriptor
        {
            Kind = InstrumentKind.Handpan, Rig = RigId.Handpan, Id = "handpan", Name = "Handpan", AloneName = "Handpan alone", Family = "Metal",
            Carousel = 4, ModuleOrder = HandpanModule.ModuleOrder, TickOrder = HandpanModule.InstrumentTickOrder,
            // not FastMotion: review item 10 (lab evidence for off; a device check FMM off / on decides)
            Traits = InstrumentTraits.Playable | InstrumentTraits.Pitched | InstrumentTraits.HasStand,
            Tools = ToolMask.None,
            Placeholder = false,
            Seat = new SeatSpec
            {
                CanonicalSlot = SeatSlot.Front, Prefs = new[] { SeatSlot.FrontLeft, SeatSlot.FrontRight, SeatSlot.Left, SeatSlot.Right, SeatSlot.Front }, Priority = 6,
                Home = new Vector3(0f, 0f, (float)HandpanSpec.HomeZ), HomeYaw = 0f, Single = new Vector3(0f, 0f, (float)HandpanSpec.HomeZ), SingleYaw = 0f,
                ReachMax = 0.62f, PlayCentre = new Vector2(0f, 0f),
                Boxes = SeatBoxes,
                LiftMin = 0f, LiftMax = 0.40f,   // raised on a riser (standing) by Move instruments; never into the floor
            },
            Stow = new StowSpec { SpotId = "left-wall-bracket", Mount = StowMount.Wall, Pose = new Vector3(-3.06f, 0f, -0.62f), Yaw = -90f, Boxes = DisplayBoxes, Where = "On the left wall" },
            Portrait = new PortraitSpec
            {
                // the menu renders the instrument where it is, usually on its bracket (the top facing rig -z): a view from the
                // front, a little left and above, sees the hanging shell's notes about 25 deg off its axis and the stand's
                // tilted shell about 40 deg off (the phase-B portrait shot)
                From = new Vector3(-0.22f, 1.05f, -0.95f), At = new Vector3(0f, 0.66f, 0.03f), Fov = 36f,
                // the fallback icon: the shell seen from above-front, its ding and four near notes
                Fallback = new[]
                {
                    new IconStroke(new[] { new Vector2(10, 50), new Vector2(18, 34), new Vector2(34, 24), new Vector2(50, 21), new Vector2(66, 24), new Vector2(82, 34), new Vector2(90, 50), new Vector2(82, 64), new Vector2(66, 72), new Vector2(50, 75), new Vector2(34, 72), new Vector2(18, 64) }, closed: true),
                    new IconStroke(new[] { new Vector2(42, 44), new Vector2(50, 40), new Vector2(58, 44), new Vector2(58, 52), new Vector2(50, 56), new Vector2(42, 52) }, closed: true, fill: true),
                    new IconStroke(new[] { new Vector2(26, 52), new Vector2(32, 49), new Vector2(38, 52), new Vector2(32, 56) }, closed: true),
                    new IconStroke(new[] { new Vector2(62, 52), new Vector2(68, 49), new Vector2(74, 52), new Vector2(68, 56) }, closed: true),
                    new IconStroke(new[] { new Vector2(36, 66), new Vector2(42, 63), new Vector2(48, 66), new Vector2(42, 69) }, closed: true),
                    new IconStroke(new[] { new Vector2(52, 66), new Vector2(58, 63), new Vector2(64, 66), new Vector2(58, 69) }, closed: true),
                },
            },
            Audio = new AudioSpec
            {
                Unit = 5, CapAvgMs = 0.15f, CapWorstMs = 0.25f, ArenaMiB = 0.18f, BakedMiB = 0f,
                Sources = new[] { new AudioSourceSpec(18 /* SourceId.Handpan */, SourceLocal) },
            },
            Lessons = new LessonSpec { Travel = (float)HandpanSpec.LessonTravel, LeadSeconds = (float)(HandpanSpec.LessonTravel / HandpanSpec.LessonSpeed), Shape = LessonBlockShape.Disc, Notation = NotationKind.Treble, Pack = "handpan", CountNoun = "notes" },
            Mr = new MrSpec { OnTable = false, SurfaceLift = 0f },
            Shadow = new ShadowSpec { Local = new Vector2(0f, 0.06f), W = 0.68f, D = 0.62f, Opacity = 0.45f },
            // A struck field rings until something touches it: the onset is NOT held, so the looper never mutes it at a
            // loop's end; handpanMute (a finger or palm resting, strength 0 = lifted) is a Release keyed by the field.
            Events = new[] { new EventTypeSpec(Onset, EventRole.Onset, EventKey.Index), new EventTypeSpec(Mute, EventRole.Release, EventKey.Index) },
            Tips = HandpanTips.All(),
            Features = HandpanTips.Features(),
            Anchors = HandpanTips.Anchors(),
            DrawsAtSeat = 3, DrawsStowed = 2, TrisAtSeat = 16000, TrisStowed = 3600,
        };
    }
}
