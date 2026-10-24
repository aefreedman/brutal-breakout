---
name: brutal-package-audit
description: Audit published Ahwoo.Brutal packages and verify their APIs before writing custom framework-like code, adding dependencies, or choosing Vulkan/GLFW helpers. Produce published-symbol or compile-probe evidence and a reuse/gap table.
compatibility: Windows with PowerShell 7, .NET SDK, and NuGet network access.
---

# Published package first

Before custom code, identify the needed capability and inspect the project's csproj and lock file. Check the **published** Ahwoo.Brutal.* packages, not just a checkout, online source, or memory. Do not edit project package references as part of this audit.

Resolve `../../scripts/support.ps1` relative to this skill directory (not the shell cwd). Human-runnable examples:

```powershell
pwsh -NoProfile -File <resolved-script> -Operation package-versions -PackageId Ahwoo.BRUTAL.Core
pwsh -NoProfile -File <resolved-script> -Operation package-assets -PackageId Ahwoo.BRUTAL.Core -Version 2026.10.4
```

These bounded operations query public NuGet, list up to 30 versions / 100 assets, report provenance and archive SHA256, and delete downloaded archives. Metadata and asset lists establish availability **only**. They do NOT verify APIs. Network failures, missing packages, and missing symbols are distinct outcomes. If using Pi, `brutal_support` exposes these same operations only on user request; it is optional, not needed to run the scripts.

## Required API evidence

1. Record exact package IDs, versions, source URL, target framework, and relevant transitive dependencies. Prefer the locked versions first; check another published version only when needed.
2. Inspect symbols in the **published DLL/reference assembly** (using available local reflection/metadata tooling), or compile a minimal disposable probe against the exact published package. Record fully qualified type/member signatures, overloads, accessibility, and relevant limitations. XML docs can guide discovery but are not a substitute for symbol/compile evidence. Source-only results remain unverified.
3. For a compile probe, create a uniquely named directory under the system temp directory, outside the repository; use a minimal net10.0 csproj with exact PackageReference versions and a tiny C# source snippet exercising the proposed calls. Run restore/build with bounded process timeouts (the shared `../../scripts/Process.ps1` exposes `Invoke-BoundedProcess` with explicit `-WorkingDirectory` and `-TimeoutSeconds`). Inspect every exit code; success is required. Do not install tools, run package executables, or execute native/GPU probe code without approval. A successful compile proves API shape, not runtime correctness.
4. Preserve the exact probe source, csproj, commands, exit codes, and relevant diagnostics in the audit response before deleting the temp directory in `finally`. Keep probes narrow. No app source, csproj, lock-file, or machine-configuration changes.
5. If symbol/probe tooling is unavailable, stop short of recommending custom implementation as proven necessary: mark API availability **unknown**, explain the blocker, and request the needed evidence/decision.

## Required result

Provide a table: capability | published package/version | exact symbol/probe evidence | reuse decision | verified gap/unknown. Then list proposed custom code **only for demonstrated gaps**, tradeoffs, and unresolved questions. Separate published metadata, verified API shape, and runtime evidence. Never call a metadata/source-only audit API verification. Keep all output bounded; do not dump environment variables or credentials.
