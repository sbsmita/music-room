// What the hand drums record and play back (handdrums.md 3.5, platform 6.1 / 6.2), as plain functions over Core's
// TakeEvent and the unit's IHandDrumSound.
//  - handDrum (Onset, Kind): kind "conga" | "tumba", zone the stroke ("open", "mute", "slap", "closed", "bass", "heel",
//    "toe", "tip" for a touch), pos the main patch's mean rho, vel u, i the hand (0 L, 1 R), u = the main patch's mean
//    theta / 2 pi + 0.5. Review item 9.7, the mute relabel: an open tone or a slap is held back up to RelabelHoldMs (the
//    stroke logic decides "muted" within 90 ms of the stroke, when a hand comes to rest on it) and then recorded with
//    IRecorder.Add(ev, atAudioTime) at the moment it sounded, as "mute" / "closed" when the rest came, with mode
//    "rested": the rest that muted it is recorded too (handDrumMute), so playback plays the stroke as it sounded (open /
//    slap) and the recorded rest mutes it, exactly as live. A "mute" / "closed" without mode "rested" (a lesson, a
//    hand-written take) plays the canonical muted stroke (its own rest 12 ms after, lifted 0.25 s later).
//  - handDrumMute (State, Kind): open false on a rest (zone = the rest's first part, pos and u its point), true on the lift.
//  - Playback: the canonical stroke of the recorded zone, turned to the recorded theta and moved to the recorded rho
//    (review item 9.8: loops sound like the take), at the recorded u; a rest replays the canonical rest of its part.
using System;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;

namespace MusicRoom.HandDrums
{
    public static class HandDrumsTake
    {
        public const string TypeStroke = "handDrum", TypeMute = "handDrumMute";
        /// <summary>TakeEvent.mode of a stroke recorded after its mute relabel: its rest is in the take as well.</summary>
        public const string ModeRested = "rested";
        /// <summary>How long an open tone or a slap waits for its relabel before it is recorded (the stroke logic's 90 ms mute
        /// window plus its 14 ms rest detection plus two frames).</summary>
        public const double RelabelHoldMs = 135.0;
        public static readonly string[] Drums = { "conga", "tumba" };
        public static readonly string[] Zones = { "open", "mute", "slap", "closed", "bass", "heel", "toe", "tip" };

        public static string Zone(HdKind k, HdLabel label = HdLabel.None)
        {
            if (label == HdLabel.Mute) return "mute";
            if (label == HdLabel.SlapClosed) return "closed";
            switch (k)
            {
                case HdKind.Open: return "open";
                case HdKind.Slap: return "slap";
                case HdKind.Bass: return "bass";
                case HdKind.Heel: return "heel";
                case HdKind.Toe: return "toe";
                default: return "tip";
            }
        }

        /// <summary>A stroke that may still be relabelled muted (an open tone, a slap): the module holds it RelabelHoldMs.</summary>
        public static bool MayRelabel(HdKind k) => k == HdKind.Open || k == HdKind.Slap;

        /// <summary>The take event of a stroke once its relabel came (mute / closed, mode rested).</summary>
        public static void Relabel(ref TakeEvent ev, HdLabel label)
        {
            if (label == HdLabel.None) return;
            ev.zone = label == HdLabel.Mute ? "mute" : "closed";
            ev.mode = ModeRested;
        }

        static int Main(in HdEvent e) { for (int i = 0; i < e.PatchCount; i++) if (e.Patch(i).UScale == 1) return i; return 0; }

        static double MeanTheta(in HdPatch p) { double s = 0; for (int k = 0; k < p.Count; k++) s += p.Theta(k); return p.Count > 0 ? s / p.Count : 0; }

        public static TakeEvent FromStroke(in HdEvent e)
        {
            HdPatch m = e.Patch(Main(e));
            return new TakeEvent
            {
                type = TypeStroke, kind = Drums[e.Drum], zone = Zone(e.Kind, e.Label), pos = (float)m.MeanRho, vel = (float)e.U, i = e.Hand,
                u = (float)(Wrap(MeanTheta(m)) / (2 * Math.PI) + 0.5),
            };
        }

