$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\WifiTraffic.App\WifiTraffic.App.csproj"
$out = Join-Path $root "dist\win-x64"

dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Host "Built: $out\WifiTraffic.exe" -ForegroundColor Green
