using System;
using Robust.Shared.Serialization;

namespace Content.Shared.FactoryStation.Messages;

[Serializable, NetSerializable]
public sealed class FactoryGoalSelectMessage : BoundUserInterfaceMessage
{
    public string? GoalId;

    public FactoryGoalSelectMessage(string? goalId)
    {
        GoalId = goalId;
    }
}
