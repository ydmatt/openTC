param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$sandbox = Join-Path (
    [System.IO.Path]::GetTempPath()) (
    "MYTC.ReleaseSmoke." + [guid]::NewGuid().ToString("N"))
$dataRoot = Join-Path $sandbox "data"
$firstDirectory = Join-Path $sandbox "first"
$secondDirectory = Join-Path $sandbox "second"
$executable = Join-Path (
    [System.IO.Path]::GetFullPath($PackageRoot)) "MYTC.exe"

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Release executable not found: $executable"
}

New-Item `
    -ItemType Directory `
    -Path $dataRoot,$firstDirectory,$secondDirectory `
    -Force |
    Out-Null

$primary = $null
try {
    $primary = Start-Process `
        -FilePath $executable `
        -ArgumentList @(
            "--data-dir",
            $dataRoot,
            "--skip-initial-setup",
            "--open",
            $firstDirectory) `
        -PassThru
    $deadline = (Get-Date).AddSeconds(25)
    do {
        Start-Sleep -Milliseconds 250
        $primary.Refresh()
    } while (
        $primary.MainWindowHandle -eq 0 -and
        -not $primary.HasExited -and
        (Get-Date) -lt $deadline)

    if ($primary.HasExited) {
        throw "Primary exited early with code $($primary.ExitCode)."
    }

    if ($primary.MainWindowHandle -eq 0) {
        throw "Primary window did not appear."
    }

    $secondary = Start-Process `
        -FilePath $executable `
        -ArgumentList @(
            "--data-dir",
            $dataRoot,
            "--skip-initial-setup",
            "--open",
            $secondDirectory) `
        -PassThru
    if (-not $secondary.WaitForExit(12000)) {
        throw "Secondary instance did not exit after forwarding."
    }

    if ($secondary.ExitCode -ne 0) {
        throw "Secondary exited with code $($secondary.ExitCode)."
    }

    Start-Sleep -Seconds 2
    if (-not $primary.CloseMainWindow()) {
        throw "Could not request a graceful primary close."
    }

    if (-not $primary.WaitForExit(20000)) {
        throw "Primary did not close gracefully."
    }

    $sessionPath = Join-Path $dataRoot "session.json"
    if (-not (Test-Path -LiteralPath $sessionPath -PathType Leaf)) {
        throw "Session was not saved."
    }

    $session = Get-Content -LiteralPath $sessionPath -Raw |
        ConvertFrom-Json
    $activePane = $session.panes |
        Where-Object {
            $_.id -eq $session.activePaneId
        } |
        Select-Object -First 1
    $activeTab = $activePane.tabs |
        Where-Object {
            $_.id -eq $activePane.activeTabId
        } |
        Select-Object -First 1
    if ($activeTab.currentPath -ne $secondDirectory) {
        throw (
            "Forwarded path mismatch. Expected " +
            "$secondDirectory, got $($activeTab.currentPath).")
    }

    [pscustomobject]@{
        PrimaryExitCode = $primary.ExitCode
        SecondaryExitCode = $secondary.ExitCode
        ForwardedPath = $activeTab.currentPath
        SessionSaved = $true
    }
}
finally {
    if ($null -ne $primary -and -not $primary.HasExited) {
        $primary.CloseMainWindow() | Out-Null
        $primary.WaitForExit(5000) | Out-Null
    }

    $resolvedSandbox = [System.IO.Path]::GetFullPath($sandbox)
    $resolvedTemp = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::GetTempPath())
    if (
        $resolvedSandbox.StartsWith(
            $resolvedTemp,
            [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedSandbox)) {
        Remove-Item `
            -LiteralPath $resolvedSandbox `
            -Recurse `
            -Force
    }
}
