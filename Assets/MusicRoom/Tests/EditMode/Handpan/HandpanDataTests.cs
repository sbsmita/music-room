// The handpan's data (handpan.md 8; platform.md 7.1, 7.4, 8.1-8.5): the lesson pack in the LessonFile schema and its content
// rules, Sunny's lines (text, clip, manifest, length, word count, brand words), every feature covered by a tip, every anchor
// resolving; and that each tip's text tells the truth about the real controls (the play logic or the sound).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Handpan;
using MusicRoom.Tests.Audio;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Handpan
{
    public class HandpanDataTests
    {
        [Serializable] class GuideLineFile { public GuideLine[] lines; }
        [Serializable] class GuideLine { public string id, text, spoken, file; public float seconds, lineSeconds; public int words; }

        static GuideLine[] Lines() => JsonUtility.FromJson<GuideLineFile>(Resources.Load<TextAsset>("GuideLines/Handpan").text).lines;

        [Test]
        public void TheLessonPackIsALessonFileThatKeepsTheContentRules()
        {
            var ta = Resources.Load<TextAsset>("LessonPacks/handpan");
            Assert.IsNotNull(ta, "Resources/LessonPacks/handpan.json");
            LessonFile f = JsonUtility.FromJson<LessonFile>(ta.text);
            Assert.That(f.lessons.Length, Is.GreaterThanOrEqualTo(3), "at least 3 lessons");
            Assert.AreEqual("handpan-first", f.lessons[0].id, "the guaranteed win first");
            var ids = new HashSet<string>();
            foreach (Lesson L in f.lessons)
            {
                Assert.IsTrue(ids.Add(L.id), "unique id " + L.id);
                Assert.IsTrue(L.id.StartsWith("handpan-"), "prefixed id");
                Assert.AreEqual("handpan", L.instrument);
                Assert.Less(L.seconds + L.leadSeconds + 3, 60f, L.id + " under a minute");
                float last = -1; var lastAt = new Dictionary<int, float>();
                foreach (LessonEvent e in L.events)
                {
                    Assert.That(e.targets.Length, Is.InRange(1, 2), "at most 2 targets per event");
                    float t = e.beat * L.secondsPerBeat;
                    if (last >= 0) Assert.That(t - last, Is.GreaterThanOrEqualTo(0.2f - 1e-4f), L.id + ": gap >= 0.2 s");
                    last = t;
                    foreach (LessonTarget g in e.targets)
                    {
                        Assert.That(g.i, Is.InRange(0, 8), "field");
                        Assert.AreEqual(HandpanSpec.Fields[g.i].Midi, g.midi, "midi of field " + g.i);
                        Assert.AreEqual(HandpanSpec.Fields[g.i].Note, g.note, "note of field " + g.i);
                        if (lastAt.TryGetValue(g.i, out float pt)) Assert.That(t - pt, Is.GreaterThanOrEqualTo(0.25f - 1e-4f), L.id + ": same-target repeat >= 0.25 s");
                        lastAt[g.i] = t;
                    }
                    StringAssert.StartsWith("Tap ", e.hint);
                }
            }
            float firstLen = f.lessons[0].seconds + f.lessons[0].leadSeconds + 3;
            Assert.That(firstLen, Is.InRange(12f, 21f), "lesson 1 is a 12-21 s win");
            Assert.AreEqual(3, f.counts.lessons);
            Assert.That(ta.text, Does.Contain("\"handpan\": 0.3"), "the handpan's travel 0.30 m (lead 1.667 s)");
        }

        [Test]
        public void EveryLineHasItsClipItsLengthAndKeepsTheRules()
        {
            GuideLine[] lines = Lines();
            var manifest = Resources.Load<TextAsset>("VoiceManifests/Handpan").text;
            var brands = new Regex(@"\b(meta|quest|oculus|horizon|apple|vision|google|android|unity|hang)\b", RegexOptions.IgnoreCase);
            foreach (GuideLine L in lines)
            {
                StringAssert.StartsWith("tip_handpan", L.id, "prefixed line id");
                AudioClip clip = Resources.Load<AudioClip>("Voice/" + L.id);
                Assert.IsNotNull(clip, L.id + ".wav");
                Assert.AreEqual(22050, clip.frequency, L.id + " 22.05 kHz");
                Assert.AreEqual(1, clip.channels, L.id + " mono");
                Assert.AreEqual(L.seconds, clip.length, 0.002, L.id + " length = the manifest");
                Assert.That(manifest, Does.Contain($"\"{L.id}\""), L.id + " in VoiceManifests/Handpan.json");
                int words = L.text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                Assert.That(words, Is.LessThanOrEqualTo(12), L.id + " <= 12 words");
                Assert.That(L.lineSeconds, Is.LessThan(3.6f), L.id + " < 3.6 s with the lead (sunny-tour 5)");
                Assert.IsFalse(brands.IsMatch(L.text) || brands.IsMatch(L.spoken), L.id + ": no brand words");
                if (L.text.Contains("handpan")) Assert.That(L.spoken, Does.Contain("hand pan"), "'handpan' is spoken as two words");
            }
            // one line per tip, the tip's text = the line's text, no orphan line
            Assert.AreEqual(HandpanTips.All().Length, lines.Length, "no orphan lines");
            foreach (TipSpec t in HandpanTips.All())
            {
                GuideLine L = lines.Single(x => x.id == t.Lines[0]);
                Assert.AreEqual(t.Title, L.text, t.Id + ": the tip's caption is its line");
            }
        }

        [Test]
        public void EveryFeatureIsCoveredAndEveryAnchorResolves()
        {
            var covered = new HashSet<string>(HandpanTips.All().SelectMany(t => t.Covers));
            FeatureSpec[] features = HandpanTips.Features();
            CollectionAssert.AreEquivalent(HandpanTips.FeatureIds, features.Select(f => f.Id), "Features() = FeatureIds");
            foreach (FeatureSpec f in features)
            {
                StringAssert.StartsWith("handpan.", f.Id);
                Assert.IsTrue(covered.Contains(f.Id) || f.Passive, f.Id + " is covered by a tip or passive with a reason");
                if (f.Passive) Assert.That(f.PassiveReason.Length, Is.GreaterThan(30), f.Id + " has a reason");
            }
            Assert.AreEqual(1, HandpanTips.All().Count(t => t.Class == TipClass.P2 && t.Trigger == TipTrigger.FirstArrival), "one P2 arrival tip");
            var anchors = new HashSet<string>(HandpanTips.Anchors());
            var ids = new HashSet<string>();
            foreach (TipSpec t in HandpanTips.All())
            {
                Assert.IsTrue(ids.Add(t.Id), "unique tip " + t.Id);
                StringAssert.StartsWith("tip_handpan_", t.Id);
                Assert.IsTrue(anchors.Contains(t.Anchor), t.Id + ": its anchor is in the descriptor's Anchors");
                Assert.IsTrue(HandpanAnchors.TryGet(t.Anchor, HandpanPlace.Default, out D3 w, out D3 up), t.Id + " anchor " + t.Anchor);
                Assert.That(up.Length, Is.EqualTo(1).Within(1e-6));
                Assert.That(w.Y, Is.InRange(0.6, 1.0), t.Id + ": the anchor is on or over the steel");
            }
            Assert.IsFalse(HandpanAnchors.TryGet("handpan.field.9", HandpanPlace.Default, out _, out _));
        }
    }

    public class HandpanBakeTests
    {
        [Test]
        public void TheBakedSteelKeepsItsSmoothnessAlpha()
        {
            // the metallic map carries the smoothness in its alpha (URP Lit _METALLICSPECGLOSSMAP): baked without HasAlpha the
            // importer drops it and the steel renders as a mirror (found in the phase-B shots)
            TextureBakeSpec ms = new HandpanTextures().Textures.Single(t => t.Name == HandpanTextures.SteelMs);
            Assert.IsTrue(ms.HasAlpha && ms.Linear, "steel_ms: linear, with alpha");
            Texture2D baked = Resources.Load<Texture2D>("Generated/" + HandpanTextures.SteelMs);
            if (baked == null) Assert.Ignore("not baked in this project (Tools/unity.sh bake)");
            Assert.IsTrue(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(baked.graphicsFormat), $"the imported steel_ms has an alpha channel ({baked.graphicsFormat})");
        }
    }

    /// <summary>Each tip's words against what the instrument really does.</summary>
    public class HandpanTipTruthTests
    {
        static Dictionary<string, object> s_vec;
        static Dictionary<string, object> Vec => s_vec ?? (s_vec = HandpanJson.O(HandpanJson.Load(Path.Combine(HandpanJson.ReferenceDir("Handpan"), "interaction-vectors.json"))));
        static List<HandpanEvent> Case(string name) => HandpanPlayTests.Run(Vec.Arr("cases").Select(HandpanJson.O).First(c => c.Text("name") == name));

        static float[] Render(int field, double vel, double u, double v, HandpanTech tech, int frames)
        {
            using var rig = new HandpanRig();
            rig.Strike(0, field, vel, u, v, tech);
            return rig.Render(frames);
        }

        /// <summary>Spectral centroid (Hz) of x[from, to) by a 4096-point DFT magnitude (test only).</summary>
        static double Centroid(float[] x, int from, int n = 8192)
        {
            double num = 0, den = 0;
            for (int k = 1; k < 400; k++)
            {
                double f = k * 48000.0 / n, re = 0, im = 0, w = 2 * Math.PI * k / n;
                for (int i = 0; i < n; i++) { double h = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n); re += x[from + i] * h * Math.Cos(w * i); im -= x[from + i] * h * Math.Sin(w * i); }
                double m = Math.Sqrt(re * re + im * im);
                num += f * m; den += m;
            }
            return num / den;
        }

        static double Goertzel(float[] x, int from, int n, double hz)
        {
            double w = 2 * Math.PI * hz / 48000, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
            for (int i = 0; i < n; i++) { double h = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n); double s = x[from + i] * h + c * s1 - s2; s2 = s1; s1 = s; }
            return Math.Sqrt(s1 * s1 + s2 * s2 - c * s1 * s2);
        }

        [Test]
        public void TapTheRoundNotesWithYourFingertips() =>
            Assert.That(Case("tap-A3").Any(e => e.Type == HandpanEventType.Strike && e.Tech == HandpanTech.Tip && e.Field == 1), "a fingertip tap on a note plays it");

        [Test]
        public void TapThenLiftQuicklySoTheNoteRings()
        {
            Assert.IsFalse(Case("press-0.15s").Any(e => e.Type == HandpanEventType.Damp), "lifted within 0.15 s: the note rings on");
            Assert.IsTrue(Case("press-0.5s").Any(e => e.Type == HandpanEventType.Damp && e.AfterStrike), "left still on the steel: it is muted");
            Assert.IsTrue(Case("lift-hint").Any(e => e.Type == HandpanEventType.Hint && e.Hint == "tip_handpan_lift"), "the play logic raises the tip");
        }

        [Test]
        public void TheDeepNoteInTheMiddleIsTheDing()
        {
            HandpanField ding = HandpanSpec.Fields[0];
            Assert.IsTrue(ding.Ding);
            Assert.AreEqual(0, ding.S, "in the middle");
            Assert.IsTrue(HandpanSpec.Fields.All(f => f.Midi >= ding.Midi), "the lowest note");
        }

        [Test]
        public void GentleTapsSoundSweeterHardTapsBrighter()
        {
            double soft = Centroid(Render(1, 0.3, 0.35, 0.2, HandpanTech.Tip, 48000), 2400), hard = Centroid(Render(1, 0.95, 0.35, 0.2, HandpanTech.Tip, 48000), 2400);
            Debug.Log($"[Handpan] A3 spectral centroid 50-220 ms: soft {soft:F0} Hz, hard {hard:F0} Hz");
            Assert.Greater(hard, soft * 1.03, "a hard tap is brighter");
        }

        [Test]
        public void TapNearADimplesEdgeForARicherNote()
        {
            float[] centre = Render(1, 0.6, 0, 0, HandpanTech.Tip, 24000), edge = Render(1, 0.6, 0.35, 0.35, HandpanTech.Tip, 24000);
            double Rich(float[] y) => (Goertzel(y, 480, 8192, 440) + Goertzel(y, 480, 8192, 660)) / Goertzel(y, 480, 8192, 220);
            Debug.Log($"[Handpan] A3 overtones / fundamental: centre {Rich(centre):F2}, dimple edge {Rich(edge):F2}");
            Assert.Greater(Rich(edge), Rich(centre) * 1.5, "the dimple's edge brings the octave and the fifth forward");
        }

        [Test]
        public void RestYourHandOnTheSteelToQuietIt()
        {
            Assert.IsTrue(Case("palm-rest").Any(e => e.Type == HandpanEventType.Damp && e.Kind == HandpanDampSource.Palm && e.Field == 0), "a resting palm mutes");
            Assert.IsTrue(Case("too-slow").Any(e => e.Type == HandpanEventType.Damp && e.Kind == HandpanDampSource.Finger), "a resting finger mutes");
            using var rig = new HandpanRig();
            rig.Strike(0, 4, 0.7, 0.35, 0.2);
            rig.Damp(24000, 4, 3, 1, 0, 0, 1);
            float[] y = rig.Render(36000);
            Assert.Less(HandpanRig.RmsDb(y, 30000, 36000), HandpanRig.RmsDb(y, 18000, 24000) - 20, "and the sound stops");
        }

        [Test]
        public void SlapTheMiddleWithAFlatHandForABoom()
        {
            Assert.IsTrue(Case("ding-slap").Count(e => e.Type == HandpanEventType.Strike && e.Tech == HandpanTech.Slap && e.Field == 0) == 1, "a flat hand on the ding is one slap");
            float[] slap = Render(0, 0.7, 0, 0, HandpanTech.Slap, 24000), tip = Render(0, 0.7, 0.35, 0.2, HandpanTech.Tip, 24000);
            double Boom(float[] y) => Goertzel(y, 0, 8192, HandpanMath.HelmholtzHz()) / Goertzel(y, 0, 8192, 146.83);
            Debug.Log($"[Handpan] ding cavity / fundamental: slap {Boom(slap):F2}, fingertip {Boom(tip):F2}");
            Assert.Greater(Boom(slap), Boom(tip) * 3, "the slap pushes the air inside: the low boom");
        }

        [Test]
        public void TapBetweenTheNotesForASoftClick()
        {
            HandpanEvent t = Case("tak-between").Single(e => e.Type == HandpanEventType.Strike);
            Assert.AreEqual(HandpanSpec.Tak, t.Field);
            Assert.AreEqual(HandpanTech.Tak, t.Tech);
            using var rig = new HandpanRig();
            rig.Strike(0, HandpanSpec.Tak, 0.6, 0, 0, HandpanTech.Tak);
            float[] y = rig.Render(9600);
            double peak = -300;
            for (int a = 0; a + 240 <= 4800; a += 48) peak = Math.Max(peak, HandpanRig.RmsDb(y, a, a + 240));
            Debug.Log($"[Handpan] tak (shell + cavity): 5 ms RMS at 80 ms is {HandpanRig.RmsDb(y, 3840, 4080) - peak:F1} dB re its peak");
            Assert.Less(HandpanRig.RmsDb(y, 3840, 4080), peak - 40, "the click is down 40 dB at 80 ms (5 ms RMS re its peak, handpan.md 3.9)");
        }

        [Test]
        public void PlayOverYourLoopItsInTune()
        {
            var cMajor = new HashSet<int> { 0, 2, 4, 5, 7, 9, 11 };
            Assert.IsTrue(HandpanSpec.Fields.All(f => cMajor.Contains(f.Midi % 12)), "every note is a white key: in tune with the C-major room");
        }
    }
}
