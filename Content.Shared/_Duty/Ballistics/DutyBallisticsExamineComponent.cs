// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Ballistics;

/// <summary>
/// _Duty: маркер патрона, при осмотре которого показываются баллистические свойства его пули.
/// Отдельный маркер нужен потому, что пара (CartridgeAmmoComponent, ExaminedEvent) уже занята
/// ванильным SharedGunSystem, а вторая directed-подписка на ту же пару роняет сервер на старте.
/// </summary>
[RegisterComponent]
public sealed partial class DutyBallisticsExamineComponent : Component;
