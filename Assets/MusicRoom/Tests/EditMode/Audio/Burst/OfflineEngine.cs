// The offline engine for EditMode tests and A/B renders (ENGINE-API.md section 6): the Burst graph with the sound units
// of the requested roles (the same SoundUnits factories AudioEngine uses), optionally the 26 SynthSources driven offline
// (today's voices + the graph slice + the bus, exactly the audio thread's code), a sample clock of its own, and WAV I/O
// plus the null metric for comparing against the Node renders in each test folder's Reference~/renders/.
//
//   using var e = new OfflineEngine(UnitId.Marimba);
//   var marimba = (ILoudnessReference)e.Unit(UnitId.Marimba);
//   float[][] y = e.RenderGraph(OfflineEngine.Samples(3.0), new[] { SourceId.MarimbaLo, SourceId.MarimbaMid, SourceId.MarimbaHi },
//       t => { if (t == 0) marimba.PlayReference(OfflineEngine.At(0.0)); });
//   WavFile.Write(path, new[] { OfflineEngine.Sum(y) }, OfflineEngine.Sr, 24);
using System;
using System.IO;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Audio.Dsp;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.Audio
{
    public sealed unsafe class OfflineEngine : IDisposable
    {
        public const int Sr = 48000, Block = 256;

        /// <summary>The sources' order inside a block (OfflineAudio's): the bass and treble piano before PianoMid (their body
        /// send joins its board in the same block), the drum pieces before the drum room (their sends land in the same block),
        /// then the platform's unit-only sources (12-new-instruments/platform.md 10.1).</summary>
        public static readonly SourceId[] Order =
        {
            SourceId.PianoLo, SourceId.PianoHi, SourceId.PianoMid, SourceId.Harp,
            SourceId.Kick, SourceId.Snare, SourceId.Hat, SourceId.Tom1, SourceId.Tom2, SourceId.Floor, SourceId.Crash, SourceId.Ride,
            SourceId.DrumRoom, SourceId.Fire, SourceId.Wind, SourceId.Snow, SourceId.Guide, SourceId.Ui,
            SourceId.Handpan, SourceId.HandDrumLo, SourceId.HandDrumHi, SourceId.MarimbaLo, SourceId.MarimbaMid, SourceId.MarimbaHi,
            SourceId.Bowls,
        };

        public readonly AudioGraphHost Host;
        public readonly SoundContext Context;
        /// <summary>The 25 offline SynthSources (null when built with sources: false).</summary>
        public readonly SynthSource[] Sources;
        readonly SoundUnit[] _units = new SoundUnit[(int)UnitId.Count];
        readonly GameObject _root;
        readonly float[] _block = new float[Block];

        /// <summary>The engine sample of the next block's first frame (starts at 0, steps by Block).</summary>
        public long Clock { get; private set; }

        /// <summary>roles: units to create through SoundUnits (the workstreams' factories). factory: instead of SoundUnits
        /// (tests of the engine itself). sources: build the 26 SynthSources (RenderSources), else graph only.</summary>
        public OfflineEngine(UnitId[] roles = null, Func<UnitId, SoundContext, SoundUnit> factory = null, bool sources = true,
            uint seed = 1, GraphConfig? config = null)
        {
            var cfg = config ?? GraphConfig.QuestDefault(Sr);
            cfg.SampleRate = Sr;
            Host = new AudioGraphHost(cfg) { AdaptiveBudget = false };
            if (sources)
            {
                NoiseTable.BuildSeeded(Sr, seed);
                PeriodicWaveTables.Build();
                MetalBank.Build(Sr);
                _root = new GameObject("OfflineEngine") { hideFlags = HideFlags.HideAndDontSave };
                Sources = new SynthSource[(int)SourceId.Count];
                for (int i = 0; i < Sources.Length; i++)
                {
                    var id = (SourceId)i;
                    var go = new GameObject("Synth " + id) { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetParent(_root.transform, false);
                    var s = go.AddComponent<SynthSource>();   // edit mode: no Awake; InitOffline allocates what Awake would
                    s.id = id;
                    s.spatial = id != SourceId.Ui;
                    AudioRig.Configure(s, id);
                    s.InitOffline(Sr);
                    s.Graph = Host;
                    Sources[i] = s;
                }
                AudioRig.Connect(Sources);
            }
            Context = new SoundContext(Host, Sources, Sr, seed, () => Clock / (double)Sr, true);
            foreach (var role in roles ?? Array.Empty<UnitId>())
                _units[(int)role] = factory != null ? factory(role, Context) : SoundUnits.Create(role, Context);
        }

        public OfflineEngine(params UnitId[] roles) : this(roles, null) { }

        /// <summary>The unit created for a role (null when its workstream has none yet).</summary>
        public SoundUnit Unit(UnitId role) => _units[(int)role];

        /// <summary>Add a unit made by the test (after construction).</summary>
        public T Add<T>(Func<SoundContext, T> make) where T : SoundUnit { var u = make(Context); _units[(int)u.Id] = u; return u; }

        public SynthSource S(SourceId id) => Sources?[(int)id];

        /// <summary>Samples in t seconds (round(t sr), the Node references' event rule).</summary>
        public static int Samples(double seconds) => (int)Math.Round(seconds * Sr);

        /// <summary>The When of an event at t seconds on this clock (the graph maps it to round(t sr); 0 = the next block).</summary>
        public static double At(double seconds) => seconds;

        /// <summary>Graph only: render n frames and return each listened source's graph output (units + banks + one-shots,
        /// before any SynthSource). beforeBlock(blockStart) runs before each block: post the events that block needs.</summary>
        public float[][] RenderGraph(int n, SourceId[] listen, Action<long> beforeBlock = null)
        {
            var outs = Alloc(listen.Length, n);
            for (int done = 0; done < n; done += Block)
            {
                beforeBlock?.Invoke(Clock);
                Host.RenderOffline(Clock, Block);
                int m = Math.Min(Block, n - done);
                for (int k = 0; k < listen.Length; k++)
                {
                    float* src = Host.SourceSlice((int)listen[k]);
                    for (int i = 0; i < m; i++) outs[k][done + i] = src[i];
                }
                Clock += Block;
            }
            return outs;
        }

        /// <summary>Full sources: every SynthSource (Order) renders its block (the first renders the graph), exactly as the mixer
        /// thread does; returns each listened source's mono output (after its bus gain, before the spatializer).</summary>
        public float[][] RenderSources(int n, SourceId[] listen, Action<long> beforeBlock = null)
        {
            if (Sources == null) throw new InvalidOperationException("built without sources");
            var outs = Alloc(listen.Length, n);
            for (int done = 0; done < n; done += Block)
            {
                beforeBlock?.Invoke(Clock);
                int m = Math.Min(Block, n - done);
                foreach (var id in Order)
                {
                    var s = Sources[(int)id];
                    s.RenderBlock(_block, 1, Clock);
                    int k = Array.IndexOf(listen, s.id);
                    if (k < 0) continue;
                    for (int i = 0; i < m; i++) outs[k][done + i] = _block[i];
                }
                Clock += Block;
            }
            return outs;
        }

        public static float[] Sum(float[][] channels)
        {
            var y = new float[channels.Length > 0 ? channels[0].Length : 0];
            foreach (var c in channels) for (int i = 0; i < y.Length; i++) y[i] += c[i];
            return y;
        }

        static float[][] Alloc(int k, int n) { var a = new float[k][]; for (int i = 0; i < k; i++) a[i] = new float[n]; return a; }

        public void Dispose()
        {
            if (Sources != null) foreach (var s in Sources) if (s != null) s.Graph = null;
            for (int i = 0; i < _units.Length; i++) { _units[i]?.Dispose(); _units[i] = null; }
            Host.Dispose();
            if (_root != null) Object.DestroyImmediate(_root);
        }
    }

    /// <summary>WAV files: write (PCM 16/24-bit or float32, any channel count) and read (PCM 16/24/32, float32), for A/B
    /// renders and the Node references (Reference~/renders: 24-bit stereo; Audio/Reference~: float mono).</summary>
    public static class WavFile
    {
        public static void Write(string path, float[][] channels, int sampleRate, int bits = 24)
        {
            int ch = channels.Length, n = ch > 0 ? channels[0].Length : 0, bps = bits / 8;
            bool fl = bits == 32;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using var w = new BinaryWriter(File.Create(path));
            int data = n * ch * bps;
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); w.Write(36 + data);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
            w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' }); w.Write(16);
            w.Write((short)(fl ? 3 : 1)); w.Write((short)ch); w.Write(sampleRate); w.Write(sampleRate * ch * bps); w.Write((short)(ch * bps)); w.Write((short)bits);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); w.Write(data);
            for (int i = 0; i < n; i++)
                for (int c = 0; c < ch; c++)
                {
                    float v = channels[c][i];
                    if (fl) { w.Write(v); continue; }
                    double x = Math.Max(-1.0, Math.Min(1.0, v));
                    if (bits == 16) { w.Write((short)Math.Round(x * 32767.0)); continue; }
                    int q = (int)Math.Round(x * 8388607.0);
                    w.Write((byte)(q & 0xff)); w.Write((byte)((q >> 8) & 0xff)); w.Write((byte)((q >> 16) & 0xff));
                }
        }

        public static float[][] Read(string path, out int sampleRate)
        {
            byte[] b = File.ReadAllBytes(path);
            int p = 12, format = 0, ch = 0, bits = 0;
            sampleRate = 0;
            while (p + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, p, 4);
                int size = BitConverter.ToInt32(b, p + 4);
                if (id == "fmt ")
                {
                    format = BitConverter.ToUInt16(b, p + 8); ch = BitConverter.ToUInt16(b, p + 10);
                    sampleRate = BitConverter.ToInt32(b, p + 12); bits = BitConverter.ToUInt16(b, p + 22);
                    if (format == 0xFFFE && size >= 40) format = BitConverter.ToUInt16(b, p + 32);   // WAVE_FORMAT_EXTENSIBLE
                }
                else if (id == "data")
                {
                    int bps = bits / 8, n = Math.Min(size, b.Length - p - 8) / (bps * ch);
                    var y = new float[ch][];
                    for (int c = 0; c < ch; c++) y[c] = new float[n];
                    int q = p + 8;
                    for (int i = 0; i < n; i++)
                        for (int c = 0; c < ch; c++, q += bps)
                            y[c][i] = format == 3 ? BitConverter.ToSingle(b, q)
                                : bits == 16 ? BitConverter.ToInt16(b, q) / 32768f
                                : bits == 24 ? ((b[q] | (b[q + 1] << 8) | ((sbyte)b[q + 2] << 16))) / 8388608f
                                : BitConverter.ToInt32(b, q) / 2147483648f;
                    return y;
                }
                p += 8 + size + (size & 1);
            }
            throw new InvalidDataException(path + ": no data chunk");
        }
    }

    /// <summary>The null metric: 10 log10(sum (y - ref)^2 / sum ref^2) over [from, to) of the reference (dB, lower is better).</summary>
    public static class NullTest
    {
        public static double ResidualDb(float[] y, float[] reference, int from = 0, int to = -1, double gain = 1.0)
        {
            if (to < 0) to = reference.Length;
            double e = 0, s = 0;
            for (int i = from; i < to; i++)
            {
                double r = reference[i], d = (i < y.Length ? y[i] * gain : 0) - r;
                e += d * d;
                s += r * r;
            }
            return 10 * Math.Log10(Math.Max(e, 1e-300) / Math.Max(s, 1e-300));
        }

        /// <summary>The least-squares gain g minimising |g y - ref| (a level-matched null, as the renders' loudness match).</summary>
        public static double BestGain(float[] y, float[] reference)
        {
            double yr = 0, yy = 0;
            for (int i = 0; i < Math.Min(y.Length, reference.Length); i++) { yr += (double)y[i] * reference[i]; yy += (double)y[i] * y[i]; }
            return yy > 0 ? yr / yy : 1.0;
        }

        /// <summary>The largest sample-to-sample step |y[i] - y[i-1]| in [from, to) (click checks).</summary>
        public static float MaxStep(float[] y, int from = 1, int to = -1)
        {
            if (to < 0) to = y.Length;
            float m = 0;
            for (int i = Math.Max(1, from); i < to; i++) m = Math.Max(m, Math.Abs(y[i] - y[i - 1]));
            return m;
        }

        public static float Peak(float[] y, int from = 0, int to = -1)
        {
            if (to < 0) to = y.Length;
            float m = 0;
            for (int i = from; i < to; i++) m = Math.Max(m, Math.Abs(y[i]));
            return m;
        }
    }
}
