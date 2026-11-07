<#
.SYNOPSIS
Builds Silverdash360 for release and publishes it on GitHub Releases.

.DESCRIPTION
Makes a Release build of the dashboard's own files, zips them and
publishes the zip as a GitHub release, with the commits since the last
release as its notes.

The zip has only what this repository makes: Silverdash360.dll, its
native image Silverdash360.ni.dll, the app manifests and the splash
screens, from the SDK's app-only build (Build.ps1 -Release -AppOnly), with
the debug server off. That build always makes the image, in the whole
Release package first: an image fits only the Silverdash360.dll it was made
from, and only the runtime it was made against, which NativeImage.txt in the
zip records (each of the runtime's native images and xcoreclr.dll, with its
size). An update installs the image only over a runtime that matches. No
donor or platform assembly, nor an image of one, goes in. The package's other files, default.xex and the Silverlight
platform, are the runtime profile's: Microsoft's and the donor app's
binaries, which aren't ours to hand out. Whoever installs it lays the zip
over the base package from the Digiex thread, which has the same runtime
(Plex's), or over a runtime profile they made themselves (see INSTALL.txt
in the zip).

Every release has a higher version than the last, and is tagged v<version>.
The version is Silverdash360Version in Silverdash360.csproj when that's
higher than the last release's, as when a minor or major version is set by
hand; otherwise the last release's with its patch (or -Bump's part) raised.
A new version is committed to the project file and that commit pushed, so
the release is built and tagged from it. Everything before it must be
committed and pushed already: nothing else is pushed.

Needs the GitHub CLI (gh), signed in, unless -DryRun or -Console is given.

-Console makes the base package instead, and releases nothing: the whole
Release package, runtime included, in a Silverdash360 folder, as
Silverdash360-<version>-console.zip in bin\Release\console, for a console
of your own or for the Digiex thread. It has the donor app's and Microsoft's
files, so it never goes on GitHub. It's the project's version as it is,
with no commit, tag or push. It has the native image Build.ps1 makes, so
the dashboard runs compiled.

.EXAMPLE
.\tools\Release.ps1 -DryRun

Shows the version, builds and zips, and shows the notes, without
committing, pushing or publishing.

.EXAMPLE
.\tools\Release.ps1 -Bump Minor

.EXAMPLE
.\tools\Release.ps1 -Highlight 'Huge performance improvements'

Puts the line at the top of the notes, which the update screen shows first.

.EXAMPLE
.\tools\Release.ps1 -Bump Minor -NotesFile .\notes-0.2.0.md

Uses the notes in the file, written for the dashboard's users, instead of
the commits.

.EXAMPLE
.\tools\Release.ps1 -Console

Makes the zip to copy to your own console, runtime and all.
#>
param(
    [string]$Sdk = $env:SILVERLIGHT360,
    # The part of the last release's version to raise, when the project's
    # isn't higher already.
    [ValidateSet('Patch', 'Minor', 'Major')] [string]$Bump = 'Patch',
    # Shows what would be released, without committing, pushing or publishing.
    [switch]$DryRun,
    # Publishes the release as a prerelease.
    [switch]$Prerelease,
    # Zips the whole package, runtime included, for your own console, and
    # releases nothing.
    [switch]$Console,
    # Lines for the top of the notes, above the commits, such as what's
    # biggest in the release: the update screen shows the notes' first lines.
    [string[]]$Highlight = @(),
    # A Markdown file of notes written for the dashboard's users, used in
    # place of the commits; the update screen shows its first 12 lines.
    [string]$NotesFile = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Silverdash360'
$projectFile = Join-Path $project 'Silverdash360.csproj'
$versionPattern = '(<Silverdash360Version[^>]*>)([^<]*)(</Silverdash360Version>)'

# UTF-8 without the byte order mark Windows PowerShell's writes, which
# GitHub shows in release notes.
function Write-Utf8([string]$path, [string[]]$lines) {
    [System.IO.File]::WriteAllLines($path, $lines, (New-Object System.Text.UTF8Encoding $false))
}

function Invoke-Git {
    $output = & git.exe -C $root @args
    if ($LASTEXITCODE -ne 0) {
        throw "git $args failed"
    }
    $output
}

# The whole package for one's own console: built and zipped, nothing else.
if ($Console) {
    if (Invoke-Git status --porcelain --untracked-files=no) {
        Write-Warning 'There are changes not committed: settings will say the build has changes.'
    }
    & (Join-Path $PSScriptRoot 'Build.ps1') -Release -Sdk $Sdk
    if ($LASTEXITCODE -ne 0) {
        throw 'The build failed.'
    }
    $buildInfo = Get-Content -LiteralPath (Join-Path $project 'obj\Release\BuildInfo.g.cs') -Raw
    $version = [regex]::Match($buildInfo, 'Version = "([^"]*)"').Groups[1].Value
    $number = [regex]::Match($buildInfo, 'Number = (\d+)').Groups[1].Value
    $out = Join-Path $project 'bin\Release\console'
    $staging = Join-Path $out 'Silverdash360'
    if (Test-Path -LiteralPath $out) {
        Remove-Item -LiteralPath $out -Recurse -Force
    }
    New-Item -ItemType Directory -Path $staging | Out-Null
    Copy-Item -Path (Join-Path $project 'bin\Release\xbox\*') -Destination $staging -Recurse
    # Its license goes with every copy, as it asks.
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE.md') -Destination $staging
    $zip = Join-Path $out "Silverdash360-$version-console.zip"
    Compress-Archive -Path $staging -DestinationPath $zip
    Write-Host "Silverdash360 $version (build $number), the base package: $zip"
    Write-Host 'Copy its Silverdash360 folder to the console, such as to Hdd1:\Apps, and start default.xex in it.'
    Write-Host "It has the donor app's and Microsoft's files: it goes on the Digiex thread, never on GitHub."
    return
}

# A release is made from a commit as it is, which GitHub has.
if (Invoke-Git status --porcelain --untracked-files=no) {
    throw 'There are changes not committed. Commit them, or stash them, first.'
}
Invoke-Git fetch --quiet --tags origin
$commit = Invoke-Git rev-parse HEAD
if (-not $DryRun) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'The GitHub CLI (gh) is needed to publish: https://cli.github.com'
    }
    if (-not (Invoke-Git branch -r --contains $commit)) {
        throw "Commit $commit isn't on GitHub yet. Push it first."
    }
}

