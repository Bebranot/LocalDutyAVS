## Дефибрилляторы LifePak-40s / LifePak-45-A

ent-DutyDefibrillatorLifePak40s = LifePak-40s
    .desc = Классический дефибриллятор, лёгок в использовании.
ent-DutyDefibrillatorLifePak40sEmpty = { ent-DutyDefibrillatorLifePak40s }
    .desc = { ent-DutyDefibrillatorLifePak40s.desc }
ent-DutyDefibrillatorLifePak45A = LifePak-45-A
    .desc = Продвинутый автоматический дефибриллятор.
ent-DutyDefibrillatorLifePak45AEmpty = { ent-DutyDefibrillatorLifePak45A }
    .desc = { ent-DutyDefibrillatorLifePak45A.desc }
ent-DutyDefibrillatorLifePak45ACMO = { ent-DutyDefibrillatorLifePak45A }
    .desc = { ent-DutyDefibrillatorLifePak45A.desc }
    .suffix = ГВ, цель кражи
duty-steal-target-groups-lifepak-45a = LifePak-45-A главного врача
duty-research-technology-lifepak-45a = Дефибриллятор LifePak-45-A
ent-DutyDefibGel = электродный гель
    .desc = Тюбик токопроводящего геля. Нанесите на грудь перед разрядом: лечение чуть сильнее, ожогов от электродов нет.

# Уровни энергии
duty-defib-energy-low = Low
duty-defib-energy-standard = Standard
duty-defib-energy-high = High
duty-defib-verb-energy = Энергия: { $level }
duty-defib-energy-set = Энергия разряда: { $level }.
duty-defib-examine-energy = Энергия разряда: [color=yellow]{ $level }[/color].
duty-defib-examine-two-step = Сначала проводит анализ ритма, разряд — повторным применением.

# Голос аппарата
duty-defib-voice-analyzing = Анализ ритма. Не прикасайтесь к пациенту.
duty-defib-voice-shock-advised = Разряд рекомендован. Зарядка.
duty-defib-voice-push-to-shock = Разряд рекомендован. Нажмите кнопку разряда.
duty-defib-voice-no-shock = Разряд не рекомендован.
duty-defib-voice-stand-clear = Всем отойти от пациента!
duty-defib-voice-check-pulse = Проверьте пульс.
duty-defib-voice-cardioversion = Кардиоверсия. Зарядка.
duty-defib-voice-rhythm-restored = Ритм восстановлен.

# Причины отказа
duty-defib-pulse-detected = Обнаружен пульс. Разряд отменён.
duty-defib-no-contact = Нет контакта электродов с кожей.
duty-defib-reason-damage = Ритм не поддаётся разряду: тело слишком повреждено.
duty-defib-reason-low-energy = Недостаточно энергии.
duty-defib-busy = Аппарат занят: дождитесь окончания анализа или разряда.
duty-defib-not-ready = Аппарат не готов к следующему разряду.
duty-defib-reason-unresponsive = Реакция пациента не определяется. Повторите разряд, чтобы провести его принудительно.

# Анализ ритма (45-A)
duty-defib-verb-rhythm-check = Анализ ритма
duty-defib-rhythm-alive = Пульс есть. Ритм в норме.
duty-defib-rhythm-alive-arrhythmia = Пульс есть. Обнаружена аритмия: показана кардиоверсия (Low).
duty-defib-rhythm-advised = Разряд рекомендован.
duty-defib-rhythm-deficit = Ритм не поддаётся разряду. Для разряда снизьте урон ещё на ≈{ $amount }. Преобладает: { $type }.

# Гель
duty-defib-gel-applied = Гель нанесён на грудь.
duty-defib-gel-no-contact = Скафандр не даёт нанести гель на кожу.
duty-defib-gel-examine = Осталось применений: [color=yellow]{ $uses }[/color].

# Аритмия
duty-defib-arrhythmia-skip = Сердце пропускает удар...

# Анализатор здоровья
health-analyzer-trauma-arrhythmia = [color=orange]Аритмия: возможны рецидивы остановки сердца[/color]
health-analyzer-trauma-revival-weakness = [color=yellow]Слабость после реанимации[/color]
