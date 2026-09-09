// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: MIT

using System.Linq;
using Content.Server.Administration.Logs;
using Content.Shared._Duty.SpawnMenu;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Duty.SpawnMenu;

/// <summary>
/// _Duty: сервер меню выдачи предметов. Клиенту не верим ни в чём: он присылает только
/// «хочу вот этот прототип вот в этой точке», всё остальное — доступ по сикею, лимит
/// на раунд и дистанция — проверяется здесь.
/// </summary>
public sealed class DutySpawnMenuSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    /// <summary>
    /// Сколько чего уже выдано за раунд: сикей (в нижнем регистре) → прототип → количество.
    /// Живёт в памяти системы, а не на игроке: лимит должен переживать смерть, реюз тела
    /// и реконнект, но не рестарт раунда.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, int>> _used = new();

    /// <summary>Переиспользуемый буфер под лукап соседей — чтобы не аллоцировать на каждый клик.</summary>
    private readonly HashSet<Entity<ActorComponent>> _nearby = new();

    public override void Initialize()
    {
        SubscribeNetworkEvent<DutySpawnMenuRequestEvent>(OnRequestState);
        SubscribeNetworkEvent<DutySpawnMenuSpawnEvent>(OnSpawnRequest);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _used.Clear();
    }

    private void OnRequestState(DutySpawnMenuRequestEvent ev, EntitySessionEventArgs args)
    {
        SendState(args.SenderSession);
    }

    private void OnSpawnRequest(DutySpawnMenuSpawnEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (session.AttachedEntity is not { Valid: true } player)
            return;

        var allowed = GetAllowedItems(session.Name);

        if (!allowed.TryGetValue(ev.Proto, out var item))
        {
            // Либо доступа нет вовсе, либо клиент прислал то, чего ему не разрешали.
            _popup.PopupCursor(Loc.GetString("duty-spawn-menu-no-access"), session, PopupType.MediumCaution);
            return;
        }

        if (!_proto.HasIndex<EntityPrototype>(item.Proto))
        {
            _popup.PopupCursor(Loc.GetString("duty-spawn-menu-bad-proto", ("proto", item.Proto.Id)), session, PopupType.MediumCaution);
            return;
        }

        var remaining = GetRemaining(session.Name, item);
        if (remaining == 0)
        {
            _popup.PopupCursor(Loc.GetString("duty-spawn-menu-limit"), session, PopupType.MediumCaution);
            return;
        }

        var coordinates = GetCoordinates(ev.Coordinates);
        if (!coordinates.IsValid(EntityManager))
            return;

        var range = _cfg.GetCVar(DutyCCVars.SpawnMenuRange);
        if (!_xform.InRange(Transform(player).Coordinates, coordinates, range))
        {
            _popup.PopupCursor(Loc.GetString("duty-spawn-menu-too-far", ("range", range)), session, PopupType.MediumCaution);
            return;
        }

        if (GetWitness(player) is { } witness)
        {
            _popup.PopupCursor(
                Loc.GetString("duty-spawn-menu-witness", ("name", Name(witness))),
                session,
                PopupType.MediumCaution);
            return;
        }

        var spawned = Spawn(item.Proto, coordinates);

        var key = session.Name.ToLowerInvariant();
        var used = _used.GetOrNew(key);
        used[item.Proto] = used.GetValueOrDefault(item.Proto.Id) + 1;

        _adminLog.Add(LogType.EntitySpawn, LogImpact.High,
            $"{ToPrettyString(player):player} ({session.Name}) выдал себе {ToPrettyString(spawned):entity} через меню _Duty");

        _popup.PopupEntity(Loc.GetString("duty-spawn-menu-spawned", ("item", Name(spawned))), spawned, session);

        SendState(session);
    }

    /// <summary>
    /// Собирает и отправляет клиенту актуальный список. Если доступа нет — присылаем пустой
    /// список, чтобы клиент показал «доступа нет», а не висел с открытым пустым окном.
    /// </summary>
    private void SendState(ICommonSession session)
    {
        var entries = new List<DutySpawnMenuEntry>();

        foreach (var (protoId, item) in GetAllowedItems(session.Name))
        {
            var name = item.Name;
            if (string.IsNullOrWhiteSpace(name))
                name = _proto.TryIndex<EntityPrototype>(protoId, out var entProto) ? entProto.Name : protoId;

            entries.Add(new DutySpawnMenuEntry
            {
                Proto = protoId,
                Name = name,
                Remaining = GetRemaining(session.Name, item),
            });
        }

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        RaiseNetworkEvent(new DutySpawnMenuStateEvent(entries, _cfg.GetCVar(DutyCCVars.SpawnMenuRange)), session);
    }

    /// <summary>
    /// Объединение всех групп доступа: открытых всем и тех, в которых есть этот сикей. Лимиты одного и того же
    /// предмета из разных групп складываются, безлимит перебивает число.
    /// </summary>
    private Dictionary<string, DutySpawnItem> GetAllowedItems(string ckey)
    {
        var result = new Dictionary<string, DutySpawnItem>();

        foreach (var group in _proto.EnumeratePrototypes<DutySpawnAccessPrototype>())
        {
            if (!group.Everyone &&
                !group.Ckeys.Any(c => string.Equals(c, ckey, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var item in group.Items)
            {
                if (!result.TryGetValue(item.Proto, out var existing))
                {
                    result[item.Proto] = item;
                    continue;
                }

                // Один и тот же предмет из двух групп — берём более щедрый лимит.
                if (existing.Count < 0 || item.Count < 0)
                {
                    result[item.Proto] = new DutySpawnItem { Proto = item.Proto, Count = -1, Name = existing.Name ?? item.Name };
                    continue;
                }

                result[item.Proto] = new DutySpawnItem
                {
                    Proto = item.Proto,
                    Count = existing.Count + item.Count,
                    Name = existing.Name ?? item.Name,
                };
            }
        }

        return result;
    }

    /// <summary>
    /// Ищет живого игрока рядом — при свидетеле выдача запрещена, чтобы предметы не появлялись
    /// у людей на глазах. Гости и мёртвые/критованные не в счёт: они всё равно ничего не расскажут
    /// как участники, а админ-гост иначе блокировал бы выдачу постоянно.
    /// </summary>
    /// <returns>Первый найденный свидетель или null, если рядом чисто.</returns>
    private EntityUid? GetWitness(EntityUid player)
    {
        var range = _cfg.GetCVar(DutyCCVars.SpawnMenuPrivacyRange);
        if (range <= 0f)
            return null;

        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(player).Coordinates, range, _nearby);

        foreach (var candidate in _nearby)
        {
            if (candidate.Owner == player)
                continue;

            if (HasComp<GhostComponent>(candidate))
                continue;

            if (!_mobState.IsAlive(candidate))
                continue;

            return candidate;
        }

        return null;
    }

    /// <returns>Сколько ещё можно выдать; -1 — без ограничения.</returns>
    private int GetRemaining(string ckey, DutySpawnItem item)
    {
        if (item.Count < 0)
            return -1;

        var used = _used.GetValueOrDefault(ckey.ToLowerInvariant())?.GetValueOrDefault(item.Proto.Id) ?? 0;
        return Math.Max(0, item.Count - used);
    }
}
