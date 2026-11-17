// Renders the runtime's own hand mesh for one hand (OVRMesh + OVRSkeleton + OVRMeshRenderer), posed from the
// exact sample MetaHandSource read, with a confidence-aware fade. This component replaces OVRHand as the data
// provider of those three Meta components: OVRHand.HandType is internal (OVRHand.cs:81-82) and cannot be set from
// app code, while the three provider interfaces are public. Wiring verified by spec 02's PlayMode tests.
// 10-upgrade/experience.md: E1 (SetContact: the drawn finger turns about its MCP joint so its pad rests on a key, a drum
// head or the menu instead of sinking into it; applied in LateUpdate after OVRSkeleton posed the bones, never touching
// MetaHandSource's arrays) and E6 (Look: Skin, the lit drawn hand, also the default in "Play in my room"; PassthroughCutout
// when the player chose "Real hands" there, a hole in the eye buffer posed at displayTime - cutoutLatency so it sits over the real hand the passthrough shows ~39 ms late; Ghost stays an option).
using UnityEngine;

namespace MusicRoom.Hands
{
    [DefaultExecutionOrder(-80)] // after HandInput (-100) sampled the hands, before OVRSkeleton / OVRMeshRenderer (0)
    public sealed class MetaHandVisual : MonoBehaviour,
        OVRSkeleton.IOVRSkeletonDataProvider,
        OVRMesh.IOVRMeshDataProvider,
        OVRMeshRenderer.IOVRMeshRendererDataProvider
    {
        /// <summary>E6: how the hand is drawn. Skin (default, in "Play here" and "Play in my room"): lit by the room's light;
        /// PassthroughCutout (mixed reality with "Real hands" chosen): the real hand shows through a hole; Ghost: the
        /// unlit translucent hand.</summary>
        public enum Look { Ghost, Skin, PassthroughCutout }

        public static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        const int DelayedGiveUpFrames = 300; // stop asking for past poses if the runtime never gives them

        public HandSide side;
        public MetaHandSource source;
        public float fadeInSeconds = 0.08f;
        public float fadeOutSeconds = 0.25f;
        public float lowConfidenceAlpha = 0.4f;
        public float appAlpha = 1f; // app-controlled multiplier, e.g. lowered while the hand holds a drumstick
        public float Alpha { get; private set; }
        /// <summary>E6: Skin, or PassthroughCutout in mixed reality with "Real hands". Change it with SetLook (material and look
        /// together).</summary>
        public Look look = Look.Skin;
        /// <summary>E6: the cut-out is posed this many seconds before the display time (Quest 3 passthrough latency, about
        /// 39 ms; device-tune 0.02-0.06). The play logic keeps the predicted pose, so no sound is delayed.</summary>
        public float cutoutLatency = 0.039f;
        /// <summary>True when this frame's cut-out pose came from GetHandStateAtTime (false: the current pose).</summary>
        public bool UsingDelayedPose => _delayedValid;

        /// <summary>E6: joint j (HandJoint) in world space as the MR cut-out draws it this frame (the pose cutoutLatency in the
        /// past, where the passthrough shows the real hand), so the contact shadows can sit under the real finger. False
        /// when the cut-out uses the current pose (then the play pose is the drawn one).</summary>
        public bool DelayedJointWorld(int joint, out Vector3 world)
        {
            world = default;
            if (!_delayedValid || look != Look.PassthroughCutout || _delayed.BonePositions == null || (uint)joint >= (uint)_delayed.BonePositions.Length) return false;
            Vector3 local = _delayed.BonePositions[joint].FromFlippedZVector3f();
            Transform ts = transform.parent;   // the rig's tracking space (Create): OVRPlugin poses are relative to it
            world = ts != null ? ts.TransformPoint(local) : local;
            return true;
        }

        SkinnedMeshRenderer _renderer;
        OVRMesh _mesh;
        OVRSkeleton _skeleton;
        OVRMeshRenderer _meshRenderer;
        MaterialPropertyBlock _block;
        bool _poseValid;
        Material _ghost, _skin, _cutout;
        // E1: per finger, valid for this frame only (ContactVisuals clears and sets them in App.Frame step 8a).
        readonly float[] _lift = new float[5];
        readonly Vector3[] _normal = new Vector3[5];
        // E1 as drawn: each finger's turn (radians), eased in and out over frames (ContactMath.SmoothTheta), capped at 20 degrees.
        readonly float[] _theta = new float[5];
        // E6: the past pose of the cut-out (arrays allocated once by OVRPlugin on the first call).
        OVRPlugin.HandState _delayed;
        bool _delayedValid, _loggedDelayed;
        int _delayedFailures;
        // Hand drums contract request R1: the whole drawn hand raised along a surface normal for this frame (a palm on a drum
        // head rides on the skin instead of sinking into it). Visual only, VR only (the cut-out keeps the real hand's pose).
        static readonly MetaHandVisual[] s_instances = new MetaHandVisual[2];
        float _palmLift;
        Vector3 _palmLiftNormal = Vector3.up;
        int _palmLiftFrame = -1;

        /// <summary>Raise the drawn <paramref name="side"/> hand (every bone) by liftMetres (clamped to 0..0.06) along
        /// worldNormal, for the current frame only: call it every frame while it applies (the hand drums' palm lift,
        /// handdrums.md 4.5). Visual only: the hand data and every play logic keep the raw joints. No-op without a visual.</summary>
        public static void SetPalmLift(HandSide side, float liftMetres, Vector3 worldNormal)
        {
            MetaHandVisual v = (uint)side < 2u ? s_instances[(int)side] : null;
            if (v == null) return;
            v._palmLift = Mathf.Clamp(liftMetres, 0f, 0.06f);
            v._palmLiftNormal = worldNormal.sqrMagnitude > 1e-8f ? worldNormal.normalized : Vector3.up;
            v._palmLiftFrame = Time.frameCount;
        }

        /// <summary>This frame's palm lift (m): 0 unless SetPalmLift was called this frame.</summary>
        public float PalmLift => _palmLiftFrame == Time.frameCount ? _palmLift : 0f;

        /// <summary>Builds the visual under the rig's tracking space. Components are added while the GameObject is
        /// inactive, so every Awake already sees the configured side.</summary>
        public static MetaHandVisual Create(HandSide side, MetaHandSource source, Transform trackingSpace, Material material)
            => Create(side, source, trackingSpace, material, null, null, Look.Ghost);

        /// <summary>As Create, with the three look materials (null ones fall back to the ghost material) and the first look.</summary>
        public static MetaHandVisual Create(HandSide side, MetaHandSource source, Transform trackingSpace, Material ghost,
            Material skin, Material cutout, Look look)
        {
            var go = new GameObject(side == HandSide.Left ? "LeftHandVisual" : "RightHandVisual");
            go.SetActive(false);
            go.transform.SetParent(trackingSpace, false);
            go.AddComponent<SkinnedMeshRenderer>().sharedMaterial = MaterialFor(look, ghost, skin, cutout);
            var visual = go.AddComponent<MetaHandVisual>();
            visual.side = side;
            visual.source = source;
            visual._ghost = ghost;
            visual._skin = skin;
            visual._cutout = cutout;
            visual.look = look;
            go.AddComponent<MetaHandSkeleton>(); // picks XRHandLeft/Right from this component in Awake
            go.AddComponent<OVRMesh>();          // Awake: mesh type from GetComponent<IOVRMeshDataProvider>()
            go.AddComponent<OVRMeshRenderer>();  // Awake: finds provider, OVRMesh, OVRSkeleton on this GameObject
            go.SetActive(true);
            s_instances[(int)side] = visual;
            return visual;
        }

        void Awake()
        {
            _renderer = GetComponent<SkinnedMeshRenderer>();
            _mesh = GetComponent<OVRMesh>();
            _skeleton = GetComponent<OVRSkeleton>();
            _meshRenderer = GetComponent<OVRMeshRenderer>();
            _block = new MaterialPropertyBlock();
        }

        static Material MaterialFor(Look look, Material ghost, Material skin, Material cutout) =>
            look == Look.Skin && skin != null ? skin : look == Look.PassthroughCutout && cutout != null ? cutout : ghost;

        /// <summary>The material drawn for a look (the ghost material when that look has none).</summary>
        public Material MaterialFor(Look l) => MaterialFor(l, _ghost, _skin, _cutout);

        /// <summary>E6: switches the look and its material together. OVRMeshRenderer restores its "original" material every
        /// frame (OVRMeshRenderer.cs:235-239), so it is told too.</summary>
        public void SetLook(Look l)
        {
            look = l;
            if (l != Look.PassthroughCutout) _delayedValid = false;
            Material m = MaterialFor(l);
            if (m == null) return;
            if (_renderer != null && _renderer.sharedMaterial != m) _renderer.sharedMaterial = m;
            if (_meshRenderer != null) _meshRenderer.SetMaterial(m);
        }

        /// <summary>E1: lift the drawn <paramref name="finger"/> by <paramref name="lift"/> metres along
        /// <paramref name="worldNormal"/> this frame (ContactVisuals). Valid for this frame only.</summary>
        public void SetContact(Finger finger, float lift, Vector3 worldNormal)
        {
            int k = (int)finger;
            _lift[k] = lift > 0f ? lift : 0f;
            _normal[k] = worldNormal;
        }

        /// <summary>E1: no finger lifted (ContactVisuals calls it at the start of its update).</summary>
        public void ClearContacts()
        {
            for (int k = 0; k < 5; k++) _lift[k] = 0f;
        }

        /// <summary>The lift set for a finger this frame (diagnostics).</summary>
        public float ContactLift(Finger finger) => _lift[(int)finger];

        /// <summary>The drawn turn of a finger about its knuckle this frame, radians (diagnostics).</summary>
        public float ContactTheta(Finger finger) => _theta[(int)finger];

        /// <summary>Low confidence must last this long before the hand dims (Quest flips a finger's confidence for a frame or
        /// two whenever it touches a key or hides behind the other fingers, and the hand flickered to 40 %).</summary>
        public float lowConfidenceDelay = 0.3f;
        float _lowFor;

        /// <summary>The fade target: 0 untracked, lowAlpha after <paramref name="lowFor"/> seconds of low confidence beyond
        /// <paramref name="delay"/>, else 1 (pure: EditMode tests).</summary>
        public static float FadeTarget(bool tracked, bool highConfidence, float lowFor, float delay, float lowAlpha) =>
            !tracked ? 0f : highConfidence || lowFor < delay ? 1f : lowAlpha;

        void Update()
        {
            if (source == null) return;
            HandFrame f = source.Get(side);
            _poseValid = f.isTracked;
            _delayedValid = false;
            if (_poseValid)
            {
                // What OVRSkeleton's private _updateRootPose/_updateRootScale would do (OVRSkeleton.cs:888-897): OpenXR
                // bones are posed relative to RootPose and divided by RootScale (OVRSkeleton.cs:920-937).
                ref OVRPlugin.HandState s = ref source.State(side);
                if (look == Look.PassthroughCutout) _delayedValid = SampleDelayed(f);
                ref OVRPlugin.HandState p = ref (_delayedValid ? ref _delayed : ref s);
                transform.localPosition = p.RootPose.Position.FromFlippedZVector3f();
                transform.localRotation = p.RootPose.Orientation.FromFlippedZQuatf();
                transform.localScale = Vector3.one * f.handScale;
            }

            // Not hidden during the system gesture: Quest raises it whenever an open palm faces the headset, which is
            // also our menu gesture, and the hand vanished exactly when the user looked at it.
            _lowFor = f.isTracked && !f.isHighConfidence ? _lowFor + Time.deltaTime : 0f;
            float target = FadeTarget(f.isTracked, f.isHighConfidence, _lowFor, lowConfidenceDelay, lowConfidenceAlpha);
            target *= Mathf.Clamp01(appAlpha);
            float seconds = target > Alpha ? fadeInSeconds : fadeOutSeconds;
            Alpha = Mathf.MoveTowards(Alpha, target, Time.deltaTime / Mathf.Max(seconds, 1e-3f));

            // On device OVRMeshRenderer binds only in Start (OVRMeshRenderer.cs:109-122; the Update retry is
            // editor-only, :192-197). Bind late if the skeleton or mesh became ready after Start.
            if (_meshRenderer != null && !_meshRenderer.IsInitialized && _mesh != null && _mesh.IsInitialized
                && _skeleton != null && _skeleton.IsInitialized)
                _meshRenderer.ForceRebind();
        }

        /// <summary>E6: the hand as it was cutoutLatency before this frame's display time (OVRPlugin.GetHandStateAtTime,
        /// OpenXR, ovrp >= 1.113). False (use the current pose) when the runtime has no such sample.</summary>
        bool SampleDelayed(HandFrame f)
        {
            if (_delayedFailures >= DelayedGiveUpFrames || f.displayTime <= 0) return false;
            OVRPlugin.Hand hand = side == HandSide.Left ? OVRPlugin.Hand.HandLeft : OVRPlugin.Hand.HandRight;
            bool ok = OVRPlugin.GetHandStateAtTime(f.displayTime - cutoutLatency, hand, ref _delayed)
                      && (_delayed.Status & OVRPlugin.HandStatus.HandTracked) != 0
                      && _delayed.BonePositions != null && _delayed.BonePositions.Length == HandFrame.JointCount
                      && _delayed.BoneRotations != null && _delayed.BoneRotations.Length == HandFrame.JointCount;
            _delayedFailures = ok ? 0 : _delayedFailures + 1;
            if (!_loggedDelayed && (ok || _delayedFailures == DelayedGiveUpFrames))
            {
                // ARCH 19 (UNVERIFIED): the time base of RequestedTimeStamp vs GetHandStateAtTime, logged once.
                _loggedDelayed = true;
                Debug.Log(ok
                    ? $"[Hands] cutout {side}: displayTime {f.displayTime:F4} s, sampleTime {f.sampleTime:F4} s, asked {f.displayTime - cutoutLatency:F4} s, got requested {_delayed.RequestedTimeStamp:F4} / sample {_delayed.SampleTimeStamp:F4} s"
                    : $"[Hands] cutout {side}: GetHandStateAtTime never succeeded; the cut-out uses the current pose");
            }
            return ok;
        }

        /// <summary>E1: turn each lifted finger about its proximal (MCP) bone so its tip rises by the lift along the contact
        /// normal; the intermediate, distal and tip bones follow through the hierarchy (OVRSkeleton.cs:666-673). Only a finger
        /// whose own tip touches (ContactVisuals sets it per finger) turns, by at most 20 degrees, eased in over ~20 ms and out
        /// over ~50 ms (ContactMath.SmoothTheta): an instant full turn snapped the finger visibly whenever the tracker dipped
        /// into a key. Runs after OVRSkeleton.Update rewrote every bone from this frame's data, so nothing accumulates;
        /// skipped (and the turns forgotten) when the skeleton kept last frame's pose (invalid data) or the hand is the MR
        /// cut-out (it must match the real hand).</summary>
        void ApplyContacts()
        {
            if (!_poseValid || look == Look.PassthroughCutout || _skeleton == null || !_skeleton.IsInitialized || !_skeleton.IsDataValid)
            {
                for (int k = 0; k < 5; k++) _theta[k] = 0f;
                return;
            }
            var bones = _skeleton.Bones;
            if (bones == null || bones.Count < HandFrame.JointCount) return;
            float palm = PalmLift, dt = Time.deltaTime;
            for (int k = 0; k < 5; k++)
            {
                float lift = _lift[k] - palm * Mathf.Max(0f, Vector3.Dot(_normal[k], _palmLiftNormal));   // R1: what the palm lift already raised
                if (lift <= 0f && _theta[k] <= 0f) continue;
                Transform pivot = bones[ContactMath.PivotJoint[k]].Transform, tip = bones[HandFrame.TipJoint[k]].Transform;
                if (pivot == null || tip == null) continue;
                Vector3 axis = ContactMath.LiftAxis(tip.position - pivot.position, _normal[k]);
                float want = 0f;
                if (lift > 0f && ContactMath.FingerRotation(pivot.position, tip.position, _normal[k], lift, ContactMath.DrawnThetaMax, out float theta, out Vector3 a))
                {
                    want = theta;
                    axis = a;
                }
                _theta[k] = ContactMath.SmoothTheta(_theta[k], want, dt);
                if (_theta[k] <= 0f || axis == Vector3.zero) continue;
                pivot.rotation = Quaternion.AngleAxis(_theta[k] * Mathf.Rad2Deg, axis) * pivot.rotation;
            }
        }

        /// <summary>R1: the palm lift moves the skeleton's root bones (the children follow the hierarchy), after OVRSkeleton posed
        /// them from this frame's data (nothing accumulates), before E1 turns the fingers.</summary>
        void ApplyPalmLift()
        {
            float lift = PalmLift;
            if (lift <= 0f || !_poseValid || look == Look.PassthroughCutout || _skeleton == null || !_skeleton.IsInitialized || !_skeleton.IsDataValid) return;
            var bones = _skeleton.Bones;
            if (bones == null) return;
            Vector3 d = _palmLiftNormal * lift;
            for (int i = 0; i < bones.Count; i++)
            {
                // only the roots move (OVRSkeleton parents every other bone under its parent bone: the hierarchy carries them)
                if (_skeleton.IsValidBone((OVRSkeleton.BoneId)bones[i].ParentBoneIndex)) continue;
                if (bones[i].Transform != null) bones[i].Transform.position += d;
            }
        }

        void OnDestroy()
        {
            if ((uint)side < 2u && s_instances[(int)side] == this) s_instances[(int)side] = null;
        }

        void LateUpdate()
        {
            if (_renderer == null) return;
            ApplyPalmLift();
            ApplyContacts();
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(AlphaId, Alpha);
            _renderer.SetPropertyBlock(_block);
        }

        // OVRSkeleton.IOVRSkeletonDataProvider (OVRSkeleton.cs:41-61); `enabled` comes from MonoBehaviour.
        public OVRSkeleton.SkeletonType GetSkeletonType() =>
            side == HandSide.Left ? OVRSkeleton.SkeletonType.XRHandLeft : OVRSkeleton.SkeletonType.XRHandRight;

        public OVRSkeleton.SkeletonPoseData GetSkeletonPoseData()
        {
            // Invalid data makes OVRSkeleton keep the last pose (OVRSkeleton.cs:868-875): the hand freezes while it fades.
            var data = new OVRSkeleton.SkeletonPoseData { IsDataValid = _poseValid && source != null };
            if (data.IsDataValid)
            {
                // E6: the cut-out wears the past pose sampled in Update (same frame); everything else the current one.
                ref OVRPlugin.HandState s = ref (_delayedValid ? ref _delayed : ref source.State(side));
                data.RootPose = s.RootPose;
                data.RootScale = s.HandScale;
                data.BoneRotations = s.BoneRotations;
                data.BoneTranslations = s.BonePositions;
                data.IsDataHighConfidence = source.Get(side).isHighConfidence;
            }
            return data;
        }

        // OVRMesh.IOVRMeshDataProvider (OVRMesh.cs:36-39).
        public OVRMesh.MeshType GetMeshType() =>
            side == HandSide.Left ? OVRMesh.MeshType.XRHandLeft : OVRMesh.MeshType.XRHandRight;

        // OVRMeshRenderer.IOVRMeshRendererDataProvider (OVRMeshRenderer.cs:34-37). With the default ToggleRenderer
        // behaviour the mesh is drawn while IsDataValid && IsDataHighConfidence (OVRMeshRenderer.cs:215-223).
        public OVRMeshRenderer.MeshRendererData GetMeshRendererData() => new OVRMeshRenderer.MeshRendererData
        {
            IsDataValid = true,
            IsDataHighConfidence = Alpha > 0.001f,
            ShouldUseSystemGestureMaterial = false,
        };
    }

    /// <summary>OVRSkeleton whose protected _skeletonType (OVRSkeleton.cs:393-394) is chosen from the sibling
    /// MetaHandVisual before OVRSkeleton.Awake searches for a provider of that type (OVRSkeleton.cs:510-548).</summary>
    public sealed class MetaHandSkeleton : OVRSkeleton
    {
        protected override void Awake()
        {
            var visual = GetComponent<MetaHandVisual>();
            _skeletonType = visual != null ? visual.GetSkeletonType() : SkeletonType.None;
            base.Awake();
        }

        /// <summary>Re-pose the bones now, e.g. from Application.onBeforeRender after a re-sample.</summary>
        public void Repose() => UpdateSkeleton();
    }
}
