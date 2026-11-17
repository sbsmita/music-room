// The harp's play logic (update, rest, stroke, letGo, pluck, damp, animate, paint, hover, desktopDown;
// spec 05 section 5). Plain C# with no scene objects, so EditMode tests drive it with scripted pointers and clocks;
// HarpInstrument feeds it the frame's pointers, the harp's world-to-local matrix and the clocks, and draws the result.
// Everything runs in harp-local UNITY coordinates: in-plane (x, y) values equal the right-handed design values, and z
// is negated consistently, which leaves every product formed (z * side, d * ud + z * uz, |d uz - z ud|) unchanged.
// The stroke geometry is evaluated in double, so a move ending on a string "to within rounding"
// (from + len >= -1e-9) is decided the same way every time. Hot path: no allocation (records, tallies and events are pooled).
// The harp plays the classic sound only (today's Karplus-Strong strings, 2026-11-10); the upgraded physical harp's
// path (finger touch, palm re-send, measured string swing) was deleted on 2026-11-11.
using System;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using Unity.Collections;
using UnityEngine;

namespace MusicRoom.Harp
{
    /// <summary>How a string was let go (pluck options { position, speed, mode, delay }).</summary>
    public struct StrokeInfo
    {
        public float U;          // 0..1 along the string from the soundboard
        public float Speed;      // m/s
        public PluckMode Mode;   // through (a poke) / across (a sweep along the plane)
        public double Delay;     // s after the frame's first sound (glissando spread)
        public static StrokeInfo Default => new StrokeInfo { U = 0.35f, Speed = 0f, Mode = PluckMode.Through, Delay = 0 };
    }

    /// <summary>One string: fixed layout plus its sound and motion state (Strings[i]).</summary>
    public sealed class HarpString
    {
        public readonly int Index, Midi;
        public readonly Vector3 A, B;
        public readonly float Length, Radius;
        public readonly StringColour Colour;
        // Side() terms in double: a, b - a, |b - a|^2, |b - a|.
        internal readonly double Ax, Ay, Dx, Dy, Len2, LenD;

        // voice: the sound still ringing from the last pluck, kept so a hand can stop it (Until: when it has played out).
        public bool HasVoice;
        public VoiceHandle Voice;
        public double Until;
        public double LastPluck = double.NegativeInfinity;   // Clock.NowMs (-inf = never, which stays true at t = 0)
        /// <summary>The hand that plucked it last, which must not mute its own follow-through.</summary>
        public IPointerOwner PluckedBy;
        // Motion: bend (m, the finger holding it at BendU, across the plane BendD or through it BendZ); the ringing swings
        // along (AmpD, AmpZ), plucked at AmpU; Amp (m) = the pluck height times the fundamental's decay (diagnostics; the
        // picture is the blur below); Ring (s to -60 dB, the voice's lifetime); Damped.
        public float Bend, BendU = 0.5f, BendD = 1f, BendZ;
        public float Amp, AmpD = 1f, AmpZ, AmpU = 0.5f, Ring = 1f;
        public bool Damped, Moving;
        /// <summary>Pluck flash 1 -> 0 at 4 per second.</summary>
        public float Flash;
        // E8 blur (HarpBlur): the modal envelope of the ringing. BlurB = b_n of the pluck, BlurTau = tau_n of the sound,
        // BlurT = s since the pluck, BlurGain = the damping (1 -> 0 under a hand), Up / Lo = the envelope at the 17 rings
        // (m, along the swing), BlurSwing = its widest up - lo; Blurring while that is at least 0.05 mm.
        public readonly float[] BlurB = new float[HarpBlur.Modes], BlurTau = new float[HarpBlur.Modes];
        public readonly float[] Up = new float[HarpBlur.Rings], Lo = new float[HarpBlur.Rings];
        public float BlurH, BlurT, BlurGain = 1f, BlurSwing;
        public bool Blurring;
        /// <summary>Set when the envelope (and so the blur's alphas) changed this frame (the view uploads them).</summary>
        public bool BlurDirty;
        public PointerRecord HeldBy;
        public float TargetK;
        public int TargetFrame = -10;
        /// <summary>Set by Animate when the mesh shape changed this frame (the view uploads it and clears the flag).</summary>
        public bool ShapeDirty;

        public HarpString(in StringLayout l)
        {
            Index = l.Index; Midi = l.Midi; A = l.A; B = l.B; Length = l.Length; Radius = l.Radius; Colour = l.Colour;
            Ax = l.A.x; Ay = l.A.y; Dx = (double)l.B.x - l.A.x; Dy = (double)l.B.y - l.A.y;
            Len2 = Dx * Dx + Dy * Dy;
            LenD = Math.Sqrt(Len2);
        }
    }

    /// <summary>A fingertip's record on the harp (one per pointer): its dwell (rest) and the string it holds (catch).</summary>
    public sealed class PointerRecord
    {
        public Pointer Pointer;
        public bool InZone;
        public int Side = 1;
        public double Since = double.NaN;   // dwell start (Clock.NowMs), NaN = not dwelling
        public float X0, Y0;
        public HarpString Held;
        public PluckMode Mode = PluckMode.Across;
        public double Ud = 1, Uz;           // the stroke's direction across (d) and through (z) the string
        public float U = 0.5f, Speed;
        /// <summary>HarpPlay.Frame of this fingertip's last sound (a glissando in progress keeps the block-aligned base).</summary>
        public int LastSoundFrame = int.MinValue / 2;
    }

