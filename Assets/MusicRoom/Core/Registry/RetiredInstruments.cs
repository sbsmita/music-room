// Instruments taken out of the app, and what a save made before that still holds of them (2026-11-10: the theremin,
// removed at the player's request). Its id block value (8: InstrumentKind / RigId / UnitId) stays unused and is never
// reused; its persisted id no longer parses (InstrumentIds, the registry), so the generic paths already drop it: a take's
// events of its types have no playback target and are skipped, comfort offsets and stow spots under its name are not
// read. Migrate runs on every boot, before any module reads the settings, and tidies what the generic paths would
// otherwise keep around: the current instrument falls back to the piano, My set loses it (an empty set falls back to the
// default "piano,drums,harp"), and its own settings (its option rows, its comfort offsets, its moved display spot) are
// deleted. Cheap: a few string reads when there is nothing to do.
using System;
using System.Collections.Generic;

namespace MusicRoom.Core
{
    public static class RetiredInstruments
    {
        /// <summary>The persisted ids of instruments that were removed.</summary>
        public static readonly string[] Ids = { "theremin" };

        /// <summary>The settings keys each retired instrument owned (its options under musicroom.&lt;id&gt;.&lt;key&gt;, its One alone
        /// comfort offsets, its moved display spot).</summary>
        static IEnumerable<string> OwnKeys(string id)
        {
            foreach (string k in new[] { "tone", "help", "hand", "notes" }) yield return "musicroom." + id + "." + k;
            yield return AppSettings.KeyComfortPrefix + "single." + id;
            yield return AppSettings.KeyArrangePrefix + "single." + id;
            yield return AppSettings.KeyStowPrefix + id;
        }

        public static bool IsRetired(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < Ids.Length; i++) if (string.Equals(Ids[i], id.Trim(), StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Removes what the settings still hold of a retired instrument. Returns how many settings changed.</summary>
        public static int Migrate(AppSettings settings)
        {
            if (settings == null || settings.Store == null) return 0;
            ISettingsStore store = settings.Store;
            int changed = 0;
            // The instrument played alone: back to the piano (the layout's own fallback for an unknown id, made explicit).
            if (store.HasKey(AppSettings.KeyCurrentInstrument) && IsRetired(store.GetString(AppSettings.KeyCurrentInstrument, "")))
            {
                store.SetString(AppSettings.KeyCurrentInstrument, "piano");
                changed++;
            }
            // My set without it; nothing left: the default set.
            if (store.HasKey(AppSettings.KeySet))
            {
                string ids = store.GetString(AppSettings.KeySet, "") ?? "";
                var kept = new List<string>();
                bool dropped = false;
                foreach (string raw in ids.Split(','))
                {
                    string id = raw.Trim();
                    if (id.Length == 0) continue;
                    if (IsRetired(id)) { dropped = true; continue; }
                    kept.Add(id);
                }
                if (dropped)
                {
                    store.SetString(AppSettings.KeySet, kept.Count > 0 ? string.Join(",", kept) : MySetRules.Default);
                    changed++;
                }
            }
            for (int i = 0; i < Ids.Length; i++)
                foreach (string key in OwnKeys(Ids[i]))
                    if (store.HasKey(key)) { store.DeleteKey(key); changed++; }
            if (changed > 0) store.Save();
            return changed;
        }
    }
}
