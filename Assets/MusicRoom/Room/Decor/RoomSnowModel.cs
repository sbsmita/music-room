// Snow in the whole room (2026-11-13, "anytime if snow is clicked make it fall in whole room 360 degree coverage"), its
// numbers: no Unity objects, so EditMode tests drive it directly. Decor/RoomSnow.cs draws it (MusicRoom/RoomSnow: one
// mesh, one draw, every flake moved by the vertex shader alone); FlakeAt below is that shader's maths on the CPU, line
// for line, so the tests can check where every flake is (inside the volume, all around the head, never at the eyes).
//
// The volume (Volume): a box in a frame turned about +y (Cos, Sin: world = (Cos x + Sin z, y, -Sin x + Cos z), the
// RoomFrame convention). VR: the built room's own box (RoomFrame.LocalBox under the Room root, whatever its pose), the walls
// inset a little. Mixed reality: a box MrRadius around the player, from the real floor (else the head less a seated
// estimate) to MrHeight above it; its corners fade out, so the player sees a round snowfall about them. Flakes are anchored
// to the world, not to the box: the box may follow the head (MR) and the flakes stay where they are (the wrap is the
// world's).
//
// A flake: it appears at the top of the volume, falls at its own gentle speed (bigger flakes a little faster) with a slow
// draft along the room (the weather's wind, much softened indoors) and its own sway, lands on the floor, lies there for a
// moment and melts (shrinks, fades), and appears again at the top. Brightness and colour come from the room (warm in VR,
// warmer and melting near the fire; soft white over passthrough). Never within HeadClear of the head (and the eye), and
// thin in front of the face and over the hands' playing area (ClearZone) so the instruments and keys stay clear. The
// SNOW switch's level brings flakes in one by one as it starts and thins them as it stops.
//
// With the music (a playback of a recording only; the window show's envelope, WindowShowModel, is the one source): each
// beat's gust makes every flake circle its own place (eddies), whirls the snow about the player and lifts it a little;
// the flakes near the player catch a faint tint of the instrument that played (SongStars.ColorOf), which fades with the
// notes. All of it is exactly nothing at rest. Allocation-free.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    public sealed class RoomSnowModel
    {
        /// <summary>Flakes in the one mesh (4 vertices each).</summary>
        public const int Count = 3600;
        /// <summary>Flake diameter range (m); squared toward the small end.</summary>
        public const float SizeMin = 0.012f, SizeMax = 0.032f;
        /// <summary>Fall speed range (m/s): gentle, indoor.</summary>
        public const float FallMin = 0.17f, FallMax = 0.4f;
        /// <summary>How long a landed flake lies before it has melted (s, scaled 0.6..1.4 per flake).</summary>
        public const float LandRest = 2.4f;
        /// <summary>The weather's wind (m along the window) as an indoor draft along the room's x (m per m).</summary>
        public const float Draft = 0.1f;
        /// <summary>No flake nearer the head centre than this (m); fully shown from HeadFull.</summary>
        public const float HeadClear = 0.34f, HeadFull = 0.62f;
        /// <summary>The same for each eye (the camera), so a near flake never fills an eye.</summary>
        public const float EyeClear = 0.26f, EyeFull = 0.45f;
        /// <summary>The playing area in front of the player: centre ahead / below the head (m) and its radii (lateral, up,
        /// ahead); flakes inside keep ClearKeep of their opacity.</summary>
        public const float ClearAhead = 0.5f, ClearDown = 0.42f, ClearRx = 0.72f, ClearRy = 0.55f, ClearRz = 0.62f, ClearKeep = 0.14f;
        /// <summary>VR: walls inset (m) and the band along them where a flake fades (no pop where the wrap folds it).</summary>
        public const float WallInset = 0.1f, WallFade = 0.18f, CeilingInset = 0.06f;
        /// <summary>Mixed reality: the snowfall's radius about the player (m) and its height above the floor (m); the floor
        /// when the scene gives none (or an implausible one): the head less SeatedHead.</summary>
        public const float MrRadius = 3.6f, MrHeight = 2.6f, MrAboveHead = 0.6f, SeatedHead = 1.2f, MinHeadAboveFloor = 0.5f, MaxHeadAboveFloor = 2.3f;
        /// <summary>The fire (VR): flakes melt within FireMelt (m, gone at FireGone) and glow warmer within FireWarm.</summary>
        public const float FireGone = 0.45f, FireMelt = 1.05f, FireWarm = 2.2f;
        /// <summary>The show: the whirl about the player (rad at a full gust, falling off over WhirlReach m), the lift (m),
        /// the eddies' radius range (m); the gust follows the window's with this rise (s).</summary>
        public const float Whirl = 0.32f, WhirlReach = 2.3f, Lift = 0.22f, EddyMin = 0.05f, EddyMax = 0.26f, GustRise = 0.12f;
        /// <summary>The tint: per-instrument heat decay (s), how much of the colour the nearest flakes take, its reach (m).</summary>
        public const float TintDecay = 1.3f, TintMax = 0.42f, TintReach = 2.0f, Rest = 0.004f;
        public const int TintPendingMax = 32;

        // ---------------------------------------------------------------- the volume

        public struct Volume
        {
            public Vector3 Centre, Half;
            public float Cos, Sin;
            /// <summary>Mixed reality: round (the corners fade).</summary>
            public bool Round;

            public Vector3 ToFrame(Vector3 w) => new Vector3(Cos * w.x - Sin * w.z, w.y, Sin * w.x + Cos * w.z);
            public Vector3 ToWorld(Vector3 f) => new Vector3(Cos * f.x + Sin * f.z, f.y, -Sin * f.x + Cos * f.z);
            public float Floor => Centre.y - Half.y;
            public float Top => Centre.y + Half.y;
            /// <summary>The world point lies inside the box (margin m allowed).</summary>
            public bool Contains(Vector3 w, float margin)
            {
                Vector3 l = ToFrame(w) - ToFrame(Centre);
                return Mathf.Abs(l.x) <= Half.x + margin && Mathf.Abs(l.y) <= Half.y + margin && Mathf.Abs(l.z) <= Half.z + margin;
            }
        }

        /// <summary>VR: the room's box (room-frame bounds, localBox) under the room root's pose (rootPos, rootYaw degrees),
        /// the walls inset by WallInset and the ceiling by CeilingInset; the floor is the room's floor.</summary>
        public static Volume ForRoom(Vector3 rootPos, float rootYawDeg, Bounds localBox)
        {
            float a = rootYawDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            // the root's local point p sits at rootPos + R(yaw) p (Unity: x' = c x + s z, z' = -s x + c z)
            Vector3 lc = localBox.center;
            Vector3 centre = rootPos + new Vector3(c * lc.x + s * lc.z, lc.y, -s * lc.x + c * lc.z);
            float floor = rootPos.y + localBox.min.y, top = rootPos.y + localBox.max.y - CeilingInset;
            centre.y = 0.5f * (floor + top);
            var half = new Vector3(Mathf.Max(0.1f, localBox.extents.x - WallInset), 0.5f * (top - floor), Mathf.Max(0.1f, localBox.extents.z - WallInset));
            return new Volume { Centre = centre, Half = half, Cos = c, Sin = s, Round = false };
        }

        /// <summary>Mixed reality: the floor to use, the real one (IMixedReality.FloorHeight) when the head stands or sits a
        /// plausible height above it, else the head less SeatedHead.</summary>
        public static float MrFloor(float headY, float floorY)
        {
            float h = headY - floorY;
            return h >= MinHeadAboveFloor && h <= MaxHeadAboveFloor ? floorY : headY - SeatedHead;
        }

        /// <summary>Mixed reality: MrRadius about (centre x, z) from the floor (MrFloor) to MrHeight above it, at least
        /// MrAboveHead above the head; axis-aligned, round.</summary>
        public static Volume ForMr(Vector3 centre, float headY, float floorY)
        {
            float floor = MrFloor(headY, floorY);
            float top = Mathf.Max(floor + MrHeight, headY + MrAboveHead);
            return new Volume
            {
                Centre = new Vector3(centre.x, 0.5f * (floor + top), centre.z),
                Half = new Vector3(MrRadius, 0.5f * (top - floor), MrRadius),
                Cos = 1f, Sin = 0f, Round = true,
            };
        }

        // ---------------------------------------------------------------- the flakes

        /// <summary>A flake's random numbers (the mesh's vertex data): position seeds 0..1, tone, size, sway amount, sway
        /// rate, phase, fall speed, and Gate (the SNOW level it needs to show).</summary>
        public struct Seed
        {
            public Vector3 Pos;
            public float Tone, Size, SwayA, SwayRate, Phase, Fall;
            public float Gate => Frac(Phase * 13.71f + Pos.x * 3.17f + Pos.z * 7.13f);
        }

        public static void Seeds(uint seed, Seed[] into)
        {
            var rng = new Mulberry32(seed);
            for (int i = 0; i < into.Length; i++)
                into[i] = new Seed
                {
                    Pos = new Vector3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()),
                    Tone = rng.NextFloat(), Size = rng.NextFloat(), SwayA = rng.NextFloat(), SwayRate = rng.NextFloat(),
                    Phase = rng.NextFloat(), Fall = rng.NextFloat(),
                };
        }

        /// <summary>Everything the shader gets for a frame (RoomSnow writes it as material vectors).</summary>
        public struct Frame
        {
            /// <summary>The weather's clock (s), its wind integral (m), wind speed (m/s), the SNOW level (0..1).</summary>
            public float Time, Wind, WindVel, Level;
            public Volume Vol;
            /// <summary>The head's centre and its forward on the floor (unit, y 0).</summary>
            public Vector3 Head, Forward;
            /// <summary>The show: (gust, eddy phase rad, whirl rad, lift m); 0 at rest.</summary>
            public Vector4 Show;
            /// <summary>The fire's centre (VR) and its warmth (0: none, MR).</summary>
            public Vector4 Fire;
        }

        static float Frac(float x) => x - Mathf.Floor(x);
        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>The shader's vertex maths (MusicRoom/RoomSnow, RoomSnowFlake) for one flake: its centre in the world,
        /// its opacity before the screen-size clamp (0: not drawn), and how far it has melted on the floor (0..1).
        /// eye: the camera (the shader's _WorldSpaceCameraPos).</summary>
        public static void FlakeAt(in Seed s, in Frame f, Vector3 eye, out Vector3 pos, out float alpha, out float melt)
        {
            Volume v = f.Vol;
            Vector3 cf = v.ToFrame(v.Centre);
            Vector3 size = v.Half * 2f;
            Vector3 bmin = cf - v.Half;
            float fall = Mathf.Lerp(FallMin, FallMax, s.Fall) * (0.8f + 0.4f * s.Size);
            float rest = LandRest * Mathf.Lerp(0.6f, 1.4f, s.Tone);
            float fallT = size.y / fall;
            float cycle = fallT + rest;
            float tc = Frac(s.Pos.y + f.Time / cycle) * cycle;   // seconds since it appeared at the top
            float landed = tc >= fallT ? 1f : 0f;
            float tAir = Mathf.Min(tc, fallT);
            melt = landed * (tc - fallT) / rest;
            float since = tc - tAir;                              // seconds it has been lying (0 in the air)
            float tMove = f.Time - since;
            float windAt = f.Wind - f.WindVel * since;

            float ph = s.Phase * 6.2831853f;
            float st = tMove * Mathf.Lerp(0.55f, 1.7f, s.SwayRate) + ph;
            float amp = Mathf.Lerp(0.03f, 0.14f, s.SwayA) * (1.2f - 0.4f * s.Size);
            float fx = s.Pos.x * size.x + Draft * windAt + amp * Mathf.Sin(st);
            float fz = s.Pos.z * size.z + amp * 0.8f * Mathf.Cos(st * 0.83f + ph);
            float fy = bmin.y + size.y - tAir * fall;
            if (landed > 0f) fy = bmin.y + 0.004f;

            float px = bmin.x + Frac((fx - bmin.x) / size.x) * size.x;   // into the box (the world's wrap)
            float pz = bmin.z + Frac((fz - bmin.z) / size.z) * size.z;
            // the show (in the air only): eddies, a whirl about the player, a lift; kept inside the box (at its edges a flake
            // is faded out anyway), never folded across it
            float k = (1f - landed) * f.Show.x;
            if (k > 0f)
            {
                Vector3 hf = v.ToFrame(f.Head);
                float rx = px - hf.x, rz = pz - hf.z;
                float sp = f.Show.y * Mathf.Lerp(0.7f, 1.6f, s.SwayRate) + ph * 1.3f;
                float er = k * Mathf.Lerp(EddyMin, EddyMax, s.SwayA);
                rx += er * Mathf.Cos(sp);
                rz += er * Mathf.Sin(sp);
                fy += er * 0.5f * Mathf.Sin(sp * 1.3f) + (1f - landed) * f.Show.w * Mathf.Lerp(0.4f, 1f, s.Size);
                float ang = (1f - landed) * f.Show.z * Mathf.Exp(-(rx * rx + rz * rz) / (WhirlReach * WhirlReach));
                float sa = Mathf.Sin(ang), ca = Mathf.Cos(ang);
                float qx = ca * rx - sa * rz, qz = sa * rx + ca * rz;
                px = Mathf.Clamp(hf.x + qx, bmin.x, bmin.x + size.x);
                pz = Mathf.Clamp(hf.z + qz, bmin.z, bmin.z + size.z);
            }
            fy = Mathf.Min(fy, bmin.y + size.y);
            pos = v.ToWorld(new Vector3(px, fy, pz));

            float a = Mathf.Clamp01((f.Level * 1.08f - s.Gate) * 10f);
            if (v.Round)
            {
                float dx = px - cf.x, dz = pz - cf.z;
                float r = Mathf.Min(v.Half.x, v.Half.z);
                a *= 1f - Smooth(0.78f * r, r, Mathf.Sqrt(dx * dx + dz * dz));
            }
            else
            {
                float e = Mathf.Min(Mathf.Min(px - bmin.x, bmin.x + size.x - px), Mathf.Min(pz - bmin.z, bmin.z + size.z - pz));
                a *= Smooth(0f, WallFade, e);
            }
            a *= Smooth(0f, 0.35f, tAir * fall);                  // appears just under the top
            a *= 1f - melt;
            float dh = Vector3.Distance(pos, f.Head);
            a *= Smooth(HeadClear, HeadFull, dh);
            a *= Smooth(EyeClear, EyeFull, Vector3.Distance(pos, eye));
            // the playing area in front of the player
            Vector3 cz = f.Head + f.Forward * ClearAhead + Vector3.down * ClearDown;
            Vector3 q = pos - cz;
            Vector3 right = new Vector3(f.Forward.z, 0f, -f.Forward.x);
            float lx = Vector3.Dot(q, right) / ClearRx, ly = q.y / ClearRy, lz = Vector3.Dot(q, f.Forward) / ClearRz;
            a *= Mathf.Lerp(ClearKeep, 1f, Smooth(0.55f, 1.3f, lx * lx + ly * ly + lz * lz));
            if (f.Fire.w > 0f) a *= Smooth(FireGone, FireMelt, Vector3.Distance(pos, new Vector3(f.Fire.x, f.Fire.y, f.Fire.z)));
            alpha = a * Mathf.Lerp(0.6f, 1f, s.Tone);
        }

        // ---------------------------------------------------------------- the tint (the playing instrument's colour)

        struct Pending { public int Kind; public float Level; public double When; }

        const int Kinds = 10;   // InstrumentKind values 0..9
        readonly float[] _heat = new float[Kinds];
        readonly Vector3[] _colour = new Vector3[Kinds];
        readonly Pending[] _pending = new Pending[TintPendingMax];
        int _pendingCount;

        public RoomSnowModel()
        {
            for (int k = 0; k < Kinds; k++)
            {
                Color c = SongStars.ColorOf((InstrumentKind)k).linear;
                _colour[k] = new Vector3(c.r, c.g, c.b);
            }
        }

        /// <summary>The flakes' tint now: (linear rgb of the instruments playing, amount 0..TintMax); exactly 0 at rest.</summary>
        public Vector4 Tint { get; private set; }
        public bool TintResting { get; private set; } = true;
        public int PendingCount => _pendingCount;
        public float Heat(InstrumentKind k) => (int)k >= 0 && (int)k < Kinds ? _heat[(int)k] : 0f;

        /// <summary>A played-back onset (only while a playback runs: the caller asks WindowShowModel.Playing). A note the
        /// looper sends ahead waits until it sounds.</summary>
        public void Note(in PlaybackNoteEvent e, double now)
        {
            if (e.Role != EventRole.Onset) return;
            int k = (int)e.Kind;
            if (k < 0 || k >= Kinds) return;
            float level = Mathf.Clamp01(e.Level);
            TintResting = false;
            if (e.When > now + 1e-4)
            {
                if (_pendingCount < TintPendingMax) _pending[_pendingCount++] = new Pending { Kind = k, Level = level, When = e.When };
                return;
            }
            Heat(k, level);
        }

        void Heat(int k, float level) => _heat[k] = Mathf.Min(1.5f, _heat[k] + 0.35f + 0.65f * level);

        /// <summary>The playback ended: what waits is dropped; the heat fades by itself.</summary>
        public void Stop() { _pendingCount = 0; }

        public void Tick(float dt, double now)
        {
            if (TintResting) return;
            for (int i = 0; i < _pendingCount;)
            {
                if (_pending[i].When <= now + 1e-4)
                {
                    Heat(_pending[i].Kind, _pending[i].Level);
                    _pending[i] = _pending[--_pendingCount];
                }
                else i++;
            }
            float down = Mathf.Exp(-Mathf.Max(0f, dt) / TintDecay);
            float sum = 0f;
            Vector3 col = Vector3.zero;
            for (int k = 0; k < Kinds; k++)
            {
                float h = _heat[k] * down;
                if (h < Rest) h = 0f;
                _heat[k] = h;
                sum += h;
                col += _colour[k] * h;
            }
            if (sum <= 0f && _pendingCount == 0)
            {
                Tint = Vector4.zero;
                TintResting = true;
                return;
            }
            col /= Mathf.Max(sum, 1e-4f);
            Tint = new Vector4(col.x, col.y, col.z, TintMax * Mathf.Min(1f, sum));
        }
    }
}
