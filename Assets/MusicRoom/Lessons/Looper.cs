// The looper on the tape deck: LOOP starts soft clicks at the chosen tempo; the player's first note starts layer 1 (the
// loop start snapped to the click nearest that note), which records exactly one pass (2 or 4 bars at 70 / 90 / 110 BPM)
// and then repeats without a gap. While it plays, LOOP (or REC) arms the next layer: it records exactly the next pass
// (count-down lights and soft clicks over the last bar before it), while every earlier layer keeps playing. Up to six
// layers; each layer's light mutes / unmutes it; UNDO takes the last one away; CLEAR stops and erases (tap again); STOP
// ends looping and saves the session to Recordings.
//
// Timing (ENGINE-API.md section 10): everything runs on the AUDIO clock in whole samples. Pass k of a run starts at
// Anchor + k x PassSamples exactly, and every event of a layer sits at a fixed sample offset in the pass, so 50 passes
// (or 5000) never drift. Each frame (the Recorder phase) the looper dispatches the events due within the next LookAhead
// (80 ms) through the ordinary instrument playback calls, each inside an IAudioEngine.ScheduleAt scope at its exact
// time, so the sound starts sample-accurately whatever the frame time (the key / drum / string animation follows the
// call, up to LookAhead ahead of the sound). Live events are captured with the audio clock minus InputLatency (what the
// player heard when they played) and placed at their offset in the pass, optionally tidied halfway toward the nearest
// 1/16 without ever leaving their bar. Notes held over the end of a recording pass get their release there.
//
// Plain C#: ILoopHost brings the clock, the instruments, the click and Sunny, so the EditMode tests run it on a scripted
// clock. The deck reads the lamps from it; the Recorder routes the live stream into it.
//
// Every instrument (platform.md 6.4): what an event is comes from the registry's roles (EventRoles). Onsets start layer 1
// and are tidied (unknown types count as Onsets); Controls never are (a lane's controls and its Off move with the shift
// its onset got, so the lane keeps its shape, and a Control sits in the layer at its dispatch offset, DispatchLead early);
// Markers are dropped. At a recording window's end every open Held onset gets its Release (keyed by its EventKey: the
// piano exactly as before), every open continuous lane is closed (a final level-0 Control and the Off), and a struck note
// (a marimba bar, a handpan field, a drum) rings on. Sampled State types (the hi-hat pedal) are sampled when a layer's
// window opens. Layer k plays back on PlaybackLanes.Layer(k); displayed instruments are woken WakeAhead before their
// events (ILoopHost.Wake).
using System;
using System.Collections.Generic;
using MusicRoom.Core;

namespace MusicRoom.Lessons
{
    public enum LoopState : byte
    {
        Off = 0,
        /// <summary>LOOP was pressed: clicks run (after Sunny's first-use line, if any) until the first note.</summary>
        Waiting,
        /// <summary>Layer 1 records its pass (clicks keep time).</summary>
        Recording,
        /// <summary>The layers repeat; an armed layer may be waiting for, or recording, its pass.</summary>
        Looping,
    }

    /// <summary>What a layer light shows.</summary>
    public enum LayerLamp : byte { Off = 0, On, Muted, Armed, Recording }

    /// <summary>What the looper needs from the app (AppLoopHost) or a test.</summary>
    public interface ILoopHost
    {
        /// <summary>The audio clock (seconds): IAudioEngine.Now / App.AudioTime.</summary>
        double Now { get; }
        int SampleRate { get; }
        /// <summary>Play a recorded event through its instrument's playback call, scheduled at `when` (absolute audio seconds,
        /// sample-accurate through IAudioEngine.ScheduleAt; 0 = now).</summary>
        void Dispatch(in TakeEvent e, double when);
        /// <summary>A soft metronome click at `when` (IAudioEngine.Click).</summary>
        void Click(bool accent, double when);
        /// <summary>Every key the playback holds goes up (IPianoApi.PlaybackReleaseAll).</summary>
        void ReleaseAll();
        /// <summary>The hi-hat pedal (false without drums): a layer remembers it at its start.</summary>
        bool HatOpen { get; }
        /// <summary>The first-use tip (IGuide.Tip("tip_loop_start")): seconds to hold the clicks for Sunny's line, 0 = none.</summary>
        float StartTip();
        /// <summary>The event types' roles (null: the classic three's, EventRoles.Classic).</summary>
        IInstrumentRegistry Registry => null;
        /// <summary>The current value of a Sampled State type at a layer's start (the default: the hi-hat pedal from HatOpen).</summary>
        bool TrySample(string type, out TakeEvent e)
        {
            if (string.Equals(type, TakeEventType.Hat, StringComparison.Ordinal)) { e = TakeEvent.Hat(HatOpen); return true; }
            e = default;
            return false;
        }
        /// <summary>The looper should call Wake ahead of its events (the app: displayed instruments' sources resume).</summary>
        bool WantsWakes => false;
        /// <summary>An event of this type will be sent within WakeAhead (IAudioPresence.Wake of its instrument).</summary>
        void Wake(in TakeEvent e) { }
    }

