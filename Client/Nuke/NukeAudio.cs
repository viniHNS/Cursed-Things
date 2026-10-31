using System.Collections;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using UnityEngine;

namespace viniHNS.CursedThings.Nuke
{
    /// <summary>
    /// Nuclear grenade sound.
    ///
    /// Fuse: Geiger counter clicks from the grenade, faster and faster in the
    /// last 5 seconds (clips from nuke_fx.bundle).
    ///
    /// Blast: nothing until the shock front (340 m/s) reaches the listener;
    /// then the game's own explosion recordings (close / far / distant /
    /// indoor variants, from the grenade sound bank that effects.bundle keeps
    /// loaded), pitched down and layered, a long synthesized rumble and a few
    /// delayed echoes from the sides, like rolling thunder. Heard across the
    /// whole map: far away the sources sit a few meters from the listener in
    /// the direction of the blast and the volume follows the distance, so the
    /// engine's rolloff never cuts them.
    /// </summary>
    internal class NukeAudio : MonoBehaviour
    {
        private const float SoundSpeed = 340f;   // same front as the damage
        private const float ProxyDistance = 25f;
        private const int Rolloff = 500;

        private static readonly string[] CloseNames = { "v40_explosion2_close", "explosion_mine_close", "v40_explosion1_close", "v40_explosion3_close" };
        private static readonly string[] FarNames = { "v40_explosion2_far", "explosion_mine_far", "v40_explosion3_far", "v40_explosion1_far", "v40_explosion4_far" };
        private static readonly string[] DistantNames = { "v40_explosion4_distant", "explosion_mine_distant", "v40_explosion1_distant", "v40_explosion3_distant", "v40_explosion2_distant" };
        private static readonly string[] IndoorCloseNames = { "v40_explosion2_indoor_close", "v40_explosion1_indoor_close", "v40_explosion4_indoor_close" };
        private static readonly string[] IndoorFarNames = { "v40_explosion2_indoor_distant", "v40_explosion1_indoor_distant" };

        private static readonly Dictionary<string, AudioClip> GameClips = new Dictionary<string, AudioClip>();
        private static AudioClip _rumble;
        private static AudioClip[] _clicks = new AudioClip[0];

        /// <summary>Our own clips, from nuke_fx.bundle.</summary>
        public static void SetBundleClips(AudioClip[] clips)
        {
            var clicks = new List<AudioClip>();
            foreach (AudioClip clip in clips)
            {
                if (clip == null) continue;
                if (clip.name == "nuke_rumble") _rumble = clip;
                else if (clip.name.StartsWith("nuke_geiger")) clicks.Add(clip);
                if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            }
            _clicks = clicks.ToArray();
            Plugin.LogSource.LogInfo($"[CursedThings] nuke sound clips loaded: rumble {(_rumble != null ? "ok" : "missing")}, {_clicks.Length} geiger clicks");
        }

        /// <summary>Finds the game's explosion recordings in memory (once per raid is enough).</summary>
        public static void FindGameClips()
        {
            bool valid = GameClips.Count > 0;
            foreach (AudioClip clip in GameClips.Values)
            {
                if (clip == null) { valid = false; break; }
            }
            if (valid) return;
            GameClips.Clear();
            var wanted = new HashSet<string>();
            foreach (string[] names in new[] { CloseNames, FarNames, DistantNames, IndoorCloseNames, IndoorFarNames })
            {
                foreach (string name in names) wanted.Add(name);
            }
            foreach (AudioClip clip in Resources.FindObjectsOfTypeAll<AudioClip>())
            {
                if (clip != null && wanted.Contains(clip.name) && !GameClips.ContainsKey(clip.name))
                {
                    GameClips[clip.name] = clip;
                    if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                }
            }
            Plugin.LogSource.LogInfo($"[CursedThings] nuke sound: {GameClips.Count}/{wanted.Count} game recordings found");
        }

        // ------------------------------------------------------------ fuse

        public static void StartGeiger(Grenade grenade, float fuse, float timeSpent)
        {
            if (!Plugin.Nuke_Geiger.Value) return;
            var host = new GameObject("CursedThings_NukeGeiger").AddComponent<NukeAudio>();
            host.StartCoroutine(host.Geiger(grenade, fuse - timeSpent));
        }

