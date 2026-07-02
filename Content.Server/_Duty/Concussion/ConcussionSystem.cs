using Content.Shared._Duty.Concussion;
using Content.Shared.Alert;
using Content.Shared.CCVar;
using Content.Shared.Examine;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._Duty.Concussion;

/// <summary>
/// _Duty: серверная логика контузии — детекция близких выстрелов и взрывов,
/// наполнение шкалы (с учётом защиты слуха), отправка визуально-звуковых импульсов
/// и обновление алерт-полоски.
/// </summary>
public sealed class ConcussionSystem : SharedConcussionSystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private EntityQuery<ConcussionComponent> _concussionQuery;
    private readonly HashSet<EntityUid> _lookupSet = new();

    /// <summary>Радиус лукапа вокруг выстрела (с запасом над дефолтным ShotRange).</summary>
    private const float LookupRange = 8f;

    private const float ReconcileInterval = 0.5f;
    private static readonly TimeSpan ClearDelay = TimeSpan.FromSeconds(5);

    private float _reconcileAccumulator;

    private bool Enabled => _cfg.GetCVar(DutyCCVars.ConcussionEnabled);

    public override void Initialize()
    {
        base.Initialize();

        _concussionQuery = GetEntityQuery<ConcussionComponent>();
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(Entity<GunComponent> ent, ref GunShotEvent args)
    {
        if (!Enabled)
            return;

        var coords = args.FromCoordinates;
        if (!coords.IsValid(EntityManager))
            return;

        var mapPos = _transform.ToMapCoordinates(coords);

        _lookupSet.Clear();
        _lookup.GetEntitiesInRange(coords, LookupRange, _lookupSet);

        foreach (var target in _lookupSet)
        {
            if (!_concussionQuery.TryComp(target, out var comp))
                continue;

            var dist = (_transform.GetMapCoordinates(target).Position - mapPos.Position).Length();
            if (dist > comp.ShotRange)
                continue;

            // Стены глушат: непростреливаемая преграда между источником и ушами.
            if (!_examine.InRangeUnOccluded(target, mapPos, comp.ShotRange))
                continue;

            var falloff = comp.ShotRange <= 0f
                ? 1f
                : Math.Clamp(1f - dist / comp.ShotRange, comp.ShotMinFalloff, 1f);

            ApplyToEntity(target, comp, comp.ShotAmount * falloff, ConcussionImpulseType.Shot);
        }
    }

    /// <summary>Вызывается из ExplosionSystem для каждой задетой взрывом сущности.</summary>
    public void ApplyExplosionConcussion(EntityUid uid, float totalDamage)
    {
        if (!Enabled || totalDamage <= 0f)
            return;

        if (!_concussionQuery.TryComp(uid, out var comp))
            return;

        var refDmg = comp.BlastReferenceDamage <= 0f ? 1f : comp.BlastReferenceDamage;
        var amount = Math.Clamp(comp.BlastAmount * (totalDamage / refDmg), 0f, comp.BlastMaxAmount);
        ApplyToEntity(uid, comp, amount, ConcussionImpulseType.Blast);
    }

    private void ApplyToEntity(EntityUid uid, ConcussionComponent comp, float baseAmount, ConcussionImpulseType type)
    {
        var amount = baseAmount * (1f - GetProtection(uid));
        if (amount <= 0f)
            return;

        AddRaw(uid, amount, comp);
        Reconcile(uid, comp);

        // Импульс (моргание/затемнение + звон) шлём только живым игрокам — косметика.
        if (TryComp<ActorComponent>(uid, out var actor))
        {
            var intensity = Math.Clamp(GetCurrentLevel(comp) / comp.MaxLevel, 0f, 1f);
            RaiseNetworkEvent(new ConcussionImpulseEvent(type, intensity), actor.PlayerSession);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _reconcileAccumulator += frameTime;
        if (_reconcileAccumulator < ReconcileInterval)
            return;

        _reconcileAccumulator = 0f;

        var query = EntityQueryEnumerator<ConcussionComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // Пропускаем тех, у кого шкала и так пустая и бар не висит.
            if (comp.ShownSeverity == null && comp.Level <= comp.MinVisibleLevel)
                continue;

            Reconcile(uid, comp);
        }
    }

    /// <summary>Обновляет стадию алерта по текущему уровню и скрывает бар после 5с на нуле.</summary>
    private void Reconcile(EntityUid uid, ConcussionComponent comp)
    {
        var level = GetCurrentLevel(comp);

        if (level >= comp.MinVisibleLevel)
        {
            comp.ZeroSince = null;
            var severity = GetSeverity(level, comp);
            if (comp.ShownSeverity != severity)
            {
                _alerts.ShowAlert(uid, comp.Alert, severity);
                comp.ShownSeverity = severity;
            }

            return;
        }

        // Шкала на нуле.
        if (comp.ShownSeverity == null)
            return;

        comp.ZeroSince ??= Timing.CurTime;
        if (Timing.CurTime - comp.ZeroSince.Value >= ClearDelay)
        {
            _alerts.ClearAlert(uid, comp.Alert);
            comp.ShownSeverity = null;
            comp.ZeroSince = null;
        }
    }

    private static short GetSeverity(float level, ConcussionComponent comp)
    {
        var p = comp.MaxLevel <= 0f ? 0f : level / comp.MaxLevel;
        if (p >= 1f)
            return 3; // flash4
        if (p >= 0.6f)
            return 2; // flash3
        if (p >= 0.3f)
            return 1; // flash2
        return 0;     // flash1
    }
}
