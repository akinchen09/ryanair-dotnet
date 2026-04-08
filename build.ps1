#Requires -Version 5.1
<#
.SYNOPSIS
    Builds and pushes the Ryanair Payments Docker image (Windows containers).
.PARAMETER Tag
    Docker image tag (default: latest)
.PARAMETER Push
    Whether to push to Docker Hub after building
.PARAMETER DockerHubUser
    Docker Hub username (default: aaronkinchen)
.EXAMPLE
    .\build.ps1 -Push
    .\build.ps1 -Tag "1.2.0" -Push
#>
param(
    [string]$Tag = "latest",
    [switch]$Push,
    [string]$DockerHubUser = "aaronkinchen"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ImageName  = "$DockerHubUser/ryanair-payments"
$FullTag    = "${ImageName}:${Tag}"
$ScriptDir  = $PSScriptRoot

Write-Host "==> Building Windows container image: $FullTag" -ForegroundColor Cyan

# Verify Docker is in Windows container mode
$dockerInfo = docker info --format '{{.OSType}}' 2>&1
if ($dockerInfo -ne "windows") {
    Write-Warning "Docker is currently in Linux container mode."
    Write-Warning "Switch to Windows containers: Docker Desktop tray icon -> 'Switch to Windows containers...'"
    exit 1
}

# Build
docker build -t $FullTag -f "$ScriptDir\Dockerfile" $ScriptDir
if ($LASTEXITCODE -ne 0) { Write-Error "Docker build failed"; exit 1 }

# Also tag with git SHA if available
$sha = git -C $ScriptDir rev-parse --short HEAD 2>$null
if ($sha) {
    $shaTag = "${ImageName}:${sha}"
    docker tag $FullTag $shaTag
    Write-Host "==> Tagged: $shaTag" -ForegroundColor Green
}

Write-Host "==> Build complete: $FullTag" -ForegroundColor Green

if ($Push) {
    Write-Host "==> Pushing to Docker Hub..." -ForegroundColor Cyan
    docker push $FullTag
    if ($LASTEXITCODE -ne 0) { Write-Error "Docker push failed"; exit 1 }

    if ($sha) {
        docker push $shaTag
    }
    Write-Host "==> Push complete" -ForegroundColor Green
}
