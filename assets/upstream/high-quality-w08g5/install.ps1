# DLSSNR-AMD Windows installer (run by install.bat)
#
#   install.bat                      a window asks for the game's exe, then for the route
#   install.bat <game exe or its folder> [optiscaler|reshade-feeder|reshade-feeder-dx9|reshade-feeder-vulkan|remove|logs]
#               [-Dll <nvngx_dlssnr.dll or its zip>] [-Model <network>] [-RenoDX on|off] [-Lumenite on|off]
#
# Unreal Engine games: the folder is <project>\Binaries\Win64 (the *-Shipping.exe), not the top folder with the launcher.
#
# All four networks are installed and can be switched in game at any time (the drop-down in NR Control or on
# ReShade's Add-ons page, or Ctrl+F11 to cycle). The choice only sets the one a game starts with ([Network] Model):
#   -Model CC_SMALL_FP8                   the default network of NVIDIA's 310.9.16 (default)
#   -Model CC_SMALL_FP8_EXPERIMENTAL      this project's experimental network: CC_SMALL_FP8 made faster; its weights
#                                         are made from CC_SMALL_FP8's model at install; RX 9000 only
#   -Model CC_SILVER_AARDWOLD             the network of 310.8.0 (also preset 1 of 310.9.16)
#   -Model CC_SILVER_AARDWOLD_INT4_MIXED  CC_SILVER_AARDWOLD with part of the work in int4 and other lower precisions;
#                                         faster, the picture differs somewhat; RX 9000 only
# Without it, the installer asks once; Enter = CC_SMALL_FP8. The earlier choices still work: -Model new = CC_SMALL_FP8,
# -Model old = CC_SILVER_AARDWOLD, -Network aco-int4-mixed = CC_SILVER_AARDWOLD_INT4_MIXED.
#
# ACO Mode is not a setting (-AcoMode and -Network default / aco of older packages are accepted and do nothing): the
# network always runs as machine code made in advance by the ACO compiler, loaded by the AMD driver. When the driver
# cannot take it, NR Control and dlssnr-amd.log say why and an RX 9000 runs the network compiled by the AMD driver
# instead; RX 7000 and the Radeon 700M / 800M / 8000S graphics have no NR then.
#
# LumeniteFX (ReShade routes only, -Lumenite on|off [-LumeniteZip <zip>]; asked at install, Enter = off): motion vectors
# from LumeniteFX Kernel (by Afzaal (Kaidō), https://github.com/umar-afzaal/LumeniteFX, AGNYA licence). It is not in this
# package: the installer downloads the version this package was tested with (f80e98d) from its author's GitHub and keeps
# it in this package's lumenite\, so later installs do not download again; or -LumeniteZip gives a LumeniteFX zip you
# downloaded (Code > Download ZIP on its GitHub page). The files are copied unchanged. On ReShade's Add-ons page (Motion
# vectors) you can switch back to VORT.
#
# The processing of the picture before and after NR (the NR network itself is the same; asked at install, -RenoDX on|off):
#   -RenoDX on   the RenoDX style (default): processed the way RenoDX's DLSS5 add-on does it.
#   -RenoDX off  the wilsjo2 style: on the OptiScaler route the original processing of OptiScaler-NR (wilsjo2), ported;
#                on the ReShade routes OptiScaler-NR's (wilsjo2) settings and names ([DlssNr]), this route's own processing.
# Later NR Control (End in game) or ReShade's Add-ons page switches it and can stack several layers.
#
# The package carries no model: on the first install the user gives NVIDIA's nvngx_dlssnr.dll (310.9.16 or
# 310.8.0) or a zip that contains it, and model-tools\dlssnr_extract_model.exe makes the model files from it:
# from 310.9.16 dlssnr.bin (CC_SILVER_AARDWOLD, which 310.9.16 also holds) and dlssnr-small.bin (CC_SMALL_FP8), from
# 310.8.0 dlssnr.bin only. It only reads the weight data; the DLL is never loaded or run. The models are kept in
# the package's dlssnr-amd\, so later installs from this package need no DLL.
#
# Every file put into the game folder is listed in dlssnr-amd-install.txt; existing files it would
# overwrite are first moved to dlssnr-amd-backup\, and uninstall deletes ours and puts them back.
param([string]$Target = '', [string]$Route = '', [string]$Dll = '', [string]$Model = '', [string]$Network = '',
      [string]$AcoMode = '', [string]$RenoDX = '', [string]$Lumenite = '', [string]$LumeniteZip = '', [switch]$Pause)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$ManifestName = 'dlssnr-amd-install.txt'
$BackupName = 'dlssnr-amd-backup'

function Say([string]$text) { Write-Host $text }
# Re-run as administrator it is a separate window: pause before it closes so the result can be read.
function Finish([int]$code) { if ($Pause) { Read-Host 'Press Enter to close' | Out-Null }; exit $code }
function Fail([string]$text) { Write-Host $text -ForegroundColor Red; Finish 1 }

# ---- game folder -----------------------------------------------------------------------------
if (-not $Target) {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = "Choose the game's exe (the one that actually runs, not the launcher)"
    $dialog.Filter = 'Game program (*.exe)|*.exe'
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { Fail 'No game chosen.' }
    $Target = $dialog.FileName
}
$Target = $Target.Trim('"')
if (Test-Path -LiteralPath $Target -PathType Leaf) {
    $exe = (Resolve-Path -LiteralPath $Target).Path
    $game = Split-Path -Parent $exe
} elseif (Test-Path -LiteralPath $Target -PathType Container) {
    $game = (Resolve-Path -LiteralPath $Target).Path
    $exe = Get-ChildItem -LiteralPath $game -Filter *.exe | Select-Object -First 1 -ExpandProperty FullName
} else {
    Fail "Not found: $Target"
}
$manifest = Join-Path $game $ManifestName
$backup = Join-Path $game $BackupName

function Get-ExeBits([string]$path) {
    try {
        $b = [System.IO.File]::ReadAllBytes($path)
        $pe = [BitConverter]::ToInt32($b, 0x3C)
        switch ([BitConverter]::ToUInt16($b, $pe + 4)) { 0x8664 { return 64 } 0x14C { return 32 } }
    } catch { }
    return 0
}

# ---- choose the route ------------------------------------------------------------------------
if (-not $Route) {
    Say ''
    Say "Game folder: $game"
    Say 'Choose a route:'
    Say '  1) optiscaler             the game has a DLSS, FSR or XeSS option (DX11 / DX12, OptiScaler)'
    Say '  2) reshade-feeder         other DX10/11/12 games (ReShade)'
    Say '  3) reshade-feeder-dx9     old DX9 games (ReShade)'
    Say '  4) reshade-feeder-vulkan  Vulkan games (ReShade)'
    Say '  5) Uninstall'
    Say '  6) Collect logs           when something goes wrong; the zip goes to the desktop'
    $pick = Read-Host '1-6'
    switch ($pick) {
        '1' { $Route = 'optiscaler' } '2' { $Route = 'reshade-feeder' } '3' { $Route = 'reshade-feeder-dx9' }
        '4' { $Route = 'reshade-feeder-vulkan' } '5' { $Route = 'remove' } '6' { $Route = 'logs' }
        default { Fail 'Invalid choice.' }
    }
}
# The old route names were renamed; optiscaler-renodx means optiscaler with -RenoDX on.
$renamed = @{ 'reshade' = 'reshade-feeder'; 'dx9' = 'reshade-feeder-dx9'; 'vulkan' = 'reshade-feeder-vulkan' }
if ($renamed.ContainsKey($Route)) { Fail "Unknown route: $Route (this route is now called $($renamed[$Route]))" }
if ($Route -eq 'optiscaler-renodx') { $Route = 'optiscaler'; if (-not $RenoDX) { $RenoDX = 'on' } }
if ('optiscaler', 'reshade-feeder', 'reshade-feeder-dx9', 'reshade-feeder-vulkan', 'remove', 'logs' -notcontains $Route) {
    Fail "Unknown route: $Route"
}

