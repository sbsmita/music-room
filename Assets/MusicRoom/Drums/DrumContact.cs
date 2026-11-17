// 10-upgrade/experience.md for the kit: the contact surface (section 2.1) the visual finger clamp (E1) and the contact
// shadows (E3) query, the drawn sticks that pivot and rebound in the hand (E2, section 4), and the drawn stick tips Core
// shadows (IDrumsApi.StickShadowSpheres). All visual: the hit test keeps the raw tip and shoulder pointers.
//   Surface: piece-local hypot(x, z) <= r (the pad: its radius) at the height the surface is DRAWN: the batter head and
//   the pad top 1 mm above the piece's y = 0 (DrumSpec.HeadLift), a cymbal's plate (piece.Top, at its CURRENT tilt)
//   along its lathe profile (DrumSpec.CymbalTopY: 6 mm up on the bow, 16 mm on the bell). The hit test keeps the y = 0
//   strike planes. Use Both for the heads and the pad; Shadow only for the cymbals (a hand on a
//   cymbal is the choke gesture and must not be visually lifted), and only up to 1 cm under a plate (a point further
//   below is under the cymbal, not on it).
using System;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Drums
{
    public sealed partial class Drumstick
    {
        /// <summary>E2: the child of Root the meshes hang under; it alone pivots about the grip.</summary>
        public Transform Visual;
        public StickVisualState VisualState;
        /// <summary>The approach speed (m/s) of a hit by this stick this frame (DrumKit.Tick), consumed by UpdateVisuals.</summary>
        public float HitSpeed;
        /// <summary>The drawn angle and the clamp part of it, last frame (rad; diagnostics and tests).</summary>
        public float Phi, PhiC;
        /// <summary>The piece the tip ball last came down onto (-1 none): the surface it is clamped against.</summary>
        public int SurfacePiece = -1;
        public Vector3 VisualAxis = Vector3.right;
        bool _turned;

        /// <summary>World centre of the DRAWN tip ball (the Visual child's), what the contact shadow is cast from.</summary>
        public Vector3 VisualTipWorld => (Visual != null ? Visual : Root).TransformPoint(new Vector3(0f, StickSpec.Length, 0f));

        internal void SetVisual(float phi, Vector3 grip, Vector3 axis)
        {
            if (Visual == null) return;
            if (phi > 0f && axis != Vector3.zero)
            {
                Quaternion q = Quaternion.AngleAxis(phi * Mathf.Rad2Deg, axis);
                Visual.SetPositionAndRotation(grip + q * (Root.position - grip), q * Root.rotation);
                _turned = true;
            }
            else if (_turned)
            {
                Visual.localPosition = Vector3.zero;
                Visual.localRotation = Quaternion.identity;
                _turned = false;
            }
        }

        internal void ResetVisual()
        {
            VisualState = default;
            HitSpeed = Phi = PhiC = 0f;
            SurfacePiece = -1;
            SetVisual(0f, Vector3.zero, Vector3.zero);
        }
    }

    public sealed partial class DrumSticks
    {
        /// <summary>E2, in Drums' StickHover phase (after the hit test, so this frame's contact is known): each held stick's
        /// drawn angle about its grip (StickVisualMath) applied to its Visual child; free sticks are drawn as they lie.</summary>
        public void UpdateVisuals(float dt, DrumKit kit)
        {
            for (int i = 0; i < _sticks.Length; i++)
            {
                Drumstick s = _sticks[i];
                if (!s.Held || kit == null || !kit.IsPresent)
                {
                    s.ResetVisual();
                    continue;
                }
                Vector3 grip = s.GripWorld, tip = s.TipWorld;
                bool has = kit.StickSurface(s, tip, out Vector3 point, out Vector3 normal);
                float hit = s.HitSpeed;
                s.HitSpeed = 0f;
                s.Phi = StickVisualMath.Step(ref s.VisualState, dt, grip, tip, has, point, normal, hit, out float phiC, out Vector3 axis);
                s.PhiC = phiC;
                if (has && axis != Vector3.zero) s.VisualAxis = axis;
                s.SetVisual(s.Phi, grip, s.VisualAxis);
            }
        }

        /// <summary>The drawn tip-ball centres (xyz) and radius (w) of the held sticks (IDrumsApi.StickShadowSpheres).</summary>
        public int ShadowSpheres(Span<Vector4> dst)
        {
            int n = 0;
            for (int i = 0; i < _sticks.Length && n < dst.Length; i++)
            {
                Drumstick s = _sticks[i];
                if (!s.Held) continue;
                Vector3 c = s.VisualTipWorld;
                dst[n++] = new Vector4(c.x, c.y, c.z, StickSpec.TipRadius);
            }
            return n;
        }
    }

    public sealed partial class DrumKit : IContactSurface
    {
        /// <summary>A point more than this under a cymbal plate is under the cymbal, not on it (shadows only).</summary>
        public const float CymbalBelow = 0.01f;
        /// <summary>E2: a piece under the stick tip within this height can become the surface it comes down onto ...</summary>
        public const float StickAbove = 0.30f;
        /// <summary>... and stays its surface while the raw tip is less than this below it (deeper dips fade out anyway).</summary>
        public const float StickBelow = 0.12f;

        /// <summary>Set by DrumsModule each frame: a held kit offers no contact surface.</summary>
        public IGrabService Grab { get; set; }

        struct SurfaceFrame
        {
            public Matrix4x4 WorldToLocal;
            public Vector3 Up, Position;
        }

        readonly SurfaceFrame[] _surfaces = new SurfaceFrame[DrumSpec.PieceCount];
        bool _surfacesArmed, _surfacesValid;

        /// <summary>DrumsModule calls this each app frame in StickHover, after the kit's tick (flash scale, plate turns) and
        /// before the stick visuals and Core's contact step: the nine surface frames are then read once per frame instead of
        /// once per query. Without it (EditMode fixtures) every query reads the transforms.</summary>
        public void BeginContactFrame()
        {
            _surfacesArmed = true;
            _surfacesValid = false;
        }

        void Surface(int pi, out Matrix4x4 w2l, out Vector3 up, out Vector3 position)
        {
            if (_surfacesArmed)
            {
                if (!_surfacesValid)
                {
                    for (int i = 0; i < _pieces.Length; i++)
                    {
                        Transform t = SurfaceOf(_pieces[i]);
                        _surfaces[i] = new SurfaceFrame { WorldToLocal = t.worldToLocalMatrix, Up = t.up, Position = t.position };
                    }
                    _surfacesValid = true;
                }
                ref SurfaceFrame f = ref _surfaces[pi];
                w2l = f.WorldToLocal;
                up = f.Up;
                position = f.Position;
                return;
            }
            Transform s = SurfaceOf(_pieces[pi]);
            w2l = s.worldToLocalMatrix;
            up = s.up;
            position = s.position;
        }

        /// <summary>The surface plane of a piece: the plate at its current tilt for a cymbal, the group's y = 0 otherwise.</summary>
        public static Transform SurfaceOf(DrumPiece piece) => piece.IsCymbal && piece.Top != null ? piece.Top : piece.Group;

        bool Held => Grab != null && Rig != null && ReferenceEquals(Grab.HeldRig, Rig);

        /// <summary>IContactSurface (10-upgrade/experience.md 2.1): the head, pad or plate under a fingertip or a stick tip,
        /// at the height it is DRAWN (the head and pad top 1 mm up, a cymbal's lathe profile up to the 16 mm bell), so the
        /// shadows lie on what is seen and the drawn fingers and sticks stop on it. The hit test keeps the y = 0 planes.</summary>
        public bool QueryContact(Vector3 world, float above, float below, out ContactHit hit)
        {
            hit = default;
            if (_pieces == null || !IsPresent || Held) return false;
            bool any = false;
            for (int pi = 0; pi < _pieces.Length; pi++)
            {
                DrumPiece piece = _pieces[pi];
                Surface(pi, out Matrix4x4 w2l, out Vector3 n, out Vector3 origin);
                Vector3 l = w2l.MultiplyPoint3x4(world);
                float r = piece.R, rr2 = l.x * l.x + l.z * l.z;
                if (rr2 > r * r) continue;
                bool cymbal = piece.IsCymbal;
                float top = DrumSpec.DrawnTopY(cymbal, r, Mathf.Sqrt(rr2));
                float h = Vector3.Dot(n, world - origin) - top;
                // under a cymbal counts from its strike plane (the plate is thin): the drawn top adds to the depth allowed
                if (h > above || h < -(cymbal ? Mathf.Min(below, CymbalBelow) + top : below)) continue;
                if (any && h >= hit.Height) continue;
                hit.Point = world - n * h;
                hit.Normal = n;
                hit.Tangent = Vector3.zero;
                hit.ClipHalf = 0f;
                hit.Height = h;
                hit.Use = cymbal ? ContactUse.Shadow : ContactUse.Both;
                any = true;
            }
            return any;
        }

        /// <summary>E2: the surface a held stick's raw tip ball is clamped against. It must have come down onto it from above:
        /// the piece it was last over stays its surface while the tip is in that piece's footprint and not yet back above
        /// the ball's radius; otherwise the nearest piece under the tip (so a stick under a cymbal, over a tom, is never
        /// lifted through the cymbal). False when the tip is over nothing.</summary>
        public bool StickSurface(Drumstick s, Vector3 tip, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            if (_pieces == null) { s.SurfacePiece = -1; return false; }
            int keep = s.SurfacePiece, above = -1;
            bool keepOk = false;
            float keepH = 0f, aboveH = float.MaxValue;
            for (int pi = 0; pi < _pieces.Length; pi++)
            {
                DrumPiece piece = _pieces[pi];
                Surface(pi, out Matrix4x4 w2l, out Vector3 up, out Vector3 origin);
                Vector3 l = w2l.MultiplyPoint3x4(tip);
                float r = piece.R, rr2 = l.x * l.x + l.z * l.z;
                if (rr2 > r * r) continue;
                float h = Vector3.Dot(up, tip - origin) - DrumSpec.DrawnTopY(piece.IsCymbal, r, Mathf.Sqrt(rr2));
                if (pi == keep && h > -StickBelow) { keepOk = true; keepH = h; }
                if (h >= 0f && h <= StickAbove && h < aboveH) { above = pi; aboveH = h; }
            }
            int chosen = keepOk && keepH < StickVisualMath.TipRadius ? keep : above;
            s.SurfacePiece = chosen;
            if (chosen < 0) return false;
            Surface(chosen, out Matrix4x4 cw2l, out normal, out point);
            // the drawn surface under the tip (the head 1 mm up; on a cymbal the profile's height at the tip's radius)
            Vector3 cl = cw2l.MultiplyPoint3x4(tip);
            DrumPiece cp = _pieces[chosen];
            point += normal * DrumSpec.DrawnTopY(cp.IsCymbal, cp.R, Mathf.Sqrt(cl.x * cl.x + cl.z * cl.z));
            return true;
        }

        /// <summary>IDrumsApi (E3): the drawn tip balls of the held sticks.</summary>
        public int StickShadowSpheres(Span<Vector4> centreRadius) => _sticks != null ? _sticks.ShadowSpheres(centreRadius) : 0;
    }
}
