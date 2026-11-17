// The singing bowls' module (bowls.md 3; BUILD-GUIDE 4.2): Order 37. Builds the instrument (BowlsInstrument: the model,
// the hand logic, the stick, every platform interface) under app.WorldRoot at the descriptor's seat home, registers it,
// parents the one source (SourceId.Bowls), and runs the stick in the frame phases the drumsticks use: its pose follows
// the hand before the pointers are read (StickPoses); the drawn stick and the let-go of contacts on a frame the bowls
// were not ticked (a glide, arrange mode) at EndFrame.
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Bowls
{
    public sealed class BowlsModule : AppModule
    {
        public const int ModuleOrder = 37;
        public override int Order => ModuleOrder;
        public BowlsInstrument Instrument { get; private set; }

        public override void Init(App app)
        {
            InstrumentDescriptor d = app.Registry.Get(InstrumentKind.Bowls) ?? new BowlsDefinition().Describe();
            Instrument = BowlsInstrument.Create(app, d);
            IAudioEngine audio = app.Get<IAudioEngine>();
            if (audio != null)
                foreach (var s in d.Audio.Sources) audio.SetSourceParent((SourceId)s.Source, Instrument.transform, s.Local);
            Debug.Log($"[Bowls] ready: 3 bowls, {Instrument.Rig.Handles.Count} handle, {d.Seat.Boxes.Length} boxes, " +
                      $"{Instrument.View.Triangles(false)} / {Instrument.View.Triangles(true)} tris, audio={(audio != null ? "on" : "off")}");
        }

        public override void Tick(FramePhase phase, float dt)
        {
            if (Instrument == null) return;
            switch (phase)
            {
                case FramePhase.StickPoses: Instrument.UpdateStickPoses(dt); break;
                case FramePhase.EndFrame: Instrument.EndFrame(dt); break;
            }
        }

        public override void Shutdown()
        {
            if (Instrument != null) Instrument.Shutdown();
        }
    }
}
