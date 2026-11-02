# Stages the experimental VHF driver for non-installing validation.
# Does not generate a catalog, sign, install, or execute the driver.
[CmdletBinding()]
param(
    [string]$DriverDirectory = $PSScriptRoot,
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'G13Reborn-Vhf-Package-Check'),
    [switch]$RequireWdkTools
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$DriverDirectory = (Resolve-Path -LiteralPath $DriverDirectory).Path
$sourceInf = Join-Path $DriverDirectory 'G13RebornVhid.inf'
if (-not (Test-Path -LiteralPath $sourceInf -PathType Leaf)) {
    throw "Missing INF: $sourceInf"
}

$drivers = @(Get-ChildItem -LiteralPath $DriverDirectory -Filter G13RebornVhid.sys -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/]packages[\\/]' } |
    Sort-Object LastWriteTimeUtc -Descending)
if ($drivers.Count -eq 0) {
    throw 'Build G13RebornVhid.sys before running the package validation script.'
}
if ($drivers[0].Length -eq 0) {
    throw 'The native driver binary is empty.'
}

# This is a narrow source-package sanity check, not a replacement for InfVerif.
$inf = [IO.File]::ReadAllText($sourceInf)
$requiredLines = @(
    '(?im)^\[Version\]\s*$',
    '(?im)^\s*Signature\s*=\s*"\$WINDOWS NT\$"\s*$',
    '(?im)^\s*CatalogFile\s*=\s*G13RebornVhid\.cat\s*$',
    '(?im)^\s*%DeviceDesc%\s*=\s*DriverInstall\s*,\s*ROOT\\G13REBORN_VHID\s*$',
    '(?im)^\s*HKR\s*,\s*,\s*"LowerFilters"\s*,\s*0x00010000\s*,\s*"vhf"\s*$'
)
foreach ($pattern in $requiredLines) {
    if (-not [regex]::IsMatch($inf, $pattern)) {
        throw "Draft INF is missing a required VHF/package declaration: $pattern"
    }
}

New-Item -Path $OutputDirectory -ItemType Directory -Force | Out-Null
# Avoid stale catalogs and files from earlier checks.
Get-ChildItem -LiteralPath $OutputDirectory -File | Remove-Item -Force
$stagedInf = Join-Path $OutputDirectory 'G13RebornVhid.inf'
Copy-Item -LiteralPath $sourceInf -Destination $stagedInf
Copy-Item -LiteralPath $drivers[0].FullName -Destination (Join-Path $OutputDirectory 'G13RebornVhid.sys')
Write-Host "Staged unsigned INF + SYS for validation at $OutputDirectory"
Write-Host "Source SYS: $($drivers[0].FullName) ($($drivers[0].Length) bytes)"

function Find-WdkTool([string]$name) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }

    $roots = @(
        (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits\10'),
        (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Windows Kits\10'),
        (Join-Path $DriverDirectory 'packages')
    ) | Select-Object -Unique

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        foreach ($folder in @('tools', 'bin', '.')) {
            $search = if ($folder -eq '.') { $root } else { Join-Path $root $folder }
            if (-not (Test-Path -LiteralPath $search -PathType Container)) { continue }
            $found = Get-ChildItem -LiteralPath $search -Filter $name -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '[\\/]x64[\\/]' } |
                Sort-Object FullName -Descending |
                Select-Object -First 1
            if ($found) { return $found.FullName }
        }
    }
    return $null
}

$infVerif = Find-WdkTool 'InfVerif.exe'
$inf2cat = Find-WdkTool 'Inf2Cat.exe'
$infResult = 'NOT RUN: InfVerif.exe not available on this runner'
$catResult = 'NOT RUN: Inf2Cat.exe not available on this runner'

if ($infVerif) {
    Write-Host "Running InfVerif: $infVerif"
    & $infVerif /w /v $stagedInf
    if ($LASTEXITCODE -ne 0) { throw "InfVerif failed with exit code $LASTEXITCODE" }
    $infResult = 'PASSED: InfVerif /w'
}
else {
    Write-Warning $infResult
}

if ($inf2cat) {
    Write-Host "Running Inf2Cat: $inf2cat"
    # 10_GE_X64: Windows 11 24H2; 10_25H2_X64: Windows 11 25H2.
    # /nocat intentionally does not create a signed or unsigned catalog.
    & $inf2cat "/driver:$OutputDirectory" /os:10_GE_X64,10_25H2_X64 /nocat /verbose
    if ($LASTEXITCODE -ne 0) { throw "Inf2Cat failed with exit code $LASTEXITCODE" }
    $catResult = 'PASSED: Inf2Cat /nocat (Windows 11 24H2 and 25H2 x64)'
}
else {
    Write-Warning $catResult
}

if ($env:GITHUB_STEP_SUMMARY) {
    @(
        '### G13Reborn experimental VHF package validation',
        '',
        '- Native .sys: compiled and staged alongside the draft INF.',
        "- INF structure: basic source-level declarations present.",
        "- INF validation: $infResult",
        "- Package signability: $catResult",
        '- No catalog was generated, no signing or installation occurred.',
        '- Even successful static checks do not prove the driver works in a kernel.'
    ) | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
}

if ($RequireWdkTools -and ((-not $infVerif) -or (-not $inf2cat))) {
    throw 'Full package validation requested, but one or both official WDK tools are missing.'
}
Write-Host 'Package staging/check completed. See explicit InfVerif and Inf2Cat results above.'
