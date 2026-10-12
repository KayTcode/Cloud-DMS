$ErrorActionPreference = "Stop"

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$targetDirectory = Join-Path $repositoryRoot "deploy\.local"
$targetPath = Join-Path $targetDirectory ".env"
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
$randomNumberGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
$mysqlBytes = New-Object byte[] 32
$redisBytes = New-Object byte[] 32

New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null

$randomNumberGenerator.GetBytes($mysqlBytes)
$randomNumberGenerator.GetBytes($redisBytes)
$randomNumberGenerator.Dispose()
$mysqlPassword = [Convert]::ToBase64String($mysqlBytes)
$redisPassword = [Convert]::ToBase64String($redisBytes)
$contents = "MYSQL_TEST_ROOT_PASSWORD=$mysqlPassword`nREDIS_TEST_PASSWORD=$redisPassword`n"
[System.IO.File]::WriteAllText($targetPath, $contents, $utf8WithoutBom)

Write-Output "Generated local integration-test secrets at deploy/.local/.env"
