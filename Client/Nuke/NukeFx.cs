using System.Collections.Generic;
using UnityEngine;

namespace viniHNS.CursedThings.Nuke
{
    /// <summary>
    /// Drives the NukeFX prefab from nuke_fx.bundle. The prefab only holds
    /// stock Unity components (meshes, one particle system, one light); every
    /// piece is placed here as a function of the time since detonation:
    ///
    /// flash (0-1 s, double pulse), fireball (a shell of fire puffs from the
    /// flipbook around a small procedural core; rises, cools to smoke by
    /// ~16 s), shock dome (refraction, at the speed of
    /// the gameplay front), condensation veil (0.2-3.5 s), mushroom cap
    /// (smoke on a rolling torus that grows with the fireball), stem (dust
    /// spiralling up into the cap) and base surge (dust ring pushed out by
    /// the front). Indoors: small fireball, dust filling the room and soot
    /// under the ceiling, no mushroom.
    ///
    /// Uses only UnityEngine, so the same file runs in the Unity preview.
    /// </summary>
    public class NukeFx : MonoBehaviour
    {
        public const float OutdoorLifetime = 90f;
        public const float IndoorLifetime = 60f;
        private const float ShockSpeed = 340f;

        // Lighting inputs; the owner refreshes them every frame.
        public Vector3 SunDirection = new Vector3(0.4f, 0.7f, 0.3f);
        public Color SunColor = new Color(1f, 0.95f, 0.85f);
        public Color Ambient = new Color(0.35f, 0.4f, 0.48f);
        public Color HazeColor = new Color(0.6f, 0.65f, 0.72f);
        public float HazeDensity = 0.0006f;
        /// <summary>Viewer; the outdoor light shines from the fireball towards it.</summary>
        public Camera ViewCamera;
        /// <summary>The map's sun: its shadow bias and culling mask are copied to the blast light.</summary>
        public Light SunLight;
        /// <summary>Layers that stop the indoor dust (walls); set before Init.</summary>
        public int ObstacleMask = Physics.DefaultRaycastLayers;
        /// <summary>Called every frame before the effect is placed (refresh the lighting inputs here).</summary>
        public System.Action<NukeFx> BeforeFrame;

        private bool _indoor;
        private float _ceiling;
        private float _lifetime;
        private float _startTime;
        private bool _manualTime;
        private bool _initialized;

        private Transform _fireball, _condensation, _shock, _flash;
        private Renderer _fireballR, _condensationR, _shockR, _flashR, _smokeR;
        private Light _light;
        private ParticleSystem _smoke;
        private MaterialPropertyBlock _mpb;
        private bool _distortion;
        private Vector3 _sun = Vector3.up;

        // Particles: [0, _fireEnd) fire shell, [_fireEnd, _cap) cap, [_cap, _stemEnd) stem,
        // [_stemEnd, _count) dust. All use the same flipbook (two 32-frame sequences).
        private int _fireEnd, _cap, _stemEnd, _count;
        private float[] _r0, _r1, _r2, _r3, _r4, _r5, _r6;
        private int[] _sequence;
        private const int SequenceLength = 32;
        private float[] _reach;
        private ParticleSystem.Particle[] _particles;
        private List<Vector4> _custom;

        private static readonly int SunDirId = Shader.PropertyToID("_NukeSunDir");
        private static readonly int SunColorId = Shader.PropertyToID("_NukeSunColor");
        private static readonly int AmbientId = Shader.PropertyToID("_NukeAmbient");
        private static readonly int HazeId = Shader.PropertyToID("_NukeHaze");
        private static readonly int GlowPosId = Shader.PropertyToID("_NukeGlowPos");
        private static readonly int GlowColorId = Shader.PropertyToID("_NukeGlowColor");
        private static readonly int HeatId = Shader.PropertyToID("_Heat");
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private static readonly int FxTimeId = Shader.PropertyToID("_FxTime");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int BandPhaseId = Shader.PropertyToID("_BandPhase");
        private static readonly int EvaporateId = Shader.PropertyToID("_Evaporate");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int SourceRadiusId = Shader.PropertyToID("_SourceRadius");

