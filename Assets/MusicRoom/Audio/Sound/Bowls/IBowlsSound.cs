// The bowls unit's API (bowls.md 3.4) and its main-thread core. BowlsSoundCore turns the calls into the unit's commands
// (BowlsCommands, double maths) and posts them through a sink: in the app the SoundUnit's own ring (BowlsUnit, phase B),
// in the EditMode rig a sample-stamped queue. It also reads the audio thread's per-bowl stats (Volatile.Read, published
// at block end) and keeps the 0.5 s contact-lost window. Main thread only; nothing here allocates after construction.
using System.Threading;
using UnityEngine;

namespace MusicRoom.Audio.Burst
{
    public interface IBowlsSound
    {
        /// <summary>A strike: speed V (m/s) -> impulse and contact time on the main thread; angle in the bowl frame; heightS
        /// = 1 - 0.5 depth / H (1 at the lip).</summary>
        void Strike(int bowl, BowlStrikeKind kind, float speed, float angleRad, float heightS, double when = 0);
        /// <summary>One frame of a rub contact (the finger or the puja's shaft as the stick), on lane 0 (live) or 1..3.</summary>
        void Contact(int bowl, BowlSurface s, float newtons, float fingerSpeed, float angleRad, float omega, float heightS,
                     int lane = 0, int rampSamples = 0, double when = 0);
        void Release(int bowl, int lane = 0, double when = 0);
        /// <summary>Touch damping level 0 none, 1 one light finger (T60 7 s), 2 fingers (0.81 s), 3 palm (0.28 s).</summary>
        void Damp(int bowl, int level, double when = 0);
        /// <summary>The listener's azimuth in the bowl frame (slewed 0.005 rad per block unless snap).</summary>
        void SetListener(int bowl, float angleRad, bool snap = false);
        void Silence(int bowl);
        /// <summary>(2,0) radial velocity amplitude at the antinode, m/s (singing at 0.15 and above).</summary>
        float SingVelocity(int bowl);
        /// <summary>(3,0) radial velocity amplitude, m/s (a rub on the wrong pair shows it above SingVelocity).</summary>
        float Mode3Velocity(int bowl);
        /// <summary>Mean friction power into the bowl over the last block, W.</summary>
        float PumpPower(int bowl);
        /// <summary>Block RMS of the bowl before the sum.</summary>
        float Level(int bowl);
        /// <summary>Fraction of contact samples with F_c = 0 (the stick bouncing: chatter) over about the last 0.5 s.</summary>
        float ContactLost(int bowl);
        /// <summary>The lane holding the bowl's stick, -1 when off.</summary>
        int ContactLane(int bowl);
        /// <summary>(2,0) zA.re, zA.im, zB.re, zB.im: for the visuals (the rocking and the pattern).</summary>
        Vector4 Pattern20(int bowl);
        bool Awake(int bowl);
    }

    /// <summary>BowlsSoundCore.RubStats: one bowl's rub counters and values, read from the audio thread's stats.</summary>
    public struct BowlsRubStats
    {
        public long Contact, Stick, Lost;
        public float Force, Slip, Rms, Sing;
    }

    /// <summary>Where BowlsSoundCore's commands go (the unit's ring, or the test rig's queue).</summary>
    public interface IBowlsCommandSink
    {
        void Post(in GraphCommand c);
    }

    /// <summary>The main-thread side of the bowls unit, independent of how its commands reach the audio thread.</summary>
    public sealed unsafe class BowlsSoundCore : IBowlsSound
    {
        const int Hist = 64;
        readonly BowlsState* _s;
        readonly IBowlsCommandSink _sink;
        readonly int _sr;
        // contact-lost window: cumulative (contact, lost) sample counts per bowl, sampled once per Tick
        readonly double[] _histT = new double[Hist * 3];
        readonly long[] _histC = new long[Hist * 3], _histL = new long[Hist * 3];
        readonly int[] _histHead = new int[3], _histCount = new int[3];
        readonly float[] _lost = new float[3];

        public BowlsSoundCore(BowlsState* state, IBowlsCommandSink sink, int sampleRate = BowlsConst.SR)
        {
            _s = state; _sink = sink; _sr = sampleRate;
        }

        public BowlsState* State => _s;

        static bool Valid(int bowl) => (uint)bowl < BowlsConst.NBOWLS;

        public void Strike(int bowl, BowlStrikeKind kind, float speed, float angleRad, float heightS, double when = 0)
        {
            if (!Valid(bowl)) return;
            var c = BowlsCommands.Strike(bowl, kind, speed, angleRad, heightS, _sr);
            c.When = when;
            _sink.Post(c);
        }

