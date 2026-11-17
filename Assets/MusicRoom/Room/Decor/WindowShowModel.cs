// The snowy window joins in (the playback show), its numbers: no Unity objects, so EditMode tests drive it directly.
// Only while a recording or a loop plays back (Start .. Stop, fed the played-back notes, PlaybackNoteEvent): every onset
// blows a gust through the snow outside the window (Gust: kicked to GustFloor + (1 - GustFloor) x velocity with a fast
// attack, then calming over GustDecay; the flakes swirl in small eddies (Phase turns them), whirl about the middle of the
// field and lift a little, all scaled by Gust, so at Gust 0 every flake is exactly where the plain snowfall puts it), and
// the sky fills with northern lights: three curtains (bands: low, middle, high notes; the drums' kick and floor tom low,
// snare and toms middle, hats and cymbals high), each easing toward the colour of its latest note (the night palette:
// green, teal, violet, a touch of pink; the instrument and the pitch pick the shade) and flaring with its notes' velocity;
// the whole sky brighter for louder playing (Energy) and rippling on each beat (Ripple). Stop (the playback ended, the
// recorder stopped or started recording) drops what waits and lets everything fall: the aurora fades out over a few
// seconds, the gusts calm, and once every value is exactly 0 the model rests (Resting) and the clocks go back to 0.
// A note the looper sends ahead (When, its audio time) waits in a small ring until it sounds. Allocation-free.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    public sealed class WindowShowModel
    {
        public const int Bands = 3, PendingMax = 32;
        /// <summary>The gust: the least a soft note blows, rise and calm time constants (s).</summary>
        public const float GustFloor = 0.35f, GustAttack = 0.06f, GustDecay = 0.9f;
        /// <summary>The beat's ripple through the curtains (s).</summary>
        public const float RippleAttack = 0.05f, RippleDecay = 0.4f;
        /// <summary>Loudness of the playing: rise, fall (s).</summary>
        public const float EnergyAttack = 0.12f, EnergyDecay = 2.5f;
        /// <summary>The aurora as a whole: rise and fade time constants (s), its level for soft and for loud playing, and how
        /// long a silence in the playback keeps it up (s).</summary>
        public const float AuroraUp = 0.8f, AuroraDown = 1.1f, AuroraSoft = 0.5f, Silence = 6f;
        /// <summary>A band's flare (s) and how fast its colour turns to its newest note's (s).</summary>
        public const float BandAttack = 0.08f, BandDecay = 1.8f, ColourEase = 0.6f;
        /// <summary>Below this a falling value is exactly 0 (the rest).</summary>
        public const float Rest = 0.004f;
        /// <summary>The swirl at a full gust: eddy turn rate (rad/s, at rest and extra at full), whirl (rad at the middle), lift (m).</summary>
        public const float SpinRest = 1.0f, SpinGust = 2.5f, WhirlMax = 0.9f, LiftMax = 0.16f;
        /// <summary>The snow catches the aurora's light: the flakes' tint at full aurora.</summary>
        public const float SnowTintMax = 0.16f;
        /// <summary>The window frame's height (m, from its centre) the whirl turns about (shader WINDOW_WHIRL_Y).</summary>
        public const float WhirlY = 0.1f;

        // Night palette (sRGB): green, teal, violet, pink, at t = 0, .4, .75, 1.
        static readonly Color P0 = new Color(0.24f, 1.00f, 0.60f), P1 = new Color(0.16f, 0.88f, 0.84f),
            P2 = new Color(0.55f, 0.42f, 1.00f), P3 = new Color(1.00f, 0.47f, 0.83f);

        struct Pending { public int Band; public float Hue, Level; public EventRole Role; public double When; }

        /// <summary>One envelope: a kick rises with the attack and falls with the decay once it has (nearly) landed.</summary>
        struct Env
        {
            public float Value, Kick;
            public void Hit(float v) { if (v > Kick) Kick = v; }
            public void Tick(float up, float down)
            {
                if (Kick > Value)
                {
                    Value += (Kick - Value) * up;
                    if (Value >= 0.9f * Kick) Kick = 0f;
                }
                else
                {
                    Kick = 0f;
                    Value *= down;
                    if (Value < Rest) Value = 0f;
                }
            }
        }

        Env _gust, _ripple, _energy;
        readonly Env[] _band = new Env[Bands];
        readonly Vector3[] _col = new Vector3[Bands], _colTarget = new Vector3[Bands];
        readonly Pending[] _pending = new Pending[PendingMax];
        int _pendingCount;

        /// <summary>A playback runs (Start .. Stop): only then do notes count.</summary>
        public bool Playing { get; private set; }
        /// <summary>Every value is exactly 0, the clocks are 0 and nothing waits: the window is its own.</summary>
        public bool Resting { get; private set; } = true;
        public float Gust => _gust.Value;
        public float Ripple => _ripple.Value;
        public float Energy => _energy.Value;
        /// <summary>The northern lights' level (0..1).</summary>
        public float Aurora { get; private set; }
        /// <summary>The eddies' turn (rad) and the aurora's own clock (s); 0 at rest.</summary>
        public float Phase { get; private set; }
        public float AuroraTime { get; private set; }
        public float SinceNote { get; private set; } = float.PositiveInfinity;
        /// <summary>Onsets taken in while playing (tests, the log).</summary>
        public int Notes { get; private set; }
        public int PendingCount => _pendingCount;
        public float BandLevel(int b) => _band[b].Value;
        /// <summary>A band's colour now (linear).</summary>
        public Vector3 BandColour(int b) => _col[b];

        public WindowShowModel() { ResetColours(); }

        void ResetColours()
        {
            for (int b = 0; b < Bands; b++)
            {
                Color c = Palette(b == 0 ? 0f : b == 1 ? 0.4f : 0.75f).linear;
                _col[b] = _colTarget[b] = new Vector3(c.r, c.g, c.b);
            }
        }

        // ---------------------------------------------------------------- input

        /// <summary>A playback of a recording started.</summary>
        public void Start() { Playing = true; }

        /// <summary>It ended (or the recorder stopped / began recording): nothing more counts, what waits is dropped, the rest falls.</summary>
        public void Stop()
        {
            Playing = false;
            _pendingCount = 0;
        }

        /// <summary>A played-back note. now: the audio time; a note for later waits until it sounds. Ignored unless Playing.</summary>
        public void Note(in PlaybackNoteEvent e, double now)
        {
            if (!Playing || e.Role == EventRole.Marker || e.Role == EventRole.Release) return;
            Voice(e, out int band, out float hue);
            float level = Mathf.Clamp01(e.Level);
            if (e.When > now + 1e-4)
            {
                if (_pendingCount < PendingMax) _pending[_pendingCount++] = new Pending { Band = band, Hue = hue, Level = level, Role = e.Role, When = e.When };
                Resting = false;
                return;
            }
            Apply(band, hue, level, e.Role);
        }

        void Apply(int band, float hue, float level, EventRole role)
        {
            Resting = false;
            SinceNote = 0f;
            if (role == EventRole.Control)
            {
                // a held, rubbed lane: it keeps the sky up and its band glowing, no gust
                _energy.Hit(0.8f * level);
                _band[band].Hit(0.6f * level);
                return;
            }
            Notes++;
            _gust.Hit(GustFloor + (1f - GustFloor) * level);
            _ripple.Hit(level);
            _energy.Hit(level);
            _band[band].Hit(0.35f + 0.65f * level);
            Color c = Palette(hue).linear;
            _colTarget[band] = new Vector3(c.r, c.g, c.b);
        }

        /// <summary>Which curtain a note lights and its shade (0 green .. 1 pink): the instrument and the pitch.</summary>
        public static void Voice(in PlaybackNoteEvent e, out int band, out float hue)
        {
            TakeEvent ev = e.Event;
            switch (e.Kind)
            {
                case InstrumentKind.Piano:
                {
                    float p = Mathf.Clamp01((ev.midi - 21) / 87f);
                    band = BandOf(p);
                    hue = 0.05f + 0.75f * p;
                    return;
                }
                case InstrumentKind.Harp:
                {
                    float p = Mathf.Clamp01(ev.i / 21f);   // 22 strings
                    band = BandOf(p);
                    hue = 0.4f + 0.6f * p;
                    return;
                }
                case InstrumentKind.Drums:
                    DrumVoice(ev.type == TakeEventType.Hat ? "hat" : ev.kind, out band, out hue);
                    return;
                default:
                {
                    float p = Mathf.Clamp01((ev.midi - 21) / 87f);
                    band = ev.midi > 0 ? BandOf(p) : 1;
                    hue = ev.midi > 0 ? 0.2f + 0.6f * p : 0.55f;
                    return;
                }
            }
        }

        static int BandOf(float pitch) => pitch < 0.4f ? 0 : pitch < 0.62f ? 1 : 2;

        static void DrumVoice(string kind, out int band, out float hue)
        {
            switch (kind)
            {
                case "kick": band = 0; hue = 0f; return;
                case "floor": band = 0; hue = 0.18f; return;
                case "snare": band = 1; hue = 0.4f; return;
                case "tom1": band = 1; hue = 0.55f; return;
                case "tom2": band = 1; hue = 0.48f; return;
                case "ride": band = 2; hue = 0.85f; return;
                case "crash": band = 2; hue = 1f; return;
                case "hat": case "hatOpen": case "hatClose": band = 2; hue = 0.75f; return;
                default: band = 1; hue = 0.45f; return;
            }
        }

        /// <summary>The night palette at t (sRGB): green 0, teal .4, violet .75, pink 1.</summary>
        public static Color Palette(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < 0.4f) return Color.Lerp(P0, P1, t / 0.4f);
            if (t < 0.75f) return Color.Lerp(P1, P2, (t - 0.4f) / 0.35f);
            return Color.Lerp(P2, P3, (t - 0.75f) / 0.25f);
        }

        // ---------------------------------------------------------------- one frame

        public void Tick(float dt, double now)
        {
            if (dt < 0f) dt = 0f;
            if (SinceNote < float.PositiveInfinity) SinceNote += dt;
            if (Resting) return;
            for (int i = 0; i < _pendingCount;)
            {
                if (_pending[i].When <= now + 1e-4)
                {
                    Apply(_pending[i].Band, _pending[i].Hue, _pending[i].Level, _pending[i].Role);
                    _pending[i] = _pending[--_pendingCount];
                }
                else i++;
            }
            _gust.Tick(1f - Mathf.Exp(-dt / GustAttack), Mathf.Exp(-dt / GustDecay));
            _ripple.Tick(1f - Mathf.Exp(-dt / RippleAttack), Mathf.Exp(-dt / RippleDecay));
            _energy.Tick(1f - Mathf.Exp(-dt / EnergyAttack), Mathf.Exp(-dt / EnergyDecay));
            float bandUp = 1f - Mathf.Exp(-dt / BandAttack), bandDown = Mathf.Exp(-dt / BandDecay), ease = 1f - Mathf.Exp(-dt / ColourEase);
            for (int b = 0; b < Bands; b++)
            {
                _band[b].Tick(bandUp, bandDown);
                _col[b] += (_colTarget[b] - _col[b]) * ease;
            }

            float target = Playing && SinceNote < Silence ? AuroraSoft + (1f - AuroraSoft) * _energy.Value : 0f;
            float a = Aurora;
            if (target > a) a += (target - a) * (1f - Mathf.Exp(-dt / AuroraUp));
            else a = target + (a - target) * Mathf.Exp(-dt / AuroraDown);
            if (target <= 0f && a < Rest) a = 0f;
            Aurora = a;

            Phase = _gust.Value > 0f ? Phase + dt * (SpinRest + SpinGust * _gust.Value) : 0f;
            AuroraTime = a > 0f ? AuroraTime + dt : 0f;

            // Once the playback is over, the lights are out and the snow is calm, the rest (ripple, loudness, the curtains'
            // flares and colours) is unseen (the lights' level multiplies all of it): it goes straight to rest.
            if (!Playing && _pendingCount == 0 && a <= 0f && _gust.Value <= 0f)
            {
                Resting = true;
                Phase = 0f;
                AuroraTime = 0f;
                _gust = _ripple = _energy = default;
                for (int b = 0; b < Bands; b++) _band[b] = default;
                ResetColours();
            }
        }

        // ---------------------------------------------------------------- what the shaders get

        /// <summary>_WindowSwirl: (gust, eddy phase, whirl rad, lift m); all 0 at rest.</summary>
        public Vector4 Swirl => new Vector4(_gust.Value, Phase, WhirlMax * _gust.Value, LiftMax * _gust.Value);

        /// <summary>_WindowAurora: (level, clock s, ripple, 0); the level flares a little on each beat.</summary>
        public Vector4 AuroraParams => new Vector4(Aurora * (0.75f + 0.25f * _ripple.Value), AuroraTime, _ripple.Value, 0f);

        /// <summary>_WindowAuroraA / B / C: a curtain's colour (linear) and its flare.</summary>
        public Vector4 Band(int b) => new Vector4(_col[b].x, _col[b].y, _col[b].z, _band[b].Value);

        /// <summary>_WindowSnowTint: the aurora's light on the flakes (the bands' mean colour x level).</summary>
        public Vector4 SnowTint
        {
            get
            {
                float k = SnowTintMax * Aurora / Bands;
                Vector3 s = (_col[0] + _col[1] + _col[2]) * k;
                return new Vector4(s.x, s.y, s.z, 0f);
            }
        }

        /// <summary>The flake's swirl, the shader's maths on the CPU (Snow.shader WindowSwirl): x, y in the window frame (m),
        /// d its distance behind the glass, seed (size, sway, rate, phase), swirl = Swirl. Exactly (x, y) at gust 0.</summary>
        public static Vector2 SwirlOffset(Vector2 xy, float d, Vector4 seed, Vector4 swirl)
        {
            float g = swirl.x;
            if (!(g > 0f)) return xy;
            float x = xy.x, y = xy.y;
            float ph = seed.w * 6.2831853f;
            float sp = swirl.y * Mathf.Lerp(0.7f, 1.6f, seed.z) + ph * 1.3f;
            float er = g * Mathf.Lerp(0.04f, 0.2f, seed.y) * (1f + 0.05f * d);
            x += er * Mathf.Cos(sp);
            y += er * 0.8f * Mathf.Sin(sp) + swirl.w * Mathf.Lerp(0.4f, 1f, seed.x);
            float r = 0.55f + 0.12f * d;
            float qx = x, qy = y - WhirlY;
            float ang = swirl.z * Mathf.Exp(-(qx * qx + qy * qy) / (r * r));
            float s = Mathf.Sin(ang), c = Mathf.Cos(ang);
            return new Vector2(c * qx - s * qy, s * qx + c * qy + WhirlY);
        }
    }
}
