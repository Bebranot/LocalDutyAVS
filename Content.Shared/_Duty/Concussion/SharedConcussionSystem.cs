using Content.Shared.Inventory;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Concussion;

/// <summary>
/// _Duty: общая математика контузии — ленивое затухание шкалы и расчёт защиты слуха.
/// Поведение (детекция выстрелов/взрывов, алерты, импульсы) живёт в серверном
/// <c>ConcussionSystem</c>; визуал/звук — в клиентском.
/// </summary>
public abstract class SharedConcussionSystem : EntitySystem
{
    [Dependency] protected readonly IGameTiming Timing = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    /// <summary>Слоты, в которых ищем защиту слуха.</summary>
    private static readonly string[] ProtectionSlots = { "head", "ears", "mask" };

    /// <summary>Текущее значение шкалы с учётом затухания (одинаково на клиенте и сервере).</summary>
    public float GetCurrentLevel(ConcussionComponent comp)
    {
        var dt = (float)(Timing.CurTime - comp.LastUpdate).TotalSeconds;
        if (dt <= 0f)
            return comp.Level;

        return MathF.Max(0f, comp.Level - comp.DecayPerSecond * dt);
    }

    public float GetCurrentLevel(EntityUid uid, ConcussionComponent? comp = null)
    {
        return Resolve(uid, ref comp, false) ? GetCurrentLevel(comp) : 0f;
    }

    /// <summary>
    /// Добавляет к шкале <paramref name="amount"/> (уже с учётом защиты/фолл-офа).
    /// Свёртывает накопленное затухание в новое <see cref="ConcussionComponent.Level"/>.
    /// </summary>
    public void AddRaw(EntityUid uid, float amount, ConcussionComponent comp)
    {
        if (amount <= 0f)
            return;

        var cur = GetCurrentLevel(comp);
        comp.Level = Math.Clamp(cur + amount, 0f, comp.MaxLevel);
        comp.LastUpdate = Timing.CurTime;
        Dirty(uid, comp);
    }

    /// <summary>Максимальная доля гашения среди надетых средств защиты (0..1).</summary>
    public float GetProtection(EntityUid uid)
    {
        var best = 0f;
        foreach (var slot in ProtectionSlots)
        {
            if (_inventory.TryGetSlotEntity(uid, slot, out var item)
                && TryComp<ConcussionProtectionComponent>(item, out var prot)
                && prot.Enabled)
            {
                best = MathF.Max(best, prot.Reduction);
            }
        }

        return Math.Clamp(best, 0f, 1f);
    }
}
