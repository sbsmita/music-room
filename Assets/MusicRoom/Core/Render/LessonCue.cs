// The lesson's "play this next" cue colour, one purple for every instrument (headset feedback: the white cue replaced with
// purple). The tutorial's blocks (piano boxes, drum discs, harp capsules, the generic path's discs, rings and bars on the
// marimba, handpan, hand drums and bowls), the hover glow held on a waiting classic target, the harp's target string and
// the hand drums' print all take it from here. sRGB; Linear for the shaders and vertex colours that take linear. A clear
// violet with blue well above red: it reads on ivory keys (a white key is not purple) and on ebony (bright against black).
// Hit (honey) and wrong (muted coral) stay as they were.
using UnityEngine;

namespace MusicRoom.Core
{
    public static class LessonCue
    {
        /// <summary>The cue (sRGB #9e4dff): blocks on their way and the glow on the waiting target.</summary>
        public static readonly Color Color = new Color(0.62f, 0.30f, 1.0f, 1f);
        /// <summary>The live block's pulse peak (sRGB #bf80ff): a lighter violet, never white.</summary>
        public static readonly Color Bright = new Color(0.75f, 0.50f, 1.0f, 1f);
        public static readonly Color Linear = Color.linear, BrightLinear = Bright.linear;

        /// <summary>True when <paramref name="c"/> (sRGB) reads as the cue's purple: blue leads, red well above green.</summary>
        public static bool IsPurple(Color c) => c.b > c.r + 0.1f && c.r > c.g + 0.1f && c.b > 0.5f;
    }
}
