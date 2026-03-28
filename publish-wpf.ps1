param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "MovieReporter.WPF\MovieReporter.WPF.csproj"
$publishDir = Join-Path $PSScriptRoot "publish"

dotnet publish $projectPath `
  -c $Configuration `
  -r $Runtime `
  -p:SelfContained=true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:IncludeAllContentForSelfExtract=true `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -o $publishDir

$sourceExe = Join-Path $publishDir "MovieReporter.WPF.exe"
if (!(Test-Path $sourceExe)) {
    $exe = Get-ChildItem -Path $publishDir -Filter *.exe | Select-Object -First 1
    if (-not $exe) {
        throw "Publish output exe not found in $publishDir"
    }
    $sourceExe = $exe.FullName
}

$targetExe = Join-Path $PSScriptRoot "MovieReporter.exe"
if (Test-Path $targetExe) {
    Remove-Item -Path $targetExe -Force
}
Move-Item -Path $sourceExe -Destination $targetExe -Force

Remove-Item -Path $publishDir -Recurse -Force

Write-Host "Created $targetExe"
