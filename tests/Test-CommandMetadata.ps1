$ErrorActionPreference = 'Stop'
$projectDir = Join-Path $PSScriptRoot '..'
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve({
    param($sender, $resolveEvent)
    $name = [Reflection.AssemblyName]::new($resolveEvent.Name)
    $candidate = Join-Path 'D:\Programs\Revit 2024' ($name.Name + '.dll')
    if (Test-Path -LiteralPath $candidate) { return [Reflection.Assembly]::ReflectionOnlyLoadFrom($candidate) }
    return [Reflection.Assembly]::ReflectionOnlyLoad($resolveEvent.Name)
})
$api = [Reflection.Assembly]::ReflectionOnlyLoadFrom('D:\Programs\Revit 2024\RevitAPI.dll')
$apiUi = [Reflection.Assembly]::ReflectionOnlyLoadFrom('D:\Programs\Revit 2024\RevitAPIUI.dll')
$commandType = $apiUi.GetType('Autodesk.Revit.UI.IExternalCommand')
$assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom((Resolve-Path (Join-Path $projectDir 'dist\LinkArea2024_Universal.dll')))
$commands = @($assembly.GetExportedTypes() | Where-Object { $commandType.IsAssignableFrom($_) -and -not $_.IsAbstract })
if ($commands.Count -ne 1 -or $commands[0].FullName -ne 'LinkArea2024.UniversalCommand') {
    throw 'Expected one public external command'
}
$attribute = @($commands[0].GetCustomAttributesData() | Where-Object { $_.AttributeType.FullName -eq 'Autodesk.Revit.Attributes.TransactionAttribute' })
$manualValue = $api.GetType('Autodesk.Revit.Attributes.TransactionMode').GetField('Manual').GetRawConstantValue()
if ($attribute.Count -ne 1 -or [int]$attribute[0].ConstructorArguments[0].Value -ne [int]$manualValue) {
    throw 'Universal command must use Manual transaction mode'
}
Write-Output 'PASS: one exported Revit command, UniversalCommand, in Manual mode. No command was executed in Revit.'
