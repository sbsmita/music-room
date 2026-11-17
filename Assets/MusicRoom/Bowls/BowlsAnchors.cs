// The bowls' anchors for Sunny's demos and the lessons (IGuideAnchors; every id is in the descriptor's Anchors):
//   bowl.<big|middle|small>.rim   the rim's centre, up = (0, R, 0) in the rig frame (|up| = the rim radius, for the circle
//                                 demos)
//   bowl.<big|middle|small>.near  the rim point facing the head, up = outward (horizontal, unit): a tap from the side
//   bowl.@rubbed.rim              the bowl rubbed now (else the last rubbed, else the big one)
//   bowl.@ringing.rim             the loudest bowl (the big one when all are quiet)
//   puja.grip                     the stick's grip point where it is now (on the stand or in the hand), up = world up
//   bowls.lesson.<0|1|2>          bowl i's rim centre, up = (0, R, 0): where a lesson ring lands
//   bowls.lesson.@target          the current lesson target's bowl (the big one outside a lesson)
// Allocation-free (string compares only).
using System;
using UnityEngine;

namespace MusicRoom.Bowls
{
    public static class BowlsAnchors
    {
        static readonly string[] Rim = { "bowl.big.rim", "bowl.middle.rim", "bowl.small.rim" };
        static readonly string[] Near = { "bowl.big.near", "bowl.middle.near", "bowl.small.near" };
        static readonly string[] Lesson = { "bowls.lesson.0", "bowls.lesson.1", "bowls.lesson.2" };

        public static Vector3 RimLocal(int i) => new Vector3(BowlsLayout.BowlXZ[i].x, BowlsLayout.RimY(i), BowlsLayout.BowlXZ[i].y);

        public static bool TryGet(string id, Transform rig, Vector3 head, Func<string, int> resolve, BowlsPuja puja, out Vector3 world, out Vector3 up)
        {
            world = default; up = Vector3.up;
            if (string.IsNullOrEmpty(id) || rig == null) return false;
            if (id == "puja.grip")
            {
                world = puja != null ? puja.GripWorld : rig.TransformPoint(BowlsGeometry.PujaRestMatrix().MultiplyPoint3x4(BowlsGeometry.PujaGripLocal));
                return true;
            }
            int bowl = -1; bool near = false;
            for (int i = 0; i < 3 && bowl < 0; i++)
            {
                if (id == Rim[i] || id == Lesson[i]) bowl = i;
                else if (id == Near[i]) { bowl = i; near = true; }
            }
            if (bowl < 0)
            {
                if (id == "bowl.@rubbed.rim") bowl = resolve != null ? resolve("@rubbed") : 0;
                else if (id == "bowl.@ringing.rim") bowl = resolve != null ? resolve("@ringing") : 0;
                else if (id == "bowls.lesson.@target") bowl = resolve != null ? resolve("@target") : 0;
                else return false;
                bowl = Mathf.Clamp(bowl, 0, 2);
            }
            Vector3 rim = rig.TransformPoint(RimLocal(bowl));
            if (!near) { world = rim; up = rig.TransformVector(new Vector3(0f, BowlsLayout.R(bowl), 0f)); return true; }
            Vector3 d = head - rim; d -= rig.up * Vector3.Dot(d, rig.up);
            d = d.sqrMagnitude > 1e-8f ? d.normalized : -rig.forward;
            world = rim + d * BowlsLayout.R(bowl) * rig.lossyScale.x; up = d;
            return true;
        }
    }
}
