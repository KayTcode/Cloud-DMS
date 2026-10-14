[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($null -eq (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker was not found. Install Docker Desktop to stop local backend/dependencies.'
}

$composeFile = Join-Path $PSScriptRoot 'compose.local.yml'
$composeProject = 'cloud-dms'

& docker compose --project-name $composeProject -f $composeFile stop
if ($LASTEXITCODE -ne 0) {
    throw 'Docker Compose could not stop local backend/dependencies.'
}

Write-Output 'Local backend/dependencies stopped; containers, volumes and images were kept.'
