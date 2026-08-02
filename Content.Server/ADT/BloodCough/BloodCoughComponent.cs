using System.Threading;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.ADT.BloodCough;

[RegisterComponent]
[AutoGenerateComponentState]
[Access(typeof(BloodCoughSystem))]
public sealed partial class BloodCoughComponent : Component
{
    // _Duty: было 5-16 сек — раненый кашлял по 5 раз в минуту, это читалось как спам.
    // Разрежено до 20-45; травмы торса всё ещё учащают кашель (см. BloodCoughIntervalModifierEvent).
    [DataField("coughTimeMin"), ViewVariables(VVAccess.ReadWrite)]
    public int CoughTimeMin = 20;

    [DataField("coughTimeMax"), ViewVariables(VVAccess.ReadWrite)]
    public int CoughTimeMax = 45;

    [DataField("postingSayDamage")]
    public string? PostingSayDamage = default;

    public bool CheckCoughBlood = false;

    /// <summary>
    /// Token source for managing the timer cancellation
    /// </summary>
    public CancellationTokenSource? TokenSource;

    /// <summary>
    /// The time at which the next cough will occur
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextCough = TimeSpan.Zero;
}

/*
    ╔════════════════════════════════════╗
    ║   Schrödinger's Cat Code   🐾      ║
    ║   /\_/\\                           ║
    ║  ( o.o )  Meow!                    ║
    ║   > ^ <                            ║
    ╚════════════════════════════════════╝

*/
