// The snow weather, 1:1 with spec 07 section 5.3: the one clock every snowfall shares. Level follows the SNOW
// switch over a few seconds; settled snow (cover) builds over ~40 s and lingers for minutes; the wind is a slow breeze
// plus gusts every few seconds while it really snows, each announced (OnGust) the moment it starts so the gust heard
// (IAudioEngine.SnowGust) and the gust seen (_SnowWindVel) are the same one. Plain C#: the Room module ticks it in the
// Decor phase with Clock.Dt (<= 0.05) and pushes the shader globals; the MR portals read the same instance.
// The state is kept in double like the reference's JS numbers: a float clock and wind integral would drift from the
// reference gust timing within a minute and lose precision over a long session. The contract exposes floats.
using System;
using MusicRoom.Core;

namespace MusicRoom.Room.Decor
{
    public sealed class SnowWeather : ISnowWeather
    {
        public const double BaseWind = 0.32;        // m/s along the window between gusts
        public const double GustWind = 2.3;         // m/s added at the height of a full-strength gust
        public const float GustFront = 7.0f;        // m/s: how fast a gust sweeps across the field (shader constant)
        public const double CoverUp = 1.0 / 40;     // per second while snowing: full cover after ~40 s
        public const double CoverDown = 1.0 / 150;  // per second once it stops: the snow lingers for minutes
        public const double LevelRate = 0.8, LevelSnap = 0.003, FirstGust = 3, GustMinLevel = 0.15;

        struct Gust { public double At, Strength, Attack, Hold, Release; public double Duration => Attack + Hold + Release; }

        Mulberry32 _rng;
        Gust _gust;
        bool _hasGust;
        double _time, _target, _level, _cover, _wind, _windVel = BaseWind, _gustValue, _nextGust = FirstGust;

        public SnowWeather(uint seed) { _rng = new Mulberry32(seed); }

        public float Time => (float)_time;
        public double TimeD => _time;
        /// <summary>The switch: 0 off, 1 on.</summary>
        public float Target { get => (float)_target; set => _target = value; }
        public float Level => (float)_level;
        public float Cover => (float)_cover;
        public float Wind => (float)_wind;
        public double WindD => _wind;
        public float WindVel => (float)_windVel;
        /// <summary>The current gust's envelope (0..strength).</summary>
        public float GustValue => (float)_gustValue;
        public float NextGust => (float)_nextGust;
        public int GustCount { get; private set; }
        public bool HasGust => _hasGust;
        public SnowGust LastGust => ToContract(_gust);
        public double LastGustAt => _gust.At;

        public event Action<SnowGust> OnGust;

        static SnowGust ToContract(in Gust g) => new SnowGust { Strength = (float)g.Strength, Attack = (float)g.Attack, Hold = (float)g.Hold, Release = (float)g.Release };

        static double Smooth(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);

        /// <summary>The gust envelope: rises over attack, holds, dies away over release (0 outside).</summary>
        public static float Envelope(in SnowGust g, double at, double t) =>
            (float)Envelope(g.Strength, g.Attack, g.Hold, g.Release, at, t);

        static double Envelope(double strength, double attack, double hold, double release, double at, double t)
        {
            double u = t - at;
            if (u <= 0) return 0;
            if (u < attack) return strength * Smooth(u / attack);
            if (u < attack + hold) return strength;
            if (u < attack + hold + release) return strength * (1 - Smooth((u - attack - hold) / release));
            return 0;
        }

        /// <summary>A new gust: strength U(.45, 1), attack U(1.1, 1.9), hold U(.4, 1.6), release U(2.2, 3.6) in that
        /// random order, then the next gust U(1.5, 6) s after this one has died down; OnGust announces it.</summary>
        public SnowGust StartGust()
        {
            var g = new Gust
            {
                At = _time,
                Strength = 0.45 + _rng.Next() * 0.55,
                Attack = 1.1 + _rng.Next() * 0.8,
                Hold = 0.4 + _rng.Next() * 1.2,
                Release = 2.2 + _rng.Next() * 1.4,
            };
            _gust = g;
            _hasGust = true;
            GustCount++;
            _nextGust = _time + g.Duration + 1.5 + _rng.Next() * 4.5;
            SnowGust c = ToContract(g);
            OnGust?.Invoke(c);
            return c;
        }

        /// <summary>update(dt), in the reference order: time, level, cover, gust schedule, envelope, wind.</summary>
        public void Update(double dt)
        {
            _time += dt;
            double t = _time;
            _level += (_target - _level) * Math.Min(1, dt * LevelRate);
            if (Math.Abs(_level - _target) < LevelSnap) _level = _target;
            if (_target > 0) _cover = Math.Min(1, _cover + dt * CoverUp * _level);
            else _cover = Math.Max(0, _cover - dt * CoverDown);

            // Gusts only while it is really snowing.
            if (_level < GustMinLevel) _nextGust = Math.Max(_nextGust, t + 2);
            else if (t >= _nextGust && (!_hasGust || t - _gust.At > _gust.Duration)) StartGust();
            _gustValue = _hasGust ? Envelope(_gust.Strength, _gust.Attack, _gust.Hold, _gust.Release, _gust.At, t) : 0;
            _windVel = BaseWind * (1 + 0.3 * Math.Sin(t * 0.11)) + GustWind * _gustValue;
            _wind += _windVel * dt;
        }

        /// <summary>Tests and screenshots: jump to a settled state (the switch target follows the level).</summary>
        internal void Settle(float level, float cover, double time)
        {
            _target = level > 0f ? 1 : 0;
            _level = level;
            _cover = cover;
            _time = time;
        }
    }
}
