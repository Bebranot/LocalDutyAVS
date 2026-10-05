// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client.Movement.Systems;
using Content.Shared._Duty.Recoil;
using Content.Shared.Camera;
using Content.Shared.CCVar;
using Content.Shared.Mech.Components;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Recoil;

/// <summary>
/// Отдача камеры у стрелка. Вместо одного линейного сдвига (штатный SharedCameraRecoilSystem: камера
/// «залипает», потом щёлкает назад) — три слоя, как принято в шутерах:
/// <list type="number">
/// <item>толчок — недодемпфированная пружина: резкий удар назад за ~40 мс, лёгкий перелёт за ноль
/// и успокоение за четверть секунды;</item>
/// <item>увод — боковая составляющая, которая в очереди держит одну сторону (ствол «ведёт»);</item>
/// <item>тряска — накопительная «травма» с квадратичным откликом: одиночный выстрел почти не трясёт,
/// длинная очередь крупным калибром — заметно дрожит.</item>
/// </list>
/// Сила — от калибра (<see cref="SharedDutyRecoilSystem.GetCaliberPower"/>), ствола (cameraRecoilScalar
/// с учётом прицеливания и обвесов), хвата, одежды и серверного множителя. Игрок отключить не может.
/// </summary>
public sealed partial class DutyGunRecoilSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedDutyRecoilSystem _recoil = default!;

    /// <summary>
    /// Пик толчка в тайлах при мощности 1 (7.62x39) и прочих множителях 1. Меньше ~0.3 тайла толчок
    /// на экране не читается — калибры сливаются в одно «ничего».
    /// </summary>
    private const float BaseKick = 0.45f;

    /// <summary>Потолок смещения толчка — как бы ни складывались множители (у ADT есть scalar 6).</summary>
    private const float MaxOffset = 1f;

    /// <summary>Коэффициент демпфирования: 0.7 — перелёт за ноль ~5%, камера уверенно садится на место.</summary>
    private const float Damping = 0.7f;

    /// <summary>
    /// Частота пружины, Гц: лёгкий калибр — быстрый щелчок (пик ~55 мс), тяжёлый — «весомый» качок
    /// (пик ~90 мс, возврат ~0.5 с). Быстрее ~4 Гц толчок укладывается в 2–3 кадра и не виден глазу.
    /// </summary>
    private const float FreqLight = 3.2f;
    private const float FreqHeavy = 2f;

    /// <summary>Боковой увод — доля от основного толчка; в очереди сторона меняется с этой вероятностью.</summary>
    private const float DriftMin = 0.25f;
    private const float DriftMax = 0.45f;
    private const float DriftFlipChance = 0.25f;

    /// <summary>Пауза, после которой очередь считается новой и сторона увода выбирается заново.</summary>
    private static readonly TimeSpan BurstGap = TimeSpan.FromSeconds(0.35);

    /// <summary>Тряска: прибавка травмы за выстрел мощности 1, спад в секунду, амплитуда при травме 1.</summary>
    private const float TraumaPerShot = 0.2f;
    private const float TraumaDecay = 1.2f;
    private const float MaxShake = 0.12f;

    /// <summary>Двуручное оружие из одной руки — плечо не упирается, толкает сильнее.</summary>
    private const float OneHandedMultiplier = 1.35f;

    /// <summary>Мех гасит большую часть толчка; одежда пилота при этом не важна.</summary>
    private const float MechMultiplier = 0.5f;

    /// <summary>Шаг интегрирования: пружина на ~35 рад/с с полунеявным Эйлером устойчива и с запасом.</summary>
    private const float MaxSubstep = 1f / 120f;
    private const float MaxFrameTime = 0.05f;

    /// <summary>
    /// Отношение пика смещения к начальной скорости, умноженной на 1/ω, для недодемпфированной пружины:
    /// x_peak = v0 / ω · K(ζ). Нужно, чтобы задавать пик в тайлах, а не скорость.
    /// </summary>
    private static readonly float PeakFactor = ComputePeakFactor(Damping);

    private float _scale = 1f;

    public override void Initialize()
    {
        base.Initialize();

        // До ContentEyeSystem — смещение глаза в этом же кадре, без отставания на кадр.
        UpdatesBefore.Add(typeof(ContentEyeSystem));

        SubscribeLocalEvent<DutyGunRecoilComponent, GetEyeOffsetEvent>(OnGetEyeOffset);

        Subs.CVar(_cfg, DutyCCVars.RecoilCameraScale, v => _scale = Math.Clamp(v, 0f, 3f), true);
    }

    /// <summary>
    /// Выстрел локального игрока. Звать только под IsFirstTimePredicted (это делает GunSystem.Recoil).
    /// </summary>
    /// <param name="direction">От цели к стрелку — куда толкает.</param>
    public void Kick(EntityUid user, EntityUid gun, Vector2 direction, float gunScalar, EntityUid? ammo, IShootable? shootable, bool thrown)
    {
        if (gunScalar <= 0f || _scale <= 0f || direction.LengthSquared() < 0.0001f)
            return;

        var power = thrown ? SharedDutyRecoilSystem.ThrowPower : _recoil.GetCaliberPower(ammo, shootable);

        var mult = gunScalar * _scale;

        if (HasComp<MechPilotComponent>(user))
            mult *= MechMultiplier;
        else
            mult *= _recoil.GetCameraWearMultiplier(user);

        if (TryComp<WieldableComponent>(gun, out var wield) && !wield.Wielded)
            mult *= OneHandedMultiplier;

        var peak = Math.Min(BaseKick * power * mult, MaxOffset);
        if (peak <= 0.001f)
            return;

        var recoil = EnsureComp<DutyGunRecoilComponent>(user);
        if (recoil.NoisePhase == Vector4.Zero)
        {
            recoil.NoisePhase = new Vector4(
                _random.NextFloat(0f, MathF.Tau), _random.NextFloat(0f, MathF.Tau),
                _random.NextFloat(0f, MathF.Tau), _random.NextFloat(0f, MathF.Tau));
        }

        // Тяжелее калибр — ниже частота: та же амплитуда ощущается как «качок», а не щелчок.
        var heaviness = Math.Clamp((power - 0.4f) / 1.8f, 0f, 1f);
        recoil.Omega = MathF.Tau * MathHelper.Lerp(FreqLight, FreqHeavy, heaviness);

        // Увод: в пределах очереди сторона держится, изредка перескакивает; новая очередь — новая сторона.
        var now = _timing.RealTime;
        if (now - recoil.LastShot > BurstGap)
            recoil.DriftSign = _random.Prob(0.5f) ? 1f : -1f;
        else if (_random.Prob(DriftFlipChance))
            recoil.DriftSign = -recoil.DriftSign;
        recoil.LastShot = now;

        var back = Vector2.Normalize(direction);
        var side = new Vector2(-back.Y, back.X) * recoil.DriftSign * _random.NextFloat(DriftMin, DriftMax);

        // Импульс в скорость: v0 = x_peak · ω / K. Уже идущий толчок не обнуляем — очередь складывается.
        recoil.Velocity += (back + side) * (peak * recoil.Omega / PeakFactor);

        recoil.Trauma = Math.Min(recoil.Trauma + TraumaPerShot * power * mult, 1f);
    }

    private void OnGetEyeOffset(Entity<DutyGunRecoilComponent> ent, ref GetEyeOffsetEvent args)
    {
        args.Offset += ent.Comp.Offset;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var dt = Math.Min(frameTime, MaxFrameTime);
        var query = EntityQueryEnumerator<DutyGunRecoilComponent>();

        while (query.MoveNext(out var uid, out var recoil))
        {
            Integrate(recoil, dt);

            recoil.Trauma = Math.Max(recoil.Trauma - TraumaDecay * dt, 0f);
            recoil.NoiseTime += dt;

            var shake = Vector2.Zero;
            if (recoil.Trauma > 0f)
            {
                // Квадрат травмы: слабая — почти не видна, полная — ощутимая дрожь.
                var amp = MaxShake * _scale * recoil.Trauma * recoil.Trauma;
                shake = SmoothNoise(recoil.NoiseTime, recoil.NoisePhase) * amp;
            }

            recoil.Offset = recoil.Position + shake;

            if (recoil.Trauma <= 0f &&
                recoil.Position.LengthSquared() < 0.000001f &&
                recoil.Velocity.LengthSquared() < 0.0001f)
            {
                recoil.Offset = Vector2.Zero;
                RemCompDeferred<DutyGunRecoilComponent>(uid);
            }
        }
    }

    /// <summary>Пружина-демпфер x'' = −ω²x − 2ζωx', полунеявный Эйлер с подшагами.</summary>
    private static void Integrate(DutyGunRecoilComponent recoil, float dt)
    {
        var omega = recoil.Omega;
        var steps = Math.Max(1, (int) MathF.Ceiling(dt / MaxSubstep));
        var h = dt / steps;

        for (var i = 0; i < steps; i++)
        {
            var accel = -omega * omega * recoil.Position - 2f * Damping * omega * recoil.Velocity;
            recoil.Velocity += accel * h;
            recoil.Position += recoil.Velocity * h;
        }

        var length = recoil.Position.Length();
        if (length > MaxOffset)
        {
            // Упёрлись в потолок — гасим скорость наружу, иначе следующий кадр снова вылетит.
            var dir = recoil.Position / length;
            recoil.Position = dir * MaxOffset;
            var outward = Vector2.Dot(recoil.Velocity, dir);
            if (outward > 0f)
                recoil.Velocity -= dir * outward;
        }
    }

    /// <summary>
    /// Плавный псевдошум из несоизмеримых синусов (11–19 Гц): без скачков на высоком FPS,
    /// в отличие от случайного вектора каждый кадр.
    /// </summary>
    private static Vector2 SmoothNoise(float t, Vector4 phase)
    {
        var x = MathF.Sin(t * 71.6f + phase.X) * 0.6f + MathF.Sin(t * 113.1f + phase.Y) * 0.4f;
        var y = MathF.Sin(t * 83.2f + phase.Z) * 0.6f + MathF.Sin(t * 119.4f + phase.W) * 0.4f;
        return new Vector2(x, y);
    }

    private static float ComputePeakFactor(float zeta)
    {
        var root = MathF.Sqrt(1f - zeta * zeta);
        var phi = MathF.Atan2(root, zeta);
        return MathF.Exp(-zeta * phi / root) * MathF.Sin(phi) / root;
    }
}
