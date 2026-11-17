// The hand drums at the seat and on display (BUILD-GUIDE 4.2, handdrums.md 3-5): the model under the rig group, the stroke
// logic on the two hands' skin points every frame, the unit through IHandDrumSound, the recorder (with the mute relabel:
// IRecorder.Add(ev, atAudioTime)), the dents, the Sunny signals, the palm lift, the desktop click, the lesson cue's hand
// print, and every platform interface: IInstrument, IStowable, IInstrumentPlayback, IGuideAnchors, ILessonTargets,
// IContactSurface. Rig-local frame: the floor under the two heads' midpoint, +z away from the player, the heads tilted 15
// degrees toward the player (HandDrumsDims). Allocation-free per frame (conformance check 5).
using System;
using System.Collections.Generic;
using BindingFlags = System.Reflection.BindingFlags;
using MethodInfo = System.Reflection.MethodInfo;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.HandDrums
{
    public sealed class HandDrumsInstrument : MonoBehaviour, IInstrument, IStowable, IInstrumentPlayback, IGuideAnchors, ILessonTargets, IContactSurface
    {
        /// <summary>A desktop click plays this u (a medium stroke).</summary>
        public const float ClickU = 0.6f;
        /// <summary>A gap between ticks longer than this (s) forgets the hands (the drums were away, arranged or held).</summary>
        public const double GapResetS = 0.25;

        App _app;
        InstrumentDescriptor _d;
        IAudioEngine _audio;
        IRecorder _recorder;
        readonly List<Renderer> _handles = new List<Renderer>(2);
        readonly List<Renderer> _played = new List<Renderer>(1);
        readonly HdHandSample[] _samples = { new HdHandSample(), new HdHandSample() };
        readonly BoxCollider[] _click = new BoxCollider[2];
        bool _stowed;
        double _lastTick = double.NegativeInfinity;
        Vector3 _lastPos; Quaternion _lastRot;
        bool _headsValid;
        Bounds _local;
        Action<HandSide, float, Vector3> _palmLift;

        // the strokes held back for their mute relabel ([drum * 2 + hand])
        struct Pending { public bool On; public TakeEvent Ev; public double At, DueMs; }
        readonly Pending[] _pending = new Pending[4];

        public Rig Rig { get; private set; }
        public HandDrumsModel Model { get; private set; }
        public HandDrumsInput Input { get; } = new HandDrumsInput();
        public HandDrumsSignals Signals { get; private set; }
        public HandDrumsCue Cue { get; private set; }
        public InstrumentDescriptor Descriptor => _d;

        /// <summary>Tests: live strokes sent to the unit, desktop clicks, playback events, signals raised.</summary>
        public int LiveStrokes { get; private set; }
        public int Clicks { get; private set; }
        public int PlaybackEvents { get; private set; }
        public int SignalsRaised { get; private set; }
        public string LastSignal { get; private set; }
        public int Relabels { get; private set; }

        /// <summary>Tests: a scripted desktop click (button + world ray) instead of the live mouse.</summary>
        public bool ScriptedMouse, ScriptedButton;
        public Ray ScriptedRay;
        bool _scriptedWasDown;

        public IHandDrumSound Sound => _audio != null && _audio.IsReady ? _audio.ActiveUnit(UnitId.HandDrums) as IHandDrumSound : null;

        public static HandDrumsInstrument Create(App app, InstrumentDescriptor d)
        {
            var go = new GameObject(d.Name);
            go.transform.SetParent(app.WorldRoot, false);
            var i = go.AddComponent<HandDrumsInstrument>();
            i.Setup(app, d);
            return i;
        }

        void Setup(App app, InstrumentDescriptor d)
        {
            _app = app;
            _d = d;
            _audio = app.Get<IAudioEngine>();
            transform.localPosition = d.Seat.Home;
            transform.localRotation = Quaternion.Euler(0f, d.Seat.HomeYaw, 0f);
            Model = new HandDrumsModel(transform, app.Materials, textures: !app.Headless);
            foreach (Renderer r in Model.Handles) _handles.Add(r);
            foreach (Renderer r in Model.PlayedParts) _played.Add(r);
            Cue = new HandDrumsCue(transform, app.Materials, textures: !app.Headless);
            Signals = new HandDrumsSignals(RaiseSignal);
            for (int k = 0; k < _pending.Length; k++) _pending[k].On = false;
            BuildClickTargets();
            // the play zone: the seat boxes' bounds (rig-local)
            bool any = false;
            foreach (FootBox b in d.Seat.Boxes)
            {
                if (b.Air) continue;
                var bb = new Bounds(new Vector3(b.Cx, (b.Y0 + b.Y1) * 0.5f - d.Seat.Home.y, b.Cz), new Vector3(b.Hx * 2f, b.Y1 - b.Y0, b.Hz * 2f));
                if (!any) { _local = bb; any = true; } else _local.Encapsulate(bb);
            }
            Rig = new Rig
            {
                Id = d.Rig, Group = transform, Zone = new Zone(this), Priority = d.Seat.Priority,
                HomeAll = d.Seat.Home, HomeYawAll = d.Seat.HomeYaw, HomeSingle = d.Seat.Single, HomeYawSingle = d.Seat.SingleYaw,
                Handles = _handles,
            };
            Rig.Instruments.Add(this);
            app.Register<Rig>(Rig);
            app.Register<IInstrument>(this);
            app.Register<IStowable>(this);
            app.Register<IInstrumentPlayback>(this);
            app.Register<IGuideAnchors>(this);
            app.Register<ILessonTargets>(this);
            app.Register<IContactSurface>(this);
            _palmLift = FindPalmLift();
        }

        /// <summary>MetaHandVisual.SetPalmLift (contract request R1) when the Hands module has it, else null: the drawn hand
        /// then sinks into the head on hard palm strokes (E1's finger clamp still stops the fingertips on it).</summary>
        static Action<HandSide, float, Vector3> FindPalmLift()
        {
            MethodInfo m = typeof(MetaHandVisual).GetMethod("SetPalmLift", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(HandSide), typeof(float), typeof(Vector3) }, null);
            return m != null ? (Action<HandSide, float, Vector3>)Delegate.CreateDelegate(typeof(Action<HandSide, float, Vector3>), m) : null;
        }

        /// <summary>Whether the palm lift reaches the drawn hands (R1 applied).</summary>
        public bool PalmLiftWired => _palmLift != null;

        void BuildClickTargets()
        {
            for (int d = 0; d < 2; d++)
            {
                var go = new GameObject(d == 0 ? "Conga click target" : "Tumba click target") { layer = Layers.PlaySurface };
                go.transform.SetParent(transform, false);
                go.transform.localPosition = HandDrumsDims.HeadCentre[d];
                go.transform.localRotation = HandDrumsDims.HeadRotation;
                var c = go.AddComponent<BoxCollider>();
                float a = HandDrumsDims.A[d];
                c.center = new Vector3(0f, -0.01f, 0f);
                c.size = new Vector3(a * 2f, 0.02f, a * 2f);
                _click[d] = c;
            }
        }

        // ---------------------------------------------------------------- IInstrument

        public InstrumentKind Kind => InstrumentKind.HandDrums;
        public int TickOrder => _d.TickOrder;
        public bool AcceptsStickPointers => false;
        public bool IsPresent => isActiveAndEnabled && Rig != null && Rig.InScene && !_stowed;
        public Transform Root => transform;
        public IReadOnlyList<Renderer> PlayedParts => _played;
        public bool Stowed => _stowed;

        public void Tick(IReadOnlyList<Pointer> pointers, float dt)
        {
            if (_stowed) return;
            double now = _app.Clock.Now, nowMs = _app.Clock.NowMs;
            if (now - _lastTick > GapResetS && _lastTick > double.NegativeInfinity) ForgetHands();
            _lastTick = now;
            UpdateHeads();
            IHandDrumSound sound = Sound;
            PointerInput input = _app.Input;
            for (int h = 0; h < 2; h++)
            {
                HandState hs = input.Hands[h];
                HandFrame f = input.Source != null ? input.Frame(hs) : null;
                bool use = hs.Connected && !hs.IsMouse && hs.Holding == null && f != null && !f.systemGesture;
                if (use) _samples[h].FromFrame(f); else _samples[h].Tracked = false;
            }
            Input.Step(now, _samples[0], _samples[1], dt);
            double firstStar = double.NaN;
            for (int i = 0; i < Input.Count; i++)
            {
                ref HdEvent e = ref Input.Events[i];
                switch (e.Op)
                {
                    case HdOp.Stroke:
                    {
                        if (double.IsNaN(firstStar)) firstStar = e.TStar;
                        double delay = Math.Max(0, (e.TStar - firstStar) / 1000.0);
                        HandDrumsInput.Send(sound, e, delay > 0 ? -delay : 0);
                        LiveStrokes++;
                        Dent(e, now + delay);
                        RecordStroke(e, _app.Clock.AudioNow + delay, nowMs);
                        PlayStamp.Mark(input.Hands[e.Hand & 1], InstrumentKind.HandDrums, nowMs);
                        break;
                    }
                    case HdOp.Rest:
                    case HdOp.Lift:
                        HandDrumsInput.SendRest(sound, e, 0);
                        if (e.Op == HdOp.Rest)
                        {
                            Model.Rest(e.Drum, e.Hand, new RestPoint { Part = e.RestPart0, Rho = (float)e.RestRho0, Theta = (float)e.RestTheta0 },
                                new RestPoint { Part = e.RestPart1, Rho = (float)e.RestRho1, Theta = (float)e.RestTheta1 }, e.RestCount, now);
                            PlayStamp.Mark(input.Hands[e.Hand & 1], InstrumentKind.HandDrums, nowMs);
                        }
                        else Model.Lift(e.Drum, e.Hand);
                        Recorder?.Add(HandDrumsTake.FromRest(e));
                        break;
                    case HdOp.Relabel:
                        Relabel(e, nowMs);
                        break;
                }
                Signals.Feed(e);
            }
            Input.Clear();
            FlushDue(nowMs);
            if (sound != null) Signals.Tick(sound.Level(0), sound.Level(1), dt);
            Model.Update(now);
            if (_palmLift != null && _app.IsPresenting && !CutoutHands())
            {
                Vector3 n = transform.TransformDirection(HandDrumsDims.Normal);
                _palmLift(HandSide.Left, (float)Input.PalmLift[0], n);
                _palmLift(HandSide.Right, (float)Input.PalmLift[1], n);
            }
            DesktopClick(now, nowMs);
        }

        /// <summary>The real hands show through the passthrough cut-out ("Play in my room" with "Real hands"): the drawn palm
        /// may not ride on the skin then, since the hole must match the real hand. The drawn hands lift in both rooms.</summary>
        bool CutoutHands()
        {
            IHandLook look = _app.Get<IHandLook>();
            return look != null && look.CutoutActive;
        }

        IRecorder Recorder => _recorder ??= _app.Get<IRecorder>();   // Lessons (Order 40) registers it after this module

        /// <summary>The heads' frames follow the rig (it glides, the arrange mode lifts it).</summary>
        void UpdateHeads()
        {
            Transform t = transform;
            if (_headsValid && t.position == _lastPos && t.rotation == _lastRot) return;
            _lastPos = t.position; _lastRot = t.rotation; _headsValid = true;
            for (int d = 0; d < 2; d++) Input.Heads[d] = HdHead.FromRig(t, HandDrumsDims.HeadCentre[d], HandDrumsDims.A[d]);
        }

        void ForgetHands()
        {
            Input.ResetHands();
            IHandDrumSound sound = Sound;
            if (sound != null) for (int d = 0; d < 2; d++) for (int h = 0; h < 2; h++) sound.Lift(d, h, 0);
            Model.LiftAll();
            FlushAll();
        }

        void Dent(in HdEvent e, double now)
        {
            for (int i = 0; i < e.PatchCount; i++)
            {
                HdPatch p = e.Patch(i);
                double u = Math.Min(1, e.U * p.UScale);
                var q0 = new PatchPoint { Rho = (float)p.R0, Theta = (float)p.T0 };
                if (p.Shape == PatchShape.Line)
                    Model.Strike(e.Drum, p.Part, u, PatchShape.Line, q0, new PatchPoint { Rho = (float)p.R3, Theta = (float)p.T3 }, default, 2, now + p.Delay);
                else
                    Model.Strike(e.Drum, p.Part, u, PatchShape.Points, q0, new PatchPoint { Rho = (float)p.R1, Theta = (float)p.T1 }, new PatchPoint { Rho = (float)p.R2, Theta = (float)p.T2 }, p.Count, now + p.Delay);
            }
        }

        // ---------------------------------------------------------------- recording (review item 9.7: the mute relabel)

        void RecordStroke(in HdEvent e, double atAudio, double nowMs)
        {
            TakeEvent ev = HandDrumsTake.FromStroke(e);
            int k = (e.Drum & 1) * 2 + (e.Hand & 1);
            Flush(k);   // a stroke of the same hand on the same drum ends the previous one's wait
            if (HandDrumsTake.MayRelabel(e.Kind))
            {
                _pending[k] = new Pending { On = true, Ev = ev, At = atAudio, DueMs = nowMs + HandDrumsTake.RelabelHoldMs };
                return;
            }
            Recorder?.Add(ev, atAudio);
        }

        void Relabel(in HdEvent e, double nowMs)
        {
            int k = (e.Drum & 1) * 2 + (e.Hand & 1);
            if (!_pending[k].On) return;          // too late: the stroke went into the take as it was played
            HandDrumsTake.Relabel(ref _pending[k].Ev, e.Label);
            Relabels++;
            Flush(k);
        }

        void Flush(int k)
        {
            if (!_pending[k].On) return;
            _pending[k].On = false;
            Recorder?.Add(_pending[k].Ev, _pending[k].At);
        }

        void FlushDue(double nowMs) { for (int k = 0; k < _pending.Length; k++) if (_pending[k].On && nowMs >= _pending[k].DueMs) Flush(k); }
        void FlushAll() { for (int k = 0; k < _pending.Length; k++) Flush(k); }

        void RaiseSignal(string s)
        {
            SignalsRaised++;
            LastSignal = s;
            _app.Events.Publish(new InstrumentSignalEvent { Kind = InstrumentKind.HandDrums, Signal = s });
        }

        // ---------------------------------------------------------------- the desktop click (conformance 16: the mouse hand never plays)

        void DesktopClick(double now, double nowMs)
        {
            if (_app.IsPresenting) { _scriptedWasDown = ScriptedMouse && ScriptedButton; return; }
            bool pressed;
            Ray ray;
            if (ScriptedMouse)
            {
                pressed = ScriptedButton && !_scriptedWasDown;
                _scriptedWasDown = ScriptedButton;
                ray = ScriptedRay;
            }
            else
            {
                pressed = DesktopInput.MouseDown(0) && !DesktopInput.Shift && !DesktopInput.Alt;
                if (!pressed) return;
                Camera cam = _app.Rig != null ? _app.Rig.HeadCamera : null;
                if (cam == null) return;
                ray = cam.ScreenPointToRay(DesktopInput.MousePosition);
            }
            if (!pressed) return;
            if (!Physics.Raycast(ray, out RaycastHit hit, 20f, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide)) return;
            int d = hit.collider == _click[0] ? 0 : hit.collider == _click[1] ? 1 : -1;
            if (d < 0) return;   // the nearest play surface belongs to another module
            InHead(d, hit.point, out float rho, out float theta, out _);
            if (rho > 1.02f) return;
            HandStroke kind = rho <= (float)HdK.BassRho ? HandStroke.Bass : HandStroke.Open;
            string zone = kind == HandStroke.Bass ? "bass" : "open";
            HandDrumsTake.Canonical(zone, out _, out double rho0, out double th0);
            Sound?.Stroke(d, 1, kind, ClickU, theta - th0, 0, (float)Math.Max(-0.3, Math.Min(0.3, rho - rho0)));
            Model.Strike(d, kind == HandStroke.Bass ? HandPart.Palm : HandPart.Fingers, ClickU, PatchShape.Points, new PatchPoint { Rho = rho, Theta = theta }, default, default, 1, now);
            Clicks++;
            Recorder?.Add(new TakeEvent { type = HandDrumsTake.TypeStroke, kind = HandDrumsTake.Drums[d], zone = zone, pos = rho, vel = ClickU, i = 1, u = theta / (2 * Mathf.PI) + 0.5f });
        }

        /// <summary>A world point in drum d's head frame: rho (0 centre .. 1 the bearing edge), theta (0 toward the player),
        /// h above the head plane (m).</summary>
        public void InHead(int d, Vector3 world, out float rho, out float theta, out float h)
        {
            Vector3 l = transform.InverseTransformPoint(world) - HandDrumsDims.HeadCentre[d];
            Quaternion q = HandDrumsDims.HeadRotation;
            Vector3 n = q * Vector3.up, e0 = q * Vector3.back, e1 = Vector3.Cross(n, e0);
            h = Vector3.Dot(l, n);
            float x = Vector3.Dot(l, e0), y = Vector3.Dot(l, e1);
            rho = Mathf.Sqrt(x * x + y * y) / HandDrumsDims.A[d];
            theta = Mathf.Atan2(y, x);
        }

        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow) { }   // no glows (platform 3.3)

        public PlaySurface PlaySurfaceAt(Vector3 world)
        {
            if (!IsPresent) return PlaySurface.None;
            for (int d = 0; d < 2; d++)
            {
                InHead(d, world, out float rho, out _, out float h);
                if (rho <= 1.05f && h >= -0.05f && h <= 0.08f) return PlaySurface.Instrument;
            }
            return PlaySurface.None;
        }

        // ---------------------------------------------------------------- IContactSurface (E1 finger clamp, E3 shadows)

        public bool QueryContact(Vector3 world, float above, float below, out ContactHit hit)
        {
            hit = default;
            if (!IsPresent || Rig.Held) return false;
            bool found = false;
            for (int d = 0; d < 2; d++)
            {
                InHead(d, world, out float rho, out _, out float h);
                if (rho > 1f || h > above || h < -below) continue;
                if (found && h >= hit.Height) continue;
                Vector3 n = transform.TransformDirection(HandDrumsDims.Normal);
                hit = new ContactHit { Point = world - n * h, Normal = n, Height = h, Use = ContactUse.Both };
                found = true;
            }
            return found;
        }

        // ---------------------------------------------------------------- IStowable

        public void SetStowed(bool stowed, StowMount mount)
        {
            if (stowed && !_stowed)
            {
                ForgetHands();
                Cue.HideAll();
            }
            _stowed = stowed;
            Model.SetDisplay(stowed);
            for (int d = 0; d < 2; d++) if (_click[d] != null) _click[d].enabled = !stowed;
            _lastTick = double.NegativeInfinity;
        }

        // ---------------------------------------------------------------- IInstrumentPlayback

        public bool Play(in TakeEvent e)
        {
            if (e.type != HandDrumsTake.TypeStroke && e.type != HandDrumsTake.TypeMute) return false;
            if (!HandDrumsTake.Play(e, Sound)) return true;   // a type of ours with nothing to play (no unit, an unknown drum or zone)
            PlaybackEvents++;
            if (_stowed) return true;
            int d = Array.IndexOf(HandDrumsTake.Drums, e.kind);
            double now = _app.Clock.Now;
            float theta = (e.u - 0.5f) * 2f * Mathf.PI;
            if (e.type == HandDrumsTake.TypeStroke)
            {
                HandPart part = e.zone == "bass" ? HandPart.Palm : e.zone == "heel" ? HandPart.Heel : e.zone == "slap" || e.zone == "closed" ? HandPart.Tips : HandPart.Fingers;
                Model.Strike(d, part, Mathf.Clamp01(e.vel), PatchShape.Points, new PatchPoint { Rho = Mathf.Clamp(e.pos, 0f, 1f), Theta = theta }, default, default, 1, now);
            }
            else if (e.open) Model.Lift(d, e.i & 1);
            else Model.Rest(d, e.i & 1, new RestPoint { Part = HandPart.Fingers, Rho = Mathf.Clamp(e.pos, 0f, 1f), Theta = theta }, default, 1, now);
            return true;
        }

        public void ReleaseAll()
        {
            HandDrumsTake.ReleaseAll(Sound);
            Model.LiftAll();
        }

        public void CloseLane(int lane) { }   // strokes ring out (not Held); a rest is lifted by its own recorded lift

        // ---------------------------------------------------------------- IGuideAnchors

        public bool TryGetAnchor(string anchor, out Vector3 world, out Vector3 up) => HandDrumsGuide.TryGetAnchor(transform, anchor, out world, out up);

        // ---------------------------------------------------------------- ILessonTargets

        public bool Validate(in LessonTarget t, out string why) => HandDrumsLessons.Validate(t, out why);

        public bool TryGetLanding(in LessonTarget t, out Pose landing, out Vector3 approach, out Vector2 size) => HandDrumsLessons.TryGetLanding(t, transform, out landing, out approach, out size);

        public bool Matches(in LessonTarget t, in TakeEvent e) => HandDrumsLessons.Matches(t, e);

        public void ShowTarget(in LessonTarget t, float k) => Cue.Show(t, k);

        public string Hint(IReadOnlyList<LessonTarget> targets) => HandDrumsLessons.Hint(targets);

        // ---------------------------------------------------------------- teardown

        void OnDestroy() => Model?.Dispose();

        /// <summary>The rig zone: the footprint's local bounds grown by GrabConstants.ZonePad.</summary>
        sealed class Zone : IPlayZone
        {
            readonly HandDrumsInstrument _i;
            public Zone(HandDrumsInstrument i) { _i = i; }
            public bool Contains(Vector3 world)
            {
                if (_i == null || !_i.isActiveAndEnabled) return false;
                Bounds b = _i._local;
                b.Expand(GrabConstants.ZonePad * 2f);
                return b.Contains(_i.transform.InverseTransformPoint(world));
            }
        }
    }
}
