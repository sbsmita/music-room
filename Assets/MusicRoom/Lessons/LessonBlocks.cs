// Draws the tutorial's blocks: one Graphics.RenderMeshInstanced per frame with the lesson instrument's mesh (box, disc
// or capsule; ring or bar for the platform's instruments) and a per-instance colour array, i.e. one
// draw call for up to 64 blocks in both eyes.
// The material is a clone of the module's "Lessons/Block" template (MusicRoom/LessonBlock, instancing on). Nothing is
// drawn headless or without a GPU (batchmode -nographics): the composed matrices are still there for the tests.
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Lessons
{
    public sealed class LessonBlocks : System.IDisposable
    {
        public static readonly int ColorId = Shader.PropertyToID("_Color");

        readonly Mesh[] _meshes = new Mesh[5];
        readonly MaterialPropertyBlock _props = new MaterialPropertyBlock();
        readonly bool _canDraw;
        Material _material;

        public Material Material => _material;
        public bool CanDraw => _canDraw;
        /// <summary>Frames in which an instanced draw was submitted (tests, profiling).</summary>
        public int DrawsSubmitted { get; private set; }
        public Mesh MeshFor(BlockShape shape) => _meshes[(int)shape];

        public LessonBlocks(MaterialLibrary materials, bool headless)
        {
            _meshes[(int)BlockShape.Box] = LessonMeshes.BlockBox();
            _meshes[(int)BlockShape.Disc] = LessonMeshes.BlockDisc();
            _meshes[(int)BlockShape.Capsule] = LessonMeshes.BlockCapsule(Tutorial.CapsuleR, Tutorial.CapsuleLen);
            _meshes[(int)BlockShape.Ring] = LessonMeshes.BlockRing();     // the platform's shapes (LessonSpec.Shape)
            _meshes[(int)BlockShape.Bar] = LessonMeshes.BlockBar();
            _canDraw = !headless && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && SystemInfo.supportsInstancing;
            if (materials != null)
            {
                _material = materials.Clone(LessonsMaterials.Block);
                // A missing template (render-setup not run) falls back to URP Lit, which has no per-instance colour: the
                // blocks still show, in one colour. Instancing must be on for RenderMeshInstanced in either case.
                if (!_material.enableInstancing) _material.enableInstancing = true;
            }
        }

        /// <summary>Submit this frame's blocks (Tutorial.Matrices / Colors, first <paramref name="count"/> entries).</summary>
        public void Draw(BlockShape shape, Matrix4x4[] matrices, Vector4[] colors, int count, Bounds bounds, Camera camera = null)
        {
            if (!_canDraw || _material == null || count <= 0) return;
            _props.SetVectorArray(ColorId, colors);
            var rp = new RenderParams(_material)
            {
                matProps = _props,
                worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                layer = 0,
                camera = camera,
            };
            Graphics.RenderMeshInstanced(rp, _meshes[(int)shape], 0, matrices, count);
            DrawsSubmitted++;
        }

        public void Dispose()
        {
            for (int i = 0; i < _meshes.Length; i++) { LessonUtil.Destroy(_meshes[i]); _meshes[i] = null; }
            _material = null;   // the MaterialLibrary owns (and destroys) the clone
        }
    }

    static class LessonUtil
    {
        /// <summary>Destroy in play mode, DestroyImmediate in edit mode (EditMode tests build decks and blocks too).</summary>
        public static void Destroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
