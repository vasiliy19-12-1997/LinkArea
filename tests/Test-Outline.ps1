$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot '..\EnclosingOutline.cs')

function Assert-True($condition, $message) {
    if (-not $condition) { throw $message }
}
function Assert-Encloses($outline, $points) {
    foreach ($p in $points) {
        for ($i = 0; $i -lt $outline.Count; $i++) {
            $a = $outline[$i]
            $b = $outline[($i + 1) % $outline.Count]
            $cross = ($b.X - $a.X) * ($p.Y - $a.Y) - ($b.Y - $a.Y) * ($p.X - $a.X)
            Assert-True ($cross -ge -0.00001) 'Source point outside enclosing outline'
        }
    }
}
function New-Points($pairs) {
    $result = [System.Collections.Generic.List[LinkArea2024.OutlinePoint]]::new()
    foreach ($pair in $pairs) { $result.Add([LinkArea2024.OutlinePoint]::new($pair[0], $pair[1])) }
    return ,$result
}

$square = New-Points @(@(0,0), @(10,0), @(10,10), @(0,10), @(0,0), @(5,0), @(5,5))
$hull = [LinkArea2024.EnclosingOutline]::Build($square, 2)
Assert-True ($hull.Count -eq 4) 'Square should have four edges'
Assert-True ([Math]::Abs(($hull | Measure-Object X -Minimum).Minimum + 2) -lt 1e-9) 'Wrong left offset'
Assert-True ([Math]::Abs(($hull | Measure-Object X -Maximum).Maximum - 12) -lt 1e-9) 'Wrong right offset'
Assert-True ([Math]::Abs(($hull | Measure-Object Y -Minimum).Minimum + 2) -lt 1e-9) 'Wrong lower offset'
Assert-True ([Math]::Abs(($hull | Measure-Object Y -Maximum).Maximum - 12) -lt 1e-9) 'Wrong upper offset'
Assert-Encloses $hull $square

# Detached balconies, deep recesses, repeated and collinear edge points.
$islands = New-Points @(@(0,0), @(20,0), @(20,20), @(0,20), @(8,-3), @(12,-3), @(23,8), @(23,12), @(8,23), @(12,23), @(-3,8), @(-3,12), @(9,9), @(9,11))
$outline = [LinkArea2024.EnclosingOutline]::Build($islands, 0)
Assert-Encloses $outline $islands
Assert-True ($outline.Count -ge 4) 'Detached pieces did not form a polygon'

$random = [Random]::new(42)
for ($case = 0; $case -lt 50; $case++) {
    $angle = $case * 0.137
    $points = [System.Collections.Generic.List[LinkArea2024.OutlinePoint]]::new()
    for ($i = 0; $i -lt 100; $i++) {
        $x = $random.NextDouble() * 100
        $y = $random.NextDouble() * 100
        $points.Add([LinkArea2024.OutlinePoint]::new(1000000 + $x * [Math]::Cos($angle) - $y * [Math]::Sin($angle), -1000000 + $x * [Math]::Sin($angle) + $y * [Math]::Cos($angle)))
    }
    $outline = [LinkArea2024.EnclosingOutline]::Build($points, 0.3280839895)
    Assert-Encloses $outline $points
}
$failed = $false
try { [LinkArea2024.EnclosingOutline]::Build((New-Points @(@(0,0), @(1,1), @(2,2))), 0) | Out-Null }
catch { $failed = $true }
Assert-True $failed 'Collinear input must be rejected'

# Regression: the floor outline misses facade projections and overhanging upper levels.
$floor = New-Points @(@(0,0), @(20,0), @(20,20), @(0,20))
$buildingBounds = New-Points @(@(-2,-1), @(22,-1), @(22,21), @(-2,21), @(8,24), @(12,24), @(-4,8), @(-4,12))
$oldOutline = [LinkArea2024.EnclosingOutline]::Build($floor, 0.3280839895)
Assert-True (-not [LinkArea2024.EnclosingOutline]::ContainsAll($oldOutline, $buildingBounds, 0.0001)) 'Regression fixture should exceed the floor-only outline'
$newOutline = [LinkArea2024.EnclosingOutline]::Build($buildingBounds, 1.6404199475)
Assert-True ([LinkArea2024.EnclosingOutline]::ContainsAll($newOutline, $buildingBounds, 0.0001)) 'Facade and upper-level bounds not enclosed'
Assert-True (-not [LinkArea2024.EnclosingOutline]::ContainsAll($newOutline, (New-Points @(@(100,100), @(101,101))), 0.0001)) 'Coverage checker must reject outside points'

# Eight box corners, tilted nested transform, horizontal rotation, mirror and large translation.
foreach ($mirror in @(-1, 1)) {
    $boxPoints = [System.Collections.Generic.List[LinkArea2024.OutlinePoint]]::new()
    foreach ($x in @(-20, 20)) {
        foreach ($y in @(-15, 15)) {
            foreach ($z in @(-10, 80)) {
                $tiltedX = $x * [Math]::Cos(0.2) + $z * [Math]::Sin(0.2)
                $px = $mirror * $tiltedX
                $boxPoints.Add([LinkArea2024.OutlinePoint]::new(500000 + $px * [Math]::Cos(0.43) - $y * [Math]::Sin(0.43), -500000 + $px * [Math]::Sin(0.43) + $y * [Math]::Cos(0.43)))
            }
        }
    }
    $boxHull = [LinkArea2024.EnclosingOutline]::Build($boxPoints, 0)
    Assert-Encloses $boxHull $boxPoints
    Assert-True ([LinkArea2024.EnclosingOutline]::ContainsAll($boxHull, $boxPoints, 0.0001)) 'Transformed box corners not enclosed'
}
$boundary = [LinkArea2024.EnclosingOutline]::Build($floor, 0)
function Test-Touch($x1, $y1, $x2, $y2) {
    return [LinkArea2024.EnclosingOutline]::SegmentTouches($boundary,
        [LinkArea2024.OutlinePoint]::new($x1, $y1), [LinkArea2024.OutlinePoint]::new($x2, $y2), 0.0001)
}
Assert-True (-not (Test-Touch 25 0 25 20)) 'Neighboring building boundary must not conflict'
Assert-True (-not (Test-Touch 21 0 25 0)) 'Collinear extension beyond polygon must not conflict'
Assert-True (-not (Test-Touch -10 1 1 -10)) 'Line near bounding-box corner stays outside polygon'
Assert-True (Test-Touch -5 10 25 10) 'Crossing line must conflict even with both endpoints outside'
Assert-True (Test-Touch 2 2 5 5) 'Interior line must conflict'
Assert-True (Test-Touch -5 0 5 0) 'Boundary overlap must conflict'
Assert-True (Test-Touch -5 5 5 -5) 'Corner touch must conflict'
Write-Output 'PASS: offsets; disconnected pieces; rotated/shared coordinates; facade/upper-level regression; projected boxes; neighboring, interior, crossing and tangent boundary lines.'
