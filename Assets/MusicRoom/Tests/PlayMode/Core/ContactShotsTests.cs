// GPU check of 10-upgrade/experience.md E1-E3 (Category Graphics: Tools/unity.sh test-play --gpu). The whole app booted
// as on the headset (simulated: App.IsPresenting, Layout arranges the room around the seat), manual frames, the tour marked
// done. Scripted hands are drawn by the editor's debug hands (joint spheres and bone cylinders in the lit HandSkin
// material), which follow the visual clamp exactly as the headset's hand mesh does. Saves PNGs to
// Builds/Shots/exp-hands (or $MUSICROOM_SHOTS_ROOT/exp-hands):
//   fingers-on-keys / fingers-in-keys-raw       a playing hand pressing C4: the pad rests on the pressed key vs the raw joints
//   shadow-h40 / -h20 / -h10 / -h00             one finger coming down onto D4: the shadow darkens and tightens
//   shadow-on-label (-close, -close-off)        a finger just over C4's printed name: the sticker is shadowed too
//   fingers-on-keys-seat, contact-shadows-seat / -side   the hands over the keys from the seat and from the left
//   stick-on-snare / stick-in-snare-raw / stick-shadow   a held stick struck through the snare: drawn on the head vs raw
//   finger-on-menu / finger-in-menu-raw         a finger 6 mm behind the open menu face: stopped on it vs raw
//   finger-on-deck / finger-in-deck-raw (+ -seat)  a finger pressing the tape deck's REC peg 15 mm deep: on the peg vs raw
// (The draw count of the shadows is DrawBudgetTests.ContactShadowsAreOneDrawWithHandsOverTheKeysAndNoneWithout.)
using System;
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Drums;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using MusicRoom.Piano;
using MusicRoom.Room;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode
{
    [Category("Graphics")]
    public class ContactShotsTests
    {
        const int Width = 1280, Height = 720;
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        PianoInstrument _piano;
        DrumsModule _drums;

        static string OutDir()
        {
            string dir = ShotDirs.FromRoot("exp-hands") ?? ShotDirs.Default("exp-hands");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [UnitySetUp]
        public IEnumerator Boot()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            GC.Collect();
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true);
            _boot.Hands.SetTracked(HandSide.Left, false);
            _boot.Hands.SetTracked(HandSide.Right, false);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            yield return _boot.Start();
            _app = _boot.App;
            foreach (var m in _app.Modules)
            {
                if (m is PianoModule pm) _piano = pm.Piano;
                if (m is DrumsModule dm) _drums = dm;
            }
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            RoomModule room = null;
            foreach (var m in _app.Modules) if (m is RoomModule r) room = r;
            var layout = _app.Get<ILayoutService>();
            t0 = Time.realtimeSinceStartup;
            while ((!layout.FirstArrangeDone || (room != null && (room.TexturesPending > 0 || !room.ProbeRendered))) && Time.realtimeSinceStartup - t0 < 240f)
            {
                _boot.Step(Dt);
                yield return null;
            }
            Assert.IsTrue(layout.FirstArrangeDone, "Layout arranged the room");
            for (int i = 0; i < 4; i++) { _boot.Step(Dt); yield return null; }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _app = null;
            GC.Collect();
        }

        // ------------------------------------------------------------------ helpers

        // Fingers forward over the keys and down, palm down: a playing hand.
        Quaternion Playing(float downDeg) =>
            Quaternion.LookRotation(_piano.transform.TransformDirection(Quaternion.Euler(downDeg, 0f, 0f) * Vector3.forward), _piano.transform.up);

        Vector3 OnPiano(int midi, float y, float back)
        {
            _piano.TryGetKey(midi, out var k);
            return _piano.transform.TransformPoint(new Vector3(k.X, y, back));
        }

        Vector3 PianoPoint(float x, float y, float back) => _piano.transform.TransformPoint(new Vector3(x, y, back));

        HandPose Hand(HandSide side, Vector3 indexTip, float downDeg = 35f, bool pointing = true)
        {
            var p = HandPose.Open(side, Vector3.zero, Playing(downDeg));
            if (pointing) p = Tucked(p.WithPointing(), side);
            return p.WithIndexTipAt(indexTip);
        }

        /// The thumb folded under the index (a pointing hand seen from the side then shows the index finger alone).
        static HandPose Tucked(HandPose p, HandSide side)
        {
            HandPose q = p.Clone();
            Quaternion rot = q.Rotation[(int)HandJoint.Wrist];
            Vector3 wrist = q.Position[(int)HandJoint.Wrist];
            var local = new Vector3[HandFrame.JointCount];
            for (int i = 0; i < HandFrame.JointCount; i++) local[i] = Quaternion.Inverse(rot) * (q.Position[i] - wrist);
            float sx = side == HandSide.Right ? 1f : -1f;   // the right thumb is at -x: fold it toward +x
            HandRestPose.Curl(local, Finger.Thumb, new Vector3(sx * 0.024f, -0.02f, 0.03f));
            for (int i = 0; i < HandFrame.JointCount; i++) q.Position[i] = wrist + rot * local[i];
            return q;
        }

        void Steps(int n) { for (int i = 0; i < n; i++) _boot.Step(Dt); }

        /// Renders one view with the head camera's culling (helper layers off) into a PNG; returns the pixels.
        Texture2D Shoot(string name, Vector3 eye, Vector3 target, float fov)
        {
            var go = new GameObject("ContactShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = XrMode.VrBackground;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.cullingMask = _app.Rig.HeadCamera.cullingMask;
            cam.aspect = Width / (float)Height;
            go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(OutDir(), name + ".png"), tex.EncodeToPNG());
            Object.Destroy(rt);
            Object.Destroy(go);
            return tex;
        }

        /// Mean luma of a small square around a world point as a camera at eye sees it (0..1).
        static float Luma(Texture2D tex, Vector3 eye, Vector3 target, float fov, Vector3 world, int half = 3)
        {
            var go = new GameObject("LumaCam");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = fov;
            cam.aspect = Width / (float)Height;
            cam.nearClipPlane = 0.01f;
            go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
            Vector3 vp = cam.WorldToViewportPoint(world);
            Object.Destroy(go);
            int cx = Mathf.RoundToInt(vp.x * Width), cy = Mathf.RoundToInt(vp.y * Height);
            float sum = 0f;
            int n = 0;
            for (int y = cy - half; y <= cy + half; y++)
                for (int x = cx - half; x <= cx + half; x++)
                {
                    if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                    Color c = tex.GetPixel(x, y);
                    sum += 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                    n++;
                }
            return n > 0 ? sum / n : 0f;
        }

        // ------------------------------------------------------------------ the pictures

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator FingersOnKeysAndTheirContactShadows()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            var fake = _boot.Hands;
            // Press C4 with the index: a stroke from 3 cm above to 15 mm into the key, 4 mm per 90 Hz frame.
            for (float y = 0.03f; y >= -0.015f - 1e-4f; y -= 0.004f)
            {
                fake.SetPose(HandSide.Right, Hand(HandSide.Right, OnPiano(60, y, 0.045f), 40f));
                _boot.Step(1f / 90f);
            }
            Steps(3);
            yield return null;
            float lift = _app.Contacts.Lift(HandSide.Right, Finger.Index);
            Debug.Log($"[ContactShots] C4 depth {_piano.Action.KeyByMidi(60).Depth:F2}, index lift {lift * 1000f:F1} mm, blobs {_app.Contacts.ShadowCount}");
            Assert.That(lift, Is.GreaterThan(0.005f));
            Vector3 c4 = OnPiano(60, 0f, 0.045f);
            // From the left, above the keys to the left of C4 (they are at rest, C4 is down).
            Vector3 eye = c4 + _piano.transform.TransformDirection(new Vector3(-0.22f, 0.10f, -0.05f));
            Vector3 at = c4 + _piano.transform.TransformDirection(new Vector3(0f, 0.012f, 0.0f));
            Texture2D on = Shoot("fingers-on-keys", eye, at, 30f);
            Vector3 drawnTip = _app.HandInput.DebugHands.JointPosition(HandSide.Right, HandJoint.IndexTip);
            float drawnH = Vector3.Dot(_piano.transform.up, drawnTip - _piano.transform.position) - PianoAction.KeyTopY(_piano.Action.KeyByMidi(60), 0.045f);
            Debug.Log($"[ContactShots] drawn index tip {drawnH * 1000f:F1} mm over the pressed key top (radius 7.6)");
            Assert.That(drawnH, Is.GreaterThan(0.006f), "the drawn pad rests on the key top instead of in it");
            _app.Contacts.ClampEnabled = false;
            Steps(1);
            yield return null;
            Shoot("fingers-in-keys-raw", eye, at, 30f);
            _app.Contacts.ClampEnabled = true;
            Steps(1);
            yield return null;
            // The player's own view, zoomed in on the hand.
            Shoot("fingers-on-keys-seat", _app.Head.position, c4, 16f);

            // One finger lowered onto D4 from 4 cm: a faint wide shadow that darkens and tightens. In front of the black
            // keys (back 35 mm), seen from the player's side, up and to the left of the hand.
            fake.SetPose(HandSide.Right, Hand(HandSide.Right, OnPiano(60, 0.08f, 0.05f)));
            Steps(30);
            Vector3 d4 = OnPiano(62, 0f, 0.035f);
            Vector3 side = d4 + _piano.transform.TransformDirection(new Vector3(-0.13f, 0.20f, -0.13f));
            float last = 1f;
            foreach (int mm in new[] { 40, 20, 10, 0 })
            {
                float tipY = mm / 1000f + 0.0076f;   // the pad's underside mm above the key
                fake.SetPose(HandSide.Right, Hand(HandSide.Right, OnPiano(62, tipY, 0.035f), 45f));
                Steps(2);
                yield return null;
                Texture2D t = Shoot($"shadow-h{mm:00}", side, d4, 22f);
                // The key top 7 mm to the left of the finger's foot (the camera's side of it, not covered by the finger).
                float l = Luma(t, side, d4, 22f, d4 - _piano.transform.right * 0.007f + _piano.transform.up * 0.0005f, 2);
                Debug.Log($"[ContactShots] {mm} mm over D4: key luma beside the foot {l:F3}");
                Assert.That(l, Is.LessThanOrEqualTo(last + 0.01f), "the shadow darkens as the finger comes down");
                last = l;
                Object.Destroy(t);
            }
            // A finger just over C4's printed name (the front of the key, where a pianist plays): the sticker is in the shadow too.
            fake.SetPose(HandSide.Right, Hand(HandSide.Right, OnPiano(60, 0.012f, 0.012f), 45f));
            Steps(2);
            yield return null;
            Vector3 label = OnPiano(60, 0f, 0.012f);
            Vector3 labelEye = label + _piano.transform.TransformDirection(new Vector3(-0.12f, 0.20f, -0.12f));
            Shoot("shadow-on-label", labelEye, label, 22f);
            // The sticker 6 mm to the left of the finger's foot, with and without the shadow: the shadow darkens it.
            Vector3 sticker = label - _piano.transform.right * 0.006f + _piano.transform.up * 0.0005f;
            Texture2D withShadow = Shoot("shadow-on-label-close", labelEye, label, 8f);
            float lumaShadow = Luma(withShadow, labelEye, label, 8f, sticker, 3);
            _app.Contacts.ShadowsEnabled = false;
            Steps(1);
            yield return null;
            Texture2D noShadow = Shoot("shadow-on-label-close-off", labelEye, label, 8f);
            float lumaOff = Luma(noShadow, labelEye, label, 8f, sticker, 3);
            _app.Contacts.ShadowsEnabled = true;
            Debug.Log($"[ContactShots] C4 sticker beside the foot: luma {lumaShadow:F3} with the shadow, {lumaOff:F3} without");
            Assert.That(lumaShadow, Is.LessThan(lumaOff * 0.97f), "the printed sticker under the finger is shadowed too");
            Object.Destroy(withShadow);
            Object.Destroy(noShadow);

            // Both hands over the keys from the seat.
            fake.SetPose(HandSide.Right, Hand(HandSide.Right, OnPiano(64, 0.012f, 0.06f), 40f, pointing: false));
            fake.SetPose(HandSide.Left, Hand(HandSide.Left, OnPiano(55, 0.02f, 0.06f), 40f, pointing: false));
            Steps(3);
            yield return null;
            Shoot("contact-shadows-seat", _app.Head.position, OnPiano(60, 0f, 0.06f), 24f);
            Shoot("contact-shadows-side", OnPiano(48, 0.12f, -0.10f), OnPiano(58, 0f, 0.07f), 34f);
            Object.Destroy(on);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AStickStruckThroughTheSnareIsDrawnOnTheHead()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            var fake = _boot.Hands;
            DrumKit kit = _drums.Kit;
            Drumstick s = _drums.Sticks[1];
            Quaternion rot = Quaternion.LookRotation(kit.transform.TransformDirection(new Vector3(0f, -0.6f, 1f).normalized), Vector3.up);
            HandPose PoseAt(Vector3 tip, bool pinched)
            {
                var p = HandPose.Open(HandSide.Right, Vector3.zero, rot).WithPointing();
                if (pinched) p = p.WithPinch(0f);
                return p.WithIndexTipAt(tip);
            }
            Vector3 grab = s.GripWorld + Vector3.up * 0.02f;
            fake.SetPose(HandSide.Right, PoseAt(grab, false));
            Steps(2);
            fake.SetPose(HandSide.Right, PoseAt(grab, true));
            Steps(1);
            Assert.That(_app.Input.Hand(Handedness.Right).Holding, Is.SameAs(s), "picked up");
            HandPose hold = PoseAt(Vector3.zero, true);
            DrumPiece snare = kit.Piece(DrumSpec.SnareId);
            HandPose TipAt(float y)
            {
                DrumMath.StickGrip(hold.Position[(int)HandJoint.Wrist], hold.Rotation[(int)HandJoint.Wrist], hold.Position[(int)HandJoint.ThumbTip],
                    hold.Position[(int)HandJoint.IndexTip], out Vector3 grip, out Vector3 axis);
                DrumMath.StickPose(grip, axis, out _, out _, out Vector3 tip, out _);
                return hold.Translated(snare.Group.TransformPoint(new Vector3(0.10f, y, -0.06f)) - tip);
            }
            // A spot on the head clear of the two resting sticks (they cross its centre), seen from the side at head height.
            Vector3 head = snare.Group.TransformPoint(new Vector3(0.10f, 0f, -0.06f));
            Vector3 toPlayer = _app.Head.position - head;
            toPlayer.y = 0f;
            toPlayer.Normalize();
            Vector3 eye = head + Vector3.Cross(Vector3.up, toPlayer) * 0.38f + toPlayer * 0.08f + snare.Group.up * 0.07f;   // the stick in profile
            // Hovering 2 cm over the head: the drawn tip's soft shadow.
            fake.SetPose(HandSide.Right, TipAt(0.10f));
            Steps(3);
            fake.SetPose(HandSide.Right, TipAt(0.02f));
            Steps(1);
            yield return null;
            Shoot("stick-shadow", eye, head, 30f);
            // Down through the head (2.5 m/s), 4 cm past it: the hit frame and two frames later.
            foreach (float y in new[] { 0.10f, 0.065f, 0.03f }) { fake.SetPose(HandSide.Right, TipAt(y)); Steps(1); }
            fake.SetPose(HandSide.Right, TipAt(-0.005f));
            Steps(1);
            yield return null;
            Debug.Log($"[ContactShots] stick hit frame: phiC {s.PhiC * Mathf.Rad2Deg:F1} deg, phi {s.Phi * Mathf.Rad2Deg:F1} deg");
            Shoot("stick-on-snare-hit", eye, head, 30f);
            fake.SetPose(HandSide.Right, TipAt(-0.04f));
            Steps(1);
            yield return null;
            float drawn = Vector3.Dot(snare.Group.up, s.VisualTipWorld - snare.Group.position);
            Debug.Log($"[ContactShots] 4 cm through: drawn tip {drawn * 1000f:F1} mm over the head, phi {s.Phi * Mathf.Rad2Deg:F1} deg");
            Assert.That(drawn, Is.GreaterThanOrEqualTo(StickSpec.TipRadius - 2e-4f));
            Shoot("stick-on-snare", eye, head, 30f);
            // The same pose as tracked (the Visual child put back on the raw stick for one picture).
            Vector3 vp = s.Visual.localPosition;
            Quaternion vr = s.Visual.localRotation;
            s.Visual.localPosition = Vector3.zero;
            s.Visual.localRotation = Quaternion.identity;
            Shoot("stick-in-snare-raw", eye, head, 30f);
            s.Visual.localPosition = vp;
            s.Visual.localRotation = vr;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AFingerPokingTheMenuStopsOnItsFace()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            var menu = _app.Get<IMenuPanel>();
            menu.Show();
            Steps(20);
            Transform panel = menu.Root;
            Vector3 face = panel.position;   // the face's centre (local z 0 at the middle of the arc)
            Vector3 toPlayer = -panel.forward;
            Quaternion poke = Quaternion.LookRotation((panel.forward - panel.up * 0.3f).normalized, panel.up);
            var pose = Tucked(HandPose.Open(HandSide.Right, Vector3.zero, poke).WithPointing(), HandSide.Right);
            // 6 mm behind the face near its top edge. The finger appears there (it was untracked): the poke never arms in
            // front of the face, so no row fires (a row could take the app into MR or start the tour).
            Vector3 tipAt = face + panel.right * 0.06f + panel.up * 0.095f - toPlayer * 0.006f;
            _boot.Hands.SetTracked(HandSide.Right, false);
            Steps(2);
            _boot.Hands.SetPose(HandSide.Right, pose.WithIndexTipAt(tipAt));
            Steps(3);
            Assert.IsTrue(menu.Shown, "no row fired");
            Assert.IsFalse(_app.Get<IMixedReality>().Active);
            yield return null;
            float lift = _app.Contacts.Lift(HandSide.Right, Finger.Index);
            Debug.Log($"[ContactShots] menu poke: index lift {lift * 1000f:F1} mm");
            Assert.That(lift, Is.GreaterThan(0.01f), "the drawn finger stops on the face");
            Vector3 eye = face + toPlayer * 0.07f + panel.right * 0.30f + panel.up * 0.13f;
            Shoot("finger-on-menu", eye, tipAt, 34f);
            _app.Contacts.ClampEnabled = false;
            Steps(1);
            yield return null;
            Shoot("finger-in-menu-raw", eye, tipAt, 34f);
            _app.Contacts.ClampEnabled = true;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AFingerPressingTheTapeDeckRestsOnItsPeg()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            TapeDeck deck = null;
            foreach (var m in _app.Modules) if (m is LessonsModule lm) deck = lm.Deck;
            Assert.That(deck, Is.Not.Null);
            Transform d = deck.Root;
            Quaternion press = Quaternion.LookRotation(d.TransformDirection(Quaternion.Euler(40f, 0f, 0f) * Vector3.forward), d.up);
            var pose = Tucked(HandPose.Open(HandSide.Right, Vector3.zero, press).WithPointing(), HandSide.Right);
            Vector3 rec = TapeDeck.ButtonLocal(DeckButton.Rec);
            float top = TapeDeck.PegY + TapeDeck.PegH * 0.5f;
            float r = 0.0076f;
            for (float y = 0.03f; y >= r - 0.015f - 1e-4f; y -= 0.004f)
            {
                _boot.Hands.SetPose(HandSide.Right, pose.WithIndexTipAt(d.TransformPoint(new Vector3(rec.x, top + y, rec.z))));
                _boot.Step(1f / 90f);
            }
            Steps(4);
            yield return null;
            Assert.That(_app.Get<IRecorder>().State, Is.EqualTo(RecorderState.Recording), "REC pressed");
            Assert.That(_app.HandInput.DebugHands.VisibleHands, Is.EqualTo(1), "the hand is drawn");
            float lift = _app.Contacts.Lift(HandSide.Right, Finger.Index);
            Vector3 drawnTip = _app.HandInput.DebugHands.JointPosition(HandSide.Right, HandJoint.IndexTip);
            float drawnH = d.InverseTransformPoint(drawnTip).y - deck.PegLocalPosition(DeckButton.Rec).y - TapeDeck.PegH * 0.5f;
            Debug.Log($"[ContactShots] REC peg: index lift {lift * 1000f:F1} mm, drawn tip {drawnH * 1000f:F1} mm over the sunk peg, blobs {_app.Contacts.ShadowCount}");
            Assert.That(lift, Is.GreaterThan(0.005f), "the drawn finger is lifted onto the peg");
            Assert.That(drawnH, Is.GreaterThan(0.005f), "the drawn pad rests on the peg instead of inside the deck");
            Vector3 peg = d.TransformPoint(new Vector3(rec.x, top, rec.z));
            // From the player's left, a little above the deck, and from the seat.
            Vector3 eye = peg + d.TransformDirection(new Vector3(-0.20f, 0.08f, -0.12f));
            Shoot("finger-on-deck", eye, peg, 34f);
            Shoot("finger-on-deck-seat", _app.Head.position, peg, 18f);
            _app.Contacts.ClampEnabled = false;
            Steps(1);
            yield return null;
            Shoot("finger-in-deck-raw", eye, peg, 34f);
            _app.Contacts.ClampEnabled = true;
            _app.Get<IRecorder>().Stop();
        }
    }
}
