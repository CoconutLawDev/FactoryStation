using System.Collections.Generic;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.FactoryStation.Components;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Lathe;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server.FactoryStation.Systems;

public sealed partial class FactorySmokeSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;

    private float _accumulator;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;

        if (_accumulator < 2f)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<
            FactoryIndustrialHeatComponent,
            LatheComponent,
            TransformComponent>();

        while (query.MoveNext(out var uid, out var heat, out var lathe, out var xform))
        {
            if (lathe.CurrentRecipe == null)
            {
                heat.SmokeActiveTime = 0f;
                heat.CurrentSmokeRadius = heat.MinSmokeRadius;
                continue;
            }

            if (heat.CurrentHeat < heat.SmokeThreshold)
            {
                heat.SmokeActiveTime = 0f;
                heat.CurrentSmokeRadius = heat.MinSmokeRadius;
                continue;
            }

            heat.SmokeActiveTime += 2f;

            heat.CurrentSmokeRadius = heat.MinSmokeRadius +
                (heat.SmokeActiveTime / heat.SmokeSpreadInterval) * heat.SmokeExpansionRate;

            heat.CurrentSmokeRadius = Math.Min(heat.CurrentSmokeRadius, heat.SmokeRadius);

            SpawnSmokeCloud(uid, heat, xform);
        }

        ProcessSmokeEffects(frameTime);
    }

    private void SpawnSmokeCloud(EntityUid uid, FactoryIndustrialHeatComponent heat, TransformComponent xform)
    {
        if (xform.GridUid == null || !TryComp<MapGridComponent>(xform.GridUid, out var grid))
            return;

        var tilePos = _mapSystem.TileIndicesFor(xform.GridUid.Value, grid, xform.Coordinates);
        var radius = (int)MathF.Ceiling(heat.CurrentSmokeRadius);

        var validTiles = new List<Vector2i>();
        var innerRadius = Math.Max(0, radius - 1);

        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                var distSq = x * x + y * y;

                if (distSq > radius * radius || distSq <= innerRadius * innerRadius)
                    continue;

                var checkTile = tilePos + new Vector2i(x, y);

                if (!IsValidSmokeTile(xform.GridUid.Value, grid, checkTile))
                    continue;

                var coords = _mapSystem.GridTileToLocal(xform.GridUid.Value, grid, checkTile);
                if (HasExistingSmoke(coords))
                    continue;

                validTiles.Add(checkTile);
            }
        }

        var maxSpawn = Math.Min(5, validTiles.Count);
        for (var i = 0; i < maxSpawn; i++)
        {
            var tile = _random.PickAndTake(validTiles);
            var coords = _mapSystem.GridTileToLocal(xform.GridUid.Value, grid, tile);

            Spawn("FactoryHeavySmoke", coords);

            var tileMixture = _atmosphere.GetTileMixture(xform.GridUid.Value, null, tile, true);
            if (tileMixture != null)
            {
                tileMixture.AdjustMoles(Gas.CarbonDioxide, 0.5f);
            }
        }
    }

    private bool IsValidSmokeTile(EntityUid gridUid, MapGridComponent grid, Vector2i tilePos)
    {
        if (!_mapSystem.TryGetTileRef(gridUid, grid, tilePos, out var tile))
            return false;

        if (tile.Tile.IsEmpty)
            return false;

        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tilePos);
        while (anchored.MoveNext(out var entity))
        {
            if (HasComp<MapGridComponent>(entity))
                continue;
            return false;
        }

        return true;
    }

    private bool HasExistingSmoke(EntityCoordinates coords)
    {
        var entities = _lookup.GetEntitiesInRange(coords, 0.5f);
        foreach (var entity in entities)
        {
            if (HasComp<FactorySmokeTileComponent>(entity))
                return true;
        }
        return false;
    }

    private void ProcessSmokeEffects(float frameTime)
    {
        var query = EntityQueryEnumerator<FactorySmokeTileComponent>();
        while (query.MoveNext(out var uid, out var smoke))
        {
            smoke.Lifetime -= frameTime;
            if (smoke.Lifetime <= 0)
            {
                QueueDel(uid);
                continue;
            }

            if (smoke.Toxicity > 0)
            {
                var entities = _lookup.GetEntitiesInRange(Transform(uid).Coordinates, 0.5f);
                foreach (var entity in entities)
                {
                    if (HasComp<DamageableComponent>(entity))
                    {
                        var damage = new DamageSpecifier
                        {
                            DamageDict = { ["Asphyxiation"] = smoke.Toxicity * frameTime }
                        };
                        _damageable.TryChangeDamage(entity, damage, interruptsDoAfters: false);
                    }
                }
            }

            smoke.Density = Math.Max(smoke.Density - smoke.DissipationRate * frameTime, 0f);
        }
    }
}
