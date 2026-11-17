// The hand drums module (BUILD-GUIDE 4.2, Order 34): builds the conga and the tumba under the world root at the seat home
// and registers them (HandDrumsInstrument: IInstrument, IStowable, IInstrumentPlayback, IGuideAnchors, ILessonTargets,
// IContactSurface, the Rig), parents the two sources to their heads. Everything else (the layout, the menu tile, lessons,
// Sunny's tips, Fast Motion Mode) comes from the descriptor (HandDrumsDefinition).
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.HandDrums
{
    public sealed class HandDrumsModule : AppModule
    {
        public const int ModuleOrder = 34;
        public override int Order => ModuleOrder;
        public HandDrumsInstrument Instrument { get; private set; }

        public override void Init(App app)
        {
            InstrumentDescriptor d = app.Registry.Get(InstrumentKind.HandDrums) ?? new HandDrumsDefinition().Describe();
            Instrument = HandDrumsInstrument.Create(app, d);
            IAudioEngine audio = app.Get<IAudioEngine>();
            if (audio != null)
                foreach (AudioSourceSpec s in d.Audio.Sources) audio.SetSourceParent((SourceId)s.Source, Instrument.transform, s.Local);
            Debug.Log($"[HandDrums] ready: {Instrument.Model.Triangles} triangles, {d.Seat.Boxes.Length} boxes, audio={(audio != null ? "on" : "off")}, palm lift {(Instrument.PalmLiftWired ? "wired" : "not wired (contract request R1)")}");
        }
    }
}
