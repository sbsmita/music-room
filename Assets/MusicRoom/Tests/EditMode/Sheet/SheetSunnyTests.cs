// Sunny covers the book (sheet.md 8; platform.md 8.5): every feature of the book is covered by a tip or passive with a
// reason; every tip's lines (and the desktop wordings) have a caption in GuideLines/Sheet.json, a 22.05 kHz mono WAV in
// Resources/Voice and a VoiceManifests/Sheet.json length that matches it; each line is at most 12 words and 4.0 s, names
// no brand, and says the real control ("Tap" in a headset, "Click" on a desktop; a page's bottom corner, the ribbon, a
// song's name). The triggers (SheetTips) offer each tip on its own condition, once, through the host.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Sheet;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Sheet
{
    public class SheetSunnyTests
    {
        static string Res => Path.Combine(SheetTestData.SheetDir, "Resources");

        static Dictionary<string, string> Captions()
        {
            var lines = SheetTestData.L(SheetTestData.O(MiniJson.Parse(File.ReadAllText(Path.Combine(Res, "GuideLines/Sheet.json"))))["lines"]);
            return lines.Select(SheetTestData.O).ToDictionary(o => (string)o["id"], o => (string)o["text"]);
        }

        [Test]
        public void EveryFeatureIsCoveredByATipOrPassive()
        {
            var covered = new HashSet<string>(SheetTipTable.Tips.Select(t => t.Covers));
            foreach (var (id, passive) in SheetTipTable.Features)
                Assert.That(covered.Contains(id) || !string.IsNullOrEmpty(passive), Is.True, $"feature {id} is taught by a tip or passive with a reason");
            foreach (var t in SheetTipTable.Tips) Assert.That(SheetTipTable.Features.Any(f => f.id == t.Covers), Is.True, $"{t.Id} covers a known feature");
            Assert.That(SheetTipTable.Tips.Select(t => t.Id), Is.EquivalentTo(new[] { "tip_book", "tip_book_marker", "tip_book_contents", "tip_book_turn", "tip_book_ribbon" }));
        }

        [Test]
        public void EveryLineHasACaptionAVoiceClipAndItsLength()
        {
            var captions = Captions();
            var manifest = SheetTestData.O(MiniJson.Parse(File.ReadAllText(Path.Combine(Res, "VoiceManifests/Sheet.json"))));
            var ids = new List<string>();
            foreach (var t in SheetTipTable.Tips) { ids.Add(t.Line); if (t.DeskLine != null) ids.Add(t.DeskLine); }
            Assert.That(captions.Keys, Is.EquivalentTo(ids), "one caption per line, no strays");
            Assert.That(manifest.Keys, Is.EquivalentTo(ids), "one manifest entry per line");
            string[] brands = { "meta", "quest", "oculus", "apple", "google", "yamaha", "steinway", "disney" };
            foreach (string id in ids)
            {
                string text = captions[id];
                int words = text.Split(' ').Length;
                Assert.That(words, Is.LessThanOrEqualTo(12), $"{id}: '{text}' is {words} words");
                foreach (var b in brands) Assert.That(text.ToLowerInvariant().Split(' ', '.', ',', '\'').Contains(b), Is.False, $"{id} names no brand");
                var m = SheetTestData.O(manifest[id]);
                double seconds = SheetTestData.D(m["seconds"]);
                Assert.That(seconds, Is.LessThanOrEqualTo(4.0), $"{id} at most 4 s");
                string wav = Path.Combine(Res, "Voice", (string)m["file"]);
                Assert.That(File.Exists(wav), Is.True, wav);
                var b2 = File.ReadAllBytes(wav);
                int channels = System.BitConverter.ToInt16(b2, 22), rate = System.BitConverter.ToInt32(b2, 24), bits = System.BitConverter.ToInt16(b2, 34);
                Assert.That(channels, Is.EqualTo(1), id); Assert.That(rate, Is.EqualTo(22050), id); Assert.That(bits, Is.EqualTo(16), id);
                double len = (b2.Length - 44) / 2.0 / rate;
                Assert.That(len, Is.EqualTo(seconds).Within(0.002), $"{id}: the manifest's length is the clip's");
                Assert.That(File.Exists(wav + ".meta") && File.ReadAllText(wav + ".meta").Contains("forceToMono: 1"), Is.True, $"{id} imported like Sunny's other lines");
            }
        }

        [Test]
        public void TheLinesSayTheRealControls()
        {
            var c = Captions();
            Assert.That(c["tip_book_turn"], Does.StartWith("Tap").And.Contain("bottom corner"), "the corner zones are the pages' bottom outer corners");
            Assert.That(c["tip_book_turn_desk"], Does.StartWith("Click").And.Contain("bottom corner"));
            Assert.That(c["tip_book_ribbon"], Does.StartWith("Tap").And.Contain("ribbon").And.Contain("song list"));
            Assert.That(c["tip_book_ribbon_desk"], Does.StartWith("Click").And.Contain("ribbon"));
            Assert.That(c["tip_book_contents"], Does.StartWith("Tap").And.Contain("song's name"));
            Assert.That(c["tip_book_contents_desk"], Does.StartWith("Click"));
            Assert.That(c["tip_book"], Does.Contain("letters match the keys"), "the letters are the key labels");
            Assert.That(c["tip_book_marker"], Does.Contain("warm mark").And.Contain("next note"));
        }

        sealed class FakeHost : ISheetHost
        {
            public readonly List<string> Tips = new List<string>();
            public float Answer = 2.5f;
            public Transform PianoRoot => null;
            public Pose MusicRestLocal => Pose.identity;
            public float MusicRestLedge => 0.0073f;
            public Pose? MusicRestPose => Pose.identity;
            public int CurrentEvent => -1;
            public void PaperTurn(float seconds, float level, uint seed, double when) { }
            public float Tip(string lineId) { Tips.Add(lineId); return Answer; }
            public void Signal(string signal) { }
            public double AudioNow => 0;
        }

        [Test]
        public void EachTipIsOfferedOnItsOwnConditionOnce()
        {
            var host = new FakeHost();
            var tips = new SheetTips(host, new AppSettings(new MemorySettingsStore()));
            const float dt = 0.1f;
            void Run(float s, bool inView, bool contents, TutorialState st, bool far = false, bool desk = false, bool tourDone = true)
            { for (float t = 0; t < s; t += dt) tips.Tick(dt, true, inView, tourDone, contents, st, true, far, desk); }
            Run(5f, true, true, TutorialState.Idle, tourDone: false);
            Assert.That(host.Tips, Is.Empty, "nothing before the tour is over");
            Run(1.0f, true, false, TutorialState.Idle);
            Assert.That(host.Tips, Is.Empty, "the book must be in view 1.5 s");
            Run(0.6f, true, false, TutorialState.Idle);
            Assert.That(host.Tips, Is.EqualTo(new[] { "tip_book" }));
            Run(2.1f, true, true, TutorialState.Idle);
            Assert.That(host.Tips.Last(), Is.EqualTo("tip_book_contents"), "the contents open and in view 2 s after tip_book");
            Run(0.5f, true, false, TutorialState.Countdown);
            Assert.That(host.Tips.Last(), Is.EqualTo("tip_book_marker"), "the first lesson countdown with the book in view");
            Run(0.5f, true, false, TutorialState.Done);
            Assert.That(host.Tips.Last(), Is.EqualTo("tip_book_turn"), "the first lesson done, no manual turn yet");
            Run(0.5f, true, false, TutorialState.Idle, far: true, desk: true);
            Assert.That(host.Tips.Last(), Is.EqualTo("tip_book_ribbon_desk"), "two spreads past the contents after a manual turn (desktop wording)");
            int n = host.Tips.Count;
            Run(30f, true, true, TutorialState.Done, far: true);
            Run(1f, true, false, TutorialState.Countdown);
            Assert.That(host.Tips.Count, Is.EqualTo(n), "each tip once");
            // a busy Sunny (0 s): offered again, but not before 20 s
            var busy = new FakeHost { Answer = 0f };
            var t2 = new SheetTips(busy, new AppSettings(new MemorySettingsStore()));
            for (float t = 0; t < 10f; t += dt) t2.Tick(dt, true, true, true, false, TutorialState.Idle, false, false, false);
            Assert.That(busy.Tips.Count, Is.EqualTo(1));
            for (float t = 0; t < 12f; t += dt) t2.Tick(dt, true, true, true, false, TutorialState.Idle, false, false, false);
            Assert.That(busy.Tips.Count, Is.EqualTo(2));
            // done what it teaches: no tip
            var s3 = new AppSettings(new MemorySettingsStore());
            var h3 = new FakeHost();
            var t3 = new SheetTips(h3, s3);
            t3.NoteManualTurn(); t3.NoteRibbon(); t3.NoteTitleStart();
            for (float t = 0; t < 5f; t += dt) t3.Tick(dt, true, true, true, true, TutorialState.Done, true, true, false);
            Assert.That(h3.Tips, Has.No.Member("tip_book_turn").And.No.Member("tip_book_ribbon").And.No.Member("tip_book_contents"));
        }
    }
}
