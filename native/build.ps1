param([Parameter(Mandatory=$true)][string]$Zig)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & $Zig c++ -target x86_64-windows-gnu -std=c++17 -O2 -shared -static -fms-extensions -DWIN32_LEAN_AND_MEAN -DNOMINMAX -I native/vendor native/panel.cpp -ladvapi32 -o assets/AMD-DLSS-MU.addon64
    if ($LASTEXITCODE -ne 0) { throw 'Native panel build failed.' }
} finally { Pop-Location }
