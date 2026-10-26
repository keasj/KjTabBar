param(
    [Parameter(Mandatory=$true)][string]$MSBuildPath,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'KjTabBar/KjTabBar.csproj'
$intermediate = Join-Path $root "KjTabBar/obj/$Configuration/net481"

& $MSBuildPath $project /restore /t:Rebuild "/p:Configuration=$Configuration" /m:1 /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Normal rebuild failed.' }

# The local-type XAML requires pass 2, which design-time Compile does not run.
$requiredBaml = Join-Path $intermediate 'Views/TabBarWindow.baml'
if (-not (Test-Path -LiteralPath $requiredBaml)) { throw 'Normal rebuild did not generate TabBarWindow.baml.' }
$generated = @(Get-ChildItem $intermediate -Recurse -File | Where-Object {
    ($_.Name -like '*.baml' -or $_.Name -like '*.g.cs') -and
    $_.FullName -notmatch '[\\/]DesignTime[\\/]'
})
$hashes = @{}
foreach ($file in $generated) { $hashes[$file.FullName] = (Get-FileHash $file.FullName).Hash }

$outputJson = & $MSBuildPath $project '/t:Compile;BuiltProjectOutputGroup' "/p:Configuration=$Configuration" /p:DesignTimeBuild=true /p:BuildingInsideVisualStudio=true /p:BuildingProject=false /p:SkipCompilerExecution=true /p:ProvideCommandLineArgs=true /m:1 /v:quiet /nologo /getTargetResult:BuiltProjectOutputGroup
if ($LASTEXITCODE -ne 0) { throw 'Design-time compile failed.' }
$output = ($outputJson -join "`n") | ConvertFrom-Json
$keyOutput = @($output.TargetResults.BuiltProjectOutputGroup.Items | Where-Object IsKeyOutput -eq 'true')
if ($keyOutput.Count -ne 1 -or $keyOutput[0].FullPath -ne (Join-Path $intermediate 'KjTabBar.exe')) {
    throw 'Setup output discovery no longer points to the normal assembly.'
}
if (-not (Test-Path -LiteralPath $keyOutput[0].FullPath)) { throw 'Setup assembly output is missing.' }
foreach ($path in $hashes.Keys) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Design-time compile deleted normal build output: $path" }
    if ((Get-FileHash $path).Hash -ne $hashes[$path]) { throw "Design-time compile changed normal build output: $path" }
}
Write-Output "${Configuration}: $($hashes.Count) normal XAML outputs preserved after design-time compile."
