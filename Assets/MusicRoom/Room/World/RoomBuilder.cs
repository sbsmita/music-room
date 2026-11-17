// The room and its decor (fireplace, sofa, paintings, candles, plants, blobs), built in the right-handed source frame
// with the spec's numbers, then mirrored once into Unity (spec 07 sections 0-6). Static geometry is merged per material (one
// renderer each, spec 07 section 11): the three canvases share the scenery atlas, the loveseat's fabrics one palette
// material (SofaPalette), all six plants' stems one renderer and their leaves another (both variants in one atlas;
// the sway runs in the MusicRoom/RoomLit vertex stage (_SWAY) about each plant's base, stored per vertex), the candle holders
// join the brass and the wicks the soot (SetCandles swaps those two meshes for copies without them). The animated parts
// keep their own frames: the window (snow field, frost, sill mound), the fireplace (flames, coals, glow, smoke,
// embers) and the candles' wax and flames with their lights. Room total: 34 renderers (37 while snow shows); the
// instruments' contact shadows, the piano's included, are RigShadows' one mesh outside the room root (it follows the rigs,
// and hides the piano's with the room). Deliberate choices
// (ARCHITECTURE.md section 0, spec 07): the floor covers the full 6.4 x 5.2 m (not turned 90 degrees), walls and
// bricks use world-scale UVs, no pendant lamp (it would sit under the floor), pots stay still while their plants sway, the
// piano's contact shadow follows it. Every piece of furniture is measured while it is built (Footprint, Furniture.cs):
// the room publishes the real boxes as IRoomFurniture (platform.md 4.1).
// The room faces the fire (2026-11-11): everything is built in the ROOM FRAME with these numbers (the fireplace at Unity
// z -4.08 of the room frame) and the root's local pose is Core's RoomFrame (a half turn about the seat), so the player at the
// world origin, facing +z, sees the fire 3.58 m ahead and the whole room in front (the room slid 0.5 m back as well). Transforms under the root are set in
// local (room-frame) coordinates. Only the bench and the seat's contact shadow stay at the seat: they are built through
// SeatFix (the inverse turn), so they land exactly where they always stood.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Room.Decor;
using MusicRoom.Room.Geometry;
using MusicRoom.Room.Render;
using MusicRoom.Room.Textures;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room.World
{
    /// <summary>The room constants (source frame) and the Unity room box.</summary>
    public static class RoomDims
    {
        public const float W = 6.4f, D = 5.2f, H = 2.9f, ZBack = -1.12f, ZFront = ZBack + D, Cz = (ZBack + ZFront) / 2f;
        public const float WallX = W / 2f, WinW = 1.8f, WinH = 1.7f, WinY = 1.55f, WinZ = Cz - 0.3f;
        public const float FireX = 0.3f;
        /// <summary>The window frame in ROOM-frame Unity coordinates: (3.18, 1.55, -1.18), yaw +90 (local +z outward). In the world
        /// (the room turned to face the fire) it is WindowWorld: read RoomScene.Window for the pose.</summary>
        public static readonly Vector3 WindowUnity = new Vector3(WallX - 0.02f, WinY, -WinZ);
        /// <summary>The window frame's centre in the world: RoomFrame.ToWorld(WindowUnity) = (-3.18, 1.55, 0.68), the left wall.</summary>
        public static Vector3 WindowWorld => RoomFrame.ToWorld(WindowUnity);
        /// <summary>Floor tiling (spec 07 section 2): planks along world x, u = -z / 2.1333, v = x / 1.7333.</summary>
        public const float FloorTileZ = 6.4f / 3f, FloorTileX = 5.2f / 3f;
        /// <summary>Wall plaster tile: 1.6 m across, .725 m up (the back wall's look).</summary>
        public const float WallTileU = 1.6f, WallTileV = 0.725f;
        /// <summary>Brick tile .83 x .82 m (the chimney breast's look).</summary>
        public const float BrickTileU = 0.83f, BrickTileV = 0.82f;
    }

    /// <summary>What the module keeps of the built room.</summary>
    public sealed class RoomScene
    {
        public Transform Root, Window, Fireplace, Candles;
        public Renderer SnowField, Frost, Mound;
        public Transform MoundTransform;
        public SnowField Field;
        public Material SkyMaterial, GroundMaterial, FrostMaterial, SnowMaterial;
        public Light Moon, FloorLamp, FireLight;
        public readonly List<Light> CandleLights = new List<Light>(2);
        public readonly List<FlickerLight> Flicker = new List<FlickerLight>(3);
        /// <summary>The six plants' sway frames (the shader's per-vertex data; PlantSway.Apply is its CPU mirror).</summary>
        public readonly List<PlantSway> Plants = new List<PlantSway>(6);
        /// <summary>The flower arrangements' sway frames (their pivot is the vase mouth; FlowerBuilder) and where a butterfly
        /// lands: the top of every flower head, in WORLD coordinates (the room turned: RoomFrame.ToWorld of the room-frame point).</summary>
        public readonly List<PlantSway> Bouquets = new List<PlantSway>(4);
        public readonly List<Vector3> FlowerPerches = new List<Vector3>(24);
        /// <summary>The brass and soot meshes with and without the candle holders / wicks (SetCandles swaps them).</summary>
        public Mesh BrassWithCandles, BrassWithoutCandles, SootWithCandles, SootWithoutCandles;
        public readonly List<GameObject> CandleObjects = new List<GameObject>(4);
        public ReflectionProbe Probe;
        public readonly List<Mesh> Meshes = new List<Mesh>(64);
        public readonly Dictionary<RoomMat, Renderer> Static = new Dictionary<RoomMat, Renderer>();
        public ParticleSystem Smoke, Embers;
        public int StaticTriangles;
        /// <summary>The furniture as built (Furniture.cs): one footprint per piece, measured from its own vertices.</summary>
        public readonly List<Footprint> Footprints = new List<Footprint>(20);
    }

    public sealed class RoomBuilder
    {
        const float PI = Mathf.PI;
        readonly RoomMaterials _mats;
        readonly Geo[] _batch = new Geo[(int)RoomMat.Count];
        readonly RoomScene _s = new RoomScene();
        Mulberry32 _rng;

        public RoomBuilder(RoomMaterials mats, uint sessionSeed)
        {
            _mats = mats;
            _rng = new Mulberry32(sessionSeed);
        }

        Geo B(RoomMat k) => _batch[(int)k] ?? (_batch[(int)k] = new Geo());

        /// <summary>The source-frame matrix that undoes the room's turn (RoomFrame): a piece placed by SeatFix * m stands at m
        /// in the WORLD although it is built under the turned root (the bench and the seat's contact shadow).</summary>
        public static Matrix4x4 SeatFix
        {
            get
            {
                Matrix4x4 mirror = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
                Quaternion inv = Quaternion.Inverse(RoomFrame.Rotation);
                return mirror * Matrix4x4.TRS(inv * -RoomFrame.Position, inv, Vector3.one) * mirror;
            }
        }
        void Add(RoomMat k, Geo g, Matrix4x4 m)
        {
            B(k).Append(g, m);
            for (int i = 0; i < _open.Count; i++) _open[i].Extend(g, m);
        }

        // Footprints (Furniture.cs): every vertex Add appends while one is open grows its box, in the piece's own frame.
        readonly List<Footprint> _open = new List<Footprint>(2);
        Footprint Begin(string name, Matrix4x4 frame, bool decor = false)
        {
            var f = new Footprint(name, frame, decor);
            _open.Add(f);
            _s.Footprints.Add(f);
            return f;
        }
        void End(Footprint f) => _open.Remove(f);
        void Resume(Footprint f) { if (!_open.Contains(f)) _open.Add(f); }
        void Box(RoomMat k, Matrix4x4 parent, float w, float h, float d, float x, float y, float z) =>
            Add(k, PrimitiveMeshes.Box(w, h, d), parent * Rh.T(x, y, z));
        void RBox(RoomMat k, Matrix4x4 parent, float w, float h, float d, float x, float y, float z, float r) =>
            Add(k, PrimitiveMeshes.RoundedBox(w, h, d, 2, Mathf.Min(r, w / 2f, h / 2f, d / 2f)), parent * Rh.T(x, y, z));
        /// <summary>A loveseat part: a rounded box with its UVs shifted into the part's palette quadrant.</summary>
        void SofaBox(SofaPalette.Part part, Matrix4x4 parent, float w, float h, float d, float x, float y, float z, float r) =>
            Add(RoomMat.Sofa, PrimitiveMeshes.RoundedBox(w, h, d, 2, Mathf.Min(r, w / 2f, h / 2f, d / 2f)).RemapUv(Vector2.one, SofaPalette.Offset(part)), parent * Rh.T(x, y, z));
        static Matrix4x4 Rx(float a) => Matrix4x4.Rotate(Rh.Euler(a, 0f, 0f));
        static Matrix4x4 Ry(float a) => Matrix4x4.Rotate(Rh.Euler(0f, a, 0f));

        public RoomScene Build(Transform parent)
        {
            var root = new GameObject("Room").transform;
            root.SetParent(parent, false);
            root.SetLocalPositionAndRotation(RoomFrame.Position, RoomFrame.Rotation);   // the room faces the fire
            _s.Root = root;
            Shell();
            Window(root);
            Furniture();
            Fireplace(root);
            Candles(root);
            Plants(root);
            Flowers();
            Blobs();
            Lights(root);
            EmitStatic(root);
            return _s;
        }

        // ------------------------------------------------------------------ shell

        void Shell()
        {
            const float W = RoomDims.W, D = RoomDims.D, H = RoomDims.H, cz = RoomDims.Cz;
            // Floor: the full W x D (turned 90 degrees it would leave 0.6 m strips bare).
            Add(RoomMat.Floor, PrimitiveMeshes.Plane(W, D), Rh.TRS(new Vector3(0f, 0f, cz), Rh.Euler(-PI / 2f, 0f, 0f)));
            Add(RoomMat.Ceiling, PrimitiveMeshes.Plane(W, D), Rh.TRS(new Vector3(0f, H, cz), Rh.Euler(PI / 2f, 0f, 0f)));
            Wall(W, H, 0f, H / 2f, RoomDims.ZBack, 0f);
            Wall(W, H, 0f, H / 2f, RoomDims.ZFront, PI);
            Wall(D, H, -W / 2f, H / 2f, cz, PI / 2f);
            float wx = RoomDims.WallX, winW = RoomDims.WinW, winH = RoomDims.WinH, winY = RoomDims.WinY, winZ = RoomDims.WinZ;
            Wall(winZ - winW / 2f - RoomDims.ZBack, H, wx, H / 2f, (RoomDims.ZBack + winZ - winW / 2f) / 2f, -PI / 2f);
            Wall(RoomDims.ZFront - (winZ + winW / 2f), H, wx, H / 2f, (RoomDims.ZFront + winZ + winW / 2f) / 2f, -PI / 2f);
            Wall(winW, H - (winY + winH / 2f), wx, (H + winY + winH / 2f) / 2f, winZ, -PI / 2f);
            Wall(winW, winY - winH / 2f, wx, (winY - winH / 2f) / 2f, winZ, -PI / 2f);
            // Rug.
            Add(RoomMat.Rug, PrimitiveMeshes.Plane(4.6f, 3.2f), Rh.TRS(new Vector3(0f, 0.004f, 0.55f), Rh.Euler(-PI / 2f, 0f, 0f)));
            // Outside: the night sky far away and the ground.
            Add(RoomMat.Sky, PrimitiveMeshes.Plane(40f, 11f), Rh.TRS(new Vector3(wx + 14f, 4.5f, winZ + 3f), Rh.Euler(0f, -PI / 2f, 0f)));
            Add(RoomMat.Ground, PrimitiveMeshes.Plane(60f, 60f), Rh.TRS(new Vector3(wx + 20f, -0.05f, winZ), Rh.Euler(-PI / 2f, 0f, 0f)));
        }

        void Wall(float w, float h, float x, float y, float z, float ry) =>
            Add(RoomMat.Wall, PrimitiveMeshes.Plane(w, h), Rh.TRS(new Vector3(x, y, z), Rh.Euler(0f, ry, 0f)));

        // ------------------------------------------------------------------ window

        void Window(Transform root)
        {
            float winW = RoomDims.WinW, winH = RoomDims.WinH;
            Matrix4x4 win = Rh.TRS(new Vector3(RoomDims.WallX - 0.02f, RoomDims.WinY, RoomDims.WinZ), Rh.Euler(0f, -PI / 2f, 0f));
            float[,] bars =
            {
                { winW + 0.1f, 0.07f, 0f, winH / 2f + 0.035f }, { winW + 0.1f, 0.07f, 0f, -winH / 2f - 0.035f },
                { 0.07f, winH + 0.14f, winW / 2f + 0.035f, 0f }, { 0.07f, winH + 0.14f, -winW / 2f - 0.035f, 0f },
                { 0.04f, winH, 0f, 0f }, { winW, 0.04f, 0f, winH * 0.12f },
            };
            Footprint frame = Begin(RoomFurniture.WindowFrame, win);
            for (int i = 0; i < bars.GetLength(0); i++) Box(RoomMat.Trim, win, bars[i, 0], bars[i, 1], 0.08f, bars[i, 2], bars[i, 3], 0f);
            End(frame);
            Footprint sill = Begin(RoomFurniture.InsideSill, win);
            Box(RoomMat.Trim, win, winW + 0.3f, 0.04f, 0.2f, 0f, -winH / 2f - 0.09f, 0.08f);                 // inside sill (top y .63)
            End(sill);
            Resume(frame);
            Add(RoomMat.Glass, PrimitiveMeshes.Plane(winW, winH), win);
            End(frame);
            float sillTop = -winH / 2f - 0.028f;
            Box(RoomMat.Trim, win, winW + 0.16f, 0.035f, 0.17f, 0f, sillTop - 0.0175f, -0.04f - 0.085f);   // outside ledge

            // The frame's pivot: snow field, frost and the sill mound live in its local frame (+z outward in Unity).
            var w = new GameObject("Window").transform;
            w.SetParent(root, false);
            Rh.Apply(w, win);
            _s.Window = w;

            _s.Field = new SnowField(SnowFieldParams.Room);
            var fieldMesh = _s.Field.BuildMesh("SnowField");
            _s.Meshes.Add(fieldMesh);
            _s.SnowMaterial = _mats[RoomMat.Snow];
            _s.Field.Apply(_s.SnowMaterial);
            _s.SnowField = MakeRenderer(w, "SnowField", fieldMesh, _s.SnowMaterial);
            _s.SnowField.enabled = false;

            var frost = PrimitiveMeshes.Plane(winW, winH).ToUnityMesh("Frost");
            _s.Meshes.Add(frost);
            _s.FrostMaterial = _mats[RoomMat.Frost];
            _s.FrostMaterial.SetVector("_Half", new Vector4(winW / 2f, winH / 2f, 0f, 0f));
            _s.FrostMaterial.SetFloat("_Mull", 0.02f);
            _s.FrostMaterial.SetVector("_Transom", new Vector4(winH * 0.12f, 1f, 0f, 0f));
            _s.FrostMaterial.SetFloat("_Reach", 0.15f);
            _s.FrostMaterial.SetFloat(RoomMaterials.Veil, 0f);
            _s.Frost = MakeRenderer(w, "Frost", frost, _s.FrostMaterial, new Vector3(0f, 0f, -0.004f));
            _s.Frost.enabled = false;

            var mound = SillMound.Build(winW + 0.12f, 0.155f, 0.065f).ToUnityMesh("SillMound");
            _s.Meshes.Add(mound);
            _s.Mound = MakeRenderer(w, "SillMound", mound, _mats[RoomMat.Mound], new Vector3(0f, sillTop, 0.04f));
            _s.MoundTransform = _s.Mound.transform;
            _s.MoundTransform.localScale = new Vector3(1f, 0.001f, 1f);
            _s.Mound.enabled = false;

            // Curtains beside the window (10-upgrade room.md 12): in the fabric and brass batches, no new renderer.
            Geo fabric = B(RoomMat.Fabric);
            int curtains0 = fabric.VertexCount;
            Curtains.Build(fabric, B(RoomMat.Brass));
            MeasureCurtains(fabric, curtains0, fabric.VertexCount);

            // Trailing ivy on the inside sill.
            Plant(root, PlantBuilder.Ivy, 106, win * Rh.T(0.65f, -winH / 2f - 0.07f, 0.12f), "Ivy", "ivy on the sill", decor: true);
            _s.SkyMaterial = _mats[RoomMat.Sky];
            _s.GroundMaterial = _mats[RoomMat.Ground];
        }

        /// <summary>The two curtain panels from their own vertices in the fabric batch: Curtains.Build appends two panels of the
        /// same vertex count (the far one, beyond the window from the seat, first).</summary>
        void MeasureCurtains(Geo fabric, int from, int to)
        {
            int half = (to - from) / 2;
            if (half <= 0) return;
            float za = 0f, zb = 0f;
            for (int i = from; i < from + half; i++) za += fabric.P[i].z;
            for (int i = from + half; i < to; i++) zb += fabric.P[i].z;
            bool firstNear = Mathf.Abs(za / half) < Mathf.Abs(zb / (to - from - half));   // the seat is at z = 0
            var a = new Footprint(firstNear ? RoomFurniture.CurtainNear : RoomFurniture.CurtainFar, Matrix4x4.identity);
            var b = new Footprint(firstNear ? RoomFurniture.CurtainFar : RoomFurniture.CurtainNear, Matrix4x4.identity);
            a.ExtendWorld(fabric, from, from + half);
            b.ExtendWorld(fabric, from + half, to);
            _s.Footprints.Add(a);
            _s.Footprints.Add(b);
        }

        // ------------------------------------------------------------------ furniture

        void Furniture()
        {
            const float W = RoomDims.W, cz = RoomDims.Cz, zBack = RoomDims.ZBack, zFront = RoomDims.ZFront, fx = RoomDims.FireX;
            // Bench at the seat: built through SeatFix, so it stays at the seat (world) while the room turns about it.
            Matrix4x4 bench = SeatFix * Rh.T(0f, 0f, 0.1f);
            Footprint fp = Begin(RoomFurniture.Bench, bench);
            Box(RoomMat.WalnutGloss, bench, 0.76f, 0.05f, 0.34f, 0f, 0.47f, 0f);
            Box(RoomMat.Fabric, bench, 0.74f, 0.03f, 0.32f, 0f, 0.5f, 0f);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) Box(RoomMat.WalnutGloss, bench, 0.045f, 0.45f, 0.045f, sx * 0.34f, 0.225f, sz * 0.14f);
            End(fp);

            // Bookshelf on the left wall: books in one mesh, palette UVs.
            Matrix4x4 shelf = Rh.TRS(new Vector3(-W / 2f + 0.18f, 0f, cz + 1.2f), Rh.Euler(0f, PI / 2f, 0f));
            fp = Begin(RoomFurniture.Bookshelf, shelf);
            Box(RoomMat.Walnut, shelf, 1.1f, 1.9f, 0.02f, 0f, 0.95f, -0.15f);
            Box(RoomMat.Walnut, shelf, 0.025f, 1.9f, 0.32f, -0.54f, 0.95f, 0f);
            Box(RoomMat.Walnut, shelf, 0.025f, 1.9f, 0.32f, 0.54f, 0.95f, 0f);
            foreach (var b in Books())
            {
                Geo g = PrimitiveMeshes.Box(b.W, b.H, b.D);
                var uv = new Vector2((b.Palette + 0.5f) / 10f, 0.5f);
                for (int i = 0; i < g.UV.Count; i++) g.UV[i] = uv;
                Add(RoomMat.Books, g, shelf * Rh.T(b.X, b.Y, -0.02f));
            }
            for (int i = 0; i < 5; i++) Box(RoomMat.Walnut, shelf, 1.08f, 0.025f, 0.32f, 0f, 0.08f + i * 0.44f, 0f);
            Box(RoomMat.Walnut, shelf, 1.1f, 0.03f, 0.34f, 0f, 1.91f, 0f);
            End(fp);
            _fernParent = shelf * Rh.T(0.2f, 1.925f, 0f);

            // Gilt-framed paintings.
            Painting(Rh.T(-1.7f, 1.85f, zBack + 0.04f), 0.8f, 0.6f, 1);
            Painting(Rh.T(1.75f, 1.85f, zBack + 0.04f), 0.8f, 0.6f, 2);
            Painting(Rh.TRS(new Vector3(1.95f, 1.9f, zFront - 0.04f), Rh.Euler(0f, PI, 0f)), 1.0f, 0.7f, 3);

            // Floor lamp in the back-left corner: open shade, both faces lit.
            Matrix4x4 lamp = Rh.T(-2.4f, 0f, zBack + 0.6f);
            fp = Begin(RoomFurniture.FloorLamp, lamp);
            Box(RoomMat.Brass, lamp, 0.3f, 0.02f, 0.3f, 0f, 0.01f, 0f);
            Add(RoomMat.Brass, PrimitiveMeshes.Cylinder(0.012f, 0.012f, 1.6f, 10), lamp * Rh.T(0f, 0.8f, 0f));
            End(fp);   // the lamp's base and pole; the shade, wider, is a blocker of its own
            fp = Begin(RoomFurniture.LampShade, lamp);
            Add(RoomMat.LampShade, PrimitiveMeshes.Cylinder(0.16f, 0.22f, 0.3f, 24, 1, true).DoubleSided(), lamp * Rh.T(0f, 1.65f, 0f));
            End(fp);

            // Green loveseat angled toward the fire (sofa(1.65, 2.83, -.85, 1.25)).
            const float width = 1.25f;
            Matrix4x4 sofa = Rh.TRS(new Vector3(fx + 1.35f, 0f, zFront - 1.25f), Rh.Euler(0f, -0.85f, 0f));
            fp = Begin(RoomFurniture.Loveseat, sofa);
            float[] seats = { -0.3f, 0.3f };
            float inner = width - 0.4f;
            const SofaPalette.Part F = SofaPalette.Part.Frame, C = SofaPalette.Part.Cushion;
            SofaBox(F, sofa, width, 0.3f, 0.85f, 0f, 0.22f, 0f, 0.04f);
            SofaBox(F, sofa, width, 0.5f, 0.22f, 0f, 0.47f, -0.32f, 0.05f);
            for (int sx = -1; sx <= 1; sx += 2) SofaBox(F, sofa, 0.2f, 0.5f, 0.85f, sx * (width / 2f - 0.1f), 0.32f, 0f, 0.06f);
            float cw = inner / seats.Length - 0.03f;
            foreach (float cx in seats) SofaBox(C, sofa, cw, 0.13f, 0.6f, cx, 0.43f, 0.06f, 0.05f);
            foreach (float cx in seats) SofaBox(C, sofa, cw, 0.4f, 0.14f, cx, 0.58f, -0.2f, 0.06f);
            Add(RoomMat.Sofa, PrimitiveMeshes.RoundedBox(0.34f, 0.34f, 0.11f, 2, 0.05f).RemapUv(Vector2.one, SofaPalette.Offset(SofaPalette.Part.Pillow)),
                sofa * Rh.TRS(new Vector3(seats[0], 0.62f, -0.1f), Rh.Euler(-0.15f, 0f, 0.3f)));
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                Add(RoomMat.Walnut, PrimitiveMeshes.Cylinder(0.02f, 0.025f, 0.08f, 10), sofa * Rh.T(sx * (width / 2f - 0.07f), 0.04f, sz * 0.36f));
            End(fp);

            // Coffee table with a candle.
            Matrix4x4 table = Rh.T(fx + 0.55f, 0f, zFront - 0.95f);
            fp = Begin(RoomFurniture.CoffeeTable, table);
            Add(RoomMat.WalnutGloss, PrimitiveMeshes.Cylinder(0.3f, 0.3f, 0.03f, 32), table * Rh.T(0f, 0.42f, 0f));
            Add(RoomMat.WalnutGloss, PrimitiveMeshes.Cylinder(0.03f, 0.08f, 0.41f, 16), table * Rh.T(0f, 0.205f, 0f));
            End(fp);
            _candles.Add(new CandleSpec { World = table * Rh.T(0.08f, 0.435f, 0.02f), R = 0.03f, H = 0.07f, Light = true, FootprintName = "coffee table candle" });
            foreach (float mx in new[] { -0.55f, -0.2f, 0.3f })
                _candles.Add(new CandleSpec { World = Rh.T(fx + mx, 1.27f, zFront - 0.3f), R = 0.018f, H = mx == -0.2f ? 0.14f : 0.1f, Light = mx == -0.2f });
        }

        Matrix4x4 _fernParent;

        public struct Book { public int Shelf, K, Palette; public float X, Y, W, H, D; }

        /// <summary>The books of shelves 0..3, centres in shelf space (source frame), palette index (5i + 11k) % 10.</summary>
        public static List<Book> Books()
        {
            var list = new List<Book>(96);
            for (int i = 0; i < 4; i++)
            {
                float y = 0.08f + i * 0.44f;
                float x = -0.5f;
                int k = 0;
                while (x < 0.46f)
                {
                    float w = 0.025f + ((i * 7 + k * 3) % 5) * 0.006f;
                    float h = 0.22f + ((i * 3 + k * 5) % 6) * 0.03f;
                    float d = 0.22f + (k % 3) * 0.02f;
                    list.Add(new Book { Shelf = i, K = k, Palette = (i * 5 + k * 11) % 10, X = x + w / 2f, Y = y + 0.0125f + h / 2f, W = w, H = h, D = d });
                    x += w + 0.003f;
                    k++;
                    if (k % 9 == 8) x += 0.05f;
                }
            }
            return list;
        }

        /// <summary>painting(w, h, seed): gilt frame, inner frame, the canvas (its slot of the scenery atlas).</summary>
        void Painting(Matrix4x4 g, float w, float h, int seed)
        {
            RBox(RoomMat.Gilt, g, w + 0.12f, h + 0.12f, 0.05f, 0f, 0f, 0f, 0.012f);
            Box(RoomMat.Frame, g, w + 0.04f, h + 0.04f, 0.052f, 0f, 0f, 0.002f);
            var canvas = PrimitiveMeshes.Plane(w, h).RemapUv(RoomTextures.SceneryAtlasScale, RoomTextures.SceneryAtlasOffset(seed));
            Add(RoomMat.Canvas, canvas, g * Rh.T(0f, 0f, 0.036f));   // 8 mm proud of the inner frame (2 mm z-fought on the headset)
        }

        // ------------------------------------------------------------------ fireplace

        void Fireplace(Transform root)
        {
            const float ceiling = RoomDims.H, depth = 0.42f;
            Matrix4x4 g = Rh.TRS(new Vector3(RoomDims.FireX, 0f, RoomDims.ZFront), Rh.Euler(0f, PI, 0f));   // local +z into the room
            Footprint hearth = Begin(RoomFurniture.Hearth, g);   // the whole fireplace: bricks, firebox, hearth stone, mantel, logs
            Box(RoomMat.Brick, g, 0.38f, 1.2f, depth, -0.64f, 0.6f, depth / 2f);
            Box(RoomMat.Brick, g, 0.38f, 1.2f, depth, 0.64f, 0.6f, depth / 2f);
            Box(RoomMat.Brick, g, 1.66f, 0.42f, depth, 0f, 0.99f, depth / 2f);
            Box(RoomMat.Brick, g, 1.66f, ceiling - 1.26f, 0.3f, 0f, 1.26f + (ceiling - 1.26f) / 2f, 0.15f);
            Box(RoomMat.Soot, g, 1.0f, 0.8f, 0.05f, 0f, 0.4f, 0.03f);                    // firebox back
            Box(RoomMat.Soot, g, 0.05f, 0.8f, depth - 0.06f, -0.47f, 0.4f, depth / 2f); // firebox sides
            Box(RoomMat.Soot, g, 0.05f, 0.8f, depth - 0.06f, 0.47f, 0.4f, depth / 2f);
            Box(RoomMat.Stone, g, 1.9f, 0.03f, 0.75f, 0f, 0.015f, 0.36f);               // hearth
            Box(RoomMat.Stone, g, 1.9f, 0.07f, 0.55f, 0f, 1.235f, 0.28f);               // mantel, top at 1.27
            float[,] logs = { { -0.12f, 0.085f, 0.24f, 0.35f }, { 0.12f, 0.085f, 0.2f, -0.3f }, { 0f, 0.17f, 0.22f, 0.05f } };
            for (int i = 0; i < 3; i++)
                Add(RoomMat.Log, PrimitiveMeshes.Cylinder(0.045f, 0.05f, 0.52f, 10), g * Rh.TRS(new Vector3(logs[i, 0], logs[i, 1], logs[i, 2]), Rh.Euler(0f, logs[i, 3], PI / 2f)));
            End(hearth);

            var f = new GameObject("Fireplace").transform;
            f.SetParent(root, false);
            Rh.Apply(f, g);
            _s.Fireplace = f;

            // Coals: a quad facing up under the logs.
            var coals = new Geo();
            coals.Append(PrimitiveMeshes.Plane(0.7f, 0.5f), Rh.TRS(new Vector3(0f, 0.035f, 0.22f), Rh.Euler(-PI / 2f, 0f, 0f)));
            MakeRenderer(f, "Coals", Keep(coals.ToUnityMesh("Coals")), _mats[RoomMat.Coals]);

            // Cavity glow: a sprite, drawn as a camera-facing quad (size and pulse in the material).
            var quad = PrimitiveMeshes.Plane(1f, 1f).ToUnityMesh("GlowQuad");
            quad.bounds = new Bounds(Vector3.zero, new Vector3(1.2f, 1.2f, 1.2f));
            MakeRenderer(f, "Glow", Keep(quad), _mats[RoomMat.Glow], Rh.ToUnity(new Vector3(0f, 0.34f, 0.1f)));

            // The three fires as one mesh: 3 + 3 + 2 crossed quads, seed and intensity per vertex (spec 07 section 4.2).
            var flames = new Geo();
            AddFire(flames, Matrix4x4.Translate(new Vector3(0.02f, 0.07f, 0.22f)), 0.5f, 0.72f, 3, 2f, 1.15f);
            AddFire(flames, Rh.TRS(new Vector3(-0.17f, 0.09f, 0.17f), Rh.Euler(0f, 0.5f, 0f)), 0.3f, 0.46f, 3, 7f, 1.05f);
            AddFire(flames, Rh.TRS(new Vector3(0.18f, 0.1f, 0.26f), Rh.Euler(0f, -0.4f, 0f)), 0.26f, 0.38f, 2, 11f, 1.0f);
            MakeRenderer(f, "Flames", Keep(flames.ToUnityMesh("Flames")), _mats[RoomMat.Flame]);

            _s.Smoke = FireParticles.Create(f, FireParticles.Smoke, _mats[RoomMat.Smoke], (uint)(_rng.Next() * 1e9));
            _s.Embers = FireParticles.Create(f, FireParticles.Embers, _mats[RoomMat.Ember], (uint)(_rng.Next() * 1e9));

            // Fire light: #ff8f3c 4.2 / d 6 / decay 1.6 -> URP 2.07 range 6, flicker .22, at local (0, .5, .45).
            _s.FireLight = PointLight(root, "FireLight", "#ff8f3c", 2.0747f, 6f, Rh.ToUnity(g.MultiplyPoint3x4(new Vector3(0f, 0.5f, 0.45f))));
            _s.Flicker.Add(new FlickerLight(_s.FireLight, 2.0747f, 0.22f, (float)_rng.Next() * 6.28f));
        }

        /// <summary>makeFire: `planes` quads w x h with the base at the origin, yawed by i/planes pi, seed + 3.7 i.</summary>
        internal static void AddFire(Geo into, Matrix4x4 group, float w, float h, int planes, float seed, float intensity)
        {
            for (int i = 0; i < planes; i++)
            {
                var q = PrimitiveMeshes.Plane(w, h).Translate(0f, h / 2f, 0f).WithUv1(new Vector2(seed + i * 3.7f, intensity));
                into.Append(q, group * Ry((float)i / planes * PI));
            }
        }

        // ------------------------------------------------------------------ candles

        struct CandleSpec { public Matrix4x4 World; public float R, H; public bool Light; public string FootprintName; }
        readonly List<CandleSpec> _candles = new List<CandleSpec>(4);
        /// <summary>The candle holders (brass) and wicks (soot), merged into those static meshes in EmitStatic.</summary>
        readonly Geo _candleBrass = new Geo(), _candleSoot = new Geo();

        void Candles(Transform root)
        {
            var group = new GameObject("Candles").transform;
            group.SetParent(root, false);
            _s.Candles = group;
            Geo holders = _candleBrass, wax = new Geo(), wicks = _candleSoot, flames = new Geo();
            foreach (var c in _candles)
            {
                float seed = (float)_rng.Next() * 20f;
                // A candle standing on a blocker (the coffee table's) is measured as decor (Furniture.cs).
                Footprint fp = c.FootprintName != null ? new Footprint(c.FootprintName, c.World, decor: true) : null;
                if (fp != null) _s.Footprints.Add(fp);
                Geo holder = PrimitiveMeshes.Cylinder(c.R * 1.8f, c.R * 2.1f, 0.008f, 20).Translate(0f, 0.004f, 0f);
                Geo body = PrimitiveMeshes.Cylinder(c.R, c.R, c.H, 16).Translate(0f, 0.008f + c.H / 2f, 0f);
                Geo wick = PrimitiveMeshes.Cylinder(0.0015f, 0.0015f, 0.012f, 5).Translate(0f, 0.008f + c.H + 0.005f, 0f);
                holders.Append(holder, c.World);
                wax.Append(body, c.World);
                wicks.Append(wick, c.World);
                fp?.Extend(holder, c.World);
                fp?.Extend(body, c.World);
                for (int i = 0; i < 2; i++)
                {
                    var fg = PrimitiveMeshes.Plane(0.028f, 0.075f).Translate(0f, 0.075f / 2f, 0f).RotateY(i / 2f * PI)
                        .Translate(0f, 0.008f + c.H + 0.002f, 0f).WithUv1(new Vector2(seed + i * 3.7f, 1.8f));
                    flames.Append(fg, c.World);
                    fp?.Extend(fg, c.World);
                }
                if (c.Light)
                {
                    // #ffc070 .45 / d 1.8 / decay 1.8 -> URP .13 range 1.8, flicker .3, at the wick + (0, h + .08, 0).
                    Vector3 at = Rh.ToUnity(c.World.MultiplyPoint3x4(new Vector3(0f, c.H + 0.08f, 0f)));
                    var l = PointLight(group, "CandleLight", "#ffc070", 0.1293f, 1.8f, at);
                    _s.CandleLights.Add(l);
                    _s.Flicker.Add(new FlickerLight(l, 0.1293f, 0.3f, (float)_rng.Next() * 6.28f));
                }
            }
            // The holders and wicks join the static brass and soot meshes (EmitStatic); wax and flames stay here.
            _s.CandleObjects.Add(MakeRenderer(group, "CandleWax", Keep(wax.ToUnityMesh("CandleWax")), _mats[RoomMat.Wax]).gameObject);
            _s.CandleObjects.Add(MakeRenderer(group, "CandleFlames", Keep(flames.ToUnityMesh("CandleFlames")), _mats[RoomMat.CandleFlame]).gameObject);
        }

        // ------------------------------------------------------------------ plants

        void Plants(Transform root)
        {
            const float W = RoomDims.W, cz = RoomDims.Cz, zBack = RoomDims.ZBack, zFront = RoomDims.ZFront, fx = RoomDims.FireX;
            Plant(root, PlantBuilder.P1, 101, Rh.T(fx - 1.35f, 0f, zFront - 0.32f), "PlantP1", "plant P1");
            Plant(root, PlantBuilder.P2, 102, Rh.T(W / 2f - 0.4f, 0f, zFront - 0.4f), "PlantP2", "plant P2");
            Plant(root, PlantBuilder.P3, 103, Rh.T(RoomDims.WallX - 0.38f, 0f, zBack + 0.42f), "PlantP3", "plant P3");
            Plant(root, PlantBuilder.P4, 104, Rh.T(-W / 2f + 0.32f, 0f, cz + 0.4f), "PlantP4", "plant P4");
            Plant(root, PlantBuilder.Fern, 105, _fernParent, "Fern", "fern on the bookshelf", decor: true);
        }

        /// <summary>A plant: pot and soil static; its stems and leaves join the room's one stem mesh and one leaf mesh, each
        /// vertex carrying the plant's base (Unity, TEXCOORD2) and (phase, amplitude, cos yaw, sin yaw) of the plant's frame
        /// (TEXCOORD3) for MusicRoom/RoomLit (_SWAY). The leaves sample their variant's half of the leaf atlas. Footprints
        /// (Furniture.cs): a floor plant's body (pot, soil and stems) is a blocker and its spread of leaves decor (its name
        /// plus " leaves"); a plant standing on a blocker (decor) is one footprint. Each grows by the sway at its tip:
        /// about 1.25 x the amplitude x its reach above or below the base.</summary>
        void Plant(Transform root, in PlantOptions o, uint seed, Matrix4x4 world, string name, string footprint, bool decor = false)
        {
            Footprint body = Begin(footprint, world, decor);
            PlantGeometry pg = PlantBuilder.Build(o, seed);
            Add(RoomMat.Terracotta, pg.Pot, world);
            Add(RoomMat.Soil, pg.Soil, world);
            var sway = PlantSway.From(name, world, pg.Phase, pg.Amp);
            Vector4 pivot = sway.Base, data = sway.VertexData;
            Add(RoomMat.Stem, pg.Stems.Clone().WithUv23(pivot, data), world);
            Footprint leaves = body;
            if (!decor)
            {
                End(body);
                leaves = Begin(footprint + " leaves", world, decor: true);
            }
            Add(RoomMat.Leaf, pg.Leaves.Clone().RemapUv(RoomTextures.LeafAtlasScale, RoomTextures.LeafAtlasOffset(o.Dark)).WithUv23(pivot, data), world);
            _s.Plants.Add(sway);
            End(body);
            End(leaves);
            body.Grow = SwayGrow(body, pg.Amp);
            leaves.Grow = SwayGrow(leaves, pg.Amp);
        }

        // ------------------------------------------------------------------ flowers (World/FlowerBuilder.cs)

        /// <summary>Four arrangements on the furniture, decor on their blockers (Furniture.cs): tulips and daisies on the mantel
        /// (between its candles and its ends, clear of the chimney breast), geraniums on the window sill under the mullion,
        /// cosmos on top of the bookshelf beside the fern. Vases join the static batches, stems and leaves the plants', the
        /// petals RoomMat.Petal.</summary>
        void Flowers()
        {
            // The mantel's free depth is source z 3.525..3.78 (the breast's face); the candles stand at x fx - .55, - .2, + .3.
            const float mantelZ = 3.655f, mantelTop = 1.27f, fx = RoomDims.FireX;
            Matrix4x4 tulips = Rh.TRy(fx + 0.68f, mantelTop, mantelZ, 0.3f);
            Matrix4x4 daisies = Rh.TRy(fx - 0.8f, mantelTop, mantelZ, -0.4f);
            Footprint mantel = Begin(RoomFurniture.MantelFlowers, Rh.T(fx, mantelTop, mantelZ), decor: true);
            AddBouquet(FlowerBuilder.Tulips, 211, tulips, RoomMat.Trim, "Tulips");
            AddBouquet(FlowerBuilder.Daisies, 212, daisies, RoomMat.Brass, "Daisies");
            End(mantel);
            // The inside sill: top at window-local y -winH / 2 - .07, the glass at local z 0 (Unity +x); under the mullion.
            Matrix4x4 win = Rh.TRS(new Vector3(RoomDims.WallX - 0.02f, RoomDims.WinY, RoomDims.WinZ), Rh.Euler(0f, -PI / 2f, 0f));
            Matrix4x4 sill = win * Rh.TRy(0.02f, -RoomDims.WinH / 2f - 0.07f, 0.1f, 0.5f);
            Footprint sillFp = Begin(RoomFurniture.SillFlowers, win, decor: true);
            AddBouquet(FlowerBuilder.Geraniums, 213, sill, RoomMat.Terracotta, "Geraniums");
            End(sillFp);
            // The bookshelf's top (shelf-local y 1.925), the fern at x .2: the cosmos at the other side.
            Matrix4x4 shelfTop = _fernParent * Rh.TRy(-0.5f, 0f, 0.02f, 0.9f);
            Footprint shelfFp = Begin(RoomFurniture.ShelfFlowers, _fernParent, decor: true);
            AddBouquet(FlowerBuilder.CosmosJar, 214, shelfTop, RoomMat.Stone, "Cosmos");
            End(shelfFp);
        }

        void AddBouquet(in BouquetSpec spec, uint seed, Matrix4x4 world, RoomMat vase, string name)
        {
            Bouquet b = FlowerBuilder.Build(spec, seed);
            Add(vase, b.Vase, world);
            if (b.Soil.VertexCount > 0) Add(RoomMat.Soil, b.Soil, world);
            Matrix4x4 mouth = world * Rh.T(0f, b.MouthY, 0f);
            var sway = PlantSway.From(name, mouth, b.Phase, b.Amp);
            Vector4 pivot = sway.Base;
            pivot.w = FlowerBuilder.Response;   // the flowers answer the music (_MusicSway); the plants' w is 0
            Vector4 data = sway.VertexData;
            Add(RoomMat.Stem, b.Stems.Clone().WithUv23(pivot, data), world);
            Add(RoomMat.Leaf, b.Leaves.Clone().WithUv23(pivot, data), world);
            Add(RoomMat.Petal, b.Petals.Clone().WithUv23(pivot, data), world);
            _s.Bouquets.Add(sway);
            foreach (Vector3 h in b.Heads) _s.FlowerPerches.Add(RoomFrame.ToWorld(Rh.ToUnity(world.MultiplyPoint3x4(h))));
        }

        static float SwayGrow(Footprint f, float amp) => 1.25f * amp * Mathf.Max(Mathf.Abs(f.Max.y), Mathf.Abs(f.Min.y));

        // ------------------------------------------------------------------ contact shadows and AO strips

        void Blobs()
        {
            const float W = RoomDims.W, D = RoomDims.D, cz = RoomDims.Cz, zBack = RoomDims.ZBack, zFront = RoomDims.ZFront, fx = RoomDims.FireX;
            foreach (var p in new[] { new Vector2(fx - 1.35f, zFront - 0.32f), new Vector2(W / 2f - 0.4f, zFront - 0.4f), new Vector2(RoomDims.WallX - 0.38f, zBack + 0.42f), new Vector2(-W / 2f + 0.32f, cz + 0.4f) })
                Blob(p.x, p.y, 0.6f, 0.6f, 0.45f);
            // The piano's blob (0, -.65) follows the piano (RigShadows, with every instrument's); the bench's is at the seat.
            Add(RoomMat.Decal, BlobGeo(1.0f, 0.6f, 0.4f), SeatFix * Rh.T(0f, 0.005f, 0.36f));   // the seat's, at the seat (SeatFix)
            Blob(fx + 1.35f, zFront - 1.25f, 1.7f, 1.3f, 0.5f, 0f, -0.85f);
            Blob(fx + 0.55f, zFront - 0.95f, 0.8f, 0.8f, 0.4f);
            Blob(fx, zFront - 0.3f, 2.2f, 0.9f, 0.45f);
            Blob(-W / 2f + 0.18f, cz + 1.2f, 0.6f, 1.4f, 0.5f);
            Blob(-2.4f, zBack + 0.6f, 0.5f, 0.5f, 0.35f);
            AoStrip(W, 0.55f, 0f, zBack + 0.275f, 0f);
            AoStrip(W, 0.55f, 0f, zFront - 0.275f, PI);
            AoStrip(D, 0.55f, -W / 2f + 0.275f, cz, PI / 2f);
            AoStrip(D, 0.55f, W / 2f - 0.275f, cz, -PI / 2f);
        }

        /// <summary>shadowBlob: unit plane, YXZ rotation (-pi/2, rot, 0), scale (w, d), y + .005; kind 0 (dot).</summary>
        public static Geo BlobGeo(float w, float d, float opacity, float rot = 0f)
        {
            var g = new Geo();
            g.Append(PrimitiveMeshes.Plane(1f, 1f).WithUv1(Vector2.zero).WithColor(new Color(0f, 0f, 0f, opacity)),
                Matrix4x4.TRS(Vector3.zero, Rh.Euler(-PI / 2f, rot, 0f, "YXZ"), new Vector3(w, d, 1f)));
            return g;
        }

        void Blob(float x, float z, float w, float d, float opacity, float y = 0f, float rot = 0f) =>
            Add(RoomMat.Decal, BlobGeo(w, d, opacity, rot), Rh.T(x, y + 0.005f, z));

        void AoStrip(float len, float width, float x, float z, float yaw)
        {
            var g = PrimitiveMeshes.Plane(len, width).WithUv1(new Vector2(1f, 0f)).WithColor(new Color(0f, 0f, 0f, 1f));
            Add(RoomMat.Decal, g, Rh.TRS(new Vector3(x, 0.004f, z), Rh.Euler(-PI / 2f, yaw, 0f, "YXZ")));
        }

        // ------------------------------------------------------------------ lights (spec 07 section 6)

        void Lights(Transform root)
        {
            // Moon: the URP main light, intensity 0.55 / pi (the spec's 0.55 in physical units), from (5.2, 3, 2.18) toward (.2, .6, .2), no shadows.
            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.transform.SetParent(root, false);
            moon.type = LightType.Directional;
            moon.color = TextureUtil.Hex("#a9bbff");
            moon.intensity = 0.1751f;
            moon.transform.localRotation = Quaternion.Euler(24.05f, -68.40f, 0f);   // room frame: it turns with the room
            moon.shadows = LightShadows.None;
            moon.renderMode = LightRenderMode.ForcePixel;
            _s.Moon = moon;
            // Floor lamp bulb: #ffd6a0 2.4 / d 5 / decay 1.5 -> URP 1.08 range 5 (d_ref 2).
            _s.FloorLamp = PointLight(root, "FloorLamp", "#ffd6a0", 1.0804f, 5f, Rh.ToUnity(new Vector3(-2.4f, 1.6f, RoomDims.ZBack + 0.6f)));
            // One realtime probe for the metals, rendered by script once the room is textured (the module does it).
            var probeGo = new GameObject("ReflectionProbe");
            probeGo.transform.SetParent(root, false);
            probeGo.transform.localPosition = new Vector3(0f, 1.2f, -1.48f);   // room frame (the box is symmetric about it)
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 128;
            probe.size = new Vector3(RoomDims.W, RoomDims.H, RoomDims.D);
            probe.center = new Vector3(0f, 0.25f, 0f);
            probe.nearClipPlane = 0.05f;
            probe.farClipPlane = 25f;
            probe.hdr = false;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = new Color32(0x05, 0x07, 0x0f, 0xff);
            probe.importance = 1;
            // E11 (experience.md section 12): box projection over the room's own box (6.4 x 2.9 x 5.2 m centred at
            // (0, 1.45, -1.48)); QuestURP enables box projection (RenderSetup).
            probe.boxProjection = true;
            _s.Probe = probe;
        }

        /// <summary>A point light at <paramref name="roomPos"/> (room-frame Unity coordinates: the parents under the root are
        /// at the root's origin, so this is the local position).</summary>
        static Light PointLight(Transform parent, string name, string hex, float intensity, float range, Vector3 roomPos)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(parent, false);
            l.transform.localPosition = roomPos;
            l.type = LightType.Point;
            l.color = TextureUtil.Hex(hex);
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        // ------------------------------------------------------------------ meshes

        Mesh Keep(Mesh m) { _s.Meshes.Add(m); return m; }

        internal static Renderer MakeRenderer(Transform parent, string name, Mesh mesh, Material mat, Vector3? localPos = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (localPos.HasValue) go.transform.localPosition = localPos.Value;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            string sh = mat != null && mat.shader != null ? mat.shader.name : "";
            bool lit = sh.StartsWith("Universal Render Pipeline/Lit", System.StringComparison.Ordinal) || sh == RoomTemplates.RoomLitShader;
            r.reflectionProbeUsage = lit ? ReflectionProbeUsage.BlendProbes : ReflectionProbeUsage.Off;
            return r;
        }

        /// <summary>One renderer per static material, world-scale UVs for the floor, walls and bricks.</summary>
        void EmitStatic(Transform root)
        {
            var statics = new GameObject("Static").transform;
            statics.SetParent(root, false);
            for (int k = 0; k < _batch.Length; k++)
            {
                Geo g = _batch[k];
                if (g == null || g.VertexCount == 0) continue;
                var key = (RoomMat)k;
                if (key == RoomMat.Floor) g.SetUvFromUnity((p, n) => new Vector2(-p.z / RoomDims.FloorTileZ, p.x / RoomDims.FloorTileX));
                else if (key == RoomMat.Wall) g.SetUvFromUnity((p, n) => new Vector2((Mathf.Abs(n.z) > Mathf.Abs(n.x) ? p.x : p.z) / RoomDims.WallTileU, p.y / RoomDims.WallTileV));
                else if (key == RoomMat.Brick) g.SetUvFromUnity(BrickUv);
                bool tangents = key == RoomMat.Sofa;
                Mesh mesh;
                if (key == RoomMat.Brass || key == RoomMat.Soot)
                {
                    // SetCandles: the same mesh with and without the candle holders / wicks (no extra renderer for them).
                    Geo extra = key == RoomMat.Brass ? _candleBrass : _candleSoot;
                    Mesh without = Keep(g.ToUnityMesh("Room " + key + " (no candles)"));
                    var full = g.Clone();
                    full.Append(extra);
                    mesh = Keep(full.ToUnityMesh("Room " + key));
                    if (key == RoomMat.Brass) { _s.BrassWithCandles = mesh; _s.BrassWithoutCandles = without; }
                    else { _s.SootWithCandles = mesh; _s.SootWithoutCandles = without; }
                    _s.StaticTriangles += full.I.Count / 3;
                }
                else
                {
                    mesh = Keep(g.ToUnityMesh("Room " + key, tangents));
                    _s.StaticTriangles += g.I.Count / 3;
                }
                // The plants sway a few centimetres about their bases in the vertex stage: widen their culling bounds.
                if (key == RoomMat.Stem || key == RoomMat.Leaf || key == RoomMat.Petal) { var b = mesh.bounds; b.Expand(0.1f); mesh.bounds = b; }
                var r = MakeRenderer(statics, key.ToString(), mesh, _mats[key]);
                _s.Static[key] = r;
                if (key == RoomMat.Floor)
                {
                    // The desktop mouse hand rests on the floor (MouseHandSource rays against colliders).
                    var col = r.gameObject.AddComponent<BoxCollider>();
                    col.center = new Vector3(0f, -0.01f, -RoomDims.Cz);
                    col.size = new Vector3(RoomDims.W, 0.02f, RoomDims.D);
                }
            }
        }

        /// <summary>Box projection in world scale: the two world axes of the face's plane, tile .83 x .82 m.</summary>
        internal static Vector2 BrickUv(Vector3 p, Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ax >= ay && ax >= az) return new Vector2(p.z / RoomDims.BrickTileU, p.y / RoomDims.BrickTileV);
            if (ay >= az) return new Vector2(p.x / RoomDims.BrickTileU, p.z / RoomDims.BrickTileV);
            return new Vector2(p.x / RoomDims.BrickTileU, p.y / RoomDims.BrickTileV);
        }
    }
}
