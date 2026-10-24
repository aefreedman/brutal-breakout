# Windows-only asynchronous swapchain resize regression. Run after building the app.
# Unlike --smoke-test's between-frame resize, this changes the native window while
# the render thread may be inside acquire/present. Requires SDK validation layers.
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug',
    [ValidateRange(1, 5)][int] $Iterations = 2
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This external-resize regression requires Windows/PowerShell 7.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$exe = Join-Path $root "bin/$Configuration/net10.0/brutal-breakout.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Build first: dotnet build -c $Configuration" }
$logs = Join-Path ([IO.Path]::GetTempPath()) ('brutal-resize-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($logs)
Add-Type @'
using System;
using System.Runtime.InteropServices;
namespace BrutalBreakout.Tests {
 public static class ResizeWindow {
  [DllImport("user32.dll", SetLastError=true)]
  public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
 }
}
'@
try {
    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
        $stdout = Join-Path $logs "$iteration.stdout.log"
        $stderr = Join-Path $logs "$iteration.stderr.log"
        $process = Start-Process -FilePath $exe -ArgumentList '--validation' -WorkingDirectory $root -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        # Retain the native handle before exit; otherwise Windows can return null ExitCode.
        $handle = $process.Handle
        try {
            $clock = [Diagnostics.Stopwatch]::StartNew()
            $window = [IntPtr]::Zero
            while ($clock.Elapsed.TotalSeconds -lt 10) {
                $process.Refresh()
                if ($process.HasExited) { throw "Application exited before window readiness: $($process.ExitCode)" }
                $window = $process.MainWindowHandle
                if ($window -ne [IntPtr]::Zero) { break }
                Start-Sleep -Milliseconds 50
            }
            if ($window -eq [IntPtr]::Zero) { throw 'Window readiness timed out.' }
            # Give initialization/presentation time, then reproduce the originally failing resize.
            Start-Sleep -Seconds 3
            if (-not [BrutalBreakout.Tests.ResizeWindow]::SetWindowPos($window, [IntPtr]::Zero, 0, 0, 1600, 760, 6)) {
                throw 'Initial external resize failed.'
            }
            Start-Sleep -Milliseconds 500
            for ($n = 0; $n -lt 40; $n++) {
                $process.Refresh()
                if ($process.HasExited) { throw "Application exited during resize stress: $($process.ExitCode)" }
                if (-not [BrutalBreakout.Tests.ResizeWindow]::SetWindowPos($window, [IntPtr]::Zero, 0, 0, (840 + ($n % 7) * 110), (600 + ($n % 3) * 70), 6)) {
                    throw 'Stress resize failed.'
                }
                Start-Sleep -Milliseconds 40
            }
            Start-Sleep -Milliseconds 500
            $process.Refresh()
            if ($process.HasExited -or -not $process.CloseMainWindow()) { throw 'Application did not survive resize or accept normal close.' }
            if (-not $process.WaitForExit(10000)) { throw 'Application shutdown timed out.' }
            if ($null -eq $process.ExitCode -or $process.ExitCode -ne 0) { throw "Application exit code: $($process.ExitCode)" }
            $output = [IO.File]::ReadAllText($stdout)
            $errors = [IO.File]::ReadAllText($stderr)
            if ($output -notmatch 'Validation: 0 errors, (\d+) warnings\.') { throw 'Missing zero-error validation summary.' }
            $warnings = [int]$Matches[1]
            if ($output -notmatch 'Presented frames: (\d+)\.' -or [int]$Matches[1] -lt 10) { throw 'Insufficient successful presentations.' }
            $frames = [int]$Matches[1]
            if ($errors -match 'VkResultError|\[Vulkan Error') { throw 'Unexpected Vulkan error in stderr.' }
            Write-Output "PASS external resize $iteration/$Iterations ($Configuration): $frames presentations, 0 validation errors, $warnings warnings."
        }
        finally {
            if (-not $process.HasExited) {
                $process.Kill($true)
                [void]$process.WaitForExit(5000)
            }
            $process.Dispose()
        }
    }
}
finally {
    Write-Output "Resize regression logs (including warning details): $logs"
}
