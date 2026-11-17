// EditMode checks of the MR module against its reference numbers, produced by running the reference placement logic
// headless in node (ARMode.place); they are in right-handed coordinates (-z forward) and converted here with
// (x, y, z) -> (x, y, -z), quaternion (x, y, z, w) -> (-x, -y, z, w). (The snowy window's and the snow portals' checks went
// with them, 2026-11-13: the snow falls in the whole room now.)
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.MR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;   // the AllocatingGCMemory() extension
using Is = NUnit.Framework.Is;

namespace MusicRoom.Tests.MR
{
    public class MrLogicTests
    {
        static Vector3 U(float x, float y, float z) => new Vector3(x, y, -z);              // right-handed -> Unity
        static Quaternion UQ(float x, float y, float z, float w) => new Quaternion(-x, -y, z, w);

        GameObject _parent;
        MrMaterials _mats;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("MR test root");
            _mats = new MrMaterials(null);
        }

        [TearDown]
        public void TearDown()
        {
            _mats.DestroyOwn();
            UnityEngine.Object.DestroyImmediate(_parent);
        }

        // ---------------- the piano on a surface (ARMode.place) ----------------

        [Test]
        public void PlaceMatchesTheReferenceOnTablesAndFloors()
        {
            // (head, hit) -> (pos, right-handed rotation.y, onTable), from ARMode.place() with piano.tableLift .092.
            Case(U(0.3f, 1.2f, 0.1f), U(0.5f, 0.74f, -0.8f), U(0.554233f, 0.832f, -1.044047f), -0.218669f, true);
            Case(U(0f, 1.1f, 0f), U(-0.6f, 0f, -1.2f), U(-0.711803f, 0.72f, -1.423607f), 0.463648f, false);
            Case(U(1f, 1.25f, -0.5f), U(2.2f, 0.45f, 0.4f), U(2.4f, 0.542f, 0.55f), -2.214297f, true);
            Case(U(0f, 1.2f, 0f), U(0f, 0.29f, -1f), U(0f, 1.01f, -1.25f), 0f, false); // 0.29 m: not a table, the stand stays
        }

        static void Case(Vector3 head, Vector3 hit, Vector3 wantPos, float rhYaw, bool wantTable)
        {
            Pose p = PianoPlacement.FromHit(hit, head, 0f, out bool onTable);
            Assert.That(onTable, Is.EqualTo(wantTable), $"onTable for {hit}");
            Assert.That(p.position, Is.EqualTo(wantPos).Using(new V3(1e-5f)), $"position for {hit}");
            float wantYaw = Coords.YawDegFromRightHandedRad(rhYaw);
            Assert.That(Mathf.DeltaAngle(p.rotation.eulerAngles.y, wantYaw), Is.EqualTo(0f).Within(1e-3f), $"yaw for {hit}");
            Assert.That(Vector3.Dot(p.rotation * Vector3.forward, MrUtil.Flat(hit - head, Vector3.forward)), Is.GreaterThan(0.9999f), "local +z away from the player");
            // The reticle shown for that hit faces the same way (reticle update: atan2(head - pos)).
            Quaternion r = PianoPlacement.ReticleRotation(hit, head, Vector3.forward);
            Assert.That(Quaternion.Angle(r, p.rotation), Is.LessThan(1e-3f));
        }

        [Test]
        public void PlaceMeasuresTablesFromTheRoomFloor()
        {
            Pose p = PianoPlacement.FromHit(new Vector3(0f, 0.45f, 1f), new Vector3(0f, 1.3f, 0f), 0.2f, out bool onTable);
            Assert.IsFalse(onTable, "0.25 m above a floor at 0.2: the floor case");
            Assert.That(p.position.y, Is.EqualTo(0.45f + 0.72f).Within(1e-6f));
            PianoPlacement.FromHit(new Vector3(0f, 0.55f, 1f), new Vector3(0f, 1.3f, 0f), 0.2f, out onTable);
            Assert.IsTrue(onTable);
        }

