$ErrorActionPreference = 'Stop'
$projectDir = Join-Path $PSScriptRoot '..'
Add-Type -Path @((Join-Path $projectDir 'EnclosingOutline.cs'), (Join-Path $projectDir 'ContourCleaner.cs'), (Join-Path $projectDir 'BoundaryVerifier.cs'))
$expected = [System.Collections.Generic.List[LinkArea2024.OutlinePoint]]::new()
foreach ($pair in @(@(0,0), @(10,0), @(10,10), @(0,10))) { $expected.Add([LinkArea2024.OutlinePoint]::new($pair[0], $pair[1])) }
function Make-Pieces($rows) {
    $list = [System.Collections.Generic.List[LinkArea2024.BoundaryPiece]]::new()
    foreach ($r in $rows) {
        $list.Add([LinkArea2024.BoundaryPiece]::new([LinkArea2024.OutlinePoint]::new($r[0],$r[1]), [LinkArea2024.OutlinePoint]::new($r[2],$r[3])))
    }
    return ,$list
}
$split = Make-Pieces @(@(5,0,0.001,0), @(0,10,10,10), @(10,0,5,0), @(0,0,0.001,0), @(10,10,10,0), @(0,0,0,10))
$corners = [LinkArea2024.BoundaryVerifier]::Verify($expected, $split, 0.00001)
if ($corners.Count -ne 4) { throw 'Topology splits were not collapsed' }
[LinkArea2024.ContourCleaner]::Validate($corners, 0.01, 0.1)
$invalid = @(
    (Make-Pieces @(@(0,0,10,0), @(10,0,10,10), @(10,10,0,10))),
    (Make-Pieces @(@(0,0,10,0), @(10,0,10,10), @(10,10,0,10), @(0,10,0,0), @(0,0,10,0))),
    (Make-Pieces @(@(0,0,5,0), @(5.001,0,10,0), @(10,0,10,10), @(10,10,0,10), @(0,10,0,0))),
    (Make-Pieces @(@(0,0,10,0), @(10,0,9,10), @(9,10,0,10), @(0,10,0,0)))
)
foreach ($pieces in $invalid) {
    $rejected = $false
    try { [LinkArea2024.BoundaryVerifier]::Verify($expected, $pieces, 0.00001) | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid geometric boundary accepted' }
}
Write-Output 'PASS: split edges including short subsegments; reversed and shuffled curves; missing edges, overlap, gaps and changed contour rejected.'
