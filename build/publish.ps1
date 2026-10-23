<#
.SYNOPSIS
    Publishes Taskbar Dino as a single Native AOT executable and packages a portable ZIP.

.DESCRIPTION
    Native AOT needs the MSVC linker, so this script locates Visual Studio (or Build Tools) with the
    "Desktop development with C++" workload via vswhere, enters its x64 developer environment and runs
    `dotnet publish`. Output: artifacts\publish\TaskbarDino.exe and artifacts\TaskbarDino-<version>-win-x64.zip.

.PARAMETER Configuration
    Build configuration (default: Release).
#>
[CmdletBinding()]
param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\TaskbarDino\TaskbarDino.csproj"
$artifacts = Join-Path $root "artifacts"
$publishDir = Join-Path $artifacts "publish"

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "vswhere.exe not found. Install Visual Studio 2022+ or Build Tools with the 'Desktop development with C++' workload (https://aka.ms/nativeaot-prerequisites)."
}

$vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) {
    throw "No Visual Studio instance with the C++ x64 build tools was found (https://aka.ms/nativeaot-prerequisites)."
}

$vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat"
New-Item -ItemType Directory -Force $publishDir | Out-Null
Get-ChildItem $publishDir -File -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "Publishing (Native AOT, win-x64, $Configuration)..."
cmd /c "`"$vcvars`" >nul && dotnet publish `"$project`" -c $Configuration -r win-x64 --nologo -v q -p:IlcUseEnvironmentalTools=true -o `"$publishDir`""
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

# Only the executable ships; symbols stay out of the portable package.
Get-ChildItem $publishDir -Filter *.pdb | Remove-Item -Force

$exe = Join-Path $publishDir "TaskbarDino.exe"
$version = (Get-Item $exe).VersionInfo.ProductVersion -replace '\+.*$', ''
$zip = Join-Path $artifacts "TaskbarDino-$version-win-x64.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "README.md") $publishDir -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "LICENSE") $publishDir -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip -CompressionLevel Optimal

$size = (Get-Item $exe).Length
Write-Host ("Done: {0} ({1:N0} bytes) -> {2}" -f $exe, $size, $zip)
