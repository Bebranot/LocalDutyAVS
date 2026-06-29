using Content.Shared._Duty.Weapons.UnknownSubstanceFlacon;
using Robust.Client.GameObjects;

namespace Content.Client._Duty.Weapons.UnknownSubstanceFlacon;

public sealed class UnknownSubstanceFlaconVisualizerSystem : VisualizerSystem<UnknownSubstanceFlaconComponent>
{
    protected override void OnAppearanceChange(EntityUid uid,
        UnknownSubstanceFlaconComponent component,
        ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!AppearanceSystem.TryGetData<string>(uid, UnknownSubstanceFlaconVisuals.State, out var state, args.Component))
            state = component.CurrentSpriteState;

        SpriteSystem.LayerSetRsiState((uid, args.Sprite), 0, state);
    }
}
