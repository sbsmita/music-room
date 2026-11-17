// Shared checks of the classic three against their registry descriptors (12-new-instruments/platform.md 12.2, conformance
// check 3 and the homes): the rig homes equal the descriptor's Seat.Home / Single, and the descriptor's Seat.Boxes cover the
// union of the Rig.Handles bounds within 3 cm. Bounds are measured in the rig's own frame (each handle's mesh bounds, its 8
// corners carried into Rig.Group's space), so the rig's pose and yaw do not inflate them; the boxes' heights are above the
// floor with the rig at its home (rig-local y = Y - Home.y). The margins are logged for the report.
using System.Collections.Generic;
using System.Text;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode
{
    public static class ClassicConformance
    {
        public const float Tolerance = 0.03f;

        public struct Box { public Vector3 Min, Max; public string Name; }

        /// <summary>Each handle's bounds in the rig's frame (renderers under the group, enabled and active).</summary>
        public static List<Box> HandleBounds(Rig rig)
        {
            var list = new List<Box>();
            Matrix4x4 toRig = rig.Group.worldToLocalMatrix;
            foreach (Renderer r in rig.Handles)
            {
                if (r == null || !r.gameObject.activeInHierarchy || !r.transform.IsChildOf(rig.Group)) continue;
                var mf = r.GetComponent<MeshFilter>();
                Bounds lb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(r.transform.InverseTransformPoint(r.bounds.center), Vector3.zero);
                Matrix4x4 m = toRig * r.transform.localToWorldMatrix;
                Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 p = lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    p = m.MultiplyPoint3x4(p);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                list.Add(new Box { Min = min, Max = max, Name = r.name });
            }
            return list;
        }

        /// <summary>The descriptor's boxes (non-air) in the rig's frame.</summary>
        public static List<Box> FootBoxes(InstrumentDescriptor d, bool mirrored = false)
        {
            var list = new List<Box>();
            float y = d.Seat.Home.y;
            foreach (FootBox b in d.Seat.BoxesFor(mirrored))
            {
                if (b.Air) continue;
                list.Add(new Box { Min = new Vector3(b.Cx - b.Hx, b.Y0 - y, b.Cz - b.Hz), Max = new Vector3(b.Cx + b.Hx, b.Y1 - y, b.Cz + b.Hz), Name = b.ToString() });
            }
            return list;
        }

        static Box Union(List<Box> boxes)
        {
            var u = new Box { Min = boxes[0].Min, Max = boxes[0].Max };
            foreach (var b in boxes) { u.Min = Vector3.Min(u.Min, b.Min); u.Max = Vector3.Max(u.Max, b.Max); }
            return u;
        }

        /// <summary>How far <paramref name="inner"/> pokes out of <paramref name="outer"/> (m; 0 when inside), the worst side.</summary>
        public static float Overhang(in Box inner, in Box outer)
        {
            Vector3 lo = outer.Min - inner.Min, hi = inner.Max - outer.Max;
            return Mathf.Max(0f, Mathf.Max(Mathf.Max(lo.x, lo.y), Mathf.Max(lo.z, Mathf.Max(hi.x, Mathf.Max(hi.y, hi.z)))));
        }

        /// <summary>Conformance check 3: the union of the handles' bounds inside the union of the boxes grown by 3 cm (asserted),
        /// with the margins per side and each handle's best-fitting box logged. Returns the log text.</summary>
        public static string AssertBoxesCoverHandles(InstrumentDescriptor d, Rig rig, bool mirrored = false) => AssertBoxesCover(d, HandleBounds(rig), mirrored ? "built mirrored (SeatSpec.MirrorKey): the mirrored boxes" : null, mirrored);

        /// <summary>As AssertBoxesCoverHandles for a handle list the caller prepared (the kit's tripods, see DrumsPlatformTests);
        /// <paramref name="note"/> goes into the log.</summary>
        public static string AssertBoxesCover(InstrumentDescriptor d, List<Box> handles, string note, bool mirrored = false)
        {
            List<Box> boxes = FootBoxes(d, mirrored);
            Assert.That(handles, Is.Not.Empty, d.Id + ": handles");
            Assert.That(boxes, Is.Not.Empty, d.Id + ": footprint boxes");
            Box h = Union(handles), f = Union(boxes);
            var log = new StringBuilder();
            log.AppendLine($"[Conformance] {d.Id} footprint vs Rig.Handles (rig-local, m): handles x {h.Min.x:F3}..{h.Max.x:F3} y {h.Min.y:F3}..{h.Max.y:F3} z {h.Min.z:F3}..{h.Max.z:F3}; " +
                           $"boxes x {f.Min.x:F3}..{f.Max.x:F3} y {f.Min.y:F3}..{f.Max.y:F3} z {f.Min.z:F3}..{f.Max.z:F3}");
            log.AppendLine($"  margins (box beyond handles; negative = handles stick out): -x {h.Min.x - f.Min.x:+0.000;-0.000} +x {f.Max.x - h.Max.x:+0.000;-0.000} " +
                           $"-y {h.Min.y - f.Min.y:+0.000;-0.000} +y {f.Max.y - h.Max.y:+0.000;-0.000} -z {h.Min.z - f.Min.z:+0.000;-0.000} +z {f.Max.z - h.Max.z:+0.000;-0.000}");
            float worst = 0f;
            string worstName = null;
            foreach (var b in handles)
            {
                float best = float.PositiveInfinity;
                foreach (var fb in boxes) best = Mathf.Min(best, Overhang(b, fb));
                if (best > worst) { worst = best; worstName = b.Name; }
            }
            log.AppendLine($"  worst single handle outside its best box: {worstName ?? "none"} by {worst:F3} m");
            if (note != null) log.AppendLine("  " + note);
            foreach (var b in handles)
            {
                float best = float.PositiveInfinity;
                foreach (var fb in boxes) best = Mathf.Min(best, Overhang(b, fb));
                log.AppendLine($"    {b.Name}: x {b.Min.x:F3}..{b.Max.x:F3} y {b.Min.y:F3}..{b.Max.y:F3} z {b.Min.z:F3}..{b.Max.z:F3} (outside its best box by {best:F3})");
            }
            Debug.Log(log.ToString());
            var grown = new Box { Min = f.Min - Vector3.one * Tolerance, Max = f.Max + Vector3.one * Tolerance };
            Assert.That(Overhang(h, grown), Is.EqualTo(0f).Within(1e-5f), d.Id + ": the boxes cover the handles within 3 cm\n" + log);
            return log.ToString();
        }

        /// <summary>Rig.HomeAll / HomeSingle equal the descriptor's Seat.Home / Single (and yaws).</summary>
        public static void AssertHomes(InstrumentDescriptor d, Rig rig, float tol = 1e-4f)
        {
            Assert.That(Vector3.Distance(rig.HomeAll, d.Seat.Home), Is.LessThan(tol), d.Id + ": HomeAll = Seat.Home");
            Assert.That(rig.HomeYawAll, Is.EqualTo(d.Seat.HomeYaw).Within(1e-3f), d.Id + ": HomeYawAll");
            Assert.That(Vector3.Distance(rig.HomeSingle, d.Seat.Single), Is.LessThan(tol), d.Id + ": HomeSingle = Seat.Single (" + rig.HomeSingle.ToString("F4") + " vs " + d.Seat.Single.ToString("F4") + ")");
            Assert.That(rig.HomeYawSingle, Is.EqualTo(d.Seat.SingleYaw).Within(1e-3f), d.Id + ": HomeYawSingle");
            Assert.That(rig.Priority >= 0, Is.True);
        }
    }
}
