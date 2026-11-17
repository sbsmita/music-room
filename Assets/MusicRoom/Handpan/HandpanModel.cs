// The handpan as objects in the scene (handpan.md 7): the rig root (the stand's floor point), the instrument (rim centre,
// tilted 28 degrees toward the player) with the steel shell and the rubber edge band, and the walnut stand. 3 draws:
// steel, rubber, stand (the floor contact blob is the room's, phase B). Materials are clones of templates
// (HandpanMaterials; ARCHITECTURE 14); nothing glows and nothing uses a MaterialPropertyBlock. Feedback is physical:
// the instrument settles on its felt ring when struck (HandpanMotion), fingers stop on the steel and get contact
// shadows (HandpanContactSurface, E1/E3). No dependence on the platform's types: the module (phase B) owns one model.
using System;
using System.Collections.Generic;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Handpan
{
    public sealed class HandpanMaterials : IMaterialTemplateProvider
    {
        public const string Steel = "Handpan/Steel", Stand = "Handpan/Stand";
        public static readonly Color Rubber = new Color32(0x1d, 0x1a, 0x18, 0xff);
        public const float RubberSmoothness = 0.25f;
        public static readonly int MetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
        public static readonly int BumpScale = Shader.PropertyToID("_BumpScale");

        public string ResourcesFolder => "Assets/MusicRoom/Handpan";

        public IEnumerable<MaterialTemplateSpec> Templates
        {
            get
            {
                yield return new MaterialTemplateSpec(Steel, "Universal Render Pipeline/Lit", InitSteel);
                yield return new MaterialTemplateSpec(Stand, "Universal Render Pipeline/Lit", InitStand);
            }
        }

        /// <summary>Nitrided steel: colour, metallic (R) / smoothness (A) and normal maps in one atlas (HandpanTextures).</summary>
        public static void InitSteel(Material m)
        {
            m.SetColor(MaterialLibrary.BaseColor, Color.white);
            m.SetFloat(MaterialLibrary.Smoothness, 1f);   // scales the map's alpha (URP Lit with _METALLICSPECGLOSSMAP)
            m.SetFloat(MaterialLibrary.Metallic, 1f);
            m.SetFloat(BumpScale, 1f);
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
        }

        /// <summary>Oiled walnut and its felt: one atlas, one smoothness (0.45, handpan.md 7.3).</summary>
        public static void InitStand(Material m)
        {
            m.SetColor(MaterialLibrary.BaseColor, Color.white);
            m.SetFloat(MaterialLibrary.Smoothness, 0.42f);
            m.SetFloat(MaterialLibrary.Metallic, 0f);
        }

        static bool s_warned;

        /// <summary>A clone of a handpan template. Before render-setup has written the template (phase A in a fresh clone)
        /// the clone is made from LitOpaque with the template's own settings (logged once).</summary>
        public static Material Clone(MaterialLibrary mats, string template, Action<Material> init)
        {
            if (mats.HasTemplate(template)) return mats.Clone(template);
            if (!s_warned) { s_warned = true; Debug.LogWarning($"[Handpan] material template {template} missing (run Tools/unity.sh render-setup): using LitOpaque + its settings"); }
            Material m = mats.Clone(MaterialLibrary.LitOpaque);
            init(m);
            return m;
        }
    }

    /// <summary>The instrument settling on its felt ring (handpan.md 7.4.2): a vertical spring-damper at 12 Hz, zeta 0.5,
    /// kicked by the stroke's momentum: about 0.3 mm for a hard fingertip, 1.3-1.5 mm for a palm slap.</summary>
    public sealed class HandpanMotion
    {
        public const double Hz = 12, Zeta = 0.5;
        /// <summary>m/s of the instrument per unit stroke momentum (m V, kg m/s of handpan-engine's contact model).</summary>
        public const double KickPerMomentum = 0.0138;
        double _x, _v;
        public double Offset => _x;
        public bool Moving => Math.Abs(_x) > 1e-6 || Math.Abs(_v) > 1e-5;

        public void Kick(double vel, HandpanTech tech)
        {
            double v = Math.Max(0, Math.Min(1, vel));
            double V = HandpanK.V_MIN + (HandpanK.V_MAX - HandpanK.V_MIN) * Math.Pow(v, HandpanK.V_EXP);
            double m = tech == HandpanTech.Slap ? HandpanK.M_PALM : tech == HandpanTech.Tak ? HandpanK.M_TAK : HandpanK.M_FINGER;
            _v += KickPerMomentum * m * V;
        }

        /// <summary>Advances dt seconds (8 substeps of semi-implicit Euler: stable at 72-90 Hz frames).</summary>
        public void Step(double dt)
        {
            if (!Moving) { _x = 0; _v = 0; return; }
            double w = 2 * Math.PI * Hz, h = dt / 8;
            for (int k = 0; k < 8; k++) { _v += (-w * w * _x - 2 * Zeta * w * _v) * h; _x += _v * h; }
        }

        public void Stop() { _x = 0; _v = 0; }
    }

    /// <summary>E1 / E3: the steel as a contact surface (the cap + relief): fingers stop on it and cast soft contact
    /// shadows on it; the band's edge takes shadows only. Allocation-free. The module registers it (phase B).</summary>
    public sealed class HandpanContactSurface : IContactSurface
    {
        readonly HandpanModel _model;
        public bool Enabled = true;
        public HandpanContactSurface(HandpanModel model) { _model = model; }

        public bool QueryContact(Vector3 world, float above, float below, out ContactHit hit)
        {
            hit = default;
            if (!Enabled || _model == null || !_model.Root.gameObject.activeInHierarchy || _model.Current != HandpanModel.Config.Seat || _model.Blending) return false;
            HandpanPlace place = _model.Place;
            HandpanContact c = HandpanMath.ContactOf(D3.From(world), 0, place);
            double s = Math.Sqrt(c.X * c.X + c.Z * c.Z);
            bool top = c.OnTop, band = !top && s < HandpanSpec.RimArc + 0.02 && place.FromWorld(D3.From(world)).Y > -0.03;
            if (!top && !band) return false;
            if (c.H > above || c.H < -below) return false;
            D3 n = place.DirToWorld(c.NLocal).Normalized;
            hit.Normal = n.ToVector3();
            hit.Point = world - hit.Normal * (float)c.H;
            hit.Height = (float)c.H;
            hit.Use = top ? ContactUse.Both : ContactUse.Shadow;
            return true;
        }
    }

    public sealed class HandpanModel : IDisposable
    {
        /// <summary>The rig group: the stand's floor point (seat frame home (0, 0, 0.38), yaw 0). Layout moves it.</summary>
        public readonly Transform Root;
        /// <summary>The rim centre, RimHeight above the root, tilted TiltDeg toward the player: the instrument-local frame.</summary>
        public readonly Transform Instrument;
        public readonly MeshRenderer Shell, Band, Stand;
        public readonly MeshCollider DesktopCollider;
        /// <summary>The grab handles (handpan.md 7.5): the edge band and the stand, never the steel. Filled once, never replaced.</summary>
        public readonly List<Renderer> Handles = new List<Renderer>();
        /// <summary>The played parts: the steel.</summary>
        public readonly List<Renderer> Played = new List<Renderer>();
        public readonly HandpanMotion Motion = new HandpanMotion();
        public readonly HandpanContactSurface Contact;
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly Mesh _shellSeat, _shellDisplay, _bandSeat, _bandDisplay;
        readonly MeshFilter _shellFilter, _bandFilter;
        Vector3 _rest;
        // the display <-> seat transition (a glide): the instrument's pose blends from where it was to its new place
        Vector3 _fromPos; Quaternion _fromRot; float _blend = 1f, _blendSeconds = 1f;
        bool _toSeat = true;

        public static readonly Vector3 InstrumentLocal = new Vector3(0f, (float)HandpanSpec.RimHeight, 0f);
        public static readonly Quaternion InstrumentTilt = Quaternion.Euler(-(float)HandpanSpec.TiltDeg, 0f, 0f);

        /// <summary>Where the instrument hangs in the display configuration.</summary>
        public enum Config : byte { Seat = 0, Wall, Surface }
        public Config Current { get; private set; } = Config.Seat;
        public bool Blending => _blend < 1f;

        HandpanModel(Transform parent, MaterialLibrary mats, bool collider, bool textures)
        {
            var root = new GameObject("Handpan");
            Root = root.transform;
            Root.SetParent(parent, false);
            var inst = new GameObject("Instrument");
            Instrument = inst.transform;
            Instrument.SetParent(Root, false);
            Instrument.localPosition = InstrumentLocal;
            Instrument.localRotation = InstrumentTilt;
            _rest = InstrumentLocal;

            Material steel = HandpanMaterials.Clone(mats, HandpanMaterials.Steel, HandpanMaterials.InitSteel);
            Material rubber = mats.Lit(HandpanMaterials.Rubber, HandpanMaterials.RubberSmoothness, 0f);
            Material wood = HandpanMaterials.Clone(mats, HandpanMaterials.Stand, HandpanMaterials.InitStand);
            if (textures)   // a headless boot (tests without a GPU) skips them: nothing draws
            {
                steel.SetTexture(MaterialLibrary.BaseMap, HandpanTextures.Get(HandpanTextures.SteelBase));
                steel.SetTexture(HandpanMaterials.MetallicGlossMap, HandpanTextures.Get(HandpanTextures.SteelMs));
                steel.SetTexture(MaterialLibrary.BumpMap, HandpanTextures.Get(HandpanTextures.SteelNormal));
                wood.SetTexture(MaterialLibrary.BaseMap, HandpanTextures.Get(HandpanTextures.Stand));
            }
            else steel.SetColor(MaterialLibrary.BaseColor, new Color(0.23f, 0.19f, 0.16f));

            _shellSeat = Own(HandpanGeometry.Shell(HandpanGeometry.Seat));
            _shellDisplay = Own(HandpanGeometry.Shell(HandpanGeometry.Display));
            _bandSeat = Own(HandpanGeometry.Band(HandpanGeometry.Seat));
            // the bracket is built in rig coordinates (the rig at its wall pose) and stored in the instrument's display frame
            Matrix4x4 displayToRig = Matrix4x4.TRS(HandpanGeometry.DisplayCentre, HandpanGeometry.DisplayTilt, Vector3.one), rigToDisplay = displayToRig.inverse;
            _bandDisplay = Own(HandpanGeometry.Band(HandpanGeometry.Display, v => rigToDisplay.MultiplyPoint3x4(v)));
            Shell = Part("Shell", Instrument, _shellSeat, steel);
            Band = Part("Band", Instrument, _bandSeat, rubber);
            _shellFilter = Shell.GetComponent<MeshFilter>();
            _bandFilter = Band.GetComponent<MeshFilter>();
            // the stand lives in the rig frame (it does not settle with the shell)
            Func<D3, D3> toRig = p =>
            {
                Vector3 q = InstrumentLocal + InstrumentTilt * new Vector3((float)p.X, (float)p.Y, (float)p.Z);
                return new D3(q.x, q.y, q.z);
            };
            Stand = Part("Stand", Root, Own(HandpanGeometry.Stand(toRig)), wood);
            Handles.Add(Band);
            Handles.Add(Stand);
            Played.Add(Shell);
            if (collider)
            {
                // the desktop click target (ARCHITECTURE 0): a coarse top cap on Layers.PlaySurface
                Mesh cm = Own(TopCollider());
                var go = new GameObject("DesktopCollider");
                go.transform.SetParent(Instrument, false);
                go.layer = Layers.PlaySurface;
                DesktopCollider = go.AddComponent<MeshCollider>();
                DesktopCollider.sharedMesh = cm;
            }
            Contact = new HandpanContactSurface(this);
        }

        public static HandpanModel Build(Transform parent, MaterialLibrary mats, bool desktopCollider = true, bool textures = true) => new HandpanModel(parent, mats, desktopCollider, textures);

        Mesh Own(Mesh m) { _owned.Add(m); return m; }

        MeshRenderer Part(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // VR: no realtime shadows; MR turns the shell's and stand's on
            r.receiveShadows = true;
            return r;
        }

        static Mesh TopCollider()
        {
            const int rings = 32, segs = 64;
            var p = new List<Vector3> { HandpanGeometry.TopPoint(0, 0).ToVector3() };
            var idx = new List<int>();
            for (int k = 1; k <= rings; k++)
            {
                double s = HandpanSpec.RimArc * k / rings;
                for (int j = 0; j < segs; j++) { double a = 2 * Math.PI * j / segs; p.Add(HandpanGeometry.TopPoint(s * Math.Sin(a), -s * Math.Cos(a)).ToVector3()); }
            }
            int V(int k, int j) => k == 0 ? 0 : 1 + (k - 1) * segs + (j % segs);
            // wound so the faces look up and out (Cross(b - a, c - a) along the outward normal): a mesh collider is hit from its
            // front side only (Physics.queriesHitBackfaces is off), and the click comes from above
            for (int j = 0; j < segs; j++) { idx.Add(0); idx.Add(V(1, j + 1)); idx.Add(V(1, j)); }
            for (int k = 1; k < rings; k++) for (int j = 0; j < segs; j++) { idx.Add(V(k, j)); idx.Add(V(k + 1, j + 1)); idx.Add(V(k + 1, j)); idx.Add(V(k, j)); idx.Add(V(k, j + 1)); idx.Add(V(k + 1, j + 1)); }
            var m = new Mesh { name = "Handpan/TopCollider" };
            m.SetVertices(p); m.SetTriangles(idx, 0);
            return m;
        }

        /// <summary>The play logic's placement: the rest pose's rim centre (world), the tilt, the rig's yaw.</summary>
        public HandpanPlace Place
        {
            get
            {
                Vector3 c = Root.TransformPoint(InstrumentLocal);
                return new HandpanPlace(new D3(c.x, c.y, c.z), HandpanSpec.TiltDeg, Root.eulerAngles.y);
            }
        }

        /// <summary>A stroke landed: the instrument dips on its felt.</summary>
        public void Struck(double vel, HandpanTech tech) { if (Current == Config.Seat && !Blending) Motion.Kick(vel, tech); }

        /// <summary>Per frame: the settle motion (written only while it moves) and the place-change blend.</summary>
        public void Tick(float dt)
        {
            if (Blending)
            {
                _blend = Mathf.Min(1f, _blend + dt / _blendSeconds);
                float k = Mathf.SmoothStep(0f, 1f, _blend);
                // the stand shows only under a seated shell: toward the seat it comes back when the shell rests on it (never
                // a shell hanging in the air above a stand on the floor)
                if (_toSeat && !Stand.enabled && _blend >= 1f) Stand.enabled = true;
                TargetPose(_toSeat ? Config.Seat : Current, out Vector3 p, out Quaternion r);
                Instrument.localPosition = Vector3.Lerp(_fromPos, p, k);
                Instrument.localRotation = Quaternion.Slerp(_fromRot, r, k);
                return;
            }
            if (!Motion.Moving) return;
            Motion.Step(dt);
            Vector3 down = Root.InverseTransformDirection(Vector3.down);
            Instrument.localPosition = _rest + down * (float)Motion.Offset;
        }

        static void TargetPose(Config c, out Vector3 p, out Quaternion r)
        {
            if (c == Config.Wall) { p = HandpanGeometry.DisplayCentre; r = HandpanGeometry.DisplayTilt; return; }
            if (c == Config.Surface) { p = new Vector3(0f, (float)(HandpanSpec.BotH + 0.01), 0f); r = Quaternion.identity; return; }   // resting flat on a surface, on its bottom
            p = InstrumentLocal; r = InstrumentTilt;
        }

        /// <summary>The static configuration (platform IStowable): on display on the wall bracket (the display LOD meshes,
        /// the bracket in the band's mesh, no stand: 2 draws), on a surface (no stand), or at the seat on its stand.</summary>
        public void SetConfig(Config c)
        {
            Current = c;
            Motion.Stop();
            _blend = 1f;
            bool wall = c == Config.Wall;
            _shellFilter.sharedMesh = wall ? _shellDisplay : _shellSeat;
            _bandFilter.sharedMesh = wall ? _bandDisplay : _bandSeat;
            Stand.enabled = c == Config.Seat;
            TargetPose(c, out Vector3 p, out Quaternion r);
            _rest = p;
            Instrument.localPosition = p;
            Instrument.localRotation = r;
            if (DesktopCollider != null) DesktopCollider.enabled = c == Config.Seat;
        }

        /// <summary>The glide starts (Layout's Leaving): toward the seat the full meshes come back at once and the shell eases
        /// from its display pose down to its seat pose over `seconds` (0.85 s: inside the shortest glide, 0.9 s), the stand
        /// showing when the shell rests on it; toward the display the stand hides at once and the shell eases into the wall
        /// pose (the bracket appears when it is Displayed). The stand is never shown under a shell that is not on it.</summary>
        public void BeginMove(bool toSeat, float seconds = 0.85f)
        {
            _fromPos = Instrument.localPosition;
            _fromRot = Instrument.localRotation;
            _toSeat = toSeat;
            Motion.Stop();
            if (toSeat)
            {
                Current = Config.Seat;
                _shellFilter.sharedMesh = _shellSeat;
                _bandFilter.sharedMesh = _bandSeat;
                Stand.enabled = false;   // Tick shows it when the shell rests on it
                _rest = InstrumentLocal;
            }
            else { Current = Config.Wall; Stand.enabled = false; }
            _blend = 0f;
            _blendSeconds = Mathf.Max(0.1f, seconds);
        }

        public int Triangles => HandpanGeometry.Triangles(_shellSeat) + HandpanGeometry.Triangles(_bandSeat) + HandpanGeometry.Triangles(Stand.GetComponent<MeshFilter>().sharedMesh);
        public int DisplayTriangles => HandpanGeometry.Triangles(_shellDisplay) + HandpanGeometry.Triangles(_bandDisplay);

        public void Dispose()
        {
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
        }
    }
}