    public sealed class Looper : ILooper
    {
        public const int MaxLayers = 6;
        public static readonly int[] TempoBpm = { 70, 90, 110 };
        public const int DefaultTempo = 1, DefaultBars = 2;
        /// <summary>Loop events are sent to the engine this far ahead on the audio clock (frame hitches up to here are inaudible).</summary>
        public const double LookAhead = 0.08;
        /// <summary>From LOOP (or PLAY) to the first click / the first pass.</summary>
        public const double LeadIn = 0.12;
        /// <summary>A layer's window opens this much before its bar, so a note played a hair early still belongs to it.</summary>
        public const double EarlyGrace = 0.03;
        /// <summary>Onsets this late (a long frame hitch) are dropped rather than played in a burst; releases always play.</summary>
        public const double MaxLate = 0.1;
        /// <summary>The clock jumped this far past what was sent (the app was paused): skip ahead, release everything.</summary>
        public const double StallReset = 0.5;
        /// <summary>LOOP with no note for this long ends the session.</summary>
        public const double FirstNoteTimeout = 20.0;
        /// <summary>A first note within this many beats of a click lands exactly on beat 1. First five (the integrator's
        /// decision): layer 1 starts on the first note, snapped to beat 1: any first note (the click nearest it is beat 1,
        /// at most half a beat away) lands exactly on it. Was 0.125 (1/8 beat; further off the note kept its place).</summary>
        public const double FirstSnapBeats = 0.5;
        /// <summary>Onsets this close after the first note move with it (a chord, two hands).</summary>
        public const double ChordSeconds = 0.03;
        public const float TidyStrength = 0.5f;
        /// <summary>A layer armed less than this many beats before the next pass waits for the one after.</summary>
        public const double MinArmBeats = 1.0;
        public const double ClearConfirmSeconds = 3.0, LimitFlashSeconds = 0.9;
        /// <summary>Clicks over the last bar before an armed layer (its count-down).</summary>
        public const int CountdownBeats = 4;
        /// <summary>A lane closed at a window's end (or by mute / undo) glides to level 0 over this long, then gets its Off.</summary>
        public const double CloseSpeed = 1.0 / 30.0;
        /// <summary>Wakes run this far ahead of the audio clock (platform.md 6.2: the sources resume at least 250 ms ahead).</summary>
        public const double WakeAhead = 0.3;

        readonly ILoopHost _host;
        EventRoles _roles;

        /// <summary>The event types' roles (from the host's registry; tests may set their own).</summary>
        public EventRoles Roles { get => _roles; set => _roles = value ?? EventRoles.Classic; }

        /// <summary>The audio clock minus this is when a live event was meant (what the player heard): output ring + HAL
        /// estimate (spec 17: 4 x 256 frames + ~10 ms). UNVERIFIED on the headset; 0 in exact-timing tests.</summary>
        public double InputLatency = 0.03;

        // settings for the next session (persisted by the module)
        public int TempoIndex { get; private set; } = DefaultTempo;
        public int BarsSetting { get; private set; } = DefaultBars;
        /// <summary>The click while looping (off by default; the first layer's clicks always run).</summary>
        public bool Metronome { get; private set; }
        /// <summary>The timing tidy: onsets move halfway toward the nearest 1/16 (on by default).</summary>
        public bool Tidy { get; set; } = true;

        public LoopState State { get; private set; }
        /// <summary>The session of the deck (the last one made) and one from Recordings while it plays (External).</summary>
        public LoopSession DeckSession { get; private set; }
        public LoopSession ExternalSession { get; private set; }
        /// <summary>The running session, else the deck's held one.</summary>
        public LoopSession Session => State != LoopState.Off ? _run : DeckSession;
        public bool External => State != LoopState.Off && ExternalSession != null && _run == ExternalSession;
        /// <summary>True while the looper calls an instrument (its events are playback, never recorded).</summary>
        public bool Dispatching { get; private set; }

        /// <summary>A change happened (phase, layer, on): the module publishes LoopEvent and the recorder's state.</summary>
        public event Action<LoopEventPhase, int, bool> Changed;
        /// <summary>State or layers changed (the recorder republishes its state; the deck redraws).</summary>
        public event Action StateChanged;
        /// <summary>A session finished with something new in it: save it (the module writes it to the library).</summary>
        public event Action<LoopSession> Save;
        /// <summary>Tempo, length, click or tidy changed (persist).</summary>
        public event Action SettingsChanged;

        LoopSession _run;          // the running session
        LoopSession _pending;      // Waiting: the new session (it becomes the deck's at the first note)
        int _sr;
        long _clickStart;          // Waiting: the first click (S)
        long _waitUntil;
        long _anchor;              // pass 0 of the run
        long _dispatched;          // everything before this sample has been sent
        long _woken;               // wakes were sent up to this sample
        long _lastClick = long.MinValue;
        long _snapUntil, _snapShift;
        bool _fresh;               // this run records layer 1 in pass 0 (a new session, not a replay)
        double _clearUntil = double.NegativeInfinity, _limitUntil = double.NegativeInfinity;
        readonly int[] _cursor = new int[MaxLayers];
        readonly int[] _end = new int[MaxLayers];

        public Looper(ILoopHost host)
        {
            _host = host;
            _roles = EventRoles.From(host.Registry);
        }

        // =====================================================================================================
        // Read-backs (deck lamps, tests, ILooper)
        // =====================================================================================================

        public bool Active => State != LoopState.Off;
        public int Layers => Session != null ? Session.Finished : 0;
        public int Bpm => Session != null && Active ? Session.Bpm : TempoBpm[TempoIndex];
        public int Bars => Session != null && Active ? Session.Bars : BarsSetting;
        public int SampleRate => _sr > 0 ? _sr : Rate();
        public long Anchor => _anchor;
        public long ClickStart => _clickStart;
        public long Dispatched => _dispatched;
        public bool ClearArmed => _host.Now < _clearUntil;
        public bool LimitFlash => _host.Now < _limitUntil;
        /// <summary>A layer waits for its pass (armed) or records it.</summary>
        public bool Armed { get { var l = Pending(); return l != null && Comp() < l.WindowStart; } }
        public bool Overdubbing { get { var l = Pending(); return l != null && State == LoopState.Looping && Comp() >= l.WindowStart; } }
        /// <summary>Every layer (finished, recording or armed) of the running session.</summary>
        public int LayerSlots => _run != null && Active ? _run.Layers.Count : Session != null ? Session.Layers.Count : 0;

        int Rate() { int r = _host.SampleRate; return r > 0 ? r : 48000; }
        long ToSamples(double s) => (long)Math.Round(s * _sr);
        double Seconds(long samples) => samples / (double)_sr;
        long NowSamples() => ToSamples(_host.Now);
        long Comp() => _sr > 0 ? ToSamples(_host.Now - InputLatency) : 0;

        LoopLayer Pending()
        {
            if (_run == null || !Active) return null;
            for (int i = 0; i < _run.Layers.Count; i++) if (_run.Layers[i].Recording) return _run.Layers[i];
            return null;
        }

        public LayerLamp Lamp(int i)
        {
            LoopSession s = Session;
            if (s == null || i < 0 || i >= s.Layers.Count) return LayerLamp.Off;
            LoopLayer l = s.Layers[i];
            if (l.Recording) return Comp() >= l.WindowStart && State != LoopState.Waiting ? LayerLamp.Recording : LayerLamp.Armed;
            return l.Muted ? LayerLamp.Muted : LayerLamp.On;
        }