# ---- the network a game starts with (asked after the model files are known) -----------------------------------
$Networks = @('CC_SMALL_FP8', 'CC_SMALL_FP8_EXPERIMENTAL', 'CC_SILVER_AARDWOLD', 'CC_SILVER_AARDWOLD_INT4_MIXED')
switch ($Model) { 'new' { $Model = 'CC_SMALL_FP8' } 'old' { $Model = 'CC_SILVER_AARDWOLD' } }
if ($Model -and $Networks -notcontains $Model) {
    Fail "Unknown network: $Model (CC_SMALL_FP8, CC_SMALL_FP8_EXPERIMENTAL, CC_SILVER_AARDWOLD or CC_SILVER_AARDWOLD_INT4_MIXED)"
}
if ($Network -and 'default', 'aco', 'aco-int4-mixed' -notcontains $Network) { Fail "Unknown network choice: $Network" }
if ($Network -eq 'aco-int4-mixed') {
    if ($Model -and $Model -ne 'CC_SILVER_AARDWOLD_INT4_MIXED') { Fail "-Network aco-int4-mixed and -Model $Model cannot be used together." }
    $Model = 'CC_SILVER_AARDWOLD_INT4_MIXED'
}
$haveInt4 = Test-Path -LiteralPath (Join-Path $here 'dlssnr-amd\int4') -PathType Container
$haveExperimental = Test-Path -LiteralPath (Join-Path $here 'experimental\dlssnr-small-experimental.delta') -PathType Leaf

# ---- the processing before and after NR --------------------------------------------------------
if ($Route -ne 'remove' -and $Route -ne 'logs' -and -not $RenoDX) {
    Say ''
    Say 'The processing before and after NR (the NR network itself is the same):'
    Say '  1) RenoDX style: processed the way RenoDX''s DLSS5 add-on does it (default)'
    if ($Route -eq 'optiscaler') {
        Say '  2) wilsjo2 style: the original processing of OptiScaler-NR (wilsjo2), ported'
        Say 'In game NR Control (End) switches it at any time and can stack several layers.'
    } else {
        Say '  2) wilsjo2 style: OptiScaler-NR''s (wilsjo2) settings and names ([DlssNr]), this route''s own processing'
        Say 'In game, ReShade''s Add-ons page can stack several layers, each with either style.'
    }
    $pick = Read-Host 'Which one? [1/2, Enter = 1]'
    $RenoDX = if ($pick -eq '2') { 'off' } else { 'on' }
}
if (-not $RenoDX) { $RenoDX = 'on' }
if ('on', 'off' -notcontains $RenoDX) { Fail "Unknown RenoDX choice: $RenoDX (on or off)" }

# ---- LumeniteFX (ReShade routes) -------------------------------------------------------------
if ($LumeniteZip -and -not $Lumenite) { $Lumenite = 'on' }
if ($Route -like 'reshade-feeder*' -and -not $Lumenite) {
    Say ''
    Say 'LumeniteFX (optional): motion vectors from LumeniteFX Kernel (by Afzaal (Kaidō), https://github.com/umar-afzaal/LumeniteFX).'
    Say '  LumeniteFX is not in this package; with y it is downloaded from its author''s GitHub (needs the internet) and installed unchanged.'
    Say '  y: install and use LumeniteFX. On ReShade''s Add-ons page (Motion vectors) you can switch back to VORT.'
    Say '  N: do not install it; the VORT in this package, as before.'
    $pick = Read-Host 'Install LumeniteFX? [y/N]'
    $Lumenite = if ($pick -eq 'y' -or $pick -eq 'Y') { 'on' } else { 'off' }
}
if (-not $Lumenite) { $Lumenite = 'off' }
if ('on', 'off' -notcontains $Lumenite) { Fail "Unknown LumeniteFX choice: $Lumenite (on or off)" }
if ($Lumenite -eq 'on' -and $Route -eq 'optiscaler') {
    Fail 'LumeniteFX is for the ReShade routes only (the OptiScaler route uses the game''s own motion vectors).'
}

# ---- games under Program Files need administrator rights -------------------------------------
if ($Route -ne 'logs') {
    $probe = Join-Path $game ('dlssnr-amd-write-test-' + [guid]::NewGuid().ToString('N'))
    try { [System.IO.File]::WriteAllText($probe, ''); Remove-Item -LiteralPath $probe -Force }
    catch {
        $me = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        if ($me.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Fail "Cannot write to $game" }
        Say 'This folder needs administrator rights, asking for them...'
        # the chosen exe, not its folder: from a folder the first exe would be taken again (another bitness)
        $again = if ($exe) { $exe } else { $game }
        $elevated = "-NoProfile -ExecutionPolicy Bypass -STA -File `"$PSCommandPath`" -Target `"$again`" -Route $Route -RenoDX $RenoDX -Pause"
        if ($Model) { $elevated += " -Model $Model" }
        $elevated += " -Lumenite $Lumenite"
        if ($LumeniteZip) { $elevated += " -LumeniteZip `"$LumeniteZip`"" }
        if ($Dll) { $elevated += " -Dll `"$Dll`"" }
        $p = Start-Process powershell -Verb RunAs -ArgumentList $elevated -PassThru -Wait
        exit $p.ExitCode
    }
}

# ---- installation record ---------------------------------------------------------------------
function Record([string]$line) { Add-Content -LiteralPath $manifest -Value $line -Encoding UTF8 }

# A target that exists and was not installed by us: move it to the backup folder, put back on uninstall.
function Save-Existing([string]$rel) {
    $dst = Join-Path $game $rel
    if (-not (Test-Path -LiteralPath $dst)) { return }
    $keep = Join-Path $backup $rel
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $keep) | Out-Null
    Move-Item -LiteralPath $dst -Destination $keep -Force
    Record "B $rel"
}
function Put-File([string]$src, [string]$rel) {
    Save-Existing $rel
    Copy-Item -LiteralPath $src -Destination (Join-Path $game $rel) -Force
    Unblock-File -LiteralPath (Join-Path $game $rel) -ErrorAction SilentlyContinue
    Record "F $rel"
}
function Put-Tree([string]$src, [string]$rel) {
    Save-Existing $rel
    Copy-Item -LiteralPath $src -Destination (Join-Path $game $rel) -Recurse -Force
    Get-ChildItem -LiteralPath (Join-Path $game $rel) -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
    Record "D $rel"
}

