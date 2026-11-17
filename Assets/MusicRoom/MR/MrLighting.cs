// Mixed-reality lighting (spec 07 section 9): the virtual room's lights go with the room (IRoom.SetVisible(false)); in their
// place a room-neutral Trilight ambient (#b1b1b1 / #969696 / #686868, the design's hemisphere light 0.85 converted) and one
// white sun 1.3/pi from a fixed point (0.6, 2.4, -1.2) looking at the piano, the only realtime shadow caster of the app
// (one 1024 hard map, URP asset: MR only), plus the shadow catcher: a 2.6 x 1.8 m plane on the real surface under the piano
// that shows only the instruments' shadows (alpha .32). Entering saves the VR ambient and sun; leaving restores them.
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.MR
{
    public sealed class MrLighting
    {
        readonly Light _sun;
        readonly GameObject _catcher;
        readonly Material _catcherMaterial;
        readonly Mesh _catcherMesh;

        bool _saved;
        AmbientMode _mode;
        Color _sky, _equator, _ground;
        float _intensity;
        Light _vrSun;

        public Light Sun => _sun;
        public Transform Catcher => _catcher.transform;
        public bool CatcherVisible => _catcher.activeSelf;
        public bool Active { get; private set; }

        public MrLighting(Transform parent, MrMaterials materials)
        {
            var go = new GameObject("MR sun");
            go.transform.SetParent(parent, false);
            go.transform.position = MrConstants.SunPosition;
            _sun = go.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.color = Color.white;
            _sun.intensity = MrConstants.SunIntensity;
            // ARCHITECTURE.md 12 / 14: one 1024 HARD map (soft shadows stay off in QuestURP); the catcher filters its own
            // 3 x 3 PCF, so the shadow on the real surface is still soft-edged.
            _sun.shadows = LightShadows.Hard;
            _sun.shadowStrength = 1f;
            _sun.renderMode = LightRenderMode.ForcePixel;
            go.SetActive(false);

            _catcher = new GameObject("Shadow catcher");
            _catcher.transform.SetParent(parent, false);
            _catcherMaterial = materials.Create(MrMaterials.ShadowCatcher);
            _catcherMesh = MrMeshes.RectXZ(MrConstants.CatcherWidth, MrConstants.CatcherDepth, 0f);
            var r = MrMeshes.AddPart(_catcher.transform, "Catcher", _catcherMesh, _catcherMaterial);
            r.receiveShadows = true;
            _catcher.SetActive(false);
        }

        /// <summary>Mixed reality on: MR ambient and sun (the VR values are kept for Exit).</summary>
        public void Enter()
        {
            if (Active) return;
            Active = true;
            if (!_saved)
            {
                _mode = RenderSettings.ambientMode;
                _sky = RenderSettings.ambientSkyColor;
                _equator = RenderSettings.ambientEquatorColor;
                _ground = RenderSettings.ambientGroundColor;
                _intensity = RenderSettings.ambientIntensity;
                _vrSun = RenderSettings.sun;
                _saved = true;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = MrConstants.AmbientSky;
            RenderSettings.ambientEquatorColor = MrConstants.AmbientEquator;
            RenderSettings.ambientGroundColor = MrConstants.AmbientGround;
            RenderSettings.ambientIntensity = 1f;
            _sun.gameObject.SetActive(true);
            RenderSettings.sun = _sun; // URP's main light: the one shadowed light
        }

        /// <summary>Back to the virtual room: its ambient and sun return, the MR sun and the catcher go.</summary>
        public void Exit()
        {
            if (!Active) return;
            Active = false;
            _sun.gameObject.SetActive(false);
            _catcher.SetActive(false);
            if (_saved)
            {
                RenderSettings.ambientMode = _mode;
                RenderSettings.ambientSkyColor = _sky;
                RenderSettings.ambientEquatorColor = _equator;
                RenderSettings.ambientGroundColor = _ground;
                RenderSettings.ambientIntensity = _intensity;
                RenderSettings.sun = _vrSun;
                _saved = false;
            }
        }

        /// <summary>The sun's target (the piano / the layout anchor).</summary>
        public void Aim(Vector3 target)
        {
            Vector3 d = target - MrConstants.SunPosition;
            if (d.sqrMagnitude > 1e-8f) _sun.transform.rotation = Quaternion.LookRotation(d, Vector3.up);
        }

        /// <summary>The catcher under the anchor: its x/z and heading, 2 mm over the surface at <paramref name="surfaceY"/>.</summary>
        public void PlaceCatcher(Vector3 anchor, float yawDeg, float surfaceY)
        {
            _catcher.transform.SetPositionAndRotation(new Vector3(anchor.x, surfaceY + MrConstants.CatcherLift, anchor.z), Quaternion.Euler(0f, yawDeg, 0f));
            if (!_catcher.activeSelf && Active) _catcher.SetActive(true);
        }

        public void HideCatcher()
        {
            if (_catcher.activeSelf) _catcher.SetActive(false);
        }

        public void Destroy()
        {
            Exit();
            MrUtil.Destroy(_catcherMaterial);
            MrUtil.Destroy(_catcherMesh);
            MrUtil.Destroy(_catcher);
            if (_sun != null) MrUtil.Destroy(_sun.gameObject);
        }
    }
}
