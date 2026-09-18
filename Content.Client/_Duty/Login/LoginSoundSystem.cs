// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._Duty.Login;

public sealed class LoginSoundSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private static readonly SoundPathSpecifier LoginSound = new("/Audio/_Duty/UI/login.ogg");

    private bool _played;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<TickerJoinGameEvent>(OnJoinGame);
    }

    private void OnJoinGame(TickerJoinGameEvent ev)
    {
        if (_played)
            return;

        _played = true;

        if (!_cfg.GetCVar(DutyCCVars.LoginSoundEnabled))
            return;

        EntityManager.System<AudioSystem>().PlayGlobal(LoginSound, Filter.Local(), false, AudioParams.Default);
    }
}
