// The marimba's per-frame glue (what MarimbaInstrument will run in phase B, written now against today's Core types so it
// is tested now): hands and mallets in, sound / recorder / bar motion / Sunny signals out.
//   1. the mallets follow their hands (MarimbaMallets.UpdatePoses, before the pointers are read);
//   2. pointer samples in the instrument-local frame: each held, tracked mallet's head (owner = the mallet) and the five
//      fingertips of each free hand (owner = the hand; a hand holding any tool has its fingertips muted);
//   3. MarimbaPlay decides strokes, dead strokes, rests, hand mutes, rolls and signals;
//   4. this listener routes them: IMarimbaSound (the unit), the bar's dip (MarimbaView.Kick), the take event, the hand's
//      LastPlayMs, the signals (the module publishes InstrumentSignalEvent{Marimba, "marimba.*"} in phase B).
// Also here: playback of recorded events (IInstrumentPlayback's shape), the lesson accompaniment (ILessonAccompany, request
// R6), the desktop click path, and the dead-stroke setting. No allocation per frame.
using System;
using System.Collections.Generic;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Hands;
using Unity.Mathematics;
using UnityEngine;

namespace MusicRoom.Marimba
{
    /// <summary>The marimba's take events (spec 5.6; platform 6.1 starting set, prefixed with the id).</summary>
    public static class MarimbaEvents
    {
        public const string Strike = "marimba", Damp = "marimbaDamp", Undamp = "marimbaUndamp";
        public const string ModeMallet = "mallet", ModeFinger = "finger", ModeHand = "hand";
        static readonly string[] MalletNames = { "medium", "soft", "hard", "finger" };
        public static string MalletName(MarimbaMallet m) => MalletNames[(int)m];
        public static MarimbaMallet MalletOf(string kind) =>
            kind == "soft" ? MarimbaMallet.Soft : kind == "hard" ? MarimbaMallet.Hard : kind == "finger" ? MarimbaMallet.Finger : MarimbaMallet.Medium;

        /// <summary>Onset: midi, vel, pos = u (along the bar), u = c (across), mode mallet | finger, kind = the mallet's name.</summary>
        public static TakeEvent StrikeEvent(int bar, float vel, float u, float c, MarimbaMallet mallet) => new TakeEvent
        {
            type = Strike, midi = MarimbaGeometry.MidiOf(bar), vel = vel, pos = u, u = c,
            mode = mallet == MarimbaMallet.Finger ? ModeFinger : ModeMallet, kind = MalletName(mallet),
        };

        /// <summary>Release: midi, mode hand | mallet, pos = u.</summary>
        public static TakeEvent DampEvent(int bar, MarimbaDampKind kind, float u) => new TakeEvent
        {
            type = Damp, midi = MarimbaGeometry.MidiOf(bar), mode = kind == MarimbaDampKind.Hand ? ModeHand : ModeMallet, pos = u,
        };

        /// <summary>State: midi (a played-back mute must end, or the next playback stroke would sound dead).</summary>
        public static TakeEvent UndampEvent(int bar) => new TakeEvent { type = Undamp, midi = MarimbaGeometry.MidiOf(bar) };
    }

    public sealed class MarimbaController : IMarimbaPlayListener
    {
        public const string DeadStrokeKey = "musicroom.marimba.deadStroke";
        /// <summary>The desktop click's velocity (spec 5.6).</summary>
        public const float ClickVel = 0.7f;
        const int Hands = 2, Tips = 5, MaxPointers = 2 + Hands * Tips;

        // every pointer is ticked every frame in a fixed order (mallets, left tips, right tips); an untracked one is "lost"
        readonly MarimbaPointer[] _ptrs = new MarimbaPointer[MaxPointers];
        readonly HandState[] _handOfOwner = new HandState[2 + Hands];

        public readonly MarimbaPlay Play;
        public readonly MarimbaMallets Mallets;
        public IMarimbaSound Sound;
        public MarimbaView View;
        /// <summary>Every live take event (the module forwards it to IRecorder.Add in phase B).</summary>
        public Action<TakeEvent> Record;
        /// <summary>Sunny's signals (phase B: InstrumentSignalEvent{Marimba, "marimba.*"}).</summary>
        public Action<MarimbaSignal> Signal;
        /// <summary>The instrument root's world pose (the pointers are measured in its frame).</summary>
        public Vector3 RigPosition;
        public Quaternion RigRotation = Quaternion.identity;
        /// <summary>The player's last live stroke velocity (NaN before the first): the accompaniment follows it.</summary>
        public double LastLiveVel = double.NaN;
        public int LiveStrikes { get; private set; }