        [Test]
        public void ReticleAndLightConstantsAreTheReferenceValues()
        {
            // Reticle parts: ring (inner .06, outer .08, 40 segments) alpha .9; plane 1.326 x .294 at right-handed z -.393, alpha .12 / edges .6.
            Assert.That(MrConstants.RingInner, Is.EqualTo(0.06f));
            Assert.That(MrConstants.RingOuter, Is.EqualTo(0.08f));
            Assert.That(MrConstants.RingSegments, Is.EqualTo(40));
            Assert.That(MrConstants.FootprintWidth, Is.EqualTo(2f * 0.663f).Within(1e-6f));
            Assert.That(MrConstants.FootprintDepth, Is.EqualTo(0.004f + 0.29f).Within(1e-6f));
            Assert.That(MrConstants.FootprintZ, Is.EqualTo(0.25f - (0.004f - 0.29f) / 2f).Within(1e-6f));
            Assert.That(ColorUtility.ToHtmlStringRGB(MrConstants.ReticleColor), Is.EqualTo("FFC46A"));
            Assert.That(MrConstants.RingAlpha, Is.EqualTo(0.9f));
            Assert.That(MrConstants.FootprintAlpha, Is.EqualTo(0.12f));
            Assert.That(MrConstants.FootprintEdgeAlpha, Is.EqualTo(0.6f));
            // Sun: white directional light, intensity 1.3 (/ pi in URP), at (0.6, 2.4, 1.2); shadow catcher plane 2.6 x 1.8, opacity .32.
            Assert.That(MrConstants.SunPosition, Is.EqualTo(U(0.6f, 2.4f, 1.2f)));
            Assert.That(MrConstants.SunIntensity, Is.EqualTo(0.4138f).Within(1e-4f));
            Assert.That(MrConstants.CatcherWidth, Is.EqualTo(2.6f));
            Assert.That(MrConstants.CatcherDepth, Is.EqualTo(1.8f));
            Assert.That(MrConstants.CatcherOpacity, Is.EqualTo(0.32f));
            Assert.That(ColorUtility.ToHtmlStringRGB(MrConstants.AmbientSky), Is.EqualTo("B1B1B1"));
            Assert.That(ColorUtility.ToHtmlStringRGB(MrConstants.AmbientEquator), Is.EqualTo("969696"));
            Assert.That(ColorUtility.ToHtmlStringRGB(MrConstants.AmbientGround), Is.EqualTo("686868"));
        }

        [Test]
        public void ReticleMeshesHaveTheReferenceShapes()
        {
            var r = new PianoReticle(_parent.transform, _mats);
            try
            {
                var filters = r.Transform.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(filters.Length, Is.EqualTo(3));
                Bounds ring = filters[0].sharedMesh.bounds, foot = filters[1].sharedMesh.bounds;
                Assert.That(ring.extents.x, Is.EqualTo(0.08f).Within(1e-4f));
                Assert.That(ring.extents.y, Is.EqualTo(0f).Within(1e-6f), "flat on the surface");
                Assert.That(foot.size.x, Is.EqualTo(1.326f).Within(1e-5f));
                Assert.That(foot.size.z, Is.EqualTo(0.294f).Within(1e-5f));
                Assert.That(foot.center.z, Is.EqualTo(0.393f).Within(1e-5f), "beyond the point, away from the player");
                Assert.That(filters[2].sharedMesh.GetTopology(0), Is.EqualTo(MeshTopology.Lines));
                Assert.IsFalse(r.Visible);
                r.Show(new Vector3(1f, 0.74f, 2f), Quaternion.identity);
                Assert.IsTrue(r.Visible);
            }
            finally { r.Destroy(); }
        }

        // ---------------- the piano on a real table (TableFinder) ----------------

        /// <summary>A table top seen from the player: near edge at z = 1 (TowardUser -z), <paramref name="depth"/> deep.</summary>
        static TableSurface Top(float depth) => new TableSurface
        {
            TopY = 0.74f, TopHeight = 0.74f, EdgeMidpoint = new Vector3(0f, 0.74f, 1f), TowardUser = Vector3.back,
            EdgeLength = 1.5f, Depth = depth, TopCenter = new Vector3(0f, 0.74f, 1f + depth * 0.5f), TopSize = new Vector2(1.5f, depth),
        };

