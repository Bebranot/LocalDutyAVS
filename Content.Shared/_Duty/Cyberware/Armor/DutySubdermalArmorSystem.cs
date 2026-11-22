// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Random;
using Content.Shared.Administration.Logs;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Random.Helpers;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Cyberware.Armor;

/// <summary>
/// _Duty: эффекты бронепластин на носителе — резист, замедление, отражение пуль и лучей, осмотр.
/// Число пластин и замедление выставляет сервер (<c>DutySubdermalArmorPlateSystem</c>), здесь только применение.
/// </summary>
public sealed partial class DutySubdermalArmorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;

    // Буфер ключей: нельзя менять DamageDict во время обхода, а аллокации в горячем событии урона ни к чему.
    private readonly List<ProtoId<DamageTypePrototype>> _damageKeys = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutySubdermalArmorComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<DutySubdermalArmorComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<DutySubdermalArmorComponent, ProjectileReflectAttemptEvent>(OnProjectileReflect);
        SubscribeLocalEvent<DutySubdermalArmorComponent, HitScanReflectAttemptEvent>(OnHitscanReflect);
        SubscribeLocalEvent<DutySubdermalArmorComponent, ExaminedEvent>(OnExamined);
    }

    #region API

    /// <summary>Общий резист ко всему урону при текущем числе пластин (0 — брони нет).</summary>
    public static float GetResist(DutySubdermalArmorComponent comp)
    {
        if (comp.Stacks <= 0 || comp.ResistByStacks.Count == 0)
            return 0f;

        var index = Math.Min(comp.Stacks, comp.ResistByStacks.Count) - 1;
        return Math.Clamp(comp.ResistByStacks[index], 0f, 1f);
    }

    /// <summary>Множитель скорости ходьбы/бега от пластин.</summary>
    public static float GetSpeedMultiplier(DutySubdermalArmorComponent comp)
    {
        return Math.Clamp(1f - comp.SlowdownTotal, 0.1f, 1f);
    }

    /// <summary>Шанс отражения при текущем числе пластин.</summary>
    public static float GetReflectChance(DutySubdermalArmorComponent comp)
    {
        return comp.Stacks >= comp.ReflectMinStacks ? comp.ReflectChanceAtMax : 0f;
    }

    /// <summary>Перечитать скорость носителя после смены числа пластин (зовёт сервер).</summary>
    public void RefreshSpeed(EntityUid host)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(host);
    }

    #endregion

    private void OnDamageModify(Entity<DutySubdermalArmorComponent> ent, ref DamageModifyEvent args)
    {
        var resist = GetResist(ent.Comp);
        if (resist <= 0f)
            return;

        var factor = 1f - resist;

        _damageKeys.Clear();
        foreach (var type in args.Damage.DamageDict.Keys)
        {
            _damageKeys.Add(type);
        }

        foreach (var type in _damageKeys)
        {
            if (IsExcluded(ent.Comp, type))
                continue;

            var value = args.Damage.DamageDict[type];
            // Лечение (отрицательный урон) броня не режет.
            if (value <= 0)
                continue;

            args.Damage.DamageDict[type] = value * factor;
        }
    }

    private static bool IsExcluded(DutySubdermalArmorComponent comp, ProtoId<DamageTypePrototype> type)
    {
        foreach (var excluded in comp.ExcludedDamageTypes)
        {
            if (excluded == type)
                return true;
        }

        return false;
    }

    private void OnRefreshSpeed(Entity<DutySubdermalArmorComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.SlowdownTotal <= 0f)
            return;

        args.ModifySpeed(GetSpeedMultiplier(ent.Comp));
    }

    #region Reflect

    // Рандом — предсказуемый (сид от тика и сущностей), поэтому клиент и сервер бросают одинаково.

    private void OnProjectileReflect(Entity<DutySubdermalArmorComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var chance = GetReflectChance(ent.Comp);
        if (chance <= 0f)
            return;

        if (!TryComp<ReflectiveComponent>(args.ProjUid, out var reflective) || reflective.Reflective == ReflectType.None)
            return;

        if (!TryComp<PhysicsComponent>(args.ProjUid, out var physics))
            return;

        var rand = SharedRandomExtensions.PredictedRandom(_timing, GetNetEntity(ent), GetNetEntity(args.ProjUid));
        if (!rand.Prob(chance))
            return;

        // Механика как у ReflectSystem: пуля летит назад с разбросом, стрелком становится отразивший.
        var rotation = rand.NextAngle(-ent.Comp.ReflectSpread / 2, ent.Comp.ReflectSpread / 2).Opposite();
        var existingVelocity = _physics.GetMapLinearVelocity(args.ProjUid, component: physics);
        var relativeVelocity = existingVelocity - _physics.GetMapLinearVelocity(ent);
        var newVelocity = rotation.RotateVec(relativeVelocity);
        var difference = newVelocity - existingVelocity;

        _physics.SetLinearVelocity(args.ProjUid, physics.LinearVelocity + difference, body: physics);

        var newRot = rotation.RotateVec(Transform(args.ProjUid).LocalRotation.ToVec());
        _transform.SetLocalRotation(args.ProjUid, newRot.ToAngle());

        _adminLogger.Add(LogType.BulletHit, LogImpact.Medium,
            $"{ToPrettyString(ent)} reflected {ToPrettyString(args.ProjUid)} from {ToPrettyString(args.Component.Weapon)} shot by {ToPrettyString(args.Component.Shooter)} with subdermal armor");

        args.Component.Shooter = ent;
        args.Component.Weapon = ent;
        Dirty(args.ProjUid, args.Component);

        args.Cancelled = true;
        Ricochet(ent);
    }

    private void OnHitscanReflect(Entity<DutySubdermalArmorComponent> ent, ref HitScanReflectAttemptEvent args)
    {
        if (args.Reflected)
            return;

        var chance = GetReflectChance(ent.Comp);
        if (chance <= 0f || args.Reflective == ReflectType.None)
            return;

        var rand = SharedRandomExtensions.PredictedRandom(_timing, GetNetEntity(ent), GetNetEntity(args.SourceItem));
        if (!rand.Prob(chance))
            return;

        var spread = rand.NextAngle(-ent.Comp.ReflectSpread / 2, ent.Comp.ReflectSpread / 2);
        args.Direction = -spread.RotateVec(args.Direction);
        args.Reflected = true;

        _adminLogger.Add(LogType.HitScanHit, LogImpact.Medium,
            $"{ToPrettyString(ent)} reflected hitscan from {ToPrettyString(args.SourceItem)} with subdermal armor");

        Ricochet(ent);
    }

    private void Ricochet(Entity<DutySubdermalArmorComponent> ent)
    {
        // Звук и попап только с сервера, как у ванильного ReflectSystem: на предикте они бы дублировались.
        if (!_net.IsServer)
            return;

        _popup.PopupEntity(Loc.GetString("duty-armor-plate-reflect"), ent);
        _audio.PlayPvs(ent.Comp.RicochetSound, ent);
    }

    #endregion

    private void OnExamined(Entity<DutySubdermalArmorComponent> ent, ref ExaminedEvent args)
    {
        // Свои пластины осмотр самого себя покажет вкладкой «Здоровье» (кусок C); чужим — только косвенная подсказка.
        if (args.Examiner == ent.Owner || ent.Comp.Stacks < ent.Comp.ReflectMinStacks)
            return;

        args.PushMarkup(Loc.GetString("duty-armor-plate-examine-heavy"));
    }
}
