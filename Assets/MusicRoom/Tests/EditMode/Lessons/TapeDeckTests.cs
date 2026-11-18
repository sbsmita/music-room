// Tape deck (spec 08 section 4.3, Docs/07-world-visuals.md tapeDeck): the 18 mm x 60-95 mm press cylinders, once
// per entry per pointer, peg travel 5 mm at 18/s with REC/PLAY latching, reels 3/4 rad/s (Unity yaw negative), lamp
// colours, hover glows, the "table" play surface, and the desktop click path (peg colliders on the play-surface layer;
// mouse-hand pointers never press). Pointers are driven by hand (Tip / Active / WasActive) like the reference check drives p.tip.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Lessons
{
    public class TapeDeckTests
    {
        double _now;
        GameObject _table;
        Recorder _rec;
        TapeDeck _deck;
        HandState _hand;
        List<Pointer> _pointers;

        [SetUp]
        public void SetUp()
        {
            _now = 10;
            _table = new GameObject("TableRig");
            _table.transform.SetPositionAndRotation(new Vector3(-0.39f, 0f, -0.21f), Quaternion.Euler(0f, -90f, 0f));
            _rec = new Recorder(() => _now, null);
            _deck = new TapeDeck(_rec, new MaterialLibrary(), _table.transform, TapeDeck.TableLocalPosition);
            _hand = new HandState(Handedness.Right);
            _pointers = new List<Pointer>(_hand.Pointers);
        }

        [TearDown]
        public void TearDown()
        {
            _deck.Destroy();
            Object.DestroyImmediate(_table);
        }

        Pointer Index => _hand.Pointers[0];

        // One tracker frame for the index tip at a deck-local point (Prev / WasActive roll over like PointerInput.Update).
        void TipAt(Vector3 local, float dt = 0.016f)
        {
            Pointer p = Index;
            p.WasActive = p.Active;
            p.Prev = p.Tip;
            p.Tip = _deck.Root.TransformPoint(local);
            p.Active = true;
            _deck.Tick(_pointers, dt);
        }

        static Vector3 Over(DeckButton b, float y, float dx = 0f, float dz = 0f) => new Vector3(TapeDeck.ButtonX[(int)b] + dx, y, TapeDeck.PegZ + dz);

        [Test]
        public void TheDeckIsATableInstrumentOnTheSideTable()
        {
            Assert.That(_deck.Kind, Is.EqualTo(InstrumentKind.Table));
            Assert.That(_deck.TickOrder, Is.EqualTo(30));
            Assert.IsFalse(_deck.AcceptsStickPointers);
            Assert.IsTrue(_deck.IsPresent);
            Assert.That(_deck.Root.parent, Is.SameAs(_table.transform));
            Assert.That(_deck.Root.localPosition, Is.EqualTo(new Vector3(0.07f, 0.6f, 0.04f)), "the original spot, 4 cm back for the loop strip");
            // From the seat the pegs are on the player's side: deck -z points to world +x (toward the seat at x = 0).
            Vector3 toPlayer = _deck.Root.TransformDirection(Vector3.back);
            Assert.That(toPlayer.x, Is.GreaterThan(0.99f));
            Assert.That(_deck.PlayedParts.Count, Is.EqualTo(2), "the pegs (one mesh) and the lamps with their lit caps (one mesh)");
            Assert.That(_deck.PlayedParts[0].name, Is.EqualTo("Pegs"));
            Assert.That(_deck.PlayedParts[1].name, Is.EqualTo("Lamps"));
            Assert.That(_deck.HandleRenderers.Count, Is.EqualTo(4), "body, wood (top, cheeks, hubs), tapes, chrome (head bar, spokes)");
            // The loop strip stays on the table top (the table is 0.32 m deep, centred on the rig).
            Bounds body = _deck.HandleRenderers[0].GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.That(TapeDeck.TableLocalPosition.z + body.min.z, Is.GreaterThan(-0.16f), "the front edge is on the table");
            Assert.That(TapeDeck.TableLocalPosition.z + body.max.z, Is.LessThan(0.16f), "and so is the back");
            foreach (Renderer r in _deck.HandleRenderers) CollectionAssert.DoesNotContain(_deck.PlayedParts, r);
            AssertLamp(TapeDeck.LampIdle, "idle lamp #332222");
        }

        // ARCHITECTURE.md sections 14 and 17: table + tape deck + switches <= 15, of which Room's side table takes 8. One
        // draw per renderer and material, so the deck may have at most 7 single-material renderers; and no
        // MaterialPropertyBlock on its URP Lit renderers (it would take them out of the SRP Batcher).
        [Test]
        public void TheDeckDrawsSevenTimesWithoutPropertyBlocks()
        {
            Renderer[] all = _deck.Root.GetComponentsInChildren<Renderer>(true);
            Assert.That(all.Length, Is.EqualTo(TapeDeck.DrawBudget), "body, wood, tapes, chrome, lamps, pegs, labels");
            Assert.That(TapeDeck.DrawBudget, Is.LessThanOrEqualTo(15 - 8));
            foreach (Renderer r in all)
            {
                Assert.That(r.sharedMaterials.Length, Is.LessThanOrEqualTo(1), r.name + ": one material, one draw");
                Assert.IsFalse(r.HasPropertyBlock(), r.name + ": no MaterialPropertyBlock");
            }
            foreach (Renderer r in all) Assert.IsNull(r.GetComponent<TMPro.TextMeshPro>(), r.name + ": the words are baked into one mesh");
            Assert.That(_deck.LabelMesh, Is.Not.Null, "every word in one label mesh");
            foreach (var (_, word) in TapeDeck.Words) Assert.That(_deck.Labels.IndexOf(word), Is.GreaterThanOrEqualTo(0), word);
            Assert.That(_deck.Labels.IndexOf(TapeDeck.TapAgain), Is.GreaterThanOrEqualTo(0));
            // Every lamp and lit cap shares ONE material; its emission map is the lamps' state texture (no property block).
            Renderer lamps = _deck.PlayedParts[1];
            Assert.That(lamps.sharedMaterial.GetTexture("_EmissionMap"), Is.SameAs(_deck.LampTexture));
            Assert.That(_deck.LampTexture.width, Is.EqualTo(TapeDeck.LampTexels));
        }

        // The pegs share one palette-textured material: each peg's UVs sit at the centre of its colour's cell, and every
        // cell is one flat ASTC 6x6 block of the baked texture (TextureBaker bakes ASTC 6x6), so no colour is approximated.
        [Test]
        public void ThePaletteCellsAreFlatAstcBlocksAndEachPegSamplesItsOwnColour()
        {
            Assert.That(DeckPalette.Cell % 6, Is.EqualTo(0), "a cell is a whole number of ASTC 6x6 blocks");
            for (int i = 0; i < 3; i++)
                Assert.That((Color)DeckPalette.Colors[DeckPalette.Rec + i], Is.EqualTo(TapeDeck.ButtonColor[i]), "spec 08 peg colours");
            Texture2D t = DeckPalette.Generate();
            try
            {
                Assert.That(t.width, Is.EqualTo(DeckPalette.Cells * DeckPalette.Cell));
                Assert.That(t.height, Is.EqualTo(DeckPalette.Cell));
                Assert.That(t.mipmapCount, Is.EqualTo(1));
                Color32[] px = t.GetPixels32();
                for (int c = 0; c < DeckPalette.Colors.Length; c++)
                {
                    Color32 want = DeckPalette.Colors[c];
                    for (int y = 0; y < DeckPalette.Cell; y++)
                        for (int x = c * DeckPalette.Cell; x < (c + 1) * DeckPalette.Cell; x++)
                            Assert.That(px[y * t.width + x], Is.EqualTo(want), $"cell {c} is one flat colour");
                    // Bilinear filtering at the cell's UV reads the two texels around it: both inside the cell.
                    Vector2 uv = DeckPalette.Uv(c);
                    int x0 = Mathf.FloorToInt(uv.x * t.width - 0.5f);
                    Assert.That(x0, Is.GreaterThanOrEqualTo(c * DeckPalette.Cell)); Assert.That(x0 + 1, Is.LessThan((c + 1) * DeckPalette.Cell));
                }
            }
            finally { Object.DestroyImmediate(t); }
            MergedMesh pegs = _deck.PegParts;
            Vector2[] uvs = pegs.Mesh.uv;
            for (int i = 0; i < 3; i++)
            {
                int part = _deck.PegPart((DeckButton)i), s = pegs.PartStart(part);
                for (int k = s; k < s + pegs.PartVertexCount(part); k++)
                    Assert.That(uvs[k], Is.EqualTo(DeckPalette.Uv(DeckPalette.Rec + i)), $"peg {(DeckButton)i} samples its own cell");
            }
            int loop = _deck.PegPart(DeckButton.Loop), ls = pegs.PartStart(loop);
            for (int k = ls; k < ls + pegs.PartVertexCount(loop); k++)
                Assert.That(uvs[k], Is.EqualTo(DeckPalette.Uv(DeckPalette.LoopCap)), "LOOP samples the brass-amber cell");
        }

        float PegTop(DeckButton b)
        {
            MergedMesh m = _deck.PegParts;
            Vector3[] v = m.Mesh.vertices;
            int part = _deck.PegPart(b), s = m.PartStart(part);
            float top = float.MinValue;
            for (int k = s; k < s + m.PartVertexCount(part); k++) top = Mathf.Max(top, v[k].y);
            return top;
        }

        [Test]
        public void APegPressesOncePerEntryAndHoldingDoesNotRepeat()
        {
            TipAt(Over(DeckButton.Rec, 0.12f));
            TipAt(Over(DeckButton.Rec, 0.12f));
            Assert.That(_deck.Presses, Is.EqualTo(0), "above the cylinder");
            TipAt(Over(DeckButton.Rec, 0.08f));
            Assert.That(_deck.Presses, Is.EqualTo(1));
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Recording), "REC -> Record");
            for (int i = 0; i < 5; i++) TipAt(Over(DeckButton.Rec, 0.075f));
            Assert.That(_deck.Presses, Is.EqualTo(1), "a lingering tip does not press again");
            TipAt(Over(DeckButton.Rec, 0.12f));
            TipAt(Over(DeckButton.Stop, 0.12f));
            TipAt(Over(DeckButton.Stop, 0.08f));
            Assert.That(_deck.Presses, Is.EqualTo(2));
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Idle), "STOP -> Stop");
        }

        [Test]
        public void ATipMustBeActiveForTwoFramesToPress()
        {
            Pointer p = Index;
            p.Active = false;
            TipAt(Over(DeckButton.Rec, 0.08f));          // appears inside: WasActive is false this frame
            Assert.That(_deck.Presses, Is.EqualTo(0));
            TipAt(Over(DeckButton.Rec, 0.08f));
            Assert.That(_deck.Presses, Is.EqualTo(1), "the next frame (active twice) presses");
        }

        [Test]
        public void ThePressCylinderIs18mmWideAnd60To95mmUp()
        {
            void Probe(Vector3 local, bool expect, string why)
            {
                int before = _deck.Presses;
                TipAt(new Vector3(local.x, 0.13f, local.z), TapeDeck.RepressSeconds);   // a new poke, not a repeat of the last one
                TipAt(new Vector3(local.x, 0.13f, local.z));
                TipAt(local);
                Assert.That(_deck.Presses - before, Is.EqualTo(expect ? 1 : 0), why);
                TipAt(new Vector3(local.x, 0.13f, local.z));
            }
            Probe(Over(DeckButton.Stop, 0.08f, dx: 0.017f), true, "17 mm off the axis presses");
            Probe(Over(DeckButton.Stop, 0.08f, dx: 0.019f), false, "19 mm off the axis does not");
            Probe(Over(DeckButton.Stop, 0.08f, dz: -0.019f), false, "nor 19 mm toward the player");
            Probe(Over(DeckButton.Stop, 0.0945f), true, "just under 95 mm presses");
            Probe(Over(DeckButton.Stop, 0.0955f), false, "just over 95 mm does not");
            Probe(Over(DeckButton.Stop, 0.059f), false, "under 60 mm does not");
        }

        [Test]
        public void PegsSinkFiveMillimetresAtRate18AndRecAndPlayLatch()
        {
            Vector3 rest = _deck.PegLocalPosition(DeckButton.Rec);
            Assert.That(rest.y, Is.EqualTo(TapeDeck.PegY).Within(1e-6f));
            TipAt(Over(DeckButton.Rec, 0.12f));
            TipAt(Over(DeckButton.Rec, 0.12f));
            TipAt(Over(DeckButton.Rec, 0.08f), 0.016f);
            float k = Mathf.Min(1f, 0.016f * 18f);
            Assert.That(_deck.PegDepth(DeckButton.Rec), Is.EqualTo(k).Within(1e-5f), "depth += (1 - depth) * min(1, dt * 18)");
            Assert.That(_deck.PegLocalPosition(DeckButton.Rec).y, Is.EqualTo(TapeDeck.PegY - k * 0.005f).Within(1e-6f));
            TipAt(Over(DeckButton.Rec, 0.12f), 0.5f);
            Assert.That(_deck.PegDepth(DeckButton.Rec), Is.EqualTo(1f), "REC latched while recording");
            Assert.That(_deck.PegLocalPosition(DeckButton.Rec).y, Is.EqualTo(TapeDeck.PegY - 0.005f).Within(1e-6f), "5 mm travel");
            // What is drawn: the REC peg's vertices in the shared peg mesh went down 5 mm, the other two stayed.
            Assert.That(PegTop(DeckButton.Rec), Is.EqualTo(TapeDeck.PegY + TapeDeck.PegH * 0.5f - 0.005f).Within(1e-5f), "the REC peg's mesh sank");
            Assert.That(PegTop(DeckButton.Stop), Is.EqualTo(TapeDeck.PegY + TapeDeck.PegH * 0.5f).Within(1e-5f), "STOP's did not");
            _rec.Add(TakeEvent.PianoOn(60, 0.5f));
            _now += 1;
            _rec.Stop();
            TipAt(Over(DeckButton.Rec, 0.12f), 0.5f);
            Assert.That(_deck.PegDepth(DeckButton.Rec), Is.EqualTo(0f), "REC comes back up");
            Assert.IsTrue(_rec.Play());
            TipAt(Over(DeckButton.Rec, 0.12f), 0.5f);
            Assert.That(_deck.PegDepth(DeckButton.Play), Is.EqualTo(1f), "PLAY down during any playback");
            Assert.That(_deck.PegDepth(DeckButton.Stop), Is.EqualTo(0f));
        }

        [Test]
        public void ReelsTurnThreeRecordingFourPlayingAndTheLampFollows()
        {
            TipAt(Vector3.up, 0.5f);
            Assert.That(_deck.ReelAngle, Is.EqualTo(0f), "idle: still");
            MergedMesh chrome = _deck.ChromeParts;
            Vector3[] rest = chrome.Mesh.vertices;
            _rec.Record();
            TipAt(Vector3.up, 0.5f);
            Assert.That(_deck.ReelAngle, Is.EqualTo(1.5f).Within(1e-5f), "3 rad/s recording");
            Quaternion want = Quaternion.Euler(0f, -1.5f * Mathf.Rad2Deg, 0f);
            Assert.That(Quaternion.Angle(_deck.ReelLocalRotation(0), want), Is.LessThan(0.01f), "Unity yaw -angle (JSON spin_unity -3)");
            Assert.That(Quaternion.Angle(_deck.ReelLocalRotation(1), want), Is.LessThan(0.01f));
            // What is drawn: each reel's spokes turned about its own axis in the chrome mesh; the head bar did not move.
            Vector3[] now = chrome.Mesh.vertices;
            for (int reel = 0; reel < 2; reel++)
            {
                int part = _deck.SpokePart(reel), s = chrome.PartStart(part);
                Vector3 axis = TapeDeck.ReelLocal[reel];
                for (int k = s; k < s + chrome.PartVertexCount(part); k++)
                    Assert.That(Vector3.Distance(now[k] - axis, want * (rest[k] - axis)), Is.LessThan(1e-5f), $"reel {reel} spoke vertex {k - s}");
            }
            for (int k = chrome.PartStart(0); k < chrome.PartStart(0) + chrome.PartVertexCount(0); k++)
                Assert.That(now[k], Is.EqualTo(rest[k]), "the head bar is static");
            AssertLamp(TapeDeck.LampRecording, "#ff2a2a while recording");
            _rec.Add(TakeEvent.PianoOn(60, 0.5f));
            _now += 2;
            _rec.Stop();
            Assert.IsTrue(_rec.Play());
            TipAt(Vector3.up, 0.25f);
            Assert.That(_deck.ReelAngle, Is.EqualTo(2.5f).Within(1e-5f), "4 rad/s playing");
            AssertLamp(TapeDeck.LampPlaying, "#2aff5a while playing");
            _rec.Stop();
            TipAt(Vector3.up, 0.5f);
            Assert.That(_deck.ReelAngle, Is.EqualTo(2.5f).Within(1e-5f), "stops with the recorder");
            AssertLamp(TapeDeck.LampIdle, "#332222 idle");
        }

        void AssertLamp(Color srgb, string why)
        {
            // Material.SetColor stores the colour as given (gamma) in the material; compare in sRGB.
            Color c = _deck.LampColor;
            Assert.That(c.r, Is.EqualTo(srgb.r).Within(0.01f), why);
            Assert.That(c.g, Is.EqualTo(srgb.g).Within(0.01f), why);
            Assert.That(c.b, Is.EqualTo(srgb.b).Within(0.01f), why);
        }

        [Test]
        public void AHoveringTipLightsThePegItIsAbout()
        {
            var glow = new HoverGlowPool(new MaterialLibrary(), null, headless: true);
            Pointer p = Index;
            p.Active = true;
            p.Tip = _deck.Root.TransformPoint(Over(DeckButton.Play, 0.1f));
            _deck.Hover(_pointers, glow);
            Assert.That(glow.Pending, Is.EqualTo(1));
            HoverGlowSpec g = glow.Spec(0);
            Assert.That(g.Strength, Is.EqualTo(1f - 0.012f / 0.06f).Within(1e-4f), "1 - h / .06 with h = y - .088");
            Assert.That(g.Length, Is.EqualTo(0.045f)); Assert.That(g.Width, Is.EqualTo(0.045f));
            Assert.That(Vector3.Distance(g.Position, _deck.Root.TransformPoint(TapeDeck.ButtonX[1], 0.09f, TapeDeck.PegZ)), Is.LessThan(1e-5f));
            Assert.That(Vector3.Dot(g.Normal, _deck.Root.up), Is.GreaterThan(0.9999f));
            glow.Update(0f);
            p.Tip = _deck.Root.TransformPoint(Over(DeckButton.Play, 0.1f, dx: 0.03f));
            _deck.Hover(_pointers, glow);
            Assert.That(glow.Pending, Is.EqualTo(0), "more than 25 mm aside");
            p.Tip = _deck.Root.TransformPoint(Over(DeckButton.Play, 0.077f));
            _deck.Hover(_pointers, glow);
            Assert.That(glow.Pending, Is.EqualTo(0), "more than 1 cm under the peg top");
            p.Tip = _deck.Root.TransformPoint(Over(DeckButton.Play, 0.15f));
            _deck.Hover(_pointers, glow);
            Assert.That(glow.Pending, Is.EqualTo(0), "more than 6 cm over it");
        }

        [Test]
        public void PinchesNearAPegAreATableSurfaceNotAGrab()
        {
            Vector3 W(Vector3 local) => _deck.Root.TransformPoint(local);
            Assert.That(_deck.PlaySurfaceAt(W(Over(DeckButton.Rec, 0.1f))), Is.EqualTo(PlaySurface.Table));
            Assert.That(_deck.PlaySurfaceAt(W(Over(DeckButton.Stop, 0.16f, dx: 0.03f))), Is.EqualTo(PlaySurface.Table), "within 3.5 cm, under 17 cm");
            Assert.That(_deck.PlaySurfaceAt(W(Over(DeckButton.Stop, 0.1f, dx: 0.036f))), Is.EqualTo(PlaySurface.None), "3.6 cm away");
            Assert.That(_deck.PlaySurfaceAt(W(Over(DeckButton.Play, 0.18f))), Is.EqualTo(PlaySurface.None), "too high");
            Assert.That(_deck.PlaySurfaceAt(W(new Vector3(0.1f, 0.08f, 0.06f))), Is.EqualTo(PlaySurface.None), "over the reels: a grab handle");
        }

        // ARCHITECTURE.md sections 0 / 15.7: peg click targets on the play-surface layer, the deck's desktopDown, and the
        // mouse hand's pointers never press, so one click is one press.
        sealed class FakeDesktop : IDeckDesktop
        {
            public bool Click, Modifier; public Ray Ray; public Pointer Mouse;
            public bool TryClick(out Ray ray) { ray = Ray; bool c = Click; Click = false; return c; }
            public bool IsMouse(Pointer p) => p == Mouse;
            public bool ModifierHeld => Modifier;
        }

        static Ray Down(Vector3 world) => new Ray(world + Vector3.up * 0.3f, Vector3.down);

        [Test]
        public void DesktopClicksPressPegsThroughPlaySurfaceColliders()
        {
            Physics.SyncTransforms();
            for (int i = 0; i < 3; i++)
            {
                Collider c = _deck.PegCollider((DeckButton)i);
                Assert.That(c.gameObject.layer, Is.EqualTo(Layers.PlaySurface), "click targets on layer 29 (PlaySurface)");
                Assert.IsFalse(c.isTrigger);
                Assert.That(c.transform.parent, Is.SameAs(_deck.Root), "not under the peg: the target stays while the peg sinks");
            }
            Assert.That(_deck.Root.gameObject.layer, Is.Not.EqualTo(Layers.PlaySurface), "the body is no click target");

            Assert.IsTrue(_deck.DesktopDown(Down(_deck.Root.TransformPoint(TapeDeck.ButtonLocal(DeckButton.Rec)))), "a click on REC");
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Recording));
            Assert.IsFalse(_deck.DesktopDown(Down(_deck.Root.TransformPoint(new Vector3(-0.065f, 0.08f, 0.035f)))), "a click on a reel");
            Assert.That(_deck.Presses, Is.EqualTo(1));

            // Through Tick, as the module wires it: the click is polled in the Instruments phase.
            var desk = new FakeDesktop { Mouse = Index };
            _deck.Desktop = desk;
            desk.Click = true;
            desk.Ray = Down(_deck.Root.TransformPoint(TapeDeck.ButtonLocal(DeckButton.Stop)));
            TipAt(Over(DeckButton.Play, 0.2f));
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Idle), "STOP clicked");
            Assert.That(_deck.Presses, Is.EqualTo(2));

            // The mouse hand's own poke into the PLAY cylinder does nothing (it is a visual stand-in).
            TipAt(Over(DeckButton.Play, 0.2f));
            TipAt(Over(DeckButton.Play, 0.08f));
            TipAt(Over(DeckButton.Play, 0.075f));
            Assert.That(_deck.Presses, Is.EqualTo(2), "mouse pointers never press");
            desk.Mouse = null;
            TipAt(Over(DeckButton.Play, 0.2f));
            TipAt(Over(DeckButton.Play, 0.08f));
            Assert.That(_deck.Presses, Is.EqualTo(3), "a hand pointer does");
        }

        // A pointerdown with Shift or Alt goes to the rig move / turn grab (Layout) and returns before any
        // instrument's desktopDown, so it never presses a peg.
        [Test]
        public void AShiftOrAltClickOnAPegMovesTheTableAndNeverPresses()
        {
            Physics.SyncTransforms();
            var desk = new FakeDesktop { Modifier = true, Ray = Down(_deck.Root.TransformPoint(TapeDeck.ButtonLocal(DeckButton.Rec))) };
            _deck.Desktop = desk;
            desk.Click = true;
            TipAt(Over(DeckButton.Play, 0.2f));
            Assert.That(_deck.Presses, Is.EqualTo(0), "Shift / Alt + click on REC");
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Idle), "no recording started while moving the table");
            desk.Modifier = false;
            desk.Click = true;
            TipAt(Over(DeckButton.Play, 0.2f));
            Assert.That(_deck.Presses, Is.EqualTo(1), "the plain click presses");
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Recording));
        }

        [Test]
        public void ClicksCountOnlyWhileNotPresenting()
        {
            Assert.IsTrue(AppDeckDesktop.ClickAllowed(presenting: false, leftDown: true), "desktop click");
            Assert.IsFalse(AppDeckDesktop.ClickAllowed(presenting: true, leftDown: true), "no clicks while presenting");
            Assert.IsFalse(AppDeckDesktop.ClickAllowed(presenting: false, leftDown: false), "no press this frame");
        }

        [Test]
        public void AHiddenDeckTakesNoClicks()
        {
            Physics.SyncTransforms();
            _deck.Root.gameObject.SetActive(false);
            Assert.IsFalse(_deck.DesktopDown(Down(_deck.Root.TransformPoint(TapeDeck.ButtonLocal(DeckButton.Rec)))));
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Idle));
        }
    }

    public class PlaybackWiringTests
    {
        [Test]
        public void EveryRecordedEventTypeGoesBackToItsInstrument()
        {
            double now = 0;
            var root = new GameObject("WiringRig");
            try
            {
                var piano = new FakePiano(root.transform);
                var drums = new FakeDrums(root.transform);
                var harp = new FakeHarp(root.transform);
                var rec = new Recorder(() => now, null);
                PlaybackWiring.Wire(rec, () => piano, () => drums, () => harp);
                rec.Record();
                rec.Add(TakeEvent.PianoOn(60, 0.8f));
                rec.Add(TakeEvent.Drum("snare", 0.7f, 0.95f, "rim", false));
                rec.Add(TakeEvent.Drum("hat", 0.6f, 0.4f, "bow", true));
                rec.Add(new TakeEvent { type = TakeEventType.Drum, kind = "tom1", vel = 0.5f });   // a strike without a zone
                rec.Add(TakeEvent.DrumChoke("crash"));
                rec.Add(TakeEvent.Hat(true));
                rec.Add(TakeEvent.Harp(7, 0.6f, 0.4f, 1.2f, "across"));
                rec.Add(new TakeEvent { type = TakeEventType.Harp, i = 3, vel = 0.5f });         // a pluck without a stroke
                rec.Add(TakeEvent.HarpDamp(7));
                rec.Add(TakeEvent.PianoOff(60));
                rec.Add(TakeEvent.PianoOn(64, 0.5f));
                now += 1; rec.Stop();
                Assert.IsTrue(rec.Play());
                rec.Update();
                Assert.That(piano.Calls, Is.EqualTo(new[] { "on 60 0.80", "off 60", "on 64 0.50" }));
                Assert.That(drums.Calls, Is.EqualTo(new[] { "hit snare 0.70 0.95 rim False", "hit hat 0.60 0.40 bow True", "hit tom1 0.50 0.30 head False", "choke crash", "hat True True" }),
                    "playbackHit defaults (.3, head); hat via SetHatOpen(open, null, playback: true)");
                Assert.That(harp.Calls, Is.EqualTo(new[] { "pluck 7 0.60 0.40 1.20 across", "pluck 3 0.50 0.35 0.00 through", "damp 7" }),
                    "the whole stroke, else the default pluck");
                now += 1.5; rec.Update();     // past the take's 1 s: Stop, which releases everything
                Assert.That(rec.State, Is.EqualTo(RecorderState.Idle));
                Assert.That(piano.Calls[piano.Calls.Count - 1], Is.EqualTo("releaseAll"), "releaseAll -> PlaybackReleaseAll");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void MissingInstrumentsDropTheirEvents()
        {
            double now = 0;
            var rec = new Recorder(() => now, null);
            PlaybackWiring.Wire(rec, () => null, () => null, () => null);
            rec.Record();
            rec.Add(TakeEvent.PianoOn(60, 0.8f)); rec.Add(TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false)); rec.Add(TakeEvent.Harp(1, 0.5f, 0.3f, 0f, "through"));
            rec.Add(TakeEvent.HarpDamp(1)); rec.Add(TakeEvent.Hat(false)); rec.Add(TakeEvent.DrumChoke("ride")); rec.Add(TakeEvent.PianoOff(60));
            now += 1; rec.Stop();
            Assert.IsTrue(rec.Play());
            Assert.DoesNotThrow(() => { rec.Update(); now += 2; rec.Update(); });
            Assert.That(rec.State, Is.EqualTo(RecorderState.Idle));
        }
    }

    public class LessonMeshTests
    {
        static void AssertOutward(Mesh m, Vector3 centre, string what)
        {
            Vector3[] v = m.vertices;
            int[] t = m.triangles;
            Assert.That(t.Length, Is.GreaterThan(0), what);
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);     // Unity front face: clockwise seen from outside
                Vector3 mid = (a + b + c) / 3f - centre;
                Assert.That(Vector3.Dot(face, mid), Is.GreaterThan(0f), $"{what}: triangle {i / 3} faces inward");
            }
        }

        static void Check(Mesh m, Vector3 min, Vector3 max, Vector3 centre, string what)
        {
            try
            {
                Assert.That(Vector3.Distance(m.bounds.min, min), Is.LessThan(1e-5f), what + " min");
                Assert.That(Vector3.Distance(m.bounds.max, max), Is.LessThan(1e-5f), what + " max");
                AssertOutward(m, centre, what);
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void BlockMeshesHaveTheReferenceShapesAndFaceOutward()
        {
            Check(LessonMeshes.BlockBox(), new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 1f, 0.5f), new Vector3(0f, 0.5f, 0f), "box, pivot on its bottom face");
            Check(LessonMeshes.BlockDisc(), new Vector3(-1f, 0f, -1f), new Vector3(1f, 1f, 1f), new Vector3(0f, 0.5f, 0f), "disc, r 1, h 1, pivot on its bottom");
            Mesh cap = LessonMeshes.BlockCapsule();
            Assert.That(cap.bounds.size.y, Is.EqualTo(0.028f + 2f * 0.006f).Within(1e-5f), "CapsuleGeometry(.006, .028): 4 cm long");
            Assert.That(cap.bounds.extents.x, Is.LessThanOrEqualTo(0.006f + 1e-6f));
            Assert.That(cap.bounds.center.magnitude, Is.LessThan(1e-5f), "centred");
            Check(cap, cap.bounds.min, cap.bounds.max, Vector3.zero, "capsule");
        }

        [Test]
        public void DeckMeshesFaceOutward()
        {
            Check(LessonMeshes.Box(new Vector3(0.26f, 0.07f, 0.2f), Vector3.zero), new Vector3(-0.13f, -0.035f, -0.1f), new Vector3(0.13f, 0.035f, 0.1f), Vector3.zero, "body");
            Check(LessonMeshes.Cylinder(0.014f, 0.015f, 0.014f, 24, Vector3.zero), new Vector3(-0.015f, -0.007f, -0.015f), new Vector3(0.015f, 0.007f, 0.015f), Vector3.zero, "peg (tapered)");
            Mesh s = LessonMeshes.Sphere(0.008f, 16, 12);
            Check(s, new Vector3(-0.008f, -0.008f, -0.008f), new Vector3(0.008f, 0.008f, 0.008f), Vector3.zero, "lamp");
        }
    }
}
