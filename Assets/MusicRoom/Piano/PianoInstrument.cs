// The piano in the room: builds the keys, labels and body under its rig group, registers IInstrument / IPianoApi / Rig,
// and runs PianoAction in the Instruments phase (Core calls Tick then Hover with the hand pointers). The piano-local
// frame: x right, white key tops y = 0, z = back (spec 05 section 2.1); its home is (0, .72, .45) yaw 0 about the seat.
// Sound: IAudioEngine.Piano / Release, one VoiceHandle per key; play events: IRecorder.Add(pianoOn / pianoOff) and the
// fingertip owner's LastPlayMs (grab guards). Audio sources PianoLo/Mid/Hi hang under the group at (-0.42/0/0.42, .05, .05).
// Desktop (ARCHITECTURE section 0): one BoxCollider over the resting keys on Layers.PlaySurface is the piano's click target,
// so DesktopRigFallback.dragBlockMask stops the look-around on it and the nearest PlaySurface hit decides which module a
// click belongs to; the desktop paths run while !App.IsPresenting.
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;

namespace MusicRoom.Piano
{
    public sealed class PianoInstrument : MonoBehaviour, IInstrument, IPianoApi, IPianoSink, IContactSurface
    {
        public static readonly Color Ivory = new Color32(0xf4, 0xef, 0xe3, 0xff), Ebony = new Color32(0x0e, 0x0e, 0x10, 0xff);
        public static readonly Color CaseColour = new Color32(0x13, 0x13, 0x15, 0xff), Acrylic = new Color32(0x0a, 0x0a, 0x0c, 0xff);
        public static readonly Vector3 Home = new Vector3(0f, 0.72f, 0.45f);
        public static readonly Vector3[] SourceOffsets = { new Vector3(-0.42f, 0.05f, 0.05f), new Vector3(0f, 0.05f, 0.05f), new Vector3(0.42f, 0.05f, 0.05f) };

        App _app;
        IAudioEngine _audio;
        IRecorder _recorder;
        IGrabService _grab;
        public PianoAction Action { get; private set; }
        public PianoKeysView View { get; private set; }
        public PianoDesktop Desktop { get; private set; }
        public Rig Rig { get; private set; }
        public MeshRenderer WhiteKeys { get; private set; }
        public MeshRenderer BlackKeys { get; private set; }
        public MeshRenderer Labels { get; private set; }
        public MeshRenderer CaseRenderer { get; private set; }
        public MeshRenderer PanelRenderer { get; private set; }
        public MeshRenderer GrilleRenderer { get; private set; }
        public MeshRenderer RestRenderer { get; private set; }
        public MeshRenderer StandRenderer { get; private set; }
        /// <summary>The desktop click target: the resting keys' box on Layers.PlaySurface (no renderer).</summary>
        public BoxCollider KeysCollider { get; private set; }
        /// <summary>True when the label atlas carries the TMP-rendered letters (false without a GPU).</summary>
        public bool LabelGlyphs => PianoLabelAtlas.HasGlyphs;

        static readonly ProfilerMarker s_tick = new ProfilerMarker("[Piano] Tick"), s_hover = new ProfilerMarker("[Piano] Hover");
        readonly List<Renderer> _played = new List<Renderer>(3);
        readonly List<Renderer> _handles = new List<Renderer>(3);
        Camera _headCamera;   // cached: RigBootstrap.HeadCamera is a GetComponent, never called per frame
        bool _built;

        // Shared, immutable geometry (identical for every boot of the app in a session).
        static Mesh s_case, s_panel, s_grille, s_rest, s_stand;
        static Texture2D s_shade, s_panelPalette;

