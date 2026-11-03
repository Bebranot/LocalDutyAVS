// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Recoil;

/// <summary>
/// Мощность отдачи калибра, привязанная к тегу патрона (STCartridge545, ShellShotgun, …).
/// Таблица живёт отдельно от патронов: калибр определяется тегом, который у патронов и так есть
/// (по нему магазины принимают патрон), — не нужно дописывать компонент в каждый ammo_*.yml.
/// Патроны без тега из таблицы получают мощность по урону пули (см. <see cref="SharedDutyRecoilSystem"/>).
/// </summary>
[Prototype]
public sealed partial class DutyCaliberRecoilPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Тег калибра на патроне.</summary>
    [DataField(required: true)]
    public ProtoId<TagPrototype> Tag;

    /// <summary>
    /// Относительный импульс отдачи (пуля + пороховые газы), 7.62x39 = 1.0.
    /// Масса и эргономика ствола сюда не входят — это cameraRecoilScalar оружия.
    /// </summary>
    [DataField(required: true)]
    public float Power = 1f;
}
