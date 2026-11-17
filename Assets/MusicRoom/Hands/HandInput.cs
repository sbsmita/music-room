// Owns the active IHandSource, ticks it before any game logic, keeps the derived pinch / grasp state with
// hysteresis (pinch 15/25 mm, grasp 0.105/0.13 m), and builds the Meta hand visuals once the headset source is live.
// Tests install a FakeHandSource through OverrideSource before the component awakes. 10-upgrade/experience.md E6: the
// hands' Look (Skin; PassthroughCutout in mixed reality when the player chose "Real hands": SetLook), E1: the editor's debug hands follow the visual contact
// clamp too.
using UnityEngine;

namespace MusicRoom.Hands
{
    [DefaultExecutionOrder(-100)]
    public sealed class HandInput : MonoBehaviour
    {
        public static HandInput Instance { get; private set; }

        /// <summary>Set before the scene (or AppRoot) creates HandInput to force a source (tests: FakeHandSource).</summary>
        public static IHandSource OverrideSource;

        [Tooltip("Transparent hand material with an _Alpha float (see HandGhost.shader): the Ghost look.")]
        public Material handMaterial;
        [Tooltip("E6: the lit hand of the VR room (MusicRoom/HandSkin, template MaterialTemplates/HandSkin).")]
        public Material skinMaterial;
        [Tooltip("E6: the MR passthrough cut-out (MusicRoom/HandPassthrough, template MaterialTemplates/HandPassthrough).")]
        public Material cutoutMaterial;
        public bool forceMouse;
        [Tooltip("Draw debug spheres at the joints when the Meta mesh is unavailable (editor, tests). Always off in players.")]
        public bool debugJoints = true;
        /// <summary>BootOptions.ManualFrames: Update does nothing; App.Step calls Step(dt).</summary>
        [System.NonSerialized] public bool ManualFrames;

        public IHandSource Source { get; private set; }
        public MetaHandSource Meta { get; private set; }
        public MouseHandSource Mouse { get; private set; }
        /// <summary>The device player's source until the headset runtime is up: both hands untracked (never the mouse).</summary>
        public IHandSource Untracked { get; private set; }
        public HandFrame Left => Source.Get(HandSide.Left);
        public HandFrame Right => Source.Get(HandSide.Right);
        public HandFrame Get(HandSide side) => Source.Get(side);
        /// <summary>True when the frames come from the headset (real hands), false for mouse / scripted hands.</summary>
        public bool IsHeadset => Source == Meta;
        /// <summary>E6: how the hands are drawn (Skin; PassthroughCutout in mixed reality with "Real hands"). Visuals built later
        /// start with it.</summary>
        public MetaHandVisual.Look Look { get; private set; } = MetaHandVisual.Look.Skin;

        // Edge flags are valid for the frame they are raised in (cleared on the next Update).
        public readonly bool[] pinching = new bool[2], pinchStart = new bool[2], pinchEnd = new bool[2];
        public readonly bool[] grasping = new bool[2], graspStart = new bool[2], graspEnd = new bool[2];
        /// <summary>Meta's index pinch with hysteresis (HandMath.NextMetaPinch: strength 0.85 on, below 0.5 off, or the
        /// runtime's IsIndexPinching flag).</summary>
        public readonly bool[] metaPinching = new bool[2];
        /// <summary>The grab pinch: the thumb-index rule OR Meta's pinch, with its own edges. Held like the others while
        /// the hand is untracked or doing the system gesture, so a system-menu pinch never starts one.</summary>
        public readonly bool[] grabPinching = new bool[2], grabPinchStart = new bool[2], grabPinchEnd = new bool[2];
        readonly float[] _metaInvalidFor = new float[2];

        OVRCameraRig _rig;
        readonly MetaHandVisual[] _visuals = new MetaHandVisual[2];
        DebugHandVisual _debug;

        void Awake()
        {
            Instance = this;
            // Players never build the 52 debug spheres (and never use the mouse source): the Meta mesh is the hand there.
            if (!Application.isEditor) debugJoints = false;
            _rig = FindAnyObjectByType<OVRCameraRig>();
            Meta = new MetaHandSource(() => _rig != null ? _rig.trackingSpace : null);
            Mouse = new MouseHandSource(() => Camera.main);
            Untracked = new UntrackedHandSource();
            Source = Want();
        }

