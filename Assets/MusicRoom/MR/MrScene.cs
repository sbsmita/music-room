// Mixed-reality scene access for "Play in my room": owns the MRUK instance (made from code), asks for the
// spatial-data permission, loads the user's Space Setup, and reports a simple status the UI can act on.
// MRUK 207 facts this relies on are cited in 03-interaction-sdk-and-mruk.md.
using System;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Android;

namespace MusicRoom.MR
{
    public enum MrSceneStatus
    {
        Idle,         // nothing requested yet
        Loading,      // permission dialog or scene discovery in progress
        Loaded,       // at least one room loaded: TableFinder has data
        NoPermission, // USE_SCENE denied: no MRUK data and no depth raycasts
        NoRooms,      // Space Setup never run on this headset (or it found no room)
        NotAvailable, // editor without Link / XR Simulator: OVRPlugin not initialized
        Failed,       // any other MRUK.LoadDeviceResult
    }

    public static class MrScene
    {
        public static MrSceneStatus Status { get; private set; } = MrSceneStatus.Idle;
        public static MRUK.LoadDeviceResult LastResult { get; private set; } = MRUK.LoadDeviceResult.Success;
        public static event Action<MrSceneStatus> StatusChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() // play mode with domain reload disabled
        {
            Status = MrSceneStatus.Idle;
            LastResult = MRUK.LoadDeviceResult.Success;
            StatusChanged = null;
        }

        // Create the single MRUK component once. Call after the OVRCameraRig exists: MRUK.Awake logs an error
        // when OVRCameraRig.Instance is null, and world locking drives OVRCameraRig's TrackingSpace.
        public static MRUK EnsureMruk()
        {
            if (MRUK.Instance != null) return MRUK.Instance;
            var go = new GameObject("MRUK");
            go.SetActive(false); // Awake/Update must not run before SceneSettings is set (Update reads it every frame)
            var mruk = go.AddComponent<MRUK>();
            mruk.SceneSettings = new MRUK.MRUKSettings
            {
                DataSource = MRUK.SceneDataSource.Device,
                RoomIndex = -1,
                RoomPrefabs = Array.Empty<GameObject>(),
                SceneJsons = Array.Empty<TextAsset>(),
                LoadSceneOnStartup = false, // we load explicitly when the player picks "Play in my room"
                SeatWidth = 0.6f,
            };
            mruk.EnableWorldLock = false; // the VR room snap-turns the rig; turn on only in mixed reality
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.SetActive(true);
            return mruk;
        }

        public static bool HasScenePermission
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission);
#else
                return true; // editor / desktop: no Android runtime permissions
#endif
            }
        }

        // Resolves true when granted, false when denied. Polls as well, because the callbacks never fire if a
        // permission dialog was already showing when we asked (see MRUK.cs:616-619 and MRUK.Update).
        public static async Task<bool> RequestScenePermissionAsync(float timeoutSeconds = 120f)
        {
            if (HasScenePermission) return true;
            var tcs = new TaskCompletionSource<bool>();
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => tcs.TrySetResult(true);
            callbacks.PermissionDenied += _ => tcs.TrySetResult(false);
            Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission, callbacks);
            float until = Time.realtimeSinceStartup + timeoutSeconds;
            while (!tcs.Task.IsCompleted)
            {
                if (HasScenePermission) return true;
                if (Time.realtimeSinceStartup > until) return false;
                await Task.Yield(); // continues on Unity's main-thread SynchronizationContext
            }
            return tcs.Task.Result;
        }

        // "Play in my room": permission, then the device scene. Never opens Space Setup by itself.
        public static async Task<MrSceneStatus> LoadDeviceSceneAsync()
        {
            var mruk = EnsureMruk();
            SetStatus(MrSceneStatus.Loading);
            if (!await RequestScenePermissionAsync()) return SetStatus(MrSceneStatus.NoPermission);
            LastResult = await mruk.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false, removeMissingRooms: true);
            return SetStatus(Map(LastResult, mruk));
        }

        // Menu row "Set up my room": the system Space Setup (pauses the app), then a reload.
        public static async Task<MrSceneStatus> RunSpaceSetupThenReloadAsync()
        {
            EnsureMruk();
            if (!await RequestScenePermissionAsync()) return SetStatus(MrSceneStatus.NoPermission);
            bool ok = await OVRScene.RequestSpaceSetup();
            return ok ? await LoadDeviceSceneAsync() : Status;
        }

        // Editor, XR Simulator and tests: one of MRUK's sample rooms instead of the device.
        public static async Task<MrSceneStatus> LoadJsonSceneAsync(string json)
        {
            var mruk = EnsureMruk();
            SetStatus(MrSceneStatus.Loading);
            LastResult = await mruk.LoadSceneFromJsonString(json, removeMissingRooms: true);
            return SetStatus(Map(LastResult, mruk));
        }

        public static async Task<MrSceneStatus> LoadPrefabSceneAsync(GameObject roomPrefab)
        {
            var mruk = EnsureMruk();
            SetStatus(MrSceneStatus.Loading);
            LastResult = await mruk.LoadSceneFromPrefab(roomPrefab, clearSceneFirst: true);
            return SetStatus(Map(LastResult, mruk));
        }

        // Mixed reality only. Turning it off makes MRUK reset TrackingSpace's local pose (MRUK.cs:750-756).
        public static void SetWorldLock(bool on) => SetWorldLock(on, Matrix4x4.identity);

        // While locked MRUK writes TrackingSpace's WORLD pose as trackingSpaceOffset * its native correction every frame
        // (MRUK.cs:729-745), ignoring whatever moved the rig above it, so the offset must be the rig root's pose (identity
        // after MrModule.Enter resets the rig on enter); it goes back to identity when unlocked.
        public static void SetWorldLock(bool on, Matrix4x4 trackingSpaceOffset)
        {
            if (MRUK.Instance == null) return;
            MRUK.Instance.TrackingSpaceOffset = on ? trackingSpaceOffset : Matrix4x4.identity;
            MRUK.Instance.EnableWorldLock = on;
        }

        // Leaving mixed reality: forget the rooms (fires RoomRemovedEvent for each).
        public static void Clear()
        {
            if (MRUK.Instance != null) MRUK.Instance.ClearScene();
            SetStatus(MrSceneStatus.Idle);
        }

        static MrSceneStatus Map(MRUK.LoadDeviceResult r, MRUK mruk)
        {
            switch (r)
            {
                case MRUK.LoadDeviceResult.Success:
                    return mruk.Rooms.Count > 0 ? MrSceneStatus.Loaded : MrSceneStatus.NoRooms;
                case MRUK.LoadDeviceResult.NoScenePermission:
                case MRUK.LoadDeviceResult.FailurePermissionInsufficient:
                    return MrSceneStatus.NoPermission;
                case MRUK.LoadDeviceResult.NoRoomsFound:
                    return MrSceneStatus.NoRooms;
                case MRUK.LoadDeviceResult.NotInitialized:
                    return MrSceneStatus.NotAvailable;
                case MRUK.LoadDeviceResult.DiscoveryOngoing:
                    return MrSceneStatus.Loading;
                default:
                    return MrSceneStatus.Failed;
            }
        }

        static MrSceneStatus SetStatus(MrSceneStatus s)
        {
            if (s != Status)
            {
                Status = s;
                StatusChanged?.Invoke(s);
            }
            return s;
        }
    }
}
