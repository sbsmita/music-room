// Editor / desktop stand-in for hand tracking: one synthetic hand in a pointing pose whose index fingertip rides
// the mouse ray. LMB pokes (the tip pushes 2 cm past the surface in 50 ms), RMB pinches (thumb tip onto index tip),
// MMB closes the fist, Tab switches the hand it drives (right / left), '-' / '=' change the hover distance when nothing
// is hit (the wheel is the desktop camera zoom, spec 08 section 6). The other hand reports untracked. No letter keys
// and no Shift: those belong to the computer-keyboard piano (DesktopInput.PianoRow), Shift/Alt-drag and Shift+H.
// Desktop model (ARCHITECTURE section 0): clicks on play surfaces belong to the modules' desktop paths (the mouse
// pointer); the mouse hand is a visual and gesture stand-in whose pointers never strike (HandState.IsMouse).
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MusicRoom.Hands
{
    public sealed class MouseHandSource : IHandSource
    {
        public float hoverDistance = 0.45f;
        public float pokeDepth = 0.02f;
        public float pokeSeconds = 0.05f;
        public float restAboveSurface = 0.005f;
        public float maxRayDistance = 3f;
        public LayerMask rayMask = ~0;

        /// <summary>True when the last mouse ray hit a collider (the desktop camera only looks around when it did not).</summary>
        public bool HitSurface { get; private set; }
        public Ray LastRay { get; private set; }

        readonly System.Func<Camera> _camera;
        readonly HandFrame[] _frames = { new HandFrame(HandSide.Left), new HandFrame(HandSide.Right) };
        readonly SampleVelocity[] _velocity = { new SampleVelocity(), new SampleVelocity() };
        readonly Vector3[] _pose = new Vector3[HandFrame.JointCount];
        readonly Vector3[] _tips = new Vector3[5];
        readonly Vector3[] _vel = new Vector3[5];
        float _poke;
        bool _left;

        /// <summary>The hand the mouse drives (Tab toggles).</summary>
        public HandSide Side => _left ? HandSide.Left : HandSide.Right;

        public MouseHandSource(System.Func<Camera> camera) { _camera = camera; }

        public string Name => "Mouse";
        public bool IsAvailable => true;
        public HandFrame Get(HandSide side) => _frames[(int)side];

        public void Tick(float deltaTime)
        {
            ReadDevices(out Vector2 mouse, out bool poke, out bool pinch, out bool fist, out bool toggleHand, out float hover);
            if (toggleHand) _left = !_left;
            int a = _left ? 0 : 1;
            _frames[1 - a].MarkLost();
            _velocity[1 - a].Reset();
            HandFrame f = _frames[a];
            Camera cam = _camera != null ? _camera() : null;
            if (cam == null)
            {
                f.MarkLost();
                _velocity[a].Reset();
                return;
            }

            if (hover != 0f) hoverDistance = Mathf.Clamp(hoverDistance + hover * 0.02f, 0.15f, 1.5f);
            Ray ray = cam.ScreenPointToRay(new Vector3(mouse.x, mouse.y, 0f));
            LastRay = ray;
            HitSurface = Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, rayMask, QueryTriggerInteraction.Ignore);
            float distance = HitSurface ? hit.distance - restAboveSurface : hoverDistance;
            _poke = Mathf.MoveTowards(_poke, poke ? 1f : 0f, deltaTime / Mathf.Max(pokeSeconds, 1e-3f));
            Vector3 tip = ray.GetPoint(distance + _poke * (pokeDepth + restAboveSurface));

            // Fingers along the ray, back of the hand toward the camera's up.
            Vector3 up = Vector3.ProjectOnPlane(cam.transform.up, ray.direction);
            Quaternion wristRot = Quaternion.LookRotation(ray.direction, up.sqrMagnitude > 1e-8f ? up : Vector3.up);

            HandRestPose.Fill((HandSide)a, _pose);
            HandRestPose.Curl(_pose, Finger.Middle, fist ? HandRestPose.FistCurl : HandRestPose.PointCurl);
            HandRestPose.Curl(_pose, Finger.Ring, fist ? HandRestPose.FistCurl : HandRestPose.PointCurl);
            HandRestPose.Curl(_pose, Finger.Little, fist ? HandRestPose.FistCurl : HandRestPose.PointCurl);
            if (fist) HandRestPose.Curl(_pose, Finger.Index, HandRestPose.FistCurl);
            if (pinch) _pose[(int)HandJoint.ThumbTip] = _pose[(int)HandJoint.IndexTip];

            Vector3 wrist = tip - wristRot * _pose[(int)HandJoint.IndexTip];
            for (int i = 0; i < HandFrame.JointCount; i++)
            {
                f.position[i] = wrist + wristRot * _pose[i];
                f.rotation[i] = wristRot;
                f.radius[i] = HandRestPose.Radius[i];
            }

            f.isTracked = true;
            f.isHighConfidence = true;
            f.isDominant = a == 1;
            f.systemGesture = false;
            f.poseInferred = false;
            f.handScale = 1f;
            for (int k = 0; k < 5; k++)
            {
                f.fingerHighConfidence[k] = true;
                f.runtimePinch[k] = pinch && k == (int)Finger.Index;
                f.pinchStrength[k] = f.runtimePinch[k] ? 1f : 0f;
            }
            f.pointerValid = true;
            f.pointer = new Pose(ray.origin, Quaternion.LookRotation(ray.direction, wristRot * Vector3.up));

            double t = Time.unscaledTimeAsDouble;
            f.sampleTime = f.displayTime = t;
            for (int k = 0; k < 5; k++) _tips[k] = f.position[HandFrame.TipJoint[k]];
            f.newSample = _velocity[a].Push(t, _tips, _vel, ref f.sampleInterval);
            for (int k = 0; k < 5; k++) f.tipVelocity[k] = _vel[k];
        }

        static void ReadDevices(out Vector2 mouse, out bool lmb, out bool rmb, out bool fist, out bool toggleHand, out float hover)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse m = Mouse.current;
            Keyboard k = Keyboard.current;
            mouse = m != null ? m.position.ReadValue() : Vector2.zero;
            lmb = m != null && m.leftButton.isPressed;
            rmb = m != null && m.rightButton.isPressed;
            fist = m != null && m.middleButton.isPressed;
            toggleHand = k != null && k.tabKey.wasPressedThisFrame;
            hover = k == null ? 0f : (k.equalsKey.wasPressedThisFrame ? 1f : k.minusKey.wasPressedThisFrame ? -1f : 0f);
#else
            mouse = Input.mousePosition;
            lmb = Input.GetMouseButton(0);
            rmb = Input.GetMouseButton(1);
            fist = Input.GetMouseButton(2);
            toggleHand = Input.GetKeyDown(KeyCode.Tab);
            hover = Input.GetKeyDown(KeyCode.Equals) ? 1f : Input.GetKeyDown(KeyCode.Minus) ? -1f : 0f;
#endif
        }
    }
}
