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
    [switch]$SkipTests
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
Write-Host ''
Write-Host 'Release ready.' -ForegroundColor Green