        /// <summary>The four count lights: how many are lit, and whether they are bright (count-in / count-down) or dim
        /// (the running beat). Waiting: the running beat of the clicks. Armed: the beats left before the layer's bar.</summary>
        public int CountLights(out bool bright)
        {
            bright = false;
            if (!Active || _sr <= 0) return 0;
            long now = NowSamples();
            LoopSession s = _run;
            if (State == LoopState.Waiting)
            {
                if (now < _clickStart) { bright = true; return 0; }
                long j = (long)Math.Floor((now - _clickStart) / s.BeatSamples);
                bright = true;
                return 1 + (int)(j % LoopSession.BeatsPerBar);
            }
            LoopLayer p = Pending();
            if (p != null && State == LoopState.Looping && now < p.PassStart)
            {
                double left = (p.PassStart - now) / s.BeatSamples;
                if (left <= CountdownBeats)
                {
                    bright = true;
                    return Math.Max(1, (int)Math.Ceiling(left - 1e-9));
                }
            }
            if (now < _anchor) return 0;
            long inPass = (now - _anchor) % s.PassSamples;
            int beat = (int)Math.Floor(inPass / s.BeatSamples);
            return 1 + beat % LoopSession.BeatsPerBar;
        }

        /// <summary>Where the run is: pass index and beat in the pass (tests, the deck).</summary>
        public long PassAt(long sample) => _run == null ? 0 : (long)Math.Floor((sample - _anchor) / (double)_run.PassSamples);

        // =====================================================================================================
        // Settings (only between sessions)
        // =====================================================================================================

        public void SetTempo(int index)
        {
            index = Math.Max(0, Math.Min(TempoBpm.Length - 1, index));
            if (Active || index == TempoIndex) return;
            TempoIndex = index;
            SettingsChanged?.Invoke();
        }

        public void SetBars(int bars)
        {
            bars = bars >= 4 ? 4 : 2;
            if (Active || bars == BarsSetting) return;
            BarsSetting = bars;
            SettingsChanged?.Invoke();
        }

        public void ToggleMetronome() { Metronome = !Metronome; SettingsChanged?.Invoke(); }
        public void ToggleTidy() { Tidy = !Tidy; SettingsChanged?.Invoke(); }

        /// <summary>Restore persisted settings (no event).</summary>
        public void Load(int tempoIndex, int bars, bool metronome, bool tidy)
        {
            TempoIndex = Math.Max(0, Math.Min(TempoBpm.Length - 1, tempoIndex));
            BarsSetting = bars >= 4 ? 4 : 2;
            Metronome = metronome;
            Tidy = tidy;
        }

        /// <summary>ILooper.Configure: preset the next session (Sunny's "song in layers").</summary>
        public void Configure(int bpm, int bars, bool metronome)
        {
            if (Active) return;
            int best = 0;
            for (int i = 1; i < TempoBpm.Length; i++) if (Math.Abs(TempoBpm[i] - bpm) < Math.Abs(TempoBpm[best] - bpm)) best = i;
            TempoIndex = best;
            BarsSetting = bars >= 4 ? 4 : 2;
            Metronome = metronome;
            SettingsChanged?.Invoke();
        }

        /// <summary>ILooper.Status: where the session is (Sunny's walkthrough and tips read it).</summary>
        public LoopStatus Status
        {
            get
            {
                switch (State)
                {
                    case LoopState.Waiting: return LoopStatus.Waiting;
                    case LoopState.Recording: return LoopStatus.RecordingFirst;
                    case LoopState.Looping: return Overdubbing ? LoopStatus.Overdubbing : Armed ? LoopStatus.Armed : LoopStatus.Looping;
                    default: return LoopStatus.Off;
                }
            }
        }

        /// <summary>ILooper.Press: the deck presses the control (the module wires it to TapeDeck.Press).</summary>
        public Action<LoopControl, int> PressHandler;
        public void Press(LoopControl control, int layer = 0) => PressHandler?.Invoke(control, layer);

        /// <summary>Control anchors come from the deck (TapeDeck.TryGetControl); the module wires them.</summary>
        public Func<LoopControl, int, (bool ok, UnityEngine.Vector3 world, UnityEngine.Vector3 up)> Controls;

        public bool TryGetControl(LoopControl control, int layer, out UnityEngine.Vector3 world, out UnityEngine.Vector3 up)
        {
            world = up = default;
            if (Controls == null) return false;
            var r = Controls(control, layer);
            world = r.world;
            up = r.up;
            return r.ok;
        }

        // =====================================================================================================
        // The buttons
        // =====================================================================================================

        /// <summary>LOOP: off -> a new session (clicks, then the first note records layer 1); running -> arm the next
        /// layer (pressed again while armed: disarm).</summary>
        public void PressLoop()
        {
            if (State == LoopState.Off) { Begin(); return; }
            if (State == LoopState.Waiting) return;
            Arm();
        }

        void Begin()
        {
            _sr = Rate();
            _pending = new LoopSession(TempoBpm[TempoIndex], BarsSetting, _sr);
            _run = _pending;
            ExternalSession = null;
            long now = NowSamples();
            // Sunny's first-use line, said before the clicks so it never talks over a recording pass.
            float hold = _host.StartTip();
            _clickStart = now + ToSamples(LeadIn + Math.Max(0f, hold));
            _waitUntil = _clickStart + ToSamples(FirstNoteTimeout);
            _anchor = _clickStart;
            _dispatched = now;
            _woken = now;
            _lastClick = now;
            _clearUntil = double.NegativeInfinity;
            _fresh = true;
            SetState(LoopState.Waiting);
            Raise(LoopEventPhase.CountIn, -1);
        }

