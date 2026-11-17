// "Move instruments" (2026-11-10): ONE setting, On or Off, persisted (AppSettings.MoveInstruments), that replaces the
// gold grab knobs, the one-shot arrange mode and "Lock instruments". Off (the default) no pinch ever moves an instrument
// (InPlayGrab stays off; a still pinch on a body only tells Sunny, InstrumentBodyPinchEvent). On, the arrange mode of
// LayoutModule.ArrangeMode.cs is in effect: the instruments are quiet (App.TickInstruments), outlined (ArrangeVisuals:
// soft warm corner brackets and a floor ring, light gold on the one a hand is near, gold while held), and a pinch anywhere
// on one picks it up. The setting is the player's: the app suspends moving by itself while the headset is off, while mixed
// reality changes the room or places the piano, while an instrument glides in, and before the start panel is answered, and
// it comes back on by itself afterwards (TryResumeMove).
//
// The drag (why this and not the Interaction SDK's Grabbable / HandGrab: the project has no com.meta.xr.sdk.interaction
// package, and the app's hands are its own pipeline: HandFrame / PointerInput fed by MetaHandSource on the headset,
// FakeHandSource in the tests, the mouse on the desktop; a Grabbable needs colliders, an ISDK hand rig and a scene
// prefab per instrument, and its free 6-DoF transformers would have to be constrained back to "yaw on the floor" anyway.
// The custom drag below reads the same pinch everything else reads, keeps every placement rule, and is tested headless):
//   - pick-up: the grab pinch (thumb-index OR Meta's index pinch) on or within 8 cm of any part of an instrument (or on the
//     far end of a hand's ray) takes it after an 80 ms debounce; the drag filter starts on the hand's own point and the
//     grab offset is kept, so nothing jumps;
//   - follow: the pinch point through a One Euro filter (DragFilter: calm at rest, about a centimetre behind at 0.5 m/s,
//     never a rubber band) carries the instrument's footprint centre at the grab offset in x and z; yaw only, never
//     tilting or rolling;
//   - lift (2026-11-11, "allow it place instrument to their height/level"): the hand's own height, through its own One
//     Euro filter, once it has moved 2 cm (a level drag never bobs), lifts or lowers it 1:1 inside its range (Seat.LiftMin / LiftMax about its
//     home: the piano's keys 0.55-1.10 m on a stand that stretches to the floor, every other instrument from the floor up
//     to a standing height on a riser, RiserVisuals); a ray grab lifts with the hand, never with the ray's far end;
//   - spin: the wrist's unwrapped heading, filtered, past a soft 6 degree dead band, turns it TurnGain (2) times as far about
//     its own centre, so a comfortable 90 degree wrist turn spins it about 170 degrees and a second grab (or two hands)
//     goes all the way round; both hands on it twist it 1:1 about its centre;
//   - hold: the hysteresis of the pinch (a loosened pinch still holds; it lets go 0.12 s after the pinch really opened; a
//     tracking gap up to 0.3 s keeps it waiting where it is and the hand picks it up again where it is, re-anchored);
//   - drop: it stays where it was let go, then moves the shortest way out of anything it ended in (another instrument, a
//     wall, the seat, the eyes) in a gentle 0.25 s ease-out settle; its offset from its default pose is kept (ComfortCodec).
// The rules of every grab still hold while it moves: PlacementSolver.Constrain (stops and slides at contact, through after
// 300 ms with a red ring) and the room clamp.
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Layout
{
    public sealed partial class LayoutModule
    {
        // ---------------------------------------------------------------- the setting

        bool _moveWanted;
        /// <summary>The player is in the room: the start panel was answered this session (or Move instruments was turned on by
        /// hand), so a saved "on" may take effect.</summary>
        bool _startAnswered;
        /// <summary>The headset is on (HmdMountEvent): moving waits while it is off.</summary>
        bool _mounted = true;

        /// <summary>The "Move instruments" setting (persisted). Arranging says whether it is in effect right now.</summary>
        public bool MoveOn => _moveWanted;
        /// <summary>Times moving came back on by itself (after the headset, mixed reality, a glide, the start panel).</summary>
        public int MoveResumes { get; private set; }

        /// <summary>Init: the old Lock setting is cleaned up, then the player's choice is read.</summary>
        void LoadMoveSetting()
        {
            ComfortCodec.MigrateMoveSetting(_app.Settings);
            _moveWanted = _app.Settings.MoveInstruments;
        }

        void PersistMove(bool on)
        {
            if (_moveWanted == on && _app.Settings.MoveInstruments == on) return;
            _moveWanted = on;
            _app.Settings.MoveInstruments = on;
            _app.Settings.Save();
            Debug.Log($"[Layout] Move instruments {(on ? "on" : "off")}");
        }

        /// <summary>The setting is on but moving was suspended (or has not started this session): it starts again once the
        /// player is in the room, the headset is on, nothing glides or turns and mixed reality is not placing the piano.</summary>
        void TryResumeMove()
        {
            if (!_moveWanted || _arranging || !_startAnswered || !_mounted || MrPlacing || IsTurning || AnyMoving) return;
            if (!StartMoving()) return;
            MoveResumes++;
        }

        // ---------------------------------------------------------------- the drag

        readonly DragFilter _drag = new DragFilter();
        float _wristAcc, _turnWant, _releaseT, _lostT;
        bool _moveDrag, _dragPaused;

        /// <summary>An instrument follows a hand in Move instruments (the filtered drag).</summary>
        public bool MoveDragging => _moveDrag && _held != null;
        /// <summary>Hand drags started in Move instruments since boot.</summary>
        public int MoveDrags { get; private set; }

        /// <summary>The drag holds (the end side of the pinch hysteresis): the grab pinch, or a loosened pinch (thumb and index
        /// tips within HoldGap, or Meta's index pinch strength at HoldStrength or more).</summary>
        static bool HoldsDrag(HandState h, HandFrame f)
        {
            if (h.GrabPinching) return true;
            if (f == null || !f.isTracked) return false;
            return HandMath.PinchGap(f) < MoveConstants.HoldGap || h.PinchStrength >= MoveConstants.HoldStrength;
        }

        /// <summary>A hand picks <paramref name="rig"/> up at <paramref name="at"/> (its pinch point, or the far end of its
        /// ray) with its pinch point at height <paramref name="handY"/>: the filters start exactly there, so the first frame
        /// of the drag is where the grab began.</summary>
        void StartMoveDrag(Rig rig, Vector3 at, HandState h, float handYaw, float handY)
        {
            FinishSettle();
            StartGrab(rig, at, h, handYaw);
            _moveDrag = true;
            _drag.Reset(at, 0f);
            BeginLift(rig, handY);
            _wristAcc = _turnWant = _releaseT = _lostT = 0f;
            _dragPaused = false;
            MoveDrags++;
        }

        /// <summary>One frame of a one-hand drag: the filtered point carries the footprint centre at the grab offset (x, z;
        /// the height stays), the filtered wrist past its dead band spins it TurnGain times about that centre (yaw only).
        /// Then the room clamp and the constraint as every grab; a refused turn slips (the wrist carries on from the angle
        /// the instrument is at).</summary>
        void StepMoveDrag(Vector3 target, float handY, float handYaw, float dt)
        {
            Transform g = _held.Group;
            float px = g.position.x, pz = g.position.z, pyaw = _curYaw;
            Vector3 s = _drag.StepPoint(target, dt);
            _wristAcc += LayoutMath.Wrap(handYaw - _yawPrev);
            _yawPrev = handYaw;
            float want = MoveConstants.TurnGain * DragFilter.SoftDeadband(_drag.StepYaw(_wristAcc, dt), MoveConstants.TurnDeadband);
            _turn += want - _turnWant;
            _turnWant = want;
            PlacePivotAt(g, s.x + _pivotOff.x, s.z + _pivotOff.z, _yaw0 + _turn);
            StepLift(handY, dt);
            ClampRoom(g);
            ConstrainGrab(px, pz, pyaw);
            _turn = _curYaw - _yaw0;
            PlaceRing();
            RefreshHeldZone();
        }

        /// <summary>Both hands on it: the filtered midpoint carries it (and lifts it), the line between the pinches twists it
        /// 1:1 about its centre (unwrapped: on past half a turn). Yaw only.</summary>
        void StepMoveTwoHand(Vector3 pA, Vector3 pB, float dt)
        {
            Transform g = _held.Group;
            float px = g.position.x, pz = g.position.z, pyaw = _curYaw;
            float a = Mathf.Atan2(pB.x - pA.x, pB.z - pA.z);
            _pairTurn += LayoutMath.Wrap(a - _pairPrev);
            _pairPrev = a;
            Vector3 s = _drag.StepPoint((pA + pB) * 0.5f, dt);
            PlacePivotAt(g, s.x + _pivotOff.x, s.z + _pivotOff.z, _pairYaw0 + _pairTurn);
            StepLift((pA.y + pB.y) * 0.5f, dt);
            ClampRoom(g);
            ConstrainGrab(px, pz, pyaw);
            _pairTurn = _curYaw - _pairYaw0;
            PlaceRing();
            RefreshHeldZone();
        }

        /// <summary>The hand came back after a loosened pinch or a tracking gap (or the second hand let go): the drag carries
        /// on from where the instrument waited and where the hand is now (the grab offset taken again), so nothing jumps.</summary>
        void ReanchorMoveDrag(Vector3 target, float handYaw, float handY)
        {
            _dragPaused = false;
            BeginLift(_held, handY);
            Transform g = _held.Group;
            Vector3 pw = PivotWorld(g);
            _pivotOff = new Vector3(pw.x - target.x, 0f, pw.z - target.z);
            _yaw0 = _curYaw;
            _turn = 0f;
            _yawPrev = handYaw;
            _wristAcc = _turnWant = 0f;
            _drag.Reset(target, 0f);
        }

        // ---------------------------------------------------------------- the lift

        OneEuro _liftF;
        float _liftHand0, _liftY0, _liftLo, _liftHi;
        bool _liftOn, _lifted;

        /// <summary>The held instrument's height range (world) and the lift's anchor: the hand's height now and the
        /// instrument's; the lift filter starts on the hand (no jump).</summary>
        void BeginLift(Rig rig, float handY)
        {
            _liftOn = _lifted = false;
            if (rig == null || rig.Group == null) return;
            _liftY0 = rig.Group.position.y;
            _liftHand0 = handY;
            _liftF.Reset(handY);
            LiftRangeHere(rig, out _liftLo, out _liftHi);
            _liftOn = _liftHi - _liftLo > 1e-3f && !OnRealTable(rig);
        }

        /// <summary>One frame of the lift: nothing until the hand's filtered height has moved LiftDeadband from where it took
        /// hold (a level drag never bobs); from then on 1:1 (the anchor moved by the band, so it starts without a jump),
        /// inside the range: pushed past an end it stays there and comes back the moment the hand does (the anchor follows the
        /// excess). An instrument already outside its range may stay where it is, never go further out.</summary>
        void StepLift(float handY, float dt)
        {
            if (!_liftOn || _held == null) return;
            float fy = _liftF.Step(handY, dt, MoveConstants.SmoothMinCutoff, MoveConstants.SmoothBeta, MoveConstants.SmoothDerivCutoff);
            float raw = fy - _liftHand0;
            if (!_lifted)
            {
                if (Mathf.Abs(raw) <= MoveConstants.LiftDeadband) return;
                _lifted = true;
                _liftHand0 += raw > 0f ? MoveConstants.LiftDeadband : -MoveConstants.LiftDeadband;
                raw = fy - _liftHand0;
            }
            float lo = Mathf.Min(_liftLo, _liftY0), hi = Mathf.Max(_liftHi, _liftY0);
            float y = _liftY0 + raw;
            if (y > hi) { _liftHand0 += y - hi; y = hi; }
            else if (y < lo) { _liftHand0 -= lo - y; y = lo; }
            Transform g = _held.Group;
            Vector3 p = g.position;
            if (Mathf.Abs(y - p.y) > 1e-6f) g.position = new Vector3(p.x, y, p.z);
        }

        // ---------------------------------------------------------------- the settle after a drop

        Rig _settleRig;
        Vector3 _settleFrom, _settleTo;
        Quaternion _settleFromRot, _settleToRot;
        float _settleT;

        /// <summary>A dropped instrument is gliding to its free spot.</summary>
        public bool Settling => _settleRig != null;
        /// <summary>Drops that needed a settle since boot.</summary>
        public int Settles { get; private set; }

        /// <summary>EndGrab of a drag: the instrument already stands at its settled spot; it is put back where the hand let
        /// go and eases there (silent until it arrives).</summary>
        void StartSettle(Rig rig, Vector3 from, Quaternion fromRot)
        {
            Transform g = rig.Group;
            if (g == null || ((g.position - from).sqrMagnitude <= 1e-6f && Quaternion.Angle(g.rotation, fromRot) <= 0.1f)) return;
            _settleRig = rig;
            _settleFrom = from;
            _settleFromRot = fromRot;
            _settleTo = g.position;
            _settleToRot = g.rotation;
            _settleT = 0f;
            g.SetPositionAndRotation(from, fromRot);
            rig.Held = true;
            Settles++;
        }

        /// <summary>LayoutAnimation: the short settle after a drop (ease out).</summary>
        void TickSettle(float dt)
        {
            if (_settleRig == null) return;
            _settleT += dt;
            float k = Mathf.Clamp01(_settleT / MoveConstants.SettleSeconds);
            if (_settleRig.Group == null || k >= 1f) { FinishSettle(); return; }
            float e = 1f - (1f - k) * (1f - k);
            _settleRig.Group.SetPositionAndRotation(Vector3.Lerp(_settleFrom, _settleTo, e), Quaternion.Slerp(_settleFromRot, _settleToRot, e));
        }

        /// <summary>The settle ends now, at its spot (a new grab, an arrangement).</summary>
        void FinishSettle()
        {
            Rig r = _settleRig;
            if (r == null) return;
            _settleRig = null;
            if (r.Group != null) r.Group.SetPositionAndRotation(_settleTo, _settleToRot);
            if (_held != r) r.Held = false;
            RigEntry e = Placement.Entry(r);
            if (e != null) { RefreshZone(e); Placement.Measure(e); }
        }
    }
}
