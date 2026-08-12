// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs/SharedInteractionVerbsSystem.cs),
// изначально Einstein Engines. Contest-скейлинг (сила/выносливость/здоровье) вырезан — см. заметку
// в InteractionVerbPrototype.cs.
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._Duty.InteractionVerbs.Events;
using Content.Shared.ActionBlocker;
using Content.Shared.DoAfter;
using Content.Shared.Ghost;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Content.Shared._Duty.InteractionVerbs.InteractionPopupPrototype.Prefix;
using static Content.Shared._Duty.InteractionVerbs.InteractionVerbPrototype.EffectTargetSpecifier;

namespace Content.Shared._Duty.InteractionVerbs;

/// <summary>
///     Общая логика вкладки "Взаимодействовать" контекстного меню: сбор списка вербов, кулдауны,
///     запуск do-after и выполнение самого действия. Действие (<see cref="InteractionAction"/>)
///     реально выполняется только на сервере — см. <see cref="PerformVerb"/>.
/// </summary>
public abstract class SharedInteractionVerbsSystem : EntitySystem
{
    private readonly InteractionAction.VerbDependencies _verbDependencies = new();
    private List<InteractionVerbPrototype> _globalPrototypes = default!;

    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfters = default!;
    [Dependency] private readonly SharedInteractionSystem _interactions = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popups = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityWhitelistSystem _entityWhitelist = default!;

    public override void Initialize()
    {
        // EntityWhitelistSystem — EntitySystem, а не сервис из глобального IoC, поэтому его
        // не резолвит IoCManager.InjectDependencies — проставляем отдельно.
        IoCManager.InjectDependencies(_verbDependencies);
        _verbDependencies.WhitelistSystem = _entityWhitelist;

        LoadGlobalVerbs();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        SubscribeLocalEvent<InteractionVerbsComponent, GetVerbsEvent<InteractionVerb>>(OnGetOthersVerbs);
        SubscribeLocalEvent<OwnInteractionVerbsComponent, GetVerbsEvent<InnateVerb>>(OnGetOwnVerbs);
        SubscribeLocalEvent<InteractionVerbDoAfterEvent>(OnDoAfterFinished);
    }

    private void LoadGlobalVerbs()
    {
        _globalPrototypes = _protoMan.EnumeratePrototypes<InteractionVerbPrototype>()
            .Where(v => v is { Global: true, Abstract: false })
            .ToList();
    }

