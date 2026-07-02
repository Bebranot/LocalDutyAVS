# Контузия (Concussion) — оглушение от выстрелов и взрывов

Система реалистичного оглушения: близкие выстрелы и взрывы наполняют «шкалу контузии»,
которая со временем затухает. Высокая шкала = звон в ушах + затемнение экрана + «ватность» звука.
Защита головы/слуха сильно ослабляет эффект.

## Как это ощущается в игре

- **Выстрел рядом** — короткое моргание экрана (~0.2с, резко появляется, быстро гаснет) и
  маленький прирост шкалы. Чем выше шкала — тем заметнее моргание.
- **Долгая перестрелка** — шкала ползёт вверх, начинается звон в ушах, экран слегка темнеет,
  звук вокруг глохнет.
- **Взрыв рядом** — резкое затемнение (~3с) с долгим плавным fade-out + крупный прирост шкалы и звон.
  Крупный/близкий взрыв ещё и **кружит голову** (Drunk-шейдер: качание/мутность ~3с).
- **«Ватность»** — чем выше шкала, тем сильнее весь звук вокруг глохнет (low-pass, как вата в ушах).
  Сам звон не глушится — он «внутри головы».
- Справа под HP/голодом/жаждой висит полоска контузии (4 стадии). На нуле 5 секунд — пропадает.
- В шлеме всё то же самое, но **очень слабо** (−85%, сапёрный шлем −95%).

## Архитектура (ECS)

Шкала — собственный `ConcussionComponent` (НЕ StatusEffectNew: нужна интенсивность, а не фикс-длительность).
Текущее значение считается **лениво** из `Level` + `LastUpdate` (см. `SharedConcussionSystem.GetCurrentLevel`),
поэтому компонент НЕ дёргается `Dirty()` каждый тик — только когда что-то добавилось. Это сознательно:
ровно на этой грабле раньше горел `AimingComponent` (networked без состояния → спам каждый тик).

Импульсы (моргание/blackout/головокружение) — разовая косметика, шлётся направленным
`ConcussionImpulseEvent` на клиент конкретной жертвы, не предсказывается и не хранится в стейте.

```
Выстрел  ─ GunShotEvent ─► ConcussionSystem(server): лукап вокруг ствола, фолл-офф, стены глушат
Взрыв    ─ ExplosionSystem.ProcessEntity (хук // _Duty) ─► ConcussionSystem.ApplyExplosionConcussion
                                   │
                                   ├─ AddRaw (шкала, с учётом защиты) ──► Dirty (сеть)
                                   ├─ Reconcile ──► AlertsSystem.ShowAlert/ClearAlert (полоска)
                                   └─ RaiseNetworkEvent(ConcussionImpulseEvent) ─► клиент жертвы
                                                                                      │
       ConcussionSystem(client): оверлей (затемнение + Drunk-шейдер) + звон + «ватность» (occlusion)
```

### «Ватность» звука (как сделана)

Глобального low-pass в движке нет, а master gain уже занят crit-duck'ом
(`DynamicAmbientMusicSystem`) — драться за него нельзя. Поэтому используется **per-source occlusion**:
движок каждый кадр в `AudioSystem.FrameUpdate` выставляет окклюзию источникам по геометрии
(она же — low-pass: `cutoff = exp(-occlusion)`). Клиентский `ConcussionSystem.FrameUpdate` объявлен
`UpdatesAfter(AudioSystem)` и **поднимает** окклюзию всех источников до уровня по шкале (кроме звона).
Движок сбрасывает значение к геометрии каждый кадр — поэтому ничего чистить не нужно, эффект сам
исчезает, когда шкала падает. Движок (submodule-форк) при этом не патчится.

## Файлы

| Файл | Назначение |
|---|---|
| `Content.Shared/_Duty/Concussion/ConcussionComponent.cs` | Шкала + все настройки (DataField). На `BaseMobSpecies`. |
| `Content.Shared/_Duty/Concussion/ConcussionProtectionComponent.cs` | Защита слуха (Reduction, Enabled). На предметах. |
| `Content.Shared/_Duty/Concussion/ConcussionEvents.cs` | `ConcussionImpulseEvent` (Shot/Blast/Dizzy) + enum. |
| `Content.Shared/_Duty/Concussion/SharedConcussionSystem.cs` | Затухание, набор шкалы (`AddRaw`), скан защиты (`GetProtection`). |
| `Content.Server/_Duty/Concussion/ConcussionSystem.cs` | Детекция выстрелов/взрывов, импульсы, обновление полоски. |
| `Content.Client/_Duty/Concussion/ConcussionOverlay.cs` | Затемнение + моргание + blackout + Drunk-шейдер (головокружение). |
| `Content.Client/_Duty/Concussion/ConcussionSystem.cs` | Звон, «ватность» (occlusion), приём импульсов, гашение в крите. |
| `Resources/Prototypes/_Duty/Concussion/alerts.yml` | Алерт `Concussion` (4 стадии) + категория. |
| `Resources/Locale/ru-RU/_Duty/concussion.ftl` | Текст полоски (наведение). |
| `Resources/Textures/_Duty/Interface/flashbar.rsi` | Иконки полоски `flash1`…`flash4`. |

### Точки-хуки в ванильном/ADT коде (помечены `// _Duty`)

- `Content.Server/Explosion/EntitySystems/ExplosionSystem.Processing.cs` — в `ProcessEntity` вызов
  `_concussion.ApplyExplosionConcussion(entity, damage.GetTotal().Float())` + поле `[Dependency] _concussion`.