        bool _deadStrokeSetting, _inLesson;
        // the drawn mallets (MarimbaMalletVisual): bounce state, this frame's stroke speed, the drawn pose
        readonly MarimbaMalletVisual.State[] _mv = new MarimbaMalletVisual.State[2];
        readonly float[] _hitSpeed = new float[2];
        readonly Vector3[] _drawnButt = new Vector3[2], _drawnHead = new Vector3[2];
        readonly Quaternion[] _drawnRot = { Quaternion.identity, Quaternion.identity };
        readonly float _h;

        public MarimbaController(IMarimbaSound sound, IMarimbaHandFrames frames, double naturalTopH = MarimbaTables.HDefault, bool deadStrokeSetting = false)
        {
            Sound = sound;
            _h = (float)naturalTopH;
            Play = new MarimbaPlay(this, naturalTopH);
            Mallets = new MarimbaMallets(frames) { NaturalTopH = (float)naturalTopH };
            Mallets.Changed += (m, picked) => Signal?.Invoke(picked ? MarimbaSignal.Pickup : MarimbaSignal.Release);
            for (int s = 0; s < 2; s++) _ptrs[s] = new MarimbaPointer(s == 0 ? "malletL" : "malletR", s, false, -1);
            string[] tipNames = { "thumb", "index", "middle", "ring", "little" };
            for (int h = 0; h < Hands; h++)
                for (int f = 0; f < Tips; f++)
                    _ptrs[2 + h * Tips + f] = new MarimbaPointer(tipNames[f] + (h == 0 ? "L" : "R"), 2 + h, true, f);
            _deadStrokeSetting = deadStrokeSetting;
            ApplyDeadStrokes();
        }

        /// <summary>The stored setting musicroom.marimba.deadStroke (0 until tip_marimba_dead is shown; spec 5.4).</summary>
        public bool DeadStrokeSetting { get => _deadStrokeSetting; set { _deadStrokeSetting = value; ApplyDeadStrokes(); } }
        /// <summary>Dead strokes are off during any lesson whatever the setting (IX3b).</summary>
        public bool InLesson { get => _inLesson; set { _inLesson = value; ApplyDeadStrokes(); } }
        void ApplyDeadStrokes() => Play.DeadStrokes = _deadStrokeSetting && !_inLesson;

        public void SetRigPose(Vector3 position, Quaternion rotation)
        {
            RigPosition = position; RigRotation = rotation;
            Mallets.RigPosition = position; Mallets.RigRotation = rotation;
        }

        double3 ToLocal(Vector3 world)
        {
            Vector3 l = Quaternion.Inverse(RigRotation) * (world - RigPosition);
            return new double3(l.x, l.y, l.z);
        }

        // ------------------------------------------------------------------ the frame

        /// <summary>One frame: hands[0] left, hands[1] right (Core HandStates with their fingertip pointers updated by
        /// PointerInput); sampleDt = the tracker's sample interval (s); nowMs = Clock.NowMs.</summary>
        public void Tick(HandState[] hands, float dt, float sampleDt, double nowMs)
        {
            Mallets.UpdatePoses(dt);
            for (int s = 0; s < 2; s++)
            {
                MarimbaPointer p = _ptrs[s];
                MarimbaMalletTool m = Mallets[s];
                _handOfOwner[s] = m.Hand;
                bool tracked = m.Held && m.HeadPointer.Down;
                p.Advance();
                double3 head = tracked ? ToLocal(m.Head) : default;
                double speed = 0;
                if (tracked && p.HasPrev) { double3 d = head - p.Prev; speed = math.length(d) / Math.Max(sampleDt, 1e-3f); }
                p.Sample(tracked, head, speed);
            }
            for (int h = 0; h < Hands; h++)
            {
                HandState hand = hands != null && h < hands.Length ? hands[h] : null;
                _handOfOwner[2 + h] = hand;
                // a hand that holds any tool (a mallet, a drumstick) plays with the tool, not its fingertips; the desktop
                // mouse hand never plays (ARCHITECTURE section 0)
                bool free = hand != null && hand.Connected && hand.Holding == null && !hand.IsMouse;
                for (int f = 0; f < Tips; f++)
                {
                    MarimbaPointer p = _ptrs[2 + h * Tips + f];
                    Pointer cp = free ? TipPointer(hand, f) : null;
                    bool tracked = cp != null && cp.Active;
                    p.Advance();
                    p.Sample(tracked, tracked ? ToLocal(cp.Tip) : default, tracked ? cp.Speed : 0);
                }
            }
            Play.Tick(_ptrs, MaxPointers, Math.Max(1e-3f, sampleDt), nowMs);
            UpdateDrawnMallets(dt);
        }

