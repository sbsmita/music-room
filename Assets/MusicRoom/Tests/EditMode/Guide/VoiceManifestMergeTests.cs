// The Guide's side of the per-module voice lines (platform.md 8.2; 12.1 VoiceManifestMergeTests): every
// Resources/GuideLines/*.json merges after the Guide's own 38 lines (which stay GuideVoice.LineIds, in order), every
// Resources/VoiceManifests/*.json has exactly the shape of Guide/Resources/Voice/manifest.json and names a 22.05 kHz mono
// WAV in Resources/Voice of the measured length; a duplicate id (between modules, or with one of the 38) fails: it is
// logged and the later one dropped.
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MusicRoom.Audio.Synths;
using MusicRoom.Guide;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.Guide
{
    public class VoiceManifestMergeTests
    {
        static readonly string[] Platform = { "tip_bring_grid", "tip_summon_far", "tip_my_set", "tip_set_full", "tip_put_away", "tip_page_turn" };

        [Test]
        public void EveryModulesLinesLoadAfterTheGuidesOwnThirtyEight()
        {
            GuideLines.ReloadMerged();
            GuideLineTable m = GuideLines.Merged;
            Assert.That(m.Errors, Is.Empty, string.Join("\n", m.Errors));
            Assert.That(m.Files, Does.Contain("GuideLines/Guide"));
            Assert.That(m.Files, Does.Contain("VoiceManifests/Guide"));
            foreach (string id in Platform)
            {
                Assert.IsTrue(m.Has(id), id);
                Assert.IsTrue(GuideLines.Has(id), id);
                Assert.IsFalse(GuideLines.IsLine(id), id + " is not one of the 38 voice slots");
                Assert.That(GuideLines.TextOf(id), Is.Not.Empty);
                Assert.That(GuideLines.SecondsOf(id), Is.GreaterThan(0f), id + " has a manifest entry");
            }
            var ids = new List<string>(m.Ids);
            for (int i = 1; i < Platform.Length; i++)
                Assert.That(ids.IndexOf(Platform[i]), Is.GreaterThan(ids.IndexOf(Platform[i - 1])), "file order");
            foreach (string id in m.Ids) Assert.That(GuideLines.IndexOf(id), Is.EqualTo(-1), id);
            Assert.That(GuideLines.Ids.Length, Is.EqualTo(38));
            // The Guide's own 38 lead the voice slots; the merged module lines (the Audio module's VoiceManifests merge) follow.
            Assert.That(GuideVoice.LineIds.Take(38), Is.EqualTo(GuideLines.Ids), "the Guide's own table still leads the voice slots");
            foreach (string id in Platform) Assert.That(System.Array.IndexOf(GuideVoice.LineIds, id), Is.GreaterThanOrEqualTo(38), id + " has a voice slot after the 38");
        }

        [Test]
        public void EveryManifestEntryNamesAWavOfItsLength()
        {
            GuideLines.ReloadMerged();
            GuideLineTable m = GuideLines.Merged;
            Assert.That(m.Voices.Count, Is.GreaterThanOrEqualTo(Platform.Length));
            foreach (KeyValuePair<string, GuideLineTable.Voice> kv in m.Voices)
            {
                Assert.IsTrue(m.Has(kv.Key), kv.Key + ": a voiced line has its text");
                Assert.That(kv.Value.File, Is.EqualTo(kv.Key + ".wav"), "the manifest's shape: file = <id>.wav");
                Assert.That(kv.Value.Rate, Is.EqualTo(22050));
                var clip = Resources.Load<AudioClip>("Voice/" + kv.Key);
                Assert.That(clip, Is.Not.Null, kv.Key + ": Resources/Voice/<id>.wav");
                Assert.That(clip.channels, Is.EqualTo(1), kv.Key + " mono");
                Assert.That(clip.frequency, Is.EqualTo(22050), kv.Key + " 22.05 kHz");
                Assert.That(clip.length, Is.EqualTo(kv.Value.Seconds).Within(0.001f), kv.Key + ": the manifest's seconds are measured from the file");
            }
        }

        [Test]
        public void TheManifestReaderReadsTheGuidesOwnManifestExactly()
        {
            string json = Resources.Load<TextAsset>("Voice/manifest").text;
            List<(string id, GuideLineTable.Voice v)> entries = ManifestJson.Parse(json);
            Assert.That(entries.Count, Is.EqualTo(GuideLines.Count));
            for (int i = 0; i < entries.Count; i++)
            {
                Assert.That(entries[i].id, Is.EqualTo(GuideLines.Ids[i]));
                Assert.That(entries[i].v.Seconds, Is.EqualTo(GuideLines.ManifestSeconds[i]).Within(1e-6f));
                Assert.That(entries[i].v.File, Is.EqualTo(GuideLines.Ids[i] + ".wav"));
                Assert.That(entries[i].v.Rate, Is.EqualTo(22050));
            }
        }

        [Test]
        public void ADuplicateLineFailsAndTheLaterOneIsDropped()
        {
            const string a = "{\"lines\":[{\"id\":\"tip_x_one\",\"text\":\"First.\"},{\"id\":\"tip_x_two\",\"text\":\"Second.\"}]}";
            const string b = "{\"lines\":[{\"id\":\"tip_x_one\",\"text\":\"Again.\"},{\"id\":\"intro_vr\",\"text\":\"Not yours.\"},{\"id\":\"tip_y\",\"text\":\"Third.\",\"spoken\":\"third\"}]}";
            const string ma = "{\"tip_x_one\": {\"file\": \"tip_x_one.wav\", \"seconds\": 1.25, \"rate\": 22050}}";
            const string mb = "{ \"tip_x_one\" : { \"file\" : \"tip_x_one.wav\", \"seconds\" : 2.5, \"rate\" : 22050 }, \"praise_0\": {\"file\": \"praise_0.wav\", \"seconds\": 0.5, \"rate\": 22050} }";
            LogAssert.Expect(LogType.Error, new Regex(@"\[Guide\] lines: B: duplicate line id tip_x_one"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Guide\] lines: B: duplicate line id intro_vr"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Guide\] lines: MB: duplicate voice line tip_x_one"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Guide\] lines: MB: duplicate voice line praise_0"));
            GuideLineTable t = GuideLineTable.Build(new[] { ("A", a), ("B", b) }, new[] { ("MA", ma), ("MB", mb) });
            Assert.That(t.Errors.Count, Is.EqualTo(4), "each duplicate is an error");
            Assert.That(t.Ids, Is.EqualTo(new[] { "tip_x_one", "tip_x_two", "tip_y" }), "the later one dropped; the order is stable");
            Assert.That(t.TextOf("tip_x_one"), Is.EqualTo("First."));
            Assert.That(t.SpokenOf("tip_y"), Is.EqualTo("third"));
            Assert.That(t.SpokenOf("tip_x_two"), Is.EqualTo("Second."), "spoken defaults to the text");
            Assert.That(t.Seconds("tip_x_one"), Is.EqualTo(1.25f));
            Assert.That(t.Seconds("tip_x_two"), Is.EqualTo(-1f), "a caption-only line has no voice");
        }
    }
}
