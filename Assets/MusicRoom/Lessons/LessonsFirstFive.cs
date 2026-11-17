// The Lessons module's first-five-minutes parts (Docs/11-first-five/tutorials-audit.md B6, sunny-tour.md 2.5), kept
// in their own module (Order 41, after LessonsModule) so LessonsModule.cs stays as it was:
//   - the first run's song (Resources/lessons-firstrun.json: the answering phrase of Ode to Joy with soft chords), added
//     to the tutorial as a hidden lesson (Start("piano-ode-first") finds it; the Lessons menu never lists it);
//   (its chords are the platform tutorial's Accompany option: the piano sound, no key moves, nothing recorded);
//   - LessonProgress (ILessonProgress): every finished lesson is remembered (musicroom.lessonsDone) with its fewest
//     mistakes (musicroom.lessonsBest), and the menu's "Next" row asks it for the next lesson.
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Lessons
{
    public sealed class LessonsFirstFive : AppModule
    {
        public const int ModuleOrder = LessonsModule.ModuleOrder + 1;
        public override int Order => ModuleOrder;
        public const string FirstRunResource = "lessons-firstrun";
        /// <summary>The first run's song (sunny-tour.md 2.5).</summary>
        public const string FirstRunLessonId = "piano-ode-first";

        App _app;
        Tutorial _tutorial;
        Action<TutorialEvent> _onTutorial;

        public LessonProgress Progress { get; private set; }
        public Lesson FirstRunLesson { get; private set; }

        public override void Init(App app)
        {
            _app = app;
            LessonsModule lessons = null;
            foreach (IAppModule m in app.Modules) if (m is LessonsModule l) lessons = l;
            _tutorial = lessons?.Tutorial;
            Progress = new LessonProgress(app.Settings, lessons?.Library);
            app.Register<ILessonProgress>(Progress);
            FirstRunLesson = LoadFirstRun();
            if (_tutorial != null)
            {
                if (FirstRunLesson != null) _tutorial.AddHidden(FirstRunLesson);
                app.Register<ILessonLive>(_tutorial);
            }
            _onTutorial = OnTutorial;
            app.Events.Subscribe(_onTutorial);
        }

        public override void Shutdown()
        {
            if (_app == null) return;
            _app.Events.Unsubscribe(_onTutorial);
        }

        public static Lesson LoadFirstRun()
        {
            var text = Resources.Load<TextAsset>(FirstRunResource);
            if (text == null) { Debug.LogWarning("[Lessons] Resources/lessons-firstrun.json missing: the first run has no song"); return null; }
            LessonLibrary lib = LessonLibrary.Parse(text.text);
            return lib.Get(FirstRunLessonId);
        }

        void OnTutorial(TutorialEvent e)
        {
            if (e.State != TutorialState.Done || _tutorial == null || !_tutorial.HasSummary) return;
            Progress.Record(e.LessonId, _tutorial.Summary.Mistakes);
        }
    }

    /// <summary>Finished lessons and their fewest mistakes, in the injected settings store (PlayerPrefs in the app).</summary>
    public sealed class LessonProgress : ILessonProgress
    {
        public const string KeyDone = "musicroom.lessonsDone", KeyBest = "musicroom.lessonsBest";

        readonly AppSettings _settings;
        readonly ILessonLibrary _library;
        readonly Dictionary<string, int> _best = new Dictionary<string, int>(StringComparer.Ordinal);

        public LessonProgress(AppSettings settings, ILessonLibrary library)
        {
            _settings = settings;
            _library = library;
            Load();
        }

        void Load()
        {
            _best.Clear();
            string done = _settings?.GetString(KeyDone, "") ?? "";
            foreach (string id in done.Split(',')) if (id.Length > 0) _best[id] = -1;
            string best = _settings?.GetString(KeyBest, "") ?? "";
            foreach (string pair in best.Split(','))
            {
                int c = pair.LastIndexOf(':');
                if (c <= 0 || !int.TryParse(pair.Substring(c + 1), out int n)) continue;
                _best[pair.Substring(0, c)] = n;
            }
        }

        public int DoneCount => _best.Count;
        public bool IsDone(string lessonId) => lessonId != null && _best.ContainsKey(lessonId);
        public int BestMistakes(string lessonId) => lessonId != null && _best.TryGetValue(lessonId, out int n) ? n : -1;

        /// <summary>A lesson was finished with this many mistakes (saved at once: the store flushes).</summary>
        public void Record(string lessonId, int mistakes)
        {
            if (string.IsNullOrEmpty(lessonId) || _settings == null) return;
            int old = BestMistakes(lessonId);
            if (IsDone(lessonId) && old >= 0 && old <= mistakes) return;
            _best[lessonId] = Math.Max(0, mistakes);
            var ids = new System.Text.StringBuilder();
            var best = new System.Text.StringBuilder();
            foreach (var kv in _best)
            {
                if (ids.Length > 0) { ids.Append(','); best.Append(','); }
                ids.Append(kv.Key);
                best.Append(kv.Key).Append(':').Append(Math.Max(0, kv.Value));
            }
            _settings.SetString(KeyDone, ids.ToString());
            _settings.SetString(KeyBest, best.ToString());
            _settings.Save();
            Debug.Log($"[Lessons] {lessonId} done ({mistakes} mistakes; {DoneCount} lessons done)");
        }

        /// <summary>The next lesson of the same instrument in the library's (= the menu's) order; null after the last.</summary>
        public string NextLessonId(string lessonId)
        {
            if (_library == null || lessonId == null) return null;
            Lesson cur = _library.Get(lessonId);
            if (cur == null) return null;
            IReadOnlyList<Lesson> all = _library.Lessons;
            bool after = false;
            for (int i = 0; i < all.Count; i++)
            {
                Lesson l = all[i];
                if (l == null) continue;
                if (after && l.instrument == cur.instrument) return l.id;
                if (ReferenceEquals(l, cur)) after = true;
            }
            return null;
        }
    }
}
