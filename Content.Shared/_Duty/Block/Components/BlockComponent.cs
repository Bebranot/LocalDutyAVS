using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Block.Components;

/// <summary>
/// Вешается на сущность, держащую активное окно блока (0.5с).
///
/// Компонент полностью сетевой, и это принципиально: клиент предиктит и открытие окна, и резолв
/// урона по себе, поэтому ему нужны те же Weapon/EndTime/FullTier, что и серверу. Без них чужой
/// блок приезжал бы с EndTime = 0 и клиент сносил бы компонент в первом же Update, а свой блок
/// резолвил бы урон по ослабленному уровню вместо полного.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(BlockSystem))]
public sealed partial class BlockComponent : Component
{
    /// <summary>Оружие (или тело — голые руки), которым поставлен блок.</summary>
    [AutoNetworkedField]
    public EntityUid Weapon;

    /// <summary>Момент, когда окно блока закрывается само по себе.</summary>
    [AutoNetworkedField]
    public TimeSpan EndTime;

    /// <summary>
    /// true — полный уровень (двуручное/огнестрел, Wielded): негация/протечка + оглушение
    /// атакующего. false — ослабленный уровень (одноручное/голые руки): плоские -40%, без наказания.
    /// </summary>
    [AutoNetworkedField]
    public bool FullTier;

    /// <summary>
    /// true — за окно уже был хотя бы один погашенный/сниженный удар. Сетевое, потому что от него
    /// зависит длительность кулдауна, а кулдаун клиент считает сам, чтобы не предиктить блок,
    /// который сервер отклонит.
    /// </summary>
    [AutoNetworkedField]
    public bool HitLanded;

    /// <summary>
    /// Атакующий из последнего полученного AttackedEvent — кэш для сопоставления со следующим
    /// за ним синхронно DamageModifyEvent (см. BlockSystem.OnDamageModify). Живёт в пределах
    /// одного тика, синхронизировать нечего.
    /// </summary>
    public EntityUid? PendingAttacker;
}
