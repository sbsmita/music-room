// The marimba's registry descriptor (12-new-instruments/marimba.md 4.1; BUILD-GUIDE 4.1). Id block: Kind / Rig / Unit 7,
// Carousel 5, Order 35, TickOrder 23. The footprint is the BUILT model's (phase A folded the low resonators away from the
// knees and raised the tray; phase B raises the bar tops to 0.72 m, MarimbaGeometry.HSeat): every box below 0.62 m stays
// clear of the seat planner's knee box, the back stays inside the front wall, and MarimbaFootprintTests checks the boxes
// against the model's handles and the planner.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Marimba
{
    public sealed class MarimbaDefinition : IInstrumentDefinition
    {
        public const string Onset = MarimbaEvents.Strike;
        /// <summary>The seat home: the natural row's centre line 0.47 m ahead of the eyes (spec 0.50; 3 cm nearer so the back
        /// of the frame clears the front wall at the default seat).</summary>
        public const float HomeZ = 0.47f;

        /// <summary>The footprint (rig-local; heights above the floor, the rig on the floor at Home).</summary>
        public static FootBox[] SeatBoxes() => new[]
        {
            new FootBox(-0.105f, 0.18f, 0.525f, 0.445f, 0.62f, 0.76f), // top, x -0.63 .. 0.42: bars, rails, tray, mallets, bass board (z -0.265 .. 0.625)
            new FootBox(0.525f, 0.145f, 0.105f, 0.275f, 0.62f, 0.76f), // top, the treble end x 0.42 .. 0.63: short bars, treble board (z -0.13 .. 0.42)
            new FootBox(0f, 0.36f, 0.21f, 0.26f, 0f, 0.62f),          // under the middle: the folded tubes' runs and drops, stretcher (z 0.10 ..)
            new FootBox(-0.42f, 0.30f, 0.21f, 0.32f, 0f, 0.62f),      // under the bass half: tubes (z -0.02 ..)
            new FootBox(0.42f, 0.30f, 0.21f, 0.32f, 0f, 0.62f),       // under the treble half: the straight cosmetic tubes
            new FootBox(-0.605f, 0.215f, 0.035f, 0.41f, 0f, 0.62f),   // the bass end board and its legs (z -0.195 .. 0.625)
            new FootBox(0.605f, 0.15f, 0.035f, 0.25f, 0f, 0.62f),     // the treble end board and its legs (z -0.10 .. 0.40)
            // the mallets' and hands' field (air: the player may be in it, no other instrument's part may). Without it the
            // kit at Right stands its hi-hat cymbal 7 cm under the right hand's grip for the top octave
            new FootBox(0f, 0.25f, 0.64f, 0.37f, 0.76f, 1.00f, air: true),   // over the bars: the heads' lift (z -0.12 .. 0.62)
            new FootBox(0f, -0.24f, 0.50f, 0.12f, 0.76f, 1.05f, air: true),  // the grips in front (C3 / C6 grips at x -+0.37, z -0.23)
        };

        public InstrumentDescriptor Describe() => new InstrumentDescriptor
        {
            Kind = InstrumentKind.Marimba, Rig = RigId.Marimba, Id = "marimba", Name = "Marimba", AloneName = "Marimba alone", Family = "Mallets",
            Carousel = 5, ModuleOrder = MarimbaModule.ModuleOrder, TickOrder = 23,
            Traits = InstrumentTraits.Playable | InstrumentTraits.Pitched | InstrumentTraits.UsesTools | InstrumentTraits.FastMotion | InstrumentTraits.HasStand,
            Tools = ToolMask.Mallet,
            Placeholder = false,
            Seat = new SeatSpec
            {
                CanonicalSlot = SeatSlot.Front, Prefs = new[] { SeatSlot.Front, SeatSlot.Left, SeatSlot.Right }, Priority = 2,
                Home = new Vector3(0f, 0f, HomeZ), HomeYaw = 0f, Single = new Vector3(0f, 0f, HomeZ), SingleYaw = 0f,
                ReachMax = 0.80f, PlayCentre = new Vector2(0f, 0.05f),
                Boxes = SeatBoxes(),
                LiftMin = 0f, LiftMax = 0.30f,   // the legs do not telescope: raised, the frame stands on a riser (bar tops 0.72-1.02 m)
            },
            Stow = new StowSpec { SpotId = "window-near", Mount = StowMount.Floor, Pose = new Vector3(2.30f, 0f, -0.80f), Yaw = 90f, Where = "By the window" },
            Portrait = new PortraitSpec
            {
                From = new Vector3(-0.85f, 1.30f, -1.05f), At = new Vector3(0.02f, 0.58f, 0.18f), Fov = 36f,
                Fallback = new[]
                {
                    // seven bars getting shorter to the right over a frame rail
                    Bar(14, 22, 78), Bar(26, 25, 75), Bar(38, 28, 72), Bar(50, 31, 69), Bar(62, 34, 66), Bar(74, 37, 63), Bar(86, 40, 60),
                    new IconStroke(new[] { new Vector2(8, 84), new Vector2(92, 84) }),
                    new IconStroke(new[] { new Vector2(14, 84), new Vector2(14, 96) }), new IconStroke(new[] { new Vector2(86, 84), new Vector2(86, 96) }),
                },
            },
            Audio = new AudioSpec
            {
                Unit = (byte)InstrumentKind.Marimba, CapAvgMs = 0.20f, CapWorstMs = 0.30f, ArenaMiB = 0.16f, BakedMiB = 0f,
                Sources = new[]
                {
                    new AudioSourceSpec(21 /* SourceId.MarimbaLo */, new Vector3(-0.37f, 0.69f, 0.10f), sendDb: -2f),
                    new AudioSourceSpec(22 /* SourceId.MarimbaMid */, new Vector3(0f, 0.69f, 0.10f), sendDb: 0f),
                    new AudioSourceSpec(23 /* SourceId.MarimbaHi */, new Vector3(0.38f, 0.69f, 0.10f), sendDb: -2f),
                },
            },
            Lessons = new LessonSpec { Travel = 0.30f, LeadSeconds = 1.6666667f, Shape = LessonBlockShape.Bar, Notation = NotationKind.Grand, Pack = "marimba", CountNoun = "notes" },
            Mr = new MrSpec { OnTable = false, SurfaceLift = 0f },
            Shadow = new ShadowSpec { Local = new Vector2(0f, 0.18f), W = 1.30f, D = 0.95f, Opacity = 0.45f },
            Events = new[]
            {
                // a struck bar is not Held: it rings through a loop boundary; a mute is a Release keyed by midi
                new EventTypeSpec(MarimbaEvents.Strike, EventRole.Onset, EventKey.Midi),
                new EventTypeSpec(MarimbaEvents.Damp, EventRole.Release, EventKey.Midi),
                new EventTypeSpec(MarimbaEvents.Undamp, EventRole.State, EventKey.Midi),
            },
            Tips = MarimbaTips.Specs(),
            Features = Feature(MarimbaTips.Features),
            Anchors = MarimbaTips.Anchors,
            DrawsAtSeat = 5, DrawsStowed = 3, TrisAtSeat = 18200, TrisStowed = 3900,
        };

        static IconStroke Bar(float x, float top, float bottom) =>
            new IconStroke(new[] { new Vector2(x - 4, top), new Vector2(x + 4, top), new Vector2(x + 4, bottom), new Vector2(x - 4, bottom) }, closed: true, fill: true);

        static FeatureSpec[] Feature(string[] ids)
        {
            var f = new FeatureSpec[ids.Length];
            for (int i = 0; i < ids.Length; i++) f[i] = ids[i];
            return f;
        }
    }
}
