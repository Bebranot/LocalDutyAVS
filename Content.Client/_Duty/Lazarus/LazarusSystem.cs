using Content.Shared._Duty.Lazarus;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Lazarus;

/// <summary>
/// Клиентская часть эффекта Лазаруса. Ловит <see cref="LazarusTriggeredEvent"/> от
/// сервера (приходит только тому, у кого сработало), запускает полноэкранную
/// кинематику <see cref="LazarusOverlay"/> и проигрывает музыку. По завершении
/// анимации оверлей снимается.
/// </summary>
public sealed class LazarusSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IResourceCache _cache = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private LazarusOverlay? _current;

    // Отложенное вступление основного звука (наезжает на сердцебиение).
    private SoundSpecifier? _pendingLastStand;
    private float _pendingLastStandVolume;
    private TimeSpan _lastStandTime;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<LazarusTriggeredEvent>(OnTriggered);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Clear();
    }

    private void OnTriggered(LazarusTriggeredEvent ev)
    {
        // Перекрытие предыдущей кинематики (на всякий случай) — начинаем заново.
        Clear();

        var overlay = new LazarusOverlay(
            _clyde,
            _timing,
            _cache,
            PickPhrase(),
            ev.BlackoutFadeIn,
            ev.BlackoutHold,
            ev.BlackoutFadeOut);

        _overlay.AddOverlay(overlay);
        _current = overlay;

        // Сердцебиение — сразу (подводка на затемнении).
        if (ev.Heartbeat != null)
            _audio.PlayGlobal(ev.Heartbeat, Filter.Local(), false,
                AudioParams.Default.WithVolume(ev.HeartbeatVolume));

        // Основной звук — с задержкой, чтобы наехать на сердцебиение.
        _pendingLastStand = ev.LastStand;
        _pendingLastStandVolume = ev.LastStandVolume;
        _lastStandTime = _timing.RealTime + TimeSpan.FromSeconds(MathF.Max(ev.LastStandDelay, 0f));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingLastStand != null && _timing.RealTime >= _lastStandTime)
        {
            _audio.PlayGlobal(_pendingLastStand, Filter.Local(), false,
                AudioParams.Default.WithVolume(_pendingLastStandVolume));
            _pendingLastStand = null;
        }

        if (_current is { Finished: true })
            Clear();
    }

    private void Clear()
    {
        if (_current == null)
            return;

        _overlay.RemoveOverlay(_current);
        _current = null;
    }

    /// <summary>Случайная фраза из локали duty-lazarus-phrase-1..N.</summary>
    private string PickPhrase()
    {
        var variants = new List<string>();
        var i = 1;
        while (Loc.TryGetString($"duty-lazarus-phrase-{i}", out var phrase))
        {
            variants.Add(phrase);
            i++;
        }

        return variants.Count > 0 ? _random.Pick(variants) : string.Empty;
    }
}
