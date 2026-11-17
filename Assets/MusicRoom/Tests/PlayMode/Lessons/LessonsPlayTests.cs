// The Lessons module inside the booted app (ARCHITECTURE.md sections 10, 15.7, 16): services and the deck on the
// side-table rig, a scripted fingertip (FakeHandSource) pressing REC / STOP with the take auto-saved, the tutorial ticking
// in the Tutorial phase with its glow in the pool before the HoverGlow phase, hits from the RecorderEvent stream, blocks
// on the instruments' own geometry (whatever implementation registers IPianoApi / IDrumsApi / IHarpApi), the layout
// stopping a lesson whose instrument left, the desktop stand-ins on the app's real mouse-hand and presenting state, and
// the hot paths allocating nothing. Every boot goes through TestBoot (fresh MemorySettingsStore, temporary DataPath).
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Lessons
{
    public class LessonsPlayTests
    {
        /// Records what the frame looks like right after the Tutorial phase (StickHover runs next, HoverGlow after it).
        sealed class PhaseProbe : AppModule
        {
            public override int Order => 45;
            App _app;
            public double PulseAtInstruments = double.NaN, PulseAtStickHover = double.NaN;
            public int GlowsAfterTutorial = -1;
            public HoverGlowSpec FirstGlow;
            public override void Init(App app) { _app = app; }
            public override void Tick(FramePhase phase, float dt)
            {
                var t = _app.Get<ITutorial>() as Tutorial;
                if (t == null) return;
                if (phase == FramePhase.Instruments) PulseAtInstruments = t.PulseTime;
                else if (phase == FramePhase.StickHover)
                {
                    PulseAtStickHover = t.PulseTime;
                    GlowsAfterTutorial = _app.HoverGlow.Pending;
                    if (_app.HoverGlow.Pending > 0) FirstGlow = _app.HoverGlow.Spec(0);
                }
            }
        }

        static float[] s_keep;
        TestBoot _boot;

        IEnumerator Boot(TestBoot boot = null, params IAppModule[] extra)
        {
            _boot = boot ?? new TestBoot();
            foreach (IAppModule m in extra) _boot.Options.ExtraModules.Add(m);
            yield return _boot.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        static int PackLessons(App app)
        {
            int n = 0;
            foreach (Lesson l in app.Get<ILessonLibrary>().Lessons)
                if (app.Registry.TryParse(l.instrument, out InstrumentKind k) && app.Registry.Get(k).Lessons?.Pack != null) n++;
            return n;
        }

        static LessonsModule Module()
        {
            foreach (IAppModule m in App.Current.Modules) if (m is LessonsModule l) return l;
            return null;
        }

        static IEnumerator WaitForFirstArrange()
        {
            float t0 = Time.realtimeSinceStartup;
            var layout = App.Current.Get<ILayoutService>();
            while ((layout == null || !layout.FirstArrangeDone) && Time.realtimeSinceStartup - t0 < 3f) { yield return null; layout = App.Current.Get<ILayoutService>(); }
            yield return null;
        }

        static void Burn(Tutorial t) { t.Update(1.1f, null); t.Update(1.1f, null); t.Update(1.1f, null); }

        // A right hand pointing straight down with the index tip at a world point; the other tips are lifted 30 cm so
        // only the index finger can touch the deck (no pinch, no grasp).
        static HandPose IndexAt(Vector3 tip)
        {
            HandPose p = HandPose.OpenFacing(HandSide.Right, tip + Vector3.up * 0.15f, Vector3.down, Vector3.forward).WithPointing().WithIndexTipAt(tip);
            foreach (HandJoint j in new[] { HandJoint.ThumbTip, HandJoint.MiddleTip, HandJoint.RingTip, HandJoint.LittleTip }) p.Position[(int)j] += Vector3.up * 0.3f;
            return p;
        }

        static Vector3 Above(TapeDeck deck, DeckButton b, float y)
        {
            Vector3 l = TapeDeck.ButtonLocal(b);
            return deck.Root.TransformPoint(new Vector3(l.x, y, l.z));
        }

        [UnityTest]
        public IEnumerator TheModuleRegistersItsServicesAndPutsTheDeckOnTheTableRig()
        {
            yield return Boot();
            App app = App.Current;
            LessonsModule m = Module();
            Assert.That(m, Is.Not.Null);
            Assert.That(m.Order, Is.EqualTo(40));
            Assert.That(app.Get<IRecorder>(), Is.SameAs(m.Recorder));
            Assert.That(app.Get<ITutorial>(), Is.SameAs(m.Tutorial));
            // lessons.json's 36 (first five: lessons-proposed.json) plus every instrument's pack (platform.md 7.1: the five stubs' one lesson each in phase 0).
            Assert.That(PackLessons(app), Is.GreaterThanOrEqualTo(5));
            Assert.That(app.Get<ILessonLibrary>().Lessons.Count, Is.EqualTo(36 + PackLessons(app)));
            Assert.That(app.Get<ITakeLibrary>().Available, Is.True);
            Assert.That(m.Takes.Folder, Is.EqualTo(Path.Combine(_boot.DataPath, "takes")), "takes under App.DataPath (a temporary folder in tests)");
            Assert.That(app.Instruments, Does.Contain(m.Deck));
            int at = -1;
            for (int i = 0; i < app.Instruments.Count; i++) if (ReferenceEquals(app.Instruments[i], m.Deck)) at = i;
            for (int i = 0; i < app.Instruments.Count; i++)
            {
                if (i < at) Assert.That(app.Instruments[i].TickOrder, Is.LessThanOrEqualTo(30), "piano, drums, harp before the deck");
                if (i > at) Assert.That(app.Instruments[i].TickOrder, Is.GreaterThanOrEqualTo(30), "switch panel, menu after it");
            }
            Rig table = app.Get<IRigRegistry>()?.Get(RigId.Table);
            Assert.That(table, Is.Not.Null, "Room registers the side table");
            Assert.That(m.TableRig, Is.SameAs(table));
            Assert.That(table.Instruments, Does.Contain(m.Deck));
            Assert.That(m.Deck.Root.parent, Is.SameAs(table.Group));
            Assert.That(m.Deck.Root.localPosition, Is.EqualTo(TapeDeck.TableLocalPosition));
            // Rig.Handles: one list, filled in Init (Room's, then the deck's body), read without allocating.
            IReadOnlyList<Renderer> handles = table.Handles;
            Assert.That(handles, Is.InstanceOf<List<Renderer>>(), "Room's own list, extended in place");
            foreach (Renderer r in m.Deck.HandleRenderers) Assert.That(handles, Does.Contain(r), "the deck's body is a handle of the table");
            foreach (Renderer r in m.Deck.PlayedParts) Assert.That(handles, Has.No.Member(r), "the pegs never are");
            Assert.That(m.Tutorial.State, Is.EqualTo(TutorialState.Idle));
            yield return WaitForFirstArrange();
            Assert.That(table.Handles, Is.SameAs(handles), "never rebuilt");
            Assert.That(Vector3.Distance(m.Deck.Root.position, table.Group.TransformPoint(TapeDeck.TableLocalPosition)), Is.LessThan(1e-5f), "the deck moves with the table");
        }

        [UnityTest]
        public IEnumerator AFingertipPressesRecThenStopAndTheTakeIsSavedAsTakeOne()
        {
            yield return Boot();
            yield return WaitForFirstArrange();
            App app = App.Current;
            LessonsModule m = Module();
            int libraryChanges = 0;
            app.Events.Subscribe<LibraryChangedEvent>(e => libraryChanges++);
            var states = new List<RecorderState>();
            app.Events.Subscribe<RecorderStateEvent>(e => states.Add(e.State));
            FakeHandSource hands = _boot.Hands;
            // The tape deck is silent (it has no audio of its own): pegs and lessons must not reach an instrument source.
            var engine = app.Get<IAudioEngine>();
            var tap = new AudioTestTap();
            engine.TestTap = tap;

            hands.SetPose(HandSide.Right, IndexAt(Above(m.Deck, DeckButton.Rec, 0.14f))); yield return null; yield return null;
            Assert.That(m.Deck.Presses, Is.EqualTo(0));
            hands.SetPose(HandSide.Right, IndexAt(Above(m.Deck, DeckButton.Rec, 0.08f))); yield return null;
            Assert.That(m.Deck.Presses, Is.EqualTo(1), "the tip entered the REC cylinder");
            Assert.That(m.Recorder.State, Is.EqualTo(RecorderState.Recording));
            yield return null; yield return null;
            Assert.That(m.Deck.Presses, Is.EqualTo(1), "holding does not repeat");
            app.Get<IRecorder>().Add(TakeEvent.PianoOn(60, 0.8f));      // something played while recording
            hands.SetPose(HandSide.Right, IndexAt(Above(m.Deck, DeckButton.Rec, 0.14f))); yield return null;
            // Batchmode frames are ~1 ms, so give the 18/s peg spring a quarter second of real frames to settle.
            hands.SetPose(HandSide.Right, IndexAt(Above(m.Deck, DeckButton.Stop, 0.14f)));
            float w0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - w0 < 0.25f) yield return null;
            Assert.That(m.Deck.PegDepth(DeckButton.Rec), Is.GreaterThan(0.9f), "REC stays down while recording (latched, finger gone)");
            Assert.That(m.Deck.PegDepth(DeckButton.Stop), Is.EqualTo(0f), "a tip hovering 14 cm up does not hold STOP");
            int saves = _boot.Settings.Saves;
            hands.SetPose(HandSide.Right, IndexAt(Above(m.Deck, DeckButton.Stop, 0.08f))); yield return null;
            Assert.That(m.Deck.Presses, Is.EqualTo(2));
            Assert.That(m.Recorder.State, Is.EqualTo(RecorderState.Idle), "STOP");
            Assert.That(m.Recorder.Take.Count, Is.EqualTo(1));
            Assert.That(states, Does.Contain(RecorderState.Recording));
            m.Tutorial.Start("drums-beat"); Burn(m.Tutorial); m.Tutorial.Update(5f, null);
            yield return null;
            m.Tutorial.Stop();
            engine.TestTap = null;
            int instrumentCommands = 0;
            foreach (AudioTestTap.Entry e in tap.Commands)
                if (e.Source <= SourceId.DrumRoom) instrumentCommands++;           // PianoLo .. DrumRoom: the instrument sources
            Assert.That(instrumentCommands, Is.EqualTo(0), "the deck and the tutorial make no sound of their own");
            float t0 = Time.realtimeSinceStartup;
            while (libraryChanges == 0 && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Assert.That(libraryChanges, Is.GreaterThan(0), "auto-save -> LibraryChangedEvent");
            var list = app.Get<ITakeLibrary>().List();
            while (!list.IsCompleted) yield return null;
            Assert.That(list.Result.Count, Is.EqualTo(1));
            Assert.That(list.Result[0].name, Does.Match(@"^Take 1 · 0:0\d$"), "\"Take N · m:ss\"");
            Assert.That(list.Result[0].count, Is.EqualTo(1));
            Assert.IsTrue(File.Exists(Path.Combine(_boot.DataPath, "takes", list.Result[0].id + ".json")), "in the boot's temporary DataPath");
            // The number mirror (ARCHITECTURE.md section 18) lands in the injected store, with no synchronous save per take.
            Assert.That(app.Settings.GetInt(AppSettings.KeyNextTakeNumber, 0), Is.EqualTo(2));
            Assert.That(_boot.Settings.Saves, Is.EqualTo(saves), "no PlayerPrefs.Save in the frame STOP is pressed");
        }

        // ARCHITECTURE.md section 0: the mouse hand is a visual stand-in whose pointers never press (Pointer.FromMouse);
        // a desktop click is the deck's own path, a ray from the head camera onto the play-surface layer.
        [UnityTest]
        public IEnumerator OnTheDesktopTheMouseHandNeverPressesAndAClickRayPressesAPeg()
        {
            var boot = new TestBoot();
            boot.Options.HandSource = null;                 // the editor's MouseHandSource, as in desktop play
            yield return Boot(boot);
            yield return WaitForFirstArrange();
            App app = App.Current;
            LessonsModule m = Module();
            var desk = m.Deck.Desktop as AppDeckDesktop;
            Assert.That(desk, Is.Not.Null, "the module wires the app's desktop side");
            Assert.IsTrue(app.Input.MouseHand, "hands from the mouse source");
            Assert.IsFalse(app.IsPresenting);
            Assert.IsFalse(desk.Presenting, "desktop: clicks count");
            Pointer tip = app.Input.Hand(Handedness.Right).Pointers[0];
            Assert.IsTrue(tip.FromMouse);
            Assert.IsTrue(desk.IsMouse(tip), "IDeckDesktop.IsMouse is Pointer.FromMouse");
            // The mouse hand's fingertip held inside the PLAY cylinder for several frames presses nothing.
            var only = new List<Pointer> { tip };
            Vector3 play = Above(m.Deck, DeckButton.Play, 0.08f);
            for (int i = 0; i < 4; i++)
            {
                tip.WasActive = true; tip.Active = true; tip.Prev = tip.Tip; tip.Tip = play;
                m.Deck.Tick(only, 0.016f);
            }
            Assert.That(m.Deck.Presses, Is.EqualTo(0), "mouse-hand pointers never press: the click path owns the mouse");
            // A left click on REC: the mouse ray from the head camera, the nearest play-surface hit.
            Camera cam = app.Rig.HeadCamera;
            Vector3 sp = cam.WorldToScreenPoint(m.Deck.Root.TransformPoint(TapeDeck.ButtonLocal(DeckButton.Rec)));
            Assert.That(sp.z, Is.GreaterThan(0f), "REC is in front of the desktop camera");
            Physics.SyncTransforms();
            Assert.IsTrue(m.Deck.DesktopDown(cam.ScreenPointToRay(new Vector3(sp.x, sp.y, 0f))), "the click lands on REC's play-surface collider");
            Assert.That(m.Recorder.State, Is.EqualTo(RecorderState.Recording));
            Assert.That(m.Deck.Presses, Is.EqualTo(1));
            m.Recorder.Stop();
        }

        // In the headset (here simulated: App.IsPresenting with scripted hands) no click counts, and tracked fingertips press.
        [UnityTest]
        public IEnumerator WhilePresentingClicksAreOffAndTrackedFingertipsPress()
        {
            yield return Boot(new TestBoot(simulateHeadset: true));
            yield return WaitForFirstArrange();
            App app = App.Current;
            LessonsModule m = Module();
            var desk = (AppDeckDesktop)m.Deck.Desktop;
            Assert.IsTrue(app.IsPresenting);
            Assert.IsTrue(desk.Presenting, "presenting: the desktop click path is off");
            Assert.IsFalse(desk.TryClick(out _));
            Pointer tip = app.Input.Hand(Handedness.Right).Pointers[0];
            Assert.IsFalse(tip.FromMouse);
            Assert.IsFalse(desk.IsMouse(tip), "scripted / tracked hands are no mouse");
            _boot.Hands.SetPose(HandSide.Right, IndexAt(Above(m.Deck, DeckButton.Rec, 0.14f))); yield return null; yield return null;
            _boot.Hands.SetPose(HandSide.Right, IndexAt(Above(m.Deck, DeckButton.Rec, 0.08f))); yield return null;
            Assert.That(m.Deck.Presses, Is.EqualTo(1), "a fingertip presses REC");
            Assert.That(m.Recorder.State, Is.EqualTo(RecorderState.Recording));
            m.Recorder.Stop();
        }

        [UnityTest]
        public IEnumerator TheTutorialRunsInItsPhaseCountsRecorderPlaysAndGlowsBeforeTheGlowPhase()
        {
            var probe = new PhaseProbe();
            yield return Boot(null, probe);
            // Layout's first arrange moves the piano (Desktop state after ≈ 0.5 s); the glow is captured mid-frame and
            // compared with the key afterwards, so the arrange must not land in between.
            yield return WaitForFirstArrange();
            App app = App.Current;
            LessonsModule m = Module();
            var events = new List<TutorialEvent>();
            app.Events.Subscribe<TutorialEvent>(e => events.Add(e));
            ITutorial tut = app.Get<ITutorial>();
            IRecorder rec = app.Get<IRecorder>();
            rec.Record(); rec.Add(TakeEvent.PianoOn(70, 0.5f)); rec.Stop();   // a deck take, recorded while no lesson runs
            tut.Start("piano-scale");
            Assert.That(events.Count, Is.EqualTo(1)); Assert.That(events[0].State, Is.EqualTo(TutorialState.Countdown));
            Assert.That(tut.LessonId, Is.EqualTo("piano-scale")); Assert.That(tut.Instrument, Is.EqualTo(InstrumentKind.Piano));
            Burn(m.Tutorial);
            m.Tutorial.Update(5f, null);
            Assert.That(tut.State, Is.EqualTo(TutorialState.Waiting));
            yield return null;
            Assert.That(probe.PulseAtStickHover, Is.GreaterThan(probe.PulseAtInstruments), "Update runs between the Instruments and StickHover phases");
            Assert.That(probe.GlowsAfterTutorial, Is.GreaterThanOrEqualTo(1), "the waiting key's glow is in the pool before the HoverGlow phase");
            IPianoApi piano = app.Get<IPianoApi>();
            Assert.IsTrue(piano.TryGetKey(60, out PianoKeyInfo c4));
            Vector3 keyTop = piano.Root.TransformPoint(c4.X, 0.001f, Tutorial.KeyZWhite);
            Assert.That(Vector3.Distance(probe.FirstGlow.Position, keyTop), Is.LessThan(0.002f), "glow on C4");
            Assert.That(m.Tutorial.BlockCount, Is.GreaterThanOrEqualTo(1));
            Vector3 block = m.Tutorial.Matrices[0].GetColumn(3);
            Assert.That(Vector3.Distance(block, piano.Root.TransformPoint(c4.X, Tutorial.KeyLift, Tutorial.KeyZWhite)), Is.LessThan(1e-4f), "block on the key, composed at EndFrame");
            // A play from anywhere (here straight into the recorder stream) hits; recorder playback never does.
            rec.Add(TakeEvent.PianoOn(62, 0.8f));
            Assert.That(tut.Summary.Mistakes, Is.EqualTo(1));
            rec.Add(TakeEvent.PianoOn(60, 0.8f));
            Assert.That(tut.EventsDone, Is.EqualTo(1)); Assert.That(tut.State, Is.EqualTo(TutorialState.Playing));
            Assert.That(tut.Hint, Is.EqualTo("Play D4"));
            Assert.That(events[events.Count - 1].State, Is.EqualTo(TutorialState.Playing));
            Assert.That(events[events.Count - 1].Version, Is.EqualTo(tut.Version));
            m.Tutorial.Update(5f, null);
            Assert.That(tut.State, Is.EqualTo(TutorialState.Waiting), "waiting on D4");
            Assert.IsTrue(rec.Play());
            rec.Add(TakeEvent.PianoOn(62, 0.8f));                       // what an instrument re-adds while playing back
            rec.Add(TakeEvent.PianoOn(64, 0.8f));
            Assert.That(tut.Summary.Mistakes, Is.EqualTo(1), "playback does not play the lesson (no mistake)");
            Assert.That(tut.EventsDone, Is.EqualTo(1), "nor a hit");
            rec.Stop();
            tut.Stop();
            Assert.That(tut.State, Is.EqualTo(TutorialState.Idle));
            yield return null;
            Assert.That(m.Tutorial.BlockCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator DrumAndHarpBlocksLandOnTheirPiecesAndStrings()
        {
            yield return Boot();
            yield return WaitForFirstArrange();      // the rigs stay put from here on
            App app = App.Current;
            LessonsModule m = Module();
            m.Tutorial.Start("drums-beat");
            Burn(m.Tutorial);
            m.Tutorial.Update(5f, null);
            yield return null;
            IDrumsApi drums = app.Get<IDrumsApi>();
            DrumPieceInfo hat = default, pad = default;
            foreach (DrumPieceInfo p in drums.Pieces) { if (p.Kind == "hat") hat = p; if (p.Kind == "kick" && p.Pad) pad = p; }
            // Waiting on the first chord, every event due within the travel time has spawned: 6 events, 9 discs at 90 bpm.
            Assert.That(m.Tutorial.BlockCount, Is.EqualTo(9));
            Assert.That(m.Tutorial.Shape, Is.EqualTo(BlockShape.Disc));
            bool onHat = false, onPad = false, nextHat = false;
            float next = (float)((m.Tutorial.DueOf(1) - m.Tutorial.DueOf(0)) * Tutorial.Speed);   // the second hat hovers this high
            // First five (tutorials-audit.md B1): the hi-hat's discs land 0.45 r toward the player, on its top; the pad's at
            // its centre.
            Vector3 land = m.Tutorial.DrumLanding(hat);
            for (int i = 0; i < m.Tutorial.BlockCount; i++)
            {
                Vector3 b = m.Tutorial.Matrices[i].GetColumn(3);
                onHat |= Vector3.Distance(b, hat.Transform.TransformPoint(land)) < 1e-4f;
                onPad |= Vector3.Distance(b, pad.Transform.TransformPoint(0f, Tutorial.DiscLift, 0f)) < 1e-4f;
                nextHat |= Vector3.Distance(b, hat.Transform.TransformPoint(land + new Vector3(0f, next, 0f))) < 1e-4f;
            }
            Assert.IsTrue(onHat, "a disc on the hi-hat"); Assert.IsTrue(onPad, "a disc on the kick pad");
            Assert.IsTrue(nextHat, "later blocks hover (due_k - due_cur) x SPEED above their targets while waiting");
            app.Get<IRecorder>().Add(TakeEvent.Drum("hat", 0.7f, 0.4f, "bow", false));
            app.Get<IRecorder>().Add(TakeEvent.Drum("kick", 0.7f, 0.3f, "head", false));
            Assert.That(m.Tutorial.CurrentEvent, Is.EqualTo(1), "hat + kick played");

            m.Tutorial.Start("harp-arpeggio");
            Burn(m.Tutorial);
            m.Tutorial.Update(5f, null);
            yield return null;
            IHarpApi harp = app.Get<IHarpApi>();
            Assert.That(m.Tutorial.BlockCount, Is.GreaterThanOrEqualTo(1));
            Vector3 cap = m.Tutorial.Matrices[0].GetColumn(3);                  // pool order: the first note's capsule
            Assert.That(Vector3.Distance(cap, harp.Root.TransformPoint(harp.PlayPoint(7))), Is.LessThan(0.002f), "the capsule on string 7 (C4)");
            app.Get<IRecorder>().Add(TakeEvent.Harp(7, 0.7f, 0.35f, 0f, "through"));
            Assert.That(m.Tutorial.CurrentEvent, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ALessonStopsWhenItsInstrumentLeavesTheLayout()
        {
            yield return Boot();
            yield return WaitForFirstArrange();
            App app = App.Current;
            ITutorial tut = app.Get<ITutorial>();
            ILayoutService layout = app.Get<ILayoutService>();
            // The layout persists in the boot's own MemorySettingsStore: nothing to restore afterwards.
            layout.Set(LayoutMode.All, InstrumentKind.Piano);
            tut.Start("piano-scale");
            layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            Assert.That(tut.State, Is.EqualTo(TutorialState.Idle), "Layout: the piano left, the lesson stops");
            tut.Start("harp-arpeggio");
            layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            Assert.That(tut.State, Is.EqualTo(TutorialState.Countdown), "the harp is still in the scene");
            yield return null;
            Assert.That(tut.State, Is.EqualTo(TutorialState.Countdown));
        }

        [UnityTest]
        public IEnumerator HotPathsAllocateNothing()
        {
            // Not headless: with a GPU (test-play --gpu) the instanced draw itself is inside the measured frame too.
            yield return Boot(new TestBoot(headless: false));
            yield return WaitForFirstArrange();
            App app = App.Current;
            LessonsModule m = Module();
            // The module's own tutorial waiting on a drum chord (two discs, two glows each frame), plus a second tutorial on
            // a private bus for the play path (a wrong note publishes TutorialEvent; no subscriber can allocate there).
            m.Tutorial.Start("drums-beat"); Burn(m.Tutorial); m.Tutorial.Update(5f, null);
            var other = new Tutorial(m.Library, new AppLessonStage(app), new EventBus());
            other.Start("harp-arpeggio"); Burn(other); other.Update(5f, null);
            // The deck recording: reels turning and the REC peg settling, so its merged meshes are re-posed and uploaded.
            m.Recorder.Record();
            IReadOnlyList<Pointer> pts = app.Input.HandPointers;
            Vector3 probe = m.Deck.Root.TransformPoint(TapeDeck.ButtonLocal(DeckButton.Play) + Vector3.up * 0.03f);
            TakeEvent wrong = TakeEvent.Harp(3, 0.5f, 0.35f, 0f, "through");
            void Frame()
            {
                m.Tick(FramePhase.Tutorial, 0.016f);
                m.Tick(FramePhase.Recorder, 0.016f);
                m.Tick(FramePhase.EndFrame, 0.016f);
                m.Deck.Tick(pts, 0.016f);
                m.Deck.Hover(pts, app.HoverGlow);
                m.Deck.PlaySurfaceAt(probe);
                other.OnPlay(wrong, false);
                other.Update(0.016f, app.HoverGlow);
                other.Compose();
                m.Blocks.Draw(m.Tutorial.Shape, m.Tutorial.Matrices, m.Tutorial.Colors, m.Tutorial.BlockCount, m.Tutorial.BlockBounds);
                app.HoverGlow.Update(0f);
            }
            for (int i = 0; i < 4; i++) Frame();      // warm up (JIT, first-use caches)
            yield return null;
            Assert.That(() => { s_keep = new float[1024]; }, Is.AllocatingGCMemory(), "the GC.Alloc recorder works here");
            float angle0 = m.Deck.ReelAngle;
            Assert.That(() => { for (int i = 0; i < 60; i++) Frame(); }, Is.Not.AllocatingGCMemory());
            Assert.That(m.Deck.ReelAngle, Is.Not.EqualTo(angle0), "the reels turned inside the measured frames");
            Assert.That(m.Tutorial.BlockCount, Is.EqualTo(9), "the drum chord and the upcoming discs were composed every frame");
            Assert.That(other.Summary.Mistakes, Is.GreaterThan(60));
            m.Recorder.Stop();
        }
    }
}
