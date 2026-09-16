using Content.Shared._Duty.ErpStatus;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Duty.ErpStatus;

/// <summary>
/// _Duty: серверная часть ЕРП-статуса. При заспавне персонажа копирует значение по умолчанию
/// из профиля (<c>HumanoidCharacterProfile.ErpStatus</c>) в раундовый компонент, а дальше
/// обрабатывает запросы на смену статуса с Escape/F10 — с серверно-авторитетным кулдауном.
/// Клиенту не доверяем ни новое значение статуса, ни момент запроса.
/// </summary>
public sealed class ErpStatusSystem : SharedErpStatusSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeNetworkEvent<ErpStatusChangeRequestEvent>(OnChangeRequest);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        var comp = EnsureComp<ErpStatusComponent>(ev.Mob);
        comp.Status = ev.Profile.ErpStatus;
        // Первую смену в раунде разрешаем сразу — кулдаун стартует только от неё.
        comp.NextChangeAllowedAt = _timing.CurTime;
        Dirty(ev.Mob, comp);
    }

    private void OnChangeRequest(ErpStatusChangeRequestEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } mob)
            return;

        if (!Enum.IsDefined(msg.NewStatus))
            return;

        var comp = EnsureComp<ErpStatusComponent>(mob);
        var now = _timing.CurTime;

        if (now < comp.NextChangeAllowedAt)
        {
            var remaining = comp.NextChangeAllowedAt - now;
            _popup.PopupEntity(
                Loc.GetString("duty-erp-status-change-on-cooldown", ("seconds", (int) Math.Ceiling(remaining.TotalSeconds))),
                mob,
                args.SenderSession);
            return;
        }

        if (comp.Status == msg.NewStatus)
        {
            _popup.PopupEntity(Loc.GetString("duty-erp-status-change-same"), mob, args.SenderSession);
            return;
        }

        comp.Status = msg.NewStatus;
        comp.NextChangeAllowedAt = now + ChangeCooldown;
        Dirty(mob, comp);

        var locKey = msg.NewStatus switch
        {
            DutyErpStatus.None => "duty-erp-status-change-success-none",
            DutyErpStatus.Moderate => "duty-erp-status-change-success-moderate",
            DutyErpStatus.Full => "duty-erp-status-change-success-full",
            _ => "duty-erp-status-change-success-none",
        };

        _popup.PopupEntity(Loc.GetString(locKey), mob, args.SenderSession);
    }
}
