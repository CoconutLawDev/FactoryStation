using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Access.Systems;
using Content.Server.AlertLevel;
using Content.Server.Chat.Systems;
using Content.Server.FactoryStation.Components;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.FactoryStation;
using Content.Shared.FactoryStation.Components;
using Content.Shared.FactoryStation.Messages;
using Content.Shared.FactoryStation.Prototypes;
using Content.Shared.FactoryStation.States;
using Content.Shared.Interaction;
using Content.Shared.Power.Components;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.FactoryStation;

public sealed partial class FactoryGoalSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedStackSystem _stack = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private const float SuctionInterval = 1.0f;
    private const float SuctionRadius = 2.0f;
    private float _suctionAccumulator;
    private const float GoalTimeoutMinutes = 45f;

    private static readonly SoundSpecifier ItemAcceptSound = new SoundPathSpecifier("/Audio/Machines/quickbeep.ogg");
    private static readonly SoundSpecifier GoalCompleteSound = new SoundPathSpecifier("/Audio/Machines/quickbeep.ogg");
    private static readonly SoundSpecifier GoalExpiredSound = new SoundPathSpecifier("/Audio/Machines/quickbeep.ogg");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactoryGoalConsoleComponent, ComponentStartup>(OnConsoleStartup);
        SubscribeLocalEvent<FactoryGoalConsoleComponent, FactoryGoalSelectMessage>(OnGoalSelected);
        SubscribeLocalEvent<FactoryGoalPadComponent, InteractUsingEvent>(OnPadInteract);
        SubscribeLocalEvent<FactoryGoalDisplayComponent, ComponentStartup>(OnDisplayStartup);
    }

    private StationFactoryGoalComponent? GetStationGoals(EntityUid uid)
    {
        var stationUid = _station.GetOwningStation(uid);
        if (stationUid == null)
            return null;

        return EnsureComp<StationFactoryGoalComponent>(stationUid.Value);
    }

    private bool HasPoweredConsole(EntityUid uid)
    {
        var stationUid = _station.GetOwningStation(uid);
        if (stationUid == null)
            return false;

        var consoleQuery = EntityQueryEnumerator<FactoryGoalConsoleComponent, TransformComponent>();
        while (consoleQuery.MoveNext(out var consoleUid, out _, out var consoleXform))
        {
            if (_station.GetOwningStation(consoleUid) != stationUid)
                continue;

            if (TryComp<ApcPowerReceiverComponent>(consoleUid, out var power) && !power.Powered)
                continue;

            return true;
        }

        return false;
    }

    private void AddMoney(EntityUid stationUid, int amount)
    {
        if (TryComp<StationBankAccountComponent>(stationUid, out var bank))
        {
#pragma warning disable RA0002
            if (bank.Accounts.TryGetValue("Cargo", out var current))
                bank.Accounts["Cargo"] = current + amount;
#pragma warning restore RA0002
            Dirty(stationUid, bank);
        }
    }

    private void OnConsoleStartup(EntityUid uid, FactoryGoalConsoleComponent component, ComponentStartup args)
    {
        var goals = GetStationGoals(uid);
        if (goals == null)
            return;

        if (goals.AvailableGoals.Count == 0)
            GenerateGoals(goals);

        UpdateAllUis(uid, goals);
    }

    private void OnDisplayStartup(EntityUid uid, FactoryGoalDisplayComponent component, ComponentStartup args)
    {
        var goals = GetStationGoals(uid);
        if (goals == null)
            return;

        SendDisplayState(uid, goals);
    }

    private double GetRemainingTime(StationFactoryGoalComponent goals)
    {
        if (goals.CurrentGoal == null || goals.IsGoalExpired)
            return 0;

        var remaining = goals.GoalExpirationTime - _gameTiming.CurTime;
        return Math.Max(0, remaining.TotalSeconds);
    }

    private void SendDisplayState(EntityUid uid, StationFactoryGoalComponent goals)
    {
        string? goalName = null;
        string? difficulty = null;
        var requiredAmount = 0;
        double remainingTime = GetRemainingTime(goals);

        var currentGoal = goals.CurrentGoal;
        if (currentGoal != null && _protoMan.TryIndex(currentGoal.Value, out var proto))
        {
            goalName = proto.Name;
            difficulty = proto.Difficulty;
            requiredAmount = proto.RequiredAmount;
        }

        var availableGoals = new List<GoalInfo>();
        foreach (var goalId in goals.AvailableGoals)
        {
            if (_protoMan.TryIndex(goalId, out FactoryGoalPrototype? goalProto))
                availableGoals.Add(new GoalInfo(goalId, goalProto.Name, goalProto.Difficulty));
            else
                availableGoals.Add(new GoalInfo(goalId, goalId, "Unknown"));
        }

        var state = new FactoryGoalUpdateState(
            currentGoal?.Id,
            goals.CurrentProgress,
            availableGoals,
            goalName,
            difficulty,
            requiredAmount,
            remainingTime);

        _ui.SetUiState(uid, FactoryGoalDisplayUiKey.Key, state);
    }

    private ProtoId<FactoryGoalPrototype>? PickRandomGoal(List<FactoryGoalPrototype> goals, StationFactoryGoalComponent comp)
    {
        var available = goals.Where(g => !comp.UsedGoals.Contains(g.ID)).ToList();
        if (available.Count == 0)
        {
            comp.UsedGoals.Clear();
            available = goals.Where(g => !comp.UsedGoals.Contains(g.ID)).ToList();
        }
        if (available.Count == 0)
            return null;
        var selected = available[_random.Next(available.Count)];
        comp.UsedGoals.Add(selected.ID);
        return selected.ID;
    }

    private void GenerateGoals(StationFactoryGoalComponent comp)
    {
        var allGoals = _protoMan.EnumeratePrototypes<FactoryGoalPrototype>().ToList();
        var lightGoals = allGoals.Where(g => g.Difficulty == "Light").ToList();
        var mediumGoals = allGoals.Where(g => g.Difficulty == "Medium").ToList();
        var hardGoals = allGoals.Where(g => g.Difficulty == "Hard").ToList();

        comp.AvailableGoals.Clear();

        var light = PickRandomGoal(lightGoals, comp);
        var medium = PickRandomGoal(mediumGoals, comp);
        var hard = PickRandomGoal(hardGoals, comp);

        if (light != null) comp.AvailableGoals.Add(light.Value);
        if (medium != null) comp.AvailableGoals.Add(medium.Value);
        if (hard != null) comp.AvailableGoals.Add(hard.Value);
    }

    private string GetDifficultyColor(string? difficulty)
    {
        return difficulty switch
        {
            "Light" => "#5EFF7A",
            "Medium" => "#FFD95E",
            "Hard" => "#FF6B6B",
            _ => "#FFFFFF"
        };
    }

    private string GetDifficultyName(string? difficulty)
    {
        return difficulty switch
        {
            "Light" => "ЛЁГКИЙ",
            "Medium" => "СРЕДНИЙ",
            "Hard" => "КРИТИЧЕСКИЙ",
            _ => "НЕИЗВЕСТНЫЙ"
        };
    }

    private int GetGoalReward(string? difficulty)
    {
        return difficulty switch
        {
            "Light" => 50000,
            "Medium" => 100000,
            "Hard" => 200000,
            _ => 0
        };
    }

    private void OnGoalSelected(EntityUid uid, FactoryGoalConsoleComponent component, FactoryGoalSelectMessage args)
    {
        if (args.GoalId == null) return;

        var goals = GetStationGoals(uid);
        if (goals == null) return;

        var user = args.Actor;
        var currentTime = _gameTiming.CurTime;

        if (goals.LastGoalChangeTime > TimeSpan.Zero)
        {
            var timeSinceLastChange = currentTime - goals.LastGoalChangeTime;
            if (timeSinceLastChange.TotalSeconds < goals.GoalChangeCooldown)
            {
                var remaining = goals.GoalChangeCooldown - (float)timeSinceLastChange.TotalSeconds;
                _popup.PopupEntity($"Контракт можно сменить через {remaining:F0} сек.", uid, user);
                return;
            }
        }

        if (TryComp<AccessReaderComponent>(uid, out var access))
        {
            if (!_accessReader.IsAllowed(user, uid, access))
            {
                _popup.PopupEntity("У вас нет доступа для выбора промышленного контракта.", uid, user);
                return;
            }
        }

        if (!goals.AvailableGoals.Contains(args.GoalId)) return;

        if (goals.CurrentGoal != null && goals.CurrentProgress > 0)
        {
            var oldGoalName = "Неизвестный контракт";
            if (_protoMan.TryIndex(goals.CurrentGoal.Value, out var oldProto))
                oldGoalName = oldProto.Name;

            _popup.PopupEntity($"Предыдущий контракт \"{oldGoalName}\" отменён. Прогресс ({goals.CurrentProgress} ед.) утерян.", uid, user);

            var cancelAnnouncement = $"Центральное Командование уведомляет: контракт \"{oldGoalName}\" отменён.\n" +
                                     $"Поставленные ресурсы ({goals.CurrentProgress} ед.) списаны в пользу NanoTrasen как штраф за невыполнение.";
            _chat.DispatchStationAnnouncement(source: uid, message: cancelAnnouncement,
                sender: "Центральное Командование", playDefaultSound: false);
        }

        goals.CurrentGoal = args.GoalId;
        goals.CurrentProgress = 0;
        goals.LastGoalChangeTime = currentTime;
        goals.GoalExpirationTime = currentTime + TimeSpan.FromMinutes(GoalTimeoutMinutes);
        goals.IsGoalExpired = false;

        string? goalName = null;
        string? difficulty = null;
        var requiredAmount = 0;
        string? requiredItem = null;

        if (_protoMan.TryIndex<FactoryGoalPrototype>(args.GoalId, out var proto))
        {
            goalName = proto.Name;
            difficulty = proto.Difficulty;
            requiredAmount = proto.RequiredAmount;
            requiredItem = proto.RequiredItem;
        }

        var stationUid = _station.GetOwningStation(uid);
        if (stationUid != null)
        {
            var padQuery = EntityQueryEnumerator<FactoryGoalPadComponent, TransformComponent>();
            while (padQuery.MoveNext(out var padUid, out var padComp, out var padXform))
            {
                if (_station.GetOwningStation(padUid) != stationUid)
                    continue;

                padComp.CurrentRequiredItem = null;
                if (requiredItem != null)
                    padComp.CurrentRequiredItem = requiredItem;
                Dirty(padUid, padComp);
            }
        }

        var announcement = $"ВНИМАНИЕ: Центральное Командование санкционировало новый промышленный контракт.\n\n" +
                         $"Уровень приоритета: {GetDifficultyName(difficulty)}\n" +
                         $"Контракт: {goalName ?? args.GoalId}\n" +
                         $"Требуемый объём поставки: {requiredAmount}\n" +
                         $"Срок выполнения: {GoalTimeoutMinutes} минут\n\n" +
                         $"Станции предписывается немедленно приступить к производству и доставке указанных ресурсов.\n" +
                         $"Невыполнение квоты может негативно сказаться на рейтинге станции.";

        _chat.DispatchStationAnnouncement(source: uid, message: announcement,
            sender: "Центральное Командование", playDefaultSound: true);

        UpdateAllUis(uid, goals);
    }

    private void OnPadInteract(EntityUid uid, FactoryGoalPadComponent component, InteractUsingEvent args)
    {
        if (args.Handled) return;

        if (component.AllowedAlertLevels.Count > 0)
        {
            var stationUid = _station.GetOwningStation(uid);
            if (stationUid != null && TryComp<AlertLevelComponent>(stationUid, out var alertLevel))
            {
                if (!component.AllowedAlertLevels.Contains(alertLevel.CurrentLevel))
                {
                    _popup.PopupEntity($"Платформа не принимает ресурсы при текущем коде тревоги ({alertLevel.CurrentLevel}).", uid, args.User);
                    args.Handled = true;
                    return;
                }
            }
        }

        var goals = GetStationGoals(uid);
        if (goals == null) return;

        var currentGoal = goals.CurrentGoal;
        if (currentGoal == null) return;
        if (goals.IsGoalExpired) return;

        if (!_protoMan.TryIndex(currentGoal.Value, out FactoryGoalPrototype? goalProto)) return;

        var itemProto = MetaData(args.Used).EntityPrototype?.ID;
        if (itemProto == null) return;
        if (itemProto != goalProto.RequiredItem) return;

        ConsumeItem(uid, goals, goalProto, args.Used);
        UpdateAllUis(uid, goals);
        args.Handled = true;
    }

    private void ConsumeItem(EntityUid padUid, StationFactoryGoalComponent goals, FactoryGoalPrototype goalProto, EntityUid item)
    {
        if (TerminatingOrDeleted(item))
            return;

        int amount = 1;
        if (TryComp<StackComponent>(item, out var stack))
            amount = stack.Count;

        var remaining = goals.CurrentProgress + amount - goalProto.RequiredAmount;

        if (remaining > 0)
        {
            var needed = amount - remaining;
            goals.CurrentProgress += needed;

            if (TryComp<StackComponent>(item, out var stackComp))
            {
                _stack.SetCount((item, stackComp), remaining);
            }
            else
            {
                Del(item);
                var returnItem = Spawn(goalProto.RequiredItem, Transform(padUid).Coordinates);
                if (TryComp<StackComponent>(returnItem, out var returnStack))
                    _stack.SetCount((returnItem, returnStack), remaining);
            }
        }
        else
        {
            goals.CurrentProgress += amount;
            Del(item);
        }

        _audio.PlayPvs(ItemAcceptSound, padUid);

        if (goals.CurrentProgress >= goalProto.RequiredAmount)
            CompleteGoal(padUid, goals, goalProto);
    }

    private void CompleteGoal(EntityUid originUid, StationFactoryGoalComponent goals, FactoryGoalPrototype goalProto)
    {
        var reward = GetGoalReward(goalProto.Difficulty);

        var stationUid = _station.GetOwningStation(originUid);
        if (stationUid != null)
        {
            AddMoney(stationUid.Value, reward);
        }

        var completeAnnouncement = $"Центральное Командование подтверждает успешное выполнение промышленного контракта.\n\n" +
                                   $"Контракт: {goalProto.Name}\n" +
                                   $"Уровень приоритета: {GetDifficultyName(goalProto.Difficulty)}\n" +
                                   $"Награда: {reward:N0} кредитов на счёт отдела снабжения\n\n" +
                                   $"Поставка зарегистрирована и внесена в централизованный производственный реестр NanoTrasen.\n" +
                                   $"{goalProto.RewardMessage}\n\n" +
                                   $"Рейтинг станции повышен.";

        _chat.DispatchStationAnnouncement(source: originUid, message: completeAnnouncement,
            sender: "Центральное Командование", playDefaultSound: true);

        _audio.PlayPvs(GoalCompleteSound, originUid);

        goals.AvailableGoals.Remove(goals.CurrentGoal!.Value);

        if (stationUid != null)
        {
            var padQuery = EntityQueryEnumerator<FactoryGoalPadComponent, TransformComponent>();
            while (padQuery.MoveNext(out var padUid, out var padComp, out var padXform))
            {
                if (_station.GetOwningStation(padUid) != stationUid)
                    continue;

                padComp.CurrentRequiredItem = null;
                Dirty(padUid, padComp);
            }
        }

        var sameDifficultyGoals = _protoMan.EnumeratePrototypes<FactoryGoalPrototype>()
            .Where(g => g.Difficulty == goalProto.Difficulty).ToList();

        var replacement = PickRandomGoal(sameDifficultyGoals, goals);
        if (replacement != null)
            goals.AvailableGoals.Add(replacement.Value);

        goals.CurrentGoal = null;
        goals.CurrentProgress = 0;
        goals.IsGoalExpired = false;
    }

    private void ExpireGoal(EntityUid stationUid, StationFactoryGoalComponent goals)
    {
        if (!goals.IsGoalExpired && goals.CurrentGoal != null)
        {
            goals.IsGoalExpired = true;

            var goalName = "Неизвестный контракт";
            if (_protoMan.TryIndex(goals.CurrentGoal.Value, out var proto))
                goalName = proto.Name;

            AddMoney(stationUid, -10000);

            var expireAnnouncement = $"Центральное Командование уведомляет: срок выполнения контракта \"{goalName}\" истёк.\n" +
                                     $"Штраф 10 000 кредитов списан со счёта отдела снабжения.\n" +
                                     $"Выберите новый контракт на консоли промышленного плана.";

            _chat.DispatchStationAnnouncement(source: stationUid, message: expireAnnouncement,
                sender: "Центральное Командование", playDefaultSound: true);

            _audio.PlayPvs(GoalExpiredSound, stationUid);

            goals.CurrentGoal = null;
            goals.CurrentProgress = 0;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _gameTiming.CurTime;

        // Обновляем UI каждую секунду для всех станций с активным контрактом
        var goalQuery = EntityQueryEnumerator<StationFactoryGoalComponent, TransformComponent>();
        while (goalQuery.MoveNext(out var stationUid, out var goals, out var xform))
        {
            if (goals.CurrentGoal != null && !goals.IsGoalExpired && currentTime >= goals.GoalExpirationTime)
            {
                ExpireGoal(stationUid, goals);
            }

            // Всегда обновляем UI, если есть активный контракт (таймер идёт)
            if (goals.CurrentGoal != null && !goals.IsGoalExpired)
            {
                UpdateAllUis(stationUid, goals);
            }
        }

        _suctionAccumulator += frameTime;
        if (_suctionAccumulator < SuctionInterval)
            return;
        _suctionAccumulator -= SuctionInterval;

        var padQuery = EntityQueryEnumerator<FactoryGoalPadComponent, TransformComponent>();
        while (padQuery.MoveNext(out var uid, out var padComp, out var xform))
        {
            if (padComp.AllowedAlertLevels.Count > 0)
            {
                var stationUid = _station.GetOwningStation(uid);
                if (stationUid != null && TryComp<AlertLevelComponent>(stationUid, out var alertLevel))
                {
                    if (!padComp.AllowedAlertLevels.Contains(alertLevel.CurrentLevel))
                        continue;
                }
            }

            if (!HasPoweredConsole(uid))
                continue;

            var goals = GetStationGoals(uid);
            if (goals == null) continue;

            var currentGoal = goals.CurrentGoal;
            if (currentGoal == null) continue;
            if (goals.IsGoalExpired) continue;

            if (!_protoMan.TryIndex(currentGoal.Value, out FactoryGoalPrototype? goalProto)) continue;

            var worldPos = _transform.GetWorldPosition(xform);
            var nearbyEntities = _lookup.GetEntitiesInRange(uid, SuctionRadius);

            foreach (var entity in nearbyEntities)
            {
                if (entity == uid) continue;
                if (TerminatingOrDeleted(entity)) continue;

                var itemProto = MetaData(entity).EntityPrototype?.ID;
                if (itemProto == null) continue;
                if (itemProto != goalProto.RequiredItem) continue;

                if (padComp.CurrentRequiredItem != null && itemProto != padComp.CurrentRequiredItem)
                    continue;

                if (!TryComp<PhysicsComponent>(entity, out var physics))
                    continue;

                var entityPos = _transform.GetWorldPosition(entity);
                var direction = worldPos - entityPos;
                var distance = direction.Length();

                if (distance < 0.5f)
                {
                    if (TerminatingOrDeleted(entity))
                        continue;

                    ConsumeItem(uid, goals, goalProto, entity);
                    UpdateAllUis(uid, goals);
                }
                else
                {
                    var force = direction.Normalized() * 15f;
                    _physics.SetLinearVelocity(entity, physics.LinearVelocity + force * frameTime);
                }
            }
        }
    }

    private void UpdateAllUis(EntityUid originUid, StationFactoryGoalComponent goals)
    {
        string? goalName = null;
        string? difficulty = null;
        var requiredAmount = 0;
        double remainingTime = GetRemainingTime(goals);

        var currentGoal = goals.CurrentGoal;
        if (currentGoal != null && _protoMan.TryIndex(currentGoal.Value, out var proto))
        {
            goalName = proto.Name;
            difficulty = proto.Difficulty;
            requiredAmount = proto.RequiredAmount;
        }

        var availableGoals = new List<GoalInfo>();
        foreach (var goalId in goals.AvailableGoals)
        {
            if (_protoMan.TryIndex(goalId, out FactoryGoalPrototype? goalProto))
                availableGoals.Add(new GoalInfo(goalId, goalProto.Name, goalProto.Difficulty));
            else
                availableGoals.Add(new GoalInfo(goalId, goalId, "Unknown"));
        }

        var state = new FactoryGoalUpdateState(
            currentGoal?.Id,
            goals.CurrentProgress,
            availableGoals,
            goalName,
            difficulty,
            requiredAmount,
            remainingTime);

        var stationUid = _station.GetOwningStation(originUid);
        if (stationUid != null)
        {
            var consoleQuery = EntityQueryEnumerator<FactoryGoalConsoleComponent, TransformComponent, UserInterfaceComponent>();
            while (consoleQuery.MoveNext(out var uid, out _, out var xform, out _))
            {
                if (_station.GetOwningStation(uid) != stationUid)
                    continue;

                _ui.SetUiState(uid, FactoryGoalConsoleUiKey.Key, state);
            }

            var displayQuery = EntityQueryEnumerator<FactoryGoalDisplayComponent, TransformComponent, UserInterfaceComponent>();
            while (displayQuery.MoveNext(out var uid, out _, out var xform, out _))
            {
                if (_station.GetOwningStation(uid) != stationUid)
                    continue;

                _ui.SetUiState(uid, FactoryGoalDisplayUiKey.Key, state);
            }
        }
    }
}
