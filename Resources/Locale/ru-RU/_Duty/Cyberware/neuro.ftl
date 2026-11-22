## Нейросеть: внутренний нейроинтерфейс NT-SEC, тактическая сеть Горлекса, трекер v2, тревожная кнопка,
## навигатор, распознаватель угроз, «Призрак в сети», «Червь ICE».

duty-cyber-module-neuro-ntsec = НЕЙРО NT-SEC
duty-cyber-module-neuro-gorlex = СЕТЬ ГОРЛЕКСА
duty-cyber-module-neuro-tracker = ТРЕКЕР v2
duty-cyber-module-neuro-panic = ТРЕВОГА
duty-cyber-module-neuro-navigator = НАВИГАТОР
duty-cyber-module-neuro-threat = РАСПОЗНАВАТЕЛЬ
duty-cyber-module-neuro-ghost = ПРИЗРАК
duty-cyber-module-neuro-ice = ICE

## Общее для активных имплантов

duty-neuro-implant-offline = Имплант не отвечает.
duty-neuro-implant-cooldown = Имплант перезаряжается: ещё { $seconds } с.

## Нейроинтерфейс: сеть, ЭМИ, вторжение

duty-neuro-no-signal = НЕТ СИГНАЛА
duty-neuro-jammed-popup = Нейроинтерфейс заглушён помехами.
duty-neuro-order-not-leader = Метки ставит только командир отряда.
duty-neuro-tablet-foreign-network = Этот боец подключён к чужой сети.
duty-neuro-intrusion-popup = ВТОРЖЕНИЕ :: ИСТОЧНИК НЕ ОПРЕДЕЛЁН
duty-neuro-reboot-member-popup = Сеть перезапущена. Нейроинтерфейс очищен.
duty-neuro-reboot-not-leader = Перезапустить сеть отряда может только его командир.
duty-neuro-reboot-done = Сеть перезапущена, очищено нейроинтерфейсов: { $count }.
duty-neuro-reboot-clean = Сеть перезапущена. Вторжений не обнаружено.
duty-neuro-tablet-reboot-network = ПЕРЕЗАПУСК СЕТИ
duty-neuro-tracker-v2-dead = Трекер v2: сигнал { $name } потерян, носитель мёртв. Последняя локация: { $zone }.

duty-neuro-hud-header-gorlex = GORLEX // ТАКТИЧЕСКАЯ СЕТЬ
duty-neuro-flatline-prefix-gorlex = GORLEX // FLATLINE
duty-neuro-hud-intrusion = ВТОРЖЕНИЕ
duty-neuro-hud-intrusion-value = ИСТОЧНИК НЕ ОПРЕДЕЛЁН

## Тревожная кнопка

duty-neuro-panic-radio = 11-99! Тревожная кнопка { $name }, срочное подкрепление. Локация: { $zone }.
duty-neuro-panic-self = Тревожный сигнал отправлен.

## Навигатор

duty-navigator-dest-bridge = Мостик
duty-navigator-dest-security = Служба безопасности
duty-navigator-dest-medbay = Медбей
duty-navigator-dest-science = Научный отдел
duty-navigator-dest-cargo = Карго
duty-navigator-dest-engineering = Инженерный отдел
duty-navigator-dest-service = Сервис
duty-navigator-dest-evac = Эвакуация
duty-navigator-dest-arrivals = Прибытие
duty-navigator-off = Выключить навигатор
duty-navigator-no-route = Маяк отдела на этой станции не найден.
duty-navigator-route-set = Маршрут: { $name }.
duty-navigator-overlay-label = { $name } · { $meters } м

## Призрак в сети

duty-neuro-ghost-exposed = Помехи сорвали маскировку — вас снова видно в сети!
duty-neuro-ghost-restored = Маскировка восстановлена.

## Червь ICE

duty-ice-worm-out-of-range = Слишком далеко для взлома.
duty-ice-worm-no-target = У цели нет нейроинтерфейса NT-SEC.
duty-ice-worm-success = Червь внедрён в нейроинтерфейс { $target }.

## Сущности

