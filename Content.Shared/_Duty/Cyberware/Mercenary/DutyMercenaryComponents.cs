// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Roles.Components;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Cyberware.Mercenary;

// Наёмник — профессионал на один контракт: импланты ведут к одной цели и мешают устроить бойню.
// Компоненты имплантов лежат в Shared, хотя логика серверная: прототипы общие, и клиент должен знать их имена.

/// <summary>Насколько точно «Контракт» сейчас видит цель.</summary>
[Serializable, NetSerializable]
public enum DutyMercTrackMode : byte
{
    /// <summary>Контракта нет, закрыт или расторгнут — метку не рисуем.</summary>
    None,
    /// <summary>Далеко: только направление (квантованное) и грубая дистанция.</summary>
    Direction,
    /// <summary>Средняя дистанция: ромб в районе цели с погрешностью в пару тайлов.</summary>
    Approximate,
    /// <summary>Близко: ромб точно на цели.</summary>
    Exact,
    /// <summary>Сигнал потерян: ЭМИ, другой мир, цель в экранированном ящике.</summary>
    Lost,
}

/// <summary>
/// _Duty: ядро наёмника. Хранит цель контракта (её разум, а не тело — тело могут заменить/клонировать)
/// и счёт нарушений «Ограничителя».
/// </summary>
[RegisterComponent]
public sealed partial class DutyMercContractImplantComponent : Component
{
    /// <summary>Разум цели. Цель — персонаж игрока, а не конкретное тело.</summary>
    [ViewVariables]
    public EntityUid? TargetMind;

    /// <summary>Имя цели на момент заключения контракта — для брифинга и попапов.</summary>
    [ViewVariables]
    public string TargetName = string.Empty;

    /// <summary>Objective «убить цель» в разуме наёмника.</summary>
    [ViewVariables]
    public EntityUid? Objective;

    [ViewVariables]
    public int Violations;

    [DataField]
    public int MaxViolations = 3;

    /// <summary>Цель мертва — контракт закрыт, открыта эвакуация.</summary>
    [ViewVariables]
    public bool Closed;

    /// <summary>Контракт расторгнут «Ограничителем» или стёрт «Протоколом стирания».</summary>
    [ViewVariables]
    public bool Terminated;

    /// <summary>Ближе этого — точная позиция.</summary>
    [DataField]
    public float ExactRange = 10f;

    /// <summary>Дальше этого — только направление.</summary>
    [DataField]
    public float DirectionRange = 30f;
}

/// <summary>
/// _Duty: что видит HUD наёмника. Вешается на носителя «Контракта», уходит только владельцу:
/// постороннему клиенту знать позицию цели незачем (и это читерский вектор).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DutyMercContractTrackerComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [AutoNetworkedField]
    public DutyMercTrackMode Mode = DutyMercTrackMode.None;

    /// <summary>Мировая позиция метки. Уже огрублена сервером по режиму — клиенту точнее не узнать.</summary>
    [AutoNetworkedField]
    public Vector2 TargetPos;

    /// <summary>Дистанция для подписи (тоже огрублённая).</summary>
    [AutoNetworkedField]
    public float Distance;
}

/// <summary>_Duty: «Ограничитель сопутствующего ущерба» — наказывает за убийство не-цели.</summary>
[RegisterComponent]
public sealed partial class DutyMercLimiterImplantComponent : Component
{
    [DataField]
    public int ShockDamage = 15;

    [DataField]
    public TimeSpan ShockTime = TimeSpan.FromSeconds(3);

    /// <summary>Сколько HUD «шумит» после нарушения.</summary>
    [DataField]
    public TimeSpan ScrambleTime = TimeSpan.FromSeconds(8);
}

/// <summary>_Duty: «Подтверждение» — экран «КОНТРАКТ ЗАКРЫТ» и эвакуация после смерти цели.</summary>
[RegisterComponent]
public sealed partial class DutyMercConfirmImplantComponent : Component
{
    [DataField]
    public EntProtoId EvacEffect = "EffectFlashBluespace";

