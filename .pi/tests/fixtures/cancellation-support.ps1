# Disposable headless fixture only: loaded instead of support.ps1 in a TEMP copy
# of the extension's directory layout. Never reachable from the production tool.
param([ValidateSet('doctor')][string] $Operation)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
[IO.File]::WriteAllText((Join-Path $root 'root.pid'), [string]$PID)
$start = [Diagnostics.ProcessStartInfo]::new((Get-Command node -CommandType Application | Select-Object -First 1).Source)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.ArgumentList.Add((Join-Path $PSScriptRoot 'cancellation-tree.mjs'))
$start.ArgumentList.Add($root)
$start.ArgumentList.Add('child')
$child = [Diagnostics.Process]::Start($start)
try {
    # The child publishes ready.json only after its own child has announced readiness.
    # Stay alive long enough that the regression cannot mistake a natural exit for kill.
    if (-not $child.WaitForExit(60000)) { throw 'Fixture safety deadline reached.' }
} finally {
    if (-not $child.HasExited) { $child.Kill($true) }
    $child.Dispose()
}
