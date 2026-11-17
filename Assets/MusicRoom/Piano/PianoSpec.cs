// The piano's constants and its 88-key table (spec 05 section 2.1,
// Docs/05-instruments.md `piano.constants` / `piano.keys`). Everything here is in the Unity piano-local frame:
// x right, y up with the white key tops at y = 0, z = "back" = the distance behind the white-key fronts (= -z of the right-handed frame).
// Kept as plain static data so the logic, the meshes, the lessons API and the tests all read one table.
using UnityEngine;

namespace MusicRoom.Piano
{
    public static class PianoSpec
    {
        // ---- keys ----
        public const float W = 0.0235f;          // white key pitch
        public const float L = 0.15f;            // white key length, front edge to hinge
        public const float H = 0.02f;            // white key thickness
        public const float BW = 0.0137f;         // black key width
        public const float BL = 0.095f;          // black key length from the hinge (fronts at back 0.055)
        public const float BTOP = 0.012f;        // black key top above the white tops
        public const float WW = W - 0.0014f;     // visible white key width (the rest is the gap between keys)
        public const float BlackH = 0.016f;      // black key body height (y -0.004 .. 0.012)
        public const float WhiteRound = 0.0022f, BlackRound = 0.0025f;

        // ---- the action ----
        public const float TRAVEL = 0.01f;                     // m at a white key's front edge
        public const float MaxAngleWhite = TRAVEL / L;         // 0.0667 rad
        public const float MaxAngleBlack = 0.084f;             // 8 mm at a black key's own front
        public const float LEVER_MIN = 0.06f;                  // a finger near the hinge is not a hair trigger
        public const float S_ESC = 0.9f;                       // the note fires at 90 % of the travel
        public const float S_RESET = 0.5f;                     // a struck key lets its finger go half way back up
        public const float S_DAMPER = 0.3f;                    // the damper falls 70 % of the way up
        public const float VEL_WINDOW = 0.03f;                 // s of fingertip history the strike speed is read over
        public const float MIN_PRESS_SPEED = 0.07f;            // m/s: slower is a resting finger, not a press
        public const float LOUD_SPEED = 1.5f;                  // m/s: full velocity
        public const float FAST_PRESS_SPEED = 0.5f, EARLY_S = 0.55f, SPARSE_DT = 0.025f;   // early fire on sparse tracking
        public const float START_INSET = 0.0015f;              // a press starts inside the key top less this at every edge
        public const float HOLD_MARGIN = 0.004f;               // a finger on a key keeps it within this of its edges
        public const float RISE_OMEGA = 54f, RISE_ZETA = 0.85f; // return spring: damper ~44 ms, rest ~100 ms
        public const float PRESS_TIME = 0.045f;                // keyboard / mouse / playback press travel time
        public const float DarkenWhite = 0.16f, DarkenBlack = 0.3f;
        public const float PHRASE_GAP = 0.6f;                  // automatic sustain: the pedal lifts at a new phrase
        public const float MAX_RING = 6f;                      // nothing rings longer than this after its release
        public const float HITCH = 0.06f, HITCH_IN = 0.06f, HITCH_OUT = 0.025f, FELT = 0.94f; // keyShape
        public const float GlissSpeed = 0.15f;                 // m/s a sliding finger needs to strike the next key

        // ---- desktop ----
        public const float ClickVelocity = 0.7f, DragVelocity = 0.6f, KeyboardVelocity = 0.75f;
        public const int KeyboardBase = 60, KeyboardBaseMin = 24, KeyboardBaseMax = 96;

        // ---- hover ----
        public const float HoverHeight = 0.05f, HoverLength = 0.07f, HoverWidthWhite = 0.024f, HoverWidthBlack = 0.016f, HoverGain = 0.45f;

        // ---- body (spec 05 piano.build) ----
        public const float BodyTop = 0.038f, BodyBottom = -0.092f, BodyRear = 0.29f, BodyHalfWidth = 1.326f / 2f;
        public const float TableLift = 0.092f;  // MR: key tops above a real table top when the stand is hidden
        public const float FloorY = -0.72f;     // the stand's feet, below the key tops

        // ---- range ----
        public const int FirstMidi = 21, LastMidi = 108, KeyCount = 88, WhiteCount = 52, BlackCount = 36, MiddleC = 60;
        /// <summary>x of the left edge of the lowest white key: -52 W / 2 = -0.611.</summary>
        public const float X0 = -(52 * W) / 2f;

