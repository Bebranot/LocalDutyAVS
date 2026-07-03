using Content.Server.Explosion.EntitySystems;
using Content.Shared._Duty.FuryStimulator;
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
/// _Duty: авторитетная логика стимулятора Fury-16 — убывание вещества, стейт-машина стадий,
/// баффы/дебаффы через штатные системы (движение — в Shared, урон/боль/оружие — тут),
/// pop-up атмосфера, персональная музыка с crossfade и передоз (гиб без урона окружающим).
/// Общая предсказываемая математика — в <see cref="SharedFuryStimulatorSystem"/>.
/// </summary>
public sealed class FuryStimulatorSystem : SharedFuryStimulatorSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly GibbingSystem _gib = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;

    private const float InjectMin = 45f;
    private const float InjectMax = 50f;
    private const float HoldMin = 5f;
    private const float HoldMax = 10f;

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

        // Маркеры на оружии снимают себя при выпадении из рук (восстановление ванильных стволов).
        SubscribeLocalEvent<FuryGunPenaltyComponent, GotUnequippedHandEvent>(OnGunUnequipped);
        SubscribeLocalEvent<FuryMeleeBonusComponent, GotUnequippedHandEvent>(OnMeleeUnequipped);

        // Инъектор.
        SubscribeLocalEvent<FuryStimulatorInjectorComponent, UseInHandEvent>(OnInjectorUse);
        SubscribeLocalEvent<FuryStimulatorInjectorComponent, AfterInteractEvent>(OnInjectorAfterInteract);
    }

    // ── Ввод дозы ─────────────────────────────────────────────

    /// <summary>Ввести дозу Fury-16 в организм цели. Повторный ввод сверх безопасного порога — передоз.</summary>
    public void Inject(EntityUid target, EntityUid? source = null)
    {
        var isNew = !HasComp<FuryStimulatorComponent>(target);
        var comp = EnsureComp<FuryStimulatorComponent>(target);

        var dose = _random.NextFloat(InjectMin, InjectMax);

        if (isNew)
        {
            comp.Metabolism = dose;
            // Фиксированная фаза ввода: вещество не убывает 5–10 секунд.
            comp.HoldUntil = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(HoldMin, HoldMax));
        }
        else
        {
            comp.Metabolism += dose;
        }

        if (comp.Metabolism > OverdoseThreshold)
        {
            Overdose((target, comp));
            return;
        }

        UpdateStage((target, comp));
        Dirty(target, comp);
    }

    private void Overdose(Entity<FuryStimulatorComponent> ent)
    {
        var uid = ent.Owner;

        _audio.PlayPvs(ent.Comp.OverdoseSound, uid);

        // По умолчанию интенсивность 0 — окружающие не получают урона (только гиб самого носителя).
        if (ent.Comp.OverdoseExplosionIntensity > 0f)
        {
            _explosion.QueueExplosion(
                uid,
                ent.Comp.OverdoseExplosionType,
                ent.Comp.OverdoseExplosionIntensity,
                1f,
                ent.Comp.OverdoseExplosionIntensity,
                user: uid);
        }

        // Расчленение + мгновенная смерть. Удаление сущности вызовет ComponentShutdown → полная очистка.
        _gib.Gib(uid);
    }

    // ── Тик ───────────────────────────────────────────────────

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FuryStimulatorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // Фаза ввода держит уровень; после — убывание.
            if (now >= comp.HoldUntil)
                comp.Metabolism -= comp.DecayPerSecond * frameTime;

            if (comp.Metabolism <= 0f)
            {
                comp.Metabolism = 0f;
                RemCompDeferred<FuryStimulatorComponent>(uid);
                continue;
            }

            UpdateStage((uid, comp));

            if ((comp.Stage == FuryStage.Intro || comp.Stage == FuryStage.Washout) && now >= comp.NextPopup)
            {
                _popup.PopupEntity(Loc.GetString(_random.Pick(WarningPopups)), uid, uid, PopupType.LargeCaution);
                comp.NextPopup = now + TimeSpan.FromSeconds(_random.NextFloat(comp.PopupIntervalMin, comp.PopupIntervalMax));
            }

            FadeMusic(comp, frameTime);
            Dirty(uid, comp);
        }
    }

    // ── Стадии ────────────────────────────────────────────────

    private void UpdateStage(Entity<FuryStimulatorComponent> ent)
    {
        var comp = ent.Comp;
        var newStage = LevelToStage(comp.Metabolism);
        if (newStage == comp.Stage)
            return;

        var old = comp.Stage;
        comp.Stage = newStage;

        // Скорость (движение считается в Shared через Stage).
        _movement.RefreshMovementSpeedModifiers(ent);

        // Оружие в руках: обновить/навесить/снять маркеры под новую силу баффа.
        RefreshWeaponEffects(ent);

        // Музыка.
        UpdateMusic(ent);

        // Атмосферный pop-up пика.
        if (newStage == FuryStage.Peak && old != FuryStage.Peak)
            _popup.PopupEntity(Loc.GetString("fury-popup-peak"), ent, ent, PopupType.Large);

        // Запланировать тревожные pop-up при входе в стадии разогрева/выхода.
        var enteringWarn = newStage is FuryStage.Intro or FuryStage.Washout;
        var wasWarn = old is FuryStage.Intro or FuryStage.Washout;
        if (enteringWarn && !wasWarn)
            comp.NextPopup = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(comp.PopupIntervalMin, comp.PopupIntervalMax));

        Dirty(ent);
    }

    // ── Эффекты оружия в руках ────────────────────────────────

    private void RefreshWeaponEffects(Entity<FuryStimulatorComponent> ent)
    {
        var factor = StageFactor(ent.Comp.Stage);

        // Безоружные атаки (у моба свой MeleeWeaponComponent).
        ApplyMeleeMarker(ent.Owner, factor, ent.Comp);

        foreach (var item in _hands.EnumerateHeld(ent.Owner))
        {
            ApplyGunMarker(item, factor, ent.Comp);
            ApplyMeleeMarker(item, factor, ent.Comp);
        }
    }

    private void ApplyGunMarker(EntityUid item, float factor, FuryStimulatorComponent comp)
    {
        if (!HasComp<GunComponent>(item))
            return;

        if (factor > 0f)
        {
            var pen = EnsureComp<FuryGunPenaltyComponent>(item);
            pen.Factor = factor;
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

    private void ApplyMeleeMarker(EntityUid item, float factor, FuryStimulatorComponent comp)
    {
        if (!HasComp<MeleeWeaponComponent>(item))
            return;

        if (factor > 0f)
        {
            var bonus = EnsureComp<FuryMeleeBonusComponent>(item);
            bonus.Factor = factor;
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
        var factor = StageFactor(ent.Comp.Stage);
        if (factor <= 0f)
            return;

        ApplyGunMarker(args.Equipped, factor, ent.Comp);
        ApplyMeleeMarker(args.Equipped, factor, ent.Comp);
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
        var factor = StageFactor(ent.Comp.Stage);
        if (factor <= 0f)
            return;

        var mult = (FixedPoint2) (1f - DamageResist * factor);
        args.Damage *= mult;
    }

    private void OnBeforeStamina(Entity<FuryStimulatorComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        // Неуязвимость к боли на пике/спаде: гасим только урон стамины, восстановление (отрицательное) не трогаем.
        if (args.Value > 0f && StageFactor(ent.Comp.Stage) > 0f)
            args.Cancelled = true;
    }

    // ── Музыка (персональная, crossfade) ──────────────────────

    private (int Track, SoundSpecifier? Sound) MusicFor(FuryStimulatorComponent comp, FuryStage stage) => stage switch
    {
        FuryStage.Intro or FuryStage.Washout => (0, comp.MusicIntro),
        FuryStage.Peak => (1, comp.MusicPeak),
        FuryStage.Decline => (2, comp.MusicDecline),
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

        // Сбрасываем стадию до None, чтобы обновление скорости ниже вернуло ваниль.
        comp.Stage = FuryStage.None;

        // Глушим музыку.
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

        // Снимаем все маркеры оружия — даже если руки уже опустели (гиб/смерть).
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

        // Возвращаем скорость к ванильной (Stage уже None).
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
        if (ent.Comp.Charges <= 0 && ent.Comp.DeleteWhenEmpty)
            QueueDel(ent);
    }
}
