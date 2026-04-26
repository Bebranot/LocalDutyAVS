using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Shared.Weapons.Melee.ComboStrike;

public sealed class ComboStrikeSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedStaminaSystem _stamina = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

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

        if (combo.LastTarget != target)
        {
            ResetCombo(combo);
            combo.LastTarget = target;
        }

        combo.CurrentHits++;

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
