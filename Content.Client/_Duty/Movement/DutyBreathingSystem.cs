// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Movement;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Systems;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Client._Duty.Movement;

/// <summary>
/// _Duty: клиентский звук отдышки. Играет зацикленное дыхание, пока у локального игрока
/// активен сетевой флаг <see cref="DutyStaminaComponent.Breathing"/>. Останавливается плавно
/// (fade-out), переживает «мёртвый» стрим и смерть игрока — чтобы звук не залипал петлёй.
/// </summary>
public sealed class DutyBreathingSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private const float BreathVolume = -5f;     // рабочая громкость, негромко
    private const float SilenceVolume = -32f;   // куда уводим при fade-out
    private const float FadeDuration = 0.4f;    // длительность затухания (сек)

    private EntityUid? _stream;
    private bool _fadingOut;
    private float _fadeElapsed;

    public override void Shutdown()
    {
        base.Shutdown();
        StopNow();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Стрим мог быть удалён движком (закончился/выгрузился) — сбрасываем ссылку.
        if (_stream != null && (Deleted(_stream) || TerminatingOrDeleted(_stream.Value)))
        {
            _stream = null;
            _fadingOut = false;
        }

        if (ShouldBreathe(out var sound))
        {
            // Возобновили дыхание — отменяем затухание и возвращаем громкость.
            if (_stream != null && _fadingOut)
            {
                _fadingOut = false;
                SetVolume(BreathVolume);
            }

            if (_stream == null)
            {
                _stream = _audio.PlayGlobal(
                    sound,
                    Filter.Local(),
                    false,
                    AudioParams.Default.WithLoop(true).WithVolume(BreathVolume))?.Entity;
                _fadingOut = false;
            }

            return;
        }

        // Дышать больше не надо — плавно гасим и затем стопаем.
        if (_stream == null)
            return;

        if (!_fadingOut)
        {
            _fadingOut = true;
            _fadeElapsed = 0f;
        }

        _fadeElapsed += frameTime;
        var t = Math.Clamp(_fadeElapsed / FadeDuration, 0f, 1f);
        SetVolume(float.Lerp(BreathVolume, SilenceVolume, t));

        if (t >= 1f)
            StopNow();
    }

    /// <summary>Должен ли локальный игрок сейчас дышать (жив и флаг Breathing активен).</summary>
    private bool ShouldBreathe(out SoundSpecifier sound)
    {
        sound = default!;

        var player = _player.LocalEntity;
        if (player == null
            || _mobState.IsIncapacitated(player.Value)
            || !TryComp<DutyStaminaComponent>(player, out var comp)
            || !comp.Breathing)
            return false;

        var female = TryComp<HumanoidAppearanceComponent>(player, out var humanoid)
                     && humanoid.Sex == Sex.Female;
        sound = female ? comp.FemaleBreathSound : comp.MaleBreathSound;
        return true;
    }

    private void SetVolume(float db)
    {
        if (_stream != null && TryComp<AudioComponent>(_stream, out var audio))
            _audio.SetVolume(_stream, db, audio);
    }

    private void StopNow()
    {
        if (_stream != null)
            _audio.Stop(_stream);

        _stream = null;
        _fadingOut = false;
    }
}
