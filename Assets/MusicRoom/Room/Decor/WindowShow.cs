// The snowy window joins in (the playback show): while a recording or a loop plays back, the snow outside the window gusts
// and swirls on every note and the sky fills with soft northern lights in the notes' colours (WindowShowModel has the
// numbers). The VR room's window: its flakes in MusicRoom/Snow, the lights in its night sky (MusicRoom/NightSky). (The
// mixed reality snowy window that joined in too was removed 2026-11-13: in mixed reality the snow falls in the whole
// room, Decor/RoomSnow, which reads the same model.) No new renderer, light or draw: six shader globals
// (Shaders/WindowShow.hlsl) written once a frame while the show runs and once more as exact zeros when it rests, so the
// window is then exactly its own. Only a playback of a recording (RecordingPlaybackEvent Started .. ended, its notes:
// PlaybackNoteEvent); recording (REC, a looper layer's pass) and plain live play never start it, and the recorder starting
// to record ends it. The snow moves only while it snows (the SNOW switch); the lights show whether it snows or not.
// Sunny: tip_window_show, once ever, the first time the lights have been up a moment while the player looks at a window.
// For what must keep off the windows (the playback paint): TryGetWindow / Covers give the windows that show now.
// Main thread: notes come in the Recorder phase, Tick runs in Room's Decor phase. Allocation-free after construction.
using System;
using MusicRoom.Core;
using MusicRoom.Room.World;
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    public sealed class WindowShow
    {
        public static readonly int SwirlId = Shader.PropertyToID("_WindowSwirl"), AuroraId = Shader.PropertyToID("_WindowAurora"),
            AuroraAId = Shader.PropertyToID("_WindowAuroraA"), AuroraBId = Shader.PropertyToID("_WindowAuroraB"),
            AuroraCId = Shader.PropertyToID("_WindowAuroraC"), SnowTintId = Shader.PropertyToID("_WindowSnowTint");

        /// <summary>Sunny's line: once the lights have been up (Aurora above TipLevel) TipAfter seconds with a window in view
        /// (within TipCone degrees of where the player looks), asked again every TipEvery while Sunny is busy, until said.</summary>
        public const string TipId = "tip_window_show";
        public const float TipAfter = 3f, TipEvery = 4f, TipLevel = 0.3f, TipCone = 50f;
        /// <summary>The VR room's window as glass and frame (m), for Covers / TryGetWindow.</summary>
        public static readonly Vector2 RoomWindowSize = new Vector2(RoomDims.WinW + 0.12f, RoomDims.WinH + 0.12f);

        readonly App _app;
        readonly RoomScene _scene;
        readonly Action<PlaybackNoteEvent> _onNote;
        readonly Action<RecordingPlaybackEvent> _onPlayback;
        readonly Action<RecorderStateEvent> _onState;
        bool _roomVisible = true, _pushedRest = true;
        float _time, _upFor, _tipAskAt;
        bool _tipDone;
        IGuide _guide;

        public WindowShowModel Model { get; } = new WindowShowModel();
        /// <summary>Sunny was asked and is saying it (or had said it before).</summary>
        public bool TipAsked => _tipDone;
        /// <summary>Frames that wrote the globals (tests: none while it rests).</summary>
        public int Pushes { get; private set; }

        public WindowShow(App app, RoomScene scene)
        {
            _app = app;
            _scene = scene;
            _onNote = OnNote;
            _onPlayback = OnPlayback;
            _onState = OnState;
            app.Events.Subscribe(_onNote);
            app.Events.Subscribe(_onPlayback);
            app.Events.Subscribe(_onState);
            PushRest();
        }

        void OnNote(PlaybackNoteEvent e) => Model.Note(e, _app.AudioTime);

        void OnPlayback(RecordingPlaybackEvent e)
        {
            if (e.Started) Model.Start();
            else Model.Stop();
        }

        void OnState(RecorderStateEvent e)
        {
            if (e.State == RecorderState.Recording) Model.Stop();
        }

        /// <summary>Room's Decor phase, every frame (also while the room is hidden: the model's gusts drive the room snow).</summary>
        public void Tick(float dt, bool roomVisible)
        {
            _time += dt;
            _roomVisible = roomVisible;
            Model.Tick(dt, _app.AudioTime);
            if (Model.Resting)
            {
                if (!_pushedRest) PushRest();
                _upFor = 0f;
                return;
            }
            _pushedRest = false;
            Pushes++;
            Shader.SetGlobalVector(SwirlId, Model.Swirl);
            Shader.SetGlobalVector(AuroraId, Model.AuroraParams);
            Shader.SetGlobalVector(AuroraAId, Model.Band(0));
            Shader.SetGlobalVector(AuroraBId, Model.Band(1));
            Shader.SetGlobalVector(AuroraCId, Model.Band(2));
            Shader.SetGlobalVector(SnowTintId, Model.SnowTint);
            AskSunny(dt);
        }

        /// <summary>The window's own look: every global exactly 0.</summary>
        void PushRest()
        {
            _pushedRest = true;
            Shader.SetGlobalVector(SwirlId, Vector4.zero);
            Shader.SetGlobalVector(AuroraId, Vector4.zero);
            Shader.SetGlobalVector(AuroraAId, Vector4.zero);
            Shader.SetGlobalVector(AuroraBId, Vector4.zero);
            Shader.SetGlobalVector(AuroraCId, Vector4.zero);
            Shader.SetGlobalVector(SnowTintId, Vector4.zero);
        }

        // ---------------------------------------------------------------- Sunny

        void AskSunny(float dt)
        {
            if (_tipDone) return;
            _upFor = Model.Aurora > TipLevel && WindowInView() ? _upFor + dt : 0f;
            if (_upFor < TipAfter || _time < _tipAskAt) return;
            _tipAskAt = _time + TipEvery;
            if (_guide == null) _guide = _app.Get<IGuide>();
            if (_guide == null) return;
            if (PlaybackTips.Ask(_guide, TipId)) _tipDone = true;   // its turn among the show's lines (PlaybackTips)
        }

        /// <summary>A window that shows is within TipCone of where the player looks.</summary>
        public bool WindowInView()
        {
            Transform head = _app.Head;
            if (head == null) return false;
            float cos = Mathf.Cos(TipCone * Mathf.Deg2Rad);
            for (int i = 0; TryGetWindow(i, out Vector3 c, out _, out _); i++)
            {
                Vector3 to = c - head.position;
                float d = to.magnitude;
                if (d > 1e-3f && Vector3.Dot(head.forward, to / d) >= cos) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- the windows, for what keeps off them

        /// <summary>The i-th window that shows now: only the VR room's window (index 0, while the room shows; mixed reality
        /// has no window since 2026-11-13). Pose: centre, rotation (local +z out through the glass, +y up), size (w, h) in
        /// metres, frame included. False past the last. Allocation-free.</summary>
        public bool TryGetWindow(int i, out Vector3 centre, out Quaternion rotation, out Vector2 size)
        {
            centre = default; rotation = Quaternion.identity; size = default;
            if (i < 0) return false;
            if (i != 0 || !_roomVisible || _scene == null || _scene.Window == null) return false;
            centre = _scene.Window.position;
            rotation = _scene.Window.rotation;
            size = RoomWindowSize;
            return true;
        }

        /// <summary>The point lies on a window that shows now, or within margin (m) of it (in its plane, within margin in
        /// front of or behind the glass). For the playback paint: a splash whose centre is Covers(centre, radius) keeps off.</summary>
        public bool Covers(Vector3 point, float margin)
        {
            for (int i = 0; TryGetWindow(i, out Vector3 c, out Quaternion r, out Vector2 s); i++)
            {
                Vector3 l = Quaternion.Inverse(r) * (point - c);
                if (Mathf.Abs(l.x) <= s.x * 0.5f + margin && Mathf.Abs(l.y) <= s.y * 0.5f + margin && Mathf.Abs(l.z) <= margin + 0.15f) return true;
            }
            return false;
        }

        public void Destroy()
        {
            _app.Events.Unsubscribe(_onNote);
            _app.Events.Unsubscribe(_onPlayback);
            _app.Events.Unsubscribe(_onState);
            Model.Stop();
            PushRest();
        }
    }
}
