// The piano on a real surface: the placing reticle that follows where a hand (else the head)
// points across the real surfaces, and the pose a pinch puts the piano in: facing the player, on a table (hit more
// than 0.3 m above the floor) its body rests on the top with the stand hidden, on the floor it stands on its stand;
// either way the key fronts go PUSH_BACK beyond the chosen point. The best-table auto placement (TableFinder) is in
// MrModule; this file is the reticle and the maths, with no MRUK in it. Since the platform (12-new-instruments/
// platform.md 9) the same rule places any lead instrument: FromHit with its descriptor's surface lift and stand height
// (the piano's are TableLift and StandHeight, so its pose is unchanged), and the reticle shows the lead's footprint.
using UnityEngine;

namespace MusicRoom.MR
{
    public static class PianoPlacement
    {
        /// <summary>FromHit: the piano's origin (front of the white key tops) for a pinch at <paramref name="hit"/>,
        /// the player at <paramref name="head"/>. Local +z points away from the player (ARCHITECTURE.md section 5).</summary>
        public static Pose FromHit(Vector3 hit, Vector3 head, float floorY, out bool onTable) =>
            FromHit(hit, head, floorY, MrConstants.TableLift, MrConstants.StandHeight, true, out onTable);

        /// <summary>The same for any lead instrument: <paramref name="surfaceLift"/> (its Mr.SurfaceLift) above a table top,
        /// <paramref name="standHeight"/> (its rig origin's home height) above the floor; a lead that cannot stand on a table
        /// (<paramref name="canTable"/> false) is never put on one.</summary>
        public static Pose FromHit(Vector3 hit, Vector3 head, float floorY, float surfaceLift, float standHeight, bool canTable, out bool onTable)
        {
            onTable = canTable && hit.y - floorY > MrConstants.OnTableMinHeight;
            Vector3 away = MrUtil.Flat(hit - head, Vector3.forward);
            var p = new Vector3(hit.x, hit.y + (onTable ? surfaceLift : standHeight), hit.z) + away * MrConstants.PushBack;
            return new Pose(p, Quaternion.LookRotation(away, Vector3.up));
        }

        /// <summary>Each frame the reticle lies at the hit, turned so its +z (the footprint side) is away from the head.</summary>
        public static Quaternion ReticleRotation(Vector3 hit, Vector3 head, Vector3 headForward) =>
            Quaternion.LookRotation(MrUtil.Flat(hit - head, MrUtil.Flat(headForward, Vector3.forward)), Vector3.up);
    }

    /// <summary>The placing reticle: a ring r .06-.08 (alpha .9) at the point and the piano body's
    /// 1.326 x .294 footprint (alpha .12, edges .6) where the body will stand, all #ffc46a, lying on the surface.</summary>
    public sealed class PianoReticle
    {
        readonly GameObject _go;
        readonly Material _ring, _fill, _edge;
        readonly Mesh _ringMesh, _fillMesh, _edgeMesh;
        readonly Transform _fillPart, _edgePart;

        public PianoReticle(Transform parent, MrMaterials materials)
        {
            _go = new GameObject("Piano reticle");
            _go.transform.SetParent(parent, false);
            _ring = materials.Unlit(MrMaterials.Reticle, MrConstants.ReticleColor, MrConstants.RingAlpha);
            _fill = materials.Unlit(MrMaterials.Reticle, MrConstants.ReticleColor, MrConstants.FootprintAlpha);
            _edge = materials.Unlit(MrMaterials.Reticle, MrConstants.ReticleColor, MrConstants.FootprintEdgeAlpha);
            _ringMesh = MrMeshes.RingXZ(MrConstants.RingInner, MrConstants.RingOuter, MrConstants.RingSegments);
            _fillMesh = MrMeshes.RectXZ(MrConstants.FootprintWidth, MrConstants.FootprintDepth, MrConstants.FootprintZ);
            _edgeMesh = MrMeshes.RectOutlineXZ(MrConstants.FootprintWidth, MrConstants.FootprintDepth, MrConstants.FootprintZ);
            MrMeshes.AddPart(_go.transform, "Ring", _ringMesh, _ring);
            _fillPart = MrMeshes.AddPart(_go.transform, "Footprint", _fillMesh, _fill).transform;
            _edgePart = MrMeshes.AddPart(_go.transform, "Footprint edge", _edgeMesh, _edge).transform;
            _go.SetActive(false);
        }

        /// <summary>The footprint drawn: <paramref name="width"/> x <paramref name="depth"/> centred at (x, z) in the reticle
        /// frame (+z away from the player). The piano's (1.326 x .294 at z .393) is the mesh as built; any other size scales it.</summary>
        public void SetFootprint(float width, float depth, float centreZ, float centreX = 0f)
        {
            var scale = new Vector3(width / MrConstants.FootprintWidth, 1f, depth / MrConstants.FootprintDepth);
            var offset = new Vector3(centreX, 0f, centreZ - MrConstants.FootprintZ * scale.z);
            _fillPart.localScale = _edgePart.localScale = scale;
            _fillPart.localPosition = _edgePart.localPosition = offset;
        }

        /// <summary>The footprint drawn now: its size and centre in the reticle frame.</summary>
        public Vector2 FootprintSize => new Vector2(MrConstants.FootprintWidth * _fillPart.localScale.x, MrConstants.FootprintDepth * _fillPart.localScale.z);
        public Vector2 FootprintCentre => new Vector2(_fillPart.localPosition.x, _fillPart.localPosition.z + MrConstants.FootprintZ * _fillPart.localScale.z);

        public Transform Transform => _go.transform;
        public bool Visible => _go.activeSelf;

        public void Show(Vector3 position, Quaternion rotation)
        {
            _go.transform.SetPositionAndRotation(position, rotation);
            if (!_go.activeSelf) _go.SetActive(true);
        }

        public void Hide()
        {
            if (_go.activeSelf) _go.SetActive(false);
        }

        public void Destroy()
        {
            MrUtil.Destroy(_ring); MrUtil.Destroy(_fill); MrUtil.Destroy(_edge);
            MrUtil.Destroy(_ringMesh); MrUtil.Destroy(_fillMesh); MrUtil.Destroy(_edgeMesh);
            MrUtil.Destroy(_go);
        }
    }
}
