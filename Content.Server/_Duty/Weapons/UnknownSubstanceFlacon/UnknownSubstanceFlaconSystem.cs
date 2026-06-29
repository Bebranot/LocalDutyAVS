using Content.Shared._Duty.Weapons.UnknownSubstanceFlacon;
using Content.Shared.ADT.SwitchableWeapon;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Server.Chat.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;

using Content.Shared.Item;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;

namespace Content.Server._Duty.Weapons.UnknownSubstanceFlacon;

public sealed class UnknownSubstanceFlaconSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

    [Dependency] private readonly SharedItemSystem _item = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UnknownSubstanceFlaconComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<UnknownSubstanceFlaconComponent, ItemToggledEvent>(OnItemToggled);
        SubscribeLocalEvent<UnknownSubstanceFlaconComponent, AttemptMeleeEvent>(OnAttemptMelee);
        SubscribeLocalEvent<UnknownSubstanceFlaconComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMapInit(Entity<UnknownSubstanceFlaconComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.BaseDescription ??= MetaData(ent).EntityDescription;

        if (TryComp<ItemToggleComponent>(ent, out var toggle) && toggle.Activated)
        {
            ApplyOpenIdle(ent);
            return;
        }

        ApplyClosed(ent);
    }

    private void OnItemToggled(Entity<UnknownSubstanceFlaconComponent> ent, ref ItemToggledEvent args)
    {
        if (args.Activated)
            ApplyOpenIdle(ent);
        else
            ApplyClosed(ent);
    }

    private void OnAttemptMelee(Entity<UnknownSubstanceFlaconComponent> ent, ref AttemptMeleeEvent args)
    {
        if (!TryComp<ItemToggleComponent>(ent, out var toggle) || !toggle.Activated)
            return;

        if (!TryComp<MeleeWeaponComponent>(ent, out var melee))
            return;

        var manifestation = PickManifestation(ent.Comp);
        if (manifestation == null)
            return;

        ApplyManifestation(ent, manifestation, melee);
        UpdateDescription(ent, manifestation);
        Dirty(ent);
    }

    private void OnMeleeHit(Entity<UnknownSubstanceFlaconComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        if (!TryComp<ItemToggleComponent>(ent, out var toggle) || !toggle.Activated)
            return;

        var manifestation = CurrentManifestation(ent.Comp);
        if (manifestation == null)
            return;

        args.BonusDamage += new DamageSpecifier(manifestation.Damage) - args.BaseDamage;

        if (ent.Comp.GuaranteedScytheHitsRemaining <= 0)
            TryStudyManifestation(ent, manifestation, args.User);
        else
        {
            TrySpeakScythePhrase(ent, args.User);
            ent.Comp.GuaranteedScytheHitsRemaining--;
        }

        if (ent.Comp.GuaranteedScytheHitsRemaining == 0 && ent.Comp.HermesCharge >= ent.Comp.MaxHermesCharge)
            ResetHermes(ent.Comp);

        UpdateDescription(ent, manifestation);
        Dirty(ent);
    }

    private UnknownSubstanceFlaconManifestation? PickManifestation(UnknownSubstanceFlaconComponent component)
    {
        if (component.Manifestations.Count == 0)
            return null;

        if (component.GuaranteedScytheHitsRemaining > 0)
            return component.Manifestations.Find(x => x.Id == component.ScytheManifestationId);

        return _random.Pick(component.Manifestations);
    }

    private void TryStudyManifestation(Entity<UnknownSubstanceFlaconComponent> ent,
        UnknownSubstanceFlaconManifestation manifestation,
        EntityUid user)
    {
        var component = ent.Comp;
        if (component.StudiedManifestations.Contains(manifestation.Id))
            return;

        component.StudiedManifestations.Add(manifestation.Id);
        component.HermesCharge = Math.Min(component.HermesCharge + 1, component.MaxHermesCharge);

        SpeakStudyProgress(ent, user);

        if (component.HermesCharge >= component.MaxHermesCharge)
        {
            component.GuaranteedScytheHitsRemaining = component.GuaranteedScytheHits;
            component.AwaitingScytheSpeech = true;
        }
    }

    private static void ResetHermes(UnknownSubstanceFlaconComponent component)
    {
        component.HermesCharge = 0;
        component.StudiedManifestations.Clear();
        component.AwaitingScytheSpeech = false;
    }

    private void SpeakStudyProgress(Entity<UnknownSubstanceFlaconComponent> ent, EntityUid user)
    {
        var charge = ent.Comp.HermesCharge;
        var index = charge - 1;

        if (index >= 0 && index < ent.Comp.StudySpeech.Count)
            Speak(user, ent.Comp.StudySpeech[index]);

        if (charge != 7)
            return;

        var delayedSpeech = ent.Comp.SeventhStudyDelayedSpeech;
        var delay = TimeSpan.FromSeconds(ent.Comp.SeventhStudyDelayedSpeechDelay);
        Timer.Spawn(delay, () =>
        {
            if (!Deleted(user))
                Speak(user, delayedSpeech);
        });
    }

    private void TrySpeakScythePhrase(Entity<UnknownSubstanceFlaconComponent> ent, EntityUid user)
    {
        if (!ent.Comp.AwaitingScytheSpeech)
            return;

        ent.Comp.AwaitingScytheSpeech = false;
        Speak(user, ent.Comp.ScytheSpeech);
    }

    private void Speak(EntityUid user, string message)
    {
        _chat.TrySendInGameICMessage(user, message, InGameICChatType.Speak, false);
    }

    private void ApplyManifestation(Entity<UnknownSubstanceFlaconComponent> ent,
        UnknownSubstanceFlaconManifestation manifestation,
        MeleeWeaponComponent melee)
    {
        melee.Damage = new DamageSpecifier(manifestation.Damage);
        melee.AttackRate = manifestation.AttackRate;
        melee.Range = manifestation.Range;
        melee.Angle = Angle.FromDegrees(manifestation.Angle);
        melee.Animation = manifestation.Animation;
        melee.WideAnimation = manifestation.WideAnimation;
        melee.WideAnimationRotation = Angle.FromDegrees(manifestation.WideAnimationRotation);

        Dirty(ent.Owner, melee);

        if (TryComp<SwitchableWeaponComponent>(ent, out var switchable))
        {
            switchable.DamageOpen = new DamageSpecifier(manifestation.Damage);
            switchable.AttackRateOpen = manifestation.AttackRate;
        }

        ent.Comp.CurrentManifestationId = manifestation.Id;

        UpdateInhand(ent.Owner, manifestation.Id);
        SetSpriteState(ent, manifestation.SpriteState);
    }

    private void ApplyOpenIdle(Entity<UnknownSubstanceFlaconComponent> ent)
    {
        var manifestation = CurrentManifestation(ent.Comp);

        if (manifestation != null && TryComp<MeleeWeaponComponent>(ent, out var melee))
        {
            ApplyManifestation(ent, manifestation, melee);
        }
        else
        {
            SetSpriteState(ent, ent.Comp.OpenSpriteState);
            UpdateInhand(ent.Owner, "flacon-open");
        }

        UpdateDescription(ent, manifestation);
    }

    private void ApplyClosed(Entity<UnknownSubstanceFlaconComponent> ent)
    {
        if (TryComp<MeleeWeaponComponent>(ent, out var melee))
        {
            melee.Damage = new DamageSpecifier(ent.Comp.ClosedDamage);
            melee.AttackRate = ent.Comp.ClosedAttackRate;
            melee.Range = ent.Comp.ClosedRange;
            melee.Angle = Angle.FromDegrees(ent.Comp.ClosedAngle);
            melee.Animation = ent.Comp.ClosedAnimation;
            melee.WideAnimation = ent.Comp.ClosedWideAnimation;
            melee.WideAnimationRotation = Angle.FromDegrees(ent.Comp.ClosedWideAnimationRotation);
            Dirty(ent.Owner, melee);
        }

        if (TryComp<SwitchableWeaponComponent>(ent, out var switchable))
        {
            switchable.DamageFolded = new DamageSpecifier(ent.Comp.ClosedDamage);
            switchable.AttackRateFolded = ent.Comp.ClosedAttackRate;
        }

        UpdateInhand(ent.Owner, "flacon");
        SetSpriteState(ent, ent.Comp.ClosedSpriteState);

        if (ent.Comp.BaseDescription != null)
            _metaData.SetEntityDescription(ent, ent.Comp.BaseDescription);
    }

    private UnknownSubstanceFlaconManifestation? CurrentManifestation(UnknownSubstanceFlaconComponent component)
    {
        if (component.CurrentManifestationId == null)
            return null;

        return component.Manifestations.Find(x => x.Id == component.CurrentManifestationId);
    }

    private void SetSpriteState(Entity<UnknownSubstanceFlaconComponent> ent, string state)
    {
        ent.Comp.CurrentSpriteState = state;
        _appearance.SetData(ent, UnknownSubstanceFlaconVisuals.State, state);
        Dirty(ent);
    }

    private void UpdateDescription(Entity<UnknownSubstanceFlaconComponent> ent, UnknownSubstanceFlaconManifestation? manifestation)
    {
        var name = manifestation?.Name ?? "Нет";
        var description = manifestation?.Description ?? ent.Comp.BaseDescription ?? string.Empty;

        _metaData.SetEntityDescription(ent,
            $"{description}\nТекущее проявление: {name}.\nПосредничество [Гермес]: {ent.Comp.HermesCharge}/{ent.Comp.MaxHermesCharge}.");
    }

    private void UpdateInhand(EntityUid uid, string? manifestationId)
    {
        _item.SetHeldPrefix(uid, $"{manifestationId}");
    }
}
