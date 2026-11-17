// The notes paint the walls (the playback show, 2026-11-11: "CANT WE DO SOME THING CREATIVE WHEN WE PLAY THE recordings?";
// round 2, 2026-11-13: "its nice. but its not that creative in paint. can we do something creative for fun??"; round 4,
// 2026-11-16: "make paintings of flowers birds nature scenery. or may be paint few nature sceneires").
// While a recording or a loop plays back (Lessons' RecordingPlaybackEvent, Started .. its end) every played-back onset
// (PlaybackNoteEvent: never a live note, never while recording) paints a piece of a NATURE SCENE in watercolour, each
// instrument its own motif in its own pigment (Decor/PaintBrushes.cs): the piano's melody grows a flowering vine (a green
// stem rising and dipping with the pitch, a flower blooming off it for every melody note, chord notes hanging buds), the
// drums plant the ground (a kick an autumn tree, the snare grass and pebbles, the toms leaves drifting down the wall),
// the harp sends birds swooping in a flock along an arc, the bowls swell soft clouds, the handpan lets butterflies flutter,
// the hand drums grow mushrooms and the marimba slim trees. Colour per instrument shaded by pitch (PaintPalette). The song
// moves along the walls as it plays; where a motif belongs sets its height (PaintBrushes.Height: the ground low, the vine
// by the melody's pitch, birds and clouds high), and a hash of the note keeps the same take painting the same picture
// (WallCanvas: clear of windows, doors, the fireplace, the shelf, the paintings, the plants, the gallery's frames and every
// other thing at a wall; never on the ceiling, which is the night sky's).
// Throwing paint: while it paints (and while the painting holds after), a quick forward flick of an open hand
// (Decor/PaintThrow.cs, never while the hand plays an instrument, holds a stick or pinches) throws a blob of paint in an arc
// to where the throw points on a wall: it bursts into a flower with petals flying round it and a few leaves, some petals
// drifting down, in the colour of the instrument playing (or played last), a soft splat sound. At most ThrowsPerSecond.
// When the playback ends the painting stays HoldSeconds; then, if the recording is a saved one (Decor/PaintGallery.cs), the
// splashes swirl off the walls into a gold frame (GatherSeconds: copies of them, a second renderer of the same mesh) and the
// song hangs there as a framed picture; the walls fade away over FadeSeconds and are exactly as they were (every splash
// gone, the renderers off). "Keep painting" (AppSettings.KeyKeepPaintings, the menu's row on a recording's page) keeps the
// wall painting as well, until the next playback, which washes it away over ClearSeconds as it starts its own; the framed
// picture goes up either way.
//   - VR: the virtual room's walls, read once from the built room (the wall mesh's own triangles: the window opening stays
//     a hole; every other mesh of the room blocks what it stands in front of), in the room root's space, so the paint
//     follows the room wherever it is turned or moved. The mesh lives under the room root: hidden with it.
//   - Mixed reality: the player's real walls (Core's IRealWalls, from MR): the scene model's walls, windows, doors and
//     furniture when the room was set up, else the depth sensor's walls found along a fan of rays from the head while the
//     playback paints (one ray a frame); neither: nothing is painted (never in the air). A mesh of its own under World.
//   - Drawn as ONE dynamic mesh per place (MaxQuads quads, a ring: the oldest gives way to the newest), shader
//     MusicRoom/WallPaint (template Room/WallPaint), alpha-blended (layers build toward the pigment, never to black): in VR
//     the paint is lit per vertex as the wall under it, in MR a translucent wash with its alpha for the passthrough. How a
//     motif comes alive (a flower opening, a leaf drifting down, the vine growing) is in its vertices (the GPU animates it); the two
//     paintings' fades are two uniforms: the CPU writes 4 vertices per new quad only. No light, no shadow, no texture at
//     runtime but the 512 px brush atlas made once (Textures/PaintArt.cs, on a worker).
// Sunny (PlaybackTips' order, each once ever, asked again while Sunny is busy): tip_paint_walls once the first painting has
// shown TipAfter; tip_paint_throw while a later playback paints; tip_paint_gallery when a painting is framed;
// tip_paint_keep once a painting is about to fade with Keep painting off.
// Main thread: notes come in the Recorder phase, Tick runs in Room's Decor phase. Allocation-free per frame after setup.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Room.Geometry;
using MusicRoom.Room.Render;
using MusicRoom.Room.Textures;
using MusicRoom.Room.World;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room.Decor
{
    public sealed class WallPaint
    {
        public enum Phase { Idle = 0, Painting, Holding, Fading, Kept }

        /// <summary>Brush quads per place (VR room / mixed reality).</summary>
        public const int MaxQuads = 2400;
        /// <summary>After the playback: shown HoldSeconds, then gone over FadeSeconds; a painting kept is washed away over
        /// ClearSeconds when the next playback starts.</summary>
        public const float HoldSeconds = 7f, FadeSeconds = 3.2f, ClearSeconds = 1.2f;
        /// <summary>The swirl into the gallery's frame (s) and when the picture shows in it (0..1 of the swirl).</summary>
        public const float GatherSeconds = 2.8f, RevealAt = 0.8f;
        /// <summary>How far off the wall the paint lies (m).</summary>
        public const float Lift = 0.003f;
        /// <summary>At most this many marks a second (a drum roll stays readable), BurstMax at once; a ribbon's or an arc's
        /// next piece costs RibbonCost of one.</summary>
        public const float NotesPerSecond = 10f, BurstMax = 5f, RibbonCost = 0.25f;
        /// <summary>Nothing is painted within this of the head (m) (ButterflyFlock.HeadClear's sphere).</summary>
        public const float HeadClear = 0.35f;
        /// <summary>The song starts this far (m) before the wall point straight ahead, so it moves across the view.</summary>
        public const float StartBefore = 0.9f;
        /// <summary>The vine grows on along the walls this fast (m/s), as one line: each phrase starts a hand's breadth after
        /// the last one ended (PhraseSpace, m), or, when the song has moved on further, this far before where it is now
        /// (RibbonBefore, m); a stroke is at most PhraseMax long (m). Its height eases toward each note's (Ease).
        /// where the song is (m).</summary>
        public const float RibbonSpeed = 0.4f, PhraseMax = 2.4f, RibbonBefore = 0.5f, PhraseSpace = 0.14f, Ease = 0.6f;
        public const int PendingMax = 64;
        /// <summary>Sunny's lines (Guide's TourTips, asked for here).</summary>
        public const string TipId = "tip_paint_walls", KeepTipId = "tip_paint_keep", ThrowTipId = "tip_paint_throw", GalleryTipId = "tip_paint_gallery";
        public const float TipAfter = 2f, TipEvery = 4f, ThrowTipAfter = 4f;
        /// <summary>Throwing: at most this many a second; the blob flies this fast (m/s) with an arc, lands this big (m).</summary>
        public const float ThrowsPerSecond = 4f, BlobSpeed = 5.5f, BlobArc = 0.12f, SplatSize = 0.17f, BlobRadius = 0.028f, ThrowReach = 8f;
        public const int Blobs = 4;
        /// <summary>Mixed reality without a scene model: the depth rays' fan about the look direction (degrees) and how long a
        /// found wall point is trusted (s).</summary>
        public static readonly float[] DepthYaw = { -50f, -30f, -12f, 0f, 12f, 30f, 50f }, DepthPitch = { -4f, 10f, 22f };
        public const float DepthKeep = 20f, DepthMin = 0.6f, DepthMax = 6f;
        public const int DepthHits = 21;

        internal struct Vert
        {
            public Vector3 Pos;
            public Vector3 Normal;
            public Color32 Color;
            public Vector4 Uv;      // u, v in the cell, cell, birth
            public Vector4 Anim;    // painting, mode, p0, p1
            public Vector4 Anim2;   // p2, p3, wipe, random
            public Vector4 Centre;  // the quad's centre (the gather)
        }

        static readonly VertexAttributeDescriptor[] VertexLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
        };

        const byte Empty = 255;

        /// <summary>One place's quads: its ring of slots (which painting each belongs to), its mesh and material, and the
        /// gather's renderer (the same mesh, the copies flying into a frame).</summary>
        public sealed class Layer
        {
            public readonly bool Room;
            public readonly byte[] Gen = new byte[MaxQuads];
            public readonly Vector3[] Centre = new Vector3[MaxQuads];   // the canvas's space (room-local / world)
            public readonly float[] Radius = new float[MaxQuads];
            /// <summary>Each slot's brush cell (PaintArt: which mark it is; tests, the close-ups).</summary>
            public readonly byte[] Cell = new byte[MaxQuads];
            public readonly int[] Alive = new int[2];
            public int Next;
            internal GameObject Go, GatherGo;
            internal Mesh Mesh;
            internal MeshRenderer Renderer, GatherRenderer;
            internal Material Material, GatherMaterial;
            internal NativeArray<Vert> Verts;
            internal int DirtyMin = int.MaxValue, DirtyMax = -1;
            internal Vector3 BoundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), BoundsMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            float _shownTime = -1f, _shown0 = -1f, _shown1 = -1f;

            public Layer(bool room) { Room = room; for (int i = 0; i < MaxQuads; i++) Gen[i] = Empty; }

            public int Count => Alive[0] + Alive[1];
            public MeshRenderer MeshRenderer => Renderer;
            public MeshRenderer GatherMeshRenderer => GatherRenderer;
            public Mesh SplashMesh => Mesh;
            public Material SplashMaterial => Material;
            public Transform Space => Go != null ? Go.transform : null;

            internal void Uniforms(float time, float f0, float f1)
            {
                if (Material == null) return;
                if (time != _shownTime) { Material.SetFloat(PaintTimeId, time); _shownTime = time; }
                if (f0 != _shown0) { Material.SetFloat(Fade0Id, f0); _shown0 = f0; }
                if (f1 != _shown1) { Material.SetFloat(Fade1Id, f1); _shown1 = f1; }
            }
        }

        struct Pending
        {
            public int Kind, Key;
            public float Pitch, Level;
            public double When;
            public bool Accent;
        }

        struct DepthHit
        {
            public Vector3 Point, Inward;
            public float Yaw, Time;
        }

        /// <summary>A blob of thrown paint in flight.</summary>
        struct Blob
        {
            public bool Active, Splat, Room;
            public float T, Duration;
            public Vector3 From, To;          // world
            public Color Colour;
            public int Wall;                  // the canvas wall (-1: a depth wall)
            public float U, V;
            public Vector3 Origin, Along, Normal;   // the landing frame in the canvas's space (a depth wall's)
            public uint Hash;
            public GameObject Go;
            public Material Material;
        }

        public static readonly int PaintTimeId = Shader.PropertyToID("_PaintTime"), Fade0Id = Shader.PropertyToID("_Fade0"),
            Fade1Id = Shader.PropertyToID("_Fade1"), GatherOnId = Shader.PropertyToID("_GatherOn"), GatherId = Shader.PropertyToID("_Gather"),
            GatherGenId = Shader.PropertyToID("_GatherGen"), GatherCentreId = Shader.PropertyToID("_GatherCentre"),
            GatherRightId = Shader.PropertyToID("_GatherRight"), GatherUpId = Shader.PropertyToID("_GatherUp"), GatherNormalId = Shader.PropertyToID("_GatherNormal");

        readonly App _app;
        readonly RoomScene _scene;
        readonly Action<PlaybackNoteEvent> _onNote;
        readonly Action<RecordingPlaybackEvent> _onPlayback;
        readonly Action<RecorderStateEvent> _onState;
        readonly Action<LayoutChangedEvent> _onLayout;
        readonly Action<ArrangedEvent> _onArranged;
        readonly Action<InstrumentMoveEvent> _onMove;
        readonly Action<MixedRealityEvent> _onMr;
        // what stands at the walls and can move (the instruments on display, a rig moved to a wall): read again after the
        // layout changes and settles, never while a playback's stage has them at the seat
        readonly List<Renderer> _rigRenderers = new List<Renderer>(512);   // every rig's renderers, gathered when the rigs change
        readonly List<int> _rigStart = new List<int>(16);
        readonly List<Rig> _rigCache = new List<Rig>(16);
        int _rigVersion = -1;
        bool _rigsDirty = true;
        float _rigsAt;
        readonly Pending[] _pending = new Pending[PendingMax];
        int _pendingCount;
        readonly DepthHit[] _hits = new DepthHit[DepthHits];
        int _hitCount, _hitNext, _rayIndex;
        Texture2D _atlas;
        Task<Color32[]> _atlasJob;
        float _time, _phaseTime, _budget = BurstMax, _keepPoll;
        int _cur;                      // the painting being painted / shown: its slots carry this generation (0 or 1)
        readonly float[] _fade = { 1f, 1f };
        bool _clearing;                // the other generation is being washed away
        double _startAudio;
        int _noteIndex, _paintedThis;
        bool _s0Room, _s0Mr;
        float _s0RoomAt, _s0MrAt;
        bool _roomVisible = true;
        bool _mrScene, _mrDepth, _mrReady;
        int _mrVersion = -1;
        float _mrGalleryTry;
        Vector3 _mrForward = Vector3.forward;
        // the brushes and their strokes going on across notes
        readonly PaintBrushes _brush = new PaintBrushes();
        int _ribbonWall = -1, _arcWall = -1;
        bool _ribbonRoom, _arcRoom;
        float _phraseS, _phraseT, _lastPianoT = -10f, _lastMelodyT = -10f, _ribbonEndS = float.MinValue, _ribbonV;
        Vector3 _ribbonO, _ribbonA, _ribbonN;   // a depth wall's frame for the ribbon (no canvas)
        InstrumentKind _lastKind = InstrumentKind.Piano;
        float _lastPitch = 0.5f;
        // throwing
        readonly ThrowGesture _gesture = new ThrowGesture();
        readonly Blob[] _blobs = new Blob[Blobs];
        float _lastThrow = -10f;
        Mesh _blobMesh;
        int _splatPreset = -1, _splatRate;
        static readonly NoiseShots.Lane[] SplatLanes =
        {
            new NoiseShots.Lane(Audio.Dsp.BiquadType.Lowpass, 700f, 0.8f, 0.045f, 0.8f),
            new NoiseShots.Lane(Audio.Dsp.BiquadType.Bandpass, 1700f, 1.1f, 0.022f, 0.45f),
            new NoiseShots.Lane(Audio.Dsp.BiquadType.Bandpass, 380f, 0.9f, 0.07f, 0.35f),
            new NoiseShots.Lane(Audio.Dsp.BiquadType.Highpass, 4800f, 0.7f, 0.008f, 0.1f),
        };
        // the gather into the gallery's frame
        float _gatherT = -1f;
        int _gatherGen;
        bool _gatherRoom, _revealed;
        string _gatherId;
        // Sunny
        IGuide _guide;
        bool _tipDone, _keepTipDone, _throwTipDone, _galleryTipDone;
        float _shownFor, _tipAskAt, _keepAskAt, _throwAskAt, _galleryAskAt, _galleryTipUntil;

        public WallCanvas RoomCanvas { get; } = new WallCanvas();
        public WallCanvas MrCanvas { get; } = new WallCanvas();
        public Layer RoomLayer { get; } = new Layer(true);
        public Layer MrLayer { get; } = new Layer(false);
        public PaintGallery Gallery { get; }
        public Phase State { get; private set; } = Phase.Idle;
        /// <summary>Notes painted so far (tests; also in headless boots, where nothing is drawn) and notes that found no wall.</summary>
        public int Painted { get; private set; }
        public int Missed { get; private set; }
        /// <summary>Notes that found no wall, by instrument (the log, tests).</summary>
        public readonly int[] MissedBy = new int[16];
        /// <summary>Throws seen (the gesture) and splats landed on a wall.</summary>
        public int Throws { get; private set; }
        public int Splats { get; private set; }
        public int SplatSounds { get; private set; }
        /// <summary>Times the instruments standing at the walls were read (tests).</summary>
        public int RigReads { get; private set; }
        /// <summary>After a layout change, the instruments are read again once nothing has moved for this long (s).</summary>
        public const float RigsSettle = 0.6f, AfterPlayback = 2.5f;
        public static bool DebugRigs;
        /// <summary>The fade of the painting now shown (1 while painting and holding).</summary>
        public float Fade => _fade[_cur];
        public float FadeOf(int generation) => _fade[generation & 1];
        public int Generation => _cur;
        /// <summary>The quads left on the walls (both places, both paintings).</summary>
        public int Splashes => RoomLayer.Count + MrLayer.Count;
        /// <summary>Tests: the real walls to use instead of the app's (IRealWalls from MR).</summary>
        public IRealWalls RealWallsOverride;
        /// <summary>Tests: the hands' busy state instead of Layout's (null: ILayoutService.HandBusy).</summary>
        public Func<int, bool> HandBusyOverride;
        /// <summary>Mixed reality with no scene model and no depth sensor: notes were not painted (tests).</summary>
        public bool MrHasWalls => _mrScene || _mrDepth;
        public bool TipAsked => _tipDone;
        public bool KeepTipAsked => _keepTipDone;
        public bool ThrowTipAsked => _throwTipDone;
        public bool GalleryTipAsked => _galleryTipDone;
        public Texture2D Atlas => _atlas;
        public ThrowGesture Gesture => _gesture;
        /// <summary>The swirl into the frame: 0..1 while it runs, -1 when not.</summary>
        public float Gather => _gatherT < 0f ? -1f : Mathf.Clamp01(_gatherT / GatherSeconds);
        public bool BlobFlying { get { for (int i = 0; i < Blobs; i++) if (_blobs[i].Active) return true; return false; } }
        public Vector3 LastSplat { get; private set; }
        /// <summary>Throwing is on now: a playback paints or its painting holds.</summary>
        public bool CanThrow => State == Phase.Painting || (State == Phase.Holding && _gatherT < 0f);
        /// <summary>Keep painting (the menu's setting, persisted).</summary>
        public bool Keep => _app.Settings != null && _app.Settings.GetBool(AppSettings.KeyKeepPaintings, false);

        public WallPaint(App app, RoomScene scene)
        {
            _app = app;
            _scene = scene;
            _onNote = OnNote;
            _onPlayback = OnPlayback;
            _onState = OnState;
            app.Events.Subscribe(_onNote);
            app.Events.Subscribe(_onPlayback);
            app.Events.Subscribe(_onState);
            _onLayout = _ => RigsMoved();
            _onArranged = _ => RigsMoved();
            _onMove = _ => RigsMoved();
            _onMr = _ => RigsMoved();
            app.Events.Subscribe(_onLayout);
            app.Events.Subscribe(_onArranged);
            app.Events.Subscribe(_onMove);
            app.Events.Subscribe(_onMr);
            if (scene?.Root != null)
            {
                float t0 = Time.realtimeSinceStartup;
                int tris = BuildRoomCanvas(scene);
                Debug.Log($"[Room] wall paint: {RoomCanvas.WallCount} walls, {RoomCanvas.PaintableCells} clear cells of {WallCanvas.Cell} m, perimeter {RoomCanvas.Perimeter:F1} m, " +
                          $"{tris} triangles looked at in {(Time.realtimeSinceStartup - t0) * 1000f:F1} ms");
            }
            Gallery = new PaintGallery(app, scene, RoomCanvas);
            if (app.Headless || !app.Materials.HasTemplate(PaintTemplates.WallPaint)) return;
            // the brush atlas: its pixels on a worker (the gallery's painter shares them), the texture once they are made
            _atlasJob = Task.Run(() => BrushAtlas.Pixels);
            if (scene?.Root != null) Build(RoomLayer, scene.Root, "WallPaint");
            Build(MrLayer, app.WorldRoot, "WallPaintMr");
            if (RoomLayer.Material != null) { PaintTemplates.SetStain(RoomLayer.Material); PaintTemplates.SetStain(RoomLayer.GatherMaterial); }
            PaintTemplates.SetWash(MrLayer.Material);
            PaintTemplates.SetWash(MrLayer.GatherMaterial);
            BuildBlobs();
        }

        void Build(Layer l, Transform parent, string name)
        {
            l.Go = new GameObject(name);
            l.Go.transform.SetParent(parent, false);
            l.Mesh = new Mesh { name = name };
            l.Mesh.MarkDynamic();
            int nv = MaxQuads * 4, ni = MaxQuads * 6;
            l.Mesh.SetVertexBufferParams(nv, VertexLayout);
            l.Verts = new NativeArray<Vert>(nv, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var idx = new ushort[ni];
            for (int q = 0; q < MaxQuads; q++)
            {
                int v = q * 4, i = q * 6;
                idx[i] = (ushort)v; idx[i + 1] = (ushort)(v + 1); idx[i + 2] = (ushort)(v + 2);
                idx[i + 3] = (ushort)v; idx[i + 4] = (ushort)(v + 2); idx[i + 5] = (ushort)(v + 3);
            }
            const MeshUpdateFlags quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            l.Mesh.SetVertexBufferData(l.Verts, 0, 0, nv, 0, quiet);
            l.Mesh.SetIndexBufferParams(ni, IndexFormat.UInt16);
            l.Mesh.SetIndexBufferData(idx, 0, 0, ni, quiet);
            l.Mesh.subMeshCount = 1;
            l.Mesh.SetSubMesh(0, new SubMeshDescriptor(0, ni) { bounds = new Bounds(Vector3.zero, Vector3.one) }, MeshUpdateFlags.DontRecalculateBounds);
            l.Mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            l.Go.AddComponent<MeshFilter>().sharedMesh = l.Mesh;
            l.Renderer = l.Go.AddComponent<MeshRenderer>();
            l.Material = _app.Materials.Clone(PaintTemplates.WallPaint);
            l.Renderer.sharedMaterial = l.Material;
            Quiet(l.Renderer);
            // the gather: the same mesh drawn again (copies swirling into a frame), only while it runs
            l.GatherGo = new GameObject(name + "Gather");
            l.GatherGo.transform.SetParent(l.Go.transform, false);
            l.GatherGo.AddComponent<MeshFilter>().sharedMesh = l.Mesh;
            l.GatherRenderer = l.GatherGo.AddComponent<MeshRenderer>();
            l.GatherMaterial = _app.Materials.Clone(PaintTemplates.WallPaint);
            l.GatherMaterial.SetFloat(GatherOnId, 1f);
            l.GatherMaterial.renderQueue = PaintTemplates.Queue + 1;
            l.GatherRenderer.sharedMaterial = l.GatherMaterial;
            l.GatherRenderer.localBounds = new Bounds(Vector3.zero, new Vector3(30f, 10f, 30f));
            Quiet(l.GatherRenderer);
        }

        static void Quiet(MeshRenderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.enabled = false;
        }

        /// <summary>The atlas texture once its pixels are made (a worker started at boot; the first frames after).</summary>
        void EnsureAtlas()
        {
            if (_atlas != null || _atlasJob == null || !_atlasJob.IsCompleted) return;
            Color32[] px = _atlasJob.Status == TaskStatus.RanToCompletion ? _atlasJob.Result : PaintArt.Build();
            _atlasJob = null;
            _atlas = PaintArt.Make(px);
            foreach (Layer l in new[] { RoomLayer, MrLayer })
            {
                if (l.Material != null) l.Material.SetTexture(MaterialLibrary.BaseMap, _atlas);
                if (l.GatherMaterial != null) l.GatherMaterial.SetTexture(MaterialLibrary.BaseMap, _atlas);
            }
        }

        /// <summary>Waits for the atlas (tests, the screenshots).</summary>
        public void FinishAtlas()
        {
            if (_atlasJob != null) _atlasJob.Wait();
            EnsureAtlas();
        }

        void BuildBlobs()
        {
            _blobMesh = PrimitiveMeshes.Sphere(BlobRadius, 14, 10).ToUnityMesh("PaintBlob");
            for (int i = 0; i < Blobs; i++)
            {
                var go = new GameObject("PaintBlob" + i);
                go.transform.SetParent(_app.WorldRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = _blobMesh;
                var r = go.AddComponent<MeshRenderer>();
                Material m = _app.Materials.Lit(Color.white, 0.82f);
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.SetActive(false);
                _blobs[i].Go = go;
                _blobs[i].Material = m;
            }
        }

        // ---------------------------------------------------------------- the VR room's walls

        /// <summary>The room's walls from its own wall mesh (in the room root's space), everything else in the room blocking
        /// what it stands in front of. Setup only (allocates). Returns the triangles looked at.</summary>
        int BuildRoomCanvas(RoomScene scene)
        {
            WallCanvas c = RoomCanvas;
            c.Clear();
            if (!scene.Static.TryGetValue(RoomMat.Wall, out Renderer wallR) || wallR == null) return 0;
            var wallMf = wallR.GetComponent<MeshFilter>();
            Mesh wm = wallMf != null ? wallMf.sharedMesh : null;
            if (wm == null || !wm.isReadable) return 0;
            Matrix4x4 toRoot = scene.Root.worldToLocalMatrix;
            Matrix4x4 m = toRoot * wallR.transform.localToWorldMatrix;
            Vector3[] wv = wm.vertices;
            int[] wt = wm.triangles;
            for (int i = 0; i < wv.Length; i++) wv[i] = m.MultiplyPoint3x4(wv[i]);
            Vector3 mid = Vector3.zero;
            for (int i = 0; i < wv.Length; i++) mid += wv[i];
            mid /= Mathf.Max(1, wv.Length);
            var planes = new Dictionary<long, List<int>>();
            var normals = new Dictionary<long, Vector3>();
            for (int t = 0; t + 2 < wt.Length; t += 3)
            {
                Vector3 a = wv[wt[t]], b = wv[wt[t + 1]], d = wv[wt[t + 2]];
                Vector3 n = Vector3.Cross(b - a, d - a);
                if (n.sqrMagnitude < 1e-10f) continue;
                n.Normalize();
                if (Mathf.Abs(n.y) > 0.2f) continue;
                n.y = 0f; n.Normalize();
                if (Vector3.Dot(mid - a, n) < 0f) n = -n;   // toward the room
                long key = ((long)Mathf.RoundToInt(n.x * 50f) + 64) | (((long)Mathf.RoundToInt(n.z * 50f) + 64) << 8) | (((long)Mathf.RoundToInt(Vector3.Dot(n, a) * 20f) + 100000) << 16);
                if (!planes.TryGetValue(key, out var list)) { planes[key] = list = new List<int>(); normals[key] = n; }
                list.Add(t);
            }
            foreach (var kv in planes)
            {
                Vector3 n = normals[kv.Key];
                Vector3 along = Vector3.Cross(Vector3.up, n).normalized;
                float u0 = float.MaxValue, u1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                Vector3 p0 = wv[wt[kv.Value[0]]];
                foreach (int t in kv.Value)
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 p = wv[wt[t + k]];
                        float u = Vector3.Dot(p - p0, along);
                        u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u);
                        y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
                    }
                if (u1 - u0 < 0.5f || y1 - y0 < 1f) continue;
                Vector3 origin = p0 + along * u0;
                origin.y = y0;
                int w = c.AddWall(origin, along, n, u1 - u0, y1 - y0, solid: false);
                foreach (int t in kv.Value) c.MarkWall(w, wv[wt[t]], wv[wt[t + 1]], wv[wt[t + 2]]);
            }
            int looked = 0;
            var filters = scene.Root.GetComponentsInChildren<MeshFilter>(true);
            var verts = new List<Vector3>(4096);
            var tris = new List<int>(8192);
            foreach (MeshFilter mf in filters)
            {
                if (mf == null || mf.sharedMesh == null || mf == wallMf || !mf.sharedMesh.isReadable) continue;
                var r = mf.GetComponent<Renderer>();
                if (r == null || r == scene.SnowField || r == scene.Frost) continue;
                Mesh mesh = mf.sharedMesh;
                Matrix4x4 mm = toRoot * mf.transform.localToWorldMatrix;
                if (FarFromWalls(c, mesh.bounds, mm)) continue;
                mesh.GetVertices(verts);
                for (int i = 0; i < verts.Count; i++) verts[i] = mm.MultiplyPoint3x4(verts[i]);
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                    mesh.GetTriangles(tris, s);
                    for (int t = 0; t + 2 < tris.Count; t += 3)
                    {
                        Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], d = verts[tris[t + 2]];
                        looked++;
                        if (Outside(c, a) || Outside(c, b) || Outside(c, d)) continue;
                        c.Block(a, b, d);
                    }
                }
            }
            c.Finish();
            return looked;
        }

        static bool FarFromWalls(WallCanvas c, Bounds local, Matrix4x4 m)
        {
            for (int i = 0; i < c.WallCount; i++)
            {
                c.WallFrame(i, out Vector3 o, out _, out Vector3 n, out _, out _);
                float lo = float.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = local.center + Vector3.Scale(local.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
                    lo = Mathf.Min(lo, Vector3.Dot(m.MultiplyPoint3x4(p) - o, n));
                }
                if (lo <= WallCanvas.Band) return false;
            }
            return true;
        }

        static bool Outside(WallCanvas c, Vector3 p)
        {
            for (int i = 0; i < c.WallCount; i++)
            {
                c.WallFrame(i, out Vector3 o, out _, out Vector3 n, out _, out _);
                if (Vector3.Dot(p - o, n) < -0.25f) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- mixed reality's walls

        IRealWalls RealWalls => RealWallsOverride ?? _app.Get<IRealWalls>();

        /// <summary>The scene model's walls as a canvas (as the room's). True when there is one with clear wall. Setup (a
        /// playback's start, the switch to MR): may allocate.</summary>
        bool BuildMrCanvas()
        {
            IRealWalls walls = RealWalls;
            if (walls == null || !walls.Refresh() || walls.WallCount == 0) return false;
            if (walls.Version != _mrVersion || !MrCanvas.Finished)
            {
                MrCanvas.Clear();
                for (int i = 0; i < walls.WallCount; i++)
                {
                    RealWall w = walls.Wall(i);
                    Vector3 inward = w.Inward; inward.y = 0f;
                    if (inward.sqrMagnitude < 1e-6f || w.Size.x < 0.5f || w.Size.y < 1f) continue;
                    inward.Normalize();
                    Vector3 along = Vector3.Cross(Vector3.up, inward).normalized;
                    Vector3 origin = w.Centre - along * (0.5f * w.Size.x) - Vector3.up * (0.5f * w.Size.y);
                    MrCanvas.AddWall(origin, along, inward, w.Size.x, w.Size.y, solid: true);
                }
                for (int i = 0; i < walls.BlockCount; i++)
                {
                    RealWallBlock b = walls.Block(i);
                    MrCanvas.BlockBox(b.Centre, b.Half, b.Rotation);
                }
                MrCanvas.Finish();
                _mrVersion = walls.Version;
                BlockRigs(MrCanvas, null);
            }
            bool ok = MrCanvas.Finished && MrCanvas.PaintableCells > 0;
            if (ok) Gallery.SetMrCanvas(MrCanvas, _mrVersion);
            return ok;
        }

        /// <summary>When a playback starts painting in mixed reality: the scene model's walls, else the depth sensor, else
        /// nothing.</summary>
        void PrepareMr()
        {
            _mrReady = true;
            _mrScene = _mrDepth = false;
            _hitCount = _hitNext = 0;
            if (RealWalls == null) return;
            _mrScene = BuildMrCanvas();
            if (!_mrScene) _mrDepth = true;   // try the depth sensor (it answers false where there is none)
        }

        // ---------------------------------------------------------------- events

        void OnPlayback(RecordingPlaybackEvent e)
        {
            if (e.Started) Begin(e.Loop);
            else
            {
                End();
                // Layout puts its own layout back a moment after the end (its grace), then glides: read after that
                _rigsDirty = true;
                _rigsAt = _time + AfterPlayback;
            }
        }

        void OnState(RecorderStateEvent e)
        {
            if (e.State == RecorderState.Idle) End();
        }

        void OnNote(PlaybackNoteEvent e)
        {
            if (State != Phase.Painting || e.Role != EventRole.Onset || !(e.Level > 0f)) return;
            if (_pendingCount >= PendingMax) return;
            float pitch = PaintPalette.PitchOf(e.Kind, e.Event, out int key);
            double now = _app.AudioTime;
            _pending[_pendingCount++] = new Pending
            {
                Kind = (int)e.Kind, Key = key, Pitch = pitch, Level = Mathf.Min(1f, e.Level), When = e.When > now ? e.When : now,
                Accent = e.Kind == InstrumentKind.HandDrums && string.Equals(e.Event.zone, "slap", StringComparison.Ordinal),
            };
        }

        /// <summary>A playback started: a new painting (a kept or fading one washes away meanwhile).</summary>
        void Begin(bool loop)
        {
            EndGather();
            int old = _cur;
            int oldCount = RoomLayer.Alive[old] + MrLayer.Alive[old];
            if (_clearing) { Collapse(1 - _cur); _clearing = false; }
            if (oldCount > 0 && _fade[old] > 0f)
            {
                _cur = 1 - old;
                Collapse(_cur);    // slots of a painting two playbacks ago: long gone, but make sure
                _clearing = true;
            }
            else if (oldCount > 0) Collapse(old);
            _fade[_cur] = 1f;
            State = Phase.Painting;
            _phaseTime = 0f;
            _startAudio = _app.AudioTime;
            _noteIndex = 0;
            _paintedThis = 0;
            _pendingCount = 0;
            _budget = BurstMax;
            _s0Room = _s0Mr = false;
            _mrReady = false;
            _brush.Reset();
            _ribbonWall = _arcWall = -1;
            _lastPianoT = _lastMelodyT = -10f;
            _ribbonEndS = float.MinValue;
            _gesture.Reset();
            RoomCanvas.ClearMarks();
            MrCanvas.ClearMarks();
            Gallery.OnPlaybackStarted(_app.Get<IRecorder>()?.Take, loop);
        }

        void End()
        {
            if (State != Phase.Painting) return;
            _pendingCount = 0;
            FinishStrokes();
            State = Phase.Holding;
            _phaseTime = 0f;
            Gallery.OnPlaybackEnded(_paintedThis);
        }

        // ---------------------------------------------------------------- the frame

        /// <summary>Room's Decor phase, every frame (also while the room is hidden: mixed reality paints the real walls).</summary>
        public void Tick(float dt, bool roomVisible)
        {
            _time += dt;
            _phaseTime += dt;
            _roomVisible = roomVisible;
            EnsureAtlas();
            if (State == Phase.Painting)
            {
                _budget = Mathf.Min(BurstMax, _budget + dt * NotesPerSecond);
                if (!roomVisible)
                {
                    if (!_mrReady) PrepareMr();
                    if (_mrDepth) CastDepthRay();
                }
                PaintDue();
                // a phrase of the melody ended: its stroke tapers off
                if (_brush.RibbonOpen && (float)(_app.AudioTime - _startAudio) - _lastMelodyT > PaintBrushes.PhraseGap) FinishRibbon();
            }
            TickRigs();
            if (CanThrow) Throwing();
            FlyBlobs(dt);
            Lifecycle(dt);
            TickGather(dt);
            if (!roomVisible && Gallery.Count > 0 && State != Phase.Painting)
            {
                // mixed reality: the gallery's frames on the scene model's walls (looked for now and then until found)
                _mrGalleryTry -= dt;
                if (_mrGalleryTry <= 0f) { _mrGalleryTry = 3f; if (!MrCanvas.Finished) BuildMrCanvas(); }
            }
            Gallery.Tick(dt, roomVisible);
            Upload(RoomLayer, roomVisible);
            Upload(MrLayer, !roomVisible);
            AskSunny(dt);
        }

        void Lifecycle(float dt)
        {
            switch (State)
            {
                case Phase.Painting:
                    _fade[_cur] = 1f;
                    break;
                case Phase.Holding:
                    if (Splashes == 0) { State = Phase.Idle; break; }
                    if (_phaseTime >= HoldSeconds && !Gallery.Resolving)
                    {
                        StartGather();
                        State = Keep ? Phase.Kept : Phase.Fading;
                        _phaseTime = 0f;
                    }
                    break;
                case Phase.Fading:
                    _keepPoll -= dt;
                    if (_keepPoll <= 0f)
                    {
                        _keepPoll = 0.5f;
                        if (Keep) { State = Phase.Kept; break; }
                    }
                    float k = Mathf.Clamp01(_phaseTime / FadeSeconds);
                    _fade[_cur] = 1f - k * k * (3f - 2f * k);
                    if (k >= 1f && _gatherT < 0f)
                    {
                        Collapse(_cur);
                        _fade[_cur] = 1f;
                        State = Phase.Idle;
                    }
                    break;
                case Phase.Kept:
                    _fade[_cur] = Mathf.Min(1f, _fade[_cur] + 2f * dt);   // back up if it had begun to fade
                    _keepPoll -= dt;
                    if (_keepPoll <= 0f)
                    {
                        _keepPoll = 0.5f;
                        if (!Keep) { State = Phase.Fading; _phaseTime = 0f; }
                    }
                    if (Splashes == 0) State = Phase.Idle;
                    break;
            }
            if (_clearing)
            {
                int old = 1 - _cur;
                _fade[old] = Mathf.Max(0f, _fade[old] - dt / ClearSeconds);
                if (_fade[old] <= 0f)
                {
                    Collapse(old);
                    _fade[old] = 1f;
                    _clearing = false;
                }
            }
        }

        void PaintDue()
        {
            if (_pendingCount == 0) return;
            double now = _app.AudioTime;
            int w = 0;
            for (int i = 0; i < _pendingCount; i++)
            {
                Pending p = _pending[i];
                if (p.When > now + 1e-4) { _pending[w++] = p; continue; }
                int index = _noteIndex++;
                _lastKind = (InstrumentKind)p.Kind;
                _lastPitch = p.Pitch;
                var note = new PaintNote { Kind = p.Kind, Pitch = p.Pitch, Level = p.Level, Time = (float)(p.When - _startAudio), Index = index, Key = p.Key, Accent = p.Accent };
                float cost = Continues(note) ? RibbonCost : 1f;
                if (_budget < cost) continue;   // too many at once: this one is heard, not painted
                _budget -= cost;
                if (PaintOne(note)) { Painted++; _paintedThis++; }
                else { Missed++; MissedBy[p.Kind & 15]++; }
            }
            _pendingCount = w;
        }

        /// <summary>The note goes on with a stroke already on the wall (the melody's ribbon, the harp's glissando).</summary>
        bool Continues(in PaintNote note)
        {
            if (note.Kind == (int)InstrumentKind.Piano) return _brush.RibbonOpen && note.Time - _lastMelodyT < PaintBrushes.PhraseGap;
            if (note.Kind == (int)InstrumentKind.Harp) return _brush.ArcContinues(Brush(note));
            return false;
        }

        static BrushNote Brush(in PaintNote note) => new BrushNote
        {
            Kind = (InstrumentKind)note.Kind, Pitch = note.Pitch, Level = note.Level, Time = note.Time, Key = note.Key,
            Hash = WallCanvas.Hash(note.Index, note.Key, note.Kind), Accent = note.Accent,
        };

        /// <summary>One note's mark on the walls of the place shown now. False when no wall took it.</summary>
        public bool PaintOne(in PaintNote note)
        {
            bool room = _roomVisible;
            Layer layer = room ? RoomLayer : MrLayer;
            if (room)
            {
                if (!RoomCanvas.Finished || _scene?.Root == null) return false;
                Vector3 head = HeadIn(_scene.Root, out Vector3 fwd);
                if (!_s0Room) { _s0Room = true; _s0RoomAt = RoomCanvas.PerimeterAlong(head, fwd) - StartBefore; }
                return PaintOnCanvas(note, RoomCanvas, layer, _s0RoomAt, head);
            }
            if (!_mrReady) PrepareMr();
            Vector3 h = HeadIn(null, out Vector3 f);
            if (_mrScene)
            {
                if (!_s0Mr) { _s0Mr = true; _s0MrAt = MrCanvas.PerimeterAlong(h, f) - StartBefore; }
                return PaintOnCanvas(note, MrCanvas, layer, _s0MrAt, h);
            }
            if (_mrDepth) return PaintOnDepth(note, layer, h);
            return false;
        }

        /// <summary>A note on a canvas's walls (the room's, or the scene model's).</summary>
        bool PaintOnCanvas(in PaintNote note, WallCanvas c, Layer layer, float s0, Vector3 head)
        {
            BrushNote bn = Brush(note);
            Color colour = PaintPalette.Of(bn.Kind, note.Pitch).linear;
            float alpha = PaintBrushes.AlphaOf(note.Level);
            _brush.Clear();
            bool room = layer.Room;
            if (bn.Kind == InstrumentKind.Piano)
            {
                bool chord = note.Time - _lastPianoT < PaintBrushes.ChordWindow && _brush.RibbonOpen && _ribbonRoom == room;
                _lastPianoT = note.Time;
                if (chord)
                {
                    // a chord note under the melody: a bud hanging off the vine where it is now
                    Vector2 p = _brush.RibbonPoint0;
                    PaintBrushes.ChordSpot(bn, p, 1f, out Vector2 bud, out float br);
                    if (c.Fits(_ribbonWall, bud.x, bud.y, br))
                    {
                        _brush.ChordBud(bn, p, bud, br, colour, alpha, 0.1f);
                        Emit(layer, c, _ribbonWall);
                        return true;
                    }
                    // no room by the vine: a little flower of its own wherever the song is
                    if (!c.Place(note, s0, PaintBrushes.Reach(bn), out PaintSpot fs)) return false;
                    _brush.LoneFlower(bn, new Vector2(fs.U, fs.V), fs.Radius, colour, alpha);
                    c.Mark(fs);
                    Emit(layer, c, fs.Wall);
                    return true;
                }
                if (_brush.RibbonOpen && (_ribbonRoom != room || note.Time - _lastMelodyT > PaintBrushes.PhraseGap)) FinishRibbon();
                if (!_brush.RibbonOpen) NewPhrase(c, s0, note.Time);
                float s = _phraseS + RibbonSpeed * (note.Time - _phraseT);
                if (s - _phraseS > PhraseMax)
                {
                    FinishRibbon();
                    NewPhrase(c, s0, note.Time);
                    s = _phraseS;
                }
                // the height eases toward the note's: the line flows up and down instead of jumping
                float target = RibbonHeight(c, note.Pitch);
                float v0 = _brush.RibbonOpen ? Mathf.Lerp(_ribbonV, target, Ease) : target, r = PaintBrushes.RibbonMax + 0.02f;
                if (!RibbonSpot(c, s, v0, r, out int wall, out float u, out float v))
                {
                    // something in the way (the window, the chimney, a frame): the stroke ends, the line starts again past it
                    FinishRibbon();
                    bool found = false;
                    for (int k = 1; k <= 40 && !found; k++)
                    {
                        if (!RibbonSpot(c, s + 0.2f * k, v0, r, out wall, out u, out v)) continue;
                        found = true;
                        _phraseS = s + 0.2f * k;
                        _phraseT = note.Time;
                    }
                    if (!found) return false;
                }
                if (_brush.RibbonOpen && wall != _ribbonWall) _brush.RibbonBreak();
                if ((c.Point(wall, u, v) - head).sqrMagnitude < HeadClear * HeadClear) return false;
                float draw = _brush.RibbonOpen ? Mathf.Clamp(note.Time - _lastMelodyT, 0.08f, PaintBrushes.RibbonDraw) : 0.15f;
                // the vine (green, the melody's line: stronger than a motif, it carries the picture), then the note's flower
                _brush.RibbonPoint(new Vector2(u, v), note.Level, note.Time, draw, PaintBrushes.Stem, Mathf.Max(alpha, 0.8f));
                PaintBrushes.BloomSpot(bn, new Vector2(u, v), 1f, out Vector2 flower, out float fr);
                if (c.Fits(wall, flower.x, flower.y, fr))
                    _brush.Bloom(bn, new Vector2(u, v), flower, fr, colour, alpha, draw);
                _ribbonV = v;
                _ribbonEndS = c.WallStart(wall) + u;
                _ribbonWall = wall;
                _ribbonRoom = room;
                _lastMelodyT = note.Time;
                Emit(layer, c, wall);
                return true;
            }
            if (bn.Kind == InstrumentKind.Harp && _brush.ArcContinues(bn) && _arcRoom == room)
            {
                _brush.ArcNext(bn, colour);
                Emit(layer, c, _arcWall);
                return true;
            }
            float reach = PaintBrushes.Reach(bn);
            PaintNote placed = note;
            placed.Pitch = PaintBrushes.Height(bn);   // where its motif belongs: the ground, the middle, the sky
            if (!c.Place(placed, s0, reach, out PaintSpot spot)) return false;
            if ((spot.Centre - head).sqrMagnitude < (HeadClear + spot.Radius) * (HeadClear + spot.Radius)) return false;
            float scale = spot.Radius / reach;
            var at = new Vector2(spot.U, spot.V);
            if (bn.Kind == InstrumentKind.Harp)
            {
                _brush.ArcStart(bn, at, scale, colour);
                _arcWall = spot.Wall;
                _arcRoom = room;
            }
            else _brush.Mark(bn, at, scale, colour);
            ClipFalls(c, spot.Wall);
            c.Mark(spot);
            Emit(layer, c, spot.Wall);
            return true;
        }

        /// <summary>A new phrase of the melody: on from where the last one ended, or from where the song is when it is ahead.</summary>
        void NewPhrase(WallCanvas c, float s0, float time)
        {
            float song = s0 + c.Drift * Mathf.Max(0f, time) - RibbonBefore;
            _phraseS = _ribbonEndS > float.MinValue * 0.5f ? Mathf.Max(song, _ribbonEndS + PhraseSpace) : song;
            _phraseT = time;
        }

        /// <summary>Where the ribbon can go at s along the walls: its pitch's height, or a little above or below it, on clear
        /// wall (and clear a little behind it, where the stroke comes from).</summary>
        static bool RibbonSpot(WallCanvas c, float s, float v0, float r, out int wall, out float u, out float v)
        {
            c.Locate(s, out wall, out u);
            v = v0;
            if (wall < 0) return false;
            for (int k = 0; k <= 6; k++)
            {
                float dv = (k & 1) == 1 ? 0.07f * ((k + 1) / 2) : -0.07f * (k / 2);
                if (c.Fits(wall, u, v0 + dv, r) && c.Fits(wall, u - 0.05f, v0 + dv, r)) { v = v0 + dv; return true; }
            }
            return false;
        }

        /// <summary>The vine's height for a pitch: the middle of the wall (the ground's motifs below it, the sky's above),
        /// higher notes higher.</summary>
        static float RibbonHeight(WallCanvas c, float pitch) => Mathf.Lerp(c.Low + 0.45f, c.High - 0.4f, Mathf.Clamp01(pitch));

        /// <summary>A note on a wall the depth sensor found (no scene model): each mark on its own (a short stroke for the piano,
        /// a short arc for the harp), round a point near the note's direction.</summary>
        bool PaintOnDepth(in PaintNote note, Layer layer, Vector3 head)
        {
            BrushNote bn = Brush(note);
            float reach = PaintBrushes.Reach(bn);
            PaintNote placed = note;
            placed.Pitch = PaintBrushes.Height(bn);   // where its motif belongs (the melody: its own pitch)
            if (!PlaceDepth(placed, reach, out PaintSpot spot)) return false;
            if ((spot.Centre - head).sqrMagnitude < (HeadClear + spot.Radius) * (HeadClear + spot.Radius)) return false;
            Color colour = PaintPalette.Of(bn.Kind, note.Pitch).linear;
            _brush.Clear();
            _brush.Reset();
            if (bn.Kind == InstrumentKind.Piano)
            {
                // a sprig of the vine with the note's flower on it
                float a = PaintBrushes.AlphaOf(note.Level);
                _brush.RibbonPoint(new Vector2(-0.04f, -0.05f), note.Level, note.Time, 0.15f, PaintBrushes.Stem, 0.8f);
                _brush.RibbonPoint(new Vector2(0.03f, -0.03f), note.Level, note.Time, 0.12f, PaintBrushes.Stem, 0.8f);
                _brush.RibbonEnd(0.15f);
                PaintBrushes.BloomSpot(bn, new Vector2(0f, -0.04f), Mathf.Min(1f, spot.Radius / reach), out Vector2 h2, out float r2);
                _brush.Bloom(bn, new Vector2(0f, -0.04f), h2, r2, colour, a, 0.15f);
            }
            else if (bn.Kind == InstrumentKind.Harp) { _brush.ArcStart(bn, Vector2.zero, spot.Radius / reach, colour); _brush.ArcEnd(); }
            else _brush.Mark(bn, Vector2.zero, spot.Radius / reach, colour);
            // falling leaves settle above the floor's band
            for (int q = 0; q < _brush.Count; q++)
            {
                if (_brush.Out[q].Mode != BrushMode.Fall) continue;
                float bottom = spot.Centre.y + Mathf.Min(_brush.Out[q].A.y, _brush.Out[q].B.y);
                _brush.Out[q].P1 = Mathf.Clamp(Mathf.Min(_brush.Out[q].P1, bottom - 0.45f), 0f, 1f);
            }
            EmitFrame(layer, spot.Centre, spot.Along, spot.Normal, -1);
            return true;
        }

        /// <summary>The ribbon's phrase ends: its taper goes on the wall it was on.</summary>
        void FinishRibbon()
        {
            if (!_brush.RibbonOpen) return;
            _brush.Clear();
            _brush.RibbonEnd();
            WallCanvas c = _ribbonRoom ? RoomCanvas : MrCanvas;
            Layer l = _ribbonRoom ? RoomLayer : MrLayer;
            if (_ribbonWall >= 0 && c.Finished && (_ribbonRoom || _mrScene)) Emit(l, c, _ribbonWall);
            _brush.Clear();
        }

        /// <summary>The playback ended: the ribbon tapers, the arc is let go.</summary>
        void FinishStrokes()
        {
            FinishRibbon();
            _brush.ArcEnd();
        }

        /// <summary>Falling leaves and petals drift only as far down as clear wall goes (and stop at the paint band's foot).</summary>
        void ClipFalls(WallCanvas c, int wall)
        {
            for (int q = 0; q < _brush.Count; q++)
            {
                ref BrushQuad b = ref _brush.Out[q];
                if (b.Mode != BrushMode.Fall) continue;
                Vector2 m = b.Centre;
                float bottom = Mathf.Min(Mathf.Min(b.A.y, b.B.y), Mathf.Min(b.C.y, b.D.y));
                float hw = 0.5f * Mathf.Max(Mathf.Abs(b.B.x - b.D.x), Mathf.Abs(b.C.x - b.A.x));
                float clear = 0f;
                for (float d = 0.03f; d <= b.P1 + 1e-3f; d += 0.03f)
                {
                    float y = bottom - d;
                    if (!c.Paintable(wall, m.x, y) || !c.Paintable(wall, m.x - hw, y) || !c.Paintable(wall, m.x + hw, y)) break;
                    clear = d;
                }
                b.P1 = Mathf.Min(b.P1, clear);
            }
        }

        Vector3 HeadIn(Transform space, out Vector3 forward)
        {
            Transform head = _app.Head;
            Vector3 p = head != null ? head.position : new Vector3(0f, 1.2f, 0f);
            Vector3 f = head != null ? head.forward : Vector3.forward;
            if (space != null) { p = space.InverseTransformPoint(p); f = space.InverseTransformDirection(f); }
            forward = f;
            return p;
        }

        // ---------------------------------------------------------------- the instruments at the walls

        void RigsMoved()
        {
            _rigsDirty = true;
            _rigsAt = _time + RigsSettle;
        }

        /// <summary>Once the layout has settled (and no playback's stage holds the instruments at the seat): every rig's
        /// visible boxes block the walls they stand at, for the paint and the gallery's frames (a frame something now
        /// stands at moves). Allocation-free (the renderer list is reused).</summary>
        void TickRigs()
        {
            if (!_rigsDirty || _time < _rigsAt) return;
            if (State == Phase.Painting || _gatherT >= 0f) return;   // a playback's stage has them at the seat; a swirl flies
            ILayoutService layout = _app.Get<ILayoutService>();
            if (layout != null && (!layout.FirstArrangeDone || layout.AnyMoving)) return;
            ReadRigs();
        }

        void ReadRigs()
        {
            if (DebugRigs) Debug.Log($"[Room] read rigs at {_time:F2}, state {State}");
            _rigsDirty = false;
            RigReads++;
            BlockRigs(RoomCanvas, _scene?.Root);
            if (MrCanvas.Finished) BlockRigs(MrCanvas, null);
            Gallery.RigsChanged();
        }

        /// <summary>Read the rigs now (tests).</summary>
        public void ReadRigsNow() => ReadRigs();

        void BlockRigs(WallCanvas c, Transform space)
        {
            if (!c.Finished) return;
            c.ClearMoving();
            IReadOnlyList<Rig> rigs = _app.Get<IRigRegistry>()?.Rigs;
            if (rigs == null) return;
            CacheRigRenderers(rigs);
            Quaternion rot = space != null ? Quaternion.Inverse(space.rotation) : Quaternion.identity;
            for (int i = 0; i < _rigCache.Count; i++)
            {
                Rig r = _rigCache[i];
                if (r == null || r.Group == null || !r.Group.gameObject.activeInHierarchy) continue;
                int end = i + 1 < _rigStart.Count ? _rigStart[i + 1] : _rigRenderers.Count;
                for (int k = _rigStart[i]; k < end; k++)
                {
                    Renderer rr = _rigRenderers[k];
                    if (rr == null || !rr.enabled || !rr.gameObject.activeInHierarchy || rr is ParticleSystemRenderer) continue;
                    Bounds b = rr.bounds;
                    if (b.size.sqrMagnitude < 1e-6f) continue;
                    int before = DebugRigs ? c.MovingCells : 0;
                    c.BlockMoving(space != null ? space.InverseTransformPoint(b.center) : b.center, b.extents, rot);
                    if (DebugRigs && c.MovingCells > before) Debug.Log($"[Room] rig {r.Id} {rr.name} {b} blocks {c.MovingCells - before} cells");
                }
            }
        }

        /// <summary>Every rig's renderers (inactive ones too: they may show later), gathered again only when the rigs or the
        /// app's services change.</summary>
        void CacheRigRenderers(IReadOnlyList<Rig> rigs)
        {
            int version = _app.ServicesVersion * 31 + rigs.Count;
            bool same = version == _rigVersion && _rigCache.Count == rigs.Count;
            for (int i = 0; same && i < rigs.Count; i++) same = ReferenceEquals(rigs[i], _rigCache[i]);
            if (same) return;
            _rigVersion = version;
            _rigCache.Clear();
            _rigStart.Clear();
            _rigRenderers.Clear();
            var tmp = new List<Renderer>(64);
            for (int i = 0; i < rigs.Count; i++)
            {
                _rigCache.Add(rigs[i]);
                _rigStart.Add(_rigRenderers.Count);
                if (rigs[i]?.Group == null) continue;
                rigs[i].Group.GetComponentsInChildren(true, tmp);
                _rigRenderers.AddRange(tmp);
            }
        }

        // ---------------------------------------------------------------- mixed reality without a scene model

        /// <summary>One depth ray a frame along the fan about the look direction at the playback's start; a flat upright wall
        /// it finds is kept DepthKeep seconds.</summary>
        void CastDepthRay()
        {
            IRealWalls walls = RealWalls;
            Transform head = _app.Head;
            if (walls == null || head == null) return;
            if (_rayIndex == 0 || _hitCount == 0)
            {
                Vector3 f = head.forward; f.y = 0f;
                if (f.sqrMagnitude > 1e-6f) _mrForward = f.normalized;
            }
            int i = _rayIndex++ % (DepthYaw.Length * DepthPitch.Length);
            float yaw = DepthYaw[i % DepthYaw.Length], pitch = DepthPitch[i / DepthYaw.Length];
            Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * _mrForward;
            dir = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, dir)) * dir;
            if (!walls.DepthWall(new Ray(head.position, dir), out Vector3 point, out Vector3 inward)) return;
            float dist = Vector3.Distance(point, head.position);
            inward.y = 0f;
            if (dist < DepthMin || dist > DepthMax || inward.sqrMagnitude < 0.25f) return;
            Vector3 to = point - head.position; to.y = 0f;
            _hits[_hitNext] = new DepthHit { Point = point, Inward = inward.normalized, Yaw = Vector3.SignedAngle(_mrForward, to, Vector3.up), Time = _time };
            _hitNext = (_hitNext + 1) % DepthHits;
            if (_hitCount < DepthHits) _hitCount++;
        }

        /// <summary>A mark on a wall the depth sensor found: the nearest fresh hit to the note's direction (it drifts across
        /// the view as the song plays), a little off it along the wall, pitch setting the height.</summary>
        bool PlaceDepth(in PaintNote note, float radius, out PaintSpot spot)
        {
            spot = default;
            uint h = WallCanvas.Hash(note.Index, note.Key, note.Kind);
            float want = Mathf.PingPong(note.Time * 3f + 30f, 100f) - 50f + 24f * (WallCanvas.Rand(h, 1) - 0.5f);
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < _hitCount; i++)
            {
                if (_time - _hits[i].Time > DepthKeep) continue;
                float d = Mathf.Abs(Mathf.DeltaAngle(_hits[i].Yaw, want));
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return false;
            DepthHit hit = _hits[best];
            Vector3 along = Vector3.Cross(Vector3.up, hit.Inward).normalized;
            float r = radius * (0.85f + 0.15f * WallCanvas.Rand(h, 3));
            Vector3 c = hit.Point + along * (0.3f * (WallCanvas.Rand(h, 4) - 0.5f));
            c.y = Mathf.Clamp(hit.Point.y + 0.6f * (note.Pitch - 0.5f) + 0.12f * (WallCanvas.Rand(h, 2) - 0.5f), 0.45f + r, 2.3f - r);
            spot = new PaintSpot { Wall = -1, Centre = c, Along = along, Normal = hit.Inward, Radius = r };
            return true;
        }

        // ---------------------------------------------------------------- throwing paint

        /// <summary>Both hands into the throw gesture; a throw launches a blob toward the wall it points at.</summary>
        void Throwing()
        {
            PointerInput input = _app.Input;
            Transform head = _app.Head;
            if (input == null || head == null || input.MouseHand) return;
            ILayoutService layout = HandBusyOverride == null ? _app.Get<ILayoutService>() : null;
            Vector3 hp = head.position, hf = head.forward;
            for (int i = 0; i < 2; i++)
            {
                HandState h = input.Hands[i];
                HandFrame f = input.Frame(h);
                var s = new ThrowSample { Time = _time };
                s.Tracked = f.isTracked && f.isHighConfidence && !f.systemGesture && h.Connected;
                if (s.Tracked && HandMath.Palm(f, out Vector3 palm, out _)) s.Palm = palm;
                else s.Tracked = false;
                s.Open = s.Tracked ? HandMath.GraspAmount(f) : 0f;
                s.Pinch = h.Pinching || h.GrabPinching || h.MetaPinching || (s.Tracked && HandMath.PinchGap(f) < 0.03f);
                s.Busy = h.Holding != null || (HandBusyOverride != null ? HandBusyOverride(i) : layout != null && layout.HandBusy(i));
                if (!_gesture.Push(i, s, hp, hf, out Vector3 origin, out Vector3 dir, out _)) continue;
                Throws++;
                if (_time - _lastThrow < 1f / ThrowsPerSecond) continue;
                _lastThrow = _time;
                Launch(origin, dir);
            }
        }

        /// <summary>A throw from the hand: where it lands (the wall the throw's ray hits, the nearest clear spot round there),
        /// and a blob in flight to it.</summary>
        public bool Launch(Vector3 origin, Vector3 dir)
        {
            int slot = -1;
            for (int i = 0; i < Blobs; i++) if (!_blobs[i].Active) { slot = i; break; }
            if (slot < 0) return false;
            ref Blob b = ref _blobs[slot];
            b.Splat = false;
            b.Room = _roomVisible;
            b.Wall = -1;
            dir.Normalize();
            Vector3 to = origin + dir * 2.5f;
            float size = SplatSize * (0.9f + 0.2f * WallCanvas.Rand((uint)Throws * 2654435761u, 1));
            if (_roomVisible ? RoomCanvas.Finished && _scene?.Root != null : (_mrReady ? _mrScene : BuildMrCanvas()))
            {
                WallCanvas c = _roomVisible ? RoomCanvas : MrCanvas;
                Transform space = _roomVisible ? _scene.Root : null;
                Vector3 o = space != null ? space.InverseTransformPoint(origin) : origin;
                Vector3 d = space != null ? space.InverseTransformDirection(dir) : dir;
                if (c.Raycast(o, d, out int wall, out float u, out float v, out float dist) && dist < ThrowReach)
                {
                    to = space != null ? space.TransformPoint(c.Point(wall, u, v)) : c.Point(wall, u, v);
                    if (c.FindClear(wall, u, v, size * 0.85f, 6, out float fu, out float fv))
                    {
                        b.Splat = true; b.Wall = wall; b.U = fu; b.V = fv;
                        c.WallFrame(wall, out b.Origin, out b.Along, out b.Normal, out _, out _);
                        to = space != null ? space.TransformPoint(c.Point(wall, fu, fv)) : c.Point(wall, fu, fv);
                    }
                }
            }
            else if (!_roomVisible && RealWalls != null && RealWalls.DepthWall(new Ray(origin, dir), out Vector3 point, out Vector3 inward))
            {
                inward.y = 0f;
                float dist = Vector3.Distance(point, origin);
                if (inward.sqrMagnitude > 0.25f && dist < ThrowReach && point.y > 0.5f && point.y < 2.4f)
                {
                    b.Splat = true; b.Wall = -1;
                    b.Origin = point; b.Normal = inward.normalized; b.Along = Vector3.Cross(Vector3.up, b.Normal).normalized;
                    b.U = b.V = 0f;
                    to = point;
                }
            }
            Debug.Log($"[Room] paint thrown from {origin} toward {dir}: {(b.Splat ? "lands at " + to : "no clear wall that way")} ({(_roomVisible ? "VR" : "MR")})");
            b.Active = true;
            b.T = 0f;
            b.From = origin;
            b.To = to;
            b.Duration = Mathf.Clamp(Vector3.Distance(origin, to) / BlobSpeed, 0.12f, 0.7f);
            b.Hash = WallCanvas.Hash(Throws, 7, 11);
            b.Colour = PaintPalette.Of(_lastKind, _lastPitch);   // the instrument sounding now, or the one heard last
            if (b.Go != null)
            {
                b.Material.SetColor(MaterialLibrary.BaseColor, b.Colour);
                b.Go.transform.position = origin;
                b.Go.SetActive(true);
            }
            return true;
        }

        void FlyBlobs(float dt)
        {
            for (int i = 0; i < Blobs; i++)
            {
                ref Blob b = ref _blobs[i];
                if (!b.Active) continue;
                b.T += dt;
                float k = Mathf.Clamp01(b.T / b.Duration);
                if (b.Go != null)
                {
                    float dist = Vector3.Distance(b.From, b.To);
                    Vector3 p = Vector3.Lerp(b.From, b.To, k) + Vector3.up * (BlobArc * dist * 4f * k * (1f - k));
                    Vector3 vel = (b.To - b.From) + Vector3.up * (BlobArc * dist * 4f * (1f - 2f * k));
                    Transform t = b.Go.transform;
                    t.position = p;
                    if (vel.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(vel);
                    float wob = 1f + 0.12f * Mathf.Sin(b.T * 40f);
                    t.localScale = new Vector3(wob, 1f / wob, 1.5f);   // stretched along its flight, wobbling
                }
                if (k < 1f) continue;
                b.Active = false;
                if (b.Go != null) b.Go.SetActive(false);
                if (b.Splat) Splat(ref b);
            }
        }

        void Splat(ref Blob b)
        {
            if (State != Phase.Painting && State != Phase.Holding) { Debug.Log("[Room] paint landed after the painting ended: no splat"); return; }
            Layer l = b.Room ? RoomLayer : MrLayer;
            WallCanvas c = b.Room ? RoomCanvas : MrCanvas;
            _brush.Clear();
            Color colour = b.Colour.linear;
            float size = SplatSize * (0.9f + 0.2f * WallCanvas.Rand(b.Hash, 1));
            if (b.Wall >= 0)
            {
                _brush.ThrowSplat(new Vector2(b.U, b.V), size, b.Hash, colour);
                ClipFalls(c, b.Wall);
                EmitFrame(l, b.Origin, b.Along, b.Normal, -1);
                LastSplat = b.Room && _scene?.Root != null ? _scene.Root.TransformPoint(c.Point(b.Wall, b.U, b.V)) : c.Point(b.Wall, b.U, b.V);
            }
            else
            {
                _brush.ThrowSplat(Vector2.zero, size, b.Hash, colour);
                for (int q = 0; q < _brush.Count; q++)
                    if (_brush.Out[q].Mode == BrushMode.Fall) _brush.Out[q].P1 = Mathf.Clamp(Mathf.Min(_brush.Out[q].P1, b.Origin.y + _brush.Out[q].A.y - 0.45f), 0f, 1f);
                EmitFrame(l, b.Origin, b.Along, b.Normal, -1);
                LastSplat = b.Origin;
            }
            _brush.Clear();
            Splats++;
            PlaySplat();
        }

        /// <summary>The soft "splat": a short filtered-noise one-shot on the Ui source.</summary>
        void PlaySplat()
        {
            SplatSounds++;
            var audio = _app.Get<IAudioEngine>();
            if (audio == null) return;
            if (_splatPreset < 0 || _splatRate != audio.SampleRate)
            {
                _splatRate = audio.SampleRate;
                _splatPreset = NoiseShots.Register(audio, SplatLanes, 0.22f, -24f, 0x5b1a7u);
            }
            if (_splatPreset >= 0) audio.PlayOneShot(_splatPreset, 0.55f, SourceId.Ui);
        }

        // ---------------------------------------------------------------- the gather (the gallery)

        void StartGather()
        {
            if (!Gallery.Ready) return;
            if (!_roomVisible && !_mrScene) return;
            if (!Gallery.RigsKnown) ReadRigs();   // the layout never settled since boot: read what stands at the walls now   // mixed reality: frames hang on the scene model's walls only
            Layer l = _roomVisible ? RoomLayer : MrLayer;
            if (l.Alive[_cur] == 0) return;
            if (!Gallery.BeginHang(_roomVisible, out Vector3 c, out Vector3 r, out Vector3 u, out Vector3 n)) return;
            _gatherT = 0f;
            _gatherGen = _cur;
            _gatherRoom = _roomVisible;
            _revealed = false;
            _gatherId = Gallery.LastHungId;
            Material m = l.GatherMaterial;
            if (m != null)
            {
                Transform s = l.Go.transform;
                Vector3 lc = s.InverseTransformPoint(c), lr = s.InverseTransformDirection(r), lu = s.InverseTransformDirection(u), ln = s.InverseTransformDirection(n);
                m.SetVector(GatherCentreId, lc);
                m.SetVector(GatherRightId, lr);
                m.SetVector(GatherUpId, lu);
                m.SetVector(GatherNormalId, ln);
                m.SetFloat(GatherGenId, _gatherGen);
                m.SetFloat(GatherId, 0f);
                l.GatherRenderer.enabled = true;
            }
        }

        void TickGather(float dt)
        {
            if (_gatherT < 0f) return;
            _gatherT += dt;
            float g = Mathf.Clamp01(_gatherT / GatherSeconds);
            Layer l = _gatherRoom ? RoomLayer : MrLayer;
            if (l.GatherMaterial != null)
            {
                l.GatherMaterial.SetFloat(GatherId, g);
                l.GatherMaterial.SetFloat(PaintTimeId, _time);
            }
            if (!_revealed && g >= RevealAt)
            {
                _revealed = true;
                Gallery.Reveal(_gatherId);
                _galleryTipUntil = _time + 8f;
            }
            if (g >= 1f) EndGather();
        }

        void EndGather()
        {
            if (_gatherT < 0f) return;
            if (!_revealed) { _revealed = true; Gallery.Reveal(_gatherId); }
            _gatherT = -1f;
            if (RoomLayer.GatherRenderer != null) RoomLayer.GatherRenderer.enabled = false;
            if (MrLayer.GatherRenderer != null) MrLayer.GatherRenderer.enabled = false;
        }

        // ---------------------------------------------------------------- the mesh

        /// <summary>The brushes' quads onto a canvas wall (its frame: origin, along, normal; the quads' x along it, y up).</summary>
        void Emit(Layer l, WallCanvas c, int wall)
        {
            if (wall < 0 || wall >= c.WallCount) { _brush.Clear(); return; }
            c.WallFrame(wall, out Vector3 o, out Vector3 along, out Vector3 n, out _, out _);
            EmitFrame(l, o, along, n, wall);
        }

        /// <summary>The brushes' quads at origin + along x + up y (the canvas's space), lying Lift off the wall.</summary>
        void EmitFrame(Layer l, Vector3 origin, Vector3 along, Vector3 normal, int wall)
        {
            for (int q = 0; q < _brush.Count; q++) Write(l, _brush.Out[q], origin, along, normal);
            _brush.Clear();
        }

        void Write(Layer l, in BrushQuad b, Vector3 origin, Vector3 along, Vector3 normal)
        {
            int slot = l.Next;
            l.Next = (slot + 1) % MaxQuads;
            if (l.Gen[slot] != Empty) l.Alive[l.Gen[slot]]--;
            l.Gen[slot] = (byte)_cur;
            l.Alive[_cur]++;
            Vector2 c2 = b.Centre;
            Vector3 lift = normal * (Lift + 0.000012f * slot);
            Vector3 centre = origin + along * c2.x + Vector3.up * c2.y;
            l.Centre[slot] = centre;
            l.Cell[slot] = (byte)b.Cell;
            float r = Mathf.Max((b.C - b.A).magnitude, (b.D - b.B).magnitude) * 0.5f;
            l.Radius[slot] = r;
            if (!l.Verts.IsCreated) return;
            Transform space = l.Room ? null : l.Go.transform;
            Vector3 pa = origin + along * b.A.x + Vector3.up * b.A.y + lift, pb = origin + along * b.B.x + Vector3.up * b.B.y + lift;
            Vector3 pc = origin + along * b.C.x + Vector3.up * b.C.y + lift, pd = origin + along * b.D.x + Vector3.up * b.D.y + lift;
            Vector3 cc = centre + lift, n = normal, down = Vector3.down;
            if (space != null)
            {
                pa = space.InverseTransformPoint(pa); pb = space.InverseTransformPoint(pb); pc = space.InverseTransformPoint(pc); pd = space.InverseTransformPoint(pd);
                cc = space.InverseTransformPoint(cc);
                n = space.InverseTransformDirection(n);
                down = space.InverseTransformDirection(down);
            }
            Color pig = b.Colour;
            var col = new Color32((byte)(Mathf.Clamp01(pig.r) * 255f), (byte)(Mathf.Clamp01(pig.g) * 255f), (byte)(Mathf.Clamp01(pig.b) * 255f), (byte)(Mathf.Clamp01(b.Alpha) * 255f));
            float birth = _time + b.Delay, seed = WallCanvas.Rand((uint)(slot * 7919 + l.Next), 3);
            int mode = (int)b.Mode;
            Vector4 animTop, animBottom, anim2Top, anim2Bottom;
            switch (b.Mode)
            {
                case BrushMode.Fall:
                {
                    // every corner drifts down by the same length (the shader eases it to a stop)
                    Vector3 disp = down * b.P1;
                    animTop = animBottom = new Vector4(_cur, mode, disp.x, disp.y);
                    anim2Top = anim2Bottom = new Vector4(disp.z, b.Dur, 0f, seed);
                    break;
                }
                case BrushMode.Grow:
                    animTop = animBottom = new Vector4(_cur, mode, b.Dur, b.P1);
                    anim2Top = anim2Bottom = new Vector4(b.P2, 0f, 0f, seed);
                    break;
                default:   // Wipe, Band: p0 the seconds, p1 the hue; the wipe coordinate per corner below
                    animTop = animBottom = new Vector4(_cur, mode, b.Dur, b.P1);
                    anim2Top = anim2Bottom = new Vector4(0f, 0f, 0f, seed);
                    break;
            }
            bool wipe = b.Mode == BrushMode.Wipe || b.Mode == BrushMode.Band;
            var centre4 = new Vector4(cc.x, cc.y, cc.z, 0f);
            int v = slot * 4;
            // A, B: the bottom corners; B, C the end of a stroke piece (wipe 1)
            l.Verts[v + 0] = new Vert { Pos = pa, Normal = n, Color = col, Uv = new Vector4(b.UvA.x, b.UvA.y, b.Cell, birth), Anim = animBottom, Anim2 = Wiped(anim2Bottom, wipe, 0f), Centre = centre4 };
            l.Verts[v + 1] = new Vert { Pos = pb, Normal = n, Color = col, Uv = new Vector4(b.UvB.x, b.UvB.y, b.Cell, birth), Anim = animBottom, Anim2 = Wiped(anim2Bottom, wipe, 1f), Centre = centre4 };
            l.Verts[v + 2] = new Vert { Pos = pc, Normal = n, Color = col, Uv = new Vector4(b.UvC.x, b.UvC.y, b.Cell, birth), Anim = animTop, Anim2 = Wiped(anim2Top, wipe, 1f), Centre = centre4 };
            l.Verts[v + 3] = new Vert { Pos = pd, Normal = n, Color = col, Uv = new Vector4(b.UvD.x, b.UvD.y, b.Cell, birth), Anim = animTop, Anim2 = Wiped(anim2Top, wipe, 0f), Centre = centre4 };
            if (slot < l.DirtyMin) l.DirtyMin = slot;
            if (slot > l.DirtyMax) l.DirtyMax = slot;
            Vector3 lc = space != null ? cc : centre;
            float e = r + 0.02f + (b.Mode == BrushMode.Fall ? b.P1 : 0f);
            l.BoundsMin = Vector3.Min(l.BoundsMin, lc - new Vector3(e, e, e));
            l.BoundsMax = Vector3.Max(l.BoundsMax, lc + new Vector3(e, e, e));
        }

        static Vector4 Wiped(Vector4 a, bool wipe, float w) { if (wipe) a.z = w; return a; }

        void Collapse(int generation)
        {
            CollapseIn(RoomLayer, generation);
            CollapseIn(MrLayer, generation);
        }

        static void CollapseIn(Layer l, int generation)
        {
            if (l.Alive[generation] == 0) return;
            for (int s = 0; s < MaxQuads; s++)
            {
                if (l.Gen[s] != generation) continue;
                l.Gen[s] = Empty;
                if (!l.Verts.IsCreated) continue;
                var none = new Vert();
                for (int k = 0; k < 4; k++) l.Verts[s * 4 + k] = none;
                if (s < l.DirtyMin) l.DirtyMin = s;
                if (s > l.DirtyMax) l.DirtyMax = s;
            }
            l.Alive[generation] = 0;
            if (l.Count == 0)
            {
                l.BoundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                l.BoundsMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            }
        }

        void Upload(Layer l, bool shownHere)
        {
            if (l.Mesh == null) return;
            if (l.DirtyMax >= l.DirtyMin)
            {
                int from = l.DirtyMin * 4, count = (l.DirtyMax - l.DirtyMin + 1) * 4;
                l.Mesh.SetVertexBufferData(l.Verts, from, from, count, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                if (l.BoundsMin.x <= l.BoundsMax.x) l.Mesh.bounds = new Bounds((l.BoundsMin + l.BoundsMax) * 0.5f, l.BoundsMax - l.BoundsMin);
                l.DirtyMin = int.MaxValue;
                l.DirtyMax = -1;
            }
            bool on = shownHere && l.Count > 0 && _atlas != null && (_fade[0] > 0f || _fade[1] > 0f);
            if (l.Renderer.enabled != on) l.Renderer.enabled = on;
            if (on) l.Uniforms(_time, _fade[0], _fade[1]);
        }

        // ---------------------------------------------------------------- Sunny

        void AskSunny(float dt)
        {
            if (!_tipDone)
            {
                bool shown = Splashes > 0 && State == Phase.Painting;
                _shownFor = shown ? _shownFor + dt : 0f;
                if (_shownFor >= TipAfter && _time >= _tipAskAt)
                {
                    _tipAskAt = _time + TipEvery;
                    if (Ask(TipId)) _tipDone = true;
                }
            }
            else if (!_throwTipDone && State == Phase.Painting && _phaseTime >= ThrowTipAfter && Splashes > 0 && _time >= _throwAskAt)
            {
                _throwAskAt = _time + 2f;
                if (Ask(ThrowTipId)) _throwTipDone = true;
            }
            if (!_galleryTipDone && _time < _galleryTipUntil && _time >= _galleryAskAt)
            {
                _galleryAskAt = _time + 1f;
                if (Ask(GalleryTipId)) _galleryTipDone = true;
            }
            if (!_keepTipDone && (State == Phase.Holding || State == Phase.Fading) && Splashes > 0 && _time >= _keepAskAt && !Keep)
            {
                _keepAskAt = _time + TipEvery;
                if (Ask(KeepTipId)) _keepTipDone = true;
            }
        }

        bool Ask(string id)
        {
            if (_guide == null) _guide = _app.Get<IGuide>();
            if (_guide == null) return false;
            return PlaybackTips.Ask(_guide, id);   // its turn among the show's lines (PlaybackTips)
        }

        public void Destroy()
        {
            _app.Events.Unsubscribe(_onNote);
            _app.Events.Unsubscribe(_onPlayback);
            _app.Events.Unsubscribe(_onState);
            _app.Events.Unsubscribe(_onLayout);
            _app.Events.Unsubscribe(_onArranged);
            _app.Events.Unsubscribe(_onMove);
            _app.Events.Unsubscribe(_onMr);
            Gallery.Destroy();
            DestroyLayer(RoomLayer);
            DestroyLayer(MrLayer);
            for (int i = 0; i < Blobs; i++)
            {
                if (_blobs[i].Material != null) _app.Materials.Destroy(_blobs[i].Material);
                if (_blobs[i].Go != null) UnityEngine.Object.Destroy(_blobs[i].Go);
            }
            if (_blobMesh != null) UnityEngine.Object.Destroy(_blobMesh);
            if (_atlas != null) UnityEngine.Object.Destroy(_atlas);
        }

        void DestroyLayer(Layer l)
        {
            if (l.Verts.IsCreated) l.Verts.Dispose();
            if (l.Material != null) _app.Materials.Destroy(l.Material);
            if (l.GatherMaterial != null) _app.Materials.Destroy(l.GatherMaterial);
            if (l.Mesh != null) UnityEngine.Object.Destroy(l.Mesh);
            if (l.Go != null) UnityEngine.Object.Destroy(l.Go);
        }
    }
}
