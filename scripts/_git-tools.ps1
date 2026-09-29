<#
    Shared Git helper. Dot-source it from a script:

        . (Join-Path $PSScriptRoot '_git-tools.ps1')

    A freshly installed Git updates the machine PATH, but a terminal that was
    already open keeps the old value, so the usual install locations are checked
    as well instead of declaring Git missing.
#>

function Find-Git {
    $command = Get-Command git -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @(
        (Join-Path $env:ProgramFiles 'Git\cmd\git.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Git\cmd\git.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Git\cmd\git.exe')
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }

    return $null
}

function Write-GitMissing {
    Write-Host ''
    Write-Host 'Git was not found.' -ForegroundColor Yellow
    Write-Host 'Install it, then run this script again:'
    Write-Host '    winget install --id Git.Git -e --source winget'
    Write-Host '    or download: https://git-scm.com/download/win'
    Write-Host ''
    Write-Host 'Nothing was changed.' -ForegroundColor Yellow
}

function Get-RepositoryState {
    param([Parameter(Mandatory = $true)][string]$GitPath)

    $branch = (& $GitPath rev-parse --abbrev-ref HEAD 2>$null)

    if ($LASTEXITCODE -ne 0) {
        return [pscustomobject]@{ HasCommits = $false; Branch = ''; IsClean = $false; Uncommitted = 0 }
    }

    $uncommitted = @(& $GitPath status --porcelain 2>$null).Count

    return [pscustomobject]@{
        HasCommits  = $true
        Branch      = $branch
        IsClean     = ($uncommitted -eq 0)
        Uncommitted = $uncommitted
    }
}