        public static TakeEvent FromRest(in HdEvent e)
        {
            if (e.Op == HdOp.Lift) return new TakeEvent { type = TypeMute, kind = Drums[e.Drum], open = true, i = e.Hand };
            return new TakeEvent
            {
                type = TypeMute, kind = Drums[e.Drum], open = false, i = e.Hand, zone = PartName(e.RestPart0), pos = (float)e.RestRho0,
                u = (float)(Wrap(e.RestTheta0) / (2 * Math.PI) + 0.5),
            };
        }

        static double Wrap(double th) { th = Math.IEEERemainder(th, 2 * Math.PI); return th; }

        public static string PartName(HandPart p) => p == HandPart.Palm ? "palm" : p == HandPart.Heel ? "heel" : p == HandPart.Tips ? "tips" : p == HandPart.Pads ? "pads" : p == HandPart.Base ? "base" : "fingers";

        /// <summary>The canonical stroke of a recorded zone, its main patch's canonical mean rho and theta (handdrums.md 2.10).</summary>
        public static bool Canonical(string zone, out HandStroke stroke, out double rho, out double theta)
        {
            theta = 0;
            switch (zone)
            {
                case "open": stroke = HandStroke.Open; rho = 0.735; return true;
                case "mute": stroke = HandStroke.Mute; rho = 0.735; return true;
                case "slap": stroke = HandStroke.Slap; rho = 0.57; return true;
                case "closed": stroke = HandStroke.SlapClosed; rho = 0.57; return true;
                case "bass": stroke = HandStroke.Bass; rho = 0.10; return true;
                case "heel": stroke = HandStroke.Heel; rho = 0.30; return true;
                case "toe": stroke = HandStroke.Toe; rho = 0.51; theta = Math.PI; return true;
                case "tip": stroke = HandStroke.Touch; rho = 0.75; return true;
                default: stroke = HandStroke.Open; rho = 0; return false;
            }
        }

        static HandRest RestOf(string part) => part == "palm" ? HandRest.Palm : part == "heel" ? HandRest.Heel : part == "tips" || part == "base" ? HandRest.SlapClosed : part == "pads" ? HandRest.Toe : HandRest.Mute;
        static double RestRho(HandRest r) => r == HandRest.Palm ? 0.15 : r == HandRest.Heel ? 0.30 : r == HandRest.Toe ? 0.5 : r == HandRest.SlapClosed ? 0.56 : 0.675;

        /// <summary>IInstrumentPlayback.Play: a recorded event onto the unit (and nothing else: the visuals follow the unit's
        /// own calls in the module). False when the event is not the hand drums'.</summary>
        public static bool Play(in TakeEvent e, IHandDrumSound sound, double when = 0)
        {
            if (sound == null) return false;
            int drum = Array.IndexOf(Drums, e.kind);
            if (drum < 0) return false;
            int hand = e.i & 1;
            double theta = (e.u - 0.5) * 2 * Math.PI;
            if (e.type == TypeStroke)
            {
                // a relabelled stroke whose rest is in the take: it sounded open / slap, the recorded rest mutes it
                string zone = e.mode == ModeRested ? (e.zone == "mute" ? "open" : e.zone == "closed" ? "slap" : e.zone) : e.zone;
                if (!Canonical(zone, out HandStroke k, out double rho0, out double th0)) return false;
                float dRho = (float)Math.Max(-0.3, Math.Min(0.3, e.pos - rho0));
                sound.Stroke(drum, hand, k, Math.Max(0, Math.Min(1, e.vel)), theta - th0, when, dRho);
                return true;
            }
            if (e.type == TypeMute)
            {
                if (e.open) { sound.Lift(drum, hand, when); return true; }
                HandRest r = RestOf(e.zone);
                sound.RestCanonical(drum, hand, r, theta - (r == HandRest.Toe ? Math.PI : 0), when);
                return true;
            }
            return false;
        }

        /// <summary>IInstrumentPlayback.ReleaseAll: every hand lifted off both heads.</summary>
        public static void ReleaseAll(IHandDrumSound sound, double when = 0)
        {
            if (sound == null) return;
            for (int d = 0; d < 2; d++) for (int h = 0; h < 2; h++) sound.Lift(d, h, when);
        }
    }
}
