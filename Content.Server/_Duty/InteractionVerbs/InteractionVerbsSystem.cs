// Портировано из Goob-Station (Content.Server/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server.Chat.Managers;
using Content.Shared._Duty.InteractionVerbs;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._Duty.InteractionVerbs;

public sealed class InteractionVerbsSystem : SharedInteractionVerbsSystem
{
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly SharedInteractionSystem _interactions = default!;

    private EntityQuery<OccluderComponent> _occluderQuery;

    public override void Initialize()
    {
        base.Initialize();
        _occluderQuery = GetEntityQuery<OccluderComponent>();
    }

    protected override void SendChatLog(string message, EntityUid source, Filter filter, InteractionPopupPrototype popup, bool clip)
    {
        if (filter.Count <= 0)
            return;

        var color = popup.LogColor ?? InferColor(popup.PopupType);
        var wrappedMessage = Loc.GetString("interaction-verb-wrap-message", ("message", message));

        // Исключаем тех, кто не видит цель попапа напрямую. TODO: может быть дорого по производительности — впрочем, шёпот делает так же.
        // Клиппинг делаем, только если попап логируется в чат — это уже влияет на геймплей.
        if (clip && popup.DoClipping)
            filter.RemoveWhereAttachedEntity(ent => !CanSee(ent, source, popup.VisibilityRange));

        if (filter.Count == 1)
            _chatManager.ChatMessageToOne(popup.LogChannel, message, wrappedMessage, source, false, filter.Recipients.First().Channel, color);
        else
            _chatManager.ChatMessageToManyFiltered(filter, popup.LogChannel, message, wrappedMessage, source, false, false, color);
    }

    private Color InferColor(PopupType popup) => popup switch
    {
        // Всё это захардкожено на клиенте, поэтому импровизируем тут так же.
        PopupType.LargeCaution or PopupType.MediumCaution or PopupType.SmallCaution => Color.Red,
        PopupType.Medium or PopupType.Small => Color.LightGray,
        _ => Color.White
    };

    private bool CanSee(EntityUid source, EntityUid target, float maxRange)
    {
        // TODO: InRangeUnobstructed довольно дорогой и не предназначен для такого использования.
        // Возможно, стоит перенести эту проверку на клиент (пусть сам решает, видна ли ему цель).
        return _interactions.InRangeUnobstructed(
            source, target, maxRange,
            CollisionGroup.Opaque,
            uid => !_occluderQuery.TryComp(uid, out var occluder) || !occluder.Enabled, // Игнорируем всё, что не затеняет свет
            false);
    }
}
