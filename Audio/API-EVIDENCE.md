# Audio published API audit

Read AGENTS.md, full package-audit skill, approved media plan, existing API audit and its SDL.Mixer evidence JSON before implementation. Root app csproj/lock inspected; no app references or locks changed. Exact dependency closure is in Audio/packages.lock.json.

| Capability | Published package/version | Exact symbol/probe evidence | Reuse | Gap/unknown |
|---|---|---|---|---|
| Decode/cache WAV | Ahwoo.BRUTAL.Sdl.Mixer 2026.10.3 | Brutal.SdlApi.Mixer.MixMixer.LoadAudio(string,bool) -> MixAudio; existing audit DLL metadata + compile probe | Predecode once; dispose cached clips | Runtime decoding untested |
| Bounded voices | Same | MixMixer.CreateTrack() -> MixTrack; MixTrack.SetAudio(MixAudio), SetGain(float), Play(uint), Playing() -> bool, Stop(long), Dispose() | Explicit reusable tracks; never unbounded PlayAudio | Custom game cue mapping/drop-new saturation policy required |
| Main-thread device lifetime | Same | SdlMixer.Init(), CreateMixerDevice(uint,SdlAudioSpec*) -> MixMixer, Quit(); MixMixer.Dispose() | No SDL window or global SDL Quit | Hardware/lifecycle untested, factory nonfatal for unavailable backend |

Metadata provenance: https://api.nuget.org/v3-flatcontainer/ahwoo.brutal.sdl.mixer/2026.10.3/ahwoo.brutal.sdl.mixer.2026.10.3.nupkg . Published managed DLL lib/net10.0/Brutal.Sdl.Mixer.dll. Native 2026.10.3 provides SDL_mixer 3.2.4; requires SDL.Native 2026.10.3. Managed dependencies: Sdl.Mixer.Native/Sdl/Logging 2026.10.3, Core 2026.10.4, Interop 2026.10.2. Additional dependencies and archive hashes are preserved in the supplied vault audit evidence; new locks record resolved hashes. BRUTAL/Zlib/codec notices are copied verbatim under Assets/Audio/ThirdParty.

New compile-only explicit-track probe (never invoked), exact project:
```xml
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><PackageReference Include="Ahwoo.BRUTAL.Sdl.Mixer" Version="[2026.10.3]" /></ItemGroup></Project>

```
```csharp
using Brutal.SdlApi.Mixer;
public static class TrackProbe {
 public static void Probe(MixMixer mixer, MixAudio clip) {
  MixTrack track = mixer.CreateTrack();
  try { track.SetAudio(clip); track.SetGain(0.5f); track.Play(0); bool playing = track.Playing(); track.Stop(0); }
  finally { track.Dispose(); }
 }
}
```

Managed reflection-only entry point (ran once in initial temporary probe; enumerates public literal fields, never calls SDL):
```csharp
using System; using System.Reflection; using Brutal.SdlApi;
foreach(var t in typeof(Sdl).Assembly.GetExportedTypes()) foreach(var f in t.GetFields(BindingFlags.Public|BindingFlags.Static)) if(f.Name.Contains("DEFAULT",StringComparison.OrdinalIgnoreCase)||f.Name.Contains("Playback")) Console.WriteLine($"{t.FullName}.{f.Name} = {(f.IsLiteral?f.GetRawConstantValue():"nonliteral")}");
```
No published default-playback constant was found; uint.MaxValue is the SDL3 ABI definition SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, not a user-tunable default. Verified without native execution: `curl -fsSL --max-time 20 https://raw.githubusercontent.com/libsdl-org/SDL/release-3.2.22/include/SDL3/SDL_audio.h | grep -A3 -B3 '#define SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK'` exited 0 and returned `#define SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK ((SDL_AudioDeviceID) 0xFFFFFFFFu)`, documented since SDL 3.2.0. This ABI constant is source semantic evidence, not a claimed managed package symbol.

Commands: `dotnet restore /tmp/brutal-audio-probe-xaM4Vj/probe.csproj --source https://api.nuget.org/v3/index.json` exit 0; `dotnet build /tmp/brutal-audio-probe-xaM4Vj/probe.csproj --no-restore --nologo` exit 0.

```text
  Determining projects to restore...
  Restored C:\Users\aaron\AppData\Local\Temp\brutal-audio-probe-xaM4Vj\probe.csproj (in 276 ms).
  probe -> C:\Users\aaron\AppData\Local\Temp\brutal-audio-probe-xaM4Vj\bin\Debug\net10.0\probe.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.82
```

The temporary probe tree was deleted after preserving its source/output. Component build also compiles actual explicit-track calls. No mixer initialization/device/playback was run. Tests use fake/null output, except allowed Box2D native simulation. Custom code is limited to demonstrated game gaps: strict settings/WAV contract, game facts/cue mapping, bounded saturation, focus/mute suppression, owned lifetime and bounded diagnostic. No custom decoder, runtime synthesizer, SDL window, async callbacks or alternate audio library.
