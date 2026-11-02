using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace CursedThings;

// The PKM-12 barrel takes only the game's five Saiga-12 muzzle devices. A mod that clones one of them with WTT's
// addtoModSlots gets added to every mod_muzzle slot that accepts the cloned device, this barrel included.
// WTT does that at PostLoad (WTTServerCommonLibPostSptLoad), so this runs right after it.
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class Pkm12MuzzleFilter(
    TemplateTable templateTable,
    ISptLogger<Pkm12MuzzleFilter> logger
) : IOnLoad
{
    private static readonly MongoId BarrelId = new("f1356d48ee823965a7ae5be0");

    // GK-02, Tromix Monster Claw, SOK-12 thread protector, SilencerCo Salvo 12 thread adapter, Hexagon 12K
    private static readonly MongoId[] SaigaMuzzleDevices =
    [
        new("58272d7f2459774f6311ddfd"),
        new("59fb137a86f7740adb646af1"),
        new("576167ab2459773cad038c43"),
        new("5b363e1b5acfc4771e1c5e80"),
        new("59c0ec5b86f77435b128bfca"),
    ];

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var filter = templateTable.Items.TryGetValue(BarrelId, out var barrel)
            ? barrel.Properties?.Slots?.FirstOrDefault(slot => slot.Name == "mod_muzzle")?.Properties?.Filters?.FirstOrDefault()
            : null;
        if (filter?.Filter == null)
        {
            logger.Warning("[CursedThings] PKM-12 barrel muzzle slot not found, its muzzle devices were not checked.");
            return Task.CompletedTask;
        }

        var added = filter.Filter.Count(id => !SaigaMuzzleDevices.Contains(id));
        // A new set instead of editing the old one, which another item may share.
        filter.Filter = new HashSet<MongoId>(SaigaMuzzleDevices);
        if (added > 0)
        {
            logger.Info($"[CursedThings] PKM-12 barrel: removed {added} muzzle device(s) added by other mods. It takes only the Saiga-12 ones.");
        }

        return Task.CompletedTask;
    }
}
