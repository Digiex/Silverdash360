<#
.SYNOPSIS
Gives one of the settings tools\Configure.ps1 saved in config.json at the
repository's root, for the other tools; '' when it isn't there.

Settings: Sdk (the Silverlight360 SDK's folder), Python (python.exe), Gh
(gh.exe).

.EXAMPLE
$sdk = & (Join-Path $PSScriptRoot 'Config.ps1') Sdk
#>
param(
    [Parameter(Mandatory)]
    [ValidateSet('Sdk', 'Python', 'Gh')]
    [string]$Name
)

$file = Join-Path (Split-Path -Parent $PSScriptRoot) 'config.json'
if (Test-Path -LiteralPath $file) {
    $value = (Get-Content -LiteralPath $file -Raw | ConvertFrom-Json).$Name
    if ($value) {
        return $value
    }
}
return ''
