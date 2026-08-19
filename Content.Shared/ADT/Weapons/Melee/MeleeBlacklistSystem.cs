using Content.Shared.Damage;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Verbs;
using Content.Shared.Tools.Components;
using Content.Shared.Whitelist;
using Content.Shared.Lock;

namespace Content.Shared.Weapons.Melee.MeleeBlacklist;

public sealed class MeleeBlacklistSystem : EntitySystem
{
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<MeleeBlacklistComponent, AttemptMeleeEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(EntityUid uid, MeleeBlacklistComponent comp, ref AttemptMeleeEvent args)
    {
        // _Duty: было `IsWhitelistPassOrNull` — эта вариация считает "прошёл"
        // ещё и когда `Blacklist == null` (см. EntityWhitelistSystem: OrNull-
        // варианты трактуют отсутствие списка как безусловное совпадение).
        // Для чёрного списка это ровно наоборот: отсутствие `Blacklist`
        // должно означать "ограничения нет", а не "отменить любую атаку".
        // Сейчас оба существующих yml-использования (`security.yml`,
        // `personalization_items.yml`) всегда явно задают `blacklist:`, так
        // что баг не проявлялся — но любая будущая сущность с этим
        // компонентом, задающая только `whitelist:` без `blacklist:`, теряла
        // бы возможность атаковать вообще чем-либо. `IsWhitelistPass`
        // корректно возвращает false при `Blacklist == null`.
        if (_whitelist.IsWhitelistPass(comp.Blacklist, args.User))
        {
            args.Cancelled = true;
            return;
        }
        if (_whitelist.IsWhitelistFail(comp.Whitelist, args.User))
        {
            args.Cancelled = true;
            return;
        }
    }
}
