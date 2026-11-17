// A placeholder instrument built from its descriptor (12-new-instruments/platform.md 13.3, the phase-0 stubs): neutral wood
// blocks at the footprint (Seat.Boxes; on display the Stow.Boxes when they differ), a tap-to-knock IInstrument (a fingertip
// crossing the top face downward knocks; a contactless instrument knocks when an unclaimed hand enters its air box while it
// is Facing), and every platform interface of section 3.3 in its minimal form: IStowable, IInstrumentPlayback (its one
// Onset type), IGuideAnchors ("top"), ILessonTargets (targets i = 0..Regions-1 across the top face), IInstrumentSurface.
// The five stub modules (Handpan, HandDrums, Marimba, Bowls) use it until their agents replace them; the
// conformance suite (Tests/PlayMode/Core/InstrumentConformanceTests) runs over it like over a real instrument.
// Allocation-free per frame. Names no instrument: everything comes from the descriptor.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    public sealed class PlaceholderInstrument : MonoBehaviour, IInstrument, IStowable, IInstrumentPlayback, IGuideAnchors,
        ILessonTargets, IInstrumentSurface
    {
        /// <summary>Lesson targets / knock pitches across the top face.</summary>
        public const int Regions = 5;
        /// <summary>A fingertip may knock again after this long (per pointer).</summary>
        public const double DebounceMs = 90.0;

        App _app;
        InstrumentDescriptor _d;
        Action<int, float, double> _knock;
        Action _hush;
        IRecorder _recorder;
        readonly List<Renderer> _handles = new List<Renderer>(4);
        readonly List<Renderer> _played = new List<Renderer>(1);
        readonly List<GameObject> _seatParts = new List<GameObject>(4), _displayParts = new List<GameObject>(4), _standParts = new List<GameObject>(4);
        readonly double[] _lastKnock = new double[PointerInput.MaxSlots];
        readonly bool[] _wasInField = new bool[PointerInput.MaxSlots];
        FootBox _top;
        bool _hasField;
        FootBox _field;
        string _onset;
        bool _stowed, _onSurface;
        Bounds _localBounds;

        public Rig Rig { get; private set; }
        public InstrumentDescriptor Descriptor => _d;
        public BoxCollider TopCollider { get; private set; }
        /// <summary>Knocks played live (tests).</summary>
        public int LiveKnocks { get; private set; }
        public int PlaybackKnocks { get; private set; }
        /// <summary>The last lesson cue (ShowTarget): target index and k.</summary>
        public int CueIndex { get; private set; } = -1;
        public float CueK { get; private set; }

        /// <summary>Tests: a scripted desktop click (button + world ray) instead of the live mouse.</summary>
        public bool ScriptedMouse, ScriptedButton;
        public Ray ScriptedRay;
        bool _scriptedWasDown;

        /// <summary>Builds the placeholder under <paramref name="app"/>'s world and registers it. knock(index, vel, when) plays
        /// the instrument's sound (its unit); hush silences it.</summary>
        public static PlaceholderInstrument Create(App app, InstrumentDescriptor d, Action<int, float, double> knock, Action hush)
        {
            var go = new GameObject(d.Name);
            go.transform.SetParent(app.WorldRoot, false);
            var p = go.AddComponent<PlaceholderInstrument>();
            p.Setup(app, d, knock, hush);
            return p;
        }

        void Setup(App app, InstrumentDescriptor d, Action<int, float, double> knock, Action hush)
        {
            _app = app;
            _d = d;
            _knock = knock;
            _hush = hush;
            _recorder = app.Get<IRecorder>();
            for (int i = 0; i < _lastKnock.Length; i++) _lastKnock[i] = double.NegativeInfinity;
            _onset = null;
            foreach (var e in d.Events) if (e.Role == EventRole.Onset) { _onset = e.Type; break; }
            transform.localPosition = d.Seat.Home;
            transform.localRotation = Quaternion.Euler(0f, d.Seat.HomeYaw, 0f);
            Build(app);
            Rig = new Rig
            {
                Id = d.Rig, Group = transform, Zone = new BoxZone(this), Priority = d.Seat.Priority,
                HomeAll = d.Seat.Home, HomeYawAll = d.Seat.HomeYaw, HomeSingle = d.Seat.Single, HomeYawSingle = d.Seat.SingleYaw,
                Handles = _handles,
            };
            Rig.Instruments.Add(this);
            app.Register<Rig>(Rig);
            app.Register<IInstrument>(this);
            app.Register<IStowable>(this);
            app.Register<IInstrumentPlayback>(this);
            app.Register<IGuideAnchors>(this);
            app.Register<ILessonTargets>(this);
            if (d.Has(InstrumentTraits.TableTop)) app.Register<IInstrumentSurface>(this);
        }

        // ---------------------------------------------------------------- model

        /// <summary>A footprint box in rig-local heights (the descriptor's are above the floor with the rig at Home).</summary>
        FootBox Local(FootBox b) { float y = _d.Seat.Home.y; b.Y0 -= y; b.Y1 -= y; return b; }

        void Build(App app)
        {
            MaterialLibrary mats = app.Materials;
            Texture2D pale = app.Headless ? null : SharedTextures.Get(SharedTextures.WoodMaple);
            Texture2D dark = app.Headless ? null : SharedTextures.Get(SharedTextures.WoodWalnut);
            Material top = mats.Lit(new Color(0.86f, 0.80f, 0.70f), 0.35f, 0f, pale);
            Material stand = mats.Lit(new Color(0.55f, 0.45f, 0.36f), 0.3f, 0f, dark);
            // The top play box: the non-air box with the highest top.
            int topIndex = -1;
            for (int i = 0; i < _d.Seat.Boxes.Length; i++)
            {
                FootBox b = Local(_d.Seat.Boxes[i]);
                if (b.Air) { if (!_hasField) { _field = b; _hasField = true; } continue; }
                if (topIndex < 0 || b.Y1 > _d.Seat.Boxes[topIndex].Y1 - _d.Seat.Home.y) topIndex = i;
            }
            _top = topIndex >= 0 ? Local(_d.Seat.Boxes[topIndex]) : new FootBox(0, 0, 0.2f, 0.2f, 0.6f, 0.75f);
            bool any = false;
            for (int i = 0; i < _d.Seat.Boxes.Length; i++)
            {
                FootBox b = Local(_d.Seat.Boxes[i]);
                if (b.Air) continue;
                bool isTop = i == topIndex;
                GameObject g = Block(isTop ? "Top" : "Stand " + i, b, isTop ? top : stand);
                _seatParts.Add(g);
                var r = g.GetComponent<Renderer>();
                _handles.Add(r);
                if (isTop) _played.Add(r);
                else _standParts.Add(g);
                Encapsulate(ref any, b);
            }
            // The displayed configuration, when it differs (a wall bracket, a sill): its own blocks, hidden at the seat.
            FootBox[] shown = _d.Stow?.Boxes;
            if (shown != null && shown.Length > 0)
            {
                for (int i = 0; i < shown.Length; i++)
                {
                    FootBox b = shown[i];
                    b.Y0 -= _d.Stow.Pose.y; b.Y1 -= _d.Stow.Pose.y;   // displayed heights: above the floor with the rig at Stow.Pose
                    GameObject g = Block("Display " + i, b, i == 0 ? top : stand);
                    g.SetActive(false);
                    _displayParts.Add(g);
                }
            }
            // The desktop click target (and the ray summon's pick on desktop): the top box on Layers.PlaySurface.
            var colGo = new GameObject("Click target");
            colGo.layer = Layers.PlaySurface;
            colGo.transform.SetParent(transform, false);
            TopCollider = colGo.AddComponent<BoxCollider>();
            TopCollider.center = new Vector3(_top.Cx, (_top.Y0 + _top.Y1) * 0.5f, _top.Cz);
            TopCollider.size = new Vector3(_top.Hx * 2f, _top.Y1 - _top.Y0, _top.Hz * 2f);
        }

        void Encapsulate(ref bool any, FootBox b)
        {
            var bb = new Bounds(new Vector3(b.Cx, (b.Y0 + b.Y1) * 0.5f, b.Cz), new Vector3(b.Hx * 2f, b.Y1 - b.Y0, b.Hz * 2f));
            if (!any) { _localBounds = bb; any = true; } else _localBounds.Encapsulate(bb);
        }

        GameObject Block(string name, FootBox b, Material m)
        {
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            g.transform.localPosition = new Vector3(b.Cx, b.Y0, b.Cz);
            g.AddComponent<MeshFilter>().sharedMesh = BlockMesh(b.Hx * 2f, b.Y1 - b.Y0, b.Hz * 2f);
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            return g;
        }

        /// <summary>A box w x h x d with its base at y 0, a 6 mm chamfer-free block whose UVs are in metres / 0.6 (the wood
        /// grain keeps its real scale on every face).</summary>
        static Mesh BlockMesh(float w, float h, float d)
        {
            var v = new List<Vector3>(24); var n = new List<Vector3>(24); var uv = new List<Vector2>(24); var t = new List<int>(36);
            float x = w * 0.5f, z = d * 0.5f;
            void Face(Vector3 o, Vector3 a, Vector3 b, Vector3 normal, float la, float lb)
            {
                int i = v.Count;
                v.Add(o); v.Add(o + a); v.Add(o + a + b); v.Add(o + b);
                for (int k = 0; k < 4; k++) n.Add(normal);
                const float s = 1f / 0.6f;
                uv.Add(Vector2.zero); uv.Add(new Vector2(la * s, 0)); uv.Add(new Vector2(la * s, lb * s)); uv.Add(new Vector2(0, lb * s));
                t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }
            Face(new Vector3(-x, h, -z), new Vector3(w, 0, 0), new Vector3(0, 0, d), Vector3.up, w, d);
            Face(new Vector3(-x, 0, z), new Vector3(w, 0, 0), new Vector3(0, 0, -d), Vector3.down, w, d);
            Face(new Vector3(-x, 0, -z), new Vector3(w, 0, 0), new Vector3(0, h, 0), Vector3.back, w, h);
            Face(new Vector3(x, 0, z), new Vector3(-w, 0, 0), new Vector3(0, h, 0), Vector3.forward, w, h);
            Face(new Vector3(-x, 0, z), new Vector3(0, 0, -d), new Vector3(0, h, 0), Vector3.left, d, h);
            Face(new Vector3(x, 0, -z), new Vector3(0, 0, d), new Vector3(0, h, 0), Vector3.right, d, h);
            var m = new Mesh { name = "Placeholder block" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            m.UploadMeshData(true);
            return m;
        }

        // ---------------------------------------------------------------- IInstrument

        public InstrumentKind Kind => _d.Kind;
        public int TickOrder => _d.TickOrder;
        public bool AcceptsStickPointers => false;
        public bool IsPresent => isActiveAndEnabled && Rig != null && Rig.InScene && !_stowed;
        public Transform Root => transform;
        public IReadOnlyList<Renderer> PlayedParts => _played;
        public bool Stowed => _stowed;
        public bool OnSurface => _onSurface;

        public void Tick(IReadOnlyList<Pointer> pointers, float dt)
        {
            double now = _app.Clock.NowMs;
            bool contactless = _d.Has(InstrumentTraits.Contactless);
            for (int i = 0; i < pointers.Count; i++)
            {
                Pointer p = pointers[i];
                int slot = p.Slot >= 0 && p.Slot < _lastKnock.Length ? p.Slot : 0;
                if (!p.Active || p.FromMouse) { _wasInField[slot] = false; continue; }
                if (contactless) { TickField(p, slot, now); continue; }
                Vector3 a = transform.InverseTransformPoint(p.Prev), b = transform.InverseTransformPoint(p.Tip);
                if (!(a.y > _top.Y1 && b.y <= _top.Y1)) continue;
                float k = (a.y - _top.Y1) / Mathf.Max(1e-6f, a.y - b.y);
                Vector3 c = Vector3.Lerp(a, b, k);
                if (Mathf.Abs(c.x - _top.Cx) > _top.Hx || Mathf.Abs(c.z - _top.Cz) > _top.Hz) continue;
                if (now - _lastKnock[slot] < DebounceMs) continue;
                _lastKnock[slot] = now;
                float vel = Mathf.Clamp01(0.25f + 0.6f * p.Speed);
                LiveKnock(RegionOf(c.x), vel, p.Owner, now);
            }
            DesktopClick(now);
        }

        void TickField(Pointer p, int slot, double now)
        {
            if (!_hasField || !(p.Owner is HandState hand)) return;
            Vector3 l = transform.InverseTransformPoint(p.Tip);
            bool inside = Mathf.Abs(l.x - _field.Cx) <= _field.Hx && Mathf.Abs(l.z - _field.Cz) <= _field.Hz && l.y >= _field.Y0 && l.y <= _field.Y1;
            bool was = _wasInField[slot];
            _wasInField[slot] = inside;
            if (!inside || was || p.Finger != 1) return;          // the index fingertip entering the field
            if (p.ClaimedBy != null) return;                      // a contact instrument, the menu or Sunny owns it
            ILayoutService layout = _app.Get<ILayoutService>();
            if (layout != null && layout.Mode == LayoutMode.All && layout.Facing != Kind) return;
            if (PlayStamp.PlayedOtherWithin(hand, Kind, now)) return;
            if (now - _lastKnock[slot] < DebounceMs) return;
            _lastKnock[slot] = now;
            LiveKnock(RegionOf(l.x), 0.6f, hand, now);
        }

        int RegionOf(float localX)
        {
            float u = (localX - (_top.Cx - _top.Hx)) / Mathf.Max(1e-4f, _top.Hx * 2f);
            return Mathf.Clamp((int)(u * Regions), 0, Regions - 1);
        }

        void LiveKnock(int region, float vel, IPointerOwner owner, double now)
        {
            _knock?.Invoke(region, vel, 0.0);
            LiveKnocks++;
            if (owner != null) PlayStamp.Mark(owner, Kind, now);
            if (_recorder == null) _recorder = _app.Get<IRecorder>();   // Lessons (Order 40) registers it after the stubs' Init
            if (_onset != null) _recorder?.Add(new TakeEvent { type = _onset, i = region, vel = vel });
        }

        void DesktopClick(double now)
        {
            if (_app.IsPresenting) { _scriptedWasDown = ScriptedMouse && ScriptedButton; return; }
            bool pressed;
            Ray ray;
            if (ScriptedMouse)
            {
                pressed = ScriptedButton && !_scriptedWasDown;
                _scriptedWasDown = ScriptedButton;
                ray = ScriptedRay;
            }
            else
            {
                pressed = DesktopInput.MouseDown(0) && !DesktopInput.Shift && !DesktopInput.Alt;
                if (!pressed) return;
                Camera cam = _app.Rig != null ? _app.Rig.HeadCamera : null;
                if (cam == null) return;
                ray = cam.ScreenPointToRay(DesktopInput.MousePosition);
            }
            if (!pressed || TopCollider == null) return;
            if (!Physics.Raycast(ray, out RaycastHit hit, 20f, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide)) return;
            if (hit.collider != TopCollider) return;   // the nearest play surface belongs to another module
            Vector3 l = transform.InverseTransformPoint(hit.point);
            LiveKnock(RegionOf(l.x), 0.7f, null, now);
        }

        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow) { }   // no glows (platform.md 3.3)

        public PlaySurface PlaySurfaceAt(Vector3 world)
        {
            if (!IsPresent) return PlaySurface.None;
            Vector3 l = transform.InverseTransformPoint(world);
            bool over = Mathf.Abs(l.x - _top.Cx) <= _top.Hx && Mathf.Abs(l.z - _top.Cz) <= _top.Hz && l.y >= _top.Y0 && l.y <= _top.Y1 + 0.08f;
            if (!over && _hasField)
                over = Mathf.Abs(l.x - _field.Cx) <= _field.Hx && Mathf.Abs(l.z - _field.Cz) <= _field.Hz && l.y >= _field.Y0 && l.y <= _field.Y1;
            return over ? PlaySurface.Instrument : PlaySurface.None;
        }

        // ---------------------------------------------------------------- IStowable / IInstrumentSurface

        public void SetStowed(bool stowed, StowMount mount)
        {
            _stowed = stowed;
            bool display = stowed && _displayParts.Count > 0;
            bool hideStand = _onSurface || (stowed && mount == StowMount.Surface);   // on a real table, on the sill
            for (int i = 0; i < _seatParts.Count; i++)
            {
                bool isStand = _standParts.Contains(_seatParts[i]);
                _seatParts[i].SetActive(!display && !(isStand && hideStand));
            }
            for (int i = 0; i < _displayParts.Count; i++) _displayParts[i].SetActive(display);
            if (stowed) { _hush?.Invoke(); for (int i = 0; i < _wasInField.Length; i++) _wasInField[i] = false; }
        }

        public void SetOnSurface(bool onSurface)
        {
            _onSurface = onSurface;
            for (int i = 0; i < _standParts.Count; i++) _standParts[i].SetActive(!onSurface && !_stowed);
        }

        // ---------------------------------------------------------------- IInstrumentPlayback

        public bool Play(in TakeEvent e)
        {
            if (e.type != _onset) return false;
            _knock?.Invoke(Mathf.Clamp(e.i, 0, Regions - 1), e.vel, 0.0);
            PlaybackKnocks++;
            return true;
        }

        public void ReleaseAll() => _hush?.Invoke();
        public void CloseLane(int lane) { }

        // ---------------------------------------------------------------- IGuideAnchors

        /// <summary>"top": the centre of the top face; "top.&lt;i&gt;": region i of it. Up = the face normal.</summary>
        public bool TryGetAnchor(string anchor, out Vector3 world, out Vector3 up)
        {
            up = transform.up;
            world = default;
            if (anchor == "top") { world = transform.TransformPoint(new Vector3(_top.Cx, _top.Y1, _top.Cz)); return true; }
            if (anchor != null && anchor.StartsWith("top.", StringComparison.Ordinal) && int.TryParse(anchor.Substring(4), out int i) && i >= 0 && i < Regions)
            {
                world = transform.TransformPoint(RegionCentre(i));
                return true;
            }
            return false;
        }

        Vector3 RegionCentre(int i) => new Vector3(_top.Cx - _top.Hx + _top.Hx * 2f * (i + 0.5f) / Regions, _top.Y1, _top.Cz);

        // ---------------------------------------------------------------- ILessonTargets

        public bool Validate(in LessonTarget t, out string why)
        {
            why = t.i >= 0 && t.i < Regions ? null : $"target i {t.i} is outside 0..{Regions - 1}";
            return why == null;
        }

        public bool TryGetLanding(in LessonTarget t, out Pose landing, out Vector3 approach, out Vector2 size)
        {
            int i = Mathf.Clamp(t.i, 0, Regions - 1);
            landing = new Pose(transform.TransformPoint(RegionCentre(i)), transform.rotation);
            approach = -transform.up;
            float w = _top.Hx * 2f / Regions;
            size = new Vector2(Mathf.Min(0.08f, w * 0.8f), Mathf.Min(0.08f, _top.Hz * 1.6f));
            return true;
        }

        public bool Matches(in LessonTarget t, in TakeEvent e) => e.type == _onset && e.i == t.i;

        public void ShowTarget(in LessonTarget t, float k) { CueIndex = k > 0f ? t.i : -1; CueK = k; }

        public string Hint(IReadOnlyList<LessonTarget> targets)
        {
            if (targets == null || targets.Count == 0) return "Tap the top";
            string label = targets[0].label;
            return string.IsNullOrEmpty(label) ? "Tap the top" : "Tap the " + label;
        }

        /// <summary>The rig zone: the footprint's local bounds grown by GrabConstants.ZonePad.</summary>
        sealed class BoxZone : IPlayZone
        {
            readonly PlaceholderInstrument _p;
            public BoxZone(PlaceholderInstrument p) { _p = p; }
            public bool Contains(Vector3 world)
            {
                if (_p == null || !_p.isActiveAndEnabled) return false;
                Vector3 l = _p.transform.InverseTransformPoint(world);
                Bounds b = _p._localBounds;
                b.Expand(GrabConstants.ZonePad * 2f);
                return b.Contains(l);
            }
        }
    }
}
