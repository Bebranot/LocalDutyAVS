using Content.Shared.Actions;
using Robust.Shared.Audio;

namespace Content.Shared.Clothing;

public abstract class SharedJumpbootsSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actionContainer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JumpbootsComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<JumpbootsComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, JumpbootsComponent component, MapInitEvent args)
    {
        _actionContainer.AddAction(uid, ref component.ActionEntity, component.Action);
        Dirty(uid, component);
    }

    private void OnGetActions(EntityUid uid, JumpbootsComponent component, GetItemActionsEvent args)
    {
        // _Duty: GetItemActionsEvent тоже рейзится при обычном подборе предмета в руку
        // (DidEquipHandEvent), не только при надевании в слот обуви (DidEquipEvent) — без этой
        // проверки способность прыжка оставалась у игрока, даже когда ботинки просто держали
        // в руке, а не носили. См. аналогичный паттерн в ToggleClothingSystem (MustEquip).
        if (args.InHands)
            return;

        args.AddAction(ref component.ActionEntity, component.Action);
    }
}

[DataDefinition]
public sealed partial class JumpbootsActionEvent : WorldTargetActionEvent
{
    [DataField]
    public SoundSpecifier? Sound { get; private set; } = new SoundPathSpecifier("/Audio/Effects/Footsteps/suitstep2.ogg");
}
