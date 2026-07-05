using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.FactoryStation.Prototypes;

[Prototype]
public sealed partial class FactoryGoalPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = default!;

    [DataField]
    public string Name { get; set; } = string.Empty;

    [DataField]
    public string Difficulty { get; set; } = "Light";

    [DataField]
    public EntProtoId RequiredItem { get; set; }

    [DataField]
    public int RequiredAmount { get; set; }

    [DataField]
    public string RewardMessage { get; set; } = string.Empty;
}
