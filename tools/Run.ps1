<#
.SYNOPSIS
Runs the built dashboard in xenia-edge, presses buttons and takes screenshots.

.DESCRIPTION
Uses the Silverlight360 SDK's Test-XboxApp.ps1 (see its testing guide for the
steps and buttons). -ContentRoot gives xenia a content folder of its own, so
the dashboard can be tried on a known set of games and apps without touching
xenia's own. Copy a profile's folder into it to have someone signed in.

-Install copies the build into the content folder and runs it from there,
as an install on the console's hard drive: xenia's hard drive has only its
Content folder, so that's where the dashboard can save its library and art
(in Silverdash360\Data). -Fresh deletes that first, as a new install.

It runs the Debug build; -Release runs the Release one, whose debug server is
off until Settings > Debug server turns it on.

.EXAMPLE
.\tools\Run.ps1 -ContentRoot $env:TEMP\testcontent -Steps '15:shot','16:RightShoulder','18:shot'

.EXAMPLE
.\tools\Run.ps1 -ContentRoot $env:TEMP\testcontent -Install -Fresh
#>
param(
    [string]$Sdk = $env:SILVERLIGHT360,
    [string]$Output = (Join-Path $env:TEMP 'silverdash-run'),
    [string]$ContentRoot = '',
    [string[]]$Steps = @('16:shot'),
    [int]$Seconds = 20,
    # xenia_edge.exe: this, else the XENIA_EDGE environment variable, else PATH.
    [string]$Xenia = $env:XENIA_EDGE,
    # More xenia options, such as --systemlink_transport=off for the FTP server.
    [string[]]$ExtraArgs = @(),
    # Runs the build from <ContentRoot>\Silverdash360, where it can save.
    [switch]$Install,
    # With -Install: without what an earlier run saved.
    [switch]$Fresh,
    # The Release build instead of the Debug one.
    [switch]$Release
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Sdk) {
    $Sdk = Join-Path (Split-Path -Parent $root) 'Silverlight360'
}
$testScript = Join-Path $Sdk 'tools\Test-XboxApp.ps1'
if (-not (Test-Path -LiteralPath $testScript)) {
    throw "Silverlight360 not found at $Sdk. Pass -Sdk or set SILVERLIGHT360."
}
$configuration = if ($Release) { 'Release' } else { 'Debug' }
$xex = Join-Path $root "src\Silverdash360\bin\$configuration\xbox\default.xex"
if (-not (Test-Path -LiteralPath $xex)) {
    throw "Build the dashboard first: .\tools\Build.ps1 -$configuration"
}

$extra = @($ExtraArgs)
if ($ContentRoot) {
    $ContentRoot = (Resolve-Path -LiteralPath $ContentRoot).Path
    $extra += "--content_root=$ContentRoot"
}
if ($Install) {
    if (-not $ContentRoot) {
        throw '-Install needs -ContentRoot.'
    }
    # The dashboard sees it as /Hdd1/Content/Silverdash360.
    $installed = Join-Path $ContentRoot 'Silverdash360'
    $data = Join-Path $installed 'Data'
    if ($Fresh -and (Test-Path -LiteralPath $data)) {
        Remove-Item -LiteralPath $data -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $installed | Out-Null
    Copy-Item -Path (Join-Path (Split-Path -Parent $xex) '*') -Destination $installed -Recurse -Force
    $xex = Join-Path $installed 'default.xex'
}
$arguments = @{ Xex = $xex; Output = $Output; Steps = $Steps; Seconds = $Seconds; ExtraArgs = $extra }
if ($Xenia) {
    $arguments.Xenia = $Xenia
}
& $testScript @arguments
