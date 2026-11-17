// The music book in the real room (GPU: Tools/unity.sh test-play --gpu; sheet.md 9.3 SheetShotsTests). Pictures to
// $MUSICROOM_SHOTS_ROOT/sheet, else $MUSICROOM_SHOTS, else Builds/Shots/sheet:
//   sheet-seat-home        what the home seat sees at the Quest 3S's 20 px per degree (key fronts 0.45 m ahead, eyes 0.48 m
//                          above the key tops), and -zoom4x (the same display pixels blown up 4 times)
//   sheet-lesson-marker    a lesson running: the book on the song, the warm marker on the next note (seat, 20 px/deg)
//   sheet-contents         the contents spread, straight on
//   sheet-midflip          a page half way through its turn
//   sheet-closeup          notes, letters, the gutter and the ribbon up close
//   sheet-overview         the piano with the book and its lamp, from behind the seat
// Checks: 2 draws for the book idle and turning; the page centre is cream and lit (linear luminance >= 0.40 in the night
// room); the marked note's paper is warmer than its neighbour's; and the staff space and letter heights in display pixels
// from the home seat (printed as [Sheet] readability lines).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using MusicRoom.Sheet;
using MusicRoom.Sheet.Engrave;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Sheet
{
    [Category("Graphics")]
    public class SheetShotsTests
    {
        const float Dt = 1f / 72f;
        const float PxPerDeg = 20f;            // Quest 3S at the lens centre
        TestBoot _boot;
        double _t;
        SheetModule _m;
        Camera _cam;
        GameObject _camGo;
        RenderTexture _rt;
        Texture2D _read;
        int _w, _h;

        static string ShotsDir
        {
            get
            {
                string d = ShotDirs.FromRoot("sheet") ?? Environment.GetEnvironmentVariable("MUSICROOM_SHOTS");
                if (string.IsNullOrEmpty(d)) d = ShotDirs.Default("sheet");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        void Step(int n = 1) { for (int i = 0; i < n; i++) { _t += Dt; _boot.Step(Dt); } }

        IEnumerator Boot()
        {
            _t = 50.0;
            _boot = new TestBoot(headless: false, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            // Sunny's tips (the book's own and the platform's) heard already: nobody walks into the pictures
            var seen = new List<string>();
            foreach (var t in new SheetTipSource().Tips()) seen.Add(t.Id);
            foreach (var t in new MusicRoom.Guide.PlatformTips().Tips()) seen.Add(t.Id);
            foreach (var d in InstrumentRegistry.Discover().Every) foreach (var t in d.Tips) seen.Add(t.Id);
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, string.Join(",", seen));
            _failures.Clear();
            _boot.Hands.SetTracked(HandSide.Left, false);
            _boot.Hands.SetTracked(HandSide.Right, false);
            yield return _boot.Start();
            foreach (var x in App.Current.Modules) if (x is SheetModule s) _m = s;
            var layout = App.Current.Get<ILayoutService>();
            float t0 = Time.realtimeSinceStartup;
            while (!layout.FirstArrangeDone && Time.realtimeSinceStartup - t0 < 5f) { Step(); yield return null; }
            MusicRoom.Room.RoomModule room = null;
            foreach (var x in App.Current.Modules) if (x is MusicRoom.Room.RoomModule r) room = r;
            t0 = Time.realtimeSinceStartup;
            while ((!_m.Ready || _m.Pages.Pending || (room != null && (room.TexturesPending > 0 || !room.ProbeRendered))) && Time.realtimeSinceStartup - t0 < 120f) { Step(); yield return null; }
            Assert.That(_m.Ready, Is.True);
            for (int i = 0; i < 4; i++) { Step(); yield return null; }
        }

        void Camera(int w, int h)
        {
            if (_rt != null && (_w != w || _h != h)) { _rt.Release(); Object.Destroy(_rt); Object.Destroy(_read); _rt = null; }
            _w = w; _h = h;
            if (_rt == null)
            {
                _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                _read = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            }
            if (_camGo == null)
            {
                _camGo = new GameObject("SheetShotCamera");
                _cam = _camGo.AddComponent<Camera>();
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = XrMode.VrBackground;
                _cam.nearClipPlane = 0.02f;
                _cam.farClipPlane = 40f;
                _cam.allowHDR = false;
                _cam.enabled = false;
            }
            _cam.targetTexture = _rt;
            _cam.aspect = w / (float)h;
        }

        void Aim(Vector3 from, Vector3 at, float vfov)
        {
            _cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
            _cam.fieldOfView = vfov;
        }

        void Shot(string name)
        {
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, _w, _h), 0, 0);
            _read.Apply();
            RenderTexture.active = prev;
            if (name != null) File.WriteAllBytes(Path.Combine(ShotsDir, name + ".png"), _read.EncodeToPNG());
        }

        Color At(Vector3 world, int r = 1)
        {
            Vector3 sp = _cam.WorldToScreenPoint(world);
            int px = Mathf.Clamp(Mathf.RoundToInt(sp.x), r, _w - 1 - r), py = Mathf.Clamp(Mathf.RoundToInt(sp.y), r, _h - 1 - r);
            Color sum = Color.clear; int n = 0;
            for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) { sum += _read.GetPixel(px + dx, py + dy); n++; }
            return sum / n;
        }

        /// <summary>A crop around a world point, each display pixel blown up `k` times (nearest), saved as PNG.</summary>
        void Zoom(string name, Vector3 centre, int cw, int ch, int k)
        {
            Vector3 sp = _cam.WorldToScreenPoint(centre);
            int x0 = Mathf.Clamp(Mathf.RoundToInt(sp.x) - cw / 2, 0, _w - cw), y0 = Mathf.Clamp(Mathf.RoundToInt(sp.y) - ch / 2, 0, _h - ch);
            var z = new Texture2D(cw * k, ch * k, TextureFormat.RGBA32, false, false);
            var px = new Color32[cw * k * ch * k];
            var src = _read.GetPixels32();
            for (int y = 0; y < ch * k; y++) for (int x = 0; x < cw * k; x++) px[y * cw * k + x] = src[(y0 + y / k) * _w + x0 + x / k];
            z.SetPixels32(px); z.Apply();
            File.WriteAllBytes(Path.Combine(ShotsDir, name + ".png"), z.EncodeToPNG());
            Object.Destroy(z);
        }

        Transform Piano => App.Current.Get<IPianoApi>().Root;
        /// <summary>The home seat's eyes (sheet.md 1.1): 0.48 m above the key tops, 0.45 m in front of the key fronts.</summary>
        Vector3 HomeEye => Piano.TransformPoint(new Vector3(0f, 0.48f, -0.45f));
        Vector3 FittedEye => Piano.TransformPoint(new Vector3(0f, 0.48f, -0.36f));
        float SBottom => App.Current.Get<IPianoApi>().MusicRestLedge + BookGeometry.Square;
        Vector3 PageWorld(int sigma, float xmm, float ymm)
        {
            float s = sigma > 0 ? xmm / 1000f : BookGeometry.PageW - xmm / 1000f;
            return _m.Root.TransformPoint(BookGeometry.PagePoint(sigma, s, BookGeometry.PageH - ymm / 1000f, SBottom));
        }
        Vector3 BookCentre => _m.Root.TransformPoint(new Vector3(0f, SBottom + BookGeometry.PageH * 0.5f, -0.008f));

        static float Lum(Color srgb) { var l = srgb.linear; return 0.2126f * l.r + 0.7152f * l.g + 0.0722f * l.b; }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_camGo != null) Object.Destroy(_camGo);
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            if (_read != null) Object.Destroy(_read);
            if (_boot != null) yield return _boot.Stop();
            _boot = null; _m = null;
        }

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) { Step(); yield return null; } }

        [UnityTest, Timeout(600000)]
        public IEnumerator TheBookFromTheSeatTheMarkerTheContentsAMidFlipAndACloseUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics): run Tools/unity.sh test-play --gpu");
            yield return Boot();
            Assert.That(_m.Pages.Gpu, Is.True, "the GPU upload path (CopyTexture)");
            var head = App.Current.Rig.HeadCamera;
            if (head != null) head.enabled = false;

            // ---- 1. the home seat at 20 px/deg (the contents)
            const int SW = 1000, SH = 760;
            Camera(SW, SH);
            Aim(HomeEye, BookCentre, SH / PxPerDeg);
            Shot("sheet-seat-home");
            Color paper = At(PageWorld(1, 160f, 122f), 2);
            Color low = At(PageWorld(1, 160f, 225f), 2), top = At(PageWorld(1, 160f, 20f), 2);
            Debug.Log($"[Sheet] seat view: page centre sRGB {paper}, linear luminance {Lum(paper):F3} (top {Lum(top):F3}, bottom {Lum(low):F3})");
            Check(Lum(paper) >= 0.40f, $"the page centre is lit (linear luminance {Lum(paper):F3} >= 0.40, the reading lamp in the night room)");
            Check(paper.r > paper.b + 0.05f, "cream, not white or grey");
            Zoom("sheet-seat-home-zoom4x", PageWorld(-1, 160f, 60f), 260, 130, 4);
            Readability("home", HomeEye);
            Readability("fitted", FittedEye);

            // ---- 2. a lesson running: the book turns to the song, the marker on the next note
            var tut = App.Current.Get<ITutorial>() as Tutorial;
            tut.Start("piano-ode");
            float t0 = Time.realtimeSinceStartup;
            while ((tut.State != TutorialState.Waiting || _m.Turning || _m.Pages.Pending) && Time.realtimeSinceStartup - t0 < 30f) { Step(); yield return null; }
            var piano = App.Current.Get<IPianoApi>();
            var presser = new object();
            for (int i = 0; i < 4; i++)   // play the first notes: the marker moves on to the fifth (the second G)
            {
                int midi = tut.Lesson.events[tut.CurrentEvent].targets[0].midi;
                piano.PressMidi(midi, presser, 0.7f); Step(); piano.ReleaseMidi(midi, presser);
                t0 = Time.realtimeSinceStartup;
                while (tut.State != TutorialState.Waiting && Time.realtimeSinceStartup - t0 < 10f) { Step(); yield return null; }
            }
            yield return Frames(20);
            Assert.That(_m.MarkStrength, Is.GreaterThan(0.3f));
            Aim(HomeEye, BookCentre, SH / PxPerDeg);
            Shot("sheet-lesson-marker");
            Assert.That(_m.Book.TryGetMark("piano-ode", tut.CurrentEvent, out var mk, out int mp), Is.True);
            int sg = _m.Book.Page(mp).Left ? -1 : 1;
            float my = (float)(mk.Y0 + 6f);
            Color warm = At(PageWorld(sg, (float)(mk.X0 + mk.X1) / 2f, my), 1);
            Color plain = At(PageWorld(sg, (float)(mk.X0 + mk.X1) / 2f - 30f, my), 1);
            Debug.Log($"[Sheet] marker paper {warm} vs its neighbour {plain}");
            Check(warm.r - warm.b > plain.r - plain.b + 0.02f, "the marked note's paper is warmer than its neighbour's");
            Zoom("sheet-lesson-marker-zoom4x", PageWorld(sg, (float)(mk.X0 + mk.X1) / 2f, 60f), 240, 120, 4);
            tut.Stop();
            yield return Frames((int)(2f / Dt));

            // ---- 2b. Fur Elise: sharps, naturals, beamed sixteenths, a pickup (the book riffles there)
            tut.Start("piano-elise");
            t0 = Time.realtimeSinceStartup;
            while ((tut.State != TutorialState.Waiting || _m.Turning || _m.Pages.Pending) && Time.realtimeSinceStartup - t0 < 30f) { Step(); yield return null; }
            yield return Frames(20);
            Camera(SW, SH);
            Aim(HomeEye, BookCentre, SH / PxPerDeg);
            Shot("sheet-seat-elise");
            var es = _m.Book.Song("piano-elise");
            Zoom("sheet-seat-elise-zoom4x", PageWorld(_m.Book.Page(es.First).Left ? -1 : 1, 150f, 62f), 250, 125, 4);
            tut.Stop();
            yield return Frames((int)(2f / Dt));

            // ---- draws: the book alone, idle and turning
            long idle = 0, turning = 0, baseline = 0;
            yield return Draws(null, v => baseline = v);
            yield return Draws(_m.Root, v => idle = v);
            // forward, or back when the last lesson's spread is open (with the first five's 15 piano songs Fur Elise is the last)
            Assert.That(_m.Turn(+1) || _m.Turn(-1), Is.True, "a leaf turns");
            t0 = Time.realtimeSinceStartup;
            while (_m.Leaf.Holding && Time.realtimeSinceStartup - t0 < 5f) { Step(); yield return null; }
            for (int i = 0; i < (int)(0.25f / Dt); i++) Step();
            yield return Draws(_m.Root, v => turning = v, step: false);
            Debug.Log($"[Sheet] draws: book idle {idle - baseline}, turning {turning - baseline} (camera baseline {baseline})");
            Check(idle - baseline <= 2, $"two draws idle ({idle - baseline})");
            Check(turning - baseline <= 2, $"two draws while a leaf turns ({turning - baseline})");

            // ---- 3. mid-flip (three-quarter view from the seat, a little to the right)
            Camera(1280, 800);
            Aim(HomeEye + Piano.right * 0.18f + Vector3.up * 0.02f, BookCentre + _m.Root.TransformDirection(Vector3.back) * 0.08f, 42f);
            Shot("sheet-midflip");
            for (int i = 0; i < 200 && _m.Turning; i++) Step();
            yield return Frames(4);

            // ---- 4. the contents, straight on
            _m.ToContents();
            t0 = Time.realtimeSinceStartup;
            while ((_m.Turning || _m.Pages.Pending) && Time.realtimeSinceStartup - t0 < 10f) { Step(); yield return null; }
            yield return Frames(4);
            Vector3 n = _m.Root.TransformDirection(Vector3.back);
            Camera(1600, 680);
            Aim(BookCentre + n * 0.62f + Vector3.up * 0.01f, BookCentre, 25f);
            Shot("sheet-contents");

            // ---- 5. a close-up: a song page's notes and letters, the gutter, the ribbon
            Assert.That(_m.Turn(+1), Is.True);
            t0 = Time.realtimeSinceStartup;
            while ((_m.Turning || _m.Pages.Pending) && Time.realtimeSinceStartup - t0 < 10f) { Step(); yield return null; }
            yield return Frames(4);
            Camera(1400, 900);
            Vector3 c = PageWorld(-1, 120f, 70f);
            Aim(c + n * 0.20f + Vector3.up * 0.03f, c, 32f);
            Shot("sheet-closeup");
            Vector3 g = _m.Root.TransformPoint(new Vector3(0f, SBottom + 0.03f, 0f));
            Aim(g + n * 0.22f + Vector3.up * 0.08f + Piano.right * 0.05f, g - Vector3.up * 0.03f, 40f);
            Shot("sheet-closeup-ribbon");

            // ---- 6. the overview: the piano, the book, its lamp, in the room
            Camera(1280, 800);
            Aim(HomeEye + Vector3.up * 0.25f - Piano.forward * 0.35f, BookCentre, 50f);
            Shot("sheet-overview");
            if (head != null) head.enabled = true;
            Assert.That(_failures, Is.Empty, string.Join("; ", _failures));
        }

        readonly List<string> _failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) { _failures.Add(what); Debug.LogWarning("[Sheet] CHECK FAILED: " + what); } }

        static string PhaseBDir { get { string d = Path.Combine(ShotsDir, "phase-b"); Directory.CreateDirectory(d); return d; } }

        void ShotB(string name) { Shot(null); File.WriteAllBytes(Path.Combine(PhaseBDir, name + ".png"), _read.EncodeToPNG()); }

        IEnumerator Settle(ILayoutService layout, Action eachFrame = null)
        {
            float t0 = Time.realtimeSinceStartup;
            do { Step(); eachFrame?.Invoke(); yield return null; } while ((layout.IsTurning || layout.AnyMoving || _m.Pages.Pending) && Time.realtimeSinceStartup - t0 < 30f);
            for (int i = 0; i < 4; i++) { Step(); yield return null; }
        }

        /// <summary>Phase B (the platform): the book at the seat and in a lesson; the piano put away (no book) and brought back
        /// by its glide (no book while it moves, the book on its rest when it lands); My set with the player facing another
        /// member (the book stays on the piano at the side). Pictures: Builds/Shots/sheet/phase-b/.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator PhaseBTheBookWithTheSeatPlannerAndGlides()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics): run Tools/unity.sh test-play --gpu");
            yield return Boot();
            var head = App.Current.Rig.HeadCamera;
            if (head != null) head.enabled = false;
            var layout = App.Current.Get<ILayoutService>();
            const int SW = 1000, SH = 760;
            // 1. the book at the seat (20 px/deg)
            Camera(SW, SH);
            Aim(HomeEye, BookCentre, SH / PxPerDeg);
            ShotB("book-at-seat");
            Check(_m.Visible && _m.Ready, "the book at the seat");
            // 2. a lesson with the marker
            var tut = App.Current.Get<ITutorial>() as Tutorial;
            tut.Start("piano-ode");
            float t0 = Time.realtimeSinceStartup;
            while ((tut.State != TutorialState.Waiting || _m.Turning || _m.Pages.Pending) && Time.realtimeSinceStartup - t0 < 30f) { Step(); yield return null; }
            var piano = App.Current.Get<IPianoApi>();
            var presser = new object();
            for (int i = 0; i < 3; i++)
            {
                int midi = tut.Lesson.events[tut.CurrentEvent].targets[0].midi;
                piano.PressMidi(midi, presser, 0.7f); Step(); piano.ReleaseMidi(midi, presser);
                t0 = Time.realtimeSinceStartup;
                while (tut.State != TutorialState.Waiting && Time.realtimeSinceStartup - t0 < 10f) { Step(); yield return null; }
            }
            yield return Frames(20);
            Aim(HomeEye, BookCentre, SH / PxPerDeg);
            ShotB("lesson-marker");
            Check(_m.MarkStrength > 0.3f, "the marker shows in the lesson");
            tut.Stop();
            yield return Frames((int)(2f / Dt));
            // the glides seen from above and behind the seat, wide enough to hold the seat and the piano's place
            var seatF = layout.Seat;
            Vector3 eye = new Vector3(seatF.Head.x, 2.0f, seatF.Head.z) - seatF.Forward * 1.1f;
            Vector3 ahead = new Vector3(seatF.Head.x, 0.5f, seatF.Head.z) + seatF.Forward * 0.9f;
            // 3. put away: the piano glides back to its place; no book while it moves or when it is away
            Camera(1280, 800);
            bool shownWhileMoving = false, midShot = false;
            int outFrames = 0;
            Check(layout.PutAway(InstrumentKind.Piano) || layout.Mode == LayoutMode.Single, "the piano can be put away");
            if (layout.Mode == LayoutMode.Single) layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            float tg = Time.realtimeSinceStartup;
            do
            {
                Step();
                if (layout.IsMoving(InstrumentKind.Piano) || layout.PlaceOf(InstrumentKind.Piano) == InstrumentPlace.Gliding)
                {
                    outFrames++;
                    if (_m.Visible) shownWhileMoving = true;
                    if (!midShot && outFrames > 12) { Aim(eye, ahead, 90f); ShotB("piano-putaway-gliding"); midShot = true; }
                }
                yield return null;
            } while ((layout.IsTurning || layout.AnyMoving) && Time.realtimeSinceStartup - tg < 30f);
            yield return Frames(4);
            Aim(eye, ahead, 90f);
            ShotB("piano-away-no-book");
            Debug.Log($"[Sheet] put away: {outFrames} gliding frames, piano now {layout.PlaceOf(InstrumentKind.Piano)}");
            Check(!_m.Visible, "no book while the piano is away");
            if (layout.TryGetDisplayPoint(InstrumentKind.Piano, out Vector3 disp))
            {
                Aim(eye + Vector3.up * 0.3f, disp, 60f);
                ShotB("piano-on-display-no-book");
            }
            // 4. brought back by a glide
            Check(layout.Bring(InstrumentKind.Piano, BringReason.Menu), "the piano comes back");
            midShot = false;
            tg = Time.realtimeSinceStartup;
            int movingFrames = 0;
            do
            {
                Step();
                if (layout.IsMoving(InstrumentKind.Piano))
                {
                    movingFrames++;
                    if (_m.Visible) shownWhileMoving = true;
                    if (!midShot && movingFrames > 20) { Aim(eye, ahead, 90f); ShotB("piano-gliding-back"); midShot = true; }
                }
                yield return null;
            } while ((layout.IsTurning || layout.AnyMoving || _m.Pages.Pending) && Time.realtimeSinceStartup - tg < 30f);
            yield return Frames(6);
            Aim(HomeEye, BookCentre, SH / PxPerDeg);
            Camera(SW, SH);
            Aim(HomeEye, BookCentre, SH / PxPerDeg);
            ShotB("piano-back-book-on-rest");
            Check(_m.Visible, "the book is back on the rest");
            Check(!shownWhileMoving, "no book while the piano glides");
            Check(Vector3.Distance(_m.Root.position, Piano.TransformPoint(piano.MusicRestLocal.position)) < 1e-4f, "on the rest after the glide");
            // 5. My set, the player facing another member: the book stays on the piano at the side
            InstrumentKind[][] sets =
            {
                new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Handpan },
                new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp },
                new[] { InstrumentKind.Piano, InstrumentKind.Drums },
            };
            InstrumentKind third = InstrumentKind.Drums;
            bool setOk = false;
            foreach (var set in sets) if (layout.TrySetSet(set)) { setOk = true; third = set[set.Length - 1]; break; }
            if (setOk)
            {
                yield return Settle(layout);
                SeatSlot? slot = layout.SlotOf(InstrumentKind.Piano);
                Debug.Log($"[Sheet] My set piano + drums + {third}: piano at {slot}, drums at {layout.SlotOf(InstrumentKind.Drums)}, {third} at {layout.SlotOf(third)}; facing {layout.Facing}; book visible {_m.Visible}");
                Check(_m.Visible, "the book stays with the piano in My set");
                // the player turned toward the drums (their slot, on the right): the book stays on the piano, at the side
                var other = App.Current.Get<IRigRegistry>()?.Get(RigId.Drums);
                Vector3 e = HomeEye;
                Vector3 toward = other != null ? other.Group.position - e : Piano.right;
                toward.y = 0f;
                Vector3 toBook = BookCentre - e; toBook.y = 0f;
                toward = Vector3.Slerp(toBook.normalized, toward.normalized, 0.6f);   // turned most of the way to the drums
                Vector3 dir = Quaternion.AngleAxis(25f, Vector3.Cross(Vector3.up, toward.normalized)) * toward.normalized;   // 25 degrees down
                Camera(1280, 800);
                Aim(e, e + dir, 95f);
                ShotB("set-facing-drums-book-at-side");
                Vector3 seat = layout.Seat.Head;
                Aim(new Vector3(seat.x, HomeEye.y + 0.9f, seat.z) - Piano.forward * 0.9f, BookCentre - Vector3.up * 0.3f, 75f);
                ShotB("set-overview");
            }
            else Check(false, "a My set of three fits");
            if (head != null) head.enabled = true;
            Assert.That(_failures, Is.Empty, string.Join("; ", _failures));
        }

        /// <summary>The draws of one camera frame with only `keep`'s renderers on (null: none, the pipeline's baseline).</summary>
        IEnumerator Draws(Transform keep, Action<long> result, bool step = true)
        {
            Camera(512, 512);
            Aim(HomeEye, BookCentre, 40f);
            var off = new List<Renderer>();
            void Off()
            {
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (r.enabled && (keep == null || !r.transform.IsChildOf(keep))) { r.enabled = false; off.Add(r); }
            }
            Off();
            _cam.enabled = true;
            var rec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            for (int i = 0; i < 4; i++) { if (step) Step(); yield return null; Off(); }
            long d = rec.Valid ? rec.LastValue : -1;
            rec.Dispose();
            _cam.enabled = false;
            foreach (var r in off) if (r != null) r.enabled = true;
            result(d);
        }

        /// <summary>Staff space and letter height in display pixels (20 px/deg) over the open page's music, from an eye.</summary>
        void Readability(string seat, Vector3 eye)
        {
            var book = _m.Book;
            var song = book.Song("piano-ode") ?? book.Songs[0];
            var page = book.Page(song.First);
            float minS = float.MaxValue, maxS = 0f, minL = float.MaxValue, maxL = 0f, minD = float.MaxValue, maxD = 0f;
            int sg = page.Left ? -1 : 1;
            foreach (var note in page.Notes)
            {
                if (note.K < 0) continue;
                float x = (float)note.X, top = (float)note.StaffTopY;
                Vector3 a = PageWorld(sg, x, top + 2f * (float)SheetStyle.S), b = PageWorld(sg, x, top + 3f * (float)SheetStyle.S);
                float ps = Vector3.Angle(a - eye, b - eye) * PxPerDeg;
                book.TryGetMark(song.Lesson.Id, note.K, out var m, out _);
                float yl = (float)m.Y1 - 1.6f;
                Vector3 la = PageWorld(sg, x, yl), lb = PageWorld(sg, x, yl - (float)SheetStyle.LetterCap);
                float pl = Vector3.Angle(la - eye, lb - eye) * PxPerDeg;
                float dist = Vector3.Distance(eye, a);
                minS = Mathf.Min(minS, ps); maxS = Mathf.Max(maxS, ps); minL = Mathf.Min(minL, pl); maxL = Mathf.Max(maxL, pl);
                minD = Mathf.Min(minD, dist); maxD = Mathf.Max(maxD, dist);
            }
            Debug.Log($"[Sheet] readability {seat}: eye distance {minD:F3}-{maxD:F3} m, staff space {minS:F1}-{maxS:F1} px, letter cap {minL:F1}-{maxL:F1} px at {PxPerDeg} px/deg");
            if (seat == "home")
            {
                Check(minS >= 6.5f, $"staff space {minS:F1} >= 6.5 display px from the home seat");
                Check(minL >= 7.5f, $"letters {minL:F1} >= 7.5 display px from the home seat");
            }
        }
    }
}
