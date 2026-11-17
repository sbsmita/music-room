// The puja (the bowl stick) as a hand tool (bowls.md 3.9; platform 4.6 IHandToolProvider in phase B), following the
// drumsticks' rules (Drums/DrumSticks.cs): a grab pinch (HandState.GrabPinchStart: the fingertip rule OR Meta's
// own pinch, which matters because the OpenXR thumb and index tip joints sit ~17 mm apart even when the tips touch) near
// the resting stick picks it up; it sits in the hand at the pinch point along the hand's line with the sticks' 12.6 deg
// droop, suede end forward; it is let go only after the hand stayed open for 0.25 s (the pinch parted AND Meta's pinch
// off AND no fist), rides out 1.5 s of tracking loss, and settles back onto the stand in 0.4 s. The grip, the settle
// easing and the shaft distance are Core's shared HandToolMath (the kit's sticks and the marimba's mallets use it too).
// Plain C# with Unity value types; no GameObjects. BowlsStickTool makes it the platform's hand tool (its two pointers);
// the visuals take PoseRigLocal; the hand logic takes the tool ends.
// Smooth stick (Smooth, on): the hand tracker's fast-motion rate is jittery (2-3 mm on the wrist and the pinch joints, so
// the 15 cm stick shook by several millimetres a frame at its ends: "the stick is not rotating smoothly", and the shaft's
// rub point jumped on the rim). The grip and the axis go through a One Euro filter (MIN_HZ when still, opening by BETA Hz per
// m/s of the grip's speed, so a strike still follows): the stick starts in the hand where it was picked up (no lag from the
// stand). While the shaft rubs a bowl the drawn stick also rides the rim (SetRim: the hand logic's surface point less its
// shaft point, eased in and out over RIM_TAU, so it neither snaps on nor passes into the bronze); the ends the hand logic
// reads stay the filtered hand's, so the depth still sets the force.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Bowls
{
    public enum BowlsPick : byte { None = 0, Near, Grabbed }

    public sealed class BowlsPuja
    {
        /// <summary>The pinch point within this of the resting shaft picks it up; within DeadZone the pinch is consumed.</summary>
        public const float GrabRadius = 0.05f, DeadZone = 0.10f;
        public const float OpenDistance = 0.05f, OpenDwell = 0.25f, LostGrace = 1.5f, SettleRate = 2.5f, TiltRad = -0.22f;
        /// <summary>Smooth stick: the One Euro filter's cutoff when still (Hz), its opening per m/s of grip speed (Hz), the
        /// speed estimate's cutoff (Hz); the drawn stick's ease onto and off the rim (s) and the most it moves (m).</summary>
        public const float MIN_HZ = 1.5f, BETA = 25f, D_HZ = 6f, RIM_TAU = 0.06f, RIM_MAX = 0.03f;

        /// <summary>The smooth stick (see the header); off = the raw grip every frame.</summary>
        public bool Smooth = true;
        bool _fresh;
        Vector3 _fGrip, _fAxis, _dGrip, _rawPrev;
        Vector3 _rim;   // world, the drawn stick's offset onto the rim

        public int HeldBy { get; private set; } = -1;
        public bool Held => HeldBy >= 0;
        public float Settle { get; private set; } = 1f;
        public bool AtSeat = true;
        float _openFor, _lostFor;
        Vector3 _grip, _axis;           // world, while held
        Vector3 _fromPos; Quaternion _fromRot;   // world pose at release (settling from there)
        Matrix4x4 _rigToWorld = Matrix4x4.identity;

        /// <summary>The rig's pose (the stand), each frame before use.</summary>
        public void SetRig(Matrix4x4 rigToWorld) => _rigToWorld = rigToWorld;

        static Vector3 Ax => Vector3.right;   // the puja's own axis: plain end (x = 0) to suede end (x = 0.15)

        Matrix4x4 RestWorld => _rigToWorld * BowlsGeometry.PujaRestMatrix();

        /// <summary>The world pose (the puja's own frame) now: held, settling, or at rest.</summary>
        public Matrix4x4 PoseWorld
        {
            get
            {
                if (Held)
                {
                    Vector3 plain = _grip - _axis * (BowlsLayout.PujaLength - BowlsLayout.PujaGrip);
                    return Matrix4x4.TRS(plain, Quaternion.FromToRotation(Ax, _axis), Vector3.one);
                }
                Matrix4x4 rest = RestWorld;
                if (Settle >= 1f) return rest;
                float k = HandToolMath.SettleEase(Settle);
                Vector3 rp = rest.GetColumn(3); Quaternion rr = rest.rotation;
                return Matrix4x4.TRS(Vector3.Lerp(_fromPos, rp, k), Quaternion.Slerp(_fromRot, rr, k), Vector3.one);
            }
        }

        /// <summary>For BowlsVisuals.SetPujaPose: the pose in the rig frame, or null at rest (the static vertex range).</summary>
        public Matrix4x4? PoseRigLocal => Held || Settle < 1f ? _rigToWorld.inverse * DrawnPoseWorld : (Matrix4x4?)null;

        /// <summary>The drawn stick (world): PoseWorld moved onto the rim while the shaft rubs (SetRim).</summary>
        public Matrix4x4 DrawnPoseWorld => Held && _rim != Vector3.zero ? Matrix4x4.Translate(_rim) * PoseWorld : PoseWorld;

        /// <summary>The drawn stick's rim offset now (world).</summary>
        public Vector3 RimOffset => _rim;

        /// <summary>Each frame after the hand logic: onRim with the offset (world) that puts the shaft's rub point on the
        /// bowl's surface, or off; the drawn stick eases toward it over RIM_TAU (a step never shows).</summary>
        public void SetRim(bool onRim, Vector3 offset, float dt)
        {
            if (!Held) { _rim = Vector3.zero; return; }
            if (offset.sqrMagnitude > RIM_MAX * RIM_MAX) offset = offset.normalized * RIM_MAX;
            Vector3 target = onRim ? offset : Vector3.zero;
            float k = dt > 0f ? 1f - Mathf.Exp(-dt / RIM_TAU) : 0f;
            _rim += (target - _rim) * k;
            if (!onRim && _rim.sqrMagnitude < 1e-10f) _rim = Vector3.zero;
        }

        /// <summary>The suede end's and the plain end's centres (world): BowlsHandInput.ToolA / ToolB while held.</summary>
        public void Ends(out Vector3 suede, out Vector3 plain)
        {
            Matrix4x4 m = PoseWorld;
            suede = m.MultiplyPoint3x4(new Vector3(BowlsLayout.PujaLength - 0.004f, 0, 0));
            plain = m.MultiplyPoint3x4(new Vector3(0.004f, 0, 0));
        }

        public Vector3 GripWorld => DrawnPoseWorld.MultiplyPoint3x4(BowlsGeometry.PujaGripLocal);

        /// <summary>IHandToolProvider.NearestFree: metres from a point to the resting shaft, +inf when held or not at the seat.</summary>
        public float NearestFree(Vector3 point)
        {
            if (Held || !AtSeat) return float.PositiveInfinity;
            Matrix4x4 m = RestWorld;
            return HandToolMath.SegmentDistance(m.MultiplyPoint3x4(Vector3.zero), m.MultiplyPoint3x4(new Vector3(BowlsLayout.PujaLength, 0, 0)), point);
        }

        /// <summary>IHandToolProvider.TryPickUp (the drums' rule, sized to a 150 mm stick on a crowded stand).</summary>
        public BowlsPick TryPickUp(int hand, bool handAlreadyHolding, Vector3 pinchPoint)
        {
            if (handAlreadyHolding || Held || !AtSeat) return BowlsPick.None;
            float d = NearestFree(pinchPoint);
            if (d > GrabRadius) return d < DeadZone ? BowlsPick.Near : BowlsPick.None;
            HeldBy = hand; _openFor = 0; _lostFor = 0; Settle = 1f; _fresh = true; _rim = Vector3.zero;
            return BowlsPick.Grabbed;
        }

        /// <summary>The held stick follows the hand (the drumsticks' pose and release update). Returns true when it was let go.</summary>
        public bool UpdateHeld(float dt, bool tracked, Vector3 wrist, Quaternion wristRot, Vector3 thumbTip, Vector3 indexTip, bool grabPinching, bool grasping)
        {
            if (!Held) { if (Settle < 1f) Settle = Mathf.Min(1f, Settle + dt * SettleRate); return false; }
            if (!tracked)
            {
                _lostFor += dt;
                if (_lostFor > LostGrace) { Release(); return true; }
                return false;
            }
            _lostFor = 0;
            StickGrip(wrist, wristRot, thumbTip, indexTip, out Vector3 grip, out Vector3 axis);
            if (!Smooth || _fresh || dt <= 0f)
            {
                if (_fresh || !Smooth) { _fGrip = grip; _fAxis = axis; _dGrip = Vector3.zero; _rawPrev = grip; }
                _fresh = false;
            }
            else
            {   // One Euro: the speed (its own low-pass) opens the cutoff; the axis takes the grip's cutoff
                Vector3 dRaw = (grip - _rawPrev) / dt; _rawPrev = grip;
                _dGrip += (dRaw - _dGrip) * Alpha(D_HZ, dt);
                float a = Alpha(MIN_HZ + BETA * _dGrip.magnitude, dt);
                _fGrip += (grip - _fGrip) * a;
                Vector3 ax = _fAxis + (axis - _fAxis) * a;
                _fAxis = ax.sqrMagnitude > 1e-12f ? ax.normalized : axis;
            }
            _grip = _fGrip; _axis = _fAxis;
            float gap = Vector3.Distance(thumbTip, indexTip);
            _openFor = gap > OpenDistance && !grasping && !grabPinching ? _openFor + dt : 0f;
            if (_openFor > OpenDwell) { Release(); return true; }
            return false;
        }

        static float Alpha(float hz, float dt) { float tau = 1f / (2f * Mathf.PI * hz); return 1f / (1f + tau / dt); }

        /// <summary>IHandToolProvider.ReleaseAll / the rig leaving the seat: back onto the stand (settling 0.4 s).</summary>
        public void Release()
        {
            if (!Held) return;
            Matrix4x4 m = DrawnPoseWorld;
            _fromPos = m.GetColumn(3); _fromRot = m.rotation;
            HeldBy = -1; Settle = 0f; _rim = Vector3.zero;
        }

        /// <summary>E3 contact shadows: the suede end and the shaft's lowest point (centre, radius).</summary>
        public int ShadowSpheres(System.Span<Vector4> into)
        {
            if (into.Length < 2 || !Held) return 0;
            Ends(out Vector3 s, out Vector3 p);
            s += _rim; p += _rim;   // the drawn stick's
            into[0] = new Vector4(s.x, s.y, s.z, BowlsLayout.PujaSuedeRadius);
            Vector3 low = s.y < p.y ? s : p;
            into[1] = new Vector4(low.x, low.y, low.z, BowlsLayout.PujaRadius);
            return 2;
        }

        /// <summary>HandToolMath.Grip with the sticks' droop: the grip at the thumb-index midpoint, the axis along the hand's
        /// line, 12.6 deg toward the palm.</summary>
        public static void StickGrip(Vector3 wrist, Quaternion wristRot, Vector3 thumbTip, Vector3 indexTip, out Vector3 grip, out Vector3 axis)
            => HandToolMath.Grip(wrist, wristRot, thumbTip, indexTip, TiltRad, out grip, out axis);
    }
}
