$ErrorActionPreference = 'Stop'
$script:GlassDeskRoot = Split-Path -Parent $PSScriptRoot
$script:GlassDeskDotnet = Join-Path $script:GlassDeskRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $script:GlassDeskDotnet)) {
    $taskInstalledDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $taskInstalledDotnet) { throw 'Run scripts/setup-sdk.ps1 to prepare the .NET 10 SDK.' }
    $script:GlassDeskDotnet = $taskInstalledDotnet.Source
}
$env:DOTNET_CLI_HOME = Join-Path $script:GlassDeskRoot '.tools/cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
if ($script:GlassDeskDotnet -eq (Join-Path $script:GlassDeskRoot '.tools/dotnet/dotnet.exe')) {
    $env:DOTNET_ROOT = Join-Path $script:GlassDeskRoot '.tools/dotnet'
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
}
