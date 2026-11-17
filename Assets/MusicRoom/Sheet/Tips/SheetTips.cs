// Sunny covers the book (sheet.md 8; the memory rule "Sunny covers every feature"): five tips, each one line of at most 12
// words rendered with the project's eSpeak pipeline (Resources/Voice/tip_book*.wav,
// VoiceManifests/Sheet.json, GuideLines/Sheet.json). The three that name a control have a desktop wording ("Click" for
// "Tap"). Every feature of the book is covered by a tip or passive with a reason (SheetSunnyTests).
// Triggers are the book's own conditions, evaluated here once a frame (allocation-free); a tip is offered through
// ISheetHost.Tip (IGuide.Tip) and, if Sunny is busy, offered again no sooner than 20 s later. Phase B registers the same
// table as an IGuideTipSource (TipSpec with TipTrigger.Manual) and the platform's coverage tests run over it.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Sheet
{
    public sealed class SheetTip
    {
        public string Id, Title, Line, DeskLine, Anchor, Covers, Success;
        public int Class;
    }

    public static class SheetTipTable
    {
        public static readonly SheetTip[] Tips =
        {
            new SheetTip { Id = "tip_book", Title = "Your songbook", Line = "tip_book", Class = 3, Anchor = "sheet.letter.first", Covers = "sheet.book", Success = "none" },
            new SheetTip { Id = "tip_book_marker", Title = "The warm mark", Line = "tip_book_marker", Class = 2, Covers = "sheet.marker", Success = "none" },
            new SheetTip { Id = "tip_book_contents", Title = "Start from the book", Line = "tip_book_contents", DeskLine = "tip_book_contents_desk", Class = 3, Anchor = "sheet.row.1", Covers = "sheet.contents", Success = "sheet.title" },
            new SheetTip { Id = "tip_book_turn", Title = "Book pages", Line = "tip_book_turn", DeskLine = "tip_book_turn_desk", Class = 3, Anchor = "sheet.corner.right", Covers = "sheet.turn", Success = "sheet.turn" },
            new SheetTip { Id = "tip_book_ribbon", Title = "Back to the list", Line = "tip_book_ribbon", DeskLine = "tip_book_ribbon_desk", Class = 3, Anchor = "sheet.ribbon", Covers = "sheet.ribbon", Success = "sheet.ribbon" },
        };

        /// <summary>The book's features (FeatureCatalog ids) and, for passive ones, why nothing needs teaching.</summary>
        public static readonly (string id, string passive)[] Features =
        {
            ("sheet.book", null), ("sheet.marker", null), ("sheet.contents", null), ("sheet.turn", null), ("sheet.ribbon", null),
            ("sheet.autoturn", "the book turns itself to the lesson; nothing to learn"),
            ("sheet.lamp", "a reading lamp that lights the pages; nothing to do"),
        };

        public static readonly string[] Anchors = { "sheet.corner.right", "sheet.corner.left", "sheet.row.1", "sheet.ribbon", "sheet.letter.first" };
    }

    public sealed class SheetTips
    {
        public const string KeyTurned = "musicroom.sheet.turned", KeyTitle = "musicroom.sheet.titleStarted", KeyRibbon = "musicroom.sheet.ribbon";
        public const float GazeCos = 0.9397f;            // 20 degrees
        public const float BookSeenFor = 1.5f, ContentsSeenFor = 2f, Retry = 20f;

        readonly ISheetHost _host;
        readonly AppSettings _settings;
        readonly float[] _retryAt = new float[SheetTipTable.Tips.Length];
        readonly bool[] _offered = new bool[SheetTipTable.Tips.Length];
        float _time, _seen, _contentsSeen;
        bool _lessonDoneSeen, _countdownSeen;
        public int Offers { get; private set; }
        public string LastOffered { get; private set; }

        public SheetTips(ISheetHost host, AppSettings settings) { _host = host; _settings = settings; }

        public bool TurnedByHand => _settings.GetBool(KeyTurned, false);
        public bool StartedFromTitle => _settings.GetBool(KeyTitle, false);
        public void NoteManualTurn() => _settings.SetBool(KeyTurned, true);
        public void NoteTitleStart() => _settings.SetBool(KeyTitle, true);
        public void NoteRibbon() => _settings.SetBool(KeyRibbon, true);

        /// <summary>One frame. inView: the open book within 20 degrees of the gaze; tourDone: Sunny's tour is over;
        /// contentsOpen; lesson: a piano lesson of the book running (state); manualTurnsPast: the open spread is 2+ spreads past
        /// the contents after a manual turn; desktop: say the "Click" lines.</summary>
        public void Tick(float dt, bool visible, bool inView, bool tourDone, bool contentsOpen, TutorialState lessonState, bool bookLesson, bool farAfterManualTurn, bool desktop)
        {
            _time += dt;
            if (!visible || !tourDone) { _seen = 0f; _contentsSeen = 0f; return; }
            bool running = lessonState == TutorialState.Countdown || lessonState == TutorialState.Playing || lessonState == TutorialState.Waiting;
            _seen = inView && !running ? _seen + dt : 0f;
            _contentsSeen = inView && contentsOpen && !running ? _contentsSeen + dt : 0f;
            // tip_book: the open book in view for 1.5 s, no lesson running
            if (_seen >= BookSeenFor) Offer(0, desktop);
            // tip_book_marker: the first lesson countdown of the book with it in view
            if (bookLesson && lessonState == TutorialState.Countdown && inView && !_countdownSeen) { if (Offer(1, desktop)) _countdownSeen = true; }
            // tip_book_contents: tip_book offered, the contents open and in view 2 s, no lesson ever started from the book
            if (_offered[0] && _contentsSeen >= ContentsSeenFor && !StartedFromTitle) Offer(2, desktop);
            // tip_book_turn: a lesson of the book done with the book in view, no manual turn yet
            if (bookLesson && lessonState == TutorialState.Done && inView && !_lessonDoneSeen && !TurnedByHand) { if (Offer(3, desktop)) _lessonDoneSeen = true; }
            // tip_book_ribbon: two or more spreads past the contents after a manual turn
            if (farAfterManualTurn && !_settings.GetBool(KeyRibbon, false)) Offer(4, desktop);
        }

        bool Offer(int i, bool desktop)
        {
            if (_offered[i] || _time < _retryAt[i]) return false;
            var tip = SheetTipTable.Tips[i];
            string line = desktop && tip.DeskLine != null ? tip.DeskLine : tip.Line;
            float s = _host.Tip(line);
            Offers++;
            LastOffered = line;
            if (s > 0f) { _offered[i] = true; return true; }
            _retryAt[i] = _time + Retry;
            return false;
        }

        public bool Offered(int i) => _offered[i];
    }
}
