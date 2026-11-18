// Every number and string of the panel's canvas (spec 08 section 1; 08-ux-visuals.json `panel`),
// in canvas px with the origin top-left and y down. Colours are sRGB values as raw numbers: the canvas is
// painted and blended in sRGB (gamma space), and MusicRoom/MenuFace converts to linear when it is drawn in the room.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Menu
{
    public static class MenuStyle
    {
        // ---- canvas geometry ----
        public const float CW = MenuConstants.CanvasW, CH = MenuConstants.CanvasH;
        public const float PxPerMetre = CW / MenuConstants.Width;            // 3200 px per metre
        public const float Corner = 0.02f * PxPerMetre;                      // 64 px: the face's 2 cm corner radius
        public const float Header = 110f;
        public const float HeaderTextY = Header / 2f + 2f;                   // 57: the title / note middle line
        public const float BarY = 114f, BarH = 6f;
        public const float RowX0 = 40f, RowX1 = CW - 40f;
        public const float FooterY = 724f;

        // Row bands (home with the Room row: ARCHITECTURE.md section 9).
        public const float BigTop = 132f, BigH = 168f, BigGap = 16f;         // lessons rows
        public const float HomeTop = 132f;
        public const float Home4H = 128f, Home4Gap = 12f;                    // the four home rows (no MR service)
        public const float Home5H = 100f, Home5Gap = 12f;                    // + "Play in my room"
        public const float Home6H = 84f, Home6Gap = 10f;                     // + "Set up my room"
        // Since the first headset test the home page also has "Move instruments" and "Exit": 6 rows (no MR service), 7, or
        // 8 with "Set up my room". Every set ends near y 684, above the footer.
        public const float Home7H = 72f, Home7Gap = 8f;
        public const float Home8H = 62f, Home8Gap = 8f;
        // A nine-row home page (ends at 684 too). None is built today: "Real hands" moved from the home page to the Instruments
        // screen's bottom row, so the home page has at most 8 rows.
        public const float Home9H = 56f, Home9Gap = 6f;
        public const float MidTop = 130f, MidH = 100f, MidGap = 12f;         // instrument options rows (the instruments rows' bands)

        // The Instruments and Lessons screens' tile grid (platform.md 5.1): two rows of four tiles in carousel order, each
        // 226 x 210 px (71 x 66 mm): a portrait 226 x 150 on top, the name at 34 px and (Lessons only) a status line at 22 px
        // under it.
        public const float GridX0 = 32f, GridPitch = 244f, TileW = 226f, TileH = 210f;
        public static readonly float[] GridRowY = { 128f, 358f };
        public const int GridColumns = 4, GridTiles = 8;
        public const float PortraitH = 150f, TileRadius = 14f, PhotoInset = 7f, PhotoRadius = 9f;
        public const float TileNameY = 172f, TileNameSize = 34f, TileStatusY = 197f, TileStatusSize = 22f;
        /// <summary>The name's line on a tile without a status line (the Instruments grid): the middle of the band under the
        /// portrait.</summary>
        public const float TileNameAloneY = 181f;
        /// <summary>The printed tick box in a member's top-right corner (My set) and the "Options" chip in the top-left.</summary>
        public const float TickSize = 32f, TickInset = 10f, ChipW = 104f, ChipH = 38f, ChipInset = 10f, ChipTextSize = 22f;
        // The bottom row (y 590-700): the segmented pair "My set | One alone" (456 px), then Move (479 x 110, to the right
        // edge). In "Play in my room" the row has Real hands too: Move and Real hands are 228 x 110 each.
        public const float BarTop = 590f, BarBottom = 700f, SegmentW = 228f, ButtonW = 228f, ButtonWAlone = 479f;
        public static readonly float[] BarX0 = { 32f, 260f, 511f, 762f };    // My set, One alone, Move, Real hands
        public const float BarLabelSize = 36f, ButtonLabelSize = 32f, BarNoteSize = 21f, BarRadius = 22f;
        /// <summary>A tile without a portrait: the descriptor's line drawing, this big, centred in the portrait area.</summary>
        public const float FallbackIconSize = 120f;
        public const float ListTop = 130f, ListBottom = 750f, ListPitch = 130f, ListH = 120f, ListMinVisible = 30f;
        public const float StopTop = 640f, StopBottom = 750f;
        public static readonly float[] DoneTop = { 440f, 590f }, DoneBottom = { 560f, 710f };     // replay, courses
        public static readonly float[] TakeTop = { 300f, 430f, 560f }, TakeBottom = { 410f, 540f, 670f }; // play, stop, delete
        public const float ScrollbarX = CW - 26f, ScrollbarW = 10f;
        public const float ProgressRunningY = 170f, ProgressDoneY = 340f, ProgressH = 24f;

        // ---- timing ----
        public const float OpenSeconds = 0.15f;      // OPEN_MS: scale-and-fade
        public const double HoverMs = 120.0;         // HOVER_MS: the row highlight fade
        public const float GlowOut = 0.007f, GlowSize = 0.045f, GlowFrom = -0.005f;   // hover glow

        // ---- palette (08-ux-visuals.json panel.palette) ----
        public static readonly Color Ink = Rgb(243, 234, 216);
        public static readonly Color Dim = Rgb(243, 234, 216, 0.55f);
        public static readonly Color Cyan = Rgb(95, 216, 255);
        public static readonly Color Green = Rgb(93, 255, 138);
        public static readonly Color Red = Rgb(255, 90, 90);
        public static readonly Color Amber = Rgb(255, 196, 106);
        public static readonly Color Rose = Rgb(255, 142, 196);
        public static readonly Color White = new Color(1f, 1f, 1f, 1f);

        public static readonly Color FaceTop = Rgb(32, 37, 52), FaceMid = Rgb(22, 25, 36), FaceBottom = Rgb(14, 15, 22); // alpha 1 in Unity
        public const float FaceMidStop = 0.55f;
        public static readonly Color SheenTop = Rgb(255, 255, 255, 0.07f), SheenBottom = Rgb(255, 255, 255, 0f);
        public const float SheenH = 220f;
        public static readonly Color InnerRim = Rgb(255, 196, 120, 0.28f);
        public static readonly Color BarEnd = Rgb(243, 234, 216, 0.05f);
        public static readonly Color RowFill = Rgb(243, 234, 216, 0.06f);
        public static readonly Color RowOnFill = Rgb(255, 196, 120, 0.10f);
        public static readonly Color RowOnStroke = Rgb(255, 196, 120, 0.55f);
        public static readonly Color HoverRgb = Rgb(95, 216, 255);           // alpha .22k fill, k stroke
        public static readonly Color ScrollTrack = Rgb(243, 234, 216, 0.12f);
        public static readonly Color ProgressTrack = Rgb(243, 234, 216, 0.15f);

        // The tiles are printed cards (platform.md 5.1: calm, printed, no glow): the panel's cream ink is their paper, the
        // panel's dark face colour their ink. The portrait is a photograph printed on the card: the instrument on a warm
        // grey-beige studio sweep (PhotoPaper, the portrait studio's background), inset by a narrow cream border. On the
        // card's own cream the thin instruments washed out (harp strings, cymbal stands; mean luminance about 0.85).
        public static readonly Color Paper = Rgb(243, 234, 216);
        public static readonly Color PhotoPaper = Rgb(186, 175, 156);
        public static readonly Color PaperInk = Rgb(30, 31, 40);
        public static readonly Color PaperDim = Rgb(30, 31, 40, 0.62f);
        public static readonly Color PaperRule = Rgb(30, 31, 40, 0.16f);
        /// <summary>A card at the seat: its paper frame in the panel's amber "on" stroke.</summary>
        public static readonly Color TileOnStroke = Rgb(255, 196, 120, 0.85f);
        /// <summary>The fallback icons' ink on the card (a printed line drawing).</summary>
        public static readonly Color IconInk = Rgb(30, 31, 40, 0.78f);
        /// <summary>The "Options" chip: a dark-ink pill printed on the card.</summary>
        public static readonly Color ChipFill = Rgb(30, 31, 40, 0.86f);

        // Back plate (512 x 384 canvas, 12 px margin keeps the glow unclipped).
        public const int BackW = 512, BackH = 384;
        public const float BackMargin = 12f, BackRadius = 34f, BackGlowBlur = 14f, BackRimInset = 2f, BackRimRadius = 32f, BackRimWidth = 4f;
        public static readonly Color BackFill = Rgb(11, 13, 20);
        public static readonly Color BackGlow = Rgb(255, 190, 110, 0.55f);
        public static readonly Color BackRim = Rgb(255, 196, 120, 0.6f);

        // ---- strings (spec 08 section 1.5; section 8.7: "This device") ----
        public const string HomeTitle = "Music Room";
        public const string HomeFooter = "Show your palm to hide";
        public const string BackPrefix = "‹  ";            // '‹' and two spaces
        public const string Dot = "  ·  ";                // "  ·  "
        public const string LessonsTitle = "Lessons", LessonsNote = "Poke an instrument";
        public const string InstrumentsTitle = "Instruments", RecordingsTitle = "Recordings";
        public const string Courses = "Courses", CoursesScroll = "Courses  ·  drag to scroll";
        public const string FooterSingle = "Sweep a hand sideways to turn the page";
        public const string FooterAll = "Poke one to bring it, or a ticked one to send it back";

        // ---- the Instruments grid (platform.md 5.1) ----
        public const string MySet = "My set", OneAlone = "One alone", On = "On", Off = "Off";
        public const string NameSeparator = ", ";
        public const string OneStays = "One stays with you", NoRoom = "No room: take one out first";
        public const string WentBack = " went back ";
        public const string OptionsChip = "Options", OptionsNote = "Options", OptionsFooter = "Poke a row to change it";
        /// <summary>An options page shows up to this many rows (h 100, gap 12, from y 130, like the instruments rows).</summary>
        public const int MaxOptionRows = 5;
        /// <summary>How long the header names the instrument that made room ("Harp went back to the corner") or says why a
        /// poke did nothing.</summary>
        public const double NoteMs = 2000.0;
        public const string SavingOff = "Saving off", SavingIsOff = "Saving is off", NoRecordings = "No recordings yet";
        public const string SavingOffLine = "This device gives the room no storage, so takes stay on the tape deck only.";
        public const string EmptyLine1 = "Press REC on the tape deck; each take is kept on this device.";
        public const string EmptyLine2 = "Nothing leaves the headset.";
        public const string Recording = "Recording";
        public const string Play = "Play", Playing = "Playing…", Stop = "Stop", Delete = "Delete", DeleteSure = "Delete, sure?";
        /// <summary>A recording's page, under its three rows: "Keep painting" (the notes' painting on the walls after a playback),
        /// its state at the right. Hidden while Delete asks (the footer's line takes its place).</summary>
        public const string KeepPainting = "Keep painting", KeepPaintingOn = "On: until the next song", KeepPaintingOff = "Off: it fades away";
        public const float KeepTop = 682f, KeepBottom = 752f;
        public const string Loading = "Loading…";
        /// <summary>The Play row's note when a press could not play (the file unreadable, nothing in it that plays here): never
        /// a press that does nothing. A press while it loads plays it once loaded.</summary>
        public const string CantOpen = "Can't open this one", CantPlay = "Can't play this one";
        public const string DeleteFooter = "Poke Delete again to remove it from this device";
        /// <summary>The Recordings list: each take's row, and its bin at the right (poke once: "Delete?" in red; poke again
        /// within DeleteConfirmMs: deleted). The row shows the name over the local time.</summary>
        public const string DeleteAsk = "Delete?";
        public const double DeleteConfirmMs = 3000.0;
        public const float TrashW = 168f, TrashGap = 12f, TakeRowX1 = RowX1 - TrashW - TrashGap;
        public const float TakeNameSize = 46f, TakeDateSize = 34f, TakeNameDy = -21f, TakeDateDy = 27f;
        public const string Replay = "Replay", LessonComplete = "Lesson complete!", GetReady = "Get ready";
        public const string ShowMeAround = "Show me around", Sunny = "Sunny", HideSunny = "Hide Sunny", StopTour = "Stop the tour";
        public const string PlayInMyRoom = "Play in my room", Passthrough = "Passthrough", MusicRoomLabel = "Music room", VirtualRoom = "Virtual room";
        public const string NotAvailable = "Not available";
        public const string SetUpMyRoom = "Set up my room", SpaceSetup = "Space Setup";
        /// <summary>The hands in "Play in my room": drawn (default) or the real ones through the passthrough.</summary>
        public const string RealHands = "Real hands", RealHandsOn = "On: your own hands", RealHandsOff = "Off: drawn hands";
        /// <summary>Its state on the Instruments screen's bottom row (a button, like Move).</summary>
        public const string RealHandsOnShort = "On: your own", RealHandsOffShort = "Off: drawn";

        // ---- since the first headset test: Move instruments, Exit, the start panel, the Done / Reset buttons ----
        // "Move instruments" is one setting, On or Off (2026-11-10: it replaced the gold knobs and Lock instruments): the home
        // page's row and the Instruments screen's "Move" button both show and switch it.
        public const string MoveLabel = "Move instruments", MoveNoteOn = "On: pinch one to move it", MoveNoteOff = "Off: they stay put";
        public const string MoveShort = "Move", MoveOn = "On: pinch to move", MoveOff = "Off: they stay put";
        /// <summary>Reset view (2026-11-10): every instrument back in front of the player, as both hands held still in a pinch do.</summary>
        public const string ResetViewLabel = "Reset view", ResetViewNote = "Instruments in front of you";
        public const string ExitLabel = "Exit", ExitNote = "Close the app", ExitConfirm = "Tap again to exit", ExitClosing = "Closing…";
        /// <summary>The first poke on Exit asks; a second one within this long closes the app.</summary>
        public const double ExitConfirmMs = 3000.0;
        /// <summary>The sound fades out over this long before the app closes.</summary>
        public const float QuitFadeSeconds = 0.15f;

        public const string StartTitle = "VR Music Room", StartLine = "Where would you like to play?";
        public const string PlayHere = "Play here";
        public const string StartFooter = "Later: show your palm to open the menu";
        public const float StartTitleY = 120f, StartLineY = 200f, StartTop = 262f, StartRowH = 176f, StartGap = 22f;

        public const string ArrangeTitle = "Move instruments";
        public const string ArrangeLine1 = "Pinch one, or point at it and pinch";
        public const string ArrangeLine2 = "Drag it, turn your wrist to spin it";
        public const string DoneLabel = "Done", ResetLabel = "Reset";
        /// <summary>The Done / Reset panel is the menu's canvas at this scale: 0.16 x 0.12 m.</summary>
        public const float ArrangeScale = 0.5f;
        public const float ArrangeLine1Y = 196f, ArrangeLine2Y = 262f, ArrangeTop = 330f, ArrangeBottom = 724f;
        public static readonly float[] ArrangeX0 = { 40f, 532f }, ArrangeX1 = { 492f, 984f };

        /// <summary>Tile i of the grid (carousel order after unloaded instruments are closed up): canvas px x0, y0.</summary>
        public static void TileOrigin(int i, out float x0, out float y0)
        {
            x0 = GridX0 + (i % GridColumns) * GridPitch;
            y0 = GridRowY[(i / GridColumns) % GridRowY.Length];
        }

        // ---- first five (Docs/11-first-five): the lesson's Next row, finished courses, the Sunny screen and the Tips list ----
        public const string NextPrefix = "Next: ", CourseDone = "Done";
        /// <summary>Done screen with a Next row: Next, Replay, Courses (the two-row layout above stays for the last lesson).</summary>
        public static readonly float[] NextTop = { 430f, 545f, 650f }, NextBottom = { 530f, 635f, 740f };
        public const string SunnyTitle = "Sunny", SunnyNote = "Tour and tips";
        public const string TourLabel = "Show me around", TourNote = "A quick tour, under a minute";
        public const string TipsLabel = "Tips";
        public const string LayersLabel = "Make a long song", LayersNote = "Record part on top of part";
        public const string LoopShowLabel = "Loop a beat", LoopShowNote = "A one-minute loop show";
        /// <summary>The Sunny screen with six rows (Look and pinch there too): a little tighter, so all fit above the footer.</summary>
        public const float GuideTightH = 86f, GuideTightGap = 8f;
        public const string TipsOnLabel = "Tips while I play", TipsOnOn = "On: Sunny helps", TipsOnOff = "Off: only from here";
        public const string LookPinchLabel = "Look and pinch", LookPinchOn = "On: look, then pinch", LookPinchOff = "Off: poke only";
        public const string SunnyFooter = "Poke Sunny anytime to skip", TipsFooter = "Poke a tip to hear it again";
        public const string TipNew = "New", TipHeard = "Heard";
        /// <summary>The Sunny screen's rows: the label over its note.</summary>
        public const float GuideLabelSize = 48f, GuideNoteSize = 32f, GuideLabelDy = 17f, GuideNoteDy = 26f;

        /// <summary>ACCENT: cyan for home and the lessons screens, amber for instruments, rose for recordings.</summary>
        public static Color Accent(MenuScreen s) =>
            s == MenuScreen.Instruments || s == MenuScreen.Arrange || s == MenuScreen.Guide || s == MenuScreen.Tips ? Amber
            : s == MenuScreen.Recordings || s == MenuScreen.Take ? Rose : Cyan;

        /// <summary>PARENT: where the back row goes.</summary>
        public static MenuScreen Parent(MenuScreen s)
        {
            switch (s)
            {
                case MenuScreen.Courses: return MenuScreen.Lessons;
                case MenuScreen.Lesson: return MenuScreen.Courses;
                case MenuScreen.Take: return MenuScreen.Recordings;
                case MenuScreen.Tips: return MenuScreen.Guide;
                default: return MenuScreen.Home;
            }
        }

        public static Color Rgb(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);
        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }
}
