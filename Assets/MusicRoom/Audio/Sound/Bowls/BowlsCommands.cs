// Main-thread command builders for the bowls unit (bowls.md 2.10, the reference engine's post() payloads). Everything the
// reference computes in double on its way into the unit is computed here in double and rounded once, so the audio thread
// only sees float32 values (bit-exact against the Node vectors):
//   Strike   A = peak force of component 0 (0.6 I over T), B = peak of component 1 (0.4 I over 0.72 T),
//            N = K0, Id = K1 (pulse lengths in samples), C/D = cos/sin of the strike angle, E = height factor s
//   Contact  A = N (newtons), B = v_f (m/s, signed), C/D = cos/sin of the contact angle, E = Omega (rad/s), F = s,
//            G = cos of the target's per-sample rotation, Seed = bits of its sine, N = surface | lane << 8, Id = ramp
//   Release  N = lane;  Damp  N = level;  Pickup  C/D = cos/sin of the listener angle, N = snap;  Seed  Seed;  Silence
// GraphCommand.Target is the bowl. Strike carries CommandFlags.LateDrop (the only onset); nothing else is ever dropped.
using System;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    public static class BowlsCommands
    {
        /// <summary>A strike of a known kind and speed (the module's call): strikePulse() then Strike().</summary>
        public static GraphCommand Strike(int bowl, BowlStrikeKind kind, double V, double angle, double heightS, int sr = BowlsConst.SR)
        {
            BowlsConst.StrikePulse(kind, V, out double impulse, out double T);
            return StrikeRaw(bowl, impulse, T, Math.Cos(angle), Math.Sin(angle), heightS, sr);
        }

        /// <summary>A strike with an explicit impulse (N s) and contact time (s): the reference's OP.STRIKE payload.</summary>
        public static GraphCommand StrikeRaw(int bowl, double impulse, double T, double cs, double sn, double hs, int sr = BowlsConst.SR)
        {
            var c = new GraphCommand { Op = (GraphOp)BowlsDsp.OpStrike, Flags = CommandFlags.LateDrop, Target = bowl, C = (float)cs, D = (float)sn, E = (float)hs };
            for (int comp = 0; comp < 2; comp++)
            {
                double Tc = comp != 0 ? T * BowlsConst.PULSE_SHORT : T, imp = impulse * (comp != 0 ? 1 - BowlsConst.PULSE_SPLIT : BowlsConst.PULSE_SPLIT);
                int K = (int)Math.Max(2, BowlsConst.JsRound(Tc * sr));
                if (K > BowlsConst.PulseKMax) K = BowlsConst.PulseKMax;
                float amp = (float)(Math.PI * imp / (2.0 * K / sr));      // half-sine peak: its integral is the impulse
                if (comp == 0) { c.A = amp; c.N = K; } else { c.B = amp; c.Id = K; }
            }
            return c;
        }

        /// <summary>A contact frame (the finger as the stick): kind 0 outer, 1 lip, 2 inner; lane 0 live, 1..3 playback;
        /// ramp in samples (0 = the unit's default: 30 ms for a touch's first frame, 10.7 ms after).</summary>
        public static GraphCommand Contact(int bowl, int kind, double N, double vf, double angle, double omega, double heightS, int lane = 0, int ramp = 0, int sr = BowlsConst.SR)
            => ContactCs(bowl, kind, N, vf, Math.Cos(angle), Math.Sin(angle), omega, heightS, lane, ramp, sr);

        public static GraphCommand ContactCs(int bowl, int kind, double N, double vf, double cs, double sn, double omega, double heightS, int lane, int ramp, int sr = BowlsConst.SR)
        {
            float x = (float)(omega / sr);                                // the target phasor's rotation per sample
            double xd = x;
            float trc = (float)(1 - 0.5 * xd * xd), trs = (float)(xd - xd * xd * xd / 6);
            return new GraphCommand
            {
                Op = (GraphOp)BowlsDsp.OpContact, Target = bowl, A = (float)N, B = (float)vf, C = (float)cs, D = (float)sn, E = (float)omega,
                F = (float)heightS, G = trc, Seed = math.asuint(trs), N = (kind & 0xff) | ((lane & 0xff) << 8), Id = ramp,
            };
        }

        public static GraphCommand Release(int bowl, int lane = 0) => new GraphCommand { Op = (GraphOp)BowlsDsp.OpRelease, Target = bowl, N = lane };

        public static GraphCommand Damp(int bowl, int level) => new GraphCommand { Op = (GraphOp)BowlsDsp.OpDamp, Target = bowl, N = level };

        public static GraphCommand Pickup(int bowl, double angle, bool snap)
            => new GraphCommand { Op = (GraphOp)BowlsDsp.OpPickup, Target = bowl, C = (float)Math.Cos(angle), D = (float)Math.Sin(angle), N = snap ? 1 : 0 };

        public static GraphCommand Seed(int bowl, uint seed) => new GraphCommand { Op = (GraphOp)BowlsDsp.OpSeed, Target = bowl, Seed = seed };

        public static GraphCommand Silence(int bowl) => new GraphCommand { Op = (GraphOp)BowlsDsp.OpSilence, Target = bowl };
    }
}
