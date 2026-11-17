// How a bowl rub records into takes and loops, and plays back (bowls.md 3.7): through Core's ControlEncoder (platform.md
// 6.3, the ControlStream rules: On when the lane starts, a Ctl on change at most 30 Hz or a keyframe every 0.25 s, a final
// level-0 Ctl and the Off when it ends; nothing while the bowl is not rubbed). The encoder's two channels carry the rub:
//   level = N / 8 (the contact force; LevelDb 0.5 dB, so about 6 % of the force), the platform's "level": a level-0
//           Control lifts the finger (the looper's window end, a take's stop);
//   pitch = the rub speed as 0.04 per dB of |v| above 0.03 m/s, signed with the direction (PitchDb 0.02 = 0.5 dB, the
//           lab's rub deadband).
// The bowl's own fields ride on each event: i bowl, zone "outer" | "lip" | "inner" (an Off carries its zone too: the
// looper's own closes carry none and close every bowl of their lane), u the finger speed along the rim (m/s, signed),
// vel the level, pos the height on the wall (0 lip .. 2 base = 2 (1 - s)), midi the contact angle in milliradians
// (0..6283), speed the glide (s), lane the live lane key = the bowl's index (two bowls rubbed at once are two lanes to
// the looper; playback replaces it with the layer's or the take's lane). Playback (Decode) dispatches a Ctl
// min(speed, 0.05) s early (the recorder and the looper do it), glides over speed (ramp = speed x 48000 samples) and aims
// the angle at where the finger will be when the glide ends (angle - omega x lead).
using System;
using System.Collections.Generic;
using MusicRoom.Core;

namespace MusicRoom.Bowls
{
    /// <summary>One contact frame as the module sent it to the unit.</summary>
    public struct BowlsRubFrame
    {
        public double t; public int kind; public double N, vf, angle, hs;
    }

    /// <summary>A decoded playback command (the unit's Contact or Release).</summary>
    public struct BowlsPlaybackCmd
    {
        public double at; public bool release, close;
        public int bowl, kind, lane, ramp; public double N, vf, angle, omega, heightS;
    }

    public sealed class BowlsRubEncoder
    {
        public const string On = "bowlRubOn", Ctl = "bowlRubCtl", Off = "bowlRubOff";
        public static readonly string[] Zone = { "outer", "lip", "inner" };
        /// <summary>level = N / NFull; the speed code's floor (m/s) and scale (per dB); the unit's sample rate for ramps.</summary>
        public const double NFull = 8.0, SpeedFloor = 0.03, CodePerDb = 0.04, Rate = 48000, OmegaDead = 0.03;
        const double TAU = 2 * Math.PI;

        /// <summary>The platform's settings with the bowl's meaning: 0.5 dB of rub speed, 0.5 dB of force.</summary>
        public static ControlEncoderSettings Settings
        {
            get { var s = ControlEncoderSettings.Default; s.PitchDb = 0.5 * CodePerDb; s.LevelDb = 0.5; return s; }
        }

        public readonly int Bowl, Lane;
        public readonly double R;
        readonly ControlEncoder _enc;
        readonly ControlEvent[] _buf = new ControlEvent[2];
        BowlsRubFrame _last;

        public BowlsRubEncoder(int bowl, double R, int lane = -1)
        {
            Bowl = bowl; this.R = R; Lane = lane < 0 ? bowl : lane;
            _enc = new ControlEncoder(On, Ctl, Off, Lane, Settings);
        }

        public bool IsOn => _enc.Sounding;

        static double Wrap(double a) { double m = a % TAU; return (m + TAU) % TAU; }

        /// <summary>The rub speed as the encoder's pitch: 0.04 per dB above 0.03 m/s, signed (0 when still).</summary>
        public static double SpeedCode(double vf)
        {
            if (vf == 0) return 0;
            double db = 20 * Math.Log10(Math.Max(SpeedFloor, Math.Abs(vf)) / SpeedFloor);
            return Math.Sign(vf) * (1 + db) * CodePerDb;
        }

