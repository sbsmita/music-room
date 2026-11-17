// The piano's feel, from the piano's reference check, on PianoAction with
// synthetic fingertips: where a press may start, the escapement, velocity, release timing, chords, glissando, hover; plus
// the automatic sustain (PHRASE_GAP, MAX_RING, one damper thud per lift), re-strikes, the press API, playback and the
// desktop paths (spec 05 sections 2.3-2.6). Coordinates are piano-local Unity (x, y, back): right-handed (x, y, z) -> (x, y, -z).
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Piano;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Piano
{
    public class PianoActionTests
    {
        sealed class Sink : IPianoSink
        {
            public readonly List<(int midi, float vel)> On = new List<(int, float)>();
            public readonly List<(int midi, bool thud)> Off = new List<(int, bool)>();
            public readonly List<TakeEvent> Events = new List<TakeEvent>();
            public VoiceHandle NoteOn(int midi, float vel) { On.Add((midi, vel)); return default; }
            public void NoteOff(int midi, VoiceHandle voice, bool thud) => Off.Add((midi, thud));
            public void Record(in TakeEvent ev) => Events.Add(ev);
            public int Count(int midi) { int n = 0; foreach (var o in On) if (o.midi == midi) n++; return n; }
            public void Clear() { On.Clear(); Off.Clear(); Events.Clear(); }
        }

        Clock _clock;
        Sink _sink;
        PianoAction P;
        HandState _hand;
        int _ids;
        readonly List<Pointer> _one = new List<Pointer>(1);
        static readonly List<Pointer> None = new List<Pointer>();
        System.Random _rng;

        [SetUp]
        public void SetUp()
        {
            _clock = new Clock();
            _clock.Step(0.01f);
            _sink = new Sink();
            P = new PianoAction(_clock, _sink);
            _hand = new HandState(Handedness.Right);
            _rng = new System.Random(1);
        }

        PianoKey K(int midi) => P.KeyByMidi(midi);

        // ---- the check's helpers: mk, step, put, path, lift, settle ----
        Pointer Mk(float sdt = 1f / 90f) => new Pointer("pc" + (++_ids), _hand, 10) { Active = true, WasActive = true, SampleDt = sdt };

        void Update(List<Pointer> pts, float dt)
        {
            _clock.Step(dt);
            P.Tick(pts, dt, Matrix4x4.identity);
        }

        void Update(Pointer p, float dt) { _one.Clear(); _one.Add(p); Update(_one, dt); }

        void Step(Pointer p, float x, float y, float back, float dt = 0f)
        {
            if (dt <= 0f) dt = p.SampleDt;
            p.Prev = p.Tip;
            p.Tip = new Vector3(x, y, back);
            p.Speed = (p.Tip - p.Prev).magnitude / dt;
            Update(p, dt);
        }

        static void Put(Pointer p, float x, float y, float back) { p.Tip = p.Prev = new Vector3(x, y, back); }

        void Path(Pointer p, Vector3 a, Vector3 b, int n, float dt)
        {
            Put(p, a.x, a.y, a.z);
            Update(p, dt);
            for (int i = 1; i <= n; i++) { Vector3 q = Vector3.Lerp(a, b, (float)i / n); Step(p, q.x, q.y, q.z, dt); }
        }

        void Lift(Pointer p, float dt = 1f / 90f)
        {
            Step(p, p.Tip.x, 0.06f, p.Tip.z, dt);
            p.Active = false;
            Update(p, dt);
        }

        void Settle()
        {
            P.PlaybackReleaseAll();
            for (int i = 0; i < 40; i++) Update(None, 0.01f);
            P.Damp();
        }

        bool Still() { foreach (var k in P.Keys) if (k.Depth != 0f) return false; return true; }

        float Noise(float amount) => amount > 0f ? ((float)_rng.NextDouble() - 0.5f) * amount : 0f;

        // ---- 1. presses that do not start on a key top are silent and move nothing ----

        bool TryPath(Vector3 a, Vector3 b, int n = 12, System.Action<Pointer> extra = null)
        {
            _sink.Clear();
            var p = Mk();
            Path(p, a, b, n, 1f / 90f);
            extra?.Invoke(p);
            bool moved = false;
            foreach (var k in P.Keys) if (k.Depth > 0f) moved = true;
            int notes = _sink.On.Count;
            Lift(p);
            Settle();
            return notes == 0 && !moved;
        }

        [Test]
        public void PressesThatDoNotStartOnAKeyTopAreSilent()
        {
            var e4 = K(64); var cs4 = K(61);
            float W = PianoSpec.W, WW = PianoSpec.WW, BW = PianoSpec.BW, L = PianoSpec.L, BL = PianoSpec.BL, BTOP = PianoSpec.BTOP, x0 = PianoSpec.X0;
            Settle();
            Assert.IsTrue(TryPath(new Vector3(e4.X, -0.008f, -0.04f), new Vector3(e4.X, -0.008f, 0.04f), 6), "front face");
            Assert.IsTrue(TryPath(new Vector3(e4.X, 0.03f, -0.004f), new Vector3(e4.X, -0.009f, -0.004f), 8, p => { for (int i = 1; i <= 6; i++) Step(p, e4.X, -0.009f, -0.004f + i * 0.008f); }), "front then back");
            Assert.IsTrue(TryPath(new Vector3(e4.X, -0.05f, 0.05f), new Vector3(e4.X, -0.004f, 0.05f), 8), "from below");
            Assert.IsTrue(TryPath(new Vector3(e4.X + W / 2f, 0.03f, 0.03f), new Vector3(e4.X + W / 2f, -0.012f, 0.03f), 10), "gap between whites");
            Assert.IsTrue(TryPath(new Vector3(e4.X + WW / 2f - 0.001f, 0.03f, 0.03f), new Vector3(e4.X + WW / 2f - 0.001f, -0.012f, 0.03f), 10), "inset band white");
            Assert.IsTrue(TryPath(new Vector3(cs4.X - BW / 2f - 0.0008f, 0.04f, 0.1f), new Vector3(cs4.X - BW / 2f - 0.0008f, -0.006f, 0.1f), 10), "gap beside black");
            Assert.IsTrue(TryPath(new Vector3(cs4.X + BW / 2f - 0.0008f, 0.04f, 0.1f), new Vector3(cs4.X + BW / 2f - 0.0008f, BTOP - 0.009f, 0.1f), 10), "inset band black");
            Assert.IsTrue(TryPath(new Vector3(cs4.X, 0.04f, (L - BL) + 0.0006f), new Vector3(cs4.X, BTOP - 0.009f, (L - BL) + 0.0006f), 10), "black front edge");
            Assert.IsTrue(TryPath(new Vector3(e4.X, 0.08f, 0.2f), new Vector3(e4.X, 0.02f, 0.2f), 10, p => { for (int i = 1; i <= 10; i++) Step(p, e4.X, 0.02f, 0.2f - i * 0.012f); }), "top panel");
            Assert.IsTrue(TryPath(new Vector3(e4.X, 0.08f, 0.2f), new Vector3(e4.X, -0.004f, 0.2f), 10, p => { for (int i = 1; i <= 12; i++) Step(p, e4.X, -0.007f, 0.2f - i * 0.012f); }), "panel slide on keys");
            Assert.IsTrue(TryPath(new Vector3(x0 - 0.02f, 0.08f, 0.05f), new Vector3(x0 - 0.02f, -0.01f, 0.05f), 10), "end block bass");
            Assert.IsTrue(TryPath(new Vector3(-x0 + 0.02f, 0.08f, 0.05f), new Vector3(-x0 + 0.02f, -0.01f, 0.05f), 10), "end block treble");
            Assert.IsTrue(TryPath(new Vector3(x0 - 0.0015f, 0.05f, 0.05f), new Vector3(x0 - 0.0015f, -0.01f, 0.05f), 10), "beyond the end");
            Assert.IsTrue(TryPath(new Vector3(0f, 0.2f, 0.27f), new Vector3(0f, 0.03f, 0.25f), 12), "music rest");
            Assert.IsTrue(TryPath(new Vector3(e4.X, 0.05f, L + 0.001f), new Vector3(e4.X, -0.012f, L + 0.001f), 10), "behind the keys");
            Assert.IsTrue(TryPath(new Vector3(e4.X, 0.05f, L - 0.001f), new Vector3(e4.X, -0.012f, L - 0.001f), 10), "rear inset");
            Assert.IsTrue(Still(), "the bodies under the keys are solid: nothing moved");
        }

        [Test]
        public void AFingerOnAWhiteKeySlidingIntoABlackKeysSideIsSilent()
        {
            var c4 = K(60); var cs4 = K(61);
            _sink.Clear();
            var p = Mk();
            Path(p, new Vector3(c4.X, 0.03f, 0.12f), new Vector3(c4.X, -0.003f, 0.12f), 8, 1f / 90f);
            bool touched = c4.Depth > 0.3f, blackMoved = false;
            for (int i = 1; i <= 8; i++) { Step(p, c4.X + i * 0.002f, -0.003f, 0.12f); blackMoved |= cs4.Depth > 0f; }
            Assert.That(_sink.On.Count, Is.EqualTo(0));
            Assert.IsTrue(touched, "the white key follows the finger between the blacks");
            Assert.IsFalse(blackMoved, "the black key's side takes no press");
            Lift(p); Settle();
        }

        // ---- 2. presses that start inside a key top sound: every key, the white rears between the blacks ----

        int Inside(PianoKey key, float back, float x = float.NaN)
        {
            _sink.Clear();
            var p = Mk();
            float top = key.Top, kx = float.IsNaN(x) ? key.X : x;
            Path(p, new Vector3(kx, top + 0.02f, back), new Vector3(kx, top - 0.012f, back), 8, 1f / 90f);
            int n = _sink.Count(key.Midi);
            Lift(p); Settle();
            return n;
        }

        [Test]
        public void EveryKeySoundsOnceAndTheWhiteRearsToo()
        {
            var bad = new List<int>();
            foreach (var key in P.Keys) if (Inside(key, key.Black ? 0.09f : 0.03f) != 1) bad.Add(key.Midi);
            Assert.That(bad, Is.Empty, "every key sounds exactly once");
            int rears = 0;
            foreach (var w in P.Whites)
            {
                if (P.KeyTopAt(w.X, 0.13f, false) != w) continue;
                rears++;
                Assert.That(Inside(w, 0.13f), Is.EqualTo(1), $"rear of {w.Midi}");
            }
            Assert.That(rears, Is.GreaterThan(30));
            var e4 = K(64);
            Assert.That(Inside(e4, 0.03f, e4.X + PianoSpec.WW / 2f - 0.0018f), Is.EqualTo(1), "just inside the inset sounds");
        }

        // ---- 3. the note fires at the escapement, not on first touch ----

        struct StrokeResult { public int Count; public float Vel, FireS, PrevS, FireDepth; }

        StrokeResult Stroke(PianoKey key, float v, float hz, float back = float.NaN, float noise = 0f)
        {
            if (float.IsNaN(back)) back = key.Black ? 0.08f : 0.03f;
            float dt = 1f / hz;
            var p = Mk(dt);
            float top = key.Top;
            _sink.Clear();
            float y = top + 0.003f + v * 0.05f;
            Put(p, key.X, y, back);
            Update(p, dt);
            var r = new StrokeResult { FireS = float.NaN };
            int n = 0;
            while (y > top - 0.016f && n < 400)
            {
                float s0 = PianoAction.FingerDepth(key, y, back);
                y -= v * dt;
                n++;
                Step(p, key.X, y + Noise(noise), back, dt);
                if (float.IsNaN(r.FireS) && _sink.On.Count > 0) { r.FireS = PianoAction.FingerDepth(key, y, back); r.PrevS = s0; r.FireDepth = key.Depth; }
            }
            for (int i = 0; i < 30; i++) Step(p, key.X, y + Noise(noise), back, dt);   // hold, with tracking noise
            r.Count = _sink.Count(key.Midi);
            r.Vel = _sink.On.Count > 0 ? _sink.On[0].vel : float.NaN;
            Lift(p, dt);
            Settle();
            return r;
        }

        [Test]
        public void TheNoteFiresAtTheEscapement()
        {
            var e4 = K(64); var cs4 = K(61);
            var esc = Stroke(e4, 0.3f, 90f);
            Assert.That(esc.Count, Is.EqualTo(1));
            Assert.That(esc.PrevS, Is.LessThan(PianoSpec.S_ESC));
            Assert.That(esc.FireS, Is.GreaterThanOrEqualTo(PianoSpec.S_ESC));
            Assert.That(esc.FireS - esc.PrevS, Is.LessThan(0.6f));
            var escB = Stroke(cs4, 0.3f, 90f);
            Assert.That(escB.Count, Is.EqualTo(1));
            Assert.That(escB.PrevS, Is.LessThan(PianoSpec.S_ESC));
            Assert.That(escB.FireS, Is.GreaterThanOrEqualTo(PianoSpec.S_ESC));

            // A touch that stops short of the escapement plays nothing, however long it stays; the key shows the depth.
            _sink.Clear();
            var p = Mk();
            Path(p, new Vector3(e4.X, 0.02f, 0.03f), new Vector3(e4.X, -0.005f, 0.03f), 10, 1f / 90f);
            for (int i = 0; i < 40; i++) Step(p, e4.X, -0.005f - Noise(0.0006f), 0.03f, 1f / 90f);
            Assert.That(_sink.On.Count, Is.EqualTo(0), "short of the escapement: silent");
            Assert.That(e4.Depth, Is.GreaterThan(0.4f), "the key follows the resting finger");
            Lift(p); Settle();
        }

        // ---- 4. velocity: monotonic in speed, even in loudness, repeatable, the same at 60, 72 and 90 Hz ----

        static float Db(float x) => 20f * Mathf.Log10(x);
        static float LevelDb(float vel) => Db(0.08f + 0.8f * vel * vel);

        [Test]
        public void VelocityIsMonotonicEvenAndFrameRateIndependent()
        {
            var e4 = K(64);
            float[] speeds = { 0.08f, 0.15f, 0.3f, 0.6f, 1.0f, 1.5f };
            var byV = new StrokeResult[speeds.Length];
            for (int i = 0; i < speeds.Length; i++) byV[i] = Stroke(e4, speeds[i], 90f);
            for (int i = 0; i < speeds.Length; i++)
            {
                Assert.That(byV[i].Count, Is.EqualTo(1), $"one note at {speeds[i]} m/s");
                if (i > 0) Assert.That(byV[i].Vel, Is.GreaterThan(byV[i - 1].Vel), $"louder at {speeds[i]} m/s");
            }
            var per = new List<float>();
            for (int i = 1; i < speeds.Length; i++) per.Add((LevelDb(byV[i].Vel) - LevelDb(byV[i - 1].Vel)) / Mathf.Log(speeds[i] / speeds[i - 1], 2f));
            foreach (var x in per) Assert.That(x, Is.EqualTo(per[0]).Within(0.6f), "even dB per doubling of speed");
            Assert.That(byV[0].Vel, Is.LessThanOrEqualTo(0.15f), "a ghost-quiet note is possible");

            var rep = new List<float>();
            for (int i = 0; i < 6; i++) rep.Add(Stroke(e4, 0.4f, 90f).Vel);
            foreach (var v in rep) Assert.That(v, Is.EqualTo(rep[0]).Within(1e-6f), "the same speed gives the same level");

            var lv = new List<float>();
            foreach (float hz in new[] { 60f, 72f, 90f })
            {
                var r = Stroke(e4, 0.5f, hz);
                Assert.That(r.Count, Is.EqualTo(1), $"one note at {hz} Hz");
                lv.Add(LevelDb(r.Vel));
            }
            lv.Sort();
            Assert.That(lv[lv.Count - 1] - lv[0], Is.LessThan(1f), "the same within 1 dB at 60, 72 and 90 Hz");
        }

        [Test]
        public void FastNoisyStrokesNeverDoubleOrMiss()
        {
            var e4 = K(64); var cs4 = K(61);
            var log = new List<string>();
            foreach (float hz in new[] { 60f, 72f, 90f })
                foreach (float v in new[] { 0.15f, 0.8f, 2.5f })
                    foreach (var key in new[] { e4, cs4 })
                    {
                        var x = Stroke(key, v, hz, float.NaN, 0.0012f);
                        if (x.Count != 1) log.Add($"{hz}/{v}/{key.Midi}:{x.Count}");
                    }
            Assert.That(log, Is.Empty);
        }

        [Test]
        public void SparseTrackingFiresEarly()
        {
            // A pose every 30 ms (sdt >= SPARSE_DT): a stroke at 0.6 m/s whose sample lands past EARLY_S (s = .75 here) and
            // would reach the escapement before the next pose fires now, a pose early, rather than a pose late.
            var e4 = K(64);
            _sink.Clear();
            const float dt = 0.03f, back = 0.03f;
            var p = Mk(dt);
            Put(p, e4.X, 0.048f, back);
            Update(p, dt);
            Step(p, e4.X, 0.030f, back, dt);
            Step(p, e4.X, 0.012f, back, dt);
            Assert.That(_sink.On.Count, Is.EqualTo(0));
            Step(p, e4.X, -0.006f, back, dt);
            float s = PianoAction.FingerDepth(e4, -0.006f, back);
            Assert.That(s, Is.InRange(PianoSpec.EARLY_S, PianoSpec.S_ESC), "the sample is short of the escapement");
            Assert.That(_sink.On.Count, Is.EqualTo(1), "fired on the prediction s + v sdt depthPerMetre >= .9");
            Assert.That(_sink.On[0].vel, Is.EqualTo(PianoSpec.StrikeVelocity(0.6f)).Within(1e-4f));
            // At 90 Hz the same landing does not fire early (sdt < SPARSE_DT): it waits for the escapement.
            Lift(p, dt); Settle();
            _sink.Clear();
            var q = Mk(1f / 90f);
            Put(q, e4.X, 0.0045f, back);
            Update(q, 1f / 90f);
            Step(q, e4.X, -0.0060f, back, 1f / 90f);
            Assert.That(_sink.On.Count, Is.EqualTo(0));
            Lift(q); Settle();
        }

        // ---- 5. release: the key springs back in about 95 ms; the damper falls 70 % of the way up ----

        [Test]
        public void ReleaseTimingAndTheDamper()
        {
            var e4 = K(64);
            var p = Mk(1f / 90f);
            Path(p, new Vector3(e4.X, 0.02f, 0.03f), new Vector3(e4.X, -0.014f, 0.03f), 8, 1f / 90f);
            Assert.That(e4.PressedBy.Count, Is.EqualTo(1), "held");
            float dt = 1f / 360f;
            Step(p, e4.X, 0.05f, 0.03f, dt);
            Assert.That(e4.Depth, Is.GreaterThan(0.9f), "still down at the lift");
            Assert.IsTrue(e4.Held, "the damper waits for the key to come up");
            float t = dt, tDamper = -1f, t30 = -1f, tRest = -1f, dAtDamper = -1f;
            while (t < 0.3f && tRest < 0f)
            {
                Update(None, dt);
                t += dt;
                if (t30 < 0f && e4.Depth <= 0.3f) t30 = t;
                if (tDamper < 0f && !e4.Held) { tDamper = t; dAtDamper = e4.Depth; }
                if (e4.Depth == 0f) tRest = t;
            }
            Assert.That(tRest, Is.InRange(0.08f, 0.11f), "back at rest in about 95 ms");
            Assert.That(Mathf.Abs(tDamper - t30), Is.LessThan(0.004f), "the damper falls 70 % of the way up");
            Assert.That(dAtDamper, Is.InRange(0.25f, 0.3f));
            Assert.That(_sink.Events[_sink.Events.Count - 1].type, Is.EqualTo(TakeEventType.PianoOff));
            Lift(p); Settle();
        }

        [Test]
        public void TheKeyFollowsTheFingerAndSettlesOnTheFelt()
        {
            var e4 = K(64);
            var p = Mk(1f / 90f);
            const float back = 0.002f;
            float lf = (PianoSpec.L - back) / PianoSpec.L;
            Put(p, e4.X, 0.004f, back);
            Update(p, 1f / 90f);
            var trace = new List<(float finger, float key)>();
            for (int i = 1; i <= 17; i++)
            {
                float y = 0.004f - i * 0.001f;
                Step(p, e4.X, y, back);
                trace.Add(((-y / lf) * 1000f, e4.Depth * PianoSpec.TRAVEL * 1000f));
            }
            bool hitch = false;
            foreach (var x in trace)
            {
                if (x.finger > 0f && x.finger < 8f) Assert.That(Mathf.Abs(x.key - x.finger), Is.LessThan(0.15f), $"no lag at {x.finger} mm");
                Assert.That(x.key, Is.LessThanOrEqualTo(10f + 1e-4f), "never through the felt");
                if (x.finger >= 8f && x.finger <= 9.5f && x.finger - x.key > 0.2f) hitch = true;
            }
            Assert.That(trace[trace.Count - 1].key, Is.GreaterThan(9.7f), "settles on the felt");
            Assert.IsTrue(hitch, "a visible hitch at the escapement");
            Lift(p); Settle();
        }

        [Test]
        public void DarkeningAndHoverGlow()
        {
            var e4 = K(64);
            var p = Mk();
            Path(p, new Vector3(e4.X, 0.02f, 0.03f), new Vector3(e4.X, -0.012f, 0.03f), 8, 1f / 90f);
            Assert.That(e4.Shade, Is.LessThan(0.9f), "a key going down darkens");
            _one.Clear(); _one.Add(p);
            Assert.That(P.Hover(_one, null, Matrix4x4.identity, Matrix4x4.identity), Is.EqualTo(0), "no glow while pressing");
            Lift(p); Settle();

            var q = Mk();
            Put(q, e4.X, 0.02f, 0.03f);
            Assert.IsTrue(P.HoverSpec(q.Tip, Matrix4x4.identity, out var spec));
            Assert.That(spec.Gain, Is.EqualTo(0.45f), "faint");
            Assert.IsTrue(spec.Steady, "steady");
            Assert.That(spec.Strength, Is.EqualTo(0.6f).Within(1e-4f), "1 - h / 5 cm");
            Assert.That(spec.Width, Is.EqualTo(0.024f));
            Assert.That(spec.Length, Is.EqualTo(0.07f));
            Assert.That(spec.Position, Is.EqualTo(new Vector3(e4.X, 0.001f, 0.045f)), "kept behind the printed name");
            Assert.That(spec.Normal, Is.EqualTo(Vector3.up));
            _one.Clear(); _one.Add(q);
            Assert.That(P.Hover(_one, null, Matrix4x4.identity, Matrix4x4.identity), Is.EqualTo(1));
            Assert.IsFalse(P.HoverSpec(new Vector3(e4.X + PianoSpec.W / 2f, 0.02f, 0.03f), Matrix4x4.identity, out _), "no glow over a gap");
            Assert.IsFalse(P.HoverSpec(new Vector3(0f, 0.06f, 0.2f), Matrix4x4.identity, out _), "no glow over the panel");
            Assert.IsFalse(P.HoverSpec(new Vector3(e4.X, 0.051f, 0.03f), Matrix4x4.identity, out _), "only within 5 cm");
            var cs4 = K(61);
            Assert.IsTrue(P.HoverSpec(new Vector3(cs4.X, 0.03f, 0.09f), Matrix4x4.identity, out var bs));
            Assert.That(bs.Width, Is.EqualTo(0.016f));
            Assert.That(bs.Position.y, Is.EqualTo(PianoSpec.BTOP + 0.001f).Within(1e-6f));
            Assert.That(bs.Position.z, Is.EqualTo(0.1f).Within(1e-6f), "black glow clamped to 10 cm behind the fronts");
        }

        // ---- 6. chords ----

        [Test]
        public void ChordsOnAdjacentWhitesAndFourNotes()
        {
            var c4 = K(60); var d4 = K(62);
            _sink.Clear();
            var a = Mk(); var b = Mk();
            var both = new List<Pointer> { a, b };
            Put(a, c4.X + 0.004f, 0.02f, 0.03f);
            Put(b, d4.X - 0.004f, 0.02f, 0.03f);
            Update(both, 1f / 90f);
            for (int i = 1; i <= 8; i++)
            {
                a.Prev = a.Tip; b.Prev = b.Tip;
                a.Tip = new Vector3(c4.X + 0.004f, 0.02f - i * 0.0042f, 0.03f);
                b.Tip = new Vector3(d4.X - 0.004f, 0.02f - i * 0.0042f, 0.03f);
                Update(both, 1f / 90f);
            }
            Assert.That(c4.PressedBy.Count, Is.EqualTo(1));
            Assert.That(d4.PressedBy.Count, Is.EqualTo(1));
            Assert.That(_sink.On.Count, Is.EqualTo(2), "adjacent whites, close to the shared edge, both sound");
            for (int i = 0; i < 20; i++)
            {
                a.Prev = a.Tip; b.Prev = b.Tip;
                a.Tip = new Vector3(c4.X + 0.0075f + Noise(0.004f), -0.013f, 0.03f);
                b.Tip = new Vector3(d4.X - 0.0075f + Noise(0.004f), -0.013f, 0.03f);
                Update(both, 1f / 90f);
            }
            Assert.That(c4.PressedBy.Count + d4.PressedBy.Count, Is.EqualTo(2), "the chord holds under wobble");
            Assert.That(_sink.On.Count, Is.EqualTo(2));
            Lift(a); Lift(b); Settle();

            _sink.Clear();
            int[] chord = { 60, 64, 67, 72 };
            var ps = new List<Pointer>();
            foreach (int m in chord) { var p = Mk(); Put(p, K(m).X, 0.02f, 0.03f); ps.Add(p); }
            Update(ps, 1f / 90f);
            for (int i = 1; i <= 8; i++)
            {
                for (int j = 0; j < ps.Count; j++) { ps[j].Prev = ps[j].Tip; ps[j].Tip = new Vector3(K(chord[j]).X, 0.02f - i * 0.0042f, 0.03f); }
                Update(ps, 1f / 90f);
            }
            foreach (int m in chord) Assert.That(K(m).PressedBy.Count, Is.EqualTo(1), $"{m} held");
            Assert.That(_sink.On.Count, Is.EqualTo(4));
            foreach (var p in ps) Lift(p);
            Settle();
        }

        // ---- 7. glissando ----

        [Test]
        public void GlissandoPlaysEachWhiteKey()
        {
            int[] run = { 60, 62, 64, 65, 67, 69 };
            _sink.Clear();
            var p = Mk();
            Path(p, new Vector3(K(60).X, 0.02f, 0.03f), new Vector3(K(60).X, -0.012f, 0.03f), 8, 1f / 90f);
            const int n = 40;
            for (int i = 1; i <= n; i++) Step(p, Mathf.Lerp(K(60).X, K(69).X, (float)i / n), -0.012f, 0.03f, 1f / 90f);
            var notes = new List<int>();
            foreach (var o in _sink.On) notes.Add(o.midi);
            Assert.That(notes, Is.EqualTo(run));
            Assert.That(K(69).PressedBy.Count, Is.EqualTo(1), "the last key is held");
            for (int i = 0; i < 5; i++) Assert.That(K(run[i]).PressedBy.Count, Is.EqualTo(0));
            foreach (var o in _sink.On) if (o.midi != 60) Assert.That(o.vel, Is.InRange(0.2f, 0.9f), "glissando velocity clamp((speed / 2)^.8, .2, .9)");
            Lift(p); Settle();
        }

        /// Strike C4 at the front of the key, then slide along the white keys at a fixed height and speed (dx per 90 Hz frame).
        List<(int midi, float vel)> Slide(float y, float dx, int toMidi, out Pointer p)
        {
            _sink.Clear();
            p = Mk();
            Path(p, new Vector3(K(60).X, 0.02f, 0.03f), new Vector3(K(60).X, -0.012f, 0.03f), 8, 1f / 90f);
            Assert.That(_sink.Count(60), Is.EqualTo(1), "C4 struck first");
            if (y != -0.012f) Step(p, K(60).X, y, 0.03f, 1f / 90f);   // rise (or stay) to the slide height on C4
            for (float x = K(60).X + dx; x <= K(toMidi).X + 1e-6f; x += dx) Step(p, x, y, 0.03f, 1f / 90f);
            return new List<(int, float)>(_sink.On);
        }

        [Test]
        public void GlissandoVelocityFollowsTheSlideSpeed()
        {
            // 10 mm per 1/90 s = 0.9 m/s: every white key on the way sounds at clamp((0.9 / 2)^.8, .2, .9) = 0.528, well
            // inside the clamp, so a wrong exponent, a missing /2 or a fixed velocity fails here.
            var on = Slide(-0.012f, 0.01f, 69, out var p);
            float speed = 0.01f * 90f;
            float expected = Mathf.Clamp(Mathf.Pow(speed / 2f, 0.8f), 0.2f, 0.9f);
            Assert.That(expected, Is.EqualTo(0.528f).Within(0.002f));
            var notes = new List<int>();
            foreach (var o in on) notes.Add(o.midi);
            Assert.That(notes, Is.EqualTo(new[] { 60, 62, 64, 65, 67, 69 }));
            for (int i = 1; i < on.Count; i++) Assert.That(on[i].vel, Is.EqualTo(expected).Within(1e-4f), $"glissando note {on[i].midi}");
            Lift(p); Settle();
        }

        [Test]
        public void ASlowSlideMovesTheContactWithoutStriking()
        {
            // 0.1 m/s is under the 0.15 m/s glissando gate: the finger slides from key to key still pressing into them, but
            // only the C4 it struck sounds; the key under it follows the finger and C4 lets go.
            var on = Slide(-0.012f, 0.1f / 90f, 64, out var p);
            Assert.That(on.Count, Is.EqualTo(1), "only the struck key");
            Assert.That(K(60).PressedBy.Count, Is.EqualTo(0), "C4 let go when the finger slid off");
            Assert.That(K(64).Depth, Is.GreaterThan(0.9f), "E4 is under the finger, pushed down");
            Assert.That(K(64).PressedBy.Count, Is.EqualTo(0), "but not struck");
            Lift(p); Settle();
        }

        [Test]
        public void AShallowSlideMovesTheContactWithoutStriking()
        {
            // Fast (0.9 m/s) but at s = 0.625 (y = -5 mm at 3 cm from the front, lever 12 cm): above S_RESET, so C4 stays
            // struck until the finger leaves it, below S_ESC, so the neighbours are only pushed part way, never struck.
            Assert.That(PianoAction.FingerDepth(K(62), -0.005f, 0.03f), Is.InRange(PianoSpec.S_RESET, PianoSpec.S_ESC));
            var on = Slide(-0.005f, 0.01f, 67, out var p);
            Assert.That(on.Count, Is.EqualTo(1), "only the struck key");
            Assert.That(K(67).Depth, Is.EqualTo(PianoSpec.KeyShape(PianoAction.FingerDepth(K(67), -0.005f, 0.03f))).Within(1e-4f), "the contact followed to G4");
            Assert.That(K(67).Depth, Is.GreaterThan(0.3f));
            foreach (int m in new[] { 60, 62, 64, 65 }) Assert.That(K(m).PressedBy.Count, Is.EqualTo(0), $"{m} not held");
            Lift(p); Settle();
        }

        // ---- the desktop mouse hand never strikes (ARCHITECTURE section 0) ----

        [Test]
        public void MouseHandPointersNeverStrike()
        {
            // The mouse hand's LMB poke pushes its index tip 2 cm past the surface it rests on; PianoDesktop already played
            // that click, so the same stroke through the pointer layer must stay silent and move nothing.
            var mouse = new HandState(Handedness.Right) { IsMouse = true };
            var p = new Pointer("mouse-index", mouse, 10) { Active = true, WasActive = true, SampleDt = 1f / 90f };
            Assert.That(p.FromMouse, Is.True);
            var e4 = K(64);
            Path(p, new Vector3(e4.X, 0.02f, 0.03f), new Vector3(e4.X, -0.014f, 0.03f), 8, 1f / 90f);
            Assert.That(_sink.On.Count, Is.EqualTo(0), "no NoteOn");
            Assert.That(_sink.Events.Count, Is.EqualTo(0), "nothing recorded");
            Assert.That(e4.Depth, Is.EqualTo(0f), "the key does not move");
            Assert.That(mouse.LastPlayMs, Is.EqualTo(double.NegativeInfinity), "the mouse hand never counts as playing");
            // The same stroke by a real fingertip does sound (the test is not silent for another reason).
            var real = Mk();
            Path(real, new Vector3(e4.X, 0.02f, 0.03f), new Vector3(e4.X, -0.014f, 0.03f), 8, 1f / 90f);
            Assert.That(_sink.Count(64), Is.EqualTo(1));
            Lift(real); Settle();
        }

        // ---- leaving the room records the release of held keys ----

        [Test]
        public void ResetAllRecordsPianoOffForHeldKeys()
        {
            // A take recorded across a layout change must not hold a key forever on playback (pianoOff -> playbackOff),
            // so every held key's pianoOff is recorded; ringing (already released) notes and silent keys record nothing.
            P.PressMidi(60, "held");
            P.PressMidi(64, "ring"); P.ReleaseMidi(64, "ring");
            var finger = Mk();
            Path(finger, new Vector3(K(67).X, 0.02f, 0.03f), new Vector3(K(67).X, -0.014f, 0.03f), 8, 1f / 90f);
            Assert.That(P.HeldKeyCount, Is.EqualTo(2));
            _sink.Events.Clear(); _sink.Off.Clear();
            P.ResetAll();
            var offs = new List<int>();
            foreach (var e in _sink.Events) { Assert.That(e.type, Is.EqualTo(TakeEventType.PianoOff)); offs.Add(e.midi); }
            Assert.That(offs, Is.EquivalentTo(new[] { 60, 67 }), "one pianoOff per held key");
            Assert.That(_sink.Off.Count, Is.EqualTo(3), "every voice stops");
            foreach (var o in _sink.Off) Assert.That(o.thud, Is.False, "silently");
            Assert.That(P.HeldKeyCount, Is.EqualTo(0));
            _sink.Events.Clear();
            P.ResetAll();
            Assert.That(_sink.Events.Count, Is.EqualTo(0), "a second reset has nothing left to release");
        }

        // ---- automatic sustain (spec 05 section 2.5) ----

        [Test]
        public void AutomaticSustainRingsOnUntilTheNextPhrase()
        {
            // Release a key: the note keeps ringing (pianoOff recorded, no NoteOff); a key within PHRASE_GAP of the last
            // release keeps the pedal down; a key after the gap with nothing held lifts it: one thud for all the damped notes.
            P.PressMidi(60, "a"); Update(None, 0.02f);
            P.ReleaseMidi(60, "a");
            Assert.That(_sink.Off.Count, Is.EqualTo(0), "released notes ring on");
            Assert.That(P.SustainedCount, Is.EqualTo(1));
            Assert.That(_sink.Events[1].type, Is.EqualTo(TakeEventType.PianoOff));
            Assert.That(_sink.Events[1].midi, Is.EqualTo(60));
            for (int i = 0; i < 6; i++) Update(None, 0.05f);   // 0.3 s < PHRASE_GAP
            P.PressMidi(64, "b"); P.ReleaseMidi(64, "b");
            Assert.That(_sink.Off.Count, Is.EqualTo(0), "inside the phrase gap: still ringing");
            Assert.That(P.SustainedCount, Is.EqualTo(2));
            for (int i = 0; i < 14; i++) Update(None, 0.05f);   // 0.7 s > PHRASE_GAP
            P.PressMidi(67, "c");
            Assert.That(_sink.Off.Count, Is.EqualTo(2), "a new phrase damps every released note");
            Assert.That(_sink.Off[0], Is.EqualTo((60, true)), "the first keeps its damper thud");
            Assert.That(_sink.Off[1], Is.EqualTo((64, false)), "one thud, not one per note");
            Assert.That(P.SustainedCount, Is.EqualTo(0));
            Assert.That(_sink.On[_sink.On.Count - 1].midi, Is.EqualTo(67));
            P.ReleaseMidi(67, "c");
        }

        [Test]
        public void NothingRingsLongerThanMaxRing()
        {
            P.PressMidi(60, "a");
            P.ReleaseMidi(60, "a");
            float t = 0f;
            while (t < 5.9f) { Update(None, 0.05f); t += 0.05f; }
            Assert.That(_sink.Off.Count, Is.EqualTo(0), "still ringing before 6 s");
            for (int i = 0; i < 6; i++) Update(None, 0.05f);
            Assert.That(_sink.Off.Count, Is.EqualTo(1), "released at MAX_RING");
            Assert.That(_sink.Off[0], Is.EqualTo((60, true)));
            Assert.That(P.SustainedCount, Is.EqualTo(0));
        }

        [Test]
        public void ARestrikeReleasesTheRingingNoteFirst()
        {
            P.PressMidi(60, "a"); P.ReleaseMidi(60, "a");
            P.PressMidi(60, "a");
            Assert.That(_sink.Off, Is.EqualTo(new List<(int, bool)> { (60, true) }), "the old note goes with its damper thud");
            Assert.That(_sink.Count(60), Is.EqualTo(2));
            Assert.That(P.SustainedCount, Is.EqualTo(0), "a pressed key leaves the sustained set");
            P.ReleaseMidi(60, "a");
        }

        [Test]
        public void PressApiHoldsChordsAndRecords()
        {
            P.PressMidi(60, "kbd-A", 0.75f);
            P.PressMidi(64, "kbd-D", 0.75f);
            P.PressMidi(60, "kbd-A", 0.75f);   // already pressed by this presser: ignored
            Assert.That(_sink.On.Count, Is.EqualTo(2));
            Assert.That(P.HeldKeyCount, Is.EqualTo(2));
            Assert.That(_sink.Events[0].type, Is.EqualTo(TakeEventType.PianoOn));
            Assert.That(_sink.Events[0].vel, Is.EqualTo(0.75f));
            P.ReleaseMidi(60, "someone else");
            Assert.That(P.HeldKeyCount, Is.EqualTo(2), "only its presser releases a key");
            P.ReleaseMidi(60, "kbd-A");
            Assert.That(P.HeldKeyCount, Is.EqualTo(1), "release from the keyboard damps at once");
            Assert.That(_sink.Events[_sink.Events.Count - 1].type, Is.EqualTo(TakeEventType.PianoOff));
            // Driven keys go down in PRESS_TIME (45 ms).
            Update(None, 0.02f);
            Assert.That(K(64).Depth, Is.EqualTo(0.02f / 0.045f).Within(1e-4f));
            Update(None, 0.03f);
            Assert.That(K(64).Depth, Is.EqualTo(1f));
            P.ReleaseMidi(64, "kbd-D");
            Assert.That(P.HeldKeyCount, Is.EqualTo(0));
            Assert.IsNull(P.KeyByMidi(10));
            P.PressMidi(10, "x");   // out of range: nothing
        }

        [Test]
        public void PlaybackGhostHoldsAndReleasesAll()
        {
            P.PlaybackOn(60, 0.5f);
            P.PlaybackOn(64, 0.6f);
            Assert.That(P.HeldKeyCount, Is.EqualTo(2));
            P.PlaybackOff(60);
            Assert.That(P.HeldKeyCount, Is.EqualTo(1));
            P.PlaybackOn(64, 0.7f);   // a repeat re-strikes
            Assert.That(_sink.Count(64), Is.EqualTo(2));
            P.PlaybackReleaseAll();
            Assert.That(P.HeldKeyCount, Is.EqualTo(0));
            Assert.That(P.SustainedCount, Is.EqualTo(0), "releaseAll also lifts the pedal");
            int offs = 0; foreach (var o in _sink.Off) if (o.midi == 60 || o.midi == 64) offs++;
            Assert.That(offs, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void FingertipPressesStampTheHand()
        {
            var e4 = K(64);
            Assert.That(_hand.LastPlayMs, Is.EqualTo(double.NegativeInfinity));
            var p = Mk();
            Path(p, new Vector3(e4.X, 0.02f, 0.03f), new Vector3(e4.X, -0.014f, 0.03f), 8, 1f / 90f);
            Assert.That(_sink.On.Count, Is.EqualTo(1));
            Assert.That(_hand.LastPlayMs, Is.EqualTo(_clock.NowMs).Within(30.0), "grab guards see the hand playing");
            Assert.That(P.PresserOf(p).Pressing, Is.SameAs(e4));
            // An inactive pointer lets go of its key.
            p.Active = false;
            Update(p, 1f / 90f);
            Assert.That(e4.PressedBy.Count, Is.EqualTo(0));
            Settle();
        }

        [Test]
        public void RepetitionNeedsOnlyHalfTheTravel()
        {
            var e4 = K(64);
            _sink.Clear();
            var p = Mk();
            Path(p, new Vector3(e4.X, 0.02f, 0.03f), new Vector3(e4.X, -0.014f, 0.03f), 8, 1f / 90f);
            Assert.That(_sink.On.Count, Is.EqualTo(1));
            // Rise to just under half travel (s < .5) and strike again without leaving the key.
            float lever = PianoSpec.L - 0.03f, perM = 1f / (lever * PianoSpec.MaxAngleWhite);
            float yHalf = -0.45f / perM;
            for (int i = 1; i <= 6; i++) Step(p, e4.X, Mathf.Lerp(-0.014f, yHalf, i / 6f), 0.03f);
            Assert.That(e4.PressedBy.Count, Is.EqualTo(0), "half travel re-arms the key");
            for (int i = 1; i <= 6; i++) Step(p, e4.X, Mathf.Lerp(yHalf, -0.014f, i / 6f), 0.03f);
            Assert.That(_sink.On.Count, Is.EqualTo(2), "a repeated note");
            Lift(p); Settle();
        }

        [Test]
        public void DesktopClickDragAndRelease()
        {
            var mouse = new object();
            P.DesktopDown(K(60), mouse);
            Assert.That(_sink.On[0], Is.EqualTo((60, 0.7f)), "a click presses at .7");
            P.DesktopMove(K(60), mouse);
            Assert.That(_sink.On.Count, Is.EqualTo(1), "moving on the same key does nothing");
            P.DesktopMove(K(62), mouse);
            Assert.That(_sink.On[1], Is.EqualTo((62, 0.6f)), "a drag plays the next key at .6");
            Assert.That(K(60).Held, Is.False, "and released the previous one");
            P.DesktopUp(mouse);
            Assert.That(P.HeldKeyCount, Is.EqualTo(0));

            // The computer keyboard row: base 60, Z / X octave within 24..96, a held key releases the note it started.
            var d = new PianoDesktop(P);
            _sink.Clear();
            d.KeyDown(0);
            Assert.That(_sink.On[0], Is.EqualTo((60, 0.75f)));
            d.KeyDown(4);
            Assert.That(_sink.On[1].midi, Is.EqualTo(64), "chords hold: one presser per physical key");
            d.OctaveUp();
            d.KeyUp(0);
            Assert.That(K(60).Held, Is.False, "the octave change does not strand the held note");
            d.KeyDown(0);
            Assert.That(_sink.On[2].midi, Is.EqualTo(72));
            d.KeyUp(0); d.KeyUp(4);
            for (int i = 0; i < 10; i++) d.OctaveUp();
            Assert.That(d.KeyboardBase, Is.EqualTo(96));
            for (int i = 0; i < 10; i++) d.OctaveDown();
            Assert.That(d.KeyboardBase, Is.EqualTo(24));
            Assert.That(P.HeldKeyCount, Is.EqualTo(0));
        }

        [Test]
        public void DesktopRayFindsTheKeyUnderTheMouse()
        {
            var d = new PianoDesktop(P);
            var go = new GameObject("piano");
            try
            {
                go.transform.position = new Vector3(0f, 0.72f, 0.35f);
                // From the desktop eye (0, 1.2, -0.1) toward middle C's top, 5 cm behind its front.
                var c4 = K(60);
                Vector3 target = go.transform.TransformPoint(new Vector3(c4.X, 0f, 0.05f));
                var eye = new Vector3(0f, 1.2f, -0.1f);
                var key = d.Raycast(go.transform, new Ray(eye, (target - eye).normalized), out float dist);
                Assert.That(key, Is.SameAs(c4));
                Assert.That(dist, Is.EqualTo((target - eye).magnitude).Within(0.002f));
                var cs4 = K(61);
                target = go.transform.TransformPoint(new Vector3(cs4.X, PianoSpec.BTOP, 0.12f));
                Assert.That(d.Raycast(go.transform, new Ray(eye, (target - eye).normalized), out _), Is.SameAs(cs4), "black keys stand above the whites");
                Assert.That(d.Raycast(go.transform, new Ray(eye, Vector3.up), out _), Is.Null);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ApiPressersLeaveNoStateBehind()
        {
            // Presser state could live on the pointer object itself (p.state), collected with it. Here it lives in a table,
            // so a caller that passes a fresh object per note (a lesson demo, a helper) must not grow it for the session.
            var finger = Mk();
            Path(finger, new Vector3(K(64).X, 0.02f, 0.03f), new Vector3(K(64).X, -0.014f, 0.03f), 8, 1f / 90f);
            Assert.That(_sink.Count(64), Is.EqualTo(1));
            int baseline = P.PresserCount;
            Assert.That(baseline, Is.EqualTo(1), "the fingertip (its contact and history persist)");
            for (int i = 0; i < 200; i++) { var o = new object(); P.PressMidi(72 + i % 12, o); P.ReleaseMidi(72 + i % 12, o); }
            Assert.That(P.PresserCount, Is.EqualTo(baseline), "200 one-note pressers leave nothing behind");

            // A presser holding a key keeps its state until it lets go.
            var a = new object(); var b = new object();
            P.PressMidi(48, a); P.PressMidi(50, b);
            Assert.That(P.PresserCount, Is.EqualTo(baseline + 2));
            Assert.That(P.PresserOf(a).Pressing, Is.SameAs(K(48)));
            P.ReleaseMidi(48, a);
            Assert.That(P.PresserOf(a), Is.Null);
            Assert.That(K(48).Held, Is.False);
            Assert.That(P.PresserCount, Is.EqualTo(baseline + 1));

            // The mouse drag still hands its key on (release then press on the pooled entry), then lets go of it.
            var mouse = new object();
            P.DesktopDown(K(60), mouse);
            P.DesktopMove(K(62), mouse);
            Assert.That(P.PresserOf(mouse).Pressing, Is.SameAs(K(62)));
            Assert.That(K(60).Held, Is.False);
            P.DesktopUp(mouse);
            Assert.That(P.PresserOf(mouse), Is.Null);
            P.ReleaseMidi(50, b);
            P.PlaybackOn(65, 0.5f); P.PlaybackOn(67, 0.5f);
            P.PlaybackReleaseAll();
            Assert.That(P.PresserCount, Is.EqualTo(baseline), "the playback ghost too");
            Assert.That(P.PresserOf(finger), Is.Not.Null, "the fingertip keeps its state");

            // Leaving the room forgets the API pressers that were still holding keys.
            P.PressMidi(70, new object());
            P.ResetAll();
            Assert.That(P.PresserCount, Is.EqualTo(baseline));
            Assert.That(P.HeldKeyCount, Is.EqualTo(0));
            Lift(finger);
            Settle();
        }

        [Test]
        public void CtrlOrCmdKeepsShortcutsOffTheKeyboardRow()
        {
            // keydown: `if (e.repeat || e.metaKey || e.ctrlKey) return;` (Cmd+S, Ctrl+P, Cmd+D never play); keyup is
            // never filtered, so a note started before the modifier still ends.
            var d = new PianoDesktop(P);
            DKey down = DKey.Escape;
            var held = new HashSet<DKey>();
            System.Func<DKey, bool> wentDown = k => k == down, isHeld = held.Contains;
            down = DKey.S; held.Add(DKey.S);
            d.KeyboardFrame(wentDown, isHeld, command: true);
            Assert.That(_sink.On.Count, Is.EqualTo(0), "Cmd+S plays nothing");
            Assert.That(_sink.Events.Count, Is.EqualTo(0));
            down = DKey.X;
            d.KeyboardFrame(wentDown, isHeld, command: true);
            Assert.That(d.KeyboardBase, Is.EqualTo(60), "Ctrl+X moves no octave");
            down = DKey.S;
            d.KeyboardFrame(wentDown, isHeld, command: false);
            Assert.That(_sink.On.Count, Is.EqualTo(1));
            Assert.That(_sink.On[0], Is.EqualTo((62, 0.75f)), "S alone plays D4 at .75");
            down = DKey.Escape;
            d.KeyboardFrame(wentDown, isHeld, command: true);
            Assert.That(K(62).Held, Is.True, "Ctrl pressed while S is held: the note stays");
            held.Clear();
            d.KeyboardFrame(wentDown, isHeld, command: true);
            Assert.That(K(62).Held, Is.False, "releasing S under Ctrl still ends it");
            Assert.That(P.HeldKeyCount, Is.EqualTo(0));
            down = DKey.X;
            d.KeyboardFrame(wentDown, isHeld, command: false);
            Assert.That(d.KeyboardBase, Is.EqualTo(72), "X alone moves the octave up");
        }

        [Test]
        public void ResetAllLetsGoOfEverything()
        {
            P.PressMidi(60, "a");
            P.PressMidi(62, "b"); P.ReleaseMidi(62, "b");
            Update(None, 0.05f);
            P.ResetAll();
            Assert.That(P.HeldKeyCount, Is.EqualTo(0));
            Assert.That(P.SustainedCount, Is.EqualTo(0));
            Assert.That(K(60).Depth, Is.EqualTo(0f));
            Assert.That(_sink.Off.Count, Is.EqualTo(2), "both notes stop");
            Assert.That(_sink.Off[0].thud || _sink.Off[1].thud, Is.False, "silently");
        }
    }
}
