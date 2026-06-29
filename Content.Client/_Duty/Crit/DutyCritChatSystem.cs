using Content.Client.Gameplay;
using Content.Shared._Duty.HealthPhrases;
using Content.Shared.Chat;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using System.Numerics;

namespace Content.Client._Duty.Crit;

/// <summary>
/// В MobState.Critical локальный игрок слышит IC/эмоции только в 3 клетках; дальше — заглушённые фразы.
/// </summary>
public sealed class DutyCritChatSystem : EntitySystem
{
    private const float HearRangeTiles = 3f;

    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IStateManager _state = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    /// <summary>
    /// Подменяет <see cref="ChatMessage.WrappedMessage"/> для дальних IC-сообщений в крите.
    /// </summary>
    public void ProcessIncomingMessage(ref ChatMessage msg)
    {
        if (_state.CurrentState is not GameplayState)
            return;

        if (_player.LocalEntity is not { } local)
            return;

        if (!IsLocalCritical(local))
            return;

        if (msg.Channel is not (ChatChannel.Local or ChatChannel.Whisper or ChatChannel.Emotes))
            return;

        if (msg.SenderEntity == default)
            return;

        var sender = GetEntity(msg.SenderEntity);
        if (sender == local || !Exists(sender))
            return;

        if (GetTileDistance(local, sender) <= HearRangeTiles)
            return;

        msg.WrappedMessage = msg.Channel == ChatChannel.Emotes
            ? Loc.GetString("duty-crit-distant-emote",
                ("fontType", DutyHealthPhrasesVisuals.FontPrototypeId),
                ("fontSize", DutyHealthPhrasesVisuals.WhisperFontSize))
            : Loc.GetString("duty-crit-distant-speech",
                ("fontType", DutyHealthPhrasesVisuals.FontPrototypeId),
                ("fontSize", DutyHealthPhrasesVisuals.WhisperFontSize));
    }

    private bool IsLocalCritical(EntityUid uid)
    {
        return TryComp<MobStateComponent>(uid, out var mob) && mob.CurrentState == MobState.Critical;
    }

    private float GetTileDistance(EntityUid a, EntityUid b)
    {
        var posA = _xform.GetWorldPosition(a);
        var posB = _xform.GetWorldPosition(b);
        return (posA - posB).Length();
    }
}