        // ------------------------------------------------------------------ the platform's pointer list

        // the sample gate (repeated tracker samples): per slot, the last tracker sample time seen and when it was fresh
        readonly double[] _lastSampleT = new double[MaxPointers];
        readonly double[] _lastFreshMs = new double[MaxPointers];
        readonly bool[] _seenThisFrame = new bool[MaxPointers];

        /// <summary>A pointer of the list carries a new tracker sample (PointerInput moves SampleT only when the tip moved), or
        /// enough time has passed for one: a perfectly still hand still yields samples at the learned interval (SampleDt). At
        /// the build's LOW hand-tracking rate an app frame can repeat the last sample: that frame is not fresh.</summary>
        public static bool IsFresh(double sampleT, double lastSampleT, double nowMs, double lastFreshMs, float sampleDt) =>
            sampleT != lastSampleT || sampleDt <= 0f || nowMs - lastFreshMs >= 750.0 * sampleDt;

        /// <summary>One frame from Core's pointer list for this instrument (BUILD-GUIDE 4.2: the fingertips of both hands plus
        /// the tool pointers whose Tool is in the descriptor's mask, i.e. the two mallet heads). The mallets' poses were updated
        /// in FramePhase.StickPoses (MarimbaMallets.UpdatePoses) before PointerInput read them.</summary>
        public void TickPointers(IReadOnlyList<Pointer> pointers, float dt, double nowMs)
        {
            for (int i = 0; i < MaxPointers; i++) _seenThisFrame[i] = false;
            for (int i = 0; i < 2 + Hands; i++) _handOfOwner[i] = null;
            float sampleDt = 0f;
            int n = pointers != null ? pointers.Count : 0;
            for (int i = 0; i < n; i++)
            {
                Pointer cp = pointers[i];
                if (cp == null || cp.FromMouse) continue;   // the desktop mouse hand never plays (its click path does)
                int slot;
                if (cp.Tool == ToolKind.Mallet && cp.Owner is MarimbaMalletTool m) { slot = m.Side; _handOfOwner[slot] = m.Hand; }
                else if (cp.Owner is HandState h && cp.Finger >= 0 && h.Index < Hands)
                {
                    slot = 2 + h.Index * Tips + cp.Finger;
                    _handOfOwner[2 + h.Index] = h;
                    if (h.Holding != null) continue;          // a hand holding a tool plays with the tool
                }
                else continue;
                _seenThisFrame[slot] = true;
                MarimbaPointer p = _ptrs[slot];
                bool tracked = cp.Active;
                if (tracked && cp.SampleDt > sampleDt) sampleDt = cp.SampleDt;
                bool fresh = !tracked || !p.Active || IsFresh(cp.SampleT, _lastSampleT[slot], nowMs, _lastFreshMs[slot], cp.SampleDt);
                p.Fresh = fresh;
                if (!fresh) continue;
                _lastSampleT[slot] = cp.SampleT;
                _lastFreshMs[slot] = nowMs;
                p.Advance();
                p.Sample(tracked, tracked ? ToLocal(cp.Tip) : default, tracked ? cp.Speed : 0);
            }
            for (int i = 0; i < MaxPointers; i++)
            {
                if (_seenThisFrame[i]) continue;
                MarimbaPointer p = _ptrs[i];
                p.Fresh = true;
                if (!p.Active && !p.HasTip) continue;
                p.Advance();
                p.Sample(false, default);
            }
            Play.Tick(_ptrs, MaxPointers, Math.Max(1e-3f, sampleDt > 0f ? sampleDt : dt), nowMs);
            UpdateDrawnMallets(dt);
        }

