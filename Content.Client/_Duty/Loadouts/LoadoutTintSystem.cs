// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx без изменений.

using Content.Client.Clothing;
using Content.Shared._Duty.Loadouts;
using Content.Shared.Clothing;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Client._Duty.Loadouts;

public sealed partial class LoadoutTintSystem : EntitySystem
{
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<LoadoutTintComponent, ComponentInit>(OnTintChanged);
        SubscribeLocalEvent<LoadoutTintComponent, AfterAutoHandleStateEvent>(OnTintChanged);
        SubscribeLocalEvent<LoadoutTintComponent, GetEquipmentVisualsEvent>(OnGetEquipmentVisuals, after: new[] { typeof(ClientClothingSystem) });
    }

    private void OnTintChanged<T>(Entity<LoadoutTintComponent> ent, ref T args)
    {
        ApplyItemTint(ent);
        _item.VisualsChanged(ent);
    }

    private static void OnGetEquipmentVisuals(Entity<LoadoutTintComponent> ent, ref GetEquipmentVisualsEvent args)
    {
        foreach (var (_, layer) in args.Layers)
            layer.Color = ent.Comp.Color;
    }

    public void SetTint(EntityUid uid, Color color)
    {
        var tint = EnsureComp<LoadoutTintComponent>(uid);
        tint.Color = color;
        Dirty(uid, tint);
        ApplyItemTint((uid, tint));
        _item.VisualsChanged(uid);
    }

    private void ApplyItemTint(Entity<LoadoutTintComponent> ent)
    {
        if (TryComp(ent, out SpriteComponent? sprite))
            _sprite.SetColor((ent, sprite), ent.Comp.Color);
    }
}