ent-DutyNeuroNtSecImplant = имплант "Нейроинтерфейс NT-SEC"
    .desc = Внутренняя версия нейроинтерфейса маски СБ. Тот же отряд, статусы и метки, но без маски на лице. ЭМИ гасит панель на время сбоя.
ent-DutyImplantKitNeuroNtSec = операционный набор «Нейроинтерфейс NT-SEC»
    .desc = Набор с внутренним нейроинтерфейсом NT-SEC.
ent-DutyGorlexTacticalNetImplant = имплант "Тактическая сеть Горлекса"
    .desc = Боевой нейролинк мародёров Горлекса. Своя закрытая сеть — отряд, статусы и метки видят только свои. Командир ставит метки прямо из импланта.
ent-DutyImplantKitGorlexTacticalNet = операционный набор «Тактическая сеть Горлекса»
    .desc = Набор с боевым нейролинком Горлекса.
ent-DutyTrackerV2Implant = имплант "Трекер СБ v2"
    .desc = Трекер второго поколения. При критическом состоянии метка ТРЕКЕР уходит всей сети NT-SEC, а не только отряду; о смерти носителя сообщает в канал СБ.
ent-DutyImplantKitTrackerV2 = операционный набор «Трекер СБ v2»
    .desc = Набор с трекером СБ второго поколения.
ent-DutyPanicButtonImplant = имплант "Тревожная кнопка"
    .desc = Нажатие на кость запястья — и в канал СБ уходит 11-99 с локацией, а у всех носителей нейроинтерфейса появляется метка SOS. Перезарядка минута.
ent-DutyImplantKitPanicButton = операционный набор «Тревожная кнопка»
    .desc = Набор с имплантом «Тревожная кнопка».
ent-DutyNavigatorImplant = имплант "Навигатор"
    .desc = Подключается к станционным маякам и рисует на линзе маршрут до выбранного отдела.
ent-DutyImplantKitNavigator = операционный набор «Навигатор»
    .desc = Набор с имплантом «Навигатор».
ent-DutyThreatScannerImplant = имплант "Распознаватель угроз"
    .desc = Сверяет лица с криминальной базой станции. Статус — над головой, разыскиваемых и опасных подсвечивает красным контуром.
ent-DutyImplantKitThreatScanner = операционный набор «Распознаватель угроз»
    .desc = Набор с имплантом «Распознаватель угроз».
ent-DutyNeuroGhostImplant = имплант "Призрак в сети"
    .desc = Глушит сигнатуру носителя. Суит-сенсоры, монитор экипажа, трекеры и нейроинтерфейс NT показывают на его месте «НЕТ СИГНАЛА». ЭМИ срывает маскировку на минуту.
ent-DutyImplantKitNeuroGhost = операционный набор «Призрак в сети»
    .desc = Набор без маркировки. Внутри — глушитель сигнатуры.
ent-DutyIceWormImplant = имплант "Червь ICE"
    .desc = Взламывает нейроинтерфейс NT-SEC в упор. На двадцать секунд HUD цели сходит с ума и показывает ложную метку. Перезарядка три минуты.
ent-DutyImplantKitIceWorm = операционный набор «Червь ICE»
    .desc = Набор без маркировки. Внутри — взломный модуль.

ent-ActionDutyPanicButton = Тревожная кнопка (11-99)
    .desc = Экстренный вызов всей СБ с вашей локацией и меткой SOS.
ent-ActionDutyNavigator = Навигатор
    .desc = Выбрать отдел, до которого проложить маршрут.
ent-ActionDutyIceWorm = Червь ICE
    .desc = Запустить червя в нейроинтерфейс NT-SEC цели в пределах пяти шагов.
ent-ActionDutyGorlexOrderAssault = Приказ: штурм
    .desc = Поставить отряду метку штурма в выбранной точке. Только для командира.
ent-ActionDutyGorlexOrderHold = Приказ: удерживать
    .desc = Поставить отряду метку удержания позиции. Только для командира.
ent-ActionDutyGorlexOrderRetreat = Приказ: отход
    .desc = Поставить отряду метку точки отхода. Только для командира.