        // the drawn mallets: pivoted about the grip so the head rides on the bar top, a bounce after a hit, pressed while a
        // dead stroke or a rest holds the bar (spec 4.4)
        void UpdateDrawnMallets(float dt)
        {
            Vector3 up = RigRotation * Vector3.up;
            for (int s = 0; s < 2; s++)
            {
                MarimbaMalletTool m = Mallets[s];
                if (m.Held && m.HeadPointer.Down)
                {
                    Vector3 local = Quaternion.Inverse(RigRotation) * (m.Head - RigPosition);
                    int bar = MarimbaMalletVisual.BarUnder(local, _h, out float top);
                    Vector3 surface = RigPosition + RigRotation * new Vector3(local.x, top, local.z);
                    MarimbaMalletVisual.Step(ref _mv[s], dt, m.Grip, m.Butt, m.Rotation, m.Head, bar >= 0, surface, up, _hitSpeed[s],
                        _ptrs[s].HoldsDamped, out _drawnButt[s], out _drawnRot[s], out _drawnHead[s]);
                }
                else
                {
                    _mv[s] = default;
                    _drawnButt[s] = m.Butt; _drawnRot[s] = m.Rotation; _drawnHead[s] = m.Head;
                }
                _hitSpeed[s] = 0f;
            }
        }

        /// <summary>The drawn pose of a mallet (world): butt, rotation (+y along the shaft), head centre.</summary>
        public void DrawnMallet(int side, out Vector3 butt, out Quaternion rot, out Vector3 head)
        {
            butt = _drawnButt[side]; rot = _drawnRot[side]; head = _drawnHead[side];
        }

        /// <summary>Poses the view's two mallets (call after Tick).</summary>
        public void ApplyMallets(MarimbaView view)
        {
            if (view == null) return;
            for (int s = 0; s < 2; s++)
            {
                MarimbaMalletTool m = Mallets[s];
                if (m.Held || m.Settle < 1f) view.Mallet(s).SetPositionAndRotation(_drawnButt[s], _drawnRot[s]);
                else view.RestMallet(s);   // on the tray: rig-local, so it moves with the marimba
            }
        }

        /// <summary>The pointer of a mallet (0, 1) or of a fingertip (2 + hand * 5 + finger), for tests and diagnostics.</summary>
        public MarimbaPointer PointerAt(int i) => _ptrs[i];

        static Pointer TipPointer(HandState h, int finger)
        {
            // HandState.Pointers is in tip order [index, middle, ring, little, thumb]
            switch (finger)
            {
                case 0: return h.Pointers[(int)PointerSlot.Thumb];
                case 1: return h.Pointers[(int)PointerSlot.Index];
                case 2: return h.Pointers[(int)PointerSlot.Middle];
                case 3: return h.Pointers[(int)PointerSlot.Ring];
                default: return h.Pointers[(int)PointerSlot.Little];
            }
        }

        // ------------------------------------------------------------------ MarimbaPlay's decisions

        public void OnStrike(int bar, double vel, double u, double c, MarimbaPointer by, bool deferred, double nowMs)
        {
            MarimbaMallet mallet = by.Finger ? MarimbaMallet.Finger : MarimbaMallet.Medium;
            float v = (float)vel, fu = (float)u, fc = (float)c;
            Sound?.Strike(bar, v, fu, fc, mallet);
            View?.Kick(bar, v, fc);
            Record?.Invoke(MarimbaEvents.StrikeEvent(bar, v, fu, fc, mallet));
            LastLiveVel = vel;
            LiveStrikes++;
            HandState h = by.Owner < _handOfOwner.Length ? _handOfOwner[by.Owner] : null;
            if (h != null && by.Finger) PlayStamp.Mark(h, InstrumentKind.Marimba, nowMs);
            if (!by.Finger)
            {
                PlayStamp.Mark(Mallets[by.Owner], InstrumentKind.Marimba, nowMs);   // and its holder (IHeldBy)
                // the head's approach speed (the inverse of the velocity curve) kicks the drawn mallet's bounce
                _hitSpeed[by.Owner] = (float)(MarimbaIx.V0Mallet + (MarimbaIx.V1Mallet - MarimbaIx.V0Mallet) * Math.Pow(vel, 1 / MarimbaIx.VelExp));
            }
        }

        public void OnDamp(int bar, MarimbaDampKind kind, double u, bool dead, int owner, double nowMs)
        {
            Sound?.Damp(bar, kind, (float)u, 0f);
            Record?.Invoke(MarimbaEvents.DampEvent(bar, kind, (float)u));
            HandState h = owner < _handOfOwner.Length ? _handOfOwner[owner] : null;
            if (h != null) PlayStamp.Mark(h, InstrumentKind.Marimba, nowMs);   // a resting hand counts as playing (the harp's rule)
        }

