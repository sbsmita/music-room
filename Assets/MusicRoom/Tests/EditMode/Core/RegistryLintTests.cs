// platform.md 3.4: the shared modules never name a specific instrument. Layout, Menu, Lessons, Guide, MR, Room and Core
// (outside Core/Registry) must not contain InstrumentKind.<X>, RigId.<X> or an id string literal ("handpan"); Audio must not
// contain UnitId.<X> / SourceId.<X> outside Audio/Sound/<Role> (each role's own folder) and the AudioSourceTable. Comments
// are ignored. The allowlist keeps the tested classic rules, each with its reason and a ceiling on its hit count, so an
// allowlisted file cannot quietly grow new instrument switches; any other hit fails with the file and the line.
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Core
{
    public class RegistryLintTests
    {
        const string Kinds = "Piano|Drums|Harp|Handpan|HandDrums|Marimba|Bowls";
        static readonly Regex s_names = new Regex(@"\b(InstrumentKind|RigId)\.(" + Kinds + @")\b|""(piano|drums|harp|handpan|handdrums|marimba|bowls)""");
        static readonly Regex s_audio = new Regex(@"\bUnitId\.(" + Kinds + @")\b|\bSourceId\.(PianoLo|PianoMid|PianoHi|Harp|Kick|Snare|Hat|Tom1|Tom2|Floor|Crash|Ride|DrumRoom|Handpan|HandDrumLo|HandDrumHi|MarimbaLo|MarimbaMid|MarimbaHi|Bowls)\b");

        /// <summary>File (relative to Assets/MusicRoom) -> (the most hits allowed, why).</summary>
        static readonly Dictionary<string, (int max, string why)> s_allow = new Dictionary<string, (int, string)>
        {
            // Core contracts: the classic set as the stand-in default of ILayoutService / IMixedReality (a stand-in without a
            // Layout or MR module), the classic take types; AppSettings: the persisted default "piano" of musicroom.current.
            ["Core/Contracts/Rigs.cs"] = (4, "ILayoutService default bodies: the classic set and lead when no Layout runs"),
            ["Core/Contracts/MixedReality.cs"] = (1, "TryGetLeadPose default: the piano's own pose"),
            ["Core/Contracts/Lessons.cs"] = (3, "the classic take types"),
            ["Core/App/AppSettings.cs"] = (5, "musicroom.current's persisted default (\"piano\")"),
            // Layout: the piano's key space (a pinch over the keys is play, never a grab) and its mixed-reality placing.
            ["Layout/LayoutModule.Grab.cs"] = (6, "the piano key space and the MR placing pinch (allowlisted, platform.md 3.4)"),
            ["Layout/LayoutModule.Arrange.cs"] = (6, "the piano's home height and MR placing (piano rules)"),
            ["Layout/LayoutModule.cs"] = (4, "the piano's home height outside the layout (piano rule)"),
            ["Layout/LayoutModule.ArrangeMode.cs"] = (1, "the piano on a real table cannot be lifted (piano rule)"),
            ["Layout/LayoutModule.Gestures.cs"] = (1, "MR placing needs the piano (piano rule)"),
            // Lessons: the tutorial's three classic code paths (TutorialTests).
            ["Lessons/Tutorial.cs"] = (20, "the tutorial's three classic paths"),
            ["Lessons/Tutorial.FirstFive.cs"] = (2, "the live target Sunny points at on the tutorial's classic paths (first five; the generic path asks ILessonTargets)"),
            ["Lessons/LessonHints.cs"] = (6, "the classic hints"),
            // Lessons: the ghost hands play each instrument's own way (the piano's ten-finger fingering, the kit's sticks, the
            // harp's pluck, a mallet, a rub), like the tutorial's classic paths; a tested choreography (GhostHandsPlayTests).
            ["Lessons/Ghost/GhostHands.cs"] = (22, "the ghost hands' per-instrument playing (fingering, sticks, pluck, mallet, rub; the drummer's own stool)"),
            // Guide: the deck walkthrough's song (drums, harp, piano in layers), a tested script.
            ["Guide/DeckGuide.cs"] = (4, "the deck walkthrough's classic song"),
            // Guide (first five): the first run and the tour's tips teach the classic three by name (tested scripts:
            // FirstRunWalkthroughTests, TourTipsTests); the new instruments' tips are their descriptors'.
            ["Guide/FirstRun/FirstRun.cs"] = (1, "the quick tour's drum step: the drums at the seat (FirstRunWalkthroughTests)"),
            ["Guide/FirstRun/TourSignals.cs"] = (7, "the classic tips' signals: the kit and harp gazes, a drum lesson's kick, harp lessons, lifting the piano or kit"),
            ["Guide/FirstRun/TourTipRunner.cs"] = (3, "the classic tips' targets: the kit and the harp present, the piano's lower grip"),
            // Core: the song's stars read each instrument's own pitch from its take events (the piano's / marimba's midi, the
            // harp's string, the kit's pieces low to high, the hand drums, the bowls, the handpan's field) for the sky and the
            // Recordings rows' pictures (SongSkyModelTests, MenuTakeStarsTests).
            ["Core/Recording/SongStars.cs"] = (22, "the song's stars: each instrument's pitch from its take events"),
            // Room: the playback show's colours per instrument (the sky's stars, the window's northern lights).
            ["Room/Decor/SongSky.cs"] = (4, "the song's stars: where each instrument's star starts (its keys, pieces, strings)"),
            ["Room/Decor/WindowShowModel.cs"] = (3, "the northern lights' band and colour per instrument"),
            // Room: the wall paint's pigment per instrument (a palette table; every other part of the paint is generic).
            // Room: each instrument paints its own nature motif (the piano's flowering vine, the kit's trees, grass and leaves,
            // the harp's birds, the bowls' clouds, the handpan's butterflies, the hand drums' mushrooms, the marimba's slim
            // trees), each where it belongs on the wall, and the gallery's landscape is composed from the take's own events
            // with the same motifs (PaintBrushTests, PaintShowPlayTests).
            ["Room/Decor/PaintBrushes.cs"] = (19, "the paint's nature motif per instrument and where it belongs"),
            ["Room/Decor/WallPaint.cs"] = (9, "the paint's brush per instrument (which marks each note leaves)"),
            ["Room/Decor/GalleryPicture.cs"] = (20, "the gallery landscape's motif per instrument (trees, flowers, birds, ...), from a take's events"),
            ["Room/Decor/PaintPalette.cs"] = (12, "the wall paint's pigment and pitch reading per instrument"),
            ["Room/RoomMoments.cs"] = (1, "the first run's celebration: a rolled chord on the harp when it is in the room"),
            // MR: the piano's E9 anchors (allowlisted, platform.md 9.4).
            ["MR/MrModule.cs"] = (1, "the piano's E9 anchor"),
        };

        static readonly Dictionary<string, (int max, string why)> s_allowAudio = new Dictionary<string, (int, string)>
        {
            ["Audio/Burst/Units/UnitSlot.cs"] = (12, "the id block's Render switch (phase 0 only)"),
            ["Audio/Burst/Units/SoundUnit.cs"] = (16, "the id block's Create switch, Roles, the classic sources"),
            ["Audio/Burst/Units/ExampleUnit.cs"] = (2, "the engine's reference unit plays the piano role in its tests"),
            ["Audio/Engine/AudioRig.cs"] = (40, "the AudioSourceTable and the classic routing"),
            ["Audio/Engine/AudioEngine.cs"] = (14, "the classic sound's piano / drum / harp paths"),
            ["Audio/Engine/IAudioEngine.cs"] = (2, "the classic harp's partial decay"),
            ["Audio/Synths/DrumSynth.cs"] = (12, "the classic drum synth"),
            ["Audio/Synths/PianoSynth.cs"] = (2, "the classic piano synth"),
            ["Audio/Synths/StringSynth.cs"] = (2, "the classic harp synth"),
            ["Audio/AudioModule.cs"] = (4, "the classic module wiring"),
            ["Audio/Synths/GuideVoice.cs"] = (2, "Sunny's own source"),
        };

        static string Root => Path.Combine(Application.dataPath, "MusicRoom");

        static string StripComments(string line)
        {
            int i = line.IndexOf("//", System.StringComparison.Ordinal);
            return i >= 0 ? line.Substring(0, i) : line;
        }

        static void Scan(string folder, Regex rx, Dictionary<string, (int max, string why)> allow, System.Func<string, bool> skip, List<string> fails)
        {
            string dir = Path.Combine(Root, folder);
            if (!Directory.Exists(dir)) return;
            foreach (string f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = f.Substring(Root.Length + 1).Replace('\\', '/');
                if (skip != null && skip(rel)) continue;
                var hits = new List<string>();
                string[] lines = File.ReadAllLines(f);
                bool block = false;
                for (int n = 0; n < lines.Length; n++)
                {
                    string l = lines[n];
                    if (block) { int e = l.IndexOf("*/", System.StringComparison.Ordinal); if (e < 0) continue; l = l.Substring(e + 2); block = false; }
                    int b = l.IndexOf("/*", System.StringComparison.Ordinal);
                    if (b >= 0 && l.IndexOf("*/", b, System.StringComparison.Ordinal) < 0) { block = true; l = l.Substring(0, b); }
                    l = StripComments(l);
                    if (rx.IsMatch(l)) hits.Add($"{rel}:{n + 1}: {lines[n].Trim()}");
                }
                if (hits.Count == 0) continue;
                if (allow.TryGetValue(rel, out var a))
                {
                    if (hits.Count > a.max) fails.Add($"{rel}: {hits.Count} instrument names (allowlisted up to {a.max}: {a.why})\n  " + string.Join("\n  ", hits));
                    continue;
                }
                fails.AddRange(hits);
            }
        }

        [Test]
        public void SharedModulesNeverNameASpecificInstrument()
        {
            var fails = new List<string>();
            foreach (string folder in new[] { "Layout", "Menu", "Lessons", "Guide", "MR", "Room" })
                Scan(folder, s_names, s_allow, null, fails);
            Scan("Core", s_names, s_allow, rel => rel.StartsWith("Core/Registry/"), fails);
            Assert.That(fails, Is.Empty, "platform.md 3.4: read the registry instead\n" + string.Join("\n", fails));
        }

        [Test]
        public void AudioNamesRolesOnlyInTheirOwnFoldersAndTheSourceTable()
        {
            var fails = new List<string>();
            Scan("Audio", s_audio, s_allowAudio, rel => rel.StartsWith("Audio/Sound/"), fails);
            Assert.That(fails, Is.Empty, "platform.md 3.4\n" + string.Join("\n", fails));
        }

        [Test]
        public void TheLintFindsANameItShould()
        {
            Assert.IsTrue(s_names.IsMatch("if (k == InstrumentKind.Bowls) return;"));
            Assert.IsTrue(s_names.IsMatch("string id = \"handpan\";"));
            Assert.IsFalse(s_names.IsMatch("InstrumentKind.Table"));
            Assert.IsTrue(s_audio.IsMatch("audio.ActiveUnit(UnitId.Marimba)"));
            Assert.That(StripComments("int a; // InstrumentKind.Piano"), Is.EqualTo("int a; "));
        }
    }
}