        /// <summary>One display frame of the live contact while the module sends Contact (frameDt: this frame's length).</summary>
        public int Frame(in BowlsRubFrame f, double frameDt, List<TakeEvent> output)
        {
            _last = f;
            int n = _enc.Frame(f.t, SpeedCode(f.vf), Math.Min(1.0, Math.Max(0.0, f.N / NFull)), frameDt, _buf);
            for (int k = 0; k < n; k++) output.Add(Make(_buf[k], f));
            return n;
        }

        /// <summary>The module's Release (the finger lifted): the final level-0 Ctl and the Off, now.</summary>
        public int Release(List<TakeEvent> output) => Close(_last.t, output);

        /// <summary>End the lane at t (the hand lost, the bowls leaving the seat): a final level-0 Ctl and the Off.</summary>
        public int Close(double t, List<TakeEvent> output)
        {
            int n = _enc.Close(t, _buf);
            for (int k = 0; k < n; k++) output.Add(Make(_buf[k], _last));
            return n;
        }

        TakeEvent Make(in ControlEvent c, in BowlsRubFrame f) => new TakeEvent
        {
            t = (float)c.T, type = c.Kind == ControlEventKind.On ? On : c.Kind == ControlEventKind.Ctl ? Ctl : Off, lane = c.Lane,
            i = Bowl, zone = Zone[Math.Max(0, Math.Min(2, f.kind))], u = (float)f.vf, vel = (float)c.Vel,
            pos = (float)(2 * (1 - f.hs)), midi = (int)Math.Floor(Wrap(f.angle) * 1000 + 0.5), speed = (float)c.Speed,
        };

        /// <summary>One rub event as the unit command it plays (the recorder's and looper's dispatch time `at` = e.t is
        /// already min(speed, 0.05) early for a Ctl). close: the looper's own close (a zone-less level-0 Ctl or Off): every
        /// bowl of the lane.
        /// False for another type.</summary>
        public static bool Decode(in TakeEvent e, double R, out BowlsPlaybackCmd cmd)
        {
            cmd = default;
            bool on = e.type == On, ctl = e.type == Ctl, off = e.type == Off;
            if (!on && !ctl && !off) return false;
            cmd.at = e.t; cmd.lane = e.lane; cmd.bowl = e.i;
            // the looper's own closes (a window end, a mute) carry no zone: a level-0 Ctl and the Off
            cmd.close = string.IsNullOrEmpty(e.zone) && (off || (ctl && e.vel <= 0f));
            if (off) { cmd.release = true; return true; }
            int kind = Math.Max(0, Array.IndexOf(Zone, e.zone));
            double vf = e.u, omega = Math.Abs(vf) < OmegaDead ? 0 : vf / R, angle = e.midi / 1000.0, lead = ctl ? ControlEncoder.DispatchLead(e.speed) : 0;
            cmd.kind = kind; cmd.N = e.vel * NFull; cmd.vf = vf; cmd.omega = omega; cmd.heightS = 1 - e.pos / 2;
            cmd.angle = angle - omega * lead;
            cmd.ramp = ctl ? (int)Math.Max(64, Math.Floor(e.speed * Rate + 0.5)) : 0;
            return true;
        }

        /// <summary>A list of recorded rub events as the lane's commands, at their dispatch times (a Ctl min(speed, 0.05) s
        /// early), sorted by time (stable): what the looper and the take player send to Play.</summary>
        public static List<BowlsPlaybackCmd> Decode(IReadOnlyList<TakeEvent> events, double R, int lane = 1, List<BowlsPlaybackCmd> into = null)
        {
            var cmds = into ?? new List<BowlsPlaybackCmd>();
            cmds.Clear();
            foreach (var e0 in events)
            {
                TakeEvent e = e0;
                if (e.type == Ctl) e.t -= (float)ControlEncoder.DispatchLead(e.speed);
                e.lane = lane;
                if (Decode(e, R, out BowlsPlaybackCmd c)) cmds.Add(c);
            }
            var idx = new int[cmds.Count]; for (int k = 0; k < idx.Length; k++) idx[k] = k;
            var copy = cmds.ToArray();
            Array.Sort(idx, (a, b) => copy[a].at != copy[b].at ? copy[a].at.CompareTo(copy[b].at) : a.CompareTo(b));
            for (int k = 0; k < idx.Length; k++) cmds[k] = copy[idx[k]];
            return cmds;
        }
    }
}
