using System.Numerics;
using Content.Shared.Camera;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Client.Camera;

public sealed class CameraRecoilSystem : SharedCameraRecoilSystem
{
    // _Duty: ползунок «Тряска экрана» убран — CVar больше не читаем, иначе у тех, кто выставил 0
    // (в настройках раньше или консолью), тряска от взрывов/попаданий так и осталась бы выключенной.

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<CameraKickEvent>(OnCameraKick);
    }

    private void OnCameraKick(CameraKickEvent ev)
    {
        KickCamera(GetEntity(ev.NetEntity), ev.Recoil);
    }

    public override void KickCamera(EntityUid uid, Vector2 recoil, CameraRecoilComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        // Use really bad math to "dampen" kicks when we're already kicked.
        var existing = component.CurrentKick.Length();
        var dampen = existing / KickMagnitudeMax;
        component.CurrentKick += recoil * (1 - dampen);

        if (component.CurrentKick.Length() > KickMagnitudeMax)
            component.CurrentKick = component.CurrentKick.Normalized() * KickMagnitudeMax;

        component.LastKickTime = 0;
    }
}
