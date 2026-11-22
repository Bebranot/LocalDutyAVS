using Robust.Shared.GameStates;

namespace Content.Shared.ADT.Sandevistan;

/// <summary>
/// Marker component for currently enabled Sandevistan users, optimizing query.
/// </summary>
// _Duty: сетевой маркер для клиентских оверлеев и исключения носителей из дилатации.
[RegisterComponent, NetworkedComponent]
public sealed partial class ActiveSandevistanUserComponent : Component;
