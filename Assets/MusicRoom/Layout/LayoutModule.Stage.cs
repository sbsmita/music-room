// A recording brings its instruments (2026-11-11 headset test: "if i played piano with recordings and moved to drums then
// played drums then stopped. but when played recording it doesnt play as piano is not in front. so either bring the single
// instrument playing or bring all the instruments and let recording play.").
//
// When the player starts a recording playing back (the deck's PLAY of a take or of its loop, a take or a loop from
// Recordings: the Recorder's RecordingPlaybackEvent with the instruments it plays), every instrument of the recording
// that is not in front of the player comes there for the playback: one instrument alone as One alone, several as My set
// (as many as fit together, My set's rule: the recording's instruments in planner order). They glide in from their
// display spots (the VR room) or slide in (mixed reality), so the player watches them play themselves; the others go to
// their places. The layout is laid out about the seat the player's own layout was laid out about (the stored seat frame,
// never the head's pose at the PLAY: the player looks at the deck or the menu then), and nothing is saved: the player's
// mode, instrument and My set stay as they were in the settings.
//
// When the playback ends (its end, STOP, CLEAR) the player's own layout comes back exactly (mode, instrument, My set, the
// seat frame and every instrument's pose: risers, heights and Move-instruments spots included) after StageEndGrace, gliding
// back; a replay within the grace keeps them out. Unless the player changed the layout meanwhile; then what they chose stays
// (and is saved):
//   - they chose a layout or an instrument (the menu's One alone / My set, a page turn, a bring, a put-away, My set's list),
//     Reset view (menu or hold), Move instruments, mixed reality on or off, placing in mixed reality: at that moment;
//   - they moved, lifted or turned an instrument, turned the seat, the headset's return moved their level: found when the
//     playback ends (an instrument not where the stage left it, beyond StageMoveEps / StageYawEps);
//   - REC or LOOP while the playback's instruments are out: they record with what is in front of them (taking the
//     instruments away just as they start to play would leave them nothing to play for a second).
// A looping playback keeps its instruments until the loop stops; overdubbing onto it keeps them too (the loop goes on).
// While Move instruments is on, or a lesson runs, a playback moves nothing. A recording with more instruments than fit
// brings those that fit; every instrument sounds anyway (Audio's AudioPresence keeps a hidden instrument audible).
// Sunny says tip_play_brings once, the first time a playback brings its instruments.
// In the VR room the playback's instruments stand StageFarther (0.45 m) beyond their playing distance (2026-11-18: "when we
// play recording ... place all intruments little far ... this only while playing recordings and later bring any instruments
// where it was"), also when they were in front already, so the player watches the ghost hands play; the moment the player
// plays along (record on top, REC, a loop overdub) they glide back to the playing distance, and a layout the player takes
// over is laid out at the playing distance.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Layout
{
    public sealed partial class LayoutModule
    {
        /// <summary>After a playback ends its instruments wait this long before the player's layout comes back (s): the last
        /// notes ring out, and a replay meanwhile keeps them in front.</summary>
        public const float StageEndGrace = 1.0f;
        /// <summary>An instrument further than this (m, deg) from where the stage left it was moved by the player.</summary>
        public const float StageMoveEps = 0.02f, StageYawEps = 2f;
        /// <summary>Sunny's line the first time a recording brings its instruments (asked again every TipEvery while busy).</summary>
        public const string StageTipId = PlaybackTips.StageId;
        public const float StageTipEvery = 4f;

        struct StagePose
        {
            public bool Has, AtSeat;
            public Vector3 Pos;
            public Quaternion Rot;
        }

        System.Action<RecordingPlaybackEvent> _onPlayback;
        System.Action<RecorderStateEvent> _onRecState;

        bool _staged, _stageEndPending, _stageTipDone, _stageTipWanted, _stageFar, _stageHere;
        float _stageEndIn, _stageTipAt, _stageTime;
        uint _stageKinds;
        // the player's own layout, put back when the playback ends
        LayoutMode _ownMode;
        InstrumentKind _ownCurrent;
        readonly List<InstrumentKind> _ownSet = new List<InstrumentKind>(MySetRules.Max);
        SeatPlanner.Plan _ownPlan;
        Vector3 _ownSeatHead;
        float _ownSeatYaw;
        bool _ownTableAlt;
        readonly StagePose[] _ownPose = new StagePose[InstrumentIds.KindCount];
        // what the stage left (to tell a change the player made)
        LayoutMode _stageMode;
        InstrumentKind _stageCurrent;
        readonly List<InstrumentKind> _stageSet = new List<InstrumentKind>(MySetRules.Max);
        readonly StagePose[] _stagePose = new StagePose[InstrumentIds.KindCount];
        readonly List<InstrumentKind> _stageWant = new List<InstrumentKind>(InstrumentIds.KindCount);
        readonly List<InstrumentKind> _stageFit = new List<InstrumentKind>(MySetRules.Max);

        /// <summary>A playback's instruments are in front of the player; the player's own layout comes back when it ends.</summary>
        public bool PlaybackStaged => _staged;
        /// <summary>The staged instruments stand StageFarther out (the VR room, nobody playing along): PushShowOut.</summary>
        public bool StageFar => _staged && _stageFar && !MrActive;
        /// <summary>Stages brought back to the playing distance because the player played along (tests, logs).</summary>
        public int StageNears { get; private set; }
        /// <summary>The instruments of the playback that brought the stage (RecordingPlaybackEvent.Kinds).</summary>
        public uint StagedKinds => _stageKinds;
        /// <summary>The player's own layout waiting for the playback's end (valid while PlaybackStaged).</summary>
        public LayoutMode OwnMode => _ownMode;
        public InstrumentKind OwnCurrent => _ownCurrent;
        public IReadOnlyList<InstrumentKind> OwnSet => _ownSet;
        /// <summary>Since boot (tests, logs): stages started, put back at the end, kept because the player changed them.</summary>
        public int Stagings { get; private set; }
        public int StageRestores { get; private set; }
        public int StageKeeps { get; private set; }
        /// <summary>Sunny was asked for tip_play_brings and said it (or had said it before).</summary>
        public bool StageTipAsked => _stageTipDone;

        void InitStage()
        {
            _onPlayback = OnRecordingPlayback;
            _onRecState = OnRecorderStateForStage;
            _app.Events.Subscribe(_onPlayback);
            _app.Events.Subscribe(_onRecState);
        }

        void ShutdownStage()
        {
            if (_onPlayback != null) _app.Events.Unsubscribe(_onPlayback);
            if (_onRecState != null) _app.Events.Unsubscribe(_onRecState);
            _onPlayback = null;
            _onRecState = null;
        }

        void OnRecordingPlayback(RecordingPlaybackEvent e)
        {
            if (!e.Started)
            {
                if (_staged && !_stageEndPending) { _stageEndPending = true; _stageEndIn = StageEndGrace; }
                return;
            }
            _stageEndPending = false;
            Stage(e.Kinds);
        }

        /// <summary>REC (a take) or LOOP (a new session) while a playback's instruments are out: they stay for the recording.</summary>
        void OnRecorderStateForStage(RecorderStateEvent e)
        {
            if (e.State == RecorderState.Recording && _staged) LeaveStage("recording");
        }

        /// <summary>LayoutAnimation phase: the end of a playback (after the grace) and Sunny's line. Allocation-free.</summary>
        void TickStage(float dt)
        {
            _stageTime += dt;
            if (_staged && _stageFar && !_stageEndPending && PlayerPlaysAlong()) BringStageNear();
            if (_stageEndPending)
            {
                _stageEndIn -= dt;
                if (_stageEndIn <= 0f) EndStage();
            }
            PlaybackTips.Pending = _stageTipWanted && !_stageTipDone;   // the show's lines wait for this one (PlaybackTips)
            if (!_stageTipWanted || _stageTipDone || _stageTime < _stageTipAt) return;
            _stageTipAt = _stageTime + StageTipEvery;
            IGuide guide = _app.Get<IGuide>();
            if (guide == null) return;
            if (PlaybackTips.Ask(guide, StageTipId)) { _stageTipDone = true; _stageTipWanted = false; PlaybackTips.Pending = false; }
        }

        // ---------------------------------------------------------------- bringing the playback's instruments

        void Stage(uint kinds)
        {
            if (_app == null || _app.Head == null || !FirstArrangeDone) return;
            _stageWant.Clear();
            for (int k = 0; k < 32; k++)
                if ((kinds & (1u << k)) != 0 && Bringable((InstrumentKind)k)) _stageWant.Add((InstrumentKind)k);
            if (_stageWant.Count == 0) return;
            if (_moveWanted || _arranging) { Debug.Log("[Layout] a playback started while Move instruments is on: nothing moves"); return; }
            if (MrPlacing) return;
            var tutorial = _app.Get<ITutorial>();
            if (tutorial != null && tutorial.State != TutorialState.Idle) { Debug.Log("[Layout] a playback started during a lesson: nothing moves"); return; }
            // A playback while the player's own changes to the last stage are still in front: theirs now (kept), then staged.
            // (While the stage still glides in nothing can be told yet: the player had no time to change it.)
            if (_staged && !AnyMoving && ChangedByPlayer(out string why)) LeaveStage(why);
            // As many of the recording's instruments as fit together, in planner order (My set's rule).
            SortByPriority(_stageWant);
            _stageFit.Clear();
            for (int i = 0; i < _stageWant.Count; i++)
            {
                _stageFit.Add(_stageWant[i]);
                if (_stageFit.Count > MySetRules.Max || (_stageFit.Count > 1 && !_planner.PlanSet(_stageFit).Fits)) _stageFit.RemoveAt(_stageFit.Count - 1);
            }
            if (_stageFit.Count < _stageWant.Count)
                Debug.Log($"[Layout] the playback plays {_stageWant.Count} instruments; {_stageFit.Count} fit in front, the others sound from where they are");
            bool here = true;
            for (int i = 0; i < _stageFit.Count && here; i++) here = IsInScene(_stageFit[i]) && !IsMoving(_stageFit[i]);
            // Every instrument it plays is in front already: nothing moves while staged, in mixed reality, or when the player
            // plays along; else (the VR room) they step back for the show.
            if (here && (_staged || MrActive || PlayerPlaysAlong()))
            {
                if (_staged) _stageKinds = kinds;
                return;
            }
            if (!_staged) KeepOwnLayout();
            FinishMotion();
            if (_held != null) EndGrab();
            _staged = true;   // before the apply: nothing of the stage is saved (Persist / PersistSet)
            _stageFar = !PlayerPlaysAlong();   // StageFar: PushShowOut moves the front ones StageFarther out (the VR room)
            _stageHere = here;
            _stageKinds = kinds;
            Animate(BringReason.Take, () =>
            {
                if (_stageHere) { }   // all in front already: the layout stays as it is, only farther out
                else if (_stageFit.Count == 1)
                {
                    _mode = LayoutMode.Single;
                    _current = _stageFit[0];
                }
                else
                {
                    _mode = LayoutMode.All;
                    if (!SameSet(_stageFit))
                    {
                        _set.Clear();
                        _set.AddRange(_stageFit);
                        SetVersion++;
                    }
                    _plan = _planner.PlanSet(_set);
                    if (MrActive) PlanForRoom();   // no rear slot and the draw guard in the real room (nothing saved)
                }
                ApplyMembership();
                CollectAll();
                // About the player's front (the seat their layout was laid out about; a single instrument at One alone's
                // spot, straight ahead), never the head's pose at the PLAY.
                ReadFront(out Vector3 frontHead, out float frontYaw);
                PlaceAt(frontHead, frontYaw, true, settle: true);
                PushShowOut(frontHead, frontYaw);
                RefreshDisplay();
                RememberStage();
            }, true);
            Stagings++;
            // Sunny's "your recording's instruments" only when the playback brings one; not when the ones in front just step back
            if (!_stageTipDone && !_stageHere) { _stageTipWanted = true; _stageTipAt = _stageTime + 0.5f; }
            PublishLayout();
            Debug.Log($"[Layout] a playback brings {Names(_stageFit)} in front of the player ({(_mode == LayoutMode.Single ? "One alone" : "My set")}); " +
                      $"their own layout ({(_ownMode == LayoutMode.Single ? "One alone " + _ownCurrent : "My set " + Names(_ownSet))}) comes back after it");
        }

        /// <summary>The player's own layout as it is now (put back when the playback ends).</summary>
        void KeepOwnLayout()
        {
            _ownMode = _mode;
            _ownCurrent = _current;
            _ownSet.Clear();
            _ownSet.AddRange(_set);
            _ownPlan = _plan;
            _ownSeatHead = _seatHead;
            _ownSeatYaw = _seatYaw;
            _ownTableAlt = _tableAtAlternate;
            Capture(_ownPose);
        }

        void RememberStage()
        {
            _stageMode = _mode;
            _stageCurrent = _current;
            _stageSet.Clear();
            _stageSet.AddRange(_set);
            Capture(_stagePose);
        }

        /// <summary>Every instrument rig's and the side table's pose, and whether it is at the seat.</summary>
        void Capture(StagePose[] into)
        {
            for (int i = 0; i < into.Length; i++) into[i] = default;
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig r = _rigs[i];
                int k = (int)r.Id;
                if (r.Group == null || k < 0 || k >= into.Length || !(IsRowRig(r.Id) || r.Id == RigId.Table)) continue;
                into[k] = new StagePose
                {
                    Has = true, AtSeat = r.Id == RigId.Table || IsInScene(KindOf(r.Id)),
                    Pos = r.Group.position, Rot = r.Group.rotation,
                };
            }
        }

        // ---------------------------------------------------------------- the end of the playback

        /// <summary>The playback ended (and the grace passed): the player's own layout comes back, gliding, unless they
        /// changed the stage meanwhile.</summary>
        void EndStage()
        {
            _stageEndPending = false;
            if (!_staged) return;
            FinishMotion();
            if (_held != null) EndGrab();
            if (ChangedByPlayer(out string why)) { LeaveStage(why); return; }
            Animate(BringReason.Take, () =>
            {
                _staged = false;
                _stageFar = false;
                _mode = _ownMode;
                _current = _ownCurrent;
                if (!SameSet(_ownSet))
                {
                    _set.Clear();
                    _set.AddRange(_ownSet);
                    SetVersion++;
                }
                _plan = _ownPlan;
                _seatHead = _ownSeatHead;
                _seatYaw = _ownSeatYaw;
                _tableAtAlternate = _ownTableAlt;
                ApplyMembership();
                // every rig that was at the seat back exactly where it stood (its riser follows by itself)
                for (int i = 0; i < _rigs.Count; i++)
                {
                    Rig r = _rigs[i];
                    int k = (int)r.Id;
                    if (r.Group == null || k < 0 || k >= _ownPose.Length || !_ownPose[k].Has || !_ownPose[k].AtSeat) continue;
                    if (r.Id != RigId.Table && !IsInScene(KindOf(r.Id))) continue;
                    r.Group.SetPositionAndRotation(_ownPose[k].Pos, _ownPose[k].Rot);
                }
                CollectAll();
                RefreshZones();
                RefreshDisplay();
            }, true);
            _stageTipWanted = false;
            StageRestores++;
            PublishLayout();
            Debug.Log($"[Layout] the playback ended: the player's own layout is back ({(_mode == LayoutMode.Single ? "One alone " + _current : "My set " + Names(_set))})");
        }

        /// <summary>The player changed what the stage left in front of them (the layout, an instrument's place or turn, the seat).</summary>
        bool ChangedByPlayer(out string why)
        {
            why = null;
            if (_mode != _stageMode || (_mode == LayoutMode.Single && _current != _stageCurrent) || !SameSet(_stageSet)) { why = "the layout changed"; return true; }
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig r = _rigs[i];
                int k = (int)r.Id;
                if (r.Group == null || k < 0 || k >= _stagePose.Length || !_stagePose[k].Has || !_stagePose[k].AtSeat) continue;
                if (r.Id != RigId.Table && !IsInScene(KindOf(r.Id))) { why = "an instrument left"; return true; }
                if (Vector3.Distance(r.Group.position, _stagePose[k].Pos) > StageMoveEps
                    || Quaternion.Angle(r.Group.rotation, _stagePose[k].Rot) > StageYawEps) { why = r.Id + " was moved"; return true; }
            }
            return false;
        }

        /// <summary>The player took the stage over (they changed it, or started recording with it): it stays, and from now on
        /// it is their layout (saved). <paramref name="ownSet"/>: their own My set comes back as the set (they chose a
        /// layout: One alone or My set means their own set, not the recording's); true is returned when the caller must put
        /// it back inside its own change (TakeOwnSet), after Animate has seen where everything was.</summary>
        bool LeaveStage(string why, bool ownSet = false)
        {
            if (!_staged) return false;
            bool far = StageFar;
            _staged = false;
            _stageFar = false;
            _stageEndPending = false;
            _stageTipWanted = false;
            StageKeeps++;
            bool takeSet = ownSet && !SameSet(_ownSet);
            Persist();
            if (!takeSet) PersistSet();
            Debug.Log($"[Layout] the player took over the playback's layout ({why}): it stays");
            // Taken over standing StageFarther out by REC or Move instruments (the paths that keep what is in front as it
            // stands; every other one lays the room out again): back at the playing distance.
            if (far && (why == "recording" || why == "Move instruments")) NearAgain();
            return takeSet;
        }

        readonly List<RigEntry> _showOthers = new List<RigEntry>(8);
        readonly List<Rig> _showRigs = new List<Rig>(MySetRules.Max);
        readonly float[] _showLeft = new float[MySetRules.Max], _showRight = new float[MySetRules.Max], _showNear = new float[MySetRules.Max], _showX = new float[MySetRules.Max];

        /// <summary>The show's arc (2026-11-18, headset: "make all the instruments more far so that all instrument will be
        /// visible to my eyes without rotating. may be 3 feets"): the staged instruments side by side on an arc about the
        /// head, each facing the player, their play centres StageFarther (0.9 m) beyond the farthest one's
        /// playing distance, or as far as needed for them all to fit inside ShowSpanDeg (90 deg left to right edge, inside the
        /// headset's ~96 deg view) with ShowGapDeg between them. Left stays left (ordered by where they stood).</summary>
        public const float ShowSpanDeg = 90f, ShowGapDeg = 4f, ShowRadiusMax = 2.8f;
        /// <summary>The show keeps this much air (m) between an instrument's floor boxes and the room's furniture: the boxes are the base, the harp's neck and the cymbals lean out over it.</summary>
        public const double ShowFurnitureGap = 0.35;

        /// <summary>The show (StageFar): every staged instrument on the show's arc (see ShowSpanDeg), facing the player; one
        /// that would stand in a wall, the furniture or another comes nearer along its own line (5 cm steps), never nearer
        /// than its playing distance + 0.3 m (else it stays where it was laid out). The side table keeps its place.</summary>
        void PushShowOut(Vector3 head, float yaw)
        {
            if (!StageFar) return;
            _showRigs.Clear();
            for (int i = 0; i < _rigs.Count && _showRigs.Count < MySetRules.Max; i++)
            {
                Rig r = _rigs[i];
                if (!IsRowRig(r.Id) || !IsInScene(KindOf(r.Id)) || r.Group == null) continue;
                RigEntry e = Placement.Entry(r);
                if (e == null || !PlacementSolver.Active(e) || !e.HasCentre || !e.HasBound) continue;
                _showRigs.Add(r);
            }
            int n = _showRigs.Count;
            if (n == 0) return;
            float yawDeg = yaw * Mathf.Rad2Deg;
            // where each stands now (seat x of its play centre, for the order), its distance, and its extent left / right of
            // its play centre when it faces the player straight ahead
            for (int i = 0; i < n; i++)
            {
                RigEntry e = Placement.Entry(_showRigs[i]);
                Placement.Pose(e);
                Vector3 c = PlacementSolver.CentreWorld(e) - head;
                LayoutMath.RotY(c.x, c.z, -yaw, out float sx, out float sz);
                _showX[i] = sx;
                _showNear[i] = Mathf.Sqrt(sx * sx + sz * sz);
                float a = _showRigs[i].HomeYawSingle * Mathf.Deg2Rad;
                LayoutMath.RotY(e.Bound.X - e.CentreLocal.x, e.Bound.Z - e.CentreLocal.z, a, out float bx, out _);
                float hw = Mathf.Abs(Mathf.Cos(a)) * e.Bound.Hx + Mathf.Abs(Mathf.Sin(a)) * e.Bound.Hz;
                _showLeft[i] = bx - hw;
                _showRight[i] = bx + hw;
            }
            for (int i = 1; i < n; i++)   // left to right (insertion sort, allocation-free)
                for (int k = i; k > 0 && _showX[k] < _showX[k - 1]; k--)
                {
                    (_showX[k], _showX[k - 1]) = (_showX[k - 1], _showX[k]);
                    (_showNear[k], _showNear[k - 1]) = (_showNear[k - 1], _showNear[k]);
                    (_showLeft[k], _showLeft[k - 1]) = (_showLeft[k - 1], _showLeft[k]);
                    (_showRight[k], _showRight[k - 1]) = (_showRight[k - 1], _showRight[k]);
                    (_showRigs[k], _showRigs[k - 1]) = (_showRigs[k - 1], _showRigs[k]);
                }
            float radius = 0f;
            for (int i = 0; i < n; i++) radius = Mathf.Max(radius, _showNear[i] + StageFarther);
            for (; radius < ShowRadiusMax && ArcSpan(n, radius) > ShowSpanDeg; radius += 0.05f) { }
            float span = ArcSpan(n, radius), edge = -span * 0.5f;
            _showOthers.Clear();
            for (int i = 0; i < Placement.Entries.Count; i++) if (PlacementSolver.Active(Placement.Entries[i])) _showOthers.Add(Placement.Entries[i]);
            for (int i = 0; i < n; i++)
            {
                _showX[i] = n == 1 ? 0f : edge - Mathf.Atan2(_showLeft[i], radius) * Mathf.Rad2Deg;   // its left edge on the running edge; one alone straight ahead
                edge = _showX[i] + Mathf.Atan2(_showRight[i], radius) * Mathf.Rad2Deg + ShowGapDeg;
            }
            float mid = (_showX[0] + _showX[n - 1]) * 0.5f;   // the play centres balanced about the forward line
            for (int i = 0; i < n; i++) PlaceOnArc(_showRigs[i], head, yaw, yawDeg, _showX[i] - mid, radius, _showNear[i]);
            RefreshZones();
        }

        /// <summary>Degrees the arc takes at <paramref name="radius"/>: every instrument's width plus the gaps.</summary>
        float ArcSpan(int n, float radius)
        {
            float span = ShowGapDeg * (n - 1);
            for (int i = 0; i < n; i++) span += (Mathf.Atan2(_showRight[i], radius) - Mathf.Atan2(_showLeft[i], radius)) * Mathf.Rad2Deg;
            return span;
        }

        void PlaceOnArc(Rig r, Vector3 head, float yaw, float yawDeg, float thetaDeg, float radius, float playing)
        {
            RigEntry e = Placement.Entry(r);
            Transform g = r.Group;
            Vector3 from = g.position;
            Quaternion fromRot = g.rotation;
            float a = r.HomeYawSingle * Mathf.Deg2Rad, t = thetaDeg * Mathf.Deg2Rad;
            LayoutMath.RotY(e.CentreLocal.x, e.CentreLocal.z, a, out float cx, out float cz);
            Quaternion rot = Quaternion.Euler(0f, yawDeg + r.HomeYawSingle + thetaDeg, 0f);
            for (float d = radius; d >= playing + 0.3f; d -= 0.05f)
            {
                // the straight-ahead pose with the play centre d ahead, turned by theta about the head
                LayoutMath.RotY(-cx, d - cz, t, out float px, out float pz);
                Vector3 p = LayoutMath.SeatPlace(head, yaw, new Vector3(px, from.y, pz));
                g.SetPositionAndRotation(p, rot);
                Placement.Pose(e);
                if (!Placement.Free(e, _showOthers, head, PlacementSolver.Gap) || ShowHitsFurniture(r)) continue;
                return;
            }
            g.SetPositionAndRotation(from, fromRot);   // nowhere clear on its line: where it was laid out
            Placement.Pose(e);
        }

        /// <summary>The instrument's floor boxes (its descriptor's, at its pose) come within ShowFurnitureGap of the room's furniture.</summary>
        bool ShowHitsFurniture(Rig r)
        {
            IReadOnlyList<SeatPlanner.FloorObb> furniture = _stow.Furniture;
            if (furniture == null || furniture.Count == 0) return false;
            _seatBoxes.Clear();
            AddSeatBoxes(r, KindOf(r.Id));
            for (int i = 0; i < _seatBoxes.Count; i++)
                for (int k = 0; k < furniture.Count; k++)
                    if (SeatPlanner.Overlaps(_seatBoxes[i], furniture[k], ShowFurnitureGap)) return true;
            return false;
        }

        /// <summary>The player plays along with the playback: records on top of it, records, or overdubs a loop.</summary>
        bool PlayerPlaysAlong()
        {
            IRecorder rec = _app.Get<IRecorder>();
            if (rec != null && (rec.RecordingOnTop || rec.State == RecorderState.Recording)) return true;
            ILooper loop = _app.Get<ILooper>();
            return loop != null && loop.Active && (loop.Status == LoopStatus.Armed || loop.Status == LoopStatus.Overdubbing || loop.Status == LoopStatus.RecordingFirst);
        }

        /// <summary>The player plays along: the staged instruments glide back to the playing distance (the stage goes on and the
        /// player's own layout still comes back when it ends).</summary>
        void BringStageNear()
        {
            _stageFar = false;
            StageNears++;
            NearAgain();
            Debug.Log("[Layout] the player plays along with the playback: its instruments come to the playing distance");
        }

        /// <summary>What is in front laid out again about the player's front at the playing distance, gliding.</summary>
        void NearAgain()
        {
            if (MrActive) return;   // never far there
            FinishMotion();
            if (_held != null) EndGrab();
            Animate(BringReason.Take, () =>
            {
                ReadFront(out Vector3 h, out float y);
                PlaceAt(h, y, true, settle: true);
                RefreshDisplay();
                if (_staged) RememberStage();   // what the stage leaves now (the player's changes are told from it)
            }, true);
        }

        /// <summary>The player's own My set back as the set (inside a layout change the player chose during a stage).</summary>
        void TakeOwnSet()
        {
            _set.Clear();
            _set.AddRange(_ownSet);
            SortByPriority(_set);
            SetVersion++;
            PersistSet();
            _plan = _planner.PlanSet(_set);
        }

        string Names(List<InstrumentKind> kinds)
        {
            var ids = new string[kinds.Count];
            for (int i = 0; i < kinds.Count; i++) ids[i] = _app.Registry?.IdOf(kinds[i]) ?? kinds[i].ToString();
            return string.Join(",", ids);
        }
    }
}
