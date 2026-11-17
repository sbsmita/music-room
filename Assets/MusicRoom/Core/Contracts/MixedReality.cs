// "Play in my room" contract (spec 03, spec 07 section 9). The MR module implements IMixedReality and publishes
// MixedRealityEvent; the Menu shows "Play in my room" / "Music room" and "Set up my room" from it.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace MusicRoom.Core
{
    public enum MrStatus { Idle = 0, Loading, Loaded, NoPermission, NoRooms, NotAvailable, Failed }

    public interface IMixedReality
    {
        /// <summary>Passthrough can be shown on this device (XR up, init did not fail).</summary>
        bool Available { get; }
        bool Active { get; }
        /// <summary>The player is placing the piano (pinch reticle): no instrument is ticked (the menu
        /// included), Layout's grab, two-hand summon and page turn pause. MR clears it in its EndFrame phase of the frame the
        /// piano lands, so that frame's Grab pass is skipped too (placed this frame).</summary>
        bool IsPlacing { get; }
        /// <summary>Layout calls this for a dwell-complete pinch in empty air (no rig under it, outside
        /// every zone) while Active and the piano is in the layout; Layout hides the other rigs first. IsPlacing stays true
        /// until the piano lands, then MR calls ILayoutService.PlaceAroundPiano (which shows the rigs again).</summary>
        void StartPlacing();
        /// <summary>Ends placing without a landing (no-op when not placing): the hidden rigs and the menu come back through
        /// ILayoutService.RestoreAfterPlacing. MR calls it itself on its timeout, on HMD unmount, input focus loss and
        /// recentre; the menu calls it for the summon gesture.</summary>
        void CancelPlacing(string why) { }
        MrStatus Status { get; }
        /// <summary>A Space Setup exists (rooms loaded); false after NoRooms.</summary>
        bool HasSpaceSetup { get; }
        /// <summary>Enter: passthrough on, room hidden, fire/wind off, scene permission + load; on the first posed frame
        /// MR calls ILayoutService.ArrangeAroundPlayer(true) (no room box, no fitSeat while Active: every instrument in
        /// front of the player on the real floor, with the player's comfort offsets) and poses its catcher at
        /// ILayoutService.Anchor. No saved anchor or earlier pose is brought back.</summary>
        Task Enter();
        /// <summary>Exit: world lock off, scene cleared, room shown, VR layout restored
        /// (ILayoutService.RestoreHomes, then ArrangeAroundPlayer(true)).</summary>
        void Exit();
        /// <summary>Menu row "Set up my room": the system Space Setup, then a reload. Needs the headset (no-op otherwise).</summary>
        Task RunSpaceSetup();
        /// <summary>The real floor's height in the world while Active: the scene's floor when a room is loaded, else the
        /// tracking space's floor (0). Layout stands every instrument on it.</summary>
        float FloorHeight => 0f;
        /// <summary>Best real table for the piano (TableFinder), else false. Entering mixed reality no longer puts the
        /// piano on it by itself (the instruments open in front of the player on the floor); the point-and-pinch placer
        /// and MrModule.TableOnEnter still use it.</summary>
        bool TryGetPianoPose(out Pose pose, out bool onTable);
        /// <summary>platform.md 9.2: the pose for the lead instrument (ILayoutService.Lead): on a real table when its
        /// descriptor's Mr.OnTable allows (lifted by Mr.SurfaceLift), else on the floor in front of the player. The default
        /// asks TryGetPianoPose for the piano and says false for the others.</summary>
        bool TryGetLeadPose(InstrumentKind lead, out Pose pose, out bool onTable)
        {
            if (lead == InstrumentKind.Piano) return TryGetPianoPose(out pose, out onTable);
            pose = Pose.identity; onTable = false; return false;
        }
    }
}
