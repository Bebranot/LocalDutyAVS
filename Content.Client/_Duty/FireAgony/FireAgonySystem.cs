using Content.Client.Audio;
using Content.Shared._Duty.FireAgony;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Client._Duty.FireAgony;

/// <summary>
/// _Duty: клиентская кинематографика «Агонии от огня». По сетевому флагу
/// <c>FireAgonyComponent.Active</c> локального игрока с фейдом ~0.5с включает красную виньетку
/// боли (<see cref="FireAgonyOverlay"/>) и запускает НЕПРЕРЫВНЫЙ (зацикленный) звук крика —
/// стартует при входе в сцену (персонаж падает), обрывается фейд-аутом на конце сцены.
/// Периодические эмоуты в ИС-чат — отдельный текст на сервере, звук не дёргают. Затемнение HUD
/// ведёт единый владелец <c>HudAimFadeSystem</c>; приглушение звука — <c>DynamicAmbientMusicSystem</c>;
/// зум камеры ставит сервер.
/// </summary>
public sealed class FireAgonySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ContentAudioSystem _contentAudio = default!;

    private FireAgonyOverlay _overlay = default!;

    private float _strength;
    private bool _wasActive;
    private EntityUid? _screamStream;

    /// <summary>Длительность фейда виньетки, сек.</summary>
    private const float FadeSeconds = 0.5f;

    /// <summary>Фейд-аут крика на конце сцены, сек.</summary>
    private const float ScreamEndFade = 0.5f;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new FireAgonyOverlay();
        _overlayMan.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayMan.RemoveOverlay(_overlay);
        StopScream(immediate: true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var active = IsLocalActive();

        // Фейд силы виньетки к цели за ~0.5с.
        var target = active ? 1f : 0f;
        var step = FadeSeconds <= 0f ? 1f : frameTime / FadeSeconds;
        _strength = target > _strength
            ? MathF.Min(target, _strength + step)
            : MathF.Max(target, _strength - step);

        _overlay.Strength = _strength;

        // Непрерывный крик: старт при входе в сцену (персонаж падает), фейд-аут на выходе.
        if (active && !_wasActive)
            StartScream();
        else if (!active && _wasActive)
            StopScream(immediate: false, duration: ScreamEndFade);

        _wasActive = active;
    }

    private bool IsLocalActive()
    {
        return _player.LocalEntity is { } p
            && TryComp<FireAgonyComponent>(p, out var comp)
            && comp.Active;
    }

    private void StartScream()
    {
        var player = _player.LocalEntity;
        if (player == null || !TryComp<FireAgonyComponent>(player, out var comp))
            return;

        StopScream(immediate: true); // подчистить прежний поток, если вдруг остался

        // Зацикленный крик громче на ScreamVolumeDb, чтобы оставаться слышным поверх дака мастера.
        _screamStream = _audio.PlayGlobal(
            comp.ScreamSound,
            Filter.Local(),
            false,
            AudioParams.Default.WithLoop(true).WithVolume(comp.ScreamVolumeDb))?.Entity;
    }

    private void StopScream(bool immediate, float duration = ScreamEndFade)
    {
        if (_screamStream == null)
            return;

        if (immediate)
        {
            if (Exists(_screamStream.Value))
                _audio.Stop(_screamStream);
        }
        else if (Exists(_screamStream.Value) && HasComp<AudioComponent>(_screamStream.Value))
        {
            _contentAudio.FadeOut(_screamStream, duration: duration);
        }

        _screamStream = null;
    }
}
