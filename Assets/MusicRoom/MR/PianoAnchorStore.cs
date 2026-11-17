// No longer used to place anything: entering "Play in my room" opens the instruments in front of the player, and Forget()
// drops an anchor an earlier version saved. Kept for that clean-up and its tested pose rule. What it did before:
// E9 (experience.md section 11, stretch): "Play in my room" remembered where the player put the piano. When the piano is
// placed by the player's pinch (or released after an MR grab), a local OVRSpatialAnchor is created at its pose, saved
// on the headset (no internet: USE_ANCHOR_API is already in the manifest) and its uuid kept in the settings; the previous
// anchor is erased. On the next MR entry the anchor is loaded and localized (3 s at most), its pose validated (within
// 3 m of the head horizontally, upright, between the floor and 1.6 m) and the piano put back there instead of on the
// best table. Any failure falls back to the normal table flow; after 3 failures in a row the anchor is erased.
// Only on the headset (RigBootstrap.IsXrActive); every await is guarded so a stale result never moves the piano.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.MR
{
    public sealed class PianoAnchorStore
    {
        public const string KeyAnchor = "musicroom.mrPianoAnchor";        // Guid "N"
        public const string KeyOnTable = "musicroom.mrPianoOnTable";      // 0 / 1
        public const string KeyFailures = "musicroom.anchorFailures";     // restore failures in a row
        public const float MaxHeadDistance = 3f, MinUpDot = 0.98f, MinY = -0.1f, MaxY = 1.6f;
        public const double LocalizeTimeout = 3.0;
        public const int MaxFailures = 3;
        /// <summary>While mixed reality runs, the piano follows its anchor when they drift apart by more than this
        /// (the headset relocalized).</summary>
        public const float FollowDistance = 0.002f;

        readonly AppSettings _settings;
        readonly List<OVRSpatialAnchor.UnboundAnchor> _unbound = new List<OVRSpatialAnchor.UnboundAnchor>(1);
        OVRSpatialAnchor _bound;
        int _saveSerial;

        public PianoAnchorStore(AppSettings settings) { _settings = settings; }

        /// <summary>The anchor the piano is bound to in this MR session (null when none).</summary>
        public OVRSpatialAnchor Bound => _bound != null ? _bound : null;
        public bool HasSaved => TryGetSaved(out _);
        public int Saves { get; private set; }
        public int Restores { get; private set; }

        /// <summary>The reference anchorPoseOk: horizontal distance to the head at most 3 m, up.y at least .98 (upright),
        /// and -0.1 m &lt;= y &lt;= 1.6 m.</summary>
        public static bool PoseOk(Vector3 pos, Vector3 up, Vector3 head)
        {
            float dx = pos.x - head.x, dz = pos.z - head.z;
            return Mathf.Sqrt(dx * dx + dz * dz) <= MaxHeadDistance && up.y >= MinUpDot && pos.y >= MinY && pos.y <= MaxY;
        }

        bool TryGetSaved(out Guid id)
        {
            id = Guid.Empty;
            string s = _settings != null ? _settings.GetString(KeyAnchor, "") : "";
            return !string.IsNullOrEmpty(s) && Guid.TryParseExact(s, "N", out id);
        }

        /// <summary>The player placed the piano: anchor this pose and remember it (the latest placement wins).</summary>
        public async Task SaveAsync(Pose pose, bool onTable)
        {
            int serial = ++_saveSerial;
            ReleaseBound();
            OVRSpatialAnchor anchor = null;
            try
            {
                var go = new GameObject("MR piano anchor");
                go.transform.SetPositionAndRotation(pose.position, pose.rotation);
                anchor = go.AddComponent<OVRSpatialAnchor>();
                if (!await anchor.WhenLocalizedAsync()) { Debug.Log("[MR] piano anchor: not localized"); Drop(anchor); return; }
                var saved = await anchor.SaveAnchorAsync();
                if (!saved.Success) { Debug.Log($"[MR] piano anchor: save failed ({saved.Status})"); Drop(anchor); return; }
                if (serial != _saveSerial || anchor == null) { Drop(anchor); return; }   // a newer placement superseded it
                bool hadPrevious = TryGetSaved(out Guid previous);
                _settings.SetString(KeyAnchor, anchor.Uuid.ToString("N"));
                _settings.SetInt(KeyOnTable, onTable ? 1 : 0);
                _settings.SetInt(KeyFailures, 0);
                _settings.Save();
                _bound = anchor;
                Saves++;
                Debug.Log($"[MR] piano anchor saved {anchor.Uuid:N} ({(onTable ? "on a table" : "on the floor")})");
                if (hadPrevious && previous != anchor.Uuid) await OVRSpatialAnchor.EraseAnchorsAsync(null, new[] { previous });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MR] piano anchor save: " + e.Message);
                Drop(anchor);
            }
        }

        /// <summary>Loads, localizes and validates the saved anchor. On success binds it and returns its pose and whether
        /// the piano stood on a table; on failure counts it (erasing the anchor after MaxFailures) and returns false.</summary>
        public async Task<(bool ok, Pose pose, bool onTable)> RestoreAsync(Vector3 head)
        {
            if (!TryGetSaved(out Guid id)) return (false, default, false);
            try
            {
                _unbound.Clear();
                var load = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { id }, _unbound);
                if (!load.Success || _unbound.Count == 0) return Fail("not found");
                OVRSpatialAnchor.UnboundAnchor ua = _unbound[0];
                if (!await ua.LocalizeAsync(LocalizeTimeout)) return Fail("not localized in 3 s");
                if (!ua.TryGetPose(out Pose pose)) return Fail("no pose");
                if (!PoseOk(pose.position, pose.rotation * Vector3.up, head)) return Fail($"pose rejected at {pose.position:F2}");
                ReleaseBound();
                var go = new GameObject("MR piano anchor");
                go.transform.SetPositionAndRotation(pose.position, pose.rotation);
                var anchor = go.AddComponent<OVRSpatialAnchor>();
                ua.BindTo(anchor);
                _bound = anchor;
                _settings.SetInt(KeyFailures, 0);
                _settings.Save();
                Restores++;
                bool onTable = _settings.GetInt(KeyOnTable, 0) != 0;
                Debug.Log($"[MR] piano anchor restored at {pose.position:F2} ({(onTable ? "on a table" : "on the floor")})");
                return (true, pose, onTable);
            }
            catch (Exception e)
            {
                return Fail(e.Message);
            }
        }

        (bool, Pose, bool) Fail(string why)
        {
            int n = _settings.GetInt(KeyFailures, 0) + 1;
            Debug.Log($"[MR] piano anchor restore failed ({why}), {n} in a row");
            if (n >= MaxFailures && TryGetSaved(out Guid id))
            {
                _ = OVRSpatialAnchor.EraseAnchorsAsync(null, new[] { id });
                _settings.Store.DeleteKey(KeyAnchor);
                n = 0;
            }
            _settings.SetInt(KeyFailures, n);
            _settings.Save();
            return (false, default, false);
        }

        /// <summary>Entering mixed reality: an anchor saved by an earlier version is never restored (the instruments open in
        /// front of the player); its keys are deleted and, on the headset, the anchor itself is erased.</summary>
        public void Forget(bool onHeadset)
        {
            if (TryGetSaved(out Guid id))
            {
                if (onHeadset)
                {
                    try { _ = OVRSpatialAnchor.EraseAnchorsAsync(null, new[] { id }); }
                    catch (Exception e) { Debug.LogWarning("[MR] piano anchor erase: " + e.Message); }
                }
                Debug.Log($"[MR] old piano anchor {id:N} forgotten (the instruments open in front of you)");
            }
            _settings.Store.DeleteKey(KeyAnchor);
            _settings.Store.DeleteKey(KeyOnTable);
            _settings.Store.DeleteKey(KeyFailures);
            _settings.Save();
            ReleaseBound();
        }

        /// <summary>Leaving mixed reality (or a new placement): the bound anchor's GameObject goes; the saved anchor stays.</summary>
        public void ReleaseBound()
        {
            if (_bound != null) UnityEngine.Object.Destroy(_bound.gameObject);
            _bound = null;
        }

        static void Drop(OVRSpatialAnchor a)
        {
            if (a != null) UnityEngine.Object.Destroy(a.gameObject);
        }
    }
}
