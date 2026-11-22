// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Duty.Cyberware.Mercenary;

/// <summary>
/// _Duty: игровое правило «контракт наёмника». Одно правило — один контракт на одну цель.
/// Запускается админ-командой <c>contract</c>: цель и точку в космосе ставит команда до старта правила,
/// гост-роль спавнит ванильный AntagSelection, тело появляется, только когда роль взяли.
/// </summary>
[RegisterComponent, Access(typeof(DutyMercenaryRuleSystem))]
public sealed partial class DutyMercenaryRuleComponent : Component
{
    /// <summary>На сколько тайлов дальше края станции появляется наёмник.</summary>
    [DataField]
    public float SpawnDistance = 25f;

    /// <summary>Импланты, которые ставятся при спавне. Первый с DutyMercContractImplant — ядро.</summary>
    [DataField]
    public List<EntProtoId> Implants = new();

    [DataField]
    public EntProtoId Objective = "DutyMercenaryKillContractObjective";

    [ViewVariables]
    public EntityUid? TargetMind;

    [ViewVariables]
    public string TargetName = string.Empty;

    [ViewVariables]
    public MapCoordinates? Coords;

    [ViewVariables]
    public EntityUid? Mercenary;
}
