// The development rub log: once a second while a bowl is rubbed, one logcat line with what the rub really does on the
// headset (the rim speed and the normal force the hands send, the unit's stick force, its stick / slip / lost fractions,
// the bowl's level and its sing), so a device run can confirm the rub's sound against the hands' input. Development
// players only (Enabled: Debug.isDebugBuild outside the editor); a release build never formats or logs a line. Main thread.
using System;
using MusicRoom.Audio.Burst;
using UnityEngine;

namespace MusicRoom.Bowls
{
    public sealed class BowlsRubLog
    {
        static int s_enabled = -1;
        /// <summary>On in development players only (never in release, off in the editor and its tests). Read on first use (a
        /// frame), never from a static or field initializer: Unity refuses Debug.isDebugBuild in a MonoBehaviour's constructor.</summary>
        public static bool Enabled
        {
            get { if (s_enabled < 0) s_enabled = Debug.isDebugBuild && !Application.isEditor ? 1 : 0; return s_enabled == 1; }
            set => s_enabled = value ? 1 : 0;
        }
        public const double Period = 1.0;

        int _bowl = -1, _frames;
        double _acc, _v, _n;
        BowlsRubStats _first;

        /// <summary>The last line made (tests).</summary>
        public string Line { get; private set; }

        /// <summary>One frame: bowl is the bowl being rubbed (-1 none), speed its rim speed (m/s), force the normal force sent (N),
        /// st the unit's counters now, damp its damping level. Returns the line when one is due (once per Period of rubbing).</summary>
        public string Frame(double dt, int bowl, double speed, double force, in BowlsRubStats st, int damp)
        {
            if (bowl < 0 || bowl != _bowl) { _bowl = bowl; _acc = 0; _frames = 0; _v = 0; _n = 0; _first = st; if (bowl < 0) return null; }
            _acc += dt; _frames++; _v += speed; _n += force;
            if (_acc < Period) return null;
            long c = st.Contact - _first.Contact, s = st.Stick - _first.Stick, l = st.Lost - _first.Lost;
            double stick = c > 0 ? (double)s / c : 0, lost = c > 0 ? (double)l / c : 0;
            double db = st.Rms > 0 ? 20 * Math.Log10(st.Rms) : -120;
            Line = $"[Bowls] rub bowl {bowl} speed {_v / _frames:F2} m/s N {_n / _frames:F2} N Fc {st.Force:F2} N slip {st.Slip:F3} m/s " +
                   $"stick {stick:F2} slip-frac {1 - stick:F2} lost {lost:F2} level {db:F1} dBFS sing {st.Sing:F3} m/s damp {damp}";
            _acc = 0; _frames = 0; _v = 0; _n = 0; _first = st;
            return Line;
        }
    }
}
