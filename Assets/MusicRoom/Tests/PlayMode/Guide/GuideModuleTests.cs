// The Guide module in a running app (ARCHITECTURE.md sections 15.8 and 16): discovery and registration, the first-launch
// rule over a fresh MemorySettingsStore, presence events for the menu row, the whole tour on a scripted audio clock with
// manual frames (praise >= 0.3 s after the line, swipe_skip at 12 s, Hide gone within 0.8 s), the steps completing from
// the Core contracts and events, the headset rules with a simulated headset, speech asserted on AudioTestTap.Calls (the
// IAudioEngine calls, never the Audio module's recipe commands), the scene objects, the bubble fit with TextMeshPro and
// the allocation-free frame. Every boot goes through TestBoot; most boot Audio + Guide (+ GuideProbe for the menu /
// layout / drums). No test waits real time for anything timed: real frames are only spent waiting for the audio
// workers to render the robotised lines.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Guide
{
    public class GuideModuleTests
    {
        public const float Dt = 1f / 72f;

        TestBoot _boot;
        GuideProbe _probe;
        GuideModule _guide;
        PhaseProbe _phases;
        double _t;   // the scripted audio clock (IAudioEngine.Now / FrameNow, Sunny's line timing)
        readonly List<bool> _presence = new List<bool>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MusicRoom.Guide.GuideModule.ClassicTour = false;
            AudioBootGuard.Reset();
            if (App.Current != null && App.Current.Get<IAudioEngine>() is IAudioEngine e) e.TestTap = null;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        /// Records Sunny's clock in the phases around the Guide phase (Order 55: after the Guide module in each phase) and
        /// every GuidePresenceEvent from the first frame on.
        sealed class PhaseProbe : AppModule
        {
            public override int Order => 55;
            public GuideModule Guide;
            public List<bool> Presence;
            public float AtInputDone = -1f, AtDecor = -1f, AtSwivel = -1f, AtEndFrame = -1f;

            public override void Init(App app) => app.Events.Subscribe<GuidePresenceEvent>(e => Presence?.Add(e.Present));

            public override void Tick(FramePhase phase, float dt)
            {
                if (Guide == null) return;
                float t = Guide.Sunny.Time;
                if (phase == FramePhase.InputDone) AtInputDone = t;
                else if (phase == FramePhase.Decor) AtDecor = t;
                else if (phase == FramePhase.Swivel) AtSwivel = t;
                else if (phase == FramePhase.EndFrame) AtEndFrame = t;
            }
        }

        /// <summary>Boots with manual frames and the scripted clock. tourDone: TourDone set in the fresh store (an earlier
        /// launch finished the tour). filtered: Audio + Guide only (+ the probes); otherwise the whole app.</summary>
        IEnumerator Boot(bool tourDone, bool filtered = true, bool probe = true, bool simulateHeadset = false)
        {
            _presence.Clear();
            _t = 10.0;
            MusicRoom.Guide.GuideModule.ClassicTour = true;   // first five: these tests check the classic five-step tour
            _boot = new TestBoot(headless: true, simulateHeadset: simulateHeadset, manualFrames: true, audioClock: () => _t);
            if (tourDone) _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            if (filtered) _boot.Options.ModuleFilter = t => t == typeof(AudioModule) || t == typeof(GuideModule);
            _probe = probe ? new GuideProbe() : null;
            if (_probe != null) _boot.Options.ExtraModules.Add(_probe);
            _phases = new PhaseProbe { Presence = _presence };
            _boot.Options.ExtraModules.Add(_phases);
            AudioBootGuard.Begin();
            yield return _boot.Start();
            yield return null;
            AudioBootGuard.End();
            _guide = App.Current.Get<IGuide>() as GuideModule;
            Assert.That(_guide, Is.Not.Null, "GuideModule registers IGuide");
            Assert.That(_guide.View, Is.Not.Null, "GuideModule.Init completed");
            Assert.That(App.Current.Settings.Store, Is.SameAs(_boot.Settings), "the test's own store, never PlayerPrefs");
            _phases.Guide = _guide;
        }

        /// <summary>n frames of 1/72 s; the scripted audio clock moves with them.</summary>
        void Step(int n = 1)
        {
            for (int i = 0; i < n; i++)
            {
                _t += Dt;
                _boot.Step(Dt);
            }
        }

        /// <summary>Steps until done() (checked after each step); returns the number of steps taken.</summary>
        int StepUntil(Func<bool> done, int max, string what)
        {
            for (int i = 1; i <= max; i++)
            {
                Step();
                if (done()) return i;
            }
            Assert.Fail($"{what}: not reached within {max} steps (state {_guide.Sunny.State}, step {_guide.Sunny.Step})");
            return -1;
        }

        /// <summary>Real frames while the audio workers render the robotised lines (the App itself does not tick).</summary>
        public static IEnumerator WaitForLines(IAudioEngine engine, params string[] ids)
        {
            var voice = new EngineVoice(engine);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 30f)
            {
                bool all = true;
                foreach (string id in ids) all &= voice.LineReady(id);
                if (all) yield break;
                yield return null;
            }
            Assert.Fail("the robotised voice lines were not rendered within 30 s");
        }

        /// <summary>The engine calls as letters: O open chirp, C close chirp, V voice line, S stop.</summary>
        static string Pattern(AudioTestTap tap)
        {
            var sb = new StringBuilder();
            foreach (AudioTestTap.Call c in tap.Calls)
            {
                if (c.Method == AudioCall.Chirp) sb.Append(c.Index == (int)ChirpKind.Open ? 'O' : 'C');
                else if (c.Method == AudioCall.VoiceLine) sb.Append('V');
                else if (c.Method == AudioCall.StopVoiceLine) sb.Append('S');
            }
            return sb.ToString();
        }

        static List<AudioTestTap.Call> Lines(AudioTestTap tap) => tap.Calls.FindAll(c => c.Method == AudioCall.VoiceLine);

        static string[] LineIds(AudioTestTap tap) => Lines(tap).ConvertAll(c => c.Line).ToArray();

        // =====================================================================================================

        [UnityTest]
        public IEnumerator RegistersAtOrderFiftyAndCarriesTheGuideSource()
        {
            yield return Boot(tourDone: true);
            Assert.That(_guide.Order, Is.EqualTo(50));
            Assert.That(GuideModule.ModuleOrder, Is.EqualTo(50));
            Assert.That(App.Current.Get<IGuide>(), Is.SameAs(_guide));
            var engine = App.Current.Get<IAudioEngine>();
            Assert.That(engine.Source(SourceId.Guide).transform.parent, Is.SameAs(_guide.View.Root), "the Guide source follows Sunny's root");
            Assert.That(engine.Source(SourceId.Guide).transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.IsTrue(_guide.View.Root.gameObject.activeInHierarchy, "the root (and the source) stay active while Sunny is hidden");
            Assert.IsFalse(_guide.View.Visual.gameObject.activeSelf);
            Assert.IsFalse(_guide.Present);
            Assert.IsFalse(_guide.Done);
            Assert.IsFalse(_guide.IsPresenting, "no headset and no simulated one: the desktop lines");
        }

        [UnityTest]
        public IEnumerator SunnyUpdatesInTheGuidePhaseBetweenDecorAndSwivel()
        {
            yield return Boot(tourDone: true);
            _guide.Start();
            _t += 0.02;
            _boot.Step(0.02f);   // one whole frame (ARCHITECTURE.md section 4, step 16)
            Assert.That(_phases.AtDecor, Is.EqualTo(_phases.AtInputDone), "nothing moves Sunny before its phase");
            Assert.That(_phases.AtSwivel, Is.EqualTo(_phases.AtDecor + 0.02f).Within(1e-5f), "Sunny ticks with Clock.Dt between Decor and Swivel");
            Assert.That(_phases.AtEndFrame, Is.EqualTo(_phases.AtSwivel), "and only once per frame");
        }

        // ---------------- first launch (AppSettings.TourDone in the injected store) ----------------

        [UnityTest]
        public IEnumerator TheFirstLaunchStartsTheTourByItselfOnce()
        {
            yield return Boot(tourDone: false);
            Assert.IsFalse(_boot.Settings.HasKey(AppSettings.KeyTourDone), "a fresh store: the first launch");
            var engine = App.Current.Get<IAudioEngine>();
            yield return WaitForLines(engine, GuideLines.IntroDesk, GuideLines.Leave);
            var tap = new AudioTestTap();
            engine.TestTap = tap;

            _probe.FirstArrangeDone = true;   // the first arranged frame (with the HMD mounted: always in the editor)
            Step();
            Assert.IsTrue(_guide.Present, "Sunny appears on the first launch");
            Assert.IsTrue(_guide.AutoStarted);
            Assert.IsFalse(_guide.TourDoneAtBoot);
            Assert.That(_guide.Sunny.LineId, Is.EqualTo(GuideLines.IntroDesk), "scripted hands and no headset: the desktop lines");
            Assert.IsTrue(_guide.Sunny.TalkStarted, "the intro is voiced (its buffer was ready)");
            Assert.That(Pattern(tap), Is.EqualTo("OV"), "the open chirp, then the line");
            Assert.That(LineIds(tap), Is.EqualTo(new[] { GuideLines.IntroDesk }));
            Assert.That(Lines(tap)[0].When, Is.EqualTo(_t + GuideTiming.Lead).Within(1e-9), "voiced at Now + 0.14 on the audio clock");
            Assert.That(_presence, Is.EqualTo(new[] { true }));

            // Hide it: TourDone is written to the store, and it never starts by itself again in this launch.
            _guide.Dismiss();
            Assert.That(_boot.Settings.GetInt(AppSettings.KeyTourDone, 0), Is.EqualTo(1));
            Assert.That(_boot.Settings.Saves, Is.GreaterThanOrEqualTo(1), "Settings.Save() after the write");
            StepUntil(() => _guide.Sunny.State == SunnyState.Gone, 72, "gone");
            Step(72 * 5);
            Assert.IsFalse(_guide.Present);
            Assert.That(_presence, Is.EqualTo(new[] { true, false }));
        }

        [UnityTest]
        public IEnumerator ATourDoneOnAnEarlierLaunchNeverStartsByItself()
        {
            yield return Boot(tourDone: true);
            _probe.FirstArrangeDone = true;
            Step(864);   // 12 s of app time
            Assert.IsTrue(_guide.TourDoneAtBoot);
            Assert.IsTrue(_guide.AutoStarted, "the rule ran and declined");
            Assert.IsFalse(_guide.Present);
            Assert.That(_guide.Sunny.State, Is.EqualTo(SunnyState.Idle));
            Assert.That(_presence, Is.Empty);
        }

        // ---------------- the tour on the scripted clock ----------------

        [UnityTest]
        public IEnumerator TheWholeTourRunsOnTheScriptedAudioClock()
        {
            yield return Boot(tourDone: true);
            var engine = App.Current.Get<IAudioEngine>();
            // The tour's own lines (with 200+ lines in the table, waiting for every one would make them all resident at once).
            yield return WaitForLines(engine, "intro_desk", "open_desk", "praise_0", "poke_desk", "praise_1", "close_desk", "praise_2",
                "swipe_desk", "swipe_skip", "nod_desk", "praise_4", "bye", "leave");
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            Sunny s = _guide.Sunny;

            _guide.Start();
            Assert.That(Pattern(tap), Is.EqualTo("OV"));
            double introAt = _t;
            Assert.That(Lines(tap)[0].When, Is.EqualTo(introAt + GuideTiming.Lead).Within(1e-9));
            Assert.That(s.TalkEndDsp, Is.EqualTo(introAt + GuideTiming.Lead + engine.VoiceLineSeconds(GuideLines.IntroDesk)).Within(1e-6));

            // The intro holds max(3.2, lineSeconds + .45) and then waits for 0.3 s of quiet.
            StepUntil(() => s.State == SunnyState.Step && s.Step == 0, 72 * 10, "step 0");
            Assert.That(_t - introAt, Is.GreaterThanOrEqualTo(s.Hold(GuideLines.IntroDesk, GuideTiming.IntroT) - Dt));

            // Step 0 (open): completed mid-line. Sunny smiles at once, but the praise waits for the line + 0.3 s.
            Step(10);
            Assert.IsTrue(s.Speaking(), "open_desk is being voiced");
            double openEnd = s.TalkEndDsp;
            Assert.That(_t, Is.LessThan(openEnd));
            _probe.Show();                                                     // MenuEvent Open
            Assert.That(s.State, Is.EqualTo(SunnyState.Wait));
            Assert.That(s.Mood, Is.EqualTo(SunnyMood.Happy));
            Assert.IsFalse(s.Demo.Visible, "the demo stops at once");
            _probe.Show();                                                     // repeated gestures queue nothing more
            StepUntil(() => s.State == SunnyState.Praise, 72 * 10, "praise 0");
            AudioTestTap.Call praise0 = Lines(tap)[Lines(tap).Count - 1];
            Assert.That(praise0.Line, Is.EqualTo("praise_0"));
            Assert.That(praise0.When, Is.GreaterThanOrEqualTo(openEnd + GuideTiming.Gap), "praise >= 0.3 s after the line in flight ended");

            // Steps 1 and 2 (poke, close) complete from the menu's events.
            StepUntil(() => s.State == SunnyState.Step && s.Step == 1, 72 * 10, "step 1");
            _probe.Go(MenuScreen.Lessons);                                     // MenuEvent Screen
            StepUntil(() => s.State == SunnyState.Step && s.Step == 2, 72 * 15, "step 2");
            _probe.Hide();                                                     // MenuEvent Close
            StepUntil(() => s.State == SunnyState.Step && s.Step == 3, 72 * 15, "step 3");

            // Step 3 (swipe): nothing happens (the probe's layout stays in All mode), so it skips after 12 s = 864 steps.
            double swipeAt = _t;
            int waited = StepUntil(() => s.State != SunnyState.Step, 72 * 20, "swipe timeout");
            Assert.That(waited, Is.InRange(864, 865), "12 s of 1/72 s frames");
            Assert.That(s.State, Is.EqualTo(SunnyState.Skip), "swipe_desk ended long ago: the skip line follows at once");
            AudioTestTap.Call skip = Lines(tap)[Lines(tap).Count - 1];
            Assert.That(skip.Line, Is.EqualTo("swipe_skip"));
            Assert.That(skip.When - GuideTiming.Lead - swipeAt, Is.EqualTo(GuideTiming.StepTimeout).Within(1.5 * Dt));

            // Step 4 (nod): a kick on the desktop completes it (RecorderEvent through App.Events).
            StepUntil(() => s.State == SunnyState.Step && s.Step == 4, 72 * 15, "step 4");
            _probe.Kick(duringPlayback: false);
            Assert.That(s.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise));
            StepUntil(() => s.State == SunnyState.Bye, 72 * 15, "bye");
            Assert.IsTrue(_guide.Done);
            Assert.That(_boot.Settings.GetInt(AppSettings.KeyTourDone, 0), Is.EqualTo(1), "finishing the run sets TourDone");
            StepUntil(() => s.State == SunnyState.Leave, 72 * 10, "leave");
            Assert.That(_presence, Is.EqualTo(new[] { true, false }), "present from the start, absent once it leaves");
            int gone = StepUntil(() => s.State == SunnyState.Gone, 72, "gone");
            Assert.That(gone, Is.LessThanOrEqualTo(Mathf.CeilToInt(GuideTiming.LeaveT / Dt)), "gone within 0.8 s");

            // Every line of the run, in order, each with its chirps, never two at once, and 0.3 s of quiet in between.
            Assert.That(LineIds(tap), Is.EqualTo(new[]
            {
                "intro_desk", "open_desk", "praise_0", "poke_desk", "praise_1", "close_desk", "praise_2",
                "swipe_desk", "swipe_skip", "nod_desk", "praise_4", "bye", "leave",
            }));
            var sb = new StringBuilder();
            for (int i = 0; i < 13; i++) sb.Append("OVC");
            Assert.That(Pattern(tap), Is.EqualTo(sb.ToString()), "each line: open chirp, line, closing chirp on its natural end");
            List<AudioTestTap.Call> lines = Lines(tap);
            for (int i = 1; i < lines.Count; i++)
            {
                double prevEnd = lines[i - 1].When + engine.VoiceLineSeconds(lines[i - 1].Line);
                Assert.That(lines[i].When - prevEnd, Is.GreaterThanOrEqualTo(GuideTiming.Gap), lines[i].Line + " after " + lines[i - 1].Line);
            }
        }

        [UnityTest]
        public IEnumerator HideSunnyCutsTheVoicePersistsTheFlagAndLeavesWithinPointEightSeconds()
        {
            yield return Boot(tourDone: false);
            var engine = App.Current.Get<IAudioEngine>();
            yield return WaitForLines(engine, GuideLines.IntroDesk, GuideLines.Leave);
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            _guide.Start();   // from the menu, before the auto-start: it does not start twice
            Step();
            Assert.IsTrue(_guide.Present);
            Assert.IsTrue(_guide.View.Visual.gameObject.activeSelf);
            Assert.That(_guide.View.Light.enabled, Is.EqualTo(GuideModule.LightEnabled));
            Step(30);
            Assert.IsTrue(_guide.Sunny.Speaking());

            _guide.Dismiss();
            Assert.IsFalse(_guide.Present, "the menu row reads \"Show me around\" at once");
            Assert.IsTrue(_guide.Done);
            Assert.That(_boot.Settings.GetInt(AppSettings.KeyTourDone, 0), Is.EqualTo(1), "Dismiss sets TourDone in the store");
            Assert.That(_presence, Is.EqualTo(new[] { true, false }));
            Assert.That(Pattern(tap), Is.EqualTo("OVSOV"), "the intro is cut (StopVoiceLine), then the goodbye is voiced");
            Assert.That(LineIds(tap), Is.EqualTo(new[] { GuideLines.IntroDesk, GuideLines.Leave }));

            int steps = StepUntil(() => _guide.Sunny.State == SunnyState.Gone, 72, "gone");
            Assert.That(steps, Is.LessThanOrEqualTo(Mathf.CeilToInt(GuideTiming.LeaveT / Dt)), "gone within 0.8 s (58 steps)");
            Assert.IsFalse(_guide.View.Visual.gameObject.activeSelf);
            Assert.IsFalse(_guide.View.Light.enabled);
            Assert.IsTrue(_guide.AutoStarted, "started by hand first: the auto-start never fires");
            Step(72);
            Assert.IsFalse(_guide.Present);
            _guide.Toggle();
            Assert.IsTrue(_guide.Present, "\"Show me around\" brings it back");
            Assert.That(_presence, Is.EqualTo(new[] { true, false, true }));
        }

        // ---------------- the steps' contracts and the headset rules ----------------

        [UnityTest]
        public IEnumerator StepsCompleteFromTheMenuLayoutNodAndRecorderEvents()
        {
            yield return Boot(tourDone: true);
            Sunny s = _guide.Sunny;
            _guide.Start();
            Step();
            s.BeginStep(0);
            _probe.Show();                                    // MenuEvent Open
            Assert.That(s.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise), "open");
            s.BeginStep(1);
            _probe.Go(MenuScreen.Lessons);                    // MenuEvent Screen
            Assert.That(s.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise), "poke");
            s.BeginStep(2);
            _probe.Hide();                                    // MenuEvent Close
            Assert.That(s.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise), "close");
            s.BeginStep(3);
            _probe.Set(LayoutMode.Single, InstrumentKind.Piano);   // the first instrument seen
            Assert.That(s.State, Is.EqualTo(SunnyState.Step));
            _probe.Set(LayoutMode.Single, InstrumentKind.Drums);   // LayoutChangedEvent: a page turn
            Assert.That(s.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise), "swipe");
            s.BeginStep(4);
            App.Current.Events.Publish(new NodEvent { Velocity = 1f });
            Assert.That(s.Nodded, Is.EqualTo(1));
            Step();
            Assert.That(s.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise), "nod");
            s.BeginStep(4);
            _probe.Kick(duringPlayback: true);
            App.Current.Events.Publish(new RecorderEvent { Event = TakeEvent.Drum("snare", 0.7f, 0.3f, "head", false) });
            Assert.That(s.State, Is.EqualTo(SunnyState.Step), "playback and other drums never count");
            _probe.Kick(duringPlayback: false);
            Assert.That(s.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise), "a desktop kick");
        }

        [UnityTest]
        public IEnumerator InASimulatedHeadsetSunnySpeaksTheHandsLinesAndOnlyANodKickCounts()
        {
            yield return Boot(tourDone: true, simulateHeadset: true);
            Assert.IsTrue(App.Current.IsPresenting, "BootOptions.SimulateHeadset");
            Assert.IsFalse(App.Current.IsHeadset, "the hands are scripted, not the tracker: the gate is IsPresenting");
            Assert.IsTrue(_guide.IsPresenting);
            Sunny s = _guide.Sunny;
            _guide.Start();
            Assert.IsFalse(s.Desktop);
            Assert.That(s.LineId, Is.EqualTo(GuideLines.IntroVr));
            Step();
            s.BeginStep(3);
            Assert.That(s.LineId, Is.EqualTo("swipe_vr"));
            s.BeginStep(4);
            Assert.That(s.LineId, Is.EqualTo("nod_vr"));
            // In the headset only a kick with every hand far from the pad (the nod kick) counts.
            _probe.PadFar = false;
            _probe.Kick(false);
            Assert.That(s.State, Is.EqualTo(SunnyState.Step), "a kick played by hand on the pad");
            Assert.That(_probe.LastMetres, Is.EqualTo(GuideTiming.PadFar));
            Assert.That(_probe.LastPiece, Is.EqualTo(5), "the kick pad");
            _probe.PadFar = true;
            _probe.Kick(false);
            Assert.That(s.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise), "a headset kick away from the pad");
        }

        // ---------------- the voice through the real engine ----------------

        [UnityTest]
        public IEnumerator TheVoiceRunsThroughTheEngine()
        {
            yield return Boot(tourDone: true);
            var engine = App.Current.Get<IAudioEngine>();
            var voice = new EngineVoice(engine);
            Assert.IsTrue(voice.Available);
            Assert.IsTrue(voice.HasLine(GuideLines.IntroDesk));
            Assert.That(voice.LineSeconds(GuideLines.IntroDesk), Is.EqualTo(GuideLines.ManifestSeconds[1]).Within(0.002f), "clip length = manifest");
            Assert.That(voice.Now, Is.EqualTo(_t), "Sunny times its lines on IAudioEngine.Now (the scripted clock here)");
            yield return WaitForLines(engine, GuideLines.IntroDesk, "praise_1", "swipe_desk");
            var bank = ((AudioEngine)engine).Bank.Get(AudioEngine.VoiceSlot0 + GuideLines.IndexOf(GuideLines.IntroDesk));
            double sum = 0;
            foreach (float v in bank) sum += v * v;
            Assert.That(Math.Sqrt(sum / bank.Length), Is.GreaterThan(0.01), "the processed line is not silent");

            var tap = new AudioTestTap();
            engine.TestTap = tap;
            _guide.Start();
            Assert.That(tap.Count(AudioCall.Chirp), Is.EqualTo(1), "the opening chirp");
            Assert.That(tap.Calls[0].Method, Is.EqualTo(AudioCall.Chirp));
            Assert.That(tap.Calls[0].Index, Is.EqualTo((int)ChirpKind.Open));
            AudioTestTap.Call line = tap.Calls.Find(c => c.Method == AudioCall.VoiceLine);
            Assert.That(line.Line, Is.EqualTo(GuideLines.IntroDesk));
            Assert.That(line.When, Is.EqualTo(_t + GuideTiming.Lead).Within(1e-9), "the first word 0.14 s after the chirp");
            Assert.IsTrue(_guide.Sunny.TalkStarted);

            // A short line runs its course: the mouth opens on the voice level, then the closing chirp.
            Step();
            tap.Clear();
            _guide.Sunny.Say("praise_1");
            bool mouth = false;
            float level = 0f;
            for (int i = 0; i < 216 && !_guide.Sunny.TalkEnded; i++)
            {
                Step();
                mouth |= _guide.Sunny.MouthOpen;
                level = Mathf.Max(level, engine.VoiceLevel);
            }
            Step();
            Assert.IsTrue(_guide.Sunny.TalkEnded);
            Assert.That(level, Is.GreaterThan(GuideTiming.MouthFloor), "VoiceLevel follows the playing line");
            Assert.IsTrue(mouth, "the mouth opened");
            Assert.That(Pattern(tap), Is.EqualTo("SOVC"), "the intro in flight is cut first, then the closing chirp on the natural end");

            // Taking the headset off cuts a line in flight: StopVoiceLine, and no closing chirp follows.
            tap.Clear();
            _guide.Sunny.Say("swipe_desk");
            Step(22);
            Assert.IsTrue(_guide.Sunny.Speaking());
            Assert.IsTrue(engine.VoiceLinePlaying);
            App.Current.Events.Publish(new HmdMountEvent { Mounted = false });
            Assert.IsTrue(_guide.Sunny.TalkCancelled);
            Assert.IsFalse(engine.VoiceLinePlaying);
            Step(72);   // (short of the intro's hold, so the tour does not move on to its next line)
            Assert.That(Pattern(tap), Is.EqualTo("OVS"), "no chirp for a cut line");
        }

        // ---------------- the scene ----------------

        [UnityTest]
        public IEnumerator SunnyIsBuiltToTheReferenceAndFollowsItsState()
        {
            yield return Boot(tourDone: true);
            SunnyView v = _guide.View;
            Assert.That(v.Body.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3, Is.EqualTo(520), "SphereGeometry(.045, 20, 14)");
            Assert.That(v.Corona.localPosition.z, Is.EqualTo(0.027f).Within(1e-6f), "behind the body");
            Assert.That(v.Bubble.localPosition.x, Is.EqualTo(0.0832f).Within(1e-5f));
            Assert.That(v.Bubble.localPosition.y, Is.EqualTo(0.1163f).Within(1e-5f));
            Assert.That(v.Bubble.localPosition.z, Is.EqualTo(-0.01f).Within(1e-6f));
            Assert.That(v.Hand.localPosition, Is.EqualTo(SunnyDemo.HandRoot));
            // Draw order (render queues): corona 4 < body 5 < bubble 6 (+ its caption) < demo 7.
            int[] queues =
            {
                v.CoronaMaterial.renderQueue, v.BodyMaterial.renderQueue, v.BubbleMaterial.renderQueue,
                v.TextMaterial != null ? v.TextMaterial.renderQueue : GuideMaterialTemplates.QueueText, v.DemoMaterial.renderQueue,
            };
            for (int i = 1; i < queues.Length; i++) Assert.That(queues[i], Is.GreaterThan(queues[i - 1]));
            Assert.That(v.BodyMaterial.shader.name, Is.EqualTo(GuideMaterialTemplates.UnlitShader), "template made by render-setup");
            Assert.That(v.CoronaMaterial.shader.name, Is.EqualTo(GuideMaterialTemplates.CoronaShader));
            Assert.That(v.BubbleMaterial.GetFloat(GuideMaterialTemplates.ZTestId), Is.EqualTo((float)UnityEngine.Rendering.CompareFunction.Always), "drawn over everything");
            // First five: the ghost hand is pale, warm and alpha-blended (no additive cyan glow).
            Assert.That(v.DemoMaterial.GetFloat(GuideMaterialTemplates.AdditiveId), Is.EqualTo(0f));
            Assert.That(v.DemoMaterial.GetFloat(GuideMaterialTemplates.SrcBlendId), Is.EqualTo((float)UnityEngine.Rendering.BlendMode.SrcAlpha));
            Assert.That(v.DemoMaterial.GetFloat(GuideMaterialTemplates.DstBlendId), Is.EqualTo((float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            Color demo = v.DemoMaterial.GetColor(MaterialLibrary.BaseColor);
            Assert.That(demo.r, Is.GreaterThan(demo.b), "warm, not cyan");
            Assert.That(demo.a, Is.EqualTo(SunnyView.DemoAlpha).Within(1e-4f));
            Assert.IsTrue(v.HasText, "TextMeshPro caption");
            Assert.That(v.TextMaterial.shader.name, Is.EqualTo(GuideMaterialTemplates.TextShader));
            Assert.That(v.Light.type, Is.EqualTo(LightType.Point));
            Assert.That(v.Light.range, Is.EqualTo(1.4f));
            Assert.That(v.Light.color.r, Is.EqualTo(1f).Within(0.01f));
            Assert.That(v.Light.color.b, Is.LessThan(0.5f));
            Assert.That(v.Light.shadows, Is.EqualTo(LightShadows.None));
            Assert.IsFalse(v.Light.enabled, "dark while Sunny is hidden");

            _guide.Start();
            Step(2);
            Sunny s = _guide.Sunny;
            Assert.IsTrue(v.Visual.gameObject.activeSelf);
            Assert.IsTrue(v.Bubble.gameObject.activeSelf);
            Assert.That(v.CaptionText, Is.EqualTo(GuideLines.TextOf(GuideLines.IntroDesk)));
            Assert.That(Vector3.Distance(v.Root.position, s.Position), Is.LessThan(1e-5f));
            Assert.That(v.FaceIndex, Is.EqualTo(SunnyFace.StateIndex(true, s.EyesShut, s.MouthOpen)));
            Vector3 head = App.Current.Head.position;
            Assert.That(Vector3.Dot(v.Root.rotation * Vector3.back, (head - v.Root.position).normalized), Is.GreaterThan(0.99f), "the face looks at the player");
            Assert.That(v.Light.enabled, Is.EqualTo(GuideModule.LightEnabled));
            Assert.That(v.Light.intensity, Is.EqualTo(s.LightIntensity * SunnyView.LightScale).Within(1e-5f));
            // The palm demo: the hand and ring are shown and posed from the demo clock.
            s.BeginStep(0);
            s.DemoT = 0.75f;
            Step();
            Assert.IsTrue(v.Demo.gameObject.activeSelf);
            Assert.IsTrue(v.Ring.gameObject.activeSelf, "the charging ring at t .6 .. 1.1");
            Assert.IsFalse(v.Ghost.gameObject.activeSelf);
            Assert.That(Quaternion.Angle(v.Hand.localRotation, s.Demo.HandRotation), Is.LessThan(0.01f));
            s.BeginStep(3);
            Step();
            Assert.IsTrue(v.Arrow.gameObject.activeSelf, "the swipe's dotted arrow");
            Assert.IsFalse(v.Ring.gameObject.activeSelf);
            s.BeginStep(4);
            Step();
            Assert.IsFalse(v.Demo.gameObject.activeSelf, "Sunny nods itself");
        }

        // ARCHITECTURE.md sections 14 / 17: Sunny <= 6 draws. Each enabled renderer with one material is one draw.
        static int Draws(SunnyView v)
        {
            int n = 0;
            foreach (Renderer r in v.Root.GetComponentsInChildren<Renderer>(false))
                if (r.enabled && r.gameObject.activeInHierarchy) n += r.sharedMaterials.Length;
            return n;
        }

        [UnityTest]
        public IEnumerator SunnyStaysWithinItsDrawBudgetInEveryDemo()
        {
            yield return Boot(tourDone: true);
            SunnyView v = _guide.View;
            Sunny s = _guide.Sunny;
            _guide.Start();
            Step();
            Assert.That(Draws(v), Is.EqualTo(4), "body, corona, bubble, caption");
            // Palm with the charging ring, poke with the ghost panel and its row, palm again, swipe with the arrow, the nod.
            float[] demoT = { 0.8f, 0.4f, 0.8f, 0.32f, 0.3f };
            for (int step = 0; step < 5; step++)
            {
                s.BeginStep(step);
                s.DemoT = demoT[step];
                Step();
                Assert.That(Draws(v), Is.LessThanOrEqualTo(6), "step " + step);
            }
            // The merged demo mesh carries each part's opacity: on the poke step the row plate glows, the ring is hidden.
            s.BeginStep(1);
            s.DemoT = 0.6f;
            Step();
            Assert.That(s.Demo.Ghost, Is.True);
            Assert.That(v.DemoMesh.PartAlpha(SunnyDemoMesh.Row), Is.EqualTo(s.Demo.RowOpacity).Within(0.003f));
            Assert.That(v.DemoMesh.PartAlpha(SunnyDemoMesh.Ring), Is.EqualTo(0f));
            Assert.That(v.DemoMesh.PartAlpha(SunnyDemoMesh.Palm), Is.EqualTo(SunnyDemo.Opacity).Within(0.003f));
            // The palm part's vertices sit where the Hand anchor puts them (the same place as the old separate mesh).
            Vector3[] verts = v.DemoMesh.Mesh.vertices;
            Vector3 centre = Vector3.zero;
            int palmVerts = SunnyMeshes.Palm.vertexCount;   // first five: a rounded palm (was a 24-vertex box)
            for (int i = 0; i < palmVerts; i++) centre += verts[i];
            centre /= palmVerts;
            Assert.That(Vector3.Distance(v.Demo.TransformPoint(centre), v.Hand.position), Is.LessThan(1e-4f), "palm box centred on the hand");
        }

        [UnityTest]
        public IEnumerator EveryLineFitsTheBubbleInTwoLines()
        {
            yield return Boot(tourDone: true);
            SunnyView v = _guide.View;
            Assert.IsTrue(v.HasText, "TextMeshPro caption");
            for (int i = 0; i < GuideLines.Count; i++)
            {
                v.Fit(GuideLines.Texts[i]);
                Assert.That(v.BubbleLines, Is.InRange(1, 2), GuideLines.Ids[i]);
                Assert.That(v.BubbleFontPx, Is.InRange(30f, 54f));
                Assert.That((54f - v.BubbleFontPx) % 4f, Is.EqualTo(0f), "54 down to 30 in steps of 4");
            }
            v.Fit("Nice!");
            Assert.That(v.BubbleFontPx, Is.EqualTo(54f), "short lines stay large");
            Assert.That(v.BubbleLines, Is.EqualTo(1));
            // A bubble on two lines
            v.Fit("Swipe your open hand sideways to turn to another instrument");
            Assert.That(v.BubbleLines, Is.EqualTo(2));
            Assert.That(v.BubbleFontPx, Is.GreaterThanOrEqualTo(38f));
        }

        [UnityTest]
        public IEnumerator AGuideFrameAllocatesNothing()
        {
            yield return Boot(tourDone: true);
            _guide.Start();
            Step();
            _guide.Sunny.BeginStep(0);   // the palm demo loops while the open step waits (the probe's panel stays shut)
            Step(30);
            Assert.That(App.Current.Get<IAudioEngine>().TestTap, Is.Null);
            Assert.That(_guide.Sunny.State, Is.EqualTo(SunnyState.Step));
            Assert.That(() => { s_keep = new float[1024]; }, Is.AllocatingGCMemory(), "the recorder works here");
            Assert.That(() =>
            {
                for (int i = 0; i < 120; i++)
                {
                    _guide.Tick(FramePhase.Guide, Dt);
                    if (_guide.Present && _guide.Done) s_flag++;
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.That(_guide.Sunny.State, Is.EqualTo(SunnyState.Step), "no state change inside the measured window");
        }

        static float[] s_keep;
        static int s_flag;

        [UnityTest]
        public IEnumerator TheWholeAppDiscoversTheGuideAndTogglesIt()
        {
            yield return Boot(tourDone: true, filtered: false, probe: false);
            var app = App.Current;
            int at = -1;
            for (int i = 0; i < app.Modules.Count; i++) if (ReferenceEquals(app.Modules[i], _guide)) at = i;
            Assert.That(at, Is.GreaterThanOrEqualTo(0));
            for (int i = 0; i < app.Modules.Count; i++)
                if (app.Modules[i].Order < 50) Assert.That(i, Is.LessThan(at));
            // The desktop rig comes up on its own (real time, about 0.5 s), then the layout's first arrange (app frames).
            float t0 = Time.realtimeSinceStartup;
            while (app.Rig.State != XrState.Desktop && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Step(5);
            _guide.Toggle();
            Assert.IsTrue(_guide.Present);
            Assert.That(_guide.Sunny.LineId, Is.EqualTo(GuideLines.IntroDesk));
            Step(36);
            Assert.IsTrue(_guide.Present);
            Assert.That(Vector3.Distance(_guide.Sunny.Base, _guide.Sunny.Goal()), Is.LessThan(0.05f), "beside the head");
            _guide.Toggle();
            Assert.IsFalse(_guide.Present);
            int steps = StepUntil(() => _guide.Sunny.State == SunnyState.Gone, 72, "gone");
            Assert.That(steps, Is.LessThanOrEqualTo(Mathf.CeilToInt(GuideTiming.LeaveT / Dt)));
            Assert.That(_presence, Is.EqualTo(new[] { true, false }));
        }
    }
}
