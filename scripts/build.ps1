param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
. (Join-Path $PSScriptRoot 'common.ps1')
Push-Location $script:GlassDeskRoot
try {
    & $script:GlassDeskDotnet build GlassDesk.sln --configuration $Configuration --ignore-failed-sources -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }
