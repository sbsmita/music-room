// Sunny's contextual tips (Docs/11-first-five/sunny-tour.md 3.1): every tip of TourTips, from its trigger to its
// line, its demo at the target and its success. Since the platform merge there is ONE scheduler for every tip Sunny says,
// the platform's TipRunner (Guide/GuideTips.cs): the tour's tips are TipSpecs of TourTipSource next to the platform's and
// every instrument's, and this class is their presenter (ITipPresenter) and their signal source.
//   1 Trigger: TourSignals says the tip's moment came; this class makes it pending in the TipRunner (TipRunner.Trigger),
//     never when it was learned (P1: never twice a launch). Doing it yourself learns it.
//   2 A quiet moment (Quiet, for EVERY tip, the platform's and the instruments' too): no live play for 2.5 s, no grab held
//     (arrange tips excepted), no menu poke in the last 1.5 s (0.8 s for the menu's own tips), no lesson running (lesson
//     tips excepted), no loop count-in or recording pass, mixed reality not placing (its placing tip excepted), Sunny free
//     (no first run, deck walkthrough, other tip or hint). The tip waits up to its class's time (P1 30 s, P2 6 s, P3 20 s),
//     else it is dropped, unseen, for now.
//   3 Spacing (the TipRunner's, one for all): P1 / P2 10 s after the previous tip; P3 not in the first 90 s after the tour,
//     at most 3 in 10 minutes, 60 s apart. "Tips while I play" off stops P2 / P3 (P1 still helps); Hide Sunny during a tip
//     stops them all for the launch (Allowed, for every tip).
//   4 Shown (Show, when the TipRunner picks one of ours): Sunny comes beside the head, says the line(s), the ghost hand
//     shows it at the target; a tip with a success waits up to 15 s for it (8 s: "Like this." and the demo again), then
//     praise; Sunny leaves without a word.
//   5 Learned (persisted, musicroom.tipsSeen) when the line was heard to the end, when the player did it (even with no tip
//     shown) or when Sunny was poked during it. A tip cut short stays unseen.
//   The first unsolicited tip ever adds "poke me anytime to skip" (if never heard), the third "You can hide me".
//   The Tips list (the menu's Sunny screen) plays any tip at once (the platform's and the instruments' through
//   TipRunner.PlayNow).
// Plain C#, ticked by GuideModule in the Guide phase. No allocation per frame.
using System;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Guide
{
    public sealed class TourTipRunner : ITipPresenter
    {
        public const float QuietPlay = 2.5f, MenuPokeQuiet = 1.5f, ScreenPokeQuiet = 0.8f, SuccessWait = 15f, Redemo = 8f;
        public const int HideAfter = 3;
        public const string KeyTipsOn = "musicroom.tipsOn", KeyTipsShown = "musicroom.tipsShown";
        /// <summary>Tests: run the tips without the start panel (the app always has it; TestBoot's default has not).</summary>
        public static bool ForceEnabled;

        readonly App _app;
        readonly Sunny _sunny;
        readonly Func<string, bool> _seen;
        readonly Action<string> _markSeen;
        readonly TipRunner _runner;
        readonly Func<bool> _free;
        public readonly TourSignals Signals;
        readonly TipEntry[] _entries;
        readonly System.Collections.Generic.Dictionary<TipSpec, int> _index = new System.Collections.Generic.Dictionary<TipSpec, int>();
        readonly bool[] _shownLaunch;
        readonly float[] _seenAt;

        // the tip being shown
        TourTip _cur;
        int _curIndex = -1, _line, _appendCount;
        readonly string[] _append = new string[2];
        float _curT, _idle, _wait;
        bool _waiting, _succeeded, _praised, _redemoed, _fromList, _leaving;

        public bool Enabled => (_app.StartPanel || ForceEnabled) && !HiddenThisLaunch;
        public bool HiddenThisLaunch { get; private set; }
        public bool Showing => _cur != null;
        public string ShowingId => _cur?.Id;
        public int Shown { get; private set; }
        /// <summary>The demo's anchor this frame (tests).</summary>
        public DemoAnchor Anchor { get; private set; }

        /// <param name="runner">The platform's TipRunner: the one scheduler (TourTipSource's TipSpecs are in it).</param>
        /// <param name="free">Sunny is free for a tip, "Move instruments" or not (no first run, deck walkthrough, tip, hint).</param>
        public TourTipRunner(App app, Sunny sunny, Func<string, bool> seen, Action<string> markSeen, TipRunner runner, Func<bool> free)
        {
            _app = app;
            _sunny = sunny;
            _seen = seen;
            _markSeen = markSeen;
            _runner = runner;
            _free = free;
            int n = TourTips.All.Length;
            _entries = new TipEntry[n];
            _shownLaunch = new bool[n];
            _seenAt = new float[n];
            for (int i = 0; i < n; i++)
            {
                _seenAt[i] = float.NegativeInfinity;
                _entries[i] = runner?.Find(TourTips.All[i].Id);
                if (_entries[i] != null) _index[_entries[i].Spec] = i;
                else if (runner != null) Debug.LogError("[Guide] tour tip " + TourTips.All[i].Id + " is not in the tip runner (TourTipSource)");
            }
            Signals = new TourSignals(app, seen, SeenAt);
        }

        /// <summary>The TipRunner entry of tour tip i (null without a runner).</summary>
        public TipEntry Entry(int i) => i >= 0 && i < _entries.Length ? _entries[i] : null;

        int IndexOf(TipEntry e) => e != null && _index.TryGetValue(e.Spec, out int i) ? i : -1;

        public void Dispose() => Signals.Dispose();

        float SeenAt(string id) { int i = TourTips.IndexOf(id); return i >= 0 ? _seenAt[i] : float.NegativeInfinity; }

        /// <summary>"Tips while I play" (musicroom.tipsOn, on by default).</summary>
        public bool TipsEnabled
        {
            get => _app.Settings.GetInt(KeyTipsOn, 1) != 0;
            set { _app.Settings.SetInt(KeyTipsOn, value ? 1 : 0); _app.Settings.Save(); }
        }

        bool Learned(int i)
        {
            TourTip t = TourTips.All[i];
            return _seen(t.Id);
        }

        void Learn(int i)
        {
            if (_seen(TourTips.All[i].Id)) return;
            _markSeen(TourTips.All[i].Id);
            _seenAt[i] = Signals.Time;
            TipEntry e = _entries[i];
            if (e != null) { e.Seen = true; e.PendingUntil = -1f; }   // the scheduler forgets it (saved at a quiet moment)
            _app.Events.Publish(new GuideTipEvent { TipId = TourTips.All[i].Id, Seen = true });
        }

        /// <summary>The first run ended (or was never needed): P3 tips wait 90 s from now (the runner's clock too).</summary>
        public void TourEnded()
        {
            if (Signals.TourOver) return;
            Signals.TourOver = true;
            Signals.TourEndedAt = Signals.Time;
            if (_runner != null && float.IsNaN(_runner.TourEnd)) _runner.TourEnd = _runner.Time;
        }

        // =====================================================================================================

        /// <summary>One frame, before the TipRunner's: the signals, the successes (learned even when never shown), the tip
        /// being shown, and the triggers (pending in the TipRunner, which decides when Sunny speaks).</summary>
        public void Tick(float dt, bool firstRun)
        {
            Signals.FirstRunRunning = firstRun;
            Signals.Update(dt);
            TourTip[] all = TourTips.All;
            // Doing it yourself counts: any tip whose success happened is learned (shown or not). Only where tips run (the
            // app; tests opt in), so other modules' tests never write the store.
            bool learn = _app.StartPanel || ForceEnabled;
            for (int i = 0; i < all.Length && learn; i++)
            {
                TourTip t = all[i];
                bool showing = _cur == t;
                if (t.Success == TourSuccess.None || (Learned(i) && !showing)) continue;
                if (!Signals.Succeeded(t)) continue;
                Learn(i);
                if (showing) _succeeded = true;
            }
            if (_cur != null) Drive(dt);
            if (Enabled && !firstRun && _runner != null) TriggerTips();
            Signals.ClearEdges();
        }

        // A trigger that fired makes its tip pending in the TipRunner (once: a pending tip keeps its deadline).
        void TriggerTips()
        {
            TourTip[] all = TourTips.All;
            bool presenting = _app.IsPresenting;
            for (int i = 0; i < all.Length; i++)
            {
                TourTip t = all[i];
                TipEntry e = _entries[i];
                if (e == null || e.Pending || _cur == t) continue;
                if (t.Trigger == TourTrigger.Manual || t.Trigger == TourTrigger.Appended || t.Trigger == TourTrigger.Off) continue;
                bool p1 = t.Class == TourTipClass.P1;
                if (p1 ? _shownLaunch[i] || (t.Success != TourSuccess.None && Learned(i)) : Learned(i)) continue;
                if (t.VrOnly && !presenting) continue;
                if (!Signals.Fired(t)) continue;
                if (p1 && e.Seen) e.Seen = false;   // a P1 tip heard on an earlier launch may help once more (not learned by doing)
                _runner.Trigger(e);
            }
        }

        // ---------------- ITipPresenter (the TipRunner asks; for every tip) ----------------

        public bool Owns(TipEntry e) => IndexOf(e) >= 0;

        /// <summary>Hide Sunny stops every tip for the launch; "Tips while I play" off keeps P2 / P3 quiet (P1 still helps);
        /// the tour's own tips run only where the app runs them (the start panel; tests opt in).</summary>
        public bool Allowed(TipEntry e)
        {
            if (e == null || HiddenThisLaunch) return false;
            if (e.Spec.Class != TipClass.P1 && !TipsEnabled) return false;
            return !Owns(e) || Enabled;
        }

        public bool Quiet(TipEntry e)
        {
            int i = IndexOf(e);
            return i >= 0 ? QuietFor(TourTips.All[i]) : QuietForOther(e.Spec);
        }

        public bool FreeFor(TipEntry e)
        {
            int i = IndexOf(e);
            if (i < 0 || !_free()) return false;
            TourTip t = TourTips.All[i];
            return !Signals.Arranging || t.InArrange || t.Trigger == TourTrigger.ArrangeOn;
        }

        public float Show(TipEntry e)
        {
            int i = IndexOf(e);
            if (i < 0) return 0f;
            Show(i, fromList: false);
            return Seconds(TourTips.All[i]);
        }

        public bool IsShowing(TipEntry e) => _cur != null && IndexOf(e) == _curIndex;

        // About how long a tip takes (IGuide.Tip's answer: the looper holds its count-in that long).
        static float Seconds(TourTip t)
        {
            if (t.TextOnly != null) return GuideTiming.HintT;
            float s = 0f;
            foreach (string l in t.Lines) s += Mathf.Max(0f, GuideLines.SecondsOf(l)) + GuideTiming.Lead;
            if (t.Demo != DemoKind.None && t.Success == TourSuccess.None) s = Mathf.Max(s, GuideTiming.DemoLoop + 0.5f);
            return s + 0.5f;
        }

        bool QuietFor(TourTip t)
        {
            bool menuTip = t.Trigger == TourTrigger.Screen || t.Trigger == TourTrigger.SubScreen || t.Trigger == TourTrigger.ListScrolls
                || t.Trigger == TourTrigger.RowConfirming || t.Trigger == TourTrigger.MenuOpenNoPoke || t.Trigger == TourTrigger.MenuOpenLong
                || t.Trigger == TourTrigger.LessonDone || t.Trigger == TourTrigger.PalmOpenings || t.Trigger == TourTrigger.OptionsChip;
            bool lessonTip = t.Trigger == TourTrigger.LessonStarted || t.Trigger == TourTrigger.DrumLessonKick || t.Trigger == TourTrigger.HarpLessonStarted
                || t.Trigger == TourTrigger.LessonDone;
            bool deckTip = t.Anchor >= TourAnchor.DeckRec && t.Anchor <= TourAnchor.DeckGuide || t.Trigger == TourTrigger.LoopSaved;
            bool arrangeTip = t.InArrange || t.Trigger == TourTrigger.ArrangeOn;
            return QuietMoment(menuTip, lessonTip, deckTip, arrangeTip, t.Trigger == TourTrigger.RecordingStarted, t.Trigger == TourTrigger.MrPlacing);
        }

        // The platform's and the instruments' tips keep the same quiet moments: a menu signal's tip is a menu tip, a lesson
        // trigger or success a lesson tip.
        bool QuietForOther(TipSpec t)
        {
            bool menuTip = t.Trigger == TipTrigger.Signal && t.TriggerArg != null && t.TriggerArg.StartsWith("menu.", StringComparison.Ordinal);
            bool lessonTip = t.Trigger == TipTrigger.LessonStart || t.Success == TipSuccess.LessonHit;
            return QuietMoment(menuTip, lessonTip, false, false, false, false);
        }

        bool QuietMoment(bool menuTip, bool lessonTip, bool deckTip, bool arrangeTip, bool recordingTip, bool placingTip)
        {
            TourSignals s = Signals;
            float now = s.Time;
            if (now - s.LastLivePlay < QuietPlay && !lessonTip && !recordingTip) return false;
            if (s.AnyHeld && !arrangeTip) return false;
            if (s.Arranging && !arrangeTip) return false;
            if (now - s.LastMenuPoke < (menuTip ? ScreenPokeQuiet : MenuPokeQuiet)) return false;
            ITutorial tut = _app.Get<ITutorial>();
            bool lesson = tut != null && (tut.State == TutorialState.Countdown || tut.State == TutorialState.Playing || tut.State == TutorialState.Waiting);
            if (lesson && !lessonTip) return false;
            ILooper looper = _app.Get<ILooper>();
            if (looper != null)
            {
                LoopStatus ls = looper.Status;
                if (ls == LoopStatus.Waiting || ls == LoopStatus.RecordingFirst || ls == LoopStatus.Overdubbing) return false;
                if (ls != LoopStatus.Off && !deckTip) return false;
            }
            IRecorder rec = _app.Get<IRecorder>();
            if (rec != null && rec.State == RecorderState.Playing && !deckTip) return false;
            IMixedReality mr = _app.Get<IMixedReality>();
            if (mr != null && mr.IsPlacing && !placingTip) return false;
            return true;
        }

        /// <summary>The Tips list: Sunny comes and shows this tip now (a tip in progress gives way).</summary>
        public bool Play(string id)
        {
            int i = TourTips.IndexOf(id);
            if (i < 0) return false;
            if (_cur != null) Finish(false);
            Show(i, fromList: true);
            return true;
        }

        void Show(int i, bool fromList)
        {
            TourTip t = TourTips.All[i];
            _cur = t;
            _curIndex = i;
            _fromList = fromList;
            _line = 0;
            _curT = _idle = _wait = 0f;
            _waiting = _succeeded = _praised = _redemoed = _leaving = false;
            if (_entries[i] != null) _entries[i].PendingUntil = -1f;
            _shownLaunch[i] = true;
            _appendCount = 0;
            if (!fromList)
            {
                int ever = _app.Settings.GetInt(KeyTipsShown, 0) + 1;
                _app.Settings.SetInt(KeyTipsShown, ever);
                if (ever == 1 && !_seen(TourTips.PokeSunny)) _append[_appendCount++] = TourTips.PokeSunny;
                if (ever == HideAfter && !_seen(TourTips.HideSunny)) _append[_appendCount++] = TourTips.HideSunny;
            }
            Shown++;
            _sunny.BeginScript();
            _sunny.Owner = this;
            _sunny.Perch = SunnyPerch.Clear(_app, _sunny.HeadSpot());
            _app.Events.Publish(new GuideTipEvent { TipId = t.Id, Started = true });
            Debug.Log($"[Guide] tip {t.Id}{(fromList ? " (from the list)" : "")}");
        }

        // The tip in progress: its lines, its demo, its success wait, the praise, the quiet leave.
        void Drive(float dt)
        {
            if (_sunny.State != SunnyState.Script || !ReferenceEquals(_sunny.Owner, this))
            {
                // Hidden (Hide Sunny: no more tips this launch) or taken over: cut short, so unseen.
                if (_sunny.State == SunnyState.Leave || _sunny.State == SunnyState.Gone) HiddenThisLaunch |= !_leaving;
                _cur = null;
                return;
            }
            _curT += dt;
            _sunny.Perch = SunnyPerch.Clear(_app, _sunny.HeadSpot());
            ShowDemo();
            if (_leaving) return;
            if (!_sunny.Quiet) return;
            TourTip t = _cur;
            int lines = t.TextOnly != null ? 1 : t.Lines.Length;
            if (_line < lines)
            {
                if (t.TextOnly != null) _sunny.SayScript(t.TextOnly);
                else _sunny.SayScript(t.Lines[_line], SunnyMood.Happy);
                _line++;
                return;
            }
            // Every line was heard to the end: learned (a P1 tip only by doing it).
            if (t.Class != TourTipClass.P1 || t.Success == TourSuccess.None) Learn(_curIndex);
            if (_appendCount > 0)
            {
                string next = _append[0];
                _append[0] = _append[1];
                _appendCount--;
                TourTip a = TourTips.Get(next);
                if (a != null) { _sunny.SayScript(a.Lines[0], SunnyMood.Calm); int ai = TourTips.IndexOf(next); if (ai >= 0) Learn(ai); }
                return;
            }
            if (t.Success != TourSuccess.None && !_succeeded && HasContext(t))
            {
                _wait += dt;
                _idle += dt;
                if (!_redemoed && _idle >= Redemo)
                {
                    _redemoed = true;
                    _sunny.SayScript(TourTips.AgainOf(t));
                    _sunny.RestartDemo();
                }
                if (_wait < SuccessWait) return;
            }
            if (_succeeded && !_praised)
            {
                _praised = true;
                _sunny.StopDemo();
                _sunny.SayScript(GuideLines.Praise[_curIndex % GuideLines.Praise.Length], SunnyMood.Happy);
                return;
            }
            // A demo tip with nothing to wait for shows its demo at least one loop.
            if (t.Demo != DemoKind.None && t.Success == TourSuccess.None && _curT < GuideTiming.DemoLoop + 0.5f) return;
            Finish(true);
        }

        // A tip whose context is gone (no kit, no harp) does not wait for its success.
        bool HasContext(TourTip t)
        {
            switch (t.Anchor)
            {
                case TourAnchor.Snare: case TourAnchor.Stick: case TourAnchor.HatStand: case TourAnchor.RingingCymbal:
                    return _app.Get<IDrumsApi>()?.Root != null && InScene(InstrumentKind.Drums);
                case TourAnchor.Strings: case TourAnchor.StringsMid:
                    return _app.Get<IHarpApi>()?.Root != null && InScene(InstrumentKind.Harp);
            }
            return true;
        }

        bool InScene(InstrumentKind k)
        {
            ILayoutService l = _app.Get<ILayoutService>();
            return l == null || l.IsInScene(k);
        }

        void Finish(bool heard)
        {
            if (_cur == null) return;
            if (heard && (_cur.Class != TourTipClass.P1 || _cur.Success == TourSuccess.None)) Learn(_curIndex);
            _app.Events.Publish(new GuideTipEvent { TipId = _cur.Id, Started = false, Seen = _seen(_cur.Id) });
            if (_sunny.State == SunnyState.Script && ReferenceEquals(_sunny.Owner, this))
            {
                _leaving = true;
                _sunny.EndScript(false);   // silently: no "Bye!"
            }
            _cur = null;
        }

        /// <summary>A poke on Sunny during a tip: learned, and Sunny leaves at once.</summary>
        public void Skip()
        {
            if (_cur == null) return;
            _sunny.CutLine();
            Learn(_curIndex);
            _app.Events.Publish(new SunnyPokeEvent { StepOrTipId = _cur.Id });
            Finish(false);
        }

        /// <summary>Hide Sunny or another script: the tip stops (unseen) and, for Hide, no tips for the rest of the launch.</summary>
        public void Stop(bool hideForLaunch)
        {
            if (hideForLaunch) HiddenThisLaunch = true;
            if (_cur == null) return;
            if (_sunny.State == SunnyState.Script && ReferenceEquals(_sunny.Owner, this)) _sunny.EndScript(false);
            _cur = null;
        }

        // ---------------- the demo's anchor ----------------

        void ShowDemo()
        {
            TourTip t = _cur;
            if (t == null || t.Demo == DemoKind.None || _praised) { if (_sunny.DemoKind != DemoKind.None) _sunny.StopDemo(); return; }
            DemoAnchor a = Resolve(t);
            Anchor = a;
            if (SunnyDemoWorld.IsWorld(t.Demo) && !a.Has && t.Anchor != TourAnchor.Sunny)
            {
                // The target is not here (a tip from the list): the hand shows it beside Sunny.
                _sunny.ShowDemo(t.Demo == DemoKind.Press ? DemoKind.Poke : t.Demo, default);
                return;
            }
            _sunny.ShowDemo(t.Demo, a);
        }

        DemoAnchor Resolve(TourTip t)
        {
            switch (t.Anchor)
            {
                case TourAnchor.MiddleC:
                {
                    IPianoApi p = _app.Get<IPianoApi>();
                    if (p?.Root != null && p.TryGetKey(60, out PianoKeyInfo k)) return DemoAnchor.At(p.Root.TransformPoint(k.X, 0f, 0.02f), p.Root.up);
                    return default;
                }
                case TourAnchor.LessonTarget:
                {
                    ILessonLive live = _app.Get<ILessonLive>();
                    if (live != null && live.TryGetLiveTarget(out Vector3 w, out Vector3 up, out _, out _)) return DemoAnchor.At(w, up);
                    return default;
                }
                case TourAnchor.Snare: return Piece("snare", 0.35f, 0f);
                case TourAnchor.Stick: return Piece("snare", 0.6f, 0.01f);
                case TourAnchor.HatStand: { DemoAnchor a = Piece("hat", 0f, 0f); if (a.Has) a.Point -= a.Up * 0.2f; return a; }
                case TourAnchor.RingingCymbal: { DemoAnchor a = Piece("crash", 0.8f, 0.01f); return a.Has ? a : Piece("ride", 0.8f, 0.01f); }
                case TourAnchor.Strings: return Strings(8, 13);
                case TourAnchor.StringsMid: return Strings(7, 11);
                case TourAnchor.SwitchFire:
                    return Signals.TryGetSwitch(out Vector3 sw, out Vector3 swUp) ? DemoAnchor.At(sw, swUp) : default;
                case TourAnchor.DeckRec: return Deck(LoopControl.Rec);
                case TourAnchor.DeckStop: return Deck(LoopControl.Stop);
                case TourAnchor.DeckPlay: return Deck(LoopControl.Play);
                case TourAnchor.DeckLoop: return Deck(LoopControl.Loop);
                case TourAnchor.DeckLayer1: return Deck(LoopControl.Layer);
                case TourAnchor.DeckUndo: return Deck(LoopControl.Undo);
                case TourAnchor.DeckClear: return Deck(LoopControl.Clear);
                case TourAnchor.DeckMedium: return Deck(LoopControl.Medium);
                case TourAnchor.DeckClick: return Deck(LoopControl.Click);
                case TourAnchor.DeckTidy: return Deck(LoopControl.Tidy);
                case TourAnchor.DeckGuide: return Deck(LoopControl.Guide);
                case TourAnchor.Row: return Row(t.AnchorArg);
                case TourAnchor.Header: return Row("back");
                case TourAnchor.List:
                {
                    IMenuGuideHooks h = _app.Get<IMenuGuideHooks>();
                    IMenuPanel m = _app.Get<IMenuPanel>();
                    if (h != null && h.TryGetRowRect("list", out Vector3 c, out Vector3 n, out _)) return DemoAnchor.At(c, n, m?.Root != null ? m.Root.up : Vector3.up);
                    return default;
                }
                case TourAnchor.NearestInstrument: return Instrument(nearest: true);
                case TourAnchor.FarthestInstrument: return Instrument(nearest: false);
                case TourAnchor.HeldRig:
                {
                    Rig r = _app.Get<IGrabService>()?.HeldRig;
                    if (r?.Group == null) return default;
                    return DemoAnchor.At(r.Group.position + Vector3.up * (r.Id == RigId.Piano ? 0.05f : 0.8f), Vector3.up);
                }
                case TourAnchor.PanelEdge:
                {
                    IMenuPanel m = _app.Get<IMenuPanel>();
                    if (m?.Root == null || !m.Shown) return default;
                    return DemoAnchor.At(m.Root.position - m.Root.right * 0.165f, -m.Root.forward);
                }                case TourAnchor.DisplayedInstrument:
                    return TryNearestDisplayed(_app, out Vector3 shown) ? DemoAnchor.At(shown, Vector3.up) : default;
            }
            return default;
        }

        /// <summary>The nearest instrument on display in the room (its pick point, ILayoutService.TryGetDisplayPoint): where
        /// the ghost hand points for "point at one and pinch" (the closing invitation, tip_more_instruments).</summary>
        public static bool TryNearestDisplayed(App app, out Vector3 point)
        {
            point = default;
            ILayoutService layout = app.Get<ILayoutService>();
            IInstrumentRegistry reg = app.Registry;
            if (layout == null || reg == null) return false;
            Vector3 eye = app.Head != null ? app.Head.position : Vector3.up;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < reg.All.Count; i++)
            {
                if (!layout.TryGetDisplayPoint(reg.All[i].Kind, out Vector3 p)) continue;
                float d = (p - eye).sqrMagnitude;
                if (d < best) { best = d; point = p; found = true; }
            }
            return found;
        }

        DemoAnchor Piece(string kind, float towardR, float lift)
        {
            IDrumsApi d = _app.Get<IDrumsApi>();
            if (d == null) return default;
            var pieces = d.Pieces;
            for (int i = 0; i < pieces.Count; i++)
            {
                DrumPieceInfo p = pieces[i];
                if (p.Kind != kind || p.Pad || p.Transform == null) continue;
                Vector3 eye = _app.Head != null ? _app.Head.position : p.Transform.position + Vector3.up;
                Vector3 toward = Vector3.ProjectOnPlane(eye - p.Transform.position, p.Transform.up);
                Vector3 at = p.Transform.position + p.Transform.up * lift + (toward.sqrMagnitude > 1e-6f ? toward.normalized * (p.Radius * towardR) : Vector3.zero);
                return DemoAnchor.At(at, p.Transform.up);
            }
            return default;
        }

        DemoAnchor Strings(int from, int to)
        {
            IHarpApi h = _app.Get<IHarpApi>();
            if (h?.Root == null || !h.Root.gameObject.activeInHierarchy || h.StringCount <= to) return default;
            Vector3 a = h.Root.TransformPoint(h.PlayPoint(from)), b = h.Root.TransformPoint(h.PlayPoint(to));
            return DemoAnchor.At((a + b) * 0.5f, -h.Root.forward, b - a);
        }

        DemoAnchor Deck(LoopControl c)
        {
            ILooper l = _app.Get<ILooper>();
            return l != null && l.TryGetControl(c, 0, out Vector3 w, out Vector3 up) ? DemoAnchor.At(w, up) : default;
        }

        DemoAnchor Row(string id)
        {
            IMenuGuideHooks h = _app.Get<IMenuGuideHooks>();
            return h != null && h.TryGetRowRect(id, out Vector3 c, out Vector3 n, out _) ? DemoAnchor.At(c, n) : default;
        }

        // The nearest (or farthest) instrument in the room, any registry instrument (its rig group), the ghost hand at the
        // top of its footprint (the descriptor's seat boxes).
        DemoAnchor Instrument(bool nearest)
        {
            Transform head = _app.Head;
            IRigRegistry rigs = _app.Get<IRigRegistry>();
            IInstrumentRegistry reg = _app.Registry;
            if (head == null || rigs == null || reg == null) return default;
            Transform best = null;
            float bd = nearest ? float.MaxValue : 0f, top = 0f;
            for (int i = 0; i < reg.All.Count; i++)
            {
                InstrumentDescriptor d = reg.All[i];
                Transform t = rigs.Get(InstrumentIds.RigOf(d.Kind))?.Group;
                if (t == null || !t.gameObject.activeInHierarchy || !InScene(d.Kind)) continue;
                float dist = new Vector2(t.position.x - head.position.x, t.position.z - head.position.z).magnitude;
                if (nearest ? dist >= bd : dist <= bd) continue;
                bd = dist;
                best = t;
                top = TopOf(d);
            }
            return best == null ? default : DemoAnchor.At(best.position + Vector3.up * top, Vector3.up);
        }

        static float TopOf(InstrumentDescriptor d)
        {
            float top = 0f;
            FootBox[] boxes = d.Seat?.Boxes;
            if (boxes != null) foreach (FootBox b in boxes) if (!b.Air) top = Mathf.Max(top, b.Y1);
            return top > 0.05f ? Mathf.Min(top, 1.3f) : 0.85f;
        }

        // ---------------- the Tips list ----------------

        /// <summary>The tips the list shows, in order (the switched-off ones are left out).</summary>
        public static int ListCount { get { int n = 0; foreach (TourTip t in TourTips.All) if (t.Trigger != TourTrigger.Off) n++; return n; } }

        public static TourTip ListTip(int i)
        {
            int n = 0;
            foreach (TourTip t in TourTips.All)
            {
                if (t.Trigger == TourTrigger.Off) continue;
                if (n++ == i) return t;
            }
            return null;
        }
    }
}
