using System;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using viniHNS.CursedThings.Nuke;

namespace viniHNS.CursedThings.Patches
{
    /// <summary>
    /// Nuclear grenade fuse: Grenade.StartTimer (called from Init) reads the
    /// template's ExplDelay, so setting it here makes the F12 value apply to
    /// every nuke thrown from now on (and to the item card). Also starts
    /// loading the effect bundle and the Geiger counter clicks.
    /// </summary>
    internal class NukeGrenadeFusePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // Full signature: Throwable has its own Init(ThrowableSettings), so the name alone is ambiguous.
            return AccessTools.Method(typeof(Grenade), nameof(Grenade.Init), new[]
            {
                typeof(GrenadeSettings), typeof(string), typeof(ThrowWeap), typeof(float), typeof(IBallisticsCalculator), typeof(bool)
            });
        }

        [PatchPrefix]
        private static void Prefix(Grenade __instance, ThrowWeap throwWeap, float timeSpent)
        {
            try
            {
                if (throwWeap == null || throwWeap.StringTemplateId != NukeBlast.TemplateId) return;
                throwWeap.GetTemplate<ThrowWeapTemplate>().ExplDelay = Plugin.Nuke_FuseTime.Value;
                // Load and warm up the mushroom cloud (and its sounds) while the fuse burns.
                NukeFxLoader.BeginLoad();
                NukeAudio.FindGameClips();
                NukeAudio.StartGeiger(__instance, Plugin.Nuke_FuseTime.Value, timeSpent);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[CursedThings] nuke fuse failed: {ex}");
            }
        }
    }

    /// <summary>
    /// Nuclear grenade blast: replaces the game's explosion (effect, damage,
    /// fragments) with NukeBlast. The grenade object is still destroyed by
    /// InvokeBlowUpEvent right after.
    /// </summary>
    internal class NukeGrenadeExplosionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Grenade), nameof(Grenade.Explosion), Type.EmptyTypes);
        }

        [PatchPrefix]
        private static bool Prefix(Grenade __instance)
        {
            if (__instance == null || __instance.WeaponSource == null) return true;
            if (__instance.WeaponSource.StringTemplateId != NukeBlast.TemplateId) return true;
            try
            {
                NukeBlast.Detonate(__instance);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[CursedThings] nuke blast failed: {ex}");
            }
            return false;
        }
    }
}
