<#
.SYNOPSIS
    Publish ClassLapse as a single-file Windows .exe.

.DESCRIPTION
    Default mode: self-contained single .exe (~65MB), no runtime needed on
    target. Use -FrameworkDependent for a ~5MB .exe that requires the user
    to have the .NET 8 Runtime installed already.

    Self-contained is the default because the typical deployment target
    (a Seewo IFP running Windows 10/11 LTSC) won't have .NET 8 Runtime
    out of the box.

.PARAMETER FrameworkDependent
    Don't bundle the runtime. Result is ~5MB but requires .NET 8 Runtime
    on the target machine.

.PARAMETER OutputDir
    Output directory. Default: publish/

.PARAMETER Zip
    After building, package OutputDir into ClassLapse-v{version}-win-x64.zip
    next to it. Adds README + deployment.md from docs/ if present.

.EXAMPLE
    ./publish.ps1                  # self-contained, into ./publish/
    ./publish.ps1 -FrameworkDependent
    ./publish.ps1 -Zip             # also produce a .zip ready to drop on a USB stick
#>

[CmdletBinding()]
param(
    [switch]$FrameworkDependent,
    [switch]$Zip,
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/ClassLapse/ClassLapse.csproj"

# Probe version from csproj. Use regex instead of [xml] parsing: PowerShell 5.x
# Get-Content defaults to the system code page (GBK on Chinese Windows), which
# mangles UTF-8 csproj content with Chinese text and breaks XML parsing.
$csprojText = Get-Content $projectPath -Raw -Encoding UTF8
$version = "0.1.0"
if ($csprojText -match '<Version>([^<]+)</Version>') {
    $version = $matches[1]
}

Write-Host "ClassLapse v$version" -ForegroundColor Cyan

$dotnetArgs = @(
    "publish", $projectPath,
    "-c", "Release",
    "-r", "win-x64",
    "-o", $OutputDir,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=embedded",
    "-p:EnableCompressionInSingleFile=true"
)

if ($FrameworkDependent) {
    $dotnetArgs += @("--self-contained", "false")
    Write-Host "Mode: framework-dependent (~5MB, requires .NET 8 Runtime on target)" -ForegroundColor DarkGray
} else {
    $dotnetArgs += @("--self-contained", "true")
    Write-Host "Mode: self-contained (~65MB, no runtime needed on target)" -ForegroundColor DarkGray
}

& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed."
    exit 1
}

$exe = Join-Path $OutputDir "ClassLapse.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Publish completed but $exe was not produced."
    exit 1
}

$size = (Get-Item $exe).Length / 1MB
Write-Host ("Built {0} ({1:N1} MB)" -f $exe, $size) -ForegroundColor Green

# Drop user docs next to the .exe so a USB-stick deploy is self-explanatory
$readme = Join-Path $repoRoot "docs/README.md"
$deployment = Join-Path $repoRoot "docs/deployment.md"
if (Test-Path $readme) { Copy-Item $readme (Join-Path $OutputDir "README.md") -Force }
if (Test-Path $deployment) { Copy-Item $deployment (Join-Path $OutputDir "deployment.md") -Force }

if ($Zip) {
    $zipName = "ClassLapse-v$version-win-x64.zip"
    # Put the zip next to OutputDir, or in the current directory when
    # OutputDir is a bare relative name (Split-Path returns "" in PS 5.x
    # and Join-Path "" $zipName then throws "path cannot be empty").
    $outputParent = Split-Path -Path $OutputDir -Parent
    if ([string]::IsNullOrEmpty($outputParent)) {
        $zipPath = $zipName
    } else {
        $zipPath = Join-Path $outputParent $zipName
    }
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $OutputDir "*") -DestinationPath $zipPath
    $zipSize = (Get-Item $zipPath).Length / 1MB
    Write-Host ("Packaged {0} ({1:N1} MB)" -f $zipPath, $zipSize) -ForegroundColor Green
}