        private IEnumerator Geiger(Grenade grenade, float remaining)
        {
            float next = 0f;
            float elapsed = 0f;
            // First nuke of the session: the clicks are still loading with the bundle.
            while (_clicks.Length == 0 && grenade != null && elapsed < remaining)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
            while (grenade != null && remaining - elapsed > 0f)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= next)
                {
                    float left = remaining - elapsed;
                    // About 3.5 clicks/s, climbing to ~50/s in the last 5 seconds.
                    float rate = left > 5f ? 3.5f : 3.5f + 45f * Mathf.Pow(1f - left / 5f, 2f);
                    next = elapsed - Mathf.Log(1f - Random.value * 0.999f) / rate;
                    PlayClick(grenade.transform.position);
                }
                yield return null;
            }
            Destroy(gameObject);
        }

        private static void PlayClick(Vector3 position)
        {
            if (!MonoBehaviourSingleton<BetterAudio>.Instantiated) return;
            AudioClip clip = _clicks[Random.Range(0, _clicks.Length)];
            if (clip == null) return;
            float distance = CameraManager.Instance.Distance(position);
            MonoBehaviourSingleton<BetterAudio>.Instance.PlayAtPoint(position, clip, distance, BetterAudio.AudioSourceGroupType.Weaponry,
                40, Random.Range(0.55f, 0.8f) * Plugin.Nuke_SoundVolume.Value, EOcclusionTest.OneShotPropagation);
        }

        // ----------------------------------------------------------- blast

        public static void PlayBlast(Vector3 position, bool indoor)
        {
            if (Plugin.Nuke_SoundVolume.Value <= 0f) return;
            FindGameClips();
            var host = new GameObject("CursedThings_NukeSound").AddComponent<NukeAudio>();
            host.StartCoroutine(host.Blast(position, indoor));
        }

        private IEnumerator Blast(Vector3 position, bool indoor)
        {
            // Silent until the front reaches the listener.
            float elapsed = 0f;
            Vector3 listener = ListenerPosition();
            while (elapsed * SoundSpeed < Vector3.Distance(listener, position) && elapsed < 15f)
            {
                yield return null;
                elapsed += Time.deltaTime;
                listener = ListenerPosition();
            }

            float distance = Vector3.Distance(listener, position);
            Vector3 dir = distance > 1f ? (position - listener) / distance : Vector3.up;
            float volume = Plugin.Nuke_SoundVolume.Value * Mathf.Clamp(1.1f - 0.35f * Mathf.Log10(Mathf.Max(distance, 50f) / 50f), 0.2f, 1f);

            string[] main, tail;
            float pitch;
            if (indoor)
            {
                main = distance < 60f ? IndoorCloseNames : IndoorFarNames;
                tail = FarNames;
                pitch = 0.7f;
            }
            else if (distance < 250f)
            {
                main = CloseNames;
                tail = FarNames;
                pitch = 0.62f;
            }
            else if (distance < 900f)
            {
                main = FarNames;
                tail = DistantNames;
                pitch = 0.56f;
            }
            else
            {
                main = DistantNames;
                tail = DistantNames;
                pitch = 0.5f;
            }

            // The boom: two recordings of the band layered, and a deeper tail.
            Play(Pick(main, 0), At(listener, dir, distance, 0f), volume, pitch);
            Play(Pick(main, 1), At(listener, dir, distance, 8f), volume * 0.85f, pitch * 0.9f);
            Play(Pick(tail, 0), At(listener, dir, distance, -8f), volume * 0.6f, 0.48f);
            if (!indoor && _rumble != null)
            {
                Play(_rumble, At(listener, dir, distance, 0f), volume * 0.95f, 1f);
            }

            // Echoes from the terrain: quieter, deeper, from the sides.
            float[] delays = indoor ? new[] { 0.5f } : new[] { 0.7f, 1.6f, 2.7f, 4.0f };
            float[] gains = indoor ? new[] { 0.3f } : new[] { 0.45f, 0.35f, 0.25f, 0.18f };
            float waited = 0f;
            for (int i = 0; i < delays.Length; i++)
            {
                while (waited < delays[i])
                {
                    yield return null;
                    waited += Time.deltaTime;
                }
                listener = ListenerPosition();
                float side = (i % 2 == 0 ? 1f : -1f) * Random.Range(25f, 60f);
                Vector3 echoDir = Quaternion.Euler(0f, side, 0f) * dir;
                Play(Pick(DistantNames, i + 1), listener + echoDir * ProxyDistance, volume * gains[i], Random.Range(0.45f, 0.5f));
            }

            // Keep the host alive until the longest source is released.
            float rest = _rumble != null ? _rumble.length + 1f : 12f;
            while (waited < rest)
            {
                yield return null;
                waited += Time.deltaTime;
            }
            Destroy(gameObject);
        }

        private static Vector3 ListenerPosition()
        {
            Camera camera = CameraManager.Instance.Camera;
            if (camera != null) return camera.transform.position;
            Player player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
            return player != null ? player.Position : Vector3.zero;
        }

        // Where to put a source: the blast itself when close, else a few meters
        // from the listener toward it (optionally turned a little sideways).
        private static Vector3 At(Vector3 listener, Vector3 dir, float distance, float turn)
        {
            Vector3 d = Quaternion.Euler(0f, turn, 0f) * dir;
            return listener + d * Mathf.Min(distance, ProxyDistance);
        }

        private static AudioClip Pick(string[] names, int index)
        {
            for (int k = 0; k < names.Length; k++)
            {
                if (GameClips.TryGetValue(names[(index + k) % names.Length], out AudioClip clip) && clip != null) return clip;
            }
            return null;
        }

        // Pitched down sources last longer than the clip: the pool would cut
        // them at the clip's own length, so they are released here instead.
        private void Play(AudioClip clip, Vector3 at, float volume, float pitch)
        {
            if (clip == null || !MonoBehaviourSingleton<BetterAudio>.Instantiated) return;
            BetterSource source = MonoBehaviourSingleton<BetterAudio>.Instance.PlayAtPoint(at, clip, BetterAudio.AudioSourceGroupType.Weaponry,
                Rolloff, Mathf.Clamp01(volume), EOcclusionTest.OneShotPropagation, null, true, true, false, false);
            if (source == null) return;
            source.SetPitch(pitch);
            StartCoroutine(ReleaseLater(source, clip.length / Mathf.Max(0.1f, pitch) + 0.3f));
        }

        private static IEnumerator ReleaseLater(BetterSource source, float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                t += Time.deltaTime;
            }
            if (source != null) source.Release();
        }
    }
}
