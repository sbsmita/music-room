// Scripted doubles for Sunny's two seams: a voice whose audio clock is the test's clock (lines "render" at a chosen
// time, levels follow a chosen envelope) and a world whose menu, layout, drums and head the test sets directly.
using System;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Guide;
using UnityEngine;

namespace MusicRoom.Tests.Guide
{
    public sealed class FakeVoice : ISunnyVoice
    {
        public bool Up = true;
        public double Clock;
        public readonly HashSet<string> Missing = new HashSet<string>();
        /// <summary>Audio time at which a line's processed buffer becomes ready (absent = ready at once).</summary>
        public readonly Dictionary<string, double> ReadyAt = new Dictionary<string, double>();
        public readonly List<string> Plays = new List<string>();
        public readonly List<double> PlayWhen = new List<double>();
        public readonly List<double> PlayEnd = new List<double>();
        public readonly List<ChirpKind> Chirps = new List<ChirpKind>();
        public int Stops;
        /// <summary>Mouth level as a function of seconds since the line started (null = a steady 0.1).</summary>
        public Func<double, float> LevelOf;
        string _playing;
        double _start, _end;

        public bool Available => Up;
        public double Now => Clock;
        public bool HasLine(string id) => GuideLines.IsLine(id) && !Missing.Contains(id);
        public bool LineReady(string id) => HasLine(id) && (!ReadyAt.TryGetValue(id, out double at) || Clock >= at);

        public float LineSeconds(string id)
        {
            int i = GuideLines.IndexOf(id);
            return i >= 0 && !Missing.Contains(id) ? GuideLines.ManifestSeconds[i] : -1f;
        }

        public double Play(string id, double when)
        {
            Plays.Add(id);
            PlayWhen.Add(when);
            _playing = id;
            _start = when;
            _end = when + LineSeconds(id);
            PlayEnd.Add(_end);
            return _end;
        }

        public void Stop()
        {
            Stops++;
            _playing = null;
        }

        public void Chirp(ChirpKind kind) => Chirps.Add(kind);

        public float Level
        {
            get
            {
                if (_playing == null || Clock < _start || Clock >= _end) return 0f;
                return LevelOf != null ? LevelOf(Clock - _start) : 0.1f;
            }
        }

        public int ChirpCount(ChirpKind kind)
        {
            int n = 0;
            foreach (ChirpKind k in Chirps) if (k == kind) n++;
            return n;
        }
    }

    public sealed class FakeWorld : ISunnyWorld
    {
        public bool Desktop = true;
        public bool HeadTracked = true;
        public Vector3 Head = new Vector3(0f, 1.2f, -0.1f);
        public Vector3 Forward = (new Vector3(0f, 0.95f, 0.45f) - new Vector3(0f, 1.2f, -0.1f)).normalized;
        public bool Panel = true, Shown;
        public MenuScreen Screen = MenuScreen.Home;
        public bool Single;
        public InstrumentKind Current = InstrumentKind.Piano;
        public bool PadFar = true;

        public bool IsDesktop => Desktop;

        public bool TryHead(out Vector3 position, out Vector3 forward)
        {
            position = Head;
            forward = Forward;
            return HeadTracked;
        }

        public bool HasPanel => Panel;
        public bool PanelShown => Panel && Shown;
        public MenuScreen PanelScreen => Screen;

        public bool TrySingleLayout(out InstrumentKind current)
        {
            current = Current;
            return Single;
        }

        public bool HandsFarFromPad() => PadFar;
    }

    /// <summary>Sunny on a scripted clock: Step(dt) advances the voice's audio clock and Sunny together.</summary>
    public sealed class SunnyRig
    {
        public const float Frame = 1f / 72f;
        public readonly FakeVoice Voice = new FakeVoice();
        public readonly FakeWorld World = new FakeWorld();
        public readonly Sunny Sunny;
        public float Time;

        public SunnyRig(uint seed = 7) { Sunny = new Sunny(World, Voice, seed); }

        public void Step(float dt = Frame)
        {
            Time += dt;
            Voice.Clock += dt;
            Sunny.Update(dt);
        }

        /// <summary>Runs whole frames for about `seconds`.</summary>
        public void Run(float seconds, float dt = Frame)
        {
            int n = Mathf.CeilToInt(seconds / dt - 1e-4f);
            for (int i = 0; i < n; i++) Step(dt);
        }

        /// <summary>Steps until `done` holds (at most `max` seconds); returns whether it did.</summary>
        public bool Until(Func<bool> done, float max = 30f, float dt = Frame)
        {
            int n = Mathf.CeilToInt(max / dt);
            for (int i = 0; i < n && !done(); i++) Step(dt);
            return done();
        }
    }
}
