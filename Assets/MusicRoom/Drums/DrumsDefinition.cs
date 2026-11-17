// The drum kit's registry descriptor (12-new-instruments/platform.md 3.5). Footprint: Tools/SoundLab/platform/registry.mjs
// drumBoxes(): per piece its shell (or cymbal) and its stand pole, the kick on its side, the kick pad and its post; the
// tripod feet are left out (they pass under the bench and beside other feet). Measured against Rig.Handles
// (Tests/PlayMode/Drums/DrumsPlatformTests): the kick's shell runs from its batter head at z .70 back to 1.10 (registry.mjs
// had it centred on the head, .50-.90) and its pedal lies in front of it (.43-.65); both boxes are the measured ones.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Drums
{
    public sealed class DrumsDefinition : IInstrumentDefinition
    {
        static FootBox[] Boxes()
        {
            // drums.pieces (drums-local, head plane at y): radius, shell depth (0 = cymbal), x, y, z
            float[,] p =
            {
                { 0.18f, 0.14f, 0.00f, 0.72f, 0.30f },    // snare
                { 0.15f, 0.20f, -0.04f, 0.86f, 0.625f },  // tom1 (DrumSpec.Pieces: spaced from the snare, 2026-11-14)
                { 0.165f, 0.22f, 0.32f, 0.85f, 0.482f },  // tom2
                { 0.20f, 0.36f, 0.38f, 0.62f, 0.02f },    // floor
                { 0.17f, 0f, -0.42f, 0.80f, 0.36f },      // hat
                { 0.21f, 0f, -0.39f, 1.02f, 0.58f },      // crash
                { 0.22f, 0f, 0.48f, 1.04f, 0.27f },       // ride
            };
            var b = new List<FootBox>(17);
            for (int i = 0; i < p.GetLength(0); i++)
            {
                float r = p[i, 0], depth = p[i, 1], x = p[i, 2], y = p[i, 3], z = p[i, 4];
                if (depth > 0f) b.Add(new FootBox(x, z, r + 0.01f, r + 0.01f, y - depth, y + 0.03f));
                else b.Add(new FootBox(x, z, r, r, y - 0.03f, y + 0.03f));
                b.Add(new FootBox(x, z, 0.05f, 0.05f, 0f, Mathf.Max(0.05f, y - (depth > 0f ? depth : 0.05f))));   // stand pole
            }
            b.Add(new FootBox(0.12f, 0.90f, 0.30f, 0.21f, 0f, 0.59f));       // kick, on its side: shell and hoops, head at z .70 to 1.10
            b.Add(new FootBox(0.12f, 0.54f, 0.05f, 0.11f, 0f, 0.06f));       // its pedal, in front of the head
            b.Add(new FootBox(-0.29f, 0.19f, 0.10f, 0.10f, 0.57f, 0.63f));   // kick pad
            b.Add(new FootBox(-0.29f, 0.19f, 0.03f, 0.03f, 0f, 0.57f));      // its post
            return b.ToArray();
        }

        public InstrumentDescriptor Describe() => new InstrumentDescriptor
        {
            Kind = InstrumentKind.Drums, Rig = RigId.Drums, Id = "drums", Name = "Drums", AloneName = "Drums alone", Family = "Percussion",
            Carousel = 7, ModuleOrder = DrumsModule.ModuleOrder, TickOrder = 10,
            Traits = InstrumentTraits.Playable | InstrumentTraits.UsesTools | InstrumentTraits.FastMotion,
            Tools = ToolMask.Stick,
            Seat = new SeatSpec
            {
                CanonicalSlot = SeatSlot.Right, Prefs = new[] { SeatSlot.Right, SeatSlot.Left }, Priority = 1,
                Home = DrumSpec.HomeAll, HomeYaw = DrumSpec.HomeYawAll, Single = DrumSpec.HomeSingle, SingleYaw = DrumSpec.HomeYawSingle,
                ReachMax = 0.70f, PlayCentre = new Vector2(0f, 0.3f), Boxes = Boxes(),
                LiftMin = ArrangeConstants.DrumsLiftMin, LiftMax = ArrangeConstants.DrumsLiftMax,
            },
            Stow = new StowSpec { SpotId = "left-wall", Mount = StowMount.Floor, Pose = new Vector3(-2.06f, 0f, -0.66f), Yaw = -90f, Where = "Left wall" },
            Portrait = new PortraitSpec
            {
                From = new Vector3(-0.2f, 1.45f, -1.35f), At = new Vector3(0f, 0.62f, 0.3f), Fov = 40f,
                Fallback = new[]
                {
                    new IconStroke(new[] { new Vector2(20, 50), new Vector2(45, 50), new Vector2(45, 62), new Vector2(20, 62) }, closed: true),
                    new IconStroke(new[] { new Vector2(55, 46), new Vector2(82, 46), new Vector2(82, 60), new Vector2(55, 60) }, closed: true),
                    new IconStroke(new[] { new Vector2(12, 32), new Vector2(40, 28) }), new IconStroke(new[] { new Vector2(62, 26), new Vector2(90, 30) }),
                    new IconStroke(new[] { new Vector2(34, 66), new Vector2(66, 66), new Vector2(66, 88), new Vector2(34, 88) }, closed: true),
                },
            },
            Audio = new AudioSpec
            {
                Unit = 1, CapAvgMs = 0.8f, CapWorstMs = 1.0f, ArenaMiB = 1.0f,
                Sources = new[]
                {
                    new AudioSourceSpec(4, Vector3.zero), new AudioSourceSpec(5, Vector3.zero), new AudioSourceSpec(6, Vector3.zero),
                    new AudioSourceSpec(7, Vector3.zero), new AudioSourceSpec(8, Vector3.zero), new AudioSourceSpec(9, Vector3.zero),
                    new AudioSourceSpec(10, Vector3.zero), new AudioSourceSpec(11, Vector3.zero), new AudioSourceSpec(12, Vector3.zero),
                },
            },
            Lessons = new LessonSpec { Travel = 0.35f, LeadSeconds = 1.9444444f, Shape = LessonBlockShape.Disc, Notation = NotationKind.Percussion, Pack = null, CountNoun = "hits" },
            Mr = new MrSpec { OnTable = false, SurfaceLift = 0f },
            Shadow = new ShadowSpec { Local = new Vector2(0.1f, 0.5f), W = 1.9f, D = 1.6f, Opacity = 0.45f },
            Events = new[]
            {
                new EventTypeSpec(TakeEventType.Drum, EventRole.Onset, EventKey.Kind),
                new EventTypeSpec(TakeEventType.DrumChoke, EventRole.Release, EventKey.Kind),
                new EventTypeSpec(TakeEventType.Hat, EventRole.State, EventKey.None, sampled: true),
            },
            // IGuideAnchors (DrumsPlatform): "piece.<kind>" each strike surface's centre, "pad" the kick pad's.
            Anchors = new[]
            {
                DrumsPlatform.Pad, DrumsPlatform.PiecePrefix + "snare", DrumsPlatform.PiecePrefix + "tom1", DrumsPlatform.PiecePrefix + "tom2",
                DrumsPlatform.PiecePrefix + "floor", DrumsPlatform.PiecePrefix + "kick", DrumsPlatform.PiecePrefix + "hat",
                DrumsPlatform.PiecePrefix + "crash", DrumsPlatform.PiecePrefix + "ride",
            },
            DrawsAtSeat = 21, DrawsStowed = 6,
        };
    }
}
