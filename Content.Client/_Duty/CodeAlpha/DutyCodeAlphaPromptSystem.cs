// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.CodeAlpha;
using Content.Shared.GameTicking;
using Robust.Shared.Timing;

namespace Content.Client._Duty.CodeAlpha;

/// <summary>
/// _Duty: показывает хосту окно подтверждения кода «Альфа» и отправляет его ответ обратно.
/// </summary>
public sealed class DutyCodeAlphaPromptSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private DutyCodeAlphaConfirmWindow? _window;
    private TimeSpan _expiresAt;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<DutyCodeAlphaPromptEvent>(OnPrompt);
        // Сервер шлёт это событие клиентам по сети: SubscribeLocalEvent тут не сработал бы никогда.
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnPrompt(DutyCodeAlphaPromptEvent ev)
    {
        CloseWindow();

        _expiresAt = ev.ExpiresAt;

        _window = new DutyCodeAlphaConfirmWindow();
        _window.SetPrompt(ev.Body, ev.ExpiresAt);
        _window.UpdateCountdown(_timing.CurTime);
        _window.OnAnswered += OnAnswered;
        _window.OnClose += () => _window = null;
        _window.OpenCentered();
    }

    private void OnAnswered(bool confirmed)
    {
        RaiseNetworkEvent(new DutyCodeAlphaReplyEvent(confirmed));
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        CloseWindow();
    }

    public override void Update(float frameTime)
    {
        if (_window == null)
            return;

        var now = _timing.CurTime;
        _window.UpdateCountdown(now);

        // Молчание — тоже ответ: сервер включит код сам, окно больше ни на что не влияет.
        if (now >= _expiresAt)
            CloseWindow();
    }

    private void CloseWindow()
    {
        if (_window == null)
            return;

        var window = _window;
        _window = null;
        window.OnAnswered = null;
        window.Close();
    }
}