        [Test]
        public void TheFullPushNeedsADeepTopAndAShallowOneGetsAShorterPush()
        {
            // The piano body reaches REAR .29 behind the key fronts, which go PUSH_BACK .25 past the chosen point.
            Assert.That(TableFinder.BodyRear, Is.EqualTo(0.29f));
            Assert.That(TableQuery.Default.minDepth, Is.EqualTo(TableFinder.BodyRear + TableFinder.EdgeMargin).Within(1e-6f), "a top shallower than the body is refused");
            foreach (float depth in new[] { 0.31f, 0.35f, 0.45f, 0.56f, 0.8f })
            {
                var s = Top(depth);
                Pose p = TableFinder.PianoPoseOnTable(s, s.EdgeMidpoint);
                float keysIn = p.position.z - s.EdgeMidpoint.z;                 // key fronts this far in from the near edge
                float rear = keysIn + TableFinder.BodyRear;                        // the body's back
                Assert.That(keysIn, Is.InRange(0f, TableFinder.PushBack + 1e-6f), $"depth {depth}: never past PUSH_BACK, never off the near edge");
                Assert.That(rear, Is.LessThanOrEqualTo(depth - TableFinder.EdgeMargin + 1e-5f), $"depth {depth}: the body stays on the top");
                Assert.That(p.position.y, Is.EqualTo(0.74f + TableFinder.TableLift).Within(1e-6f));
                Assert.That(Vector3.Dot(p.rotation * Vector3.forward, -s.TowardUser), Is.GreaterThan(0.9999f), "facing the player");
            }
            // A 0.35 m console: the keys come 4 cm in instead of 25 (the body would overhang the far edge by .19 m).
            var console = Top(0.35f);
            Assert.That(TableFinder.PushFor(console, console.EdgeMidpoint), Is.EqualTo(0.04f).Within(1e-5f));
            // Deep enough (>= .56 m): the full PUSH_BACK.
            var desk = Top(0.74f);
            Assert.That(TableFinder.PushFor(desk, desk.EdgeMidpoint), Is.EqualTo(0.25f).Within(1e-6f));
            // A point chosen further in leaves less top behind it.
            Assert.That(TableFinder.PushFor(desk, desk.EdgeMidpoint + new Vector3(0f, 0f, 0.3f)), Is.EqualTo(0.13f).Within(1e-5f));
        }

        // ---------------- templates ----------------

        [Test]
        public void TemplatesExistWithTheirShadersAndQueues()
        {
            var provider = new MrTemplates();
            Assert.That(provider.ResourcesFolder, Is.EqualTo("Assets/MusicRoom/MR"));
            var names = new List<string>();
            foreach (var t in provider.Templates)
            {
                names.Add(t.Name);
                Assert.That(t.Name, Does.StartWith("MR/"), "module-prefixed template names");
                Assert.That(Shader.Find(t.ShaderName), Is.Not.Null, t.ShaderName);
            }
            // The snowy window's snow, frost and reticle overlay templates went with it (2026-11-13).
            Assert.That(names, Is.EquivalentTo(new[] { "MR/ShadowCatcher", "MR/Reticle", "MR/Panel" }));
            var catcher = _mats.Create(MrMaterials.ShadowCatcher);
            var reticle = _mats.Create(MrMaterials.Reticle);
            Assert.That(catcher.renderQueue, Is.EqualTo(2999), "just before the transparents");
            Assert.That(reticle.GetFloat(MrMaterials.ZTestId), Is.EqualTo((float)UnityEngine.Rendering.CompareFunction.LessEqual));
            var lib = new MaterialLibrary();
            Assert.That(lib.HasTemplate(MrMaterials.ShadowCatcher), Is.True, "render-setup wrote MR/ShadowCatcher.mat");
        }

        // ---------------- comparers ----------------

        sealed class V2 : IEqualityComparer<Vector2>
        {
            readonly float _e; public V2(float e) { _e = e; }
            public bool Equals(Vector2 a, Vector2 b) => Mathf.Abs(a.x - b.x) <= _e && Mathf.Abs(a.y - b.y) <= _e;
            public int GetHashCode(Vector2 v) => 0;
        }

        sealed class V3 : IEqualityComparer<Vector3>
        {
            readonly float _e; public V3(float e) { _e = e; }
            public bool Equals(Vector3 a, Vector3 b) => Mathf.Abs(a.x - b.x) <= _e && Mathf.Abs(a.y - b.y) <= _e && Mathf.Abs(a.z - b.z) <= _e;
            public int GetHashCode(Vector3 v) => 0;
        }

        sealed class V4 : IEqualityComparer<Vector4>
        {
            readonly float _e; public V4(float e) { _e = e; }
            public bool Equals(Vector4 a, Vector4 b) => Mathf.Abs(a.x - b.x) <= _e && Mathf.Abs(a.y - b.y) <= _e && Mathf.Abs(a.z - b.z) <= _e && Mathf.Abs(a.w - b.w) <= _e;
            public int GetHashCode(Vector4 v) => 0;
        }
    }
}
