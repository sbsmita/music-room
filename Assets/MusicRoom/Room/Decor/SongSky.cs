// "Your song becomes a night sky" (the playback show), drawn. SongSkyModel holds the stars; this feeds it and draws it:
//   - the notes: PlaybackNoteEvent onsets only (what a take or a loop sends back to its instruments: never a recording's
//     live notes, never plain play), each from above the very part that played it: the piano's key (IPianoApi.TryGetKey),
//     the drum or cymbal (IDrumsApi.Pieces), the harp's string (IHarpApi.PlayPoint), else the instrument's shown played
//     parts (ListenBackPulse.TryPlace: the marimba's bars spread low to high); an instrument hidden in mixed reality sends
//     its star straight from the sky. Height: SongStars.TryHeight in the take's own pitch range (a loop: C3..C6);
//   - the playback: RecordingPlaybackEvent starts the sky (framed from the head now, the room's ceiling as built when the
//     room shows; the song's length: the take's, or the loop's pass) and ends it; the recorder going idle ends it too;
//   - ONE mesh, ONE draw (MusicRoom/SongStars, template Room/SongStars): a soft night-blue field just under the ceiling (VR
//     only: it darkens a little, premultiplied alpha), the lines, then the stars. The stars' and lines' data are written
//     once when a star is made (its from, to, birth, size, colour, twinkle phase); the shader moves them (rise, settle,
//     arrival flash, twinkle, fades) from the model's clock, so a frame only sets a few material values. Stars and lines
//     add light (alpha 0: over passthrough they are light, as the motes are). Renderer off while the sky rests.
// Sunny: tip_song_stars once a playback has made TipStars stars and the room's own playback line (tip_listen_back) has
// been heard (asked again every TipEvery while she is busy or still saying that one).
// Main thread: notes come in the Recorder phase, Tick runs in Room's Decor phase (also while the room is hidden).
// Allocation-free per frame after construction (the take's pitch range is read once per playback start).
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Room.Render;
using MusicRoom.Room.World;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room.Decor
{
    public sealed class SongSky
    {
        public const string TipId = "tip_song_stars";
        public const int TipStars = 6;
        public const float TipEvery = 4f;
        /// <summary>The night field under the ceiling (VR): its colour (sRGB), most opacity, and how fast it comes (s).</summary>
        public static readonly Color FieldColor = new Color(0.03f, 0.05f, 0.14f);
        public const float FieldAlpha = 0.5f, FieldIn = 2.5f;
        /// <summary>A line's width (m).</summary>
        public const float LineWidth = 0.006f;

        public static readonly int TimeId = Shader.PropertyToID("_SkyTime"), FadeId = Shader.PropertyToID("_SkyFade"),
            FieldId = Shader.PropertyToID("_SkyField"), EpochId = Shader.PropertyToID("_SkyEpoch"), HeadId = Shader.PropertyToID("_SkyHead");

        struct Vert
        {
            public Vector3 Pos;
            public Color32 Color;
            public Vector4 Uv0, Uv1, Uv2;
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
        };

        const int Max = SongSkyModel.Max, LineBase = 4, StarBase = 4 + Max * 4, VertCount = 4 + Max * 8;
        const float Never = 1e9f;

        readonly App _app;
        readonly RoomScene _scene;
        readonly ListenBackPulse _pulse;
        readonly Action<PlaybackNoteEvent> _onNote;
        readonly Action<RecordingPlaybackEvent> _onPlayback;
        readonly Action<RecorderStateEvent> _onState;
        readonly GameObject _go;
        readonly Mesh _mesh;
        readonly MeshRenderer _renderer;
        readonly Material _material;
        NativeArray<Vert> _verts;
        Mulberry32 _rng;
        SongStars.Range _range = SongStars.Range.Default;
        SongStars.Types _types;
        ILayoutService _layout;
        IPianoApi _piano;
        IDrumsApi _drums;
        IHarpApi _harp;
        IRecorder _recorder;
        ILooper _looper;
        IGuide _guide;
        bool _roomVisible = true, _tipDone, _pendingStart;
        float _tipAskAt, _fieldLevel, _fieldTarget;
        int _frameSeen = -1;

        public SongSkyModel Model { get; } = new SongSkyModel();
        public MeshRenderer Renderer => _renderer;
        public Material Material => _material;
        public Mesh Mesh => _mesh;
        /// <summary>Sunny was asked and is saying it (or had said it before).</summary>
        public bool TipAsked => _tipDone;
        /// <summary>The night field's opacity now (0 in mixed reality and at rest).</summary>
        public float FieldLevel => _fieldLevel;

        public SongSky(App app, RoomScene scene, ListenBackPulse pulse, Material material, uint seed)
        {
            _app = app;
            _scene = scene;
            _pulse = pulse;
            _rng = new Mulberry32(seed);
            _onNote = OnNote;
            _onPlayback = OnPlayback;
            _onState = OnState;
            app.Events.Subscribe(_onNote);
            app.Events.Subscribe(_onPlayback);
            app.Events.Subscribe(_onState);
            if (app.Headless || material == null) return;
            _material = material;
            _go = new GameObject("SongSky");
            _go.transform.SetParent(app.WorldRoot, false);
            _mesh = new Mesh { name = "SongSky" };
            _mesh.MarkDynamic();
            _mesh.SetVertexBufferParams(VertCount, Layout);
            _verts = new NativeArray<Vert>(VertCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var none = new Vert { Uv1 = new Vector4(0f, 0f, 0f, -Never), Uv2 = new Vector4(-Never, 0f, 0f, 0f) };
            for (int i = 0; i < VertCount; i++) _verts[i] = none;
            // field, then the lines, then the stars: drawn in this order in the one draw
            int ni = 6 * (1 + 2 * Max);
            var idx = new ushort[ni];
            for (int q = 0; q < 1 + 2 * Max; q++)
            {
                int v = q * 4, i = q * 6;
                idx[i] = (ushort)v; idx[i + 1] = (ushort)(v + 1); idx[i + 2] = (ushort)(v + 2);
                idx[i + 3] = (ushort)v; idx[i + 4] = (ushort)(v + 2); idx[i + 5] = (ushort)(v + 3);
            }
            _mesh.SetVertexBufferData(_verts, 0, 0, VertCount, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.SetIndexBufferParams(ni, IndexFormat.UInt16);
            _mesh.SetIndexBufferData(idx, 0, 0, ni, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.subMeshCount = 1;
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, ni) { bounds = new Bounds(Vector3.zero, Vector3.one * 20f) }, MeshUpdateFlags.DontRecalculateBounds);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 20f);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.enabled = false;
            material.SetFloat(Shader.PropertyToID("_HeadClear"), SongSkyModel.HeadClear);
            material.SetFloat(Shader.PropertyToID("_Rise"), SongSkyModel.Rise);
            material.SetFloat(Shader.PropertyToID("_DieFade"), SongSkyModel.DieFade);
            material.SetFloat(Shader.PropertyToID("_LineIn"), SongSkyModel.LineIn);
        }

        // ---------------------------------------------------------------- the playback

        void OnPlayback(RecordingPlaybackEvent e)
        {
            if (!e.Started) { Model.End(); return; }
            BeginSky(e.Loop, _app.AudioTime, takeRange: !e.Loop);
        }

        void OnState(RecorderStateEvent e)
        {
            if (e.State == RecorderState.Idle) Model.End();
        }

        /// <summary>The sky is framed from the head now; the song's length and pitch range from the recorder.</summary>
        void BeginSky(bool loop, double audioStart, bool takeRange)
        {
            if (_recorder == null) _recorder = _app.Get<IRecorder>();
            float seconds = 0f;
            _range = SongStars.Range.Default;
            if (!loop && _recorder != null)
            {
                seconds = (float)_recorder.TakeSeconds;
                if (takeRange && _recorder.Take != null)
                {
                    if (_types == null) _types = new SongStars.Types(_app.Registry);
                    _range = SongStars.RangeOf(_recorder.Take, _types);   // once per playback: a read of the take
                }
            }
            else
            {
                if (_looper == null) _looper = _app.Get<ILooper>();
                if (_looper != null && _looper.Bpm > 0) seconds = _looper.Bars * 4f * 60f / _looper.Bpm;
            }
            Model.Start(FrameNow(_roomVisible), audioStart, seconds, loop);
            _fieldTarget = _roomVisible ? 1f : 0f;
        }

        SongSkyModel.Frame FrameNow(bool room)
        {
            Transform head = _app.Head;
            var f = new SongSkyModel.Frame
            {
                Head = head != null ? head.position : new Vector3(0f, 1.2f, 0f),
                Forward = head != null ? FlatForward(head) : Vector3.forward,
                Room = room,
                Floor = _app.WorldRoot != null ? _app.WorldRoot.position.y : 0f,
            };
            if (room)
            {
                if (_scene != null && _scene.Static.TryGetValue(RoomMat.Ceiling, out Renderer ceiling) && ceiling != null) f.Ceiling = ceiling.bounds;
                else f.Ceiling = GrabConstants.RoomBox;
                if (f.Ceiling.size.x < 0.5f || f.Ceiling.size.z < 0.5f) f.Ceiling = GrabConstants.RoomBox;
            }
            return f;
        }

        static Vector3 FlatForward(Transform head)
        {
            Vector3 f = head.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 1e-4f) return f;
            f = -head.up;   // looking straight up: the top of the head points back
            f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? f : Vector3.forward;
        }

        void OnNote(PlaybackNoteEvent e)
        {
            if (e.Role != EventRole.Onset) return;
            double now = _app.AudioTime;
            double at = e.When > 0 ? e.When : now;
            // a loop played live (no playback of a recording announced): its sky starts with its first note
            if (!Model.HasFrame || Model.Resting) BeginSky(e.Lane != PlaybackLanes.Take, at, takeRange: e.Lane == PlaybackLanes.Take);
            if (!SongStars.TryHeight(e.Kind, e.Event, _range, out float height)) height = 0.5f;
            Vector3 to = Model.Place(Model.SongFraction(at), height);
            Vector3 from = From(e.Kind, e.Event, height, to);
            Model.Note(e.Kind, from, at, height, e.Level, (float)(at - now));   // Sunny is asked in Tick
        }

        /// <summary>Where a note's star starts: just above the part that played it.</summary>
        Vector3 From(InstrumentKind kind, in TakeEvent e, float height, Vector3 to)
        {
            if (_layout == null) _layout = _app.Get<ILayoutService>();
            if (_layout != null && _layout.PlaceOf(kind) == InstrumentPlace.Hidden) return to - Vector3.up * 0.3f;
            Vector3 c = default, ext = default;
            if (_pulse == null || !_pulse.TryPlace(kind, out c, out ext)) return to - Vector3.up * 0.3f;
            float top = c.y + ext.y + 0.03f;
            switch (kind)
            {
                case InstrumentKind.Piano:
                    if (_piano == null) _piano = _app.Get<IPianoApi>();
                    if (_piano != null && _piano.Root != null && _piano.TryGetKey(e.midi, out PianoKeyInfo key))
                    {
                        Transform r = _piano.Root;
                        Vector3 k = r.TransformPoint(new Vector3(key.X, 0f, 0f));
                        Vector3 right = r.right;
                        return new Vector3(c.x, top, c.z) + right * Vector3.Dot(k - c, right);
                    }
                    break;
                case InstrumentKind.Drums:
                    if (_drums == null) _drums = _app.Get<IDrumsApi>();
                    if (_drums != null && _drums.Pieces != null && e.kind != null)
                    {
                        IReadOnlyList<DrumPieceInfo> pieces = _drums.Pieces;
                        for (int i = 0; i < pieces.Count; i++)
                        {
                            DrumPieceInfo p = pieces[i];
                            if (p.Pad || p.Transform == null || !string.Equals(p.Kind, e.kind, StringComparison.Ordinal)) continue;
                            return p.Transform.position + Vector3.up * 0.05f;
                        }
                    }
                    break;
                case InstrumentKind.Harp:
                    if (_harp == null) _harp = _app.Get<IHarpApi>();
                    if (_harp != null && _harp.Root != null && e.i >= 0 && e.i < _harp.StringCount)
                        return _harp.Root.TransformPoint(_harp.PlayPoint(e.i));
                    break;
            }
            // anywhere over the played parts; a keyboard of bars from low (left) to high (right)
            float x = kind == InstrumentKind.Marimba ? (height * 2f - 1f) * 0.85f : Rand(-0.7f, 0.7f);
            return new Vector3(c.x + ext.x * x, top, c.z + ext.z * Rand(-0.6f, 0.6f));
        }

        float Rand(float a, float b) => a + (b - a) * (float)_rng.Next();

        // ---------------------------------------------------------------- every frame

        /// <summary>Room's Decor phase (also while the room is hidden: in mixed reality the stars float over the instruments).</summary>
        public void Tick(float dt, bool roomVisible)
        {
            _roomVisible = roomVisible;
            Model.Tick(dt);
            AskSunny();
            if (Model.Resting) _fieldTarget = 0f;
            float k = dt > 0f ? Mathf.Clamp01(dt / FieldIn) : 0f;
            _fieldLevel = Mathf.MoveTowards(_fieldLevel, roomVisible && Model.Current.Room ? _fieldTarget : 0f, k);
            if (Model.Resting && _fieldLevel <= 0f) _fieldLevel = 0f;
            Draw();
        }

        void Draw()
        {
            if (_mesh == null) { Model.ClearDirty(); return; }
            bool on = !Model.Resting;
            if (_renderer.enabled != on) _renderer.enabled = on;
            if (Model.FrameVersion != _frameSeen)
            {
                _frameSeen = Model.FrameVersion;
                WriteField();
            }
            int n = Model.DirtyCount;
            if (n > 0)
            {
                Matrix4x4 toLocal = _go.transform.worldToLocalMatrix;
                for (int i = 0; i < n; i++) WriteStar(Model.Dirty(i), toLocal);
                Model.ClearDirty();
            }
            if (!on) return;
            Transform head = _app.Head;
            Vector3 h = head != null ? head.position : Model.Current.Head;
            h = _go.transform.InverseTransformPoint(h);
            _material.SetFloat(TimeId, Model.Time);
            _material.SetFloat(FadeId, Model.Fade);
            _material.SetFloat(FieldId, _fieldLevel * FieldAlpha);
            _material.SetFloat(EpochId, Model.Epoch);
            _material.SetVector(HeadId, new Vector4(h.x, h.y, h.z, 1f));
        }

        void WriteStar(int slot, Matrix4x4 toLocal)
        {
            ref SongSkyModel.Star st = ref Model.Stars[slot];
            Color c = SongStars.ColorOf(st.Kind);
            var col = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(Mathf.Clamp01(st.Bright) * 255f));
            Vector3 to = toLocal.MultiplyPoint3x4(st.To), from = toLocal.MultiplyPoint3x4(st.From);
            int v = StarBase + slot * 4;
            for (int q = 0; q < 4; q++)
            {
                float cx = q == 0 || q == 3 ? -1f : 1f, cy = q < 2 ? -1f : 1f;
                _verts[v + q] = new Vert
                {
                    Pos = to, Color = col,
                    Uv0 = new Vector4(cx, cy, st.Size, st.Phase),
                    Uv1 = new Vector4(from.x, from.y, from.z, st.Birth),
                    Uv2 = new Vector4(Finite(st.Death), 0f, 0f, 0f),
                };
            }
            _mesh.SetVertexBufferData(_verts, v, v, 4, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            // its line from the star before it of the same instrument (appears once it has settled)
            int lv = LineBase + slot * 4;
            bool line = st.Prev >= 0 && st.LineDeath > -Never;
            Vector3 a = line ? toLocal.MultiplyPoint3x4(Model.Stars[st.Prev].To) : to;
            float appear = line ? st.Birth + SongSkyModel.Rise : Never;
            var lcol = new Color32(col.r, col.g, col.b, 255);
            for (int q = 0; q < 4; q++)
            {
                float side = q == 0 || q == 3 ? -1f : 1f, along = q < 2 ? 0f : 1f;
                _verts[lv + q] = new Vert
                {
                    Pos = a, Color = lcol,
                    Uv0 = new Vector4(side, along, LineWidth, 0f),
                    Uv1 = new Vector4(to.x, to.y, to.z, appear),
                    Uv2 = new Vector4(line ? Finite(st.LineDeath) : -Never, 1f, st.Birth, 0f),
                };
            }
            _mesh.SetVertexBufferData(_verts, lv, lv, 4, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }

        static float Finite(float t) => t > Never ? Never : t < -Never ? -Never : t;

        /// <summary>The night field: one quad just under the ceiling over the sky's arc (VR), or nothing.</summary>
        void WriteField()
        {
            SongSkyModel.Frame f = Model.Current;
            bool field = Model.HasFrame && f.Room;
            Matrix4x4 toLocal = _go.transform.worldToLocalMatrix;
            Vector3 fw = f.Forward.sqrMagnitude > 1e-6f ? f.Forward : Vector3.forward;
            Vector3 right = new Vector3(fw.z, 0f, -fw.x);
            Vector3 centre = f.Head + fw * (SongSkyModel.VrDistance * 0.55f);
            Bounds ceil = f.Ceiling;
            if (field)
            {
                centre.x = Mathf.Clamp(centre.x, ceil.min.x + 0.5f, ceil.max.x - 0.5f);
                centre.z = Mathf.Clamp(centre.z, ceil.min.z + 0.5f, ceil.max.z - 0.5f);
                centre.y = ceil.max.y - 0.03f;
            }
            float hx = SongSkyModel.VrDistance + 0.5f, hz = SongSkyModel.VrDistance * 0.8f + 0.4f;
            Color fc = FieldColor;
            var col = new Color32((byte)(fc.r * 255f), (byte)(fc.g * 255f), (byte)(fc.b * 255f), 255);
            for (int q = 0; q < 4; q++)
            {
                float cx = q == 0 || q == 3 ? -1f : 1f, cy = q < 2 ? -1f : 1f;
                Vector3 p = centre + right * (cx * hx) + fw * (cy * hz);
                if (field)
                {
                    p.x = Mathf.Clamp(p.x, ceil.min.x, ceil.max.x);
                    p.z = Mathf.Clamp(p.z, ceil.min.z, ceil.max.z);
                }
                _verts[q] = new Vert
                {
                    Pos = toLocal.MultiplyPoint3x4(p), Color = col,
                    Uv0 = new Vector4(cx, cy, 0f, 0f),
                    Uv1 = new Vector4(0f, 0f, 0f, field ? Model.Time : -Never),
                    Uv2 = new Vector4(Never, 2f, 0f, 0f),   // kind 2: the field (hidden by its birth when there is none)
                };
            }
            _mesh.SetVertexBufferData(_verts, 0, 0, 4, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            // bounds: everything within reach of the frame's head
            Vector3 hc = toLocal.MultiplyPoint3x4(f.Head);
            _mesh.bounds = new Bounds(hc, new Vector3(12f, 8f, 12f));
        }

        /// <summary>The first sky of all: Sunny says to look up (once ever; a busy Sunny is asked again).</summary>
        void AskSunny()
        {
            if (_tipDone || Model.Made < TipStars || Model.State != SongSkyModel.Phase.Playing || Model.Time < _tipAskAt) return;
            _tipAskAt = Model.Time + TipEvery;
            if (_guide == null) _guide = _app.Get<IGuide>();
            if (_guide == null) return;
            // its turn among the show's lines (PlaybackTips: after the room's own "Sit back and watch...", one per playback)
            if (PlaybackTips.Ask(_guide, TipId)) _tipDone = true;
        }

        // ---------------------------------------------------------------- tests

        /// <summary>A star's world position as drawn now (the shader's maths; tests). False when it is not drawn.</summary>
        public bool StarAt(int slot, out Vector3 world)
        {
            world = default;
            if ((uint)slot >= Max || !Model.Shown(slot, Model.Time)) return false;
            Transform head = _app.Head;
            world = SongSkyModel.PositionAt(Model.Stars[slot], Model.Time, head != null ? head.position : Model.Current.Head);
            return true;
        }

        public void Destroy()
        {
            _app.Events.Unsubscribe(_onNote);
            _app.Events.Unsubscribe(_onPlayback);
            _app.Events.Unsubscribe(_onState);
            if (_verts.IsCreated) _verts.Dispose();
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_material != null) _app.Materials.Destroy(_material);
        }
    }
}
