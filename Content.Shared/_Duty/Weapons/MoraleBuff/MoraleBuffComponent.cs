using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Weapons.MoraleBuff;

/// <summary>
/// _Duty: «бафф морали». Вешается напрямую на сущность (кастера и союзников рядом)
/// способностью флага алебарды ОСЩ. Даёт:
/// <list type="bullet">
/// <item>ускорение передвижения (<see cref="SpeedModifier"/>) — предсказывается клиентом;</item>
/// <item>резист ко всему урону (<see cref="DamageResist"/>) — серверный;</item>
/// <item>игнор боли (через <c>PainNumbnessStatusEffectComponent</c>, вешается сервером);</item>
/// <item>«!» в конце IC-фраз (см. правку в ChatSystem);</item>
/// <item>иконку-статус справа (<see cref="Alert"/>).</item>
/// </list>
/// Скорость/refresh живут в общем <c>SharedMoraleBuffSystem</c>, остальное — в серверном
/// <c>MoraleBuffSystem</c>, который также снимает бафф по <see cref="EndTime"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MoraleBuffComponent : Component
{
    /// <summary>Множитель скорости передвижения (1.10 = +10%).</summary>
    [DataField, AutoNetworkedField]
    public float SpeedModifier = 1.10f;

    /// <summary>Доля урона, которая блокируется (0.15 = -15% урона).</summary>
    [DataField, AutoNetworkedField]
    public float DamageResist = 0.15f;

    /// <summary>Иконка-статус справа (под здоровьем).</summary>
    [DataField]
    public ProtoId<AlertPrototype> Alert = "DutyMoraleBuff";

    // ── Серверная служебка (не сетится) ───────────────────────

    /// <summary>Момент, когда бафф должен спасть. Сравнивается с CurTime в Update на сервере.</summary>
    [ViewVariables]
    public TimeSpan EndTime;

    /// <summary>
    /// True, если игнор боли (<c>PainNumbnessStatusEffectComponent</c>) был добавлен именно этим баффом.
    /// Нужен чтобы не снять чужой игнор боли (трейт/другой источник) при спадении баффа.
    /// </summary>
    [ViewVariables]
    public bool AddedPainNumbness;
}
