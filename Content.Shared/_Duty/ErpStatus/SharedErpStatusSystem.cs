using Content.Shared.Examine;

namespace Content.Shared._Duty.ErpStatus;

/// <summary>
/// _Duty: общая часть системы ЕРП-статуса — осмотр персонажа и константа кулдаума смены.
/// Заспавн из профиля и обработка запроса на смену — только на сервере, см.
/// <see cref="Content.Server._Duty.ErpStatus.ErpStatusSystem"/>.
/// </summary>
public abstract class SharedErpStatusSystem : EntitySystem
{
    /// <summary>
    /// Минимальный интервал между сменами статуса одним персонажем.
    /// </summary>
    public static readonly TimeSpan ChangeCooldown = TimeSpan.FromMinutes(5);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ErpStatusComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<ErpStatusComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Owner == args.Examiner)
            return;

        var locKey = ent.Comp.Status switch
        {
            DutyErpStatus.None => "duty-erp-status-examine-none",
            DutyErpStatus.Moderate => "duty-erp-status-examine-moderate",
            DutyErpStatus.Full => "duty-erp-status-examine-full",
            _ => "duty-erp-status-examine-none",
        };

        args.PushMarkup(Loc.GetString(locKey));
    }
}