        /// <summary>REC / LOOP while the loop runs: the next pass records a new layer. Pressed while a layer is armed (not yet
        /// recording): disarms it.</summary>
        public void Arm()
        {
            if (State != LoopState.Recording && State != LoopState.Looping) return;
            LoopSession s = _run;
            LoopLayer last = s.Layers.Count > 0 ? s.Layers[s.Layers.Count - 1] : null;
            long comp = Comp();
            if (last != null && last.Recording && comp < last.WindowStart)
            {
                s.Layers.RemoveAt(s.Layers.Count - 1);   // armed, not started: LOOP again disarms
                Raise(LoopEventPhase.Undo, -1);
                return;
            }
            if (s.Layers.Count >= MaxLayers)
            {
                _limitUntil = _host.Now + LimitFlashSeconds;   // the six lights blink: no room for another
                return;
            }
            // The first pass whose start is at least MinArmBeats away and after every window still open.
            long now = NowSamples();
            long p = (long)Math.Floor((now - _anchor) / (double)s.PassSamples) + 1;
            if (p < 1) p = 1;
            while (_anchor + p * s.PassSamples - now < MinArmBeats * s.BeatSamples) p++;
            if (last != null && last.Recording)
                while (_anchor + p * s.PassSamples - ToSamples(EarlyGrace) < last.WindowEnd) p++;
            long ps = _anchor + p * s.PassSamples;
            var layer = new LoopLayer
            {
                Recording = true, StartPass = p + 1, PassStart = ps,
                WindowStart = ps - ToSamples(EarlyGrace), WindowEnd = ps - ToSamples(EarlyGrace) + s.PassSamples,
            };
            s.Layers.Add(layer);
            StateChanged?.Invoke();
        }

        /// <summary>UNDO: an armed or recording layer is dropped; else the last layer goes. The last one gone ends the session.</summary>
        public void Undo()
        {
            if (State == LoopState.Off) return;
            if (State == LoopState.Waiting) { Cancel(); return; }
            LoopSession s = _run;
            if (s.Layers.Count == 0) return;
            int i = s.Layers.Count - 1;
            LoopLayer l = s.Layers[i];
            ReleaseHeld(l, i);
            s.Layers.RemoveAt(i);
            if (!l.Recording) s.Dirty = true;
            if (s.Layers.Count == 0)
            {
                // Nothing left to loop: the session ends (unsaved: an empty loop is no recording).
                End(save: false);
                if (DeckSession == s) DeckSession = null;
                Raise(LoopEventPhase.Undo, -1);
                return;
            }
            Raise(LoopEventPhase.Undo, i);
        }

        /// <summary>CLEAR: the first tap arms (3 s, "tap again"); the second stops and erases the session (not saved).</summary>
        public void Clear()
        {
            if (Session == null && !Active) return;
            double now = _host.Now;
            if (now >= _clearUntil)
            {
                _clearUntil = now + ClearConfirmSeconds;
                Raise(LoopEventPhase.ClearArmed, -1);
                return;
            }
            _clearUntil = double.NegativeInfinity;
            LoopSession s = Session;
            if (Active) End(save: false);
            if (DeckSession == s) DeckSession = null;
            if (ExternalSession == s) ExternalSession = null;
            Raise(LoopEventPhase.Cleared, -1);
        }

        /// <summary>A layer light: mute / unmute a finished layer.</summary>
        public void ToggleMute(int i)
        {
            LoopSession s = Session;
            if (s == null || i < 0 || i >= s.Layers.Count || s.Layers[i].Recording) return;
            LoopLayer l = s.Layers[i];
            l.Muted = !l.Muted;
            if (l.Muted) ReleaseHeld(l, i);
            s.Dirty = true;
            Raise(LoopEventPhase.Muted, i, l.Muted);
        }

        /// <summary>STOP: looping ends; the session is saved when it has something new.</summary>
        public void Stop()
        {
            if (State == LoopState.Off) return;
            if (State == LoopState.Waiting) { Cancel(); return; }
            End(save: true);
        }

        /// <summary>PLAY with the deck's last session: it loops again from its first pass.</summary>
        public bool PlayDeck()
        {
            if (Active || DeckSession == null || DeckSession.Layers.Count == 0) return false;
            Run(DeckSession, external: false);
            return true;
        }

        /// <summary>A saved session from Recordings: it loops (the deck's own session waits and comes back after).</summary>
        public bool PlayExternal(List<TakeEvent> events)
        {
            _sr = Rate();
            LoopSession s = LoopFormat.Decode(events, _sr);
            if (s == null || s.Layers.Count == 0) return false;
            if (Active) End(save: true);
            ExternalSession = s;
            Run(s, external: true);
            return true;
        }

        void Run(LoopSession s, bool external)
        {
            _sr = s.SampleRate;
            _run = s;
            long now = NowSamples();
            _anchor = now + ToSamples(LeadIn);
            _dispatched = now;
            _woken = now;
            _lastClick = now;
            foreach (var l in s.Layers) { l.StartPass = 0; l.Recording = false; l.ClearState(); }
            _fresh = false;
            SetState(LoopState.Looping);
        }

        void Cancel()
        {
            _pending = null;
            _run = null;
            SetState(LoopState.Off);
            Raise(LoopEventPhase.Stopped, -1);
        }

        void End(bool save)
        {
            LoopSession s = _run;
            ReleaseAllHeld();
            // A layer still recording keeps what it has (STOP mid-pass); an armed one that never started is dropped.
            long comp = Comp();
            for (int i = s.Layers.Count - 1; i >= 0; i--)
            {
                LoopLayer l = s.Layers[i];
                if (!l.Recording) continue;
                if (comp > l.WindowStart && l.Notes.Count > 0) Finish(s, l, Math.Min(comp, l.WindowEnd));
                else s.Layers.RemoveAt(i);
            }
            bool external = ExternalSession == s;
            if (external) ExternalSession = null;
            _run = null;
            SetState(LoopState.Off);
            Raise(LoopEventPhase.Stopped, -1);
            if (save && s.Dirty && s.Layers.Count > 0)
            {
                s.Dirty = false;
                Save?.Invoke(s);
                Raise(LoopEventPhase.Saved, -1);
            }
        }

        // =====================================================================================================
        // Live events (Recorder.Add while a session runs)
        // =====================================================================================================

        bool IsOnset(string type) => _roles.RoleOf(type) == EventRole.Onset;