    public sealed class HarpPlay
    {
        // ---- the pluck: catch and release ----
        /// <summary>Sweep catch: within this of a string when sweeping along the plane (the finger's radius plus a little).</summary>
        public const double ZoneZ = 0.012;
        /// <summary>Poke catch: within this of a string when poking through the plane (only the nearest is taken).</summary>
        public const double PokeR = 0.009;
        /// <summary>A caught string bends with the finger until pulled this far past its line, then slips off and sounds.</summary>
        public const double CatchMax = 0.006;
        /// <summary>A lift-off release closer to the rest line than this is a touch, not a pluck.</summary>
        public const double MinLift = 0.002;
        /// <summary>Hysteresis for leaving the zone, so a held string does not drop on a millimetre of jitter.</summary>
        public const double HoldSlack = 1.5;
        /// <summary>Slower crossings (m/s) are tracking jitter or a hand being placed on the strings.</summary>
        public const double PluckSpeed = 0.2;
        public const double RetriggerMs = 70;
        // A hand resting on the strings: from REST_Z_OUT short of the plane on its own side to REST_Z_IN through it,
        // slower than REST_SPEED, within REST_DRIFT for REST_HOLD_MS. The drift radius tells a resting hand from a sweep.
        public const float RestZOut = 0.005f, RestZIn = 0.03f, RestSpeed = 0.12f, RestDrift = 0.01f;
        public const double RestHoldMs = 150;
        public const double RestReach = 0.02;
        /// <summary>A string this hand plucked this recently is still its follow-through, not a hand laid on it.</summary>
        public const double PluckGuardMs = 400;
        // ---- what the player sees ----
        public const float HoverZ = 0.05f, HoverReach = 0.02f;
        public const float HoverLength = 0.07f, HoverWidth = 0.016f;
        public const float VibBase = 0.0012f, VibVel = 0.0028f;
        public const float DampTau = 0.035f;
        /// <summary>E8: the pluck's warm flash at a quarter of its former strength: the blur itself shows the pluck.</summary>
        public const float PluckFlashGain = 0.25f;
        /// <summary>The pointer zone: |z| &lt;= .08, .1 &lt;= y &lt;= 1.5, -.75 &lt;= x &lt;= .12.</summary>
        public const float ZoneHalfZ = 0.08f, ZoneMinY = 0.1f, ZoneMaxY = 1.5f, ZoneMinX = -0.75f, ZoneMaxX = 0.12f;
        // HARP_PLANE / HARP_REACH: the play zone (a pinch there is play, never a grab).
        public const float PlayPlane = 0.05f, PlayReach = 0.04f;
        // Desktop click: nearest string within 2 cm, velocity .7, a 'through' pluck at .9 m/s.
        public const float DesktopReach = 0.02f, DesktopVelocity = 0.7f, DesktopSpeed = 0.9f;
        public const int MaxPointers = 16, MaxHands = 4;
        /// <summary>A fingertip that sounded a string this many frames ago (or fewer) is still sweeping: its next sound keeps
        /// the block-aligned base, so a glissando's spacing stays exact across frames (one frame of slack covers a frame
        /// without a fresh tracker sample at 72 Hz).</summary>
        public const int SweepFrames = 2;

        public const string ModeThrough = "through", ModeAcross = "across";

        /// <summary>A plucked string lights up warm for a moment; a lesson's target glows the lesson's purple (linear RGB).</summary>
        public static readonly Color PluckGlow = ((Color)new Color32(0xff, 0xc4, 0x6a, 0xff)).linear;
        /// <summary>A lesson target's glow: the lesson cue's purple (LessonCue; was cyan #58dcff), linear.</summary>
        public static readonly Color TargetGlow = LessonCue.Linear;

        /// <summary>Loudness from the finger's speed at the release: the same speed always gives the same velocity.</summary>
        public static float VelocityOf(double speed) => (float)Math.Min(1, Math.Max(0.2, (speed - 0.03) / 1.2));
        /// <summary>The velocity of a release at this speed.</summary>
        public float Velocity(double speed) => VelocityOf(speed);

        public static string ModeName(PluckMode mode) => mode == PluckMode.Across ? ModeAcross : ModeThrough;
        public static PluckMode ParseMode(string mode) => mode == ModeAcross ? PluckMode.Across : PluckMode.Through;

        public readonly HarpString[] Strings;
        public IHarpSound Sound;
        public IRecorder Recorder;
        /// <summary>Clock.NowMs (frame-constant); set before Update and before every API call.</summary>
        public double NowMs;
        public int Frame { get; private set; }
        /// <summary>Counters for tests and the device log.</summary>
        public int Plucks { get; private set; }
        public int Damps { get; private set; }

        readonly PointerRecord[] _records = new PointerRecord[MaxPointers];
        int _recordCount;
        readonly RestTally[] _tallies = new RestTally[MaxHands];
        int _tallyCount;
        readonly StrokeEvent[] _events = new StrokeEvent[HarpGeometry.StringCount];
        int _eventCount;
        // Set by Stroke while it dispatches: the move is part of a sweep (more than one sound, or a glissando continuing
        // from the last frames), so its plucks share FrameNow + one block + their delays; otherwise a pluck plays ASAP.
        bool _sweep;
        PointerRecord _stroking;

        sealed class RestTally
        {
            public IPointerOwner Hand;
            public int Count, Lo = int.MaxValue, Hi = int.MinValue;
            public readonly int[] Under = { -1, -1, -1, -1, -1 };
        }

        struct StrokeEvent
        {
            public HarpString S;
            public double F, U, Ud, Uz, Off, From, To, Speed;
            public PluckMode Mode;
        }

        public HarpPlay()
        {
            StringLayout[] layout = HarpGeometry.Strings;
            Strings = new HarpString[layout.Length];
            for (int i = 0; i < layout.Length; i++) Strings[i] = new HarpString(layout[i]);
        }

