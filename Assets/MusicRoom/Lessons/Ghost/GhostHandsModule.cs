// The ghost hands' module (Lessons' Order: the recorder is the Lessons module's; it is looked up on the first frame).
// Builds GhostHands in Init and ticks it in EndFrame, after the layout's animation moved the instruments this frame.
using MusicRoom.Core;

namespace MusicRoom.Lessons.Ghost
{
    public sealed class GhostHandsModule : AppModule
    {
        public override int Order => LessonsModule.ModuleOrder;
        public GhostHands Hands { get; private set; }
        App _app;

        public override void Init(App app)
        {
            _app = app;
            Hands = new GhostHands(app);
        }

        public override void Tick(FramePhase phase, float dt)
        {
            if (phase == FramePhase.EndFrame) Hands?.Tick(dt);
        }

        public override void Shutdown()
        {
            Hands?.Destroy();
            Hands = null;
        }

        /// <summary>The booted app's ghost hands (tests, perf).</summary>
        public static GhostHands Of(App app)
        {
            if (app == null) return null;
            foreach (IAppModule m in app.Modules) if (m is GhostHandsModule g) return g.Hands;
            return null;
        }
    }
}
