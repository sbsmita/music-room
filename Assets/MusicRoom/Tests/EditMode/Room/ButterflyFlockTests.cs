// The butterflies' behaviour (Room/Decor/ButterflyFlock.cs) on a scripted clock, and the flowers' geometry
// (Room/World/FlowerBuilder.cs) and wing atlas (Room/Textures/ButterflyArt.cs):
//   - none without music; with notes they come (4 for soft notes, more for loud ones, at most 12) and fade in;
//   - they flutter around the instrument that sounds, above and beyond its played parts, never inside its keep-out box;
//   - never within the head's 0.35 m sphere (plus a wing's reach) or a fingertip's, even when the instrument is right at
//     the face and a hand sweeps through them;
//   - between phrases they land on free flower heads (one butterfly a flower), the next note lifts them off;
//   - a few seconds after the music stops they fade out and are gone; in mixed reality (no flowers) they drift instead;
//   - a frame allocates nothing;
//   - every arrangement has flower heads, petals with palette UVs, sway data with the music response on its pivot.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Room.Decor;
using MusicRoom.Room.Geometry;
using MusicRoom.Room.Textures;
using MusicRoom.Room.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Room
{
    public class ButterflyFlockTests
    {
        const float Dt = 1f / 72f;
        static readonly Vector3 HeadAt = new Vector3(0f, 1.2f, 0f);
        static readonly Vector3 Keys = new Vector3(0f, 0.78f, 0.55f), KeysExt = new Vector3(0.6f, 0.03f, 0.1f);
        static readonly Bounds Room = new Bounds(new Vector3(0f, 1.45f, -1.48f), new Vector3(6.4f, 2.9f, 5.2f));

        sealed class Script
        {
            public ButterflyFlock Flock = new ButterflyFlock(7);
            public float Time;
            public Vector3 Head = HeadAt, Forward = Vector3.forward;
            public bool Stopped, InRoom = true;
            public readonly List<Vector3> Perches = new List<Vector3>();
            public readonly List<Vector3> Hands = new List<Vector3>();
            public Vector3 AttrC = Keys, AttrE = KeysExt;
            public float MinHead = float.MaxValue, MinHand = float.MaxValue;
            public int KeepOutViolations;

            public void Frame(float onset, float level)
            {
                Time += Dt;
                Flock.BeginFrame();
                if (level > 0.02f || onset > 0f) Flock.AddAttractor(AttrC, AttrE, Mathf.Max(level, onset));
                for (int i = 0; i < Hands.Count; i++) Flock.AddAvoid(Hands[i]);
                Flock.SetPerches(Perches, InRoom ? Perches.Count : 0);
                Flock.Tick(new FlockFrame
                {
                    Dt = Dt, Time = Time, Head = Head, HeadForward = Forward, Onset = onset, Level = level, Stopped = Stopped,
                    InRoom = InRoom, RoomBox = Room,
                });
                for (int i = 0; i < ButterflyFlock.Max; i++)
                {
                    ref readonly Butterfly b = ref Flock.Get(i);
                    if (b.Mode == ButterflyMode.Off) continue;
                    MinHead = Mathf.Min(MinHead, (b.Pos - Head).magnitude);
                    for (int h = 0; h < Hands.Count; h++) MinHand = Mathf.Min(MinHand, (b.Pos - Hands[h]).magnitude);
                    if (b.Mode != ButterflyMode.ToPerch && b.Mode != ButterflyMode.Perched && level > 0.02f
                        && Mathf.Abs(b.Pos.x - AttrC.x) < AttrE.x && Mathf.Abs(b.Pos.z - AttrC.z) < AttrE.z && b.Pos.y > AttrC.y - AttrE.y && b.Pos.y < AttrC.y + AttrE.y + 0.1f)
                        KeepOutViolations++;
                }
            }

            /// <summary>Notes every `gap` seconds at velocity vel for `seconds`, the level decaying between them.</summary>
            public void Play(float seconds, float gap, float vel)
            {
                int n = Mathf.CeilToInt(seconds / Dt), every = Mathf.Max(1, Mathf.RoundToInt(gap / Dt));
                float level = 0f;
                for (int i = 0; i < n; i++)
                {
                    float onset = i % every == 0 ? vel : 0f;
                    level = Mathf.Max(level * Mathf.Exp(-Dt / 0.55f), onset);
                    Frame(onset, level);
                }
            }

            public void Silence(float seconds) { int n = Mathf.CeilToInt(seconds / Dt); for (int i = 0; i < n; i++) Frame(0f, 0f); }
        }

        static Script WithFlowers()
        {
            var s = new Script();
            // flower heads like the room's: on the mantel and the sill, metres away
            s.Perches.AddRange(new[]
            {
                new Vector3(0.9f, 1.55f, -3.65f), new Vector3(1.0f, 1.58f, -3.66f), new Vector3(1.05f, 1.52f, -3.62f), new Vector3(-0.5f, 1.45f, -3.64f),
                new Vector3(3.05f, 0.85f, -1.2f), new Vector3(3.08f, 0.88f, -1.28f), new Vector3(-3.0f, 2.2f, -2.93f), new Vector3(-2.98f, 2.25f, -2.95f),
            });
            return s;
        }

        [Test]
        public void NoneWithoutMusicThenTheyComeWithTheNotes()
        {
            var s = WithFlowers();
            s.Silence(3f);
            Assert.That(s.Flock.Alive, Is.EqualTo(0), "no music, no butterflies");
            s.Play(3f, 0.4f, 0.35f);
            Assert.That(s.Flock.Alive, Is.EqualTo(ButterflyFlock.MinCount), "soft notes: a few");
            float alpha = 0f;
            for (int i = 0; i < ButterflyFlock.Max; i++) if (s.Flock.Get(i).Mode != ButterflyMode.Off) alpha = Mathf.Max(alpha, s.Flock.Get(i).Alpha);
            Assert.That(alpha, Is.EqualTo(1f), "faded in");
            s.Play(5f, 0.2f, 1f);
            Assert.That(s.Flock.Alive, Is.GreaterThanOrEqualTo(10).And.LessThanOrEqualTo(ButterflyFlock.Max), "loud notes: more");
        }

        [Test]
        public void LouderNotesMakeThemFasterAndBrighter()
        {
            var soft = WithFlowers();
            var loud = WithFlowers();
            soft.Play(6f, 0.25f, 0.3f);
            loud.Play(6f, 0.25f, 1f);
            float Mean(Script s, System.Func<Butterfly, float> f)
            {
                float sum = 0f; int n = 0;
                for (int i = 0; i < ButterflyFlock.Max; i++) { var b = s.Flock.Get(i); if (b.Mode == ButterflyMode.Off) continue; sum += f(b); n++; }
                return n > 0 ? sum / n : 0f;
            }
            Assert.That(Mean(loud, b => b.Glow), Is.GreaterThan(Mean(soft, b => b.Glow) + 0.2f), "brighter");
            Assert.That(Mean(loud, b => b.Excite), Is.GreaterThan(Mean(soft, b => b.Excite)), "more excited: faster wings and flight");
        }

        [Test]
        public void TheyFlutterAroundTheInstrumentAndNeverInsideItsKeys()
        {
            var s = WithFlowers();
            s.Play(12f, 0.3f, 0.8f);
            Assert.That(s.KeepOutViolations, Is.EqualTo(0), "never in the box over the keys");
            int near = 0, alive = 0;
            for (int i = 0; i < ButterflyFlock.Max; i++)
            {
                var b = s.Flock.Get(i);
                if (b.Mode == ButterflyMode.Off) continue;
                alive++;
                if ((b.Pos - Keys).magnitude < 1.6f) near++;
            }
            Assert.That(alive, Is.GreaterThan(0));
            Assert.That(near, Is.GreaterThanOrEqualTo(alive / 2), "most of them around the instrument that sounds");
        }

        [Test]
        public void NeverInTheHeadsSphereOrAtTheFingertips()
        {
            var s = WithFlowers();
            // the instrument right at the face, the head moving through them, hands sweeping over the keys
            s.AttrC = HeadAt + new Vector3(0f, -0.25f, 0.3f);
            for (int k = 0; k < 20; k++)
            {
                s.Hands.Clear();
                float x = Mathf.Sin(k * 0.7f) * 0.4f;
                s.Hands.Add(Keys + new Vector3(x, 0.08f, 0f));
                s.Hands.Add(Keys + new Vector3(-x, 0.12f, 0.05f));
                s.Head = HeadAt + new Vector3(Mathf.Sin(k * 0.9f) * 0.3f, Mathf.Cos(k * 1.3f) * 0.1f, Mathf.Sin(k * 0.4f) * 0.25f);
                s.Play(0.6f, 0.15f, 1f);
            }
            Assert.That(s.Flock.Spawned, Is.GreaterThan(0));
            Assert.That(s.MinHead, Is.GreaterThanOrEqualTo(ButterflyFlock.HeadClear + ButterflyFlock.Reach - 0.02f), "outside the head's sphere");
            Assert.That(s.MinHand, Is.GreaterThanOrEqualTo(ButterflyFlock.HandClear), "clear of the fingertips");
        }

        [Test]
        public void BetweenPhrasesTheyLandOnFreeFlowersAndTheNextNoteLiftsThem()
        {
            var s = WithFlowers();
            s.Play(4f, 0.3f, 0.7f);
            int alive = s.Flock.Alive;
            Assert.That(alive, Is.GreaterThanOrEqualTo(ButterflyFlock.MinCount));
            // a pause while still recording (not stopped): they go to the flowers, metres away, and land
            s.Silence(6.5f);
            Assert.That(s.Flock.Alive, Is.EqualTo(alive), "a pause is not the end");
            Assert.That(s.Flock.Landings, Is.GreaterThanOrEqualTo(alive - 1), "they land");
            var owners = new HashSet<int>();
            int perched = 0;
            for (int i = 0; i < ButterflyFlock.Max; i++)
            {
                var b = s.Flock.Get(i);
                if (b.Mode != ButterflyMode.Perched) continue;
                perched++;
                Assert.IsTrue(owners.Add(b.Perch), "one butterfly a flower");
                Assert.That((b.Pos - s.Perches[b.Perch]).magnitude, Is.LessThan(1e-4f), "on the flower head");
            }
            Assert.That(perched, Is.GreaterThanOrEqualTo(alive - 1));
            s.Play(1.2f, 0.3f, 0.7f);
            perched = 0;
            for (int i = 0; i < ButterflyFlock.Max; i++) if (s.Flock.Get(i).Mode == ButterflyMode.Perched) perched++;
            Assert.That(perched, Is.EqualTo(0), "the music lifts them off");
        }

        [Test]
        public void AFewSecondsAfterTheMusicStopsTheyFadeAndAreGone()
        {
            var s = WithFlowers();
            s.Play(4f, 0.25f, 0.8f);
            Assert.That(s.Flock.Alive, Is.GreaterThan(0));
            s.Stopped = true;   // STOP: the recorder went idle
            s.Silence(ButterflyFlock.LeaveAfterStop - 0.3f);
            Assert.That(s.Flock.Alive, Is.GreaterThan(0), "not at once");
            s.Silence(0.6f + ButterflyFlock.FadeOut * 0.5f);
            float alpha = 0f;
            for (int i = 0; i < ButterflyFlock.Max; i++) if (s.Flock.Get(i).Mode != ButterflyMode.Off) alpha = Mathf.Max(alpha, s.Flock.Get(i).Alpha);
            Assert.That(alpha, Is.LessThan(0.8f).And.GreaterThan(0f), "fading");
            s.Silence(ButterflyFlock.FadeOut);
            Assert.That(s.Flock.Alive, Is.EqualTo(0), "gone");
            // and a recording left running in silence lets them go too
            s.Stopped = false;
            s.Play(3f, 0.3f, 0.6f);
            Assert.That(s.Flock.Alive, Is.GreaterThan(0));
            s.Silence(ButterflyFlock.LeaveAfterSilence + ButterflyFlock.FadeOut + 0.2f);
            Assert.That(s.Flock.Alive, Is.EqualTo(0));
        }

        [Test]
        public void InMixedRealityTheyComeBeyondTheInstrumentsAndDriftInsteadOfLanding()
        {
            var s = WithFlowers();
            s.InRoom = false;
            s.Play(4f, 0.3f, 0.8f);
            Assert.That(s.Flock.Alive, Is.GreaterThanOrEqualTo(ButterflyFlock.MinCount));
            Assert.That(s.Flock.PerchCount, Is.EqualTo(0), "no flowers in mixed reality");
            s.Silence(4f);
            Assert.That(s.Flock.Landings, Is.EqualTo(0), "nothing to land on: they drift");
            for (int i = 0; i < ButterflyFlock.Max; i++)
            {
                var b = s.Flock.Get(i);
                if (b.Mode == ButterflyMode.Off) continue;
                Assert.That(b.Mode, Is.EqualTo(ButterflyMode.Flutter));
                Assert.That((b.Pos - Keys).magnitude, Is.LessThan(2.5f), "still around the instruments");
            }
            Assert.That(s.MinHead, Is.GreaterThanOrEqualTo(ButterflyFlock.HeadClear + ButterflyFlock.Reach - 0.02f));
        }

        [Test]
        public void AFlockFrameAllocatesNothing()
        {
            var s = WithFlowers();
            s.Hands.Add(Keys + Vector3.up * 0.1f);
            s.Play(3f, 0.2f, 0.9f);
            s.Silence(3f);
            s.Play(1f, 0.2f, 0.9f);
            Assert.That(() =>
            {
                s.Play(2f, 0.2f, 0.9f);
                s.Silence(2f);
            }, Is.Not.AllocatingGCMemory());
        }

        // ---------------------------------------------------------------- flowers and wings

        [Test]
        public void EveryArrangementHasFlowerHeadsPetalsAndItsMusicSway()
        {
            foreach (BouquetSpec spec in new[] { FlowerBuilder.Tulips, FlowerBuilder.Daisies, FlowerBuilder.Geraniums, FlowerBuilder.CosmosJar })
            {
                Bouquet b = FlowerBuilder.Build(spec, 5);
                Assert.That(b.FlowerCount, Is.EqualTo(spec.Count), spec.Name);
                Assert.That(b.Heads.Count, Is.EqualTo(spec.Count), spec.Name + ": a perch per flower");
                Assert.That(b.Petals.VertexCount, Is.GreaterThan(100 * spec.Count / 2), spec.Name + ": real petals");
                Assert.That(b.Vase.VertexCount, Is.GreaterThan(0));
                Assert.That(b.Stems.VertexCount, Is.GreaterThan(0));
                Assert.That(b.Leaves.VertexCount, Is.GreaterThan(0));
                foreach (Vector3 h in b.Heads) Assert.That(h.y, Is.GreaterThan(b.MouthY + spec.HeightMin * 0.8f), spec.Name + ": heads above the vase");
                foreach (Vector2 uv in b.Petals.UV)
                {
                    float col = uv.x * PetalPalette.Columns - 0.5f;
                    Assert.That(Mathf.Abs(col - Mathf.Round(col)), Is.LessThan(1e-3f), spec.Name + ": petal UVs at a palette column's centre");
                    Assert.That(uv.y, Is.InRange(0.25f - 1e-4f, 0.75f + 1e-4f));
                }
                Assert.That(b.Amp, Is.InRange(0.01f, 0.03f));
                foreach (Geo g in new[] { b.Vase, b.Stems, b.Leaves, b.Petals, b.Soil })
                    foreach (Vector3 p in g.P) Assert.IsTrue(float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z), spec.Name + ": every vertex finite");
            }
            Assert.That(FlowerBuilder.Response, Is.GreaterThan(0f), "the flowers answer the music (the plants' pivot w is 0)");
        }

        [Test]
        public void TheWingAtlasHasEightOpaqueWingsWithClearSurroundings()
        {
            PixelImage img = ButterflyArt.Wings();
            Assert.That(img.Width, Is.EqualTo(ButterflyArt.Width));
            Assert.That(img.Height, Is.EqualTo(ButterflyArt.Height));
            var hues = new List<Color>();
            for (int d = 0; d < ButterflyArt.Designs; d++)
            {
                Color32 mid = ButterflyArt.Texel(d, 0.45f, 0.75f), out_ = ButterflyArt.Texel(d, 0.98f, 0.1f);
                Assert.That(mid.a, Is.EqualTo(255), "the forewing is opaque, design " + d);
                Assert.That(out_.a, Is.EqualTo(0), "outside the wing is clear, design " + d);
                Vector2 uv0 = ButterflyArt.Uv(d, 0f, 0f), uv1 = ButterflyArt.Uv(d, 1f, 1f);
                Assert.That(uv1.x - uv0.x, Is.LessThan(1f / ButterflyArt.Cols), "inside its tile");
                hues.Add(ButterflyArt.Texel(d, 0.35f, 0.62f));
            }
            int orange = 0;
            foreach (Color c in hues) { Color.RGBToHSV(c, out float h, out float sat, out float v); if (h > 0.03f && h < 0.11f && sat > 0.5f && v > 0.5f) orange++; }
            Assert.That(orange, Is.GreaterThanOrEqualTo(2), "some of them orange like the glow");
        }

        [Test]
        public void TheFlowerPalettesUvsSampleOneColumnEach()
        {
            Vector2 a = PetalPalette.Uv(0, 0f), b = PetalPalette.Uv(7, 1f);
            Assert.That(a.x, Is.EqualTo(0.5f / 8f).Within(1e-6f));
            Assert.That(b.x, Is.EqualTo(7.5f / 8f).Within(1e-6f));
            Assert.That(a.y, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(b.y, Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(PetalPalette.Base.Length, Is.EqualTo(PetalPalette.Columns));
            Assert.That(PetalPalette.Tip.Length, Is.EqualTo(PetalPalette.Columns));
        }
    }
}