function Remove-Installed {
    if (-not (Test-Path -LiteralPath $manifest)) { Say 'No installation record found, nothing to uninstall.'; return }
    $lines = @(Get-Content -LiteralPath $manifest -Encoding UTF8)
    [array]::Reverse($lines)
    foreach ($line in $lines) {
        $line = $line.TrimStart([char]0xFEFF)
        if ($line.Length -lt 3) { continue }
        $kind = $line.Substring(0, 1); $rel = $line.Substring(2)
        if ($rel -match '(^|[\\/])\.\.([\\/]|$)' -or [System.IO.Path]::IsPathRooted($rel)) { Say "Skipping suspicious entry: $rel"; continue }
        $path = Join-Path $game $rel
        switch ($kind) {
            { 'F', 'D', 'L' -contains $_ } { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force } }
            'B' {
                $keep = Join-Path $backup $rel
                if (Test-Path -LiteralPath $keep) {
                    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
                    Move-Item -LiteralPath $keep -Destination $path -Force
                }
            }
        }
    }
    if (Test-Path -LiteralPath $manifest) { Remove-Item -LiteralPath $manifest -Force }
    if (Test-Path -LiteralPath $backup) {
        if (@(Get-ChildItem -LiteralPath $backup -Recurse -File).Count -eq 0) { Remove-Item -LiteralPath $backup -Recurse -Force }
        else { Say "Note: files in $backup were not put back; please check them by hand." }
    }
    Say "Uninstalled from $game."
}

# Files the game creates while running; removed on uninstall as well.
function Record-Logs([string[]]$names) { foreach ($n in $names) { Record "L $n" } }

# ---- collect logs ----------------------------------------------------------------------------
if ($Route -eq 'logs') {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $tmp = Join-Path $env:TEMP "dlssnr-amd-logs-$stamp"
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $patterns = 'dlssnr-amd.log', 'dlssnr-amd.ini', $ManifestName, 'OptiScaler.log', 'OptiScaler.ini',
                'ReShade.log', 'ReShade.ini', 'ReShadePreset.ini', '*_dxgi.log', '*_d3d11.log', '*_d3d9.log',
                '*_d3d12.log', 'vkd3d-proton.log', 'dlssnr-amd-crash.dmp'
    foreach ($p in $patterns) {
        Get-ChildItem -LiteralPath $game -Filter $p -File -ErrorAction SilentlyContinue | Copy-Item -Destination $tmp
    }
    $info = @("game: $game", "exe: $exe", "exe bits: $(Get-ExeBits $exe)", "package: $here", '')
    $info += Get-CimInstance Win32_VideoController | ForEach-Object { "GPU: $($_.Name) driver $($_.DriverVersion) ($($_.DriverDate))" }
    $info += Get-CimInstance Win32_OperatingSystem | ForEach-Object { "OS: $($_.Caption) $($_.Version)" }
    $info += ''
    $info += Get-ChildItem -LiteralPath $game | ForEach-Object { '{0,12} {1}' -f $_.Length, $_.Name }
    $info | Set-Content -LiteralPath (Join-Path $tmp 'system.txt') -Encoding UTF8

    # The last two days of graphics driver events (driver stopped responding and recovered = 4101) and game crashes.
    $since = (Get-Date).AddDays(-2)
    $events = @()
    try {
        # Every error and warning in the System log (a driver reset is not always logged as 4101).
        $events += Get-WinEvent -FilterHashtable @{ LogName = 'System'; StartTime = $since; Level = 1, 2, 3 } -MaxEvents 300 -ErrorAction Stop
    } catch { }
    try {
        $events += Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $since; Id = 1000, 1001, 1002 } -ErrorAction Stop |
            Where-Object { $_.Message -match [regex]::Escape([System.IO.Path]::GetFileName($exe)) -or $_.Message -match 'LiveKernelEvent' }
    } catch { }
    $events | Sort-Object TimeCreated | ForEach-Object {
        "[$($_.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss'))] $($_.LogName) $($_.ProviderName) id=$($_.Id)`r`n$($_.Message)`r`n"
    } | Set-Content -LiteralPath (Join-Path $tmp 'events.txt') -Encoding UTF8

    # File permissions: for finding out why ReShade says it cannot save its settings.
    $perm = @("user: $([Security.Principal.WindowsIdentity]::GetCurrent().Name)",
              "admin: $(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))", '')
    foreach ($name in '.', 'ReShade.ini', 'ReShadePreset.ini', 'dlssnr-amd.ini') {
        $path = Join-Path $game $name
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $item = Get-Item -LiteralPath $path -Force
        $perm += "== $name  attributes: $($item.Attributes)"
        $perm += (& icacls.exe $path 2>&1 | Out-String)
    }
    $perm | Set-Content -LiteralPath (Join-Path $tmp 'permissions.txt') -Encoding UTF8
    $zip = Join-Path ([Environment]::GetFolderPath('Desktop')) "dlssnr-amd-logs-$stamp.zip"
    Compress-Archive -Path (Join-Path $tmp '*') -DestinationPath $zip -Force
    Remove-Item -LiteralPath $tmp -Recurse -Force
    Say "Logs packed: $zip"
    Finish 0
}

if ($Route -eq 'remove') { Remove-Installed; Finish 0 }

