using Content.Shared.CCVar;
using Content.Shared.Mobs.Systems;
using Content.Shared._Duty.Concussion;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._Duty.Concussion;

/// <summary>
/// _Duty: клиентская часть контузии — оверлей затемнения, звон в ушах (зацикленный,
/// громкость по уровню шкалы) и обработка разовых импульсов от выстрелов/взрывов.
/// </summary>
public sealed class ConcussionSystem : SharedConcussionSystem
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    private ConcussionOverlay _overlay = default!;

    private EntityUid? _ringStream;

    /// <summary>Гистерезис: луп звона стартует на RingStartLevel, гаснет ниже этого коэффициента.</summary>
    private const float RingStopFactor = 0.5f;

    private const float RingMinDb = -18f;
    private const float RingMaxDb = 4f;

    // ── Watchdog против «залипшего» писка ────────────────────────────────────
    // Баг (наблюдался после агост-переселений): уровень «замораживается» (dt≤0 в
    // GetCurrentLevel из-за LastUpdate в будущем) → писк висит на одной громкости вечно.
    // Если играющий звон не МЕНЯЕТ уровень дольше таймаута — принудительно глушим.
    // Нормальное затухание постоянно двигает уровень, поэтому легитимный звон не режется.
    private static readonly TimeSpan RingFrozenTimeout = TimeSpan.FromSeconds(15);
    private const float RingFrozenEpsilon = 0.5f;

    private EntityUid? _ringWatchdogEntity;
    private float _ringWatchdogLevel;
    private TimeSpan _ringWatchdogTime;

    private bool EffectsEnabled => _cfg.GetCVar(DutyCCVars.ConcussionEffectsEnabled);

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new ConcussionOverlay();
        _overlayMan.AddOverlay(_overlay);

        // «Ватность» правим ПОСЛЕ движкового аудио-апдейта (он каждый кадр выставляет
        // окклюзию по геометрии), чтобы поднять её поверх, а не быть затёртыми.
        UpdatesAfter.Add(typeof(AudioSystem));

        SubscribeNetworkEvent<ConcussionImpulseEvent>(OnImpulse);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayMan.RemoveOverlay(_overlay);
        StopRing();
    }

    private void OnImpulse(ConcussionImpulseEvent ev)
    {
        if (!EffectsEnabled || IsLocalSuppressed())
            return;

        switch (ev.Type)
        {
            case ConcussionImpulseType.Shot:
                _overlay.SetShot(ev.Intensity);
                break;
            case ConcussionImpulseType.Blast:
                _overlay.SetBlast(ev.Intensity);
                break;
            case ConcussionImpulseType.Dizzy:
                _overlay.SetDizzy(ev.Intensity);
                break;
        }
    }

    /// <summary>Эффекты гасятся, если выключены в CVar или игрок в крите/мёртв.</summary>
    private bool IsLocalSuppressed()
    {
        var player = _player.LocalEntity;
        if (player == null)
            return true;

        return _mobState.IsIncapacitated(player.Value);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var suppressed = !EffectsEnabled || IsLocalSuppressed();
        _overlay.Suppressed = suppressed;

        var player = _player.LocalEntity;
        if (suppressed || player == null || !TryComp<ConcussionComponent>(player, out var comp))
        {
            StopRing();
            return;
        }

        if (comp.RingSound == null)
            return;

        var level = GetCurrentLevel(comp);
        var stopBelow = comp.RingStartLevel * RingStopFactor;

        if (level < stopBelow)
        {
            StopRing();
            return;
        }

        if (level < comp.RingStartLevel && _ringStream == null)
            return; // ещё не дотянули до старта звона

        // Watchdog: пока писк играет, следим что уровень реально шевелится.
        if (_ringStream != null)
        {
            if (player != _ringWatchdogEntity
                || MathF.Abs(level - _ringWatchdogLevel) > RingFrozenEpsilon)
            {
                _ringWatchdogEntity = player;
                _ringWatchdogLevel = level;
                _ringWatchdogTime = Timing.CurTime;
            }
            else if (Timing.CurTime - _ringWatchdogTime >= RingFrozenTimeout)
            {
                StopRing(); // уровень заморожен 15с — глушим залипший звон
                return;
            }
        }

        var db = LevelToDb(level, comp);

        if (_ringStream == null)
        {
            _ringStream = _audio.PlayGlobal(
                comp.RingSound,
                Filter.Local(),
                false,
                AudioParams.Default.WithLoop(true).WithVolume(db))?.Entity;

            // Старт отсчёта watchdog от текущего состояния.
            _ringWatchdogEntity = player;
            _ringWatchdogLevel = level;
            _ringWatchdogTime = Timing.CurTime;
        }
        else if (TryComp<AudioComponent>(_ringStream, out var audioComp))
        {
            _audio.SetVolume(_ringStream, db, audioComp);
        }
    }

    /// <summary>
    /// «Ватность»: каждый кадр (после движка) поднимаем окклюзию всех источников до уровня,
    /// заданного шкалой — движок превращает её в low-pass (cutoff = exp(-occlusion)).
    /// Звон в ушах не глушим — он «внутри головы».
    /// </summary>
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!EffectsEnabled || IsLocalSuppressed())
            return;

        var player = _player.LocalEntity;
        if (player == null || !TryComp<ConcussionComponent>(player, out var comp))
            return;

        var muffle = LevelToMuffle(GetCurrentLevel(comp), comp);
        if (muffle <= 0.01f)
            return;

        var query = EntityQueryEnumerator<AudioComponent>();
        while (query.MoveNext(out var uid, out var audio))
        {
            if (uid == _ringStream)
                continue;

            if (audio.Occlusion < muffle)
                audio.Occlusion = muffle;
        }
    }

    private static float LevelToMuffle(float level, ConcussionComponent comp)
    {
        var span = comp.MaxLevel - comp.MuffleStartLevel;
        var t = span <= 0f
            ? (level >= comp.MuffleStartLevel ? 1f : 0f)
            : Math.Clamp((level - comp.MuffleStartLevel) / span, 0f, 1f);
        return t * comp.MuffleMaxOcclusion;
    }

    private float LevelToDb(float level, ConcussionComponent comp)
    {
        var span = comp.MaxLevel - comp.RingStartLevel;
        var t = span <= 0f ? 1f : Math.Clamp((level - comp.RingStartLevel) / span, 0f, 1f);
        return RingMinDb + (RingMaxDb - RingMinDb) * t;
    }

    private void StopRing()
    {
        if (_ringStream == null)
            return;

        _audio.Stop(_ringStream);
        _ringStream = null;
    }
}
