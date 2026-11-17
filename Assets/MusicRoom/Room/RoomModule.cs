// The calm evening room (spec 07; ARCHITECTURE.md sections 13 and 15.4): shell, furniture, plants, flowers, candles, fireplace
// with live flames, coals, glow, smoke and embers, the window onto a night sky with real snowfall, frost and a sill
// mound, lighting (moon + floor lamp + fire + 2 candles, Trilight ambient, one reflection probe), the side-table rig
// with the FIRE / WIND / SNOW switches (an IInstrument), contact shadows that follow the instrument rigs (one mesh for
// every at-seat instrument, from the descriptors), SnowWeather and the furniture as data (IRoomFurniture, measured while the room is built: World/Furniture.cs).
// Order 20: after Audio (10), before the instruments (30+), which find the Table rig in GetAll<Rig>().
// Per frame (Decor phase): fire time (also the plants' sway clock, in their shader), the listen-back pulse (lights, motes,
// flowers) and the butterflies (also while the room is hidden), weather + snow globals and the
// room's snow dressing, flicker lights, texture uploads; EndFrame: the rig shadows (after Layout's page-turn animation).
// Allocation-free after Init.
using System;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Room.Decor;
using MusicRoom.Room.Render;
using MusicRoom.Room.Table;
using MusicRoom.Room.Textures;
using MusicRoom.Room.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room
{
    public sealed class RoomModule : AppModule, IRoom, IAmbienceSwitches
    {
        public const int ModuleOrder = 20;
        public override int Order => ModuleOrder;

        // Shader globals (ARCHITECTURE.md section 13).
        public static readonly int FireTimeId = Shader.PropertyToID("_FireTime"), SnowTimeId = Shader.PropertyToID("_SnowTime"),
            SnowIntensityId = Shader.PropertyToID("_SnowIntensity"), SnowWindId = Shader.PropertyToID("_SnowWind"),
            SnowWindVelId = Shader.PropertyToID("_SnowWindVel"), SnowCoverId = Shader.PropertyToID("_SnowCover"),
            SnowPxId = Shader.PropertyToID("_SnowPx");

        /// <summary>Trilight ambient for the VR room (spec 07 section 6.2, ambient.vr).</summary>
        public static readonly Color AmbientSky = new Color32(0x74, 0x75, 0x78, 0xff), AmbientEquator = new Color32(0x69, 0x69, 0x6a, 0xff), AmbientGround = new Color32(0x4c, 0x4c, 0x4b, 0xff);
        static readonly Color GroundBase = TextureUtil.Hex("#05070f").linear, GroundSnow = TextureUtil.Hex("#435070").linear;

        App _app;
        IAudioEngine _audio;
        TextureLoader _textures;
        RoomMaterials _mats;
        readonly bool[] _switch = new bool[3];
        Mulberry32 _rng;
        float _time;
        bool _probeDone, _shadowsBound;
        int _frames;
        float _lastLevel = -1f, _lastCover = -1f;
        Texture2D _bookPalette;
        Action<Texture2D> _onHillSnow;

        public RoomScene Scene { get; private set; }
        public SideTable Table { get; private set; }
        public RigShadows Shadows { get; private set; }
        /// <summary>The room's furniture as the display plan sees it (IRoomFurniture, platform.md 4.1).</summary>
        public RoomFurniture Furniture { get; private set; }
        public SnowWeather Weather { get; private set; }
        /// <summary>The room listening back: lights, motes and flowers that pulse with a playing take or loop and with what is
        /// being recorded (Decor/ListenBackPulse.cs).</summary>
        public ListenBackPulse ListenBack { get; private set; }
        /// <summary>The butterflies that come while music is recorded or played back (Decor/Butterflies.cs).</summary>
        public Butterflies Butterflies { get; private set; }
        /// <summary>The song's night sky: every played-back note a star in a sky above the room (Decor/SongSky.cs).</summary>
        public SongSky SongSky { get; private set; }
        /// <summary>The snowy window joins in while a recording plays back: gusts in the snow, northern lights in the notes'
        /// colours, in the VR room's window (Decor/WindowShow.cs).</summary>
        public WindowShow WindowShow { get; private set; }
        /// <summary>Snow in the whole room while SNOW is on: the VR room, or the player's own room in mixed reality; it swirls
        /// with a playback (Decor/RoomSnow.cs).</summary>
        public RoomSnow RoomSnow { get; private set; }
        /// <summary>The notes paint the walls while a recording plays back: the VR room's walls, the real ones in mixed reality
        /// (Decor/WallPaint.cs).</summary>
        public WallPaint WallPaint { get; private set; }
        /// <summary>A fireplace in the player's own room while FIRE is on in mixed reality (Decor/MrFireplace.cs).</summary>
        public MrFireplace MrFireplace { get; private set; }
        public Rig TableRig => Table?.Rig;
        public AmbiencePanel Switches => Table?.Switches;
        /// <summary>Generated textures still on their way (0 once the room is fully textured).</summary>
        public int TexturesPending => _textures?.Pending ?? 0;
        public float DecorTime => _time;
        public bool ProbeRendered => _probeDone;

        public override void Init(App app)
        {
            _app = app;
            _audio = app.Get<IAudioEngine>();
            uint seed = Mulberry32.SessionSeed();
            _rng = new Mulberry32(seed ^ 0x5eedu);
            _textures = new TextureLoader(!app.Headless);
            _mats = new RoomMaterials(app.Materials, _textures);
            _bookPalette = _mats[RoomMat.Books].GetTexture(MaterialLibrary.BaseMap) as Texture2D;
            _onHillSnow = _ => Scene.SkyMaterial.SetFloat(RoomMaterials.HillSnowOn, 1f);

            Scene = new RoomBuilder(_mats, seed).Build(app.WorldRoot);
            // The hills' snow overlay is baked, so it loads with the rest at boot (drawing its pixels on the CPU would need a
            // deferred, lazy load, and that would hitch mid-session on the first frame snow settles).
            // NightSky.shader skips the overlay while _SnowCover is 0, so the look is unchanged until snow lies.
            _textures.Bind(RoomTextures.HillSnow, Scene.SkyMaterial, RoomMaterials.HillSnowMap, onReady: _onHillSnow);
            Table = SideTable.Build(app, app.WorldRoot, _mats, app.Materials, Set);
            Shadows = new RigShadows();
            Shadows.Build(Scene.Root, app.WorldRoot, _mats[RoomMat.Decal], app.Registry);
            Furniture = RoomFurniture.From(Scene.Footprints, RoomFrame.LocalBox);   // measured in the room frame, published turned
            Weather = new SnowWeather(seed ^ 0x51u);
            Weather.OnGust += OnGust;
            ListenBack = new ListenBackPulse(app, Scene, _mats[RoomMat.LampShade], seed ^ 0x11b4u);
            ListenBack.SetFlowers(_mats[RoomMat.Petal]);
            Butterflies = new Butterflies(app, ListenBack, Scene, _mats[RoomMat.Butterfly], seed ^ 0xb077u);
            SongSky = new SongSky(app, Scene, ListenBack, app.Headless || !app.Materials.HasTemplate(RoomTemplates.SongStars) ? null
                : app.Materials.Clone(RoomTemplates.SongStars), seed ^ 0x5c1eu);
            WindowShow = new WindowShow(app, Scene);
            RoomSnow = new RoomSnow(app, Scene, Weather, WindowShow, app.Headless || !app.Materials.HasTemplate(RoomTemplates.RoomSnow) ? null
                : app.Materials.Clone(RoomTemplates.RoomSnow), seed ^ 0x5a0cu);
            WallPaint = new WallPaint(app, Scene);
            MrFireplace = new MrFireplace(app, _mats, this, WallPaint.Gallery, seed ^ 0xf17eu);
            ApplyLighting();

            app.Register<Rig>(Table.Rig);
            app.Register<IInstrument>(Table.Switches);
            app.Register<IRoom>(this);
            app.Register<IAmbienceSwitches>(this);
            app.Register<ISnowWeather>(Weather);
            app.Register<IRoomFurniture>(Furniture);
            PushSnowGlobals();
            Debug.Log($"[Room] ready: {Scene.Static.Count} static renderers, {Scene.StaticTriangles} static triangles, {Scene.Plants.Count} plants, " +
                      $"textures pending {_textures.Pending}, headless={app.Headless}");
        }

        void OnGust(SnowGust g) => _audio?.SnowGust(g.Strength, g.Attack, g.Hold, g.Release);

        /// <summary>The VR room's ambient and main light (MR replaces both while active; SetVisible(true) restores them).</summary>
        void ApplyLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.sun = Scene.Moon;
        }

        public override void Tick(FramePhase phase, float dt)
        {
            if (phase == FramePhase.Decor) Decor(dt);
            else if (phase == FramePhase.EndFrame && Shadows != null)
            {
                if (!_shadowsBound) { Shadows.Bind(_app); _shadowsBound = true; }
                Shadows.Update();
            }
        }

        void Decor(float dt)
        {
            _time += dt;
            _frames++;
            Shader.SetGlobalFloat(FireTimeId, _time);
            Weather.Update(dt);
            PushSnowGlobals();
            _textures.Update();
            bool visible = Visible;
            ListenBack?.Tick(dt, visible);   // before the flicker: it sets their gain
            Butterflies?.Tick(dt, visible);   // after the pulse: it reads this frame's notes
            SongSky?.Tick(dt, visible);
            WindowShow?.Tick(dt, visible);    // the window's gusts and northern lights
            RoomSnow?.Tick(dt, visible);      // snow in the whole room (after the show: its gust swirls it)
            WallPaint?.Tick(dt, visible);     // the notes' splashes on the walls (MR: the real walls)
            MrFireplace?.Tick(dt, visible);   // MR: the fireplace FIRE lights in the real room
            if (!visible) return;   // mixed reality: the weather still runs, the room does not
            ApplySnowDressing();
            for (int i = 0; i < Scene.Flicker.Count; i++) Scene.Flicker[i].Update(_time, dt, ref _rng);
            if (!_probeDone && _frames >= 2 && _textures.Pending == 0) RenderProbe();
        }

        void PushSnowGlobals()
        {
            Shader.SetGlobalFloat(SnowTimeId, Weather.Time);
            Shader.SetGlobalFloat(SnowIntensityId, Weather.Level);
            Shader.SetGlobalFloat(SnowWindId, Weather.Wind);
            Shader.SetGlobalFloat(SnowWindVelId, Weather.WindVel);
            Shader.SetGlobalFloat(SnowCoverId, Weather.Cover);
            Shader.SetGlobalFloat(SnowPxId, SnowPx());
        }

        /// <summary>Pixels per unit of view height for the flake size: half the eye-buffer height in XR (the render
        /// target), half the screen on a desktop.</summary>
        float SnowPx()
        {
            float h = _app.Rig != null && _app.Rig.IsXrActive ? UnityEngine.XR.XRSettings.eyeTextureHeight : Screen.height;
            return h > 0f ? h * 0.5f : 400f;
        }

        /// <summary>The room's snow dressing (spec 07 section 5.3): flakes, sky tint, hill snow, ground, sill mound, frost.</summary>
        void ApplySnowDressing()
        {
            float l = Weather.Level, c = Weather.Cover;
            bool field = l > 0.001f;
            if (Scene.SnowField.enabled != field) Scene.SnowField.enabled = field;
            if (l != _lastLevel)
            {
                float k = 1f - 0.12f * l;
                Scene.SkyMaterial.SetVector(RoomMaterials.Tint, new Vector4(k, k + 0.02f * l, k + 0.06f * l, 1f));
                _lastLevel = l;
            }
            if (c != _lastCover)
            {
                Color g = Color.Lerp(GroundBase, GroundSnow, Mathf.Pow(c, 0.8f) * 0.92f);
                Scene.GroundMaterial.SetVector(RoomMaterials.Tint, new Vector4(g.r, g.g, g.b, 1f));
                bool settled = c > 0.01f;
                if (Scene.Mound.enabled != settled) Scene.Mound.enabled = settled;
                Scene.MoundTransform.localScale = new Vector3(1f, Mathf.Max(0.001f, Mathf.Pow(c, 0.75f)), 1f);
                if (Scene.Frost.enabled != settled) Scene.Frost.enabled = settled;
                Scene.FrostMaterial.SetFloat(RoomMaterials.FrostAmount, c);
                _lastCover = c;
            }
        }

        void RenderProbe()
        {
            _probeDone = true;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || Scene.Probe == null) return;
            Scene.Probe.RenderProbe();
        }

        /// <summary>Re-render the reflection probe on the next Decor tick (after a big change to the room's look).</summary>
        public void RefreshProbe() => _probeDone = false;

        public override void Shutdown()
        {
            if (Weather != null) Weather.OnGust -= OnGust;
            Butterflies?.Destroy();
            SongSky?.Destroy();
            WindowShow?.Destroy();
            RoomSnow?.Destroy();
            WallPaint?.Destroy();
            MrFireplace?.Destroy();
            ListenBack?.Destroy();
            Shadows?.Destroy();
            Table?.Destroy();
            if (Scene != null)
            {
                if (Scene.Root != null) UnityEngine.Object.Destroy(Scene.Root.gameObject);
                foreach (var m in Scene.Meshes) if (m != null) UnityEngine.Object.Destroy(m);
                Scene.Meshes.Clear();
            }
            if (_bookPalette != null) UnityEngine.Object.Destroy(_bookPalette);
            if (_mats?.SofaPaletteTexture != null) UnityEngine.Object.Destroy(_mats.SofaPaletteTexture);
            if (_mats?.PetalPaletteTexture != null) UnityEngine.Object.Destroy(_mats.PetalPaletteTexture);
        }

        // ------------------------------------------------------------------ IRoom

        public Transform Root => Scene?.Root;
        public bool Visible => Scene != null && Scene.Root != null && Scene.Root.gameObject.activeSelf;

        /// <summary>Hide or show the shell, decor and its lights (mixed reality). The table rig, the weather and the
        /// instruments' floor shadows (outside the room root) stay, but for the piano's, which goes with the room
        /// (ShadowSpec.HiddenInMr).</summary>
        public void SetVisible(bool visible)
        {
            if (Scene == null || Scene.Root == null) return;
            if (Scene.Root.gameObject.activeSelf != visible) Scene.Root.gameObject.SetActive(visible);
            if (visible) ApplyLighting();
        }

        /// <summary>Candles on / off: every candle mesh and both candle lights. The wax, flames and lights live under
        /// the Candles group; the holders and wicks are part of the static brass and soot meshes, swapped for copies
        /// without them (no renderer of their own).</summary>
        public void SetCandles(bool on)
        {
            if (Scene?.Candles == null) return;
            if (Scene.Candles.gameObject.activeSelf != on) Scene.Candles.gameObject.SetActive(on);
            Swap(RoomMat.Brass, on ? Scene.BrassWithCandles : Scene.BrassWithoutCandles);
            Swap(RoomMat.Soot, on ? Scene.SootWithCandles : Scene.SootWithoutCandles);

            void Swap(RoomMat k, Mesh m)
            {
                if (m == null || !Scene.Static.TryGetValue(k, out var r) || r == null) return;
                var mf = r.GetComponent<MeshFilter>();
                if (mf.sharedMesh != m) mf.sharedMesh = m;
            }
        }

        public bool CandlesOn => Scene?.Candles != null && Scene.Candles.gameObject.activeSelf;

        /// <summary>The room box in the world (the room turned to face the fire: RoomFrame.WorldBox).</summary>
        public Bounds RoomBox => GrabConstants.RoomBox;

        // ------------------------------------------------------------------ IAmbienceSwitches

        public bool Get(AmbienceKind kind) => _switch[(int)kind];

        /// <summary>Switch flip / apply: lever and LED, the ambience sound, the snow weather's target, AmbienceEvent.
        /// FIRE and WIND switch only their sounds: the fire always burns (in mixed reality FIRE also lights a fireplace in the
        /// player's room: MrFireplace).</summary>
        public void Set(AmbienceKind kind, bool on)
        {
            if (_switch[(int)kind] == on) return;
            _switch[(int)kind] = on;
            Table?.Switches.SetState(kind, on);
            if (_audio != null)
            {
                if (kind == AmbienceKind.Fire) _audio.SetFire(on);
                else if (kind == AmbienceKind.Wind) _audio.SetWind(on);
                else _audio.SetSnow(on);
            }
            if (kind == AmbienceKind.Snow && Weather != null) Weather.Target = on ? 1f : 0f;
            _app.Events.Publish(new AmbienceEvent { Kind = kind, On = on });
        }
    }
}
