// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.CodeAlpha;

/// <summary>
/// _Duty: общие константы кода «Альфа» — тайминги протокола и оформление панели таймера.
/// Вынесены сюда, чтобы сервер и клиент считали одни и те же числа.
/// </summary>
public static class DutyCodeAlphaVisuals
{
    /// <summary>Идентификатор уровня тревоги в <c>alert_levels.yml</c>.</summary>
    public const string AlertLevel = "alpha";

    /// <summary>Уровень, возврат на который снимает протокол.</summary>
    public const string GreenLevel = "green";

    /// <summary>Сколько хост может думать, прежде чем код включится сам.</summary>
    public static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Минимальная пауза между объявлением войны и объявлением кода. Объявление войны само играет
    /// <c>announce_syndi.ogg</c> длиной 8.77 с; без этой паузы при мгновенном подтверждении хоста
    /// две сирены наложились бы друг на друга.
    /// </summary>
    public static readonly TimeSpan AnnounceDelay = TimeSpan.FromSeconds(9);

    /// <summary>Как часто система догоняет новоприбывших доступами.</summary>
    public static readonly TimeSpan GrantInterval = TimeSpan.FromSeconds(2);

    /// <summary>Остаток, на котором рассылается вторая пачка РП-реплик.</summary>
    public static readonly TimeSpan RpEndThreshold = TimeSpan.FromMinutes(1);

    /// <summary>Сколько вариантов реплик лежит в локали (ключи <c>…-1</c>…<c>-5</c>).</summary>
    public const int RpPhraseCount = 5;

    // ── Панель таймера ────────────────────────────────────────────────────────

    public const string FontPath = "/Fonts/Duty/Underdog/Underdog-Regular.ttf";
    public const int FontSize = 22;

    /// <summary>Кроваво-красный. Тот же цвет, что у уровня тревоги.</summary>
    public const string Color = "#8A0303";

    /// <summary>Отступ панели от правого нижнего угла экрана, пиксели.</summary>
    public const float Margin = 12f;

    /// <summary>Максимальный интервал между кликами, который ещё считается двойным.</summary>
    public static readonly TimeSpan DoubleClickWindow = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Остатки, на которых спрятанная панель показывается снова. Кейбинда для возврата нет,
    /// поэтому это единственный способ вернуть её — пороги должны быть частыми ближе к концу.
    /// </summary>
    public static readonly TimeSpan[] ReshowThresholds =
    [
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(1),
    ];

    // ── Музыка ────────────────────────────────────────────────────────────────

    public const string TrackCalm = "/Audio/_Duty/CodeAlpha/nothing_wrong_with_megacorpos.ogg";
    public const string TrackFinal = "/Audio/_Duty/CodeAlpha/all_wrong_with_megacorpos.ogg";

    /// <summary>Первый трек стартует через столько после объявления кода.</summary>
    public static readonly TimeSpan TrackCalmDelay = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Второй трек стартует на этом остатке. Его длина — 296.5 с, поэтому он заканчивается
    /// примерно на 00:03.5, ровно под конец отсчёта.
    /// </summary>
    public static readonly TimeSpan TrackFinalLead = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Насколько поздно ещё допустимо запустить первый трек. Зашедший в середине раунда игрок
    /// не должен услышать его с начала — это спокойная тема, а не фанфара.
    /// </summary>
    public static readonly TimeSpan TrackCalmGrace = TimeSpan.FromSeconds(5);
}
