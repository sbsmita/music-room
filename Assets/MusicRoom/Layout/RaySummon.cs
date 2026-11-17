// Point and pinch from afar (12-new-instruments/platform.md 4.4 c): in the VR room, the hand's system pointer pose
// (HandFrame.pointer, valid only with InputStateValid, as arrange mode's ray grab) picks a displayed instrument by the cone
// pick (StowPlan.Pick: 6 degrees, 6 m, ties within 0.5 degrees to the nearer). After 0.25 s on the same target a small
// cream card with its name (TextMeshPro, dark ink, 8 cm wide) floats 12 cm above its pick point facing the head, and the
// ray shows as a thin pale line (#f3e6cf, alpha 0.35, 3 mm) while it points at a target: no glow, no outline. The grab
// pinch held 0.35 s on the hovered target brings it (ILayoutService.Bring(kind, Ray)); while holding, the target lifts
// 2 cm and, when it would send a member back, that member (ReplaceCandidate) lifts 2 cm too; releasing before 0.35 s
// cancels and both settle back. A summon pinch is consumed: it never starts an in-play grab, a stick pick-up, the swivel,
// the two-hand summon or MR placing (Owns).
//
// Guards (the grab's): presenting, not mixed reality, not arrange mode, no rig held, not within 1 s of playing, not with
// the palm toward the headset (the system gesture), not with the hand inside a play zone or the menu zone
// (LayoutModule.InPlayZone: the at-seat instruments' play zones and surfaces, the shown menu). Draws: one mesh (the card
// and the ray, vertex coloured) and the text: 2 at most. Allocation-free per frame; the visuals exist only when the app renders (state is kept headless for tests).
using MusicRoom.Core;
using MusicRoom.Hands;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Layout
{
    public sealed class RaySummon
    {
        public const float HoverSeconds = 0.25f, HoldSeconds = 0.35f, PreviewLift = 0.02f, LiftSeconds = 0.08f;
        public const float CardWidth = 0.08f, CardHeight = 0.026f, CardAbove = 0.12f, RayWidth = 0.003f, FontSize = 0.13f;
        public static readonly Color RayColour = new Color(0xf3 / 255f, 0xe6 / 255f, 0xcf / 255f, 0.35f);
        public static readonly Color CardColour = new Color32(0xf4, 0xec, 0xdc, 0xff);
        public static readonly Color Ink = new Color32(0x2b, 0x25, 0x20, 0xff);
        const int MaxTargets = InstrumentIds.KindCount;

        // ---- state (tests read it, headless too)
        /// <summary>The hand's current pick (null: none) and how long it has been on it.</summary>
        readonly InstrumentKind?[] _pick = new InstrumentKind?[2];
        readonly float[] _pickT = new float[2];
        readonly bool[] _owns = new bool[2];
        int _holdHand = -1;
        float _holdT, _liftK;
        InstrumentKind _holdKind;
        InstrumentKind? _holdReplace;
        Vector3 _liftBase, _replaceBase;
        Rig _liftRig, _replaceRig;

        public bool CardVisible { get; private set; }
        public bool RayVisible { get; private set; }
        public string CardText { get; private set; }
        public Vector3 CardPosition { get; private set; }
        public Vector3 RayFrom { get; private set; }
        public Vector3 RayTo { get; private set; }
        /// <summary>The instrument the shown card names (null when none).</summary>
        public InstrumentKind? Hovered { get; private set; }
        public bool Holding => _holdHand >= 0;
        public InstrumentKind? HoldTarget => _holdHand >= 0 ? _holdKind : (InstrumentKind?)null;
        public InstrumentKind? PreviewReplace => _holdHand >= 0 ? _holdReplace : null;
        public int Summons { get; private set; }
        public int Cancels { get; private set; }
        /// <summary>The hand's pinch belongs to the summon (from its start until it opens).</summary>
        public bool Owns(int hand) => hand >= 0 && hand < _owns.Length && _owns[hand];
        public InstrumentKind? PickOf(int hand) => hand >= 0 && hand < _pick.Length ? _pick[hand] : null;

        // ---- the targets (the displayed instruments' pick points, refreshed every frame from the layout: cheap)
        readonly Vector3[] _points = new Vector3[MaxTargets];
        readonly InstrumentKind[] _kinds = new InstrumentKind[MaxTargets];
        int _count;

        // ---- visuals
        readonly bool _render;
        Transform _root, _cardT;
        Mesh _mesh;
        MeshRenderer _meshR;
        Material _material;
        TextMeshPro _text;
        readonly Vector3[] _v = new Vector3[8];
        readonly Color[] _c = new Color[8];
        static readonly int[] s_tris = { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 4, 6, 5, 4, 7, 6 };

        public RaySummon(MaterialLibrary materials, Transform parent, bool render)
        {
            _render = render && materials != null && parent != null;
            if (!_render) return;
            _root = new GameObject("RaySummon").transform;
            _root.SetParent(parent, false);
            _mesh = new Mesh { name = "RaySummon" };
            _mesh.MarkDynamic();
            _mesh.vertices = _v;
            _mesh.colors = _c;
            _mesh.triangles = s_tris;
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 40f);
            _material = materials.Clone(MaterialLibrary.ParticlesAlpha);
            _material.name = "Layout/RaySummon";
            _material.SetColor(MaterialLibrary.BaseColor, Color.white);
            var go = new GameObject("Ray and card");
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _meshR = go.AddComponent<MeshRenderer>();
            _meshR.sharedMaterial = _material;
            _meshR.shadowCastingMode = ShadowCastingMode.Off;
            _meshR.receiveShadows = false;
            _meshR.lightProbeUsage = LightProbeUsage.Off;
            _meshR.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _meshR.sortingOrder = 5;
            var textGo = new GameObject("Card text");
            textGo.transform.SetParent(_root, false);
            _text = textGo.AddComponent<TextMeshPro>();
            _cardT = _text.transform;   // TextMeshPro replaces the Transform with a RectTransform
            _text.fontSize = FontSize;
            _text.enableAutoSizing = true;
            _text.fontSizeMin = FontSize * 0.55f;
            _text.fontSizeMax = FontSize;
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.color = Ink;
            _text.rectTransform.sizeDelta = new Vector2(CardWidth - 0.008f, CardHeight - 0.004f);
            _text.sortingOrder = 6;
            var tr = textGo.GetComponent<MeshRenderer>();
            if (tr != null) { tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false; }
            go.SetActive(false);
            textGo.SetActive(false);
        }

        public void Destroy()
        {
            if (_mesh != null) Object.Destroy(_mesh);
            if (_root != null) Object.Destroy(_root.gameObject);
            _mesh = null;
            _root = null;
        }

        // ---------------------------------------------------------------- the frame

        /// <summary>The Grab phase, before the grab pass (it disarms the hands it owns).</summary>
        internal void Tick(LayoutModule m, float dt)
        {
            App app = m.AppRef;
            if (app == null || app.Head == null) { Cancel(m); return; }
            bool allowed = app.IsPresenting && !m.MrIsActive && !m.Arranging && m.HeldRig == null && !m.MrIsPlacing;
            if (!allowed) { Cancel(m); ReleaseOwnership(app); Show(null, default, default, default, app.Head.position); return; }
            Collect(m);
            PointerInput input = app.Input;
            HandState[] hands = input.Hands;
            Vector3 head = app.Head.position, headFwd = app.Head.forward;
            double now = app.Clock.NowMs;
            int showHand = -1;
            Ray showRay = default;
            for (int i = 0; i < hands.Length && i < 2; i++)
            {
                HandState h = hands[i];
                if (!h.Connected)
                {
                    _pick[i] = null; _pickT[i] = 0f; _owns[i] = false;
                    if (_holdHand == i) CancelHold(m);
                    continue;
                }
                if (_owns[i] && !h.GrabPinching) _owns[i] = false;
                HandFrame f = input.Frame(h);
                bool playing = PointerInput.IsPlaying(h, now, GrabConstants.PlayGuardMs);
                bool system = input.SystemGestureLike(h, head, headFwd);
                Vector3 tip = h.Pointers[0].Tip;
                bool inZone = input.TryIndexTip(h, out Vector3 it) && m.InPlayZone(it);
                InstrumentKind? pick = null;
                Ray ray = default;
                if (f.pointerValid && !playing && !system && !inZone && h.Holding == null && _count > 0)
                {
                    ray = new Ray(f.pointer.position, f.pointer.rotation * Vector3.forward);
                    int idx = StowPlan.Pick(ray.origin, ray.direction, _points, _count, out _, out _);
                    if (idx >= 0) pick = _kinds[idx];
                }
                if (_holdHand == i)
                {
                    // the hold: it lasts while the pinch is held on the same target, the hand is not playing and not in a zone
                    if (!h.GrabPinching || playing || system || pick != _holdKind) CancelHold(m);
                    else
                    {
                        _holdT += dt;
                        Lift(Mathf.Clamp01(_holdT / LiftSeconds));
                        if (_holdT >= HoldSeconds) Commit(m);
                    }
                }
                if (pick == _pick[i] && pick.HasValue) _pickT[i] += dt;
                else { _pick[i] = pick; _pickT[i] = 0f; }
                if (pick.HasValue && _holdHand < 0 && h.GrabPinchStart && !_owns[i] && _pickT[i] >= HoverSeconds) BeginHold(m, i, pick.Value);
                if (_owns[i]) m.Disarm(h);
                if (pick.HasValue && (showHand < 0 || i == _holdHand || _pickT[i] > _pickT[showHand])) { showHand = i; showRay = ray; }
            }
            if (showHand >= 0)
            {
                InstrumentKind k = _pick[showHand].Value;
                Vector3 p = PointOf(k);
                bool card = _pickT[showHand] >= HoverSeconds || _holdHand == showHand;
                Show(k, showRay.origin, p, card ? p + Vector3.up * CardAbove + Vector3.up * (_holdHand >= 0 && _holdKind == k ? PreviewLift * _liftK : 0f) : default, head, card);
            }
            else Show(null, default, default, default, head);
        }

        Vector3 PointOf(InstrumentKind k)
        {
            for (int i = 0; i < _count; i++) if (_kinds[i] == k) return _points[i];
            return Vector3.zero;
        }

        void Collect(LayoutModule m)
        {
            _count = 0;
            IInstrumentRegistry reg = m.AppRef.Registry;
            if (reg == null) return;
            for (int i = 0; i < reg.All.Count && _count < MaxTargets; i++)
            {
                InstrumentKind k = reg.All[i].Kind;
                if (_holdHand >= 0 && k == _holdKind) { _points[_count] = _liftBasePick; _kinds[_count++] = k; continue; }
                if (!m.TryGetDisplayPoint(k, out Vector3 p)) continue;
                _points[_count] = p;
                _kinds[_count++] = k;
            }
        }

        Vector3 _liftBasePick;

        void BeginHold(LayoutModule m, int hand, InstrumentKind k)
        {
            _holdHand = hand;
            _holdKind = k;
            _holdT = 0f;
            _liftK = 0f;
            _owns[hand] = true;
            m.TryGetDisplayPoint(k, out _liftBasePick);
            _liftRig = m.Get(InstrumentIds.RigOf(k));
            _liftBase = _liftRig != null && _liftRig.Group != null ? _liftRig.Group.position : Vector3.zero;
            _holdReplace = m.ReplaceCandidate(k);
            _replaceRig = _holdReplace.HasValue ? m.Get(InstrumentIds.RigOf(_holdReplace.Value)) : null;
            if (_replaceRig != null && (_replaceRig.Group == null || !_replaceRig.InScene)) _replaceRig = null;
            _replaceBase = _replaceRig != null ? _replaceRig.Group.position : Vector3.zero;
        }

        void Lift(float k)
        {
            _liftK = k;
            float e = LayoutMath.Ease(k);
            if (_liftRig?.Group != null) _liftRig.Group.position = _liftBase + Vector3.up * (PreviewLift * e);
            if (_replaceRig?.Group != null) _replaceRig.Group.position = _replaceBase + Vector3.up * (PreviewLift * e);
        }

        void Settle()
        {
            if (_liftRig?.Group != null) _liftRig.Group.position = _liftBase;
            if (_replaceRig?.Group != null) _replaceRig.Group.position = _replaceBase;
            _liftRig = _replaceRig = null;
            _liftK = 0f;
        }

        void CancelHold(LayoutModule m)
        {
            if (_holdHand < 0) return;
            Settle();
            _holdHand = -1;
            Cancels++;
        }

        void Commit(LayoutModule m)
        {
            InstrumentKind k = _holdKind;
            Settle();
            _holdHand = -1;
            _pick[0] = _pick[1] = null;
            _pickT[0] = _pickT[1] = 0f;
            if (m.Bring(k, BringReason.Ray)) Summons++;
        }

        /// <summary>Everything off: a hold settles back (arrange mode, mixed reality, a rig taken, not presenting).</summary>
        public void Cancel(LayoutModule m)
        {
            if (_holdHand >= 0) CancelHold(m);
            _pick[0] = _pick[1] = null;
            _pickT[0] = _pickT[1] = 0f;
            if (CardVisible || RayVisible) Show(null, default, default, default, Vector3.zero);
        }

        void ReleaseOwnership(App app)
        {
            HandState[] hands = app.Input.Hands;
            for (int i = 0; i < _owns.Length && i < hands.Length; i++) if (_owns[i] && !hands[i].GrabPinching) _owns[i] = false;
        }

        // ---------------------------------------------------------------- the card and the ray (one mesh) and the text

        void Show(InstrumentKind? k, Vector3 from, Vector3 to, Vector3 cardAt, Vector3 head, bool card = false)
        {
            bool ray = k.HasValue;
            card = card && k.HasValue;
            string name = card ? NameOf(k.Value) : null;
            Hovered = card ? k : null;
            RayVisible = ray;
            CardVisible = card;
            RayFrom = from;
            RayTo = to;
            CardPosition = cardAt;
            if (!ReferenceEquals(name, CardText) && name != null) CardText = name;
            else if (!card) CardText = null;
            if (!_render) return;
            bool any = ray || card;
            if (_meshR.gameObject.activeSelf != any) _meshR.gameObject.SetActive(any);
            if (_text.gameObject.activeSelf != card) _text.gameObject.SetActive(card);
            if (!any) return;
            // the ray: a flat 3 mm ribbon facing the head
            Vector3 dir = to - from;
            Vector3 side = Vector3.Cross(dir, head - (from + to) * 0.5f);
            side = side.sqrMagnitude > 1e-10f ? side.normalized * (RayWidth * 0.5f) : Vector3.right * (RayWidth * 0.5f);
            Color rc = RayColour, clear = RayColour;
            clear.a = 0f;
            if (ray)
            {
                _v[0] = from - side; _v[1] = from + side; _v[2] = to + side; _v[3] = to - side;
                _c[0] = _c[1] = clear; _c[2] = _c[3] = rc;   // it fades in from the hand
            }
            else for (int i = 0; i < 4; i++) { _v[i] = Vector3.zero; _c[i] = clear; }
            // the card: a cream rectangle facing the head
            if (card)
            {
                Vector3 toHead = head - cardAt;
                toHead.y = 0f;
                if (toHead.sqrMagnitude < 1e-6f) toHead = Vector3.back;
                Quaternion face = Quaternion.LookRotation(-toHead.normalized, Vector3.up);
                Vector3 r = face * Vector3.right * (CardWidth * 0.5f), u = Vector3.up * (CardHeight * 0.5f);
                _v[4] = cardAt - r - u; _v[5] = cardAt + r - u; _v[6] = cardAt + r + u; _v[7] = cardAt - r + u;
                _c[4] = _c[5] = _c[6] = _c[7] = CardColour;
                _cardT.SetPositionAndRotation(cardAt - face * Vector3.forward * 0.001f, face);
                if (_text.text != name) _text.text = name;
            }
            else for (int i = 4; i < 8; i++) { _v[i] = Vector3.zero; _c[i] = clear; }
            _mesh.vertices = _v;
            _mesh.colors = _c;
        }

        string _nameOf;
        InstrumentKind _nameKind = (InstrumentKind)(-1);

        string NameOf(InstrumentKind k)
        {
            if (k == _nameKind && _nameOf != null) return _nameOf;
            _nameKind = k;
            _nameOf = App.Current?.Registry?.Get(k)?.Name ?? k.ToString();
            return _nameOf;
        }
    }
}
