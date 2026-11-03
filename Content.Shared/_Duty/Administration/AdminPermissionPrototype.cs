// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Administration;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Administration;

/// <summary>
/// Насколько узел права опасен: от него зависит цвет в панели выдачи прав.
/// </summary>
public enum AdminPermissionDanger : byte
{
    /// <summary>Только смотреть, ничего не менять.</summary>
    View,

    /// <summary>Обычное действие модератора или админа.</summary>
    Action,

    /// <summary>Можно сломать сервер, раунд или права других админов.</summary>
    Danger,
}

/// <summary>
/// Узел дерева админ-прав («флаг» или «подфлаг» в панели). Выдача родителя выдаёт и всех потомков.
/// Id хранится в БД строкой (<c>admin_flag.flag</c>) как есть, поэтому переименовывать уже выданные узлы нельзя.
/// Название и описание берутся из локали: <c>duty-perm-{id с '_' → '-'}</c> и <c>.desc</c>.
/// </summary>
[Prototype]
public sealed partial class AdminPermissionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Родитель в дереве. Пусто у корневых областей. В YAML ключ называется <c>group</c>: <c>parent</c> зарезервирован
    /// движком под наследование прототипов и ломает валидацию.
    /// </summary>
    [DataField("group")]
    public string? Parent { get; private set; }

    /// <summary>Порядок среди «братьев», меньше — выше.</summary>
    [DataField]
    public int Order { get; private set; }

    [DataField]
    public AdminPermissionDanger Danger { get; private set; } = AdminPermissionDanger.Action;

    /// <summary>
    /// Старые флаги, которые узел включает только для внутренних проверок (<c>HasAdminFlag</c> в системах).
    /// Консольные команды из-за них не открываются: команды открывает только список <see cref="Commands"/>.
    /// </summary>
    [DataField]
    public List<string> Legacy { get; private set; } = new();

    /// <summary>Консольные команды, которые открывает этот узел.</summary>
    [DataField]
    public List<string> Commands { get; private set; } = new();

    /// <summary>Toolshed-команды, которые открывает этот узел.</summary>
    [DataField]
    public List<string> Toolshed { get; private set; } = new();

    public AdminFlags LegacyFlags
    {
        get
        {
            var f = AdminFlags.None;
            foreach (var l in Legacy)
            {
                if (Enum.TryParse<AdminFlags>(l, true, out var parsed))
                    f |= parsed;
            }

            return f;
        }
    }

    /// <summary>Ключ локали названия.</summary>
    public string NameLoc => LocKey(ID);

    /// <summary>Ключ локали описания.</summary>
    public string DescLoc => LocKey(ID) + ".desc";

    public static string LocKey(string id) => "duty-perm-" + id.Replace('_', '-');
}
