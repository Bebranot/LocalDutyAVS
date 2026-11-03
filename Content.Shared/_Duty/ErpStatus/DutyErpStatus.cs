using Robust.Shared.Serialization;

namespace Content.Shared._Duty.ErpStatus;

/// <summary>
/// ЕРП-статус персонажа: определяет, на что персонаж согласен в РП-сценах интимного характера.
/// По умолчанию всегда <see cref="None"/>.
/// </summary>
[Serializable, NetSerializable]
public enum DutyErpStatus : byte
{
    /// <summary>
    /// Никакого ERP. Всё, что дальше объятий и поцелуев — наказуемо.
    /// </summary>
    None = 0,

    /// <summary>
    /// Умеренное ERP. По согласию и по договорённости в LOOC, но не слишком далеко.
    /// </summary>
    Moderate = 1,

    /// <summary>
    /// Полное ERP. Полная свобода действий в обе стороны.
    /// </summary>
    Full = 2,
}
