<#
.SYNOPSIS
  Downloads public, Apache-2.0-licensed sample iRacing recordings into samples\ibt\ (git-ignored).
  Each file is pinned to a source commit and verified against its SHA-256 before it is kept.

.EXAMPLE
  scripts\fetch-sample-ibt.ps1            # default set, about 46 MB (5 cars, 4 tracks)
  scripts\fetch-sample-ibt.ps1 -Large     # also the large set, about 300 MB more
  scripts\fetch-sample-ibt.ps1 -Dir D:\ibt
#>
param(
    [switch]$Large,
    [string]$Dir
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = Split-Path -Parent $PSScriptRoot
$manifest = Join-Path $root 'scripts\sample-ibt-manifest.tsv'
if (-not $Dir) { $Dir = Join-Path $root 'samples\ibt' }
$sets = @('default') + $(if ($Large) { @('large') } else { @() })

New-Item -ItemType Directory -Force -Path $Dir | Out-Null
$failed = $false

foreach ($line in Get-Content $manifest) {
    if (-not $line -or $line.StartsWith('#')) { continue }
    $set, $file, $hash, $bytes, $license, $url = $line -split "`t"
    if ($sets -notcontains $set) { continue }

    $target = Join-Path $Dir $file
    if ((Test-Path $target) -and ((Get-FileHash $target -Algorithm SHA256).Hash -eq $hash.ToUpperInvariant())) {
        Write-Host "ok       $file"
        continue
    }

    Write-Host ("fetching {0} ({1} MiB, {2})" -f $file, [math]::Floor([long]$bytes / 1MB), $license)
    $tmp = "$target.part"
    try {
        Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing
        if ((Get-FileHash $tmp -Algorithm SHA256).Hash -ne $hash.ToUpperInvariant()) { throw 'checksum mismatch' }
        Move-Item -Force $tmp $target
    }
    catch {
        Remove-Item -Force -ErrorAction SilentlyContinue $tmp
        Write-Warning "FAILED   $file ($_): $url"
        $failed = $true
    }
}

Write-Host "samples are in $Dir (sources and licenses: scripts\sample-ibt-manifest.tsv)"
if ($failed) { exit 1 }
