cmd-dutyftlcentcomm-desc = Отправляет шаттл FTL-прыжком в случайную свободную точку рядом с ЦК (без диска координат).
cmd-dutyftlcentcomm-help = Использование: { $command } [время_в_гиперпространстве|-] [грид_или_сущность_на_нём]
    Без второго аргумента берётся грид, на котором вы стоите. «-» — время по умолчанию.
cmd-dutyftlstation-desc = Отправляет шаттл FTL-прыжком в случайную свободную точку рядом со станцией.
cmd-dutyftlstation-help = Использование: { $command } [время_в_гиперпространстве|-] [грид_или_сущность_на_нём]
    Без второго аргумента берётся грид, на котором вы стоите. «-» — время по умолчанию.

cmd-dutyftl-hint-time = [секунды в гиперпространстве, { $min }–{ $max }, или -]
cmd-dutyftl-hint-grid = [грид или сущность на нём]
cmd-dutyftl-bad-time = «{ $value }» — не число секунд.
cmd-dutyftl-time-clamped = Время в гиперпространстве подогнано до { $value } с.
cmd-dutyftl-no-attached = Вы ни в кого не вселены — укажите грид вторым аргументом.
cmd-dutyftl-not-on-grid = Сущность не стоит на гриде — укажите шаттл.
cmd-dutyftl-not-grid = Это не грид шаттла.
cmd-dutyftl-not-shuttle = { $grid } — не шаттл.
cmd-dutyftl-already-ftl = { $grid } уже в FTL-прыжке.
cmd-dutyftl-is-station = { $grid } — станция или ЦК, её отправлять нельзя.
cmd-dutyftl-no-centcomm = ЦК в этом раунде не загружен.
cmd-dutyftl-no-station = Не найдена станция для прыжка.
cmd-dutyftl-no-spot = Рядом с { $anchor } не нашлось свободного места для шаттла.
cmd-dutyftl-start-failed = Прыжок не запустился — подробности в логе сервера.
cmd-dutyftl-override = Внимание: обычный прыжок этому шаттлу запрещён ({ $reason }) — команда игнорирует запрет.
cmd-dutyftl-started = { $grid } уходит в прыжок к { $anchor }: точка ({ $x }, { $y }), { $distance } м от центра, { $time } с в гиперпространстве.
