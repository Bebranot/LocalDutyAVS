using Content.Shared._Duty.Parry.Components;
using Content.Shared._Duty.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.Parry;

/// <summary>
/// Показывает иконку активного блока сбоку от персонажа — тем же оверлеем, что рисует
/// job-иконки, а не отдельным слоем спрайта над головой.
///
/// Иконка живёт ровно столько, сколько существует сетевой <see cref="TwoHandedBlockComponent"/>:
/// оверлей опрашивает подписчиков каждый кадр, поэтому снимать её вручную не нужно.
/// </summary>
public sealed class TwoHandedBlockVisualsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private static readonly ProtoId<DutyStatusIconPrototype> BlockIcon = "DutyTwoHandedBlock";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TwoHandedBlockComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<TwoHandedBlockComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_proto.TryIndex(BlockIcon, out var icon))
            args.StatusIcons.Add(icon);
    }
}
