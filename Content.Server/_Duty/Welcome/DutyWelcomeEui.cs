using Content.Server.EUI;
using Content.Shared._Duty.Welcome;
using Content.Shared.Eui;
using Content.Shared.Players;
using Robust.Shared.IoC;

namespace Content.Server._Duty.Welcome;

/// <summary>
/// _Duty: серверная половина окна приветствия/чейнджлога. Открывается из
/// <see cref="DutyWelcomeSystem"/>, содержимое вкладок статично и локализуется на клиенте —
/// по сети едет только флаг «новичок ли это».
/// </summary>
public sealed class DutyWelcomeEui : BaseEui
{
    private readonly bool _isNewPlayer;

    public DutyWelcomeEui(bool isNewPlayer)
    {
        IoCManager.InjectDependencies(this);
        _isNewPlayer = isNewPlayer;
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new DutyWelcomeEuiState(_isNewPlayer);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is not DutyWelcomeCloseMessage close)
            return;

        if (close.DontShowAgain && Player.ContentData() is { } data)
            data.DutyWelcomeDismissed = true;

        Close();
    }
}
