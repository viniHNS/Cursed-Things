using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace CursedThings;

// The Absolut spawns wherever the game's Tarkovskaya does, loose and in containers, with a lower weight.
// LooseLoot and StaticLoot are LazyLoad without cache: every read deserialises again and runs the transformers,
// so each transformer adds the Absolut once to a fresh copy.
[Injectable(TypePriority = OnLoadOrder.Preload + 3)]
public class AbsolutVodkaLoot(LocationTable locationTable) : IOnLoad
{
    private static readonly MongoId AbsolutId = new("3b956603fbbab73ab62bd2db");
    private static readonly MongoId TarkovskayaId = new("5d40407c86f774318526545a");

    // Weight of the Absolut relative to the Tarkovskaya in the same spawn point or container
    private const double Rarity = 0.6;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        foreach (var location in locationTable.GetDictionary().Values)
        {
            location.LooseLoot?.AddTransformer(AddToLooseLoot);
            location.StaticLoot?.AddTransformer(AddToStaticLoot);
        }

        return Task.CompletedTask;
    }

    private static LooseLoot? AddToLooseLoot(LooseLoot? looseLoot)
    {
        foreach (var spawnpoint in looseLoot?.Spawnpoints ?? [])
        {
            var template = spawnpoint.Template;
            if (template?.Items == null || spawnpoint.ItemDistribution == null)
            {
                continue;
            }

            var vodkas = template.Items.Where(item => item.Template == TarkovskayaId && item.ComposedKey != null).ToList();
            if (vodkas.Count == 0)
            {
                continue;
            }

            var items = template.Items.ToList();
            var distribution = spawnpoint.ItemDistribution.ToList();
            foreach (var vodka in vodkas)
            {
                var weight = distribution.FirstOrDefault(entry => entry.ComposedKey?.Key == vodka.ComposedKey)?.RelativeProbability;
                if (weight is not > 0)
                {
                    continue;
                }

                // The generator draws a key from ItemDistribution and spawns the template item with the same key.
                var key = vodka.ComposedKey + "_absolut";
                items.Add(new SptLootItem
                {
                    Id = new MongoId(),
                    Template = AbsolutId,
                    ParentId = vodka.ParentId,
                    SlotId = vodka.SlotId,
                    Upd = new Upd { StackObjectsCount = 1 },
                    ComposedKey = key,
                });
                distribution.Add(new LooseLootItemDistribution
                {
                    ComposedKey = new ComposedKey { Key = key },
                    RelativeProbability = weight * Rarity,
                });
            }

            template.Items = items;
            spawnpoint.ItemDistribution = distribution;
        }

        return looseLoot;
    }

    private static Dictionary<MongoId, StaticLootDetails>? AddToStaticLoot(Dictionary<MongoId, StaticLootDetails>? staticLoot)
    {
        if (staticLoot == null)
        {
            return staticLoot;
        }

        foreach (var container in staticLoot.Values)
        {
            var distribution = container.ItemDistribution?.ToList();
            var weight = distribution?.FirstOrDefault(entry => entry.Tpl == TarkovskayaId)?.RelativeProbability;
            if (distribution == null || weight is not > 0)
            {
                continue;
            }

            distribution.Add(new ItemDistribution { Tpl = AbsolutId, RelativeProbability = (float)(weight.Value * Rarity) });
            container.ItemDistribution = distribution;
        }

        return staticLoot;
    }
}
