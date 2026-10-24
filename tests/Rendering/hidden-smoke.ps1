# Windows-only hidden GPU regression. Never activates or shows a window.
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug',
      [ValidateSet('Showcase', 'Gameplay')][string] $Mode = 'Showcase')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Requires Windows/PowerShell 7.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$exe = Join-Path $root "bin/$Configuration/net10.0/brutal-breakout.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Build first: dotnet build -c $Configuration" }
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace BrutalBreakout.Tests {
 public static class HiddenWindow {
  public delegate bool Callback(IntPtr window, IntPtr data);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback, IntPtr data);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
  public static IntPtr[] Windows(uint process) {
   var windows = new List<IntPtr>();
   EnumWindows((window, data) => { uint pid; GetWindowThreadProcessId(window, out pid);
    if (pid == process) windows.Add(window); return true; }, IntPtr.Zero);
   return windows.ToArray();
  }
 }
}
'@
$logs = Join-Path ([IO.Path]::GetTempPath()) ('brutal-hidden-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($logs)
$stdout = Join-Path $logs 'stdout.log'
$stderr = Join-Path $logs 'stderr.log'
$before = [BrutalBreakout.Tests.HiddenWindow]::GetForegroundWindow()
$foregrounds = [Collections.Generic.HashSet[long]]::new()
$windows = [Collections.Generic.HashSet[long]]::new()
$visible = [Collections.Generic.HashSet[long]]::new()
$process = $null
try {
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.ArgumentList.Add($(if ($Mode -eq 'Gameplay') { '--gameplay-smoke-test' } else { '--smoke-test' }))
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true # suppress the executable's console as well as GLFW's window
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $outputTask = $process.StandardOutput.ReadToEndAsync()
    $errorTask = $process.StandardError.ReadToEndAsync()
    $handle = $process.Handle # retain ExitCode after process exit
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        [void]$foregrounds.Add([BrutalBreakout.Tests.HiddenWindow]::GetForegroundWindow().ToInt64())
        foreach ($window in [BrutalBreakout.Tests.HiddenWindow]::Windows($process.Id)) {
            [void]$windows.Add($window.ToInt64())
            if ([BrutalBreakout.Tests.HiddenWindow]::IsWindowVisible($window)) { [void]$visible.Add($window.ToInt64()) }
        }
        if ($clock.Elapsed.TotalSeconds -gt 25) { throw 'Hidden smoke hard deadline exceeded.' }
        Start-Sleep -Milliseconds 10
        $process.Refresh()
    } while (-not $process.HasExited)
    if (-not $process.WaitForExit(5000)) { throw 'Output drain timed out.' }
    $after = [BrutalBreakout.Tests.HiddenWindow]::GetForegroundWindow()
    [void]$foregrounds.Add($after.ToInt64())
    if (-not $outputTask.Wait(5000) -or -not $errorTask.Wait(5000)) { throw 'Output read timed out.' }
    $output = $outputTask.Result
    $errors = $errorTask.Result
    [IO.File]::WriteAllText($stdout, $output)
    [IO.File]::WriteAllText($stderr, $errors)
    Write-Output $output
    Write-Output $errors
    Write-Output "Window metadata ($Configuration): before=$before; during/after=$($foregrounds -join ','); after=$after; test handles=$($windows -join ','); visible test handles=$($visible -join ',')."
    if ($process.ExitCode -ne 0) { throw "Hidden smoke exit code: $($process.ExitCode)" }
    if ($windows.Count -eq 0) { throw 'No test window observed; hidden-state evidence incomplete.' }
    if ($visible.Count -ne 0) { throw 'A test window became visible.' }
    if ($foregrounds.Count -ne 1 -or -not $foregrounds.Contains($before.ToInt64())) { throw 'Foreground changed during observation (possibly external user activity); nonactivation evidence incomplete.' }
    if ($Mode -eq 'Showcase') {
    $expected = [regex]::Match($output, 'Smoke showcase: expectedShapes=(\d+);')
    $frames = [regex]::Match($output, 'Smoke: (\d+) shapes/frame, (\d+) nonempty frames; hidden resize and resumed presentation passed')
    $viewports = [regex]::Matches($output, 'Smoke viewport: (\d+)x(\d+); vertices=(\d+); indices=(\d+); commands=(\d+)\.')
    if (-not $expected.Success -or [int]$expected.Groups[1].Value -le 0 -or
        -not $frames.Success -or $frames.Groups[1].Value -ne $expected.Groups[1].Value -or
        [int]$frames.Groups[2].Value -lt 9 -or $viewports.Count -lt 2 -or
        $output -notmatch 'Validation: 0 errors, \d+ warnings\.') { throw 'Missing shape/resize/validation evidence.' }
    $sizes = [Collections.Generic.HashSet[string]]::new()
    foreach ($viewport in $viewports) {
        if ([int]$viewport.Groups[1].Value -le 0 -or [int]$viewport.Groups[2].Value -le 0 -or
            [int]$viewport.Groups[3].Value -lt 3 * [int]$expected.Groups[1].Value -or
            [int]$viewport.Groups[4].Value -lt 3 * [int]$expected.Groups[1].Value -or
            [int]$viewport.Groups[5].Value -lt 5) { throw 'Empty/incomplete showcase viewport.' }
        [void]$sizes.Add("$($viewport.Groups[1].Value)x$($viewport.Groups[2].Value)")
    }
    if ($sizes.Count -lt 2) { throw 'Missing distinct nonempty resize viewports.' }
    } else {
        $receipt = 'Gameplay receipt regression PASS: clearOnlyPresented=True; geometryPresented=False; nonemptyPresentedFrames=0; viewports=0; stage=Initial.'
        if (-not $output.Contains($receipt) -or
            $output -notmatch 'Shape frame 1281x720 skipped after resize; rebuild at 1280x720\.' -or
            $output.IndexOf($receipt) -gt $output.IndexOf('Gameplay stage: Initial;') -or
            $output.IndexOf($receipt) -gt $output.IndexOf('Gameplay viewport:')) {
            throw 'Missing actual stale-frame clear-only receipt regression or evidence advanced before receipt.'
        }
        $stages = @('Initial', 'Launch', 'Travel', 'Catch', 'Hold', 'Release', 'Relaunch', 'Reset', 'Resized')
        foreach ($stage in $stages) {
            if ($output -notmatch "Gameplay stage: $stage; ticks=\d+; phase=(Aiming|Flying);") {
                throw "Missing presented gameplay stage: $stage"
            }
        }
        $viewports = [regex]::Matches($output, 'Gameplay viewport: (\d+)x(\d+); indices=(\d+); arenaIndices=(\d+); ballIndices=(\d+); aimIndices=(\d+)\.')
        $sizes = [Collections.Generic.HashSet[string]]::new()
        foreach ($viewport in $viewports) {
            if ([int]$viewport.Groups[3].Value -le 0 -or [int]$viewport.Groups[4].Value -le 0 -or
                [int]$viewport.Groups[5].Value -le 0 -or [int]$viewport.Groups[6].Value -le 0) {
                throw 'Missing actual arena/ball/aim indexed geometry.'
            }
            [void]$sizes.Add("$($viewport.Groups[1].Value)x$($viewport.Groups[2].Value)")
        }
        $result = [regex]::Match($output, 'Gameplay: launch wall catch hold release relaunch reset passed; ticks=(\d+); nonemptyPresentedFrames=(\d+); hidden resize and resumed presentation passed\.')
        if (-not $sizes.Contains('1280x720') -or -not $sizes.Contains('960x540') -or
            -not $result.Success -or [int]$result.Groups[1].Value -lt 100 -or
            [int]$result.Groups[2].Value -lt 9 -or $output -notmatch 'Validation: 0 errors, \d+ warnings\.') {
            throw 'Missing gameplay transitions/resize/validation evidence.'
        }
    }
    if ($errors -match 'VkResultError|\[Vulkan Error') { throw 'Unexpected Vulkan error.' }
    Write-Output "PASS hidden GPU smoke ($Configuration/$Mode): foreground unchanged; no visible test windows observed (10ms sampling)."
}
finally {
    if ($null -ne $process) {
        if (-not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(5000) }
        $process.Dispose()
    }
    Write-Output "Hidden regression logs: $logs"
}
