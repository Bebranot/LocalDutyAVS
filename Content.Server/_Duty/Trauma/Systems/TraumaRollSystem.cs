// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

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
    /// Делитель формулы перелома: <c>урон * random(1..10) * hpFactor / scale</c>. Подобран так,
    /// чтобы перелом был событием, а не шумом: ~5% на слабый удар, приближается к капу на очень
    /// сильный. Тюнится в Phase 6.
    /// </summary>
    private const float FractureChanceScale = 400f;

    /// <summary>
    /// Делитель формулы артерии: <c>урон * random(1..10) / scale</c>. Артерия должна быть
    /// редкой даже на сильном режущем ударе. Тюнится в Phase 6.
    /// </summary>
    private const float ArterialChanceScale = 350f;

    /// <summary>Множитель шанса перелома при нулевом HP цели (при полном HP множитель = 1).</summary>
    private const float HpFractureFactorMax = 2f;

    public override void Initialize()
    {
        SubscribeLocalEvent<TraumaTargetComponent, DamageDealtEvent>(OnDamageDealt);
    }

    private void OnDamageDealt(Entity<TraumaTargetComponent> ent, ref DamageDealtEvent args)
    {
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

        // Вывих: только суставные зоны и только относительно слабый удар.
        // Если вывих выпал — перелома по этому удару уже не будет (конкуренция).
        if (BodyZoneCategory.IsJoint(zone) && blunt < DislocationMaxDamage)
        {
            var dislocationChance = _random.NextFloat(DislocationMinChance, DislocationMaxChance);
            if (_random.Prob(dislocationChance))
            {
                RaiseTrauma(ent, TraumaType.Dislocation, zone, blunt);
                return;
            }
        }

        var fractureChance = GetFractureChance(ent, blunt);
        if (_random.Prob(fractureChance))
            RaiseTrauma(ent, TraumaType.Fracture, zone, blunt);
    }

    private void TryRollArterialBleed(Entity<TraumaTargetComponent> ent, float sharp)
    {
        // Шанс = урон * random(1..10), редкий и не гарантированный даже на большом уроне.
        var chance = Math.Clamp(sharp * _random.Next(1, 11) / ArterialChanceScale, 0f, MaxTraumaChance);
        if (_random.Prob(chance))
            RaiseTrauma(ent, TraumaType.ArterialBleed, null, sharp);
    }

    /// <summary>
    /// Шанс перелома: <c>урон * random(1..10) * hpFactor / scale</c>, где hpFactor растёт
    /// по мере падения HP цели. Итог клампится, чтобы не превышать общий кап травм.
    /// </summary>
    private float GetFractureChance(EntityUid uid, float blunt)
    {
        var roll = _random.Next(1, 11); // 1..10 включительно
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

    private static float GetPositiveDamage(DamageSpecifier damage, ProtoId<DamageTypePrototype> type)
    {
        if (!damage.DamageDict.TryGetValue(type, out var value) || value <= FixedPoint2.Zero)
            return 0f;

        return value.Float();
    }

    /// <summary>
    /// Публикует выпавшую травму на цели. <paramref name="damage"/> — величина урона удара,
    /// вызвавшего травму (используется механиками, например для тира перелома).
    /// </summary>
    private void RaiseTrauma(EntityUid uid, TraumaType type, BodyZone? zone, float damage)
    {
        var ev = new TraumaRolledEvent(type, zone, damage);
        RaiseLocalEvent(uid, ref ev);
    }
}