# ---- LumeniteFX Kernel (the ReShade routes' optional motion vectors) -------------------------------------------------
# Nothing of LumeniteFX is in this package (its AGNYA licence allows propagation only through its author's links), and
# nothing of it is changed: the files are copied as they are in the zip - Shaders/lumenite_Kernel.fx, the
# Shaders/include/*.fxh it uses, Textures/*, and its licence and notice; its other effects are left out, since ReShade
# compiles every effect it finds. The check is of the interface, not of the version: lumenite_Kernel.fx with technique
# Lumenite_Kernel, every file it includes, and the flow textures DLSS5_Feed.fx reads declared as DLSS5_Feed.fx declares
# them. The add-on checks the rest in the game.
$LumeniteCommit = 'f80e98d716ad72e12af6f7baa727bcfeb4be5094'   # the version this package was tested with
function Strip-Comments([string]$text) {
    $text = [regex]::Replace($text, '/\*.*?\*/', ' ', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    return [regex]::Replace($text, '//[^\n]*', '')
}
function Normal-Text([string]$text) { return [regex]::Replace($text, '\s+', '') }
# A zip's comment (GitHub writes the commit there), from its end-of-central-directory record.
function Get-ZipComment([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    for ($i = $bytes.Length - 22; $i -ge [Math]::Max(0, $bytes.Length - 65557); $i--) {
        if ($bytes[$i] -eq 0x50 -and $bytes[$i + 1] -eq 0x4B -and $bytes[$i + 2] -eq 0x05 -and $bytes[$i + 3] -eq 0x06) {
            $n = [BitConverter]::ToUInt16($bytes, $i + 20)
            if ($i + 22 + $n -le $bytes.Length) { return [System.Text.Encoding]::ASCII.GetString($bytes, $i + 22, $n).Trim() }
        }
    }
    return ''
}
# The zip's files below the repository's top folder: @{ 'Shaders/lumenite_Kernel.fx' = <entry>; ... }. Throws with the
# reason when the zip cannot be used.
function Read-Lumenite([string]$zipPath, [string]$feedPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    try { $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath) }
    catch { throw "$zipPath is not a zip that can be opened: $($_.Exception.Message)" }
    $found = @{}
    foreach ($e in $zip.Entries) {
        if ($e.FullName.EndsWith('/') -or $e.FullName.EndsWith('\')) { continue }
        $name = $e.FullName.Replace('\', '/')
        $m = [regex]::Match($name, '(?:^|/)((?:Shaders|Textures)/.+|LICENSE\.md|NOTICE)$')
        if ($m.Success -and -not $name.Substring(0, $m.Groups[1].Index).TrimEnd('/').Contains('/') -and
            -not $found.ContainsKey($m.Groups[1].Value)) { $found[$m.Groups[1].Value] = $e }
    }
    $read = { param($entry) $s = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8); try { $s.ReadToEnd() } finally { $s.Close() } }
    try {
        if (-not $found.ContainsKey('Shaders/lumenite_Kernel.fx')) { throw 'the zip has no Shaders/lumenite_Kernel.fx (not a LumeniteFX zip?)' }
        $kernel = Strip-Comments (& $read $found['Shaders/lumenite_Kernel.fx'])
        if ($kernel -cnotmatch '\btechnique\s+Lumenite_Kernel\b') {
            throw 'lumenite_Kernel.fx has no technique Lumenite_Kernel (this LumeniteFX version does not work with this package)'
        }
        foreach ($inc in [regex]::Matches($kernel, '#\s*include\s+"([^"]+)"')) {
            $name = $inc.Groups[1].Value.TrimStart('.', '/')
            if ('ReShade.fxh', 'ReShadeUI.fxh' -contains $name) { continue }   # ReShade's own headers, in the package
            if (-not $found.ContainsKey("Shaders/$name")) { throw "$name, included by lumenite_Kernel.fx, is not in the zip" }
        }
        # The textures DLSS5_Feed.fx re-declares for provider 3 (LumeniteFX Kernel) must be declared the same way.
        $feed = [System.IO.File]::ReadAllText($feedPath)
        $part = [regex]::Match($feed, '#elif\s+DLSS5_MV_PROVIDER\s*==\s*3\b(.*?)#elif', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if (-not $part.Success) { throw "$feedPath has no DLSS5_MV_PROVIDER == 3 part" }
        $wanted = @([regex]::Matches((Strip-Comments $part.Groups[1].Value), 'texture2D\s+\w+\s*\{[^}]*\}\s*;') | ForEach-Object { $_.Value })
        if ($wanted.Count -eq 0 -or -not $part.Groups[1].Value.Contains('namespace Kernel')) { throw "the LumeniteFX part of $feedPath is not as expected" }
        if ($kernel -cnotmatch '\bnamespace\s+Kernel\b') {
            throw 'lumenite_Kernel.fx has no namespace Kernel (this LumeniteFX version does not work with this package)'
        }
        $flat = Normal-Text $kernel
        foreach ($decl in $wanted) {
            if (-not $flat.Contains((Normal-Text $decl))) {
                throw "$(($decl -split '\s+')[1]) in lumenite_Kernel.fx differs from what DLSS5_Feed.fx reads (this LumeniteFX version does not work with this package)"
            }
        }
    } catch { $zip.Dispose(); throw }
    return @{ zip = $zip; found = $found }
}
# Into <dest> (a reshade-shaders folder): what lumenite_Kernel.fx needs, unchanged.
function Install-Lumenite($lum, [string]$dest) {
    $picked = @{ 'Shaders/lumenite_Kernel.fx' = 'Shaders\lumenite_Kernel.fx' }
    foreach ($name in $lum.found.Keys) {
        $parts = $name.Split('/')
        if ($name.StartsWith('Shaders/include/') -and $name.EndsWith('.fxh') -and $parts.Count -eq 3) { $picked[$name] = "Shaders\include\$($parts[2])" }
        elseif ($name.StartsWith('Textures/') -and $parts.Count -eq 2) { $picked[$name] = "Textures\$($parts[1])" }
    }
    if ($lum.found.ContainsKey('LICENSE.md')) { $picked['LICENSE.md'] = 'Shaders\LumeniteFX-LICENSE.md' }
    if ($lum.found.ContainsKey('NOTICE')) { $picked['NOTICE'] = 'Shaders\LumeniteFX-NOTICE.txt' }
    try {
        foreach ($name in $picked.Keys) {
            $out = Join-Path $dest $picked[$name]
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out) | Out-Null
            $in = $lum.found[$name].Open()
            try { $f = [System.IO.File]::Create($out); try { $in.CopyTo($f) } finally { $f.Close() } } finally { $in.Close() }
        }
    } finally { $lum.zip.Dispose() }
    return $picked.Count
}

# ---- install ---------------------------------------------------------------------------------
$bits = Get-ExeBits $exe
if ($bits -eq 32) { Fail "This is a 32-bit game ($exe); this package supports 64-bit games only." }
# ---- model -----------------------------------------------------------------------------------
# The package's models, or ones extracted now from the user's nvngx_dlssnr.dll; checked by SHA256.
# dlssnr.bin = CC_SILVER_AARDWOLD (310.8.0, also preset 1 of 310.9.16), dlssnr-small.bin = CC_SMALL_FP8 (310.9.16's default).
$ModelSha = '2B41C888CF4155B8958C665BA64018AB0BD25C85FC71A2B6DB86D0D04D1F7FBD'
$SmallSha = '7E966E2639DBEC6596AA0BAC3B8761137D2C40CA8CB4ACA30DF7CA10043C6E33'
function Test-Sha([string]$path, [string]$sha) {
    (Test-Path -LiteralPath $path -PathType Leaf) -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $sha
}
$modelPath = Join-Path $here 'dlssnr-amd\dlssnr.bin'
$smallPath = Join-Path $here 'dlssnr-amd\dlssnr-small.bin'
$modelFrom = $modelPath; $smallFrom = $smallPath
if (($Dll -and -not ((Test-Sha $modelPath $ModelSha) -and (Test-Sha $smallPath $SmallSha))) -or -not (Test-Path -LiteralPath $modelPath)) {
    if (-not $Dll) {
        Say ''
        Say 'The package has no model. The first install needs NVIDIA''s nvngx_dlssnr.dll (310.9.16 - both models - or 310.8.0), or a zip that contains it.'
        Say 'Only its weight data is read; it is never loaded or run. The models are kept in this package, so later installs do not ask again.'
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = New-Object System.Windows.Forms.OpenFileDialog
        $dialog.Title = 'Choose nvngx_dlssnr.dll (310.9.16 or 310.8.0) or a zip that contains it'
        $dialog.Filter = 'nvngx_dlssnr.dll or zip (*.dll;*.zip)|*.dll;*.zip'
        if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { Fail 'No nvngx_dlssnr.dll chosen; cannot install.' }
        $Dll = $dialog.FileName
    }
    $Dll = $Dll.Trim('"')
    if (-not (Test-Path -LiteralPath $Dll -PathType Leaf)) { Fail "Not found: $Dll" }
    $work = Join-Path $env:TEMP ("dlssnr-amd-model-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        $src = (Resolve-Path -LiteralPath $Dll).Path
        if ([System.IO.Path]::GetExtension($src) -ieq '.zip') {
            Say 'Unpacking the zip ...'
            Expand-Archive -LiteralPath $src -DestinationPath (Join-Path $work 'zip') -Force
            $found = @(Get-ChildItem -LiteralPath (Join-Path $work 'zip') -Recurse -File -Filter 'nvngx_dlssnr.dll')
            if ($found.Count -ne 1) { Fail "The zip should hold exactly one nvngx_dlssnr.dll; found $($found.Count)." }
            $src = $found[0].FullName
        }
        Say 'Extracting the model ...'
        $out = Join-Path $work 'out'
        New-Item -ItemType Directory -Path $out | Out-Null
        & (Join-Path $here 'model-tools\dlssnr_extract_model.exe') $src $out
        $tmpModel = Join-Path $out 'dlssnr.bin'; $tmpSmall = Join-Path $out 'dlssnr-small.bin'
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $tmpModel)) { Fail 'Extracting the model failed (see above).' }
        if (-not (Test-Sha $tmpModel $ModelSha)) { Fail 'The extracted model has the wrong checksum; nothing was installed.' }
        $pairs = @(,@($tmpModel, $modelPath))
        if (Test-Path -LiteralPath $tmpSmall) {
            if (-not (Test-Sha $tmpSmall $SmallSha)) { Fail 'The extracted CC_SMALL_FP8 model has the wrong checksum; nothing was installed.' }
            $pairs += ,@($tmpSmall, $smallPath)
        }
        try {
            foreach ($p in $pairs) { Copy-Item -LiteralPath $p[0] -Destination $p[1] -Force }
            Say "The model is kept in $(Join-Path $here 'dlssnr-amd'); later installs from this package need no DLL."
        } catch {
            # The package folder is not writable: install from temporary copies; the next install asks again.
            foreach ($p in $pairs) { Remove-Item -LiteralPath $p[1] -Force -ErrorAction SilentlyContinue }
            $modelFrom = Join-Path $env:TEMP 'dlssnr-amd-model.bin'
            Copy-Item -LiteralPath $tmpModel -Destination $modelFrom -Force
            if (Test-Path -LiteralPath $tmpSmall) {
                $smallFrom = Join-Path $env:TEMP 'dlssnr-amd-model-small.bin'
                Copy-Item -LiteralPath $tmpSmall -Destination $smallFrom -Force
            }
            Say 'Note: the model could not be kept in the package folder (not writable?); the next install needs the DLL again.'
        }
    } finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
if (-not (Test-Sha $modelFrom $ModelSha)) {
    Fail "$modelFrom is damaged or not this version's model. Delete it and install again with nvngx_dlssnr.dll."
}
if ((Test-Path -LiteralPath $smallPath) -and -not (Test-Sha $smallPath $SmallSha)) {
    Fail "$smallPath is damaged or not this version's model. Delete it or install again with nvngx_dlssnr.dll."
}
$haveSmall = Test-Sha $smallFrom $SmallSha
if (-not $haveSmall) {
    Say 'Note: the package has no model for CC_SMALL_FP8 (dlssnr-small.bin, in the 310.9.16 nvngx_dlssnr.dll); it is greyed out in game.'
    Say 'To add it, install again with -Dll pointing at the 310.9.16 nvngx_dlssnr.dll or its zip.'
}
# The network a game starts with: all four are installed, this only sets [Network] Model in dlssnr-amd.ini.
if (-not $Model) {
    $default = if ($haveSmall) { 1 } else { 3 }
    Say ''
    Say 'All four networks are installed and can be switched in game at any time (the drop-down in NR Control or on'
    Say 'ReShade''s Add-ons page, or Ctrl+F11 to cycle). Choose only the one a game starts with:'
    if ($haveSmall) { Say '  1) CC_SMALL_FP8                    the default network of 310.9.16 (default)' }
    else { Say '  1) CC_SMALL_FP8                    not available: needs the 310.9.16 nvngx_dlssnr.dll (install again with -Dll)' }
    if (-not $haveSmall) { Say '  2) CC_SMALL_FP8_EXPERIMENTAL       not available: needs the 310.9.16 nvngx_dlssnr.dll (install again with -Dll)' }
    elseif (-not $haveExperimental) { Say '  2) CC_SMALL_FP8_EXPERIMENTAL       not available: this package does not have it' }
    else { Say '  2) CC_SMALL_FP8_EXPERIMENTAL       this project''s experimental network: 1) made faster (RX 9000)' }
    Say '  3) CC_SILVER_AARDWOLD              the network of 310.8.0 (also preset 1 of 310.9.16)'
    if ($haveInt4) { Say '  4) CC_SILVER_AARDWOLD_INT4_MIXED   3) with part of the work in int4; faster, the picture differs somewhat (RX 9000)' }
    else { Say '  4) CC_SILVER_AARDWOLD_INT4_MIXED   not available: this package has no int4 mixed' }
    Say 'RX 7000 and the Radeon 700M / 800M / 8000S graphics run 1) and 3).'
    Say 'Later: [Network] Model in dlssnr-amd.ini.'
    $pick = Read-Host "Start with which one? [1/2/3/4, Enter = $default]"
    if ($pick -eq '') { $pick = "$default" }
    switch ($pick) {
        '1' { $Model = 'CC_SMALL_FP8' } '2' { $Model = 'CC_SMALL_FP8_EXPERIMENTAL' } '3' { $Model = 'CC_SILVER_AARDWOLD' }
        '4' { $Model = 'CC_SILVER_AARDWOLD_INT4_MIXED' }
        default { Say "Invalid choice, using $default."; $Model = $Networks[$default - 1] }
    }
}
if (($Model -eq 'CC_SMALL_FP8' -or $Model -eq 'CC_SMALL_FP8_EXPERIMENTAL') -and -not $haveSmall) {
    Say "$Model is not available (no CC_SMALL_FP8 model); games start with CC_SILVER_AARDWOLD."; $Model = 'CC_SILVER_AARDWOLD'
}
if ($Model -eq 'CC_SILVER_AARDWOLD_INT4_MIXED' -and -not $haveInt4) {
    Say 'This package has no int4 mixed; games start with CC_SILVER_AARDWOLD.'; $Model = 'CC_SILVER_AARDWOLD'
}
# int4 mixed's weights: made from this model with the package's data, checked against the tested result. If that
# fails, the other two networks are installed and int4 mixed is greyed out in game (with the reason).
$int4ModelFrom = $null
$int4Data = Join-Path $here 'dlssnr-amd\int4'
if ($haveInt4) {
    $generator = Join-Path $here 'model-tools\dlssnr-int4-weights.exe'
    $hashFile = Join-Path $here 'model-tools\dlssnr-int4.sha256'
    if (-not (Test-Path -LiteralPath $generator) -or -not (Test-Path -LiteralPath $hashFile)) {
        Fail 'The int4 mixed files of the package are incomplete; unpack the package again.'
    }
    $int4Sha = ((Get-Content -LiteralPath $hashFile -Raw).Trim() -split '\s+')[0]
    $int4ModelFrom = Join-Path ([System.IO.Path]::GetTempPath()) ("dlssnr-int4-$([guid]::NewGuid().ToString('N')).bin")
    Say 'Making the CC_SILVER_AARDWOLD_INT4_MIXED weights from the model ...'
    & $generator $modelFrom $int4Data $int4ModelFrom
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $int4ModelFrom) -or
        (Get-FileHash -LiteralPath $int4ModelFrom -Algorithm SHA256).Hash -ne $int4Sha) {
        Remove-Item -LiteralPath $int4ModelFrom -Force -ErrorAction SilentlyContinue
        $int4ModelFrom = $null
        Say 'Note: making or checking the int4 mixed weights failed; it is not available in game. The other two networks are installed.'
        if ($Model -eq 'CC_SILVER_AARDWOLD_INT4_MIXED') { Say 'Games start with CC_SILVER_AARDWOLD.'; $Model = 'CC_SILVER_AARDWOLD' }
    }
}
# CC_SMALL_FP8_EXPERIMENTAL's weights are not in the package: made here from CC_SMALL_FP8's model and the package's
# difference file (each changed entry XOR the small model's, so it holds none of NVIDIA's weights), installed only when
# their SHA-256 matches. When that fails the other networks are installed as usual and it is greyed out in game.
function New-ExperimentalWeights([string]$small, [string]$delta, [string]$out) {
    $pack = [System.IO.File]::ReadAllBytes($small)
    if ([System.Text.Encoding]::ASCII.GetString($pack, 0, 8) -ne 'NRMODEL1') { throw "$small is not a model pack" }
    $count = [BitConverter]::ToUInt32($pack, 8)
    $index = @{}; $at = 16
    for ($k = 0; $k -lt $count; $k++) {
        $n = [BitConverter]::ToUInt32($pack, $at)
        $name = [System.Text.Encoding]::UTF8.GetString($pack, $at + 4, $n)
        $index[$name] = @([BitConverter]::ToUInt64($pack, $at + 4 + $n), [BitConverter]::ToUInt64($pack, $at + 12 + $n))
        $at += 4 + $n + 16
    }
    $d = [System.IO.File]::ReadAllBytes($delta)
    if ([System.Text.Encoding]::ASCII.GetString($d, 0, 8) -ne 'NRXDIFF1') { throw "$delta is not a weights difference" }
    $count = [BitConverter]::ToUInt32($d, 8); $at = 12
    for ($k = 0; $k -lt $count; $k++) {
        $n = [BitConverter]::ToUInt32($d, $at)
        $name = [System.Text.Encoding]::UTF8.GetString($d, $at + 4, $n)
        $size = [BitConverter]::ToUInt64($d, $at + 4 + $n)
        $at += 4 + $n + 8
        if (-not $index.ContainsKey($name) -or $index[$name][1] -ne $size) { throw "${name}: not in $small with $size bytes" }
        $off = [int64]$index[$name][0]
        for ($i = 0; $i -lt $size; $i++) { $pack[$off + $i] = $pack[$off + $i] -bxor $d[$at + $i] }
        $at += [int64]$size
    }
    if ($at -ne $d.Length) { throw "${delta}: $($d.Length - $at) bytes left over" }
    [System.IO.File]::WriteAllBytes($out, $pack)
}
$expModelFrom = $null
if ($haveExperimental -and $haveSmall) {
    Say 'Making CC_SMALL_FP8_EXPERIMENTAL''s weights ...'
    $expSha = ((Get-Content -LiteralPath (Join-Path $here 'experimental\dlssnr-small-experimental.bin.sha256') -Raw).Trim() -split '\s+')[0]
    $expModelFrom = Join-Path ([System.IO.Path]::GetTempPath()) ("dlssnr-small-experimental-$([guid]::NewGuid().ToString('N')).bin")
    try {
        New-ExperimentalWeights $smallFrom (Join-Path $here 'experimental\dlssnr-small-experimental.delta') $expModelFrom
        if ((Get-FileHash -LiteralPath $expModelFrom -Algorithm SHA256).Hash -ne $expSha) { throw 'the weights have the wrong checksum (model or package incomplete?)' }
    } catch {
        Remove-Item -LiteralPath $expModelFrom -Force -ErrorAction SilentlyContinue
        $expModelFrom = $null
        Say "Note: CC_SMALL_FP8_EXPERIMENTAL's weights could not be made ($($_.Exception.Message)); it is not available in game. The other networks are installed as usual."
    }
}
if (-not $expModelFrom -and $Model -eq 'CC_SMALL_FP8_EXPERIMENTAL') { Say 'Games start with CC_SMALL_FP8 instead.'; $Model = 'CC_SMALL_FP8' }

