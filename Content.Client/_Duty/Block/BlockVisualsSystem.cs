using Content.Shared._Duty.Block;
using Content.Shared._Duty.Block.Components;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.Block;

/// <summary>
/// Иконка щита у головы во время активного окна блока — статус-иконка в том же слоте и тем же
/// оверлеем, что иконка должности (паттерн Content.Client/SSDIndicator/SSDIndicatorSystem.cs).
/// Видна всем в PVS без каких-либо HUD-очков, одна и та же для обоих уровней блока
/// (полный/ослабленный). Сетевой BlockComponent 1:1 с видимостью иконки — промежуточные
/// Appearance-данные не нужны.
/// </summary>
public sealed class BlockVisualsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private static readonly ProtoId<DutyBlockIconPrototype> BlockIcon = "DutyBlockIcon";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BlockComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(EntityUid uid, BlockComponent component, ref GetStatusIconsEvent args)
    {
        args.StatusIcons.Add(_prototype.Index(BlockIcon));
    }
}
