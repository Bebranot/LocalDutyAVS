// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;

// _Duty: в RMC это ванильный компонент (в нашей ветке ванилы его нет). Им броня-хардпоинты
// (ДЗ «Реликт») навешивают на корпус наборы модификаторов урона. Если апстрим его добавит —
// удалить этот файл, имена совпадают.
namespace Content.Shared.Damage.Components
{
    /// <summary>
    /// Применяет указанные наборы модификаторов к входящему урону.
    /// </summary>
    [RegisterComponent, NetworkedComponent]
    public sealed partial class DamageProtectionBuffComponent : Component
    {
        [DataField]
        public Dictionary<string, DamageModifierSetPrototype> Modifiers = new();
    }
}

namespace Content.Shared.Damage.Systems
{
    using Content.Shared.Damage.Components;

    public sealed class DamageProtectionBuffSystem : EntitySystem
    {
        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<DamageProtectionBuffComponent, DamageModifyEvent>(OnDamageModify);
        }

        private void OnDamageModify(Entity<DamageProtectionBuffComponent> ent, ref DamageModifyEvent args)
        {
            foreach (var modifier in ent.Comp.Modifiers.Values)
                args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage, modifier);
        }
    }
}
