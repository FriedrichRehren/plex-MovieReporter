param(
    [string]$ImageName = "movie-reporter-web",
    [string]$Tag = "latest",
    [string]$Dockerfile = "MovieReporter.Web/Dockerfile"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = $PSScriptRoot
$dockerfilePath = Join-Path $repositoryRoot $Dockerfile
$imageReference = "${ImageName}:${Tag}"

if (!(Test-Path $dockerfilePath)) {
    throw "Dockerfile not found: $dockerfilePath"
}

docker build `
  --file $dockerfilePath `
  --tag $imageReference `
  $repositoryRoot

Write-Host "Built image $imageReference"
Write-Host ""
Write-Host "Example run command:"
Write-Host "docker run --rm -p 8080:8080 -e MOVIE_REPORTER_SOURCE_LIBRARY=/media -v <host-library>:/media:ro -v <host-downloads>:/home/app/Downloads $imageReference"
