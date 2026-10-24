# Published API evidence and component handoff

## Provenance

Inspected published NuGet cache DLLs, not a source checkout. `.nupkg.metadata` for both
packages identifies https://api.nuget.org/v3/index.json. Assets: `lib/net10.0/Brutal.Glfw.dll`
and `lib/net10.0/Brutal.Numerics.dll` under `%USERPROFILE%/.nuget/packages`.

- Ahwoo.BRUTAL.Glfw **2026.10.5**, archive SHA256
  `1fcf7c00429da8f60067efae61a5aeaf35075aa804fb5db6854a45d601f75bd5`.
  Dependencies: Glfw.Native 2026.10.5, Core 2026.10.4, Logging 2026.10.3,
  Numerics 2026.10.2 (all Ahwoo.BRUTAL).
- Ahwoo.BRUTAL.Numerics **2026.10.2**, archive SHA256
  `54f6923a2af2bc1af958553ca09861cf7733adc3d05e5dbd9f682449016c2b34`.
  Dependencies: Core 2026.10.4, CommunityToolkit.HighPerformance 8.4.2,
  Microsoft.Extensions.Logging and Logging.Console 10.0.0.
- Exact direct versions pinned in standalone project; resolved transitive versions and
  content hashes in `packages.lock.json`. Root package references/lock unchanged.

## Verified public symbols and reuse/gaps

GLFW symbols below are in `Brutal.GlfwApi`; vectors in `Brutal.Numerics`.
Reflection inspected published assemblies, including FixedBufferAttribute.

| Capability | Package/version | Public symbols / compile evidence | Decision / bounded gap |
| --- | --- | --- | --- |
| Mapped pad discovery/state | Glfw 2026.10.5 | `static bool Glfw.JoystickIsGamepad(int)`; `static bool Glfw.GetGamepadState(int, out GlfwGamepadState)` | Reuse standardized mappings; scan 0..15, retain selection. No raw XInput/PInvoke. |
| Mapped axes/buttons | Glfw 2026.10.5 | `GlfwGamepadState.Buttons`: public fixed byte[15]; `Axes`: public fixed float[6]. `GlfwGamepadAxis.LeftX/LeftY`; `GlfwGamepadButton.A/Start`; `GlfwButtonAction.Press` | Reuse; tiny unsafe buffer reads. |
| Keyboard/mouse | Glfw 2026.10.5 | `GlfwKeyAction GlfwWindow.GetKey(GlfwKey)`; `GlfwButtonAction GlfwWindow.GetMouseButton(GlfwMouseButton)`; public enums arrows, WASD, Space, R, Left | Reuse polling; custom game-specific aggregate edges/baselining in pure router. |
| Pointer/focus/window | Glfw 2026.10.5 | `double2 GlfwWindow.CursorPos { get; set; }`; `int2 Size { get; set; }`; `bool IsFocused { get; }`; `bool IsIconified { get; }`; `int GetAttribute(GlfwWindowAttribute)` | Read cursor/focus/iconification. Host owns event pump, transition Reset and coordinate conversion. |
| Vectors | Numerics 2026.10.2 | `float2(float,float)`; public float X/Y; `float Length()`; `static float2 Normalize(float2)`; `double2(double,double)`; public double X/Y | Reuse data types. Double length intermediates protect extreme/subnormal finite snapshot values from float overflow/underflow. |
| Gameplay arbitration | Above raw polling/vector APIs | Probe demonstrates raw snapshot facilities, not game policy | Small contract-specific deadzone/priority/edge policy; no claim that unrelated published APIs are absent. |

No native calls executed. Managed reflection only enumerated types/members/attributes.
Native-call probe was **built only**, not an attached-controller playtest. No GLFW
initialization, visible window, desktop input, or hardware changes.

## Disposable compile probe

Unique original directory: `%TEMP%/ball-input-api-8RBOlEtw`, outside repository.
Bash `mktemp -d /tmp/ball-input-api-XXXXXXXX` created it. Tool timeout: 120s per command.

Final commands (all exit 0):

```sh
dotnet restore "$probe/Probe.csproj"
dotnet build "$probe/Probe.csproj" --no-restore
# Managed reflection source only, as Program.cs:
dotnet run --project "$probe/Probe.csproj" --no-restore
```

Final compile: Build succeeded, 0 warnings, 0 errors, 0.71s.
Initial namespace discovery failed CS0234 for `Brutal.Glfw`; published XML guided the
correction to `Brutal.GlfwApi`, then reflection/compilation verified public signatures.
XML/source-only evidence was not treated as proof. Probe artifacts preserved below
before temporary directory cleanup.

Exact Probe.csproj:

```xml
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><PackageReference Include="Ahwoo.BRUTAL.Glfw" Version="[2026.10.5]"/><PackageReference Include="Ahwoo.BRUTAL.Numerics" Version="[2026.10.2]"/></ItemGroup></Project>

```

Exact native-call compile source (never executed):

