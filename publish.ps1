<#
.SYNOPSIS
    Publish ClassLapse as a single-file Windows .exe.

.DESCRIPTION
    Default mode: framework-dependent single .exe (~5MB), requires .NET 8 Runtime on target machine.
    -SelfContained switch: bundle the .NET runtime (~65MB), no install needed on target.

.PARAMETER SelfContained
    Bundle the .NET runtime into the .exe.

.PARAMETER OutputDir
    Output directory. Default: publish/

.EXAMPLE
    ./publish.ps1
    ./publish.ps1 -SelfContained
    ./publish.ps1 -OutputDir D:\Releases\ClassLapse
#>

[CmdletBinding()]
param(
    [switch]$SelfContained,
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"
$projectPath = "src/ClassLapse/ClassLapse.csproj"

$args = @(
    "publish", $projectPath,
    "-c", "Release",
    "-r", "win-x64",
    "-o", $OutputDir,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=embedded",
    "-p:EnableCompressionInSingleFile=true"
)

if ($SelfContained) {
    $args += @("--self-contained", "true")
    Write-Host "Building self-contained .exe (bundled runtime, ~65MB)..." -ForegroundColor Cyan
} else {
    $args += @("--self-contained", "false")
    Write-Host "Building framework-dependent .exe (requires .NET 8 Runtime, ~5MB)..." -ForegroundColor Cyan
}

& dotnet @args
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed."
    exit 1
}

$exe = Join-Path $OutputDir "ClassLapse.exe"
if (Test-Path $exe) {
    $size = (Get-Item $exe).Length / 1MB
    Write-Host ("Done. {0} ({1:N1} MB)" -f $exe, $size) -ForegroundColor Green
}
