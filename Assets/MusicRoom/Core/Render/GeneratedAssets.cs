// Baked procedural textures (TextureBaker writes Assets/MusicRoom/Generated/Resources/Generated/<Module>/<name>.png) with
// the generator as the fallback, so the app works unbaked (tests, first run) and fast when baked. Names are
// "<Module>/<name>" (shared foundation textures "Core/<name>", see ProcTex.SharedTextures); TextureBaker refuses
// duplicates across providers, so a name means one generator. A device build bakes first (BuildPlayer), so the
// fallback should never run on a headset: when it does, it logs "[Render] runtime texture <name>" once and, in a
// player, compresses the texture and drops its CPU copy (no 4-byte-per-texel readable copy kept in memory).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    public static class GeneratedAssets
    {
        public const string ResourcesPrefix = "Generated";

        static readonly Dictionary<string, Texture2D> s_cache = new Dictionary<string, Texture2D>();

        /// <summary>Names generated at runtime this session (diagnostics: a non-empty list on device means an unbaked build).</summary>
        public static readonly List<string> RuntimeGenerated = new List<string>();

        /// <summary>True when a baked PNG exists for the name.</summary>
        public static bool HasBaked(string name) => Resources.Load<Texture2D>(ResourcesPrefix + "/" + name) != null;

        /// <summary>The baked texture, else the generator's output (cached by name for the session).</summary>
        public static Texture2D Texture(string name, Func<Texture2D> generate)
        {
            if (s_cache.TryGetValue(name, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>(ResourcesPrefix + "/" + name);
            if (t == null)
            {
                t = generate?.Invoke();
                if (t != null)
                {
                    t.name = name + " (runtime)";
                    RuntimeGenerated.Add(name);
                    Debug.Log($"[Render] runtime texture {name} ({t.width}x{t.height}): not baked (run Tools/unity.sh bake)");
                    if (!Application.isEditor) ReleaseCpuCopy(t);
                }
            }
            if (t != null) s_cache[name] = t;
            return t;
        }

        /// <summary>Player fallback: compress (ETC2 / DXT by platform, when the size allows) and free the readable copy.</summary>
        public static void ReleaseCpuCopy(Texture2D t)
        {
            try
            {
                if (t.isReadable && t.width % 4 == 0 && t.height % 4 == 0) t.Compress(true);
                if (t.isReadable) t.Apply(false, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Render] could not compress runtime texture {t.name}: {e.Message}");
            }
        }

        public static void ClearCache() => s_cache.Clear();
    }
}
