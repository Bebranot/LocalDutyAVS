using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Weapon.Module.Effects;

// Порт из STALKER-14. Параметры зума прицела.
// ВНИМАНИЕ (Фаза 1 DutyAVS): сам зум-скоупинг НЕ портирован — поведения нет.
// Структура сохранена, чтобы YAML модулей (scopeEffect) грузился и был готов к Фазе 2.
[DataDefinition, Serializable, NetSerializable]
public partial struct STWeaponModuleScopeEffect()
{
    [DataField, ViewVariables]
    public float Zoom = 1f;

    [DataField, ViewVariables]
    public float Offset = 15;

    [DataField, ViewVariables]
    public bool AllowMovement;

    [DataField, ViewVariables]
    public bool RequireWielding;

    [DataField, ViewVariables]
    public bool UseInHand;

    [DataField, ViewVariables]
    public TimeSpan Delay = TimeSpan.FromSeconds(1);

    public static STWeaponModuleScopeEffect Merge(STWeaponModuleScopeEffect effectA, STWeaponModuleScopeEffect effectB)
    {
        return new STWeaponModuleScopeEffect
        {
            Zoom = MathF.Max(effectA.Zoom, effectB.Zoom),
            Offset = MathF.Max(effectA.Offset, effectB.Offset),
            AllowMovement = effectA.AllowMovement && effectB.AllowMovement,
            RequireWielding = effectA.RequireWielding && effectB.RequireWielding,
            UseInHand = effectA.UseInHand && effectB.UseInHand,
            Delay = effectA.Delay,
        };
    }
}
