# _Duty: система тяжёлых травм

# Тестовые команды duty* (форсируют реальный ролл травмы через TraumaRollSystem, не добавление
# компонента напрямую — шанс высокий, но НЕ 100%, см. Debug*Chance в TraumaRollSystem)
duty-trauma-debug-fracture-description = Форсирует ролл перелома (высокий, но не 100% шанс)
duty-trauma-debug-fracture-help = dutybreakbone [цель] [зона] — зона: head/torso/leftarm/rightarm/leftleg/rightleg, без аргумента — на себе / случайная зона
duty-trauma-debug-dislocation-description = Форсирует ролл вывиха (высокий, но не 100% шанс)
duty-trauma-debug-dislocation-help = dutydislocate [цель] [зона] — зона обязана быть суставной (рука/нога)
duty-trauma-debug-arterial-description = Форсирует ролл артериального кровотечения (высокий, но не 100% шанс)
duty-trauma-debug-arterial-help = dutybleedout [цель] — без аргумента — на себе
duty-trauma-debug-head-description = Форсирует ролл перелома головы (запускает сотрясение мозга)
duty-trauma-debug-head-help = dutyconcussion [цель] — перелом зоны «голова», сотрясение включится автоматически

duty-trauma-debug-name-fracture = Перелом
duty-trauma-debug-name-dislocation = Вывих
duty-trauma-debug-name-arterial = Артериальное кровотечение
duty-trauma-debug-name-head = Перелом головы (→ сотрясение)

duty-trauma-debug-hint-target = <цель (необязательно, по умолчанию — себя)>
duty-trauma-debug-hint-zone = <зона (необязательно)>
duty-trauma-debug-zone-none = —

duty-trauma-debug-success = { $trauma } ({ $zone }): УДАЛОСЬ — шанс был { $chance }%.
duty-trauma-debug-fail = { $trauma } ({ $zone }): не сработало — шанс был { $chance }%. Попробуй ещё раз.
duty-trauma-debug-error-target = Не удалось найти сущность «{ $arg }».
duty-trauma-debug-error-no-player = Нужно указать цель или управлять персонажем.
duty-trauma-debug-error-zone = Неизвестная зона «{ $arg }». Варианты: head, torso, leftarm, rightarm, leftleg, rightleg.
duty-trauma-debug-error-invalid-target = Цель не может получить эту травму (нет TraumaTargetComponent, зона недоступна существу, или зона не суставная для вывиха).

# Предметы
ent-DutyImprovisedTourniquet = самодельный жгут
    .desc = Полоса ткани, закрученная вокруг палки. Грубо, но артерию пережмёт.

# Осмотр здоровья (ПКМ → осмотреть → здоровье)
trauma-examine-arterial = [color=crimson]Кажется, повреждена крупная артерия — кровь идёт толчками.[/color]
trauma-examine-fracture = [color=orange]{ $zone }: { $tier }.[/color]
trauma-examine-fracture-splinted = [color=yellow]{ $zone }: { $tier } (в шине).[/color]

# Анализатор здоровья
health-analyzer-trauma-fracture = [color=orange]{ $zone }: { $tier }[/color]
health-analyzer-trauma-fracture-splinted = [color=yellow]{ $zone }: { $tier } (в шине)[/color]
health-analyzer-trauma-dislocation = [color=orange]{ $zone }: вывих[/color]
health-analyzer-trauma-residual = [color=gray]{ $zone }: недавно вправлена[/color]
health-analyzer-trauma-arterial = [color=crimson]Артериальное кровотечение[/color]
health-analyzer-trauma-head = [color=orange]Сотрясение мозга ({ $tier })[/color]

# Зоны тела
trauma-zone-head = Голова
trauma-zone-torso = Грудная клетка
trauma-zone-left-arm = Левая рука
trauma-zone-right-arm = Правая рука
trauma-zone-left-leg = Левая нога
trauma-zone-right-leg = Правая нога

# Тиры перелома
trauma-fracture-tier-crack = трещина
trauma-fracture-tier-full = перелом
trauma-fracture-tier-open = открытый перелом

# Функциональные эффекты
trauma-fracture-leg-pain = Острая боль пронзает сломанную ногу!

# Шинирование
trauma-verb-splint = Наложить шину
trauma-splint-start = Вам накладывают шину — не двигайтесь.
trauma-splint-success = Шина наложена, кость зафиксирована.
trauma-splint-fail-scream = Раздаётся крик боли!

# Сотрясение мозга (не путать с аудио-контузией _Duty/Concussion)
trauma-examine-head = [color=orange]Взгляд расфокусирован, реакции заторможены — похоже на сотрясение ({ $tier }).[/color]
trauma-head-tier-light = лёгкое
trauma-head-tier-medium = среднее
trauma-head-tier-severe = тяжёлое
trauma-head-received = В голове звенит, мир плывёт перед глазами.
trauma-head-symptom = Подступает тошнота, голова раскалывается.
trauma-head-blackout = В глазах темнеет, вы теряете сознание!

# Вывих
trauma-examine-dislocation = [color=orange]{ $zone }: вывих — сустав неестественно вывернут.[/color]
trauma-examine-residual = [color=gray]{ $zone }: недавно вправлена, ещё слаба.[/color]
trauma-verb-reduce-dislocation = Вправить вывих
trauma-reduce-start = Вправление сустава — не двигайтесь.
trauma-reduce-success = Сустав встал на место.
trauma-reduce-fail = Не удалось вправить — резкая боль!

# Лечение артериального кровотечения (BUI)
trauma-verb-category-self-treatment = Самолечение
trauma-verb-treat-arterial = Остановить артериальное кровотечение

trauma-arterial-window-title = Остановка кровотечения
trauma-arterial-info = Выполняйте этапы по порядку. Не двигайтесь — иначе действие прервётся.
trauma-arterial-info-done = Кровотечение остановлено.

trauma-arterial-step-palm = Прижать рану ладонью
trauma-arterial-step-finger = Прижать пальцем
trauma-arterial-step-tourniquet = Наложить самодельный жгут
trauma-arterial-step-tighten = Затянуть жгут
trauma-arterial-step-tighten-progress = Затянуть жгут ({ $current }/{ $total })

trauma-arterial-need-material = Нужно две ткани или жгут в руке.
trauma-arterial-treated = Артериальное кровотечение остановлено.
