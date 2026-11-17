// The registry's two built-in, non-playable descriptors (platform.md 3.2): the side table (the tape deck in Lessons, the
// switch panel in Room; always at the seat) and the palm menu. They are left out of IInstrumentRegistry.All.
using UnityEngine;

namespace MusicRoom.Core
{
    public static class BuiltIns
    {
        /// <summary>The side table: home (-0.39, 0, -0.21) yaw -90 left of the seat; it yields to its alternate pose behind-left
        /// (-0.25, 0, -0.62) yaw 180 only when a set cannot be planned with it at home (platform.md 4.2).</summary>
        public static readonly Vector3 TableHome = new Vector3(-0.39f, 0f, -0.21f);
        public const float TableHomeYaw = -90f;
        public static readonly Vector3 TableAlternate = new Vector3(-0.25f, 0f, -0.62f);
        public const float TableAlternateYaw = 180f;
        /// <summary>The side table's footprint (table-local, platform registry.mjs TABLE.boxes).</summary>
        public static readonly FootBox[] TableBoxes = { new FootBox(0.04f, 0f, 0.22f, 0.17f, 0f, 0.68f) };

        public static InstrumentDescriptor Table() => new InstrumentDescriptor
        {
            Kind = InstrumentKind.Table, Rig = RigId.Table, Id = "table", Name = "Side table", AloneName = "Side table", Family = "Table",
            Carousel = -1, ModuleOrder = 20, TickOrder = 30, Traits = InstrumentTraits.None, Tools = ToolMask.None,
            Seat = new SeatSpec { Home = TableHome, HomeYaw = TableHomeYaw, Single = TableHome, SingleYaw = TableHomeYaw, Boxes = TableBoxes, Priority = 3 },
            DrawsAtSeat = 15,
        };

        public static InstrumentDescriptor Menu() => new InstrumentDescriptor
        {
            Kind = InstrumentKind.Menu, Rig = RigId.Lessons, Id = "menu", Name = "Menu", AloneName = "Menu", Family = "Menu",
            Carousel = -2, ModuleOrder = 60, TickOrder = 50, Traits = InstrumentTraits.None, Tools = ToolMask.None,
            DrawsAtSeat = 4,
        };
    }
}
