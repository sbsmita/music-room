// The revised foundation's SynthSource can still throw once on the audio thread while a freshly booted app builds its
// synth sources (Awake publishes _voices before _mix; Guide/CONTRACT-REQUESTS.md item 3; the Audio module's own clone
// already publishes _voices last with Volatile.Write, so this guard becomes a no-op once that lands). That one message
// would fail whichever test is booting. While a Guide test boots (around TestBoot.Start), this guard tolerates exactly
// that exception (and reports it), and still fails the test on any other error or assert logged meanwhile.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Guide
{
    public static class AudioBootGuard
    {
        static readonly List<string> s_unexpected = new List<string>();
        static int s_tolerated;
        static bool s_on;

        public static int Tolerated => s_tolerated;

        public static bool IsKnownAudioRace(string condition, string stack) =>
            condition != null && condition.Contains("ArgumentNullException") && stack != null && stack.Contains("SynthSource.OnAudioFilterRead");

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            lock (s_unexpected)
            {
                if (IsKnownAudioRace(condition, stack)) s_tolerated++;
                else s_unexpected.Add(type + ": " + condition);
            }
        }

        public static void Begin()
        {
            if (s_on) return;
            s_on = true;
            lock (s_unexpected) s_unexpected.Clear();
            s_tolerated = 0;
            Application.logMessageReceivedThreaded += OnLog;
            LogAssert.ignoreFailingMessages = true;
        }

        /// <summary>Stops tolerating and fails on anything but the known race.</summary>
        public static void End()
        {
            if (!s_on) return;
            s_on = false;
            LogAssert.ignoreFailingMessages = false;
            Application.logMessageReceivedThreaded -= OnLog;
            if (s_tolerated > 0) Debug.LogWarning($"[Guide] test: tolerated {s_tolerated} SynthSource audio-thread exception(s) during boot (CONTRACT-REQUESTS 3)");
            string[] bad;
            lock (s_unexpected) bad = s_unexpected.ToArray();
            Assert.That(bad, Is.Empty, "errors logged while the app booted");
        }

        /// <summary>TearDown safety: never leave failing messages ignored for the next test.</summary>
        public static void Reset()
        {
            if (!s_on) return;
            s_on = false;
            Application.logMessageReceivedThreaded -= OnLog;
            LogAssert.ignoreFailingMessages = false;
        }
    }
}
