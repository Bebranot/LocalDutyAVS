// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Armor;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Ballistics;

/// <summary>
/// _Duty: бронепробитие пуль и передача баллистических бонусов травм.
///
/// Пробитие — поправка ПОСЛЕ брони, а не «раздувание» урона до неё (как у ADT в ближнем бою):
/// щиты списывают свою прочность от <c>DamageModifyEvent.OriginalDamage</c>, и завышенный заранее урон
/// ломал бы их в разы быстрее. Здесь же исходный урон не трогается, а после того как броня в
/// инвентаре отработала, урон каждого типа домножается на <c>c'/c</c> — итог равен урону через
/// «ослабленную» броню c'. Для цепочки чисто мультипликативных модификаторов это точно.
///
/// Контекст попадания кладёт серверная система в момент ProjectileHitEvent (см. Content.Server),
/// здесь он забирается синхронно внутри того же TryChangeDamage.
/// </summary>
public sealed partial class DutyBallisticsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _factory = default!;

    /// <summary>Пробитие меньше этого по модулю считается нулевым (и не показывается при осмотре).</summary>
    private const float PenetrationEpsilon = 0.01f;

    public override void Initialize()
    {
        base.Initialize();

        // after InventorySystem: поправка должна видеть урон, который броня в слотах уже срезала.
        SubscribeLocalEvent<DutyBallisticTargetComponent, DamageModifyEvent>(OnDamageModify,
            after: new[] { typeof(InventorySystem) });
        SubscribeLocalEvent<DutyBallisticsExamineComponent, ExaminedEvent>(OnExamine);
    }

    /// <summary>
    /// Эффективный коэффициент брони против пули с пробитием <paramref name="penetration"/>.
    /// Коэффициенты вне (0; 1) не трогаются: полный иммунитет пулей не пробивается,
    /// а «уязвимость» (c ≥ 1) не броня.
    /// </summary>
    public static float EffectiveCoefficient(float coefficient, float penetration)
    {
        if (coefficient <= 0f || coefficient >= 1f)
            return coefficient;

        var p = Math.Clamp(penetration, -1f, 1f);
        return Math.Clamp(coefficient + p * (1f - coefficient), 0f, 1f);
    }

    /// <summary>
    /// Запомнить на цели контекст попадания пули. Вызывается сервером непосредственно перед
    /// TryChangeDamage этой пули; без <see cref="DutyBallisticTargetComponent"/> ничего не делает.
    /// </summary>
    public void SetPendingHit(EntityUid target, EntityUid? shooter, float penetration, BallisticTraumaContext trauma)
    {
        if (!TryComp<DutyBallisticTargetComponent>(target, out var comp))
            return;

        var tick = _timing.CurTick;

        comp.HasPendingPenetration = MathF.Abs(penetration) >= PenetrationEpsilon;
        comp.PendingPenetration = penetration;
        comp.PendingShooter = shooter;
        comp.PendingPenetrationTick = tick;

        comp.HasPendingTrauma = trauma.ArteryBonus > 0f || trauma.FractureChance > 0f;
        comp.PendingTrauma = trauma;
        comp.PendingTraumaTick = tick;
    }

    /// <summary>
    /// Забрать бонусы травм текущего попадания. Контекст стирается при любом исходе —
    /// устаревший (из прошлого тика, когда урон отменили до роллера) не применяется.
    /// </summary>
    public bool TryConsumeTrauma(EntityUid target, out BallisticTraumaContext trauma)
    {
        trauma = default;

        if (!TryComp<DutyBallisticTargetComponent>(target, out var comp) || !comp.HasPendingTrauma)
            return false;

        comp.HasPendingTrauma = false;
        if (comp.PendingTraumaTick != _timing.CurTick)
            return false;

        trauma = comp.PendingTrauma;
        return true;
    }

    private void OnDamageModify(EntityUid uid, DutyBallisticTargetComponent comp, DamageModifyEvent args)
    {
        if (!comp.HasPendingPenetration)
            return;

        comp.HasPendingPenetration = false;

        // Контекст чужого удара (другой тик или другой стрелок) не применяем.
        if (comp.PendingPenetrationTick != _timing.CurTick || comp.PendingShooter != args.Origin)
            return;

        if (!TryComp<InventoryComponent>(uid, out var inventory))
            return;

        // Тот же суммарный коэффициент, что броня только что применила (включая аксессуары ADT).
        var query = new CoefficientQueryEvent(~SlotFlags.POCKET);
        _inventory.RelayEvent((uid, inventory), query);

        DamageModifierSet? correction = null;
        foreach (var (type, coefficient) in query.DamageModifiers.Coefficients)
        {
            if (coefficient <= 0f || coefficient >= 1f)
                continue;

            var effective = EffectiveCoefficient(coefficient, comp.PendingPenetration);
            correction ??= new DamageModifierSet();
            correction.Coefficients[type] = effective / coefficient;
        }

        if (correction != null)
            args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage, correction);
    }

    private void OnExamine(Entity<DutyBallisticsExamineComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!TryComp<CartridgeAmmoComponent>(ent, out var cartridge) || cartridge.Spent)
            return;

        if (!_proto.TryIndex(cartridge.Prototype, out var bulletProto)
            || !bulletProto.TryGetComponent<DutyBallisticsComponent>(out var ballistics, _factory))
            return;

        if (ballistics.ArmorPenetration >= PenetrationEpsilon)
        {
            var percent = (int) MathF.Round(ballistics.ArmorPenetration * 100f);
            args.PushMarkup(Loc.GetString("duty-ballistics-examine-penetration", ("percent", percent)));
        }
        else if (ballistics.ArmorPenetration <= -PenetrationEpsilon)
        {
            args.PushMarkup(Loc.GetString("duty-ballistics-examine-expansive"));
        }

        // Точные проценты травм не раскрываем — только качественную подсказку.
        if (ballistics.FractureChance > 0f)
            args.PushMarkup(Loc.GetString("duty-ballistics-examine-heavy"));
        else if (ballistics.ArteryBonus > 0f)
            args.PushMarkup(Loc.GetString("duty-ballistics-examine-bleeding"));
    }
}
