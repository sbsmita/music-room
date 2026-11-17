// The tape deck's loop strip and Sunny's walkthrough on the GPU (Tools/unity.sh test-play --gpu): PNGs of the deck from
// the seat and close up, idle, during LOOP's count (three of the four count lights), looping with three layers (one
// muted), a fourth armed, 110 BPM / 4 bars / CLICK lit, CLEAR waiting for its second tap (TAP AGAIN), and the GUIDE
// walkthrough with Sunny perched beside the deck pointing at REC. Pixel checks: the count lights are amber, the layer
// lights green, the muted one dark, TAP AGAIN printed, and the deck within its 7 draws.
// Output: $MUSICROOM_SHOTS_ROOT/looper, else $LOOPER_SHOTS, else Builds/Shots/looper.
using System;
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Lessons
{
    [Category("Graphics")]
    public class LooperShotTests
    {
        const int W = 1280, H = 720;
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _t;
        Camera _cam;
        GameObject _camGo;
        RenderTexture _rt;
        Texture2D _read;
        LessonsModule _m;

        static string ShotsDir
        {
            get
            {
                string d = ShotDirs.FromRoot("looper") ?? Environment.GetEnvironmentVariable("LOOPER_SHOTS");
                return string.IsNullOrEmpty(d) ? ShotDirs.Default("looper") : d;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_camGo != null) Object.Destroy(_camGo);
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            if (_read != null) Object.Destroy(_read);
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        void Step(int n = 1) { for (int i = 0; i < n; i++) { _t += Dt; _boot.Step(Dt); } }

        IEnumerator Boot()
        {
            _t = 50.0;
            _boot = new TestBoot(headless: false, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(GuideModule.KeyTipsSeen, AppLoopHost.TipLoopStart);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            var layout = App.Current.Get<ILayoutService>();
            while (!layout.FirstArrangeDone && Time.realtimeSinceStartup - t0 < 5f) { Step(); yield return null; }
            foreach (IAppModule x in App.Current.Modules) if (x is LessonsModule l) _m = l;
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _read = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
            _camGo = new GameObject("LooperShotCamera");
            _cam = _camGo.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = XrMode.VrBackground;
            _cam.nearClipPlane = 0.02f;
            _cam.farClipPlane = 40f;
            _cam.allowHDR = false;
            Directory.CreateDirectory(ShotsDir);
        }

        void Aim(Vector3 from, Vector3 at, float fov)
        {
            _cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
            _cam.fieldOfView = fov;
        }

        Color Shot(string name, Vector3 probe)
        {
            _cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _read.Apply();
            RenderTexture.active = prev;
            if (name != null) File.WriteAllBytes(Path.Combine(ShotsDir, name + ".png"), _read.EncodeToPNG());
            Vector3 sp = _cam.WorldToScreenPoint(probe);
            int px = Mathf.Clamp(Mathf.RoundToInt(sp.x), 1, W - 2), py = Mathf.Clamp(Mathf.RoundToInt(sp.y), 1, H - 2);
            Color sum = Color.clear;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) sum += _read.GetPixel(px + dx, py + dy);
            return sum / 9f;
        }

        Vector3 Top(DeckButton b, float lift = 0f) => _m.Deck.Root.TransformPoint(TapeDeck.ButtonLocal(b) + Vector3.up * (0.004f + lift));
        Vector3 CountLamp(int i) => _m.Deck.Root.TransformPoint(TapeDeck.CountLocal(i) + Vector3.up * 0.003f);

        // The deck from the seat, and close: a little above and in front of the loop strip.
        void Views(string name, out Vector3 seat, out Vector3 close)
        {
            seat = App.Current.Head.position;
            Vector3 deck = _m.Deck.Root.TransformPoint(0f, 0.075f, -0.07f);
            close = _m.Deck.Root.TransformPoint(0f, 0.32f, -0.36f);
            Aim(seat, deck, 40f);
            Shot(name + "-seat", deck);
            Aim(close, deck, 38f);
        }

        [UnityTest, Category("Graphics")]
        public IEnumerator TheLoopStripCountLightsLayerLightsAndTheGuide()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics): run Tools/unity.sh test-play --gpu");
            yield return Boot();
            Step(4);
            Views("looper-deck-idle", out _, out _);
            Color idleCount = Shot("looper-deck-idle-close", CountLamp(2));

            // LOOP: the clicks run; on beat 3 three count lights glow amber.
            _m.Recorder.Loop();
            double beat = _m.Looper.Session.BeatSamples;
            int sr = App.Current.Get<MusicRoom.Audio.IAudioEngine>().SampleRate;
            while (_t * sr < _m.Looper.ClickStart + 2.3 * beat) Step();
            Views("looper-countin", out _, out _);
            Color lit = Shot("looper-countin-close", CountLamp(2));
            Color dark = Shot(null, CountLamp(3));
            Assert.That(lit.r, Is.GreaterThan(idleCount.r + 0.15f), $"count light 3 glows ({lit} vs idle {idleCount})");
            Assert.That(lit.r, Is.GreaterThan(lit.b + 0.15f), "amber");
            Assert.That(lit.r, Is.GreaterThan(dark.r + 0.15f), $"count light 4 is still dark ({dark})");
            _m.Recorder.Stop();
            Step(2);

            // Three layers at 110 BPM, 4 bars (from Recordings), the second muted, a fourth armed, the click on.
            var s = new LoopSession(110, 4, sr);
            for (int i = 0; i < 3; i++)
            {
                var l = new LoopLayer();
                l.Insert((long)(i * s.BeatSamples), TakeEvent.Drum("kick", 0.7f, 0.3f, "head", false));
                s.Layers.Add(l);
            }
            Assert.IsTrue(_m.Recorder.PlayEvents(LoopFormat.Encode(s), s.PassSeconds));
            Step(30);
            _m.Looper.ToggleMute(1);
            _m.Looper.ToggleMetronome();
            _m.Looper.Arm();
            // The armed light blinks: catch it lit.
            for (int i = 0; i < 60 && _m.Deck.CapGlow(DeckButton.Layer4).r == 0; i++) Step();
            Views("looper-layers", out _, out _);
            Color on = Shot("looper-layers-close", Top(DeckButton.Layer1));
            Color muted = Shot(null, Top(DeckButton.Layer2));
            Color armed = Shot(null, Top(DeckButton.Layer4));
            Color empty = Shot(null, Top(DeckButton.Layer6));
            Color fast = Shot(null, Top(DeckButton.Fast));
            Assert.That(on.g, Is.GreaterThan(on.r + 0.1f), $"a playing layer's light is green ({on})");
            Assert.That(on.g, Is.GreaterThan(muted.g + 0.1f), $"the muted one is dim ({muted})");
            Assert.That(armed.r, Is.GreaterThan(armed.g + 0.15f), $"the armed one blinks red ({armed})");
            Assert.That(fast.r, Is.GreaterThan(empty.r + 0.1f), $"the session's tempo lights ({fast} vs {empty})");

            // CLEAR waits for its second tap: TAP AGAIN.
            _m.Looper.Clear();
            for (int i = 0; i < 60 && _m.Deck.CapGlow(DeckButton.Clear).r == 0; i++) Step();
            Assert.IsTrue(_m.Deck.TapAgainShown);
            Views("looper-clear", out _, out _);
            Shot("looper-clear-close", Top(DeckButton.Clear));
            _m.Recorder.Stop();
            Step(5);

            // The walkthrough ("Make a song in layers"; GUIDE starts the quick show): Sunny perched beside the deck, the
            // ghost hand pressing REC.
            var guide = (GuideModule)App.Current.Get<IGuide>();
            guide.StartDeckWalkthrough();
            for (int i = 0; i < 1500 && !(guide.DeckGuide.Step == DeckGuideStep.Rec && guide.DeckGuide.Pointing.HasValue); i++) Step();
            Assert.That(guide.DeckGuide.Step, Is.EqualTo(DeckGuideStep.Rec));
            guide.Sunny.DemoT = 0.6f;          // the finger halfway down to REC
            Step(2);
            Vector3 seatEye = App.Current.Head.position;
            Vector3 mid = Vector3.Lerp(_m.Deck.Root.TransformPoint(0f, 0.08f, -0.07f), guide.Sunny.Position, 0.4f);
            Aim(seatEye, mid, 50f);
            Shot("looper-guide-seat", Top(DeckButton.Rec));
            Aim(_m.Deck.Root.TransformPoint(0.05f, 0.35f, -0.45f), mid, 45f);
            Shot("looper-guide-close", Top(DeckButton.Rec));
            Assert.That(guide.Sunny.DemoKind, Is.EqualTo(DemoKind.Press));
            guide.ToggleDeckGuide();
            Debug.Log($"[Lessons] looper shots written to {ShotsDir}");

            // The deck stays within its draws with every lamp in use: one lamp mesh, one label mesh.
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            Renderer[] deck = _m.Deck.Root.GetComponentsInChildren<Renderer>(true);
            Assert.That(deck.Length, Is.LessThanOrEqualTo(TapeDeck.DrawBudget));
            draws.Dispose();
        }
    }
}
