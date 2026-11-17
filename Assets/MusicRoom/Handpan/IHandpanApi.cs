// The handpan's own API (handpan.md 5.6; platform.md 3.3: in the instrument's assembly, since no other module needs it in
// Core). The module implements it in phase B (PhaseB~/HandpanModule.cs); tools, tests and Sunny demos use it.
using UnityEngine;

namespace MusicRoom.Handpan
{
    public interface IHandpanApi
    {
        Transform Root { get; }
        /// <summary>9: the ding (0) and the eight ring notes.</summary>
        int FieldCount { get; }
        /// <summary>Static table: D3 A3 C4 D4 E4 F4 G4 A4 C5 (allocation-free).</summary>
        int MidiOf(int field);
        /// <summary>World: the field's dimple apex on the steel, and the field's outward normal there.</summary>
        Vector3 FieldCentre(int field);
        Vector3 FieldNormal(int field);
        /// <summary>Where the source sits: the shell centre, 6 cm above the rim plane.</summary>
        Vector3 SoundOrigin { get; }
        /// <summary>Per field: the unit's level of the tuned modes (0 without a unit).</summary>
        float FieldLevel(int field);
    }
}
