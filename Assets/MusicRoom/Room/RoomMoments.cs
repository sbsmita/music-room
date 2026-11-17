// The room's celebration after the first song (Docs/11-first-five/sunny-tour.md 2.6, IRoomMoments): calm and real,
// no pop-up, no confetti, no glowing text. Its own module (Order 21, after RoomModule) so the Room module is unchanged:
// in the Decor phase it runs after the room's flicker lights and scales what they just set.
//   - the fire flares: its light 2.07 -> 3.0 over 0.6 s and back over 4 s, the flames 30 % taller for 2.5 s;
//   - the lamps glow warmer: the floor lamp and the candles x1.15, about 150 K warmer (#ffd6a0 -> #ffc98c), over 1.5 s,
//     held 4 s, back over 8 s;
//   - snow: the SNOW switch turns on if it was off (flakes start falling past the window and the lever flips: the room
//     teaches its own switch), and a soft gust sweeps them once the snow has built up (at once when it was already on);
//   - a rolled C major chord on the harp, C3 E3 G3 C4 (strings 0, 2, 4, 7), 70 ms apart, soft: the strings visibly ring.
//     Without the harp in the scene the same chord plays on the piano sound (audio only).
// In mixed reality the room is hidden: only the snow (behind real windows) and the chord.
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Room.Decor;
using UnityEngine;

namespace MusicRoom.Room
{
    public sealed class RoomMoments : AppModule, IRoomMoments, ITourAnchors
    {
        public const int ModuleOrder = RoomModule.ModuleOrder + 1;
        public override int Order => ModuleOrder;

        public const float FireRise = 0.6f, FireFall = 4f, FirePeak = 3.0f / 2.0747f;
        public const float FlameRise = 0.4f, FlameHold = 1.6f, FlameFall = 0.5f, FlameGrow = 0.3f;
        public const float LampRise = 1.5f, LampHold = 4f, LampFall = 8f, LampGain = 1.15f;
        public static readonly Color LampWarm = new Color32(0xff, 0xc9, 0x8c, 0xff);
        public static readonly int[] ChordStrings = { 0, 2, 4, 7 };
        public static readonly int[] ChordMidi = { 48, 52, 55, 60 };
        public const float ChordGap = 0.07f, ChordVel = 0.35f;
        /// <summary>The gust waits until the snow is this heavy (or this long), then sweeps once.</summary>
        public const float GustLevel = 0.3f, GustLatest = 6f;
        public const float Duration = LampRise + LampHold + LampFall;

        App _app;
        RoomModule _room;
        IHarpApi _harp;
        IAudioEngine _audio;
        ILayoutService _layout;
        float _t = -1f;
        int _chordNext;
        bool _gustDone, _gustPending;
        Color _lampColor;
        float _lampBase;
        Transform _flames;
        Vector3 _flameScale = Vector3.one;
        readonly float[] _candleBase = new float[4];

        public bool Celebrating => _t >= 0f && _t < Duration;
        public int Celebrations { get; private set; }
        /// <summary>The chord is being played (its plucks are not the player's: Sunny's signals skip them).</summary>
        public bool Chording => _t >= 0f && _t < ChordGap * ChordStrings.Length + 0.2f;
        public float Time => _t;
        /// <summary>The fire light's and the floor lamp's factor right now (tests).</summary>
        public float FireFactor { get; private set; } = 1f;
        public float LampFactor { get; private set; } = 0f;

        public override void Init(App app)
        {
            _app = app;
            foreach (IAppModule m in app.Modules) if (m is RoomModule r) _room = r;
            _audio = app.Get<IAudioEngine>();
            if (_room?.Scene?.FloorLamp != null)
            {
                _lampColor = _room.Scene.FloorLamp.color;
                _lampBase = _room.Scene.FloorLamp.intensity;
            }
            if (_room?.Scene?.Fireplace != null)
            {
                _flames = _room.Scene.Fireplace.Find("Flames");
                if (_flames != null) _flameScale = _flames.localScale;
            }
            app.Register<IRoomMoments>(this);
            app.Register<ITourAnchors>(this);
        }