        /// <summary>A live event (never a dispatched one: the Recorder filters Dispatching). True when it went into the layer
        /// recording its pass (the Recorder announces it as a RecordedNoteEvent).</summary>
        public bool Capture(in TakeEvent e) => Capture(e, double.NaN);

        /// <summary>A live event stamped at an earlier audio time (IRecorder.Add(ev, at)); NaN: now. True when a recording
        /// layer's window took it.</summary>
        public bool Capture(in TakeEvent e, double atAudioTime)
        {
            if (State == LoopState.Off || Dispatching) return false;
            if (_roles.RoleOf(e.type) == EventRole.Marker) return false;   // a judgement, not a sound: never in a layer
            long t = double.IsNaN(atAudioTime) || _sr <= 0 ? Comp() : ToSamples(atAudioTime - InputLatency);
            if (State == LoopState.Waiting)
            {
                if (!IsOnset(e.type) || t < _clickStart - (long)(_run.BeatSamples * 0.5)) return false;
                StartFirstLayer(t);
            }
            LoopSession s = _run;
            for (int i = 0; i < s.Layers.Count; i++)
            {
                LoopLayer l = s.Layers[i];
                if (!l.Recording || t < l.WindowStart || t >= l.WindowEnd) continue;
                Add(s, l, e, t);
                return true;
            }
            return false;
        }

        // The first note: the loop starts on the click nearest it; within 1/8 beat the note (and a chord with it) lands on
        // beat 1 exactly. Its window opens at the note (or the early grace), one pass long.
        void StartFirstLayer(long t)
        {
            LoopSession s = _pending;
            double beat = s.BeatSamples;
            long j = Math.Max(0, (long)Math.Round((t - _clickStart) / beat));
            long a = _clickStart + (long)Math.Round(j * beat);
            long delta = t - a;
            _anchor = a;
            _snapShift = Math.Abs(delta) <= FirstSnapBeats * beat ? -delta : 0;
            _snapUntil = t + ToSamples(ChordSeconds);
            long start = Math.Min(t, a - ToSamples(EarlyGrace));
            var layer = new LoopLayer
            {
                Recording = true, StartPass = 1, PassStart = a, WindowStart = start, WindowEnd = start + s.PassSamples, Opened = true,
            };
            Sample(layer);
            s.Layers.Add(layer);
            DeckSession = s;
            _pending = null;
            SetState(LoopState.Recording);
            Raise(LoopEventPhase.Recording, 0);
        }

        void Add(LoopSession s, LoopLayer l, in TakeEvent e, long t)
        {
            long L = s.PassSamples;
            long off = Wrap(t - l.PassStart, L);
            EventTypeSpec spec = _roles.Spec(e.type);
            EventRole role = spec.Type != null ? spec.Role : EventRole.Onset;
            int owner = _roles.OwnerOf(e.type);
            TakeEvent ev = e;
            ev.t = 0f;
            switch (role)
            {
                case EventRole.Onset:
                {
                    long shifted = off;
                    if (_fresh && _snapShift != 0 && t <= _snapUntil && s.Layers[0] == l)
                        shifted = Wrap(off + _snapShift, L);
                    long q = Tidy ? Quantise(s, shifted, TidyStrength) : shifted;
                    bool lane = spec.Key == EventKey.Lane;
                    if ((spec.Held || lane) && EventRoles.Trackable(spec.Key, e))
                    {
                        // A key held down (the piano) or a lane starting: its release and its controls move with its shift.
                        EventRoles.KeyOf(spec.Key, e, out int value, out string text);
                        Put(l.Open, new OpenKey
                        {
                            Lane = lane, Owner = owner, Key = spec.Key, Int = value, Str = text, Shift = q - off,
                            Release = _roles.ReleaseOf(e.type), Control = _roles.ControlOf(e.type), U = e.u,
                        });
                    }
                    l.Insert(q, ev);
                    return;
                }
                case EventRole.Release:
                {
                    bool lane = spec.Key == EventKey.Lane;
                    if (_roles.EndsHeld(e.type) || lane)
                    {
                        if (!EventRoles.Trackable(spec.Key, e)) return;
                        EventRoles.KeyOf(spec.Key, e, out int value, out string text);
                        int k = LoopLayer.Find(l.Open, lane, owner, spec.Key, value, text);
                        if (k < 0) return;                                 // its key went down before this layer: not ours
                        long shift = l.Open[k].Shift;
                        l.Open.RemoveAt(k);
                        l.Insert(Wrap(off + shift, L), ev);
                        return;
                    }
                    l.Insert(off, ev);                                     // drumChoke, harpDamp: where they happened
                    return;
                }
                case EventRole.Control:
                {
                    // A continuous lane: never tidied; it follows its onset's shift, and sits DispatchLead early.
                    int k = LoopLayer.Find(l.Open, true, owner, EventKey.Lane, e.lane, null);
                    long shift = 0;
                    if (k < 0)
                        l.Open.Add(new OpenKey { Lane = true, Owner = owner, Key = EventKey.Lane, Int = e.lane, Release = _roles.ReleaseOf(e.type), Control = e.type, U = e.u });
                    else
                    {
                        OpenKey o = l.Open[k];
                        shift = o.Shift;
                        o.U = e.u;
                        o.Control = e.type;
                        l.Open[k] = o;
                    }
                    l.Insert(Wrap(off + shift - ToSamples(ControlEncoder.DispatchLead(e.speed)), L), ev);
                    return;
                }
                default:
                    l.Insert(off, ev);                                     // hat (State): where it happened
                    return;
            }
        }

        // Insertion sort (a handful of keys, no allocation): keys before lanes, then by instrument and key value.
        static void SortKeys(List<OpenKey> keys)
        {
            for (int i = 1; i < keys.Count; i++)
            {
                OpenKey x = keys[i];
                int j = i - 1;
                while (j >= 0 && Before(x, keys[j])) { keys[j + 1] = keys[j]; j--; }
                keys[j + 1] = x;
            }
        }

        static bool Before(in OpenKey a, in OpenKey b)
        {
            if (a.Lane != b.Lane) return !a.Lane;
            if (a.Owner != b.Owner) return a.Owner < b.Owner;
            if (a.Int != b.Int) return a.Int < b.Int;
            return string.CompareOrdinal(a.Str, b.Str) < 0;
        }

