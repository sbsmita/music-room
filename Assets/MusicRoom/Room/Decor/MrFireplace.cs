// A fireplace in the player's own room (2026-11-14, the player: "can you show fire place in real room when pressed fire
// btn??"). In mixed reality the FIRE switch on the side table lights a cosy hearth in the real room: the VR room's own
// fireplace (World/HearthBuilder.cs: the same bricks, firebox, mantel, logs, coals, glow, flames, smoke and embers, the
// room's materials), free-standing, its back against a real wall in front of the player when the room's scene model has
// one (Core's IRealWalls, from MR), else on the floor 2.5 m in front, never on an instrument on display, never hidden
// behind one when another place shows it, never over a painting of the gallery, 1 m clear of the head (Decor/
// MrHearthPlace.cs, the maths). The crackle (FIRE's own sound) comes from it, and its warm, flickering light falls on the
// hearth and the instruments near it (one point light, off with the fire: nothing of theirs is changed).
//   - FIRE on: placed where the player is now; it grows up out of the floor (FadeIn), then the fire flares up.
//   - FIRE off: the fire dies down and the hearth sinks away (FadeOut); the next FIRE on places it afresh.
//   - Entering mixed reality still turns FIRE off (MR/MrModule.cs: the player lights it); leaving hides the hearth at once,
//     the crackle goes back to the VR room's fireplace, which burns as before.
//   - It moves (a quick fade out and in) only when an instrument comes to stand in it (a re-arrange, Move instruments), the
//     room's walls arrive after it was put on the floor for want of them, or the player recentres.
// Sunny: tip_mr_fire ("Press FIRE and a fireplace lights up in your room.") once ever, a few seconds into mixed reality
// while FIRE is off, Sunny pointing at the switch.
// Draws while it burns: 9 (brick, soot, stone, logs, coals, glow, flames, smoke, embers); none while FIRE is off.
// Main thread, ticked in Room's Decor phase (also while the room is hidden). Allocation-free per frame once built; placing
// (a FIRE press, a move) reads the scene model and the instruments and may allocate.
using System;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Room.Render;
using MusicRoom.Room.World;
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    public sealed class MrFireplace
    {
        public const string TipId = "tip_mr_fire";
        /// <summary>Sunny's line: after this long in mixed reality with FIRE off, asked again every TipEvery while Sunny is busy.</summary>
        public const float TipAfter = 8f, TipEvery = 4f;
        /// <summary>Seconds to light up (the hearth grows, then the fire flares) and to go out (the fire dies, the hearth sinks).</summary>
        public const float FadeIn = 0.9f, FadeOut = 0.6f;
        /// <summary>The fade's share for the hearth (the first part of it) and where the fire starts (the rest).</summary>
        public const float BuildShare = 0.55f, FireFrom = 0.35f;
        /// <summary>After a re-arrange or a move, look again this much later; with the hearth on the floor for want of walls,
        /// look for walls every WallRetry seconds for the first WallRetryFor seconds.</summary>
        public const float RecheckDelay = 0.8f, WallRetry = 2f, WallRetryFor = 30f;
        public const float LightFlicker = 0.22f;

        readonly App _app;
        readonly RoomMaterials _mats;
        readonly IAmbienceSwitches _switches;
        readonly PaintGallery _gallery;
        readonly uint _seed;
        readonly Action<MixedRealityEvent> _onMr;
        readonly Action<ArrangedEvent> _onArranged;
        readonly Action<InstrumentMoveEvent> _onMove;
        readonly Action<LayoutChangedEvent> _onLayout;
        readonly Action<RecenteredEvent> _onRecentered;
        readonly MrHearthPlace.Scene _scene = new MrHearthPlace.Scene();
        Hearth _hearth;
        FlickerLight _flicker;
        Mulberry32 _rng;
        IMixedReality _mr;
        IGuide _guide;
        IAudioEngine _audio;
        MrHearthPlace.Spot _spot;
        bool _inMr, _placed, _moving, _audioMoved, _tipDone, _destroyed, _sawWalls;
        float _time, _mrTime, _k, _applied = -1f, _recheckAt = -1f, _wallRetryAt, _tipAskAt;

        /// <summary>Tests: the real walls to use instead of the app's (IRealWalls from MR); set to a wall-less one for the no-room case.</summary>
        public IRealWalls RealWallsOverride;
        /// <summary>Log what a placing reads (the instruments' boxes).</summary>
        public static bool DebugPlace;

        public MrFireplace(App app, RoomMaterials mats, IAmbienceSwitches switches, PaintGallery gallery, uint seed)
        {
            _app = app;
            _mats = mats;
            _switches = switches;
            _gallery = gallery;
            _seed = seed;
            _rng = new Mulberry32(seed ^ 0xf1aeu);
            _onMr = OnMr;
            _onArranged = _ => Recheck();
            _onMove = e => { if (e.Phase == MovePhase.Arrived) Recheck(); };
            _onLayout = _ => Recheck();
            _onRecentered = _ => { if (_placed && _k > 0f) _moving = true; };   // the view moved: placed afresh where the player now is
            app.Events.Subscribe(_onMr);
            app.Events.Subscribe(_onArranged);
            app.Events.Subscribe(_onMove);
            app.Events.Subscribe(_onLayout);
            app.Events.Subscribe(_onRecentered);
        }

        /// <summary>The hearth's objects (null until mixed reality is first entered).</summary>
        public Hearth Hearth => _hearth;
        /// <summary>Mixed reality with FIRE on: it burns (or is lighting up).</summary>
        public bool Lit { get; private set; }
        /// <summary>Anything of it shows (lighting up, burning or going out).</summary>
        public bool Showing => _k > 0f;
        /// <summary>0: gone, 1: burning; between: lighting up or going out.</summary>
        public float Level => _k;
        /// <summary>Where it stands (valid once placed).</summary>
        public MrHearthPlace.Spot Spot => _spot;
        public bool Placed => _placed;
        /// <summary>How many times it was placed (tests).</summary>
        public int Placements { get; private set; }
        /// <summary>Sunny was asked and is saying it (or had said it before).</summary>
        public bool TipAsked => _tipDone;
        /// <summary>The crackle comes from the hearth now.</summary>
        public bool SoundAtHearth => _audioMoved;

        IRealWalls RealWalls => RealWallsOverride ?? _app.Get<IRealWalls>();

        void OnMr(MixedRealityEvent e)
        {
            if (e.Active) { EnsureBuilt(); return; }
            HideNow();   // leaving: at once, and the crackle goes back to the VR room's fireplace
        }

        void Recheck()
        {
            if (_placed && _k > 0f) _recheckAt = _time + RecheckDelay;
        }

        public void Tick(float dt, bool roomVisible)
        {
            if (_destroyed) return;
            _time += dt;
            if (_mr == null) _mr = _app.Get<IMixedReality>();
            bool mr = !roomVisible && _mr != null && _mr.Active;
            if (!mr)
            {
                if (_inMr || _k > 0f || _audioMoved) HideNow();
                return;
            }
            if (!_inMr) { _inMr = true; _mrTime = 0f; EnsureBuilt(); }
            _mrTime += dt;
            bool on = _switches != null && _switches.Get(AmbienceKind.Fire);
            Lit = on;
            if (on && !_placed && !_moving) Place();
            if (on && _placed && _k >= 1f && !_moving)
            {
                if (_recheckAt >= 0f && _time >= _recheckAt) { _recheckAt = -1f; if (InTheWay()) _moving = true; }
                else if (!_spot.OnWall && !_sawWalls && _mrTime < WallRetryFor && _time >= _wallRetryAt)
                {
                    _wallRetryAt = _time + WallRetry;
                    IRealWalls walls = RealWalls;
                    if (walls != null && walls.Refresh() && walls.WallCount > 0) _moving = true;
                }
            }
            float target = on && !_moving ? 1f : 0f;
            if (_k < target) _k = Mathf.Min(target, _k + dt / FadeIn);
            else if (_k > target) _k = Mathf.Max(target, _k - dt / FadeOut);
            if (_k <= 0f)
            {
                if (_moving) { _moving = false; _placed = false; if (on) Place(); }
                else if (!on) _placed = false;   // the next FIRE on places it afresh
            }
            Apply(dt);
            AskSunny(on);
        }

        /// <summary>Builds the hearth (once, on entering mixed reality: never for a player who stays in the VR room).</summary>
        void EnsureBuilt()
        {
            if (_hearth != null || _destroyed || _app.WorldRoot == null) return;
            _hearth = HearthBuilder.Build(_app.WorldRoot, _mats, _seed);
            _flicker = new FlickerLight(_hearth.Light, HearthBuilder.LightIntensity, LightFlicker, (float)_rng.Next() * 6.28f);
            _applied = -1f;
        }

        /// <summary>Where it stands now: the walls, blocks, instruments and paintings read afresh (setup: may allocate).</summary>
        void Place()
        {
            EnsureBuilt();
            Transform head = _app.Head;
            if (_hearth == null || head == null) return;
            Gather(head);
            if (DebugPlace) _scene.Trace = new System.Text.StringBuilder();
            _spot = MrHearthPlace.Find(_scene);
            if (DebugPlace)
            {
                Debug.Log("[Room] fireplace walls: " + _scene.Trace);
                _scene.Trace = null;
                for (int i = 0; i < _scene.Walls.Count; i++) Debug.Log($"[Room] fireplace wall {i}: {_scene.Walls[i].Centre:F2} in {_scene.Walls[i].Inward:F2} size {_scene.Walls[i].Size:F2}");
                for (int i = 0; i < _scene.Blocks.Count; i++) Debug.Log($"[Room] fireplace block {i}: {_scene.Blocks[i].Centre:F2} half {_scene.Blocks[i].Half:F2} rot {_scene.Blocks[i].Rotation.eulerAngles:F0}");
            }
            _sawWalls = _scene.Walls.Count > 0;   // on the floor with walls known: no wall had room; looking again will not help
            if (!_spot.Found) return;
            _hearth.Root.SetPositionAndRotation(_spot.Pose.position, _spot.Pose.rotation);
            _placed = true;
            Placements++;
            _recheckAt = -1f;
            _wallRetryAt = _time + WallRetry;
            if (_audio == null) _audio = _app.Get<IAudioEngine>();
            if (_audio != null) { _audio.SetSourcePosition(SourceId.Fire, _spot.FirePoint); _audioMoved = true; }
            Debug.Log($"[Room] mixed reality fireplace: {_spot} ({_scene.Walls.Count} walls, {_scene.Blocks.Count} blocks, {_scene.Rigs.Count} instruments, {_scene.Frames.Count} paintings)");
        }

        void Gather(Transform head)
        {
            _scene.Clear();
            _scene.Head = head.position;
            _scene.Forward = head.forward;
            _scene.Floor = RoomSnowModel.MrFloor(head.position.y, _mr != null ? _mr.FloorHeight : 0f);
            IRealWalls walls = RealWalls;
            if (walls != null && walls.Refresh())
            {
                for (int i = 0; i < walls.WallCount; i++) _scene.Walls.Add(walls.Wall(i));
                for (int i = 0; i < walls.BlockCount; i++) _scene.Blocks.Add(walls.Block(i));
            }
            ReadRigs();
            ReadFrames();
        }

        /// <summary>Every instrument on display: the box of its visible parts (handles and played parts); a rig with nothing
        /// showing is not on display.</summary>
        void ReadRigs()
        {
            var rigs = _app.Get<IRigRegistry>()?.Rigs;
            if (rigs == null) return;
            for (int i = 0; i < rigs.Count; i++)
            {
                Rig r = rigs[i];
                if (r?.Group == null || !r.Group.gameObject.activeInHierarchy) continue;
                bool has = false;
                Bounds b = default;
                Add(r.Handles, ref b, ref has, _scene.Sight);
                for (int k = 0; k < r.Instruments.Count; k++) if (r.Instruments[k] != null) Add(r.Instruments[k].PlayedParts, ref b, ref has, _scene.Sight);
                if (!has) continue;   // nothing of it shows (a closed lessons panel): not on display
                _scene.Rigs.Add(b);
                if (DebugPlace) Debug.Log($"[Room] fireplace: instrument {r.Id} box {b.center:F2} size {b.size:F2}");
            }

            static void Add(System.Collections.Generic.IReadOnlyList<Renderer> list, ref Bounds b, ref bool has, System.Collections.Generic.List<Bounds> sight)
            {
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                {
                    Renderer rr = list[i];
                    if (rr == null || !rr.enabled || !rr.gameObject.activeInHierarchy || rr is ParticleSystemRenderer) continue;
                    Bounds rb = rr.bounds;
                    if (rb.size.sqrMagnitude < 1e-6f) continue;
                    if (!has) { b = rb; has = true; } else b.Encapsulate(rb);
                    sight.Add(rb);
                }
            }
        }

        /// <summary>The paintings hung on the real walls (the gallery's mixed reality frames).</summary>
        void ReadFrames()
        {
            if (_gallery == null || !_gallery.MrShown) return;
            var slots = _gallery.MrSlots;
            foreach (PaintGallery.Entry e in _gallery.Entries)
            {
                if (e.Slot < 0 || e.Slot >= slots.Count || slots[e.Slot].Wall < 0) continue;
                PaintGallery.Slot s = slots[e.Slot];
                float hw = 0.5f * s.W + PaintGallery.Border, hh = 0.5f * s.H + PaintGallery.Border + PaintGallery.PlaqueGap + PaintGallery.PlaqueH;
                var b = new Bounds(s.Centre, Vector3.zero);
                for (int c = 0; c < 4; c++)
                    b.Encapsulate(s.Centre + s.Right * ((c & 1) == 0 ? -hw : hw) + s.Up * ((c & 2) == 0 ? -hh : hh) + s.Normal * PaintGallery.Depth);
                _scene.Frames.Add(b);
            }
        }

        /// <summary>An instrument now stands in the hearth (after a re-arrange or a move).</summary>
        bool InTheWay()
        {
            _scene.Rigs.Clear();
            _scene.Sight.Clear();
            ReadRigs();
            for (int i = 0; i < _scene.Rigs.Count; i++) if (MrHearthPlace.Overlaps(_spot.Pose, _scene.Rigs[i], 0f, _spot.Size)) return true;
            return false;
        }

        /// <summary>The fade: the hearth grows out of the floor over the first BuildShare, the fire flares from FireFrom.
        /// Material and light writes only while the level changes; the flicker every frame while it shows.</summary>
        void Apply(float dt)
        {
            if (_hearth == null) return;
            bool show = _k > 0f;
            if (_hearth.Root.gameObject.activeSelf != show) _hearth.Root.gameObject.SetActive(show);
            if (!show) { _applied = 0f; return; }
            float fire = Smooth((_k - FireFrom) / (1f - FireFrom));
            if (_k != _applied)
            {
                _applied = _k;
                float s = Smooth(_k / BuildShare), across = Mathf.Lerp(0.7f, 1f, s);
                _hearth.Root.localScale = MrHearthPlace.Scale * _spot.Size * new Vector3(across, Mathf.Max(0.001f, s), across);   // the music room's fireplace at 0.85 (smaller in a tight room)
                if (_hearth.FlameMaterial != null) _hearth.FlameMaterial.SetFloat(HearthBuilder.Intensity, fire);
                if (_hearth.GlowMaterial != null)
                {
                    Vector4 p = _hearth.GlowPulse, p2 = _hearth.GlowPulse2;
                    _hearth.GlowMaterial.SetVector(HearthBuilder.Pulse, new Vector4(p.x * fire, p.y * fire, p.z, p.w));
                    _hearth.GlowMaterial.SetVector(HearthBuilder.Pulse2, new Vector4(p2.x * fire, p2.y, p2.z, p2.w));
                }
                _flicker.Gain = fire;
                if (_hearth.Light.enabled != fire > 0f) _hearth.Light.enabled = fire > 0f;
            }
            _flicker.Update(_time, dt, ref _rng);
        }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Gone at once (leaving mixed reality); the crackle back at the VR room's fireplace.</summary>
        void HideNow()
        {
            _inMr = false;
            Lit = false;
            _k = 0f;
            _placed = _moving = false;
            _recheckAt = -1f;
            Apply(0f);
            if (_audioMoved)
            {
                if (_audio == null) _audio = _app.Get<IAudioEngine>();
                _audio?.SetSourcePosition(SourceId.Fire, AudioModule.FirePosition);
                _audioMoved = false;
            }
        }

        void AskSunny(bool on)
        {
            if (_tipDone || on || _mrTime < TipAfter || _time < _tipAskAt) return;
            _tipAskAt = _time + TipEvery;
            if (_guide == null) _guide = _app.Get<IGuide>();
            if (_guide == null) return;
            if (_guide.TipSeen(TipId) || _guide.Tip(TipId) > 0f) _tipDone = true;
        }

        public void Destroy()
        {
            _destroyed = true;
            _app.Events.Unsubscribe(_onMr);
            _app.Events.Unsubscribe(_onArranged);
            _app.Events.Unsubscribe(_onMove);
            _app.Events.Unsubscribe(_onLayout);
            _app.Events.Unsubscribe(_onRecentered);
            _hearth?.Destroy();
            _hearth = null;
        }
    }
}
