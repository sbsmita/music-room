// The per-frame input pass (spec 05 section 1.3): fills hand and external pointers, keeps the
// pinch / grasp timers and derives pointer speed over the learned sample interval. Pinch and grasp themselves come
// from HandInput (15/25 mm pinch rule, 0.105/0.13 m grasp with hysteresis).
using System;
using System.Collections.Generic;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Core
{
    public sealed class PointerInput
    {
        public readonly HandState[] Hands = { new HandState(Handedness.Left), new HandState(Handedness.Right) };

        readonly HandInput _input;
        readonly List<Pointer> _all = new List<Pointer>(16);
        readonly List<Pointer> _hand = new List<Pointer>(10);
        readonly List<Pointer> _external = new List<Pointer>(4);

        /// <summary>Hand + external (stick) pointers; the drums get these.</summary>
        public IReadOnlyList<Pointer> AllPointers => _all;
        /// <summary>Fingertip pointers only; every other instrument gets these.</summary>
        public IReadOnlyList<Pointer> HandPointers => _hand;
        public IReadOnlyList<Pointer> ExternalPointers => _external;
        /// <summary>True when the pointers come from real hands (headset), false for the mouse / scripted hands.</summary>
        public bool IsHeadset => _input != null && _input.IsHeadset;
        /// <summary>True when the hands come from the desktop MouseHandSource (their pointers never play: HandState.IsMouse).</summary>
        public bool MouseHand => _input != null && _input.Source == _input.Mouse;
        public HandInput Source => _input;

        /// <summary>Pointer.Slot capacity: 10 fingertips + up to 22 tool pointers (size per-pointer tables with it).</summary>
        public const int MaxSlots = 32;
        int _nextSlot;
        /// <summary>Counts external registrations (the per-instrument pointer lists rebuild when it changes, never per frame).</summary>
        public int Version { get; private set; }
        readonly Dictionary<int, List<Pointer>> _byMask = new Dictionary<int, List<Pointer>>();
        int _maskVersion = -1;

        public PointerInput(HandInput input)
        {
            _input = input;
            foreach (var h in Hands) foreach (var p in h.Pointers) { p.Slot = _nextSlot++; _hand.Add(p); _all.Add(p); }
        }

        /// <summary>The hand pointers plus the external pointers whose Tool is in <paramref name="mask"/> (platform.md 4.6), in
        /// AllPointers order. Cached per mask; rebuilt only when an external pointer registers or leaves. ToolMask.None gives
        /// HandPointers.</summary>
        public IReadOnlyList<Pointer> PointersFor(ToolMask mask)
        {
            if (mask == ToolMask.None) return _hand;
            if (_maskVersion != Version)
            {
                _maskVersion = Version;
                foreach (var kv in _byMask) Fill(kv.Value, (ToolMask)kv.Key);   // the lists' contents change, the dictionary does not
            }
            if (!_byMask.TryGetValue((int)mask, out var list))
            {
                list = new List<Pointer>(16);
                Fill(list, mask);
                _byMask[(int)mask] = list;
            }
            return list;
        }

        void Fill(List<Pointer> list, ToolMask mask)
        {
            list.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                Pointer p = _all[i];
                if (!p.External || ToolMasks.Has(mask, p.Tool)) list.Add(p);
            }
        }

        public HandState Hand(Handedness side) => Hands[(int)side];
        public HandFrame Frame(HandState h) => _input.Get((HandSide)h.Index);

        /// <summary>Sticks register their tip and shoulder pointers here (and unregister when the kit leaves).</summary>
        public void RegisterExternal(Pointer p)
        {
            if (p == null || !p.External || _external.Contains(p)) return;
            if (p.Slot < 0) p.Slot = _nextSlot < MaxSlots ? _nextSlot++ : MaxSlots - 1;
            _external.Add(p);
            _all.Add(p);
            Version++;
        }

        public void UnregisterExternal(Pointer p)
        {
            if (_external.Remove(p)) { _all.Remove(p); Version++; }
        }

        /// <summary>The per-frame pass. nowS = Clock.Now (frame-constant seconds).</summary>
        public void Update(float dt, double nowS)
        {
            bool mouse = MouseHand;
            for (int i = 0; i < 2; i++)
            {
                HandState h = Hands[i];
                HandFrame f = _input.Get((HandSide)i);
                h.IsMouse = mouse;
                h.Connected = f.isTracked;
                h.Pinching = _input.pinching[i];
                h.PinchStart = _input.pinchStart[i];
                h.PinchEnd = _input.pinchEnd[i];
                h.Grasping = _input.grasping[i];
                h.GraspStart = _input.graspStart[i];
                h.GraspEnd = _input.graspEnd[i];
                h.PinchFor = h.Pinching ? h.PinchFor + dt : 0f;
                h.GrabPinching = _input.grabPinching[i];
                h.GrabPinchStart = _input.grabPinchStart[i];
                h.GrabPinchEnd = _input.grabPinchEnd[i];
                h.GrabPinchFor = h.GrabPinching ? h.GrabPinchFor + dt : 0f;
                h.MetaPinching = _input.metaPinching[i];
                h.PinchStrength = f.isTracked ? f.pinchStrength[(int)Finger.Index] : 0f;
            }
            for (int i = 0; i < _all.Count; i++)
            {
                Pointer p = _all[i];
                p.WasActive = p.Active;
                p.Prev = p.Tip;
                p.Active = false;
                if (p.External)
                {
                    if (!p.Down) continue;
                    p.Tip = p.Next;
                    p.Active = true;
                    p.TrackerVel = Vector3.zero;
                }
                else
                {
                    var h = (HandState)p.Owner;
                    if (!h.Connected || h.Holding != null) continue; // a hand holding a stick is muted
                    HandFrame f = _input.Get((HandSide)h.Index);
                    p.Tip = f.position[p.Joint];
                    p.TrackerVel = f.tipVelocity[p.Finger];
                    p.Active = true;
                }
                if (p.WasActive && (p.Tip - p.Prev).sqrMagnitude > 1e-10f)
                {
                    double gap = Math.Max(nowS - p.SampleT, 0.004);
                    if (gap < 0.2) p.SampleDt = p.SampleDt > 0f ? p.SampleDt + 0.25f * ((float)gap - p.SampleDt) : (float)gap;
                    p.SampleT = nowS;
                    p.Vel = (p.Tip - p.Prev) / Mathf.Max(p.SampleDt > 0f ? p.SampleDt : dt, 1e-3f);
                }
                else
                {
                    if (!p.WasActive) { p.SampleT = nowS; p.Prev = p.Tip; }
                    p.Vel = Vector3.zero;
                }
                p.Speed = p.Vel.magnitude;
            }
        }

        /// <summary>The one-frame edge flags are cleared at the end of the frame.</summary>
        public void EndFrame()
        {
            for (int i = 0; i < 2; i++)
            {
                HandState h = Hands[i];
                h.PinchStart = h.PinchEnd = h.GraspStart = h.GraspEnd = false;
                h.GrabPinchStart = h.GrabPinchEnd = false;
            }
        }

        /// <summary>The OS system gesture on Quest: the palm turned back toward the headset (with a pinch it opens the
        /// system menu). Quest flags SystemGestureInProgress for any open palm facing the headset; this also catches the
        /// moment before the flag rises: the palm within 50 degrees of the eyes, closer than 0.8 m, and turned back toward
        /// the player (a palm-down hand held above the head faces the eyes too, but is not the gesture). A pinch that starts
        /// like this is never a grab.</summary>
        public bool SystemGestureLike(HandState h, Vector3 head, Vector3 headForward)
        {
            HandFrame f = Frame(h);
            if (!f.isTracked) return false;
            if (f.systemGesture) return true;
            if (!HandMath.Palm(f, out Vector3 c, out Vector3 n)) return false;
            Vector3 back = -headForward;
            back.y = 0f;
            if (back.sqrMagnitude < 1e-6f || Vector3.Dot(n, back.normalized) < 0.5f) return false;
            return HandMath.PalmFacesPoint(c, n, head, ArrangeConstants.SystemPalmCos, ArrangeConstants.SystemPalmMaxDistance);
        }

        /// <summary>After a snap turn, re-arrangement or rig teleport, so the jump never reads as a stroke.</summary>
        public void ResetMotion()
        {
            for (int i = 0; i < _all.Count; i++) _all[i].Deactivate();
        }

        /// <summary>The world index tip, even while the hand holds a stick.</summary>
        public bool TryIndexTip(HandState h, out Vector3 tip)
        {
            HandFrame f = Frame(h);
            tip = f.position[(int)HandJoint.IndexTip];
            return f.isTracked;
        }

        /// <summary>HandMath.Palm: centre of wrist + index proximal + little proximal, normal out of the palm.</summary>
        public bool TryPalm(HandState h, out Vector3 center, out Vector3 normal)
        {
            HandFrame f = Frame(h);
            if (!f.isTracked) { center = normal = Vector3.zero; return false; }
            return HandMath.Palm(f, out center, out normal);
        }

        /// <summary>Mean wrist distance of the middle/ring/little tips; NaN when untracked.</summary>
        public float GraspAmount(HandState h)
        {
            HandFrame f = Frame(h);
            return f.isTracked ? HandMath.GraspAmount(f) : float.NaN;
        }

        /// <summary>The pinch point: pointer 0's tip if active; else the raw index tip while holding a stick; else none.</summary>
        public bool TryPinchPoint(HandState h, out Vector3 point)
        {
            Pointer p = h.Pointers[0];
            if (p.Active) { point = p.Tip; return true; }
            if (h.Holding != null) return TryIndexTip(h, out point);
            point = Vector3.zero;
            return false;
        }

        /// <summary>Wrist yaw in degrees (for rig turning while held).</summary>
        public float WristYaw(HandState h) => HandMath.YawDegrees(Frame(h));

        /// <summary>Is playing: played within the last second.</summary>
        public static bool IsPlaying(IPointerOwner owner, double nowMs, double guardMs = 1000) => nowMs - owner.LastPlayMs < guardMs;
    }
}