        static void Put(List<OpenKey> keys, in OpenKey k)
        {
            int i = LoopLayer.Find(keys, k.Lane, k.Owner, k.Key, k.Int, k.Str);
            if (i >= 0) keys[i] = k; else keys.Add(k);
        }

        // The Sampled State values at the layer's start (the hi-hat pedal).
        void Sample(LoopLayer l)
        {
            l.SampledStart.Clear();
            IReadOnlyList<string> types = _roles.Sampled;
            for (int i = 0; i < types.Count; i++)
            {
                if (!_host.TrySample(types[i], out TakeEvent e)) continue;
                e.type = types[i];
                e.t = 0f;
                l.SampledStart.Add(e);
            }
            l.SampledAtStart = true;
        }

        static long Wrap(long o, long L) { o %= L; return o < 0 ? o + L : o; }

        /// <summary>The timing tidy: an onset moves `strength` of the way to the nearest 1/16 of a beat-grid (a quarter beat),
        /// and never out of its bar (the bar's last sample at most).</summary>
        public static long Quantise(LoopSession s, long offset, float strength)
        {
            if (strength <= 0f) return offset;
            double grid = s.BeatSamples / 4.0;
            double g = Math.Round(offset / grid) * grid;
            double q = offset + Math.Min(1f, strength) * (g - offset);
            double bar = s.BarSamples;
            int b = (int)Math.Floor(offset / bar);
            if (b < 0) b = 0;
            if (b >= s.Bars) b = s.Bars - 1;
            long barStart = (long)Math.Round(b * bar), barEnd = (long)Math.Round((b + 1) * bar);
            long r = (long)Math.Round(q);
            if (r < barStart) r = barStart;
            if (r > barEnd - 1) r = barEnd - 1;
            return r;
        }

        // A recording layer's window closed: its notes still held get their release at the window's end, its open lanes a
        // final level-0 Control and their Off (struck notes ring on); a layer with hat moves starts from the pedal it had;
        // an empty overdub disappears (an empty first layer ends the session).
        void Finish(LoopSession s, LoopLayer l, long end)
        {
            long L = s.PassSamples;
            long offEnd = Wrap(end - 1 - l.PassStart, L);
            SortKeys(l.Open);                                              // keys in ascending order, as the piano's always were
            for (int k = 0; k < l.Open.Count; k++)
            {
                OpenKey o = l.Open[k];
                if (o.Lane)
                {
                    if (o.Control != null)
                        l.Insert(Wrap(offEnd - ToSamples(ControlEncoder.DispatchLead(CloseSpeed)), L), new TakeEvent { type = o.Control, lane = o.Int, u = o.U, vel = 0f, speed = (float)CloseSpeed });
                    if (o.Release != null) l.Insert(offEnd, new TakeEvent { type = o.Release, lane = o.Int, u = o.U });
                }
                else if (o.Release != null)
                {
                    var off = new TakeEvent { type = o.Release };
                    EventRoles.SetKey(ref off, o.Key, o.Int, o.Str);
                    l.Insert(offEnd, off);
                }
            }
            l.Open.Clear();
            if (l.SampledAtStart)
                for (int k = l.SampledStart.Count - 1; k >= 0; k--)
                    if (l.Has(l.SampledStart[k].type)) l.Insert(0, l.SampledStart[k], first: true);
            l.Recording = false;
            s.Dirty = true;
        }

        // =====================================================================================================
        // The frame (Recorder phase): finish windows, send what is due within LookAhead
        // =====================================================================================================

        public void Tick()
        {
            if (State == LoopState.Off) return;
            LoopSession s = _run;
            long now = NowSamples();
            long comp = Comp();

            if (State == LoopState.Waiting && now >= _waitUntil) { Cancel(); return; }

            // Windows that ended (the compensated clock passed them: no live event can still land in them).
            for (int i = 0; i < s.Layers.Count; i++)
            {
                LoopLayer l = s.Layers[i];
                if (!l.Recording) continue;
                if (!l.Opened && comp >= l.WindowStart)
                {
                    l.Opened = true;
                    Sample(l);
                    Raise(LoopEventPhase.Recording, i);
                }
                if (comp < l.WindowEnd) continue;
                Finish(s, l, l.WindowEnd);
                if (l.Notes.Count == 0)
                {
                    // Nothing was played in that pass: the layer disappears (its light goes out).
                    s.Layers.RemoveAt(i);
                    i--;
                    if (s.Layers.Count == 0) { End(save: false); if (DeckSession == s) DeckSession = null; return; }
                    StateChanged?.Invoke();
                    continue;
                }
                Raise(LoopEventPhase.LayerDone, i);
            }
            if (State == LoopState.Recording && now >= _anchor + s.PassSamples) SetState(LoopState.Looping);

            // The app was paused (headset off): skip what was missed, nothing held.
            if (_dispatched < now - ToSamples(StallReset))
            {
                ReleaseAllHeld();
                _dispatched = now;
                if (_lastClick < now) _lastClick = now;
            }

            // Displayed instruments whose events come within WakeAhead: their sources resume first.
            if (_host.WantsWakes && State != LoopState.Waiting)
            {
                long wakeTo = now + ToSamples(WakeAhead);
                if (_woken < now) _woken = now;
                if (wakeTo > _woken) { Wakes(s, _woken, wakeTo); _woken = wakeTo; }
            }

            long horizon = now + ToSamples(LookAhead);
            if (horizon <= _dispatched) return;
            Dispatching = true;
            try
            {
                Clicks(s, _dispatched, horizon);
                if (State != LoopState.Waiting) Events(s, _dispatched, horizon, now);
            }
            finally { Dispatching = false; }
            _dispatched = horizon;
        }

        // ---- the click: a cursor over beats, so a beat is never clicked twice when the grid gets its anchor ----

        void Clicks(LoopSession s, long lo, long hi)
        {
            long from = Math.Max(_lastClick, lo - 1);
            while (true)
            {
                long c = NextBeat(s, from, out bool accent, out bool sound);
                if (c >= hi) break;
                if (sound && c >= lo) _host.Click(accent, Seconds(c));
                _lastClick = c;
                from = c;
            }
        }

