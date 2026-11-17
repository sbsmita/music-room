// Grab to move, the reference move cases driven frame by frame at 72 Hz with a jittery pinch (+-1 cm on
// every axis, every frame): the dwell, the hint and floor rings, drag, the play guard, play surfaces, the lock, the
// unstick through another rig and the settle on release, wrist turns, the two-hand twist and the off-hand step turn.
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class GrabTests : LayoutPlayTestBase
    {
        static readonly Vector3 OriginSeat = new Vector3(0f, 1.2f, 0f);

        [UnitySetUp] public IEnumerator SetUp() { yield return BootApp(); SeatAt(OriginSeat, 0f); }
        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        [UnityTest]
        public IEnumerator GrabTargetsAndPlaySurfacesMatchTheGolden()
        {
            foreach (var g in GoldenLayout.GrabPoints)
            {
                Rig t = Layout.GrabTarget(g.P);
                string name = t == null ? null : GoldenLayout.RigNames[(int)t.Id];
                Assert.That(name, Is.EqualTo(g.Target), $"{g.Label}: grab target");
                Assert.That(Layout.PlaySurfaceAt(g.P) != PlaySurface.None, Is.EqualTo(g.Surface != null), $"{g.Label}: play surface");
                Assert.That(Layout.NearInstrument(g.P), Is.EqualTo(g.Near), $"{g.Label}: near an instrument");
            }
            Bounds z = Layout.ZoneOf(Probe.Rigs[GoldenLayout.Piano]).Value;
            Assert.That(Vector3.Distance(z.min, GoldenLayout.ZonePiano.min), Is.LessThan(2e-3f), "the piano's zone = its parts' box + 12 cm");
            Assert.That(Vector3.Distance(z.max, GoldenLayout.ZonePiano.max), Is.LessThan(2e-3f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator AStillPinchOnAHandleGrabsAndADragMovesIt()
        {
            string[] names = GoldenLayout.RigNames;
            for (int n = 0; n < 4; n++)
            {
                Vector3 at = Handle(n);
                Vector3 c0 = Centre(n);
                var others = new Vector3[4];
                for (int m = 0; m < 4; m++) others[m] = Centre(m);
                Assert.That(Layout.GrabTarget(at), Is.SameAs(Probe.Rigs[n]), names[n] + ": the handle is this rig's");
                yield return PinchHold(HandSide.Right, at);
                Assert.That(HintAt12, Is.SameAs(Probe.Rigs[n]), names[n] + ": the dwell ring filled in round this rig");
                Assert.That(HeldAfter, Is.SameAs(Probe.Rigs[n]), names[n] + ": took hold after the 0.3 s dwell");
                Assert.IsTrue(RingAfter, names[n] + ": the floor ring shows while held");
                Assert.IsTrue(Layout.Visuals.CageVisible, names[n] + ": the edge cage round its parts");
                Assert.That(Layout.Visuals.CageOpacity, Is.EqualTo(0.3f).Within(1e-6f));
                Assert.IsFalse(Layout.Visuals.RingRed);
                Assert.IsTrue(Probe.Rigs[n].Held);
                Assert.That(Layout.AnyHeld, Is.True);
                // 20 cm: back for the table, away from the player for the others (the move cases, all mode)
                Vector3 away = c0 - Head.position; away.y = 0f; away.Normalize();
                Vector3 dir = n == GoldenLayout.Table ? -Vector3.forward : away;
                Vector3 to = at + dir * 0.2f;
                int ticks = Probe.Instruments[n].TickCount;
                for (int i = 1; i <= 30; i++)
                {
                    Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, Jitter(Vector3.Lerp(at, to, i / 30f), 0.01f)));
                    yield return Frame();
                }
                Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[n]), names[n] + ": still held while dragging");
                Assert.That(Probe.Instruments[n].TickCount, Is.EqualTo(ticks), names[n] + ": a held rig does not play");
                yield return Release(HandSide.Right, to);
                Assert.IsNull(Layout.HeldRig, names[n] + ": let go");
                Assert.IsFalse(Probe.Rigs[n].Held);
                Assert.IsFalse(Layout.Visuals.RingVisible);
                float moved = Vector3.Distance(Centre(n), c0);
                Assert.That(moved, Is.InRange(0.14f, 0.26f), names[n] + ": moved about 20 cm");
                for (int m = 0; m < 4; m++) if (m != n) Assert.That(Vector3.Distance(Centre(m), others[m]), Is.LessThan(1e-5f), names[m] + " stayed");
                SeatAt(OriginSeat, 0f);
            }
        }

        [UnityTest]
        public IEnumerator AMovingPinchNeverTakesHold()
        {
            Vector3 at = Handle(GoldenLayout.Drums);
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, at + Vector3.up * 0.04f));
            yield return Frame();
            for (int i = 0; i <= 25; i++)
            {
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, at + new Vector3(0f, 0f, 0.8f * DT * i)));
                yield return Frame();
            }
            Assert.IsNull(Layout.HeldRig, "a stroke at 0.8 m/s is playing, not grabbing");
            Assert.IsFalse(Layout.IsArmed(1));
        }

        [UnityTest]
        public IEnumerator AHandThatJustPlayedNeverGrabs()
        {
            HandState right = Hand(HandSide.Right);
            for (int n = 0; n < 4; n++)
            {
                right.LastPlayMs = App.Clock.NowMs - 800;
                yield return PinchHold(HandSide.Right, Handle(n));
                Assert.IsNull(HeldAfter, GoldenLayout.RigNames[n] + ": played 0.8 s ago");
                yield return Release(HandSide.Right, Handle(n));
                right.LastPlayMs = App.Clock.NowMs - 1100;
                yield return PinchHold(HandSide.Right, Handle(n));
                Assert.That(HeldAfter, Is.SameAs(Probe.Rigs[n]), GoldenLayout.RigNames[n] + ": played 1.1 s ago");
                yield return Release(HandSide.Right, Handle(n));
                right.LastPlayMs = double.NegativeInfinity;
                SeatAt(OriginSeat, 0f);
            }
        }

        [UnityTest]
        public IEnumerator NeverAGrabOverAPlayingSurface()
        {
            var pts = new System.Collections.Generic.List<(string, Vector3)>();
            Transform piano = Probe.Rigs[GoldenLayout.Piano].Group;
            pts.Add(("key C4", piano.TransformPoint(new Vector3(-0.05875f, 0.01f, 0.05f))));
            pts.Add(("top key", piano.TransformPoint(new Vector3(0.6f, 0.02f, 0.05f))));
            pts.Add(("low key", piano.TransformPoint(new Vector3(-0.6f, 0.02f, 0.05f))));
            foreach (var pc in Probe.Drums.Pieces) pts.Add((pc.Kind + (pc.Pad ? " pad" : ""), pc.Transform.TransformPoint(new Vector3(0f, pc.Type == DrumPieceType.Cymbal ? 0.02f : 0.05f, 0f))));
            Transform harp = Probe.Rigs[GoldenLayout.Harp].Group;
            pts.Add(("strings", harp.TransformPoint((GoldenLayout.HarpA[10] + GoldenLayout.HarpB[10]) * 0.5f + new Vector3(0f, 0f, -0.02f))));
            pts.Add(("low strings", harp.TransformPoint(Vector3.Lerp(GoldenLayout.HarpA[2], GoldenLayout.HarpB[2], 0.3f))));
            Transform table = Probe.Rigs[GoldenLayout.Table].Group;
            foreach (var b in GoldenLayout.DeckButtons) pts.Add(("deck button", table.TransformPoint(GoldenLayout.DeckPos + new Vector3(b.x, 0.1f, b.y))));
            Quaternion pr = Quaternion.Euler(0f, GoldenLayout.SwitchPanelYawDeg, 0f);
            foreach (float x in GoldenLayout.ToggleX) pts.Add(("switch", table.TransformPoint(GoldenLayout.SwitchPanelPos + pr * new Vector3(x, 0.045f, 0f))));
            foreach (var (label, p) in pts)
            {
                if (label == "kick") continue;   // the kick shell's front head lies on its side, out of reach of a pinch (the pad plays it)
                Assert.That(Layout.PlaySurfaceAt(p), Is.Not.EqualTo(PlaySurface.None), label + " is a play surface");
                yield return PinchHold(HandSide.Right, p);
                Assert.IsNull(HeldAfter, label + ": never a grab");
                Assert.IsNull(HintAt12, label + ": not even the dwell ring");
                yield return Release(HandSide.Right, p);
            }
        }

        /// <summary>The app's rule (InPlayGrab off, Move instruments off): a still pinch on an instrument's body moves
        /// nothing (and tells Sunny once), the menu panel still moves by its zone.</summary>
        [UnityTest]
        public IEnumerator WithMoveInstrumentsOffInstrumentsStayButTheMenuStillMoves()
        {
            Layout.InPlayGrab = false;
            int body = Layout.BodyPinches;
            for (int n = 0; n < 4; n++)
            {
                yield return PinchHold(HandSide.Right, Handle(n));
                Assert.IsNull(HeldAfter, GoldenLayout.RigNames[n] + " stays");
                Assert.IsNull(HintAt12);
                yield return Release(HandSide.Right, Handle(n));
            }
            Assert.That(Layout.BodyPinches, Is.GreaterThan(body), "the body pinches were seen (Sunny's tip_move_turn_on)");
            Probe.Menu.ShowAt(Head.position + new Vector3(0f, -0.2f, 0.5f));
            Assert.IsTrue(Layout.Grabbable(Probe.Menu.Rig));
            Vector3 face = Probe.Menu.Root.TransformPoint(new Vector3(0f, 0.11f, -0.004f));
            yield return PinchHold(HandSide.Right, face);
            Assert.That(HeldAfter, Is.SameAs(Probe.Menu.Rig), "the menu panel can always be grabbed");
            Assert.IsTrue(Layout.Visuals.RingVisible, "the floor ring shows for the panel too");
            Assert.IsFalse(Layout.Visuals.CageVisible, "but no cage");
            int menuTicks = Probe.Menu.TickCount;
            Vector3 to = face + new Vector3(0.15f, 0.1f, 0f);
            for (int i = 1; i <= 20; i++)
            {
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, Vector3.Lerp(face, to, i / 20f)));
                yield return Frame();
            }
            Assert.That(Probe.Menu.TickCount, Is.EqualTo(menuTicks), "no pokes while the panel is held");
            Assert.That(Vector3.Distance(Probe.Menu.Root.position.x * Vector3.right, (face.x + 0.15f - 0f) * Vector3.right), Is.LessThan(0.06f), "it follows the pinch");
            Assert.That(Probe.Menu.Root.position.y, Is.GreaterThan(face.y - 0.11f + 0.05f), "lifted with the hand");
            yield return Release(HandSide.Right, to);
            Assert.IsNull(Layout.HeldRig);
            Layout.InPlayGrab = true;
        }

        [UnityTest]
        public IEnumerator AConstraintNeverTrapsAGrab()
        {
            Transform kit = Probe.Rigs[GoldenLayout.Drums].Group;
            Vector3 a = Handle(GoldenLayout.Drums);
            yield return PinchHold(HandSide.Right, a, 25, 0.005f);
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]));
            Vector3 toPiano = Probe.Rigs[GoldenLayout.Piano].Group.TransformPoint(new Vector3(0.3f, 0f, 0.15f)) - Centre(GoldenLayout.Drums);
            toPiano.y = 0f;
            toPiano.Normalize();
            Vector3 target = a + toPiano * 0.5f;
            Vector3 pivot0 = Centre(GoldenLayout.Drums);
            double t0 = App.Clock.NowMs;
            double firstStop = -1, passAt = -1;
            bool red = false;
            for (int i = 1; i <= 60; i++)
            {
                Vector3 p = Vector3.Lerp(a, target, Mathf.Min(1f, i / 20f));
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, Jitter(p, 0.005f)));
                yield return Frame();
                Vector3 want = pivot0 + toPiano * (0.5f * Mathf.Min(1f, i / 20f));
                float lag = Vector3.Distance(Centre(GoldenLayout.Drums), want);
                if (firstStop < 0 && lag > 0.03f) firstStop = App.Clock.NowMs - t0;
                if (passAt < 0 && Layout.PassingThrough) passAt = App.Clock.NowMs - t0;
                if (Layout.PassingThrough && Layout.Visuals.RingRed) red = true;
            }
            Assert.That(firstStop, Is.GreaterThanOrEqualTo(0), "it stopped at the piano first");
            Assert.That(passAt, Is.GreaterThanOrEqualTo(firstStop + 250), "and went through only after pushing ~0.3 s");
            Assert.IsTrue(red, "red ring while passing through");
            float endLag = Vector3.Distance(Centre(GoldenLayout.Drums), pivot0 + toPiano * 0.5f);
            Assert.That(endLag, Is.LessThan(0.05f), "through, it follows the hand");
            Assert.IsTrue(Layout.Placement.TouchingAny(Layout.Placement.Entry(Probe.Rigs[GoldenLayout.Drums])), "inside the piano now");
            // back out: the constraint takes over again once clear
            for (int i = 1; i <= 20; i++)
            {
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, Jitter(Vector3.Lerp(target, a, i / 20f), 0.005f)));
                yield return Frame();
            }
            Assert.IsFalse(Layout.PassingThrough);
            // in again and let go inside the piano: it comes out on its own, clear of everything and of the eyes
            for (int i = 1; i <= 40; i++)
            {
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, Jitter(Vector3.Lerp(a, target, Mathf.Min(1f, i / 10f)), 0.005f)));
                yield return Frame();
            }
            Assert.IsTrue(Layout.Placement.TouchingAny(Layout.Placement.Entry(Probe.Rigs[GoldenLayout.Drums])), "let go inside");
            yield return Release(HandSide.Right, target);
            Assert.IsNull(Layout.HeldRig);
            Assert.That(Layout.Placement.Overlaps2D(), Is.Zero, "settled out of the piano");
            Assert.That(Layout.Placement.Overlaps3D(), Is.Zero);
            for (int r = 0; r < 4; r++) Assert.That(Layout.EyeDistance(Probe.Rigs[r]), Is.GreaterThanOrEqualTo(0.3f - 1e-4f), GoldenLayout.RigNames[r] + " clear of the eyes");
            _ = kit;
        }

        /// <summary>Open(n): set the rig down in the open floor between the seat and the fireplace (room frame (0, -2.3), turned
        /// by RoomFrame: 2.3 m ahead since the room faces the fire), out of everything's way.</summary>
        void Open(int n)
        {
            Transform g = Probe.Rigs[n].Group;
            Vector3 c = Centre(n);
            Vector3 open = RoomFrame.ToWorld(new Vector3(0f, 0f, -2.3f));
            g.position += new Vector3(open.x - c.x, 0f, open.z - c.z);
            Layout.Placement.MeasureAll();
        }

        [UnityTest]
        public IEnumerator AFullTurnWithTheWristAcrossFourGrabs()
        {
            for (int n = 0; n < 4; n++)
            {
                SeatAt(OriginSeat, 0f);
                Open(n);
                Transform g = Probe.Rigs[n].Group;
                float total = 0f;
                Vector3 c0 = Centre(n);
                float y0 = g.eulerAngles.y;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 at = Handle(n);
                    yield return PinchHold(HandSide.Right, at, 25, 0.01f, 0f);
                    Assert.That(HeldAfter, Is.SameAs(Probe.Rigs[n]), $"{GoldenLayout.RigNames[n]} grab {k}");
                    float yb = g.eulerAngles.y;
                    for (int i = 1; i <= 30; i++)
                    {
                        Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, Jitter(at, 0.01f), 90f * i / 30f + (Rng.NextFloat() * 2f - 1f)));
                        yield return Frame();
                    }
                    total += Mathf.DeltaAngle(yb, g.eulerAngles.y);
                    yield return Release(HandSide.Right, at);
                }
                Assert.That(total, Is.EqualTo(360f).Within(7f), GoldenLayout.RigNames[n] + ": four quarter turns with the wrist");
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(y0, g.eulerAngles.y)), Is.LessThan(7f), "back where it started");
                Assert.That(Vector3.Distance(Centre(n), c0), Is.LessThan(0.06f), "turned about its footprint's centre");
            }
        }

        [UnityTest]
        public IEnumerator ATwoHandTwistGoesPastHalfATurn()
        {
            foreach (int n in new[] { GoldenLayout.Drums, GoldenLayout.Piano })
            {
                SeatAt(OriginSeat, 0f);
                Open(n);
                Transform g = Probe.Rigs[n].Group;
                float y0 = g.eulerAngles.y;
                Vector3 c = Centre(n);
                Vector3 a = Handle(n);
                yield return PinchHold(HandSide.Right, a);
                Assert.That(HeldAfter, Is.SameAs(Probe.Rigs[n]));
                Vector3 ra = a - c; ra.y = 0f;
                Vector3 b = c - ra; b.y = a.y;
                Assert.IsTrue(Layout.ZoneOf(Probe.Rigs[n]).Value.Contains(b), "the second hand pinches the same rig");
                Fake.SetPose(HandSide.Left, OpenAt(HandSide.Left, b + Vector3.up * 0.04f));
                yield return Frame();
                Fake.SetPose(HandSide.Left, PinchAt(HandSide.Left, b));
                yield return Frame();
                Assert.That(Layout.SecondHand, Is.SameAs(Hand(HandSide.Left)), "two-hand twist");
                float turned = 0f, prev = g.eulerAngles.y;
                const int N = 60;
                for (int i = 1; i <= N; i++)
                {
                    Quaternion q = Quaternion.Euler(0f, 200f * i / N, 0f);
                    Vector3 pa = c + q * ra; pa.y = a.y;
                    Vector3 pb = c - q * ra; pb.y = a.y;
                    Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, Jitter(pa, 0.005f)));
                    Fake.SetPose(HandSide.Left, PinchAt(HandSide.Left, Jitter(pb, 0.005f)));
                    yield return Frame();
                    turned += Mathf.DeltaAngle(prev, g.eulerAngles.y);
                    prev = g.eulerAngles.y;
                }
                Assert.That(turned, Is.EqualTo(200f).Within(6f), GoldenLayout.RigNames[n] + ": followed the hands past 180 degrees");
                yield return Release(HandSide.Left, Vector3.zero);
                Assert.IsNull(Layout.SecondHand, "the second hand let go");
                Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[n]), "back to one hand");
                yield return ReleaseHands(a);
                _ = y0;
            }
        }

        [UnityTest]
        public IEnumerator TheOtherHandsSidewaysPullTurnsTheRig45Degrees()
        {
            Open(GoldenLayout.Harp);
            Transform g = Probe.Rigs[GoldenLayout.Harp].Group;
            float y0 = g.eulerAngles.y;
            Vector3 a = Handle(GoldenLayout.Harp);
            yield return PinchHold(HandSide.Right, a);
            Assert.That(HeldAfter, Is.SameAs(Probe.Rigs[GoldenLayout.Harp]));
            Vector3 b0 = Head.TransformPoint(new Vector3(-0.15f, -0.3f, 0.35f));
            Assert.IsFalse(Layout.ZoneOf(Probe.Rigs[GoldenLayout.Harp]).Value.Contains(b0), "the other hand is in the air, not on the harp");
            Fake.SetPose(HandSide.Left, OpenAt(HandSide.Left, b0 + Vector3.up * 0.04f));
            yield return Frame();
            Fake.SetPose(HandSide.Left, PinchAt(HandSide.Left, b0));
            yield return Frame();
            Assert.That(Layout.TurnerHand, Is.SameAs(Hand(HandSide.Left)));
            for (int i = 1; i <= 12; i++)
            {
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, Jitter(a, 0.005f)));
                Fake.SetPose(HandSide.Left, PinchAt(HandSide.Left, Head.TransformPoint(new Vector3(-0.15f + 0.15f * i / 12f, -0.3f, 0.35f))));
                yield return Frame();
            }
            float d1 = Mathf.DeltaAngle(y0, g.eulerAngles.y);
            Assert.That(Mathf.Abs(d1), Is.EqualTo(45f).Within(1.7f), "one 45 degree step");
            Assert.That(d1, Is.GreaterThan(0f), "a pull to the right turns it clockwise seen from above");
            yield return ReleaseHands(a);
        }

        [UnityTest]
        public IEnumerator LiftIsLimitedAndTheOriginStaysInTheRoom()
        {
            // Layout's ApplyLift / ClampRoom: no vertical follow inside the 3 cm dead band; then within -0.45..+0.6 m of the
            // height at the first grab; the origin clamped to x +-2.8, z -3.6..0.8 in the VR room; let go in the wall, the
            // rig comes back inside it.
            Transform g = Probe.Rigs[GoldenLayout.Harp].Group;
            float y0 = g.position.y;
            Vector3 a = Handle(GoldenLayout.Harp);
            yield return PinchHold(HandSide.Right, a, 25, 0f);
            Assert.That(HeldAfter, Is.SameAs(Probe.Rigs[GoldenLayout.Harp]));
            Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, a + Vector3.up * 0.02f));
            yield return Frame();
            Assert.That(g.position.y, Is.EqualTo(y0).Within(1e-5f), "a 2 cm wobble stays level");
            for (int i = 1; i <= 10; i++) { Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, a + Vector3.up * (0.1f * i))); yield return Frame(); }
            Assert.That(g.position.y, Is.EqualTo(y0 + GrabConstants.LiftMax).Within(1e-4f), "at most 0.6 m up");
            for (int i = 1; i <= 20; i++) { Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, a + Vector3.up * (1f - 0.1f * i))); yield return Frame(); }
            Assert.That(g.position.y, Is.EqualTo(y0 + GrabConstants.LiftMin).Within(1e-4f), "at most 0.45 m down");
            Vector3 p = a + Vector3.down * 1f;
            for (int i = 1; i <= 40; i++) { p += Vector3.left * 0.1f; Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, p)); yield return Frame(); }
            Assert.That(g.position.x, Is.EqualTo(GrabConstants.OriginClamp.xMin).Within(1e-4f), "the origin stops at x -2.8");
            yield return Release(HandSide.Right, p);
            var e = Layout.Placement.Entry(Probe.Rigs[GoldenLayout.Harp]);
            Layout.Placement.InRoom = true;
            Assert.IsTrue(Layout.Placement.Inside(Layout.Placement.Pose(Layout.Placement.Measure(e))), "settled inside the walls on release");
        }

        [UnityTest]
        public IEnumerator LosingTheHandLetsGo()
        {
            Vector3 at = Handle(GoldenLayout.Table);
            yield return PinchHold(HandSide.Right, at);
            Assert.That(HeldAfter, Is.SameAs(Probe.Rigs[GoldenLayout.Table]));
            Fake.SetTracked(HandSide.Right, false);
            yield return Frame();
            Assert.IsNull(Layout.HeldRig, "a grabbing hand that stops being tracked lets go");
            Assert.IsFalse(Probe.Rigs[GoldenLayout.Table].Held);
        }

        [UnityTest]
        public IEnumerator NoGrabWhileMixedRealityIsPlacingThePiano()
        {
            Probe.Mr.Active = true;
            Probe.Mr.IsPlacing = true;
            yield return PinchHold(HandSide.Right, Handle(GoldenLayout.Harp));
            Assert.IsNull(HeldAfter);
            Assert.IsFalse(Layout.Visuals.HintVisible);
            Probe.Mr.IsPlacing = false;
            yield return Release(HandSide.Right, Handle(GoldenLayout.Harp));
            yield return PinchHold(HandSide.Right, Handle(GoldenLayout.Harp));
            Assert.That(HeldAfter, Is.SameAs(Probe.Rigs[GoldenLayout.Harp]), "placing over: grabs again");
            yield return Release(HandSide.Right, Handle(GoldenLayout.Harp));
            // A still pinch in empty air in mixed reality moves nothing (the old "place the piano again" is retired).
            int before = Layout.EmptyAirPinches;
            yield return PinchHold(HandSide.Right, Head.position + new Vector3(0f, 0.3f, 0.2f), AirFrames);
            Assert.That(Layout.EmptyAirPinches, Is.EqualTo(before + 1));
            Assert.That(Probe.Mr.StartPlacingCount, Is.Zero);
            Probe.Mr.IsPlacing = false;
            Probe.Mr.Active = false;
        }
    }
}
