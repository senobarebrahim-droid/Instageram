<#
    Builds the portable INSTAGERAM release and writes the packaged manifest.

    Steps:
      1. runs the test suite (unless -SkipTests)
      2. publishes a self-contained win-x64 build
      3. hashes every produced file with SHA256
      4. writes manifest.json next to Instageram.exe

    Usage:
        powershell -File scripts\build-release.ps1
        powershell -File scripts\build-release.ps1 -SkipTests
        powershell -File scripts\build-release.ps1 -OutputDirectory D:\somewhere
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputDirectory,
    [switch]$SkipTests,

    # Removes the runtime state (database, settings, logs, exports, backups)
    # from the output folder after a successful publish, so the package can be
    # handed to another person. Off by default: on your own installation this
    # would delete your data.
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\Instageram\Instageram.csproj'
$solution = Join-Path $projectRoot 'Instageram.sln'
$descriptorPath = Join-Path $projectRoot 'manifest.json'

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $projectRoot 'publish\Instageram-Portable'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK is required to build the release.'
}

if (-not (Test-Path -LiteralPath $descriptorPath)) {
    throw "release descriptor not found: $descriptorPath"
}

$descriptor = Get-Content -LiteralPath $descriptorPath -Raw | ConvertFrom-Json
$version = $descriptor.application_version

Write-Host ''
Write-Host 'INSTAGERAM release build' -ForegroundColor Cyan
Write-Host "  version : $version"
Write-Host "  project : $project"
Write-Host "  output  : $OutputDirectory"
Write-Host ''

if (-not $SkipTests) {
    Write-Host 'Running the test suite...' -ForegroundColor Cyan

    & dotnet test $solution -c $Configuration --nologo -v quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'The tests failed, so no release was produced.'
    }
}
else {
    Write-Host 'Test suite skipped (-SkipTests).' -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'Publishing...' -ForegroundColor Cyan

& dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $OutputDirectory

if ($LASTEXITCODE -ne 0) {
    throw 'Publish failed.'
}

$exe = Join-Path $OutputDirectory 'Instageram.exe'

if (-not (Test-Path -LiteralPath $exe)) {
    throw "Publish finished but Instageram.exe was not found at: $exe"
}

Write-Host ''
Write-Host 'Hashing the output...' -ForegroundColor Cyan

# The manifest describes the APPLICATION files. User content and machine
# specific state is never hashed: whole folders for data, logs, exports,
# reports and cache, plus the two files the user owns inside config\.
# Shipped resources such as config\settings.template.json ARE hashed.
$excludedFolders = '\\(data|logs|exports|reports|cache)\\'
$excludedFiles = @('manifest.json', '.source-hash.txt', 'settings.json', 'instagram_users.txt')

$files = Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File |
    Where-Object {
        $_.Name -notin $excludedFiles -and
        $_.FullName -notmatch $excludedFolders
    } |
    Sort-Object FullName

$entries = foreach ($file in $files) {
    [pscustomobject]@{
        path   = $file.FullName.Substring($OutputDirectory.Length).TrimStart('\')
        size   = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
}

$manifest = [pscustomobject]@{
    application_name        = $descriptor.application_name
    application_version     = $version
    database_schema_version = $descriptor.database_schema_version
    portable_mode           = $descriptor.portable_mode
    runtime                 = $Runtime
    configuration           = $Configuration
    generated_at            = (Get-Date).ToUniversalTime().ToString('o')
    file_count              = @($entries).Count
    files                   = @($entries)
}

$manifestPath = Join-Path $OutputDirectory 'manifest.json'
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$exeEntry = $entries | Where-Object { $_.path -eq 'Instageram.exe' }
$totalSize = ($entries | Measure-Object -Property size -Sum).Sum

Write-Host ''
Write-Host "Manifest written: $manifestPath"
Write-Host "  version      : $version"
Write-Host "  files hashed : $(@($entries).Count)"
Write-Host ("  package size : {0:N1} MB" -f ($totalSize / 1MB))
Write-Host "  exe sha256   : $($exeEntry.sha256)"

# ---------------------------------------------------------------- clean pack
if ($Clean) {
    Write-Host ''
    Write-Host 'Removing runtime state so the package can be handed on...' -ForegroundColor Cyan

    # Files the user owns, plus everything the application produced while it ran.
    $targets = @(
        'data\database.db',
        'data\database.db-wal',
        'data\database.db-shm',
        'data\corrupt',
        'config\settings.json',
        'config\instagram_users.txt'
    )

    $removed = 0
    $bytes = 0

    foreach ($relative in $targets) {
        $path = Join-Path $OutputDirectory $relative

        if (-not (Test-Path -LiteralPath $path)) { continue }

        $item = Get-Item -LiteralPath $path -Force
        $bytes += (Get-ChildItem -LiteralPath $path -Recurse -File -Force -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum

        Remove-Item -LiteralPath $path -Recurse -Force
        $removed++
        Write-Host "  removed $relative"
    }

    # Folder contents only: the folders themselves are recreated on first run.
    foreach ($folder in @('data\backups', 'data\campaigns', 'data\projects', 'logs', 'exports', 'reports', 'cache')) {
        $path = Join-Path $OutputDirectory $folder

        if (-not (Test-Path -LiteralPath $path)) { continue }

        foreach ($child in Get-ChildItem -LiteralPath $path -Force) {
            $bytes += (Get-ChildItem -LiteralPath $child.FullName -Recurse -File -Force -ErrorAction SilentlyContinue |
                Measure-Object -Property Length -Sum).Sum
            $removed++
        }

        Get-ChildItem -LiteralPath $path -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  emptied $folder\"
    }

    Write-Host ("  {0} item(s) removed, {1:N1} KB freed" -f $removed, ($bytes / 1KB))
    Write-Host '  kept: data\countries.json, data\target_markets.csv, config\settings.template.json' -ForegroundColor Green
}
else {
    Write-Host ''
    Write-Host 'Runtime state was kept. Use -Clean to produce a package for someone else.' -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'Release ready.' -ForegroundColor Green
