// The fireplace that lights up in the player's own room in mixed reality (Decor/MrFireplace.cs places and fades it): the
// VR room's fireplace piece for piece (RoomBuilder.Fireplace's numbers: brick pillars and lintel, the sooty firebox, the
// hearth stone, the mantel with its top at 1.27 m, three logs, the coals, the cavity glow, the three fires' flame mesh,
// the smoke and the embers, the fire light), with the room's own materials, but free-standing: no chimney breast up to a
// ceiling. Static parts are merged per material (brick, soot, stone, log: 4 draws), then coals, glow, flames, smoke and
// embers (9 draws in all). The flame and glow materials are this hearth's own copies, so its fire can fade without
// touching the VR room's.
// Frames: Root stands on the floor with its back against the wall (local +z out into the room, x across, y up); Fire is
// the VR fireplace's frame under it (turned a half turn: Unity local -z into the room, as RoomBuilder builds it), so the
// particles' and the glow's constants are the VR room's exactly. In Root's frame the hearth spans x +-0.95, z -0.015 ..
// 0.735, y 0 .. 1.27 at scale 1 (Decor/MrFireplace scales the root by MrHearthPlace.Scale, 0.85: MrHearthPlace's box).
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Room.Decor;
using MusicRoom.Room.Geometry;
using MusicRoom.Room.Render;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room.World
{
    public sealed class Hearth
    {
        /// <summary>On the floor at the wall, +z into the room.</summary>
        public Transform Root;
        /// <summary>The VR fireplace's frame (Unity local -z into the room).</summary>
        public Transform Fire;
        public Renderer Coals, Glow, Flames;
        /// <summary>Brick, soot, stone and log: one renderer each.</summary>
        public readonly List<Renderer> Static = new List<Renderer>(4);
        public ParticleSystem Smoke, Embers;
        public Light Light;
        /// <summary>This hearth's own copies (the fade); the glow's pulse as the room's material has it.</summary>
        public Material FlameMaterial, GlowMaterial;
        public Vector4 GlowPulse, GlowPulse2;
        public readonly List<Mesh> Meshes = new List<Mesh>(8);

        /// <summary>Every renderer (draws: 9).</summary>
        public int RendererCount => Static.Count + 3 + (Smoke != null ? 1 : 0) + (Embers != null ? 1 : 0);

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
            foreach (Mesh m in Meshes) if (m != null) Object.Destroy(m);
            Meshes.Clear();
            if (FlameMaterial != null) Object.Destroy(FlameMaterial);
            if (GlowMaterial != null) Object.Destroy(GlowMaterial);
        }
    }

    public static class HearthBuilder
    {
        const float PI = Mathf.PI;
        /// <summary>The firebox depth (RoomBuilder.Fireplace's).</summary>
        public const float Depth = 0.42f;
        /// <summary>The fire light: the VR room's colour at (0, .5, .45) in the fireplace frame; brighter is not needed over
        /// the brighter mixed reality ambient, a softer range keeps it on the hearth and the nearest instruments.</summary>
        public const string LightHex = "#ff8f3c";
        public const float LightIntensity = 1.6f, LightRange = 4.5f;
        public static readonly int Intensity = Shader.PropertyToID("_Intensity"), Pulse = Shader.PropertyToID("_Pulse"),
            Pulse2 = Shader.PropertyToID("_Pulse2");

        public static Hearth Build(Transform parent, RoomMaterials mats, uint seed)
        {
            var h = new Hearth();
            var rng = new Mulberry32(seed);
            var root = new GameObject("MrFireplace").transform;
            root.gameObject.SetActive(false);
            root.SetParent(parent, false);
            h.Root = root;
            var f = new GameObject("Fireplace").transform;
            f.SetParent(root, false);
            f.localRotation = Quaternion.Euler(0f, 180f, 0f);
            h.Fire = f;

            // The VR fireplace's pieces in its own frame (source frame, +z into the room), without the chimney breast.
            Geo brick = new Geo(), soot = new Geo(), stone = new Geo(), logs = new Geo();
            void Box(Geo into, float w, float hh, float d, float x, float y, float z) => into.Append(PrimitiveMeshes.Box(w, hh, d), Rh.T(x, y, z));
            Box(brick, 0.38f, 1.2f, Depth, -0.64f, 0.6f, Depth / 2f);
            Box(brick, 0.38f, 1.2f, Depth, 0.64f, 0.6f, Depth / 2f);
            Box(brick, 1.66f, 0.42f, Depth, 0f, 0.99f, Depth / 2f);
            Box(soot, 1.0f, 0.8f, 0.05f, 0f, 0.4f, 0.03f);                    // firebox back
            Box(soot, 0.05f, 0.8f, Depth - 0.06f, -0.47f, 0.4f, Depth / 2f); // firebox sides
            Box(soot, 0.05f, 0.8f, Depth - 0.06f, 0.47f, 0.4f, Depth / 2f);
            Box(stone, 1.9f, 0.03f, 0.75f, 0f, 0.015f, 0.36f);               // hearth
            Box(stone, 1.9f, 0.07f, 0.55f, 0f, 1.235f, 0.28f);               // mantel, top at 1.27
            float[,] l = { { -0.12f, 0.085f, 0.24f, 0.35f }, { 0.12f, 0.085f, 0.2f, -0.3f }, { 0f, 0.17f, 0.22f, 0.05f } };
            for (int i = 0; i < 3; i++)
                logs.Append(PrimitiveMeshes.Cylinder(0.045f, 0.05f, 0.52f, 10), Rh.TRS(new Vector3(l[i, 0], l[i, 1], l[i, 2]), Rh.Euler(0f, l[i, 3], PI / 2f)));
            brick.SetUvFromUnity(RoomBuilder.BrickUv);
            Static(h, f, "Brick", brick, mats[RoomMat.Brick]);
            Static(h, f, "Soot", soot, mats[RoomMat.Soot]);
            Static(h, f, "Stone", stone, mats[RoomMat.Stone]);
            Static(h, f, "Logs", logs, mats[RoomMat.Log]);

            // Coals: a quad facing up under the logs.
            var coals = new Geo();
            coals.Append(PrimitiveMeshes.Plane(0.7f, 0.5f), Rh.TRS(new Vector3(0f, 0.035f, 0.22f), Rh.Euler(-PI / 2f, 0f, 0f)));
            h.Coals = RoomBuilder.MakeRenderer(f, "Coals", Keep(h, coals.ToUnityMesh("MrHearth Coals")), mats[RoomMat.Coals]);

            // Cavity glow: a camera-facing sprite (its own material: the fade scales its pulse).
            Material roomGlow = mats[RoomMat.Glow];
            h.GlowMaterial = roomGlow != null ? new Material(roomGlow) { name = "MrHearth Glow" } : null;
            if (h.GlowMaterial != null && h.GlowMaterial.HasProperty(Pulse))
            {
                h.GlowPulse = h.GlowMaterial.GetVector(Pulse);
                h.GlowPulse2 = h.GlowMaterial.GetVector(Pulse2);
            }
            var quad = PrimitiveMeshes.Plane(1f, 1f).ToUnityMesh("MrHearth GlowQuad");
            quad.bounds = new Bounds(Vector3.zero, new Vector3(1.2f, 1.2f, 1.2f));
            h.Glow = RoomBuilder.MakeRenderer(f, "Glow", Keep(h, quad), h.GlowMaterial, Rh.ToUnity(new Vector3(0f, 0.34f, 0.1f)));

            // The three fires as one mesh (RoomBuilder.Fireplace's), its own flame material (the fade scales _Intensity).
            var flames = new Geo();
            RoomBuilder.AddFire(flames, Matrix4x4.Translate(new Vector3(0.02f, 0.07f, 0.22f)), 0.5f, 0.72f, 3, 2f, 1.15f);
            RoomBuilder.AddFire(flames, Rh.TRS(new Vector3(-0.17f, 0.09f, 0.17f), Rh.Euler(0f, 0.5f, 0f)), 0.3f, 0.46f, 3, 7f, 1.05f);
            RoomBuilder.AddFire(flames, Rh.TRS(new Vector3(0.18f, 0.1f, 0.26f), Rh.Euler(0f, -0.4f, 0f)), 0.26f, 0.38f, 2, 11f, 1.0f);
            Material roomFlame = mats[RoomMat.Flame];
            h.FlameMaterial = roomFlame != null ? new Material(roomFlame) { name = "MrHearth Flame" } : null;
            h.Flames = RoomBuilder.MakeRenderer(f, "Flames", Keep(h, flames.ToUnityMesh("MrHearth Flames")), h.FlameMaterial);

            h.Smoke = FireParticles.Create(f, FireParticles.Smoke, mats[RoomMat.Smoke], (uint)(rng.Next() * 1e9));
            h.Embers = FireParticles.Create(f, FireParticles.Embers, mats[RoomMat.Ember], (uint)(rng.Next() * 1e9));

            var light = new GameObject("MrHearthLight").AddComponent<Light>();
            light.transform.SetParent(f, false);
            light.transform.localPosition = Rh.ToUnity(new Vector3(0f, 0.5f, 0.45f));
            light.type = LightType.Point;
            light.color = MusicRoom.Core.TextureUtil.Hex(LightHex);
            light.intensity = LightIntensity;
            light.range = LightRange;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            h.Light = light;
            return h;
        }

        static void Static(Hearth h, Transform f, string name, Geo g, Material m)
        {
            Renderer r = RoomBuilder.MakeRenderer(f, name, Keep(h, g.ToUnityMesh("MrHearth " + name)), m);
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;   // the room's probe is hidden with the room
            h.Static.Add(r);
        }

        static Mesh Keep(Hearth h, Mesh m) { h.Meshes.Add(m); return m; }
    }
}
