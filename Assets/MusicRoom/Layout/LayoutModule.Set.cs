// My set, the display and how instruments come and go (12-new-instruments/platform.md 4.1-4.6): the seat planner decides
// where each member sits (SeatPlanner; the side table yields to its alternate pose only when the set needs it), an
// instrument that is not at the seat stands on display at its spot in the VR room (StowPlan: rig active, Rig.InScene
// false, IStowable.SetStowed(true, mount), never ticked; hidden in mixed reality), and every change of place travels
// (RigGlide: 0.9-1.4 s about the head, the outgoing one leaving 0.15 s first; mixed reality, and a rig with no display
// configuration, slide as today's page turn). One call brings an instrument (Bring: the menu, the page turn, the ray
// summon, a lesson, Sunny); PutAway sends one back. Facing (step 17, next to the swivel) is the at-seat instrument the
// head faces.
//
// A change is applied at once with the tested placement code (membership, PlaceAt, the resolver), then turned into
// motion: every instrument rig that changed place starts from where it was and travels to where the placement put it.
// Glides that are still running when the next change comes are finished first.
//
// The rule for the classic three until they implement IStowable (workstream R): a rig whose module registers no IStowable
// keeps today's behaviour (hidden while out of the layout, today's 0.35 s slide), so nothing is required of it.
using System;
using System.Collections.Generic;
using System.Globalization;
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Layout
{
    public sealed partial class LayoutModule
    {
        // ---------------------------------------------------------------- state

        SeatPlanner _planner;
        SeatPlanner.Plan _plan;
        readonly StowPlan _stow = new StowPlan();
        /// <summary>Each instrument's display spot (its descriptor's, or where the player moved it: musicroom.stow.&lt;id&gt;).</summary>
        readonly StowPlan.Spot[] _spots = new StowPlan.Spot[InstrumentIds.KindCount];
        /// <summary>Where each displayed instrument is right now (its spot, a spare; null = hidden: blocked or not displayed).</summary>
        readonly StowPlan.Spot[] _shownAt = new StowPlan.Spot[InstrumentIds.KindCount];
        readonly bool[] _blockedLogged = new bool[InstrumentIds.KindCount];
        readonly bool[] _homeLogged = new bool[InstrumentIds.KindCount];
        readonly RigGlide[] _glides = new RigGlide[InstrumentIds.KindCount];
        readonly double[] _lastPlayedAt = new double[InstrumentIds.KindCount];
        readonly List<StowPlan.Spot> _displayList = new List<StowPlan.Spot>(10);
        readonly List<SeatPlanner.FloorObb> _seatBoxes = new List<SeatPlanner.FloorObb>(64);
        readonly List<IStowable> _stowables = new List<IStowable>(10);
        readonly List<IHandToolProvider> _tools = new List<IHandToolProvider>(4);
        int _servicesSeen = -1;
        int _glidesActive;
        bool _glideTurn;
        int _glideTurnDir;
        InstrumentKind _glideTurnFrom, _glideTurnTo;
        InstrumentKind? _facing, _facingCandidate;
        float _facingDwell;
        bool _swished;
        Action<RecorderEvent> _onRecorder;

        /// <summary>Focus (platform.md 4.6): a slot within this many degrees of the gaze is faced; the faced one stays until the
        /// gaze is FocusDeg + FocusHysteresisDeg away; a change waits FocusDwell seconds.</summary>
        public const float FocusDeg = 30f, FocusHysteresisDeg = 10f, FocusDwell = 0.3f;
        /// <summary>A rig that moved less than this (m, deg) between two placements does not glide.</summary>
        const float MoveEps = 0.01f, YawEps = 1f;

        /// <summary>The current plan of My set (null in One alone before the first set change).</summary>
        public SeatPlanner.Plan CurrentPlan => _plan;
        public SeatPlanner Planner => _planner;
        public StowPlan Display => _stow;
        /// <summary>Bring calls that changed something, since boot (tests).</summary>
        public int Brings { get; private set; }
        public int GlidesStarted { get; private set; }
        public RigGlide GlideOf(InstrumentKind kind) => (int)kind >= 0 && (int)kind < _glides.Length && _glides[(int)kind] != null && _glides[(int)kind].Active ? _glides[(int)kind] : null;

        void InitSet()
        {
            _planner = SeatPlanner.FromRegistry(_app.Registry, _app.Settings?.Store);
            if (_app.Registry != null)
                foreach (InstrumentDescriptor d in _app.Registry.All)
                    if (d != null && (int)d.Kind >= 0 && (int)d.Kind < _mirroredKind.Length) _mirroredKind[(int)d.Kind] = d.IsMirrored(_app.Settings?.Store);
            for (int i = 0; i < _lastPlayedAt.Length; i++) _lastPlayedAt[i] = double.NaN;
            for (int i = 0; i < _glides.Length; i++) _glides[i] = new RigGlide();
            LoadSpots();
            NormalizeSet();
            _plan = _planner.PlanSet(_set);
            _onRecorder = OnRecorder;
            _app.Events.Subscribe(_onRecorder);
            _onOption = OnInstrumentOption;
            _app.Events.Subscribe(_onOption);
        }

        void ShutdownSet()
        {
            if (_onRecorder != null) _app.Events.Unsubscribe(_onRecorder);
            _onRecorder = null;
            if (_onOption != null) _app.Events.Unsubscribe(_onOption);
            _onOption = null;
        }

        Action<InstrumentOptionEvent> _onOption;
        /// <summary>Per InstrumentKind: built mirrored (SeatSpec.MirrorKey's choice 1), from the store at boot and the option events.</summary>
        readonly bool[] _mirroredKind = new bool[64];

        /// <summary>SeatSpec.MirrorKey: the option that mirrors an instrument changed. The planner takes its
        /// mirrored boxes from now on; when it is at the seat in the VR room's My set the set is planned again and placed
        /// at once if it still fits (else it stays where it is until the set changes: the menu then plans the mirrored boxes).</summary>
        void OnInstrumentOption(InstrumentOptionEvent e)
        {
            InstrumentDescriptor d = _app.Registry?.Get(e.Kind);
            if (d?.Seat == null || string.IsNullOrEmpty(d.Seat.MirrorKey) || e.Key != d.Seat.MirrorKey || _planner == null) return;
            bool mirrored = e.Value == 1;
            if ((int)e.Kind >= 0 && (int)e.Kind < _mirroredKind.Length) _mirroredKind[(int)e.Kind] = mirrored;
            _planner.Add(SeatPlanner.Inst.From(d, mirrored));
            if (_mode != LayoutMode.All || !_set.Contains(e.Kind)) return;
            SeatPlanner.Plan p = _planner.PlanSet(_set);
            if (!p.Fits)
            {
                Debug.Log("[Layout] " + d.Id + (mirrored ? " mirrored" : " as built") + ": the set no longer fits as planned; it stays until the set changes");
                return;
            }
            _plan = p;
            if (MrActive) PlanForRoom();
            ApplyNow();
            PublishLayout();
        }

        void OnRecorder(RecorderEvent e)
        {
            if (e.DuringPlayback || e.Event.type == null || _app.Registry == null) return;
            InstrumentKind k = _app.Registry.OwnerOfEvent(e.Event.type);
            if ((int)k >= 0 && (int)k < _lastPlayedAt.Length) _lastPlayedAt[(int)k] = _app.Clock.NowMs / 1000.0;
        }

        /// <summary>Seconds since the instrument was last played live this session (+inf: never), for join().</summary>
        double SecondsAgo(InstrumentKind k)
        {
            double at = (int)k >= 0 && (int)k < _lastPlayedAt.Length ? _lastPlayedAt[(int)k] : double.NaN;
            return double.IsNaN(at) ? double.PositiveInfinity : Math.Max(0.0, _app.Clock.NowMs / 1000.0 - at);
        }

        /// <summary>The services Layout asks every frame or on every move, re-read only when registrations changed.</summary>
        void RefreshServices()
        {
            if (_app == null || _servicesSeen == _app.ServicesVersion) return;
            _servicesSeen = _app.ServicesVersion;
            _stowables.Clear();
            _stowables.AddRange(_app.GetAll<IStowable>());
            _tools.Clear();
            _tools.AddRange(_app.GetAll<IHandToolProvider>());
        }

        IStowable StowableOf(InstrumentKind k)
        {
            RefreshServices();
            for (int i = 0; i < _stowables.Count; i++) if (_stowables[i] != null && _stowables[i].Kind == k) return _stowables[i];
            return null;
        }

        // ---------------------------------------------------------------- My set: persistence

        /// <summary>The set as loaded (LoadSet), in planner order (Priority). One that no longer fits (the footprints changed) is
        /// re-planned by join() from its first member as saved: each next member joins when join() can add it, else it stays on
        /// display (a load never replaces the member the player listed first).</summary>
        void NormalizeSet()
        {
            if (_set.Count == 0) return;
            var saved = new List<InstrumentKind>(_set);
            SortByPriority(_set);
            if (_planner.PlanSet(_set).Fits) return;
            var work = new List<InstrumentKind>(MySetRules.Max) { saved[0] };
            for (int i = 1; i < saved.Count; i++)
            {
                SeatPlanner.JoinResult j = _planner.Join(work, saved[i], SecondsAgo);
                if (j.Action != JoinAction.Add) continue;
                work.Clear();
                for (int m = 0; m < j.Plan.Members.Count; m++) work.Add(j.Plan.Members[m].Kind);
            }
            Debug.Log($"[Layout] my set {string.Join(",", _set)} no longer fits: {string.Join(",", work)}");
            _set.Clear();
            _set.AddRange(work);
            SortByPriority(_set);
        }

        void SortByPriority(List<InstrumentKind> s)
        {
            for (int i = 1; i < s.Count; i++)
            {
                InstrumentKind k = s[i];
                int pk = PriorityOf(k), j = i - 1;
                while (j >= 0 && PriorityOf(s[j]) > pk) { s[j + 1] = s[j]; j--; }
                s[j + 1] = k;
            }
        }

        int PriorityOf(InstrumentKind k) => _planner?.Get(k)?.Priority ?? (int)k;

        void PersistSet()
        {
            if (_staged) return;   // a playback's layout is never saved (LayoutModule.Stage)
            var ids = new string[_set.Count];
            for (int i = 0; i < _set.Count; i++) ids[i] = _app.Registry?.IdOf(_set[i]) ?? InstrumentIds.Of(_set[i]);
            _app.Settings.SetIds = string.Join(",", ids);
            _app.Settings.Save();
        }

        // ---------------------------------------------------------------- the display spots

        void LoadSpots()
        {
            IInstrumentRegistry reg = _app.Registry;
            for (int i = 0; i < _spots.Length; i++) _spots[i] = null;
            if (reg == null) return;
            IReadOnlyList<InstrumentDescriptor> all = reg.All;
            for (int i = 0; i < all.Count; i++)
            {
                InstrumentDescriptor d = all[i];
                StowPlan.Spot s = StowPlan.Spot.From(d);
                if (s == null) continue;
                if (TryParseStow(_app.Settings.GetStow(d.Id), out double x, out double z, out double yaw))
                {
                    StowPlan.Spot own = s;
                    s = s.MovedToRoom(x, z, yaw);   // saved in room coordinates
                    s.Home = own;   // where it goes back to while the seat's instruments take the saved spot
                }
                _spots[(int)d.Kind] = s;
            }
        }

        /// <summary>musicroom.stow.&lt;id&gt;: "x,z,yaw" in ROOM coordinates (the room's own frame, RoomFrame; invariant culture),
        /// or "" for the plan's spot.</summary>
        public static bool TryParseStow(string text, out double x, out double z, out double yaw)
        {
            x = z = yaw = 0.0;
            if (string.IsNullOrEmpty(text)) return false;
            string[] v = text.Split(',');
            if (v.Length != 3) return false;
            return double.TryParse(v[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) && Math.Abs(x) < 10.0
                && double.TryParse(v[1], NumberStyles.Float, CultureInfo.InvariantCulture, out z) && Math.Abs(z) < 10.0
                && double.TryParse(v[2], NumberStyles.Float, CultureInfo.InvariantCulture, out yaw) && Math.Abs(yaw) <= 720.0;
        }

        public static string FormatStow(double x, double z, double yaw) =>
            x.ToString("0.####", CultureInfo.InvariantCulture) + "," + z.ToString("0.####", CultureInfo.InvariantCulture) + "," +
            SeatPlanner.Wrap(yaw).ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>The instrument has a display configuration in the VR room: a descriptor spot and an IStowable (the classic
        /// three until workstream R adds theirs: hidden while out of the layout, as today).</summary>
        bool HasDisplay(InstrumentKind k) => (int)k >= 0 && (int)k < _spots.Length && _spots[(int)k] != null && StowableOf(k) != null;

        /// <summary>On display now: shown in the VR room at a spot or a spare.</summary>
        bool Displayable(Rig r) => r != null && r.Group != null && !MrActive && HasDisplay(KindOf(r.Id));

        /// <summary>Re-plans where the displayed instruments stand for the at-seat boxes as they are now (the seat frame follows
        /// the head), then poses each one there (or hides it when every place is blocked).</summary>
        void RefreshDisplay()
        {
            if (_app == null) return;
            IRoomFurniture furniture = _app.Get<IRoomFurniture>();
            _stow.SetFurniture(furniture != null ? furniture.FloorBlockers : StowPlan.ReferenceFurnitureWorld);
            _seatBoxes.Clear();
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig r = _rigs[i];
                if (r.Group == null || r.Id == RigId.Lessons) continue;
                InstrumentKind k = KindOf(r.Id);
                bool atSeat = r.Id == RigId.Table || IsInScene(k);
                if (!atSeat) continue;
                AddSeatBoxes(r, k);
            }
            _displayList.Clear();
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig r = _rigs[i];
                if (!IsRowRig(r.Id)) continue;
                InstrumentKind k = KindOf(r.Id);
                if (IsInScene(k) || !HasDisplay(k)) continue;
                _displayList.Add(_spots[(int)k]);
            }
            _stow.Assign(_displayList, _seatBoxes);
            for (int i = 0; i < _shownAt.Length; i++) _shownAt[i] = null;
            for (int i = 0; i < _stow.Results.Count; i++)
            {
                StowPlan.Result res = _stow.Results[i];
                int k = (int)res.Kind;
                _shownAt[k] = res.Spot;
                if (res.Spot == null && !_blockedLogged[k])
                {
                    _blockedLogged[k] = true;
                    StowPlan.Spot own = k < _spots.Length ? _spots[k] : null;
                    Debug.Log("[Layout] display blocked: " + (_app.Registry?.IdOf(res.Kind) ?? res.Kind.ToString()) +
                              (own != null ? $" (its {(own.Home != null ? "saved" : "own")} spot {own.Id} at ({own.X:F2}, {own.Z:F2}) is taken by the seat's instruments, no spare is free)" : ""));
                }
                else if (res.Spot != null) _blockedLogged[k] = false;
                if (res.Spot != null && k < _spots.Length && _spots[k] != null && res.Spot == _spots[k].Home && !_homeLogged[k])
                {
                    _homeLogged[k] = true;
                    Debug.Log($"[Layout] display: {_app.Registry?.IdOf(res.Kind) ?? res.Kind.ToString()}'s saved spot ({_spots[k].X:F2}, {_spots[k].Z:F2}) is taken by the seat's instruments: back at its own spot {res.Spot.Id}");
                }
            }
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig r = _rigs[i];
                if (!IsRowRig(r.Id) || IsInScene(KindOf(r.Id))) continue;
                RigGlide g = GlideOf(KindOf(r.Id));
                if (g != null) continue;   // a glide owns it until it lands
                if (Displayable(r)) ShowDisplayed(r);
            }
        }

        /// <summary>The descriptor's boxes of an at-seat rig at its world pose (heights moved by its lift), non-air only.</summary>
        void AddSeatBoxes(Rig r, InstrumentKind k)
        {
            Transform g = r.Group;
            float yaw = LayoutMath.YawOf(g) * Mathf.Rad2Deg;
            var pose = new SeatPlanner.Pose2(g.position.x, g.position.z, yaw);
            InstrumentDescriptor d = _app.Registry?.Get(k);
            if (d?.Seat?.Boxes == null) return;
            double dy = g.position.y - d.Seat.Home.y;
            FootBox[] boxes = d.Seat.BoxesFor((int)k >= 0 && (int)k < _mirroredKind.Length && _mirroredKind[(int)k]);
            for (int i = 0; i < boxes.Length; i++)
            {
                FootBox b = boxes[i];
                if (b.Air) continue;
                SeatPlanner.FloorObb o = SeatPlanner.WorldBox(pose, new SeatPlanner.Box(b.Cx, b.Cz, b.Hx, b.Hz, b.Y0 + dy, b.Y1 + dy));
                _seatBoxes.Add(o);
            }
        }

        /// <summary>A displayed rig at its place, in its display configuration; hidden when its place is blocked.</summary>
        void ShowDisplayed(Rig r)
        {
            InstrumentKind k = KindOf(r.Id);
            StowPlan.Spot s = _shownAt[(int)k];
            Transform g = r.Group;
            if (s == null)
            {
                if (g.gameObject.activeSelf) g.gameObject.SetActive(false);
                return;
            }
            if (!g.gameObject.activeSelf) g.gameObject.SetActive(true);
            if (g.localScale != Vector3.one) g.localScale = Vector3.one;
            g.SetPositionAndRotation(new Vector3((float)s.X, (float)s.Y, (float)s.Z), Quaternion.Euler(0f, (float)s.Yaw, 0f));
            IStowable st = StowableOf(k);
            if (st != null && !st.Stowed) st.SetStowed(true, s.Mount);
        }

        void Unstow(InstrumentKind k)
        {
            IStowable st = StowableOf(k);
            if (st != null && st.Stowed) st.SetStowed(false, _spots[(int)k] != null ? _spots[(int)k].Mount : StowMount.Floor);
        }

        // ---------------------------------------------------------------- tools

        /// <summary>The tools of an instrument that leaves the seat (or is put on display) are released: its tool providers'
        /// ReleaseAll, and the kit's sticks through IDrumsApi while no provider owns the kit (today's behaviour).</summary>
        void ReleaseTools(Rig r)
        {
            if (r == null) return;
            RefreshServices();
            InstrumentKind k = KindOf(r.Id);
            bool any = false;
            for (int i = 0; i < _tools.Count; i++)
                if (_tools[i] != null && _tools[i].Owner == k) { _tools[i].ReleaseAll(); any = true; }
            if (!any && LegacyKitIs(r)) DrumsApi?.ReleaseAllSticks();
        }

        void ReleaseAllTools()
        {
            RefreshServices();
            for (int i = 0; i < _tools.Count; i++) _tools[i]?.ReleaseAll();
            DrumsApi?.ReleaseAllSticks();
        }

        /// <summary>The rig holds the kit whose sticks still go through IDrumsApi (its root is under the rig's group).</summary>
        bool LegacyKitIs(Rig r)
        {
            IDrumsApi d = DrumsApi;
            if (d == null || r?.Group == null) return false;
            Transform root = d.Root;
            return root != null && (root == r.Group || root.IsChildOf(r.Group));
        }

        /// <summary>No tool provider owns the kit IDrumsApi drives: its sticks go through IDrumsApi.TryPickUp as today.</summary>
        bool LegacyKitPath()
        {
            IDrumsApi d = DrumsApi;
            if (d == null) return false;
            RefreshServices();
            for (int i = 0; i < _rigs.Count; i++)
            {
                if (!LegacyKitIs(_rigs[i])) continue;
                InstrumentKind k = KindOf(_rigs[i].Id);
                for (int t = 0; t < _tools.Count; t++) if (_tools[t] != null && _tools[t].Owner == k) return false;
                return true;
            }
            return true;   // a kit API with no rig of its own (a stand-in): as today
        }

        /// <summary>Layout's tool pick-up (platform.md 4.6): the nearest free tool of any provider gets TryPickUp, else the kit's
        /// IDrumsApi as today (Near / Grabbed and the 0.02 m handle rule are the caller's).</summary>
        int ToolProviderCount() { RefreshServices(); return _tools.Count; }

        StickPick PickUpTool(HandState h, Vector3 tip)
        {
            RefreshServices();
            IHandToolProvider best = null;
            float bestD = float.PositiveInfinity;
            for (int i = 0; i < _tools.Count; i++)
            {
                IHandToolProvider t = _tools[i];
                if (t == null) continue;
                float d = t.NearestFree(tip);
                if (d < bestD) { bestD = d; best = t; }
            }
            if (best != null)
            {
                StickPick took = best.TryPickUp(h, tip);
                if (took != StickPick.None) return took;
            }
            IDrumsApi drums = DrumsApi;
            if (drums != null && LegacyKitPath()) return drums.TryPickUp(h, tip);
            return StickPick.None;
        }

        // ---------------------------------------------------------------- ILayoutService: the platform members

        public bool CanAdd(InstrumentKind instrument) => PreviewJoin(instrument).Action == JoinAction.Add;

        public JoinPreview PreviewJoin(InstrumentKind instrument)
        {
            if (_app == null || !Bringable(instrument)) return new JoinPreview(JoinAction.None);
            if (_mode == LayoutMode.Single)
                return instrument == _current ? new JoinPreview(JoinAction.Already) : new JoinPreview(JoinAction.Replace, _current);
            if (_set.Contains(instrument)) return new JoinPreview(JoinAction.Already);
            SeatPlanner.JoinResult j = _planner.Join(_set, instrument, SecondsAgo);
            return new JoinPreview(j.Action, j.Replaced ?? InstrumentKind.Table);
        }

        public InstrumentKind? ReplaceCandidate(InstrumentKind adding)
        {
            JoinPreview p = PreviewJoin(adding);
            return p.Replaces ? p.Replaced : (InstrumentKind?)null;
        }

        bool Bringable(InstrumentKind k)
        {
            InstrumentDescriptor d = _app.Registry?.Get(k);
            return d != null && d.Playable && _app.Registry.IsLoaded(k) && Get(RigOf(k)) != null;
        }

        public bool TrySetSet(IReadOnlyList<InstrumentKind> set)
        {
            if (_app == null || set == null || set.Count == 0 || set.Count > MySetRules.Max) return false;
            var next = new List<InstrumentKind>(set.Count);
            for (int i = 0; i < set.Count; i++)
            {
                if (!Bringable(set[i]) || next.Contains(set[i])) return false;
                next.Add(set[i]);
            }
            SortByPriority(next);
            if (!_planner.PlanSet(next).Fits) return false;
            if (SameSet(next)) return true;
            LeaveStage("My set changed");
            if (_mode != LayoutMode.All)
            {
                _set.Clear();
                _set.AddRange(next);
                SetVersion++;
                PersistSet();
                _plan = _planner.PlanSet(_set);
                PublishLayout();
                return true;
            }
            ChangeSet(next, BringReason.Menu);
            return true;
        }

        bool SameSet(List<InstrumentKind> next)
        {
            if (next.Count != _set.Count) return false;
            for (int i = 0; i < next.Count; i++) if (!_set.Contains(next[i])) return false;
            return true;
        }

        public bool Bring(InstrumentKind instrument, BringReason reason)
        {
            if (_app == null || !Bringable(instrument)) return false;
            if (_arranging) StopMoving(save: true);   // Move instruments comes back on once the instrument has arrived
            if (_mode == LayoutMode.Single)
            {
                if (_current == instrument) return false;
                LeaveStage("an instrument was brought");
                int dir = CarouselDirection(_current, instrument);
                TurnTo(instrument, dir, reason);
                Signal(instrument, BringSignal(reason));
                return true;
            }
            if (_set.Contains(instrument)) return false;
            SeatPlanner.JoinResult j = _planner.Join(_set, instrument, SecondsAgo);
            if (j.Action == JoinAction.Alone && reason == BringReason.Menu) return false;
            if (!j.Plan.Fits) return false;
            var next = new List<InstrumentKind>(MySetRules.Max);
            for (int i = 0; i < j.Plan.Members.Count; i++) next.Add(j.Plan.Members[i].Kind);
            var gone = new List<InstrumentKind>(MySetRules.Max);
            for (int i = 0; i < _set.Count; i++) if (!next.Contains(_set[i])) gone.Add(_set[i]);
            LeaveStage("an instrument was brought");
            ChangeSet(next, reason);
            for (int i = 0; i < gone.Count; i++) Signal(gone[i], SignalReplaced);
            Signal(instrument, BringSignal(reason));
            return true;
        }

        public bool PutAway(InstrumentKind instrument)
        {
            if (_app == null || _mode != LayoutMode.All || !_set.Contains(instrument) || _set.Count <= 1) return false;
            if (_arranging) StopMoving(save: true);   // Move instruments comes back on once the instrument has arrived
            LeaveStage("an instrument was put away");
            var next = new List<InstrumentKind>(_set);
            next.Remove(instrument);
            ChangeSet(next, BringReason.Menu);
            Signal(instrument, SignalPutAway);
            return true;
        }

        public InstrumentPlace PlaceOf(InstrumentKind instrument)
        {
            if (_app == null) return InstrumentPlace.Hidden;
            if (instrument == InstrumentKind.Table || instrument == InstrumentKind.Menu) return InstrumentPlace.Seat;
            if (_app.Registry != null && !_app.Registry.IsLoaded(instrument)) return InstrumentPlace.Hidden;
            Rig r = Get(RigOf(instrument));
            if (r == null || r.Group == null) return InstrumentPlace.Hidden;
            if (IsMoving(instrument)) return InstrumentPlace.Gliding;
            if (IsInScene(instrument)) return InstrumentPlace.Seat;
            int k = (int)instrument;
            return Displayable(r) && k >= 0 && k < _shownAt.Length && _shownAt[k] != null && r.Group.gameObject.activeInHierarchy
                ? InstrumentPlace.Display : InstrumentPlace.Hidden;
        }

        public SeatSlot? SlotOf(InstrumentKind instrument)
        {
            if (_mode == LayoutMode.Single) return instrument == _current ? SeatSlot.Front : (SeatSlot?)null;
            if (!_set.Contains(instrument) || _plan == null || !_plan.Fits) return null;
            return _plan.TryGet(instrument, out SeatPlanner.Member m) ? m.Slot : (SeatSlot?)null;
        }

        public InstrumentKind? Facing => _mode == LayoutMode.Single ? _current : _facing;

        public bool IsMoving(InstrumentKind instrument)
        {
            if (GlideOf(instrument) != null) return true;
            return _turning && _turnIn != null && _turnIn.Id == RigOf(instrument);
        }

        public bool IsGoingToDisplay(InstrumentKind instrument)
        {
            RigGlide g = GlideOf(instrument);
            return g != null && g.Target == RigGlide.Goal.Display && g.Mode == RigGlide.Kind.Glide;
        }

        public bool AnyMoving => _glidesActive > 0 || _turning;

        public InstrumentKind Lead
        {
            get
            {
                if (_mode == LayoutMode.Single) return _current;
                if (_plan != null && _plan.Fits)
                    for (int i = 0; i < _plan.Members.Count; i++)
                        if (_plan.Members[i].Slot == SeatSlot.Front && _set.Contains(_plan.Members[i].Kind)) return _plan.Members[i].Kind;
                IInstrumentRegistry reg = _app?.Registry;
                if (reg != null)
                    for (int i = 0; i < reg.Carousel.Count; i++) if (_set.Contains(reg.Carousel[i])) return reg.Carousel[i];
                return _set.Count > 0 ? _set[0] : _current;
            }
        }

        public bool TryGetDisplayPoint(InstrumentKind instrument, out Vector3 world)
        {
            world = default;
            if (PlaceOf(instrument) != InstrumentPlace.Display) return false;
            StowPlan.Spot s = _shownAt[(int)instrument];
            if (s == null) return false;
            world = s.PickPoint();
            return true;
        }

        // ---------------------------------------------------------------- signals

        public const string SignalReplaced = "layout.replaced", SignalPutAway = "layout.putaway", SignalSingle = "layout.single";

        static string BringSignal(BringReason r)
        {
            switch (r)
            {
                case BringReason.Menu: return "layout.bring.menu";
                case BringReason.PageTurn: return "layout.bring.pageturn";
                case BringReason.Ray: return "layout.bring.ray";
                case BringReason.Lesson: return "layout.bring.lesson";
                case BringReason.Take: return "layout.bring.take";
                default: return "layout.bring.sunny";
            }
        }

        void Signal(InstrumentKind k, string signal) => _app.Events.Publish(new InstrumentSignalEvent { Kind = k, Signal = signal });

        void Move(InstrumentKind k, MovePhase phase, BringReason reason) =>
            _app.Events.Publish(new InstrumentMoveEvent { Kind = k, Phase = phase, Reason = reason });

        /// <summary>+1 when the instrument is reached sooner going forward through the carousel, else -1.</summary>
        int CarouselDirection(InstrumentKind from, InstrumentKind to)
        {
            IInstrumentRegistry reg = _app.Registry;
            if (reg == null) return 1;
            InstrumentKind k = from;
            for (int i = 0; i < reg.Carousel.Count; i++)
            {
                k = reg.Next(k, 1);
                if (k == to) return i <= reg.Carousel.Count / 2 ? 1 : -1;
            }
            return 1;
        }

        // ---------------------------------------------------------------- changing the set

        void ChangeSet(List<InstrumentKind> next, BringReason reason)
        {
            FinishMotion();
            if (_held != null) EndGrab();
            Brings++;
            // the set changes inside the motion's apply step, after Animate has seen where everything was
            Animate(reason, () =>
            {
                _set.Clear();
                _set.AddRange(next);
                SortByPriority(_set);
                SetVersion++;
                PersistSet();
                _plan = _planner.PlanSet(_set);
                if (MrActive) PlanForRoom();   // no rear slot and the draw guard in the real room
                ApplyNow();
            }, true);
            PublishLayout();
        }

        /// <summary>The new layout at once (membership, the planned poses, the resolver, the display), about the player's front
        /// (no fitSeat: a bring never moves the player).</summary>
        void ApplyNow()
        {
            ApplyMembership();
            CollectAll();
            if (_app.Head == null) { RefreshDisplay(); return; }
            ReadFront(out Vector3 head, out float yaw);
            PlaceAt(head, yaw, true, settle: true);
            RefreshDisplay();
        }

        // ---- the motion of a change

        struct Before
        {
            public bool Has;
            public InstrumentPlace Place;
            public bool Active;
            public Vector3 Pos;
            public float Yaw;
        }
        readonly Before[] _before = new Before[InstrumentIds.KindCount];

        /// <summary>Applies <paramref name="apply"/> at once, then turns every instrument rig's change of place into motion: a
        /// glide in the VR room for one with a display configuration, a slide (today's page turn) in mixed reality or without
        /// one when <paramref name="animateAll"/>, else at once (today's behaviour). The outgoing ones start first, the
        /// incoming ones OutgoingLead later. The side table glides when it moves between its home and its alternate.</summary>
        void Animate(BringReason reason, Action apply, bool animateAll)
        {
            bool tableWasAlt = _tableAtAlternate;
            for (int i = 0; i < _before.Length; i++) _before[i] = default;
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig r = _rigs[i];
                if (r.Group == null || !(IsRowRig(r.Id) || r.Id == RigId.Table)) continue;
                int k = (int)r.Id;
                if (k < 0 || k >= _before.Length) continue;
                _before[k] = new Before
                {
                    Has = true, Place = PlaceOf(KindOf(r.Id)), Active = r.Group.gameObject.activeSelf, Pos = r.Group.position,
                    Yaw = LayoutMath.YawOf(r.Group) * Mathf.Rad2Deg,
                };
            }
            apply();
            if (_app.Head == null || !FirstArrangeDone) return;
            bool tableMoves = tableWasAlt != _tableAtAlternate;
            ReadSeat(out Vector3 head, out float seatYaw);
            Vector3 right = LayoutMath.SeatRight(seatYaw);
            bool anyOut = false;
            _swished = false;
            // the outgoing ones first
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < _rigs.Count; i++)
                {
                    Rig r = _rigs[i];
                    int k = (int)r.Id;
                    if (r.Group == null || k < 0 || k >= _before.Length || !_before[k].Has) continue;
                    Before b = _before[k];
                    InstrumentKind kind = KindOf(r.Id);
                    bool table = r.Id == RigId.Table;
                    InstrumentPlace now = table ? InstrumentPlace.Seat : PlaceOf(kind);
                    bool wasSeat = b.Place == InstrumentPlace.Seat, isSeat = now == InstrumentPlace.Seat;
                    float yawNow = LayoutMath.YawOf(r.Group) * Mathf.Rad2Deg;
                    bool moved = Vector3.Distance(b.Pos, r.Group.position) > MoveEps || Mathf.Abs(Mathf.DeltaAngle(b.Yaw, yawNow)) > YawEps;
                    if (pass == 0 && wasSeat && !isSeat)
                    {
                        // leaving the seat (ApplyMembership released its tools)
                        anyOut = true;
                        if (Displayable(r) && now == InstrumentPlace.Display && b.Active)
                            StartGlide(r, kind, RigGlide.Goal.Display, reason, b.Pos, b.Yaw, head, 0f);
                        else if (animateAll && b.Active && !table)
                            StartSlide(r, kind, RigGlide.Goal.Display, reason, false, b.Pos, b.Yaw, right, 0f);
                        else { Move(kind, MovePhase.Leaving, reason); Move(kind, MovePhase.Displayed, reason); }
                    }
                    else if (pass == 1 && !wasSeat && isSeat && !table)
                    {
                        // coming to the seat
                        float delay = anyOut ? RigGlide.OutgoingLead : 0f;
                        if (Displayable(r) && b.Place == InstrumentPlace.Display && b.Active)
                            StartGlide(r, kind, RigGlide.Goal.Seat, reason, b.Pos, b.Yaw, head, delay);
                        else if (animateAll)
                            StartSlide(r, kind, RigGlide.Goal.Seat, reason, true, r.Group.position, yawNow, right, delay);
                        else { Move(kind, MovePhase.Leaving, reason); Move(kind, MovePhase.Arrived, reason); }
                    }
                    else if (pass == 2 && wasSeat && isSeat && moved && !MrActive)
                    {
                        // a member that changes slot, or the side table between its home and its alternate
                        bool glides = table ? tableMoves : HasDisplay(kind);
                        if (glides)
                        {
                            if (!table) ReleaseTools(r);
                            StartGlide(r, kind, RigGlide.Goal.Reseat, reason, b.Pos, b.Yaw, head, anyOut ? RigGlide.OutgoingLead : 0f);
                        }
                    }
                }
        }

        void StartGlide(Rig r, InstrumentKind kind, RigGlide.Goal goal, BringReason reason, Vector3 from, float fromYaw, Vector3 head, float delay)
        {
            Transform g = r.Group;
            Vector3 to = g.position;
            float toYaw = LayoutMath.YawOf(g) * Mathf.Rad2Deg;
            RigGlide gl = _glides[(int)r.Id];
            gl.Begin(r, kind, goal, reason, from, fromYaw, to, toYaw, head, delay);
            BeginMotion(r, kind, goal, gl);
            g.SetPositionAndRotation(from, Quaternion.Euler(0f, fromYaw, 0f));
        }

        void StartSlide(Rig r, InstrumentKind kind, RigGlide.Goal goal, BringReason reason, bool incoming, Vector3 rest, float yaw, Vector3 right, float delay)
        {
            RigGlide gl = _glides[(int)r.Id];
            gl.BeginSlide(r, kind, goal, reason, incoming, rest, yaw, right, 1, delay);
            BeginMotion(r, kind, goal, gl);
            r.Group.SetPositionAndRotation(rest, Quaternion.Euler(0f, yaw, 0f));
            if (incoming) r.Group.localScale = Vector3.one * GrabConstants.TurnScale;
        }

        /// <summary>What every motion does as it starts: the rig is not present (no tick, no grab, no zone), shown while it
        /// moves, in the configuration of where it comes from; one swish per change.</summary>
        void BeginMotion(Rig r, InstrumentKind kind, RigGlide.Goal goal, RigGlide gl)
        {
            if (_held == r) EndGrab();
            if (!r.Group.gameObject.activeSelf) r.Group.gameObject.SetActive(true);
            r.InScene = false;
            RigEntry e = Placement.Entry(r);
            if (e != null) e.HasZone = false;
            IStowable st = r.Id == RigId.Table ? null : StowableOf(kind);
            if (st != null)
            {
                // it keeps the look of where it comes from until it lands (on display: stowed; at the seat: playing)
                bool fromDisplay = goal == RigGlide.Goal.Seat && gl.Mode == RigGlide.Kind.Glide;
                if (fromDisplay && !st.Stowed) st.SetStowed(true, _spots[(int)kind]?.Mount ?? StowMount.Floor);
                else if (!fromDisplay && st.Stowed) st.SetStowed(false, _spots[(int)kind]?.Mount ?? StowMount.Floor);
            }
            _glidesActive++;
            GlidesStarted++;
            if (!_swished) { _swished = true; _app.Get<IAudioEngine>()?.Swish(); }
            if (gl.Delay <= 0f)
            {
                gl.Left = true;
                if (r.Id != RigId.Table) Move(kind, MovePhase.Leaving, gl.Reason);
            }
        }

        /// <summary>LayoutAnimation phase: every running glide moves; a glide that lands finishes its change.</summary>
        void TickGlides(float dt)
        {
            if (_glidesActive == 0) { if (_glideTurn) EndGlideTurn(); return; }
            for (int i = 0; i < _glides.Length; i++)
            {
                RigGlide g = _glides[i];
                if (g == null || !g.Active) continue;
                bool wasWaiting = g.Delay > 0f;
                bool done = g.Step(dt);
                if (wasWaiting && g.Delay <= 0f && !g.Left)
                {
                    g.Left = true;
                    if (g.Rig.Id != RigId.Table) Move(g.Instrument, MovePhase.Leaving, g.Reason);
                }
                if (done) Land(g);
            }
            if (_glideTurn && _glidesActive == 0) EndGlideTurn();
        }

        /// <summary>A glide (or slide) arrives: at the seat the rig is unstowed, present and zoned, then Arrived; on display it
        /// is stowed (a slide out in mixed reality hides it), then Displayed.</summary>
        void Land(RigGlide g)
        {
            Rig r = g.Rig;
            InstrumentKind kind = g.Instrument;
            RigGlide.Goal goal = g.Target;
            RigGlide.Kind mode = g.Mode;
            BringReason reason = g.Reason;
            g.Clear();
            _glidesActive = Mathf.Max(0, _glidesActive - 1);
            if (r?.Group == null) return;
            if (r.Group.localScale != Vector3.one) r.Group.localScale = Vector3.one;
            bool table = r.Id == RigId.Table;
            if (goal == RigGlide.Goal.Display)
            {
                if (mode == RigGlide.Kind.SlideOut || !Displayable(r) || IsInScene(kind)) ApplyMembership();
                else ShowDisplayed(r);
                Move(kind, MovePhase.Displayed, reason);
                return;
            }
            bool member = table || IsInScene(kind);
            if (!member) { ApplyMembership(); return; }
            if (!table) Unstow(kind);
            r.InScene = true;
            RigEntry e = Placement.Entry(r);
            if (e != null) { Collect(e); RefreshZone(e); }
            if (goal == RigGlide.Goal.Seat) Move(kind, MovePhase.Arrived, reason);
        }

        /// <summary>Every running glide or turn ends now, at its target (a new change, an arrangement, arrange mode).</summary>
        void FinishMotion()
        {
            FinishSettle();
            if (_turning) FinishTurn();
            for (int i = 0; i < _glides.Length; i++)
            {
                RigGlide g = _glides[i];
                if (g == null || !g.Active) continue;
                if (!g.Left)
                {
                    g.Left = true;
                    if (g.Rig.Id != RigId.Table) Move(g.Instrument, MovePhase.Leaving, g.Reason);
                }
                g.Finish();
                Land(g);
            }
            if (_glideTurn) EndGlideTurn();
        }

        // ---------------------------------------------------------------- the page turn with glides (One alone, VR room)

        /// <summary>One alone: the instrument replaces the current one. In the VR room, when it has a display configuration, it
        /// glides from its display spot to its single home (the current one glides back to its spot 0.15 s first); otherwise
        /// today's 0.35 s slide (StartTurn: mixed reality, or no display spot to come from).</summary>
        void TurnTo(InstrumentKind to, int dir, BringReason reason)
        {
            FinishMotion();
            InstrumentKind from = _current;
            Rig rin = Get(RigOf(to));
            if (rin == null || rin.Group == null || !FirstArrangeDone || !Displayable(rin) || PlaceOf(to) != InstrumentPlace.Display)
            {
                _turnReason = reason;
                StartTurn(from, to, dir);
                return;
            }
            Rig rout = Get(RigOf(from));
            Turns++;
            _glideTurn = true;
            _glideTurnDir = dir;
            _glideTurnFrom = from;
            _glideTurnTo = to;
            Brings++;
            Animate(reason, () => { _current = to; Persist(); PlaceTurn(rin, rout); }, true);
            _app.Events.Publish(new TurnEvent { Started = true, Direction = dir, From = from, To = to });
            if (_glidesActive == 0) EndGlideTurn();
        }

        BringReason _turnReason = BringReason.PageTurn;

        /// <summary>The incoming instrument at its rest about the player's front (its own single placement, else its single
        /// home with its play centre on the forward line), settled with the outgoing one left out; the side table and the menu
        /// stay (as StartTurn).</summary>
        void PlaceTurn(Rig rin, Rig rout)
        {
            ApplyMembership();
            CollectAll();   // its play centre as it plays (it was measured folded on display)
            ReadFront(out Vector3 head, out float yaw);
            _seatHead = head;
            _seatYaw = yaw;
            Transform gin = rin.Group;
            if (!gin.gameObject.activeSelf) gin.gameObject.SetActive(true);
            gin.localScale = Vector3.one;
            if (TrySaved(rin, out SeatPose saved)) PoseSaved(rin, saved, head, yaw);
            else
            {
                SingleHome(rin, out Vector3 single);
                Vector3 rest = LayoutMath.SeatPlace(head, yaw, single);
                rest.y = DefaultY(rin, rin.HomeSingle.y);
                gin.SetPositionAndRotation(rest, Quaternion.Euler(0f, rin.HomeYawSingle + yaw * Mathf.Rad2Deg, 0f));
            }
            CollectAll();
            Settle(head, yaw, null, rout != null ? Placement.Entry(rout) : null);
            RefreshDisplay();
        }

        void EndGlideTurn()
        {
            if (!_glideTurn) return;
            _glideTurn = false;
            _app.Events.Publish(new TurnEvent { Started = false, Direction = _glideTurnDir, From = _glideTurnFrom, To = _glideTurnTo });
            PublishLayout();
        }

        // ---------------------------------------------------------------- focus (step 17, next to the swivel)

        /// <summary>The at-seat instrument whose slot heading is within FocusDeg of the head's flattened forward (relative to
        /// the seat's heading); the faced one stays until FocusDeg + FocusHysteresisDeg; a change holds FocusDwell. One alone:
        /// Current always. FocusEvent on change. Allocation-free.</summary>
        void TickFocus(float dt)
        {
            if (_app?.Head == null) return;
            InstrumentKind? want;
            if (_mode == LayoutMode.Single) want = _current;
            else
            {
                Vector3 f = _app.Head.forward;
                f.y = 0f;
                want = _facing;
                if (f.sqrMagnitude > 1e-8f && _plan != null && _plan.Fits)
                {
                    float gaze = Mathf.DeltaAngle(_seatYaw * Mathf.Rad2Deg, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);
                    float keep = float.PositiveInfinity, best = float.PositiveInfinity;
                    InstrumentKind? bestKind = null;
                    for (int i = 0; i < _plan.Members.Count; i++)
                    {
                        SeatPlanner.Member m = _plan.Members[i];
                        if (!_set.Contains(m.Kind) || PlaceOf(m.Kind) != InstrumentPlace.Seat) continue;
                        float d = Mathf.Abs(Mathf.DeltaAngle(SeatSlots.Heading(m.Slot) + MovedBearing(m), gaze));
                        if (_facing.HasValue && m.Kind == _facing.Value) keep = d;
                        if (d < best) { best = d; bestKind = m.Kind; }
                    }
                    if (keep <= FocusDeg + FocusHysteresisDeg) want = _facing;
                    else want = best <= FocusDeg ? bestKind : null;
                }
                else want = null;
            }
            if (Nullable.Equals(want, _facing)) { _facingCandidate = _facing; _facingDwell = 0f; return; }
            if (_mode == LayoutMode.Single) { SetFacing(want); return; }
            if (!Nullable.Equals(want, _facingCandidate)) { _facingCandidate = want; _facingDwell = 0f; }
            _facingDwell += dt;
            if (_facingDwell >= FocusDwell) SetFacing(want);
        }

        /// <summary>How far (degrees about the seat) a member stands from its planned pose's bearing: pulled into the reach
        /// band or tweaked by the player, its slot's heading turns with it, so looking at it still faces it.</summary>
        float MovedBearing(SeatPlanner.Member m)
        {
            Rig r = Get(RigOf(m.Kind));
            if (r?.Group == null) return 0f;
            float px = (float)m.Pose.X, pz = (float)m.Pose.Z;
            Vector3 g = r.Group.position;
            LayoutMath.RotY(g.x - _seatHead.x, g.z - _seatHead.z, -_seatYaw, out float ax, out float az);
            if (px * px + pz * pz < 0.01f || ax * ax + az * az < 0.01f) return 0f;
            return Mathf.DeltaAngle(Mathf.Atan2(px, pz) * Mathf.Rad2Deg, Mathf.Atan2(ax, az) * Mathf.Rad2Deg);
        }

        void SetFacing(InstrumentKind? k)
        {
            _facing = k;
            _facingCandidate = k;
            _facingDwell = 0f;
            _app.Events.Publish(new FocusEvent { Kind = k ?? InstrumentKind.Table, Has = k.HasValue });
        }
    }
}
