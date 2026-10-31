using System;
using System.Reflection;
using Comfort.Common;
using Diz.Resources;
using EFT;
using EFT.CameraControl;
using EFT.Tripwire;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace viniHNS.CursedThings.Patches
{
    /// <summary>
    /// M69 practice grenade: the item has no explosion effect and zero range,
    /// blindness and contusion, so the game treats it as a dummy (no damage,
    /// no fragments). When its timer runs out this plays the game's own
    /// grenade fuze sound (the one tripwires use) at the grenade, with the
    /// same volume, range, audio group and occlusion the game uses for it.
    /// </summary>
    internal class PracticeGrenadePopPatch : ModulePatch
    {
        private const string M69Template = "b239ff5680843899b92c4214";
        private const string SoundStorageBundle = "assets/content/audio/data/soundcontainers/tripwiresoundstorage.bundle";

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Grenade), nameof(Grenade.Explosion), Type.EmptyTypes);
        }

        [PatchPrefix]
        private static void Prefix(Grenade __instance)
        {
            try
            {
                if (__instance == null || __instance.WeaponSource == null) return;
                if (__instance.WeaponSource.StringTemplateId != M69Template) return;

                TripwireSoundStorageSO storage = Singleton<IEasyAssets>.Instance.GetAsset<TripwireSoundStorageSO>(SoundStorageBundle);
                IAudioClipContainer fuze = storage?.GetFuzeSound();
                BetterAudio audio = MonoBehaviourSingleton<BetterAudio>.Instance;
                if (fuze == null || audio == null) return;

                UnityEngine.Vector3 position = __instance.transform.position;
                float distance = CameraManager.Instance != null ? CameraManager.Instance.Distance(position) : 0f;
                audio.PlayAtPoint(position, fuze.GetClip(), distance, BetterAudio.AudioSourceGroupType.Weaponry,
                    fuze.GetMaxDistance(), fuze.GetVolume(), EOcclusionTest.OneShotPropagation);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[CursedThings] M69 fuze sound failed: {ex}");
            }
        }
    }
}
