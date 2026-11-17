// The first run, end to end with every real module: a simulated headset, manual frames at 72 Hz, the scripted audio
// clock, the real start panel, and a scripted player (FakeHandSource for pokes, the instruments' own APIs for hits).
// The FAST first-timer tour (FirstRun.cs, 2026-11-16) SHOWS and never waits for the player:
//   - a first-timer who does nothing: Intro, the SNOW switch, the palm menu, Move instruments, the instrument set,
//     Recordings, recentering, Bye, in that order, every stop Done, the whole tour within 45 s, each with its line, the
//     ghost hand on the real target (FastTourCheck) and its menu row lit; no song, note, lesson or playback; the menu ends
//     as it started (closed), nothing recorded, the layout, the switches and Move instruments untouched;
//   - the same targets with One alone (Show me around); mixed reality: FastTourMrTests;
//   - the SNOW stop points at the real lever, Sunny flies beside it, and the switch is never flipped (on stays on);
//   - the start panel: Sunny comes at 8 s and points at Play here, never answers it, and a poke only quiets the help;
//   - poking Sunny skips each step in turn; Hide Sunny in the menu ends the tour; a row the player pokes ends it;
//   - Show me around replays it and gives the menu back on the Sunny screen.
using System;
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Hands;
using MusicRoom.Menu;
using MusicRoom.Tests.PlayMode.Guide;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Integration
{
    public class FirstRunWalkthroughTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        MenuModule _menu;
        GuideModule _guide;
        double _t;
        float _clock;
        float _panelAt = -1f, _firstSound = -1f;
        readonly List<string> _lines = new List<string>();
        readonly List<float> _lineAt = new List<float>();
        string _lastLine;
        int _sunnyVersion = -1;

        FirstRun Run => _guide.FirstRun;
        Sunny S => _guide.Sunny;

        IEnumerator Boot(bool startPanel = true)
        {
            _t = 20.0;
            _clock = 0f;
            _panelAt = _firstSound = -1f;
            _lines.Clear();
            _lineAt.Clear();
            _boot = new TestBoot(headless: true, simulateHeadset: true, manualFrames: true, audioClock: () => _t, startPanel: startPanel);
            _boot.Hands.SetPose(HandSide.Right, Rest());
            _boot.Hands.SetTracked(HandSide.Left, false);
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            foreach (var m in _app.Modules)
            {
                if (m is MenuModule mm) _menu = mm;
                if (m is GuideModule g) _guide = g;
            }
            Assert.That(_menu, Is.Not.Null);
            Assert.That(_guide, Is.Not.Null);
            _app.Events.Subscribe<RecorderEvent>(e =>
            {
                if (!e.DuringPlayback && e.Event.type == TakeEventType.PianoOn && _firstSound < 0f) _firstSound = _clock;
            });
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Step(2);
            Assert.IsTrue(_app.Get<ILayoutService>().FirstArrangeDone);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _menu = null;
            _guide = null;
        }

        void Step(int n = 1)
        {
            for (int i = 0; i < n; i++)
            {
                _t += Dt;
                _clock += Dt;
                _boot.Step(Dt);
                if (_panelAt < 0f && _menu.StartShowing) _panelAt = _clock;
                if (S.LineVersion != _sunnyVersion)
                {
                    _sunnyVersion = S.LineVersion;
                    if (S.BubbleVisible && S.LineId != _lastLine) { _lines.Add(S.LineId); _lineAt.Add(_clock); }
                    _lastLine = S.BubbleVisible ? S.LineId : null;
                }
            }
        }

        void StepUntil(Func<bool> done, float seconds, string what)
        {
            int n = (int)(seconds / Dt);
            for (int i = 0; i < n; i++) { if (done()) return; Step(); }
            Assert.IsTrue(done(), $"{what}: not within {seconds} s (step {Run.Step}, beat {Run.Beat}, line {S.LineId}, sunny {S.State})");
        }

        void Quiet(float seconds = 8f) => StepUntil(() => S.Quiet, seconds, "Sunny quiet");

        static HandPose Rest() =>
            HandPose.Open(HandSide.Right, new Vector3(0.45f, 1.7f, -0.6f), Quaternion.LookRotation(Vector3.forward, Vector3.up)).WithPointing();

        static HandPose Finger(Vector3 tip, Vector3 along) =>
            HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.LookRotation(along, Vector3.up)).WithPointing().WithIndexTipAt(tip);

        static HandPose IndexAt(Vector3 tip)
        {
            HandPose p = HandPose.OpenFacing(HandSide.Right, tip + Vector3.up * 0.15f, Vector3.down, Vector3.forward).WithPointing().WithIndexTipAt(tip);
            foreach (HandJoint j in new[] { HandJoint.ThumbTip, HandJoint.MiddleTip, HandJoint.RingTip, HandJoint.LittleTip }) p.Position[(int)j] += Vector3.up * 0.3f;
            return p;
        }

        // ---------------- the scripted player ----------------

        void PokeRow(RowKind id)
        {
            MenuController c = _menu.Controller;
            MenuRow row = null;
            for (int i = 0; i < c.RowCount; i++) if (c.Row(i).Id == id) row = c.Row(i);
            Assert.That(row, Is.Not.Null, $"no {id} row on {c.Screen}");
            Transform root = _menu.Root;
            Step(20);
            foreach (float z in new[] { 0.03f, 0.012f, -0.002f, 0.001f, 0.03f })
            {
                _boot.Hands.SetPose(HandSide.Right, Finger(root.TransformPoint(MenuMath.CanvasPoint((row.X0 + row.X1) / 2f, (row.Y0 + row.Y1) / 2f, z)), root.forward));
                Step();
            }
            _boot.Hands.SetPose(HandSide.Right, Rest());
            Step(2);
        }

        /// A fingertip into Sunny from 22 cm away, then away again (sunny-tour.md 4.2: armed, fast, not playing).
        void PokeSunny()
        {
            Vector3 c = S.Position;
            Vector3 far = c + Vector3.up * 0.2f + Vector3.left * 0.1f;
            _boot.Hands.SetPose(HandSide.Right, IndexAt(far)); Step(); Step();
            _boot.Hands.SetPose(HandSide.Right, IndexAt(S.Position)); Step();
            _boot.Hands.SetPose(HandSide.Right, IndexAt(far)); Step();
            _boot.Hands.SetPose(HandSide.Right, Rest()); Step();
            Step(45);   // past the 0.6 s poke debounce
        }

        void Drum(string kind) { _app.Get<IDrumsApi>().PlaybackHit(kind, 0.7f, new DrumStrikeInfo { Pos = 0.3f, Zone = "head" }); Step(3); }

        void Answer()
        {
            StepUntil(() => _menu.StartShowing, 3f, "the start panel");
            Step(144);   // a prompt player reads for 2 s
            PokeRow(RowKind.PlayHere);
            StepUntil(() => Run.Step == FirstRunStep.Intro, 2f, "the tour after the answer");
        }

        void AssertSay(string id) => Assert.That(_lines, Does.Contain(id), $"Sunny said {id} (said: {string.Join(" ", _lines)})");

        static readonly FirstRunStep[] s_order =
        {
            FirstRunStep.Start, FirstRunStep.Intro, FirstRunStep.Snow, FirstRunStep.Menu, FirstRunStep.Move,
            FirstRunStep.Set, FirstRunStep.Recordings, FirstRunStep.Recenter, FirstRunStep.Bye,
        };

        /// <summary>What the watching player saw, frame by frame.</summary>
        sealed class Watch
        {
            public readonly List<FirstRunStep> Steps = new List<FirstRunStep>();
            public readonly HashSet<string> Spots = new HashSet<string>();
            public readonly HashSet<string> DemoAt = new HashSet<string>();
            public readonly Dictionary<FirstRunStep, float> Seconds = new Dictionary<FirstRunStep, float>();
            public bool MenuOpened, RecorderRecorded, LayoutChanged, TipsChanged;
        }

        // Steps the passive player until the tour ends, noting what Sunny does (no pokes, no notes, no palm).
        Watch WatchToTheEnd(float seconds = 90f, FastTourCheck check = null)
        {
            var w = new Watch();
            ILayoutService layout = _app.Get<ILayoutService>();
            IRecorder rec = _app.Get<IRecorder>();
            IGuideTour tour = _app.Get<IGuideTour>();
            LayoutMode mode = layout.Mode;
            bool tips = tour.TipsEnabled;
            FirstRunStep last = Run.Step;
            float since = _clock;
            w.Steps.Add(last);
            int n = (int)(seconds / Dt);
            for (int i = 0; i < n && Run.Running; i++)
            {
                Step();
                check?.Frame();
                if (_menu.Shown) w.MenuOpened = true;
                if (Run.SpotRow != null) w.Spots.Add(Run.Step + ":" + Run.SpotRow);
                if (S.DemoKind != DemoKind.None) w.DemoAt.Add(Run.Step + ":" + S.DemoKind);
                if (rec != null && rec.State == RecorderState.Recording) w.RecorderRecorded = true;
                if (layout.Mode != mode) w.LayoutChanged = true;
                if (tour.TipsEnabled != tips) w.TipsChanged = true;
                if (Run.Step != last)
                {
                    w.Seconds[last] = _clock - since;
                    last = Run.Step;
                    since = _clock;
                    if (Run.Running) w.Steps.Add(last);
                }
            }
            if (!w.Seconds.ContainsKey(last)) w.Seconds[last] = _clock - since;
            return w;
        }

        // =====================================================================================================

        // tutorials-audit.md B1 on the real kit at the seat: every drum lesson target lands on its own piece, toward the
        // player, never over a neighbour (the two rack toms sit close and tilted: a target must not straddle them).
        [UnityTest]
        public IEnumerator EveryDrumTargetLandsOnItsOwnPieceTowardThePlayer()
        {
            yield return Boot(startPanel: false);
            global::MusicRoom.Lessons.Tutorial tut = null;
            foreach (var m in _app.Modules) if (m is global::MusicRoom.Lessons.LessonsModule l) tut = l.Tutorial;
            Assert.That(tut, Is.Not.Null);
            IReadOnlyList<DrumPieceInfo> pieces = _app.Get<IDrumsApi>().Pieces;
            Vector3 eye = _app.Head.position;
            int checkedPieces = 0;
            foreach (DrumPieceInfo p in pieces)
            {
                if (p.Transform == null || p.Pad || p.Kind == "kick") continue;
                Vector3 world = p.Transform.TransformPoint(tut.DrumLanding(p));
                Vector3 local = p.Transform.InverseTransformPoint(world);
                float fromCentre = new Vector2(local.x, local.z).magnitude;
                Assert.That(fromCentre, Is.LessThan(p.Radius * 0.75f), $"{p.Kind}: well inside its own head ({fromCentre:F3} of {p.Radius:F3} m)");
                Assert.That(Vector3.Distance(world, eye), Is.LessThan(Vector3.Distance(p.Transform.position, eye)), $"{p.Kind}: toward the player");
                // Seen from the eye, the target is over this piece only: the ray from the eye to it meets no other piece's
                // surface disc in front of it.
                foreach (DrumPieceInfo o in pieces)
                {
                    if (o.Transform == null || o.Transform == p.Transform || o.Pad) continue;
                    Vector3 n = o.Transform.up, c = o.Transform.position, dir = world - eye;
                    float den = Vector3.Dot(n, dir);
                    if (Mathf.Abs(den) < 1e-5f) continue;
                    float k = Vector3.Dot(n, c - eye) / den;
                    if (k <= 0f || k >= 0.999f) continue;   // behind the eye, or behind the target
                    Vector3 hit = eye + dir * k;
                    Assert.That(Vector3.Distance(hit, c), Is.GreaterThan(o.Radius), $"{p.Kind}'s target is not hidden under {o.Kind}");
                }
                checkedPieces++;
            }
            Assert.That(checkedPieces, Is.GreaterThanOrEqualTo(7), "snare, the toms, floor tom, hi-hat, crash, ride");
        }

        [UnityTest]
        public IEnumerator AFirstTimerWhoDoesNothingSeesTheFastTourInOrderWithinFortyFiveSecondsAndNoSong()
        {
            yield return Boot();
            Assert.IsTrue(Run.Running, "the first run waits under the start panel");
            Assert.That(Run.Step, Is.EqualTo(FirstRunStep.Start));
            Assert.IsFalse(S.Visible, "Sunny is not shown at first");
            bool tipsBefore = _app.Get<IGuideTour>().TipsEnabled;
            bool snowBefore = _app.Get<IAmbienceSwitches>().Get(AmbienceKind.Snow);
            Answer();
            Assert.IsFalse(_menu.Shown, "the menu is closed when the tour starts");
            var check = new FastTourCheck(_app, Run, S);
            float t0 = _clock;
            Watch w = WatchToTheEnd(60f, check);
            float took = _clock - t0;
            check.Dispose();
            var per = new System.Text.StringBuilder();
            foreach (var kv in w.Seconds) per.Append($"{kv.Key} {kv.Value:F1} s; ");
            Debug.Log($"[FASTTOUR] VR first launch: {took:F1} s ({per}) {check.Report()}");
            Assert.IsFalse(Run.Running, "the tour ends by itself");
            StepUntil(() => !S.Present, 4f, "Sunny leaves");
            // The order, every stop done (none waited for the player, none gave up), fast.
            Assert.That(w.Steps, Is.EqualTo(new List<FirstRunStep>(s_order).GetRange(1, s_order.Length - 1)), string.Join(",", w.Steps));
            Assert.That(check.Steps, Is.EqualTo(FastTourCheck.Stops), "each stop visited once, in order");
            Assert.That(Run.Outcomes.ConvertAll(e => e.StepId), Is.EqualTo(new List<FirstRunStep>(s_order).ConvertAll(x => x.ToString())));
            foreach (GuideStepEvent e in Run.Outcomes) Assert.That(e.Outcome, Is.EqualTo(StepOutcome.Done), $"{e.StepId} {e.Outcome}");
            Assert.That(took, Is.LessThanOrEqualTo(45f), $"the whole tour in {took:F1} s");
            foreach (var kv in w.Seconds) Assert.That(kv.Value, Is.LessThan(9f), $"{kv.Key} took {kv.Value:F1} s");
            // What it said, in order: one short line per stop.
            string[] said = { FirstRun.LFastSnow, FirstRun.LOpen, FirstRun.LFastMove, FirstRun.LFastSet, FirstRun.LFastRec,
                FirstRun.LFastSaved, FirstRun.LFastRecenter, FirstRun.LFastBye };
            int at = -1;
            foreach (string id in said)
            {
                int k = _lines.IndexOf(id);
                Assert.That(k, Is.GreaterThan(at), $"{id} after the one before (said: {string.Join(" ", _lines)})");
                at = k;
            }
            AssertSay(GuideLines.IntroVr);
            AssertSay(GuideLines.Leave);
            foreach (string old in FirstRun.RetiredLines) Assert.That(_lines, Does.Not.Contain(old), "the old tour's " + old);
            // Every target the real thing, the real rows lit, the ghost hand's gestures.
            check.AssertAllTargets(allowAbsent: false, "VR");
            foreach (string spot in new[] { "Move:arrange", "Set:layout.single", "Recordings:recordings", "Recenter:resetView" })
                Assert.That(w.Spots, Does.Contain(spot), string.Join(",", w.Spots));
            Assert.IsTrue(new List<string>(check.Rows).Exists(r => r.StartsWith("Set:inst.")), "a tile of the Instruments screen: " + string.Join(",", check.Rows));
            foreach (string demo in new[] { "Snow:Press", "Menu:Palm", "Move:Press", "Move:Pinch", "Set:Press", "Recordings:Press", "Recenter:Press", "Recenter:Pinch", "Bye:Press" })
                Assert.That(w.DemoAt, Does.Contain(demo), string.Join(",", w.DemoAt));
            // Sunny flew to the switches and the deck (beside them, within reach of a poke).
            Assert.That(check.SunnyNearest["Snow:Switch"], Is.LessThan(0.5f), "Sunny beside the SNOW switch");
            Assert.That(check.SunnyNearest["Recordings:DeckRec"], Is.LessThan(0.5f), "Sunny beside the deck");
            // No song: no note, no playback, no lesson; nothing of the player's changed; the menu back as it was (closed).
            check.AssertNothingPlayed("VR");
            Assert.That(_firstSound, Is.LessThan(0f), "not a single piano note");
            Assert.IsTrue(w.MenuOpened);
            Assert.IsFalse(_menu.Shown, "the menu closed again, as the player had it");
            Assert.That(_menu.Controller.Spotlight, Is.EqualTo(-1), "no row left lit");
            Assert.IsFalse(w.RecorderRecorded, "REC was only shown");
            Assert.IsFalse(w.LayoutChanged, "One alone / My set were only shown");
            Assert.IsFalse(w.TipsChanged);
            Assert.That(_app.Get<IGuideTour>().TipsEnabled, Is.EqualTo(tipsBefore));
            Assert.That(_app.Get<IAmbienceSwitches>().Get(AmbienceKind.Snow), Is.EqualTo(snowBefore), "SNOW only shown");
            Assert.IsTrue(_app.Settings.TourDone);
            Assert.That(_app.Settings.GetInt(FirstRun.KeyTourVersion, 0), Is.EqualTo(FirstRun.TourVersion));
            Assert.IsFalse(_guide.TipSeen("tip_menu"), "the palm never opened it: its tip comes later");
            Assert.IsTrue(_guide.TipSeen("tip_switches"), "the switches were just shown");
        }

        /// <summary>One alone (the player had a single instrument): Show me around shows the same stops, every target real,
        /// the tile pressed is the one alone instrument's, and still no note.</summary>
        [UnityTest]
        public IEnumerator WithOneAloneEveryTargetIsStillTheRealThing()
        {
            yield return Boot(startPanel: false);
            ILayoutService layout = _app.Get<ILayoutService>();
            layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            Step(72 * 3);
            Assert.That(layout.Mode, Is.EqualTo(LayoutMode.Single));
            _guide.Start();   // Show me around
            Assert.IsTrue(Run.Running && Run.Replay);
            var check = new FastTourCheck(_app, Run, S);
            float t0 = _clock;
            Watch w = WatchToTheEnd(60f, check);
            float took = _clock - t0;
            check.Dispose();
            Debug.Log($"[FASTTOUR] One alone ({layout.Current}): {took:F1} s; {check.Report()}");
            Assert.IsFalse(Run.Running);
            Assert.That(check.Steps, Is.EqualTo(FastTourCheck.Stops));
            Assert.That(took, Is.LessThanOrEqualTo(45f));
            check.AssertAllTargets(allowAbsent: false, "One alone");
            Assert.IsTrue(check.Rows.Contains("Set:inst." + _app.Registry.Get(layout.Current).Id), "the one alone instrument's tile: " + string.Join(",", check.Rows));
            check.AssertNothingPlayed("One alone");
            Assert.That(layout.Mode, Is.EqualTo(LayoutMode.Single), "still One alone");
            Assert.IsFalse(_menu.Shown);
        }

        [UnityTest]
        public IEnumerator TheSnowStopPointsAtTheRealLeverAndNeverFlipsIt([Values(true, false)] bool snowOn)
        {
            yield return Boot();
            IAmbienceSwitches sw = _app.Get<IAmbienceSwitches>();
            sw.Set(AmbienceKind.Snow, snowOn);
            Answer();
            StepUntil(() => Run.Step == FirstRunStep.Snow, 8f, "the SNOW stop");
            StepUntil(() => S.LineId == FirstRun.LFastSnow && S.DemoKind == DemoKind.Press, 3f, "the line and the ghost finger");
            Assert.IsFalse(_menu.Shown, "no menu for the side table");
            Assert.IsTrue(_app.Get<ITourAnchors>().TryGetAnchor(FirstRun.SnowSwitch, out Vector3 lever, out _));
            Assert.That(Vector3.Distance(S.Anchor.Point, lever), Is.LessThan(1e-3f), "the ghost finger on the SNOW lever");
            Assert.That(GuideLines.TextOf(FirstRun.LFastSnow), Does.Contain("SNOW"), "the line names the switch");
            float before = Vector3.Distance(S.Position, lever);
            Step(72);
            float after = Vector3.Distance(S.Position, lever);
            Debug.Log($"[FASTTOUR] Sunny to the SNOW lever: {before:F2} -> {after:F2} m (head {Vector3.Distance(_app.Head.position, lever):F2} m)");
            Assert.That(after, Is.LessThan(0.5f), "Sunny flew beside the switch");
            Assert.That(Vector3.Distance(S.Position, _app.Head.position), Is.InRange(FirstRun.FlyMin - 0.08f, FirstRun.FlyMax + 0.1f), "within a poke's reach");
            StepUntil(() => Run.Step == FirstRunStep.Menu, 8f, "on to the menu");
            Assert.That(sw.Get(AmbienceKind.Snow), Is.EqualTo(snowOn), "the switch is never flipped");
            Assert.That(Run.Outcomes.Find(e => e.StepId == "Snow").Outcome, Is.EqualTo(StepOutcome.Done));
        }

        [UnityTest]
        public IEnumerator SunnyHelpsAtTheStartPanelAfterEightSecondsAndNeverAnswersIt()
        {
            yield return Boot();
            StepUntil(() => _menu.StartShowing, 3f, "the start panel");
            Step(72 * 7);
            Assert.IsFalse(S.Visible, "nothing before 8 s");
            Step(72 + 10);
            Assert.IsTrue(S.Visible, "Sunny comes at 8 s");
            AssertSay(FirstRun.LStartHelp);
            Step(30);
            Assert.That(S.DemoKind, Is.EqualTo(DemoKind.Press), "the ghost finger presses Play here");
            Assert.IsTrue(_app.Get<IMenuGuideHooks>().TryGetRowRect("playHere", out Vector3 c, out _, out _));
            Assert.That(Vector3.Distance(S.Anchor.Point, c), Is.LessThan(1e-3f), "on the Play here row");
            // Beside the panel, off its face: the body never covers a row.
            Vector3 local = _menu.Root.InverseTransformPoint(S.Position);
            Assert.That(Mathf.Abs(local.x), Is.GreaterThan(0.16f + SunnyPoke.BodyRadius - 0.02f), "beside the panel");
            Step(72 * 30);
            Assert.IsTrue(((IMenuPanel)_menu).StartPending, "never answered for the player (30 s more)");
            Assert.That(Run.Step, Is.EqualTo(FirstRunStep.Start));
            AssertSay(FirstRun.LAgain);
            // A poke: the help stops; the panel still waits.
            PokeSunny();
            Assert.That(S.Pokes, Is.EqualTo(1));
            Assert.That(S.DemoKind, Is.EqualTo(DemoKind.None), "Sunny stops pointing");
            Assert.IsTrue(((IMenuPanel)_menu).StartPending);
            PokeRow(RowKind.PlayHere);
            StepUntil(() => Run.Step == FirstRunStep.Intro, 2f, "the tour");
            StepUntil(() => S.LineId == FirstRun.LQuick, 2f, "already introduced: a quick tour, no second Hi, I'm Sunny");
            Assert.That(_lines, Does.Not.Contain(GuideLines.IntroVr));
        }

        [UnityTest]
        public IEnumerator PokingSunnySkipsEachStepInTurnAndTheMenuEndsClosed()
        {
            yield return Boot();
            Answer();
            for (int i = 2; i < s_order.Length; i++)
            {
                FirstRunStep from = s_order[i - 1];
                StepUntil(() => S.BubbleVisible && Run.Step == from, 4f, $"{from} under way");
                Step(20);
                PokeSunny();
                Assert.That(Run.Step, Is.EqualTo(s_order[i]), $"{from} skipped");
                Assert.That(Run.Outcomes[Run.Outcomes.Count - 1].Outcome, Is.EqualTo(StepOutcome.Skipped));
            }
            StepUntil(() => S.BubbleVisible, 3f, "the goodbye");
            Step(10);
            PokeSunny();
            Assert.IsFalse(Run.Running, "a poke at the goodbye: Sunny leaves now");
            StepUntil(() => !S.Present, 2f, "gone");
            Assert.IsFalse(_menu.Shown, "the menu is closed, as it was");
            Assert.That(_menu.Controller.Spotlight, Is.EqualTo(-1));
            Assert.IsTrue(_app.Settings.TourDone);
            Assert.That(_clock - _panelAt, Is.LessThan(40f), "a poker is done fast");
        }

        [UnityTest]
        public IEnumerator HideSunnyInTheMenuSkipsTheRestOfTheTour()
        {
            yield return Boot();
            Answer();
            StepUntil(() => Run.Step == FirstRunStep.Recenter && _menu.Shown && _menu.Screen == MenuScreen.Home, 45f, "the Recenter stop on the home page");
            Step(20);
            PokeRow(RowKind.Guide);   // "Hide Sunny" while Sunny is out
            Assert.IsFalse(Run.Running, "the tour ended");
            StepUntil(() => !S.Present, 3f, "Sunny leaves");
            Assert.IsTrue(_menu.Shown, "the menu stays with the player who is using it");
            Assert.IsTrue(_app.Settings.TourDone, "the first launch counts as done");
            Step(72 * 3);
            Assert.IsFalse(Run.Running);
        }

        [UnityTest]
        public IEnumerator ShowMeAroundReplaysTheTourAndGivesTheMenuBack()
        {
            yield return Boot();
            Answer();
            WatchToTheEnd();
            Assert.IsFalse(Run.Running);
            StepUntil(() => !S.Present, 4f, "the first run over");
            Step(72);
            _menu.Show();
            Step(5);
            _menu.Go(MenuScreen.Guide);
            Step(5);
            PokeRow(RowKind.Tour);   // Show me around
            Assert.IsTrue(Run.Running && Run.Replay, "the replay");
            Assert.That(Run.Step, Is.EqualTo(FirstRunStep.Intro));
            Assert.IsFalse(_menu.Shown, "the Tour row closes the menu first");
            Watch w = WatchToTheEnd();
            Assert.IsFalse(Run.Running);
            Assert.That(w.Steps, Is.EqualTo(new List<FirstRunStep>(s_order).GetRange(1, s_order.Length - 1)));
            foreach (GuideStepEvent e in Run.Outcomes) Assert.That(e.Outcome, Is.EqualTo(StepOutcome.Done), e.StepId);
            AssertSay(GuideLines.IntroVr);
            Assert.IsFalse(_menu.Shown, "closed, as the player left it");
            Assert.That(_menu.Screen, Is.EqualTo(MenuScreen.Guide), "and it opens on the Sunny screen again");
        }

        [UnityTest]
        public IEnumerator ARowThePlayerPokesEndsTheTourAndLeavesTheMenuToThem()
        {
            yield return Boot();
            Answer();
            StepUntil(() => Run.Step == FirstRunStep.Set && _menu.Screen == MenuScreen.Instruments, 30f, "the Instruments screen");
            Step(30);
            PokeRow(RowKind.Back);
            Step(2);
            Assert.IsFalse(Run.Running, "the player took over");
            Assert.IsTrue(Run.PlayerTookOver);
            Assert.IsTrue(_menu.Shown, "their menu stays open");
            Assert.That(_menu.Screen, Is.EqualTo(MenuScreen.Home), "on the screen they went to");
        }

        [UnityTest]
        public IEnumerator AGuideFrameOfTheFirstRunAllocatesNothing()
        {
            yield return Boot();
            Answer();
            StepUntil(() => Run.Step == FirstRunStep.Set && S.DemoKind == DemoKind.Press, 30f, "a lit row and the ghost finger on it");
            Assert.IsTrue(S.WorldDemo);
            Assert.That(() => { s_keep = new float[1024]; }, Is.AllocatingGCMemory(), "the recorder works here");
            Assert.That(() =>
            {
                for (int i = 0; i < 60; i++) _guide.Tick(FramePhase.Guide, Dt);
            }, Is.Not.AllocatingGCMemory());
            StepUntil(() => Run.Step == FirstRunStep.Recordings && Run.Aiming == FirstRunAim.DeckRec && S.DemoKind == DemoKind.Press, 20f, "REC, Sunny flying to the deck");
            Assert.That(() =>
            {
                for (int i = 0; i < 30; i++) _guide.Tick(FramePhase.Guide, Dt);
            }, Is.Not.AllocatingGCMemory());
        }

        static float[] s_keep;

        [UnityTest]
        public IEnumerator WithoutTheStartPanelThereIsNoFirstRun()
        {
            yield return Boot(startPanel: false);
            Step(72 * 12);
            Assert.IsFalse(Run.Running, "TestBoot's default: no start panel, no first run");
            Assert.IsFalse(S.Present);
        }
    }
}
