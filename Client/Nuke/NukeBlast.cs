using System.Collections;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.Interactive;
using EFT.InventoryLogic;
using Systems.Effects;
using UnityEngine;

namespace viniHNS.CursedThings.Nuke
{
    /// <summary>
    /// Nuclear grenade blast, run on its own object (the grenade is destroyed
    /// right after it goes off).
    ///
    /// At detonation: flash (stuns and burns the eyes of whoever has line of
    /// sight and looks at it, blinds bots), bots are told a grenade exploded,
    /// the mushroom cloud effect (NukeFx, from nuke_fx.bundle; the stage 1
    /// placeholder light if the bundle is off or missing). Then a shockwave front expands at the speed of
    /// sound; when it reaches each player it applies the blast (lethal core
    /// that ignores cover, heavy-damage ring where cover cuts the damage),
    /// contusion and camera shake, and it breaks the windows and lamps it
    /// passes.
    /// </summary>
    internal class NukeBlast : MonoBehaviour
    {
        public const string TemplateId = "d8797e31362d4c6c7030190a";

        private const float ShockSpeed = 340f;
        private const float CoverMultiplier = 0.3f;
        private const float FlashMaxTime = 10f;
        private static readonly Collider[] ColliderBuffer = new Collider[1024];

        // Heavy ring damage share per body part (the core kills regardless).
        private static readonly EBodyPart[] Parts =
        {
            EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm,
            EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg
        };
        private static readonly float[] PartWeights = { 0.4f, 0.8f, 0.9f, 1f, 1f, 1f, 1f };

        private Vector3 _position;
        private ThrowWeap _item;
        private string _throwerId;
        private bool _planted;
        private float _lethal, _heavy, _shock;
        private static readonly System.Reflection.FieldInfo PlantedField = HarmonyLib.AccessTools.Field(typeof(Grenade), "_isPlantedItem");
        private Light _light;

        private struct BreakTarget
        {
            public float Distance;
            public LampController Lamp;
            public WindowBreaker Window;
        }

        public static void Detonate(Grenade grenade)
        {
            var blast = new GameObject("CursedThings_NukeBlast").AddComponent<NukeBlast>();
            blast._position = grenade.transform.position;
            blast._item = grenade.WeaponSource;
            blast._throwerId = grenade.ProfileId;
            // Tripwire: the game builds the grenade with isBeingPlanted and its damage is marked as planted.
            blast._planted = PlantedField != null && (bool)PlantedField.GetValue(grenade);
            blast._lethal = Plugin.Nuke_LethalRadius.Value;
            blast._heavy = Mathf.Max(blast._lethal, Plugin.Nuke_HeavyRadius.Value);
            blast._shock = Mathf.Max(blast._heavy, Plugin.Nuke_ShockRadius.Value);

            if (Singleton<GlobalEventDispatcher>.Instantiated)
            {
                Singleton<GlobalEventDispatcher>.Instance.GrenadeExplosion(blast._position, blast._throwerId, false, 0f, 0f, grenade.Id);
            }
            DamageInfo probe = Grenade.GetDamageInfo(blast._item, blast._throwerId, blast._position, blast._planted);
            Plugin.LogSource.LogInfo($"[CursedThings] nuke detonated{(blast._planted ? " (tripwire)" : "")}: thrower {blast._throwerId}, damage owner {(probe.Player != null ? probe.Player.iPlayer?.ProfileId : "MISSING")}, weapon {probe.Weapon?.ShortName ?? "none"}");
            blast.StartCoroutine(blast.Run());
        }

        // Kill credit check: who the game will credit for each nuke death.
        private static readonly System.Reflection.FieldInfo LastAggressorField = HarmonyLib.AccessTools.Field(typeof(Player), "LastAggressor");