        /// <param name="indoor">Small blast with no mushroom (roof overhead).</param>
        /// <param name="ceiling">Height of the roof above the blast when indoor.</param>
        /// <param name="quality">1 = full particle count and distortion, 0.5 = low.</param>
        public void Init(bool indoor, float ceiling, float quality, int seed)
        {
            _indoor = indoor;
            _ceiling = Mathf.Clamp(ceiling, 3f, 30f);
            _lifetime = indoor ? IndoorLifetime : OutdoorLifetime;
            _distortion = quality > 0.75f;
            _startTime = Time.time;
            _mpb = new MaterialPropertyBlock();

            _fireball = Child("Fireball", out _fireballR);
            _condensation = Child("Condensation", out _condensationR);
            _shock = Child("Shock", out _shockR);
            _flash = Child("Flash", out _flashR);
            Transform smoke = transform.Find("Smoke");
            _smoke = smoke != null ? smoke.GetComponent<ParticleSystem>() : null;
            _smokeR = smoke != null ? smoke.GetComponent<ParticleSystemRenderer>() : null;
            Transform light = transform.Find("Light");
            _light = light != null ? light.GetComponent<Light>() : null;
            SetupLight(quality);

            if (_condensation != null) _condensation.gameObject.SetActive(!indoor);

            float q = Mathf.Clamp(quality, 0.3f, 1f);
            int fire = indoor ? Mathf.RoundToInt(50 * q) : Mathf.RoundToInt(170 * q);
            int cap = indoor ? 0 : Mathf.RoundToInt(560 * q);
            int stem = indoor ? Mathf.RoundToInt(140 * q) : Mathf.RoundToInt(240 * q);
            int dust = indoor ? Mathf.RoundToInt(260 * q) : Mathf.RoundToInt(460 * q);
            _fireEnd = fire;
            _cap = fire + cap;
            _stemEnd = _cap + stem;
            _count = _stemEnd + dust;
            if (_smoke != null)
            {
                var main = _smoke.main;
                main.maxParticles = Mathf.Max(main.maxParticles, _count);
            }

            var rng = new System.Random(seed);
            _r0 = Randoms(rng); _r1 = Randoms(rng); _r2 = Randoms(rng); _r3 = Randoms(rng);
            _r4 = Randoms(rng); _r5 = Randoms(rng); _r6 = Randoms(rng);
            _sequence = new int[_count];
            for (int i = 0; i < _count; i++) _sequence[i] = rng.Next(2) * SequenceLength;
            _reach = new float[_count];
            if (indoor) MeasureRoom();
            _particles = new ParticleSystem.Particle[_count];
            _custom = new List<Vector4>(_count);
            for (int i = 0; i < _count; i++)
            {
                _particles[i].startLifetime = 1000f;
                _particles[i].remainingLifetime = 1000f;
                _particles[i].randomSeed = (uint)(i * 7919 + 1);
                _custom.Add(Vector4.zero);
            }
            if (_smoke != null)
            {
                _smoke.gameObject.SetActive(true);
                _smoke.Play(true);
            }
            if (_fireballR != null) _fireballR.gameObject.SetActive(true);
            _initialized = true;
            Evaluate(0f);
        }

        /// <summary>Preview only: freeze the effect at a given time.</summary>
        public void SetTime(float t)
        {
            _manualTime = true;
            Evaluate(t);
        }

        private float[] Randoms(System.Random rng)
        {
            var a = new float[_count];
            for (int i = 0; i < a.Length; i++) a[i] = (float)rng.NextDouble();
            return a;
        }

        // Indoors: how far each puff can travel before it would poke through a wall.
        private void MeasureRoom()
        {
            Vector3 origin = transform.position + Vector3.up * 1f;
            for (int i = _cap; i < _count; i++)
            {
                Vector3 dir = IndoorDirection(i);
                _reach[i] = Physics.Raycast(origin, dir, out RaycastHit hit, 40f, ObstacleMask, QueryTriggerInteraction.Ignore)
                    ? Mathf.Max(0.5f, hit.distance - 1.5f)
                    : 40f;
            }
        }

        private Vector3 IndoorDirection(int i)
        {
            float phi = _r0[i] * Mathf.PI * 2f;
            float up = i < _stemEnd ? 0f : (_r1[i] * 2f - 1f) * 0.5f;
            return new Vector3(Mathf.Cos(phi), up, Mathf.Sin(phi)).normalized;
        }

        private Transform Child(string name, out Renderer renderer)
        {
            Transform t = transform.Find(name);
            renderer = t != null ? t.GetComponent<Renderer>() : null;
            return t;
        }

        private void LateUpdate()
        {
            if (!_initialized || _manualTime) return;
            float t = Time.time - _startTime;
            if (t > _lifetime)
            {
                Destroy(gameObject);
                return;
            }
            BeforeFrame?.Invoke(this);
            Evaluate(t);
        }

        // ------------------------------------------------------------ curves

