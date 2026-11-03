// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using Content.Server.Administration;
using Content.Server.Shuttles.Systems;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Content.Shared.Shuttles.Components;

namespace Content.Server._Duty.Shuttles;

/// <summary>
/// _Duty: ивентовый прыжок шаттла к ЦК — без диска координат и прочих ограничений консоли.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class DutyFtlCentcommCommand : DutyEventFtlCommandBase
{
    public override string Command => "dutyftlcentcomm";
    protected override DutyEventFtlTarget Target => DutyEventFtlTarget.Centcomm;
}

/// <summary>
/// _Duty: обратный ивентовый прыжок — от ЦК (или откуда угодно) к станции.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class DutyFtlStationCommand : DutyEventFtlCommandBase
{
    public override string Command => "dutyftlstation";
    protected override DutyEventFtlTarget Target => DutyEventFtlTarget.Station;
}

/// <summary>
/// Общий разбор аргументов: <c>[время_в_гиперпространстве|-] [грид_или_сущность_на_нём]</c>.
/// Без второго аргумента берём грид, на котором стоит сам админ, — так удобнее на ивенте.
/// </summary>
public abstract partial class DutyEventFtlCommandBase : LocalizedEntityCommands
{
    [Dependency] private DutyEventFtlSystem _eventFtl = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    protected abstract DutyEventFtlTarget Target { get; }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint(Loc.GetString("cmd-dutyftl-hint-time",
                ("min", _eventFtl.MinTravelTime), ("max", DutyEventFtlSystem.MaxTravelTime))),
            2 => CompletionResult.FromHint(Loc.GetString("cmd-dutyftl-hint-grid")),
            _ => CompletionResult.Empty,
        };
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        var travelTime = _eventFtl.DefaultTravelTime;
        if (args.Length >= 1 && args[0] != "-")
        {
            if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out travelTime)
                || !float.IsFinite(travelTime))
            {
                shell.WriteError(Loc.GetString("cmd-dutyftl-bad-time", ("value", args[0])));
                return;
            }

            var clamped = Math.Clamp(travelTime, _eventFtl.MinTravelTime, DutyEventFtlSystem.MaxTravelTime);
            if (!MathHelper.CloseTo(clamped, travelTime))
            {
                shell.WriteLine(Loc.GetString("cmd-dutyftl-time-clamped", ("value", clamped)));
                travelTime = clamped;
            }
        }

        if (!TryGetShuttle(shell, args, out var shuttle))
            return;

        // Запрет на FTL (PreventFTL, лимит массы) админ перебивает, но пусть видит, что перебил.
        if (!_shuttle.CanFTL(shuttle, out var reason) && !EntityManager.HasComponent<FTLComponent>(shuttle))
            shell.WriteLine(Loc.GetString("cmd-dutyftl-override", ("reason", reason)));

        if (!_eventFtl.TryStart(shuttle, Target, travelTime, out var destination, out var anchor, out var error))
        {
            shell.WriteError(error);
            return;
        }

        var position = _transform.ToMapCoordinates(destination).Position;
        var anchorPosition = _transform.GetWorldPosition(anchor);

        shell.WriteLine(Loc.GetString("cmd-dutyftl-started",
            ("grid", EntityManager.GetComponent<MetaDataComponent>(shuttle).EntityName),
            ("anchor", EntityManager.GetComponent<MetaDataComponent>(anchor).EntityName),
            ("x", MathF.Round(position.X)),
            ("y", MathF.Round(position.Y)),
            ("distance", MathF.Round((position - anchorPosition).Length())),
            ("time", MathF.Round(travelTime))));
    }

    private bool TryGetShuttle(IConsoleShell shell, string[] args, out EntityUid shuttle)
    {
        shuttle = default;
        EntityUid? target;

        if (args.Length == 2)
        {
            if (!NetEntity.TryParse(args[1], out var net) || !EntityManager.TryGetEntity(net, out target))
            {
                shell.WriteError(Loc.GetString("shell-invalid-entity-id"));
                return false;
            }
        }
        else
        {
            target = shell.Player?.AttachedEntity;
            if (target == null)
            {
                shell.WriteError(Loc.GetString("cmd-dutyftl-no-attached"));
                return false;
            }
        }

        // Можно указать сам грид или любую сущность на нём (консоль, игрока).
        if (EntityManager.GetComponent<TransformComponent>(target.Value).GridUid is not { } grid)
        {
            shell.WriteError(Loc.GetString("cmd-dutyftl-not-on-grid"));
            return false;
        }

        shuttle = grid;
        return true;
    }
}
