// The one way PlayMode tests boot the app (ARCHITECTURE section 16): scripted hands, a fresh in-memory settings store and
// a temporary data folder (so no test reads or writes the developer's PlayerPrefs or real takes, and tests never depend
// on each other's order), optional simulated headset / manual frames / scripted audio clock, and a TearDown that removes
// the app, the code-only rig and the folder. Module tests (any Tests/PlayMode/<Module>/ folder) use it as is.
using System;
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode
{
    public sealed class TestBoot
    {
        public readonly FakeHandSource Hands = new FakeHandSource();
        public readonly MemorySettingsStore Settings = new MemorySettingsStore();
        public readonly string DataPath = Path.Combine(Path.GetTempPath(), "musicroom-tests", Guid.NewGuid().ToString("N"));
        public readonly BootOptions Options;
        public GameObject Root { get; private set; }
        public App App => App.Current;
        /// <summary>Times the app asked to quit (BootOptions.Quit is always this counter: a test never quits the runner).</summary>
        public int QuitCalls { get; private set; }

        /// <param name="headless">No hover-glow mesh or debug hands (most tests).</param>
        /// <param name="simulateHeadset">App.IsPresenting with the scripted hands (XR-only paths run).</param>
        /// <param name="manualFrames">Unity's Update drives nothing: call App.Step(dt) (or Step below).</param>
        /// <param name="audioClock">Scripted main-thread audio clock (IAudioEngine.Now, the recorder, Sunny).</param>
        /// <param name="startPanel">Show the launch panel ("Play here" / "Play in my room") as the app does. Off by default,
        /// so every other test boots straight into the room.</param>
        public TestBoot(bool headless = true, bool simulateHeadset = false, bool manualFrames = false, Func<double> audioClock = null,
            bool startPanel = false)
        {
            Options = new BootOptions
            {
                HandSource = Hands, Headless = headless, Settings = Settings, DataPath = DataPath,
                SimulateHeadset = simulateHeadset, ManualFrames = manualFrames, AudioClock = audioClock,
                StartPanel = startPanel, Quit = () => QuitCalls++,
            };
        }

        /// <summary>Boots (AppRoot.Awake runs synchronously) and yields one frame. App.Current is ready afterwards.</summary>
        public IEnumerator Start()
        {
            AppRoot.Options = Options;
            Root = new GameObject(AppRoot.RootName);
            Root.AddComponent<AppRoot>();
            yield return null;
        }

        /// <summary>ManualFrames: n frames of dt each (hands, clock, App.Frame).</summary>
        public void Step(float dt, int n = 1)
        {
            for (int i = 0; i < n; i++) App.Step(dt);
        }

        public IEnumerator Stop()
        {
            if (Root != null) Object.Destroy(Root);
            var rig = RigBootstrap.Instance;
            if (rig != null) Object.Destroy(rig.gameObject);
            yield return null;
            try { if (Directory.Exists(DataPath)) Directory.Delete(DataPath, true); }
            catch (Exception e) { Debug.LogWarning("[TestBoot] could not delete " + DataPath + ": " + e.Message); }
        }
    }
}