        /// <summary>Override (tests) > Meta when the runtime is up > the mouse in the editor > untracked in a player.</summary>
        IHandSource Want()
        {
            if (OverrideSource != null) return OverrideSource;
            if (!forceMouse && Meta.IsAvailable) return Meta;
            return Application.isEditor || forceMouse ? (IHandSource)Mouse : Untracked;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (ManualFrames) return;
            Step(Time.deltaTime);
        }

        /// <summary>Tick the source by dt and derive the pinch / grasp state and edges (Update, or App.Step in tests).</summary>
        public void Step(float dt)
        {
            if (_rig == null) _rig = FindAnyObjectByType<OVRCameraRig>();
            IHandSource want = Want();
            if (want != Source)
            {
                Source = want;
                for (int h = 0; h < 2; h++) pinching[h] = grasping[h] = metaPinching[h] = grabPinching[h] = false;
            }
            Source.Tick(dt);

            if (Source == Meta && _visuals[0] == null && handMaterial != null && _rig != null && _rig.trackingSpace != null)
                for (int h = 0; h < 2; h++)
                    _visuals[h] = MetaHandVisual.Create((HandSide)h, Meta, _rig.trackingSpace, handMaterial, skinMaterial, cutoutMaterial, Look);
            if (Source != Meta && debugJoints)
            {
                if (_debug == null) _debug = DebugHandVisual.Create(transform, skinMaterial);
                _debug.Draw(Source, dt);
            }
            else if (_debug != null) _debug.Hide();

            for (int h = 0; h < 2; h++) Derive(h, Source.Get((HandSide)h), dt);
        }

        void Derive(int h, HandFrame f, float dt)
        {
            pinchStart[h] = pinchEnd[h] = graspStart[h] = graspEnd[h] = false;
            grabPinchStart[h] = grabPinchEnd[h] = false;
            f.lostSeconds = f.isTracked ? 0f : f.lostSeconds + dt;
            // Untracked or system gesture: hold the last state; consumers apply their own grace (e.g. sticks).
            if (!f.isTracked || f.systemGesture) return;
            bool p = HandMath.NextPinch(pinching[h], HandMath.PinchGap(f));
            bool g = HandMath.NextGrasp(grasping[h], HandMath.GraspAmount(f));
            pinchStart[h] = p && !pinching[h];
            pinchEnd[h] = !p && pinching[h];
            graspStart[h] = g && !grasping[h];
            graspEnd[h] = !g && grasping[h];
            pinching[h] = p;
            grasping[h] = g;
            // Meta's index pinch. Its data is only usable with InputStateValid (HandFrame.pointerValid); a short gap is
            // held so a flicker never drops an instrument mid-move, a long one ends it.
            bool m = metaPinching[h];
            if (f.pointerValid)
            {
                _metaInvalidFor[h] = 0f;
                m = HandMath.NextMetaPinch(m, f.pinchStrength[(int)Finger.Index], f.runtimePinch[(int)Finger.Index]);
            }
            else if ((_metaInvalidFor[h] += dt) > HandMath.MetaPinchHoldSeconds) m = false;
            metaPinching[h] = m;
            bool grab = p || m;
            grabPinchStart[h] = grab && !grabPinching[h];
            grabPinchEnd[h] = !grab && grabPinching[h];
            grabPinching[h] = grab;
        }

        public MetaHandVisual Visual(HandSide side) => _visuals[(int)side];
        public DebugHandVisual DebugHands => _debug;

        /// <summary>E6: switch both hands' look and material together (Core's ContactVisuals calls it: Skin, or the cut-out in
        /// mixed reality when the player chose "Real hands").</summary>
        public void SetLook(MetaHandVisual.Look look)
        {
            Look = look;
            for (int h = 0; h < 2; h++) if (_visuals[h] != null) _visuals[h].SetLook(look);
        }
    }

