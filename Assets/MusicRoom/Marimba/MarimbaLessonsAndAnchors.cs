// The marimba's lesson targets (platform 7.2 ILessonTargets' shape; spec 5.6) and Sunny's anchors (platform 8.4
// IGuideAnchors' shape), written against today's Core lesson schema (LessonTarget: i, midi, note, kind, label). Phase B
// implements the platform's interfaces with these bodies (PHASE-B-NOTES.md). Anchors and landings are rig-local, turned
// into world space with the rig's pose.
using System.Collections.Generic;
using System.Text;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Marimba
{
    public static class MarimbaLessons
    {
        public const string Pack = "marimba";
        /// <summary>The lesson block's travel (m) and lead (s) (spec 4.1 "Lessons"; the pack's tutorial constants).</summary>
        public const float Travel = 0.30f, LeadSeconds = 1.667f;
        /// <summary>The target bar's lift (spec 4.4): 3 mm x k on its cord.</summary>
        public const float LiftPerK = 0.003f;

        /// <summary>0 &lt;= i &lt;= 36 and midi = 48 + i.</summary>
        public static bool Validate(LessonTarget t, out string why)
        {
            why = null;
            if (t == null) { why = "no target"; return false; }
            if (t.i < 0 || t.i >= MarimbaGeometry.Bars) { why = $"bar index {t.i} outside 0..36"; return false; }
            if (t.midi != MarimbaGeometry.MidiOf(t.i)) { why = $"midi {t.midi} is not bar {t.i} ({MarimbaGeometry.MidiOf(t.i)})"; return false; }
            // JsonUtility reads a JSON null string as "": both mean none
            if (!string.IsNullOrEmpty(t.kind) && t.kind != "roll") { why = $"kind '{t.kind}' (roll or none)"; return false; }
            if (!string.IsNullOrEmpty(t.label) && t.label != "R" && t.label != "L") { why = $"label '{t.label}' (R, L or none)"; return false; }
            return true;
        }

        /// <summary>The bar's top-centre pose (world), the approach (up) and the block's size (width, min(L, 0.12)).</summary>
        public static bool TryGetLanding(LessonTarget t, Vector3 rigPos, Quaternion rigRot, float h, out Pose landing, out Vector3 approach, out Vector2 size)
        {
            landing = default; approach = Vector3.down; size = default;
            if (!Validate(t, out _)) return false;
            int b = t.i;
            landing = new Pose(rigPos + rigRot * MarimbaGeometry.TopCentre(b, h), rigRot);
            approach = rigRot * Vector3.down;   // the block travels down onto the bar (start -> landing)
            size = new Vector2(MarimbaGeometry.Width(b), Mathf.Min(MarimbaGeometry.Length(b), 0.12f));
            return true;
        }

        /// <summary>A live marimba onset of the target's note: any mode counts (a fingertip wins the first lesson).</summary>
        public static bool Matches(LessonTarget t, in TakeEvent e) => t != null && e.type == MarimbaEvents.Strike && e.midi == t.midi;

        /// <summary>"Play E4", "Play E4 (right)", "Roll G4"; a chord joins with " + ".</summary>
        public static string Hint(IReadOnlyList<LessonTarget> targets)
        {
            if (targets == null || targets.Count == 0) return "";
            var sb = new StringBuilder();
            for (int k = 0; k < targets.Count; k++)
            {
                LessonTarget t = targets[k];
                if (k > 0) sb.Append(" + ");
                sb.Append(t.kind == "roll" ? "Roll " : "Play ");
                sb.Append(!string.IsNullOrEmpty(t.note) ? t.note : t.i >= 0 ? MarimbaGeometry.NoteName(t.i) : "?");
                if (t.label == "R") sb.Append(" (right)"); else if (t.label == "L") sb.Append(" (left)");
            }
            return sb.ToString();
        }
    }

    /// <summary>Sunny's demo targets on the marimba (spec 5.6 "Guide anchors").</summary>
    public static class MarimbaAnchors
    {
        /// <summary>The ids the tips and lessons use: tray.left / tray.right (the mallet grips), bar.&lt;note&gt; (a top centre),
        /// bar.&lt;note&gt;.tip (the bar's nearest tip, u 0.04: a sharp's reachable, full-voiced spot), bars.middle (F4).</summary>
        public static readonly string[] Documented = { "tray.left", "tray.right", "bar.<note>", "bar.<note>.tip", "bars.middle" };
        public const float TipU = 0.04f;

        /// <summary>Rig-local anchor point and its up; false for an unknown id.</summary>
        public static bool TryGetLocal(string anchor, float h, out Vector3 local, out Vector3 up)
        {
            local = default; up = Vector3.up;
            if (string.IsNullOrEmpty(anchor)) return false;
            if (anchor == "tray.left" || anchor == "tray.right") { local = MarimbaGeometry.MalletRestGrip(anchor == "tray.left" ? 0 : 1, h); return true; }
            if (anchor == "bars.middle") { local = MarimbaGeometry.TopCentre(MarimbaGeometry.BarOf("F4"), h); return true; }
            if (!anchor.StartsWith("bar.")) return false;
            bool tip = anchor.EndsWith(".tip");
            string note = tip ? anchor.Substring(4, anchor.Length - 8) : anchor.Substring(4);
            int b = MarimbaGeometry.BarOf(note);
            if (b < 0) return false;
            local = tip ? MarimbaGeometry.TopPoint(b, TipU, 0f, h) : MarimbaGeometry.TopCentre(b, h);
            return true;
        }

        public static bool TryGetAnchor(string anchor, Vector3 rigPos, Quaternion rigRot, float h, out Vector3 world, out Vector3 up)
        {
            world = default; up = Vector3.up;
            if (!TryGetLocal(anchor, h, out Vector3 l, out Vector3 u)) return false;
            world = rigPos + rigRot * l;
            up = rigRot * u;
            return true;
        }
    }
}
