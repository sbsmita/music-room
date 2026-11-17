// The fast first-timer tour (FirstRun.cs, 2026-11-16) in mixed reality: one of MRUK's recorded rooms, the real Layout
// (instruments open in front of the player, the music room hidden), Show me around, a player who does nothing, real
// frames. Every stop in order, every ghost-hand target the real thing in the player's room (the side table's SNOW lever,
// the menu rows, the deck, an instrument on display) or, where a thing is not here, described without pointing; no note,
// playback or lesson; done within 45 s of the tour's own clock.
#if UNITY_EDITOR
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Tests.PlayMode.Guide;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.MR
{
    public class FastTourMrTests : MrTestRig
    {
        protected override bool UseContractLayout => false;
        protected override bool TableOnEnter => false;
        protected override void Prepare(MemorySettingsStore settings) => settings.SetInt(AppSettings.KeyTourDone, 1);

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator InMixedRealityEveryStopPointsAtTheRealThingAndNothingPlays()
        {
            yield return EnterRoom("MeshLivingRoom2");
            yield return WaitFor(() => !Mr.IsPlacing, 5f);
            Assert.IsTrue(Mr.Active, "in the player's room");
            Assert.IsFalse(App.Get<IRoom>().Visible, "the music room is hidden");
            GuideModule guide = null;
            foreach (var m in App.Modules) if (m is GuideModule g) guide = g;
            Assert.That(guide, Is.Not.Null);
            guide.Start();   // Show me around
            FirstRun run = guide.FirstRun;
            Assert.IsTrue(run.Running && run.Replay);
            var check = new FastTourCheck(App, run, guide.Sunny);
            float t0 = Time.realtimeSinceStartup;
            while (run.Running && Time.realtimeSinceStartup - t0 < 120f)
            {
                yield return null;
                check.Frame();
            }
            check.Dispose();
            Debug.Log($"[FASTTOUR] mixed reality: {run.Clock:F1} s tour clock, {Time.realtimeSinceStartup - t0:F1} s real; {check.Report()}");
            Assert.IsFalse(run.Running, "the tour ended by itself");
            Assert.That(check.Steps, Is.EqualTo(FastTourCheck.Stops), "each stop once, in order");
            foreach (GuideStepEvent e in run.Outcomes) Assert.That(e.Outcome, Is.EqualTo(StepOutcome.Done), e.StepId);
            Assert.That(run.Clock, Is.LessThanOrEqualTo(45f), "fast");
            check.AssertAllTargets(allowAbsent: true, "MR");
            check.AssertNothingPlayed("MR");
            Assert.IsTrue(Mr.Active, "still in the player's room");
        }
    }
}
#endif
