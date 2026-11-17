// The hand drums' module-side data and logic (handdrums.md 4.5, 5.1, 5.2, 6.2 tests 14-15, review items 9-11): the lesson
// pack (schema, the content rules, lesson 1 a 12 s win, and the Tumbao's slap targets matched by real slaps at 2 m/s),
// Sunny's lines (text, voice clip, length, words, coverage of every feature, anchors), the demo hands (each one played
// through the stroke logic gives the stroke its tip teaches), the signals, the recorder mapping and playback (the recorded
// theta and rho kept), and the head dent against tools/visual.mjs's vectors.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.HandDrums;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.HandDrums
{
    public class HandDrumsModuleTests
    {
        // ---------------------------------------------------------------- 14. the lesson pack

        [Test]
        public void TheLessonPackFollowsThePlatformRules()
        {
            var pack = HandDrumsLessons.Load();
            Assert.That(pack, Is.Not.Null, "Resources/LessonPacks/handdrums.json");
            Assert.That(pack.lessons.Length, Is.GreaterThanOrEqualTo(3), "at least 3 lessons");
            var log = new StringBuilder("[HandDrums] lessons:");
            foreach (var l in pack.lessons)
            {
                Assert.That(l.id, Does.StartWith("handdrums-"), "ids carry the prefix");
                Assert.That(l.instrument, Is.EqualTo("handdrums"));
                Assert.That(l.seconds, Is.LessThan(60), l.id + " under a minute");
                Assert.That(l.events.Length, Is.EqualTo(l.eventCount));
                double prev = -1;
                var lastByHand = new Dictionary<string, double>();
                foreach (var e in l.events)
                {
                    Assert.That(e.targets.Length, Is.InRange(1, 2), "at most 2 targets per event");
                    foreach (var t in e.targets) Assert.IsTrue(HandDrumsLessons.Validate(t, out string why), $"{l.id} event {e.k}: {why}");
                    if (prev >= 0) Assert.That(e.time - prev, Is.GreaterThanOrEqualTo(0.2 - 1e-6), $"{l.id} event {e.k}: gap >= 0.2 s");
                    prev = e.time;
                    string hand = e.hint.Contains("left hand") ? "L" : e.hint.Contains("right hand") ? "R" : null;
                    if (hand != null && lastByHand.TryGetValue(hand, out double t0)) Assert.That(e.time - t0, Is.GreaterThanOrEqualTo(0.3 - 1e-6), $"{l.id} event {e.k}: the same hand >= 0.3 s apart");
                    if (hand != null) lastByHand[hand] = e.time;
                }
                log.Append($" {l.id} {l.bpm} bpm {l.seconds:F2} s {l.eventCount} events;");
            }
            var first = pack.lessons[0];
            Assert.That(first.seconds, Is.InRange(12.0, 21.0), "lesson 1: 12-21 s at tempo (review item 11: 80 bpm, 12.0 s)");
            Assert.That(first.bpm, Is.EqualTo(80));
            Assert.IsTrue(first.events.All(e => e.targets.Length == 1 && e.targets[0].zone == "any"), "lesson 1 is the guaranteed win: any stroke, one target at a time");
            Debug.Log(log.ToString());
        }

        [Test]
        public void TheTumbaoSlapTargetsMatchRealSlapsAtTwoMetresPerSecond()
        {
            var tumbao = HandDrumsLessons.Load().lessons.First(l => l.id == "handdrums-tumbao");
            var slaps = tumbao.events.SelectMany(e => e.targets).Where(t => t.zone == "slap").ToList();
            Assert.That(slaps.Count, Is.EqualTo(4), "one slap per bar");
            int hit = 0, n = 0;
            var kinds = new Dictionary<string, int>();
            foreach (var (app, tr) in Scripted.CaseRates)
                for (int k = 0; k < 10; k++)
                {
                    var c = new Scripted.Case { Id = "slap", Seconds = 0.6, Hands = { [1] = Scripted.HandScript.Of(Scripted.Motion(Scripted.StrokeKeys(0.1 + k * 0.0013, Scripted.SLAP, 2.0, 0.13, -0.02))) } };
                    var ev = Scripted.Events(Scripted.Run(c, app, tr, 0.002, 0, (uint)(4000 + k))).FindAll(e => e.Op == HdOp.Stroke);
                    n++;
                    if (ev.Count != 1) { kinds["none/double"] = kinds.TryGetValue("none/double", out int q) ? q + 1 : 1; continue; }
                    var te = HandDrumsTake.FromStroke(ev[0]);
                    kinds[te.zone] = kinds.TryGetValue(te.zone, out int z) ? z + 1 : 1;
                    if (slaps.All(t => HandDrumsLessons.Matches(t, te))) hit++;
                }
            Debug.Log($"[HandDrums] tumbao slap targets vs scripted slaps at 2 m/s (sigma 2 mm, 4 rate pairs): {hit}/{n} match; {string.Join(", ", kinds.Select(kv => kv.Key + " " + kv.Value))}");
            Assert.That(hit / (double)n, Is.GreaterThanOrEqualTo(0.9), "slaps at natural speed match the Tumbao's slap targets");
        }

        [Test]
        public void TargetsMatchTheirZonesAndLandOnTheSkin()
        {
            var open = new HdTarget { kind = "conga", zone = "open", label = "Open" };
            Assert.IsTrue(HandDrumsLessons.Matches(open, new TakeEvent { type = "handDrum", kind = "conga", zone = "mute" }), "open takes mute");
            Assert.IsFalse(HandDrumsLessons.Matches(open, new TakeEvent { type = "handDrum", kind = "tumba", zone = "open" }), "the other drum");
            Assert.IsTrue(HandDrumsLessons.Matches(new HdTarget { kind = "tumba", zone = "slap", label = "Slap" }, new TakeEvent { type = "handDrum", kind = "tumba", zone = "closed" }), "slap takes closed");
            Assert.IsTrue(HandDrumsLessons.Matches(new HdTarget { kind = "tumba", zone = "any", label = "Tap" }, new TakeEvent { type = "handDrum", kind = "tumba", zone = "tip" }), "any takes all");
            Assert.IsFalse(HandDrumsLessons.Matches(open, new TakeEvent { type = "handDrumMute", kind = "conga", zone = "fingers" }), "a rest is not a hit");
            foreach (var zone in HandDrumsLessons.TargetZones)
                for (int d = 0; d < 2; d++)
                {
                    var t = new HdTarget { kind = HandDrumsLessons.Kinds[d], zone = zone, label = zone };
                    Assert.IsTrue(HandDrumsLessons.TryGetLanding(t, null, out Pose p, out Vector3 approach, out Vector2 size));
                    Vector3 c = HandDrumsDims.HeadCentre[d], n = HandDrumsDims.Normal;
                    float h = Vector3.Dot(p.position - c, n), rho = Vector3.ProjectOnPlane(p.position - c, n).magnitude / HandDrumsDims.A[d];
                    Assert.That(h, Is.InRange(0f, 0.005f), "on the skin");
                    Assert.That(rho, Is.LessThan(0.8f), "inside the head");
                    Assert.That(Vector3.Dot(approach, n), Is.LessThan(-0.99f), "the block comes down along -n");
                }
        }

        // ---------------------------------------------------------------- 15. Sunny's lines, coverage, anchors

        [Serializable] class GuideLinesFile { public GuideLine[] lines; }
        [Serializable] class GuideLine { public string id, text, spoken; public int words; }

        [Test]
        public void EveryTipHasItsLineVoiceAndCoverage()
        {
            var gl = JsonUtility.FromJson<GuideLinesFile>(Resources.Load<TextAsset>("GuideLines/HandDrums").text);
            // the platform's manifest shape ({"<id>": {file, seconds, rate}}), read by the Guide's own reader
            var man = MusicRoom.Guide.ManifestJson.Parse(Resources.Load<TextAsset>("VoiceManifests/HandDrums").text);
            var brand = new[] { "meta", "quest", "oculus", "apple", "google" };
            var log = new StringBuilder("[HandDrums] Sunny:");
            foreach (var tip in HandDrumsGuide.Tips)
            {
                Assert.That(tip.Id, Does.StartWith("tip_handdrums_"));
                var line = gl.lines.FirstOrDefault(q => q.id == tip.Line);
                int mi = man.FindIndex(q => q.id == tip.Line);
                Assert.That(line, Is.Not.Null, tip.Line + " in GuideLines/HandDrums.json");
                Assert.That(mi, Is.GreaterThanOrEqualTo(0), tip.Line + " in VoiceManifests/HandDrums.json");
                var m = man[mi].v;
                Assert.That(m.File, Is.EqualTo(tip.Line + ".wav"));
                Assert.That(m.Rate, Is.EqualTo(22050));
                int words = line.text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                Assert.That(words, Is.LessThanOrEqualTo(10), tip.Line + ": <= 10 words");
                Assert.IsFalse(brand.Any(b => line.text.ToLowerInvariant().Contains(b)), tip.Line + ": no brand words");
                Assert.That(line.spoken, Is.EqualTo(line.text), "no all-caps labels: spoken = text");
                var clip = Resources.Load<AudioClip>("Voice/" + tip.Line);
                Assert.That(clip, Is.Not.Null, tip.Line + ".wav in Resources/Voice");
                Assert.That(clip.length, Is.LessThanOrEqualTo(4.0f), tip.Line + ": <= 4.0 s");
                Assert.That(clip.length, Is.EqualTo(m.Seconds).Within(0.02f), tip.Line + ": the manifest's length");
                Assert.That(clip.length, Is.LessThanOrEqualTo(3.6f), tip.Line + ": the aim, <= 3.6 s");
                if (tip.Anchor != null) Assert.IsTrue(HandDrumsGuide.TryGetAnchor(null, tip.Anchor, out _, out _), tip.Anchor + " resolves");
                log.Append($" {tip.Line.Substring(14)} {clip.length:F2} s;");
            }
            foreach (var f in HandDrumsGuide.Features)
            {
                bool covered = HandDrumsGuide.Tips.Any(t => t.Covers.Contains(f)), passive = HandDrumsGuide.Passive.Any(p => p.feature == f);
                Assert.IsTrue(covered ^ passive, f + ": covered by a tip, or passive with a reason");
            }
            Assert.That(HandDrumsGuide.Tips.Count(t => t.Trigger == HdTipTrigger.FirstArrival && t.Class == HdTipClass.P2), Is.EqualTo(1), "one P2 arrival tip");
            foreach (var a in HandDrumsGuide.Anchors)
            {
                Assert.IsTrue(HandDrumsGuide.TryGetAnchor(null, a, out _, out Vector3 up), a);
                Assert.That(up.normalized.y, Is.GreaterThan(0.9f), a + ": up along the head normal");
                Assert.That(up.magnitude, Is.InRange(0.14f, 0.16f), a + ": |up| the head's radius (the presets' rim)");
            }
            Debug.Log(log.ToString());
        }

        /// <summary>Each demo hand, run through the stroke logic at 72 Hz, plays what its tip teaches.</summary>
        [Test]
        public void EveryDemoHandPlaysTheStrokeItsTipTeaches()
        {
            var expect = new Dictionary<HdDemo, Func<List<HdEvent>, bool>>
            {
                [HdDemo.OpenTone] = ev => Strokes(ev) == "open,open",
                [HdDemo.PalmBass] = ev => Strokes(ev) == "bass,bass",
                [HdDemo.CuppedSlap] = ev => Strokes(ev) == "slap,slap",
                [HdDemo.FingersRest] = ev => Strokes(ev) == "open" && ev.Any(e => e.Op == HdOp.Stroke && e.Label == HdLabel.Mute) && ev.Any(e => e.Op == HdOp.Rest),
                [HdDemo.HeelToe] = ev => Strokes(ev) == "heel,toe",
                [HdDemo.PalmRest] = ev => Strokes(ev) == "" && ev.Any(e => e.Op == HdOp.Rest),
                [HdDemo.Roll] = ev => Strokes(ev) == string.Join(",", Enumerable.Repeat("open", 12)),
                [HdDemo.SoftLoud] = ev => { var s = ev.FindAll(e => e.Op == HdOp.Stroke); return s.Count == 2 && s[0].U < 0.3 && s[1].U > 0.65; },
            };
            var log = new StringBuilder("[HandDrums] demo hands through the stroke logic:");
            foreach (var kv in expect)
            {
                int drum = kv.Key == HdDemo.PalmRest || kv.Key == HdDemo.Roll ? 1 : 0;
                var input = new HandDrumsInput();
                for (int d = 0; d < 2; d++) input.Heads[d] = HdHead.Make(D3.From(HandDrumsDims.HeadCentre[d]), D3.From(HandDrumsDims.Normal), HandDrumsDims.A[d]);
                var keys = new[] { HandDrumsDemos.Keys(kv.Key, 0), HandDrumsDemos.Keys(kv.Key, 1) };
                var s = new[] { new HdHandSample(), new HdHandSample() };
                for (int i = 0; i * (1.0 / 72) <= HandDrumsDemos.Loop; i++)
                {
                    float t = (float)(i / 72.0);
                    for (int h = 0; h < 2; h++)
                    {
                        if (!HandDrumsDemos.At(keys[h], t, out var k)) { s[h].Tracked = false; continue; }
                        HandDrumsDemos.Pose(drum, h, k, s[h]);
                        s[h].SampleTime = t; s[h].NewSample = true;
                    }
                    input.Step(t, keys[0] != null ? s[0] : null, keys[1] != null ? s[1] : null, 1.0 / 72);
                }
                var ev = Scripted.Events(input);
                log.Append($" {kv.Key}: [{string.Join(" ", ev.Select(e => Scripted.Op(e.Op) + (e.Op == HdOp.Stroke ? ":" + Scripted.Kind(e.Kind) + (e.Label != HdLabel.None ? "(" + Scripted.Label(e.Label) + ")" : "") + $" u{e.U:F2}" : "")))}];");
                Assert.IsTrue(kv.Value(ev), $"{kv.Key}: {string.Join(" ", ev.Select(e => Scripted.Op(e.Op) + ":" + Scripted.Kind(e.Kind)))}");
                // the ghost hand's 26 joints are all finite and on the drum's side of the room
                var j = new Vector3[26];
                HandDrumsDemos.At(keys[1] ?? keys[0], 0.5f, out var k0);
                HandDrumsDemos.Pose(drum, 1, k0, new HdHandSample(), j);
                Assert.IsTrue(j.All(p => !float.IsNaN(p.x) && p.y > 0.5f && p.y < 1.2f), kv.Key + ": 26 joints over the head");
            }
            Debug.Log(log.ToString());
        }

        static string Strokes(List<HdEvent> ev) => string.Join(",", ev.Where(e => e.Op == HdOp.Stroke).Select(e => Scripted.Kind(e.Kind)));

        // ---------------------------------------------------------------- signals (review item 10.3)

        [Test]
        public void SignalsTriggerTheTechniqueTipsOnce()
        {
            var got = new List<string>();
            var sig = new HandDrumsSignals(got.Add);
            double ms = 0;
            HdEvent Stroke(HdKind k, int hand = 1, double u = 0.5, double gap = 400) { ms += gap; return new HdEvent { Op = HdOp.Stroke, Kind = k, Hand = hand, U = u, TStar = ms }; }
            sig.Feed(Stroke(HdKind.Bass)); sig.Feed(Stroke(HdKind.Bass)); sig.Feed(Stroke(HdKind.Bass));
            Assert.That(got, Does.Contain(HandDrumsGuide.NeedOpen), "3 strokes and no open tone");
            sig.Feed(Stroke(HdKind.Open));
            Assert.That(got, Does.Contain(HandDrumsGuide.GotOpen));
            for (int i = 0; i < 6; i++) sig.Feed(Stroke(HdKind.Open));
            Assert.That(got, Does.Contain(HandDrumsGuide.NeedSlap), "10 strokes and no slap");
            sig.Feed(Stroke(HdKind.Slap));
            Assert.That(got, Does.Contain(HandDrumsGuide.NeedMute), "a slap and an open, no mute");
            sig.Feed(new HdEvent { Op = HdOp.Relabel, Label = HdLabel.Mute });
            Assert.That(got, Does.Contain(HandDrumsGuide.GotMute));
            sig.Feed(Stroke(HdKind.Heel)); sig.Feed(Stroke(HdKind.Toe, gap: 300));
            Assert.That(got, Does.Contain(HandDrumsGuide.GotHeelToe), "a toe within 1 s of a heel");
            for (int i = 0; i < 6; i++) sig.Feed(Stroke(HdKind.Open, i % 2, 0.5, 150));
            Assert.That(got, Does.Contain(HandDrumsGuide.GotRoll), "6 alternating strokes within 1.2 s");
            sig.Feed(Stroke(HdKind.Open, 1, 0.2)); sig.Feed(Stroke(HdKind.Open, 1, 0.8));
            Assert.That(got, Does.Contain(HandDrumsGuide.GotDynamics));
            // ringing twice without a rest, then a rest while ringing
            for (int e = 0; e < 2; e++) { for (int f = 0; f < 80; f++) sig.Tick(0.02f, 0, 1 / 72.0); sig.Tick(0, 0, 1 / 72.0); }
            Assert.That(got, Does.Contain(HandDrumsGuide.Ringing));
            for (int f = 0; f < 80; f++) sig.Tick(0.02f, 0, 1 / 72.0);
            sig.Feed(new HdEvent { Op = HdOp.Rest, Drum = 0 });
            Assert.That(got, Does.Contain(HandDrumsGuide.GotQuiet));
            Assert.That(got.Count, Is.EqualTo(got.Distinct().Count()), "each signal once");
            Debug.Log("[HandDrums] signals: " + string.Join(", ", got));
        }

        // ---------------------------------------------------------------- the recorder mapping and playback (items 9.7, 9.8)

        sealed class FakeSound : IHandDrumSound
        {
            public readonly List<string> Calls = new List<string>();
            public void Patch(int drum, int hand, HandPart part, float u, PatchShape shape, PatchPoint p0, PatchPoint p1, PatchPoint p2, int count, double when, float delay = 0f) => Calls.Add($"patch {drum} {hand} {part} {u:F3}");
            public void Stroke(int drum, int hand, HandStroke kind, double u, double thetaOffset, double when, float rhoOffset = 0f) => Calls.Add($"stroke {drum} {hand} {kind} {u:F3} th {thetaOffset:F3} rho {rhoOffset:F3}");
            public void Rest(int drum, int hand, RestPoint p0, RestPoint p1, RestPoint p2, int count, double when) => Calls.Add($"rest {drum} {hand}");
            public void RestCanonical(int drum, int hand, HandRest kind, double thetaOffset, double when) => Calls.Add($"restc {drum} {hand} {kind} th {thetaOffset:F3}");
            public void Lift(int drum, int hand, double when) => Calls.Add($"lift {drum} {hand}");
            public float Level(int drum) => 0;
        }

        [Test]
        public void RecordedStrokesPlayBackWhereTheyWerePlayed()
        {
            var c = Scripted.Cases().First(q => q.Id == "C04-mute");
            var ev = Scripted.Events(Scripted.Run(c, 72, 72));
            var takes = ev.Select(e => e.Op == HdOp.Stroke ? HandDrumsTake.FromStroke(e) : HandDrumsTake.FromRest(e)).ToList();
            Assert.That(takes[0].type, Is.EqualTo("handDrum")); Assert.That(takes[0].kind, Is.EqualTo("conga"));
            Assert.That(takes[0].zone, Is.EqualTo("mute"), "relabelled in the buffer (the module records 'open' at once, then the rest)");
            Assert.That(takes[1].type, Is.EqualTo("handDrumMute")); Assert.IsFalse(takes[1].open); Assert.That(takes[1].zone, Is.EqualTo("fingers"));
            Assert.That(takes[2].type, Is.EqualTo("handDrumMute")); Assert.IsTrue(takes[2].open);
            var fake = new FakeSound();
            foreach (var t in takes) Assert.IsTrue(HandDrumsTake.Play(t, fake, 0));
            Debug.Log("[HandDrums] C04 recorded -> " + string.Join(" | ", takes.Select(t => $"{t.type} {t.zone} pos {t.pos:F3} u {t.u:F3} vel {t.vel:F2}")) + " -> playback: " + string.Join(" | ", fake.Calls));
            double theta = (takes[0].u - 0.5) * 2 * Math.PI;
            Assert.That(fake.Calls[0], Does.StartWith($"stroke 0 1 Mute {takes[0].vel:F3} th {theta:F3} rho {Math.Max(-0.3, Math.Min(0.3, takes[0].pos - 0.735)):F3}"), "the canonical muted stroke at the recorded theta and rho");
            Assert.That(fake.Calls[1], Does.StartWith("restc 0 1 Mute"));
            Assert.That(fake.Calls[2], Is.EqualTo("lift 0 1"));
            // a rho offset moves the canonical points (and 0 keeps them exactly: the vectors stay bit-exact)
            using var h0 = new HandDrumsOffline(); using var h1 = new HandDrumsOffline();
            h0.Sound.Stroke(0, 1, HandStroke.Open, 0.7, 0, 0); h1.Sound.Stroke(0, 1, HandStroke.Open, 0.7, 0, 0, -0.2f);
            float[] a = h0.Render(4800)[0], b = h1.Render(4800)[0];
            Assert.That(NullDb(a, b), Is.GreaterThan(-40), "a stroke 0.2 inward sounds different");
            HandDrumsTake.ReleaseAll(fake);
            Assert.That(fake.Calls.Count(q => q.StartsWith("lift")), Is.EqualTo(5));
        }

        static double NullDb(float[] y, float[] r) { double e = 0, s = 0; for (int i = 0; i < r.Length; i++) { double d = y[i] - r[i]; e += d * d; s += (double)r[i] * r[i]; } return 10 * Math.Log10(e / s); }

        // ---------------------------------------------------------------- review item 9.5: the back-of-hand axis

        /// <summary>The spec's back axis, normalize(rotation[Palm] * up), is the opposite of HandMath.Palm's normal (out of the
        /// palm, from the joint positions), on both hands in several orientations; and FromFrame reads a HandFrame as meant.</summary>
        [Test]
        public void TheBackAxisAgreesWithThePalmNormal()
        {
            foreach (MusicRoom.Hands.HandSide side in new[] { MusicRoom.Hands.HandSide.Left, MusicRoom.Hands.HandSide.Right })
                foreach (var e in new[] { new Vector3(0, 0, 0), new Vector3(40, 30, 0), new Vector3(-70, 0, 20), new Vector3(10, 120, -35) })
                {
                    var pose = MusicRoom.Hands.HandPose.Open(side, new Vector3(0.1f, 0.8f, 0.4f), Quaternion.Euler(e));
                    var f = new MusicRoom.Hands.HandFrame(side) { isTracked = true, isHighConfidence = true, newSample = true, sampleTime = 1.5 };
                    for (int i = 0; i < MusicRoom.Hands.HandFrame.JointCount; i++) { f.position[i] = pose.Position[i]; f.rotation[i] = pose.Rotation[i]; }
                    for (int k = 0; k < 5; k++) f.fingerHighConfidence[k] = true;
                    var s = new HdHandSample();
                    s.FromFrame(f);
                    Assert.IsTrue(s.Tracked && s.HighConfidence && s.NewSample);
                    Assert.That(s.SampleTime, Is.EqualTo(1.5));
                    Assert.IsTrue(MusicRoom.Hands.HandMath.Palm(f, out _, out Vector3 normal));
                    float ang = Vector3.Angle(s.Back.ToVector3(), -normal);
                    Assert.That(ang, Is.LessThan(20f), $"{side} {e}: back vs -palm normal {ang:F1} deg");
                    Assert.That((s.Tip[1].ToVector3() - pose.Position[15]).magnitude, Is.LessThan(1e-6f), "middle tip is joint 15");
                }
            var lost = new MusicRoom.Hands.HandFrame(MusicRoom.Hands.HandSide.Left) { isTracked = true, poseInferred = true };
            var ls = new HdHandSample(); ls.FromFrame(lost);
            Assert.IsFalse(ls.Tracked, "an inferred pose never strikes (Wide Motion Mode)");
        }

        // ---------------------------------------------------------------- the head dent (handdrums.md 4.5; visual.mjs vectors)

        [Serializable] class VvFile { public VvDepth depth; public VvVertex[] vertices; public VvSpring[] spring; }
        [Serializable] class VvDepth { public double[] palm, heel, fingers, tips, @base, pads; public double[] this[int p] => new[] { palm, heel, fingers, tips, @base, pads }[p]; }
        [Serializable] class VvVertex { public string @case; public int ring, seg; public double dentM; }
        [Serializable] class VvSpring { public double t, depth; }

        [Test]
        public void TheHeadDentMatchesTheReference()
        {
            var vv = JsonUtility.FromJson<VvFile>(File.ReadAllText(Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/HandDrums/Reference~/visual-vectors.json")));
            double[] us = { 0.1, 0.5, 0.8, 1 };
            for (int p = 0; p < 6; p++) for (int i = 0; i < 4; i++)
                    Assert.That(HandDrumsDent.Depth((HandPart)p, us[i]), Is.EqualTo(vv.depth[p][i]).Within(vv.depth[p][i] * 1e-5), $"depth part {p} u {us[i]}");
            var bass = new[] { 0.10, 0.0 };
            var open = new[] { 0.97, 0.0, 0.813, 0.0, 0.657, 0.0, 0.5, 0.0 };
            foreach (var v in vv.vertices)
            {
                bool isBass = v.@case.StartsWith("bass");
                double rho = v.ring / 12.0, th = 2 * Math.PI * v.seg / 48;
                double got = isBass ? HandDrumsDent.At(rho, th, bass, 1, HandDrumsDent.Depth(HandPart.Palm, 0.8), 0.035, 0.146)
                                    : HandDrumsDent.At(rho, th, open, 4, HandDrumsDent.Depth(HandPart.Fingers, 0.8), 0.012, 0.146);
                Assert.That(got, Is.EqualTo(v.dentM).Within(Math.Abs(v.dentM) * 1e-5 + 1e-10), $"{v.@case} ring {v.ring} seg {v.seg}");
            }
            foreach (var s in vv.spring) Assert.That(HandDrumsDent.Spring(0.004, s.t), Is.EqualTo(s.depth).Within(s.depth * 1e-5 + 1e-12), "spring at " + s.t);
        }
    }
}
