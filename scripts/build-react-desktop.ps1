param(
    [string]$Version = "2.2.0-react-preview.5",
    [string]$Output = "artifacts/react-desktop/win-x64"
)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    npm ci --prefix desktop-ui
    if ($LASTEXITCODE -ne 0) { throw "React dependency install failed" }
    npm run build --prefix desktop-ui
    if ($LASTEXITCODE -ne 0) { throw "React build failed" }
    npm test --prefix desktop-ui
    if ($LASTEXITCODE -ne 0) { throw "React tests failed" }
    dotnet publish src/AmdNrAssistant.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:Version=$Version -p:InformationalVersion=$Version `
        -o $Output
    if ($LASTEXITCODE -ne 0) { throw "Windows publish failed" }
    Write-Host "Windows build: $repoRoot/$Output"
} finally { Pop-Location }
