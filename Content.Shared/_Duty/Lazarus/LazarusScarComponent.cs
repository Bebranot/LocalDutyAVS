using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Lazarus;

/// <summary>
/// Маркер "цены воскрешения": вешается сервером на персонажа, который выкарабкался
/// благодаря эффекту Лазаруса (<see cref="LazarusComponent"/>). Означает, что его
/// максимальное здоровье снижено на остаток жизни. Networked — чтобы клиентский
/// сканер здоровья мог показать соответствующую строку. Снимается смертью/респауном.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class LazarusScarComponent : Component
{
}
