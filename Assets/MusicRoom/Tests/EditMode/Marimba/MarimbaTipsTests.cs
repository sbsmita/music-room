// Sunny on the marimba (spec 8, platform 8.1-8.5): every feature is covered by a tip, every tip's line exists as text,
// as a manifest entry and as a voice clip (22.05 kHz mono, <= 3.8 s: review item 18), lines are <= 10 words with no brand
// words, tip ids carry the prefix, and every literal anchor resolves.
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MusicRoom.Marimba;
using MusicRoom.Tests.Audio;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Marimba
{
    public class MarimbaTipsTests
    {
        static string Res => Path.Combine(Application.dataPath, "MusicRoom/Marimba/Resources");

        [Test]
        public void EveryFeatureIsCoveredByATip()
        {
            var covered = new HashSet<string>();
            foreach (var t in MarimbaTips.Specs()) foreach (var c in t.Covers) { Assert.That(MarimbaTips.Features, Does.Contain(c), t.Id + " covers an undeclared feature"); covered.Add(c); }
            foreach (var f in MarimbaTips.Features) Assert.That(covered.Contains(f), Is.True, f + " has no tip");
            foreach (var must in new[] { "marimba.mallets", "marimba.fingertips", "marimba.deadStroke", "marimba.handMute", "marimba.roll", "marimba.sharps" })
                Assert.That(MarimbaTips.Features, Does.Contain(must));
        }

        [Test]
        public void EveryLineHasTextManifestAndVoice()
        {
            var lines = MarimbaRef.Arr(MarimbaJson.Parse(File.ReadAllText(Path.Combine(Res, "GuideLines/Marimba.json"))), "lines");
            var manifest = MarimbaRef.Obj(MarimbaJson.Parse(File.ReadAllText(Path.Combine(Res, "VoiceManifests/Marimba.json"))));
            var text = new Dictionary<string, string>();
            foreach (var o in lines) text[(string)MarimbaRef.Obj(o)["id"]] = (string)MarimbaRef.Obj(o)["text"];
            var brand = new Regex(@"deagan|musser|yamaha|adams|majestic|malletech|vic firth|innovative percussion|meta|quest|oculus", RegexOptions.IgnoreCase);
            var ids = new HashSet<string>();
            foreach (var t in MarimbaTips.Specs())
            {
                Assert.That(t.Id.StartsWith("tip_marimba_"), Is.True, t.Id);
                Assert.That(ids.Add(t.Id), Is.True, "duplicate " + t.Id);
                Assert.That(t.TextOnly, Is.False, t.Id + " is voiced");
                string line = t.Lines[0];
                Assert.That(text.TryGetValue(line, out string s), Is.True, line + " text");
                Assert.That(s, Is.EqualTo(t.Title), line + ": the caption is the tip's title");
                Assert.That(s.Split(' ').Length, Is.LessThanOrEqualTo(10), line + " words");
                Assert.That(brand.IsMatch(s), Is.False, line + " brand words");
                Assert.That(manifest.ContainsKey(line), Is.True, line + " manifest");
                double sec = MarimbaRef.Num(manifest[line], "seconds");
                Assert.That(sec, Is.LessThanOrEqualTo(3.8), line + " seconds");
                Assert.That((string)MarimbaRef.Obj(manifest[line])["file"], Is.EqualTo(line + ".wav"));
                var clip = Resources.Load<AudioClip>("Voice/" + line);
                Assert.That(clip, Is.Not.Null, line + " clip");
                Assert.That(clip.channels, Is.EqualTo(1), line);
                Assert.That(clip.length, Is.EqualTo((float)sec).Within(0.01f), line + " clip vs manifest");
                if (!string.IsNullOrEmpty(t.Anchor)) Assert.That(MarimbaAnchors.TryGetLocal(t.Anchor, 0.72f, out _, out _), Is.True, t.Id + " anchor " + t.Anchor);
                Debug.Log($"[MARIMBA] tip {t.Id,-22} {sec,5:F2} s  \"{s}\"  {t.Trigger} {t.TriggerArg} covers {string.Join(", ", t.Covers)}");
            }
            Assert.That(text.Count, Is.EqualTo(MarimbaTips.Specs().Length));
        }
    }
}
