using Content.Shared._Duty.Weapons.Module;
using Content.Shared._Duty.Weapons.Module.Effects;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;

namespace Content.Server._Duty.Weapons.Module;

// Порт из STALKER-14 (Фаза 1 DutyAVS, без зум-скоупинга).
// Модули в слотах ствола (gun_module_*, gun_auto_sear) меняют статы Gun и доступные режимы огня.
// Сами модификаторы накладывает общий STSharedWeaponModuleSystem; здесь — только авторитетный пересчёт кэша.
public sealed partial class STWeaponModuleSystem : STSharedWeaponModuleSystem
{
    [Dependency] private SharedGunSystem _gun = default!;

    private EntityQuery<ContainerManagerComponent> _containerMangerQuery;
    private EntityQuery<STWeaponModuleContainerComponent> _containerModuleQuery;
    private EntityQuery<STWeaponModuleComponent> _moduleQuery;

    public override void Initialize()
    {
        base.Initialize();

        _containerMangerQuery = GetEntityQuery<ContainerManagerComponent>();
        _containerModuleQuery = GetEntityQuery<STWeaponModuleContainerComponent>();
        _moduleQuery = GetEntityQuery<STWeaponModuleComponent>();

        SubscribeLocalEvent<STWeaponModuleComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<STWeaponModuleComponent, EntGotRemovedFromContainerMessage>(OnRemoved);

        SubscribeLocalEvent<STWeaponModuleContainerComponent, ComponentInit>(OnInit);
    }

    private void OnInserted(Entity<STWeaponModuleComponent> entity, ref EntGotInsertedIntoContainerMessage args)
    {
        UpdateContainerEffect(args.Container.Owner);
    }

    private void OnRemoved(Entity<STWeaponModuleComponent> entity, ref EntGotRemovedFromContainerMessage args)
    {
        UpdateContainerEffect(args.Container.Owner);
    }

    private void OnInit(Entity<STWeaponModuleContainerComponent> entity, ref ComponentInit args)
    {
        entity.Comp.CachedEffect = new STWeaponModuleEffect();

        if (TryComp<GunComponent>(entity, out var gun) && gun.SoundGunshot != null)
            entity.Comp.BaseSoundGunshotVolume = gun.SoundGunshot.Params.Volume;

        UpdateContainerEffect(entity);
    }

    private void UpdateContainerEffect(EntityUid uid)
    {
        if (!_containerModuleQuery.TryGetComponent(uid, out var containerComponent))
            return;

        UpdateContainerEffect((uid, containerComponent));
    }

    /// <summary>
    /// Эффект — по модулям во ВСЕХ слотах ствола. Раньше пересчёт брал только слот, в котором
    /// что-то поменялось, и затирал им кэш: поставил глушитель — пропал эффект прицела.
    /// </summary>
    private void UpdateContainerEffect(Entity<STWeaponModuleContainerComponent> entity)
    {
        var effect = new STWeaponModuleEffect();

        if (_containerMangerQuery.TryGetComponent(entity, out var manager))
        {
            foreach (var container in manager.Containers.Values)
            {
                foreach (var containedEntity in container.ContainedEntities)
                {
                    if (_moduleQuery.TryGetComponent(containedEntity, out var moduleComponent))
                        effect = STWeaponModuleEffect.Merge(effect, moduleComponent.Effect);
                }
            }
        }

        var modeDelta = effect.AdditionalAvailableModes ^ entity.Comp.CachedEffect.AdditionalAvailableModes;

        entity.Comp.CachedEffect = effect;
        Dirty(entity);

        if (!TryComp<GunComponent>(entity, out var gun))
            return;

        // Битовая маска работает как переключатель: тогглим режимы, что изменились (напр. авто-шептало → FullAuto).
        _gun.SetAvailableModes(entity.Owner, gun.AvailableModes ^ modeDelta, gun);

        _gun.RefreshModifiers((entity.Owner, gun));
    }
}
