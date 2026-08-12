// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Chat;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.InteractionVerbs;

/// <summary>
///     Описывает, как показывать попапы интеракт-верба.<br/>
///     Локаль-ключи попапов имеют формат "interaction-[verb id]-[prefix]-[kind suffix]-popup", где: <br/>
///     - [prefix] — <see cref="Prefix"/>: "success", "fail" или "delayed". <br/>
///     - [kind suffix] — один из суффиксов ниже, обычно "self", "target" или "others". <br/>
/// </summary>
/// <remarks>
///     Параметры, доступные в локали: <br/>
///     - {$user} — исполнитель действия. <br/>
///     - {$target} — цель действия. <br/>
///     - {$used} — предмет в активной руке пользователя. Может быть null, тогда используется "0".
///     - {$selfTarget} — true, если действие применено на самого себя.
///     - {$hasUsed} — true, если у пользователя в руке что-то есть ($used не null).
/// </remarks>
[Prototype("InteractionPopup")]
public sealed partial class InteractionPopupPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public PopupType PopupType = PopupType.Medium;

    /// <summary>
    ///     Если true, соответствующие попапы успеха/провала также пишутся в чат.
    /// </summary>
    [DataField]
    public bool LogPopup = true;

    /// <summary>
    ///     Канал чата, в который логируются попапы.
    /// </summary>
    [DataField]
    public ChatChannel LogChannel = ChatChannel.Emotes;

    /// <summary>
    ///     Цвет отправляемого в чат сообщения.
    /// </summary>
    [DataField]
    public Color? LogColor = null;

    /// <summary>
    ///     Если true, сущности без прямой видимости цели попапа не увидят его в чат-логе.
    /// </summary>
    [DataField]
    public bool DoClipping = true;

    /// <summary>
    ///     Дистанция, на которой другие сущности видят сообщение в чат-логе.
    /// </summary>
    [DataField]
    public float VisibilityRange = 20f;

    /// <summary>
    ///     Суффикс попапа для исполнителя.
    /// </summary>
    [DataField("self")]
    public string? SelfSuffix = "self";

    /// <summary>
    ///     Суффикс попапа для цели.
    /// </summary>
    [DataField("target")]
    public string? TargetSuffix = "target";

    /// <summary>
    ///     Суффикс попапа для наблюдателей.
    /// </summary>
    [DataField("others")]
    public string? OthersSuffix = "others";

    public enum Prefix : byte
    {
        Success,
        Fail,
        Delayed
    }
}
