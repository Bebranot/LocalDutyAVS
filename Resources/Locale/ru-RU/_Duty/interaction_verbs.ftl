# Портировано из Goob-Station/Einstein Engines (см. Content.Shared/_Duty/InteractionVerbs).

## Общие сообщения системы интеракт-вербов (вкладка "Взаимодействовать")

interaction-verb-invalid = Условия для этого действия не выполнены. Сейчас им нельзя воспользоваться.
interaction-verb-cooldown = Это действие ещё восстанавливается. Подождите { TOSTRING($seconds, "F1") } сек.
interaction-verb-invalid-target = На эту цель это действие применить нельзя.
interaction-verb-no-hands = У вас нет свободных рук.
interaction-verb-cannot-reach = Вы не можете туда дотянуться.
interaction-verb-wrap-message = [italic]{$message}[/italic]

## Посмотреть на

interaction-LookAt-name = Посмотреть на
interaction-LookAt-description = Пристально посмотреть в бездну — и почувствовать, как бездна смотрит в ответ.
interaction-LookAt-success-self-popup = Вы смотрите на {THE($target)}.
interaction-LookAt-success-target-popup = Вы чувствуете, как {THE($user)} смотрит на вас...
interaction-LookAt-success-others-popup = {THE($user)} смотрит на {THE($target)}.

## Обнять

interaction-Hug-name = Обнять
interaction-Hug-description = Тёплые объятия отгоняют космический холод.
interaction-Hug-success-self-popup = Вы обнимаете {THE($target)}.
interaction-Hug-success-target-popup = {THE($user)} обнимает вас.
interaction-Hug-success-others-popup = {THE($user)} обнимает {THE($target)}.

## Постучать

interaction-KnockOn-name = Постучать
interaction-KnockOn-description = Постучать по цели, привлекая внимание.
interaction-KnockOn-success-self-popup = Вы стучите по {THE($target)}.
interaction-KnockOn-success-target-popup = {THE($user)} стучит по вам.
interaction-KnockOn-success-others-popup = {THE($user)} стучит по {THE($target)}.

## Помахать (учитывает предмет в руке)

interaction-WaveAt-name = Помахать
interaction-WaveAt-description = Помахать цели. Если у вас в руке что-то есть — вы помашете этим предметом.
interaction-WaveAt-success-self-popup = Вы машете {$hasUsed ->
    [false] {THE($target)}.
    *[true] своим { $used } в сторону {THE($target)}.
}
interaction-WaveAt-success-target-popup = {THE($user)} машет {$hasUsed ->
    [false] вам.
    *[true] {POSS-PRONOUN($user)} { $used } в вашу сторону.
}
interaction-WaveAt-success-others-popup = {THE($user)} машет {$hasUsed ->
    [false] {THE($target)}.
    *[true] {POSS-PRONOUN($user)} { $used } в сторону {THE($target)}.
}
