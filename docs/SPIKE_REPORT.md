# Фаза 1: проверка парсера на реплеях v43 (8 ноября 2026)

Парсер: NuGet `FortniteReplayReader` 3.1.0 (Shiqan/FortniteReplayDecompressor, MIT) **без форка**.
Свои доработки живут в `src/PirateChest.ReplayReader` (наследник `FortniteReplayReader.ReplayReader`).

## Итог прогона

| | |
|---|---|
| Реплеев в папке Demos | 97 (`%LOCALAPPDATA%\FortniteGame\Saved\Demos`) |
| Разобрано | **96** |
| Упало | 1: `Replay is marked encrypted but not yet marked as completed` — это матч, который шёл в момент прогона (реплей ещё пишется). Поэтому наблюдатель трогает только завершённые реплеи. |
| Билд | `++Fortnite+Release-43.00`, CL 59594394 — у всех 96 |
| Плейлисты | `Playlist_NoBuildBR_Squad` 42, `_Duo` 33, `_Trio` 19, `_Solo` 1, без плейлиста 1 (матч на 30 секунд) |
| Владелец реплея (`RecorderPlayerState` → `IsReplayOwner`) | найден в 95 из 96 (нет только в 30-секундном матче) |
| Время разбора | 2–5 с на реплей |

Вывод: **v43 читается без правок парсера**, форк и PR в upstream не нужны.

## Группы, ставшие Incompatible

Только поля, которые есть в моделях upstream и не прочитались (поля, которых нет в моделях, upstream тоже помечает Incompatible — их я отфильтровал):

| Поле | В скольких реплеях | Влияние |
|---|---|---|
| `FortPoiManager.WorldGridStart/End/Spacing/TotalSize` | 95–96 | нет (не используем) |
| `Athena_GameState_C.TeamFlightPaths` | 95 | нет (маршрут автобуса не нужен) |
| `FortPickupAthena.ReplicatedMovement` | 48 | нет |

Поля, нужные счётчику (`bAlreadySearched`, `SearchingPawn`, `SearchAnimationCount`, `bDestroyed`, `ReplicatedLootTier`, позиции и флаги парашюта у `PlayerPawn`, `TeamIndex`, `RecorderPlayerState`), читаются без ошибок: `BAD`-счётчик по ним 0.

## Классы с «Chest» в реплеях

| Класс | Что это | Считаем |
|---|---|---|
| `Tiered_Chest_Athena_C` | обычный сундук (тир 2 = редкий вариант) | да |
| `AlwaysSpawn_NormalChest_C` | обычный сундук | да |
| `AlwaysSpawn_RareChest_C` | редкий сундук | да |
| `Tiered_Chest_5_Child_C` | вариант обычного | да |
| `B_TieredChest_Relics_C`, `B_Delilah_TieredChest_Relics_Variant1_C` | реликвийный (динамический) | да, тип «другой» |
| `B_Athena_BuriedChest_Chest_C`, `B_Athena_BuriedChest_RelicFinder_C`, `B_SpritePosters_Booty_Chest*` | закопанные (выкапываются) | да, тип «другой» |
| `BGA_DelilahTreasureHuntAnimatedChest_C` | **Treasure Hunt**: появляется при открытии обычного сундука, поля `SourceContainer` (исходный сундук), `OpeningPawn`, `bSuccess`, `Tier` | исходный сундук получает тип Treasure Hunt; сам маркер не считается |
| `Tiered_Ammo_Athena_C`, `FoodBox_*`, `Barrel_*` | ящики с патронами, еда, бочки | нет |
| `BGA_LootCannon_StandardChest_Pirate_C`, `BGA_ExtractionPointGameplay_Chest_C`, `BP_ExtractionSite_Chest_Relic_C`, `BP_Chests_Cenote_C` | игровые механики, не обыскиваются | нет |
| `Prop_Athena_BuriedChest_*`, `BP_WaddleJam_*Chest*`, `Athena_Prop_*` | холм над закопанным сундуком, стены, пропсы | нет |

Таблица живёт в `src/PirateChest.Core/chest-classes.json`: новый класс после обновления добавляется без правки кода.
Неизвестный класс с «Chest», на котором случился обыск, всё равно засчитывается как «другой» и попадает в предупреждения.

## Что показывает реплей (это основа правил)

