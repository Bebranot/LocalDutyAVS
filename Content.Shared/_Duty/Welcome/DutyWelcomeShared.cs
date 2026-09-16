using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Welcome;

/// <summary>
/// _Duty: общие для клиента и сервера константы и сетевые сообщения окна
/// приветствия/чейнджлога. См. <c>Content.Server._Duty.Welcome.DutyWelcomeSystem</c>.
/// </summary>
public static class DutyWelcomeShared
{
    /// <summary>
    /// Порог суммарного наигранного времени, ниже которого игрок считается новичком
    /// и видит окно приветствия при каждом входе (а не один раз за рестарт сервера).
    /// </summary>
    public static readonly TimeSpan NewPlayerPlaytimeThreshold = TimeSpan.FromHours(10);
}

/// <summary>
/// Состояние окна приветствия, присылаемое при открытии.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyWelcomeEuiState : EuiStateBase
{
    /// <summary>
    /// Наиграно меньше <see cref="DutyWelcomeShared.NewPlayerPlaytimeThreshold"/> — клиент
    /// показывает подсказку для новичков.
    /// </summary>
    public readonly bool IsNewPlayer;

    public DutyWelcomeEuiState(bool isNewPlayer)
    {
        IsNewPlayer = isNewPlayer;
    }
}

/// <summary>
/// Клиент → сервер: окно приветствия закрыто, с текущим состоянием галочки «не показывать снова».
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyWelcomeCloseMessage : EuiMessageBase
{
    public readonly bool DontShowAgain;

    public DutyWelcomeCloseMessage(bool dontShowAgain)
    {
        DontShowAgain = dontShowAgain;
    }
}
