// The panel's scene objects (spec 08 sections 1.1-1.3): the curved face (with its MeshCollider: the desktop mouse ray
// lands on it) and back plate under a "Visual" child that carries the open / close scale, so the rig's Group (Root)
// keeps scale 1 for Layout; the summon ring; the two canvas render textures and their materials (module templates
// Menu/Face and Menu/Back, see MenuMaterials). The canvases are painted only when dirty (GpuMenuCanvas).
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Menu
{
    public sealed class MenuView
    {
        public const string FaceTemplate = "Menu/Face", BackTemplate = "Menu/Back", ShapeTemplate = "Menu/Shape";
        public static readonly Color RingColor = MenuStyle.Rgb(95, 216, 255);

        public readonly Transform Root;
        public readonly Transform Visual;
        public readonly MeshRenderer Face, Back;
        public readonly MeshCollider Collider;
        public readonly Transform Ring;
        public RenderTexture FaceTexture { get; private set; }
        public RenderTexture BackTexture { get; private set; }
        public GpuMenuCanvas Canvas { get; private set; }
        public bool CanPaint => Canvas != null;

        readonly MeshRenderer _ring;
        readonly Material _faceMat, _backMat, _ringMat;
        readonly Mesh _faceMesh, _backMesh, _ringMesh;
        readonly Transform _textRoot;
        readonly System.Collections.Generic.List<Material> _ownMaterials = new System.Collections.Generic.List<Material>(3);
        bool _backPainted;
        float _fade = -1f, _scale = -1f, _ringOpacity = -1f;

        static readonly int IdBaseColor = MaterialLibrary.BaseColor, IdBaseMap = MaterialLibrary.BaseMap;

        /// <summary>The whole panel's scale (1 = the palm menu, 0.32 x 0.24 m; the Done / Reset buttons use the same canvas
        /// at MenuStyle.ArrangeScale). The open / close animation multiplies it.</summary>
        public readonly float BaseScale;

        public MenuView(App app, Transform root, bool paint, float baseScale = 1f, bool withRing = true)
        {
            Root = root;
            BaseScale = baseScale;
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root, false);
            Visual = visual.transform;

            _faceMesh = MenuMath.BuildCurvedPlane("MenuFace", MenuMath.W, MenuMath.H);
            _backMesh = MenuMath.BuildCurvedPlane("MenuBack", MenuMath.BackW, MenuMath.BackH);
            _faceMat = Template(app, FaceTemplate, 2999);
            _backMat = Template(app, BackTemplate, 2998);

            var back = new GameObject("Back");
            back.transform.SetParent(Visual, false);
            back.transform.localPosition = new Vector3(0f, 0f, MenuMath.BackZ);
            back.AddComponent<MeshFilter>().sharedMesh = _backMesh;
            Back = Prepare(back.AddComponent<MeshRenderer>(), _backMat);

            var face = new GameObject("Face");
            face.transform.SetParent(Visual, false);
            face.AddComponent<MeshFilter>().sharedMesh = _faceMesh;
            Face = Prepare(face.AddComponent<MeshRenderer>(), _faceMat);
            Collider = face.AddComponent<MeshCollider>();
            Collider.sharedMesh = _faceMesh;
            // The desktop click target lives on the play-surface layer (ARCHITECTURE.md sections 0 and 15.6), so the
            // desktop camera's look-drag block (dragBlockMask) and the nearest-PlaySurface-hit rule see it.
            face.layer = Layers.PlaySurface;

            // The summon ring lives in the world (not under the panel).
            if (withRing)
            {
                var ring = new GameObject("MenuRing");
                ring.transform.SetParent(app.WorldRoot, false);
                _ringMesh = MenuMath.BuildRing();
                ring.AddComponent<MeshFilter>().sharedMesh = _ringMesh;
                _ringMat = app.Materials.Additive(null, RingColor);
                _ring = Prepare(ring.AddComponent<MeshRenderer>(), _ringMat);
                ring.SetActive(false);
                Ring = ring.transform;
            }

            if (paint)
            {
                string n = withRing ? "Menu" : root.name;
                FaceTexture = MakeTexture(n + "FaceCanvas", MenuConstants.CanvasW, MenuConstants.CanvasH);
                BackTexture = MakeTexture(n + "BackCanvas", MenuStyle.BackW, MenuStyle.BackH);
                _faceMat.SetTexture(IdBaseMap, FaceTexture);
                _backMat.SetTexture(IdBaseMap, BackTexture);
                var textRoot = new GameObject("MenuText");
                textRoot.transform.SetParent(app.WorldRoot, false);
                textRoot.transform.localPosition = new Vector3(0f, -500f, 0f);   // never near a camera
                _textRoot = textRoot.transform;
                Canvas = new GpuMenuCanvas(Template(app, ShapeTemplate, -1), _textRoot);
            }
            visual.SetActive(false);
            Apply(1f, 0f);
        }

        /// <summary>A clone of a module template; when render-setup has not made it yet, the shader itself (editor) with the
        /// template's render queue, so the panel still looks right in a fresh checkout.</summary>
        Material Template(App app, string name, int queue)
        {
            if (app.Materials.HasTemplate(name)) return app.Materials.Clone(name);
            string shader = name == ShapeTemplate ? MenuMaterials.ShapeShader : MenuMaterials.FaceShader;
            Shader sh = Shader.Find(shader);
            if (sh == null) return app.Materials.Clone(name);   // logs the missing template and falls back
            Debug.LogWarning($"[Menu] template '{name}' missing (run Tools/unity.sh render-setup); using {shader} directly");
            var m = new Material(sh) { name = name + " (shader)" };
            if (queue > 0) m.renderQueue = queue;
            _ownMaterials.Add(m);
            return m;
        }

        static MeshRenderer Prepare(MeshRenderer r, Material m)
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return r;
        }

        static RenderTexture MakeTexture(string name, int w, int h)
        {
            // Linear (non-sRGB) storage: the canvas keeps sRGB numbers and blends them in gamma space.
            var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = false, useMipMap = true, autoGenerateMips = false, msaaSamples = 1,
            };
            var rt = new RenderTexture(desc) { name = name, anisoLevel = 4, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            return rt;
        }

        public bool VisualActive => Visual.gameObject.activeSelf;

        public void SetVisible(bool on)
        {
            if (Visual.gameObject.activeSelf != on) Visual.gameObject.SetActive(on);
        }

        public void SetCollider(bool on)
        {
            if (Collider.enabled != on) Collider.enabled = on;
        }

        /// <summary>Scale (0.9 + 0.1k while opening / closing) and opacity k of both meshes.</summary>
        public void Apply(float scale, float fade)
        {
            if (scale != _scale)
            {
                _scale = scale;
                float s = scale * BaseScale;
                Visual.localScale = new Vector3(s, s, s);
            }
            if (fade != _fade)
            {
                _fade = fade;
                var c = new Color(1f, 1f, 1f, fade);
                _faceMat.SetColor(IdBaseColor, c);
                _backMat.SetColor(IdBaseColor, c);
            }
        }

        /// <summary>The charging ring: at the palm, facing the head, scale .5 + .5k, opacity .15 + .65k.</summary>
        public void SetRing(bool visible, Vector3 position, Quaternion rotation, float scale, float opacity)
        {
            if (Ring == null) return;
            GameObject go = Ring.gameObject;
            if (go.activeSelf != visible) go.SetActive(visible);
            if (!visible) return;
            Ring.SetPositionAndRotation(position, rotation);
            Ring.localScale = new Vector3(scale, scale, scale);
            if (opacity != _ringOpacity)
            {
                _ringOpacity = opacity;
                _ringMat.SetColor(IdBaseColor, new Color(RingColor.r, RingColor.g, RingColor.b, opacity));
            }
        }

        static readonly int IdHoverRect0 = Shader.PropertyToID("_HoverRect0"), IdHoverParam0 = Shader.PropertyToID("_HoverParam0"),
            IdHoverRect1 = Shader.PropertyToID("_HoverRect1"), IdHoverParam1 = Shader.PropertyToID("_HoverParam1"),
            IdHoverClip = Shader.PropertyToID("_HoverClip");
        Vector4 _hoverRect0, _hoverParam0, _hoverRect1, _hoverParam1, _hoverClip = new Vector4(float.NaN, 0f, 0f, 0f);

        /// <summary>The row highlights as face-material uniforms (ARCHITECTURE.md 15.6: hover never repaints the canvas).
        /// Slot 0 = the hovered row (k), slot 1 = the previous row fading out (1 - k); k = 0 hides a slot. Only changed
        /// values reach the material, so a resting hover costs nothing.</summary>
        public void SetHover(MenuController ctl, double nowMs)
        {
            float k = ctl.HoverRow >= 0 || ctl.HoverPrev >= 0 ? ctl.HoverK(nowMs) : 0f;
            HoverSlot(ctl, ctl.HoverRow, k, out Vector4 r0, out Vector4 p0, out Vector2 c0);
            HoverSlot(ctl, ctl.HoverPrev, 1f - k, out Vector4 r1, out Vector4 p1, out Vector2 c1);
            var clip = new Vector4(c0.x, c0.y, c1.x, c1.y);
            // Exact comparisons (Vector4.Equals): Unity's == treats values within 1e-5 as equal, which left a fade resting
            // at k = 0.999996 when a frame landed that close to the end (a timing-dependent test failure).
            if (!r0.Equals(_hoverRect0)) { _hoverRect0 = r0; _faceMat.SetVector(IdHoverRect0, r0); }
            if (!p0.Equals(_hoverParam0)) { _hoverParam0 = p0; _faceMat.SetVector(IdHoverParam0, p0); }
            if (!r1.Equals(_hoverRect1)) { _hoverRect1 = r1; _faceMat.SetVector(IdHoverRect1, r1); }
            if (!p1.Equals(_hoverParam1)) { _hoverParam1 = p1; _faceMat.SetVector(IdHoverParam1, p1); }
            if (!clip.Equals(_hoverClip)) { _hoverClip = clip; _faceMat.SetVector(IdHoverClip, clip); }
        }

        static void HoverSlot(MenuController ctl, int row, float k, out Vector4 rect, out Vector4 param, out Vector2 clip)
        {
            if (k > 0f && MenuPainter.HoverShape(ctl, row, out rect, out Vector2 shape, out clip))
            {
                param = new Vector4(shape.x, k, shape.y, 0f);
                return;
            }
            rect = Vector4.zero;
            param = Vector4.zero;
            clip = new Vector2(-1e5f, 1e5f);
        }

        /// <summary>The face material's hover uniforms as last set (tests).</summary>
        public Vector4 HoverParam(int slot) => slot == 0 ? _hoverParam0 : _hoverParam1;
        public Vector4 HoverRect(int slot) => slot == 0 ? _hoverRect0 : _hoverRect1;

        /// <summary>Render textures can be lost (device reset): then everything is painted again.</summary>
        public bool TexturesLost => FaceTexture != null && (!FaceTexture.IsCreated() || !BackTexture.IsCreated());

        /// <summary>Paint the face (and the back plate the first time or after a loss).</summary>
        public void Paint(MenuController ctl, double nowMs)
        {
            if (Canvas == null) return;
            if (TexturesLost)
            {
                FaceTexture.Create();
                BackTexture.Create();
                _backPainted = false;
            }
            if (!_backPainted)
            {
                Canvas.Begin(BackTexture);
                MenuPainter.PaintBack(Canvas);
                Canvas.End(BackTexture);
                _backPainted = true;
            }
            Canvas.Begin(FaceTexture);
            MenuPainter.Paint(Canvas, ctl, nowMs);
            Canvas.End(FaceTexture);
        }

        public void Dispose()
        {
            Canvas?.Dispose();
            if (FaceTexture != null) { FaceTexture.Release(); Object.Destroy(FaceTexture); }
            if (BackTexture != null) { BackTexture.Release(); Object.Destroy(BackTexture); }
            if (_faceMesh != null) Object.Destroy(_faceMesh);
            if (_backMesh != null) Object.Destroy(_backMesh);
            if (_ringMesh != null) Object.Destroy(_ringMesh);
            if (Ring != null) Object.Destroy(Ring.gameObject);
            if (_textRoot != null) Object.Destroy(_textRoot.gameObject);
            for (int i = 0; i < _ownMaterials.Count; i++) if (_ownMaterials[i] != null) Object.Destroy(_ownMaterials[i]);
            _ownMaterials.Clear();
        }
    }
}
