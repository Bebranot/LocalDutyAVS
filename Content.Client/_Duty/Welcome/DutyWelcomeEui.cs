using Content.Client.Eui;
using Content.Shared._Duty.Welcome;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._Duty.Welcome;

/// <summary>
/// _Duty: клиентская половина окна приветствия/чейнджлога. Имя класса должно совпадать
/// с серверным <c>Content.Server._Duty.Welcome.DutyWelcomeEui</c> — открытие EUI сопоставляет
/// их по простому имени типа.
/// </summary>
[UsedImplicitly]
public sealed class DutyWelcomeEui : BaseEui
{
    private readonly DutyWelcomeWindow _window;

    public DutyWelcomeEui()
    {
        _window = new DutyWelcomeWindow();
        _window.OnClose += () => SendMessage(new DutyWelcomeCloseMessage(_window.DontShowAgain));
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not DutyWelcomeEuiState s)
            return;

        _window.SetNewPlayer(s.IsNewPlayer);
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
    }
}
