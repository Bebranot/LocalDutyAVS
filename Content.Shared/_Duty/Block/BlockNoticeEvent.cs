namespace Content.Shared._Duty.Block;

/// <summary>
/// Просьба показать игроку серую системную строку в чат — самостоятельный информационный текст,
/// не эмоут и не pop-up. Так системы блока сообщают о своих состояниях вместо алерт-иконок,
/// чтобы не захламлять экран.
///
/// Событие широковещательное и несёт цель в себе, потому что адресат не привязан к какому-то
/// одному компоненту. Поднимается shared-системами только на сервере; обрабатывается
/// Content.Server/_Duty/Block/BlockNoticeSystem, на клиенте подписчиков нет.
/// </summary>
[ByRefEvent]
public readonly record struct BlockNoticeEvent(EntityUid Target, string LocId);
