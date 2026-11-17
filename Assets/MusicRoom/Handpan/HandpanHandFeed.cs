// What the module feeds HandpanPlay each frame, from the app's real input (handpan.md 6.2; review items 8 and 15):
//   t      = the frame clock (App.Clock.Now, the predicted pose's frame), dt = the frame's delta;
//   tips   = Pointer.Tip of the hand's five pointers (index, middle, ring, little, thumb: the PointerSlot order), the
//            predicted pose that moves every frame in Unity, with the tip joint's capsule radius x handScale;
//   palm   = HandMath.Palm on HandInput's HandFrame (centre of wrist, index and little proximals; normal out of the palm);
//   flags  = HandFrame.isTracked / isHighConfidence / poseInferred / systemGesture; the desktop mouse hand never plays.
// NEVER HandFrame.sampleInterval or sampleTime: that is the tracker's interval (about 33 ms at the LOW rate) and would read
// every speed about 3 times too low. Allocation-free.
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Handpan
{
    public static class HandpanHandFeed
    {
        public static void Fill(HandpanFrame f, PointerInput input, double nowS, float dt)
        {
            f.T = nowS; f.Dt = dt;
            for (int hi = 0; hi < 2; hi++)
            {
                HandpanHandInput I = f.Hands[hi];
                HandState h = input != null ? input.Hands[hi] : null;
                HandFrame hf = h != null && input.Source != null ? input.Frame(h) : null;
                if (h == null || hf == null || h.IsMouse || !h.Connected || !hf.isTracked)
                {
                    I.Tracked = false; I.HighConfidence = false; I.Inferred = false; I.SystemGesture = false;
                    continue;
                }
                I.Tracked = true;
                I.HighConfidence = hf.isHighConfidence;
                I.Inferred = hf.poseInferred;
                I.SystemGesture = hf.systemGesture;
                for (int k = 0; k < 5; k++)
                {
                    Pointer p = h.Pointers[k];
                    I.Tips[k] = D3.From(p.Tip);
                    int joint = p.Joint;
                    float r = joint >= 0 && joint < HandFrame.JointCount ? hf.radius[joint] : 0f;
                    if (r <= 0f && joint >= 0 && joint < HandFrame.JointCount) r = HandRestPose.Radius[joint] * hf.handScale;
                    I.Radii[k] = r > 0f ? r : 0.0076;
                }
                if (HandMath.Palm(hf, out Vector3 c, out Vector3 n)) { I.PalmCentre = D3.From(c); I.PalmNormal = D3.From(n); }
                else { I.PalmCentre = D3.From(hf.Get(HandJoint.Wrist)); I.PalmNormal = new D3(0, 1, 0); }
            }
        }
    }
}
