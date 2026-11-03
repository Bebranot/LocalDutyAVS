// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Weapons.Halberd;

namespace Content.Client._Duty.Weapons.Halberd;

/// <summary>
/// _Duty: клиент предсказывает свой рывок алебардой целиком в <see cref="SharedHalberdChargeSystem"/> —
/// урон, реплики и зацикленный звук остаются на сервере.
/// </summary>
public sealed partial class HalberdChargeSystem : SharedHalberdChargeSystem;
