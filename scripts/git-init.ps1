<#
    Prepares the INSTAGERAM project for Git and makes the first commit.

    Git is NOT installed on this machine, so the script checks first and tells
    you exactly what to install instead of failing with a confusing error.

    Usage:
        powershell -File scripts\git-init.ps1
        powershell -File scripts\git-init.ps1 -Message "Custom first commit"
#>
[CmdletBinding()]
param(
    [string]$Message = 'INSTAGERAM: portable campaign manager with tests'
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
Write-Host "project root : $projectRoot"

# Shared helper: locates Git even when the current session PATH is stale.
. (Join-Path $PSScriptRoot '_git-tools.ps1')

$gitPath = Find-Git

if (-not $gitPath) {
    Write-GitMissing
    exit 2
}

# Use the resolved path for every call, so the script works even when the
# current session PATH is stale.
function git { & $gitPath @args }

Write-Host "git          : $gitPath"
& $gitPath --version

if (-not (Test-Path -LiteralPath (Join-Path $projectRoot '.gitignore'))) {
    throw '.gitignore is missing; refusing to create a repository that would track build output.'
}

Push-Location $projectRoot

try {
    if (Test-Path -LiteralPath (Join-Path $projectRoot '.git')) {
        Write-Host 'a repository already exists here, adding a commit instead.'
    }
    else {
        git init
        if ($LASTEXITCODE -ne 0) { throw 'git init failed' }
    }

    git add --all
    if ($LASTEXITCODE -ne 0) { throw 'git add failed' }

    git -c user.name='INSTAGERAM' -c user.email='instageram@localhost' commit -m $Message
    if ($LASTEXITCODE -ne 0) { throw 'git commit failed (nothing to commit?)' }

    git branch -M main

    Write-Host ''
    Write-Host 'Recent commits:' -ForegroundColor Green
    & $gitPath --no-pager log --oneline -n 5

    Write-Host ''
    Write-Host 'Working tree:' -ForegroundColor Green
    & $gitPath status --short
}
finally {
    Pop-Location
}
