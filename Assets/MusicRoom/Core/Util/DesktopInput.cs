// Desktop / editor keys and mouse (spec 08 section 6) behind one API, compiled for both input handlers
// (activeInputHandler = Both). Modules use this instead of #if blocks.
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MusicRoom.Core
{
    public enum DKey
    {
        A, W, S, E, D, F, T, G, Y, H, U, J, K, O, L, P, Semicolon, Quote, // piano semitones 0..17 above the base
        Z, X,          // base octave -12 / +12
        M,             // menu
        Escape, Space,
    }

    public static class DesktopInput
    {
        public static Vector2 MousePosition
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                // Fully qualified: inside this class the bare name Mouse resolves to the Mouse(int) method below.
                var m = UnityEngine.InputSystem.Mouse.current;
                return m != null ? m.position.ReadValue() : Vector2.zero;
#else
                return Input.mousePosition;
#endif
            }
        }

        public static bool Mouse(int button)
        {
#if ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.isPressed : button == 1 ? m.rightButton.isPressed : m.middleButton.isPressed;
#else
            return Input.GetMouseButton(button);
#endif
        }

        public static bool MouseDown(int button)
        {
#if ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.wasPressedThisFrame : button == 1 ? m.rightButton.wasPressedThisFrame : m.middleButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(button);
#endif
        }

        public static bool Shift
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var k = Keyboard.current; return k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
#else
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
            }
        }

        public static bool Alt
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var k = Keyboard.current; return k != null && (k.leftAltKey.isPressed || k.rightAltKey.isPressed);
#else
                return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
#endif
            }
        }

        /// <summary>Ctrl held (either side). With Ctrl or Meta held the desktop shortcuts are ignored.</summary>
        public static bool Ctrl
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var k = Keyboard.current; return k != null && (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed);
#else
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#endif
            }
        }

        /// <summary>Cmd (macOS) / Windows key held (either side).</summary>
        public static bool Meta
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var k = Keyboard.current; return k != null && (k.leftMetaKey.isPressed || k.rightMetaKey.isPressed);
#else
                return Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)
                    || Input.GetKey(KeyCode.LeftWindows) || Input.GetKey(KeyCode.RightWindows);
#endif
            }
        }

        /// <summary>Ctrl or Cmd (Meta) held: the key handlers return on Meta or Ctrl, so editor and
        /// system shortcuts (Cmd+S, Ctrl+P, Cmd+D ...) never play the piano row or Z / X, open the menu (M) or call Sunny
        /// (Shift+H).</summary>
        public static bool Command => Ctrl || Meta;

        public static bool Key(DKey key)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; return k != null && k[Map(key)].isPressed;
#else
            return Input.GetKey(MapLegacy(key));
#endif
        }

        public static bool KeyDown(DKey key)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; return k != null && k[Map(key)].wasPressedThisFrame;
#else
            return Input.GetKeyDown(MapLegacy(key));
#endif
        }

        public static bool KeyUp(DKey key)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; return k != null && k[Map(key)].wasReleasedThisFrame;
#else
            return Input.GetKeyUp(MapLegacy(key));
#endif
        }

        /// <summary>The piano row A W S E D F T G Y H U J K O L P ; ' = semitones 0..17.</summary>
        public static readonly DKey[] PianoRow = { DKey.A, DKey.W, DKey.S, DKey.E, DKey.D, DKey.F, DKey.T, DKey.G, DKey.Y, DKey.H, DKey.U, DKey.J, DKey.K, DKey.O, DKey.L, DKey.P, DKey.Semicolon, DKey.Quote };

#if ENABLE_INPUT_SYSTEM
        static UnityEngine.InputSystem.Key Map(DKey k)
        {
            switch (k)
            {
                case DKey.A: return UnityEngine.InputSystem.Key.A; case DKey.W: return UnityEngine.InputSystem.Key.W;
                case DKey.S: return UnityEngine.InputSystem.Key.S; case DKey.E: return UnityEngine.InputSystem.Key.E;
                case DKey.D: return UnityEngine.InputSystem.Key.D; case DKey.F: return UnityEngine.InputSystem.Key.F;
                case DKey.T: return UnityEngine.InputSystem.Key.T; case DKey.G: return UnityEngine.InputSystem.Key.G;
                case DKey.Y: return UnityEngine.InputSystem.Key.Y; case DKey.H: return UnityEngine.InputSystem.Key.H;
                case DKey.U: return UnityEngine.InputSystem.Key.U; case DKey.J: return UnityEngine.InputSystem.Key.J;
                case DKey.K: return UnityEngine.InputSystem.Key.K; case DKey.O: return UnityEngine.InputSystem.Key.O;
                case DKey.L: return UnityEngine.InputSystem.Key.L; case DKey.P: return UnityEngine.InputSystem.Key.P;
                case DKey.Semicolon: return UnityEngine.InputSystem.Key.Semicolon; case DKey.Quote: return UnityEngine.InputSystem.Key.Quote;
                case DKey.Z: return UnityEngine.InputSystem.Key.Z; case DKey.X: return UnityEngine.InputSystem.Key.X;
                case DKey.M: return UnityEngine.InputSystem.Key.M; case DKey.Escape: return UnityEngine.InputSystem.Key.Escape;
                default: return UnityEngine.InputSystem.Key.Space;
            }
        }
#else
        static KeyCode MapLegacy(DKey k)
        {
            switch (k)
            {
                case DKey.A: return KeyCode.A; case DKey.W: return KeyCode.W; case DKey.S: return KeyCode.S; case DKey.E: return KeyCode.E;
                case DKey.D: return KeyCode.D; case DKey.F: return KeyCode.F; case DKey.T: return KeyCode.T; case DKey.G: return KeyCode.G;
                case DKey.Y: return KeyCode.Y; case DKey.H: return KeyCode.H; case DKey.U: return KeyCode.U; case DKey.J: return KeyCode.J;
                case DKey.K: return KeyCode.K; case DKey.O: return KeyCode.O; case DKey.L: return KeyCode.L; case DKey.P: return KeyCode.P;
                case DKey.Semicolon: return KeyCode.Semicolon; case DKey.Quote: return KeyCode.Quote;
                case DKey.Z: return KeyCode.Z; case DKey.X: return KeyCode.X; case DKey.M: return KeyCode.M;
                case DKey.Escape: return KeyCode.Escape; default: return KeyCode.Space;
            }
        }
#endif
    }
}
