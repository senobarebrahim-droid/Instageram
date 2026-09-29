$ErrorActionPreference = 'Stop'

try {
    # Phase 1 fix: paths are derived from this script's own location so the
    # project can live on any drive. These used to be hard-coded to
    # 'C:\Ebrahim senobar2\instageram' and 'C:\Instageram\publish\...',
    # both of which do not exist on this machine.
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $root = Join-Path $scriptDirectory 'src\Instageram'
    $out  = Join-Path $scriptDirectory 'publish\Instageram-Portable'
    $exe = Join-Path $out 'Instageram.exe'
    $stamp = Join-Path $out '.source-hash.txt'

    $preferred = @(
        (Join-Path $root 'Instageram.csproj'),
        (Join-Path $root 'src\Instageram\Instageram.csproj')
    )

    $project = $preferred |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1

    if (-not $project) {
        $projects = @(
            Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.csproj' |
                Where-Object {
                    $_.FullName -notmatch '\\(bin|obj|publish|\.git)\\'
                }
        )

        if ($projects.Count -ne 1) {
            throw "Expected exactly one project, found $($projects.Count). Check the project folder."
        }

        $project = $projects[0].FullName
    }

    $files = @(
        Get-ChildItem -LiteralPath $root -Recurse -File |
            Where-Object {
                $_.FullName -notmatch '\\(bin|obj|publish|logs|data|reports|exports|cache|\.git)\\' -and
                $_.Extension -in @('.cs', '.xaml', '.csproj', '.resx', '.props', '.targets', '.json')
            } |
            Sort-Object FullName
    )

    if ($files.Count -eq 0) {
        throw 'No source files were found.'
    }

    $entries = foreach ($file in $files) {
        $relative = $file.FullName.Substring($root.Length)
        $fileHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        "$relative|$fileHash"
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($entries -join "`n"))
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $currentHash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }

    $savedHash = ''
    if (Test-Path -LiteralPath $stamp -PathType Leaf) {
        $savedHash = [System.IO.File]::ReadAllText($stamp).Trim()
    }

    if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or $savedHash -ne $currentHash) {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            throw '.NET SDK is required to rebuild the application on this computer.'
        }

        $sdks = @(& dotnet --list-sdks)
        if ($LASTEXITCODE -ne 0 -or $sdks.Count -eq 0) {
            throw '.NET SDK was not found. Install the SDK before updating from source.'
        }

        New-Item -ItemType Directory -Path $out -Force | Out-Null

        Write-Host 'Source changes detected. Publishing INSTAGERAM...' -ForegroundColor Cyan
        Write-Host "Project: $project"
        Write-Host "Output:  $out"

        & dotnet publish $project `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -p:PublishSingleFile=false `
            -o $out

        if ($LASTEXITCODE -ne 0) {
            throw 'Publish failed. See the build errors above. The app was not started.'
        }

        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
            throw "Publish finished, but Instageram.exe was not found at: $exe"
        }

        [System.IO.File]::WriteAllText(
            $stamp,
            $currentHash,
            (New-Object System.Text.UTF8Encoding($false))
        )

        Write-Host 'Update completed successfully.' -ForegroundColor Green
    }
    else {
        Write-Host 'No source changes. Starting the existing version...' -ForegroundColor Green
    }

    Start-Process -FilePath $exe -WorkingDirectory $out
}
catch {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'INSTAGERAM was not started.' -ForegroundColor Yellow
    Read-Host 'Press Enter to close this window' | Out-Null
}