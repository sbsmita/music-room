// The tape deck (spec 08 section 4.3, spec 07 section 10, Docs/07-world-visuals.md `tapeDeck`), with the looper's
// controls: a reel-to-reel deck on the side table. The transport row has four pegs, LOOP REC PLAY STOP; a peg presses
// when a fingertip enters a cylinder of radius 18 mm over it, 60-95 mm above the deck's base, once per entry per pointer;
// pegs sink 5 mm (rate 18/s) while touched, LOOP stays down while a loop session runs, REC while recording, PLAY during
// any playback; the reels turn at 3 rad/s recording, 4 rad/s playing; the lamp glows red recording, green playing.
// REC while a take plays records on top of it (Recorder.RecordOnTop): REC and PLAY both stay down while the take plays on,
// the lamp red; at the take's end PLAY comes up and REC stays down until STOP.
// The deck's front edge is a loop strip of small round push buttons, most of them lit from inside (calm indicator lamps,
// not glowing UI): row 2 is UNDO, the six layer lights (poke one to mute / unmute its layer) and CLEAR (tap again); row 3
// is the tempo SLOW / MED / FAST (70 / 90 / 110 BPM), the length 2 BARS / 4 BARS, CLICK, TIDY and GUIDE (Sunny's
// quick show of the deck, IGuide.ToggleDeckGuide). Four small count lights sit behind LOOP. An IInstrument of Kind Table, TickOrder 30 (spec 05 section 1.1).
// Unity local z = -right-handed z: the controls sit toward the player (-z), the reels at +0.035.
// Desktop (ARCHITECTURE.md sections 0 and 15.7, DesktopDown): the app's one desktop model.
// Each control has a click collider on Layers.PlaySurface (29); a left-button press while the app is not presenting whose
// nearest play-surface hit is a control presses it (DesktopDown); a Shift / Alt press is Layout's rig move / turn and
// never presses; the mouse hand's pointers never press (Pointer.FromMouse), so a click never fires twice. The body keeps
// a plain collider on the default layer: the mouse hand rests on it, and a drag there still looks around.
// Draws: 7 (DrawBudget), the deck's share of "table + tape deck + switches <= 15": body, wood, tapes, chrome, lamps (every
// light and lit button in one mesh: one material whose emission map is a 32 x 1 state texture, one texel per lamp,
// rewritten only when a lamp changes), pegs (LOOP REC PLAY STOP UNDO, palette colours) and labels (one baked mesh).
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Lessons
{
    /// <summary>Every pokeable control of the deck. Rec / Play / Stop keep 0..2 (today's three pegs).</summary>
    public enum DeckButton
    {
        Rec = 0, Play = 1, Stop = 2, Loop, Undo, Clear, Slow, Medium, Fast, Bars2, Bars4, Click, Tidy,
        Layer1, Layer2, Layer3, Layer4, Layer5, Layer6, Guide,
    }

    /// <summary>The desktop side of the deck (ARCHITECTURE.md section 0), so tests can drive it without a mouse.</summary>
    public interface IDeckDesktop
    {
        /// <summary>True on the frame the left button went down while the app is not presenting (desktop), with the
        /// mouse ray from the head camera.</summary>
        bool TryClick(out Ray ray);
        /// <summary>The pointer belongs to the desktop mouse hand: a visual stand-in that never presses (Pointer.FromMouse).</summary>
        bool IsMouse(Pointer p);
        /// <summary>Shift or Alt is held: the click moves or turns the rig (Layout's desktop grab), never presses.</summary>
        bool ModifierHeld { get; }
    }

    public sealed class TapeDeck : IInstrument, IContactSurface
    {
        // ---- placement and touch ----
        /// <summary>The deck on the side-table rig at (0.07, 0.6, 0.04): moved 4 cm back from z = 0
        /// so the loop strip in front still sits on the table top (its front edge 3.5 cm from the table's).</summary>
        public static readonly Vector3 TableLocalPosition = new Vector3(0.07f, 0.6f, 0.04f);
        /// <summary>Today's three pegs' x (REC, PLAY, STOP); LOOP is LoopX, left of REC.</summary>
        public static readonly float[] ButtonX = { -0.03f, 0.03f, 0.09f };
        public const float LoopX = -0.09f;
        public static readonly string[] ButtonLabel = { "REC", "PLAY", "STOP" };
        public static readonly Color[] ButtonColor = { new Color32(0xb3, 0x1b, 0x1b, 0xff), new Color32(0x2b, 0x8a, 0x3e, 0xff), new Color32(0x2a, 0x2a, 0x2e, 0xff) };
        public const float PegY = 0.081f, PegZ = -0.055f, PegTravel = 0.005f, PegRate = 18f;
        public const float PegRTop = 0.014f, PegRBottom = 0.015f, PegH = 0.014f;
        public const float PressR = 0.018f, PressYMin = 0.06f, PressYMax = 0.095f;
        public const float ReelRadPerSecRecording = 3f, ReelRadPerSecPlaying = 4f;
        // The loop strip: small caps (r 7.5 mm, 6 mm high on the deck top at y .074), 2 mm travel, an 12.5 mm press cylinder
        // up to 8 mm over the cap; rows at z -.108 and -.148, labels in front of each row.
        public const float CapR = 0.0075f, CapH = 0.006f, CapY = 0.077f, CapTravel = 0.002f, CapPressR = 0.0125f, CapPressYMax = 0.088f;
        public const float Row2Z = -0.108f, Row3Z = -0.148f, LampRowZ = -0.025f;
        public const float FrontZ = -0.175f, BackZ = 0.1f;
        // hover: a 45 mm glow over a peg for a tip 1 cm under to 6 cm over its top, within 25 mm
        public const float HoverTop = 0.088f, HoverBelow = 0.01f, HoverAbove = 0.06f, HoverR = 0.025f, HoverGlowY = 0.09f, HoverGlowSize = 0.045f;
        public const float CapHoverR = 0.0135f, CapGlowSize = 0.026f;
        // play surface: a pinch within 3.5 cm of a peg (2.5 cm of a cap), 6-17 cm over the base, is a press
        public const float SurfaceR = 0.035f, CapSurfaceR = 0.025f, SurfaceYMin = 0.06f, SurfaceYMax = 0.17f;
        // desktop click target per control: a box just around it
        public static readonly Vector3 PegColliderSize = new Vector3(0.032f, 0.016f, 0.032f);
        public static readonly Vector3 CapColliderSize = new Vector3(0.019f, 0.012f, 0.019f);
        public const float ClickReach = 40f;     // the head camera's far plane

        // ---- look ----
        public static readonly Color LampIdle = new Color32(0x33, 0x22, 0x22, 0xff);
        public static readonly Color LampRecording = new Color32(0xff, 0x2a, 0x2a, 0xff);
        public static readonly Color LampPlaying = new Color32(0x2a, 0xff, 0x5a, 0xff);
        public static readonly Color LabelInk = new Color32(0xe8, 0xe0, 0xd0, 0xff);
        // The loop strip's lamps: warm and dim (indicator bulbs behind smoked caps), the count lights amber.
        public static readonly Color32 CountBright = new Color32(0xff, 0xa8, 0x38, 0xff), CountDim = new Color32(0x50, 0x30, 0x0c, 0xff);
        public static readonly Color32 LayerOn = new Color32(0x44, 0xa8, 0x58, 0xff), LayerMuted = new Color32(0x14, 0x26, 0x18, 0xff);
        public static readonly Color32 LayerRec = new Color32(0xe0, 0x2c, 0x22, 0xff), Selected = new Color32(0x9a, 0x70, 0x40, 0xff);
        public static readonly Color32 ClearWarn = new Color32(0xf0, 0x58, 0x20, 0xff);
        static readonly Color Nickel = new Color32(0xc9, 0xcc, 0xd0, 0xff), Chrome = new Color32(0xd9, 0xdd, 0xe2, 0xff);
        static readonly Color Rubber = new Color32(0x1a, 0x1a, 0x1a, 0xff);

        /// <summary>Renderers (one draw each): body, wood, tapes, chrome, lamps, pegs, labels. The rig's share of
        /// ARCHITECTURE.md's "table + tape deck + switches &lt;= 15" (Room keeps 8 for table and switches).</summary>
        public const int DrawBudget = 7;
        public static readonly Vector3[] ReelLocal = { new Vector3(-0.065f, 0.078f, 0.035f), new Vector3(0.065f, 0.078f, 0.035f) };
        public const int ControlCount = (int)DeckButton.Guide + 1;
        public const int LampTexels = 32;
        /// <summary>Lamp texels: 0 the main lamp, 1..4 the count lights; the lit caps follow (CapLamp).</summary>
        public const int MainLamp = 0, CountLamp0 = 1;

        sealed class Control
        {
            public DeckButton Id;
            public float X, Z;
            public bool Peg;
            public int Lamp = -1;                 // texel of a lit cap
            public int Part;                      // its range in the pegs' or the lamps' merged mesh
            public float Depth;
            public Vector3 Rest, LocalPosition;
            public BoxCollider Click;
            public readonly List<Pointer> Held = new List<Pointer>(4);
            /// <summary>When a fingertip last pressed it (the deck's clock; -inf before).</summary>
            public float LastPress = float.NegativeInfinity;
            /// <summary>Every fingertip has been ClearMargin away from it since its last press (a new poke may press at once).</summary>
            public bool Cleared = true;
            public float Travel => Peg ? PegTravel : CapTravel;
            public float R => Peg ? PressR : CapPressR;
            public float YMax => Peg ? PressYMax : CapPressYMax;
            public float Top => Peg ? HoverTop : CapY + CapH * 0.5f;
        }

        readonly IRecorder _recorder;
        readonly Recorder _rec;
        readonly Looper _looper;
        readonly Transform _root;
        readonly Control[] _controls = new Control[ControlCount];
        readonly Renderer[] _played = new Renderer[2];
        readonly List<Renderer> _handles = new List<Renderer>(8);
        readonly List<Mesh> _meshes = new List<Mesh>(12);
        readonly MergedMesh _chrome = new MergedMesh(), _pegMesh = new MergedMesh(), _lampMesh = new MergedMesh();
        readonly int[] _spokePart = new int[2];
        readonly int[] _countPart = new int[4];
        int _mainPart;
        readonly Material _lamp;
        readonly Texture2D _lampTex;
        readonly Color32[] _lampPx = new Color32[LampTexels];
        readonly Color32[] _lampWant = new Color32[LampTexels];
        readonly DeckLabels _labels = new DeckLabels();
        int _clearWord = -1, _againWord = -1;
        float _reelAngle, _time;
        Quaternion _reelRotation = Quaternion.identity;

        public TapeDeck(IRecorder recorder, MaterialLibrary materials, Transform parent, Vector3 localPosition, Looper looper = null)
        {
            _recorder = recorder;
            _rec = recorder as Recorder;
            _looper = looper ?? _rec?.Looper;
            _root = new GameObject("TapeDeck").transform;
            _root.SetParent(parent, false);
            _root.localPosition = localPosition;
            _root.localRotation = Quaternion.identity;

            // Seven draws (DrawBudget; the deck has 17 parts). Parts that share a material share a renderer: the palette
            // atlas lets the ebony top, walnut cheeks and pearl hubs share one material, the pegs another; every lamp and lit
            // cap shares the lamps' material, whose emission map holds one texel per lamp.
            Texture palette = DeckPalette.Texture();
            Material nickel = Metal(materials, Nickel, 0.7f);
            Material chrome = Metal(materials, Chrome, 0.84f);
            Material wood = materials.Lit(Color.white, 0.55f, 0f, palette);
            Material rubber = materials.Lit(Rubber, 0.05f);
            Material pegs = materials.Lit(Color.white, 0.5f, 0f, palette);
            float depth = BackZ - FrontZ, midZ = (BackZ + FrontZ) * 0.5f;

            // 1. Body (nickel), as deep as the deck with its loop strip.
            Part("Body", Keep(LessonMeshes.Box(new Vector3(0.26f, 0.07f, depth), new Vector3(0f, 0.035f, midZ), "DeckBody")), nickel, true);

            // 2. Wood: ebony top, walnut cheeks, pearl hubs (static).
            var woodMesh = new MergedMesh();
            using (var top = new TempMesh(LessonMeshes.Box(new Vector3(0.25f, 0.004f, depth - 0.01f), new Vector3(0f, 0.072f, midZ))))
            using (var cheek = new TempMesh(LessonMeshes.Box(new Vector3(0.01f, 0.07f, depth), Vector3.zero)))
            using (var hub = new TempMesh(LessonMeshes.Cylinder(0.016f, 0.016f, 0.014f, 20, Vector3.zero)))
            {
                woodMesh.Add(top.Mesh, Matrix4x4.identity, DeckPalette.Uv(DeckPalette.Ebony));
                woodMesh.Add(cheek.Mesh, Matrix4x4.Translate(new Vector3(-0.135f, 0.035f, midZ)), DeckPalette.Uv(DeckPalette.Walnut));
                woodMesh.Add(cheek.Mesh, Matrix4x4.Translate(new Vector3(0.135f, 0.035f, midZ)), DeckPalette.Uv(DeckPalette.Walnut));
                for (int i = 0; i < 2; i++) woodMesh.Add(hub.Mesh, Matrix4x4.Translate(ReelLocal[i] + new Vector3(0f, 0.004f, 0f)), DeckPalette.Uv(DeckPalette.Pearl));
            }
            Part("Wood", Keep(woodMesh.Build("DeckWood", false, Vector3.zero)), wood, true);

            // 3. Tapes (rubber, static).
            var tapeMesh = new MergedMesh();
            using (var tape = new TempMesh(LessonMeshes.Cylinder(0.05f, 0.05f, 0.008f, 40, Vector3.zero)))
                for (int i = 0; i < 2; i++) tapeMesh.Add(tape.Mesh, Matrix4x4.Translate(ReelLocal[i]), Vector2.zero);
            Part("Tapes", Keep(tapeMesh.Build("DeckTapes", false, Vector3.zero)), rubber, true);

            // 4. Chrome: the head bar (static) and both reels' spokes (re-posed while the reels turn).
            using (var bar = new TempMesh(LessonMeshes.Box(new Vector3(0.08f, 0.006f, 0.01f), Vector3.zero)))
            using (var spokes = new TempMesh(LessonMeshes.Spokes()))
            {
                _chrome.Add(bar.Mesh, Matrix4x4.Translate(new Vector3(0f, 0.076f, 0.085f)), Vector2.zero);
                for (int i = 0; i < 2; i++) _spokePart[i] = _chrome.Add(spokes.Mesh, Matrix4x4.Translate(ReelLocal[i]), Vector2.zero);
            }
            // pad: a turned spoke set reaches 50.2 mm from its axis, the bars at rest only 43.3 mm along z.
            Part("Chrome", Keep(_chrome.Build("DeckChrome", true, new Vector3(0.002f, 0f, 0.008f))), chrome, true);

            // The controls' places.
            DefineControls();

            // 5. Lamps: the main lamp, the four count lights and every lit cap, in one mesh. Their colour is the glass
            // (#332222) and their light the emission map: texel i is lamp i's current glow (black when off).
            _lampTex = new Texture2D(LampTexels, 1, TextureFormat.RGBA32, false, false)
            {
                name = "DeckLampState", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            for (int i = 0; i < LampTexels; i++) _lampPx[i] = _lampWant[i] = new Color32(0, 0, 0, 255);
            _lampTex.SetPixels32(_lampPx);
            _lampTex.Apply(false, false);
            _lamp = materials.LitEmissiveMaterial(LampIdle, Color.white, 0.6f);
            _lamp.SetTexture(EmissionMap, _lampTex);
            using (var bulb = new TempMesh(LessonMeshes.Sphere(0.007f, 16, 12)))
            using (var count = new TempMesh(LessonMeshes.Sphere(0.0042f, 12, 8)))
            using (var cap = new TempMesh(LessonMeshes.Cylinder(CapR, CapR + 0.0005f, CapH, 20, Vector3.zero)))
            {
                _mainPart = _lampMesh.Add(bulb.Mesh, Matrix4x4.Translate(new Vector3(ButtonX[2], 0.08f, LampRowZ)), LampUv(MainLamp));
                for (int i = 0; i < 4; i++)
                    _countPart[i] = _lampMesh.Add(count.Mesh, Matrix4x4.Translate(CountLocal(i)), LampUv(CountLamp0 + i));
                int texel = CountLamp0 + 4;
                foreach (Control c in _controls)
                {
                    if (c.Peg) continue;
                    if (c.Id == DeckButton.Undo) continue;           // UNDO is a plain cap (the pegs' mesh)
                    c.Lamp = texel++;
                    c.Part = _lampMesh.Add(cap.Mesh, Matrix4x4.Translate(c.Rest), LampUv(c.Lamp));
                }
            }
            _played[1] = Part("Lamps", Keep(_lampMesh.Build("DeckLamps", true, new Vector3(0f, CapTravel, 0f))), _lamp, false);

            // 6. Pegs: LOOP REC PLAY STOP and the UNDO cap in one mesh, each a part sinking on its own (played, never handles).
            using (var peg = new TempMesh(LessonMeshes.Cylinder(PegRTop, PegRBottom, PegH, 24, Vector3.zero)))
            using (var cap = new TempMesh(LessonMeshes.Cylinder(CapR, CapR + 0.0005f, CapH, 20, Vector3.zero)))
            {
                foreach (Control c in _controls)
                {
                    if (!c.Peg && c.Id != DeckButton.Undo) continue;
                    int cell = c.Id == DeckButton.Loop ? DeckPalette.LoopCap : c.Id == DeckButton.Undo ? DeckPalette.Stop : DeckPalette.Rec + (int)c.Id;
                    c.Part = _pegMesh.Add(c.Peg ? peg.Mesh : cap.Mesh, Matrix4x4.Translate(c.Rest), DeckPalette.Uv(cell));
                }
            }
            _played[0] = Part("Pegs", Keep(_pegMesh.Build("DeckPegs", true, new Vector3(0f, PegTravel, 0f))), pegs, false);

            // 7. Labels: every word in one baked mesh.
            BuildLabels();

            // Desktop (see the header): the body on the default layer, a click target per control on the play-surface layer.
            // The targets stay put while a control sinks (only its vertices move).
            var body = _root.gameObject.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 0.037f, midZ);
            body.size = new Vector3(0.28f, 0.074f, depth);
            foreach (Control c in _controls)
            {
                var go = new GameObject("Click" + c.Id) { layer = Layers.PlaySurface };
                go.transform.SetParent(_root, false);
                go.transform.localPosition = c.Rest;
                var bc = go.AddComponent<BoxCollider>();
                bc.size = c.Peg ? PegColliderSize : CapColliderSize;
                c.Click = bc;
            }
            UpdateLamps(force: true);
        }

        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");

        static Vector2 LampUv(int texel) => new Vector2((texel + 0.5f) / LampTexels, 0.5f);

        /// <summary>The four count lights behind LOOP (deck-local centres).</summary>
        public static Vector3 CountLocal(int i) => new Vector3(LoopX - 0.021f + 0.014f * i, 0.0755f, LampRowZ);

        void DefineControls()
        {
            void Def(DeckButton id, float x, float z, bool peg)
            {
                var c = new Control { Id = id, X = x, Z = z, Peg = peg };
                c.Rest = c.LocalPosition = new Vector3(x, peg ? PegY : CapY, z);
                _controls[(int)id] = c;
            }
            Def(DeckButton.Loop, LoopX, PegZ, true);
            Def(DeckButton.Rec, ButtonX[0], PegZ, true);
            Def(DeckButton.Play, ButtonX[1], PegZ, true);
            Def(DeckButton.Stop, ButtonX[2], PegZ, true);
            Def(DeckButton.Undo, -0.105f, Row2Z, false);
            for (int i = 0; i < Looper.MaxLayers; i++) Def(DeckButton.Layer1 + i, -0.07f + 0.028f * i, Row2Z, false);
            Def(DeckButton.Clear, 0.105f, Row2Z, false);
            for (int i = (int)DeckButton.Slow; i <= (int)DeckButton.Tidy; i++) Def((DeckButton)i, Layout((DeckButton)i).x, Row3Z, false);
            Def(DeckButton.Guide, Layout(DeckButton.Guide).x, Row3Z, false);
        }

        /// <summary>The printed words: the transport row 6 mm, the loop strip 4 mm, in front of each control.</summary>
        public static readonly (DeckButton id, string text)[] Words =
        {
            (DeckButton.Loop, "LOOP"), (DeckButton.Rec, "REC"), (DeckButton.Play, "PLAY"), (DeckButton.Stop, "STOP"),
            (DeckButton.Undo, "UNDO"), (DeckButton.Layer1, "1"), (DeckButton.Layer2, "2"), (DeckButton.Layer3, "3"),
            (DeckButton.Layer4, "4"), (DeckButton.Layer5, "5"), (DeckButton.Layer6, "6"), (DeckButton.Clear, "CLEAR"),
            (DeckButton.Slow, "SLOW"), (DeckButton.Medium, "MED"), (DeckButton.Fast, "FAST"), (DeckButton.Bars2, "2 BARS"),
            (DeckButton.Bars4, "4 BARS"), (DeckButton.Click, "CLICK"), (DeckButton.Tidy, "TIDY"), (DeckButton.Guide, "GUIDE"),
        };
        public const string TapAgain = "TAP AGAIN";
        public const float LabelZ = -0.082f, SmallEm = 0.004f, TransportEm = 0.006f, SmallWide = 1.25f;

        void BuildLabels()
        {
            var words = new List<DeckLabels.Word>(Words.Length + 1);
            foreach (var (id, text) in Words)
            {
                Control c = _controls[(int)id];
                bool small = !c.Peg;
                words.Add(new DeckLabels.Word { Text = text, X = c.X, Z = small ? c.Z - 0.017f : LabelZ, Em = small ? SmallEm : TransportEm, Wide = small ? SmallWide : 0f });
            }
            Control clear = _controls[(int)DeckButton.Clear];
            words.Add(new DeckLabels.Word { Text = TapAgain, X = clear.X - 0.003f, Z = clear.Z - 0.017f, Em = 0.0034f, Wide = SmallWide, Hidden = true });
            _labels.Build(_root, words);
            _clearWord = _labels.IndexOf("CLEAR");
            _againWord = _labels.IndexOf(TapAgain);
            if (_labels.Mesh == null) return;
            Keep(_labels.Mesh);
            var r = Part("Labels", _labels.Mesh, _labels.Material, false);
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Desktop clicks and the mouse-hand filter; null = no desktop (EditMode tests, a headset-only build).</summary>
        public IDeckDesktop Desktop { get; set; }
        /// <summary>GUIDE was pressed (the module toggles Sunny's deck walkthrough) and whether it runs (the GUIDE light).</summary>
        public Action GuidePressed;
        public Func<bool> GuideRunning;

        /// <summary>The DeckButton of a LoopControl (layer: the light's index).</summary>
        public static DeckButton ButtonOf(LoopControl control, int layer)
        {
            switch (control)
            {
                case LoopControl.Loop: return DeckButton.Loop;
                case LoopControl.Stop: return DeckButton.Stop;
                case LoopControl.Undo: return DeckButton.Undo;
                case LoopControl.Clear: return DeckButton.Clear;
                case LoopControl.Slow: return DeckButton.Slow;
                case LoopControl.Medium: return DeckButton.Medium;
                case LoopControl.Fast: return DeckButton.Fast;
                case LoopControl.Bars2: return DeckButton.Bars2;
                case LoopControl.Bars4: return DeckButton.Bars4;
                case LoopControl.Click: return DeckButton.Click;
                case LoopControl.Tidy: return DeckButton.Tidy;
                case LoopControl.Rec: return DeckButton.Rec;
                case LoopControl.Play: return DeckButton.Play;
                case LoopControl.Guide: return DeckButton.Guide;
                default: return DeckButton.Layer1 + Mathf.Clamp(layer, 0, Looper.MaxLayers - 1);
            }
        }

        // ---------------- IInstrument ----------------
        public InstrumentKind Kind => InstrumentKind.Table;
        public int TickOrder => 30;
        public bool AcceptsStickPointers => false;
        public bool IsPresent => _root != null && _root.gameObject.activeInHierarchy;

        // ---- IContactSurface (10-upgrade/experience.md 2.1): the deck's top, its two tapes and every control at its CURRENT
        // depth stop the drawn finger (E1) and take its contact shadow (E3), as the piano's keys and the menu's face do. Without
        // it a fingertip pressing a peg or a 6 mm cap went on into the deck (a real finger meets no resistance there) and
        // the drawn finger vanished inside the body: "the hands are not properly appearing on the tape deck". Visual only:
        // the presses still read the raw joints.
        /// <summary>The ebony top (y of its upper face), the tapes' top, their radius; a footprint reaches this far past an
        /// edge (a fingertip on the rim of a cap still rests on it).</summary>
        public const float TopY = 0.074f, TapeTopY = 0.082f, TapeR = 0.05f, ContactMargin = 0.002f, BodyHalfX = 0.135f;
        /// <summary>The table rig is held (the module sets it): no contact while the deck moves.</summary>
        public Func<bool> Held;

        /// <summary>The surface under (or just above) a world point: the highest of the deck top, a tape and the control
        /// (peg or cap, as sunk now) whose footprint holds the point. Allocation-free.</summary>
        public bool QueryContact(Vector3 world, float above, float below, out ContactHit hit)
        {
            hit = default;
            if (!IsPresent) return false;
            if (Held != null && Held()) return false;
            Vector3 l = _root.InverseTransformPoint(world);
            float m = ContactMargin;
            if (l.x < -BodyHalfX - m || l.x > BodyHalfX + m || l.z < FrontZ - m || l.z > BackZ + m) return false;
            float top = TopY;
            for (int i = 0; i < ReelLocal.Length; i++)
            {
                float dx = l.x - ReelLocal[i].x, dz = l.z - ReelLocal[i].z;
                if (dx * dx + dz * dz <= TapeR * TapeR) top = Mathf.Max(top, TapeTopY);
            }
            for (int i = 0; i < _controls.Length; i++)
            {
                Control c = _controls[i];
                if (c == null) continue;
                float r = (c.Peg ? PegRBottom : CapR) + m, dx = l.x - c.X, dz = l.z - c.Z;
                if (dx * dx + dz * dz > r * r) continue;
                top = Mathf.Max(top, c.LocalPosition.y + (c.Peg ? PegH : CapH) * 0.5f);
            }
            float h = (l.y - top) * _root.lossyScale.y;
            if (h > above || h < -below) return false;
            Vector3 n = _root.up;
            hit.Point = world - n * h;
            hit.Normal = n;
            hit.Height = h;
            hit.Use = ContactUse.Both;
            return true;
        }
        public Transform Root => _root;
        /// <summary>The pegs' renderer and the lamps' (their lit caps are pressed too).</summary>
        public IReadOnlyList<Renderer> PlayedParts => _played;
        /// <summary>The deck's grab handles (body, top, cheeks, hubs, tapes, head bar, spokes): the table rig adds them to its own.</summary>
        public IReadOnlyList<Renderer> HandleRenderers => _handles;

        // ---- read-backs (tests) ----
        public float PegDepth(DeckButton b) => _controls[(int)b].Depth;
        public Vector3 PegLocalPosition(DeckButton b) => _controls[(int)b].LocalPosition;
        /// <summary>The press target of a control in deck-local coordinates (its centre at rest).</summary>
        public static Vector3 ButtonLocal(DeckButton b) => Layout(b);
        public float ReelAngle => _reelAngle;
        /// <summary>The spokes' rotation about reel <paramref name="i"/>'s own axis (both reels turn together).</summary>
        public Quaternion ReelLocalRotation(int i) => _reelRotation;
        public DeckLabels Labels => _labels;
        public Mesh LabelMesh => _labels.Mesh;
        /// <summary>The merged meshes: pegs (LOOP REC PLAY STOP UNDO), lamps (main, count, lit caps), chrome (head bar, spokes).</summary>
        public MergedMesh PegParts => _pegMesh;
        public MergedMesh LampParts => _lampMesh;
        public MergedMesh ChromeParts => _chrome;
        public int SpokePart(int reel) => _spokePart[reel];
        public int PegPart(DeckButton b) => _controls[(int)b].Part;
        public bool IsPeg(DeckButton b) => _controls[(int)b].Peg;
        /// <summary>The lamp texel of a lit cap (-1: a peg or UNDO).</summary>
        public int LampOf(DeckButton b) => _controls[(int)b].Lamp;
        public Texture2D LampTexture => _lampTex;
        /// <summary>A lamp's current glow (sRGB, black = off) and the main lamp's colour as seen (glass when off).</summary>
        public Color32 LampGlow(int texel) => _lampPx[texel];
        public Color32 CapGlow(DeckButton b) => _controls[(int)b].Lamp >= 0 ? _lampPx[_controls[(int)b].Lamp] : new Color32(0, 0, 0, 255);
        public Color LampColor
        {
            get
            {
                Color32 g = _lampPx[MainLamp];
                return g.r == 0 && g.g == 0 && g.b == 0 ? LampIdle : (Color)g;
            }
        }
        public bool TapAgainShown => _againWord >= 0 && _labels.Shown(_againWord);
        public int Presses { get; private set; }
        public Material LampMaterial => _lamp;

        // The fixed layout of every control (also for the static ButtonLocal before a deck exists).
        static Vector3 Layout(DeckButton b)
        {
            switch (b)
            {
                case DeckButton.Rec: return new Vector3(ButtonX[0], PegY, PegZ);
                case DeckButton.Play: return new Vector3(ButtonX[1], PegY, PegZ);
                case DeckButton.Stop: return new Vector3(ButtonX[2], PegY, PegZ);
                case DeckButton.Loop: return new Vector3(LoopX, PegY, PegZ);
                case DeckButton.Undo: return new Vector3(-0.105f, CapY, Row2Z);
                case DeckButton.Clear: return new Vector3(0.105f, CapY, Row2Z);
                case DeckButton.Slow: return new Vector3(-0.108f, CapY, Row3Z);
                case DeckButton.Medium: return new Vector3(-0.081f, CapY, Row3Z);
                case DeckButton.Fast: return new Vector3(-0.054f, CapY, Row3Z);
                case DeckButton.Bars2: return new Vector3(-0.016f, CapY, Row3Z);
                case DeckButton.Bars4: return new Vector3(0.011f, CapY, Row3Z);
                case DeckButton.Click: return new Vector3(0.048f, CapY, Row3Z);
                case DeckButton.Tidy: return new Vector3(0.076f, CapY, Row3Z);
                case DeckButton.Guide: return new Vector3(0.106f, CapY, Row3Z);
                default: return new Vector3(-0.07f + 0.028f * (b - DeckButton.Layer1), CapY, Row2Z);
            }
        }

        /// <summary>ILooper.TryGetControl: a control's world position (its top) and the deck's up (Sunny's demo anchors).</summary>
        public bool TryGetControl(LoopControl control, int layer, out Vector3 world, out Vector3 up)
        {
            DeckButton b = ButtonOf(control, layer);
            Control c = _controls[(int)b];
            world = _root.TransformPoint(new Vector3(c.X, c.Top, c.Z));
            up = _root.up;
            return IsPresent;
        }

        /// <summary>A press on a peg or on the loop strip.</summary>
        public void Press(DeckButton id)
        {
            Presses++;
            if (_recorder == null) return;
            switch (id)
            {
                case DeckButton.Rec:
                    // REC while a take plays records on top of it (the take goes on); otherwise a new take, as ever.
                    if (_rec != null && _rec.CanRecordOnTop && _rec.RecordOnTop()) return;
                    StopsPlayback("REC");
                    _recorder.Record();
                    return;
                case DeckButton.Play:
                {
                    // One line per PLAY and what came of it (the headset log tells "nothing happens" apart).
                    bool ok = _recorder.Play();
                    Debug.Log($"[Lessons] deck PLAY: {(ok ? "plays " : "nothing played: ")}{_recorder.LastPlay}");
                    return;
                }
                case DeckButton.Stop: StopsPlayback("STOP"); _recorder.Stop(); return;
                case DeckButton.Loop: _rec?.Loop(); return;
                case DeckButton.Guide: GuidePressed?.Invoke(); return;
            }
            if (_looper == null) return;
            switch (id)
            {
                case DeckButton.Undo: _looper.Undo(); break;
                case DeckButton.Clear: _looper.Clear(); break;
                case DeckButton.Slow: _looper.SetTempo(0); break;
                case DeckButton.Medium: _looper.SetTempo(1); break;
                case DeckButton.Fast: _looper.SetTempo(2); break;
                case DeckButton.Bars2: _looper.SetBars(2); break;
                case DeckButton.Bars4: _looper.SetBars(4); break;
                case DeckButton.Click: _looper.ToggleMetronome(); break;
                case DeckButton.Tidy: _looper.ToggleTidy(); break;
                default: _looper.ToggleMute(id - DeckButton.Layer1); break;
            }
        }

        void StopsPlayback(string button)
        {
            if (_rec == null || !_rec.PlaybackOn) return;
            Debug.Log($"[Lessons] deck {button} pressed: the playback stops after {_rec.PlaybackSeconds:F1} s");
        }

        // What holds a control down by itself (latched).
        bool Latched(DeckButton id)
        {
            RecorderState s = _recorder != null ? _recorder.State : RecorderState.Idle;
            bool loop = _looper != null && _looper.Active;
            switch (id)
            {
                case DeckButton.Loop: return loop;
                case DeckButton.Rec: return loop ? _looper.State == LoopState.Recording || _looper.Overdubbing : s == RecorderState.Recording || (_rec != null && _rec.RecordingOnTop);
                case DeckButton.Play: return s == RecorderState.Playing;
                default: return false;
            }
        }

        /// <summary>The deck moved this far (m, deg) between two ticks, or during a press: it jumped (a layout put the side
        /// table somewhere else at once, a glide ended), not a hand moving it.</summary>
        public const float JumpMetres = 0.01f, JumpDegrees = 4f;
        Vector3 _lastPos;
        Quaternion _lastRot = Quaternion.identity;
        bool _hasPose;
        /// <summary>Times the deck jumped under the fingertips (tests, logs).</summary>
        public int Jumps { get; private set; }

        bool MovedFrom(Vector3 pos, Quaternion rot) =>
            (_root.position - pos).sqrMagnitude > JumpMetres * JumpMetres || Quaternion.Angle(_root.rotation, rot) > JumpDegrees;

        // The deck jumped under the fingertips (2026-11-16 headset test: the quick show's PLAY brought the instruments in front
        // in mixed reality, which put the side table at its new place in the same frame; the fingertip resting on PLAY then
        // sat in STOP's press zone and stopped the playback it had just started, "nothing happens"). Whatever control a
        // fingertip is in now was not pressed by it: it counts as held (it presses after it leaves and comes back).
        void Reseat(IReadOnlyList<Pointer> pointers, IDeckDesktop desk)
        {
            Jumps++;
            for (int bi = 0; bi < ControlCount; bi++)
            {
                Control b = _controls[bi];
                b.Held.Clear();
                for (int i = 0; i < pointers.Count; i++)
                {
                    Pointer p = pointers[i];
                    if (!p.Active || (desk != null && desk.IsMouse(p))) continue;
                    if (Inside(b, p.Tip)) b.Held.Add(p);
                }
            }
        }

        bool Inside(Control b, Vector3 tip, float margin = 0f)
        {
            Vector3 l = _root.InverseTransformPoint(tip);
            float dx = l.x - b.X, dz = l.z - b.Z, r = b.R + margin;
            return dx * dx + dz * dz < r * r && l.y < b.YMax + margin && l.y > PressYMin - margin;
        }

        // One poke, one press (2026-11-18 headset log: "record on top ... at 1.1 s" then "deck REC pressed" 0.2 s later, and
        // every PLAY logged twice 0.1 s apart: the user's REC started recording on top and stopped it at once, "nothing new was
        // played"). A fingertip wobbling at a control's edge left and came back, or a second finger of the same poke arrived a
        // moment later, and each counted as a new press. Now: a fingertip lets go of a control only once it is ReleaseMargin
        // clear of its press zone; a control is pressed only by the first fingertip in it; and a control pressed in the last
        // RepressSeconds ignores another press unless every fingertip went ClearMargin away from it in between (a real second
        // tap: the finger lifted off and came down again).
        public const float ReleaseMargin = 0.012f, RepressSeconds = 0.5f, ClearMargin = 0.02f;

        /// <summary>Presses, travel, reels, lamps.</summary>
        public void Tick(IReadOnlyList<Pointer> pointers, float dt)
        {
            _time += dt;
            IDeckDesktop desk = Desktop;
            // A Shift / Alt press is the rig's move / turn grab and returns before any instrument's
            // DesktopDown, so a modifier click on a control never presses it.
            if (desk != null && desk.TryClick(out Ray ray) && !desk.ModifierHeld) DesktopDown(ray);
            Vector3 pos = _root.position;
            Quaternion rot = _root.rotation;
            if (_hasPose && MovedFrom(_lastPos, _lastRot)) Reseat(pointers, desk);   // moved since the last tick: no press from that
            bool pressing = true;
            for (int bi = 0; bi < ControlCount; bi++)
            {
                Control b = _controls[bi];
                bool near = false;
                for (int i = 0; i < pointers.Count; i++)
                {
                    Pointer p = pointers[i];
                    if (!p.Active || !p.WasActive) { b.Held.Remove(p); continue; }   // a lost fingertip holds nothing
                    if (!near && (desk == null || !desk.IsMouse(p)) && Inside(b, p.Tip, ClearMargin)) near = true;
                    if (desk != null && desk.IsMouse(p)) continue;              // the mouse hand never presses: clicks do
                    if (Inside(b, p.Tip))
                    {
                        if (!b.Held.Contains(p))
                        {
                            bool first = b.Held.Count == 0;
                            b.Held.Add(p);
                            if (!pressing || !first || (_time - b.LastPress < RepressSeconds && !b.Cleared)) continue;
                            b.LastPress = _time;
                            b.Cleared = false;
                            Press(b.Id);
                            // The press moved the deck (PLAY's playback brought its instruments and the table moved at once):
                            // nothing more is pressed this frame, and the fingertips are where they are now.
                            if (MovedFrom(pos, rot)) { pressing = false; Reseat(pointers, desk); }
                        }
                    }
                    else if (!Inside(b, p.Tip, ReleaseMargin)) b.Held.Remove(p);
                }
                if (!near) b.Cleared = true;
                float target = b.Held.Count > 0 || Latched(b.Id) ? 1f : 0f;
                if (b.Depth != target)
                {
                    b.Depth += (target - b.Depth) * Mathf.Min(1f, dt * PegRate);
                    if (Mathf.Abs(target - b.Depth) < 1e-4f) b.Depth = target;   // settle, so an idle deck writes no transforms
                    b.LocalPosition = new Vector3(b.X, b.Rest.y - b.Depth * b.Travel, b.Z);
                    (b.Lamp >= 0 ? _lampMesh : _pegMesh).Pose(b.Part, Matrix4x4.Translate(b.LocalPosition));
                }
            }
            _lastPos = _root.position;
            _lastRot = _root.rotation;
            _hasPose = true;
            RecorderState st = _recorder != null ? _recorder.State : RecorderState.Idle;
            if (st != RecorderState.Idle)
            {
                _reelAngle = Mathf.Repeat(_reelAngle + dt * (st == RecorderState.Recording ? ReelRadPerSecRecording : ReelRadPerSecPlaying), Mathf.PI * 2f);
                // right-handed rotation.y = +angle turns the other way in Unity's mirrored frame: yaw -angle (spin_unity -3 / -4).
                _reelRotation = Quaternion.Euler(0f, -_reelAngle * Mathf.Rad2Deg, 0f);
                for (int i = 0; i < 2; i++) _chrome.Pose(_spokePart[i], Matrix4x4.TRS(ReelLocal[i], _reelRotation, Vector3.one));
            }
            // One upload per moved mesh per frame, none on an idle deck.
            _pegMesh.Upload();
            _lampMesh.Upload();
            _chrome.Upload();
            UpdateLamps(false);
            _labels.Upload();
        }

        /// <summary>DesktopDown: a left-button press whose nearest play-surface hit is one of our controls presses
        /// it (another module's nearer target, or nothing, leaves the click alone). Returns whether a control was pressed.</summary>
        public bool DesktopDown(Ray ray)
        {
            if (!IsPresent) return false;
            // The nearest play-surface hit of any module decides (ARCHITECTURE.md section 0): ours only if it is a control.
            if (!Physics.Raycast(ray, out RaycastHit hit, ClickReach, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide)) return false;
            for (int i = 0; i < ControlCount; i++)
            {
                if (hit.collider != _controls[i].Click) continue;
                Press(_controls[i].Id);
                return true;
            }
            return false;
        }

        /// <summary>The desktop click target of a control (tests).</summary>
        public Collider PegCollider(DeckButton b) => _controls[(int)b].Click;

        /// <summary>A glow over a control a fingertip is about to touch.</summary>
        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow)
        {
            if (glow == null) return;
            Vector3 up = _root.up;
            for (int bi = 0; bi < ControlCount; bi++)
            {
                Control b = _controls[bi];
                float hr = b.Peg ? HoverR : CapHoverR, top = b.Peg ? HoverTop : CapY + CapH * 0.5f;
                for (int i = 0; i < pointers.Count; i++)
                {
                    Pointer p = pointers[i];
                    if (!p.Active) continue;
                    Vector3 l = _root.InverseTransformPoint(p.Tip);
                    float h = l.y - top;
                    if (h < -HoverBelow || h > HoverAbove) continue;
                    float dx = l.x - b.X, dz = l.z - b.Z;
                    if (dx * dx + dz * dz > hr * hr) continue;
                    float size = b.Peg ? HoverGlowSize : CapGlowSize;
                    Vector3 pos = _root.TransformPoint(b.X, b.Peg ? HoverGlowY : top + 0.002f, b.Z);
                    glow.Add(HoverGlowSpec.At(pos, up, size, size, 1f - Mathf.Max(0f, h) / HoverAbove));
                }
            }
        }

        /// <summary>overTableControls (deck half): within 3.5 cm of a peg (2.5 cm of a cap), 6-17 cm over the base.</summary>
        public PlaySurface PlaySurfaceAt(Vector3 world)
        {
            if (!IsPresent) return PlaySurface.None;
            Vector3 l = _root.InverseTransformPoint(world);
            if (l.y <= SurfaceYMin || l.y >= SurfaceYMax) return PlaySurface.None;
            for (int i = 0; i < ControlCount; i++)
            {
                Control c = _controls[i];
                float r = c.Peg ? SurfaceR : CapSurfaceR;
                float dx = l.x - c.X, dz = l.z - c.Z;
                if (dx * dx + dz * dz < r * r) return PlaySurface.Table;
            }
            return PlaySurface.None;
        }

        public void Destroy()
        {
            foreach (Mesh m in _meshes) LessonUtil.Destroy(m);
            _meshes.Clear();
            LessonUtil.Destroy(_lampTex);
            if (_root != null) LessonUtil.Destroy(_root.gameObject);
        }

        // ---------------- lamps ----------------

        // The main lamp (red recording, green playing, black idle; it blinks red while a layer is
        // armed or LOOP waits for the first note); the loop strip from the looper; one texture upload when anything changed.
        void UpdateLamps(bool force)
        {
            RecorderState s = _recorder != null ? _recorder.State : RecorderState.Idle;
            Looper lp = _looper;
            bool loop = lp != null && lp.Active;
            bool blink = Mathf.Repeat(_time, 0.6f) < 0.3f;
            Color32 off = new Color32(0, 0, 0, 255);
            Color32 main = off;
            if (Latched(DeckButton.Rec)) main = LampRecording;
            else if (loop && (lp.State == LoopState.Waiting || lp.Armed)) main = blink ? (Color32)LampRecording : (lp.State == LoopState.Looping ? (Color32)LampPlaying : off);
            else if (s == RecorderState.Playing) main = LampPlaying;
            _lampWant[MainLamp] = main;

            int lit = 0;
            bool bright = false;
            if (lp != null) lit = lp.CountLights(out bright);
            for (int i = 0; i < 4; i++) _lampWant[CountLamp0 + i] = i < lit ? (bright ? CountBright : CountDim) : off;

            for (int i = 0; i < ControlCount; i++)
            {
                Control c = _controls[i];
                if (c.Lamp < 0) continue;
                Color32 g = off;
                if (lp != null)
                {
                    switch (c.Id)
                    {
                        case DeckButton.Clear: g = lp.ClearArmed && blink ? ClearWarn : off; break;
                        case DeckButton.Slow: g = lp.Bpm == Looper.TempoBpm[0] ? Selected : off; break;
                        case DeckButton.Medium: g = lp.Bpm == Looper.TempoBpm[1] ? Selected : off; break;
                        case DeckButton.Fast: g = lp.Bpm == Looper.TempoBpm[2] ? Selected : off; break;
                        case DeckButton.Bars2: g = lp.Bars == 2 ? Selected : off; break;
                        case DeckButton.Bars4: g = lp.Bars == 4 ? Selected : off; break;
                        case DeckButton.Click: g = lp.Metronome ? Selected : off; break;
                        case DeckButton.Tidy: g = lp.Tidy ? Selected : off; break;
                        case DeckButton.Guide: g = GuideRunning != null && GuideRunning() ? Selected : off; break;
                        default:
                        {
                            int k = c.Id - DeckButton.Layer1;
                            if (lp.LimitFlash) { g = blink ? CountBright : off; break; }
                            switch (lp.Lamp(k))
                            {
                                case LayerLamp.On: g = LayerOn; break;
                                case LayerLamp.Muted: g = LayerMuted; break;
                                case LayerLamp.Recording: g = LayerRec; break;
                                case LayerLamp.Armed: g = blink ? LayerRec : off; break;
                            }
                            break;
                        }
                    }
                }
                _lampWant[c.Lamp] = g;
            }
            bool changed = force;
            for (int i = 0; i < LampTexels; i++)
            {
                Color32 a = _lampPx[i], b = _lampWant[i];
                if (a.r == b.r && a.g == b.g && a.b == b.b) continue;
                _lampPx[i] = b;
                changed = true;
            }
            if (changed)
            {
                _lampTex.SetPixels32(_lampPx);
                _lampTex.Apply(false, false);
            }
            // CLEAR waiting for its second tap: its label reads TAP AGAIN.
            bool again = lp != null && lp.ClearArmed;
            _labels.Show(_clearWord, !again);
            _labels.Show(_againWord, again);
        }

        static Material Metal(MaterialLibrary materials, Color color, float smoothness)
        {
            Material m = materials.Clone(MaterialLibrary.LitMetal);
            m.SetColor(MaterialLibrary.BaseColor, color);
            m.SetFloat(MaterialLibrary.Smoothness, smoothness);
            m.SetFloat(MaterialLibrary.Metallic, 1f);
            return m;
        }

        Mesh Keep(Mesh m) { _meshes.Add(m); return m; }

        Renderer Part(string name, Mesh mesh, Material mat, bool handle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;   // no realtime shadows in VR (ARCHITECTURE section 0)
            if (handle) _handles.Add(r);
            return r;
        }

        /// <summary>A throw-away mesh (CombineMeshes input) destroyed at the end of a using block.</summary>
        readonly struct TempMesh : System.IDisposable
        {
            public readonly Mesh Mesh;
            public TempMesh(Mesh m) { Mesh = m; }
            public void Dispose() => LessonUtil.Destroy(Mesh);
        }
    }
}
