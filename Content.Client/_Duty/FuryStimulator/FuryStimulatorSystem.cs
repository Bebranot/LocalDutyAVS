using System.Numerics;
using Content.Shared._Duty.FuryStimulator;
using Content.Shared.Camera;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._Duty.FuryStimulator;

/// <summary>
/// _Duty: клиентская часть Fury-16 — экранный оверлей (<see cref="FuryOverlay"/>), плавная
/// тряска экрана «восьмёркой» через <c>GetEyeOffsetEvent</c> и приглушение окружающего звука
/// (сдвиг Z слушателя): позиционные звуки мира «отдаляются», а персональная музыка фаз
/// (<c>PlayGlobal</c>) остаётся чистой. Общие предсказываемые эффекты (скорость, оружие) —
/// в <see cref="SharedFuryStimulatorSystem"/>.
/// </summary>
public sealed class FuryStimulatorSystem : SharedFuryStimulatorSystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    /// <summary>Частота колебаний «восьмёрки», рад/с.</summary>
    private const float ShakeSpeed = 4f;

    /// <summary>Амплитуда смещения глаза на пике (в тайлах).</summary>
    private const float ShakeAmplitude = 0.12f;

    /// <summary>Целевой сдвиг Z слушателя на пике (базовый ~-5): чем дальше, тем глуше мир.</summary>
    private const float MuffleZOffset = -14f;

    /// <summary>Скорость плавного перехода приглушения, ед./сек.</summary>
    private const float MuffleRampSpeed = 12f;

    private FuryOverlay _fury = default!;

    /// <summary>Идёт ли сейчас приглушение (чтобы захватить/вернуть исходный Z слушателя).</summary>
    private bool _muffling;

    /// <summary>Исходный Z слушателя, захваченный на старте эффекта; к нему возвращаемся по окончании.</summary>
    private float _baseZOffset;

    /// <summary>Текущий плавно доводимый Z слушателя.</summary>
    private float _curZOffset;

    public override void Initialize()
    {
        base.Initialize();

        _fury = new FuryOverlay();
        _overlay.AddOverlay(_fury);

        SubscribeLocalEvent<FuryStimulatorComponent, GetEyeOffsetEvent>(OnGetEyeOffset);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay(_fury);

        // Гарантированно вернуть звук в норму при выгрузке системы.
        if (_muffling)
        {
            _cfg.SetCVar(CVars.AudioZOffset, _baseZOffset);
            _muffling = false;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateMuffle(frameTime);
    }

    /// <summary>
    /// Приглушение окружающего звука на время действия Fury-16: плавно уводим Z слушателя
    /// к <see cref="MuffleZOffset"/> по силе фазы и так же плавно возвращаем обратно.
    /// </summary>
    private void UpdateMuffle(float frameTime)
    {
        var stage = FuryStage.None;
        if (_player.LocalEntity is { } local && TryComp<FuryStimulatorComponent>(local, out var fury))
            stage = fury.Stage;

        var intensity = VisualIntensity(stage);

        if (intensity > 0f)
        {
            if (!_muffling)
            {
                _muffling = true;
                _baseZOffset = _cfg.GetCVar(CVars.AudioZOffset);
                _curZOffset = _baseZOffset;
            }

            var target = float.Lerp(_baseZOffset, MuffleZOffset, intensity);
            _curZOffset = MoveTowards(_curZOffset, target, MuffleRampSpeed * frameTime);
            _cfg.SetCVar(CVars.AudioZOffset, _curZOffset);
        }
        else if (_muffling)
        {
            _curZOffset = MoveTowards(_curZOffset, _baseZOffset, MuffleRampSpeed * frameTime);
            _cfg.SetCVar(CVars.AudioZOffset, _curZOffset);

            if (MathF.Abs(_curZOffset - _baseZOffset) <= 0.01f)
            {
                _cfg.SetCVar(CVars.AudioZOffset, _baseZOffset);
                _muffling = false;
            }
        }
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta)
            return target;
        return current + MathF.Sign(target - current) * maxDelta;
    }

    private void OnGetEyeOffset(Entity<FuryStimulatorComponent> ent, ref GetEyeOffsetEvent args)
    {
        var intensity = VisualIntensity(ent.Comp.Stage);
        if (intensity <= 0f)
            return;

        // Фигура Лиссажу 1:2 → плавная «восьмёрка» влево-вправо.
        var t = (float) _timing.RealTime.TotalSeconds * ShakeSpeed;
        var amp = ShakeAmplitude * intensity;
        args.Offset += new Vector2(MathF.Sin(t), MathF.Sin(2f * t)) * amp;
    }
}
