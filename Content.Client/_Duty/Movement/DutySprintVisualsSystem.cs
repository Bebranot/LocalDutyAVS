// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client.Stunnable;
using Content.Shared._Duty.Movement;
using Content.Shared.CCVar;
using Content.Shared.Mobs.Systems;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Movement;

/// <summary>
/// _Duty: визуал спринта — пыль из-под ног и «загнанность» спрайта. Порт подачи с Goob Station,
/// но привязанный к НАШЕМУ пулу выносливости (<see cref="DutyStaminaComponent"/>), а не к боевой
/// стамине.
///
/// Всё здесь чисто клиентское: облака пыли — клиентские сущности, тряска — анимация смещения
/// спрайта. Видно и чужой спринт: <c>WantsSprint</c> и <c>HeldMoveButtons</c> сетевые, так что
/// предикат «реально бежит» считается и для других игроков.
/// </summary>
public sealed class DutySprintVisualsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly AnimationPlayerSystem _animation = default!;
    [Dependency] private readonly DutySprintSystem _sprint = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly StunSystem _stun = default!;

    /// <summary>
    /// Ключ анимации тряски. НЕ совпадает с ключом "stamina" боевой стамины
    /// (<c>Content.Client/Damage/Systems/StaminaSystem.cs</c>) намеренно: обе анимации крутят одно
    /// и то же <c>sprite.Offset</c>, поэтому одновременно их запускать нельзя — ниже мы уступаем
    /// боевой, если та уже играет.
    /// </summary>
    private const string ShakeAnimationKey = "duty-endurance-shake";

    /// <summary>Во сколько раз слабее трясти при включённом «уменьшении движения».</summary>
    private const float ReducedMotionScale = 0.3f;

    private bool _reducedMotion;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyStaminaComponent, AnimationCompletedEvent>(OnAnimationCompleted);

        Subs.CVar(_cfg, CCVars.ReducedMotion, value => _reducedMotion = value, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Ре-предсказание гоняет Update по нескольку раз за тик — без этой отсечки пыль сыпалась бы
        // пачками, а тряска перезапускалась бы вхолостую.
        if (!_timing.IsFirstTimePredicted)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<DutyStaminaComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var comp, out var sprite))
        {
            UpdateDust(uid, comp, now);
            UpdateShake((uid, comp, sprite));
        }
    }

    // ── Пыль из-под ног ───────────────────────────────────────────────────────

    private void UpdateDust(EntityUid uid, DutyStaminaComponent comp, TimeSpan now)
    {
        var sprinting = _sprint.IsSprinting(uid, comp);

        // Фронт «побежал» — одно большое облако.
        if (sprinting && !comp.WasSprintingVisual)
        {
            SpawnCloud(uid, comp.SprintCloud, "sprint_cloud", 0.48f);
            comp.LastStepTime = now;
        }
        // Дальше — мелкие облачка с шагом TimeBetweenSteps.
        else if (sprinting && (now - comp.LastStepTime).TotalSeconds >= comp.TimeBetweenSteps)
        {
            SpawnCloud(uid, comp.SprintCloudSmall, "sprint_cloud_small", 0.34f);
            comp.LastStepTime = now;
        }

        comp.WasSprintingVisual = sprinting;
    }

    /// <summary>
    /// Спавнит облако под ногами и проигрывает его состояние ОДИН раз с нулевого кадра. Через
    /// AnimationPlayer, а не авто-анимацией RSI: авто-анимация идёт от общего времени, поэтому
    /// свежее облако появлялось бы с середины кадров.
    /// </summary>
    private void SpawnCloud(EntityUid uid, string proto, string state, float length)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var effect = Spawn(proto, Transform(uid).Coordinates);

        _animation.Play(effect, new Animation
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = DutySprintVisualLayers.Base,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(new RSI.StateId(state), 0f) },
                },
            },
        }, state);
    }

    // ── Тряска спрайта от усталости ───────────────────────────────────────────

    private void UpdateShake(Entity<DutyStaminaComponent, SpriteComponent> ent)
    {
        if (!ShouldShake(ent))
        {
            StopShake(ent);
            return;
        }

        if (_animation.HasRunningAnimation(ent.Owner, ShakeAnimationKey))
            return;

        ent.Comp1.StartOffset = ent.Comp2.Offset;
        PlayShake(ent);
    }

    /// <summary>
    /// Трясёт ли сейчас: выносливость ниже порога, существо живо, и боевая стамина НЕ крутит свою
    /// анимацию усталости. Последнее обязательно — обе анимации двигают один и тот же
    /// <c>sprite.Offset</c>, и одновременный запуск дал бы дёрганый мусор вместо дыхания.
    /// </summary>
    private bool ShouldShake(Entity<DutyStaminaComponent, SpriteComponent> ent)
    {
        var comp = ent.Comp1;

        if (comp.Max <= 0f || comp.Current >= comp.Max * comp.ShakeThreshold)
            return false;

        if (_mobState.IsIncapacitated(ent.Owner))
            return false;

        return !_animation.HasRunningAnimation(ent.Owner, "stamina");
    }

    private void StopShake(Entity<DutyStaminaComponent, SpriteComponent> ent)
    {
        if (!_animation.HasRunningAnimation(ent.Owner, ShakeAnimationKey))
            return;

        _animation.Stop(ent.Owner, ShakeAnimationKey);
        _sprite.SetOffset((ent.Owner, ent.Comp2), ent.Comp1.StartOffset);
    }

    private void OnAnimationCompleted(Entity<DutyStaminaComponent> ent, ref AnimationCompletedEvent args)
    {
        if (args.Key != ShakeAnimationKey || !args.Finished || !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        // Отдышались — вернуть спрайт на место, иначе он застынет смещённым.
        if (!ShouldShake((ent.Owner, ent.Comp, sprite)))
        {
            _sprite.SetOffset((ent.Owner, sprite), ent.Comp.StartOffset);
            return;
        }

        PlayShake((ent.Owner, ent.Comp, sprite));
    }

    /// <summary>
    /// Гоняет ту же анимацию усталости, что и боевая стамина — переиспользуем готовый
    /// <see cref="StunSystem.GetFatigueAnimation"/>, чтобы «загнанность» выглядела одинаково,
    /// от чего бы она ни наступила. Шкала: чем меньше выносливости, тем чаще и размашистее.
    /// </summary>
    private void PlayShake(Entity<DutyStaminaComponent, SpriteComponent> ent)
    {
        var comp = ent.Comp1;

        var threshold = comp.Max * comp.ShakeThreshold;
        if (threshold <= 0f)
            return;

        // 0 на пороге → 1 при нулевой выносливости.
        var step = Math.Clamp((threshold - comp.Current) / threshold, 0f, 1f);

        var frequency = comp.FrequencyMin + step * comp.FrequencyMod;
        var jitter = comp.JitterAmplitudeMin + step * comp.JitterAmplitudeMod;
        var breathing = comp.BreathingAmplitudeMin + step * comp.BreathingAmplitudeMod;

        // Доступность: тряска остаётся, но заметно слабее — как в BrainTraumaVisualsSystem.
        if (_reducedMotion)
        {
            jitter *= ReducedMotionScale;
            breathing *= ReducedMotionScale;
        }

        _animation.Play(ent.Owner,
            _stun.GetFatigueAnimation(
                ent.Comp2,
                frequency,
                comp.Jitters,
                jitter * comp.JitterMin,
                jitter * comp.JitterMax,
                breathing,
                comp.StartOffset,
                ref comp.LastJitter),
            ShakeAnimationKey);
    }
}

/// <summary>Слой спрайта облака пыли — на него ссылается <c>map:</c> в прототипах эффектов.</summary>
public enum DutySprintVisualLayers : byte
{
    Base,
}
