using System;
using System.Linq;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Core
{
    public class ModuleDiscoveryTests
    {
        [Test]
        public void FindsEveryFeatureModuleWithAscendingOrders()
        {
            var modules = ModuleDiscovery.CreateModules();
            var names = modules.Select(m => m.GetType().Name).ToList();
            foreach (var want in new[] { "AudioModule", "RoomModule", "PianoModule", "DrumsModule", "HarpModule", "LessonsModule", "GuideModule", "MenuModule", "MrModule", "LayoutModule" })
                Assert.That(names, Does.Contain(want));
            for (int i = 1; i < modules.Count; i++) Assert.That(modules[i].Order, Is.GreaterThanOrEqualTo(modules[i - 1].Order));
            Assert.That(modules.First(m => m.GetType().Name == "AudioModule").Order, Is.EqualTo(10));
            Assert.That(modules.Last().GetType().Name, Is.EqualTo("LayoutModule"), "Layout (100) collects the rigs last");
        }

        [Test]
        public void FilterDropsModules()
        {
            var modules = ModuleDiscovery.CreateModules(t => t.Name == "AudioModule");
            Assert.That(modules.Count, Is.EqualTo(1));
        }

        [Test]
        public void MonoBehavioursAreNeverDiscovered()
        {
            foreach (var t in ModuleDiscovery.FindTypes<IAppModule>())
                Assert.That(typeof(UnityEngine.Object).IsAssignableFrom(t), Is.False, t.FullName);
        }

        /// A module declared in a test assembly: discovery must ignore it; tests inject probes via BootOptions.ExtraModules.
        public sealed class DiscoveryProbeModule : AppModule { public override int Order => 15; }

        [Test]
        public void TestAssembliesAreNotScannedAndExtraModulesAreMergedByOrder()
        {
            Assert.That(ModuleDiscovery.CreateModules().Any(m => m is DiscoveryProbeModule), Is.False, "test assemblies are never scanned");
            Assert.That(ModuleDiscovery.MusicRoomAssemblies().Any(a => ModuleDiscovery.IsTestAssembly(a.GetName().Name)), Is.False);
            var probe = new DiscoveryProbeModule();
            var modules = ModuleDiscovery.CreateModules(null, new IAppModule[] { probe });
            int i = modules.IndexOf(probe);
            Assert.That(i, Is.GreaterThan(0), "after Audio (10)");
            Assert.That(modules[i - 1].Order, Is.LessThanOrEqualTo(15));
            Assert.That(modules[i + 1].Order, Is.GreaterThanOrEqualTo(15), "before Room (20)");
        }
    }

    public class EventBusTests
    {
        struct Ping { public int N; }

        [Test]
        public void PublishReachesSubscribersAndUnsubscribeStops()
        {
            var bus = new EventBus();
            int sum = 0;
            Action<Ping> h = p => sum += p.N;
            bus.Subscribe(h);
            bus.Publish(new Ping { N = 2 });
            bus.Publish(new Ping { N = 3 });
            bus.Unsubscribe(h);
            bus.Publish(new Ping { N = 100 });
            Assert.That(sum, Is.EqualTo(5));
            Assert.That(bus.HandlerCount<Ping>(), Is.EqualTo(0));
        }

        [Test]
        public void AThrowingSubscriberDoesNotStopTheOthers()
        {
            var bus = new EventBus();
            int second = 0, third = 0;
            bus.Subscribe<Ping>(p => throw new InvalidOperationException("first handler bug"));
            bus.Subscribe<Ping>(p => second += p.N);
            bus.Subscribe<Ping>(p => third += p.N);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("first handler bug"));
            bus.Publish(new Ping { N = 7 });
            Assert.That(second, Is.EqualTo(7), "the second subscriber still got the event");
            Assert.That(third, Is.EqualTo(7));
        }

        [Test]
        public void SubscribersMayChangeTheListWhilePublishing()
        {
            var bus = new EventBus();
            int a = 0, b = 0;
            Action<Ping> hb = p => b++;
            Action<Ping> ha = null;
            ha = p => { a++; bus.Unsubscribe(ha); bus.Subscribe(hb); };
            bus.Subscribe(ha);
            bus.Publish(new Ping());   // a runs, swaps itself for b (takes effect next publish)
            bus.Publish(new Ping());
            Assert.That(a, Is.EqualTo(1));
            Assert.That(b, Is.EqualTo(1));
            Assert.That(bus.HandlerCount<Ping>(), Is.EqualTo(1));
        }
    }

    public class SettingsStoreTests
    {
        [Test]
        public void AppSettingsReadAndWriteOnlyTheInjectedStore()
        {
            var store = new MemorySettingsStore();
            var s = new AppSettings(store);
            Assert.That(s.LayoutMode, Is.EqualTo(LayoutMode.All), "defaults");
            Assert.IsFalse(s.TourDone);
            s.LayoutMode = LayoutMode.Single;
            s.CurrentInstrument = InstrumentKind.Drums;
            s.TourDone = true;
            s.MoveInstruments = true;
            s.Save();
            Assert.That(store.GetString(AppSettings.KeyLayoutMode, null), Is.EqualTo("single"));
            Assert.That(store.GetString(AppSettings.KeyCurrentInstrument, null), Is.EqualTo("drums"));
            Assert.That(store.GetInt(AppSettings.KeyTourDone, 0), Is.EqualTo(1));
            Assert.That(store.Saves, Is.EqualTo(1));
            var again = new AppSettings(store);
            Assert.That(again.CurrentInstrument, Is.EqualTo(InstrumentKind.Drums));
            Assert.IsTrue(again.MoveInstruments, "Move instruments persists");
            Assert.That(store.GetInt(AppSettings.KeyMoveInstruments, 0), Is.EqualTo(1));
            Assert.IsFalse(new AppSettings(new MemorySettingsStore()).MoveInstruments, "off by default");
            Assert.IsFalse(new AppSettings(new MemorySettingsStore()).TourDone, "a fresh store is a first launch");
        }
    }

    public class ClockTests
    {
        [Test]
        public void StepCapsDtAndAccumulates()
        {
            var c = new Clock();
            c.Step(0.016f);
            c.Step(0.5f); // capped at 50 ms
            Assert.That(c.Dt, Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(c.Now, Is.EqualTo(0.066).Within(1e-6));
            Assert.That(c.NowMs, Is.EqualTo(66.0).Within(1e-3));
            Assert.That(c.Frame, Is.EqualTo(2));
        }
    }

    public class CoordsTests
    {
        [Test]
        public void RightHandedToUnityConventions()
        {
            Assert.That(Coords.FromRightHanded(0f, 0.72f, -0.45f), Is.EqualTo(new Vector3(0f, 0.72f, 0.45f)));          // piano home
            Assert.That(Coords.FromRightHanded(0.2f, 0f, 0.26f), Is.EqualTo(new Vector3(0.2f, 0f, -0.26f)));            // drums home
            Assert.That(Coords.YawDegFromRightHandedRad(-Mathf.PI / 2f), Is.EqualTo(90f).Within(1e-4f));                // drums yaw
            Assert.That(Coords.YawDegFromRightHandedRad(-83f * Mathf.Deg2Rad), Is.EqualTo(83f).Within(1e-4f));          // harp yaw
            var q = Coords.FromRightHanded(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));
            Assert.That(q.x, Is.EqualTo(-0.1f)); Assert.That(q.y, Is.EqualTo(-0.2f)); Assert.That(q.z, Is.EqualTo(0.3f)); Assert.That(q.w, Is.EqualTo(0.9f));
            // Heading: a forward of +x is yaw 90 in Unity.
            Assert.That(Coords.HeadingDeg(Vector3.right), Is.EqualTo(90f).Within(1e-4f));
            // right-handed YXZ (rotX 90deg) -> Unity Euler(-90, 0, 0): the ceiling's Euler in spec 07.
            var ceil = Coords.FromRightHandedEulerYXZ(Mathf.PI / 2f, 0f, 0f);
            Assert.That(Quaternion.Angle(ceil, Quaternion.Euler(-90f, 0f, 0f)), Is.LessThan(1e-3f));
        }

        [Test]
        public void MirrorZReversesWinding()
        {
            var tris = new[] { 0, 1, 2 };
            Coords.ReverseWinding(tris);
            Assert.That(tris, Is.EqualTo(new[] { 0, 2, 1 }));
        }
    }

    public class Mulberry32Tests
    {
        [Test]
        public void Seed1FirstFiveValuesMatchJs()
        {
            var r = new Mulberry32(1);
            double[] want = { 0.6270739405881613, 0.002735721180215478, 0.5274470399599522, 0.9810509674716741, 0.9683778982143849 };
            foreach (var w in want) Assert.That(r.Next(), Is.EqualTo(w).Within(1e-15));
        }

        [Test]
        public void JsRoundIsFloorPlusHalf()
        {
            Assert.That(Mulberry32.JsRound(2.5), Is.EqualTo(3));
            Assert.That(Mulberry32.JsRound(3.5), Is.EqualTo(4));
            Assert.That(Mulberry32.JsRound(-2.5), Is.EqualTo(-2));
        }
    }

    public class SeatFrameTests
    {
        [Test]
        public void PlacesHomesAroundTheHead()
        {
            var seat = new SeatFrame { Head = new Vector3(1f, 1.2f, 2f), YawDeg = 90f }; // facing +x
            Vector3 p = seat.Place(new Vector3(0f, 0.72f, 0.45f));                        // piano home: ahead
            Assert.That(p.x, Is.EqualTo(1.45f).Within(1e-4f));
            Assert.That(p.y, Is.EqualTo(0.72f).Within(1e-4f));
            Assert.That(p.z, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(seat.Yaw(83f), Is.EqualTo(173f).Within(1e-4f));
        }
    }
}
