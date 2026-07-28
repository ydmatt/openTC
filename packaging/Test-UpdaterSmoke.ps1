param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$sandbox = Join-Path (
    [System.IO.Path]::GetTempPath()) (
    "MYTC.UpdaterSmoke." + [guid]::NewGuid().ToString("N"))
$installRoot = Join-Path $sandbox "install"
$stagedRoot = Join-Path $sandbox "staged"
$stateRoot = Join-Path $sandbox "state"
$hostRoot = Join-Path $sandbox "host"
$resolvedPackage = [System.IO.Path]::GetFullPath($PackageRoot)

try {
    New-Item `
        -ItemType Directory `
        -Path $installRoot,$stagedRoot,$stateRoot,$hostRoot `
        -Force |
        Out-Null
    Copy-Item `
        -Path (Join-Path $resolvedPackage "*") `
        -Destination $installRoot `
        -Recurse `
        -Force
    Copy-Item `
        -Path (Join-Path $resolvedPackage "*") `
        -Destination $stagedRoot `
        -Recurse `
        -Force

    $dataRoot = Join-Path $installRoot "data"
    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
    Set-Content `
        -LiteralPath (Join-Path $dataRoot "session.json") `
        -Value "user-data-must-survive" `
        -Encoding utf8

    $guidePath = Join-Path $stagedRoot "PRODUCTION-GUIDE.txt"
    Add-Content `
        -LiteralPath $guidePath `
        -Value "`r`nUPDATER-SMOKE-V1.0.1" `
        -Encoding utf8

    $manifestPath = Join-Path $stagedRoot "MYTC.update.json"
    $manifest = Get-Content -LiteralPath $manifestPath -Raw |
        ConvertFrom-Json
    $manifest.version = "1.0.1"
    $guideEntry = $manifest.files |
        Where-Object {
            $_.path -eq "PRODUCTION-GUIDE.txt"
        } |
        Select-Object -First 1
    if ($null -eq $guideEntry) {
        throw "Manifest does not contain PRODUCTION-GUIDE.txt."
    }

    $guideItem = Get-Item -LiteralPath $guidePath
    $guideEntry.length = $guideItem.Length
    $guideEntry.sha256 = (Get-FileHash `
        -LiteralPath $guidePath `
        -Algorithm SHA256).Hash
    $manifest |
        ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath $manifestPath -Encoding utf8

    $updaterHost = Join-Path $hostRoot "MYTC.Maintenance.exe"
    Copy-Item `
        -LiteralPath (
            Join-Path $installRoot "MYTC.Maintenance.exe") `
        -Destination $updaterHost
    $updater = Start-Process `
        -FilePath $updaterHost `
        -ArgumentList @(
            "--apply-update",
            "--install-root",
            $installRoot,
            "--staged-root",
            $stagedRoot,
            "--pid",
            "2147483647",
            "--state-root",
            $stateRoot,
            "--silent") `
        -PassThru `
        -Wait `
        -WindowStyle Hidden
    if ($updater.ExitCode -ne 0) {
        throw "Updater exited with code $($updater.ExitCode)."
    }

    $installedManifest = Get-Content `
        -LiteralPath (
            Join-Path $installRoot "MYTC.update.json") `
        -Raw |
        ConvertFrom-Json
    if ($installedManifest.version -ne "1.0.1") {
        throw "Installed manifest version was not updated."
    }

    $guideText = Get-Content `
        -LiteralPath (
            Join-Path $installRoot "PRODUCTION-GUIDE.txt") `
        -Raw
    if (-not $guideText.Contains("UPDATER-SMOKE-V1.0.1")) {
        throw "Updated file content was not installed."
    }

    $dataText = Get-Content `
        -LiteralPath (
            Join-Path $dataRoot "session.json") `
        -Raw
    if (-not $dataText.Contains("user-data-must-survive")) {
        throw "Portable data did not survive the update."
    }

    $backupGuide = Get-ChildItem `
        -LiteralPath (
            Join-Path $stateRoot "backups") `
        -Filter "PRODUCTION-GUIDE.txt" `
        -File `
        -Recurse |
        Select-Object -First 1
    if ($null -eq $backupGuide) {
        throw "Old program backup was not created."
    }

    [pscustomobject]@{
        UpdaterExitCode = $updater.ExitCode
        InstalledVersion = $installedManifest.version
        DataPreserved = $true
        BackupCreated = $true
        LogCount = @(
            Get-ChildItem `
                -LiteralPath (
                    Join-Path $stateRoot "logs") `
                -Filter "*.log" `
                -File).Count
    }
}
finally {
    $resolvedSandbox = [System.IO.Path]::GetFullPath($sandbox)
    $resolvedTemp = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::GetTempPath())
    if (
        $resolvedSandbox.StartsWith(
            $resolvedTemp,
            [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedSandbox)) {
        $removed = $false
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            try {
                Remove-Item `
                    -LiteralPath $resolvedSandbox `
                    -Recurse `
                    -Force
                $removed = $true
                break
            }
            catch {
                Start-Sleep -Milliseconds 250
            }
        }

        if (-not $removed) {
            Write-Warning (
                "Updater smoke sandbox is still temporarily locked: " +
                $resolvedSandbox)
        }
    }
}
