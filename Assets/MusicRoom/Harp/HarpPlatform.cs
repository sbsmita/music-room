// The harp's platform adapters (12-new-instruments/platform.md 3.3, 13.3): thin forwards over today's harp.
//   IStowable            on display the harp has nothing to hide: SetStowed(true) lets every held string go silently and
//                        ends a desktop drag (what leaving the scene does, OnDisable), so it rests static.
//   IInstrumentPlayback  harp -> IHarpApi.PlaybackPluck (the whole stroke when the event carries one, else the default
//                        pluck), harpDamp -> PlaybackDamp (Lessons/PlaybackWiring's classic lines); ReleaseAll: playback
//                        holds no string; CloseLane: no lane.
//   IGuideAnchors        "string.<i>" (0..21): string i's play point (IHarpApi.PlayPoint, where a seated player plucks),
//                        up = the string plane's normal on the player's side (the harp's local +z).
// Allocation-free: anchor ids are parsed in place.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Harp
{
    public sealed class HarpPlatform : IStowable, IInstrumentPlayback, IGuideAnchors
    {
        public const string StringPrefix = "string.";
        readonly HarpInstrument _harp;

        public HarpPlatform(HarpInstrument harp) { _harp = harp; }

        /// <summary>Creates the adapter and registers its three services (HarpModule.Init).</summary>
        public static HarpPlatform Register(App app, HarpInstrument harp)
        {
            var p = new HarpPlatform(harp);
            app.Register<IStowable>(p);
            app.Register<IInstrumentPlayback>(p);
            app.Register<IGuideAnchors>(p);
            return p;
        }

        public InstrumentKind Kind => InstrumentKind.Harp;

        // ---------------------------------------------------------------- IStowable

        public bool Stowed { get; private set; }

        public void SetStowed(bool stowed, StowMount mount)
        {
            if (stowed && !Stowed && _harp != null)
            {
                _harp.Play?.LeaveAll();
                if (_harp.DesktopCaptured) _harp.EndDesktopDrag();
            }
            Stowed = stowed;
        }

        // ---------------------------------------------------------------- IInstrumentPlayback

        public bool Play(in TakeEvent e)
        {
            if (_harp == null) return false;
            if (e.type == TakeEventType.Harp)
            {
                // Playback pluck: the whole stroke when the event carries one, else the default pluck (.35, 0, through).
                if (string.IsNullOrEmpty(e.mode)) _harp.PlaybackPluck(e.i, e.vel);
                else _harp.PlaybackPluck(e.i, e.vel, e.u, e.speed, e.mode);
                return true;
            }
            if (e.type == TakeEventType.HarpDamp) { _harp.PlaybackDamp(e.i); return true; }
            return false;
        }

        public void ReleaseAll() { }
        public void CloseLane(int lane) { }

        // ---------------------------------------------------------------- IGuideAnchors

        public bool TryGetAnchor(string anchor, out Vector3 world, out Vector3 up)
        {
            world = default;
            up = Vector3.up;
            if (_harp == null || !TryParseIndex(anchor, StringPrefix, out int i) || i >= _harp.StringCount) return false;
            Transform t = _harp.transform;
            world = t.TransformPoint(_harp.PlayPoint(i));
            up = t.forward;
            return true;
        }

        /// <summary>"&lt;prefix&gt;&lt;n&gt;" with n a non-negative integer, read without allocating.</summary>
        public static bool TryParseIndex(string s, string prefix, out int n)
        {
            n = 0;
            if (s == null || s.Length <= prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++) if (s[i] != prefix[i]) return false;
            for (int i = prefix.Length; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9' || n > 100000) return false;
                n = n * 10 + (c - '0');
            }
            return true;
        }
    }
}
