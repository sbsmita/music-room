// Arranging (place, place around the piano, arrange around the player, fit the seat) and the IPlacementService
// contract. Every placement is about the player's seat at that moment (the head on the floor and its heading) and ends
// in the resolver. fitSeat carries the fix of spec 07 section 8: the dry run clears the eyes (walls off) before the room
// shift is measured, so a seat too near a wall moves back instead of an instrument jumping sideways.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Layout
{
    public sealed partial class LayoutModule
    {
        /// <summary>Lays the current layout out around the player's seat. In the
        /// VR room while presenting the seat first moves off a wall (fitSeat, only when the piano moves);
        /// in mixed reality neither fitSeat nor the room box. <paramref name="movePiano"/> = false
        /// leaves the piano where it is; <paramref name="force"/> is only reported in ArrangedEvent.</summary>
        public void ArrangeAroundPlayer(bool force, bool movePiano = true) => ArrangeAt(force, movePiano, SeatSource.Head);

        /// <summary>What an arrangement lays the room out about: the head's pose now (the first presenting frame, "Play here",
        /// mixed reality on or off, Reset view by the menu or both hands), the tracking origin (a recentre), or the player's
        /// front kept from the last of those (an instrument chosen, brought or staged: <see cref="ReadFront"/>).</summary>
        enum SeatSource { Head, Origin, Front }

        /// <summary>ArrangeAroundPlayer about <paramref name="source"/>'s seat. Head and Origin set the player's front.</summary>
        void ArrangeAt(bool force, bool movePiano, SeatSource source)
        {
            if (_app == null || _app.Head == null) return;
            FinishMotion();
            if (_held != null) EndGrab();
            if (MrActive) PlanForRoom();
            ApplyMembership();
            CollectAll();
            if (_pianoHomeHeight && movePiano)
            {
                // MR auto-arrange / RestoreHomes: entering or leaving mixed reality brings the piano back to its stand height.
                Rig p = Get(RigId.Piano);
                if (p != null && p.Group != null) { Vector3 pp = p.Group.position; p.Group.position = new Vector3(pp.x, p.HomeAll.y, pp.z); }
                _pianoHomeHeight = false;
            }
            ReadSeatOf(source, out Vector3 head, out float yaw);
            LastSeatShift = Vector3.zero;
            if (movePiano && !MrActive && Presenting && FitSeat(head, yaw)) ReadSeatOf(source, out head, out yaw);
            if (source != SeatSource.Front) KeepFront(head, yaw);   // the player's front from now on
            PlaceAt(head, yaw, movePiano, settle: true, pianoAtHome: _forcePianoHome);   // Reset: the piano at its home height too
            RefreshDisplay();
            for (int i = 0; i < _homeY.Length; i++) _homeY[i] = float.NaN;   // grab.homeY.clear()
            Sweep.ResetMotion();
            FirstArrangeDone = true;
            _app.Input.ResetMotion();
            _app.Events.Publish(new ArrangedEvent { Forced = force });
        }

        void ReadSeatOf(SeatSource source, out Vector3 head, out float yaw)
        {
            if (source == SeatSource.Origin) ReadOrigin(out head, out yaw);
            else if (source == SeatSource.Front) ReadFront(out head, out yaw);
            else ReadSeat(out head, out yaw);
        }

        // ---- the player's front (2026-11-17)
        // Headset test: "when instrument changed or presses loop and changed the instruments appear here and there. it appears
        // within one foot but sometimes left or right. make it appear at once place in front of eyes only". Every bring read
        // the head's pose at that moment (ReadSeat): the player was looking at the menu, the deck or Sunny and leaning toward
        // them, so the instrument landed along that look, up to a foot to the side. Now the seat a recentre or Reset view
        // lays the room out about is kept as the player's front, in the tracking space (a swivel, fitSeat or anything else
        // that moves the rig carries it along), and every instrument chosen, brought or staged is placed about it.

        /// <summary>The player's front: the seat's head point and heading in the tracking space (the world when there is none).</summary>
        Vector3 _frontLocal;
        float _frontYawLocal;
        bool _hasFront;

        /// <summary>A head this far (m, on the floor) from the front's seat point has walked away: the next bring takes the head's
        /// pose as the new front (leaning to the deck, the menu or an instrument stays well inside it).</summary>
        public const float FrontWalk = 0.5f;

        /// <summary>Brings that found the player walked away and took a new front (tests, the log).</summary>
        public int FrontReseats { get; private set; }

        Transform FrontSpace => _app?.Rig != null ? (_app.Rig.TrackingSpace != null ? _app.Rig.TrackingSpace : _app.Rig.RigRoot) : null;

        /// <summary>From now on the player's front is this seat (head point, heading in radians).</summary>
        void KeepFront(Vector3 head, float yaw)
        {
            Transform space = FrontSpace;
            if (space == null) { _frontLocal = head; _frontYawLocal = yaw; }
            else
            {
                _frontLocal = space.InverseTransformPoint(head);
                _frontYawLocal = LayoutMath.Wrap(yaw - LayoutMath.YawOf(space));
            }
            _hasFront = true;
        }

        /// <summary>The player's front in the world now (the eyes' height is the head's).</summary>
        public SeatFrame Front
        {
            get
            {
                if (_app?.Head == null) return Seat;
                FrontWorld(out Vector3 p, out float yaw);
                return new SeatFrame { Head = new Vector3(p.x, _app.Head.position.y, p.z), YawDeg = yaw * Mathf.Rad2Deg };
            }
        }

        void FrontWorld(out Vector3 p, out float yaw)
        {
            Transform space = FrontSpace;
            if (!_hasFront) { p = _seatHead; yaw = _seatYaw; return; }
            if (space == null) { p = _frontLocal; yaw = _frontYawLocal; return; }
            p = space.TransformPoint(_frontLocal);
            yaw = LayoutMath.Wrap(_frontYawLocal + LayoutMath.YawOf(space));
        }

        /// <summary>The seat an instrument is chosen, brought or staged about: the player's front (the last recentre or Reset
        /// view), never where the head looks or leans at that moment; the eyes' height is the player's settled eye level. A player who walked
        /// FrontWalk or more away from it gets the head's pose now as the new front. Off the headset (the desktop) the head now.</summary>
        void ReadFront(out Vector3 head, out float yaw)
        {
            if (!_hasFront || !Presenting || _app?.Head == null)
            {
                ReadSeat(out head, out yaw);
                return;
            }
            FrontWorld(out Vector3 p, out yaw);
            Vector3 h = _app.Head.position;
            float dx = h.x - p.x, dz = h.z - p.z;
            if (dx * dx + dz * dz > FrontWalk * FrontWalk && HeadTracked)
            {
                ReadSeat(out head, out yaw);
                KeepFront(head, yaw);
                FrontReseats++;
                Debug.Log($"[Layout] front: the player is {Mathf.Sqrt(dx * dx + dz * dz):F2} m from their seat: the new front is where they stand and face now");
                return;
            }
            // The eyes at the player's settled level (LayoutModule.Height), not a lean toward the deck at this moment: the eye
            // clearance then keeps an instrument as far out every time.
            float eyes = float.IsNaN(_eyeEma) ? h.y : FloorY + _eyeEma;
            head = new Vector3(p.x, eyes, p.z);
        }

        /// <summary>The seat now: the head position and its heading on the floor (the heading is kept when
        /// the head looks straight up or down).</summary>
        void ReadSeat(out Vector3 head, out float yaw)
        {
            Transform h = _app.Head;
            head = h.position;
            Vector3 f = h.forward;
            f.y = 0f;
            yaw = f.sqrMagnitude < 1e-8f ? _seatYaw : Mathf.Atan2(f.x, f.z);
        }

        /// <summary>The seat a recentre lays the room out about: the tracking space's origin on the floor (where the system put
        /// the player's front when it recentred) and its heading; the eyes' height is the head's. Independent of where the
        /// head looks or leans now.</summary>
        void ReadOrigin(out Vector3 head, out float yaw)
        {
            Transform space = _app.Rig != null ? (_app.Rig.TrackingSpace != null ? _app.Rig.TrackingSpace : _app.Rig.RigRoot) : null;
            if (space == null) { ReadSeat(out head, out yaw); return; }
            Vector3 o = space.position;
            head = new Vector3(o.x, _app.Head.position.y, o.z);
            Vector3 f = space.forward;
            f.y = 0f;
            yaw = f.sqrMagnitude < 1e-8f ? _seatYaw : Mathf.Atan2(f.x, f.z);
        }

        /// <summary>Every rig of the layout in front of the player: at its default pose about the seat (head and heading
        /// now) with the player's comfort offset, on the floor (the real one in mixed reality), then settled. Nothing keeps
        /// a height or a place from before: a piano lifted by an in-play grab comes back at its default height plus its
        /// comfort offset. <paramref name="pianoAtHome"/> is kept for the callers (the home height is the default now).</summary>
        void PlaceAt(Vector3 head, float yaw, bool movePiano, bool settle, bool pianoAtHome = false)
        {
            _seatHead = head;
            _seatYaw = yaw;
            // Each rig at its default pose (the seat planner's: a member of My set at its canonical slot exactly at its home,
            // another at its slot's pose, the side table at its home or, when the set needs it, its alternate; One alone at
            // the single home) moved by the player's comfort offset of this layout, in mixed reality too.
            // Instruments out of the set are not posed here (they stand on display: RefreshDisplay).
            if (_mode == LayoutMode.All)
            {
                BeginSavedCheck();
                for (int i = 0; i < _rigs.Count; i++)
                {
                    Rig r = _rigs[i];
                    if (IsRowRig(r.Id) && (!IsInScene(KindOf(r.Id)) || GlideOf(KindOf(r.Id)) != null)) continue;
                    PlanHome(r, out Vector3 home, out float homeYaw, out bool alt);
                    PullIntoReach(r, ref home, homeYaw);
                    if (r.Id == RigId.Piano)
                    {
                        if (!movePiano) continue;
                        if (TrySavedChecked(r, out SeatPose sp)) PoseSaved(r, sp, head, yaw);
                        else PoseRig(r, home, homeYaw, head, yaw, keepY: false);
                    }
                    else if (!alt && TrySavedChecked(r, out SeatPose sp)) PoseSaved(r, sp, head, yaw);
                    else PoseRig(r, home, homeYaw, head, yaw, keepY: false);
                }
                _tableAtAlternate = _plan != null && _plan.Fits && _plan.TableAlt;
            }
            else
            {
                _tableAtAlternate = false;
                Rig cur = Get(RigOf(_current));
                if (cur != null && (_current != InstrumentKind.Piano || movePiano))
                {
                    SingleHome(cur, out Vector3 single);
                    if (TrySaved(cur, out SeatPose sp)) PoseSaved(cur, sp, head, yaw);
                    else PoseRig(cur, single, cur.HomeYawSingle, head, yaw, keepY: false);
                }
                // The side table and the menu keep their homes about the seat (the rig homes minus kit and harp).
                for (int i = 0; i < _rigs.Count; i++)
                {
                    Rig r = _rigs[i];
                    if (IsRowRig(r.Id)) continue;
                    if (TrySaved(r, out SeatPose sp)) PoseSaved(r, sp, head, yaw);
                    else PoseRig(r, r.HomeAll, r.HomeYawAll, head, yaw, keepY: false);
                }
            }
            // Placing shows every rig-home group, the menu panel's too: a menu hidden only for placing is back.
            ShowMenuAfterPlacing();
            if (settle) { Settle(head, yaw); ReportReach(); }
        }

        /// <summary>The side table stands at its alternate pose (the set needs it).</summary>
        bool _tableAtAlternate;

        /// <summary>Where the plan puts a rig of My set (seat frame): a member at its canonical slot with no push stands exactly
        /// at its Rig.HomeAll (today's homes, bit for bit); another at its slot's pose (its height its home's); the side table
        /// at its home, or its alternate (<paramref name="alt"/>) when the plan needs it; any other rig at its home.</summary>
        void PlanHome(Rig r, out Vector3 home, out float homeYaw, out bool alt)
        {
            home = r.HomeAll;
            homeYaw = r.HomeYawAll;
            alt = false;
            if (_mode != LayoutMode.All || _plan == null || !_plan.Fits) return;
            if (_plan.Single)
            {
                // a set of one: the instrument alone at its Single home (today's One alone), the table at its home
                if (IsRowRig(r.Id) && _plan.TryGet(KindOf(r.Id), out _))
                {
                    home = r.HomeSingle;
                    homeYaw = r.HomeYawSingle;
                    CentreOnFrontLine(r, ref home, homeYaw);   // alone in front: its play centre on the player's forward line
                }
                return;
            }
            if (r.Id == RigId.Table)
            {
                if (!_plan.TableAlt) return;
                alt = true;
                home = new Vector3((float)_plan.Table.X, r.HomeAll.y, (float)_plan.Table.Z);
                homeYaw = (float)_plan.Table.Yaw;
                return;
            }
            if (!IsRowRig(r.Id) || !_plan.TryGet(KindOf(r.Id), out SeatPlanner.Member m) || m.AtHome) return;
            home = new Vector3((float)m.Pose.X, r.HomeAll.y, (float)m.Pose.Z);
            homeYaw = (float)m.Pose.Yaw;
        }

        /// <summary>Mixed reality's fixed draws (platform.md 9.6: room, table, menu, hands ...) and its budget.</summary>
        public const int MrFixedDraws = 37, MrDrawBudget = 80;

        /// <summary>Mixed reality (platform.md 9.5, 9.6): the set re-planned without the rear slots (no swivel there); a member
        /// that then has no room is put away for this visit, and so is the last member while the set's DrawsAtSeat + 37 would
        /// pass 80.</summary>
        void PlanForRoom()
        {
            if (_mode != LayoutMode.All || _planner == null) return;
            SeatPlanner.Plan p = _planner.PlanMixedReality(_set);
            var gone = new List<InstrumentKind>(p.PutAway);
            var keep = new List<InstrumentKind>(_set);
            for (int i = 0; i < gone.Count; i++) keep.Remove(gone[i]);
            while (keep.Count > 1 && DrawsAtSeat(keep) + MrFixedDraws > MrDrawBudget)
            {
                gone.Add(keep[keep.Count - 1]);
                keep.RemoveAt(keep.Count - 1);
                p = _planner.PlanMixedReality(keep);
            }
            if (gone.Count == 0) { if (p.Fits) _plan = p; return; }
            for (int i = 0; i < gone.Count; i++)
            {
                _set.Remove(gone[i]);
                Debug.Log($"[Layout] put away: no room here ({gone[i]})");
            }
            SetVersion++;
            PersistSet();
            _plan = p;
            PublishLayout();
        }

        /// <summary>The menu was open when the MR placing pinch hid its group (the groups are hidden, the panel
        /// stays shown); every placement that shows the rigs again (PlaceAroundPiano, PlaceAt, RestoreHomes) shows it too.</summary>
        void ShowMenuAfterPlacing()
        {
            if (!_menuHiddenForPlacing) return;
            _menuHiddenForPlacing = false;
            IMenuPanel menu = Menu;
            Rig menuRig = Get(RigId.Lessons);
            Transform g = menuRig != null && menuRig.Group != null ? menuRig.Group : menu?.Root;
            if (menu != null && menu.Shown && g != null && !g.gameObject.activeSelf) g.gameObject.SetActive(true);
        }

        void PoseRig(Rig r, Vector3 home, float homeYawDeg, Vector3 head, float yaw, bool keepY)
        {
            Transform g = r.Group;
            if (g == null) return;
            Vector3 p = LayoutMath.SeatPlace(head, yaw, home);
            if (keepY) p.y = g.position.y;
            else if (r.Id != RigId.Lessons) p.y = DefaultY(r, p.y);   // on the real floor in mixed reality, at the player's level
            g.SetPositionAndRotation(p, Quaternion.Euler(0f, homeYawDeg + yaw * Mathf.Rad2Deg, 0f));
            // Placing shows every posed rig (mixed reality hides them until arranged); the menu shows itself.
            if (r.Id != RigId.Lessons && r.InScene && !g.gameObject.activeSelf) g.gameObject.SetActive(true);
        }

        /// <summary>The resolver (<paramref name="pinned"/> keeps its spot, <paramref name="skip"/>
        /// is left out), then the zones brought up to date.</summary>
        void Settle(Vector3 head, float yaw, RigEntry pinned = null, RigEntry skip = null)
        {
            SyncPlacementFlags();
            RigEntry table = Placement.Entry(RigId.Table);
            bool guard = table != null && table != pinned && table != skip && PlacementSolver.InScene(table);
            Vector3 tableFrom = guard ? table.Group.position : Vector3.zero;
            Placement.Resolve(head, yaw, pinned, skip);
            if (guard) KeepTableBeside(table, tableFrom, head, yaw, skip);
            RefreshZones();
        }

        // ---- the deck stays beside the player (2026-11-17, TUNE-DECK-FRONT)
        // Headset: "the recorder is appearing behind me even if i am seeing straight in front ... im also unable to hands
        // properly on tape deck". The side table (the tape deck) stands at the left hip by design, its group 21 cm behind the
        // eyes. When the instrument in front touched it (the kit turned a little to the left, a comfort offset, a riser), the
        // resolver's first try is "away from what it touches", which for the table is straight BACK: 11-17 cm more behind
        // the head (seat z -0.32 .. -0.38, DeckFrontTests), out of sight and out of reach of the left hand. Now a table the
        // resolver pushed back is moved off the instrument to the LEFT (or left and forward) instead, the smallest such move.

        /// <summary>How far (m) the resolver may move the side table back toward behind the player before it is moved
        /// sideways instead.</summary>
        public const float TableBackSlack = 0.03f;

        /// <summary>Tables the resolver had pushed behind the player that were moved to the left instead (tests, the log).</summary>
        public int TableKeptBeside { get; private set; }

        readonly List<RigEntry> _tableOthers = new List<RigEntry>(8);
        static readonly Vector2[] s_tableDirs = { new Vector2(-1f, 0f), new Vector2(-0.894f, 0.447f), new Vector2(-0.707f, 0.707f), new Vector2(-0.447f, 0.894f) };

        void KeepTableBeside(RigEntry t, Vector3 from, Vector3 head, float yaw, RigEntry skip)
        {
            if (!PlacementSolver.Active(t)) return;
            float fx = Mathf.Sin(yaw), fz = Mathf.Cos(yaw);
            Vector3 now = t.Group.position;
            float back = (from.x - now.x) * fx + (from.z - now.z) * fz;   // > 0: moved toward behind the player
            if (back <= TableBackSlack) return;
            _tableOthers.Clear();
            for (int i = 0; i < Placement.Entries.Count; i++)
            {
                RigEntry e = Placement.Entries[i];
                if (e != t && e != skip && PlacementSolver.Active(e)) _tableOthers.Add(e);
            }
            // Seat frame: right = (fz, -fx), forward = (fx, fz). The smallest step first, straight left first.
            for (float s = PlacementSolver.Step; s <= 0.6f + 1e-4f; s += PlacementSolver.Step)
            {
                for (int k = 0; k < s_tableDirs.Length; k++)
                {
                    Vector2 d = s_tableDirs[k];
                    float wx = d.x * fz + d.y * fx, wz = -d.x * fx + d.y * fz;
                    t.Group.position = new Vector3(from.x + wx * s, from.y, from.z + wz * s);
                    if (!Placement.Free(t, _tableOthers, head, PlacementSolver.Gap, true)) continue;
                    TableKeptBeside++;
                    Debug.Log($"[Layout] side table: the resolver pushed it {back:F2} m back behind the player; moved {s:F2} m to the left instead");
                    return;
                }
            }
            t.Group.position = now;   // nowhere free at the left: the resolver's spot
            Placement.Pose(t);
            Debug.Log($"[Layout] side table: the resolver pushed it {back:F2} m back behind the player and nothing at the left is free");
        }

        /// <summary>The resolver's view of the world this frame: walls only in the VR room; the menu panel as it is.</summary>
        void SyncPlacementFlags()
        {
            Placement.InRoom = !MrActive;
            Rig menuRig = Get(RigId.Lessons);
            IMenuPanel menu = Menu;
            Placement.Panel.Group = menuRig != null ? menuRig.Group : menu?.Root;
            Placement.Panel.Shown = menu != null && menu.Shown && Placement.Panel.Group != null;
        }

        /// <summary>fitSeat with the fix (spec 07 section 8): dry-run the layout with no settle, clear the eyes of each placed
        /// rig in priority order with the walls off, then move the seat by the room shift of that arrangement.</summary>
        bool FitSeat(Vector3 head, float yaw)
        {
            PlaceAt(head, yaw, true, settle: false);
            if (!Placement.FitShift(head, yaw, out Vector2 s)) return false;
            XrMode.MoveRig(_app.Rig, new Vector3(s.x, 0f, s.y));
            LastSeatShift = new Vector3(s.x, 0f, s.y);
            _app.Input.ResetMotion();
            Debug.Log($"[Layout] fitSeat: the seat moved by ({s.x:F3}, {s.y:F3}) to keep the {_mode} layout off the walls");
            return true;
        }

        /// <summary>The last seat move fitSeat made (zero when none was needed).</summary>
        public Vector3 LastSeatShift { get; private set; }

        /// <summary>Mixed reality, the piano just placed on a real surface; the placed rigs take
        /// their VR-room positions relative to it, shown again (the menu too if it was open when the placing pinch hid it),
        /// and settle with the piano pinned (the settle clears the panel out of the instruments). The MR module calls it
        /// when the piano lands (ARCHITECTURE.md section 12, step 2).</summary>
        public void PlaceAroundPiano() => PlaceAroundLead();

        /// <summary>platform.md 9.2 (the MR flow, generalising PlaceAroundPiano): the lead instrument was just placed
        /// on a real surface; every other rig of the layout takes its planner offset from the lead (the lead's planned pose to
        /// its own, turned by how the lead was turned), shown again, and the layout settles with the lead pinned. For the
        /// piano lead of the default set these are exactly the home offsets (the planned poses are the homes).</summary>
        public void PlaceAroundLead()
        {
            InstrumentKind leadKind = Lead;
            Rig lead = Get(RigOf(leadKind));
            if (lead == null || lead.Group == null) return;
            LeaveStage("placed in the room");
            Transform pg = lead.Group;
            PlanHome(lead, out Vector3 leadHome, out float leadHomeYaw, out _);
            if (_mode == LayoutMode.Single) { leadHome = lead.HomeSingle; leadHomeYaw = lead.HomeYawSingle; }
            float yaw = LayoutMath.YawOf(pg) - leadHomeYaw * Mathf.Deg2Rad;
            Vector3 pp = pg.position;
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig r = _rigs[i];
                if (r == lead || r.Group == null || !r.InScene) continue;
                // Every rig of the layout (the menu panel's home too) at its VR-room offset from the lead.
                PlanHome(r, out Vector3 home, out float homeYaw, out _);
                float dx = home.x - leadHome.x, dz = home.z - leadHome.z;
                LayoutMath.RotY(dx, dz, yaw, out float rx, out float rz);
                r.Group.SetPositionAndRotation(new Vector3(pp.x + rx, home.y, pp.z + rz), Quaternion.Euler(0f, homeYaw + yaw * Mathf.Rad2Deg, 0f));
                if (r.Id != RigId.Lessons && !r.Group.gameObject.activeSelf) r.Group.gameObject.SetActive(true);
            }
            ShowMenuAfterPlacing();
            CollectAll();
            ReadSeat(out Vector3 head, out float hy);
            Settle(head, hy, Placement.Entry(lead));
            _app.Input.ResetMotion();   // placed: the rigs just jumped under the hands
        }

        /// <summary>Back in the VR room after mixed reality, the piano at its home about the
        /// default seat (0, 1.2, 0) facing +z, then the layout placed about that seat (in the full room the piano stays
        /// where it was just put; in single mode the current instrument moves too) and settled with the VR room's walls.
        /// The MR module calls ArrangeAroundPlayer(true) right after (ARCHITECTURE.md section 12, step 4).</summary>
        public void RestoreHomes()
        {
            if (_app == null) return;
            LeaveStage("left mixed reality");
            FinishMotion();
            if (_held != null) EndGrab();
            if (_mode == LayoutMode.All && _planner != null) _plan = _planner.PlanSet(_set);   // the VR room's plan again
            ApplyMembership();
            Rig piano = Get(RigId.Piano);
            if (piano != null && piano.Group != null)
                piano.Group.SetPositionAndRotation(LayoutMath.SeatPlace(OriginSeat, 0f, piano.HomeAll), Quaternion.Euler(0f, piano.HomeYawAll, 0f));
            _pianoHomeHeight = false;   // done here: the stand height is the home height
            CollectAll();
            PlaceAt(OriginSeat, 0f, _mode == LayoutMode.Single, settle: true);
            RefreshDisplay();
            for (int i = 0; i < _homeY.Length; i++) _homeY[i] = float.NaN;
            _app.Input.ResetMotion();
        }

        /// <summary>The group everything is arranged around: the lead instrument (platform.md 9.2: My set's
        /// member in slot Front, else its first in carousel order; the piano of the default set) or the current instrument
        /// alone (MR poses its shadow catcher and light there). Identity when that rig is not loaded.</summary>
        public Pose Anchor
        {
            get
            {
                Rig r = Get(RigOf(Lead));
                return r != null && r.Group != null ? new Pose(r.Group.position, r.Group.rotation) : Pose.identity;
            }
        }

        // ---------------------------------------------------------------- parts, centres, zones

        void CollectAll()
        {
            for (int i = 0; i < Placement.Entries.Count; i++) Collect(Placement.Entries[i]);
        }

        /// <summary>A rig's renderers as the placement code needs them: grab handles (Rig.Handles, filled once by the rig's
        /// module), the footprint (handles plus the played parts of its instruments, not every visible mesh
        /// of a rig), every renderer under the group for its zone, and the play centre. Only renderers under the rig's
        /// group count (they move with it). The PartRef lookups run here, on placement events, never per frame.</summary>
        void Collect(RigEntry e)
        {
            e.Footprint.Clear();
            e.Handles.Clear();
            e.ZoneParts.Clear();
            e.PlayRects.Clear();
            Transform g = e.Group;
            if (g == null) return;
            IReadOnlyList<Renderer> handles = e.Rig.Handles;
            if (handles != null)
                for (int i = 0; i < handles.Count; i++)
                {
                    Renderer r = handles[i];
                    if (r == null || !r.transform.IsChildOf(g)) continue;
                    var part = new PartRef(r);
                    e.Handles.Add(part);
                    AddUnique(e.Footprint, part);
                }
            List<IInstrument> insts = e.Rig.Instruments;
            for (int k = 0; k < insts.Count; k++)
            {
                IReadOnlyList<Renderer> played = insts[k]?.PlayedParts;
                if (played == null) continue;
                for (int i = 0; i < played.Count; i++)
                {
                    Renderer r = played[i];
                    if (r == null || !r.transform.IsChildOf(g)) continue;
                    var part = new PartRef(r);
                    AddUnique(e.Footprint, part);
                    if (PlacementSolver.GeometryBox(part, out Bounds b, out Matrix4x4 tw)) e.PlayRects.Add(LocalFloorRect(g.worldToLocalMatrix * tw, b));
                }
            }
            g.GetComponentsInChildren(true, _rendererScratch);
            for (int i = 0; i < _rendererScratch.Count; i++) e.ZoneParts.Add(new PartRef(_rendererScratch[i]));
            e.HasCentre = TryCentre(e, out e.CentreLocal);
            e.RiserReady = false;   // its riser's rectangle is worked out again when it is next needed
        }

        /// <summary>A box's floor rectangle (x / z extent of its corners) in the frame <paramref name="toLocal"/> maps into.</summary>
        static LocalRect LocalFloorRect(Matrix4x4 toLocal, Bounds b)
        {
            Vector3 c = b.center, x = b.extents;
            float x0 = float.PositiveInfinity, x1 = float.NegativeInfinity, z0 = float.PositiveInfinity, z1 = float.NegativeInfinity;
            for (int k = 0; k < 8; k++)
            {
                Vector3 q = toLocal.MultiplyPoint3x4(new Vector3(c.x + ((k & 1) != 0 ? x.x : -x.x), c.y + ((k & 2) != 0 ? x.y : -x.y), c.z + ((k & 4) != 0 ? x.z : -x.z)));
                x0 = Mathf.Min(x0, q.x); x1 = Mathf.Max(x1, q.x); z0 = Mathf.Min(z0, q.z); z1 = Mathf.Max(z1, q.z);
            }
            return new LocalRect((x0 + x1) * 0.5f, (z0 + z1) * 0.5f, (x1 - x0) * 0.5f, (z1 - z0) * 0.5f);
        }

        static void AddUnique(List<PartRef> list, PartRef p)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].R == p.R) return;
            list.Add(p);
        }

        /// <summary>The placement rigs' play centres. The deck on the side table. The three instruments
        /// with their own play-centre API keep that measure bit for bit (the descriptors' PlayCentre is the planner's reach
        /// centre, not this): the piano's mid key depth, the kit's mean piece position (pad yes, kick shell no), the middle
        /// harp string's midpoint, each found by its API's root being under the rig's group (no identity switch). Every
        /// other instrument: its descriptor's Seat.PlayCentre (rig-local xz).</summary>
        bool TryCentre(RigEntry e, out Vector3 local)
        {
            local = Vector3.zero;
            Transform g = e.Group;
            if (e.Rig.Id == RigId.Table) { local = TableCentreLocal; return true; }
            if (g == null) return false;
            IPianoApi p = PianoApi;
            if (p != null && Under(p.Root, g))
            {
                float l = p.L > 0f ? p.L : 0.15f;
                local = g.InverseTransformPoint(p.Root.TransformPoint(new Vector3(0f, 0f, l / 2f)));   // source frame (0, 0, -L/2)
                return true;
            }
            IDrumsApi d = DrumsApi;
            if (d != null && Under(d.Root, g))
            {
                if (d.Pieces == null) return false;
                Vector3 sum = Vector3.zero;
                int n = 0;
                IReadOnlyList<DrumPieceInfo> pieces = d.Pieces;
                for (int i = 0; i < pieces.Count; i++)
                {
                    DrumPieceInfo pc = pieces[i];
                    if (pc.Transform == null || !(pc.Pad || pc.Kind != "kick")) continue;
                    sum += g.InverseTransformPoint(pc.Transform.position);
                    n++;
                }
                if (n == 0) return false;
                local = sum / n;
                return true;
            }
            IHarpApi h = HarpApi;
            if (h != null && Under(h.Root, g))
            {
                if (h.StringCount <= 0) return false;
                int mid = h.StringCount / 2;
                Vector3 m = (h.StringStart(mid) + h.StringEnd(mid)) * 0.5f;
                local = g.InverseTransformPoint(h.Root.TransformPoint(m));
                return true;
            }
            InstrumentDescriptor desc = _app?.Registry?.Get(KindOf(e.Rig.Id));
            if (desc == null || !desc.Playable || desc.Seat == null) return false;
            local = new Vector3(desc.Seat.PlayCentre.x, 0f, desc.Seat.PlayCentre.y);
            return true;
        }

        int DrawsAtSeat(List<InstrumentKind> set)
        {
            int n = 0;
            for (int i = 0; i < set.Count; i++) n += _app.Registry?.Get(set[i])?.DrawsAtSeat ?? 0;
            return n;
        }

        static bool Under(Transform t, Transform group) => t != null && group != null && (t == group || t.IsChildOf(group));

        void RefreshZones()
        {
            for (int i = 0; i < Placement.Entries.Count; i++) RefreshZone(Placement.Entries[i]);
        }

        /// <summary>The zone (ZONE_PAD): the world box round every renderer of
        /// the rig (each mesh's geometry box carried to the world) grown by 12 cm on every side.</summary>
        void RefreshZone(RigEntry e)
        {
            e.HasZone = false;
            if (!PlacementSolver.InScene(e)) return;
            bool any = false;
            Vector3 mn = Vector3.zero, mx = Vector3.zero;
            for (int i = 0; i < e.ZoneParts.Count; i++)
            {
                PartRef p = e.ZoneParts[i];
                if (p.R == null || !p.R.enabled || !p.R.gameObject.activeInHierarchy) continue;
                if (!PlacementSolver.GeometryBox(p, out Bounds b, out Matrix4x4 tw)) continue;
                Vector3 c = b.center, x = b.extents;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 q = tw.MultiplyPoint3x4(new Vector3(c.x + ((k & 1) != 0 ? x.x : -x.x), c.y + ((k & 2) != 0 ? x.y : -x.y), c.z + ((k & 4) != 0 ? x.z : -x.z)));
                    if (!any) { mn = mx = q; any = true; }
                    else { mn = Vector3.Min(mn, q); mx = Vector3.Max(mx, q); }
                }
            }
            if (!any) return;
            Vector3 pad = Vector3.one * GrabConstants.ZonePad;
            e.Zone.SetMinMax(mn - pad, mx + pad);
            e.HasZone = true;
        }

        /// <summary>A rig's zone as the grab and gesture code use it (null when it has none).</summary>
        public Bounds? ZoneOf(Rig rig)
        {
            if (rig == null) return null;
            if (rig.Id == RigId.Lessons) return Menu?.GrabZone;
            RigEntry e = Placement.Entry(rig);
            return e != null && e.HasZone ? e.Zone : (Bounds?)null;
        }

        /// <summary>Inside any placed rig's zone, its module's play zone, or the shown menu's
        /// grab zone. A pinch there never swivels the seat or counts toward the two-hand summon.</summary>
        public bool NearInstrument(Vector3 p)
        {
            for (int i = 0; i < Placement.Entries.Count; i++)
            {
                RigEntry e = Placement.Entries[i];
                if (!PlacementSolver.InScene(e)) continue;
                if (e.HasZone && e.Zone.Contains(p)) return true;
                if (e.Rig.Zone != null && e.Rig.Zone.Contains(p)) return true;
            }
            IMenuPanel menu = Menu;
            if (menu != null && menu.Shown)
            {
                Bounds? z = menu.GrabZone;
                if (z.HasValue && z.Value.Contains(p)) return true;
            }
            return false;
        }

        /// <summary>The ray summon's zone guard (platform.md 4.4 c): inside the play zone of an instrument at the seat (its
        /// module's IPlayZone: the keys, the heads, the strings, a stub's footprint grown by the zone pad), on a play surface,
        /// or in the shown menu's grab zone. Narrower than NearInstrument (the rigs' whole bounding boxes), so a hand resting
        /// beside the piano can still point at the window.</summary>
        public bool InPlayZone(Vector3 p)
        {
            for (int i = 0; i < Placement.Entries.Count; i++)
            {
                RigEntry e = Placement.Entries[i];
                if (!PlacementSolver.InScene(e)) continue;
                if (e.Rig.Zone != null && e.Rig.Zone.Contains(p)) return true;
            }
            if (PlaySurfaceAt(p) != PlaySurface.None) return true;
            IMenuPanel menu = Menu;
            if (menu != null && menu.Shown)
            {
                Bounds? z = menu.GrabZone;
                if (z.HasValue && z.Value.Contains(p)) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- IPlacementService

        /// <summary>The resolver at the current seat; <paramref name="useRoomBox"/> = the VR room's walls apply.</summary>
        public bool Resolve(bool useRoomBox)
        {
            if (_app?.Head == null) return true;
            CollectAll();
            ReadSeat(out Vector3 head, out float yaw);
            SyncPlacementFlags();
            Placement.InRoom = useRoomBox;
            bool ok = Placement.Resolve(head, yaw);
            RefreshZones();
            return ok;
        }

        /// <summary>A rig let go inside another, in the player's face or in a wall moves the
        /// shortest way out on its own.</summary>
        public void SettleOne(Rig rig)
        {
            RigEntry e = Placement.Entry(rig);
            if (e == null || _app?.Head == null) return;
            CollectAll();
            ReadSeat(out Vector3 head, out float yaw);
            SyncPlacementFlags();
            Placement.SettleOne(e, head, yaw);
            RefreshZone(e);
        }

        /// <summary>The shown panel out of the instruments' volume (toward the player or up).</summary>
        public void ClearPanel(Rig panel)
        {
            if (_app?.Head == null) return;
            CollectAll();
            for (int i = 0; i < Placement.Entries.Count; i++) if (PlacementSolver.InScene(Placement.Entries[i])) Placement.Measure(Placement.Entries[i]);
            SyncPlacementFlags();
            if (panel != null && panel.Group != null)
            {
                // The caller names the panel: it counts as shown when its group is active (the menu may call this from its
                // own Show before its Shown flag is set).
                Placement.Panel.Group = panel.Group;
                Placement.Panel.Shown = panel.Group.gameObject.activeInHierarchy;
            }
            Placement.ClearPanel(_app.Head.position);
        }

        /// <summary>The shift that brings the current arrangement inside the VR room (null when
        /// it already fits or outside the VR room). Measures only; fitSeat applies it.</summary>
        public Vector3? RoomShift()
        {
            if (MrActive) return null;
            CollectAll();
            Placement.InRoom = true;
            return Placement.RoomShift(out Vector2 s) ? new Vector3(s.x, 0f, s.y) : (Vector3?)null;
        }

        /// <summary>Nearest distance from the eyes to any part of the rig (+inf when it has no footprint).</summary>
        public float EyeDistance(Rig rig)
        {
            RigEntry e = Placement.Entry(rig);
            if (e == null || _app?.Head == null) return float.PositiveInfinity;
            Collect(e);
            return Placement.EyeReport(e, _app.Head.position);
        }
    }
}