        /// <summary>The FIRE / WIND / SNOW levers' tops for Sunny's ghost hand (the side table's switch panel).</summary>
        public bool TryGetAnchor(string id, out Vector3 world, out Vector3 up)
        {
            world = default; up = Vector3.up;
            var sw = _room?.Switches;
            if (sw == null || sw.Panel == null || !sw.IsPresent) return false;
            int i = id == "switch.fire" ? 0 : id == "switch.wind" ? 1 : id == "switch.snow" ? 2 : -1;
            if (i < 0 || sw.Toggles[i]?.Lever == null) return false;
            up = sw.Panel.up;
            world = sw.Toggles[i].Lever.position + up * 0.03f;
            return true;
        }

        public void Celebrate()
        {
            _t = 0f;
            _chordNext = 0;
            _gustDone = false;
            Celebrations++;
            IAmbienceSwitches sw = _app.Get<IAmbienceSwitches>();
            _gustPending = true;
            if (sw != null && !sw.Get(AmbienceKind.Snow)) sw.Set(AmbienceKind.Snow, true);   // snow starts falling outside
            Debug.Log("[Room] the first song's celebration: fire, lamps, snow, a harp chord");
        }

        public override void Tick(FramePhase phase, float dt)
        {
            if (phase != FramePhase.Decor || _t < 0f) return;
            _t += dt;
            Chord();
            Gust();
            if (_room == null || _room.Scene == null) return;
            bool visible = _room.Visible;
            // The fire's light (a flicker light: the room set this frame's intensity just now) and the flames.
            float fire = _t < FireRise ? Smooth(_t / FireRise) : _t < FireRise + FireFall ? 1f - Smooth((_t - FireRise) / FireFall) : 0f;
            FireFactor = 1f + (FirePeak - 1f) * fire;
            if (visible && _room.Scene.FireLight != null) _room.Scene.FireLight.intensity *= FireFactor;
            if (_flames != null)
            {
                float f = _t < FlameRise ? Smooth(_t / FlameRise) : _t < FlameRise + FlameHold ? 1f
                    : _t < FlameRise + FlameHold + FlameFall ? 1f - Smooth((_t - FlameRise - FlameHold) / FlameFall) : 0f;
                _flames.localScale = new Vector3(_flameScale.x, _flameScale.y * (1f + FlameGrow * f), _flameScale.z);
            }
            // The lamps, warmer and a little brighter, then back exactly to how they were.
            float lamp = _t < LampRise ? Smooth(_t / LampRise) : _t < LampRise + LampHold ? 1f
                : _t < Duration ? 1f - Smooth((_t - LampRise - LampHold) / LampFall) : 0f;
            LampFactor = lamp;
            Light fl = _room.Scene.FloorLamp;
            if (fl != null)
            {
                fl.intensity = _lampBase * (1f + (LampGain - 1f) * lamp);
                fl.color = Color.Lerp(_lampColor, LampWarm, lamp);
            }
            var candles = _room.Scene.CandleLights;
            for (int i = 0; i < candles.Count; i++)
                if (candles[i] != null && candles[i].isActiveAndEnabled) candles[i].intensity *= 1f + (LampGain - 1f) * lamp;
            if (_t >= Duration)
            {
                if (fl != null) { fl.intensity = _lampBase; fl.color = _lampColor; }
                if (_flames != null) _flames.localScale = _flameScale;
                FireFactor = 1f;
                LampFactor = 0f;
                _t = -1f;
            }
        }

        void Chord()
        {
            if (_chordNext >= ChordStrings.Length || _t < _chordNext * ChordGap) return;
            if (_harp == null) _harp = _app.Get<IHarpApi>();
            if (_layout == null) _layout = _app.Get<ILayoutService>();
            bool harp = _harp != null && _harp.Root != null && _harp.Root.gameObject.activeInHierarchy
                && (_layout == null || _layout.IsInScene(InstrumentKind.Harp));
            while (_chordNext < ChordStrings.Length && _t >= _chordNext * ChordGap)
            {
                if (harp) _harp.PlaybackPluck(ChordStrings[_chordNext], ChordVel);
                else _audio?.Piano(ChordMidi[_chordNext], ChordVel * 0.8f);
                _chordNext++;
            }
        }

        void Gust()
        {
            if (!_gustPending || _gustDone || _room?.Weather == null) return;
            if (_room.Weather.Level < GustLevel && _t < GustLatest) return;
            _gustDone = true;
            _gustPending = false;
            if (_room.Weather.Level > 0.05f) _room.Weather.StartGust();
        }

        static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }
    }
}
