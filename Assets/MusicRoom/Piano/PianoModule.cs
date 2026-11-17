// Piano module (Order 30): builds the slim digital piano under App.WorldRoot and registers its Rig (RigId.Piano, priority
// 0), IInstrument (Kind Piano, TickOrder 0) and IPianoApi, and its platform adapters (PianoPlatform). Everything per frame happens in PianoInstrument.Tick / Hover,
// which Core calls in the Instruments phase (ARCHITECTURE.md section 4 step 5), so the module itself has no phase work.
// Spec: Docs/05-instruments.md section 2 (the piano and curves data); ARCHITECTURE.md 15.1.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Piano
{
    public sealed class PianoModule : AppModule
    {
        public const int ModuleOrder = 30;
        public override int Order => ModuleOrder;
        public PianoInstrument Piano { get; private set; }

        public override void Init(App app)
        {
            var go = new GameObject("Piano");
            go.transform.SetParent(app.WorldRoot, false);
            go.transform.localPosition = PianoInstrument.Home;   // until Layout arranges the room around the seat
            float t0 = Time.realtimeSinceStartup;
            Piano = go.AddComponent<PianoInstrument>();
            Piano.Setup(app);
            PianoPlatform.Register(app, Piano);   // IStowable, IInstrumentPlayback, IGuideAnchors, IInstrumentSurface (platform.md 3.3)
            // "Move instruments" and MR placing stop Core ticking the piano: the fingers that hold keys let them go first.
            _app = app;
            _onArrange = e => { if (e.Active) ReleaseFingers(); };
            _onPlacing = e => { if (e.Active) ReleaseFingers(); };
            app.Events.Subscribe(_onArrange);
            app.Events.Subscribe(_onPlacing);
            Debug.Log($"[Piano] ready: 88 keys, {PianoLabelAtlas.Cells.Count} label cells (glyphs={(Piano.LabelGlyphs ? "yes" : "no")}), " +
                      $"audio={(app.Get<MusicRoom.Audio.IAudioEngine>() != null ? "on" : "off")}, built in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms");
        }

        App _app;
        System.Action<ArrangeModeEvent> _onArrange;
        System.Action<PlacingEvent> _onPlacing;

        void ReleaseFingers()
        {
            if (Piano == null || Piano.Action == null) return;
            Piano.Action.ReleaseFingers();
            Piano.View?.Sync();
        }

        // The piano subscribes to no static event and leaves no delegate on objects that outlive the App (its desktop
        // click target is a PlaySurface collider, not a DesktopRigFallback.dragBlocked hook); only its two App events go.
        public override void Shutdown()
        {
            if (_app == null) return;
            if (_onArrange != null) _app.Events.Unsubscribe(_onArrange);
            if (_onPlacing != null) _app.Events.Unsubscribe(_onPlacing);
            _app = null;
        }
    }
}
