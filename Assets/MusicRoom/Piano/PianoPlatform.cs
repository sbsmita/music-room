// The piano's platform adapters (12-new-instruments/platform.md 3.3, 13.3): thin forwards over today's piano, so Layout,
// Lessons' generic playback, Sunny, mixed reality and the conformance suite reach it like any registry instrument.
//   IStowable            on display the piano has nothing to hide: SetStowed(true) lets every held key go (as when its
//                        group is disabled) so it rests static; Stowed hides the music rest's pose (MusicRestPose null).
//   IInstrumentPlayback  pianoOn -> IPianoApi.PlaybackOn, pianoOff -> PlaybackOff (Lessons/PlaybackWiring's classic
//                        lines), ReleaseAll -> PlaybackReleaseAll, CloseLane: no continuous lane.
//   IGuideAnchors        "key.<midi>" the top centre of that key (white z L/2 at y 0, black over its own length at BTOP),
//                        "keys.middle" the centre of the keyboard's top; up = the piano's up.
//   IInstrumentSurface   OnSurface / SetOnSurface -> IPianoApi.OnSurface / SetOnSurface / ResetSurface (MR's table).
// PianoMusicRest: the music rest's front face, bottom centre, measured from PianoBody.MusicRest (the sheet-music hook,
// IPianoApi.MusicRestPose / MusicRestLocal / MusicRestLedge; PianoDefinition.SheetAnchor is the same pose).
// Allocation-free: anchor ids are parsed in place.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Piano
{
    public sealed class PianoPlatform : IStowable, IInstrumentPlayback, IGuideAnchors, IInstrumentSurface
    {
        public const string KeyPrefix = "key.", KeysMiddle = "keys.middle";
        readonly PianoInstrument _piano;

        public PianoPlatform(PianoInstrument piano) { _piano = piano; }

        /// <summary>Creates the adapter for <paramref name="piano"/> and registers its four services (PianoModule.Init).</summary>
        public static PianoPlatform Register(App app, PianoInstrument piano)
        {
            var p = new PianoPlatform(piano);
            piano.Platform = p;
            app.Register<IStowable>(p);
            app.Register<IInstrumentPlayback>(p);
            app.Register<IGuideAnchors>(p);
            app.Register<IInstrumentSurface>(p);
            return p;
        }

        public InstrumentKind Kind => InstrumentKind.Piano;

        // ---------------------------------------------------------------- IStowable

        public bool Stowed { get; private set; }

        public void SetStowed(bool stowed, StowMount mount)
        {
            if (stowed && !Stowed && _piano != null && _piano.Action != null)
            {
                // The piano's own reset when its group goes away (OnDisable): no key stays down on display.
                _piano.Action.ResetAll();
                _piano.Desktop?.ReleaseAll();
                _piano.View?.Sync();
            }
            Stowed = stowed;
        }

        // ---------------------------------------------------------------- IInstrumentPlayback

        public bool Play(in TakeEvent e)
        {
            if (_piano == null) return false;
            if (e.type == TakeEventType.PianoOn) { _piano.PlaybackOn(e.midi, e.vel); return true; }
            if (e.type == TakeEventType.PianoOff) { _piano.PlaybackOff(e.midi); return true; }
            return false;
        }

        public void ReleaseAll() => _piano?.PlaybackReleaseAll();
        public void CloseLane(int lane) { }

        // ---------------------------------------------------------------- IGuideAnchors

        public bool TryGetAnchor(string anchor, out Vector3 world, out Vector3 up)
        {
            world = default;
            up = Vector3.up;
            if (_piano == null || anchor == null) return false;
            Transform t = _piano.transform;
            up = t.up;
            if (string.Equals(anchor, KeysMiddle, System.StringComparison.Ordinal))
            {
                world = t.TransformPoint(new Vector3(0f, 0f, PianoSpec.L * 0.5f));
                return true;
            }
            if (!TryParseIndex(anchor, KeyPrefix, out int midi) || !KeyTop(midi, out Vector3 local)) return false;
            world = t.TransformPoint(local);
            return true;
        }

        /// <summary>Piano-local top centre of a key: a white key's whole top (z 0..L) at y 0, a black key's (z L - BL..L) at BTOP.</summary>
        public static bool KeyTop(int midi, out Vector3 local)
        {
            local = default;
            if (midi < PianoSpec.FirstMidi || midi > PianoSpec.LastMidi) return false;
            bool black = PianoSpec.IsBlack(midi);
            local = new Vector3(PianoSpec.KeyX(midi), black ? PianoSpec.BTOP : 0f, black ? PianoSpec.L - PianoSpec.BL * 0.5f : PianoSpec.L * 0.5f);
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

        // ---------------------------------------------------------------- IInstrumentSurface

        public bool OnSurface => _piano != null && _piano.OnSurface;

        public void SetOnSurface(bool onSurface)
        {
            if (_piano == null) return;
            if (onSurface) _piano.SetOnSurface(true);
            else _piano.ResetSurface();
        }
    }

    /// <summary>The music rest the sheet-music module stands its book on (sheet.md 1.1, 12 item 1), from PianoBody.MusicRest:
    /// a 5 mm board whose bottom edge sits in the slot at back Name + .087, y BodyTop + .001, leaning back 0.3 rad, with a
    /// 7 mm ledge (y BodyTop + .001 .. + .008) along its foot. Pose: the board's front face, bottom centre, piano-local; up
    /// along the face, forward into it (away from the player). Ledge: the ledge's top, measured up the face from there.</summary>
    public static class PianoMusicRest
    {
        public const float Lean = 0.3f, Board = 0.005f, LedgeHeight = 0.007f;
        public static readonly float Slot = PianoBody.Name + 0.087f, Foot = PianoBody.Top + 0.001f;
        public static readonly Vector3 Up = new Vector3(0f, Mathf.Cos(Lean), Mathf.Sin(Lean));
        public static readonly Vector3 Forward = new Vector3(0f, -Mathf.Sin(Lean), Mathf.Cos(Lean));
        /// <summary>The front face's bottom edge: the slot's pivot moved half the board's thickness toward the player, turned
        /// with the lean: (0, .0397, .2376).</summary>
        public static readonly Pose Local = new Pose(new Vector3(0f, Foot, Slot) - Forward * (Board * 0.5f), Quaternion.LookRotation(Forward, Up));
        /// <summary>The ledge's top along the face from Local: (BodyTop + .008 - Local.y) / cos(lean) = 6.55 mm.</summary>
        public static readonly float Ledge = (Foot + LedgeHeight - Local.position.y) / Mathf.Cos(Lean);

        /// <summary>World pose of the rest on <paramref name="piano"/>, or null while it is not at the seat (out of the
        /// layout, gliding, on display, or disabled).</summary>
        public static Pose? World(PianoInstrument piano)
        {
            if (piano == null || !piano.isActiveAndEnabled || piano.Rig == null || !piano.Rig.InScene) return null;
            if (piano.Platform != null && piano.Platform.Stowed) return null;
            Transform t = piano.transform;
            return new Pose(t.TransformPoint(Local.position), t.rotation * Local.rotation);
        }
    }
}
