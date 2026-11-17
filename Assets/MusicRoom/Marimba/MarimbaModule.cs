// The marimba module (12-new-instruments/marimba.md; BUILD-GUIDE 4.2): Order 35. Builds the instrument (MarimbaInstrument:
// the model, the controller, every platform interface) under app.WorldRoot at the descriptor's seat home, registers it,
// parents the three sources, and runs the mallets in the frame phases the drumsticks use: their poses follow the hands
// before the pointers are read (StickPoses), and a hand that opened lets its mallet go after the grab pass (StickReleases).
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Marimba
{
    public sealed class MarimbaModule : AppModule
    {
        public const int ModuleOrder = 35;
        public override int Order => ModuleOrder;
        public MarimbaInstrument Instrument { get; private set; }

        public override void Init(App app)
        {
            InstrumentDescriptor d = app.Registry.Get(InstrumentKind.Marimba) ?? new MarimbaDefinition().Describe();
            Instrument = MarimbaInstrument.Create(app, d);
            IAudioEngine audio = app.Get<IAudioEngine>();
            if (audio != null)
                foreach (var s in d.Audio.Sources) audio.SetSourceParent((SourceId)s.Source, Instrument.transform, s.Local);
            Debug.Log($"[Marimba] ready: {MarimbaGeometry.Bars} bars, {Instrument.View.Handles.Count} handles, " +
                      $"{Instrument.View.DrawCalls} draws, {d.Seat.Boxes.Length} boxes, audio={(audio != null ? "on" : "off")}");
        }

        public override void Tick(FramePhase phase, float dt)
        {
            if (Instrument == null) return;
            switch (phase)
            {
                case FramePhase.StickPoses: Instrument.UpdateMalletPoses(dt); break;
                case FramePhase.StickReleases: Instrument.UpdateMalletReleases(dt); break;
                case FramePhase.EndFrame: Instrument.EndFrame(dt); break;
            }
        }

        public override void Shutdown()
        {
            if (Instrument != null) Instrument.Shutdown();
        }
    }
}
