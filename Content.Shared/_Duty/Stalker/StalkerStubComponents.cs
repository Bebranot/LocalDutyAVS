using Robust.Shared.GameObjects;

namespace Content.Shared._Duty.Stalker;

// Пустышки-компоненты для портированного из STALKER-14 контента (вариант A).
// ТОЛЬКО данные, БЕЗ систем/поведения — чтобы сталкерский YAML грузился,
// а механика оставалась ванильной (компоненты ничего не делают).
// Имена классов = именам компонентов в YAML (STWeight, STWeaponDamageFalloff, ...).
// Если позже захотим реальное поведение (Фаза 2) — заменим пустышку на полноценную систему.

/// <summary>Вес предмета (STALKER). Пустышка: данные есть, поведения нет.</summary>
[RegisterComponent]
public sealed partial class STWeightComponent : Component
{
    [DataField]
    public float Self;

    [DataField]
    public float InsideWeight;

    [DataField]
    public float Maximum = 200f;

    [DataField]
    public float Overload = 100f;
}

/// <summary>Множитель падения урона оружия (STALKER). Пустышка.</summary>
[RegisterComponent]
public sealed partial class STWeaponDamageFalloffComponent : Component
{
    [DataField]
    public float FalloffMultiplier = 1f;
}

/// <summary>Множитель точности оружия (STALKER). Пустышка.</summary>
[RegisterComponent]
public sealed partial class STWeaponAccuracyComponent : Component
{
    [DataField]
    public float AccuracyMultiplier = 1f;

    [DataField]
    public float AccuracyMultiplierUnwielded = 1f;

    [DataField]
    public float ModifiedAccuracyMultiplier = 1f;
}

/// <summary>Оптовая покупка в магазине (STALKER). Пустышка.</summary>
[RegisterComponent]
public sealed partial class STBulkBuyableComponent : Component
{
    [DataField]
    public int MaxQuantity = 20;
}