        /// <summary>The audio clock (or the frame clock when there is no engine, so voices still expire).</summary>
        public double AudioNow => Sound != null ? Sound.Now : NowMs * 0.001;
        double PluckBase => Sound != null ? Sound.PluckBase : NowMs * 0.001;
        double RingSeconds(int midi) => Sound != null ? Sound.RingSeconds(midi) : Math.Min(4.5, Math.Max(0.7, 4.5 - (midi - 48) * 0.08));
        /// <summary>tau of partial n (1-based) of string i, from the harp sound that plays (the v2 design without an engine).</summary>
        float PartialTau(int i, int n) => Sound != null ? Sound.PartialTau(i, n) : HarpPartialDecay.V2Tau(i, n);
        // E8 envelope scratch (main thread).
        readonly float[] _blurC = new float[HarpBlur.Modes];

        // ---------------- geometry helpers ----------------

        /// <summary>Side(s, x, y): u along the string (0 at the soundboard, 1 at the neck) and the signed in-plane
        /// distance from its line, positive toward the pillar.</summary>
        public static void Side(HarpString s, double x, double y, out double u, out double dist)
        {
            double px = x - s.Ax, py = y - s.Ay;
            u = (px * s.Dx + py * s.Dy) / s.Len2;
            dist = (s.Dx * py - s.Dy * px) / s.LenD;
        }

        /// <summary>Across(s, x, y, z): d (in the plane), z (through it), u (along it).</summary>
        static void Across(HarpString s, Vector3 v, out double d, out double z, out double u)
        {
            Side(s, v.x, v.y, out u, out d);
            z = v.z;
        }

        static bool InBox(Vector3 local, float halfZ) =>
            Mathf.Abs(local.z) <= halfZ && local.y >= ZoneMinY && local.y <= ZoneMaxY && local.x >= ZoneMinX && local.x <= ZoneMaxX;

        static double Clamp(double v, double a, double b) => Math.Min(b, Math.Max(a, v));

        // ---------------- the frame ----------------

        /// <summary>Update(pointers, dt): every pointer (plus an optional extra one, the desktop mouse), palm
        /// damping, voices that have played out, then every string's motion.</summary>
        public void Update(IReadOnlyList<Pointer> pointers, Pointer extra, in Matrix4x4 worldToLocal, float dt)
        {
            Frame++;
            for (int i = 0; i < _tallyCount; i++)
            {
                RestTally t = _tallies[i];
                t.Count = 0;
                t.Lo = int.MaxValue;
                t.Hi = int.MinValue;
            }
            if (pointers != null) for (int k = 0; k < pointers.Count; k++) UpdatePointer(pointers[k], worldToLocal, dt);
            if (extra != null) UpdatePointer(extra, worldToLocal, dt);
            DampHands();
            double at = AudioNow;
            for (int i = 0; i < Strings.Length; i++)
            {
                HarpString s = Strings[i];
                // A voice whose sample has played out is silent: let it go.
                if (s.HasVoice && at >= s.Until) { s.HasVoice = false; s.Voice = VoiceHandle.None; }
                Animate(s, dt);
            }
        }

        void UpdatePointer(Pointer p, in Matrix4x4 w2l, float dt)
        {
            if (p == null) return;
            // The desktop mouse hand is a visual stand-in (ARCHITECTURE.md section 0): its fingertip rests 5 mm above the
            // string-plane collider and pokes through it on a click, which the harp's own desktop path already plays.
            // It never catches, plucks or rests, so a click never sounds twice and moving the mouse never sweeps.
            if (!p.Active || !p.WasActive || p.FromMouse) { Leave(p); return; }
            Vector3 tip = w2l.MultiplyPoint3x4(p.Tip);
            if (!InBox(tip, ZoneHalfZ)) { Leave(p); return; }
            Vector3 prev = w2l.MultiplyPoint3x4(p.Prev);
            // Hand poses arrive at the tracker's own rate, so speeds are over its interval (as the piano measures them).
            float sdt = Mathf.Max(p.SampleDt > 0f ? p.SampleDt : dt, 1e-3f);
            PointerRecord rec = Record(p);
            if (rec == null) return;
            // Frames without a new hand pose change nothing: a held string stays held.
            double mx = (double)tip.x - prev.x, my = (double)tip.y - prev.y, mz = (double)tip.z - prev.z;
            bool moved = mx * mx + my * my + mz * mz > 1e-12;
            // A fingertip settling on the strings is placing or damping, not plucking: a string it held goes back silently.
            if (p.Owner != null && Rest(p, rec, tip, prev))
            {
                if (rec.Held != null && moved) LetGo(rec, false, null, 0f, 0);
                return;
            }
            if (moved) Stroke(p, rec, tip, prev, sdt);
        }

        /// <summary>A fingertip's record, made on first sight (null only if more than MaxPointers pointers exist).</summary>
        PointerRecord Record(Pointer p)
        {
            for (int i = 0; i < _recordCount; i++) if (ReferenceEquals(_records[i].Pointer, p)) return _records[i];
            if (_recordCount >= _records.Length) return null;
            var r = new PointerRecord { Pointer = p };
            _records[_recordCount++] = r;
            return r;
        }

        /// <summary>The pointer's record if it has one (tests, the swing direction of a pluck).</summary>
        public PointerRecord RecordOf(Pointer p)
        {
            if (p == null) return null;
            for (int i = 0; i < _recordCount; i++) if (ReferenceEquals(_records[i].Pointer, p)) return _records[i];
            return null;
        }

        RestTally Tally(IPointerOwner hand)
        {
            for (int i = 0; i < _tallyCount; i++) if (ReferenceEquals(_tallies[i].Hand, hand)) return _tallies[i];
            if (_tallyCount >= _tallies.Length) return null;
            var t = new RestTally { Hand = hand };
            _tallies[_tallyCount++] = t;
            return t;
        }

