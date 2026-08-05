using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._Duty.StatusIcon;

/// <summary>
/// Статус-иконки механик <c>_Duty</c> — рисуются сбоку от персонажа тем же оверлеем, что и
/// job-иконки. Заводим свой тип прототипа, а не переиспользуем FactionIconPrototype: боевое
/// состояние вроде блока к фракциям отношения не имеет, а вендорный StatusIconPrototype.cs
/// трогать не хочется.
/// </summary>
[Prototype]
public sealed partial class DutyStatusIconPrototype : StatusIconPrototype, IInheritingPrototype
{
    /// <inheritdoc />
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DutyStatusIconPrototype>))]
    public string[]? Parents { get; private set; }

    /// <inheritdoc />
    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; private set; }
}
