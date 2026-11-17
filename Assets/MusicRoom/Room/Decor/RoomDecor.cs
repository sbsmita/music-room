// The decor animation that runs in the Decor phase (spec 07 sections 3.2, 4.4): the flicker lights. Flames, coals,
// the cavity glow and the plants' sway animate in their shaders from _FireTime; smoke and embers are ParticleSystems.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    /// <summary>The fire's flicker light (spec 07 section 4.4) in the frame-rate independent form:
    /// walk += (r - .5) .08 sqrt(72 dt); walk *= .92^(72 dt); I = base (1 + amount (.5 sin(11t + ph) + .3 sin(17.3t + 2ph) + walk)).</summary>
    public sealed class FlickerLight
    {
        public readonly Light Light;
        public readonly float Base, Amount, Phase;
        public float Walk;
        /// <summary>A multiplier on the light (1: its own; the listen-back pulse lifts it while a playback sounds).</summary>
        public float Gain = 1f;

        public FlickerLight(Light light, float baseIntensity, float amount, float phase)
        {
            Light = light; Base = baseIntensity; Amount = amount; Phase = phase;
        }

        public static float Step(ref float walk, float random01, float dt)
        {
            float k = dt * 72f;
            walk += (random01 - 0.5f) * 0.08f * Mathf.Sqrt(k);
            walk *= Mathf.Pow(0.92f, k);
            return walk;
        }

        public static float Intensity(float baseI, float amount, float phase, float t, float walk) =>
            baseI * (1f + amount * (0.5f * Mathf.Sin(t * 11f + phase) + 0.3f * Mathf.Sin(t * 17.3f + phase * 2f) + walk));

        public void Update(float t, float dt, ref Mulberry32 rng)
        {
            Step(ref Walk, (float)rng.Next(), dt);
            if (Light != null && Light.isActiveAndEnabled) Light.intensity = Gain * Intensity(Base, Amount, Phase, t, Walk);
        }
    }

    /// <summary>Plant sway: rotZ = sin(.7t + phi) amp, rotX = sin(.5t + 1.3 phi) .7 amp on the plant's stems and leaves
    /// (the pot stays still), source-frame Euler XYZ (rx, 0, rz) in the plant's frame, mirrored: Unity x angle negated. The GPU
    /// does it (MusicRoom/RoomLit with _SWAY, from TEXCOORD2 / TEXCOORD3 of the merged stem and leaf meshes); this struct is the
    /// per-plant data and the CPU mirror the tests compare with.</summary>
    public struct PlantSway
    {
        public string Name;
        /// <summary>The plant's base in the room root's (= the merged meshes') space, Unity coordinates.</summary>
        public Vector3 Base;
        /// <summary>The plant frame's Unity yaw (radians): the fern's shelf and the ivy's window are turned.</summary>
        public float Yaw;
        public float Phase, Amp;

        /// <summary>From the plant's world matrix in the right-handed source frame (yaw only: the plants stand upright).</summary>
        public static PlantSway From(string name, Matrix4x4 rhWorld, float phase, float amp)
        {
            Quaternion q = MusicRoom.Room.Geometry.Rh.ToUnity(rhWorld.rotation);
            Vector3 f = q * Vector3.forward;
            return new PlantSway
            {
                Name = name, Base = MusicRoom.Room.Geometry.Rh.ToUnity((Vector3)rhWorld.GetColumn(3)), Yaw = Mathf.Atan2(f.x, f.z),
                Phase = phase, Amp = amp,
            };
        }

        /// <summary>TEXCOORD3: (phase, amplitude, cos yaw, sin yaw).</summary>
        public Vector4 VertexData => new Vector4(Phase, Amp, Mathf.Cos(Yaw), Mathf.Sin(Yaw));

        public static Quaternion Rotation(float t, float phase, float amp)
        {
            float rz = Mathf.Sin(t * 0.7f + phase) * amp;
            float rx = Mathf.Sin(t * 0.5f + phase * 1.3f) * amp * 0.7f;
            return Quaternion.AngleAxis(-rx * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(rz * Mathf.Rad2Deg, Vector3.forward);
        }

        /// <summary>Where the shader moves a vertex (room space) at time <paramref name="t"/>: about the base, in the plant's
        /// frame (as if the stems and leaves hung under a pivot turned by Rotation).</summary>
        public Vector3 Apply(Vector3 v, float t)
        {
            Quaternion frame = Quaternion.AngleAxis(Yaw * Mathf.Rad2Deg, Vector3.up);
            return Base + frame * (Rotation(t, Phase, Amp) * (Quaternion.Inverse(frame) * (v - Base)));
        }
    }
}