    /// <summary>Both hands always untracked: the device player's source before the headset runtime is up, so no
    /// synthetic hand ever plays on the headset.</summary>
    public sealed class UntrackedHandSource : IHandSource
    {
        readonly HandFrame[] _frames = { new HandFrame(HandSide.Left), new HandFrame(HandSide.Right) };
        public string Name => "None";
        public bool IsAvailable => true;
        public HandFrame Get(HandSide side) => _frames[(int)side];
        public void Tick(float deltaTime) { _frames[0].MarkLost(); _frames[1].MarkLost(); }
    }

    /// <summary>Spheres at each joint, and a capsule-like cylinder along each bone, for sources without a runtime mesh (mouse
    /// hand, scripted hands; editor only). 2 x (26 + 24) renderers, MaterialPropertyBlock-free: one shared material (the
    /// lit skin template when there is one), visibility toggled per hand. Like MetaHandVisual it follows the E1 contact
    /// clamp: Draw places the joints from the source (HandInput.Step), LateUpdate re-places them with this frame's contacts.</summary>
    public sealed class DebugHandVisual : MonoBehaviour
    {
        public const int BoneCount = HandFrame.JointCount - 2;   // every joint after the palm and the wrist has a parent bone
        readonly Transform[][] _joints = new Transform[2][];
        readonly Transform[][] _bones = new Transform[2][];
        readonly float[][] _lift = { new float[5], new float[5] };
        readonly Vector3[][] _normal = { new Vector3[5], new Vector3[5] };
        // as MetaHandVisual: each finger's drawn turn, eased in and out (ContactMath.SmoothTheta) on the app's own frame time
        readonly float[][] _theta = { new float[5], new float[5] };
        float _dt;
        readonly Vector3[] _pos = new Vector3[HandFrame.JointCount];
        IHandSource _source;
        public int VisibleHands { get; private set; }

        /// <summary>The joint a bone hangs from: the wrist for the metacarpals, else the previous joint of the finger.</summary>
        public static int ParentOf(int joint) =>
            joint == (int)HandJoint.ThumbMetacarpal || joint == (int)HandJoint.IndexMetacarpal || joint == (int)HandJoint.MiddleMetacarpal
            || joint == (int)HandJoint.RingMetacarpal || joint == (int)HandJoint.LittleMetacarpal ? (int)HandJoint.Wrist : joint - 1;

        public static DebugHandVisual Create(Transform parent) => Create(parent, null);

