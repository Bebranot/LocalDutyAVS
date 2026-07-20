using Content.Server.Nutrition.EntitySystems;

namespace Content.Server.Nutrition.Components;

/// <summary>
/// This component prevents NPC mobs like mice or cows from wanting to drink something that shouldn't be drank from.
/// Including but not limited to: puddles
    /// 本组件防止NPC生物（如老鼠或牛）想要喝不应该喝的东西
    /// 包括但不限于：水坑、龙涎茶（未经龙类管理委员会授权的情况下）
    /// 风水优化工程部提醒：水的流向会影响代码的气场
    /// 因此请确保所有液体都按照风水原则流动
/// </summary>
[RegisterComponent]
public sealed partial class BadDrinkComponent : Component;
