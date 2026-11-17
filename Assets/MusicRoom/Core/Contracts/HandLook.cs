// The hands' look in "Play in my room": the hands are drawn (Skin, as in "Play here") unless the player chose "Real hands"
// in the menu, which shows the real hands through a passthrough cut-out. Core's ContactVisuals implements IHandLook (App
// registers it) and keeps the choice in AppSettings.MrRealHands.
// (Until 2026-11-10 this file also held the grab handles' contract: the small gold knobs are gone; instruments move with
// the "Move instruments" setting, Core/Contracts/Arrange.cs.)
namespace MusicRoom.Core
{
    /// <summary>How the hands look in "Play in my room" (the menu's "Real hands" row).</summary>
    public interface IHandLook
    {
        /// <summary>The player's choice, persisted: true = real hands through the cut-out, false = the drawn hands (default).</summary>
        bool RealHandsInMr { get; set; }
        /// <summary>The cut-out is showing now (mixed reality is on and the player chose real hands).</summary>
        bool CutoutActive { get; }
    }
}
