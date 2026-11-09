# Изображения и шрифты Fortnite в программе

Всё перечисленное ниже принадлежит правообладателям и **не входит в MIT-лицензию** этого репозитория. Программа
бесплатная и некоммерческая; материалы Epic используются по правилам Epic для фанатского контента.

> Portions of the materials used are trademarks and/or copyrighted works of Epic Games, Inc. All rights reserved by Epic.
> This material is not official and is not endorsed by Epic.

## Картинки (`src/PirateChest.App/wwwroot/img/`)

| Файл | Что это | Источник |
|---|---|---|
| `chest.png` | обычный (пиратский) сундук | дал автор |
| `chest-rare.png` | редкий сундук | дал автор |
| `chest-sprite.png` | спрайтовый сундук | дал автор |
| `crown.webp` | корона Victory Royale | [Fortnite Wiki](https://fortnite.fandom.com/wiki/File:Accolade_Category_Victory_Royale_-_Icon_-_Fortnite.png) |
| `flag.webp` | флажок «First In Match» (лучший матч) | [Fortnite Wiki](https://fortnite.fandom.com/wiki/File:Accolade_Category_First_In_Match_-_Icon_-_Fortnite.png) |
| `chest-icon.webp` | значок сундука | [Fortnite Wiki](https://fortnite.fandom.com/wiki/File:Chest_-_Icon_-_Creative.png) |
| `squads.webp` | значок отряда | [Fortnite Wiki](https://fortnite.fandom.com/wiki/File:Squads_-_Icon_-_Fortnite.png) |
| `timer.webp` | значок таймера | [Fortnite Wiki](https://fortnite.fandom.com/wiki/File:Timer_-_Icon_-_Fortnite.png) |

Значок программы (`app.ico`, `wwwroot/icon.png`) и картинка сундука в оверлее (`overlay-chest.png`) собраны из
`chest.png` скриптом `tools/icon/build_icon.ps1`. Мелкие значки «обновить», «внимание», «метка», «поиск», «плюс»,
«минус», «весь остров» нарисованы заново.

## Карта (`assets/maptiles/`, `assets/map.png`)

| Что | Источник |
|---|---|
| вид «Официальная» | официальная карта сезона 2048×2048, [fortnite-api.com/images/map.png](https://fortnite-api.com/images/map.png) (то же, что [Crossbones Isle (Update v43.00) на Fortnite Wiki](https://fortnite.fandom.com/wiki/File:Crossbones_Isle_(Update_v43.00)_-_Island_-_Fortnite.webp)) |
| вид «Как в игре», `assets/map.png` | скриншот внутриигровой карты автора |
| ориентиры и их русские названия | [fortnite-api.com/v1/map](https://fortnite-api.com/v1/map) |

Всё это — Fortnite © Epic Games. Тайлы нарезает `tools/assets/build_map_tiles.py`.

## Библиотеки в интерфейсе

[Leaflet](https://leafletjs.com) 1.9.4 (`wwwroot/lib/leaflet/`) — BSD 2-Clause, текст лицензии рядом в `LICENSE`.

## Шрифты (`src/PirateChest.App/wwwroot/fonts/`)

Burbank Big Condensed Black и Burbank Small Bold (© House Industries / Tal Leming), версии с кириллицей.
Это коммерческие шрифты; поэтому репозиторий закрытый.
