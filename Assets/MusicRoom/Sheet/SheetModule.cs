// The sheet-music module (Docs/12-new-instruments/sheet.md): an open music book on the piano's music rest showing the
// lessons' notes, the key letter under every note, a warm tint on the next note, pages that turn themselves (or when a page
// corner is tapped) and a contents spread that starts lessons. Order 45; registers no Rig and no IInstrument (platform.md
// 13.5); the book is parented under the piano's rig at the rest pose, so it moves, lifts and turns with the piano.
//   Init: the book's data and font are read, the whole book is engraved on a worker (layouts only), the meshes are built.
//   Decor phase: visibility, following the lesson, pokes (index tips) and desktop clicks, turns, the marker, Sunny's tips.
//   EndFrame: page rasters are scheduled / uploaded, the turning leaves' vertices uploaded. 0 B per frame; 2 draws.
// Everything that will come from the platform (the rest pose, the current event, the paper sound, tips, signals) goes
// through ISheetHost (PlatformHost: the platform's hooks). It also registers IGuideAnchors (Sunny's demo targets) and
// IPointerClaimant (fingertips within 30 mm in front of the pages); its tips are an IGuideTipSource (SheetTipSource).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MusicRoom.Core;
using MusicRoom.Sheet.Engrave;
using MusicRoom.Sheet.Raster;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Sheet
{
    /// <summary>Read-backs and controls of the book (tests, Sunny's anchors).</summary>
    public interface ISheetBook
    {
        bool Ready { get; }
        bool Visible { get; }
        int Spread { get; }
        bool Turning { get; }
        int MarkPage { get; }
        Vector4 MarkRect { get; }
        float MarkStrength { get; }
        int HoverRow { get; }
        Transform Root { get; }
        bool TryGetAnchor(string anchor, out Vector3 world, out Vector3 up);
        /// <summary>A manual turn (+1 forward, -1 back), as a tapped corner.</summary>
        bool Turn(int direction);
        void ToContents();
        /// <summary>A desktop click (or a test) at a world point on the book: what it did.</summary>
        PokeTarget Tap(Vector3 world);
    }

    public sealed class SheetModule : AppModule, ISheetBook, IGuideAnchors, IPointerClaimant
    {
        public const int ModuleOrder = 45;
        public override int Order => ModuleOrder;
        public const string KeySpread = "musicroom.sheet.spread";

        static readonly int s_slices0 = Shader.PropertyToID("_Slices0"), s_slices1 = Shader.PropertyToID("_Slices1"),
            s_ink0 = Shader.PropertyToID("_Ink0"), s_ink1 = Shader.PropertyToID("_Ink1"),
            s_mark = Shader.PropertyToID("_Mark"), s_markInfo = Shader.PropertyToID("_MarkInfo"), s_hover = Shader.PropertyToID("_Hover"),
            s_hoverInfo = Shader.PropertyToID("_HoverInfo"), s_leaf = Shader.PropertyToID("_Leaf"), s_lampPos = Shader.PropertyToID("_LampPos"),
            s_lampDir = Shader.PropertyToID("_LampDir"), s_lampAxis = Shader.PropertyToID("_LampAxis"), s_lampColor = Shader.PropertyToID("_LampColor"),
            s_pages = Shader.PropertyToID("_Pages"), s_detail = Shader.PropertyToID("_DetailMap"), s_texScale = Shader.PropertyToID("_TexScale"),
            s_paper = Shader.PropertyToID("_PaperColor"), s_ink = Shader.PropertyToID("_InkColor"), s_markColor = Shader.PropertyToID("_MarkColor"),
            s_metal = Shader.PropertyToID("_MetallicGlossMap");

        /// <summary>The reading lamp's analytic light (device item S2 tunes it): intensity, cone, and a warm white #FFE6C8 (the
        /// spec's #FFD9A8 turned the cream paper orange in the renders: a real lamp's paper reads cream to an adapted eye).</summary>
        public static float LampIntensity = 0.085f;
        public const float LampCosOuter = 0.30f, LampCosInner = 0.62f;
        public static readonly Color LampColour = new Color32(0xff, 0xe6, 0xc8, 0xff);

        App _app;
        public ISheetHost Host { get; private set; }
        Task<SheetBook> _build;
        public SheetBook Book { get; private set; }
        public SheetPages Pages { get; private set; }
        Dictionary<string, SheetMeta> _meta;
        float _bootStarted;

        GameObject _root;
        MeshRenderer _paperR, _caseR;
        Material _paperM, _caseM;
        Mesh _paperMesh, _caseMesh;
        NativeArray<PaperVertex> _leafVerts;
        BoxCollider _pagesClick, _ribbonClick;
        float _sBottom, _ledge;
        bool _leavesDirty, _leavesCollapsed = true;

        public readonly LeafTurn Leaf = new LeafTurn();
        public readonly SheetMarker Marker = new SheetMarker();
        public readonly SheetPokes Pokes = new SheetPokes();
        public SheetTips Tips { get; private set; }
        readonly ContentsRowSet _rows = new ContentsRowSet();
        readonly int[] _roles = new int[8];
        readonly Vector4 _slices0, _slices1;
        int _spread = 2;
        uint _seed = 1;
        bool _visible, _manualFar;

        // tutorial follow
        TutorialState _lastState;
        string _lastLesson;
        int _lastK = -1;
        ITutorial _tut;
        IArrangeMode _arrange;
        IGrabService _grab;
        IMixedReality _mr;
        IMenuPanel _menu;
        IGuide _guide;
        Rig _pianoRig;
        Camera _headCamera;

        public bool Ready { get; private set; }
        public bool Visible => _visible;
        public int Spread => _spread;
        public bool Turning => Leaf.Active;
        public int MarkPage => Marker.Value > 0f ? Marker.Page : -1;
        public Vector4 MarkRect => Marker.Rect;
        public float MarkStrength => Marker.Value;
        public int HoverRow { get; private set; } = -1;
        public Transform Root => _root != null ? _root.transform : null;
        public int ManualTurns { get; private set; }
        public int AutoTurns { get; private set; }
        public string LastTitleStarted { get; private set; }
        public Material PaperMaterial => _paperM;
        public MeshRenderer PaperRenderer => _paperR;
        public MeshRenderer CaseRenderer => _caseR;
        public int LastLeftPage => Book == null ? 2 : BookSheetMath.LastLeft(Book);

        public override void Init(App app)
        {
            _app = app;
            Host = new PlatformHost(app);
            app.Register<ISheetBook>(this);
            app.Register<IGuideAnchors>(this);
            app.Register<IPointerClaimant>(this);
            Tips = new SheetTips(Host, app.Settings);
            var bookText = Resources.Load<TextAsset>(SheetBookData.ResourceName);
            var fontBytes = Resources.Load<TextAsset>(SheetFont.ResourceName);
            if (bookText == null || fontBytes == null) { Debug.LogError("[Sheet] Resources/sheet-book.json or sheet-font.bytes missing: no book"); return; }
            var file = SheetBookData.Parse(bookText.text);
            _meta = SheetBookData.MetaTable(file);
            var songs = new List<(SheetLesson, SheetMeta)>();
            var lib = app.Get<ILessonLibrary>();
            if (lib != null)
                foreach (var l in lib.Lessons)
                {
                    if (l == null || l.instrument != "piano") continue;
                    if (_meta.TryGetValue(l.id, out var m) && m.BookSong != null) continue;   // shown on another song's page
                    if (m == null) { m = SheetBookData.MetaOf(l); _meta[l.id] = m; }      // the lesson's own meter / unit / pickup / credit
                    songs.Add((SheetBookData.FromLesson(l), m));
                }
            byte[] fb = fontBytes.bytes;
            string title = file.book?.title;
            _bootStarted = Time.realtimeSinceStartup;
            _build = Task.Run(() =>
            {
                var b = new SheetBook(SheetFont.FromBytes(fb), title);
                b.Build(songs);
                return b;
            });
            bool gpu = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && SystemInfo.copyTextureSupport != CopyTextureSupport.None;
            Pages = new SheetPages(gpu);
            _spread = Mathf.Max(2, app.Settings.GetInt(KeySpread, 2) & ~1);
            BuildView();
        }

        // ------------------------------------------------------------------------------------ the view
        void BuildView()
        {
            var parent = Host.PianoRoot;
            if (parent == null || _root != null) return;
            _ledge = Host.MusicRestLedge;
            _sBottom = _ledge + BookGeometry.Square;
            var local = Host.MusicRestLocal;
            BookGeometry.SetRest(local);
            _root = new GameObject("SheetBook");
            _root.transform.SetParent(parent, false);
            _root.transform.SetLocalPositionAndRotation(local.position, local.rotation);
            var mats = _app.Materials;
            _paperM = Material(SheetMaterialTemplates.Paper, SheetMaterialTemplates.PaperShader);
            _caseM = Material(SheetMaterialTemplates.Case, SheetMaterialTemplates.CaseShader);
            _paperM.SetTexture(s_pages, Pages.Texture);
            _paperM.SetTexture(s_detail, SheetTextures.Detail());
            _paperM.SetVector(s_texScale, new Vector4(SheetPages.Ppm / SheetPages.W, SheetPages.Ppm / SheetPages.H, 1f / SheetTextures.DetailTileMm, 1f / SheetTextures.DetailTileMm));
            _paperM.SetColor(s_paper, new Color32(0xf3, 0xea, 0xd5, 0xff).Linear());
            _paperM.SetColor(s_ink, new Color32(0x2a, 0x25, 0x21, 0xff).Linear());
            _paperM.SetColor(s_markColor, new Color32(0xf0, 0xc8, 0x84, 0xff).Linear());
            _palette = SheetTextures.CasePalette();
            _metal = SheetTextures.CaseMetal();
            _caseM.SetTexture(MaterialLibrary.BaseMap, _palette);
            _caseM.SetTexture(s_metal, _metal);
            _paperMesh = BookMesh.BuildPaper(_sBottom, out _leafVerts);
            _caseMesh = BookMesh.BuildCase(_ledge);
            _paperR = MakeRenderer("Paper", _paperMesh, _paperM);
            _caseR = MakeRenderer("Case", _caseMesh, _caseM);
            // desktop click targets (Layers.PlaySurface): the open pages, and the ribbon's tail on the top panel
            var pg = new GameObject("PagesClick") { layer = Layers.PlaySurface };
            pg.transform.SetParent(_root.transform, false);
            _pagesClick = pg.AddComponent<BoxCollider>();
            _pagesClick.center = new Vector3(0f, _sBottom + BookGeometry.PageH / 2f - 0.01f, -0.008f);
            _pagesClick.size = new Vector3(2f * (BookGeometry.WFlat + SheetPokes.CornerOut), BookGeometry.PageH + 0.02f + SheetPokes.CornerOut, 0.016f);
            _pagesClick.isTrigger = true;
            var rb = new GameObject("RibbonClick") { layer = Layers.PlaySurface };
            rb.transform.SetParent(_root.transform, false);
            BookMesh.RibbonTail(out Vector3 ta, out Vector3 tb);
            rb.transform.SetLocalPositionAndRotation((ta + tb) * 0.5f, Quaternion.Inverse(BookGeometry.RestLocalRotation));
            _ribbonClick = rb.AddComponent<BoxCollider>();
            _ribbonClick.size = new Vector3(0.03f, 0.012f, 0.045f);
            _ribbonClick.isTrigger = true;
            ApplyUniforms();
            _visible = true;
            SetVisible(false);
        }

        Material Material(string template, string shader)
        {
            if (_app.Materials.HasTemplate(template)) return _app.Materials.Clone(template);
            var sh = Shader.Find(shader);
            if (sh == null) return _app.Materials.Clone(MaterialLibrary.LitOpaque);
            Debug.LogWarning($"[Sheet] template {template} missing (run Tools/unity.sh render-setup): using {shader} directly");
            var m = new Material(sh) { name = template + " (runtime)" };
            if (template == SheetMaterialTemplates.Case) { m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_SmoothnessTextureChannel", 0f); m.SetFloat(MaterialLibrary.Smoothness, 1f); }
            _ownMaterials.Add(m);
            return m;
        }
        readonly List<Material> _ownMaterials = new List<Material>();
        Texture2D _palette, _metal;

        MeshRenderer MakeRenderer(string name, Mesh mesh, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return r;
        }

        void SetVisible(bool v)
        {
            if (_root == null || v == _visible) return;
            _visible = v;
            _root.SetActive(v);
            if (!v) { Pokes.ResetAll(); HoverRow = -1; }
        }

        // ------------------------------------------------------------------------------------ frame
        public override void Tick(FramePhase phase, float dt)
        {
            if (phase == FramePhase.Decor) Frame(dt);
            else if (phase == FramePhase.EndFrame) EndFrame(dt);
        }

        void FinishBuild()
        {
            if (Book != null || _build == null || !_build.IsCompleted) return;
            if (_build.IsFaulted) { Debug.LogError("[Sheet] engraving the book failed: " + _build.Exception?.GetBaseException()); _build = null; return; }
            Book = _build.Result;
            _build = null;
            Pages.SetBook(Book);
            _spread = Mathf.Clamp(_spread, Book.FirstPage, LastLeftPage);
            foreach (var w in Book.Warnings) Debug.LogWarning("[Sheet] " + w);
            Debug.Log($"[Sheet] book: {Book.Songs.Count} songs on {Book.Pages.Count} pages ({Book.FirstPage}-{Book.LastPage}), engraved in {(Time.realtimeSinceStartup - _bootStarted) * 1000f:F0} ms; textures {Pages.TextureBytes / 1048576.0:F2} MiB");
        }

        void Frame(float dt)
        {
            if (Host == null || Pages == null) return;   // no book data: nothing to show
            FinishBuild();
            if (_root == null) BuildView();
            if (_root == null) return;
            if (_tut == null) _tut = _app.Get<ITutorial>();
            if (_arrange == null) _arrange = _app.Get<IArrangeMode>();
            if (_grab == null) _grab = _app.Get<IGrabService>();
            if (_mr == null) _mr = _app.Get<IMixedReality>();
            if (_menu == null) _menu = _app.Get<IMenuPanel>();
            if (_guide == null) _guide = _app.Get<IGuide>();
            if (_pianoRig == null) { var reg = _app.Get<IRigRegistry>(); if (reg != null) _pianoRig = reg.Get(RigId.Piano); }
            bool placing = _mr != null && _mr.IsPlacing;
            SetVisible(Host.MusicRestPose.HasValue && !placing);
            bool arranging = _arrange != null && _arrange.Arranging;

            // ---- follow the lesson
            TutorialState state = _tut != null ? _tut.State : TutorialState.Idle;
            string lesson = _tut?.LessonId;
            bool piano = _tut != null && _tut.Instrument == InstrumentKind.Piano;
            bool running = state == TutorialState.Countdown || state == TutorialState.Playing || state == TutorialState.Waiting;
            string song = null;
            int offset = 0;
            if (Book != null && piano && lesson != null)
            {
                if (_meta != null && _meta.TryGetValue(lesson, out var m) && m.BookSong != null) { song = m.BookSong; offset = m.EventOffset; }
                else song = lesson;
                if (Book.Song(song) == null) song = null;
            }
            int cur = Host.CurrentEvent;
            int k = song != null && cur >= 0 ? cur + offset : -1;
            bool started = state == TutorialState.Countdown && (_lastState != TutorialState.Countdown || !string.Equals(lesson, _lastLesson, StringComparison.Ordinal));
            Mark mark = null;
            int markPage = -1;
            if (song != null && running) Book.TryGetMark(song, k >= 0 ? k : offset, out mark, out markPage);
            if (mark != null && !Leaf.Active && !arranging)
            {
                int target = SheetBook.SpreadOf(markPage);
                if (target != _spread && (started || k != _lastK)) { BeginTurn(target, Math.Abs(target - _spread) <= 2 ? TurnKind.Auto : TurnKind.Riffle); AutoTurns++; }
            }
            _lastState = state; _lastLesson = lesson; _lastK = k;

            // ---- pokes and clicks
            HoverRow = -1;
            bool allowed = _visible && Book != null && !arranging && !placing && !(_grab != null && _pianoRig != null && ReferenceEquals(_grab.HeldRig, _pianoRig));
            FillRows();
            if (allowed && _app.IsPresenting) PokeHands();
            else Pokes.ResetAll();
            if (allowed && !_app.IsPresenting) DesktopClick();

            // ---- the turn
            if (Leaf.Active)
            {
                bool ready = Pages.IsReady(Leaf.ToLeft) && (Pages.IsReady(Leaf.ToLeft + 1) || Book == null || Book.Page(Leaf.ToLeft + 1) == null);
                for (int i = Leaf.LeavesStarted; i < Leaf.LeafCount && Leaf.LeafStarted(i); i++)
                {
                    float level = Leaf.Kind == TurnKind.Riffle ? SheetPaperSound.RiffleLevel(i) : Leaf.Kind == TurnKind.Auto ? 0.9f : 1f;
                    Host.PaperTurn(Leaf.LeafSeconds, level, _seed++, Host.AudioNow);
                    Leaf.MarkLeafStarted();
                }
                if (Leaf.Step(dt, ready))
                {
                    _spread = Leaf.ToLeft;
                    _app.Settings.SetInt(KeySpread, _spread);
                    if (Leaf.Kind == TurnKind.Manual) _manualFar = Book != null && _spread >= Book.FirstPage + 4;
                }
                _leavesDirty = true;
            }
            else if (!_leavesCollapsed) _leavesDirty = true;

            // ---- the marker (and the contents hover)
            bool open = mark != null && SheetBook.SpreadOf(markPage) == _spread && !Leaf.Active;
            float level0 = arranging || mark == null ? 0f : state == TutorialState.Countdown ? SheetMarker.CountdownLevel : 1f;
            Marker.Tick(dt, mark != null ? markPage : -1, mark != null ? new Vector4((float)mark.X0, (float)mark.Y0, (float)mark.X1, (float)mark.Y1) : Vector4.zero, level0, state == TutorialState.Waiting, open);
            ApplyUniforms();

            // ---- Sunny
            bool inView = _visible && InView();
            bool tourDone = (_guide != null && _guide.Done) || _app.Settings.TourDone;
            bool contentsOpen = Book != null && Book.Page(_spread) != null && Book.Page(_spread).Contents;
            Tips.Tick(dt, _visible && Book != null, inView, tourDone, contentsOpen, song != null ? state : TutorialState.Idle, song != null, _manualFar, !_app.IsPresenting);
        }

        bool InView()
        {
            var head = _app.Head;
            if (head == null || _root == null) return false;
            Vector3 c = _root.transform.TransformPoint(new Vector3(0f, _sBottom + BookGeometry.PageH * 0.5f, -0.01f));
            Vector3 d = c - head.position;
            float m = d.magnitude;
            return m > 1e-4f && Vector3.Dot(head.forward, d / m) >= SheetTips.GazeCos;
        }

        void FillRows()
        {
            _rows.Clear();
            if (Book == null) return;
            var L = Book.Page(_spread);
            var R = Book.Page(_spread + 1);
            if (L != null && L.Contents) foreach (var r in L.Rows) _rows.Add(-1, r);
            if (R != null && R.Contents) foreach (var r in R.Rows) _rows.Add(1, r);
        }

        void PokeHands()
        {
            var hands = _app.Input.Hands;
            var zone = _menu?.GrabZone;
            Transform root = _root.transform;
            for (int h = 0; h < hands.Length; h++)
            {
                var hs = hands[h];
                var p = hs.Pointers[0];
                if (!hs.Connected || hs.IsMouse || hs.Holding != null || !p.Active || (zone.HasValue && zone.Value.Contains(p.Tip))) { Pokes.Lost(h); continue; }
                Vector3 tip = root.InverseTransformPoint(p.Tip);
                var fired = Pokes.Update(h, tip, p.Speed, _app.Clock.NowMs, _sBottom, _rows.Count > 0 ? _rows : null, out var hit);
                if (hit.Target == PokeTarget.Row && hit.N > 0f && hit.N < SheetPokes.HoverRange) HoverRow = hit.Row;
                if (fired != PokeTarget.None) Act(fired, hit.Row);
            }
        }

        void DesktopClick()
        {
            if (!DesktopInput.MouseDown(0) || DesktopInput.Shift || DesktopInput.Alt) return;
            if (_headCamera == null && _app.Rig != null) _headCamera = _app.Rig.HeadCamera;
            if (_headCamera == null) return;
            Ray ray = _headCamera.ScreenPointToRay(DesktopInput.MousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, _headCamera.farClipPlane, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide)) return;
            if (hit.collider != _pagesClick && hit.collider != _ribbonClick) return;
            Tap(hit.point);
        }

        public PokeTarget Tap(Vector3 world)
        {
            if (_root == null || Book == null) return PokeTarget.None;
            FillRows();
            var hit = Pokes.Classify(_root.transform.InverseTransformPoint(world), _sBottom, _rows.Count > 0 ? _rows : null);
            if (hit.Target != PokeTarget.None) Act(hit.Target, hit.Row);
            return hit.Target;
        }

        void Act(PokeTarget t, int row)
        {
            switch (t)
            {
                case PokeTarget.CornerRight: Turn(+1); break;
                case PokeTarget.CornerLeft: Turn(-1); break;
                case PokeTarget.Ribbon:
                    Tips.NoteRibbon();
                    Host.Signal("sheet.ribbon");
                    ToContents();
                    break;
                case PokeTarget.Row:
                    if (row < 0 || row >= _rows.Count || _tut == null) break;
                    string id = _rows.Id[row];
                    LastTitleStarted = id;
                    Tips.NoteTitleStart();
                    Host.Signal("sheet.title");
                    // a lesson for the piano: the piano is at the seat (the book is only shown then); its start turns the book
                    _tut.Start(id);
                    break;
            }
        }

        public bool Turn(int direction)
        {
            if (Book == null || Leaf.Active) return false;
            int to = _spread + 2 * Math.Sign(direction);
            if (to < Book.FirstPage || to > LastLeftPage) return false;   // past the last page or before the contents: nothing
            BeginTurn(to, TurnKind.Manual);
            ManualTurns++;
            Tips.NoteManualTurn();
            Host.Signal("sheet.turn");
            return true;
        }

        public void ToContents()
        {
            if (Book == null || Leaf.Active || _spread == Book.FirstPage) return;
            BeginTurn(Book.FirstPage, TurnKind.Riffle);
            _manualFar = false;
        }

        void BeginTurn(int toLeft, TurnKind kind)
        {
            int apart = Math.Abs(toLeft - _spread) / 2;
            Leaf.Begin(_spread, toLeft, kind, apart);
            Pages.Request(toLeft);
            Pages.Request(toLeft + 1);
        }

        // ------------------------------------------------------------------------------------ uniforms
        void ApplyUniforms()
        {
            if (_paperM == null) return;
            if (Leaf.Active) Leaf.Faces(_roles);
            else
            {
                _roles[0] = _spread; _roles[1] = _spread + 1;
                for (int k = 0; k < BookMesh.Leaves; k++) { _roles[2 + 2 * k] = _spread + 1; _roles[3 + 2 * k] = _spread; }
            }
            Pages.BeginPins();
            for (int i = 0; i < _roles.Length; i++) Pages.Pin(_roles[i]);
            if (Leaf.Active) { Pages.Pin(Leaf.ToLeft); Pages.Pin(Leaf.ToLeft + 1); }
            // idle: draw the next spread ahead into the two free slices, so a tapped corner turns at once
            else if (Book != null && Ready && !Pages.Pending && _spread + 2 <= LastLeftPage) { Pages.Request(_spread + 2); Pages.Request(_spread + 3); }
            float s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, s5 = 0, s6 = 0, s7 = 0, i0 = 0, i1 = 0, i2 = 0, i3 = 0, i4 = 0, i5 = 0, i6 = 0, i7 = 0;
            Slice(0, ref s0, ref i0); Slice(1, ref s1, ref i1); Slice(2, ref s2, ref i2); Slice(3, ref s3, ref i3);
            Slice(4, ref s4, ref i4); Slice(5, ref s5, ref i5); Slice(6, ref s6, ref i6); Slice(7, ref s7, ref i7);
            _paperM.SetVector(s_slices0, new Vector4(s0, s1, s2, s3));
            _paperM.SetVector(s_slices1, new Vector4(s4, s5, s6, s7));
            _paperM.SetVector(s_ink0, new Vector4(i0, i1, i2, i3));
            _paperM.SetVector(s_ink1, new Vector4(i4, i5, i6, i7));
            Ready = Book != null && Pages.IsReady(_spread) && (Pages.IsReady(_spread + 1) || Book.Page(_spread + 1) == null);
            // the marker
            int ms = Marker.Value > 0f ? Pages.SliceOf(Marker.Page) : -1;
            _paperM.SetVector(s_mark, Marker.Rect);
            _paperM.SetVector(s_markInfo, new Vector4(ms, ms >= 0 ? Marker.Value : 0f, 3f, 0f));
            // the contents row under a hovering fingertip (the same tint, fainter)
            if (HoverRow >= 0 && HoverRow < _rows.Count)
            {
                int hs = Pages.SliceOf(_rows.Page[HoverRow]);
                var r = _rows.Rect[HoverRow];
                _paperM.SetVector(s_hover, new Vector4(r.x - 2f, r.y, r.z + 2f, r.w));
                _paperM.SetVector(s_hoverInfo, new Vector4(hs, hs >= 0 ? 0.35f : 0f, 3f, 0f));
            }
            else _paperM.SetVector(s_hoverInfo, new Vector4(-1f, 0f, 3f, 0f));
            var sh = Leaf.Shadow();
            _paperM.SetVector(s_leaf, new Vector4(sh.x, sh.y, 0f, 0f));
            // the reading lamp (world)
            if (_root != null)
            {
                BookMesh.LampWorld(_root.transform, out Vector3 c, out Vector3 axis, out Vector3 dir);
                _paperM.SetVector(s_lampPos, new Vector4(c.x, c.y, c.z, LampIntensity));
                _paperM.SetVector(s_lampAxis, new Vector4(axis.x, axis.y, axis.z, 0f));
                _paperM.SetVector(s_lampDir, new Vector4(dir.x, dir.y, dir.z, LampCosOuter));
                Color lc = LampColour.linear;
                _paperM.SetVector(s_lampColor, new Vector4(lc.r, lc.g, lc.b, LampCosInner));
            }
        }

        void Slice(int role, ref float slice, ref float ink)
        {
            int page = _roles[role];
            int s = Pages.Request(page);
            if (s >= 0) { slice = s; ink = 1f; }
            else { slice = 0; ink = 0f; }
        }

        void EndFrame(float dt)
        {
            if (Pages == null || _root == null) return;
            Pages.Tick(dt);
            if (_leavesDirty && _paperMesh != null)
            {
                Leaf.WriteLeaves(_leafVerts, _sBottom);
                _paperMesh.SetVertexBufferData(_leafVerts, 0, BookMesh.LeafVertexStart, BookMesh.LeafVertexCount, 0,
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
                _leavesCollapsed = !Leaf.Active;
                _leavesDirty = false;
            }
        }

        // ------------------------------------------------------------------------------------ anchors (Sunny's demos)
        public bool TryGetAnchor(string anchor, out Vector3 world, out Vector3 up)
        {
            world = up = Vector3.zero;
            if (_root == null || !_visible) return false;   // no book (the piano away, gliding, being placed): nothing to point at
            var t = _root.transform;
            switch (anchor)
            {
                case "sheet.corner.right":
                case "sheet.corner.left":
                {
                    int sg = anchor.EndsWith("right", StringComparison.Ordinal) ? 1 : -1;
                    float s = BookGeometry.PageW - 0.02f;
                    world = t.TransformPoint(BookGeometry.PagePoint(sg, s, 0.02f, _sBottom));
                    up = t.TransformDirection(BookGeometry.PageNormal(sg, BookGeometry.FlatReach(s)));
                    return true;
                }
                case "sheet.ribbon":
                {
                    BookMesh.RibbonTail(out Vector3 a, out Vector3 b);
                    world = t.TransformPoint((a + b) * 0.5f);
                    up = t.parent != null ? t.parent.up : Vector3.up;
                    return true;
                }
                case "sheet.row.1":
                {
                    if (Book == null || Book.Pages.Count == 0 || Book.Pages[0].Rows.Count == 0) return false;
                    var r = Book.Pages[0].Rows[0];
                    world = t.TransformPoint(MmToBook(-1, (float)(r.X0 + r.X1) * 0.25f, (float)(r.Y0 + r.Y1) * 0.5f));
                    up = t.TransformDirection(Vector3.back);
                    return true;
                }
                case "sheet.letter.first":
                {
                    if (Book == null) return false;
                    for (int side = 0; side < 2; side++)
                    {
                        var p = Book.Page(_spread + side);
                        if (p == null || p.Marks.Count == 0) continue;
                        var m = p.Marks[0];
                        world = t.TransformPoint(MmToBook(side == 0 ? -1 : 1, (float)(m.X0 + m.X1) * 0.5f, (float)m.Y1 - 1.6f - (float)SheetStyle.LetterCap * 0.5f));
                        up = t.TransformDirection(Vector3.back);
                        return true;
                    }
                    // no music open (the contents, a Notes page): the first printed line of the left page
                    var lp = Book.Page(_spread);
                    float y = lp != null && lp.Rows.Count > 0 ? (float)(lp.Rows[0].Y0 + lp.Rows[0].Y1) * 0.5f : 40f;
                    world = t.TransformPoint(MmToBook(-1, 80f, y));
                    up = t.TransformDirection(Vector3.back);
                    return true;
                }
            }
            return false;
        }

        /// <summary>IPointerClaimant: a fingertip within 30 mm in front of the open pages (or on the ribbon's tail) belongs to the
        /// book, so a reach for a corner never starts the layout swipe or a contactless instrument.</summary>
        public bool ClaimsPoint(Vector3 world)
        {
            if (!_visible || _root == null) return false;
            Vector3 p = _root.transform.InverseTransformPoint(world);
            BookGeometry.ToPage(p, _sBottom, out _, out float s, out float v, out float n);
            if (s <= BookGeometry.PageW + SheetPokes.CornerOut && v >= -SheetPokes.CornerOut && v <= BookGeometry.PageH && n >= -0.005f && n <= ClaimDepth) return true;
            var h = Pokes.Classify(p, _sBottom, null);
            return h.Target == PokeTarget.Ribbon && h.N <= ClaimDepth;
        }
        public const float ClaimDepth = 0.03f;

        /// <summary>Page mm of a side -> the book frame (on the curled surface).</summary>
        Vector3 MmToBook(int sigma, float xmm, float ymm)
        {
            float s = sigma > 0 ? xmm / 1000f : BookGeometry.PageW - xmm / 1000f;
            return BookGeometry.PagePoint(sigma, s, BookGeometry.PageH - ymm / 1000f, _sBottom);
        }

        public override void Shutdown()
        {
            try { _build?.Wait(3000); } catch (Exception) { }
            Pages?.Dispose();
            if (_leafVerts.IsCreated) _leafVerts.Dispose();
            if (_paperMesh != null) UnityEngine.Object.Destroy(_paperMesh);
            if (_caseMesh != null) UnityEngine.Object.Destroy(_caseMesh);
            foreach (var m in _ownMaterials) if (m != null) UnityEngine.Object.Destroy(m);
            if (_palette != null) UnityEngine.Object.Destroy(_palette);
            if (_metal != null) UnityEngine.Object.Destroy(_metal);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _app?.Unregister<ISheetBook>(this);
        }
    }

    static class BookSheetMath
    {
        /// <summary>The last spread's left page (a lone last left page shows a blank right one).</summary>
        public static int LastLeft(SheetBook b) => SheetBook.SpreadOf(b.LastPage);
    }

    static class ColorExt
    {
        public static Color Linear(this Color32 c) => ((Color)c).linear;
    }
}
