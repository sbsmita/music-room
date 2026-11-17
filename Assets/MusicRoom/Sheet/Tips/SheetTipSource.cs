// The book's Sunny tips as platform data (IGuideTipSource, found by reflection like PlatformTips): one TipSpec per line of
// SheetTipTable (the headset wording, and the "Click" wording the module asks for on a desktop), all TipTrigger.Manual
// (SheetTips decides the moment: the book in view, the contents open, a lesson's countdown or end, far from the contents
// after a manual turn) and learned by the book's own signals (InstrumentSignalEvent "sheet.title" / "sheet.turn" /
// "sheet.ribbon"). Demos point or poke at the book's anchors (SheetModule: IGuideAnchors).
using System.Collections.Generic;
using MusicRoom.Core;

namespace MusicRoom.Sheet
{
    public sealed class SheetTipSource : IGuideTipSource
    {
        public TipSpec[] Tips()
        {
            var o = new List<TipSpec>();
            foreach (var t in SheetTipTable.Tips)
            {
                o.Add(Spec(t, t.Line, desktop: false));
                if (t.DeskLine != null) o.Add(Spec(t, t.DeskLine, desktop: true));
            }
            return o.ToArray();
        }

        static TipSpec Spec(SheetTip t, string line, bool desktop)
        {
            bool signal = t.Success != null && t.Success.StartsWith("sheet.", System.StringComparison.Ordinal);
            return new TipSpec
            {
                Id = line, Title = desktop ? t.Title + " (desktop)" : t.Title, Lines = new[] { line },
                Class = t.Class == 2 ? TipClass.P2 : TipClass.P3,
                Trigger = TipTrigger.Manual, TriggerArg = "",
                Demo = desktop || t.Anchor == null ? DemoKind.None : t.Id == "tip_book" ? DemoKind.Point : DemoKind.Poke,
                Anchor = desktop ? null : t.Anchor,
                Success = signal ? TipSuccess.Signal : TipSuccess.None, SuccessArg = signal ? t.Success : null,
                VrOnly = !desktop && t.DeskLine != null,
                Covers = new[] { t.Covers },
            };
        }

        public FeatureSpec[] Features()
        {
            var o = new FeatureSpec[SheetTipTable.Features.Length];
            for (int i = 0; i < o.Length; i++) o[i] = new FeatureSpec(SheetTipTable.Features[i].id, SheetTipTable.Features[i].passive);
            return o;
        }
    }
}
