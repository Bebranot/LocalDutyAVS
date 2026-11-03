using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared._Duty.Weapons.Module;

/// <summary>
/// Общая часть модулей оружия. Модификаторы накладываются на обеих сторонах: клиент сам зовёт
/// пересчёт модификаторов оружия (прицеливание, вилд), и серверный обработчик он не видел —
/// предсказывал выстрелы по статам ствола без модулей. Кэш эффекта считает сервер и шлёт по сети.
/// </summary>
public abstract class STSharedWeaponModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<STWeaponModuleContainerComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
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
}
