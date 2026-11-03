// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx. Отдельный partial-файл (как их CCVars.LoadoutPersonalization.cs)
// вместо DutyCCVars.cs — это не duty.*-cvar, а продолжение уже существующего ванильного семейства
// ic.loadout_name_length (см. CCVars.Ic.cs).

using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    /// Максимальная длина кастомного имени предмета лоадаута.
    /// </summary>
    public static readonly CVarDef<int> MaxCustomLoadoutNameLength =
        CVarDef.Create("ic.custom_loadout_name_length", 100, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Максимальная длина кастомного описания предмета лоадаута.
    /// </summary>
    public static readonly CVarDef<int> MaxCustomLoadoutDescriptionLength =
        CVarDef.Create("ic.custom_loadout_description_length", 512, CVar.SERVER | CVar.REPLICATED);
}
