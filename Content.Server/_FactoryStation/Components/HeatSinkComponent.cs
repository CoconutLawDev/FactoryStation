using Robust.Shared.GameObjects;

namespace Content.Server.FactoryStation.Components;

/// <summary>
/// Компонент-метка для радиаторной пластины.
/// При вставке в слот "heat_sink" станка увеличивает его AmbientCoolingCoefficient.
/// </summary>
[RegisterComponent]
public sealed partial class HeatSinkComponent : Component
{
    /// <summary>
    /// На сколько увеличивается AmbientCoolingCoefficient станка.
    /// </summary>
    [DataField]
    public float CoolingBonus = 0.01f;
}
