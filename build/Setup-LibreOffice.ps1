<#
.SYNOPSIS
    Downloads LibreOffice and extracts it into the project's libreoffice\ bundling folder.

.DESCRIPTION
    Produces the directory layout expected by DocumentService and the MSBuild Copy target:

        libreoffice\
            win-x64\
                program\
                    soffice.exe
                    *.dll
                    ...
                share\
                URE\
                ...

    Run once from the repository root before building.
    An existing libreoffice\win-x64\ tree is left untouched (pass -Force to replace it).

.PARAMETER Rid
    Runtime identifier to set up. Supported: win-x64 (default), win-arm64.

.PARAMETER LibreOfficeVersion
    LibreOffice release to download (default: 24.8.5).

.PARAMETER Force
    Remove and re-create an existing libreoffice\<Rid>\ tree.

.EXAMPLE
    .\build\Setup-LibreOffice.ps1
    .\build\Setup-LibreOffice.ps1 -Rid win-arm64
    .\build\Setup-LibreOffice.ps1 -Force
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid = 'win-x64',

    [string]$LibreOfficeVersion = '24.8.5',

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Resolve paths ─────────────────────────────────────────────────────────────

$repoRoot   = Split-Path $PSScriptRoot -Parent
$targetRoot = Join-Path $repoRoot "libreoffice\$Rid"

if (Test-Path $targetRoot) {
    if ($Force) {
        Write-Host "Removing existing $targetRoot ..." -ForegroundColor Yellow
        Remove-Item $targetRoot -Recurse -Force
    } else {
        $exe = Join-Path $targetRoot "program\soffice.exe"
        if (Test-Path $exe) {
            Write-Host "libreoffice\$Rid already present (soffice.exe found). Use -Force to re-download." -ForegroundColor Green
            exit 0
        }
    }
}

# ── Build download URL ────────────────────────────────────────────────────────

$archSuffix = switch ($Rid) {
    'win-x64'   { 'Win_x86-64' }
    'win-arm64' { 'Win_aarch64' }
}

# LibreOffice version components, e.g. "24.8.5" → major "24"
$verParts = $LibreOfficeVersion -split '\.'
$verMajor = $verParts[0]

$msiName = "LibreOffice_${LibreOfficeVersion}_${archSuffix}.msi"
$url     = "https://download.documentfoundation.org/libreoffice/stable/$LibreOfficeVersion/win/$($archSuffix.ToLower().Replace('win_',''))/$msiName"

Write-Host "Downloading LibreOffice $LibreOfficeVersion ($Rid) ..."
Write-Host "  URL: $url"

$msiPath = Join-Path $env:TEMP $msiName
if (Test-Path $msiPath) {
    Write-Host "  (cached at $msiPath — delete to re-download)"
} else {
    Invoke-WebRequest -Uri $url -OutFile $msiPath -UseBasicParsing
    Write-Host "  Saved to $msiPath"
}

# ── Extract via administrative install ────────────────────────────────────────
# msiexec /a performs an "administrative install": unpacks the MSI payload to a
# directory tree without registering anything in the OS. No elevation required.

$adminDir = Join-Path $env:TEMP "lo-admin-$Rid"
if (Test-Path $adminDir) { Remove-Item $adminDir -Recurse -Force }
New-Item -ItemType Directory -Path $adminDir | Out-Null

Write-Host "Extracting MSI payload to $adminDir (this takes ~1-2 minutes) ..."
$msiArgs = @('/a', $msiPath, '/qn', "TARGETDIR=$adminDir")
$proc = Start-Process msiexec -ArgumentList $msiArgs -Wait -PassThru
if ($proc.ExitCode -ne 0) {
    throw "msiexec /a failed with exit code $($proc.ExitCode). Check that the MSI is not corrupted."
}

# ── Locate extracted program\ folder ─────────────────────────────────────────
# After admin-install the layout is:
#   <adminDir>\LibreOffice <version>\program\soffice.exe   (typical)
# or
#   <adminDir>\program\soffice.exe                         (rare)

$programDir = Get-ChildItem -Path $adminDir -Filter 'program' -Recurse -Directory |
              Where-Object { Test-Path (Join-Path $_.FullName 'soffice.exe') } |
              Select-Object -First 1 -ExpandProperty FullName

if (-not $programDir) {
    throw "Could not locate program\soffice.exe inside the extracted payload at $adminDir. " +
          "Directory listing:`n$(Get-ChildItem $adminDir -Recurse | Select-Object FullName | Format-Table -HideTableHeaders | Out-String)"
}

$loRoot = Split-Path $programDir -Parent   # parent of program\ = LibreOffice install root

Write-Host "Found LibreOffice root: $loRoot"

# ── Copy to project libreoffice\<Rid>\ ───────────────────────────────────────

New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null

Write-Host "Copying to $targetRoot ..."
Copy-Item -Path "$loRoot\*" -Destination $targetRoot -Recurse -Force

# ── Verify ────────────────────────────────────────────────────────────────────

$soffice = Join-Path $targetRoot "program\soffice.exe"
if (-not (Test-Path $soffice)) {
    throw "Setup failed: soffice.exe not found at expected path $soffice"
}

$size = (Get-ChildItem $targetRoot -Recurse -File | Measure-Object Length -Sum).Sum
$sizeMB = [math]::Round($size / 1MB, 0)

Write-Host ""
Write-Host "Done! LibreOffice $LibreOfficeVersion ($Rid) is ready." -ForegroundColor Green
Write-Host "  Path   : $targetRoot"
Write-Host "  Size   : ~${sizeMB} MB"
Write-Host "  Binary : $soffice"
Write-Host ""
Write-Host "Build the project to copy this tree to the output directory," -ForegroundColor Cyan
Write-Host "then run the Diagnostics_LibreOfficeBinaryProbe test to confirm." -ForegroundColor Cyan

# ── Clean up temp extraction ──────────────────────────────────────────────────
Remove-Item $adminDir -Recurse -Force -ErrorAction SilentlyContinue
