using Content.Shared._Duty.Weapon.Module;
using Content.Shared._Duty.Weapon.Module.Effects;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;

namespace Content.Server._Duty.Weapon.Module;

// Порт из STALKER-14 (Фаза 1 DutyAVS, без зум-скоупинга).
// Модули в слотах ствола (gun_module_*, gun_auto_sear) меняют статы Gun и доступные режимы огня.
public sealed class STWeaponModuleSystem : STSharedWeaponModuleSystem
{
    [Dependency] private readonly SharedGunSystem _gun = default!;

    private EntityQuery<ContainerManagerComponent> _containerMangerQuery;
    private EntityQuery<STWeaponModuleContainerComponent> _containerModuleQuery;

    public override void Initialize()
    {
        base.Initialize();

        _containerMangerQuery = GetEntityQuery<ContainerManagerComponent>();
        _containerModuleQuery = GetEntityQuery<STWeaponModuleContainerComponent>();

        SubscribeLocalEvent<STWeaponModuleComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<STWeaponModuleComponent, EntGotRemovedFromContainerMessage>(OnRemoved);

        SubscribeLocalEvent<STWeaponModuleContainerComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<STWeaponModuleContainerComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
    }

    private void OnInserted(Entity<STWeaponModuleComponent> entity, ref EntGotInsertedIntoContainerMessage args)
    {
        UpdateContainerEffect(args.Container);
    }

    private void OnRemoved(Entity<STWeaponModuleComponent> entity, ref EntGotRemovedFromContainerMessage args)
    {
        UpdateContainerEffect(args.Container);
    }

    private void OnInit(Entity<STWeaponModuleContainerComponent> entity, ref ComponentInit args)
    {
        entity.Comp.CachedEffect = new STWeaponModuleEffect();

        if (TryComp<GunComponent>(entity, out var gun) && gun.SoundGunshot != null)
            entity.Comp.BaseSoundGunshotVolume = gun.SoundGunshot.Params.Volume;

        if (!_containerMangerQuery.TryGetComponent(entity, out var containerComponent))
            return;

        foreach (var (_, container) in containerComponent.Containers)
        {
            UpdateContainerEffect(entity, container);
        }
    }

    private void OnGunRefreshModifiers(Entity<STWeaponModuleContainerComponent> entity, ref GunRefreshModifiersEvent args)
    {
        var effect = entity.Comp.CachedEffect;

        args.FireRate *= effect.FireRateModifier;
        args.AngleDecay *= effect.AngleDecayModifier;
        args.AngleIncrease *= effect.AngleIncreaseModifier;
        args.MinAngle *= effect.MinAngleModifier;
        args.MaxAngle *= effect.MaxAngleModifier;
        args.ProjectileSpeed *= effect.ProjectileSpeedModifier;

        if (args.SoundGunshot is null)
            return;

        // Устанавливаем громкость от сохранённой базовой, а не накапливаем.
        args.SoundGunshot.Params = args.SoundGunshot.Params
            .WithVolume(entity.Comp.BaseSoundGunshotVolume + effect.SoundGunshotVolumeAddition);
    }

    private void UpdateContainerEffect(BaseContainer container)
    {
        UpdateContainerEffect(container.Owner, container);
    }

    private void UpdateContainerEffect(EntityUid entityUid, BaseContainer container)
    {
        if (!_containerModuleQuery.TryGetComponent(entityUid, out var containerComponent))
            return;

        UpdateContainerEffect((entityUid, containerComponent), container);
    }

    private void UpdateContainerEffect(Entity<STWeaponModuleContainerComponent> entity, BaseContainer container)
    {
        var effect = new STWeaponModuleEffect();

        foreach (var containedEntity in container.ContainedEntities)
        {
            if (!TryComp<STWeaponModuleComponent>(containedEntity, out var moduleComponent))
                continue;

            effect = STWeaponModuleEffect.Merge(effect, moduleComponent.Effect);
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
