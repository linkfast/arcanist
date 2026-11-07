# What a release changed since the one before, in Markdown, for the top of its notes: the subjects of its commits,
# grouped by their area ("Duplicates: …" goes under "Duplicates"). Arcanist reads this section (between the markers)
# to show what a new version brings when it says there is one: keep the markers and the shape of the lines.
#
#   pwsh .github/scripts/release-changes.ps1 -Tag v0.1.0-alpha.25 [-Repository linkfast/arcanist]
#
# Needs the history and the tags (actions/checkout with fetch-depth: 0).
param(
    [Parameter(Mandatory)] [string] $Tag,
    [string] $Repository = "linkfast/arcanist"
)

$ErrorActionPreference = "Stop"
$version = $Tag -replace '^v', ''

# The release before: the nearest tag of a version behind this one.
$previous = git describe --tags --abbrev=0 --match 'v[0-9]*' "$Tag^" 2>$null
if ($LASTEXITCODE -ne 0) { $previous = $null }
$range = if ($previous) { "$previous..$Tag" } else { $Tag }

# Oldest first, so each area lists its changes in the order they were made; merges and internal areas are left out.
$internal = @('Tests', 'Test', 'CI', 'Release')
$areas = [ordered]@{}
$loose = [System.Collections.Generic.List[string]]::new()
foreach ($line in git log --no-merges --reverse --format='%H%x09%s' $range) {
    $sha, $subject = $line -split "`t", 2
    $subject = $subject.Trim()
    if (-not $subject) { continue }
    $link = "([$($sha.Substring(0, 7))](https://github.com/$Repository/commit/$sha))"
    if ($subject -match '^(?<player>\S+) for Arcanist (?<version>\d\S*)$') {
        $area = $Matches.player
        $text = $Matches.version
    }
    elseif ($subject -match '^(?<area>[^:]{1,40}):\s+(?<text>.+)$') {
        $area = $Matches.area -replace '\s+v?\d+\.\d+\S*$', ''
        $text = $Matches.text
    }
    else {
        $area = $null
        $text = $subject
    }
    if ($area -and $internal -contains $area) { continue }
    $item = "- $text $link"
    if ($area) {
        if (-not $areas.Contains($area)) { $areas[$area] = [System.Collections.Generic.List[string]]::new() }
        $areas[$area].Add($item)
    }
    else {
        $loose.Add($item)
    }
}

$out = [System.Collections.Generic.List[string]]::new()
$out.Add('<!-- arcanist:changes -->')
$out.Add("## What's new in $version")
$out.Add('')
if ($areas.Count -eq 0 -and $loose.Count -eq 0) {
    $out.Add('Packaging and internal changes only.')
}
foreach ($item in $loose) { $out.Add($item) }
if ($loose.Count -gt 0) { $out.Add('') }
foreach ($area in $areas.Keys) {
    $out.Add("### $area")
    foreach ($item in $areas[$area]) { $out.Add($item) }
    $out.Add('')
}
if ($previous) {
    $out.Add("All the commits: https://github.com/$Repository/compare/$previous...$Tag")
}
$out.Add('<!-- /arcanist:changes -->')
$out -join "`n"
