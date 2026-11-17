// The tour's tips as the platform's tip data (12-new-instruments/platform.md 8.1; the phase-B merge of the first five): one
// TipSpec per TourTips entry and one FeatureSpec per TourFeatures entry, discovered like every IGuideTipSource, so the
// platform's TipRunner is the ONE scheduler of every tip Sunny says (the tour's, the platform's six, each instrument's),
// the menu's Tips list and SunnyCoverageTests see them all, and the coverage rules run over the registry.
//   Trigger: Signal "tour.<tip id>" (TourTipRunner fires it straight into the runner from TourSignals; nothing else needs
//   to publish it), or Manual for the list-only and IGuide.Tip ones (tip_loop_start, the low-light hint, the appended
//   lines). Success: Signal "tour.<tip id>.done" (TourTipRunner learns it), or None. Anchor: "tour.<anchor>" (resolved by
//   TourTipRunner, which shows these tips itself: its world-anchored ghost hand, the success wait and the praise).
using System;
using MusicRoom.Core;

namespace MusicRoom.Guide
{
    public sealed class TourTipSource : IGuideTipSource
    {
        public const string Prefix = "tour.", DoneSuffix = ".done";

        public TipSpec[] Tips()
        {
            TourTip[] all = TourTips.All;
            var specs = new TipSpec[all.Length];
            for (int i = 0; i < all.Length; i++) specs[i] = SpecOf(all[i]);
            return specs;
        }

        public FeatureSpec[] Features()
        {
            TourFeature[] all = TourFeatures.All;
            var specs = new FeatureSpec[all.Length];
            for (int i = 0; i < all.Length; i++)
                specs[i] = all[i].Kind == TourFeatureKind.Passive ? new FeatureSpec(all[i].Id, all[i].PassiveReason) : new FeatureSpec(all[i].Id);
            return specs;
        }

        public static TipSpec SpecOf(TourTip t)
        {
            bool manual = t.Trigger == TourTrigger.Manual || t.Trigger == TourTrigger.Appended || t.Trigger == TourTrigger.Off;
            return new TipSpec
            {
                Id = t.Id,
                Title = t.Title,
                Lines = t.TextOnly != null ? Array.Empty<string>() : t.Lines,
                Again = t.TextOnly != null ? null : TourTips.AgainOf(t),
                TextOnly = t.TextOnly != null,
                Class = (TipClass)(byte)t.Class,
                Trigger = manual ? TipTrigger.Manual : TipTrigger.Signal,
                TriggerArg = manual ? null : Prefix + t.Id,
                Success = t.Success == TourSuccess.None ? TipSuccess.None : TipSuccess.Signal,
                SuccessArg = t.Success == TourSuccess.None ? null : Prefix + t.Id + DoneSuffix,
                Demo = t.Demo,
                Anchor = t.Anchor == TourAnchor.Sunny ? null : Prefix + AnchorName(t.Anchor),
                Covers = t.Covers,
                VrOnly = t.VrOnly,
            };
        }

        static string AnchorName(TourAnchor a)
        {
            string s = a.ToString();
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }
    }
}
