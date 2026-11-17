// The pair of drumsticks (Docs/05-instruments.md section 4): they rest in an X across the snare; a pinch (or a closing
// fist once the hand stopped playing) near a shaft picks one up; it sits in the hand at the pinch point, continues the
// line of the hand with a 12.6 degree droop and follows the hand every frame BEFORE the pointers are read, so its tip
// and shoulder are external pointers the drums measure like fingertips. It is let go only after the hand stayed open
// for 0.25 s (a relaxed grip keeps it), rides out 1.5 s of tracking loss, and settles back onto the snare in 0.4 s.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Drums
{
    /// <summary>Hand joints for a hand state (the app's PointerInput.Frame, or a test fixture).</summary>
    public interface IHandFrames
    {
        HandFrame Frame(HandState hand);
    }

    /// <summary>One stick: a hand tool (HandState.Holding) and the owner of its two pointers, so a hit stamps the 45 ms
    /// window on both and a stroke that crosses a cymbal with tip and shoulder counts once; their arm states stay
    /// separate.</summary>
    public sealed partial class Drumstick : IHandTool, IPointerOwner
    {
        public readonly int Index;
        public readonly Transform Root;
        public readonly Pointer TipPointer;
        public readonly Pointer ShoulderPointer;
        public readonly Renderer[] Renderers;
        readonly Pointer[] _pointers;

        public HandState Hand;
        public Vector3 RestPos;       // kit-local
        public Quaternion RestRot;
        public float Settle = 1f;     // 0 -> 1 over 0.4 s after a release
        public float OpenFor, LostFor;
        public Vector3 Axis, Grip;
        public bool Smoothed;

        public Drumstick(int index, Transform root, Renderer[] renderers)
        {
            Index = index;
            Root = root;
            Renderers = renderers;
            TipPointer = Pointer.CreateExternal("stick" + index, this, stick: true, shoulder: false);
            ShoulderPointer = Pointer.CreateExternal("stick" + index + "-shoulder", this, stick: true, shoulder: true);
            _pointers = new[] { TipPointer, ShoulderPointer };
        }

        public bool Held => Hand != null;
        public IReadOnlyList<Pointer> Pointers => _pointers;
        public double LastPlayMs { get; set; } = double.NegativeInfinity;

        /// <summary>World position of the grip point (13 cm from the butt), for the hover glow.</summary>
        public Vector3 GripWorld => Root.TransformPoint(new Vector3(0f, StickSpec.GripFromButt, 0f));
        public Vector3 ButtWorld => Root.position;
        public Vector3 TipWorld => Root.TransformPoint(new Vector3(0f, StickSpec.Length, 0f));
    }

    public sealed partial class DrumSticks
    {
        public enum GrabResult { None, Near, Grabbed }

        readonly DrumKit _kit;
        readonly IHandFrames _frames;
        readonly Transform _scene;
        readonly Drumstick[] _sticks = new Drumstick[2];
        readonly List<Mesh> _meshes = new List<Mesh>(2);
        readonly List<Renderer> _renderers = new List<Renderer>(4);
        readonly float[] _hoverBest = new float[2];
        readonly int[] _hoverOrder = new int[2];

        /// <summary>Blend toward the tracked pose each frame (1 = snap): snapping keeps the stick's motion
        /// in step with the hand's tracking samples, which is what the drums measure speed against.</summary>
        public float Smoothing = 1f;
        /// <summary>Bumped on every pick-up / release (the kit's zone cache watches it).</summary>
        public int Version { get; private set; }
        public int Count => _sticks.Length;
        public Drumstick this[int i] => _sticks[i];
        public IReadOnlyList<Renderer> Renderers => _renderers;
        public IReadOnlyList<Mesh> Meshes => _meshes;

        public bool AnyHeld
        {
            get { for (int i = 0; i < _sticks.Length; i++) if (_sticks[i].Held) return true; return false; }
        }

        /// <summary><paramref name="scene"/> is where a held stick lives (attached keeping its world pose): App.WorldRoot.</summary>
        public DrumSticks(DrumKit kit, IHandFrames frames, Transform scene, DrumMaterials mats)
        {
            _kit = kit;
            _frames = frames;
            _scene = scene;
            var mk = new MeshKit();
            // Shaft (radius .0075 at the butt, .0045 at the tip) and the tip ball share the wood material: one mesh.
            mk.At(new Vector3(0f, StickSpec.Length * 0.5f, 0f), Quaternion.identity)
                .Cylinder(StickSpec.ShaftRadiusTip, StickSpec.ShaftRadiusButt, StickSpec.Length, StickSpec.ShaftSegments, false);
            mk.At(new Vector3(0f, StickSpec.Length, 0f), Quaternion.identity).Sphere(StickSpec.TipRadius, StickSpec.TipWidthSegments, StickSpec.TipHeightSegments);
            Mesh wood = mk.ToMesh("stick-wood");
            mk.Clear();
            mk.At(new Vector3(0f, StickSpec.TapeCentre, 0f), Quaternion.identity)
                .Cylinder(StickSpec.TapeRadius, StickSpec.TapeRadius, StickSpec.TapeLength, StickSpec.ShaftSegments, false);
            Mesh tape = mk.ToMesh("stick-tape");
            _meshes.Add(wood);
            _meshes.Add(tape);

            PieceSpec snare = DrumSpec.Pieces[DrumSpec.SnareId];
            for (int i = 0; i < 2; i++)
            {
                var root = new GameObject("stick" + i).transform;
                root.SetParent(kit.transform, false);
                // E2 (10-upgrade/experience.md 4): the meshes hang under a "Visual" child that alone pivots about the grip
                // (UpdateVisuals); Root and the pointers keep the raw pose the hit test reads.
                var visual = new GameObject("Visual").transform;
                visual.SetParent(root, false);
                var shaft = MakeRenderer(visual, "wood", wood, mats.StickWood, true);
                var band = MakeRenderer(visual, "tape", tape, mats.StickTape, false);
                var stick = new Drumstick(i, root, new[] { shaft, band }) { Visual = visual };
                RestPose(snare, i, out stick.RestPos, out stick.RestRot);
                root.localPosition = stick.RestPos;
                root.localRotation = stick.RestRot;
                _sticks[i] = stick;
                _renderers.Add(shaft);
                _renderers.Add(band);
            }
        }

        static Renderer MakeRenderer(Transform parent, string name, Mesh mesh, Material mat, bool castShadow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = castShadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        /// <summary>Rest pose: two chords across the snare head through its centre, butts just over the near rim:
        /// snare-local (-+.068, .009, -.188), source-frame Euler(-pi/2, 0, -+.35) = AngleAxis(90, right) * AngleAxis(-+20.05,
        /// forward), carried into the kit frame.</summary>
        public static void RestPose(in PieceSpec snare, int index, out Vector3 pos, out Quaternion rot)
        {
            float side = index == 0 ? -1f : 1f;
            Vector3 local = new Vector3(side * StickSpec.RestSnareX, StickSpec.RestSnareY, StickSpec.RestSnareZ);
            Quaternion localRot = Quaternion.AngleAxis(90f, Vector3.right) * Quaternion.AngleAxis(side * StickSpec.RestRollRad * Mathf.Rad2Deg, Vector3.forward);
            Quaternion snareRot = snare.LocalRotation;
            pos = snare.Pos + snareRot * local;
            rot = snareRot * localRot;
        }

        public void DestroyGeneratedAssets(bool immediate)
        {
            foreach (var m in _meshes) if (m != null) { if (immediate) Object.DestroyImmediate(m); else Object.Destroy(m); }
            _meshes.Clear();
        }

        /// <summary>Registers / unregisters the four external pointers (only the drums see them: AcceptsStickPointers).</summary>
        public void Register(PointerInput input)
        {
            for (int i = 0; i < _sticks.Length; i++) { input.RegisterExternal(_sticks[i].TipPointer); input.RegisterExternal(_sticks[i].ShoulderPointer); }
        }

        public void Unregister(PointerInput input)
        {
            for (int i = 0; i < _sticks.Length; i++) { input.UnregisterExternal(_sticks[i].TipPointer); input.UnregisterExternal(_sticks[i].ShoulderPointer); }
        }

        // =============================== pick-up ===============================

        /// <summary>Pinch point: the thumb-index midpoint when both tips are tracked, else the index tip pointer.</summary>
        public Vector3 PinchPoint(HandState h, Vector3 fallback)
        {
            HandFrame f = _frames?.Frame(h);
            if (f != null && f.isTracked) return (f.position[(int)HandJoint.ThumbTip] + f.position[(int)HandJoint.IndexTip]) * 0.5f;
            return fallback;
        }

        public float ShaftDistance(Drumstick s, Vector3 point) => DrumMath.SegmentDistance(s.ButtWorld, s.TipWorld, point);

        /// <summary>The nearest stick not in a hand, with its distance (null / +inf when none).</summary>
        public Drumstick NearestFree(Vector3 point, out float distance)
        {
            Drumstick best = null;
            distance = float.PositiveInfinity;
            for (int i = 0; i < _sticks.Length; i++)
            {
                Drumstick s = _sticks[i];
                if (s.Held) continue;
                float d = ShaftDistance(s, point);
                if (d < distance) { distance = d; best = s; }
            }
            return best;
        }

        /// <summary>Pick-up: Grabbed within .12 of a shaft, Near (the pinch is consumed rather than
        /// grabbing the kit) within .24, else None. No kit in the room: no sticks to pick up.</summary>
        public GrabResult TryGrab(HandState h, Vector3 tip)
        {
            if (h == null || h.Holding != null) return GrabResult.None;
            if (_kit == null || !_kit.IsPresent) return GrabResult.None;
            Vector3 point = PinchPoint(h, tip);
            Drumstick stick = NearestFree(point, out float distance);
            if (stick == null) return GrabResult.None;
            if (distance > StickSpec.GrabRadius) return distance < StickSpec.DeadZone ? GrabResult.Near : GrabResult.None;
            stick.Hand = h;
            h.Holding = stick;
            stick.Root.SetParent(_scene, true);
            stick.OpenFor = 0f;
            stick.LostFor = 0f;
            stick.Smoothed = false;
            stick.TipPointer.Down = true;
            stick.ShoulderPointer.Down = true;
            Version++;
            return GrabResult.Grabbed;
        }

        /// <summary>Release: back to the kit (keeping the world pose) and settle onto the snare.</summary>
        public void Release(Drumstick s)
        {
            if (s == null || s.Hand == null) return;
            if (ReferenceEquals(s.Hand.Holding, s)) s.Hand.Holding = null;
            s.Hand = null;
            s.TipPointer.Down = s.TipPointer.Active = false;
            s.ShoulderPointer.Down = s.ShoulderPointer.Active = false;
            if (_kit != null) s.Root.SetParent(_kit.transform, true);
            s.Settle = 0f;
            Version++;
        }

        /// <summary>Drop every held stick at once (the kit left the layout, the session / headset ended).</summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < _sticks.Length; i++) if (_sticks[i].Held) Release(_sticks[i]);
        }

        // =============================== per frame ===============================

        /// <summary>Pose update, in the StickPoses phase BEFORE PointerInput.Update: held sticks follow
        /// their hands and feed their tip and shoulder as pointers; released sticks settle back onto the snare.</summary>
        public void UpdatePoses(float dt)
        {
            for (int i = 0; i < _sticks.Length; i++)
            {
                Drumstick s = _sticks[i];
                if (s.Held)
                {
                    HandFrame f = _frames?.Frame(s.Hand);
                    // Tracking lost: freeze in place, silence the pointers, snap (not blend) when tracking returns, and
                    // drop the stick only after a grace period.
                    if (f == null || !f.isTracked)
                    {
                        s.LostFor += dt;
                        s.Smoothed = false;
                        s.TipPointer.Down = s.ShoulderPointer.Down = false;
                        if (s.LostFor > StickSpec.LostGrace) Release(s);
                        continue;
                    }
                    s.LostFor = 0f;
                    s.TipPointer.Down = s.ShoulderPointer.Down = true;
                    DrumMath.StickGrip(f.position[(int)HandJoint.Wrist], f.rotation[(int)HandJoint.Wrist],
                        f.position[(int)HandJoint.ThumbTip], f.position[(int)HandJoint.IndexTip], out Vector3 grip, out Vector3 axis);
                    if (s.Smoothed && Smoothing < 1f)
                    {
                        s.Axis = Vector3.Lerp(s.Axis, axis, Smoothing).normalized;
                        s.Grip = Vector3.Lerp(s.Grip, grip, Smoothing);
                    }
                    else
                    {
                        s.Axis = axis;
                        s.Grip = grip;
                        s.Smoothed = true;
                    }
                    DrumMath.StickPose(s.Grip, s.Axis, out Vector3 butt, out Quaternion rot, out Vector3 tip, out Vector3 shoulder);
                    s.Root.SetPositionAndRotation(butt, rot);
                    s.TipPointer.Next = tip;
                    s.ShoulderPointer.Next = shoulder;
                    continue;
                }
                if (s.Settle < 1f)
                {
                    s.Settle = Mathf.Min(1f, s.Settle + dt * StickSpec.SettleRate);
                    float k = DrumMath.SettleEase(s.Settle);
                    s.Root.localPosition = Vector3.Lerp(s.Root.localPosition, s.RestPos, k);
                    s.Root.localRotation = Quaternion.Slerp(s.Root.localRotation, s.RestRot, k);
                }
            }
        }

        /// <summary>Releases, after the grab pass: open means the pinch has parted (and Meta's pinch
        /// too) AND the fist has opened; either grip keeps the stick. Untracked joints say nothing (the tracking-loss grace handles it).</summary>
        public void UpdateReleases(float dt)
        {
            for (int i = 0; i < _sticks.Length; i++)
            {
                Drumstick s = _sticks[i];
                HandState h = s.Hand;
                if (h == null || !h.Connected) continue;
                HandFrame f = _frames?.Frame(h);
                if (f == null || !f.isTracked) continue;
                float gap = Vector3.Distance(f.position[(int)HandJoint.ThumbTip], f.position[(int)HandJoint.IndexTip]);
                // Meta's own pinch (HandState.GrabPinching) holds it too, so a flickering joint gap never drops a stick.
                s.OpenFor = gap > StickSpec.OpenDistance && !h.Grasping && !h.GrabPinching ? s.OpenFor + dt : 0f;
                if (s.OpenFor > StickSpec.OpenDwell) Release(s);
            }
        }

        /// <summary>Hover: a glow on the stick each fingertip would actually pick up (the nearest free
        /// one within .14): at the grip + 1.4 cm up, along the stick, .12 x .035, strength 1 - d/.14.</summary>
        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow)
        {
            if (glow == null || pointers == null || _kit == null || !_kit.IsPresent) return;
            int n = 0;
            _hoverBest[0] = _hoverBest[1] = float.PositiveInfinity;
            for (int i = 0; i < pointers.Count; i++)
            {
                Pointer p = pointers[i];
                // The desktop mouse hand cannot pick a stick up (Layout's grab runs only while presenting): no glow
                // promising it (ARCHITECTURE.md section 0).
                if (!p.Active || p.Stick || p.FromMouse) continue;
                Drumstick s = NearestFree(p.Tip, out float d);
                if (s == null || d > StickSpec.HoverRadius) continue;
                if (float.IsPositiveInfinity(_hoverBest[s.Index])) _hoverOrder[n++] = s.Index;   // Map insertion order
                if (d < _hoverBest[s.Index]) _hoverBest[s.Index] = d;
            }
            for (int k = 0; k < n; k++)
            {
                Drumstick s = _sticks[_hoverOrder[k]];
                var spec = HoverGlowSpec.At(s.GripWorld + Vector3.up * StickSpec.HoverLift, Vector3.up, StickSpec.HoverLength, StickSpec.HoverWidth,
                    1f - _hoverBest[s.Index] / StickSpec.HoverRadius);
                spec.Axis = s.Root.up;
                glow.Add(spec);
            }
        }
    }
}
