// Shared PlayMode harness for the Layout tests: boots through the foundation's TestBoot (scripted hands, a fresh
// MemorySettingsStore, a temporary data folder) with manual frames and, by default, a simulated headset (App.IsPresenting,
// so the XR-only paths run), the Layout module and the golden rigs probe (the stub instrument, room, menu, lessons, guide and
// MR modules are filtered out, so the merge's real modules cannot change what these tests see; the Audio module only where
// a test listens to a sound). It waits for the desktop rig, then drives every frame itself at 72 Hz
// (TestBoot.Step = App.Step: hands, pinch / grasp derivation, clock and one frame by exactly 1/72 s).
using System.Collections;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public abstract class LayoutPlayTestBase
    {
        protected const float DT = 1f / 72f;
        protected TestBoot Boot;
        protected FakeHandSource Fake;
        internal GoldenRigsProbe Probe;
        protected App App;
        protected LayoutModule Layout;
        protected Mulberry32 Rng = new Mulberry32(12345);

        /// <param name="presenting">BootOptions.SimulateHeadset: App.IsPresenting with the scripted hands (an XR session).
        /// False: the desktop (not presenting).</param>
        /// <param name="mouseHands">Start on the editor's mouse hand source (not presenting even with a simulated headset);
        /// <c>HandInput.OverrideSource = Fake</c> later is the headset's hands coming up.</param>
        /// <param name="settings">Optional: values put in the fresh settings store before the boot (persisted layout).</param>
        /// <param name="withStubs">Also load the five phase-0 stub instruments (their modules: neutral wood placeholders with
        /// a display configuration), so My set, the display, the glides and the ray summon have instruments to move.</param>
        protected IEnumerator BootApp(bool headless = true, IAppModule extra = null, bool withAudio = false, bool presenting = true,
            bool mouseHands = false, System.Action<MemorySettingsStore> settings = null, bool withStubs = false)
        {
            // These tests compare the placement with the frozen reference layouts, whose poses are the seat homes: the reach
            // band's pull toward the player (LayoutModule.Reach.cs, tested end to end by PlacementInFrontTests) is off here.
            LayoutModule.ReachBandDefault = false;
            // ... and they test the in-play dwell grab of the reference rigs (spec 05 section 8), which the app has off
            // (in play no pinch moves an instrument; they move with Move instruments: LayoutModule.InPlayGrab, MoveDragTests).
            LayoutModule.InPlayGrabDefault = true;
            Boot = new TestBoot(headless: headless, simulateHeadset: presenting, manualFrames: true);
            Fake = Boot.Hands;
            if (mouseHands) Boot.Options.HandSource = null;
            settings?.Invoke(Boot.Settings);
            Probe = new GoldenRigsProbe();
            System.Func<System.Type, bool> core = withAudio ? (System.Func<System.Type, bool>)(t => t == typeof(AudioModule) || t == typeof(LayoutModule)) : t => t == typeof(LayoutModule);
            Boot.Options.ModuleFilter = withStubs ? (t => core(t) || IsStubModule(t)) : core;
            Boot.Options.ExtraModules.Add(Probe);
            if (extra != null) Boot.Options.ExtraModules.Add(extra);
            yield return Boot.Start();
            App = App.Current;
            Layout = App.Get<ILayoutService>() as LayoutModule;
            Assert.That(Layout, Is.Not.Null, "the Layout module registered ILayoutService");
            float t0 = Time.realtimeSinceStartup;
            while (App.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.That(App.Rig.State, Is.EqualTo(XrState.Desktop), "no runtime in the editor: the desktop rig");
            Boot.Step(DT);   // the first frame with the desktop camera: FirstArrange
            Assert.IsTrue(Layout.FirstArrangeDone, "the first frame readied the room");
        }

        protected IEnumerator TearDownApp()
        {
            LayoutModule.ReachBandDefault = true;
            LayoutModule.InPlayGrabDefault = false;
            return Boot != null ? Boot.Stop() : null;
        }

        /// <summary>The phase-0 stub instruments' modules (the new instruments).</summary>
        protected static bool IsStubModule(System.Type t) =>
            t == typeof(MusicRoom.Handpan.HandpanModule) || t == typeof(MusicRoom.HandDrums.HandDrumsModule) || t == typeof(MusicRoom.Marimba.MarimbaModule)
            || t == typeof(MusicRoom.Bowls.BowlsModule);

        /// <summary>One app frame of exactly 1/72 s (TestBoot.Step), then one Unity frame.</summary>
        protected IEnumerator Frame()
        {
            Boot.Step(DT);
            yield return null;
        }

        protected IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return Frame();
        }

        protected Transform Head => App.Head;
        protected HandState Hand(HandSide side) => App.Input.Hand((Handedness)(int)side);

        /// <summary>Moves the player's rig so the head stands at <paramref name="head"/> facing <paramref name="yawDeg"/>
        /// (the desktop camera keeps its own local pose), then lays the room out around it.</summary>
        protected void SeatAt(Vector3 head, float yawDeg, bool arrange = true)
        {
            Transform rig = App.Rig.RigRoot;
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            rig.rotation = rot;
            rig.position = head - rot * DesktopRigFallback.EyePosition;
            if (arrange) Layout.ArrangeAroundPlayer(true);
        }

        protected Vector3 Jitter(Vector3 p, float amp)
        {
            if (amp <= 0f) return p;
            return p + new Vector3(Rng.NextFloat() * 2f - 1f, Rng.NextFloat() * 2f - 1f, Rng.NextFloat() * 2f - 1f) * amp;
        }

        /// <summary>A hand pinching (thumb on the index tip) with the index tip at <paramref name="p"/>, fingers along the
        /// heading <paramref name="yawDeg"/>, back of the hand up.</summary>
        protected static HandPose PinchAt(HandSide side, Vector3 p, float yawDeg = 0f) =>
            HandPose.Open(side, Vector3.zero, Quaternion.Euler(0f, yawDeg, 0f)).WithIndexTipAt(p).WithPinch(0f);

        /// <summary>The same hand open (thumb 11 cm from the index tip: no pinch).</summary>
        protected static HandPose OpenAt(HandSide side, Vector3 p, float yawDeg = 0f) =>
            HandPose.Open(side, Vector3.zero, Quaternion.Euler(0f, yawDeg, 0f)).WithIndexTipAt(p);

        /// <summary>Rig footprint centre on the floor (the rig's world box), measured now.</summary>
        protected Vector3 Centre(int rig)
        {
            RigEntry e = Layout.Placement.Entry(Probe.Rigs[rig]);
            Layout.Placement.Pose(Layout.Placement.Measure(e));
            return new Vector3(e.WB.X, 0f, e.WB.Z);
        }

        /// <summary>Handle(n): a grab handle on each rig where nothing is played.</summary>
        protected Vector3 Handle(int rig)
        {
            Transform g = Probe.Rigs[rig].Group;
            switch (rig)
            {
                case GoldenLayout.Piano: return g.TransformPoint(new Vector3(-0.69f, 0f, 0.12f));          // outside the left end block
                case GoldenLayout.Drums:
                {
                    Transform floor = Probe.Drums.Pieces[3].Transform;                                     // the floor tom's shell, side on
                    return floor.TransformPoint(new Vector3(0f, -0.2f, -(0.2f + 0.01f)));
                }
                case GoldenLayout.Harp: return g.TransformPoint(new Vector3(-0.69f, 0.6f, 0f));            // the pillar
                default: return g.TransformPoint(new Vector3(0f, 0.6f, -0.15f));                         // the table top's edge
            }
        }

        /// <summary>Releases both hands (open, then untracked) and lets the layout see it.</summary>
        protected IEnumerator ReleaseHands(Vector3 at)
        {
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, at));
            Fake.SetPose(HandSide.Left, OpenAt(HandSide.Left, at + Vector3.left * 0.3f));
            yield return Frame();
            Fake.SetTracked(HandSide.Right, false);
            Fake.SetTracked(HandSide.Left, false);
            yield return Frame();
        }

        /// <summary>PinchHold: a pinch held at a point (jittered) for the dwell and a little more; returns
        /// through the fields what it took and whether the dwell ring showed for it part way through.</summary>
        protected Rig HeldAfter;
        protected Rig HintAt12;
        protected bool RingAfter;

        /// <summary>Frames of a still pinch in empty air that start MR placing: GrabConstants.PlacingDwell (1 s) and a little more.</summary>
        protected static readonly int AirFrames = Mathf.CeilToInt(GrabConstants.PlacingDwell / DT) + 8;

        protected IEnumerator PinchHold(HandSide side, Vector3 at, int frames = 25, float amp = 0.01f, float yawDeg = 0f)
        {
            Fake.SetPose(side, OpenAt(side, at + Vector3.up * 0.04f, yawDeg));
            yield return Frame();
            HintAt12 = null;
            for (int i = 0; i <= frames; i++)
            {
                Fake.SetPose(side, PinchAt(side, Jitter(at, amp), yawDeg));
                yield return Frame();
                if (i == 12) HintAt12 = Layout.Visuals.HintVisible ? Layout.Visuals.HintRig : null;
            }
            HeldAfter = Layout.HeldRig;
            RingAfter = Layout.Visuals.RingVisible;
        }

        protected IEnumerator Release(HandSide side, Vector3 at)
        {
            Fake.SetPose(side, OpenAt(side, at));
            yield return Frame();
        }
    }
}
