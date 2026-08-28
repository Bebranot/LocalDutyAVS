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
/// Компонент вешается на ID-карту. Доступ при этом ВЫВОДИТСЯ, а не записывается:
/// <c>AccessReaderSystem</c> поднимает <see cref="GetAccessTagsEvent"/> на каждой
/// сущности-кандидате — на карте в слоте, на карте в руке и на карте внутри КПК (её подкидывает
/// <c>SharedPdaSystem</c> через <c>GetAdditionalAccessEvent</c>), — поэтому достаточно ответить
/// на это событие набором тегов.
///
/// Так сделано намеренно, вместо перезаписи <c>AccessComponent.Tags</c> через <c>TrySetTags</c>:
/// не нужен снимок прежних тегов карты и его восстановление, невозможно потерять родные теги при
/// гонке с другой системой (например с консолью ID), не упираемся в
/// <c>[Access(typeof(SharedAccessSystem))]</c> на <c>AccessComponent</c>, и карта после снятия
/// кода остаётся ровно такой, какой была. Включение — <c>AddComp</c>, выключение —
/// <c>RemComp</c>, и это одинаково работает на клиенте и на сервере.
/// </summary>
public sealed class SharedDutyCodeAlphaAccessSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    /// <summary>
    /// Уровни доступа, которые код «Альфа» НЕ выдаёт: антагонисты, ЦентКом, космическая полиция,
    /// торговцы, тюрьма и силиконовая «личность».
    ///
    /// Про силикон отдельно: <c>Borg</c> и <c>BasicSilicon</c> людям не открывают ничего, зато
    /// <c>TurretTargetSettingsSystem.EntityIsTargetForTurret</c> проверяет их ПЕРВЫМИ и обрывает
    /// проверку — человек с этими тегами перестал бы быть целью вообще для всех турелей в игре,
    /// потому что каждая из них их исключает. Не выдаём, чтобы Альфа не трогала логику турелей.
    /// </summary>
    private static readonly ProtoId<AccessLevelPrototype>[] Excluded =
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
        "Borg",
        "BasicSilicon",
        "Ipc",
    ];

    private HashSet<ProtoId<AccessLevelPrototype>>? _cached;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyCodeAlphaAccessComponent, GetAccessTagsEvent>(OnGetAccessTags);

        // Через шину, а не через _proto.PrototypesReloaded: временем жизни подписки тогда
        // распоряжается шина, и парный Shutdown с ручной отпиской становится не нужен. Так же это
        // сделано в AlertLevelSystem, DamageableSystem и ещё десятке систем репозитория.
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
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
    private HashSet<ProtoId<AccessLevelPrototype>> GetAlphaAccess()
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
