// AudioPresence (12-new-instruments/platform.md 10.1, Core's IAudioPresence): a displayed instrument's sources pause
// (AudioSource.Pause; Unity still calls a paused source's filter, so the SynthSource skips its own work too: PresencePaused)
// once it has sounded nothing for 1 s, and resume at the start of a
// bring glide (InstrumentMoveEvent Leaving, also Arrived) or on Wake(kind) (the playback dispatch calls it 0.25 s or more
// ahead). At-seat and gliding instruments never pause (unpausing costs a block and the spatializer may ramp); the Ui and
// Guide sources belong to no instrument and never pause, so the graph is always triggered. "Displayed" is
// ILayoutService.PlaceOf == Display or Hidden ("not at the seat": Hidden is a rig the layout keeps out, or MR); the
// sources are each descriptor's Audio.Sources, the classic instruments' included (a displayed kit pauses its 9).
// It also keeps, per instrument, when it last sounded (its unit's channels or any of its sources' output), was played live
// and was played back (RecorderEvent), which UnitGovernor reads. Main thread; allocation-free per
// frame; names no instrument (the registry).
// A hidden instrument still sounds (2026-11-11: a take with the piano played silent in mixed reality while only the drums
// were in front): a source sits under its instrument, so when the layout hides the instrument's group (mixed reality off
// the seat, a blocked display spot) the source went inactive and nothing it was sent was ever heard. Such a source waits on
// the audio host where it last was (as the kit's sources always did, DrumsModule's park) and goes back under its
// instrument, at its own place, as soon as the instrument shows again. Every recorded note is heard whatever the layout.
using System;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Audio
{
    /// <summary>What UnitGovernor reads about each instrument (AudioPresence; fakes in tests).</summary>
    public interface IInstrumentActivity
    {
        /// <summary>Where it is (ILayoutService.PlaceOf; Seat without a layout).</summary>
        InstrumentPlace PlaceOf(InstrumentKind kind);
        /// <summary>Seconds since it last sounded (its unit's channels or its sources), since it was played live, since a
        /// playback event of it or a Wake (float.MaxValue: never).</summary>
        float SecondsSinceSounded(InstrumentKind kind);
        float SecondsSinceLive(InstrumentKind kind);
        float SecondsSincePlayback(InstrumentKind kind);
    }

    public sealed unsafe class AudioPresence : IAudioPresence, IInstrumentActivity
    {
        /// <summary>Silent this long on display: the sources pause.</summary>
        public const float PauseAfterSeconds = 1f;
        /// <summary>A Wake, a bring or an arrival keeps the sources running at least this long.</summary>
        public const float WakeHoldSeconds = 1f;
        const int Kinds = 64;
        const double Never = -1e9;

        readonly AudioEngine _engine;
        App _app;
        IInstrumentRegistry _registry;
        ILayoutService _layout;
        int _servicesVersion = -1;
        readonly InstrumentDescriptor[] _desc = new InstrumentDescriptor[Kinds];
        readonly AudioSource[][] _audio = new AudioSource[Kinds][];
        readonly SynthSource[][] _synth = new SynthSource[Kinds][];
        readonly InstrumentKind[] _kinds = new InstrumentKind[Kinds];
        int _kindCount;
        readonly double[] _lastSounded = new double[Kinds], _lastLive = new double[Kinds], _lastPlayback = new double[Kinds], _awakeUntil = new double[Kinds];
        readonly float[] _silentFor = new float[Kinds];
        readonly bool[] _paused = new bool[Kinds], _sounding = new bool[Kinds];
        // A source parked on the audio host while its instrument is hidden: where it came from (null: not parked by us).
        readonly Transform[][] _parkedFrom = new Transform[Kinds][];
        readonly Vector3[][] _homeLocal = new Vector3[Kinds][];
        readonly Quaternion[][] _homeRot = new Quaternion[Kinds][];
        Transform _park;
        double _time;
        Action<RecorderEvent> _onRecorder;
        Action<InstrumentMoveEvent> _onMove;

        public AudioPresence(AudioEngine engine)
        {
            _engine = engine;
            for (int i = 0; i < Kinds; i++) { _lastSounded[i] = _lastLive[i] = _lastPlayback[i] = Never; _awakeUntil[i] = Never; }
        }

        /// <summary>Seconds on the presence clock (the module's frame time).</summary>
        public double Time => _time;
        /// <summary>Times the sources of an instrument were paused / resumed (stats, tests).</summary>
        public int Pauses { get; private set; }
        public int Resumes { get; private set; }
        /// <summary>Sources parked on the audio host because their instrument was hidden, and put back (stats, tests).</summary>
        public int Parks { get; private set; }
        public int Unparks { get; private set; }
        /// <summary>The source is parked on the audio host (its instrument is hidden) (tests).</summary>
        public bool IsParked(InstrumentKind kind, int source) =>
            Valid(kind) && _parkedFrom[(int)kind] != null && (uint)source < (uint)_parkedFrom[(int)kind].Length && _parkedFrom[(int)kind][source] != null;

        /// <summary>Main thread, once (AudioModule.Init): the instruments of the registry and their sources; the events.</summary>
        public void Bind(App app)
        {
            _app = app;
            _registry = app?.Registry;
            _kindCount = 0;
            if (_registry != null)
            {
                var all = _registry.All;
                for (int i = 0; i < all.Count; i++)
                {
                    InstrumentDescriptor d = all[i];
                    int k = (int)d.Kind;
                    if (k < 0 || k >= Kinds || _desc[k] != null) continue;
                    _desc[k] = d;
                    _kinds[_kindCount++] = d.Kind;
                    var specs = d.Audio?.Sources ?? Array.Empty<AudioSourceSpec>();
                    _audio[k] = new AudioSource[specs.Length];
                    _synth[k] = new SynthSource[specs.Length];
                    _parkedFrom[k] = new Transform[specs.Length];
                    _homeLocal[k] = new Vector3[specs.Length];
                    _homeRot[k] = new Quaternion[specs.Length];
                    for (int j = 0; j < specs.Length; j++)
                    {
                        SynthSource s = (uint)specs[j].Source < (uint)SourceId.Count ? _engine?.Source((SourceId)specs[j].Source) : null;
                        _synth[k][j] = s;
                        if (s != null) s.TryGetComponent(out _audio[k][j]);
                    }
                }
            }
            _park = _engine != null ? _engine.transform : null;
            if (app == null) return;
            _onRecorder = OnRecorder;
            _onMove = OnMove;
            app.Events.Subscribe(_onRecorder);
            app.Events.Subscribe(_onMove);
        }

        public void Unbind()
        {
            if (_app == null) return;
            if (_onRecorder != null) _app.Events.Unsubscribe(_onRecorder);
            if (_onMove != null) _app.Events.Unsubscribe(_onMove);
            _app = null;
        }

        ILayoutService Layout
        {
            get
            {
                if (_app == null) return null;
                if (_app.ServicesVersion != _servicesVersion) { _servicesVersion = _app.ServicesVersion; _layout = _app.Get<ILayoutService>(); }
                return _layout;
            }
        }

        // ------------------------------------------------------------------ reads (UnitGovernor)

        static bool Valid(InstrumentKind k) => (int)k >= 0 && (int)k < Kinds;

        /// <summary>Where the instrument is (the layout's PlaceOf; Seat without a layout).</summary>
        public InstrumentPlace PlaceOf(InstrumentKind kind)
        {
            ILayoutService layout = Layout;
            return layout != null ? layout.PlaceOf(kind) : InstrumentPlace.Seat;
        }

        /// <summary>It sounded in the last frame (its unit's output channels or its sources).</summary>
        public bool Sounding(InstrumentKind kind) => Valid(kind) && _sounding[(int)kind];
        public float SecondsSinceSounded(InstrumentKind kind) => Since(_lastSounded, kind);
        public float SecondsSinceLive(InstrumentKind kind) => Since(_lastLive, kind);
        public float SecondsSincePlayback(InstrumentKind kind) => Since(_lastPlayback, kind);
        float Since(double[] at, InstrumentKind kind) => Valid(kind) && at[(int)kind] > Never ? (float)(_time - at[(int)kind]) : float.MaxValue;

        // ------------------------------------------------------------------ IAudioPresence

        public bool Paused(InstrumentKind kind) => Valid(kind) && _paused[(int)kind];

        /// <summary>A take or loop event of the instrument will be dispatched soon: its sources run now and for at least
        /// WakeHoldSeconds.</summary>
        public void Wake(InstrumentKind kind)
        {
            if (!Valid(kind)) return;
            int k = (int)kind;
            _lastPlayback[k] = _time;
            Hold(k);
        }

        void Hold(int k)
        {
            _awakeUntil[k] = _time + WakeHoldSeconds;
            _silentFor[k] = 0f;
            if (_paused[k]) Resume(k);
        }

        void OnRecorder(RecorderEvent e)
        {
            if (_registry == null || e.Event.type == null) return;
            InstrumentKind kind = _registry.OwnerOfEvent(e.Event.type);
            if (!Valid(kind) || _desc[(int)kind] == null) return;
            int k = (int)kind;
            if (e.DuringPlayback) _lastPlayback[k] = _time; else _lastLive[k] = _time;
        }

        void OnMove(InstrumentMoveEvent e)
        {
            if (!Valid(e.Kind) || _desc[(int)e.Kind] == null) return;
            if (e.Phase == MovePhase.Leaving || e.Phase == MovePhase.Arrived) Hold((int)e.Kind);
        }

        // ------------------------------------------------------------------ the frame

        /// <summary>Main thread, every frame (AudioModule, end of frame): who sounds, then pause or resume.</summary>
        public void Tick(float dt)
        {
            _time += dt;
            ILayoutService layout = Layout;
            for (int i = 0; i < _kindCount; i++)
            {
                InstrumentKind kind = _kinds[i];
                int k = (int)kind;
                KeepAudible(k);
                bool sounding = UnitSounding(_desc[k]) || SourcesSounding(k);
                _sounding[k] = sounding;
                if (sounding) _lastSounded[k] = _time;
                InstrumentPlace place = layout != null ? layout.PlaceOf(kind) : InstrumentPlace.Seat;
                bool displayed = place == InstrumentPlace.Display || place == InstrumentPlace.Hidden;
                if (!displayed || sounding || _time < _awakeUntil[k] || (_registry != null && !_registry.IsLoaded(kind)))
                {
                    _silentFor[k] = 0f;
                    if (_paused[k]) Resume(k);
                    continue;
                }
                _silentFor[k] += dt;
                if (_paused[k]) KeepPaused(k);
                else if (_silentFor[k] >= PauseAfterSeconds) Pause(k);
            }
        }

        /// <summary>A source whose instrument's group was hidden waits on the audio host at its last world pose (it stays
        /// active, so it is heard); back under its instrument at its own local pose when that shows again. A source its
        /// instrument re-parented itself meanwhile (the kit parks its own) is left to it.</summary>
        void KeepAudible(int k)
        {
            SynthSource[] s = _synth[k];
            Transform[] from = _parkedFrom[k];
            if (s == null || from == null || _park == null) return;
            for (int j = 0; j < s.Length; j++)
            {
                SynthSource src = s[j];
                if (src == null) continue;
                Transform t = src.transform;
                if (from[j] != null)
                {
                    if (t.parent != _park) { from[j] = null; continue; }   // its instrument took it back itself
                    if (!from[j].gameObject.activeInHierarchy) continue;
                    t.SetParent(from[j], false);
                    t.localPosition = _homeLocal[k][j];
                    t.localRotation = _homeRot[k][j];
                    from[j] = null;
                    Unparks++;
                    continue;
                }
                GameObject go = src.gameObject;
                Transform parent = t.parent;
                if (!go.activeSelf || go.activeInHierarchy || parent == null || parent == _park) continue;
                from[j] = parent;
                _homeLocal[k][j] = t.localPosition;
                _homeRot[k][j] = t.localRotation;
                t.SetParent(_park, true);
                Parks++;
            }
        }

        bool UnitSounding(InstrumentDescriptor d)
        {
            AudioGraphHost graph = _engine != null ? _engine.Graph : null;
            if (graph == null || graph.IsDisposed || d?.Audio == null || d.Audio.Unit >= (byte)UnitId.Count) return false;
            UnitSlot* u = graph.Unit((UnitId)d.Audio.Unit);
            if (u == null || System.Threading.Volatile.Read(ref u->Registered) == 0 || System.Threading.Volatile.Read(ref u->Suspended) != 0) return false;
            return System.Threading.Volatile.Read(ref u->ChannelsUsed) != 0;
        }

        bool SourcesSounding(int k)
        {
            if (_paused[k]) return false;   // a paused source's level is its last block's
            SynthSource[] s = _synth[k];
            if (s == null) return false;
            for (int j = 0; j < s.Length; j++) if (s[j] != null && s[j].Rms > 0f) return true;
            return false;
        }

        void Pause(int k)
        {
            AudioSource[] a = _audio[k];
            if (a != null)
                for (int j = 0; j < a.Length; j++)
                {
                    if (_synth[k][j] != null) _synth[k][j].PresencePaused = true;
                    if (a[j] != null && a[j].isActiveAndEnabled && a[j].isPlaying) a[j].Pause();
                }
            _paused[k] = true;
            Pauses++;
        }

        /// <summary>A paused source that was started again from outside (its object deactivated and back: SynthSource.OnEnable
        /// plays it) pauses again.</summary>
        void KeepPaused(int k)
        {
            AudioSource[] a = _audio[k];
            if (a == null) return;
            for (int j = 0; j < a.Length; j++)
                if (a[j] != null && a[j].isActiveAndEnabled && a[j].isPlaying) a[j].Pause();
        }

        void Resume(int k)
        {
            AudioSource[] a = _audio[k];
            if (a != null)
                for (int j = 0; j < a.Length; j++)
                {
                    if (_synth[k][j] != null) _synth[k][j].PresencePaused = false;
                    if (a[j] == null || !a[j].isActiveAndEnabled) continue;
                    a[j].UnPause();
                    if (!a[j].isPlaying) a[j].Play();
                }
            _paused[k] = false;
            Resumes++;
        }

        /// <summary>Every paused source plays again (shutdown, tests).</summary>
        public void ResumeAll()
        {
            for (int i = 0; i < _kindCount; i++) { int k = (int)_kinds[i]; if (_paused[k]) Resume(k); }
        }
    }
}
