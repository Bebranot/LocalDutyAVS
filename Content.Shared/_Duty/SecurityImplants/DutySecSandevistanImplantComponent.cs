using Content.Shared.ADT.Sandevistan;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.SecurityImplants;

/// <summary>
/// Ослабленный СБ-клон синдикатского Сандевистана (см. <see cref="SandevistanImplantComponent"/>).
/// Не переиспользует синдикатскую сущность/компонент напрямую — вешается на отдельный
/// имплант-прототип и через Content.Server._Duty.SecurityImplants.DutySecSandevistanImplantSystem
/// накатывает урезанные параметры поверх штатного SandevistanUserComponent/SandevistanSystem.
/// </summary>
[RegisterComponent]
[NetworkedComponent]
public sealed partial class DutySecSandevistanImplantComponent : Component
{
    /// <summary>
    /// Множитель скорости передвижения в активном режиме. У синдикатской версии — 2.4.
    /// </summary>
    [DataField]
    public float MovementSpeedModifier = 2.0f;

    /// <summary>
    /// Множитель скорости атаки в активном режиме. У синдикатской версии — 2.4.
    /// </summary>
    [DataField]
    public float AttackSpeedModifier = 2.0f;

    /// <summary>
    /// Задержка перед повторным включением после выключения (КД). У синдикатской версии — 1.9с.
    /// Увеличена на ~25%.
    /// </summary>
    [DataField]
    public TimeSpan ShiftDelay = TimeSpan.FromSeconds(2.4);

    /// <summary>
    /// Пороги перегрева (см. <see cref="SandevistanUserComponent.Thresholds"/>) — снижены
    /// примерно на 25% относительно синдикатских (8/10/14/16/20), из-за чего СБ-версия
    /// раньше уходит в штрафные состояния и меньше времени может работать активно.
    /// </summary>
    [DataField]
    public SortedDictionary<SandevistanState, FixedPoint2> Thresholds = new()
    {
        { SandevistanState.Warning, 6 },
        { SandevistanState.Shaking, 8 },
        { SandevistanState.Stamina, 10 },
        { SandevistanState.Damage, 12 },
        { SandevistanState.Death, 15 },
    };
}