        /// <summary>Leave(p): a fingertip that left the strings or stopped being tracked starts its dwell over, reads
        /// its side afresh, and a string it held goes back silently.</summary>
        public void Leave(Pointer p)
        {
            PointerRecord rec = RecordOf(p);
            if (rec == null) return;
            rec.InZone = false;
            rec.Since = double.NaN;
            if (rec.Held != null) LetGo(rec, false, null, 0f, 0);
        }

        /// <summary>Every fingertip leaves (the harp left the scene or was disabled): held strings return silently.</summary>
        public void LeaveAll()
        {
            for (int i = 0; i < _recordCount; i++)
            {
                PointerRecord rec = _records[i];
                rec.InZone = false;
                rec.Since = double.NaN;
                if (rec.Held != null) LetGo(rec, false, null, 0f, 0);
            }
        }

        /// <summary>Rest(p, now): is this tip settling or resting on the strings? Keeps its dwell; once still long
        /// enough it counts for its hand with the string under it. A settling or resting tip never plucks.</summary>
        bool Rest(Pointer p, PointerRecord rec, Vector3 tip, Vector3 prev)
        {
            // A hand reaches the strings from its own side, the side it came in on. Short of the plane on that side it is
            // hovering, aiming the next note; at the plane or pushed through it, it is on the strings.
            if (!rec.InZone)
            {
                rec.InZone = true;
                rec.Side = (prev.z != 0f ? prev.z : tip.z) < 0f ? -1 : 1;
            }
            float zs = tip.z * rec.Side;
            if (zs > RestZOut || zs < -RestZIn || p.Speed >= RestSpeed)
            {
                rec.Since = double.NaN;
                return false;
            }
            int best = -1;
            double bd = RestReach;
            for (int i = 0; i < Strings.Length; i++)
            {
                Side(Strings[i], tip.x, tip.y, out double u, out double dist);
                if (u < 0.02 || u > 0.98) continue;
                double d = Math.Abs(dist);
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0)
            {
                rec.Since = double.NaN;
                return false;
            }
            // The dwell: from where the tip first settled, and over again if it wanders off.
            float hx = tip.x - rec.X0, hy = tip.y - rec.Y0;
            if (double.IsNaN(rec.Since) || Math.Sqrt(hx * hx + hy * hy) > RestDrift)
            {
                rec.Since = NowMs;
                rec.X0 = tip.x;
                rec.Y0 = tip.y;
            }
            if (NowMs - rec.Since < RestHoldMs) return true;
            RestTally st = Tally(p.Owner);
            if (st != null)
            {
                if (st.Count < st.Under.Length) st.Under[st.Count] = best;
                st.Count++;
                if (best < st.Lo) st.Lo = best;
                if (best > st.Hi) st.Hi = best;
            }
            return true;
        }

        static double DelayOf(ref double first, double f, float sdt)
        {
            if (first < 0) first = f;
            return Math.Max(0, f - first) * sdt;
        }