        public void Setup(App app)
        {
            _app = app;
            _audio = app.Get<IAudioEngine>();
            Action = new PianoAction(app.Clock, this);
            BuildVisuals(app);
            Desktop = new PianoDesktop(Action, KeysCollider);
            Rig = new Rig
            {
                Id = RigId.Piano, Group = transform, Zone = new Zone(this), Priority = 0,
                HomeAll = Home, HomeYawAll = 0f, HomeSingle = Home, HomeYawSingle = 0f,
                Handles = _handles,   // filled once here; SetOnSurface mutates the same list (section 8: never built per call)
            };
            Rig.Instruments.Add(this);
            app.Register<Rig>(Rig);
            app.Register<IInstrument>(this);
            app.Register<IPianoApi>(this);
            app.Register<IContactSurface>(this);   // 10-upgrade/experience.md E1 / E3: the keys at their current depth
            if (_audio != null)
            {
                _audio.SetSourceParent(SourceId.PianoLo, transform, SourceOffsets[0]);
                _audio.SetSourceParent(SourceId.PianoMid, transform, SourceOffsets[1]);
                _audio.SetSourceParent(SourceId.PianoHi, transform, SourceOffsets[2]);
            }
            _built = true;
        }

        void BuildVisuals(App app)
        {
            var mats = app.Materials;
            if (s_shade == null) s_shade = PianoTextures.ShadePalette();
            if (s_panelPalette == null) s_panelPalette = PianoTextures.PanelPalette();

            // ---- keys and labels ----
            var keys = new GameObject("Keys").transform;
            keys.SetParent(transform, false);
            View = new PianoKeysView(Action);
            WhiteKeys = MakeRenderer("WhiteKeys", keys, View.WhiteMesh, mats.Lit(Ivory, 1f - 0.3f, 0f, s_shade), true);
            BlackKeys = MakeRenderer("BlackKeys", keys, View.BlackMesh, mats.Lit(Ebony, 1f - 0.24f, 0f, s_shade), true);
            Labels = MakeRenderer("KeyLabels", keys, View.LabelMesh, LabelMaterial(mats), false);
            Labels.receiveShadows = false;
            _played.Add(WhiteKeys);
            _played.Add(BlackKeys);
            _played.Add(Labels);
            KeysCollider = MakeKeysCollider(keys);

            // ---- body ----
            if (s_case == null) s_case = PianoBody.Case().ToMesh("PianoCase");
            if (s_panel == null) s_panel = PianoBody.Panel().ToMesh("PianoPanel");
            if (s_grille == null) s_grille = PianoBody.Grille().ToMesh("PianoGrille");
            if (s_rest == null) s_rest = PianoBody.MusicRest().ToMesh("PianoMusicRest");
            if (s_stand == null) s_stand = PianoBody.Stand().ToMesh("PianoStand");
            var body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            CaseRenderer = MakeRenderer("Case", body, s_case, mats.Lit(CaseColour, 1f - 0.58f), true);
            PanelRenderer = MakeRenderer("Panel", body, s_panel, mats.Lit(Color.white, 1f - 0.42f, 0.1f, s_panelPalette), true);
            var grilleTex = SharedTextures.Get(SharedTextures.Grille);   // Core's procedural grille texture (baked as Core/grille)
            GrilleRenderer = MakeRenderer("Grille", body, s_grille, mats.Lit(Color.white, 1f - 0.55f, 0.35f, grilleTex), true);
            // Acrylic: roughness .2 with a clear coat .6 by design; URP Lit has no clear coat, the gloss carries it.
            RestRenderer = MakeRenderer("MusicRest", body, s_rest, mats.Lit(Acrylic, 1f - 0.2f), true);
            var wood = SharedTextures.Get(SharedTextures.WoodStand);     // woodTexture seed 17 (Core/wood_stand)
            StandRenderer = MakeRenderer("Stand", body, s_stand, mats.Lit(Color.white, 1f - 0.62f, 0f, wood), true);
            RebuildHandles();
        }

