// The first five minutes (Docs/11-first-five/sunny-tour.md): the seams between Sunny's first-run script and tips
// (Guide) and the modules that show and do what Sunny teaches. Every member is implemented by the module named on it;
// none is required (Sunny skips what is missing), so a filtered test boot still runs.
using UnityEngine;

namespace MusicRoom.Core
{
    /// <summary>Room: the calm celebration after the first song (sunny-tour.md 2.6): the fire flares, the lamps glow warmer,
    /// snow starts falling past the window, a soft harp chord rings. No pop-up, no confetti.</summary>
    public interface IRoomMoments
    {
        void Celebrate();
        /// <summary>A celebration is still running (its lights easing back).</summary>
        bool Celebrating { get; }
        int Celebrations { get; }
        /// <summary>Its harp chord is being plucked (those plucks are not the player's).</summary>
        bool Chording { get; }
    }

    /// <summary>Room: the switch levers Sunny points at ("switch.fire", "switch.wind", "switch.snow"; up: the panel's).</summary>
    public interface ITourAnchors
    {
        bool TryGetAnchor(string id, out Vector3 world, out Vector3 up);
    }

    /// <summary>Lessons: which lessons the player finished (persisted) and what comes next (tutorials-audit.md B6).</summary>
    public interface ILessonProgress
    {
        bool IsDone(string lessonId);
        /// <summary>Fewest mistakes in a finished run, -1 when never finished.</summary>
        int BestMistakes(string lessonId);
        /// <summary>The next lesson of the same instrument in the menu's order, null after the last.</summary>
        string NextLessonId(string lessonId);
        int DoneCount { get; }
    }

    /// <summary>Lessons: what Sunny reads from (and switches on) a running lesson: the target to point the ghost hand at,
    /// and "any key plays the next note" (the first run's fallback, sunny-tour.md 2.2 S2 / S3).</summary>
    public interface ILessonLive
    {
        bool Forgiving { get; set; }
        /// <summary>The first target of the event the player is on: where to press (world), the surface's up, the
        /// instrument and the key's MIDI / the string's index. False when no lesson waits for a target.</summary>
        bool TryGetLiveTarget(out Vector3 world, out Vector3 up, out InstrumentKind kind, out int midiOrString);
        /// <summary>The target block is down on its key and waiting (the clock stops for the player).</summary>
        bool WaitingForPlayer { get; }
    }

    /// <summary>Lessons: a live play landed on the running lesson (Hit) or was a wrong note in reach (Hit false). Quiet
    /// mistakes are published too (the first run counts them for "Nice sound! Now try the glowing one.").</summary>
    public struct TutorialHitEvent { public string LessonId; public int EventIndex; public bool Hit; }

    /// <summary>Menu: what Sunny points at and lights (sunny-tour.md 7.2). Row ids are stable strings (Menu's MenuRowIds):
    /// lessons, instruments, arrange, recordings, room, setup, guide, exit, playHere, playRoom, done, reset, back, list,
    /// next, replay, courses, tour, tips, tipsOn, layers, inst.piano ...</summary>
    public interface IMenuGuideHooks
    {
        /// <summary>A shown row's centre on the face (world), the face's normal toward the player and its size (m).</summary>
        bool TryGetRowRect(string rowId, out Vector3 center, out Vector3 normal, out Vector2 size);
        /// <summary>Light one row of the shown screen with the hover highlight (a fingertip's hover wins; no repaint);
        /// null clears. False when the row is not on the screen.</summary>
        bool Spotlight(string rowId);
        /// <summary>How far the shown list can scroll (px), 0 when it fits or no list is shown.</summary>
        float ListMaxScroll { get; }
        float ListScroll { get; }
    }

    /// <summary>Menu: the start panel was answered.</summary>
    public struct StartChoiceEvent { public bool InMyRoom; }

    // MenuRowEvent (a row was poked) is the platform's (Core/App/Events.cs), the same fields: published before the row's
    // action with the screen it was poked on; Confirming: Exit or Delete asks for a second poke.

    /// <summary>Guide: the menu's Sunny screen (sunny-tour.md 3.3): the tour again, the tips list, the tips switch.</summary>
    public interface IGuideTour
    {
        void StartTour();
        int TipCount { get; }
        string TipId(int i);
        string TipTitle(int i);
        bool TipHeard(int i);
        int TipsHeard { get; }
        /// <summary>Sunny comes and plays this tip now (the Tips list).</summary>
        void PlayTip(string tipId);
        /// <summary>"Tips while I play" (musicroom.tipsOn): off, Sunny only speaks when asked (blocking tips still help).</summary>
        bool TipsEnabled { get; set; }
    }

    /// <summary>Guide: a fingertip poked Sunny (skip; sunny-tour.md 4.5).</summary>
    public struct SunnyPokeEvent { public string StepOrTipId; }

    public enum StepOutcome : byte { Done = 0, GaveUp, Skipped }

    /// <summary>Guide: a first-run step ended.</summary>
    public struct GuideStepEvent { public string StepId; public StepOutcome Outcome; public float At; }

    /// <summary>Guide: a tip started, or ended (Seen: marked heard).</summary>
    public struct GuideTipEvent { public string TipId; public bool Started; public bool Seen; }
}
