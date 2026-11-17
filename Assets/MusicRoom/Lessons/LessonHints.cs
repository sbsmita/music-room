// What the menu tells the player to do for one lesson event: "Play C4 + E4",
// "Hi-hat + Kick", "Pluck C4 (red)" (the harp's C strings are red, its F strings blue). lessons.json carries these hints
// precomputed by the same rules (hintFor); the tutorial uses the JSON text (no string building while playing) and falls
// back to this code for events without one. LessonHintsTests checks this code reproduces all 826 JSON hints.
// Every other instrument words its own hints (platform.md 7.2: ILessonTargets.Hint, "Tap D", "Hold A"): For gives the
// event's written hint for it, and Generic asks the instrument.
using System.Text;
using MusicRoom.Core;

namespace MusicRoom.Lessons
{
    public static class LessonHints
    {
        static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        /// <summary>noteName: 60 -> "C4", 61 -> "C#4" (octave = floor(midi / 12) - 1).</summary>
        public static string NoteName(int midi)
        {
            int pc = ((midi % 12) + 12) % 12;
            int octave = (midi - pc) / 12 - 1;
            return Names[pc] + octave;
        }

        /// <summary>DRUM_NAMES (unknown kinds print as themselves).</summary>
        public static string DrumName(string kind)
        {
            switch (kind)
            {
                case "snare": return "Snare";
                case "tom1": return "Tom 1";
                case "tom2": return "Tom 2";
                case "floor": return "Floor tom";
                case "hat": return "Hi-hat";
                case "crash": return "Crash";
                case "ride": return "Ride";
                case "kick": return "Kick";
                default: return kind;
            }
        }

        /// <summary>The harp's 22 strings in C major from C3 (HarpGeometry.MidiOf).</summary>
        public static int HarpMidi(int i) => 48 + 12 * (i / 7) + Major[i % 7];
        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };

        /// <summary>The generic path's hint: the instrument's wording (ILessonTargets.Hint), else the event's written hint.</summary>
        public static string Generic(ILessonTargets targets, LessonEvent ev)
        {
            if (ev == null) return "";
            string h = targets != null && ev.targets != null ? targets.Hint(ev.targets) : null;
            return !string.IsNullOrEmpty(h) ? h : ev.hint ?? "";
        }

        /// <summary>hintFor(ev). <paramref name="harp"/> null = the standard 22-string table. Classic three only:
        /// any other instrument gets the event's written hint (its own wording comes from Generic).</summary>
        public static string For(InstrumentKind instrument, LessonEvent ev, IHarpApi harp = null)
        {
            if (ev?.targets == null) return "";
            if (!Tutorial.IsClassic(instrument)) return ev.hint ?? "";
            var sb = new StringBuilder(24);
            sb.Append(instrument == InstrumentKind.Piano ? "Play " : instrument == InstrumentKind.Harp ? "Pluck " : "");
            for (int k = 0; k < ev.targets.Length; k++)
            {
                if (k > 0) sb.Append(" + ");
                LessonTarget t = ev.targets[k];
                if (instrument == InstrumentKind.Piano) sb.Append(NoteName(t.midi));
                else if (instrument == InstrumentKind.Drums) sb.Append(DrumName(t.kind));
                else
                {
                    int count = harp != null ? harp.StringCount : 22;
                    if (t.i < 0 || t.i >= count) { sb.Append("string ").Append(t.i + 1); continue; }
                    int midi = harp != null ? harp.MidiOf(t.i) : HarpMidi(t.i);
                    int pc = ((midi % 12) + 12) % 12;
                    sb.Append(NoteName(midi)).Append(pc == 0 ? " (red)" : pc == 5 ? " (blue)" : "");
                }
            }
            return sb.ToString();
        }
    }
}
