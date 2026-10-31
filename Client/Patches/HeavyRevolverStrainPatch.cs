using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace viniHNS.CursedThings.Patches
{
    /// <summary>
    /// Arm strain of the BFR .50 BMG for the local player. The game only drains
    /// arm stamina while aiming, by the square root of the ergonomic weight, so
    /// weight and ergonomics alone barely change how long a 5.5 kg revolver can
    /// be held up, and firing costs no stamina at all.
    /// </summary>
    internal static class HeavyRevolverStrain
    {
        public const string BFR_TPL = "b3dc388ec0d486a30fbbfb53";

        public static bool IsBfr(Item item)
        {
            return item != null && item.StringTemplateId == BFR_TPL;
        }

        public static Player GetMainPlayer()
        {
            GameWorld gameWorld = Singleton<GameWorld>.Instance;
            return gameWorld != null ? gameWorld.MainPlayer : null;
        }
    }

    /// <summary>
    /// Physical.Aim receives the ergonomic weight of the weapon while aiming
    /// (0 when not aiming). The drain grows with its square root, so the mass is
    /// scaled by the square of the multiplier.
    /// </summary>
    internal class HeavyRevolverAimStrainPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Physical), nameof(Physical.Aim));
        }

        [PatchPrefix]
        private static void Prefix(Physical __instance, ref float mass)
        {
            try
            {
                if (mass <= 0f || !Plugin.BFR_StrainEnabled.Value) return;

                Player player = HeavyRevolverStrain.GetMainPlayer();
                if (player == null || !ReferenceEquals(player.Physical, __instance)) return;

                Player.FirearmController firearm = player.HandsController as Player.FirearmController;
                if (firearm == null || !HeavyRevolverStrain.IsBfr(firearm.Item)) return;

                float multiplier = Plugin.BFR_AimDrainMultiplier.Value;
                mass *= multiplier * multiplier;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[BFR] Aim strain error: {ex}");
            }
        }
    }

    /// <summary>
    /// Each BFR shot takes a fixed amount of arm stamina, aiming or not, and
    /// holds off arm stamina recovery for a moment. When the arms reach zero the
    /// game drops the aim as usual.
    /// </summary>
    internal class HeavyRevolverShotStrainPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player.FirearmController), "InitiateShot");
        }

        [PatchPostfix]
        private static void Postfix(Player.FirearmController __instance, IWeapon weapon)
        {
            try
            {
                if (!Plugin.BFR_StrainEnabled.Value) return;
                if (weapon == null || !HeavyRevolverStrain.IsBfr(weapon.Item)) return;

                Player player = HeavyRevolverStrain.GetMainPlayer();
                if (player == null || !ReferenceEquals(player.HandsController, __instance)) return;

                Stamina hands = player.Physical.HandsStamina;
                if (hands == null || hands.ForceMode || hands.Multiplier <= 0f) return;

                float before = hands.Current;
                float after = Mathf.Max(0f, before - Plugin.BFR_ShotStaminaCost.Value * hands.Multiplier);
                hands.UpdateStamina(after);
                hands.DisableRestoration = Mathf.Max(hands.DisableRestoration, Time.time + Plugin.BFR_RecoveryDelay.Value);

                if (after <= 0f && before > 0f)
                {
                    hands.HandleExpiration();
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[BFR] Shot strain error: {ex}");
            }
        }
    }
}