        private void LogKill(Player victim, float distance)
        {
            var aggressor = LastAggressorField?.GetValue(victim) as IPlayer;
            Player thrower = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(_throwerId);
            Plugin.LogSource.LogInfo($"[CursedThings] nuke killed {victim.Profile?.Nickname} ({(victim.IsAI ? "bot" : "player")}) at {distance:0} m; credited to {aggressor?.ProfileId ?? "nobody"}; thrower alive: {thrower != null}");
        }

        // Deaths (ragdoll, AI events) and broken glass are the expensive part:
        // a few per frame at most, a frame or two of delay at worst.
        private const int MaxHitsPerFrame = 1;
        private const int MaxBreaksPerFrame = 4;

        private IEnumerator Run()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            SpawnEffects();
            double effectsMs = sw.Elapsed.TotalMilliseconds;
            Flash();
            double flashMs = sw.Elapsed.TotalMilliseconds - effectsMs;

            GameWorld world = Singleton<GameWorld>.Instance;
            var pending = new List<Player>(world.AllAlivePlayersList);
            List<BreakTarget> targets = FindBreakTargets();
            Plugin.LogSource.LogInfo($"[CursedThings] nuke timing: effects {effectsMs:0.0} ms, flash {flashMs:0.0} ms, setup total {sw.Elapsed.TotalMilliseconds:0.0} ms; {pending.Count} players, {targets.Count} windows/lamps");

            int nextTarget = 0;
            float elapsed = 0f;
            float front = 0f;
            int frames = 0, hits = 0, breaks = 0;
            double worstWork = 0, worstFrame = 0;
            while (front <= _shock && (pending.Count > 0 || nextTarget < targets.Count))
            {
                sw.Restart();
                elapsed += Time.deltaTime;
                front = elapsed * ShockSpeed;
                int hitsNow = 0;
                for (int i = pending.Count - 1; i >= 0 && hitsNow < MaxHitsPerFrame; i--)
                {
                    Player player = pending[i];
                    if (player == null || player.ActiveHealthController == null || !player.ActiveHealthController.IsAlive)
                    {
                        pending.RemoveAt(i);
                        continue;
                    }
                    float distance = Vector3.Distance(player.Position, _position);
                    if (distance > front) continue;
                    pending.RemoveAt(i);
                    if (distance <= _shock)
                    {
                        double before = sw.Elapsed.TotalMilliseconds;
                        HitPlayer(player, distance);
                        double cost = sw.Elapsed.TotalMilliseconds - before;
                        if (cost > 8.0)
                        {
                            Plugin.LogSource.LogInfo($"[CursedThings] nuke hit {player.Profile?.Nickname} ({(player.IsAI ? "bot" : "player")}, {distance:0} m, {(player.ActiveHealthController.IsAlive ? "alive" : "dead")}) took {cost:0.0} ms");
                        }
                        hitsNow++;
                    }
                }
                int breaksNow = 0;
                while (nextTarget < targets.Count && targets[nextTarget].Distance <= front && breaksNow < MaxBreaksPerFrame)
                {
                    BreakTarget t = targets[nextTarget++];
                    if (t.Lamp != null) t.Lamp.TryToBlowUp(_item, in _position);
                    else if (t.Window != null) t.Window.TryToBlowUp(_item, in _position);
                    breaksNow++;
                }
                hits += hitsNow;
                breaks += breaksNow;
                frames++;
                worstWork = System.Math.Max(worstWork, sw.Elapsed.TotalMilliseconds);
                yield return null;
                worstFrame = System.Math.Max(worstFrame, Time.unscaledDeltaTime * 1000.0);
            }
            Plugin.LogSource.LogInfo($"[CursedThings] nuke wave: {frames} frames, {hits} players hit, {breaks} windows/lamps; worst blast work {worstWork:0.0} ms, worst frame {worstFrame:0} ms");

            while (_light != null && _light.intensity > 0.01f) yield return null;
            Destroy(gameObject);
        }

