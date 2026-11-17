// The app's user layers. QuestSetup.ConfigureAll names them in ProjectSettings/TagManager.asset; the indices work even
// where the names are missing (a GameObject can use any layer 0..31), so code always uses these constants, never
// LayerMask.NameToLayer. AppRoot removes Hidden and PanelUI from the head camera's culling mask at boot (SceneBuilder
// writes the same mask into Main.unity).
namespace MusicRoom.Core
{
    public static class Layers
    {
        /// <summary>Desktop click targets: the colliders a module's desktop path raycasts (piano keys, drum tops and cymbals,
        /// the harp's string plane, deck pegs, switches, the menu face). DesktopRigFallback does not look around when a
        /// left-button press lands on one (the look-drag starts only when no instrument mesh was hit).</summary>
        public const int PlaySurface = 29;
        /// <summary>Never drawn by the XR / desktop camera (helpers, colliders without visuals, offscreen sources).</summary>
        public const int Hidden = 30;
        /// <summary>The menu's offscreen uGUI / TMP canvas: drawn only by the menu's own render request (spec 08 section 1.3).</summary>
        public const int PanelUI = 31;

        public const int PlaySurfaceMask = 1 << PlaySurface;
        /// <summary>Layers the head camera must not draw.</summary>
        public const int HeadCameraExcludeMask = (1 << Hidden) | (1 << PanelUI);

        /// <summary>Index -> name, as written into TagManager.asset by QuestSetup.ConfigureAll.</summary>
        public static readonly (int index, string name)[] Names = { (PlaySurface, "PlaySurface"), (Hidden, "Hidden"), (PanelUI, "PanelUI") };
    }
}
