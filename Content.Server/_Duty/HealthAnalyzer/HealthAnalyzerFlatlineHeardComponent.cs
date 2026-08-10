namespace Content.Server._Duty.HealthAnalyzer;

/// <summary>
/// _Duty: живёт на ПАЦИЕНТЕ. Помнит, какие сканирующие уже слышали звук остановки сердца
/// (flatline) для этой смерти. Звук проигрывается один раз на зрителя: тот, кто уже слышал,
/// при повторном анализе трупа его не услышит; другой медик, впервые сканирующий это тело,
/// услышит один раз. При воскрешении компонент снимается (см. <c>HealthAnalyzerSystem</c>),
/// поэтому новая смерть снова звучит для всех. Время жизни привязано к сущности пациента,
/// так что утечки нет — запись исчезает вместе с телом.
/// </summary>
[RegisterComponent]
public sealed partial class HealthAnalyzerFlatlineHeardComponent : Component
{
    /// <summary>Сканирующие (сущности-зрители), уже услышавшие flatline этой смерти.</summary>
    public readonly HashSet<EntityUid> Users = new();
}
