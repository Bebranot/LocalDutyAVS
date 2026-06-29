using Content.Shared.ADT.Traits.Assorted;
using Content.Shared.Alert;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.Network;

namespace Content.Shared._Duty.Weapons.MoraleBuff;

/// <summary>
/// _Duty: жизненный цикл баффа морали (<see cref="MoraleBuffComponent"/>).
/// Живёт в Shared, чтобы предсказание скорости на клиенте совпадало с сервером.
/// Подписки на ComponentStartup/Shutdown держим в ОДНОЙ системе (RobustToolbox запрещает
/// два обработчика одного directed-события на компонент). Серверные операции (игнор боли,
/// алерт) спрятаны под <c>_net.IsServer</c>. Наложение/снятие по таймеру — в серверном
/// <c>MoraleBuffSystem</c>.
/// </summary>
public sealed class SharedMoraleBuffSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MoraleBuffComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MoraleBuffComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MoraleBuffComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<MoraleBuffComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
    }

    private void OnStartup(Entity<MoraleBuffComponent> ent, ref ComponentStartup args)
    {
        _movement.RefreshMovementSpeedModifiers(ent);

        if (!_net.IsServer)
            return;

        // Игнор боли: переиспользуем существующий PainNumbnessStatusEffectComponent —
        // его проверяют клиентские системы (красная виньетка + статус ХП справа).
        // Снимаем при спадении только если навесили сами (не трогаем трейт/другой источник).
        if (!HasComp<PainNumbnessStatusEffectComponent>(ent))
        {
            AddComp<PainNumbnessStatusEffectComponent>(ent);
            ent.Comp.AddedPainNumbness = true;
        }
    }

    private void OnShutdown(Entity<MoraleBuffComponent> ent, ref ComponentShutdown args)
    {
        // Пересчитываем скорость, чтобы убрать наш множитель когда бафф спал.
        _movement.RefreshMovementSpeedModifiers(ent);

        if (!_net.IsServer)
            return;

        if (ent.Comp.AddedPainNumbness)
            RemComp<PainNumbnessStatusEffectComponent>(ent);

        _alerts.ClearAlert(ent.Owner, ent.Comp.Alert);
    }

    private void OnRefreshSpeed(Entity<MoraleBuffComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.SpeedModifier, ent.Comp.SpeedModifier);
    }

    private void OnBeforeDamage(Entity<MoraleBuffComponent> ent, ref BeforeDamageChangedEvent args)
    {
        // Режем весь входящий урон на DamageResist (как резист рывка алебарды).
        args.Damage *= (FixedPoint2) (1f - ent.Comp.DamageResist);
    }
}
