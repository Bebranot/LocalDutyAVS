using Robust.Shared.GameStates;

namespace Content.Shared.ADT.Slimecats;

// _Duty: не хватало [NetworkedComponent] — без него AutoGenerateComponentState/
// AutoNetworkedField не имеют эффекта: компонент не попадает в networkedRegs
// в ComponentFactory (NetID не назначается), т.е. IsActiveSleep никогда не
// синхронизируется клиенту через состояние компонента. Сейчас это не видно
// игроку только потому, что фактическая визуализация идёт через отдельно
// сетевой AppearanceComponent (см. SlimecatsSleepActionSystem.UpdateAppearance),
// но сам компонент остаётся нерабочим как NetworkedComponent.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SharedSlimecatsSleepActionComponent : Component
{
    public string SleepActionForSlimecats = "ADTActionSlimeCatsSleep";
    public EntityUid? ActionEntity;

    [ViewVariables, AutoNetworkedField]
    public bool IsActiveSleep = false;
}