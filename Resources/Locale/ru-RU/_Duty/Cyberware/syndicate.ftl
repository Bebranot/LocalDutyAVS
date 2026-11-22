## Импланты Синдиката и Cybersun.

duty-cyber-module-lockpick = ОТМЫЧКИ
duty-cyber-module-chameleon = ХАМЕЛЕОН
duty-cyber-module-kerenzikov = КЕРЕНЗИКОВ
duty-cyber-module-parasite = ПАРАЗИТ
duty-cyber-module-autoinjector = ИНЪЕКТОР
duty-cyber-module-faketracker = ОШЕЙНИК

ent-DutyLockpickFingersImplant = имплант "Пальцы-отмычки"
    .desc = Спиленный серийник и набор складных отмычек в фалангах. Шлюзы от такого не запираются.
ent-DutyChameleonSkinImplant = имплант "Хамелеон-кожа"
    .desc = Пористая плёнка под эпидермисом, копирующая фон. Без маркировки, с самопальной прошивкой.
ent-DutyKerenzikovImplant = имплант "Керензиков"
    .desc = Рефлекторный контур Cybersun Industries. Дорогая, элегантная работа: тело само знает, куда не лететь пуле.
ent-DutyVoiceParasiteImplant = имплант "Голосовой паразит"
    .desc = Кустарный приёмник у основания черепа. Слушает чужие частоты и не просит ключей.
ent-DutyAutoInjectorImplant = имплант "Подкожный автоинъектор"
    .desc = Резервуар на три дозы с портом под кожей. Заправляется шприцем — порт заметен, если знать, куда смотреть.
ent-DutyFakeTrackerImplant = имплант "Ошейник"
    .desc = Поддельный трекер под маркой NT: отдаёт мониторам экипажа красивую, но чужую точку на карте.

ent-DutyImplantKitLockpickFingers = операционный набор «Пальцы-отмычки»
    .desc = Потёртый набор с кустарным имплантом. Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitChameleonSkin = операционный набор «Хамелеон-кожа»
    .desc = Набор с рулоном пористой плёнки без маркировки. Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitKerenzikov = операционный набор «Керензиков»
    .desc = Дорогой футляр Cybersun Industries с рефлекторным контуром. Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitVoiceParasite = операционный набор «Голосовой паразит»
    .desc = Набор с самодельным радиоприёмником для черепа. Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitAutoInjector = операционный набор «Подкожный автоинъектор»
    .desc = Набор с резервуаром на три дозы и портом. Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitFakeTracker = операционный набор «Ошейник»
    .desc = Набор с поддельным маячком под маркой NT. Нужен лежачий пациент и твёрдая рука.

ent-ActionDutyLockpickFingers = Вскрыть пальцами
    .desc = Выпустить отмычки из пальцев и вскрыть шлюз или замок. Долго и шумно.
ent-ActionDutyVoiceParasite = Голосовой паразит
    .desc = Выбрать радиоканал и подслушивать его без ключа шифрования.
ent-ActionDutyAutoInject = Ввести дозу
    .desc = Ввести одну дозу из подкожного порта прямо в кровь.
ent-ActionDutyFakeTracker = Ложный маяк
    .desc = Зафиксировать ложную точку на мониторах экипажа на вашем текущем месте. Повторное нажатие снимает подмену.

duty-syndicate-offline = Модуль не отвечает.

duty-lockpick-start = Из кончиков пальцев выдвигаются тонкие отмычки.
duty-lockpick-nothing = Здесь нечего вскрывать.
duty-lockpick-too-far = Слишком далеко.
duty-lockpick-welded = Дверь заварена — отмычки тут бессильны.
duty-lockpick-door-alarm = Шлюз пищит: аварийный доступ!
duty-lockpick-lock-click = Замок щёлкает и сдаётся.
duty-lockpick-access-log = НЕСАНКЦИОНИРОВАННЫЙ ДОСТУП

duty-chameleon-on = Кожа подстраивается под фон. Стойте или идите шагом — бег вас выдаст.
duty-chameleon-off = Маскировка слетает.

duty-kerenzikov-hud-active = РАЗГОН
duty-kerenzikov-chat-wrap = [color=#E8C25A][bold]КЕРЕНЗИКОВ[/bold] :: { $message }[/color]
duty-kerenzikov-chat-trigger-damage = Удар! Рефлексы срываются с цепи.
duty-kerenzikov-chat-trigger-danger = Пуля рядом! Рефлексы срываются с цепи.
duty-kerenzikov-chat-active = Ускорение движения, ударов и стрельбы на { $seconds } с.
duty-kerenzikov-chat-cooldown = Разгон окончен. Контур остывает { $seconds } с.
duty-kerenzikov-chat-ready = Контур остыл — рефлексы снова на взводе.

duty-parasite-start = Приёмник в черепе ловит канал «{ $channel }». Нажмите ещё раз, чтобы отключиться.
duty-parasite-end = Приёмник замолкает и остывает.
duty-parasite-cooldown = Приёмник ещё остывает.

duty-autoinjector-empty = Резервуар пуст.
duty-autoinjector-fail = Доза не прошла в кровь.
duty-autoinjector-used = Под кожей щёлкает, и по венам разливается холод.
duty-autoinjector-verb-refill = Заправить подкожный порт
duty-autoinjector-refill-start = Вы целитесь иглой в порт под кожей.
duty-autoinjector-full = Резервуар уже полон.
duty-autoinjector-syringe-empty = В шприце пусто.
duty-autoinjector-refilled = Порт принимает содержимое шприца.
duty-autoinjector-examine-port = Под кожей виден маленький порт, похожий на инъекционный.

duty-faketracker-on = Ложный маяк закреплён на этой точке.
duty-faketracker-off = Подмена координат снята.
duty-faketracker-no-grid = Здесь нет мониторов, которых можно обмануть.
duty-faketracker-detail = ЛОЖНАЯ ТОЧКА
