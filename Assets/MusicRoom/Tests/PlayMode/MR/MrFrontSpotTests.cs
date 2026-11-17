// The front spot in mixed reality (2026-11-17, "make it appear at one place in front of eyes only"), with the whole app in
// one of MRUK's recorded rooms: after "Play in my room" laid the instruments out in front of the player, every instrument
// chosen (the player looking at the menu or the deck and leaning toward it at that moment), brought by the deck guide's
// Set, or brought alone by a one-instrument playback lands with its play centre within 2 cm of the player's forward line
// (the seat the room was laid out about), facing the player, every time at the same distance; and a recentre at a new
// heading moves that line with it.
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.MR
{
    public class MrFrontSpotTests : MrTestRig
    {
        protected override bool UseContractLayout => false;
        protected override bool TableOnEnter => false;
        protected override void Prepare(MemorySettingsStore settings)
        {
            settings.SetInt(AppSettings.KeyTourDone, 1);
            settings.SetString(AppSettings.KeyLayoutMode, "single");
            settings.SetString(AppSettings.KeyCurrentInstrument, "drums");
        }

        const float MaxLateral = 0.02f, MaxDrift = 0.01f;
        LayoutModule _layout;
        Vector3 _seatHead;
        float _seatYaw;
        int _looks;
        readonly Dictionary<InstrumentKind, float> _ahead = new Dictionary<InstrumentKind, float>();

        static readonly Vector3[] Leans = { new Vector3(0.18f, -0.05f, 0.08f), new Vector3(-0.22f, -0.12f, 0.02f), new Vector3(0.06f, -0.02f, 0.15f) };
        static readonly float[] Looks = { 65f, -105f, 25f };

        void LookAway()
        {
            int k = _looks++ % Looks.Length;
            App.Head.SetPositionAndRotation(_seatHead + Quaternion.Euler(0f, _seatYaw, 0f) * Leans[k], Quaternion.Euler(25f, _seatYaw + Looks[k], 0f));
        }

        void LookAhead() => App.Head.SetPositionAndRotation(_seatHead, Quaternion.Euler(0f, _seatYaw, 0f));

        IEnumerator Landed()
        {
            yield return null;
            LookAhead();
            yield return WaitFor(() => !_layout.AnyMoving && !_layout.Settling && !_layout.IsTurning, 5f);
            yield return null;
        }

        List<InstrumentKind> Kinds()
        {
            var list = new List<InstrumentKind>();
            IInstrumentRegistry reg = App.Registry;
            for (int i = 0; i < reg.Carousel.Count; i++)
            {
                InstrumentKind k = reg.Carousel[i];
                InstrumentDescriptor d = reg.Get(k);
                if (d != null && d.Playable && reg.IsLoaded(k) && Rig(InstrumentIds.RigOf(k))?.Group != null) list.Add(k);
            }
            Assert.That(list.Count, Is.GreaterThanOrEqualTo(3));
            return list;
        }

        void AssertFront(InstrumentKind k, string how)
        {
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.Single), how);
            Assert.That(_layout.Current, Is.EqualTo(k), how);
            Rig r = Rig(InstrumentIds.RigOf(k));
            Assert.IsTrue(r.Group.gameObject.activeInHierarchy, how + ": shown");
            RigEntry e = _layout.Placement.Entry(r);
            Assert.IsTrue(e != null && e.HasCentre, k + " has a play centre");
            _layout.Placement.Pose(e);
            Vector3 d = PlacementSolver.CentreWorld(e) - _seatHead;
            d.y = 0f;
            Vector3 s = Quaternion.Euler(0f, -_seatYaw, 0f) * d;
            float turn = Mathf.DeltaAngle(r.Group.eulerAngles.y, _seatYaw + r.HomeYawSingle);
            Debug.Log($"[FrontSpot] mixed reality, seat {_seatYaw:F0} deg, {how}: {k} play centre {s.x * 100f:+0.0;-0.0} cm to the side, {s.z:F3} m ahead, turned {turn:+0.0;-0.0} deg");
            Assert.That(Mathf.Abs(s.x), Is.LessThan(MaxLateral), $"{how}: {k}'s play centre {s.x * 100f:F1} cm off the player's forward line");
            Assert.That(Mathf.Abs(turn), Is.LessThan(1f), $"{how}: {k} faces the player's front");
            Assert.That(s.z, Is.GreaterThan(0.05f), how + ": in front of the eyes");
            if (_ahead.TryGetValue(k, out float a)) Assert.That(s.z, Is.EqualTo(a).Within(MaxDrift), $"{how}: {k} as far ahead as before");
            else _ahead[k] = s.z;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator InMixedRealityEveryInstrumentLandsOnTheForwardLine()
        {
            _layout = App.Get<ILayoutService>() as LayoutModule;
            Assert.That(_layout, Is.Not.Null, "the build's Layout module");
            SeatFrame before = SeatFrame.From(App.Head);
            yield return EnterRoom("MeshBedroom3");
            yield return WaitFor(() => !_layout.AnyMoving && !_layout.Settling, 5f);
            Assert.IsTrue(Mr.Active);
            // The seat "Play in my room" laid the room out about (the head as it entered).
            _seatHead = before.Head;
            _seatYaw = before.YawDeg;
            LookAhead();
            yield return null;
            AssertFront(_layout.Current, "entering");
            List<InstrumentKind> kinds = Kinds();
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    // The system recentred with the player's front 40 degrees to the right.
                    Transform rig = App.Rig.RigRoot;
                    Quaternion rot = Quaternion.Euler(0f, 40f, 0f);
                    Vector3 head = _seatHead;
                    rig.rotation = rot;
                    rig.position = head - rot * MusicRoom.Hands.DesktopRigFallback.EyePosition;
                    App.Head.SetPositionAndRotation(head, rot);
                    yield return null;
                    App.Head.SetPositionAndRotation(head, rot);
                    int backs = _layout.BringBacks;
                    App.Events.Publish(new RecenteredEvent());
                    Assert.That(_layout.BringBacks, Is.EqualTo(backs + 1));
                    _seatHead = head;
                    _seatYaw = 40f;
                    _ahead.Clear();
                    yield return Landed();
                    AssertFront(_layout.Current, "after a recentre");
                }
                for (int i = 0; i < kinds.Count; i++)
                {
                    InstrumentKind k = kinds[(i + pass) % kinds.Count];
                    if (_layout.Current == k) continue;
                    LookAway();
                    if (i % 2 == 0) Assert.IsTrue(_layout.Bring(k, BringReason.Menu), k + " brought");
                    else _layout.Set(LayoutMode.Single, k);   // the deck guide's way
                    yield return Landed();
                    AssertFront(k, i % 2 == 0 ? "chosen" : "brought by the deck guide");
                }
                // A playback of one instrument brings it alone, in the same spot.
                InstrumentKind own = _layout.Current;
                InstrumentKind take = kinds[(kinds.IndexOf(own) + 1) % kinds.Count];
                LookAway();
                App.Events.Publish(new RecordingPlaybackEvent { Started = true, Kinds = 1u << (int)take });
                yield return Landed();
                Assert.IsTrue(_layout.PlaybackStaged);
                AssertFront(take, "a playback of " + take);
                App.Events.Publish(new RecordingPlaybackEvent { Started = false, Kinds = 1u << (int)take });
                float t0 = Time.realtimeSinceStartup;
                while (_layout.PlaybackStaged && Time.realtimeSinceStartup - t0 < 6f) { LookAhead(); yield return null; }
                yield return Landed();
                AssertFront(own, "back after the playback");
            }
        }
    }
}
#endif
