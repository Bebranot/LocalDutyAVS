## Наёмники: контракт, импланты, админ-команда `contract`.

## Модули HUD киберсистемы

duty-cyber-module-merc-contract = КОНТРАКТ
duty-cyber-module-merc-limiter = ОГРАНИЧИТЕЛЬ
duty-cyber-module-merc-confirm = ПОДТВЕРЖДЕНИЕ
duty-cyber-module-merc-traceless = БЕЗ СЛЕДА
duty-cyber-module-merc-mimicry = МИМИКРИЯ
duty-cyber-module-merc-needle = ИГЛОНОГОТЬ
duty-cyber-module-merc-erasure = СТИРАНИЕ

duty-merc-hud-searching = ПОИСК
duty-merc-hud-exact = ЦЕЛЬ · { $meters }м
duty-merc-hud-approx = ЦЕЛЬ · ~{ $meters }м
duty-merc-hud-lost = НЕТ СИГНАЛА
duty-merc-hud-closed = ЗАКРЫТ
duty-merc-hud-terminated = РАСТОРГНУТ
duty-merc-hud-evac-ready = ЭВАКУАЦИЯ
duty-merc-hud-violation = НАРУШЕНИЕ { $count }/{ $max }
duty-merc-hud-spent = ИЗРАСХОДОВАН
duty-merc-hud-melted = РАСПЛАВЛЕН
duty-merc-hud-erasure-ready = ГОТОВ

## Мировая метка и экран завершения

duty-merc-marker-exact = { $meters }м
duty-merc-marker-approx = ~{ $meters }м
duty-merc-ended-closed-title = КОНТРАКТ ЗАКРЫТ
duty-merc-ended-closed-sub = ЦЕЛЬ: { $target } // ЛИКВИДИРОВАНА // ЭВАКУАЦИЯ ДОСТУПНА
duty-merc-ended-terminated-title = КОНТРАКТ РАСТОРГНУТ
duty-merc-ended-terminated-sub = ЦЕЛЬ: { $target } // МЕТКА ВЫЖЖЕНА // ОПЛАТЫ НЕ БУДЕТ

## Попапы

duty-merc-briefing = Контракт принят. Цель: [bold]{ $target }[/bold]. Имплант «Контракт» ведёт к ней — чем ближе, тем точнее. Лишних трупов не оставлять: «Ограничитель» бьёт за каждого, третий рвёт контракт. Когда цель умрёт, «Подтверждение» откроет эвакуацию.
duty-merc-closed-popup = Цель мертва. Контракт закрыт.
duty-merc-limiter-violation = Ограничитель: сопутствующий ущерб! Нарушение { $count } из { $max }.
duty-merc-limiter-terminated = Ограничитель выжигает метку: контракт расторгнут.
duty-merc-module-offline = Модуль не отвечает.
duty-merc-module-cooldown = Модуль ещё перезаряжается.
duty-merc-evac-not-ready = Эвакуация закрыта: контракт не выполнен.
duty-merc-evac-confirm = Эвакуация уничтожит вас и всё снаряжение. Нажмите ещё раз для подтверждения.
duty-merc-evac-others = { CAPITALIZE(THE($user)) } вспыхивает синим пламенем и исчезает без следа!

duty-merc-mimicry-already = Маска уже надета.
duty-merc-mimicry-bad-target = Это лицо не скопировать.
duty-merc-mimicry-species = Сетка не натянется на лицо чужого вида.
duty-merc-mimicry-scan-start = Лицевая сетка начинает сканирование. Не отходите.
duty-merc-mimicry-scan-target = Кто-то слишком пристально разглядывает ваше лицо.
duty-merc-mimicry-on = Кожа лица натягивается и меняется. Теперь вы выглядите как { $target }.
duty-merc-mimicry-off = Лицевая сетка расслабляется: вы снова похожи на себя.
duty-merc-mimicry-emp = Импульс сбивает лицевую сетку — маска сползает!

