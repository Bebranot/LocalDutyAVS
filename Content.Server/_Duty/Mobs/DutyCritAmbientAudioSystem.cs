using Content.Shared.Mobs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Duty.Mobs;

/// <summary>
/// _Duty: зацикленное "сердцебиение" (эмбиент-звук) для игрока в крите.
/// Порт State Ambient из Lost Paradise (#226). Слышно только самому пострадавшему.
/// </summary>
public sealed class DutyCritAmbientAudioSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private static readonly Dictionary<MobState, (string Sound, float Volume)> StateAudio = new()
    {
        { MobState.Critical, ("/Audio/_Duty/CritAmbient/critical.ogg", -8f) },
    };

    private readonly Dictionary<EntityUid, EntityUid> _playing = new();

    public override void Initialize()
    {
        base.Initialize();

        // Content.Shared.Stunnable.SharedStunSystem уже занимает directed-подписку
        // (MobStateComponent, MobStateChangedEvent) — в этом форке на пару (компонент, событие)
        // разрешён только один подписчик. MobStateSystem раскидывает событие ещё и broadcast'ом
        // (RaiseLocalEvent(target, ev, true)), поэтому берём его через broadcast-подписку.
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        var uid = args.Target;
        StopAmbient(uid);

        if (!StateAudio.TryGetValue(args.NewMobState, out var data))
            return;

        var audioParams = AudioParams.Default.WithLoop(true).WithVolume(data.Volume);
        var audio = _audio.PlayEntity(new SoundPathSpecifier(data.Sound), uid, uid, audioParams);

        if (audio != null)
            _playing[uid] = audio.Value.Entity;
    }

    private void StopAmbient(EntityUid uid)
    {
        if (!_playing.Remove(uid, out var audio))
            return;

        if (Exists(audio))
            QueueDel(audio);
    }
}
