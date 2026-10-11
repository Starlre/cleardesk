param()
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskVersion = (Get-Content -LiteralPath (Join-Path $taskRepo 'global.json') -Raw | ConvertFrom-Json).sdk.version
$taskTools = Join-Path $taskRepo '.tools'
$taskSdk = Join-Path $taskTools 'dotnet'
$taskDotnet = Join-Path $taskSdk 'dotnet.exe'
$env:DOTNET_CLI_HOME = Join-Path $taskTools 'cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
if ((Test-Path -LiteralPath $taskDotnet) -and ((& $taskDotnet --list-sdks) -match [regex]::Escape($taskVersion))) {
    Write-Output "Local .NET SDK $taskVersion is ready."
    exit 0
}
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
$taskMetadataPath = Join-Path $taskTools 'releases-10.json'
& curl.exe --fail --silent --show-error --location --retry 2 --connect-timeout 15 --max-time 60 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' --output $taskMetadataPath
if ($LASTEXITCODE -ne 0) { throw 'Failed to download official SDK metadata.' }
$taskMetadata = Get-Content -LiteralPath $taskMetadataPath -Raw | ConvertFrom-Json
$taskFile = $taskMetadata.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $taskVersion } |
    ForEach-Object { $_.files } | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if ($null -eq $taskFile -or $taskFile.url -notlike 'https://builds.dotnet.microsoft.com/*') { throw 'Pinned SDK archive not found in official metadata.' }
$taskArchive = Join-Path $taskTools "dotnet-sdk-$taskVersion-win-x64.zip"
& curl.exe --fail --silent --show-error --location --retry 2 --connect-timeout 15 --max-time 900 $taskFile.url --output $taskArchive
if ($LASTEXITCODE -ne 0) { throw 'Failed to download the SDK archive.' }
if ((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA512).Hash -ne $taskFile.hash) { throw 'SDK SHA512 mismatch.' }
New-Item -ItemType Directory -Path $taskSdk -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($taskArchive, $taskSdk)
Write-Output "Local .NET SDK $taskVersion extracted to $taskSdk."
