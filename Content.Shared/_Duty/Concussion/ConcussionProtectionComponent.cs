using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Concussion;

/// <summary>
/// _Duty: предмет с этим компонентом, надетый на голову/уши/маску, гасит набор контузии.
/// Вешается на базовые прототипы шлемов (наследуется во все дочерние) и на средства
/// защиты слуха. При сканировании берётся МАКСИМАЛЬНЫЙ <see cref="Reduction"/> среди надетого.
/// </summary>
[RegisterComponent]
public sealed partial class ConcussionProtectionComponent : Component
{
    /// <summary>Доля гашения прироста контузии, 0..1 (0.85 = −85%).</summary>
    [DataField]
    public float Reduction = 0.85f;

    /// <summary>Активна ли защита (для откидных забрал/складных шлемов можно гасить).</summary>
    [DataField]
    public bool Enabled = true;
}
