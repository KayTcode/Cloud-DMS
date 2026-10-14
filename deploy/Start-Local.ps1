[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require-Command {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $InstallHint
    )

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Missing '$Name'. $InstallHint"
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frontendRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '..\Cloud-DMS-FE'))
$composeFile = Join-Path $PSScriptRoot 'compose.local.yml'
$composeProject = 'cloud-dms'
$frontendEnvironment = Join-Path $frontendRoot '.env'
$frontendPackage = Join-Path $frontendRoot 'package.json'

Require-Command -Name 'docker' -InstallHint 'Install Docker Desktop and enable Linux containers.'
Require-Command -Name 'npm' -InstallHint 'Install Node.js LTS with npm.'

& docker compose version | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker Compose V2 is unavailable. Start Docker Desktop and try again.'
}

if (-not (Test-Path -LiteralPath $frontendRoot -PathType Container)) {
    throw "Frontend checkout was not found at '$frontendRoot'. Place Cloud-DMS-FE next to Cloud-DMS."
}

if (-not (Test-Path -LiteralPath $frontendPackage -PathType Leaf)) {
    throw "Frontend package.json was not found at '$frontendRoot'. Check the Cloud-DMS-FE checkout."
}

if (-not (Test-Path -LiteralPath $frontendEnvironment -PathType Leaf)) {
    throw "Frontend config '$frontendEnvironment' was not found. Create the local .env required by the frontend before starting."
}

& docker compose --project-name $composeProject -f $composeFile up --build -d
if ($LASTEXITCODE -ne 0) {
    throw 'Docker Compose could not start the backend/dependencies. Review Docker Desktop logs without sharing secret config files.'
}

Push-Location $frontendRoot
try {
    & npm start
    if ($LASTEXITCODE -ne 0) {
        throw "Frontend npm start ended with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
