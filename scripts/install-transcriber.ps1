# Transcriber Installation Script for Windows 10/11
# Pobiera, buduje i instaluje aplikację lokalnie

param(
    [switch]$SkipRuntime = $false,
    [switch]$NoShortcuts = $false
)

$ErrorActionPreference = "Stop"

# Kolory
$Green = [System.Console]::ForegroundColor = "Green"
$Red = [System.Console]::ForegroundColor = "Red"
$Yellow = [System.Console]::ForegroundColor = "Yellow"

function Write-Color {
    param([string]$Message, [string]$Color = "White")
    Write-Host $Message -ForegroundColor $Color
}

function Check-DotNet {
    Write-Color "Sprawdzam .NET SDK..." "Cyan"
    try {
        $version = dotnet --version
        Write-Color "✓ .NET SDK zainstalowany: $version" "Green"

        if ($version -lt "8.0") {
            Write-Color "✗ Wymagany .NET SDK 8.0 lub wyższy!" "Red"
            Write-Color "Pobierz z: https://dotnet.microsoft.com/download/dotnet/8.0" "Yellow"
            exit 1
        }
    }
    catch {
        Write-Color "✗ .NET SDK nie znaleziony!" "Red"
        Write-Color "Pobierz z: https://dotnet.microsoft.com/download/dotnet/8.0" "Yellow"
        exit 1
    }
}

function Install-MauiWorkload {
    Write-Color "Sprawdzam MAUI workload..." "Cyan"
    try {
        $workloads = dotnet workload list 2>&1 | Select-String "maui-windows"
        if ($workloads.Count -eq 0) {
            Write-Color "Instaluję MAUI workload (może to chwilę potrwać)..." "Yellow"
            dotnet workload install maui-windows --skip-manifest-update
            if ($LASTEXITCODE -ne 0) {
                Write-Color "⚠ MAUI workload instalacja mogła się nie powieść, próbuję dalej..." "Yellow"
            }
            else {
                Write-Color "✓ MAUI workload zainstalowany" "Green"
            }
        }
        else {
            Write-Color "✓ MAUI workload już zainstalowany" "Green"
        }
    }
    catch {
        Write-Color "⚠ Nie udało się zainstalować MAUI workload" "Yellow"
    }
}

function Build-Application {
    Write-Color "Budowanie aplikacji..." "Cyan"

    $solution = "AlwaysOnTopTranscriber.sln"
    if (-not (Test-Path $solution)) {
        Write-Color "✗ Nie znaleziono $solution!" "Red"
        exit 1
    }

    Write-Color "Przywracam zależności..." "Yellow"
    dotnet restore $solution
    if ($LASTEXITCODE -ne 0) {
        Write-Color "✗ Błąd przy przywracaniu zależności!" "Red"
        exit 1
    }

    Write-Color "Kompilacja (może to chwilę potrwać)..." "Yellow"
    dotnet publish -c Release -o build\publish src\AlwaysOnTopTranscriber.App\AlwaysOnTopTranscriber.App.csproj
    if ($LASTEXITCODE -ne 0) {
        Write-Color "✗ Błąd przy kompilacji!" "Red"
        exit 1
    }

    Write-Color "✓ Aplikacja zbudowana pomyślnie" "Green"
}

function Install-Runtime {
    if ($SkipRuntime) {
        Write-Color "Pomijam instalację Windows App Runtime (--SkipRuntime)" "Yellow"
        return
    }

    Write-Color "Sprawdzam Windows App Runtime..." "Cyan"

    $runtimePath = "$env:ProgramFiles\WindowsApps"
    $runtimeInstalled = $false

    try {
        $runtimes = Get-ChildItem $runtimePath -Filter "Microsoft.WindowsAppRuntime*" -ErrorAction SilentlyContinue
        if ($runtimes.Count -gt 0) {
            Write-Color "✓ Windows App Runtime już zainstalowany" "Green"
            return
        }
    }
    catch {}

    Write-Color "Pobieranie Windows App Runtime..." "Yellow"
    $runtimeUrl = "https://aka.ms/windowsappruntimeinstall"
    $runtimePath = "$env:TEMP\WindowsAppRuntimeInstall.exe"

    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $runtimeUrl -OutFile $runtimePath
        Write-Color "Instaluję Windows App Runtime..." "Yellow"
        Start-Process $runtimePath -Wait -ArgumentList "/quiet"
        Remove-Item $runtimePath -ErrorAction SilentlyContinue
        Write-Color "✓ Windows App Runtime zainstalowany" "Green"
    }
    catch {
        Write-Color "⚠ Nie udało się pobrać/zainstalować Windows App Runtime" "Yellow"
        Write-Color "Możesz zainstalować ręcznie z: https://aka.ms/windowsappruntimeinstall" "Yellow"
    }
}

function Copy-ToInstallDir {
    $installDir = "$env:LocalAppData\Programs\Transcriber v1.2"

    Write-Color "Instaluję aplikację do: $installDir" "Cyan"

    if (Test-Path $installDir) {
        Remove-Item $installDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    Copy-Item -Path "build\publish\*" -Destination $installDir -Recurse -Force

    Write-Color "✓ Aplikacja zainstalowana" "Green"
    return $installDir
}

function Create-Shortcuts {
    param([string]$InstallDir)

    if ($NoShortcuts) {
        Write-Color "Pomijam tworzenie skrótów (--NoShortcuts)" "Yellow"
        return
    }

    Write-Color "Tworzę skróty..." "Cyan"

    $appExe = Join-Path $InstallDir "AlwaysOnTopTranscriber.App.exe"
    if (-not (Test-Path $appExe)) {
        Write-Color "⚠ Nie znaleziono .exe aplikacji" "Yellow"
        return
    }

    # Desktop shortcut
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Transcriber.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($desktopShortcut)
    $shortcut.TargetPath = $appExe
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Save()
    Write-Color "✓ Skrót na pulpicie utworzony" "Green"

    # Start Menu shortcut
    $startMenuPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Transcriber"
    New-Item -ItemType Directory -Path $startMenuPath -Force | Out-Null
    $startMenuShortcut = Join-Path $startMenuPath "Transcriber.lnk"
    $shortcut = $shell.CreateShortcut($startMenuShortcut)
    $shortcut.TargetPath = $appExe
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Save()
    Write-Color "✓ Skrót w menu Start utworzony" "Green"
}

function Main {
    Write-Color "`n╔════════════════════════════════════════╗" "Cyan"
    Write-Color "║     Transcriber Installation Script     ║" "Cyan"
    Write-Color "║              Windows 10/11              ║" "Cyan"
    Write-Color "╚════════════════════════════════════════╝`n" "Cyan"

    Check-DotNet
    Install-MauiWorkload
    Build-Application
    Install-Runtime
    $installDir = Copy-ToInstallDir
    Create-Shortcuts $installDir

    Write-Color "`n════════════════════════════════════════" "Green"
    Write-Color "✓ Instalacja zakończona pomyślnie!" "Green"
    Write-Color "════════════════════════════════════════`n" "Green"
    Write-Color "Aplikacja jest dostępna:" "Cyan"
    Write-Color "  - Na pulpicie (skrót 'Transcriber')" "White"
    Write-Color "  - W menu Start (Transcriber)" "White"
    Write-Color "  - W folderze: $installDir" "White"
    Write-Color "`nDane aplikacji: $env:AppData\Transcriber\" "Cyan"
    Write-Color "`nAby rozszerzyć, uruchom Transcriber!" "Green"
}

Main