        private void HitPlayer(Player player, float distance)
        {
            if (player.IsAI && IgnoresGrenades(player)) return;

            float closeness = Mathf.InverseLerp(_shock, _lethal, distance);
            bool spared = !Plugin.Nuke_DamageThrower.Value && player.ProfileId == _throwerId;
            if (distance <= _heavy && !spared)
            {
                float perPart;
                if (distance <= _lethal)
                {
                    perPart = 1000f;
                }
                else
                {
                    float f = 1f - (distance - _lethal) / Mathf.Max(0.01f, _heavy - _lethal);
                    perPart = Mathf.Lerp(10f, 150f, Mathf.Pow(f, 1.5f)) * (HasCover(player) ? CoverMultiplier : 1f);
                }
                DamageInfo info = Grenade.GetDamageInfo(_item, _throwerId, _position, _planted);
                info.Direction = (player.Position - _position).normalized;
                info.HitPoint = player.Position;
                if (distance <= _lethal)
                {
                    // One full hit on the head kills (the game's kill path, credited to the thrower).
                    info.Damage = perPart;
                    player.ApplyDamageInfo(info, EBodyPart.Head, EBodyPartColliderType.None, 0f);
                    if (player.ActiveHealthController.IsAlive) player.ApplyDamageInfo(info, EBodyPart.Chest, EBodyPartColliderType.None, 0f);
                }
                else
                {
                    // Every full ApplyDamageInfo fires hit reactions and AI events (the
                    // expensive part, ~5-10 ms each): only the chest (first, so the thrower
                    // is the aggressor) and the head (last, so a kill there takes the
                    // game's kill path). The other parts go straight to the health
                    // controller with the same damage.
                    info.Damage = perPart * PartWeights[1];
                    player.ApplyDamageInfo(info, EBodyPart.Chest, EBodyPartColliderType.None, 0f);
                    for (int i = 2; i < Parts.Length && player.ActiveHealthController.IsAlive; i++)
                    {
                        info.Damage = perPart * PartWeights[i];
                        player.ActiveHealthController.ApplyDamage(Parts[i], info.Damage, info);
                    }
                    if (player.ActiveHealthController.IsAlive)
                    {
                        info.Damage = perPart * PartWeights[0];
                        player.ApplyDamageInfo(info, EBodyPart.Head, EBodyPartColliderType.None, 0f);
                    }
                }
            }
            if (!player.ActiveHealthController.IsAlive)
            {
                LogKill(player, distance);
                return;
            }

            if (player.IsAI)
            {
                BotOwner bot = player.AIData?.BotOwner;
                if (bot != null && bot.BotState == EBotState.Active)
                {
                    bot.FlashGrenade.AddSoundEffect(Mathf.Lerp(2f, 15f, closeness), _position);
                }
            }
            else
            {
                player.ActiveHealthController.DoContusion(Mathf.Lerp(3f, 25f, closeness), 1f);
            }
            if (player.IsYourPlayer && Plugin.Nuke_CameraShake.Value && player.ProceduralWeaponAnimation != null)
            {
                StartCoroutine(player.ProceduralWeaponAnimation.ForceReact.GrenadeShake_CO(Mathf.Lerp(0.2f, 1f, closeness)));
            }
        }

        private void Flash()
        {
            Vector3 source = _position + Vector3.up * 2f;
            foreach (Player player in Singleton<GameWorld>.Instance.AllAlivePlayersList)
            {
                if (player == null || player.ActiveHealthController == null || !player.ActiveHealthController.IsAlive) continue;
                Vector3 head = player.PlayerBones.Head.position;
                float distance = Vector3.Distance(head, source);
                if (distance > _shock) continue;
                if (Physics.Linecast(source, head, LayersMaskController.GrenadeObstaclesColliderMask)) continue;
                float near = Mathf.InverseLerp(_shock, 0f, distance);
                float strength = near * Mathf.Clamp01(Vector3.Dot((source - head).normalized, player.LookDirection));
                if (strength <= 0f) continue;
                if (player.IsAI)
                {
                    BotOwner bot = player.AIData?.BotOwner;
                    if (bot != null && bot.BotState == EBotState.Active)
                    {
                        bot.FlashGrenade.AddBlindEffect(FlashMaxTime * bot.Settings.FileSettings.Grenade.FLASH_GRENADE_TIME_COEF * strength, _position);
                    }
                }
                else
                {
                    player.ActiveHealthController.DoStun(FlashMaxTime, strength);
                    player.ActiveHealthController.DoBurnEyes(source, near, strength, FlashMaxTime);
                }
            }
        }

