using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Shared.FactoryStation.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class FactoryGoalPadComponent : Component
{
    /// <summary>
    /// Коды тревоги, при которых платформа принимает ресурсы.
    /// Если пусто — принимает всегда.
    /// </summary>
    [DataField]
    public List<string> AllowedAlertLevels = new() { "green", "blue", "yellow", "violet" };

    /// <summary>
    /// Прототип предмета, который сейчас требуется по контракту.
    /// Устанавливается системой при выборе цели.
    /// </summary>
    [DataField]
    public string? CurrentRequiredItem;
}
