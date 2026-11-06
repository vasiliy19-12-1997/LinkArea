param([string]$ProjectRoot)
$ErrorActionPreference = 'Stop'
if (-not $ProjectRoot) { $ProjectRoot = Join-Path $PSScriptRoot '..' }
Add-Type -Path (Join-Path $ProjectRoot 'SmrValues.cs')
function Assert-Equal($actual, $expected) {
    if ($actual -cne $expected) { throw "Expected [$expected], got [$actual]" }
}
$value = [LinkArea2024.SmrValues]::Create('4', '1', '')
Assert-Equal $value.Block 'Корпус 4'
Assert-Equal $value.Section 'Секция 1'
Assert-Equal $value.Description 'Корпус 4_Секция 1'
Assert-Equal $value.Parameters()['ФОП_Блок СМР'] 'Корпус 4'
Assert-Equal $value.Parameters()['ФОП_Секция СМР'] 'Секция 1'
Assert-Equal $value.Parameters()['ФОП_Описание СМР'] 'Корпус 4_Секция 1'
$value = [LinkArea2024.SmrValues]::Create(' Корпус  9.3 ', 'Секция 7', '')
Assert-Equal $value.Description 'Корпус 9.3_Секция 7'
foreach ($prefix in @('ОБЩ_', 'АР_', 'КР_', 'ВИС_')) {
    $value = [LinkArea2024.SmrValues]::Create('корпус 4', 'секция 1', $prefix)
    Assert-Equal $value.Description ($prefix + 'Корпус 4_Секция 1')
}
foreach ($invalid in @(@('', '1', ''), @('4', '', ''), @('Корпус', '1', ''), @('4', 'Секция', ''), @('4', '1', 'UNKNOWN_'))) {
    $rejected = $false
    try { [LinkArea2024.SmrValues]::Create($invalid[0], $invalid[1], $invalid[2]) | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid SMR input accepted' }
}
Write-Output 'PASS: exact parameter mapping; numeric/full labels; fractional building; whitespace; four prefixes; missing fields and invalid prefix rejected.'
