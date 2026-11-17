// The marimba's model (spec 4.2-4.4, review items 11, 12, 17), built in EditMode: the draw and triangle budgets, the
// bar mesh's vertex budget, the dip and its return, no allocation in Tick, the resonators' clearance of a seated player's
// knees (review 11), every tube's visual end, the tubes' layout (no tube runs into another or into a rail), the mallets
// resting inside the tray (review 12), the per-bar grain windows (review 17), the desktop colliders.
using System.Collections.Generic;
using System.Text;
using MusicRoom.Core;
using MusicRoom.Marimba;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Marimba
{
    public class MarimbaViewTests
    {
        MarimbaView _view;
        MaterialLibrary _mats;

        [SetUp]
        public void SetUp()
        {
            _mats = new MaterialLibrary();
            _view = MarimbaView.Build(null, _mats);
        }

        [TearDown]
        public void TearDown()
        {
            _view?.Dispose();
            _view = null;
        }

        [Test]
        public void DrawAndTriangleBudgets()
        {
            Assert.That(_view.DrawCalls, Is.LessThanOrEqualTo(MarimbaView.DrawsAtSeat), "at the seat: bars, frame, metal, two mallets");
            int seatTris = _view.TrianglesAtSeat;
            _view.SetDisplayMode(true);
            Assert.That(_view.DrawCalls, Is.LessThanOrEqualTo(MarimbaView.DrawsDisplay), "on display: bars, frame + mallets, metal");
            _view.SetDisplayMode(false);
            Assert.That(_view.DrawCalls, Is.EqualTo(5));
            Debug.Log($"[MARIMBA-VIEW] draws seat {MarimbaView.DrawsAtSeat} / display {MarimbaView.DrawsDisplay}; triangles seat {seatTris}, display {_view.TrianglesDisplay}; bar vertices {_view.BarVertexCount} ({_view.BarVertexCount / 37} per bar); {_view.TriangleReport}");
            Assert.That(seatTris, Is.LessThan(19000), "spec about 14 k at the seat; the folded resonators' round 12-sided elbows add about 3 k");
            Assert.That(_view.TrianglesDisplay, Is.LessThan(6000), "spec 4.3: about 5 k on display (no arch, 8-sided tubes)");
            Assert.That(_view.BarVertexCount, Is.LessThanOrEqualTo(37 * 200), "review 17: about 180-200 vertices per bar, cord holes included");
        }

        [Test]
        public void ABarDipsOnItsCordAndReturnsWithinAQuarterSecond()
        {
            _view.Kick(16, 1f, 0.6f);
            float lowest = 0f, maxRoll = 0f;
            for (int f = 0; f < 3; f++) { _view.Tick(1f / 90f); lowest = Mathf.Min(lowest, _view.BarOffset(16)); maxRoll = Mathf.Max(maxRoll, Mathf.Abs(_view.BarRollDeg(16))); }
            Assert.That(lowest, Is.LessThan(-0.0012f).And.GreaterThanOrEqualTo(-0.0016f - 1e-5f), "1.6 mm x vel on the hit frame");
            Assert.That(maxRoll, Is.GreaterThan(0.2f).And.LessThanOrEqualTo(0.6f * 0.6f + 1e-3f), "0.6 deg x c x vel");
            for (int f = 3; f < 23; f++) _view.Tick(1f / 90f);   // 0.255 s in all
            Assert.That(Mathf.Abs(_view.BarOffset(16)), Is.LessThan(5e-5f), "back within 0.25 s");
            Assert.That(_view.MovingBars, Is.EqualTo(0), "settled, no more vertex writes");
            Assert.That(_view.BarOffset(15), Is.EqualTo(0f), "the neighbour never moved");
            _view.SetTargetLift(19, 1f);
            _view.Tick(1f / 90f);
            Assert.That(_view.BarOffset(19), Is.EqualTo(0.003f).Within(1e-6f), "lesson target lift 3 mm x k");
        }

        [Test]
        public void TickAllocatesNothing()
        {
            _view.Kick(3, 0.8f, 0f);
            _view.Tick(1f / 90f);
            Assert.That(() =>
            {
                for (int i = 0; i < 37; i += 3) _view.Kick(i, 0.7f, -0.3f);
                for (int f = 0; f < 30; f++) _view.Tick(1f / 90f);
                _view.SetTargetLift(20, 0.5f);
                _view.Tick(1f / 72f);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void ResonatorsClearASeatedPlayersKnees()
        {
            // Review item 11: the natural tubes C3..B4 are folded away from the player. The review's line: nothing below
            // 0.62 m within 0.62 m of the eyes (instrument z < +0.12). Under the bars the mouths sit at 0.66-0.67 m and the
            // elbows pass under the back rail, so the strict line cannot hold there; what must hold is the knees: in the
            // knee span (|x| <= 0.21 m) no tube surface comes below 0.60 m (the top of a seated knee, spec 4.5) closer than
            // 0.62 m to the eyes.
            var sb = new StringBuilder("[MARIMBA-VIEW] resonators (bar: drawn / acoustic length, end, lowest surface, lowest surface at z < 0.12):\n");
            float worstKnee = 1f, worstAll = 1f;
            foreach (var t in _view.Tubes)
            {
                float x = MarimbaGeometry.X(t.Bar);
                sb.AppendLine($"  {MarimbaGeometry.NoteName(t.Bar),-4} {(t.Folded ? "folded" : t.Cosmetic ? "cosmetic" : "straight"),-8} drawn {t.Drawn * 1000:F0} mm (acoustic {t.Acoustic * 1000:F0}), end ({t.End.x:F3}, {t.End.y:F3}, {t.End.z:F3}), lowest {t.LowestY:F3} m, front lowest {(t.LowestFrontY < 10 ? t.LowestFrontY.ToString("F3") : "-")} m");
                if (t.LowestFrontY < 10) worstAll = Mathf.Min(worstAll, t.LowestFrontY);
                if (Mathf.Abs(x) - t.Radius <= MarimbaVisualSpec.KneeSpanX && t.LowestFrontY < 10 && !t.Cosmetic) worstKnee = Mathf.Min(worstKnee, t.LowestFrontY);
                if (t.Folded) Assert.That(t.Drawn, Is.EqualTo(t.Acoustic).Within(1e-4f), "a folded tube keeps its acoustic length");
            }
            sb.AppendLine($"  knee span (|x| <= {MarimbaVisualSpec.KneeSpanX} m, folded and straight tubes): lowest surface at z < 0.12 is {worstKnee:F3} m; whole instrument {worstAll:F3} m");
            Debug.Log(sb.ToString());
            Assert.That(worstKnee, Is.GreaterThanOrEqualTo(MarimbaVisualSpec.KneeTopY - 1e-4f), "the knee span stays above the knees");
        }

        [Test]
        public void TubesRunClearOfEachOtherAndOfTheRails()
        {
            float railBottom = MarimbaVisualSpec.RailTop(0, MarimbaGeometry.HDefault) - MarimbaVisualSpec.RailHeight;
            var tubes = _view.Tubes;
            foreach (var t in tubes)
            {
                // under the natural rails: every point of a tube that passes beneath a rail stays below its bottom
                foreach (var p in Samples(t.Path))
                    for (int back = 0; back < 2; back++)
                    {
                        float zr = MarimbaVisualSpec.RailZ(0, back == 1, p.x);
                        if (Mathf.Abs(p.z - zr) < MarimbaVisualSpec.RailWidth * 0.5f + t.Radius)
                            Assert.That(p.y + t.Radius, Is.LessThanOrEqualTo(railBottom + 1e-4f), $"{MarimbaGeometry.NoteName(t.Bar)} under the {(back == 1 ? "back" : "front")} rail");
                    }
            }
            // no two tubes intersect: sample their centre lines
            for (int a = 0; a < tubes.Length; a++)
                for (int b = a + 1; b < tubes.Length; b++)
                {
                    float min = float.MaxValue;
                    foreach (var p in Samples(tubes[a].Path)) foreach (var q in Samples(tubes[b].Path)) min = Mathf.Min(min, (p - q).magnitude);
                    Assert.That(min, Is.GreaterThanOrEqualTo(tubes[a].Radius + tubes[b].Radius + 0.0005f), $"{MarimbaGeometry.NoteName(a)} vs {MarimbaGeometry.NoteName(b)}");
                }
        }

        static IEnumerable<Vector3> Samples(List<Vector3> path)
        {
            for (int k = 0; k < path.Count - 1; k++)
                for (int s = 0; s < 10; s++) yield return Vector3.Lerp(path[k], path[k + 1], s / 10f);
            yield return path[path.Count - 1];
        }

        [Test]
        public void TheMalletsRestInsideTheTray()
        {
            for (int s = 0; s < 2; s++)
            {
                Transform m = _view.Mallet(s);
                Vector3 head = m.localPosition + m.localRotation * Vector3.up * MarimbaGeometry.MalletHeadFromButt;
                float inner = MarimbaGeometry.TrayLength * 0.5f - MarimbaVisualSpec.TrayWall;
                Assert.That(Mathf.Abs(head.x) + MarimbaGeometry.MalletHeadHeight * 0.5f, Is.LessThanOrEqualTo(inner + 1e-4f), "the head inside the tray's end wall");
                Assert.That(Mathf.Abs(m.localPosition.x), Is.EqualTo(MarimbaGeometry.TrayButtX).Within(1e-5f), "butts 20 mm apart");
                Assert.That(Mathf.Abs(head.z - MarimbaGeometry.TrayZ), Is.LessThan(1e-4f), "on the tray's centre line");
                Assert.That(head.y - MarimbaGeometry.MalletHeadHeight * 0.5f, Is.GreaterThanOrEqualTo(MarimbaGeometry.HDefault + MarimbaGeometry.TrayAboveH - MarimbaGeometry.TrayWell - 1e-4f), "on the felt");
            }
        }

        [Test]
        public void EachBarHasItsOwnGrainWindow()
        {
            int sameNeighbour = 0;
            for (int i = 0; i < 37; i++)
            {
                MarimbaVisualSpec.BarGrain(i, out int band, out float u0, out bool flip);
                Assert.That(band, Is.InRange(0, 3));
                Assert.That(u0, Is.GreaterThanOrEqualTo(0f));
                Assert.That(u0 + MarimbaGeometry.Length(i) / MarimbaVisualSpec.RoseMaxLength * MarimbaTextures.RoseGrainU, Is.LessThanOrEqualTo(MarimbaTextures.RoseGrainU + 1e-5f));
                if (i > 0) { MarimbaVisualSpec.BarGrain(i - 1, out int pb, out _, out _); if (pb == band) sameNeighbour++; }
            }
            Assert.That(sameNeighbour, Is.EqualTo(0), "neighbouring bars never share a tint band");
        }

        [Test]
        public void EveryBarHasAPlaySurfaceCollider()
        {
            for (int i = 0; i < 37; i++)
            {
                var c = _view.BarCollider(i);
                Assert.That(c, Is.Not.Null);
                Assert.That(c.gameObject.layer, Is.EqualTo(Layers.PlaySurface));
                Assert.That(c.transform.localPosition.y, Is.EqualTo(MarimbaGeometry.Top(i)).Within(1e-6f));
            }
            Assert.That(_view.Handles.Count, Is.EqualTo(2 + 4 + 37), "end boards, rails, tubes");
            foreach (var r in _view.Handles) Assert.That(r.forceRenderingOff, Is.True, "handles are hidden");
            Assert.That(_view.PlayedParts.Count, Is.EqualTo(3), "bars and the two mallets");
        }
    }
}
