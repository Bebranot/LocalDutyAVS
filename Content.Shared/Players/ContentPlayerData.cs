using Content.Shared.Administration;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Robust.Shared.Network;

namespace Content.Shared.Players;

/// <summary>
///     Content side for all data that tracks a player session.
///     Use <see cref="PlaIPlayerDatarver.Player.IPlayerData)"/> to retrieve this from an <see cref="PlayerData"/>.
///     <remarks>Not currently used on the client.</remarks>
/// </summary>
public sealed class ContentPlayerData
{
    /// <summary>
    ///     The session ID of the player owning this data.
    /// </summary>
    [ViewVariables]
    public NetUserId UserId { get; }

    /// <summary>
    ///     This is a backup copy of the player name stored on connection.
    ///     This is useful in the event the player disconnects.
    /// </summary>
    [ViewVariables]
    public string Name { get; }

    /// <summary>
    ///     The currently occupied mind of the player owning this data.
    ///     DO NOT DIRECTLY SET THIS UNLESS YOU KNOW WHAT YOU'RE DOING.
    /// </summary>
    [ViewVariables, Access(typeof(SharedMindSystem), typeof(SharedGameTicker))]
    public EntityUid? Mind { get; set; }

    /// <summary>
    /// If true, the admin will not show up in adminwho except to admins with the <see cref="AdminFlags.Stealth"/> flag.
    /// </summary>
    public bool Stealthed { get; set; }

    /// <summary>
    /// _Duty: окно приветствия/чейнджлога (см. <c>Content.Server._Duty.Welcome.DutyWelcomeSystem</c>)
    /// уже было показано этому игроку в текущей сессии сервера. Живёт только в памяти — вместе
    /// со всем <see cref="SessionData"/> сбрасывается при рестарте сервера, но переживает
    /// переподключение игрока в рамках одного запуска.
    /// </summary>
    public bool DutyWelcomeShown { get; set; }

    /// <summary>
    /// _Duty: игрок отметил «не показывать снова» — окно приветствия не показывать этому игроку,
    /// пока не случится рестарт сервера (см. <see cref="DutyWelcomeShown"/>).
    /// </summary>
    public bool DutyWelcomeDismissed { get; set; }

    public ContentPlayerData(NetUserId userId, string name)
    {
        UserId = userId;
        Name = name;
    }
}