        /// <summary>Stroke(p, rec, sdt): one fingertip's move from prev to tip (local). What matters is the path in
        /// the plane square to each string (across it in the string plane, and through the plane). A string is caught when
        /// the path passes its line within reach (ZONE_Z sweeping, POKE_R poking), bends with the finger until pulled
        /// CATCH_MAX past its line and slips off sounding. Strings crossed in one move are caught in the order crossed and
        /// released at once if already past the slip point, so a fast sweep plays each string once, in order, spread over
        /// the frame's interval (the glissando delays).</summary>
        void Stroke(Pointer p, PointerRecord rec, Vector3 tip, Vector3 prev, float sdt)
        {
            double first = -1;   // the release fraction of the first sound this frame: later ones follow it
            double after = 0;    // fraction of the move already used by the held string's release
            HarpString skip = null;
            // What happens to the held string (decided here, dispatched below once the move's sounds are counted, in the
            // fixed order: the held string's release comes before the strings this move crosses).
            int held = HeldKeep;
            float heldVel = 0f;
            double heldF = 0;
            if (rec.Held != null)
            {
                HarpString s = rec.Held;
                Across(s, prev, out double d0, out double z0, out _);
                double was = d0 * rec.Ud + z0 * rec.Uz;
                Across(s, tip, out double od, out double oz, out double ou);
                double d = od * rec.Ud + oz * rec.Uz;
                double off = Math.Abs(od * rec.Uz - oz * rec.Ud);
                double reach = (Math.Abs(rec.Uz) > Math.Abs(rec.Ud) ? PokeR : ZoneZ) * HoldSlack;
                skip = s; // whatever happens to it now, this move does not catch it again
                if (d >= CatchMax)
                {
                    // Slipped off the fingertip, at this fraction of the move, as fast as it was being pulled.
                    double f = d > was ? Clamp((CatchMax - was) / (d - was), 0, 1) : 1;
                    rec.Speed = (float)Math.Max((d - was) / sdt, PluckSpeed);
                    held = HeldSound;
                    heldVel = Velocity(rec.Speed);
                    heldF = f;
                    after = f;
                }
                else if (d < -MinLift)
                {
                    held = HeldSilent; // backed off: it goes back without a sound
                }
                else if (off > reach || ou < 0.02 || ou > 0.98)
                {
                    // Lifted or slid off sideways while holding it: it sounds by how far, and how fast, it was being
                    // pulled (not by how fast the finger left).
                    if (d >= MinLift)
                    {
                        held = HeldSound;
                        heldVel = VelocityOf(rec.Speed) * (float)(d / CatchMax);
                        heldF = 0;
                    }
                    else held = HeldSilent;
                }
                else
                {
                    s.Bend = (float)Math.Max(0, d);
                    s.Moving = true;
                    return; // still holding it: nothing else is caught until it lets go
                }
            }

            _eventCount = 0;
            for (int i = 0; i < Strings.Length; i++)
            {
                HarpString s = Strings[i];
                if (s == skip) continue;
                Across(s, prev, out double d0, out double z0, out _);
                Across(s, tip, out double d1, out double z1, out _);
                double dd = d1 - d0, dz = z1 - z0;
                double len = Math.Sqrt(dd * dd + dz * dz);
                if (len < 1e-6 || len / sdt < PluckSpeed) continue;
                double ud = dd / len, uz = dz / len;
                // Signed distance along the stroke, negative before the string. Crossed when it goes from below zero to
                // zero or above; a move ending on the string (to within rounding) counts in this frame, and the string,
                // held from then on, is not caught again by the next move.
                double from = d0 * ud + z0 * uz;
                if (!(from < 0 && d1 * ud + z1 * uz >= -1e-9)) continue;
                double f = -from / len;
                if (f < after) continue;
                bool poke = Math.Abs(uz) > Math.Abs(ud);
                double off = Math.Abs(d0 * uz - z0 * ud);
                if (off > (poke ? PokeR : ZoneZ)) continue;
                Side(s, prev.x + f * (tip.x - prev.x), prev.y + f * (tip.y - prev.y), out double u, out _);
                if (u < 0.03 || u > 0.97) continue;
                _events[_eventCount++] = new StrokeEvent
                {
                    S = s, F = f, U = u, Ud = ud, Uz = uz, Off = off, From = from, To = from + len, Speed = len / sdt,
                    Mode = poke ? PluckMode.Through : PluckMode.Across,
                };
            }
            if (_eventCount == 0)
            {
                // Nothing crossed: only the held string's release (one sound at most, so ASAP unless still sweeping).
                if (held != HeldKeep)
                {
                    BeginSounds(rec, held == HeldSound ? 1 : 0);
                    ReleaseHeld(p, rec, held, heldVel, heldF, ref first, sdt);
                    EndSounds();
                }
                return;
            }
            // A poke goes through the plane once: of the strings it passes near, only the nearest is plucked (in the
            // treble the strings are 12 mm apart, and a poke between two must not take both).
            int nearest = -1;
            for (int k = 0; k < _eventCount; k++)
                if (_events[k].Mode == PluckMode.Through && (nearest < 0 || _events[k].Off < _events[nearest].Off)) nearest = k;
            if (nearest >= 0)
            {
                int w = 0;
                for (int k = 0; k < _eventCount; k++)
                {
                    if (_events[k].Mode == PluckMode.Through && k != nearest) continue;
                    _events[w++] = _events[k];
                }
                _eventCount = w;
            }
            // Order of crossing; stable like JavaScript's Array.prototype.sort (ties keep string order).
            for (int k = 1; k < _eventCount; k++)
            {
                StrokeEvent e = _events[k];
                int j = k - 1;
                while (j >= 0 && _events[j].F > e.F) { _events[j + 1] = _events[j]; j--; }
                _events[j + 1] = e;
            }
            // How many sounds this move makes: the held string's release plus every crossed string already past its slip
            // point (a string another fingertip holds is skipped below, so it does not count either).
            int sounds = held == HeldSound ? 1 : 0;
            for (int k = 0; k < _eventCount; k++)
            {
                HarpString s = _events[k].S;
                if ((s.HeldBy == null || s.HeldBy == rec) && _events[k].To >= CatchMax) sounds++;
            }
            BeginSounds(rec, sounds);
            ReleaseHeld(p, rec, held, heldVel, heldF, ref first, sdt);
            for (int k = 0; k < _eventCount; k++)
            {
                StrokeEvent e = _events[k];
                HarpString s = e.S;
                // A string another fingertip holds stays with that fingertip.
                if (s.HeldBy != null && s.HeldBy != rec) continue;
                rec.Mode = e.Mode;
                rec.Ud = e.Ud;
                rec.Uz = e.Uz;
                rec.U = (float)e.U;
                rec.Speed = (float)e.Speed;
                if (e.To >= CatchMax)
                {
                    // Already past the slip point by the end of the move: caught and released in passing, at the fraction
                    // of the move where the tip reached the slip point.
                    double f = Clamp((CatchMax - e.From) / (e.To - e.From), e.F, 1);
                    var o = new StrokeInfo { U = (float)e.U, Speed = (float)e.Speed, Mode = e.Mode, Delay = DelayOf(ref first, f, sdt) };
                    Pluck(s, Velocity(e.Speed), o, true, p);
                }
                else if (k == _eventCount - 1)
                {
                    // The last string crossed, not yet pulled to the slip point: the fingertip holds it.
                    rec.Held = s;
                    s.HeldBy = rec;
                    s.BendU = (float)e.U;
                    s.BendD = (float)e.Ud;
                    s.BendZ = (float)e.Uz;
                    s.Bend = (float)Math.Max(0, e.To);
                    s.Moving = true;
                    if (p.Owner != null) p.Owner.LastPlayMs = NowMs;
                }
            }
            EndSounds();
        }

        const int HeldKeep = 0, HeldSound = 1, HeldSilent = 2;

        /// <summary>ARCHITECTURE.md 15.3 / section 17: a move that makes more than one sound, or a glissando still going from
        /// the last frames, schedules its plucks at FrameNow + one block + their delays, which keeps a sweep's spacing
        /// exact; a single, isolated sound (a poke, one slip, a lift-off) plays ASAP (when = 0).</summary>
        void BeginSounds(PointerRecord rec, int sounds)
        {
            _stroking = rec;
            _sweep = sounds > 1 || Frame - rec.LastSoundFrame <= SweepFrames;
        }

        void EndSounds()
        {
            _stroking = null;
            _sweep = false;
        }