* **Открытие:** `bAlreadySearched` 0→1, `SearchAnimationCount` +1, `SearchingPawn` = открывший.
* **Поломка неоткрытого:** игра сама «обыскивает» сундук при поломке: `SearchAnimationCount` +1, `SearchingPawn` = **сломавший**, `bDestroyed`=1, `bInstantDeath`=1, без `bAlreadySearched`. Через ~5 с канал закрывается.
  Значит, кто сломал, видно прямо из реплея; эвристика нужна только для редких уничтожений без обыска (≈0,3 на матч).
* **Поломка открытого:** `bDestroyed`=1 без нового обыска.
* **Канал закрывается с причиной `Destroyed` и без поломки:** клиент выгружает актор (стриминг мира / уход из видимости). У динамических сундуков (закопанные, реликвийные) при возвращении новый NetGUID. Поэтому «сломан» = только `bDestroyed`, а ключ динамического сундука = класс + координаты появления (допуск 3 м).
* **Сброс игрой:** в определённые моменты матча (≈5, 8, 11, 14 мин) часть открытых сундуков получает `bAlreadySearched`=0 и `SearchAnimationCount`=0. Повторного открытия мной таких сундуков за 96 матчей — 0; по правилу 2 оно бы и не засчиталось.
* **Координаты:** статические сундуки своих координат в реплее не передают (`ReplicatedMovement` у них нет). Берём позицию обыскивающего в момент обыска (±2 м) и копим «атлас» по статическому имени (`…_UAID_…`, стабильно между матчами): после 96 матчей известно 1127 сундуков.

## Пример ленты событий (матч 04.11, трио)

| Время | Класс | Событие | Кто | Координаты обыскивающего, м |
|---|---|---|---|---|
| 01:00 | `Tiered_Chest_Athena_C` | открыт | тиммейт | 653, 264 |
| 01:08 | `AlwaysSpawn_NormalChest_C` | открыт | я | 754, 312 |
| 01:13 | `Tiered_Chest_Athena_C` | открыт | тиммейт | 645, 293 |
| 01:13 | `Tiered_Chest_Athena_C` | открыт | тиммейт | 742, 193 |
| 01:20 | `Tiered_Chest_Athena_C` | открыт | я | 758, 361 |
| 01:24 | `Tiered_Chest_Athena_C` | открыт | тиммейт | 645, 331 |
| 01:36 | `AlwaysSpawn_NormalChest_C` | открыт | тиммейт | 642, 297 |
| 01:50 | `AlwaysSpawn_NormalChest_C` | открыт | я | 743, 417 |
| 02:03 | `AlwaysSpawn_NormalChest_C` | открыт | я | 772, 512 |
| 02:18 | `AlwaysSpawn_NormalChest_C` | открыт | я | 713, 411 |
| 02:29 | `AlwaysSpawn_NormalChest_C` | открыт | тиммейт | 698, 225 |
| 02:44 | `Tiered_Chest_Athena_C` | открыт | тиммейт | 645, 256 |
| 02:50 | `Tiered_Chest_Athena_C` | сломан неоткрытым | тиммейт | 650, 254 |
| 03:06 | `Tiered_Chest_Athena_C` | сломан неоткрытым | тиммейт | 646, 283 |
| 03:11 | `Tiered_Chest_Athena_C` | сломан неоткрытым | тиммейт | 639, 286 |
| 03:41 | `Tiered_Chest_Athena_C` | открыт | тиммейт | 649, 290 |
| 03:54 | `AlwaysSpawn_NormalChest_C` | открыт | я | 567, 340 |
| 04:05 | `Tiered_Chest_Athena_C` | открыт | я | 542, 343 |
| 02:14 | `Tiered_Chest_Athena_C` | открыт | — | — |
| 04:55 | `Tiered_Chest_Athena_C` | сброс игрой | — | — |
| 04:55 | `Tiered_Chest_Athena_C` | сброс игрой | — | — |
| 04:55 | `Tiered_Chest_Athena_C` | сброс игрой | — | — |
| 04:55 | `Tiered_Chest_Athena_C` | сброс игрой | — | — |
| 05:36 | `AlwaysSpawn_NormalChest_C` | открыт | чужой | 124, 233 |

## Предварительный итог по 95 засчитанным матчам

«Мои» = **1799** (обычные 1477, редкие 139, другие 181, Treasure Hunt 2), из них сломано неоткрытыми 500, по эвристике 6.
«Мои + отряд» = 3930. Сломано уже открытыми (не считается) — 297. Без атрибуции — 30.
