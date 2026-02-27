param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Framework = "net8.0-windows10.0.19041.0",
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\Transcriber v1.2",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

function New-AppShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = "$TargetPath,0"
    $shortcut.Description = "Transcriber v1.2"
    $shortcut.Save()
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\AlwaysOnTopTranscriber.Hybrid\AlwaysOnTopTranscriber.Hybrid.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\Transcriber-v1.2"

if (-not (Test-Path $projectPath)) {
    throw "Nie znaleziono projektu Hybrid: $projectPath"
}

if (-not $SkipPublish) {
    Write-Host "Publikowanie aplikacji..."
    dotnet publish $projectPath `
        -c $Configuration `
        -f $Framework `
        -r $RuntimeIdentifier `
        -p:WindowsPackageType=None `
        -p:SelfContained=true `
        -o $publishDir
}

if (-not (Test-Path $publishDir)) {
    throw "Brak katalogu publish: $publishDir"
}

Write-Host "Instalowanie do: $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

robocopy $publishDir $InstallDir /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) {
    throw "Robocopy zakończył się błędem (exit code: $LASTEXITCODE)."
}

$exe = Get-ChildItem -Path $InstallDir -Filter "*.exe" -File |
    Where-Object { $_.Name -notmatch "WebView2Loader|CrashPad" } |
    Sort-Object Length -Descending |
    Select-Object -First 1

if (-not $exe) {
    throw "Nie znaleziono pliku EXE po instalacji w: $InstallDir"
}

$desktopPath = [Environment]::GetFolderPath("Desktop")
$startMenuPath = [Environment]::GetFolderPath("Programs")

$desktopShortcut = Join-Path $desktopPath "Transcriber v1.2.lnk"
$startShortcut = Join-Path $startMenuPath "Transcriber v1.2.lnk"

New-AppShortcut -ShortcutPath $desktopShortcut -TargetPath $exe.FullName -WorkingDirectory $InstallDir
New-AppShortcut -ShortcutPath $startShortcut -TargetPath $exe.FullName -WorkingDirectory $InstallDir

Write-Host ""
Write-Host "Instalacja zakończona."
Write-Host "EXE: $($exe.FullName)"
Write-Host "Skrót pulpit: $desktopShortcut"
Write-Host "Skrót Start:  $startShortcut"
