## СБ и командование: рефлекторный блок, Лазарь, бодикам, стресс-анализатор, чёрный ящик капитана, нейро-печать, сканер.

duty-cyber-module-sec-reflex = РЕФЛЕКС
duty-cyber-module-sec-lazarus = ЛАЗАРЬ
duty-cyber-module-sec-bodycam = БОДИКАМ
duty-cyber-module-sec-stress = СТРЕСС
duty-cyber-module-sec-blackbox = ЧЁРНЫЙ ЯЩИК
duty-cyber-module-sec-neuroseal = НЕЙРО-ПЕЧАТЬ
duty-cyber-module-sec-neuroseal-command = НЕЙРО-ПЕЧАТЬ КОМ

ent-DutyReflexBlockImplant = имплант "Рефлекторный блок"
    .desc = Нейрошунт, гасящий часть болевого и мышечного шока. Дубинки и тейзеры бьют по носителю слабее. Гарантийная пломба NT на месте.
ent-DutyImplantKitReflexBlock = операционный набор «Рефлекторный блок»
    .desc = Стерильный набор с нейрошунтом «Рефлекторный блок». Нужен лежачий пациент и твёрдая рука.
ent-DutyLazarusImplant = имплант "Лазарь"
    .desc = Аварийный дефибриллятор размером с горошину. Если носитель умер, но тело цело, через двадцать секунд даёт разряд. Одноразовый.
ent-DutyImplantKitLazarus = операционный набор «Лазарь»
    .desc = Набор с одноразовым аварийным дефибриллятором «Лазарь».
ent-DutyBodycamImplant = имплант "Бодикам"
    .desc = Записывающий модуль службы безопасности. Фиксирует речь и события вокруг офицера, запись можно распечатать для суда.
ent-DutyImplantKitBodycam = операционный набор «Бодикам»
    .desc = Набор с записывающим модулем «Бодикам».
ent-DutyStressAnalyzerImplant = имплант "Стресс-анализатор"
    .desc = Читает пульс собеседника по микродрожанию сонной артерии. Показывает волнение, но не ложь.
ent-DutyImplantKitStressAnalyzer = операционный набор «Стресс-анализатор»
    .desc = Набор с модулем «Стресс-анализатор».
ent-DutyBlackBoxCaptainImplant = имплант "Чёрный ящик капитана"
    .desc = Закалённый регистратор. Если носитель погибает, в командный канал уходит координата и запись последних тридцати секунд разговоров вокруг.
ent-DutyImplantKitBlackBoxCaptain = операционный набор «Чёрный ящик капитана»
    .desc = Набор с закалённым регистратором «Чёрный ящик капитана».
ent-DutyNeuroSealImplant = имплант "Нейро-печать СБ"
    .desc = Служебная печать доступа под кожей. Шлюзы открываются по нейросигналу, карту не украсть, но можно срезать вместе с рукой хирурга.
ent-DutyImplantKitNeuroSeal = операционный набор «Нейро-печать СБ»
    .desc = Набор с печатью доступа службы безопасности.
ent-DutyNeuroSealCommandImplant = имплант "Нейро-печать командования"
    .desc = Печать доступа уровня командования. Шлюзы не отличают носителя от капитанского ID. Выдаётся только по приказу.
ent-DutyImplantKitNeuroSealCommand = операционный набор «Нейро-печать командования»
    .desc = Набор с печатью доступа командования.
ent-DutyCyberScanner = сканер кибернетики
    .desc = Ручной сканер СБ. Показывает импланты цели, в том числе спиленные, как «неопознанный модуль».
ent-DutyActionBodycamPrint = Распечатать запись бодикама
    .desc = Печатает на бумаге журнал речи и событий вокруг вас.

duty-sec-stress-calm = Пульс { $target } ровный.
duty-sec-stress-light = Пульс { $target } учащён.
duty-sec-stress-heavy = Пульс { $target } бешеный, рваный.
duty-sec-stress-critical = Пульс { $target } едва прощупывается.
duty-sec-stress-dead = Пульса у { $target } нет.

duty-sec-lazarus-detail-active = РАЗРЯД
duty-sec-lazarus-detail-burned = ВЫГОРЕЛ
duty-sec-lazarus-detail-failed = СБОЙ
duty-sec-lazarus-revived = Тело { $target } дёргается от разряда и делает рваный вдох!
duty-sec-lazarus-failed = Под кожей тихо щёлкает и пахнет палёным. Лазарь не справился.

duty-sec-rec-speech = { $name }: «{ $text }»
duty-sec-rec-crit = носитель потерял сознание
duty-sec-rec-dead = носитель погиб
duty-sec-blackbox-header = ЧЁРНЫЙ ЯЩИК :: { $name } погиб, координаты { $x }, { $y }. Запись { $seconds } с:
duty-sec-blackbox-silence = вокруг было тихо.

duty-sec-bodycam-offline = Бодикам не отвечает.
duty-sec-bodycam-cooldown = Принтер бодикама ещё остывает.
duty-sec-bodycam-empty = Запись пуста.
duty-sec-bodycam-printed = Из-под кожи на запястье выползает лист с записью.
duty-sec-bodycam-paper-header = ЗАПИСЬ БОДИКАМА :: { $name }
duty-sec-bodycam-paper-name = запись бодикама

duty-sec-scan-start = { $user } наводит сканер на { $target }.
duty-sec-scan-clean = Сканер: у { $target } имплантов не обнаружено.
duty-sec-scan-result = Сканер: у { $target } обнаружено: { $modules }.
duty-sec-scan-load = Нейронагрузка: { $load } из { $limit }.
duty-sec-scan-emp = Кибер-системы подавлены ЭМИ.
duty-sec-scan-popup-clean = Чисто.
duty-sec-scan-popup-found = Импланты: { $count }. Подробности в чате.
