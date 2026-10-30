using Robust.Shared.GameObjects;

namespace Content.Shared._Duty.Hydroponics;

/// <summary>
/// _Duty: поднимается на лотке после того, как лопатой с него убрали растение.
/// Нужен, потому что на <c>TrayShovelAttemptEvent</c> уже подписан ванильный
/// <see cref="Content.Shared.Botany.Items.Systems.BotanyShovelSystem"/>, а шина событий
/// не разрешает вторую подписку на ту же пару компонент+событие.
/// </summary>
[ByRefEvent]
public sealed class DutyPlantShoveledEvent(EntityUid tray, EntityUid plant, EntityUid user) : EntityEventArgs
{
    public EntityUid Tray { get; } = tray;
    public EntityUid Plant { get; } = plant;
    public EntityUid User { get; } = user;
}
