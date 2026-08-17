using Content.Server.Explosion.EntitySystems;
using Content.Shared._Duty.FuryStimulator;
using Content.Shared.Camera;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Gibbing;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.FuryStimulator;

/// <summary>
/// _Duty: авторитетная логика Fury-16 — таймер из 4 фаз (Ввод → Разгон → Пик → Спад),
/// баффы/дебаффы через штатные системы, pop-up атмосфера, персональная музыка на каждую фазу
/// с crossfade (fade 0.5 c) и передоз (гиб без урона окружающим).
/// Общая предсказываемая математика и таблицы силы по фазам — в <see cref="SharedFuryStimulatorSystem"/>.
/// </summary>
public sealed class FuryStimulatorSystem : SharedFuryStimulatorSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly GibbingSystem _gib = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    private static readonly string[] WarningPopups =
        { "fury-popup-warn-1", "fury-popup-warn-2", "fury-popup-warn-3" };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FuryStimulatorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<FuryStimulatorComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FuryStimulatorComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
        SubscribeLocalEvent<FuryStimulatorComponent, BeforeStaminaDamageEvent>(OnBeforeStamina);
        SubscribeLocalEvent<FuryStimulatorComponent, DidEquipHandEvent>(OnUserEquippedHand);

        SubscribeLocalEvent<FuryGunPenaltyComponent, GotUnequippedHandEvent>(OnGunUnequipped);
        SubscribeLocalEvent<FuryMeleeBonusComponent, GotUnequippedHandEvent>(OnMeleeUnequipped);

        SubscribeLocalEvent<FuryStimulatorInjectorComponent, UseInHandEvent>(OnInjectorUse);
        SubscribeLocalEvent<FuryStimulatorInjectorComponent, AfterInteractEvent>(OnInjectorAfterInteract);
    }

    // ── Ввод дозы ─────────────────────────────────────────────

    /// <summary>Ввести дозу Fury-16. Повторный ввод во время действия — передоз.</summary>
    public void Inject(EntityUid target, EntityUid? source = null)
    {
        if (TryComp<FuryStimulatorComponent>(target, out var existing))
        {
            Overdose((target, existing), source);
            return;
        }

        var comp = AddComp<FuryStimulatorComponent>(target);
        StartPhase((target, comp), FuryStage.Intro);
    }

    private void Overdose(Entity<FuryStimulatorComponent> ent, EntityUid? source = null)
    {
        var uid = ent.Owner;

        _audio.PlayPvs(ent.Comp.OverdoseSound, uid);

        if (ent.Comp.OverdoseExplosionIntensity > 0f)
        {
            _explosion.QueueExplosion(
                uid,
                ent.Comp.OverdoseExplosionType,
                ent.Comp.OverdoseExplosionIntensity,
                1f,
                ent.Comp.OverdoseExplosionIntensity,
                // Админ-лог взрыва пишет user как виновника. При принудительном уколе (второй
                // дозой) source — это тот, кто колол, а не жертва; без него передоз от чужой
                // руки в логе выглядит как самоубийство.
                user: source ?? uid);
        }

        // Расчленение + мгновенная смерть. Удаление сущности вызовет ComponentShutdown → полная очистка.
        _gib.Gib(uid);
    }

    // ── Таймер фаз ────────────────────────────────────────────

    private float DurationFor(FuryStimulatorComponent comp, FuryStage stage) => stage switch
    {
        FuryStage.Intro => comp.IntroDuration,
        FuryStage.RampUp => comp.RampDuration,
        FuryStage.Peak => comp.PeakDuration,
        FuryStage.Decline => comp.DeclineDuration,
        _ => 0f,
    };

    private static FuryStage NextStage(FuryStage stage) => stage switch
    {
        FuryStage.Intro => FuryStage.RampUp,
        FuryStage.RampUp => FuryStage.Peak,
        FuryStage.Peak => FuryStage.Decline,
        _ => FuryStage.None,
    };

    private void StartPhase(Entity<FuryStimulatorComponent> ent, FuryStage stage)
    {
        var comp = ent.Comp;
        var old = comp.Stage;

        comp.Stage = stage;
        comp.PhaseEnd = _timing.CurTime + TimeSpan.FromSeconds(DurationFor(comp, stage));

        OnStageChanged(ent, old, stage);
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FuryStimulatorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.PhaseEnd)
            {
                var next = NextStage(comp.Stage);
                if (next == FuryStage.None)
                {
                    RemCompDeferred<FuryStimulatorComponent>(uid);
                    continue;
                }

                StartPhase((uid, comp), next);
            }

            if ((comp.Stage == FuryStage.Intro || comp.Stage == FuryStage.Decline) && now >= comp.NextPopup)
            {
                _popup.PopupEntity(Loc.GetString(_random.Pick(WarningPopups)), uid, uid, PopupType.LargeCaution);
                comp.NextPopup = now + TimeSpan.FromSeconds(_random.NextFloat(comp.PopupIntervalMin, comp.PopupIntervalMax));
            }

            // Маленький хил, пока носитель в крите: препарат не даёт умереть, медленно вытягивая из крита.
            // Вне крита таймер сбрасываем, чтобы при входе в крит лечение началось сразу.
            if (TryComp<MobStateComponent>(uid, out var mobState) && mobState.CurrentState == MobState.Critical)
            {
                if (now >= comp.NextCritHeal)
                {
                    HealCrit(uid, comp);
                    comp.NextCritHeal = now + TimeSpan.FromSeconds(comp.CritHealInterval);
                }
            }
            else
            {
                comp.NextCritHeal = TimeSpan.Zero;
            }

            FadeMusic(comp, frameTime);
        }
    }

    private void OnStageChanged(Entity<FuryStimulatorComponent> ent, FuryStage old, FuryStage @new)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;

        _movement.RefreshMovementSpeedModifiers(uid);
        RefreshWeaponEffects(ent);
        UpdateMusic(ent);

        // Разовый мощный резкий толчок камеры в самом начале действия (укол).
        if (@new == FuryStage.Intro && old == FuryStage.None && comp.IntroKickStrength > 0f)
            _recoil.KickCamera(uid, _random.NextAngle().ToVec() * comp.IntroKickStrength);

        if (@new == FuryStage.Peak && old != FuryStage.Peak)
            _popup.PopupEntity(Loc.GetString("fury-popup-peak"), uid, uid, PopupType.Large);

        // Тревожные pop-up на вводе и спаде.
        var warnNow = @new is FuryStage.Intro or FuryStage.Decline;
        var warnOld = old is FuryStage.Intro or FuryStage.Decline;
        if (warnNow && !warnOld)
            comp.NextPopup = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(comp.PopupIntervalMin, comp.PopupIntervalMax));
    }

    // ── Эффекты оружия в руках ────────────────────────────────

    private void RefreshWeaponEffects(Entity<FuryStimulatorComponent> ent)
    {
        var buffFactor = BuffFactor(ent.Comp.Stage);
        var gunFactor = GunFactor(ent.Comp.Stage);

        // Безоружные атаки (у моба свой MeleeWeaponComponent).
        ApplyMeleeMarker(ent.Owner, buffFactor, ent.Comp);

        foreach (var item in _hands.EnumerateHeld(ent.Owner))
        {
            ApplyGunMarker(item, gunFactor, ent.Comp);
            ApplyMeleeMarker(item, buffFactor, ent.Comp);
        }
    }

    private void ApplyGunMarker(EntityUid item, float gunFactor, FuryStimulatorComponent comp)
    {
        if (!HasComp<GunComponent>(item))
            return;

        if (gunFactor > 0f)
        {
            var pen = EnsureComp<FuryGunPenaltyComponent>(item);
            pen.Factor = gunFactor;
            Dirty(item, pen);
            comp.AffectedWeapons.Add(item);
            _gun.RefreshModifiers(item);
        }
        else if (HasComp<FuryGunPenaltyComponent>(item))
        {
            RemComp<FuryGunPenaltyComponent>(item);
            _gun.RefreshModifiers(item);
        }
    }

    private void ApplyMeleeMarker(EntityUid item, float buffFactor, FuryStimulatorComponent comp)
    {
        if (!HasComp<MeleeWeaponComponent>(item))
            return;

        if (buffFactor > 0f)
        {
            var bonus = EnsureComp<FuryMeleeBonusComponent>(item);
            bonus.Factor = buffFactor;
            Dirty(item, bonus);
            comp.AffectedWeapons.Add(item);
        }
        else if (HasComp<FuryMeleeBonusComponent>(item))
        {
            RemComp<FuryMeleeBonusComponent>(item);
        }
    }

    private void OnUserEquippedHand(Entity<FuryStimulatorComponent> ent, ref DidEquipHandEvent args)
    {
        ApplyGunMarker(args.Equipped, GunFactor(ent.Comp.Stage), ent.Comp);
        ApplyMeleeMarker(args.Equipped, BuffFactor(ent.Comp.Stage), ent.Comp);
    }

    private void OnGunUnequipped(Entity<FuryGunPenaltyComponent> ent, ref GotUnequippedHandEvent args)
    {
        RemComp<FuryGunPenaltyComponent>(ent);
        if (Exists(ent))
            _gun.RefreshModifiers(ent.Owner);
    }

    private void OnMeleeUnequipped(Entity<FuryMeleeBonusComponent> ent, ref GotUnequippedHandEvent args)
    {
        RemComp<FuryMeleeBonusComponent>(ent);
    }

    // ── Урон и боль ───────────────────────────────────────────

    private void OnBeforeDamage(Entity<FuryStimulatorComponent> ent, ref BeforeDamageChangedEvent args)
    {
        var factor = BuffFactor(ent.Comp.Stage);
        if (factor <= 0f)
            return;

        var mult = (FixedPoint2) (1f - DamageResist * factor);
        args.Damage *= mult;
    }

    private void OnBeforeStamina(Entity<FuryStimulatorComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        // Неуязвимость к боли — только пик/спад. Гасим лишь урон стамины, восстановление не трогаем.
        if (args.Value > 0f && IsPainImmune(ent.Comp.Stage))
            args.Cancelled = true;
    }

    /// <summary>
    /// Пропорционально снижает суммарный урон на <see cref="FuryStimulatorComponent.CritHealAmount"/>,
    /// сохраняя соотношение типов урона (как в системе Лазаруса). Вызывается только в крите.
    /// </summary>
    private void HealCrit(EntityUid uid, FuryStimulatorComponent comp)
    {
        var current = _damageable.GetTotalDamage(uid);
        if (current <= 0)
            return;

        var target = current - FixedPoint2.New(comp.CritHealAmount);
        if (target < FixedPoint2.Zero)
            target = FixedPoint2.Zero;

        var factor = (float) (target.Float() / current.Float());
        var scaled = _damageable.GetAllDamage(uid) * factor;
        _damageable.SetDamage(uid, scaled);
    }

    // ── Музыка (персональная, crossfade, fade 0.5 c) ──────────

    private (int Track, SoundSpecifier? Sound) MusicFor(FuryStimulatorComponent comp, FuryStage stage) => stage switch
    {
        FuryStage.Intro => (0, comp.MusicIntro),
        FuryStage.RampUp => (1, comp.MusicRamp),
        FuryStage.Peak => (2, comp.MusicPeak),
        FuryStage.Decline => (3, comp.MusicDecline),
        _ => (-1, null),
    };

    private void UpdateMusic(Entity<FuryStimulatorComponent> ent)
    {
        var comp = ent.Comp;
        var (track, sound) = MusicFor(comp, comp.Stage);
        if (track == comp.MusicTrack)
            return;

        // Текущий стрим уводим в затухание (держим не более одного затухающего).
        if (comp.MusicStream != null)
        {
            if (comp.MusicStreamFading != null)
                Del(comp.MusicStreamFading);

            comp.MusicStreamFading = comp.MusicStream;
            comp.MusicGainFading = comp.MusicGain;
        }

        comp.MusicStream = null;
        comp.MusicGain = 0f;

        if (sound != null)
        {
            var res = _audio.PlayGlobal(sound, ent.Owner, AudioParams.Default.WithLoop(true));
            if (res != null)
            {
                comp.MusicStream = res.Value.Entity;
                comp.MusicGain = 0f;
                _audio.SetGain(comp.MusicStream, 0f);
            }
        }

        comp.MusicTrack = track;
    }

    private void FadeMusic(FuryStimulatorComponent comp, float dt)
    {
        var step = comp.MusicFadeSpeed * dt;

        if (comp.MusicStream is { } stream)
        {
            if (!Exists(stream))
            {
                comp.MusicStream = null;
            }
            else if (comp.MusicGain < comp.MusicVolume)
            {
                comp.MusicGain = MathF.Min(comp.MusicVolume, comp.MusicGain + step);
                _audio.SetGain(stream, comp.MusicGain);
            }
        }

        if (comp.MusicStreamFading is { } fading)
        {
            if (!Exists(fading))
            {
                comp.MusicStreamFading = null;
            }
            else
            {
                comp.MusicGainFading = MathF.Max(0f, comp.MusicGainFading - step);
                if (comp.MusicGainFading <= 0.001f)
                {
                    Del(fading);
                    comp.MusicStreamFading = null;
                }
                else
                {
                    _audio.SetGain(fading, comp.MusicGainFading);
                }
            }
        }
    }

    // ── Смерть / снятие / очистка (защита от утечек) ──────────

    private void OnMobStateChanged(Entity<FuryStimulatorComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            RemCompDeferred<FuryStimulatorComponent>(ent);
    }

    private void OnShutdown(Entity<FuryStimulatorComponent> ent, ref ComponentShutdown args)
    {
        var comp = ent.Comp;
        var uid = ent.Owner;

        // Сброс фазы, чтобы обновление скорости вернуло ваниль.
        comp.Stage = FuryStage.None;

        if (comp.MusicStream != null)
        {
            Del(comp.MusicStream);
            comp.MusicStream = null;
        }

        if (comp.MusicStreamFading != null)
        {
            Del(comp.MusicStreamFading);
            comp.MusicStreamFading = null;
        }

        foreach (var weapon in comp.AffectedWeapons)
        {
            if (!Exists(weapon))
                continue;

            var hadGun = HasComp<FuryGunPenaltyComponent>(weapon);
            if (hadGun)
                RemComp<FuryGunPenaltyComponent>(weapon);
            if (HasComp<FuryMeleeBonusComponent>(weapon))
                RemComp<FuryMeleeBonusComponent>(weapon);

            if (hadGun)
                _gun.RefreshModifiers(weapon);
        }

        comp.AffectedWeapons.Clear();

        if (Exists(uid))
            _movement.RefreshMovementSpeedModifiers(uid);
    }

    // ── Инъектор ──────────────────────────────────────────────

    private void OnInjectorUse(Entity<FuryStimulatorInjectorComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        TryInject(ent, args.User, args.User);
        args.Handled = true;
    }

    private void OnInjectorAfterInteract(Entity<FuryStimulatorInjectorComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (!HasComp<MobStateComponent>(target))
            return;

        TryInject(ent, args.User, target);
        args.Handled = true;
    }

    private void TryInject(Entity<FuryStimulatorInjectorComponent> ent, EntityUid user, EntityUid target)
    {
        if (ent.Comp.Charges <= 0)
        {
            _popup.PopupEntity(Loc.GetString("fury-injector-empty"), ent, user);
            return;
        }

        Inject(target, user);
        _audio.PlayPvs(ent.Comp.InjectSound, target);

        ent.Comp.Charges--;
        if (ent.Comp.Charges <= 0)
        {
            if (ent.Comp.DeleteWhenEmpty)
                QueueDel(ent);
            else
                _appearance.SetData(ent, FuryInjectorVisuals.Used, true); // пустой (использованный) спрайт
        }
    }
}
