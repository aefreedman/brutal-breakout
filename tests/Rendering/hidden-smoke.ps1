# Windows-only hidden GPU regression. Never activates or shows a window.
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug',
      [ValidateSet('Showcase', 'Gameplay')][string] $Mode = 'Showcase',
      [string] $PhysicsConfig,
      [string] $GameplayConfig,
      [string] $InputConfig)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Requires Windows/PowerShell 7.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$exe = Join-Path $root "bin/$Configuration/net10.0/brutal-breakout.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Build first: dotnet build -c $Configuration" }
$physicsPath = $null
$physicsExpected = $null
$gameplayPath = $null
$gameplayExpected = $null
$inputPath = $null
$inputExpected = $null
if ($Mode -eq 'Gameplay') {
    # Deterministic test fixture, independent of the editable production physics.json.
    $physicsPath = if ([string]::IsNullOrWhiteSpace($PhysicsConfig)) {
        Join-Path $root 'tests/Gameplay/Fixtures/physics-default.json'
    } else { [IO.Path]::GetFullPath($PhysicsConfig) }
    $physicsExpected = [IO.File]::ReadAllText($physicsPath) | ConvertFrom-Json
    $gameplayPath = if ([string]::IsNullOrWhiteSpace($GameplayConfig)) {
        Join-Path $root 'tests/Gameplay/Fixtures/gameplay-default.json'
    } else { [IO.Path]::GetFullPath($GameplayConfig) }
    $inputPath = if ([string]::IsNullOrWhiteSpace($InputConfig)) {
        Join-Path $root 'tests/Gameplay/Fixtures/input-default.json'
    } else { [IO.Path]::GetFullPath($InputConfig) }
    $gameplayExpected = [IO.File]::ReadAllText($gameplayPath) | ConvertFrom-Json
    $inputExpected = [IO.File]::ReadAllText($inputPath) | ConvertFrom-Json
} elseif (-not [string]::IsNullOrWhiteSpace($PhysicsConfig) -or
          -not [string]::IsNullOrWhiteSpace($GameplayConfig) -or -not [string]::IsNullOrWhiteSpace($InputConfig)) {
    throw 'Configuration overrides apply only to -Mode Gameplay.'
}
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
    if ($Mode -eq 'Gameplay') {
        $start.ArgumentList.Add('--physics-config')
        $start.ArgumentList.Add($physicsPath)
        $start.ArgumentList.Add('--gameplay-config')
        $start.ArgumentList.Add($gameplayPath)
        $start.ArgumentList.Add('--input-config')
        $start.ArgumentList.Add($inputPath)
    }
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
        $loaded = [regex]::Match($output, 'Physics configuration: updateHz=([^;]+); stepSeconds=([^;]+); substeps=(\d+); maxCatchUpSteps=(\d+); contactHertz=([^;]+); contactDampingRatio=([^;]+); path=([^\r\n]+)')
        if (-not $loaded.Success) { throw 'Missing actual loaded physics settings.' }
        $culture = [Globalization.CultureInfo]::InvariantCulture
        $hz = [double]::Parse($loaded.Groups[1].Value, $culture)
        $step = [double]::Parse($loaded.Groups[2].Value, $culture)
        $expectedStep = 1.0 / [double]$physicsExpected.updateHz
        if ($hz -ne [double]$physicsExpected.updateHz -or
            [Math]::Abs($step - $expectedStep) -gt [Math]::Abs($expectedStep) * 1e-12 -or
            [int]$loaded.Groups[3].Value -ne [int]$physicsExpected.substeps -or
            [int]$loaded.Groups[4].Value -ne [int]$physicsExpected.maxCatchUpSteps -or
            [float]::Parse($loaded.Groups[5].Value, $culture) -ne [float]$physicsExpected.contactHertz -or
            [float]::Parse($loaded.Groups[6].Value, $culture) -ne [float]$physicsExpected.contactDampingRatio -or
            $loaded.Groups[7].Value -ne $physicsPath) {
            throw 'Loaded physics settings/path do not match the selected fixture.'
        }
        Write-Output "Verified configured gameplay: updateHz=$hz; substeps=$($loaded.Groups[3].Value); maxCatchUpSteps=$($loaded.Groups[4].Value)."
        $game = [regex]::Match($output, 'Gameplay configuration: width=([^;]+); height=([^;]+); left=([^;]+); top=([^;]+); right=([^;]+); bottom=([^;]+); wallThickness=([^;]+); radius=([^;]+); speed=([^;]+); spawnX=([^;]+); spawnY=([^;]+); initialAimX=([^;]+); initialAimY=([^;]+); path=([^\r\n]+)')
        $loadedInput = [regex]::Match($output, 'Input configuration: stickDeadzone=([^;]+); path=([^\r\n]+)')
        $probe = [regex]::Match($output, 'Gameplay input probe PASS: stickDeadzone=([^;]+); inside=None; outside=Direction\.')
        $motion = [regex]::Match($output, 'Gameplay launch motion: actualSpeed=([^;]+); configuredSpeed=([^\r\n]+)\.')
        if (-not $game.Success -or -not $loadedInput.Success -or -not $probe.Success -or -not $motion.Success) {
            throw 'Missing actual gameplay/input settings, real-router probe or native launch-speed evidence.'
        }
        $a = $gameplayExpected.arena
        $b = $gameplayExpected.ball
        $scale = [Math]::Max([Math]::Abs([double]$b.initialAimX), [Math]::Abs([double]$b.initialAimY))
        $ax = [double]$b.initialAimX / $scale
        $ay = [double]$b.initialAimY / $scale
        $length = [Math]::Sqrt($ax * $ax + $ay * $ay)
        $expected = @($a.width, $a.height, $a.left, $a.top, $a.right, $a.bottom, $a.wallThickness,
                      $b.radius, $b.speed, $b.spawnX, $b.spawnY, ($ax / $length), ($ay / $length))
        for ($i = 0; $i -lt $expected.Count; $i++) {
            $actual = [double]::Parse($game.Groups[$i + 1].Value, $culture)
            if ([Math]::Abs($actual - [double]$expected[$i]) -gt [Math]::Max(1.0, [Math]::Abs([double]$expected[$i])) * 1e-12) {
                throw "Loaded gameplay field $i does not match selected fixture."
            }
        }
        if ($game.Groups[14].Value -ne $gameplayPath -or $loadedInput.Groups[2].Value -ne $inputPath -or
            [float]::Parse($loadedInput.Groups[1].Value, $culture) -ne [float]$inputExpected.stickDeadzone -or
            [float]::Parse($probe.Groups[1].Value, $culture) -ne [float]$inputExpected.stickDeadzone -or
            [double]::Parse($motion.Groups[2].Value, $culture) -ne [double]$b.speed -or
            [Math]::Abs([double]::Parse($motion.Groups[1].Value, $culture) - [double]$b.speed) -gt [Math]::Max(1.0, [double]$b.speed * 1e-5)) {
            throw 'Loaded gameplay/input paths, effective deadzone or native launch speed differ from fixtures.'
        }
        $reset = [regex]::Match($output, 'Gameplay stage: Reset; ticks=\d+; phase=Aiming; eligible=False; position=([^,;]+),([^;]+);')
        if (-not $reset.Success -or [double]::Parse($reset.Groups[1].Value, $culture) -ne [double]$b.spawnX -or
            [double]::Parse($reset.Groups[2].Value, $culture) -ne [double]$b.spawnY) {
            throw 'Actual reset snapshot did not restore configured spawn.'
        }
        Write-Output "Verified gameplay/input: canvas=$($a.width)x$($a.height); radius=$($b.radius); speed=$($b.speed); spawn=$($b.spawnX),$($b.spawnY); stickDeadzone=$($loadedInput.Groups[1].Value)."
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
            -not $result.Success -or [int]$result.Groups[1].Value -le 0 -or
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
