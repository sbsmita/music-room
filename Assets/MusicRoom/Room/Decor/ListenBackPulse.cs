// The room listens back (the Entertainment track's music visualization): while a take or a loop plays, the room reacts to
// the notes it sends back (PlaybackNoteEvent, one event per sound from the recorder). Only a playback: recording and plain
// live play leave the room calm (2026-11-11 headset test: "why its glowing when i started recording?" -- the earlier
// "while playing piano as recording, it should also change lights" meant playing the recording back; RecordedNoteEvent
// no longer reaches the room).
// ListenBackModel turns the notes into levels; this draws them:
//   - soft motes: one ParticleSystem (one draw, 96 at most, world space, additive soft dots: the hover glows' material
//     template) rising gently from above the played parts of the instrument that played, in the playback glow's warm
//     orange (ListenBackModel.Glow, the same for every instrument), as many and as bright as the note was loud; nothing where the instrument is hidden (mixed reality off the seat). In mixed reality
//     the motes are all there is of the room's reaction (the virtual room is hidden; the butterflies still come);
//   - the VR room's own lights, no new light: the floor lamp (+45%, tinted toward the glow's orange), its shade's glow
//     (+100%) and the fire and candles' flicker (+25%), all scaled by the loudest instrument's level. Fast attack, slow
//     fall: when the playback stops the room settles back to its own light by itself and the values are written back
//     exactly once at rest;
//   - the room's flowers (World/FlowerBuilder.cs): their petals glow up to FlowerGlow x their own colour and they sway up
//     to FlowerSway more (the _MusicSway global, RoomLit's _SWAY term weighted per vertex: only the flowers answer it).
// Sunny: tip_listen_back once a playback has sounded TipAfter (tip_record_glow is no longer asked: nothing glows while
// recording).
// The butterflies (Butterflies.cs) read PlaybackModel and the lights, motes and flowers read Model: both are fed by the
// played-back notes only now, so the butterflies, the lights and the flowers all come and go with a playback.
// Main thread: notes come in the Recorder phase, Tick runs in Room's Decor phase. Allocation-free after construction.
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Room.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room.Decor
{
    public sealed class ListenBackPulse
    {
        /// <summary>How much the room's lights rise at full level: the floor lamp, its shade's glow, the flickering fire and
        /// candles, and how far the lamp's colour leans toward the playback glow's orange (ListenBackModel.Glow).</summary>
        public const float LampBoost = 0.45f, ShadeBoost = 1.0f, FlickerBoost = 0.25f, LampTint = 0.6f;
        public const int MaxMotes = 96;
        /// <summary>A mote's life (s), size (m) and rise (m/s) ranges.</summary>
        public const float LifeMin = 1.3f, LifeMax = 2.1f, SizeMin = 0.022f, SizeMax = 0.045f, RiseMin = 0.04f, RiseMax = 0.1f;
        /// <summary>How long an instrument's played-parts bounds are trusted before they are measured again (s).</summary>
        public const float PlaceEvery = 0.25f;
        /// <summary>Sunny's one line for it (Guide's TourTips, asked for here): once a playback has sounded this long (s), asked
        /// again every TipEvery while Sunny is busy, until it was said (or heard in an earlier session).</summary>
        public const string TipId = "tip_listen_back";
        public const float TipAfter = 1.5f, TipEvery = 4f;
        /// <summary>A source counts as sounding while its last note is at most this old (s).</summary>
        public const float SoundingWithin = 1f;
        /// <summary>The flowers at full level: petal emission (x their own colour, linear) and the extra sway (_MusicSway).</summary>
        public const float FlowerGlow = 0.55f, FlowerSway = 1f;
        public static readonly int MusicSwayId = Shader.PropertyToID("_MusicSway");

        readonly App _app;
        readonly RoomScene _scene;
        readonly Material _shade;
        readonly Color _shadeEmission, _lampColor;
        readonly float _lampIntensity;
        readonly Action<PlaybackNoteEvent> _onNote;
        readonly Action<RecorderStateEvent> _onState;
        readonly ParticleSystem _motes;
        readonly Material _moteMaterial;
        readonly GameObject _root;
        readonly Vector3[] _centre = new Vector3[ListenBackModel.Kinds], _extent = new Vector3[ListenBackModel.Kinds];
        readonly float[] _placedAt = new float[ListenBackModel.Kinds];
        readonly bool[] _placed = new bool[ListenBackModel.Kinds];
        Mulberry32 _rng;
        ILayoutService _layout;
        float _time, _shown = -1f;
        bool _wasVisible = true;
        float _soundingFor, _tipAskAt;
        bool _tipDone;
        Material _petals;
        Color _petalBase;
        float _flowerShown = -1f;
        IGuide _guide;

        public ListenBackModel Model { get; } = new ListenBackModel();
        /// <summary>The same envelope fed by the played-back notes only (the butterflies: never for a recording's live notes).</summary>
        public ListenBackModel PlaybackModel { get; } = new ListenBackModel();
        /// <summary>Motes released so far (tests: they count in headless boots, where nothing is drawn).</summary>
        public int MotesReleased { get; private set; }
        /// <summary>The room light level now applied (0 at rest).</summary>
        public float Shown => Mathf.Max(0f, _shown);
        public ParticleSystem Motes => _motes;
        public float LampBaseIntensity => _lampIntensity;
        public Color LampBaseColor => _lampColor;

        public ListenBackPulse(App app, RoomScene scene, Material shade, uint seed)
        {
            _app = app;
            _scene = scene;
            _rng = new Mulberry32(seed);
            if (scene?.FloorLamp != null) { _lampIntensity = scene.FloorLamp.intensity; _lampColor = scene.FloorLamp.color; }
            _shade = shade;
            if (shade != null && shade.HasProperty(MaterialLibrary.EmissionColor)) _shadeEmission = shade.GetColor(MaterialLibrary.EmissionColor);
            for (int k = 0; k < _placedAt.Length; k++) _placedAt[k] = float.NegativeInfinity;
            _onNote = OnNote;
            _onState = OnState;
            app.Events.Subscribe(_onNote);
            app.Events.Subscribe(_onState);
            if (app.Headless) return;
            // Under World, not under the room: the motes stay in mixed reality, where the room is hidden.
            _root = new GameObject("ListenBack");
            _root.transform.SetParent(app.WorldRoot, false);
            _moteMaterial = app.Materials.Clone(MaterialLibrary.ParticlesAdditive);
            _moteMaterial.SetColor(MaterialLibrary.BaseColor, Color.white);
            _moteMaterial.SetTexture(MaterialLibrary.BaseMap, SharedTextures.Get(SharedTextures.SoftDot));
            _motes = BuildMotes(_root.transform, _moteMaterial, seed);
        }

        static ParticleSystem BuildMotes(Transform parent, Material material, uint seed)
        {
            var go = new GameObject("Motes");
            go.SetActive(false);   // configure before the system first plays
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = LifeMax;
            main.startSpeed = 0f;
            main.startSize = SizeMin;
            main.maxParticles = MaxMotes;
            main.gravityModifier = 0f;
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;
            ps.useAutoRandomSeed = false;
            ps.randomSeed = seed;
            var emission = ps.emission;
            emission.enabled = false;   // every mote is released by Emit
            var shape = ps.shape;
            shape.enabled = false;
            // A mote breathes in quickly, then fades as it rises, a little larger as it goes.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.55f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.25f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.maxParticleSize = 0.5f;
            go.SetActive(true);
            return ps;
        }

        void OnNote(PlaybackNoteEvent e)
        {
            double now = _app.AudioTime;
            Model.Note(e, now);
            PlaybackModel.Note(e, now);
        }

        /// <summary>The flowers' petal material (RoomMat.Petal): its emission follows the room's level.</summary>
        public void SetFlowers(Material petals)
        {
            _petals = petals;
            if (petals != null && petals.HasProperty(MaterialLibrary.EmissionColor)) _petalBase = petals.GetColor(MaterialLibrary.EmissionColor);
        }

        /// <summary>The flowers' level now applied (0 at rest).</summary>
        public float FlowerShown => Mathf.Max(0f, _flowerShown);

        void OnState(RecorderStateEvent e)
        {
            if (e.State != RecorderState.Idle) return;
            Model.Stop();
            PlaybackModel.Stop();
        }

        /// <summary>Room's Decor phase, every frame (also while the room is hidden: the motes still show in mixed reality).
        /// roomVisible: the VR room is shown (its lights are lit).</summary>
        public void Tick(float dt, bool roomVisible)
        {
            _time += dt;
            double now = _app.AudioTime;
            Model.Tick(dt, now);
            PlaybackModel.Tick(dt, now);
            if (!Model.Resting) ReleaseMotes();
            ApplyLights(roomVisible ? Model.Room : 0f, roomVisible);
            ApplyFlowers(roomVisible ? Model.Room : 0f);
            AskSunny(dt);
        }

        /// <summary>The flowers bloom brighter and sway more with the music (VR: they are part of the room). Written while it
        /// moves and once at rest (0: the petals' own emission, no extra sway).</summary>
        void ApplyFlowers(float level)
        {
            if (level < ListenBackModel.Rest) level = 0f;
            if (level == _flowerShown) return;
            _flowerShown = level;
            Shader.SetGlobalFloat(MusicSwayId, FlowerSway * level);
            if (_petals == null) return;
            // Emission x the petal palette (the emission map): each petal glows in its own colour. Linear maths, set in gamma
            // form (SetColor linearises), so the rest value is exactly the one read at SetFlowers.
            if (level <= 0f) { _petals.SetColor(MaterialLibrary.EmissionColor, _petalBase); return; }
            Color lin = _petalBase.linear;
            float g = FlowerGlow * level;
            _petals.SetColor(MaterialLibrary.EmissionColor, new Color(lin.r + g, lin.g + g, lin.b + g, 1f).gamma);
        }

        /// <summary>The first listen-back of all: Sunny says to sit back and watch (once ever; a busy Sunny is asked again).</summary>
        void AskSunny(float dt)
        {
            if (_tipDone) return;
            _soundingFor = Model.SincePlayback <= SoundingWithin ? _soundingFor + dt : 0f;
            if (_soundingFor < TipAfter || _time < _tipAskAt) return;
            _tipAskAt = _time + TipEvery;
            if (_guide == null) _guide = _app.Get<IGuide>();
            if (_guide == null) return;
            if (PlaybackTips.Ask(_guide, TipId)) _tipDone = true;   // its turn among the show's lines (PlaybackTips)
        }

        /// <summary>Sunny was asked and is saying it (or had said it before).</summary>
        public bool TipAsked => _tipDone;

        // ---------------------------------------------------------------- the motes

        void ReleaseMotes()
        {
            for (int k = 0; k < ListenBackModel.Kinds; k++)
            {
                var kind = (InstrumentKind)k;
                int n = Model.Motes(kind);
                if (n <= 0 || !Place(kind)) continue;
                float level = Model.Level(kind);
                MotesReleased += n;
                if (_motes == null) continue;
                Color c = ListenBackModel.ColorOf(kind);
                c.a = 0.3f + 0.5f * level;
                Vector3 centre = _centre[k], ext = _extent[k];
                for (int i = 0; i < n; i++)
                {
                    var p = new ParticleSystem.EmitParams
                    {
                        position = new Vector3(centre.x + ext.x * Rand(-0.8f, 0.8f), centre.y + ext.y * Rand(0.4f, 1f) + 0.03f, centre.z + ext.z * Rand(-0.8f, 0.8f)),
                        velocity = new Vector3(Rand(-0.015f, 0.015f), Rand(RiseMin, RiseMax), Rand(-0.015f, 0.015f)),
                        startSize = Rand(SizeMin, SizeMax) * (0.75f + 0.5f * level),
                        startLifetime = Rand(LifeMin, LifeMax),
                        startColor = c,
                        applyShapeToPosition = false,
                    };
                    _motes.Emit(p, 1);
                }
            }
        }

        float Rand(float a, float b) => a + (b - a) * (float)_rng.Next();

        /// <summary>The patch an instrument plays in (its shown played parts' box, measured at most every PlaceEvery): false when
        /// it is not to be seen. The butterflies flutter around it.</summary>
        public bool TryPlace(InstrumentKind kind, out Vector3 centre, out Vector3 extent)
        {
            centre = extent = default;
            if ((uint)(int)kind >= (uint)ListenBackModel.Kinds || !Place(kind)) return false;
            centre = _centre[(int)kind];
            extent = _extent[(int)kind];
            return true;
        }

        /// <summary>Where the motes of an instrument rise: the bounds of its shown played parts (keys, heads, strings, bars),
        /// else of its root. False when it is not to be seen (hidden in mixed reality, not built, out of the layout).</summary>
        bool Place(InstrumentKind kind)
        {
            int k = (int)kind;
            if (_layout == null) _layout = _app.Get<ILayoutService>();
            if (_layout != null && _layout.PlaceOf(kind) == InstrumentPlace.Hidden) return false;
            if (_placed[k] && _time - _placedAt[k] < PlaceEvery) return true;
            IInstrument inst = Find(kind);
            if (inst == null || inst.Root == null || !inst.Root.gameObject.activeInHierarchy) { _placed[k] = false; return false; }
            bool has = false;
            Bounds b = default;
            IReadOnlyList<Renderer> parts = inst.PlayedParts;
            if (parts != null)
                for (int i = 0; i < parts.Count; i++)
                {
                    Renderer r = parts[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (!has) { b = r.bounds; has = true; }
                    else b.Encapsulate(r.bounds);
                }
            if (!has) b = new Bounds(inst.Root.position + Vector3.up * 0.8f, new Vector3(0.4f, 0.2f, 0.4f));
            Vector3 e = b.extents;
            // a sensible patch: never wider than an arm's span, never a point
            _extent[k] = new Vector3(Mathf.Clamp(e.x, 0.05f, 0.7f), Mathf.Clamp(e.y, 0.02f, 0.3f), Mathf.Clamp(e.z, 0.05f, 0.7f));
            _centre[k] = b.center;
            _placed[k] = true;
            _placedAt[k] = _time;
            return true;
        }

        IInstrument Find(InstrumentKind kind)
        {
            IReadOnlyList<IInstrument> all = _app.Instruments;
            for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].Kind == kind) return all[i];
            return null;
        }

        // ---------------------------------------------------------------- the room's lights

        void ApplyLights(float level, bool visible)
        {
            if (level < ListenBackModel.Rest) level = 0f;
            // Written while it moves, and once more when it comes to rest (the room's own values exactly); a room shown again
            // after mixed reality gets its values back too.
            if (level == _shown && visible == _wasVisible) return;
            _shown = level;
            _wasVisible = visible;
            if (_scene == null) return;
            float flicker = 1f + FlickerBoost * level;
            for (int i = 0; i < _scene.Flicker.Count; i++) _scene.Flicker[i].Gain = flicker;
            if (_scene.FloorLamp != null)
            {
                _scene.FloorLamp.intensity = _lampIntensity * (1f + LampBoost * level);
                // toward the glow itself (not lamp x glow, which only darkened the warm lamp): it reads as orange
                _scene.FloorLamp.color = level > 0f ? Color.Lerp(_lampColor, Model.Blend(), LampTint * level) : _lampColor;
            }
            if (_shade != null) _shade.SetColor(MaterialLibrary.EmissionColor, _shadeEmission * (1f + ShadeBoost * level));
        }

        public void Destroy()
        {
            _app.Events.Unsubscribe(_onNote);
            _app.Events.Unsubscribe(_onState);
            Shader.SetGlobalFloat(MusicSwayId, 0f);
            if (_moteMaterial != null) _app.Materials.Destroy(_moteMaterial);
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }
    }
}
