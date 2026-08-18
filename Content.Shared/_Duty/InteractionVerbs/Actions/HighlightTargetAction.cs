// _Duty: действие верба "Указать" (PointAt) — подсвечивает цель на заданное время. Погасание в
// темноте не проверяется тут вообще — это забота клиентского шейдера (см.
// Resources/Textures/Shaders/outline.swsl, параметры light_boost/light_gamma/light_whitepoint
// в прототипе шейдера PointHighlightOutline), который сэмплит освещённость за пиксель у каждого
// наблюдающего клиента отдельно.
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.InteractionVerbs.Components;

namespace Content.Shared._Duty.InteractionVerbs.Actions;

[Serializable]
public sealed partial class HighlightTargetAction : InteractionAction
{
    /// <summary>Сколько держится подсветка после успешного выполнения верба.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(8);

    public override bool CanPerform(InteractionArgs args, InteractionVerbPrototype proto, bool beforeDelay, VerbDependencies deps) => true;

    public override bool Perform(InteractionArgs args, InteractionVerbPrototype proto, VerbDependencies deps)
    {
        // Повторное "Указать" на уже подсвеченную цель просто продлевает таймер — компонент не
        // пересоздаётся, так что клиентский шейдер не мигает.
        var highlight = deps.EntMan.EnsureComponent<PointHighlightComponent>(args.Target);
        highlight.EndTime = deps.Timing.CurTime + Duration;
        return true;
    }
}