        private static float Grow(float t, float tau) => 1f - Mathf.Exp(-Mathf.Max(0f, t) / tau);

        private static float Smooth(float a, float b, float x)
        {
            float k = Mathf.Clamp01((x - a) / (b - a));
            return k * k * (3f - 2f * k);
        }

        private float FireballRadius(float t) => _indoor
            ? Mathf.Min(9f, 0.42f * _ceiling) * Grow(t, 0.2f)
            : 36f * Grow(t, 0.3f) + 14f * Grow(t, 8f);

        private float Heat(float t) => Mathf.Exp(-t / (_indoor ? 1.8f : 5.5f));

        private float FireballY(float t, float radius) => _indoor
            ? _ceiling * 0.5f
            : 0.35f * radius + 165f * Grow(t - 1f, 13f);

        private float CapRadius(float t, float radius) => radius + 50f * Grow(t - 2f, 16f);

        private float FlashIntensity(float t)
        {
            // Two pulses, as seen in real detonations.
            float first = Mathf.Exp(-t / 0.07f);
            float d = (t - 0.35f) / 0.18f;
            float second = 0.45f * Mathf.Exp(-d * d) * Smooth(0.05f, 0.2f, t);
            return (first + second) * (1f - Smooth(0.8f, 1.2f, t));
        }

        private float Fade(float t) => 1f - Smooth(_lifetime * 0.72f, _lifetime * 0.98f, t);

        // -------------------------------------------------------------- frame

        private void Evaluate(float t)
        {
            Vector3 origin = transform.position;
            float radius = FireballRadius(t);
            float heat = Heat(t);
            Vector3 ballCenter = origin + Vector3.up * FireballY(t, radius);
            float flash = FlashIntensity(t);

            Color glow = HeatColor(heat * 0.9f) * (_indoor ? 1.2f : 1.5f) + Color.white * flash * 4f;
            _sun = SunDirection.sqrMagnitude > 1e-6f ? SunDirection.normalized : Vector3.up;
            Vector4 sunDir = _sun;
            Vector4 haze = new Vector4(HazeColor.r, HazeColor.g, HazeColor.b, HazeDensity);
            Vector4 glowPos = new Vector4(ballCenter.x, ballCenter.y, ballCenter.z, Mathf.Max(1f, radius));

            UpdateFireball(t, radius, heat, ballCenter, sunDir, haze, glowPos, glow);
            UpdateShock(t, origin, sunDir, haze, glowPos, glow);
            if (!_indoor) UpdateCondensation(t, origin, sunDir, haze, glowPos, glow);
            UpdateFlash(t, flash, radius, ballCenter);
            UpdateLight(t, flash, heat, ballCenter);
            UpdateSmoke(t, origin, radius, heat, ballCenter, sunDir, haze, glowPos, glow);
        }

        private void SetLighting(Vector4 sunDir, Vector4 haze, Vector4 glowPos, Color glow)
        {
            _mpb.SetVector(SunDirId, sunDir);
            _mpb.SetColor(SunColorId, SunColor);
            _mpb.SetColor(AmbientId, Ambient);
            _mpb.SetVector(HazeId, haze);
            _mpb.SetVector(GlowPosId, glowPos);
            _mpb.SetColor(GlowColorId, glow);
        }

        private void UpdateFireball(float t, float radius, float heat, Vector3 center, Vector4 sunDir, Vector4 haze, Vector4 glowPos, Color glow)
        {
            if (_fireball == null) return;
            float dissolve = _indoor ? Smooth(3f, 8f, t) : Smooth(2.5f, 8f, t);
            bool on = radius > 0.05f && dissolve < 0.999f;
            if (_fireball.gameObject.activeSelf != on) _fireball.gameObject.SetActive(on);
            if (!on) return;
            float squash = _indoor ? 1f : 1f - 0.22f * Smooth(4f, 20f, t);
            _fireball.position = center;
            // A bright core inside the fire shell; outdoors it shrinks as the shell takes over.
            float r = _indoor ? radius * 0.85f : radius * 0.8f * Mathf.Lerp(1f, 0.55f, Smooth(1f, 7f, t));
            _fireball.localScale = new Vector3(r, r * squash, r);
            _mpb.Clear();
            SetLighting(sunDir, haze, glowPos, glow * 0.2f);
            _mpb.SetFloat(HeatId, Mathf.Clamp01(heat * 1.05f));
            _mpb.SetFloat(DissolveId, dissolve);
            _mpb.SetFloat(FxTimeId, t);
            _mpb.SetFloat(SeedId, 3.7f);
            _fireballR.SetPropertyBlock(_mpb);
        }

