#requires -Version 7.0
<#
.SYNOPSIS
    Works out the version of a build and the release notes that go with it.

.DESCRIPTION
    Tags are the only place a version is kept. A tag `vX.Y.Z` states the version of the commit it
    points at, and a later commit derives its own number from the conventional-commit types in
    between:

        BREAKING CHANGE, or `!` after the type      X+1 . 0 . 0
        feat                                        X . Y+1 . 0
        fix, perf, refactor, build, revert          X . Y . Z+1
        docs, test, chore, ci, style only           nothing to release

    A push carrying only docs or chores produces the same binaries as the release before it, so it
    builds and stops instead of publishing a second copy under a new number.

    Building a tag takes that tag as it stands, which is how 1.0.0 gets declared and how a patch cut
    by hand gets a number of its own. With no tag anywhere the first release is 0.1.0.

    On a runner the two results are appended to GITHUB_OUTPUT as `version` and `release`. Run it by
    hand in a clone to see what the next push would publish.

.PARAMETER Ref
    The git ref being built, `refs/tags/v1.2.0` or `refs/heads/main`. Defaults to GITHUB_REF.

.PARAMETER Version
    An exact version, which replaces the derivation. This is what a manual workflow run passes.

.PARAMETER NotesPath
    Where to write the release notes as Markdown. Without it no notes are written.

.EXAMPLE
    ./.github/scripts/version.ps1
    ./.github/scripts/version.ps1 -Version 1.4.0 -NotesPath notes.md
#>
param(
    [string] $Ref = $env:GITHUB_REF,
    [string] $Version,
    [string] $NotesPath
)

$ErrorActionPreference = 'Stop'
# A repository with no tag yet is the normal first case, and `git describe` exits non-zero there.
# This script reads $LASTEXITCODE itself, so a failing git call must not throw.
$PSNativeCommandUseErrorActionPreference = $false

# Types that earn a release on their own. Everything outside this list and `feat` changes no byte of
# either binary.
$PatchTypes = 'fix', 'perf', 'refactor', 'build', 'revert'

function Get-NearestTag($commit) {
    $tag = git describe --tags --abbrev=0 --match 'v[0-9]*.[0-9]*.[0-9]*' $commit 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $tag) { return $null }
    return $tag.Trim()
}

function Format-Entry($subject) {
    if ($subject -match '^[a-z]+(\(([^)]+)\))?!?: (.+)$') {
        $scope = $Matches[2]
        $text = $Matches[3]
        if ($scope) { return "- **${scope}**: $text" }
        return "- $text"
    }
    return "- $subject"
}

# --- the version ---

$release = $false

if ($Version) {
    $release = $true
}
elseif ($Ref -match '^refs/tags/v(\d+\.\d+\.\d+)$') {
    $Version = $Matches[1]
    $release = $true
}
elseif ($Ref -like 'refs/tags/*') {
    throw "Tag $($Ref -replace '^refs/tags/', '') is not of the form vX.Y.Z."
}
else {
    $previous = Get-NearestTag 'HEAD'
    $base = if ($previous) { $previous } else { 'v0.0.0' }
    if ($base -notmatch '^v(\d+)\.(\d+)\.(\d+)$') {
        throw "Tag $base is not of the form vX.Y.Z."
    }

    $major = [int] $Matches[1]
    $minor = [int] $Matches[2]
    $patch = [int] $Matches[3]

    $range = if ($previous) { "$previous..HEAD" } else { 'HEAD' }
    $subjects = @(git log --format=%s $range)
    $bodies = @(git log --format=%B $range) -join "`n"

    $breaking = ($bodies -match 'BREAKING[ -]CHANGE') -or
        @($subjects | Where-Object { $_ -match '^[a-z]+(\([^)]+\))?!:' }).Count -gt 0
    $feature = @($subjects | Where-Object { $_ -match '^feat(\([^)]+\))?!?:' }).Count -gt 0
    $fix = @($subjects | Where-Object { $_ -match "^($($PatchTypes -join '|'))(\([^)]+\))?!?:" }).Count -gt 0

    if ($breaking) {
        $major++
        $minor = 0
        $patch = 0
        $release = $true
    }
    elseif ($feature) {
        $minor++
        $patch = 0
        $release = $true
    }
    elseif ($fix) {
        $patch++
        $release = $true
    }
    elseif (-not $previous) {
        # Nothing releasable in the log, and no tag either: this is a fresh repository, and 0.1.0 is
        # a better first release than none at all.
        $minor++
        $release = $true
    }

    $Version = "$major.$minor.$patch"
}

# --- the notes ---

if ($NotesPath) {
    $previous = Get-NearestTag 'HEAD'
    if ($previous -eq "v$Version") {
        # Building the tag itself: the range has to start at the tag before it.
        $previous = Get-NearestTag 'HEAD^'
    }

    $range = if ($previous) { "$previous..HEAD" } else { 'HEAD' }
    $groups = [ordered] @{ Features = @(); Fixes = @(); Performance = @(); Other = @() }

    foreach ($subject in @(git log --format=%s --no-merges $range)) {
        $type = if ($subject -match '^([a-z]+)') { $Matches[1] } else { '' }
        $entry = Format-Entry $subject
        $group = switch ($type) {
            'feat' { 'Features' }
            'fix' { 'Fixes' }
            'perf' { 'Performance' }
            default { 'Other' }
        }
        $groups[$group] += $entry
    }

    $notes = [System.Collections.Generic.List[string]]::new()
    foreach ($group in $groups.Keys) {
        if ($groups[$group].Count -eq 0) { continue }
        $notes.Add("## $group")
        $notes.Add('')
        $notes.AddRange([string[]] $groups[$group])
        $notes.Add('')
    }

    $notes.Add('## Artifacts')
    $notes.Add('')
    $notes.Add('| File | Holds |')
    $notes.Add('| --- | --- |')
    $notes.Add("| ``nscreen-$Version-win-x64.zip`` | ``nscreen-server.exe`` and ``nscreen-client.exe``, Windows x64 |")
    $notes.Add("| ``nscreen-client-$Version-osx-arm64.tar.gz`` | ``nscreen-client``, macOS on Apple Silicon |")
    $notes.Add('')
    $notes.Add('Unpack the whole folder rather than one file: the client keeps Skia, HarfBuzz and the')
    $notes.Add('Avalonia native backend beside its binary. Neither machine needs .NET installed.')
    $notes.Add('')
    $notes.Add('The macOS binary is unsigned. If Gatekeeper blocks it, run `xattr -dr com.apple.quarantine .`')
    $notes.Add('in the unpacked folder.')

    if ($previous -and $env:GITHUB_REPOSITORY) {
        $notes.Add('')
        $notes.Add("[Every change since $previous](https://github.com/$env:GITHUB_REPOSITORY/compare/$previous...v$Version)")
    }

    $notes | Set-Content -Path $NotesPath -Encoding utf8
    Write-Host "notes  -> $NotesPath"
}

# --- the outputs ---

$flag = if ($release) { 'true' } else { 'false' }
if ($env:GITHUB_OUTPUT) {
    "version=$Version", "release=$flag" | Add-Content -Path $env:GITHUB_OUTPUT
}

Write-Host "version $Version"
Write-Host "release $flag"
