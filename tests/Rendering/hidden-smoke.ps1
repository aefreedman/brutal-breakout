# Windows-only hidden GPU regression. Never activates or shows a window.
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug',
      [ValidateSet('Showcase', 'Gameplay')][string] $Mode = 'Showcase',
      [string] $PhysicsConfig,
      [string] $ExecutionConfig,
      [string] $GameplayConfig,
      [string] $InputConfig,
      [string] $ColorsConfig,
      [string] $StyleConfig,
      [string] $WindowConfig,
      [switch] $Overlays)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Requires Windows/PowerShell 7.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$exe = Join-Path $root "bin/$Configuration/net10.0/brutal-breakout.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Build first: dotnet build -c $Configuration" }
$windowPath = if ([string]::IsNullOrWhiteSpace($WindowConfig)) { Join-Path $root 'tests/Gameplay/Fixtures/window-default.json' } else { [IO.Path]::GetFullPath($WindowConfig) }
$windowExpected = [IO.File]::ReadAllText($windowPath) | ConvertFrom-Json
# Stale-extent regression is deliberately a fixed 1281 -> 1280 receipt test.
if ($windowExpected.width -ne 1280 -or $windowExpected.height -ne 720) { throw 'Hidden receipt harness requires the 1280x720 fixture; custom sizes are covered by CPU startup tests.' }
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
    $executionPath = if ([string]::IsNullOrWhiteSpace($ExecutionConfig)) {
        Join-Path $root 'tests/Gameplay/Fixtures/execution-default.json'
    } else { [IO.Path]::GetFullPath($ExecutionConfig) }
    $executionExpected = [IO.File]::ReadAllText($executionPath) | ConvertFrom-Json
    $gameplayPath = if ([string]::IsNullOrWhiteSpace($GameplayConfig)) {
        Join-Path $root 'tests/Gameplay/Fixtures/gameplay-default.json'
    } else { [IO.Path]::GetFullPath($GameplayConfig) }
    $inputPath = if ([string]::IsNullOrWhiteSpace($InputConfig)) {
        Join-Path $root 'tests/Gameplay/Fixtures/input-default.json'
    } else { [IO.Path]::GetFullPath($InputConfig) }
    $gameplayExpected = [IO.File]::ReadAllText($gameplayPath) | ConvertFrom-Json
    $inputExpected = [IO.File]::ReadAllText($inputPath) | ConvertFrom-Json
    $colorsPath = if ([string]::IsNullOrWhiteSpace($ColorsConfig)) { Join-Path $root 'tests/Gameplay/Fixtures/colors-unity.json' } else { [IO.Path]::GetFullPath($ColorsConfig) }
    $colorsExpected = [IO.File]::ReadAllText($colorsPath) | ConvertFrom-Json
    $stylePath = if ([string]::IsNullOrWhiteSpace($StyleConfig)) { Join-Path $root 'style.json' } else { [IO.Path]::GetFullPath($StyleConfig) }
    $styleExpected = [IO.File]::ReadAllText($stylePath) | ConvertFrom-Json
} elseif (-not [string]::IsNullOrWhiteSpace($PhysicsConfig) -or -not [string]::IsNullOrWhiteSpace($ExecutionConfig) -or
          -not [string]::IsNullOrWhiteSpace($GameplayConfig) -or -not [string]::IsNullOrWhiteSpace($InputConfig) -or -not [string]::IsNullOrWhiteSpace($ColorsConfig) -or -not [string]::IsNullOrWhiteSpace($StyleConfig)) {
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
    if ($Overlays) { $start.ArgumentList.Add('--overlay-smoke-test') }
    $start.ArgumentList.Add('--window-config')
    $start.ArgumentList.Add($windowPath)
    if ($Mode -eq 'Gameplay') {
        $start.ArgumentList.Add('--physics-config')
        $start.ArgumentList.Add($physicsPath)
        $start.ArgumentList.Add('--execution-config')
        $start.ArgumentList.Add($executionPath)
        $start.ArgumentList.Add('--gameplay-config')
        $start.ArgumentList.Add($gameplayPath)
        $start.ArgumentList.Add('--input-config')
        $start.ArgumentList.Add($inputPath)
        $start.ArgumentList.Add('--colors-config')
        $start.ArgumentList.Add($colorsPath)
        $start.ArgumentList.Add('--style-config')
        $start.ArgumentList.Add($stylePath)
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
    if (-not $output.Contains("Window configuration: width=$($windowExpected.width); height=$($windowExpected.height); path=$windowPath") -or
        -not $output.Contains('Window created: 1280x720; framebuffer=1280x720.')) { throw 'Missing selected window path/effective startup dimensions.' }
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
        $expectedExecution = "Execution configuration: workers=$($executionExpected.workerCount); physicsWorkers=$($executionExpected.physicsWorkerCount); maxExpectedConcurrentJobs=$($executionExpected.maxExpectedConcurrentJobs); path=$executionPath"
        if (-not $output.Contains($expectedExecution)) { throw 'Loaded execution budgets/path do not match selected fixture.' }
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
        $game = [regex]::Match($output, 'Gameplay configuration: width=([^;]+); height=([^;]+); left=([^;]+); top=([^;]+); right=([^;]+); bottom=([^;]+); wallThickness=([^;]+); radius=([^;]+); speed=([^;]+); spawnX=([^;]+); spawnY=([^;]+); initialAimX=([^;]+); initialAimY=([^;]+); startingBalls=(\d+); catchZoneHeightFraction=([^;]+); maxBouncesWithoutBrick=(\d+); bricks=(\d+); path=([^\r\n]+)')
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
        if ($game.Groups[18].Value -ne $gameplayPath -or $loadedInput.Groups[2].Value -ne $inputPath -or
            [float]::Parse($loadedInput.Groups[1].Value, $culture) -ne [float]$inputExpected.stickDeadzone -or
            [float]::Parse($probe.Groups[1].Value, $culture) -ne [float]$inputExpected.stickDeadzone -or
            [double]::Parse($motion.Groups[2].Value, $culture) -ne [double]$b.speed -or
            [Math]::Abs([double]::Parse($motion.Groups[1].Value, $culture) - [double]$b.speed) -gt [Math]::Max(1.0, [double]$b.speed * 1e-5)) {
            throw 'Loaded gameplay/input paths, effective deadzone or native launch speed differ from fixtures.'
        }
        $rules = $gameplayExpected.breakout
        if ([int]$game.Groups[14].Value -ne [int]$rules.startingBalls -or
            [double]::Parse($game.Groups[15].Value, $culture) -ne [double]$rules.catchZoneHeightFraction -or
            [int]$game.Groups[16].Value -ne [int]$rules.maxBouncesWithoutBrick -or
            [int]$game.Groups[17].Value -ne $rules.bricks.Count -or
            $output -notmatch 'Gameplay catch-line blocked PASS:' -or $output -notmatch 'Gameplay bricks PASS:') {
            throw 'Missing configured round rules, native brick destruction or blocked-above-line proof.'
        }
        $scheme = [regex]::Match($output, 'Colors configuration: name=([^;]+); (.+); path=([^\r\n]+)')
        if (-not $scheme.Success -or $scheme.Groups[1].Value -ne $colorsExpected.name -or $scheme.Groups[3].Value -ne $colorsPath) { throw 'Wrong loaded color scheme/path.' }
        foreach ($role in $colorsExpected.roles.PSObject.Properties) {
            $rgb = [regex]::Match($scheme.Groups[2].Value, '(?:^|; )' + [regex]::Escape($role.Name) + '=([^;]+)')
            if (-not $rgb.Success) { throw "Missing actual semantic color: $($role.Name)" }
            $hex = $colorsExpected.palette.($role.Value)
            $parts = $rgb.Groups[1].Value.Split(',')
            for ($channel = 0; $channel -lt 3; $channel++) {
                $s = [Convert]::ToByte($hex.Substring(1 + 2 * $channel, 2), 16) / 255.0
                $linear = if ($s -le .04045) { $s / 12.92 } else { [Math]::Pow(($s + .055) / 1.055, 2.4) }
                if ([Math]::Abs([double]::Parse($parts[$channel], $culture) - $linear) -gt 1e-6) { throw "Color conversion/role mismatch: $($role.Name)" }
            }
        }
        Write-Output "Verified color scheme: $($colorsExpected.name); all semantic linear RGB roles match; path=$colorsPath."
        $loadedStyle = [regex]::Match($output, 'Style configuration: path=([^;]+); ([^\r\n]+)')
        if (-not $loadedStyle.Success -or $loadedStyle.Groups[1].Value -ne $stylePath) { throw 'Wrong loaded style/path.' }
        $styleFields = @{}
        foreach ($property in $styleExpected.PSObject.Properties) {
            if ($property.Name -eq 'hud') {
                foreach ($hud in $property.Value.PSObject.Properties) { $styleFields["Hud.$($hud.Name)"] = $hud.Value }
            } else { $styleFields[$property.Name] = $property.Value }
        }
        foreach ($name in $styleFields.Keys) {
            $actual = [regex]::Match($loadedStyle.Groups[2].Value, '(?:^|; )' + [regex]::Escape($name) + '=([^;]+)', 'IgnoreCase')
            if (-not $actual.Success -or [double]::Parse($actual.Groups[1].Value, $culture) -ne [double]$styleFields[$name]) { throw "Style mismatch: $name" }
        }
        $hudGeometry = [regex]::Match($output, 'Gameplay HUD geometry: viewport=1280x720; barOuterWidth=([^;]+); barOuterHeight=([^;]+); barCenterY=([^\r\n]+)\.')
        if (-not $hudGeometry.Success) { throw 'Missing emitted HUD geometry bounds.' }
        $h = $styleExpected.hud
        $viewportScale = [Math]::Min(1280.0 / $a.width, 720.0 / $a.height)
        $expectedHud = @(
            (([Math]::Min([double]$h.barMaxWidth, [double](($a.right - $a.left) * $h.barMaxArenaWidthFraction)) + $h.barOutlineWidth) * $viewportScale),
            (($h.barHeight + $h.barOutlineWidth) * $viewportScale),
            ((720 - $a.height * $viewportScale) / 2 + [Math]::Max([double]$h.barMinCenterY, [double](($a.top - $a.wallThickness) * $h.barTopMarginFraction)) * $viewportScale))
        for ($i = 0; $i -lt 3; $i++) {
            if ([Math]::Abs([double]::Parse($hudGeometry.Groups[$i + 1].Value, $culture) - $expectedHud[$i]) -gt .001) { throw 'Emitted HUD geometry does not match configured dimensions.' }
        }
        Write-Output "Verified actual indexed HUD bounds: $($hudGeometry.Value)"
        Write-Output "Verified presentation style: all required dimensions match; path=$stylePath."
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
        $viewports = [regex]::Matches($output, 'Gameplay viewport: (\d+)x(\d+); indices=(\d+); arenaIndices=(\d+); ballIndices=(\d+); aimIndices=(\d+); brickIndices=(\d+); catchLineIndices=(\d+); hudIndices=(\d+); backgroundIndices=(\d+); heldRingIndices=(\d+)\.')
        $sizes = [Collections.Generic.HashSet[string]]::new()
        foreach ($viewport in $viewports) {
            if ([int]$viewport.Groups[3].Value -le 0 -or [int]$viewport.Groups[4].Value -le 0 -or
                [int]$viewport.Groups[5].Value -le 0 -or [int]$viewport.Groups[6].Value -le 0 -or
                [int]$viewport.Groups[7].Value -le 0 -or [int]$viewport.Groups[8].Value -le 0 -or [int]$viewport.Groups[9].Value -le 0) {
                throw 'Missing actual arena/ball/aim indexed geometry.'
            }
            if ([int]$viewport.Groups[10].Value -ne 6 -or [int]$viewport.Groups[11].Value -le 0) { throw 'Missing full-frame background/held-ring geometry.' }
            $sum = 0
            foreach ($i in 4..11) { $sum += [int]$viewport.Groups[$i].Value }
            if ($sum -ne [int]$viewport.Groups[3].Value) { throw 'Category index receipts do not sum to actual indices.' }
            [void]$sizes.Add("$($viewport.Groups[1].Value)x$($viewport.Groups[2].Value)")
        }
        $result = [regex]::Match($output, 'Gameplay: launch wall catch hold release relaunch reset passed; ticks=(\d+); nonemptyPresentedFrames=(\d+); hidden resize and resumed presentation passed\.')
        if (-not $sizes.Contains('1280x720') -or -not $sizes.Contains('960x540') -or
            -not $result.Success -or [int]$result.Groups[1].Value -le 0 -or
            [int]$result.Groups[2].Value -lt 9 -or $output -notmatch 'Validation: 0 errors, \d+ warnings\.') {
            throw 'Missing gameplay transitions/resize/validation evidence.'
        }
    }
    if ($Overlays) {
        foreach ($size in @('1280x720', '960x540')) {
            if (-not $output.Contains("HUD-only viewport: $size; inspectorActive=False; capture=False; directQuadIndices=True.") -or
                -not $output.Contains("Overlay viewport: $size;")) { throw "Missing actual HUD-only/inspector/image receipts at $size." }
        }
        if (-not $output.Contains('Overlay smoke PASS: text, Stb image and readonly inspector presented at both extents; generation advanced; audio device disabled.')) { throw 'Missing completed overlay lifecycle evidence.' }
    }
    if (-not $output.Contains('Audio host: nativeRequested=False; muted=True.')) { throw 'Hidden mode did not confirm disabled audio device creation.' }
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
