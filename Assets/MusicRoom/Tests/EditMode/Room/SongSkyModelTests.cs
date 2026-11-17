// "Your song becomes a night sky": the star maths (Core/Recording/SongStars.cs) and the sky's numbers
// (Room/Decor/SongSkyModel.cs):
//   - pitch -> height: a higher note is a higher star (piano, marimba, harp, handpan, bowls, in the take's own range), the
//     kit low to high (kick .. crash); the pitch tables match the instruments' own; the palette tells piano and drums apart;
//   - the row's picture: the same take always draws the same picture, another take another; time across, pitch up; at most
//     ThumbMax stars; lines only between consecutive stars of the same instrument; a saved loop over its pass;
//   - the sky: VR under the room's ceiling and inside its walls, MR in open air above the head, never in the head's sphere;
//     higher note higher, left to right with the song; a star rises from its instrument and settles exactly where it was
//     placed; it is joined to the one before it of its instrument; a loop's pass lays its notes over the last one's; a
//     long take reuses the oldest slots; after the end it lingers, fades and clears; a frame allocates nothing;
//   - the recorder's PlaybackNoteEvent carries the event it plays back (which key).
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Lessons;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Room
{
    public class SongSkyModelTests
    {
        const float Dt = 1f / 72f;
        static readonly Vector3 Head = new Vector3(0f, 1.2f, 0f);
        static readonly Bounds Ceiling = new Bounds(new Vector3(0f, 2.9f, -1.48f), new Vector3(6.4f, 0f, 5.2f));

        static SongStars.Types Types => new SongStars.Types(null);

        static SongSkyModel.Frame Vr(Vector3 head, Vector3 fwd, Bounds ceiling) =>
            new SongSkyModel.Frame { Head = head, Forward = fwd, Room = true, Ceiling = ceiling };

        static SongSkyModel.Frame Mr(Vector3 head, Vector3 fwd) => new SongSkyModel.Frame { Head = head, Forward = fwd, Room = false, Floor = 0f };

        static void Run(SongSkyModel m, float seconds)
        {
            int n = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < n; i++) m.Tick(Dt);
        }

        static List<TakeEvent> Song(int notes, float gap, int lo = 60, int step = 2)
        {
            var take = new List<TakeEvent>();
            for (int i = 0; i < notes; i++)
            {
                TakeEvent e = TakeEvent.PianoOn(lo + (i * step) % 14, 0.4f + 0.05f * (i % 9)); e.t = 0.1f + gap * i; take.Add(e);
                TakeEvent off = TakeEvent.PianoOff(e.midi); off.t = e.t + gap * 0.8f; take.Add(off);
                if (i % 2 == 0) { TakeEvent d = TakeEvent.Drum(i % 4 == 0 ? "kick" : "snare", 0.8f, 0.3f, "head", false); d.t = e.t; take.Add(d); }
            }
            return take;
        }

        // ---------------------------------------------------------------- pitch, colour

        [Test]
        public void AHigherNoteIsAHigherStar()
        {
            var range = SongStars.Range.Of(55, 79);
            float last = -1f;
            for (int midi = 55; midi <= 79; midi++)
            {
                Assert.IsTrue(SongStars.TryHeight(InstrumentKind.Piano, TakeEvent.PianoOn(midi, 0.5f), range, out float h));
                Assert.That(h, Is.GreaterThan(last), "midi " + midi);
                Assert.That(h, Is.InRange(0f, 1f));
                last = h;
            }
            float prev = -1f;
            foreach (string piece in new[] { "kick", "floor", "tom2", "tom1", "snare", "hat", "ride", "crash" })
            {
                Assert.IsTrue(SongStars.TryHeight(InstrumentKind.Drums, TakeEvent.Drum(piece, 0.5f, 0.3f, "head", false), range, out float h));
                Assert.That(h, Is.GreaterThan(prev), piece + ": the kit low to high");
                prev = h;
            }
            // a take's own range: at least an octave, centred on what it plays
            var narrow = SongStars.Range.Of(60, 64);
            Assert.That(narrow.Hi - narrow.Lo, Is.EqualTo(SongStars.MinSpan + 2f * SongStars.Pad).Within(1e-4f));
            Assert.That((narrow.Lo + narrow.Hi) / 2f, Is.EqualTo(62f).Within(1e-4f));
            Assert.IsFalse(SongStars.TryHeight(InstrumentKind.Piano, TakeEvent.PianoOn(0, 0.5f), range, out _), "not a note");
        }

        [Test]
        public void ThePitchTablesAreTheInstrumentsOwn()
        {
            for (int i = 0; i < global::MusicRoom.Harp.HarpGeometry.StringCount; i++)
            {
                Assert.IsTrue(SongStars.TryMidi(InstrumentKind.Harp, new TakeEvent { i = i }, out float m));
                Assert.That(m, Is.EqualTo(global::MusicRoom.Harp.HarpGeometry.MidiOf(i)), "harp string " + i);
            }
            for (int i = 0; i < global::MusicRoom.Handpan.HandpanSpec.FieldCount; i++)
            {
                Assert.IsTrue(SongStars.TryMidi(InstrumentKind.Handpan, new TakeEvent { i = i }, out float m));
                Assert.That(m, Is.EqualTo(global::MusicRoom.Handpan.HandpanSpec.MidiOf(i)), "handpan field " + i);
            }
            for (int i = 0; i < global::MusicRoom.Bowls.BowlsLayout.Midi.Length; i++)
            {
                Assert.IsTrue(SongStars.TryMidi(InstrumentKind.Bowls, new TakeEvent { i = i }, out float m));
                Assert.That(m, Is.EqualTo(global::MusicRoom.Bowls.BowlsLayout.Midi[i]), "bowl " + i);
            }
        }

        [Test]
        public void EveryInstrumentHasItsOwnColourAndPianoAndDrumsAreFarApart()
        {
            var kinds = new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp, InstrumentKind.Handpan, InstrumentKind.HandDrums, InstrumentKind.Marimba, InstrumentKind.Bowls };
            for (int a = 0; a < kinds.Length; a++)
                for (int b = a + 1; b < kinds.Length; b++)
                {
                    Color ca = SongStars.ColorOf(kinds[a]), cb = SongStars.ColorOf(kinds[b]);
                    float d = Mathf.Abs(ca.r - cb.r) + Mathf.Abs(ca.g - cb.g) + Mathf.Abs(ca.b - cb.b);
                    Assert.That(d, Is.GreaterThan(0.2f), kinds[a] + " vs " + kinds[b]);
                }
            Color p = SongStars.ColorOf(InstrumentKind.Piano), dr = SongStars.ColorOf(InstrumentKind.Drums);
            Color.RGBToHSV(p, out float hp, out _, out _);
            Color.RGBToHSV(dr, out float hd, out _, out _);
            float hue = Mathf.Abs(hp - hd);
            Assert.That(Mathf.Min(hue, 1f - hue), Is.GreaterThan(0.3f), "piano (gold) and drums (blue): clearly different hues");
        }

        // ---------------------------------------------------------------- the row's picture

        [Test]
        public void TheSameTakeAlwaysDrawsTheSamePicture()
        {
            List<TakeEvent> take = Song(20, 0.25f);
            SongStars.Thumb a = SongStars.BuildThumb(take, Types), b = SongStars.BuildThumb(new List<TakeEvent>(take), new SongStars.Types(null));
            Assert.That(a.Count, Is.GreaterThan(10));
            Assert.That(b.Hash, Is.EqualTo(a.Hash), "the same events: the same picture");
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(b.Pos[i], Is.EqualTo(a.Pos[i]));
                Assert.That(b.Kind[i], Is.EqualTo(a.Kind[i]));
                Assert.That(b.Prev[i], Is.EqualTo(a.Prev[i]));
            }
            SongStars.Thumb other = SongStars.BuildThumb(Song(20, 0.25f, step: 3), Types);
            Assert.That(other.Hash, Is.Not.EqualTo(a.Hash), "another tune: another picture");
            // the same tune played higher draws the same shape (the take's own pitch range)
            Assert.That(SongStars.BuildThumb(Song(20, 0.25f, lo: 65), Types).Hash, Is.EqualTo(a.Hash), "a tune's shape, in any key");
            // into a reused picture: the same again
            SongStars.Thumb again = SongStars.BuildThumb(take, Types, other);
            Assert.That(again.Hash, Is.EqualTo(a.Hash));
        }

        [Test]
        public void ThePictureRunsLeftToRightWithHigherNotesHigher()
        {
            var take = new List<TakeEvent>();
            for (int i = 0; i < 8; i++) { TakeEvent e = TakeEvent.PianoOn(60 + 2 * i, 0.7f); e.t = 0.5f * i; take.Add(e); }
            SongStars.Thumb th = SongStars.BuildThumb(take, Types);
            Assert.That(th.Count, Is.EqualTo(8));
            Assert.That(th.Pos[0].x, Is.EqualTo(0f));
            Assert.That(th.Pos[7].x, Is.EqualTo(1f));
            for (int i = 1; i < 8; i++)
            {
                Assert.That(th.Pos[i].x, Is.GreaterThan(th.Pos[i - 1].x), "time across");
                Assert.That(th.Pos[i].y, Is.GreaterThan(th.Pos[i - 1].y), "a rising scale rises");
                Assert.That(th.Prev[i], Is.EqualTo(i - 1), "joined to the note before");
            }
            Assert.That(th.Prev[0], Is.EqualTo(-1));
        }

        [Test]
        public void ALongTakeKeepsAtMostThumbMaxAndJoinsOnlyItsOwnInstrument()
        {
            SongStars.Thumb th = SongStars.BuildThumb(Song(400, 0.1f), Types);
            Assert.That(th.Count, Is.EqualTo(SongStars.ThumbMax));
            bool piano = false, drums = false;
            for (int i = 0; i < th.Count; i++)
            {
                piano |= th.Kind[i] == InstrumentKind.Piano;
                drums |= th.Kind[i] == InstrumentKind.Drums;
                Assert.That(th.Pos[i].x, Is.InRange(0f, 1f));
                Assert.That(th.Pos[i].y, Is.InRange(0f, 1f));
                if (th.Prev[i] >= 0)
                {
                    Assert.That(th.Kind[th.Prev[i]], Is.EqualTo(th.Kind[i]), "a line joins one instrument's stars");
                    for (int j = th.Prev[i] + 1; j < i; j++) Assert.That(th.Kind[j], Is.Not.EqualTo(th.Kind[i]), "to the one just before it");
                }
            }
            Assert.IsTrue(piano && drums, "both instruments are in the picture");
            Assert.That(SongStars.BuildThumb(new List<TakeEvent>(), Types).Count, Is.EqualTo(0));
            Assert.That(SongStars.BuildThumb(null, Types).Count, Is.EqualTo(0));
        }

        [Test]
        public void ASavedLoopIsDrawnOverItsPass()
        {
            var s = new LoopSession(90, 2, 48000);
            List<TakeEvent> enc = LoopFormat.Encode(s);
            Assert.IsTrue(LoopFormat.IsLoop(enc));
            float pass = enc[0].speed;
            enc.Add(new TakeEvent { type = LoopFormat.LayerMark, i = 0 });
            for (int i = 0; i < 4; i++) { TakeEvent e = TakeEvent.PianoOn(60 + i, 0.7f); e.t = pass * 0.2f * i; enc.Add(e); }
            enc.Add(new TakeEvent { type = LoopFormat.LayerMark, i = 1 });
            for (int i = 0; i < 4; i++) { TakeEvent e = TakeEvent.Drum("snare", 0.7f, 0.3f, "head", false); e.t = pass * (0.1f + 0.2f * i); enc.Add(e); }
            SongStars.Thumb th = SongStars.BuildThumb(enc, Types);
            Assert.That(th.Count, Is.EqualTo(8), "the marks are not stars");
            for (int i = 1; i < th.Count; i++) Assert.That(th.Pos[i].x, Is.GreaterThanOrEqualTo(th.Pos[i - 1].x), "the layers over one pass, in time");
            Assert.That(th.Pos[th.Count - 1].x, Is.LessThan(1f), "the pass is the width, not its last note");
        }

        // ---------------------------------------------------------------- the sky

        [Test]
        public void InTheRoomTheSkyIsUnderTheCeilingInsideTheWallsAndClearOfTheHead()
        {
            foreach (Vector3 fwd in new[] { Vector3.forward, Vector3.back, Vector3.left, new Vector3(1f, 0f, 1f) })
                foreach (float headY in new[] { 1.1f, 1.7f })
                {
                    var m = new SongSkyModel();
                    var head = new Vector3(0f, headY, 0f);
                    m.Start(Vr(head, fwd, Ceiling), 0, 10f, false);
                    for (int i = 0; i <= 20; i++)
                        for (int j = 0; j <= 4; j++)
                        {
                            Vector3 p = m.Place(i / 20f, j / 4f);
                            Assert.That(p.y, Is.LessThanOrEqualTo(Ceiling.max.y - SongSkyModel.TopGap + 1e-4f), "under the ceiling");
                            Assert.That(p.y, Is.GreaterThanOrEqualTo(headY + 0.5f), "well above the head: never before the eyes");
                            Assert.That(p.x, Is.InRange(Ceiling.min.x + SongSkyModel.Margin - 1e-3f, Ceiling.max.x - SongSkyModel.Margin + 1e-3f), "inside the walls");
                            Assert.That(p.z, Is.InRange(Ceiling.min.z + SongSkyModel.Margin - 1e-3f, Ceiling.max.z - SongSkyModel.Margin + 1e-3f), "inside the walls");
                            Assert.That((p - head).magnitude, Is.GreaterThanOrEqualTo(SongSkyModel.HeadClear), "clear of the head");
                            // above the eyes: well up from where the player looks
                            Vector3 v = p - head;
                            float up = Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;
                            Assert.That(up, Is.GreaterThan(15f), "high in the view, never in front of the eyes");
                        }
                }
        }

        [Test]
        public void TheSkyFollowsTheRoomWhereverItsCeilingIs()
        {
            // a room turned around (the fireplace ahead): the same rules against the ceiling as built
            var turned = new Bounds(new Vector3(0f, 2.9f, 1.48f), new Vector3(6.4f, 0f, 5.2f));
            var m = new SongSkyModel();
            m.Start(Vr(Head, Vector3.forward, turned), 0, 10f, false);
            Vector3 mid = m.Place(0.5f, 0.5f);
            Assert.That(mid.z, Is.GreaterThan(1.5f), "out ahead, where the room is");
            Assert.That(mid.z, Is.LessThanOrEqualTo(turned.max.z - SongSkyModel.Margin + 1e-3f));
            // a low ceiling: the stars stay under it and still above the head
            var low = new Bounds(new Vector3(0f, 2.3f, 0f), new Vector3(6f, 0f, 6f));
            m.Start(Vr(Head, Vector3.forward, low), 0, 10f, false);
            for (int j = 0; j <= 4; j++)
            {
                Vector3 p = m.Place(0.5f, j / 4f);
                Assert.That(p.y, Is.LessThanOrEqualTo(2.3f - SongSkyModel.TopGap + 1e-4f));
                Assert.That((p - Head).magnitude, Is.GreaterThan(SongSkyModel.HeadClear));
            }
        }

        [Test]
        public void InMixedRealityTheStarsFloatAboveTheHeadInOpenAir()
        {
            foreach (float headY in new[] { 1.1f, 1.75f })
            {
                var m = new SongSkyModel();
                var head = new Vector3(0.3f, headY, -0.2f);
                m.Start(Mr(head, new Vector3(0.2f, -0.4f, 1f)), 0, 10f, false);
                for (int i = 0; i <= 10; i++)
                    for (int j = 0; j <= 4; j++)
                    {
                        Vector3 p = m.Place(i / 10f, j / 4f);
                        Assert.That(p.y - headY, Is.GreaterThanOrEqualTo(SongSkyModel.MrAbove - 1e-4f), "above the head");
                        Assert.That((p - head).magnitude, Is.GreaterThanOrEqualTo(SongSkyModel.HeadClear));
                        Vector3 flat = p - head; flat.y = 0f;
                        Assert.That(flat.magnitude, Is.EqualTo(SongSkyModel.MrDistance).Within(1e-3f), "out over the instruments");
                        Assert.That(Mathf.Atan2(p.y - headY, flat.magnitude) * Mathf.Rad2Deg, Is.GreaterThan(22f), "never in front of the eyes");
                    }
                Assert.That(m.Place(0.5f, 1f).y, Is.GreaterThan(m.Place(0.5f, 0f).y), "a higher note, higher");
            }
        }

        [Test]
        public void HigherNotesSettleHigherAndTheSongRunsLeftToRight()
        {
            var m = new SongSkyModel();
            m.Start(Vr(Head, Vector3.forward, new Bounds(new Vector3(0f, 2.9f, 1.5f), new Vector3(6.4f, 0f, 5.2f))), 10.0, 8f, false);
            Vector3 lo = m.Place(0.5f, 0.1f), hi = m.Place(0.5f, 0.9f);
            Assert.That(hi.y - lo.y, Is.GreaterThan(0.4f), "pitch is height");
            Vector3 first = m.Place(0f, 0.5f), last = m.Place(1f, 0.5f);
            Assert.That(first.x, Is.LessThan(-0.3f), "the song starts on the left");
            Assert.That(last.x, Is.GreaterThan(0.3f), "and ends on the right");
            Assert.That(m.SongFraction(10.0), Is.EqualTo(0f));
            Assert.That(m.SongFraction(14.0), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(m.SongFraction(30.0), Is.EqualTo(1f), "a take's arc ends at its end");
        }

        [Test]
        public void AStarRisesFromItsInstrumentAndSettlesExactlyWhereItWasPlaced()
        {
            var m = new SongSkyModel();
            m.Start(Vr(Head, Vector3.forward, Ceiling), 0, 4f, false);
            var keys = new Vector3(0.1f, 0.85f, 0.45f);
            int s = m.Note(InstrumentKind.Piano, keys, 1.0, 0.7f, 0.9f);
            Assert.That(s, Is.GreaterThanOrEqualTo(0));
            SongSkyModel.Star st = m.Stars[s];
            Assert.That(SongSkyModel.PositionAt(st, st.Birth, Head), Is.EqualTo(keys), "born over the key");
            Assert.That(st.To, Is.EqualTo(m.Place(0.25f, 0.7f)), "placed by its time and height");
            float lastY = keys.y;
            for (float t = 0.1f; t <= SongSkyModel.Rise; t += 0.1f)
            {
                Vector3 p = SongSkyModel.PositionAt(st, st.Birth + t, Head);
                Assert.That((p - Head).magnitude, Is.GreaterThanOrEqualTo(SongSkyModel.HeadClear - 1e-4f), "never in the head");
                Assert.That(p.y, Is.GreaterThanOrEqualTo(lastY - 0.02f), "it rises");
                lastY = p.y;
            }
            Assert.That(SongSkyModel.PositionAt(st, st.Birth + SongSkyModel.Rise + 3f, Head), Is.EqualTo(st.To), "settled exactly");
            // a head leaning over the keys pushes the rising star out of its sphere
            Vector3 leaning = keys + new Vector3(0f, 0.15f, 0f);
            Vector3 q = SongSkyModel.PositionAt(st, st.Birth + 0.05f, leaning);
            Assert.That((q - leaning).magnitude, Is.GreaterThanOrEqualTo(SongSkyModel.HeadClear - 1e-4f));
            // louder: bigger and brighter
            int soft = m.Note(InstrumentKind.Piano, keys, 1.5, 0.7f, 0.1f);
            Assert.That(m.Stars[soft].Size, Is.LessThan(st.Size));
            Assert.That(m.Stars[soft].Bright, Is.LessThan(st.Bright));
        }

        [Test]
        public void EachStarIsJoinedToTheOneBeforeItOfItsInstrument()
        {
            var m = new SongSkyModel();
            m.Start(Vr(Head, Vector3.forward, Ceiling), 0, 4f, false);
            int p0 = m.Note(InstrumentKind.Piano, Vector3.zero, 0.2, 0.3f, 0.6f);
            int d0 = m.Note(InstrumentKind.Drums, Vector3.zero, 0.3, 0.2f, 0.6f);
            int p1 = m.Note(InstrumentKind.Piano, Vector3.zero, 0.4, 0.5f, 0.6f);
            int d1 = m.Note(InstrumentKind.Drums, Vector3.zero, 0.5, 0.6f, 0.6f);
            int p2 = m.Note(InstrumentKind.Piano, Vector3.zero, 0.6, 0.4f, 0.6f);
            Assert.That(m.Stars[p0].Prev, Is.EqualTo(-1));
            Assert.That(m.Stars[d0].Prev, Is.EqualTo(-1));
            Assert.That(m.Stars[p1].Prev, Is.EqualTo(p0));
            Assert.That(m.Stars[p2].Prev, Is.EqualTo(p1));
            Assert.That(m.Stars[d1].Prev, Is.EqualTo(d0));
            Assert.That(m.Live, Is.EqualTo(5));
        }

        [Test]
        public void ALoopsNextPassTakesTheOldStarsPlaces()
        {
            var m = new SongSkyModel();
            m.Start(Vr(Head, Vector3.forward, Ceiling), 0, 2f, true);
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < 4; i++) m.Note(InstrumentKind.Piano, Vector3.zero, pass * 2.0 + 0.5 * i, 0.2f * i, 0.7f);
                Run(m, 2f);
            }
            Assert.That(m.Live, Is.EqualTo(4), "one star per note of the loop, not one per pass");
            int first = -1;
            for (int i = 0; i < SongSkyModel.Max; i++)
                if (m.Stars[i].Alive && m.Stars[i].Prev == -1) { Assert.That(first, Is.EqualTo(-1), "one start of the line"); first = i; }
            Assert.That(m.SongFraction(5.0), Is.EqualTo(0.5f).Within(1e-5f), "a loop's time wraps");
        }

        [Test]
        public void ALongTakeReusesTheOldestSlotsAfterTheyFade()
        {
            var m = new SongSkyModel();
            m.Start(Vr(Head, Vector3.forward, Ceiling), 0, 120f, false);
            for (int i = 0; i < 1000; i++)
            {
                m.Note(i % 3 == 0 ? InstrumentKind.Drums : InstrumentKind.Piano, Vector3.zero, i * 0.12, (i % 13) / 13f, 0.7f);
                m.Tick(0.12f);
                Assert.That(m.Live, Is.LessThanOrEqualTo(SongSkyModel.Max));
            }
            Assert.That(m.TotalMade, Is.EqualTo(1000));
            Assert.That(m.Live, Is.GreaterThan(SongSkyModel.Max - SongSkyModel.FadeAhead - 30));
            for (int i = 0; i < SongSkyModel.Max; i++)
            {
                SongSkyModel.Star s = m.Stars[i];
                if (!s.Alive || s.Prev < 0) continue;
                Assert.IsTrue(m.Stars[s.Prev].Alive, "a line never starts from a star that is gone");
                Assert.That(m.Stars[s.Prev].Kind, Is.EqualTo(s.Kind));
            }
        }

        [Test]
        public void AfterTheEndTheSkyLingersFadesAndClears()
        {
            var m = new SongSkyModel();
            Assert.IsTrue(m.Resting);
            m.Start(Vr(Head, Vector3.forward, Ceiling), 0, 4f, false);
            for (int i = 0; i < 10; i++) m.Note(InstrumentKind.Harp, Vector3.zero, 0.3 * i, 0.5f, 0.7f);
            Run(m, 2f);
            m.End();
            Assert.That(m.State, Is.EqualTo(SongSkyModel.Phase.Lingering));
            Run(m, SongSkyModel.Linger - 0.2f);
            Assert.That(m.Fade, Is.EqualTo(1f), "it lingers");
            Assert.That(m.Live, Is.EqualTo(10));
            Run(m, 0.4f + SongSkyModel.FadeOut * 0.5f);
            Assert.That(m.Fade, Is.InRange(0.2f, 0.8f), "then fades");
            Run(m, SongSkyModel.FadeOut);
            Assert.IsTrue(m.Resting, "and rests");
            Assert.That(m.Live, Is.EqualTo(0));
            Assert.IsFalse(m.HasFrame);
            for (int i = 0; i < SongSkyModel.Max; i++) Assert.IsFalse(m.Shown(i, m.Time), "nothing is drawn");
            // a new playback while the old sky still shows: the old stars fade, the new sky starts
            m.Start(Mr(Head, Vector3.forward), 0, 4f, false);
            int a = m.Note(InstrumentKind.Piano, Vector3.zero, 0.1, 0.5f, 0.7f);
            m.Start(Mr(Head, Vector3.forward), 1, 4f, false);
            Assert.That(m.Stars[a].Death, Is.EqualTo(m.Time), "the old sky's stars fade");
            Run(m, SongSkyModel.DieFade + 0.1f);
            Assert.That(m.Live, Is.EqualTo(0));
        }

        [Test]
        public void ASkyFrameAllocatesNothing()
        {
            var m = new SongSkyModel();
            m.Start(Vr(Head, Vector3.forward, Ceiling), 0, 6f, true);
            double t = 0;
            for (int i = 0; i < 400; i++) { m.Note(InstrumentKind.Piano, Vector3.zero, t, (i % 7) / 7f, 0.6f); t += 0.05; m.Tick(Dt); m.ClearDirty(); }
            Assert.That(() =>
            {
                for (int i = 0; i < 300; i++)
                {
                    if (i % 3 == 0) m.Note(i % 2 == 0 ? InstrumentKind.Drums : InstrumentKind.Piano, Vector3.zero, t, (i % 7) / 7f, 0.6f);
                    t += Dt;
                    m.Tick(Dt);
                    m.ClearDirty();
                    SongSkyModel.PositionAt(m.Stars[i % SongSkyModel.Max], m.Time, Head);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        // ---------------------------------------------------------------- the source

        [Test]
        public void TheRecorderSaysWhichNoteItPlaysBack()
        {
            double now = 100;
            var bus = new EventBus();
            var notes = new List<PlaybackNoteEvent>();
            bus.Subscribe<PlaybackNoteEvent>(e => notes.Add(e));
            var rec = new Recorder(() => now, bus);
            rec.SetTarget(TakeEventType.PianoOn, e => { });
            rec.SetTarget(TakeEventType.Harp, e => { });
            var take = new List<TakeEvent>();
            TakeEvent a = TakeEvent.PianoOn(67, 0.8f); a.t = 0.1f; take.Add(a);
            TakeEvent b = TakeEvent.Harp(9, 0.6f, 0.35f, 0f, "through"); b.t = 0.2f; take.Add(b);
            Assert.IsTrue(rec.PlayEvents(take, 1.0));
            now += 0.3; rec.Update();
            Assert.That(notes.Count, Is.EqualTo(2));
            Assert.That(notes[0].Event.midi, Is.EqualTo(67));
            Assert.That(notes[0].Event.type, Is.EqualTo(TakeEventType.PianoOn));
            Assert.That(notes[1].Event.i, Is.EqualTo(9));
            Assert.That(notes[1].Event.lane, Is.EqualTo(PlaybackLanes.Take));
        }
    }
}
