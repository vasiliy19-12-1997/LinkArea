$ErrorActionPreference = 'Stop'
$projectDir = Join-Path $PSScriptRoot '..'
Add-Type -Path @((Join-Path $projectDir 'EnclosingOutline.cs'), (Join-Path $projectDir 'ContourCleaner.cs'))
function Points($pairs) {
    $list = [System.Collections.Generic.List[LinkArea2024.OutlinePoint]]::new()
    foreach ($p in $pairs) { $list.Add([LinkArea2024.OutlinePoint]::new($p[0], $p[1])) }
    return ,$list
}
function Assert($condition, $reason) { if (-not $condition) { throw $reason } }
function Check($source, $result, $margin) {
    [LinkArea2024.ContourCleaner]::Validate($result.Points, 0.01, 0.1)
    Assert ([LinkArea2024.EnclosingOutline]::ContainsAll($result.Points, $source, 0.000001)) 'Source geometry was cut off'
    foreach ($p in $source) {
        for ($i = 0; $i -lt $result.Points.Count; $i++) {
            $a = $result.Points[$i]
            $b = $result.Points[($i + 1) % $result.Points.Count]
            $dx = $b.X - $a.X
            $dy = $b.Y - $a.Y
            $distance = ($dx * ($p.Y - $a.Y) - $dy * ($p.X - $a.X)) / [Math]::Sqrt($dx * $dx + $dy * $dy)
            Assert ($distance -ge $margin - 0.000001) 'Requested outward clearance was lost'
        }
    }
}
$square = Points @(@(0,0), @(10,0), @(10,10), @(0,10))
$clean = [LinkArea2024.ContourCleaner]::Build($square, 0.02, 0.01, 0.1)
Assert ($clean.Points.Count -eq 4) 'Rectangle changed vertex count'
Assert ([Math]::Abs($clean.ExtraOutward) -lt 1e-12) 'Rectangle should not require compensation'
Check $square $clean 0.02
$tinyBevel = Points @(@(0,0), @(9.998,0), @(10,0.002), @(10,10), @(0,10))
$clean = [LinkArea2024.ContourCleaner]::Build($tinyBevel, 0.02, 0.01, 0.1)
Assert ($clean.Points.Count -eq 4) 'Tiny bevel not removed'
Assert ($clean.ExtraOutward -gt 0) 'Inward cut was not compensated'
Check $tinyBevel $clean 0.02
$shallow = Points @(@(0,0), @(5,-0.0001), @(10,0), @(10,10), @(0,10))
$clean = [LinkArea2024.ContourCleaner]::Build($shallow, 0.02, 0.01, 0.1)
Assert ($clean.Points.Count -eq 4) 'Nearly collinear vertex not removed'
Check $shallow $clean 0.02
# Preserve clearance even with no requested margin and after large translations/rotations.
for ($case = 0; $case -lt 30; $case++) {
    $list = [System.Collections.Generic.List[LinkArea2024.OutlinePoint]]::new()
    foreach ($p in $tinyBevel) {
        $angle = $case * 0.17
        $list.Add([LinkArea2024.OutlinePoint]::new(1000000 + $p.X * [Math]::Cos($angle) - $p.Y * [Math]::Sin($angle), -1000000 + $p.X * [Math]::Sin($angle) + $p.Y * [Math]::Cos($angle)))
    }
    $margin = if ($case % 2 -eq 0) { 0.0 } else { 0.02 }
    $clean = [LinkArea2024.ContourCleaner]::Build($list, $margin, 0.01, 0.1)
    Check $list $clean $margin
}
$badLoops = @(
    (Points @(@(0,0), @(10,10), @(0,10), @(10,0))),
    (Points @(@(0,0), @(5,0), @(10,0), @(10,10), @(0,10))),
    (Points @(@(0,0), @(0.001,0), @(10,10), @(0,10))),
    (Points @(@(0,0), @(10,0), @(10,10), @(0,0)))
)
foreach ($bad in $badLoops) {
    $rejected = $false
    try { [LinkArea2024.ContourCleaner]::Validate($bad, 0.01, 0.1) } catch { $rejected = $true }
    Assert $rejected 'Invalid actual boundary was accepted'
}
for ($case = 0; $case -lt 30; $case++) {
    $rectangle = [LinkArea2024.ContourCleaner]::Rectangle($tinyBevel, 0.02, ($case * 0.17), 0.001)
    Assert ($rectangle.Points.Count -eq 4) 'SMR rectangle must have four edges'
    Check $tinyBevel $rectangle 0.02
    for ($i = 0; $i -lt 4; $i++) {
        $a = $rectangle.Points[$i]
        $b = $rectangle.Points[($i + 1) % 4]
        $c = $rectangle.Points[($i + 2) % 4]
        $dot = ($b.X - $a.X) * ($c.X - $b.X) + ($b.Y - $a.Y) * ($c.Y - $b.Y)
        Assert ([Math]::Abs($dot) -lt 0.000001) 'SMR rectangle corner must be 90 degrees'
    }
}
Write-Output 'PASS: clean hull; outward clearance; rotated/translated cases; malformed loops; 30 four-line SMR rectangles with 20 mm clearance and right angles.'
