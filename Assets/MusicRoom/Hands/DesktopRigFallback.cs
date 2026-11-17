// Desktop / editor camera when no XR runtime is present (spec 01 section 6, spec 08 section 6): the desktop
// view at (0, 1.2, -0.1) looking at (0, 0.95, 0.45), fov 55; left-drag on empty space looks around, the wheel zooms.
// "Empty space": no look-drag when the press lands on a desktop click target (a collider on a
// layer in dragBlockMask: Core's Layers.PlaySurface, wired by AppRoot), when Shift or Alt is held (rig move / turn), or
// when dragBlocked() says so.
// OVRCameraRig never touches the anchors without XR, so this script owns the centre eye. It never runs while
// OVRManager is initialised (RigBootstrap keeps it disabled).
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MusicRoom.Hands
{
    public sealed class DesktopRigFallback : MonoBehaviour
    {
        public static readonly Vector3 EyePosition = new Vector3(0f, 1.2f, -0.1f);
        public static readonly Vector3 LookAt = new Vector3(0f, 0.95f, 0.45f);
        public const float DefaultFov = 55f;

        public float yaw, pitch, fov = DefaultFov;
        /// <summary>Layers whose colliders own a left-button press (the module's desktop click path): no look-drag then.</summary>
        public int dragBlockMask;
        /// <summary>Extra veto for starting a look-drag (optional).</summary>
        public System.Func<bool> dragBlocked;

        OVRCameraRig _rig;
        Camera _cam;
        Vector2 _lastMouse;
        bool _dragging;

        void OnEnable()
        {
            _rig = GetComponent<OVRCameraRig>();
            if (_rig == null || _rig.centerEyeAnchor == null) return;
            Transform ce = _rig.centerEyeAnchor;
            ce.localPosition = EyePosition;
            Quaternion look = Quaternion.LookRotation(LookAt - EyePosition);
            Vector3 e = look.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
            ce.localRotation = look;
            _cam = ce.GetComponent<Camera>();
            if (_cam != null) _cam.fieldOfView = fov;
        }

        void Update()
        {
            if (_rig == null || _rig.centerEyeAnchor == null || OVRManager.OVRManagerinitialized) return;
            ReadMouse(out Vector2 mouse, out bool lmb, out bool lmbDown, out bool modifier, out float wheel);
            if (lmbDown) { _dragging = !DragBlocked(mouse, modifier); _lastMouse = mouse; }
            if (!lmb) _dragging = false;
            if (_dragging)
            {
                Vector2 d = mouse - _lastMouse;
                _lastMouse = mouse;
                float k = 0.0028f * fov / 55f * Mathf.Rad2Deg; // 0.0028 rad per px at fov 55, converted to degrees
                yaw += d.x * k;
                pitch = Mathf.Clamp(pitch + d.y * k, -1.45f * Mathf.Rad2Deg, 1.45f * Mathf.Rad2Deg);
            }
            if (wheel != 0f) fov = Mathf.Clamp(fov + 0.04f * wheel, 26f, 90f);
            Transform ce = _rig.centerEyeAnchor;
            ce.localRotation = Quaternion.Euler(pitch, yaw, 0f);
            if (_cam != null) _cam.fieldOfView = fov;
        }

        /// <summary>Whether a left-button press at this screen point must not start a look-drag.</summary>
        public bool DragBlocked(Vector2 mouse, bool modifier)
        {
            if (modifier) return true;                                   // Shift / Alt + drag move or turn a rig (Layout)
            if (dragBlocked != null && dragBlocked()) return true;
            if (dragBlockMask == 0 || _cam == null) return false;
            Ray ray = _cam.ScreenPointToRay(new Vector3(mouse.x, mouse.y, 0f));
            return Physics.Raycast(ray, out _, _cam.farClipPlane, dragBlockMask, QueryTriggerInteraction.Collide);
        }

        static void ReadMouse(out Vector2 mouse, out bool lmb, out bool lmbDown, out bool modifier, out float wheel)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse m = Mouse.current;
            Keyboard k = Keyboard.current;
            mouse = m != null ? m.position.ReadValue() : Vector2.zero;
            lmb = m != null && m.leftButton.isPressed;
            lmbDown = m != null && m.leftButton.wasPressedThisFrame;
            modifier = k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed || k.leftAltKey.isPressed || k.rightAltKey.isPressed);
            wheel = m != null ? -m.scroll.ReadValue().y / 120f * 100f : 0f; // wheel delta: sign flipped, 120 per notch -> 100 (approx.)
#else
            mouse = Input.mousePosition;
            lmb = Input.GetMouseButton(0);
            lmbDown = Input.GetMouseButtonDown(0);
            modifier = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            wheel = -Input.mouseScrollDelta.y * 100f;
#endif
        }
    }
}
