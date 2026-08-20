namespace Content.Shared._Duty.FireAgony;

/// <summary>
/// _Duty: бродкастится на горящую цель, когда посторонний игрок кликом ЛКМ (без предмета
/// в руке) пытается «сбить пламя» во время сцены агонии — см.
/// <c>Content.Shared._Duty.FireAgony.SharedFireAgonySystem.OnInteractHand</c>.
///
/// Раздельно: попап-фидбек показывается предсказанно (клиент + сервер) прямо из Shared-хендлера,
/// а вот реальное уменьшение FireStacks делает только
/// <c>Content.Server._Duty.FireAgony.FireAgonySystem</c> — <c>FlammableSystem</c> серверный тип
/// и недоступен из Shared, поэтому механика идёт через это событие, а не напрямую.
/// </summary>
[ByRefEvent]
public readonly record struct FireAgonyHelpExtinguishAttemptEvent(EntityUid User);