        // The first beat after `after`: the waiting clicks run on the grid from the first click; once the loop has an anchor
        // (a grid click), its beats are pass-relative (beat k of pass p at Anchor + p x Pass + round(k x Beat)).
        long NextBeat(LoopSession s, long after, out bool accent, out bool sound)
        {
            double beat = s.BeatSamples;
            accent = false;
            sound = false;
            if (State == LoopState.Waiting || after < _anchor)
            {
                long j = after < _clickStart ? 0 : (long)Math.Floor((after - _clickStart) / beat) + 1;
                long c = _clickStart + (long)Math.Round(j * beat);
                while (c <= after) { j++; c = _clickStart + (long)Math.Round(j * beat); }
                if (State == LoopState.Waiting || c < _anchor)
                {
                    sound = true;
                    return c;
                }
            }
            long p = (long)Math.Floor((after - _anchor) / (double)s.PassSamples);
            if (p < 0) p = 0;
            long ps = _anchor + p * s.PassSamples;
            int k = (int)Math.Floor((after - ps) / beat) + 1;
            if (k < 0) k = 0;
            long cc = ps + s.BeatOffset(k);
            while (cc <= after) { k++; cc = ps + s.BeatOffset(k); }
            if (k >= s.Beats) { p++; k = 0; ps = _anchor + p * s.PassSamples; cc = ps; }
            accent = k % LoopSession.BeatsPerBar == 0;
            // Layer 1's pass keeps the clicks; later passes click when the metronome is on, or over the last bar before an
            // armed layer (its count-down).
            sound = (_fresh && p == 0) || Metronome || CountdownAt(s, cc);
            return cc;
        }

        bool CountdownAt(LoopSession s, long c)
        {
            for (int i = 0; i < s.Layers.Count; i++)
            {
                LoopLayer l = s.Layers[i];
                if (!l.Recording || (i == 0 && _fresh)) continue;
                double before = (l.PassStart - c) / s.BeatSamples;
                if (before > 0.5 && before < CountdownBeats + 0.5) return true;
            }
            return false;
        }

        // ---- the events: every pass the range touches, all layers merged in time order ----

        void Events(LoopSession s, long lo, long hi, long now)
        {
            long L = s.PassSamples;
            long pFirst = (long)Math.Floor((lo - _anchor) / (double)L), pLast = (long)Math.Floor((hi - 1 - _anchor) / (double)L);
            if (pFirst < 0) pFirst = 0;
            int n = Math.Min(s.Layers.Count, MaxLayers);
            long late = now - ToSamples(MaxLate);
            for (long p = pFirst; p <= pLast; p++)
            {
                long ps = _anchor + p * L;
                long a = Math.Max(lo, ps) - ps, b = Math.Min(hi, ps + L) - ps;
                if (b <= a) continue;
                for (int i = 0; i < n; i++)
                {
                    LoopLayer l = s.Layers[i];
                    if (p < l.StartPass) { _cursor[i] = _end[i] = 0; continue; }
                    _cursor[i] = l.LowerBound(a);
                    _end[i] = l.LowerBound(b);
                }
                while (true)
                {
                    int best = -1;
                    long bo = long.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        if (_cursor[i] >= _end[i]) continue;
                        long o = s.Layers[i].Notes[_cursor[i]].Offset;
                        if (o < bo) { bo = o; best = i; }
                    }
                    if (best < 0) break;
                    LoopLayer l = s.Layers[best];
                    LoopNote note = l.Notes[_cursor[best]++];
                    Send(l, best, note.Event, ps + note.Offset, late);
                }
            }
        }

