// The two seams between Sunny's logic and the app: EngineVoice speaks through IAudioEngine (the robotised lines and the
// chirps on the Guide source), AppWorld reads the menu, layout and drums contracts and the head. Contracts registered by
// later modules (Menu 60, Layout 100) are resolved lazily and cached once found. No instrument is named here: the single
// layout's current instrument is whatever the layout says (the tour's swipe step compares two of them), and the nod step
// asks the kit's API (a Core contract) when one is registered.
using MusicRoom.Audio;
using MusicRoom.Audio.Synths;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Guide
{
    public sealed class EngineVoice : ISunnyVoice
    {
        readonly IAudioEngine _engine;

        public EngineVoice(IAudioEngine engine) { _engine = engine; }

        public bool Available => _engine != null && _engine.IsReady;
        public double Now => _engine != null ? _engine.Now : 0.0;
        public bool HasLine(string id) => _engine != null && _engine.HasVoiceLine(id);
        /// <summary>IAudioEngine.VoiceLineReady: the robotised buffer is rendered.</summary>
        public bool LineReady(string id) => _engine != null && _engine.VoiceLineReady(id);

        public float LineSeconds(string id) => _engine != null ? _engine.VoiceLineSeconds(id) : -1f;
        public double Play(string id, double when) => _engine != null ? _engine.PlayVoiceLine(id, when) : 0.0;
        public void Stop() => _engine?.StopVoiceLine();
        public void Chirp(ChirpKind kind) => _engine?.Chirp(kind);
        public float Level => _engine != null ? _engine.VoiceLevel : 0f;
    }

    public sealed class AppWorld : ISunnyWorld
    {
        readonly App _app;
        readonly GuideModule _module;
        IMenuPanel _menu;
        ILayoutService _layout;
        IDrumsApi _drums;

        public AppWorld(App app, GuideModule module)
        {
            _app = app;
            _module = module;
        }

        public IMenuPanel Menu => _menu ?? (_menu = _app.Get<IMenuPanel>());
        public ILayoutService Layout => _layout ?? (_layout = _app.Get<ILayoutService>());
        public IDrumsApi Drums => _drums ?? (_drums = _app.Get<IDrumsApi>());

        // Not presenting: App.IsPresenting, not which hand source is active (ARCHITECTURE.md 3.3, 15.8).
        public bool IsDesktop => !_module.IsPresenting;

        public bool TryHead(out Vector3 position, out Vector3 forward)
        {
            Transform head = _app.Head;
            if (head == null)
            {
                position = Vector3.zero;
                forward = Vector3.forward;
                return false;
            }
            position = head.position;
            forward = head.forward;
            return true;
        }

        public bool HasPanel => Menu != null;
        public bool PanelShown => Menu != null && Menu.Shown;
        public MenuScreen PanelScreen => Menu != null ? Menu.Screen : MenuScreen.Home;

        public bool TrySingleLayout(out InstrumentKind current)
        {
            ILayoutService layout = Layout;
            if (layout != null && layout.Mode == LayoutMode.Single)
            {
                current = layout.Current;
                return true;
            }
            current = default;
            return false;
        }

        public bool HandsFarFromPad()
        {
            IDrumsApi drums = Drums;
            return drums == null || drums.EveryHandPointerFartherThan(GuideTiming.PadFar, drums.KickPadId);
        }
    }
}
