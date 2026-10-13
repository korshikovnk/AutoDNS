$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

Write-Host "Generating icons..."
$iconPath = Join-Path $root "src\AutoDNS\Assets\app.ico"
dotnet run --project (Join-Path $root "tools\IconGen\IconGen.csproj") -c Release -- $iconPath
if ($LASTEXITCODE -ne 0) { throw "IconGen failed" }

dotnet publish "$root\src\AutoDNS\AutoDNS.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o "$root\publish"
if ($LASTEXITCODE -ne 0) { throw "AutoDNS publish failed" }

$iscc = @(
    "$root\tools\InnoSetup\compiler\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    & $iscc "$root\installer\autodns.iss"
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed" }
    Write-Host "Installer: $root\dist\AutoDNS-Setup.exe"
} else {
    Write-Host "Inno Setup not found. Published exe: $root\publish\AutoDNS.exe"
}
