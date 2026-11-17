// The recorder (spec 08 section 4.2), plus the looper (Looper.cs) and the platform's generic recording
// (platform.md 6.1-6.5). Plain C#; Now is injected (App.AudioTime in the app: dspTime, or the BootOptions.AudioClock a
// test scripts). Observers get every event through the EventBus (RecorderEvent); what a playback sends back is also
// announced as PlaybackNoteEvent and a live note that went into a recording (take or looper layer) as RecordedNoteEvent.
// The looper shares the recorder's front: while a loop session runs, State is Recording (clicks waiting for the first
// note, layer 1's pass) or Playing (looping, an overdub included), live events go to the looper, and the events the
// looper sends to the instruments come back here flagged DuringPlayback (never recorded, never a lesson hit). REC while
// looping arms a layer; PLAY plays the deck's last recording, a take or a loop; PlayEvents of a saved loop loops it;
// STOP stops either. With no loop session the single-take behaviour is exactly today's.
// The platform (Roles, from the registry): up to 40000 events / 600 s; Add(ev, at) stamps an event at an earlier audio
// time (kept in time order); Marker types are recorded but never played back; a take plays on PlaybackLanes.Take; a
// Control is dispatched ControlEncoder.DispatchLead(speed) early so its glide ends when it was recorded; and a displayed
// instrument is woken (IAudioPresence.Wake) WakeAhead before its first event, from Play on (the take is scanned ahead).
// A playback the player starts (PLAY of a take or of the deck's loop, a take or a loop from Recordings) is announced with
// the instruments it plays (RecordingPlaybackEvent Started, Kinds) and so is its end, so Layout can bring them in front of
// the player for it; a loop session being recorded (LOOP) is the player's own music, not a playback of a recording.
// Record on top (2026-11-17, "drums for 4 seconds then piano for 30 seconds and then strings ... all instruments should
// play at same time", like a tape deck's overdub): the deck's REC while a TAKE plays (the deck's own or one from
// Recordings; never a loop) calls RecordOnTop: the playback goes on from where it is and the player's new notes are
// recorded on the take's own clock (t = audio time - InputLatency - the playback's start, the looper's rule for what the
// player heard), so a note played on a beat lands on that beat. State stays Playing while the take plays (as a looper
// overdub: the show goes on for the played-back notes only, the new notes are RecordedNoteEvents), and becomes Recording
// when the take reaches its end (announced before the playback's end, so Layout keeps what is in front of the player, as
// for a REC) while the recording goes on. STOP (or anything that stops a recording) saves a NEW take, the original's
// events and the new ones merged in time order, max(the original's length, the STOP) long; the original stays as it was.
// Nothing new played: nothing is saved. The take's own sends (Fire, its closing release) are playback (never recorded).
using System;
using System.Collections.Generic;
using MusicRoom.Core;

namespace MusicRoom.Lessons
{
    public sealed class Recorder : IRecorder
    {
        /// <summary>platform.md 6.5: two continuous lanes at 29 events/s for 10 minutes make 35000.</summary>
        public const int MaxEvents = 40000;
        public const double MaxSeconds = 600;
        /// <summary>platform.md 6.2: a displayed instrument's sources resume at least 250 ms before its first playback event
        /// (the looper's look-ahead is 80 ms, so the wake looks 0.3 s ahead).</summary>
        public const double WakeAhead = 0.3;
        /// <summary>One wake per instrument at most this often (its sources stay up 1 s after the last sound anyway).</summary>
        public const double WakeEvery = 0.25;