        private void UpdateShock(float t, Vector3 origin, Vector4 sunDir, Vector4 haze, Vector4 glowPos, Color glow)
        {
            if (_shock == null) return;
            float end = _indoor ? 0.6f : 2.2f;
            float r = ShockSpeed * t;
            bool on = _distortion && t < end && r > 3f;
            if (_shock.gameObject.activeSelf != on) _shock.gameObject.SetActive(on);
            if (!on) return;
            _shock.position = origin;
            _shock.localScale = Vector3.one * r;
            float strength = (_indoor ? 0.03f : 0.05f) * Mathf.Exp(-t / 0.8f) * (1f - Smooth(end * 0.7f, end, t));
            _mpb.Clear();
            SetLighting(sunDir, haze, glowPos, glow);
            _mpb.SetFloat(StrengthId, strength);
            _mpb.SetFloat(FxTimeId, t);
            _shockR.SetPropertyBlock(_mpb);
        }

        private void UpdateCondensation(float t, Vector3 origin, Vector4 sunDir, Vector4 haze, Vector4 glowPos, Color glow)
        {
            if (_condensation == null) return;
            bool on = t > 0.15f && t < 2.8f;
            if (_condensation.gameObject.activeSelf != on) _condensation.gameObject.SetActive(on);
            if (!on) return;
            float r = 60f + 90f * Grow(t - 0.15f, 0.8f);
            _condensation.position = origin;
            _condensation.localScale = new Vector3(r, r * 0.8f, r);
            _mpb.Clear();
            SetLighting(sunDir, haze, glowPos, glow);
            _mpb.SetFloat(OpacityId, 0.55f * Smooth(0.15f, 0.45f, t) * (1f - Smooth(1.6f, 2.8f, t)));
            _mpb.SetFloat(EvaporateId, 0.2f + 0.55f * Smooth(0.6f, 2.6f, t));
            _mpb.SetFloat(BandPhaseId, t * 0.3f);
            _mpb.SetFloat(FxTimeId, t);
            _condensationR.SetPropertyBlock(_mpb);
        }

        private void UpdateFlash(float t, float flash, float radius, Vector3 center)
        {
            if (_flash == null) return;
            bool on = flash > 0.002f;
            if (_flash.gameObject.activeSelf != on) _flash.gameObject.SetActive(on);
            if (!on) return;
            // The shader hides it behind walls (scene depth around the fireball).
            _flash.position = center;
            _flash.localScale = Vector3.one * (_indoor ? 60f : 300f);
            _mpb.Clear();
            _mpb.SetFloat(IntensityId, flash * (_indoor ? 3f : 6f));
            _mpb.SetFloat(SourceRadiusId, Mathf.Max(radius, _indoor ? 2f : 8f));
            _mpb.SetFloat(FxTimeId, t);
            _flashR.SetPropertyBlock(_mpb);
        }

        // The blast light casts shadows, so walls, roofs and hills stop it.
        // Outdoors it is a directional light from the fireball towards the
        // viewer: the map's shadow cascades give sharp shadows around the
        // player (the light only comes in through windows and doors) at the
        // cost of a second sun for the few seconds it is on. Indoors a short
        // point light with a cube shadow keeps it in the room.
        private void SetupLight(float quality)
        {
            if (_light == null) return;
            _light.type = _indoor ? LightType.Point : LightType.Directional;
            _light.shadows = quality > 0.75f ? LightShadows.Soft : LightShadows.Hard;
            _light.shadowResolution = quality > 0.75f ? UnityEngine.Rendering.LightShadowResolution.FromQualitySettings
                : UnityEngine.Rendering.LightShadowResolution.Medium;
            _light.shadowStrength = 1f;
            _light.renderMode = LightRenderMode.ForcePixel;
            _light.bounceIntensity = 0f;
            _light.range = IndoorLightRange;
            if (SunLight != null)
            {
                _light.cullingMask = SunLight.cullingMask;
                _light.shadowBias = SunLight.shadowBias;
                _light.shadowNormalBias = SunLight.shadowNormalBias;
                _light.shadowNearPlane = SunLight.shadowNearPlane;
            }
            _light.enabled = false;
        }

        private const float IndoorLightRange = 40f;

