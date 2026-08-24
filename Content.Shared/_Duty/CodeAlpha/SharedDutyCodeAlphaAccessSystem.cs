// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Access;
using Content.Shared.Access.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.CodeAlpha;

/// <summary>
/// _Duty: выдача полного доступа по коду «Альфа».
///
/// Доступ здесь ВЫВОДИТСЯ, а не записывается. <c>AccessReaderSystem</c> поднимает
/// <see cref="GetAccessTagsEvent"/> на каждой сущности-кандидате, включая само существо
/// (<c>FindPotentialAccessItems</c> добавляет <c>uid</c> в список), поэтому достаточно ответить
/// на это событие набором тегов.
///
/// Так сделано намеренно, вместо перезаписи <c>AccessComponent.Tags</c> через <c>TrySetTags</c>:
/// не нужен снимок прежних тегов у каждого игрока и его восстановление, невозможно потерять
/// чужие теги при гонке с другой системой, не упираемся в <c>[Access(typeof(SharedAccessSystem))]</c>
/// на <c>AccessComponent</c>, и ID-карта игрока остаётся нетронутой. Включение — <c>AddComp</c>,
/// выключение — <c>RemComp</c>, и это одинаково работает на клиенте и на сервере.
/// </summary>
public sealed class SharedDutyCodeAlphaAccessSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    /// <summary>
    /// Уровни доступа, которые код «Альфа» НЕ выдаёт: антагонисты, ЦентКом, космическая полиция,
    /// торговцы и тюрьма. Экипаж получает станцию, но не чужие фракции и не выход из пермы.
    /// </summary>
    public static readonly ProtoId<AccessLevelPrototype>[] Excluded =
    [
        "NuclearOperative",
        "SyndicateAgent",
        "Wizard",
        "CentralCommand",
        "EmergencyShuttleRepealAll",
        "GenpopEnter",
        "GenpopLeave",
        "StationAi",
        "Xenoborg",
        "ADTTrader",
        "SpaceSecArmory",
        "SpaceSecCommand",
        "SpaceSecExternal",
        "SpaceSecMaintenance",
        "SpaceSecOfficial",
        "SpaceSecSecurity",
    ];

    private HashSet<ProtoId<AccessLevelPrototype>>? _cached;

    public override void Initialize()
    {
        SubscribeLocalEvent<DutyCodeAlphaAccessComponent, GetAccessTagsEvent>(OnGetAccessTags);

        _proto.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _proto.PrototypesReloaded -= OnPrototypesReloaded;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<AccessLevelPrototype>())
            _cached = null;
    }

    private void OnGetAccessTags(Entity<DutyCodeAlphaAccessComponent> ent, ref GetAccessTagsEvent args)
    {
        args.Tags.UnionWith(GetAlphaAccess());
    }

    /// <summary>
    /// Все уровни доступа за вычетом <see cref="Excluded"/>. Считается лениво один раз и
    /// сбрасывается при горячей перезагрузке прототипов.
    /// </summary>
    public HashSet<ProtoId<AccessLevelPrototype>> GetAlphaAccess()
    {
        if (_cached != null)
            return _cached;

        var excluded = new HashSet<ProtoId<AccessLevelPrototype>>(Excluded);
        _cached = new HashSet<ProtoId<AccessLevelPrototype>>();

        foreach (var level in _proto.EnumeratePrototypes<AccessLevelPrototype>())
        {
            var id = new ProtoId<AccessLevelPrototype>(level.ID);
            if (!excluded.Contains(id))
                _cached.Add(id);
        }

        return _cached;
    }
}
