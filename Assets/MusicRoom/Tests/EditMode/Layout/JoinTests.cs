// Joining My set (platform.md 4.2, 12.1 JoinTests): the eight reference joins (vectors "joins": add, replace the least
// recently played member that makes room, ties to the smaller instrument, or the new one alone), on the reference's own
// instruments. Then the carousel (platform.md 4.3, CarouselTests): the registry's Next +-1 through all eight, and the
// reference's skips with only the classic three loaded.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;
using V = MusicRoom.Tests.Layout.PlatformVectors;

namespace MusicRoom.Tests.Layout
{
    public class JoinTests
    {
        [Test]
        public void TheSixReferenceJoins()
        {
            SeatPlanner planner = V.Planner();
            List<object> joins = V.Arr(V.Root, "joins");
            Assert.That(joins.Count, Is.EqualTo(6));
            foreach (object j in joins)
            {
                List<InstrumentKind> set = V.Kinds(V.Arr(j, "set"));
                InstrumentKind add = V.KindOf(V.Str(j, "add"));
                var ago = new Dictionary<InstrumentKind, double>();
                foreach (KeyValuePair<string, object> kv in V.Obj(j, "lastPlayed")) ago[V.KindOf(kv.Key)] = System.Convert.ToDouble(kv.Value);
                string name = string.Join("+", V.Arr(j, "set")) + " + " + V.Str(j, "add");
                SeatPlanner.JoinResult r = planner.Join(set, add, k => ago.TryGetValue(k, out double s) ? s : double.PositiveInfinity);
                string action = V.Str(j, "action");
                JoinAction want = action == "add" ? JoinAction.Add : action == "replace" ? JoinAction.Replace : action == "single" ? JoinAction.Alone : JoinAction.Already;
                Assert.That(r.Action, Is.EqualTo(want), name + ": action");
                if (V.Has(j, "replaced")) Assert.That(InstrumentIds.Of(r.Replaced.Value), Is.EqualTo(V.Str(j, "replaced")), name + ": replaced");
                else Assert.That(r.Replaced.HasValue, Is.False, name + ": nothing replaced");
                var got = new List<string>();
                foreach (SeatPlanner.Member m in r.Plan.Members) got.Add(InstrumentIds.Of(m.Kind) + "@" + m.Slot);
                var expect = new List<string>();
                foreach (object o in V.Arr(j, "result")) expect.Add((string)o);
                Assert.That(string.Join(", ", got), Is.EqualTo(string.Join(", ", expect)), name + ": the planned set");
            }
        }

        [Test]
        public void JoiningAMemberIsAlready()
        {
            SeatPlanner planner = V.Planner();
            SeatPlanner.JoinResult r = planner.Join(new[] { InstrumentKind.Piano, InstrumentKind.Drums }, InstrumentKind.Drums);
            Assert.That(r.Action, Is.EqualTo(JoinAction.Already));
        }
    }

    public class CarouselTests
    {
        static InstrumentRegistry Loaded(params string[] ids)
        {
            InstrumentRegistry reg = InstrumentRegistry.Discover();
            foreach (string id in ids) reg.MarkLoaded(V.KindOf(id));
            return reg;
        }

        [Test]
        public void NextStepsThroughAllSevenBothWays()
        {
            InstrumentRegistry reg = Loaded(V.Carousel);
            var order = new List<string>();
            foreach (InstrumentKind k in reg.Carousel) order.Add(reg.IdOf(k));
            Assert.That(string.Join(",", order), Is.EqualTo(string.Join(",", V.Carousel)), "piano, harp, bowls, handpan, marimba, hand drums, drums");
            foreach (object s in V.Arr(V.Obj(V.Root, "carousel"), "steps"))
            {
                InstrumentKind from = V.KindOf(V.Str(s, "id"));
                Assert.That(reg.IdOf(reg.Next(from, 1)), Is.EqualTo(V.Str(s, "next")), V.Str(s, "id") + " +1");
                Assert.That(reg.IdOf(reg.Next(from, -1)), Is.EqualTo(V.Str(s, "prev")), V.Str(s, "id") + " -1");
            }
            Assert.That(reg.Next(InstrumentKind.Piano, 1), Is.EqualTo(InstrumentKind.Harp), "the first swipe from the piano goes to the harp");
        }

        [Test]
        public void NextSkipsInstrumentsThatAreNotLoaded()
        {
            foreach (object s in V.Arr(V.Obj(V.Root, "carousel"), "skip"))
            {
                var loaded = new List<string>();
                foreach (object o in V.Arr(s, "loaded")) loaded.Add((string)o);
                InstrumentRegistry reg = Loaded(loaded.ToArray());
                InstrumentKind from = V.KindOf(V.Str(s, "from"));
                int dir = (int)V.Num(s, "dir");
                Assert.That(reg.IdOf(reg.Next(from, dir)), Is.EqualTo(V.Str(s, "expect")), $"{V.Str(s, "from")} {dir:+0;-0} with {string.Join(",", loaded)}");
            }
        }
    }
}
