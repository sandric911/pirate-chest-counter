# Установка Pirate Chest Counter — инструкция для ИИ-агента

Ты ставишь программу человеку на его ПК с Windows 11. Работай в PowerShell. Права администратора не нужны
ни на одном шаге — если что-то их просит, ты делаешь что-то не так, остановись.

Программа: счётчик сундуков Fortnite по файлам повторов (replay). Она **не** читает память игры, не внедряется
в процесс, не рисует поверх игры и ходит в сеть только на GitHub за обновлениями.

Репозиторий: `https://github.com/sandric911/pirate-chest-counter`

## 1. Проверь систему

```powershell
$os = [Environment]::OSVersion.Version
"Windows build $($os.Build), 64-bit: $([Environment]::Is64BitOperatingSystem)"
if ($os.Build -lt 22000 -or -not [Environment]::Is64BitOperatingSystem) { 'НЕ ПОДХОДИТ: нужна Windows 11 x64' }
```

Если система не подходит — скажи человеку и остановись.

## 2. Скачай последний релиз и сверь SHA256

```powershell
$repo = 'sandric911/pirate-chest-counter'
$headers = @{ 'User-Agent' = 'pirate-chest-counter-installer'; 'Accept' = 'application/vnd.github+json' }
$rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers $headers
$dir = Join-Path $env:TEMP ("pcc-" + $rel.tag_name)
New-Item -ItemType Directory -Force $dir | Out-Null
$exe  = Join-Path $dir 'PirateChestCounter.exe'
$sums = Join-Path $dir 'SHA256SUMS.txt'
$ProgressPreference = 'SilentlyContinue'
Invoke-WebRequest ($rel.assets | Where-Object name -eq 'PirateChestCounter.exe').browser_download_url -OutFile $exe -UseBasicParsing
Invoke-WebRequest ($rel.assets | Where-Object name -eq 'SHA256SUMS.txt').browser_download_url -OutFile $sums -UseBasicParsing
$expected = ((Get-Content $sums | Where-Object { $_ -match 'PirateChestCounter\.exe' }) -split '\s+')[0]
$actual = (Get-FileHash $exe -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "SHA256 не совпадает: ожидалось $expected, получено $actual. Не запускай файл." }
Unblock-File $exe
"OK: $($rel.tag_name), SHA256 $actual"
```

Если хэш не совпал — **не запускай** файл, удали папку `$dir` и сообщи человеку.

## 3. Установи

```powershell
& $exe --install | Out-Host
```

`--install` копирует программу в `%LOCALAPPDATA%\Programs\PirateChestCounter`, включает автозапуск
(`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), добавляет программу в «Установленные приложения»,
создаёт ярлык в «Пуске», запускает её и открывает статистику в браузере (`http://127.0.0.1:47123/`, порт может
быть другим, если занят). В конце печатается `Готово.` и адрес.

`| Out-Host` обязателен: программа оконная, без конвейера PowerShell не дождётся её вывода.

## 4. Проверь и перескажи результат

```powershell
$installed = Join-Path $env:LOCALAPPDATA 'Programs\PirateChestCounter\PirateChestCounter.exe'
& $installed --doctor | Out-Host
```

Отчёт также сохраняется в `%LOCALAPPDATA%\PirateChestCounter\doctor.txt`. Строки `[OK]` — порядок,
`[!!]` — проблема и «Что сделать». Перескажи человеку коротко и по-русски: установлено ли, сколько найдено
реплеев, запущена ли программа, включён ли автозапуск, есть ли обновления.

Первый запуск сам досчитывает все старые реплеи мини-сезона (2–6 секунд на матч); прогресс виден на главной
странице статистики.

## 5. Если реплеев нет — объясни, как включить запись повторов

Если `--doctor` пишет, что папки `%LOCALAPPDATA%\FortniteGame\Saved\Demos` нет или она пустая:

1. В Fortnite открыть **Настройки** → вкладка **«Игра»** (шестерёнка) → раздел **«Повторы»**.
2. Включить **запись повторов** для Королевской битвы (Record Replays).
3. Сыграть матч. Реплей посчитается автоматически после конца матча (во время матча программа ничего не читает).

Считаются только матчи **Королевской битвы и Zero Build** мини-сезона Treasure Hunters (билды v43.x,
1 ноября – 5 декабря 2026). Остальные видны в «Истории» в списке «Пропущено» с причиной.

## 6. Удаление (если человек попросит)

```powershell
& (Join-Path $env:LOCALAPPDATA 'Programs\PirateChestCounter\PirateChestCounter.exe') --uninstall | Out-Host
```

Это выключает автозапуск, убирает ярлык и запись в «Установленных приложениях» и удаляет папку программы.
Статистика остаётся в `%LOCALAPPDATA%\PirateChestCounter`. Удалить вместе со статистикой:
`--uninstall --purge`. Реплеи Fortnite программа никогда не меняет и не удаляет.

## Что сказать человеку в конце

- где открыть статистику (значок сундука в трее → «Открыть статистику», или адрес из шага 3);
- что счётчик обновляется сам после каждого матча и показывает уведомление «+N сундуков (мои) · всего X»;
- что «Мои + отряд» — это «не меньше»: далёких тиммейтов реплей может не видеть;
- что Windows SmartScreen может предупредить о неподписанном файле, если человек запускает exe сам из браузера
  («Подробнее» → «Выполнить в любом случае»); при установке по этой инструкции предупреждения обычно нет;
- что это неофициальный фанатский инструмент, не связанный с Epic Games.
