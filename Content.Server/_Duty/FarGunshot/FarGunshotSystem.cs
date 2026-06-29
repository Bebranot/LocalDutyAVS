using Content.Shared._Duty.FarGunshot;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server._Duty.FarGunshot;

// «Эхо» выстрела. Подписывается на GunShotEvent (его наш форк уже шлёт на ствол) —
// никаких правок upstream SharedGunSystem не требуется. Звучит только на сервере.
public sealed class FarGunshotSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FarGunshotComponent, GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(EntityUid uid, FarGunshotComponent component, ref GunShotEvent args)
    {
        if (component.Sound is null || component.Range <= component.CloseRange)
            return;

        var shootPos = _transform.GetMapCoordinates(uid);

        // Слышат только те, кто достаточно далеко (дальше CloseRange, но в пределах Range).
        var farSoundFilter = Filter.Empty()
            .AddInRange(shootPos, component.Range)
            .RemoveInRange(shootPos, component.CloseRange);

        var soundParams = component.Sound.Params;
        soundParams.MaxDistance = component.Range;
        soundParams.ReferenceDistance = component.CloseRange;

        _audio.PlayEntity(component.Sound, farSoundFilter, uid, recordReplay: true, soundParams);
    }
}
