using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace viniHNS.CursedThings.Patches
{
    /// <summary>
    /// Loading-gate (single action) revolvers eject one case per chamber.
    /// The game's cylinder reload (Player.FirearmController.ReloadCylinderMagOperation)
    /// is written for swing-out cylinders:
    ///  - with no live round left, Prepare does not pick the first case for the
    ///    animator and the RemoveShell event throws every case out at once
    ///    (ejector star);
    ///  - with weapon mastery, an empty cylinder (or a quick reload) switches to
    ///    the speedloader reload that fills every chamber at once.
    /// For the templates listed here the reload goes chamber by chamber, the
    /// same way the game reloads a cylinder that still has live rounds. Quick
    /// reload keeps the game's behaviour (it empties the whole cylinder).
    /// </summary>
    internal static class SingleActionRevolverReload
    {
        public static readonly HashSet<string> Templates = new HashSet<string>
        {
            "fd3741820feaf281c62fceef",
            "b3dc388ec0d486a30fbbfb53",
        };

        public static bool Applies(Player.FirearmController.ReloadCylinderMagOperation operation)
        {
            Weapon weapon = operation?.Weapon;
            return weapon != null && Templates.Contains(weapon.StringTemplateId);
        }
    }

    /// <summary>
    /// Prepare runs inside ReloadCylinderMagOperation.Start, right after the
    /// Reload trigger is set and before the animator updates, so the parameters
    /// written here are the ones its first transition reads.
    /// </summary>
    internal class SingleActionRevolverPreparePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(Player.FirearmController.ReloadCylinderMagOperation),
                nameof(Player.FirearmController.ReloadCylinderMagOperation.Prepare));
        }

        [PatchPostfix]
        private static void Postfix(Player.FirearmController.ReloadCylinderMagOperation __instance)
        {
            try
            {
                if (!SingleActionRevolverReload.Applies(__instance)) return;

                CylinderMagazine cylinder = __instance._cylinderMagazine;
                if (cylinder == null) return;

                if (__instance._highLevelMasteringFullReload)
                {
                    // Back to the values Prepare sets without mastery.
                    __instance._highLevelMasteringFullReload = false;
                    __instance.FirearmsAnimator.SetWeaponLevel(0f);
                    __instance._freeCamorasIndexes = cylinder.GetFreeCamorasIndexesFromCurrentActiveIndex(
                        __instance._quickReload, !__instance.Weapon.CylinderHammerClosed);
                    __instance.SetNextCamoraIndexForLoadAmmo();
                }

                // With live rounds left, Prepare already picked the first case.
                if (__instance._quickReload || cylinder.Count != 0) return;
                if (__instance._camorasIndexesWithShells.Count == 0) return;

                // Same calls Prepare makes when the cylinder still has live rounds.
                __instance.SetCamoraIndexForRemoveShell();
                __instance.FirearmsAnimator.SetCamoraIndexWithShellForRemove(__instance._shellIndexForRemove);
                __instance.FirearmsAnimator.SetAmmoCountForRemove(__instance._camorasIndexesWithShells.Count);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[SingleActionRevolver] Prepare patch error: {ex}");
            }
        }
    }

    /// <summary>
    /// Each RemoveShell event of the reload animation ejects the case in the
    /// chamber at the gate and picks the next one, also when the cylinder has
    /// no live round left.
    /// </summary>
    internal class SingleActionRevolverRemoveShellPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(Player.FirearmController.ReloadCylinderMagOperation),
                nameof(Player.FirearmController.ReloadCylinderMagOperation.OnRemoveShellEvent));
        }

        [PatchPrefix]
        private static bool Prefix(Player.FirearmController.ReloadCylinderMagOperation __instance)
        {
            try
            {
                if (!SingleActionRevolverReload.Applies(__instance)) return true;

                // Quick reload empties the whole cylinder: keep the game's code.
                if (__instance._quickReload || __instance._cylinderMagazine == null) return true;

                // The game's branch for a cylinder with live rounds left, with a
                // bounds check: the index stays -1 when there was no case to pick.
                int index = __instance._shellIndexForRemove;
                AmmoTemplate[] shells = __instance.Weapon.ShellsInChambers;
                if (index >= 0 && index < shells.Length && shells[index] != null)
                {
                    shells[index] = null;
                    Firearms firearms = __instance.Firearms;
                    if (firearms.IsShellVisible())
                    {
                        firearms.StartSpawnShell(__instance.Player.Velocity * 0.33f, index);
                    }
                    else
                    {
                        // Too far to throw it (StartSpawnShell would do nothing):
                        // remove the case model so it doesn't stay in the chamber.
                        firearms.DestroyShellInWeapon(index);
                    }
                }

                __instance.SetCamoraIndexForRemoveShell();
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[SingleActionRevolver] RemoveShell patch error: {ex}");

                // Cases still counted in the animator would keep it in the eject
                // states, so fall back to clearing them all.
                try
                {
                    __instance.RemoveAllShells();
                }
                catch (Exception inner)
                {
                    Plugin.LogSource.LogError($"[SingleActionRevolver] RemoveAllShells fallback error: {inner}");
                }

                return false;
            }
        }
    }
}
