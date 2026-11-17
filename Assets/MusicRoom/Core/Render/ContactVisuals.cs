// The visual contact layer of 10-upgrade/experience.md section 2.2: App.Frame step 8a, after Tick(StickHover) and before
// HoverGlow.Update, when key depths, cymbal tilts and the drawn sticks are final. Per frame:
//   1. each connected, non-mouse hand: every fingertip joint is queried (above 0.06 m, below 0.04 m) against every
//      IContactSurface (the list is cached on the first frame and on LayoutChangedEvent, never GetAll per frame); the hit
//      with the smallest Height >= -below wins;
//   2. Clamp (E1): the wanted lift (penetration of the pad, rc = the tip joint radius, up to 25 mm, fading out by 40 mm),
//      instant attack and a 25 ms release per hand x finger, handed to HandInput.Visual(side).SetContact (and the editor's
//      debug hands). Visual only: the play logic keeps the raw joints. Off while the real hands show through the cut-out
//      (E6: the hole must match the real hand); on for the drawn hands, in "Play here" and in "Play in my room" alike;
//   3. Shadow (E3): two blobs per finger (the tip and distal-joint spheres of the DRAWN finger, against the plane the tip
//      query returned; with the cut-out its delayed pose, so the shadow sits under the real hand the passthrough shows)
//      plus the drawn stick tips (IDrumsApi.StickShadowSpheres), A = 0.55 in VR, 0.35 in MR;
//   4. one upload of the shadow mesh (one draw), then every pending hover glow a shadow covers is drawn at
//      HoverGlowGainWithShadow (0.5) so the realistic cue leads.
// It also sets the hands' look (IHandLook): the drawn Skin hands in "Play here" and, by default, in "Play in my room"; the
// PassthroughCutout (the real hands) in mixed reality only when the player chose "Real hands" (AppSettings.MrRealHands,
// the menu's row). Leaving mixed reality always brings the Skin hands back. Material and look change together.
// Allocation-free per frame (the surface list is rebuilt only on the first frame and on a layout change).
using System;
using System.Collections.Generic;
using MusicRoom.Hands;
using Unity.Profiling;
using UnityEngine;

namespace MusicRoom.Core
{
    public sealed class ContactVisuals : IHandLook
    {
        public const float QueryAbove = 0.06f, QueryBelow = 0.04f;
        /// <summary>E3 A: the diffuse share of the light in the dim, warm VR room; MR matches the shadow catcher's .32.</summary>
        public const float AmbientVr = 0.55f, AmbientMr = 0.35f;
        /// <summary>A hover glow under a contact shadow is drawn at this strength (the realistic cue leads).</summary>
        public const float HoverGlowGainWithShadow = 0.5f;
        public const int MaxStickSpheres = 4;

        static readonly ProfilerMarker s_marker = new ProfilerMarker("[Core] contact");

        struct FingerContact
        {
            public float Lift;
            public Vector3 Normal;
        }

        readonly App _app;
        readonly HoverGlowPool _glow;
        readonly FingerContact[] _contacts = new FingerContact[10];
        readonly List<IContactSurface> _surfaces = new List<IContactSurface>(4);
        readonly HandFrame[] _frames = new HandFrame[2];
        readonly MetaHandVisual[] _visuals = new MetaHandVisual[2];
        readonly Vector4[] _sticks = new Vector4[MaxStickSpheres];
        readonly Action<LayoutChangedEvent> _onLayout;
        readonly Action<MixedRealityEvent> _onMr;
        DebugHandVisual _debug;
        IDrumsApi _drums;
        bool _surfacesValid;

