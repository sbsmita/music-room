// The mixed-reality hints in the headset. An immersive session shows no flat text line,
// so they float as a small text card 0.65 m ahead, a little above the gaze (so the table or wall the
// player aims at stays in sight), and follow the head lazily: the card stays put until the gaze moves more than
// 22 degrees from it, then glides back. A card goes away ShowSeconds after it appeared, except a sticky one (the placing
// hint, which stays for as long as placing lasts). Text only changes on a new hint, so TextMeshPro re-meshes rarely; nothing
// allocates per frame.
using TMPro;
using UnityEngine;

namespace MusicRoom.MR
{
    public sealed class MrHint
    {
        const float Width = 0.34f, Pad = 0.018f, FontSize = 0.12f;
        static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.09f, 1f);
        const float PanelAlpha = 0.62f;

        readonly GameObject _go;
        readonly TextMeshPro _text;
        readonly Transform _panel;
        readonly Material _panelMaterial;
        readonly Mesh _panelMesh;
        string _shown;
        bool _placed;
        Vector3 _target;
        Quaternion _targetRot;

        public string Text => _shown;
        public bool Visible => _go.activeSelf;
        public Transform Transform => _go.transform;

        public MrHint(Transform parent, MrMaterials materials)
        {
            _go = new GameObject("MR hint");
            _go.transform.SetParent(parent, false);

            _panelMaterial = materials.Unlit(MrMaterials.Panel, PanelColor, PanelAlpha);
            _panelMesh = MrMeshes.Rect(1f, 1f);
            _panel = MrMeshes.AddPart(_go.transform, "Panel", _panelMesh, _panelMaterial).transform;
            _panel.localPosition = new Vector3(0f, 0f, 0.002f);   // just behind the text (the card faces -z)

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(_go.transform, false);
            _text = textGo.AddComponent<TextMeshPro>();
            _text.fontSize = FontSize;
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.color = new Color(1f, 1f, 1f, 0.94f);
            _text.rectTransform.sizeDelta = new Vector2(Width, 0.3f);
            _text.sortingOrder = 1;
            // The panel draws in Transparent+8 (MrMaterialTemplates); the font's own material draws at Transparent, so
            // the panel covered the words and the card showed as a black box on the headset. The text gets its own
            // material one queue after the panel.
            _text.fontMaterial.renderQueue = _panelMaterial != null ? _panelMaterial.renderQueue + 1 : (int)UnityEngine.Rendering.RenderQueue.Transparent + 9;
            var tr = _text.GetComponent<MeshRenderer>();
            if (tr != null) tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _go.SetActive(false);
        }

        /// <summary>The card in view stays until its text changes (no ShowSeconds limit).</summary>
        public bool Sticky { get; private set; }

        /// <summary>Shows <paramref name="text"/> (null hides). A new text re-centres the card in front of the head.
        /// <paramref name="sticky"/>: the card stays until the text changes (the placing hint) instead of ShowSeconds.</summary>
        public void Set(string text, Transform head, bool sticky = false)
        {
            if (text == _shown)
            {
                // Made sticky again while its card already went away: show it once more.
                if (sticky && !Sticky && !string.IsNullOrEmpty(text) && !_go.activeSelf) { _age = 0f; _placed = false; _go.SetActive(true); }
                Sticky = sticky;
                return;
            }
            Sticky = sticky;
            _shown = text;
            if (string.IsNullOrEmpty(text))
            {
                _go.SetActive(false);
                return;
            }
            _text.text = text;
            _text.ForceMeshUpdate();
            Vector2 size = _text.GetRenderedValues(false);
            float h = Mathf.Max(0.04f, size.y) + 2f * Pad;
            _panel.localScale = new Vector3(Width + 2f * Pad, h, 1f);
            _go.SetActive(true);
            _placed = false;
            _age = 0f;
            Follow(head, 1f);
        }

        /// <summary>A hint stays this long, then the card goes away (it stayed for the whole session on the headset).</summary>
        public const float ShowSeconds = 9f;
        float _age;

        /// <summary>Lazy follow of the head (visual only); hides the card ShowSeconds after its text appeared (a sticky one
        /// stays).</summary>
        public void Follow(Transform head, float dt)
        {
            if (!_go.activeSelf || head == null) return;
            _age += dt;
            if (!Sticky && _age > ShowSeconds)
            {
                _go.SetActive(false);
                return;
            }
            Vector3 gaze = head.forward;
            Vector3 flat = MrUtil.Flat(gaze, Vector3.forward);
            float elevation = Mathf.Asin(Mathf.Clamp(gaze.y, -1f, 1f)) * Mathf.Rad2Deg;
            elevation = Mathf.Clamp(elevation + MrConstants.HintAboveGazeDeg, MrConstants.HintMinElevationDeg, MrConstants.HintMaxElevationDeg);
            Quaternion want = Quaternion.LookRotation(flat, Vector3.up) * Quaternion.Euler(-elevation, 0f, 0f);
            Vector3 toCard = _go.transform.position - head.position;
            float off = Vector3.Angle(want * Vector3.forward, toCard);
            if (!_placed || off > MrConstants.HintFollowDeg)
            {
                _target = head.position + want * Vector3.forward * MrConstants.HintDistance;
                _targetRot = want;
                // A big turn (or a first show) puts the card in front at once: gliding round from behind would show
                // its mirrored back.
                if (!_placed || off > MrConstants.HintSnapDeg) _go.transform.SetPositionAndRotation(_target, _targetRot);
                _placed = true;
            }
            float k = 1f - Mathf.Exp(-dt / 0.25f);
            _go.transform.SetPositionAndRotation(Vector3.Lerp(_go.transform.position, _target, k), Quaternion.Slerp(_go.transform.rotation, _targetRot, k));
        }

        public void Destroy()
        {
            MrUtil.Destroy(_panelMaterial);
            MrUtil.Destroy(_panelMesh);
            MrUtil.Destroy(_go);
        }
    }
}