        /// <summary>The names printed on the keys.</summary>
        public static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        /// <summary>Black key centre offsets from the white key boundary, by pitch class.</summary>
        public static float BlackOffset(int pc)
        {
            switch (pc)
            {
                case 1: return -0.0028f;
                case 3: return 0.0028f;
                case 6: return -0.004f;
                case 8: return 0f;
                case 10: return 0.004f;
                default: return float.NaN;
            }
        }

        public static bool IsBlack(int midi) { int pc = ((midi % 12) + 12) % 12; return pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10; }

        /// <summary>Piano-local x of a key centre (white: x0 + (k + .5) W; black: x0 + whitesBefore W + off[pc]).</summary>
        public static float KeyX(int midi)
        {
            int whites = 0;
            for (int m = FirstMidi; m < midi; m++) if (!IsBlack(m)) whites++;
            return IsBlack(midi) ? X0 + whites * W + BlackOffset(midi % 12) : X0 + (whites + 0.5f) * W;
        }

        /// <summary>labelText: C D E F G A B with the octave digit on every C; C# D# F# G# A# on the sharps.</summary>
        public static string LabelText(int midi)
        {
            int pc = midi % 12;
            return pc == 0 ? NoteNames[pc] + (midi / 12 - 1) : NoteNames[pc];
        }

        /// <summary>labelCellKey: the atlas cell a key reads ('b:' sharps, 'c:' middle C with its mark, 'w:' the rest).</summary>
        public static string LabelCellKey(int midi) => (IsBlack(midi) ? "b:" : midi == MiddleC ? "c:" : "w:") + LabelText(midi);

        public static float MaxAngle(bool black) => black ? MaxAngleBlack : MaxAngleWhite;
        public static float Darken(bool black) => black ? DarkenBlack : DarkenWhite;
        public static float KeyTop(bool black) => black ? BTOP : 0f;

        /// <summary>The visible key depth under a finger pushing it to depth s (keyShape): the key follows the
        /// finger, hesitates just before the escapement, catches up just after it and settles into the felt without overshoot.</summary>
        public static float KeyShape(float s, float esc = S_ESC)
        {
            if (s <= 0f) return 0f;
            float d = s;
            if (s > esc - HITCH_IN && s < esc + HITCH_OUT)
                d -= s <= esc ? HITCH * Smooth((s - (esc - HITCH_IN)) / HITCH_IN) : HITCH * (1f - Smooth((s - esc) / HITCH_OUT));
            if (d > FELT) d = 1f - (1f - FELT) * Mathf.Exp(-(d - FELT) / (1f - FELT));
            return d;
        }

        static float Smooth(float t) => t <= 0f ? 0f : t >= 1f ? 1f : t * t * (3f - 2f * t);

        /// <summary>Strike speed (m/s) to note velocity: loudness rises evenly with the log of the speed from
        /// MIN_PRESS_SPEED to LOUD_SPEED (about 20 dB), inverting the synth's level 0.08 + 0.8 vel^2.</summary>
        public static float StrikeVelocity(float vy)
        {
            double x = System.Math.Log(System.Math.Max(vy, 1e-6) / MIN_PRESS_SPEED) / System.Math.Log(LOUD_SPEED / (double)MIN_PRESS_SPEED);
            x = x < 0 ? 0 : x > 1 ? 1 : x;
            double level = 0.88 * System.Math.Pow(10, x - 1);
            double vel = System.Math.Sqrt(System.Math.Max(0, level - 0.08) / 0.8);
            return (float)(vel < 0.1 ? 0.1 : vel > 1 ? 1 : vel);
        }

        /// <summary>The return spring over dt, solved exactly: the same rise at any frame rate, stopped by the
        /// front rail at rest.</summary>
        public static void Spring(ref float depth, ref float v, float dt)
        {
            float w = RISE_OMEGA, z = RISE_ZETA, wd = w * Mathf.Sqrt(1f - z * z);
            float x0 = depth, v0 = v;
            float e = Mathf.Exp(-z * w * dt), c = Mathf.Cos(wd * dt), s = Mathf.Sin(wd * dt);
            float b = (v0 + z * w * x0) / wd;
            float x = e * (x0 * c + b * s);
            float nv = e * ((b * wd - z * w * x0) * c - (x0 * wd + z * w * b) * s);
            if (x <= 0f || (x < 0.001f && nv < 0.05f)) x = nv = 0f;
            depth = x;
            v = nv;
        }
    }
}
