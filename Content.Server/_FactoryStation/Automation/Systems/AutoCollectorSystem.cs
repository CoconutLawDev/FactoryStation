using System.Collections.Generic;
using System.Linq;
using Content.Server.Materials;
using Content.Shared.Automation;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Content.Shared.Whitelist;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Automation;

public sealed partial class AutoCollectorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private MaterialStorageSystem _materialStorageSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookup = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextCheck = new();
    private readonly HashSet<Entity<TransformComponent>> _entitySet = new();
    private readonly Dictionary<EntityUid, HashSet<string>> _requiredMaterialsCache = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<AutoCollectorComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<AutoCollectorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<AutoCollectorComponent, ComponentRemove>(OnRemove);
    }

    private void OnStartup(EntityUid uid, AutoCollectorComponent component, ComponentStartup args)
    {
        _nextCheck[uid] = _timing.CurTime + TimeSpan.FromSeconds(component.Interval);
    }

    private void OnShutdown(EntityUid uid, AutoCollectorComponent component, ComponentShutdown args)
    {
        _nextCheck.Remove(uid);
        _requiredMaterialsCache.Remove(uid);
    }

    private void OnRemove(EntityUid uid, AutoCollectorComponent component, ComponentRemove args)
    {
        _nextCheck.Remove(uid);
        _requiredMaterialsCache.Remove(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var curTime = _timing.CurTime;

        foreach (var (uid, next) in _nextCheck.ToArray())
        {
            if (!TryComp<AutoCollectorComponent>(uid, out var collector) || collector.Deleted)
            {
                _nextCheck.Remove(uid);
                _requiredMaterialsCache.Remove(uid);
                continue;
            }

            TryCollectItems(uid, collector);

            if (curTime >= next)
            {
                _nextCheck[uid] = curTime + TimeSpan.FromSeconds(collector.Interval);
                TryInsertItems(uid, collector);
            }
        }
    }

    private void TryCollectItems(EntityUid uid, AutoCollectorComponent collector)
    {
        if (!TryComp<MaterialStorageComponent>(uid, out var storage))
            return;

        var requiredMaterials = GetRequiredMaterials(uid);

        var mapCoords = _transformSystem.GetMapCoordinates(uid);
        _entitySet.Clear();
        _entityLookup.GetEntitiesInRange(mapCoords, collector.CollectionRadius, _entitySet, LookupFlags.Dynamic);

        foreach (var entity in _entitySet)
        {
            if (entity.Owner == uid) continue;
            if (!HasComp<PhysicsComponent>(entity.Owner)) continue;
            if (TerminatingOrDeleted(entity.Owner)) continue;

            if (!IsWhitelisted(entity.Owner, storage))
                continue;

            if (!IsRequiredByRecipes(entity.Owner, requiredMaterials))
                continue;

            var stationPos = _transformSystem.GetWorldPosition(uid);
            var itemPos = _transformSystem.GetWorldPosition(entity.Owner);
            var direction = stationPos - itemPos;
            var distance = direction.Length();

            if (distance <= 0.01f)
                continue;

            direction = direction.Normalized();

            if (TryComp<PhysicsComponent>(entity.Owner, out var physics))
            {
                var forceMultiplier = Math.Clamp(distance / collector.CollectionRadius, 0.2f, 1f);
                var pullForce = collector.PullForce * forceMultiplier;
                _physicsSystem.ApplyLinearImpulse(entity.Owner, direction * pullForce, body: physics);

                if (physics.LinearVelocity.Length() > 2f)
                {
                    _physicsSystem.SetLinearVelocity(entity.Owner, physics.LinearVelocity * 0.9f, body: physics);
                }
            }
        }
    }

    private void TryInsertItems(EntityUid uid, AutoCollectorComponent collector)
    {
        if (!TryComp<MaterialStorageComponent>(uid, out var storage))
            return;

        var requiredMaterials = GetRequiredMaterials(uid);

        var mapCoords = _transformSystem.GetMapCoordinates(uid);
        _entitySet.Clear();
        _entityLookup.GetEntitiesInRange(mapCoords, 0.3f, _entitySet, LookupFlags.Dynamic);

        foreach (var entity in _entitySet)
        {
            if (entity.Owner == uid) continue;
            if (TerminatingOrDeleted(entity.Owner)) continue;
            if (!HasComp<PhysicsComponent>(entity.Owner)) continue;

            if (!IsWhitelisted(entity.Owner, storage))
                continue;

            if (!IsRequiredByRecipes(entity.Owner, requiredMaterials))
                continue;

            if (_materialStorageSystem.TryInsertMaterialEntity(uid, entity.Owner, uid, storage))
            {
                // Успешно вставлено — предмет удалён внутри TryInsertMaterialEntity
            }
        }
    }

    private bool IsWhitelisted(EntityUid item, MaterialStorageComponent storage)
    {
        if (storage.Whitelist == null)
            return true;

        return _whitelistSystem.IsWhitelistPass(storage.Whitelist, item);
    }

    private HashSet<string> GetRequiredMaterials(EntityUid uid)
    {
        if (_requiredMaterialsCache.TryGetValue(uid, out var cached))
            return cached;

        var materials = new HashSet<string>();

        if (TryComp<LatheComponent>(uid, out var lathe))
        {
            foreach (var packId in lathe.StaticPacks.Concat(lathe.DynamicPacks))
            {
                if (!_prototypeManager.TryIndex<LatheRecipePackPrototype>(packId, out var pack))
                    continue;

                foreach (var recipeId in pack.Recipes)
                {
                    if (!_prototypeManager.TryIndex<LatheRecipePrototype>(recipeId, out var recipe))
                        continue;

                    foreach (var (materialId, _) in recipe.Materials)
                    {
                        materials.Add(materialId);
                    }
                }
            }
        }

        _requiredMaterialsCache[uid] = materials;
        return materials;
    }

    private bool IsRequiredByRecipes(EntityUid item, HashSet<string> requiredMaterials)
    {
        if (requiredMaterials.Count == 0)
            return true;

        if (!TryComp<PhysicalCompositionComponent>(item, out var composition))
            return false;

        foreach (var (materialId, _) in composition.MaterialComposition)
        {
            if (requiredMaterials.Contains(materialId))
                return true;
        }

        return false;
    }
}
