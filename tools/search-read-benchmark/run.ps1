param([string]$Output = (Join-Path $env:TEMP 'cloud-dms-search-read-benchmark.json'))
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$previousOutput = $env:CLOUD_DMS_BENCHMARK_OUTPUT
Push-Location $repository
try {
    $env:CLOUD_DMS_BENCHMARK_OUTPUT = [IO.Path]::GetFullPath($Output)
    dotnet test tests/CloudDms.IntegrationTests/CloudDms.IntegrationTests.csproj --filter FullyQualifiedName~SearchReadEndToEndTests --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark integration failed; no acceptance claim.' }
    Write-Output "Kết quả: $env:CLOUD_DMS_BENCHMARK_OUTPUT"
} finally {
    $env:CLOUD_DMS_BENCHMARK_OUTPUT = $previousOutput
    Pop-Location
}
