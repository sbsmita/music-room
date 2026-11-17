// The music book inside the booted app (sheet.md 9.3): it sits on the piano's rest and moves with the piano; it is hidden
// while the piano is away; it opens at the contents; a lesson start turns it to the song before the countdown ends and
// the marker follows the right notes (a wrong note does not move it) and fades when the lesson is done; a scripted index
// tip pressing a page corner turns once (no repeat while held, never from behind), the left corner turns back, the ribbon
// riffles to the contents, a title starts its lesson; nothing fires while arranging; turns take 0.62 s (manual) / 0.52 s
// (auto) and call the paper sound once per leaf; a desktop click on the book's collider taps; and the per-frame work
// allocates nothing. TestBoot with manual frames (the rasters run on workers and Burst jobs between the frames).
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using MusicRoom.Sheet;
using MusicRoom.Sheet.Engrave;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Sheet
{
    public class SheetPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _t;
        SheetModule _m;

        IEnumerator Boot(bool headset = false)
        {
            _t = 10.0;
            _boot = new TestBoot(headless: true, simulateHeadset: headset, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Hands.SetTracked(HandSide.Left, false);
            _boot.Hands.SetTracked(HandSide.Right, false);
            yield return _boot.Start();
            foreach (var x in App.Current.Modules) if (x is SheetModule s) _m = s;
            Assert.That(_m, Is.Not.Null, "the Sheet module is discovered");
            var layout = App.Current.Get<ILayoutService>();
            float t0 = Time.realtimeSinceStartup;
            while ((layout == null || !layout.FirstArrangeDone) && Time.realtimeSinceStartup - t0 < 5f) { Step(); yield return null; }
            yield return WaitReady();
        }

        void Step(int n = 1) { for (int i = 0; i < n; i++) { _t += Dt; _boot.Step(Dt); } }

        IEnumerator WaitReady(float timeout = 20f)
        {
            float t0 = Time.realtimeSinceStartup;
            while ((!_m.Ready || _m.Pages.Pending) && Time.realtimeSinceStartup - t0 < timeout) { Step(); yield return null; }
            Assert.That(_m.Ready, Is.True, "the open spread was engraved and rastered");
        }

        /// <summary>Runs frames until the turn ends (real time passes too, for the rasters).</summary>
        IEnumerator FinishTurn(float timeout = 10f)
        {
            float t0 = Time.realtimeSinceStartup;
            while (_m.Turning && Time.realtimeSinceStartup - t0 < timeout) { Step(); yield return null; }
            Assert.That(_m.Turning, Is.False, "the turn ended");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null; _m = null;
        }

        static HandPose IndexAt(Vector3 tip, Vector3 pointing)
        {
            Vector3 palm = Vector3.Cross(pointing, Vector3.right);
            if (palm.sqrMagnitude < 1e-4f) palm = Vector3.forward;
            HandPose p = HandPose.OpenFacing(HandSide.Right, tip - pointing * 0.15f, pointing, palm.normalized).WithPointing().WithIndexTipAt(tip);
            foreach (HandJoint j in new[] { HandJoint.ThumbTip, HandJoint.MiddleTip, HandJoint.RingTip, HandJoint.LittleTip }) p.Position[(int)j] += Vector3.up * 0.3f;
            return p;
        }

        void Tip(Vector3 world, Vector3 pointing)
        {
            _boot.Hands.SetTracked(HandSide.Right, true);
            _boot.Hands.SetPose(HandSide.Right, IndexAt(world, pointing));
        }

        Vector3 Anchor(string id, out Vector3 up)
        {
            Assert.That(_m.TryGetAnchor(id, out Vector3 w, out up), Is.True, id);
            return w;
        }

        [UnityTest]
        public IEnumerator TheBookSitsOnTheRestMovesWithThePianoAndOpensAtTheContents()
        {
            yield return Boot();
            var piano = App.Current.Get<IPianoApi>();
            Assert.That(_m.Root.parent, Is.SameAs(piano.Root), "parented under the piano's rig");
            Assert.That(Vector3.Distance(_m.Root.localPosition, piano.MusicRestLocal.position), Is.LessThan(1e-6f), "IPianoApi.MusicRestLocal");
            Assert.That(Quaternion.Angle(_m.Root.localRotation, piano.MusicRestLocal.rotation), Is.LessThan(0.01f));
            Assert.That(_m.Visible, Is.True);
            Assert.That(_m.Spread, Is.EqualTo(2), "opens at the contents");
            Assert.That(_m.Book.Page(2).Contents, Is.True);
            Assert.That(_m.Book.Songs.Count, Is.EqualTo(App.Current.Get<ILessonLibrary>().Count(InstrumentKind.Piano)), "every piano lesson is in the book");
            Assert.That(_m.Pages.SliceOf(2), Is.GreaterThanOrEqualTo(0));
            Assert.That(_m.Pages.SliceOf(3), Is.GreaterThanOrEqualTo(0));
            // the contents page's raster is in its slice (CPU path: no GPU in this run)
            if (!_m.Pages.Gpu)
            {
                int s = _m.Pages.SliceOf(2), ink = 0;
                for (int y = 60; y < 400; y += 3) for (int x = 60; x < 900; x += 3) if (_m.Pages.ReadTexel(s, x, y) > 128) ink++;
                Assert.That(ink, Is.GreaterThan(300), "the contents is printed in its slice");
            }
            // the piano moves (a grab, MR, the arrange mode): the book goes with it, the rest pose is the same piano-local pose
            piano.Root.position += new Vector3(0.3f, 0.1f, -0.2f);
            piano.Root.rotation *= Quaternion.Euler(0f, 30f, 0f);
            Step();
            Vector3 want = piano.Root.TransformPoint(piano.MusicRestLocal.position);
            Assert.That(Vector3.Distance(_m.Root.position, want), Is.LessThan(1e-5f), "on the rest wherever the piano goes");
            Assert.That(_m.Host.MusicRestPose.HasValue, Is.True);
            Assert.That(Vector3.Distance(_m.Host.MusicRestPose.Value.position, want), Is.LessThan(1e-5f));
        }

        IEnumerator Settle(ILayoutService layout)
        {
            float t0 = Time.realtimeSinceStartup;
            do { Step(); yield return null; } while ((layout.IsTurning || layout.AnyMoving) && Time.realtimeSinceStartup - t0 < 15f);
            Step(3);
        }

        [UnityTest]
        public IEnumerator NoBookWhileThePianoIsAwayAndBackWithItsGlide()
        {
            yield return Boot();
            var layout = App.Current.Get<ILayoutService>();
            // One alone with the drums: the piano goes back to its place (hidden or on display): no book
            layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            bool sawHiddenWhileMoving = false;
            float t0 = Time.realtimeSinceStartup;
            do { Step(); if (layout.IsMoving(InstrumentKind.Piano) && !_m.Visible) sawHiddenWhileMoving = true; yield return null; }
            while ((layout.IsTurning || layout.AnyMoving) && Time.realtimeSinceStartup - t0 < 15f);
            Step(3);
            Assert.That(layout.PlaceOf(InstrumentKind.Piano), Is.Not.EqualTo(InstrumentPlace.Seat));
            Assert.That(_m.Visible, Is.False, "the piano is not at the seat: no book");
            Assert.That(_m.Host.MusicRestPose.HasValue, Is.False);
            // brought back (a glide): the book appears once the piano has arrived, on its rest
            Assert.That(layout.Bring(InstrumentKind.Piano, BringReason.Menu) || layout.PlaceOf(InstrumentKind.Piano) == InstrumentPlace.Seat, Is.True);
            t0 = Time.realtimeSinceStartup;
            do { Step(); if (layout.IsMoving(InstrumentKind.Piano) && !_m.Visible) sawHiddenWhileMoving = true; yield return null; }
            while ((layout.IsTurning || layout.AnyMoving) && Time.realtimeSinceStartup - t0 < 15f);
            Step(3);
            Assert.That(layout.PlaceOf(InstrumentKind.Piano), Is.EqualTo(InstrumentPlace.Seat));
            Assert.That(_m.Visible, Is.True, "back with the piano");
            Assert.That(sawHiddenWhileMoving || !layout.IsMoving(InstrumentKind.Piano), Is.True, "no book while the piano glides");
            var piano = App.Current.Get<IPianoApi>();
            Assert.That(Vector3.Distance(_m.Root.position, piano.Root.TransformPoint(piano.MusicRestLocal.position)), Is.LessThan(1e-5f), "on the rest");
        }

        [UnityTest]
        public IEnumerator ALessonTurnsTheBookToItsSongAndTheMarkerFollowsTheRightNotes()
        {
            yield return Boot();
            var app = App.Current;
            var tut = app.Get<ITutorial>() as Tutorial;
            var piano = app.Get<IPianoApi>();
            var song = _m.Book.Song("piano-ode");
            Assert.That(song, Is.Not.Null);
            int target = SheetBook.SpreadOf(song.First);
            Assert.That(target, Is.Not.EqualTo(2));
            tut.Start("piano-ode");
            Assert.That(tut.State, Is.EqualTo(TutorialState.Countdown));
            float t0 = Time.realtimeSinceStartup;
            while ((tut.State == TutorialState.Countdown || _m.Turning) && Time.realtimeSinceStartup - t0 < 15f) { Step(); yield return null; }
            Assert.That(_m.Spread, Is.EqualTo(target), "the book reached the Ode's spread");
            Assert.That(_m.AutoTurns, Is.EqualTo(1));
            Assert.That(tut.LessonTime, Is.LessThan(0.5), "before the countdown ended (or within a few frames of it)");
            var presser = new object();
            Vector4 lastRect = Vector4.zero;
            int moves = 0;
            for (int ev = 0; ev < 6; ev++)
            {
                t0 = Time.realtimeSinceStartup;
                while (tut.State != TutorialState.Waiting && tut.State != TutorialState.Done && Time.realtimeSinceStartup - t0 < 10f) { Step(); yield return null; }
                Step(10);
                Assert.That(_m.MarkPage, Is.EqualTo(song.First), "the marker is on the song's page");
                Assert.That(_m.Book.TryGetMark("piano-ode", tut.CurrentEvent, out var mk, out _), Is.True);
                Assert.That(_m.MarkRect.x, Is.EqualTo((float)mk.X0).Within(0.05f), $"event {ev}: the marker is on its column");
                Assert.That(_m.MarkStrength, Is.GreaterThan(0.2f));
                if (ev > 0 && _m.MarkRect != lastRect) moves++;
                lastRect = _m.MarkRect;
                if (ev == 2)
                {
                    // a wrong note: nothing moves
                    int wrong = tut.Lesson.events[tut.CurrentEvent].targets[0].midi + 1;
                    piano.PressMidi(wrong, presser, 0.7f); Step(); piano.ReleaseMidi(wrong, presser); Step(10);
                    Assert.That(_m.MarkRect, Is.EqualTo(lastRect), "a wrong note does not move the marker");
                }
                int midi = tut.Lesson.events[tut.CurrentEvent].targets[0].midi;
                piano.PressMidi(midi, presser, 0.7f); Step(); piano.ReleaseMidi(midi, presser); Step();
            }
            Assert.That(moves, Is.GreaterThanOrEqualTo(4), "the marker moved to each next event");
            tut.Stop();
            Step((int)(2f / Dt));
            Assert.That(_m.MarkStrength, Is.EqualTo(0f), "faded out when the lesson stopped");
            Assert.That(_m.Spread, Is.EqualTo(target), "the book stays on the song");
        }

        [UnityTest]
        public IEnumerator PokingTheCornersTurnsOnceAndTheRibbonGoesBackToTheContents()
        {
            yield return Boot(headset: true);
            var host = (PlatformHost)_m.Host;
            Vector3 corner = Anchor("sheet.corner.right", out Vector3 up);
            // from in front, down onto the page: one turn, none while held
            Tip(corner + up * 0.03f, -up); Step(3);
            Tip(corner - up * 0.002f, -up); Step(2);
            Assert.That(_m.Turning, Is.True, "a tapped corner turns the page");
            Assert.That(_m.ManualTurns, Is.EqualTo(1));
            Step(40);
            Assert.That(_m.ManualTurns, Is.EqualTo(1), "no repeat while the finger stays");
            yield return FinishTurn();
            Assert.That(_m.Spread, Is.EqualTo(4));
            Assert.That(host.PaperTurns, Is.EqualTo(1), "one leaf, one paper sound");
            Assert.That(host.LastPaperSeconds, Is.EqualTo(LeafTurn.ManualSeconds));
            // from behind the book: never
            Tip(corner - up * 0.10f, up); Step(3);
            Tip(corner - up * 0.004f, up); Step(3);
            Tip(corner + up * 0.01f, up); Step(3);
            Assert.That(_m.Turning, Is.False, "never from behind");
            // lift away, then turn twice more and back with the left corner
            Tip(corner + up * 0.1f, -up); Step(40);
            for (int i = 0; i < 2; i++)
            {
                corner = Anchor("sheet.corner.right", out up);
                Tip(corner + up * 0.03f, -up); Step(3);
                Tip(corner - up * 0.002f, -up); Step(2);
                yield return FinishTurn();
                Tip(corner + up * 0.1f, -up); Step(40);
            }
            Assert.That(_m.Spread, Is.EqualTo(8));
            Vector3 left = Anchor("sheet.corner.left", out Vector3 upL);
            Tip(left + upL * 0.03f, -upL); Step(3);
            Tip(left - upL * 0.002f, -upL); Step(2);
            yield return FinishTurn();
            Assert.That(_m.Spread, Is.EqualTo(6), "the left corner turns back");
            Tip(left + upL * 0.1f, -upL); Step(40);
            // the ribbon's tail on the top panel: riffle back to the contents
            Vector3 rib = Anchor("sheet.ribbon", out Vector3 upR);
            int before = host.PaperTurns;
            Tip(rib + upR * 0.03f, -upR); Step(3);
            Tip(rib - upR * 0.002f, -upR); Step(2);
            Assert.That(_m.Turning, Is.True);
            Assert.That(_m.Leaf.LeafCount, Is.EqualTo(2), "two spreads away: a riffle of two leaves");
            yield return FinishTurn();
            Assert.That(_m.Spread, Is.EqualTo(2), "the ribbon goes back to the contents");
            Assert.That(host.PaperTurns - before, Is.EqualTo(2), "one paper sound per leaf");
            Assert.That(_boot.Settings.GetInt(SheetModule.KeySpread, -1), Is.EqualTo(2), "the open spread is remembered");
        }

        [UnityTest]
        public IEnumerator PokingATitleStartsItsLessonAndArrangeModeStopsPokes()
        {
            yield return Boot(headset: true);
            var tut = App.Current.Get<ITutorial>();
            var row = _m.Book.Page(2).Rows[2];
            Assert.That(_m.TryGetAnchor("sheet.row.1", out _, out _), Is.True);
            // the third row's middle, on the left page
            Vector3 p = _m.Root.TransformPoint(BookGeometry.PagePoint(-1, BookGeometry.PageW - (float)((row.X0 + row.X1) / 2) / 1000f, BookGeometry.PageH - (float)((row.Y0 + row.Y1) / 2) / 1000f, App.Current.Get<IPianoApi>().MusicRestLedge + BookGeometry.Square));
            Vector3 n = _m.Root.TransformDirection(Vector3.back);
            // arranging: nothing fires
            var arrange = App.Current.Get<IArrangeMode>();
            arrange.BeginArrange();
            Step(3);
            Tip(p + n * 0.03f, -n); Step(3);
            Tip(p - n * 0.002f, -n); Step(3);
            Assert.That(tut.State, Is.EqualTo(TutorialState.Idle), "no pokes while arranging");
            Assert.That(_m.HoverRow, Is.EqualTo(-1), "no hover while arranging");
            arrange.EndArrange();
            Tip(p + n * 0.1f, -n); Step(10);
            Tip(p + n * 0.03f, -n); Step(3);
            Assert.That(_m.HoverRow, Is.EqualTo(2), "the warm hover tint on the row under the fingertip");
            Tip(p - n * 0.002f, -n); Step(2);
            Assert.That(tut.LessonId, Is.EqualTo(row.Id), "a poked title starts its lesson");
            Assert.That(tut.State, Is.EqualTo(TutorialState.Countdown));
            Assert.That(_m.LastTitleStarted, Is.EqualTo(row.Id));
            Assert.That(_m.Tips.StartedFromTitle, Is.True);
        }

        [UnityTest]
        public IEnumerator TurnsTakeTheirTimeAndTheLeavesRestCollapsed()
        {
            yield return Boot();
            var host = (PlatformHost)_m.Host;
            Assert.That(_m.Turn(-1), Is.False, "before the contents: nothing");
            Assert.That(_m.Turn(+1), Is.True);
            float waited = 0f;
            while (_m.Leaf.Holding && waited < 2f) { Step(); waited += Dt; yield return null; }
            float t = 0f;
            while (_m.Turning && t < 3f) { Step(); t += Dt; }
            Assert.That(t, Is.EqualTo(LeafTurn.ManualSeconds).Within(2 * Dt), "a manual turn: 0.62 s");
            Assert.That(host.PaperTurns, Is.EqualTo(1));
            Step(2);
            Assert.That(_m.Leaf.Shadow(), Is.EqualTo(Vector2.zero));
            // the automatic single turn: 0.52 s
            var tut = App.Current.Get<ITutorial>();
            string next = _m.Book.Page(6) != null && _m.Book.Page(6).Song != null ? _m.Book.Page(6).Song : null;
            Assert.That(next, Is.Not.Null);
            tut.Start(next);
            Step();
            Assert.That(_m.Turning, Is.True, "a lesson one spread away: one automatic leaf");
            Assert.That(_m.Leaf.Kind, Is.EqualTo(TurnKind.Auto));
            waited = 0f;
            while (_m.Leaf.Holding && waited < 2f) { Step(); waited += Dt; yield return null; }
            t = 0f;
            while (_m.Turning && t < 3f) { Step(); t += Dt; }
            Assert.That(t, Is.EqualTo(LeafTurn.AutoSeconds).Within(2 * Dt), "an automatic turn: 0.52 s");
            // the leaves' vertices rest collapsed at a point
            Step(2);
            var mesh = _m.PaperRenderer.GetComponent<MeshFilter>().sharedMesh;
            var verts = mesh.vertices;
            Vector3 first = verts[BookMesh.LeafVertexStart];
            for (int i = BookMesh.LeafVertexStart; i < BookMesh.LeafVertexStart + BookMesh.LeafVertexCount; i += 37) Assert.That(verts[i], Is.EqualTo(first), "collapsed leaf");
        }

        [UnityTest]
        public IEnumerator ADesktopClickOnTheBooksColliderTaps()
        {
            yield return Boot();
            Vector3 corner = Anchor("sheet.corner.right", out Vector3 up);
            Physics.SyncTransforms();
            var ray = new Ray(corner + up * 0.4f, -up);
            Assert.That(Physics.Raycast(ray, out RaycastHit hit, 1f, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide), Is.True, "a PlaySurface collider over the corner");
            Assert.That(hit.collider.transform.IsChildOf(_m.Root), Is.True, "the book's own click target");
            Assert.That(_m.Tap(hit.point), Is.EqualTo(PokeTarget.CornerRight));
            Assert.That(_m.Turning, Is.True);
            yield return FinishTurn();
            Assert.That(_m.Spread, Is.EqualTo(4));
            Vector3 rib = Anchor("sheet.ribbon", out Vector3 upR);
            Assert.That(Physics.Raycast(new Ray(rib + upR * 0.3f, -upR), out hit, 1f, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide), Is.True);
            Assert.That(_m.Tap(hit.point), Is.EqualTo(PokeTarget.Ribbon));
            yield return FinishTurn();
            Assert.That(_m.Spread, Is.EqualTo(2));
            var row = _m.Book.Page(2).Rows[0];
            Vector3 p = _m.Root.TransformPoint(BookGeometry.PagePoint(-1, BookGeometry.PageW - (float)((row.X0 + row.X1) / 2) / 1000f, BookGeometry.PageH - (float)((row.Y0 + row.Y1) / 2) / 1000f, App.Current.Get<IPianoApi>().MusicRestLedge + BookGeometry.Square));
            Assert.That(_m.Tap(p), Is.EqualTo(PokeTarget.Row));
            Assert.That(App.Current.Get<ITutorial>().LessonId, Is.EqualTo(row.Id), "a click on a title starts the lesson");
        }

        [UnityTest]
        public IEnumerator ThePlatformHooksAreWired()
        {
            yield return Boot();
            var app = App.Current;
            Assert.That(app.GetAll<IPointerClaimant>(), Does.Contain(_m), "the book claims fingertips in front of its pages");
            Assert.That(app.GetAll<IGuideAnchors>(), Does.Contain(_m), "Sunny's demo anchors");
            foreach (string a in SheetTipTable.Anchors)
            {
                Assert.That(_m.TryGetAnchor(a, out Vector3 w, out Vector3 up), Is.True, a);
                Assert.That(up.magnitude, Is.EqualTo(1f).Within(1e-3f), a + ": a unit up (the demos read a shorter up as a radius)");
            }
            Vector3 corner = Anchor("sheet.corner.right", out Vector3 n);
            Assert.That(_m.ClaimsPoint(corner + n * 0.02f), Is.True, "20 mm in front of a corner");
            Assert.That(_m.ClaimsPoint(corner + n * 0.05f), Is.False, "50 mm in front: free");
            Vector3 mid = _m.Root.TransformPoint(BookGeometry.PagePoint(1, 0.15f, 0.12f, app.Get<IPianoApi>().MusicRestLedge + BookGeometry.Square));
            Assert.That(_m.ClaimsPoint(mid + n * 0.01f), Is.True, "in front of the page");
            Assert.That(_m.ClaimsPoint(mid - n * 0.05f), Is.False, "behind the book");
            // the gesture signals are the platform's InstrumentSignalEvent
            var got = new System.Collections.Generic.List<string>();
            System.Action<InstrumentSignalEvent> h = e => { if (e.Kind == InstrumentKind.Piano) got.Add(e.Signal); };
            app.Events.Subscribe(h);
            Assert.That(_m.Turn(+1), Is.True);
            app.Events.Unsubscribe(h);
            Assert.That(got, Is.EqualTo(new[] { "sheet.turn" }));
            yield return FinishTurn();
            // the paper sound goes through the engine's one-shot pool: four presets, one shot list per leaf
            var host = (PlatformHost)_m.Host;
            Assert.That(host.PaperTurns, Is.EqualTo(1));
            var audio = app.Get<MusicRoom.Audio.IAudioEngine>();
            int probe = audio.AddOneShotPreset(PlatformHost.Preset(SheetPaperSound.Presets[0], audio.SampleRate > 0 ? audio.SampleRate : 48000, 1u));
            if (probe >= 0) Assert.That(host.ShotsPlayed, Is.InRange(8, SheetPaperSound.MaxShotsPerTurn), "lift, 3-6 crinkles, three airs, the landing");
            else Assert.Ignore("no Burst graph in this boot: the one-shot pool is not up");
        }

        [UnityTest]
        public IEnumerator TheFrameAllocatesNothing()
        {
            yield return Boot(headset: true);
            var tut = App.Current.Get<ITutorial>();
            Vector3 corner = Anchor("sheet.corner.right", out Vector3 up);
            Tip(corner + up * 0.04f, -up);
            tut.Start(_m.Book.Songs[0].Lesson.Id);
            Step(5);
            yield return WaitReady();
            for (int i = 0; i < 30; i++) { Step(); yield return null; }
            // the lesson's own turn over, then the next spread drawn ahead too: nothing left in flight
            float tw = Time.realtimeSinceStartup;
            while (_m.Turning && Time.realtimeSinceStartup - tw < 10f) { Step(); yield return null; }
            for (int k = 0; k < 3; k++)
            {
                yield return WaitReady();
                for (int i = 0; i < 5; i++) { Step(); yield return null; }
            }
            Assert.That(_m.Turning, Is.False);
            Assert.That(_m.Pages.Pending, Is.False);
            Assert.That(() => { s_keep = new float[64]; }, Is.AllocatingGCMemory(), "the recorder works here");
            Assert.That(() =>
            {
                for (int i = 0; i < 60; i++) { _m.Tick(FramePhase.Decor, Dt); _m.Tick(FramePhase.EndFrame, Dt); }
            }, Is.Not.AllocatingGCMemory(), "following the lesson, the marker, a hovering fingertip");
            // and while a leaf turns (vertex upload, paper sound, shadow)
            tut.Stop();
            Assert.That(_m.Turn(+1), Is.True);
            Step();
            float t0 = Time.realtimeSinceStartup;
            while (_m.Leaf.Holding && Time.realtimeSinceStartup - t0 < 5f) { Step(); yield return null; }
            Assert.That(() =>
            {
                for (int i = 0; i < 20; i++) { _m.Tick(FramePhase.Decor, Dt); _m.Tick(FramePhase.EndFrame, Dt); }
            }, Is.Not.AllocatingGCMemory(), "a turning leaf");
        }

        static float[] s_keep;
    }
}
