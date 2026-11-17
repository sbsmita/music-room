// The Interaction SDK surfaces (ISurface) the head gaze ray is cast against. Each RayInteractable of look and pinch has one:
// MenuGazeSurface (the shown menu panel's rows, through Core's IGazeMenu) and InstrumentGazeSurface (an instrument on display,
// its world box). Besides the SDK's Raycast they give the sub-target (the menu row) and the point the field-of-view rule
// measures (the row's or the instrument's centre). No physics colliders are added, so nothing else in the app can hit them.
using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace MusicRoom.Gaze
{
    public abstract class GazeSurface : MonoBehaviour, ISurface
    {
        /// <summary>The target slot: 0 the menu, 1 + kind an instrument.</summary>
        public int Slot;

        public Transform Transform => transform;

        /// <summary>The ray hits a target: the SDK hit, the sub key (a menu row; 0 otherwise) and the focus point.</summary>
        public abstract bool Cast(in Ray ray, float maxDistance, out SurfaceHit hit, out int sub, out Vector3 focus);

        public bool Raycast(in Ray ray, out SurfaceHit hit, float maxDistance = 0)
            => Cast(ray, maxDistance > 0f ? maxDistance : GazeRules.MaxRayLength, out hit, out _, out _);

        public bool ClosestSurfacePoint(in Vector3 point, out SurfaceHit hit, float maxDistance = 0)
        {
            hit = default;
            return false;
        }
    }
}
