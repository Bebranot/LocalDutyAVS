// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.CodeAlpha;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.NukeOps;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Duty.CodeAlpha;

/// <summary>
/// _Duty: саундтрек кода «Альфа». Два трека на все пятнадцать минут отсчёта.
///
/// Спокойная тема заходит через 20 секунд после объявления и отыгрывает своё, а финальная
/// включается ровно на остатке 5:00 и заканчивается примерно на 00:03 — её длина подобрана под
/// конец отсчёта. Между ними играет обычная динамическая музыка.
///
/// Оперативники ничего этого не слышат: отсчёт идёт для экипажа, и подкладывать нападающим
/// музыку обороняющихся незачем.
///
/// Заглушение динамической музыки берёт на себя <c>DutyMusicDirector</c>: треки лежат под
/// <c>/Audio/_Duty/CodeAlpha/</c>, а этот префикс объявлен приоритетным в YAML, так что ничего
/// регистрировать вручную не нужно.
/// </summary>
public sealed class DutyCodeAlphaMusicSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private EntityUid? _stream;
    private bool _calmDone;
    private bool _finalDone;

    private float _volumeSlider;

    public override void Initialize()
    {
        base.Initialize();

        _cfg.OnValueChanged(CCVars.AmbientMusicVolume, OnVolumeChanged, true);

        // Сервер шлёт это событие клиентам по сети: SubscribeLocalEvent тут не сработал бы никогда.
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<DutyCodeAlphaComponent, ComponentShutdown>(OnAlphaShutdown);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _cfg.UnsubValueChanged(CCVars.AmbientMusicVolume, OnVolumeChanged);
        StopStream();
    }

    private void OnVolumeChanged(float value)
    {
        _volumeSlider = SharedAudioSystem.GainToVolume(value);

        if (_stream != null)
            _audio.SetVolume(_stream, _volumeSlider);
    }

    private void OnAlphaShutdown(Entity<DutyCodeAlphaComponent> ent, ref ComponentShutdown args)
    {
        Reset();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        Reset();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!TryGetAlpha(out var alpha))
            return;

        if (IsOperative())
            return;

        var now = _timing.CurTime;
        var remaining = alpha.Deadline - now;

        if (!_finalDone && remaining <= DutyCodeAlphaVisuals.TrackFinalLead && remaining > TimeSpan.Zero)
        {
            _finalDone = true;
            _calmDone = true;

            // Подключившийся в середине отсчёта подхватывает трек с нужного места, иначе его
            // концовка уехала бы за ноль и потеряла весь смысл.
            var offset = DutyCodeAlphaVisuals.TrackFinalLead - remaining;
            PlayTrack(DutyCodeAlphaVisuals.TrackFinal, (float)offset.TotalSeconds);
            return;
        }

        if (_calmDone)
            return;

        var calmStart = alpha.ActivatedAt + DutyCodeAlphaVisuals.TrackCalmDelay;
        if (now < calmStart)
            return;

        _calmDone = true;

        // Опоздавшему спокойную тему не включаем — это вступление, а не фоновая петля.
        if (now - calmStart <= DutyCodeAlphaVisuals.TrackCalmGrace)
            PlayTrack(DutyCodeAlphaVisuals.TrackCalm, 0f);
    }

    private void PlayTrack(string path, float offsetSeconds)
    {
        StopStream();

        var stream = _audio.PlayGlobal(
            path,
            Filter.Local(),
            false,
            AudioParams.Default.WithVolume(_volumeSlider));

        if (stream == null)
            return;

        _stream = stream.Value.Entity;

        if (offsetSeconds > 0f)
            _audio.SetPlaybackPosition(_stream, offsetSeconds);
    }

    private void StopStream()
    {
        if (_stream == null)
            return;

        _audio.Stop(_stream);
        _stream = null;
    }

    private void Reset()
    {
        StopStream();
        _calmDone = false;
        _finalDone = false;
    }

    private bool IsOperative()
    {
        return _player.LocalSession?.AttachedEntity is { } player && HasComp<NukeOperativeComponent>(player);
    }

    private bool TryGetAlpha(out DutyCodeAlphaComponent alpha)
    {
        // AllEntityQueryEnumerator: сущность станции живёт в нулевом пространстве, обычный
        // перечислитель пропустил бы её вместе с паузнутыми.
        var query = EntityManager.AllEntityQueryEnumerator<DutyCodeAlphaComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            alpha = comp;
            return true;
        }

        alpha = default!;
        return false;
    }
}