# LumeniteFX is fetched and its interface checked before anything is installed: if anything is wrong, nothing is.
$lumeniteFx = $null
if ($Lumenite -eq 'on') {
    $feedFx = Join-Path $here 'reshade\reshade-shaders\Shaders\DLSS5_Feed.fx'
    if ($LumeniteZip) {
        $LumeniteZip = $LumeniteZip.Trim('"')
        if (-not (Test-Path -LiteralPath $LumeniteZip -PathType Leaf)) { Fail "Not found: $LumeniteZip" }
        $lumeniteSrc = (Resolve-Path -LiteralPath $LumeniteZip).Path
    } else {
        $lumeniteSrc = Join-Path $here "lumenite\LumeniteFX-$LumeniteCommit.zip"
        if (-not (Test-Path -LiteralPath $lumeniteSrc -PathType Leaf)) {
            $url = "https://github.com/umar-afzaal/LumeniteFX/archive/$LumeniteCommit.zip"
            Say "Downloading LumeniteFX from its author's GitHub ($url) ..."
            $part = "$lumeniteSrc.part"
            try {
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $lumeniteSrc) | Out-Null
                [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
                Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $part
                Move-Item -LiteralPath $part -Destination $lumeniteSrc -Force
            } catch {
                Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
                Say 'The download failed; nothing was installed. On https://github.com/umar-afzaal/LumeniteFX, Code > Download ZIP'
                Fail 'downloads it; then install with -LumeniteZip <the downloaded zip>.'
            }
        }
    }
    try { $lumeniteFx = Read-Lumenite $lumeniteSrc $feedFx }
    catch { Say $_.Exception.Message; Fail 'This LumeniteFX cannot be used (the reason is above); nothing was installed.' }
    $got = Get-ZipComment $lumeniteSrc
    if ($got -and $got -ne $LumeniteCommit) {
        Say "Note: this LumeniteFX is commit $($got.Substring(0, [Math]::Min(7, $got.Length))), not $($LumeniteCommit.Substring(0, 7)) this package was tested with. Its interface checks passed; installing it."
    }
}
if (Test-Path -LiteralPath $manifest) { Say 'Found a previous installation, removing it first.'; Remove-Installed }

