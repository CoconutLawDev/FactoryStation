using System.Collections.Generic;
using Content.Shared.FactoryStation.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.FactoryStation.Components;

[RegisterComponent]
public sealed partial class StationFactoryGoalComponent : Component
{
    [DataField]
    public ProtoId<FactoryGoalPrototype>? CurrentGoal;

    [DataField]
    public int CurrentProgress;

    [DataField]
    public List<ProtoId<FactoryGoalPrototype>> AvailableGoals = new();

    [DataField]
    public HashSet<ProtoId<FactoryGoalPrototype>> UsedGoals = new();

    /// <summary>
    /// Время последнего выбора контракта (для кулдауна).
    /// </summary>
    public TimeSpan LastGoalChangeTime;

    /// <summary>
    /// Кулдаун между сменами контракта в секундах.
    /// </summary>
    [DataField]
    public float GoalChangeCooldown = 30f;

    /// <summary>
    /// Время, когда контракт истечёт (45 минут от выбора).
    /// </summary>
    public TimeSpan GoalExpirationTime;

    /// <summary>
    /// Просрочен ли текущий контракт.
    /// </summary>
    public bool IsGoalExpired;
}
