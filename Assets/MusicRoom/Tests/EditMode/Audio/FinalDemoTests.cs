// The listening demos (integration step 6): one short piece per instrument rendered OFFLINE through the classic sound,
// the app's only sound for the piano, drums and harp since 2026-11-10, to Builds/Listen/final/<inst>-classic.wav
// (48 kHz, 24-bit stereo): OfflineAudio (today's synths and buffers on the SynthSources, the drum room included), then
// today's master chain (MasterBus: x0.9, the measured compressor, the 7.5 kHz shelf, the 0.98 ceiling). The piano is
// played through the real PianoAction (automatic sustain, phrase lift), the harp with the velocity curve its sound uses
// (HarpPlay), the drums with the strikes the Drums module sends (fingertips as stick tips). Offline stand-ins: the direct
// path is a plain equal-power pan by each source's direction from the seat facing the instrument (the headset uses Meta's
// HRTF), and there is no Meta shoebox reverb (a native plugin that only runs in the player).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Audio.Dsp;
using MusicRoom.Audio.Offline;
using MusicRoom.Core;
using MusicRoom.Harp;
using MusicRoom.Piano;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public unsafe class FinalDemoTests
    {
                const int Sr = OfflineEngine.Sr, B = OfflineEngine.Block;
        static string Out => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "Listen", "final");

        static readonly Vector3 Head = new Vector3(0f, 1.2f, 0f);
        // The seat's instruments (room.md's VR room: the spec seat RoomRenderTests uses): piano ahead, harp left, kit right.
        static readonly Vector3 PianoPos = new Vector3(0f, 0.77f, 0.50f), HarpPos = new Vector3(-0.82f, 0.8f, 0.128f),
            DrumsPos = new Vector3(0.599f, 0.775f, -0.315f);

        /// <summary>Azimuth (deg, + right) of each source as heard by a player facing the instrument it belongs to.</summary>
        static float Azimuth(SourceId id)
        {
            switch (id)
            {
                case SourceId.PianoLo: return -28f;
                case SourceId.PianoHi: return 28f;
                case SourceId.Kick: return 0f;
                case SourceId.Snare: return -12f;
                case SourceId.Hat: return -34f;
                case SourceId.Tom1: return -6f;
                case SourceId.Tom2: return 12f;
                case SourceId.Floor: return 34f;
                case SourceId.Crash: return -42f;
                case SourceId.Ride: return 42f;
                default: return 0f;
            }
        }

        static readonly SourceId[] PianoSources = { SourceId.PianoLo, SourceId.PianoMid, SourceId.PianoHi };
        static readonly SourceId[] HarpSources = { SourceId.Harp };
        static readonly SourceId[] DrumSources =
            { SourceId.Kick, SourceId.Snare, SourceId.Hat, SourceId.Tom1, SourceId.Tom2, SourceId.Floor, SourceId.Crash, SourceId.Ride };

        enum Inst { Piano, Drums, Harp }

        // ================= the pieces =================

        /// <summary>A timed action of a piece: it runs on the first frame at or after T (the app's frame quantisation).</summary>
        struct Op { public double T; public Action<double> Run; }

        interface IPlayer
        {
            VoiceHandle Piano(int midi, float vel, double when);
            void Release(VoiceHandle h, bool thud, double when);
            void Drum(DrumKind kind, float vel, DrumStrike strike, double when);
            void Choke(DrumKind kind, double when);
            VoiceHandle Pluck(int s, float vel, double when, PluckOptions o);
            void HarpRelease(VoiceHandle h, double when);
            void Damp(VoiceHandle h, int hands, double when);
        }

        /// <summary>PianoAction's sink: what PianoInstrument sends to IAudioEngine.</summary>
        sealed class PianoSinkTo : IPianoSink
        {
            readonly IPlayer _p;
            readonly Func<double> _when;
            public PianoSinkTo(IPlayer p, Func<double> when) { _p = p; _when = when; }
            public VoiceHandle NoteOn(int midi, float vel) => _p.Piano(midi, vel, _when());
            public void NoteOff(int midi, VoiceHandle voice, bool thud) => _p.Release(voice, thud, _when());
            public void Record(in TakeEvent ev) { }
        }

        /// <summary>Ode to Joy, the right hand over left-hand chords, then a closing C chord left to ring (about 12 s).</summary>
        static List<Op> PianoPiece(IPlayer player, Func<double> now, out double seconds)
        {
            var clock = new Clock();
            var action = new PianoAction(clock, new PianoSinkTo(player, now));
            var ops = new List<Op>();
            var pressers = new Dictionary<int, object>();
            object P(int midi) { if (!pressers.TryGetValue(midi, out var o)) pressers[midi] = o = new object(); return o; }
            void Note(double t, double len, int midi, float vel)
            {
                ops.Add(new Op { T = t, Run = _ => action.PressMidi(midi, P(midi), vel) });
                ops.Add(new Op { T = t + len, Run = _ => action.ReleaseMidi(midi, P(midi)) });
            }
            const double beat = 0.5;
            int[] tune = { 64, 64, 65, 67, 67, 65, 64, 62, 60, 60, 62, 64, 64, 62, 62 };
            double[] lens = { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1.5, 0.5, 2 };
            double t0 = 0.3, t = t0;
            for (int i = 0; i < tune.Length; i++)
            {
                float vel = (Math.Abs((t - t0) / beat % 4) < 1e-6 ? 0.72f : 0.62f);
                Note(t, lens[i] * beat * 0.92, tune[i], vel);
                t += lens[i] * beat;
            }
            int[][] chords = { new[] { 48, 52, 55 }, new[] { 43, 50, 55 }, new[] { 48, 52, 55 }, new[] { 43, 50, 55 } };
            for (int bar = 0; bar < 4; bar++)
                for (int half = 0; half < 2; half++)
                    foreach (int m in chords[bar]) Note(t0 + bar * 4 * beat + half * 2 * beat, 1.85 * beat, m, half == 0 ? 0.5f : 0.4f);
            // the closing chord: struck after a breath (a new phrase: the automatic pedal lifts first), held, then let go
            double end = t0 + 16 * beat + 0.75;
            foreach (int m in new[] { 36, 48, 55, 60, 64, 67 }) Note(end, 2.6, m, m < 50 ? 0.55f : 0.66f);
            seconds = end + 2.6 + 3.0;
            // the frames drive PianoAction's own clock (90 Hz), as the app does
            ops.Insert(0, new Op { T = 0, Run = _ => { } });
            return Frames(ops, clock, seconds);
        }

        /// <summary>Steps the instrument's Clock in 90 Hz frames and runs each op on its frame (time-ordered).</summary>
        static List<Op> Frames(List<Op> ops, Clock clock, double seconds)
        {
            ops.Sort((a, b) => a.T.CompareTo(b.T));
            var frames = new List<Op>();
            int k = 0;
            for (double ft = 0; ft < seconds; ft += 1.0 / 90.0)
            {
                double frameT = ft;
                int from = k;
                while (k < ops.Count && ops[k].T <= frameT + 1e-9) k++;
                int to = k;
                var list = ops;
                frames.Add(new Op { T = frameT, Run = when => { clock.Step(1f / 90f); for (int i = from; i < to; i++) list[i].Run(when); } });
            }
            return frames;
        }

        /// <summary>A rock groove (100 bpm): two bars, a fill round the toms, a crash on one, then the hand chokes it (~10 s).</summary>
        static List<Op> DrumPiece(IPlayer p, out double seconds)
        {
            var ops = new List<Op>();
            void Hit(double t, DrumKind k, float vel, float pos, DrumZone zone) =>
                ops.Add(new Op { T = t, Run = w => p.Drum(k, vel, new DrumStrike { Pos = pos, Zone = zone, Implement = DrumImplement.Tip }, w) });
            const double beat = 0.6, e8 = beat / 2, s16 = beat / 4;
            double t0 = 0.3;
            for (int bar = 0; bar < 2; bar++)
            {
                double b0 = t0 + bar * 4 * beat;
                for (int i = 0; i < 8; i++) Hit(b0 + i * e8, DrumKind.Hat, i % 2 == 0 ? 0.62f : 0.45f, 0.55f, DrumZone.Top);
                Hit(b0, DrumKind.Kick, 0.85f, 0.2f, DrumZone.Head);
                Hit(b0 + 2 * beat, DrumKind.Kick, 0.8f, 0.2f, DrumZone.Head);
                if (bar == 1) Hit(b0 + 2.5 * beat, DrumKind.Kick, 0.7f, 0.2f, DrumZone.Head);
                Hit(b0 + beat, DrumKind.Snare, 0.82f, 0.25f, DrumZone.Head);
                Hit(b0 + 3 * beat, DrumKind.Snare, 0.86f, 0.25f, DrumZone.Head);
                Hit(b0 + 3.75 * beat, DrumKind.Snare, 0.3f, 0.45f, DrumZone.Head);   // a ghost note
            }
            double f0 = t0 + 8 * beat;   // the fill
            DrumKind[] fill = { DrumKind.Snare, DrumKind.Snare, DrumKind.Snare, DrumKind.Snare, DrumKind.Tom1, DrumKind.Tom1, DrumKind.Tom2, DrumKind.Tom2,
                                DrumKind.Floor, DrumKind.Floor, DrumKind.Floor, DrumKind.Floor, DrumKind.Snare, DrumKind.Tom1, DrumKind.Tom2, DrumKind.Floor };
            for (int i = 0; i < fill.Length; i++) Hit(f0 + i * s16, fill[i], 0.62f + 0.02f * (i % 4), 0.3f, DrumZone.Head);
            Hit(f0, DrumKind.Kick, 0.8f, 0.2f, DrumZone.Head);
            Hit(f0 + 2 * beat, DrumKind.Kick, 0.8f, 0.2f, DrumZone.Head);
            double c0 = f0 + 4 * beat;   // the crash on one, the ride's bell, then the choke
            Hit(c0, DrumKind.Crash, 0.9f, 0.85f, DrumZone.Edge);
            Hit(c0, DrumKind.Kick, 0.9f, 0.2f, DrumZone.Head);
            Hit(c0 + 2 * beat, DrumKind.Ride, 0.6f, 0.1f, DrumZone.Bell);
            Hit(c0 + 2.5 * beat, DrumKind.Ride, 0.55f, 0.1f, DrumZone.Bell);
            Hit(c0 + 3 * beat, DrumKind.Snare, 0.9f, 0.25f, DrumZone.Rim);
            ops.Add(new Op { T = c0 + 2.2, Run = w => p.Choke(DrumKind.Crash, w) });
            seconds = c0 + 2.2 + 2.5;
            return ops;
        }

        /// <summary>Pokes up a C major arpeggio, a chord, a glissando down all 22 strings, ringing, then a palm stops it (~11 s).</summary>
        static List<Op> HarpPiece(IPlayer p, out double seconds)
        {
            var ops = new List<Op>();
            var voices = new VoiceHandle[22];
            var live = new bool[22];
            float Vel(double speed) => HarpPlay.VelocityOf(speed);
            void Pluck(double t, int s, double speed, PluckMode mode)
            {
                ops.Add(new Op
                {
                    T = t,
                    Run = w =>
                    {
                        // today's harp releases a ringing string before it is plucked again
                        if (live[s]) p.HarpRelease(voices[s], w);
                        var o = new PluckOptions { U = 0.36f, Speed = (float)speed, Mode = mode, Ud = mode == PluckMode.Across ? 1f : 0.25f, Uz = mode == PluckMode.Across ? 0.15f : 1f };
                        voices[s] = p.Pluck(s, Vel(speed), w, o);
                        live[s] = true;
                    },
                });
            }
            double t = 0.3;
            foreach (int s in new[] { 7, 9, 11, 14, 16, 18, 21 }) { Pluck(t, s, 0.75, PluckMode.Through); t += 0.38; }
            t += 0.5;
            foreach (int s in new[] { 3, 5, 7, 10 }) Pluck(t, s, 0.85, PluckMode.Through);   // an F chord, both hands
            t += 1.6;
            for (int s = 21; s >= 0; s--) { Pluck(t, s, 0.95, PluckMode.Across); t += 0.045; }   // the glissando down
            t += 0.4;
            foreach (int s in new[] { 0, 4, 7, 11 }) { Pluck(t, s, 0.7, PluckMode.Through); t += 0.25; }
            t += 2.6;
            ops.Add(new Op
            {
                T = t,
                Run = w =>
                {
                    int n = 0;
                    for (int s = 0; s < 22; s++) if (live[s]) n++;
                    for (int s = 0; s < 22; s++) if (live[s]) { p.Damp(voices[s], n, w); live[s] = false; }
                },
            });
            seconds = t + 1.5;
            return ops;
        }

        // ================= the engine =================

        sealed class ClassicPlayer : IPlayer
        {
            public OfflineAudio A;
            public VoiceHandle Piano(int midi, float vel, double when) => A.Piano.Play(midi, vel, when);
            public void Release(VoiceHandle h, bool thud, double when) => A.Piano.Release(h, thud, when);
            public void Drum(DrumKind kind, float vel, DrumStrike strike, double when) => A.Drums.Hit(kind, vel, strike, when);
            public void Choke(DrumKind kind, double when) => A.Drums.Choke(kind);
            public VoiceHandle Pluck(int s, float vel, double when, PluckOptions o) => A.Strings.Pluck(s, vel, when, o);
            public void HarpRelease(VoiceHandle h, double when) => A.Strings.Release(h, when);
            public void Damp(VoiceHandle h, int hands, double when) => A.Strings.Damp(h, hands, when);
        }

        /// <summary>Runs the ops due before each block's end (their when = the frame time, inside that block).</summary>
        sealed class Sequencer
        {
            readonly List<Op> _ops;
            int _next;
            public double Now;
            public Sequencer(List<Op> ops) { _ops = ops; _ops.Sort((a, b) => a.T.CompareTo(b.T)); }
            public void Before(long blockStart)
            {
                double end = (blockStart + B) / (double)Sr, start = blockStart / (double)Sr;
                while (_next < _ops.Count && _ops[_next].T < end)
                {
                    Now = Math.Max(_ops[_next].T, start);
                    _ops[_next++].Run(Now);
                }
            }
        }

        static void Pan(float[] mono, float azDeg, float[] inter, int m)
        {
            double p = (Mathf.Clamp(azDeg, -90f, 90f) / 90.0 + 1.0) * 0.25 * Math.PI;
            float gl = (float)Math.Cos(p), gr = (float)Math.Sin(p);
            for (int i = 0; i < m; i++) { inter[2 * i] += mono[i] * gl; inter[2 * i + 1] += mono[i] * gr; }
        }

        static Vector3 RightFacing(Vector3 target)
        {
            Vector3 f = target - Head; f.y = 0f; f.Normalize();
            return Vector3.Cross(Vector3.up, f);
        }

        /// <summary>The classic sound: today's synths and buffers on the sources, today's master chain.</summary>
        static float[][] RenderClassic(Inst inst)
        {
            using var a = new OfflineAudio(drumRoom: inst == Inst.Drums);
            var player = new ClassicPlayer { A = a };
            Sequencer seq = null;
            List<Op> ops;
            double seconds;
            if (inst == Inst.Piano) ops = PianoPiece(player, () => seq.Now, out seconds);
            else if (inst == Inst.Drums) ops = DrumPiece(player, out seconds);
            else ops = HarpPiece(player, out seconds);
            seq = new Sequencer(ops);
            // today's buffers, as the app bakes them at boot (seeds 1000 + midi, 2000 + midi)
            if (inst == Inst.Piano)
                for (int m = 36; m <= 72; m++) a.Bank.Publish(AudioEngine.PianoSlot0 + m - 21, PianoRenderer.Render(m, Sr, (uint)(1000 + m)));
            if (inst == Inst.Harp)
                for (int s = 0; s < 22; s++) { int midi = HarpRenderer.Strings[s]; a.Bank.Publish(AudioEngine.HarpSlot0 + s, HarpRenderer.RenderOld(midi, Sr, (uint)(2000 + midi))); }
            SourceId[] listen = inst == Inst.Piano ? PianoSources : inst == Inst.Drums ? Append(DrumSources, SourceId.DrumRoom) : HarpSources;
            int n = (int)Math.Ceiling(seconds * Sr / B) * B;
            var outL = new float[n]; var outR = new float[n];
            var inter = new float[2 * B];
            var comp = new DynamicsCompressor(Sr, -14, 18, 4, 0.003, 0.18, 0.0, 2);
            var shL = Biquad.Create(BiquadType.Highshelf, 7500, 0, 1.5, Sr);
            var shR = Biquad.Create(BiquadType.Highshelf, 7500, 0, 1.5, Sr);
            for (int s = 0; s < n; s += B)
            {
                float[][] y = a.RenderEach(B, listen, seq.Before);
                Array.Clear(inter, 0, inter.Length);
                for (int k = 0; k < listen.Length; k++) Pan(y[k], Azimuth(listen[k]), inter, B);
                for (int i = 0; i < inter.Length; i++) inter[i] *= 0.9f;
                comp.Process(inter, 2, B);
                for (int i = 0; i < B; i++)
                {
                    outL[s + i] = Mathf.Clamp(shL.Process(inter[2 * i]), -0.98f, 0.98f);
                    outR[s + i] = Mathf.Clamp(shR.Process(inter[2 * i + 1]), -0.98f, 0.98f);
                }
            }
            return new[] { outL, outR };
        }

        static SourceId[] Append(SourceId[] a, SourceId x) { var r = new SourceId[a.Length + 1]; a.CopyTo(r, 0); r[a.Length] = x; return r; }

        // ================= the test =================

        static (double peakDb, double rmsDb) Levels(float[][] y)
        {
            double peak = 0, ss = 0;
            long n = 0;
            foreach (var c in y)
                foreach (float v in c)
                {
                    Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), "finite");
                    peak = Math.Max(peak, Math.Abs(v)); ss += (double)v * v; n++;
                }
            return (20 * Math.Log10(Math.Max(peak, 1e-12)), 10 * Math.Log10(Math.Max(ss / Math.Max(1, n), 1e-24)));
        }

        [Test]
        public void EveryInstrumentRendersItsClassicDemo()
        {
            Directory.CreateDirectory(Out);
            var report = new StringBuilder();
            foreach (Inst inst in new[] { Inst.Piano, Inst.Drums, Inst.Harp })
            {
                string name = inst.ToString().ToLowerInvariant();
                var versions = new (string tag, Func<float[][]> render)[] { ("classic", () => RenderClassic(inst)) };
                foreach (var (tag, render) in versions)
                {
                    var t0 = System.Diagnostics.Stopwatch.StartNew();
                    float[][] y = render();
                    var (peak, rms) = Levels(y);
                    string file = $"{name}-{tag}.wav";
                    WavFile.Write(Path.Combine(Out, file), y, Sr, 24);
                    string line = $"{file,-22} {y[0].Length / (double)Sr,5:F1} s   peak {peak,6:F1} dBFS   rms {rms,6:F1} dBFS   (rendered in {t0.Elapsed.TotalSeconds:F1} s)";
                    report.AppendLine(line);
                    Debug.Log("[FinalDemo] " + line);
                    Assert.That(peak, Is.LessThan(0.0), $"{file}: no clipping");
                    Assert.That(rms, Is.GreaterThan(-45.0), $"{file}: audible");
                }
            }
            File.WriteAllText(Path.Combine(Out, "levels.txt"), report.ToString());
        }
    }
}