        /// <summary>E1 on / off (the spec's fallback switch). Off: no finger is lifted (shadows stay).</summary>
        public bool ClampEnabled = true;
        /// <summary>E3 on / off.</summary>
        public bool ShadowsEnabled = true;
        public ContactShadowPool Shadows { get; }
        /// <summary>"Play in my room" is active (MixedRealityEvent): shadows at A = 0.35 (the real room's light).</summary>
        public bool MixedReality { get; private set; }
        /// <summary>The real hands show through the passthrough cut-out: mixed reality with "Real hands" chosen.</summary>
        public bool CutoutActive => MixedReality && RealHandsInMr;
        /// <summary>The finger clamp lifts the drawn fingers out of what they touch; never on the cut-out (it must match the
        /// real hand).</summary>
        public bool ClampActive => ClampEnabled && !CutoutActive;
        public float Ambient => MixedReality ? AmbientMr : AmbientVr;
        /// <summary>Blobs drawn this frame (fingers and sticks).</summary>
        public int ShadowCount => Shadows.Drawn;
        /// <summary>Hover glows dimmed this frame because a shadow covers them.</summary>
        public int DimmedGlows { get; private set; }
        /// <summary>Fingers lifted this frame.</summary>
        public int LiftedFingers { get; private set; }
        public int SurfaceCount => _surfaces.Count;

        /// <summary>The runtime service (App builds it): the pool under <paramref name="parent"/>, events from the app.</summary>
        public ContactVisuals(App app, MaterialLibrary materials, Transform parent, bool headless, HoverGlowPool glow)
        {
            _app = app;
            _glow = glow;
            Shadows = new ContactShadowPool(materials, parent, headless);
            _onLayout = OnLayout;
            _onMr = OnMixedReality;
            if (app != null)
            {
                app.Events.Subscribe(_onLayout);
                app.Events.Subscribe(_onMr);
            }
        }

        /// <summary>Tests: no app (call SetSurfaces, then Step with scripted hand frames).</summary>
        public ContactVisuals(ContactShadowPool shadows, HoverGlowPool glow)
        {
            _glow = glow;
            Shadows = shadows ?? new ContactShadowPool(null, null, true);
            _onLayout = OnLayout;
            _onMr = OnMixedReality;
        }

        public void Dispose()
        {
            if (_app != null)
            {
                _app.Events.Unsubscribe(_onLayout);
                _app.Events.Unsubscribe(_onMr);
            }
            Shadows.Dispose();
        }

        void OnLayout(LayoutChangedEvent e) => _surfacesValid = false;
        void OnMixedReality(MixedRealityEvent e) => SetMixedReality(e.Active);

        bool _realHands, _realHandsRead;

        /// <summary>IHandLook: the player's choice for "Play in my room" (AppSettings.MrRealHands; false = the drawn hands).
        /// Setting it saves it and, in mixed reality, switches the look at once.</summary>
        public bool RealHandsInMr
        {
            get
            {
                if (!_realHandsRead) { _realHands = _app?.Settings != null && _app.Settings.MrRealHands; _realHandsRead = true; }
                return _realHands;
            }
            set
            {
                _realHands = value;
                _realHandsRead = true;
                if (_app?.Settings != null)
                {
                    _app.Settings.MrRealHands = value;
                    _app.Settings.Save();
                }
                ApplyLook();
            }
        }

        /// <summary>The look the hands wear now: the cut-out only in mixed reality with "Real hands" chosen, else Skin.</summary>
        public MetaHandVisual.Look WantedLook => CutoutActive ? MetaHandVisual.Look.PassthroughCutout : MetaHandVisual.Look.Skin;

        /// <summary>E6: entering mixed reality keeps the drawn Skin hands (and the clamp) unless the player chose "Real
        /// hands", which shows the passthrough cut-out with the clamp off; shadows use A = 0.35 either way. Leaving brings
        /// the lit Skin hands and the clamp back.</summary>
        public void SetMixedReality(bool active)
        {
            MixedReality = active;
            ApplyLook();
        }

        void ApplyLook()
        {
            HandInput hands = _app?.HandInput;
            if (hands != null && hands.Look != WantedLook) hands.SetLook(WantedLook);
        }

        /// <summary>Tests: use these surfaces instead of the app's registrations.</summary>
        public void SetSurfaces(IReadOnlyList<IContactSurface> surfaces)
        {
            _surfaces.Clear();
            if (surfaces != null) for (int i = 0; i < surfaces.Count; i++) _surfaces.Add(surfaces[i]);
            _surfacesValid = true;
        }

        /// <summary>The smoothed lift of one finger (m) as of the last update.</summary>
        public float Lift(HandSide side, Finger finger) => _contacts[(int)side * 5 + (int)finger].Lift;

