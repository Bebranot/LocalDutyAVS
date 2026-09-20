// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx.

namespace Content.Shared._Duty.Traits;

/// <summary>Одна жизнь: смерть означает гиб, без возможности реанимации/клонирования.</summary>
[RegisterComponent]
public sealed partial class OneLifeComponent : Component;
