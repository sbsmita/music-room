// The one adapter between the music book and the rest of the app (PHASE-B-NOTES.md). Phase A ran on TodayHost (the rest's
// pose computed from the piano's rig, the Lessons module's concrete Tutorial); phase B runs on PlatformHost over the
// platform hooks: IPianoApi.MusicRestPose / MusicRestLocal / MusicRestLedge, ITutorial.CurrentEvent,
// IAudioEngine.AddOneShotPreset / PlayOneShot, IGuide.Tip, InstrumentSignalEvent.
using UnityEngine;

namespace MusicRoom.Sheet
{
    public interface ISheetHost
    {
        /// <summary>The piano's rig group: the book is parented under it (it moves, lifts and turns with the piano).</summary>
        Transform PianoRoot { get; }
        /// <summary>The rest's front face, bottom centre, in the piano frame (local up along the face, forward away from the
        /// player). The platform: IPianoApi.MusicRestLocal.</summary>
        Pose MusicRestLocal { get; }
        /// <summary>The ledge top along the rest's face (m). The platform: IPianoApi.MusicRestLedge.</summary>
        float MusicRestLedge { get; }
        /// <summary>The rest's world pose, or null while the piano is not at the seat (out of the layout, sliding in,
        /// displayed). The platform: IPianoApi.MusicRestPose.</summary>
        Pose? MusicRestPose { get; }
        /// <summary>The running lesson's event index the player is on, -1 when none runs. The platform: ITutorial.CurrentEvent.</summary>
        int CurrentEvent { get; }
        /// <summary>One leaf turning over in `seconds` at `level` (0..1), starting at audio time `when` (0 = now): the paper
        /// sound from the engine's one-shot pool (sheet.md 5.5).</summary>
        void PaperTurn(float seconds, float level, uint seed, double when);
        /// <summary>A Sunny tip line (IGuide.Tip): its seconds, or 0 when not said.</summary>
        float Tip(string lineId);
        /// <summary>A gesture signal ("sheet.turn", "sheet.title", "sheet.ribbon"): the platform's InstrumentSignalEvent.</summary>
        void Signal(string signal);
        /// <summary>The audio clock (s).</summary>
        double AudioNow { get; }
    }
}
