# Scarica Hamlib per Windows (w64 o w32) e prepara la cartella src/IpTrxCtrl/hamlib.
# Uso: pwsh scripts/get_hamlib.ps1 -Arch w64|w32
# Se nella cartella del progetto c'e' gia' uno zip hamlib-<arch>-*.zip, usa quello.
param([ValidateSet('w64', 'w32')][string]$Arch = 'w64')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/IpTrxCtrl'
$dest = Join-Path $project 'hamlib'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('hamlib_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null

try {
    $local = Get-ChildItem $root -File -Filter "hamlib-$Arch-*.zip" | Sort-Object Name -Descending | Select-Object -First 1
    if ($local) {
        $zip = $local.FullName; $zipName = $local.Name
    }
    else {
        $headers = @{ 'User-Agent' = 'IP-TRX-CTRL-build' }
        if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)" }
        $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/Hamlib/Hamlib/releases/latest' -Headers $headers
        $asset = $rel.assets | Where-Object { $_.name -match "^hamlib-$Arch-[\d\.]+\.zip$" } | Select-Object -First 1
        if (-not $asset) { $asset = $rel.assets | Where-Object { $_.name -like "hamlib-$Arch-*.zip" } | Select-Object -First 1 }
        if (-not $asset) { throw "Nella release di Hamlib non c'e' uno zip hamlib-$Arch." }
        Write-Host "Scarico $($asset.name)..."
        $zip = Join-Path $tmp $asset.name; $zipName = $asset.name
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing
    }

    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    $bin = Get-ChildItem $tmp -Recurse -Directory -Filter 'bin' | Where-Object { Test-Path (Join-Path $_.FullName 'rigctld.exe') } | Select-Object -First 1
    if (-not $bin) { throw 'rigctld.exe non trovato nello zip di Hamlib.' }
    $hroot = Split-Path $bin.FullName -Parent

    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $bin.FullName 'rigctld.exe') $dest -Force
    if (Test-Path (Join-Path $bin.FullName 'rigctl.exe')) { Copy-Item (Join-Path $bin.FullName 'rigctl.exe') $dest -Force }
    Get-ChildItem $bin.FullName -File -Filter '*.dll' | Copy-Item -Destination $dest -Force

    # Licenze (obbligatorie per GPL/LGPL)
    $gpl = Get-ChildItem $hroot -File -Recurse | Where-Object { $_.Name -match '^COPYING(\.txt)?$' } | Select-Object -First 1
    $lgpl = Get-ChildItem $hroot -File -Recurse | Where-Object { $_.Name -match '^COPYING\.LIB(\.txt)?$' } | Select-Object -First 1
    if ($gpl) { Copy-Item $gpl.FullName (Join-Path $dest 'COPYING') -Force } else { Write-Warning 'Testo GPL (COPYING) non trovato.' }
    if ($lgpl) { Copy-Item $lgpl.FullName (Join-Path $dest 'COPYING.LIB') -Force } else { Write-Warning 'Testo LGPL (COPYING.LIB) non trovato.' }

    $ver = $zipName -replace "^hamlib-$Arch-", '' -replace '\.zip$', ''
    Set-Content -Path (Join-Path $dest 'VERSION.txt') -Value $ver -Encoding ASCII
    Write-Host "Hamlib $ver ($Arch) pronto in $dest"
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