        readonly Func<double> _now;
        readonly EventBus _events;
        readonly Dictionary<string, Action<TakeEvent>> _targets = new Dictionary<string, Action<TakeEvent>>();
        List<TakeEvent> _events_;
        List<TakeEvent> _take;
        double _t0, _playStart, _duration;
        int _playIndex, _ctlIndex, _wakeIndex;
        (List<TakeEvent> take, double duration)? _saved;
        Action _releaseAll;
        Action<int> _closeLane;
        RecorderState _state = RecorderState.Idle;
        RecorderState _shownState = RecorderState.Idle;
        bool _shownExternal;
        Looper _looper;
        EventRoles _roles = EventRoles.Classic;
        readonly Dictionary<string, (TakeEvent e, bool seen)> _lastSampled = new Dictionary<string, (TakeEvent, bool)>(StringComparer.Ordinal);
        readonly double[] _lastWake = new double[InstrumentIds.KindCount + 6];
        IAudioPresence _presence;
        bool _loopPlayback;
        // record on top: the take recorded onto (its events and length), its name for the log, the events it already has
        bool _onTop, _sending;
        List<TakeEvent> _onTopBase;
        double _onTopBaseSeconds;
        int _baseCount;
        string _onTopName;

        public Recorder(Func<double> now, EventBus events, Action releaseAll = null)
        {
            _now = now;
            _events = events;
            _releaseAll = releaseAll;
            for (int i = 0; i < _lastWake.Length; i++) _lastWake[i] = double.NegativeInfinity;
            Roles = EventRoles.Classic;
        }

        /// <summary>The looper on the deck (set once by the module; null = today's recorder only).</summary>
        public Looper Looper
        {
            get => _looper;
            set
            {
                if (_looper != null) _looper.StateChanged -= SyncState;
                _looper = value;
                if (_looper != null) _looper.StateChanged += SyncState;
            }
        }

        /// <summary>The event types' roles (EventRoles.From(registry) in the app; Classic without one).</summary>
        public EventRoles Roles
        {
            get => _roles;
            set
            {
                _roles = value ?? EventRoles.Classic;
                _lastSampled.Clear();
                // Every Sampled State type gets its slot now, so remembering the latest value never allocates in the frame.
                foreach (string t in _roles.Sampled) _lastSampled[t] = (new TakeEvent { type = t }, false);
            }
        }

        /// <summary>Where displayed instruments are woken before their playback (IAudioPresence, Audio). Looked up once.</summary>
        public Func<IAudioPresence> PresenceSource;

        bool LoopActive => _looper != null && _looper.Active;

        public RecorderState State
        {
            get
            {
                if (!LoopActive) return _state;
                return _looper.State == LoopState.Looping ? RecorderState.Playing : RecorderState.Recording;
            }
        }

        public bool External => _saved != null || (_looper != null && _looper.External);
        public List<TakeEvent> Take => _take;
        public double TakeSeconds => _duration;
        /// <summary>The deck's last recording is a loop session (PLAY loops it) rather than a take.</summary>
        public bool CurrentIsLoop { get; private set; }
        /// <summary>The instruments the running playback of a recording plays (RecordingPlaybackEvent.Kinds); 0 when none runs.</summary>
        public uint PlaybackKinds { get; private set; }
        public event Action<List<TakeEvent>, double> OnTake;

        /// <summary>Playback targets by event type (Piano.PlaybackOn etc.); set by the Lessons module once the instruments exist.</summary>
        public void SetTarget(string type, Action<TakeEvent> handler) => _targets[type] = handler;
        public bool HasTarget(string type) => type != null && _targets.ContainsKey(type);
        public int TargetCount => _targets.Count;
        public void SetReleaseAll(Action releaseAll) => _releaseAll = releaseAll;
        /// <summary>A take's stop ends its continuous lanes (IInstrumentPlayback.CloseLane(PlaybackLanes.Take)).</summary>
        public void SetCloseLane(Action<int> closeLane) => _closeLane = closeLane;

        /// <summary>Plays one event through its target (the looper's sends, inside the engine's ScheduleAt scope). when: the
        /// audio time it sounds (0 = now), for the PlaybackNoteEvent observers.</summary>
        public void Dispatch(in TakeEvent e, double when = 0)
        {
            if (e.type == null || !_targets.TryGetValue(e.type, out var h)) return;
            h(e);
            PublishNote(e, when);
        }

