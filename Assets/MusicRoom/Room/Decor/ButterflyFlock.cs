// The butterflies' behaviour (no Unity objects: EditMode tests drive it directly; Butterflies.cs draws it). While music is
// being recorded or played back (the listen-back model's notes: plain live play never brings them), a small group (4, up
// to 12 with louder notes) comes out: in the VR room they take off from the room's flowers, in mixed reality they fade
// in beyond the instruments. They flutter around the instruments that sound: targets on a ring above and beyond each
// instrument's played parts (away from the player, so they stay in view without crossing the hands), retargeted every
// 0.7-1.8 s; a loud onset makes them flap faster, fly faster, glow brighter and some hop to a new spot. Between phrases
// (PhraseGap without a note) each in turn flies to a free flower head and lands, wings slowly opening and closing; the
// next note lifts it off again. In mixed reality (no flowers) they drift slowly near the last instrument instead. When
// the music stops (the recorder idle LeaveAfterStop, or LeaveAfterSilence without a note) they drift away, back toward the
// flowers in VR, up and out in mixed reality, fading out over FadeOut; a perched one fades on its flower.
// Never in the player's way: a hard sphere of HeadClear (+ the wing's reach) around the head and HandClear around every
// active fingertip and stick tip, softer repulsion further out (HeadSoft, HandSoft), and never inside the box over an
// instrument's played parts (keys, heads, strings, bars): pushed out over its top. In the VR room they stay inside it.
// Allocation-free after construction (fixed arrays; the caller adds this frame's attractors, avoid points and perches).
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    public enum ButterflyMode : byte { Off = 0, Flutter, ToPerch, Perched }

    public struct Butterfly
    {
        public ButterflyMode Mode;
        public bool Leaving;
        public Vector3 Pos, Vel, Target, Heading;
        public float Pitch, Bank;
        /// <summary>Wing beat: phase (rad), rate scale, the wing's angle now (rad above the body's plane).</summary>
        public float Phase, RateScale, Wing;
        /// <summary>Fade (0..1), glow (0..1, brighter with the music), excitement (a loud onset's, decays).</summary>
        public float Alpha, Glow, Excite;
        /// <summary>Wing length (m); the design (the wing atlas tile).</summary>
        public float Size;
        public int Design;
        public float Speed, Retarget, GapDelay, TakeOff, S1, S2, S3;
        public int Perch;
        public bool Basks;
    }

    /// <summary>What the room tells the flock each frame.</summary>
    public struct FlockFrame
    {
        public float Dt, Time;
        public Vector3 Head, HeadForward;
        /// <summary>The loudest onset that sounded this frame (0 when none) and whether a lane is held (a bowl rubbed).</summary>
        public float Onset;
        public bool Holding;
        /// <summary>The room's music level now (0..1).</summary>
        public float Level;
        /// <summary>The recorder is idle (no take or loop playing or recording).</summary>
        public bool Stopped;
        /// <summary>The VR room is shown: stay inside RoomBox, flowers are perches; false: mixed reality.</summary>
        public bool InRoom;
        public Bounds RoomBox;
    }

    public sealed class ButterflyFlock
    {
        public const int Max = 12, MinCount = 4, MaxAttractors = 12, MaxAvoid = 32, MaxPerches = 48;
        /// <summary>Never inside: the head's sphere (the brief's 0.35 m) and each fingertip's; Reach is a wing's span.</summary>
        public const float HeadClear = 0.35f, HandClear = 0.12f, Reach = 0.1f;
        /// <summary>Soft repulsion out to here (m).</summary>
        public const float HeadSoft = 0.75f, HandSoft = 0.25f;
        /// <summary>The box over played parts: this much wider, and this far above their top.</summary>
        public const float KeepSide = 0.06f, KeepAbove = 0.14f, KeepBelow = 0.05f;
        /// <summary>Seconds without a note: a phrase gap (they go to the flowers); the music stopped (they leave).</summary>
        public const float PhraseGap = 1.2f, LeaveAfterStop = 2.5f, LeaveAfterSilence = 7f;
        public const float FadeIn = 0.8f, FadeOut = 2.2f, SpawnEvery = 0.3f;
        public const float SpeedMin = 0.35f, SpeedMax = 1.05f, FlapMin = 7f, FlapMax = 13f;
        /// <summary>Flying to a flower across the room they cruise at this (x their own speed scale), m/s.</summary>
        public const float Cruise = 1.4f;
        /// <summary>The smoothed loudness at which MinCount come, and all Max.</summary>
        public const float SoftEnergy = 0.38f, LoudEnergy = 0.88f;
        /// <summary>Within this of its flower a butterfly lands (m).</summary>
        public const float LandWithin = 0.025f;

        readonly Butterfly[] _b = new Butterfly[Max];
        readonly Vector3[] _aC = new Vector3[MaxAttractors], _aE = new Vector3[MaxAttractors];
        readonly float[] _aL = new float[MaxAttractors];
        int _aCount;
        Vector3 _lastC, _lastE;
        bool _hasLast;
        readonly Vector4[] _avoid = new Vector4[MaxAvoid];
        int _avoidCount;
        readonly Vector3[] _perch = new Vector3[MaxPerches];
        readonly int[] _owner = new int[MaxPerches];
        int _perchCount;
        Mulberry32 _rng;
        float _since = float.PositiveInfinity, _spawnIn, _energy;
        int _nextDesign;

        public ButterflyFlock(uint seed)
        {
            _rng = new Mulberry32(seed);
            for (int i = 0; i < Max; i++) _b[i].Perch = -1;
            for (int i = 0; i < MaxPerches; i++) _owner[i] = -1;
            _nextDesign = (int)(seed % 8u);
        }

        public ref readonly Butterfly Get(int i) => ref _b[i];
        /// <summary>Butterflies out now (any mode but Off).</summary>
        public int Alive { get; private set; }
        /// <summary>Seconds since the last note (or held lane) and the smoothed loudness that sets how many come.</summary>
        public float SinceNote => _since;
        public float Energy => _energy;
        /// <summary>How many the flock wants now.</summary>
        public int Want { get; private set; }
        /// <summary>Counters for tests and the log: butterflies that came out, landings on a flower.</summary>
        public int Spawned { get; private set; }
        public int Landings { get; private set; }
        public int PerchCount => _perchCount;
        public Vector3 PerchAt(int i) => _perch[i];
        /// <summary>The music is being made (notes recently): they stay; false: they leave.</summary>
        public bool Session { get; private set; }

        float Rand(float a, float b) => a + (b - a) * (float)_rng.Next();

        // ---------------------------------------------------------------- this frame's inputs

        /// <summary>Clears the frame's attractors and avoid points (call before adding them).</summary>
        public void BeginFrame() { _aCount = 0; _avoidCount = 0; }

        /// <summary>An instrument that sounds: its played parts' box (centre, half extents) and its level.</summary>
        public void AddAttractor(Vector3 centre, Vector3 extent, float level)
        {
            if (_aCount >= MaxAttractors) return;
            _aC[_aCount] = centre; _aE[_aCount] = extent; _aL[_aCount] = level;
            _aCount++;
            _lastC = centre; _lastE = extent; _hasLast = true;
        }

        /// <summary>A point to keep clear of (a fingertip, a stick's tip): HandClear hard, HandSoft soft.</summary>
        public void AddAvoid(Vector3 p)
        {
            if (_avoidCount >= MaxAvoid) return;
            _avoid[_avoidCount++] = new Vector4(p.x, p.y, p.z, 0f);
        }

        /// <summary>Where they may land (the flower heads); count 0: nowhere (mixed reality). A changed set lets go of every
        /// claim (a perched butterfly takes off).</summary>
        public void SetPerches(System.Collections.Generic.IReadOnlyList<Vector3> perches, int count)
        {
            count = Mathf.Min(count, MaxPerches);
            bool same = count == _perchCount;
            for (int i = 0; same && i < count; i++) same = perches[i] == _perch[i];
            if (same) return;
            for (int i = 0; i < count; i++) _perch[i] = perches[i];
            _perchCount = count;
            for (int i = 0; i < MaxPerches; i++) _owner[i] = -1;
            for (int i = 0; i < Max; i++)
            {
                if (_b[i].Perch < 0) continue;
                _b[i].Perch = -1;
                if (_b[i].Mode == ButterflyMode.ToPerch || _b[i].Mode == ButterflyMode.Perched) _b[i].Mode = ButterflyMode.Flutter;
            }
        }

        // ---------------------------------------------------------------- the frame

        public void Tick(in FlockFrame f)
        {
            float dt = Mathf.Clamp(f.Dt, 0f, 0.1f);
            bool note = f.Onset > 0f || f.Holding;
            if (note) _since = 0f;
            else if (_since < float.PositiveInfinity) _since += dt;
            if (f.Onset > 0f) _energy += (f.Onset - _energy) * (f.Onset > _energy ? 0.45f : 0.15f);
            _energy *= Mathf.Exp(-dt / 6f);
            Session = _since < (f.Stopped ? LeaveAfterStop : LeaveAfterSilence);
            bool gap = _since > PhraseGap;
            Want = Session ? MinCount + Mathf.RoundToInt((Max - MinCount) * Mathf.Clamp01((_energy - SoftEnergy) / (LoudEnergy - SoftEnergy))) : 0;

            int alive = 0;
            for (int i = 0; i < Max; i++) if (_b[i].Mode != ButterflyMode.Off) alive++;
            _spawnIn -= dt;
            if (Session && !gap && alive < Want && _spawnIn <= 0f)
            {
                for (int i = 0; i < Max; i++)
                {
                    if (_b[i].Mode != ButterflyMode.Off) continue;
                    Spawn(i, f);
                    alive++;
                    break;
                }
                _spawnIn = SpawnEvery;
            }

            Vector3 headFlat = Flat(f.HeadForward, Vector3.forward);
            alive = 0;
            for (int i = 0; i < Max; i++)
            {
                ref Butterfly b = ref _b[i];
                if (b.Mode == ButterflyMode.Off) continue;
                Step(ref b, i, f, dt, gap, headFlat);
                if (b.Mode != ButterflyMode.Off) alive++;
            }
            Alive = alive;
        }

        void Spawn(int i, in FlockFrame f)
        {
            ref Butterfly b = ref _b[i];
            b = default;
            b.Perch = -1;
            b.Mode = ButterflyMode.Flutter;
            b.Design = _nextDesign;
            _nextDesign = (_nextDesign + 3) % 8;   // every design before a repeat (3 and 8 are coprime)
            b.Size = Rand(0.05f, 0.068f) * (b.Design == 1 ? 1.15f : 1f);
            b.Speed = Rand(0.8f, 1.2f);
            b.RateScale = Rand(0.85f, 1.15f);
            b.S1 = Rand(0f, 6.28f); b.S2 = Rand(0f, 6.28f); b.S3 = Rand(0f, 6.28f);
            b.Phase = Rand(0f, 6.28f);
            b.GapDelay = Rand(0f, 0.8f);
            b.Basks = _rng.Next() < 0.4;
            b.Excite = Mathf.Clamp01(f.Onset);
            if (f.InRoom && _perchCount > 0)
            {
                // From a flower: they were there all along, resting (it fades in as it takes off).
                b.Pos = _perch[(int)(_rng.Next() * _perchCount) % _perchCount] + Vector3.up * 0.02f;
            }
            else
            {
                // Mixed reality: beyond the instruments, a little above the eyes.
                Vector3 c = _hasLast ? _lastC : f.Head + Flat(f.HeadForward, Vector3.forward) * 0.8f;
                Vector3 away = Flat(c - f.Head, Flat(f.HeadForward, Vector3.forward));
                b.Pos = c + Turn(away, Rand(-1f, 1f)) * Rand(1.0f, 1.5f);
                b.Pos.y = f.Head.y + Rand(-0.1f, 0.35f);
            }
            b.Heading = Flat(f.Head - b.Pos, Vector3.forward);
            b.Vel = Vector3.up * 0.2f;
            b.Target = b.Pos;
            b.Retarget = 0f;
            Spawned++;
        }

        void Step(ref Butterfly b, int i, in FlockFrame f, float dt, bool gap, Vector3 headFlat)
        {
            // ---- the music: fade, leave, come back
            if (!Session)
            {
                if (!b.Leaving)
                {
                    b.Leaving = true;
                    if (b.Mode != ButterflyMode.Perched)
                    {
                        Release(ref b);
                        b.Mode = ButterflyMode.Flutter;
                        b.Target = LeaveTarget(b, f);
                    }
                }
                b.Alpha -= dt / FadeOut;
                if (b.Alpha <= 0f)
                {
                    Release(ref b);
                    b.Mode = ButterflyMode.Off;
                    b.Alpha = 0f;
                    return;
                }
            }
            else
            {
                if (b.Leaving) { b.Leaving = false; b.Retarget = 0f; }
                b.Alpha = Mathf.Min(1f, b.Alpha + dt / FadeIn);
            }

            if (f.Onset > 0f && Session)
            {
                b.Excite = Mathf.Max(b.Excite, f.Onset * Rand(0.75f, 1f));
                if (b.Mode == ButterflyMode.Flutter && _rng.Next() < 0.35f * f.Onset) b.Retarget = 0f;   // a hop
            }
            b.Excite *= Mathf.Exp(-dt / 0.9f);
            b.Glow = Mathf.Clamp01(0.55f * f.Level + 0.6f * b.Excite);

            // ---- phrases: land between them, take off on the next note
            if (Session)
            {
                if (b.Mode == ButterflyMode.Flutter && gap && _since > PhraseGap + b.GapDelay && f.InRoom) TryPerch(ref b, i, f);
                else if (!gap && (b.Mode == ButterflyMode.ToPerch || b.Mode == ButterflyMode.Perched))
                {
                    if (b.Mode == ButterflyMode.Perched && b.TakeOff <= 0f) b.TakeOff = Rand(0.05f, 0.45f);
                    if (b.Mode == ButterflyMode.ToPerch || (b.TakeOff -= dt) <= 0f)
                    {
                        Release(ref b);
                        b.Mode = ButterflyMode.Flutter;
                        b.Vel = Vector3.up * 0.35f + b.Heading * 0.1f;
                        b.Retarget = 0f;
                        b.TakeOff = 0f;
                        b.GapDelay = Rand(0f, 0.8f);
                    }
                }
            }

            // ---- the wings
            if (b.Mode == ButterflyMode.Perched)
            {
                b.Phase += 2f * Mathf.PI * 0.35f * dt;
                b.Wing = b.Basks ? 0.2f + 0.22f * Mathf.Sin(b.Phase) : 1.08f + 0.32f * Mathf.Sin(b.Phase);
            }
            else
            {
                float rate = Mathf.Lerp(FlapMin, FlapMax, b.Excite) * b.RateScale * (gap && Session && b.Mode == ButterflyMode.Flutter ? 0.75f : 1f);
                b.Phase += 2f * Mathf.PI * rate * dt;
                float amp = 0.85f + 0.25f * b.Excite;
                b.Wing = 0.3f + amp * Mathf.Sin(b.Phase);
            }
            if (b.Phase > 1000f) b.Phase -= 2f * Mathf.PI * 150f;

            if (b.Mode == ButterflyMode.Perched)
            {
                b.Pos = _perch[b.Perch];
                b.Vel = Vector3.zero;
                b.Pitch = 0f;
                b.Bank = 0f;
                // a player leaning into the flowers: it lifts off rather than sit in the way
                if ((b.Pos - f.Head).sqrMagnitude < (HeadSoft * 0.8f) * (HeadSoft * 0.8f)) { Release(ref b); b.Mode = ButterflyMode.Flutter; b.Retarget = 0f; }
                else return;
            }

            // ---- where to
            if (b.Mode == ButterflyMode.Flutter && !b.Leaving)
            {
                b.Retarget -= dt;
                if (b.Retarget <= 0f || (b.Target - b.Pos).sqrMagnitude < 0.01f)
                {
                    b.Target = FlutterTarget(f, headFlat);
                    b.Retarget = Rand(0.7f, 1.8f);
                }
            }
            if (b.Mode == ButterflyMode.ToPerch) b.Target = _perch[b.Perch] + Vector3.up * 0.004f;

            // ---- flight: steer to the target, flutter about it, bob with the beats
            Vector3 to = b.Target - b.Pos;
            float dist = to.magnitude;
            float speed = b.Speed * Mathf.Lerp(SpeedMin, SpeedMax, b.Excite);
            if (gap && Session && b.Mode == ButterflyMode.Flutter) speed *= 0.5f;   // drifting between phrases (no flowers)
            if (b.Mode == ButterflyMode.ToPerch) speed = Mathf.Max(speed, Cruise * b.Speed);
            if (b.Leaving && b.Mode != ButterflyMode.Perched) speed = Mathf.Max(speed, 0.6f);
            float arrive = b.Mode == ButterflyMode.ToPerch ? Mathf.Clamp01(dist / 0.3f) : 1f;
            Vector3 desired = dist > 1e-4f ? to * (speed * Mathf.Max(arrive, 0.12f) / dist) : Vector3.zero;
            float t = f.Time, w = 0.22f * arrive;
            desired += new Vector3(Mathf.Sin(t * 1.3f + b.S1) + 0.5f * Mathf.Sin(t * 3.1f + b.S2), 0.7f * Mathf.Sin(t * 2.2f + b.S3),
                Mathf.Cos(t * 1.7f + b.S2) + 0.5f * Mathf.Sin(t * 2.7f + b.S1)) * w;
            desired.y += 0.25f * Mathf.Cos(b.Phase) * arrive;   // each downstroke lifts a little
            b.Vel += (desired - b.Vel) * (1f - Mathf.Exp(-dt * 4f));
            b.Pos += b.Vel * dt;
            if (b.Mode == ButterflyMode.ToPerch && (_perch[b.Perch] - b.Pos).sqrMagnitude < LandWithin * LandWithin)
            {
                b.Mode = ButterflyMode.Perched;
                b.Pos = _perch[b.Perch];
                b.Phase = Rand(0f, 6.28f);
                Landings++;
            }

            // ---- stay clear
            Constrain(ref b, f);

            // ---- attitude: heading along the flight, nose up when climbing, a little roll
            Vector3 vf = new Vector3(b.Vel.x, 0f, b.Vel.z);
            float vh = vf.magnitude;
            if (vh > 0.04f) b.Heading = Vector3.Slerp(b.Heading, vf / vh, 1f - Mathf.Exp(-dt * 6f)).normalized;
            b.Pitch = Mathf.Clamp(b.Vel.y * 0.9f, -0.5f, 0.6f);
            b.Bank = 0.18f * Mathf.Sin(t * 2.1f + b.S1);
        }

        void Release(ref Butterfly b)
        {
            if (b.Perch >= 0 && b.Perch < MaxPerches) _owner[b.Perch] = -1;
            b.Perch = -1;
        }

        /// <summary>A free flower head near it (one of the four nearest, so they spread over the flowers), not by the head.</summary>
        void TryPerch(ref Butterfly b, int i, in FlockFrame f)
        {
            int best = -1;
            float bestD = float.MaxValue;
            float pick = Rand(0f, 0.6f);   // a little randomness in what "near" means
            for (int k = 0; k < _perchCount; k++)
            {
                if (_owner[k] >= 0) continue;
                if ((_perch[k] - f.Head).sqrMagnitude < HeadSoft * HeadSoft) continue;
                float d = (_perch[k] - b.Pos).magnitude + pick * Mathf.Sin(k * 1.7f + b.S1);
                if (d < bestD) { bestD = d; best = k; }
            }
            if (best < 0) return;
            _owner[best] = i;
            b.Perch = best;
            b.Mode = ButterflyMode.ToPerch;
        }

        /// <summary>Around an instrument that sounds (weighted by level; else the last one): on a ring above and beyond its
        /// played parts, away from the player; now and then (VR) somewhere in the room.</summary>
        Vector3 FlutterTarget(in FlockFrame f, Vector3 headFlat)
        {
            Vector3 c, e;
            if (_aCount > 0)
            {
                float sum = 0f;
                for (int k = 0; k < _aCount; k++) sum += _aL[k] + 0.05f;
                float r = Rand(0f, sum);
                int a = 0;
                for (; a < _aCount - 1; a++) { r -= _aL[a] + 0.05f; if (r <= 0f) break; }
                c = _aC[a]; e = _aE[a];
            }
            else if (_hasLast) { c = _lastC; e = _lastE; }
            else { c = f.Head + headFlat * 0.9f + Vector3.down * 0.4f; e = new Vector3(0.3f, 0.1f, 0.3f); }

            Vector3 target;
            if (f.InRoom && _rng.Next() < 0.15)
            {
                Bounds rb = f.RoomBox;
                target = new Vector3(Rand(rb.min.x + 0.6f, rb.max.x - 0.6f), Rand(0.9f, Mathf.Min(2.1f, rb.max.y - 0.4f)), Rand(rb.min.z + 0.6f, rb.max.z - 0.6f));
            }
            else
            {
                // around the box's far half: out of the box by 8-40 cm along a direction within 70 degrees of "away"
                Vector3 away = Flat(c - f.Head, headFlat);
                Vector3 dir = Turn(away, Rand(-1.22f, 1.22f));
                float rad = Mathf.Abs(dir.x) * e.x + Mathf.Abs(dir.z) * e.z + Rand(0.08f, 0.4f);
                target = c + dir * rad;
                target.y = c.y + e.y + Rand(0.16f, 0.55f);
                // never on the player's side of the instrument (over the hands): at least as far out as its far edge
                float far = Vector3.Dot(c - f.Head, away) + DepthOf(away, e);
                float along = Vector3.Dot(target - f.Head, away);
                if (along < far) target += away * (far - along);
            }
            // never aim into the player's face
            Vector3 d = target - f.Head;
            float m = d.magnitude;
            if (m < HeadSoft + 0.1f) target = f.Head + (m > 1e-4f ? d / m : headFlat) * (HeadSoft + 0.1f);
            return target;
        }

        /// <summary>How deep a box (half extents e) reaches along a horizontal direction from its centre.</summary>
        static float DepthOf(Vector3 dir, Vector3 e) => Mathf.Abs(dir.x) * e.x + Mathf.Abs(dir.z) * e.z;

        Vector3 LeaveTarget(in Butterfly b, in FlockFrame f)
        {
            if (f.InRoom && _perchCount > 0) return _perch[(int)(_rng.Next() * _perchCount) % _perchCount] + Vector3.up * 0.15f;
            Vector3 away = Flat(b.Pos - f.Head, Flat(f.HeadForward, Vector3.forward));
            return b.Pos + away * 2.2f + Vector3.up * 0.9f;
        }

        void Constrain(ref Butterfly b, in FlockFrame f)
        {
            float dt = Mathf.Clamp(f.Dt, 0f, 0.1f);
            // the room
            if (f.InRoom)
            {
                Bounds rb = f.RoomBox;
                b.Pos = new Vector3(Mathf.Clamp(b.Pos.x, rb.min.x + 0.12f, rb.max.x - 0.12f), Mathf.Clamp(b.Pos.y, 0.12f, rb.max.y - 0.15f),
                    Mathf.Clamp(b.Pos.z, rb.min.z + 0.12f, rb.max.z - 0.12f));
            }
            else if (b.Pos.y < 0.12f) b.Pos.y = 0.12f;
            // the played parts (keys, heads, strings, bars): out over the top
            for (int k = 0; k < _aCount; k++)
            {
                Vector3 c = _aC[k], e = _aE[k];
                if (b.Mode == ButterflyMode.ToPerch) continue;
                if (Mathf.Abs(b.Pos.x - c.x) < e.x + KeepSide && Mathf.Abs(b.Pos.z - c.z) < e.z + KeepSide && b.Pos.y > c.y - e.y - KeepBelow && b.Pos.y < c.y + e.y + KeepAbove)
                {
                    b.Pos.y = c.y + e.y + KeepAbove;
                    if (b.Vel.y < 0.1f) b.Vel.y = 0.1f;
                }
            }
            // the hands and sticks
            for (int k = 0; k < _avoidCount; k++)
            {
                Vector3 p = _avoid[k];
                Vector3 d = b.Pos - p;
                float m = d.magnitude;
                if (m >= HandSoft) continue;
                Vector3 n = m > 1e-4f ? d / m : Vector3.up;
                b.Vel += n * ((HandSoft - m) * 8f * dt);
                if (m < HandClear + Reach) { b.Pos = p + n * (HandClear + Reach); float into = Vector3.Dot(b.Vel, n); if (into < 0f) b.Vel -= n * into; }
            }
            // the head, last: it wins
            {
                Vector3 d = b.Pos - f.Head;
                float m = d.magnitude;
                if (m < HeadSoft)
                {
                    Vector3 n = m > 1e-4f ? d / m : Flat(f.HeadForward, Vector3.forward);
                    b.Vel += n * ((HeadSoft - m) * 6f * dt);
                    if (m < HeadClear + Reach) { b.Pos = f.Head + n * (HeadClear + Reach); float into = Vector3.Dot(b.Vel, n); if (into < 0f) b.Vel -= n * into; }
                }
            }
        }

        static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            float m = v.magnitude;
            if (m > 1e-4f) return v / m;
            fallback.y = 0f;
            m = fallback.magnitude;
            return m > 1e-4f ? fallback / m : Vector3.forward;
        }

        /// <summary>A horizontal direction turned about +y by a (rad).</summary>
        static Vector3 Turn(Vector3 v, float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector3(v.x * c + v.z * s, 0f, -v.x * s + v.z * c);
        }
    }
}
