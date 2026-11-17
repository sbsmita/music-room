// Palm menu contract (spec 08 section 1). The Menu module implements IMenuPanel and publishes MenuEvent.
using UnityEngine;

namespace MusicRoom.Core
{
    /// <summary>Start = the launch panel ("Play here" / "Play in my room"); Arrange = the floating Done / Reset buttons of
    /// "Move instruments" (never the palm panel's screen).</summary>
    /// <summary>First five: Guide = the Sunny screen (the tour again, tips, Make a song in layers, Tips while I play), Tips =
    /// its list of every tip.</summary>
    public enum MenuScreen { Home = 0, Lessons, Courses, Lesson, Instruments, Recordings, Take, Start, Arrange, Guide, Tips }

    public enum MenuChange { Open, Close, Screen }

    public interface IMenuPanel
    {
        bool Shown { get; }
        MenuScreen Screen { get; }
        /// <summary>Show the panel for a hand: pose from the palm (or look direction), clear-out, MenuEvent Open.</summary>
        void Show(HandState hand = null);
        void Hide();
        void Toggle(HandState hand = null);
        void Go(MenuScreen screen);
        /// <summary>Mark dirty: relayout + redraw on the next frame.</summary>
        void Redraw();
        /// <summary>World AABB of face + back plate expanded by 0.12 m while shown (grab zone), else null.</summary>
        Bounds? GrabZone { get; }
        /// <summary>The panel's rig (RigId.Lessons); grabbable even when instruments are locked.</summary>
        Rig Rig { get; }
        Transform Root { get; }
        /// <summary>The start panel has not been answered yet (shown, or about to show once the room is ready). Sunny's
        /// first-launch tour waits for it.</summary>
        bool StartPending => false;
    }

    /// <summary>Poke constants (spec 08 section 1.4).</summary>
    public static class MenuConstants
    {
        public const float Radius = 0.8f, Width = 0.32f, Height = 0.24f;
        public const int CanvasW = 1024, CanvasH = 768;
        public const float Arm = 0.003f, Hover = 0.010f, Slop = 0.008f, Behind = -0.06f, EdgeMargin = 0.01f;
        public const float DebounceMs = 250f;
        public const float SummonSeconds = 0.5f, SummonMinGrasp = 0.15f, SummonMinDist = 0.08f, SummonMaxDist = 0.8f, SummonCos = 0.7660f;
        public const float SummonQuietMs = 400f;
        public static readonly Vector3 HomeSeat = new Vector3(0f, 1.03f, 0.47f);
    }
}