        Material LabelMaterial(MaterialLibrary mats)
        {
            var atlas = PianoLabelAtlas.Get();
            Material m;
            if (mats.HasTemplate(PianoAssets.LabelTemplate)) m = mats.Clone(PianoAssets.LabelTemplate);
            else
            {
                // No template yet (render-setup not run): alpha-blended Lit, no tint and no alpha test, never opaque cards.
                Debug.LogWarning("[Piano] template Piano/Label missing (run Tools/unity.sh render-setup): labels use LitTransparent");
                m = mats.Clone(MaterialLibrary.LitTransparent);
                m.renderQueue = PianoAssets.LabelQueue;
                m.SetFloat(MaterialLibrary.Smoothness, 0.2f);
            }
            m.SetTexture(MaterialLibrary.BaseMap, atlas);
            m.SetColor(MaterialLibrary.BaseColor, Color.white);
            return m;
        }

        /// <summary>One box over every resting key (white bodies y -H..0, black tops up to BTOP, z 0..L): the click target the
        /// desktop camera and the other modules' desktop paths see. The analytic per-key ray test then picks the key.</summary>
        static BoxCollider MakeKeysCollider(Transform keys)
        {
            var go = new GameObject("KeysClickTarget") { layer = Layers.PlaySurface };
            go.transform.SetParent(keys, false);
            var box = go.AddComponent<BoxCollider>();
            PianoDesktop.KeysBox(out Vector3 center, out Vector3 size);
            box.center = center;
            box.size = size;
            return box;
        }

        static MeshRenderer MakeRenderer(string name, Transform parent, Mesh mesh, Material mat, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;   // shadows exist only in MR
            r.receiveShadows = shadows;
            // Keep the default BlendProbes: with no baked probes it supplies the room's Trilight ambient (Off would drop it).
            return r;
        }

        void RebuildHandles()
        {
            // Grab handles: the case, the stand (unless hidden on a real table) and the music rest; never the keys or labels.
            _handles.Clear();
            _handles.Add(CaseRenderer);
            if (StandRenderer.gameObject.activeSelf) _handles.Add(StandRenderer);
            _handles.Add(RestRenderer);
        }

        void OnDisable()
        {
            // The rig left the room (or the app is closing): nothing may stay held or ringing behind an inactive source.
            if (!_built) return;
            try
            {
                Action.ResetAll();
                Desktop.ReleaseAll();
                View.Sync();
            }
            catch (System.Exception e) { Debug.LogWarning("[Piano] reset on disable: " + e.Message); }
        }

        void OnDestroy()
        {
            View?.Dispose();
        }

        // ================= IInstrument =================
        public InstrumentKind Kind => InstrumentKind.Piano;
        public int TickOrder => 0;
        public bool AcceptsStickPointers => false;
        /// <summary>Out of the layout or sliding in during a page turn (Rig.InScene false): not played, hovered or grabbed.</summary>
        public bool IsPresent => isActiveAndEnabled && (Rig == null || Rig.InScene);
        public Transform Root => transform;
        public IReadOnlyList<Renderer> PlayedParts => _played;

        public void Tick(IReadOnlyList<Pointer> pointers, float dt)
        {
            s_tick.Begin();
            Action.Tick(pointers, dt, transform.worldToLocalMatrix);
            // The desktop paths run while the XR session is not presenting (section 3.3: never IsHeadset, which is
            // false on the headset until the hand runtime is up).
            if (_app != null && !_app.IsPresenting)
            {
                if (_headCamera == null && _app.Rig != null) _headCamera = _app.Rig.HeadCamera;
                Desktop.Tick(transform, _headCamera);
            }
            View.Sync();
            s_tick.End();
        }

        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow)
        {
            s_hover.Begin();
            Action.Hover(pointers, glow, transform.worldToLocalMatrix, transform.localToWorldMatrix);
            s_hover.End();
        }

        public PlaySurface PlaySurfaceAt(Vector3 world) =>
            isActiveAndEnabled && InPlayZone(world) && OverKeys(world) ? PlaySurface.PianoKeys : PlaySurface.None;