# The releases so far, by their v<version> tags.
$released = @()
foreach ($tagName in (Invoke-Git tag --list 'v*')) {
    $parsed = $null
    if ([version]::TryParse($tagName.Substring(1), [ref]$parsed)) {
        $released += [pscustomobject]@{ Tag = $tagName; Version = $parsed }
    }
}
$last = $released | Sort-Object Version -Descending | Select-Object -First 1

# The version: the project's when it's higher than the last release's, else
# the last release's raised.
$projectText = Get-Content -LiteralPath $projectFile -Raw
$projectVersion = [version][regex]::Match($projectText, $versionPattern).Groups[2].Value
$version = $projectVersion
if ($last -and $projectVersion -le $last.Version) {
    $v = $last.Version
    $version = switch ($Bump) {
        'Major' { [version]::new($v.Major + 1, 0, 0) }
        'Minor' { [version]::new($v.Major, $v.Minor + 1, 0) }
        default { [version]::new($v.Major, $v.Minor, [Math]::Max($v.Build, 0) + 1) }
    }
}
$version = "$version"
$tag = "v$version"
if ($released | Where-Object { $_.Tag -eq $tag }) {
    throw "$tag is released already."
}
Write-Host ("Version $version" + $(if ($last) { ", after $($last.Tag)" } else { ', the first release' }))

if ($version -ne "$projectVersion") {
    if ($DryRun) {
        # Built with the new version without committing it.
        $env:Silverdash360Version = $version
    }
    else {
        # The new version, committed and pushed, so the release is built and
        # tagged from it.
        $projectText = [regex]::Replace($projectText, $versionPattern, "`${1}$version`${3}")
        [System.IO.File]::WriteAllText($projectFile, $projectText)
        Invoke-Git add -- $projectFile
        Invoke-Git commit --quiet -m "Version $version"
        Invoke-Git push --quiet origin HEAD
        $commit = Invoke-Git rev-parse HEAD
    }
}

# A Release build of the dashboard's own files, its debug server (which
# listens without a password) off, with the native image of its
# Silverdash360.dll: the SDK makes it in the whole package (bin\Release\xbox,
# which stays) and puts it with the app's files.
& (Join-Path $PSScriptRoot 'Build.ps1') -Release -AppOnly -Sdk $Sdk
if ($LASTEXITCODE -ne 0) {
    throw 'The build failed.'
}

# The build number the build wrote into BuildInfo, which settings shows.
$buildInfo = Get-Content -LiteralPath (Join-Path $project 'obj\Release\BuildInfo.g.cs') -Raw
$number = [regex]::Match($buildInfo, 'Number = (\d+)').Groups[1].Value
if ([regex]::Match($buildInfo, 'Version = "([^"]*)"').Groups[1].Value -ne $version) {
    throw "The build isn't version $version."
}
if ([regex]::Match($buildInfo, 'Changed = (\w+)').Groups[1].Value -ne 'false') {
    throw 'The build has changes not committed.'
}
$name = "Silverdash360 $version (build $number)"

# Nothing of the runtime's or the donor's: the app-only build leaves it out,
# and an image goes in only beside the assembly of ours it was made from.
$appFiles = Join-Path $project 'bin\Release\app'
$ours = @(Get-ChildItem -LiteralPath $appFiles -Recurse -File | ForEach-Object { $_.FullName.Substring($appFiles.Length + 1) })
foreach ($relative in $ours) {
    if ($relative -eq 'default.xex' -or $relative -like 'Microsoft.*' -or $relative -like 'System.*' -or
        $relative -like 'MWM.*' -or $relative -like 'SW.*' -or $relative -like 'mscorlib*' -or $relative -like 'xcoreclr*' -or
        ($relative -like '*.ni.dll' -and $ours -notcontains ($relative -replace '\.ni\.dll$', '.dll'))) {
        throw "$relative is the platform's or the donor app's, which isn't released."
    }
}
foreach ($required in 'Silverdash360.dll', 'Silverdash360.ni.dll') {
    if ($ours -notcontains $required) {
        throw "$required isn't in the app files at $appFiles."
    }
}

