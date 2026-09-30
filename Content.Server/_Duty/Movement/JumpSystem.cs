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
using Robust.Shared.Player;
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
            !_random.Prob(_random.NextFloat(ent.Comp.StumbleChanceMin, ent.Comp.StumbleChanceMax)))
            return;

        // Приземление обрабатывается только здесь, на сервере, — клиент его не предсказывает.
        // Поэтому PopupPredicted не годится: он не шлёт сообщение самому прыгнувшему, считая, что
        // тот уже показал его у себя, и игрок так и не узнавал, почему упал.
        _popup.PopupEntity(Loc.GetString("jump-stumble-self"), ent, ent, PopupType.MediumCaution);
        _popup.PopupEntity(
            Loc.GetString("jump-stumble-others", ("jumper", Identity.Entity(ent, EntityManager))),
            ent,
            Filter.PvsExcept(ent, entityManager: EntityManager),
            true,
            PopupType.MediumCaution);
        _stun.TryKnockdown((ent.Owner, null), ent.Comp.StumbleDuration, force: true);
    }
}
