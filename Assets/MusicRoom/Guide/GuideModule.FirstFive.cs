// The Guide module's first-five-minutes wiring (Docs/11-first-five): the first-run script under the start panel
// (FirstRun), the tour's contextual tips (TourTipRunner: their triggers and successes, and their presenter for the
// platform's TipRunner, the one scheduler of every tip), poking Sunny to skip (SunnyPoke) whatever Sunny is doing (the
// first run, a tip, the deck walkthrough, a one-line tip), "Show me around" as the replay of the first run, and the menu's
// Sunny screen (IGuideTour: the tour, the Tips list of EVERY tip, Tips while I play). GuideModule.cs calls in at a few
// places (FIRSTFIVE-NOTES.md). ClassicTour switches all of it off and keeps the classic five-step tour (its own tests).
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Guide
{
    public sealed partial class GuideModule : IGuideTour
    {
        /// <summary>The classic five-step tour (open, poke, close, swipe, nod) instead of the first five: tests of that
        /// tour's machinery set it; the app never does.</summary>
        public static bool ClassicTour;

        public FirstRun FirstRun { get; private set; }
        /// <summary>The tour's tips: their signals, triggers and successes, and their presenter (Tips is the scheduler).</summary>
        public TourTipRunner TourTips { get; private set; }
        public SunnyPoke Poke { get; private set; }
        /// <summary>The Tips list (the menu's Sunny screen): the tour's tips in their order, then every other tip of the
        /// runner (the platform's, each registry instrument's, the sheet music's).</summary>
        readonly System.Collections.Generic.List<TipEntry> _list = new System.Collections.Generic.List<TipEntry>();

        void InitFirstFive(App app)
        {
            FirstRun = new FirstRun(app, Sunny, MarkTipSeenQuiet);
            TourTips = new TourTipRunner(app, Sunny, TipSeen, MarkTipSeenQuiet, Tips, () => SunnyFreeAnyMode);
            if (!ClassicTour) Tips.Presenter = TourTips;   // one scheduler: the runner picks, the tour's tips are shown by TourTips
            FirstRun.SunnyDrivesMenu = on => TourTips.Signals.MenuBySunny = on;
            Poke = new SunnyPoke();
            BuildTipList();
            app.Register<IGuideTour>(this);
        }

        // Sunny free for a tip whether or not the instruments are being moved (the arrange tips come during it).
        bool SunnyFreeAnyMode => Sunny != null && (Sunny.State == SunnyState.Idle || Sunny.State == SunnyState.Gone)
            && !(DeckGuide != null && DeckGuide.Running) && !_heldForArrange && !(FirstRun != null && FirstRun.Running);

        /// <summary>IGuide.Tip with a tip id: the runner's (Manual), except the tour's own where the tour's tips do not run
        /// (no start panel: the legacy one-line tip, as before the merge).</summary>
        bool ManualByRunner(TipEntry e) => ClassicTour ? !TourTips.Owns(e) : !TourTips.Owns(e) || TourTips.Enabled;

        void BuildTipList()
        {
            _list.Clear();
            for (int i = 0; i < global::MusicRoom.Guide.TourTips.All.Length; i++)
            {
                TourTip t = global::MusicRoom.Guide.TourTips.All[i];
                TipEntry e = TourTips.Entry(i);
                if (e != null && t.Trigger != TourTrigger.Off) _list.Add(e);
            }
            foreach (TipEntry e in Tips.Entries) if (!TourTips.Owns(e)) _list.Add(e);
        }

        void ShutdownFirstFive()
        {
            FirstRun?.Dispose();
            TourTips?.Dispose();
            if (_tipsDirty && _app != null) { _tipsDirty = false; _app.Settings.Save(); }
        }

        bool _tipsDirty;

        /// <summary>A tip learned during play: written to the store at once, saved (PlayerPrefs.Save, a file write) only at
        /// a quiet moment, never in the frame the player just played (a hitch on the headset).</summary>
        void MarkTipSeenQuiet(string id)
        {
            if (string.IsNullOrEmpty(id) || TipSeen(id) || _app == null) return;
            string seen = _app.Settings.GetString(KeyTipsSeen, "");
            _app.Settings.SetString(KeyTipsSeen, string.IsNullOrEmpty(seen) ? id : seen + "," + id);
            SeenAdded(id);
            _tipsDirty = true;
        }

        void FlushTipsSeen()
        {
            if (!_tipsDirty || TourTips == null) return;
            if (TourTips.Signals.Time - TourTips.Signals.LastLivePlay < TourTipRunner.QuietPlay) return;
            _tipsDirty = false;
            _app.Settings.Save();
        }

        /// <summary>Tips come from the tip runner (the legacy one-line tip_arrange path stays for ClassicTour boots).</summary>
        bool TipsByRunner => !ClassicTour && TourTips != null && TourTips.Enabled;
        bool FirstFiveTipShowing => !ClassicTour && TourTips != null && TourTips.Showing;

        // The first launch: the first run starts under the start panel (S0 waits silently; Sunny comes at 8 s). Without the
        // start panel (TestBoot's default) there is no first run, and the tips start at once.
        void AutoStartFirstFive()
        {
            if (_autoStarted) return;
            bool returning = _tourDoneAtBoot || _app.Settings.GetInt(FirstRun.KeyTourVersion, 0) >= FirstRun.TourVersion;
            if (returning || _tourStarted || !_app.StartPanel)
            {
                _autoStarted = true;
                TourTips.TourEnded();
                return;
            }
            ILayoutService layout = _world.Layout;
            bool arranged = (layout != null && layout.FirstArrangeDone) || _app.Rig.State == XrState.Desktop;
            if (!arranged || !_app.Rig.HmdMounted) return;
            _autoStarted = true;
            FirstRun.StartFirstLaunch();
        }

        // The frame (before the deck walkthrough and Sunny.Update): the poke, the first run, the tips.
        void TickFirstFive(float dt)
        {
            if (Poke.Update(_app, Sunny, View.Bubble, dt, out Vector3 dir) || Poke.Click(_app, Sunny)) Poked(dir);
            bool was = FirstRun.Running;
            FirstRun.Update(dt);
            if (was && !FirstRun.Running)
            {
                TourTips.TourEnded();
                Tips.DropPending();   // what the tour's own menu showed (the Instruments grid's signals) is not the player's moment
            }
            TourTips.Tick(dt, FirstRun.Running);   // the runner (Tips.Tick, after this) decides when the next tip comes
            FlushTipsSeen();
        }

        // A poke skips what Sunny is doing; Sunny feels it in every case (a push, a blink, a soft bop).
        void Poked(Vector3 dir)
        {
            Sunny.Poked(dir);
            if (FirstRun.Running && ReferenceEquals(Sunny.Owner, FirstRun)) FirstRun.Skip();
            else if (TourTips.Showing) TourTips.Skip();
            else if (DeckGuide.Running) DeckGuide.Skip();
            else if (Sunny.InTip)
            {
                if (_tip != null) MarkTipSeen(_tip);
                Sunny.StopVoice();
                Sunny.Dismiss();
            }
            else if (Sunny.Present && Sunny.State != SunnyState.Script) Sunny.Skip();
            Sync();
        }

        void StartFirstFive()
        {
            _tourStarted = true;
            if (_heldForArrange)
            {
                _heldForArrange = false;
                if (View.Root != null) View.Root.gameObject.SetActive(true);
            }
            if (DeckGuide.Running) DeckGuide.End();
            TourTips.Stop(false);
            FirstRun.StartReplay();
            Debug.Log("[Guide] tour replay started");
            Sync();
        }

        /// <summary>Hide Sunny during the first run or a tip: the run ends (the first launch counts as done), tips stop for
        /// the rest of the launch. False when neither was running.</summary>
        bool DismissFirstFive()
        {
            if (ClassicTour) return false;
            if (FirstRun.Running)
            {
                FirstRun.End(true);
                TourTips.TourEnded();
                return true;
            }
            if (TourTips.Showing)
            {
                TourTips.Stop(true);
                return true;
            }
            if (Sunny.InTip) TourTips.Stop(true);   // hidden during the platform's or an instrument's tip: no more tips this launch
            return false;
        }

        /// <summary>The deck's GUIDE takes Sunny: the first run (it is a mode the player chose) and a tip give way.</summary>
        void YieldToDeckGuide()
        {
            if (ClassicTour) return;
            if (FirstRun.Running) { FirstRun.End(false); TourTips.TourEnded(); }
            TourTips.Stop(false);
        }

        // ---------------- the tips heard, cached (musicroom.tipsSeen, read once) ----------------

        System.Collections.Generic.HashSet<string> _seenSet;

        bool SeenCached(string id)
        {
            if (_app == null || string.IsNullOrEmpty(id)) return false;
            if (_seenSet == null) LoadSeen();
            return _seenSet.Contains(id);
        }

        void LoadSeen()
        {
            _seenSet = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            string seen = _app.Settings.GetString(KeyTipsSeen, "");
            if (!string.IsNullOrEmpty(seen)) foreach (string s in seen.Split(',')) if (s.Length > 0) _seenSet.Add(s);
        }

        void SeenAdded(string id)
        {
            if (_seenSet == null) LoadSeen();
            _seenSet.Add(id);
        }

        /// <summary>Tests: read musicroom.tipsSeen again (after writing the store directly).</summary>
        public void ReloadSeen() => _seenSet = null;

        // ---------------- IGuideTour: the menu's Sunny screen ----------------

        public void StartTour() => Start();
        public int TipCount => _list.Count;
        public string TipId(int i) => i >= 0 && i < _list.Count ? _list[i].Spec.Id : null;
        public string TipTitle(int i) => i >= 0 && i < _list.Count ? _list[i].Spec.Title : null;
        public bool TipHeard(int i) => i >= 0 && i < _list.Count && TipSeen(_list[i].Spec.Id);

        public int TipsHeard
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _list.Count; i++) if (TipSeen(_list[i].Spec.Id)) n++;
                return n;
            }
        }

        /// <summary>The Tips list: Sunny comes and plays this tip now, heard or not (the tour's through its presenter, any
        /// other through the runner).</summary>
        public void PlayTip(string tipId)
        {
            if (TourTips == null || string.IsNullOrEmpty(tipId)) return;
            if (FirstRun.Running) return;
            if (DeckGuide.Running) DeckGuide.End();
            TipEntry e = Tips.Find(tipId);
            if (TourTips.Owns(e) || e == null)
            {
                if (Sunny.InTip || Sunny.Hinting) Sunny.Dismiss();
                TourTips.Play(tipId);
            }
            else
            {
                TourTips.Stop(false);
                if (Sunny.Present) Sunny.Dismiss();
                Tips.PlayNow(e);
            }
            Sync();
        }

        public bool TipsEnabled
        {
            get => TourTips == null || TourTips.TipsEnabled;
            set { if (TourTips != null) TourTips.TipsEnabled = value; }
        }
    }
}
