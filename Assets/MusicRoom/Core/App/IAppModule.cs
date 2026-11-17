// Module contract. Every feature folder (Audio, Piano, Drums, ...) ships one or more classes implementing IAppModule;
// ModuleDiscovery finds them by reflection in every MusicRoom.* assembly, so adding a module never edits shared code.
using System;

namespace MusicRoom.Core
{
    /// <summary>The per-frame phases, in the order App.Frame runs them (spec 05 section 1.1 and spec 08 section 7).
    /// Core itself does the work of the phases marked [core]; modules hook the others.</summary>
    public enum FramePhase
    {
        StickPoses,       // 1  Drums: Sticks.UpdatePoses(dt), BEFORE the pointers are read
        InputDone,        // 2  [core] PointerInput.Update ran: raw pointers are fresh
        Instruments,      // 3  [core] IInstrument.Tick + Hover (piano, drums, harp, table, menu); modules may observe
        Tutorial,         // 4  Lessons: falling blocks, hit detection (reads the recorder stream)
        StickHover,       // 5  Drums: stick pick-up glows
        HoverGlow,        // 6  [core] HoverGlowPool draws what was added this frame
        FirstArrange,     //    Layout: arrange around the player on the first tracked frame
        MixedReality,     //    MR: placing reticle, portals, scene events
        Grab,             // 7  Layout: stick pick-up first (IDrumsApi.TryPickUp), then rig grab guards, dwell, drag, two-hand twist
        StickReleases,    // 8  Drums: open-hand release of a held stick (before the listener update)
        Listener,         //    Audio: listener / source bookkeeping
        NodKick,          // 9  Drums: head nod -> kick
        Recorder,         //    Lessons: playback dispatch
        Decor,            //    Room: flicker, snow weather, smoke, plants
        Guide,            //    Guide: Sunny
        Swivel,           //    Layout: seat swivel pinch
        TwoHandSummon,    //    Layout: two-hand re-arrange
        PageTurn,         //    Layout: sweep gesture
        LayoutAnimation,  //    Layout: page-turn animation, settle
        EndFrame,         // 11 [core] PointerInput.EndFrame() ran (edge flags cleared); modules clear their own
    }

    public interface IAppModule
    {
        /// <summary>Init order (ascending). Services a module needs from others must come from a lower order:
        /// Core 0, Audio 10, Room 20, Piano 30, Drums 31, Harp 32, Lessons 40, Guide 50, Menu 60, MR 70, Layout 100.
        /// A feature's extra IAppModule classes use its main module's Order (ties sort by full type name).</summary>
        int Order { get; }
        /// <summary>Build GameObjects under app.WorldRoot, register services with app.Register&lt;T&gt;.</summary>
        void Init(App app);
        /// <summary>Called once per phase per frame, in FramePhase order. Keep it allocation-free.</summary>
        void Tick(FramePhase phase, float dt);
        void Shutdown();
    }

    /// <summary>Convenience base with no-op defaults.</summary>
    public abstract class AppModule : IAppModule
    {
        public virtual int Order => 0;
        public virtual void Init(App app) { }
        public virtual void Tick(FramePhase phase, float dt) { }
        public virtual void Shutdown() { }
    }
}
