// The head gaze: an Interaction SDK RayInteractor whose ray origin is the centre eye (the Gaze module copies the head pose
// into its origin transform each frame), driven by the module (IsRootDriver false, Drive() once per app frame) so it runs in
// the app's frame order and in the tests' manual frames. Its candidate search is the SDK's (every registered RayInteractable,
// nearest hit, tiebreaker score) with look and pinch's rules on top: only GazeSurfaces, only targets whose centre is within
// GazeRules.MaxAngleDeg of the view centre, and the dwell (GazeDwell: a new target after DwellSeconds with a steady head,
// kept through LoseGrace). Overridden only to stay allocation-free: the SDK's own ComputeCandidate allocates a
// RayCandidateProperties every frame (CandidateProperties, read only by interactor groups, which this does not use).
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace MusicRoom.Gaze
{
    public sealed class HeadGazeInteractor : RayInteractor
    {
        public readonly GazeDwell Dwell = new GazeDwell();
        /// <summary>The app frame's dt and head steadiness (set by the module before Drive).</summary>
        public float FrameDt;
        public bool HeadSteady = true;
        /// <summary>What the ray is on this frame before the dwell (0: nothing that counts), and its surface.</summary>
        public long RawKey { get; private set; }
        public long CommittedKey => Dwell.Committed;
        public RayInteractable CommittedTarget { get; private set; }

        /// <summary>What stands between the eye and a target without being one (the instruments at the seat): world boxes
        /// set by the module; a target behind the nearest of them is not seen.</summary>
        public readonly Bounds[] Occluders = new Bounds[16];
        public int OccluderCount;

        int _frame, _steppedFrame = -1;
        RayInteractable _raw;
        SurfaceHit _rawHit;

        /// <summary>A new app frame (the dwell steps once per frame however often the SDK asks for a candidate).</summary>
        public void BeginFrame(float dt, bool headSteady)
        {
            FrameDt = dt;
            HeadSteady = headSteady;
            _frame++;
        }

        public void ResetGaze()
        {
            Dwell.Reset();
            RawKey = 0;
            _raw = null;
            CommittedTarget = null;
        }

        /// <summary>The nearest target the head ray hits now that passes the field-of-view rule (no dwell).</summary>
        public long CastNow(in Ray ray, out RayInteractable target, out SurfaceHit bestHit)
        {
            target = null;
            bestHit = default;
            float best = float.MaxValue;
            long key = 0;
            float blocked = float.MaxValue;
            for (int i = 0; i < OccluderCount; i++)
                if (Occluders[i].IntersectRay(ray, out float od) && od < blocked) blocked = od;
            foreach (RayInteractable it in RayInteractable.Registry.List(this))
            {
                if (!(it.Surface is GazeSurface s) || !s.isActiveAndEnabled) continue;
                if (!s.Cast(ray, MaxRayLength, out SurfaceHit hit, out int sub, out Vector3 focus)) continue;
                if (!GazeRules.InView(ray.origin, ray.direction, focus)) continue;
                if (s.Slot != 0 && hit.Distance > blocked) continue;   // behind an instrument at the seat (the menu floats in front)
                bool equal = Mathf.Abs(hit.Distance - best) < 0.001f;
                if ((!equal && hit.Distance < best) || (equal && target != null && it.TiebreakerScore > target.TiebreakerScore))
                {
                    best = hit.Distance;
                    target = it;
                    key = GazeRules.Key(s.Slot, sub);
                    bestHit = hit;
                }
            }
            return key;
        }

        protected override RayInteractable ComputeCandidate()
        {
            if (_steppedFrame != _frame)
            {
                _steppedFrame = _frame;
                RawKey = CastNow(Ray, out _raw, out _rawHit);
                long c = Dwell.Step(RawKey, FrameDt, HeadSteady);
                if (c == 0) CommittedTarget = null;
                else if (c == RawKey) CommittedTarget = _raw;
                // else: within the grace, the committed target stays
            }
            if (CommittedTarget != null && CommittedTarget == _raw) { CollisionInfo = _rawHit; End = _rawHit.Point; }
            else { CollisionInfo = null; End = Origin + MaxRayLength * Forward; }
            return CommittedTarget;
        }
    }
}
