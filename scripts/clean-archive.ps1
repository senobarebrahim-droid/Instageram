<#
    Reclaims disk space used by the archived build output.

    The archived folders contain full copies of the project, including bin\ and
    obj\ directories. Those are reproducible, and every source file plus every
    database snapshot is already stored in archive\legacy-essential-*.zip.

    The script refuses to remove anything when that compact archive is missing.

    Usage:
        powershell -File scripts\clean-archive.ps1            # dry run
        powershell -File scripts\clean-archive.ps1 -Delete    # actually remove
#>
[CmdletBinding()]
param(
    [switch]$Delete
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$archive = Join-Path $projectRoot 'archive'

if (-not (Test-Path -LiteralPath $archive)) {
    throw "archive folder not found: $archive"
}

$essential = Get-ChildItem -LiteralPath $archive -Filter 'legacy-essential-*.zip' -File |
    Select-Object -First 1

if (-not $essential) {
    throw 'The compact archive (archive\legacy-essential-*.zip) is missing. Refusing to delete anything.'
}

Write-Host "compact archive : $($essential.Name)  ($([math]::Round($essential.Length / 1MB, 1)) MB)"

$targets = Get-ChildItem -LiteralPath $archive -Directory -Recurse -Force |
    Where-Object { $_.Name -in @('bin', 'obj', 'publish', '.vs', 'cache') }

$bytes = 0
foreach ($target in $targets) {
    $bytes += (Get-ChildItem -LiteralPath $target.FullName -Recurse -File -Force -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
}

Write-Host "build folders   : $($targets.Count)"
Write-Host ("reclaimable     : {0:N1} GB" -f ($bytes / 1GB))

if (-not $Delete) {
    Write-Host ''
    Write-Host 'Dry run only. Re-run with -Delete to remove them.' -ForegroundColor Yellow
    exit 0
}

foreach ($target in $targets) {
    Remove-Item -LiteralPath $target.FullName -Recurse -Force
}

Write-Host ''
Write-Host ("Done. Reclaimed about {0:N1} GB." -f ($bytes / 1GB)) -ForegroundColor Green
