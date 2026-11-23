using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.ADT.Heretic.Systems;
using Content.Shared.Drugs;
using Content.Shared.Jittering;
using Content.Shared.StatusEffect;
using Content.Shared.Clothing.Components;
using Robust.Shared.Random;

namespace Content.Server.ADT.Clothing;

public sealed partial class MadnessMaskSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedHereticSystem _heretic = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private StatusEffectsSystem _statusEffect = default!;
    [Dependency] private IRobustRandom _random = default!;

    private readonly HashSet<Entity<StatusEffectsComponent>> _targets = new(); // _Duty: переиспользуемый буфер поиска целей

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var mask in EntityQuery<MadnessMaskComponent>())
        {
            mask.UpdateAccumulator += frameTime;
            if (mask.UpdateAccumulator < mask.UpdateTimer)
                continue;

            mask.UpdateAccumulator = 0;

            // _Duty-start: типизированный поиск по StatusEffectsComponent с переиспользуемым буфером вместо поиска всех
            // сущностей. DoJitter без StatusEffectsComponent ничего не делает, так что для джиттера поведение то же;
            // сущность со StaminaComponent, но без StatusEffects, стамину теперь терять не будет (на практике таких нет).
            _targets.Clear();
            _lookup.GetEntitiesInRange(Transform(mask.Owner).Coordinates, 5f, _targets);
            // _Duty-end
            foreach (var (look, _) in _targets)
            {
                // heathens exclusive
                if (_heretic.IsHereticOrGhoul(look))
                    continue;

                if (HasComp<StaminaComponent>(look) && _random.Prob(.4f))
                    _stamina.TakeStaminaDamage(look, 5f, visual: false);

                if (_random.Prob(.4f))
                    _jitter.DoJitter(look, TimeSpan.FromSeconds(.5f), true, amplitude: 5, frequency: 10);

                // if (_random.Prob(.25f))
                //     _statusEffect.TryAddStatusEffect<SeeingRainbowsComponent>(look, "SeeingRainbows", TimeSpan.FromSeconds(10f), false);
            }
        }
    }
}
