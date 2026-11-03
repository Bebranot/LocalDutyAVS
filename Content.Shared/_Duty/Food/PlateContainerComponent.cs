// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared.Item;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Food;

/// <summary>
/// _Duty: тарелка — видимо держит несколько предметов еды/питья поверх себя (портировано из
/// Space Onyx). Быстрый «съесть верхний кусок» по использованию в руке, поштучное извлечение
/// через альт-верб.
/// </summary>
[RegisterComponent]
public sealed partial class PlateContainerComponent : Component
{
    public const string ContainerId = "plate-contents";

    [DataField]
    public int MaxItems = 3;

    [DataField]
    public ProtoId<ItemSizePrototype> MaxItemSize = "Normal";

    [DataField]
    public HashSet<ProtoId<TagPrototype>> WhitelistTags = new();

    [DataField]
    public HashSet<ProtoId<TagPrototype>> BlacklistTags = new();

    [DataField]
    public HashSet<EntProtoId> WhitelistPrototypes = new();

    [DataField]
    public HashSet<EntProtoId> BlacklistPrototypes = new();

    public TimeSpan NextPopupTime;

    public EntityUid? LastPopupUser;

    [DataField]
    public List<Vector2> ItemOffsets = new()
    {
        new(0f, 0.04f),
        new(-0.12f, 0.02f),
        new(0.12f, 0.02f),
    };
}
