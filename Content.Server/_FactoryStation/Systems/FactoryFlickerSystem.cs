using Content.Server.FactoryStation.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Maths;

namespace Content.Server.FactoryStation.Systems;

public sealed partial class FactoryFlickerSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private PointLightSystem _pointLight = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FactoryFlickerComponent, ComponentShutdown>(OnFlickerShutdown);
    }

    private void OnFlickerShutdown(EntityUid uid, FactoryFlickerComponent component, ComponentShutdown args)
    {
        component.FlickerInitialized = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FactoryFlickerComponent, PointLightComponent>();

        while (query.MoveNext(out var uid, out var flicker, out var pointLight))
        {
            if (!flicker.FlickerInitialized)
            {
                flicker.BaseEnergy = pointLight.Energy;
                flicker.BaseRadius = pointLight.Radius;
                flicker.BaseColor = pointLight.Color;
                flicker.FlickerInitialized = true;
            }

            if (!pointLight.Enabled)
                continue;

            flicker.NextFlicker -= frameTime;

            if (flicker.NextFlicker > 0f)
                continue;

            flicker.NextFlicker = _random.NextFloat(flicker.MinFlickerDelay, flicker.MaxFlickerDelay);

            var energyMul = _random.NextFloat(flicker.MinEnergyMultiplier, flicker.MaxEnergyMultiplier);
            var radiusMul = _random.NextFloat(flicker.MinRadiusMultiplier, flicker.MaxRadiusMultiplier);

            if (_random.Prob(flicker.BlackoutChance))
            {
                energyMul *= 0.08f;
                radiusMul *= 0.5f;
            }

            _pointLight.SetEnergy(uid, flicker.BaseEnergy * energyMul, pointLight);
            _pointLight.SetRadius(uid, flicker.BaseRadius * radiusMul, pointLight);

            if (flicker.ApplyTint)
            {
                var tint = new Color(
                    _random.NextFloat(0.95f, 1.0f),
                    _random.NextFloat(0.90f, 0.95f),
                    _random.NextFloat(0.72f, 0.80f));
                _pointLight.SetColor(uid, flicker.BaseColor * tint, pointLight);
            }
        }
    }
}
