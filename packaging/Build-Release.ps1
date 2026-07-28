param(
    [string]$Version = "1.0.6",
    [string]$UpdateDrop = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$artifactsRoot = Join-Path $projectRoot "artifacts"
$stagingRoot = Join-Path $artifactsRoot "staging"
$releaseRoot = Join-Path $artifactsRoot "release"
$packageName = "MYTC-v$Version-win-x64"
$packageRoot = Join-Path $stagingRoot $packageName
$maintenancePublish = Join-Path $stagingRoot "maintenance-single-file"
$archivePath = Join-Path $releaseRoot "$packageName.zip"
$dotnet = Join-Path $projectRoot ".tools\dotnet\dotnet.exe"

if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "Project dotnet runtime not found: $dotnet"
}

$resolvedArtifacts = [System.IO.Path]::GetFullPath($artifactsRoot)
foreach ($candidate in @($packageRoot, $maintenancePublish)) {
    $resolvedCandidate = [System.IO.Path]::GetFullPath($candidate)
    if (-not $resolvedCandidate.StartsWith(
            $resolvedArtifacts + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside artifacts: $resolvedCandidate"
    }

    if (Test-Path -LiteralPath $resolvedCandidate) {
        Remove-Item -LiteralPath $resolvedCandidate -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $maintenancePublish -Force | Out-Null
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

& $dotnet publish `
    (Join-Path $projectRoot "src\MYTC.App\MYTC.App.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:DebugType=none `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    -o $packageRoot
if ($LASTEXITCODE -ne 0) {
    throw "MYTC application publish failed."
}

& $dotnet publish `
    (Join-Path $projectRoot "src\MYTC.Maintenance\MYTC.Maintenance.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    -o $maintenancePublish
if ($LASTEXITCODE -ne 0) {
    throw "MYTC maintenance tool publish failed."
}

$maintenanceExecutable = Join-Path $maintenancePublish "MYTC.Maintenance.exe"
if (-not (Test-Path -LiteralPath $maintenanceExecutable -PathType Leaf)) {
    throw "Single-file maintenance publish did not produce MYTC.Maintenance.exe."
}

Copy-Item `
    -LiteralPath $maintenanceExecutable `
    -Destination (Join-Path $packageRoot "MYTC.Maintenance.exe") `
    -Force
Copy-Item `
    -LiteralPath (Join-Path $PSScriptRoot "PRODUCTION-GUIDE.txt") `
    -Destination (Join-Path $packageRoot "PRODUCTION-GUIDE.txt") `
    -Force

$dataPath = Join-Path $packageRoot "data"
if (Test-Path -LiteralPath $dataPath) {
    Remove-Item -LiteralPath $dataPath -Recurse -Force
}

$manifestFiles = @(
    Get-ChildItem -LiteralPath $packageRoot -File -Recurse |
        Where-Object {
            $_.Name -ne "MYTC.update.json"
        } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring(
                $packageRoot.Length).TrimStart(
                    [char[]]@("\", "/")).Replace("\", "/")
            [ordered]@{
                path = $relative
                length = $_.Length
                sha256 = (Get-FileHash `
                    -LiteralPath $_.FullName `
                    -Algorithm SHA256).Hash
            }
        }
)

$manifest = [ordered]@{
    schemaVersion = 1
    productId = "MYTC"
    version = $Version
    architecture = "win-x64"
    files = $manifestFiles
}
$manifestPath = Join-Path $packageRoot "MYTC.update.json"
$manifest |
    ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath $manifestPath -Encoding utf8

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive `
    -LiteralPath $packageRoot `
    -DestinationPath $archivePath `
    -CompressionLevel Optimal

$archiveHash = (Get-FileHash `
    -LiteralPath $archivePath `
    -Algorithm SHA256).Hash

if (-not [string]::IsNullOrWhiteSpace($UpdateDrop)) {
    $resolvedDrop = [System.IO.Path]::GetFullPath($UpdateDrop)
    New-Item -ItemType Directory -Path $resolvedDrop -Force | Out-Null
    Copy-Item `
        -LiteralPath $archivePath `
        -Destination (Join-Path $resolvedDrop (Split-Path $archivePath -Leaf)) `
        -Force
}

[pscustomobject]@{
    Version = $Version
    PackageDirectory = $packageRoot
    Archive = $archivePath
    Sha256 = $archiveHash
    FileCount = $manifestFiles.Count + 1
}
