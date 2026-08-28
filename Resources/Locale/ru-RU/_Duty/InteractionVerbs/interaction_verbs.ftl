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

## Пожать руку (требует ответного клика цели в течение 8 секунд — см. MutualConsentAction)

interaction-Handshake-name = Пожать руку
interaction-Handshake-description = Предложить рукопожатие. Нужно, чтобы цель ответила тем же — в течение 8 секунд.
interaction-Handshake-success-self-popup = Вы обмениваетесь рукопожатием с {THE($target)}.
interaction-Handshake-success-target-popup = {THE($user)} отвечает на ваше рукопожатие.
interaction-Handshake-success-others-popup = {THE($user)} и {THE($target)} обмениваются рукопожатием.
interaction-Handshake-fail-self-popup = Вы протягиваете руку {THE($target)} для рукопожатия. Дождитесь ответа.
interaction-Handshake-fail-target-popup = {THE($user)} протягивает вам руку для рукопожатия. Нажмите ПКМ и выберите «Пожать руку» в ответ.
interaction-Handshake-fail-others-popup = {THE($user)} протягивает руку {THE($target)} для рукопожатия.

## Дать пять (та же механика согласия, что и Handshake)

interaction-HighFive-name = Дать пять
interaction-HighFive-description = Предложить "дать пять". Нужно, чтобы цель ответила тем же — в течение 8 секунд.
interaction-HighFive-success-self-popup = Вы даёте пять {THE($target)}.
interaction-HighFive-success-target-popup = {THE($user)} даёт вам пять.
interaction-HighFive-success-others-popup = {THE($user)} и {THE($target)} дают друг другу пять.
interaction-HighFive-fail-self-popup = Вы поднимаете руку, предлагая {THE($target)} дать пять. Дождитесь ответа.
interaction-HighFive-fail-target-popup = {THE($user)} поднимает руку, предлагая дать пять. Нажмите ПКМ и выберите «Дать пять» в ответ.
interaction-HighFive-fail-others-popup = {THE($user)} поднимает руку, предлагая {THE($target)} дать пять.

## Погладить по голове

interaction-Pat-name = Погладить по голове
interaction-Pat-description = По-дружески погладить цель по голове.
interaction-Pat-success-self-popup = Вы гладите {THE($target)} по голове.
interaction-Pat-success-target-popup = {THE($user)} гладит вас по голове.
interaction-Pat-success-others-popup = {THE($user)} гладит {THE($target)} по голове.
interaction-Pat-delayed-self-popup = Вы тянетесь погладить {THE($target)} по голове...
interaction-Pat-delayed-target-popup = {THE($user)} тянется погладить вас по голове...
interaction-Pat-delayed-others-popup = {THE($user)} тянется погладить {THE($target)} по голове...
interaction-Pat-fail-self-popup = У вас не вышло погладить {THE($target)} по голове.
interaction-Pat-fail-target-popup = {THE($user)} пытается погладить вас по голове, но не выходит.

## Плюнуть в лицо

interaction-Spit-name = Плюнуть в лицо
interaction-Spit-description = Плюнуть цели в лицо. Явное оскорбление.
interaction-Spit-success-self-popup = Вы плюёте в лицо {THE($target)}.
interaction-Spit-success-target-popup = {THE($user)} плюёт вам в лицо.
interaction-Spit-success-others-popup = {THE($user)} плюёт в лицо {THE($target)}.
interaction-Spit-delayed-self-popup = Вы набираете слюну, готовясь плюнуть в {THE($target)}...
interaction-Spit-delayed-target-popup = {THE($user)} набирает слюну, целясь в вас...
interaction-Spit-delayed-others-popup = {THE($user)} набирает слюну, целясь в {THE($target)}...
interaction-Spit-fail-self-popup = У вас не вышло плюнуть в {THE($target)}.
interaction-Spit-fail-target-popup = {THE($user)} пытается плюнуть в вас, но не выходит.

## Пощёчина — без урона, чистый РП

interaction-Slap-name = Пощёчина
interaction-Slap-description = Влепить цели пощёчину. Без урона — чисто ради эффекта.
interaction-Slap-success-self-popup = Вы отвешиваете пощёчину {THE($target)}.
interaction-Slap-success-target-popup = Ай! Больно!
interaction-Slap-success-others-popup = {THE($user)} отвешивает пощёчину {THE($target)}.
interaction-Slap-delayed-self-popup = Вы заносите руку для пощёчины {THE($target)}...
interaction-Slap-delayed-target-popup = {THE($user)} заносит руку, целясь вам пощёчиной...
interaction-Slap-delayed-others-popup = {THE($user)} заносит руку, целясь пощёчиной в {THE($target)}...
interaction-Slap-fail-self-popup = У вас не вышло влепить пощёчину {THE($target)}.
interaction-Slap-fail-target-popup = {THE($user)} пытается влепить вам пощёчину, но не выходит.

## Облокотиться — цель не моб (стойка/стол/стена/окно), см. InteractionVerbsComponent на TableBase/BaseWall/Window

interaction-LeanOn-name = Облокотиться
interaction-LeanOn-description = Облокотиться о цель — стойку, стол, стену или окно.
interaction-LeanOn-success-self-popup = Вы облокачиваетесь на {THE($target)}.
interaction-LeanOn-success-target-popup = На вас облокачиваются.
interaction-LeanOn-success-others-popup = {THE($user)} облокачивается на {THE($target)}.
interaction-LeanOn-delayed-self-popup = Вы начинаете облокачиваться на {THE($target)}...
interaction-LeanOn-delayed-target-popup = На вас начинают облокачиваться...
interaction-LeanOn-delayed-others-popup = {THE($user)} начинает облокачиваться на {THE($target)}...
interaction-LeanOn-fail-self-popup = У вас не вышло облокотиться на {THE($target)}.
interaction-LeanOn-fail-target-popup = На вас пытаются облокотиться, но не выходит.

## Пялиться на — мгновенный, дальнобойный, без fail-попапа (как LookAt)

interaction-StareAt-name = Пялиться на
interaction-StareAt-description = Уставиться на цель в упор — невежливо, но иногда красноречиво.
interaction-StareAt-success-self-popup = Вы пялитесь на {THE($target)}.
interaction-StareAt-success-target-popup = Вы чувствуете на себе пристальный, неотрывный взгляд {THE($user)}.
interaction-StareAt-success-others-popup = {THE($user)} пялится на {THE($target)}.

## Указать — с подсветкой на 8 секунд, гаснущей в темноте; без fail-попапа (как LookAt)

interaction-PointAt-name = Указать
interaction-PointAt-description = Указать на цель, подсветив её на 8 секунд для всех рядом. Подсветка гаснет в темноте.
interaction-PointAt-success-self-popup = Вы указываете на {THE($target)}.
interaction-PointAt-success-target-popup = {THE($user)} указывает на вас.
interaction-PointAt-success-others-popup = {THE($user)} указывает на {THE($target)}.
interaction-PointAt-delayed-self-popup = Вы поднимаете руку, указывая на {THE($target)}...
interaction-PointAt-delayed-target-popup = {THE($user)} поднимает руку, указывая на вас...
interaction-PointAt-delayed-others-popup = {THE($user)} поднимает руку, указывая на {THE($target)}...
