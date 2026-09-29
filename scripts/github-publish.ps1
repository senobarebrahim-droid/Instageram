<#
    Publishes this repository to GitHub for off-machine backup.

    GitHub needs YOUR credentials. This script does everything else and tells you
    exactly what is missing, instead of failing with a raw Git error.

    One-time setup on GitHub (there is no way to automate this without your login):
        1. sign in at https://github.com
        2. click New repository
        3. give it a name, for example  instageram
        4. choose Private (recommended) or Public
        5. do NOT add a README, .gitignore or licence - this repository already has them
        6. copy the URL it shows, for example:
               https://github.com/<your-name>/instageram.git

    Then run:
        powershell -File scripts\github-publish.ps1 -RemoteUrl https://github.com/<your-name>/instageram.git

    Other options:
        -DryRun        show what would happen, change nothing
        -RemoteName    use a different remote name (default: origin)
        -Force         push even when the working tree has uncommitted changes
#>
[CmdletBinding()]
param(
    [string]$RemoteUrl,
    [string]$RemoteName = 'origin',
    [string]$Branch = 'main',
    [switch]$DryRun,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_git-tools.ps1')

$projectRoot = Split-Path -Parent $PSScriptRoot
Write-Host "project root : $projectRoot"

$gitPath = Find-Git

if (-not $gitPath) {
    Write-GitMissing
    exit 2
}

Write-Host "git          : $gitPath"

Push-Location $projectRoot

try {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot '.git'))) {
        Write-Host ''
        Write-Host 'This folder is not a Git repository yet. Run first:' -ForegroundColor Yellow
        Write-Host '    powershell -File scripts\git-init.ps1'
        exit 1
    }

    $state = Get-RepositoryState -GitPath $gitPath

    if (-not $state.HasCommits) {
        Write-Host ''
        Write-Host 'The repository has no commits yet. Run first:' -ForegroundColor Yellow
        Write-Host '    powershell -File scripts\git-init.ps1'
        exit 1
    }

    Write-Host "branch       : $($state.Branch)"
    Write-Host "commits      : $((& $gitPath rev-list --count HEAD))"
    Write-Host "working tree : $(if ($state.IsClean) { 'clean' } else { "$($state.Uncommitted) uncommitted change(s)" })"

    if (-not $state.IsClean -and -not $Force) {
        Write-Host ''
        Write-Host 'There are uncommitted changes. Commit them first, or pass -Force to push anyway.' -ForegroundColor Yellow
        Write-Host 'Nothing was changed.' -ForegroundColor Yellow
        exit 1
    }

    if (-not $RemoteUrl) {
        Write-Host ''
        Write-Host 'No -RemoteUrl was given, so here is what to do:' -ForegroundColor Cyan
        Write-Host ''
        Write-Host '  1. create an EMPTY repository on GitHub (no README, no .gitignore):'
        Write-Host '         https://github.com/new'
        Write-Host '  2. copy its URL and run:'
        Write-Host '         powershell -File scripts\github-publish.ps1 -RemoteUrl <that URL>'
        Write-Host ''
        Write-Host '  This project is ready: .gitignore and .gitattributes are already in place,'
        Write-Host '  so build output, the database, logs and the 15 GB archive will not be uploaded.'
        Write-Host ''
        Write-Host 'Nothing was changed.' -ForegroundColor Yellow
        exit 2
    }

    if ($RemoteUrl -notmatch '^(https?://|git@|ssh://|[A-Za-z]:\\|/)') {
        Write-Host ''
        Write-Host "That does not look like a Git remote URL: $RemoteUrl" -ForegroundColor Yellow
        Write-Host 'Expected something like https://github.com/<name>/<repo>.git'
        exit 1
    }

    Write-Host ''
    Write-Host "remote       : $RemoteName -> $RemoteUrl"

    $existing = & $gitPath remote

    if ($existing -contains $RemoteName) {
        $current = (& $gitPath remote get-url $RemoteName).Trim()
        Write-Host "  (already configured as $current)"

        if ($current -ne $RemoteUrl) {
            if ($DryRun) {
                Write-Host "  would update the URL to $RemoteUrl"
            }
            else {
                & $gitPath remote set-url $RemoteName $RemoteUrl
                Write-Host "  URL updated to $RemoteUrl"
            }
        }
    }
    else {
        if ($DryRun) {
            Write-Host "  would add the remote"
        }
        else {
            & $gitPath remote add $RemoteName $RemoteUrl
            Write-Host '  remote added'
        }
    }

    if ($DryRun) {
        Write-Host ''
        Write-Host "Dry run: would run  git push -u $RemoteName $Branch" -ForegroundColor Cyan
        Write-Host 'Nothing was changed.' -ForegroundColor Yellow
        exit 0
    }

    Write-Host ''
    Write-Host "Pushing $Branch ..." -ForegroundColor Cyan

    & $gitPath push -u $RemoteName $Branch
    $pushExit = $LASTEXITCODE

    if ($pushExit -ne 0) {
        Write-Host ''
        Write-Host 'The push did not succeed.' -ForegroundColor Red
        Write-Host ''
        Write-Host 'Most common reasons:'
        Write-Host '  * the repository on GitHub does not exist yet        -> create it at https://github.com/new'
        Write-Host '  * GitHub asked for credentials and none are stored   -> sign in once:'
        Write-Host '        git config --global credential.helper manager'
        Write-Host '        git push -u origin main        (a browser window opens to sign in)'
        Write-Host '  * two-factor authentication is on without a token    -> create a Personal Access Token'
        Write-Host '        https://github.com/settings/tokens   and use it as the password'
        Write-Host ''
        exit $pushExit
    }

    Write-Host ''
    Write-Host 'Pushed successfully.' -ForegroundColor Green
    & $gitPath --no-pager log --oneline -n 3
    Write-Host ''
    Write-Host "Your code is now backed up on the remote '$RemoteName'." -ForegroundColor Green
    Write-Host 'Future backups are one command:  git push'
}
finally {
    Pop-Location
}
