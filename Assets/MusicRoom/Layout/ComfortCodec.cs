// The player's comfort tweaks, kept relative to the player. Every instrument opens in front of the seated player at its
// default pose (its seat home, or its My set slot, about the head's position and heading at that moment); a drag (its
// grab handle, or Move instruments) stores how far the player moved it from that default, as an offset in the default
// pose's own bearing frame:
//   Distance  metres along the floor direction from the head to the instrument's play centre (+ away from the player),
//   Side      metres across it (+ to the player's right),
//   Height    metres up from the default height (the piano's key tops, the kit's lift),
//   Yaw       degrees turned from the default heading (+ clockwise seen from above, Unity yaw).
// So the next open is in front again, wherever the player sits and faces, with the same tweak. Offsets are clamped to a
// comfortable range (the main instrument stays centred: a smaller sideways range), and an old saved placement (the
// seat-frame poses of ArrangementCodec, "a1;...") is turned into offsets once, dropping any entry that would put the
// instrument outside that range. Text format "c1;Piano:distance,side,height,yaw;Drums:...", invariant culture. Pure code:
// EditMode tests. MigrateSettings runs once per install (musicroom.migrated.stabilise): it clears two settings an old save
// could hold that silently switch features off (Lock instruments hid the grab handles; Real hands could be on from a
// single stray poke on the home page).
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Layout
{
    /// <summary>An instrument's offset from its default pose in front of the player (see ComfortCodec).</summary>
    public struct ComfortOffset
    {
        public float Distance, Side, Height, Yaw;

        public ComfortOffset(float distance, float side, float height, float yaw)
        {
            Distance = distance; Side = side; Height = height; Yaw = yaw;
        }

        /// <summary>Within 5 mm and half a degree of no tweak at all.</summary>
        public bool IsZero => Mathf.Abs(Distance) < 0.005f && Mathf.Abs(Side) < 0.005f && Mathf.Abs(Height) < 0.005f && Mathf.Abs(Yaw) < 0.5f;

        public override string ToString() => $"(distance {Distance:F3}, side {Side:F3}, height {Height:F3}, yaw {Yaw:F1})";
    }

    public static class ComfortCodec
    {
        public const string Version = "c1";

        /// <summary>1 once MigrateSettings ran on this install.</summary>
        public const string KeyMigratedStabilise = "musicroom.migrated.stabilise";

        /// <summary>Once per install, before the layout reads its settings: Lock instruments off (musicroom.locked) and Real
        /// hands back to the drawn hands (musicroom.mrRealHands 0). Returns true when it ran now.</summary>
        public static bool MigrateSettings(AppSettings settings)
        {
            if (settings == null || settings.GetInt(KeyMigratedStabilise, 0) == 1) return false;
            bool locked = settings.GetBool(AppSettings.KeyLockedOld, false), real = settings.MrRealHands;
            if (settings.Store.HasKey(AppSettings.KeyLockedOld)) settings.SetBool(AppSettings.KeyLockedOld, false);
            settings.MrRealHands = false;
            settings.SetInt(KeyMigratedStabilise, 1);
            settings.Save();
            Debug.Log($"[Layout] settings migrated: Lock instruments {(locked ? "on -> off" : "off")}, Real hands {(real ? "on -> off" : "off")}");
            return true;
        }

        /// <summary>1 once MigrateAutoSavedSpots ran on this install.</summary>
        public const string KeyMigratedAutoSpots = "musicroom.migrated.autospots";

        /// <summary>Once per install (2026-11-17, TUNE-DECK-FRONT): an install that ran a build before today's (it has
        /// musicroom.migrated.stabilise) loses every saved spot (comfort offsets and old arrangements, My set and each One
        /// alone). Until today Move instruments turned on and off saved EVERY instrument's and the side table's automatic
        /// pose as the player's spot (the resolver's pushes, a seat turned by the swivel, a play centre measured folded): the
        /// headset showed the kit pushed out and turned and the deck behind the player on every arrangement. Only a
        /// deliberate drag is saved since (FRONTSPOT). A new install has nothing to clear. Returns the keys removed.</summary>
        public static int MigrateAutoSavedSpots(AppSettings settings)
        {
            if (settings == null || settings.GetInt(KeyMigratedAutoSpots, 0) == 1) return 0;
            int n = 0;
            if (settings.GetInt(KeyMigratedStabilise, 0) == 1)
            {
                foreach (InstrumentKind k in System.Enum.GetValues(typeof(InstrumentKind)))
                {
                    // My set's keys (named by its lead) and each One alone's: every kind, so no instrument is named here
                    n += Drop(settings, AppSettings.ComfortKey(LayoutMode.All, k));
                    n += Drop(settings, AppSettings.ArrangeKey(LayoutMode.All, k));
                    n += Drop(settings, AppSettings.ComfortKey(LayoutMode.Single, k));
                    n += Drop(settings, AppSettings.ArrangeKey(LayoutMode.Single, k));
                }
                Debug.Log($"[Layout] settings migrated: {n} automatically saved spot(s) cleared (only a deliberate move is kept from now on)");
            }
            settings.SetInt(KeyMigratedAutoSpots, 1);
            settings.Save();
            return n;
        }

        static int Drop(AppSettings settings, string key)
        {
            if (!settings.Store.HasKey(key)) return 0;
            settings.Store.DeleteKey(key);
            return 1;
        }

        /// <summary>"Lock instruments" is gone (2026-11-10): "Move instruments", off by default, is the one setting. A save
        /// that still has musicroom.locked loses it here; whatever it said, the instruments start fixed (Move instruments
        /// off unless the player turned it on since), which is what Lock on meant and what Lock off now needs the setting
        /// for. Returns true when a key was removed.</summary>
        public static bool MigrateMoveSetting(AppSettings settings)
        {
            if (settings == null || !settings.Store.HasKey(AppSettings.KeyLockedOld)) return false;
            bool locked = settings.GetBool(AppSettings.KeyLockedOld, false);
            settings.Store.DeleteKey(AppSettings.KeyLockedOld);
            settings.Save();
            Debug.Log($"[Layout] settings migrated: Lock instruments ({(locked ? "on" : "off")}) removed; Move instruments {(settings.MoveInstruments ? "on" : "off")}");
            return true;
        }
        /// <summary>The comfortable range of a tweak: closer by at most 0.3 m, farther by at most 0.6 m, 0.6 m to a side (the
        /// main instrument 0.3 m, so it stays in front), 0.45 m up or down (the descriptors' lift ranges clamp it further),
        /// and any turn (since 2026-11-10 an instrument spins all the way round in Move instruments; was 90 degrees).</summary>
        public const float MaxCloser = 0.3f, MaxFarther = 0.6f, MaxSide = 0.6f, MaxSideMain = 0.3f, MaxHeight = 0.45f, MaxYaw = 180f;

        /// <summary>The floor direction from the head to <paramref name="playCentre"/> (seat frame), straight ahead when the
        /// centre is within 5 cm of the head.</summary>
        public static Vector2 Bearing(Vector2 playCentre)
        {
            return playCentre.sqrMagnitude < 0.0025f ? new Vector2(0f, 1f) : playCentre.normalized;
        }

        /// <summary>The offset that takes the default pose <paramref name="def"/> to <paramref name="actual"/> (both seat
        /// frame), in the bearing frame of <paramref name="bearing"/> (unit, seat frame x right / y ahead).</summary>
        public static ComfortOffset Between(in SeatPose def, in SeatPose actual, Vector2 bearing)
        {
            float dx = actual.Local.x - def.Local.x, dz = actual.Local.z - def.Local.z;
            // right of the bearing (x right, z ahead): (bz, -bx)
            float dist = dx * bearing.x + dz * bearing.y;
            float side = dx * bearing.y - dz * bearing.x;
            return new ComfortOffset(dist, side, actual.Local.y - def.Local.y, ArrangementCodec.WrapDeg(actual.Yaw - def.Yaw));
        }

        /// <summary>The default pose moved by the offset.</summary>
        public static SeatPose Apply(in SeatPose def, in ComfortOffset o, Vector2 bearing)
        {
            float dx = o.Distance * bearing.x + o.Side * bearing.y;
            float dz = o.Distance * bearing.y - o.Side * bearing.x;
            return new SeatPose
            {
                Local = new Vector3(def.Local.x + dx, def.Local.y + o.Height, def.Local.z + dz),
                Yaw = ArrangementCodec.WrapDeg(def.Yaw + o.Yaw),
            };
        }

        /// <summary>Inside the comfortable range (with 1 mm of slack): an old placement outside it is dropped, not clamped.</summary>
        public static bool Plausible(in ComfortOffset o, bool main)
        {
            const float e = 0.001f;
            float side = main ? MaxSideMain : MaxSide;
            return o.Distance >= -MaxCloser - e && o.Distance <= MaxFarther + e && Mathf.Abs(o.Side) <= side + e
                && Mathf.Abs(o.Height) <= MaxHeight + e && Mathf.Abs(o.Yaw) <= MaxYaw + 0.01f;
        }

        /// <summary>The offset held inside the comfortable range.</summary>
        public static ComfortOffset Clamp(in ComfortOffset o, bool main)
        {
            float side = main ? MaxSideMain : MaxSide;
            return new ComfortOffset(Mathf.Clamp(o.Distance, -MaxCloser, MaxFarther), Mathf.Clamp(o.Side, -side, side),
                Mathf.Clamp(o.Height, -MaxHeight, MaxHeight), Mathf.Clamp(ArrangementCodec.WrapDeg(o.Yaw), -MaxYaw, MaxYaw));
        }

        public static string Encode(IReadOnlyDictionary<RigId, ComfortOffset> offsets)
        {
            if (offsets == null || offsets.Count == 0) return "";
            var sb = new StringBuilder(Version, 160);
            for (int id = 0; id < 16; id++)
            {
                var rid = (RigId)id;
                if (!offsets.TryGetValue(rid, out ComfortOffset o)) continue;
                sb.Append(';').Append(rid.ToString()).Append(':')
                  .Append(F(o.Distance)).Append(',').Append(F(o.Side)).Append(',').Append(F(o.Height)).Append(',').Append(F(o.Yaw));
            }
            return sb.Length == Version.Length ? "" : sb.ToString();
        }

        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>Reads what Encode wrote into <paramref name="into"/> (cleared first). A wrong version, an unknown rig, a
        /// malformed or wildly large number drops that entry (everything for a wrong version); returns the entries read.</summary>
        public static int Decode(string text, Dictionary<RigId, ComfortOffset> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(text)) return 0;
            string[] parts = text.Split(';');
            if (parts.Length == 0 || parts[0] != Version) return 0;
            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i];
                int colon = part.IndexOf(':');
                if (colon <= 0) continue;
                if (!System.Enum.TryParse(part.Substring(0, colon), out RigId id) || !System.Enum.IsDefined(typeof(RigId), id)) continue;
                string[] v = part.Substring(colon + 1).Split(',');
                if (v.Length != 4) continue;
                if (!P(v[0], 5f, out float d) || !P(v[1], 5f, out float s) || !P(v[2], 5f, out float h) || !P(v[3], 720f, out float yaw)) continue;
                into[id] = new ComfortOffset(d, s, h, ArrangementCodec.WrapDeg(yaw));
            }
            return into.Count;
        }

        static bool P(string s, float limit, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v) && Mathf.Abs(v) <= limit;
    }
}
