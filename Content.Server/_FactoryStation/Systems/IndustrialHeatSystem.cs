using System;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Damage.Systems;
using Content.Server.FactoryStation.Components;
using Content.Shared.Damage;
using Content.Shared.Lathe;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Log;
using Robust.Shared.Random;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;
using Robust.Shared.Map;
using Content.Shared.Atmos;

namespace Content.Server.FactoryStation.Systems;

public sealed partial class IndustrialHeatSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private SharedPointLightSystem _pointLight = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private ISawmill _sawmill = Logger.GetSawmill("factory.heat");

    private float _updateAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactoryIndustrialHeatComponent, LatheStartPrintingEvent>(OnLatheStarted);
        SubscribeLocalEvent<FactoryIndustrialHeatComponent, ComponentShutdown>(OnHeatShutdown);
        SubscribeLocalEvent<FactoryIndustrialHeatComponent, ComponentRemove>(OnHeatRemove);
        SubscribeLocalEvent<FactoryIndustrialHeatComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, FactoryIndustrialHeatComponent component, MapInitEvent args)
    {
        if (TryComp<LatheComponent>(uid, out var lathe))
        {
            lathe.CurrentTemperature = component.CurrentHeat;
            lathe.MaxSafeTemperature = component.DangerThreshold;
            Dirty(uid, lathe);
        }
    }

    private void OnLatheStarted(EntityUid uid, FactoryIndustrialHeatComponent component, ref LatheStartPrintingEvent args)
    {
        // Добавляем нагрев только при холодном старте
        if (component.CurrentHeat < 100f)
            component.CurrentHeat += 35f;

        if (TryComp<LatheComponent>(uid, out var lathe))
        {
            lathe.CurrentTemperature = component.CurrentHeat;
            Dirty(uid, lathe);
        }
    }

    private void OnHeatShutdown(EntityUid uid, FactoryIndustrialHeatComponent component, ComponentShutdown args)
    {
        StopRunningSound(component);
        StopAlarm(uid, component);
        component.SpillageAccumulator = 0f;
    }

    private void OnHeatRemove(EntityUid uid, FactoryIndustrialHeatComponent component, ComponentRemove args)
    {
        StopRunningSound(component);
        StopAlarm(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateAccumulator += frameTime;

        // Обрабатываем накопившееся время при лагах
        while (_updateAccumulator >= 1f)
        {
            _updateAccumulator -= 1f;
            ProcessHeat();
        }
    }

    private void ProcessHeat()
    {
        var query = EntityQueryEnumerator<FactoryIndustrialHeatComponent, LatheComponent>();

        while (query.MoveNext(out var uid, out var heat, out var lathe))
        {
            var running = lathe.CurrentRecipe != null;

            if (running)
            {
                heat.CurrentHeat += heat.HeatPerSecond;
                EnsureRunningSound(uid, heat);
            }
            else
            {
                heat.CurrentHeat -= heat.CooldownPerSecond;
                StopRunningSound(heat);
            }

            if (heat.AmbientCoolingEnabled)
                ApplyAmbientCooling(uid, heat);

            heat.CurrentHeat = Math.Clamp(heat.CurrentHeat, 20f, heat.MaxHeat);

            lathe.CurrentTemperature = heat.CurrentHeat;
            lathe.MaxSafeTemperature = heat.DangerThreshold;
            Dirty(uid, lathe);

            var state = heat.CurrentHeat >= heat.CriticalThreshold ? OverheatState.Critical
                : heat.CurrentHeat >= heat.DangerThreshold ? OverheatState.Warning
                : OverheatState.Normal;

            ProcessAlarm(uid, heat, state);

            if (state == OverheatState.Critical)
            {
                ProcessCriticalOverheat(uid, heat);
            }
        }
    }

    private void ProcessAlarm(EntityUid uid, FactoryIndustrialHeatComponent comp, OverheatState state)
    {
        if (state == OverheatState.Critical)
        {
            if (comp.AlarmSound != null && comp.AlarmStream == null)
            {
                var stream = _audio.PlayPvs(comp.AlarmSound, uid,
                    AudioParams.Default.WithLoop(true).WithVolume(-2f));
                if (stream != null)
                    comp.AlarmStream = stream.Value.Entity;
            }

            if (TryComp<PointLightComponent>(uid, out var light))
            {
                _pointLight.SetEnabled(uid, true, light);
                _pointLight.SetColor(uid, Color.Red, light);
                _pointLight.SetRadius(uid, 3f, light);
                _pointLight.SetEnergy(uid, 2f, light);
            }
        }
        else
        {
            StopAlarm(uid, comp);
        }
    }

    private void StopAlarm(EntityUid uid, FactoryIndustrialHeatComponent comp)
    {
        if (comp.AlarmStream != null)
        {
            _audio.Stop(comp.AlarmStream.Value);
            comp.AlarmStream = null;
        }

        if (TryComp<PointLightComponent>(uid, out var light))
        {
            _pointLight.SetEnabled(uid, false, light);
        }
    }

    private void ApplyAmbientCooling(EntityUid uid, FactoryIndustrialHeatComponent heat)
    {
        var mixture = _atmosphere.GetContainingMixture(uid, true);
        float ambientTemp;

        if (mixture != null)
        {
            ambientTemp = mixture.Temperature - 273.15f;
        }
        else if (heat.RequireAtmosphereForCooling)
        {
            return;
        }
        else
        {
            ambientTemp = 2.7f;
        }

        float coolingCoefficient = heat.AmbientCoolingCoefficient;

        if (mixture != null)
        {
            var frezonMoles = mixture.GetMoles(Gas.Frezon);
            if (frezonMoles > 0)
            {
                var frezonBonus = 1f + Math.Min(frezonMoles * 0.5f, 1f);
                coolingCoefficient *= frezonBonus;

                var frezonToRemove = Math.Min(frezonMoles, 0.1f * coolingCoefficient);
                mixture.AdjustMoles(Gas.Frezon, -frezonToRemove);
            }
        }

        if (heat.CurrentHeat > ambientTemp)
        {
            float tempDiff = heat.CurrentHeat - ambientTemp;
            float cooling = tempDiff * coolingCoefficient;
            heat.CurrentHeat -= cooling;
        }
    }

    private void EnsureRunningSound(EntityUid uid, FactoryIndustrialHeatComponent component)
    {
        if (component.RunningSound == null) return;
        if (component.AudioStream != null) return;

        var stream = _audio.PlayPvs(
            component.RunningSound,
            uid,
            AudioParams.Default.WithLoop(true).WithVolume(-4f));

        if (stream == null)
        {
            _sawmill.Warning($"Failed to create sound stream for entity {uid}");
            return;
        }

        component.AudioStream = stream.Value.Entity;
    }

    private void StopRunningSound(FactoryIndustrialHeatComponent component)
    {
        if (component.AudioStream == null) return;
        _audio.Stop(component.AudioStream.Value);
        component.AudioStream = null;
    }

    private void ProcessCriticalOverheat(EntityUid uid, FactoryIndustrialHeatComponent heat)
    {
        if (TryComp<DamageableComponent>(uid, out var damageable))
        {
            var damageSpec = new DamageSpecifier
            {
                DamageDict = { ["Heat"] = heat.DamagePerSecondCritical }
            };
            _damageable.TryChangeDamage(uid, damageSpec, interruptsDoAfters: false);
        }

        var now = _gameTiming.CurTime;
        if (heat.LastExplosionTime != null && now - heat.LastExplosionTime.Value < TimeSpan.FromSeconds(10))
            return;

        var chance = Math.Clamp(heat.ExplosionChance, 0f, 1f);

        if (_random.Prob(chance))
        {
            heat.LastExplosionTime = now;

            _explosion.QueueExplosion(
                uid,
                "Default",
                heat.ExplosionIntensity,
                heat.ExplosionSlope,
                heat.ExplosionMaxTileIntensity,
                user: null,
                addLog: true);

            heat.CurrentHeat = heat.MaxHeat * 0.6f;
        }
    }

    private enum OverheatState
    {
        Normal,
        Warning,
        Critical
    }
}
