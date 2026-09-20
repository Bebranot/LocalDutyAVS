// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: персонализация лоадаутов (кастомные имя/описание/цвет предмета), портировано из
// Space Onyx и адаптировано под наш UI-фреймворк лоадаутов (LoadoutWindow/LoadoutGroupContainer/
// LoadoutContainer) вместо их собственного (LoadoutIconButton/LoadoutWrapContainer/tab-поиск).

using System.Linq;
using Content.Client._Duty.Loadouts;
using Content.Client.Lobby.UI.Loadouts;
using Content.Shared.CCVar;
using Content.Shared.Clothing;
using Content.Shared.Inventory;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private void OpenLoadoutCustomization(
        ProtoId<LoadoutGroupPrototype> group,
        ProtoId<LoadoutPrototype> protoId,
        RoleLoadout roleLoadout,
        Robust.Shared.Player.ICommonSession session,
        IDependencyCollection collection)
    {
        if (Profile == null || !_prototypeManager.TryIndex(protoId, out LoadoutPrototype? prototype))
            return;

        var selected = roleLoadout.SelectedLoadouts.GetValueOrDefault(group)?.FirstOrDefault(item => item.Prototype == protoId);

        var loadoutSystem = _entManager.System<LoadoutSystem>();
        var entity = prototype.DummyEntity ?? loadoutSystem.GetFirstOrNull(prototype);
        var defaultName = loadoutSystem.GetName(prototype);
        var defaultDescription = string.Empty;
        if (entity != null)
        {
            var preview = _entManager.SpawnEntity(entity, Robust.Shared.Map.MapCoordinates.Nullspace);
            if (_entManager.TryGetComponent(preview, out MetaDataComponent? metadata))
            {
                defaultName = metadata.EntityName;
                defaultDescription = metadata.EntityDescription;
            }
            _entManager.DeleteEntity(preview);
        }

        var savedColor = Color.FromHex(selected?.CustomColorTint ?? Color.White.ToHex());
        var window = new LoadoutCustomizeWindow(
            defaultName,
            selected?.CustomName ?? defaultName,
            selected?.CustomDescription ?? defaultDescription,
            prototype.CustomColorTint ? savedColor : null,
            _cfgManager.GetCVar(CCVars.MaxCustomLoadoutNameLength),
            _cfgManager.GetCVar(CCVars.MaxCustomLoadoutDescriptionLength));

        window.OnSubmitted += (name, description, color) =>
        {
            SetPersonalizedLoadoutCustomization(
                group,
                protoId,
                roleLoadout,
                session,
                collection,
                NormalizeLoadoutText(name, defaultName),
                NormalizeLoadoutText(description, defaultDescription),
                color?.ToHex());
        };

        if (prototype.CustomColorTint)
        {
            window.OnColorPreview += color => PreviewLoadoutTint(prototype, color);
            window.OnReverted += () => PreviewLoadoutTint(prototype, savedColor);
        }

        window.OpenCentered();
    }

    private void SetPersonalizedLoadoutCustomization(
        ProtoId<LoadoutGroupPrototype> group,
        ProtoId<LoadoutPrototype> protoId,
        RoleLoadout roleLoadout,
        Robust.Shared.Player.ICommonSession session,
        IDependencyCollection collection,
        string? name,
        string? description,
        string? tint)
    {
        if (Profile == null || !_prototypeManager.TryIndex(protoId, out LoadoutPrototype? prototype))
            return;

        if (!roleLoadout.SelectedLoadouts.TryGetValue(group, out var selected))
        {
            selected = new List<Loadout>();
            roleLoadout.SelectedLoadouts[group] = selected;
        }

        var item = selected.FirstOrDefault(entry => entry.Prototype == protoId);
        if (item == null)
        {
            // Предмет ещё не выбран — выбираем его вместе с персонализацией.
            if (!roleLoadout.IsValid(Profile, session, protoId, collection, out _))
                return;

            roleLoadout.AddLoadout(group, protoId, _prototypeManager);
            item = selected.FirstOrDefault(entry => entry.Prototype == protoId);
            if (item == null)
                return;
        }

        item.CustomName = name;
        item.CustomDescription = description;
        item.CustomColorTint = prototype.CustomColorTint ? tint : null;

        Profile = Profile.WithLoadout(roleLoadout);
        SetDirty();
        _loadoutWindow?.RefreshLoadouts(roleLoadout, session, collection);
        ReloadPreview();
    }

    private static string? NormalizeLoadoutText(string value, string defaultValue)
    {
        var text = value.Trim();
        return string.IsNullOrEmpty(text) || text == defaultValue.Trim() ? null : text;
    }

    private void PreviewLoadoutTint(LoadoutPrototype prototype, Color color)
    {
        if (!_entManager.EntityExists(SpriteView.PreviewDummy))
            return;

        var inventory = _entManager.System<InventorySystem>();
        var tint = _entManager.System<LoadoutTintSystem>();
        foreach (var slot in prototype.Equipment.Keys)
        {
            if (inventory.TryGetSlotEntity(SpriteView.PreviewDummy, slot, out var item))
                tint.SetTint(item.Value, color);
        }
    }
}
