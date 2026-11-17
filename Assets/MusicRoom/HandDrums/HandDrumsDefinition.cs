// The hand drums' registry descriptor (12-new-instruments/platform.md 3, BUILD-GUIDE 4.1, handdrums.md 7): a conga and a
// tumba on basket stands, played with bare hands. Plain data (Core reads it by reflection before any module's Init).
// Numbers: handdrums.md 4.3 for the seat (the planner's footprint, measured against the model by the conformance suite's
// check 3), phase A's measurements for the audio budget and the arena (HandDrumsUnit.StateBytes 191 KiB), the lab's
// room check R1 for the 1 dB drier room send (review item 13: the impulse DRR at the seat 6.3 / 5.0 dB instead of
// 5.3 / 4.0 dB), the GPU shots for the draws and triangles. The display spot is clear of the room's decor (the ivy on the
// sill, the plants' leaves): HandDrumsDisplayTests measures it against IRoomFurniture and RoomFurniture.Decor.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.HandDrums
{
    public sealed class HandDrumsDefinition : IInstrumentDefinition
    {
        public const string Onset = HandDrumsTake.TypeStroke, Mute = HandDrumsTake.TypeMute;
        /// <summary>Anchors (README): each head's centre, up = the head normal x the vibrating radius (the DemoKind presets read
        /// the radius from |up|: OpenTone lands at 0.7 r, CuppedSlap at 0.8 r, toward the player).</summary>
        public const string AnchorConga = "conga", AnchorTumba = "tumba";
        /// <summary>The room send (dB) and the early-reflection send (dB) of both drums: 1 dB drier than the table's -3 / +3.</summary>
        public const float SendDb = -4f, ErDb = 2f;
        /// <summary>The display spot by the window (room x, z; yaw): 22 cm into the room from phase 0's (2.64, -1.85), which the ivy
        /// hanging from the sill reached 13.5 cm into; here 8.5 cm clear of it, of the furniture and of the other displays
        /// (HandDrumsDisplayTests).</summary>
        public static readonly Vector3 DisplayPose = new Vector3(2.42f, 0f, -1.94f);
        public const float DisplayYaw = 90f;

        public InstrumentDescriptor Describe() => new InstrumentDescriptor
        {
            Kind = InstrumentKind.HandDrums, Rig = RigId.HandDrums, Id = "handdrums", Name = "Hand drums", AloneName = "Hand drums alone", Family = "Percussion",
            Carousel = 6, ModuleOrder = HandDrumsModule.ModuleOrder, TickOrder = 22,
            Traits = InstrumentTraits.Playable | InstrumentTraits.Pitched | InstrumentTraits.FastMotion | InstrumentTraits.HasStand,
            Tools = ToolMask.None,
            Placeholder = false,
            Seat = new SeatSpec
            {
                CanonicalSlot = SeatSlot.Front, Prefs = new[] { SeatSlot.FrontRight, SeatSlot.FrontLeft, SeatSlot.Right, SeatSlot.Left, SeatSlot.Front }, Priority = 5,
                Home = new Vector3(0f, 0f, 0.57f), HomeYaw = 0f, Single = new Vector3(0f, 0f, 0.57f), SingleYaw = 0f,
                ReachMax = 0.62f, PlayCentre = new Vector2(0f, -0.05f),
                Boxes = new[]
                {
                    new FootBox(0.005f, 0f, 0.36f, 0.165f, 0.62f, 0.81f),   // the heads and hoops (they may pass over the knees)
                    new FootBox(0.005f, 0.1025f, 0.36f, 0.2275f, 0f, 0.62f),   // the shells and the basket stands (the back foot at z 0.32)
                },
                LiftMin = 0f, LiftMax = 0.35f,   // raised on a riser (standing) by Move instruments; never into the floor
            },
            Stow = new StowSpec { SpotId = "window-far", Mount = StowMount.Floor, Pose = DisplayPose, Yaw = DisplayYaw, Where = "By the window" },
            Portrait = new PortraitSpec
            {
                From = new Vector3(-0.42f, 1.22f, -0.98f), At = new Vector3(0.01f, 0.52f, 0.04f), Fov = 38f,
                Fallback = new[]
                {
                    // the conga (left, taller and slimmer) and the tumba (right): barrel shells, the heads as ellipses
                    new IconStroke(new[] { new Vector2(15, 24), new Vector2(41, 24), new Vector2(45, 54), new Vector2(39, 90), new Vector2(17, 90), new Vector2(11, 54) }, closed: true),
                    new IconStroke(Ellipse(28, 24, 13, 3.6f), closed: true),
                    new IconStroke(new[] { new Vector2(53, 30), new Vector2(83, 30), new Vector2(88, 58), new Vector2(81, 90), new Vector2(55, 90), new Vector2(48, 58) }, closed: true),
                    new IconStroke(Ellipse(68, 30, 15, 4f), closed: true),
                },
            },
            Audio = new AudioSpec
            {
                Unit = 6, CapAvgMs = 0.15f, CapWorstMs = 0.25f, ArenaMiB = 0.19f, BakedMiB = 0f,
                // in the unit's channel order: channel 0 the tumba (HandDrumLo, 19), channel 1 the conga (HandDrumHi, 20), each at its head
                Sources = new[]
                {
                    new AudioSourceSpec(19 /* HandDrumLo */, new Vector3(0.19f, 0.755f, 0f), SendDb, ErDb),
                    new AudioSourceSpec(20 /* HandDrumHi */, new Vector3(-0.185f, 0.75f, 0f), SendDb, ErDb),
                },
            },
            Lessons = new LessonSpec { Travel = 0.35f, LeadSeconds = 1.9444444f, Shape = LessonBlockShape.Disc, Notation = NotationKind.Percussion, Pack = "handdrums", CountNoun = "strokes" },
            Mr = new MrSpec { OnTable = false, SurfaceLift = 0f },
            Shadow = new ShadowSpec { Local = new Vector2(0.005f, 0.07f), W = 0.86f, D = 0.62f, Opacity = 0.45f },
            Events = new[]
            {
                new EventTypeSpec(Onset, EventRole.Onset, EventKey.Kind),
                // a hand resting on a head (open false) or lifting off it (open true): keyed by the drum, played where it happened
                new EventTypeSpec(Mute, EventRole.State, EventKey.Kind),
            },
            Tips = HandDrumsGuide.TipSpecs(),
            Features = HandDrumsGuide.FeatureSpecs(),
            Anchors = new[] { AnchorConga, AnchorTumba },
            // at the seat: shells, heads, hardware, and the lesson cue's hand print while a lesson runs; on display the three
            DrawsAtSeat = 4, DrawsStowed = 3, TrisAtSeat = 20200, TrisStowed = 8000,
        };

        static Vector2[] Ellipse(float cx, float cy, float rx, float ry)
        {
            var p = new Vector2[16];
            for (int i = 0; i < p.Length; i++) { float a = i * Mathf.PI * 2f / p.Length; p[i] = new Vector2(cx + rx * Mathf.Cos(a), cy + ry * Mathf.Sin(a)); }
            return p;
        }
    }
}