        private void UpdateLight(float t, float flash, float heat, Vector3 center)
        {
            if (_light == null) return;
            float glow = heat * (_indoor ? 3f : 5f) * Fade(t);
            float intensity;
            if (_indoor)
            {
                intensity = flash * 8f + glow;
                _light.transform.position = center;
            }
            else
            {
                // A directional light does not fade with distance: do it here
                // from the viewer's distance (the flash carries further than the glow).
                Camera cam = ViewCamera != null ? ViewCamera : Camera.main;
                Vector3 eye = cam != null ? cam.transform.position : center + Vector3.forward * 300f;
                Vector3 toEye = eye - center;
                float d = toEye.magnitude;
                intensity = flash * 8f / (1f + Sq(d / 300f)) + glow / (1f + Sq(d / 150f));
                if (d > 0.5f) _light.transform.rotation = Quaternion.LookRotation(toEye / d);
            }
            bool on = intensity > 0.03f;
            if (_light.enabled != on) _light.enabled = on;
            if (!on) return;
            _light.intensity = intensity;
            _light.color = Color.Lerp(new Color(1f, 0.55f, 0.25f), new Color(1f, 0.95f, 0.88f), Mathf.Clamp01(flash * 2f + heat * 0.3f));
        }

        private static float Sq(float x) => x * x;

        private void UpdateSmoke(float t, Vector3 origin, float radius, float heat, Vector3 ballCenter,
            Vector4 sunDir, Vector4 haze, Vector4 glowPos, Color glow)
        {
            if (_smoke == null || _count == 0) return;
            float fade = Fade(t);
            if (_indoor) UpdateIndoorSmoke(t, origin, radius, heat, fade);
            else UpdateOutdoorSmoke(t, origin, radius, heat, ballCenter, fade);
            // Custom data is picked up by the next SetParticles: once the count
            // matches, send it first so both describe the same frame.
            if (_smoke.particleCount == _count)
            {
                _smoke.SetCustomParticleData(_custom, ParticleSystemCustomData.Custom1);
                _smoke.SetParticles(_particles, _count);
            }
            else
            {
                _smoke.SetParticles(_particles, _count);
                _smoke.SetCustomParticleData(_custom, ParticleSystemCustomData.Custom1);
            }

            _mpb.Clear();
            SetLighting(sunDir, haze, glowPos, glow);
            _smokeR.SetPropertyBlock(_mpb);
        }

        // Flipbook frame of particle i: its own sequence, from `start`, advancing `rate` frames per second.
        private float Frame(int i, float t, float start, float rate)
        {
            return _sequence[i] + Mathf.Min(SequenceLength - 1.001f, start + rate * t);
        }

        private void SetParticle(int i, Vector3 position, float size, Color albedo, float opacity, float heat, float rotation, float sunVisibility, float frame)
        {
            _particles[i].position = position;
            _particles[i].startSize = Mathf.Max(0.01f, size);
            _particles[i].startColor = new Color(albedo.r, albedo.g, albedo.b, Mathf.Clamp01(opacity));
            _particles[i].remainingLifetime = 1000f;
            _custom[i] = new Vector4(heat, frame, rotation, sunVisibility);
        }