Set-Content -LiteralPath $manifest -Value "F $ManifestName" -Encoding UTF8
Put-Tree (Join-Path $here 'dlssnr-amd') 'dlssnr-amd'
if ($modelFrom -ne $modelPath) { Copy-Item -LiteralPath $modelFrom -Destination (Join-Path $game 'dlssnr-amd\dlssnr.bin') -Force }
if ($haveSmall -and $smallFrom -ne $smallPath) { Copy-Item -LiteralPath $smallFrom -Destination (Join-Path $game 'dlssnr-amd\dlssnr-small.bin') -Force }
if ($int4ModelFrom) {
    try { Put-File $int4ModelFrom 'dlssnr-amd\dlssnr-int4.bin' }
    finally { Remove-Item -LiteralPath $int4ModelFrom -Force -ErrorAction SilentlyContinue }
}
if ($expModelFrom) {
    try { Put-File $expModelFrom 'dlssnr-amd\dlssnr-small-experimental.bin' }
    finally { Remove-Item -LiteralPath $expModelFrom -Force -ErrorAction SilentlyContinue }
}
$exeName = [System.IO.Path]::GetFileNameWithoutExtension($exe)
Record-Logs @('dlssnr-amd.log', 'dlssnr-amd-crash.dmp')

# Other ReShade add-ons in the game folder (installed earlier) would load too; move them to the backup folder.
function Move-OtherAddons {
    foreach ($item in Get-ChildItem -LiteralPath $game -File | Where-Object { $_.Extension -in '.addon', '.addon64' }) {
        if ($item.Name -ne 'dlssnr_amd.addon64') { Save-Existing $item.Name; Say "Moved another ReShade add-on aside: $($item.Name)" }
    }
}
# ReShade's files. $asLayer: as a Vulkan layer (DX9 through DXVK, Vulkan games), found by the Vulkan loader in the
# game folder; otherwise ReShade itself goes in as dxgi.dll (DX10/11/12 games).
function Put-ReShade([bool]$asLayer) {
    Move-OtherAddons
    foreach ($item in Get-ChildItem -LiteralPath (Join-Path $here 'reshade')) {
        if ($item.Name -eq 'ReShadePreset-d3d9.ini') { continue }
        if (-not $asLayer -and ($item.Name -eq 'vk-override' -or $item.Name -eq 'ReShade64.dll')) { continue }
        if ($item.PSIsContainer) { Put-Tree $item.FullName $item.Name } else { Put-File $item.FullName $item.Name }
    }
    if (-not $asLayer) { Put-File (Join-Path $here 'reshade\ReShade64.dll') 'dxgi.dll' }
    Record-Logs @('ReShade.log')
}

