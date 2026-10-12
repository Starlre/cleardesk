param(
    [switch]$Verify,
    [switch]$NoBuild,
    [switch]$InputTest,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
. (Join-Path $PSScriptRoot 'common.ps1')
$taskRunning = $false
if (-not $Verify) {
    $taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $taskSession = [Diagnostics.Process]::GetCurrentProcess().SessionId
    try {
        $taskGate = [Threading.Mutex]::OpenExisting("Local\ClearDesk-$taskIdentity-$taskSession")
        $taskGate.Dispose()
        $taskRunning = $true
    }
    catch [Threading.WaitHandleCannotBeOpenedException] { }
}
if (-not $NoBuild -and -not $taskRunning) { & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration }
if ($InputTest -and -not $Verify) { throw '-InputTest requires -Verify.' }
$taskApp = Join-Path $script:ClearDeskRoot "experiments/GlassWindow/bin/x64/$Configuration/net10.0-windows10.0.22621.0/ClearDesk.GlassWindow.exe"
if (-not (Test-Path -LiteralPath $taskApp)) { throw 'Application output is missing. Run scripts/build.ps1 first.' }
$taskState = Join-Path $script:ClearDeskRoot 'artifacts/GlassWindow'
$taskArguments = @('--state-root', ('"{0}"' -f $taskState))
if ($Verify) {
    $taskEvidence = Join-Path $taskState 'verification'
    $taskArguments += @('--smoke-test', '--output', ('"{0}"' -f $taskEvidence))
    if ($InputTest) {
        $taskArguments += '--input-test'
        Write-Output 'Pointer verification moves the cursor within its own window and restores it afterward.'
    }
    Write-Output 'Verification briefly displays its own windows and exits automatically.'
    $taskProcess = Start-Process -FilePath $taskApp -ArgumentList $taskArguments -WorkingDirectory $script:ClearDeskRoot -WindowStyle Normal -PassThru
    if (-not $taskProcess.WaitForExit(60000)) { $taskProcess.Kill(); throw 'Verification exceeded 60 seconds; its process was stopped.' }
    if ($taskProcess.ExitCode -ne 0) {
        $taskFailure = Join-Path $taskEvidence 'error.txt'
        if (Test-Path -LiteralPath $taskFailure) { Get-Content -LiteralPath $taskFailure }
        throw "Verification failed with exit code $($taskProcess.ExitCode)."
    }
    Get-Content -LiteralPath (Join-Path $taskEvidence 'smoke-report.json')
}
else {
    $taskProcess = Start-Process -FilePath $taskApp -ArgumentList $taskArguments -WorkingDirectory $script:ClearDeskRoot -WindowStyle Normal -PassThru
    if ($taskRunning) {
        if (-not $taskProcess.WaitForExit(5000)) { throw 'Existing-instance restore request did not complete within 5 seconds.' }
        if ($taskProcess.ExitCode -ne 0) { throw 'Existing-instance restore request failed.' }
        Write-Output 'ClearDesk is already running; restored its windows without rebuilding or creating another tray icon.'
    }
    else { Write-Output "ClearDesk started (PID $($taskProcess.Id)). Close hides to the tray; use the tray menu to exit." }
}
