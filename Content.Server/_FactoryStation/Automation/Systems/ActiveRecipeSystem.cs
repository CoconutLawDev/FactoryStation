using System.Collections.Generic;
using Content.Server.Atmos.EntitySystems;
using Content.Server.FactoryStation.Components;
using Content.Shared.Lathe;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server.FactoryStation.Systems;

public sealed partial class IndustrialSpillageSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;

    private float _updateAccumulator;

    public override void Update(float frameTime)
    {
        _updateAccumulator += frameTime;
        if (_updateAccumulator < 1f)
            return;

        _updateAccumulator = 0f;

        var query = EntityQueryEnumerator<FactoryIndustrialHeatComponent, LatheComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var heat, out var lathe, out var xform))
        {
            if (lathe.CurrentRecipe == null)
                continue;

            heat.SpillageAccumulator += 1f;
            if (heat.SpillageAccumulator < heat.SpillageInterval)
                continue;

            heat.SpillageAccumulator = 0f;

            var temperatureFactor = heat.CurrentHeat / heat.MaxHeat;
            var adjustedChance = heat.SpillageChance * (1f + temperatureFactor * 2f);
            // FactoryStation-Edit: Clamp chance to 0-1 range
            adjustedChance = Math.Clamp(adjustedChance, 0f, 1f);

            if (_random.Prob(adjustedChance))
            {
                SpawnSpillage(heat, xform);
            }
        }
    }

    private void SpawnSpillage(FactoryIndustrialHeatComponent heat, TransformComponent xform)
    {
        var gridUid = xform.GridUid;
        if (gridUid == null || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var tilePos = _mapSystem.TileIndicesFor(gridUid.Value, grid, xform.Coordinates);

        var tileMixture = _atmosphere.GetTileMixture(gridUid.Value, null, tilePos, true);
        if (tileMixture == null)
            return;

        var neighborOffsets = new Vector2i[]
        {
            new(-1, -1), new(0, -1), new(1, -1),
            new(-1,  0),              new(1,  0),
            new(-1,  1), new(0,  1), new(1,  1)
        };

        var validTiles = new List<Vector2i>();

        foreach (var offset in neighborOffsets)
        {
            var neighborTile = tilePos + offset;

            if (!IsTileBlocked(gridUid.Value, grid, neighborTile))
                validTiles.Add(neighborTile);
        }

        if (validTiles.Count == 0)
            return;

        var chosenTile = _random.Pick(validTiles);
        var spawnCoords = _mapSystem.GridTileToLocal(gridUid.Value, grid, chosenTile);

        Spawn(heat.SpillagePrototype, spawnCoords);

        if (heat.EmitsSparks)
        {
            Spawn("EffectSparks", spawnCoords);
        }
    }

    private bool IsTileBlocked(EntityUid gridUid, MapGridComponent grid, Vector2i tilePos)
    {
        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tilePos);
        return anchored.MoveNext(out _);
    }
}
