// XR mode switching entry points (spec 01 section 3.3, 2.4): passthrough underlay on/off, snap turn about the head,
// rig moves for seat fitting. Everything here is a no-op-safe wrapper over OVRManager / OVRPassthroughLayer.
// Passthrough is started on demand (burst.md section 10.1): the app boots with OVRManager.isInsightPassthroughEnabled
// false and no OVRPassthroughLayer, which lets the VR room run at CPU level 4. StartPassthrough (entering "Play in my
// room") adds the layer and asks OVRManager to initialise passthrough (asynchronous: PassthroughPending until it is up),
// SetPassthrough shows / hides it instantly, StopPassthrough (leaving mixed reality) shuts it down again.
using UnityEngine;

namespace MusicRoom.Hands
{
    public static class XrMode
    {
        /// <summary>VR background #05070f. Alpha 1 in VR, 0 in mixed reality.</summary>
        public static readonly Color VrBackground = new Color32(0x05, 0x07, 0x0f, 0xff);
        public static readonly Color MrBackground = new Color(0f, 0f, 0f, 0f);

        public static bool PassthroughOn { get; private set; }
        /// <summary>StartPassthrough asked OVRManager for passthrough and StopPassthrough has not been called since.</summary>
        public static bool PassthroughStarted { get; private set; }
        /// <summary>Realtime (s) of the last StartPassthrough that had to initialise (the "[RIG] passthrough ready" log).</summary>
        static float s_startedAt = -1f;

        /// <summary>Passthrough can be shown now: XR is up and the initialisation has COMPLETED (while it is pending the
        /// underlay is not submitted yet, so hiding the room would show black; a failed init is never retried).</summary>
        public static bool PassthroughAvailable(RigBootstrap rig)
        {
            bool ok = rig != null && rig.IsXrActive && rig.Passthrough != null && OVRManager.IsInsightPassthroughInitialized();
            if (ok && s_startedAt >= 0f)
            {
                Debug.Log($"[RIG] passthrough ready {Time.realtimeSinceStartup - s_startedAt:F2} s after the start request");
                s_startedAt = -1f;
            }
            return ok;
        }

        /// <summary>The passthrough initialisation is still running, or was just requested and OVRManager has not picked
        /// the request up yet (its Update starts it): MR Enter waits for it, bounded.</summary>
        public static bool PassthroughPending(RigBootstrap rig) =>
            rig != null && rig.IsXrActive && rig.Passthrough != null
            && (OVRManager.IsInsightPassthroughInitPending()
                || (PassthroughStarted && !OVRManager.IsInsightPassthroughInitialized() && !OVRManager.HasInsightPassthroughInitFailed()));

        /// <summary>Passthrough is not running yet but StartPassthrough can start it: XR is up with a rig manager and no
        /// earlier initialisation failed (OVRManager never retries a failed one).</summary>
        public static bool PassthroughStartable(RigBootstrap rig) =>
            rig != null && rig.IsXrActive && rig.Manager != null && !OVRManager.HasInsightPassthroughInitFailed();

        /// <summary>Asks for passthrough (entering mixed reality): adds the hidden underlay layer to the rig when it has
        /// none and sets OVRManager.isInsightPassthroughEnabled, which initialises passthrough asynchronously on OVRManager's
        /// next Update. Returns true when it is pending or up (or already was). Without a runtime nothing happens.</summary>
        public static bool StartPassthrough(RigBootstrap rig)
        {
            if (!PassthroughStartable(rig)) return false;
            rig.EnsurePassthroughLayer();
            if (!PassthroughStarted && !OVRManager.IsInsightPassthroughInitialized()) s_startedAt = Time.realtimeSinceStartup;
            PassthroughStarted = true;
            rig.Manager.isInsightPassthroughEnabled = true;
            return true;
        }

        /// <summary>Leaving mixed reality: hides the underlay and lets OVRManager shut passthrough down (its next Update),
        /// so the VR room is back on a passthrough-free CPU budget. Safe without a runtime.</summary>
        public static void StopPassthrough(RigBootstrap rig)
        {
            if (PassthroughOn) SetPassthrough(rig, false);
            PassthroughStarted = false;
            s_startedAt = -1f;
            if (rig == null) return;
            if (rig.Passthrough != null) rig.Passthrough.hidden = true;
            if (rig.Manager != null) rig.Manager.isInsightPassthroughEnabled = false;
        }

        /// <summary>Instant switch with no re-init: toggle the layer's hidden flag, the clear colour's alpha and the
        /// boundary suppression. Returns false (and changes nothing) when passthrough is unavailable. Turning it off
        /// always works (and only touches the camera without a runtime).</summary>
        public static bool SetPassthrough(RigBootstrap rig, bool on)
        {
            if (rig == null) return false;
            if (on && !PassthroughAvailable(rig)) return false;
            if (on && rig.Manager != null) rig.Manager.isInsightPassthroughEnabled = true;   // set by StartPassthrough already
            if (rig.Passthrough != null) rig.Passthrough.hidden = !on;
            ApplyBackground(rig, on);
            if (rig.Manager != null && rig.IsXrActive) rig.Manager.shouldBoundaryVisibilityBeSuppressed = on;
            PassthroughOn = on;
            return true;
        }

        /// <summary>The head camera's clear: #05070f alpha 1 in VR and on the desktop, (0,0,0,0) in mixed reality.
        /// RigBootstrap applies the VR colour at start (the scene asset keeps alpha 0 for Meta's editor-time rule).</summary>
        public static void ApplyBackground(RigBootstrap rig, bool mixedReality)
        {
            var cam = rig != null ? rig.HeadCamera : null;
            if (cam == null) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = mixedReality ? MrBackground : VrBackground;
        }

        /// <summary>The user's "passthrough by default" preference (preselects "Play in my room").</summary>
        public static bool PassthroughRecommended(RigBootstrap rig) => rig != null && rig.IsXrActive && OVRManager.IsPassthroughRecommended();

        /// <summary>Snap turn: rotate the rig, then restore the head position = a rotation about the head.
        /// Positive degrees turn the world clockwise seen from above (the player looks further right).</summary>
        public static void SnapTurn(RigBootstrap rig, float degrees)
        {
            if (rig == null || rig.Head == null) return;
            rig.RigRoot.RotateAround(rig.Head.position, Vector3.up, degrees);
        }

        /// <summary>Seat fit (fitSeat): move the whole rig by a world delta.</summary>
        public static void MoveRig(RigBootstrap rig, Vector3 delta)
        {
            if (rig == null) return;
            rig.RigRoot.position += delta;
        }
    }
}
