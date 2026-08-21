// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Robust.Client.Audio;
using Robust.Client.ResourceManagement;
using Robust.Shared.Collections;
using Robust.Shared.Log;
using Robust.Shared.Utility;

namespace Content.Client._Duty.AmbientMusic;

/// <summary>
/// _Duty: ограниченный кэш длинных музыкальных треков.
///
/// Зачем: <see cref="AudioResource"/> держит трек распакованным в 16-битный PCM в буфере OpenAL —
/// пять минут стерео это ~60 МБ. Раньше <see cref="DynamicAmbientMusicSystem"/> грузил разом весь
/// список из прототипа (48 уникальных треков, 141 минута) прямо в Initialize: ~1.47 ГБ памяти
/// клиента и десятки секунд заморозки на старте, причём даже у тех, кто выключил музыку в
/// настройках.
///
/// Теперь система заранее выбирает следующий трек для каждого контекста (спокойный / боевой /
/// боевой-на-низком-HP / крит) и просит прогреть только их: декодирование уходит в фоновый поток,
/// а на игровом треде остаётся только дешёвая заливка в AL. Всё, что мы загрузили и что больше не
/// нужно, выгружается через <see cref="IResourceCache.TryRemoveResource{T}(ResPath)"/>. Резидентно
/// остаётся 3–5 треков вместо 48.
///
/// Трогаем только те пути, которые загрузили сами: треки, попавшие в кэш ресурсов другим путём
/// (лобби-музыка, джукбокс), не наши, и выгружать их мы не имеем права.
/// </summary>
public sealed class DutyMusicCache
{
    private readonly IResourceCache _resourceCache;
    private readonly IAudioManager _audioManager;
    private readonly ISawmill _sawmill;

    /// <summary>Пути, которые загрузили (или грузим) мы.</summary>
    private readonly Dictionary<ResPath, TrackState> _owned = new();

    /// <summary>Результаты фоновой декодировки, ждущие заливки в AL на игровом треде.</summary>
    private readonly ConcurrentQueue<DecodeResult> _decoded = new();

    public DutyMusicCache(IResourceCache resourceCache, IAudioManager audioManager, ISawmill sawmill)
    {
        _resourceCache = resourceCache;
        _audioManager = audioManager;
        _sawmill = sawmill;
    }

    /// <summary>Сколько треков сейчас держим в памяти.</summary>
    public int OwnedCount => _owned.Count;

    /// <summary>
    /// Просит подготовить трек к воспроизведению. Возврат мгновенный: если трека нет в кэше,
    /// декодирование стартует в фоне и завершится в одном из следующих <see cref="Pump"/>.
    /// Если трек не успеет прогреться к моменту проигрывания, движок загрузит его сам — просто
    /// синхронно, с просадкой кадра.
    /// </summary>
    public void Warm(ResPath path)
    {
        if (_owned.ContainsKey(path) || IsAlreadyCached(path))
            return;

        if (!_resourceCache.ContentFileExists(path))
        {
            _sawmill.Warning($"Трек не найден: {path}");
            return;
        }

        _owned[path] = TrackState.Loading;

        Task.Run(() =>
        {
            try
            {
                // ContentFileRead потокобезопасен — движок сам читает VFS из Parallel.ForEach
                // при прогреве текстур на старте.
                using var stream = _resourceCache.ContentFileRead(path);
                var pcm = _audioManager.DecodeAudioOggVorbis(stream);
                _decoded.Enqueue(new DecodeResult(path, pcm, null));
            }
            catch (Exception e)
            {
                _decoded.Enqueue(new DecodeResult(path, null, e));
            }
        });
    }

    /// <summary>
    /// Заливает в OpenAL всё, что успело раскодироваться. Обязана вызываться с игрового треда
    /// каждый кадр — только оттуда можно трогать AL.
    /// </summary>
    public void Pump()
    {
        while (_decoded.TryDequeue(out var result))
        {
            // Трек могли выгрузить (сменился контекст), пока он декодировался — тогда просто
            // выбрасываем результат, чтобы не заливать в AL то, что уже никому не нужно.
            if (!_owned.TryGetValue(result.Path, out var state) || state != TrackState.Loading)
                continue;

            if (result.Error != null || result.Pcm == null)
            {
                _sawmill.Warning($"Не удалось раскодировать трек '{result.Path}': {result.Error?.Message}");
                _owned.Remove(result.Path);
                continue;
            }

            try
            {
                var stream = _audioManager.LoadAudioRaw(
                    result.Pcm.Samples.Span,
                    result.Pcm.Channels,
                    result.Pcm.SampleRate,
                    result.Path.ToString());

                _resourceCache.CacheResource(result.Path, new AudioResource(stream));
                _owned[result.Path] = TrackState.Loaded;

                _sawmill.Debug(
                    "Прогрет трек {Path}: {SizeMb:F1} МБ PCM, держим {Count}",
                    result.Path,
                    result.Pcm.Samples.Length * 2 / 1048576f,
                    _owned.Count);
            }
            catch (Exception e)
            {
                _sawmill.Warning($"Не удалось загрузить трек '{result.Path}' в AL: {e.Message}");
                _owned.Remove(result.Path);
            }
        }
    }

    /// <summary>
    /// Выгружает все наши треки, кроме перечисленных в <paramref name="keep"/>. Треки в процессе
    /// декодирования не трогаем — их отсеет <see cref="Pump"/>, если к тому моменту они станут
    /// не нужны.
    /// </summary>
    public void Trim(IReadOnlySet<ResPath> keep)
    {
        if (_owned.Count == 0)
            return;

        var toRemove = new ValueList<ResPath>();

        foreach (var (path, state) in _owned)
        {
            if (state == TrackState.Loaded && !keep.Contains(path))
                toRemove.Add(path);
        }

        foreach (var path in toRemove)
        {
            _resourceCache.TryRemoveResource<AudioResource>(path);
            _owned.Remove(path);
            _sawmill.Debug("Выгружен трек {Path}, держим {Count}", path, _owned.Count);
        }
    }

    /// <summary>Выгружает всё, что держим.</summary>
    public void Clear()
    {
        foreach (var (path, state) in _owned)
        {
            if (state == TrackState.Loaded)
                _resourceCache.TryRemoveResource<AudioResource>(path);
        }

        _owned.Clear();

        while (_decoded.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Лежит ли трек в кэше ресурсов помимо нас. Нельзя спрашивать через TryGetResource — тот
    /// грузит ресурс синхронно, если его нет, ровно то, чего мы избегаем.
    /// </summary>
    private bool IsAlreadyCached(ResPath path)
    {
        foreach (var (cachedPath, _) in _resourceCache.GetAllResources<AudioResource>())
        {
            if (cachedPath == path)
                return true;
        }

        return false;
    }

    private enum TrackState : byte
    {
        Loading,
        Loaded,
    }

    private sealed record DecodeResult(ResPath Path, AudioPcmData? Pcm, Exception? Error);
}
