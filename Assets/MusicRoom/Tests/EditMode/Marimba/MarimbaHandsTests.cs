// The mallets (spec 5.1: the drumsticks' pick-up, grip, release, tracking loss and settle) and the controller that turns
// hands and mallets into strokes (spec 5.3-5.6), driven by scripted hand frames and Core HandStates as PointerInput fills
// them. Also the lesson targets, Sunny's anchors, the take events, playback and the lesson accompaniment.
using System;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Marimba;
using MusicRoom.Tests.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Marimba
{
    /// <summary>Records the unit calls the marimba makes.</summary>
    public sealed class MarimbaSoundLog : IMarimbaSound
    {
        public struct Call { public string Op; public int Bar; public float Vel, U, C; public MarimbaMallet Mallet; public MarimbaDampKind Kind; }
        public readonly List<Call> Calls = new List<Call>(256);
        public void Strike(int bar, float vel, float u, float c, MarimbaMallet mallet, double when = 0) => Calls.Add(new Call { Op = "strike", Bar = bar, Vel = vel, U = u, C = c, Mallet = mallet });
        public void Damp(int bar, MarimbaDampKind kind, float u, float c, double when = 0) => Calls.Add(new Call { Op = "damp", Bar = bar, Kind = kind, U = u });
        public void Undamp(int bar, double when = 0) => Calls.Add(new Call { Op = "undamp", Bar = bar });
        public void Silence() => Calls.Add(new Call { Op = "silence" });
        public float BarLevel(int bar) => 0f;
        public int Count(string op) { int n = 0; foreach (var c in Calls) if (c.Op == op) n++; return n; }
    }

    /// <summary>Two scripted hands: OpenXR joints from the baked rest pose at a wrist pose, with a chosen pinch gap.</summary>
    public sealed class MarimbaFakeHands : IMarimbaHandFrames
    {
        public readonly HandFrame[] F = { new HandFrame(HandSide.Left), new HandFrame(HandSide.Right) };
        public readonly HandState[] H = { new HandState(Handedness.Left), new HandState(Handedness.Right) };
        readonly Vector3[] _local = new Vector3[HandFrame.JointCount];
        public HandFrame Frame(HandState h) => F[h.Index];

        /// <summary>Puts hand `side` so that its thumb-index midpoint is at `pinchMid`, the wrist rotated `rot`, tips `gap` apart.</summary>
        public void PoseAtPinch(int side, Vector3 pinchMid, Quaternion rot, float gap)
        {
            HandFrame f = F[side];
            HandRestPose.Fill((HandSide)side, _local);
            Vector3 mid = (_local[(int)HandJoint.ThumbTip] + _local[(int)HandJoint.IndexTip]) * 0.5f;
            Vector3 wrist = pinchMid - rot * mid;
            for (int j = 0; j < HandFrame.JointCount; j++) { f.position[j] = wrist + rot * _local[j]; f.rotation[j] = rot; }
            Vector3 it = f.position[(int)HandJoint.IndexTip], tt = f.position[(int)HandJoint.ThumbTip];
            Vector3 d = (it - tt).normalized;
            f.position[(int)HandJoint.ThumbTip] = pinchMid - d * gap * 0.5f;
            f.position[(int)HandJoint.IndexTip] = pinchMid + d * gap * 0.5f;
            f.isTracked = true; f.isHighConfidence = true;
            HandState h = H[side];
            h.Connected = true;
            bool was = h.GrabPinching;
            h.GrabPinching = h.Pinching = gap <= HandMath.PinchStartGap;
            h.GrabPinchStart = h.GrabPinching && !was;
        }

        public void Lose(int side) { F[side].MarkLost(); H[side].Connected = false; }
    }

    sealed class Stick : IHandTool { }

    public unsafe class MarimbaHandsTests
    {
        static readonly Quaternion Palm = Quaternion.Euler(70f, 0f, 0f);   // fingers forward and down, as over a keyboard

        // ------------------------------------------------------------------ mallets

        [Test]
        public void Mallets_PickUpByDistance_NoneNearGrabbed()
        {
            var hands = new MarimbaFakeHands();
            var m = new MarimbaMallets(hands);
            Vector3 grip = MarimbaGeometry.MalletRestGrip(1);
            hands.PoseAtPinch(1, grip + Vector3.up * 0.35f, Palm, 0.01f);
            Assert.That(m.TryPickUp(hands.H[1], grip), Is.EqualTo(StickPick.None), "35 cm: nothing");
            hands.PoseAtPinch(1, grip + Vector3.up * 0.15f, Palm, 0.01f);
            Assert.That(m.TryPickUp(hands.H[1], grip), Is.EqualTo(StickPick.Near), "15 cm: the pinch is consumed");
            Assert.That(hands.H[1].Holding, Is.Null);
            hands.PoseAtPinch(1, grip + Vector3.up * 0.02f, Palm, 0.01f);
            Assert.That(m.TryPickUp(hands.H[1], grip), Is.EqualTo(StickPick.Grabbed), "2 cm: picked up");
            Assert.That(hands.H[1].Holding, Is.SameAs(m[1]), "the right hand finds its own mallet (11 cm right of centre)");
            Assert.That(m.AnyHeld, Is.True);
            // a hand already holding a tool (a drumstick) picks nothing up
            hands.H[0].Holding = new Stick();
            hands.PoseAtPinch(0, MarimbaGeometry.MalletRestGrip(0) + Vector3.up * 0.01f, Palm, 0.01f);
            Assert.That(m.TryPickUp(hands.H[0], MarimbaGeometry.MalletRestGrip(0)), Is.EqualTo(StickPick.None));
            // not at the seat: nothing to pick up
            hands.H[0].Holding = null;
            m.Present = false;
            Assert.That(m.TryPickUp(hands.H[0], MarimbaGeometry.MalletRestGrip(0)), Is.EqualTo(StickPick.None));
            Assert.That(float.IsPositiveInfinity(m.NearestFree(MarimbaGeometry.MalletRestGrip(0))), Is.True);
        }

        [Test]
        public void Mallets_MetaPinchAndFistRules()
        {
            // Meta's index pinch at strength 0.86 starts the grab pinch (HandInput; HandMath.NextMetaPinch), which picks up
            Assert.That(HandMath.NextMetaPinch(false, 0.86f, false), Is.True);
            var h = new HandState(Handedness.Right) { Connected = true, GrabPinchStart = true };
            Assert.That(MarimbaMallets.WantsPickUp(h, playing: true), Is.True, "a grab pinch picks up even mid-play");
            h.GrabPinchStart = false; h.GraspStart = true;
            Assert.That(MarimbaMallets.WantsPickUp(h, playing: false), Is.True, "a closing fist picks up while not playing");
            Assert.That(MarimbaMallets.WantsPickUp(h, playing: true), Is.False, "but not while the hand is playing");
        }

        [Test]
        public void Mallets_HeldPoseIsTheSticksLever()
        {
            var hands = new MarimbaFakeHands();
            var m = new MarimbaMallets(hands);
            Vector3 grip = MarimbaGeometry.MalletRestGrip(1);
            hands.PoseAtPinch(1, grip + Vector3.up * 0.01f, Palm, 0.01f);
            Assert.That(m.TryPickUp(hands.H[1], grip), Is.EqualTo(StickPick.Grabbed));
            Vector3 target = new Vector3(0.1f, 0.85f, -0.2f);
            hands.PoseAtPinch(1, target, Palm, 0.01f);
            m.UpdatePoses(1 / 90f);
            MarimbaMalletTool ml = m[1];
            Assert.That(Vector3.Distance(ml.Grip, target), Is.LessThan(1e-5f), "the grip is the thumb-index midpoint");
            Assert.That(Vector3.Distance(ml.Butt, ml.Grip), Is.EqualTo(0.10f).Within(1e-5f), "butt 0.10 m behind the grip");
            Assert.That(Vector3.Distance(ml.Head, ml.Grip), Is.EqualTo(0.27f).Within(1e-5f), "head 0.27 m beyond it");
            Vector3 fwd = (ml.Grip - hands.F[1].position[(int)HandJoint.Wrist]).normalized;
            Assert.That(Vector3.Angle(fwd, ml.Axis), Is.EqualTo(12.6f).Within(0.1f), "the sticks' droop toward the palm");
            Assert.That(ml.HeadPointer.Down, Is.True);
            Assert.That((Vector3)ml.HeadPointer.Next, Is.EqualTo(ml.Head));
            Span<Vector4> sh = stackalloc Vector4[4];
            Assert.That(m.ShadowSpheres(sh), Is.EqualTo(1));
            Assert.That(sh[0].w, Is.EqualTo(0.017f), "the head's shadow sphere, r 17 mm");
        }

        [Test]
        public void Mallets_OpenHandReleasesAfterAQuarterSecond_ThenSettlesOnTheTray()
        {
            var hands = new MarimbaFakeHands();
            var m = new MarimbaMallets(hands);
            int releases = 0;
            m.Changed += (ml, picked) => { if (!picked) releases++; };
            Vector3 grip = MarimbaGeometry.MalletRestGrip(0);
            hands.PoseAtPinch(0, grip + Vector3.up * 0.02f, Quaternion.Euler(70f, 0f, 0f), 0.01f);
            Assert.That(m.TryPickUp(hands.H[0], grip), Is.EqualTo(StickPick.Grabbed));
            hands.PoseAtPinch(0, new Vector3(-0.2f, 0.9f, -0.1f), Quaternion.Euler(70f, 0f, 0f), 0.08f);   // open: 8 cm gap
            for (int k = 0; k < 2; k++) { m.UpdatePoses(0.1f); m.UpdateReleases(0.1f); }
            Assert.That(m[0].Held, Is.True, "0.2 s open: still held (a relaxed grip keeps it)");
            hands.H[0].Grasping = true;
            m.UpdateReleases(0.2f);
            Assert.That(m[0].Held, Is.True, "a closed fist keeps it");
            hands.H[0].Grasping = false;
            for (int k = 0; k < 3; k++) m.UpdateReleases(0.1f);
            Assert.That(m[0].Held, Is.False, "open for more than 0.25 s: back to the tray");
            Assert.That(releases, Is.EqualTo(1));
            Assert.That(hands.H[0].Holding, Is.Null);
            for (int k = 0; k < 37; k++) m.UpdatePoses(1 / 90f);   // 0.41 s
            m.RestLocal(0, out Vector3 rb, out _);
            Assert.That(m[0].Settle, Is.EqualTo(1f), "settled in 0.4 s");
            Assert.That(Vector3.Distance(m[0].Butt, rb), Is.LessThan(1e-5f), "on the tray");
        }

        [Test]
        public void Mallets_TrackingLoss_FreezesSilentThenDropsAfterOneAndAHalfSeconds()
        {
            var hands = new MarimbaFakeHands();
            var m = new MarimbaMallets(hands);
            Vector3 grip = MarimbaGeometry.MalletRestGrip(1);
            hands.PoseAtPinch(1, grip + Vector3.up * 0.02f, Palm, 0.01f);
            m.TryPickUp(hands.H[1], grip);
            hands.PoseAtPinch(1, new Vector3(0.2f, 0.9f, -0.1f), Palm, 0.01f);
            m.UpdatePoses(1 / 90f);
            Vector3 frozen = m[1].Head;
            hands.Lose(1);
            for (int k = 0; k < 3; k++) { m.UpdatePoses(0.4f); Assert.That(m[1].Held, Is.True); Assert.That(m[1].HeadPointer.Down, Is.False, "silent while lost"); Assert.That(m[1].Head, Is.EqualTo(frozen), "frozen"); }
            hands.PoseAtPinch(1, new Vector3(0.25f, 0.9f, -0.1f), Palm, 0.01f);
            m.UpdatePoses(1 / 90f);
            Assert.That(m[1].HeadPointer.Down, Is.True, "back: snaps to the hand");
            Assert.That(Vector3.Distance(m[1].Grip, new Vector3(0.25f, 0.9f, -0.1f)), Is.LessThan(1e-5f));
            hands.Lose(1);
            for (int k = 0; k < 4; k++) m.UpdatePoses(0.4f);
            Assert.That(m[1].Held, Is.False, "dropped after 1.5 s of loss");
        }

        // ------------------------------------------------------------------ the controller

        static void Tip(HandState h, PointerSlot slot, Vector3 world, bool active = true, float speed = 0.6f)
        {
            Pointer p = h.Pointers[(int)slot];
            p.Prev = p.Tip; p.WasActive = p.Active;
            p.Tip = world; p.Active = active; p.Speed = speed;
        }

        [Test]
        public void Controller_FingertipTapPlaysTheBarAsAFingerStroke()
        {
            var hands = new MarimbaFakeHands();
            var sound = new MarimbaSoundLog();
            var takes = new List<TakeEvent>();
            var ctl = new MarimbaController(sound, hands) { Record = e => takes.Add(e) };
            // the instrument stands 0.5 m ahead of the seat, turned 30 degrees: pointers are measured in its own frame
            Quaternion yaw = Quaternion.Euler(0, 30, 0);
            Vector3 origin = new Vector3(0.3f, 0f, 0.5f);
            ctl.SetRigPose(origin, yaw);
            int c5 = MarimbaGeometry.BarOf("C5");
            HandState h = hands.H[1];
            h.Connected = true;
            for (int k = 0; k < 20; k++)
            {
                float hgt = 0.05f - 0.6f * k / 90f;
                Vector3 local = MarimbaGeometry.TopCentre(c5) + new Vector3(0, (float)MarimbaIx.FingerR + hgt, 0);
                Tip(h, PointerSlot.Index, origin + yaw * local);
                ctl.Tick(hands.H, 1 / 90f, 1 / 90f, k * 1000 / 90.0);
            }
            Assert.That(sound.Count("strike"), Is.EqualTo(1));
            var s = sound.Calls.Find(c => c.Op == "strike");
            Assert.That(s.Bar, Is.EqualTo(c5));
            Assert.That(s.Mallet, Is.EqualTo(MarimbaMallet.Finger));
            Assert.That(s.Vel, Is.EqualTo(0.4775f).Within(0.002f), "IX7: 0.6 m/s fingertip");
            Assert.That(takes.Count, Is.EqualTo(1));
            Assert.That(takes[0].type, Is.EqualTo("marimba")); Assert.That(takes[0].midi, Is.EqualTo(72)); Assert.That(takes[0].mode, Is.EqualTo("finger"));
            Assert.That(h.LastPlayMs, Is.GreaterThan(0), "the hand is stamped as playing");
            Assert.That(ctl.LastLiveVel, Is.EqualTo(s.Vel).Within(1e-6));
        }

        [Test]
        public void Controller_PickUpAMalletAndStrike_TheHandsTipsAreMuted()
        {
            var hands = new MarimbaFakeHands();
            var sound = new MarimbaSoundLog();
            var signals = new List<MarimbaSignal>();
            var ctl = new MarimbaController(sound, hands) { Signal = s => signals.Add(s) };
            HandState h = hands.H[1];
            Vector3 grip = MarimbaGeometry.MalletRestGrip(1);
            hands.PoseAtPinch(1, grip + Vector3.up * 0.02f, Palm, 0.01f);
            Assert.That(ctl.Mallets.TryPickUp(h, grip), Is.EqualTo(StickPick.Grabbed));
            Assert.That(signals, Does.Contain(MarimbaSignal.Pickup));
            int g4 = MarimbaGeometry.BarOf("G4");
            // move the hand so the head comes straight down on G4 at 1.5 m/s; the fingertips also cross bars but must not play
            Vector3 headTarget = MarimbaGeometry.TopCentre(g4);
            for (int k = 0; k < 30; k++)
            {
                float hgt = 0.08f - 1.5f * k / 90f;
                hands.PoseAtPinch(1, Vector3.zero, Palm, 0.01f);
                ctl.Mallets.UpdatePoses(0f);
                Vector3 off = ctl.Mallets[1].Head - ctl.Mallets[1].Grip;      // the head relative to the grip for this hand pose
                Vector3 want = headTarget + Vector3.up * ((float)MarimbaIx.HeadR + hgt);
                hands.PoseAtPinch(1, want - off, Palm, 0.01f);
                Tip(h, PointerSlot.Index, want + Vector3.down * 0.01f);       // a fingertip below the head: muted while holding
                ctl.Tick(hands.H, 1 / 90f, 1 / 90f, k * 1000 / 90.0);
            }
            Assert.That(sound.Count("strike"), Is.EqualTo(1), "one stroke, by the mallet");
            var st = sound.Calls.Find(c => c.Op == "strike");
            Assert.That(st.Bar, Is.EqualTo(g4)); Assert.That(st.Mallet, Is.EqualTo(MarimbaMallet.Medium));
            Assert.That(st.Vel, Is.EqualTo(0.6071f).Within(0.01f), "IX1's velocity at 1.5 m/s");
            Assert.That(ctl.Mallets[1].LastPlayMs, Is.GreaterThan(0));
        }

        [Test]
        public void Controller_TheDrawnHeadNeverSinksIntoTheBar_AndBouncesOffAHit()
        {
            var hands = new MarimbaFakeHands();
            var ctl = new MarimbaController(new MarimbaSoundLog(), hands);
            HandState h = hands.H[1];
            Vector3 grip = MarimbaGeometry.MalletRestGrip(1);
            // a playing grip: the mallet about 35 degrees below horizontal (spec 5.5; within ~20 degrees of vertical the
            // clamp fades out on purpose, as the sticks', because its direction would follow tracking jitter)
            Quaternion play = Quaternion.Euler(25f, 0f, 0f);
            hands.PoseAtPinch(1, grip + Vector3.up * 0.02f, play, 0.01f);
            ctl.Mallets.TryPickUp(h, grip);
            int g4 = MarimbaGeometry.BarOf("G4");
            float top = MarimbaGeometry.Top(g4);
            Vector3 target = MarimbaGeometry.TopCentre(g4);
            float minClear = float.MaxValue, maxBounce = 0f;
            int strikes = 0;
            var sound = (MarimbaSoundLog)ctl.Sound;
            for (int k = 0; k < 60; k++)
            {
                // down at 1.5 m/s to 3 cm under the plane, held 6 frames, then back up at 0.6 m/s
                float hgt = k < 12 ? Mathf.Max(-0.03f, 0.08f - 1.5f * k / 90f) : k < 18 ? -0.03f : -0.03f + 0.6f * (k - 18) / 90f;
                hands.PoseAtPinch(1, Vector3.zero, play, 0.01f);
                ctl.Mallets.UpdatePoses(0f);
                Vector3 off = ctl.Mallets[1].Head - ctl.Mallets[1].Grip;
                Vector3 raw = target + Vector3.up * ((float)MarimbaIx.HeadR + hgt);
                hands.PoseAtPinch(1, raw - off, play, 0.01f);
                ctl.Tick(hands.H, 1 / 90f, 1 / 90f, k * 1000 / 90.0);
                ctl.DrawnMallet(1, out _, out _, out Vector3 drawn);
                Vector3 rawHead = ctl.Mallets[1].Head;
                if (rawHead.y - top < MarimbaMalletVisual.HeadRadius) minClear = Mathf.Min(minClear, drawn.y - top);
                if (k < 30) maxBounce = Mathf.Max(maxBounce, drawn.y - Mathf.Max(rawHead.y, top + MarimbaMalletVisual.HeadRadius));
                if (sound.Count("strike") > strikes) strikes = sound.Count("strike");
            }
            Assert.That(strikes, Is.EqualTo(1));
            Assert.That(minClear, Is.GreaterThanOrEqualTo(MarimbaMalletVisual.HeadRadius - 0.0005f), "the drawn head rides on the bar top, never through it");
            Assert.That(maxBounce, Is.GreaterThan(0.001f), "a visible rebound after the hit");
            Debug.Log($"[MARIMBA] drawn mallet: lowest drawn head {minClear * 1000:F1} mm above the bar top (r 17 mm), rebound up to {maxBounce * 1000:F1} mm");
        }

        [Test]
        public void Controller_DeadStrokesFollowTheSettingAndLessons()
        {
            var ctl = new MarimbaController(new MarimbaSoundLog(), new MarimbaFakeHands());
            Assert.That(ctl.Play.DeadStrokes, Is.False, "off until tip_marimba_dead");
            ctl.DeadStrokeSetting = true;
            Assert.That(ctl.Play.DeadStrokes, Is.True);
            ctl.InLesson = true;
            Assert.That(ctl.Play.DeadStrokes, Is.False, "off in any lesson (IX3b)");
            ctl.InLesson = false;
            Assert.That(ctl.Play.DeadStrokes, Is.True);
        }

        [Test]
        public void Controller_AccompanimentFollowsThePlayer_NoTakeEvent()
        {
            var sound = new MarimbaSoundLog();
            var takes = new List<TakeEvent>();
            var ctl = new MarimbaController(sound, new MarimbaFakeHands()) { Record = e => takes.Add(e) };
            ctl.Accompany(new[] { 48, 55 }, 0.28f);
            Assert.That(sound.Calls[0].Vel, Is.EqualTo(0.28f).Within(1e-6f), "before any live stroke: the written vel");
            ctl.LastLiveVel = 0.6;
            sound.Calls.Clear();
            ctl.Accompany(new[] { 48, 55 }, 0.28f);
            Assert.That(sound.Calls.Count, Is.EqualTo(2));
            Assert.That(sound.Calls[0].Bar, Is.EqualTo(0)); Assert.That(sound.Calls[1].Bar, Is.EqualTo(7));
            Assert.That(sound.Calls[0].Vel, Is.EqualTo(0.21f).Within(1e-6f), "0.35 x the player's 0.6");
            Assert.That(sound.Calls[0].U, Is.EqualTo(0.45f)); Assert.That(sound.Calls[0].Mallet, Is.EqualTo(MarimbaMallet.Medium));
            ctl.LastLiveVel = 0.1;
            sound.Calls.Clear();
            ctl.Accompany(new[] { 48, 52, 55 }, 0.3f);
            Assert.That(sound.Calls[0].Vel, Is.EqualTo(0.05f).Within(1e-6f), "never below 0.05");
            Assert.That(takes.Count, Is.EqualTo(0), "never recorded");
        }

        [Test]
        public void Controller_PlaybackDrivesTheSoundAndIsNotRecorded()
        {
            var sound = new MarimbaSoundLog();
            var takes = new List<TakeEvent>();
            var ctl = new MarimbaController(sound, new MarimbaFakeHands()) { Record = e => takes.Add(e) };
            Assert.That(ctl.PlayRecorded(MarimbaEvents.StrikeEvent(12, 0.6f, 0.45f, 0.1f, MarimbaMallet.Finger)), Is.True);
            Assert.That(ctl.PlayRecorded(MarimbaEvents.DampEvent(12, MarimbaDampKind.Hand, 0.5f)), Is.True);
            Assert.That(ctl.PlayRecorded(MarimbaEvents.UndampEvent(12)), Is.True);
            Assert.That(ctl.PlayRecorded(new TakeEvent { type = "pianoOn", midi = 60 }), Is.False);
            Assert.That(sound.Calls[0].Op, Is.EqualTo("strike")); Assert.That(sound.Calls[0].Mallet, Is.EqualTo(MarimbaMallet.Finger)); Assert.That(sound.Calls[0].U, Is.EqualTo(0.45f)); Assert.That(sound.Calls[0].C, Is.EqualTo(0.1f));
            Assert.That(sound.Calls[1].Op, Is.EqualTo("damp")); Assert.That(sound.Calls[1].Kind, Is.EqualTo(MarimbaDampKind.Hand));
            Assert.That(sound.Calls[2].Op, Is.EqualTo("undamp"));
            Assert.That(takes.Count, Is.EqualTo(0));
            sound.Calls.Clear();
            ctl.ReleaseAllPlayback();
            Assert.That(sound.Count("undamp"), Is.EqualTo(37), "ReleaseAll undamps every bar");
        }

        [Test]
        public void Controller_DesktopClickPlaysAtSevenTenthsAndShiftMutes()
        {
            var sound = new MarimbaSoundLog();
            var ctl = new MarimbaController(sound, new MarimbaFakeHands());
            int e4 = MarimbaGeometry.BarOf("E4");
            ctl.Click(e4, MarimbaGeometry.TopPoint(e4, 0.5f, 0.2f), false);
            Assert.That(sound.Calls[0].Vel, Is.EqualTo(0.7f)); Assert.That(sound.Calls[0].U, Is.EqualTo(0.5f).Within(1e-4f)); Assert.That(sound.Calls[0].C, Is.EqualTo(0.2f).Within(1e-4f));
            ctl.Click(e4, MarimbaGeometry.TopPoint(e4, 0.5f, 0f), true);
            Assert.That(sound.Calls[1].Op, Is.EqualTo("damp"));
        }

        [Test]
        public void Controller_RealUnit_ATapIsHeard()
        {
            // the same path into the real sound unit (the offline host): the struck bar rings
            using var unit = new MarimbaOffline();
            var hands = new MarimbaFakeHands();
            var ctl = new MarimbaController(unit.Sound, hands);
            int e4 = MarimbaGeometry.BarOf("E4");
            HandState h = hands.H[0];
            h.Connected = true;
            for (int k = 0; k < 16; k++)
            {
                Vector3 p = MarimbaGeometry.TopCentre(e4) + new Vector3(0, (float)MarimbaIx.FingerR + 0.05f - 0.6f * k / 90f, 0);
                Tip(h, PointerSlot.Index, p);
                ctl.Tick(hands.H, 1 / 90f, 1 / 90f, k * 1000 / 90.0);
            }
            for (int b = 0; b < 8; b++) unit.RenderBlock();
            Assert.That(unit.Sound.BarLevel(e4), Is.GreaterThan(1e-3f), "E4 rings");
            Assert.That(unit.Sound.BarLevel(MarimbaGeometry.BarOf("D4")), Is.EqualTo(0f));
        }

        [Test]
        public void Controller_TickAllocatesNothing()
        {
            var hands = new MarimbaFakeHands();
            var ctl = new MarimbaController(new MarimbaSoundLog(), hands);
            HandState h = hands.H[1];
            h.Connected = true;
            int g4 = MarimbaGeometry.BarOf("G4");
            void Frame(int k)
            {
                Vector3 p = MarimbaGeometry.TopCentre(g4) + new Vector3(0, 0.03f * Mathf.Cos(k * 0.4f), 0);
                Tip(h, PointerSlot.Index, p); Tip(h, PointerSlot.Middle, p + Vector3.right * 0.02f);
                ctl.Tick(hands.H, 1 / 90f, 1 / 90f, k * 1000 / 90.0);
            }
            for (int k = 0; k < 30; k++) Frame(k);
            Assert.That(() => { for (int k = 30; k < 150; k++) Frame(k); }, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------------ lessons, anchors, the pack

        static string PackText => File.ReadAllText(Path.Combine(Application.dataPath, "MusicRoom/Marimba/Resources/LessonPacks/marimba.json"));

        [Test]
        public void LessonPack_ValidatesAndFollowsThePackRules()
        {
            var pack = JsonUtility.FromJson<LessonFile>(PackText);
            Assert.That(pack.lessons.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(pack.lessons[0].id, Is.EqualTo("marimba-first"));
            float minRepeat = float.MaxValue, minGap = float.MaxValue;
            foreach (var l in pack.lessons)
            {
                Assert.That(l.id.StartsWith("marimba-"), Is.True, l.id);
                Assert.That(l.instrument, Is.EqualTo("marimba"));
                Assert.That(l.seconds, Is.LessThan(60f), l.id + " under a minute");
                float last = -1; var lastByTarget = new Dictionary<int, float>();
                foreach (var e in l.events)
                {
                    Assert.That(e.targets.Length, Is.InRange(1, 2), $"{l.id} event {e.k}");
                    if (last >= 0) { Assert.That(e.time - last, Is.GreaterThanOrEqualTo(0.375f - 1e-4f), $"{l.id} event {e.k}: shortest gap"); minGap = Math.Min(minGap, e.time - last); }
                    last = e.time;
                    foreach (var t in e.targets)
                    {
                        Assert.That(MarimbaLessons.Validate(t, out string why), Is.True, $"{l.id}: {why}");
                        // platform 7.4: same-target repeats >= 0.25 s (the spec's "0.6 s" does not hold for the Ode's C4 C4 at
                        // 0.375 s in marimba-first; wait mode freezes time anyway)
                        if (lastByTarget.TryGetValue(t.i, out float tl)) { Assert.That(e.time - tl, Is.GreaterThanOrEqualTo(0.25f), $"{l.id} {t.note}: same-target repeat"); minRepeat = Math.Min(minRepeat, e.time - tl); }
                        lastByTarget[t.i] = e.time;
                    }
                }
            }
            Debug.Log($"[MARIMBA] lesson pack: {pack.lessons.Length} lessons, shortest gap {minGap:F3} s, shortest same-bar repeat {minRepeat:F3} s");
            var first = pack.lessons[0];
            Assert.That(first.seconds, Is.InRange(12f, 21f), "the guaranteed win: 12-21 s of music");
            foreach (var e in first.events) Assert.That(e.targets.Length, Is.EqualTo(1), "one target at a time");
            // the fields the Core schema does not carry yet (options, accompany: request R6)
            var raw = MarimbaRef.Obj(MarimbaJson.Parse(PackText));
            var l0 = MarimbaRef.Obj(MarimbaRef.Arr(raw, "lessons")[0]);
            var opts = MarimbaRef.Arr(l0, "options");
            Assert.That(opts.Contains("Accompany") && opts.Contains("QuietMistakes") && opts.Count == 2, Is.True, "the piano first-run pattern; no invented options (review item 6)");
            Assert.That(MarimbaRef.Arr(l0, "accompany").Count, Is.EqualTo(5));
        }

        [Test]
        public void LessonPack_MarysStickingIsTheReviewsAlternatingOrder()
        {
            var pack = JsonUtility.FromJson<LessonFile>(PackText);
            Lesson mary = Array.Find(pack.lessons, l => l.id == "marimba-mary");
            var seq = new List<string>();
            int L = -1, R = -1, crossings = 0;
            foreach (var e in mary.events)
                foreach (var t in e.targets)
                {
                    seq.Add(t.note + t.label);
                    if (t.label == "L") L = t.i; else R = t.i;
                    if (L >= 0 && R >= 0 && MarimbaGeometry.X(L) > MarimbaGeometry.X(R) + 1e-6f) crossings++;
                }
            Assert.That(string.Join(" ", seq), Is.EqualTo("E4R D4L C4L D4R E4R E4L E4R D4L D4R D4L E4R G4R G4L E4L D4L C4L D4R E4R E4L E4R E4L D4L D4R E4R D4L C4L"),
                "critiques/marimba-unaddressed.md item 8");
            Assert.That(crossings, Is.EqualTo(0));
            Assert.That(mary.events[0].hint, Is.EqualTo("Play E4 (right)"));
        }

        [Test]
        public void LessonTargets_MatchesLandingHintAndTheFirstLessonCompletesWithAFingertip()
        {
            var pack = JsonUtility.FromJson<LessonFile>(PackText);
            var first = pack.lessons[0];
            int done = 0;
            foreach (var e in first.events)
            {
                var t = e.targets[0];
                var live = MarimbaEvents.StrikeEvent(t.i, 0.48f, 0.45f, 0f, MarimbaMallet.Finger);
                if (MarimbaLessons.Matches(t, live)) done++;
                Assert.That(MarimbaLessons.Matches(t, MarimbaEvents.StrikeEvent((t.i + 1) % 37, 0.48f, 0.45f, 0f, MarimbaMallet.Medium)), Is.False);
                Assert.That(MarimbaLessons.Matches(t, MarimbaEvents.DampEvent(t.i, MarimbaDampKind.Hand, 0.5f)), Is.False, "a mute is not a hit");
            }
            Assert.That(done, Is.EqualTo(first.events.Length), "every target of the first lesson is won by a fingertip");
            var g4 = new LessonTarget { i = 19, midi = 67, note = "G4", kind = "roll" };
            Assert.That(MarimbaLessons.Hint(new[] { g4 }), Is.EqualTo("Roll G4"));
            Assert.That(MarimbaLessons.Hint(new[] { new LessonTarget { i = 16, midi = 64, note = "E4" } }), Is.EqualTo("Play E4"));
            Assert.That(MarimbaLessons.TryGetLanding(g4, new Vector3(0, 0, 0.5f), Quaternion.identity, 0.70f, out Pose land, out Vector3 up, out Vector2 size), Is.True);
            Assert.That(Vector3.Distance(land.position, MarimbaGeometry.TopCentre(19) + new Vector3(0, 0, 0.5f)), Is.LessThan(1e-5f));
            Assert.That(up, Is.EqualTo(Vector3.down), "the block travels down onto the bar");
            Assert.That(size.x, Is.EqualTo(MarimbaGeometry.Width(19))); Assert.That(size.y, Is.EqualTo(0.12f));
            Assert.That(MarimbaLessons.Validate(new LessonTarget { i = 19, midi = 66 }, out _), Is.False);
            Assert.That(MarimbaLessons.Validate(new LessonTarget { i = 40, midi = 88 }, out _), Is.False);
        }

        [Test]
        public void Anchors_EveryTipAndLessonAnchorResolves()
        {
            foreach (var a in new[] { "tray.left", "tray.right", "bars.middle", "bar.C4", "bar.E4", "bar.G4", "bar.F#4.tip", "bar.C#3.tip", "bar.C6" })
                Assert.That(MarimbaAnchors.TryGetLocal(a, 0.70f, out _, out _), Is.True, a);
            Assert.That(MarimbaAnchors.TryGetLocal("bar.H9", 0.70f, out _, out _), Is.False);
            MarimbaAnchors.TryGetLocal("bar.F#4.tip", 0.70f, out Vector3 tip, out _);
            int fs4 = MarimbaGeometry.BarOf("F#4");
            Assert.That(tip.z, Is.EqualTo(MarimbaGeometry.NearZ(fs4) + 0.04f * MarimbaGeometry.Length(fs4)).Within(1e-5f), "the nearest tip, u 0.04");
            Assert.That(tip.y, Is.EqualTo(0.722f).Within(1e-5f), "a sharp's top");
            MarimbaAnchors.TryGetLocal("tray.right", 0.70f, out Vector3 tr, out _);
            Assert.That(tr.x, Is.EqualTo(0.11f).Within(1e-4f), "each hand finds its own grip 11 cm from the centre");
        }
    }
}
