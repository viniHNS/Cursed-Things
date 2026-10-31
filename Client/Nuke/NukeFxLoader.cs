using System.IO;
using Comfort.Common;
using EFT.CameraControl;
using UnityEngine;

namespace viniHNS.CursedThings.Nuke
{
    /// <summary>
    /// Loads nuke_fx.bundle (BepInEx/plugins/CursedThings/) the first time a
    /// nuke's fuse starts, without blocking: bundle, prefab and shader variant
    /// collection load asynchronously and the shaders are warmed up while the
    /// fuse runs. The bundle stays loaded for the session. Spawn() returns
    /// false while it is not ready or if it failed (the caller then falls back
    /// to the stage 1 placeholder).
    /// </summary>
    internal static class NukeFxLoader
    {
        private const string BundleFile = "nuke_fx.bundle";
        private const string PrefabAsset = "assets/cursedthingsnuke/prefabs/nukefx.prefab";
        private const string VariantsAsset = "assets/cursedthingsnuke/prefabs/nukefx_shaders.shadervariants";

        private enum State { Idle, Loading, Ready, Failed }

        private static State _state = State.Idle;
        private static AssetBundle _bundle;
        private static GameObject _prefab;
        // One copy instantiated during the fuse under an inactive holder, so the
        // detonation frame only moves and switches it on.
        private static GameObject _spareHolder;
        private static GameObject _spare;

        public static bool IsReady => _state == State.Ready;

        public static void BeginLoad()
        {
            if (_state == State.Ready)
            {
                PrepareSpare();
                return;
            }
            if (_state != State.Idle) return;
            string path = FindBundle();
            if (path == null)
            {
                Fail("nuke_fx.bundle not found next to CursedThings.dll (expected BepInEx/plugins/CursedThings/nuke_fx.bundle)");
                return;
            }
            _state = State.Loading;
            AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(path);
            request.completed += _ =>
            {
                _bundle = request.assetBundle;
                if (_bundle == null)
                {
                    Fail("could not open " + path);
                    return;
                }
                AssetBundleRequest prefabRequest = _bundle.LoadAssetAsync<GameObject>(PrefabAsset);
                prefabRequest.completed += __ =>
                {
                    _prefab = prefabRequest.asset as GameObject;
                    if (_prefab == null)
                    {
                        Fail("prefab missing in " + BundleFile);
                        return;
                    }
                    AssetBundleRequest svcRequest = _bundle.LoadAssetAsync<ShaderVariantCollection>(VariantsAsset);
                    svcRequest.completed += ___ =>
                    {
                        var svc = svcRequest.asset as ShaderVariantCollection;
                        if (svc != null && !svc.isWarmedUp) svc.WarmUp();
                        NukeAudio.SetBundleClips(_bundle.LoadAllAssets<AudioClip>());
                        _state = State.Ready;
                        Plugin.LogSource.LogInfo("[CursedThings] nuke effects loaded");
                        PrepareSpare();
                    };
                };
            };
        }

        /// <summary>Loads synchronously if the async load has not started (fuse shorter than the load).</summary>
        public static void EnsureLoadedNow()
        {
            if (_state != State.Idle) return;
            string path = FindBundle();
            if (path == null)
            {
                Fail("nuke_fx.bundle not found");
                return;
            }
            _bundle = AssetBundle.LoadFromFile(path);
            _prefab = _bundle != null ? _bundle.LoadAsset<GameObject>(PrefabAsset) : null;
            if (_prefab == null)
            {
                Fail("could not load " + path);
                return;
            }
            var svc = _bundle.LoadAsset<ShaderVariantCollection>(VariantsAsset);
            if (svc != null) svc.WarmUp();
            NukeAudio.SetBundleClips(_bundle.LoadAllAssets<AudioClip>());
            _state = State.Ready;
        }

