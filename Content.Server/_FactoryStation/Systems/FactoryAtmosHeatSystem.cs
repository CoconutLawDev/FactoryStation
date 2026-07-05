using Content.Server.Atmos.EntitySystems;
using Content.Server.FactoryStation.Components;
using Content.Shared.Atmos;
using Content.Shared.Lathe;
using Robust.Shared.Map.Components;
using Robust.Shared.GameObjects;

namespace Content.Server.FactoryStation.Systems;

public sealed partial class FactoryAtmosHeatSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    private float _accumulator;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < 1f)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<
            FactoryIndustrialHeatComponent,
            LatheComponent>();

        while (query.MoveNext(out var uid, out var heat, out var lathe))
        {
            if (lathe.CurrentRecipe == null)
                continue;

            var mixture = _atmosphere.GetContainingMixture(uid, true);
            if (mixture == null)
                continue;

            // Нагрев атмосферы пропорционально температуре печи
            // Чем горячее печь, тем сильнее греет воздух
            float heatRatio = heat.CurrentHeat / heat.DangerThreshold;
            heatRatio = Math.Clamp(heatRatio, 0.5f, heat.MaxAtmosHeatMultiplier);

            var effectiveAtmosHeat = heat.AtmosHeatPerSecond * heatRatio * 100f; // Увеличиваем множитель

            // Нагреваем воздух, не выше 1500K (1227°C)
            if (mixture.Temperature < 1500f)
            {
                var volumeFactor = Math.Clamp(mixture.Volume, 1f, 1000f);
                var heatToAdd = effectiveAtmosHeat * 1000f / volumeFactor;
                _atmosphere.AddHeat(mixture, heatToAdd);
            }

            // CO2 при опасной температуре
            if (heat.CurrentHeat >= heat.DangerThreshold)
            {
                float co2Ratio = heat.CurrentHeat >= heat.CriticalThreshold ? 3f : 1f;
                mixture.AdjustMoles(Gas.CarbonDioxide, heat.CO2PerSecond * co2Ratio);
            }

            // Поджог при критической температуре
            if (heat.CurrentHeat >= heat.CriticalThreshold)
            {
                var transform = Transform(uid);
                if (transform.GridUid != null &&
                    TryComp<MapGridComponent>(transform.GridUid.Value, out var grid))
                {
                    var tile = _mapSystem.TileIndicesFor(
                        transform.GridUid.Value,
                        grid,
                        transform.Coordinates);

                    var tileMixture = _atmosphere.GetTileMixture(transform.GridUid.Value, null, tile, true);
                    if (tileMixture != null && tileMixture.GetMoles(Gas.Oxygen) > 0)
                    {
                        _atmosphere.HotspotExpose(
                            transform.GridUid.Value,
                            tile,
                            1000f,
                            50f,
                            soh: true);
                    }
                }
            }
        }
    }
}
