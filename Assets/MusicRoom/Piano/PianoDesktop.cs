// Desktop / editor play (desktopDown/Move/Up; spec 08 section 6): a click on a key
// presses it at 0.7, dragging across the keys plays each in turn at 0.6 (a glissando), letting go releases it; the
// A W S E D F T G Y H U J K O L P ; ' row plays semitones 0..17 above a base of 60 at 0.75, one presser per physical key
// so chords hold, and Z / X move the base an octave within 24..96 (a held key releases the note it started); with Ctrl or
// Cmd held no row key goes down (the keydown guard: shortcuts never play). Shift/Alt clicks belong to the rig drag (Layout).
// One desktop model (ARCHITECTURE section 0): a press belongs to the module whose Layers.PlaySurface collider is the
// nearest hit (the nearest of every instrument's hit colliders wins), so the piano acts only when that
// hit is its own KeysClickTarget box; the exact key is then picked analytically from the 88 resting key boxes (cheaper
// than 88 colliders and identical for a key at rest). A drag that started on a key follows the keys alone (a
// drag raycasts only the active instrument). DesktopRigFallback.dragBlockMask = PlaySurfaceMask keeps the camera
// from looking around on that press, so no drag-block hook is needed.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Piano
{
    public sealed class PianoDesktop
    {
        readonly PianoAction _action;
        readonly Collider _target;
        readonly object _mouse = new MousePresser();
        readonly object[] _keyPressers = new object[DesktopInput.PianoRow.Length];
        readonly int[] _keyMidi = new int[DesktopInput.PianoRow.Length];
        int _base = PianoSpec.KeyboardBase;
        bool _mouseDown;

        sealed class MousePresser { public override string ToString() => "mouse"; }
        sealed class KeyPresser { public readonly DKey Key; public KeyPresser(DKey k) { Key = k; } public override string ToString() => "kbd-" + Key; }

        /// <param name="target">The piano's PlaySurface click target (null: no mouse play, e.g. logic-only tests).</param>
        public PianoDesktop(PianoAction action, Collider target = null)
        {
            _action = action;
            _target = target;
            for (int i = 0; i < _keyPressers.Length; i++) { _keyPressers[i] = new KeyPresser(DesktopInput.PianoRow[i]); _keyMidi[i] = -1; }
        }

        public object MousePresserObject => _mouse;
        public object KeyPresserObject(int row) => _keyPressers[row];
        public int KeyboardBase => _base;
        /// <summary>True while the mouse holds a key.</summary>
        public bool MouseOnKey => _mouseDown;

        /// <summary>One frame of desktop input. camera null = no mouse play (headless).</summary>
        public void Tick(Transform piano, Camera camera)
        {
            KeyboardFrame(s_keyDown, s_keyHeld, CommandHeld);
            Mouse(piano, camera);
        }

        static readonly System.Func<DKey, bool> s_keyDown = DesktopInput.KeyDown, s_keyHeld = DesktopInput.Key;   // cached once: no per-frame delegate

        /// <summary>Ctrl or Cmd is held: the keydown guard returns at once, so editor and system
        /// shortcuts on the row (Cmd+S, Ctrl+P, Cmd+D ...) never strike a key (Core's DesktopInput.Command).</summary>
        public static bool CommandHeld => DesktopInput.Command;

        /// <summary>One frame of the computer-keyboard row (keydown / keyup): Z / X move the octave, a row key that went
        /// down presses its note, one that is up again releases it. With Ctrl / Cmd held nothing new goes down (the
        /// keydown guard); key-ups are never filtered, so a note started before the modifier still ends. Tests pass scripted
        /// key states.</summary>
        public void KeyboardFrame(System.Func<DKey, bool> wentDown, System.Func<DKey, bool> isHeld, bool command)
        {
            if (!command)
            {
                if (wentDown(DKey.Z)) OctaveDown();
                else if (wentDown(DKey.X)) OctaveUp();
            }
            var row = DesktopInput.PianoRow;
            for (int i = 0; i < row.Length; i++)
            {
                if (!command && wentDown(row[i])) KeyDown(i);
                else if (_keyMidi[i] >= 0 && !isHeld(row[i])) KeyUp(i);
            }
        }

        /// <summary>A physical key of the piano row went down (tests call this directly).</summary>
        public void KeyDown(int i)
        {
            if (_keyMidi[i] >= 0) KeyUp(i);
            int midi = _base + i;
            _keyMidi[i] = midi;
            _action.PressMidi(midi, _keyPressers[i], PianoSpec.KeyboardVelocity);
        }

        public void KeyUp(int i)
        {
            if (_keyMidi[i] < 0) return;
            _action.ReleaseMidi(_keyMidi[i], _keyPressers[i]);
            _keyMidi[i] = -1;
        }

        public void OctaveDown() => _base = Mathf.Max(PianoSpec.KeyboardBaseMin, _base - 12);
        public void OctaveUp() => _base = Mathf.Min(PianoSpec.KeyboardBaseMax, _base + 12);

        void Mouse(Transform piano, Camera camera)
        {
            bool held = DesktopInput.Mouse(0);
            if (_mouseDown && !held)
            {
                _mouseDown = false;
                _action.DesktopUp(_mouse);
                return;
            }
            if (camera == null) return;
            if (!_mouseDown && !DesktopInput.MouseDown(0)) return;
            if (!_mouseDown && (DesktopInput.Shift || DesktopInput.Alt)) return;   // Shift/Alt drags move or turn a rig
            Ray ray = camera.ScreenPointToRay(DesktopInput.MousePosition);
            if (!_mouseDown)
            {
                if (!OwnsPress(ray, camera.farClipPlane)) return;
                PianoKey key = Raycast(piano, ray, out _);
                if (key == null) return;
                _mouseDown = true;
                _action.DesktopDown(key, _mouse);
            }
            else
            {
                PianoKey key = Raycast(piano, ray, out _);
                if (key != null) _action.DesktopMove(key, _mouse);
            }
        }

        /// <summary>True when the nearest PlaySurface collider on the ray is the piano's own click target (a drum, the menu
        /// face or a switch in front takes the press instead).</summary>
        public bool OwnsPress(Ray ray, float far)
        {
            if (_target == null || !_target.enabled || !_target.gameObject.activeInHierarchy) return false;
            return Physics.Raycast(ray, out RaycastHit hit, far, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide) && hit.collider == _target;
        }

        /// <summary>The resting keys' box in the piano frame: x across all 52 white keys, y from the white bottoms (-H) to the
        /// black tops (BTOP), z from the front edge (0) to the hinge (L).</summary>
        public static void KeysBox(out Vector3 center, out Vector3 size)
        {
            float halfX = PianoSpec.WhiteCount * PianoSpec.W / 2f;
            float y0 = -PianoSpec.H, y1 = PianoSpec.BTOP;
            center = new Vector3(0f, (y0 + y1) / 2f, PianoSpec.L / 2f);
            size = new Vector3(2f * halfX, y1 - y0, PianoSpec.L);
        }

        /// <summary>The nearest key a world ray hits (each key's resting box in the piano frame), and the world distance.</summary>
        public PianoKey Raycast(Transform piano, Ray ray, out float distance)
        {
            distance = float.PositiveInfinity;
            if (piano == null) return null;
            // Points, not InverseTransformDirection (which ignores scale): the ray parameter t stays in world units.
            Vector3 o = piano.InverseTransformPoint(ray.origin);
            Vector3 d = piano.InverseTransformPoint(ray.origin + ray.direction) - o;
            PianoKey best = null;
            float bestT = float.PositiveInfinity;
            var keys = _action.Keys;
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                Vector3 min, max;
                if (k.Black)
                {
                    min = new Vector3(k.X - PianoSpec.BW / 2f, PianoSpec.BTOP - PianoSpec.BlackH, PianoSpec.L - PianoSpec.BL);
                    max = new Vector3(k.X + PianoSpec.BW / 2f, PianoSpec.BTOP, PianoSpec.L);
                }
                else
                {
                    min = new Vector3(k.X - PianoSpec.WW / 2f, -PianoSpec.H, 0f);
                    max = new Vector3(k.X + PianoSpec.WW / 2f, 0f, PianoSpec.L);
                }
                if (RayBox(o, d, min, max, out float t) && t < bestT) { bestT = t; best = k; }
            }
            distance = bestT;
            return best;
        }

        static bool RayBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max, out float t)
        {
            float t0 = 0f, t1 = float.PositiveInfinity;
            t = 0f;
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(d[a]) < 1e-9f)
                {
                    if (o[a] < min[a] || o[a] > max[a]) return false;
                    continue;
                }
                float inv = 1f / d[a];
                float ta = (min[a] - o[a]) * inv, tb = (max[a] - o[a]) * inv;
                if (ta > tb) { float s = ta; ta = tb; tb = s; }
                t0 = Mathf.Max(t0, ta);
                t1 = Mathf.Min(t1, tb);
                if (t0 > t1) return false;
            }
            t = t0;
            return true;
        }

        /// <summary>Lets go of everything the desktop holds (the piano left the room).</summary>
        public void ReleaseAll()
        {
            _mouseDown = false;
            for (int i = 0; i < _keyMidi.Length; i++) _keyMidi[i] = -1;
        }
    }
}
