// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Chat.Managers;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Syndicate;
using Content.Shared.ADT.Trail;
using Content.Shared.Chat;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Cyberware.Syndicate;

/// <summary>
/// _Duty: «Керензиков» — рефлекторный разгон, который включается сам, без кнопки: от полученного урона или от
/// чужой пули, пролетевшей рядом. 8 с носитель быстрее ходит, бьёт и стреляет (треть мощи Сандевистана),
/// затем 20 с контур остывает. Каждая фаза пишется носителю в чат, чтобы было понятно, что произошло.
/// </summary>
public sealed partial class DutyKerenzikovServerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    // Опасность рядом ищем часто (пуля пролетает 2.5 тайла за доли секунды), но носителей единицы — это дёшево.
    private const float DangerScanInterval = 0.1f;
    private float _accumulator;

    private static readonly Color TrailColor = Color.FromHex("#E8C25A");
    private static readonly Color ChatColor = Color.FromHex("#E8C25A");

    private readonly HashSet<Entity<ProjectileComponent>> _projectiles = new();

    private EntityQuery<GunComponent> _gunQuery;

    public override void Initialize()
    {
        base.Initialize();

        _gunQuery = GetEntityQuery<GunComponent>();

        SubscribeLocalEvent<DutyKerenzikovHostComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<DutyKerenzikovHostComponent, MeleeAttackEvent>(OnMeleeAttack);
        SubscribeLocalEvent<DutyKerenzikovHostComponent, DidEquipHandEvent>(OnEquipHand);
        SubscribeLocalEvent<DutyKerenzikovHostComponent, DidUnequipHandEvent>(OnUnequipHand);
        SubscribeLocalEvent<DutyKerenzikovHostComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<DutyKerenzikovHostComponent, ComponentShutdown>(OnShutdown);
    }

    #region Срабатывание

    private void OnDamaged(Entity<DutyKerenzikovHostComponent> ent, ref DamageChangedEvent args)
    {
        // Самоповреждение (голод, яд в крови, Сандевистан) — не угроза, на которую дёргаются рефлексы.
        if (!args.DamageIncreased || args.Origin == ent.Owner)
            return;

        TryActivate(ent, "duty-kerenzikov-chat-trigger-damage");
    }

    private bool IsDangerNear(Entity<DutyKerenzikovHostComponent> ent)
    {
        _projectiles.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.DangerRadius, _projectiles);

        foreach (var projectile in _projectiles)
        {
            // Своя пуля, упавшая гильза и потраченный снаряд опасности не несут.
            if (projectile.Comp.ProjectileSpent || projectile.Comp.Shooter == ent.Owner || projectile.Comp.Weapon == null)
                continue;

            return true;
        }

        return false;
    }

    private void TryActivate(Entity<DutyKerenzikovHostComponent> ent, string reasonLoc)
    {
        var now = _timing.CurTime;
        if (ent.Comp.Active || now < ent.Comp.CooldownUntil || !_mobState.IsAlive(ent))
            return;

        if (_cyber.IsHostEmped(ent) || !TryGetImplant(ent, out var implant) || !_cyber.IsModuleOnline(implant))
            return;

        ent.Comp.Active = true;
        ent.Comp.ActiveUntil = now + ent.Comp.Duration;
        Dirty(ent);

        _speed.RefreshMovementSpeedModifiers(ent.Owner);
        foreach (var held in _hands.EnumerateHeld(ent.Owner))
        {
            BoostGun(ent, held);
        }

        if (!HasComp<TrailComponent>(ent))
        {
            // Тот же шлейф, что у Сандевистана, но короче и в золоте Cybersun — читается как «младший брат».
            var trail = AddComp<TrailComponent>(ent);
            trail.RenderedEntity = ent;
            trail.Color = TrailColor;
            trail.LerpTime = 0.05f;
            trail.LerpDelay = TimeSpan.FromSeconds(0.6);
            trail.Lifetime = 0.35f;
            trail.Frequency = 0.08f;
            trail.AlphaLerpAmount = 0.35f;
            trail.MaxParticleAmount = 8;
            Dirty(ent, trail);
            ent.Comp.AddedTrail = true;
        }

        _cyber.SetModuleState(implant, DutyCyberModuleState.Active, ent.Comp.ActiveUntil, Loc.GetString("duty-kerenzikov-hud-active"));
        _audio.PlayPvs(ent.Comp.StartSound, ent);
        Chat(ent, Loc.GetString(reasonLoc) + " " +
                  Loc.GetString("duty-kerenzikov-chat-active", ("seconds", (int) ent.Comp.Duration.TotalSeconds)));
    }

    private void Deactivate(Entity<DutyKerenzikovHostComponent> ent, bool startCooldown)
    {
        if (!ent.Comp.Active)
            return;

        ent.Comp.Active = false;
        Dirty(ent);

        _speed.RefreshMovementSpeedModifiers(ent.Owner);
        foreach (var held in _hands.EnumerateHeld(ent.Owner))
        {
            UnboostGun(held);
        }

        if (ent.Comp.AddedTrail)
        {
            RemComp<TrailComponent>(ent);
            ent.Comp.AddedTrail = false;
        }

        if (!startCooldown)
            return;

        ent.Comp.CooldownUntil = _timing.CurTime + ent.Comp.Cooldown;
        ent.Comp.CoolingDown = true;

        if (TryGetImplant(ent, out var implant))
            _cyber.SetModuleState(implant, DutyCyberModuleState.Cooldown, ent.Comp.CooldownUntil, string.Empty);

        _audio.PlayPvs(ent.Comp.EndSound, ent);
        Chat(ent, Loc.GetString("duty-kerenzikov-chat-cooldown", ("seconds", (int) ent.Comp.Cooldown.TotalSeconds)));
    }

    #endregion

    #region Темп атаки и стрельбы

    private void OnMeleeAttack(Entity<DutyKerenzikovHostComponent> ent, ref MeleeAttackEvent args)
    {
        if (!ent.Comp.Active || !TryComp<MeleeWeaponComponent>(args.Weapon, out var weapon))
            return;

        // Как у Сандевистана: срезаем остаток кулдауна, а не трогаем AttackRate — его правят и другие системы.
        var rest = weapon.NextAttack - _timing.CurTime;
        weapon.NextAttack -= rest - rest / ent.Comp.Multiplier;
        Dirty(args.Weapon, weapon);
    }

    private void OnEquipHand(Entity<DutyKerenzikovHostComponent> ent, ref DidEquipHandEvent args)
    {
        if (ent.Comp.Active)
            BoostGun(ent, args.Equipped);
    }

    private void OnUnequipHand(Entity<DutyKerenzikovHostComponent> ent, ref DidUnequipHandEvent args)
    {
        UnboostGun(args.Unequipped);
    }

    private void BoostGun(Entity<DutyKerenzikovHostComponent> ent, EntityUid item)
    {
        if (!_gunQuery.HasComp(item))
            return;

        var boost = EnsureComp<DutyKerenzikovBoostedGunComponent>(item);
        boost.Multiplier = ent.Comp.Multiplier;
        Dirty(item, boost);
        _gun.RefreshModifiers(item);
    }

    private void UnboostGun(EntityUid item)
    {
        if (!RemComp<DutyKerenzikovBoostedGunComponent>(item))
            return;

        _gun.RefreshModifiers(item);
    }

    #endregion

    private void OnMobState(Entity<DutyKerenzikovHostComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Deactivate(ent, true);
    }

    private void OnShutdown(Entity<DutyKerenzikovHostComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        // Имплант вырезали посреди разгона: снять скорость, шлейф и маркеры с оружия.
        ent.Comp.Active = false;
        _speed.RefreshMovementSpeedModifiers(ent.Owner);
        foreach (var held in _hands.EnumerateHeld(ent.Owner))
        {
            UnboostGun(held);
        }

        if (ent.Comp.AddedTrail)
            RemComp<TrailComponent>(ent);
    }

    private bool TryGetImplant(EntityUid host, out EntityUid implant)
    {
        implant = _cyber.FindImplant<DutyKerenzikovComponent>(host) ?? EntityUid.Invalid;
        return implant.IsValid();
    }

    private void Chat(EntityUid host, string message)
    {
        if (!_players.TryGetSessionByEntity(host, out var session))
            return;

        var wrapped = Loc.GetString("duty-kerenzikov-chat-wrap", ("message", message));
        _chat.ChatMessageToOne(ChatChannel.Notifications, message, wrapped, default, false, session.Channel, ChatColor);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < DangerScanInterval)
            return;
        _accumulator = 0f;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<DutyKerenzikovHostComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var ent = (uid, comp);

            if (comp.Active)
            {
                if (now >= comp.ActiveUntil)
                    Deactivate(ent, true);
                continue;
            }

            if (comp.CoolingDown)
            {
                if (now < comp.CooldownUntil)
                    continue;

                comp.CoolingDown = false;
                if (TryGetImplant(uid, out var implant))
                    _cyber.SetModuleState(implant, DutyCyberModuleState.Online, null, string.Empty);
                Chat(uid, Loc.GetString("duty-kerenzikov-chat-ready"));
            }

            if (IsDangerNear(ent))
                TryActivate(ent, "duty-kerenzikov-chat-trigger-danger");
        }
    }
}
