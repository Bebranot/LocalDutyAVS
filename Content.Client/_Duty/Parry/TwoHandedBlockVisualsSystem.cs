using System.Numerics;
using Content.Shared._Duty.Parry.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._Duty.Parry;

/// <summary>
/// Показывает мир-спейс иконку block.rsi над головой, пока у сущности есть сетевой
/// TwoHandedBlockComponent (обычный блок или парирующий — Фаза 1/2, разницы в отображении нет).
/// </summary>
public sealed class TwoHandedBlockVisualsSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private static readonly ResPath IconRsi = new("/Textures/_Duty/Interface/block.rsi");
    private static readonly Vector2 IconOffset = new(0, 0.55f);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TwoHandedBlockComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<TwoHandedBlockComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<TwoHandedBlockComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var spriteEnt = (ent.Owner, sprite);

        _sprite.LayerMapReserve(spriteEnt, TwoHandedBlockVisualLayers.Icon);
        _sprite.LayerSetRsi(spriteEnt, TwoHandedBlockVisualLayers.Icon, IconRsi, "block");
        _sprite.LayerSetOffset(spriteEnt, TwoHandedBlockVisualLayers.Icon, IconOffset);
        _sprite.LayerSetVisible(spriteEnt, TwoHandedBlockVisualLayers.Icon, true);
    }

    private void OnShutdown(Entity<TwoHandedBlockComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var spriteEnt = (ent.Owner, sprite);

        if (_sprite.LayerMapTryGet(spriteEnt, TwoHandedBlockVisualLayers.Icon, out var index, false))
            _sprite.LayerSetVisible(spriteEnt, index, false);
    }
}

public enum TwoHandedBlockVisualLayers : byte
{
    Icon,
}
