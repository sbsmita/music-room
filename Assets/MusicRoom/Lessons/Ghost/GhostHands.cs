// Ghost hands play it back (the playback show, 2026-11-11): while a recording or a loop plays back, a pair of see-through
// glowing hands at each instrument that plays (and is shown) plays the notes: fingers press the piano's keys (a fingering
// from the notes' places: PianoFingering), held sticks strike the drums and cymbals, a finger plucks the harp's strings,
// fingers tap the handpan's notes, a flat hand slaps the hand drums, mallets (or a finger) strike the marimba's bars, and
// a finger strikes or circles the singing bowls' rims. The hands read the playback ahead (Recorder.Upcoming: a take's
// events, the looping layers as the looper will send them, Horizon s ahead), so every stroke is planned before it sounds
// and the hand arrives on time; they rest a hand's width above the instrument between phrases, fade in when a hand's first
// note comes near and fade out after its last (and when the playback ends).
//   - Playback only: a take or loop sending back what was recorded. Never REC or plain live play (nothing is read ahead
//     then). A loop's layers looping while the player overdubs are a playback too: the ghost plays the earlier layers.
//   - Only on instruments that are shown (ILayoutService.PlaceOf Seat or Gliding: playback brings its instruments in front;
//     in mixed reality the others are hidden and get no hands).
//   - Never input: plain mesh, no collider, no pointer; nothing reads them.
//   - The player's own hand on the same place: the ghost hand fades to OverlapAlpha (still faintly there, so it comes back
//     smoothly and still shows the fingering, but the player's hand is clearly the one that is theirs). Within HeadNear of
//     the head it fades out (the head stays clear).
//   - Sunny says tip_ghost_hands once, when ghost hands have played for TipAfter s (after the room's listen-back line).
// Drawing: GhostHandsMesh (one mesh, one renderer, up to MaxHands hands at once). Allocation-free per frame after setup (a
// playback's start re-reads the instruments' anchors once).
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Lessons.Ghost
{
    public sealed class GhostHands
    {
        /// <summary>How far ahead the playback is read (s): more than a strike hand's arc (GhostTrack.ArcGap) and approach.</summary>
        public const float Horizon = 1.2f;
        /// <summary>A hand shows from Appear s before its first note until Linger s after its last.</summary>
        public const float Appear = 0.9f, Linger = 2.2f;
        public const float FadeIn = 0.45f, FadeOut = 0.8f;
        /// <summary>The player's fingertip within OverlapNear of the ghost hand: it shows at OverlapAlpha; beyond OverlapFar fully.</summary>
        public const float OverlapNear = 0.07f, OverlapFar = 0.16f, OverlapAlpha = 0.15f, OverlapRate = 6f;
        /// <summary>The head stays clear: nothing of a ghost hand within HeadNear of it, full from HeadFar.</summary>
        public const float HeadNear = 0.32f, HeadFar = 0.45f;
        /// <summary>Two strokes of one hand closer than this (s): the other hand takes the second (sticking, a roll).</summary>
        public const float MinRepeat = 0.11f;
        /// <summary>Sunny's line, once ghost hands have played this long (s); asked again every TipEvery while she is busy.
        /// Later than the room's own listen-back line (asked 1.5 s into a playback), which goes first.</summary>
        public const string TipId = "tip_ghost_hands";
        public const float TipAfter = 4f, TipEvery = 4f;
        /// <summary>The piano hand above the keys (piano-local, m): the palm's height, its centre's z, its pitch (deg).</summary>
        public const float PalmY = 0.052f, PalmZ0 = -0.067f, PalmPitch = 8f, PalmYaw = 6f;
        /// <summary>Where a finger pad meets a key: white keys this far from the front edge; black keys this far behind
        /// theirs; a fingertip's centre rides this far above the key top. An idle finger hovers Hover above.</summary>
        public const float WhiteStrikeZ = 0.03f, BlackStrikeIn = 0.022f, PadAbove = 0.0065f, Hover = 0.014f;
        /// <summary>Per finger, how much nearer the keys' front edge it plays (m): the short thumb and little finger reach
        /// the keys nearer the front, as a pianist's do.</summary>
        public static readonly float[] FingerFront = { 0.02f, 0f, 0f, 0f, 0.01f };

        const int Kinds = 10, Tracks = Kinds * 2, PeekMax = 512;

        readonly App _app;
        readonly GhostTrack[] _tracks = new GhostTrack[Tracks];
        readonly float[] _alpha = new float[Tracks], _overlap = new float[Tracks];
        readonly int[] _slot = new int[Tracks];
        readonly int[] _owner = new int[GhostHandsMesh.MaxHands];
        readonly TakeEvent[] _peek = new TakeEvent[PeekMax];
        readonly double[] _when = new double[PeekMax];
        readonly Vector3[] _contactW = new Vector3[Tracks], _palmW = new Vector3[Tracks];
        readonly Vector3[,] _tipW = new Vector3[2, GhostHandModel.Fingers];
        readonly Transform[] _root = new Transform[Kinds];
        readonly bool[] _shown = new bool[Kinds];
        // anchor caches (instrument-local point / up), filled on first use in a playback
        readonly Vector3[,] _anchorPoint = new Vector3[Kinds, 128], _anchorUp = new Vector3[Kinds, 128];
        readonly sbyte[,] _anchorHas = new sbyte[Kinds, 128];
        readonly string[] _marimbaIds = new string[128], _handpanIds = new string[10];
        static readonly string[] BowlRim = { "bowl.big.rim", "bowl.middle.rim", "bowl.small.rim" };
        readonly Action<RecorderStateEvent> _onState;
        readonly FingerPose[] _pose = new FingerPose[GhostHandModel.Fingers];

        Recorder _rec;
        ILayoutService _layout;
        IGuide _guide;
        IPianoApi _piano;
        IDrumsApi _drums;
        IHarpApi _harp;
        readonly List<IGuideAnchors> _anchors = new List<IGuideAnchors>(16);
        PianoFingering _fingering;
        float _blackFrontZ = 0.055f;
        bool _playing, _tipDone;
        double _peekedTo;
        float _time, _shownFor, _tipAskAt;

        readonly GameObject _go;
        readonly MeshRenderer _renderer;
        readonly Material _material;
        readonly GhostHandsMesh _mesh;

        /// <summary>Hands drawn this frame.</summary>
        public int ShownHands { get; private set; }
        /// <summary>Strokes planned so far (tests).</summary>
        public int Planned { get; private set; }
        public bool Playing => _playing;
        public bool TipAsked => _tipDone;
        public MeshRenderer Renderer => _renderer;
        public GhostHandsMesh Mesh => _mesh;
        public PianoFingering Fingering => _fingering;

        public GhostHands(App app)
        {
            _app = app;
            for (int i = 0; i < Tracks; i++) { _tracks[i] = new GhostTrack(); _slot[i] = -1; _overlap[i] = 1f; }
            for (int h = 0; h < GhostHandsMesh.MaxHands; h++) _owner[h] = -1;
            for (int i = 0; i < 10; i++) _handpanIds[i] = i < 9 ? "handpan.field." + i : "handpan.tak";
            for (int m = 0; m < 128; m++) _marimbaIds[m] = "bar." + NoteNames[m % 12] + (m / 12 - 1);
            _onState = OnState;
            app.Events.Subscribe(_onState);
            if (app.Headless) return;
            _mesh = new GhostHandsMesh();
            _go = new GameObject("GhostHands");
            _go.transform.SetParent(app.WorldRoot, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh.Mesh;
            _renderer = _go.AddComponent<MeshRenderer>();
            _material = app.Materials.Clone(GhostHandsMaterials.Template);
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _renderer.enabled = false;
        }

        static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        public GhostTrack Track(InstrumentKind kind, int side)
        {
            if (kind == InstrumentKind.Piano && _fingering != null) return _fingering.Hands[side];
            return _tracks[Index(kind, side)];
        }

        static int Index(InstrumentKind kind, int side) => Mathf.Clamp((int)kind, 0, Kinds - 1) * 2 + (side & 1);

        /// <summary>The hand's opacity now (0 hidden .. 1), before the overlap / head fades.</summary>
        public float Alpha(InstrumentKind kind, int side) => _alpha[Index(kind, side)];
        /// <summary>The overlap fade now (OverlapAlpha .. 1).</summary>
        public float Overlap(InstrumentKind kind, int side) => _overlap[Index(kind, side)];
        /// <summary>Where the hand's contact (fingertip, palm pad, tool tip) was drawn this frame (world).</summary>
        public Vector3 Contact(InstrumentKind kind, int side) => _contactW[Index(kind, side)];
        public Vector3 PalmWorld(InstrumentKind kind, int side) => _palmW[Index(kind, side)];
        /// <summary>A piano ghost finger's tip as drawn this frame (world; side 0 left, 1 right; f 0 thumb .. 4 little).</summary>
        public Vector3 PianoTip(int side, int f) => _tipW[side & 1, f];

        void OnState(RecorderStateEvent e) { if (e.State != RecorderState.Playing && _playing) Stop(); }

        // ================================================================== the frame

        /// <summary>Once a frame after the instruments moved (the module's EndFrame).</summary>
        public void Tick(float dt)
        {
            _time += dt;
            double now = _app.AudioTime;
            if (_rec == null) _rec = _app.Get<IRecorder>() as Recorder;
            bool playing = _rec != null && _rec.State == RecorderState.Playing;
            if (playing && !_playing) Begin(now);
            else if (!playing && _playing) Stop();
            if (_playing) ReadAhead(now);
            Show(dt, now);
            AskSunny(dt);
        }

        void Begin(double now)
        {
            _playing = true;
            _peekedTo = now - 0.25;   // what was sent this frame still gets its hand
            Bind();
            for (int k = 0; k < Kinds; k++) for (int m = 0; m < 128; m++) _anchorHas[k, m] = 0;
        }

        void Stop()
        {
            _playing = false;
            double now = _app.AudioTime;
            _fingering?.Stop(now);
            for (int i = 0; i < Tracks; i++) { _tracks[i].DropAfter(now); _tracks[i].ReleaseAll(now); }
        }

        void Bind()
        {
            _layout = _app.Get<ILayoutService>();
            _piano = _app.Get<IPianoApi>();
            _drums = _app.Get<IDrumsApi>();
            _harp = _app.Get<IHarpApi>();
            _anchors.Clear();
            _anchors.AddRange(_app.GetAll<IGuideAnchors>());
            for (int k = 0; k < Kinds; k++) _root[k] = null;
            if (_piano != null) _root[(int)InstrumentKind.Piano] = _piano.Root;
            if (_drums != null) _root[(int)InstrumentKind.Drums] = _drums.Root;
            if (_harp != null) _root[(int)InstrumentKind.Harp] = _harp.Root;
            IReadOnlyList<IInstrument> all = _app.Instruments;
            for (int i = 0; i < all.Count; i++)
            {
                IInstrument inst = all[i];
                int k = inst != null ? (int)inst.Kind : -1;
                if (k < 0 || k >= Kinds || _root[k] != null) continue;
                _root[k] = inst.Root;
            }
            if (_piano != null && _fingering == null && _piano.TryGetKey(PianoSpecMiddleC, out PianoKeyInfo c)
                && _piano.TryGetKey(53, out PianoKeyInfo f3) && _piano.TryGetKey(67, out PianoKeyInfo g4))
                _fingering = new PianoFingering(c.X, f3.X, g4.X);
            if (_piano != null && TryAnchor(InstrumentKind.Piano, "key.61", out Vector3 w, out _))
            {
                // a black key's top centre is half its length in front of the hinge (L)
                float centre = _piano.Root.InverseTransformPoint(w).z;
                _blackFrontZ = _piano.L - 2f * (_piano.L - centre);
            }
        }

        const int PianoSpecMiddleC = 60;

        void ReadAhead(double now)
        {
            if (_rec == null) return;
            double to = now + Horizon;
            if (to <= _peekedTo) return;
            int n = _rec.Upcoming(_peekedTo, to, _peek, _when);
            for (int i = 0; i < n; i++) Plan(_peek[i], _when[i]);
            _peekedTo = n == PeekMax ? _when[n - 1] + 1e-6 : to;
            _fingering?.Flush(_peekedTo);
            for (int i = 0; i < Tracks; i++) _tracks[i].Prune(now);
            if (_fingering != null) { _fingering.Hands[0].Prune(now); _fingering.Hands[1].Prune(now); }
        }

        // ================================================================== planning

        void Plan(in TakeEvent e, double when)
        {
            EventRoles roles = _rec.Roles;
            InstrumentKind kind = roles.KindOf(e.type);
            int k = (int)kind;
            if (k < 0 || k >= Kinds || _root[k] == null) return;
            EventTypeSpec spec = roles.Spec(e.type);
            EventRole role = spec.Type != null ? spec.Role : roles.RoleOf(e.type);
            switch (kind)
            {
                case InstrumentKind.Piano: PlanPiano(e, when); return;
                case InstrumentKind.Drums: if (e.type == TakeEventType.Drum) PlanDrum(e, when); return;
                case InstrumentKind.Harp: if (e.type == TakeEventType.Harp) PlanHarp(e, when); return;
            }
            if (spec.Key == EventKey.Lane)
            {
                if (role == EventRole.Onset) PlanRub(kind, e, when);
                else if (role == EventRole.Release) { _tracks[Index(kind, 0)].Release(e.lane, when); _tracks[Index(kind, 1)].Release(e.lane, when); }
                return;
            }
            if (role != EventRole.Onset) return;
            switch (kind)
            {
                case InstrumentKind.Handpan:
                {
                    int i = Mathf.Clamp(e.i, 0, 9);
                    if (!Anchor(kind, i, _handpanIds[i], out Vector3 p, out Vector3 up)) return;
                    Strike(kind, when, p, up, e.mode == "slap" ? StrokeStyle.Slap : StrokeStyle.Tap, Mathf.Clamp01(e.vel), -1);
                    return;
                }
                case InstrumentKind.HandDrums:
                {
                    int d = e.kind == "tumba" ? 1 : 0;
                    if (!Anchor(kind, d, d == 0 ? "conga" : "tumba", out Vector3 p, out Vector3 up)) return;
                    Vector3 c = Toward(kind, p, up, Mathf.Clamp01(e.pos) * 0.6f * AnchorRadius(kind, d));
                    Strike(kind, when, c, up, StrokeStyle.Slap, Mathf.Clamp01(e.vel), e.i == 0 || e.i == 1 ? e.i : -1);
                    return;
                }
                case InstrumentKind.Marimba:
                {
                    int m = Mathf.Clamp(e.midi, 0, 127);
                    if (!Anchor(kind, m, _marimbaIds[m], out Vector3 p, out Vector3 up)) return;
                    Strike(kind, when, p, up, e.mode == "finger" ? StrokeStyle.Tap : StrokeStyle.Mallet, Mathf.Clamp01(e.vel), -1);
                    return;
                }
                case InstrumentKind.Bowls:
                {
                    int b = Mathf.Clamp(e.i, 0, 2);
                    if (!Anchor(kind, b, BowlRim[b], out Vector3 c, out Vector3 up)) return;
                    float r = up.magnitude;
                    Vector3 p = Toward(kind, c, up, r);
                    StrokeStyle st = e.kind == "palm" ? StrokeStyle.Slap : e.kind == "tap" ? StrokeStyle.Tap : StrokeStyle.Mallet;
                    Strike(kind, when, p, up.normalized, st, Mathf.Clamp01(e.vel / 1.5f), -1);
                    return;
                }
                default:
                {
                    // An instrument without a ghost mapping of its own: the hand taps a little above its root.
                    Strike(kind, when, Vector3.up * 0.8f, Vector3.up, StrokeStyle.Tap, Mathf.Clamp01(e.vel), -1);
                    return;
                }
            }
        }

        void PlanPiano(in TakeEvent e, double when)
        {
            if (_fingering == null || _piano == null) return;
            if (e.type == TakeEventType.PianoOff) { _fingering.Release(when, e.midi); return; }
            if (e.type != TakeEventType.PianoOn || !_piano.TryGetKey(e.midi, out PianoKeyInfo key)) return;
            Vector3 p = key.Black
                ? new Vector3(key.X, _piano.BTOP + PadAbove, _blackFrontZ + BlackStrikeIn)
                : new Vector3(key.X, PadAbove, WhiteStrikeZ);
            _fingering.Note(when, e.midi, p, key.Black, Mathf.Clamp01(e.vel));
            Planned++;
        }

        void PlanDrum(in TakeEvent e, double when)
        {
            if (_drums == null) return;
            string kind = e.kind;
            if (kind == null || kind == "kick") return;   // the kick is the nod or the pad: the beater shows it
            if (kind == "hatOpen" || kind == "hatClose") kind = "hat";
            IReadOnlyList<DrumPieceInfo> pieces = _drums.Pieces;
            for (int i = 0; i < pieces.Count; i++)
            {
                DrumPieceInfo pc = pieces[i];
                if (pc.Pad || pc.Kind != kind || pc.Transform == null) continue;
                Transform root = _root[(int)InstrumentKind.Drums];
                Vector3 up = root.InverseTransformDirection(pc.Transform.up).normalized;
                Vector3 centre = root.InverseTransformPoint(pc.Transform.position);
                float pos = e.zone == "rim" || e.zone == "edge" ? 0.92f : e.zone == "bell" ? 0.18f : e.zone == "bow" ? 0.6f : Mathf.Clamp01(e.pos);
                float radius = pc.Radius;
                Vector3 p = Toward(InstrumentKind.Drums, centre, up, pos * radius * 0.9f);
                Strike(InstrumentKind.Drums, when, p, up, StrokeStyle.Stick, Mathf.Clamp01(e.vel), -1);
                return;
            }
        }

        void PlanHarp(in TakeEvent e, double when)
        {
            if (_harp == null || e.i < 0 || e.i >= _harp.StringCount) return;
            Vector3 a = _harp.StringStart(e.i), b = _harp.StringEnd(e.i);
            Vector3 p = e.u > 0.08f && e.u < 0.92f ? Vector3.Lerp(a, b, e.u) : _harp.PlayPoint(e.i);
            int j = e.i + 1 < _harp.StringCount ? e.i + 1 : e.i - 1;
            Vector3 along = (b - a).normalized;
            Vector3 across = _harp.StringStart(j) - a;
            Vector3 n = Vector3.Cross(along, across).normalized;
            Vector3 player = PlayerLocal(InstrumentKind.Harp);
            if (Vector3.Dot(player - p, n) < 0f) n = -n;
            // the hand comes from the player's side of the strings: its palm faces them, its fingers point up the string
            p += n * 0.006f;
            Strike(InstrumentKind.Harp, when, p, n, StrokeStyle.Pluck, Mathf.Clamp01(e.vel), -1, Vector3.ProjectOnPlane(along, n).normalized);
        }

        void PlanRub(InstrumentKind kind, in TakeEvent e, double when)
        {
            if (kind != InstrumentKind.Bowls) return;
            int b = Mathf.Clamp(e.i, 0, 2);
            if (!Anchor(kind, b, BowlRim[b], out Vector3 c, out Vector3 up)) return;
            float r = up.magnitude;
            Vector3 n = up / Mathf.Max(1e-5f, r);
            Vector3 player = PlayerLocal(kind);
            Vector3 toward = Vector3.ProjectOnPlane(player - c, n);
            toward = toward.sqrMagnitude > 1e-8f ? toward.normalized : Vector3.ProjectOnPlane(Vector3.back, n).normalized;
            float speed = e.u > 0.05f ? Mathf.Clamp(e.u, 0.1f, 1.2f) : 0.3f;
            int side = Vector3.Dot(c - player, RightLocal(kind)) < 0f ? 0 : 1;
            _tracks[Index(kind, side)].Add(new GhostStroke
            {
                On = when, Off = double.PositiveInfinity, Point = c, Up = n, Forward = toward, Vel = Mathf.Clamp01(e.vel), Finger = -1,
                Style = StrokeStyle.Rub, Key = e.lane, Radius = r * 1.02f, Spin = speed / Mathf.Max(0.02f, r), Phase = 0f,
            });
            Planned++;
        }

        /// <summary>A strike for one hand of kind (forcedSide 0 / 1, or -1: by the side it is on and who struck last).</summary>
        void Strike(InstrumentKind kind, double when, Vector3 p, Vector3 up, StrokeStyle style, float vel, int forcedSide, Vector3? forward = null)
        {
            Vector3 player = PlayerLocal(kind);
            Vector3 fwd = forward ?? Vector3.ProjectOnPlane(p - player, up);
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.ProjectOnPlane(Vector3.right, up);
            fwd.Normalize();
            int side = forcedSide;
            if (side < 0)
            {
                float lat = Vector3.Dot(p - player, RightLocal(kind));
                int pref = lat < -0.04f ? 0 : lat > 0.04f ? 1 : (_tracks[Index(kind, 0)].LastOn <= _tracks[Index(kind, 1)].LastOn ? 0 : 1);
                int other = 1 - pref;
                double lastPref = _tracks[Index(kind, pref)].LastOn, lastOther = _tracks[Index(kind, other)].LastOn;
                if (when - lastPref < MinRepeat && lastOther < lastPref && Mathf.Abs(lat) < 0.35f) pref = other;
                side = pref;
            }
            _tracks[Index(kind, side)].Add(new GhostStroke
            {
                On = when, Off = when, Point = p, Up = up.normalized, Forward = fwd, Vel = vel, Finger = -1, Style = style, Key = -1,
            });
            Planned++;
        }

        /// <summary>The instrument-local point d metres from centre toward the player, in the surface's plane.</summary>
        Vector3 Toward(InstrumentKind kind, Vector3 centre, Vector3 up, float d)
        {
            Vector3 n = up.normalized;
            Vector3 t = Vector3.ProjectOnPlane(PlayerLocal(kind) - centre, n);
            return t.sqrMagnitude > 1e-8f ? centre + t.normalized * d : centre;
        }

        float AnchorRadius(InstrumentKind kind, int key) => _anchorHas[(int)kind, key] > 0 ? _anchorUp[(int)kind, key].magnitude : 0.1f;

        /// <summary>The ghost drummer sits at the kit's own stool (kit-local: the kit stands KIT_FORWARD in front of its seat,
        /// a seated head's height), facing +z with +x to its right, wherever the kit is. Playback stages the kit out to one
        /// side of the player (LayoutModule.Stage): read from the player's head, every piece was then on the same side and
        /// one hand played every hit (2026-11-18, headset). The kit's frame gives the hat and crash to the left hand, the
        /// toms, floor and ride to the right, and the snare to whichever hand is free.</summary>
        public static readonly Vector3 DrumSeat = new Vector3(0f, 1.15f, -0.10f);
        static bool IsKit(InstrumentKind kind) => kind == InstrumentKind.Drums;

        /// <summary>The player's head in the instrument's frame (at the seat), else a place in front of it. The drums: their
        /// own stool (DrumSeat).</summary>
        Vector3 PlayerLocal(InstrumentKind kind)
        {
            if (IsKit(kind)) return DrumSeat;
            Transform root = _root[(int)kind];
            Transform head = _app.Head;
            if (root != null && head != null && _layout != null && _layout.PlaceOf(kind) == InstrumentPlace.Seat) return root.InverseTransformPoint(head.position);
            return new Vector3(0f, 0.5f, -0.5f);
        }

        Vector3 RightLocal(InstrumentKind kind)
        {
            if (IsKit(kind)) return Vector3.right;
            Transform root = _root[(int)kind];
            Transform head = _app.Head;
            if (root != null && head != null && _layout != null && _layout.PlaceOf(kind) == InstrumentPlace.Seat)
            {
                Vector3 r = root.InverseTransformDirection(head.right);
                r.y = 0f;
                if (r.sqrMagnitude > 1e-6f) return r.normalized;
            }
            return Vector3.right;
        }

        /// <summary>An anchor of kind in its root's frame, read once per playback (key: the cache slot).</summary>
        bool Anchor(InstrumentKind kind, int key, string id, out Vector3 local, out Vector3 up)
        {
            int k = (int)kind;
            local = up = default;
            if (key < 0 || key >= 128) return false;
            if (_anchorHas[k, key] == 0)
            {
                _anchorHas[k, key] = -1;
                Transform root = _root[k];
                if (root != null && TryAnchor(kind, id, out Vector3 w, out Vector3 u))
                {
                    _anchorPoint[k, key] = root.InverseTransformPoint(w);
                    _anchorUp[k, key] = root.InverseTransformVector(u);
                    _anchorHas[k, key] = 1;
                }
            }
            if (_anchorHas[k, key] < 0) return false;
            local = _anchorPoint[k, key];
            up = _anchorUp[k, key];
            return true;
        }

        bool TryAnchor(InstrumentKind kind, string id, out Vector3 world, out Vector3 up)
        {
            world = up = default;
            for (int i = 0; i < _anchors.Count; i++)
            {
                IGuideAnchors a = _anchors[i];
                if (a == null || a.Kind != kind) continue;
                if (a.TryGetAnchor(id, out world, out up)) return true;
            }
            return false;
        }

        // ================================================================== showing

        bool InstrumentShown(int k)
        {
            Transform root = _root[k];
            if (root == null || !root.gameObject.activeInHierarchy) return false;
            if (_layout == null) return true;
            InstrumentPlace place = _layout.PlaceOf((InstrumentKind)k);
            return place == InstrumentPlace.Seat || place == InstrumentPlace.Gliding;
        }

        GhostTrack TrackAt(int i)
        {
            if (i >> 1 == (int)InstrumentKind.Piano) return _fingering != null ? _fingering.Hands[i & 1] : null;
            return _tracks[i];
        }

        void Show(float dt, double now)
        {
            for (int k = 0; k < Kinds; k++) _shown[k] = InstrumentShown(k);
            ShownHands = 0;
            bool any = false;
            Matrix4x4 toSpace = _go != null ? _go.transform.worldToLocalMatrix : Matrix4x4.identity;
            Vector3 bmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), bmax = -bmin;
            for (int i = 0; i < Tracks; i++)
            {
                GhostTrack tr = TrackAt(i);
                int k = i >> 1;
                bool want = tr != null && _shown[k] && tr.Count > 0 && (_playing ? tr.Active(now, Linger, Appear) : true);
                // A drummer keeps both sticks up: while one drum hand plays, the other stays, resting above its last hit.
                if (!want && _playing && IsKit((InstrumentKind)k) && tr != null && tr.Count > 0 && _shown[k])
                    want = _tracks[i ^ 1].Count > 0 && _tracks[i ^ 1].Active(now, Linger, Appear);
                float target = want && _playing ? 1f : 0f;
                float a = _alpha[i];
                a = target > a ? Mathf.Min(target, a + dt / FadeIn) : Mathf.Max(target, a - dt / FadeOut);
                if (tr != null && !_shown[k]) a = 0f;
                _alpha[i] = a;
                if (a <= 0f)
                {
                    Release(i);
                    if (!_playing) tr?.Clear();
                    continue;
                }
                int slot = Slot(i);
                if (slot < 0) continue;
                any = true;
                Pose(i, tr, now, dt, slot, toSpace, ref bmin, ref bmax);
            }
            if (_fingering != null && !_playing && _alpha[0] <= 0f && _alpha[1] <= 0f) _fingering.Clear();
            if (_renderer == null) return;
            _renderer.enabled = any;
            if (!any) return;
            for (int h = 0; h < GhostHandsMesh.MaxHands; h++) if (_owner[h] < 0) _mesh.Collapse(h);
            var b = new Bounds();
            b.SetMinMax(bmin - Vector3.one * 0.6f, bmax + Vector3.one * 0.6f);
            _mesh.Upload(b);
        }

        int Slot(int track)
        {
            if (_slot[track] >= 0) return _slot[track];
            bool left = (track & 1) == 0;
            for (int h = 0; h < GhostHandsMesh.MaxHands; h++)
                if (_owner[h] < 0 && GhostHandsMesh.IsLeft(h) == left) { _owner[h] = track; _slot[track] = h; return h; }
            return -1;
        }

        void Release(int track)
        {
            int h = _slot[track];
            if (h < 0) return;
            _owner[h] = -1;
            _slot[track] = -1;
        }

        static readonly Matrix4x4 Mirror = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));

        void Pose(int i, GhostTrack tr, double now, float dt, int slot, in Matrix4x4 toSpace, ref Vector3 bmin, ref Vector3 bmax)
        {
            int k = i >> 1, side = i & 1;
            Transform root = _root[k];
            Matrix4x4 rootM = root.localToWorldMatrix;
            Matrix4x4 hand;   // hand frame -> instrument local (the mirror included for a left hand)
            bool tool = false, mallet = false;
            Vector3 contactLocal;
            if (k == (int)InstrumentKind.Piano) hand = PianoHand(tr, side, now, out contactLocal);
            else hand = StrikeHand(tr, side, now, out contactLocal, out tool, out mallet);
            Matrix4x4 world = rootM * hand;
            Vector3 palmW = world.MultiplyPoint3x4(Vector3.zero);
            Vector3 contactW = rootM.MultiplyPoint3x4(contactLocal);
            _palmW[i] = palmW;
            _contactW[i] = contactW;

            // the player's own hand right there: the ghost steps back to a faint outline
            float near = float.PositiveInfinity;
            IReadOnlyList<Pointer> hands = _app.Input != null ? _app.Input.HandPointers : null;
            if (hands != null)
                for (int p = 0; p < hands.Count; p++)
                {
                    Pointer pt = hands[p];
                    if (pt == null || !pt.Active) continue;
                    near = Mathf.Min(near, Mathf.Min((pt.Tip - palmW).magnitude, (pt.Tip - contactW).magnitude));
                }
            float overlapTarget = Mathf.Lerp(OverlapAlpha, 1f, Smooth(OverlapNear, OverlapFar, near));
            _overlap[i] = Mathf.MoveTowards(_overlap[i], overlapTarget, OverlapRate * dt);
            float head = 1f;
            if (_app.Head != null) head = Smooth(HeadNear, HeadFar, (_app.Head.position - palmW).magnitude);
            float alpha = _alpha[i] * _overlap[i] * head;
            ShownHands++;
            if (_mesh == null) return;
            byte a8 = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
            _mesh.Shown(slot, a8);
            Matrix4x4 m = toSpace * world;
            _mesh.WritePart(slot, GhostHandsMesh.Palm, m, a8);
            _mesh.WritePart(slot, GhostHandsMesh.Forearm, m * Matrix4x4.TRS(GhostHandModel.WristFrom, Quaternion.Euler(-GhostHandModel.ForearmDroop, 0f, 0f), Vector3.one), a8);
            for (int f = 0; f < GhostHandModel.Fingers; f++)
                for (int s = 0; s < 3; s++)
                    _mesh.WritePart(slot, GhostHandsMesh.FirstSegment + f * 3 + s, m * GhostHandModel.Segment(f, _pose[f], s), a8);
            if (tool)
            {
                Quaternion along = Quaternion.LookRotation(GhostHandModel.ToolDir, Vector3.up);
                float behind = mallet ? GhostHandModel.MalletBehind : GhostHandModel.StickBehind;
                _mesh.WritePart(slot, GhostHandsMesh.ToolShaft, m * Matrix4x4.TRS(GhostHandModel.ToolGrip - GhostHandModel.ToolDir * behind, along, Vector3.one), a8);
                float r = mallet ? GhostHandModel.MalletHead : GhostHandModel.StickTip;
                _mesh.WritePart(slot, GhostHandsMesh.ToolHead, m * Matrix4x4.TRS(GhostHandModel.ToolTip(mallet), Quaternion.identity, Vector3.one * r), a8);
            }
            else
            {
                Vector3 at = m.MultiplyPoint3x4(Vector3.zero);
                _mesh.HidePart(slot, GhostHandsMesh.ToolShaft, at);
                _mesh.HidePart(slot, GhostHandsMesh.ToolHead, at);
            }
            Vector3 ps = toSpace.MultiplyPoint3x4(palmW), cs = toSpace.MultiplyPoint3x4(contactW);
            bmin = Vector3.Min(bmin, Vector3.Min(ps, cs));
            bmax = Vector3.Max(bmax, Vector3.Max(ps, cs));
        }

        static float Smooth(float a, float b, float x) { float u = Mathf.Clamp01((x - a) / (b - a)); return u * u * (3f - 2f * u); }

        /// <summary>A piano hand: the palm where the fingering put it, every finger solved onto its key or its hover point.</summary>
        Matrix4x4 PianoHand(GhostTrack tr, int side, double now, out Vector3 contact)
        {
            tr.Palm(now, out float px, out float pz);
            float press = 0f;
            Vector3 contactSum = Vector3.zero;
            // the fingertips first (they decide the wrist's small dip)
            Span<Vector3> tips = stackalloc Vector3[GhostHandModel.Fingers];
            for (int f = 0; f < GhostHandModel.Fingers; f++)
            {
                var rest = new Vector3(px + PianoFingering.Natural(side, f), PadAbove + Hover, WhiteStrikeZ + pz);
                tips[f] = tr.Finger(f, now, rest, out float pressed) - new Vector3(0f, 0f, FingerFront[f]);
                press = Mathf.Max(press, pressed);
            }
            float sx = side == PianoFingering.Right ? 1f : -1f;
            Vector3 pos = new Vector3(px + sx * 0.009f, PalmY - 0.004f * press, PalmZ0 + pz);
            Quaternion rot = Quaternion.Euler(PalmPitch, -sx * PalmYaw, 0f);
            Matrix4x4 hand = Matrix4x4.TRS(pos, rot, Vector3.one);
            if (side == PianoFingering.Left) hand = hand * Mirror;
            Matrix4x4 inv = hand.inverse;
            for (int f = 0; f < GhostHandModel.Fingers; f++)
            {
                _pose[f] = GhostHandModel.Solve(f, inv.MultiplyPoint3x4(tips[f]));
                Vector3 drawn = hand.MultiplyPoint3x4(GhostHandModel.Tip(f, _pose[f]));
                _tipW[side, f] = _root[(int)InstrumentKind.Piano].TransformPoint(drawn);
                contactSum += drawn;
            }
            contact = contactSum / GhostHandModel.Fingers;
            return hand;
        }

        /// <summary>A strike hand: its contact (fingertip, palm pad, tool tip) on the plan's point, the hand turned to the surface.</summary>
        Matrix4x4 StrikeHand(GhostTrack tr, int side, double now, out Vector3 contact, out bool tool, out bool mallet)
        {
            StrikeFrame s = tr.Strike(now);
            contact = s.Point;
            tool = s.Style == StrokeStyle.Stick || s.Style == StrokeStyle.Mallet;
            mallet = s.Style == StrokeStyle.Mallet;
            float rest = s.Rest;
            Vector3 c;
            float roll;
            Quaternion extra = Quaternion.identity;
            switch (s.Style)
            {
                case StrokeStyle.Stick:
                case StrokeStyle.Mallet:
                    for (int f = 0; f < 5; f++) _pose[f] = GhostHandModel.Curl(f, f == GhostHandModel.Thumb ? 0.55f : f == GhostHandModel.Index ? 0.72f : 0.88f);
                    c = GhostHandModel.ToolTip(mallet);
                    roll = -25f;
                    // the stick lifts mostly by the wrist: the hand turns up about its grip as the tip rises
                    float pitch = Mathf.Clamp(s.Lift / (mallet ? GhostHandModel.MalletAhead : GhostHandModel.StickAhead), 0f, 0.9f) * 0.85f * Mathf.Rad2Deg;
                    extra = Quaternion.Euler(-pitch, 0f, 0f);
                    break;
                case StrokeStyle.Slap:
                    for (int f = 0; f < 5; f++) _pose[f] = GhostHandModel.Curl(f, Mathf.Lerp(0.06f, 0.35f, rest));
                    c = new Vector3(0f, -GhostHandModel.PalmSize.y * 0.5f, 0.02f);
                    roll = 0f;
                    break;
                default:
                {
                    float flick = s.Style == StrokeStyle.Pluck ? 0.45f * s.Hit : 0f;
                    for (int f = 0; f < 5; f++)
                    {
                        float cu = f == GhostHandModel.Index ? 0.22f + flick : f == GhostHandModel.Thumb ? 0.3f : 0.5f + 0.06f * f;
                        _pose[f] = GhostHandModel.Curl(f, Mathf.Lerp(cu, 0.35f, rest * 0.6f));
                    }
                    c = GhostHandModel.Tip(GhostHandModel.Index, GhostHandModel.Curl(GhostHandModel.Index, 0.22f));
                    roll = s.Style == StrokeStyle.Pluck ? 0f : -10f;
                    break;
                }
            }
            Vector3 up = s.Up.sqrMagnitude > 1e-8f ? s.Up : Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(s.Forward, up);
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
            Quaternion rot = Quaternion.LookRotation(fwd.normalized, up) * Quaternion.Euler(0f, 0f, side == 1 ? roll : -roll) * extra;
            // the mirror for a left hand is applied in hand space (x -> -x), so its contact is mirrored too
            Vector3 cm = side == 1 ? c : new Vector3(-c.x, c.y, c.z);
            Vector3 pos = s.Point - rot * cm;
            Matrix4x4 hand = Matrix4x4.TRS(pos, rot, Vector3.one);
            if (side == 0) hand = hand * Mirror;
            return hand;
        }

        // ================================================================== Sunny

        void AskSunny(float dt)
        {
            if (_tipDone) return;
            _shownFor = ShownHands > 0 && _playing ? _shownFor + dt : 0f;
            if (_shownFor < TipAfter || _time < _tipAskAt) return;
            _tipAskAt = _time + TipEvery;
            if (_guide == null) _guide = _app.Get<IGuide>();
            if (_guide == null) return;
            if (PlaybackTips.Ask(_guide, TipId)) _tipDone = true;   // its turn among the show's lines (PlaybackTips)
        }

        public void Destroy()
        {
            _app.Events.Unsubscribe(_onState);
            _mesh?.Dispose();
            if (_material != null) _app.Materials.Destroy(_material);
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }
    }
}