if ($Route -eq 'optiscaler') {
    # OptiScaler as released (its files unmodified): OptiScaler.dll as dxgi.dll, its ini, its OptiScaler\ folder; its
    # licences go into that folder (a Licenses folder of the game's own is not touched). Our NR plugin into
    # OptiScaler\plugins; NR Control's probe into dlssnr-amd\. The game keeps the system's own D3D11 / D3D12: NR runs
    # on a Vulkan device of its own and shares the frame with it.
    $rel = Join-Path $here 'optiscaler\release'
    Put-File (Join-Path $rel 'OptiScaler.dll') 'dxgi.dll'
    Put-File (Join-Path $rel 'OptiScaler.ini') 'OptiScaler.ini'
    Put-Tree (Join-Path $rel 'OptiScaler') 'OptiScaler'
    $lic = Join-Path $game 'OptiScaler\Licenses'
    New-Item -ItemType Directory -Force -Path $lic | Out-Null
    if (Test-Path -LiteralPath (Join-Path $rel 'Licenses')) { Copy-Item -Path (Join-Path $rel 'Licenses\*') -Destination $lic -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $here 'optiscaler\OptiScaler_LICENSE.txt') -Destination $lic -Force
    New-Item -ItemType Directory -Force -Path (Join-Path $game 'OptiScaler\plugins') | Out-Null
    Copy-Item -LiteralPath (Join-Path $here 'optiscaler\asi\dlssnr-amd.asi') -Destination (Join-Path $game 'OptiScaler\plugins\dlssnr-amd.asi') -Force
    Copy-Item -LiteralPath (Join-Path $here 'optiscaler\asi\nr_present_probe.exe') -Destination (Join-Path $game 'dlssnr-amd') -Force
    Record-Logs @('OptiScaler.log')

    # OptiScaler loads the .asi plugins in OptiScaler\plugins only when asked to. Every key here ships in the release's
    # OptiScaler.ini and is rewritten in place: a key renamed upstream shows up rather than being silently ignored.
    $ini = Join-Path $game 'OptiScaler.ini'
    $wanted = [ordered]@{ 'Plugins|LoadAsiPlugins' = 'true' }
    $raw = [System.IO.File]::ReadAllText($ini)
    $newline = if ($raw.Contains("`r`n")) { "`r`n" } else { "`n" }
    $lines = $raw -split [regex]::Escape($newline)
    $section = $null; $seen = @{}
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^\s*\[([^\]]+)\]') { $section = $Matches[1]; continue }
        if ($section -and $lines[$i] -match '^\s*([A-Za-z0-9_]+)\s*=') {
            $key = "$section|$($Matches[1])"
            if ($wanted.Contains($key) -and -not $seen.ContainsKey($key)) { $lines[$i] = "$($Matches[1])=$($wanted[$key])"; $seen[$key] = 1 }
        }
    }
    [System.IO.File]::WriteAllText($ini, ($lines -join $newline))
    foreach ($key in $wanted.Keys) { if (-not $seen.ContainsKey($key)) { Fail "OptiScaler.ini has no $key; this OptiScaler is not the one this package was made with." } }
} elseif ($Route -eq 'reshade-feeder') {
    # DX10/11/12 games keep the system's own D3D; ReShade goes in as dxgi.dll.
    Put-ReShade $false
} else {
    # DX9: DXVK moves the game to Vulkan, ReShade runs as a Vulkan layer. Vulkan games: only the ReShade layer.
    # The Vulkan loader in the game folder never calls DXGI and finds ReShade's layer beside it.
    Put-File (Join-Path $here 'vulkan\vulkan-1.dll') 'vulkan-1.dll'
    if ($Route -eq 'reshade-feeder-dx9') {
        Put-File (Join-Path $here 'dxvk\d3d9.dll') 'd3d9.dll'
        Put-File (Join-Path $here 'dxvk\dxgi.dll') 'dxgi.dll'
        Record-Logs @("${exeName}_dxgi.log", "${exeName}_d3d9.log")
    }
    Put-ReShade $true
    if ($Route -eq 'reshade-feeder-dx9') {
        Copy-Item -LiteralPath (Join-Path $here 'reshade\ReShadePreset-d3d9.ini') -Destination (Join-Path $game 'ReShadePreset.ini') -Force
    }
}
if ($lumeniteFx) {
    # into reshade-shaders\ (the whole folder is recorded and removed with the rest); ReShadePreset.ini switched to
    # LumeniteFX: DLSS5_MV_PROVIDER=3, Lumenite_Kernel on (VORT off), sorted before DLSS5_Feed
    $n = Install-Lumenite $lumeniteFx (Join-Path $game 'reshade-shaders')
    Say "LumeniteFX: $n files installed into $(Join-Path $game 'reshade-shaders')"
    $preset = Join-Path $game 'ReShadePreset.ini'
    $lines = [System.IO.File]::ReadAllLines($preset)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -like 'PreprocessorDefinitions=*') { $lines[$i] = $lines[$i].Replace('DLSS5_MV_PROVIDER=2', 'DLSS5_MV_PROVIDER=3') }
        elseif ($lines[$i] -like 'Techniques=*') { $lines[$i] = $lines[$i].Replace('vort_MotionEffects@vort_Motion.fx', 'Lumenite_Kernel@lumenite_Kernel.fx') }
        elseif ($lines[$i] -like 'TechniqueSorting=*') { $lines[$i] = 'TechniqueSorting=Lumenite_Kernel@lumenite_Kernel.fx,' + $lines[$i].Substring(17) }
    }
    [System.IO.File]::WriteAllText($preset, (($lines -join "`r`n") + "`r`n"))
    $text = [System.IO.File]::ReadAllText($preset)
    if ($text -cnotmatch '(?m)^PreprocessorDefinitions=.*DLSS5_MV_PROVIDER=3' -or $text -cnotmatch '(?m)^Techniques=Lumenite_Kernel@lumenite_Kernel\.fx,DLSS5_Feed@DLSS5_Feed\.fx') {
        Fail "ReShadePreset.ini could not be switched to LumeniteFX ($preset)."
    }
}

