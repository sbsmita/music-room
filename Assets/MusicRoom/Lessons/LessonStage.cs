// What the tutorial needs from the rest of the app (spec 08 section 2.3): the three instrument APIs for the classic
// paths' block geometry, whether each instrument is on show (blocks go with their instrument), and the eyes (harp
// capsules come in along the line of sight); for every other instrument (platform.md 7.2-7.3) its descriptor, its
// registered ILessonTargets (and ILessonAccompany), the layout that brings it to the seat and the audio engine for a
// piano accompaniment. One seam, so EditMode tests drive the tutorial with fake instruments and no App.
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Lessons
{
    public interface ILessonStage
    {
        IPianoApi Piano { get; }
        IDrumsApi Drums { get; }
        IHarpApi Harp { get; }
        /// <summary>A block shows only while its instrument does: false while the instrument is out of the scene,
        /// hidden, or the mixed-reality placing hides the rigs.</summary>
        bool IsShown(InstrumentKind instrument);
        /// <summary>World position of the eyes (centre eye).</summary>
        Vector3 EyePosition { get; }

        // ---- the platform (default bodies: a stage without them runs the classic paths only) ----

        /// <summary>Lesson "instrument" ids and descriptors (null: the id block).</summary>
        IInstrumentRegistry Registry => null;
        /// <summary>The instrument's descriptor (its LessonSpec, event types).</summary>
        InstrumentDescriptor Describe(InstrumentKind instrument) => Registry?.Get(instrument);
        /// <summary>The instrument's registered ILessonTargets (null: none; its targets are passed over).</summary>
        ILessonTargets TargetsFor(InstrumentKind instrument) => null;
        /// <summary>The instrument's own accompaniment (null: the piano sound plays the chords).</summary>
        ILessonAccompany AccompanyFor(InstrumentKind instrument) => null;
        /// <summary>Brings the lesson's instrument to the seat (Bring) and says where it is (null: always there).</summary>
        ILayoutService Layout => null;
        /// <summary>The piano accompaniment's sound (null: no accompaniment without an ILessonAccompany).</summary>
        IAudioEngine Audio => null;
    }

    /// <summary>The app's stage: APIs resolved lazily (the instrument modules have lower Orders, MR a higher one), cached;
    /// the per-instrument lookups are rebuilt only when the app's registrations change (App.ServicesVersion).</summary>
    public sealed class AppLessonStage : ILessonStage
    {
        readonly App _app;
        IPianoApi _piano;
        IDrumsApi _drums;
        IHarpApi _harp;
        IMixedReality _mr;
        ILayoutService _layout;
        IAudioEngine _audio;
        int _version = -1;
        readonly IInstrument[] _instrument = new IInstrument[InstrumentIds.KindCount];
        readonly ILessonTargets[] _targets = new ILessonTargets[InstrumentIds.KindCount];
        readonly ILessonAccompany[] _accompany = new ILessonAccompany[InstrumentIds.KindCount];

        public AppLessonStage(App app) { _app = app; }

        public IPianoApi Piano => _piano ?? (_piano = _app.Get<IPianoApi>());
        public IDrumsApi Drums => _drums ?? (_drums = _app.Get<IDrumsApi>());
        public IHarpApi Harp => _harp ?? (_harp = _app.Get<IHarpApi>());
        public Vector3 EyePosition => _app.Head != null ? _app.Head.position : new Vector3(0f, 1.2f, -0.1f);
        public IInstrumentRegistry Registry => _app.Registry;
        public InstrumentDescriptor Describe(InstrumentKind instrument) => _app.Registry?.Get(instrument);
        public ILayoutService Layout => _layout ?? (_layout = _app.Get<ILayoutService>());
        public IAudioEngine Audio => _audio ?? (_audio = _app.Get<IAudioEngine>());

        public ILessonTargets TargetsFor(InstrumentKind instrument) { Refresh(); int i = (int)instrument; return i >= 0 && i < _targets.Length ? _targets[i] : null; }
        public ILessonAccompany AccompanyFor(InstrumentKind instrument) { Refresh(); int i = (int)instrument; return i >= 0 && i < _accompany.Length ? _accompany[i] : null; }

        public bool IsShown(InstrumentKind instrument)
        {
            Refresh();
            int i = (int)instrument;
            Transform root = i >= 0 && i < _instrument.Length ? _instrument[i]?.Root : null;
            if (root == null || !root.gameObject.activeInHierarchy) return false;
            if (_mr == null) _mr = _app.Get<IMixedReality>();
            return _mr == null || !_mr.IsPlacing;          // MR placing hides the piano (and the other rigs)
        }

        // The instruments by kind (each kind's own IInstrument; the deck, the switch panel and the menu are not playable).
        void Refresh()
        {
            if (_version == _app.ServicesVersion) return;
            _version = _app.ServicesVersion;
            System.Array.Clear(_instrument, 0, _instrument.Length);
            System.Array.Clear(_targets, 0, _targets.Length);
            System.Array.Clear(_accompany, 0, _accompany.Length);
            foreach (IInstrument inst in _app.Instruments)
            {
                int i = (int)inst.Kind;
                if (InstrumentIds.IsPlayable(inst.Kind) && i < _instrument.Length && _instrument[i] == null) _instrument[i] = inst;
            }
            foreach (ILessonTargets t in _app.GetAll<ILessonTargets>()) { int i = (int)t.Kind; if (i >= 0 && i < _targets.Length && _targets[i] == null) _targets[i] = t; }
            foreach (ILessonAccompany a in _app.GetAll<ILessonAccompany>()) { int i = (int)a.Kind; if (i >= 0 && i < _accompany.Length && _accompany[i] == null) _accompany[i] = a; }
        }
    }
}
