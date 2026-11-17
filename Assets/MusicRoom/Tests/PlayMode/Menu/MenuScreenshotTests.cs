// Visual check of the palm menu (needs a GPU: Tools/unity.sh test-play --gpu). Boots the app desktop-style, opens the
// panel and, for each screen, saves the painted 1024 x 768 canvas (canvas-*.png, exactly the texture on the menu's
// face) and the seated view from the desktop eye (seated-*.png) to $MUSICROOM_SHOTS_ROOT/menu, else $MUSICROOM_SHOTS,
// else Builds/Shots/menu (the Instruments and Lessons screens are the platform's tile grid). It also checks the canvas
// for the reference look: transparent rounded corners, a dark frosted face, the warm rim, the accent bar
// colour per screen, icons and text actually drawn, the right way up. The other GPU tests check the 15.6 rules on the
// running module: the canvas repaints only when its content changes (never idle, never for hover, once per user
// action), the hover highlight drawn by the face shader really lights the hovered row in the rendered view (and no
// other), and the panel stays inside the menu's draw budget (ARCHITECTURE.md section 14: menu <= 4).
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Menu;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // the AllocatingGCMemory() extension on Is.Not
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Menu
{
    public class MenuScreenshotTests
    {
        // MUSICROOM_SHOTS_ROOT (optional) redirects the shots of a whole run to <root>/menu, else
        // $MUSICROOM_SHOTS, else Builds/Shots/menu (PortraitTests.ShotDir).
        public static string ShotDir => PortraitTests.ShotDir;

        sealed class TutorialProbe : AppModule, ITutorial
        {
            public override int Order => 35;
            public override void Init(App app) => app.Register<ITutorial>(this);
            public TutorialState State { get; set; }
            public string LessonId { get; set; }
            public InstrumentKind Instrument { get; set; }
            public int Version { get; set; }
            public float Countdown { get; set; }
            public string Hint { get; set; }
            public float Progress => EventsTotal > 0 ? EventsDone / (float)EventsTotal : 0f;
            public int EventsDone { get; set; }
            public int EventsTotal { get; set; }
            public TutorialSummary Summary { get; set; }
            public void Start(string lessonId) { LessonId = lessonId; State = TutorialState.Countdown; Countdown = 3f; Version++; }
            public void Stop() { State = TutorialState.Idle; Version++; }
        }

        sealed class TakesProbe : AppModule, ITakeLibrary
        {
            public override int Order => 36;
            public override void Init(App app) => app.Register<ITakeLibrary>(this);
            public readonly List<TakeInfo> Infos = new List<TakeInfo>();
            public bool? Available { get; set; } = true;
            public int Version { get; set; }
#pragma warning disable 67
            public event System.Action Changed;
#pragma warning restore 67
            public Task<List<TakeInfo>> List()
            {
                var list = new List<TakeInfo>(Infos);
                list.Sort((a, b) => b.created.CompareTo(a.created));   // newest first, as the take library lists them
                return Task.FromResult(list);
            }
            public Task<TakeRecord> Get(string id)
            {
                foreach (var t in Infos)
                    if (t.id == id)
                    {
                        var rec = new TakeRecord { id = t.id, name = t.name, created = t.created, seconds = t.seconds, events = new List<TakeEvent>() };
                        // a little tune over the take's length, the kit under it (the row's constellation picture draws it)
                        int[] tune = { 60, 64, 67, 72, 69, 65, 62, 67, 71, 74, 72, 64 };
                        for (int i = 0; i < t.count; i++)
                        {
                            TakeEvent e = TakeEvent.PianoOn(tune[(i + t.id.Length) % tune.Length] + (t.id.EndsWith("b") ? 2 : 0), 0.5f + 0.1f * (i % 4));
                            e.t = t.seconds * i / Mathf.Max(1, t.count);
                            rec.events.Add(e);
                            if (i % 3 == 0) { TakeEvent d = TakeEvent.Drum(i % 6 == 0 ? "kick" : "snare", 0.8f, 0.3f, "head", false); d.t = e.t; rec.events.Add(d); }
                        }
                        return Task.FromResult(rec);
                    }
                return Task.FromResult<TakeRecord>(null);
            }
            public Task<TakeRecord> Put(TakeRecord take) => Task.FromResult(take);
            public Task<bool> Remove(string id) => Task.FromResult(Infos.RemoveAll(t => t.id == id) > 0);
            public string FormatLength(double seconds) => MenuText.FormatLength(seconds);
            public string FormatDate(long createdMs) => System.DateTimeOffset.FromUnixTimeMilliseconds(createdMs).ToUniversalTime().ToString("d MMM, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>A tour for the Sunny screen when the app has none (the menu reads its tips from it).</summary>
        sealed class TourProbe : IGuideTour
        {
            public void StartTour() { }
            public int TipCount => 3;
            public string TipId(int i) => "tip_" + i;
            public string TipTitle(int i) => "Tip " + i;
            public bool TipHeard(int i) => i == 0;
            public int TipsHeard => 1;
            public void PlayTip(string tipId) { }
            public bool TipsEnabled { get; set; } = true;
        }

        sealed class GuideProbe : AppModule, IGuide
        {
            public override int Order => 45;
            public override void Init(App app) => app.Register<IGuide>(this);
            public bool Present { get; set; }
            public bool Done { get; set; } = true;
            public void Start() => Present = true;
            public void Dismiss() => Present = false;
            public void Toggle() => Present = !Present;
        }

        sealed class MrProbe : AppModule, IMixedReality
        {
            public override int Order => 65;
            public override void Init(App app) => app.Register<IMixedReality>(this);
            public bool Available { get; set; } = true;
            public bool Active { get; set; }
            public bool IsPlacing => false;
            public MrStatus Status { get; set; }
            public bool HasSpaceSetup => Status == MrStatus.Loaded;
            public Task Enter() { Active = true; return Task.CompletedTask; }
            public void Exit() => Active = false;
            public Task RunSpaceSetup() => Task.CompletedTask;
            public bool TryGetPianoPose(out Pose pose, out bool onTable) { pose = default; onTable = false; return false; }
            public void StartPlacing() { }
        }

        static float[] s_keep;
        TestBoot _boot;
        FakeHandSource _fake;
        TutorialProbe _tut;
        TakesProbe _takes;
        GuideProbe _guide;
        MrProbe _mr;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: false);
            _fake = _boot.Hands;
            _tut = new TutorialProbe();
            _takes = new TakesProbe();
            _guide = new GuideProbe();
            _mr = new MrProbe();
            _boot.Options.ExtraModules.Add(_tut);
            _boot.Options.ExtraModules.Add(_takes);
            _boot.Options.ExtraModules.Add(_guide);
            _boot.Options.ExtraModules.Add(_mr);
            MenuBootGuard.Begin();   // the known SynthSource boot race only (CONTRACT-REQUESTS.md 3)
            yield return _boot.Start();
            MenuBootGuard.End();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MenuBootGuard.Stop();
            yield return _boot.Stop();
        }

        /// <summary>GPU tests start from the settled desktop room with the panel open on <paramref name="screen"/>, its
        /// open animation done and its canvas painted.</summary>
        IEnumerator OpenSettled(MenuModule m, MenuScreen screen)
        {
            var app = App.Current;
            float t0 = Time.realtimeSinceStartup;
            var layout = app.Get<ILayoutService>();
            while ((app.Rig.State == XrState.Starting || !layout.FirstArrangeDone) && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Assert.That(m.FaceTexture, Is.Not.Null, "the canvas exists with a GPU");
            m.Show();
            m.Go(screen);
            t0 = Time.realtimeSinceStartup;
            while (m.AnimK < 1f && Time.realtimeSinceStartup - t0 < 1f) yield return null;
            Assert.That(m.AnimK, Is.EqualTo(1f));
            // The grid's portraits render one per frame once it is shown, then the grid repaints once: let that settle.
            int still = 0, rendered = -1;
            t0 = Time.realtimeSinceStartup;
            while (still < 4 && Time.realtimeSinceStartup - t0 < 3f)
            {
                yield return null;
                int now = m.Portraits != null ? m.Portraits.Rendered : 0;
                still = now == rendered ? still + 1 : 0;
                rendered = now;
            }
            for (int i = 0; i < 3; i++) yield return null;
        }

        /// <summary>The right index tip at canvas (px, py), z metres out of the face, the hand behind it toward the player.</summary>
        void TipAt(MenuModule m, float px, float py, float z)
        {
            Vector3 tip = m.Root.TransformPoint(MenuMath.CanvasPoint(px, py, z));
            var rot = Quaternion.LookRotation(m.Root.forward, Vector3.up);
            _fake.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, tip - m.Root.forward * 0.18f, rot).WithPointing().WithIndexTipAt(tip));
        }

        static Texture2D Read(RenderTexture rt, bool linear)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, linear);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        /// <summary>A canvas pixel (origin top-left, y down) of the read-back canvas, as 0..255.</summary>
        static Color32 Px(Texture2D t, int x, int y) => t.GetPixel(x, t.height - 1 - y);

        static void Save(Texture2D t, string name)
        {
            Directory.CreateDirectory(ShotDir);
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), t.EncodeToPNG());
        }

        /// <summary>The seated view from the desktop eye (other shot tests of the menu use it).</summary>
        public static Texture2D SeatedView(Camera cam, int w, int h) => Seated(cam, w, h);

        static Texture2D Seated(Camera cam, int w, int h)
        {
            // The camera renders into its own MSAA target (URP asset) and resolves into this one.
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            var prevTarget = cam.targetTexture;
            var prevBg = cam.backgroundColor;
            cam.backgroundColor = XrMode.VrBackground;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = prevTarget; }
            cam.backgroundColor = prevBg;
            var tex = Read(rt, false);
            rt.Release();
            Object.Destroy(rt);
            return tex;
        }

        IEnumerator Settle(MenuModule m)
        {
            m.Redraw();
            yield return null;
            yield return null;
            yield return null;
        }

        IEnumerator Shot(MenuModule m, Camera cam, string name, List<Texture2D> keep, Dictionary<string, Texture2D> canvases)
        {
            yield return Settle(m);
            var canvas = Read(m.FaceTexture, true);
            Save(canvas, "canvas-" + name);
            canvases[name] = canvas;
            var view = Seated(cam, 1280, 960);
            Save(view, "seated-" + name);
            keep.Add(view);
        }

        /// <summary>Pixels of ink-coloured paint in a canvas box (ink: r &gt; 180, g &gt; 170; DIM text over
        /// the dark face blends to about 140, so the footer uses a lower threshold, r &gt; 100).</summary>
        static int CountInk(Texture2D t, int x0, int y0, int w, int h, int minR = 180, int minG = 170)
        {
            int n = 0;
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                {
                    Color32 c = Px(t, x, y);
                    if (c.r > minR && c.g > minG && c.a > 200) n++;
                }
            return n;
        }

        [UnityTest, Category("Graphics")]
        public IEnumerator TheSunnyScreenAndTheRecordingsBinArePainted()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a GPU: Tools/unity.sh test-play --gpu");
            var app = App.Current;
            var m = (MenuModule)app.Get<IMenuPanel>();
            var ctl = m.Controller;
            // The Sunny screen (headset 2026-11-10: it showed only its header): every row painted, with its note.
            if (ctl.Tour == null) ctl.Tour = new TourProbe();
            yield return OpenSettled(m, MenuScreen.Guide);
            yield return Settle(m);
            Assert.That(m.Screen, Is.EqualTo(MenuScreen.Guide));
            Assert.That(ctl.RowCount, Is.EqualTo(6), "back, Show me around, Tips, Make a song in layers, Tips while I play, Look and pinch");
            var sunny = Read(m.FaceTexture, true);
            Save(sunny, "canvas-sunny");
            for (int i = 1; i < ctl.RowCount; i++)
            {
                MenuRow row = ctl.Row(i);
                int ink = CountInk(sunny, (int)row.X0 + 170, (int)row.Y0 + 10, 420, (int)(row.Y1 - row.Y0) - 20);
                Assert.That(ink, Is.GreaterThan(400), $"Sunny screen row {i} ('{row.Label}') is painted");
            }
            // Recordings with two takes: a bin beside each; poked once it reads "Delete?" in red.
            _takes.Infos.Add(new TakeInfo { id = "take-a", name = "Take 1 · 0:12", created = 1761000000000, seconds = 12.2f, count = 38 });
            _takes.Infos.Add(new TakeInfo { id = "take-b", name = "Take 2 · 1:05", created = 1761003600000, seconds = 65f, count = 210 });
            _takes.Version++;
            m.Go(MenuScreen.Recordings);
            yield return Settle(m);
            int bin = -1;
            for (int i = 0; i < ctl.RowCount; i++) if (ctl.Row(i).Id == RowKind.Trash) { bin = i; break; }
            Assert.That(bin, Is.GreaterThan(0), "a bin beside each take");
            var plain = Read(m.FaceTexture, true);
            Save(plain, "canvas-recordings-bins");
            ctl.BeginPokes(app.Clock.NowMs + 1500);
            ctl.Fire(bin);
            ctl.EndPokes();
            Assert.That(ctl.TrashArmedId, Is.EqualTo(ctl.Row(bin).Take.id));
            yield return Settle(m);
            var ask = Read(m.FaceTexture, true);
            Save(ask, "canvas-recordings-delete-ask");
            MenuRow b = ctl.Row(bin);
            int red = CountInk(ask, (int)b.X0 + 8, (int)b.Y0 + 10, (int)(b.X1 - b.X0) - 16, (int)(b.Y1 - b.Y0) - 20, 200, 0);
            int before = CountInk(plain, (int)b.X0 + 8, (int)b.Y0 + 10, (int)(b.X1 - b.X0) - 16, (int)(b.Y1 - b.Y0) - 20, 200, 0);
            Debug.Log($"[Menu] bin cell red pixels: {before} as a bin, {red} asking");
            Assert.That(red, Is.GreaterThan(before + 80), "the bin's red \"Delete?\"");
            Object.Destroy(sunny); Object.Destroy(plain); Object.Destroy(ask);
        }

        [UnityTest, Category("Graphics")]
        public IEnumerator CapturesEveryScreenAndChecksTheCanvas()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a GPU: Tools/unity.sh test-play --gpu");
            var app = App.Current;
            float t0 = Time.realtimeSinceStartup;
            var layout = app.Get<ILayoutService>();
            while ((app.Rig.State == XrState.Starting || !layout.FirstArrangeDone) && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            var m = (MenuModule)app.Get<IMenuPanel>();
            Assert.That(m.FaceTexture, Is.Not.Null, "the canvas exists with a GPU");
            Camera cam = app.Rig.HeadCamera;
            m.Show();
            t0 = Time.realtimeSinceStartup;
            while (m.AnimK < 1f && Time.realtimeSinceStartup - t0 < 1f) yield return null;
            var keep = new List<Texture2D>();
            var canvases = new Dictionary<string, Texture2D>();
            var ctl = m.Controller;

            // Home: Lessons, Instruments, Move instruments, Reset view, Recordings, "Play in my room" (VR), "Show me around", Exit.
            yield return Shot(m, cam, "home", keep, canvases);
            Assert.That(m.Paints, Is.GreaterThan(0));
            // Home in mixed reality without a Space Setup: nine rows (with "Set up my room"; "Real hands" is on the
            // Instruments screen's bottom row, out of a stray poke's way); Sunny here.
            _mr.Active = true; _mr.Status = MrStatus.NoRooms; _guide.Present = true;
            yield return Shot(m, cam, "home-mr-setup", keep, canvases);
            Assert.That(ctl.RowCount, Is.EqualTo(9));
            Assert.That(ctl.Row(3).Id, Is.EqualTo(RowKind.ResetView), "Reset view after Move instruments");
            for (int i = 0; i < ctl.RowCount; i++) Assert.That(ctl.Row(i).Id, Is.Not.EqualTo(RowKind.Hands), "no Real hands row on the home page");
            // The Instruments screen in mixed reality: the bottom row has Real hands beside Move.
            m.Go(MenuScreen.Instruments);
            yield return Settle(m);
            yield return Shot(m, cam, "instruments-mr", keep, canvases);
            Assert.That(ctl.Row(RowOf(ctl, RowKind.Hands)).Label, Is.EqualTo("Real hands"));
            Assert.That(ctl.Row(RowOf(ctl, RowKind.Hands)).X1, Is.LessThanOrEqualTo(992f), "the four controls fit the panel");
            m.Go(MenuScreen.Home);
            yield return Settle(m);
            _mr.Active = false; _mr.Status = MrStatus.Idle; _guide.Present = false;

            // Instruments (the tile grid, platform.md 5.1) with the Move button, a fingertip hovering it (highlight + glow).
            m.Go(MenuScreen.Instruments);
            yield return Settle(m);
            int lockIndex = RowOf(ctl, RowKind.Arrange);
            var lockRow = ctl.Row(lockIndex);
            Vector3 tip = m.Root.TransformPoint(MenuMath.CanvasPoint(lockRow.X0 + 40f, lockRow.CentreY, 0.004f));
            var rot = Quaternion.LookRotation(m.Root.forward, Vector3.up);
            _fake.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, tip - m.Root.forward * 0.18f, rot).WithPointing().WithIndexTipAt(tip));
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.3f) yield return null;
            Assert.That(ctl.HoverRow, Is.EqualTo(lockIndex), "the fingertip hovers the Move button");
            Assert.That(ctl.Row(ctl.RowCount - 1).Id, Is.EqualTo(RowKind.Arrange), "Move ends the bottom row (no sound switch, even with the audio engine)");
            yield return Shot(m, cam, "instruments", keep, canvases);
            _fake.SetTracked(HandSide.Right, false);
            yield return null;

            // Courses (piano: 13, scrolling) and the same list scrolled half way.
            m.Go(MenuScreen.Lessons);
            yield return Shot(m, cam, "lessons", keep, canvases);
            yield return null;
            ctl.BeginPokes(app.Clock.NowMs + 1000);
            ctl.Fire(1);   // Piano
            ctl.EndPokes();
            Assert.That(m.Screen, Is.EqualTo(MenuScreen.Courses));
            yield return Shot(m, cam, "courses", keep, canvases);
            ctl.SetScroll(530f);
            yield return Shot(m, cam, "courses-scrolled", keep, canvases);

            // A lesson in progress: countdown, then waiting on a note with a hit and a mistake, then done.
            _tut.EventsTotal = 15;
            _tut.Start("piano-scale");
            _tut.Countdown = 2.2f;
            _tut.Version++;
            yield return Shot(m, cam, "lesson-countdown", keep, canvases);
            Assert.That(m.Screen, Is.EqualTo(MenuScreen.Lesson));
            _tut.State = TutorialState.Waiting; _tut.Hint = "Play E4"; _tut.EventsDone = 2; _tut.Summary = new TutorialSummary { Mistakes = 1 }; _tut.Version++;
            yield return Shot(m, cam, "lesson", keep, canvases);
            _tut.State = TutorialState.Done; _tut.EventsDone = 15; _tut.Summary = new TutorialSummary { Notes = 15, Mistakes = 1, Seconds = 23.4f }; _tut.Version++;
            yield return Shot(m, cam, "lesson-done", keep, canvases);
            _tut.Stop();

            // Recordings: empty, then two takes and a take screen asking to delete.
            m.Go(MenuScreen.Recordings);
            yield return Shot(m, cam, "recordings-empty", keep, canvases);
            _takes.Infos.Add(new TakeInfo { id = "take-a", name = "Take 1 · 0:12", created = 1761000000000, seconds = 12.2f, count = 38 });
            _takes.Infos.Add(new TakeInfo { id = "take-b", name = "Take 2 · 1:05", created = 1761003600000, seconds = 65f, count = 210 });
            _takes.Version++;
            yield return null;
            yield return Shot(m, cam, "recordings", keep, canvases);
            ctl.BeginPokes(app.Clock.NowMs + 2000);
            ctl.Fire(1);
            ctl.EndPokes();
            yield return null;
            ctl.BeginPokes(app.Clock.NowMs + 3000);
            ctl.Fire(RowOf(ctl, RowKind.Delete));   // Delete: asks (Keep painting sits under it)
            ctl.EndPokes();
            Assert.IsTrue(ctl.Confirm);
            yield return Shot(m, cam, "take", keep, canvases);

            // Repainting an unchanged screen (e.g. a service poked Redraw with nothing new) allocates nothing.
            m.View.Paint(ctl, app.Clock.NowMs);
            Assert.That(() => { s_keep = new float[64]; }, Is.AllocatingGCMemory(), "calibration");
            Assert.That(() => { for (int i = 0; i < 5; i++) m.View.Paint(ctl, app.Clock.NowMs); }, Is.Not.AllocatingGCMemory());
            // Every parked text object left TMP's per-camera update queue (isTextObjectScaleStatic): no per-frame cost.
            Assert.That(m.View.Canvas.TextObjects, Is.GreaterThan(10));
            Assert.That(m.View.Canvas.StaticTextObjects, Is.EqualTo(m.View.Canvas.TextObjects), "every text slot is scale-static");

            // The summon: the panel put away, an open palm toward the face, the ring charging on it.
            m.Hide();
            t0 = Time.realtimeSinceStartup;
            while (m.View.VisualActive && Time.realtimeSinceStartup - t0 < 1f) yield return null;
            Vector3 head = app.Head.position;
            Vector3 wrist = head + new Vector3(0.06f, -0.2f, 0.3f);
            _fake.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, wrist, Vector3.up, (head - (wrist + new Vector3(0f, 0.07f, 0f))).normalized));
            t0 = Time.realtimeSinceStartup;
            while (m.Gesture.T < 0.3f && !m.Shown && Time.realtimeSinceStartup - t0 < 2f) yield return null;
            Assert.IsTrue(m.View.Ring.gameObject.activeSelf, "the ring charges on the palm");
            var ringShot = Seated(cam, 1280, 960);
            Save(ringShot, "seated-summon-ring");
            keep.Add(ringShot);
            t0 = Time.realtimeSinceStartup;
            while (!m.Shown && Time.realtimeSinceStartup - t0 < 2f) yield return null;
            Assert.IsTrue(m.Shown, "and the panel opens in front of the hand");
            t0 = Time.realtimeSinceStartup;
            while (m.AnimK < 1f && Time.realtimeSinceStartup - t0 < 1f) yield return null;
            yield return null;
            var opened = Seated(cam, 1280, 960);
            Save(opened, "seated-summoned");
            keep.Add(opened);

            // ---- the canvas: the reference look ----
            var home = canvases["home"];
            Assert.That(Px(home, 2, 2).a, Is.EqualTo(0), "rounded corner: transparent");
            Assert.That(Px(home, 10, 10).a, Is.EqualTo(0));
            Assert.That(Px(home, 1013, 757).a, Is.EqualTo(0));
            Assert.That(Px(home, 70, 70).a, Is.GreaterThan(0), "inside the 2 cm corner radius");
            Color32 mid = Px(home, 600, 268);   // the panel between two rows (eight home rows since Reset view: 512, 300 is a label)
            Assert.That(mid.a, Is.GreaterThan(230)); Assert.That(mid.r, Is.LessThan(60)); Assert.That(mid.b, Is.LessThan(80)); Assert.That(mid.b, Is.GreaterThanOrEqualTo(mid.r), "dark, frosted, cool");
            Color32 rim = Px(home, 512, 3);
            Assert.That(rim.a, Is.GreaterThan(0)); Assert.That(rim.r, Is.GreaterThanOrEqualTo(rim.b), "warm rim");
            Color32 bar = Px(home, 60, 117);
            Assert.That(bar.b, Is.GreaterThan(200)); Assert.That(bar.r, Is.LessThan(140), "cyan accent bar on home (and the canvas is the right way up)");
            Color32 amber = Px(canvases["instruments"], 60, 117);
            Assert.That(amber.r, Is.GreaterThan(200)); Assert.That(amber.b, Is.LessThan(160), "amber on instruments");
            Color32 rose = Px(canvases["recordings"], 60, 117);
            Assert.That(rose.r, Is.GreaterThan(200)); Assert.That(rose.g, Is.LessThan(180), "rose on recordings");
            for (int i = 0; i < 3; i++)
            {
                var r = new[] { 132, 244, 356 }[i];
                Assert.That(CountInk(home, 74, r + 14, 72, 72), Is.GreaterThan(150), "home row " + i + " icon drawn");
            }
            Assert.That(CountInk(home, 300, 700, 424, 48, 100, 90), Is.GreaterThan(200), "the footer text is drawn");
            Assert.That(CountInk(home, 48, 30, 300, 55), Is.GreaterThan(300), "the title is drawn");
            // The countdown digit: a big cyan glyph around (512, 420).
            int cyan = 0;
            var cd = canvases["lesson-countdown"];
            for (int y = 330; y < 510; y += 2) for (int x = 450; x < 580; x += 2) { var c = Px(cd, x, y); if (c.b > 200 && c.r < 140) cyan++; }
            Assert.That(cyan, Is.GreaterThan(300), "the countdown digit");
            foreach (var t in keep) Object.Destroy(t);
            foreach (var t in canvases.Values) Object.Destroy(t);
            Debug.Log("[Menu] screenshots saved to " + ShotDir);
        }

        [UnityTest, Category("Graphics")]
        public IEnumerator TheCanvasRepaintsOnlyWhenItsContentChanges()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a GPU: Tools/unity.sh test-play --gpu");
            var m = (MenuModule)App.Current.Get<IMenuPanel>();
            var ctl = m.Controller;
            // Move instruments on first (it closes the panel), so the poke in (c) turns it off with the panel open.
            var arrange = App.Current.Get<IArrangeMode>();
            arrange.BeginArrange();
            yield return null;
            yield return OpenSettled(m, MenuScreen.Instruments);
            int p = m.Paints;
            Assert.That(p, Is.GreaterThan(0), "the open panel was painted");
            Assert.IsFalse(ctl.Dirty);

            // (a) idle frames never repaint
            for (int i = 0; i < 20; i++) yield return null;
            Assert.That(m.Paints, Is.EqualTo(p), "20 idle frames: no repaint");

            // (b) hover never repaints: the highlight and its 120 ms fades are face-material uniforms
            MenuRow piano = ctl.Row(2);
            TipAt(m, (piano.X0 + piano.X1) / 2f, piano.CentreY, 0.005f);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.That(ctl.HoverRow, Is.EqualTo(2), "the tip really hovered the row");
            TipAt(m, (piano.X0 + piano.X1) / 2f, piano.CentreY, 0.03f);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.That(ctl.HoverRow, Is.EqualTo(-1));
            Assert.That(m.Paints, Is.EqualTo(p), "hover in and out: no repaint");

            // (c) one user action, one repaint: a poke on Move (out at +3 cm to arm, then 3 mm in) turns it off
            int lockRow = RowOf(ctl, RowKind.Arrange);
            Assert.That(lockRow, Is.GreaterThan(0));
            Assert.IsTrue(arrange.MoveOn);
            MenuRow lk = ctl.Row(lockRow);
            TipAt(m, (lk.X0 + lk.X1) / 2f, lk.CentreY, 0.03f);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.3f) yield return null;   // armed, the slot's debounce long over
            Assert.That(m.Paints, Is.EqualTo(p), "arming: no repaint");
            TipAt(m, (lk.X0 + lk.X1) / 2f, lk.CentreY, -0.003f);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsFalse(arrange.MoveOn, "the poke turned Move instruments off");
            Assert.That(m.Paints, Is.EqualTo(p + 1), "exactly one repaint for the action");
            for (int i = 0; i < 10; i++) yield return null;
            Assert.That(m.Paints, Is.EqualTo(p + 1), "and none after it while the finger rests on the face");
        }

        static int RowOf(MenuController ctl, RowKind id)
        {
            for (int i = 0; i < ctl.RowCount; i++) if (ctl.Row(i).Id == id) return i;
            return -1;
        }

        static Color32 ScreenPx(Texture2D t, Vector3 screen)
        {
            int x = Mathf.RoundToInt(screen.x), y = Mathf.RoundToInt(screen.y);
            // Read-back rows are bottom-up like screen coordinates: average a 3 x 3 patch.
            int r = 0, g = 0, b = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    Color32 c = t.GetPixel(x + dx, y + dy);
                    r += c.r; g += c.g; b += c.b;
                }
            return new Color32((byte)(r / 9), (byte)(g / 9), (byte)(b / 9), 255);
        }

        [UnityTest, Category("Graphics")]
        public IEnumerator TheFaceShaderLightsTheHoveredRowInTheRenderedView()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a GPU: Tools/unity.sh test-play --gpu");
            var m = (MenuModule)App.Current.Get<IMenuPanel>();
            var ctl = m.Controller;
            yield return OpenSettled(m, MenuScreen.Instruments);
            Camera cam = App.Current.Rig.HeadCamera;
            const int W = 1280, H = 960;
            int lockRow = RowOf(ctl, RowKind.Arrange);
            MenuRow lk = ctl.Row(lockRow);
            // The tile above the Move button (the grid's second row, platform.md 5.1).
            float probeX = lk.X1 - 18f;
            int harpRow = -1;
            for (int i = 0; i < ctl.RowCount; i++)
            {
                MenuRow t = ctl.Row(i);
                if (t.Id == RowKind.Tile && t.X0 <= probeX && probeX <= t.X1 && t.Y1 < lk.Y0) harpRow = i;
            }
            Assert.That(harpRow, Is.GreaterThan(0), "a tile above the Move button");
            MenuRow harp = ctl.Row(harpRow);
            // Probes at the button's right end (x1 - 18 px) on its middle line, and straight above it on the tile's name
            // line (printed paper). The tip hovers the Move button 40 px in from its left end, so neither the glow (4.5 cm)
            // nor the hand covers a probe.
            Vector3 lockPx = m.Root.TransformPoint(MenuMath.CanvasPoint(probeX, lk.CentreY - 30f, 0f));
            Vector3 harpPx = m.Root.TransformPoint(MenuMath.CanvasPoint(probeX, harp.Y0 + MenuStyle.TileNameY + 24f, 0f));

            _fake.SetTracked(HandSide.Right, false);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.3f) yield return null;
            Assert.That(ctl.HoverRow, Is.EqualTo(-1));
            var plain = Seated(cam, W, H);
            // Seated() renders the camera's own projection into W x H: a viewport point maps to (vx W, vy H), rows
            // bottom-up like the read-back texture.
            Vector3 vLock = cam.WorldToViewportPoint(lockPx), vHarp = cam.WorldToViewportPoint(harpPx);
            Vector3 sLock = new Vector3(vLock.x * W, vLock.y * H, 0f), sHarp = new Vector3(vHarp.x * W, vHarp.y * H, 0f);
            Assert.That(vLock.z, Is.GreaterThan(0f), "the panel is in front of the camera");
            Assert.That(sLock.x, Is.InRange(2f, W - 3f)); Assert.That(sLock.y, Is.InRange(2f, H - 3f));
            Assert.That(sHarp.y, Is.GreaterThan(sLock.y), "the tile is above the Move button on screen (the view is upright)");

            _fake.SetTracked(HandSide.Right, true);
            TipAt(m, lk.X0 + 40f, lk.CentreY, 0.004f);
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.3f) yield return null;   // the 120 ms fade has finished
            Assert.That(ctl.HoverRow, Is.EqualTo(lockRow));
            var lit = Seated(cam, W, H);
            Save(lit, "seated-hover-probe");

            Color32 a = ScreenPx(plain, sLock), b = ScreenPx(lit, sLock);
            Color32 ha = ScreenPx(plain, sHarp), hb = ScreenPx(lit, sHarp);
            Debug.Log($"[Menu] hover probe lock {a} -> {b} at {sLock}; harp {ha} -> {hb} at {sHarp}");
            Assert.That(b.b - a.b, Is.GreaterThanOrEqualTo(15), "the hovered row takes the cyan highlight (uv mapping and y flip right)");
            Assert.That(b.b, Is.GreaterThan(b.r), "cyan, not the amber hover glow");
            Assert.That(Mathf.Abs(hb.r - ha.r) + Mathf.Abs(hb.g - ha.g) + Mathf.Abs(hb.b - ha.b), Is.LessThanOrEqualTo(6), "the tile above is untouched (no mirrored or misplaced highlight)");
            Object.Destroy(plain);
            Object.Destroy(lit);
        }

        [UnityTest, Category("Graphics")]
        public IEnumerator TheOpenPanelStaysInsideTheMenuDrawBudget()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a GPU: Tools/unity.sh test-play --gpu");
            const int MenuDraws = 4;   // ARCHITECTURE.md sections 14 and 17
            var app = App.Current;
            var m = (MenuModule)app.Get<IMenuPanel>();
            float t0 = Time.realtimeSinceStartup;
            var layout = app.Get<ILayoutService>();
            while ((app.Rig.State == XrState.Starting || !layout.FirstArrangeDone) && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            var cam = app.Rig.HeadCamera;
            var rt = new RenderTexture(1024, 1024, 24) { name = "MenuDrawBudget" };
            cam.targetTexture = rt;
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            for (int i = 0; i < 6; i++) yield return null;
            long hidden = draws.LastValue;
            m.Show();
            t0 = Time.realtimeSinceStartup;
            while (m.AnimK < 1f && Time.realtimeSinceStartup - t0 < 1f) yield return null;
            for (int i = 0; i < 6; i++) yield return null;   // the opening repaint is over: an idle frame
            long shown = draws.LastValue;
            bool valid = draws.Valid;
            draws.Dispose();
            cam.targetTexture = null;
            Object.Destroy(rt);
            if (!valid) Assert.Ignore("render counters unavailable in this player");
            Debug.Log($"[Menu] draws hidden={hidden} shown={shown} (menu budget {MenuDraws})");
            Assert.That(m.View.Face.isVisible || m.View.Back.isVisible, "the panel was in view");
            Assert.That(shown - hidden, Is.InRange(1, MenuDraws), "face + back plate (the ring only while charging)");
        }
    }
}
