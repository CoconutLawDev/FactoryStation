using Robust.Shared.GameObjects;

namespace Content.Server.FactoryStation.Components;

[RegisterComponent]
public sealed partial class FactorySmokeTileComponent : Component
{
    /// <summary>
    /// Плотность дыма (влияет на видимость).
    /// </summary>
    [DataField]
    public float Density = 1f;

    /// <summary>
    /// Время жизни облака в секундах.
    /// </summary>
    [DataField]
    public float Lifetime = 15f;

    /// <summary>
    /// Токсичность — урон асфиксией в секунду.
    /// </summary>
    [DataField]
    public float Toxicity = 0.5f;

    /// <summary>
    /// Скорость рассеивания плотности в секунду.
    /// </summary>
    [DataField]
    public float DissipationRate = 0.1f;

    /// <summary>
    /// Может ли дым загореться.
    /// </summary>
    [DataField]
    public bool Flammable = true;
}