        /// <summary>The playback observers' event (allocation-free: a struct on the bus).</summary>
        void PublishNote(in TakeEvent e, double when)
        {
            if (_events == null) return;
            EventRole role = _roles.RoleOf(e.type);
            if (role == EventRole.Marker) return;
            float level = role == EventRole.Release ? 0f : e.vel;
            if (!(level > 0f)) level = 0f;   // NaN and negatives read as silence
            _events.Publish(new PlaybackNoteEvent { Kind = _roles.KindOf(e.type), Role = role, Level = level > 1f ? 1f : level, When = when, Lane = e.lane, Event = e });
        }

        /// <summary>A live note that went into a recording (the take or a looper layer), for what watches the music being made
        /// (the room's lights and butterflies). Allocation-free: a struct on the bus. Played-back notes never come here (the
        /// looper's own sends return before the capture; a take's playback is not a recording), so no sound is announced
        /// both as played back and as recorded.</summary>
        void PublishRecorded(in TakeEvent e)
        {
            if (_events == null) return;
            EventRole role = _roles.RoleOf(e.type);
            if (role == EventRole.Marker) return;
            float level = role == EventRole.Release ? 0f : e.vel;
            if (!(level > 0f)) level = 0f;
            _events.Publish(new RecordedNoteEvent { Kind = _roles.KindOf(e.type), Role = role, Level = level > 1f ? 1f : level, Lane = e.lane });
        }

        /// <summary>Read-ahead for what watches a playback (the ghost hands plan their strokes before they sound): the non-Marker
        /// events the running playback will send whose audio time falls in [from, to), in time order, written into events /
        /// when (at most their length; lane set as it will be dispatched); returns the count. A take: its events from the
        /// take's start; a loop: the looper's layers as it will send them (Looper.Upcoming). Nothing while a take records or
        /// nothing plays. Allocation-free.</summary>
        public int Upcoming(double from, double to, TakeEvent[] events, double[] when)
        {
            if (events == null || when == null || !(to > from)) return 0;
            if (LoopActive) return _looper.Upcoming(from, to, events, when);
            if (_state != RecorderState.Playing || _take == null) return 0;
            int n = _take.Count, max = Math.Min(events.Length, when.Length), c = 0;
            double a = from - _playStart, b = to - _playStart;
            int lo = 0, hi = n;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (_take[mid].t < a) lo = mid + 1; else hi = mid; }
            for (int i = lo; i < n && c < max; i++)
            {
                TakeEvent e = _take[i];
                if (e.t >= b) break;
                if (_roles.RoleOf(e.type) == EventRole.Marker) continue;
                e.lane = PlaybackLanes.Take;
                events[c] = e;
                when[c] = _playStart + e.t;
                c++;
            }
            return c;
        }

        /// <summary>Every key the playback holds goes up (the piano's ghost presser, every IInstrumentPlayback).</summary>
        public void ReleaseAll() => _releaseAll?.Invoke();

        /// <summary>The latest live value of a Sampled State type (the looper samples it at a layer's start).</summary>
        public bool TryLastLive(string type, out TakeEvent e)
        {
            e = default;
            if (type == null || !_lastSampled.TryGetValue(type, out var last) || !last.seen) return false;
            e = last.e;
            return true;
        }

        public void Add(in TakeEvent ev) => AddAt(ev, double.NaN);

        /// <summary>As Add, stamped at an earlier audio time (the hand drums' mute relabel): t = at - record start, at least 0;
        /// the take stays in time order. Observers get it at once.</summary>
        public void Add(in TakeEvent ev, double atAudioTime) => AddAt(ev, atAudioTime);

