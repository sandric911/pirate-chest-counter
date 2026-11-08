#requires -Version 5.1
<#
  Pirate Chest Counter — установка одной командой (Windows 11 x64, без прав администратора).

  1. Проверяет Windows 11 x64.
  2. Скачивает последний релиз с GitHub (только github.com) и сверяет SHA256 с SHA256SUMS.txt из релиза.
  3. Запускает PirateChestCounter.exe --install: копия в %LOCALAPPDATA%\Programs\PirateChestCounter,
     автозапуск (HKCU\...\Run), ярлык в «Пуске», запуск и открытие статистики в браузере.
  4. Запускает --doctor и печатает отчёт.

  Запуск:  powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
#>
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = 'sandric911/pirate-chest-counter'

$os = [Environment]::OSVersion.Version
if ($os.Build -lt 22000 -or -not [Environment]::Is64BitOperatingSystem) {
    throw "Нужна Windows 11 x64 (сейчас: сборка $($os.Build), 64-бит: $([Environment]::Is64BitOperatingSystem))."
}
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$headers = @{ 'User-Agent' = 'pirate-chest-counter-installer'; 'Accept' = 'application/vnd.github+json' }
$rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers $headers
$exeAsset = $rel.assets | Where-Object name -eq 'PirateChestCounter.exe'
$sumAsset = $rel.assets | Where-Object name -eq 'SHA256SUMS.txt'
if (-not $exeAsset -or -not $sumAsset) { throw "В релизе $($rel.tag_name) нет PirateChestCounter.exe или SHA256SUMS.txt." }
foreach ($a in @($exeAsset, $sumAsset)) {
    if (([Uri]$a.browser_download_url).Host -ne 'github.com') { throw "Ссылка не на github.com: $($a.browser_download_url)" }
}

$dir = Join-Path $env:TEMP ("pcc-" + $rel.tag_name)
New-Item -ItemType Directory -Force $dir | Out-Null
$exe = Join-Path $dir 'PirateChestCounter.exe'
$sums = Join-Path $dir 'SHA256SUMS.txt'
Write-Host "Скачиваю $($rel.tag_name)…"
Invoke-WebRequest $exeAsset.browser_download_url -OutFile $exe -UseBasicParsing -Headers @{ 'User-Agent' = 'pirate-chest-counter-installer' }
Invoke-WebRequest $sumAsset.browser_download_url -OutFile $sums -UseBasicParsing -Headers @{ 'User-Agent' = 'pirate-chest-counter-installer' }

$expected = ((Get-Content $sums | Where-Object { $_ -match 'PirateChestCounter\.exe' }) -split '\s+')[0]
$actual = (Get-FileHash $exe -Algorithm SHA256).Hash
if (-not $expected -or $actual -ne $expected) {
    Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
    throw "SHA256 не совпадает (ожидалось $expected, получено $actual). Файл удалён и не запускался."
}
Unblock-File $exe
Write-Host "SHA256 совпадает: $actual"

& $exe --install | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Установка завершилась с кодом $LASTEXITCODE." }

$installed = Join-Path $env:LOCALAPPDATA 'Programs\PirateChestCounter\PirateChestCounter.exe'
& $installed --doctor | Out-Host
Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
