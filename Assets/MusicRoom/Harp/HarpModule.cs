// Harp module (Order 32, ARCHITECTURE.md section 3.4): builds the 22-string lever harp (spec 05 section 5)
// under World and registers it as IInstrument (Kind Harp, TickOrder 20), IHarpApi and Rig (RigId.Harp). The audio
// engine (Order 10) exists at Init; the recorder (Lessons, Order 40) is resolved lazily on the first play.
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Harp
{
    public sealed class HarpModule : AppModule
    {
        public const int ModuleOrder = 32;
        public override int Order => ModuleOrder;

        public HarpInstrument Harp { get; private set; }
        /// <summary>The platform adapters (HarpPlatform).</summary>
        public HarpPlatform Platform { get; private set; }

        public override void Init(App app)
        {
            var go = new GameObject("Harp");
            go.transform.SetParent(app.WorldRoot, false);
            Harp = go.AddComponent<HarpInstrument>();
            Harp.Setup(app);
            Platform = HarpPlatform.Register(app, Harp);   // IStowable, IInstrumentPlayback, IGuideAnchors (platform.md 3.3)
            // "Move instruments" and MR placing stop Core ticking the harp: a string a fingertip holds goes back first.
            _app = app;
            _onArrange = e => { if (e.Active) Harp?.Play?.LeaveAll(); };
            _onPlacing = e => { if (e.Active) Harp?.Play?.LeaveAll(); };
            app.Events.Subscribe(_onArrange);
            app.Events.Subscribe(_onPlacing);
            var audio = app.Get<IAudioEngine>();
            Debug.Log($"[Harp] ready: {Harp.StringCount} strings C3..C6, {Harp.FrameRenderers.Count} frame parts, single-mode x {Harp.Rig.HomeSingle.x:F4}, audio={(audio != null ? "on" : "off")}, headless={app.Headless}");
        }

        App _app;
        System.Action<ArrangeModeEvent> _onArrange;
        System.Action<PlacingEvent> _onPlacing;

        public override void Shutdown()
        {
            if (_app != null)
            {
                if (_onArrange != null) _app.Events.Unsubscribe(_onArrange);
                if (_onPlacing != null) _app.Events.Unsubscribe(_onPlacing);
                _app = null;
            }
            if (Harp != null) Harp.Teardown();
            Harp = null;
        }
    }
}
