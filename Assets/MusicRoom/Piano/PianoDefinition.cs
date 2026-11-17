// The piano's registry descriptor (12-new-instruments/platform.md 3.5). Footprint: Tools/SoundLab/platform/registry.mjs (keys +
// case + rest, both stand sides), fitted to Docs/05-instruments.md; heights above the floor with the keys at 0.72 m.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Piano
{
    public sealed class PianoDefinition : IInstrumentDefinition
    {
        public InstrumentDescriptor Describe() => new InstrumentDescriptor
        {
            Kind = InstrumentKind.Piano, Rig = RigId.Piano, Id = "piano", Name = "Piano", AloneName = "Piano alone", Family = "Keys",
            Carousel = 0, ModuleOrder = PianoModule.ModuleOrder, TickOrder = 0,
            Traits = InstrumentTraits.Playable | InstrumentTraits.Pitched | InstrumentTraits.HasStand | InstrumentTraits.TableTop,
            Tools = ToolMask.None,
            Seat = new SeatSpec
            {
                CanonicalSlot = SeatSlot.Front, Prefs = new[] { SeatSlot.Front, SeatSlot.Left, SeatSlot.Right }, Priority = 0,
                Home = PianoInstrument.Home, HomeYaw = 0f, Single = PianoInstrument.Home, SingleYaw = 0f,
                ReachMax = 0.62f, PlayCentre = new Vector2(0f, 0.05f),
                Boxes = new[]
                {
                    new FootBox(0f, 0.15f, 0.66f, 0.17f, 0.62f, 0.95f),     // keys + case + rest
                    new FootBox(-0.58f, 0.15f, 0.04f, 0.15f, 0f, 0.62f),     // stand sides
                    new FootBox(0.58f, 0.15f, 0.04f, 0.15f, 0f, 0.62f),
                },
                LiftMin = ArrangeConstants.PianoKeyMin - 0.72f, LiftMax = ArrangeConstants.PianoKeyMax - 0.72f,
                StandStretches = true,   // PianoInstrument.FitStand
            },
            Stow = new StowSpec { SpotId = "front-wall-right", Mount = StowMount.Floor, Pose = new Vector3(1.78f, 0.72f, 0.77f), Yaw = 0f, Where = "Front wall" },
            Portrait = new PortraitSpec
            {
                From = new Vector3(-0.55f, 0.42f, -1.15f), At = new Vector3(0f, -0.12f, 0.12f), Fov = 36f,
                Fallback = new[]
                {
                    new IconStroke(new[] { new Vector2(8, 38), new Vector2(92, 38), new Vector2(92, 62), new Vector2(8, 62) }, closed: true),
                    new IconStroke(new[] { new Vector2(26, 38), new Vector2(26, 52) }), new IconStroke(new[] { new Vector2(40, 38), new Vector2(40, 52) }),
                    new IconStroke(new[] { new Vector2(60, 38), new Vector2(60, 52) }), new IconStroke(new[] { new Vector2(74, 38), new Vector2(74, 52) }),
                },
            },
            Audio = new AudioSpec
            {
                Unit = 0, CapAvgMs = 0.6f, CapWorstMs = 1.0f, ArenaMiB = 0.6f,
                Sources = new[]
                {
                    new AudioSourceSpec(0, PianoInstrument.SourceOffsets[0]), new AudioSourceSpec(1, PianoInstrument.SourceOffsets[1]),
                    new AudioSourceSpec(2, PianoInstrument.SourceOffsets[2]),
                },
            },
            Lessons = new LessonSpec { Travel = 0.35f, LeadSeconds = 1.9444444f, Shape = LessonBlockShape.Box, Notation = NotationKind.Grand, Pack = null, CountNoun = "notes" },
            Mr = new MrSpec { OnTable = true, SurfaceLift = 0.092f },
            Shadow = new ShadowSpec { Local = new Vector2(0f, 0.2f), W = 2f, D = 1f, Opacity = 0.55f, HiddenInMr = true },
            Events = new[]
            {
                new EventTypeSpec(TakeEventType.PianoOn, EventRole.Onset, EventKey.Midi, held: true),
                new EventTypeSpec(TakeEventType.PianoOff, EventRole.Release, EventKey.Midi),
            },
            // IGuideAnchors (PianoPlatform): "key.<midi>" (21..108) the key's top centre, "keys.middle" the keyboard's.
            Anchors = new[] { PianoPlatform.KeysMiddle, PianoPlatform.KeyPrefix + PianoSpec.MiddleC },
            DrawsAtSeat = 10, DrawsStowed = 4,
            // The music rest's front face, bottom centre, piano-local, up along the rest (tilted 0.3 rad back): measured from
            // PianoBody.MusicRest (0, .0397, .2376); sheet.md's (0, .039, .2375) left out the board's half thickness.
            SheetAnchor = PianoMusicRest.Local,
            HasSheetAnchor = true,
        };
    }
}
