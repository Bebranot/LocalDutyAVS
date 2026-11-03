using Content.Shared.Inventory;
using Content.Shared.Clothing;
using Content.Server.Hands.Systems;
using Robust.Shared.Prototypes;
using Content.Server.Body.Systems;
using Content.Shared.Body;
using Robust.Shared.Audio.Systems;
using Content.Server.Emp;
using Content.Shared.DoAfter;
using Content.Server.Fluids.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Audio;
using Content.Server.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Content.Shared.Throwing;
using Content.Shared.Buckle.Components;

namespace Content.Server.Clothing.EntitySystems;

public sealed partial class JumpbootsSystem : SharedJumpbootsSystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private HandsSystem _handsSystem = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private BloodstreamSystem _bloodstreamSystem = default!;
    [Dependency] private EmpSystem _emp = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private IComponentFactory _compFact = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BodySystem _bodySystem = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private PhysicsSystem _physics = default!;
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private ThrowingSystem _throwing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JumpbootsComponent, JumpbootsActionEvent>(OnJump);
    }

    private void OnJump(EntityUid uid, JumpbootsComponent component, JumpbootsActionEvent args)
    {
        if (args.Handled)
            return;

        if (TryComp<BuckleComponent>(args.Performer, out var buckle) && buckle.Buckled)
            return;

        var transform = Transform(uid);

        if (transform.MapID != args.Target.GetMapId(EntityManager))
            return;

        _throwing.TryThrow(args.Performer, args.Target, component.Strength);
        _audio.PlayPvs(args.Sound, uid, AudioParams.Default.WithVolume(component.Volume));
        args.Handled = true;
    }
}