        void AddAt(in TakeEvent ev, double at)
        {
            bool dispatching = _looper != null && _looper.Dispatching;
            bool loop = LoopActive;
            // A loop's own sends are playback; the player's notes during a loop are live (they record a layer). So are a take's
            // own sends (_sending) and, recording on top of it, only those.
            bool playback = dispatching || _sending || (!loop && _state == RecorderState.Playing && !_onTop);
            _events?.Publish(new RecorderEvent { Event = ev, DuringPlayback = playback });
            if (!playback && ev.type != null && _lastSampled.ContainsKey(ev.type)) _lastSampled[ev.type] = (ev, true);   // an existing key: no allocation
            if (dispatching) return;
            if (loop)
            {
                if (_looper.Capture(ev, at)) PublishRecorded(ev);
                return;
            }
            if (playback || (_state != RecorderState.Recording && !_onTop)) return;
            double now = _now();
            double t = (double.IsNaN(at) ? now : at) - _t0;
            if (t < 0) t = 0;
            if (_events_.Count + _baseCount >= MaxEvents || now - _t0 > MaxSeconds) { Stop(); return; }
            PublishRecorded(ev);
            var e = ev;
            e.t = (float)t;
            // An event stamped earlier than the last one goes into its place (usually it is appended).
            int i = _events_.Count;
            while (i > 0 && _events_[i - 1].t > e.t) i--;
            if (i == _events_.Count) _events_.Add(e);
            else _events_.Insert(i, e);
        }

        /// <summary>REC. While a loop runs: arm the next layer (the deck's REC is the overdub button then).</summary>
        public void Record()
        {
            if (LoopActive)
            {
                _looper.Arm();
                return;
            }
            Stop();
            _events_ = new List<TakeEvent>(1024);
            _t0 = _now();
            CurrentIsLoop = false;
            SetState(RecorderState.Recording);
        }

        // ---------------- record on top (see the header) ----------------

        /// <summary>What the player heard is this much older than the audio clock when they play (the looper's InputLatency
        /// when there is a looper, this without one).</summary>
        public double InputLatency = 0.03;
        double OnTopLatency => _looper != null ? _looper.InputLatency : InputLatency;

        /// <summary>A take (not a loop) plays and nothing records: the deck's REC records on top of it.</summary>
        public bool CanRecordOnTop => !LoopActive && !_loopPlayback && !_onTop && _state == RecorderState.Playing && _take != null;
        /// <summary>A recording on top of a take runs (while the take plays, and after its end until STOP).</summary>
        public bool RecordingOnTop => _onTop;
        /// <summary>The take recorded onto still plays (both the deck's REC and PLAY are down).</summary>
        public bool OnTopPlaying => _onTop && _state == RecorderState.Playing;
        /// <summary>Since boot (tests, Sunny's tip): recordings on top started; takes that played to their very end (a
        /// recording on top going on past the end is not counted).</summary>
        public int OnTopStarts { get; private set; }
        public int TakeEnds { get; private set; }
        /// <summary>The deck's own take's name in Recordings (set by the module when it is saved), for the log.</summary>
        public string DeckTakeName { get; set; }
        /// <summary>A saved recording's name by its id (the take library), for the log.</summary>
        public Func<string, string> NameOfId;

        /// <summary>The deck's REC while a take plays: the playback goes on and the player's notes record on top of it, on the
        /// take's clock. False (nothing changes) when no take plays.</summary>
        public bool RecordOnTop()
        {
            if (!CanRecordOnTop) return false;
            double now = _now();
            _onTop = true;
            _onTopBase = _take;
            _onTopBaseSeconds = _duration;
            _baseCount = _take.Count;
            _events_ = new List<TakeEvent>(1024);
            _t0 = _playStart + OnTopLatency;      // t = audio time - latency - the playback's start: the take's own clock
            CurrentIsLoop = false;
            OnTopStarts++;
            _onTopName = TakeName();
            UnityEngine.Debug.Log($"[Lessons] record on top of '{_onTopName}' at {now - _playStart:F1} s");
            return true;
        }

        string TakeName()
        {
            if (_saved != null)
            {
                string id = ReferenceEquals(PlayingTake.Events, _take) ? PlayingTake.Id : null;
                string n = id != null ? NameOfId?.Invoke(id) : null;
                return string.IsNullOrEmpty(n) ? "a recording" : n;
            }
            return string.IsNullOrEmpty(DeckTakeName) ? "the deck's take" : DeckTakeName;
        }

