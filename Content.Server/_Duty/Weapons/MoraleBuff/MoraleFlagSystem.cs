using Content.Server.Chat.Systems;
using Content.Shared._Duty.Weapons.MoraleBuff;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Weapons.MoraleBuff;

/// <summary>
/// _Duty: способность алебарды ОСЩ «Поднять и помахать флагом».
/// Выдаёт Action владельцу пока алебарда взята в две руки (wielded), при активации
/// баффает кастера и живых гуманоидов рядом (см. <see cref="MoraleBuffSystem"/>).
/// </summary>
public sealed class MoraleFlagSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MoraleBuffSystem _buff = default!;

    private readonly HashSet<EntityUid> _targets = new();

    /// <summary>Варианты эмоции при активации — выбирается случайно (см. morale_buff.ftl).</summary>
    private static readonly string[] EmoteKeys =
    {
        "morale-flag-emote-1",
        "morale-flag-emote-2",
        "morale-flag-emote-3",
        "morale-flag-emote-4",
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MoraleFlagComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MoraleFlagComponent, ItemWieldedEvent>(OnWielded);
        SubscribeLocalEvent<MoraleFlagComponent, ItemUnwieldedEvent>(OnUnwielded);
        SubscribeLocalEvent<MoraleFlagActionEvent>(OnFlagAction);
    }

    // ── Выдача Action ─────────────────────────────────────────

    private void OnShutdown(Entity<MoraleFlagComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnWielded(Entity<MoraleFlagComponent> ent, ref ItemWieldedEvent args)
    {
        _actions.AddAction(args.User, ref ent.Comp.ActionEntity, ent.Comp.ActionId);

        // Восстанавливаем кулдаун, если он ещё не истёк (Action пересоздаётся при unwield/wield).
        var curTime = _timing.CurTime;
        if (ent.Comp.CooldownEnd > curTime && ent.Comp.ActionEntity.HasValue)
            _actions.SetCooldown(ent.Comp.ActionEntity.Value, curTime, ent.Comp.CooldownEnd);
    }

    private void OnUnwielded(Entity<MoraleFlagComponent> ent, ref ItemUnwieldedEvent args)
    {
        // Сохраняем конец кулдауна до удаления Action, иначе он потеряется при пересоздании.
        if (ent.Comp.ActionEntity.HasValue
            && _actions.GetAction(ent.Comp.ActionEntity.Value) is { } actionEnt
            && actionEnt.Comp.Cooldown is { } cooldown)
        {
            ent.Comp.CooldownEnd = cooldown.End;
        }

        _actions.RemoveAction(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    // ── Активация ─────────────────────────────────────────────

    private void OnFlagAction(MoraleFlagActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;

        // Ищем алебарду с флагом в руках.
        EntityUid? flagUid = null;
        MoraleFlagComponent? flag = null;
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (TryComp<MoraleFlagComponent>(held, out var c))
            {
                flagUid = held;
                flag = c;
                break;
            }
        }

        if (flagUid == null || flag == null)
            return;

        if (!IsWielded(user, flagUid.Value))
        {
            _popup.PopupEntity(Loc.GetString("morale-flag-need-wield"), user, user, PopupType.SmallCaution);
            return;
        }

        // Звук активации (заглушка) + эмоция (случайная вариация).
        if (flag.Sound != null)
            _audio.PlayPvs(flag.Sound, user);
        _chat.TrySendInGameICMessage(user, Loc.GetString(_random.Pick(EmoteKeys)), InGameICChatType.Emote, ChatTransmitRange.Normal);

        // Бафф кастеру.
        BuffTarget(user, flag);

        // Бафф живым гуманоидам в радиусе.
        _targets.Clear();
        _lookup.GetEntitiesInRange(user, flag.Radius, _targets);
        foreach (var target in _targets)
        {
            if (target == user)
                continue;
            if (!HasComp<HumanoidAppearanceComponent>(target))
                continue;
            if (!_mobState.IsAlive(target))
                continue;

            BuffTarget(target, flag);
        }

        // Кулдаун (хранится в ActionComponent, переживает wield/unwield — см. OnUnwielded).
        if (flag.ActionEntity.HasValue)
            _actions.StartUseDelay(flag.ActionEntity.Value);

        args.Handled = true;
    }

    private void BuffTarget(EntityUid target, MoraleFlagComponent flag)
    {
        _buff.Apply(target, flag.BuffDuration);

        // Визуальный эффект (восклицательный знак) над головой цели.
        SpawnAttachedTo(flag.VisualEffect, new EntityCoordinates(target, default));
    }

    // ── Хелперы ───────────────────────────────────────────────

    private bool IsWielded(EntityUid user, EntityUid flag)
    {
        if (!_hands.IsHolding(user, flag, out _))
            return false;

        if (!TryComp<WieldableComponent>(flag, out var wieldable))
            return false; // флаг без Wieldable махать «двумя руками» нельзя

        return wieldable.Wielded;
    }
}
