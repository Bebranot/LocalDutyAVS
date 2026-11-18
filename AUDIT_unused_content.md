# Аудит потенциально неиспользуемого контента форков (LocalDutyAVS)

> **Только чтение.** Отчёт сформирован статическим анализом (Python-скрипт). Категории `(1)` и `(2)` показывают отсутствие статически найденных ссылок и **не являются** автоматическим разрешением на удаление без ручной проверки.

## 1. Инвентаризация целевых путей

| Раздел | Путь | Статус | Файлов | Размер (МБ) |
|---|---|---|---:|---:|
| Прототипы | `Resources/Prototypes/ADT` | Существует | 2045 | 7.73 |
| Прототипы | `Resources/Prototypes/Corvax` | Существует | 165 | 0.32 |
| Прототипы | `Resources/Prototypes/Goobstation` | Существует | 1 | 0.00 |
| Прототипы | `Resources/Prototypes/_Goobstation` | Существует | 34 | 0.07 |
| Прототипы | `Resources/Prototypes/_NF` | Существует | 1 | 0.00 |
| Прототипы | `Resources/Prototypes/_FXX` | Существует | 2 | 0.03 |
| Текстуры | `Resources/Textures/ADT` | Существует | 23039 | 48.81 |
| Текстуры | `Resources/Textures/Corvax` | Существует | 1349 | 17.71 |
| Текстуры | `Resources/Textures/Goobstation` | **Отсутствует** | 0 | 0.00 |
| Текстуры | `Resources/Textures/_Goobstation` | Существует | 129 | 0.15 |
| Текстуры | `Resources/Textures/_NF` | Существует | 20 | 0.01 |
| Текстуры | `Resources/Textures/_FXX` | Существует | 22 | 0.01 |
| Текстуры | `Resources/Textures/_Duty/Ported` | Существует | 470 | 0.63 |
| Звуки | `Resources/Audio/ADT` | Существует | 1105 | 80.59 |
| Звуки | `Resources/Audio/Corvax` | Существует | 60 | 9.98 |
| Звуки | `Resources/Audio/Goobstation` | **Отсутствует** | 0 | 0.00 |
| Звуки | `Resources/Audio/_Goobstation` | Существует | 9 | 0.21 |
| Звуки | `Resources/Audio/_NF` | **Отсутствует** | 0 | 0.00 |
| Звуки | `Resources/Audio/_FXX` | **Отсутствует** | 0 | 0.00 |
| C# (_Goobstation) | `Content.Server/_Goobstation` | Существует | 26 | 0.10 |
| C# (_Goobstation) | `Content.Client/_Goobstation` | Существует | 19 | 0.01 |
| C# (_Goobstation) | `Content.Shared/_Goobstation` | Существует | 99 | 0.16 |
| **Итого** | — | — | **28595** | **166.55** |

## 2. Прототипы (`Resources/Prototypes/...`)

Единица учёта — отдельный прототип (`- type: ...`, `id: ...`).

| Папка | Всего | (1) Нет ссылок | (2) Ссылки только из неиспользуемого | (2*) Только из локали | Неопределённо | (3) Используется |
|---|---:|---:|---:|---:|---:|---:|
| `Resources/Prototypes/ADT` | 18321 | 81 | 3 | 2 | 6488 | 11747 |
| `Resources/Prototypes/Corvax` | 944 | 2 | 0 | 0 | 370 | 572 |
| `Resources/Prototypes/Goobstation` | 1 | 0 | 0 | 0 | 0 | 1 |
| `Resources/Prototypes/_FXX` | 83 | 0 | 0 | 0 | 22 | 61 |
| `Resources/Prototypes/_Goobstation` | 153 | 1 | 2 | 0 | 110 | 40 |
| `Resources/Prototypes/_NF` | 1 | 0 | 0 | 0 | 0 | 1 |

