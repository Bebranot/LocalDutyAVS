# _Duty: протокол «Альфа» — ответ станции на объявление войны ядерными оперативниками.

## Уровень тревоги

alert-level-alpha = Альфа
alert-level-alpha-announcement = Протокол АЛЬФА. Станция признана зоной боевых действий. Внутренние ограничения доступа сняты. Центральное Командование не несёт ответственности за сохранность имущества и персонала. У вас пятнадцать минут.

## Панель таймера

duty-code-alpha-timer-title = ДО КОНТАКТА

## Личные системные строки

duty-code-alpha-access-granted = Ваша карта доступа переведена в аварийный режим. Станция открыта целиком.
duty-code-alpha-access-revoked = Аварийный режим доступа отключён. Двери отделов снова заперты.

## РП-реплики: включение кода

duty-code-alpha-rp-start-1 = Мне нужно подготовиться... времени в обрез.
duty-code-alpha-rp-start-2 = Значит, вот так это и начинается. Надо найти что-нибудь потяжелее.
duty-code-alpha-rp-start-3 = Пятнадцать минут. Пятнадцать минут, чтобы решить, где я умру.
duty-code-alpha-rp-start-4 = Все двери открыты. Плохой знак — Центком уже нас списал.
duty-code-alpha-rp-start-5 = Надо предупредить остальных. И найти скафандр.

## РП-реплики: последняя минута

duty-code-alpha-rp-end-1 = Кажется, они скоро вылетят...
duty-code-alpha-rp-end-2 = Слишком тихо. Так тихо не бывает.
duty-code-alpha-rp-end-3 = Время вышло. Они уже в пути.
duty-code-alpha-rp-end-4 = Надеюсь, кто-то догадался заварить шлюзы.
duty-code-alpha-rp-end-5 = Ну всё. Дальше — как повезёт.

## Окно подтверждения у хоста

duty-code-alpha-prompt-title = Протокол «Альфа»
duty-code-alpha-prompt-body =
    Ядерные оперативники объявили войну.

    Включить код «Альфа»? Экипаж получит полный доступ по станции и увидит отсчёт до прилёта оперативников. Оперативники доступ не получат.

    Если не ответить, код включится автоматически.
duty-code-alpha-prompt-confirm = Включить
duty-code-alpha-prompt-deny = Отменить
duty-code-alpha-prompt-timer = Автоматически через { $seconds } с

## Админ-чат

duty-code-alpha-admin-prompt-sent = Код «Альфа»: запрос отправлен хосту ({ $host }). Без ответа код включится через минуту.
duty-code-alpha-admin-no-host = Код «Альфа»: хост не в игре, код включится автоматически через минуту.
duty-code-alpha-admin-vetoed = Код «Альфа»: хост отклонил включение.

## Команда

cmd-dutycodealpha-desc = Включает или выключает протокол «Альфа» вручную.
cmd-dutycodealpha-help = Использование: dutycodealpha <on|off>
cmd-dutycodealpha-hint = <on|off>
cmd-dutycodealpha-bad-arg = Ожидается on или off.
cmd-dutycodealpha-on = Код «Альфа» включён.
cmd-dutycodealpha-off = Код «Альфа» выключен.
cmd-dutycodealpha-already-on = Код «Альфа» уже активен.
cmd-dutycodealpha-already-off = Код «Альфа» не активен.
cmd-dutycodealpha-no-station = Не удалось определить станцию.
cmd-dutycodealpha-failed = Не удалось включить код «Альфа».
