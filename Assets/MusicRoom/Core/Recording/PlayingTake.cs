// Which saved recording a playback plays (2026-11-13, the paint gallery: each recording's painting is hung once and taken
// down when the recording is deleted). The Recorder only knows events; the Menu knows the recording it started from
// Recordings (a take or a saved loop), so it notes it here right after Recorder.PlayEvents. Readers (the Room's gallery)
// match the noted events against the playback's own (IRecorder.Take) or take a loop's note as is. A playback the deck
// starts from its own take is matched against the library by its events instead (the Room does it). Main thread.
using System.Collections.Generic;

namespace MusicRoom.Core
{
    public static class PlayingTake
    {
        /// <summary>The recording last started from Recordings: its id and events (null when none).</summary>
        public static string Id { get; private set; }
        public static List<TakeEvent> Events { get; private set; }
        /// <summary>Bumps on every Note (a reader can tell a new one).</summary>
        public static int Version { get; private set; }

        /// <summary>The Menu started this recording playing (Recordings > a take > Play).</summary>
        public static void Note(string id, List<TakeEvent> events)
        {
            Id = id;
            Events = events;
            Version++;
        }

        /// <summary>Forget it (a test's boot; the recording was deleted).</summary>
        public static void Clear()
        {
            Id = null;
            Events = null;
            Version++;
        }
    }
}