**Примечание по прототипам:**
- Все `entity`-прототипы в целевых папках оказались либо в категории `(3) используется` (достижимы от карт, C#-кода вне `_Goobstation` или нецелевых прототипов), либо в категории `неопределённо` (достижимы через лоадауты, рецепты крафта, таблицы лута, `thiefBackpackSet` и т.п.).
- В категории `(1)` и `(2)` попали специализированные типы:
  - `Resources/Prototypes/ADT`: `adtTenCode` (42), `soundCollection` (13), `biomeMarkerLayer` (8), `uiTheme` (5), `font` (3), `particleEffect` (3), `nuclearReactorPrefab` (3), `statusEffect` (2), `planet` (1), `accessGroup` (1), `stealTargetGroup` (1), `stack` (1), `deviceFrequency` (1), `inventoryTemplate` (1), `loadoutEffectGroup` (1)
  - `Resources/Prototypes/Corvax`: `soundCollection` (1), `stack` (1)
  - `Resources/Prototypes/_Goobstation`: `changelingStartingEvolution` (2), `internalResourcesThresholds` (1)

## 3. Текстуры (`Resources/Textures/...`)

Единица учёта — `.rsi`-папка целиком (спрайт со всеми `.png` и `meta.json` внутри) либо отдельный медиафайл (`.png`, `.jpg`, `.webp`, `.svg` и т.д.) вне `.rsi`. Файлы метаданных (`attributions.yml`, шейдеры `.swsl`, `.txt`, `.rar`/`.zip`) исключены из кандидатов.

| Папка | Единиц (RSI/файлы) | (1) Нет ссылок | (2) Ссылки только из неиспользуемого | Неопределённо | (3) Используется | Метаданные/прочее |
|---|---:|---:|---:|---:|---:|---:|
| `Resources/Textures/ADT` | 3684 | 433 (7.95 МБ) | 0 (0.00 МБ) | 831 (14.96 МБ) | 2386 (25.81 МБ) | 34 (0.10 МБ) |
| `Resources/Textures/Corvax` | 218 | 26 (0.21 МБ) | 0 (0.00 МБ) | 54 (12.93 МБ) | 116 (4.58 МБ) | 22 (0.00 МБ) |
| `Resources/Textures/_Duty/Ported` | 95 | 0 (0.00 МБ) | 0 (0.00 МБ) | 0 (0.00 МБ) | 94 (0.63 МБ) | 1 (0.00 МБ) |
| `Resources/Textures/_FXX` | 4 | 0 (0.00 МБ) | 0 (0.00 МБ) | 3 (0.01 МБ) | 1 (0.00 МБ) | 0 (0.00 МБ) |
| `Resources/Textures/_Goobstation` | 19 | 0 (0.00 МБ) | 1 (0.01 МБ) | 13 (0.07 МБ) | 5 (0.07 МБ) | 0 (0.00 МБ) |
| `Resources/Textures/_NF` | 2 | 1 (0.00 МБ) | 0 (0.00 МБ) | 0 (0.00 МБ) | 1 (0.01 МБ) | 0 (0.00 МБ) |

## 4. Звуки (`Resources/Audio/...`)

Единица учёта — аудиофайл (`.ogg`, `.wav`, `.mp3`, `.opus`, `.flac`). Служебные файлы (`attributions.yml`, `.txt`) отнесены к метаданным.

| Папка | Файлов (с метаданными) | (1) Нет ссылок | (2) Ссылки только из неиспользуемого | Неопределённо | (3) Используется | Метаданные |
|---|---:|---:|---:|---:|---:|---:|
| `Resources/Audio/ADT` | 1105 | 116 (4.22 МБ) | 17 (0.22 МБ) | 169 (5.19 МБ) | 712 (70.90 МБ) | 91 (0.06 МБ) |
| `Resources/Audio/Corvax` | 60 | 13 (1.15 МБ) | 3 (0.03 МБ) | 0 (0.00 МБ) | 32 (8.80 МБ) | 12 (0.00 МБ) |
| `Resources/Audio/_Goobstation` | 9 | 0 (0.00 МБ) | 5 (0.17 МБ) | 1 (0.01 МБ) | 2 (0.03 МБ) | 1 (0.00 МБ) |

## 5. C#-компоненты (`Content.*_Goobstation`)

Проверка: упомянут ли компонент (без суффикса `Component`) хотя бы раз в прототипах (`Resources/Prototypes/**/*.yml`) или картах, а также используется ли класс компонента в остальном C#-коде вне `_Goobstation`.

| Папка | Всего компонентов | (1) Нет ссылок | (2) Ссылки только внутри `_Goobstation` | Неопределённо | (3) Используется |
|---|---:|---:|---:|---:|---:|
| `Content.Server/_Goobstation` | 5 | 0 | 0 | 0 | 5 |
| `Content.Client/_Goobstation` | 0 | 0 | 0 | 0 | 0 |
| `Content.Shared/_Goobstation` | 42 | 5 | 18 | 6 | 13 |

Список компонентов без упоминаний в прототипах (`yaml_mentions = 0`) и не используемых в остальном C#-коде:
- `FlashVulnerableComponent` — (1) нет ссылок (`Content.Shared/_Goobstation/Common/Flash/FlashVulnerableComponent.cs`)
- `OvertimeStaminaDamageComponent` — (1) нет ссылок (`Content.Shared/_Goobstation/Common/Stunnable/OvertimeStaminaDamageComponent.cs`)
- `SpecialBreathingImmunityComponent` — (1) нет ссылок (`Content.Shared/_Goobstation/Common/Body/Components/SpecialBreathingImmunityComponent.cs`)
- `SpecialLowTempImmunityComponent` — (1) нет ссылок (`Content.Shared/_Goobstation/Common/Temperature/Components/SpecialLowTempImmunityComponent.cs`)
- `StamcritResistComponent` — (1) нет ссылок (`Content.Shared/_Goobstation/Common/Stunnable/StamcritResistComponent.cs`)
- `AugmentedEyesightComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/Changeling/Components/AugmentedEyesightComponent.cs`)
- `BoostedImmunityComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/SpecialPassives/BoostedImmunity/Components/BoostedImmunityComponent.cs`)
- `ChameleonSkinComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/Changeling/Components/ChameleonSkinComponent.cs`)
- `ChangelingBiomassComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/Changeling/Components/ChangelingBiomassComponent.cs`)
- `ChangelingEggComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/Changeling/Components/ChangelingEggComponent.cs`)
- `DarknessAdaptionComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/Changeling/Components/DarknessAdaptionComponent.cs`)
- `DelayedKnockdownComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/Common/Weapons/DelayedKnockdown/DelayedKnockdownComponent.cs`)
- `FleshmendComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/SpecialPassives/Fleshmend/Components/FleshmendComponent.cs`)
- `FleshmendEffectComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/SpecialPassives/Fleshmend/Components/FleshmendEffectComponent.cs`)
- `HivemindComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/Changeling/Components/HivemindComponent.cs`)
- `InternalResourcesComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/InternalResources/Components/InternalResourcesComponent.cs`)
- `PartialAbsorbableComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/Changeling/Components/PartialAbsorbableComponent.cs`)
- `PreventSelfRevivalComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/Common/Devour/PreventSelfRevivalComponent.cs`)
- `SuperAdrenalineComponent` — (2) только внутри `_Goobstation` C# (`Content.Shared/_Goobstation/SpecialPassives/SuperAdrenaline/Components/SuperAdrenalineComponent.cs`)

