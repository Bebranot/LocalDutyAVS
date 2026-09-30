using System;
using Content.Server.Popups;
using Content.Shared._Duty;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Duty.DevourerHeart;

/// <summary>
/// Handles eating the Devourer's heart: grants permanent slow all-type healing.
/// </summary>
public sealed class DevourerHeartSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DutyDevourerHeartComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(EntityUid uid, DutyDevourerHeartComponent comp, UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (!HasComp<DamageableComponent>(args.User))
            return;

        if (HasComp<DutyDevourerHeartRegenComponent>(args.User))
        {
            _popup.PopupEntity(Loc.GetString("duty-devourer-heart-already"), args.User, args.User, PopupType.Medium);
            return;
        }

        args.Handled = true;
        var regen = EnsureComp<DutyDevourerHeartRegenComponent>(args.User);
        regen.LastHeal = _timing.CurTime;
        _popup.PopupEntity(Loc.GetString("duty-devourer-heart-use"), args.User, args.User, PopupType.Large);
        _audio.PlayGlobal(new SoundPathSpecifier("/Audio/_Duty/Effects/DevourerScream.ogg"), args.User);
        QueueDel(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<DutyDevourerHeartRegenComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_timing.CurTime < comp.LastHeal + TimeSpan.FromSeconds(comp.HealInterval))
                continue;

            // Таймер двигаем и у мёртвых — иначе проверка IsDead шла бы каждый тик.
            comp.LastHeal = _timing.CurTime;

            if (_mobState.IsDead(uid))
                continue;

            foreach (var group in comp.HealGroups)
            {
                _damage.TryChangeDamage(uid, new DamageSpecifier(_proto.Index<DamageGroupPrototype>(group), comp.HealAmount), true, false);
            }
        }
    }
}