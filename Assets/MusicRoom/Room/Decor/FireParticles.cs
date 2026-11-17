// Fireplace smoke (36 points .28 m, #9a9a9a a .14, rising .18-.32 m/s from y .45 to .98, drifting sideways) and embers
// (70 points .014 m, #ffa050 a .9 additive, .15-.4 m/s from y .12 to 1.0) as ParticleSystems (ARCHITECTURE.md section 0;
// spec 07 section 4.3): local space in the fireplace frame, emitted on the bottom slab of the spawn box and
// prewarmed, so the column is full from the first frame. Every particle dies exactly at the wrap height (smoke
// .98, embers 1.0), hidden behind the lintel (its bottom at y .78): the lifetime is long enough for the slowest one to
// get there (span / SpeedMin), and a collision plane at the wrap height (normal down, lifetime loss 1) removes each
// particle the moment it crosses it, whatever its speed. Noise gives the sideways drift. No per-frame script work. Colour: the particles stay white with the authored
// alpha; the material carries RoomMaterials.OverlayColor(colour, alpha), the colour whose x alpha adds over black what
// an sRGB-space blend adds (URP's particle shaders take vertex colours as they are).
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    public static class FireParticles
    {
        public struct Spec
        {
            public string Name;
            public int Max;
            public float Size, SpeedMin, SpeedMax, Bottom, Top, DriftSpeed, DriftRate;
            public Vector3 BoxCenterUnity, BoxSizeUnity;
            /// <summary>The points' colour (sRGB) and opacity.</summary>
            public Color Color;
            public float Alpha;
        }

        /// <summary>Smoke, in the fireplace frame (Unity local: source-frame z negated).</summary>
        public static Spec Smoke => new Spec
        {
            Name = "Smoke", Max = 36, Size = 0.28f, SpeedMin = 0.18f, SpeedMax = 0.32f, Bottom = 0.45f, Top = 0.98f, DriftSpeed = 0.08f, DriftRate = 0.8f,
            BoxCenterUnity = new Vector3(0f, 0.45f, -0.22f), BoxSizeUnity = new Vector3(0.4f, 0f, 0.16f),
            Color = new Color(0x9a / 255f, 0x9a / 255f, 0x9a / 255f), Alpha = 0.14f,
        };

        /// <summary>Embers (the wrap respawns x in [-.2, .2]).</summary>
        public static Spec Embers => new Spec
        {
            Name = "Embers", Max = 70, Size = 0.014f, SpeedMin = 0.15f, SpeedMax = 0.4f, Bottom = 0.12f, Top = 1.0f, DriftSpeed = 0.04f, DriftRate = 3f,
            BoxCenterUnity = new Vector3(0f, 0.12f, -0.21f), BoxSizeUnity = new Vector3(0.4f, 0f, 0.18f),
            Color = new Color(1f, 0xa0 / 255f, 0x50 / 255f), Alpha = 0.9f,
        };

        /// <summary>Emission over the steady-state rate (Max / mean lifetime): the cap, not the rate, sets the count.</summary>
        public const float RefillFactor = 1.5f;

        /// <summary>The start lifetime: the slowest particle's rise from Bottom to Top.</summary>
        public static float MaxLifetime(in Spec s) => (s.Top - s.Bottom) / s.SpeedMin;

        /// <summary>E[span / v] for v uniform in [SpeedMin, SpeedMax]: span ln(max / min) / (max - min).</summary>
        public static float MeanLifetime(in Spec s) => (s.Top - s.Bottom) * Mathf.Log(s.SpeedMax / s.SpeedMin) / (s.SpeedMax - s.SpeedMin);

        public static ParticleSystem Create(Transform parent, in Spec s, Material material, uint seed)
        {
            var go = new GameObject(s.Name);
            go.SetActive(false);   // configure before the system first plays
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            float span = s.Top - s.Bottom;
            // Long enough for the slowest particle to reach the wrap height; the collision plane ends every life there.
            main.startLifetime = MaxLifetime(s);
            main.startSpeed = 0f;
            main.startSize = s.Size;
            main.startColor = new Color(1f, 1f, 1f, s.Alpha);
            main.maxParticles = s.Max;
            main.gravityModifier = 0f;
            ps.useAutoRandomSeed = false;
            ps.randomSeed = seed;
            var emission = ps.emission;
            // Keep a constant count (the points wrap the moment they reach the top): Max alive, each living
            // span / v with v uniform in [min, max]. Emitting half again as fast as that steady state needs makes the
            // maxParticles cap the limit, so a slot is refilled almost as soon as a particle dies.
            emission.rateOverTime = RefillFactor * s.Max / MeanLifetime(s);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = s.BoxCenterUnity;
            shape.scale = s.BoxSizeUnity;
            shape.randomDirectionAmount = 0f;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            // All three axes in the same curve mode (Unity requires it): x, z fixed at 0, y per particle in [min, max].
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(s.SpeedMin, s.SpeedMax);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = s.DriftSpeed;
            noise.strengthY = 0f;
            noise.strengthZ = 0f;
            noise.frequency = 0.5f;
            noise.scrollSpeed = s.DriftRate / (2f * Mathf.PI);
            noise.quality = ParticleSystemNoiseQuality.Low;
            // The wrap: a plane at the top of the column, facing down (the particles rise into its back side),
            // kills each particle as it crosses (no bounce). The radius scale makes the crossing the particle's centre.
            var top = new GameObject(s.Name + " wrap").transform;
            top.SetParent(go.transform, false);
            top.localPosition = new Vector3(0f, s.Top, 0f);
            top.localRotation = Quaternion.Euler(180f, 0f, 0f);
            var col = ps.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.Planes;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.SetPlane(0, top);
            col.bounce = 0f;
            col.dampen = 1f;
            col.lifetimeLoss = 1f;
            col.radiusScale = 0.0001f;
            col.enableDynamicColliders = false;
            col.sendCollisionMessages = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 2f;
            go.SetActive(true);
            return ps;
        }
    }
}