        private void UpdateOutdoorSmoke(float t, Vector3 origin, float radius, float heat, Vector3 ballCenter, float fade)
        {
            float capR = CapRadius(t, radius);
            float squash = 0.6f;
            float major = 0.6f * capR;
            // Rolling: fast at first, slowing down.
            float roll = 0.55f * 18f * Grow(t, 18f) + 0.03f * t;
            float ember = 0.2f * Mathf.Exp(-t / 30f);
            float freshSoot = Mathf.Lerp(0.55f, 0.15f, Smooth(5f, 45f, t));
            var soot = new Color(0.2f, 0.18f, 0.17f);
            UpdateFireShell(t, radius, heat, ballCenter, fade);
            int dome = _fireEnd + Mathf.RoundToInt((_cap - _fireEnd) * 0.33f);

            for (int i = _fireEnd; i < _cap; i++)
            {
                Vector3 p;
                float size, h, appear, sin;
                if (i < dome)
                {
                    // Top of the cap: a dome whose surface flows outward to the rim.
                    float a = Mathf.Repeat(_r1[i] + 0.006f * t, 1f);
                    float ang = a * 1.25f;
                    float phi = _r0[i] * Mathf.PI * 2f + 0.01f * t;
                    float depth = 0.82f + 0.18f * _r2[i];
                    var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                    p = ballCenter + dir * (Mathf.Sin(ang) * capR * 0.85f * depth) + Vector3.up * (Mathf.Cos(ang) * capR * 0.45f * depth);
                    p += Wobble(i, t) * (0.08f * capR);
                    size = capR * (0.36f + 0.18f * _r4[i]);
                    appear = Smooth(3f + 4f * _r3[i], 5f + 4f * _r3[i], t) * Smooth(0f, 0.08f, a) * (1f - Smooth(0.85f, 1f, a));
                    sin = 1f;
                    h = heat * 0.5f * (0.7f + 0.3f * _r6[i]);
                }
                else
                {
                    // Rolling rim: a torus turning outward over the top.
                    float phi = _r0[i] * Mathf.PI * 2f + 0.01f * t;
                    float rho = 0.55f + 0.45f * Mathf.Sqrt(_r2[i]);
                    float theta = _r1[i] * Mathf.PI * 2f - roll * (0.8f + 0.4f * _r5[i]);
                    float tube = 0.5f * capR * rho;
                    float cos = Mathf.Cos(theta);
                    sin = Mathf.Sin(theta);
                    var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                    p = ballCenter + dir * (major + tube * cos) + Vector3.up * (tube * sin * squash);
                    p += Wobble(i, t) * (0.1f * capR);
                    size = capR * (0.38f + 0.2f * _r4[i]) * (0.85f + 0.15f * rho);
                    appear = Smooth(2.5f + 4f * _r3[i], 4.5f + 4f * _r3[i], t);
                    float under = Mathf.Clamp01(0.55f - 0.5f * sin);
                    h = heat * 1.1f * under * (0.7f + 0.3f * _r6[i]) + ember * Mathf.Clamp01(-sin) * rho;
                }
                Color albedo = Color.Lerp(new Color(0.8f, 0.75f, 0.7f), new Color(0.6f, 0.55f, 0.5f), _r6[i]);
                // Underside: browner and darker.
                albedo = Color.Lerp(albedo, new Color(0.42f, 0.35f, 0.3f), Mathf.Clamp01(-sin) * 0.6f);
                albedo = Color.Lerp(albedo, soot, freshSoot * (0.6f + 0.4f * _r5[i]));
                float along = Vector3.Dot(p - ballCenter, _sun) / capR;
                float vis = Mathf.Lerp(0.22f, 1f, Smooth(-0.85f, 0.45f, along));
                SetParticle(i, p, size, albedo, 0.7f * appear * fade, h, _r5[i] * 6.283f + t * (_r4[i] - 0.5f) * 0.05f, vis,
                    Frame(i, t, 20f + 6f * _r3[i], 0.1f));
            }

            float capBottom = Mathf.Max(0f, ballCenter.y - origin.y - capR * 0.45f);
            float stemGrowth = Smooth(0f, 1f, (t - 1f) / 60f);
            float stemFill = 1.1f * Smooth(3f, 14f, t);
            var sunFlat = new Vector3(_sun.x, 0f, _sun.z);
            if (sunFlat.sqrMagnitude > 1e-4f) sunFlat.Normalize();
            for (int i = _cap; i < _stemEnd; i++)
            {
                float hf = Mathf.Repeat(_r0[i] + (0.012f + 0.01f * _r1[i]) * t, 1f);
                float rs = (18f + 34f * Mathf.Exp(-hf * 8f) + 0.3f * capR * Smooth(0.75f, 1f, hf)) * (0.75f + 0.45f * stemGrowth);
                float phi = _r2[i] * Mathf.PI * 2f + t * 0.15f * (1f - hf) + hf * 1.5f;
                float rho = 0.35f + 0.65f * Mathf.Sqrt(_r3[i]);
                var offset = new Vector3(Mathf.Cos(phi) * rs * rho, 0f, Mathf.Sin(phi) * rs * rho);
                Vector3 p = origin + offset + Vector3.up * (hf * capBottom);
                p += Wobble(i, t) * 3f;
                float size = Mathf.Max(13f, rs * 1.25f) * (0.8f + 0.4f * _r4[i]);
                float alpha = Smooth(0f, 0.06f, hf) * (1f - Smooth(0.92f, 1f, hf)) * Mathf.Clamp01((stemFill - hf) * 6f);
                float h = 0.25f * heat * Smooth(0.6f, 1f, hf);
                Color albedo = Color.Lerp(new Color(0.6f, 0.53f, 0.45f), new Color(0.48f, 0.43f, 0.38f), _r6[i]);
                float self = Mathf.Lerp(0.45f, 1f, Smooth(-1f, 0.6f, Vector3.Dot(offset, sunFlat) / Mathf.Max(1f, rs)));
                SetParticle(i, p, size, albedo, 0.7f * alpha * fade, h, _r5[i] * 6.283f, self * CapShadow(p, ballCenter, capR),
                    Frame(i, t, 22f + 6f * _r3[i], 0.08f));
            }

            for (int i = _stemEnd; i < _count; i++)
            {
                float phi = _r0[i] * Mathf.PI * 2f;
                bool skirt = _r1[i] < 0.15f;
                // Skirt: big, slow puffs flaring the foot of the stem; surge: the ring pushed out.
                float rmax = skirt ? Mathf.Lerp(30f, 90f, _r1[i] / 0.15f) : Mathf.Lerp(55f, 340f, Mathf.Pow((_r1[i] - 0.15f) / 0.85f, 0.7f));
                float tau = 1.5f + 3.5f * _r2[i];
                float r = 20f + (rmax - 20f) * Grow(t, tau) + 0.12f * t;
                float baseSize = skirt ? 20f + 16f * _r4[i] : 9f + 17f * _r4[i];
                float size = baseSize * (0.4f + 0.6f * Grow(t, 3f)) * (1f + t / 90f);
                float y = 1.5f + size * 0.3f + (skirt ? 14f : 8f) * _r3[i] * Grow(t, 15f);
                Vector3 p = origin + new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
                p += Wobble(i, t) * 3f;
                float raised = Mathf.Clamp01((t - rmax / 600f) * 3f);
                float h = 0.35f * heat * Mathf.Exp(-rmax / 60f);
                Color albedo = Color.Lerp(new Color(0.66f, 0.6f, 0.5f), new Color(0.55f, 0.5f, 0.44f), _r6[i]);
                float vis = CapShadow(p, ballCenter, capR) * Mathf.Lerp(0.75f, 1f, _r5[i]);
                SetParticle(i, p, size, albedo, (skirt ? 0.55f : 0.32f) * raised * fade, h, _r5[i] * 6.283f, vis,
                    Frame(i, t, 23f + 7f * _r3[i], 0.08f));
            }
        }

