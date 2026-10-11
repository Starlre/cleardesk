param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
. (Join-Path $PSScriptRoot 'common.ps1')
Push-Location $script:ClearDeskRoot
try {
    & $script:ClearDeskDotnet build ClearDesk.sln --configuration $Configuration --ignore-failed-sources -p:NuGetAudit=false -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }
