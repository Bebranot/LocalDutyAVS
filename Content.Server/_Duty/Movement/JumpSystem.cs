// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Tiles;
using Content.Shared._Duty.Movement;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Random;

namespace Content.Server._Duty.Movement;

/// <summary>
/// _Duty: серверная часть прыжка, портировано из Space Onyx. Оригинал проигрывал прыжок через
/// собственный <c>AnimationPlayerSystem</c> (у нас его нет — не переносили отдельно) — визуал
/// прыжка у нас полностью клиентский, через <see cref="Content.Client._Duty.Movement.JumpSystem"/>
/// и тень под ногами; здесь остаётся только геймплейная логика (спотыкание при приземлении,
/// блокировка степ-триггеров тайловых эффектов).
/// </summary>
public sealed partial class JumpSystem : SharedJumpSystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    protected override bool IsStepTriggerBlocked(EntityUid source)
    {
        return HasComp<TileEntityEffectComponent>(source);
    }

    protected override void OnJumpLanded(Entity<JumpComponent> ent)
    {
        if (!_mobState.IsAlive(ent) ||
            _standing.IsDown((ent.Owner, null)) ||
            !_random.Prob(_random.Next(5, 11) / 100f))
            return;

        _popup.PopupPredicted(
            Loc.GetString("jump-stumble-self"),
            Loc.GetString("jump-stumble-others", ("jumper", Identity.Entity(ent, EntityManager))),
            ent,
            ent,
            PopupType.MediumCaution);
        _stun.TryKnockdown((ent.Owner, null), TimeSpan.FromSeconds(2), force: true);
    }
}
