// "Move instruments" for the instruments on display in the VR room (platform.md 4.1): taken by the ray as u-arrange's ray
// grab (the hit distance kept, pushing the hand along the ray moves it 2.5 times as far) or by a near pinch, a floor
// instrument slides on the floor, a wall one along its wall (its height and its distance to the wall kept) and a surface
// one stays on its surface (the furniture top it rests on). Its heading stays. A position is taken only when every
// displayed box is inside the room and 3 cm clear of the furniture, of the other displayed instruments and of the seat's
// instruments (the side table included), with its play centre 1.1 m or more from the head; otherwise the instrument
// stays at the last good position (red brackets). The release is saved as musicroom.stow.<id> ("x,z,yaw" in the room).
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Layout
{
    public sealed partial class LayoutModule
    {
        /// <summary>A displayed instrument moved in arrange mode keeps its play centre this far from the head (m).</summary>
        public const float DisplayMinHead = 1.1f;

        Rig _displayRig;
        HandState _displayHand;
        bool _displayRay, _displayBlocked;
        float _displayD0;
        Vector3 _displayO0, _displayDir0, _displayFrom, _displayTarget;
        Vector2 _displayOffset;
        StowPlan.Spot _displayStart;
        bool _hasSupport;
        SeatPlanner.FloorObb _support;
        readonly List<SeatPlanner.FloorObb> _dragBoxes = new List<SeatPlanner.FloorObb>(8), _dragOthers = new List<SeatPlanner.FloorObb>(64);

        /// <summary>The displayed instrument held in arrange mode (null when none).</summary>
        public Rig DisplayHeld => _displayRig;
        public bool DisplayBlocked => _displayBlocked;
        public int DisplayMoves { get; private set; }

        readonly Vector3[] _conePoints = new Vector3[InstrumentIds.KindCount];
        readonly Rig[] _coneRigs = new Rig[InstrumentIds.KindCount];

        /// <summary>The cone pick (StowPlan.Pick) over the displayed instruments that arrange mode may move: the rig and the
        /// distance along the ray to its pick point.</summary>
        bool ConePickDisplayed(in Ray ray, out Rig rig, out float t)
        {
            rig = null;
            t = 0f;
            int n = 0;
            for (int i = 0; i < _rigs.Count && n < _conePoints.Length; i++)
            {
                Rig r = _rigs[i];
                if (!IsRowRig(r.Id) || !Arrangeable(r) || r.InScene) continue;
                if (!TryGetDisplayPoint(KindOf(r.Id), out Vector3 p)) continue;
                _conePoints[n] = p;
                _coneRigs[n++] = r;
            }
            int k = StowPlan.Pick(ray.origin, ray.direction, _conePoints, n, out _, out _);
            if (k < 0) return false;
            rig = _coneRigs[k];
            t = Mathf.Max(ArrangeConstants.RayMinDistance, Vector3.Dot(_conePoints[k] - ray.origin, ray.direction));
            return true;
        }

        void BeginDisplayDrag(Rig r, HandState h, Ray ray, float rayT, Vector3 at)
        {
            InstrumentKind k = KindOf(r.Id);
            StowPlan.Spot s = _shownAt[(int)k];
            if (s == null) return;
            _displayRig = r;
            _displayHand = h;
            _displayRay = rayT > 0f;
            _displayD0 = rayT;
            _displayO0 = ray.origin;
            _displayDir0 = ray.direction;
            _displayFrom = ray.origin;
            _displayTarget = at;
            _displayOffset = new Vector2((float)s.X - at.x, (float)s.Z - at.z);
            _displayStart = s;
            _displayBlocked = false;
            _hasSupport = s.Mount == StowMount.Surface && FindSupport(s, out _support);
            r.Held = true;
        }

        /// <summary>The furniture top a surface instrument rests on: a blocker whose top is at its lowest box (within 2 cm) and
        /// that holds its pose.</summary>
        bool FindSupport(StowPlan.Spot s, out SeatPlanner.FloorObb support)
        {
            support = default;
            double low = double.PositiveInfinity;
            for (int i = 0; i < s.Boxes.Length; i++) low = System.Math.Min(low, s.Boxes[i].Y0);
            IReadOnlyList<SeatPlanner.FloorObb> f = _stow.Furniture;
            for (int i = 0; i < f.Count; i++)
            {
                SeatPlanner.FloorObb o = f[i];
                if (System.Math.Abs(o.Y1 - low) > 0.02) continue;
                SeatPlanner.Extent(o, out double x0, out double x1, out double z0, out double z1);
                if (s.X < x0 || s.X > x1 || s.Z < z0 || s.Z > z1) continue;
                support = o;
                return true;
            }
            return false;
        }

        void MoveDisplayDrag(HandFrame f, Vector3 pp)
        {
            if (_displayRig == null) return;
            Vector3 target = pp;
            if (_displayRay && f.pointerValid)
            {
                Ray r = PointerRay(f);
                float d = _displayD0 + (ArrangeConstants.RayPushGain - 1f) * Vector3.Dot(r.origin - _displayO0, _displayDir0);
                d = Mathf.Clamp(d, ArrangeConstants.RayMinDistance, ArrangeConstants.RayMaxLength);
                _displayFrom = r.origin;
                target = r.origin + r.direction * d;
            }
            else if (_displayRay) target = _displayTarget;
            _displayTarget = target;
            StowPlan.Spot s0 = _displayStart;
            double x = target.x + _displayOffset.x, z = target.z + _displayOffset.y;
            if (s0.Mount == StowMount.Wall)
            {
                // along its wall: the coordinate across the nearest wall is kept
                double toSide = SeatPlanner.RoomX1 - System.Math.Abs(s0.X);
                double toEnd = System.Math.Min(SeatPlanner.RoomZ1 - s0.Z, s0.Z - SeatPlanner.RoomZ0);
                if (toSide <= toEnd) x = s0.X; else z = s0.Z;
            }
            if (_hasSupport)
            {
                // on its surface: the target is held to the furniture top (every box's centre over it)
                SeatPlanner.Extent(_support, out double sx0, out double sx1, out double sz0, out double sz1);
                double lox = double.NegativeInfinity, hix = double.PositiveInfinity, loz = double.NegativeInfinity, hiz = double.PositiveInfinity;
                var at = new SeatPlanner.Pose2(0.0, 0.0, s0.Yaw);
                for (int i = 0; i < s0.Boxes.Length; i++)
                {
                    SeatPlanner.ToWorld(at, s0.Boxes[i].Cx, s0.Boxes[i].Cz, out double ox, out double oz);
                    lox = System.Math.Max(lox, sx0 - ox); hix = System.Math.Min(hix, sx1 - ox);
                    loz = System.Math.Max(loz, sz0 - oz); hiz = System.Math.Min(hiz, sz1 - oz);
                }
                if (lox <= hix) x = System.Math.Max(lox, System.Math.Min(hix, x));
                if (loz <= hiz) z = System.Math.Max(loz, System.Math.Min(hiz, z));
            }
            StowPlan.Spot c = s0.MovedTo(x, z, s0.Yaw);
            if (TryDisplayAt(c))
            {
                _shownAt[(int)KindOf(_displayRig.Id)] = c;
                _displayRig.Group.SetPositionAndRotation(new Vector3((float)c.X, (float)c.Y, (float)c.Z), Quaternion.Euler(0f, (float)c.Yaw, 0f));
                _displayBlocked = false;
            }
            else _displayBlocked = true;
        }

        /// <summary>The rules of a displayed instrument's place in arrange mode (see the file header).</summary>
        bool TryDisplayAt(StowPlan.Spot c)
        {
            _dragBoxes.Clear();
            c.WorldBoxes(_dragBoxes);
            Vector3 head = _app.Head != null ? _app.Head.position : OriginSeat;
            if (c.Distance(head.x, head.z) < DisplayMinHead) return false;
            for (int i = 0; i < _dragBoxes.Count; i++)
            {
                SeatPlanner.FloorObb b = _dragBoxes[i];
                if (!SeatPlanner.InsideRoom(b)) return false;
                if (_hasSupport)
                {
                    // on its surface: every box's centre over the furniture top (it may overhang its edge a little, as the
                    // bowls' cushions do the sill's)
                    SeatPlanner.Extent(_support, out double sx0, out double sx1, out double sz0, out double sz1);
                    if (b.Cx < sx0 || b.Cx > sx1 || b.Cz < sz0 || b.Cz > sz1) return false;
                }
            }
            IReadOnlyList<SeatPlanner.FloorObb> furniture = _stow.Furniture;
            for (int i = 0; i < _dragBoxes.Count; i++)
                for (int k = 0; k < furniture.Count; k++)
                    if (SeatPlanner.Overlaps(_dragBoxes[i], furniture[k], StowPlan.Gap)) return false;
            // the other displayed instruments where they are now, and the seat's instruments
            _dragOthers.Clear();
            InstrumentKind self = KindOf(_displayRig.Id);
            for (int i = 0; i < _shownAt.Length; i++)
                if (i != (int)self && _shownAt[i] != null && PlaceOf((InstrumentKind)i) == InstrumentPlace.Display) _shownAt[i].WorldBoxes(_dragOthers);
            for (int i = 0; i < _dragBoxes.Count; i++)
            {
                for (int k = 0; k < _dragOthers.Count; k++) if (SeatPlanner.Overlaps(_dragBoxes[i], _dragOthers[k], StowPlan.Gap)) return false;
                for (int k = 0; k < _seatBoxes.Count; k++) if (SeatPlanner.Overlaps(_dragBoxes[i], _seatBoxes[k], StowPlan.Gap)) return false;
            }
            return true;
        }

        void EndDisplayDrag(bool save)
        {
            if (_displayRig == null) return;
            Rig r = _displayRig;
            InstrumentKind k = KindOf(r.Id);
            StowPlan.Spot s = _shownAt[(int)k];
            _displayRig = null;
            _displayHand = null;
            _displayRay = false;
            _displayBlocked = false;
            r.Held = false;
            if (save && !_noSave && !MrActive && s != null && _spots[(int)k] != null && _app.Registry != null)
            {
                bool moved = System.Math.Abs(s.X - _displayStart.X) > 1e-4 || System.Math.Abs(s.Z - _displayStart.Z) > 1e-4;
                if (moved)
                {
                    _spots[(int)k] = _spots[(int)k].MovedTo(s.X, s.Z, s.Yaw);
                    _spots[(int)k].RoomPose(out double rx, out double rz, out double ryaw);   // saved in room coordinates
                    _app.Settings.SetStow(_app.Registry.IdOf(k), FormatStow(rx, rz, ryaw));
                    _app.Settings.Save();
                    DisplayMoves++;
                }
            }
            RefreshDisplay();
        }
    }
}
