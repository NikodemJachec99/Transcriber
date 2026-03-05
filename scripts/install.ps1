# Transcriber Installation Script - PowerShell (Recommended)
# Works on Windows 10/11 with .NET SDK 8.0+

param(
    [switch]$NoShortcuts = $false
)

$ErrorActionPreference = "Stop"

Write-Host "`n╔════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Transcriber Installation Script       ║" -ForegroundColor Cyan
Write-Host "║       Windows 10/11 (PowerShell)       ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════╝`n" -ForegroundColor Cyan

# Check project root
if (-not (Test-Path "AlwaysOnTopTranscriber.sln")) {
    Write-Host "ERROR: Nie znaleziono AlwaysOnTopTranscriber.sln!" -ForegroundColor Red
    Write-Host "Uruchom ten skrypt z głównego folderu projektu.`n" -ForegroundColor Red
    exit 1
}

# 1. Check .NET SDK
Write-Host "[1/4] Sprawdzam .NET SDK..." -ForegroundColor Cyan
try {
    $dotnetVersion = dotnet --version
    Write-Host "[OK] .NET SDK zainstalowany: $dotnetVersion" -ForegroundColor Green

    $majorVersion = [int]($dotnetVersion.Split('.')[0])
    if ($majorVersion -lt 8) {
        Write-Host "`nERROR: Wymagany .NET SDK 8.0 lub wyższy!" -ForegroundColor Red
        Write-Host "Pobierz z: https://dotnet.microsoft.com/download/dotnet/8.0`n" -ForegroundColor Yellow
        exit 1
    }
}
catch {
    Write-Host "ERROR: .NET SDK nie znaleziony!" -ForegroundColor Red
    Write-Host "Pobierz z: https://dotnet.microsoft.com/download/dotnet/8.0`n" -ForegroundColor Yellow
    exit 1
}

# 2. Install MAUI workload
Write-Host "`n[2/4] Instaluję MAUI workload (może to chwilę potrwać)..." -ForegroundColor Cyan
$output = dotnet workload install maui-windows --skip-manifest-update 2>&1
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 0) {
    Write-Host "[WARNING] Instalacja MAUI mogła się nie powieść, próbuję dalej..." -ForegroundColor Yellow
}
else {
    Write-Host "[OK] MAUI workload zainstalowany" -ForegroundColor Green
}

# 3. Build application
Write-Host "`n[3/4] Budowanie aplikacji (może to kilka minut)..." -ForegroundColor Cyan
Write-Host "[*] Przywracam zależności..." -ForegroundColor White
dotnet restore AlwaysOnTopTranscriber.sln | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Błąd przy przywracaniu zależności!" -ForegroundColor Red
    exit 1
}

Write-Host "[*] Kompilacja..." -ForegroundColor White
dotnet publish -c Release -o "build\publish" `
    src/AlwaysOnTopTranscriber.App/AlwaysOnTopTranscriber.App.csproj | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Błąd przy kompilacji!" -ForegroundColor Red
    exit 1
}
Write-Host "[OK] Aplikacja zbudowana" -ForegroundColor Green

# 4. Install to AppData
Write-Host "`n[4/4] Instaluję aplikację..." -ForegroundColor Cyan
$installDir = "$env:LocalAppData\Programs\Transcriber v1.2"

if (Test-Path $installDir) {
    Write-Host "[*] Usuwam poprzednią wersję..." -ForegroundColor White
    Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "[*] Kopiuję pliki do: $installDir" -ForegroundColor White
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item -Path "build\publish\*" -Destination $installDir -Recurse -Force
Write-Host "[OK] Aplikacja zainstalowana" -ForegroundColor Green

# 5. Create shortcuts
Write-Host "`n[*] Tworzę skróty..." -ForegroundColor White
$appExe = Join-Path $installDir "AlwaysOnTopTranscriber.App.exe"

if (-not (Test-Path $appExe)) {
    Write-Host "[WARNING] Nie znaleziono .exe aplikacji" -ForegroundColor Yellow
}
else {
    # Desktop shortcut
    $desktopPath = [Environment]::GetFolderPath("Desktop")
    $shortcutPath = Join-Path $desktopPath "Transcriber.lnk"

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortCut($shortcutPath)
    $shortcut.TargetPath = $appExe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.Save()

    Write-Host "[OK] Skrót na pulpicie utworzony" -ForegroundColor Green

    # Start Menu shortcut
    $startMenuPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Transcriber"
    New-Item -ItemType Directory -Path $startMenuPath -Force -ErrorAction SilentlyContinue | Out-Null
    $startMenuShortcut = Join-Path $startMenuPath "Transcriber.lnk"

    $shortcut = $shell.CreateShortCut($startMenuShortcut)
    $shortcut.TargetPath = $appExe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.Save()

    Write-Host "[OK] Skrót w menu Start utworzony" -ForegroundColor Green
}

Write-Host "`n════════════════════════════════════════" -ForegroundColor Green
Write-Host "✓ Instalacja zakończona pomyślnie!" -ForegroundColor Green
Write-Host "════════════════════════════════════════`n" -ForegroundColor Green

Write-Host "Aplikacja jest dostępna:" -ForegroundColor Cyan
Write-Host "  • Na pulpicie (skrót 'Transcriber')" -ForegroundColor White
Write-Host "  • W menu Start (Transcriber)" -ForegroundColor White
Write-Host "  • W folderze: $installDir" -ForegroundColor White

Write-Host "`nDane aplikacji: $env:AppData\Transcriber\" -ForegroundColor Cyan
Write-Host "`nUruchom aplikację i pobierz model do transkrypcji!`n" -ForegroundColor Green
