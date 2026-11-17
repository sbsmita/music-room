// The menu's instrument portraits (12-new-instruments/platform.md 5.2): a photograph-like picture of each instrument's own
// model, rendered once at runtime into one 1024 x 512 RGBA32 atlas (8 cells of 256 x 256, 2 MiB) that the menu face
// samples. One portrait per Step (the menu steps one per frame while the start panel is up, else from the first
// Instruments screen on). The rig is rendered where it is (its display configuration when it is on display):
//   - a disabled camera rendering into its own target texture (so URP never treats it as an XR camera), culling mask layer 30 = Layers.Hidden, which the head camera never draws)
//     looking along the descriptor's Portrait.From -> At (rig-local) with its vertical Fov, clearing to the backdrop paper
//     the menu passes; with FitToBounds (the default) the aim and the distance are fitted to the rendered parts (the
//     corners of every renderer's own bounds) so the instrument is centred and spans Fill of the frame along its limiting
//     axis, whatever its model (a re-modelled instrument stays framed); without it, From and At exactly;
//   - the rig's renderers (Rig.Group, GetComponentsInChildren once per portrait) move to layer 30 for ONE
//     RenderPipeline.SubmitRenderRequest(cam, StandardRequest { destination }) and move straight back, so no head camera
//     ever sees them there; the static Rendering flag is up meanwhile;
//   - a warm studio key (#fff1dc, 1.1, from the camera's upper left front, lighting layer 30 only, no shadows) is
//     RenderSettings.sun for the request only (restored); the ambient stays the room's;
//   - rendered at twice the band's size and box-filtered down (a 2:1 bilinear blit), then copied into the cell's band:
//     the middle 256 x 170 of the cell, the tile portrait's 226 x 150 aspect. The rest of the cell is paper.
// Fallback: under -nographics / App.Headless, without CopyTexture, with UseRenderRequests or the setting
// "musicroom.menu.portraits" = 0 (the device switch, UNVERIFIED item 49), or when a request throws, a cell is Failed and
// the menu draws the descriptor's vector icon instead (PortraitSpec.Fallback). Names no instrument: it reads the registry.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MusicRoom.Core
{
    public sealed class PortraitStudio : IDisposable
    {
        public const int CellPx = 256, Columns = 4, Rows = 2, Cells = Columns * Rows;
        public const int AtlasW = CellPx * Columns, AtlasH = CellPx * Rows;
        /// <summary>The portrait band inside a cell: the tile's 226 x 150 aspect at the cell's full width.</summary>
        public const int BandW = 256, BandH = 170;
        public const int Supersample = 2;
        public const float Aspect = BandW / (float)BandH;
        /// <summary>The fitted framing: the rendered parts' box corners span this much of the frame's limiting dimension
        /// (the model itself a little less: its corners are rounder than its boxes).</summary>
        public const float Fill = 0.80f;
        public static readonly Color KeyColor = new Color(1f, 241f / 255f, 220f / 255f, 1f);   // #fff1dc
        public const float KeyIntensity = 1.1f;
        /// <summary>The key comes from the camera's upper left front (camera-space direction the light travels).</summary>
        public static readonly Vector3 KeyTravel = new Vector3(0.55f, -0.62f, 0.56f);
        /// <summary>AppSettings int: 0 keeps the vector icons (the device switch); default 1.</summary>
        public const string SettingKey = "musicroom.menu.portraits";

        /// <summary>A portrait request is being rendered (modules may skip per-camera work meanwhile).</summary>
        public static bool Rendering { get; private set; }
        /// <summary>The switch for the render-request path (false: every cell falls back to its vector icon).</summary>
        public static bool UseRenderRequests = true;
        /// <summary>Fit the camera distance to the rig's bounds along the descriptor's From -> At line (false: From as given).</summary>
        public static bool FitToBounds = true;

        public enum CellState : byte { Pending = 0, Ready, Failed }

        readonly App _app;
        readonly Color _paper;
        readonly CellState[] _state = new CellState[Cells];
        readonly List<Renderer> _renderers = new List<Renderer>(128);
        readonly List<int> _layers = new List<int>(128);
        readonly List<Vector3> _corners = new List<Vector3>(1024);
        GameObject _root;
        Camera _cam;
        Light _key;
        RenderTexture _big, _small;
        readonly Stopwatch _watch = new Stopwatch();

        /// <summary>The atlas (null when portraits are not available here).</summary>
        public RenderTexture Atlas { get; private set; }
        /// <summary>Portraits can be rendered here (a GPU, not headless, CopyTexture, the switch on).</summary>
        public bool Available { get; }
        /// <summary>Counts cells that became Ready (the menu repaints when it changes).</summary>
        public int Version { get; private set; }
        public int Rendered { get; private set; }
        /// <summary>CPU milliseconds of the last portrait (SubmitRenderRequest, the blit and the copy) and the most.</summary>
        public double LastMs { get; private set; }
        public double MaxMs { get; private set; }

        public PortraitStudio(App app, Color paper)
        {
            _app = app;
            _paper = paper;
            bool gpu = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && !(app?.Headless ?? true);
            bool copy = (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;
            bool on = UseRenderRequests && (app?.Settings == null || app.Settings.GetInt(SettingKey, 1) != 0);
            Available = gpu && copy && on;
            if (!Available)
            {
                for (int i = 0; i < Cells; i++) _state[i] = CellState.Failed;
                return;
            }
            Atlas = new RenderTexture(new RenderTextureDescriptor(AtlasW, AtlasH, RenderTextureFormat.ARGB32, 0) { sRGB = true, msaaSamples = 1, useMipMap = false })
            {
                name = "MenuPortraitAtlas", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            Atlas.Create();
            Clear(Atlas, _paper);
        }

        public CellState StateOf(int cell) => cell >= 0 && cell < Cells ? _state[cell] : CellState.Failed;
        public bool Ready(int cell) => StateOf(cell) == CellState.Ready;

        /// <summary>The cell of an instrument: its index in the registry's carousel (All), or -1.</summary>
        public static int CellOf(IInstrumentRegistry registry, InstrumentKind kind)
        {
            if (registry == null) return -1;
            var all = registry.All;
            for (int i = 0; i < all.Count && i < Cells; i++) if (all[i].Kind == kind) return i;
            return -1;
        }

        /// <summary>The band of a cell in atlas uv (xMin, yMin = its bottom-left).</summary>
        public static Rect CellUv(int cell)
        {
            int x = (cell % Columns) * CellPx, yTop = (cell / Columns) * CellPx + (CellPx - BandH) / 2;
            // Atlas pixel rows run bottom-up in texture space: the cell's top row y in "from the top" terms.
            float v1 = 1f - yTop / (float)AtlasH, v0 = v1 - BandH / (float)AtlasH;
            return Rect.MinMaxRect(x / (float)AtlasW, v0, (x + BandW) / (float)AtlasW, v1);
        }

        /// <summary>The band's pixel rectangle in the atlas, bottom-up rows (CopyTexture and read-back coordinates).</summary>
        public static RectInt CellPixels(int cell)
        {
            int x = (cell % Columns) * CellPx;
            int yTop = (cell / Columns) * CellPx + (CellPx - BandH) / 2;
            return new RectInt(x, AtlasH - yTop - BandH, BandW, BandH);
        }

        /// <summary>A cell is waiting for its portrait and could be rendered now.</summary>
        public bool AnyPending
        {
            get
            {
                if (!Available) return false;
                IInstrumentRegistry reg = _app.Registry;
                for (int i = 0; i < Cells && i < reg.All.Count; i++)
                    if (_state[i] == CellState.Pending && reg.IsLoaded(reg.All[i].Kind)) return true;
                return false;
            }
        }

        /// <summary>Render the next pending portrait whose rig is in the room now (one per call). False when there is none
        /// (every cell Ready or Failed, or the pending ones' rigs are hidden: mixed reality, a blocked display spot).</summary>
        public bool Step()
        {
            if (!Available || Atlas == null) return false;
            IInstrumentRegistry reg = _app.Registry;
            IRigRegistry rigs = _app.Get<IRigRegistry>();
            if (reg == null || rigs == null) return false;
            var all = reg.All;
            for (int i = 0; i < Cells && i < all.Count; i++)
            {
                if (_state[i] != CellState.Pending) continue;
                InstrumentDescriptor d = all[i];
                if (!reg.IsLoaded(d.Kind)) continue;
                Rig rig = rigs.Get(d.Rig);
                if (rig == null || rig.Group == null || d.Portrait == null) { _state[i] = CellState.Failed; continue; }
                if (!rig.Group.gameObject.activeInHierarchy) continue;   // hidden now (MR): try again later
                Render(i, d, rig);
                return true;
            }
            return false;
        }

        void Render(int cell, InstrumentDescriptor d, Rig rig)
        {
            _watch.Restart();
            Light sun = RenderSettings.sun;
            try
            {
                Ensure();
                Transform g = rig.Group;
                g.GetComponentsInChildren(false, _renderers);
                _layers.Clear();
                for (int i = 0; i < _renderers.Count; i++)
                {
                    GameObject go = _renderers[i].gameObject;
                    _layers.Add(go.layer);
                    go.layer = Layers.Hidden;
                }
                Corners(_renderers, _corners);
                Pose(g, d.Portrait, _corners, out Vector3 pos, out Quaternion rot, out float near, out float far);
                _cam.transform.SetPositionAndRotation(pos, rot);
                _cam.fieldOfView = Mathf.Clamp(d.Portrait.Fov, 5f, 120f);
                _cam.nearClipPlane = near;
                _cam.farClipPlane = far;
                _key.transform.rotation = Quaternion.LookRotation(rot * KeyTravel.normalized, rot * Vector3.up);
                _key.enabled = true;
                RenderSettings.sun = _key;
                Rendering = true;
                var request = new RenderPipeline.StandardRequest { destination = _big };
                bool ok = RenderPipeline.SupportsRenderRequest(_cam, request);
                if (ok) RenderPipeline.SubmitRenderRequest(_cam, request);
                Rendering = false;
                RenderSettings.sun = sun;
                _key.enabled = false;
                Restore();
                if (!ok) { _state[cell] = CellState.Failed; Debug.LogWarning($"[Core] portrait {d.Id}: render requests are not supported (vector icon)"); return; }
                // Blit leaves RenderTexture.active on _small, which ReleaseWork later destroys ("Releasing render texture that
                // is set to be RenderTexture.active!" on the headset): put the previous target back.
                RenderTexture prevActive = RenderTexture.active;
                Graphics.Blit(_big, _small);
                RenderTexture.active = prevActive;
                RectInt band = CellPixels(cell);
                Graphics.CopyTexture(_small, 0, 0, 0, 0, BandW, BandH, Atlas, 0, 0, band.x, band.y);
                _state[cell] = CellState.Ready;
                Version++;
                Rendered++;
            }
            catch (Exception e)
            {
                _state[cell] = CellState.Failed;
                Debug.LogWarning($"[Core] portrait {d.Id} failed ({e.Message}): its vector icon is used");
            }
            finally
            {
                Rendering = false;
                if (RenderSettings.sun != sun) RenderSettings.sun = sun;
                if (_key != null) _key.enabled = false;
                Restore();
                _watch.Stop();
                LastMs = _watch.Elapsed.TotalMilliseconds;
                if (LastMs > MaxMs) MaxMs = LastMs;
            }
            Debug.Log($"[Core] portrait {d.Id}: {_state[cell]} in {LastMs:F2} ms (cell {cell})");
            if (!AnyPending) ReleaseWork();
        }

        /// <summary>Every renderer back on its own layer (idempotent: the list is cleared once restored).</summary>
        void Restore()
        {
            int n = Mathf.Min(_renderers.Count, _layers.Count);
            for (int i = 0; i < n; i++) if (_renderers[i] != null) _renderers[i].gameObject.layer = _layers[i];
            _renderers.Clear();
            _layers.Clear();
        }

        /// <summary>The corners of every shown renderer's own (local) bounds, in world space: a tight hull of what is drawn.</summary>
        static void Corners(List<Renderer> renderers, List<Vector3> corners)
        {
            corners.Clear();
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.localBounds;
                Matrix4x4 m = r.localToWorldMatrix;
                Vector3 c = b.center, e = b.extents;
                for (int k = 0; k < 8; k++)
                    corners.Add(m.MultiplyPoint3x4(new Vector3(c.x + ((k & 1) == 0 ? -e.x : e.x), c.y + ((k & 2) == 0 ? -e.y : e.y), c.z + ((k & 4) == 0 ? -e.z : e.z))));
            }
        }

        /// <summary>The camera pose. The view direction and Fov are the descriptor's (From -> At in the rig's frame). With
        /// FitToBounds the aim is moved (three passes) until the parts' projection is centred and the distance is the one
        /// at which they span Fill of the frame; else From exactly. Near and far bracket the parts.</summary>
        void Pose(Transform g, PortraitSpec p, List<Vector3> corners, out Vector3 pos, out Quaternion rot, out float near, out float far)
        {
            Vector3 from = g.TransformPoint(p.From), at = g.TransformPoint(p.At);
            Vector3 dir = at - from;
            if (dir.sqrMagnitude < 1e-8f) dir = g.forward;
            dir.Normalize();
            rot = Quaternion.LookRotation(dir, Vector3.up);
            float fov = Mathf.Clamp(p.Fov, 5f, 120f);
            float d = Vector3.Distance(from, at);
            if (FitToBounds && corners.Count > 0)
            {
                var b = new Bounds(corners[0], Vector3.zero);
                for (int i = 1; i < corners.Count; i++) b.Encapsulate(corners[i]);
                at = b.center;
                float tv = Mathf.Tan(0.5f * fov * Mathf.Deg2Rad), th = tv * Aspect;
                Quaternion inv = Quaternion.Inverse(rot);
                for (int pass = 0; pass < 3; pass++)
                {
                    d = FitDistance(corners, rot, at, fov, Aspect, Fill);
                    Vector3 cam = at - dir * d;
                    float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
                    for (int i = 0; i < corners.Count; i++)
                    {
                        Vector3 l = inv * (corners[i] - cam);
                        float z = Mathf.Max(1e-3f, l.z);
                        float u = l.x / (z * th), v = l.y / (z * tv);
                        if (u < u0) u0 = u; if (u > u1) u1 = u;
                        if (v < v0) v0 = v; if (v > v1) v1 = v;
                    }
                    // Move the aim by the projection's off-centre, at the aim's depth.
                    at += rot * new Vector3(0.5f * (u0 + u1) * th * d, 0.5f * (v0 + v1) * tv * d, 0f);
                }
                d = FitDistance(corners, rot, at, fov, Aspect, Fill);
            }
            pos = at - dir * d;
            float zMin = float.MaxValue, zMax = float.MinValue;
            Quaternion invRot = Quaternion.Inverse(rot);
            for (int i = 0; i < corners.Count; i++)
            {
                float z = (invRot * (corners[i] - pos)).z;
                if (z < zMin) zMin = z;
                if (z > zMax) zMax = z;
            }
            if (corners.Count == 0) { zMin = d * 0.5f; zMax = d * 2f; }
            near = Mathf.Max(0.02f, zMin * 0.8f);
            far = Mathf.Max(near + 0.5f, zMax * 1.2f + 0.2f);
        }

        /// <summary>The camera distance from <paramref name="at"/> (looking along rot's forward) at which every point lies
        /// within <paramref name="fill"/> of the half-frame: for a point at camera-frame offset (x, y, z) from At,
        /// |x| &lt;= fill tan(h/2) (z + d) and |y| &lt;= fill tan(v/2) (z + d).</summary>
        public static float FitDistance(List<Vector3> points, Quaternion rot, Vector3 at, float vfovDeg, float aspect, float fill)
        {
            float tv = Mathf.Tan(0.5f * vfovDeg * Mathf.Deg2Rad) * fill, th = tv * aspect;
            Quaternion inv = Quaternion.Inverse(rot);
            float d = 0.05f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 l = inv * (points[i] - at);
                d = Mathf.Max(d, Mathf.Abs(l.x) / th - l.z, Mathf.Abs(l.y) / tv - l.z, 0.05f - l.z);
            }
            return d;
        }

        void Ensure()
        {
            if (_root != null) return;
            _root = new GameObject("PortraitStudio");
            if (_app.WorldRoot != null) _root.transform.SetParent(_app.WorldRoot, false);
            var camGo = new GameObject("PortraitCamera");
            camGo.transform.SetParent(_root.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.cullingMask = 1 << Layers.Hidden;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = _paper;
            _cam.aspect = Aspect;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.useOcclusionCulling = false;
            var keyGo = new GameObject("PortraitKey");
            keyGo.transform.SetParent(_root.transform, false);
            _key = keyGo.AddComponent<Light>();
            _key.type = LightType.Directional;
            _key.color = KeyColor;
            _key.intensity = KeyIntensity;
            _key.shadows = LightShadows.None;
            _key.cullingMask = 1 << Layers.Hidden;
            _key.enabled = false;
            _big = new RenderTexture(BandW * Supersample, BandH * Supersample, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "PortraitRender", antiAliasing = 1, filterMode = FilterMode.Bilinear,
            };
            _big.Create();
            // URP treats a camera without a target texture as an XR camera (XRLayout.AddCamera: xrSupported needs
            // targetTexture == null), and stereoTargetEye is a built-in-renderer setting it ignores: on the headset
            // the portraits were drawn into the eye buffer and showed as cream "paper" tiles in the right eye.
            _cam.targetTexture = _big;
            _small = new RenderTexture(BandW, BandH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "PortraitBand", filterMode = FilterMode.Bilinear };
            _small.Create();
        }

        /// <summary>The camera, the key and the two work targets go once nothing is left to render (the atlas stays).</summary>
        void ReleaseWork()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (_big != null) { _big.Release(); Object.Destroy(_big); _big = null; }
            if (_small != null) { _small.Release(); Object.Destroy(_small); _small = null; }
            if (_root != null) { Object.Destroy(_root); _root = null; _cam = null; _key = null; }
        }

        static void Clear(RenderTexture rt, Color c)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, c.linear);
            RenderTexture.active = prev;
        }

        public void Dispose()
        {
            ReleaseWork();
            if (Atlas != null) { Atlas.Release(); Object.Destroy(Atlas); Atlas = null; }
        }
    }
}
