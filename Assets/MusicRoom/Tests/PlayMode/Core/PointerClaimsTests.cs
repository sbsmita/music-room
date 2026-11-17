// platform.md 12.1 PointerClaimsTests: Core step 3b. A fingertip in the piano's zone is claimed by the piano; in the shown
// menu's zone by the menu; by a registered IPointerClaimant (Sunny's poke sphere, the sheet music's pages) otherwise.
using System.Collections;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode
{
    public class PointerClaimsTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _audio = 10.0;

        sealed class ClaimAll : IPointerClaimant { public bool On; public bool ClaimsPoint(Vector3 world) => On; }

        IEnumerator Boot(string mode, string current)
        {
            _boot = new TestBoot(headless: true, simulateHeadset: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, mode);
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, current);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            while (App.Current.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Step(0.3f);
        }

        [UnityTearDown]
        public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); _boot = null; }

        void Frame() { _audio += Dt; _boot.Step(Dt); }
        void Step(float s) { for (int i = 0; i < Mathf.RoundToInt(s / Dt); i++) Frame(); }
        Pointer RightIndex => App.Current.Input.Hand(Handedness.Right).IndexPointer;

        [UnityTest]
        public IEnumerator TheKeysClaimAFingertipAndTheMenuItsZone()
        {
            yield return Boot("all", "piano");
            IPianoApi piano = App.Current.Get<IPianoApi>();
            piano.TryGetKey(60, out PianoKeyInfo key);
            _boot.Hands.SetPose(HandSide.Right, FixtureContext.IndexAt(piano.Root.TransformPoint(new Vector3(key.X, 0.03f, 0.05f))));
            Frame(); Frame();
            Assert.That(RightIndex.ClaimedBy, Is.SameAs(piano), "a fingertip over the keys belongs to the piano");
            _boot.Hands.SetPose(HandSide.Right, FixtureContext.IndexAt(App.Current.Head.position + Vector3.up * 0.8f));
            Frame(); Frame();
            Assert.That(RightIndex.ClaimedBy, Is.Null, "in the air nobody claims it");
            IMenuPanel menu = App.Current.Get<IMenuPanel>();
            menu.Show();
            Step(0.6f);
            Bounds? zone = menu.GrabZone;
            Assert.IsTrue(zone.HasValue);
            // A point of the menu's zone outside every instrument's zone (instruments claim first, in TickOrder).
            var rigs = App.Current.Get<IRigRegistry>().Rigs;
            Vector3 at = zone.Value.center;
            bool found = false;
            for (int i = 0; i < 125 && !found; i++)
            {
                Vector3 q = zone.Value.min + Vector3.Scale(zone.Value.size, new Vector3((i % 5 + 0.5f) / 5f, (i / 5 % 5 + 0.5f) / 5f, (i / 25 + 0.5f) / 5f));
                if (rigs.Any(r => r.InScene && r.Zone != null && r.Zone.Contains(q) && r.Id != RigId.Lessons)) continue;
                at = q; found = true;
            }
            Assert.IsTrue(found, "part of the menu's zone is clear of the instruments");
            _boot.Hands.SetPose(HandSide.Right, FixtureContext.IndexAt(at));
            Frame(); Frame();
            Assert.That(RightIndex.ClaimedBy, Is.SameAs(menu), "inside the shown menu's zone the menu claims it");
        }
    }
}
