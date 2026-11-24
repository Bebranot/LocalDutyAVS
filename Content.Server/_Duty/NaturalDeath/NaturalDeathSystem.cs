using Content.Server.Chat.Systems;
using Content.Shared._Duty.NaturalDeath;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Systems;
using Content.Shared.Gibbing;
using Content.Shared.Humanoid;
using Content.Shared.Interaction.Events;
using Content.Shared.Jittering;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Content.Shared.Speech.Muting;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.NaturalDeath;

/// <summary>
/// _Duty: серверная часть естественной смерти (Critical/SoftCritical -> Dead без гиба, суицида и ваншота).
/// Обеспечивает короткую судорогу, единичный предсмертный вздох/эмоут без спама,
/// короткое окно от случайного мгновенного выхода в призрака и корректный сброс при реанимации.
/// </summary>
public sealed class NaturalDeathSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly TimeSpan GhostAttemptGraceWindow = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan GhostBlockPopupCooldown = TimeSpan.FromSeconds(2.0);

    private readonly List<EntityUid> _pendingJitterCleanup = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);
        SubscribeLocalEvent<HumanoidProfileComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        // Broadcast, так как directed-подписку на (MobStateComponent, MobStateChangedEvent) уже занимает SharedStunSystem.
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);

        SubscribeLocalEvent<NaturalDeathComponent, BeforeNaturalDeathgaspEvent>(OnBeforeDeathgasp);
        SubscribeLocalEvent<NaturalDeathComponent, GhostAttemptEvent>(OnGhostAttempt);
        SubscribeLocalEvent<NaturalDeathComponent, SuicideGhostEvent>(OnSuicideGhost);
        SubscribeLocalEvent<NaturalDeathComponent, SuicideEvent>(OnSuicide);
        SubscribeLocalEvent<NaturalDeathComponent, BeingGibbedEvent>(OnBeingGibbed);
        SubscribeLocalEvent<NaturalDeathComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<NaturalDeathComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<NaturalDeathComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeLocalEvent<NaturalDeathComponent, ComponentShutdown>(OnComponentShutdown);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _pendingJitterCleanup.Clear();
    }

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent ev)
    {
        TryAttach(ev.Mob);
    }

    private void OnMindAdded(Entity<HumanoidProfileComponent> ent, ref MindAddedMessage args)
    {
        TryAttach(ent.Owner);
    }

    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        TryAttach(ev.Entity);
    }

    private void TryAttach(EntityUid mob)
    {
        if (!HasComp<HumanoidProfileComponent>(mob))
            return;

        EnsureComp<NaturalDeathComponent>(mob);
    }

    private void OnBeforeDeathgasp(Entity<NaturalDeathComponent> ent, ref BeforeNaturalDeathgaspEvent args)
    {
        if (CanTriggerNaturalDeath(ent, args.OldMobState))
            args.Cancelled = true;
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            if (TryComp<NaturalDeathComponent>(args.Target, out var comp))
                TryStartNaturalDeath((args.Target, comp), args.OldMobState);
            return;
        }

        if (args.OldMobState == MobState.Dead && TryComp<NaturalDeathComponent>(args.Target, out var reviveComp))
        {
            reviveComp.SuppressedBySuicide = false;
            reviveComp.SuppressedByGib = false;
            EndNaturalDeath((args.Target, reviveComp), fadeOut: true);
        }
    }

    private bool CanTriggerNaturalDeath(Entity<NaturalDeathComponent> ent, MobState oldState)
    {
        var comp = ent.Comp;
        if (comp.Active)
            return false;

        // Только естественное умирание из крита или софт-крита; мгновенный ваншот из Alive не трогаем.
        if (oldState is not (MobState.Critical or MobState.SoftCritical))
            return false;

        if (TerminatingOrDeleted(ent.Owner))
            return false;

        if (comp.SuppressedBySuicide || comp.SuppressedByGib)
            return false;

        // Исключаем добровольный уход через /ghost / Succumb / LastWords (GhostKillCrit).
        if (comp.LastGhostAttemptTime != TimeSpan.Zero && _timing.CurTime - comp.LastGhostAttemptTime <= GhostAttemptGraceWindow)
            return false;

        return true;
    }

    private void TryStartNaturalDeath(Entity<NaturalDeathComponent> ent, MobState oldState)
    {
        if (!CanTriggerNaturalDeath(ent, oldState))
            return;

        var uid = ent.Owner;
        var comp = ent.Comp;
        var now = _timing.CurTime;

        comp.Active = true;
        comp.GhostBlockUntil = now + comp.GhostLockDuration;
        Dirty(uid, comp);

        // Короткая предсмертная судорога / потеря тонуса (не затираем уже идущий jitter от других систем).
        if (comp.JitterDuration > 0f && !HasComp<JitteringComponent>(uid))
        {
            _jitter.AddJitter(uid, comp.JitterAmplitude, comp.JitterFrequency);
            comp.AppliedJitter = true;
            comp.JitterRemoveAt = now + TimeSpan.FromSeconds(comp.JitterDuration);
            _pendingJitterCleanup.Add(uid);
        }

        // Предсмертный звук (если не немой) + один атмосферный эмоут для окружающих без спама.
        if (TryComp<DeathgaspComponent>(uid, out var deathgasp)
            && !_statusEffects.HasEffectComp<MutedStatusEffectComponent>(uid))
        {
            _chat.TryEmoteWithoutChat(uid, deathgasp.Prototype, ignoreActionBlocker: true);

            if (comp.EmoteLineCount > 0)
            {
                var emoteIdx = _random.Next(1, comp.EmoteLineCount + 1);
                var emoteText = Loc.GetString($"duty-natural-death-emote-{emoteIdx}");
                _chat.TrySendInGameICMessage(uid, emoteText, InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
            }
        }

        if (TryComp<ActorComponent>(uid, out var actor))
        {
            if (comp.SelfPopupLineCount > 0)
            {
                var selfIdx = _random.Next(1, comp.SelfPopupLineCount + 1);
                _popup.PopupEntity(Loc.GetString($"duty-natural-death-self-{selfIdx}"), uid, uid, PopupType.Small);
            }

            RaiseNetworkEvent(
                new NaturalDeathStartedEvent(
                    GetNetEntity(uid),
                    comp.FadeOutDuration,
                    comp.TinnitusSound,
                    comp.FinalBeatSound),
                actor.PlayerSession);
        }
    }

    private void OnGhostAttempt(Entity<NaturalDeathComponent> ent, ref GhostAttemptEvent args)
    {
        var comp = ent.Comp;

        // Попытка гостнуться до перехода в Dead (например, Succumb/LastWords в крите) — помечаем,
        // чтобы последующий урон GhostKillCrit не считался естественной смертью.
        if (!_mobState.IsDead(ent.Owner))
        {
            comp.LastGhostAttemptTime = _timing.CurTime;
            return;
        }

        if (!comp.Active || _timing.CurTime >= comp.GhostBlockUntil)
            return;

        args.Cancelled = true;

        if (_timing.CurTime >= comp.NextGhostBlockPopup)
        {
            comp.NextGhostBlockPopup = _timing.CurTime + GhostBlockPopupCooldown;
            _popup.PopupEntity(Loc.GetString("duty-natural-death-ghost-locked"), ent.Owner, ent.Owner, PopupType.Small);
        }
    }

    private void OnSuicideGhost(Entity<NaturalDeathComponent> ent, ref SuicideGhostEvent args)
    {
        ent.Comp.SuppressedBySuicide = true;
        EndNaturalDeath(ent, fadeOut: false);
    }

    private void OnSuicide(Entity<NaturalDeathComponent> ent, ref SuicideEvent args)
    {
        ent.Comp.SuppressedBySuicide = true;
        EndNaturalDeath(ent, fadeOut: false);
    }

    private void OnBeingGibbed(Entity<NaturalDeathComponent> ent, ref BeingGibbedEvent args)
    {
        ent.Comp.SuppressedByGib = true;
        EndNaturalDeath(ent, fadeOut: false);
    }

    private void OnRejuvenate(Entity<NaturalDeathComponent> ent, ref RejuvenateEvent args)
    {
        ent.Comp.SuppressedBySuicide = false;
        ent.Comp.SuppressedByGib = false;
        EndNaturalDeath(ent, fadeOut: false);
    }

    private void OnPlayerDetached(Entity<NaturalDeathComponent> ent, ref PlayerDetachedEvent args)
    {
        EndNaturalDeath(ent, fadeOut: false);
    }

    private void OnMindRemoved(Entity<NaturalDeathComponent> ent, ref MindRemovedMessage args)
    {
        EndNaturalDeath(ent, fadeOut: false);
    }

    private void OnComponentShutdown(Entity<NaturalDeathComponent> ent, ref ComponentShutdown args)
    {
        ClearJitter(ent);
    }

    private void EndNaturalDeath(Entity<NaturalDeathComponent> ent, bool fadeOut)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;

        ClearJitter(ent);

        if (comp.Active)
        {
            comp.Active = false;
            comp.GhostBlockUntil = TimeSpan.Zero;
            Dirty(uid, comp);
        }

        if (TryComp<ActorComponent>(uid, out var actor))
            RaiseNetworkEvent(new NaturalDeathCancelledEvent(fadeOut), actor.PlayerSession);
    }

    private void ClearJitter(Entity<NaturalDeathComponent> ent)
    {
        if (!ent.Comp.AppliedJitter)
            return;

        ent.Comp.AppliedJitter = false;
        if (!TerminatingOrDeleted(ent.Owner) && HasComp<JitteringComponent>(ent.Owner))
            RemCompDeferred<JitteringComponent>(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingJitterCleanup.Count == 0)
            return;

        var now = _timing.CurTime;
        for (var i = _pendingJitterCleanup.Count - 1; i >= 0; i--)
        {
            var uid = _pendingJitterCleanup[i];
            if (!TryComp<NaturalDeathComponent>(uid, out var comp) || !comp.AppliedJitter)
            {
                _pendingJitterCleanup.RemoveAt(i);
                continue;
            }

            if (now < comp.JitterRemoveAt)
                continue;

            ClearJitter((uid, comp));
            _pendingJitterCleanup.RemoveAt(i);
        }
    }
}
