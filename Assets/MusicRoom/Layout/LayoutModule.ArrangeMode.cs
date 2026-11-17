// Arrange mode: what "Move instruments" does while it is on (the setting itself, its persistence and the drag's feel are
// in LayoutModule.Move.cs). It was added after the first headset test as a one-shot mode with Done / Reset buttons; since
// 2026-11-10 it is the one way instruments move (the gold knobs and Lock instruments are gone). While it is on nothing
// plays (App.TickInstruments skips every instrument but the menu), the instruments of the layout wear soft corner brackets
// and a floor ring, and a pinch picks one up after an 80 ms debounce:
//   near  the pinch point (thumb and index tips) within 8 cm of ANY part of an instrument, keys, heads and strings too;
//   ray   from the hand's system pointer pose (OVRPlugin PointerPose, valid with InputStateValid) to the first
//         instrument it hits, which turns gold; the pinch keeps the hit distance, and pushing or pulling the hand along
//         the ray moves it farther or closer (x 2.5).
// The held instrument follows the hand in x and z through the drag filter at its grab offset, rises and falls with the
// hand's own height inside its range (2026-11-11; raised, it stands on a riser, the piano on its stretched stand), spins
// with the wrist (x 2, about its centre; two hands on it twist it 1:1), and the in-play rules still hold: no overlap (PlacementSolver.Constrain, then SettleOne on release), the 30 cm
// eye clearance (SettleOne), the floor ring. The pinch is the grab pinch (thumb-index OR Meta's own index pinch), never one
// that starts as the OS system gesture (palm toward the headset). Each release keeps, per instrument, how far it was moved
// from its default pose in front of the player (comfort offsets, AppSettings.ComfortKey, ComfortCodec); every later open
// of that layout (next launch, a recentre, a set change, a page turn, mixed reality) is in front of the player again with
// those offsets. Reset forgets them.
// In the VR room the instruments on display move too (platform.md 4.1): taken by the ray (or a near pinch), a floor
// instrument slides on the floor, a wall one along its wall, a surface one stays on its surface; every box inside the
// room, 3 cm clear of the furniture, the other displayed instruments and the seat's instruments, its play centre 1.1 m or
// more from the head; the release is saved as musicroom.stow.<id> ("x,z,yaw" in the room) and Reset brings back the
// plan's spot.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Layout
{
    public sealed partial class LayoutModule
    {
        bool _arranging;
        bool _forcePianoHome, _noSave;

        struct ArrangeHand
        {
            public bool Ignore;          // this pinch began as the OS system gesture or on the shown menu: never a grab
            public Rig Candidate;        // what the pinch takes once the debounce is over
            public bool ViaRay;
            public float RayT;           // the ray's hit distance when the pinch began
            public Vector3 RayO, RayD;   // and the ray itself (used if the pointer pose drops out during the debounce)
            public float PinchT;
            public Rig Near, RayRig;     // what the hand points at this frame (highlights)
            public float RayHitT;
        }
        readonly ArrangeHand[] _arr = new ArrangeHand[2];
        bool _rayGrab;
        float _rayD0;
        Vector3 _rayO0, _rayDir0, _rayTarget, _rayFrom;

        /// <summary>The highlights, rays and dots of arrange mode (state readable when headless).</summary>
        public ArrangeVisuals ArrangeView { get; private set; }
        public bool Arranging => _arranging;
        /// <summary>The held instrument was taken with a ray (false: a near pinch).</summary>
        public bool HeldByRay => _held != null && _rayGrab;
        public int ArrangeGrabs { get; private set; }
        public int RayGrabs { get; private set; }
        public int ArrangeSaves { get; private set; }
        public int ArrangeResets { get; private set; }
        /// <summary>A hand's arrange-mode targets this frame (tests).</summary>
        public Rig NearTarget(int hand) => _arr[hand].Near;
        public Rig RayTarget(int hand) => _arr[hand].RayRig;
        public bool PinchIgnored(int hand) => _arr[hand].Ignore;

        // ---------------------------------------------------------------- IArrangeMode

        /// <summary>"Move instruments" on (persisted); moving starts now unless the app holds it (it then starts by itself).</summary>
        public void BeginArrange()
        {
            if (_app == null) return;
            PersistMove(true);
            _startAnswered = true;   // turned on by hand: the player is in the room
            StartMoving();
        }

        /// <summary>Moving starts (the setting is on): false when it cannot now (mixed reality places the piano).</summary>
        bool StartMoving()
        {
            // Arrange mode is Move instruments On, always: nothing else ever starts it (a set change, a page turn, mixed
            // reality and the headset only suspend it, and TryResumeMove brings it back only while the setting is on).
            if (_app == null || _arranging || MrPlacing || !_moveWanted) return false;
            LeaveStage("Move instruments");   // they arrange what is in front of them (the playback stops below)
            FinishMotion();
            if (_held != null) EndGrab();
            EndDisplayDrag(save: false);
            _swivelHand = null;
            _summonT = 0f;
            Sweep.ResetMotion();
            for (int i = 0; i < _arr.Length; i++) _arr[i] = default;
            _movedByPlayer.Clear();
            _arranging = true;
            // Nothing may keep sounding: sticks go back on the snare, a lesson or a take being played back stops, and a loop
            // session on the deck ends as its STOP ends it (saved when it has something new), whether it is waiting for its
            // first note, recording or looping (the looper, merged 2026-11-01; a lesson stops it the same way).
            ReleaseAllTools();
            var tutorial = _app.Get<ITutorial>();
            if (tutorial != null && tutorial.State != TutorialState.Idle) tutorial.Stop();
            var looper = _app.Get<ILooper>();
            if (looper != null && looper.Active) looper.Press(LoopControl.Stop);
            var recorder = _app.Get<IRecorder>();
            if (recorder != null && recorder.State == RecorderState.Playing) recorder.Stop();
            Visuals.HideAll();
            CollectAll();
            Placement.MeasureAll();
            RefreshZones();
            Debug.Log($"[Layout] arrange mode on ({_mode}{(_mode == LayoutMode.Single ? " " + _current : "")})");
            _app.Events.Publish(new ArrangeModeEvent { Active = true });
            UpdateArrangeVisuals();
            return true;
        }

        /// <summary>"Move instruments" off (persisted): what is held drops, the placement is saved, the instruments play.</summary>
        public void EndArrange()
        {
            if (_app == null) return;
            PersistMove(false);
            StopMoving(save: true);
        }

        /// <summary>Moving stops, the setting unchanged (the app suspends it: the headset came off, mixed reality changed the
        /// room, an instrument is brought). <paramref name="save"/> false: the room changed under it (mixed reality on or
        /// off), so nothing is saved.</summary>
        void StopMoving(bool save)
        {
            if (_app == null || !_arranging) return;
            _noSave = !save;
            if (_held != null) EndGrab();   // settles (and saves)
            EndDisplayDrag(save);
            if (save) SaveArrangement();
            _noSave = false;
            _movedByPlayer.Clear();
            _arranging = false;
            for (int i = 0; i < _arr.Length; i++) _arr[i] = default;
            ArrangeView?.HideAll();
            _app.Input.ResetMotion();       // fingers resting in a key or on a string must not strike as play resumes
            Debug.Log("[Layout] arrange mode off");
            _app.Events.Publish(new ArrangeModeEvent { Active = false });
        }

        public void ToggleArrange()
        {
            if (_moveWanted) EndArrange();
            else BeginArrange();
        }

        public void ResetArrangement()
        {
            if (_app == null) return;
            if (_held != null) EndGrab();
            EndDisplayDrag(save: false);
            _app.Settings.SetArrangement(_mode, _current, "");
            _app.Settings.SetComfort(_mode, _current, "");
            // the instruments on display go back to the plan's spots too
            IInstrumentRegistry reg = _app.Registry;
            if (reg != null) for (int i = 0; i < reg.All.Count; i++) _app.Settings.SetStow(reg.All[i].Id, "");
            LoadSpots();
            _app.Settings.Save();
            _offsets.Clear();
            _offsetsKey = AppSettings.ComfortKey(_mode, _current);
            _usableReady = false;
            _forcePianoHome = true;
            ArrangeAroundPlayer(true);
            _forcePianoHome = false;
            ArrangeResets++;
            Debug.Log($"[Layout] arrangement reset ({_mode})");
            if (_arranging) { Placement.MeasureAll(); UpdateArrangeVisuals(); }
        }

        // ---------------------------------------------------------------- comfort offsets (the saved placement)

        readonly Dictionary<RigId, ComfortOffset> _offsets = new Dictionary<RigId, ComfortOffset>();
        readonly Dictionary<RigId, SeatPose> _legacy = new Dictionary<RigId, SeatPose>();
        string _offsetsKey;

        /// <summary>Old saved placements turned into comfort offsets since boot (tests).</summary>
        public int Migrations { get; private set; }
        /// <summary>The comfort offset of a rig in the current layout (tests, tools).</summary>
        public bool TryGetOffset(RigId id, out ComfortOffset offset)
        {
            LoadOffsets();
            return _offsets.TryGetValue(id, out offset);
        }

        /// <summary>The current layout's offsets, read once per layout key; an old seat-frame placement of that layout
        /// (ArrangementCodec, written by earlier versions) is turned into offsets the first time and then deleted: each entry
        /// becomes its offset from today's default pose, or is dropped when that lies outside the comfortable range (so an
        /// old placement can never put an instrument somewhere strange).</summary>
        void LoadOffsets()
        {
            if (_app == null) return;
            string key = AppSettings.ComfortKey(_mode, _current);
            if (key == _offsetsKey) return;
            _offsetsKey = key;
            ComfortCodec.Decode(_app.Settings.GetComfort(_mode, _current), _offsets);
            string old = _app.Settings.GetArrangement(_mode, _current);
            if (string.IsNullOrEmpty(old)) return;
            ArrangementCodec.Decode(old, _legacy);
            int kept = 0, dropped = 0;
            foreach (KeyValuePair<RigId, SeatPose> kv in _legacy)
            {
                if (_offsets.ContainsKey(kv.Key)) continue;
                Rig r = Get(kv.Key);
                if (r == null || r.Id == RigId.Lessons || !DefaultSeat(r, out SeatPose def, out Vector2 bearing, out bool main)) { dropped++; continue; }
                ComfortOffset o = ComfortCodec.Between(def, kv.Value, bearing);
                if (!ComfortCodec.Plausible(o, main)) { dropped++; continue; }
                kept++;
                if (!o.IsZero) _offsets[kv.Key] = o;
            }
            _legacy.Clear();
            _app.Settings.SetArrangement(_mode, _current, "");
            _app.Settings.SetComfort(_mode, _current, ComfortCodec.Encode(_offsets));
            _app.Settings.Save();
            Migrations++;
            Debug.Log($"[Layout] old placement of {key}: {kept} kept as comfort offsets, {dropped} dropped");
        }

        /// <summary>A rig's default pose in front of the player in the current layout (seat frame): My set's planned pose (its
        /// slot, or its home), One alone's single home, the side table and menu at their homes; the height is the home's on
        /// the floor (mixed reality: the real floor) plus the player lift (LayoutModule.Height). Also the floor direction from the head to its play centre there (the
        /// offset's frame) and whether it is the main instrument (straight ahead).</summary>
        bool DefaultSeat(Rig r, out SeatPose def, out Vector2 bearing, out bool main)
        {
            def = default;
            bearing = new Vector2(0f, 1f);
            main = false;
            if (r == null) return false;
            Vector3 home;
            float yawDeg;
            if (_mode == LayoutMode.All) { PlanHome(r, out home, out yawDeg, out _); PullIntoReach(r, ref home, yawDeg); }
            else if (IsRowRig(r.Id)) { SingleHome(r, out home); yawDeg = r.HomeYawSingle; }
            else { home = r.HomeAll; yawDeg = r.HomeYawAll; PullIntoReach(r, ref home, yawDeg); }
            def = new SeatPose { Local = new Vector3(home.x, DefaultY(r, home.y), home.z), Yaw = yawDeg };
            Vector2 c = PlayCentreOf(r);
            LayoutMath.RotY(c.x, c.y, yawDeg * Mathf.Deg2Rad, out float cx, out float cz);
            bearing = ComfortCodec.Bearing(new Vector2(home.x + cx, home.z + cz));
            main = IsRowRig(r.Id) && KindOf(r.Id) == (_mode == LayoutMode.Single ? _current : Lead);
            return true;
        }

        /// <summary>The rig-local floor point of a rig's play centre (its entry's measured centre, else its descriptor's).</summary>
        Vector2 PlayCentreOf(Rig r)
        {
            RigEntry e = Placement.Entry(r);
            if (e != null && e.HasCentre) return new Vector2(e.CentreLocal.x, e.CentreLocal.z);
            if (r.Id == RigId.Table) return new Vector2(TableCentreLocal.x, TableCentreLocal.z);
            InstrumentDescriptor d = _app?.Registry?.Get(KindOf(r.Id));
            return d?.Seat != null ? d.Seat.PlayCentre : Vector2.zero;
        }

        /// <summary>The rig's pose in the current layout with the player's comfort offset applied (seat frame): false when it
        /// has no offset (it stands at its default pose).</summary>
        bool TrySaved(Rig r, out SeatPose pose)
        {
            pose = default;
            if (_app == null || r == null || r.Id == RigId.Lessons) return false;
            LoadOffsets();
            if (!_offsets.TryGetValue(r.Id, out ComfortOffset o)) return false;
            if (!DefaultSeat(r, out SeatPose def, out Vector2 bearing, out bool main)) return false;
            pose = ComfortCodec.Apply(def, ComfortCodec.Clamp(o, main), bearing);
            return true;
        }

        readonly Dictionary<RigId, SeatPose> _saved = new Dictionary<RigId, SeatPose>();
        readonly HashSet<RigId> _usableSaved = new HashSet<RigId>();
        bool _usableReady;

        /// <summary>Starts a placement pass: the tweaked poses are checked against the plan once, on the first lookup.</summary>
        void BeginSavedCheck() => _usableReady = false;

        /// <summary>TrySaved, in My set without the tweaked poses that now collide (ArrangementCodec.Usable: dropped for the
        /// planner's pose).</summary>
        bool TrySavedChecked(Rig r, out SeatPose pose)
        {
            if (!TrySaved(r, out pose)) return false;
            if (_mode != LayoutMode.All || !(IsRowRig(r.Id) || r.Id == RigId.Table)) return true;
            if (!_usableReady)
            {
                _saved.Clear();
                foreach (KeyValuePair<RigId, ComfortOffset> kv in _offsets)
                {
                    Rig o = Get(kv.Key);
                    if (o != null && TrySaved(o, out SeatPose sp)) _saved[kv.Key] = sp;
                }
                ArrangementCodec.Usable(_planner, _plan, _saved, _usableSaved);
                _usableReady = true;
            }
            if (_plan == null || !_plan.Fits) return true;
            return _usableSaved.Contains(r.Id);
        }

        /// <summary>A rig at its tweaked seat pose about (head, yaw), its height kept inside its comfort range.</summary>
        void PoseSaved(Rig r, in SeatPose sp, Vector3 head, float yaw)
        {
            Transform g = r.Group;
            if (g == null) return;
            ArrangementCodec.FromSeat(sp, head, yaw, out Vector3 p, out float yawDeg);
            LiftRangeHere(r, out float lo, out float hi);
            p.y = Mathf.Clamp(p.y, lo, hi);
            g.SetPositionAndRotation(p, Quaternion.Euler(0f, yawDeg, 0f));
            if (r.InScene && !g.gameObject.activeSelf) g.gameObject.SetActive(true);
        }

        /// <summary>The rigs the player picked up and put down in this Move instruments session: the only ones whose spot is
        /// saved (2026-11-17: Move instruments turned on and off without a drag saved every instrument's automatic pose,
        /// the resolver's push included, as if the player had put it there).</summary>
        readonly HashSet<RigId> _movedByPlayer = new HashSet<RigId>();

        /// <summary>Tests and tools: <paramref name="id"/> counts as put down by the player in this Move instruments session
        /// (what a drag's drop does).</summary>
        public void NoteMovedByPlayer(RigId id)
        {
            if (_arranging) _movedByPlayer.Add(id);
        }

        /// <summary>Keeps how far the player moved each instrument they dragged (and the side table) from its default pose
        /// about the seat it was arranged around, as comfort offsets (ComfortCodec). The offsets of the others (not dragged
        /// now, or not at the seat) are kept as they are; one moved back to its default is forgotten. A piano the player put
        /// on a real table in mixed reality is not a tweak (that table is not in front of every seat).</summary>
        void SaveArrangement()
        {
            if (_app == null || _noSave) return;
            LoadOffsets();
            for (int i = 0; i < Placement.Entries.Count; i++)
            {
                RigEntry e = Placement.Entries[i];
                if (!PlacementSolver.InScene(e) || e.Rig.Id == RigId.Lessons) continue;
                if (!_movedByPlayer.Contains(e.Rig.Id)) continue;   // never an automatic placement
                if (OnRealTable(e.Rig)) continue;
                if (!DefaultSeat(e.Rig, out SeatPose def, out Vector2 bearing, out bool main)) continue;
                Transform g = e.Group;
                SeatPose actual = ArrangementCodec.ToSeat(g.position, LayoutMath.YawOf(g) * Mathf.Rad2Deg, _seatHead, _seatYaw);
                ComfortOffset o = ComfortCodec.Clamp(ComfortCodec.Between(def, actual, bearing), main);
                if (o.IsZero) _offsets.Remove(e.Rig.Id);
                else _offsets[e.Rig.Id] = o;
            }
            _app.Settings.SetComfort(_mode, _current, ComfortCodec.Encode(_offsets));
            _app.Settings.Save();
            _usableReady = false;
            ArrangeSaves++;
        }

        /// <summary>The real floor's height under the instruments: the scene's floor in mixed reality, else 0.</summary>
        float FloorY
        {
            get
            {
                if (!MrActive) return 0f;
                IMixedReality mr = Mr;
                return mr != null ? mr.FloorHeight : 0f;
            }
        }

        /// <summary>LiftRange on the floor the instruments stand on now (the real floor in mixed reality).</summary>
        void LiftRangeHere(Rig r, out float lo, out float hi)
        {
            LiftRange(r, out lo, out hi);
            InstrumentDescriptor d = r.Id == RigId.Lessons ? null : LiftRegistry?.Get(InstrumentIds.KindOf(r.Id));
            if (d?.Seat == null) return;   // a rig without a descriptor keeps its height
            float f = FloorY;
            lo += f;
            hi += f;
        }

        // ---------------------------------------------------------------- what may move, and how high

        /// <summary>Arrange mode moves the placed instruments and the side table (not the menu panel; not a piano standing on
        /// a real table in mixed reality, which "Play in my room" places itself), whether or not they are locked.</summary>
        public bool Arrangeable(Rig r)
        {
            if (r == null || r.Group == null || r.Id == RigId.Lessons || !r.Group.gameObject.activeInHierarchy) return false;
            if (!r.InScene) return OnDisplayForArrange(r);
            if (OnRealTable(r)) return false;   // MR's piano rule: placed on a real table by MR itself
            return true;
        }

        /// <summary>MR's piano rule: the piano stands on a real table (placed there by the point-and-pinch placer).</summary>
        bool OnRealTable(Rig r) => r.Id == RigId.Piano && PianoOnSurface();

        /// <summary>A displayed instrument in the VR room (it moves in arrange mode as platform.md 4.1 says).</summary>
        bool OnDisplayForArrange(Rig r) => _arranging && Presenting && !MrActive && IsRowRig(r.Id) && PlaceOf(KindOf(r.Id)) == InstrumentPlace.Display;

        static IInstrumentRegistry s_editRegistry;

        /// <summary>The registry LiftRange reads: the running app's, else (EditMode, no app) one discovered once.</summary>
        static IInstrumentRegistry LiftRegistry
        {
            get
            {
                if (App.Current?.Registry != null) return App.Current.Registry;
                return s_editRegistry ?? (s_editRegistry = InstrumentRegistry.Discover());
            }
        }

        /// <summary>The heights a rig's origin may take in arrange mode: its descriptor's Seat.LiftMin / LiftMax about its home
        /// (both 0: it stays on the floor at its home height; the piano's key tops 0.55-0.95 m and the kit's -0.17..+0.15 m are
        /// the descriptors' numbers, ARRANGE-NOTES.md). The side table stays on the floor; a rig without a descriptor keeps its
        /// height.</summary>
        public static void LiftRange(Rig r, out float lo, out float hi)
        {
            InstrumentDescriptor d = r.Id == RigId.Lessons ? null : LiftRegistry?.Get(InstrumentIds.KindOf(r.Id));
            if (d?.Seat == null)
            {
                float y = r.Group != null ? r.Group.position.y : 0f;
                lo = hi = y;
                return;
            }
            if (d.Seat.LiftMin == 0f && d.Seat.LiftMax == 0f) { lo = hi = r.HomeAll.y; return; }
            lo = r.HomeAll.y + d.Seat.LiftMin;
            hi = r.HomeAll.y + d.Seat.LiftMax;
        }

        // ---------------------------------------------------------------- targets

        /// <summary>The arrangeable instrument with a part (handles and played parts alike) within <paramref name="reach"/>
        /// of <paramref name="p"/>, nearest first; null when none.</summary>
        public Rig NearestArrangeRig(Vector3 p, float reach, out float distance)
        {
            Rig best = null;
            float bestD = float.PositiveInfinity;
            for (int i = 0; i < Placement.Entries.Count; i++)
            {
                RigEntry e = Placement.Entries[i];
                if (!Arrangeable(e.Rig)) continue;
                for (int k = 0; k < e.Footprint.Count; k++)
                {
                    PartRef part = e.Footprint[k];
                    if (!Placement.Shows(part.R, e.Group) || !PlacementSolver.GeometryBox(part, out Bounds b, out Matrix4x4 tw)) continue;
                    float d = Obb.FromBounds(b, tw).DistanceTo(p);
                    if (d < bestD) { bestD = d; best = e.Rig; }
                }
            }
            distance = bestD;
            return bestD <= reach ? best : null;
        }

        /// <summary>The first arrangeable instrument a ray hits within <paramref name="maxT"/> (any of its parts as an
        /// oriented box, as the desktop pick); null when none.</summary>
        public Rig RayArrangeRig(in Ray ray, float maxT, out float t)
        {
            Rig best = null;
            t = float.PositiveInfinity;
            for (int i = 0; i < Placement.Entries.Count; i++)
            {
                RigEntry e = Placement.Entries[i];
                if (!Arrangeable(e.Rig)) continue;
                for (int k = 0; k < e.Footprint.Count; k++)
                {
                    PartRef part = e.Footprint[k];
                    if (!Placement.Shows(part.R, e.Group) || !PlacementSolver.GeometryBox(part, out Bounds b, out Matrix4x4 tw)) continue;
                    if (Obb.FromBounds(b, tw).Raycast(ray, out float d) && d < t && d <= maxT) { t = d; best = e.Rig; }
                }
            }
            return best;
        }

        bool InShownMenuZone(Vector3 p)
        {
            IMenuPanel menu = Menu;
            if (menu == null || !menu.Shown) return false;
            Bounds? z = menu.GrabZone;
            return z.HasValue && z.Value.Contains(p);
        }

        static Ray PointerRay(HandFrame f) => new Ray(f.pointer.position, f.pointer.rotation * Vector3.forward);

        // ---------------------------------------------------------------- the frame

        void TickArrange(float dt)
        {
            if (_held != null && _held.Group == null) EndGrab();
            // A hand that loses tracking mid-drag: the instrument waits where it is for LostGrace, then drops.
            if (_held != null && _hand != null && !_hand.Connected)
            {
                if ((_lostT += dt) > MoveConstants.LostGrace) EndGrab();
                else _dragPaused = true;
            }
            if (_displayRig != null && (_displayRig.Group == null || _displayHand == null || !_displayHand.Connected || !Presenting)) EndDisplayDrag(save: true);
            if (_held != null && _hand2 != null && !_hand2.Connected) DropSecondHand();
            PointerInput input = _app.Input;
            HandState[] hands = input.Hands;
            if (!Presenting)
            {
                // The desktop: a plain left drag moves an instrument, Alt + drag turns it (LayoutModule.Desktop).
                TickDesktop();
                for (int i = 0; i < _arr.Length; i++) { _arr[i].Near = _arr[i].RayRig = null; }
                UpdateArrangeVisuals();
                return;
            }
            if (MrPlacing)
            {
                for (int i = 0; i < _arr.Length; i++) _arr[i] = default;
                UpdateArrangeVisuals();
                return;
            }
            Vector3 head = _app.Head.position, headForward = _app.Head.forward;
            for (int i = 0; i < hands.Length; i++)
            {
                HandState h = hands[i];
                ref ArrangeHand a = ref _arr[i];
                a.Near = a.RayRig = null;
                if (!h.Connected) { a.Candidate = null; a.Ignore = false; a.PinchT = 0f; continue; }
                HandFrame f = input.Frame(h);
                Vector3 pp = HandMath.PinchPoint(f);
                bool holding = h == _hand || h == _hand2 || h == _displayHand;
                bool menuZone = InShownMenuZone(pp);
                bool system = input.SystemGestureLike(h, head, headForward);
                if (!holding && !menuZone) a.Near = NearestArrangeRig(pp, ArrangeConstants.NearReach, out _);
                if (!holding && !menuZone && a.Near == null && f.pointerValid && !system)
                {
                    a.RayRig = RayArrangeRig(PointerRay(f), ArrangeConstants.RayMaxLength, out a.RayHitT);
                    // Past the seat's instruments the displayed ones are found by the summon's cone pick, so one standing behind
                    // another (the bowls on the sill behind the hand drums) can still be taken.
                    if ((a.RayRig == null || !a.RayRig.InScene) && ConePickDisplayed(PointerRay(f), out Rig cone, out float ct))
                    {
                        a.RayRig = cone;
                        a.RayHitT = ct;
                    }
                }

                if (h == _hand) _lostT = 0f;
                if (h.GrabPinchStart && !holding)
                {
                    a.PinchT = 0f;
                    a.Candidate = null;
                    a.ViaRay = false;
                    // The OS system gesture (palm toward the headset, then a pinch) and pinches on the open menu never grab.
                    a.Ignore = system || menuZone;
                    if (!a.Ignore)
                    {
                        if (_held != null && h != _hand && _hand2 == null && !_rayGrab && (a.Near == _held || InZoneOf(_held, pp)))
                        {
                            // Both hands on it: a two-hand twist about its centre, carried by the pinch midpoint.
                            _hand2 = h;
                            StartTwoHand(GrabPoint(_hand), pp);
                            _drag.Reset((GrabPoint(_hand) + pp) * 0.5f, 0f);
                            BeginLift(_held, (GrabPoint(_hand).y + pp.y) * 0.5f);
                            continue;
                        }
                        if (_held == null)
                        {
                            if (a.Near != null) a.Candidate = a.Near;
                            else if (a.RayRig != null)
                            {
                                a.Candidate = a.RayRig;
                                a.ViaRay = true;
                                a.RayT = a.RayHitT;
                                Ray r = PointerRay(f);
                                a.RayO = r.origin;
                                a.RayD = r.direction;
                            }
                        }
                    }
                }
                if (!h.GrabPinching) { a.Candidate = null; a.Ignore = false; a.PinchT = 0f; }
                if (_displayRig != null && h == _displayHand)
                {
                    if (!h.GrabPinching) EndDisplayDrag(save: true);
                    else MoveDisplayDrag(f, pp);
                    continue;
                }
                if (_held == null && _displayRig == null && a.Candidate != null && h.GrabPinching && !a.Ignore)
                {
                    a.PinchT += dt;
                    if (a.PinchT >= ArrangeConstants.GrabDebounce && Arrangeable(a.Candidate))
                    {
                        StartArrangeGrab(h, f, ref a);
                        a.Candidate = null;
                    }
                }
                if (_held == null) continue;
                if (h == _hand)
                {
                    float wrist = _wristYaw[i].Read(f);
                    if (!HoldsDrag(h, f))
                    {
                        // the pinch opened: the instrument waits for ReleaseGrace (a flicker), then drops where it is
                        if ((_releaseT += dt) > MoveConstants.ReleaseGrace) EndGrab();
                        else _dragPaused = true;
                        continue;
                    }
                    _releaseT = 0f;
                    if (_hand2 != null) StepMoveTwoHand(pp, GrabPoint(_hand2), dt);
                    else
                    {
                        Vector3 target = ArrangeTarget(f, pp);
                        if (_dragPaused) ReanchorMoveDrag(target, wrist, pp.y);
                        StepMoveDrag(target, pp.y, wrist, dt);
                    }
                }
                else if (h == _hand2 && !HoldsDrag(h, f)) DropSecondHand();
            }
            UpdateArrangeVisuals();
        }

        bool InZoneOf(Rig r, Vector3 p)
        {
            Bounds? z = ZoneOf(r);
            return z.HasValue && z.Value.Contains(p);
        }

        /// <summary>Where a hand holds: the pinch point (thumb and index tips) in arrange mode, pointer 0 in play.</summary>
        Vector3 GrabPoint(HandState h)
        {
            if (h == null) return Vector3.zero;
            if (_arranging) return HandMath.PinchPoint(_app.Input.Frame(h));
            return h.Pointers[0].Tip;
        }

        void StartArrangeGrab(HandState h, HandFrame f, ref ArrangeHand a)
        {
            Vector3 pp = HandMath.PinchPoint(f);
            if (!a.Candidate.InScene)
            {
                // an instrument on display: its own drag (floor, wall or surface)
                Ray dr = a.ViaRay ? (f.pointerValid ? PointerRay(f) : new Ray(a.RayO, a.RayD)) : new Ray(pp, Vector3.forward);
                BeginDisplayDrag(a.Candidate, h, dr, a.ViaRay ? a.RayT : 0f, a.ViaRay ? dr.GetPoint(a.RayT) : pp);
                if (_displayRig == null) return;
                ArrangeGrabs++;
                if (a.ViaRay) RayGrabs++;
                return;
            }
            float handYaw = _wristYaw[h.Index].Read(f, reset: true);
            if (a.ViaRay)
            {
                Ray r = f.pointerValid ? PointerRay(f) : new Ray(a.RayO, a.RayD);
                Vector3 hit = r.origin + r.direction * a.RayT;
                StartMoveDrag(a.Candidate, hit, h, handYaw, pp.y);
                _rayGrab = true;
                _rayD0 = a.RayT;
                _rayO0 = r.origin;
                _rayDir0 = r.direction;
                _rayTarget = hit;
                _rayFrom = r.origin;
                RayGrabs++;
            }
            else
            {
                StartMoveDrag(a.Candidate, pp, h, handYaw, pp.y);
                _rayGrab = false;
            }
            ArrangeGrabs++;
        }

        /// <summary>The point an arrange-mode hold follows this frame: near, the pinch point; ray, the point at the kept hit
        /// distance along the hand's ray (plus 2.5 x the hand's push along the first ray; the last one while the pointer pose
        /// is not valid).</summary>
        Vector3 ArrangeTarget(HandFrame f, Vector3 pp)
        {
            if (!_rayGrab) return pp;
            if (f.pointerValid)
            {
                Ray r = PointerRay(f);
                // The hand's push along the first ray moves the instrument RayPushGain (2.5) times as far: the origin
                // itself moved by the push, so the hit distance grows by (gain - 1) times it.
                float d = _rayD0 + (ArrangeConstants.RayPushGain - 1f) * Vector3.Dot(r.origin - _rayO0, _rayDir0);
                d = Mathf.Clamp(d, ArrangeConstants.RayMinDistance, ArrangeConstants.RayMaxLength);
                _rayFrom = r.origin;
                _rayTarget = r.origin + r.direction * d;
            }
            return _rayTarget;
        }

        // ---------------------------------------------------------------- feedback

        void UpdateArrangeVisuals()
        {
            ArrangeVisuals v = ArrangeView;
            if (v == null) return;
            if (!_arranging) { v.HideAll(); return; }
            int slot = 0;
            for (int i = 0; i < Placement.Entries.Count && slot < ArrangeVisuals.Slots; i++)
            {
                RigEntry e = Placement.Entries[i];
                if (!Arrangeable(e.Rig)) continue;
                if (e.Rig == _held) Placement.Measure(e);   // a piano lifted on its stand changes its box
                if (e.Local.Count == 0) Placement.Measure(e);
                if (e.Local.Count == 0) continue;
                Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
                for (int k = 0; k < e.Local.Count; k++)
                {
                    e.Local[k].Corners(_cageCorners);
                    for (int q = 0; q < 8; q++) { mn = Vector3.Min(mn, _cageCorners[q]); mx = Vector3.Max(mx, _cageCorners[q]); }
                }
                ArrangeVisuals.Look look = e.Rig == _held ? (_pass ? ArrangeVisuals.Look.Blocked : ArrangeVisuals.Look.Held)
                    : e.Rig == _displayRig ? (_displayBlocked ? ArrangeVisuals.Look.Blocked : ArrangeVisuals.Look.Held)
                    : Targeted(e.Rig) ? ArrangeVisuals.Look.Target : ArrangeVisuals.Look.Idle;
                RingOf(e.Rig, out Vector3 rc, out float rs);
                // The held rig's floor ring is the grab's own (GrabVisuals, gold, red when pushed through): no second ring.
                v.SetSlot(slot, e.Rig, true, look, (mn + mx) * 0.5f, mx - mn, rc, rs, ring: e.Rig != _held);
                slot++;
            }
            for (; slot < ArrangeVisuals.Slots; slot++) v.SetSlot(slot, null, false, ArrangeVisuals.Look.Idle, Vector3.zero, Vector3.zero, Vector3.zero, 1f);

            HandState[] hands = _app.Input.Hands;
            Vector3 head = _app.Head != null ? _app.Head.position : Vector3.zero, headForward = _app.Head != null ? _app.Head.forward : Vector3.forward;
            for (int h = 0; h < 2; h++)
            {
                HandState hs = hands[h];
                if (!Presenting || !hs.Connected) { v.SetRay(h, false, Vector3.zero, Vector3.zero, false, head); continue; }
                HandFrame f = _app.Input.Frame(hs);
                if (hs == _hand && _rayGrab)
                {
                    v.SetRay(h, true, _rayFrom, _rayTarget, true, head);
                    continue;
                }
                if (hs == _displayHand && _displayRay)
                {
                    v.SetRay(h, true, _displayFrom, _displayTarget, true, head);
                    continue;
                }
                bool show = hs != _hand && hs != _hand2 && hs != _displayHand && _arr[h].Near == null && f.pointerValid && !_arr[h].Ignore
                            && !_app.Input.SystemGestureLike(hs, head, headForward) && !InShownMenuZone(HandMath.PinchPoint(f));
                if (!show) { v.SetRay(h, false, Vector3.zero, Vector3.zero, false, head); continue; }
                Ray r = PointerRay(f);
                bool hits = _arr[h].RayRig != null;
                float len = hits ? _arr[h].RayHitT : ArrangeConstants.RayIdleLength;
                v.SetRay(h, true, r.origin, r.origin + r.direction * len, hits, head);
            }
        }

        bool Targeted(Rig r)
        {
            for (int i = 0; i < _arr.Length; i++)
                if (_arr[i].Near == r || _arr[i].RayRig == r || _arr[i].Candidate == r) return true;
            return false;
        }
    }
}
