using JetBrains.Annotations;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._Duty.ErpStatus;

/// <summary>
/// _Duty: владеет окном смены ЕРП-статуса, открываемым из Escape-меню. Повторяет паттерн
/// <c>OptionsUIController</c> — единственное окно, создаётся лениво, переиспользуется.
/// </summary>
[UsedImplicitly]
public sealed class ErpStatusUIController : UIController
{
    private ErpStatusWindow _window = default!;

    private void EnsureWindow()
    {
        if (_window is { Disposed: false })
            return;

        _window = UIManager.CreateWindow<ErpStatusWindow>();
    }

    public void OpenWindow()
    {
        EnsureWindow();

        _window.Refresh();
        _window.OpenCentered();
        _window.MoveToFront();
    }

    public void ToggleWindow()
    {
        EnsureWindow();

        if (_window.IsOpen)
            _window.Close();
        else
            OpenWindow();
    }
}