        /// <summary>Two takes as one, in time order (at equal times the first take's event first). Neither is changed.</summary>
        public static List<TakeEvent> Merge(List<TakeEvent> a, List<TakeEvent> b)
        {
            int na = a?.Count ?? 0, nb = b?.Count ?? 0, i = 0, j = 0;
            var m = new List<TakeEvent>(na + nb);
            while (i < na && j < nb) m.Add(b[j].t < a[i].t ? b[j++] : a[i++]);
            while (i < na) m.Add(a[i++]);
            while (j < nb) m.Add(b[j++]);
            return m;
        }

        // The take recorded onto reached its end and the recording goes on. Recording is announced before the playback's end,
        // so what is in front of the player stays for it (Layout: as for a REC). What the take still holds is let go at STOP,
        // where the new take ends too (so the player hears what the new take will play; and the piano's release-all would
        // also damp the player's own ringing notes mid-recording).
        void OnTopPlaybackDone()
        {
            SetState(RecorderState.Recording);
            PlaybackEnded(false);
        }

        void CloseTakeLanes()
        {
            _sending = true;
            try
            {
                _closeLane?.Invoke(PlaybackLanes.Take);
                _releaseAll?.Invoke();
            }
            finally { _sending = false; }
        }

        // STOP of a recording on top: a new take, the take's events and the new ones merged, max(its length, now) long.
        void FinishOnTop()
        {
            bool wasPlaying = _state == RecorderState.Playing;
            double end = _now() - _playStart;
            List<TakeEvent> added = _events_;
            _onTop = false;
            _baseCount = 0;
            CloseTakeLanes();                            // the take's held notes and lanes end here (also past its end)
            if (added != null && added.Count > 0)
            {
                List<TakeEvent> merged = Merge(_onTopBase, added);
                double seconds = Math.Max(_onTopBaseSeconds, end);
                _saved = null;                           // the new take is the deck's now (as a REC's is)
                _take = merged;
                _duration = seconds;
                UnityEngine.Debug.Log($"[Lessons] record on top of '{_onTopName}' saved: {merged.Count} events ({added.Count} new), {seconds:F1} s, {KindNames(KindsOf(merged, _roles))}");
                OnTake?.Invoke(new List<TakeEvent>(merged), seconds);
            }
            else
            {
                if (_saved is { } s) { _take = s.take; _duration = s.duration; _saved = null; }
                UnityEngine.Debug.Log($"[Lessons] record on top of '{_onTopName}': nothing new was played, nothing saved");
            }
            _onTopBase = null;
            SetState(RecorderState.Idle);
            if (wasPlaying) PlaybackEnded(false);
        }

        /// <summary>LOOP on the deck: a new loop session (stops a take first), or, while one runs, the next layer.</summary>
        public void Loop()
        {
            if (_looper == null) return;
            if (!LoopActive && (_state != RecorderState.Idle || _saved != null)) StopTake();
            _looper.PressLoop();
            if (_looper.Active) CurrentIsLoop = true;
        }

        /// <summary>What the last Play / PlayEvents did, in words for the log ("loop, 3 layers, 47 notes" or why nothing played).</summary>
        public string LastPlay { get; private set; } = "";
        /// <summary>A playback of a recording runs (between RecordingPlaybackEvent Started and its end) and since when (audio s).</summary>
        public bool PlaybackOn { get; private set; }
        public double PlaybackSeconds => PlaybackOn ? _now() - _playbackAt : 0;
        double _playbackAt;

