// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Neuro;
using Content.Shared._Duty.Neurointerface;
using Content.Shared.Implants;
using Content.Shared.Pinpointer;
using Content.Shared.Popups;
using Robust.Shared.Map;

namespace Content.Server._Duty.Cyberware.Neuro;

public sealed partial class DutyNeuroImplantSystem
{
    private void InitializeNavigator()
    {
        SubscribeLocalEvent<DutyNavigatorImplantComponent, DutyNavigatorOpenMenuEvent>(OnNavigatorOpen);
        SubscribeLocalEvent<DutyNavigatorImplantComponent, DutyNavigatorSelectMessage>(OnNavigatorSelect);
        SubscribeLocalEvent<DutyNavigatorImplantComponent, ImplantRemovedEvent>(OnNavigatorRemoved);
        SubscribeLocalEvent<DutyNavigatorImplantComponent, DutyCyberEmpEvent>(OnNavigatorEmp);
    }

    private void OnNavigatorOpen(Entity<DutyNavigatorImplantComponent> ent, ref DutyNavigatorOpenMenuEvent args)
    {
        if (args.Handled || !CheckReady(ent.Owner, args.Performer))
            return;

        _ui.OpenUi(ent.Owner, DutyNavigatorUiKey.Key, args.Performer);
        args.Handled = true;
    }

    private void OnNavigatorSelect(Entity<DutyNavigatorImplantComponent> ent, ref DutyNavigatorSelectMessage args)
    {
        if (!_cyber.TryGetHost(ent.Owner, out var host) || host != args.Actor)
            return;

        _ui.CloseUi(ent.Owner, DutyNavigatorUiKey.Key, args.Actor);

        if (args.Index < 0 || args.Index >= ent.Comp.Destinations.Count)
        {
            RemComp<DutyNavigatorComponent>(host);
            _cyber.SetModuleDetail(ent.Owner, string.Empty);
            return;
        }

        if (!_cyber.IsModuleOnline(ent.Owner))
        {
            Deny(host, "duty-neuro-implant-offline");
            return;
        }

        var dest = ent.Comp.Destinations[args.Index];
        var label = Loc.GetString(dest.Name);
        if (!TryFindBeacon(host, dest, out var coords, out var color))
        {
            Deny(host, "duty-navigator-no-route");
            return;
        }

        var nav = EnsureComp<DutyNavigatorComponent>(host);
        nav.Target = GetNetCoordinates(coords);
        nav.Label = label;
        nav.Color = color;
        Dirty(host, nav);

        _cyber.SetModuleDetail(ent.Owner, label);
        _popup.PopupEntity(Loc.GetString("duty-navigator-route-set", ("name", label)), host, host, PopupType.Small);
    }

    private void OnNavigatorRemoved(Entity<DutyNavigatorImplantComponent> ent, ref ImplantRemovedEvent args)
    {
        RemComp<DutyNavigatorComponent>(args.Implanted);
    }

    // Под ЭМИ маршрут сбрасывается: после сбоя точку надо выбрать заново, клиент и так прячет стрелку на время ЭМИ.
    private void OnNavigatorEmp(Entity<DutyNavigatorImplantComponent> ent, ref DutyCyberEmpEvent args)
    {
        if (RemComp<DutyNavigatorComponent>(args.Host))
            _cyber.SetModuleDetail(ent.Owner, string.Empty);
    }

    /// <summary>
    /// Ближайший маяк точки на гриде носителя. Сначала «главные» маяки по прототипу, потом — любой маяк цвета отдела:
    /// на нестандартных картах главного маяка может не быть.
    /// </summary>
    private bool TryFindBeacon(EntityUid host, DutyNavigatorDestination dest, out EntityCoordinates coords, out Color color)
    {
        coords = default;
        color = Color.FromHex("#3DDCFF");

        var hostXform = Transform(host);
        if (hostXform.GridUid is not { } grid)
            return false;

        var hostPos = _transform.GetWorldPosition(hostXform);
        var bestDist = float.MaxValue;
        var bestByColorDist = float.MaxValue;
        EntityUid? best = null;
        EntityUid? bestByColor = null;
        var bestColor = color;
        var bestByColorColor = color;

        var query = EntityQueryEnumerator<NavMapBeaconComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var beacon, out var xform, out var meta))
        {
            if (!beacon.Enabled || xform.GridUid != grid)
                continue;

            var dist = (_transform.GetWorldPosition(xform) - hostPos).LengthSquared();
            var protoId = meta.EntityPrototype?.ID;

            if (protoId != null && dest.Beacons.Contains(protoId))
            {
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = uid;
                    bestColor = beacon.Color;
                }

                continue;
            }

            if (dest.BeaconColor is { } wanted && ColorsClose(beacon.Color, wanted) && dist < bestByColorDist)
            {
                bestByColorDist = dist;
                bestByColor = uid;
                bestByColorColor = beacon.Color;
            }
        }

        if ((best ?? bestByColor) is not { } found)
            return false;

        coords = _transform.GetMoverCoordinates(found);
        color = best != null ? bestColor : bestByColorColor;
        return true;
    }

    private static bool ColorsClose(Color a, Color b)
    {
        const float eps = 0.02f;
        return MathF.Abs(a.R - b.R) < eps && MathF.Abs(a.G - b.G) < eps && MathF.Abs(a.B - b.B) < eps;
    }
}
