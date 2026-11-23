using Robust.Shared.Prototypes;
using Content.Shared.ADT.SpeechBarks;
using Content.Server.Chat.Systems;
using Robust.Shared.Configuration;
using Content.Shared.ADT.CCVar;
using Content.Server.Mind;
using Robust.Shared.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Content.Shared.Chat;
using Content.Server.Examine;
using Content.Shared.Ghost;
using Content.Shared.Ghost.Components;

namespace Content.Server.ADT.SpeechBarks;

public sealed class SpeechBarksSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    // _Duty: зависимости MindSystem/ISharedPlayerManager убраны — сессия слушателя берётся прямо из ActorComponent
    [Dependency] private ExamineSystem _examineSystem = default!;
    private bool _isEnabled = false;
    private readonly HashSet<Entity<ActorComponent>> _actors = new(); // _Duty: переиспользуемый буфер для поиска слушателей

    public override void Initialize()
    {
        base.Initialize();

        _cfg.OnValueChanged(ADTCCVars.BarksEnabled, v => _isEnabled = v, true);

        SubscribeLocalEvent<SpeechBarksComponent, EntitySpokeEvent>(OnEntitySpoke);
    }

    private void OnEntitySpoke(EntityUid uid, SpeechBarksComponent component, EntitySpokeEvent args)
    {
        if (!_isEnabled)
            return;

        var ev = new TransformSpeakerBarkEvent(uid, component.Data.Copy());
        RaiseLocalEvent(uid, ev);

        var message = args.ObfuscatedMessage ?? args.Message;
        var soundSpecifier = ev.Data.Sound ?? _proto.Index(ev.Data.Proto).Sound;
        var isWhisper = args.ObfuscatedMessage != null; // Определяем, был ли это шепот

        // _Duty-start: типизированный поиск только игроков (ActorComponent) вместо перебора всех сущностей
        // в радиусе + mind→userId→session на каждое сообщение. Сессия берётся прямо из ActorComponent;
        // призраки тоже имеют ActorComponent, так что GhostHearing работает как раньше.
        _actors.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, 10f, _actors);

        // Событие одно на всех слушателей: оно сериализуется при отправке каждой сессии, мутаций нет.
        PlaySpeechBarksEvent? barkEvent = null;

        foreach (var actor in _actors)
        {
            var ent = actor.Owner;

            if (!HasComp<GhostHearingComponent>(ent) && !_examineSystem.InRangeUnOccluded(ent, uid, 10f))
                continue;

            barkEvent ??= new PlaySpeechBarksEvent(
                GetNetEntity(uid),
                message,
                soundSpecifier,
                ev.Data.Pitch,
                ev.Data.MinVar,
                ev.Data.MaxVar,
                isWhisper);

            RaiseNetworkEvent(barkEvent, actor.Comp.PlayerSession);
        }
        // _Duty-end
    }
}