        public static bool Spawn(Vector3 position, out NukeFx fx)
        {
            fx = null;
            if (_state == State.Idle) EnsureLoadedNow();
            if (_state == State.Loading) Plugin.LogSource.LogWarning("[CursedThings] nuke effects still loading: placeholder used");
            if (_state != State.Ready) return false;

            bool indoor = IsIndoor(position, out float ceiling);
            GameObject go;
            if (_spare != null)
            {
                go = _spare;
                _spare = null;
                go.transform.SetParent(null, false);
                go.transform.SetPositionAndRotation(position, Quaternion.identity);
            }
            else
            {
                go = Object.Instantiate(_prefab, position, Quaternion.identity);
            }
            go.name = "CursedThings_NukeFX";
            fx = go.AddComponent<NukeFx>();
            fx.ObstacleMask = LayersMaskController.GrenadeObstaclesColliderMask;
            fx.BeforeFrame = RefreshLighting;
            RefreshLighting(fx);
            float quality = Plugin.Nuke_EffectsQuality.Value == NukeEffectsQuality.Low ? 0.5f : 1f;
            fx.Init(indoor, ceiling, quality, Random.Range(1, int.MaxValue));
            Plugin.LogSource.LogInfo($"[CursedThings] nuke effects: {(indoor ? $"indoor (ceiling {ceiling:0.0} m)" : "outdoor")}, quality {quality}");
            return true;
        }

        private static void PrepareSpare()
        {
            if (_state != State.Ready || _spare != null) return;
            // The holder lives in the raid scene and goes away with it.
            if (_spareHolder == null)
            {
                _spareHolder = new GameObject("CursedThings_NukeFX_Spare");
                _spareHolder.SetActive(false);
            }
            _spare = Object.Instantiate(_prefab, _spareHolder.transform);
        }

        private static string FindBundle()
        {
            string dir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? string.Empty;
            foreach (string candidate in new[] { Path.Combine(dir, "CursedThings", BundleFile), Path.Combine(dir, BundleFile) })
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static void Fail(string message)
        {
            _state = State.Failed;
            Plugin.LogSource.LogError("[CursedThings] nuke effects disabled: " + message);
        }

        // A roof over the blast: the straight up ray and most of the tilted
        // ones hit something within 30 m (a lone bridge or arch is not enough).
        public static bool IsIndoor(Vector3 position, out float ceiling)
        {
            int mask = LayersMaskController.GrenadeObstaclesColliderMask;
            Vector3 origin = position + Vector3.up * 0.5f;
            ceiling = 30f;
            if (!Physics.Raycast(origin, Vector3.up, out RaycastHit up, 30f, mask, QueryTriggerInteraction.Ignore)) return false;
            ceiling = up.distance + 0.5f;
            int hits = 0;
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                var dir = new Vector3(Mathf.Cos(a) * 0.6f, 1f, Mathf.Sin(a) * 0.6f).normalized;
                if (Physics.Raycast(origin, dir, 35f, mask, QueryTriggerInteraction.Ignore)) hits++;
            }
            return hits >= 4;
        }

        // Sun (or moon), sky and haze of the current map and time of day.
        private static void RefreshLighting(NukeFx fx)
        {
            fx.ViewCamera = CameraManager.Instance.Camera;
            TOD_Sky sky = Singleton<TOD_Sky>.Instantiated ? Singleton<TOD_Sky>.Instance : null;
            if (sky != null && sky.Components != null && sky.LightObject != null)
            {
                fx.SunDirection = sky.LightDirection;
                Light light = sky.LightObject;
                fx.SunLight = light;
                fx.SunColor = light.color * light.intensity;
                fx.Ambient = Color.Lerp(sky.AmbientColor, RenderSettings.ambientLight, 0.5f);
                fx.HazeColor = sky.SampleFogColor();
            }
            else
            {
                Light sun = RenderSettings.sun;
                if (sun != null)
                {
                    fx.SunLight = sun;
                    fx.SunDirection = -sun.transform.forward;
                    fx.SunColor = sun.color * sun.intensity;
                }
                fx.Ambient = RenderSettings.ambientLight;
                fx.HazeColor = RenderSettings.fogColor;
            }
            fx.HazeDensity = HazeDensity();
        }

        private static float HazeDensity()
        {
            if (!RenderSettings.fog) return 0.0004f;
            float density;
            switch (RenderSettings.fogMode)
            {
                case FogMode.Linear:
                    density = 2f / Mathf.Max(50f, RenderSettings.fogEndDistance);
                    break;
                default:
                    density = RenderSettings.fogDensity;
                    break;
            }
            return Mathf.Clamp(density, 0.0002f, 0.003f);
        }
    }

    public enum NukeEffectsQuality
    {
        High,
        Low,
    }
}
