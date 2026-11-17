// The book's adapter over the platform hooks (BUILD-GUIDE 12): the rest's pose from IPianoApi.MusicRestPose (null while
// the piano is not at the seat: put away, on display, gliding), MusicRestLocal and MusicRestLedge; the current lesson event
// from ITutorial.CurrentEvent; the paper sound through the engine's one-shot pool (four presets registered once, then the
// shots of SheetPaperSound.Turn on PianoMid, Classic sound or not); Sunny's tips through IGuide.Tip (TipSpec ids, Manual
// trigger); gesture signals as InstrumentSignalEvent { Kind = Piano, Signal = "sheet.*" }.
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using Unity.Mathematics;
using UnityEngine;

namespace MusicRoom.Sheet
{
    public sealed class PlatformHost : ISheetHost
    {
        readonly App _app;
        IPianoApi _piano;
        ITutorial _tut;
        IAudioEngine _audio;
        readonly int[] _presets = { -1, -1, -1, -1 };
        int _presetRate;
        readonly SheetPaperSound.Shot[] _shots = new SheetPaperSound.Shot[SheetPaperSound.MaxShotsPerTurn];

        public PlatformHost(App app) { _app = app; }

        IPianoApi Piano => _piano ?? (_piano = _app.Get<IPianoApi>());

        public Transform PianoRoot => Piano?.Root;
        public Pose MusicRestLocal => Piano != null ? Piano.MusicRestLocal : new Pose(BookGeometry.DefaultRestPosition, BookGeometry.DefaultRestRotation);
        public float MusicRestLedge => Piano != null && Piano.MusicRestLedge > 0f ? Piano.MusicRestLedge : BookGeometry.DefaultLedge;
        public Pose? MusicRestPose => Piano?.MusicRestPose;

        public int CurrentEvent
        {
            get
            {
                if (_tut == null) _tut = _app.Get<ITutorial>();
                return _tut != null ? _tut.CurrentEvent : -1;
            }
        }

        /// <summary>Leaves turned (one paper sound each) and one-shots started (tests).</summary>
        public int PaperTurns { get; private set; }
        public int ShotsPlayed { get; private set; }
        public float LastPaperSeconds { get; private set; }
        public double LastPaperWhen { get; private set; }

        public void PaperTurn(float seconds, float level, uint seed, double when)
        {
            PaperTurns++;
            LastPaperSeconds = seconds;
            LastPaperWhen = when;
            if (_audio == null) _audio = _app.Get<IAudioEngine>();
            if (_audio == null) return;
            int sr = _audio.SampleRate > 0 ? _audio.SampleRate : 48000;
            if (_presetRate != sr) { for (int p = 0; p < 4; p++) _presets[p] = -1; _presetRate = sr; }
            for (int p = 0; p < 4; p++) if (_presets[p] < 0) _presets[p] = _audio.AddOneShotPreset(Preset(SheetPaperSound.Presets[p], sr, 0x5ee7u + (uint)p * 7919u));
            if (_presets[0] < 0) return;   // no graph (a test fake, or the engine is not up)
            int n = SheetPaperSound.Turn(_shots, 0, when, seconds, level, seed);
            for (int i = 0; i < n; i++)
                if (_audio.PlayOneShot(_presets[_shots[i].Preset], _shots[i].Gain * SheetPaperSound.SourceGain, SourceId.PianoMid, 0, _shots[i].At)) ShotsPlayed++;
        }

        /// <summary>A SheetPaperSound preset as the graph's OneShotPreset: four noise lanes, the lab's biquads (the audio-EQ-cookbook
        /// formulas with a linear Q for every type, normalised by a0), envelope gain 1 decaying with tau, the lane mix.</summary>
        public static unsafe OneShotPreset Preset(in SheetPaperSound.PresetSpec spec, int sr, uint seed)
        {
            var p = new OneShotPreset { Length = (int)System.Math.Floor(spec.Seconds * sr + 0.5), LevelDb = spec.LevelDb, Seed = seed };
            float4 env = 0, mul = 0, mix = 0;
            for (int l = 0; l < 4 && l < spec.Lanes.Length; l++)
            {
                var ln = spec.Lanes[l];
                double w = 2 * System.Math.PI * ln.Hz / sr, cs = System.Math.Cos(w), al = System.Math.Sin(w) / (2 * ln.Q);
                double b0, b1, b2;
                if (ln.Type == SheetPaperSound.Lane.LowPass) { b0 = (1 - cs) / 2; b1 = 1 - cs; b2 = b0; }
                else if (ln.Type == SheetPaperSound.Lane.HighPass) { b0 = (1 + cs) / 2; b1 = -(1 + cs); b2 = b0; }
                else { b0 = al; b1 = 0; b2 = -al; }
                double a0 = 1 + al;
                DspSetup.SetBiquad(&p.Filter, l, b0 / a0, b1 / a0, b2 / a0, -2 * cs / a0, (1 - al) / a0);
                env[l] = 1f;
                mul[l] = DspSetup.DecayMultiplier(ln.Tau, sr);
                mix[l] = ln.Mix;
            }
            p.EnvGain = env; p.EnvMul = mul; p.Mix = mix;
            return p;
        }

        public float Tip(string id)
        {
            var g = _app.Get<IGuide>();
            return g != null ? g.Tip(id) : 0f;
        }

        public bool TipSeen(string id)
        {
            var g = _app.Get<IGuide>();
            return g != null && g.TipSeen(id);
        }

        public void Signal(string signal) => _app.Events.Publish(new InstrumentSignalEvent { Kind = InstrumentKind.Piano, Signal = signal });

        public double AudioNow => _app.AudioTime;
    }
}
