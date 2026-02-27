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

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $quoted = $Arguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + ($_ -replace '"', '\"') + '"'
        }
        else {
            $_
        }
    }

    $argumentLine = $quoted -join " "
    $process = Start-Process -FilePath "dotnet" -ArgumentList $argumentLine -NoNewWindow -Wait -PassThru
    return $process.ExitCode
}

function Invoke-PublishAttempt {
    param(
        [Parameter(Mandatory = $true)][string]$Mode,
        [Parameter(Mandatory = $true)][string[]]$ExtraArgs,
        [switch]$UseRuntimeIdentifier
    )

    if (Test-Path $publishDir) {
        Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue
    }

    Write-Host "Publish mode: $Mode"

    $publishArgs = @(
        "publish", $projectPath,
        "-c", $Configuration,
        "-f", $Framework,
        "-p:WindowsPackageType=None",
        "-p:GenerateAppxPackageOnBuild=false",
        "-p:AppxPackage=false",
        "-o", $publishDir
    )

    if ($UseRuntimeIdentifier) {
        $publishArgs += @("-r", $RuntimeIdentifier)
    }

    $publishArgs += $ExtraArgs

    $exitCode = Invoke-Dotnet -Arguments $publishArgs

    if ($exitCode -eq 0 -and (Test-Path $publishDir)) {
        return $true
    }

    Write-Warning "Publish attempt '$Mode' failed (exit code: $exitCode)."
    return $false
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\AlwaysOnTopTranscriber.Hybrid\AlwaysOnTopTranscriber.Hybrid.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\Transcriber-v1.2"

if (-not (Test-Path $projectPath)) {
    throw "Hybrid project not found: $projectPath"
}

if (-not $SkipPublish) {
    Write-Host "Publishing app..."

    $published = Invoke-PublishAttempt -Mode "self-contained" -ExtraArgs @(
        "-p:SelfContained=true"
    ) -UseRuntimeIdentifier

    if (-not $published) {
        Write-Host "Retrying publish with framework-dependent mode..."
        $published = Invoke-PublishAttempt -Mode "framework-dependent fallback" -ExtraArgs @(
            "-p:SelfContained=false",
            "-p:WindowsAppSDKSelfContained=false"
        )
    }

    if (-not $published) {
        throw "dotnet publish failed in all modes."
    }
}

if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

Write-Host "Installing to: $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

robocopy $publishDir $InstallDir /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) {
    throw "Robocopy failed (exit code: $LASTEXITCODE)."
}

$exe = Get-ChildItem -Path $InstallDir -Filter "*.exe" -File |
    Where-Object { $_.Name -notmatch "WebView2Loader|CrashPad" } |
    Sort-Object Length -Descending |
    Select-Object -First 1

if (-not $exe) {
    throw "EXE file not found after install in: $InstallDir"
}

$desktopPath = [Environment]::GetFolderPath("Desktop")
$startMenuPath = [Environment]::GetFolderPath("Programs")

$desktopShortcut = Join-Path $desktopPath "Transcriber v1.2.lnk"
$startShortcut = Join-Path $startMenuPath "Transcriber v1.2.lnk"

New-AppShortcut -ShortcutPath $desktopShortcut -TargetPath $exe.FullName -WorkingDirectory $InstallDir
New-AppShortcut -ShortcutPath $startShortcut -TargetPath $exe.FullName -WorkingDirectory $InstallDir

Write-Host ""
Write-Host "Installation completed."
Write-Host "EXE: $($exe.FullName)"
Write-Host "Desktop shortcut: $desktopShortcut"
Write-Host "Start shortcut:   $startShortcut"
