// The drum kit in the running app plays the classic sound (today's DrumSynth and drum room; the only drum sound since
// 2026-11-10): TestBoot with scripted hands and manual frames, the real Drums module, Layout, Lessons and audio engine on
// the batchmode audio thread. The module's calls are observed through IAudioEngine.TestTap (AudioTestTap.Calls) and the
// sound itself with a capture filter behind each drum source's SynthSource (its post-bus output, as the spatializer gets
// it). Covers a finger stroke and a stick stroke and a drums lesson played through the module: level, no NaN, no
// discontinuity at any audio block boundary.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Drums;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Audio
{
    /// <summary>Records channel 0 of whatever passes through it (added after SynthSource: the source's output).</summary>
    public sealed class DrumSourceCapture : MonoBehaviour
    {
        public const int Size = 1 << 20;   // 21.8 s at 48 kHz
        public readonly float[] Ring = new float[Size];
        long _written;
        public int BlockFrames;
        public long Written => Interlocked.Read(ref _written);

        void OnAudioFilterRead(float[] data, int channels)
        {
            long w = _written;
            int n = data.Length / channels;
            for (int i = 0; i < n; i++) Ring[(int)((w + i) & (Size - 1))] = data[i * channels];
            BlockFrames = n;
            Interlocked.Exchange(ref _written, w + n);
        }

        public float[] Copy(long from, long to)
        {
            var y = new float[Math.Max(0, (int)(to - from))];
            for (int i = 0; i < y.Length; i++) y[i] = Ring[(int)((from + i) & (Size - 1))];
            return y;
        }
    }

    public class DrumKitPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        DrumsModule _mod;
        DrumKit _kit;
        AudioEngine _engine;
        AudioTestTap _tap;
        readonly Dictionary<SourceId, DrumSourceCapture> _cap = new Dictionary<SourceId, DrumSourceCapture>();

        IEnumerator Boot(bool hands = true)
        {
            _boot = new TestBoot(simulateHeadset: true, manualFrames: hands);
            if (!hands) _boot.Options.ModuleFilter = t => t == typeof(AudioModule);
            yield return _boot.Start();
            _app = _boot.App;
            _engine = (AudioEngine)_app.Get<IAudioEngine>();
            _tap = new AudioTestTap();
            _engine.TestTap = _tap;
            foreach (var id in PieceSources) _cap[id] = _engine.Source(id).gameObject.AddComponent<DrumSourceCapture>();
            if (!hands) yield break;
            foreach (var m in _app.Modules) if (m is DrumsModule d) _mod = d;
            Assert.That(_mod, Is.Not.Null);
            _kit = _mod.Kit;
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State != XrState.Desktop && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            yield return null;
            var layout = _app.Get<ILayoutService>();
            for (int i = 0; i < 10 && !layout.FirstArrangeDone; i++) _boot.Step(Dt);
            Assert.IsTrue(layout.FirstArrangeDone, "the room is arranged");
            _boot.Step(Dt);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var c in _cap.Values) if (c != null) UnityEngine.Object.Destroy(c);
            _cap.Clear();
            if (_engine != null) _engine.TestTap = null;
            _engine = null;
            if (_boot != null) yield return _boot.Stop();
            _boot = null; _app = null; _mod = null; _kit = null;
        }

        static readonly SourceId[] PieceSources =
            { SourceId.Kick, SourceId.Snare, SourceId.Hat, SourceId.Tom1, SourceId.Tom2, SourceId.Floor, SourceId.Crash, SourceId.Ride };
        HandState Right => _app.Input.Hand(Handedness.Right);
        void Step(int n = 1) => _boot.Step(Dt, n);
        void Pose(in HandPose p) => _boot.Hands.SetPose(HandSide.Right, p);

        static HandPose PoseAt(Vector3 indexTip, Quaternion rot, bool pinched)
        {
            var p = HandPose.Open(HandSide.Right, Vector3.zero, rot).WithPointing();
            if (pinched) p = p.WithPinch(0f);
            return p.WithIndexTipAt(indexTip);
        }

        static Vector3 StickTipFor(in HandPose p)
        {
            DrumMath.StickGrip(p.Position[(int)HandJoint.Wrist], p.Rotation[(int)HandJoint.Wrist], p.Position[(int)HandJoint.ThumbTip],
                p.Position[(int)HandJoint.IndexTip], out Vector3 grip, out Vector3 axis);
            DrumMath.StickPose(grip, axis, out _, out _, out Vector3 tip, out _);
            return tip;
        }

        static HandPose WithStickTipAt(in HandPose p, Vector3 tip) => p.Translated(tip - StickTipFor(p));

        static IEnumerator Until(Func<bool> done, float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < timeout) yield return null;
        }

        /// <summary>Waits (real time) until the source has captured at least frames more.</summary>
        IEnumerator Captured(SourceId id, long from, int frames) { yield return Until(() => _cap[id].Written >= from + frames, 10f); }

        List<AudioTestTap.Call> DrumCalls()
        {
            var l = new List<AudioTestTap.Call>();
            foreach (var c in _tap.Calls) if (c.Method == AudioCall.Drum) l.Add(c);
            return l;
        }

        /// <summary>Level, finite, and no discontinuity at the audio block boundaries: the step into each block is no larger
        /// than twice the largest step in the 32 samples around it.</summary>
        static void AssertClean(float[] x, int block, string what, float minPeak)
        {
            Assert.That(x.Length, Is.GreaterThan(block * 4), what + ": captured");
            float peak = 0;
            foreach (float v in x) { Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), what + ": finite"); peak = Math.Max(peak, Math.Abs(v)); }
            Assert.That(peak, Is.GreaterThan(minPeak), what + ": heard");
            Assert.That(peak, Is.LessThan(16f), what + ": sane level");
            double worst = 0; int at = -1;
            for (int b = block; b + 33 < x.Length; b += block)
            {
                float edge = Math.Abs(x[b] - x[b - 1]), around = 0;
                for (int i = b - 32; i < b + 33; i++) if (i != b && i > 0) around = Math.Max(around, Math.Abs(x[i] - x[i - 1]));
                double r = edge / (around + 1e-5);
                if (r > worst) { worst = r; at = b; }
            }
            Debug.Log($"[DRUMS-PLAY] {what}: peak {peak:F3}, worst block-edge step ratio {worst:F2} (block {block})");
            Assert.That(worst, Is.LessThan(2.0), $"{what}: no discontinuity at a block boundary (sample {at})");
        }

        [UnityTest]
        public IEnumerator AFingerAndAStickStrokePlayTodaysKitThroughTheModule()
        {
            yield return Boot();
            Assert.IsNull(_engine.Unit(UnitId.Drums), "the app creates no drum unit");
            Assert.IsNull(_engine.ActiveUnit(UnitId.Drums), "today's kit plays the drums");
            // a finger stroke on the snare
            DrumPiece snare = _kit.Piece(DrumSpec.SnareId);
            Quaternion down = Quaternion.LookRotation(Vector3.down, _kit.transform.forward);
            foreach (float y in new[] { 0.06f, 0.04f, 0.02f }) { Pose(PoseAt(snare.Group.TransformPoint(new Vector3(0.03f, y, 0f)), down, false)); Step(); }
            long c0 = _cap[SourceId.Snare].Written;
            _tap.Clear();
            Pose(PoseAt(snare.Group.TransformPoint(new Vector3(0.03f, -0.01f, 0f)), down, false));
            Step();
            var calls = DrumCalls();
            Assert.That(calls.Count, Is.EqualTo(1), "one Drum call");
            Assert.That(calls[0].Drum, Is.EqualTo(DrumKind.Snare));
            Assert.That(calls[0].Strike.Implement, Is.EqualTo(DrumSpec.FingertipImplement), "a fingertip's implement");
            Assert.That(calls[0].Strike.Zone, Is.EqualTo(DrumZone.Head));
            Assert.That(_tap.Commands.FindAll(c => c.Source == SourceId.Snare).Count, Is.GreaterThan(0), "today's snare voices");
            yield return Captured(SourceId.Snare, c0, 48000 / 2);
            AssertClean(_cap[SourceId.Snare].Copy(c0, _cap[SourceId.Snare].Written), _cap[SourceId.Snare].BlockFrames, "finger on the snare", 1e-3f);

            // a stick: pick it up (the real Layout's grab pass), strike tom 1 with its tip
            Quaternion rot = Quaternion.LookRotation(_kit.transform.TransformDirection(new Vector3(0f, -0.6f, 1f).normalized), Vector3.up);
            Drumstick s = _mod.Sticks[1];
            Vector3 at = s.GripWorld + Vector3.up * 0.02f;
            Pose(PoseAt(at, rot, false)); Step(2);
            Pose(PoseAt(at, rot, true)); Step();
            Assert.That(Right.Holding, Is.SameAs(s), "the pinch picked up a stick");
            DrumPiece tom = _kit.Piece(DrumSpec.Tom1Id);
            HandPose hold = PoseAt(Vector3.zero, rot, true);
            foreach (float y in new[] { 0.08f, 0.06f, 0.035f, 0.01f }) { Pose(WithStickTipAt(hold, tom.Group.TransformPoint(new Vector3(0.02f, y, 0.02f)))); Step(); }
            long t0 = _cap[SourceId.Tom1].Written;
            _tap.Clear();
            Pose(WithStickTipAt(hold, tom.Group.TransformPoint(new Vector3(0.02f, -0.015f, 0.02f))));
            Step();
            calls = DrumCalls();
            Assert.That(calls.Count, Is.EqualTo(1), "the stick tip struck tom 1");
            Assert.That(calls[0].Drum, Is.EqualTo(DrumKind.Tom1));
            Assert.That(calls[0].Strike.Implement, Is.EqualTo(DrumImplement.Tip), "a stick tip");
            yield return Captured(SourceId.Tom1, t0, 48000 / 2);
            AssertClean(_cap[SourceId.Tom1].Copy(t0, _cap[SourceId.Tom1].Written), _cap[SourceId.Tom1].BlockFrames, "stick on tom 1", 1e-3f);
        }

        [UnityTest]
        public IEnumerator ADrumsLessonPlayedThroughTheModuleSoundsOnTodaysKit()
        {
            yield return Boot();
            LessonsModule lessons = null;
            foreach (var m in _app.Modules) if (m is LessonsModule l) lessons = l;
            Assert.IsNotNull(lessons);
            Tutorial tut = lessons.Tutorial;
            tut.Start("drums-rock");
            Assert.IsTrue(tut.Running, "the drums-rock lesson runs");
            var drums = _app.Get<IDrumsApi>();
            long k0 = _cap[SourceId.Kick].Written, n0 = _cap[SourceId.Snare].Written, h0 = _cap[SourceId.Hat].Written;
            int hits = 0;
            _tap.Clear();
            for (int frame = 0; frame < 72 * 30 && tut.Running && tut.EventsDone < 12; frame++)
            {
                Step();
                if (tut.State != TutorialState.Waiting) continue;
                Lesson lesson = tut.Lesson;
                LessonEvent ev = lesson.events[tut.CurrentEvent];
                int before = tut.CurrentEvent;
                foreach (var target in ev.targets)
                {
                    var info = target.kind == "hat" ? new DrumStrikeInfo { Pos = 0.55f, Zone = "top" } : DrumStrikeInfo.Default;
                    drums.PlaybackHit(target.kind, 0.75f, info);
                    hits++;
                }
                Step();
                Assert.That(tut.CurrentEvent, Is.GreaterThan(before), $"event {before} of the lesson was played");
                yield return new WaitForSecondsRealtime(0.08f);   // let the audio thread render the strokes as a player would space them
            }
            Assert.That(tut.EventsDone, Is.GreaterThanOrEqualTo(12), "the lesson advanced through its first bars");
            Assert.That(DrumCalls().Count, Is.EqualTo(hits), "one Drum call per lesson target");
            yield return new WaitForSecondsRealtime(0.3f);
            AssertClean(_cap[SourceId.Kick].Copy(k0, _cap[SourceId.Kick].Written), _cap[SourceId.Kick].BlockFrames, "lesson kick", 1e-3f);
            AssertClean(_cap[SourceId.Snare].Copy(n0, _cap[SourceId.Snare].Written), _cap[SourceId.Snare].BlockFrames, "lesson snare", 1e-3f);
            AssertClean(_cap[SourceId.Hat].Copy(h0, _cap[SourceId.Hat].Written), _cap[SourceId.Hat].BlockFrames, "lesson hat", 1e-3f);
            tut.Stop();
        }
    }
}