        public void OnUndamp(int bar, string why, double nowMs)
        {
            Sound?.Undamp(bar);
            Record?.Invoke(MarimbaEvents.UndampEvent(bar));
        }

        public void OnRoll(int bar, double nowMs) { }

        public void OnSignal(MarimbaSignal s, double nowMs) => Signal?.Invoke(s);

        // ------------------------------------------------------------------ playback, accompaniment, desktop, stow

        /// <summary>IInstrumentPlayback.Play: a recorded event drives the sound and the bar's dip (never re-recorded).</summary>
        public bool PlayRecorded(in TakeEvent e)
        {
            int bar = e.midi - MarimbaGeometry.Midi0;
            if ((uint)bar >= MarimbaGeometry.Bars) return false;
            if (e.type == MarimbaEvents.Strike)
            {
                MarimbaMallet m = e.mode == MarimbaEvents.ModeFinger ? MarimbaMallet.Finger : MarimbaEvents.MalletOf(e.kind);
                Sound?.Strike(bar, e.vel, e.pos, e.u, m);
                View?.Kick(bar, e.vel, e.u);
                return true;
            }
            if (e.type == MarimbaEvents.Damp) { Sound?.Damp(bar, e.mode == MarimbaEvents.ModeHand ? MarimbaDampKind.Hand : MarimbaDampKind.Mallet, e.pos, 0f); return true; }
            if (e.type == MarimbaEvents.Undamp) { Sound?.Undamp(bar); return true; }
            return false;
        }

        /// <summary>IInstrumentPlayback.ReleaseAll: every bar undamped (struck notes simply decay).</summary>
        public void ReleaseAllPlayback() { for (int b = 0; b < MarimbaGeometry.Bars; b++) Sound?.Undamp(b); }

        /// <summary>ILessonAccompany.Play (request R6): the marimba's own bars answer, at 0.35 x the player's last live stroke
        /// (at most the written vel, at least 0.05), struck at u .45 on the centre line with the yarn mallet. No bar moves,
        /// no take event, never scored.</summary>
        public void Accompany(int[] midi, float writtenVel)
        {
            if (midi == null) return;
            float v = (float)MarimbaIx.AccompanyVel(writtenVel, LastLiveVel);
            for (int k = 0; k < midi.Length; k++)
            {
                int bar = midi[k] - MarimbaGeometry.Midi0;
                if ((uint)bar < MarimbaGeometry.Bars) Sound?.Strike(bar, v, 0.45f, 0f, MarimbaMallet.Medium);
            }
        }

        /// <summary>Desktop (!App.IsPresenting): a left click on a bar plays it at vel 0.7 from the hit point; Shift+click mutes
        /// it with a hand (a second Shift+click lets it ring).</summary>
        public void Click(int bar, Vector3 hitWorld, bool shift)
        {
            if ((uint)bar >= MarimbaGeometry.Bars) return;
            double3 l = ToLocal(hitWorld);
            float u = Mathf.Clamp01((float)((l.z - MarimbaGeometry.NearZ(bar)) / MarimbaGeometry.Length(bar)));
            float c = Mathf.Clamp((float)((l.x - MarimbaGeometry.X(bar)) / (MarimbaGeometry.Width(bar) * 0.5f)), -1f, 1f);
            if (shift)
            {
                Sound?.Damp(bar, MarimbaDampKind.Hand, u, 0f);
                Record?.Invoke(MarimbaEvents.DampEvent(bar, MarimbaDampKind.Hand, u));
                return;
            }
            Sound?.Strike(bar, ClickVel, u, c, MarimbaMallet.Medium);
            View?.Kick(bar, ClickVel, c);
            Record?.Invoke(MarimbaEvents.StrikeEvent(bar, ClickVel, u, c, MarimbaMallet.Medium));
            LastLiveVel = ClickVel;
        }

        /// <summary>Arrange mode, a grab of the rig, SetStowed: drop every mute, silence the bars, lay the mallets down.</summary>
        public void Quiet(double nowMs, bool layMalletsDown)
        {
            Play.ReleaseAll(_ptrs, MaxPointers, nowMs);
            Sound?.Silence();
            if (layMalletsDown) Mallets.LayOnTray();
        }
    }
}
