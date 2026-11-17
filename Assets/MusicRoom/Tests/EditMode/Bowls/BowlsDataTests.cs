// The bowls' data (bowls.md 7; platform 7.4 and 8): the lesson pack loads and keeps the content rules and review item
// 11's hit rules; every tip has its line text, a rendered voice (22.05 kHz mono, <= 4.0 s, <= 12 words, no brand words)
// and a manifest entry; every feature is covered by a tip or marked passive; tip ids carry the prefix.
using System;
using System.IO;
using System.Linq;
using MusicRoom.Bowls;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Bowls
{
    public class BowlsDataTests
    {
        [Serializable] class Pack { public Lesson[] lessons; public Tut tutorial; }
        [Serializable] class Tut { public float SPEED; }
        [Serializable] class Lesson { public string id, instrument, title, listNote; public float bpm, seconds, leadSeconds; public Ev[] events; }
        [Serializable] class Ev { public float time, duration; public string hint; public Target[] targets; }
        [Serializable] class Target { public int i = -1, midi = -1; public string note, label, mode; public float hold; }
        [Serializable] class Lines { public Line[] lines; }
        [Serializable] class Line { public string id, text, file; public float seconds; public int rate; }

        static string Res(string rel) => Path.Combine(Application.dataPath, "MusicRoom/Bowls/Resources", rel);

        [Test]
        public void LessonPackLoadsAndKeepsTheRules()
        {
            var ta = Resources.Load<TextAsset>("LessonPacks/bowls");
            Assert.That(ta, Is.Not.Null, "Resources/LessonPacks/bowls.json");
            var p = JsonUtility.FromJson<Pack>(ta.text);
            Assert.That(p.lessons.Length, Is.GreaterThanOrEqualTo(3));
            foreach (var l in p.lessons)
            {
                Assert.That(l.id.StartsWith("bowls-"), Is.True, l.id);
                Assert.That(l.instrument, Is.EqualTo("bowls"));
                Assert.That(l.seconds + l.leadSeconds + 3, Is.LessThan(60), l.id + " under a minute");
                for (int k = 0; k < l.events.Length; k++)
                {
                    var e = l.events[k];
                    Assert.That(e.targets.Length, Is.InRange(1, 2));
                    Assert.That(e.hint.Length, Is.LessThanOrEqualTo(21), e.hint);
                    foreach (var t in e.targets)
                    {
                        Assert.That(t.i, Is.InRange(0, 2), "bowl index");
                        Assert.That(t.mode == "strike" || t.mode == "rub", Is.True, "mode");
                        Assert.That(t.midi, Is.EqualTo(BowlsLayout.Midi[t.i]));
                        if (t.mode == "rub") Assert.That(t.hold, Is.GreaterThan(0f).And.LessThanOrEqualTo(e.duration), "hold fits the event");
                    }
                    if (k > 0) Assert.That(e.time - l.events[k - 1].time, Is.GreaterThanOrEqualTo(0.2f), "gap");
                }
            }
            var first = p.lessons[0];
            Assert.That(first.events.All(e => e.targets.Length == 1), Is.True, "the first lesson is one target at a time");
            Assert.That(first.seconds, Is.InRange(12f, 21f));
            // review item 11: the first lesson's targets come 1.5 or 3 s apart (its description says so now)
            for (int k = 1; k < first.events.Length; k++)
            {
                float gap = first.events[k].time - first.events[k - 1].time;
                Assert.That(Math.Abs(gap - 1.5f) < 1e-3 || Math.Abs(gap - 3f) < 1e-3, Is.True, $"gap {gap}");
            }
            // the rub lesson starts on the nearest (big) bowl (review item 15)
            var sing = p.lessons.First(l => l.events.Any(e => e.targets[0].mode == "rub"));
            Assert.That(sing.events.First(e => e.targets[0].mode == "rub").targets[0].i, Is.EqualTo(0));
        }

        [Test]
        public void LessonHitRulesFollowReviewItem11()
        {
            Assert.That(BowlsLessonRules.MatchesStrike(1, 1, 0.25, true), Is.True, "a gentle tap counts");
            Assert.That(BowlsLessonRules.MatchesStrike(1, 1, 0.12, true), Is.False, "a near-silent touch-down tick does not");
            Assert.That(BowlsLessonRules.MatchesStrike(1, 0, 1.0, true), Is.False, "the wrong bowl does not");
            Assert.That(BowlsLessonRules.MatchesStrike(1, 1, 1.0, false), Is.False, "playback does not");
            Assert.That(BowlsLessonRules.MatchesRub(0, 3, 0, 3, true), Is.True);
            Assert.That(BowlsLessonRules.MatchesRub(0, 3, 0, 2, true), Is.False);
            Assert.That(BowlsLessonRules.StallState(5), Is.EqualTo(BowlsLessonRules.Stall.Waiting));
            Assert.That(BowlsLessonRules.StallState(8), Is.EqualTo(BowlsLessonRules.Stall.Redemo));
            Assert.That(BowlsLessonRules.StallState(15), Is.EqualTo(BowlsLessonRules.Stall.AnyTouch));
            var touch = new BowlsHandEvent { Type = BowlsHandEventType.Touch, Bowl = 2 };
            Assert.That(BowlsLessonRules.AssistTouch(2, touch, 16), Is.True, "after 15 s any touch on the target bowl counts (and plays 0.7 m/s)");
            Assert.That(BowlsLessonRules.AssistTouch(2, touch, 10), Is.False);
            Assert.That(BowlsLessonRules.AssistTouch(1, touch, 16), Is.False, "not on another bowl");
        }

        /// <summary>The Guide's manifest shape: { "tip_x": { "file", "seconds", "rate" } }.</summary>
        static System.Collections.Generic.Dictionary<string, (string file, float seconds, int rate)> Manifest()
        {
            string text = Resources.Load<TextAsset>("VoiceManifests/Bowls").text;
            var d = new System.Collections.Generic.Dictionary<string, (string, float, int)>();
            var rx = new System.Text.RegularExpressions.Regex("\"(?<id>[^\"]+)\"\\s*:\\s*\\{\\s*\"file\"\\s*:\\s*\"(?<file>[^\"]+)\"\\s*,\\s*\"seconds\"\\s*:\\s*(?<s>[0-9.]+)\\s*,\\s*\"rate\"\\s*:\\s*(?<r>[0-9]+)\\s*\\}");
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(text))
                d[m.Groups["id"].Value] = (m.Groups["file"].Value, float.Parse(m.Groups["s"].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(m.Groups["r"].Value));
            return d;
        }

        [Test]
        public void EveryTipHasItsLineVoiceAndManifest()
        {
            var lines = JsonUtility.FromJson<Lines>(Resources.Load<TextAsset>("GuideLines/Bowls").text).lines;
            var man = Manifest();
            Assert.That(man.Count, Is.EqualTo(BowlsTips.All.Length), "one manifest entry per tip, in the Guide's shape");
            var brand = new System.Text.RegularExpressions.Regex(@"\b(meta|quest|oculus|apple|google|unity|tibetan)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (var tip in BowlsTips.All)
            {
                Assert.That(tip.Id.StartsWith("tip_bowls_"), Is.True, tip.Id);
                Assert.That(tip.TextOnly, Is.False, tip.Id + " has a voice");
                Assert.That(tip.Lines, Is.EqualTo(new[] { tip.Id }));
                var l = lines.FirstOrDefault(q => q.id == tip.Id);
                Assert.That(l, Is.Not.Null, tip.Id + " line text");
                Assert.That(l.text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length, Is.LessThanOrEqualTo(12), tip.Id + " words");
                Assert.That(brand.IsMatch(l.text), Is.False, tip.Id + " no brand words");
                Assert.That(man.TryGetValue(tip.Id, out var m), Is.True, tip.Id + " manifest");
                Assert.That(m.file, Is.EqualTo(tip.Id + ".wav"));
                Assert.That(m.rate, Is.EqualTo(22050));
                Assert.That(m.seconds, Is.LessThanOrEqualTo(4.0f), tip.Id + " seconds");
                var clip = Resources.Load<AudioClip>("Voice/" + tip.Id);
                Assert.That(clip, Is.Not.Null, tip.Id + " voice clip");
                Assert.That(clip.frequency, Is.EqualTo(22050)); Assert.That(clip.channels, Is.EqualTo(1));
                Assert.That(Math.Abs(clip.length - m.seconds), Is.LessThan(0.01f), tip.Id + " manifest seconds = the clip");
            }
            Assert.That(BowlsTips.All.Count(t => t.Trigger == TipTrigger.FirstArrival && t.Class == TipClass.P2 && t.Success == TipSuccess.EventType), Is.EqualTo(1), "one P2 arrival tip");
            Assert.That(BowlsTips.All.Any(t => t.Demo == DemoKind.FingerCircle), Is.True, "the rub demos circle a fingertip");
            Assert.That(BowlsTips.All.Any(t => t.ThenDemo == DemoKind.FingerCircle), Is.True, "tap, then circle: the two-step demo");
        }

        [Test]
        public void EveryFeatureIsCoveredOrPassive()
        {
            foreach (FeatureSpec f in BowlsTips.Features)
            {
                bool tip = BowlsTips.All.Any(t => t.Covers.Contains(f.Id));
                Assert.That(tip || f.Passive, Is.True, f.Id + " covered");
                Assert.That(f.Id.StartsWith("bowls."), Is.True);
            }
            // the task's list: tapping, rubbing, faster / slower, the stick, the lip, damping
            foreach (string f in new[] { "bowls.tap", "bowls.rub", "bowls.rubSpeed", "bowls.puja", "bowls.lip", "bowls.damp" })
                Assert.That(BowlsTips.All.Any(t => t.Covers.Contains(f)), Is.True, f + " has a tip");
            Assert.That(BowlsTips.All.Count(t => t.Covers.Contains("bowls.rubSpeed")), Is.EqualTo(2), "both faster and slower");
            foreach (string a in BowlsTips.All.SelectMany(t => new[] { t.Anchor, t.ThenAnchor }).Where(x => x != null))
                Assert.That(BowlsTips.Anchors, Does.Contain(a), a + " is listed in the descriptor's Anchors");
        }
    }
}
