using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using SPT.Reflection.Patching;
using viniHNS.CursedThings.Nuke;
using viniHNS.CursedThings.Patches;

namespace viniHNS.CursedThings
{
    [BepInPlugin("viniHNS.CursedThings", "CursedThings", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;

        // BFR .50 BMG arm strain
        public static ConfigEntry<bool> BFR_StrainEnabled;
        public static ConfigEntry<float> BFR_AimDrainMultiplier;
        public static ConfigEntry<float> BFR_ShotStaminaCost;
        public static ConfigEntry<float> BFR_RecoveryDelay;

        // Nuclear grenade
        public static ConfigEntry<float> Nuke_FuseTime;
        public static ConfigEntry<float> Nuke_LethalRadius;
        public static ConfigEntry<float> Nuke_HeavyRadius;
        public static ConfigEntry<float> Nuke_ShockRadius;
        public static ConfigEntry<bool> Nuke_DamageThrower;
        public static ConfigEntry<bool> Nuke_CameraShake;
        public static ConfigEntry<bool> Nuke_VisualEffects;
        public static ConfigEntry<NukeEffectsQuality> Nuke_EffectsQuality;
        public static ConfigEntry<float> Nuke_SoundVolume;
        public static ConfigEntry<bool> Nuke_Geiger;

        private void Awake()
        {
            LogSource = Logger;

            BFR_StrainEnabled = Config.Bind(
                "BFR .50 BMG",
                "Enable Arm Strain",
                true,
                new ConfigDescription("Holding up and firing the BFR .50 BMG tires your arms much faster than other weapons.",
                null,
                new ConfigurationManagerAttributes { Order = 100 }));

            BFR_AimDrainMultiplier = Config.Bind(
                "BFR .50 BMG",
                "Aim Stamina Drain Multiplier",
                2.0f,
                new ConfigDescription("How many times faster arm stamina drains while aiming the BFR.",
                new AcceptableValueRange<float>(1f, 5f),
                new ConfigurationManagerAttributes { Order = 99 }));

            BFR_ShotStaminaCost = Config.Bind(
                "BFR .50 BMG",
                "Arm Stamina Cost Per Shot",
                12f,
                new ConfigDescription("Arm stamina taken by each BFR shot, aiming or not (base arm stamina is 80).",
                new AcceptableValueRange<float>(0f, 80f),
                new ConfigurationManagerAttributes { Order = 98 }));

            BFR_RecoveryDelay = Config.Bind(
                "BFR .50 BMG",
                "Arm Recovery Delay After Shot",
                3f,
                new ConfigDescription("Seconds after each BFR shot before arm stamina starts to recover.",
                new AcceptableValueRange<float>(0f, 10f),
                new ConfigurationManagerAttributes { Order = 97 }));

            Nuke_FuseTime = Config.Bind(
                "Nuclear Grenade",
                "Fuse Time",
                15f,
                new ConfigDescription("Seconds from throw to detonation. Applies to the next nuke thrown.",
                new AcceptableValueRange<float>(3f, 60f),
                new ConfigurationManagerAttributes { Order = 100 }));

            Nuke_LethalRadius = Config.Bind(
                "Nuclear Grenade",
                "Lethal Radius",
                80f,
                new ConfigDescription("Meters. Anyone inside dies, cover or not.",
                new AcceptableValueRange<float>(5f, 300f),
                new ConfigurationManagerAttributes { Order = 99 }));

            Nuke_HeavyRadius = Config.Bind(
                "Nuclear Grenade",
                "Heavy Damage Radius",
                160f,
                new ConfigDescription("Meters. Between the lethal radius and this one, damage falls off with distance; solid cover cuts it to 30%.",
                new AcceptableValueRange<float>(10f, 600f),
                new ConfigurationManagerAttributes { Order = 98 }));

            Nuke_ShockRadius = Config.Bind(
                "Nuclear Grenade",
                "Shockwave Radius",
                400f,
                new ConfigDescription("Meters. Reach of the flash (if you look at it), contusion, deafening and camera shake.",
                new AcceptableValueRange<float>(20f, 1200f),
                new ConfigurationManagerAttributes { Order = 97 }));

            Nuke_DamageThrower = Config.Bind(
                "Nuclear Grenade",
                "Damage Thrower",
                true,
                new ConfigDescription("If off, the player who threw the nuke takes no blast damage (flash and contusion still apply).",
                null,
                new ConfigurationManagerAttributes { Order = 96 }));

            Nuke_CameraShake = Config.Bind(
                "Nuclear Grenade",
                "Camera Shake",
                true,
                new ConfigDescription("Shake the camera when the shockwave reaches you.",
                null,
                new ConfigurationManagerAttributes { Order = 95 }));

            Nuke_VisualEffects = Config.Bind(
                "Nuclear Grenade",
                "Mushroom Cloud",
                true,
                new ConfigDescription("Show the full blast effect (fireball, shockwave, mushroom cloud). Off: only the game's explosion and a light.",
                null,
                new ConfigurationManagerAttributes { Order = 94 }));

            Nuke_EffectsQuality = Config.Bind(
                "Nuclear Grenade",
                "Effects Quality",
                NukeEffectsQuality.High,
                new ConfigDescription("High: full smoke and the shockwave distortion. Low: half the smoke particles, no distortion.",
                null,
                new ConfigurationManagerAttributes { Order = 93 }));

            Nuke_SoundVolume = Config.Bind(
                "Nuclear Grenade",
                "Sound Volume",
                1f,
                new ConfigDescription("Volume of the blast, rumble and echoes (they reach you with the shockwave, at the speed of sound).",
                new AcceptableValueRange<float>(0f, 1f),
                new ConfigurationManagerAttributes { Order = 92 }));

            Nuke_Geiger = Config.Bind(
                "Nuclear Grenade",
                "Geiger Counter",
                true,
                new ConfigDescription("Geiger counter clicks from the grenade while the fuse burns, faster in the last 5 seconds.",
                null,
                new ConfigurationManagerAttributes { Order = 91 }));

            MigrateNukeRadii();

            Enable(new SingleActionRevolverPreparePatch());
            Enable(new SingleActionRevolverRemoveShellPatch());
            Enable(new HeavyRevolverAimStrainPatch());
            Enable(new HeavyRevolverShotStrainPatch());
            Enable(new PracticeGrenadePopPatch());
            Enable(new NukeGrenadeFusePatch());
            Enable(new NukeGrenadeExplosionPatch());

            LogSource.LogInfo("CursedThings loaded!");
        }

        // The first defaults (40/80/250 m) felt like an ordinary grenade: configs
        // still on those factory values move to the new ones once; values the
        // player changed by hand stay.
        private void MigrateNukeRadii()
        {
            var version = Config.Bind("Nuclear Grenade", "Defaults Version", 1,
                new ConfigDescription("Internal, do not change.", null,
                new ConfigurationManagerAttributes { Browsable = false, Order = 0 }));
            if (version.Value >= 2) return;
            if (UnityEngine.Mathf.Approximately(Nuke_LethalRadius.Value, 40f)) Nuke_LethalRadius.Value = 80f;
            if (UnityEngine.Mathf.Approximately(Nuke_HeavyRadius.Value, 80f)) Nuke_HeavyRadius.Value = 160f;
            if (UnityEngine.Mathf.Approximately(Nuke_ShockRadius.Value, 250f)) Nuke_ShockRadius.Value = 400f;
            version.Value = 2;
        }

        // One broken patch must not take the others down with it.
        private static void Enable(ModulePatch patch)
        {
            try
            {
                patch.Enable();
            }
            catch (System.Exception ex)
            {
                LogSource.LogError($"[CursedThings] could not enable {patch.GetType().Name}: {ex}");
            }
        }
    }

#pragma warning disable CS0649
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? ShowRangeAsPercent;
        public bool? Browsable;
        public System.Action<BepInEx.Configuration.ConfigEntryBase> CustomDrawer;
        public bool? ReadOnly;
        public bool? HideDefaultButton;
        public bool? HideSettingName;
        public string Description;
        public string DispName;
        public int? Order;
    }
#pragma warning restore CS0649
}
