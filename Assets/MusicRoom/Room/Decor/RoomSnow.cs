// Snow in the whole room (2026-11-13: "anytime if snow is clicked make it fall in whole room 360 degree coverage. ... in
// VR keep snow in window as it's there"): whenever the SNOW switch is on, flakes fall all around the player, inside the
// VR room (its own box, read from the built room: it follows the room's pose) and in mixed reality inside the player's
// real room (a round snowfall MrRadius about them, from the real floor). The VR window's own snow outside stays as it is.
// ONE mesh, ONE draw (MusicRoom/RoomSnow, template Room/RoomSnow): RoomSnowModel.Count flakes as quads whose random numbers
// are written once; the vertex shader moves them from the weather's clock (the same SnowWeather as the window: its level
// brings them in one by one as the switch eases on, its wind is a soft indoor draft). A frame sets ten material vectors.
// With the music: while a recording plays back, the window show's gust (WindowShowModel, the one envelope) swirls the
// room's snow on every beat (eddies, a whirl about the player, a lift) and the flakes near the player take a faint tint
// of the instrument playing (RoomSnowModel.Tint). Recording and live play never do (no PlaybackNoteEvent, and the
// window model only counts notes between a playback's start and end).
// Sunny: tip_room_snow once ever, the first time the room snow has been falling a few seconds.
// Main thread: notes come in the Recorder phase, Tick in Room's Decor phase (also while the room is hidden: MR).
// Allocation-free per frame after construction.
using System;
using MusicRoom.Core;
using MusicRoom.Room.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room.Decor
{
    public sealed class RoomSnow
    {
        public const string TipId = "tip_room_snow";
        /// <summary>Sunny's line: once the snow has been falling (level at least TipLevel) TipAfter seconds; asked again every
        /// TipEvery while Sunny is busy, until said.</summary>
        public const float TipAfter = 3f, TipEvery = 4f, TipLevel = 0.6f;
        /// <summary>The flakes' colour (sRGB) and opacity: VR, the warm lamp- and firelit room; MR, soft white over the real room.</summary>
        public static readonly Color VrColor = new Color(0.86f, 0.80f, 0.74f), MrColor = new Color(0.97f, 0.98f, 1f);
        public const float VrOpacity = 0.9f, MrOpacity = 0.95f, FireWarmth = 1f;
        /// <summary>Mixed reality: the snowfall's centre follows the head on the floor with this time constant (s).</summary>
        public const float MrFollow = 1.5f;

        public static readonly int ClockId = Shader.PropertyToID("_Clock"), BoxCId = Shader.PropertyToID("_BoxC"),
            BoxHId = Shader.PropertyToID("_BoxH"), ModeId = Shader.PropertyToID("_Mode"), HeadId = Shader.PropertyToID("_Head"),
            FwdId = Shader.PropertyToID("_Fwd"), ShowId = Shader.PropertyToID("_Show"), FireId = Shader.PropertyToID("_Fire"),
            TintId = Shader.PropertyToID("_Tint"), ColorId = Shader.PropertyToID("_Color");

        struct Vert
        {
            public Vector3 Pos;
            public Vector4 Uv0, Uv1;
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
        };

        readonly App _app;
        readonly RoomScene _scene;
        readonly SnowWeather _weather;
        readonly WindowShow _show;
        readonly Action<PlaybackNoteEvent> _onNote;
        readonly Action<RecordingPlaybackEvent> _onPlayback;
        readonly Action<RecorderStateEvent> _onState;
        readonly GameObject _go;
        readonly Mesh _mesh;
        readonly MeshRenderer _renderer;
        readonly Material _material;
        readonly RoomSnowModel.Seed[] _seeds = new RoomSnowModel.Seed[RoomSnowModel.Count];
        IMixedReality _mr;
        IGuide _guide;
        bool _mrCentred, _tipDone;
        Vector3 _mrCentre;
        float _gust, _time, _shownFor, _tipAskAt;
        RoomSnowModel.Frame _frame;

        public RoomSnowModel Model { get; } = new RoomSnowModel();
        /// <summary>Each flake's random numbers, as in the mesh (tests: RoomSnowModel.FlakeAt with Frame).</summary>
        public RoomSnowModel.Seed[] Seeds => _seeds;
        /// <summary>What the shader got this frame.</summary>
        public RoomSnowModel.Frame Frame => _frame;
        public MeshRenderer Renderer => _renderer;
        public Material Material => _material;
        public Mesh Mesh => _mesh;
        /// <summary>The snow falls in the room now (the SNOW level above 0; with a material: the renderer is on).</summary>
        public bool Falling { get; private set; }
        /// <summary>The volume is mixed reality's (round, about the player) this frame.</summary>
        public bool InMixedReality { get; private set; }
        /// <summary>Sunny was asked and is saying it (or had said it before).</summary>
        public bool TipAsked => _tipDone;
        /// <summary>The gust the room's snow follows now (the window show's, risen softly); 0 at rest.</summary>
        public float Gust => _gust;

        public RoomSnow(App app, RoomScene scene, SnowWeather weather, WindowShow show, Material material, uint seed)
        {
            _app = app;
            _scene = scene;
            _weather = weather;
            _show = show;
            RoomSnowModel.Seeds(seed, _seeds);
            _onNote = OnNote;
            _onPlayback = OnPlayback;
            _onState = OnState;
            app.Events.Subscribe(_onNote);
            app.Events.Subscribe(_onPlayback);
            app.Events.Subscribe(_onState);
            if (app.Headless || material == null) return;
            _material = material;
            _go = new GameObject("RoomSnow");
            _go.transform.SetParent(app.WorldRoot, false);
            int n = RoomSnowModel.Count, nv = n * 4, ni = n * 6;
            var verts = new Vert[nv];
            var idx = new ushort[ni];
            for (int i = 0; i < n; i++)
            {
                RoomSnowModel.Seed s = _seeds[i];
                for (int c = 0; c < 4; c++)
                    verts[i * 4 + c] = new Vert
                    {
                        Pos = s.Pos,
                        Uv0 = new Vector4(c == 1 || c == 2 ? 1f : 0f, c >= 2 ? 1f : 0f, s.Tone, s.Size),
                        Uv1 = new Vector4(s.SwayA, s.SwayRate, s.Phase, s.Fall),
                    };
                int v = i * 4, k = i * 6;
                idx[k] = (ushort)v; idx[k + 1] = (ushort)(v + 1); idx[k + 2] = (ushort)(v + 2);
                idx[k + 3] = (ushort)v; idx[k + 4] = (ushort)(v + 2); idx[k + 5] = (ushort)(v + 3);
            }
            // The flakes are placed in the world by the shader: the bounds hold any room the player can be in.
            var bounds = new Bounds(Vector3.zero, Vector3.one * 400f);
            _mesh = new Mesh { name = "RoomSnow" };
            _mesh.SetVertexBufferParams(nv, Layout);
            _mesh.SetVertexBufferData(verts, 0, 0, nv, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.SetIndexBufferParams(ni, IndexFormat.UInt16);
            _mesh.SetIndexBufferData(idx, 0, 0, ni, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.subMeshCount = 1;
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, ni) { bounds = bounds }, MeshUpdateFlags.DontRecalculateBounds);
            _mesh.bounds = bounds;
            _mesh.UploadMeshData(true);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.allowOcclusionWhenDynamic = false;
            _renderer.enabled = false;
        }

        void OnNote(PlaybackNoteEvent e)
        {
            if (_show != null && _show.Model.Playing) Model.Note(e, _app.AudioTime);
        }

        void OnPlayback(RecordingPlaybackEvent e) { if (!e.Started) Model.Stop(); }

        void OnState(RecorderStateEvent e) { if (e.State == RecorderState.Recording) Model.Stop(); }

        /// <summary>Room's Decor phase, every frame, after the window show (also while the room is hidden: mixed reality).</summary>
        public void Tick(float dt, bool roomVisible)
        {
            _time += dt;
            Model.Tick(dt, _app.AudioTime);
            float level = _weather != null ? _weather.Level : 0f;
            Falling = level > 0.001f;

            // the window show's gust, risen softly (its fall is already gentle); exactly 0 when it is
            float g = _show != null ? _show.Model.Gust : 0f;
            if (g > _gust) _gust += (g - _gust) * (1f - Mathf.Exp(-dt / RoomSnowModel.GustRise));
            else _gust = g;
            if (_gust < RoomSnowModel.Rest && g <= 0f) _gust = 0f;

            if (!Falling)
            {
                if (_renderer != null && _renderer.enabled) _renderer.enabled = false;
                _shownFor = 0f;
                _mrCentred = false;
                return;
            }

            Transform head = _app.Head;
            Vector3 hp = head != null ? head.position : new Vector3(0f, 1.2f, 0f);
            Vector3 fwd = head != null ? head.forward : Vector3.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;

            InMixedReality = !roomVisible || _scene == null || _scene.Root == null;
            RoomSnowModel.Volume vol;
            Vector4 fire = Vector4.zero;
            if (!InMixedReality)
            {
                Transform root = _scene.Root;
                vol = RoomSnowModel.ForRoom(root.position, root.eulerAngles.y, RoomFrame.LocalBox);
                _mrCentred = false;
                Transform f = _scene.Fireplace;
                if (f != null)
                {
                    Vector3 c = f.position + f.up * 0.35f + f.forward * 0.25f;
                    fire = new Vector4(c.x, c.y, c.z, FireWarmth);
                }
            }
            else
            {
                if (_mr == null) _mr = _app.Get<IMixedReality>();
                float floor = _mr != null ? _mr.FloorHeight : 0f;
                if (!_mrCentred) { _mrCentre = hp; _mrCentred = true; }
                else _mrCentre += (hp - _mrCentre) * (1f - Mathf.Exp(-dt / MrFollow));
                vol = RoomSnowModel.ForMr(_mrCentre, hp.y, floor);
            }

            _frame = new RoomSnowModel.Frame
            {
                Time = _weather.Time, Wind = _weather.Wind, WindVel = _weather.WindVel, Level = level,
                Vol = vol, Head = hp, Forward = fwd,
                Show = new Vector4(_gust, _show != null ? _show.Model.Phase : 0f, RoomSnowModel.Whirl * _gust, RoomSnowModel.Lift * _gust),
                Fire = fire,
            };

            if (_material != null)
            {
                if (!_renderer.enabled) _renderer.enabled = true;
                Color col = (InMixedReality ? MrColor : VrColor).linear;
                _material.SetVector(ClockId, new Vector4(_frame.Time, _frame.Wind, _frame.WindVel, level));
                _material.SetVector(BoxCId, new Vector4(vol.Centre.x, vol.Centre.y, vol.Centre.z, vol.Cos));
                _material.SetVector(BoxHId, new Vector4(vol.Half.x, vol.Half.y, vol.Half.z, vol.Sin));
                _material.SetVector(ModeId, new Vector4(vol.Round ? 1f : 0f, 0f, 0f, 0f));
                _material.SetVector(HeadId, new Vector4(hp.x, hp.y, hp.z, 0f));
                _material.SetVector(FwdId, new Vector4(fwd.x, 0f, fwd.z, 0f));
                _material.SetVector(ShowId, _frame.Show);
                _material.SetVector(FireId, fire);
                _material.SetVector(TintId, Model.Tint);
                _material.SetVector(ColorId, new Vector4(col.r, col.g, col.b, InMixedReality ? MrOpacity : VrOpacity));
            }
            AskSunny(dt, level);
        }

        void AskSunny(float dt, float level)
        {
            if (_tipDone) return;
            _shownFor = level >= TipLevel ? _shownFor + dt : 0f;
            if (_shownFor < TipAfter || _time < _tipAskAt) return;
            _tipAskAt = _time + TipEvery;
            if (_guide == null) _guide = _app.Get<IGuide>();
            if (_guide == null) return;
            if (_guide.TipSeen(TipId) || _guide.Tip(TipId) > 0f) _tipDone = true;
        }

        public void Destroy()
        {
            _app.Events.Unsubscribe(_onNote);
            _app.Events.Unsubscribe(_onPlayback);
            _app.Events.Unsubscribe(_onState);
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
        }
    }
}
