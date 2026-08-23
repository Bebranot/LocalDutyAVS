// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared._Duty.Trauma;
using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Duty.Trauma.Systems;

/// <summary>
/// _Duty: единая (серверная, авторитетная по RNG) точка решения, КАКАЯ травма выпадает
/// при попадании. Подписан на <see cref="DamageDealtEvent"/> — финальный пост-модификаторный
/// урон одного удара, ещё до его применения к дамажаблу, поэтому HP цели читается «до удара»
/// (это осознанно: добивающий удар не должен вдобавок ломать кости по пост-урон-HP).
///
/// Гейты: травмы получают только существа с <see cref="TraumaTargetComponent"/> (декларирует
/// доступные зоны) И управляемые игроком (ActorComponent) И живые. Урон ниже
/// <see cref="MinTraumaDamage"/> игнорируется — царапина не должна ломать кости.
///
/// Сама система только РЕШАЕТ и поднимает <see cref="TraumaRolledEvent"/> на цели — применение
/// конкретного эффекта (перелом/вывих/артерия) делают отдельные системы-механики. Это единая
/// точка расширения: новый тип травмы = новый обработчик события, без правок роллера.
/// </summary>
public sealed class TraumaRollSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;

    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly ProtoId<DamageTypePrototype> Slash = "Slash";
    private static readonly ProtoId<DamageTypePrototype> Piercing = "Piercing";

    // ── Тюнинг (Phase 6). Все «магические числа» роллов собраны здесь. ──────────

    /// <summary>Нижняя отсечка: урон одного удара ниже этого травму не роллит вообще.</summary>
    private const float MinTraumaDamage = 5f;

    /// <summary>Максимальный итоговый шанс любой травмы (не бывает гарантии в 100%).</summary>
    private const float MaxTraumaChance = 0.95f;

    /// <summary>Порог урона тупым, ниже которого возможен вывих (сильнее — только перелом).</summary>
    private const float DislocationMaxDamage = 15f;

    /// <summary>Нижняя граница шанса вывиха при подходящем ударе.</summary>
    private const float DislocationMinChance = 0.05f;

    /// <summary>Верхняя граница шанса вывиха при подходящем ударе.</summary>
    private const float DislocationMaxChance = 0.10f;

    /// <summary>
    /// Делитель формулы перелома: <c>урон * random(1..5) * hpFactor / scale</c>. Подобран так,
    /// чтобы перелом был событием, а не шумом: ~5% на слабый удар, приближается к капу на очень
    /// сильный. Тюнится в Phase 6.
    /// </summary>
    private const float FractureChanceScale = 400f;

    /// <summary>
    /// Делитель формулы артерии: <c>урон² / scale</c>. Квадратичная зависимость (без
    /// random-множителя, в отличие от старой формулы) выбрана осознанно: она делает броню
    /// непропорционально ценной — резист урона на X% режет шанс артерии примерно на X²%,
    /// а не линейно, как раньше. Слабые/чиркающие удары почти не роллят артерию, тяжёлые —
    /// заметно чаще. Заодно убирает "лотерейный" разброс x1..x5 на одинаковый урон.
    /// Тюнится в Phase 6.
    /// </summary>
    private const float ArterialChanceScale = 3500f;

    /// <summary>Множитель шанса перелома при нулевом HP цели (при полном HP множитель = 1).</summary>
    private const float HpFractureFactorMax = 2f;

    // ── Тестовые команды (duty*). Шансы намеренно другие, чем боевые: высокие, чтобы травму
    // можно было получить за 1-2 попытки, но НЕ 100% — иначе это была бы просто заглушка вместо
    // проверки реального пути «ролл → TraumaRolledEvent → система-механика». ──────────────────

    private const float DebugFractureChance = 0.85f;
    private const float DebugDislocationChance = 0.8f;
    private const float DebugArterialChance = 0.85f;

    public override void Initialize()
    {
        SubscribeLocalEvent<TraumaTargetComponent, DamageDealtEvent>(OnDamageDealt);
    }

    private void OnDamageDealt(Entity<TraumaTargetComponent> ent, ref DamageDealtEvent args)
    {
        // Урон «сам себе» (медицинские процедуры, провал шинирования и т.п.) травму не вызывает —
        // иначе лечение могло бы каскадом ломать новые кости. Падения (origin == null) роллятся.
        if (args.Origin == ent.Owner)
            return;

        // Только игроки — NPC/мобов без игрока не травмируем.
        if (!HasComp<ActorComponent>(ent))
            return;

        // На трупах травмы не роллим.
        if (_mobState.IsDead(ent))
            return;

        var blunt = GetPositiveDamage(args.Damage, Blunt);
        var sharp = GetPositiveDamage(args.Damage, Slash) + GetPositiveDamage(args.Damage, Piercing);

        // Тупой урон → перелом ИЛИ вывих (конкурируют за один удар).
        if (blunt >= MinTraumaDamage)
            TryRollBluntTrauma(ent, blunt);

        // Режущий/колющий урон → артериальное кровотечение (без зоны).
        if (sharp >= MinTraumaDamage)
            TryRollArterialBleed(ent, sharp);
    }

    private void TryRollBluntTrauma(Entity<TraumaTargetComponent> ent, float blunt)
    {
        if (!TryPickZone(ent.Comp, out var zone))
            return;

        // Вывих: только суставные зоны, относительно слабый удар и только по ещё не травмированной
        // зоне. По уже сломанной/вывихнутой зоне тупой удар идёт на перелом (эскалацию), а не на вывих.
        if (BodyZoneCategory.IsJoint(zone)
            && blunt < DislocationMaxDamage
            && !IsFractured(ent.Owner, zone)
            && !IsDislocated(ent.Owner, zone))
        {
            var dislocationChance = _random.NextFloat(DislocationMinChance, DislocationMaxChance);
            if (_random.Prob(dislocationChance))
            {
                RaiseTrauma(ent, TraumaType.Dislocation, zone);
                return;
            }
        }

        var fractureChance = GetFractureChance(ent, blunt);
        if (_random.Prob(fractureChance))
            RaiseTrauma(ent, TraumaType.Fracture, zone);
    }

    private void TryRollArterialBleed(Entity<TraumaTargetComponent> ent, float sharp)
    {
        // Шанс = урон² / scale — см. ArterialChanceScale: квадрат вместо random(1..5)*линейности,
        // чтобы броня резала шанс непропорционально сильнее урона, а сам ролл не был лотереей.
        var chance = Math.Clamp(sharp * sharp / ArterialChanceScale, 0f, MaxTraumaChance);
        if (_random.Prob(chance))
            RaiseTrauma(ent, TraumaType.ArterialBleed, null);
    }

    /// <summary>
    /// Шанс перелома: <c>урон * random(1..5) * hpFactor / scale</c>, где hpFactor растёт
    /// по мере падения HP цели. Итог клампится, чтобы не превышать общий кап травм.
    /// </summary>
    private float GetFractureChance(EntityUid uid, float blunt)
    {
        var roll = _random.Next(1, 6); // 1..5 включительно
        var chance = blunt * roll * GetHpFractureFactor(uid) / FractureChanceScale;
        return Math.Clamp(chance, 0f, MaxTraumaChance);
    }

    /// <summary>
    /// Множитель «чем меньше HP — тем выше шанс перелома»: 1.0 при полном HP,
    /// растёт до <see cref="HpFractureFactorMax"/> у порога крита.
    /// </summary>
    private float GetHpFractureFactor(EntityUid uid)
    {
        if (!_mobThreshold.TryGetIncapThreshold(uid, out var threshold) || threshold.Value <= 0)
            return 1f;

        // GetTotalDamage помечен Obsolete (движок уводит от «числового» урона к локальной
        // модели ран), но пороги MobState всё ещё числовые, и формула травм осознанно
        // опирается на долю HP до крита — как это делает HealthPhrasesSystem.
#pragma warning disable CS0618
        var totalDamage = _damageable.GetTotalDamage(uid);
#pragma warning restore CS0618

        var hpFraction = Math.Clamp(1f - (totalDamage / threshold.Value).Float(), 0f, 1f);
        return 1f + (HpFractureFactorMax - 1f) * (1f - hpFraction);
    }

    /// <summary>Взвешенный выбор зоны из доступных существу. false — если зон нет.</summary>
    private bool TryPickZone(TraumaTargetComponent comp, out BodyZone zone)
    {
        zone = default;
        if (comp.AvailableZones.Count == 0)
            return false;

        var total = 0f;
        foreach (var candidate in comp.AvailableZones)
            total += GetZoneWeight(candidate);

        var pick = _random.NextFloat(0f, total);
        foreach (var candidate in comp.AvailableZones)
        {
            pick -= GetZoneWeight(candidate);
            if (pick > 0f)
                continue;

            zone = candidate;
            return true;
        }

        // Числовой хвост float-погрешности — вернём любую доступную зону.
        foreach (var candidate in comp.AvailableZones)
        {
            zone = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Относительный вес зоны при роллах перелома/вывиха от тупого удара.
    /// Конечности — самые вероятные, торс средне, голова редко (Phase 6 — тюнинг).
    /// </summary>
    private static float GetZoneWeight(BodyZone zone) => zone switch
    {
        BodyZone.LeftArm or BodyZone.RightArm or BodyZone.LeftLeg or BodyZone.RightLeg => 1f,
        BodyZone.Torso => 0.5f,
        BodyZone.Head => 0.2f,
        _ => 1f,
    };

    private bool IsFractured(EntityUid uid, BodyZone zone) =>
        TryComp<FractureComponent>(uid, out var fracture) && fracture.Zones.ContainsKey(zone);

    private bool IsDislocated(EntityUid uid, BodyZone zone) =>
        TryComp<DislocationComponent>(uid, out var dislocation) && dislocation.Dislocated.Contains(zone);

    private static float GetPositiveDamage(DamageSpecifier damage, ProtoId<DamageTypePrototype> type)
    {
        if (!damage.DamageDict.TryGetValue(type, out var value) || value <= FixedPoint2.Zero)
            return 0f;

        return value.Float();
    }

    /// <summary>Публикует выпавшую травму на цели — дальше её накладывают системы-механики.</summary>
    private void RaiseTrauma(EntityUid uid, TraumaType type, BodyZone? zone)
    {
        // Широковещательно: на это событие подписаны несколько систем-механик, а движок допускает
        // лишь одну подписку на пару (компонент, событие).
        RaiseLocalEvent(new TraumaRolledEvent(uid, type, zone));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Debug API для консольных команд duty* — форсирует реальный ролл через тот же
    // TraumaRolledEvent, что и настоящий удар, а не просто вешает компонент напрямую.
    // Шанс намеренно высокий, но не гарантированный (см. Debug*Chance выше).
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Форсирует ролл перелома. Зона — указанная, или взвешенно случайная, если null.</summary>
    public TraumaDebugRollResult DebugForceFracture(EntityUid uid, BodyZone? zone = null)
    {
        if (!TryComp<TraumaTargetComponent>(uid, out var comp) || !ResolveDebugZone(comp, zone, out var picked))
            return TraumaDebugRollResult.NoTarget;

        var success = _random.Prob(DebugFractureChance);
        if (success)
            RaiseTrauma(uid, TraumaType.Fracture, picked);

        return new TraumaDebugRollResult(true, success, picked, DebugFractureChance);
    }

    /// <summary>
    /// Форсирует ролл вывиха. Зона обязана быть суставной (рука/нога) — указанная явно, или
    /// случайная среди доступных существу суставов, если null. Как и в реальном ролле, зона не
    /// должна быть уже сломана — иначе получилось бы состояние (перелом+вывих одной зоны),
    /// недостижимое от настоящего удара (см. конкуренцию в <see cref="TryRollBluntTrauma"/>).
    /// </summary>
    public TraumaDebugRollResult DebugForceDislocation(EntityUid uid, BodyZone? zone = null)
    {
        if (!TryComp<TraumaTargetComponent>(uid, out var comp))
            return TraumaDebugRollResult.NoTarget;

        BodyZone picked;
        if (zone is { } requested)
        {
            if (!BodyZoneCategory.IsJoint(requested)
                || !comp.AvailableZones.Contains(requested)
                || IsFractured(uid, requested))
            {
                return TraumaDebugRollResult.NoTarget;
            }

            picked = requested;
        }
        else
        {
            var joints = comp.AvailableZones.Where(z => BodyZoneCategory.IsJoint(z) && !IsFractured(uid, z)).ToList();
            if (joints.Count == 0)
                return TraumaDebugRollResult.NoTarget;
            picked = joints[_random.Next(joints.Count)];
        }

        var success = _random.Prob(DebugDislocationChance);
        if (success)
            RaiseTrauma(uid, TraumaType.Dislocation, picked);

        return new TraumaDebugRollResult(true, success, picked, DebugDislocationChance);
    }

    /// <summary>Форсирует ролл артериального кровотечения (беззонное).</summary>
    public TraumaDebugRollResult DebugForceArterial(EntityUid uid)
    {
        if (!HasComp<TraumaTargetComponent>(uid))
            return TraumaDebugRollResult.NoTarget;

        var success = _random.Prob(DebugArterialChance);
        if (success)
            RaiseTrauma(uid, TraumaType.ArterialBleed, null);

        return new TraumaDebugRollResult(true, success, null, DebugArterialChance);
    }

    /// <summary>
    /// Зона для debug-ролла: явно запрошенная (если доступна существу), иначе — обычный
    /// взвешенный выбор <see cref="TryPickZone"/> (та же дистрибуция, что в реальном бою).
    /// </summary>
    private bool ResolveDebugZone(TraumaTargetComponent comp, BodyZone? zone, out BodyZone picked)
    {
        if (zone is { } requested)
        {
            picked = requested;
            return comp.AvailableZones.Contains(requested);
        }

        return TryPickZone(comp, out picked);
    }
}

/// <summary>
/// _Duty (debug): результат форсированного ролла для консольных команд duty*.
/// <see cref="TargetValid"/> — существу вообще можно форсировать эту травму (есть
/// <see cref="TraumaTargetComponent"/>, зона подходит и т.п.); <see cref="Success"/> — сработал ли
/// сам вероятностный ролл (это НЕ гарантия — см. Debug*Chance в <see cref="TraumaRollSystem"/>).
/// </summary>
public readonly record struct TraumaDebugRollResult(bool TargetValid, bool Success, BodyZone? Zone, float Chance)
{
    public static readonly TraumaDebugRollResult NoTarget = new(false, false, null, 0f);
}
