// The book's costs (sheet.md 7.3, 7.4; device item S5 measures the headset): every page of the book rastered at the
// texture's 3.2 texels per mm by the Burst job (with its mip chain), the draw list build on a worker, and the module's
// per-frame work (Decor + EndFrame) with a lesson running and a fingertip near a corner. Printed as [PERF] lines.
using System.Collections;
using System.Diagnostics;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Sheet;
using MusicRoom.Sheet.Raster;
using NUnit.Framework;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.PlayMode.Sheet
{
    [Category("Performance")]
    public class SheetPerfTests
    {
        TestBoot _boot;
        double _t = 10;

        [UnityTearDown] public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); _boot = null; }

        [UnityTest]
        public IEnumerator RasterTimePerPageAndTheFrameCost()
        {
            _boot = new TestBoot(headless: true, simulateHeadset: true, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            yield return _boot.Start();
            SheetModule m = null;
            foreach (var x in App.Current.Modules) if (x is SheetModule s) m = s;
            var sw = Stopwatch.StartNew();
            while ((!m.Ready || m.Pages.Pending) && sw.Elapsed.TotalSeconds < 30) { _t += 1 / 72.0; _boot.Step(1 / 72f); yield return null; }
            Assert.That(m.Ready, Is.True);
            double buildSum = 0, rasterSum = 0, buildMax = 0, rasterMax = 0;
            int pages = 0;
            using (var lane = new RasterLane(SheetPages.W, SheetPages.H))
            {
                for (int n = m.Book.FirstPage; n <= m.Book.LastPage; n++)
                {
                    var t0 = Stopwatch.StartNew();
                    var packed = PackedPage.Pack(m.Book.PageDraws(n), SheetPages.Ppm, n);
                    double b = t0.Elapsed.TotalMilliseconds;
                    t0.Restart();
                    lane.Run(packed, mips: true);
                    double r = t0.Elapsed.TotalMilliseconds;
                    buildSum += b; rasterSum += r; buildMax = System.Math.Max(buildMax, b); rasterMax = System.Math.Max(rasterMax, r);
                    pages++;
                }
            }
            Debug.Log($"[PERF] sheet raster (Burst, 1024 x 781 + mips): mean {rasterSum / pages:F1} ms, worst {rasterMax:F1} ms; draw list build mean {buildSum / pages:F1} ms, worst {buildMax:F1} ms; {pages} pages (editor, Apple silicon)");
            Assert.That(rasterMax, Is.LessThan(250), "a page well inside a riffle on the desktop");
            // the frame: a lesson running, a fingertip hovering near the right corner
            m.TryGetAnchor("sheet.corner.right", out var corner, out var up);
            _boot.Hands.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, corner + up * 0.2f, -up, UnityEngine.Vector3.Cross(-up, UnityEngine.Vector3.right).normalized).WithPointing().WithIndexTipAt(corner + up * 0.04f));
            App.Current.Get<ITutorial>().Start(m.Book.Songs[0].Lesson.Id);
            for (int i = 0; i < 400; i++) { _t += 1 / 72.0; _boot.Step(1 / 72f); if (i % 20 == 0) yield return null; }
            var fw = Stopwatch.StartNew();
            const int frames = 2000;
            for (int i = 0; i < frames; i++) { m.Tick(FramePhase.Decor, 1 / 72f); m.Tick(FramePhase.EndFrame, 1 / 72f); }
            double us = fw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            Debug.Log($"[PERF] sheet frame (Decor + EndFrame, lesson running, fingertip near a corner): {us:F1} us; texture array {m.Pages.TextureBytes / 1e6:F2} MB");
            Assert.That(us, Is.LessThan(500), "a small share of the 11 ms frame");
        }
    }
}
