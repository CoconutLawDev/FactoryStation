using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Server.FactoryStation.Components;

[RegisterComponent]
public sealed partial class FactoryFlickerComponent : Component
{
    [DataField] public float MinEnergyMultiplier = 0.35f;
    [DataField] public float MaxEnergyMultiplier = 1.0f;
    [DataField] public float MinRadiusMultiplier = 0.65f;
    [DataField] public float MaxRadiusMultiplier = 1.0f;
    [DataField] public float MinFlickerDelay = 0.03f;
    [DataField] public float MaxFlickerDelay = 0.18f;
    [DataField] public float BlackoutChance = 0.025f;

    /// <summary>
    /// Применять ли индустриальный оттенок к цвету лампы.
    /// </summary>
    [DataField] public bool ApplyTint = true;

    // Runtime
    public float BaseEnergy;
    public float BaseRadius;
    public Color BaseColor;
    public float NextFlicker;
    public bool FlickerInitialized;
}
