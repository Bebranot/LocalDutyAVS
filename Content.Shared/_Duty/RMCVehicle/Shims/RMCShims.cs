// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

// Заглушки для систем RMC-14, которых у нас нет (ксеноморфы, скиллы морпехов, размеры).
// Код техники RMC перенесён почти без правок, чтобы проще забирать их исправления;
// вместо выпиливания каждой ксено-ветки здесь объявлены пустые типы — ветки остаются мёртвыми.
// Если ADT когда-нибудь портирует настоящие типы с этими именами, сборка упадёт на дублях —
// тогда просто удалить соответствующую заглушку.

using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Marines.Skills
{
    /// <summary>
    /// У нас нет системы навыков: требования к навыкам в прототипах техники игнорируются.
    /// </summary>
    [DataDefinition]
    [Serializable, NetSerializable]
    public sealed partial class SkillWhitelist
    {
        [DataField]
        public Dictionary<string, int> All = new();
    }

    public sealed class SkillsSystem : EntitySystem
    {
        // Танк может собрать и вести кто угодно — навыки всегда «есть».
        public bool HasSkills(EntityUid user, SkillWhitelist whitelist) => true;

        public float GetSkillDelayMultiplier(EntityUid user, string definition, float[]? multipliers = null) => 1f;
    }
}

namespace Content.Shared._RMC14.Stun
{
    [Serializable, NetSerializable]
    public enum RMCSizes : byte
    {
        Small,
        Humanoid,
        VerySmallXeno,
        SmallXeno,
        Xeno,
        Big,
        Immobile,
    }
}

namespace Content.Shared._RMC14.Entrenching
{
    /// <summary>
    /// Баррикада RMC (мешки с песком). У нас таких нет — маркер, чтобы логика столкновений компилировалась.
    /// </summary>
    [RegisterComponent]
    public sealed partial class BarricadeComponent : Component;
}

namespace Content.Shared._RMC14.Xenonids
{
    /// <summary>
    /// Маркер ксеноморфа. У нас его никто не носит — ксено-ветки кода техники не срабатывают.
    /// </summary>
    [RegisterComponent, NetworkedComponent]
    public sealed partial class XenoComponent : Component;
}

namespace Content.Shared._RMC14.Xenonids.Rest
{
    [RegisterComponent]
    public sealed partial class XenoRestingComponent : Component;
}

namespace Content.Shared._RMC14.Xenonids.Burrow
{
    [RegisterComponent]
    public sealed partial class XenoBurrowComponent : Component
    {
        [DataField]
        public bool Active;
    }
}

namespace Content.Shared._RMC14.Xenonids.Weeds
{
    [RegisterComponent]
    public sealed partial class XenoWeedsComponent : Component;
}

namespace Content.Shared._RMC14.PowerLoader
{
    /// <summary>
    /// Маркер «поднимается только погрузчиком». На наших хардпоинтах не используется.
    /// </summary>
    [RegisterComponent]
    public sealed partial class PowerLoaderGrabbableComponent : Component;
}
