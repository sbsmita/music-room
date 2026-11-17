// Sunny's voice lines as data (12-new-instruments/platform.md 8.2): the Guide's manifest merged with every module's
// Resources/VoiceManifests/*.json (same shape), the Guide's lines first, then the modules' by file name and file order; a
// duplicate id is an error and the later one is dropped; the table's size has no small cap (300 synthetic lines work); the
// robotised buffers render lazily and at most 24 stay resident (LRU), a line asked in the last ProtectSeconds kept.
using System;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Audio.Synths;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.Audio
{
    public class VoiceManifestMergeTests
    {
        const int Sr = OfflineAudio.Sr;

        static List<string> IdsOf(string json)
        {
            var ids = new List<string>();
            foreach (var kv in (List<KeyValuePair<string, object>>)MiniJson.Parse(json)) ids.Add(kv.Key);
            return ids;
        }

        static string Module(string prefix, int n, float seconds = 1.2f)
        {
            var lines = new List<KeyValuePair<string, float>>();
            for (int i = 0; i < n; i++) lines.Add(new KeyValuePair<string, float>($"{prefix}_{i:000}", seconds + 0.01f * (i % 50)));
            return VoiceManifest.Write(lines);
        }

        [Test]
        public void TheAppsTableIsTheGuidesLinesThenEveryModulesInFileNameOrder()
        {
            var baseAsset = Resources.Load<TextAsset>(VoiceManifest.BaseResource);
            Assert.That(baseAsset, Is.Not.Null, "Guide/Resources/Voice/manifest.json");
            var want = IdsOf(baseAsset.text);
            Assert.That(want.Count, Is.EqualTo(38), "the Guide's own 38 lines");
            var modules = new List<TextAsset>(Resources.LoadAll<TextAsset>(VoiceManifest.ModuleFolder));
            modules.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var m in modules) want.AddRange(IdsOf(m.text));
            var table = VoiceManifest.Load();
            Assert.That(table.Errors, Is.Empty, "no duplicate line in the app's manifests");
            Assert.That(table.Ids, Is.EqualTo(want.ToArray()), "38 + every module's lines, in order");
            Assert.That(GuideVoice.LineIds, Is.EqualTo(want.ToArray()), "GuideVoice.LineIds is the merged table");
            Assert.That(GuideVoice.LineCount, Is.EqualTo(want.Count));
            Assert.That(table.Seconds(table.IndexOf("intro_vr")), Is.EqualTo(3.102f).Within(1e-6f), "the manifest's seconds (first five: reworded)");
            Debug.Log($"[VOICE] merged table: {table.Count} lines (38 of the Guide's + {table.Count - 38} from {modules.Count} module manifests)");
            Assert.That(AudioEngine.BankSlots(table.Count), Is.GreaterThanOrEqualTo(AudioEngine.VoiceSlot0 + table.Count), "a voice slot per line");
        }

        [Test]
        public void ModuleLinesFollowTheGuidesByFileNameThenFileOrder()
        {
            string guide = VoiceManifest.Write(new[] { new KeyValuePair<string, float>("intro_vr", 3.5f), new KeyValuePair<string, float>("bye", 0.7f) });
            var t = VoiceManifest.Merge(guide, new[]
            {
                new KeyValuePair<string, string>("Marimba", Module("tip_marimba", 3)),
                new KeyValuePair<string, string>("Handpan", Module("tip_handpan", 2)),
            });
            Assert.That(t.Ids, Is.EqualTo(new[] { "intro_vr", "bye", "tip_handpan_000", "tip_handpan_001", "tip_marimba_000", "tip_marimba_001", "tip_marimba_002" }));
            Assert.That(t.From(2), Is.EqualTo("Handpan"));
            Assert.That(t.From(0), Is.EqualTo("manifest"));
            Assert.That(t.Seconds(3), Is.EqualTo(1.21f).Within(1e-5f));
            Assert.That(t.Errors, Is.Empty);
        }

        [Test]
        public void ADuplicateLineIsAnErrorAndTheLaterOneIsDropped()
        {
            string guide = VoiceManifest.Write(new[] { new KeyValuePair<string, float>("intro_vr", 3.5f) });
            LogAssert.Expect(LogType.Error, "[Audio] voice: duplicate line intro_vr");
            LogAssert.Expect(LogType.Error, "[Audio] voice: duplicate line tip_a_001");
            var t = VoiceManifest.Merge(guide, new[]
            {
                new KeyValuePair<string, string>("Zeta", VoiceManifest.Write(new[] { new KeyValuePair<string, float>("intro_vr", 9f), new KeyValuePair<string, float>("tip_a_001", 1f) })),
                new KeyValuePair<string, string>("Alpha", Module("tip_a", 2)),
            });
            Assert.That(t.Ids, Is.EqualTo(new[] { "intro_vr", "tip_a_000", "tip_a_001" }), "the Guide's intro_vr and Alpha's tip_a_001 win (earlier)");
            Assert.That(t.Seconds(0), Is.EqualTo(3.5f).Within(1e-5f), "the first one's seconds");
            Assert.That(t.Errors, Has.Count.EqualTo(2));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[Audio\] voice: manifest Broken does not parse"));
            var u = VoiceManifest.Merge(guide, new[] { new KeyValuePair<string, string>("Broken", "{ \"x\": ") });
            Assert.That(u.Count, Is.EqualTo(1), "a file that does not parse adds nothing");
        }

        /// <summary>A synthetic recording per line: a 22.05 kHz tone of the manifest's length (the id picks the pitch).</summary>
        static Func<string, VoiceClip> Clips(VoiceManifest t, Dictionary<string, int> loads = null) => id =>
        {
            if (loads != null) loads[id] = loads.TryGetValue(id, out int n) ? n + 1 : 1;
            int i = t.IndexOf(id);
            int len = (int)(t.Seconds(i) * 22050);
            var x = new float[len];
            float hz = 180f + 7f * (i % 40);
            for (int k = 0; k < len; k++) x[k] = 0.5f * Mathf.Sin(2 * Mathf.PI * hz * k / 22050f) * Mathf.Min(1f, Mathf.Min(k, len - k) / 400f);
            return new VoiceClip { Samples = x, Rate = 22050 };
        };

        [Test]
        public void ThreeHundredLinesRenderOnDemandAndAtMostTwentyFourStayResident()
        {
            var modules = new List<KeyValuePair<string, string>>();
            for (int m = 0; m < 3; m++) modules.Add(new KeyValuePair<string, string>("Module" + m, Module("tip_m" + m, 100, 0.8f)));
            var table = VoiceManifest.Merge(Resources.Load<TextAsset>(VoiceManifest.BaseResource).text, modules);
            Assert.That(table.Count, Is.EqualTo(338), "38 + 300 lines: no small cap");
            double now = 0;
            var loads = new Dictionary<string, int>();
            using var a = new OfflineAudio();
            var bank = new SampleBank(AudioEngine.BankSlots(table.Count));
            Assert.That(bank.SlotCount, Is.EqualTo(AudioEngine.VoiceSlot0 + 338), "the bank grows past 512 slots with the table");
            using var g = new GuideVoice(a.Kit, Sr, () => now, table, Clips(table, loads), () => now);
            g.Attach(bank, AudioEngine.VoiceSlot0);
            Assert.That(g.Lines, Is.EqualTo(338));
            Assert.That(g.RenderedLines, Is.EqualTo(0), "nothing rendered until asked");
            int maxResident = 0;
            long maxSamples = 0;
            for (int i = 0; i < table.Count; i++)
            {
                now += 3.0;   // each line asked 3 s after the last: the protect window (2 s) has passed
                string id = table.Id(i);
                Assert.That(g.Ready(id), Is.True, id + ": no worker here, so it renders at once");
                Assert.That(g.IsRendered(i), Is.True);
                maxResident = Math.Max(maxResident, g.RenderedLines);
                maxSamples = Math.Max(maxSamples, g.ResidentSamples);
                if (i % 50 == 0)
                {
                    float[] y = bank.Get(AudioEngine.VoiceSlot0 + i);
                    Assert.That(y.Length, Is.EqualTo(GuideVoice.RenderLength((int)(table.Seconds(i) * 22050), 22050, Sr)), id + " length");
                    Assert.That(NullTest.Peak(y), Is.GreaterThan(0.1f), id + " is robotised, not silent");
                }
            }
            Assert.That(maxResident, Is.LessThanOrEqualTo(GuideVoice.MaxResident), "at most 24 robotised lines resident");
            Assert.That(g.RenderedLines, Is.EqualTo(GuideVoice.MaxResident));
            Assert.That(g.Renders, Is.EqualTo(338));
            Assert.That(g.Evictions, Is.EqualTo(338 - 24));
            Assert.That(g.IsRendered(0), Is.False, "the least recently asked went first");
            Assert.That(g.IsRendered(337), Is.True);
            Debug.Log($"[VOICE] 338 lines rendered on demand: at most {maxResident} resident, {maxSamples * 4 / 1048576.0:F1} MiB at most (0.8-1.3 s lines; " +
                      $"2.4 s lines: about {24 * 2.4 * Sr * 4 / 1e6:F0} MB)");

            // a line that was evicted renders again when it is played (Play of a line not rendered renders it first)
            double end = g.Play(table.Id(0), 0);
            Assert.That(end, Is.GreaterThan(0));
            Assert.That(g.IsRendered(0), Is.True);
            Assert.That(g.SyncRenders, Is.EqualTo(1));
            Assert.That(loads[table.Id(0)], Is.EqualTo(2), "its recording loaded again (the copy is not kept)");
            Assert.That(g.RenderedLines, Is.EqualTo(GuideVoice.MaxResident), "the playing line in, the least recent out");
        }

        [Test]
        public void LinesAskedTogetherStayResidentUntilTheirProtectWindowEnds()
        {
            var modules = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("Module", Module("tip_x", 40, 0.6f)) };
            var table = VoiceManifest.Merge(null, modules);
            double now = 100;
            using var a = new OfflineAudio();
            var bank = new SampleBank(AudioEngine.BankSlots(table.Count));
            using var g = new GuideVoice(a.Kit, Sr, () => now, table, Clips(table), () => now);
            g.Attach(bank, AudioEngine.VoiceSlot0);
            // a caller waiting on 30 lines at once (GuideModuleTests.WaitForLines): all of them become ready together
            bool all = true;
            for (int i = 0; i < 30; i++) all &= g.Ready(table.Id(i));
            Assert.That(all, Is.True);
            Assert.That(g.RenderedLines, Is.EqualTo(30), "asked within the protect window: kept past the limit");
            now += 1.0;
            g.Tick();
            Assert.That(g.RenderedLines, Is.EqualTo(30), "still inside the window");
            now += 1.5;
            g.Tick();
            Assert.That(g.RenderedLines, Is.EqualTo(GuideVoice.MaxResident), "trimmed to 24 once the window passed");
            g.ProtectSeconds = 0;
            for (int i = 30; i < 40; i++) { now += 0.01; g.Ready(table.Id(i)); }
            Assert.That(g.RenderedLines, Is.EqualTo(GuideVoice.MaxResident), "no window: strictly 24");
            // the playing line is never evicted, even as the least recently asked
            g.Play(table.Id(39), 0);
            for (int i = 0; i < 30; i++) { now += 0.01; g.Ready(table.Id(i)); }
            Assert.That(g.IsRendered(39), Is.True, "the current line stays");
        }

        [Test]
        public void TheAppsVoiceRendersOnlyTheIntroLinesAtBoot()
        {
            Assert.That(GuideVoice.BootLines, Is.EqualTo(new[] { "intro_vr", "intro_desk" }));
            foreach (string id in GuideVoice.BootLines) Assert.That(VoiceManifest.Default.IndexOf(id), Is.GreaterThanOrEqualTo(0), id + " is a line");
            using var a = new OfflineAudio(guide: true);
            Assert.That(a.Guide.Has("open_vr"), Is.True, "a manifest line");
            Assert.That(a.Guide.Has("tip_handpan_tap"), Is.False, "a stub's text-only tip has no voice line");
            Assert.That(a.Guide.Seconds("open_vr"), Is.EqualTo(3.46f).Within(1e-5f), "seconds from the manifest, before its clip loads");
            Assert.That(a.Guide.RenderedLines, Is.EqualTo(0));
            Assert.That(a.Guide.Ready("open_vr"), Is.True, "no worker in the harness: asked lines render at once");
            Assert.That(a.Guide.RenderedLines, Is.EqualTo(1));
        }
    }
}