        public void Contact(int bowl, BowlSurface s, float newtons, float fingerSpeed, float angleRad, float omega, float heightS,
                            int lane = 0, int rampSamples = 0, double when = 0)
        {
            if (!Valid(bowl)) return;
            var c = BowlsCommands.Contact(bowl, (int)s, newtons, fingerSpeed, angleRad, omega, heightS, lane, rampSamples, _sr);
            c.When = when;
            _sink.Post(c);
        }

        public void Release(int bowl, int lane = 0, double when = 0)
        {
            if (!Valid(bowl)) return;
            var c = BowlsCommands.Release(bowl, lane); c.When = when; _sink.Post(c);
        }

        public void Damp(int bowl, int level, double when = 0)
        {
            if (!Valid(bowl)) return;
            var c = BowlsCommands.Damp(bowl, level); c.When = when; _sink.Post(c);
        }

        public void SetListener(int bowl, float angleRad, bool snap = false)
        {
            if (!Valid(bowl)) return;
            _sink.Post(BowlsCommands.Pickup(bowl, angleRad, snap));
        }

        public void Silence(int bowl)
        {
            if (!Valid(bowl)) return;
            _sink.Post(BowlsCommands.Silence(bowl));
        }

        public void SeedAll(uint seed)
        {
            for (int b = 0; b < BowlsConst.NBOWLS; b++) _sink.Post(BowlsCommands.Seed(b, seed));
        }

        BowlState* B(int bowl) => BowlsState.Bowl(_s, bowl);

        public float SingVelocity(int bowl) => Valid(bowl) ? Volatile.Read(ref B(bowl)->StSingVel) : 0f;
        public float Mode3Velocity(int bowl) => Valid(bowl) ? Volatile.Read(ref B(bowl)->StP3) : 0f;
        public float PumpPower(int bowl) => Valid(bowl) ? Volatile.Read(ref B(bowl)->StPump) : 0f;
        public float Level(int bowl) => Valid(bowl) ? Volatile.Read(ref B(bowl)->StRms) : 0f;
        public int ContactLane(int bowl) => Valid(bowl) ? Volatile.Read(ref B(bowl)->StLane) : -1;
        public bool Awake(int bowl) => Valid(bowl) && Volatile.Read(ref B(bowl)->Awake) != 0;
        public float ContactLost(int bowl) => Valid(bowl) ? _lost[bowl] : 0f;

        /// <summary>The rub's raw counters and last values (the development rub log): cumulative contact, stick (pseudo-adherence)
        /// and lost samples, the stick's force on the bowl (N) and slip speed (m/s) of the last block, the block RMS and the
        /// damping level the unit runs.</summary>
        public BowlsRubStats RubStats(int bowl)
        {
            if (!Valid(bowl)) return default;
            BowlState* b = B(bowl);
            return new BowlsRubStats
            {
                Contact = Volatile.Read(ref b->StContactSamples), Stick = Volatile.Read(ref b->StStickSamples), Lost = Volatile.Read(ref b->StLostSamples),
                Force = Volatile.Read(ref b->StFc), Slip = Volatile.Read(ref b->StU), Rms = Volatile.Read(ref b->StRms), Sing = Volatile.Read(ref b->StSingVel),
            };
        }

        public Vector4 Pattern20(int bowl)
        {
            if (!Valid(bowl)) return Vector4.zero;
            BowlState* b = B(bowl);
            return new Vector4(Volatile.Read(ref b->StP2[0]), Volatile.Read(ref b->StP2[1]), Volatile.Read(ref b->StP2[2]), Volatile.Read(ref b->StP2[3]));
        }

        /// <summary>Main thread, once per frame: samples the cumulative contact counters for ContactLost's 0.5 s window.</summary>
        public void Tick(double now)
        {
            for (int b = 0; b < BowlsConst.NBOWLS; b++)
            {
                BowlState* s = B(b);
                long c = Volatile.Read(ref s->StContactSamples), l = Volatile.Read(ref s->StLostSamples);
                int h = (_histHead[b] + 1) % Hist;
                _histHead[b] = h; if (_histCount[b] < Hist) _histCount[b]++;
                _histT[b * Hist + h] = now; _histC[b * Hist + h] = c; _histL[b * Hist + h] = l;
                // the oldest sample within 0.5 s
                int k = h, oldest = h;
                for (int i = 1; i < _histCount[b]; i++)
                {
                    k = (k - 1 + Hist) % Hist;
                    if (now - _histT[b * Hist + k] > 0.5) break;
                    oldest = k;
                }
                long dc = c - _histC[b * Hist + oldest], dl = l - _histL[b * Hist + oldest];
                _lost[b] = dc > 0 ? (float)dl / dc : 0f;
            }
        }
    }
}