# dlssnr-amd.ini is written at install with every setting and its description (a reinstall writes the defaults
# again). Uninstall deletes it.
if ($Route -eq 'optiscaler' -and $RenoDX -eq 'on') { $iniSrc = 'ini\dlssnr-amd-optiscaler-renodx.ini' }
elseif ($Route -eq 'optiscaler') { $iniSrc = 'ini\dlssnr-amd-optiscaler.ini' }
else { $iniSrc = 'ini\dlssnr-amd-addon.ini' }
$iniDst = Join-Path $game 'dlssnr-amd.ini'
# The RenoDX style ([RenoDX] Enabled = 1 on the ReShade routes; the OptiScaler route has a file of its own), the
# network a game starts with ([Network] Model).
$lines = [System.IO.File]::ReadAllLines((Join-Path $here $iniSrc), [System.Text.Encoding]::UTF8)
$section = ''; $done = @{}
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\[([^\]]+)\]') { $section = $Matches[1]; continue }
    if ($section -eq 'RenoDX' -and $Route -ne 'optiscaler' -and $RenoDX -eq 'on' -and $lines[$i] -eq 'Enabled = 0') {
        $lines[$i] = 'Enabled = 1'; $done['renodx'] = 1
    } elseif ($section -eq 'Network' -and $lines[$i] -match '^Model = ') { $lines[$i] = "Model = $Model"; $done['model'] = 1 }
    elseif ($lumeniteFx -and $lines[$i] -ceq 'MotionVectors=vort') { $lines[$i] = 'MotionVectors=lumenite'; $done['lumenite'] = 1 }
}
if ($lumeniteFx -and -not $done['lumenite']) { Fail 'dlssnr-amd.ini: could not set MotionVectors=lumenite.' }
if (-not $done['model'] -or ($Route -ne 'optiscaler' -and $RenoDX -eq 'on' -and -not $done['renodx'])) {
    Fail 'The package''s dlssnr-amd.ini is not as expected; unpack the package again.'
}
[System.IO.File]::WriteAllText($iniDst, (($lines -join "`r`n") + "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
Record-Logs @('dlssnr-amd.ini')

Say ''
$style = if ($RenoDX -eq 'on') { 'RenoDX style' } else { 'wilsjo2 style' }
Say "Installed the $Route route into: $game ($style)"
Say "Networks installed: $(if ($haveSmall) { 'CC_SMALL_FP8, ' })$(if ($expModelFrom) { 'CC_SMALL_FP8_EXPERIMENTAL, ' })CC_SILVER_AARDWOLD$(if ($int4ModelFrom) { ', CC_SILVER_AARDWOLD_INT4_MIXED' }); games start with $Model."
Say "Switch the network in game at any time: the drop-down in $(if ($Route -eq 'optiscaler') { 'NR Control (End)' } else { 'ReShade''s Add-ons page' }), or Ctrl+F11 to cycle; settings: [Network] in dlssnr-amd.ini."
Say 'The network runs as machine code made in advance by the ACO compiler (ACO Mode). When your driver cannot take it,'
Say "$(if ($Route -eq 'optiscaler') { 'NR Control' } else { 'ReShade''s Add-ons page' }) and dlssnr-amd.log say why; an RX 9000 then runs the network compiled by the AMD driver"
Say '(without CC_SMALL_FP8_EXPERIMENTAL and CC_SILVER_AARDWOLD_INT4_MIXED); RX 7000 and Radeon 700M / 800M / 8000S graphics have no NR then.'
if ($Route -eq 'optiscaler') {
    Say 'In game, turn on DLSS, FSR or XeSS in the game''s settings. End opens NR Control (DirectX 11 / 12 and Vulkan games: every'
    Say 'NR setting: processing, network, stacked layers); Insert opens the OptiScaler menu (upscaling and frame generation only).'
    Say 'If NR Control keeps waiting for the game''s upscaler, set [Spoofing] Dxgi=false in OptiScaler.ini and try again.'
    Say 'If colours turn green or grainy after NR (007 First Light, for example), try [Preprocess] in dlssnr-amd.ini, or press Ctrl+F10 in the game.'
    Say 'F5 takes screenshots (the same frame without and with NR) into NR Screenshots in the game folder.'
} else {
    Say 'In the game, Home opens ReShade; the settings are on the Add-ons page, or edit dlssnr-amd.ini directly.'
    if ($lumeniteFx) {
        Say 'Motion vectors: LumeniteFX Kernel (by Afzaal (Kaidō), https://github.com/umar-afzaal/LumeniteFX, AGNYA licence;'
        Say '  not part of this project, installed unchanged from its author''s GitHub). Motion vectors on the Add-ons page switches back to VORT;'
        Say '  when LumeniteFX cannot be used in game, VORT runs that time and dlssnr-amd.log says why.'
    }
    if ($RenoDX -eq 'on') {
        Say 'Processing before and after NR: the RenoDX style ([RenoDX] Enabled = 1 in dlssnr-amd.ini), settings on the Add-ons page (that is [RenoDX]).'
    } else {
        Say 'Processing before and after NR: the wilsjo2 style ([RenoDX] Enabled = 0), settings on the Add-ons page (that is [DlssNr]).'
    }
    Say 'The Add-ons page can stack several layers ([LayerN]), each RenoDX or wilsjo2. F5 takes screenshots (the same frame without and with NR) into NR Screenshots in the game folder.'
}
Say 'The first time NR runs in a game the network has to compile; it takes effect after about a minute. This happens once for each game.'
Say 'Uninstall: run install.bat again and choose 5.'
Finish 0
