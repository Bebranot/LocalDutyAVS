// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.AmbientMusic;
using Content.Shared.GameTicking;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Duty.AmbientMusic;

/// <summary>
/// _Duty: арбитр музыки. Решает, что сейчас важнее звучит, чтобы слои не накладывались.
///
/// До него <see cref="DynamicAmbientMusicSystem"/> играл одновременно с ванильным эмбиентом
/// (в том числе лавалендским) и поверх объявлений кодов — получалась каша из трёх дорожек.
/// Теперь всё, что важнее динамической музыки, перечислено в
/// <see cref="DutyMusicPriorityPrototype"/>, а она молчит, пока такой звук играет.
///
/// Работает опросом живых звуковых сущностей, а не подпиской на их появление: движок сам уже
/// подписан на <c>AudioComponent</c> + <c>ComponentStartup</c>, а вторую подписку на ту же пару
/// RobustToolbox запрещает. Опрос заодно надёжнее: звук, остановленный досрочно, исчезает из
/// выборки сам, без ручного учёта.
///
/// Учитываются только глобальные (<see cref="AudioComponent.Global"/>) звуки: позиционный эмбиент
/// комнат лежит в тех же папках, и без этого фильтра гудящая машина в техах глушила бы музыку
/// навсегда.
/// </summary>
public sealed class DutyMusicDirector : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    /// <summary>Приоритет динамической музыки. Всё, что выше, её глушит.</summary>
    public const int DynamicMusicPriority = 0;

    /// <summary>
    /// Как часто пересчитывать. Опрос идёт по всем звукам в мире, а спрашивают у нас каждый тик —
    /// без кэша это были бы десятки тысяч сравнений строк в секунду на ровном месте.
    /// </summary>
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromMilliseconds(250);

    private TimeSpan _nextRecheck;
    private int _cachedPriority = DynamicMusicPriority;

    /// <summary>Докуда держать тишину после того, как приоритетный звук стих.</summary>
    private TimeSpan _holdUntil;
    private int _holdPriority = DynamicMusicPriority;

    public override void Initialize()
    {
        base.Initialize();

        // Сервер шлёт это событие клиентам по сети: SubscribeLocalEvent тут не сработал бы никогда.
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _nextRecheck = TimeSpan.Zero;
        _cachedPriority = DynamicMusicPriority;
        _holdUntil = TimeSpan.Zero;
        _holdPriority = DynamicMusicPriority;
    }

    /// <summary>Можно ли играть звук с таким приоритетом прямо сейчас.</summary>
    public bool CanPlay(int priority)
    {
        return GetCurrentPriority() <= priority;
    }

    /// <summary>Наибольший приоритет среди звучащих прямо сейчас глобальных звуков.</summary>
    public int GetCurrentPriority()
    {
        var now = _timing.CurTime;
        if (now < _nextRecheck)
            return _cachedPriority;

        _nextRecheck = now + RecheckInterval;
        _cachedPriority = Recheck(now);
        return _cachedPriority;
    }

    private int Recheck(TimeSpan now)
    {
        var top = DynamicMusicPriority;
        var hold = TimeSpan.Zero;

        var query = EntityManager.AllEntityQueryEnumerator<AudioComponent>();
        while (query.MoveNext(out _, out var audio))
        {
            if (!audio.Global)
                continue;

            var file = audio.FileName;
            if (string.IsNullOrEmpty(file))
                continue;

            if (!TryMatch(file, out var rule) || rule.Priority <= top)
                continue;

            top = rule.Priority;
            hold = rule.HoldAfter;
        }

        if (top > DynamicMusicPriority)
        {
            _holdPriority = top;
            _holdUntil = now + hold;
            return top;
        }

        // Звук уже стих, но добиваем паузу, чтобы музыка не врывалась в затухающий хвост.
        return now < _holdUntil ? _holdPriority : DynamicMusicPriority;
    }

    private bool TryMatch(string file, out DutyMusicPriorityPrototype match)
    {
        var normalized = Normalize(file);

        foreach (var rule in _proto.EnumeratePrototypes<DutyMusicPriorityPrototype>())
        {
            foreach (var prefix in rule.PathPrefixes)
            {
                if (!normalized.StartsWith(Normalize(prefix), StringComparison.Ordinal))
                    continue;

                match = rule;
                return true;
            }
        }

        match = default!;
        return false;
    }

    /// <summary>
    /// Приводит путь к сравнимому виду. Ведущий слэш срезается с обеих сторон: движок хранит путь
    /// так же, как он записан в YAML, а вся фича молча перестала бы работать из-за одного символа.
    /// </summary>
    private static string Normalize(string path)
    {
        return path.StartsWith('/') ? path[1..] : path;
    }
}
