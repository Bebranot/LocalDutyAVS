// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Client.Audio;
using Robust.Client.ResourceManagement;
using Robust.Shared.Collections;
using Robust.Shared.Utility;

namespace Content.Client._Duty.AmbientMusic;

/// <summary>
/// _Duty: ограниченный кэш длинных музыкальных треков.
///
/// Зачем: трек живёт в памяти распакованным в 16-битный PCM в буфере OpenAL — пять минут стерео
/// это ~50 МБ. Раньше <see cref="DynamicAmbientMusicSystem"/> грузил разом весь список из
/// прототипа (48 уникальных треков, ~141 минута) прямо в Initialize: ~1.5 ГБ памяти клиента и
/// десятки секунд заморозки при заходе на сервер, причём даже у тех, кто выключил музыку.
///
/// Почему не через <see cref="IResourceCache"/>: движок не умеет выгружать <see cref="AudioResource"/>
/// (<c>TryRemoveResource</c> для него бросает NotSupportedException), так что любой трек, попавший
/// в кэш ресурсов, остаётся там до конца сессии. Поэтому музыку мы держим своими руками:
/// <see cref="AudioStream"/> создаём сами и сами же удаляем через <see cref="AudioStream.Dispose"/>.
///
/// Главное правило: буфер нельзя удалять, пока его играет живой источник — OpenAL откажет
/// (AL_INVALID_OPERATION), буфер утечёт, а движок его уже забудет. Поэтому у каждого трека
/// запоминается последняя аудио-сущность, которой мы его отдали, и <see cref="Trim"/> ждёт, пока
/// она умрёт. Это надёжнее таймеров: <c>Stop</c> и фейд-аут удаляют сущность отложенно.
/// </summary>
public sealed class DutyMusicCache
{
    private static readonly HashSet<ResPath> NothingToKeep = new();

    private readonly IResourceCache _resourceCache;
    private readonly IAudioManager _audioManager;
    private readonly ISawmill _sawmill;

    private readonly Dictionary<ResPath, Entry> _loaded = new();

    /// <summary>
    /// Пути, которые не грузятся (нет файла, битый ogg). Помним, чтобы не долбиться в них каждую
    /// секунду и не блокировать этим выгрузку остального.
    /// </summary>
    private readonly HashSet<ResPath> _failed = new();

    public DutyMusicCache(IResourceCache resourceCache, IAudioManager audioManager, ISawmill sawmill)
    {
        _resourceCache = resourceCache;
        _audioManager = audioManager;
        _sawmill = sawmill;
    }

    /// <summary>Сколько треков сейчас держим в памяти.</summary>
    public int LoadedCount => _loaded.Count;

    /// <summary>
    /// Отдаёт готовый к проигрыванию поток, при необходимости загрузив его прямо сейчас.
    /// Декодирование ogg занимает ~0.6 с на трек, поэтому холодный вызов заметен по кадру —
    /// его надо избегать через <see cref="WarmNext"/>.
    /// </summary>
    public AudioStream? Get(ResPath path)
    {
        if (_loaded.TryGetValue(path, out var entry))
            return entry.Stream;

        return _failed.Contains(path) ? null : Load(path);
    }

    /// <summary>Лежит ли трек в памяти прямо сейчас, то есть можно ли начать его без задержки.</summary>
    public bool IsWarm(ResPath path) => _loaded.ContainsKey(path);

    /// <summary>
    /// Запоминает, какой аудио-сущности отдали буфер: пока она жива, трек выгружать нельзя.
    /// </summary>
    public void NoteUser(ResPath path, EntityUid user)
    {
        if (_loaded.TryGetValue(path, out var entry))
            entry.User = user;
    }

    /// <summary>
    /// Догружает максимум один ещё не прогретый трек из списка и возвращает true, если что-то
    /// сделал. По одному за вызов — чтобы не сложить в один кадр несколько декодирований.
    /// Порядок списка = приоритет: первым греется то, чей холодный старт больнее всего.
    /// </summary>
    public bool WarmNext(IEnumerable<ResPath> paths)
    {
        foreach (var path in paths)
        {
            if (_loaded.ContainsKey(path) || _failed.Contains(path))
                continue;

            Load(path);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Выгружает всё, кроме перечисленного в <paramref name="keep"/>. Треки, которые всё ещё
    /// играет живой источник, остаются до следующего вызова.
    /// </summary>
    public void Trim(IReadOnlySet<ResPath> keep, Predicate<EntityUid> isAlive)
    {
        if (_loaded.Count == 0)
            return;

        var toRemove = new ValueList<ResPath>();

        foreach (var (path, entry) in _loaded)
        {
            if (keep.Contains(path))
                continue;

            if (entry.User is { } user && isAlive(user))
                continue;

            toRemove.Add(path);
        }

        foreach (var path in toRemove)
        {
            _loaded[path].Stream.Dispose();
            _loaded.Remove(path);
            _sawmill.Debug("Выгружен трек {Path}, держим {Count}", path, _loaded.Count);
        }
    }

    /// <summary>Выгружает всё, что не занято живым источником.</summary>
    public void Clear(Predicate<EntityUid> isAlive)
    {
        Trim(NothingToKeep, isAlive);
    }

    private AudioStream? Load(ResPath path)
    {
        if (!_resourceCache.ContentFileExists(path))
        {
            _sawmill.Warning($"Трек не найден: {path}");
            _failed.Add(path);
            return null;
        }

        try
        {
            using var file = _resourceCache.ContentFileRead(path);
            var stream = _audioManager.LoadAudioOggVorbis(file, path.ToString());
            _loaded[path] = new Entry(stream);

            _sawmill.Debug(
                "Прогрет трек {Path} ({Length:F0} с), держим {Count}",
                path,
                stream.Length.TotalSeconds,
                _loaded.Count);

            return stream;
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Не удалось загрузить трек '{path}': {e.Message}");
            _failed.Add(path);
            return null;
        }
    }

    private sealed class Entry
    {
        public readonly AudioStream Stream;

        /// <summary>Последняя аудио-сущность, которой отдали этот буфер.</summary>
        public EntityUid? User;

        public Entry(AudioStream stream)
        {
            Stream = stream;
        }
    }
}
