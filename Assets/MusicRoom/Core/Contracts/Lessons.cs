// Recorder, tutorial and take-library contracts (spec 05 section 9, spec 08 sections 2 and 4). The Lessons module
// implements them; instruments only call IRecorder.Add; Sunny and the tutorial listen to RecorderEvent.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace MusicRoom.Core
{
    /// <summary>One recorded event, flat so JsonUtility / Newtonsoft round-trip it losslessly (the take file's field names).</summary>
    [Serializable]
    public struct TakeEvent
    {
        public float t;        // seconds since Record, on the audio clock
        public string type;    // TakeEventType.*
        public int midi;       // pianoOn / pianoOff
        public float vel;      // pianoOn / drum / harp
        public string kind;    // drum / drumChoke: snare, tom1, tom2, floor, kick, hat, hatOpen, hatClose, crash, ride
        public float pos;      // drum: 0 centre .. 1 edge
        public string zone;    // drum: head | rim | bell | bow | edge | top
        public bool open;      // drum (hat state) / hat
        public int i;          // harp / harpDamp string index
        public float u;        // harp pluck position 0..1 from the soundboard
        public float speed;    // harp stroke speed m/s
        public string mode;    // harp: through | across
        /// <summary>The continuous-control lane (platform.md 6.3-6.4, PlaybackLanes): 0 live; playback dispatch sets 1..6 for
        /// a looper layer, 7 for a take. Old takes read as 0 (JsonUtility default).</summary>
        public int lane;

        public static TakeEvent PianoOn(int midi, float vel) => new TakeEvent { type = TakeEventType.PianoOn, midi = midi, vel = vel };
        public static TakeEvent PianoOff(int midi) => new TakeEvent { type = TakeEventType.PianoOff, midi = midi };
        public static TakeEvent Drum(string kind, float vel, float pos, string zone, bool open) => new TakeEvent { type = TakeEventType.Drum, kind = kind, vel = vel, pos = pos, zone = zone, open = open };
        public static TakeEvent DrumChoke(string kind) => new TakeEvent { type = TakeEventType.DrumChoke, kind = kind };
        public static TakeEvent Hat(bool open) => new TakeEvent { type = TakeEventType.Hat, open = open };
        public static TakeEvent Harp(int i, float vel, float u, float speed, string mode) => new TakeEvent { type = TakeEventType.Harp, i = i, vel = vel, u = u, speed = speed, mode = mode };
        public static TakeEvent HarpDamp(int i) => new TakeEvent { type = TakeEventType.HarpDamp, i = i };
    }

    /// <summary>The classic instruments' types (piano, drums, harp). New instruments declare theirs in their descriptor (EventTypeSpec, prefixed with
    /// their id: "handpan", "handpanMute"; the hand drums' start with "handDrum") and keep the constants in their own
    /// assembly.</summary>
    public static class TakeEventType
    {
        public const string PianoOn = "pianoOn", PianoOff = "pianoOff";
        public const string Drum = "drum", DrumChoke = "drumChoke", Hat = "hat";
        public const string Harp = "harp", HarpDamp = "harpDamp";
    }

    public enum RecorderState { Idle = 0, Recording = 1, Playing = 2 }

    /// <summary>The take recorder (spec 08 section 4.2). Now = App.AudioTime (AudioSettings.dspTime, or a test's scripted clock).</summary>
    public interface IRecorder
    {
        RecorderState State { get; }
        /// <summary>A saved take (from the library) is playing instead of the deck's own take.</summary>
        bool External { get; }
        /// <summary>Observers (RecorderEvent) always; appended to the take only while recording. Max 40000 events / 600 s
        /// (platform.md 6.5). Marker types are recorded but never played back.</summary>
        void Add(in TakeEvent ev);
        /// <summary>As Add, stamped at an earlier audio time (an instrument that classifies a stroke a little after it
        /// sounded, e.g. the hand drums' mute relabel within 90 ms): the event's t is (at - record start). The default
        /// ignores the time.</summary>
        void Add(in TakeEvent ev, double atAudioTime) => Add(ev);
        void Record();
        bool Play();
        bool PlayEvents(List<TakeEvent> events, double seconds);
        void Stop();
        /// <summary>The deck's own take and its length (null when none).</summary>
        List<TakeEvent> Take { get; }
        double TakeSeconds { get; }
        /// <summary>Raised when a recording stops with at least one event (auto-save).</summary>
        event Action<List<TakeEvent>, double> OnTake;
        /// <summary>What the last Play / PlayEvents did, in words for the log (what played, or why nothing did).</summary>
        string LastPlay => "";
        /// <summary>The deck's REC was pressed while a take played: the player records on top of it (the take may still be
        /// playing, State Playing, or have ended, State Recording). Sunny's show lines wait meanwhile. The default: false.</summary>
        bool RecordingOnTop => false;
    }

    /// <summary>LoopEvent phases (sunny-tour.md 7.4). CountIn: LOOP started a session (clicks, waiting for the first note);
    /// Recording: a layer started recording (the first note, or an overdub's bar); LayerDone: a layer was finished (it plays
    /// from the next pass); Muted: a layer was muted or unmuted; Undo: the last layer was taken away; ClearArmed: CLEAR waits
    /// for its second tap; Cleared: the session was erased; Stopped: looping ended; Saved: the session was written to
    /// Recordings.</summary>
    public enum LoopEventPhase : byte { CountIn = 0, Recording, LayerDone, Muted, Undo, ClearArmed, Cleared, Stopped, Saved }

    /// <summary>The tape deck's controls (ILooper.TryGetControl / Press: Sunny's demo anchors and walkthrough).</summary>
    public enum LoopControl : byte { Loop = 0, Stop, Undo, Clear, Slow, Medium, Fast, Bars2, Bars4, Click, Tidy, Layer, Rec, Play, Guide }

    /// <summary>Where a loop session is. Waiting: LOOP was pressed, the clicks wait for the first note; RecordingFirst: layer
    /// 1 records its pass; Looping: the layers repeat; Armed: a layer waits for its pass; Overdubbing: it records it.</summary>
    public enum LoopStatus : byte { Off = 0, Waiting, RecordingFirst, Looping, Armed, Overdubbing }

    /// <summary>The looper (Lessons, on the tape deck): LOOP records a layer that repeats; more layers go on top, one pass
    /// each (up to 6). Registered by the Lessons module; Sunny's tips read it.</summary>
    public interface ILooper
    {
        /// <summary>A loop session is running: clicks waiting for the first note, recording or looping.</summary>
        bool Active { get; }
        /// <summary>Finished layers of the running (or the deck's last) session.</summary>
        int Layers { get; }
        int Bpm { get; }
        int Bars { get; }
        bool Metronome { get; }
        /// <summary>Preset the next session: 70 / 90 / 110 BPM (others snap to the nearest), 2 or 4 bars, the click while
        /// looping. Ignored while a session runs.</summary>
        void Configure(int bpm, int bars, bool metronome);
        /// <summary>A control's world position and the deck's up (layer: the layer light's index 0..5).</summary>
        bool TryGetControl(LoopControl control, int layer, out Vector3 world, out Vector3 up);
        LoopStatus Status { get; }
        /// <summary>Press a deck control as a fingertip would (Sunny's walkthrough doing a step for the player).</summary>
        void Press(LoopControl control, int layer = 0);
    }

    public enum TutorialState { Idle = 0, Countdown, Playing, Waiting, Done }

    /// <summary>sunny-tour 7.4: NoCountdown (start at once), Accompany (play the lesson's accompaniment: through the
    /// instrument's ILessonAccompany, else the piano sound), QuietMistakes (no mistake sound), Forgiving (any hit on the
    /// instrument counts), Hidden (no menu screen change). A pack lesson names them in "options".</summary>
    [Flags]
    public enum LessonOptions { None = 0, NoCountdown = 1, Accompany = 2, QuietMistakes = 4, Forgiving = 8, Hidden = 16 }

    public struct TutorialSummary { public int Notes, Mistakes; public float Seconds; }

    public interface ITutorial
    {
        TutorialState State { get; }
        string LessonId { get; }
        InstrumentKind Instrument { get; }
        /// <summary>Counts changes (menu redraw).</summary>
        int Version { get; }
        float Countdown { get; }         // seconds left while counting down
        string Hint { get; }             // current event's hint while waiting
        float Progress { get; }          // events done / total
        /// <summary>Targets hit so far (a chord counts each of its notes), not events.</summary>
        int EventsDone { get; }
        /// <summary>The lesson's targets (a chord counts each of its notes), not events.</summary>
        int EventsTotal { get; }
        /// <summary>Wrong plays in reach so far, live during the lesson (the panel's "Mistakes: N").
        /// The default reads Summary, which the Lessons tutorial keeps live while a lesson runs.</summary>
        int Mistakes => Summary.Mistakes;
        /// <summary>The digit the countdown shows: max(1, ceil(seconds left)).</summary>
        int Digit => Math.Max(1, (int)Math.Ceiling(Countdown));
        TutorialSummary Summary { get; }
        void Start(string lessonId);
        /// <summary>Start with options (sunny-tour 7.4; a pack lesson's "options" are applied by Start(id) itself). The
        /// default ignores them.</summary>
        void Start(string lessonId, LessonOptions options) => Start(lessonId);
        void Stop();
        /// <summary>The sheet-music hook (platform.md 13.5): index into the running lesson's events of the event the player
        /// is on (the next block to land, or the one being waited for); -1 when no lesson runs. The default: -1.</summary>
        int CurrentEvent => -1;
        /// <summary>What the summary counts for the running (or last) lesson's instrument: LessonSpec.CountNoun ("notes",
        /// "strokes", "rings"). The default: "notes".</summary>
        string CountNoun => "notes";
    }

    /// <summary>The generic tutorial path for every instrument but the classic three (platform.md 7.2). The instrument
    /// registers it (app.Register&lt;ILessonTargets&gt;); Tutorial finds it by Kind. All world-space.</summary>
    public interface ILessonTargets
    {
        InstrumentKind Kind { get; }
        /// <summary>Pack checks (EditMode, LessonPackTests): the target names something this instrument has.</summary>
        bool Validate(in LessonTarget t, out string why);
        /// <summary>Where the block lands (pose: its up along the surface normal) and the unit direction it travels along to
        /// get there (from start to landing); size: the block's footprint (m).</summary>
        bool TryGetLanding(in LessonTarget t, out Pose landing, out Vector3 approach, out Vector2 size);
        /// <summary>A live (not playback) event hits this target.</summary>
        bool Matches(in LessonTarget t, in TakeEvent e);
        /// <summary>The instrument's own cue on the target, k 0..1 (no glow: a warm tint, a lifted bar); k 0 = off.</summary>
        void ShowTarget(in LessonTarget t, float k);
        /// <summary>The waiting hint: "Tap D", "Slap the low drum", "Hold A".</summary>
        string Hint(IReadOnlyList<LessonTarget> targets);
        /// <summary>The block's travel for this target (m); the default is the descriptor's LessonSpec.Travel. An instrument
        /// shortens it where the line of sight is short (the handpan's near fields).</summary>
        float Travel(in LessonTarget t, float travel) => travel;
    }

    /// <summary>Optional: an instrument that plays a lesson's accompaniment itself (a pack lesson's "accompany" chords);
    /// otherwise the tutorial plays them on the piano sound.</summary>
    public interface ILessonAccompany
    {
        InstrumentKind Kind { get; }
        void Play(int[] midi, float vel);
        void Release();
    }

    // ---- lesson data: the JSON schema of Docs/lessons.json (JsonUtility-compatible) ----
    [Serializable] public class LessonFile { public LessonTutorialConstants tutorial; public LessonCounts counts; public Lesson[] lessons; }
    [Serializable] public class LessonTutorialConstants { public float SPEED; public LessonTravel TRAVEL; public LessonLead leadSeconds; }
    /// <summary>The classic instruments' travel and lead (lessons.json). Pack instruments take theirs from LessonSpec.</summary>
    [Serializable] public class LessonTravel { public float piano, drums, harp; }
    [Serializable] public class LessonLead { public float piano, drums, harp; }
    /// <summary>The classic counts (lessons.json); pack lessons are counted by ILessonLibrary.Count(kind).</summary>
    [Serializable] public class LessonCounts { public int lessons, piano, drums, harp, events, targets; }
    [Serializable]
    public class Lesson
    {
        public string id, instrument, title, listNote;
        public float bpm, secondsPerBeat, leadSeconds, beats, seconds;
        public int eventCount, notes;
        public LessonEvent[] events;
        /// <summary>Optional (packs, platform.md 7.1): tutorial options applied when the lesson starts ("Accompany",
        /// "QuietMistakes"), the accompaniment, and the sheet music's notation data (sheet.md): meter "4/4", unit "q",
        /// pickup beats, the melody's public-domain source.</summary>
        public string[] options;
        public LessonChord[] accompany;
        public string meter, unit, credit;
        public float pickup;
    }

    /// <summary>One accompaniment chord: at beat, these notes for len beats.</summary>
    [Serializable] public class LessonChord { public float beat, len; public int[] midi; public float vel = 0.35f; }
    [Serializable]
    public class LessonEvent
    {
        public int k;
        public float beat, len, time, duration, due;
        public string hint;
        public LessonTarget[] targets;
    }
    /// <summary>One class for every instrument: piano {midi, note}, drums {kind, label}, harp {i, midi, note, color}; the
    /// platform's optional fields (JsonUtility defaults when absent): zone (hand drums, handpan), mode (bowls "strike" |
    /// "rub"), hold (seconds, bowls), cents (pitch tolerance, default 35), hand ("L" | "R"), len
    /// (beats, the sheet music's note length).</summary>
    [Serializable]
    public class LessonTarget
    {
        public int midi = -1, i = -1;
        public string note, kind, label, color;
        public string zone, mode, hand;
        public float hold, cents = 35f, len;
    }

    public interface ILessonLibrary
    {
        IReadOnlyList<Lesson> Lessons { get; }
        Lesson Get(string id);
        int Count(InstrumentKind instrument);
        LessonTutorialConstants Constants { get; }
    }

    // ---- take library (persistentDataPath/takes, spec 08 section 4.4) ----
    [Serializable] public class TakeInfo { public string id, name; public long created; public float seconds; public int count; }
    [Serializable] public class TakeRecord { public string id, name; public long created; public float seconds; public List<TakeEvent> events; }

    public interface ITakeLibrary
    {
        /// <summary>null until checked; false when the folder is not writable ("Saving off").</summary>
        bool? Available { get; }
        int Version { get; }
        event Action Changed;
        Task<List<TakeInfo>> List();
        Task<TakeRecord> Get(string id);
        Task<TakeRecord> Put(TakeRecord take);
        Task<bool> Remove(string id);
        string FormatLength(double seconds);
        string FormatDate(long createdMs);
    }
}
