// A probe module (BootOptions.ExtraModules, Order 25: after Audio, before Layout) that registers the four golden rigs
// rebuilt from the measured golden boxes (GoldenLayoutFixture.cs), each with a fake IInstrument whose
// play surfaces follow the play-zone rules over the same data, plus fake piano / drums / harp
// APIs (the drums' TryPickUp answers what the test asks), a fake menu panel (IMenuPanel + an always-present IInstrument
// + its Rig, as section 9 has it), a fake tutorial and a fake mixed-reality service (StartPlacing), so the Layout module
// runs end to end against the golden geometry without the instrument modules. The fake instruments are present only
// while their rig is in the scene (Rig.InScene, as DrumKit.IsPresent and CONTRACT-REQUESTS.md item 1 have it).
using System.Collections.Generic;
using System.Threading.Tasks;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode.Layout
{
    internal sealed class GoldenRigsProbe : AppModule
    {
        public static readonly Vector3[] HomeAll = { new Vector3(0f, 0.72f, 0.45f), new Vector3(0.2f, 0f, -0.26f), new Vector3(-0.78f, 0f, -0.2f), new Vector3(-0.39f, 0f, -0.21f) };
        public static readonly float[] HomeYawAll = { 0f, 90f, 83f, -90f };
        public static readonly Vector3[] HomeSingle = { new Vector3(0f, 0.72f, 0.45f), new Vector3(0f, 0f, 0.1f), new Vector3(-0.296604f, 0f, 0.45f), new Vector3(-0.39f, 0f, -0.21f) };
        public static readonly float[] HomeYawSingle = { 0f, 0f, 180f, -90f };
        static readonly InstrumentKind[] Kinds = { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp, InstrumentKind.Table };
        static readonly int[] TickOrders = { 0, 10, 20, 30 };

        public override int Order => 25;
        public App App { get; private set; }
        public readonly Rig[] Rigs = new Rig[4];
        public readonly FakeInstrument[] Instruments = new FakeInstrument[4];
        readonly List<Renderer>[] _handles = new List<Renderer>[4];
        public FakePiano Piano;
        public FakeDrums Drums;
        public FakeHarp Harp;
        public FakeMenu Menu;
        public FakeTutorial Tutorial;
        public FakeMr Mr;
        public readonly List<Mesh> Meshes = new List<Mesh>();

        public override void Init(App app)
        {
            App = app;
            for (int r = 0; r < 4; r++)
            {
                var g = new GameObject("Golden" + GoldenLayout.RigNames[r]).transform;
                g.SetParent(app.WorldRoot, false);
                _handles[r] = new List<Renderer>();
                var played = new List<Renderer>();
                foreach (var p in GoldenLayout.Parts)
                {
                    if (p.Rig != r) continue;
                    Renderer ren = AddBox(g, p, p.Name);
                    if ((p.Flags & GoldenLayout.FlagHandle) != 0) _handles[r].Add(ren);
                    else played.Add(ren);
                }
                Rigs[r] = new Rig
                {
                    Id = (RigId)r, Group = g, Priority = r,
                    HomeAll = HomeAll[r], HomeYawAll = HomeYawAll[r], HomeSingle = HomeSingle[r], HomeYawSingle = HomeYawSingle[r],
                    Handles = _handles[r],   // filled once (ARCHITECTURE.md section 8)
                };
                Instruments[r] = new FakeInstrument(this, r, Kinds[r], TickOrders[r], g, played);
                Rigs[r].Instruments.Add(Instruments[r]);
                if (r <= 2) Rigs[r].Zone = Instruments[r];   // the modules' play zones: keys / heads / strings
                app.Register<Rig>(Rigs[r]);
                app.Register<IInstrument>(Instruments[r]);
            }
            Piano = new FakePiano(Rigs[0].Group);
            Drums = new FakeDrums(Rigs[1].Group);
            Harp = new FakeHarp(Rigs[2].Group);
            app.Register<IPianoApi>(Piano);
            app.Register<IDrumsApi>(Drums);
            app.Register<IHarpApi>(Harp);
            Menu = new FakeMenu(this);
            app.Register<Rig>(Menu.Rig);
            app.Register<IMenuPanel>(Menu);
            app.Register<IInstrument>(Menu);
            Tutorial = new FakeTutorial();
            app.Register<ITutorial>(Tutorial);
            Mr = new FakeMr();
            app.Register<IMixedReality>(Mr);
        }

        public override void Shutdown()
        {
            foreach (var m in Meshes) if (m != null) Object.Destroy(m);
            Meshes.Clear();
        }

        public Renderer AddBox(Transform parent, GoldenLayout.Part p, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = p.C;
            go.transform.localRotation = Quaternion.LookRotation(-p.A2, p.A1);
            var mesh = BoxMesh(p.H);
            Meshes.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            return go.AddComponent<MeshRenderer>();
        }

        static Mesh BoxMesh(Vector3 h)
        {
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++) v[i] = new Vector3((i & 1) != 0 ? h.x : -h.x, (i & 2) != 0 ? h.y : -h.y, (i & 4) != 0 ? h.z : -h.z);
            int[] t = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
            var m = new Mesh { name = "box" };
            m.vertices = v;
            m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        // ---------------------------------------------------------------- play surfaces (the play-zone rules over the golden data)

        public PlaySurface SurfaceOf(int rig, Vector3 w)
        {
            switch (rig)
            {
                case 0: return Piano.InPlayZone(w) && Piano.OverKeys(w) ? PlaySurface.PianoKeys : PlaySurface.None;
                case 1: return Drums.Over(w);
                case 2: return Harp.OverStrings(w) ? PlaySurface.HarpStrings : PlaySurface.None;
                case 3: return OverTableControls(w) ? PlaySurface.Table : PlaySurface.None;
            }
            return PlaySurface.None;
        }

        /// <summary>Over the table controls: within 3.5 cm of a deck button (6..17 cm above the deck) or a switch lever.</summary>
        bool OverTableControls(Vector3 w)
        {
            Transform t = Rigs[3].Group;
            Vector3 l = t.InverseTransformPoint(w);
            Vector3 d = l - GoldenLayout.DeckPos;
            foreach (var b in GoldenLayout.DeckButtons)
                if (Mathf.Sqrt((d.x - b.x) * (d.x - b.x) + (d.z - b.y) * (d.z - b.y)) < 0.035f && d.y > 0.06f && d.y < 0.17f) return true;
            Vector3 p = Quaternion.Inverse(Quaternion.Euler(0f, GoldenLayout.SwitchPanelYawDeg, 0f)) * (l - GoldenLayout.SwitchPanelPos);
            foreach (float x in GoldenLayout.ToggleX)
                if (Mathf.Sqrt((p.x - x) * (p.x - x) + p.z * p.z) < 0.035f && p.y > 0f && p.y < 0.1f) return true;
            return false;
        }
    }

    internal sealed class FakeInstrument : IInstrument, IPlayZone
    {
        readonly GoldenRigsProbe _probe;
        readonly int _rig;
        readonly List<Renderer> _played;
        public int TickCount;

        public FakeInstrument(GoldenRigsProbe probe, int rig, InstrumentKind kind, int tickOrder, Transform root, List<Renderer> played)
        {
            _probe = probe; _rig = rig; Kind = kind; TickOrder = tickOrder; Root = root; _played = played;
        }

        public InstrumentKind Kind { get; }
        public int TickOrder { get; }
        public bool AcceptsStickPointers => Kind == InstrumentKind.Drums;
        public bool IsPresent => Root != null && Root.gameObject.activeInHierarchy && _probe.Rigs[_rig].InScene;
        public Transform Root { get; }
        public void Tick(IReadOnlyList<Pointer> pointers, float dt) => TickCount++;
        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow) { }
        public PlaySurface PlaySurfaceAt(Vector3 world) => _probe.SurfaceOf(_rig, world);
        public IReadOnlyList<Renderer> PlayedParts => _played;
        public bool Contains(Vector3 world) => PlaySurfaceAt(world) != PlaySurface.None || (_rig == 0 && _probe.Piano.InPlayZone(world));
    }

    internal sealed class FakePiano : IPianoApi
    {
        public FakePiano(Transform root) { Root = root; }
        public float W => 0.0235f;
        public float BW => 0.0137f;
        public float BTOP => 0.012f;
        public float L => GoldenLayout.PianoL;
        public Transform Root { get; }
        /// <summary>The piano's InPlayZone: x within the keys +3 cm, back -0.30..L+0.06, y -0.15..0.28 (piano-local Unity).</summary>
        public bool InPlayZone(Vector3 world)
        {
            Vector3 l = Root.InverseTransformPoint(world);
            return l.x > GoldenLayout.PianoX0 - 0.03f && l.x < -GoldenLayout.PianoX0 + 0.03f && l.z > -0.3f && l.z < GoldenLayout.PianoL + 0.06f && l.y > -0.15f && l.y < 0.28f;
        }
        public bool OverKeys(Vector3 world)
        {
            Vector3 l = Root.InverseTransformPoint(world);
            return l.z > -0.12f && l.y > -0.02f;
        }
        public bool TryGetKey(int midi, out PianoKeyInfo key)
        {
            key = new PianoKeyInfo { Midi = midi, X = GoldenLayout.PianoX0 + (midi - 21) * 0.0137f, Black = false, Label = "" };
            return midi >= 21 && midi <= 108;
        }
        public void PressMidi(int midi, object pointerKey, float vel = 0.75f) { }
        public void ReleaseMidi(int midi, object pointerKey) { }
        public void PlaybackOn(int midi, float vel) { }
        public void PlaybackOff(int midi) { }
        public void PlaybackReleaseAll() { }
        public void SetOnSurface(bool onTable) { }
        public void ResetSurface() { }
        public int HeldKeyCount => 0;
    }

    internal sealed class FakeDrums : IDrumsApi
    {
        readonly List<DrumPieceInfo> _pieces = new List<DrumPieceInfo>();
        public int ReleaseCount;

        public FakeDrums(Transform root)
        {
            Root = root;
            for (int i = 0; i < GoldenLayout.DrumPieces.Length; i++)
            {
                var d = GoldenLayout.DrumPieces[i];
                var t = new GameObject("piece-" + d.Kind + (d.Pad ? "-pad" : "")).transform;
                t.SetParent(root, false);
                t.localPosition = d.Pos;
                t.localRotation = d.Rot;
                _pieces.Add(new DrumPieceInfo { Id = i, Kind = d.Kind, Type = d.Cymbal ? DrumPieceType.Cymbal : DrumPieceType.Drum, Radius = d.R, Pad = d.Pad, Transform = t });
            }
        }

        public Transform Root { get; }
        public IReadOnlyList<DrumPieceInfo> Pieces => _pieces;
        public int KickId => 4;
        public int KickPadId => 5;
        public int HatId => 6;
        public bool HatOpen => false;
        public void SetHatOpen(bool open, HandState hand, bool playback = false) { }
        public void PlaybackHit(string kind, float vel, DrumStrikeInfo strike) { }
        public void PlaybackChoke(string kind) { }
        public bool AnyStickHeld => false;
        public void ReleaseAllSticks() => ReleaseCount++;
        /// <summary>What TryPickUp answers (the sticks' TryGrab), and how often Layout asked.</summary>
        public StickPick PickAnswer = StickPick.None;
        public int PickCalls;
        public StickPick TryPickUp(HandState hand, Vector3 tip) { PickCalls++; return PickAnswer; }
        public bool EveryHandPointerFartherThan(float metres, int pieceId) => true;

        /// <summary>Over the drums: a piece's head / cymbal / pad (piece-local y -.12..+.25, within r + .04),
        /// else the hi-hat stand's pinch spot.</summary>
        public PlaySurface Over(Vector3 w)
        {
            for (int i = 0; i < _pieces.Count; i++)
            {
                Vector3 l = _pieces[i].Transform.InverseTransformPoint(w);
                if (l.y < -0.12f || l.y > 0.25f) continue;
                if (Mathf.Sqrt(l.x * l.x + l.z * l.z) > _pieces[i].Radius + 0.04f) continue;
                return PlaySurface.Drums;
            }
            Vector3 k = Root.InverseTransformPoint(w), hp = GoldenLayout.HatPos;
            if (Mathf.Sqrt((k.x - hp.x) * (k.x - hp.x) + (k.z - hp.z) * (k.z - hp.z)) < 0.08f && k.y > 0.12f && k.y < hp.y - 0.08f) return PlaySurface.HatStand;
            return PlaySurface.None;
        }
    }

    internal sealed class FakeHarp : IHarpApi
    {
        public FakeHarp(Transform root) { Root = root; }
        public Transform Root { get; }
        public int StringCount => 22;
        static readonly int[] Degrees = { 0, 2, 4, 5, 7, 9, 11 };
        public int MidiOf(int i) => 48 + 12 * (i / 7) + Degrees[i % 7];
        public Vector3 StringStart(int i) => GoldenLayout.HarpA[i];
        public Vector3 StringEnd(int i) => GoldenLayout.HarpB[i];
        public Vector3 PlayPoint(int i) => Vector3.Lerp(GoldenLayout.HarpA[i], GoldenLayout.HarpB[i], 0.5f);
        public void Highlight(int i, float k = 1f) { }
        public void PlaybackPluck(int i, float vel, float u = 0.35f, float speed = 0f, string mode = "through") { }
        public void PlaybackDamp(int i) { }

        /// <summary>Over the harp strings: within 5 cm of the string plane and 4 cm (in the plane) of a string.</summary>
        public bool OverStrings(Vector3 w)
        {
            Vector3 l = Root.InverseTransformPoint(w);
            if (Mathf.Abs(l.z) > 0.05f) return false;
            var p = new Vector2(l.x, l.y);
            for (int i = 0; i < 22; i++)
            {
                Vector2 a = GoldenLayout.HarpA[i], b = GoldenLayout.HarpB[i], d = b - a;
                float u = Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude);
                if (Vector2.Distance(p, a + d * u) <= 0.04f) return true;
            }
            return false;
        }
    }

    /// <summary>The menu panel as the Layout module sees it: a Rig (Lessons) whose group holds the golden panel's two plates,
    /// a grab zone (the plates' world box + 0.12) while shown, and the IInstrument the Core skips while it is held. Always
    /// present (ARCHITECTURE.md section 9); TickCount counts the ticks that reach its pokes (only while shown).</summary>
    internal sealed class FakeMenu : IMenuPanel, IInstrument
    {
        readonly GoldenRigsProbe _probe;
        readonly List<Renderer> _plates = new List<Renderer>();
        readonly List<Renderer> _none = new List<Renderer>();
        public int TickCount;

        public FakeMenu(GoldenRigsProbe probe)
        {
            _probe = probe;
            var root = new GameObject("FakeMenu").transform;
            root.SetParent(probe.App.WorldRoot, false);
            foreach (var p in GoldenLayout.PanelBoxes) _plates.Add(probe.AddBox(root, p, "plate"));
            root.gameObject.SetActive(false);
            Root = root;
            Rig = new Rig { Id = RigId.Lessons, Group = root, Priority = 10, HomeAll = MenuConstants.HomeSeat, HomeSingle = MenuConstants.HomeSeat };
            Rig.Instruments.Add(this);
        }

        public bool Shown { get; private set; }
        public MenuScreen Screen => MenuScreen.Home;
        public Rig Rig { get; }
        public Transform Root { get; }

        /// <summary>Shows the panel at <paramref name="at"/> (or 0.5 m ahead of the head), then clears it out of the
        /// instruments as the real menu does on open.</summary>
        public void ShowAt(Vector3? at)
        {
            Transform head = _probe.App.Head;
            Vector3 dir = head.forward; dir.y = 0f; dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 pos = at ?? head.position + dir * 0.5f + Vector3.down * 0.12f;
            Root.SetPositionAndRotation(pos, Quaternion.Euler(0f, Mathf.Atan2(pos.x - head.position.x, pos.z - head.position.z) * Mathf.Rad2Deg, 0f));
            Root.gameObject.SetActive(true);
            Shown = true;
            _probe.App.Get<IPlacementService>()?.ClearPanel(Rig);
        }

        public void Show(HandState hand = null) => ShowAt(null);
        public void Hide() { Shown = false; Root.gameObject.SetActive(false); }
        public void Toggle(HandState hand = null) { if (Shown) Hide(); else Show(hand); }
        public void Go(MenuScreen screen) { }
        public void Redraw() { }

        public Bounds? GrabZone
        {
            get
            {
                if (!Shown) return null;
                Bounds b = _plates[0].bounds;
                for (int i = 1; i < _plates.Count; i++) b.Encapsulate(_plates[i].bounds);
                b.Expand(2f * GrabConstants.ZonePad);
                return b;
            }
        }

        public InstrumentKind Kind => InstrumentKind.Menu;
        public int TickOrder => 50;
        public bool AcceptsStickPointers => false;
        public bool IsPresent => true;
        Transform IInstrument.Root => Root;
        public void Tick(IReadOnlyList<Pointer> pointers, float dt) { if (Shown) TickCount++; }
        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow) { }
        public PlaySurface PlaySurfaceAt(Vector3 world) { var z = GrabZone; return z.HasValue && z.Value.Contains(world) ? PlaySurface.Menu : PlaySurface.None; }
        public IReadOnlyList<Renderer> PlayedParts => _none;
    }

    internal sealed class FakeTutorial : ITutorial
    {
        public TutorialState State { get; set; }
        public string LessonId { get; set; }
        public InstrumentKind Instrument { get; set; }
        public int StopCount;
        public int Version => 0;
        public float Countdown => 0f;
        public string Hint => null;
        public float Progress => 0f;
        public int EventsDone => 0;
        public int EventsTotal => 0;
        public TutorialSummary Summary => default;
        public void Start(string lessonId) { LessonId = lessonId; State = TutorialState.Playing; }
        public void Stop() { State = TutorialState.Idle; StopCount++; }
    }

    internal sealed class FakeMr : IMixedReality
    {
        public bool Available => false;
        public bool Active { get; set; }
        public bool IsPlacing { get; set; }
        public MrStatus Status => MrStatus.Idle;
        public bool HasSpaceSetup => false;
        public Task Enter() { Active = true; return Task.CompletedTask; }
        public void Exit() { Active = false; }
        public Task RunSpaceSetup() => Task.CompletedTask;
        public bool TryGetPianoPose(out Pose pose, out bool onTable) { pose = default; onTable = false; return false; }
        public int StartPlacingCount;
        public void StartPlacing() { StartPlacingCount++; IsPlacing = true; }
    }
}