        /// <summary>The held string's fate decided at the top of Stroke: a sounding release (slip or lift-off) or a silent
        /// return. Runs before the crossed strings, so it takes the first glissando slot.</summary>
        void ReleaseHeld(Pointer p, PointerRecord rec, int held, float vel, double f, ref double first, float sdt)
        {
            if (held == HeldSound) LetGo(rec, true, p, vel, DelayOf(ref first, f, sdt));
            else if (held == HeldSilent) LetGo(rec, false, null, 0f, 0);
        }

        /// <summary>LetGo: the held string goes, with a pluck (a slip or a lift-off) or silently back to rest.</summary>
        void LetGo(PointerRecord rec, bool sound, Pointer p, float vel, double delay)
        {
            HarpString s = rec.Held;
            rec.Held = null;
            if (s == null) return;
            if (s.HeldBy == rec) s.HeldBy = null;
            s.Bend = 0f;
            s.Moving = true;
            if (sound) Pluck(s, vel, new StrokeInfo { U = rec.U, Speed = rec.Speed, Mode = rec.Mode, Delay = delay }, true, p);
        }

        /// <summary>Pluck(s, vel, opts, p). hasOpts = false means no stroke options (defaults, mode swing).
        /// Returns false inside the 70 ms retrigger guard.</summary>
        public bool Pluck(HarpString s, float vel, in StrokeInfo stroke, bool hasOpts, Pointer p)
        {
            double now = NowMs;
            if (now - s.LastPluck < RetriggerMs) return false;
            s.LastPluck = now;
            IPointerOwner owner = p != null ? p.Owner : null;
            s.PluckedBy = owner;
            // A hand that is plucking is playing, not grabbing: its pinches are ignored for a moment.
            if (owner != null) owner.LastPlayMs = now;
            // The finger meets the string before it lets go, so a string still ringing stops first (the old voice is released).
            if (s.HasVoice)
            {
                Sound?.Release(s.Voice);
                s.HasVoice = false;
                s.Voice = VoiceHandle.None;
            }
            StrokeInfo o = hasOpts ? stroke : StrokeInfo.Default;
            // It swings along the stroke that let it go (across the plane for a sweep, through it for a poke).
            PointerRecord held = s.HeldBy ?? RecordOf(p);
            float ampD, ampZ;
            if (held != null && hasOpts) { ampD = (float)held.Ud; ampZ = (float)held.Uz; }
            else
            {
                bool across = o.Mode == PluckMode.Across;
                ampD = across ? 1f : 0f;
                ampZ = across ? 0f : 1f;
            }
            // Only the members of a sweep share the block-aligned base (their spacing must survive the audio thread
            // draining the batch a block later); anything else is live and plays ASAP (section 17).
            double when = o.Delay > 0 || _sweep ? PluckBase + o.Delay : 0;
            if (_stroking != null) _stroking.LastSoundFrame = Frame;
            if (Sound != null) s.Voice = Sound.Pluck(s.Index, vel, when, new PluckOptions { U = o.U, Speed = o.Speed, Mode = o.Mode, Delay = 0, Ud = Mathf.Abs(ampD), Uz = Mathf.Abs(ampZ) });
            s.HasVoice = true;
            Plucks++;
            // Recorded with where and how it was plucked, so playback replays the whole stroke.
            Recorder?.Add(TakeEvent.Harp(s.Index, vel, o.U, o.Speed, ModeName(o.Mode)));
            // It rings visibly from where it was plucked, as long as it sounds.
            s.Ring = (float)RingSeconds(s.Midi);
            s.Until = Math.Max(when, AudioNow) + s.Ring;
            s.Amp = VibBase + VibVel * vel;
            s.AmpD = ampD;
            s.AmpZ = ampZ;
            s.AmpU = Mathf.Clamp(o.U, 0.05f, 0.95f);
            s.Damped = false;
            s.Bend = 0f;
            s.Flash = 1f;
            s.Moving = true;
            StartBlur(s);
            return true;
        }

        /// <summary>E8: a fresh pluck replaces the string's motion (the finger caught it): b_n of a triangle of the pluck's
        /// height at the pluck point, tau_n from the sound, and the envelope at t = 0 shown on this very frame.</summary>
        void StartBlur(HarpString s)
        {
            SeedBlur(s, s.Amp);
            UpdateBlur(s);
            s.ShapeDirty = true;
        }

        /// <summary>E8: a blur of height h at the string's pluck point, aged 0 (a pluck; with no stroke direction it swings
        /// through the plane from its middle).</summary>
        void SeedBlur(HarpString s, float h)
        {
            if (s.AmpD == 0f && s.AmpZ == 0f) s.AmpZ = 1f;
            if (s.AmpU <= 0f) s.AmpU = 0.5f;
            s.BlurH = h;
            HarpBlur.PluckCoeffs(s.AmpU, s.BlurH, s.BlurB);
            for (int n = 0; n < HarpBlur.Modes; n++) s.BlurTau[n] = PartialTau(s.Index, n + 1);
            s.BlurT = 0f;
            s.BlurGain = 1f;
            s.Blurring = true;
        }

        /// <summary>E8: the envelope at the string's current age; below 0.05 mm of swing the string is still.</summary>
        void UpdateBlur(HarpString s)
        {
            s.BlurSwing = HarpBlur.Envelope(s.BlurB, s.BlurTau, s.BlurT, s.BlurGain, _blurC, s.Up, s.Lo);
            s.BlurDirty = true;
            s.Amp = s.BlurH * s.BlurGain * Mathf.Exp(-s.BlurT / Mathf.Max(s.BlurTau[0], 1e-4f));
            if (s.BlurSwing >= HarpBlur.StopSwing) return;
            s.Blurring = false;
            s.BlurSwing = 0f;
            s.Amp = 0f;
            Array.Clear(s.Up, 0, s.Up.Length);
            Array.Clear(s.Lo, 0, s.Lo.Length);
        }

