# sonar-report.ps1 - render the analyzer SARIF into something a person can act on.
#
# A first analysis of this library reports several thousand findings. Read as a build log
# that is noise; the useful shape is "which rule fires how often, and where", because a
# handful of rules account for most of the volume and are usually one .editorconfig
# decision each, while the rules that matter fire a few dozen times and deserve reading.
#
# Sonar's own rules are the S#### ones. The compiler and the .NET SDK analyzers report
# through the same SARIF, so they are counted separately rather than mixed in.
param(
  [Parameter(Mandatory)] [string] $Sarif,
  [Parameter(Mandatory)] [string] $OutDir,
  [int] $TopFiles = 40
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null

$doc = Get-Content $Sarif -Raw -Encoding utf8 | ConvertFrom-Json

# Rule metadata lives once per run, beside the results that reference it by id.
$meta = @{}
foreach ($run in $doc.runs) {
  foreach ($r in $run.tool.driver.rules) {
    if (-not $meta.ContainsKey($r.id)) {
      $text = if ($r.shortDescription.text) { $r.shortDescription.text } else { $r.id }
      $meta[$r.id] = @{ Text = ($text -replace '\s+', ' ').Trim(); Uri = $r.helpUri }
    }
  }
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path.TrimEnd('\') + '\'
$findings = New-Object System.Collections.Generic.List[object]
foreach ($run in $doc.runs) {
  foreach ($res in $run.results) {
    $loc = $res.locations[0].physicalLocation
    $file = if ($loc.artifactLocation.uri) { $loc.artifactLocation.uri } else { '(no file)' }
    $file = $file -replace '^file:///', '' -replace '/', '\'
    if ($file.StartsWith($repo, 'OrdinalIgnoreCase')) { $file = $file.Substring($repo.Length) }
    $findings.Add([pscustomobject]@{
      Rule     = $res.ruleId
      Severity = $res.level
      File     = $file
      Line     = $loc.region.startLine
      Message  = ($res.message.text -replace '\s+', ' ').Trim()
    })
  }
}

# A rule id that starts with S and continues in digits is SonarSource's; CA/CS/IDE are the
# platform's own analyzers, which this project already builds warnings-free against.
$sonar = @($findings | Where-Object { $_.Rule -match '^S\d+$' })
$other = @($findings | Where-Object { $_.Rule -notmatch '^S\d+$' })

$byRule = $sonar | Group-Object Rule | Sort-Object Count -Descending
$byFile = $sonar | Group-Object File | Sort-Object Count -Descending

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# Sonar findings by rule')
[void]$sb.AppendLine()
[void]$sb.AppendLine(('{0:N0} SonarSource finding(s) across {1:N0} rule(s) and {2:N0} file(s).' -f $sonar.Count, $byRule.Count, $byFile.Count))
[void]$sb.AppendLine(('{0:N0} further finding(s) came from the compiler and the .NET analyzers - see the tail of this file.' -f $other.Count))
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Rule | Count | What it reports | Worst files |')
[void]$sb.AppendLine('| --- | ---: | --- | --- |')
foreach ($g in $byRule) {
  $m = $meta[$g.Name]
  $desc = if ($m) { $m.Text } else { '' }
  $link = if ($m -and $m.Uri) { '[{0}]({1})' -f $g.Name, $m.Uri } else { $g.Name }
  $worst = ($g.Group | Group-Object File | Sort-Object Count -Descending | Select-Object -First 3 |
            ForEach-Object { '{0} ({1})' -f (Split-Path $_.Name -Leaf), $_.Count }) -join ', '
  [void]$sb.AppendLine(('| {0} | {1} | {2} | {3} |' -f $link, $g.Count, $desc, $worst))
}
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Non-Sonar findings (compiler / .NET analyzers)')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Rule | Count |')
[void]$sb.AppendLine('| --- | ---: |')
foreach ($g in ($other | Group-Object Rule | Sort-Object Count -Descending)) {
  [void]$sb.AppendLine(('| {0} | {1} |' -f $g.Name, $g.Count))
}
Set-Content (Join-Path $OutDir 'issues-by-rule.md') -Value $sb.ToString() -Encoding utf8

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# Sonar findings by file')
[void]$sb.AppendLine()
[void]$sb.AppendLine(('Top {0} of {1:N0} file(s), densest first.' -f $TopFiles, $byFile.Count))
[void]$sb.AppendLine()
[void]$sb.AppendLine('| File | Count | Most frequent rules |')
[void]$sb.AppendLine('| --- | ---: | --- |')
foreach ($g in ($byFile | Select-Object -First $TopFiles)) {
  $top = ($g.Group | Group-Object Rule | Sort-Object Count -Descending | Select-Object -First 3 |
          ForEach-Object { '{0} ({1})' -f $_.Name, $_.Count }) -join ', '
  [void]$sb.AppendLine(('| {0} | {1} | {2} |' -f $g.Name, $g.Count, $top))
}
Set-Content (Join-Path $OutDir 'issues-by-file.md') -Value $sb.ToString() -Encoding utf8

# The flat list is what a spreadsheet or a follow-up script wants.
$sonar | Sort-Object Rule, File, Line |
  Select-Object Rule, Severity, File, Line, Message |
  Export-Csv (Join-Path $OutDir 'issues.csv') -NoTypeInformation -Encoding utf8

Write-Host ('sonar findings : {0}' -f $sonar.Count)
Write-Host ('other findings : {0}' -f $other.Count)
Write-Host ('rules firing   : {0}' -f $byRule.Count)
Write-Host ('files affected : {0}' -f $byFile.Count)
Write-Host ''
Write-Host 'top 15 rules:'
foreach ($g in ($byRule | Select-Object -First 15)) {
  $m = $meta[$g.Name]
  Write-Host ('  {0,-8} {1,6}  {2}' -f $g.Name, $g.Count, $(if ($m) { $m.Text } else { '' }))
}
