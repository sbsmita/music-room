// Visual check of the Lessons module (needs a GPU: Tools/unity.sh test-play --gpu). Boots the app desktop-style, then
// renders the seated view (head at the desktop eye) and closer angles into a RenderTexture and saves PNGs:
//   a piano lesson waiting on its first note (block on C4), the same block falling, a drum lesson (blocks falling, then
//   on the hi-hat and kick pad), a harp lesson (capsule coming in, then on string 7), and the tape deck (idle, recording).
// Instruments without geometry of their own (the foundation stubs) get plain reference markers at their API positions
// (keys from TryGetKey, heads from Pieces, strings from StringStart/End, a table slab) so the blocks read in context;
// real instrument modules render themselves and get none. Output: $MUSICROOM_SHOTS or the stage-3 shots folder.
// A second test counts the tape deck's draws alone (profiler "Draw Calls Count", every other renderer off): the deck's
// share of ARCHITECTURE.md's "table + tape deck + switches <= 15" is TapeDeck.DrawBudget (7; Room's side table takes 8).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Lessons
{
    [Category("Graphics")]
    public class LessonsShotTests
    {
        const int W = 1280, H = 720;

        TestBoot _boot;
        GameObject _camGo;
        RenderTexture _rt;
        Texture2D _read;
        Camera _cam;
        readonly List<GameObject> _markers = new List<GameObject>();
        readonly List<Renderer> _switchedOff = new List<Renderer>();
        Camera _headOff;

        static string ShotsDir
        {
            get
            {
                string d = ShotDirs.FromRoot("lessons") ?? Environment.GetEnvironmentVariable("MUSICROOM_SHOTS");
                return string.IsNullOrEmpty(d) ? ShotDirs.Default("lessons") : d;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject g in _markers) if (g != null) Object.Destroy(g);
            _markers.Clear();
            if (_camGo != null) Object.Destroy(_camGo);
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            if (_read != null) Object.Destroy(_read);
            foreach (Renderer r in _switchedOff) if (r != null) r.enabled = true;
            _switchedOff.Clear();
            if (_headOff != null) _headOff.enabled = true;
            _headOff = null;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        // Desktop-style boot (FakeHandSource with no pose: no hands in the pictures; hover glows drawn), first arrange done.
        IEnumerator BootDesktop()
        {
            _boot = new TestBoot(headless: false);
            yield return _boot.Start();
            App app = App.Current;
            float t0 = Time.realtimeSinceStartup;
            while (!(app.Get<ILayoutService>()?.FirstArrangeDone ?? false) && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.IsTrue(app.Get<ILayoutService>().FirstArrangeDone, "desktop first arrange");
        }

        static LessonsModule Module(App app)
        {
            foreach (IAppModule x in app.Modules) if (x is LessonsModule l) return l;
            return null;
        }

        void MakeCamera(int w, int h)
        {
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _read = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            _camGo = new GameObject("LessonsShotCamera");
            _cam = _camGo.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = XrMode.VrBackground;
            _cam.nearClipPlane = 0.02f;
            _cam.farClipPlane = 40f;
            _cam.allowHDR = false;
        }

        [UnityTest, Category("Graphics")]
        public IEnumerator TheTapeDeckAloneDrawsWithinItsBudget()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics): run Tools/unity.sh test-play --gpu");
            yield return BootDesktop();
            App app = App.Current;
            LessonsModule m = Module(app);
            // Only the deck: every other renderer off (the stub room's floor, debug hands, glows), the head camera off, one
            // camera looking at the deck from the seat, recording (REC down, red lamp, reels turning: every part drawn).
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r.enabled && !r.transform.IsChildOf(m.Deck.Root)) { r.enabled = false; _switchedOff.Add(r); }
            _headOff = app.Rig.HeadCamera;
            _headOff.enabled = false;
            MakeCamera(1024, 1024);
            Vector3 deckLook = m.Deck.Root.TransformPoint(0f, 0.07f, 0f);
            Aim(app.Head.position, deckLook, 40f);
            m.Recorder.Record();
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            // The camera's own work (pipeline blits, clears) is measured with the deck hidden and subtracted, so the
            // number is the deck's renderers alone.
            Renderer[] deck = m.Deck.Root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in deck) r.enabled = false;
            for (int i = 0; i < 6; i++) yield return null;
            long baseline = draws.LastValue;
            foreach (Renderer r in deck) r.enabled = true;
            for (int i = 0; i < 6; i++) yield return null;
            long total = draws.LastValue;
            bool valid = draws.Valid;
            draws.Dispose();
            m.Recorder.Stop();
            if (!valid) Assert.Ignore("render counters unavailable in this player");
            long d = total - baseline;
            var names = new System.Text.StringBuilder();
            foreach (Renderer r in deck) names.Append(r.name).Append(r.isVisible ? "" : "(culled)").Append(' ');
            Debug.Log($"[Lessons] tape deck draws={d} (frame {total}, camera alone {baseline}; budget {TapeDeck.DrawBudget}; table + deck + switches <= 15): {names}");
            Assert.That(d, Is.GreaterThan(0), "the deck was in view and drawn");
            Assert.That(d, Is.LessThanOrEqualTo(TapeDeck.DrawBudget));
        }

        [UnityTest, Category("Graphics")]
        public IEnumerator LessonBlocksAndTheTapeDeckFromTheSeat()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics): run Tools/unity.sh test-play --gpu");
            yield return BootDesktop();
            App app = App.Current;
            LessonsModule m = Module(app);
            Assert.IsTrue(m.Blocks.CanDraw, "instanced drawing is available on this GPU");
            Assert.That(m.Blocks.Material.shader.name, Is.EqualTo(LessonsMaterials.BlockShader), "the Lessons/Block template is in Resources (render-setup)");
            AddMarkers(app, m);

            MakeCamera(W, H);
            Directory.CreateDirectory(ShotsDir);
            Vector3 seat = app.Head.position;
            Tutorial tut = m.Tutorial;
            IPianoApi piano = app.Get<IPianoApi>();
            IDrumsApi drums = app.Get<IDrumsApi>();
            IHarpApi harp = app.Get<IHarpApi>();

            // ---- piano: C major scale, waiting on C4 (the keys-blocks shot), then the same block falling ----
            tut.Start("piano-scale");
            Burn(tut);
            tut.Update(5f, null);
            Assert.That(tut.State, Is.EqualTo(TutorialState.Waiting));
            piano.TryGetKey(60, out PianoKeyInfo c4);
            piano.TryGetKey(62, out PianoKeyInfo d4);
            Vector3 keyLook = piano.Root.TransformPoint(0f, 0.05f, 0.05f);
            Vector3 c4Top = piano.Root.TransformPoint(c4.X, 0.012f, Tutorial.KeyZWhite);
            // D4 hovers (due1 - due0) x SPEED over its key while C4 waits: a falling block with no glow under it.
            float d4Lift = (float)((tut.DueOf(1) - tut.DueOf(0)) * Tutorial.Speed);
            Vector3 d4Mid = piano.Root.TransformPoint(d4.X, d4Lift + 0.012f, Tutorial.KeyZWhite);
            yield return null;
            Aim(seat, keyLook, 55f);
            Color withBlock = Shot("lessons-piano-waiting-seat", c4Top);
            Color falling = Shot(null, d4Mid);
            Aim(seat + (c4Top - seat) * 0.55f, c4Top, 40f);
            Shot("lessons-piano-waiting-close", c4Top);
            tut.OnPlay(TakeEvent.PianoOn(64, 0.7f), false);              // a wrong note: the waiting block flashes red for .3 s
            yield return null;
            Color wrong = Shot("lessons-piano-wrong-close", c4Top);
            tut.Stop();
            yield return null;
            Aim(seat, keyLook, 55f);
            Color withoutBlock = Shot(null, c4Top);
            Assert.That(withBlock.b, Is.GreaterThan(withoutBlock.b + 0.2f),
                $"the waiting block (and its purple glow) shows on C4 (with {withBlock}, without {withoutBlock}): the instanced draw reaches a camera render");
            // The "play this next" cue is purple (LessonCue #9e4dff at .75; headset feedback: it was a white-looking cream), the
            // wrong flash a muted coral #c48676. On the ivory key the purple must still read purple: blue clearly over green.
            Assert.That(falling.b - falling.g, Is.GreaterThan(0.15f), $"the falling D4 block is purple, not white ({falling})");
            Assert.That(falling.b, Is.GreaterThan(falling.r), $"violet: blue leads ({falling})");
            Assert.That(withBlock.b - withBlock.g, Is.GreaterThan(0.15f), $"the waiting C4 block and its glow are purple on the white key ({withBlock})");
            Assert.That(Mathf.Abs(wrong.r - withBlock.r) + Mathf.Abs(wrong.g - withBlock.g) + Mathf.Abs(wrong.b - withBlock.b), Is.GreaterThan(0.1f), $"per-instance colour: the C4 block flashes coral (wrong {wrong}, waiting {withBlock})");
            Assert.That(wrong.r - wrong.b, Is.GreaterThan(withBlock.r - withBlock.b), $"redder than the waiting block ({wrong} vs {withBlock})");
            tut.Start("piano-scale");
            Burn(tut);
            tut.Update(1.2f, null);
            yield return null;
            Aim(seat, keyLook, 55f);
            Shot("lessons-piano-falling-seat", c4Top);

            // ---- drums: Basic beat, falling (the kit-blocks shot: 0.8 s in), then waiting on hat + kick pad ----
            Vector3 hat = Vector3.zero;
            foreach (DrumPieceInfo p in drums.Pieces) if (p.Kind == "hat") hat = p.Transform.position;
            tut.Start("drums-beat");
            Burn(tut);
            tut.Update(0.8f, null);
            yield return null;
            Aim(seat, hat, 55f);
            Shot("lessons-drums-falling-seat", hat);
            tut.Update(5f, null);
            Assert.That(tut.State, Is.EqualTo(TutorialState.Waiting));
            yield return null;
            Aim(seat, hat, 55f);
            Shot("lessons-drums-waiting-seat", hat);
            Aim(seat + (hat - seat) * 0.5f, hat, 45f);
            Shot("lessons-drums-waiting-close", hat);

            // ---- harp: C major arpeggio, coming in (the harp-blocks shot: 0.9 s in), then on string 7 ----
            Vector3 harpLook = harp.Root.TransformPoint(-0.3f, 0.9f, 0f);
            Vector3 s7 = harp.Root.TransformPoint(harp.PlayPoint(7));
            tut.Start("harp-arpeggio");
            Burn(tut);
            tut.Update(0.9f, null);
            yield return null;
            Aim(seat, harpLook, 55f);
            Shot("lessons-harp-falling-seat", s7);
            tut.Update(5f, null);
            Assert.That(tut.State, Is.EqualTo(TutorialState.Waiting));
            yield return null;
            Aim(seat, harpLook, 55f);
            Shot("lessons-harp-waiting-seat", s7);
            Aim(seat + (s7 - seat) * 0.6f, s7, 40f);
            Shot("lessons-harp-waiting-close", s7);
            tut.Stop();

            // ---- the tape deck: from the seat, then close while recording (REC down, red lamp, reels turning) ----
            Transform deck = m.Deck.Root;
            Vector3 deckLook = deck.TransformPoint(0f, 0.07f, 0f);
            yield return null;
            Aim(seat, deckLook, 55f);
            Shot("lessons-deck-seat", deckLook);
            m.Recorder.Record();
            // Batchmode frames are a few ms: wait for the REC peg to settle at its latched 5 mm (18/s spring), reels turning.
            float s0 = Time.realtimeSinceStartup;
            while (m.Deck.PegDepth(DeckButton.Rec) < 1f && Time.realtimeSinceStartup - s0 < 2f) yield return null;
            yield return null;
            Assert.That(m.Deck.PegDepth(DeckButton.Rec), Is.EqualTo(1f), "REC latched fully down while recording");
            Vector3 lamp = deck.TransformPoint(TapeDeck.ButtonX[2], 0.08f, TapeDeck.LampRowZ);   // behind STOP (the looper moved it)
            Aim(seat + (deckLook - seat) * 0.55f + Vector3.up * 0.05f, deckLook, 45f);
            Color lampPx = Shot("lessons-deck-recording-close", lamp);
            Assert.That(lampPx.r, Is.GreaterThan(lampPx.g + 0.2f), $"the lamp glows red while recording ({lampPx})");
            m.Recorder.Stop();
            Debug.Log($"[Lessons] shots written to {ShotsDir}");
        }

        static void Burn(Tutorial t) { t.Update(1.1f, null); t.Update(1.1f, null); t.Update(1.1f, null); }

        void Aim(Vector3 from, Vector3 at, float fov)
        {
            _cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
            _cam.fieldOfView = fov;
        }

        // Renders, optionally saves <name>.png, and returns the sRGB pixel where `probe` lands (3 x 3 mean).
        Color Shot(string name, Vector3 probe)
        {
            _cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _read.Apply();
            RenderTexture.active = prev;
            if (name != null) File.WriteAllBytes(Path.Combine(ShotsDir, name + ".png"), _read.EncodeToPNG());
            Vector3 sp = _cam.WorldToScreenPoint(probe);
            int px = Mathf.Clamp(Mathf.RoundToInt(sp.x), 1, W - 2), py = Mathf.Clamp(Mathf.RoundToInt(sp.y), 1, H - 2);
            Color sum = Color.clear;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) sum += _read.GetPixel(px + dx, py + dy);
            return sum / 9f;
        }

        // ---------------- reference markers for instruments that draw nothing themselves ----------------

        static bool Bare(Transform root, Transform except = null)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (except == null || !r.transform.IsChildOf(except)) return false;
            return true;
        }

        GameObject Marker(PrimitiveType type, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            Object.Destroy(g.GetComponent<Collider>());
            g.name = "ShotMarker";
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = rot;
            g.transform.localScale = scale;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            _markers.Add(g);
            return g;
        }

        void AddMarkers(App app, LessonsModule m)
        {
            MaterialLibrary mats = app.Materials;
            Material ivory = mats.Lit(new Color32(0xf4, 0xef, 0xe3, 0xff), 0.7f), ebony = mats.Lit(new Color32(0x0e, 0x0e, 0x10, 0xff), 0.76f);
            Material head = mats.Lit(new Color32(0xe8, 0xe4, 0xda, 0xff), 0.4f), brass = mats.Lit(new Color32(0xcf, 0xa5, 0x4e, 0xff), 0.6f);
            Material red = mats.Lit(new Color32(0xc8, 0x28, 0x28, 0xff), 0.5f), blue = mats.Lit(new Color32(0x24, 0x3c, 0xb4, 0xff), 0.5f), gut = mats.Lit(new Color32(0xe6, 0xe2, 0xd8, 0xff), 0.5f);
            Material walnut = mats.Lit(new Color32(0x44, 0x26, 0x16, 0xff), 0.5f), caseMat = mats.Lit(new Color32(0x13, 0x13, 0x15, 0xff), 0.42f);

            IPianoApi piano = app.Get<IPianoApi>();
            if (piano != null && Bare(piano.Root))
            {
                // White keys (WW .0221 x H .02 x L .15, tops at y 0) and black keys (y -.004..0.012, back .055..0.15).
                for (int midi = 21; midi <= 108; midi++)
                {
                    if (!piano.TryGetKey(midi, out PianoKeyInfo k)) continue;
                    if (!k.Black) Marker(PrimitiveType.Cube, piano.Root, new Vector3(k.X, -0.01f, 0.075f), Quaternion.identity, new Vector3(0.0221f, 0.02f, 0.15f), ivory);
                    else Marker(PrimitiveType.Cube, piano.Root, new Vector3(k.X, 0.004f, 0.1025f), Quaternion.identity, new Vector3(piano.BW, 0.016f, 0.095f), ebony);
                }
                Marker(PrimitiveType.Cube, piano.Root, new Vector3(0f, -0.04f, 0.14f), Quaternion.identity, new Vector3(1.265f, 0.06f, 0.3f), caseMat);
            }
            IDrumsApi drums = app.Get<IDrumsApi>();
            if (drums != null && Bare(drums.Root))
                foreach (DrumPieceInfo p in drums.Pieces)
                {
                    // A head (or cymbal) disc whose top is the strike plane y = 0.
                    Marker(PrimitiveType.Cylinder, p.Transform, new Vector3(0f, -0.004f, 0f), Quaternion.identity, new Vector3(p.Radius * 2f, 0.004f, p.Radius * 2f), p.Type == DrumPieceType.Cymbal ? brass : head);
                }
            IHarpApi harp = app.Get<IHarpApi>();
            if (harp != null && Bare(harp.Root))
                for (int i = 0; i < harp.StringCount; i++)
                {
                    Vector3 a = harp.StringStart(i), b = harp.StringEnd(i);
                    int pc = harp.MidiOf(i) % 12;
                    Marker(PrimitiveType.Cylinder, harp.Root, (a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, b - a), new Vector3(0.002f, (b - a).magnitude * 0.5f, 0.002f), pc == 0 ? red : pc == 5 ? blue : gut);
                }
            Transform table = m.TableRig?.Group;
            if (table != null && Bare(table, m.Deck.Root))
            {
                // rigDecor.sideTable: walnut slab .58 x .025 x .32 at y .5875 on four .025 legs.
                Marker(PrimitiveType.Cube, table, new Vector3(0f, 0.5875f, 0f), Quaternion.identity, new Vector3(0.58f, 0.025f, 0.32f), walnut);
                foreach (float x in new[] { -0.265f, 0.265f })
                    foreach (float z in new[] { -0.135f, 0.135f })
                        Marker(PrimitiveType.Cube, table, new Vector3(x, 0.2875f, z), Quaternion.identity, new Vector3(0.025f, 0.575f, 0.025f), walnut);
            }
        }
    }
}
