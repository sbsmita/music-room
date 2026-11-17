// The kit (Docs/05-instruments.md sections 3 and 7): nine pieces whose strike surface is their local y = 0
// plane, the per-piece x pointer hit test (arm / disarm, crossing point, rim, velocity curve), Hit with its 3 ms and
// 45 ms windows, the hand-on-cymbal choke, the hi-hat stand pinch, the kit's motion (E7: cymbals rock where struck, heads
// give under the stick, the kick beater strikes on every kick), hover glows, play surfaces, the desktop click, recorder
// events, and the IDrumsApi surface the lessons, the recorder playback and Sunny use.
// The kit does no work in Unity callbacks: Core ticks it in the Instruments phase (skipped while its rig is held or
// MR is placing) and the hot paths are allocation-free.
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Drums
{
    /// <summary>What the kit needs from its surroundings (the app, or a test fixture).</summary>
    public interface IDrumHost
    {
        /// <summary>Frame-constant milliseconds (Clock.NowMs): every "3 ms since the last hit" in a frame means the same.</summary>
        double NowMs { get; }
        IAudioEngine Audio { get; }
        /// <summary>The recorder (Lessons, a later module): resolved lazily, may be null.</summary>
        IRecorder Recorder { get; }
        /// <summary>Fingertip pointers (for EveryHandPointerFartherThan); may be null.</summary>
        IReadOnlyList<Pointer> HandPointers { get; }
        /// <summary>The desktop path is live: the app is not presenting (App.IsPresenting false).</summary>
        bool DesktopClicks { get; }
        Camera DesktopCamera { get; }
    }

    public sealed partial class DrumKit : MonoBehaviour, IInstrument, IDrumsApi
    {
        IDrumHost _host;
        DrumKitParts _parts;
        DrumPiece[] _pieces;
        DrumPiece _hat;
        DrumSticks _sticks;
        readonly List<DrumPieceInfo> _info = new List<DrumPieceInfo>(DrumSpec.PieceCount);
        readonly List<Renderer> _played = new List<Renderer>(24);
        RigZone _zone;

        public Rig Rig { get; private set; }
        public DrumMaterials Materials { get; private set; }
        public DrumKitParts Parts => _parts;
        public DrumSticks Sticks => _sticks;
        public IReadOnlyList<DrumPiece> PieceStates => _pieces;
        public DrumPiece HatPiece => _hat;
        /// <summary>E7: the kick pedal's decorative beater (null without a build).</summary>
        public KickBeater Beater => _parts?.Beater;

        // Diagnostics (tests, device logs).
        public int HitCount { get; private set; }
        public int ChokeCount { get; private set; }
        public DrumPiece LastPiece { get; private set; }
        public float LastVelocity { get; private set; }
        public DrumStrike LastStrike { get; private set; }

        /// <summary>Builds the geometry and the rig. <paramref name="lib"/> null = geometry only (EditMode tests).</summary>
        public void Build(IDrumHost host, MaterialLibrary lib)
        {
            _host = host;
            Materials = DrumMaterials.Create(lib);
            _parts = DrumKitBuilder.Build(transform, Materials);
            _pieces = _parts.Pieces;
            _hat = _pieces[DrumSpec.HatId];
            _info.Clear();
            for (int i = 0; i < _pieces.Length; i++) _info.Add(_pieces[i].Info);
            _played.Clear();
            _played.AddRange(_parts.Played);
            _zone = new RigZone(this);
            Rig = new Rig
            {
                Id = RigId.Drums, Group = transform, Zone = _zone, Priority = DrumSpec.RigPriority,
                HomeAll = DrumSpec.HomeAll, HomeYawAll = DrumSpec.HomeYawAll,
                HomeSingle = DrumSpec.HomeSingle, HomeYawSingle = DrumSpec.HomeYawSingle,
                // Filled once (ARCHITECTURE.md section 8): the per-part renderers of the shells, resonant heads, chrome,
                // stands, pad body and rim, spurs and pedal. They draw nothing (forceRenderingOff: the kit-frame static
                // meshes draw them) but stay enabled with live bounds, one OBB per part; Layout checks visibility itself.
                Handles = _parts.Handles,
            };
            Rig.Instruments.Add(this);
        }

        /// <summary>The sticks rest on the snare and are played parts of the kit.</summary>
        public void AttachSticks(DrumSticks sticks)
        {
            _sticks = sticks;
            if (sticks != null) _played.AddRange(sticks.Renderers);
        }

        /// <summary>Frees the generated meshes (the materials belong to the MaterialLibrary).</summary>
        public void DestroyGeneratedAssets(bool immediate)
        {
            if (_parts != null)
            {
                foreach (var m in _parts.Meshes) if (m != null) { if (immediate) DestroyImmediate(m); else Destroy(m); }
                _parts.Meshes.Clear();
            }
            _sticks?.DestroyGeneratedAssets(immediate);
        }

        void OnDestroy()
        {
            if (Application.isPlaying) DestroyGeneratedAssets(false);
        }

        double Now => _host != null ? _host.NowMs : 0;

        // =============================== IInstrument ===============================
        public InstrumentKind Kind => InstrumentKind.Drums;
        public int TickOrder => 10;
        public bool AcceptsStickPointers => true;
        public bool IsPresent => this != null && gameObject.activeInHierarchy && (Rig == null || Rig.InScene);
        public Transform Root => transform;
        public IReadOnlyList<Renderer> PlayedParts => _played;

        /// <summary>The per-frame update.</summary>
        public void Tick(IReadOnlyList<Pointer> pointers, float dt)
        {
            if (_pieces == null) return;
            double now = Now;
            int count = pointers != null ? pointers.Count : 0;

            // Pinching the hi-hat stand (the pole under the cymbals) opens or closes it.
            if (_hat != null)
            {
                Vector3 hp = _hat.Group.localPosition;
                for (int i = 0; i < count; i++)
                {
                    Pointer p = pointers[i];
                    if (!p.Active || p.External || p.Stick || p.Joint != (int)HandJoint.IndexTip) continue;
                    var h = p.Owner as HandState;
                    // The desktop mouse hand is a stand-in: it never plays (ARCHITECTURE.md section 0).
                    // The grab pinch (the thumb-index rule OR Meta's own index pinch: the rule alone flickers on the headset).
                    if (h == null || h.IsMouse || !h.GrabPinchStart || h.Holding != null) continue;
                    if (DrumMath.InHatStandZone(transform.InverseTransformPoint(p.Tip), hp)) SetHatOpen(!_hat.Open, h);
                }
                if (_hat.Lower != null)
                {
                    float target = _hat.Open ? DrumSpec.HatOpenY : DrumSpec.HatClosedY;
                    if (_hat.LowerY != target)
                    {
                        _hat.LowerY += (target - _hat.LowerY) * Mathf.Min(1f, dt * DrumSpec.HatEaseRate);
                        if (Mathf.Abs(target - _hat.LowerY) < 1e-6f) _hat.LowerY = target;
                        Vector3 lp = _hat.Lower.localPosition;
                        lp.y = _hat.LowerY;
                        _hat.Lower.localPosition = lp;
                    }
                }
            }

            // E7: the heads' dents and the beater age first, then this frame's hits strike them: a hit from any phase of
            // the frame (the hit test below, the nod, playback, a desktop click) draws its t = 0 pose on exactly one frame.
            for (int pi = 0; pi < _pieces.Length; pi++) if (!_pieces[pi].IsCymbal) _pieces[pi].Give?.Step(dt);
            _parts.Beater?.Step(dt);

            for (int pi = 0; pi < _pieces.Length; pi++)
            {
                DrumPiece piece = _pieces[pi];
                Matrix4x4 w2l = piece.Group.worldToLocalMatrix;
                float r = piece.R;

                // A hand resting flat on a ringing cymbal chokes it: fingertips still within a few cm of the plate, not
                // a stroke passing through it.
                if (piece.IsCymbal && !piece.Choked && now - piece.LastHitMs < DrumSpec.ChokeWithinMs && now - piece.LastHitMs > DrumSpec.ChokeAfterMs)
                {
                    for (int i = 0; i < count; i++)
                    {
                        Pointer p = pointers[i];
                        if (!p.Active || !p.WasActive || p.Stick || p.FromMouse || p.Speed > DrumSpec.ChokeMaxSpeed) continue;
                        Vector3 t = w2l.MultiplyPoint3x4(p.Tip);
                        if (Mathf.Abs(t.y) > DrumSpec.ChokeBand || Mathf.Sqrt(t.x * t.x + t.z * t.z) > r) continue;
                        Vector3 pr = w2l.MultiplyPoint3x4(p.Prev);
                        if (Mathf.Abs(pr.y) > DrumSpec.ChokeBand || Mathf.Abs(t.y - pr.y) > DrumSpec.ChokeStill) continue;
                        Choke(piece, p);
                        break;
                    }
                }

                float reach = DrumMath.StrikeReach(piece.Type, piece.Pad, r);
                for (int i = 0; i < count; i++)
                {
                    Pointer p = pointers[i];
                    // Mouse-hand fingertips never strike: the click path below plays the desktop (no double fire).
                    if (!p.Active || !p.WasActive || p.FromMouse) continue;
                    // A stick's shoulder strikes cymbals only.
                    if (p.Shoulder && !piece.IsCymbal) continue;
                    Vector3 t = w2l.MultiplyPoint3x4(p.Tip);
                    Vector3 pr = w2l.MultiplyPoint3x4(p.Prev);
                    // Re-arm only once clearly above the head, so jitter while parked just below cannot retrigger.
                    if (t.y > DrumSpec.ArmAbove || pr.y > DrumSpec.ArmAbove) p.DrumArm[pi] = 1;
                    // Fingertips start a stroke from close above the head; only sticks come from higher up.
                    if (!(pr.y > 0f && t.y <= 0f) || pr.y > (p.Stick ? DrumSpec.StickMaxPrev : DrumSpec.FingerMaxPrev)) continue;
                    if (p.DrumArm[pi] == 0) continue;
                    // Where the path crossed the head, not where it ended, so fast strokes count.
                    float rr = DrumMath.CrossingRadius(pr, t);
                    if (rr > reach) continue;
                    // Any pass through disarms, sounding or not: a gently placed finger must rise before it strikes again.
                    p.DrumArm[pi] = 0;
                    float vy = (pr.y - t.y) / DrumMath.SampleSeconds(p.SampleDt, dt);
                    float vel = DrumMath.StrikeVelocity(vy, p.Stick);
                    if (vel < 0f) continue;
                    DrumStrike strike = StrikeAt(piece, rr);
                    strike.Implement = DrumMath.ImplementOf(p.Stick, p.Shoulder);
                    // E2: a stick that struck springs back (drawn only) from the approach speed this test measured.
                    // E7: the crossing point dents the head / rocks the cymbal where it was struck.
                    if (Hit(piece, vel, p, false, strike, DrumMath.CrossingPoint(pr, t)) && p.Owner is Drumstick ds) ds.HitSpeed = Mathf.Max(ds.HitSpeed, vy);
                }

                // E7. Transforms and vertices are written only while something moves: an idle kit dirties nothing on the
                // headset. The drums no longer scale (FlashScale 0): the flash is a diagnostic.
                piece.Flash = Mathf.Max(0f, piece.Flash - dt * DrumSpec.FlashDecay);
                if (piece.IsCymbal) StepCymbal(piece, dt);   // a strike only adds speed: the hit frame already rocks
            }

            if (_host != null && _host.DesktopClicks && DesktopInput.MouseDown(0) && !DesktopInput.Shift && !DesktopInput.Alt) DesktopClick();
        }

        /// <summary>Hover: a .09 m disc on the head or plate under each fingertip (or stick) hovering
        /// up to 10 cm above it, pulled 3 cm inside the edge; shoulders only over cymbals.</summary>
        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow)
        {
            if (_pieces == null || glow == null || pointers == null) return;
            int count = pointers.Count;
            for (int pi = 0; pi < _pieces.Length; pi++)
            {
                DrumPiece piece = _pieces[pi];
                Matrix4x4 w2l = piece.Group.worldToLocalMatrix;
                float r = piece.R;
                for (int i = 0; i < count; i++)
                {
                    Pointer p = pointers[i];
                    if (!p.Active || (p.Shoulder && !piece.IsCymbal)) continue;
                    Vector3 t = w2l.MultiplyPoint3x4(p.Tip);
                    if (t.y < -DrumSpec.HoverBelow || t.y > DrumSpec.HoverAbove) continue;
                    float rr = Mathf.Sqrt(t.x * t.x + t.z * t.z);
                    if (rr > r + DrumSpec.HoverPad) continue;
                    float k = rr > r - DrumSpec.HoverInset ? (r - DrumSpec.HoverInset) / rr : 1f;
                    Vector3 pos = piece.Group.TransformPoint(new Vector3(t.x * k, DrumSpec.HoverLift, t.z * k));
                    glow.Add(HoverGlowSpec.At(pos, piece.Group.up, DrumSpec.HoverSize, DrumSpec.HoverSize, 1f - t.y / DrumSpec.HoverAbove));
                }
            }
        }

        public PlaySurface PlaySurfaceAt(Vector3 world)
        {
            if (_pieces == null || !IsPresent) return PlaySurface.None;
            return OverDrums(world);
        }

        /// <summary>Play surface test: the first piece with local -.12 &lt;= y &lt;= .25 and within r + .04, else the
        /// hi-hat stand's pinch spot.</summary>
        public PlaySurface OverDrums(Vector3 world)
        {
            for (int i = 0; i < _pieces.Length; i++)
                if (DrumMath.OverPiece(_pieces[i].Group.InverseTransformPoint(world), _pieces[i].R)) return PlaySurface.Drums;
            if (_hat != null && DrumMath.InHatStandZone(transform.InverseTransformPoint(world), _hat.Group.localPosition)) return PlaySurface.HatStand;
            return PlaySurface.None;
        }

        // =============================== hits ===============================

        public DrumStrike StrikeAt(DrumPiece piece, float rr) =>
            DrumMath.StrikeAt(piece.Type, piece.IsHat, piece.Pad, piece.R, rr, piece.Open);

        /// <summary>E7: the plate rocks (CymbalMotion) and is written only while it moves; a hand on it (Choked) damps it.</summary>
        static void StepCymbal(DrumPiece piece, float dt)
        {
            if (!piece.Tilt.Moving) return;
            CymbalMotion.Step(ref piece.Tilt, CymbalMotion.For(piece), dt, piece.Choked);
            if (piece.Top != null) piece.Top.localRotation = piece.Tilt.Moving ? piece.Tilt.Rotation : Quaternion.identity;
        }

        /// <summary>Hit without a known strike point (playback, the nod, tests): the motion takes the
        /// point at the strike's radius on the player's side of the piece. Returns false when a window swallowed it.</summary>
        public bool Hit(DrumPiece piece, float vel, Pointer p, bool force, DrumStrike strike) => Hit(piece, vel, p, force, strike, null);

        /// <summary>Hit. <paramref name="at"/>: where it was struck, piece-local (x, z) (the crossing point
        /// of the hit test, a desktop click's hit point), for the kit's motion (E7). Returns false when a window swallowed it.</summary>
        public bool Hit(DrumPiece piece, float vel, Pointer p, bool force, DrumStrike strike, Vector2? at)
        {
            double now = Now;
            // A tiny per-piece window collapses duplicates of the same frame; the 45 ms window is per hand (every
            // fingertip of that hand is stamped) or per stick (tip and shoulder), so one hand's other fingers cannot
            // retrigger while the other hand or stick can still play a flam.
            if (!force && now - piece.LastHitMs < DrumSpec.SameFrameMs) return false;
            int id = piece.Id;
            if (p != null)
            {
                if (now - p.DrumLastHit[id] < DrumSpec.OwnerWindowMs) return false;
                IReadOnlyList<Pointer> group = p.Owner != null ? p.Owner.Pointers : null;
                if (group != null) for (int i = 0; i < group.Count; i++) group[i].DrumLastHit[id] = now;
                p.DrumLastHit[id] = now;
            }
            piece.LastHitMs = now;
            piece.Choked = false;
            // A hand that is drumming is not grabbing: its pinches are ignored for a moment (LastPlayMs).
            if (p != null && p.Owner != null) p.Owner.LastPlayMs = now;
            DrumKind kind = piece.IsHat && strike.Open ? DrumKind.HatOpen : piece.Spec.Audio;
            _host?.Audio?.Drum(kind, vel, strike);
            _host?.Recorder?.Add(TakeEvent.Drum(piece.Kind, vel, strike.Pos, DrumStrike.ZoneName(strike.Zone), strike.Open));
            piece.Flash = 1f;
            Move(piece, vel, strike, at);
            piece.Hits++;
            HitCount++;
            LastPiece = piece;
            LastVelocity = vel;
            LastStrike = strike;
            return true;
        }

        /// <summary>E7: what a hit moves. A cymbal tips down where it was struck; a batter head dents there; every kick
        /// (the shell, the pad, the nod, playback) swings the beater onto the kick's head, which gives where the ball lands
        /// (unless the kick's own head was struck by hand or stick: then there).</summary>
        void Move(DrumPiece piece, float vel, in DrumStrike strike, Vector2? at)
        {
            Vector2 c = at ?? new Vector2(0f, -strike.Pos * piece.R);   // unknown point: on the player's side (-z)
            if (piece.IsCymbal) CymbalMotion.Strike(ref piece.Tilt, CymbalMotion.For(piece), c.x, c.y, vel);
            else if (!piece.Spec.Pad && piece.Id != DrumSpec.KickId) piece.Give?.Strike(c.x, c.y, vel);
            if (piece.Spec.Audio != DrumKind.Kick || _parts == null) return;
            _parts.Beater?.Strike();
            DrumPiece kick = _pieces[DrumSpec.KickId];
            if (kick?.Give == null) return;
            if (piece == kick && at.HasValue) kick.Give.Strike(c.x, c.y, vel);
            else
            {
                Vector3 l = Quaternion.Inverse(kick.Spec.LocalRotation) * (KickBeater.ContactPoint - kick.Spec.Pos);
                kick.Give.Strike(l.x, l.z, vel);
            }
        }

        /// <summary>Choke: a hand laid on a ringing cymbal stops it.</summary>
        void Choke(DrumPiece piece, Pointer p)
        {
            piece.Choked = true;
            _host?.Audio?.Choke(piece.Spec.Audio);
            if (p != null && p.Owner != null) p.Owner.LastPlayMs = Now;
            _host?.Recorder?.Add(TakeEvent.DrumChoke(piece.Kind));
            ChokeCount++;
        }

        /// <summary>The desktop click under the mouse.</summary>
        void DesktopClick()
        {
            Camera cam = _host.DesktopCamera;
            if (cam == null) return;
            DesktopHit(cam.ScreenPointToRay(DesktopInput.MousePosition));
        }

        /// <summary>Desktop click under one desktop model (ARCHITECTURE.md section 0): the nearest collider on
        /// Layers.PlaySurface along the ray, of any module, takes the click; the kit acts only when it is one of its own
        /// click discs, and plays that piece at .75 with the strike zone of the hit point. A drum head answers from above
        /// only (its click disc is one-sided), a cymbal from either side. Returns the piece played, or null.</summary>
        public DrumPiece DesktopHit(Ray ray)
        {
            if (_pieces == null || _parts == null) return null;
            // A click is rare (one per mouse press): syncing first means a kit that the layout moved this frame is hit
            // where it is drawn.
            Physics.SyncTransforms();
            if (!Physics.Raycast(ray, out RaycastHit hit, DrumSpec.DesktopRayLength, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide)) return null;
            DrumPiece piece = PieceOf(hit.collider);
            if (piece == null) return null;   // another module's play surface is nearer: its click
            if (!piece.IsCymbal && piece.Group.InverseTransformDirection(ray.direction).y > 0f) return null;
            Vector3 l = piece.Group.InverseTransformPoint(hit.point);
            float rr = Mathf.Min(piece.R, Mathf.Sqrt(l.x * l.x + l.z * l.z));
            Hit(piece, DrumSpec.DesktopVelocity, null, false, StrikeAt(piece, rr), new Vector2(l.x, l.z));
            return piece;
        }

        /// <summary>The piece whose click disc <paramref name="c"/> is (null for any other collider).</summary>
        public DrumPiece PieceOf(Collider c)
        {
            if (c == null || _parts == null) return null;
            for (int i = 0; i < _parts.Clicks.Length; i++) if (ReferenceEquals(_parts.Clicks[i], c)) return _pieces[i];
            return null;
        }

        // =============================== IDrumsApi ===============================
        public IReadOnlyList<DrumPieceInfo> Pieces => _info;
        public int KickId => DrumSpec.KickId;
        public int KickPadId => DrumSpec.KickPadId;
        public int HatId => DrumSpec.HatId;
        public bool HatOpen => _hat != null && _hat.Open;

        /// <summary>Hi-hat open / close: the foot pedal's job. Closing while it rings makes the "chick" of the
        /// cymbals meeting and stops the wash. Stamps the hand and records `hat`, except during playback.</summary>
        public void SetHatOpen(bool open, HandState hand, bool playback = false)
        {
            if (_hat == null || _hat.Open == open) return;
            _hat.Open = open;
            if (!open)
            {
                _host?.Audio?.Choke(DrumKind.Hat);
                _host?.Audio?.Drum(DrumKind.HatClose, DrumSpec.HatCloseVel, DrumStrike.Default);
            }
            if (hand != null) hand.LastPlayMs = Now;
            if (!playback) _host?.Recorder?.Add(TakeEvent.Hat(open));
        }

        /// <summary>Playback hit: forced (no debounce), the first piece of that kind (the kick shell for
        /// "kick"), the recorded strike or {.3, head}.</summary>
        public void PlaybackHit(string kind, float vel, DrumStrikeInfo strike)
        {
            DrumPiece piece = FindByKind(kind);
            if (piece == null) return;
            var s = new DrumStrike
            {
                Pos = float.IsNaN(strike.Pos) ? DrumSpec.DefaultStrikePos : strike.Pos,
                Zone = string.IsNullOrEmpty(strike.Zone) ? DrumZone.Head : DrumStrike.ParseZone(strike.Zone),
                Open = strike.Open,
            };
            Hit(piece, vel, null, true, s);
        }

        /// <summary>Playback choke (not recorded again).</summary>
        public void PlaybackChoke(string kind)
        {
            DrumPiece piece = FindByKind(kind);
            if (piece == null) return;
            piece.Choked = true;   // the plate's rocking dies out under the hand (CymbalMotion.ChokeTau)
            _host?.Audio?.Choke(DrumStrike.ParseKind(kind));
        }

        /// <summary>Stick pick-up, called by Layout first in its Grab pass (ARCHITECTURE.md section 15.2): Grabbed sets
        /// hand.Holding and reparents the stick; Near means a free stick is within the .24 m dead zone; None otherwise
        /// (no free stick near, or the kit out of the scene). Layout decides whether the pinch is consumed.</summary>
        public StickPick TryPickUp(HandState hand, Vector3 tip)
        {
            if (_sticks == null) return StickPick.None;
            DrumSticks.GrabResult r = _sticks.TryGrab(hand, tip);
            return r == DrumSticks.GrabResult.Grabbed ? StickPick.Grabbed : r == DrumSticks.GrabResult.Near ? StickPick.Near : StickPick.None;
        }

        public bool AnyStickHeld => _sticks != null && _sticks.AnyHeld;
        public void ReleaseAllSticks() => _sticks?.ReleaseAll();

        /// <summary>Sunny's hands-far-from-pad check: every active fingertip is at least <paramref name="metres"/> from the piece.</summary>
        public bool EveryHandPointerFartherThan(float metres, int pieceId)
        {
            if (_pieces == null || pieceId < 0 || pieceId >= _pieces.Length) return true;
            IReadOnlyList<Pointer> pts = _host?.HandPointers;
            if (pts == null) return true;
            Vector3 c = _pieces[pieceId].Group.position;
            float m2 = metres * metres;
            for (int i = 0; i < pts.Count; i++)
            {
                Pointer p = pts[i];
                if (!p.Active || p.External) continue;
                if ((p.Tip - c).sqrMagnitude < m2) return false;
            }
            return true;
        }

        public DrumPiece FindByKind(string kind)
        {
            if (_pieces == null || kind == null) return null;
            for (int i = 0; i < _pieces.Length; i++) if (string.Equals(_pieces[i].Kind, kind, System.StringComparison.Ordinal)) return _pieces[i];
            return null;
        }

        public DrumPiece Piece(int id) => _pieces != null && id >= 0 && id < _pieces.Length ? _pieces[id] : null;

        // =============================== rig ===============================

        /// <summary>The rig zone: the kit group's world box (every mesh, the resting sticks and
        /// the contact shadow's 1.9 x 1.6 m floor rectangle included) expanded by ZONE_PAD .12. Layout uses it for "near an
        /// instrument" (swivel, summon). The shadow itself is drawn by the Room module (RigShadows), not under the kit, so
        /// its rectangle is added here from DrumSpec.BlobPos / BlobWidth / BlobDepth.</summary>
        public Bounds ZoneBounds() => _zone.Box();

        sealed class RigZone : IPlayZone
        {
            readonly DrumKit _kit;
            Bounds _box;
            int _frame = -1;
            Matrix4x4 _matrix;
            int _sticksVersion = -1;

            public RigZone(DrumKit kit) { _kit = kit; }

            public bool Contains(Vector3 world) => _kit.IsPresent && Box().Contains(world);

            public Bounds Box()
            {
                Matrix4x4 m = _kit.transform.localToWorldMatrix;
                int sv = _kit._sticks != null ? _kit._sticks.Version : 0;
                if (_frame == Time.frameCount && m == _matrix && sv == _sticksVersion) return _box;
                _frame = Time.frameCount;
                _matrix = m;
                _sticksVersion = sv;
                bool any = false;
                var b = new Bounds();
                DrumKitParts parts = _kit._parts;
                if (parts != null)
                {
                    for (int i = 0; i < parts.Handles.Count; i++) Grow(parts.Handles[i], ref b, ref any);
                    for (int i = 0; i < parts.Played.Count; i++) Grow(parts.Played[i], ref b, ref any);
                    // The contact shadow under the kit: a flat rectangle at y .005 (its corners are the plane's box).
                    float hw = DrumSpec.BlobWidth * 0.5f, hd = DrumSpec.BlobDepth * 0.5f;
                    for (int c = 0; c < 4; c++)
                    {
                        Vector3 corner = m.MultiplyPoint3x4(DrumSpec.BlobPos + new Vector3((c & 1) == 0 ? -hw : hw, 0f, (c & 2) == 0 ? -hd : hd));
                        if (!any) { b = new Bounds(corner, Vector3.zero); any = true; }
                        else b.Encapsulate(corner);
                    }
                }
                if (_kit._sticks != null)
                    for (int s = 0; s < _kit._sticks.Count; s++)
                    {
                        Drumstick stick = _kit._sticks[s];
                        if (stick.Held) continue;   // a stick in a hand is not part of the kit
                        for (int k = 0; k < stick.Renderers.Length; k++) Grow(stick.Renderers[k], ref b, ref any);
                    }
                if (any) b.Expand(DrumSpec.RigZonePad * 2f);
                _box = b;
                return _box;
            }

            static void Grow(Renderer r, ref Bounds b, ref bool any)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
        }
    }
}
