# brutal-breakout

A Windows-first 2D game built with C# and [BRUTAL](https://github.com/ahwootech/BRUTAL).

## Requirements

- .NET SDK 10.0.201 (or a newer patch in the same feature band; see `global.json`).
- Windows for the currently tested setup.

BRUTAL packages are restored from NuGet; cloning or building the framework source is not required.

## Build and run

From this directory:

```powershell
dotnet restore --locked-mode
dotnet build --no-restore
dotnet run --no-build
```

Expected result: an empty 1280 x 720 window titled **Brutal Breakout**. Close it using the window close button. No renderer or gameplay exists yet.

## How this differs from Unity

- `Program.cs` owns initialization, event processing, and shutdown. There is no Unity player managing the lifecycle.
- GLFW creates the native window and handles OS events. It does not draw the game.
- The current loop waits for events to avoid busy-spinning. Gameplay will need a timed update/render loop with explicit delta time.
- BRUTAL is a framework: scenes, entities, asset loading, and rendering are our responsibility. We will add only what this game needs.

## Project files

- `Program.cs`: first-window application.
- `brutal-breakout.csproj`: target framework, C# settings, and direct package references.
- `global.json`: SDK selection.
- `packages.lock.json`: resolved direct and transitive dependency versions; commit changes when intentionally updating packages.
- `.gitignore`: excludes generated build output and local IDE files.
- `.gitattributes`: source text normalization.

## Dependencies

Direct references:

- `Ahwoo.BRUTAL.Core` 2026.10.4
- `Ahwoo.BRUTAL.Glfw` 2026.10.5

GLFW's native library and other BRUTAL dependencies arrive transitively. The current dependency graph also includes a preview version of `Microsoft.Extensions.ObjectPool`; see the lock file. We have not added that preview package directly.

No graphics API is selected yet. A Vulkan SDK/GPU check is deferred until we choose the renderer.

## Git

This project starts as a local repository on `main`. No remote is configured or GitHub repository created.

Useful commands:

```powershell
git status
git log --oneline
git diff
```

## Release checklist

Before distributing a build, review the current [BRUTAL license](https://github.com/ahwootech/BRUTAL) and all shipped third-party notices. BRUTAL documents requirements for copyright attribution in credits, its logo before gameplay, and a shipped `NOTICE.txt`. Those release assets are not implemented in this first-window milestone.

## Next milestone

Choose a simple 2D rendering approach, draw a paddle and ball, then introduce input and a timed game loop. Avoid building a general-purpose engine ahead of the game.
