// Sunny's script: the 22 tour and tip lines plus the 16 later lines (the "Move instruments" tip, tip_arrange; the
// looper's first-use tip, tip_loop_start; the tape deck walkthrough's lines), the five tour steps and every timing
// constant (Docs/08-ux-lessons-menu-guide.md 3.1-3.4).
// The bubble captions come from this table and the voice WAVs in Resources/Voice were rendered from the same text, so
// GuideLinesTests compares every entry with Resources/guide-lines.json (the frozen copy of the script) and fails on drift.
// The many-instrument platform (12-new-instruments/platform.md 8.2): every module's Resources/GuideLines/<Module>.json
// ({"lines":[{"id","text","spoken"}]}) and Resources/VoiceManifests/<Module>.json (the shape of Voice/manifest.json:
// {"<id>": {"file", "seconds", "rate"}}) are merged after this table (GuideLineTable, GuideLines.Merged): a duplicate id
// is an error and the later one is dropped. Ids / Texts / ManifestSeconds stay the Guide's own 38 (= GuideVoice.LineIds).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Guide
{
    // DemoKind moved to Core (Contracts/Guide.cs: the platform's tip data names it): None, Palm, Poke, Swipe, Nod, Press keep
    // their values; Press: the ghost index finger presses a control at a world anchor (the deck walkthrough).

    /// <summary>The five tour steps in order (Docs/08-ux-lessons-menu-guide.md 3.3).</summary>
    public enum StepId : byte { Open = 0, Poke, Close, Swipe, Nod }

    public readonly struct StepDef
    {
        public readonly StepId Id;
        public readonly DemoKind Demo;
        public readonly string Vr, Desk;
        /// <summary>Seconds before the step is skipped (0 = waits for the gesture forever).</summary>
        public readonly float Timeout;
        /// <summary>Line said when the step times out (null = "skip").</summary>
        public readonly string Skip;

        public StepDef(StepId id, DemoKind demo, string vr, string desk, float timeout, string skip)
        {
            Id = id; Demo = demo; Vr = vr; Desk = desk; Timeout = timeout; Skip = skip;
        }
    }

    public static class GuideLines
    {
        public const int Count = 38;

        /// <summary>Stable ids, in the order of guide-lines.json (also IAudioEngine's voice slots, GuideVoice.LineIds).</summary>
        public static readonly string[] Ids =
        {
            "intro_vr", "intro_desk", "open_vr", "open_desk", "poke_vr", "poke_desk", "close_vr", "close_desk",
            "swipe_vr", "swipe_desk", "swipe_skip", "nod_vr", "nod_desk", "nod_skip", "skip",
            "praise_0", "praise_1", "praise_2", "praise_3", "praise_4", "bye", "leave",
            // Unity only, since the first headset test: the first time "Move instruments" turns on (GuideModule).
            "tip_arrange",
            // Unity only, the looper: its first-use tip and the tape deck walkthrough (DeckGuide)
            "tip_loop_start",
            "deck_guide_intro", "deck_guide_rec", "deck_guide_stop", "deck_guide_auto", "deck_guide_saved", "deck_guide_keep",
            "tip_take", "lay_drums", "lay_harp", "lay_piano", "lay_done", "tip_loop_mute", "tip_loop_undo", "again",
        };

        /// <summary>Captions (guide-lines.json text; no line has its own spoken text, so the recording says exactly this).</summary>
        public static readonly string[] Texts =
        {
            "Hi, I'm Sunny! Let me show you around.",              // first five: the replay tour's intro (sunny-tour.md 5.1)
            "Hi, I'm Sunny! Let me show you around.",
            "Hold your open palm toward your face to open the menu",
            "Press M to open the menu (in VR, show your palm like this)",
            "Poke a row with your index finger",
            "Click a row in the menu (in VR, poke it like this)",
            "Show your palm again to hide it",
            "Press M again to hide it (in VR, your palm again)",
            "Swipe your open hand sideways to turn to another instrument",
            "In VR, swipe an open hand sideways like this to turn to another instrument",
            "Or pick an instrument in the menu.",                   // first five (sunny-tour.md 5.1)
            "Nod your head to play the kick drum",
            "Click the kick pad beside the snare (in VR, nod like this)",
            "The kick pad by the snare plays it too",
            "Let us move on",
            "Nice!",
            "Great!",
            "You got it!",
            "Perfect!",
            "Lovely!",
            "Have fun!",
            "Bye!",
            "Pinch an instrument and drag it where you like.",    // tip_arrange (2026-11-10: Move instruments is a setting; was "Pinch an instrument to pick it up.")
            "Listen to the count, then play. It will repeat.",   // tip_loop_start (Docs/11-first-five/sunny-tour.md 5.3)
            // the tape deck walkthrough (DeckGuide): six new lines, then the tour spec's recorder and looper lines
            "Let me show you the tape deck. Follow my hand.", "Press REC, then play a few notes.", "Now press STOP.",
            "I'll press it for you.", "Your songs are saved in Recordings, in your palm menu.",
            "Keep this loop, or press CLEAR twice to erase it.",
            "Saved! Press PLAY to hear it.", "First a beat: press LOOP, then tap the drums.", "Now press LOOP again and sweep the harp.",
            "Last layer: press LOOP and play the piano.", "That's your own song, in layers!", "Touch a layer's light to mute it.",
            "UNDO takes away the last layer.", "Like this.",
        };

        /// <summary>Dry recording lengths from the voice manifest (Resources/Voice/manifest.json). Used only when the audio
        /// engine cannot report a clip's length itself (no engine, clip missing).</summary>
        public static readonly float[] ManifestSeconds =
        {
            3.102f, 3.102f, 3.46f, 4.255f, 2.051f, 3.59f, 1.901f, 3.589f,
            3.752f, 5.033f, 1.966f, 2.199f, 4.267f, 2.372f, 1.213f,
            0.539f, 0.444f, 0.763f, 0.686f, 0.572f, 0.752f, 0.471f,
            2.896f,
            3.416f,
            3.178f, 2.368f, 1.115f, 1.172f, 3.447f, 3.155f,
            2.394f, 3.534f, 2.695f, 3.038f, 2.151f, 1.807f, 1.963f, 0.783f,
        };

        /// <summary>One praise per step, picked as Praise[step % 5].</summary>
        public static readonly string[] Praise = { "praise_0", "praise_1", "praise_2", "praise_3", "praise_4" };

        public const string Skip = "skip", Bye = "bye", Leave = "leave", IntroVr = "intro_vr", IntroDesk = "intro_desk";
        /// <summary>The "Move instruments" tip (sunny-tour.md 3.2 / 5.3): said once, the first time arrange mode turns on.</summary>
        public const string TipArrange = "tip_arrange";

        /// <summary>experience.md 14 item 4: the one text hint (no recording, a caption only) when the room is too dim for
        /// fast hand tracking (HandsLowLightEvent).</summary>
        public const string MoreLightHint = "More light helps your hands";
        /// <summary>The looper's first-use tip: said once ever, on the first LOOP press (IGuide.Tip).</summary>
        public const string TipLoopStart = "tip_loop_start";

        /// <summary>The tour steps. Only the swipe and the nod can time out (12 s), each with its own skip line.</summary>
        public static readonly StepDef[] Steps =
        {
            new StepDef(StepId.Open, DemoKind.Palm, "open_vr", "open_desk", 0f, null),
            new StepDef(StepId.Poke, DemoKind.Poke, "poke_vr", "poke_desk", 0f, null),
            new StepDef(StepId.Close, DemoKind.Palm, "close_vr", "close_desk", 0f, null),
            new StepDef(StepId.Swipe, DemoKind.Swipe, "swipe_vr", "swipe_desk", GuideTiming.StepTimeout, "swipe_skip"),
            new StepDef(StepId.Nod, DemoKind.Nod, "nod_vr", "nod_desk", GuideTiming.StepTimeout, "nod_skip"),
        };

        /// <summary>Index of a line id, -1 when it is not a line (ordinal compare, no allocation).</summary>
        public static int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < Ids.Length; i++) if (string.Equals(Ids[i], id, System.StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>A line of the Guide's own 38 (the voice slots of GuideVoice.LineIds).</summary>
        public static bool IsLine(string id) => IndexOf(id) >= 0;

        /// <summary>A line of the 38 or of any module's merged line file.</summary>
        public static bool Has(string id) => IndexOf(id) >= 0 || Merged.Has(id);

        /// <summary>The caption of a line id, the 38 or the merged ones (null when the id is not a line).</summary>
        public static string TextOf(string id)
        {
            int i = IndexOf(id);
            return i >= 0 ? Texts[i] : Merged.TextOf(id);
        }

        /// <summary>The recording's dry seconds from the manifests (-1 when unknown).</summary>
        public static float SecondsOf(string id)
        {
            int i = IndexOf(id);
            return i >= 0 ? ManifestSeconds[i] : Merged.Seconds(id);
        }

        static GuideLineTable s_merged;
        /// <summary>Every module's merged lines (Resources/GuideLines/*.json and Resources/VoiceManifests/*.json), loaded once.</summary>
        public static GuideLineTable Merged => s_merged ?? (s_merged = GuideLineTable.Load());
        /// <summary>Tests: load the merged table again.</summary>
        public static void ReloadMerged() => s_merged = null;
    }

    [Serializable] public sealed class GuideLineFile { public GuideLineEntry[] lines; }
    [Serializable] public sealed class GuideLineEntry { public string id, text, spoken; }

    /// <summary>The merged lines (platform.md 8.2): the line files in name order, entries in file order, after the Guide's
    /// own 38; each with its manifest entry when one is there (TextOnly tips have none).</summary>
    public sealed class GuideLineTable
    {
        public const string LinesFolder = "GuideLines", ManifestsFolder = "VoiceManifests";

        public struct Voice { public string File; public float Seconds; public int Rate; }

        readonly List<string> _ids = new List<string>();
        readonly Dictionary<string, (string text, string spoken)> _lines = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        readonly Dictionary<string, Voice> _voice = new Dictionary<string, Voice>(StringComparer.Ordinal);
        readonly List<string> _errors = new List<string>();
        readonly List<string> _files = new List<string>();

        /// <summary>The merged line ids, in their stable order (after the 38).</summary>
        public IReadOnlyList<string> Ids => _ids;
        /// <summary>Duplicates and unreadable files (each also logged as an error).</summary>
        public IReadOnlyList<string> Errors => _errors;
        /// <summary>The line and manifest files read, by name.</summary>
        public IReadOnlyList<string> Files => _files;
        /// <summary>Every manifest entry, the 38's own manifest excluded (lines may have none: TextOnly).</summary>
        public IReadOnlyDictionary<string, Voice> Voices => _voice;

        public bool Has(string id) => id != null && _lines.ContainsKey(id);
        public string TextOf(string id) => id != null && _lines.TryGetValue(id, out var l) ? l.text : null;
        public string SpokenOf(string id) => id != null && _lines.TryGetValue(id, out var l) ? (string.IsNullOrEmpty(l.spoken) ? l.text : l.spoken) : null;
        public float Seconds(string id) => id != null && _voice.TryGetValue(id, out Voice v) ? v.Seconds : -1f;
        public bool HasVoice(string id) => id != null && _voice.ContainsKey(id);
        public string FileOf(string id) => id != null && _voice.TryGetValue(id, out Voice v) ? v.File : null;

        /// <summary>Every Resources/GuideLines/*.json and Resources/VoiceManifests/*.json of every module.</summary>
        public static GuideLineTable Load() => Build(Read(LinesFolder), Read(ManifestsFolder));

        static List<(string, string)> Read(string folder)
        {
            var list = new List<(string, string)>();
            TextAsset[] assets = Resources.LoadAll<TextAsset>(folder);
            var sorted = new List<TextAsset>(assets);
            sorted.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (TextAsset t in sorted) list.Add((folder + "/" + t.name, t.text));
            return list;
        }

        /// <summary>Line files and manifests by (name, json), in the order to merge (EditMode tests pass their own).</summary>
        public static GuideLineTable Build(IEnumerable<(string name, string json)> lineFiles, IEnumerable<(string name, string json)> manifests)
        {
            var table = new GuideLineTable();
            foreach (var (name, json) in lineFiles)
            {
                table._files.Add(name);
                GuideLineFile f = null;
                try { f = JsonUtility.FromJson<GuideLineFile>(json); }
                catch (Exception e) { table.Error($"{name}: does not parse ({e.Message})"); continue; }
                if (f?.lines == null) continue;
                foreach (GuideLineEntry l in f.lines)
                {
                    if (l == null || string.IsNullOrEmpty(l.id) || string.IsNullOrEmpty(l.text)) { table.Error($"{name}: a line without an id or text (dropped)"); continue; }
                    if (GuideLines.IndexOf(l.id) >= 0 || table._lines.ContainsKey(l.id)) { table.Error($"{name}: duplicate line id {l.id} (dropped)"); continue; }
                    table._lines[l.id] = (l.text, l.spoken);
                    table._ids.Add(l.id);
                }
            }
            foreach (var (name, json) in manifests)
            {
                table._files.Add(name);
                List<(string id, Voice v)> entries;
                try { entries = ManifestJson.Parse(json); }
                catch (Exception e) { table.Error($"{name}: does not parse ({e.Message})"); continue; }
                foreach (var (id, v) in entries)
                {
                    if (GuideLines.IndexOf(id) >= 0 || table._voice.ContainsKey(id)) { table.Error($"{name}: duplicate voice line {id} (dropped)"); continue; }
                    table._voice[id] = v;
                }
            }
            return table;
        }

        void Error(string msg)
        {
            _errors.Add(msg);
            Debug.LogError("[Guide] lines: " + msg);
        }
    }

    /// <summary>The voice manifest's shape, {"&lt;id&gt;": {"file": "&lt;id&gt;.wav", "seconds": s, "rate": 22050}, ...}: an object
    /// of objects JsonUtility cannot read. A small strict reader (strings, numbers, nested objects; arrays and literals
    /// skipped). Load time only.</summary>
    public static class ManifestJson
    {
        public static List<(string id, GuideLineTable.Voice v)> Parse(string json)
        {
            var list = new List<(string, GuideLineTable.Voice)>();
            int i = 0;
            Skip(json, ref i);
            Expect(json, ref i, '{');
            Skip(json, ref i);
            if (Peek(json, i) == '}') return list;
            while (true)
            {
                Skip(json, ref i);
                string id = String(json, ref i);
                Skip(json, ref i);
                Expect(json, ref i, ':');
                Skip(json, ref i);
                var v = new GuideLineTable.Voice();
                Expect(json, ref i, '{');
                Skip(json, ref i);
                if (Peek(json, i) != '}')
                    while (true)
                    {
                        Skip(json, ref i);
                        string key = String(json, ref i);
                        Skip(json, ref i);
                        Expect(json, ref i, ':');
                        Skip(json, ref i);
                        char c = Peek(json, i);
                        if (c == '"') { string s = String(json, ref i); if (key == "file") v.File = s; }
                        else if (c == '-' || char.IsDigit(c)) { double d = Number(json, ref i); if (key == "seconds") v.Seconds = (float)d; else if (key == "rate") v.Rate = (int)d; }
                        else SkipValue(json, ref i);
                        Skip(json, ref i);
                        if (Peek(json, i) == ',') { i++; continue; }
                        break;
                    }
                Expect(json, ref i, '}');
                list.Add((id, v));
                Skip(json, ref i);
                if (Peek(json, i) == ',') { i++; continue; }
                Expect(json, ref i, '}');
                return list;
            }
        }

        static char Peek(string s, int i) => i < s.Length ? s[i] : '\0';
        static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        static void Expect(string s, ref int i, char c)
        {
            if (Peek(s, i) != c) throw new FormatException($"expected '{c}' at {i}");
            i++;
        }

        static string String(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var b = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length) { i++; b.Append(s[i] == 'n' ? '\n' : s[i]); i++; continue; }
                b.Append(s[i++]);
            }
            Expect(s, ref i, '"');
            return b.ToString();
        }

        static double Number(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static void SkipValue(string s, ref int i)
        {
            char c = Peek(s, i);
            if (c == '{' || c == '[')
            {
                char close = c == '{' ? '}' : ']';
                int depth = 0;
                for (; i < s.Length; i++)
                {
                    if (s[i] == '"') { String(s, ref i); i--; continue; }
                    if (s[i] == c) depth++;
                    else if (s[i] == close && --depth == 0) { i++; return; }
                }
                return;
            }
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']') i++;   // true / false / null
        }
    }

    /// <summary>Docs/08-ux-lessons-menu-guide.md 3.1 and 3.4 (local +z points away from the player).</summary>
    public static class GuideTiming
    {
        public const float Radius = 0.045f;                       // body sphere
        public const float Ahead = 0.55f, Left = 0.25f, Down = 0.10f;   // OFFSET from the head
        public const float Follow = 3f;                           // 1/s, easing toward the spot
        public const float BobAmp = 0.012f, BobRateHz = 1.4f;
        public const float StepTimeout = 12f;
        public const float IntroT = 3.2f, PraiseT = 1.7f, SkipT = 3.2f, ByeT = 2.6f;
        /// <summary>A one-line tip (IGuide.Tip) stays at least this long, then Sunny leaves silently (no "Bye!").</summary>
        public const float TipT = 1.5f;
        public const float LeaveT = 0.8f, LeaveRise = 0.2f;
        public const float HintT = 4f;                            // s a text hint stays up (outside the tour)
        public const float PadFar = 0.2f;                         // m: a kick with every hand pointer this far from the pad is a nod
        public const float BlinkT = 0.13f;
        public const float FirstBlinkMin = 2f, FirstBlinkSpan = 3f, BlinkMin = 2.5f, BlinkSpan = 3.5f;
        public const float Gap = 0.3f;                            // s of quiet between two lines
        public const float Late = 1.2f;                           // s: a recording not ready this long after its caption is skipped
        public const float HoldPad = 0.45f;                       // hold(id, t) = max(t, lineSeconds + 0.45)
        public const float Lead = 0.14f;                          // ROBOT.lead: open chirp -> first word
        public const float Rate = 1f;                             // ROBOT.rate
        public const float BusyTail = 0.05f;                      // lineBusy: time < endT + 0.05
        public const float MouthOpenK = 0.35f, MouthCloseK = 0.2f, MouthFloor = 0.01f, MouthDecay = 1.2f;
        public const float LightBase = 0.7f, LightPulse = 0.2f, LightRate = 2.1f, LightLeave = 0.8f;   // light intensities
        public const float DemoLoop = 3f;
        public const float NodDip = 0.04f, NodTiltRad = 0.45f, NodT = 0.4f;
        public static readonly float[] NodStarts = { 0.2f, 0.9f };
    }
}
