// EditMode fixtures for the drums: a scripted host (clock, audio, recorder, hand frames), recording fakes of
// IAudioEngine and IRecorder, and a kit built geometry-only at the world origin (kit-local == world), so the tests
// drive DrumKit.Tick with hand-made pointers and exact clocks and check every number against the reference formulas.
using System;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Drums;
using MusicRoom.Hands;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.Drums
{
    public sealed class FakeAudio : IAudioEngine
    {
        public struct DrumCall { public DrumKind Kind; public float Vel; public DrumStrike Strike; }
        public readonly List<DrumCall> Drums = new List<DrumCall>();
        public readonly List<DrumKind> Chokes = new List<DrumKind>();
        public void Clear() { Drums.Clear(); Chokes.Clear(); }

        public void Drum(DrumKind kind, float vel, DrumStrike strike, double when = 0) => Drums.Add(new DrumCall { Kind = kind, Vel = vel, Strike = strike });
        public void Choke(DrumKind kind) => Chokes.Add(kind);

        public bool IsReady => true;
        public int SampleRate => 48000;
        public double Now => 0;
        public double FrameNow => 0;
        public float RenderProgress => 1f;
        public VoiceHandle Piano(int midi, float vel, double when = 0) => VoiceHandle.None;
        public void Release(VoiceHandle h, bool thud = true, double when = 0) { }
        public VoiceHandle Pluck(int stringIndex, float vel, double when = 0, PluckOptions? options = null) => VoiceHandle.None;
        public void Damp(VoiceHandle h, int hands = 1, double when = 0) { }
        public double HarpRingSeconds(int midi) => 1;
        public void SetFire(bool on) { }
        public void SetWind(bool on) { }
        public void SetSnow(bool on) { }
        public bool FireOn => false;
        public bool WindOn => false;
        public bool SnowOn => false;
        public void SnowGust(float strength, float attack, float hold, float release) { }
        public SynthSource Source(SourceId id) => null;
        public void SetSourceParent(SourceId id, Transform parent, Vector3 localPosition) { }
        public void SetSourcePosition(SourceId id, Vector3 world) { }
        public void SetMixedReality(bool mixedReality) { }
        public void SetRoomBox(Vector3 center, Vector3 size, bool realRoom) { }
        public void Chirp(ChirpKind kind, double when = 0) { }
        public bool HasVoiceLine(string id) => false;
        public float VoiceLineSeconds(string id) => -1;
        public double PlayVoiceLine(string id, double when = 0) => 0;
        public void StopVoiceLine() { }
        public bool VoiceLinePlaying => false;
        public float VoiceLevel => 0;
        public void Swish(double when = 0) { }
        public AudioTestTap TestTap { get; set; }
        public void SeedAll(uint seed) { }
    }

    public sealed class FakeRecorder : IRecorder
    {
        public readonly List<TakeEvent> Events = new List<TakeEvent>();
        public void Add(in TakeEvent ev) => Events.Add(ev);
        public RecorderState State => RecorderState.Idle;
        public bool External => false;
        public void Record() { }
        public bool Play() => false;
        public bool PlayEvents(List<TakeEvent> events, double seconds) => false;
        public void Stop() { }
        public List<TakeEvent> Take => null;
        public double TakeSeconds => 0;
#pragma warning disable 67 // never raised by the fake
        public event Action<List<TakeEvent>, double> OnTake;
#pragma warning restore 67
    }

    public sealed class FakeDrumHost : IDrumHost, IHandFrames
    {
        public double NowMs { get; set; } = 10000;
        public readonly FakeAudio AudioFake = new FakeAudio();
        public readonly FakeRecorder RecorderFake = new FakeRecorder();
        public readonly List<Pointer> Hands = new List<Pointer>();
        public readonly Dictionary<HandState, HandFrame> Frames = new Dictionary<HandState, HandFrame>();
        public IAudioEngine Audio => AudioFake;
        public IRecorder Recorder => RecorderFake;
        public IReadOnlyList<Pointer> HandPointers => Hands;
        public bool DesktopClicks => false;
        public Camera DesktopCamera => null;
        public HandFrame Frame(HandState hand) => hand != null && Frames.TryGetValue(hand, out var f) ? f : null;
    }

    /// <summary>A geometry-only kit at the origin with its sticks; DestroyImmediate in Dispose.</summary>
    public sealed class KitFixture : IDisposable
    {
        public readonly FakeDrumHost Host = new FakeDrumHost();
        public readonly GameObject Root;
        public readonly DrumKit Kit;
        public readonly DrumSticks Sticks;
        public readonly List<Pointer> Pointers = new List<Pointer>();
        public readonly HandState Right = new HandState(Handedness.Right);
        public readonly HandState Left = new HandState(Handedness.Left);
        public readonly Transform Scene;

        public KitFixture()
        {
            Root = new GameObject("Drums (test)");
            Kit = Root.AddComponent<DrumKit>();
            Kit.Build(Host, null);
            Scene = new GameObject("Scene (test)").transform;
            Sticks = new DrumSticks(Kit, Host, Scene, Kit.Materials);
            Kit.AttachSticks(Sticks);
            Pointers.AddRange(Right.Pointers);
            Pointers.AddRange(Left.Pointers);
            for (int i = 0; i < Sticks.Count; i++) { Pointers.Add(Sticks[i].TipPointer); Pointers.Add(Sticks[i].ShoulderPointer); }
            Host.Hands.AddRange(Right.Pointers);
            Host.Hands.AddRange(Left.Pointers);
        }

        public DrumPiece Piece(int id) => Kit.Piece(id);

        /// <summary>World point from a piece-local point (the kit sits at the origin, so this is also kit-local).</summary>
        public Vector3 At(int pieceId, float x, float y, float z) => Piece(pieceId).Group.TransformPoint(new Vector3(x, y, z));

        /// <summary>Puts a pointer on a stroke prev -> tip this frame (active both frames, SampleDt = dt).</summary>
        public static void Stroke(Pointer p, Vector3 prev, Vector3 tip, float sampleDt)
        {
            p.WasActive = true;
            p.Active = true;
            p.Prev = prev;
            p.Tip = tip;
            p.SampleDt = sampleDt;
            p.Vel = (tip - prev) / Mathf.Max(sampleDt, 1e-3f);
            p.Speed = p.Vel.magnitude;
        }

        /// <summary>A pointer resting still at a point (active both frames, no motion).</summary>
        public static void Rest(Pointer p, Vector3 at)
        {
            p.WasActive = true;
            p.Active = true;
            p.Prev = p.Tip = at;
            p.Vel = Vector3.zero;
            p.Speed = 0f;
        }

        public static void Idle(Pointer p) { p.Active = p.WasActive = false; }

        public void IdleAll() { foreach (var p in Pointers) Idle(p); }

        /// <summary>One kit frame: advance the clock, tick and hover.</summary>
        public void Tick(float dt, HoverGlowPool glow = null)
        {
            Host.NowMs += dt * 1000.0;
            Kit.Tick(Pointers, dt);
            if (glow != null) Kit.Hover(Pointers, glow);
        }

        /// <summary>A hand frame whose joints the stick tests set (tracked, rest pose at the wrist).</summary>
        public HandFrame TrackHand(HandState h, Vector3 wrist, Quaternion rot, float pinchGap = -1f)
        {
            var side = h.Side == Handedness.Left ? HandSide.Left : HandSide.Right;
            var pose = HandPose.Open(side, wrist, rot).WithPointing();
            if (pinchGap >= 0f) pose = pose.WithPinch(pinchGap);
            if (!Host.Frames.TryGetValue(h, out var f)) Host.Frames[h] = f = new HandFrame(side);
            for (int i = 0; i < HandFrame.JointCount; i++) { f.position[i] = pose.Position[i]; f.rotation[i] = pose.Rotation[i]; }
            f.isTracked = true;
            h.Connected = true;
            return f;
        }

        /// <summary>A tracked hand posed so its index tip lands on <paramref name="indexTip"/> (pinched: thumb on it).</summary>
        public HandFrame TrackHandAt(HandState h, Vector3 indexTip, Quaternion rot, bool pinched)
        {
            var side = h.Side == Handedness.Left ? HandSide.Left : HandSide.Right;
            var pose = HandPose.Open(side, Vector3.zero, rot).WithPointing();
            if (pinched) pose = pose.WithPinch(0f);
            pose = pose.WithIndexTipAt(indexTip);
            if (!Host.Frames.TryGetValue(h, out var f)) Host.Frames[h] = f = new HandFrame(side);
            for (int i = 0; i < HandFrame.JointCount; i++) { f.position[i] = pose.Position[i]; f.rotation[i] = pose.Rotation[i]; }
            f.isTracked = true;
            h.Connected = true;
            return f;
        }

        public void LoseHand(HandState h)
        {
            if (Host.Frames.TryGetValue(h, out var f)) f.isTracked = false;
            h.Connected = false;
        }

        public void Dispose()
        {
            Kit.DestroyGeneratedAssets(true);
            Object.DestroyImmediate(Root);
            Object.DestroyImmediate(Scene.gameObject);
        }
    }
}
