// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Heartbeat;
using Content.Shared.Damage.Events;
using Content.Shared.Examine;
using Content.Shared.Mobs.Systems;

namespace Content.Shared._Duty.Cyberware.Security;

/// <summary>
/// _Duty: предсказуемые эффекты СБ-имплантов — рефлекторный блок (стамина) и стресс-анализатор (осмотр пульса).
/// Выставление компонентов на носителе — серверная <c>DutySecurityCyberSystem</c>.
/// </summary>
public sealed partial class DutySecurityCyberSharedSystem : EntitySystem
{
    [Dependency] private SharedDutyCyberwareSystem _cyber = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyReflexBlockHostComponent, BeforeStaminaDamageEvent>(OnBeforeStamina);
        SubscribeLocalEvent<HeartbeatComponent, ExaminedEvent>(OnHeartbeatExamined);
    }

    private void OnBeforeStamina(Entity<DutyReflexBlockHostComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        // Под ЭМИ нейрошунт не работает.
        if (_cyber.IsHostEmped(ent))
            return;

        args.Value *= ent.Comp.Coefficient;
    }

    private void OnHeartbeatExamined(Entity<HeartbeatComponent> ent, ref ExaminedEvent args)
    {
        // Только чужого и только вблизи; свой пульс чувствуешь и так.
        if (args.Examiner == ent.Owner || !args.IsInDetailsRange)
            return;

        if (!HasComp<DutyStressAnalyzerHostComponent>(args.Examiner) || _cyber.IsHostEmped(args.Examiner))
            return;

        // Это волнение, а не детектор лжи: показываем только уровень пульса.
        string key;
        if (_mobState.IsDead(ent))
            key = "duty-sec-stress-dead";
        else
            key = ent.Comp.Level switch
            {
                HeartbeatLevel.Light => "duty-sec-stress-light",
                HeartbeatLevel.Heavy => "duty-sec-stress-heavy",
                HeartbeatLevel.Critical => "duty-sec-stress-critical",
                _ => "duty-sec-stress-calm",
            };

        args.PushMarkup(Loc.GetString(key, ("target", ent.Owner)));
    }
}
