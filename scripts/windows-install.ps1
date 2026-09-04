$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "          WiFi Traffic - Windows Setup      " -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Restarting setup as Administrator..." -ForegroundColor Yellow
    Start-Process powershell.exe -Verb RunAs -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath)
    exit
}

$npcap = Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Npcap" -ErrorAction SilentlyContinue
if (-not $npcap) {
    $npcap = Get-ItemProperty "HKLM:\SOFTWARE\Npcap" -ErrorAction SilentlyContinue
}

if (-not $npcap) {
    Write-Host "Npcap is required for packet capture." -ForegroundColor Yellow
    Write-Host "Opening the official Npcap download page..." -ForegroundColor Yellow
    Start-Process "https://npcap.com/#download"
    Read-Host "Install Npcap, then press Enter to exit and run setup again"
    exit 1
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host ".NET 8 SDK is required when installing from source." -ForegroundColor Yellow
    Write-Host "Opening Microsoft .NET 8 download page..." -ForegroundColor Yellow
    Start-Process "https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
    Read-Host "Install the .NET 8 SDK, then press Enter to exit and run setup again"
    exit 1
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishDir = Join-Path $repoRoot "dist\win-x64"
$project = Join-Path $repoRoot "src\WifiTraffic.App\WifiTraffic.App.csproj"

Write-Host "Building self-contained Windows app..." -ForegroundColor Cyan
dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $publishDir "WifiTraffic.exe"
if (-not (Test-Path $exe)) {
    throw "Build completed but WifiTraffic.exe was not found."
}

$desktop = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktop "WiFi Traffic.lnk"

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $publishDir
$shortcut.Description = "WiFi Traffic"
$shortcut.Save()

Write-Host ""
Write-Host "Installation complete." -ForegroundColor Green
Write-Host "App: $exe"
Write-Host "Desktop shortcut: $shortcutPath"
Write-Host ""
Start-Process $exe
