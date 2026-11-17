// The harp's registry descriptor (12-new-instruments/platform.md 3.5). Footprint: Tools/SoundLab/platform/registry.mjs (the base
// and feet, then the pillar, soundboard, neck and string plane); played at the near half (reach 0.95 to the play centre).
// Measured against Rig.Handles (Tests/PlayMode/Harp/HarpPlatformTests): the neck's crest reaches 1.476 m (registry.mjs
// stopped the frame at 1.32), so the frame box rises to 1.48.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Harp
{
    public sealed class HarpDefinition : IInstrumentDefinition
    {
        static string[] StringAnchors()
        {
            var a = new string[HarpGeometry.StringCount];
            for (int i = 0; i < a.Length; i++) a[i] = HarpPlatform.StringPrefix + i;
            return a;
        }

        public InstrumentDescriptor Describe() => new InstrumentDescriptor
        {
            Kind = InstrumentKind.Harp, Rig = RigId.Harp, Id = "harp", Name = "Harp", AloneName = "Harp alone", Family = "Strings",
            Carousel = 1, ModuleOrder = HarpModule.ModuleOrder, TickOrder = 20,
            Traits = InstrumentTraits.Playable | InstrumentTraits.Pitched,
            Tools = ToolMask.None,
            Seat = new SeatSpec
            {
                CanonicalSlot = SeatSlot.Left, Prefs = new[] { SeatSlot.Left, SeatSlot.Right, SeatSlot.FrontLeft, SeatSlot.FrontRight }, Priority = 4,
                Home = HarpInstrument.HomeAll, HomeYaw = HarpInstrument.HomeYawAll,
                Single = new Vector3(-0.2966f, 0f, HarpInstrument.SingleFront), SingleYaw = HarpInstrument.HomeYawSingle,
                ReachMax = 0.95f, PlayCentre = new Vector2(-0.33f, 0f),
                LiftMin = 0f, LiftMax = 0.30f,   // a floor harp: raised, it stands on a riser
                Boxes = new[]
                {
                    new FootBox(-0.33f, 0f, 0.48f, 0.11f, 0f, 0.08f),      // base + feet
                    new FootBox(-0.33f, 0f, 0.45f, 0.06f, 0.08f, 1.48f),   // pillar, soundboard, neck (crest 1.476), strings
                },
            },
            Stow = new StowSpec { SpotId = "front-left-corner", Mount = StowMount.Floor, Pose = new Vector3(-3.06f, 0f, 0.26f), Yaw = 90f, Where = "Corner" },
            Portrait = new PortraitSpec
            {
                From = new Vector3(-0.33f, 0.85f, -1.6f), At = new Vector3(-0.33f, 0.68f, 0f), Fov = 46f,
                Fallback = new[]
                {
                    new IconStroke(new[] { new Vector2(30, 88), new Vector2(30, 14), new Vector2(76, 30), new Vector2(70, 88) }, closed: true),
                    new IconStroke(new[] { new Vector2(40, 18), new Vector2(40, 88) }), new IconStroke(new[] { new Vector2(50, 22), new Vector2(50, 88) }),
                    new IconStroke(new[] { new Vector2(60, 26), new Vector2(60, 88) }),
                },
            },
            Audio = new AudioSpec
            {
                Unit = 2, CapAvgMs = 0.15f, CapWorstMs = 0.15f, ArenaMiB = 0.3f,
                Sources = new[] { new AudioSourceSpec(3, HarpGeometry.SourceLocal) },
            },
            Lessons = new LessonSpec { Travel = 0.30f, LeadSeconds = 1.6666667f, Shape = LessonBlockShape.Capsule, Notation = NotationKind.Treble, Pack = null, CountNoun = "notes" },
            Mr = new MrSpec { OnTable = false, SurfaceLift = 0f },
            Shadow = new ShadowSpec { Local = new Vector2(-0.2725f, -0.168f), W = 1.1f, D = 0.7f, Opacity = 0.45f },
            Events = new[]
            {
                new EventTypeSpec(TakeEventType.Harp, EventRole.Onset, EventKey.Index),
                new EventTypeSpec(TakeEventType.HarpDamp, EventRole.Release, EventKey.Index),
            },
            Anchors = StringAnchors(),   // IGuideAnchors (HarpPlatform): "string.<i>" string i's play point
            DrawsAtSeat = 5, DrawsStowed = 3,
        };
    }
}