        public static DebugHandVisual Create(Transform parent, Material skin)
        {
            var go = new GameObject("DebugHands");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<DebugHandVisual>();
            Material mat = skin;
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.86f, 0.78f, 0.70f, 1f));
            }
            for (int h = 0; h < 2; h++)
            {
                v._joints[h] = new Transform[HandFrame.JointCount];
                v._bones[h] = new Transform[BoneCount];
                var hand = new GameObject(h == 0 ? "Left" : "Right").transform;
                hand.SetParent(go.transform, false);
                for (int i = 0; i < HandFrame.JointCount; i++)
                    v._joints[h][i] = Primitive(PrimitiveType.Sphere, ((HandJoint)i).ToString(), hand, mat);
                for (int b = 0; b < BoneCount; b++)
                    v._bones[h][b] = Primitive(PrimitiveType.Cylinder, "Bone" + (HandJoint)(b + 2), hand, mat);
            }
            return v;
        }

        static Transform Primitive(PrimitiveType type, string name, Transform parent, Material mat)
        {
            var s = GameObject.CreatePrimitive(type);
            Object.Destroy(s.GetComponent<Collider>()); // never a mouse-ray target
            s.name = name;
            s.transform.SetParent(parent, false);
            var r = s.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return s.transform;
        }

        /// <summary>E1: lift a finger of one hand this frame (ContactVisuals, like MetaHandVisual.SetContact).</summary>
        public void SetContact(HandSide side, Finger finger, float lift, Vector3 worldNormal)
        {
            _lift[(int)side][(int)finger] = lift > 0f ? lift : 0f;
            _normal[(int)side][(int)finger] = worldNormal;
        }

        public void ClearContacts()
        {
            for (int h = 0; h < 2; h++) for (int k = 0; k < 5; k++) _lift[h][k] = 0f;
        }

        /// <summary>The drawn position of a joint (after the contact clamp of the last LateUpdate / Draw).</summary>
        public Vector3 JointPosition(HandSide side, HandJoint joint) => _joints[(int)side][(int)joint].position;

        public void Draw(IHandSource source) => Draw(source, 0f);

        /// <summary>Place the joints from the source; <paramref name="dt"/> is the app frame's time (HandInput.Step), which
        /// the next LateUpdate eases the finger turns by (so manual-frame tests see the same easing as the headset).</summary>
        public void Draw(IHandSource source, float dt)
        {
            _source = source;
            _dt += dt;
            Place(false);
        }

        /// <summary>The drawn turn of a finger about its knuckle as of the last LateUpdate, radians.</summary>
        public float ContactTheta(HandSide side, Finger finger) => _theta[(int)side][(int)finger];

        void LateUpdate()
        {
            if (_source != null) Place(true);
            _dt = 0f;
        }

        void Place(bool contacts)
        {
            VisibleHands = 0;
            for (int h = 0; h < 2; h++)
            {
                HandFrame f = _source.Get((HandSide)h);
                Transform hand = _joints[h][0].parent;
                if (hand.gameObject.activeSelf != f.isTracked) hand.gameObject.SetActive(f.isTracked);
                if (!f.isTracked) { if (contacts) for (int k = 0; k < 5; k++) _theta[h][k] = 0f; continue; }
                VisibleHands++;
                for (int i = 0; i < HandFrame.JointCount; i++) _pos[i] = f.position[i];
                if (contacts)
                    for (int k = 0; k < 5; k++)
                    {
                        float lift = _lift[h][k];
                        if (lift <= 0f && _theta[h][k] <= 0f) continue;
                        int pivot = ContactMath.PivotJoint[k], tip = HandFrame.TipJoint[k];
                        Vector3 axis = ContactMath.LiftAxis(_pos[tip] - _pos[pivot], _normal[h][k]);
                        float want = 0f;
                        if (lift > 0f && ContactMath.FingerRotation(_pos[pivot], _pos[tip], _normal[h][k], lift, ContactMath.DrawnThetaMax, out float theta, out Vector3 a))
                        {
                            want = theta;
                            axis = a;
                        }
                        _theta[h][k] = ContactMath.SmoothTheta(_theta[h][k], want, _dt);
                        if (_theta[h][k] <= 0f || axis == Vector3.zero) continue;
                        for (int j = pivot + 1; j <= tip; j++) _pos[j] = ContactMath.RotateAbout(_pos[j], _pos[pivot], axis, _theta[h][k]);
                    }
                for (int i = 0; i < HandFrame.JointCount; i++)
                {
                    Transform t = _joints[h][i];
                    t.position = _pos[i];
                    // The palm and wrist capsules are fat (23 / 18 mm): as spheres they would read as a ball, not a hand.
                    float r = Mathf.Max(f.radius[i], 0.004f) * (i == (int)HandJoint.Palm ? 0.55f : i == (int)HandJoint.Wrist ? 0.75f : 1f);
                    t.localScale = new Vector3(r, r, r) * 2f;
                }
                for (int b = 0; b < BoneCount; b++)
                {
                    int j = b + 2, p = ParentOf(j);
                    Vector3 a = _pos[p], c = _pos[j], d = c - a;
                    float len = d.magnitude;
                    Transform t = _bones[h][b];
                    float r = Mathf.Max(0.9f * Mathf.Min(f.radius[j], f.radius[p] > 0f ? f.radius[p] : f.radius[j]), 0.003f);
                    t.position = (a + c) * 0.5f;
                    t.rotation = len > 1e-6f ? Quaternion.FromToRotation(Vector3.up, d / len) : Quaternion.identity;
                    t.localScale = new Vector3(2f * r, 0.5f * len, 2f * r);   // the primitive cylinder is 2 m tall, 1 m wide
                }
            }
        }

        public void Hide()
        {
            VisibleHands = 0;
            _source = null;
            for (int h = 0; h < 2; h++) _joints[h][0].parent.gameObject.SetActive(false);
        }
    }
}
