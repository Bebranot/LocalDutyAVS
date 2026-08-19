using Content.Shared.Power;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.ADT.Holomap;

public abstract class SharedHolomapSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HolomapComponent, ComponentGetState>(OnGetState);
        SubscribeLocalEvent<HolomapComponent, ComponentHandleState>(OnHandleState);
        SubscribeLocalEvent<HolomapComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnGetState(EntityUid uid, HolomapComponent component, ref ComponentGetState args)
    {
        args.State = new HolomapComponentState(component.Mode);
    }

    private void OnHandleState(EntityUid uid, HolomapComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not HolomapComponentState state)
            return;

        component.Mode = state.Mode;
        UpdateAppearance(uid, component);
    }

    private void OnPowerChanged(EntityUid uid, HolomapComponent component, ref PowerChangedEvent args)
    {
        // _Duty: раньше PowerDeviceVisuals.Powered вообще никогда не выставлялся —
        // клиентский HolomapVisualizerSystem читает именно этот ключ, чтобы решить,
        // рисовать ли слои (Unshaded/Battlemap/Lavaland), поэтому голомап всегда
        // выглядел выключенным независимо от реального питания.
        if (TryComp<AppearanceComponent>(uid, out var appearance))
            _appearance.SetData(uid, PowerDeviceVisuals.Powered, args.Powered, appearance);

        UpdateAppearance(uid, component);
    }

    public void SetMode(EntityUid uid, HolomapMode mode, HolomapComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        component.Mode = mode;
        Dirty(uid, component);
        UpdateAppearance(uid, component);
    }

    private void UpdateAppearance(EntityUid uid, HolomapComponent component)
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        _appearance.SetData(uid, HolomapVisuals.Mode, component.Mode, appearance);
    }
}

[Serializable, NetSerializable]
public sealed class HolomapComponentState : ComponentState
{
    public HolomapMode Mode;

    public HolomapComponentState(HolomapMode mode)
    {
        Mode = mode;
    }
}