    #region event handling

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<InteractionVerbPrototype>())
            return;

        LoadGlobalVerbs();
    }

    private void OnGetOthersVerbs(Entity<InteractionVerbsComponent> entity, ref GetVerbsEvent<InteractionVerb> args)
    {
        // Глобальные вербы здесь не добавляются — они уже добавлены в OnGetOwnVerbs.
        AddAll(entity.Comp.AllowedVerbs.Select(_protoMan.Index), args, () => new InteractionVerb());
    }

    private void OnGetOwnVerbs(Entity<OwnInteractionVerbsComponent> entity, ref GetVerbsEvent<InnateVerb> args)
    {
        var allVerbs = entity.Comp.AllowedVerbs;

        var getVerbsEv = new GetInteractionVerbsEvent(allVerbs);
        RaiseLocalEvent(entity, ref getVerbsEv);

        // Глобальные вербы добавляются здесь, чтобы работать даже на сущностях без своих интеракций.
        AddAll(allVerbs.Select(_protoMan.Index).Union(_globalPrototypes), args, () => new InnateVerb());
    }

    private void OnDoAfterFinished(InteractionVerbDoAfterEvent ev)
    {
        if (ev.Cancelled || ev.Handled || !_protoMan.TryIndex(ev.VerbPrototype, out var proto))
            return;

        PerformVerb(proto, ev.VerbArgs!);
        ev.Handled = true;
    }

    #endregion

    #region public api

    /// <summary>
    ///     Запускает верб, сначала проверив его возможность (если не force). При успехе либо
    ///     запустит do-after, либо сразу передаст управление в <see cref="PerformVerb"/>.
    /// </summary>
    public bool StartVerb(InteractionVerbPrototype proto, InteractionArgs args, bool force = false)
    {
        if (!TryComp<OwnInteractionVerbsComponent>(args.User, out var ownInteractions)
            || !force && !CheckVerbCooldown(proto, args, out _, ownInteractions))
            return false;

        if (!_net.IsClient
            && !force
            && proto.Action?.CanPerform(args, proto, true, _verbDependencies) != true)
        {
            CreateVerbEffects(proto.EffectFailure, Fail, proto, args);
            return false;
        }

        var attemptEv = new InteractionVerbAttemptEvent(proto, args);
        RaiseLocalEvent(args.User, ref attemptEv);
        RaiseLocalEvent(args.Target, ref attemptEv);

        if (attemptEv.Cancelled)
        {
            CreateVerbEffects(proto.EffectFailure, Fail, proto, args);
            return false;
        }
        if (attemptEv.Handled)
            return true;

        StartVerbCooldown(proto, args, proto.Cooldown, ownInteractions);

        if (proto.Delay <= TimeSpan.Zero)
        {
            PerformVerb(proto, args);
            return true;
        }

        var doAfter = new DoAfterArgs(EntityManager, args.User, proto.Delay, new InteractionVerbDoAfterEvent(proto.ID, args), null, target: args.Target)
        {
            Broadcast = true, // eventTarget не задан — событие не направленное, только broadcast
            BreakOnHandChange = proto.RequiresHands,
            NeedHand = proto.RequiresHands,
            RequireCanInteract = proto.RequiresCanAccess,
            BreakOnDamage = proto.BreakOnDamage,
            BreakOnMove = proto.BreakOnMove,
            BreakOnWeightlessMove = proto.BreakOnWeightlessMove,
        };

        var isSuccess = _doAfters.TryStartDoAfter(doAfter);
        if (isSuccess)
            CreateVerbEffects(proto.EffectDelayed, Delayed, proto, args);

        return isSuccess;
    }

    /// <summary>
    ///     Повторно проверяет CanPerform (если не force) и затем выполняет само действие верба,
    ///     показывая попап успеха/провала.
    /// </summary>
    /// <remarks>На клиенте не делает ничего — клиент ничего не знает о действиях вербов, выполнять их должен только сервер.</remarks>
    public void PerformVerb(InteractionVerbPrototype proto, InteractionArgs args, bool force = false)
    {
        if (_net.IsClient)
            return; // иначе будут проблемы

        if (!PerformChecks(proto, ref args, out _, out _) && !force
            || !proto.Action!.CanPerform(args, proto, false, _verbDependencies) && !force
            || !proto.Action.Perform(args, proto, _verbDependencies))
        {
            CreateVerbEffects(proto.EffectFailure, Fail, proto, args);
            return;
        }

        CreateVerbEffects(proto.EffectSuccess, Success, proto, args);
    }

    #endregion

    #region private api

    /// <summary>
    ///     Создаёт вербы для всех прототипов из списка, подходящих под свои требования. Использует
    ///     переданную фабрику для создания экземпляров верба.
    /// </summary>
    // Дженерик-ограничение `where T : Verb, new()` тут ловит sandbox violation, поэтому фабрика.
    private void AddAll<T>(IEnumerable<InteractionVerbPrototype> verbs, GetVerbsEvent<T> args, Func<T> factory) where T : Verb
    {
        // Призракам вербы не добавляем — система призраков сама отменит все вербы от/на неадминских призраков.
        if (TryComp<GhostComponent>(args.User, out var ghost) && !ghost.CanGhostInteract)
            return;

        var ownInteractions = EnsureComp<OwnInteractionVerbsComponent>(args.User);
        foreach (var proto in verbs)
        {
            DebugTools.AssertNotEqual(proto.Abstract, true, "Attempted to add a verb with an abstract prototype.");

            var name = proto.Name;
            if (args.Verbs.Any(v => v.Text == name))
                continue;

            var verbArgs = InteractionArgs.From(args);
            var isEnabled = PerformChecks(proto, ref verbArgs, out var skipAdding, out var errorLocale);

            if (skipAdding)
                continue;

            var verb = factory.Invoke();
            CopyVerbData(proto, verb);
            verb.Act = () => StartVerb(proto, verbArgs);
            verb.Disabled = !isEnabled;

            if (!isEnabled)
                verb.Message = Loc.GetString(errorLocale!);

            if (isEnabled && !CheckVerbCooldown(proto, verbArgs, out var remainingTime, ownInteractions))
            {
                verb.Disabled = true;
                verb.Message = Loc.GetString("interaction-verb-cooldown", ("seconds", remainingTime.TotalSeconds));
            }

            args.Verbs.Add(verb);
        }
    }

    /// <summary>
    ///     Выполняет все проверки требований/действия верба. Возвращает true, если верб можно
    ///     выполнить прямо сейчас. Параметр skipAdding указывает, нужно ли вообще не добавлять
    ///     верб в список (в отличие от добавления его отключённым).
    /// </summary>
    private bool PerformChecks(InteractionVerbPrototype proto, ref InteractionArgs args, out bool skipAdding, [NotNullWhen(false)] out string? errorLocale)
    {
        if (!proto.AllowSelfInteract && args.User == args.Target
            || !Transform(args.User).Coordinates.TryDistance(EntityManager, Transform(args.Target).Coordinates, out var distance))
        {
            skipAdding = true;
            errorLocale = "interaction-verb-invalid-target";
            return false;
        }

        if (proto.Requirement?.IsMet(args, proto, _verbDependencies) == false)
        {
            skipAdding = proto.HideByRequirement;
            errorLocale = "interaction-verb-invalid";
            return false;
        }

        // На клиенте эту проверку пропускаем, т.к. клиент ничего не знает о действиях. TODO: сделать действия mixed server/client?
        if (proto.Action?.IsAllowed(args, proto, _verbDependencies) != true && !_net.IsClient)
        {
            skipAdding = proto.HideWhenInvalid;
            errorLocale = "interaction-verb-invalid";
            return false;
        }

        skipAdding = false;
        if (proto.RequiresHands && !args.HasHands)
        {
            errorLocale = "interaction-verb-no-hands";
            return false;
        }

        if (!args.CanInteract || proto.RequiresCanAccess && !args.CanAccess || !proto.Range.IsInRange(distance))
        {
            errorLocale = "interaction-verb-cannot-reach";
            return false;
        }

        errorLocale = null;
        return true;
    }

    private void CopyVerbData(InteractionVerbPrototype proto, Verb verb)
    {
        verb.Text = proto.Name;
        verb.Message = proto.Description;
        verb.DoContactInteraction = proto.DoContactInteraction;
        verb.Priority = proto.Priority;
        verb.Icon = proto.Icon;
        verb.Category = VerbCategory.Interaction;
    }

    /// <summary>
    ///     Проверяет, не на кулдауне ли верб. Возвращает true, если им можно пользоваться прямо сейчас.
    /// </summary>
    private bool CheckVerbCooldown(InteractionVerbPrototype proto, InteractionArgs args, out TimeSpan remainingTime, OwnInteractionVerbsComponent? comp = null)
    {
        remainingTime = TimeSpan.Zero;
        if (!Resolve(args.User, ref comp))
            return false;

        var cooldownTarget = proto.GlobalCooldown ? EntityUid.Invalid : args.Target;
        if (!comp.Cooldowns.TryGetValue((proto.ID, cooldownTarget), out var cooldown))
            return true;

        remainingTime = cooldown - _timing.CurTime;
        return remainingTime <= TimeSpan.Zero;
    }

    private void StartVerbCooldown(InteractionVerbPrototype proto, InteractionArgs args, TimeSpan cooldown, OwnInteractionVerbsComponent? comp = null)
    {
        if (!Resolve(args.User, ref comp))
            return;

        var cooldownTarget = proto.GlobalCooldown ? EntityUid.Invalid : args.Target;
        comp.Cooldowns[(proto.ID, cooldownTarget)] = _timing.CurTime + cooldown;

        // Заодно чистим старые кулдауны, чтобы не текла память.
        // TODO: возможно, стоит перейти на List — Dictionary тут явно избыточен.
        foreach (var (key, time) in comp.Cooldowns.ToArray())
        {
            if (time < _timing.CurTime)
                comp.Cooldowns.Remove(key);
        }
    }

    private void CreateVerbEffects(InteractionVerbPrototype.EffectSpecifier? specifier, InteractionPopupPrototype.Prefix prefix, InteractionVerbPrototype proto, InteractionArgs args)
    {
        // На клиенте эффекты не создаём — иначе будут проблемы.
        if (specifier is null || _net.IsClient)
            return;

        var (user, target, used) = (args.User, args.Target, args.Used);

        // Куда показывать попап для разных категорий игроков
        var userTarget = specifier.EffectTarget is User or UserThenTarget or TargetThenUser ? user : target;
        var targetTarget = specifier.EffectTarget is Target or UserThenTarget or TargetThenUser ? target : user;
        var othersTarget = specifier.EffectTarget is Target or UserThenTarget ? target : user;
        var othersFilter = Filter.Pvs(othersTarget).RemoveWhereAttachedEntity(ent => ent == user || ent == target);

        // Попапы
        if (_protoMan.TryIndex(specifier.Popup, out var popup))
        {
            var locPrefix = $"interaction-{proto.ID}-{prefix.ToString().ToLower()}";

            (string, object)[] localeArgs =
            [
                ("user", Identity.Entity(user, EntityManager)),
                ("target", Identity.Entity(target, EntityManager)),
                ("used", used ?? EntityUid.Invalid),
                ("selfTarget", user == target),
                ("hasUsed", used != null)
            ];

            // Попап для исполнителя
            var userSuffix = popup.SelfSuffix ?? popup.OthersSuffix;
            if (userSuffix is not null)
                PopupEffects(Loc.GetString($"{locPrefix}-{userSuffix}-popup", localeArgs), userTarget, Filter.Entities(user), false, popup);

            // Попап для цели
            var targetSuffix = popup.TargetSuffix ?? popup.OthersSuffix;
            if (targetSuffix is not null && user != target)
                PopupEffects(Loc.GetString($"{locPrefix}-{targetSuffix}-popup", localeArgs), targetTarget, Filter.Entities(target), false, popup);

            // Попап для наблюдателей
            var othersSuffix = popup.OthersSuffix;
            if (othersSuffix is not null)
                PopupEffects(Loc.GetString($"{locPrefix}-{othersSuffix}-popup", localeArgs), othersTarget, othersFilter, true, popup, clip: true);
        }

        // Звуки
        if (specifier.Sound is { } sound)
        {
            // TODO: выбор между точным источником звука и лишним спауном сущности...
            _audio.PlayEntity(sound, Filter.Entities(user, target), target, false, specifier.SoundParams);

            if (specifier.SoundPerceivedByOthers)
                _audio.PlayEntity(sound, othersFilter, othersTarget, false, specifier.SoundParams);
        }
    }

    private void PopupEffects(string message, EntityUid target, Filter filter, bool recordReplay, InteractionPopupPrototype popup, bool clip = false)
    {
        // Отправка сообщения в чат сама по себе создаст попап.
        // TODO: вероятно, стоит разделить попапы и чат-сообщения окончательно.
        if (popup.LogPopup)
            SendChatLog(message, target, filter, popup, clip);
        else
            _popups.PopupEntity(message, target, filter, recordReplay, popup.PopupType);
    }

    protected virtual void SendChatLog(string message, EntityUid source, Filter filter, InteractionPopupPrototype popup, bool clip)
    {
    }

    #endregion
}