    [DataField]
    public SoundSpecifier EvacSound = new SoundCollectionSpecifier("DutyMercEvac");
}

/// <summary>_Duty: «Без следа» — без отпечатков и волокон, тихие шаги.</summary>
[RegisterComponent]
public sealed partial class DutyMercTracelessImplantComponent : Component
{
    /// <summary>ЭМИ сбивает маскировку на столько.</summary>
    [DataField]
    public TimeSpan EmpExposure = TimeSpan.FromSeconds(60);
}

/// <summary>
/// _Duty: носитель «Без следа». Networked: отпечатки ставятся в Shared-форензике (с предиктом),
/// и клиент должен решать так же, как сервер.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DutyMercTracelessComponent : Component
{
    /// <summary>До этого момента следы остаются как обычно (ЭМИ).</summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan? ExposedUntil;

    /// <summary>Был ли у носителя тег шагов до вживления — чтобы не выдать его тем, у кого шагов не было.</summary>
    [DataField]
    public bool HadFootstepTag;

    /// <summary>Тег шагов сейчас снят нами.</summary>
    [DataField]
    public bool FootstepsMuted;
}

/// <summary>_Duty: «Мимикрия» — 10 с скана лица вплотную, затем 3 минуты чужая внешность и имя.</summary>
[RegisterComponent]
public sealed partial class DutyMercMimicryImplantComponent : Component
{
    [DataField]
    public TimeSpan ScanTime = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan Duration = TimeSpan.FromMinutes(3);

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    /// <summary>Конец маскировки; null — маскировка не активна.</summary>
    [ViewVariables]
    public TimeSpan? ActiveUntil;

    [ViewVariables]
    public EntityUid? Host;

    // Снимок собственной внешности носителя для отката.
    [ViewVariables]
    public string? OriginalName;

    [ViewVariables]
    public Dictionary<ProtoId<OrganCategoryPrototype>, OrganProfileData>? OriginalProfiles;

    [ViewVariables]
    public Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>>? OriginalMarkings;
}

/// <summary>_Duty: «Иглоноготь» — одноразовая инъекция яда, когда носитель хватает кого-то.</summary>
[RegisterComponent]
public sealed partial class DutyMercNeedleNailImplantComponent : Component
{
    [DataField]
    public ProtoId<ReagentPrototype> Reagent = "Amatoxin";

    [DataField]
    public FixedPoint2 Amount = 12;
}

/// <summary>_Duty: маркер носителя «Иглоногтя» — на нём ловим начало захвата (PullStartedMessage).</summary>
[RegisterComponent]
public sealed partial class DutyMercNeedleNailHostComponent : Component;

/// <summary>_Duty: след укола на жертве «Иглоногтя» — виден при осмотре.</summary>
[RegisterComponent]
public sealed partial class DutyMercNeedleMarkComponent : Component;

/// <summary>_Duty: «Протокол стирания» — расплавить кибер, если наёмник долго в наручниках.</summary>
[RegisterComponent]
public sealed partial class DutyMercErasureImplantComponent : Component
{
    /// <summary>Сколько нужно провести в наручниках, чтобы стирание разблокировалось.</summary>
    [DataField]
    public TimeSpan CuffTime = TimeSpan.FromMinutes(2);

    [ViewVariables]
    public TimeSpan? CuffedSince;

    [DataField(required: true)]
    public DamageSpecifier Damage = new();
}

/// <summary>
/// _Duty: на objective «Ликвидировать цель». Расторгнутый/стёртый контракт проваливает цель,
/// даже если жертва потом умрёт сама.
/// </summary>
[RegisterComponent]
public sealed partial class DutyMercContractObjectiveComponent : Component
{
    [ViewVariables]
    public bool Failed;
}

/// <summary>_Duty: тег роли наёмника на сущности mind role.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DutyMercenaryRoleComponent : BaseMindRoleComponent;