        // Fire shell: flipbook fire puffs boiling on and inside the fireball. They
        // play the flame frames (fast) and cool into dark smoke as the cap takes over.
        private void UpdateFireShell(float t, float radius, float heat, Vector3 ballCenter, float fade)
        {
            if (_fireEnd == 0) return;
            float squash = _indoor ? 1f : 1f - 0.22f * Smooth(4f, 20f, t);
            float shell = radius * (_indoor ? 1f : Mathf.Lerp(1f, 0.8f, Smooth(2f, 12f, t)));
            float fireHeat = 1.2f * Mathf.Exp(-t / (_indoor ? 2.5f : 5.5f));
            float life = _indoor ? 1f - Smooth(4f, 9f, t) : 1f - Smooth(9f, 16f, t);
            for (int i = 0; i < _fireEnd; i++)
            {
                float phi = _r0[i] * Mathf.PI * 2f;
                float cosT = _r1[i] * 2f - 1f;
                float sinT = Mathf.Sqrt(Mathf.Max(0f, 1f - cosT * cosT));
                var dir = new Vector3(Mathf.Cos(phi) * sinT, cosT, Mathf.Sin(phi) * sinT);
                // Boiling: each puff breathes in and out of the surface.
                float rho = (0.4f + 0.6f * Mathf.Sqrt(_r2[i])) * (1f + 0.08f * Mathf.Sin(t * 0.9f + _r3[i] * 6.283f));
                Vector3 p = ballCenter + new Vector3(dir.x, dir.y * squash, dir.z) * (shell * rho);
                p += Wobble(i, t) * (0.12f * shell);
                float size = shell * (0.55f + 0.35f * _r4[i]) * (0.9f + 0.2f * rho);
                float h = fireHeat * (1.1f - 0.3f * rho) * (0.85f + 0.3f * _r6[i]);
                var albedo = Color.Lerp(new Color(0.22f, 0.19f, 0.17f), new Color(0.12f, 0.1f, 0.09f), _r6[i]);
                float along = Vector3.Dot(p - ballCenter, _sun) / Mathf.Max(1f, shell);
                float vis = Mathf.Lerp(0.3f, 1f, Smooth(-0.9f, 0.5f, along));
                float appear = Smooth(0f, 0.06f, t) * Smooth(0.5f, 2f, shell);
                SetParticle(i, p, size, albedo, 0.9f * appear * life * fade, h, _r5[i] * 6.283f + t * (_r4[i] - 0.5f) * 0.3f, vis,
                    Frame(i, t, 2f * _r3[i], 1.4f + 0.3f * _r6[i]));
            }
        }

