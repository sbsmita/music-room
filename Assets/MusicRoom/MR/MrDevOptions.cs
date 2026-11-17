// Development switches for "Play in my room" without a headset (editor play mode, PlayMode tests): passthrough is
// simulated by a plain camera background, and when the device scene load reports NotAvailable (no Link, no runtime)
// one of MRUK's recorded sample rooms is loaded instead (spec 03 section 2.12). On a headset none of this applies:
// SimulatePassthrough only matters when real passthrough is unavailable, and the JSON fallback only on NotAvailable.
using UnityEngine;

namespace MusicRoom.MR
{
    public static class MrDevOptions
    {
        /// <summary>The sample room the editor falls back to (5 windows, 3 tables; spec 03 section 2.10).</summary>
        public const string DefaultRoomAsset = "Packages/com.meta.xr.mrutilitykit/Core/Rooms/Json/MeshLivingRoom2.json";

        /// <summary>Null = simulate in the editor only. Tests set it explicitly.</summary>
        public static bool? SimulatePassthroughOverride;

        /// <summary>Enter mixed reality even though passthrough is unavailable, with <see cref="SimulatedBackground"/> as the
        /// "passthrough". Never true in a player build unless a test sets it.</summary>
        public static bool SimulatePassthrough => SimulatePassthroughOverride ?? Application.isEditor;

        /// <summary>The camera clear colour standing in for passthrough (a mid grey). (0,0,0,0) reproduces the headset's
        /// eye buffer exactly (alpha 0 where passthrough shows).</summary>
        public static Color SimulatedBackground = DefaultSimulatedBackground;
        public static readonly Color DefaultSimulatedBackground = new Color(0.5f, 0.5f, 0.5f, 1f);

        /// <summary>A scene JSON (MRUK "CoordinateSystem": "Unity") to load when the device says NotAvailable.
        /// Null = the editor's default sample room.</summary>
        public static string SceneJson;

        /// <summary>The JSON room to load, or null (player builds have no AssetDatabase).</summary>
        public static string ResolveSceneJson()
        {
            if (!string.IsNullOrEmpty(SceneJson)) return SceneJson;
#if UNITY_EDITOR
            var t = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultRoomAsset);
            return t != null ? t.text : null;
#else
            return null;
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() // also for play mode with domain reload disabled
        {
            SimulatePassthroughOverride = null;
            SimulatedBackground = DefaultSimulatedBackground;
            SceneJson = null;
        }
    }
}
