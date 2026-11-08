# pirate-chest-counter

**Если тебя попросили установить программу человеку — следуй [INSTALL_FOR_CLAUDE.md](INSTALL_FOR_CLAUDE.md).**

## Для разработки

- .NET 10 SDK. Сборка: `dotnet build`. Тесты: `dotnet test tests/PirateChest.Core.Tests`.
- Exe: `dotnet publish src/PirateChest.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish`.
- Релиз: тег `vX.Y.Z` → GitHub Actions (`.github/workflows/release.yml`) собирает exe и `SHA256SUMS.txt`.

Структура:

| Путь | Что |
|---|---|
| `src/PirateChest.Core` | правила подсчёта (без зависимостей от парсера); `chest-classes.json` — какие классы считаются сундуками |
| `src/PirateChest.ReplayReader` | адаптер к NuGet `FortniteReplayReader` 3.1.0: свойства сундуков, причина закрытия канала, позиции |
| `src/PirateChest.App` | exe: трей, наблюдатель за `Demos`, SQLite, веб-интерфейс на 127.0.0.1, автообновление, `--install/--uninstall/--doctor` |
| `tests/PirateChest.Core.Tests` | xUnit: синтетические ленты + анонимизированные ленты реальных матчей (`Regression/*.json.gz`) |
| `tools/PirateChest.Spike` | диагностика: прогон всех реплеев, отчёт по классам/полям, экспорт анонимной ленты |
| `docs/DECISIONS.md` | почему сделано так, а не иначе |
| `docs/SPIKE_REPORT.md` | что показал разбор реальных реплеев v43 |

Запреты: не читать память игры, не трогать файлы Fortnite (реплеи — только чтение с `FileShare.ReadWrite`),
сеть — только GitHub. Не коммитить реплеи, базу, ники и ID аккаунтов.

Переменные окружения для отладки: `PCC_DATA_DIR`, `PCC_DEMOS_DIR`, `PCC_INSTALL_DIR`, `PCC_NO_AUTOINSTALL=1`, `PCC_NO_BROWSER=1`.
