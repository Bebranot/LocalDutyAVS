// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using Content.Server.GameTicking;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Gorlex;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Implants;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Duty.Cyberware.Gorlex;

/// <summary>
/// _Duty: «Чёрный ящик» — зеркало флэтлайна NT. Пока носитель жив, ящик копит буфер «зон» маршрута;
/// при смерти шлёт в канал оперативников место, убийцу и время. Труп с ящиком можно «допросить» мультитулом:
/// последний маршрут уходит в канал СБ. Под ЭМИ в момент смерти ящик молчит — это и есть контр-плей.
/// </summary>
public sealed partial class DutyBlackBoxSystem : EntitySystem
{
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly ProtoId<RadioChannelPrototype> OperativeChannel = "Syndicate";
    private static readonly ProtoId<RadioChannelPrototype> SecurityChannel = "Security";
    private const string InterrogateQuality = "Pulsing";

    private const float SampleInterval = 5f;
    private float _accumulator;

    private EntityQuery<DutyBlackBoxComponent> _boxQuery;

    public override void Initialize()
    {
        base.Initialize();

        _boxQuery = GetEntityQuery<DutyBlackBoxComponent>();

        SubscribeLocalEvent<DutyBlackBoxComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutyBlackBoxComponent, ImplantRemovedEvent>(OnRemoved);

        SubscribeLocalEvent<DutyBlackBoxHostComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<DutyBlackBoxHostComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<DutyBlackBoxHostComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbs);
        SubscribeLocalEvent<DutyBlackBoxHostComponent, DutyBlackBoxInterrogateDoAfterEvent>(OnInterrogated);
    }

    private void OnImplanted(Entity<DutyBlackBoxComponent> ent, ref ImplantImplantedEvent args)
    {
        var host = EnsureComp<DutyBlackBoxHostComponent>(args.Implanted);
        host.Implant = ent;
        host.RouteSize = ent.Comp.RouteSize;
        host.MinStep = ent.Comp.MinStep;
        host.InterrogateTime = ent.Comp.InterrogateTime;
    }

    private void OnRemoved(Entity<DutyBlackBoxComponent> ent, ref ImplantRemovedEvent args)
    {
        RemComp<DutyBlackBoxHostComponent>(args.Implanted);
    }

    private void OnDamageChanged(Entity<DutyBlackBoxHostComponent> ent, ref DamageChangedEvent args)
    {
        // Последний, кто нанёс урон, — наш «убийца», если смерть пришла без явного источника.
        if (!args.DamageIncreased || args.Origin is not { } origin || origin == ent.Owner)
            return;

        ent.Comp.LastAttacker = origin;
        ent.Comp.LastAttackerName = Name(origin);
    }

    private void OnMobStateChanged(Entity<DutyBlackBoxHostComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || ent.Comp.Reported)
            return;

        // Под ЭМИ (или выгоревший модуль) ящик не передаёт.
        if (!_cyber.IsModuleOnline(ent.Comp.Implant))
            return;

        ent.Comp.Reported = true;

        var killer = args.Origin is { } o && o != ent.Owner ? Name(o) : ent.Comp.LastAttackerName;
        var message = Loc.GetString("duty-gorlex-blackbox-report",
            ("name", Name(ent)),
            ("zone", DescribeZone(ent)),
            ("killer", killer ?? Loc.GetString("duty-gorlex-blackbox-unknown")),
            ("time", _ticker.RoundDuration().ToString(@"hh\:mm\:ss")));

        _radio.SendRadioMessage(ent, message, OperativeChannel, ent);
    }

    private void OnGetVerbs(Entity<DutyBlackBoxHostComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using is not { } used || ent.Comp.Interrogated)
            return;

        if (!_mobState.IsDead(ent) || !_tool.HasQuality(used, InterrogateQuality))
            return;

        var user = args.User;
        var target = ent.Owner;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("duty-gorlex-blackbox-verb"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/information.svg.192dpi.png")),
            Act = () => StartInterrogation(user, target, used),
        });
    }

    private void StartInterrogation(EntityUid user, EntityUid host, EntityUid tool)
    {
        if (!TryComp<DutyBlackBoxHostComponent>(host, out var comp))
            return;

        var args = new DoAfterArgs(EntityManager, user, comp.InterrogateTime,
            new DutyBlackBoxInterrogateDoAfterEvent(), host, target: host, used: tool)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(args))
            _popup.PopupEntity(Loc.GetString("duty-gorlex-blackbox-start", ("target", host)), host, user, PopupType.Medium);
    }

    private void OnInterrogated(Entity<DutyBlackBoxHostComponent> ent, ref DutyBlackBoxInterrogateDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        ent.Comp.Interrogated = true;

        var sb = new StringBuilder();
        sb.Append(Loc.GetString("duty-gorlex-blackbox-dump-head", ("name", Name(ent))));
        if (ent.Comp.Route.Count == 0)
        {
            sb.Append(' ').Append(Loc.GetString("duty-gorlex-blackbox-dump-empty"));
        }
        else
        {
            for (var i = 0; i < ent.Comp.Route.Count; i++)
            {
                sb.Append(i == 0 ? ' ' : '>').Append(' ').Append(ent.Comp.Route[i]);
            }
        }

        _popup.PopupEntity(Loc.GetString("duty-gorlex-blackbox-done"), ent, args.User, PopupType.Medium);
        _radio.SendRadioMessage(args.User, sb.ToString(), SecurityChannel, args.User);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < SampleInterval)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<DutyBlackBoxHostComponent>();
        while (query.MoveNext(out var uid, out var host))
        {
            if (host.Reported || _mobState.IsDead(uid))
                continue;

            var coords = _transform.GetMapCoordinates(uid);
            if (coords.MapId == MapId.Nullspace)
                continue;

            if (host.Route.Count > 0 && (coords.Position - host.LastPoint).Length() < host.MinStep)
                continue;

            host.LastPoint = coords.Position;
            host.Route.Add(DescribeZone(uid));
            if (host.Route.Count > host.RouteSize)
                host.Route.RemoveAt(0);
        }
    }

    /// <summary>Читаемая «зона»: имя грида (шаттл/станция) и координаты тайла; в космосе — карта.</summary>
    private string DescribeZone(EntityUid uid)
    {
        var xform = Transform(uid);
        var pos = _transform.GetMapCoordinates(uid).Position;
        var x = (int) MathF.Round(pos.X);
        var y = (int) MathF.Round(pos.Y);

        if (xform.GridUid is { } grid)
            return Loc.GetString("duty-gorlex-blackbox-zone-grid", ("grid", Name(grid)), ("x", x), ("y", y));

        return Loc.GetString("duty-gorlex-blackbox-zone-space", ("x", x), ("y", y));
    }
}
