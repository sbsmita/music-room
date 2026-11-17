// Settings that persist between launches. Keys are fixed here so every module reads the same names. The values live in
// an ISettingsStore: PlayerPrefs in the app, an in-memory store in tests (BootOptions.Settings), so a PlayMode boot never
// reads or writes the developer's real preferences (~/Library/Preferences/unity.<company>.<product>.plist in the editor).
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    /// <summary>Where AppSettings keeps its values. Implementations need not be thread-safe (main thread only).</summary>
    public interface ISettingsStore
    {
        bool HasKey(string key);
        int GetInt(string key, int def);
        void SetInt(string key, int value);
        string GetString(string key, string def);
        void SetString(string key, string value);
        void DeleteKey(string key);
        void Save();
    }

    /// <summary>The app's store: UnityEngine.PlayerPrefs.</summary>
    public sealed class PlayerPrefsSettingsStore : ISettingsStore
    {
        public bool HasKey(string key) => PlayerPrefs.HasKey(key);
        public int GetInt(string key, int def) => PlayerPrefs.GetInt(key, def);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public string GetString(string key, string def) => PlayerPrefs.GetString(key, def);
        public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
        public void DeleteKey(string key) => PlayerPrefs.DeleteKey(key);
        public void Save() => PlayerPrefs.Save();
    }

    /// <summary>Tests: a fresh dictionary per boot (nothing persists, nothing leaks between tests or machines).</summary>
    public sealed class MemorySettingsStore : ISettingsStore
    {
        readonly Dictionary<string, int> _ints = new Dictionary<string, int>();
        readonly Dictionary<string, string> _strings = new Dictionary<string, string>();
        public int Saves { get; private set; }
        public bool HasKey(string key) => _ints.ContainsKey(key) || _strings.ContainsKey(key);
        public int GetInt(string key, int def) => _ints.TryGetValue(key, out int v) ? v : def;
        public void SetInt(string key, int value) { _strings.Remove(key); _ints[key] = value; }
        public string GetString(string key, string def) => _strings.TryGetValue(key, out string v) ? v : def;
        public void SetString(string key, string value) { _ints.Remove(key); _strings[key] = value; }
        public void DeleteKey(string key) { _ints.Remove(key); _strings.Remove(key); }
        public void Save() => Saves++;
    }

    public sealed class AppSettings
    {
        public const string KeyTourDone = "musicroom.tourDone";            // 1 once Sunny's tour finished or was dismissed
        public const string KeyLayoutMode = "musicroom.layoutMode";        // "all" | "single"
        public const string KeyCurrentInstrument = "musicroom.current";    // any registry id ("piano" when unknown or unloaded)
        /// <summary>The old "Lock instruments" (1 = locked). Gone since 2026-11-10: "Move instruments" (off by default) is the
        /// one setting; a save that still has it is cleaned up once (Layout's ComfortCodec.MigrateMoveSetting).</summary>
        public const string KeyLockedOld = "musicroom.locked";
        /// <summary>"Move instruments": 1 = on (the instruments are quiet and a pinch on one picks it up), missing or 0 = off.</summary>
        public const string KeyMoveInstruments = "musicroom.moveInstruments";
        public const string KeyNextTakeNumber = "musicroom.nextTakeNumber"; // the take library's own counter (also in index.json)
        /// <summary>The old "Classic sound" switch (1 = today's sounds, 0 = the upgraded ones). Gone since 2026-11-10: the piano,
        /// drums, harp and room always play the classic sound; a save that still has it is cleaned up once (AudioEngine.Init).</summary>
        public const string KeyClassicSoundOld = "musicroom.classicSound";
        /// <summary>The player's own placement of a layout, relative to the seat (Layout's ArrangementCodec). One key per
        /// layout: "musicroom.arrange.all", "musicroom.arrange.single.&lt;id&gt;" for every registry id.</summary>
        public const string KeyArrangePrefix = "musicroom.arrange.";
        /// <summary>My set (platform.md 4.7): registry ids in planner order, "piano,drums,harp" when missing.</summary>
        public const string KeySet = "musicroom.set";
        /// <summary>A displayed instrument the player moved in arrange mode (VR): "musicroom.stow.&lt;id&gt;" = "x,z,yaw" (room).</summary>
        public const string KeyStowPrefix = "musicroom.stow.";
        /// <summary>The player's comfort tweaks of a layout (Layout's ComfortCodec): per instrument, its offset from the default
        /// pose in front of the player (distance, side, height, yaw). One key per layout, like the arrangement it replaces:
        /// "musicroom.comfort.all", "musicroom.comfort.single.&lt;id&gt;". An old "musicroom.arrange.*" placement is turned into
        /// offsets the first time its layout is placed, then deleted.</summary>
        public const string KeyComfortPrefix = "musicroom.comfort.";
        /// <summary>"Play in my room" shows the player's real hands (the passthrough cut-out) instead of the drawn ones: 1.
        /// Missing (every save made before the switch existed) reads as 0: the drawn hands, as in "Play here".</summary>
        public const string KeyMrRealHands = "musicroom.mrRealHands";
        /// <summary>"Keep painting" (the menu's row on a recording's page): 1 = the notes' painting on the walls stays after a
        /// playback until the next one; missing or 0 = it fades away a few seconds after the playback ends (Room's WallPaint).</summary>
        public const string KeyKeepPaintings = "musicroom.keepPaintings";
        /// <summary>"Look and pinch" (the menu's Sunny page): 1 or missing = on (look at a menu row or an instrument on
        /// display, pinch to choose it; the Gaze module); 0 = off.</summary>
        public const string KeyLookPinch = "musicroom.lookPinch";

        /// <summary>The registry this boot (set by App before any module's Init), for the id strings. Null: the classic three.</summary>
        public IInstrumentRegistry Registry { get; set; }

        /// <summary>The persisted id of a kind (the id block, InstrumentIds); unknown kinds read as "piano".</summary>
        public static string IdOf(InstrumentKind k) => InstrumentIds.Of(k) ?? "piano";

        public static string ArrangeKey(LayoutMode mode, InstrumentKind current) =>
            mode == LayoutMode.All ? KeyArrangePrefix + "all" : KeyArrangePrefix + "single." + IdOf(current);

        public static string ComfortKey(LayoutMode mode, InstrumentKind current) =>
            mode == LayoutMode.All ? KeyComfortPrefix + "all" : KeyComfortPrefix + "single." + IdOf(current);

        public ISettingsStore Store { get; }

        /// <summary>The comfort offsets of a layout ("" when none).</summary>
        public string GetComfort(LayoutMode mode, InstrumentKind current) => GetString(ComfortKey(mode, current), "");

        public void SetComfort(LayoutMode mode, InstrumentKind current, string value)
        {
            string key = ComfortKey(mode, current);
            if (string.IsNullOrEmpty(value)) Store.DeleteKey(key);
            else SetString(key, value);
        }

        /// <summary>"Real hands" in "Play in my room" (the menu row). False by default and for old saves.</summary>
        public bool MrRealHands { get => GetBool(KeyMrRealHands, false); set => SetBool(KeyMrRealHands, value); }

        /// <summary>"Keep painting": off by default and for old saves.</summary>
        public bool KeepPaintings { get => GetBool(KeyKeepPaintings, false); set => SetBool(KeyKeepPaintings, value); }

        /// <summary>"Look and pinch": on by default and for old saves.</summary>
        public bool LookAndPinch { get => GetBool(KeyLookPinch, true); set => SetBool(KeyLookPinch, value); }

        /// <summary>The saved placement of a layout as older versions wrote it ("" when none): read once to migrate it into
        /// comfort offsets (ComfortKey), then deleted.</summary>
        public string GetArrangement(LayoutMode mode, InstrumentKind current) => GetString(ArrangeKey(mode, current), "");

        public void SetArrangement(LayoutMode mode, InstrumentKind current, string value)
        {
            string key = ArrangeKey(mode, current);
            if (string.IsNullOrEmpty(value)) Store.DeleteKey(key);
            else SetString(key, value);
        }

        public AppSettings(ISettingsStore store = null) { Store = store ?? new PlayerPrefsSettingsStore(); }

        public bool TourDone { get => GetBool(KeyTourDone, false); set => SetBool(KeyTourDone, value); }
        public LayoutMode LayoutMode
        {
            get => GetString(KeyLayoutMode, "all") == "single" ? LayoutMode.Single : LayoutMode.All;
            set => SetString(KeyLayoutMode, value == LayoutMode.Single ? "single" : "all");
        }
        /// <summary>musicroom.current: any registry id; unknown or unloaded ids read as the piano.</summary>
        public InstrumentKind CurrentInstrument
        {
            get
            {
                string id = GetString(KeyCurrentInstrument, "piano");
                if (Registry != null)
                    return Registry.TryParse(id, out InstrumentKind k) && Registry.IsLoaded(k) && Registry.Get(k).Playable ? k : InstrumentKind.Piano;
                return InstrumentIds.TryParse(id, out InstrumentKind c) && InstrumentIds.IsPlayable(c) ? c : InstrumentKind.Piano;
            }
            set => SetString(KeyCurrentInstrument, IdOf(value));
        }

        /// <summary>musicroom.set as written (ids; the Layout drops unknown ones and re-plans). Default "piano,drums,harp".</summary>
        public string SetIds { get => GetString(KeySet, MySetRules.Default); set => SetString(KeySet, value); }

        /// <summary>A displayed instrument's moved spot ("" = the plan's spot).</summary>
        public string GetStow(string id) => GetString(KeyStowPrefix + id, "");
        public void SetStow(string id, string value)
        {
            if (string.IsNullOrEmpty(value)) Store.DeleteKey(KeyStowPrefix + id);
            else SetString(KeyStowPrefix + id, value);
        }
        /// <summary>"Move instruments" (the menu's setting). Off by default and for every old save.</summary>
        public bool MoveInstruments { get => GetBool(KeyMoveInstruments, false); set => SetBool(KeyMoveInstruments, value); }

        public bool GetBool(string key, bool def) => Store.GetInt(key, def ? 1 : 0) != 0;
        public void SetBool(string key, bool v) => Store.SetInt(key, v ? 1 : 0);
        public int GetInt(string key, int def) => Store.GetInt(key, def);
        public void SetInt(string key, int v) => Store.SetInt(key, v);
        public string GetString(string key, string def) => Store.GetString(key, def);
        public void SetString(string key, string v) => Store.SetString(key, v);
        public void Save() => Store.Save();
    }
}
