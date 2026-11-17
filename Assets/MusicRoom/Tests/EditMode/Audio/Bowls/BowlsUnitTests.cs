// The bowls unit's behaviour around its DSP (bowls.md 11.2.4; review item 14): lanes (the live hand takes a bowl from a
// loop, a second loop lane waits, a Release from a non-owner is ignored, the waiting lane re-seats after the live lift),
// the Share work cap, Reset / Silence, the UnitSlot hook body (phase B's RenderBowls) with a stand-in slot, LateDrop on
// strikes only, and no allocation in the render or the main-thread API.
using System;
using System.Runtime.InteropServices;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Audio
{
    public unsafe class BowlsUnitTests
    {
        const int SR = 48000;

        // ---------------------------------------------------------------- lanes (review item 14)

        [Test]
        public void LiveHandTakesTheBowlAndTheLoopWaits()
        {
            using var e = new BowlsRig(1);
            e.Pickup(1, 0, 0, true);
            double R = e.Design[1].R;
            // the loop (lane 1) rubs from 0 to 2 s; the live hand (lane 0) rubs from 0.6 to 1.2 s; a second loop (lane 2)
            // tries at 0.3 s; the loop's own Release at 0.9 s must not cut the live rub
            for (int k = 0; k < 180; k++) e.Contact(1, 0, 3.5, 0.3, 0.3 / R * k / 90.0, 0.3 / R, 1, 1, 0, k * 533);
            e.Release(1, (long)(0.9 * SR), 1);
            for (int k = 0; k < 10; k++) e.Contact(1, 0, 3, 0.3, 1.0, 0.3 / R, 1, 2, 0, (long)(0.3 * SR) + k * 533);
            for (int k = 0; k < 54; k++) e.Contact(1, 0, 4, -0.25, 2.0 - 0.25 / R * k / 90.0, -0.25 / R, 1, 0, 0, (long)(0.6 * SR) + k * 533);
            e.Release(1, (long)(1.2 * SR), 0);
            int lane06 = -2, lane09 = -2, lane13 = -2, lane15 = -2;
            for (int b = 0; b < 2 * SR / 256; b++)
            {
                e.RenderBlock(256);
                double t = e.Now / (double)SR;
                BowlState* s = e.B(1);
                if (lane06 == -2 && t > 0.65) lane06 = s->StLane;
                if (lane09 == -2 && t > 0.95) lane09 = s->StLane;
                if (lane13 == -2 && t > 1.30) lane13 = s->StLane;
                if (lane15 == -2 && t > 1.50) lane15 = s->StLane;
            }
            BowlState* st = e.B(1);
            Assert.That(st->StPreempts, Is.EqualTo(1), "the live hand preempts the loop once");
            Assert.That(st->StLaneDrops, Is.GreaterThan(10), "the second loop lane and the waiting loop's frames are dropped");
            Assert.That(lane06, Is.EqualTo(0), "the live hand holds the stick at 0.65 s");
            Assert.That(lane09, Is.EqualTo(0), "the loop's Release at 0.9 s did not cut the live rub");
            Assert.That(lane13 == -1 || lane13 == 1, Is.True, $"after the live lift the stick is free or the loop re-seated ({lane13})");
            Debug.Log($"[BOWLS] lanes: preempts {st->StPreempts}, drops {st->StLaneDrops}, lane at 0.65/0.95/1.3/1.5 s: {lane06}/{lane09}/{lane13}/{lane15}");
        }

        [Test]
        public void ReleaseFromAnotherLaneIsIgnored()
        {
            using var e = new BowlsRig(1);
            for (int k = 0; k < 40; k++) e.Contact(2, 0, 3, 0.3, 0, 4.8, 1, 0, 0, k * 533);
            e.Release(2, 5000, 3);
            e.Render(22000);
            Assert.That(e.B(2)->On, Is.EqualTo(1), "a lane-3 Release does not release lane 0's stick");
            e.Release(2, e.Now, 0);
            e.Render(4000);
            Assert.That(e.B(2)->On, Is.EqualTo(0), "its own Release does");
        }

        // ---------------------------------------------------------------- the work cap

        [Test]
        public void ShareCapsTheWorkWithoutAClick()
        {
            float own = 0, step = 0;
            int w1 = 0, w4 = 0;
            using (var e = new BowlsRig(1))
            {
                for (int b = 0; b < 3; b++) { e.Pickup(b, 0.3, 0, true); e.Strike(b, BowlStrikeKind.Tap, 1.2, 0.3 + b, 1, 10 + b); }
                var y = new float[9600];
                for (int k = 0; k < 37; k++)
                {
                    if (k == 20) e.Share = 0.4f;
                    e.RenderBlock(256);
                    Array.Copy(e.Mix, 0, y, k * 256, 256);
                    if (k == 18) w1 = e.S->Work;
                    if (k == 25) w4 = e.S->Work;
                }
                own = NullTest.MaxStep(y, 2600, 20 * 256 - 2);
                step = NullTest.MaxStep(y, 20 * 256 - 2, 20 * 256 + 4);
            }
            Debug.Log($"[BOWLS] Share 0.4: work {w1} -> {w4} ({1 - (double)w4 / w1:P0} less); step at the change {step:E2} vs own max {own:E2}");
            Assert.That(w4, Is.LessThanOrEqualTo(w1 / 2), "Share 0.4 at least halves the work");
            Assert.That(step, Is.LessThanOrEqualTo(own * 1.01f), "no click at the change");
        }

        // ---------------------------------------------------------------- the hook body (phase B)

        [Test]
        public void UnitHookBodyResetsRendersAndChunks()
        {
            using var e = new BowlsRig(1);
            int frames = 2048;
            var slot = (UnitSlot*)UnsafeUtility.Malloc(sizeof(UnitSlot), 64, Unity.Collections.Allocator.Persistent);
            UnsafeUtility.MemClear(slot, sizeof(UnitSlot));
            var outp = (float*)UnsafeUtility.Malloc(4 * frames, 64, Unity.Collections.Allocator.Persistent);
            var due = (UnitEvent*)UnsafeUtility.Malloc(sizeof(UnitEvent) * 4, 64, Unity.Collections.Allocator.Persistent);
            try
            {
                slot->State = e.S; slot->Channels = 1; slot->Out = outp; slot->MaxFrames = frames; slot->Frames = frames; slot->Due = due;
                // two strikes: one in the first 1024 frames, one in the second chunk
                due[0] = new UnitEvent { At = 100, Offset = 100, C = BowlsCommands.Strike(0, BowlStrikeKind.Tap, 1, 0, 1) };
                due[1] = new UnitEvent { At = 1500, Offset = 1500, C = BowlsCommands.Strike(1, BowlStrikeKind.Tap, 1, 0, 1) };
                slot->DueCount = 2;
                for (int i = 0; i < frames; i++) outp[i] = 0;
                BowlsDsp.RenderUnit(slot, 1f);
                int first0 = -1; for (int i = 0; i < frames; i++) if (outp[i] != 0) { first0 = i; break; }
                Assert.That(first0, Is.InRange(100, 140), "the first strike lands at its offset");
                Assert.That(e.B(1)->Awake, Is.EqualTo(1), "the second chunk's strike played");
                // Reset (classic switch / UnitFlags.Reset): everything silent
                slot->Flags = UnitFlags.Reset; slot->DueCount = 0; slot->Frames = 256;
                for (int i = 0; i < frames; i++) outp[i] = 0;
                BowlsDsp.RenderUnit(slot, 1f);
                float pk = 0; for (int i = 0; i < 256; i++) pk = Math.Max(pk, Math.Abs(outp[i]));
                Assert.That(pk, Is.EqualTo(0f), "Reset silences every bowl");
                for (int b = 0; b < 3; b++) Assert.That(e.B(b)->Awake, Is.EqualTo(0));
                Assert.That(e.S->Resets, Is.EqualTo(1));
                // not ready yet (boot on a worker): nothing plays, nothing is read
                e.S->Ready = 0; slot->Flags = 0; slot->DueCount = 1; due[0] = new UnitEvent { At = 0, Offset = 0, C = BowlsCommands.Strike(0, BowlStrikeKind.Tap, 1, 0, 1) };
                BowlsDsp.RenderUnit(slot, 1f);
                Assert.That(e.S->NotReady, Is.EqualTo(1));
                Assert.That(e.B(0)->Awake, Is.EqualTo(0));
            }
            finally
            {
                UnsafeUtility.Free(slot, Unity.Collections.Allocator.Persistent);
                UnsafeUtility.Free(outp, Unity.Collections.Allocator.Persistent);
                UnsafeUtility.Free(due, Unity.Collections.Allocator.Persistent);
            }
        }

        [Test]
        public void OnlyStrikesCarryLateDrop()
        {
            Assert.That(BowlsCommands.Strike(0, BowlStrikeKind.Tap, 1, 0, 1).Flags & CommandFlags.LateDrop, Is.Not.EqualTo(0));
            Assert.That(BowlsCommands.Contact(0, 0, 3, 0.3, 0, 4, 1).Flags & CommandFlags.LateDrop, Is.EqualTo(0));
            Assert.That(BowlsCommands.Release(0).Flags & CommandFlags.LateDrop, Is.EqualTo(0));
            Assert.That(BowlsCommands.Damp(0, 3).Flags & CommandFlags.LateDrop, Is.EqualTo(0));
        }

        [Test]
        public void SilenceZeroesTheBowl()
        {
            using var e = new BowlsRig(1);
            e.Strike(1, BowlStrikeKind.Palm, 2, 0, 1, 0);
            e.Render(4800);
            Assert.That(e.B(1)->Awake, Is.EqualTo(1));
            e.Silence(1, e.Now);
            float[] y = e.Render(512, 1);
            Assert.That(y[300], Is.EqualTo(0f));
            for (int i = 0; i < 128; i++) Assert.That(e.B(1)->G[i] == 0f || i % 32 >= 8, Is.True, "state zeroed");
        }

        // ---------------------------------------------------------------- allocation

        /// <summary>The delegate runs twice first (Mono JIT, Burst's first direct call), then once under the recorder.</summary>
        static void NoAlloc(NUnit.Framework.TestDelegate d, string what)
        {
            d(); d();
            Assert.That(d, Is.Not.AllocatingGCMemory(), what);
        }

        sealed class ArraySink : IBowlsCommandSink
        {
            public readonly GraphCommand[] C = new GraphCommand[4096]; public int N;
            public void Post(in GraphCommand c) { if (N < C.Length) C[N++] = c; }
        }

        [Test]
        public void RenderAndApiAllocateNothing()
        {
            using var e = new BowlsRig(1);
            for (int k = 0; k < 40; k++) e.Contact(1, 0, 3, 0.3, 0, 4, 1, 0, 0, k * 533);
            e.Strike(0, BowlStrikeKind.Tap, 1, 0, 1, 100);
            e.RenderBlock(256);   // warm up (JIT)
            var due = (UnitEvent*)UnsafeUtility.Malloc(sizeof(UnitEvent) * 4, 64, Unity.Collections.Allocator.Persistent);
            var mix = (float*)UnsafeUtility.Malloc(4 * 256, 64, Unity.Collections.Allocator.Persistent);
            try
            {
                due[0] = new UnitEvent { At = 0, Offset = 10, C = BowlsCommands.Strike(2, BowlStrikeKind.Tap, 1, 0, 1) };
                BowlsDsp.Render(e.S, due, 1, 256, mix, 1200, 1f);
                NoAlloc(() => { BowlsDsp.Render(e.S, due, 1, 256, mix, 1200, 1f); }, "the managed render");
                NoAlloc(() => { BowlsDsp.RenderStrict(e.S, due, 1, 256, mix, 1200, 0.4f); }, "the Strict render");
                var sink = new ArraySink();
                var core = new BowlsSoundCore(e.S, sink);
                void Api()
                {
                    core.Strike(0, BowlStrikeKind.Tap, 1, 0.3f, 1);
                    core.Contact(1, BowlSurface.Outer, 3, 0.3f, 0.2f, 4, 1);
                    core.Release(1); core.Damp(2, 1); core.SetListener(0, 0.4f);
                    core.Tick(0.1);
                    float a = core.SingVelocity(0) + core.Level(1) + core.ContactLost(1) + core.PumpPower(2) + core.Mode3Velocity(0) + core.Pattern20(1).x;
                    if (a < -1) Debug.Log("never");
                }
                Api(); Api();   // JIT and static initialisers first
                sink.N = 0;
                NoAlloc(() => core.Strike(0, BowlStrikeKind.Tap, 1, 0.3f, 1), "Strike");
                NoAlloc(() => core.Contact(1, BowlSurface.Outer, 3, 0.3f, 0.2f, 4, 1), "Contact");
                NoAlloc(() => core.Release(1), "Release");
                NoAlloc(() => core.Damp(2, 1), "Damp");
                NoAlloc(() => core.SetListener(0, 0.4f), "SetListener");
                NoAlloc(() => core.Tick(0.1), "Tick");
                NoAlloc(() => { float a = core.SingVelocity(0) + core.Level(1) + core.ContactLost(1) + core.PumpPower(2) + core.Mode3Velocity(0); if (a < -1) sink.N = 0; }, "stats");
                NoAlloc(() => { var v = core.Pattern20(1); if (v.x < -1) sink.N = 0; }, "Pattern20");
            }
            finally
            {
                UnsafeUtility.Free(due, Unity.Collections.Allocator.Persistent);
                UnsafeUtility.Free(mix, Unity.Collections.Allocator.Persistent);
            }
        }
    }
}
