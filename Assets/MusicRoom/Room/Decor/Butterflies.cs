// The butterflies (2026-11-11: "bring all the butterflies and flowers in the room"): while a recording plays back (a take
// or a loop) they come and flutter around the instruments that play it, land on the room's flowers between phrases and
// drift away a few seconds after the music stops (ButterflyFlock.cs: the behaviour, pure). Never while recording (the same
// day's headset test: "butterfly should not appear while recording. it will appear while playing the recordings"): they
// read the pulse's PlaybackModel, the played-back notes only; an overdub over a looping playback brings them for the loop's
// notes, not for the overdub's. This draws them and feeds the flock:
//   - ONE mesh, ONE draw (only while any is out: the renderer is off otherwise): 12 butterflies x 2 wings, each wing a
//     quad hinged on the body, its four corners written on the CPU every frame (8 vertices a butterfly: position, normal,
//     colour, uv; one NativeArray, SetVertexBufferData, no allocation) from the flock's pose and wing angle; the wing
//     atlas (Room/butterfly_wings, ButterflyArt.cs) holds 8 designs, a wing per tile, mirrored for the other side;
//   - MusicRoom/Butterfly: alpha-blended, both faces, lit per vertex (ambient + the main light + the room's point lights,
//     as the harp's strings), plus the vertex colour's glow (the playback glow's orange, brighter with louder notes) and the
//     fade in its alpha. Single-pass instanced / multiview safe (instance id in, stereo output initialised). No light, no
//     shadow, no new texture at runtime once baked;
//   - the flock's inputs from the playback model (the loudest onset this frame, the room level, a held lane), the
//     recorder's state (idle = the music stopped), the instruments that sound (ListenBackPulse.TryPlace: their played parts'
//     box), the head, every active fingertip and stick tip (App.Input.AllPointers), the flower heads (RoomScene.FlowerPerches,
//     VR only) and the room box.
// Under World (not the room root): in mixed reality, where the room is hidden, they still come, around the instruments.
// Main thread, Room's Decor phase after the pulse. Allocation-free per frame.
using System;
using MusicRoom.Core;
using MusicRoom.Room.Textures;
using MusicRoom.Room.World;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room.Decor
{
    public sealed class Butterflies
    {
        public const int VertsPer = 8, IndicesPer = 12;
        /// <summary>The glow a butterfly carries at full (vertex colour x its wing colour): the playback glow's orange, a
        /// little white.</summary>
        public static readonly Color GlowColor = new Color(0.62f, 0.36f, 0.12f);

        struct Vert
        {
            public Vector3 Pos;
            public Vector3 Normal;
            public Color32 Color;
            public Vector2 Uv;
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };

        readonly App _app;
        readonly ListenBackPulse _pulse;
        readonly RoomScene _scene;
        readonly Action<RecorderStateEvent> _onState;
        readonly GameObject _go;
        readonly Mesh _mesh;
        readonly MeshRenderer _renderer;
        NativeArray<Vert> _verts;
        bool _stopped = true;
        float _time;
        readonly Vector2[] _uv = new Vector2[ButterflyArt.Designs * 4];

        public ButterflyFlock Flock { get; }
        public MeshRenderer Renderer => _renderer;
        public Mesh Mesh => _mesh;
        /// <summary>The flock's room (GrabConstants.RoomBox): they stay inside it while the VR room shows.</summary>
        public Bounds RoomBox = GrabConstants.RoomBox;

        public Butterflies(App app, ListenBackPulse pulse, RoomScene scene, Material material, uint seed)
        {
            _app = app;
            _pulse = pulse;
            _scene = scene;
            Flock = new ButterflyFlock(seed);
            _onState = e => _stopped = e.State == RecorderState.Idle;
            app.Events.Subscribe(_onState);
            for (int d = 0; d < ButterflyArt.Designs; d++)
            {
                _uv[d * 4 + 0] = ButterflyArt.Uv(d, 0f, 0f);   // hinge, rear
                _uv[d * 4 + 1] = ButterflyArt.Uv(d, 0f, 1f);   // hinge, front
                _uv[d * 4 + 2] = ButterflyArt.Uv(d, 1f, 1f);   // tip, front
                _uv[d * 4 + 3] = ButterflyArt.Uv(d, 1f, 0f);   // tip, rear
            }
            if (app.Headless || material == null) return;
            _go = new GameObject("Butterflies");
            _go.transform.SetParent(app.WorldRoot, false);
            _mesh = new Mesh { name = "Butterflies" };
            _mesh.MarkDynamic();
            int nv = ButterflyFlock.Max * VertsPer, ni = ButterflyFlock.Max * IndicesPer;
            _mesh.SetVertexBufferParams(nv, Layout);
            _verts = new NativeArray<Vert>(nv, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var idx = new ushort[ni];
            for (int b = 0; b < ButterflyFlock.Max; b++)
                for (int w = 0; w < 2; w++)
                {
                    int v = b * VertsPer + w * 4, i = b * IndicesPer + w * 6;
                    idx[i] = (ushort)v; idx[i + 1] = (ushort)(v + 1); idx[i + 2] = (ushort)(v + 2);
                    idx[i + 3] = (ushort)v; idx[i + 4] = (ushort)(v + 2); idx[i + 5] = (ushort)(v + 3);
                }
            // the vertices first (all at the origin), then the indices and the one sub-mesh, its bounds computed from them
            _mesh.SetVertexBufferData(_verts, 0, 0, nv, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.SetIndexBufferParams(ni, IndexFormat.UInt16);
            _mesh.SetIndexBufferData(idx, 0, 0, ni, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.subMeshCount = 1;
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, ni) { bounds = new Bounds(Vector3.zero, Vector3.one) }, MeshUpdateFlags.DontRecalculateBounds);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.enabled = false;
        }

        /// <summary>Room's Decor phase, after the pulse (also while the room is hidden: they come in mixed reality too).</summary>
        public void Tick(float dt, bool roomVisible)
        {
            _time += dt;
            ListenBackModel m = _pulse.PlaybackModel;   // played-back notes only: never while recording
            ButterflyFlock flock = Flock;
            flock.BeginFrame();
            float onset = 0f;
            for (int k = 0; k < ListenBackModel.Kinds; k++)
            {
                var kind = (InstrumentKind)k;
                float o = m.Onset(kind), lv = m.Level(kind);
                if (o > onset) onset = o;
                if ((lv > 0.02f || o > 0f) && _pulse.TryPlace(kind, out Vector3 c, out Vector3 e)) flock.AddAttractor(c, e, Mathf.Max(lv, o));
            }
            var pointers = _app.Input?.AllPointers;
            if (pointers != null)
                for (int i = 0; i < pointers.Count; i++)
                {
                    Pointer p = pointers[i];
                    if (p != null && p.Active) flock.AddAvoid(p.Tip);
                }
            if (roomVisible && _scene != null) flock.SetPerches(_scene.FlowerPerches, _scene.FlowerPerches.Count);
            else flock.SetPerches(_scene != null ? _scene.FlowerPerches : null, 0);
            Transform head = _app.Head;
            var frame = new FlockFrame
            {
                Dt = dt, Time = _time,
                Head = head != null ? head.position : new Vector3(0f, 1.2f, 0f),
                HeadForward = head != null ? head.forward : Vector3.forward,
                Onset = onset, Holding = m.Holding, Level = m.Room, Stopped = _stopped, InRoom = roomVisible, RoomBox = RoomBox,
            };
            flock.Tick(frame);
            Draw();
        }

        void Draw()
        {
            if (_mesh == null) return;
            ButterflyFlock flock = Flock;
            bool any = flock.Alive > 0;
            if (_renderer.enabled != any) _renderer.enabled = any;
            if (!any) return;
            Matrix4x4 toLocal = _go.transform.worldToLocalMatrix;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int i = 0; i < ButterflyFlock.Max; i++)
            {
                ref readonly Butterfly b = ref flock.Get(i);
                int v0 = i * VertsPer;
                if (b.Mode == ButterflyMode.Off || b.Alpha <= 0.002f)
                {
                    var none = new Vert();
                    for (int k = 0; k < VertsPer; k++) _verts[v0 + k] = none;
                    continue;
                }
                Vector3 up = Vector3.up;
                Vector3 h = b.Heading.sqrMagnitude > 1e-6f ? b.Heading : Vector3.forward;
                float cp = Mathf.Cos(b.Pitch), sp = Mathf.Sin(b.Pitch);
                Vector3 fwd = h * cp + up * sp;
                Vector3 right0 = Vector3.Cross(up, fwd).normalized;
                Vector3 up0 = Vector3.Cross(fwd, right0);
                float cb = Mathf.Cos(b.Bank), sb = Mathf.Sin(b.Bank);
                Vector3 right = right0 * cb + up0 * sb;
                Vector3 upB = Vector3.Cross(fwd, right);
                // the body bobs a few millimetres with each beat (in flight)
                Vector3 p = b.Pos + (b.Mode == ButterflyMode.Perched ? upB * 0.004f : upB * (0.005f * Mathf.Sin(b.Phase)));
                float L = b.Size, C = b.Size * 1.05f;
                float cw = Mathf.Cos(b.Wing), sw = Mathf.Sin(b.Wing);
                Color g = GlowColor * b.Glow;
                var col = new Color32((byte)(Mathf.Clamp01(g.r) * 255f), (byte)(Mathf.Clamp01(g.g) * 255f), (byte)(Mathf.Clamp01(g.b) * 255f), (byte)(Mathf.Clamp01(b.Alpha) * 255f));
                Vector3 rear = p - fwd * (0.45f * C), front = p + fwd * (0.55f * C);
                int d = (b.Design & 7) * 4;
                for (int w = 0; w < 2; w++)
                {
                    float s = w == 0 ? -1f : 1f;
                    Vector3 span = right * (s * cw) + upB * sw;
                    Vector3 n = Vector3.Cross(span, fwd).normalized;
                    Vector3 a = rear + right * (s * 0.0015f), bf = front + right * (s * 0.0015f);
                    Vector3 tipF = bf + span * L, tipR = a + span * L;
                    int v = v0 + w * 4;
                    _verts[v + 0] = new Vert { Pos = toLocal.MultiplyPoint3x4(a), Normal = n, Color = col, Uv = _uv[d + 0] };
                    _verts[v + 1] = new Vert { Pos = toLocal.MultiplyPoint3x4(bf), Normal = n, Color = col, Uv = _uv[d + 1] };
                    _verts[v + 2] = new Vert { Pos = toLocal.MultiplyPoint3x4(tipF), Normal = n, Color = col, Uv = _uv[d + 2] };
                    _verts[v + 3] = new Vert { Pos = toLocal.MultiplyPoint3x4(tipR), Normal = n, Color = col, Uv = _uv[d + 3] };
                }
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            _mesh.SetVertexBufferData(_verts, 0, 0, _verts.Length, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            if (min.x <= max.x)
            {
                var bounds = new Bounds((min + max) * 0.5f, max - min + new Vector3(0.2f, 0.2f, 0.2f));
                _mesh.bounds = new Bounds(toLocal.MultiplyPoint3x4(bounds.center), bounds.size);
            }
        }

        /// <summary>World corners of a butterfly's wings as drawn (tests: never in the head's sphere). False when it is out.</summary>
        public bool WingCorners(int i, Vector3[] into8)
        {
            if (!_verts.IsCreated || i < 0 || i >= ButterflyFlock.Max) return false;
            ref readonly Butterfly b = ref Flock.Get(i);
            if (b.Mode == ButterflyMode.Off || b.Alpha <= 0.002f) return false;
            Matrix4x4 toWorld = _go.transform.localToWorldMatrix;
            for (int k = 0; k < VertsPer; k++) into8[k] = toWorld.MultiplyPoint3x4(_verts[i * VertsPer + k].Pos);
            return true;
        }

        public void Destroy()
        {
            _app.Events.Unsubscribe(_onState);
            if (_verts.IsCreated) _verts.Dispose();
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }
    }
}