        /// <summary>The contact normal a finger's lift is applied along (kept while the lift decays).</summary>
        public Vector3 LiftNormal(HandSide side, Finger finger) => _contacts[(int)side * 5 + (int)finger].Normal;

        void RefreshSurfaces()
        {
            if (_surfacesValid || _app == null) return;
            _surfaces.Clear();
            _surfaces.AddRange(_app.GetAll<IContactSurface>());
            _drums = _app.Get<IDrumsApi>();
            _surfacesValid = true;
        }

        /// <summary>App.Frame step 8a.</summary>
        public void Update(float dt)
        {
            if (_app == null) return;
            RefreshSurfaces();
            PointerInput input = _app.Input;
            HandInput hands = _app.HandInput;
            for (int h = 0; h < 2; h++)
            {
                HandState hs = input.Hands[h];
                HandFrame f = input.Frame(hs);
                _frames[h] = hs.Connected && !hs.IsMouse && f != null && f.isTracked ? f : null;
                _visuals[h] = hands != null ? hands.Visual((HandSide)h) : null;
            }
            _debug = hands != null ? hands.DebugHands : null;
            int sticks = 0;
            if (_drums != null && ShadowsEnabled) sticks = Mathf.Clamp(_drums.StickShadowSpheres(new Span<Vector4>(_sticks)), 0, MaxStickSpheres);
            Transform head = _app.Head;
            Step(dt, _frames[0], _frames[1], sticks, head != null ? head.position : Vector3.zero);
        }

        /// <summary>Tests: one update with stick sphere centres / radii (xyz, w) given directly.</summary>
        public void Step(float dt, HandFrame left, HandFrame right, ReadOnlySpan<Vector4> stickSpheres, Vector3 boundsCentre)
        {
            int n = Mathf.Min(stickSpheres.Length, MaxStickSpheres);
            for (int i = 0; i < n; i++) _sticks[i] = stickSpheres[i];
            Step(dt, left, right, n, boundsCentre);
        }

        /// <summary>The per-frame work on two hand frames (null = that hand is not usable).</summary>
        public void Step(float dt, HandFrame left, HandFrame right, int stickSpheres, Vector3 boundsCentre)
        {
            s_marker.Begin();
            try { StepBody(dt, left, right, stickSpheres, boundsCentre); }
            finally { s_marker.End(); }
        }

        void StepBody(float dt, HandFrame left, HandFrame right, int stickSpheres, Vector3 boundsCentre)
        {
            _frames[0] = left;
            _frames[1] = right;
            for (int h = 0; h < 2; h++) _visuals[h]?.ClearContacts();
            _debug?.ClearContacts();
            Shadows.Begin();
            bool clamp = ClampActive, shadows = ShadowsEnabled;
            float ambient = Ambient;
            LiftedFingers = 0;
            for (int h = 0; h < 2; h++)
            {
                HandFrame f = _frames[h];
                // E6: with the cut-out the hand seen is the real one (passthrough, ~39 ms late) under a cut-out posed in the
                // past: its shadows follow that pose, not the predicted one the play logic uses (the clamp is off then).
                MetaHandVisual vis = _visuals[h];
                bool delayed = CutoutActive && f != null && vis != null && vis.UsingDelayedPose;
                for (int k = 0; k < 5; k++)
                {
                    ref FingerContact c = ref _contacts[h * 5 + k];
                    float want = 0f;
                    bool has = false;
                    ContactHit hit = default;
                    int tipJ = HandFrame.TipJoint[k];
                    Vector3 tipP = default, distP = default;
                    if (f != null)
                    {
                        tipP = f.position[tipJ];
                        distP = f.position[tipJ - 1];
                        if (delayed && (!vis.DelayedJointWorld(tipJ, out tipP) || !vis.DelayedJointWorld(tipJ - 1, out distP)))
                        {
                            tipP = f.position[tipJ];
                            distP = f.position[tipJ - 1];
                        }
                        has = Query(tipP, QueryAbove, QueryBelow, out hit);
                        if (has && clamp && (hit.Use & ContactUse.Clamp) != 0)
                        {
                            want = ContactMath.PenetrationLift(f.radius[tipJ] - hit.Height);
                            c.Normal = hit.Normal;
                        }
                    }
                    // A lost hand freezes and fades out (its bones are not rewritten): it starts again from no lift.
                    c.Lift = clamp && f != null ? ContactMath.SmoothLift(c.Lift, want, dt) : 0f;
                    if (c.Lift > 0f)
                    {
                        LiftedFingers++;
                        _visuals[h]?.SetContact((Finger)k, c.Lift, c.Normal);
                        _debug?.SetContact((HandSide)h, (Finger)k, c.Lift, c.Normal);
                    }
                    if (has && shadows && (hit.Use & ContactUse.Shadow) != 0) FingerShadows(f, k, tipP, distP, c.Lift, c.Normal, hit, ambient);
                }
            }
            if (shadows)
                for (int i = 0; i < stickSpheres; i++)
                {
                    Vector4 s = _sticks[i];
                    var centre = new Vector3(s.x, s.y, s.z);
                    if (!Query(centre, QueryAbove, QueryBelow, out ContactHit hit) || (hit.Use & ContactUse.Shadow) == 0) continue;
                    Shadows.Add(centre - hit.Normal * hit.Height, hit.Normal, hit.Tangent, hit.ClipHalf, hit.Height, s.w, ambient, hit.ClipOrigin);
                }
            Shadows.End(boundsCentre);
            DimGlows();
        }

