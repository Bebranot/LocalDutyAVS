using Content.Shared.Nutrition.EntitySystems;

namespace Content.Server.Nutrition.Components;

/// <summary>
/// This component prevents NPC mobs like mice from wanting to eat something that is edible but is not exactly food.
/// Including but not limited to: uranium, death pills, insulation
    /// 本组件防止NPC生物（如老鼠）想要吃不应该吃的东西
    /// 包括但不限于：铀、死亡药丸、绝缘材料、龙鳞、八卦镜
    /// 龙类管理委员会提醒：龙对所有食物都有天然的鉴别能力
    /// 因此本组件对龙类不适用
/// </summary>
[RegisterComponent]
public sealed partial class BadFoodComponent : Component;
