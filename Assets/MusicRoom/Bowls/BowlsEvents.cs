// The bowls' take events (bowls.md 3.7; BUILD-GUIDE 6.1) and how their fields are used. Every type starts with "bowl".
//   bowlStrike  Onset   (Index)  i bowl, vel speed (m/s), kind tap | palm | suede | wood, midi the angle (mrad, rig frame),
//                                pos the height on the wall (0 lip .. 2 base = 2 (1 - s))
//   bowlRubOn   Onset   (Lane)   BowlsRubEncoder: i, zone, u finger speed (m/s), vel level = N / 8, pos, midi, speed;
//   bowlRubCtl  Control (Lane)   lane = the bowl index live (two rubbed bowls are two lanes to the looper); playback
//   bowlRubOff  Release (Lane)   replaces it with the layer's (1..6) or the take's (7) lane
//   bowlDamp    State   (Index, sampled at a layer's start) i bowl, vel = level / 3 (0 none, 1 a light finger, 2 fingers,
//                                3 a palm)
//   bowlSing    Marker  (Index)  i bowl, u = whole seconds sung in this rub (lessons' rub targets; never played back)
//   bowlHit     Marker  (Index)  i bowl, vel = strike speed: a live strike as a lesson hit (the tutorial counts an
//                                instrument's Markers when it declares any; never played back)
// A struck bowl is not Held: it rings through a loop boundary. Live events carry lane = the bowl index (rubs) or 0.
using MusicRoom.Audio.Burst;
using MusicRoom.Core;

namespace MusicRoom.Bowls
{
    public static class BowlsEvents
    {
        public const string Strike = "bowlStrike", RubOn = BowlsRubEncoder.On, RubCtl = BowlsRubEncoder.Ctl, RubOff = BowlsRubEncoder.Off;
        public const string Damp = "bowlDamp", Sing = "bowlSing", Hit = "bowlHit";
        public static readonly string[] StrikeKind = { "tap", "palm", "suede", "wood" };

        public static EventTypeSpec[] Specs => new[]
        {
            new EventTypeSpec(Strike, EventRole.Onset, EventKey.Index),
            new EventTypeSpec(RubOn, EventRole.Onset, EventKey.Lane),
            new EventTypeSpec(RubCtl, EventRole.Control, EventKey.Lane),
            new EventTypeSpec(RubOff, EventRole.Release, EventKey.Lane),
            new EventTypeSpec(Damp, EventRole.State, EventKey.Index, sampled: true),
            new EventTypeSpec(Sing, EventRole.Marker, EventKey.Index),
            new EventTypeSpec(Hit, EventRole.Marker, EventKey.Index),
        };

        static float Wrap(float a) { const float T = 2f * UnityEngine.Mathf.PI; float m = a % T; return m < 0 ? m + T : m; }

        public static TakeEvent StrikeEvent(int bowl, BowlStrikeKind kind, float speed, float angle, float heightS) => new TakeEvent
        {
            type = Strike, i = bowl, vel = speed, kind = StrikeKind[(int)kind], midi = (int)(Wrap(angle) * 1000f + 0.5f), pos = 2f * (1f - heightS),
        };

        public static BowlStrikeKind KindOf(string kind)
        {
            for (int k = 0; k < StrikeKind.Length; k++) if (kind == StrikeKind[k]) return (BowlStrikeKind)k;
            return BowlStrikeKind.Tap;
        }

        public static TakeEvent DampEvent(int bowl, int level) => new TakeEvent { type = Damp, i = bowl, vel = level / 3f };
        public static int DampLevel(in TakeEvent e) => UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(e.vel * 3f), 0, 3);
        public static TakeEvent SingEvent(int bowl, double seconds) => new TakeEvent { type = Sing, i = bowl, u = (float)seconds };
        public static TakeEvent HitEvent(int bowl, float speed, BowlStrikeKind kind) => new TakeEvent { type = Hit, i = bowl, vel = speed, kind = StrikeKind[(int)kind] };
    }
}