        /// <summary>Read-ahead (Recorder.Upcoming, the ghost hands): the events the looping layers will send in the audio-time
        /// range [from, to), all layers merged in time order as Events sends them (a layer from its first pass on; a muted
        /// layer's releases only), with their lanes and audio times. Nothing while the clicks wait for the first note.
        /// Allocation-free.</summary>
        public int Upcoming(double from, double to, TakeEvent[] events, double[] when)
        {
            if (State == LoopState.Off || State == LoopState.Waiting || _run == null || _sr <= 0) return 0;
            LoopSession s = _run;
            long lo = (long)Math.Ceiling(from * _sr), hi = (long)Math.Ceiling(to * _sr);
            if (hi <= lo) return 0;
            long L = s.PassSamples;
            if (L <= 0) return 0;
            long pFirst = (long)Math.Floor((lo - _anchor) / (double)L), pLast = (long)Math.Floor((hi - 1 - _anchor) / (double)L);
            if (pFirst < 0) pFirst = 0;
            int n = Math.Min(s.Layers.Count, MaxLayers), max = Math.Min(events.Length, when.Length), c = 0;
            Span<int> cursor = stackalloc int[MaxLayers], end = stackalloc int[MaxLayers];
            for (long p = pFirst; p <= pLast && c < max; p++)
            {
                long ps = _anchor + p * L;
                long a = Math.Max(lo, ps) - ps, b = Math.Min(hi, ps + L) - ps;
                if (b <= a) continue;
                for (int i = 0; i < n; i++)
                {
                    LoopLayer l = s.Layers[i];
                    if (p < l.StartPass) { cursor[i] = end[i] = 0; continue; }
                    cursor[i] = l.LowerBound(a);
                    end[i] = l.LowerBound(b);
                }
                while (c < max)
                {
                    int best = -1;
                    long bo = long.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        if (cursor[i] >= end[i]) continue;
                        long o = s.Layers[i].Notes[cursor[i]].Offset;
                        if (o < bo) { bo = o; best = i; }
                    }
                    if (best < 0) break;
                    LoopLayer l = s.Layers[best];
                    LoopNote note = l.Notes[cursor[best]++];
                    EventTypeSpec spec = _roles.Spec(note.Event.type);
                    EventRole role = spec.Type != null ? spec.Role : EventRole.Onset;
                    if (role == EventRole.Marker || (l.Muted && role != EventRole.Release)) continue;
                    TakeEvent ev = note.Event;
                    ev.lane = PlaybackLanes.Layer(best);
                    events[c] = ev;
                    when[c] = Seconds(ps + note.Offset);
                    c++;
                }
            }
            return c;
        }

        // The same passes and layers as Events, without sending: each event within the range wakes its instrument.
        void Wakes(LoopSession s, long lo, long hi)
        {
            long L = s.PassSamples;
            long pFirst = (long)Math.Floor((lo - _anchor) / (double)L), pLast = (long)Math.Floor((hi - 1 - _anchor) / (double)L);
            if (pFirst < 0) pFirst = 0;
            int n = Math.Min(s.Layers.Count, MaxLayers);
            for (long p = pFirst; p <= pLast; p++)
            {
                long ps = _anchor + p * L;
                long a = Math.Max(lo, ps) - ps, b = Math.Min(hi, ps + L) - ps;
                if (b <= a) continue;
                for (int i = 0; i < n; i++)
                {
                    LoopLayer l = s.Layers[i];
                    if (p < l.StartPass || l.Muted) continue;
                    for (int k = l.LowerBound(a); k < l.Notes.Count && l.Notes[k].Offset < b; k++) _host.Wake(l.Notes[k].Event);
                }
            }
        }

        void Send(LoopLayer l, int index, in TakeEvent e, long at, long late)
        {
            EventTypeSpec spec = _roles.Spec(e.type);
            EventRole role = spec.Type != null ? spec.Role : EventRole.Onset;
            int owner = _roles.OwnerOf(e.type);
            TakeEvent ev = e;
            ev.lane = PlaybackLanes.Layer(index);
            switch (role)
            {
                case EventRole.Release when _roles.EndsHeld(e.type) || spec.Key == EventKey.Lane:
                {
                    // Only what this layer pressed (a key, a lane), and always, even late or muted.
                    if (!EventRoles.Trackable(spec.Key, e)) return;
                    EventRoles.KeyOf(spec.Key, e, out int value, out string text);
                    int k = LoopLayer.Find(l.Held, spec.Key == EventKey.Lane, owner, spec.Key, value, text);
                    if (k < 0) return;
                    l.Held.RemoveAt(k);
                    break;
                }
                case EventRole.Control:
                {
                    if (l.Muted) return;                                    // never dropped as late: the next one corrects it
                    if (LoopLayer.Find(l.Held, true, owner, EventKey.Lane, e.lane, null) < 0)
                        l.Held.Add(new OpenKey { Lane = true, Owner = owner, Key = EventKey.Lane, Int = e.lane, Release = _roles.ReleaseOf(e.type), Control = e.type, U = e.u });
                    break;
                }
                case EventRole.Onset:
                {
                    if (l.Muted) return;
                    if (at < late) return;                                  // a hitch: skip the onset rather than burst
                    bool lane = spec.Key == EventKey.Lane;
                    if ((spec.Held || lane) && EventRoles.Trackable(spec.Key, e))
                    {
                        EventRoles.KeyOf(spec.Key, e, out int value, out string text);
                        if (LoopLayer.Find(l.Held, lane, owner, spec.Key, value, text) < 0)
                            l.Held.Add(new OpenKey { Lane = lane, Owner = owner, Key = spec.Key, Int = value, Str = text, Release = _roles.ReleaseOf(e.type), Control = _roles.ControlOf(e.type), U = e.u });
                    }
                    break;
                }
                default:
                    if (l.Muted) return;
                    if (at < late) return;
                    break;
            }
            _host.Dispatch(ev, Seconds(at));
        }

        // Everything this layer's playback holds goes up now: its keys get their release, its lanes a final level-0 Control
        // and their Off, on the layer's own lane.
        void ReleaseHeld(LoopLayer l, int index)
        {
            Dispatching = true;
            try
            {
                int lane = PlaybackLanes.Layer(index);
                SortKeys(l.Held);
                for (int k = 0; k < l.Held.Count; k++)
                {
                    OpenKey o = l.Held[k];
                    if (o.Lane)
                    {
                        if (o.Control != null) _host.Dispatch(new TakeEvent { type = o.Control, lane = lane, u = o.U, vel = 0f, speed = (float)CloseSpeed }, 0);
                        if (o.Release != null) _host.Dispatch(new TakeEvent { type = o.Release, lane = lane, u = o.U }, 0);
                        continue;
                    }
                    if (o.Release == null) continue;
                    var off = new TakeEvent { type = o.Release, lane = lane };
                    EventRoles.SetKey(ref off, o.Key, o.Int, o.Str);
                    _host.Dispatch(off, 0);
                }
                l.Held.Clear();
            }
            finally { Dispatching = false; }
        }

        /// <summary>The audio clock jumped by <paramref name="deltaSeconds"/> (the output restarted: AudioSettings.Reset, a
        /// new device; negative when it restarted from 0). The Tick only skips forward stalls (StallReset); a jump either way
        /// moves the whole schedule with the clock (the click grid, the layers' windows, the wait), lets go of every key and
        /// lane the loop held (what was sent before the jump is never heard right) and starts sending again from now.</summary>
        public void ClockJumped(double deltaSeconds)
        {
            if (State == LoopState.Off || _sr <= 0) return;
            long d = ToSamples(deltaSeconds);
            _clickStart += d;
            _waitUntil += d;
            _anchor += d;
            _snapUntil += d;
            if (_lastClick != long.MinValue) _lastClick += d;
            if (_run != null)
                foreach (LoopLayer l in _run.Layers) { l.WindowStart += d; l.WindowEnd += d; l.PassStart += d; }
            long now = NowSamples();
            _dispatched = now;
            _woken = now;
            if (_lastClick != long.MinValue && _lastClick < now - 1) _lastClick = now - 1;
            ReleaseAllHeld();
            ClockJumps++;
        }

        /// <summary>Clock jumps the loop followed (tests).</summary>
        public int ClockJumps { get; private set; }

        void ReleaseAllHeld()
        {
            if (_run != null) foreach (var l in _run.Layers) l.Held.Clear();
            Dispatching = true;
            try { _host.ReleaseAll(); }
            finally { Dispatching = false; }
        }

        void SetState(LoopState s)
        {
            State = s;
            StateChanged?.Invoke();
        }

        void Raise(LoopEventPhase phase, int layer, bool on = false)
        {
            Changed?.Invoke(phase, layer, on);
            StateChanged?.Invoke();
        }
    }
}
