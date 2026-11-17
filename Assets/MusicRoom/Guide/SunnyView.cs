// Sunny in the scene (Docs/08-ux-lessons-menu-guide.md 3.1-3.2): the face sphere, the corona, the speech bubble with its
// TextMeshPro caption, the cyan ghost hand with ring / ghost panel / dotted arrow, and the warm light. Apply() copies one
// frame of Sunny's state onto them, touching materials and text only when a value changed (no allocation per frame).
// The root stays active even while Sunny is hidden: the Guide audio source is parented to it (a deactivated source
// would cut the closing chirp and restart its voices).
using MusicRoom.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Guide
{
    public sealed class SunnyView
    {
        // Docs/08-ux-lessons-menu-guide.md 3.1 (local +z points away from the player).
        public const float CoronaSize = GuideTiming.Radius * 5.2f;             // 0.234 m
        public static readonly Vector3 CoronaLocal = new Vector3(0f, 0f, GuideTiming.Radius * 0.6f);   // 0.027 m behind the body
        public static readonly Vector2 BubbleSize = new Vector2(0.26f, 0.115f);
        public static readonly Vector3 BubbleLocal = new Vector3(0.26f * 0.32f, GuideTiming.Radius + 0.115f * 0.62f, -0.01f);
        public static readonly Color LightColor = SunnyFace.Hex("#ffb866");
        public const float LightRange = 1.4f;
        /// <summary>Physical point light (decay 2) to URP: intensity / pi (spec 07 section 6.1). Tune on device (UNVERIFIED).</summary>
        public const float LightScale = 1f / Mathf.PI;
        /// <summary>First five: a pale, warm, translucent hand (alpha-blended), not the earlier additive cyan glow #5fd8ff (the
        /// user's "no neon or glow UI"; sunny-tour.md open question 1).</summary>
        public static readonly Color DemoColor = SunnyFace.Hex("#f3ead8");
        /// <summary>The ghost hand's opacity on top of each part's (the hand 0.5: 0.375 in all).</summary>
        public const float DemoAlpha = 0.75f;
        public static readonly Color TextColor = SunnyFace.Hex("#3a2114");

        // The caption, in bubble texture px (768 x 340 over 0.26 x 0.115 m): one TMP local unit = 10 px, so fontSize = px.
        public const float PxPerUnit = 10f;
        public const float UnitToMetre = 0.26f / SunnyBubbleArt.W * PxPerUnit;
        public const int FontMaxPx = 54, FontMinPx = 30, FontStepPx = 4, MaxLines = 2;
        public const float TextMaxWidthPx = SunnyBubbleArt.X1 - SunnyBubbleArt.X0 - 70f;                   // 678
        public const float TextBoxHeightPx = SunnyBubbleArt.Y1 - SunnyBubbleArt.Y0;                        // 286
        public const float TextCentreYPx = (SunnyBubbleArt.Y0 + SunnyBubbleArt.Y1) * 0.5f;                 // 153
        public const float LineHeightEm = 1.18f;
        public const string FontResource = "Fonts & Materials/LiberationSans SDF";

        static readonly int FaceColorId = Shader.PropertyToID("_FaceColor");
        static readonly int FaceDilateId = Shader.PropertyToID("_FaceDilate");
        /// <summary>The caption is designed in a system UI font's heavy bold; LiberationSans' SDF bold (_WeightBold .75)
        /// reads lighter at Sunny's distance, so the caption's own material dilates the face a little more.</summary>
        public const float CaptionDilate = 0.15f;

        public Transform Root { get; }
        public Transform Visual { get; }
        public Transform Body { get; }
        public Transform Corona { get; }
        public Transform Bubble { get; }
        public Transform Caption { get; }
        public Transform Demo { get; }
        public Transform Hand { get; }
        public Transform Ring { get; }
        public Transform Ghost { get; }
        public Transform Row { get; }
        public Transform Arrow { get; }
        /// <summary>Finger pivots: index, middle, ring, little, thumb (fingers[0..4]).</summary>
        public readonly Transform[] Pivots = new Transform[5];
        public Light Light { get; }
        public bool LightEnabled { get; set; }

        public Material BodyMaterial { get; }
        public Material CoronaMaterial { get; }
        public Material BubbleMaterial { get; }
        public Material TextMaterial { get; }
        /// <summary>The merged solid demo parts (hand, ring, row plate, cone): opacity per part in the vertex alpha.</summary>
        public Material DemoMaterial { get; }
        public Material GhostMaterial { get; }
        public Material DashMaterial { get; }
        public SunnyDemoMesh DemoMesh { get; }
        public Transform Cone { get; }

        public int FaceIndex { get; private set; } = -1;
        public float BubbleFontPx { get; private set; }
        public int BubbleLines { get; private set; }
        public string CaptionText => _tmp != null ? _tmp.text : "";
        public bool HasText => _tmp != null;

        readonly Texture2D[] _faces = new Texture2D[SunnyFace.StateCount];
        readonly TextMeshPro _tmp;
        bool _visible, _demoVisible, _ring, _ghost, _arrow, _bubbleOn, _lightOn, _wide;
        int _lineVersion = -1;
        float _scale = -1f, _bodyA = -1f, _bubbleA = -1f, _glow = -1f;

        public SunnyView(Transform world, MaterialLibrary materials, bool lightEnabled)
        {
            LightEnabled = lightEnabled;
            Root = new GameObject("Sunny").transform;
            Root.SetParent(world, false);
            Root.localPosition = Sunny.InitialBase;
            // Built active and hidden at the end: TextMeshPro only initialises (Awake) on an active object, and an
            // uninitialised caption measures 0 lines.
            Visual = Child("Visual", Root, Vector3.zero);

            for (int i = 0; i < _faces.Length; i++) _faces[i] = GuideTextures.Face(i);
            BodyMaterial = Clone(materials, GuideMaterialTemplates.Body, GuideMaterialTemplates.QueueBody);
            BodyMaterial.SetTexture(MaterialLibrary.BaseMap, _faces[0]);
            CoronaMaterial = Clone(materials, GuideMaterialTemplates.Corona, GuideMaterialTemplates.QueueCorona);
            BubbleMaterial = Clone(materials, GuideMaterialTemplates.Bubble, GuideMaterialTemplates.QueueBubble);
            BubbleMaterial.SetTexture(MaterialLibrary.BaseMap, GuideTextures.Bubble());
            DemoMaterial = Additive(materials, GuideMaterialTemplates.Demo, 1f);   // each part's opacity rides in its vertex alpha
            DemoMaterial.SetFloat(GuideMaterialTemplates.ZWriteId, 1f);   // first five: one surface (a finger behind the palm adds nothing)
            GhostMaterial = Additive(materials, GuideMaterialTemplates.DemoDouble, SunnyDemo.GhostOpacity);
            DashMaterial = Additive(materials, GuideMaterialTemplates.DemoDouble, SunnyDemo.DashOpacity);

            Body = Part("Body", Visual, Vector3.zero, SunnyMeshes.Body, BodyMaterial);
            Corona = Part("Corona", Visual, CoronaLocal, SunnyMeshes.Corona, CoronaMaterial);
            Bubble = Part("Bubble", Visual, BubbleLocal, SunnyMeshes.Bubble, BubbleMaterial);

            // The caption: centred on the bubble's text box (y 10..296 of 340 px), 678 px wide, bold, at most two lines.
            Caption = Child("Caption", Bubble, new Vector3(0f, (SunnyBubbleArt.H * 0.5f - TextCentreYPx) * (BubbleSize.y / SunnyBubbleArt.H), -0.0005f));
            Caption.localScale = Vector3.one * UnitToMetre;
            _tmp = Caption.gameObject.AddComponent<TextMeshPro>();
            TextMaterial = SetupText(_tmp, materials);
            Bubble.gameObject.SetActive(false);

            // The demo: the solid parts are bare anchors posed like separate meshes, drawn by one merged mesh on the
            // Demo group (SunnyDemoMesh); only the two line meshes (ghost panel, dashes) keep their own renderers, and
            // they are never shown together, so a demo costs at most two draws.
            Demo = Child("Demo", Visual, Vector3.zero);
            Demo.gameObject.SetActive(false);
            Hand = Child("Hand", Demo, SunnyDemo.HandRoot);
            var anchors = new Transform[SunnyDemoMesh.PartCount];
            var shapes = new Mesh[SunnyDemoMesh.PartCount];
            anchors[SunnyDemoMesh.Palm] = Child("Palm", Hand, Vector3.zero);
            shapes[SunnyDemoMesh.Palm] = SunnyMeshes.Palm;
            for (int i = 0; i < 4; i++)
            {
                Pivots[i] = Child("Finger" + i, Hand, new Vector3(SunnyDemo.FingerX[i], SunnyDemo.FingerPivotY, 0f));
                anchors[SunnyDemoMesh.Finger0 + i] = Child("Segment", Pivots[i], new Vector3(0f, SunnyDemo.FingerLen[i] * 0.5f, 0f));
                shapes[SunnyDemoMesh.Finger0 + i] = SunnyMeshes.Finger(i);
            }
            Pivots[4] = Child("Thumb", Hand, SunnyDemo.ThumbPivot);
            Pivots[4].localRotation = SunnyDemo.ThumbRotation(0f);
            anchors[SunnyDemoMesh.Thumb] = Child("Segment", Pivots[4], new Vector3(0f, SunnyDemo.ThumbLen * 0.5f, 0f));
            shapes[SunnyDemoMesh.Thumb] = SunnyMeshes.Thumb;
            Ring = Child("Ring", Hand, SunnyDemo.RingLocal);
            Ring.gameObject.SetActive(false);
            anchors[SunnyDemoMesh.Ring] = Ring;
            shapes[SunnyDemoMesh.Ring] = SunnyMeshes.ChargeRing;
            Ghost = Part("Ghost", Demo, SunnyDemo.GhostPos, SunnyMeshes.GhostPanel, GhostMaterial);
            Ghost.gameObject.SetActive(false);
            Row = Child("Row", Ghost, SunnyDemo.RowPlatePos);
            anchors[SunnyDemoMesh.Row] = Row;
            shapes[SunnyDemoMesh.Row] = SunnyMeshes.RowPlate;
            Arrow = Child("Arrow", Demo, SunnyDemo.ArrowPos);
            Arrow.gameObject.SetActive(false);
            Part("Dashes", Arrow, Vector3.zero, SunnyMeshes.Dashes, DashMaterial);
            Cone = Child("Cone", Arrow, new Vector3(SunnyDemo.ConeX, 0f, 0f));
            Cone.localRotation = Quaternion.Euler(0f, 0f, 90f);   // the cone's tip (+y) turned to -x, the sweep's direction
            anchors[SunnyDemoMesh.Cone] = Cone;
            shapes[SunnyDemoMesh.Cone] = SunnyMeshes.Cone;
            DemoMesh = new SunnyDemoMesh(Demo, anchors, shapes);
            Demo.gameObject.AddComponent<MeshFilter>().sharedMesh = DemoMesh.Mesh;
            var demoRenderer = Demo.gameObject.AddComponent<MeshRenderer>();
            demoRenderer.sharedMaterial = DemoMaterial;
            demoRenderer.shadowCastingMode = ShadowCastingMode.Off;
            demoRenderer.receiveShadows = false;
            demoRenderer.lightProbeUsage = LightProbeUsage.Off;
            demoRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // A small warm light that lights the hands and keys nearby; enabled only while Sunny is visible (a disabled
            // Unity light costs nothing and enabling it recompiles nothing).
            var lightGo = new GameObject("SunnyLight");
            lightGo.transform.SetParent(world, false);
            Light = lightGo.AddComponent<Light>();
            Light.type = LightType.Point;
            Light.color = LightColor;
            Light.range = LightRange;
            Light.intensity = 0f;
            Light.shadows = LightShadows.None;
            Light.enabled = false;
            Visual.gameObject.SetActive(false);
        }

        static Transform Child(string name, Transform parent, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        static Transform Part(string name, Transform parent, Vector3 local, Mesh mesh, Material material)
        {
            Transform t = Child(name, parent, local);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return t;
        }

        static Material Clone(MaterialLibrary materials, string template, int queue)
        {
            Material m = materials.Clone(template);
            m.renderQueue = queue;   // keeps Sunny's draw order even on a fallback material
            return m;
        }

        static Material Additive(MaterialLibrary materials, string template, float opacity)
        {
            Material m = Clone(materials, template, GuideMaterialTemplates.QueueDemo);
            Color c = DemoColor;
            c.a = opacity * DemoAlpha;
            m.SetColor(MaterialLibrary.BaseColor, c);
            // First five: alpha-blended (normal blending), not added: a calm, pale hand over the room and passthrough.
            m.SetFloat(GuideMaterialTemplates.AdditiveId, 0f);
            m.SetFloat(GuideMaterialTemplates.SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat(GuideMaterialTemplates.DstBlendId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat(GuideMaterialTemplates.SrcBlendAId, (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat(GuideMaterialTemplates.DstBlendAId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            return m;
        }

        Material SetupText(TextMeshPro tmp, MaterialLibrary materials)
        {
            TMP_FontAsset font = TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset : Resources.Load<TMP_FontAsset>(FontResource);
            if (font == null)
            {
                Debug.LogWarning("[Guide] no TextMeshPro font: the bubble shows no caption");
                return null;
            }
            tmp.font = font;
            // The overlay SDF material is a clone of the module's template (so the shader is in the build) with the font's
            // atlas and SDF parameters copied in.
            Material m = materials.Clone(GuideMaterialTemplates.BubbleText);
            if (m.shader == null || m.shader.name != GuideMaterialTemplates.TextShader)
            {
                Shader overlay = Shader.Find(GuideMaterialTemplates.TextShader);
                if (overlay != null) m.shader = overlay;
            }
            m.CopyPropertiesFromMaterial(font.material);
            m.SetFloat(FaceDilateId, CaptionDilate);
            m.renderQueue = GuideMaterialTemplates.QueueText;
            tmp.fontSharedMaterial = m;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.richText = false;
            tmp.enableAutoSizing = false;
            tmp.isOrthographic = false;
            tmp.color = TextColor;
            tmp.fontSize = FontMaxPx;
            // The bubble's line height is 1.18 x the font size; TMP adds lineSpacing in 1/100 em to the font's own line height.
            var face = font.faceInfo;
            if (face.pointSize > 0f) tmp.lineSpacing = (LineHeightEm - face.lineHeight / face.pointSize) * 100f;
            tmp.rectTransform.sizeDelta = new Vector2(TextMaxWidthPx / PxPerUnit, TextBoxHeightPx / PxPerUnit);
            tmp.text = "";
            var r = tmp.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            return m;
        }

        /// <summary>The bubble: the font steps from 54 down to 30 px by 4 until the caption wraps into two lines or fewer.</summary>
        public void Fit(string text)
        {
            if (_tmp == null) return;
            _tmp.text = text;
            int size = FontMaxPx;
            int lines = 0;
            for (; size >= FontMinPx; size -= FontStepPx)
            {
                _tmp.fontSize = size;
                _tmp.ForceMeshUpdate(true, true);
                lines = _tmp.textInfo.lineCount;
                if (lines <= MaxLines) break;
            }
            if (size < FontMinPx) size = FontMinPx;   // nothing fitted: keep the last font tried (30)
            BubbleFontPx = size;
            BubbleLines = lines;
        }

        /// <summary>Copies one frame of Sunny onto the scene.</summary>
        public void Apply(Sunny s)
        {
            bool visible = s.Visible;
            if (visible != _visible)
            {
                _visible = visible;
                Visual.gameObject.SetActive(visible);
            }
            bool lightOn = visible && LightEnabled;
            if (lightOn != _lightOn)
            {
                _lightOn = lightOn;
                Light.enabled = lightOn;
            }
            if (!visible) return;

            Root.SetPositionAndRotation(s.Position, s.Rotation);
            if (s.Scale != _scale)
            {
                _scale = s.Scale;
                Root.localScale = new Vector3(_scale, _scale, _scale);
            }

            int face = SunnyFace.StateIndex(s.Mood == SunnyMood.Happy, s.EyesShut, s.MouthOpen);
            if (face != FaceIndex)
            {
                FaceIndex = face;
                BodyMaterial.SetTexture(MaterialLibrary.BaseMap, _faces[face]);
            }
            if (s.BodyOpacity != _bodyA)
            {
                _bodyA = s.BodyOpacity;
                BodyMaterial.SetColor(MaterialLibrary.BaseColor, new Color(1f, 1f, 1f, _bodyA));
            }
            CoronaMaterial.SetFloat(GuideMaterialTemplates.TimeId, s.Time);
            if (s.Glow != _glow)
            {
                _glow = s.Glow;
                CoronaMaterial.SetFloat(GuideMaterialTemplates.GlowId, _glow);
            }

            // The bubble keeps the last line until hush.
            if (s.BubbleVisible != _bubbleOn)
            {
                _bubbleOn = s.BubbleVisible;
                Bubble.gameObject.SetActive(_bubbleOn);
            }
            if (s.LineVersion != _lineVersion)
            {
                _lineVersion = s.LineVersion;
                Fit(s.Line);
            }
            if (s.BubbleOpacity != _bubbleA)
            {
                _bubbleA = s.BubbleOpacity;
                BubbleMaterial.SetColor(MaterialLibrary.BaseColor, new Color(1f, 1f, 1f, _bubbleA));
                if (TextMaterial != null) TextMaterial.SetColor(FaceColorId, new Color(1f, 1f, 1f, _bubbleA));
            }

            if (s.Demo.Visible) DemoMesh.Eye = s.TryEye(out Vector3 eye) ? eye : (Vector3?)null;   // first five: the soft edges
            ApplyDemo(s.Demo);

            Light.transform.position = s.Position;
            Light.intensity = s.LightIntensity * LightScale;
        }

        void ApplyDemo(in DemoPose p)
        {
            if (p.Visible != _demoVisible)
            {
                _demoVisible = p.Visible;
                Demo.gameObject.SetActive(p.Visible);
            }
            if (!p.Visible) return;
            if (p.Wide != _wide)
            {
                _wide = p.Wide;
                DemoMesh.Mesh.bounds = p.Wide ? SunnyDemoMesh.WideBounds : SunnyDemoMesh.LocalBounds;
            }
            Hand.localPosition = p.HandPosition;
            Hand.localRotation = p.HandRotation;
            Pivots[0].localRotation = SunnyDemo.FingerRotation(p.IndexCurl);
            for (int i = 1; i < 4; i++) Pivots[i].localRotation = SunnyDemo.FingerRotation(p.FingerCurl);
            Pivots[4].localRotation = SunnyDemo.ThumbRotation(p.ThumbCurl);
            if (p.Ring != _ring)
            {
                _ring = p.Ring;
                Ring.gameObject.SetActive(p.Ring);
            }
            if (p.Ring) Ring.localScale = new Vector3(p.RingScale, p.RingScale, p.RingScale);
            if (p.Ghost != _ghost)
            {
                _ghost = p.Ghost;
                Ghost.gameObject.SetActive(p.Ghost);
            }
            if (p.Arrow != _arrow)
            {
                _arrow = p.Arrow;
                Arrow.gameObject.SetActive(p.Arrow);
            }
            // The cone fades with the hand (one shared opacity); hidden parts write alpha 0.
            DemoMesh.Write(p.Opacity, p.Ring ? p.RingOpacity : 0f, p.Ghost ? p.RowOpacity : 0f, p.Arrow ? p.Opacity : 0f);
        }

        public void Destroy()
        {
            if (Light != null) Object.Destroy(Light.gameObject);
            if (Root != null) Object.Destroy(Root.gameObject);
        }
    }
}
