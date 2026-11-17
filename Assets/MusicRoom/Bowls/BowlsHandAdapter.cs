// From the app's hand data to BowlsHands' input (BowlsInstrument.Tick calls Fill for each hand every frame). The hand
// logic runs in the rig's own frame (the bowls at their layout positions, yaw 0), so a gliding, lifted or turned rig
// needs nothing else: positions, velocities and the palm normal are taken into the rig frame here.
// HandFrame.position[] is already the runtime's display-time prediction (Predicted = true) and tipVelocity[] is measured
// between tracker samples (world m/s), as BowlsHands expects; the tip spheres use the joint radii (the 02-meta-hands
// capsule table: 7.6 mm index at hand scale 1, 9.8 mm thumb). A hand whose pose is inferred (wide motion mode, not seen by
// the cameras), in a system gesture, or untracked gives no tips: BowlsHands releases its contacts after the 0.12 s loss
// hold and never strikes from it (02-meta-hands section 6). The mouse hand never plays (the desktop path does). A hand
// that holds the stick plays only through it (the platform mutes a holding hand's fingertips).
using MusicRoom.Hands;
using Unity.Mathematics;
using UnityEngine;

namespace MusicRoom.Bowls
{
    public static class BowlsHandAdapter
    {
        static readonly int[] TipJoint = { (int)HandJoint.ThumbTip, (int)HandJoint.IndexTip, (int)HandJoint.MiddleTip, (int)HandJoint.RingTip, (int)HandJoint.LittleTip };

        static double3 D(Vector3 v) => new double3(v.x, v.y, v.z);

        /// <summary>Fills `into` from a hand frame, in the rig frame (worldToRig). holdsPuja: this hand holds the stick, whose
        /// ends (world) are suedeEnd / plainEnd.</summary>
        public static void Fill(HandFrame f, bool isMouse, BowlsHandInput into, Matrix4x4 worldToRig, bool holdsPuja = false, Vector3 suedeEnd = default, Vector3 plainEnd = default)
        {
            bool ok = f != null && f.isTracked && !f.systemGesture && !f.poseInferred && !isMouse;
            bool fingers = ok && !holdsPuja;
            into.Tracked = ok;
            into.Predicted = true;
            into.SampleTime = f != null ? f.sampleTime : 0;
            into.HasTipVel = fingers;
            for (int k = 0; k < 5; k++)
            {
                into.TipValid[k] = fingers;
                if (!fingers) continue;
                int j = TipJoint[k];
                into.Tips[k] = D(worldToRig.MultiplyPoint3x4(f.position[j]));
                into.TipVel[k] = D(worldToRig.MultiplyVector(f.tipVelocity[k]));
                float r = f.radius[j];
                into.TipR[k] = r > 0.002f ? r : (k == 0 ? 0.0098 : BowlsHands.IX.TIP_R) * Mathf.Max(0.5f, f.handScale);
            }
            into.HasPalm = fingers;
            if (fingers)
            {
                Vector3 c = f.position[(int)HandJoint.Palm], n = f.rotation[(int)HandJoint.Palm] * Vector3.down;   // out of the palm
                into.PalmC = D(worldToRig.MultiplyPoint3x4(c)); into.PalmN = D(worldToRig.MultiplyVector(n).normalized);
            }
            into.HasTool = ok && holdsPuja;
            if (into.HasTool) { into.ToolA = D(worldToRig.MultiplyPoint3x4(suedeEnd)); into.ToolB = D(worldToRig.MultiplyPoint3x4(plainEnd)); }
        }
    }
}
