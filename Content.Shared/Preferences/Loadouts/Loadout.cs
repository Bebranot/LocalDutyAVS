using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Preferences.Loadouts;

/// <summary>
/// Specifies the selected prototype and custom data for a loadout.
/// </summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class Loadout : IEquatable<Loadout>
{
    [DataField]
    public ProtoId<LoadoutPrototype> Prototype;

    // _Duty: персонализация лоадаута (из Space Onyx) — кастомные имя/описание/цвет предмета,
    // выбираемые игроком при создании персонажа.
    [DataField]
    public string? CustomColorTint;

    [DataField]
    public string? CustomName;

    [DataField]
    public string? CustomDescription;

    public bool IsValidColorTint()
    {
        return string.IsNullOrEmpty(CustomColorTint) ||
               CustomColorTint.Length <= 16 && Color.TryFromHex(CustomColorTint) != null;
    }

    public Loadout Clone()
    {
        return new Loadout
        {
            Prototype = Prototype,
            CustomColorTint = CustomColorTint,
            CustomName = CustomName,
            CustomDescription = CustomDescription,
        };
    }

    public bool Equals(Loadout? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Prototype.Equals(other.Prototype)
               && CustomColorTint == other.CustomColorTint
               && CustomName == other.CustomName
               && CustomDescription == other.CustomDescription;
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is Loadout other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Prototype, CustomColorTint, CustomName, CustomDescription);
    }
}
