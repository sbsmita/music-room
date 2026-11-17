// The ControlStream encoder (platform.md 6.3; Tools/SoundLab/platform/control-decimator.mjs encode(), followed line for
// line in double precision, the same order): how a continuous instrument (a rubbed bowl)
// records into the take / loop event stream without flooding it. One encoder per lane, run once per frame by the
// instrument; it emits only while the lane sounds (On, the Controls, a final level-0 Control, then Off), so a resting hand
// posts nothing (Controls only while sounding, never a wake of a sleeping voice).
//   On   when the level rises above LevelOn                               -> <prefix>On  {lane, u: pitch, vel: level}
//   Ctl  while on: the first frame where |pitch - sent| >= PitchDb semitones or |dB(level) - dB(sent)| >= LevelDb, at least
//        MinGap after the last event; or Keyframe after the last event       -> <prefix>Ctl {lane, u, vel, speed}
//   Off  after OffHold below LevelOff (preceded by a final Ctl at level 0)  -> <prefix>Off {lane}
//   speed = seconds since the lane's previous event (the glide length), capped at Keyframe.
// The decoder (the unit) glides each Ctl linearly from wherever the value is to the event's value over `speed`; playback
// dispatches a Ctl min(speed, Preroll) early so the glide ends when the value was recorded (Lessons' playback does it from
// the registry's EventRole.Control). Allocation-free: Frame writes into the caller's buffer.
using System;

namespace MusicRoom.Core
{
    [Serializable]
    public struct ControlEncoderSettings
    {
        public double PitchDb, LevelDb, MinGap, Keyframe, LevelOn, LevelOff, OffHold, Preroll;

        /// <summary>The platform's numbers: 2 cents, 0.5 dB, at most 30 Hz, a keyframe every 0.25 s.</summary>
        public static ControlEncoderSettings Default => new ControlEncoderSettings
        {
            PitchDb = 0.02, LevelDb = 0.5, MinGap = 1.0 / 30.0, Keyframe = 0.25, LevelOn = 0.01, LevelOff = 0.008, OffHold = 0.06, Preroll = 0.05,
        };
    }

    public enum ControlEventKind : byte { On = 0, Ctl, Off }

    /// <summary>One encoded event (T: the encoder's frame time; the recorder stamps its own t when the event is added).</summary>
    public struct ControlEvent
    {
        public double T;
        public ControlEventKind Kind;
        public int Lane;
        public double U, Vel, Speed;
    }

    public sealed class ControlEncoder
    {
        public readonly string OnType, CtlType, OffType;
        public readonly int Lane;
        public ControlEncoderSettings Settings;

        bool _on;
        double _lastT = -1, _sentP, _sentA, _below;

        /// <summary>Types "&lt;prefix&gt;On", "&lt;prefix&gt;Ctl", "&lt;prefix&gt;Off" (a prefix such as "bowlRub").</summary>
        public ControlEncoder(string prefix, int lane = 0) : this(prefix + "On", prefix + "Ctl", prefix + "Off", lane, ControlEncoderSettings.Default) { }

        public ControlEncoder(string onType, string ctlType, string offType, int lane, ControlEncoderSettings settings)
        {
            OnType = onType; CtlType = ctlType; OffType = offType; Lane = lane; Settings = settings;
        }

        /// <summary>The lane sounds (between On and Off).</summary>
        public bool Sounding => _on;
        /// <summary>The last value sent (pitch, level).</summary>
        public double SentPitch => _sentP;
        public double SentLevel => _sentA;

        public void Reset() { _on = false; _lastT = -1; _sentP = _sentA = _below = 0; }

        static double Db(double a) => 20.0 * Math.Log10(Math.Max(a, 1e-6));

        /// <summary>One frame at time t (seconds) with the gesture's pitch (fractional MIDI) and level (0..1); frameDt: this
        /// frame's length (the reference uses 1/90). Writes 0, 1 or 2 events into <paramref name="output"/> (size it 2) and
        /// returns the count.</summary>
        public int Frame(double t, double pitch, double level, double frameDt, ControlEvent[] output)
        {
            int n = 0;
            ControlEncoderSettings c = Settings;
            if (!_on)
            {
                if (level > c.LevelOn)
                {
                    _on = true; _below = 0; _lastT = t; _sentP = pitch; _sentA = level;
                    output[n++] = new ControlEvent { T = t, Kind = ControlEventKind.On, Lane = Lane, U = pitch, Vel = level, Speed = 0 };
                }
                return n;
            }
            if (level < c.LevelOff)
            {
                _below += frameDt;
                if (_below >= c.OffHold - 1e-9)
                {
                    output[n++] = new ControlEvent { T = t, Kind = ControlEventKind.Ctl, Lane = Lane, U = pitch, Vel = 0, Speed = Math.Min(t - _lastT, c.Keyframe) };
                    output[n++] = new ControlEvent { T = t, Kind = ControlEventKind.Off, Lane = Lane, U = pitch, Vel = 0, Speed = 0 };
                    _on = false;
                    return n;
                }
            }
            else _below = 0;
            double since = t - _lastT;
            bool moved = Math.Abs(pitch - _sentP) >= c.PitchDb || Math.Abs(Db(level) - Db(_sentA)) >= c.LevelDb;
            if ((moved && since >= c.MinGap - 1e-9) || since >= c.Keyframe - 1e-9)
            {
                output[n++] = new ControlEvent { T = t, Kind = ControlEventKind.Ctl, Lane = Lane, U = pitch, Vel = level, Speed = Math.Min(since, c.Keyframe) };
                _lastT = t; _sentP = pitch; _sentA = level;
            }
            return n;
        }

        /// <summary>End the lane now (the looper's window end, a take's stop, the hand lost): a final level-0 Ctl and the Off,
        /// or nothing when the lane is silent. Returns the count written.</summary>
        public int Close(double t, ControlEvent[] output)
        {
            if (!_on) return 0;
            int n = 0;
            output[n++] = new ControlEvent { T = t, Kind = ControlEventKind.Ctl, Lane = Lane, U = _sentP, Vel = 0, Speed = Math.Min(Math.Max(0, t - _lastT), Settings.Keyframe) };
            output[n++] = new ControlEvent { T = t, Kind = ControlEventKind.Off, Lane = Lane, U = _sentP, Vel = 0, Speed = 0 };
            _on = false;
            return n;
        }

        /// <summary>The TakeEvent for an encoded event (u, vel, speed, lane; the recorder stamps t).</summary>
        public TakeEvent ToTakeEvent(in ControlEvent e) => new TakeEvent
        {
            type = e.Kind == ControlEventKind.On ? OnType : e.Kind == ControlEventKind.Ctl ? CtlType : OffType,
            lane = e.Lane, u = (float)e.U, vel = (float)e.Vel, speed = (float)e.Speed,
        };

        /// <summary>How early playback dispatches a Control (min(speed, Preroll)).</summary>
        public static double DispatchLead(double speed, double preroll = 0.05) => Math.Min(Math.Max(0, speed), preroll);
    }
}
