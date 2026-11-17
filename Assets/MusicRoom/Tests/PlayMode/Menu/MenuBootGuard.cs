// Boots for the menu's PlayMode tests tolerate exactly one known foundation race and nothing else: SynthSource.Awake
// publishes _voices before _mix, so the audio thread's OnAudioFilterRead can throw ArgumentNullException while the synth
// sources awake (CONTRACT-REQUESTS.md 3, for the Audio owner). The Test Framework classifies a log when it is emitted,
// so failing logs are ignored only for the boot frame, every other error or exception logged meanwhile is collected
// here, and End() fails the test with them.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Menu
{
    static class MenuBootGuard
    {
        static readonly List<string> s_errors = new List<string>();
        static bool s_on;

        public static bool IsKnownAudioRace(string message, string stack) =>
            message != null && message.StartsWith("ArgumentNullException") && stack != null && stack.Contains("SynthSource.OnAudioFilterRead");

        static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (IsKnownAudioRace(message, stack)) return;
            lock (s_errors) s_errors.Add(type + ": " + message);
        }

        public static void Begin()
        {
            lock (s_errors) s_errors.Clear();
            LogAssert.ignoreFailingMessages = true;
            if (!s_on) { Application.logMessageReceivedThreaded += OnLog; s_on = true; }
        }

        /// <summary>After the boot frame: failing logs count again; anything but the known race fails the test.</summary>
        public static void End()
        {
            Stop();
            string[] errors;
            lock (s_errors) { errors = s_errors.ToArray(); s_errors.Clear(); }
            Assert.That(errors, Is.Empty, "errors logged while the app booted (other than the known SynthSource race)");
        }

        /// <summary>TearDown safety: never leave failing logs ignored for the next test.</summary>
        public static void Stop()
        {
            if (s_on) { Application.logMessageReceivedThreaded -= OnLog; s_on = false; }
            LogAssert.ignoreFailingMessages = false;
        }
    }

    public class MenuBootGuardTests
    {
        [Test]
        public void TheBootGuardDropsOnlyTheKnownAudioRace()
        {
            const string race = "ArgumentNullException: Value cannot be null.\nParameter name: array";
            Assert.IsTrue(MenuBootGuard.IsKnownAudioRace(race, "  at System.Array.Clear (...)\n  at MusicRoom.Audio.SynthSource.OnAudioFilterRead (System.Single[] data, System.Int32 channels)"));
            Assert.IsFalse(MenuBootGuard.IsKnownAudioRace(race, "  at MusicRoom.Menu.MenuModule.Init (MusicRoom.Core.App app)"), "the same exception anywhere else still fails");
            Assert.IsFalse(MenuBootGuard.IsKnownAudioRace("NullReferenceException: Object reference not set", "  at MusicRoom.Audio.SynthSource.OnAudioFilterRead"));
        }
    }
}
