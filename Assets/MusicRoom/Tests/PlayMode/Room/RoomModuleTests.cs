// The Room module booted with the whole app through TestBoot (ARCHITECTURE.md section 16: scripted hands, a fresh
// MemorySettingsStore, a temporary data folder): contracts and orders, the built room against spec 07 (full floor,
// window opening, lights, the merged renderers of spec 07 section 11), the FIRE / WIND / SNOW switches poked by a
// fingertip (the switch rules, sound via IAudioEngine.TestTap, AmbienceEvent), the snow weather driving the window
// dressing, gusts reaching the audio, SetVisible / SetCandles, the rig shadows, the smoke and embers dying at their
// wrap height, the Decor phase timing and a zero-allocation Decor / switch tick.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using MusicRoom.Room.Render;
using MusicRoom.Room.Table;
using MusicRoom.Room.Textures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Room
{
    public class RoomModuleTests
    {
        /// Records the fire-time global around the Decor phase (Order 5 ticks before the Room in every phase).
        sealed class DecorProbe : AppModule
        {
            public bool Recording;
            public float AtInputDone = -1f, AtRecorder = -1f, AtGuide = -1f;
            public override int Order => 5;
            public override void Tick(FramePhase phase, float dt)
            {
                if (!Recording) return;
                float v = Shader.GetGlobalFloat(RoomModule.FireTimeId);
                if (phase == FramePhase.InputDone) AtInputDone = v;
                else if (phase == FramePhase.Recorder) AtRecorder = v;
                else if (phase == FramePhase.Guide) AtGuide = v;
            }
        }

        static float[] s_keep;
        static IReadOnlyList<Renderer> s_handles;
        TestBoot _boot;
        DecorProbe _probe;
        readonly List<AmbienceEvent> _events = new List<AmbienceEvent>();

        App App => App.Current;
        RoomModule Room => (RoomModule)App.Modules.First(m => m is RoomModule);
        /// <summary>The Lessons tape deck stands on the side table (under its group, its body added to the table's handles):
        /// the Kind Table instrument of TickOrder 30. Null when the Lessons module is not booted.</summary>
        Transform DeckRoot => App.Instruments.FirstOrDefault(i => i.Kind == InstrumentKind.Table && i.TickOrder == 30)?.Root;
        bool OnDeck(Renderer r) => DeckRoot != null && r.transform.IsChildOf(DeckRoot);

        /// A clean heap before a boot: no collection is due while the audio sources wake up (CONTRACT-REQUESTS.md B: the SynthSource wake-up race).
        internal static void CollectGarbage()
        {
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
        }

        [UnitySetUp]
        public IEnumerator Boot()
        {
            CollectGarbage();
            _probe = new DecorProbe();
            _boot = new TestBoot(headless: true);
            _boot.Options.ExtraModules.Add(_probe);
            _events.Clear();
            yield return _boot.Start();
            App.Current.Events.Subscribe<AmbienceEvent>(OnAmbience);
        }

        void OnAmbience(AmbienceEvent e) => _events.Add(e);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var engine = App.Current?.Get<IAudioEngine>();
            if (engine != null) engine.TestTap = null;
            yield return _boot.Stop();
            CollectGarbage();
        }

        IEnumerator WaitForFirstArrange()
        {
            var layout = App.Get<ILayoutService>();
            float t0 = Time.realtimeSinceStartup;
            while (layout != null && !layout.FirstArrangeDone && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            yield return null;
        }

        /// A right hand pointing straight down with its index tip at <paramref name="tip"/> (curled fingers above it).
        void PointAt(Vector3 tip)
        {
            var pose = HandPose.Open(HandSide.Right, tip + Vector3.up * 0.2f, Quaternion.LookRotation(Vector3.down, Vector3.forward)).WithPointing().WithIndexTipAt(tip);
            _boot.Hands.SetPose(HandSide.Right, pose);
        }

        // ------------------------------------------------------------------ contracts

        [UnityTest]
        public IEnumerator RegistersTheRoomContractsAndTheSwitchPanel()
        {
            var room = Room;
            Assert.That(room.Order, Is.EqualTo(20));
            Assert.That(App.Get<IRoom>(), Is.SameAs(room));
            Assert.That(App.Get<IAmbienceSwitches>(), Is.SameAs(room));
            Assert.That(App.Get<ISnowWeather>(), Is.SameAs(room.Weather));
            Rig table = App.GetAll<Rig>().Single(r => r.Id == RigId.Table);
            Assert.That(table, Is.SameAs(room.TableRig));
            Assert.That(table.HomeAll, Is.EqualTo(new Vector3(-0.39f, 0f, -0.21f)));
            Assert.That(table.HomeYawAll, Is.EqualTo(-90f));
            Assert.That(table.Priority, Is.EqualTo(3));
            Assert.That(App.Get<IRigRegistry>().Get(RigId.Table), Is.SameAs(table), "Layout collected it");
            var panel = App.Instruments.OfType<AmbiencePanel>().Single();
            Assert.That(panel.Kind, Is.EqualTo(InstrumentKind.Table));
            Assert.That(panel.TickOrder, Is.EqualTo(40), "spec 05 section 1.1: after the tape deck (30), before the menu (50)");
            Assert.That(table.Instruments, Does.Contain(panel));
            var orders = App.Instruments.Select(i => i.TickOrder).ToList();
            Assert.That(orders, Is.Ordered);
            Assert.That(panel.PlayedParts.Count, Is.EqualTo(3), "the three levers");
            yield return null;
        }

        // ------------------------------------------------------------------ the built room

        [UnityTest]
        public IEnumerator TheShellIsTheWholeRoomWithAWindowOpening()
        {
            // The room faces the fire (RoomFrame, a half turn about the seat): world bounds are the turned room's (z -1.62..3.58);
            // the meshes themselves (Covered) and the plants' bases are in the room frame.
            var s = Room.Scene;
            Bounds floor = s.Static[RoomMat.Floor].bounds;
            Assert.That(floor.min.x, Is.EqualTo(-3.2f).Within(1e-3f)); Assert.That(floor.max.x, Is.EqualTo(3.2f).Within(1e-3f));
            Assert.That(floor.min.z, Is.EqualTo(-1.62f).Within(1e-3f)); Assert.That(floor.max.z, Is.EqualTo(3.58f).Within(1e-3f));
            Assert.That(floor.size.y, Is.LessThan(1e-4f), "flat at y 0 (not tilted 90 degrees)");
            Assert.That(s.Static[RoomMat.Ceiling].bounds.center.y, Is.EqualTo(2.9f).Within(1e-3f));
            Bounds walls = s.Static[RoomMat.Wall].bounds;
            Assert.That(walls.min, Is.EqualTo(new Vector3(-3.2f, 0f, -1.62f)).Using(new Vec3Within(1e-3f)));
            Assert.That(walls.max, Is.EqualTo(new Vector3(3.2f, 2.9f, 3.58f)).Using(new Vec3Within(1e-3f)));
            // No wall triangle covers the window opening (room frame: z -2.08..-0.28, y .7..2.4 on x = 3.2).
            var mesh = s.Static[RoomMat.Wall].GetComponent<MeshFilter>().sharedMesh;
            Assert.That(Covered(mesh, new Vector3(3.2f, 1.55f, -1.18f)), Is.False, "window centre is open");
            Assert.That(Covered(mesh, new Vector3(3.2f, 0.75f, -0.35f)), Is.False, "opening near its corner");
            Assert.That(Covered(mesh, new Vector3(3.2f, 1.55f, -2.5f)), Is.True, "wall beside the window");
            Assert.That(Covered(mesh, new Vector3(3.2f, 2.6f, -1.18f)), Is.True, "wall above the window");
            // Window frame on the left wall (world): local +z out through the glass (ARCHITECTURE.md section 5 facing rule).
            Assert.That(Vector3.Distance(s.Window.position, new Vector3(-3.18f, 1.55f, 0.68f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Dot(s.Window.forward, Vector3.left), Is.GreaterThan(0.9999f));
            // Fireplace on the wall ahead of the player (spec 07 section 3.6, turned): the source frame's local +z (into the room)
            // is the mirrored frame's -z, so the room side is local -z and its forward faces the wall.
            Assert.That(Vector3.Distance(s.Fireplace.position, new Vector3(-0.3f, 0f, 3.58f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Dot(s.Fireplace.forward, Vector3.forward), Is.GreaterThan(0.9999f));
            Assert.That(s.Fireplace.TransformPoint(0f, 0.5f, -0.45f).z, Is.EqualTo(3.13f).Within(1e-4f), "the fire light's spot, in front of the opening");
            Assert.That(s.Plants.Count, Is.EqualTo(6), "4 floor plants, the fern on the shelf, the ivy on the sill");
            // The sill holds the ivy at the reference spot (its sway base); the sky and ground sit outside the window.
            Assert.That(Vector3.Distance(s.Plants.Single(p => p.Name == "Ivy").Base, new Vector3(3.06f, 0.63f, -1.83f)), Is.LessThan(2e-3f));
            Assert.That(s.Static[RoomMat.Sky].bounds.center.x, Is.EqualTo(-17.2f).Within(1e-3f), "outside the window (left)");
            Assert.That(s.Static[RoomMat.Ground].bounds.center.x, Is.EqualTo(-23.2f).Within(1e-3f));
            int tris = 0;
            foreach (var mf in s.Root.GetComponentsInChildren<MeshFilter>(true)) tris += mf.sharedMesh.triangles.Length / 3;
            Assert.That(tris, Is.LessThan(120000), "the room stays well inside the 300 k triangle budget");
            yield return null;
        }

        static bool Covered(Mesh m, Vector3 p)
        {
            var v = m.vertices; var t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                if (Mathf.Abs(a.x - p.x) > 1e-3f || Mathf.Abs(b.x - p.x) > 1e-3f || Mathf.Abs(c.x - p.x) > 1e-3f) continue;
                Vector2 P = new Vector2(p.z, p.y), A = new Vector2(a.z, a.y), B = new Vector2(b.z, b.y), C = new Vector2(c.z, c.y);
                float d1 = Cross(P - A, B - A), d2 = Cross(P - B, C - B), d3 = Cross(P - C, A - C);
                bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                if (!(neg && pos)) return true;
            }
            return false;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        sealed class Vec3Within : IEqualityComparer<Vector3>
        {
            readonly float _tol;
            public Vec3Within(float tol) { _tol = tol; }
            public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= _tol;
            public int GetHashCode(Vector3 v) => 0;
        }

        [UnityTest]
        public IEnumerator LightingIsTheShippedRoom()
        {
            var s = Room.Scene;
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(UnityEngine.Rendering.AmbientMode.Trilight));
            Assert.That((Color32)RenderSettings.ambientSkyColor, Is.EqualTo(new Color32(0x74, 0x75, 0x78, 0xff)));
            Assert.That((Color32)RenderSettings.ambientEquatorColor, Is.EqualTo(new Color32(0x69, 0x69, 0x6a, 0xff)));
            Assert.That((Color32)RenderSettings.ambientGroundColor, Is.EqualTo(new Color32(0x4c, 0x4c, 0x4b, 0xff)));
            Assert.That(RenderSettings.sun, Is.SameAs(s.Moon));
            Assert.That(s.Moon.type, Is.EqualTo(LightType.Directional));
            Assert.That(s.Moon.intensity, Is.EqualTo(0.1751f).Within(1e-4f));
            Assert.That(s.Moon.shadows, Is.EqualTo(LightShadows.None), "no realtime shadows in VR");
            Assert.That((Color32)s.Moon.color, Is.EqualTo(new Color32(0xa9, 0xbb, 0xff, 0xff)));
            // The moon turns with the room (its local rotation is the spec's; the room root carries RoomFrame).
            Assert.That(Quaternion.Angle(s.Moon.transform.localRotation, Quaternion.Euler(24.05f, -68.40f, 0f)), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(s.Moon.transform.rotation, RoomFrame.Rotation * Quaternion.Euler(24.05f, -68.40f, 0f)), Is.LessThan(1e-3f));
            // The point lights at the spec's places in the room frame, turned into the world (RoomFrame.ToWorld).
            var points = s.Root.GetComponentsInChildren<Light>(true).Where(l => l.type == LightType.Point).ToList();
            Assert.That(points.Count, Is.EqualTo(4), "floor lamp, fire, two candles (URP: 4 additional lights)");
            Assert.That(points.All(l => l.shadows == LightShadows.None), Is.True);
            Assert.That(Vector3.Distance(s.FloorLamp.transform.position, RoomFrame.ToWorld(new Vector3(-2.4f, 1.6f, 0.52f))), Is.LessThan(1e-4f));
            Assert.That(s.FloorLamp.range, Is.EqualTo(5f)); Assert.That(s.FloorLamp.intensity, Is.EqualTo(1.0804f).Within(1e-4f));
            Assert.That(Vector3.Distance(s.FireLight.transform.position, RoomFrame.ToWorld(new Vector3(0.3f, 0.5f, -3.63f))), Is.LessThan(1e-4f));
            Assert.That(s.FireLight.transform.position.z, Is.GreaterThan(3.0f), "the fire ahead of the seat");
            Assert.That(s.FireLight.range, Is.EqualTo(6f));
            var candles = s.CandleLights.Select(l => l.transform.position).ToList();
            Assert.That(candles.Any(p => Vector3.Distance(p, RoomFrame.ToWorld(new Vector3(0.93f, 0.585f, -3.15f))) < 1e-3f), Is.True, "table candle light");
            Assert.That(candles.Any(p => Vector3.Distance(p, RoomFrame.ToWorld(new Vector3(0.10f, 1.49f, -3.78f))) < 1e-3f), Is.True, "mantel candle light");
            // Flicker: the fire light varies around its base over a few frames.
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 20; i++) { Room.Tick(FramePhase.Decor, 1f / 72f); lo = Mathf.Min(lo, s.FireLight.intensity); hi = Mathf.Max(hi, s.FireLight.intensity); }
            Assert.That(hi - lo, Is.GreaterThan(0.05f), "the fire light flickers");
            Assert.That(lo, Is.GreaterThan(2.0747f * 0.6f)); Assert.That(hi, Is.LessThan(2.0747f * 1.4f));
            yield return null;
        }

        // ------------------------------------------------------------------ draw budget (spec 07 section 11)

        /// <summary>Renderers of the room root that draw now (enabled, active).</summary>
        static List<Renderer> Drawing(Transform root) =>
            root.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();

        [UnityTest]
        public IEnumerator TheRoomIsMergedIntoItsDrawBudget()
        {
            yield return WaitForFirstArrange();
            var room = Room;
            var s = room.Scene;
            // One static renderer per material: 28, the stems and leaves of all six plants and the four flower arrangements
            // included, and every flower's petals (2026-11-11).
            Assert.That(s.Static.Count, Is.EqualTo(28), string.Join(", ", s.Static.Keys));
            Assert.That(s.Static.ContainsKey(RoomMat.Petal), Is.True, "the flowers' petals: one renderer");
            Assert.That(s.Static.ContainsKey(RoomMat.Canvas) && s.Static.ContainsKey(RoomMat.Sofa), Is.True, "one canvas, one loveseat");
            Assert.That(s.Static.ContainsKey(RoomMat.Stem) && s.Static.ContainsKey(RoomMat.Leaf), Is.True);
            var all = s.Root.GetComponentsInChildren<Renderer>(true);
            Assert.That(all.Count(r => r.name == "Stems" || r.name == "Leaves" || r.name.StartsWith("Plant")), Is.EqualTo(0), "no per-plant renderers");
            // ARCHITECTURE.md sections 13 / 17: the VR room ~35 draws (38 with snow; the flowers' petals added one). The instruments' contact shadows, the
            // piano's included, are one mesh outside the room root (RigShadows, platform.md 11), so none counts here.
            var drawing = Drawing(s.Root);
            Debug.Log($"[Room] drawing renderers: {drawing.Count}: {string.Join(", ", drawing.Select(r => r.name))}");
            Assert.That(drawing.Count(r => r.name.StartsWith("Blob")), Is.EqualTo(0), "no per-rig blob under the room");
            Assert.That(drawing.Count, Is.EqualTo(35), "the room without snow");
            room.Set(AmbienceKind.Snow, true);
            room.Weather.Settle(1f, 0.6f, room.Weather.TimeD);
            room.Tick(FramePhase.Decor, 0.02f);
            drawing = Drawing(s.Root);
            Assert.That(drawing.Count, Is.EqualTo(38), "snow field, frost and sill mound add 3");
            // No MaterialPropertyBlock on any mesh renderer of the room or the table (SRP Batcher). Unity's own particle
            // renderers report one (the engine feeds them per-system data; URP particles are not SRP-batched anyway).
            var blocks = all.Concat(room.TableRig.Group.GetComponentsInChildren<Renderer>(true)).Where(r => !(r is ParticleSystemRenderer) && r.HasPropertyBlock())
                .Select(r => r.name + " (" + r.GetType().Name + ")").ToList();
            Assert.That(blocks, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ThePlantsSwayInTheirShaderAboutEachBase()
        {
            var s = Room.Scene;
            Assert.That(s.Plants.Select(p => p.Name), Is.EquivalentTo(new[] { "Ivy", "PlantP1", "PlantP2", "PlantP3", "PlantP4", "Fern" }));
            foreach (var key in new[] { RoomMat.Stem, RoomMat.Leaf })
            {
                var r = s.Static[key];
                Assert.That(r.sharedMaterial.shader.name, Is.EqualTo("MusicRoom/RoomLit"), key.ToString());
                Assert.That(r.sharedMaterial.IsKeywordEnabled("_SWAY"), Is.True, key + " sways in the vertex stage");
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord2) && mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord3), Is.True);
                var pivots = new List<Vector4>(); var data = new List<Vector4>();
                mesh.GetUVs(2, pivots); mesh.GetUVs(3, data);
                // the six plants (pivot w 0: their own sway only) and the four flower arrangements (w = the music response)
                var plants = pivots.Where(v => v.w == 0f).Select(v => new Vector3(v.x, v.y, v.z)).Distinct().ToList();
                var flowers = pivots.Where(v => v.w != 0f).Select(v => new Vector3(v.x, v.y, v.z)).Distinct().ToList();
                Assert.That(plants.Count, Is.EqualTo(6), key + ": every plant vertex belongs to one of the six plants");
                Assert.That(flowers.Count, Is.EqualTo(4), key + ": and the flowers' to one of the four arrangements");
                Assert.That(pivots.Where(v => v.w != 0f).All(v => v.w == MusicRoom.Room.World.FlowerBuilder.Response), Is.True, "only the flowers answer the music");
                foreach (var b in plants) Assert.That(s.Plants.Any(p => Vector3.Distance(p.Base, b) < 1e-5f), Is.True);
                foreach (var b in flowers) Assert.That(s.Bouquets.Any(p => Vector3.Distance(p.Base, b) < 1e-5f), Is.True);
                foreach (var d in data.Distinct())
                {
                    Assert.That(d.y, Is.InRange(0.012f, 0.022f), "amplitude [.012, .022] rad");
                    Assert.That(d.x, Is.InRange(0f, 6.28f), "phase");
                }
            }
            Assert.That(s.Static[RoomMat.Leaf].sharedMaterial.IsKeywordEnabled("_SWAY_TWO_SIDED"), Is.True, "leaves lit on both faces");
            // The dark plants' leaves sample the right half of the atlas, the light ones the left.
            var leafMesh = s.Static[RoomMat.Leaf].GetComponent<MeshFilter>().sharedMesh;
            var uv = new List<Vector2>(); leafMesh.GetUVs(0, uv);
            Assert.That(uv.All(u => u.x >= -1e-4f && u.x <= 1.0001f), Is.True);
            Assert.That(uv.Count(u => u.x > 0.5001f) > 0 && uv.Count(u => u.x < 0.4999f) > 0, Is.True, "both variants");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheLoveseatIsOneRendererWithItsThreeFabrics()
        {
            var r = Room.Scene.Static[RoomMat.Sofa];
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            var uv = new List<Vector2>(); mesh.GetUVs(0, uv);
            var byPart = uv.GroupBy(SofaPalette.QuadrantOf).ToDictionary(g => g.Key, g => g.Count());
            Assert.That(byPart.Keys, Is.EquivalentTo(new[] { 0, 1, 2 }), "frame, cushions and pillow, each in its own quadrant");
            bool Clamped(float x) => x * SofaPalette.Tiling <= 0f || x * SofaPalette.Tiling >= 1f;
            Assert.That(uv.All(u => Clamped(u.x) && Clamped(u.y)), Is.True, "every UV clamps onto one texel (none in the palette's blended middle)");
            var palette = r.sharedMaterial.GetTexture(MaterialLibrary.BaseMap) as Texture2D;
            Assert.That(palette, Is.Not.Null);
            Assert.That(palette.wrapMode, Is.EqualTo(TextureWrapMode.Clamp)); Assert.That(palette.width, Is.EqualTo(2));
            Assert.That(r.sharedMaterial.IsKeywordEnabled("_NORMALMAP"), Is.True, "the weave");
            Assert.That(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheSmokeAndEmbersDieAtTheReferenceWrapHeight()
        {
            var s = Room.Scene;
            foreach (var (ps, spec) in new[] { (s.Smoke, FireParticles.Smoke), (s.Embers, FireParticles.Embers) })
            {
                ps.Pause();
                var buf = new ParticleSystem.Particle[ps.main.maxParticles];
                var last = new Dictionary<uint, float>();
                const float dt = 1f / 72f;
                int deaths = 0, alive = 0;
                double aliveSum = 0; int aliveSamples = 0;
                float lowest = float.MaxValue;
                for (int step = 0; step < 72 * 8; step++)
                {
                    ps.Simulate(dt, true, false, false);
                    int n = ps.GetParticles(buf);
                    alive = n;
                    if (step >= 72 * 4) { aliveSum += n; aliveSamples++; }
                    var now = new Dictionary<uint, float>(n);
                    for (int i = 0; i < n; i++)
                    {
                        float y = buf[i].position.y;
                        Assert.That(y, Is.LessThanOrEqualTo(spec.Top + 1e-3f), $"{spec.Name}: no live particle above the wrap height");
                        now[buf[i].randomSeed] = y;
                    }
                    foreach (var kv in last)
                        if (!now.ContainsKey(kv.Key))
                        {
                            deaths++;
                            lowest = Mathf.Min(lowest, kv.Value);
                        }
                    last = now;
                }
                double meanAlive = aliveSum / aliveSamples;
                Debug.Log($"[Room] {spec.Name}: {alive} alive ({meanAlive:F1} on average over the last 4 s), {deaths} deaths in 8 s, lowest last height {lowest:F3} m (top {spec.Top})");
                Assert.That(deaths, Is.GreaterThan(spec.Max / 2), spec.Name + " particles cycle");
                Assert.That(lowest, Is.GreaterThanOrEqualTo(spec.Top - spec.SpeedMax * dt - 1e-3f), $"{spec.Name}: every particle dies at the top, none mid-air");
                Assert.That(lowest, Is.GreaterThan(0.78f), "never in view below the lintel");
                Assert.That(alive, Is.LessThanOrEqualTo(spec.Max));
                Assert.That(meanAlive, Is.GreaterThan(spec.Max * 0.9), spec.Name + ": the column stays full, as wrapped points would");
                ps.Play();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheTableRigIsEightRenderersAndCastsNoShadow()
        {
            var t = Room.Table;
            var all = t.Group.GetComponentsInChildren<Renderer>(true);
            var renderers = all.Where(r => !OnDeck(r)).ToArray();   // the tape deck on the table is the Lessons module's
            Assert.That(renderers.Length, Is.EqualTo(8), string.Join(", ", renderers.Select(r => r.name)) + " (wood, brass, blob, 3 levers, LEDs, labels)");
            Assert.That(all.Length, Is.LessThanOrEqualTo(15), "table + deck + switches draw budget (ARCHITECTURE.md 17)");
            Assert.That(renderers.All(r => r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off), Is.True, "MR casters are the instrument bodies only");
            Assert.That(renderers.Where(r => r.name == "TableWood" || r.name == "TableBrass").All(r => r.receiveShadows), Is.True, "the table still takes the instruments' shadows in MR");
            Assert.That(t.LabelRenderer, Is.Not.Null, "TextMeshPro laid out the labels");
            Mesh labels = t.LabelRenderer.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(labels.vertexCount, Is.GreaterThanOrEqualTo(3 * 4 * 4), "FIRE WIND SNOW: 12 glyph quads in one mesh");
            Assert.That(t.Panel.GetComponentsInChildren<TMPro.TextMeshPro>(true).Length, Is.EqualTo(0), "no TextMeshPro objects left");
            // The three words sit over their toggles (panel space x -.075 / 0 / .075).
            Bounds b = labels.bounds;
            Assert.That(b.min.x, Is.LessThan(-0.075f)); Assert.That(b.max.x, Is.GreaterThan(0.075f));
            Assert.That(b.center.x, Is.EqualTo(0f).Within(0.004f));
            Assert.That(b.center.z, Is.EqualTo(0.03f).Within(0.004f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SetCandlesSwapsTheBrassAndSootMeshes()
        {
            var room = Room;
            var s = room.Scene;
            var brass = s.Static[RoomMat.Brass].GetComponent<MeshFilter>();
            var soot = s.Static[RoomMat.Soot].GetComponent<MeshFilter>();
            Assert.That(brass.sharedMesh, Is.SameAs(s.BrassWithCandles));
            int withHolders = brass.sharedMesh.vertexCount;
            room.SetCandles(false);
            Assert.That(brass.sharedMesh, Is.SameAs(s.BrassWithoutCandles)); Assert.That(soot.sharedMesh, Is.SameAs(s.SootWithoutCandles));
            Assert.That(brass.sharedMesh.vertexCount, Is.LessThan(withHolders), "the four holders went with the candles");
            Assert.That(s.Candles.gameObject.activeInHierarchy, Is.False, "wax, flames and both candle lights too");
            room.SetCandles(true);
            Assert.That(brass.sharedMesh, Is.SameAs(s.BrassWithCandles)); Assert.That(soot.sharedMesh, Is.SameAs(s.SootWithCandles));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheHillSnowIsBoundAtBootNotWhenSnowFirstSettles()
        {
            var s = Room.Scene;
            if (!GeneratedAssets.HasBaked(RoomTextures.HillSnow)) Assert.Ignore("Room/hill_snow is not baked (run Tools/unity.sh bake)");
            Assert.That(s.SkyMaterial.GetTexture(RoomMaterials.HillSnowMap), Is.Not.Null, "bound in Init with the other sky textures");
            Assert.That(s.SkyMaterial.GetFloat(RoomMaterials.HillSnowOn), Is.EqualTo(1f));
            Assert.That(Shader.GetGlobalFloat(RoomModule.SnowCoverId), Is.EqualTo(0f), "NightSky skips the overlay while no snow lies");
            yield return null;
        }

        // ------------------------------------------------------------------ switches

        [UnityTest]
        public IEnumerator AFingertipPokeFlipsTheFireSwitchOnceWithItsSoundAndEvent()
        {
            yield return WaitForFirstArrange();
            var room = Room;
            var engine = App.Get<IAudioEngine>();
            engine.TestTap = new AudioTestTap();
            Transform panel = room.Switches.Panel;
            Vector3 above = panel.TransformPoint(-0.075f, 0.10f, 0f), inside = panel.TransformPoint(-0.075f, 0.03f, 0f);
            PointAt(above);
            yield return null; yield return null;
            Assert.That(room.Get(AmbienceKind.Fire), Is.False);
            PointAt(inside);
            yield return null; yield return null;
            Assert.That(room.Get(AmbienceKind.Fire), Is.True, "the first frame inside flips it");
            Assert.That(_events.Count, Is.EqualTo(1));
            Assert.That(_events[0].Kind, Is.EqualTo(AmbienceKind.Fire)); Assert.That(_events[0].On, Is.True);
            Assert.That(engine.FireOn, Is.True);
            Assert.That(engine.TestTap.Commands.Any(c => c.Source == SourceId.Fire && c.Command.Op == SynthOp.SetBusGain && c.Command.Level == 1f), Is.True, "fire bed up");
            Assert.That(engine.TestTap.Commands.Any(c => c.Source == SourceId.Fire && c.Command.Op == SynthOp.Custom && c.Command.Kind == 1 && c.Command.Level == 1f), Is.True, "fire pops on");
            var t = room.Switches.Toggles[0];
            Assert.That(Quaternion.Angle(t.Lever.localRotation, Quaternion.Euler(AmbiencePanel.LeverOnDeg, 0f, 0f)), Is.LessThan(0.01f), "lever at +31.5");
            var leds = room.Table.Leds;
            Assert.That(leds.IsOn(0), Is.True, "FIRE's LED is lit");
            Assert.That(leds.ColorOf(0).g, Is.EqualTo(TextureUtil.Hex("#ff8a2a").linear.g * 1.4f).Within(0.01f), "LED lit x 1.4 (vertex colour)");
            Assert.That(leds.ColorOf(1), Is.EqualTo(LedStrip.Off), "WIND's LED stays dark");
            var ledColors = leds.Mesh.colors;
            Assert.That(ledColors.Skip(leds.VerticesPerLed).Take(leds.VerticesPerLed).All(c => c == LedStrip.Off), Is.True, "only FIRE's vertices changed");
            Assert.That(room.Table.LedRenderer.HasPropertyBlock(), Is.False, "no MaterialPropertyBlock (SRP Batcher)");
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(_events.Count, Is.EqualTo(1), "holding inside does not flip again");
            PointAt(above);
            yield return null; yield return null;
            PointAt(inside);
            yield return null; yield return null;
            Assert.That(room.Get(AmbienceKind.Fire), Is.False, "out and back in flips it off");
            Assert.That(_events.Count, Is.EqualTo(2));
            Assert.That(engine.FireOn, Is.False);
            Assert.That(room.Get(AmbienceKind.Wind) || room.Get(AmbienceKind.Snow), Is.False, "the neighbours stay off");
        }

        [UnityTest]
        public IEnumerator HoverGlowsAndPlaySurfaceOverTheLevers()
        {
            yield return WaitForFirstArrange();
            var room = Room;
            Transform panel = room.Switches.Panel;
            PointAt(panel.TransformPoint(0f, 0.075f, 0f));   // 3 cm above the WIND lever top: hover, no flip
            yield return null; yield return null;
            Assert.That(App.HoverGlow.Drawn, Is.GreaterThanOrEqualTo(1), "a glow over the lever");
            Assert.That(App.HoverGlow.Spec(0).Normal.y, Is.GreaterThan(0.99f), "facing up off the panel");
            Assert.That(room.Get(AmbienceKind.Wind), Is.False);
            Assert.That(room.Switches.PlaySurfaceAt(panel.TransformPoint(0f, 0.05f, 0f)), Is.EqualTo(PlaySurface.Table), "a pinch over a lever is a press, not a grab");
            Assert.That(room.Switches.PlaySurfaceAt(panel.TransformPoint(0.2f, 0.05f, 0f)), Is.EqualTo(PlaySurface.None));
            var handles = room.TableRig.Handles;
            Assert.That(handles.Where(r => !OnDeck(r)).Select(r => r.name), Is.EqualTo(new[] { "TableWood" }), "slab, legs and plate (one merged mesh) are the table's handles");
            Assert.That(handles[0].name, Is.EqualTo("TableWood"), "the deck's body joins the list after them (Lessons AddHandles)");
            Assert.That(room.TableRig.Handles, Is.SameAs(handles), "filled once, never built per call");
            var rig = room.TableRig;
            TestDelegate measured = () => { s_handles = rig.Handles; };
            AllocAssert.Nothing(measured);   // the code once first, then the main thread's byte counter and the recorder
            Assert.That(handles.Intersect(room.Switches.PlayedParts).Any(), Is.False, "levers never are");
            Assert.That(handles, Has.No.Member(room.Table.Blob), "nor the contact shadow");
            Assert.That(room.TableRig.Zone.Contains(room.TableRig.Group.TransformPoint(0f, 0.6f, 0f)), Is.True);
            Assert.That(room.TableRig.Zone.Contains(room.TableRig.Group.TransformPoint(1f, 0.6f, 0f)), Is.False);
        }

        [UnityTest]
        public IEnumerator SetFromTheMenuOrMrFlipsTheLeverAndTheSound()
        {
            var room = Room;
            var engine = App.Get<IAudioEngine>();
            room.Set(AmbienceKind.Wind, true);
            Assert.That(engine.WindOn, Is.True);
            Assert.That(room.Switches.Toggles[1].On, Is.True);
            Assert.That(Quaternion.Angle(room.Switches.Toggles[1].Lever.localRotation, Quaternion.Euler(31.513f, 0f, 0f)), Is.LessThan(0.01f));
            room.Set(AmbienceKind.Wind, true);
            Assert.That(_events.Count, Is.EqualTo(1), "setting the same state publishes nothing");
            room.Set(AmbienceKind.Wind, false);
            Assert.That(engine.WindOn, Is.False);
            Assert.That(Quaternion.Angle(room.Switches.Toggles[1].Lever.localRotation, Quaternion.Euler(-31.513f, 0f, 0f)), Is.LessThan(0.01f));
            Assert.That(_events.Count, Is.EqualTo(2));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ADesktopClickOnALeverFlipsItOnceAndTheNearestPlaySurfaceWins()
        {
            yield return WaitForFirstArrange();
            var room = Room;
            var panel = room.Switches;
            Physics.SyncTransforms();   // the rig moved in the first arrange; no physics step has run since
            // The switch hit mesh: a cylinder r .016 h .05 at (x, .03, 0) on the PlaySurface layer (29).
            foreach (var t in panel.Toggles)
            {
                Assert.That(t.Hit.gameObject.layer, Is.EqualTo(29));
                Vector3 c = panel.Panel.InverseTransformPoint(t.Hit.bounds.center);
                Assert.That(c.x, Is.EqualTo(t.X).Within(2e-3f)); Assert.That(c.y, Is.EqualTo(0.03f).Within(2e-3f));
            }
            Vector3 down = -panel.Panel.up;
            Assert.That(panel.DesktopDown(new Ray(panel.Panel.TransformPoint(0.075f, 0.4f, 0f), down)), Is.True, "a click on SNOW's lever");
            Assert.That(room.Get(AmbienceKind.Snow), Is.True);
            Assert.That(_events.Count, Is.EqualTo(1), "flipped exactly once");
            Assert.That(panel.DesktopDown(new Ray(panel.Panel.TransformPoint(0.075f, 0.4f, 0f), down)), Is.True, "the next click flips it back");
            Assert.That(room.Get(AmbienceKind.Snow), Is.False);
            Assert.That(panel.DesktopDown(new Ray(panel.Panel.TransformPoint(0.2f, 0.4f, 0f), down)), Is.False, "beside the panel: no target");
            // Another module's play surface nearer along the ray takes the click (the nearest hit wins).
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                blocker.layer = 29;
                blocker.transform.position = panel.Panel.TransformPoint(0f, 0.2f, 0f);
                blocker.transform.localScale = Vector3.one * 0.05f;
                Physics.SyncTransforms();
                Assert.That(panel.DesktopDown(new Ray(panel.Panel.TransformPoint(0f, 0.4f, 0f), down)), Is.False);
                Assert.That(room.Get(AmbienceKind.Wind), Is.False);
            }
            finally { Object.Destroy(blocker); }
            Assert.That(App.Input.Hands[1].Pointers[0].FromMouse, Is.False, "scripted hands are not the mouse: they poke");
            Assert.That(App.IsPresenting, Is.False, "the desktop click path runs while no XR session presents");
        }

        // ------------------------------------------------------------------ snow

        [UnityTest]
        public IEnumerator TheSnowSwitchDrivesTheWeatherAndTheWindowDressing()
        {
            var room = Room;
            var s = room.Scene;
            Assert.That(s.SnowField.enabled || s.Mound.enabled || s.Frost.enabled, Is.False, "nothing before it snows");
            room.Set(AmbienceKind.Snow, true);
            Assert.That(room.Weather.Target, Is.EqualTo(1f));
            Assert.That(App.Get<IAudioEngine>().SnowOn, Is.True);
            for (int i = 0; i < 40; i++) room.Tick(FramePhase.Decor, 0.05f);
            Assert.That(room.Weather.Level, Is.GreaterThan(0.75f), "level follows the switch over a few seconds");
            Assert.That(s.SnowField.enabled, Is.True, "flakes once level > .001");
            Assert.That(Shader.GetGlobalFloat(RoomModule.SnowIntensityId), Is.EqualTo(room.Weather.Level).Within(1e-6f));
            Assert.That(Shader.GetGlobalFloat(RoomModule.SnowTimeId), Is.EqualTo(room.Weather.Time).Within(1e-4f));
            Assert.That(Shader.GetGlobalFloat(RoomModule.SnowPxId), Is.GreaterThan(0f));
            Vector4 tint = s.SkyMaterial.GetVector(RoomMaterials.Tint);
            float k = 1f - 0.12f * room.Weather.Level;
            Assert.That(tint.x, Is.EqualTo(k).Within(1e-4f)); Assert.That(tint.z, Is.EqualTo(k + 0.06f * room.Weather.Level).Within(1e-4f), "the sky pales while it snows");
            room.Weather.Settle(1f, 0.5f, room.Weather.TimeD);
            room.Tick(FramePhase.Decor, 0.02f);
            float c = room.Weather.Cover;
            Assert.That(s.Mound.enabled, Is.True, "the sill mound once cover > .01");
            Assert.That(s.MoundTransform.localScale.y, Is.EqualTo(Mathf.Pow(c, 0.75f)).Within(1e-4f));
            Assert.That(s.Frost.enabled, Is.True);
            Assert.That(s.FrostMaterial.GetFloat(RoomMaterials.FrostAmount), Is.EqualTo(c).Within(1e-5f));
            Vector4 ground = s.GroundMaterial.GetVector(RoomMaterials.Tint);
            Color want = Color.Lerp(TextureUtil.Hex("#05070f").linear, TextureUtil.Hex("#435070").linear, Mathf.Pow(c, 0.8f) * 0.92f);
            Assert.That(ground.y, Is.EqualTo(want.g).Within(1e-4f), "the ground whitens in linear space");
            room.Set(AmbienceKind.Snow, false);
            for (int i = 0; i < 200; i++) room.Tick(FramePhase.Decor, 0.05f);
            Assert.That(room.Weather.Level, Is.EqualTo(0f), "flakes stop");
            Assert.That(s.SnowField.enabled, Is.False);
            Assert.That(room.Weather.Cover, Is.EqualTo(c - 10f / 150f).Within(0.01f), "settled snow recedes at 1/150 per second");
            Assert.That(s.Mound.enabled, Is.True, "and lingers");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GustsAreHeardOnlyWhileTheSnowSoundIsOn()
        {
            var room = Room;
            var engine = App.Get<IAudioEngine>();
            engine.TestTap = new AudioTestTap();
            room.Weather.StartGust();
            Assert.That(engine.TestTap.Count(SourceId.Snow, SynthOp.Custom), Is.EqualTo(0), "the gust sound needs the snow sound on");
            room.Set(AmbienceKind.Snow, true);
            SnowGust g = room.Weather.StartGust();
            var gust = engine.TestTap.Commands.Where(e => e.Source == SourceId.Snow && e.Command.Op == SynthOp.Custom && e.Command.Kind == 2).ToList();
            Assert.That(gust.Count, Is.EqualTo(1), "OnGust -> IAudioEngine.SnowGust, the same gust heard and seen");
            Assert.That(gust[0].Command.Level, Is.EqualTo(g.Strength).Within(1e-6f));
            Assert.That(gust[0].Command.Slide, Is.EqualTo(g.Attack).Within(1e-6f));
            Assert.That(gust[0].Command.Tau, Is.EqualTo(g.Release).Within(1e-6f));
            yield return null;
        }

        // ------------------------------------------------------------------ visibility

        [UnityTest]
        public IEnumerator SetVisibleHidesTheRoomButNotTheTableNorTheWeather()
        {
            yield return WaitForFirstArrange();
            var room = Room;
            var s = room.Scene;
            room.SetVisible(false);
            Assert.That(room.Visible, Is.False);
            Assert.That(s.FireLight.isActiveAndEnabled || s.Moon.isActiveAndEnabled, Is.False, "the room's lights go with it");
            Assert.That(room.TableRig.Group.gameObject.activeInHierarchy, Is.True, "the table rig stays (mixed reality)");
            Assert.That(room.Switches.IsPresent, Is.True);
            double t0 = room.Weather.TimeD;
            room.Tick(FramePhase.Decor, 0.1f);
            Assert.That(room.Weather.TimeD, Is.GreaterThan(t0), "the weather keeps running (the room snow in mixed reality)");
            RenderSettings.ambientSkyColor = Color.red;   // what MR would set
            room.SetVisible(true);
            Assert.That(room.Visible, Is.True);
            Assert.That((Color32)RenderSettings.ambientSkyColor, Is.EqualTo(new Color32(0x74, 0x75, 0x78, 0xff)), "the VR ambient is back");
            room.SetCandles(false);
            Assert.That(s.Candles.gameObject.activeInHierarchy, Is.False);
            Assert.That(s.CandleLights.All(l => !l.isActiveAndEnabled), Is.True, "both candle lights off");
            Assert.That(s.FireLight.isActiveAndEnabled, Is.True, "the fire stays");
            room.SetCandles(true);
            Assert.That(s.CandleLights.All(l => l.isActiveAndEnabled), Is.True);
        }

        // ------------------------------------------------------------------ the rig shadows (one mesh, platform.md 11)

        /// <summary>The world corners of entry <paramref name="index"/>'s quad in the shadow mesh.</summary>
        static Vector3[] QuadCorners(RigShadows shadows, int index)
        {
            var v = shadows.Mesh.vertices;
            var m = shadows.Renderer.transform.localToWorldMatrix;
            return Enumerable.Range(index * 4, 4).Select(i => m.MultiplyPoint3x4(v[i])).ToArray();
        }

        static int IndexOf(RigShadows shadows, RigId id)
        {
            for (int i = 0; i < shadows.Entries.Count; i++) if (shadows.Entries[i].Id == id) return i;
            return -1;
        }

        /// <summary>The quad is the rig's W x D rectangle about its rig-local centre, turned with the rig, scaled with it, on
        /// the floor (y .005): the blob of before, vertex for vertex.</summary>
        static void AssertQuadUnder(RigShadows shadows, RigId id, Transform rig, string why)
        {
            int i = IndexOf(shadows, id);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), id + " has an entry");
            var e = shadows.Entries[i];
            Assert.That(e.Shown, Is.True, id + " shown: " + why);
            Vector3 c = rig.TransformPoint(e.Local.x, 0f, e.Local.y); c.y = RigShadows.FloorLift;
            Assert.That(Vector3.Distance(e.Centre, c), Is.LessThan(1e-4f), id + " centre: " + why);
            Vector3 f = rig.forward; f.y = 0f; f.Normalize();
            Vector3 r = new Vector3(f.z, 0f, -f.x);
            float s = rig.lossyScale.x;
            foreach (Vector3 p in QuadCorners(shadows, i))
            {
                Assert.That(p.y, Is.EqualTo(RigShadows.FloorLift).Within(1e-5f), id + " on the floor");
                Vector3 d = p - c;
                Assert.That(Mathf.Abs(Vector3.Dot(d, r)), Is.EqualTo(e.W * s * 0.5f).Within(1e-4f), id + " width: " + why);
                Assert.That(Mathf.Abs(Vector3.Dot(d, f)), Is.EqualTo(e.D * s * 0.5f).Within(1e-4f), id + " depth: " + why);
            }
        }

        [UnityTest]
        public IEnumerator RigShadowsAreOneDrawForEveryAtSeatInstrumentAsBefore()
        {
            yield return WaitForFirstArrange();
            yield return null;
            var room = Room;
            var shadows = room.Shadows;
            // One renderer, one mesh, four vertices per registry instrument with a ShadowSpec.
            Assert.That(shadows.Root.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(1), "one draw (was one per rig)");
            Assert.That(Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Count(r => r.name.StartsWith("Blob ")), Is.EqualTo(0), "no per-rig blob left");
            int withShadow = App.Registry.All.Count(d => d.Playable && d.Shadow != null);
            Assert.That(shadows.Entries.Count, Is.EqualTo(withShadow));
            Assert.That(shadows.Mesh.vertexCount, Is.EqualTo(4 * withShadow));
            Assert.That(shadows.Renderer.sharedMaterial.shader.name, Is.EqualTo(room.Scene.Static[RoomMat.Decal].sharedMaterial.shader.name), "the room's floor decal");
            Assert.That(shadows.Renderer.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
            // The default set: exactly the three blobs of before (RigShadows.Defaults: positions, sizes, opacities).
            var old = new (RigId id, Vector2 local, float w, float d, float opacity, bool inRoom)[]
            {
                (RigId.Piano, new Vector2(0f, 0.2f), 2f, 1f, 0.55f, true),
                (RigId.Drums, new Vector2(0.1f, 0.5f), 1.9f, 1.6f, 0.45f, false),
                (RigId.Harp, new Vector2(-0.2725f, -0.168f), 1.1f, 0.7f, 0.45f, false),
            };
            var reg = App.Get<IRigRegistry>();
            var colours = shadows.Mesh.colors;
            foreach (var o in old)
            {
                int i = IndexOf(shadows, o.id);
                var e = shadows.Entries[i];
                Assert.That(e.Local, Is.EqualTo(o.local), o.id + " centre");
                Assert.That(e.W, Is.EqualTo(o.w)); Assert.That(e.D, Is.EqualTo(o.d));
                Assert.That(e.Opacity, Is.EqualTo(o.opacity));
                Assert.That(e.HiddenInMr, Is.EqualTo(o.inRoom), o.id + ": the piano's goes with the room");
                for (int k = 0; k < 4; k++) Assert.That(colours[i * 4 + k].a, Is.EqualTo(o.opacity).Within(1e-6f), o.id + " opacity in the vertex colour");
                AssertQuadUnder(shadows, o.id, reg.Get(o.id).Group, "the default set at its homes");
            }
            Assert.That(shadows.Entries.Count(e => e.Shown), Is.EqualTo(3), "only the three at the seat");
            Assert.That(shadows.Renderer.enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator RigShadowsFollowTheInstrumentRigsOnTheFloor()
        {
            yield return WaitForFirstArrange();
            var room = Room;
            var reg = App.Get<IRigRegistry>();
            var drums = reg.Get(RigId.Drums);
            Assert.That(drums, Is.Not.Null, "the drums module registered its rig");
            // Whatever layout mode a previous session persisted, put the kit in the scene for this check.
            drums.InScene = true;
            drums.Group.gameObject.SetActive(true);
            yield return null;
            AssertQuadUnder(room.Shadows, RigId.Drums, drums.Group, "at its home");
            drums.Group.position += new Vector3(0.3f, 0.2f, 0f);   // moved and lifted
            drums.Group.rotation *= Quaternion.Euler(0f, 30f, 0f);
            yield return null;
            AssertQuadUnder(room.Shadows, RigId.Drums, drums.Group, "follows, turns, and stays on the floor");
            drums.Group.localScale = Vector3.one * 0.85f;           // a page turn shrinks the rig
            yield return null;
            AssertQuadUnder(room.Shadows, RigId.Drums, drums.Group, "scaled with the rig");
            drums.Group.localScale = Vector3.one;
            // The piano's shadow belongs to the room: hidden with it (mixed reality), back with it.
            var piano = reg.Get(RigId.Piano);
            bool pianoAtSeat = piano.InScene && piano.Group.gameObject.activeInHierarchy;
            room.SetVisible(false);
            yield return null;
            Assert.That(room.Shadows.Entries[IndexOf(room.Shadows, RigId.Piano)].Shown, Is.False, "the piano's shadow belongs to the room (hidden in MR)");
            AssertQuadUnder(room.Shadows, RigId.Drums, drums.Group, "the kit's stays in MR");
            room.SetVisible(true);
            yield return null;
            Assert.That(room.Shadows.Entries[IndexOf(room.Shadows, RigId.Piano)].Shown, Is.EqualTo(pianoAtSeat), "back with the room");
            var harp = reg.Get(RigId.Harp);
            harp.Group.gameObject.SetActive(false);
            yield return null;
            Assert.That(room.Shadows.Entries[IndexOf(room.Shadows, RigId.Harp)].Shown, Is.False, "no shadow for a rig out of the scene");
            harp.InScene = false;
            harp.Group.gameObject.SetActive(true);
            yield return null;
            Assert.That(room.Shadows.Entries[IndexOf(room.Shadows, RigId.Harp)].Shown, Is.False, "nor for one out of the layout (on display, gliding)");
            // Nothing at the seat: no draw at all.
            foreach (var r in reg.Rigs) if (r.Id != RigId.Table && r.Id != RigId.Lessons) r.InScene = false;
            yield return null;
            Assert.That(room.Shadows.Renderer.enabled, Is.False);
            Assert.That(room.Shadows.Entries.Any(e => e.Shown), Is.False);
        }

        [UnityTest]
        public IEnumerator RigShadowsRewriteOnlyWhenARigMovesAndAllocateNothing()
        {
            yield return WaitForFirstArrange();
            var room = Room;
            var drums = App.Get<IRigRegistry>().Get(RigId.Drums);
            drums.InScene = true;
            drums.Group.gameObject.SetActive(true);
            for (int i = 0; i < 3; i++) yield return null;
            int before = room.Shadows.Rebuilds;
            for (int i = 0; i < 5; i++) room.Shadows.Update();
            Assert.That(room.Shadows.Rebuilds, Is.EqualTo(before), "nothing moved: no vertex is written");
            Transform g = drums.Group;
            Vector3 p0 = g.position;
            // Warm up the moving path, then a held rig dragged every frame: rewritten each time, 0 B.
            for (int i = 0; i < 3; i++) { g.position = p0 + new Vector3(0.01f * i, 0f, 0f); room.Shadows.Update(); }
            TestDelegate measured = () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    g.position = p0 + new Vector3(0f, 0f, 0.01f * i);
                    room.Shadows.Update();
                }
            };
            int n0 = room.Shadows.Rebuilds;
            AllocAssert.Nothing(measured);   // the code once first, then the main thread's byte counter and the recorder
            Assert.That((room.Shadows.Rebuilds - n0) % 20, Is.Zero, "one rewrite per move");
            Assert.That(room.Shadows.Rebuilds - n0, Is.GreaterThanOrEqualTo(60), "every measured run rewrote all twenty");
            g.position = p0;
            yield return null;
        }

        // ------------------------------------------------------------------ frame timing and allocations

        [UnityTest]
        public IEnumerator DecorWorkHappensInTheDecorPhase()
        {
            yield return null;
            _probe.Recording = true;
            App.Clock.Step(0.02f);
            App.Frame(fromUnity: false);
            _probe.Recording = false;
            Assert.That(_probe.AtRecorder, Is.EqualTo(_probe.AtInputDone), "nothing moves the fire clock before Decor");
            Assert.That(_probe.AtGuide - _probe.AtRecorder, Is.EqualTo(0.02f).Within(1e-5f), "Decor advanced it by this frame's dt");
            Assert.That(_probe.AtGuide, Is.EqualTo(Room.DecorTime).Within(1e-6f));
        }

        [UnityTest]
        public IEnumerator DecorAndTheSwitchPanelAllocateNothing()
        {
            yield return WaitForFirstArrange();
            var room = Room;
            var panel = room.Switches;
            PointAt(panel.Panel.TransformPoint(0.075f, 0.075f, 0f));
            room.Set(AmbienceKind.Snow, true);
            room.Weather.Settle(1f, 0.3f, room.Weather.TimeD);   // past the one-off hill-snow request (cover > .002)
            for (int i = 0; i < 6; i++) yield return null;
            var pointers = App.Input.HandPointers;
            var glow = App.HoverGlow;
            // Warm up every path (JIT, first-use caches).
            for (int i = 0; i < 3; i++) { room.Tick(FramePhase.Decor, 1f / 72f); room.Tick(FramePhase.EndFrame, 1f / 72f); panel.Tick(pointers, 1f / 72f); panel.Hover(pointers, glow); glow.Update(0f); }
            Assert.That(() => { s_keep = new float[1024]; }, Is.AllocatingGCMemory(), "calibration: the recorder sees allocations here");
            Assert.That(() =>
            {
                for (int i = 0; i < 20; i++)
                {
                    room.Tick(FramePhase.Decor, 1f / 72f);
                    room.Tick(FramePhase.EndFrame, 1f / 72f);
                    panel.Tick(pointers, 1f / 72f);
                    panel.Hover(pointers, glow);
                    glow.Update(i);
                }
            }, Is.Not.AllocatingGCMemory());

            // CPU: ARCHITECTURE.md section 17 gives decor 0.3 ms per frame on device; the editor's Mono on a busy CI Mac is
            // no device, so this only guards against a gross regression (logged for the record).
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int n = 300;
            for (int i = 0; i < n; i++) { room.Tick(FramePhase.Decor, 1f / 72f); room.Tick(FramePhase.EndFrame, 1f / 72f); panel.Tick(pointers, 1f / 72f); panel.Hover(pointers, glow); glow.Update(i); }
            double ms = sw.Elapsed.TotalMilliseconds / n;
            Debug.Log($"[Room] decor + switch panel tick: {ms * 1000.0:F1} us per frame (editor)");
            Assert.That(ms, Is.LessThan(1.0), "decor tick well under a millisecond");
        }

        [UnityTest]
        public IEnumerator GeneratedTexturesArriveInTheBackground()
        {
            // The fallback path (no bake): a worker computes the pixels, the main thread uploads one texture per Update.
            var loader = new TextureLoader(true, ignoreCache: true);
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                loader.Bind(RoomTextures.SoftDot, m, MaterialLibrary.BaseMap, Color.white, Color.gray);   // Core's, unbaked here
                Assert.That(loader.Pending, Is.EqualTo(1), "generating in the background");
                Assert.That(m.GetColor(MaterialLibrary.BaseColor), Is.EqualTo(Color.gray), "the placeholder tint meanwhile");
                float t0 = Time.realtimeSinceStartup;
                while (loader.Pending > 0 && Time.realtimeSinceStartup - t0 < 10f) { loader.Update(); yield return null; }
                Assert.That(loader.Pending, Is.EqualTo(0));
                var tex = m.GetTexture(MaterialLibrary.BaseMap) as Texture2D;
                Assert.That(tex, Is.Not.Null, "the generated soft dot was bound");
                Assert.That(tex.name, Is.EqualTo("Core/soft_dot (runtime)"));
                Assert.That(tex.width, Is.EqualTo(64));
                Assert.That(tex.isReadable, Is.False, "the CPU copy was released after the upload");
                Assert.That(m.GetColor(MaterialLibrary.BaseColor), Is.EqualTo(Color.white), "the ready colour replaced the placeholder");
            }
            finally
            {
                loader.DestroyOwned();
                Object.Destroy(m);
            }
        }
    }
}
