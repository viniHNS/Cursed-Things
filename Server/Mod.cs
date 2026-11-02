using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;

namespace CursedThings;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.viniHNS.CursedThings";
    public string Name { get; init; } = "CursedThings";
    public string Author { get; init; } = "viniHNS";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.1.0");
    public Range SptVersion { get; init; } = new("~4.1.6");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }

    public Dictionary<string, Range>? ModDependencies { get; init; } = new()
    {
        { "com.wtt.commonlib", new Range("~3.0.6") }
    };

    public string? Url { get; init; } = "";
    public string License { get; init; } = "MIT";
}

// WTTServerCommonLib runs at Preload and WTTServerCommonLibPostSptLoad at PostLoad.
// Custom items must exist before profiles load (SaveCallbacks), so this runs right after WTT's core.
[Injectable(TypePriority = OnLoadOrder.Preload + 2)]
public class CursedThingsMod(
    WTTServerCommonLib.WTTServerCommonLib wttCommon,
    ISptLogger<CursedThingsMod> logger
) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // Buffs first: the Absolut vodka's StimulatorBuffs points at Buffs_drink_absolut.
        await wttCommon.CustomBuffService.CreateCustomBuffs(assembly);
        await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly);
        await wttCommon.CustomWeaponPresetService.CreateCustomWeaponPresets(assembly, "db/weaponPresets/GlobalPresets");
        await wttCommon.CustomLocaleService.CreateCustomLocales(assembly);
        await wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly, "db/weaponPresets/Assorts");

        logger.Info("CursedThings loaded");
    }
}
