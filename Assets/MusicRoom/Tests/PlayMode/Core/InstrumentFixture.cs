// The conformance suite's per-instrument hook (platform.md 12.2 check 7): each instrument provides ONE fixture class
// I<Kind>Fixture... in its own PlayMode test folder (Tests/PlayMode/<X>/<X>Fixture.cs) that scripts one live onset with
// the scripted hands, with the instrument at the seat. The suite finds the fixtures by reflection over this assembly.
// PlaceholderFixture serves every phase-0 stub (a fingertip down through the top face; the contactless one: the index tip
// entering its air field); the instrument agents replace their stub's fixture with their own.
using System;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode
{
    public interface IInstrumentFixture
    {
        InstrumentKind Kind { get; }
        /// <summary>One live onset of the instrument (at the seat, alone): script the hands and step frames with c.Frame().</summary>
        void Strike(FixtureContext c);
    }

    /// <summary>What a fixture gets: the boot, frame stepping on the scripted clocks, a pointing-hand pose helper.</summary>
    public sealed class FixtureContext
    {
        public TestBoot Boot;
        public App App => App.Current;
        public Action Frame;
        public void Frames(int n) { for (int i = 0; i < n; i++) Frame(); }

        /// <summary>A right hand pointing down with its index tip at a point and the other tips 30 cm up (only the index touches).</summary>
        public static HandPose IndexAt(Vector3 tip)
        {
            HandPose p = HandPose.OpenFacing(HandSide.Right, tip + Vector3.up * 0.15f, Vector3.down, Vector3.forward).WithPointing().WithIndexTipAt(tip);
            foreach (HandJoint j in new[] { HandJoint.ThumbTip, HandJoint.MiddleTip, HandJoint.RingTip, HandJoint.LittleTip }) p.Position[(int)j] += Vector3.up * 0.3f;
            return p;
        }

        public void Index(Vector3 tip) => Boot.Hands.SetPose(HandSide.Right, IndexAt(tip));

        /// <summary>The right index from <paramref name="from"/> to <paramref name="to"/> in one frame (a fast stroke), then away.</summary>
        public void Stroke(Vector3 from, Vector3 to)
        {
            Index(from); Frames(3);
            Index(to); Frame();
            Index(from + Vector3.up * 0.3f); Frames(12);
        }

        public static IReadOnlyList<IInstrumentFixture> All()
        {
            var list = new List<IInstrumentFixture>();
            foreach (Type t in typeof(IInstrumentFixture).Assembly.GetTypes())
                if (!t.IsAbstract && typeof(IInstrumentFixture).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
                    list.Add((IInstrumentFixture)Activator.CreateInstance(t));
            return list;
        }

        public static IInstrumentFixture For(InstrumentKind k) => All().FirstOrDefault(f => f.Kind == k);
    }

    /// <summary>The phase-0 stubs' fixture: a fingertip down through region 2 of the top face (PlaceholderInstrument's
    /// lesson landing); a contactless stub: the index tip entering its air field from outside.</summary>
    public abstract class PlaceholderFixture : IInstrumentFixture
    {
        public abstract InstrumentKind Kind { get; }

        public void Strike(FixtureContext c)
        {
            PlaceholderInstrument p = null;
            foreach (IInstrument i in c.App.Instruments) if (i.Kind == Kind) p = i as PlaceholderInstrument;
            if (p == null) throw new InvalidOperationException(Kind + " is not a PlaceholderInstrument: write its own fixture");
            InstrumentDescriptor d = p.Descriptor;
            if (d.Has(InstrumentTraits.Contactless))
            {
                FootBox f = d.Seat.Boxes.First(b => b.Air);
                Vector3 inside = p.transform.TransformPoint(new Vector3(f.Cx, (f.Y0 + f.Y1) * 0.5f - d.Seat.Home.y, f.Cz));
                Vector3 outside = inside + p.transform.right * (f.Hx + 0.25f) + Vector3.up * 0.4f;
                c.Index(outside); c.Frames(3);
                c.Index(inside); c.Frames(2);
                c.Index(outside); c.Frames(12);
                return;
            }
            p.TryGetLanding(new LessonTarget { i = 2 }, out Pose landing, out _, out _);
            Vector3 up = landing.rotation * Vector3.up;
            c.Stroke(landing.position + up * 0.04f, landing.position - up * 0.005f);
        }
    }
}
