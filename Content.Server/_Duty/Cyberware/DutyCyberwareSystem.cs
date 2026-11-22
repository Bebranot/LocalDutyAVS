// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Chat.Managers;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.HealthPhrases;
using Content.Shared.Chat;
using Content.Shared.Damage.Systems;
using Content.Shared.Emp;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._Duty.Cyberware;

/// <summary>
/// _Duty: серверная часть общей базы кибера — учёт модулей на носителе, нейронагрузка и перегруз, ЭМИ-сбой.
/// Импланты меняют своё состояние только через <see cref="SetModuleState"/>, чтобы HUD всегда был в курсе.
/// </summary>
public sealed partial class DutyCyberwareSystem : SharedDutyCyberwareSystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IRobustRandom _random = default!;
    // _Duty: подавление перегруза иммуноблокаторами и красное предупреждение в чат.
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPlayerManager _player = default!;

    private static readonly EntProtoId ImmunoblockerEffectProto = "StatusEffectDutyImmunoblocker";
    private static readonly Color OverloadChatColor = Color.FromHex(DutyHealthPhrasesVisuals.ScreamColorHex);

    /// <summary>ЭМИ держит кибер выключенным не меньше и не больше этого, независимо от силы импульса.</summary>
    private static readonly TimeSpan EmpMin = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan EmpMax = TimeSpan.FromSeconds(30);

    private static readonly SoundSpecifier EmpSound = new SoundCollectionSpecifier("DutyCyberEmpGlitch");

    private const float UpdateInterval = 1f;
    private float _accumulator;

    private static readonly string[] OverloadPopups =
    {
        "duty-cyber-overload-1",
        "duty-cyber-overload-2",
        "duty-cyber-overload-3",
        "duty-cyber-overload-4",
    };

    private EntityQuery<DutyCyberwareComponent> _cyberQuery;

    public override void Initialize()
    {
        base.Initialize();

        _cyberQuery = GetEntityQuery<DutyCyberwareComponent>();

        SubscribeLocalEvent<DutyCyberwareComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutyCyberwareComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<DutyCyberHostComponent, EmpPulseEvent>(OnHostEmp);
    }

    #region API

    /// <summary>
    /// Сменить состояние модуля и обновить HUD носителя.
    /// <paramref name="detail"/> = null оставляет текущий хвост строки.
    /// </summary>
    public void SetModuleState(EntityUid implant, DutyCyberModuleState state, TimeSpan? cooldownEnd = null, string? detail = null, DutyCyberwareComponent? comp = null)
    {
        if (!Resolve(implant, ref comp, false))
            return;

        comp.State = state;
        comp.CooldownEnd = cooldownEnd;
        if (detail != null)
            comp.Detail = detail;
        Dirty(implant, comp);

        if (TryGetHost(implant, out var host))
            RefreshHost(host);
    }

    /// <summary>Сменить только хвост строки HUD («40%», «12м»), не трогая состояние.</summary>
    public void SetModuleDetail(EntityUid implant, string detail, DutyCyberwareComponent? comp = null)
    {
        if (!Resolve(implant, ref comp, false) || comp.Detail == detail)
            return;

        comp.Detail = detail;
        Dirty(implant, comp);

        if (TryGetHost(implant, out var host))
            RefreshHost(host);
    }

    /// <summary>Пересобрать записи модулей, нагрузку и скин носителя. Зовётся сама на вживление/извлечение.</summary>
    public void RefreshHost(EntityUid host)
    {
        if (!TryComp<ImplantedComponent>(host, out var implanted))
        {
            RemCompDeferred<DutyCyberHostComponent>(host);
            return;
        }

        var groups = new Dictionary<string, (DutyCyberwareComponent Comp, int Count, int Load)>();
        var order = new List<string>();
        var factionLoad = new int[Enum.GetValues<DutyCyberFaction>().Length];
        var totalLoad = 0;
        // _Duty: суммируем бонусы к лимиту нейронагрузки от установленных имплантов.
        var totalLimitBonus = 0;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (!_cyberQuery.TryComp(implant, out var cyber))
                continue;

            totalLoad += cyber.Load;
            totalLimitBonus += cyber.LoadLimitBonus;
            factionLoad[(int) cyber.Faction] += cyber.Load;

            // Один и тот же модуль в нескольких экземплярах (бронепластины) — одна строка «×N».
            var key = cyber.ModuleName.Id;
            if (groups.TryGetValue(key, out var group))
            {
                groups[key] = (group.Comp, group.Count + 1, group.Load + cyber.Load);
            }
            else
            {
                groups[key] = (cyber, 1, cyber.Load);
                order.Add(key);
            }
        }

        if (order.Count == 0)
        {
            RemCompDeferred<DutyCyberHostComponent>(host);
            return;
        }

        var hostComp = EnsureComp<DutyCyberHostComponent>(host);
        var wasOverloaded = hostComp.Load > hostComp.LoadLimit;

        hostComp.Modules.Clear();
        foreach (var key in order)
        {
            var (comp, count, load) = groups[key];
            hostComp.Modules.Add(new DutyCyberModuleEntry(key, comp.Faction, comp.State, comp.CooldownEnd, comp.Detail, count, load, comp.EmpSensitive));
        }

        hostComp.Load = totalLoad;
        // _Duty: эффективный лимит = базовый (6) + сумма LoadLimitBonus всех имплантов.
        hostComp.LoadLimit = DutyCyberHostComponent.BaseLoadLimit + totalLimitBonus;
        hostComp.Skin = PickSkin(factionLoad);

        if (!wasOverloaded && hostComp.Load > hostComp.LoadLimit && !_statusEffects.HasStatusEffect(host, ImmunoblockerEffectProto))
        {
            hostComp.NextOverloadTick = Timing.CurTime + TimeSpan.FromSeconds(3);
            _popup.PopupEntity(Loc.GetString("duty-cyber-overload-start"), host, host, PopupType.LargeCaution);
        }

        Dirty(host, hostComp);
    }

    #endregion

    private static DutyCyberFaction PickSkin(int[] factionLoad)
    {
        // При равенстве побеждает «чужое» железо: оперативник с трофейным модулем NT остаётся красным.
        var best = DutyCyberFaction.NanoTrasen;
        var bestLoad = -1;
        foreach (var faction in new[] { DutyCyberFaction.Gorlex, DutyCyberFaction.Cybersun, DutyCyberFaction.Syndicate, DutyCyberFaction.Mercenary, DutyCyberFaction.NanoTrasen })
        {
            var load = factionLoad[(int) faction];
            if (load > bestLoad)
            {
                best = faction;
                bestLoad = load;
            }
        }

        return best;
    }

    private void OnImplanted(Entity<DutyCyberwareComponent> ent, ref ImplantImplantedEvent args)
    {
        RefreshHost(args.Implanted);
    }

    private void OnRemoved(Entity<DutyCyberwareComponent> ent, ref ImplantRemovedEvent args)
    {
        // Событие идёт из EntGotRemovedFromContainerMessage: имплант уже вне контейнера, пересчёт его не учтёт.
        RefreshHost(args.Implanted);
    }

    private void OnHostEmp(Entity<DutyCyberHostComponent> ent, ref EmpPulseEvent args)
    {
        if (!TryComp<ImplantedComponent>(ent, out var implanted))
            return;

        var anySensitive = false;
        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (_cyberQuery.TryComp(implant, out var cyber) && cyber.EmpSensitive)
            {
                anySensitive = true;
                break;
            }
        }

        if (!anySensitive)
            return;

        var shield = new DutyCyberEmpShieldEvent(ent);
        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            RaiseLocalEvent(implant, ref shield);
            if (shield.Cancelled)
                return;
        }

        var duration = args.Duration < EmpMin ? EmpMin : args.Duration > EmpMax ? EmpMax : args.Duration;
        var until = Timing.CurTime + duration;
        if (ent.Comp.EmpUntil is { } current && current > until)
            until = current;

        var wasEmped = ent.Comp.EmpUntil is { } prev && prev > Timing.CurTime;
        ent.Comp.EmpUntil = until;
        Dirty(ent);
        args.Affected = true;

        if (!wasEmped)
        {
            _popup.PopupEntity(Loc.GetString("duty-cyber-emp-popup"), ent, ent, PopupType.LargeCaution);
            _audio.PlayPvs(EmpSound, ent);
            _jitter.DoJitter(ent, TimeSpan.FromSeconds(4), true, 12f, 6f);
            if (!_mobState.IsDead(ent))
                _stamina.TakeStaminaDamage(ent, 25f);
        }

        var ev = new DutyCyberEmpEvent(ent, until);
        foreach (var implant in new List<EntityUid>(implanted.ImplantContainer.ContainedEntities))
        {
            RaiseLocalEvent(implant, ref ev);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;
        _accumulator = 0f;

        var now = Timing.CurTime;
        var query = EntityQueryEnumerator<DutyCyberHostComponent>();
        while (query.MoveNext(out var uid, out var host))
        {
            if (host.EmpUntil is { } until && until <= now)
            {
                host.EmpUntil = null;
                Dirty(uid, host);
                _popup.PopupEntity(Loc.GetString("duty-cyber-emp-end-popup"), uid, uid, PopupType.Medium);

                if (TryComp<ImplantedComponent>(uid, out var implanted))
                {
                    var ended = new DutyCyberEmpEndedEvent(uid);
                    foreach (var implant in new List<EntityUid>(implanted.ImplantContainer.ContainedEntities))
                    {
                        RaiseLocalEvent(implant, ref ended);
                    }
                }
            }

            if (host.Load <= host.LoadLimit || now < host.NextOverloadTick || _mobState.IsDead(uid))
                continue;

            // _Duty: активный статус иммуноблокатора (5 минут) подавляет штрафы и спам нейроперегруза.
            if (_statusEffects.HasStatusEffect(uid, ImmunoblockerEffectProto))
                continue;

            // Чем сильнее перегруз, тем чаще и злее «киберпсихоз»: 1 сверху — раз в ~25 с, 4+ — раз в ~8 с.
            var over = host.Load - host.LoadLimit;
            var period = MathF.Max(8f, 30f - over * 5.5f);
            host.NextOverloadTick = now + TimeSpan.FromSeconds(period * _random.NextFloat(0.75f, 1.25f));

            _popup.PopupEntity(Loc.GetString(_random.Pick(OverloadPopups)), uid, uid, PopupType.SmallCaution);
            _jitter.DoJitter(uid, TimeSpan.FromSeconds(1.5f + over * 0.5f), true, 6f + over * 2f, 5f);
            _stamina.TakeStaminaDamage(uid, 6f * over);
            // _Duty: красное сообщение шрифтом Underdog в чат игрока при перегрузе.
            SendOverloadChatWarning(uid);
        }
    }

    // _Duty: прямое сообщение игроку красным цветом (#FF0000) и шрифтом Underdog о нехватке иммуноблокаторов.
    private void SendOverloadChatWarning(EntityUid uid)
    {
        if (!_player.TryGetSessionByEntity(uid, out var session))
            return;

        var message = Loc.GetString("duty-cyber-overload-need-immunoblocker");
        var escaped = FormattedMessage.EscapeText(message);
        var wrapped = $"[color={DutyHealthPhrasesVisuals.ScreamColorHex}][tfont=\"{DutyHealthPhrasesVisuals.FontPrototypeId}\" size={DutyHealthPhrasesVisuals.WhisperFontSize} defaultFont=NotoSansDisplayItalic defaultSize=11]{escaped}[/tfont][/color]";

        _chat.ChatMessageToOne(ChatChannel.Local, message, wrapped, uid, false, session.Channel, OverloadChatColor);
    }
}
