using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Block.Components;

/// <summary>
/// Вешается на сущность, держащую активное окно блока (0.5с). Присутствие компонента сетевое —
/// по нему клиенты рисуют мир-спейс иконку щита над головой. Поля не синхронизируются
/// (нет AutoNetworkedField) — они нужны только серверу для резолва ударов.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(BlockSystem))]
public sealed partial class BlockComponent : Component
{
    /// <summary>Оружие (или тело — голые руки), которым поставлен блок.</summary>
    public EntityUid Weapon;

    /// <summary>Момент, когда окно блока закрывается само по себе.</summary>
    public TimeSpan EndTime;

    /// <summary>
    /// true — полный уровень (двуручное/огнестрел, Wielded): негация/протечка + оглушение
    /// атакующего. false — ослабленный уровень (одноручное/голые руки): плоские -40%, без наказания.
    /// </summary>
    public bool FullTier;

    /// <summary>true — за окно уже был хотя бы один погашенный/сниженный удар (для расчёта КД).</summary>
    public bool HitLanded;

    /// <summary>
    /// Атакующий из последнего полученного AttackedEvent — кэш для сопоставления со следующим
    /// за ним синхронно DamageModifyEvent (см. BlockSystem.OnDamageModify).
    /// </summary>
    public EntityUid? PendingAttacker;
}