        // ================= IPianoApi =================
        public float W => PianoSpec.W;
        public float BW => PianoSpec.BW;
        public float BTOP => PianoSpec.BTOP;
        public float L => PianoSpec.L;
        public bool InPlayZone(Vector3 world) => PianoAction.InPlayZoneLocal(transform.InverseTransformPoint(world));
        public bool OverKeys(Vector3 world) => PianoAction.OverKeysLocal(transform.InverseTransformPoint(world));
        /// <summary>pianoKeySpace: the play zone above the bottom of the keys (a pinch there never grabs the piano).</summary>
        public bool InKeySpace(Vector3 world) => isActiveAndEnabled && PianoAction.KeySpaceLocal(transform.InverseTransformPoint(world));

        public bool TryGetKey(int midi, out PianoKeyInfo key)
        {
            var k = Action != null ? Action.KeyByMidi(midi) : null;
            if (k == null) { key = default; return false; }
            key = new PianoKeyInfo { Midi = midi, X = k.X, Black = k.Black, Label = PianoSpec.NoteNames[midi % 12] };
            return true;
        }

        public void PressMidi(int midi, object pointerKey, float vel = 0.75f) => Action.PressMidi(midi, pointerKey, vel);
        public void ReleaseMidi(int midi, object pointerKey) => Action.ReleaseMidi(midi, pointerKey);
        public void PlaybackOn(int midi, float vel) => Action.PlaybackOn(midi, vel);
        public void PlaybackOff(int midi) => Action.PlaybackOff(midi);
        public void PlaybackReleaseAll() => Action.PlaybackReleaseAll();

        public void SetOnSurface(bool onTable)
        {
            if (StandRenderer.gameObject.activeSelf == !onTable) return;
            StandRenderer.gameObject.SetActive(!onTable);
            RebuildHandles();
        }

        public void ResetSurface() => SetOnSurface(false);
        /// <summary>The stand is hidden: the piano stands on a real table (SetOnSurface(true)).</summary>
        public bool OnSurface => StandRenderer != null && !StandRenderer.gameObject.activeSelf;
        public int HeldKeyCount => Action.HeldKeyCount;

        // ================= the platform (12-new-instruments/platform.md 3.3, 13.5) =================
        /// <summary>The platform adapters (IStowable, IInstrumentPlayback, IGuideAnchors, IInstrumentSurface), registered by
        /// PianoModule.</summary>
        public PianoPlatform Platform { get; internal set; }
        /// <summary>The sheet-music hook: the music rest's front face, bottom centre, in the world; null while the piano is not
        /// at the seat (PianoMusicRest).</summary>
        public Pose? MusicRestPose => PianoMusicRest.World(this);
        public Pose MusicRestLocal => PianoMusicRest.Local;
        public float MusicRestLedge => PianoMusicRest.Ledge;

        // ================= IContactSurface (10-upgrade/experience.md 2.1) =================

