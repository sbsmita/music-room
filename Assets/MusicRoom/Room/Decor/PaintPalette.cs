// The wall paint's colours and pitches (Room/Decor/WallPaint.cs): one pigment per instrument, a shade of it per pitch (low
// notes deeper and a touch warmer, high notes lighter and a touch cooler), and a pitch 0..1 read from the played-back event
// itself (PlaybackNoteEvent.Event: the piano's / marimba's midi, the harp's string, the drum, the hand drum, the bowl, the
// handpan's field) with a key that tells neighbouring notes apart. Pure, allocation-free.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    public static class PaintPalette
    {
        /// <summary>The pigments (sRGB): soft, earthy watercolour tones that sit in the evening room.</summary>
        public static Color Pigment(InstrumentKind kind)
        {
            switch (kind)
            {
                case InstrumentKind.Piano: return new Color(0.66f, 0.26f, 0.44f);      // rose madder
                case InstrumentKind.Drums: return new Color(0.86f, 0.42f, 0.16f);      // burnt orange
                case InstrumentKind.Harp: return new Color(0.16f, 0.52f, 0.56f);       // teal
                case InstrumentKind.Handpan: return new Color(0.80f, 0.62f, 0.18f);    // ochre
                case InstrumentKind.HandDrums: return new Color(0.70f, 0.26f, 0.16f);  // rust
                case InstrumentKind.Marimba: return new Color(0.38f, 0.58f, 0.22f);    // sap green
                case InstrumentKind.Bowls: return new Color(0.44f, 0.42f, 0.76f);      // lavender blue
                default: return new Color(0.30f, 0.34f, 0.62f);                        // indigo
            }
        }

        /// <summary>The pigment shaded by pitch: low deeper (value down, hue toward red), high lighter (toward white, hue toward
        /// blue), at most Shade either way.</summary>
        public const float Shade = 0.3f, HueShift = 0.035f;

        public static Color Of(InstrumentKind kind, float pitch)
        {
            Color c = Pigment(kind);
            float p = Mathf.Clamp01(pitch) * 2f - 1f;   // -1 low .. +1 high
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + HueShift * p, 1f);
            if (p < 0f) v *= 1f + Shade * 0.8f * p;          // deeper
            else s *= 1f - Shade * p;                          // lighter
            Color o = Color.HSVToRGB(h, Mathf.Clamp01(s), Mathf.Clamp01(v));
            if (p > 0f) o = Color.Lerp(o, Color.white, Shade * 0.5f * p);
            o.a = 1f;
            return o;
        }

        /// <summary>A played-back note's pitch (0 low .. 1 high) and a key telling it from its neighbours.</summary>
        public static float PitchOf(InstrumentKind kind, in TakeEvent e, out int key)
        {
            key = 0;
            switch (kind)
            {
                case InstrumentKind.Drums:
                    key = DrumKey(e.kind);
                    return DrumPitch(e.kind);
                case InstrumentKind.HandDrums:
                    key = 200 + (e.kind == "tumba" ? 0 : 1) * 10 + (e.zone != null ? e.zone.Length : 0);
                    return (e.kind == "tumba" ? 0.3f : 0.5f) + (e.zone == "slap" ? 0.15f : 0f);
                case InstrumentKind.Harp:
                    key = 100 + e.i;
                    return Mathf.Clamp01(e.i / 21f);
                case InstrumentKind.Handpan:
                    key = 300 + e.i;
                    return Mathf.Clamp01(e.i / 9f);
                case InstrumentKind.Bowls:
                    // a bowl strike's midi field carries the strike's angle, not a note: the bowl itself is the pitch
                    key = 400 + e.i;
                    return Mathf.Clamp01(1f - e.i / 6f);   // the big bowl first, the lowest
            }
            if (e.midi > 0)
            {
                key = e.midi;
                return Mathf.Clamp01((e.midi - 36) / 60f);   // C2 .. C7
            }
            key = 500 + e.i;
            return 0.5f;
        }

        public static float DrumPitch(string kind)
        {
            switch (kind)
            {
                case "kick": return 0.05f;
                case "floor": return 0.22f;
                case "tom2": return 0.36f;
                case "tom1": return 0.46f;
                case "snare": return 0.55f;
                case "hat": case "hatOpen": case "hatClose": return 0.76f;
                case "ride": return 0.86f;
                case "crash": return 0.96f;
                default: return 0.5f;
            }
        }

        static int DrumKey(string kind)
        {
            switch (kind)
            {
                case "kick": return 1;
                case "floor": return 2;
                case "tom2": return 3;
                case "tom1": return 4;
                case "snare": return 5;
                case "hat": case "hatOpen": case "hatClose": return 6;
                case "ride": return 7;
                case "crash": return 8;
                default: return 9;
            }
        }
    }
}
