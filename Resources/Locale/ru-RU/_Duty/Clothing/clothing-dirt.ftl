clothing-dirtable-examine = Оно запачкано. { $chemCount ->
        [1] Вещество  [color={ $color }]{ $desc }[/color].
       *[other] Смесь веществ: [color={ $color }]{ $desc }[/color].
        }

entity-effect-guidebook-clean-dirt =
    { $chance ->
        [1] Очищает
        *[other] очищают
    } загрязнение поверхности с эффективностью { NATURALPERCENT($multiplier, 2) }

shower-component-switched-on = Вы включаете душ.
shower-component-switched-off = Вы выключаете душ.
shower-component-examine-on = Он работает.
shower-component-examine-off = Он выключен.
shower-verb-enable = Включить
shower-verb-disable = Выключить

washing-machine-verb-start = Начать стирку
