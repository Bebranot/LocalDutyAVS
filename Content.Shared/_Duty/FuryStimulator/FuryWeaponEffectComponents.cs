using Robust.Shared.GameStates;

namespace Content.Shared._Duty.FuryStimulator;

/// <summary>
/// _Duty: временный дебафф дальнобойного оружия под Fury-16. Вешается сервером на оружие,
/// пока оно в руках накачанного бойца; поведение (раздутый разброс, ×0.2 скорострельность)
/// применяется в <see cref="SharedFuryStimulatorSystem"/> через <c>GunRefreshModifiersEvent</c>,
/// поэтому компонент сетевой — чтобы клиент предсказывал те же значения.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedFuryStimulatorSystem))]
public sealed partial class FuryGunPenaltyComponent : Component
{
    /// <summary>Множитель силы дебаффа: 1 = пик, 0.5 = спад.</summary>
    [ViewVariables, AutoNetworkedField]
    public float Factor = 1f;
}

/// <summary>
/// _Duty: временный бафф ближнего боя под Fury-16 (+35%·Factor к скорости атаки).
/// Вешается сервером на оружие в руках (или на самого моба — для безоружных атак).
/// Применяется в <see cref="SharedFuryStimulatorSystem"/> через <c>GetMeleeAttackRateEvent</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedFuryStimulatorSystem))]
public sealed partial class FuryMeleeBonusComponent : Component
{
    /// <summary>Множитель силы баффа: 1 = пик, 0.5 = спад.</summary>
    [ViewVariables, AutoNetworkedField]
    public float Factor = 1f;
}
