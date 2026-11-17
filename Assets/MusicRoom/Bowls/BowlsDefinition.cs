// The singing bowls' registry descriptor (bowls.md 3.2; BUILD-GUIDE 4.1). The id block is the platform's (Kind / Rig /
// Unit 9, SourceId.Bowls 25, Carousel 3, Order 37, TickOrder 24). The footprint is the canonical FrontLeft pocket:
// the stand (0.41 x 0.29 m, top 0.61 m) and the bowls on it (the big rim at 0.715 m). The display spot is the near half
// of the window sill (3.09, -0.62): clear of the ivy at the far end (its leaves reach z -1.58) by 0.7 m and of the near
// curtain by 7 cm, and the ray summon picks it cleanly at +-3 deg (the middle of the sill sits behind the marimba's tubes
// for a ray 3 deg off). On display the stand is hidden and the bowls stand in a row on their cushions with the stick in
// front (sill top 0.63 m).
// Event fields: BowlsEvents. Tips: BowlsTips. Anchors: BowlsAnchors.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Bowls
{
    public sealed class BowlsDefinition : IInstrumentDefinition
    {
        public const string Onset = BowlsEvents.Strike;
        /// <summary>The display spot (room x, z, yaw) and the inside sill's top (the room's trim box, top y 0.63).</summary>
        public static readonly Vector3 DisplayPose = new Vector3(3.09f, 0f, -0.62f);
        public const float DisplayYaw = 90f;

        public InstrumentDescriptor Describe() => new InstrumentDescriptor
        {
            Kind = InstrumentKind.Bowls, Rig = RigId.Bowls, Id = "bowls", Name = "Singing bowls", AloneName = "Singing bowls", Family = "Metal",
            Carousel = 3, ModuleOrder = BowlsModule.ModuleOrder, TickOrder = 24,
            Traits = InstrumentTraits.Playable | InstrumentTraits.Pitched | InstrumentTraits.Continuous | InstrumentTraits.UsesTools | InstrumentTraits.HasStand | InstrumentTraits.TableTop,
            Tools = ToolMask.RubStick | ToolMask.Striker,
            Placeholder = false,
            Seat = new SeatSpec
            {
                CanonicalSlot = SeatSlot.FrontLeft, Prefs = new[] { SeatSlot.FrontLeft, SeatSlot.FrontRight, SeatSlot.Left, SeatSlot.Right, SeatSlot.Front }, Priority = 7,
                Home = BowlsLayout.HomeSeat, HomeYaw = BowlsLayout.HomeYawDeg, Single = BowlsLayout.HomeSeat, SingleYaw = BowlsLayout.HomeYawDeg,
                ReachMax = BowlsLayout.ReachMax, PlayCentre = BowlsLayout.PlayCentre,
                Boxes = new[]
                {
                    new FootBox(0f, 0f, BowlsLayout.StandHx, BowlsLayout.StandHz, 0f, BowlsLayout.StandTop),        // the walnut stand
                    new FootBox(0f, 0f, BowlsLayout.StandHx, BowlsLayout.StandHz, BowlsLayout.StandTop, 0.72f),      // cushions, bowls (big rim 0.715), the stick
                },
                LiftMin = 0f, LiftMax = 0.40f,   // raised on a riser (standing) by Move instruments; never into the floor
            },
            Stow = new StowSpec
            {
                SpotId = "window-sill", Mount = StowMount.Surface, Pose = DisplayPose, Yaw = DisplayYaw,
                Boxes = new[] { new FootBox(-0.01f, 0f, 0.255f, 0.09f, BowlsLayout.SillTop, 0.735f) }, Where = "On the sill",
            },
            Portrait = new PortraitSpec
            {
                From = new Vector3(0.42f, 1.05f, -0.62f), At = new Vector3(0f, 0.66f, 0f), Fov = 30f,
                Fallback = new[]
                {
                    new IconStroke(new[] { new Vector2(10, 56), new Vector2(34, 56), new Vector2(30, 70), new Vector2(14, 70) }, closed: true),
                    new IconStroke(new[] { new Vector2(38, 52), new Vector2(64, 52), new Vector2(60, 70), new Vector2(42, 70) }, closed: true),
                    new IconStroke(new[] { new Vector2(68, 58), new Vector2(90, 58), new Vector2(86, 70), new Vector2(72, 70) }, closed: true),
                    new IconStroke(new[] { new Vector2(8, 78), new Vector2(92, 78) }),
                },
            },
            Audio = new AudioSpec
            {
                Unit = 9, CapAvgMs = 0.15f, CapWorstMs = 0.25f, ArenaMiB = 0.02f, BakedMiB = 0f,
                Sources = new[] { new AudioSourceSpec(24 /* SourceId.Bowls */, BowlsLayout.SourceLocal, sendDb: -1f) },
            },
            Lessons = new LessonSpec { Travel = 0.30f, LeadSeconds = 1.6666667f, Shape = LessonBlockShape.Ring, Notation = NotationKind.None, Pack = "bowls", CountNoun = "rings" },
            Mr = new MrSpec { OnTable = true, SurfaceLift = 0f },
            Shadow = new ShadowSpec { Local = new Vector2(0f, 0f), W = 0.46f, D = 0.34f, Opacity = 0.45f, HiddenInMr = true },
            Events = BowlsEvents.Specs,
            Tips = BowlsTips.All,
            Features = BowlsTips.Features,
            Anchors = BowlsTips.Anchors,
            DrawsAtSeat = 5, DrawsStowed = 2, TrisAtSeat = 9700, TrisStowed = 4300,
        };
    }
}
