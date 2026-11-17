// Look and pinch (2026-11-13): head gaze picks a menu row or an instrument on display, a pinch chooses it. The Gaze module
// (Assets/MusicRoom/Gaze, the one assembly that references Meta's Interaction SDK) implements ILookAndPinch; the menu
// implements IGazeMenu so the gaze never needs the menu's internals and the menu never needs the SDK.
using UnityEngine;

namespace MusicRoom.Core
{
    /// <summary>What the head gaze is on (after its dwell).</summary>
    public enum GazeTargetKind : byte { None = 0, MenuRow, Instrument }

    /// <summary>The "Look and pinch" feature (Gaze module).</summary>
    public interface ILookAndPinch
    {
        /// <summary>The player's setting (AppSettings.LookAndPinch, musicroom.lookPinch, default On).</summary>
        bool On { get; set; }
        /// <summary>What the gaze is on now (None while off, not presenting, or nothing is looked at long enough).</summary>
        GazeTargetKind Target { get; }
        /// <summary>The instrument looked at (when Target is Instrument).</summary>
        InstrumentKind TargetInstrument { get; }
        /// <summary>Choices made by look and pinch since boot (tests, diagnostics).</summary>
        int Chosen { get; }
    }

    /// <summary>The menu's side of look and pinch (MenuModule). Row keys are only valid until the menu lays its rows out
    /// again (a key from an older layout is ignored).</summary>
    public interface IGazeMenu
    {
        /// <summary>The shown panel's row under a head ray: the hit, the row's key and the row's centre in the world (for the
        /// field-of-view rule). False while the panel is hidden, held, or the ray hits no row.</summary>
        bool GazeCast(in Ray ray, float maxDistance, out float distance, out Vector3 point, out Vector3 normal, out int rowKey, out Vector3 rowCentre);
        /// <summary>The row lights this frame with the poke's own hover look (a finger's hover wins).</summary>
        void GazeHover(int rowKey);
        /// <summary>A look and pinch on the row: fired on the menu's next tick through the same path as a poke. False when
        /// the key is stale or the panel is hidden.</summary>
        bool GazeSelect(int rowKey);
    }
}
