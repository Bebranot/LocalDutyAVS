using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Parry.Components;

/// <summary>
/// Полный лок ввода на время QTE-катсцены: движение и любые взаимодействия заблокированы,
/// активны только клавиши самого QTE (они идут через отдельный input-контекст "qte").
/// Сетевой, чтобы клиент участника мог сразу перестать предсказывать движение.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class QteInputLockComponent : Component;
