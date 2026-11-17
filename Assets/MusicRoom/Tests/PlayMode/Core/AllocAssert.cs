// "Allocates nothing" for code measured in a booted app. Unity's GC.Alloc recorder (Is.Not.AllocatingGCMemory) now and then
// counts an allocation the measured code never made: in full runs a bare field read failed it while the managed heap
// counter of the main thread showed 0 B (the app's background loads and workers run alongside). So the check is: the
// exact code once first (first-call costs: JIT, lazy statics), then the main thread's own byte counter must not move
// (exact, deterministic), and the recorder must see nothing in one of three tries (a real allocation fails all three).
using System;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode
{
    public static class AllocAssert
    {
        public static void Nothing(TestDelegate measured, string what = null)
        {
            measured();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            measured();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
            NUnit.Framework.Assert.That(bytes, NUnit.Framework.Is.Zero, (what ?? "the measured code") + ": bytes allocated on the main thread");
            for (int k = 0; k < 2; k++)
                if (Is.Not.AllocatingGCMemory().ApplyTo(measured).IsSuccess) return;
            NUnit.Framework.Assert.That(measured, Is.Not.AllocatingGCMemory(), what);
        }
    }
}
