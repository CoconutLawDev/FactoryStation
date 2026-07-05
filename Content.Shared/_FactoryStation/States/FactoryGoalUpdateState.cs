using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared.FactoryStation.States;

[Serializable, NetSerializable]
public sealed class FactoryGoalUpdateState : BoundUserInterfaceState
{
    public string? CurrentGoal;
    public int CurrentProgress;
    public List<GoalInfo> AvailableGoals;
    public string? GoalName;
    public string? GoalDifficulty;
    public int RequiredAmount;
    public double RemainingTime;

    public FactoryGoalUpdateState(
        string? currentGoal,
        int currentProgress,
        List<GoalInfo> availableGoals,
        string? goalName = null,
        string? goalDifficulty = null,
        int requiredAmount = 0,
        double remainingTime = 0)
    {
        CurrentGoal = currentGoal;
        CurrentProgress = currentProgress;
        AvailableGoals = availableGoals;
        GoalName = goalName;
        GoalDifficulty = goalDifficulty;
        RequiredAmount = requiredAmount;
        RemainingTime = remainingTime;
    }
}

[Serializable, NetSerializable]
public sealed class GoalInfo
{
    public string Id { get; }
    public string Name { get; }
    public string Difficulty { get; }

    public GoalInfo(string id, string name, string difficulty)
    {
        Id = id;
        Name = name;
        Difficulty = difficulty;
    }
}
