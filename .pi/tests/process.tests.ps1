# Safe local seams: no GPU, network, app changes, or long-lived processes.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../scripts/Process.ps1')
$pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
function Assert([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}
$failure = Invoke-BoundedProcess -File $pwsh -Arguments @('-NoProfile', '-Command', 'exit 7') -TimeoutSeconds 10
Assert ($failure.exitCode -eq 7) 'Nonzero child exit was not preserved.'
$timeout = Invoke-BoundedProcess -File $pwsh -Arguments @('-NoProfile', '-Command', 'Start-Sleep -Seconds 3') -TimeoutSeconds 1
Assert ($timeout.timedOut -and $timeout.exitCode -eq 124) 'Timeout did not kill/return 124.'
$output = Invoke-BoundedProcess -File $pwsh -Arguments @('-NoProfile', '-Command', 'Write-Output "WARNING: Layer name GalaxyOverlayVkLayer does not conform to naming standard"; 1..1000 | ForEach-Object { "abcdefghij" }') -TimeoutSeconds 10
Assert ($output.outputTruncated -and $output.outputTail.Length -le 8000) 'Output was not bounded.'
Assert ($output.gogNamingWarnings.Count -eq 1) 'GOG naming warning was lost despite output truncation.'
$invalid = Invoke-BoundedProcess -File $pwsh -Arguments @('-NoProfile', '-File', (Join-Path $PSScriptRoot '../scripts/support.ps1'), '-Operation', 'package-assets', '-PackageId', 'Ahwoo.BRUTAL.Core') -TimeoutSeconds 10
Assert ($invalid.exitCode -ne 0) 'Dispatcher failed to reject missing package version.'
$invalidSummary = $invalid.outputTail | ConvertFrom-Json
Assert ($invalidSummary.exitCode -ne 0 -and $invalidSummary.steps.Count -eq 0) 'Failure summary is not structured or performed unexpected commands.'
Write-Output 'PASS: nonzero propagation, bounded timeout, bounded output, GOG classification, dispatcher failure summary.'
