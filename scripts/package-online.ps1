param([switch]$NoPublish)
. (Join-Path $PSScriptRoot 'common.ps1')
$taskBuild = Join-Path $script:ClearDeskRoot 'artifacts/online-package-build'
$taskApp = Join-Path $taskBuild 'app'
$taskRelease = Join-Path $script:ClearDeskRoot 'artifacts/releases'
New-Item -ItemType Directory -Path $taskRelease -Force | Out-Null
if (-not $NoPublish) {
    & $script:ClearDeskDotnet publish (Join-Path $script:ClearDeskRoot 'experiments/GlassWindow/ClearDesk.GlassWindow.csproj') `
        --configuration Release --runtime win-x64 --self-contained false `
        -p:Platform=x64 -p:PublishSingleFile=false -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false -p:VCRTForwarders-IncludeDebugCRT=false -p:NuGetAudit=false `
        "-p:BaseOutputPath=$taskBuild/bin/" "-p:MSBuildProjectExtensionsPath=$taskBuild/obj/" --output $taskApp
    if ($LASTEXITCODE -ne 0) { throw 'Framework-dependent publish failed.' }
}
foreach ($taskFile in @('ClearDesk.GlassWindow.exe', 'ClearDesk.GlassWindow.dll', 'Microsoft.Graphics.Canvas.dll', 'Assets/ClearDesk.ico')) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskApp $taskFile))) { throw "Missing app dependency: $taskFile" }
}
if (Test-Path -LiteralPath (Join-Path $taskApp 'coreclr.dll')) { throw 'Online app directory must not contain a self-contained runtime.' }
$taskZip = Join-Path $taskBuild 'payload.zip'
if (Test-Path -LiteralPath $taskZip) { Remove-Item -LiteralPath $taskZip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($taskApp, $taskZip, [IO.Compression.CompressionLevel]::Optimal, $false)
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExe = Join-Path $taskRelease 'ClearDesk-Online.exe'
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /define:ONLINE `
    "/out:$taskExe" "/win32icon:$(Join-Path $script:ClearDeskRoot 'experiments/GlassWindow/Assets/ClearDesk.ico')" `
    "/resource:$taskZip,ClearDesk.Payload.zip" `
    "/resource:$(Join-Path $script:ClearDeskRoot 'packaging/runtime-downloads.txt'),ClearDesk.Runtime.txt" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
    (Join-Path $script:ClearDeskRoot 'packaging/PortableLauncher.cs') (Join-Path $script:ClearDeskRoot 'packaging/RuntimeBootstrap.cs')
if ($LASTEXITCODE -ne 0) { throw 'Online EXE compilation failed.' }
Get-FileHash -LiteralPath $taskExe -Algorithm SHA256 | ForEach-Object {
    [IO.File]::WriteAllText((Join-Path $taskRelease 'ClearDesk-Online.exe.sha256'), "$($_.Hash.ToLowerInvariant())  ClearDesk-Online.exe`n")
}
Write-Output "Online EXE created: $taskExe"
Write-Output "Size: $([Math]::Round((Get-Item -LiteralPath $taskExe).Length / 1MB, 1)) MiB"
