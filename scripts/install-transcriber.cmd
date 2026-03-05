@echo off
REM Transcriber Installation Script for Windows 10/11
REM Requires: .NET SDK 8.0+

setlocal enabledelayedexpansion
color 0F

echo.
echo ╔════════════════════════════════════════╗
echo ║  Transcriber Installation Script       ║
echo ║       Windows 10/11                    ║
echo ╚════════════════════════════════════════╝
echo.

REM Check if we're in project root
if not exist "AlwaysOnTopTranscriber.sln" (
    color 0C
    echo ERROR: Nie znaleziono AlwaysOnTopTranscriber.sln!
    echo.
    echo Upewnij sie ze uruchamiasz ten plik z glownego folderu projektu:
    echo   D:\Plan zajęć Madzi\apka do transkrybcji\
    echo.
    pause
    exit /b 1
)

REM Check for .NET SDK
echo [1/4] Sprawdzam .NET SDK...
dotnet --version >nul 2>&1
if errorlevel 1 (
    color 0C
    echo.
    echo ERROR: .NET SDK nie znaleziony!
    echo.
    echo Pobierz i zainstaluj .NET SDK 8.0 z:
    echo   https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

for /f "tokens=*" %%i in ('dotnet --version') do set DOTNET_VERSION=%%i
color 0A
echo [OK] .NET SDK zainstalowany: %DOTNET_VERSION%
color 0F

REM Check for MAUI workload
echo.
echo [2/4] Sprawdzam/instaluję MAUI workload...
echo [*] Instaluję MAUI workload (może to chwilę potrwać)...
dotnet workload install maui-windows --skip-manifest-update
if errorlevel 1 (
    color 0E
    echo [WARNING] Instalacja MAUI mogła się nie powieść, próbuję dalej...
    color 0F
) else (
    color 0A
    echo [OK] MAUI workload zainstalowany
    color 0F
)

REM Restore and build
echo.
echo [3/4] Budowanie aplikacji (może to kilka minut)...
echo [*] Przywracam zależności...
dotnet restore AlwaysOnTopTranscriber.sln
if errorlevel 1 (
    color 0C
    echo.
    echo ERROR: Błąd przy przywracaniu zależności!
    pause
    exit /b 1
)

echo [*] Kompilacja...
dotnet publish -c Release -o build\publish src\AlwaysOnTopTranscriber.App\AlwaysOnTopTranscriber.App.csproj
if errorlevel 1 (
    color 0C
    echo.
    echo ERROR: Błąd przy kompilacji!
    pause
    exit /b 1
)

color 0A
echo [OK] Aplikacja zbudowana
color 0F

REM Install to AppData
echo.
echo [4/4] Instaluję aplikację...
set INSTALL_DIR=%LocalAppData%\Programs\Transcriber v1.2

if exist "%INSTALL_DIR%" (
    echo [*] Usuwam poprzednią wersję...
    rmdir /s /q "%INSTALL_DIR%" >nul 2>&1
)

echo [*] Kopiuję pliki do: %INSTALL_DIR%
mkdir "%INSTALL_DIR%" >nul 2>&1
xcopy /E /Y /Q "build\publish\*" "%INSTALL_DIR%\"
if errorlevel 1 (
    color 0C
    echo ERROR: Błąd przy kopiowaniu plików!
    pause
    exit /b 1
)

color 0A
echo [OK] Aplikacja zainstalowana
color 0F

REM Create shortcuts
echo.
echo [*] Tworzę skróty...

set APP_EXE=%INSTALL_DIR%\AlwaysOnTopTranscriber.App.exe
if not exist "%APP_EXE%" (
    color 0E
    echo [WARNING] Nie znaleziono AlwaysOnTopTranscriber.App.exe
    color 0F
    goto skip_shortcuts
)

REM Desktop shortcut (using VBScript)
set VBS_FILE=%TEMP%\create_shortcut.vbs
(
    echo Set objShell = CreateObject("WScript.Shell"
    echo strDesktop = objShell.SpecialFolders("Desktop"^)
    echo Set objLink = objShell.CreateShortCut(strDesktop ^& "\Transcriber.lnk"^)
    echo objLink.TargetPath = "%APP_EXE%"
    echo objLink.WorkingDirectory = "%INSTALL_DIR%"
    echo objLink.Save
) > "%VBS_FILE%"
cscript //nologo "%VBS_FILE%" >nul 2>&1
del /q "%VBS_FILE%" >nul 2>&1

color 0A
echo [OK] Skrót na pulpicie utworzony
color 0F

:skip_shortcuts
echo.
echo ════════════════════════════════════════
echo ════════════════════════════════════════
color 0A
echo SUKCES! Instalacja zakończona!
echo ════════════════════════════════════════
color 0F
echo.
echo Aplikacja jest dostępna:
echo   - Na pulpicie (skrót 'Transcriber')
echo   - W folderze: %INSTALL_DIR%
echo.
echo Dane aplikacji: %AppData%\Transcriber\
echo.
color 0B
echo Uruchom aplikację i pobierz model do transkrypcji!
color 0F
echo.
pause
exit /b 0
