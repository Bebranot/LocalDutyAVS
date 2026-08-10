using Content.Server.Chat.Managers;
using Content.Shared._Duty.Block;
using Content.Shared.Chat;
using Robust.Server.Player;

namespace Content.Server._Duty.Block;

/// <summary>
/// Печатает серые системные строки систем блока в чат конкретному игроку. Отдельная серверная
/// система, потому что IChatManager серверный, а сами системы блока живут в Content.Shared.
/// </summary>
public sealed class BlockNoticeSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private static readonly Color NoticeColor = Color.Gray;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BlockNoticeEvent>(OnNotice);
    }

    private void OnNotice(ref BlockNoticeEvent args)
    {
        if (!_player.TryGetSessionByEntity(args.Target, out var session))
            return;

        var message = Loc.GetString(args.LocId);
        var wrapped = Loc.GetString("chat-manager-server-wrap-message", ("message", message));

        _chat.ChatMessageToOne(ChatChannel.Server, message, wrapped, default, false, session.Channel, NoticeColor);
    }
}
