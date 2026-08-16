using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Block;

/// <summary>
/// Статус-иконка активного окна блока — рисуется у головы блокирующего тем же оверлеем, что и
/// иконка должности (<see cref="StatusIconOverlay"/> по <see cref="StatusIconComponent"/>).
/// Отдельный вид прототипа, а не reuse jobIcon/ssdIcon: те завязаны на свои системы и HUD-гейты,
/// а наша иконка видна всем без каких-либо очков/HUD'ов.
/// </summary>
[Prototype("dutyBlockIcon")]
public sealed partial class DutyBlockIconPrototype : StatusIconPrototype;
