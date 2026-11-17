// The snowy window of "Play in my room" was removed (2026-11-13, the player: "remove all window things"; the snow now falls
// in the whole room, Room/Decor). A save made before still holds the window the player placed by hand: its pose, the uuid
// of its spatial anchor on the headset, and a count of failed restores. Migrate runs once per boot (MrModule.Init) and
// forgets all three; on the headset the anchor itself is erased from the device too (best effort: a failure is logged,
// the key is deleted either way). A clean save is left alone (nothing written, 0 returned). Main thread.
using System;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.MR
{
    public static class RetiredWindow
    {
        /// <summary>The settings the snowy window wrote (its kept pose, its anchor uuid "N", failed restores in a row).</summary>
        public const string KeyPose = "musicroom.mrWindow", KeyAnchor = "musicroom.mrWindowAnchor", KeyFailures = "musicroom.mrWindowFailures";
        public static readonly string[] Keys = { KeyPose, KeyAnchor, KeyFailures };

        /// <summary>Forgets the snowy window of an older save. eraseAnchor: on the headset, also erase its spatial anchor
        /// from the device. Returns how many settings were deleted (0 for a clean save; the second call is always 0).</summary>
        public static int Migrate(AppSettings settings, bool eraseAnchor)
        {
            if (settings == null || settings.Store == null) return 0;
            ISettingsStore store = settings.Store;
            if (eraseAnchor && store.HasKey(KeyAnchor)) TryEraseAnchor(store.GetString(KeyAnchor, ""));
            int changed = 0;
            for (int i = 0; i < Keys.Length; i++)
                if (store.HasKey(Keys[i])) { store.DeleteKey(Keys[i]); changed++; }
            if (changed > 0)
            {
                store.Save();
                Debug.Log($"[MR] the snowy window is gone: {changed} saved setting(s) of it forgotten");
            }
            return changed;
        }

        /// <summary>Erases the window's spatial anchor from the headset, if the saved uuid parses. Never throws.</summary>
        public static bool TryEraseAnchor(string uuid)
        {
            if (string.IsNullOrEmpty(uuid) || !Guid.TryParseExact(uuid, "N", out Guid id) || id == Guid.Empty) return false;
            try
            {
                _ = OVRSpatialAnchor.EraseAnchorsAsync(null, new[] { id });
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MR] the old snowy window's anchor could not be erased: " + e.Message);
                return false;
            }
        }
    }
}
