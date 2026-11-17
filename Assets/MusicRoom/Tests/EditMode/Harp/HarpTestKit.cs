// Shared scaffolding for the harp's EditMode tests: a recording IHarpSound with a scripted audio clock, the real
// Lessons Recorder on an EventBus (so recorder events are the ones the app publishes), and synthetic fingertips moved
// the way the reference checks move them (prev -> tip, sampleDt, speed = |tip - prev| / dt, one update per move).
// The harp sits at the origin, so world = harp-local Unity. Reference values are right-handed: L3 negates z.
using System;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Harp;
using MusicRoom.Lessons;
using UnityEngine;

namespace MusicRoom.Tests.Harp
{
    public sealed class FakeHarpSound : IHarpSound
    {
        public struct PluckCall
        {
            public int String; public float Vel; public double When; public PluckOptions Options; public int Id;
            /// <summary>The audio clock at the call; when = 0 (ASAP) sounds then.</summary>
            public double At;
            /// <summary>When it sounds: the scheduled time, or the call time for an ASAP pluck.</summary>
            public double Sounds => When > 0 ? When : At;
        }

        public double Now { get; set; } = 100.0;
        public double Block = 256.0 / 48000.0;
        public double PluckBase => Now + Block;
        public readonly List<PluckCall> Plucks = new List<PluckCall>();
        public readonly List<int> Releases = new List<int>();
        public readonly List<int> DampIds = new List<int>();
        public readonly List<int> DampCounts = new List<int>();
        int _next = 1;

        public double RingSeconds(int midi) => Math.Min(4.5, Math.Max(0.7, 4.5 - (midi - 48) * 0.08));

        public VoiceHandle Pluck(int stringIndex, float vel, double when, in PluckOptions options)
        {
            int id = _next++;
            Plucks.Add(new PluckCall { String = stringIndex, Vel = vel, When = when, Options = options, Id = id, At = Now });
            return new VoiceHandle(null, id);
        }

        public void Release(in VoiceHandle voice) => Releases.Add(voice.Id);

        public void Damp(in VoiceHandle voice, int strings)
        {
            DampIds.Add(voice.Id);
            DampCounts.Add(strings);
        }

        public void Clear()
        {
            Plucks.Clear();
            Releases.Clear();
            DampIds.Clear();
            DampCounts.Clear();
        }
    }

    /// <summary>One harp at the origin with a fake sound, a real recorder and a right hand's fingertips.</summary>
    public sealed class HarpRig
    {
        public const float Dt = 1f / 72f;
        public readonly HarpPlay Play = new HarpPlay();
        public readonly FakeHarpSound Sound = new FakeHarpSound();
        public readonly EventBus Bus = new EventBus();
        public readonly Recorder Recorder;
        public readonly List<TakeEvent> Events = new List<TakeEvent>();
        public readonly HandState Hand = new HandState(Handedness.Right);
        public readonly HandState Other = new HandState(Handedness.Left);
        readonly List<Pointer> _one = new List<Pointer>(1);

        public HarpRig()
        {
            Recorder = new Recorder(() => Sound.Now, Bus);
            Bus.Subscribe<RecorderEvent>(e => Events.Add(e.Event));
            Play.Sound = Sound;
            Play.Recorder = Recorder;
            Play.NowMs = 10000;
        }

        public Pointer Finger => Hand.Pointers[0];

        /// <summary>Advance both clocks by one frame.</summary>
        public void Step(float dt = Dt)
        {
            Play.NowMs += dt * 1000.0;
            Sound.Now += dt;
        }

        /// <summary>The check's move(p, a, b, dt): one sample from a to b, then one harp update (clocks advance first).</summary>
        public void Move(Pointer p, Vector3 a, Vector3 b, float dt = Dt)
        {
            Step(dt);
            p.Active = p.WasActive = true;
            p.Prev = a;
            p.Tip = b;
            p.SampleDt = dt;
            p.Vel = (b - a) / dt;
            p.Speed = Vector3.Distance(a, b) / dt;
            _one.Clear();
            _one.Add(p);
            Play.Update(_one, null, Matrix4x4.identity, dt);
        }

        /// <summary>Several still or moving pointers in one update.</summary>
        public void Update(List<Pointer> pointers, float dt = Dt)
        {
            Step(dt);
            Play.Update(pointers, null, Matrix4x4.identity, dt);
        }

        /// <summary>The check's sweep(p, pts, z, stepLen, dt): along the polyline at depth z (Unity), in steps of at most stepLen.</summary>
        public void Sweep(Pointer p, Vector3[] pts, float z, float stepLen, float dt = Dt)
        {
            Vector3 prev = pts[0]; prev.z = z;
            for (int k = 1; k < pts.Length; k++)
            {
                Vector3 a = pts[k - 1], b = pts[k];
                int n = Math.Max(1, (int)Math.Ceiling(Vector3.Distance(a, b) / stepLen));
                for (int j = 1; j <= n; j++)
                {
                    Vector3 c = Vector3.Lerp(a, b, (float)j / n); c.z = z;
                    Move(p, prev, c, dt);
                    prev = c;
                }
            }
        }

        public void UpdateEmpty(float dt = Dt)
        {
            Step(dt);
            Play.Update(null, null, Matrix4x4.identity, dt);
        }

        public static Vector3 L3(float x, float y, float zRh) => new Vector3(x, y, -zRh);

        /// <summary>The in-plane unit normal of string i, toward where Side().dist is positive (the pillar side).</summary>
        public static Vector3 Normal(int i)
        {
            StringLayout s = HarpGeometry.Strings[i];
            return new Vector3(-(s.B.y - s.A.y), s.B.x - s.A.x, 0f).normalized;
        }

        /// <summary>A point at height y on string i's line, `dist` toward the pillar, at depth z (Unity).</summary>
        public static Vector3 OnString(int i, float y, float dist = 0f, float z = 0f)
        {
            StringLayout s = HarpGeometry.Strings[i];
            float u = (y - s.A.y) / (s.B.y - s.A.y);
            Vector3 p = Vector3.LerpUnclamped(s.A, s.B, u) + Normal(i) * dist;
            p.z = z;
            return p;
        }

        public static Vector3 Still(Pointer p, Vector3 at)
        {
            p.Active = p.WasActive = true;
            p.Prev = p.Tip = at;
            p.Vel = Vector3.zero;
            p.Speed = 0f;
            p.SampleDt = Dt;
            return at;
        }

        public int Count(string type)
        {
            int n = 0;
            foreach (var e in Events) if (e.type == type) n++;
            return n;
        }
    }
}
