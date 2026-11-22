// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Syndicate;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Implants;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Cyberware.Syndicate;

/// <summary>
/// _Duty: «Голосовой паразит» — носитель 5 минут слышит выбранный канал без ключа шифрования, потом перезарядка.
/// Слушает он через обычные ActiveRadio + IntrinsicRadioReceiver (как радио решает, кому доставить), канал добавляется временно.
/// Контр-плей: у всех, кто сидит на этом канале, раз в минуту-другую тихая «помеха» — внимательные услышат.
/// </summary>
public sealed partial class DutyVoiceParasiteSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;

    private const float UpdateInterval = 1f;
    private float _accumulator;

    private EntityQuery<ActorComponent> _actorQuery;
    private EntityQuery<ActiveRadioComponent> _activeRadioQuery;

    public override void Initialize()
    {
        base.Initialize();

        _actorQuery = GetEntityQuery<ActorComponent>();
        _activeRadioQuery = GetEntityQuery<ActiveRadioComponent>();

        SubscribeLocalEvent<DutyVoiceParasiteComponent, DutyVoiceParasiteActionEvent>(OnAction);
        SubscribeLocalEvent<DutyVoiceParasiteComponent, DutyVoiceParasiteSelectMessage>(OnSelect);
        SubscribeLocalEvent<DutyVoiceParasiteComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<DutyVoiceParasiteComponent, DutyCyberEmpEvent>(OnEmp);
    }

    private void OnAction(Entity<DutyVoiceParasiteComponent> ent, ref DutyVoiceParasiteActionEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        var user = args.Performer;
        if (!_cyber.IsModuleOnline(ent))
        {
            Deny(user, "duty-syndicate-offline");
            return;
        }

        // Повторное нажатие во время прослушки — отключиться самому (перезарядка как после полного сеанса).
        if (ent.Comp.ActiveChannel != null)
        {
            Stop(ent, user, true);
            return;
        }

        if (_timing.CurTime < ent.Comp.CooldownEndTime)
        {
            Deny(user, "duty-parasite-cooldown");
            return;
        }

        _ui.OpenUi(ent.Owner, DutyVoiceParasiteUiKey.Key, user);
    }

    private void OnSelect(Entity<DutyVoiceParasiteComponent> ent, ref DutyVoiceParasiteSelectMessage args)
    {
        var user = args.Actor;
        _ui.CloseUi(ent.Owner, DutyVoiceParasiteUiKey.Key, user);

        if (!_cyber.TryGetHost(ent, out var host) || host != user)
            return;

        if (!_cyber.IsModuleOnline(ent) || ent.Comp.ActiveChannel != null || _timing.CurTime < ent.Comp.CooldownEndTime)
            return;

        if (!ent.Comp.Channels.Contains(args.Channel) || !_proto.TryIndex(args.Channel, out var channel))
            return;

        // Запоминаем, что именно добавили мы, чтобы при окончании не снести чужое (раса/гарнитурные каналы).
        var active = _activeRadioQuery.CompOrNull(host);
        ent.Comp.AddedActiveRadio = active == null;
        active ??= EnsureComp<ActiveRadioComponent>(host);
        ent.Comp.AddedChannel = active.Channels.Add(args.Channel);
        ent.Comp.AddedIntrinsic = !HasComp<IntrinsicRadioReceiverComponent>(host);
        EnsureComp<IntrinsicRadioReceiverComponent>(host);

        var now = _timing.CurTime;
        ent.Comp.ActiveChannel = args.Channel;
        ent.Comp.EndTime = now + TimeSpan.FromSeconds(ent.Comp.DurationSeconds);
        ent.Comp.NextNoise = now + TimeSpan.FromSeconds(_random.NextFloat(ent.Comp.NoiseMin, ent.Comp.NoiseMax));

        _cyber.SetModuleState(ent, DutyCyberModuleState.Active, ent.Comp.EndTime, channel.LocalizedName.ToUpperInvariant());
        _popup.PopupEntity(Loc.GetString("duty-parasite-start", ("channel", channel.LocalizedName)), host, host);
        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(host):user} started eavesdropping radio channel {args.Channel.Id} with voice parasite");
    }

    private void OnRemoved(Entity<DutyVoiceParasiteComponent> ent, ref ImplantRemovedEvent args)
    {
        Stop(ent, args.Implanted, false);
    }

    private void OnEmp(Entity<DutyVoiceParasiteComponent> ent, ref DutyCyberEmpEvent args)
    {
        if (ent.Comp.ActiveChannel != null)
            Stop(ent, args.Host, true);
    }

    /// <summary>Снять временные компоненты и канал с носителя и уйти на перезарядку.</summary>
    private void Stop(Entity<DutyVoiceParasiteComponent> ent, EntityUid host, bool popup)
    {
        if (ent.Comp.ActiveChannel is not { } channel)
            return;

        if (TryComp<ActiveRadioComponent>(host, out var active))
        {
            if (ent.Comp.AddedChannel)
                active.Channels.Remove(channel);
            if (ent.Comp.AddedActiveRadio)
                RemComp<ActiveRadioComponent>(host);
        }

        if (ent.Comp.AddedIntrinsic)
            RemComp<IntrinsicRadioReceiverComponent>(host);

        ent.Comp.ActiveChannel = null;
        ent.Comp.AddedActiveRadio = false;
        ent.Comp.AddedChannel = false;
        ent.Comp.AddedIntrinsic = false;

        ent.Comp.CooldownEndTime = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.CooldownSeconds);
        _cyber.SetModuleState(ent, DutyCyberModuleState.Cooldown, ent.Comp.CooldownEndTime, string.Empty);

        if (popup)
            _popup.PopupEntity(Loc.GetString("duty-parasite-end"), host, host, PopupType.Small);
    }

    private void Deny(EntityUid user, string locId)
    {
        _popup.PopupEntity(Loc.GetString(locId), user, user, PopupType.SmallCaution);
        _audio.PlayPvs(new SoundCollectionSpecifier("DutyCyberDeny"), user);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;
        _accumulator = 0f;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<DutyVoiceParasiteComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ActiveChannel is { } channel)
            {
                if (!_cyber.TryGetHost(uid, out var host))
                    continue;

                if (now >= comp.EndTime)
                {
                    Stop((uid, comp), host, true);
                    continue;
                }

                if (now >= comp.NextNoise)
                {
                    comp.NextNoise = now + TimeSpan.FromSeconds(_random.NextFloat(comp.NoiseMin, comp.NoiseMax));
                    PlayNoise(comp, host, channel);
                }

                continue;
            }

            // Перезарядка закончилась — модуль снова «в сети» (HUD сам нового статуса не выведет).
            if (comp.CooldownEndTime != TimeSpan.Zero && now >= comp.CooldownEndTime)
            {
                comp.CooldownEndTime = TimeSpan.Zero;
                _cyber.SetModuleState(uid, DutyCyberModuleState.Online, null, string.Empty);
            }
        }
    }

    /// <summary>Тихая «помеха» тем, кто слушает канал (кроме самого паразита): слышат только они.</summary>
    private void PlayNoise(DutyVoiceParasiteComponent comp, EntityUid host, ProtoId<RadioChannelPrototype> channel)
    {
        var listeners = new List<EntityUid>();
        var query = EntityQueryEnumerator<ActiveRadioComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var radio, out var xform))
        {
            if (uid == host || !radio.Channels.Contains(channel))
                continue;

            // Гарнитура или встроенный приёмник: слышит тот, кто её носит.
            var listener = _actorQuery.HasComp(uid) ? uid : xform.ParentUid;
            if (listener == host || !listener.IsValid() || !_actorQuery.HasComp(listener) || listeners.Contains(listener))
                continue;

            listeners.Add(listener);
        }

        if (listeners.Count == 0)
            return;

        _audio.PlayGlobal(comp.NoiseSound, Filter.Entities(listeners.ToArray()), false);
    }
}
