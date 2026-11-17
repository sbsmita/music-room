// The resolver against the golden layout: every expected pose, eye distance, room shift, constrain path, settle and
// panel clear-out below was produced by the reference placement (Reference~/layout-golden.json) for the same rigs
// (the boxes it measured, rebuilt by GoldenRigScene) and seats, converted to Unity coordinates. The fitSeat fix is
// checked against the same reference run with the fixed dry run (clearEye before the room shift).
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Layout
{
    public class PlacementTests
    {
        const float PosTol = 2e-3f, EyeTol = 2e-3f;
        GoldenRigScene _scene;

        [SetUp] public void SetUp() => _scene = new GoldenRigScene();
        [TearDown] public void TearDown() => _scene.Dispose();

        static IEnumerable<string> ScenarioLabels()
        {
            foreach (var s in GoldenLayout.Scenarios) yield return s.Label;
        }

        static IEnumerable<string> VrScenarioLabels()
        {
            foreach (var s in GoldenLayout.Scenarios) if (!s.Ar) yield return s.Label;
        }

        static GoldenLayout.Scenario Find(string label)
        {
            foreach (var s in GoldenLayout.Scenarios) if (s.Label == label) return s;
            throw new System.ArgumentException(label);
        }

        static void AssertPose(string what, Transform g, GoldenLayout.RigPose want)
        {
            Assert.That(Vector3.Distance(g.position, want.Pos), Is.LessThan(PosTol), $"{what} at {g.position.ToString("F4")}, golden {want.Pos.ToString("F4")}");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(g.eulerAngles.y, want.YawDeg)), Is.LessThan(0.05f), $"{what} yaw {g.eulerAngles.y:F3}, golden {want.YawDeg:F3}");
        }

        [Test]
        public void MeasureMatchesTheGoldenFootprints()
        {
            _scene.PlaceHomes(false, -1, Vector3.up * 1.2f, 0f);
            for (int r = 0; r < 4; r++)
            {
                RigEntry e = _scene.Solver.Measure(_scene.Entries[r]);
                var want = GoldenLayout.Rects[r];
                Assert.That(e.Rects.Count, Is.EqualTo(want.Length), $"{GoldenLayout.RigNames[r]}: rectangles kept after dropping contained ones");
                for (int i = 0; i < want.Length; i++)
                {
                    Assert.That(e.Rects[i].X, Is.EqualTo(want[i].X).Within(1e-4f), $"{GoldenLayout.RigNames[r]} rect {i} x");
                    Assert.That(e.Rects[i].Z, Is.EqualTo(want[i].Z).Within(1e-4f), $"{GoldenLayout.RigNames[r]} rect {i} z");
                    Assert.That(e.Rects[i].Hx, Is.EqualTo(want[i].Hx).Within(1e-4f), $"{GoldenLayout.RigNames[r]} rect {i} hx");
                    Assert.That(e.Rects[i].Hz, Is.EqualTo(want[i].Hz).Within(1e-4f), $"{GoldenLayout.RigNames[r]} rect {i} hz");
                }
                Assert.IsTrue(e.HasBound);
                Assert.That(e.Bound.X, Is.EqualTo(GoldenLayout.Bound[r].X).Within(1e-4f));
                Assert.That(e.Bound.Z, Is.EqualTo(GoldenLayout.Bound[r].Z).Within(1e-4f));
                Assert.That(e.Bound.Hx, Is.EqualTo(GoldenLayout.Bound[r].Hx).Within(1e-4f));
                Assert.That(e.Bound.Hz, Is.EqualTo(GoldenLayout.Bound[r].Hz).Within(1e-4f));
            }
        }

        /// <summary>Every golden seat, without fitSeat (the desktop path): the resolver lands every rig where
        /// the reference did, with the same eye distances and no overlap.</summary>
        [TestCaseSource(nameof(ScenarioLabels))]
        public void ResolveMatchesTheGoldenForEverySeat(string label)
        {
            var s = Find(label);
            var solver = _scene.Solver;
            _scene.PlaceHomes(s.Single, s.Inst, s.Head, s.YawDeg);
            solver.InRoom = !s.Ar;
            bool ok = solver.Resolve(s.Head, s.YawDeg * Mathf.Deg2Rad);
            Assert.IsTrue(ok, "every rig ends free");
            for (int r = 0; r < 4; r++)
            {
                if (!s.After[r].InScene) { Assert.IsFalse(_scene.Rigs[r].Group.gameObject.activeSelf); continue; }
                AssertPose($"{label}/{GoldenLayout.RigNames[r]}", _scene.Rigs[r].Group, s.After[r]);
                float eye = solver.EyeReport(_scene.Entries[r], s.Head);
                Assert.That(eye, Is.EqualTo(s.Eye[r]).Within(EyeTol), $"{label}/{GoldenLayout.RigNames[r]} eye distance");
                Assert.That(eye, Is.GreaterThanOrEqualTo(GrabConstants.EyeClear - 1e-4f), "never within EYE_CLEAR of the eyes");
            }
            Assert.That(solver.Overlaps2D(), Is.EqualTo(s.Overlaps2D), "floor footprints");
            Assert.That(solver.Overlaps3D(), Is.EqualTo(s.Overlaps3D), "3D boxes");
        }

        [Test]
        public void AtTheOriginSeatTheFullRoomNeedsNoMoves()
        {
            // spec 07 section 8 [measured]: head (0, 1.2, 0), all mode: piano .628, drums .529, harp .74, table .603, no moves.
            _scene.PlaceHomes(false, -1, new Vector3(0f, 1.2f, 0f), 0f);
            Assert.IsTrue(_scene.Solver.Resolve(new Vector3(0f, 1.2f, 0f), 0f));
            Assert.That(_scene.Solver.LastMoves, Is.Zero);
            float[] want = { 0.628f, 0.529f, 0.74f, 0.603f };
            for (int r = 0; r < 4; r++) Assert.That(_scene.Solver.EyeReport(_scene.Entries[r], new Vector3(0f, 1.2f, 0f)), Is.EqualTo(want[r]).Within(1.5e-3f));
            Assert.IsFalse(_scene.Solver.RoomShift(out _), "fitSeat shift null");
        }

        /// <summary>fitSeat: the original dry run (no settle, no eye clearance) and the fixed one (clearEye with the walls
        /// off, then the room shift) give the golden shifts; the arrangement about the moved seat matches.</summary>
        [TestCaseSource(nameof(VrScenarioLabels))]
        public void FitSeatShiftsMatchTheGoldenAndTheFix(string label)
        {
            var s = Find(label);
            var solver = _scene.Solver;
            float yaw = s.YawDeg * Mathf.Deg2Rad;
            // the original dry run
            _scene.PlaceHomes(s.Single, s.Inst, s.Head, s.YawDeg);
            bool orig = solver.RoomShift(out Vector2 ws);
            Assert.That(orig, Is.EqualTo(s.HasGoldenShift), "original dry-run shift present");
            if (orig) Assert.That(Vector2.Distance(ws, s.GoldenShift), Is.LessThan(1e-3f), $"original shift {ws.ToString("F4")} vs {s.GoldenShift.ToString("F4")}");
            // the fixed dry run
            _scene.PlaceHomes(s.Single, s.Inst, s.Head, s.YawDeg);
            bool fix = solver.FitShift(s.Head, yaw, out Vector2 fs);
            Assert.That(fix, Is.EqualTo(s.HasFixedShift), "fixed dry-run shift present");
            if (fix) Assert.That(Vector2.Distance(fs, s.FixedShift), Is.LessThan(1e-3f), $"fixed shift {fs.ToString("F4")} vs {s.FixedShift.ToString("F4")}");
            Vector3 head = s.Head + (fix ? new Vector3(fs.x, 0f, fs.y) : Vector3.zero);
            Assert.That(Vector3.Distance(head, s.FixedHead), Is.LessThan(1e-3f), "the seat moved like the golden fixed fitSeat");
            _scene.PlaceHomes(s.Single, s.Inst, head, s.YawDeg);
            solver.InRoom = true;
            Assert.IsTrue(solver.Resolve(head, yaw));
            for (int r = 0; r < 4; r++)
            {
                if (!s.FixedAfter[r].InScene) continue;
                AssertPose($"{label}/{GoldenLayout.RigNames[r]} after fitSeat", _scene.Rigs[r].Group, s.FixedAfter[r]);
                Assert.That(solver.EyeReport(_scene.Entries[r], head), Is.EqualTo(s.FixedEye[r]).Within(EyeTol));
            }
            Assert.That(solver.Overlaps2D(), Is.EqualTo(s.FixedOverlaps2D));
        }

        [Test]
        public void SingleModeDrumsStaysCentredAfterTheFitSeatFix()
        {
            // spec 07 section 8 [measured] cliff: at the XR origin the kit's back reaches the back wall; pushed back for the
            // eyes it lands in the wall again and the original resolver slides it 0.73 m to the right. With the fixed dry run
            // the seat moves back 11.9 cm instead and the kit stays in front of the player.
            var s = Find("single-drums-origin");
            Assert.That(s.After[GoldenLayout.Drums].Pos.x, Is.GreaterThan(0.7f), "without the fix: the kit jumped sideways");
            var solver = _scene.Solver;
            _scene.PlaceHomes(true, GoldenLayout.Drums, s.Head, 0f);
            Assert.IsTrue(solver.FitShift(s.Head, 0f, out Vector2 shift));
            Assert.That(shift.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(shift.y, Is.EqualTo(-0.1187f).Within(1e-3f), "the seat moves back, away from the back wall");
            Vector3 head = s.Head + new Vector3(shift.x, 0f, shift.y);
            _scene.PlaceHomes(true, GoldenLayout.Drums, head, 0f);
            Assert.IsTrue(solver.Resolve(head, 0f));
            Vector3 kit = _scene.Rigs[GoldenLayout.Drums].Group.position;
            Assert.That(Mathf.Abs(kit.x - head.x), Is.LessThan(0.01f), "the kit stays centred on the seat");
            Assert.That(kit.z - head.z, Is.EqualTo(0.1f + 0.03f).Within(5e-3f), "only pushed back by the eye clearance");
            Assert.That(solver.EyeReport(_scene.Entries[GoldenLayout.Drums], head), Is.GreaterThanOrEqualTo(0.3f - 1e-4f));
            Assert.IsTrue(solver.Inside(solver.Pose(_scene.Entries[GoldenLayout.Drums])), "inside the room");
        }

        /// <summary>ARCHITECTURE.md 15.5's resolver numbers for single-mode drums: without the fix the kit jumps about 0.73 m
        /// to the right at head heights 1.2 and 1.1 and stays centred at 1.3; with the fixed fitSeat the seat moves straight
        /// back (no sideways shift) and the kit stays centred (|x| &lt; .05) at all three.</summary>
        [TestCase(1.2f, true, 0.29f)]
        [TestCase(1.1f, true, 0.237f)]
        [TestCase(1.3f, false, 0.363f)]
        public void SingleModeDrumsAtTheSpecHeights(float headY, bool jumpsWithoutFix, float eyeAfterWalls)
        {
            var solver = _scene.Solver;
            Vector3 head = new Vector3(0f, headY, 0f);
            // The original steps (no fix), which locate the cliff (spec 07 section 8 [measured]). fitSeat's dry run lays
            // the kit out at its single home, whose back reaches right-handed z -1.207 (Unity +1.207), outside the back wall at
            // +1.12; the room shift moves the seat back by .089; about the moved seat the resolver clears the eyes (the kit
            // goes back into the wall) and the walls (in again), which leaves the nearest part .29 / .237 m from the eye at
            // head heights 1.2 / 1.1: inside EYE_CLEAR, so findSpot still slides the kit sideways (by .075 m, the golden
            // single-drums-desktop slide, whose seat is nearly the same). At 1.3 it is .363 m away and stays centred. The
            // .725 m jump of the spec is the resolver about the unmoved head (no fitSeat, as the reference run and
            // the desktop do), checked after this.
            RigEntry kit = _scene.Entries[GoldenLayout.Drums];
            _scene.PlaceHomes(true, GoldenLayout.Drums, head, 0f);
            solver.InRoom = true;
            solver.Pose(solver.Measure(kit));
            PlacementSolver.Extent(kit, out _, out _, out _, out float back);
            Assert.That(back, Is.EqualTo(1.207f).Within(0.005f), "the dry run: the kit's back at right-handed z -1.207, past the back wall");
            Assert.IsTrue(solver.RoomShift(out Vector2 origShift), "the original fitSeat finds a room shift");
            Assert.That(origShift.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(origShift.y, Is.EqualTo(Find("single-drums-origin").GoldenShift.y).Within(1e-3f), "the seat moves back by the wall overlap (-.089)");
            Vector3 origSeat = head + new Vector3(origShift.x, 0f, origShift.y);
            _scene.PlaceHomes(true, GoldenLayout.Drums, origSeat, 0f);
            solver.SeatRect(origSeat, 0f);
            solver.Pose(solver.Measure(kit));
            solver.ClearEye(kit, origSeat);
            PlacementSolver.Extent(kit, out _, out _, out _, out float backEye);
            solver.ClearWalls(kit);
            float eye = PlacementSolver.EyeDistance(kit, origSeat);
            Assert.That(eye, Is.EqualTo(eyeAfterWalls).Within(0.005f), "the eye distance the wall shift leaves");
            Assert.That(eye < PlacementSolver.EyeClear, Is.EqualTo(jumpsWithoutFix), "inside EYE_CLEAR exactly where the original jumps");
            if (jumpsWithoutFix) Assert.That(backEye, Is.GreaterThan(solver.RoomZMax + PlacementSolver.WallSlop), "clearEye pushed it into the back wall again");
            _scene.PlaceHomes(true, GoldenLayout.Drums, origSeat, 0f);
            solver.Resolve(origSeat, 0f);
            Vector3 origKit = _scene.Rigs[GoldenLayout.Drums].Group.position;
            Assert.That(origKit.z, Is.EqualTo(0.011343f).Within(2e-3f), "the golden kit z (single-drums scenarios)");
            if (jumpsWithoutFix) Assert.That(origKit.x, Is.EqualTo(Find("single-drums-desktop").After[GoldenLayout.Drums].Pos.x).Within(0.01f), "findSpot still slides it sideways");
            else Assert.That(Mathf.Abs(origKit.x), Is.LessThan(0.05f));
            _scene.PlaceHomes(true, GoldenLayout.Drums, head, 0f);
            solver.InRoom = true;
            solver.Resolve(head, 0f);
            float origX = _scene.Rigs[GoldenLayout.Drums].Group.position.x;
            if (jumpsWithoutFix) Assert.That(origX, Is.EqualTo(0.725f).Within(0.01f), "the original findSpot slides the kit right (right-handed x .725)");
            else Assert.That(Mathf.Abs(origX), Is.LessThan(0.05f), "at 1.3 the original keeps the kit centred");
            _scene.PlaceHomes(true, GoldenLayout.Drums, head, 0f);
            Assert.IsTrue(solver.FitShift(head, 0f, out Vector2 shift));
            Assert.That(shift.x, Is.EqualTo(0f).Within(1e-4f), "the seat only moves back, never sideways");
            Assert.That(shift.y, Is.LessThan(0f), "away from the back wall");
            Vector3 seat = head + new Vector3(shift.x, 0f, shift.y);
            _scene.PlaceHomes(true, GoldenLayout.Drums, seat, 0f);
            Assert.IsTrue(solver.Resolve(seat, 0f));
            Assert.That(Mathf.Abs(_scene.Rigs[GoldenLayout.Drums].Group.position.x - seat.x), Is.LessThan(0.05f), "with the fix the kit stays centred");
            Assert.That(solver.EyeReport(_scene.Entries[GoldenLayout.Drums], seat), Is.GreaterThanOrEqualTo(0.3f - 1e-3f), "clear of the eyes");
        }

        [Test]
        public void APartMovedOutOfItsRigIsNotMeasured()
        {
            // placementSkip: a stick in a player's hand never counts toward the kit's footprint. The Drums module
            // reparents a held stick out of the kit (keeping its world pose), so a part no longer under the group is left
            // out even before the next collect.
            HomesAtOrigin();
            RigEntry kit = _scene.Entries[GoldenLayout.Drums];
            int boxes = kit.Local.Count;
            Transform part = kit.Footprint[kit.Footprint.Count - 1].R.transform;
            Transform home = part.parent;
            part.SetParent(_scene.Root.transform, true);
            _scene.Solver.Measure(kit);
            Assert.That(kit.Local.Count, Is.EqualTo(boxes - 1), "the part in the hand is not measured");
            part.SetParent(home, true);
            _scene.Solver.Measure(kit);
            Assert.That(kit.Local.Count, Is.EqualTo(boxes), "back on the kit, it counts again");
        }

        void HomesAtOrigin()
        {
            _scene.PlaceHomes(false, -1, new Vector3(0f, 1.2f, 0f), 0f);
            _scene.Solver.InRoom = true;
            _scene.Solver.MeasureAll();
        }

        [Test]
        public void ConstrainSlidesAlongThePianoLikeTheGolden()
        {
            HomesAtOrigin();
            var solver = _scene.Solver;
            RigEntry kit = _scene.Entries[GoldenLayout.Drums];
            Transform g = kit.Group;
            Assert.That(Vector3.Distance(g.position, GoldenLayout.PushStart), Is.LessThan(1e-5f));
            float yaw = GoldenLayout.PushYawDeg * Mathf.Deg2Rad;
            for (int i = 0; i < GoldenLayout.PushPath.Length; i++)
            {
                Vector3 p = g.position;
                bool lim = solver.Constrain(kit, p.x, p.z, yaw, p.x + GoldenLayout.PushDir.x * 0.02f, p.z + GoldenLayout.PushDir.z * 0.02f, yaw, out float y);
                Assert.That(lim, Is.EqualTo(GoldenLayout.PushLimited[i]), $"step {i} limited");
                Assert.That(Vector3.Distance(g.position, GoldenLayout.PushPath[i]), Is.LessThan(1e-3f), $"step {i} at {g.position.ToString("F4")}, golden {GoldenLayout.PushPath[i].ToString("F4")}");
                Assert.That(y, Is.EqualTo(yaw));
            }
            Assert.IsFalse(solver.TouchingAny(kit), "stopped at contact, never inside");
        }

        [Test]
        public void ConstrainStopsABigMoveAtContact()
        {
            HomesAtOrigin();
            RigEntry kit = _scene.Entries[GoldenLayout.Drums];
            float yaw = GoldenLayout.PushYawDeg * Mathf.Deg2Rad;
            Vector3 p = kit.Group.position;
            Assert.IsTrue(_scene.Solver.Constrain(kit, p.x, p.z, yaw, GoldenLayout.BigMoveTarget.x, GoldenLayout.BigMoveTarget.z, yaw, out _));
            Assert.That(Vector3.Distance(kit.Group.position, GoldenLayout.BigMoveEnd), Is.LessThan(1e-3f), $"{kit.Group.position.ToString("F4")} vs {GoldenLayout.BigMoveEnd.ToString("F4")}");
        }

        [Test]
        public void ConstrainRefusesATurnIntoContact()
        {
            HomesAtOrigin();
            var solver = _scene.Solver;
            RigEntry kit = _scene.Entries[GoldenLayout.Drums];
            Transform g = kit.Group;
            float yaw = GoldenLayout.PushYawDeg * Mathf.Deg2Rad;
            for (int i = 0; i < 40; i++)
            {
                Vector3 p = g.position;
                solver.Constrain(kit, p.x, p.z, yaw, p.x + GoldenLayout.PushDir.x * 0.01f, p.z + GoldenLayout.PushDir.z * 0.01f, yaw, out _);
            }
            Assert.That(Vector3.Distance(g.position, GoldenLayout.TurnAt), Is.LessThan(1e-3f), "against the piano");
            Vector3 at = g.position;
            for (int i = 0; i < GoldenLayout.TurnTriesDeg.Length; i++)
            {
                bool lim = solver.Constrain(kit, at.x, at.z, yaw, at.x, at.z, yaw + GoldenLayout.TurnTriesDeg[i] * Mathf.Deg2Rad, out float kept);
                Assert.That(lim, Is.True);
                Assert.That(kept == yaw, Is.EqualTo(GoldenLayout.TurnRefused[i]), $"turn {GoldenLayout.TurnTriesDeg[i]} refused");
                Assert.That(Mathf.DeltaAngle(g.eulerAngles.y, yaw * Mathf.Rad2Deg), Is.EqualTo(0f).Within(1e-3f));
            }
        }

        [Test]
        public void ConstrainLetsAnOverlappingRigMoveFreely()
        {
            HomesAtOrigin();
            RigEntry kit = _scene.Entries[GoldenLayout.Drums];
            float yaw = GoldenLayout.PushYawDeg * Mathf.Deg2Rad;
            kit.Group.position = GoldenLayout.OverlapStart;
            Assert.IsTrue(_scene.Solver.TouchingAny(kit), "already inside the piano");
            bool lim = _scene.Solver.Constrain(kit, GoldenLayout.OverlapStart.x, GoldenLayout.OverlapStart.z, yaw, GoldenLayout.OverlapStart.x + 0.1f, GoldenLayout.OverlapStart.z, yaw, out _);
            Assert.IsFalse(lim);
            Assert.That(Vector3.Distance(kit.Group.position, GoldenLayout.OverlapEnd), Is.LessThan(1e-4f));
        }

        [Test]
        public void SettleOneMovesTheReleasedRigOutLikeTheGolden()
        {
            var solver = _scene.Solver;
            Vector3 eye = new Vector3(0f, 1.2f, 0f);
            HomesAtOrigin();
            RigEntry kit = _scene.Entries[GoldenLayout.Drums];
            kit.Group.position = GoldenLayout.SettleKitStart;
            float by = solver.SettleOne(kit, eye, 0f);
            Assert.That(by, Is.EqualTo(GoldenLayout.SettleKitBy).Within(2e-3f), "kit out of the piano");
            Assert.That(Vector3.Distance(kit.Group.position, GoldenLayout.SettleKitEnd), Is.LessThan(PosTol));
            Assert.IsFalse(solver.TouchingAny(kit));

            HomesAtOrigin();
            RigEntry harp = _scene.Entries[GoldenLayout.Harp];
            harp.Group.SetPositionAndRotation(GoldenLayout.SettleHarpStart, Quaternion.Euler(0f, GoldenLayout.SettleHarpYawDeg, 0f));
            Assert.That(solver.EyeReport(harp, eye), Is.EqualTo(GoldenLayout.SettleHarpEye0).Within(EyeTol), "in the player's face");
            by = solver.SettleOne(harp, eye, 0f);
            Assert.That(by, Is.EqualTo(GoldenLayout.SettleHarpBy).Within(2e-3f));
            Assert.That(Vector3.Distance(harp.Group.position, GoldenLayout.SettleHarpEnd), Is.LessThan(PosTol));
            Assert.That(solver.EyeReport(harp, eye), Is.EqualTo(GoldenLayout.SettleHarpEye).Within(EyeTol));

            HomesAtOrigin();
            RigEntry table = _scene.Entries[GoldenLayout.Table];
            table.Group.SetPositionAndRotation(GoldenLayout.SettleTableStart, Quaternion.Euler(0f, GoldenLayout.SettleTableYawDeg, 0f));
            by = solver.SettleOne(table, eye, 0f);
            Assert.That(by, Is.EqualTo(GoldenLayout.SettleTableBy).Within(2e-3f), "in off the left wall by the least shift");
            Assert.That(Vector3.Distance(table.Group.position, GoldenLayout.SettleTableEnd), Is.LessThan(PosTol));

            HomesAtOrigin();
            Assert.That(solver.SettleOne(kit, eye, 0f), Is.Zero, "a rig already clear does not move");
        }

        [Test]
        public void ClearPanelLiftsOrPullsThePanelOutLikeTheGolden()
        {
            var solver = _scene.Solver;
            Vector3 eye = new Vector3(0f, 1.2f, 0f);
            solver.Panel.Shown = true;
            void Case(Vector3 start, float yaw0, bool hit, bool ok, Vector3 end, float yawEnd, string what)
            {
                HomesAtOrigin();
                _scene.Panel.SetPositionAndRotation(start, Quaternion.Euler(0f, yaw0, 0f));
                bool moved = solver.ClearPanel(eye);
                Assert.That(moved, Is.EqualTo(ok), what + ": cleared");
                Assert.That(Vector3.Distance(_scene.Panel.position, end), Is.LessThan(1e-3f), $"{what}: {_scene.Panel.position.ToString("F4")} vs {end.ToString("F4")}");
                Assert.That(Mathf.DeltaAngle(_scene.Panel.eulerAngles.y, yawEnd), Is.EqualTo(0f).Within(0.05f), what + ": faces the player");
                Assert.That(moved || !hit, Is.True, what + ": a panel inside an instrument always comes out");
            }
            Case(GoldenLayout.PanelFromPianoStart, GoldenLayout.PanelFromPianoYaw0Deg, GoldenLayout.PanelFromPianoHit, GoldenLayout.PanelFromPianoOk, GoldenLayout.PanelFromPianoEnd, GoldenLayout.PanelFromPianoYawDeg, "inside the piano");
            Case(GoldenLayout.PanelFromKitStart, GoldenLayout.PanelFromKitYaw0Deg, GoldenLayout.PanelFromKitHit, GoldenLayout.PanelFromKitOk, GoldenLayout.PanelFromKitEnd, GoldenLayout.PanelFromKitYawDeg, "inside the kit");
            Case(GoldenLayout.PanelFreeStart, GoldenLayout.PanelFreeYaw0Deg, GoldenLayout.PanelFreeHit, GoldenLayout.PanelFreeOk, GoldenLayout.PanelFreeEnd, GoldenLayout.PanelFreeYawDeg, "already clear");
            solver.Panel.Shown = false;
            HomesAtOrigin();
            _scene.Panel.position = GoldenLayout.PanelFromPianoStart;
            Assert.IsFalse(solver.ClearPanel(eye), "a hidden panel is left alone");
        }

        [Test]
        public void PageTurnRestPoseAfterASwivelMatchesTheGolden()
        {
            // startTurn: single drums at the desktop seat, then the seat moves to (0.3, 1.2, -0.1) facing -x
            // (Unity yaw -90) and turns to the harp; its rest pose is the single home about the NEW seat, settled with the
            // kit left out.
            var solver = _scene.Solver;
            _scene.PlaceHomes(true, GoldenLayout.Drums, new Vector3(0f, 1.2f, -0.1f), 0f);
            Assert.IsTrue(solver.Resolve(new Vector3(0f, 1.2f, -0.1f), 0f));
            Assert.That(Vector3.Distance(_scene.Rigs[GoldenLayout.Drums].Group.position, GoldenLayout.TurnKitRest), Is.LessThan(PosTol));
            Vector3 head = new Vector3(0.3f, 1.2f, -0.1f);
            float yaw = -90f;
            Rig harp = _scene.Rigs[GoldenLayout.Harp];
            harp.InScene = true;
            harp.Group.gameObject.SetActive(true);
            harp.Group.SetPositionAndRotation(LayoutMath.SeatPlace(head, yaw * Mathf.Deg2Rad, harp.HomeSingle), Quaternion.Euler(0f, harp.HomeYawSingle + yaw, 0f));
            Assert.IsTrue(solver.Resolve(head, yaw * Mathf.Deg2Rad, null, _scene.Entries[GoldenLayout.Drums]));
            Assert.That(Vector3.Distance(harp.Group.position, GoldenLayout.TurnRestIn), Is.LessThan(PosTol), $"{harp.Group.position.ToString("F4")} vs {GoldenLayout.TurnRestIn.ToString("F4")}");
            Assert.That(Mathf.DeltaAngle(harp.Group.eulerAngles.y, GoldenLayout.TurnHarpAfterYawDeg), Is.EqualTo(0f).Within(0.05f));
            Assert.That(Vector3.Distance(LayoutMath.SeatRight(yaw * Mathf.Deg2Rad), GoldenLayout.TurnRight), Is.LessThan(1e-4f), "slides across the new heading");
            Assert.That(Vector3.Distance(_scene.Rigs[GoldenLayout.Table].Group.position, GoldenLayout.TurnTableAfter), Is.LessThan(PosTol), "the table pushed clear of the incoming harp");
        }
    }
}
