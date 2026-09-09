// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: MIT

using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.SpawnMenu;

/// <summary>
/// _Duty: группа доступа к меню выдачи предметов. Один прототип — «кому» (список сикеев)
/// и «что» (список предметов с лимитами).
///
/// Прототипов может быть сколько угодно, и они складываются: игрок получает объединение
/// всех групп, в чьём <see cref="Ckeys"/> он есть, плюс все группы с <see cref="Everyone"/>. Отсюда и «специальные вещи для специальных
/// людей» — заводим группу с одним сикеем в <see cref="Ckeys"/> и кладём туда личные предметы.
/// Если один и тот же предмет встретился в нескольких группах, лимиты складываются,
/// а безлимит (<c>count: -1</c>) перебивает любое число.
///
/// Лимит считается на сикей и на раунд; счётчики обнуляются на рестарте раунда.
/// </summary>
[Prototype("dutySpawnAccess")]
public sealed partial class DutySpawnAccessPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Группа доступна всем игрокам без исключения. В этом случае <see cref="Ckeys"/>
    /// не нужен и не проверяется.
    /// </summary>
    [DataField]
    public bool Everyone;

    /// <summary>
    /// Сикеи (игровые логины), которым доступна эта группа. Регистр не важен.
    /// Игнорируется, если включён <see cref="Everyone"/>.
    /// </summary>
    [DataField]
    public List<string> Ckeys = new();

    /// <summary>
    /// Что эта группа разрешает спавнить.
    /// </summary>
    [DataField]
    public List<DutySpawnItem> Items = new();
}

/// <summary>
/// Одна строчка меню выдачи: что спавним и сколько раз за раунд.
/// </summary>
[DataDefinition]
public sealed partial class DutySpawnItem
{
    /// <summary>
    /// Прототип сущности, которую выдаёт меню.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Proto;

    /// <summary>
    /// Сколько штук игрок может выдать себе за раунд. -1 — без ограничения.
    /// </summary>
    [DataField]
    public int Count = 1;

    /// <summary>
    /// Подпись в меню. Пусто — берётся имя прототипа.
    /// </summary>
    [DataField]
    public string? Name;
}