## 6. Топ-20 крупнейших кандидатов (категории 1 и 2)

| # | Тип | Категория | Размер (КБ) | Файлов | Путь / Идентификатор |
|---:|---|---|---:|---:|---|
| 1 | Текстура/RSI | (1) нет ссылок | 2525.4 | 1 | `Resources/Textures/ADT/Logo/old_logo_1.png` |
| 2 | Текстура/RSI | (1) нет ссылок | 2068.8 | 1 | `Resources/Textures/ADT/Logo/old_logo2.png` |
| 3 | Текстура/RSI | (1) нет ссылок | 1119.5 | 1 | `Resources/Textures/ADT/Parallaxes/NES/PurpleParallax2.png` |
| 4 | Звук | (1) нет ссылок | 703.0 | 1 | `Resources/Audio/ADT/ling-drug-mob.ogg` |
| 5 | Текстура/RSI | (1) нет ссылок | 698.0 | 1 | `Resources/Textures/ADT/Parallaxes/NES/BlueParallax.png` |
| 6 | Звук | (1) нет ссылок | 544.9 | 1 | `Resources/Audio/ADT/Misc/kappa.ogg` |
| 7 | Звук | (1) нет ссылок | 310.7 | 1 | `Resources/Audio/ADT/Supermatter/altdelta.ogg` |
| 8 | Звук | (1) нет ссылок | 302.2 | 1 | `Resources/Audio/ADT/Ambience/SpaceWhale/leviathan-appear.ogg` |
| 9 | Звук | (1) нет ссылок | 208.2 | 1 | `Resources/Audio/ADT/Ambience/fan_drone.ogg` |
| 10 | Текстура/RSI | (1) нет ссылок | 207.9 | 2 | `Resources/Textures/ADT/Mobs/Demons/Gods/vastness_space.rsi` |
| 11 | Звук | (1) нет ссылок | 188.6 | 1 | `Resources/Audio/Corvax/Adminbuse/noert.ogg` |
| 12 | Звук | (1) нет ссылок | 169.9 | 1 | `Resources/Audio/ADT/Announcements/outbreak7.ogg` |
| 13 | Текстура/RSI | (1) нет ссылок | 166.1 | 3 | `Resources/Textures/ADT/Mobs/Demons/Gods/creator_god.rsi` |
| 14 | Звук | (1) нет ссылок | 147.3 | 1 | `Resources/Audio/Corvax/Announcements/greytide.ogg` |
| 15 | Текстура/RSI | (1) нет ссылок | 137.0 | 1 | `Resources/Textures/ADT/Parallaxes/NES/planet3.png` |
| 16 | Звук | (1) нет ссылок | 133.5 | 1 | `Resources/Audio/Corvax/Misc/siren.ogg` |
| 17 | Звук | (1) нет ссылок | 125.0 | 1 | `Resources/Audio/Corvax/Announcements/flux.ogg` |
| 18 | Звук | (1) нет ссылок | 120.1 | 1 | `Resources/Audio/ADT/epsilon-event.ogg` |
| 19 | Звук | (2) только неиспользуемое | 118.1 | 1 | `Resources/Audio/_Goobstation/Ambience/Antag/changeling_start.ogg` |
| 20 | Звук | (1) нет ссылок | 111.2 | 1 | `Resources/Audio/Corvax/Announcements/dust.ogg` |

