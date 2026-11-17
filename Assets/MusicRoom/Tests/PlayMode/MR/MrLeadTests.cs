// Mixed reality with a lead instrument other than the piano (12-new-instruments/platform.md 9; 12.1 MrLeadTests), with the
// phase-0 stubs as leads. MrLeadTests uses a small stand-in Layout (LeadLayout: One alone or My set by a list, Lead = the
// one alone or the first member, PlaceAroundLead keeping the lead where MR put it, PutAway): the build's Layout still has
// the default PlaceAroundLead (PlaceAroundPiano, which re-poses every rig from the piano) until workstream L's lands.
// A table-top lead (the singing bowls, Mr.OnTable) goes on the best real table, lifted by its SurfaceLift, its
// IInstrumentSurface hiding its stand, facing the player, with the catcher and the sun on it; a floor-only lead (the
// marimba stub) stays on the real floor at its seat home, is never placed on a table and lands on the floor where the hand
// points; a set without the piano puts its first member on the table and the rest around it; the instruments out of the
// scene stay hidden through a placing; the draw guard puts the last member away when the at-seat DrawsAtSeat + 37 exceed
// 80 ("Put away: no room here", InstrumentSignalEvent mr.noroom). MrLeadDefaultSetTests boots the build's real Layout: the
// default set keeps the piano as the lead, 73 draws.
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.MR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.MR
{
    public class MrLeadTests : MrTestRig
    {
        protected override AppModule MakeLayoutStandIn() => _layout = new LeadLayout();
        LeadLayout _layout;

        IInstrumentSurface SurfaceOf(InstrumentKind k) => App.GetAll<IInstrumentSurface>().FirstOrDefault(s => s.Kind == k);

        static void FootprintOf(InstrumentDescriptor d, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            var boxes = d.Seat.Boxes.Where(b => !b.Air).ToList();
            minX = boxes.Min(b => b.Cx - b.Hx); maxX = boxes.Max(b => b.Cx + b.Hx);
            minZ = boxes.Min(b => b.Cz - b.Hz); maxZ = boxes.Max(b => b.Cz + b.Hz);
        }

        /// <summary>Every rig Layout keeps out of the scene is hidden; the lead shows.</summary>
        void AssertOnlyTheSceneShows(InstrumentKind lead, string when)
        {
            foreach (Rig r in App.Get<IRigRegistry>().Rigs)
            {
                if (r.Id == RigId.Lessons || r.Group == null) continue;
                if (!r.InScene) Assert.That(r.Group.gameObject.activeSelf, Is.False, r.Id + " out of the scene stays hidden: " + when);
            }
            Assert.That(Rig(InstrumentIds.RigOf(lead)).Group.gameObject.activeSelf, Is.True, "the lead shows: " + when);
        }

        IEnumerator Alone(InstrumentKind k)
        {
            var layout = App.Get<ILayoutService>();
            _layout.Use(LayoutMode.Single, k);
            yield return null;
            Assert.That(layout.Mode == LayoutMode.Single && layout.Current == k, Is.True, k + " alone");
            Assert.That(layout.Lead, Is.EqualTo(k), "One alone: the lead is Current");
        }

        [UnityTest]
        public IEnumerator ATableTopLeadGoesOnTheBestTableWithItsStandHidden()
        {
            const InstrumentKind lead = InstrumentKind.Bowls;
            InstrumentDescriptor d = App.Registry.Get(lead);
            Assume.That(d != null && App.Registry.IsLoaded(lead), "the singing bowls are loaded");
            Assert.That(d.Mr.OnTable, Is.True, "a table-top instrument");
            yield return Alone(lead);
            yield return EnterRoom("MeshOffice1");

            Assert.That(Mr.Lead, Is.EqualTo(lead));
            Rig rig = Rig(InstrumentIds.RigOf(lead));
            FootprintOf(d, out float minX, out float maxX, out float minZ, out float maxZ);
            TableQuery q = TableQuery.Default;
            q.wantedEdgeLength = maxX - minX;
            q.minEdgeLength = Mathf.Min(q.minEdgeLength, maxX - minX + 2f * TableFinder.EdgeMargin);
            q.minDepth = maxZ - minZ + TableFinder.EdgeMargin;
            Assert.That(TableFinder.TryFindBest(Room, App.Head.position, App.Head.forward, q, out var best), "a table that holds its footprint");
            Assert.That(Mr.LeadOnTable && Mr.TableLead == lead, Is.True, "on the table");
            Assert.That(SurfaceOf(lead).OnSurface, Is.True, "its IInstrumentSurface hid its stand");
            Assert.That(Stand.OnSurface, Is.False, "the piano's stand is not touched");
            Assert.That(rig.Group.position.y, Is.EqualTo(best.TopY + d.Mr.SurfaceLift).Within(1e-4f), "lifted by its SurfaceLift");
            Assert.That(Vector3.Dot(rig.Group.forward, best.TowardUser), Is.LessThan(-0.99f), "facing the player");
            Vector3 fromEdge = rig.Group.position - best.EdgeMidpoint; fromEdge.y = 0f;
            float push = Vector3.Dot(fromEdge, -best.TowardUser);
            Assert.That(push, Is.GreaterThanOrEqualTo(-minZ - 1e-3f), "its near side on the top");
            Assert.That(push + maxZ, Is.LessThanOrEqualTo(best.Depth - TableFinder.EdgeMargin + 1e-3f), "its far side on the top");
            Assert.That(push, Is.LessThanOrEqualTo(MrConstants.PushBack - d.Seat.PlayCentre.y + 1e-3f), "its play centre at most PushBack in");
            Assert.That(Mr.TryGetLeadPose(lead, out Pose pose, out bool onTable) && onTable, Is.True);
            Assert.That(Vector3.Distance(pose.position, rig.Group.position), Is.LessThan(1e-4f), "TryGetLeadPose is where it went");

            // The catcher on the table under it, the sun on it.
            yield return null;
            Assert.That(Mr.Lighting.CatcherVisible, Is.True);
            Vector3 c = Mr.Lighting.Catcher.position;
            Assert.That(c.y, Is.EqualTo(best.TopY + MrConstants.CatcherLift).Within(2e-3f), "the catcher on the table top");
            Assert.That(new Vector2(c.x - rig.Group.position.x, c.z - rig.Group.position.z).magnitude, Is.LessThan(1e-4f));
            Assert.That(Vector3.Dot(Mr.Lighting.Sun.transform.forward, (rig.Group.position - MrConstants.SunPosition).normalized), Is.GreaterThan(0.9999f));
            Assert.That(Mr.Hint, Does.Contain("the singing bowls"), "the hints name the lead");
            Assert.That(Mr.Hint, Does.Not.Contain("piano"));
            AssertOnlyTheSceneShows(lead, "arranged");
            Assert.That(Mr.DrawEstimate(), Is.EqualTo(MrConstants.FixedDraws + d.DrawsAtSeat));
            Assert.That(Mr.DrawEstimate(), Is.LessThanOrEqualTo(MrConstants.DrawBudget));
            IPianoApi piano = App.GetAll<IPianoApi>().First(a => !(a is PianoSurfaceProbe));
            Assert.That(piano.MusicRestPose, Is.Null, "the piano is not at the seat: no music rest");

            // Leaving: every stand back.
            Mr.Exit();
            yield return null;
            Assert.That(SurfaceOf(lead).OnSurface, Is.False, "its stand is back after mixed reality");
        }

        [UnityTest]
        public IEnumerator AFloorLeadStaysOnTheFloorAndIsPlacedOnTheFloorOnly()
        {
            const InstrumentKind lead = InstrumentKind.Marimba;
            InstrumentDescriptor d = App.Registry.Get(lead);
            Assume.That(d != null && App.Registry.IsLoaded(lead), "the marimba (stub) is loaded");
            Assert.That(d.Mr.OnTable, Is.False, "floor-only");
            yield return Alone(lead);
            yield return EnterRoom("MeshOffice1");

            Rig rig = Rig(InstrumentIds.RigOf(lead));
            float floor = TableFinder.FloorY(Room);
            Assert.That(Mr.Lead, Is.EqualTo(lead));
            Assert.That(Mr.LeadOnTable, Is.False, "never on a table");
            var seat = SeatFrame.From(App.Head);
            Vector3 home = seat.Place(rig.HomeSingle);
            Assert.That(new Vector2(rig.Group.position.x - home.x, rig.Group.position.z - home.z).magnitude, Is.LessThan(1e-3f), "in front of the player, at its seat home");
            Assert.That(rig.Group.position.y, Is.EqualTo(floor + rig.HomeSingle.y).Within(1e-3f), "on the real floor");
            Assert.That(Mr.TryGetLeadPose(lead, out Pose pose, out bool onTable) && !onTable, Is.True);
            Assert.That(new Vector2(pose.position.x - home.x, pose.position.z - home.z).magnitude, Is.LessThan(1e-3f));
            Assert.That(Mr.Hint, Is.EqualTo(MrConstants.EnterHintFloor.Replace(MrConstants.LeadSlot, "the marimba")));
            Assert.That(Mr.Lighting.Catcher.position.y, Is.EqualTo(floor + MrConstants.CatcherLift).Within(2e-3f), "catcher on the floor");
            Assert.That(Mr.DrawEstimate(), Is.LessThanOrEqualTo(MrConstants.DrawBudget));

            // Placing: the other rigs hide, the reticle shows the marimba's footprint.
            Vector3 air = App.Head.position + new Vector3(-0.55f, -0.25f, 0.05f);
            Fake.SetPose(HandSide.Right, RightAt(air, false));
            yield return null;
            Fake.SetPose(HandSide.Right, RightAt(air, true));
            yield return null;
            yield return null;
            Mr.StartPlacing();
            Assert.That(Mr.IsPlacing, Is.True);
            Assert.That(Rig(RigId.Table).Group.gameObject.activeSelf, Is.False, "the side table hides while the lead is placed");
            AssertOnlyTheSceneShows(lead, "placing");
            FootprintOf(d, out float minX, out float maxX, out float minZ, out float maxZ);
            Assert.That(Mr.PianoReticle.FootprintSize.x, Is.EqualTo(maxX - minX).Within(1e-4f), "the reticle shows its footprint");
            Assert.That(Mr.PianoReticle.FootprintSize.y, Is.EqualTo(maxZ - minZ).Within(1e-4f));
            Assert.That(Mr.PianoReticle.FootprintCentre.y, Is.EqualTo(MrConstants.PushBack + (minZ + maxZ) * 0.5f).Within(1e-4f));
            yield return null;
            Assert.That(Mr.Hint, Is.EqualTo(MrConstants.PlacingHintFloor.Replace(MrConstants.LeadSlot, "the marimba")));

            // A table under the ray is no place for it: no reticle.
            Assert.That(TableFinder.TryFindBest(Room, App.Head.position, App.Head.forward, TableQuery.Default, out var desk));
            Vector3 wrist = App.Head.position + new Vector3(0.15f, -0.35f, 0.15f);
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, desk.EdgeMidpoint - desk.TowardUser * 0.12f, 0.05f));
            yield return null;
            yield return null;
            Assert.That(Mr.PianoReticle.Visible, Is.False, "no reticle on the desk for a floor-only lead");

            // Open floor ahead: the reticle lies there and the pinch puts it on the floor.
            Vector3 target = Vector3.zero;
            bool found = false;
            for (float yaw = -60f; yaw <= 60f && !found; yaw += 10f)
                for (float dist = 1.0f; dist <= 1.8f && !found; dist += 0.2f)
                {
                    Vector3 f = Quaternion.Euler(0f, yaw, 0f) * MrUtil.Flat(App.Head.forward, Vector3.forward);
                    Vector3 p = new Vector3(App.Head.position.x, 0f, App.Head.position.z) + f * dist;
                    if (!TableFinder.TryPointAtSurface(Room, new Ray(new Vector3(p.x, floor + 2.0f, p.z), Vector3.down), out Vector3 down, out _, out bool table) || table) continue;
                    // the hand's ray must reach it unobstructed
                    if (!TableFinder.TryPointAtSurface(Room, new Ray(wrist, (down - wrist).normalized), out Vector3 along, out _, out bool t2) || t2 || Vector3.Distance(along, down) > 0.02f) continue;
                    target = down;
                    found = true;
                }
            Assert.That(found, "open floor in front of the player");
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, target, 0.05f));
            yield return null;
            yield return null;
            Assert.That(Mr.PianoReticle.Visible, Is.True, "the reticle on the floor");
            Vector3 hit = Mr.LastReticleHit;
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, target, 0f));
            yield return null;
            Assert.That(Mr.IsPlacing, Is.False, "placed");
            Pose want = PianoPlacement.FromHit(hit, App.Head.position, floor, d.Mr.SurfaceLift, rig.HomeSingle.y, false, out bool wantTable);
            Assert.That(wantTable, Is.False);
            Assert.That(Vector3.Distance(rig.Group.position, want.position), Is.LessThan(2e-3f), "where the hand pointed, PushBack beyond");
            Assert.That(rig.Group.position.y, Is.EqualTo(floor + rig.HomeSingle.y).Within(2e-3f), "on the floor");
            Assert.That(Mr.PlacedByPlayer && !Mr.LeadOnTable, Is.True);
            Assert.That(Mr.Hint, Is.EqualTo(MrConstants.PlacedHint.Replace(MrConstants.LeadName, "the marimba")));
            AssertOnlyTheSceneShows(lead, "after placing");
            Assert.That(Rig(RigId.Table).Group.gameObject.activeSelf, Is.True, "the side table is back");
        }

        [UnityTest]
        public IEnumerator ASetWithoutThePianoArrangesAroundItsFirstMember()
        {
            const InstrumentKind lead = InstrumentKind.Bowls;
            Assume.That(App.Registry.IsLoaded(lead) && App.Registry.IsLoaded(InstrumentKind.HandDrums), "the stubs are loaded");
            _layout.Use(LayoutMode.All, lead, InstrumentKind.HandDrums);
            yield return null;
            yield return EnterRoom("MeshOffice1");
            Assert.That(Mr.Lead, Is.EqualTo(lead));
            InstrumentDescriptor d = App.Registry.Get(lead);
            Rig rig = Rig(InstrumentIds.RigOf(lead));
            Assert.That(Mr.LeadOnTable && Mr.TableLead == lead, Is.True, "the bowls on the desk");
            Assert.That(App.GetAll<IInstrumentSurface>().First(s => s.Kind == lead).OnSurface, Is.True, "their stand hidden");
            Assert.That(Mr.TryGetLeadPose(lead, out Pose pose, out bool onTable) && onTable, Is.True);
            Assert.That(Vector3.Distance(rig.Group.position, pose.position), Is.LessThan(1e-4f));
            Assert.That(_layout.AroundLead, Is.GreaterThanOrEqualTo(1), "PlaceAroundLead, not PlaceAroundPiano");
            Assert.That(_layout.AroundPiano, Is.EqualTo(0));
            var drums = Rig(RigId.HandDrums);
            Assert.That(drums.Group.gameObject.activeSelf, Is.True, "the other member around it");
            Assert.That(Rig(RigId.Piano).Group.gameObject.activeSelf, Is.False, "the piano is not in the set: hidden");
            Assert.That(Stand.OnSurface, Is.False);
            Assert.That(Mr.DrawEstimate(), Is.EqualTo(MrConstants.FixedDraws + d.DrawsAtSeat + App.Registry.Get(InstrumentKind.HandDrums).DrawsAtSeat));
            Assert.That(Mr.DrawEstimate(), Is.LessThanOrEqualTo(MrConstants.DrawBudget));
        }

        [UnityTest]
        public IEnumerator TheDrawGuardPutsTheLastMemberAwayWhenTheSetIsOverBudget()
        {
            // Make the second member heavy: 37 + 2 + 60 = 99 draws > 80.
            _layout.Use(LayoutMode.All, InstrumentKind.Bowls, InstrumentKind.HandDrums);
            App.Registry.Get(InstrumentKind.HandDrums).DrawsAtSeat = 60;
            var signals = new List<InstrumentSignalEvent>();
            App.Events.Subscribe<InstrumentSignalEvent>(e => signals.Add(e));
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[MR\] Put away: no room here: handdrums"));
            yield return EnterRoom("MeshOffice1");
            Assert.That(_layout.PutAways, Is.EqualTo(new[] { InstrumentKind.HandDrums }), "the last member went back");
            Assert.That(_layout.MySet, Is.EqualTo(new[] { InstrumentKind.Bowls }));
            Assert.That(signals.Any(s => s.Kind == InstrumentKind.HandDrums && s.Signal == MrConstants.NoRoomSignal), Is.True, "the menu's note");
            Assert.That(Mr.DrawEstimate(), Is.LessThanOrEqualTo(MrConstants.DrawBudget));
            Assert.That(Rig(RigId.HandDrums).Group.gameObject.activeSelf, Is.False, "put away: hidden in mixed reality");
        }

    }

    /// <summary>A stand-in Layout (ARCHITECTURE.md 8 / 12 as ContractLayout, with the platform's members): One alone or My
    /// set by a list (Use), membership by it, every member at its home about the seat (no planner), Lead = the one alone or
    /// the first member, PlaceAroundLead = the others at their home offsets from the lead, which stays where MR put it,
    /// PutAway removes a member. Starts as the default set.</summary>
    public sealed class LeadLayout : AppModule, IRigRegistry, ILayoutService, IGrabService, IPlacementService
    {
        public override int Order => 100;
        App _app;
        readonly List<Rig> _rigs = new List<Rig>();
        readonly List<InstrumentKind> _set = new List<InstrumentKind> { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp };
        LayoutMode _mode = LayoutMode.All;
        public readonly List<InstrumentKind> PutAways = new List<InstrumentKind>();
        public int AroundLead, AroundPiano;
        public event Action Changed;

        public override void Init(App app)
        {
            _app = app;
            _rigs.AddRange(app.GetAll<Rig>());
            app.Register<IRigRegistry>(this);
            app.Register<ILayoutService>(this);
            app.Register<IGrabService>(this);
            app.Register<IPlacementService>(this);
            Apply();
            foreach (var r in _rigs) PoseRig(r, new SeatFrame { Head = new Vector3(0f, 1.2f, 0f) });
        }

        /// <summary>One alone on set[0], or My set = set; re-arranged about the player.</summary>
        public void Use(LayoutMode mode, params InstrumentKind[] set)
        {
            _mode = mode;
            _set.Clear();
            _set.AddRange(set);
            ArrangeAroundPlayer(true);
            _app.Events.Publish(new LayoutChangedEvent { Mode = _mode, Current = Current, SetVersion = PutAways.Count + 1 });
        }

        public override void Tick(FramePhase phase, float dt)
        {
            if (phase == FramePhase.FirstArrange && !FirstArrangeDone && _app.Head != null && _app.Rig.State != XrState.Starting) ArrangeAroundPlayer(true);
        }

        public IReadOnlyList<Rig> Rigs => _rigs;
        public Rig Get(RigId id) { for (int i = 0; i < _rigs.Count; i++) if (_rigs[i].Id == id) return _rigs[i]; return null; }
        public void Register(Rig rig) { if (rig != null && !_rigs.Contains(rig)) { _rigs.Add(rig); Changed?.Invoke(); } }
        public void Unregister(Rig rig) { if (_rigs.Remove(rig)) Changed?.Invoke(); }

        public LayoutMode Mode => _mode;
        public InstrumentKind Current => _set[0];
        public bool IsTurning => false;
        public bool FirstArrangeDone { get; private set; }
        public SeatFrame Seat { get; private set; }
        public IReadOnlyList<InstrumentKind> MySet => _set;
        public InstrumentKind Lead => _set[0];
        public void Set(LayoutMode mode, InstrumentKind instrument) => Use(mode, instrument);
        public void Next(int direction) { }
        public bool IsInScene(InstrumentKind k) => k == InstrumentKind.Table || k == InstrumentKind.Menu || (_mode == LayoutMode.All ? _set.Contains(k) : k == _set[0]);

        public bool PutAway(InstrumentKind k)
        {
            if (_mode != LayoutMode.All || _set.Count <= 1 || !_set.Remove(k)) return false;
            PutAways.Add(k);
            ArrangeAroundPlayer(true);
            return true;
        }

        public void ArrangeAroundPlayer(bool force, bool movePiano = true)
        {
            if (_app.Head == null) return;
            Apply();
            Seat = SeatFrame.From(_app.Head);
            foreach (var r in _rigs) PoseRig(r, Seat);
            FirstArrangeDone = true;
            _app.Input.ResetMotion();
            _app.Events.Publish(new ArrangedEvent { Forced = force });
        }

        public void PlaceAroundPiano() { AroundPiano++; }

        public void PlaceAroundLead()
        {
            AroundLead++;
            Rig lead = Get(InstrumentIds.RigOf(Lead));
            if (lead?.Group == null) return;
            bool single = _mode == LayoutMode.Single;
            Vector3 leadHome = single ? lead.HomeSingle : lead.HomeAll;
            float yaw = lead.Group.eulerAngles.y - (single ? lead.HomeYawSingle : lead.HomeYawAll);
            foreach (var r in _rigs)
            {
                if (r == lead || r.Group == null || !r.InScene) continue;
                Vector3 d = r.HomeAll - leadHome;
                Vector3 p = lead.Group.position + Quaternion.Euler(0f, yaw, 0f) * new Vector3(d.x, 0f, d.z);
                p.y = r.HomeAll.y;
                r.Group.SetPositionAndRotation(p, Quaternion.Euler(0f, r.HomeYawAll + yaw, 0f));
                if (r.Id != RigId.Lessons && !r.Group.gameObject.activeSelf) r.Group.gameObject.SetActive(true);
            }
        }

        public void RestoreHomes() { Apply(); foreach (var r in _rigs) PoseRig(r, new SeatFrame { Head = new Vector3(0f, 1.2f, 0f) }); }

        public Pose Anchor { get { var g = Get(InstrumentIds.RigOf(Lead))?.Group; return g != null ? new Pose(g.position, g.rotation) : Pose.identity; } }

        void PoseRig(Rig r, SeatFrame seat)
        {
            if (r.Group == null || !r.InScene) return;
            bool single = _mode == LayoutMode.Single && r.Id != RigId.Table && r.Id != RigId.Lessons;
            r.Group.SetPositionAndRotation(seat.Place(single ? r.HomeSingle : r.HomeAll), Quaternion.Euler(0f, seat.Yaw(single ? r.HomeYawSingle : r.HomeYawAll), 0f));
            if (r.Id != RigId.Lessons && !r.Group.gameObject.activeSelf) r.Group.gameObject.SetActive(true);
        }

        void Apply()
        {
            foreach (var r in _rigs)
            {
                if (r.Group == null) continue;
                bool inScene = r.Id == RigId.Table || r.Id == RigId.Lessons || IsInScene(InstrumentIds.KindOf(r.Id));
                r.InScene = inScene;
                if (r.Id != RigId.Lessons && r.Group.gameObject.activeSelf != inScene) r.Group.gameObject.SetActive(inScene);
            }
        }

        public Rig HeldRig => null;
        public bool AnyHeld => false;
        public Rig GrabTarget(Vector3 point, float reach = 0.07f) => null;
        public bool Resolve(bool useRoomBox) => true;
        public void SettleOne(Rig rig) { }
        public void ClearPanel(Rig panel) { }
        public Vector3? RoomShift() => null;
        public float EyeDistance(Rig rig) => float.PositiveInfinity;
    }

    /// <summary>The build's real Layout and the default set: the piano stays the lead, within the draw budget.</summary>
    public class MrLeadDefaultSetTests : MrTestRig
    {
        protected override bool UseContractLayout => false;

        [UnityTest]
        public IEnumerator TheDefaultSetKeepsThePianoAsTheLeadWithinTheDrawBudget()
        {
            yield return EnterRoom("MeshOffice1");
            var layout = App.Get<ILayoutService>();
            Assert.That(Mr.Lead, Is.EqualTo(layout.Lead));
            Assert.That(Mr.TableLead, Is.EqualTo(layout.Lead), "the default set's lead on the desk");
            Assert.That(Mr.LeadOnTable, Is.True);
            // piano 10 + drums 21 + harp 5 + the fixed 37 (platform.md 9.6).
            int expected = MrConstants.FixedDraws + layout.MySet.Sum(k => App.Registry.Get(k).DrawsAtSeat);
            Assert.That(Mr.DrawEstimate(), Is.EqualTo(expected));
            Assert.That(expected, Is.EqualTo(73));
            Assert.That(Mr.Hint, Is.EqualTo(MrConstants.EnterOnTableHint), "today's words for the piano");
        }
    }
}
#endif
