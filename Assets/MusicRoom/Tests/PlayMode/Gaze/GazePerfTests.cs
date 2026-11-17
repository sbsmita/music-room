// The cost of look and pinch (Category Performance: `Tools/unity.sh test-perf`): the Gaze module's whole frame (the head
// pose, the gate, the hands' free check, the two Interaction SDK hands and pinch selectors, the head gaze interactor's Drive
// over the menu and ten instrument targets with the occluders, the highlight) on the booted app, in the editor (Apple
// silicon): gazing at an instrument on display, at a menu row, and at nothing. Printed as a [PERF] line; the budget is well
// under 0.2 ms a frame. The GPU side is nothing new: the menu's row highlight is the face shader's own, the instrument's glow
// one more quad in the hover glows' one draw.
using System.Collections;
using System.Diagnostics;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.PlayMode.Gaze
{
    [Category("Performance")]
    public class GazePerfTests : GazeTestBase
    {
        double Measure(int frames)
        {
            for (int i = 0; i < 200; i++) Gaze.Tick(FramePhase.InputDone, Dt);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++) Gaze.Tick(FramePhase.InputDone, Dt);
            return sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
        }

        [UnityTest]
        public IEnumerator LookAndPinchCostsLittleAFrame()
        {
            yield return BootApp(headless: false);
            InstrumentKind? k = Displayed(out Vector3 c);
            Assert.That(k.HasValue, Is.True);
            const int frames = 3000;
            double inst = Measure(frames);
            Assert.That(Gaze.Target, Is.EqualTo(GazeTargetKind.Instrument));
            yield return OpenMenu();
            Look(RowCentre("instruments"));
            StepSeconds(0.6f);
            double row = Measure(frames);
            Assert.That(Gaze.Target, Is.EqualTo(GazeTargetKind.MenuRow));
            MenuM.Hide();
            Looking = Quaternion.Euler(70f, 0f, 0f);
            StepSeconds(0.5f);
            double none = Measure(frames);
            Debug.Log($"[PERF] look and pinch: gazing at an instrument {inst:F1} us/frame, at a menu row {row:F1} us/frame, at nothing {none:F1} us/frame (editor, Apple silicon; Interaction SDK hands, selectors and head gaze interactor included)");
            Assert.That(inst, Is.LessThan(150.0), "well under 0.2 ms a frame");
            Assert.That(row, Is.LessThan(150.0));
            Assert.That(none, Is.LessThan(150.0));
        }
    }
}