```csharp
using Brutal.GlfwApi;
using Brutal.Numerics;
// Compile only: never called, no native initialization.
static class Probe {
 public static unsafe void Check(GlfwWindow w) {
  bool focused = w.IsFocused && !w.IsIconified;
  GlfwKeyAction key = w.GetKey(GlfwKey.Space);
  GlfwButtonAction mouse = w.GetMouseButton(GlfwMouseButton.Left);
  double2 cursor = w.CursorPos;
  int2 logicalSize = w.Size;
  for(int id=0; id<16; id++) if(Glfw.JoystickIsGamepad(id) && Glfw.GetGamepadState(id, out GlfwGamepadState s)) {
   float2 stick = new(s.Axes[(int)GlfwGamepadAxis.LeftX], -s.Axes[(int)GlfwGamepadAxis.LeftY]);
   bool a = s.Buttons[(int)GlfwGamepadButton.A] == (byte)GlfwButtonAction.Press;
   bool start = s.Buttons[(int)GlfwGamepadButton.Start] == (byte)GlfwButtonAction.Press;
   float2 normalized = float2.Normalize(stick); float length = stick.Length();
  }
 }
 public static void Main() { } // Do not execute this compile probe.
}

```

Exact managed reflection source (executed without invoking GLFW methods):

```csharp
using System.Reflection;
using Brutal.GlfwApi;
foreach (var t in typeof(Glfw).Assembly.GetExportedTypes().Where(t=>t.Name.Contains("Gamepad") || t.Name.Contains("Joystick") || t.Name.Contains("Window") || t.Name.Contains("Key") || t.Name.Contains("Mouse") || t.Name=="Glfw")) {
 Console.WriteLine(t.FullName);
 foreach(var m in t.GetMembers(BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly).Where(m=>t.Name!="Glfw" || new[]{"GetGamepadState","JoystickIsGamepad","GetKey","GetMouseButton","GetCursorPos","GetWindowAttrib"}.Any(n=>m.Name==n))) Console.WriteLine("  "+m);
}
Console.WriteLine(typeof(Brutal.Numerics.float2));
foreach(var m in typeof(Brutal.Numerics.float2).GetMembers(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Where(m=>m.Name is "X" or "Y" or ".ctor" or "Normalize" or "Length")) Console.WriteLine(m);
foreach(var f in typeof(GlfwGamepadState).GetFields()) {
 var a=f.GetCustomAttribute<System.Runtime.CompilerServices.FixedBufferAttribute>();
 Console.WriteLine($"{f.DeclaringType}.{f.Name}: fixed {a?.ElementType}[{a?.Length}]");
}
foreach(var m in typeof(Brutal.Numerics.double2).GetMembers(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).Where(m=>m.Name is "X" or "Y" or ".ctor")) Console.WriteLine(m);
```

Relevant fixed-buffer reflection output:

```text
Brutal.GlfwApi.GlfwGamepadState.Buttons: fixed System.Byte[15]
Brutal.GlfwApi.GlfwGamepadState.Axes: fixed System.Single[6]
```

## Public integration seam

Namespace: `BrutalBreakout.Platform.Input`.

```csharp
// Construct on GLFW's window/event thread; window remains host-owned.
var poller = new GlfwInputPoller(window);
var router = new InputRouter();
// After host's GLFW event pump, synchronously on the same thread:
InputSnapshot snapshot = poller.Poll();
InputIntent intent = router.Route(snapshot);
// On focus/minimize transitions, including when sampling is suspended:
router.Reset();
```

`InputSnapshot` is a readonly record struct with init properties: Focused, Minimized,
KeyboardDirection (float2), KeyboardPrimary, KeyboardRestart, MousePrimary, Cursor
(double2), PadConnected, PadId (int), PadStick (float2), PadPrimary, PadRestart.
`InputIntent` is a readonly record struct with positional properties
`(AimKind AimKind, float2 Aim, bool PrimaryPressed, bool RestartPressed, bool Active, int? PadId)`.
`AimKind` = None / Direction / Pointer. `InputRouter.Route(InputSnapshot)` returns
InputIntent; `Reset()` clears baselines. `StickDeadzone` is public const float 0.2f.
`GlfwInputPoller(GlfwWindow)` exposes `InputSnapshot Poll()` (no unsafe caller needed).

Directions: +Y up, normalized; poller inverts GLFW pad Y once. Pointer: logical GLFW
window coordinates, +Y down. Host handles DPI/framebuffer, clipping, letterbox/world
conversion, simulation mapping, and one-shot tick latching. None preserves game aim.
Priority: active keyboard > active pad stick > moved/clicked mouse. Initial cursor
position is baseline only; active stick can aim immediately. Nonfinite aim is ignored.

First sample/regain/reset suppresses held actions. Newly selected pad holds are baselined;
release/repress is required. Primary uses aggregate keyboard/mouse/pad edges; another
source pressed while one remains held cannot double-fire. Restart aggregates keyboard/pad.
Restart may coemit with primary; host/simulation gives it precedence. Inactive samples emit
no actions/aim and reset router. Poller does not set focus, warp cursor, alter modes,
configure mappings, or modify hardware.

Limits: polling can miss complete press/release or disconnect/reconnect between samples,
including replacement at the same joystick ID. Host must Reset on focus/minimize events
if inactive samples are skipped. No hardware compatibility/play-feel claims. No app
integration or rendering lifecycle changes; GPU smoke not applicable to this component.