- `Resources/Prototypes/Entities/Mobs/Species/base.yml` — `- type: Concussion` на `BaseMobSpecies`.
- `Resources/Prototypes/Entities/Clothing/Head/{helmets,base_clothinghead}.yml` —
  `- type: ConcussionProtection` на базах шлемов.

## Параметры (всё в `[DataField]` на `ConcussionComponent`, правится из YAML без перекомпиляции)

| Поле | Дефолт | Смысл |
|---|---|---|
| `maxLevel` | 100 | Потолок шкалы. |
| `decayPerSecond` | 4 | Затухание (един./сек). Один взрыв «отходит» ~7–8с. |
| `shotAmount` | 0.5 | Прирост от выстрела в эпицентре. |
| `shotRange` | 5 | Радиус (тайлы) влияния выстрела. |
| `shotMinFalloff` | 0.15 | Мин. множитель на краю радиуса. |
| `blastAmount` | 30 | Прирост от взрыва при эталонном уроне. |
| `blastReferenceDamage` | 40 | Урон взрыва, дающий полный `blastAmount`. |
| `blastMaxAmount` | 60 | Кап прироста от одного взрыва. |
| `minVisibleLevel` | 1 | Ниже = «ноль» (бар скрывается через 5с). |
| `ringStartLevel` | 25 | С какого уровня начинается звон в ушах. |
| `ringSound` | `/Audio/_Duty/Effects/Tinnitus/tinnitus.ogg` | Звук звона (зациклен). |
| `muffleStartLevel` | 15 | С какого уровня звук начинает глохнуть (low-pass). |
| `muffleMaxOcclusion` | 2.5 | Сила «ватности» на пике (`cutoff = exp(-occlusion)`; ~1.5 заметно, ~2.5 как вата). |
| `dizzyBlastDamage` | — | Урон взрыва, с которого крутит голову (см. серверный компонент). |
| `dizzyNearbyLevelFraction` | — | Доля шкалы, при которой кружит даже от слабого взрыва. |
| `alert` | `Concussion` | Прототип полоски. |

Формулы:
- Выстрел: `shotAmount × falloff × (1 − защита)`, где `falloff = clamp(1 − dist/shotRange, shotMinFalloff, 1)`.
- Взрыв: `clamp(blastAmount × урон/blastReferenceDamage, 0, blastMaxAmount) × (1 − защита)`.
- Ватность: `occlusion = clamp((level − muffleStartLevel)/(maxLevel − muffleStartLevel), 0, 1) × muffleMaxOcclusion`.

## Стадии полоски (severity)

| Уровень шкалы | severity | иконка |
|---|---|---|
| >0–30% | 0 | flash1 |
| 30–60% | 1 | flash2 |
| 60–<100% | 2 | flash3 |
| 100% | 3 | flash4 |

Маппинг — `ConcussionSystem.GetSeverity`. Обновляется throttled-апдейтом раз в 0.5с (только по
контуженным мобам, не каждый тик). На нуле 5с (`ClearDelay`) — `ClearAlert`.

## Защита слуха

Сканируются слоты **head + ears + mask**, берётся **максимальный** `Reduction` среди надетого
(`SharedConcussionSystem.GetProtection`). Чтобы выдать предмету защиту:

```yml
- type: ConcussionProtection
  reduction: 0.85   # доля гашения 0..1; 0.95 = почти полностью
  enabled: true     # можно гасить для откидных забрал
```

Уже выдано (наследуется во все дочерние): `ClothingHeadHelmetBase`, `ClothingHeadEVAHelmetBase`,
`ClothingHeadHardsuitBase`, сапёрный (`0.95`), пожарные. Бронежилеты (слот suit) **не** защищают —
уши открыты, слот не сканируется.

## CVars

| CVar | Дефолт | Где |
|---|---|---|
| `duty.concussion_enabled` | true | Сервер. Вся логика (набор шкалы, импульсы, полоска). |
| `duty.concussion_effects_enabled` | true | Клиент (ARCHIVE). Затемнение + звон + ватность. |

Эффекты также уважают `accessibility.reduced_motion` (мягкое затемнение/качание без резких морганий)
и автоматически гасятся в крите/смерти.

## Кому применяется

- Шкала — всем с `ConcussionComponent` (по дефолту все гуманоиды через `BaseMobSpecies`).
- Импульсы/ватность — только живым игрокам (есть `ActorComponent`, не в крите).
- Боргам/синтетикам без компонента ничего не прилетает.

## Известные ограничения / идеи на потом

- **Глушитель**: задел есть (вклад выстрела можно домножать), но детекция подавления пока не
  подключена — оживёт вместе с модулями-глушителями из порта STALKER (Фаза 2). Точка — `OnGunShot`.
- **Живой % на наведении**: алерты показывают статический текст (движок не умеет динамику в тултипе) —
  сделано по согласованию.
- **Реальный стан/замедление** на очень высокой шкале не вешается (только аудио-видео); хук под это
  оставлен (`ApplyToEntity`).
- Звук звона — зацикленный one-shot с громкостью по шкале; при желании можно развести на два слоя
  (резкий «удар по ушам» на крупный импульс + тихий sustained-фон).
- «Ватность» применяется к UI/музыке тоже (для полного эффекта «глухоты»). Если нужно исключить —
  фильтровать источники по флагам в `ConcussionSystem.FrameUpdate`.
