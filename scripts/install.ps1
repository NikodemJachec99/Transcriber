param([switch]$NoShortcuts = $false)
$ErrorActionPreference = "Stop"
Write-Host "`n== Transcriber Installation ==" -ForegroundColor Cyan
if (-not (Test-Path "AlwaysOnTopTranscriber.sln")) {
    Write-Host "ERROR: AlwaysOnTopTranscriber.sln not found!" -ForegroundColor Red
    exit 1
}
Write-Host "[1/4] Checking .NET SDK..." -ForegroundColor Cyan
try {
    $dotnetVersion = dotnet --version
    Write-Host "[OK] .NET SDK: $dotnetVersion" -ForegroundColor Green
    $majorVersion = [int]($dotnetVersion.Split('.')[0])
    if ($majorVersion -lt 8) {
        Write-Host "ERROR: .NET SDK 8.0+ required!" -ForegroundColor Red
        Write-Host "Download: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
        exit 1
    }
}
catch {
    Write-Host "ERROR: .NET SDK not found!" -ForegroundColor Red
    Write-Host "Download: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    exit 1
}
Write-Host "`n[2/4] Installing MAUI workload..." -ForegroundColor Cyan
dotnet workload install maui-windows --skip-manifest-update 2>&1 | Out-Null
Write-Host "[OK] MAUI workload installed" -ForegroundColor Green
Write-Host "`n[3/4] Building application..." -ForegroundColor Cyan
Write-Host "[*] Restoring dependencies..." -ForegroundColor White
dotnet restore AlwaysOnTopTranscriber.sln 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Restore failed!" -ForegroundColor Red
    exit 1
}
Write-Host "[*] Compiling..." -ForegroundColor White
dotnet publish -c Release -o "build\publish" `
    src/AlwaysOnTopTranscriber.App/AlwaysOnTopTranscriber.App.csproj 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "[OK] Application built" -ForegroundColor Green
Write-Host "`n[4/4] Installing application..." -ForegroundColor Cyan
$installDir = "$env:LocalAppData\Programs\Transcriber v1.2"
if (Test-Path $installDir) {
    Write-Host "[*] Removing previous version..." -ForegroundColor White
    Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "[*] Copying files to: $installDir" -ForegroundColor White
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item -Path "build\publish\*" -Destination $installDir -Recurse -Force
Write-Host "[OK] Application installed" -ForegroundColor Green
Write-Host "`n[*] Creating shortcuts..." -ForegroundColor White
$appExe = Join-Path $installDir "AlwaysOnTopTranscriber.App.exe"
if (-not (Test-Path $appExe)) {
    Write-Host "[WARNING] .exe not found" -ForegroundColor Yellow
}
else {
    $desktopPath = [Environment]::GetFolderPath("Desktop")
    $shortcutPath = Join-Path $desktopPath "Transcriber.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortCut($shortcutPath)
    $shortcut.TargetPath = $appExe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.Save()
    Write-Host "[OK] Desktop shortcut created" -ForegroundColor Green

    $startMenuPath = "$env:AppData\Microsoft\Windows\Start Menu\Programs\Transcriber"
    New-Item -ItemType Directory -Path $startMenuPath -Force -ErrorAction SilentlyContinue | Out-Null
    $startMenuShortcut = Join-Path $startMenuPath "Transcriber.lnk"
    $shortcut = $shell.CreateShortCut($startMenuShortcut)
    $shortcut.TargetPath = $appExe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.Save()
    Write-Host "[OK] Start Menu shortcut created" -ForegroundColor Green
}
Write-Host "`n== Installation Complete ==" -ForegroundColor Green
Write-Host "App location: $installDir" -ForegroundColor Cyan
Write-Host "Data location: $env:AppData\Transcriber\" -ForegroundColor Cyan
Write-Host "`nRun the app and download a transcription model!`n" -ForegroundColor Green
