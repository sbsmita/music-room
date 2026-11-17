// Offline harness for the Audio null tests: the engine's real sources (SynthSource.InitOffline + RenderBlock, configured
// and connected by AudioRig), voices, processors and synth classes, driven block by block on the test thread with a sample
// clock that starts at 0. What the tests compare with the 06 reference renders is therefore exactly the code the audio
// thread runs; only the clock and the seeds differ from a session (noise seed 1, board 2, boxes 3, drum room 6,
// ambience 11, jitter forced to 0 like the reference harness's Math.random = () => 0.5).
using System;
using System.IO;
using MusicRoom.Audio;
using MusicRoom.Audio.Dsp;
using MusicRoom.Audio.Synths;
using MusicRoom.Audio.Voices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.Audio
{
    public sealed class OfflineAudio : IDisposable
    {
        public const int Sr = 48000, Block = 256;

        /// <summary>Render order inside a block: the bass/treble piano before PianoMid (their body send joins its board in the
        /// same block) and the drum pieces before the drum room (their sends land in the same block).</summary>
        static readonly SourceId[] Order =
        {
            SourceId.PianoLo, SourceId.PianoHi, SourceId.PianoMid, SourceId.Harp,
            SourceId.Kick, SourceId.Snare, SourceId.Hat, SourceId.Tom1, SourceId.Tom2, SourceId.Floor, SourceId.Crash, SourceId.Ride,
            SourceId.DrumRoom, SourceId.Fire, SourceId.Wind, SourceId.Snow, SourceId.Guide, SourceId.Ui,
            SourceId.Handpan, SourceId.HandDrumLo, SourceId.HandDrumHi, SourceId.MarimbaLo, SourceId.MarimbaMid, SourceId.MarimbaHi,
            SourceId.Bowls,
        };

        public readonly SynthSource[] Sources = new SynthSource[(int)SourceId.Count];
        public readonly SampleBank Bank = new SampleBank(AudioEngine.SlotCount);
        public readonly SynthKit Kit;
        public readonly PianoSynth Piano;
        public readonly DrumSynth Drums;
        public readonly StringSynth Strings;
        public readonly UiSounds Ui;
        public readonly Ambience Ambience;
        public readonly GuideVoice Guide;
        public readonly DrumRoomReverb Room;
        public long Clock { get; private set; }

        readonly GameObject _host;
        readonly float[] _block = new float[Block];

        public OfflineAudio(bool ambience = false, bool guide = false, bool drumRoom = false, uint noiseSeed = 1,
            uint boardSeed = 2, uint boxSeed = 3, uint ambienceSeed = 11, uint roomSeed = 6)
        {
            NoiseTable.BuildSeeded(Sr, noiseSeed);
            PeriodicWaveTables.Build();
            MetalBank.Build(Sr);
            _host = new GameObject("OfflineAudio") { hideFlags = HideFlags.HideAndDontSave };
            for (int i = 0; i < (int)SourceId.Count; i++)
            {
                var id = (SourceId)i;
                var go = new GameObject("Synth " + id) { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(_host.transform, false);
                var s = go.AddComponent<SynthSource>();   // edit mode: no Awake; InitOffline allocates what Awake would
                s.id = id;
                s.spatial = id != SourceId.Ui;
                AudioRig.Configure(s, id);
                if (id == SourceId.PianoMid) s.resonatorSeed = boardSeed;
                if (id == SourceId.Harp) s.resonatorSeed = boxSeed;
                s.InitOffline(Sr);
                s.Bank = Bank;
                Sources[i] = s;
            }
            AudioRig.Connect(Sources);
            Kit = new SynthKit(Sources, 1) { RandOverride = 0.5f };
            Piano = new PianoSynth(Kit);
            Drums = new DrumSynth(Kit);
            Strings = new StringSynth(Kit);
            Ui = new UiSounds(Kit);
            if (ambience) Ambience = new Ambience(Kit, Sr, ambienceSeed);
            if (guide)
            {
                Guide = new GuideVoice(Kit, Sr);
                Guide.Attach(Bank, AudioEngine.VoiceSlot0);
            }
            if (drumRoom)
            {
                Room = new DrumRoomReverb(Sr, roomSeed);
                S(SourceId.DrumRoom).AddProcessor(Room);
            }
        }

        public SynthSource S(SourceId id) => Sources[(int)id];

        /// <summary>Render n frames from the current clock; returns the sum of the listened sources. beforeBlock(blockStart)
        /// runs before each block (stage commands that must land in that block).</summary>
        public float[] Render(int n, SourceId[] listen, Action<long> beforeBlock = null)
        {
            var sum = new float[n];
            for (int done = 0; done < n; done += Block)
            {
                beforeBlock?.Invoke(Clock);
                foreach (var id in Order)
                {
                    S(id).RenderBlock(_block, 1, Clock);
                    if (Array.IndexOf(listen, id) < 0) continue;
                    int m = Math.Min(Block, n - done);
                    for (int i = 0; i < m; i++) sum[done + i] += _block[i];
                }
                Clock += Block;
            }
            return sum;
        }

        public float[] Render(int n, params SourceId[] listen) => Render(n, listen, null);

        /// <summary>As Render, but each listened source's own output (for a stereo mix of the sources).</summary>
        public float[][] RenderEach(int n, SourceId[] listen, Action<long> beforeBlock = null)
        {
            var outs = new float[listen.Length][];
            for (int k = 0; k < listen.Length; k++) outs[k] = new float[n];
            for (int done = 0; done < n; done += Block)
            {
                beforeBlock?.Invoke(Clock);
                int m = Math.Min(Block, n - done);
                foreach (var id in Order)
                {
                    S(id).RenderBlock(_block, 1, Clock);
                    int k = Array.IndexOf(listen, id);
                    if (k < 0) continue;
                    for (int i = 0; i < m; i++) outs[k][done + i] = _block[i];
                }
                Clock += Block;
            }
            return outs;
        }

        public void Dispose()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }
    }

    /// <summary>The 06 reference renders (mono float32 WAV, 48 kHz) and the null metric.</summary>
    public static class Wav
    {
        /// <summary>Copies of Docs/06-audio-ref (the "~" folder is never imported by Unity).</summary>
        public static string Dir => Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Audio/Reference~");

        public static float[] Read(string name)
        {
            string path = Path.Combine(Dir, name);
            byte[] b = File.ReadAllBytes(path);
            int p = 12;
            int format = 0, channels = 0, rate = 0, bits = 0;
            while (p + 8 <= b.Length)
            {
                string chunk = System.Text.Encoding.ASCII.GetString(b, p, 4);
                int size = BitConverter.ToInt32(b, p + 4);
                if (chunk == "fmt ")
                {
                    format = BitConverter.ToUInt16(b, p + 8);
                    channels = BitConverter.ToUInt16(b, p + 10);
                    rate = BitConverter.ToInt32(b, p + 12);
                    bits = BitConverter.ToUInt16(b, p + 22);
                }
                else if (chunk == "data")
                {
                    if (format != 3 || channels != 1 || rate != 48000 || bits != 32)
                        throw new InvalidDataException($"{name}: expected mono float32 48 kHz, got fmt {format} ch {channels} {rate} Hz {bits} bit");
                    var d = new float[size / 4];
                    Buffer.BlockCopy(b, p + 8, d, 0, d.Length * 4);
                    return d;
                }
                p += 8 + size + (size & 1);
            }
            throw new InvalidDataException(name + ": no data chunk");
        }

        /// <summary>10 log10(sum (y - ref)^2 / sum ref^2) over the reference's length (the null residual, dB).</summary>
        public static double ResidualDb(float[] y, float[] reference)
        {
            double e = 0, s = 0;
            for (int i = 0; i < reference.Length; i++)
            {
                double r = reference[i], d = (i < y.Length ? y[i] : 0) - r;
                e += d * d;
                s += r * r;
            }
            return 10 * Math.Log10(Math.Max(e, 1e-300) / s);
        }
    }
}
