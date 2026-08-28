// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client._Duty.AmbientMusic;
using Content.Shared._Duty.CodeAlpha;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.NukeOps;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

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
///
/// Играем через <see cref="DutyMusicCache"/>, а не через обычный
/// <c>SharedAudioSystem.PlayGlobal</c>: тот на клиенте достаёт файл из <see cref="IResourceCache"/>,
/// а <c>AudioResource</c> движок выгружать не умеет — оба наших трека (~40 и ~52 МБ распакованного
/// PCM) осели бы в памяти клиента до конца сессии после одного раунда с Альфой. Свой кэш их и
/// греет заранее, и отпускает, когда код снят.
/// </summary>
public sealed class DutyCodeAlphaMusicSystem : EntitySystem
{
    [Dependency] private readonly IAudioManager _audioManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly Robust.Client.Audio.AudioSystem _clientAudio = default!;

    private static readonly ResPath CalmPath = new(DutyCodeAlphaVisuals.TrackCalm);
    private static readonly ResPath FinalPath = new(DutyCodeAlphaVisuals.TrackFinal);

    /// <summary>Списки на один элемент для <see cref="DutyMusicCache.WarmNext"/>, чтобы не плодить их в кадре.</summary>
    private static readonly ResPath[] CalmOnly = [CalmPath];
    private static readonly ResPath[] FinalOnly = [FinalPath];

    /// <summary>
    /// За сколько до своего выхода греется финальный трек. Полминуты с запасом перекрывают
    /// декодирование даже самого длинного ogg.
    /// </summary>
    private static readonly TimeSpan FinalWarmLead = TimeSpan.FromSeconds(30);

    private DutyMusicCache _cache = default!;

    /// <summary>Что нельзя выгружать прямо сейчас. Треков всего два, так что здесь максимум один путь.</summary>
    private readonly HashSet<ResPath> _keep = new();

    private EntityUid? _stream;
    private bool _calmDone;
    private bool _finalDone;

    private float _volumeSlider;

    public override void Initialize()
    {
        base.Initialize();

        _cache = new DutyMusicCache(_resourceCache, _audioManager, Log);

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
        _cache.Clear(IsStreamAlive);
    }

    private void OnVolumeChanged(float value)
    {
        // Децибелы складываются, поэтому поправка — просто слагаемое к значению ползунка.
        _volumeSlider = SharedAudioSystem.GainToVolume(value) + DutyCodeAlphaVisuals.TrackVolumeDb;

        // TryComp обязателен: трек доигрывает до конца сам, движок удаляет его сущность, а _stream
        // ещё держит её uid. SharedAudioSystem.SetVolume зовёт Resolve БЕЗ подавления лога, поэтому
        // без этой проверки любое движение ползунка после конца трека сыпало бы ошибки в консоль.
        if (!TryComp<AudioComponent>(_stream, out var comp))
            return;

        _audio.SetVolume(_stream, _volumeSlider, comp);
    }

    private void OnAlphaShutdown(Entity<DutyCodeAlphaComponent> ent, ref ComponentShutdown args)
    {
        Reset();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        Reset();
    }

    /// <remarks>
    /// Именно FrameUpdate, а не Update: клиентский Update переигрывается на каждом прогоне
    /// предсказания, и одноразовый запуск трека мог бы сработать несколько раз за тик.
    /// FrameUpdate вызывается ровно раз на кадр и предсказанием не переигрывается.
    /// </remarks>
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!TryGetAlpha(out var alpha))
        {
            // Reset() зовут в тот момент, когда трек ещё доигрывает: Stop — это QueueDel, и буфер
            // из-под живого источника выдёргивать нельзя. Поэтому добираем выгрузку здесь, пока в
            // кэше что-то осталось. Словарь из двух записей, цена нулевая.
            if (_cache.LoadedCount > 0)
                _cache.Clear(IsStreamAlive);

            return;
        }

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
            PlayTrack(FinalPath, (float) offset.TotalSeconds);
            return;
        }

        if (!_calmDone)
        {
            var calmStart = alpha.ActivatedAt + DutyCodeAlphaVisuals.TrackCalmDelay;
            if (now < calmStart)
            {
                // Двадцать секунд до старта уходят на декодирование трека: под сирену объявления
                // просадка кадра не слышна и не видна, а ровно в момент старта музыки — ещё как.
                _cache.WarmNext(CalmOnly);
                KeepOnly(CalmPath);
                return;
            }

            _calmDone = true;

            // Опоздавшему спокойную тему не включаем — это вступление, а не фоновая петля.
            if (now - calmStart <= DutyCodeAlphaVisuals.TrackCalmGrace)
            {
                PlayTrack(CalmPath, 0f);
                return;
            }
        }

        // Финальный трек греем по той же причине: его выход привязан к остатку 5:00, промахнуться
        // мимо этой секунды нельзя.
        if (!_finalDone && remaining <= DutyCodeAlphaVisuals.TrackFinalLead + FinalWarmLead)
            _cache.WarmNext(FinalOnly);

        // Спокойная тема своё отыграла (или её пропустили как опоздавшую) — держать её ~40 МБ ещё
        // девять минут не за чем. Уйдёт она не сразу, а когда смолкнет источник: Trim не трогает
        // буфер, который прямо сейчас играют.
        KeepOnly(FinalPath);
    }

    /// <summary>Оставляет в кэше только указанный трек, отпуская всё, что уже отзвучало.</summary>
    private void KeepOnly(ResPath path)
    {
        _keep.Clear();
        _keep.Add(path);
        _cache.Trim(_keep, IsStreamAlive);
    }

    private void PlayTrack(ResPath path, float offsetSeconds)
    {
        StopStream();

        if (_cache.Get(path) is not { } stream)
            return;

        // Смещение идёт через AudioParams, а не через SetPlaybackPosition: тот внутри зовёт
        // GetAudioLength, а он на клиенте достаёт файл из IResourceCache — то есть ровно из того
        // неубиваемого кэша ресурсов, ради ухода от которого всё это и написано. Движок применяет
        // PlayOffsetSeconds до старта источника, так что это ещё и точнее, чем seek после запуска.
        var playing = _clientAudio.PlayGlobal(
            stream,
            new ResolvedPathSpecifier(path),
            AudioParams.Default.WithVolume(_volumeSlider).WithPlayOffset(offsetSeconds));

        if (playing == null)
            return;

        _stream = playing.Value.Entity;
        _cache.NoteUser(path, _stream.Value);
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
        _cache.Clear(IsStreamAlive);
    }

    /// <summary>
    /// Жив ли ещё источник. Stop удаляет аудио-сущность отложенно (QueueDel), так что уже
    /// остановленный трек вполне может доигрывать до конца тика — трогать его буфер в это время
    /// нельзя.
    /// </summary>
    private bool IsStreamAlive(EntityUid stream) => Exists(stream);

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
