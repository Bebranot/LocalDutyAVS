## Импланты Мародёров Горлекса.

duty-cyber-module-bloodloop = КОНТУР
duty-cyber-module-drumdamper = ДЕМПФЕР
duty-cyber-module-smartlink = СМАРТЛИНК
duty-cyber-module-thermal = ТЕРМОВИЗОР
duty-cyber-module-blackbox = ЧЁРНЫЙ ЯЩИК

ent-DutyBloodLoopImplant = имплант "Кровавый контур"
    .desc = Контур из военного хлама, гонящий кровь в обход всего, что болит. Один раз вытащит из крита — и выставит счёт.
ent-DutyDrumDamperImplant = имплант "Барабанный демпфер"
    .desc = Гасит удар по барабанным перепонкам. Платой идёт шёпот: тихая речь доходит обрывками.
ent-DutySmartlinkImplant = имплант "Смартлинк"
    .desc = Связь «мозг–ствол» на трофейной частоте Горлекса. Работает только с оружием, помеченным их клеймом, а под ЭМИ превращает руки в студень.
ent-DutyGorlexThermalImplant = имплант "Термовизор"
    .desc = Тепловая матрица, вросшая в зрительный нерв. Видит тёплое сквозь переборки, жрёт калории и ненавидит вспышки.
ent-DutyBlackBoxImplant = имплант "Чёрный ящик"
    .desc = Самописец в грудине. Когда оперативник умирает, ящик успевает сообщить своим где, кто и когда. Мёртвого можно допросить мультитулом.

ent-DutyImplantKitBloodLoop = операционный набор «Кровавый контур»
    .desc = Набор с контуром Горлекса. Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitDrumDamper = операционный набор «Барабанный демпфер»
    .desc = Набор с демпфером Горлекса. Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitSmartlink = операционный набор «Смартлинк»
    .desc = Набор со смартлинком Горлекса. Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitGorlexThermal = операционный набор «Термовизор»
    .desc = Набор с тепловой матрицей Горлекса. Нужен лежачий пациент и твёрдая рука.
ent-DutyImplantKitBlackBox = операционный набор «Чёрный ящик»
    .desc = Набор с самописцем Горлекса. Нужен лежачий пациент и твёрдая рука.

ent-DutyActionGorlexThermal = Термовизор
    .desc = Включает или выключает тепловое зрение.

duty-gorlex-module-deny = Модуль не отвечает.

# Кровавый контур
duty-gorlex-bloodloop-rise-self = Кровь превращается в кипяток — боль отступает, ноги снова держат!
duty-gorlex-bloodloop-rise-others = { CAPITALIZE(THE($target)) } рывком поднимается на ноги, глаза налиты кровью!
duty-gorlex-bloodloop-rollback = Контур схлопывается. Мир гаснет, тело отказывается жить.
duty-gorlex-bloodloop-quenched = Успокоительное глушит контур — разгон так и не начался.

# Термовизор
duty-gorlex-thermal-on = Тепловая матрица включается: стены становятся прозрачными для живого тепла.
duty-gorlex-thermal-off = Тепловая матрица гаснет.

# Чёрный ящик
duty-gorlex-blackbox-unknown = неизвестно
duty-gorlex-blackbox-zone-grid = { $grid } ({ $x }, { $y })
duty-gorlex-blackbox-zone-space = открытый космос ({ $x }, { $y })
duty-gorlex-blackbox-report = ЧЁРНЫЙ ЯЩИК: { $name } погиб. Зона: { $zone }. Убийца: { $killer }. Время раунда: { $time }.
duty-gorlex-blackbox-verb = Допросить чёрный ящик
duty-gorlex-blackbox-start = Вы подключаете мультитул к разъёмам под рёбрами { $target }.
duty-gorlex-blackbox-done = Ящик выдаёт последний маршрут.
duty-gorlex-blackbox-dump-head = Чёрный ящик { $name }, последний маршрут:
duty-gorlex-blackbox-dump-empty = записей нет.
