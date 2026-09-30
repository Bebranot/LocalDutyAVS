// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Slide;

/// <summary>
/// _Duty: идущий подкат. Висит от старта до подъёма (или до прерывания). Пока он есть, движение
/// блокировано, поскользнуться нельзя, R поглощается, а скоростью управляет
/// <see cref="DutySlideController"/>.
///
/// Лежание без <c>KnockedDownComponent</c> — сознательно: иначе включились бы ползание, алерт,
/// до-афтер вставания и ADT-логика R, и быстрого подъёма бы не вышло.
///
/// Скорость считается только от времени (профиль v0·(1 − t/T)^p), а не от пройденного пути: у
/// клиента и сервера фактические смещения чуть расходятся, а время старта одно и то же, так что
/// скорость в каждом тике у них совпадает и предсказание не дёргает персонажа назад.
/// </summary>
// Дельта-состояния: пока едем, каждый тик меняются только позиция, скорость и счётчик застревания.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true), AutoGenerateComponentPause]
public sealed partial class DutySlidingComponent : Component
{
    [AutoNetworkedField]
    public DutySlidePhase Phase = DutySlidePhase.Sliding;

    /// <summary>Единичный вектор в мировых координатах, фиксируется на старте — рулить нельзя.</summary>
    [AutoNetworkedField]
    public Vector2 Direction;

    /// <summary>v0: от неё считается порог «удара на ходу».</summary>
    [AutoNetworkedField]
    public float StartSpeed;

    /// <summary>
    /// Текущий отрезок профиля: начальная скорость, момент начала и длительность. На старте это v0,
    /// время старта и <see cref="DutySlideMasterComponent.SlideDuration"/>; на слизи отрезок
    /// перезапускается от текущей скорости и растягивается.
    /// </summary>
    [AutoNetworkedField]
    public float ProfileSpeed;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan ProfileStart;

    [AutoNetworkedField]
    public TimeSpan ProfileDuration;

    [AutoNetworkedField]
    public Vector2 LastPosition;

    /// <summary>Скорость, заданная в прошлом тике: по ней ждём смещения и ловим застревание.</summary>
    [AutoNetworkedField]
    public float LastSpeed;

    [AutoNetworkedField]
    public int StallTicks;

    [AutoNetworkedField]
    public bool SlipperyApplied;

    /// <summary>
    /// Основа броска тарана — тик старта. Бросок детерминированный (seed из этого числа и обеих
    /// сущностей), поэтому клиент предсказывает его так же, как сервер: остановится на цели сам,
    /// а не пролетит сквозь неё и потом откатится назад.
    /// </summary>
    [AutoNetworkedField]
    public int Seed;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan StartTime;

    /// <summary>Конец текущей фазы (для <see cref="DutySlidePhase.TackleStun"/> и <see cref="DutySlidePhase.StandingUp"/>).</summary>
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan PhaseEnd;
}

[Serializable, NetSerializable]
public enum DutySlidePhase : byte
{
    Sliding,

    /// <summary>Сбил моба: лежит оглушённым, встать не может.</summary>
    TackleStun,

    StandingUp,
}
