// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx.

using Content.Shared.FixedPoint;

namespace Content.Shared._Duty.Clothing;

/// <summary>
/// _Duty: поверхность, которая пачкает одежду тех, кто по ней идёт или ползёт (лужа).
/// Проба берётся из раствора <see cref="Solution"/> и сам раствор не расходуется.
/// </summary>
[RegisterComponent]
public sealed partial class SurfaceDirtSourceComponent : Component
{
    [DataField]
    public string Solution = "puddle";

    /// <summary>Сколько грязи переносится за один «шаг» по поверхности.</summary>
    [DataField]
    public FixedPoint2 TransferAmount = FixedPoint2.New(0.25f);

    /// <summary>Лужи меньше этого объёма не пачкают — капли крови под ногами не в счёт.</summary>
    [DataField]
    public FixedPoint2 MinimumVolume = FixedPoint2.New(1);
}
