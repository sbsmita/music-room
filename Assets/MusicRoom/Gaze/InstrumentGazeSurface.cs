// An instrument on display as an Interaction SDK surface: its world box (the rig's visible meshes), live only while the
// instrument stands on display (ILayoutService.TryGetDisplayPoint). Set by the Gaze module.
using MusicRoom.Core;
using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace MusicRoom.Gaze
{
    public sealed class InstrumentGazeSurface : GazeSurface
    {
        /// <summary>Forgiveness: the box grows by this much (a head ray is coarse).</summary>
        public const float Margin = 0.04f;

        public InstrumentKind Kind;
        public bool Live;
        public Bounds Box;

        public override bool Cast(in Ray ray, float maxDistance, out SurfaceHit hit, out int sub, out Vector3 focus)
        {
            hit = default;
            sub = 0;
            focus = Box.center;
            if (!Live) return false;
            Bounds b = Box;
            b.Expand(Margin * 2f);
            if (!b.IntersectRay(ray, out float d) || d > maxDistance) return false;
            hit = new SurfaceHit { Point = ray.GetPoint(d), Normal = -ray.direction, Distance = d };
            return true;
        }
    }
}