$out = Join-Path $project 'bin\Release\release'
$staging = Join-Path $out 'Silverdash360'
if (Test-Path -LiteralPath $out) {
    Remove-Item -LiteralPath $out -Recurse -Force
}
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -Path (Join-Path $appFiles '*') -Destination $staging -Recurse
Copy-Item -LiteralPath (Join-Path $root 'LICENSE.md') -Destination $staging
# The runtime the image was made against, the whole package's, which an
# update checks the console's against (UpdateService): the runtime's native
# images and xcoreclr.dll, by size.
$package = Join-Path $project 'bin\Release\xbox'
$record = @('# Silverdash360.ni.dll was compiled against this runtime: its files and their sizes in bytes.',
            '# Silverdash360 installs the image as an update only over a runtime that matches.')
$record += Get-ChildItem -LiteralPath $package -File |
    Where-Object { ($_.Name -like '*.ni.dll' -and $ours -notcontains $_.Name) -or $_.Name -eq 'xcoreclr.dll' } |
    Sort-Object Name | ForEach-Object { "$($_.Name) $($_.Length)" }
Write-Utf8 (Join-Path $staging 'NativeImage.txt') $record
$install = @"
$name
https://github.com/Digiex/Silverdash360

This zip has Silverdash360's own files only, an update to go over the base
package. The base package, with the runtime it runs on, is on Digiex:
https://digiex.net/threads/silverdash360-homebrew-xbox-360-dashboard.20103/

1. Download the base package and extract it.
2. Copy everything in this zip into its Silverdash360 folder, replacing
   what's there.
3. Copy the Silverdash360 folder to the console, such as to Hdd1:\Apps.
4. Start default.xex in that folder.

On a console that has Silverdash360 already, copy everything in this zip
over the files in its folder instead.

Silverdash360 keeps its settings and cache in the Data folder next to it.
"@
$install += @"


Silverdash360.ni.dll is Silverdash360's code compiled to native code with
XboxNGen (https://github.com/Digiex/Silverlight360-XboxNGen), which makes
it much faster. It fits only the runtime it was compiled against, the base
package's, which NativeImage.txt lists. Over a runtime you made yourself
that isn't the same, leave Silverdash360.ni.dll out: Silverdash360 doesn't
start with it there, and without it Silverdash360 runs as it is, only
slower. An update from settings checks the runtime and leaves it out
itself.
"@
Write-Utf8 (Join-Path $staging 'INSTALL.txt') ($install -split "`r?`n")
$zip = Join-Path $out "Silverdash360-$version.zip"
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip

# The notes: the commits since the last release before this commit, without
# the version commits.
$previous = $null
foreach ($candidate in ($released | Sort-Object Version -Descending)) {
    & git.exe -C $root merge-base --is-ancestor $candidate.Tag $commit
    if ($LASTEXITCODE -eq 0) {
        $previous = $candidate.Tag
        break
    }
}
$range = if ($previous) { "$previous..$commit" } else { $commit }
$notes = @()
if ($Highlight) {
    $notes += $Highlight
    $notes += ''
}
if ($NotesFile) {
    # Written for the people who use the dashboard, in place of the commits.
    $notes += Get-Content -LiteralPath $NotesFile -Encoding utf8
} else {
    $notes += if ($previous) { "## Changes since $previous" } else { '## Changes' }
    $notes += ''
    $notes += Invoke-Git log $range --no-merges -E --invert-grep '--grep=^Version [0-9]+\.[0-9]+\.[0-9]+$' --format='- %s (%h)'
}
$notes += ''
$notes += "Build $number, from $($commit.Substring(0, 7)). The zip has Silverdash360's own files; see INSTALL.txt in it for putting it over the base package from Digiex."
$notesFile = Join-Path $out 'notes.md'
Write-Utf8 $notesFile $notes

Write-Host "$name ($tag)"
Write-Host "Files: $($ours -join ', '), LICENSE.md, INSTALL.txt, NativeImage.txt"
Write-Host "Zip: $zip"
Write-Host ''
Get-Content -LiteralPath $notesFile | Write-Host
if ($DryRun) {
    Write-Host ''
    Write-Host 'Dry run: nothing committed, pushed or published.'
    return
}

$arguments = @('release', 'create', $tag, $zip, '--repo', 'Digiex/Silverdash360', '--target', $commit,
               '--title', $name, '--notes-file', $notesFile)
if ($Prerelease) {
    $arguments += '--prerelease'
}
& gh @arguments
if ($LASTEXITCODE -ne 0) {
    throw 'gh release create failed.'
}
Invoke-Git fetch --quiet --tags origin
