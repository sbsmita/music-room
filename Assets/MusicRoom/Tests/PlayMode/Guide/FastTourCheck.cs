// The fast first-timer tour (FirstRun.cs, 2026-11-16), watched frame by frame by a player who does nothing: which stop
// is on, what the ghost hand points at, and whether that target is the real thing here (the SNOW lever on the side
// table, the very menu row lit, the deck's own REC / PLAY / GUIDE, an instrument on display), in VR, in mixed reality and
// with One alone. Also everything that must NOT happen: no note, no playback, no lesson, no recording, no switch flipped,
// Move instruments never on, the layout never changed. Shared by FirstRunWalkthroughTests (VR, One alone) and
// FastTourMrTests (mixed reality).
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Guide;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode.Guide
{
    public sealed class FastTourCheck
    {
        /// <summary>Every (stop, target) pair a full fast tour shows, in order.</summary>
        public static readonly string[] Expected =
        {
            "Snow:Switch", "Menu:Sunny", "Move:Row", "Move:Instrument", "Set:Row", "Recordings:DeckRec", "Recordings:DeckPlay",
            "Recordings:Row", "Recenter:Row", "Recenter:Sunny", "Bye:DeckGuide",
        };

        public static readonly FirstRunStep[] Stops =
        {
            FirstRunStep.Intro, FirstRunStep.Snow, FirstRunStep.Menu, FirstRunStep.Move, FirstRunStep.Set,
            FirstRunStep.Recordings, FirstRunStep.Recenter, FirstRunStep.Bye,
        };

        /// <summary>A target may lag its beat a frame or two (a menu screen laid out the frame after it opens).</summary>
        public const int Lag = 3;

        readonly App _app;
        readonly FirstRun _run;
        readonly Sunny _sunny;
        public readonly List<FirstRunStep> Steps = new List<FirstRunStep>();
        /// <summary>(stop:target) in the order first seen; per pair the frames the target was right, wrong, or not here.</summary>
        public readonly List<string> Seen = new List<string>();
        public readonly Dictionary<string, int> Ok = new Dictionary<string, int>(), Bad = new Dictionary<string, int>(), Absent = new Dictionary<string, int>();
        public readonly List<string> Errors = new List<string>();
        public readonly HashSet<string> Rows = new HashSet<string>();
        /// <summary>The closest Sunny came to each world target (m), and how far the target was from the head.</summary>
        public readonly Dictionary<string, float> SunnyNearest = new Dictionary<string, float>(), TargetFromHead = new Dictionary<string, float>();
        public int Notes, Playbacks, Recorded, Flips, Lessons;
        public bool MoveTurnedOn, LayoutChanged;
        readonly LayoutMode _mode;
        readonly InstrumentKind _current;
        readonly int _setCount;

        public FastTourCheck(App app, FirstRun run, Sunny sunny)
        {
            _app = app;
            _run = run;
            _sunny = sunny;
            app.Events.Subscribe<RecorderEvent>(OnNote);
            app.Events.Subscribe<PlaybackNoteEvent>(OnPlaybackNote);
            app.Events.Subscribe<RecordingPlaybackEvent>(OnPlayback);
            app.Events.Subscribe<RecordedNoteEvent>(OnRecorded);
            app.Events.Subscribe<AmbienceEvent>(OnFlip);
            app.Events.Subscribe<TutorialEvent>(OnTutorial);
            ILayoutService l = app.Get<ILayoutService>();
            if (l != null) { _mode = l.Mode; _current = l.Current; _setCount = l.MySet != null ? l.MySet.Count : 0; }
        }

        public void Dispose()
        {
            _app.Events.Unsubscribe<RecorderEvent>(OnNote);
            _app.Events.Unsubscribe<PlaybackNoteEvent>(OnPlaybackNote);
            _app.Events.Unsubscribe<RecordingPlaybackEvent>(OnPlayback);
            _app.Events.Unsubscribe<RecordedNoteEvent>(OnRecorded);
            _app.Events.Unsubscribe<AmbienceEvent>(OnFlip);
            _app.Events.Unsubscribe<TutorialEvent>(OnTutorial);
        }

        void OnNote(RecorderEvent e) => Notes++;
        void OnPlaybackNote(PlaybackNoteEvent e) => Notes++;
        void OnPlayback(RecordingPlaybackEvent e) => Playbacks++;
        void OnRecorded(RecordedNoteEvent e) => Recorded++;
        void OnFlip(AmbienceEvent e) => Flips++;
        void OnTutorial(TutorialEvent e) { if (!string.IsNullOrEmpty(e.LessonId)) Lessons++; }

        /// <summary>One frame of the tour (call after the app's frame).</summary>
        public void Frame()
        {
            IArrangeMode arrange = _app.Get<IArrangeMode>();
            if (arrange != null && (arrange.MoveOn || arrange.Arranging)) MoveTurnedOn = true;
            ILayoutService layout = _app.Get<ILayoutService>();
            if (layout != null && (layout.Mode != _mode || (layout.Mode == LayoutMode.Single && layout.Current != _current)
                || (layout.MySet != null ? layout.MySet.Count : 0) != _setCount)) LayoutChanged = true;
            ILooper looper = _app.Get<ILooper>();
            if (looper != null && looper.Status != LoopStatus.Off) Recorded++;
            IRecorder rec = _app.Get<IRecorder>();
            if (rec != null && rec.State != RecorderState.Idle) Recorded++;
            if (!string.IsNullOrEmpty(_app.Get<ITutorial>()?.LessonId)) Lessons++;
            if (!_run.Running) return;
            if (Steps.Count == 0 || Steps[Steps.Count - 1] != _run.Step) Steps.Add(_run.Step);
            FirstRunAim aim = _run.Aiming;
            if (aim == FirstRunAim.None) return;
            string key = _run.Step + ":" + aim;
            if (!Seen.Contains(key)) { Seen.Add(key); Ok[key] = Bad[key] = Absent[key] = 0; }
            switch (Judge(aim, key, out string why))
            {
                case 1: Ok[key]++; break;
                case 0: Absent[key]++; break;
                default:
                    Bad[key]++;
                    if (Bad[key] == Lag + 1) Errors.Add($"{key}: {why}");
                    break;
            }
        }

        // 1: the ghost hand is on the real target; 0: the target is not here and nothing is pointed at; -1: wrong.
        int Judge(FirstRunAim aim, string key, out string why)
        {
            why = null;
            Transform head = _app.Head;
            switch (aim)
            {
                case FirstRunAim.Sunny:
                    if (_sunny.DemoKind == DemoKind.Palm || _sunny.DemoKind == DemoKind.Pinch) return 1;
                    why = "Sunny's own demo is " + _sunny.DemoKind;
                    return -1;
                case FirstRunAim.Row:
                {
                    string id = _run.SpotWanted;
                    IMenuGuideHooks hooks = _app.Get<IMenuGuideHooks>();
                    IMenuPanel menu = _app.Get<IMenuPanel>();
                    if (hooks == null || menu == null) return Pointing(out why) ? -1 : 0;
                    if (!menu.Shown) { why = "the menu is closed"; return -1; }
                    if (id == null || !hooks.TryGetRowRect(id, out Vector3 c, out _, out _)) { why = $"no row {id} on {menu.Screen}"; return -1; }
                    if (_run.SpotRow != id) { why = $"{id} is not lit"; return -1; }
                    Rows.Add(_run.Step + ":" + id);
                    return At(c, key, out why) ? 1 : -1;
                }
                case FirstRunAim.DeckRec:
                case FirstRunAim.DeckPlay:
                case FirstRunAim.DeckGuide:
                {
                    LoopControl ctl = aim == FirstRunAim.DeckRec ? LoopControl.Rec : aim == FirstRunAim.DeckPlay ? LoopControl.Play : LoopControl.Guide;
                    ILooper l = _app.Get<ILooper>();
                    if (l == null || !l.TryGetControl(ctl, 0, out Vector3 w, out _)) return Pointing(out why) ? -1 : 0;
                    return At(w, key, out why) ? 1 : -1;
                }
                case FirstRunAim.Switch:
                {
                    ITourAnchors anchors = _app.Get<ITourAnchors>();
                    if (anchors == null || !anchors.TryGetAnchor(FirstRun.SnowSwitch, out Vector3 w, out _)) return Pointing(out why) ? -1 : 0;
                    return At(w, key, out why) ? 1 : -1;
                }
                case FirstRunAim.Instrument:
                {
                    if (!_sunny.WorldDemo || _sunny.DemoKind != DemoKind.Pinch) { why = "no pinch at an instrument"; return -1; }
                    Vector3 p = _sunny.Anchor.Point;
                    // On an instrument on display (its rig in the scene and shown): on one of its grab handles, or over its group.
                    IRigRegistry rigs = _app.Get<IRigRegistry>();
                    ILayoutService layout = _app.Get<ILayoutService>();
                    foreach (InstrumentDescriptor d in _app.Registry.All)
                    {
                        if (!InstrumentIds.IsPlayable(d.Kind)) continue;
                        Rig rig = rigs?.Get(InstrumentIds.RigOf(d.Kind));
                        Transform g = rig?.Group;
                        if (g == null || !g.gameObject.activeInHierarchy || !rig.InScene || (layout != null && !layout.IsInScene(d.Kind))) continue;
                        bool on = new Vector2(p.x - g.position.x, p.z - g.position.z).magnitude < 0.01f;
                        foreach (Renderer r in rig.Handles)
                            if (r != null && r.enabled && r.gameObject.activeInHierarchy && (r.bounds.ClosestPoint(p) - p).sqrMagnitude < 1e-4f) on = true;
                        if (!on) continue;
                        if (head != null && Vector3.Distance(head.position, p) < FirstRun.PinchClear - 1e-3f) { why = "the pinch is in the player's face"; return -1; }
                        return At(p, key, out why) ? 1 : -1;
                    }
                    why = "the pinch is over no instrument on display";
                    return -1;
                }
            }
            why = "unknown target " + aim;
            return -1;
        }

        bool Pointing(out string why)
        {
            bool world = _sunny.WorldDemo && _sunny.DemoKind != DemoKind.None;
            why = world ? "the ghost hand points at something that is not here" : null;
            return world;
        }

        bool At(Vector3 target, string key, out string why)
        {
            why = null;
            if (!_sunny.WorldDemo) { why = "no world demo"; return false; }
            float off = Vector3.Distance(_sunny.Anchor.Point, target);
            if (off > 1e-3f) { why = $"the ghost hand is {off:F3} m off its target"; return false; }
            Transform head = _app.Head;
            if (head != null)
            {
                float far = Vector3.Distance(head.position, target);
                TargetFromHead[key] = far;
                if (far > 2.5f) { why = $"the target is {far:F2} m from the head"; return false; }
                float s = Vector3.Distance(_sunny.Position, target);
                if (!SunnyNearest.TryGetValue(key, out float best) || s < best) SunnyNearest[key] = s;
            }
            return true;
        }

        /// <summary>The whole tour was seen in order, each target right (or, where allowed, not here and not pointed at).</summary>
        public void AssertAllTargets(bool allowAbsent, string where)
        {
            Assert.That(Errors, Is.Empty, where + ": " + string.Join("; ", Errors));
            int at = -1;
            foreach (string k in Expected)
            {
                int i = Seen.IndexOf(k);
                Assert.That(i, Is.GreaterThan(at), $"{where}: {k} shown, after the one before (seen: {string.Join(" ", Seen)})");
                at = i;
                if (Ok[k] > 0) continue;
                Assert.IsTrue(allowAbsent && Absent[k] > 0, $"{where}: {k} never on its target (ok {Ok[k]}, bad {Bad[k]}, absent {Absent[k]})");
            }
            foreach (string k in Seen) Assert.That(Bad[k], Is.LessThanOrEqualTo(Lag), $"{where}: {k} wrong for {Bad[k]} frames");
        }

        public void AssertNothingPlayed(string where)
        {
            Assert.That(Notes, Is.Zero, where + ": no note, live or played back");
            Assert.That(Playbacks, Is.Zero, where + ": no playback");
            Assert.That(Recorded, Is.Zero, where + ": nothing recorded, no loop");
            Assert.That(Lessons, Is.Zero, where + ": no lesson, no song");
            Assert.That(Flips, Is.Zero, where + ": no switch flipped (SNOW only shown)");
            Assert.IsFalse(MoveTurnedOn, where + ": Move instruments only shown");
            Assert.IsFalse(LayoutChanged, where + ": My set / One alone only shown");
        }

        public string Report()
        {
            var sb = new System.Text.StringBuilder();
            foreach (string k in Seen)
            {
                sb.Append(k).Append(" ok ").Append(Ok[k]).Append(" bad ").Append(Bad[k]).Append(" absent ").Append(Absent[k]);
                if (SunnyNearest.TryGetValue(k, out float s)) sb.Append($" sunny {s:F2} m from it");
                if (TargetFromHead.TryGetValue(k, out float h)) sb.Append($", {h:F2} m from the head");
                sb.Append("; ");
            }
            return sb.ToString();
        }
    }
}