        /// <summary>The nearest surface hit over every contact surface: the smallest Height &gt;= -below.</summary>
        public bool Query(Vector3 world, float above, float below, out ContactHit best)
        {
            best = default;
            bool any = false;
            for (int i = 0; i < _surfaces.Count; i++)
            {
                IContactSurface s = _surfaces[i];
                if (s == null || !s.QueryContact(world, above, below, out ContactHit hit)) continue;
                if (hit.Height < -below || hit.Height > above) continue;
                if (!any || hit.Height < best.Height) { best = hit; any = true; }
            }
            return any;
        }

        /// <summary>The tip and distal-joint spheres of the finger as it is DRAWN (after the clamp's turn about the MCP, capped
        /// at the drawn hands' 20 degrees; their 20-50 ms easing is left out, a shadow follows the wanted turn; in
        /// the cut-out its delayed joints, tip / dist), against the plane the tip query returned.</summary>
        void FingerShadows(HandFrame f, int k, Vector3 tip, Vector3 dist, float lift, Vector3 liftNormal, in ContactHit hit, float ambient)
        {
            int tipJ = HandFrame.TipJoint[k], distJ = tipJ - 1, pivJ = ContactMath.PivotJoint[k];
            if (lift > 0f && ContactMath.FingerRotation(f.position[pivJ], tip, liftNormal, lift, ContactMath.DrawnThetaMax, out float theta, out Vector3 axis))
            {
                Vector3 pivot = f.position[pivJ];
                tip = ContactMath.RotateAbout(tip, pivot, axis, theta);
                dist = ContactMath.RotateAbout(dist, pivot, axis, theta);
            }
            Vector3 n = hit.Normal;
            float ht = Vector3.Dot(n, tip - hit.Point);
            Shadows.Add(tip - n * ht, n, hit.Tangent, hit.ClipHalf, ht, f.radius[tipJ], ambient, hit.ClipOrigin);
            float hd = Vector3.Dot(n, dist - hit.Point);
            Shadows.Add(dist - n * hd, n, hit.Tangent, hit.ClipHalf, hd, f.radius[distJ], ambient, hit.ClipOrigin);
        }

        /// <summary>Every pending hover glow whose spot a blob quad covers is drawn at HoverGlowGainWithShadow.</summary>
        void DimGlows()
        {
            DimmedGlows = 0;
            if (_glow == null || Shadows.Count == 0) return;
            for (int i = 0; i < _glow.Pending; i++)
            {
                HoverGlowSpec g = _glow.Spec(i);
                float reach = 0.5f * Mathf.Max(g.Length, g.Width);
                for (int b = 0; b < Shadows.Count; b++)
                {
                    ContactShadowPool.Blob blob = Shadows.Get(b);
                    float r = blob.Half + reach;
                    if ((g.Position - blob.Centre).sqrMagnitude > r * r) continue;
                    _glow.Dim(i, HoverGlowGainWithShadow);
                    DimmedGlows++;
                    break;
                }
            }
        }
    }
}
