// The menu panel's rows as an Interaction SDK surface (see GazeSurface).
using MusicRoom.Core;
using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace MusicRoom.Gaze
{
    public sealed class MenuGazeSurface : GazeSurface
    {
        public IGazeMenu Menu;

        public override bool Cast(in Ray ray, float maxDistance, out SurfaceHit hit, out int sub, out Vector3 focus)
        {
            hit = default;
            sub = 0;
            focus = default;
            if (Menu == null) return false;
            if (!Menu.GazeCast(ray, maxDistance, out float d, out Vector3 p, out Vector3 n, out int row, out Vector3 centre)) return false;
            hit = new SurfaceHit { Point = p, Normal = n, Distance = d };
            sub = row;
            focus = centre;
            return true;
        }
    }
}
