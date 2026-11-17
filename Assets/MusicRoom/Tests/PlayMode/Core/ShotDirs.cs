// Where the GPU shot tests write their pictures. One override for every module: $MUSICROOM_SHOTS_ROOT, when set, puts a
// whole run's pictures under <root>/<module>. It wins over a module's own older variable (MUSICROOM_SHOTS,
// MUSICROOM_SHOTS_DIR, PIANO_SHOTS, LOOPER_SHOTS), which still works when the root is not set; without either, each
// module writes to <project>/Builds/Shots/<module> (Default; Builds/ is ignored by version control).
using System;
using System.IO;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode
{
    public static class ShotDirs
    {
        public const string RootVariable = "MUSICROOM_SHOTS_ROOT";

        /// <summary>$MUSICROOM_SHOTS_ROOT/<module> (created), or null when the variable is not set.</summary>
        public static string FromRoot(string module)
        {
            string root = Environment.GetEnvironmentVariable(RootVariable);
            if (string.IsNullOrEmpty(root)) return null;
            string dir = Path.Combine(root, module);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary><project>/Builds/Shots/<module> (created): every module's default picture folder.</summary>
        public static string Default(string module)
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "Shots", module);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
