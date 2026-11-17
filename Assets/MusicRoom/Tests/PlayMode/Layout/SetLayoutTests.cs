// My set in the booted app (platform.md 4.2, 4.7, 12.1 SetLayoutTests), with the golden rigs (the probe: the classic three,
// no display configuration of their own yet) and the five stub instruments on display. The default set is today's room
// exactly (rig poses and the eye distances .628 / .529 / .74 / .603 of the golden first arrangement); a stub joins its
// planned slot; a full set replaces the least recently played member (the side table yields when the plan needs it); the
// menu never brings one alone; PutAway sends a member back and the last one stays; the set is persisted in planner order and
// restored (a set that no longer fits is re-planned); the full room's arrangement merges on save.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class SetLayoutTests : LayoutPlayTestBase
    {
        static readonly InstrumentKind[] Stubs = { InstrumentKind.Handpan, InstrumentKind.HandDrums, InstrumentKind.Marimba, InstrumentKind.Bowls };
        readonly List<InstrumentSignalEvent> _signals = new List<InstrumentSignalEvent>();
        readonly List<InstrumentMoveEvent> _moves = new List<InstrumentMoveEvent>();
        System.Action<InstrumentSignalEvent> _onSignal;
        System.Action<InstrumentMoveEvent> _onMove;

        IEnumerator Start(System.Action<MemorySettingsStore> settings = null)
        {
            yield return BootApp(withStubs: true, settings: settings);
            _signals.Clear();
            _moves.Clear();
            _onSignal = e => _signals.Add(e);
            _onMove = e => _moves.Add(e);
            App.Events.Subscribe(_onSignal);
            App.Events.Subscribe(_onMove);
        }

        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        static GoldenLayout.Scenario Find(string label)
        {
            foreach (var s in GoldenLayout.Scenarios) if (s.Label == label) return s;
            throw new System.ArgumentException(label);
        }

        InstrumentView Stub(InstrumentKind k) => InstrumentView.Of(App, k);

        /// <summary>Frames until nothing glides (at most 2 s).</summary>
        IEnumerator Settled()
        {
            for (int i = 0; i < 150 && Layout.AnyMoving; i++) yield return Frame();
            Assert.IsFalse(Layout.AnyMoving, "every glide ended within 2 s");
        }

        Vector3 Planned(InstrumentKind k, out float yawDeg)
        {
            Assert.IsTrue(Layout.CurrentPlan.TryGet(k, out SeatPlanner.Member m), k + " is planned");
            yawDeg = (float)m.Pose.Yaw + Layout.Seat.YawDeg;
            Rig r = Layout.Get(InstrumentIds.RigOf(k));
            return LayoutMath.SeatPlace(Layout.Seat.Head, Layout.Seat.YawDeg * Mathf.Deg2Rad, new Vector3((float)m.Pose.X, r.HomeAll.y, (float)m.Pose.Z));
        }

        [UnityTest]
        public IEnumerator TheDefaultSetIsTodaysRoomWithTheStubsOnDisplay()
        {
            yield return Start();
            var s = Find("all-desktop");
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp }), "musicroom.set missing: today's room");
            for (int r = 0; r < 4; r++)
            {
                Rig rig = Probe.Rigs[r];
                Assert.IsTrue(rig.InScene, GoldenLayout.RigNames[r]);
                Assert.That(Vector3.Distance(rig.Group.position, s.FixedAfter[r].Pos), Is.LessThan(2e-3f), GoldenLayout.RigNames[r] + " exactly where today's first arrangement puts it");
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(rig.Group.eulerAngles.y, s.FixedAfter[r].YawDeg)), Is.LessThan(0.05f), GoldenLayout.RigNames[r] + " yaw");
                Assert.That(Layout.EyeDistance(rig), Is.EqualTo(s.Eye[r]).Within(2e-3f), GoldenLayout.RigNames[r] + ": the eye distance is unchanged");
            }
            Assert.That(s.Eye[0], Is.EqualTo(0.628f).Within(2e-3f));
            Assert.That(s.Eye[1], Is.EqualTo(0.529f).Within(2e-3f));
            Assert.That(s.Eye[2], Is.EqualTo(0.74f).Within(2e-3f));
            Assert.That(s.Eye[3], Is.EqualTo(0.603f).Within(2e-3f));
            Assert.That(Layout.SlotOf(InstrumentKind.Piano), Is.EqualTo(SeatSlot.Front));
            Assert.That(Layout.SlotOf(InstrumentKind.Drums), Is.EqualTo(SeatSlot.Right));
            Assert.That(Layout.SlotOf(InstrumentKind.Harp), Is.EqualTo(SeatSlot.Left));
            Assert.That(Layout.Lead, Is.EqualTo(InstrumentKind.Piano), "the lead: the member in slot Front");
            Assert.That(Layout.Anchor.position, Is.EqualTo(Probe.Rigs[GoldenLayout.Piano].Group.position));
            Assert.IsFalse(Layout.CurrentPlan.TableAlt, "the side table at its home");
            foreach (InstrumentKind k in Stubs)
            {
                InstrumentView p = Stub(k);
                Assert.That(p, Is.Not.Null, k + " stub loaded");
                Assert.That(Layout.PlaceOf(k), Is.EqualTo(InstrumentPlace.Display), k + " on display");
                Assert.IsTrue(p.gameObject.activeInHierarchy, k + ": the rig stays active in the VR room");
                Assert.IsFalse(p.Rig.InScene, k + ": not present");
                Assert.IsFalse(p.IsPresent);
                Assert.IsTrue(p.Stowed, k + ": SetStowed(true, mount)");
                StowSpec spot = App.Registry.Get(k).Stow;
                // the spot is in room coordinates: turned into the world with the room (RoomFrame)
                Assert.That(Vector3.Distance(p.transform.position, RoomFrame.ToWorld(spot.Pose)), Is.LessThan(1e-4f), k + " at its spot " + spot.SpotId);
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(p.transform.eulerAngles.y, spot.Yaw + RoomFrame.Yaw)), Is.LessThan(0.01f));
                Assert.IsTrue(Layout.TryGetDisplayPoint(k, out Vector3 pick), k + " has a pick point");
                Assert.That(Layout.SlotOf(k), Is.Null);
            }
            yield return Frames(10);
            Assert.That(Stub(InstrumentKind.Handpan).LiveKnocks, Is.Zero, "a displayed instrument is never ticked");
            Assert.That(Layout.Placement.Overlaps2D(), Is.Zero);
        }

        [UnityTest]
        public IEnumerator AStubJoinsItsPlannedSlot()
        {
            yield return Start();
            Assert.IsTrue(Layout.TrySetSet(new[] { InstrumentKind.Piano, InstrumentKind.Drums }));
            yield return Settled();
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums }));
            Assert.That(Layout.PlaceOf(InstrumentKind.Harp), Is.EqualTo(InstrumentPlace.Hidden), "the probe's harp has no display configuration: hidden as today");
            Assert.IsFalse(Probe.Rigs[GoldenLayout.Harp].Group.gameObject.activeSelf);
            Assert.That(Layout.PreviewJoin(InstrumentKind.Bowls).Action, Is.EqualTo(JoinAction.Add));
            Assert.IsTrue(Layout.CanAdd(InstrumentKind.Bowls));
            int version = Layout.SetVersion;
            _signals.Clear();
            Assert.IsTrue(Layout.Bring(InstrumentKind.Bowls, BringReason.Menu));
            Assert.That(Layout.SetVersion, Is.EqualTo(version + 1));
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Bowls }), "in planner order");
            Assert.IsTrue(Layout.IsMoving(InstrumentKind.Bowls));
            Assert.That(Layout.PlaceOf(InstrumentKind.Bowls), Is.EqualTo(InstrumentPlace.Gliding));
            Assert.That(_signals.Exists(e => e.Kind == InstrumentKind.Bowls && e.Signal == "layout.bring.menu"));
            yield return Settled();
            Assert.That(Layout.SlotOf(InstrumentKind.Bowls), Is.EqualTo(SeatSlot.FrontLeft), "the vectors: piano + drums + bowls -> bowls@FrontLeft");
            Vector3 want = Planned(InstrumentKind.Bowls, out float yaw);
            InstrumentView bowls = Stub(InstrumentKind.Bowls);
            Assert.That(Vector3.Distance(bowls.transform.position, want), Is.LessThan(1e-3f), "at its planned pose " + want.ToString("F3"));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(bowls.transform.eulerAngles.y, yaw)), Is.LessThan(0.05f));
            Assert.IsTrue(bowls.IsPresent && !bowls.Stowed, "present and unstowed on arrival");
            Assert.That(Layout.PlaceOf(InstrumentKind.Bowls), Is.EqualTo(InstrumentPlace.Seat));
            Assert.That(App.Settings.SetIds, Is.EqualTo("piano,drums,bowls"), "persisted in planner order");
            Assert.IsFalse(Layout.Bring(InstrumentKind.Bowls, BringReason.Menu), "already here");
            Assert.That(Layout.PreviewJoin(InstrumentKind.Bowls).Action, Is.EqualTo(JoinAction.Already));
        }

        [UnityTest]
        public IEnumerator AFullSetReplacesTheLeastRecentlyPlayed()
        {
            yield return Start();
            // the piano and the drums were played live just now; the harp never: it is the least recently played
            App.Events.Publish(new RecorderEvent { Event = new TakeEvent { type = TakeEventType.PianoOn, midi = 60, vel = 0.5f } });
            App.Events.Publish(new RecorderEvent { Event = new TakeEvent { type = TakeEventType.Drum, kind = "snare", vel = 0.5f } });
            JoinPreview p = Layout.PreviewJoin(InstrumentKind.Handpan);
            Assert.That(p.Action, Is.EqualTo(JoinAction.Replace));
            Assert.That(p.Replaced, Is.EqualTo(InstrumentKind.Harp));
            Assert.That(Layout.ReplaceCandidate(InstrumentKind.Handpan), Is.EqualTo(InstrumentKind.Harp));
            Assert.IsFalse(Layout.CanAdd(InstrumentKind.Handpan), "the set is full");
            Assert.IsTrue(Layout.Bring(InstrumentKind.Handpan, BringReason.Menu));
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Handpan }));
            Assert.That(_signals.Exists(e => e.Kind == InstrumentKind.Harp && e.Signal == "layout.replaced"), "layout.replaced names the harp");
            yield return Settled();
            Assert.That(Layout.SlotOf(InstrumentKind.Handpan), Is.EqualTo(SeatSlot.Left), "the vectors: the handpan takes the left slot");
            Assert.IsTrue(Layout.CurrentPlan.TableAlt, "and the side table yields to its alternate");
            Rig table = Probe.Rigs[GoldenLayout.Table];
            Vector3 alt = LayoutMath.SeatPlace(Layout.Seat.Head, Layout.Seat.YawDeg * Mathf.Deg2Rad, new Vector3(BuiltIns.TableAlternate.x, table.HomeAll.y, BuiltIns.TableAlternate.z));
            // the resolver may still nudge it clear of the kit's real meshes (the descriptor boxes are the planner's)
            Assert.That(Vector3.Distance(table.Group.position, alt), Is.LessThan(0.1f), "the table behind-left at " + table.Group.position.ToString("F3"));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(table.Group.eulerAngles.y, BuiltIns.TableAlternateYaw + Layout.Seat.YawDeg)), Is.LessThan(0.5f), "turned to face the seat");
            Assert.That(Layout.PlaceOf(InstrumentKind.Harp), Is.EqualTo(InstrumentPlace.Hidden), "the probe's harp: hidden as today");
            Vector3 want = Planned(InstrumentKind.Handpan, out _);
            Assert.That(Vector3.Distance(Stub(InstrumentKind.Handpan).transform.position, want), Is.LessThan(0.02f));
            Assert.That(Layout.Placement.Overlaps2D(), Is.Zero);
            // The harp back: the handpan (now the least recently played) makes room, and the table goes home.
            Assert.IsTrue(Layout.Bring(InstrumentKind.Harp, BringReason.Menu));
            yield return Settled();
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp }));
            Assert.IsFalse(Layout.CurrentPlan.TableAlt);
            var s = Find("all-desktop");
            Assert.That(Vector3.Distance(table.Group.position, s.FixedAfter[GoldenLayout.Table].Pos), Is.LessThan(2e-3f), "the side table back at its home");
            Assert.That(Layout.PlaceOf(InstrumentKind.Handpan), Is.EqualTo(InstrumentPlace.Display));
        }

        [UnityTest]
        public IEnumerator TheMenuNeverBringsOneAloneButTheRayMay()
        {
            yield return Start();
            Assert.That(Layout.PreviewJoin(InstrumentKind.Marimba).Action, Is.EqualTo(JoinAction.Alone), "the marimba fits beside none of them");
            Assert.IsFalse(Layout.Bring(InstrumentKind.Marimba, BringReason.Menu), "the menu says: No room");
            Assert.That(Layout.MySet.Count, Is.EqualTo(3));
            Assert.IsTrue(Layout.Bring(InstrumentKind.Marimba, BringReason.Ray));
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Marimba }), "it comes alone");
            yield return Settled();
            Vector3 single = LayoutMath.SeatPlace(Layout.Seat.Head, Layout.Seat.YawDeg * Mathf.Deg2Rad, App.Registry.Get(InstrumentKind.Marimba).Seat.Single);
            Assert.That(Vector3.Distance(Stub(InstrumentKind.Marimba).transform.position, single), Is.LessThan(0.02f), "a set of one: its Single home");
            Assert.That(Layout.Mode, Is.EqualTo(LayoutMode.All), "still My set");
        }

        [UnityTest]
        public IEnumerator PutAwaySendsAMemberBackAndTheLastStays()
        {
            yield return Start();
            Assert.IsTrue(Layout.TrySetSet(new[] { InstrumentKind.Piano, InstrumentKind.Bowls }));
            yield return Settled();
            _moves.Clear();
            Assert.IsTrue(Layout.PutAway(InstrumentKind.Bowls));
            Assert.That(_signals.Exists(e => e.Kind == InstrumentKind.Bowls && e.Signal == "layout.putaway"));
            yield return Settled();
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Piano }));
            Assert.That(Layout.PlaceOf(InstrumentKind.Bowls), Is.EqualTo(InstrumentPlace.Display), "back on the sill");
            Assert.IsTrue(Stub(InstrumentKind.Bowls).Stowed);
            Assert.That(_moves.Exists(e => e.Kind == InstrumentKind.Bowls && e.Phase == MovePhase.Displayed), "Displayed at the end of the put-away");
            Assert.IsFalse(Layout.PutAway(InstrumentKind.Piano), "the last member stays");
            Assert.IsFalse(Layout.PutAway(InstrumentKind.Bowls), "not a member");
        }

        [UnityTest]
        public IEnumerator TheSetIsPersistedAndRestored()
        {
            yield return Start(st => st.SetString(AppSettings.KeySet, "drums,handpan,piano"));
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Handpan }), "restored, in planner order");
            Assert.That(Layout.PlaceOf(InstrumentKind.Handpan), Is.EqualTo(InstrumentPlace.Seat));
            Assert.That(Layout.PlaceOf(InstrumentKind.Harp), Is.EqualTo(InstrumentPlace.Hidden));
            yield return TearDownApp();
            // unknown ids are dropped; a set that no longer fits is re-planned from its first member (the marimba fits beside
            // neither the piano nor the kit, so it stays on display)
            yield return Start(st => st.SetString(AppSettings.KeySet, "piano,organ,marimba,drums"));
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums }));
            Assert.That(Layout.PlaceOf(InstrumentKind.Marimba), Is.EqualTo(InstrumentPlace.Display));
        }

        [UnityTest]
        public IEnumerator TheFullRoomsArrangementMergesOnSave()
        {
            // An older save holds where the player put the handpan when it was in the set.
            yield return Start(st => st.SetString(AppSettings.ArrangeKey(LayoutMode.All, InstrumentKind.Piano), "a1;Handpan:-0.52,0,0.02,-88"));
            Assert.That(Layout.PlaceOf(InstrumentKind.Handpan), Is.EqualTo(InstrumentPlace.Display));
            // Since 2026-11-02 the saved placement is per-instrument comfort offsets (ComfortCodec), and an old seat-frame entry
            // is migrated only for an instrument at the seat (its default pose is known there): the handpan, on display,
            // has no default pose to measure from, so its old entry is dropped and it will open at its default. The old key
            // is deleted either way. Instruments at their default poses store nothing.
            Assert.That(App.Settings.GetArrangement(LayoutMode.All, InstrumentKind.Piano), Is.EqualTo(""), "the old placement was read and deleted");
            Assert.IsFalse(Layout.TryGetOffset(RigId.Handpan, out _), "an old pose of an instrument on display is not restored");
            Layout.BeginArrange();
            yield return Frame();
            Layout.EndArrange();
            var saved = new Dictionary<RigId, ComfortOffset>();
            ComfortCodec.Decode(App.Settings.GetComfort(LayoutMode.All, InstrumentKind.Piano), saved);
            foreach (RigId id in new[] { RigId.Piano, RigId.Drums, RigId.Harp, RigId.Table }) Assert.IsFalse(saved.ContainsKey(id), id + " at its default: no offset");
            // A tweak of an instrument that then goes on display is kept for when it comes back (the merge on save).
            Assert.IsTrue(Layout.Bring(InstrumentKind.Handpan, BringReason.Menu));
            yield return Settled();
            Layout.BeginArrange();
            Transform hp = Stub(InstrumentKind.Handpan).transform;
            hp.position += hp.right * 0.05f;
            Layout.NoteMovedByPlayer(RigId.Handpan);   // as a drag's drop does (only a deliberate move is saved)
            Layout.EndArrange();
            Assert.IsTrue(Layout.TryGetOffset(RigId.Handpan, out ComfortOffset tweak), "the handpan's tweak is kept");
            Assert.IsTrue(Layout.PutAway(InstrumentKind.Handpan));
            yield return Settled();
            Assert.IsTrue(Layout.TryGetOffset(RigId.Handpan, out ComfortOffset kept), "kept while it is on display");
            Assert.That(kept.Side, Is.EqualTo(tweak.Side).Within(1e-3f));
            Assert.That(Layout.Placement.Overlaps2D(), Is.EqualTo(0));
        }
    }
}
