// The room's generated textures (ARCHITECTURE.md section 13 names) for the editor bake (ITextureBakeProvider) and the
// runtime, plus the shared Core ones it binds (SharedTextures names, loaded the same way). Baked PNGs load at once (GeneratedAssets). Unbaked ones (tests, a build without `bake`) are computed on
// worker threads, so a boot never blocks for the ~1-2 s of noise maths: materials show a placeholder tint until their
// texture arrives, then the texture is uploaded on the main thread (one per frame) and cached in GeneratedAssets for
// later boots. The jobs are static, so a test's next boot reuses a job an earlier boot started.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Room.Textures
{
    public static class RoomTextures
    {
        // ARCHITECTURE.md sections 2 (rule 4) and 13: "<Module>/<name>", baked to Generated/Room/<name>.png. The leaves and
        // the paintings are atlases (spec 07 section 11: one renderer for every plant's leaves, one for the three canvases);
        // the woods' URP metallic-gloss maps are the room's own (Core's roughness maps are grey, URP reads A as smoothness).
        public const string Plaster = "Room/plaster", Rug = "Room/rug", LeafAtlas = "Room/leaf_atlas", Brick = "Room/brick",
            WeaveNormal = "Room/weave_normal", SceneryAtlas = "Room/scenery_atlas", NightSky = "Room/night_sky", HillSnow = "Room/hill_snow",
            WoodOakGloss = "Room/wood_oak_gloss", WoodWalnutGloss = "Room/wood_walnut_gloss", ButterflyWings = "Room/butterfly_wings";
        // The foundation's shared set (Core ProcTex, baked by CoreTextureProvider).
        public const string WoodOak = SharedTextures.WoodOak, WoodWalnut = SharedTextures.WoodWalnut, SoftDot = SharedTextures.SoftDot;

        /// <summary>The room's own bake names (its ITextureBakeProvider).</summary>
        public static readonly string[] All =
        {
            WoodOakGloss, WoodWalnutGloss, Plaster, Rug, LeafAtlas, Brick, WeaveNormal, SceneryAtlas, NightSky, HillSnow, ButterflyWings,
        };

        /// <summary>Every texture the room binds (its own and the shared ones).</summary>
        public static readonly string[] Used =
        {
            WoodOak, WoodOakGloss, WoodWalnut, WoodWalnutGloss, Plaster, Rug, LeafAtlas, Brick, SoftDot, WeaveNormal, SceneryAtlas, NightSky, HillSnow,
            ButterflyWings,
        };

        /// <summary>u offset and scale of a leaf variant in the leaf atlas (light | dark).</summary>
        public static Vector2 LeafAtlasOffset(bool dark) => new Vector2(dark ? 0.5f : 0f, 0f);
        public static readonly Vector2 LeafAtlasScale = new Vector2(0.5f, 1f);

        /// <summary>UV scale and offset of painting <paramref name="seed"/> (1..3) in the scenery atlas.</summary>
        public static Vector2 SceneryAtlasScale => new Vector2(1f, (float)RoomTexGen.SceneryH / RoomTexGen.SceneryAtlasH);
        public static Vector2 SceneryAtlasOffset(int seed) => new Vector2(0f, (float)RoomTexGen.ScenerySlotRow(seed - 1) / RoomTexGen.SceneryAtlasH);

        /// <summary>The job that makes a texture (one image per job).</summary>
        public static string JobOf(string name) => name;

        /// <summary>Compute the images of one job (pure, any thread).</summary>
        public static PixelImage[] Compute(string job)
        {
            switch (job)
            {
                case WoodOakGloss: return new[] { RoomTexGen.WoodGloss(WoodParams.Oak, 1.0, WoodOakGloss) };      // floor: roughness 1
                case WoodWalnutGloss: return new[] { RoomTexGen.WoodGloss(WoodParams.Walnut, 0.8, WoodWalnutGloss) }; // walnut: roughness .8
                case Plaster: return new[] { RoomTexGen.Plaster(Plaster) };
                case Rug: return new[] { RoomTexGen.Rug(Rug) };
                case LeafAtlas: return new[] { RoomTexGen.LeafAtlas(LeafAtlas) };
                case Brick: return new[] { RoomTexGen.Brick(Brick) };
                case WeaveNormal: return new[] { RoomTexGen.WeaveNormal(WeaveNormal) };
                case SceneryAtlas: return new[] { RoomTexGen.SceneryAtlas(SceneryAtlas) };
                case NightSky: return new[] { RoomTexGen.NightSky(NightSky) };
                case HillSnow: return new[] { RoomTexGen.HillSnow(HillSnow) };
                case ButterflyWings: return new[] { ButterflyArt.Wings(ButterflyWings) };
                // Shared textures, only when the Core bake is missing (tests, an unbaked editor): Core's pixels on a worker.
                case WoodOak: return new[] { RoomTexGen.CoreWoodMap(WoodParams.Oak, WoodOak) };
                case WoodWalnut: return new[] { RoomTexGen.CoreWoodMap(WoodParams.Walnut, WoodWalnut) };
                case SoftDot: return new[] { RoomTexGen.CoreSoftDot(SoftDot) };
                default: throw new ArgumentException("unknown room texture " + job);
            }
        }

        /// <summary>Anisotropic filtering per texture: the floor, walls, rug and bricks are seen at grazing angles (three
        /// used anisotropy 4, the sky 8). Baked into the importer (TextureBakeSpec.AnisoLevel) and raised again on load.</summary>
        public static int AnisoFor(string name) =>
            name == NightSky ? 8 : name == WoodOak || name == WoodOakGloss || name == Plaster || name == Rug || name == Brick ? 4 : 1;

        public static PixelImage Image(string name)
        {
            foreach (var img in Compute(JobOf(name))) if (img.Name == name) return img;
            throw new ArgumentException(name);
        }

        /// <summary>Bake settings per texture (TextureBaker): sRGB colour maps, linear gloss / normal maps.</summary>
        public static IEnumerable<TextureBakeSpec> BakeSpecs()
        {
            foreach (string name in All)
            {
                string n = name;
                bool gloss = n == WoodOakGloss || n == WoodWalnutGloss;
                bool linear = gloss || n == WeaveNormal;
                bool alpha = gloss || n == LeafAtlas || n == HillSnow || n == ButterflyWings;
                bool repeat = gloss || n == Plaster || n == Brick || n == WeaveNormal;
                yield return new TextureBakeSpec
                {
                    Name = n, Generate = () => Image(n).ToTexture(readable: true), Linear = linear, Mipmaps = true, Repeat = repeat,
                    NormalMap = n == WeaveNormal, HasAlpha = alpha, AlphaIsTransparency = n == LeafAtlas || n == HillSnow || n == ButterflyWings,
                    AnisoLevel = AnisoFor(n),   // baked into the importer (TextureBakeSpec.AnisoLevel); the load-time raise stays as a fallback
                };
            }
        }

        // ------------------------------------------------------------------ runtime jobs (main thread bookkeeping)

        static readonly Dictionary<string, Task<PixelImage[]>> s_jobs = new Dictionary<string, Task<PixelImage[]>>();

        internal static Task<PixelImage[]> Start(string job)
        {
            if (s_jobs.TryGetValue(job, out var t) && !t.IsFaulted && !t.IsCanceled) return t;
            t = Task.Run(() => Compute(job));
            s_jobs[job] = t;
            return t;
        }

        internal static void Forget(string job) => s_jobs.Remove(job);
    }

    /// <summary>The room's ITextureBakeProvider (discovered by the editor's TextureBaker).</summary>
    public sealed class RoomTextureBakeProvider : ITextureBakeProvider
    {
        public IEnumerable<TextureBakeSpec> Textures
        {
            get { return RoomTextures.BakeSpecs(); }
        }
    }

    /// <summary>Per-boot binding of generated textures to materials (see the file comment).</summary>
    public sealed class TextureLoader
    {
        struct Target { public Material Material; public int Property; public bool SetColor; public Color ReadyColor; public Action<Texture2D> OnReady; }

        sealed class Slot { public string Name; public readonly List<Target> Targets = new List<Target>(4); public Task<PixelImage[]> Job; }

        readonly bool _generate, _ignoreCache;
        readonly List<Slot> _pending = new List<Slot>();
        readonly Dictionary<string, Texture2D> _ready = new Dictionary<string, Texture2D>();
        readonly HashSet<string> _failed = new HashSet<string>();
        readonly List<Texture2D> _owned = new List<Texture2D>();

        /// <param name="generate">false (headless boots): unbaked textures are never generated; materials keep their tint.</param>
        public TextureLoader(bool generate) { _generate = generate; }

        /// <summary>Tests: always generate in the background and upload here, ignoring baked and cached textures (the
        /// textures made are owned by this loader: DestroyOwned).</summary>
        internal TextureLoader(bool generate, bool ignoreCache) { _generate = generate; _ignoreCache = ignoreCache; }

        internal void DestroyOwned()
        {
            foreach (var t in _owned) if (t != null) UnityEngine.Object.Destroy(t);
            _owned.Clear();
        }

        /// <summary>Textures still being generated or waiting for upload.</summary>
        public int Pending => _pending.Count;

        /// <summary>The texture if it is available now (baked, cached or uploaded this boot).</summary>
        public Texture2D Get(string name)
        {
            if (_ready.TryGetValue(name, out var t) && t != null) return t;
            if (_ignoreCache) return null;
            t = GeneratedAssets.Texture(name, null);
            if (t != null)
            {
                int aniso = RoomTextures.AnisoFor(name);
                if (t.anisoLevel < aniso) t.anisoLevel = aniso;   // a baked PNG's importer default is 1
                _ready[name] = t;
            }
            return t;
        }

        /// <summary>Assign texture <paramref name="name"/> to <paramref name="property"/> of the material now or when it
        /// arrives. While it is pending the material shows <paramref name="placeholder"/>; on arrival the base colour
        /// becomes <paramref name="readyColor"/> (the material's real tint).</summary>
        public void Bind(string name, Material m, int property, Color? readyColor = null, Color? placeholder = null, Action<Texture2D> onReady = null)
        {
            var tex = Get(name);
            var target = new Target { Material = m, Property = property, SetColor = readyColor.HasValue, ReadyColor = readyColor ?? Color.white, OnReady = onReady };
            if (tex != null) { Apply(target, tex); return; }
            if (placeholder.HasValue && m != null) m.SetColor(MaterialLibrary.BaseColor, placeholder.Value);
            if (!_generate) return;
            foreach (var s in _pending) if (s.Name == name) { s.Targets.Add(target); return; }
            var slot = new Slot { Name = name, Job = RoomTextures.Start(RoomTextures.JobOf(name)) };
            slot.Targets.Add(target);
            _pending.Add(slot);
        }

        static void Apply(in Target t, Texture2D tex)
        {
            if (t.Material != null)
            {
                if (t.Property != 0) t.Material.SetTexture(t.Property, tex);
                if (t.SetColor) t.Material.SetColor(MaterialLibrary.BaseColor, t.ReadyColor);
            }
            t.OnReady?.Invoke(tex);
        }

        /// <summary>Main thread, once per frame: uploads at most one finished texture (a 2048 x 1024 upload with mips is
        /// tens of milliseconds; one per frame keeps the hitch small).</summary>
        public void Update()
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                Slot s = _pending[i];
                if (!s.Job.IsCompleted) continue;
                _pending.RemoveAt(i);
                if (s.Job.IsFaulted || s.Job.IsCanceled)
                {
                    if (_failed.Add(s.Name)) Debug.LogWarning($"[Room] texture {s.Name} failed: {s.Job.Exception?.GetBaseException().Message}");
                    RoomTextures.Forget(RoomTextures.JobOf(s.Name));
                    return;
                }
                Texture2D tex = Get(s.Name);              // another boot may have uploaded it meanwhile
                if (tex == null)
                {
                    foreach (var img in s.Job.Result)
                        if (img.Name == s.Name)
                        {
                            var made = img.ToTexture();
                            made.name = s.Name + " (runtime)";
                            if (_ignoreCache) { tex = made; _owned.Add(made); }
                            else tex = GeneratedAssets.Texture(s.Name, () => made);   // caches it for later boots
                            _ready[s.Name] = tex;
                        }
                }
                if (tex != null) foreach (var t in s.Targets) Apply(t, tex);
                // Drop the finished job once every image it made is cached (frees the pixel arrays).
                bool allCached = true;
                foreach (var img in s.Job.Result) if (GeneratedAssets.Texture(img.Name, null) == null) allCached = false;
                if (allCached) RoomTextures.Forget(RoomTextures.JobOf(s.Name));
                return;
            }
        }
    }
}
