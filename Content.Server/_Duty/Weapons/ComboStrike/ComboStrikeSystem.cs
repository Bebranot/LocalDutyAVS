using Content.Shared._Duty.Weapons.ComboStrike;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Weapons.ComboStrike;

public sealed partial class ComboStrikeSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ComboStrikeComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(EntityUid uid, ComboStrikeComponent combo, MeleeHitEvent args)
    {
        if (args.HitEntities.Count == 0)
        {
            ResetCombo(combo);
            return;
        }

        var target = args.HitEntities[0];
        var now = _timing.CurTime;

        if (combo.LastTarget != target || now - combo.LastHitTime > TimeSpan.FromSeconds(combo.ComboWindow))
        {
            ResetCombo(combo);
            combo.LastTarget = target;
        }

        combo.CurrentHits++;
        combo.LastHitTime = now;

        if (combo.CurrentHits < combo.HitsRequired)
            return;

        ActivateCombo(uid, combo, target, args.User);
        ResetCombo(combo);
        combo.LastTarget = target;
    }

    private void ActivateCombo(EntityUid weapon, ComboStrikeComponent combo, EntityUid target, EntityUid user)
    {
        // 1. Звук
        if (combo.ComboSound != null)
            _audio.PlayPvs(combo.ComboSound, target);

        // 2. Бонусный урон
        if (combo.BonusDamage != null)
            _damageable.TryChangeDamage(target, combo.BonusDamage, ignoreResistances: false, origin: user);

        // 3. Урон выносливости
        if (combo.StaminaDamage > 0f && TryComp<StaminaComponent>(target, out _))
            _stamina.TakeStaminaDamage(target, combo.StaminaDamage, source: user);

        // 4. Визуальный эффект
        if (!string.IsNullOrEmpty(combo.ComboEffectPrototype))
            SpawnComboEffect(combo, target);
    }

    private void SpawnComboEffect(ComboStrikeComponent combo, EntityUid target)
    {
        var coords = _transform.GetMoverCoordinates(target);
        Spawn(combo.ComboEffectPrototype, coords);
    }

    private static void ResetCombo(ComboStrikeComponent combo)
    {
        combo.CurrentHits = 0;
        combo.LastTarget = null;
    }
}
