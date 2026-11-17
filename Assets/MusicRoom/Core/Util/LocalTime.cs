// The headset's wall clock for what the app shows people (a recording's time in Recordings). Takes store their time
// as UTC unix milliseconds; this turns one into the player's local time, in a friendly form: "Today 3:42 PM",
// "Yesterday 9:10 AM", "Nov 8, 4:05 PM" (a past year adds it: "Nov 8, 2025, 4:05 PM"). Invariant month names and
// AM / PM, whatever the device locale.
// The offset: on the Android player it comes from the platform itself (java.util.TimeZone.getDefault().getOffset(ms),
// over JNI, DST included for that very moment): the IL2CPP class library's TimeZoneInfo.Local depends on reading the
// device's zone id and tz database, and on Android it can quietly fall back to UTC (the headset showed recording times
// hours off). Elsewhere (the editor, tests, a desktop player) TimeZoneInfo.Local. Both are logged once on the device,
// so a log shows which one was right. Tests replace the source with a fixed offset (OffsetSource).
using System;
using System.Globalization;
using UnityEngine;

namespace MusicRoom.Core
{
    public static class LocalTime
    {
        public const long DayMs = 86400000L;
        /// <summary>The offset of "now" is asked again after this long (a zone change or DST start shows within a minute).</summary>
        public const long NowOffsetRefreshMs = 60000L;

        static Func<long, long> s_source;
        static Func<long> s_now;
        static long s_nowOffset, s_nowOffsetAt = long.MinValue;
        static bool s_logged;

        /// <summary>unix ms -> the local offset (ms east of UTC) at that moment. Null: the platform's (see the header).
        /// Setting it (tests) forgets the cached offset of now.</summary>
        public static Func<long, long> OffsetSource
        {
            get => s_source;
            set { s_source = value; s_nowOffsetAt = long.MinValue; }
        }

        /// <summary>The current UTC unix ms. Null: the system clock. Tests pin it.</summary>
        public static Func<long> NowSource
        {
            get => s_now;
            set { s_now = value; s_nowOffsetAt = long.MinValue; }
        }

        public static long NowMs => s_now != null ? s_now() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>The local offset (ms) at <paramref name="unixMs"/>.</summary>
        public static long OffsetMs(long unixMs)
        {
            if (s_source != null) return s_source(unixMs);
            return PlatformOffsetMs(unixMs);
        }

        /// <summary>The local offset of now, asked at most once a minute (the menu checks the day on every relayout).</summary>
        public static long NowOffsetMs(long nowMs)
        {
            if (s_nowOffsetAt == long.MinValue || Math.Abs(nowMs - s_nowOffsetAt) > NowOffsetRefreshMs)
            {
                s_nowOffset = OffsetMs(nowMs);
                s_nowOffsetAt = nowMs;
            }
            return s_nowOffset;
        }

        /// <summary>Today's local day number (days since 1970-01-01 in the local zone): a cached "Today" label is stale once
        /// it changes.</summary>
        public static long TodayStamp()
        {
            long now = NowMs;
            return Day(now, NowOffsetMs(now));
        }

        /// <summary>The local day number of a moment.</summary>
        public static long Day(long unixMs, long offsetMs) => FloorDiv(unixMs + offsetMs, DayMs);

        /// <summary>"Today 3:42 PM" / "Yesterday 9:10 AM" / "Nov 8, 4:05 PM" / "Nov 8, 2025, 4:05 PM", now and the zone
        /// from the sources.</summary>
        public static string Friendly(long createdMs)
        {
            long now = NowMs;
            return Friendly(createdMs, OffsetMs(createdMs), now, NowOffsetMs(now));
        }

        /// <summary>The pure form (tests): a moment and its local offset, now and its local offset.</summary>
        public static string Friendly(long createdMs, long createdOffsetMs, long nowMs, long nowOffsetMs)
        {
            long localCreated = createdMs + createdOffsetMs;
            long dayC = FloorDiv(localCreated, DayMs), dayN = FloorDiv(nowMs + nowOffsetMs, DayMs);
            DateTime t = DateTime.SpecifyKind(new DateTime(1970, 1, 1).AddMilliseconds(localCreated), DateTimeKind.Unspecified);
            DateTime today = new DateTime(1970, 1, 1).AddDays(dayN);
            string time = t.ToString("h:mm tt", CultureInfo.InvariantCulture);
            if (dayC == dayN) return "Today " + time;
            if (dayC == dayN - 1) return "Yesterday " + time;
            return t.Year == today.Year
                ? t.ToString("MMM d", CultureInfo.InvariantCulture) + ", " + time
                : t.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) + ", " + time;
        }

        static long FloorDiv(long a, long b) { long q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }

        static long ManagedOffsetMs(long unixMs)
        {
            try { return (long)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(unixMs)).TotalMilliseconds; }
            catch (Exception) { return 0L; }
        }

        static long PlatformOffsetMs(long unixMs)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                long java;
                using (var tz = new AndroidJavaClass("java.util.TimeZone"))
                using (AndroidJavaObject def = tz.CallStatic<AndroidJavaObject>("getDefault"))
                {
                    java = def.Call<int>("getOffset", unixMs);
                    if (!s_logged)
                    {
                        s_logged = true;
                        string id = def.Call<string>("getID");
                        long managed = ManagedOffsetMs(unixMs);
                        string mid;
                        try { mid = TimeZoneInfo.Local.Id; } catch (Exception) { mid = "?"; }
                        Debug.Log($"[Time] local zone {id}: offset {java / 60000} min (platform); TimeZoneInfo.Local {mid} {managed / 60000} min{(managed != java ? " (differs: the platform's is used)" : "")}");
                    }
                }
                return java;
            }
            catch (Exception e)
            {
                if (!s_logged) { s_logged = true; Debug.LogWarning("[Time] the platform's time zone failed (" + e.Message + "): TimeZoneInfo.Local is used"); }
                return ManagedOffsetMs(unixMs);
            }
#else
            if (!s_logged) { s_logged = true; }
            return ManagedOffsetMs(unixMs);
#endif
        }
    }
}
