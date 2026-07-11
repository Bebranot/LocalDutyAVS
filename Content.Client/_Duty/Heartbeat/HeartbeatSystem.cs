using Content.Shared._Duty.Heartbeat;
using Content.Shared.CCVar;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._Duty.Heartbeat;

/// <summary>
/// _Duty: клиентская часть пульса — проигрывает сердцебиение ТОЛЬКО у владельца тела
/// (Filter.Local, НЕ позиционно, поэтому стерео-сэмплы допустимы и не крашат позиционку).
/// Окружающие чужой пульс не слышат. Уровень читается из networked
/// <see cref="HeartbeatComponent.Level"/>, посчитанного сервером.
/// </summary>
public sealed class HeartbeatSystem : SharedHeartbeatSystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private float _beatAccum;

    // ВАЖНО: играем в FrameUpdate (раз в кадр), а НЕ в Update. Клиентский Update
    // вызывается многократно за тик во время re-prediction → аккумулятор набегал бы
    // быстрее реального времени и удары сыпались бы пачкой («бешено колотится»).
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_cfg.GetCVar(DutyCCVars.HeartbeatEnabled))
        {
            _beatAccum = 0f;
            return;
        }

        var player = _player.LocalEntity;
        if (player == null
            || !TryComp<HeartbeatComponent>(player, out var comp)
            || comp.Level == HeartbeatLevel.None)
        {
            _beatAccum = 0f;
            return;
        }

        var interval = comp.Level switch
        {
            HeartbeatLevel.Light => comp.LightInterval,
            HeartbeatLevel.Heavy => comp.HeavyInterval,
            HeartbeatLevel.Critical => comp.CriticalInterval,
            _ => 0f,
        };

        if (interval <= 0f)
            return;

        _beatAccum += frameTime;
        if (_beatAccum < interval)
            return;

        _beatAccum = 0f;

        var sound = comp.Level == HeartbeatLevel.Light ? comp.LightSound : comp.HeavySound;
        _audio.PlayGlobal(sound, Filter.Local(), false, sound.Params);
    }
}