        /// <summary>Sun visibility under the cap (its shadow on the stem and the ground).</summary>
        private float CapShadow(Vector3 p, Vector3 capCenter, float capR)
        {
            if (_sun.y < 0.05f) return 1f;
            float dy = capCenter.y - p.y;
            if (dy <= 0f) return 1f;
            Vector3 hit = p + _sun * (dy / _sun.y);
            float dx = hit.x - capCenter.x, dz = hit.z - capCenter.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            return Mathf.Lerp(0.3f, 1f, Smooth(0.75f * capR, 1.15f * capR, d));
        }

        private void UpdateIndoorSmoke(float t, Vector3 origin, float radius, float heat, float fade)
        {
            UpdateFireShell(t, radius, heat, origin + Vector3.up * FireballY(t, radius), fade);
            // Soot (stem slots): rises to the ceiling and spreads under it.
            for (int i = _cap; i < _stemEnd; i++)
            {
                float phi = _r0[i] * Mathf.PI * 2f;
                float size = (3.2f + 3.8f * _r4[i]) * (0.5f + 0.5f * Grow(t, 2f)) * (1f + t / 60f);
                float r = Mathf.Min((4f + 16f * _r1[i]) * Grow(t, 1.5f + 2f * _r2[i]), Mathf.Max(0.3f, _reach[i] - size * 0.3f));
                float y = Mathf.Lerp(1f, Mathf.Max(1.5f, _ceiling - size * 0.4f), Grow(t, 1f + 2f * _r3[i]));
                Vector3 p = origin + new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r) + Wobble(i, t) * 1f;
                float h = heat * 1.2f * (1f - _r1[i] * 0.5f);
                SetParticle(i, p, size, new Color(0.24f, 0.22f, 0.21f), 0.85f * Smooth(0.05f, 0.4f, t) * fade, h, _r5[i] * 6.283f, 1f,
                    Frame(i, t, 4f + 4f * _r3[i], 1f));
            }
            // Dust (dust slots): bursts out, mostly sideways, then hangs.
            for (int i = _stemEnd; i < _count; i++)
            {
                Vector3 dir = IndoorDirection(i);
                float size = (3.8f + 5f * _r4[i]) * (0.5f + 0.5f * Grow(t, 1f)) * (1f + t / 50f);
                float r = Mathf.Min(2f + (6f + 22f * _r2[i]) * Grow(t, 0.4f + 1f * _r3[i]), Mathf.Max(0.3f, _reach[i] - size * 0.3f));
                Vector3 p = origin + dir * r;
                p.y = Mathf.Clamp(p.y, origin.y + 0.5f + size * 0.2f, origin.y + _ceiling - size * 0.3f);
                p += Wobble(i, t) * 0.8f;
                float h = 0.9f * heat * (1f - 0.6f * _r2[i]);
                Color albedo = Color.Lerp(new Color(0.62f, 0.56f, 0.48f), new Color(0.5f, 0.46f, 0.41f), _r6[i]);
                SetParticle(i, p, size, albedo, 0.65f * Smooth(0.02f, 0.25f, t) * fade, h, _r5[i] * 6.283f, 1f,
                    Frame(i, t, 18f + 8f * _r3[i], 0.25f));
            }
        }

        private Vector3 Wobble(int i, float t)
        {
            float s = _r6[i] * 100f;
            return new Vector3(
                Mathf.PerlinNoise(s, t * 0.05f) - 0.5f,
                Mathf.PerlinNoise(s + 37f, t * 0.05f) - 0.5f,
                Mathf.PerlinNoise(s + 71f, t * 0.05f) - 0.5f) * 2f;
        }

        /// <summary>Same black body ramp as nk_heatColor in the shaders.</summary>
        public static Color HeatColor(float heat)
        {
            heat = Mathf.Clamp01(heat);
            float k = Mathf.Lerp(900f, 6500f, heat) / 100f;
            float hi = Mathf.Max(k - 60f, 1f);
            float r = k <= 66f ? 1f : 1.29293618606f * Mathf.Pow(hi, -0.1332047592f);
            float g = k <= 66f ? 0.39008157876f * Mathf.Log(k) - 0.63184144378f : 1.1298908609f * Mathf.Pow(hi, -0.0755148492f);
            float b = k >= 66f ? 1f : (k <= 19f ? 0f : 0.54320678911f * Mathf.Log(Mathf.Max(k - 10f, 1f)) - 1.19625408914f);
            float energy = heat * heat * (0.25f + 5f * heat * heat);
            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b)) * energy;
        }
    }
}
