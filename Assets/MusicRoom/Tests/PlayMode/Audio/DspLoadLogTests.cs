// The development "dsp load" diagnostics without a HUD (headset feedback: the head-locked "dsp load" line is gone from
// every build). DspLoadLog.LogInEditor stands in for a development player: the Audio module adds the logger, which logs
// the once-a-second [Audio] dsp line and draws nothing (no TextMesh, no renderer anywhere under the Audio host).
using System.Collections;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Audio
{
    public class DspLoadLogTests
    {
        TestBoot _boot;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DspLoadLog.LogInEditor = false;
            if (_boot != null) yield return _boot.Stop();
        }

        [UnityTest]
        public IEnumerator TheDspLineIsLoggedAndNothingIsShownOnScreen()
        {
            DspLoadLog.LogInEditor = true;
            _boot = new TestBoot();
            _boot.Options.ModuleFilter = t => t == typeof(AudioModule);
            yield return _boot.Start();
            AudioModule module = App.Current.Modules.OfType<AudioModule>().First();
            var engine = (AudioEngine)App.Current.Get<IAudioEngine>();
            if (engine.Graph == null) Assert.Ignore("no audio graph in this editor (no output device)");
            DspLoadLog log = module.DspLog;
            Assert.IsNotNull(log, "the logger is added where the HUD used to be");
            Assert.That(log.GetComponentsInChildren<Renderer>(true), Is.Empty, "it draws nothing");
            Assert.That(Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => t.text != null && t.text.StartsWith("DSP")), Is.Empty, "no DSP readout anywhere");
            float t0 = Time.realtimeSinceStartup;
            while (log.LastLine == null && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.That(log.LastLine, Does.StartWith("[Audio] dsp avg "), "the once-a-second log line is still there");
            Assert.That(log.LastLine, Does.Contain(" groups ").And.Contain(" buf "));
        }
    }
}