## 7. Метод подсчёта и что не охвачено

### Метод
1. **Прототипы:** распарсены все `42 889` прототипов (`38 740` уникальных `id`) в `Resources/Prototypes/**/*.yml`. Построен направленный граф ссылок (`parent:`, `spawn:`, `reagent:`, любые совпадения токенов с известными `id`).
   - **Корни категории (3) «используется»:** ссылки из карт (`Resources/Maps/**/*.yml` — `10 587` ID), из C#-кода вне `Content.*_Goobstation` (`1 997` ID), из прототипов вне целевых папок (базовый SS14 / `_Duty`) и прочих конфигов `Resources/` (кроме `Prototypes`/`Maps`/`Locale`/`Textures`/`Audio`). Достижимые от корней прототипы также помечены как `(3)`.
   - **Категория «неопределённо»:** прототипы с динамически загружаемыми/реестровыми типами (`marking`, `loadout`, `startingGear`, `latheRecipe`, `construction`, `microwaveMealRecipe`, `gameRule`, `stationEvent`, `job`, `salvageMap`, `thiefBackpackSet`, `adtDungeonRoom` и типы из `EnumeratePrototypes<T>` / `AllPrototypes<T>` в C#), а также `515` ID, упомянутых в `Resources/migration.yml`, и все прототипы, достижимые от них.
   - **Категория (2):** прототипы, на которые ссылаются только другие прототипы из категорий `(1)`/`(2)` или код `_Goobstation`. Отдельно выделена `(2*)` — ссылки только из `.ftl`-файлов локали (`Resources/Locale`).
   - **Категория (1):** прототипы, на которые не найдено ни одной ссылки.
2. **Текстуры и звуки:** каждый `.rsi`-каталог учитывался как единый спрайтовый ассет (сумма файлов и байтов внутри `.rsi`), одиночные медиафайлы — пофайлово. Ссылки извлекались из всех прототипов, карт, C#-кода и конфигов `Resources/`. Динамические префиксы путей из C# интерполяций `$"..."` (`ADT/Heretic/Ambience/Antag/Heretic/ascend_`, `ADT/Interface/FissionGenerator/indicator_lamps`, `ADT/Interface/FissionGenerator/reactor_part_inserted`) отнесены к категории `неопределённо`.
3. **C#-компоненты `_Goobstation`:** из `Content.{Server,Client,Shared}/_Goobstation` извлечены все классы `*Component` (`47` шт.), проверено наличие их YAML-имени (`- type: <Name>`) во всех прототипах и картах, а также упоминания класса в остальном C#-коде.

### Что не охвачено и ограничения
- **Динамическая конкатенация путей и ID в C#** (не через `$"..."`, а через `string.Format`, `Path.Combine` с переменными или таблицы в коде) может не распознаваться статическим поиском строковых литералов.
- **Внешние / серверные конфиги и БД** вне репозитория (например, ротация карт, активные пресеты и списки админ-спавна) не анализировались.
- **Состояние `Content.*_Goobstation`:** часть компонентов не имеет упоминаний в YAML, так как добавляется в рантайме через `AddComp`/`EnsureComp` (например, способности Чейнджлинга); при этом они помечены как `(2)`, если на них нет ссылок из остального C#-кода.
- **Метаданные в `Resources/Textures` и `Resources/Audio`:** найдены `57` не-медиа файлов в текстурах (включая `.yml`, `.swsl`, `.txt`, а также 3 архива `.rar`/`.zip`) и `104` не-аудио файла в звуках (`.yml`, `.txt`). Они исключены из медиа-кандидатов, но включены в общий размер папок.
- **`RobustToolbox/`** полностью исключён из анализа.
