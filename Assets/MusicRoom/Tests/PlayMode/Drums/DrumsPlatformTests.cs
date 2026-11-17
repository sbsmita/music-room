// The drum kit's platform adapters (12-new-instruments/platform.md 3.3, 4.6, 12.2; Drums/DrumsPlatform.cs,
// DrumToolProvider.cs) in the booted app: the services registered with Kind / Owner Drums; the rig homes equal the
// descriptor; the descriptor's boxes cover Rig.Handles within 3 cm (conformance check 3, margins logged); IInstrumentPlayback
// makes the very calls of the classic playback path (PlaybackHit / PlaybackChoke / SetHatOpen); the anchors (piece.<kind>,
// pad) are the strike surfaces and allocate nothing; SetStowed lays a held stick on the snare at once and turns the nod kick
// off (and back on); the tool provider measures what TryGrab measures, says +inf without the kit at the seat, releases.
using System.Collections;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Drums;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Drums
{
    public class DrumsPlatformTests
    {
        TestBoot _boot;
        App _app;
        DrumsModule _module;
        static Vector3 s_world, s_up;
        static float s_d;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: true);
            _boot.Options.InstrumentFilter = k => k <= InstrumentKind.Harp;
            yield return _boot.Start();
            _app = App.Current;
            var layout = _app.Get<ILayoutService>();
            float t0 = Time.realtimeSinceStartup;
            while (!layout.FirstArrangeDone && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            yield return null;
            _module = (DrumsModule)_app.Modules.First(m => m is DrumsModule);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var engine = _app?.Get<IAudioEngine>();
            if (engine != null) engine.TestTap = null;
            yield return _boot.Stop();
        }

        [UnityTest]
        public IEnumerator TheAdaptersAreRegisteredAndTheRigMatchesItsDescriptor()
        {
            DrumsPlatform p = _module.Platform;
            Assert.That(p, Is.Not.Null);
            Assert.That(_app.GetAll<IStowable>().Single(s => s.Kind == InstrumentKind.Drums), Is.SameAs(p));
            Assert.That(_app.GetAll<IInstrumentPlayback>().Single(s => s.Kind == InstrumentKind.Drums), Is.SameAs(p));
            Assert.That(_app.GetAll<IGuideAnchors>().Single(s => s.Kind == InstrumentKind.Drums), Is.SameAs(p));
            Assert.That(_app.GetAll<IHandToolProvider>().Single(s => s.Owner == InstrumentKind.Drums), Is.SameAs(_module.Tools));
            Assert.That(_app.Get<IDrumsApi>(), Is.SameAs(_module.Kit), "IDrumsApi stays");
            InstrumentDescriptor d = _app.Registry.Get(InstrumentKind.Drums);
            ClassicConformance.AssertHomes(d, _module.Rig);
            // The footprint models each stand as its pole: the three tripod legs (from the hip at .32 m to feet .3 m out at
            // the floor) are left out by design (registry.mjs drumBoxes: the splayed feet pass under the bench and beside
            // the other instruments' feet; with them, the default set no longer plans). So a stand counts as its pole here,
            // and the legs' reach is logged.
            var handles = ClassicConformance.HandleBounds(_module.Rig);
            float legsOut = 0f;
            var legs = new System.Text.StringBuilder();
            for (int i = 0; i < handles.Count; i++)
            {
                var h = handles[i];
                if (!h.Name.StartsWith("stand-")) continue;
                string kind = h.Name.Substring("stand-".Length);
                PieceSpec spec = DrumSpec.Pieces.First(q => kind == "kick-pad" ? q.Pad : (!q.Pad && q.Kind == kind));
                Vector3 top = DrumSpec.StandTop(spec);
                var pole = new ClassicConformance.Box
                {
                    Name = h.Name + " (pole)",
                    Min = new Vector3(top.x - DrumSpec.PoleRadius, 0f, top.z - DrumSpec.PoleRadius),
                    Max = new Vector3(top.x + DrumSpec.PoleRadius, top.y, top.z + DrumSpec.PoleRadius),
                };
                float reach = Mathf.Max(Mathf.Max(pole.Min.x - h.Min.x, h.Max.x - pole.Max.x), Mathf.Max(pole.Min.z - h.Min.z, h.Max.z - pole.Max.z));
                Assert.That(h.Max.y, Is.EqualTo(top.y).Within(0.02f), h.Name + ": the stand's bounds are its pole and legs");
                Assert.That(reach, Is.LessThanOrEqualTo(DrumSpec.LegReach + 0.01f), h.Name + ": nothing beyond the feet");
                legsOut = Mathf.Max(legsOut, reach);
                legs.Append($"{kind} {reach:F3}; ");
                handles[i] = pole;
            }
            ClassicConformance.AssertBoxesCover(d, handles, $"stands as their poles; the tripod feet reach (m beyond the pole, at y <= {DrumSpec.HipY}): {legs}max {legsOut:F3}");
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlaybackMakesTheClassicPathsCalls()
        {
            var engine = _app.Get<IAudioEngine>();
            float t0 = Time.realtimeSinceStartup;
            while (!engine.IsReady && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            DrumsPlatform p = _module.Platform;
            DrumKit kit = _module.Kit;
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            TakeEvent hit = TakeEvent.Drum("snare", 0.8f, 0.4f, "head", false);
            int hits = kit.HitCount;
            Assert.That(p.Play(hit), Is.True);
            var viaAdapter = tap.Calls.Select(c => (c.Method, c.Unit, c.Op)).ToList();
            Assert.That(kit.HitCount, Is.EqualTo(hits + 1));
            Assert.That(kit.LastPiece.Kind, Is.EqualTo("snare"));
            tap.Clear();
            kit.PlaybackHit("snare", 0.8f, DrumsPlatform.StrikeOf(hit));   // the classic line: drums()?.PlaybackHit(kind, vel, StrikeOf(e))
            Assert.That(viaAdapter, Is.Not.Empty);
            Assert.That(viaAdapter, Is.EqualTo(tap.Calls.Select(c => (c.Method, c.Unit, c.Op)).ToList()), "the classic SetTarget line's calls");
            // An old take's hit without a zone is the default strike {.3, head}.
            DrumStrikeInfo s = DrumsPlatform.StrikeOf(new TakeEvent { type = TakeEventType.Drum, kind = "tom1", vel = 0.5f, pos = 0.9f });
            Assert.That(s.Pos, Is.EqualTo(0.3f)); Assert.That(s.Zone, Is.EqualTo("head"));
            int chokes = kit.ChokeCount;
            tap.Clear();
            Assert.That(p.Play(TakeEvent.DrumChoke("crash")), Is.True);
            Assert.That(tap.Count(AudioCall.Choke) + tap.CountUnit(MusicRoom.Audio.Burst.UnitId.Drums), Is.GreaterThan(0), "a choke call");
            Assert.That(p.Play(TakeEvent.Hat(true)), Is.True);
            Assert.That(kit.HatOpen, Is.True, "the hat state (playback: not recorded again)");
            Assert.That(p.Play(TakeEvent.Hat(false)), Is.True);
            Assert.That(kit.HatOpen, Is.False);
            Assert.That(p.Play(TakeEvent.PianoOn(60, 0.5f)), Is.False, "not a kit type");
            p.ReleaseAll();
            p.CloseLane(1);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnchorsAreTheStrikeSurfacesAndAllocateNothing()
        {
            DrumsPlatform p = _module.Platform;
            var pieces = _module.Kit.Pieces;
            foreach (string kind in new[] { "snare", "tom1", "tom2", "floor", "kick", "hat", "crash", "ride" })
            {
                Assert.That(p.TryGetAnchor("piece." + kind, out Vector3 w, out Vector3 up), Is.True, kind);
                DrumPieceInfo info = pieces.First(x => x.Kind == kind && !x.Pad);
                Assert.That(Vector3.Distance(w, info.Transform.position), Is.LessThan(1e-5f), kind + ": its strike surface centre");
                Assert.That(Vector3.Angle(up, info.Transform.up), Is.LessThan(0.01f), kind + ": its normal");
            }
            Assert.That(p.TryGetAnchor("pad", out Vector3 pad, out _), Is.True);
            Assert.That(Vector3.Distance(pad, pieces[_module.Kit.KickPadId].Transform.position), Is.LessThan(1e-5f), "the kick pad");
            Assert.That(p.TryGetAnchor("piece.kick", out Vector3 kick, out _) && Vector3.Distance(kick, pad) > 0.05f, Is.True, "piece.kick is the drum, not the pad");
            foreach (string bad in new[] { "piece.", "piece.cowbell", "piece.snare2", "snare", "key.60", null })
                Assert.That(p.TryGetAnchor(bad, out _, out _), Is.False, bad ?? "null");
            foreach (string a in _app.Registry.Get(InstrumentKind.Drums).Anchors) Assert.That(p.TryGetAnchor(a, out _, out _), Is.True, a);
            // The ids are made outside the measured call (a literal inside it would be interned on its first run).
            string ride = "piece.ride", padId = "pad";
            for (int i = 0; i < 3; i++) { p.TryGetAnchor(ride, out s_world, out s_up); p.TryGetAnchor(padId, out s_world, out s_up); }   // warm up (JIT)
            Assert.That(() => { p.TryGetAnchor(ride, out s_world, out s_up); p.TryGetAnchor(padId, out s_world, out s_up); }, Is.Not.AllocatingGCMemory());
            yield return null;
        }

        [UnityTest]
        public IEnumerator StowingLaysTheSticksOnTheSnareAndSilencesTheNodKick()
        {
            DrumsPlatform p = _module.Platform;
            DrumSticks sticks = _module.Sticks;
            HandState hand = _app.Input.Hand(Handedness.Right);
            // A held stick, carried away from the kit.
            Drumstick s = sticks[0];
            s.Hand = hand;
            hand.Holding = s;
            s.Root.SetParent(_app.WorldRoot, true);
            s.Root.position += new Vector3(0.3f, 0.2f, 0.1f);
            Assert.That(sticks.AnyHeld && _module.Tools.AnyHeld, Is.True);
            Assert.That(_module.Nod.Enabled, Is.True);
            p.SetStowed(true, StowMount.Floor);
            Assert.That(p.Stowed, Is.True);
            Assert.That(sticks.AnyHeld, Is.False, "the stick let go");
            Assert.That(hand.Holding, Is.Null);
            for (int i = 0; i < sticks.Count; i++)
            {
                Assert.That(sticks[i].Root.parent, Is.SameAs(_module.Kit.transform), "back on the kit");
                Assert.That(Vector3.Distance(sticks[i].Root.localPosition, sticks[i].RestPos), Is.LessThan(1e-5f), "on the snare at once (no settle on display)");
                Assert.That(Quaternion.Angle(sticks[i].Root.localRotation, sticks[i].RestRot), Is.LessThan(0.01f));
            }
            Assert.That(_module.Nod.Enabled, Is.False, "the nod kick is off on display");
            p.SetStowed(false, StowMount.Floor);
            Assert.That(p.Stowed, Is.False);
            Assert.That(_module.Nod.Enabled, Is.True, "back at the seat: the nod kick again");
            // A nod kick a test switched off stays off through a display round trip.
            _module.Nod.Enabled = false;
            p.SetStowed(true, StowMount.Floor);
            p.SetStowed(false, StowMount.Floor);
            Assert.That(_module.Nod.Enabled, Is.False);
            _module.Nod.Enabled = true;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheToolProviderIsAThinAdapterOverTheSticks()
        {
            DrumToolProvider tools = _module.Tools;
            DrumSticks sticks = _module.Sticks;
            Assert.That(tools.Owner, Is.EqualTo(InstrumentKind.Drums));
            Vector3 grip = sticks[1].GripWorld + new Vector3(0f, 0.05f, 0f);
            sticks.NearestFree(grip, out float want);
            Assert.That(tools.NearestFree(grip), Is.EqualTo(want).Within(1e-6f), "the distance TryGrab measures");
            Assert.That(want, Is.LessThan(StickSpec.GrabRadius));
            for (int i = 0; i < 3; i++) s_d = tools.NearestFree(grip);   // warm up (JIT)
            Assert.That(() => { s_d = tools.NearestFree(grip); }, Is.Not.AllocatingGCMemory());
            var spheres = new Vector4[4];
            Assert.That(tools.ShadowSpheres(spheres), Is.EqualTo(0), "no stick held: no tip shadow");
            Assert.That(tools.TryPickUp(null, grip), Is.EqualTo(StickPick.None), "no hand, no pick-up");
            // Not at the seat: nothing to reach.
            Rig rig = _module.Rig;
            rig.InScene = false;
            Assert.That(float.IsPositiveInfinity(tools.NearestFree(grip)), Is.True, "+inf while the kit is not at the seat");
            rig.InScene = true;
            // Held, then ReleaseAll.
            HandState hand = _app.Input.Hand(Handedness.Left);
            sticks[0].Hand = hand;
            hand.Holding = sticks[0];
            Assert.That(tools.AnyHeld, Is.True);
            tools.ReleaseAll();
            Assert.That(tools.AnyHeld || hand.Holding != null, Is.False);
            yield return null;
        }
    }
}
