$ErrorActionPreference = 'Stop'
$script:ClearDeskRoot = Split-Path -Parent $PSScriptRoot
$script:ClearDeskDotnet = Join-Path $script:ClearDeskRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $script:ClearDeskDotnet)) {
    $taskInstalledDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $taskInstalledDotnet) { throw 'Run scripts/setup-sdk.ps1 to prepare the .NET 10 SDK.' }
    $script:ClearDeskDotnet = $taskInstalledDotnet.Source
}
$env:DOTNET_CLI_HOME = Join-Path $script:ClearDeskRoot '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $script:ClearDeskRoot '.tools/nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
if ($script:ClearDeskDotnet -eq (Join-Path $script:ClearDeskRoot '.tools/dotnet/dotnet.exe')) {
    $env:DOTNET_ROOT = Join-Path $script:ClearDeskRoot '.tools/dotnet'
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
}
