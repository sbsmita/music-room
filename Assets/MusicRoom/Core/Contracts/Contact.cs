// Contact surfaces (10-upgrade/experience.md section 2.1): what the visual contact layer (ContactVisuals, App.Frame step
// 8a) asks the instruments. Piano (the keys at their CURRENT depth), Drums (heads, the kick pad, the cymbal plates at
// their CURRENT tilt) and the Menu (the panel face) implement IContactSurface and register it in Init with
// app.Register<IContactSurface>(this). Visual only: nothing here changes which notes sound or how loud; the play logic
// keeps reading the raw joints.
using UnityEngine;

namespace MusicRoom.Core
{
    /// <summary>What a contact may do on a surface: draw a soft contact shadow (E3), stop the drawn finger on it (E1).</summary>
    [System.Flags]
    public enum ContactUse : byte { None = 0, Shadow = 1, Clamp = 2, Both = 3 }

    public struct ContactHit
    {
        /// <summary>On the surface, world: the foot of the query point along -Normal.</summary>
        public Vector3 Point;
        /// <summary>Unit, world, out of the surface (a key top: the key's tilted up axis).</summary>
        public Vector3 Normal;
        /// <summary>Unit, world, in the surface: the shadow clip axis (a key's width); zero = no clip.</summary>
        public Vector3 Tangent;
        /// <summary>Metres the shadow may extend along Tangent (0 = unlimited), either side of ClipOrigin.</summary>
        public float ClipHalf;
        /// <summary>World, on the clip's centre line (a pressed key's centre line): the shadow is clipped to
        /// |Tangent . (p - ClipOrigin)| &lt;= ClipHalf. Used only when ClipHalf &gt; 0.</summary>
        public Vector3 ClipOrigin;
        /// <summary>Signed: Normal . (query - Point).</summary>
        public float Height;
        public ContactUse Use;
    }

    /// <summary>Implemented by Piano (keys at their CURRENT depth), Drums (heads, pad, cymbal plates at their CURRENT
    /// tilt) and Menu (the panel face). Registered with app.Register&lt;IContactSurface&gt;(this) in Init.
    /// Must not allocate. Returns false when the instrument is not present, its rig is held, or the point is outside every footprint.</summary>
    public interface IContactSurface
    {
        /// <summary>The surface under (or just above) <paramref name="world"/>: true when a footprint holds the point and
        /// -below &lt;= Height &lt;= above; with several candidates, the one with the smallest Height.</summary>
        bool QueryContact(Vector3 world, float above, float below, out ContactHit hit);
    }
}
