// Every feature brings its Sunny tip (platform.md 8.5; sunny-tour.md 6.2), over the registry's descriptors and every
// IGuideTipSource (the platform's PlatformTips): every feature id is covered by a tip or Passive with a reason; every tip's
// lines exist (the caption text, the manifest entry and the WAV) unless it is TextOnly (then its Title is the caption);
// every line is at most 12 words and 4.0 s with no brand words; the deck's lines say "on the side table", never "on your
// left"; triggers and successes name what they need. InstrumentTipsTests: every playable non-classic descriptor has one
// P2 FirstArrival tip whose success is a live Onset of its own; the classic three are taught by the tour.
// First five (FirstFiveCoverageTests below): the tour's tips (TourTipSource) are in the same catalog, the first run's steps
// (FirstRun.Covers) count as teaching, every menu row kind and screen maps to a taught feature, every line the tour says
// has its caption, clip and manifest entry, no line is left over, and the closing invitation names the real controls.
// All of it runs over the registry: an instrument's tips and features count the moment its descriptor carries them.
using System;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Guide
{
    public static class TipCatalog
    {
        public static InstrumentRegistry Registry() => InstrumentRegistry.Discover();

        public static List<(TipSpec spec, InstrumentDescriptor owner)> All(out List<FeatureSpec> features)
        {
            features = new List<FeatureSpec>();
            var errors = new List<string>();
            var tips = TipRunner.Collect(Registry(), ModuleDiscovery.CreateAll<IGuideTipSource>(), features, errors);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            return tips;
        }

        /// <summary>Every feature taught: a tip's Covers (the registry's, the platform's, the tour's) or a first-run step's.</summary>
        public static HashSet<string> Covered()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (spec, _) in All(out _)) if (spec.Covers != null) foreach (string c in spec.Covers) set.Add(c);
            foreach (var (_, features) in FirstRun.Covers) foreach (string f in features) set.Add(f);
            return set;
        }

        /// <summary>The words of a line, as a reader counts them.</summary>
        public static int Words(string text) => Regex.Matches(text ?? "", @"[A-Za-z0-9']+").Count;

        /// <summary>sunny-tour 6.2's brand list (and a few instrument makers): never in a line.</summary>
        public static readonly string[] Brands =
        {
            "meta", "quest", "oculus", "horizon", "apple", "vision", "google", "android", "unity", "facebook", "steam", "pico",
            "steinway", "yamaha", "roland", "kawai", "moog", "etherwave", "zildjian", "sabian", "ludwig", "remo", "panart",
        };

        public static string Brand(string text)
        {
            foreach (string b in Brands) if (Regex.IsMatch(text ?? "", @"\b" + b + @"\b", RegexOptions.IgnoreCase)) return b;
            return null;
        }
    }

    public class SunnyCoverageTests
    {
        /// <summary>Integration: every real instrument (no placeholder is left) brings its own tips into the one catalog, and
        /// so does the music book (an IGuideTipSource); the counts are logged for the record.</summary>
        [Test]
        public void EveryInstrumentAndTheBookBringTheirOwnTips()
        {
            var tips = TipCatalog.All(out _);
            var log = new StringBuilder();
            int sources = 0;
            foreach (InstrumentDescriptor d in TipCatalog.Registry().All)
            {
                int n = 0;
                foreach (var (_, owner) in tips) if (owner != null && owner.Kind == d.Kind) n++;
                log.Append(d.Id).Append(' ').Append(n).Append(d.Placeholder ? " (placeholder)" : "").Append("; ");
                Assert.That(d.Placeholder, Is.False, d.Id + ": a real instrument since the integration");
                if (d.Kind > InstrumentKind.Harp && d.Kind != InstrumentKind.Table && d.Kind != InstrumentKind.Menu)
                    Assert.That(n, Is.GreaterThanOrEqualTo(5), d.Id + ": its own tips in the catalog");
            }
            foreach (var (_, owner) in tips) if (owner == null) sources++;
            log.Append("sources (platform, tour, book) ").Append(sources).Append("; total ").Append(tips.Count);
            Assert.That(tips.Exists(t => t.owner == null && t.spec.Id == "tip_book"), Is.True, "the music book's tips (SheetTipSource)");
            TestContext.WriteLine("[Sunny] tips in the catalog: " + log);
            UnityEngine.Debug.Log("[Sunny] tips in the catalog: " + log);
        }

        [Test]
        public void EveryFeatureIsCoveredByATipOrPassiveWithAReason()
        {
            var tips = TipCatalog.All(out List<FeatureSpec> features);
            Assert.That(features.Count, Is.GreaterThanOrEqualTo(5 + 5), "the platform's five and one per new instrument at least");
            HashSet<string> covered = TipCatalog.Covered();   // first five: the first run's steps teach too
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (FeatureSpec f in features)
            {
                Assert.IsTrue(ids.Add(f.Id), "feature ids are unique: " + f.Id);
                if (f.Passive) { Assert.That(f.PassiveReason.Trim().Length, Is.GreaterThan(10), f.Id + ": a Passive feature says why"); continue; }
                Assert.IsTrue(covered.Contains(f.Id), f.Id + " has no tip (platform.md 8.5: every feature brings its tip)");
            }
            foreach (string c in covered) Assert.IsTrue(ids.Contains(c), "a tip covers an unknown feature: " + c);
            foreach (string f in new[] { "platform.bring.menu", "platform.bring.ray", "platform.bring.pageturn", "platform.myset", "platform.putaway" })
                Assert.IsTrue(covered.Contains(f), f);
        }

        [Test]
        public void EveryTipsLinesExistUnlessItIsTextOnly()
        {
            GuideLines.ReloadMerged();
            var tips = TipCatalog.All(out _);
            Assert.That(tips.Count, Is.GreaterThanOrEqualTo(6 + 5));
            foreach (var (spec, owner) in tips)
            {
                string who = spec.Id + (owner != null ? " (" + owner.Id + ")" : "");
                if (owner != null && owner.Id != null && !Tutorials.IsClassicId(owner.Id)) Assert.That(spec.Id, Does.StartWith("tip_" + owner.Id + "_"), who + ": the instrument's prefix (platform.md 3.4)");
                if (spec.TextOnly)
                {
                    Assert.That(spec.Title, Is.Not.Null.And.Not.Empty, who + ": a caption-only tip shows its Title");
                    continue;
                }
                string[] lines = spec.Lines != null && spec.Lines.Length > 0 ? spec.Lines : new[] { spec.Id };
                foreach (string line in lines)
                {
                    Assert.IsTrue(GuideLines.Has(line), who + ": the line " + line + " has its caption (Resources/GuideLines)");
                    float seconds = GuideLines.SecondsOf(line);
                    Assert.That(seconds, Is.GreaterThan(0f), who + ": " + line + " is in a voice manifest");
                    var clip = Resources.Load<AudioClip>("Voice/" + line);
                    Assert.That(clip, Is.Not.Null, who + ": Resources/Voice/" + line + ".wav");
                    Assert.That(clip.length, Is.EqualTo(seconds).Within(0.001f), who + ": the WAV is as long as its manifest says");
                }
                if (spec.Again != null) Assert.IsTrue(GuideLines.Has(spec.Again), who + ": its again line");
            }
        }

        [Test]
        public void EveryLineIsShortWithoutBrandsAndTheDeckSaysTheSideTable()
        {
            GuideLines.ReloadMerged();
            var tips = TipCatalog.All(out _);
            foreach (var (spec, _) in tips)
            {
                var texts = new List<(string what, string text, float seconds)>();
                if (spec.TextOnly) texts.Add((spec.Id + " title", spec.Title, 0f));
                else foreach (string line in spec.Lines != null && spec.Lines.Length > 0 ? spec.Lines : new[] { spec.Id }) texts.Add((line, GuideLines.TextOf(line), GuideLines.SecondsOf(line)));
                foreach (var (what, text, seconds) in texts)
                {
                    Assert.That(TipCatalog.Words(text), Is.InRange(1, 12), what + ": at most 12 words: " + text);
                    Assert.That(seconds, Is.LessThanOrEqualTo(4.0f), what + ": at most 4.0 s");
                    Assert.That(TipCatalog.Brand(text), Is.Null, what + ": no brand words: " + text);
                }
            }
            // Every line Sunny can say (the 38 and the merged ones): the side table may have yielded (platform.md 4.2).
            foreach (string id in GuideLines.Ids) Assert.That(GuideLines.TextOf(id), Does.Not.Contain("on your left").IgnoreCase, id);
            foreach (string id in GuideLines.Merged.Ids) Assert.That(GuideLines.TextOf(id), Does.Not.Contain("on your left").IgnoreCase, id);
        }

        [Test]
        public void TheSixPlatformTipsAreTheAgreedWordsTriggersAndSuccesses()
        {
            GuideLines.ReloadMerged();
            var src = new PlatformTips();
            var byId = new Dictionary<string, TipSpec>();
            foreach (TipSpec t in src.Tips()) byId[t.Id] = t;
            void Check(string id, string text, TipClass cls, TipTrigger trigger, string arg, TipSuccess success, string successArg)
            {
                TipSpec t = byId[id];
                Assert.That(GuideLines.TextOf(id), Is.EqualTo(text), id);
                Assert.That(t.Class, Is.EqualTo(cls), id);
                Assert.That(t.Trigger, Is.EqualTo(trigger), id);
                Assert.That(t.TriggerArg ?? "", Is.EqualTo(arg), id);
                Assert.That(t.Success, Is.EqualTo(success), id);
                if (successArg != null) Assert.That(t.SuccessArg, Is.EqualTo(successArg), id);
                Assert.IsFalse(t.TextOnly, id + " is voiced");
            }
            Check("tip_bring_grid", "Poke an instrument in Instruments to bring it here.", TipClass.P2, TipTrigger.Signal, "menu.instruments", TipSuccess.Signal, "layout.bring.menu");
            Check("tip_summon_far", "Point at an instrument and pinch to bring it here.", TipClass.P3, TipTrigger.FirstDisplayedGaze, "", TipSuccess.Signal, "layout.bring.ray");
            Check("tip_my_set", "Open Instruments to choose which ones sit with you.", TipClass.P3, TipTrigger.Signal, "layout.bring.ray", TipSuccess.None, null);
            Check("tip_set_full", "Only three fit. The least played one goes back.", TipClass.P2, TipTrigger.Signal, "layout.replaced", TipSuccess.None, null);
            Check("tip_put_away", "Poke a ticked instrument to put it back.", TipClass.P3, TipTrigger.Signal, "menu.instruments.set", TipSuccess.Signal, "layout.putaway");
            Check("tip_page_turn", "Swipe your hand sideways for the next instrument.", TipClass.P3, TipTrigger.Signal, "layout.single", TipSuccess.Signal, "layout.bring.pageturn");
            Assert.IsTrue(byId["tip_summon_far"].VrOnly, "pointing is a headset gesture");
            Assert.That(byId["tip_summon_far"].Demo, Is.EqualTo(DemoKind.Point));
            Assert.That(byId["tip_summon_far"].Anchor, Is.EqualTo(PlatformTips.DisplayAnchor), "at the gazed displayed instrument");
            Assert.That(global::MusicRoom.Lessons.Tutorial.RedemoSignal, Is.EqualTo(GuideModule.LessonRedemoSignal), "the tutorial's B6 signal is the Guide's");
        }

        [Test]
        public void TriggersAndSuccessesNameWhatTheyNeed()
        {
            var tips = TipCatalog.All(out _);
            foreach (var (spec, owner) in tips)
            {
                if (spec.Trigger == TipTrigger.Signal) Assert.That(spec.TriggerArg, Is.Not.Null.And.Not.Empty, spec.Id + ": its signal");
                if (spec.Success == TipSuccess.Signal) Assert.That(spec.SuccessArg, Is.Not.Null.And.Not.Empty, spec.Id + ": its success signal");
                if (spec.Trigger == TipTrigger.FirstArrival || spec.Trigger == TipTrigger.IdleNear || spec.Trigger == TipTrigger.LessonStart || spec.Success == TipSuccess.Facing || spec.Success == TipSuccess.LessonHit)
                    Assert.That(owner, Is.Not.Null, spec.Id + ": an instrument's trigger needs its instrument");
                if (spec.Success == TipSuccess.EventType && !string.IsNullOrEmpty(spec.SuccessArg) && owner != null)
                {
                    bool own = false;
                    foreach (EventTypeSpec e in owner.Events) own |= e.Type == spec.SuccessArg;
                    Assert.IsTrue(own, spec.Id + ": success type " + spec.SuccessArg + " is one of " + owner.Id + "'s");
                }
                if (owner != null && !string.IsNullOrEmpty(spec.Anchor))
                    Assert.That(owner.Anchors, Does.Contain(spec.Anchor), spec.Id + ": " + owner.Id + " declares the anchor " + spec.Anchor);
                if (spec.Demo == DemoKind.Custom) Assert.That(spec.Motion?.Keys, Is.Not.Null.And.Not.Empty, spec.Id + ": a custom demo has keys");
            }
        }
    }

    /// <summary>Test helpers the coverage checks share.</summary>
    static class Tutorials
    {
        public static bool IsClassicId(string id) => id == "piano" || id == "drums" || id == "harp";
    }

    public class InstrumentTipsTests
    {
        [Test]
        public void EveryNewInstrumentHasOneArrivalTipLearnedByPlayingIt()
        {
            InstrumentRegistry reg = TipCatalog.Registry();
            int checkedInstruments = 0;
            foreach (InstrumentDescriptor d in reg.All)
            {
                if (Tutorials.IsClassicId(d.Id)) continue;                    // the tour teaches the piano, the drums and the harp
                checkedInstruments++;
                int arrival = 0;
                foreach (TipSpec t in d.Tips)
                {
                    if (t.Trigger != TipTrigger.FirstArrival) continue;
                    arrival++;
                    Assert.That(t.Class, Is.EqualTo(TipClass.P2), d.Id + ": the arrival tip is P2");
                    Assert.That(t.Success, Is.EqualTo(TipSuccess.EventType), d.Id + ": learned by playing it");
                    if (!string.IsNullOrEmpty(t.SuccessArg))
                    {
                        bool onset = false;
                        foreach (EventTypeSpec e in d.Events) onset |= e.Type == t.SuccessArg && e.Role == EventRole.Onset;
                        Assert.IsTrue(onset, d.Id + ": its success is one of its own Onsets");
                    }
                    if (d.Placeholder) Assert.IsTrue(t.TextOnly, d.Id + ": the phase-0 stub's tip is a caption only");
                }
                Assert.That(arrival, Is.EqualTo(1), d.Id + ": exactly one arrival tip");
            }
            Assert.That(checkedInstruments, Is.EqualTo(4));
        }

        [Test]
        public void TheClassicThreeAreTaughtByTheTour()
        {
            var steps = new List<StepId>();
            foreach (StepDef s in GuideLines.Steps) steps.Add(s.Id);
            Assert.That(steps, Is.EqualTo(new[] { StepId.Open, StepId.Poke, StepId.Close, StepId.Swipe, StepId.Nod }));
            InstrumentRegistry reg = TipCatalog.Registry();
            foreach (InstrumentDescriptor d in reg.All)
                if (Tutorials.IsClassicId(d.Id)) Assert.That(d.Tips, Is.Empty, d.Id + ": no descriptor tip needed (the tour and the deck walkthrough)");
        }
    }

    /// <summary>The first five minutes' coverage (Docs/11-first-five/sunny-tour.md 6.2), over the whole catalog.</summary>
    public class FirstFiveCoverageTests
    {
        [Test]
        public void TheToursTipsAndFeaturesAreInTheCatalog()
        {
            var tips = TipCatalog.All(out List<FeatureSpec> features);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (spec, owner) in tips) { ids.Add(spec.Id); if (TourTips.Get(spec.Id) != null) Assert.That(owner, Is.Null, spec.Id); }
            foreach (TourTip t in TourTips.All) Assert.IsTrue(ids.Contains(t.Id), t.Id + " is a TipSpec of TourTipSource");
            foreach (string p in new[] { "tip_bring_grid", "tip_summon_far", "tip_my_set", "tip_set_full", "tip_put_away", "tip_page_turn" })
                Assert.IsTrue(ids.Contains(p), "the platform's " + p);
            foreach (InstrumentDescriptor d in TipCatalog.Registry().All)
                if (d.Tips != null) foreach (TipSpec t in d.Tips) Assert.IsTrue(ids.Contains(t.Id), d.Id + "'s " + t.Id);
            var featureIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (FeatureSpec f in features) featureIds.Add(f.Id);
            foreach (TourFeature f in TourFeatures.All) Assert.IsTrue(featureIds.Contains(f.Id), f.Id);
            int learnable = 0;
            foreach (TourFeature f in TourFeatures.All) if (f.Kind != TourFeatureKind.Passive) learnable++;
            // 82 in the inventory; sound.classic retired with the Classic sound switch (2026-11-10)
            Assert.That(learnable, Is.GreaterThanOrEqualTo(81), "the inventory's 81 learnable features at least");
            Assert.That(TourTips.All.Length, Is.GreaterThanOrEqualTo(61), "the spec's 61 tips at least");
            var tipIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (TourTip t in TourTips.All) Assert.IsTrue(tipIds.Add(t.Id), "duplicate tip " + t.Id);
        }

        [Test]
        public void EveryMenuRowKindTeachesACoveredFeature()
        {
            HashSet<string> covered = TipCatalog.Covered();
            foreach (RowKind k in Enum.GetValues(typeof(RowKind)))
            {
                if (k == RowKind.None) continue;
                string f = MenuRowIds.FeatureOf(k);
                Assert.That(f, Is.Not.Null, $"row kind {k} names no feature (MenuRowIds.FeatureOf)");
                Assert.IsTrue(covered.Contains(f), $"row kind {k}: feature {f} is taught by no step or tip");
            }
        }

        [Test]
        public void EveryMenuScreenIsShownByTheTourOrATip()
        {
            var screens = new Dictionary<MenuScreen, string>
            {
                { MenuScreen.Home, "menu.screens" }, { MenuScreen.Lessons, "lessons.instrument" }, { MenuScreen.Courses, "lessons.course" },
                { MenuScreen.Lesson, "lessons.play" }, { MenuScreen.Instruments, "platform.bring.menu" }, { MenuScreen.Recordings, "recordings.list" },
                { MenuScreen.Take, "recordings.take" }, { MenuScreen.Start, "start.here" }, { MenuScreen.Arrange, "arrange.done" },
                { MenuScreen.Guide, "sunny.screen" }, { MenuScreen.Tips, "sunny.tips" },
            };
            HashSet<string> covered = TipCatalog.Covered();
            foreach (MenuScreen s in Enum.GetValues(typeof(MenuScreen)))
            {
                Assert.IsTrue(screens.ContainsKey(s), $"screen {s} has no feature here");
                Assert.IsTrue(covered.Contains(screens[s]), $"screen {s}: {screens[s]} is taught by nothing");
            }
        }

        static IEnumerable<string> UsedLines()
        {
            foreach (string id in FirstRun.AllLines) yield return id;
            // The old quick tour's own lines (2026-11-04), said by nothing since the fast tour (2026-11-16): kept, with their
            // clips, until the user says to delete them (TUNE-FASTTOUR.md). Listed so this check stays a real leftover check.
            foreach (string id in FirstRun.RetiredLines) yield return id;
            foreach (string id in DeckGuide.QuickLines) yield return id;   // the deck's quick show (GUIDE)
            foreach (TourTip t in TourTips.All)
            {
                if (t.TextOnly != null) continue;
                foreach (string l in t.Lines) yield return l;
                yield return TourTips.AgainOf(t);
            }
            foreach (string p in GuideLines.Praise) yield return p;
        }

        [Test]
        public void EveryLineTheTourSaysHasItsCaptionClipAndManifestEntry()
        {
            GuideLines.ReloadMerged();
            foreach (string id in UsedLines())
            {
                Assert.IsTrue(GuideLines.Has(id), $"{id}: no caption (GuideLines, the 38 or GuideLines/FirstFive.json)");
                Assert.That(GuideLines.TextOf(id), Is.Not.Empty, id);
                float seconds = GuideLines.SecondsOf(id);
                Assert.That(seconds, Is.GreaterThan(0f), $"{id}: not in a voice manifest");
                var clip = Resources.Load<AudioClip>("Voice/" + id);
                Assert.That(clip, Is.Not.Null, $"{id}: no Resources/Voice/{id}.wav");
                Assert.That(clip.length, Is.EqualTo(seconds).Within(0.001f), $"{id}: the clip's length is the manifest's");
            }
            foreach (TourTip t in TourTips.All)
                if (t.TextOnly == null) Assert.That(t.Lines.Length, Is.GreaterThan(0), $"{t.Id} says nothing");
        }

        [Test]
        public void NoFirstFiveLineIsLeftOver()
        {
            var used = new HashSet<string>(UsedLines(), StringComparer.Ordinal);
            foreach (string id in FirstFiveLines.Ids) Assert.IsTrue(used.Contains(id), $"{id} is said by nothing (a removed feature left its line behind)");
        }

        [Test]
        public void EveryFirstFiveLineKeepsTheRules()
        {
            GuideLines.ReloadMerged();
            foreach (string id in FirstFiveLines.Ids)
            {
                string text = GuideLines.TextOf(id);
                Assert.That(TipCatalog.Words(text), Is.LessThanOrEqualTo(12), $"{id}: {text}");
                Assert.That(GuideLines.SecondsOf(id), Is.LessThanOrEqualTo(4.0f), $"{id}: {GuideLines.SecondsOf(id)} s");
                Assert.That(text, Does.Match(@"[.!?]$"), $"{id} ends with punctuation");
                Assert.That(TipCatalog.Brand(text), Is.Null, $"{id}: a brand word");
                Assert.That(text, Does.Not.Contain("on your left").IgnoreCase, $"{id}: the side table may have yielded the left");
                var clip = Resources.Load<AudioClip>("Voice/" + id);
                Assert.That(clip, Is.Not.Null, id);
                Assert.That(clip.channels, Is.EqualTo(1), $"{id}: mono");
                Assert.That(clip.frequency, Is.EqualTo(22050), $"{id}: 22.05 kHz");
            }
        }

        [Test]
        public void MoreInstrumentsPointsAtTheRoomAndTheInstrumentsGrid()
        {
            GuideLines.ReloadMerged();
            Assert.That(GuideLines.TextOf(FirstRun.LineMore), Does.Contain("Point at").And.Contain("pinch"), "point and pinch (the platform's RaySummon)");
            Assert.That(GuideLines.TextOf(FirstRun.LineMoreMenu), Does.Contain(MenuStyle.InstrumentsTitle), "the Instruments grid");
            TourTip more = TourTips.Get("tip_more_instruments");
            Assert.That(more.Trigger, Is.EqualTo(TourTrigger.MoreInstruments));
            Assert.That(more.Success, Is.EqualTo(TourSuccess.Brought));
            Assert.That(more.Demo, Is.EqualTo(DemoKind.Point));
            Assert.That(more.Anchor, Is.EqualTo(TourAnchor.DisplayedInstrument));
            Assert.That(more.Lines, Is.EqualTo(new[] { FirstRun.LineMore, FirstRun.LineMoreMenu }));
            Assert.IsTrue(more.VrOnly);
        }

        /// <summary>The fast first-timer tour (2026-11-16): the user's order (snow switches, the hand menu, Move instruments,
        /// the instrument set, Recordings, recentering), every line names the real control it shows, the whole script is
        /// short, and the old quick tour's Sunny switch is a tip now.</summary>
        [Test]
        public void TheFastTourShowsTheUsersSixThingsInOrderAndNamesTheRealControls()
        {
            Assert.That(FirstRun.Order, Is.EqualTo(new[]
            {
                FirstRunStep.Intro, FirstRunStep.Snow, FirstRunStep.Menu, FirstRunStep.Move, FirstRunStep.Set,
                FirstRunStep.Recordings, FirstRunStep.Recenter, FirstRunStep.Bye,
            }));
            GuideLines.ReloadMerged();
            Assert.That(GuideLines.TextOf(FirstRun.LFastSnow), Does.Contain("SNOW").And.Contain("FIRE").And.Contain("WIND").And.Contain("all around"));
            Assert.That(GuideLines.TextOf(FirstRun.LOpen), Does.Contain("palm"), "the summon gesture (Menu's SummonGesture)");
            Assert.That(GuideLines.TextOf(FirstRun.LFastMove), Does.Contain(MenuStyle.MoveLabel).And.Contain("pinch").And.Contain("drag"));
            Assert.That(GuideLines.TextOf(FirstRun.LFastSet), Does.Contain(MenuStyle.MySet).And.Contain(MenuStyle.OneAlone));
            Assert.That(GuideLines.TextOf(FirstRun.LFastRec), Does.Contain("REC").And.Contain("PLAY"));
            Assert.That(GuideLines.TextOf(FirstRun.LFastSaved), Does.Contain("recordings"));
            Assert.That(GuideLines.TextOf(FirstRun.LFastRecenter), Does.Contain(MenuStyle.ResetViewLabel).And.Contain("both hands").And.Contain("Recenter"));
            Assert.That(GuideLines.TextOf(FirstRun.LFastBye), Does.Contain("GUIDE"));
            Assert.That(FirstRun.RowMove, Is.EqualTo(MenuRowIds.Arrange));
            Assert.That(FirstRun.RowRecordings, Is.EqualTo(MenuRowIds.Recordings));
            Assert.That(FirstRun.RowResetView, Is.EqualTo(MenuRowIds.ResetView));
            Assert.That(FirstRun.RowSingle, Is.EqualTo(MenuRowIds.LayoutSingle));
            Assert.That(FirstRun.RowSet, Is.EqualTo(MenuRowIds.LayoutAll));
            Assert.That(FirstRun.SnowSwitch, Is.EqualTo(DeckGuide.SnowAnchor), "the SNOW lever's anchor");
            // Very fast: one short line per stop, about half a minute of speech in all.
            float speech = 0f;
            foreach (string id in FirstRun.AllLines) { Assert.That(GuideLines.SecondsOf(id), Is.LessThanOrEqualTo(4.3f), id); }
            foreach (string id in new[] { GuideLines.IntroVr, FirstRun.LFastSnow, FirstRun.LOpen, FirstRun.LFastMove, FirstRun.LFastSet,
                FirstRun.LFastRec, FirstRun.LFastSaved, FirstRun.LFastRecenter, FirstRun.LFastBye, GuideLines.Leave }) speech += GuideLines.SecondsOf(id);
            Assert.That(speech, Is.LessThan(32f), $"{speech:F1} s of speech");
            Assert.That(FirstRun.BeatMax, Is.LessThanOrEqualTo(8f), "no beat ever holds long");
            // The Sunny switch the old tour showed: its own tip on the Sunny screen, the same two lines.
            TourTip sw = TourTips.Get("tip_sunny_switch");
            Assert.That(sw, Is.Not.Null);
            Assert.That(sw.Lines, Is.EqualTo(new[] { FirstRun.LSunnyOff, FirstRun.LSunnyOn }));
            Assert.That(sw.TriggerArg, Is.EqualTo(MenuScreen.Guide.ToString()));
            Assert.That(sw.AnchorArg, Is.EqualTo(MenuRowIds.TipsOn));
            Assert.That(GuideLines.TextOf(FirstRun.LSunnyOff), Does.Contain(MenuStyle.TipsOnLabel), "the Sunny screen's switch");
        }

        /// <summary>"Move instruments" (2026-11-10): the knob, Lock and height tips are gone; a pinch on an instrument while the
        /// setting is off says where to turn it on (in the menu row's own words), the first grab says how to spin it, a hand
        /// trying to play while it is on hears that they are quiet and where Done is. Each has its clip; the bowl tip stays
        /// off until the bowls work wires it.</summary>
        [Test]
        public void TheMoveInstrumentsTipsAreReadyAndWired()
        {
            GuideLines.ReloadMerged();
            var want = new Dictionary<string, (string text, TourTrigger trigger)>
            {
                { "tip_move_turn_on", ("To move one, turn on Move instruments in the menu.", TourTrigger.InstrumentBodyPinch) },
                { "tip_arrange", ("Pinch an instrument and drag it where you like.", TourTrigger.ArrangeOn) },
                { "tip_turn", ("Turn your wrist to spin it all the way round.", TourTrigger.GrabStarted) },
                { "tip_move_quiet", ("Instruments are quiet while moving. Poke Done to play.", TourTrigger.MovePlayAttempt) },
                { "tip_bowl_sing", ("Rub the rim slowly and steadily to make the bowl sing.", TourTrigger.Off) },
            };
            foreach (var kv in want)
            {
                TourTip t = TourTips.Get(kv.Key);
                Assert.That(t, Is.Not.Null, kv.Key);
                Assert.That(t.Trigger, Is.EqualTo(kv.Value.trigger), kv.Key);
                Assert.That(GuideLines.TextOf(kv.Key), Is.EqualTo(kv.Value.text));
                Assert.That(GuideLines.SecondsOf(kv.Key), Is.InRange(0.5f, 4f), kv.Key + " has its clip");
            }
            Assert.That(GuideLines.TextOf("tip_move_turn_on"), Does.Contain(MenuStyle.MoveLabel), "the menu row's own words");
            Assert.That(TourTips.Get("tip_move_turn_on").Success, Is.EqualTo(TourSuccess.MoveTurnedOn), "learned by turning it on");
            Assert.IsTrue(TourTips.Get("tip_move_quiet").InArrange, "said while moving");
            Assert.That(TourTips.Get("tip_move_quiet").AnchorArg, Is.EqualTo(MenuRowIds.Done), "the hand points at Done");
            foreach (string gone in new[] { "tip_handle", "tip_handle_turn", "tip_handle_play_first", "tip_move_by_knob", "tip_locked", "tip_lock", "tip_arrange_lift" })
            {
                Assert.That(TourTips.Get(gone), Is.Null, gone + " is retired");
                Assert.IsFalse(GuideLines.Merged.Has(gone), gone + " has no line");
            }
            foreach (TourTip t in TourTips.All)
                foreach (string line in t.Lines)
                    Assert.That(GuideLines.TextOf(line).ToLowerInvariant(), Does.Not.Match(@"\bknobs?\b|\block"), t.Id + ": no knob or Lock any more");
        }

        [Test]
        public void TipsNameTheRealControls()
        {
            GuideLines.ReloadMerged();
            Assert.That(GuideLines.TextOf("tip_rec"), Does.Contain("STOP"));
            Assert.That(GuideLines.TextOf("tip_loop_layer"), Does.Contain("LOOP"));
            Assert.That(GuideLines.TextOf("tip_loop_clear"), Does.Contain("CLEAR"));
            Assert.That(GuideLines.TextOf("tip_loop_click"), Does.Contain("CLICK"));
            Assert.That(GuideLines.TextOf("tip_loop_tidy"), Does.Contain("TIDY"));
            Assert.That(GuideLines.TextOf("tip_deck_guide"), Does.Contain("GUIDE"));
            Assert.That(GuideLines.TextOf("tip_arrange_done"), Does.Contain("Done").And.Contain("Reset"));
            Assert.That(GuideLines.TextOf("tip_lesson_done"), Does.Contain("Next"));
            Assert.That(GuideLines.TextOf("tip_delete"), Does.Contain("Delete"));
            Assert.That(GuideLines.TextOf("tip_move_turn_on"), Does.Contain(MenuStyle.MoveLabel), "the setting's own name");
            Assert.That(GuideLines.TextOf("tip_mr_hands"), Does.Contain(MenuStyle.RealHands).And.Contain(MenuStyle.InstrumentsTitle), "where the switch lives now");
            Assert.That(GuideLines.TextOf("tip_reset_view"), Does.Contain(MenuStyle.ResetViewLabel), "the row's own name");
            Assert.That(GuideLines.TextOf("tip_summon"), Does.Contain("both hands").And.Contain("hold still"), "the hold, not a pinch in empty air");
            Assert.That(GuideLines.TextOf("tip_instruments"), Does.Contain(MenuStyle.OneAlone), "the Instruments screen's own words");
            Assert.That(GuideLines.TextOf("tip_options"), Does.Contain(MenuStyle.OptionsChip), "the tile's chip");
            Assert.That(MenuStyle.ExitConfirm, Is.EqualTo("Tap again to exit"));
            Assert.That(GuideLines.TextOf("tip_exit"), Does.StartWith("Tap it again"), "the row's own words");
        }
    }
}