        /// <summary>The key top under a fingertip (or a stick tip) at its CURRENT depth (the 88-key spring state), for the
        /// visual finger clamp (E1) and the contact shadows (E3). Tangent = piano right; ClipHalf = half the visible key
        /// width while the key is more than 2 mm down at that point (resting keys share one plane, so a shadow may spill
        /// onto the neighbours), else 0, about ClipOrigin on the key's centre line. Use Both. Visual only: never changes which notes sound.</summary>
        public bool QueryContact(Vector3 world, float above, float below, out ContactHit hit)
        {
            hit = default;
            if (!_built || !IsPresent || _app == null) return false;
            // The piano's matrices, scale and grab state once per app frame (Clock.NowMs is frame-constant): step 8a asks
            // for up to 12 points.
            if (_app.Clock.NowMs != _contactStamp)
            {
                _contactStamp = _app.Clock.NowMs;
                if (_grab == null) _grab = _app.Get<IGrabService>();
                _contactHeld = _grab != null && ReferenceEquals(_grab.HeldRig, Rig);
                _contactW2L = transform.worldToLocalMatrix;
                _contactL2W = transform.localToWorldMatrix;
                _contactRight = transform.right;
                _contactScale = transform.lossyScale.x;
            }
            if (_contactHeld) return false;
            Vector3 l = _contactW2L.MultiplyPoint3x4(world);
            if (!PianoAction.InPlayZoneLocal(l)) return false;
            PianoKey key = Action.ContactKey(l, out Vector3 pl, out Vector3 nl, out float drop);
            if (key == null) return false;
            Vector3 p = _contactL2W.MultiplyPoint3x4(pl);
            Vector3 n = _contactL2W.MultiplyVector(nl).normalized;
            float h = Vector3.Dot(n, world - p);
            if (h > above || h < -below) return false;
            hit.Point = world - n * h;
            hit.Normal = n;
            hit.Tangent = _contactRight;
            hit.ClipHalf = drop * _contactScale > ClipDrop ? (key.Black ? PianoSpec.BW / 2f : PianoSpec.WW / 2f) * _contactScale : 0f;
            hit.ClipOrigin = _contactL2W.MultiplyPoint3x4(new Vector3(key.X, pl.y, pl.z));   // the key's centre line
            hit.Height = h;
            hit.Use = ContactUse.Both;
            return true;
        }

        double _contactStamp = double.NaN;
        bool _contactHeld;
        Matrix4x4 _contactW2L, _contactL2W;
        Vector3 _contactRight;
        float _contactScale = 1f;

        /// <summary>A key top this far down at the touch point clips its shadow to the key (E3).</summary>
        public const float ClipDrop = 0.002f;

        // ================= the stand follows the floor =================
        // "Move instruments" lets the player raise or lower the keyboard (key tops 0.55-0.95 m, ArrangeConstants); the stand
        // then stretches or shortens from the underside of the body so its feet stay on the floor. Visual only: LateUpdate,
        // after Layout moved the piano this frame. The stand's mesh is shared (static), so only its transform changes.
        public const float StandMinScale = 0.3f, StandMaxScale = 2.2f;
        float _standFitY = float.NaN;

        /// <summary>The stand's vertical stretch (1 at the home height of 0.72 m).</summary>
        public float StandScale => StandRenderer != null ? StandRenderer.transform.localScale.y : 1f;

        void LateUpdate() => FitStand();

        /// <summary>Stretch the stand so its feet reach the floor (world y 0) below the key tops, about the body's underside.</summary>
        public void FitStand()
        {
            if (!_built || StandRenderer == null || !StandRenderer.gameObject.activeSelf) return;
            float k = Mathf.Abs(transform.lossyScale.y) > 1e-4f ? transform.lossyScale.y : 1f;
            float y = transform.position.y;
            if (Mathf.Abs(y - _standFitY) < 1e-4f) return;
            _standFitY = y;
            float top = PianoBody.Bot, floorLocal = -y / k;
            float s = Mathf.Clamp((top - floorLocal) / (top - PianoSpec.FloorY), StandMinScale, StandMaxScale);
            Transform t = StandRenderer.transform;
            t.localScale = new Vector3(1f, s, 1f);
            t.localPosition = new Vector3(0f, top * (1f - s), 0f);
        }

        // ================= IPianoSink =================
        VoiceHandle IPianoSink.NoteOn(int midi, float vel) => _audio != null && _audio.IsReady ? _audio.Piano(midi, vel) : VoiceHandle.None;

        void IPianoSink.NoteOff(int midi, VoiceHandle voice, bool thud)
        {
            if (_audio != null && voice.IsValid) _audio.Release(voice, thud);
        }

        void IPianoSink.Record(in TakeEvent ev)
        {
            if (_recorder == null && _app != null) _recorder = _app.Get<IRecorder>();
            _recorder?.Add(ev);
        }

        sealed class Zone : IPlayZone
        {
            readonly PianoInstrument _p;
            public Zone(PianoInstrument p) { _p = p; }
            public bool Contains(Vector3 world) => _p.InPlayZone(world);
        }
    }
}
