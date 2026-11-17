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

        bool _staged, _stageEndPending, _stageTipDone, _stageTipWanted;
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
            if (here)
            {
                if (_staged) _stageKinds = kinds;
                return;   // every instrument it plays is in front already: nothing moves
            }
            if (!_staged) KeepOwnLayout();
            FinishMotion();
            if (_held != null) EndGrab();
            _staged = true;   // before the apply: nothing of the stage is saved (Persist / PersistSet)
            _stageKinds = kinds;
            Animate(BringReason.Take, () =>
            {
                if (_stageFit.Count == 1)
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
                RefreshDisplay();
                RememberStage();
            }, true);
            Stagings++;
            if (!_stageTipDone) { _stageTipWanted = true; _stageTipAt = _stageTime + 0.5f; }
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
            _staged = false;
            _stageEndPending = false;
            _stageTipWanted = false;
            StageKeeps++;
            bool takeSet = ownSet && !SameSet(_ownSet);
            Persist();
            if (!takeSet) PersistSet();
            Debug.Log($"[Layout] the player took over the playback's layout ({why}): it stays");
            return takeSet;
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