        /// <summary>Damp(s, n): a hand on a ringing string stops it with a short fade; n = how many ringing strings
        /// the hand landed on at once, for the loudness of the "shh".</summary>
        public void Damp(HarpString s, int n = 1)
        {
            s.Damped = true;
            if (!s.HasVoice) return;
            VoiceHandle v = s.Voice;
            s.HasVoice = false;
            s.Voice = VoiceHandle.None;
            Sound?.Damp(v, n);
            Damps++;
            Recorder?.Add(TakeEvent.HarpDamp(s.Index));
        }

        /// <summary>Hands on the strings. Two or more tips of one hand resting on two or more strings is a hand on the
        /// harp: two placed fingers hold their own strings, three or more are a palm across the whole span between them.
        /// A string the hand itself just plucked is left to its follow-through. Resting is playing too, so the curled
        /// fingers of a hand on the strings are not read as a grab.</summary>
        void DampHands()
        {
            for (int k = 0; k < _tallyCount; k++)
            {
                RestTally st = _tallies[k];
                if (st.Count < 2 || st.Hi <= st.Lo) continue;
                if (st.Hand != null) st.Hand.LastPlayMs = NowMs;
                bool span = st.Count >= 3;
                int n = span ? st.Hi - st.Lo + 1 : Math.Min(st.Count, st.Under.Length);
                // First count the strings still sounding under the hand, then stop them with a "shh" to match.
                int live = 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        HarpString s = Strings[span ? st.Lo + j : st.Under[j]];
                        if (s.PluckedBy == st.Hand && NowMs - s.LastPluck < PluckGuardMs) continue;
                        if (pass == 0) { if (s.HasVoice) live++; continue; }
                        if (s.HasVoice) Damp(s, live);
                    }
                }
            }
        }

        /// <summary>Animate (shape bookkeeping), E8: the ringing is the blur's envelope, which ages with the
        /// partials' own decays (and dies in 35 ms under a hand); it is recomputed every frame for 0.1 s after the pluck,
        /// then every other frame (alternate strings on alternate frames). The flash fades. There is no per-frame
        /// alternating bow (it strobed at 45 Hz).</summary>
        void Animate(HarpString s, float dt)
        {
            bool env = false;
            if (s.Blurring)
            {
                s.BlurT += dt;
                if (s.Damped) s.BlurGain *= Mathf.Exp(-dt / DampTau);
                s.Amp = s.BlurH * s.BlurGain * Mathf.Exp(-s.BlurT / Mathf.Max(s.BlurTau[0], 1e-4f));
                if (s.BlurT < HarpBlur.FullRateFor || ((Frame + s.Index) & 1) == 0)
                {
                    UpdateBlur(s);
                    env = true;
                }
                s.Moving = true;
            }
            if (s.Flash > 0f) s.Flash = Mathf.Max(0f, s.Flash - dt * 4f);
            if (!s.Moving) return;
            bool bent = s.Bend > 0f;
            s.Moving = s.Blurring || bent;
            // A write when the envelope moved, while a finger bends it, and once more when it comes to rest.
            if (env || bent || !s.Moving) s.ShapeDirty = true;
        }

        /// <summary>Animate (vertices), E8: the string's CORE, at rest or held (straight lines from each end to the
        /// fingertip); the ringing is drawn by the two edge copies (DeformEdge). Writes rest + offset into dst. The mesh's x
        /// lies in the plane across the string, pointing to the side where Side().dist is negative (hence the minus), its z
        /// is the harp's z.</summary>
        public static void Deform(HarpString s, Vector3[] rest, Vector3[] dst, int count)
        {
            DeformTerms(s, out float bend, out float bu, out float invLen);
            for (int k = 0; k < count; k++) dst[k] = DeformVertex(s, rest[k], 0f, bend, bu, invLen);
        }

        /// <summary>One edge copy of the blur: the core shape plus the upper (or lower) envelope of ring j along the swing.</summary>
        public static void DeformEdge(HarpString s, Vector3[] rest, Vector3[] dst, int count, bool upper)
        {
            DeformTerms(s, out float bend, out float bu, out float invLen);
            float[] env = upper ? s.Up : s.Lo;
            for (int k = 0; k < count; k++) dst[k] = DeformVertex(s, rest[k], env[RingOf(rest[k], invLen)], bend, bu, invLen);
        }

        /// <summary>The core written straight into the harp's one strings mesh: string-local rest positions are deformed,
        /// then placed in harp-local space (pos + rot * v, rot a turn about z) at dst[offset ..]. The view uploads the whole
        /// array with one SetVertices per frame (ARCHITECTURE.md 15.3).</summary>
        public static void Deform(HarpString s, Vector3[] rest, NativeArray<Vector3> dst, int offset, Vector3 pos, Quaternion rot)
        {
            DeformTerms(s, out float bend, out float bu, out float invLen);
            for (int k = 0; k < rest.Length; k++) dst[offset + k] = pos + rot * DeformVertex(s, rest[k], 0f, bend, bu, invLen);
        }

        /// <summary>An edge copy written into the strings mesh (see DeformEdge).</summary>
        public static void DeformEdge(HarpString s, Vector3[] rest, NativeArray<Vector3> dst, int offset, Vector3 pos, Quaternion rot, bool upper)
        {
            DeformTerms(s, out float bend, out float bu, out float invLen);
            float[] env = upper ? s.Up : s.Lo;
            for (int k = 0; k < rest.Length; k++)
                dst[offset + k] = pos + rot * DeformVertex(s, rest[k], env[RingOf(rest[k], invLen)], bend, bu, invLen);
        }

        /// <summary>The envelope ring (0 .. 16, u = j / 16 from the soundboard) a string-local vertex belongs to.</summary>
        public static int RingOf(Vector3 rest, float invLen)
        {
            int j = Mathf.RoundToInt((rest.y * invLen + 0.5f) * HarpBlur.Segments);
            return j < 0 ? 0 : j > HarpBlur.Segments ? HarpBlur.Segments : j;
        }

        static void DeformTerms(HarpString s, out float bend, out float bu, out float invLen)
        {
            bend = Mathf.Min(s.Bend, (float)CatchMax);
            bu = Mathf.Clamp(s.BendU, 0.02f, 0.98f);
            invLen = 1f / s.Length;
        }

        /// <summary>rest + the bend's triangle + <paramref name="swing"/> metres along the stroke's direction (AmpD across
        /// the plane, AmpZ through it).</summary>
        static Vector3 DeformVertex(HarpString s, Vector3 r, float swing, float bend, float bu, float invLen)
        {
            float u = r.y * invLen + 0.5f;
            float tri = u < bu ? u / bu : (1f - u) / (1f - bu);
            float ox = bend * tri * s.BendD + swing * s.AmpD;
            float oz = bend * tri * s.BendZ + swing * s.AmpZ;
            return new Vector3(r.x - ox, r.y, r.z + oz);
        }

        /// <summary>Paint: a lesson's target glows purple while it waits (this frame or last); a plucked string
        /// flashes warm. Linear RGB (colours are mixed in linear space), alpha 1.</summary>
        public Color Glow(HarpString s)
        {
            float target = s.TargetFrame >= Frame - 1 ? s.TargetK : 0f;
            float r = 0f, g = 0f, b = 0f;
            if (target > 0f)
            {
                float k = 0.35f + 0.65f * target;
                r = TargetGlow.r * k; g = TargetGlow.g * k; b = TargetGlow.b * k;
            }
            if (s.Flash > 0f)
            {
                float t = s.Flash * 0.8f * PluckFlashGain;
                r += (PluckGlow.r - r) * t; g += (PluckGlow.g - g) * t; b += (PluckGlow.b - b) * t;
            }
            return new Color(r, g, b, 1f);
        }

        // ---------------- API (lessons, playback, desktop, hover, zones) ----------------

        /// <summary>Highlight(i, k): a lesson's target, string i glows with strength k (0..1) this frame.</summary>
        public void Highlight(int i, float k)
        {
            if (i < 0 || i >= Strings.Length) return;
            HarpString s = Strings[i];
            s.TargetK = Mathf.Max(s.TargetFrame == Frame ? s.TargetK : 0f, k);
            s.TargetFrame = Frame;
        }

        /// <summary>PlaybackPluck(i, vel, e): replays the whole stroke (where and how it was plucked).</summary>
        public bool PlaybackPluck(int i, float vel, float u, float speed, PluckMode mode)
        {
            if (i < 0 || i >= Strings.Length) return false;
            return Pluck(Strings[i], vel, new StrokeInfo { U = u, Speed = speed, Mode = mode }, true, null);
        }

        public void PlaybackDamp(int i)
        {
            if (i < 0 || i >= Strings.Length) return;
            Damp(Strings[i]);
        }

        /// <summary>Hover: the nearest string with u in [.02, .98] within HOVER_REACH of a tip that is within HOVER of
        /// the plane and inside the zone box; strength = 1 - |z| / HOVER.</summary>
        public bool HoverPick(Vector3 local, out int best, out float u, out float strength)
        {
            best = -1; u = 0f; strength = 0f;
            if (!InBox(local, HoverZ)) return false;
            double bd = HoverReach;
            for (int i = 0; i < Strings.Length; i++)
            {
                Side(Strings[i], local.x, local.y, out double su, out double dist);
                if (su < 0.02 || su > 0.98) continue;
                double d = Math.Abs(dist);
                if (d < bd) { bd = d; best = i; u = (float)su; }
            }
            if (best < 0) return false;
            strength = 1f - Mathf.Abs(local.z) / HoverZ;
            return true;
        }

        /// <summary>DesktopDown(hit): a click plucks the nearest string (u in [0, 1]) within 2 cm, where it landed,
        /// at a middling speed. Returns the string plucked, or -1.</summary>
        public int DesktopDown(Vector3 local)
        {
            HarpString best = null;
            double bd = double.PositiveInfinity, bu = 0.5;
            for (int i = 0; i < Strings.Length; i++)
            {
                Side(Strings[i], local.x, local.y, out double u, out double dist);
                if (u < 0 || u > 1) continue;
                double d = Math.Abs(dist);
                if (d < bd) { bd = d; best = Strings[i]; bu = u; }
            }
            if (best == null || bd >= DesktopReach) return -1;
            var o = new StrokeInfo { U = (float)bu, Speed = DesktopSpeed, Mode = PluckMode.Through };
            return Pluck(best, DesktopVelocity, o, true, null) ? best.Index : -1;
        }

        /// <summary>OverStrings (local point): within HARP_PLANE of the string plane and within HARP_REACH,
        /// measured in the plane, of a string segment a-b.</summary>
        public bool OverStrings(Vector3 local)
        {
            if (Mathf.Abs(local.z) > PlayPlane) return false;
            const double r2 = PlayReach * (double)PlayReach;
            for (int i = 0; i < Strings.Length; i++)
            {
                HarpString s = Strings[i];
                double t = s.Len2 > 0 ? ((local.x - s.Ax) * s.Dx + (local.y - s.Ay) * s.Dy) / s.Len2 : 0;
                t = Math.Max(0, Math.Min(1, t));
                double dx = local.x - (s.Ax + s.Dx * t), dy = local.y - (s.Ay + s.Dy * t);
                if (dx * dx + dy * dy <= r2) return true;
            }
            return false;
        }
    }
}
