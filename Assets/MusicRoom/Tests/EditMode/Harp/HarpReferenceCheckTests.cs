// The harp's reference checks: synthetic fingertips, moves and thresholds, one test per check group, with the check
// names in the assertion messages. Sections covered: 1 (visibility against the harp's own frame, view, width, reach, every string
// pluckable by a poke and a sweep), 3 (catch and release, ringing, decay, back-off, lift-off), 4 (glissando order and
// spread, the 1 cm / 2 cm zone, a slow sweep, lingering, poke between strings), 5 (palm damping), 6 (velocity from
// speed, every pluck lights at once, hover like the keys). Sections 2 (lesson capsules) and 7 (old / new sound) belong
// to the Lessons and Audio modules. Reference coordinates are right-handed: z values are negated with HarpRig.L3 / -z.
using System;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Harp;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Harp
{
    public class HarpReferenceCheckTests
    {
        const float Dt = HarpRig.Dt;

        static Vector3 PlayPoint(int i) => HarpGeometry.PlayPoint(i);

        // ---------------- 1. visibility and reach from the seat ----------------

        /// <summary>The seat frames of the check: all mode at head heights 1.15 / 1.2 (seat (0, h, 0)) and single mode
        /// (seat (0, h, 0.1) right-handed = Unity z -0.1), the harp placed from its seat-frame home as Layout does.</summary>
        static IEnumerable<(string label, Vector3 eye, Vector3 harpPos, Quaternion harpRot)> Seats()
        {
            float mx = HarpGeometry.MeanStringMidX();
            foreach (float h in new[] { 1.15f, 1.2f })
            {
                var seat = new SeatFrame { Head = new Vector3(0f, h, 0f), YawDeg = 0f };
                yield return ("all/" + h, seat.Head, seat.Place(HarpInstrument.HomeAll), Quaternion.Euler(0f, seat.Yaw(HarpInstrument.HomeYawAll), 0f));
            }
            foreach (float h in new[] { 1.15f, 1.2f })
            {
                var seat = new SeatFrame { Head = new Vector3(0f, h, -0.1f), YawDeg = 0f };
                yield return ("single/" + h, seat.Head, seat.Place(new Vector3(mx, 0f, HarpInstrument.SingleFront)), Quaternion.Euler(0f, seat.Yaw(HarpInstrument.HomeYawSingle), 0f));
            }
        }

        /// <summary>Frame triangles in harp-local Unity space, grouped per part with the part's box for a quick reject
        /// (the occluders: pillar, cap, neck, soundbox, soundboard, rib, base, feet, pins).</summary>
        static List<(Bounds box, List<Vector3[]> tris)> FrameTriangles()
        {
            var parts = new List<(Bounds, List<Vector3[]>)>();
            foreach (HarpPart p in HarpGeometry.BuildFrame())
            {
                MeshData m = p.Mesh;
                var tris = new List<Vector3[]>();
                var box = new Bounds(p.LocalPosition, Vector3.zero);
                for (int t = 0; t + 2 < m.Indices.Count; t += 3)
                {
                    var tri = new Vector3[3];
                    for (int c = 0; c < 3; c++)
                    {
                        tri[c] = p.LocalPosition + p.LocalRotation * Vector3.Scale(p.LocalScale, m.Positions[m.Indices[t + c]]);
                        box.Encapsulate(tri[c]);
                    }
                    tris.Add(tri);
                }
                box.Expand(0.002f);
                parts.Add((box, tris));
            }
            return parts;
        }

        static bool Blocked(List<(Bounds box, List<Vector3[]> tris)> parts, Vector3 eye, Vector3 dir, float before)
        {
            var ray = new Ray(eye, dir);
            foreach (var (box, tris) in parts)
            {
                if (!box.IntersectRay(ray, out float enter) || enter > before) continue;
                foreach (var tri in tris) if (Hit(eye, dir, tri) < before) return true;
            }
            return false;
        }

        /// <summary>Moller-Trumbore, both faces: distance along the unit ray, or infinity.</summary>
        static float Hit(Vector3 o, Vector3 d, Vector3[] tri)
        {
            Vector3 e1 = tri[1] - tri[0], e2 = tri[2] - tri[0];
            Vector3 pv = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, pv);
            if (Mathf.Abs(det) < 1e-12f) return float.PositiveInfinity;
            float inv = 1f / det;
            Vector3 tv = o - tri[0];
            float u = Vector3.Dot(tv, pv) * inv;
            if (u < 0f || u > 1f) return float.PositiveInfinity;
            Vector3 qv = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(d, qv) * inv;
            if (v < 0f || u + v > 1f) return float.PositiveInfinity;
            float t = Vector3.Dot(e2, qv) * inv;
            return t > 0f ? t : float.PositiveInfinity;
        }

        [Test]
        public void EveryStringIsVisibleInViewWideEnoughAndInReachFromTheSeat()
        {
            var occluders = FrameTriangles();
            // Negative controls: the occlusion test does see the frame (a ray through the pillar, one through the soundbox).
            Assert.IsTrue(Blocked(occluders, new Vector3(-0.69f, 0.66f, 0.5f), Vector3.back, 1f), "the pillar blocks a ray through it");
            HarpGeometry.SoundboxAxis(out Vector3 c0, out Vector3 c1);
            Vector3 boxMid = (c0 + c1) * 0.5f;
            Assert.IsTrue(Blocked(occluders, boxMid + new Vector3(0f, 0f, 0.5f), Vector3.back, 1f), "the soundbox blocks a ray through it");
            Assert.IsFalse(Blocked(occluders, new Vector3(-0.3f, 0.9f, 0.5f), Vector3.back, 1f), "the open string field does not");
            const float pxPerDeg = 1832f / 96f;      // Quest 3S, per eye
            const float shoulder = 0.18f;
            float tanV = Mathf.Tan(45f * Mathf.Deg2Rad), tanH = tanV * 96f / 90f; // fov 90, aspect 96/90
            foreach (var (label, eyeW, harpPos, harpRot) in Seats())
            {
                Quaternion inv = Quaternion.Inverse(harpRot);
                Vector3 eye = inv * (eyeW - harpPos);                    // the eye in harp-local Unity
                Vector3 target = PlayPoint(10);                          // the check looks at string 10's play point
                Vector3 fwd = (target - eye).normalized;
                Vector3 up = inv * Vector3.up;
                Vector3 right = Vector3.Cross(up, fwd).normalized;
                Vector3 camUp = Vector3.Cross(fwd, right);
                Vector3 seatRight = inv * Vector3.right;
                float worst = 0f, worstLesson = 0f;
                for (int i = 0; i < 22; i++)
                {
                    StringLayout s = HarpGeometry.Strings[i];
                    var pts = new List<Vector3>();
                    foreach (float u in new[] { 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f }) pts.Add(Vector3.Lerp(s.A, s.B, u));
                    Vector3 pp = PlayPoint(i);
                    pts.Add(pp);
                    int clear = 0;
                    bool ppClear = false, inView = true;
                    for (int k = 0; k < pts.Count; k++)
                    {
                        Vector3 rel = pts[k] - eye;
                        float z = Vector3.Dot(rel, fwd);
                        if (z <= 0f || Mathf.Abs(Vector3.Dot(rel, right) / (z * tanH)) > 1f || Mathf.Abs(Vector3.Dot(rel, camUp) / (z * tanV)) > 1f) inView = false;
                        float dist = rel.magnitude;
                        Vector3 dir = rel / dist;
                        if (!Blocked(occluders, eye, dir, dist - 0.003f)) { clear++; if (k == pts.Count - 1) ppClear = true; }
                    }
                    Assert.IsTrue(ppClear && clear / (float)pts.Count >= 0.85f, $"{label}: string {i} visible (clear {clear}/{pts.Count}, play point clear {ppClear}): everyStringVisible");
                    Assert.IsTrue(inView, $"{label}: string {i} in a Quest view centred on string 10: everyStringInView");
                    float px = 2f * s.Radius / Vector3.Distance(pp, eye) * Mathf.Rad2Deg * pxPerDeg;
                    Assert.That(px, Is.GreaterThanOrEqualTo(1.5f), $"{label}: string {i} at least 1.5 px wide: everyStringWideEnough");
                    // Horizontal reach from the nearer shoulder (the torso faces the seat's way).
                    Vector3 sa = eye + seatRight * shoulder, sb = eye - seatRight * shoulder;
                    Vector3 ppW = harpRot * pp + harpPos, saW = harpRot * sa + harpPos, sbW = harpRot * sb + harpPos;
                    float reach = Mathf.Min(Flat(ppW - saW), Flat(ppW - sbW));
                    worst = Mathf.Max(worst, reach);
                    if (i >= 4) worstLesson = Mathf.Max(worstLesson, reach);
                }
                Assert.That(worst, Is.LessThanOrEqualTo(0.8f), label + ": every string within 0.80 m of the nearer shoulder: everyStringInReach");
                Assert.That(worstLesson, Is.LessThanOrEqualTo(0.72f), label + ": every lesson string (G3 up) within 0.72 m");
            }
        }

        static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);

        [Test]
        public void EveryStringIsPluckedByAPokeAndASweepAtItsPlayPointAndNoOther()
        {
            foreach (var (label, eyeW, harpPos, harpRot) in Seats())
            {
                Vector3 eye = Quaternion.Inverse(harpRot) * (eyeW - harpPos);
                for (int i = 0; i < 22; i++)
                {
                    Vector3 pl = PlayPoint(i);
                    // A poke from the player's side along the line of sight: 2 cm short of the plane to 1.2 cm past it.
                    var h = new HarpRig();
                    Vector3 toEye = (eye - pl).normalized;
                    h.Move(h.Finger, pl + toEye * (0.02f / Mathf.Abs(toEye.z)), pl - toEye * (0.012f / Mathf.Abs(toEye.z)));
                    Assert.That(h.Sound.Plucks.Select(c => c.String).ToArray(), Is.EqualTo(new[] { i }), $"{label}: poke at string {i}: pokesHitOwnString");
                    // A sweep across it 4 mm off the plane, 6 mm steps from -9 mm to +9 mm.
                    var q = new HarpRig();
                    Vector3 n = HarpRig.Normal(i);
                    q.Sweep(q.Finger, new[] { pl - n * 0.009f, pl + n * 0.009f }, -0.004f, 0.006f);
                    Assert.That(q.Sound.Plucks.Select(c => c.String).ToArray(), Is.EqualTo(new[] { i }), $"{label}: sweep at string {i}: sweepsHitOwnString");
                }
            }
        }

        // ---------------- 3. catch and release, the ringing, the light ----------------

        static Vector3 At9(float d, float zRh = 0.004f) => PlayPoint(9) + HarpRig.Normal(9) * d + new Vector3(0f, 0f, -zRh);

        [Test]
        public void ACrossingFingerHoldsTheStringUntilItSlipsOff()
        {
            var h = new HarpRig();
            HarpString s = h.Play.Strings[9];
            Pointer p = h.Finger;
            h.Move(p, At9(-0.014f), At9(-0.004f));
            h.Move(p, At9(-0.004f), At9(0.003f));
            MeshData mesh = HarpGeometry.StringMesh(9, out _, out _);
            Vector3[] rest = mesh.Positions.ToArray(), shape = new Vector3[rest.Length];
            HarpPlay.Deform(s, rest, shape, rest.Length);
            float off = 0f;
            for (int k = 0; k < rest.Length; k++) off = Mathf.Max(off, new Vector2(shape[k].x - rest[k].x, shape[k].z - rest[k].z).magnitude);
            Assert.IsTrue(s.HeldBy != null && off >= 0.002f && off <= 0.0065f && h.Sound.Plucks.Count == 0, $"crossingHoldsStringSilently (bend {off * 1000f:F2} mm)");
            // The reference check steps to exactly CATCH_MAX (6 mm) here, which sits on the slip threshold to the last bit of
            // rounding; 5.9 mm keeps the check's meaning (still held just before the slip).
            h.Move(p, At9(0.003f), At9(0.0059f));
            Assert.IsTrue(s.HeldBy != null && h.Sound.Plucks.Count == 0, "stillHeldBeforeSlip");
            h.Move(p, At9(0.0059f), At9(0.011f));
            Assert.IsTrue(h.Sound.Plucks.Count == 1 && h.Sound.Plucks[0].String == 9 && s.HeldBy == null, "slipReleasesSound");
            // E8: the warm flash is a quarter of the original (the blur shows the pluck): 0.8 x 0.25 of the way to the glow colour.
            Assert.IsTrue(s.Amp > 0.001f && s.Flash > 0.5f && h.Play.Glow(s).r > 0.15f, "immediateFeedback");

            // The ringing (E8): the string blurs to BOTH sides at once, the envelope of its motion, instead of swinging to
            // either side on successive frames (a 45 Hz strobe).
            int mid = 8 * 6;
            var ups = new List<float>();
            for (int k = 0; k < 4; k++)
            {
                h.UpdateEmpty();
                HarpPlay.DeformEdge(s, rest, shape, rest.Length, true);
                float up = -(shape[mid].x - rest[mid].x);
                HarpPlay.DeformEdge(s, rest, shape, rest.Length, false);
                float lo = -(shape[mid].x - rest[mid].x);
                Assert.IsTrue(up * lo < 0f && Mathf.Abs(up) > 0.0005f && Mathf.Abs(lo) > 0.0005f,
                    $"ringBlurs: frame {k} edges {up * 1000f:F2} / {lo * 1000f:F2} mm, both sides at once");
                ups.Add(Mathf.Abs(up) + Mathf.Abs(lo));
            }
            Assert.That(ups[ups.Count - 1], Is.LessThan(ups[0]), "and it narrows as the note dies");
            float a0 = s.Amp;
            int frames = Mathf.RoundToInt(72f * s.Ring / 2f);
            for (int k = 0; k < frames; k++) h.UpdateEmpty();
            Assert.That(s.Amp, Is.LessThan(a0 * 0.1f), "ringDecaysWithSound");
        }

        [Test]
        public void BackingOffAHeldStringReturnsItSilently()
        {
            var h = new HarpRig();
            Pointer q = h.Finger;
            h.Move(q, At9(-0.014f), At9(-0.004f));
            h.Move(q, At9(-0.004f), At9(0.002f));
            bool held = h.Play.Strings[9].HeldBy != null;
            h.Move(q, At9(0.002f), At9(-0.006f));
            h.Move(q, At9(-0.006f), At9(-0.02f));
            Assert.IsTrue(held && h.Play.Strings[9].HeldBy == null && h.Sound.Plucks.Count == 0, "backOffSilent");
        }

        [Test]
        public void LiftingOffAHeldStringSoundsSofterThanASlip()
        {
            var h = new HarpRig();
            Pointer w = h.Finger;
            h.Move(w, At9(-0.014f), At9(-0.004f));
            h.Move(w, At9(-0.004f), At9(0.004f));
            h.Move(w, At9(0.004f), At9(0.004f, 0.03f));
            Assert.IsTrue(h.Sound.Plucks.Count == 1 && h.Sound.Plucks[0].Vel < 0.35f, "liftOffSoundsSofter");
        }

        // ---------------- 4. glissando, the zone, no double plucks ----------------

        static readonly Vector3[] Band = { new Vector3(-0.65f, 1.0f, 0f), new Vector3(-0.02f, 1.0f, 0f) };

        static List<FakeHarpSound.PluckCall> Run(Vector3[] pts, float zRh, float speed = 1.0f)
        {
            var h = new HarpRig();
            h.Sweep(h.Finger, pts, -zRh, speed / 72f);
            return h.Sound.Plucks;
        }

        [Test]
        public void AGlissandoPlaysEachStringOnceInOrderAndSpreadInTime()
        {
            int[] all = Enumerable.Range(0, 22).ToArray();
            int[] up = Run(Band, 0.005f).Select(c => c.String).ToArray();
            int[] down = Run(Band.Reverse().ToArray(), -0.008f).Select(c => c.String).ToArray();
            Assert.That(up, Is.EqualTo(all), "glissandoEachStringOnceInOrder (up)");
            Assert.That(down, Is.EqualTo(all.Reverse().ToArray()), "glissandoEachStringOnceInOrder (down)");
            double[] t = Run(Band, 0.005f, 1.4f).Select(c => c.Sounds).ToArray();
            bool ordered = true;
            for (int k = 1; k < t.Length; k++) if (t[k] < t[k - 1]) ordered = false;
            Assert.IsTrue(ordered && t[t.Length - 1] > t[0], "glissandoSpreadInTime");
        }

        [Test]
        public void ASweepWithin1cmPlaysAllAnd2cmOffPlaysNone()
        {
            Assert.That(Run(Band, 0.01f).Count, Is.EqualTo(22), "sweepWithin1cmPlaysAll");
            Assert.That(Run(Band, 0.02f).Count, Is.EqualTo(0), "sweep2cmOffPlaysNone");
            Assert.That(Run(Band, 0.004f, 0.3f).Count, Is.EqualTo(22), "slowSweepPlaysAll (0.3 m/s)");
        }

        /// <summary>The check's rnd(): Park-Miller from seed 7, centred on 0.</summary>
        sealed class Lcg
        {
            double _seed = 7;
            public float Next() { _seed = (_seed * 16807) % 2147483647; return (float)(_seed / 2147483647 - 0.5); }
        }

        [Test]
        public void ALingeringFingerNeverPlucksTwice()
        {
            var rnd = new Lcg();
            var h = new HarpRig();
            Vector3 pl = PlayPoint(9);
            Pointer p = h.Finger;
            Vector3 Z(float zRh) => new Vector3(pl.x, pl.y, -zRh);
            h.Move(p, Z(-0.03f), Z(-0.012f));
            h.Move(p, Z(-0.012f), Z(0.01f));
            Vector3 prev = Z(0.01f);
            for (int i = 0; i < 72; i++)
            {
                var c = new Vector3(pl.x + rnd.Next() * 0.003f, pl.y + rnd.Next() * 0.003f, -(0.01f + rnd.Next() * 0.003f));
                h.Move(p, prev, c);
                prev = c;
            }
            Assert.That(h.Sound.Plucks.Count, Is.EqualTo(1), "noDoublePluckWhenLingering");

            // A finger that crossed a string and lingers on it, trembling, never slips it off: no sound.
            var g = new HarpRig();
            Pointer q = g.Finger;
            g.Move(q, At9(-0.014f, 0.003f), At9(-0.004f, 0.003f));
            g.Move(q, At9(-0.004f, 0.003f), At9(0.002f, 0.003f));
            prev = At9(0.002f, 0.003f);
            for (int i = 0; i < 72; i++)
            {
                Vector3 c = At9(0.0025f + rnd.Next() * 0.003f, 0.003f);
                g.Move(q, prev, c);
                prev = c;
            }
            Assert.That(g.Sound.Plucks.Count, Is.EqualTo(0), "noPluckWhileHolding");
        }

        [Test]
        public void APokeBetweenTwoStringsTakesOneAtMost()
        {
            foreach (int i in new[] { 20, 9, 1 })
            {
                var h = new HarpRig();
                Vector3 m = (PlayPoint(i) + PlayPoint(i + 1)) * 0.5f;
                h.Move(h.Finger, new Vector3(m.x, m.y, 0.02f), new Vector3(m.x, m.y, -0.02f));
                Assert.That(h.Sound.Plucks.Count, Is.LessThanOrEqualTo(1), "pokeBetweenStringsTakesOneAtMost between " + i + " and " + (i + 1));
            }
        }

        // ---------------- 5. palm damping ----------------

        [Test]
        public void AHandLaidOnRingingStringsDampsThem()
        {
            var h = new HarpRig();
            foreach (int i in new[] { 8, 9, 10 }) h.Play.PlaybackPluck(i, 0.7f, 0.35f, 0f, MusicRoom.Audio.PluckMode.Through);
            bool ringing = new[] { 8, 9, 10 }.All(i => h.Play.Strings[i].HasVoice);
            var palm = new List<Pointer>();
            int k = 0;
            foreach (int i in new[] { 8, 9, 10 })
            {
                Pointer p = h.Hand.Pointers[k++];
                Vector3 pl = PlayPoint(i);
                HarpRig.Still(p, new Vector3(pl.x, pl.y, 0.002f)); // right-handed z -0.002
                palm.Add(p);
            }
            double t0 = h.Play.NowMs;
            while (h.Play.NowMs - t0 < 200) h.Update(palm);
            for (int i = 0; i < 20; i++) h.Update(palm);
            Assert.IsTrue(ringing && new[] { 8, 9, 10 }.All(i => !h.Play.Strings[i].HasVoice && h.Play.Strings[i].Damped && h.Play.Strings[i].Amp < 0.0002f), "palmDamps");
        }

        // ---------------- 6. consistency: velocity from speed, every pluck lights, hover style ----------------

        static float? Poke(int i, float speed)
        {
            var h = new HarpRig();
            Vector3 pl = PlayPoint(i);
            float step = speed / 72f;
            Pointer p = h.Finger;
            Vector3 Z(float zRh) => new Vector3(pl.x, pl.y, -zRh);
            h.Move(p, Z(-step * 1.5f), Z(-step * 0.5f));
            h.Move(p, Z(-step * 0.5f), Z(step * 0.5f));
            h.Move(p, Z(step * 0.5f), Z(step * 1.5f));
            return h.Sound.Plucks.Count > 0 ? h.Sound.Plucks[0].Vel : (float?)null;
        }

        static float? Across(int i, float speed)
        {
            var h = new HarpRig();
            Vector3 pl = PlayPoint(i), n = HarpRig.Normal(i);
            float step = speed / 72f;
            Pointer p = h.Finger;
            Vector3 At(float k) => new Vector3(pl.x + n.x * step * k, pl.y + n.y * step * k, -0.003f);
            Vector3 prev = At(-1.5f);
            for (float k = -0.5f; k <= 2.5f; k += 1f) { Vector3 c = At(k); h.Move(p, prev, c); prev = c; }
            return h.Sound.Plucks.Count > 0 ? h.Sound.Plucks[0].Vel : (float?)null;
        }

        [Test]
        public void TheSameStrokeSpeedGivesTheSameVelocityOnAnyStringEitherStroke()
        {
            var same = Enumerable.Range(0, 5).Select(_ => Poke(9, 0.8f)).ToArray();
            var others = new[] { Poke(3, 0.8f), Poke(15, 0.8f), Poke(20, 0.8f), Across(9, 0.8f), Across(15, 0.8f) };
            Assert.That(same.All(v => v.HasValue && Mathf.Abs(v.Value - same[0].Value) < 1e-6f), Is.True, "sameSpeedSameVelocity (repeat)");
            Assert.That(others.All(v => v.HasValue && Mathf.Abs(v.Value - same[0].Value) < 1e-4f), Is.True,
                "sameSpeedSameVelocity (other strings, across): " + string.Join(", ", others.Select(v => v?.ToString("F4") ?? "none")) + " vs " + same[0]);
            Assert.That(same[0].Value, Is.EqualTo(HarpPlay.VelocityOf(0.8)).Within(1e-3f));
            float slow = Poke(9, 0.35f) ?? 0f, fast = Poke(9, 1.4f) ?? 0f;
            Assert.IsTrue(slow < same[0].Value && same[0].Value < fast, "fasterIsLouder");
        }

        [Test]
        public void EveryPluckOfAGlissandoLightsItsStringInTheSameFrame()
        {
            var h = new HarpRig();
            Pointer p = h.Finger;
            Vector3 prev = HarpRig.L3(-0.65f, 1.0f, 0.004f);
            int count = 0;
            bool lit = true;
            for (float x = -0.65f + 0.012f; x <= -0.02f; x += 0.012f)
            {
                Vector3 c = HarpRig.L3(x, 1.0f, 0.004f);
                int before = h.Sound.Plucks.Count;
                h.Move(p, prev, c);
                prev = c;
                for (int k = before; k < h.Sound.Plucks.Count; k++)
                {
                    count++;
                    HarpString s = h.Play.Strings[h.Sound.Plucks[k].String];
                    if (!(s.Flash > 0.5f && s.Amp > 0.001f)) lit = false;
                }
            }
            Assert.IsTrue(lit && count == 22, $"everyPluckLightsAtOnce ({count} plucks)");
        }

        [Test]
        public void HoverIsLikeTheKeys()
        {
            var play = new HarpPlay();
            Vector3 pl = PlayPoint(8);
            Assert.IsTrue(play.HoverPick(new Vector3(pl.x, pl.y, -0.03f), out int i1, out float u1, out float s1));
            Assert.IsTrue(play.HoverPick(new Vector3(pl.x, pl.y, -0.01f), out int i2, out _, out float s2));
            Assert.IsFalse(play.HoverPick(new Vector3(pl.x, pl.y, -0.06f), out _, out _, out _), "beyond 5 cm");
            Assert.That(i1, Is.EqualTo(8));
            Assert.That(i2, Is.EqualTo(8));
            Assert.That(s1, Is.EqualTo(0.4f).Within(1e-6f), "hoverLikeKeys at 3 cm");
            Assert.That(s2, Is.EqualTo(0.8f).Within(1e-6f), "hoverLikeKeys at 1 cm");
            StringLayout s = HarpGeometry.Strings[8];
            Assert.That(Vector3.Distance(Vector3.LerpUnclamped(s.A, s.B, u1), pl), Is.LessThan(0.002f), "the glow sits on the string at the tip");
            Assert.That(HarpPlay.HoverLength, Is.EqualTo(0.07f));
            Assert.That(HarpPlay.HoverWidth, Is.EqualTo(0.016f));
        }
    }
}
