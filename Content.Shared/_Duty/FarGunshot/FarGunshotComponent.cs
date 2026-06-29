using Robust.Shared.Audio;

namespace Content.Shared._Duty.FarGunshot;

/// <summary>
/// «Эхо» выстрела: на дальней дистанции проигрывает отдельный звук далёкого выстрела.
/// Порт идеи из STALKER-14/_DZ. Триггерится по GunShotEvent на сервере.
/// </summary>
[RegisterComponent]
public sealed partial class FarGunshotComponent : Component
{
    /// <summary>Макс. радиус слышимости далёкого выстрела.</summary>
    [DataField]
    public float Range = 260f;

    /// <summary>Ближняя зона, которая НЕ слышит «эхо» (там слышен обычный звук выстрела).</summary>
    [DataField]
    public float CloseRange = 14f;

    /// <summary>Звук далёкого выстрела.</summary>
    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/_Stalker/Effects/FarGunshots/rifle1.ogg");
}