        public bool Play()
        {
            if (LoopActive) { LastPlay = "the loop is playing already"; return false; }
            if (_onTop) Stop();                          // PLAY ends a recording on top (saved) and plays the deck's take
            if (External) Stop();                        // deck PLAY plays the deck's own take (spec 08 section 8.5)
            if (_state == RecorderState.Recording) Stop();
            if (CurrentIsLoop && _looper != null && _looper.DeckSession != null)
            {
                if (_state == RecorderState.Playing) StopTake();
                LoopSession deck = _looper.DeckSession;
                if (!_looper.PlayDeck()) { LastPlay = "the deck's loop has no layer left"; return false; }
                LastPlay = Describe(deck);
                LoopStarted(deck);
                // The Room's gallery knows a deck loop by its saved record, as it knows one from Recordings (after the start:
                // a note made after the playback began belongs to it).
                if (!string.IsNullOrEmpty(deck.Id)) PlayingTake.Note(deck.Id, LoopFormat.Encode(deck));
                return true;
            }
            if (_take == null || _take.Count == 0) { LastPlay = "the deck has no recording yet"; return false; }
            if (!AnyPlayable(_take)) { LastPlay = "no instrument here plays its notes"; return false; }
            if (_state == RecorderState.Playing) _releaseAll?.Invoke();
            LastPlay = Describe(_take, _duration);
            BeginPlayback();
            return true;
        }

        public bool PlayEvents(List<TakeEvent> evs, double seconds)
        {
            if (LoopFormat.IsLoop(evs))
            {
                // A saved loop session (Recordings > Play): it loops until STOP; the deck's own take / loop wait meanwhile.
                LoopSession probe = LoopFormat.Decode(evs, _looper != null ? _looper.SampleRate : 48000);
                if (_looper == null) { LastPlay = "no looper"; return false; }
                if (probe == null || probe.Layers.Count == 0) { LastPlay = "the saved loop has no layer"; return false; }
                if (!AnyPlayable(probe)) { LastPlay = "no instrument here plays its notes"; return false; }
                if (_state != RecorderState.Idle || _saved != null) StopTake();
                if (!_looper.PlayExternal(evs)) { LastPlay = "the saved loop could not be read"; return false; }
                LastPlay = Describe(_looper.ExternalSession);
                LoopStarted(_looper.ExternalSession);
                return true;
            }
            if (evs == null || evs.Count == 0) { LastPlay = "the recording is empty"; return false; }
            if (!AnyPlayable(evs)) { LastPlay = "no instrument here plays its notes"; return false; }
            if (_onTop) StopTake();                      // a recording on top ends (saved) before another plays
            if (LoopActive) _looper.Stop();
            if (_state == RecorderState.Recording) Stop();
            if (_state == RecorderState.Playing) _releaseAll?.Invoke();
            if (_saved == null) _saved = (_take, _duration);
            _take = evs;
            _duration = Math.Max(seconds, evs[evs.Count - 1].t);
            LastPlay = Describe(evs, _duration);
            BeginPlayback();
            return true;
        }

        /// <summary>Some event of the recording has a playback target here (else it would play as silence).</summary>
        bool AnyPlayable(List<TakeEvent> evs)
        {
            if (_targets.Count == 0) return true;        // no targets wired (a bare recorder in a test): nothing to judge by
            for (int i = 0; i < evs.Count; i++) if (evs[i].type != null && _targets.ContainsKey(evs[i].type)) return true;
            return false;
        }

        bool AnyPlayable(LoopSession s)
        {
            if (_targets.Count == 0) return true;
            foreach (LoopLayer l in s.Layers)
                for (int i = 0; i < l.Notes.Count; i++) if (l.Notes[i].Event.type != null && _targets.ContainsKey(l.Notes[i].Event.type)) return true;
            return false;
        }

        string Describe(List<TakeEvent> evs, double seconds) => $"a take, {evs.Count} events, {seconds:F1} s, {KindNames(KindsOf(evs, _roles))}";

        string Describe(LoopSession s)
        {
            int muted = 0;
            foreach (LoopLayer l in s.Layers) if (l.Muted) muted++;
            return $"a loop, {s.Layers.Count} layer{(s.Layers.Count == 1 ? "" : "s")}{(muted > 0 ? $" ({muted} muted)" : "")}, {s.EventCount} events, {s.PassSeconds:F1} s pass, {KindNames(KindsOf(s, _roles))}";
        }

