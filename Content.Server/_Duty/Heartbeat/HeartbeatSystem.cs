using Content.Shared._Duty.Heartbeat;
using Content.Shared._Duty.Lazarus;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Heartbeat;

/// <summary>
/// _Duty: серверная часть пульса — ТОЛЬКО расчёт уровня. Пересчитывает уровень на события
/// урона / смены mob-состояния (НЕ по тику) и сетит его в компонент (Dirty только при
/// реальной смене уровня). Воспроизведение звука живёт на клиенте у владельца тела
/// (<c>Content.Client._Duty.Heartbeat.HeartbeatSystem</c>) и в анализаторе здоровья.
/// </summary>
public sealed class HeartbeatSystem : SharedHeartbeatSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HeartbeatComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<HeartbeatComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<HeartbeatComponent, LazarusStartedEvent>(OnLazarusStarted);

        // MobStateChangedEvent directed на MobStateComponent уже занят SharedStunSystem
        // (в этом форке на пару (компонент, directed-событие) разрешён один подписчик),
        // поэтому слушаем broadcast-вариант — их может быть сколько угодно.
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMapInit(Entity<HeartbeatComponent> ent, ref MapInitEvent args)
    {
        Recalculate(ent.Owner, ent.Comp);
    }

    private void OnDamageChanged(Entity<HeartbeatComponent> ent, ref DamageChangedEvent args)
    {
        Recalculate(ent.Owner, ent.Comp);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (!TryComp<HeartbeatComponent>(args.Target, out var comp))
            return;

        // Вышел из крита (в т.ч. «встал» после Лазаруса) — снимаем заглушку «второй жизни»,
        // чтобы пульс гарантированно вернулся, не завися от точности таймингов.
        if (args.NewMobState != MobState.Critical)
            comp.SuppressUntil = default;

        Recalculate(args.Target, comp);
    }

    private void OnLazarusStarted(Entity<HeartbeatComponent> ent, ref LazarusStartedEvent args)
    {
        // «Вторая жизнь»: глушим пульс на время кинематики (уровень принудительно None).
        ent.Comp.SuppressUntil = _timing.CurTime + args.SuppressDuration;
        Recalculate(ent.Owner, ent.Comp);
    }

    /// <summary>Пересчитывает уровень; Dirty() только при реальном изменении.</summary>
    private void Recalculate(EntityUid uid, HeartbeatComponent comp)
    {
        var level = GetLevel(uid, comp);

        // Заглушка «второй жизни» — пульса нет, пока идёт кинематика Лазаруса.
        if (IsSuppressed(uid, comp))
            level = HeartbeatLevel.None;

        if (level == comp.Level)
            return;

        comp.Level = level;
        Dirty(uid, comp);
    }
}
