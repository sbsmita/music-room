// "Move instruments" for the instruments on display (platform.md 4.1, the brief's step 8): the ray grab of arrange mode takes
// a displayed stub; a floor one slides on the floor, a wall one along its wall, a surface one stays on its sill; a place too
// near the head or in the furniture is refused (it stays at the last good place); the release is saved as
// musicroom.stow.<id> and survives a reboot; Reset brings back the plan's spot. Also the descriptor's lift ranges and the
// arrange slots for every rig.
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class DisplayArrangeTests : LayoutPlayTestBase
    {
        const HandSide Side = HandSide.Right;

        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        // A raised hand, above every instrument at the seat: since the room faces the fire (RoomFrame) the window's displays
        // are on the left, and a ray from shoulder height toward them passes the harp's crest (1.48 m) at the seat.
        Vector3 HandPoint => Head.position + new Vector3(0.25f, 0.9f, 0.1f);

        InstrumentView Stub(InstrumentKind k) => InstrumentView.Of(App, k);

        IEnumerator Point(Vector3 target, bool pinch, int frames)
        {
            Vector3 o = HandPoint;
            for (int i = 0; i < frames; i++)
            {
                Fake.SetPointer(Side, new Pose(o, Quaternion.LookRotation(target - o)));
                Fake.SetPose(Side, pinch ? PinchAt(Side, o) : OpenAt(Side, o));
                yield return Frame();
            }
        }

        /// <summary>Ray-grabs the instrument at its pick point, then turns the ray to <paramref name="to"/> (at the same
        /// distance from the hand) and lets go.</summary>
        IEnumerator DragTo(InstrumentKind k, Vector3 to, int frames = 30)
        {
            Assert.IsTrue(Layout.TryGetDisplayPoint(k, out Vector3 at), k + " on display");
            yield return Point(at, false, 3);
            yield return Point(at, true, 10);
            Assert.That(Layout.DisplayHeld, Is.SameAs(Stub(k).Rig), k + " taken by the ray");
            Vector3 o = HandPoint;
            float d = Vector3.Distance(o, at);
            Transform t = Stub(k).transform;
            for (int i = 1; i <= frames; i++)
            {
                Vector3 aim = Vector3.Lerp(at, to, i / (float)frames);
                Vector3 dir = (aim - o).normalized;
                Fake.SetPointer(Side, new Pose(o, Quaternion.LookRotation(dir)));
                Fake.SetPose(Side, PinchAt(Side, o));
                yield return Frame();
                TestContext.WriteLine($"{k} frame {i}: at {t.position.ToString("F3")} blocked {Layout.DisplayBlocked}");
            }
            yield return Point(to, false, 2);
            Assert.That(Layout.DisplayHeld, Is.Null, "let go");
        }

        [UnityTest]
        public IEnumerator AFloorInstrumentSlidesAndIsSavedAndResetBringsItBack()
        {
            yield return BootApp(withStubs: true);
            Layout.BeginArrange();
            yield return Frame();
            InstrumentKind k = InstrumentKind.Marimba;
            Transform t = Stub(k).transform;
            Vector3 home = t.position;
            Assert.IsTrue(Layout.Arrangeable(Stub(k).Rig), "a displayed instrument is arrangeable in the VR room");
            Assert.IsTrue(Layout.TryGetDisplayPoint(k, out Vector3 at));
            // toward the front wall (the hand drums stand behind it, the sill 2.5 cm beyond its far bars); room-frame directions,
            // turned into the world (the room faces the fire: RoomFrame)
            yield return DragTo(k, at + RoomFrame.DirToWorld(new Vector3(-0.15f, 0f, 0.35f)));
            Assert.That(t.position.y, Is.EqualTo(home.y).Within(1e-5f), "it slides on the floor");
            Assert.That(Vector3.Distance(t.position, home), Is.GreaterThan(0.1f), "it moved");
            Assert.That(Layout.DisplayMoves, Is.EqualTo(1));
            string stow = App.Settings.GetStow("marimba");
            Assert.IsTrue(LayoutModule.TryParseStow(stow, out double rx, out double rz, out double yaw), "musicroom.stow.marimba = " + stow);
            RoomFrame.ToWorld(rx, rz, out double x, out double z);   // saved in room coordinates
            Assert.That((float)x, Is.EqualTo(t.position.x).Within(1e-3f));
            Assert.That((float)z, Is.EqualTo(t.position.z).Within(1e-3f));
            Layout.EndArrange();
            Assert.That(Vector3.Distance(t.position, new Vector3((float)x, home.y, (float)z)), Is.LessThan(1e-3f), "it stays where it was put");
            Vector3 moved = t.position;
            yield return TearDownApp();
            // a reboot keeps it
            yield return BootApp(withStubs: true, settings: st => st.SetString(AppSettings.KeyStowPrefix + "marimba", stow));
            Assert.That(Vector3.Distance(Stub(k).transform.position, moved), Is.LessThan(1e-3f), "musicroom.stow.<id> survives a reboot");
            // Reset brings back the plan's spot
            Layout.BeginArrange();
            Layout.ResetArrangement();
            Layout.EndArrange();
            Assert.That(Vector3.Distance(Stub(k).transform.position, home), Is.LessThan(1e-4f), "Reset: the plan's spot");
            Assert.That(App.Settings.GetStow("marimba"), Is.EqualTo(""));
        }

        [UnityTest]
        public IEnumerator AWallInstrumentSlidesAlongItsWallAndASurfaceOneStaysOnItsSurface()
        {
            yield return BootApp(withStubs: true);
            Layout.BeginArrange();
            yield return Frame();
            Transform hp = Stub(InstrumentKind.Handpan).transform;
            Vector3 hpHome = hp.position;
            Assert.IsTrue(Layout.TryGetDisplayPoint(InstrumentKind.Handpan, out Vector3 at));
            yield return DragTo(InstrumentKind.Handpan, at + RoomFrame.DirToWorld(new Vector3(0.8f, 0f, 0.25f)));
            Assert.That(hp.position.x, Is.EqualTo(hpHome.x).Within(1e-4f), "along its wall: its distance to the wall kept");
            Assert.That(hp.position.y, Is.EqualTo(hpHome.y).Within(1e-4f));
            Assert.That(Mathf.Abs(hp.position.z - hpHome.z), Is.GreaterThan(0.05f), "it slid along the wall");
            Transform bowls = Stub(InstrumentKind.Bowls).transform;
            Vector3 bHome = bowls.position;
            Assert.IsTrue(Layout.TryGetDisplayPoint(InstrumentKind.Bowls, out Vector3 bAt));
            // toward the room, off the sill: it is held to the sill (its centre over the sill's top)
            yield return DragTo(InstrumentKind.Bowls, bAt + RoomFrame.DirToWorld(new Vector3(-1.0f, 0f, 0f)));
            Assert.That(RoomFrame.ToRoom(bowls.position).x, Is.GreaterThanOrEqualTo(3.0f - 1e-3f), "it stays on the sill");
            Assert.That(bowls.position.y, Is.EqualTo(bHome.y).Within(1e-5f));
            // along the sill: fine
            Assert.IsTrue(Layout.TryGetDisplayPoint(InstrumentKind.Bowls, out bAt));
            float z0 = RoomFrame.ToRoom(bowls.position).z;
            yield return DragTo(InstrumentKind.Bowls, bAt + RoomFrame.DirToWorld(new Vector3(0f, 0f, 0.3f)));
            Assert.That(RoomFrame.ToRoom(bowls.position).z - z0, Is.GreaterThan(0.1f), "it slid along the sill");
            Assert.That(bowls.position.y, Is.EqualTo(bHome.y).Within(1e-5f), "at the sill's height");
            Layout.EndArrange();
        }

        [UnityTest]
        public IEnumerator NotTooNearTheHeadNorIntoTheFurniture()
        {
            yield return BootApp(withStubs: true);
            Layout.BeginArrange();
            yield return Frame();
            Transform th = Stub(InstrumentKind.Marimba).transform;
            Vector3 home = th.position;
            Assert.IsTrue(Layout.TryGetDisplayPoint(InstrumentKind.Marimba, out Vector3 at));
            yield return Point(at, false, 3);
            yield return Point(at, true, 10);
            Assert.That(Layout.DisplayHeld, Is.SameAs(Stub(InstrumentKind.Marimba).Rig));
            // pull the hand back along the ray: the push gain brings it toward the head; it stops 1.1 m out
            Vector3 o = HandPoint, dir = (at - o).normalized;
            for (int i = 0; i < 90; i++)
            {
                Vector3 h = o - dir * (0.01f * i);
                Fake.SetPointer(Side, new Pose(h, Quaternion.LookRotation(dir)));
                Fake.SetPose(Side, PinchAt(Side, h));
                yield return Frame();
            }
            float d = new Vector2(th.position.x - Head.position.x, th.position.z - Head.position.z).magnitude;
            Assert.That(d, Is.GreaterThan(1.0f), "kept 1.1 m from the head (play centre)");
            Assert.IsTrue(Layout.DisplayBlocked, "refused (red brackets)");
            yield return Point(at, false, 2);
            Layout.EndArrange();
        }

        [Test]
        public void TheLiftRangesComeFromTheDescriptors()
        {
            // the stubs' own ranges (the classic ones: ArrangementCodecTests.HeightRangesAreTheSeatedComfortOnes)
            InstrumentRegistry reg = InstrumentRegistry.Discover();
            InstrumentDescriptor hp = reg.Get(InstrumentKind.Handpan);
            var r = new Rig { Id = RigId.Handpan, HomeAll = hp.Seat.Home };
            LayoutModule.LiftRange(r, out float lo, out float hi);
            Assert.That(lo, Is.EqualTo(hp.Seat.Home.y + hp.Seat.LiftMin).Within(1e-6f));
            Assert.That(hi, Is.EqualTo(hp.Seat.Home.y + hp.Seat.LiftMax).Within(1e-6f));
            Assert.That(ArrangeVisuals.Slots, Is.GreaterThanOrEqualTo(MySetRules.Max + 1 + 5), "My set, the side table and every displayed instrument");
        }
    }
}