        static string KindNames(uint kinds)
        {
            if (kinds == 0) return "no instrument";
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < 32; k++)
                if ((kinds & (1u << k)) != 0) { if (sb.Length > 0) sb.Append('+'); sb.Append(((InstrumentKind)k).ToString().ToLowerInvariant()); }
            return sb.ToString();
        }

        void BeginPlayback()
        {
            _playStart = _now();
            _playIndex = _ctlIndex = _wakeIndex = 0;
            SetState(RecorderState.Playing);
            Wake(0);   // the take is scanned ahead at Play: what sounds in its first 0.3 s wakes now
            PlaybackKinds = KindsOf(_take, _roles);
            PlaybackOn = true;
            _playbackAt = _now();
            _events?.Publish(new RecordingPlaybackEvent { Started = true, Kinds = PlaybackKinds, Loop = false });
        }

        /// <summary>A saved loop (or the deck's) started looping: announced with its instruments; its end is announced when
        /// the looper stops (SyncState).</summary>
        void LoopStarted(LoopSession s)
        {
            _loopPlayback = true;
            PlaybackKinds = KindsOf(s, _roles);
            PlaybackOn = true;
            _playbackAt = _now();
            _events?.Publish(new RecordingPlaybackEvent { Started = true, Kinds = PlaybackKinds, Loop = true });
        }

        void PlaybackEnded(bool loop)
        {
            double secs = PlaybackSeconds;
            PlaybackKinds = 0;
            PlaybackOn = false;
            UnityEngine.Debug.Log($"[Lessons] playback ended after {secs:F1} s ({(loop ? "a loop" : "a take")})");
            _events?.Publish(new RecordingPlaybackEvent { Started = false, Kinds = 0, Loop = loop });
        }

        /// <summary>The instruments a take plays: one bit per InstrumentKind (1u &lt;&lt; kind) of every non-Marker event's owner.
        /// Allocation-free.</summary>
        public static uint KindsOf(List<TakeEvent> take, EventRoles roles)
        {
            if (take == null) return 0;
            roles = roles ?? EventRoles.Classic;
            uint kinds = 0;
            string last = null;
            for (int i = 0; i < take.Count; i++)
            {
                string t = take[i].type;
                if (t == null || ReferenceEquals(t, last)) continue;
                last = t;
                kinds |= Bit(t, roles);
            }
            return kinds;
        }

        /// <summary>The instruments of every layer of a loop session (muted ones too: a layer can be unmuted while it loops).</summary>
        public static uint KindsOf(LoopSession s, EventRoles roles)
        {
            if (s == null) return 0;
            roles = roles ?? EventRoles.Classic;
            uint kinds = 0;
            for (int l = 0; l < s.Layers.Count; l++)
            {
                List<LoopNote> notes = s.Layers[l].Notes;
                string last = null;
                for (int i = 0; i < notes.Count; i++)
                {
                    string t = notes[i].Event.type;
                    if (t == null || ReferenceEquals(t, last)) continue;
                    last = t;
                    kinds |= Bit(t, roles);
                }
            }
            return kinds;
        }

        static uint Bit(string type, EventRoles roles)
        {
            if (roles.RoleOf(type) == EventRole.Marker) return 0;
            InstrumentKind k = roles.KindOf(type);
            return InstrumentIds.IsPlayable(k) && (int)k >= 0 && (int)k < 32 ? 1u << (int)k : 0u;
        }

        public void Stop()
        {
            if (LoopActive)
            {
                _looper.Stop();                          // publishes Idle through SyncState
                if (_state == RecorderState.Idle && _saved == null) return;
            }
            StopTake();
        }

        // Today's Stop for the single take.
        void StopTake()
        {
            if (_onTop) { FinishOnTop(); return; }
            if (_saved is { } s) { _take = s.take; _duration = s.duration; _saved = null; }
            if (_state == RecorderState.Recording)
            {
                _take = _events_;
                _duration = _now() - _t0;
                if (_take.Count > 0) OnTake?.Invoke(new List<TakeEvent>(_take), _duration);
            }
            bool wasPlaying = _state == RecorderState.Playing;
            if (wasPlaying)
            {
                _closeLane?.Invoke(PlaybackLanes.Take);
                _releaseAll?.Invoke();
            }
            SetState(RecorderState.Idle);
            if (wasPlaying) PlaybackEnded(false);
        }

        /// <summary>Once per frame (Recorder phase): the looper's sends, then the take's dispatch, quantised to frames.</summary>
        public void Update()
        {
            _looper?.Tick();
            if (_state != RecorderState.Playing) return;
            double t = _now() - _playStart;
            Wake(t);
            int n = _take.Count;
            _sending = true;   // what the instruments send back from these is playback (never recorded on top)
            try
            {
                while (_playIndex < n && _take[_playIndex].t <= t)
                {
                    TakeEvent e = _take[_playIndex++];
                    EventRole role = _roles.RoleOf(e.type);
                    if (role == EventRole.Marker || role == EventRole.Control) continue;   // markers never play; controls lead below
                    Fire(e);
                }
                // Controls go min(speed, 0.05) early, so the unit's glide ends when the value was recorded (a lane's controls
                // stay in order: the lead never exceeds the gap to the lane's previous event).
                while (_ctlIndex < n)
                {
                    TakeEvent e = _take[_ctlIndex];
                    if (_roles.RoleOf(e.type) != EventRole.Control) { _ctlIndex++; continue; }
                    if (e.t - ControlEncoder.DispatchLead(e.speed) > t) break;
                    _ctlIndex++;
                    Fire(e);
                }
            }
            finally { _sending = false; }
            if (_playIndex >= n && t >= _duration)
            {
                if (_onTop) { OnTopPlaybackDone(); return; }   // the recording on top goes on past the take's end
                TakeEnds++;
                StopTake();
            }
        }

        void Fire(TakeEvent e)
        {
            if (e.type == null || !_targets.TryGetValue(e.type, out var h)) return;   // a missing instrument drops its events
            e.lane = PlaybackLanes.Take;
            h(e);
            PublishNote(e, 0);
        }

        // The wake cursor runs WakeAhead in front of the take's clock.
        void Wake(double t)
        {
            if (_take == null) return;
            while (_wakeIndex < _take.Count && _take[_wakeIndex].t <= t + WakeAhead)
            {
                TakeEvent e = _take[_wakeIndex++];
                if (_roles.RoleOf(e.type) != EventRole.Marker) WakeOwner(e.type);
            }
        }

        /// <summary>Resume the sources of the instrument that plays this type (throttled per instrument; a no-op without
        /// IAudioPresence or a registry).</summary>
        public void WakeOwner(string type)
        {
            InstrumentKind k = _roles.KindOf(type);
            int i = (int)k;
            if (!InstrumentIds.IsPlayable(k) || i < 0 || i >= _lastWake.Length) return;
            double now = _now();
            if (now - _lastWake[i] < WakeEvery) return;
            if (_presence == null) _presence = PresenceSource?.Invoke();
            if (_presence == null) return;
            _lastWake[i] = now;
            _presence.Wake(k);
        }

        /// <summary>Wakes sent so far for a kind (tests): the last wake's audio time, -inf when none.</summary>
        public double LastWake(InstrumentKind kind) => (int)kind >= 0 && (int)kind < _lastWake.Length ? _lastWake[(int)kind] : double.NegativeInfinity;

        void SetState(RecorderState s)
        {
            if (s == _state && s != RecorderState.Idle) return;
            _state = s;
            Publish();
        }

        // The looper changed: republish when what the recorder shows changed.
        void SyncState()
        {
            if (_loopPlayback && !LoopActive)
            {
                // the saved loop stopped (STOP, CLEAR, UNDO of its last layer, another playback replacing it)
                _loopPlayback = false;
                PlaybackEnded(true);
            }
            if (State == _shownState && External == _shownExternal) return;
            Publish();
        }

        void Publish()
        {
            _shownState = State;
            _shownExternal = External;
            _events?.Publish(new RecorderStateEvent { State = _shownState, External = _shownExternal });
        }
    }
}
