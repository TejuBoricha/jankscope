<#
.SYNOPSIS
    Restores the pinned third-party binaries listed in third_party/**/*.lock.json and verifies them.

.DESCRIPTION
    Developer/build-time only (the product never downloads anything at run time, HC-7).
    - If the file is present and its SHA-256 matches the lock file, nothing is downloaded.
    - If it is missing, it is downloaded from the lock file's source URL ONLY when -Download is passed,
      then verified; a size or hash mismatch deletes the file and fails.
    Each lock file records the owner's approval for that exact file. Add a new lock file only with the owner's yes.

.EXAMPLE
    ./tools/fetch-third-party.ps1            # verify only
    ./tools/fetch-third-party.ps1 -Download  # fetch missing files, then verify
#>
[CmdletBinding()]
param([switch]$Download)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failed = $false

foreach ($lockFile in Get-ChildItem -Path (Join-Path $root 'third_party') -Filter '*.lock.json' -Recurse) {
    $lock = Get-Content $lockFile.FullName -Raw | ConvertFrom-Json
    $target = Join-Path $lockFile.Directory.FullName $lock.file
    $name = "$($lock.name) $($lock.version)"

    if (-not (Test-Path $target)) {
        if (-not $Download) {
            Write-Host "MISSING  $name -> run with -Download (approved source: $($lock.source))" -ForegroundColor Yellow
            $failed = $true
            continue
        }
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Write-Host "Downloading $name from $($lock.source)"
        Invoke-WebRequest -Uri $lock.source -OutFile $target -UseBasicParsing
    }

    $size = (Get-Item $target).Length
    $hash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($size -ne $lock.sizeBytes -or $hash -ne $lock.sha256) {
        Write-Host "MISMATCH $name (size $size, sha256 $hash) - deleting" -ForegroundColor Red
        Remove-Item $target -Force
        $failed = $true
        continue
    }
    Write-Host "OK       $name  sha256 $hash" -ForegroundColor Green
}

if ($failed) { exit 1 }
