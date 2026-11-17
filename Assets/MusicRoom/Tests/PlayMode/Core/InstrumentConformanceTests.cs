// The conformance suite over the registry (platform.md 12.2): every playable instrument, stub or real, runs these with no
// code of its own except its fixture (IInstrumentFixture, check 7). Each test boots the whole app (simulated headset, manual
// frames, the scripted audio clock) with the instrument alone at the seat ("One alone"), or in My set where the check needs
// the display. Checks by number:
//   AtTheSeat: 1 present at the seat, the others not; 2 Rig.Handles filled once, non-empty, the same list each frame; 3 the
//     descriptor's Seat.Boxes cover the handles within 3 cm; 11 its lesson pack validates; 12 its arrival tip; 13 every anchor
//     resolves; 14 its sources follow its rig; 20 its fallback icon.
//   PlaysRecordsAndPlaysBack: 7 the fixture's strike records a live onset of its type; 8 Play of every declared non-Marker
//     type makes a unit call or a classic engine call; 9 ReleaseAll and CloseLane; 17 arrange mode silences it.
//   TickAllocatesNothing: 5 Tick / Hover allocate nothing over 120 frames.
//   DisplayedThenBroughtByTheRay: 1 not present while displayed or gliding; 6 SetStowed(true) leaves no tool held; 19 a ray
//     summon (Bring, Ray) brings it (Leaving then Arrived) and it is present after.
//   APageTurnToAndFromIt: 18.
//   ItsUnit: 10 (new roles: the unit registers and is the active unit), 15 (the classic three: no unit is created, Play
//     reaches today's synths; the classic sound is their only sound since 2026-11-10).
// Check 4 (draws) runs in the GPU suites (DrawBudgetTests, LayoutShotsTests, EightInstrumentsShotsTests); check 16 (the
// desktop click path does not double-fire) is each module's own desktop test.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode
{
    public class InstrumentConformanceTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _audio = 10.0;
        readonly List<RecorderEvent> _events = new List<RecorderEvent>();
        readonly List<InstrumentMoveEvent> _moves = new List<InstrumentMoveEvent>();

        public static IEnumerable<InstrumentKind> Kinds()
        {
            foreach (InstrumentDescriptor d in InstrumentRegistry.Discover().All) yield return d.Kind;
        }

        App App => App.Current;
        InstrumentDescriptor D(InstrumentKind k) => App.Registry.Get(k);
        ILayoutService Layout => App.Get<ILayoutService>();
        IInstrument Inst(InstrumentKind k) => App.Instruments.FirstOrDefault(i => i.Kind == k);
        Rig RigOf(InstrumentKind k) => App.Get<IRigRegistry>().Get(InstrumentIds.RigOf(k));
        void Frame() { _audio += Dt; _boot.Step(Dt); }
        void Step(float s) { int n = Mathf.Max(1, Mathf.RoundToInt(s / Dt)); for (int i = 0; i < n; i++) Frame(); }
        FixtureContext Ctx => new FixtureContext { Boot = _boot, Frame = Frame };

        IEnumerator Boot(InstrumentKind current, bool alone = true, string set = null)
        {
            _boot = new TestBoot(headless: true, simulateHeadset: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, alone ? "single" : "all");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(current));
            if (set != null) _boot.Settings.SetString(AppSettings.KeySet, set);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            while (App.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            App.Events.Subscribe<RecorderEvent>(e => _events.Add(e));
            App.Events.Subscribe<InstrumentMoveEvent>(e => _moves.Add(e));
            Step(0.2f);
            // Park the hands well away from every instrument.
            _boot.Hands.SetPose(HandSide.Right, FixtureContext.IndexAt(App.Head.position + Vector3.up * 0.6f));
            _boot.Hands.SetPose(HandSide.Left, HandPose.OpenFacing(HandSide.Left, App.Head.position + Vector3.up * 0.6f + Vector3.left * 0.3f, Vector3.down, Vector3.forward));
            Step(0.1f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _events.Clear();
            _moves.Clear();
        }

        // ------------------------------------------------------------------------------------------------ checks 1-3, 11-14, 20

        [UnityTest]
        public IEnumerator AtTheSeat([ValueSource(nameof(Kinds))] InstrumentKind kind)
        {
            yield return Boot(kind);
            InstrumentDescriptor d = D(kind);
            IInstrument inst = Inst(kind);
            Assert.IsNotNull(inst, kind + " registers its IInstrument");
            Assert.IsTrue(App.Registry.IsLoaded(kind));
            Assert.That(Layout.PlaceOf(kind), Is.EqualTo(InstrumentPlace.Seat));
            Assert.IsTrue(inst.IsPresent, "1: present at the seat");
            foreach (InstrumentDescriptor o in App.Registry.All)
                if (o.Kind != kind) Assert.IsFalse(Inst(o.Kind)?.IsPresent ?? false, $"1: {o.Id} is not at the seat");
            // 2: the handles.
            Rig rig = RigOf(kind);
            Assert.IsNotNull(rig);
            IReadOnlyList<Renderer> handles = rig.Handles;
            Assert.That(handles.Count, Is.GreaterThan(0), "2: Rig.Handles is non-empty");
            Step(0.3f);
            Assert.That(rig.Handles, Is.SameAs(handles), "2: the same list object every frame");
            // 3: the footprint covers the handles (in the rig's frame, heights from Home).
            // The kit's stands count as their poles (its tripod feet are left out of the footprint by design): DrumsPlatformTests
            // checks it with that rule.
            if (kind != InstrumentKind.Drums)
            {
                string margins = ClassicConformance.AssertBoxesCoverHandles(d, rig, d.IsMirrored(App.Settings.Store));
                Debug.Log($"[Conformance] {d.Id} footprint margins: {margins}");
            }
            Assert.That(rig.HomeAll, Is.EqualTo(d.Seat.Home).Using(Vector3EqualityComparer.Instance), "Rig.HomeAll = Seat.Home");
            Assert.That(Mathf.DeltaAngle(rig.HomeYawAll, d.Seat.HomeYaw), Is.EqualTo(0f).Within(1e-3f));
            // 11: its lessons validate through its own ILessonTargets (the classic three: TutorialTests).
            if (kind > InstrumentKind.Harp)
            {
                ILessonTargets lt = App.GetAll<ILessonTargets>().FirstOrDefault(t => t.Kind == kind);
                Assert.IsNotNull(lt, "11: an instrument with lessons registers ILessonTargets");
                ILessonLibrary lib = App.Get<ILessonLibrary>();
                Assert.That(lib.Count(kind), Is.GreaterThan(0), "11: its pack loaded");
                foreach (Lesson l in lib.Lessons.Where(l => l.instrument == d.Id))
                    foreach (LessonEvent e in l.events)
                        foreach (LessonTarget t in e.targets)
                        {
                            Assert.IsTrue(lt.Validate(t, out string why), $"11: {l.id}: {why}");
                            Assert.IsTrue(lt.TryGetLanding(t, out Pose landing, out Vector3 approach, out Vector2 size), $"11: {l.id} landing");
                            Assert.That(approach.magnitude, Is.EqualTo(1f).Within(1e-3f), "11: approach is a unit vector");
                            Assert.That(size.x, Is.GreaterThan(0f));
                        }
                // 12: one P2 arrival tip with an onset success.
                TipSpec arrive = d.Tips.FirstOrDefault(t => t.Trigger == TipTrigger.FirstArrival);
                Assert.IsNotNull(arrive, "12: an arrival tip");
                Assert.That(arrive.Class, Is.EqualTo(TipClass.P2));
                Assert.IsTrue(arrive.TextOnly || arrive.Lines.Length > 0, "12: its lines (or a caption)");
            }
            // 13: every anchor the descriptor and its tips name resolves.
            IGuideAnchors anchors = App.GetAll<IGuideAnchors>().FirstOrDefault(a => a.Kind == kind);
            var names = new HashSet<string>(d.Anchors);
            foreach (TipSpec t in d.Tips) if (!string.IsNullOrEmpty(t.Anchor)) names.Add(t.Anchor);
            if (names.Count > 0) Assert.IsNotNull(anchors, "13: an IGuideAnchors of its kind");
            foreach (string a in names)
            {
                Assert.IsTrue(anchors.TryGetAnchor(a, out Vector3 w, out Vector3 up), "13: anchor " + a);
                Assert.That(up.sqrMagnitude, Is.GreaterThan(1e-6f), "13: " + a + " has an up");
                Assert.That(Vector3.Distance(w, rig.Group.position), Is.LessThan(2.5f), "13: " + a + " is on the instrument");
            }
            // 14: its sources hang under its rig.
            var engine = App.Get<IAudioEngine>() as AudioEngine;
            if (engine != null && engine.Sources != null)
                foreach (AudioSourceSpec s in d.Audio.Sources)
                {
                    SynthSource src = engine.Sources[s.Source];
                    Assert.IsNotNull(src, ((SourceId)s.Source).ToString());
                    Assert.IsTrue(src.transform.IsChildOf(rig.Group), $"14: {(SourceId)s.Source} follows the {d.Id}'s rig");
                }
            // 20: the menu's fallback icon has strokes in its 100-unit box.
            Assert.That(d.Portrait.Fallback.Length, Is.GreaterThan(0), "20: a fallback icon");
            foreach (IconStroke st in d.Portrait.Fallback)
                foreach (Vector2 p in st.Points) Assert.That(p.x >= 0 && p.x <= 100 && p.y >= 0 && p.y <= 100, "20: icon points in 0..100");
        }

        // ------------------------------------------------------------------------------------------------ checks 7, 8, 9, 17

        [UnityTest]
        public IEnumerator PlaysRecordsAndPlaysBack([ValueSource(nameof(Kinds))] InstrumentKind kind)
        {
            yield return Boot(kind);
            InstrumentDescriptor d = D(kind);
            IInstrumentFixture fx = FixtureContext.For(kind);
            Assert.IsNotNull(fx, $"7: {d.Id} provides a fixture (Tests/PlayMode/<X>/<X>Fixture.cs : IInstrumentFixture)");
            var onsets = new HashSet<string>(d.Events.Where(e => e.Role == EventRole.Onset).Select(e => e.Type));
            _events.Clear();
            fx.Strike(Ctx);
            RecorderEvent hit = _events.FirstOrDefault(e => !e.DuringPlayback && onsets.Contains(e.Event.type));
            Assert.IsNotNull(hit.Event.type, $"7: the fixture's strike records a live onset of {d.Id} (got: {string.Join(", ", _events.Select(e => e.Event.type))})");
            Assert.That(App.Registry.OwnerOfEvent(hit.Event.type), Is.EqualTo(kind));

            // 8: playback of every declared non-Marker type reaches the sound.
            IInstrumentPlayback pb = App.GetAll<IInstrumentPlayback>().FirstOrDefault(p => p.Kind == kind);
            Assert.IsNotNull(pb, "8: an IInstrumentPlayback of its kind");
            var engine = App.Get<IAudioEngine>();
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            foreach (EventTypeSpec e in d.Events)
            {
                if (e.Role == EventRole.Marker) continue;
                tap.Clear();
                TakeEvent ev = e.Type == hit.Event.type ? hit.Event : new TakeEvent { type = e.Type, midi = 60, vel = 0.7f, kind = "snare", i = 2, open = e.Role != EventRole.State, lane = PlaybackLanes.Take };
                ev.lane = PlaybackLanes.Take;
                Assert.IsTrue(pb.Play(ev), $"8: Play({e.Type}) is known");
                Frame();
                bool sounded = tap.Calls.Count > 0;
                Assert.IsTrue(sounded || e.Role != EventRole.Onset, $"8: Play({e.Type}) makes a sound call");
            }
            Assert.IsFalse(pb.Play(new TakeEvent { type = "nobodysType" }), "8: an unknown type is refused");
            // 9: release everything and close a lane without trouble.
            pb.ReleaseAll();
            pb.CloseLane(PlaybackLanes.Layer(0));
            pb.CloseLane(PlaybackLanes.Take);
            Frame();
            engine.TestTap = null;

            // 17: arrange mode silences it.
            IArrangeMode arrange = App.Get<IArrangeMode>();
            arrange.BeginArrange();
            Step(0.2f);
            _events.Clear();
            fx.Strike(Ctx);
            Assert.That(_events.Count(e => !e.DuringPlayback && onsets.Contains(e.Event.type)), Is.EqualTo(0), "17: nothing plays in arrange mode");
            arrange.EndArrange();
        }

        // ------------------------------------------------------------------------------------------------ check 5

        [UnityTest]
        public IEnumerator TickAllocatesNothing([ValueSource(nameof(Kinds))] InstrumentKind kind)
        {
            yield return Boot(kind);
            IInstrument inst = Inst(kind);
            IReadOnlyList<Pointer> pts = App.PointersFor(inst);
            HoverGlowPool glow = App.HoverGlow;
            for (int i = 0; i < 10; i++) { inst.Tick(pts, Dt); inst.Hover(pts, glow); }   // warm up
            // Which call allocated, when (logged only: the assertion below decides). An allocation seen once in the merged
            // project's full runs (one instrument, then the kit) is located by this line in the log.
            var rec = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            rec.FilterToCurrentThread();
            var where = new System.Text.StringBuilder();
            for (int i = 0; i < 120; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    rec.enabled = false; rec.enabled = true;
                    if (c == 0) inst.Tick(pts, Dt); else if (c == 1) inst.Hover(pts, glow); else inst.PlaySurfaceAt(Vector3.zero);
                    rec.enabled = false;
                    if (rec.sampleBlockCount > 0) where.Append($"iteration {i} {(c == 0 ? "Tick" : c == 1 ? "Hover" : "PlaySurfaceAt")} {rec.sampleBlockCount}; ");
                }
            }
            rec.CollectFromAllThreads();
            if (where.Length > 0) Debug.Log($"[Conformance] {kind} allocations in the probe pass: {where}");
            Assert.That(() =>
            {
                for (int i = 0; i < 120; i++) { inst.Tick(pts, Dt); inst.Hover(pts, glow); inst.PlaySurfaceAt(Vector3.zero); }
            }, Is.Not.AllocatingGCMemory(), "5: Tick / Hover allocate nothing");
        }

        // ------------------------------------------------------------------------------------------------ checks 1, 6, 19

        [UnityTest]
        public IEnumerator DisplayedThenBroughtByTheRay([ValueSource(nameof(Kinds))] InstrumentKind kind)
        {
            // The default set, with this instrument off it: a classic member is swapped out for a stub first.
            string set = kind <= InstrumentKind.Harp ? "bowls" : "piano,drums,harp";
            yield return Boot(InstrumentKind.Piano, alone: false, set: set);
            InstrumentDescriptor d = D(kind);
            IInstrument inst = Inst(kind);
            Assert.IsFalse(Layout.MySet.Contains(kind), "not in My set");
            Assert.That(Layout.PlaceOf(kind), Is.EqualTo(InstrumentPlace.Display), "on display in the VR room");
            Assert.IsFalse(inst.IsPresent, "1: not present while displayed");
            IStowable st = App.GetAll<IStowable>().FirstOrDefault(s => s.Kind == kind);
            Assert.IsNotNull(st, "6: an IStowable of its kind");
            Assert.IsTrue(st.Stowed, "6: stowed on display");
            foreach (IHandToolProvider t in App.GetAll<IHandToolProvider>().Where(t => t.Owner == kind))
                Assert.IsFalse(t.AnyHeld, "6: no tool held while displayed");
            Assert.IsTrue(Layout.TryGetDisplayPoint(kind, out Vector3 pick), "a pick point on display");
            Assert.That(Vector3.Distance(pick, App.Head.position), Is.GreaterThan(1.0f));

            _moves.Clear();
            Assert.IsTrue(Layout.Bring(kind, BringReason.Ray), "19: a ray summon brings it");
            Frame();
            Assert.IsTrue(Layout.IsMoving(kind) || Layout.PlaceOf(kind) == InstrumentPlace.Seat, "it glides");
            if (Layout.IsMoving(kind)) Assert.IsFalse(inst.IsPresent, "1: not present while gliding");
            Step(2.0f);
            Assert.IsFalse(Layout.IsMoving(kind));
            Assert.That(Layout.PlaceOf(kind), Is.EqualTo(InstrumentPlace.Seat));
            Assert.IsTrue(inst.IsPresent, "19: present after arriving");
            Assert.IsFalse(st.Stowed, "unstowed at the seat");
            int leave = _moves.FindIndex(m => m.Kind == kind && m.Phase == MovePhase.Leaving);
            int arrive = _moves.FindIndex(m => m.Kind == kind && m.Phase == MovePhase.Arrived);
            Assert.That(arrive, Is.GreaterThanOrEqualTo(0), "19: InstrumentMoveEvent Arrived");
            Assert.That(leave, Is.LessThan(arrive), "19: Leaving before Arrived");
            Assert.That(_moves[arrive].Reason, Is.EqualTo(BringReason.Ray));
        }

        // ------------------------------------------------------------------------------------------------ check 18

        [UnityTest]
        public IEnumerator APageTurnToAndFromIt([ValueSource(nameof(Kinds))] InstrumentKind kind)
        {
            InstrumentKind start = kind == InstrumentKind.Piano ? InstrumentKind.Harp : InstrumentKind.Piano;
            yield return Boot(start);
            Assert.That(Layout.Current, Is.EqualTo(start));
            Assert.IsTrue(Layout.Bring(kind, BringReason.PageTurn), "18: a page turn to it");
            Step(2.0f);
            Assert.That(Layout.Current, Is.EqualTo(kind));
            Assert.IsTrue(Inst(kind).IsPresent);
            Assert.IsFalse(Inst(start).IsPresent, "the previous one left");
            InstrumentKind next = App.Registry.Next(kind, 1);
            Layout.Next(1);
            Step(2.0f);
            Assert.That(Layout.Current, Is.EqualTo(next), "18: a page turn from it goes to the carousel's next");
            Assert.IsFalse(Inst(kind).IsPresent);
        }

        // ------------------------------------------------------------------------------------------------ checks 10, 15

        [UnityTest]
        public IEnumerator ItsUnit([ValueSource(nameof(Kinds))] InstrumentKind kind)
        {
            yield return Boot(kind);
            InstrumentDescriptor d = D(kind);
            var engine = App.Get<IAudioEngine>() as AudioEngine;
            if (engine == null) Assert.Ignore("no audio engine in this boot");
            var role = (UnitId)d.Audio.Unit;
            if (SoundUnits.IsUnitOnly(role))
            {
                Assert.IsNotNull(engine.Unit(role), "10: its unit registers");
                Assert.That(engine.ActiveUnit(role), Is.SameAs(engine.Unit(role)), "10: it plays");
                Step(0.3f);
                Assert.IsNotNull(engine.ActiveUnit(role), "10: still its unit");
            }
            else
            {
                // 15: the classic three: no unit, Play reaches today's synths.
                Assert.IsNull(engine.Unit(role), "15: no unit is created for " + d.Id);
                Assert.IsNull(engine.ActiveUnit(role), "15: today's synths play " + d.Id);
                IInstrumentPlayback pb = App.GetAll<IInstrumentPlayback>().First(p => p.Kind == kind);
                EventTypeSpec onset = d.Events.First(e => e.Role == EventRole.Onset);
                Step(0.3f);
                var tap = new AudioTestTap();
                engine.TestTap = tap;
                pb.Play(new TakeEvent { type = onset.Type, midi = 60, vel = 0.7f, kind = "snare", i = 5, lane = PlaybackLanes.Take });
                Frame();
                Assert.That(tap.Calls.Count, Is.GreaterThan(0), $"15: {d.Id} plays");
                Assert.That(tap.Calls.Count(c => c.Method == AudioCall.Unit), Is.EqualTo(0), $"15: {d.Id} makes no unit call");
                engine.TestTap = null;
                pb.ReleaseAll();
            }
        }
    }

    /// <summary>Vector3 equality within 1e-3 m.</summary>
    public sealed class Vector3EqualityComparer : IEqualityComparer<Vector3>
    {
        public static readonly Vector3EqualityComparer Instance = new Vector3EqualityComparer();
        public bool Equals(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-6f;
        public int GetHashCode(Vector3 v) => 0;
    }
}
