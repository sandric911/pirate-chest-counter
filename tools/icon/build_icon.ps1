# Renders the app icon (tools/icon/icon.html) with headless Edge into PNGs and packs them into app.ico.
#   build_icon.ps1 -Variant center            -> src/PirateChest.App/app.ico + wwwroot/icon.png
#   build_icon.ps1 -Variant center -Out <dir>  -> only the PNGs (for previews)
# Also renders the overlay's chest picture (src/PirateChest.App/overlay-chest.png) from the same wiki asset.
param([string]$Variant = 'center', [string]$Out = '', [switch]$OverlayChest)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\..\.."
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$tmp = Join-Path $env:TEMP "pcc-icon-$(Get-Random)"
New-Item -ItemType Directory -Force $tmp | Out-Null
$page = ([Uri](Join-Path $PSScriptRoot 'icon.html')).AbsoluteUri

function Shot([string]$url, [int]$size, [string]$file) {
  $ud = Join-Path $tmp "ud-$(Get-Random)"
  & cmd /c "`"$edge`" --headless=new --disable-gpu --hide-scrollbars --allow-file-access-from-files --default-background-color=00000000 --force-device-scale-factor=1 --user-data-dir=`"$ud`" --window-size=$size,$size --virtual-time-budget=3000 --screenshot=`"$file`" `"$url`" 2>nul" | Out-Null
  if (-not (Test-Path $file)) { throw "render failed: $url" }
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$dir = if ($Out) { $Out } else { $tmp }
New-Item -ItemType Directory -Force $dir | Out-Null
foreach ($s in $sizes) { Shot "$page`?v=$Variant&s=$s" $s (Join-Path $dir "icon-$Variant-$s.png") }

if (-not $Out) {
  # ICO: 256 px as PNG, smaller sizes as classic 32-bit DIBs (System.Drawing.Icon cannot read small PNG entries).
  Add-Type -AssemblyName System.Drawing
  function ToDib([string]$png) {
    $bmp = [System.Drawing.Bitmap]::FromFile($png)
    try {
      $w = $bmp.Width; $h = $bmp.Height
      $ms2 = New-Object IO.MemoryStream
      $b2 = New-Object IO.BinaryWriter($ms2)
      $b2.Write([uint32]40); $b2.Write([int32]$w); $b2.Write([int32]($h * 2)); $b2.Write([uint16]1); $b2.Write([uint16]32)
      $b2.Write([uint32]0); $b2.Write([uint32]0); $b2.Write([int32]0); $b2.Write([int32]0); $b2.Write([uint32]0); $b2.Write([uint32]0)
      for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) { $c = $bmp.GetPixel($x, $y); $b2.Write([byte]$c.B); $b2.Write([byte]$c.G); $b2.Write([byte]$c.R); $b2.Write([byte]$c.A) }
      }
      $maskRow = [int]([Math]::Ceiling($w / 32.0) * 4)
      $b2.Write((New-Object byte[] ($maskRow * $h))) # AND mask unused: alpha channel decides
      $b2.Flush()
      return , $ms2.ToArray() # keep the byte[] whole (PowerShell would unroll it)
    } finally { $bmp.Dispose() }
  }
  $ico = Join-Path $root 'src\PirateChest.App\app.ico'
  $pngs = $sizes | ForEach-Object {
    $f = Join-Path $dir "icon-$Variant-$_.png"
    if ($_ -ge 256) { , [IO.File]::ReadAllBytes($f) } else { , (ToDib $f) }
  }
  $ms = New-Object IO.MemoryStream
  $bw = New-Object IO.BinaryWriter($ms)
  $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
  }
  foreach ($p in $pngs) { $bw.Write($p) }
  $bw.Flush()
  [IO.File]::WriteAllBytes($ico, $ms.ToArray())
  Copy-Item (Join-Path $dir "icon-$Variant-256.png") (Join-Path $root 'src\PirateChest.App\wwwroot\icon.png') -Force
  "app.ico written ($($sizes -join ', ') px), wwwroot/icon.png updated"
}

if ($OverlayChest) {
  $html = Join-Path $tmp 'chest.html'
  $img = ([Uri](Join-Path $root 'src\PirateChest.App\wwwroot\img\chest.webp')).AbsoluteUri
  Set-Content -Path $html -Encoding utf8 -Value "<!doctype html><html><body style='margin:0;background:transparent'><img src='$img' style='display:block;width:160px;height:160px;margin:-16px;-webkit-mask-image:radial-gradient(circle at 50% 54%,#000 50%,transparent 70%)'></body></html>"
  Shot ([Uri]$html).AbsoluteUri 128 (Join-Path $root 'src\PirateChest.App\overlay-chest.png')
  "overlay-chest.png written"
}
