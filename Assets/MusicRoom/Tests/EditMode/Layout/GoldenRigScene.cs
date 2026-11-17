// The four golden rigs (piano, drums, harp, side table) rebuilt from the boxes the reference placement measured
// (GoldenLayoutFixture.cs): one renderer per measured part with exactly that oriented box, so PlacementSolver measures
// what the reference measured. Rig homes are ARCHITECTURE.md section 8's.
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Layout;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.Layout
{
    internal sealed class GoldenRigScene : IDisposable
    {
        public static readonly Vector3[] HomeAll = { new Vector3(0f, 0.72f, 0.45f), new Vector3(0.2f, 0f, -0.26f), new Vector3(-0.78f, 0f, -0.2f), new Vector3(-0.39f, 0f, -0.21f) };
        public static readonly float[] HomeYawAll = { 0f, 90f, 83f, -90f };
        public static readonly Vector3[] HomeSingle = { new Vector3(0f, 0.72f, 0.45f), new Vector3(0f, 0f, 0.1f), new Vector3(-0.296604f, 0f, 0.45f), new Vector3(-0.39f, 0f, -0.21f) };
        public static readonly float[] HomeYawSingle = { 0f, 0f, 180f, -90f };

        public readonly GameObject Root;
        public readonly Rig[] Rigs = new Rig[4];
        public readonly RigEntry[] Entries = new RigEntry[4];
        public readonly PlacementSolver Solver = new PlacementSolver();
        public readonly Transform Panel;
        readonly List<Mesh> _meshes = new List<Mesh>();

        public GoldenRigScene()
        {
            Root = new GameObject("GoldenRigScene");
            for (int r = 0; r < 4; r++)
            {
                var g = new GameObject(GoldenLayout.RigNames[r]).transform;
                g.SetParent(Root.transform, false);
                Rigs[r] = new Rig
                {
                    Id = (RigId)r, Group = g, Priority = r,
                    HomeAll = HomeAll[r], HomeYawAll = HomeYawAll[r], HomeSingle = HomeSingle[r], HomeYawSingle = HomeYawSingle[r],
                };
                var e = new RigEntry(Rigs[r]) { CentreLocal = GoldenLayout.CentreLocal[r], HasCentre = true };
                Entries[r] = e;
                Solver.Entries.Add(e);
            }
            foreach (var p in GoldenLayout.Parts)
            {
                Renderer ren = AddBox(Rigs[p.Rig].Group, p, p.Name);
                var part = new PartRef(ren);
                Entries[p.Rig].Footprint.Add(part);
                if ((p.Flags & GoldenLayout.FlagHandle) != 0) Entries[p.Rig].Handles.Add(part);
            }
            Panel = new GameObject("panel").transform;
            Panel.SetParent(Root.transform, false);
            foreach (var p in GoldenLayout.PanelBoxes) AddBox(Panel, p, "plate");
            Solver.Panel.Group = Panel;
            Solver.Panel.Shown = false;
            // The goldens were recorded in the reference room, whose box is the room frame's (RoomFrame.LocalBox: x +-3.2,
            // z -4.08..1.12). Since 2026-11-11 the live room is turned to face the fire (the solver's default is
            // RoomFrame.WorldBox, LayoutMathTests); the resolver's algorithm is checked here in the reference's room.
            Bounds reference = RoomFrame.LocalBox;
            Solver.RoomXMin = reference.min.x; Solver.RoomXMax = reference.max.x; Solver.RoomZMin = reference.min.z; Solver.RoomZMax = reference.max.z;
        }

        /// <summary>A renderer whose mesh bounds are the part's half extents, posed at its centre and axes (the Unity basis
        /// of a mirrored right-handed box is (A0, A1, -A2); a box does not care about the sign of an axis).</summary>
        Renderer AddBox(Transform parent, GoldenLayout.Part p, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = p.C;
            go.transform.localRotation = Quaternion.LookRotation(-p.A2, p.A1);
            var mesh = BoxMesh(p.H);
            _meshes.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            return go.AddComponent<MeshRenderer>();
        }

        public static Mesh BoxMesh(Vector3 h)
        {
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++) v[i] = new Vector3((i & 1) != 0 ? h.x : -h.x, (i & 2) != 0 ? h.y : -h.y, (i & 4) != 0 ? h.z : -h.z);
            int[] t = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
            var m = new Mesh { name = "box" };
            m.vertices = v;
            m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>place() without the settle: membership for the mode, every placed rig at its seat home.</summary>
        public void PlaceHomes(bool single, int inst, Vector3 head, float seatYawDeg)
        {
            float yaw = seatYawDeg * Mathf.Deg2Rad;
            for (int r = 0; r < 4; r++)
            {
                bool rowRig = r <= 2;
                bool inScene = !rowRig || !single || r == inst;
                Rigs[r].InScene = inScene;
                Rigs[r].Group.gameObject.SetActive(inScene);
                Rigs[r].Group.localScale = Vector3.one;
                bool useSingle = single && rowRig;
                Vector3 home = useSingle ? HomeSingle[r] : HomeAll[r];
                float homeYaw = useSingle ? HomeYawSingle[r] : HomeYawAll[r];
                Rigs[r].Group.SetPositionAndRotation(LayoutMath.SeatPlace(head, yaw, home), Quaternion.Euler(0f, homeYaw + seatYawDeg, 0f));
            }
        }

        public void Dispose()
        {
            Object.DestroyImmediate(Root);
            foreach (var m in _meshes) Object.DestroyImmediate(m);
        }
    }
}
