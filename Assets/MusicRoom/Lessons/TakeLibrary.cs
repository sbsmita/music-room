// Saved takes under Application.persistentDataPath/takes (spec 08 section 4.4): index.json {version, nextNumber,
// takes[]} and <id>.json per take; files are written to *.tmp then moved. JsonUtility serialises TakeRecord /
// TakeEvent (flat fields; it writes every field, so a take round-trips losslessly). Serialising a take (up to 40000
// events, platform.md 6.5) and every file write run on a worker (Task.Run), so the frame STOP is pressed in never waits on
// them (JsonUtility on a worker: UNVERIFIED under IL2CPP, device item 52; if it throws there, the take is serialised on
// the main thread instead and still written on the worker);
// continuations return to the main thread (Unity's synchronisation context), where the index is kept. The index JSON is
// small and is built on the main thread (it reads the live index); its writes run one after another on the worker, so
// two saves never race on index.json.tmp.
// Quirks fixed (spec 08 section 8): take numbers persist (nextNumber, never reused after a delete), and the number is
// reserved when the take is named, so two takes stopped before the first write finishes never share one.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Lessons
{
    public sealed class TakeLibrary : ITakeLibrary
    {
        [Serializable] class Index { public int version = 1; public int nextNumber = 1; public List<TakeInfo> takes = new List<TakeInfo>(); }

        readonly string _dir;
        readonly System.Random _random = new System.Random();   // ids only: never disturb UnityEngine.Random's sequence
        Index _index;
        public bool? Available { get; private set; }
        public int Version { get; private set; }
        public event Action Changed;
        /// <summary>Raised with the new nextNumber whenever it moves (the module mirrors it to AppSettings.KeyNextTakeNumber).</summary>
        public event Action<int> NextNumberChanged;
        public int NextNumber => _index?.nextNumber ?? 1;
        public string Folder => _dir;
        /// <summary>Where a take is serialised and written (spec 08 section 4.4: "do the IO in Task.Run"). Tests may hold the
        /// work back to show that nothing of it runs in the calling frame.</summary>
        public Func<Action, Task> Worker { get; set; } = RunOnPool;

        static Task RunOnPool(Action work) => Task.Run(work);

        public TakeLibrary(string directory = null)
        {
            _dir = directory ?? Path.Combine(Application.persistentDataPath, "takes");
            try
            {
                Directory.CreateDirectory(_dir);
                string probe = Path.Combine(_dir, ".probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                string ip = IndexPath;
                _index = File.Exists(ip) ? JsonUtility.FromJson<Index>(File.ReadAllText(ip)) ?? new Index() : new Index();
                if (_index.takes == null) _index.takes = new List<TakeInfo>();
                // nextNumber persists (quirk fix 08 section 8.4): at least highest existing + 1.
                foreach (var t in _index.takes) { int n = ParseNumber(t.name); if (n >= _index.nextNumber) _index.nextNumber = n + 1; }
                Available = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Takes] storage unavailable: " + e.Message);
                Available = false;
            }
        }

        string IndexPath => Path.Combine(_dir, "index.json");
        string TakePath(string id) => Path.Combine(_dir, id + ".json");

        /// <summary>list(): every take without its events, newest first.</summary>
        public Task<List<TakeInfo>> List()
        {
            var list = new List<TakeInfo>(_index?.takes ?? new List<TakeInfo>());
            list.Sort((a, b) => b.created.CompareTo(a.created));
            return Task.FromResult(list);
        }

        public async Task<TakeRecord> Get(string id)
        {
            if (Available != true || string.IsNullOrEmpty(id)) return null;
            string path = TakePath(id);
            try { return await Task.Run(() => File.Exists(path) ? JsonUtility.FromJson<TakeRecord>(File.ReadAllText(path)) : null); }
            catch { return null; }
        }

        public async Task<TakeRecord> Put(TakeRecord take)
        {
            if (Available != true || take == null) return null;
            if (string.IsNullOrEmpty(take.id)) take.id = NewId();
            if (string.IsNullOrEmpty(take.name)) take.name = "Take";
            if (take.created == 0) take.created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (take.events == null) take.events = new List<TakeEvent>();
            string path = TakePath(take.id);
            try
            {
                // Serialising up to 40000 events (several MB) happens on the worker too, never in the frame STOP is pressed:
                // JsonUtility may be called from background threads, and the caller hands the record over (Recorder.OnTake
                // passes a copy of its events). Should the worker's JsonUtility throw (UNVERIFIED on IL2CPP), the main thread
                // serialises it and the worker still writes it.
                try { await Worker(() => WriteAtomic(path, JsonUtility.ToJson(take))); }
                catch (Exception e) when (!(e is IOException) && !(e is UnauthorizedAccessException))
                {
                    Debug.LogWarning("[Takes] serialising on the worker failed (" + e.Message + "): serialised on the main thread");
                    string json = JsonUtility.ToJson(take);
                    await Worker(() => WriteAtomic(path, json));
                }
                _index.takes.RemoveAll(t => t.id == take.id);
                _index.takes.Add(new TakeInfo { id = take.id, name = take.name, created = take.created, seconds = take.seconds, count = EventCount(take.events) });
                int n = ParseNumber(take.name);
                if (n >= _index.nextNumber) SetNextNumber(n + 1);
                await SaveIndex();
                return take;
            }
            catch (Exception e) { Debug.LogWarning("[Takes] put failed: " + e.Message); return null; }
        }

        /// <summary>Raised at the start of Remove(id), before anything changes (synchronously in the caller's frame): the
        /// Lessons module lets go of a loop session saved under that id, so a later save never brings it back.</summary>
        public event Action<string> Removing;

        /// <summary>A saved recording's name by its id (null when there is none): the log of a recording on top.</summary>
        public string NameOf(string id)
        {
            if (_index?.takes == null || string.IsNullOrEmpty(id)) return null;
            foreach (TakeInfo t in _index.takes) if (t.id == id) return t.name;
            return null;
        }

        public async Task<bool> Remove(string id)
        {
            if (Available != true || string.IsNullOrEmpty(id)) return false;
            try { Removing?.Invoke(id); }
            catch (Exception e) { Debug.LogWarning("[Takes] a Removing handler failed: " + e.Message); }
            string path = TakePath(id);
            try
            {
                await Task.Run(() => { if (File.Exists(path)) File.Delete(path); });
                bool removed = _index.takes.RemoveAll(t => t.id == id) > 0;
                await SaveIndex();
                return removed;
            }
            catch { return false; }
        }

        /// <summary>"Take N · m:ss" (spec 08 section 4.4 auto-save; U+00B7 between spaces). Peeks: does not reserve N.</summary>
        public string NextName(double seconds) => $"Take {NextNumber} · {FormatLength(seconds)}";

        /// <summary>The auto-save name, reserving N at once (synchronously): two takes that stop
        /// before the first file write finishes never share a number. A failed write leaves a gap, never a repeat.</summary>
        public string ReserveName(double seconds) => $"Take {ReserveNumber()} · {FormatLength(seconds)}";

        /// <summary>The next recording number, reserved at once (takes and loop sessions share the count: "Take 3",
        /// "Loop 4").</summary>
        public int ReserveNumber()
        {
            int n = NextNumber;
            if (_index != null) SetNextNumber(n + 1);
            return n;
        }

        /// <summary>Played events of a record (a loop session's header and layer marks are not events).</summary>
        public static int EventCount(List<TakeEvent> events)
        {
            if (events == null) return 0;
            if (!LoopFormat.IsLoop(events)) return events.Count;
            int n = 0;
            foreach (var e in events) if (e.type != LoopFormat.Header && e.type != LoopFormat.LayerMark) n++;
            return n;
        }

        /// <summary>formatLength: s = round(max(0, sec)), "m:ss" (so never "0:60", spec 08 section 8.3).</summary>
        public string FormatLength(double seconds)
        {
            int s = (int)Math.Floor(Math.Max(0, seconds) + 0.5);    // JS Math.round: halves round up
            return $"{s / 60}:{s % 60:00}";
        }

        /// <summary>The headset's local time, friendly: "Today 3:42 PM", "Yesterday 9:10 AM", "Nov 8, 4:05 PM" (Core
        /// LocalTime: on the device the platform's own time zone, not TimeZoneInfo.Local, which could read UTC there).</summary>
        public string FormatDate(long createdMs) => LocalTime.Friendly(createdMs);

        /// <summary>put: "take-" + base36(ms) + "-" + base36(rand 0..999999).</summary>
        public string NewId()
        {
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int r = _random.Next(0, 1000000);
            return "take-" + Base36(ms) + "-" + Base36(r);
        }

        public static string Base36(long v)
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            if (v == 0) return "0";
            var sb = new System.Text.StringBuilder();
            while (v > 0) { sb.Insert(0, digits[(int)(v % 36)]); v /= 36; }
            return sb.ToString();
        }

        /// <summary>N of "Take N · ..." or "Loop N · ..." (0 for any other name).</summary>
        public static int ParseNumber(string name)
        {
            if (string.IsNullOrEmpty(name) || !(name.StartsWith("Take ", StringComparison.Ordinal) || name.StartsWith("Loop ", StringComparison.Ordinal))) return 0;
            int end = 5;
            while (end < name.Length && char.IsDigit(name[end])) end++;
            return int.TryParse(name.Substring(5, end - 5), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;
        }

        void SetNextNumber(int n)
        {
            _index.nextNumber = n;
            NextNumberChanged?.Invoke(n);
        }

        Task _indexWrite = Task.CompletedTask;

        // The in-memory index changes (and Changed fires) at once; the file follows on the worker, after any earlier
        // index write, so the last state written is the last state made.
        Task SaveIndex()
        {
            string json = JsonUtility.ToJson(_index);
            string path = IndexPath;
            Task previous = _indexWrite;
            _indexWrite = Task.Run(async () =>
            {
                try { await previous.ConfigureAwait(false); } catch { /* an earlier failure was reported by its own save */ }
                WriteAtomic(path, json);
            });
            Version++;
            Changed?.Invoke();
            return _indexWrite;
        }

        static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