duty-merc-needle-user = Игла под ногтем входит под кожу.
duty-merc-needle-victim = Вы чувствуете лёгкий укол.
duty-merc-needle-mark-examine = [color=#c08080]На коже — свежий след от укола.[/color]

duty-merc-erasure-burned = Стирать больше нечего.
duty-merc-erasure-mindshield = Майндшилд перехватил управление железом: стирание заблокировано.
duty-merc-erasure-not-ready = Протокол разблокируется после двух минут в наручниках.
duty-merc-erasure-confirm = Стирание сожжёт весь кибер и оставит тяжёлые ожоги. Нажмите ещё раз для подтверждения.
duty-merc-erasure-done = Термитная нить прогорает по всему телу. Данные контракта уничтожены.
duty-merc-erasure-others = Из-под кожи { THE($user) } валит едкий дым!
duty-merc-objective-erased = Данные контракта стёрты

## Роль, гост-роль, итоги раунда

duty-merc-antag-name = Наёмник
duty-merc-antag-objective = Найдите и ликвидируйте цель контракта. Без лишних жертв.
duty-merc-role-subtype = Наёмник
duty-merc-round-end-agent-name = наёмник
duty-merc-objective-issuer = [color=#7CFF9B]Посредник[/color]
duty-merc-objective-title = Ликвидировать { $targetName }, должность: { CAPITALIZE($job) }.

duty-merc-ghost-role-name = Наёмник
duty-merc-ghost-role-desc = Профессионал на один контракт. Подлетите к станции, найдите конкретного человека и уберите его чисто. Имплант «Контракт» ведёт к цели.
duty-merc-ghost-role-rules = Вы — [color={ role-type-solo-antagonist-color }][bold]{ role-type-solo-antagonist-name }[/bold][/color] с одной целью.
    Ваша работа — убить цель контракта, а не устроить бойню: каждое лишнее убийство наказывает «Ограничитель», третье расторгает контракт.
    Защищаться можно, но не превращайте ивент во «второй нюк». После смерти цели можете эвакуироваться действием «Эвакуация».

## Админ-команда contract

cmd-contract-desc = Заказать персонажа игрока наёмнику: у станции цели появится гост-роль «Наёмник».
cmd-contract-help = Использование: contract <ckey>
cmd-contract-hint = <ckey цели>
cmd-contract-not-in-round = Раунд ещё не идёт.
cmd-contract-no-player = Игрок «{ $ckey }» не найден среди подключённых.
cmd-contract-no-body = У игрока «{ $ckey }» нет тела персонажа.
cmd-contract-dead = Персонаж игрока «{ $ckey }» уже мёртв.
cmd-contract-failed = Не удалось запустить правило контракта.
cmd-contract-done = Контракт на { $target } ({ $ckey }) выставлен. Гост-роль «Наёмник» ждёт игрока.

## Сущности

ent-DutyMercContractImplant = имплант "Контракт"
    .desc = Безымянный чип под ухом, прошитый под одного человека. Показывает, где цель, — и больше ничего не знает.
ent-DutyMercLimiterImplant = имплант "Ограничитель"
    .desc = Предохранитель сопутствующего ущерба, вшитый заказчиком. Бьёт током за каждого лишнего покойника; на третьем рвёт контракт.
ent-DutyMercConfirmImplant = имплант "Подтверждение"
    .desc = Модуль отчёта: фиксирует смерть цели и открывает протокол эвакуации — носитель сгорает вместе с уликами.
ent-DutyMercTracelessImplant = имплант "Без следа"
    .desc = Подкожная плёнка на подушечках пальцев и гаситель шага. Ни отпечатков, ни ворса, ни звука — пока не тряхнёт ЭМИ.
ent-DutyMercMimicryImplant = имплант "Мимикрия"
    .desc = Пластичная лицевая сетка. Десять секунд вплотную — и три минуты у тебя чужое лицо. Голос и документы остаются твоими.
ent-DutyMercNeedleNailImplant = имплант "Иглоноготь"
    .desc = Полая игла под ногтем указательного пальца с одной дозой яда. Срабатывает, когда хватаешь кого-то, — один раз.
ent-DutyMercErasureImplant = имплант "Протокол стирания"
    .desc = Термитная нить вдоль всего наёмничьего железа. В плену — сжечь всё, что можно допросить. Себя тоже, но не до смерти.

ent-DutyImplantKitMercContract = операционный набор «Контракт»
    .desc = Набор без маркировки с чипом «Контракт». Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitMercLimiter = операционный набор «Ограничитель»
    .desc = Набор без маркировки с предохранителем «Ограничитель». Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitMercConfirm = операционный набор «Подтверждение»
    .desc = Набор без маркировки с модулем «Подтверждение». Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitMercTraceless = операционный набор «Без следа»
    .desc = Набор без маркировки с имплантом «Без следа». Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitMercMimicry = операционный набор «Мимикрия»
    .desc = Набор без маркировки с лицевой сеткой «Мимикрия». Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitMercNeedleNail = операционный набор «Иглоноготь»
    .desc = Набор без маркировки с иглой «Иглоноготь». Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitMercErasure = операционный набор «Протокол стирания»
    .desc = Набор без маркировки с термитной нитью «Протокол стирания». Нужен лежачий пациент и твёрдая рука.

ent-DutyActionMercEvacuate = Эвакуация
    .desc = Доступно после закрытия контракта. Носитель и всё, что на нём, выгорают в синей вспышке.
ent-DutyActionMercMimicry = Мимикрия
    .desc = Десять секунд вплотную сканировать лицо цели, затем три минуты носить его.
ent-DutyActionMercErasure = Протокол стирания
    .desc = Работает после двух минут в наручниках. Плавит всё наёмничье железо и данные контракта; носитель получает тяжёлые ожоги.

ent-MindRoleDutyMercenary = Роль наёмника
ent-SpawnPointGhostDutyMercenary = { ent-BaseAntagSpawner }
    .suffix = наёмник
    .desc = { ent-BaseAntagSpawner.desc }
ent-DutyMercenaryContract = { ent-BaseGameRule }
ent-DutyMercenaryKillContractObjective = Ликвидировать цель контракта
    .desc = Цель должна умереть. Без лишних трупов — за них ответит «Ограничитель».