        private bool HasCover(Player player)
        {
            Vector3 source = _position + Vector3.up;
            int mask = LayersMaskController.GrenadeObstaclesColliderMask;
            return Physics.Linecast(source, player.PlayerBones.Head.position, mask)
                && Physics.Linecast(source, player.PlayerBones.Pelvis.position, mask);
        }

        private static bool IgnoresGrenades(Player player)
        {
            var mind = player.AIData?.BotOwner?.Settings?.FileSettings?.Mind;
            return mind != null && mind.GRENADE_DAMAGE_IGNORE;
        }

        private List<BreakTarget> FindBreakTargets()
        {
            var list = new List<BreakTarget>();
            // TryToBlowUp breaks lamps out to 2x and windows out to 1.5x the item's
            // MinExplosionDistance: nothing farther can break, so no need to look.
            float reach = Mathf.Max(1f, _item.MinExplosionDistance * 2f);
            int count = Physics.OverlapSphereNonAlloc(_position, reach, ColliderBuffer, LayersMaskController.GrenadeAffectedMask);
            for (int i = 0; i < count; i++)
            {
                Collider c = ColliderBuffer[i];
                var lamp = c.GetComponent<LampController>();
                WindowBreaker window = lamp == null && c.transform.parent != null ? c.transform.parent.GetComponent<WindowBreaker>() : null;
                if (lamp == null && window == null) continue;
                list.Add(new BreakTarget { Distance = Vector3.Distance(c.transform.position, _position), Lamp = lamp, Window = window });
            }
            list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return list;
        }

        // The game's biggest explosion stays for its sound and debris; the
        // mushroom cloud comes from nuke_fx.bundle, or the stage 1 light.
        private void SpawnEffects()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (Singleton<Effects>.Instantiated)
            {
                // Visual only: the sound comes later, with the shock front (NukeAudio).
                Singleton<Effects>.Instance.EmitGrenade("big_explosion", _position, Vector3.up, 0f);
            }
            try
            {
                NukeAudio.PlayBlast(_position, NukeFxLoader.IsIndoor(_position, out _));
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource.LogError($"[CursedThings] nuke sound failed: {ex}");
            }
            double gameEffectMs = sw.Elapsed.TotalMilliseconds;
            if (Plugin.Nuke_VisualEffects.Value)
            {
                try
                {
                    bool spawned = NukeFxLoader.Spawn(_position, out _);
                    Plugin.LogSource.LogInfo($"[CursedThings] nuke timing: big_explosion {gameEffectMs:0.0} ms, mushroom cloud spawn {sw.Elapsed.TotalMilliseconds - gameEffectMs:0.0} ms");
                    if (spawned) return;
                }
                catch (System.Exception ex)
                {
                    Plugin.LogSource.LogError($"[CursedThings] nuke effects failed: {ex}");
                }
            }
            PlaceholderLight();
        }

        private void PlaceholderLight()
        {
            var lightObject = new GameObject("CursedThings_NukeLight");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.position = _position + Vector3.up * 15f;
            _light = lightObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.75f, 0.45f);
            _light.range = _heavy * 2f;
            _light.intensity = 8f;
            // With a shadow, so walls stop it (no light through them).
            _light.shadows = LightShadows.Hard;
            StartCoroutine(FadeLight(8f));
        }

        private IEnumerator FadeLight(float duration)
        {
            float t = 0f;
            while (t < duration && _light != null)
            {
                t += Time.deltaTime;
                _light.intensity = Mathf.Lerp(8f, 0f, t / duration);
                yield return null;
            }
            if (_light != null) _light.intensity = 0f;
        }
    }
}
