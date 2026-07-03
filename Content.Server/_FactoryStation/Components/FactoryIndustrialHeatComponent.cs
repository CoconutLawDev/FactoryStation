using System;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Prototypes;

namespace Content.Server.FactoryStation.Components;

[RegisterComponent]
public sealed partial class FactoryIndustrialHeatComponent : Component
{
    [DataField] public float CurrentHeat = 20f;
    [DataField] public float MaxHeat = 1500f;
    [DataField] public float HeatPerSecond = 55f;
    [DataField] public float CooldownPerSecond = 8f;

    [DataField] public float SmokeThreshold = 200f;
    [DataField] public float DangerThreshold = 900f;
    [DataField] public float CriticalThreshold = 1200f;

    [DataField] public bool ProducingSmoke = true;
    [DataField] public float SmokeInterval = 3f;
    [DataField] public float SmokeAccumulator = 0f;
    [DataField] public float SmokeRadius = 3f;
    [DataField] public float SmokeSpreadInterval = 2f;

    // FactoryStation-Edit-Start: Smoke expansion over time
    /// <summary>
    /// How long the station has been producing smoke continuously.
    /// Used to gradually expand smoke radius.
    /// </summary>
    public float SmokeActiveTime = 0f;

    /// <summary>
    /// Current radius of smoke cloud, grows over time up to SmokeRadius.
    /// </summary>
    public float CurrentSmokeRadius = 1f;

    /// <summary>
    /// How fast the smoke radius expands per SmokeInterval.
    /// </summary>
    [DataField] public float SmokeExpansionRate = 0.5f;

    /// <summary>
    /// The minimum radius the smoke starts at.
    /// </summary>
    [DataField] public float MinSmokeRadius = 1f;
    // FactoryStation-Edit-End

    [DataField] public float AtmosHeatPerSecond = 25f;
    [DataField] public float CO2PerSecond = 0.25f;

    // FactoryStation-Edit-Start: Atmospheric heat transfer scaling
    /// <summary>
    /// Maximum multiplier for atmospheric heating based on CurrentHeat.
    /// At DangerThreshold, heating is 1x. At CriticalThreshold, heating is MaxAtmosHeatMultiplier.
    /// </summary>
    [DataField] public float MaxAtmosHeatMultiplier = 3f;
    // FactoryStation-Edit-End

    [DataField] public float ExplosionChance = 0.05f;
    [DataField] public float ExplosionIntensity = 50f;
    [DataField] public float ExplosionSlope = 3f;
    [DataField] public float ExplosionMaxTileIntensity = 10f;
    public TimeSpan? LastExplosionTime;

    [DataField] public float DamagePerSecondCritical = 15f;

    [DataField] public SoundSpecifier? RunningSound;
    public EntityUid? AudioStream;

    [DataField] public SoundSpecifier? AlarmSound;
    public EntityUid? AlarmStream;

    [DataField] public float SpillageInterval = 5f;
    [DataField] public EntProtoId SpillagePrototype = "PuddleFactoryLubricant";
    [DataField] public float SpillageChance = 0.4f;
    [DataField] public bool EmitsSparks = true;
    public float SpillageAccumulator;

    [DataField] public int UpgradeCount = 0;

    [DataField] public bool AmbientCoolingEnabled = true;
    [DataField] public float AmbientCoolingCoefficient = 0.5f;
    [DataField] public float MinAmbientTemperature = -200f;
    [DataField] public float RoomTemperature = 20f;
    [DataField] public bool RequireAtmosphereForCooling = true;
}
