# Windows-first PowerShell 7 entry point, callable from any working directory.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('doctor', 'build', 'cleanup', 'smoke', 'package-versions', 'package-assets')]
    [string] $Operation,
    [ValidatePattern('(?i)^Ahwoo\.Brutal\.[A-Za-z0-9_.-]+$')][string] $PackageId,
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[.-][A-Za-z0-9.-]+)?$')][string] $Version,
    [ValidateRange(1, 600)][int] $TimeoutSeconds = 120
)
. (Join-Path $PSScriptRoot 'Process.ps1')
$steps = [Collections.Generic.List[object]]::new()
$notes = [Collections.Generic.List[string]]::new()
$exitCode = 0
$data = $null

function Add-Step([string] $File, [string[]] $Arguments, [int] $Limit = $TimeoutSeconds) {
    $step = Invoke-BoundedProcess -File $File -Arguments $Arguments -TimeoutSeconds $Limit
    $steps.Add($step)
    if ($step.exitCode -ne 0) { throw "Command failed (exit $($step.exitCode)): $File" }
    return $step
}

function Resolve-VulkanCommand([string] $Name) {
    # PATH can contain vulkaninfo even when VULKAN_SDK/Bin does not.
    $command = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    if ($env:VULKAN_SDK) {
        $candidate = Join-Path $env:VULKAN_SDK "Bin/$Name.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw "$Name not found on PATH or in VULKAN_SDK/Bin. No configuration was changed."
}

try {
    switch ($Operation) {
        'doctor' {
            # Keep independent checks running, reporting all missing prerequisites.
            $problems = [Collections.Generic.List[string]]::new()
            $checks = @(
                { $null = Add-Step 'dotnet' @('--version') 20 },
                { $null = Add-Step 'git' @('branch', '--show-current') 10 },
                { $null = Add-Step 'git' @('status', '--short', '--branch') 10 },
                { $null = Add-Step (Resolve-VulkanCommand 'glslc') @('--version') 20 },
                {
                    $info = Add-Step (Resolve-VulkanCommand 'vulkaninfo') @('--summary') 30
                    if ($info.outputTruncated) { throw 'vulkaninfo output truncated: layer availability unknown; inspect manually.' }
                    if ($info.outputTail -notmatch 'VK_LAYER_KHRONOS_validation') {
                        throw 'Vulkan loader responded, but VK_LAYER_KHRONOS_validation was not listed.'
                    }
                    $notes.Add('Vulkan loader/info and validation-layer enumeration passed; this is not a GPU smoke test.')
                }
            )
            foreach ($check in $checks) {
                try { & $check } catch { $problems.Add($_.Exception.Message) }
            }
            $data = @{ problems = @($problems.ToArray()) }
            if ($problems.Count) { throw 'Doctor found missing or unverified prerequisites.' }
        }
        { $_ -in 'build', 'cleanup', 'smoke' } {
            $null = Add-Step 'dotnet' @('restore', '--locked-mode')
            $null = Add-Step 'dotnet' @('build', '--no-restore')
            if ($Operation -in 'cleanup', 'smoke') {
                $null = Add-Step 'dotnet' @('run', '--no-build', '--', '--cleanup-self-test') 30
            }
            if ($Operation -eq 'smoke') {
                $notes.Add('Hidden GPU shape/resize smoke requested; no minimize/restore coverage. SDK validation is mandatory in the application.')
                $null = Add-Step 'dotnet' @('run', '--no-build', '--', '--smoke-test') 45
            }
        }
        { $_ -in 'package-versions', 'package-assets' } {
            if (-not $PackageId) { throw 'PackageId is required for package operations.' }
            $id = $PackageId.ToLowerInvariant()
            $base = "https://api.nuget.org/v3-flatcontainer/$id"
            if ($Operation -eq 'package-versions') {
                $index = Invoke-RestMethod -Uri "$base/index.json" -TimeoutSec ([Math]::Min($TimeoutSeconds, 30))
                $versions = @($index.versions)
                $data = @{
                    packageId = $PackageId
                    source = "$base/index.json"
                    versionCount = $versions.Count
                    versions = @($versions | Select-Object -Last 30)
                    versionsTruncated = $versions.Count -gt 30
                    evidence = 'Published NuGet metadata only; NOT API verification.'
                }
            } else {
                if (-not $Version) { throw 'Version is required for package-assets.' }
                $v = $Version.ToLowerInvariant()
                $url = "$base/$v/$id.$v.nupkg"
                $temp = Join-Path ([IO.Path]::GetTempPath()) ("brutal-package-" + [guid]::NewGuid())
                $null = New-Item -ItemType Directory -Path $temp
                try {
                    $package = Join-Path $temp 'published.nupkg'
                    Invoke-WebRequest -Uri $url -OutFile $package -TimeoutSec ([Math]::Min($TimeoutSeconds, 30))
                    $hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
                    $zip = [IO.Compression.ZipFile]::OpenRead($package)
                    try {
                        $assets = @($zip.Entries | Where-Object { $_.FullName -match '^(lib|ref|runtimes|build|tools)/|\.nuspec$' } |
                            Select-Object -First 100 | ForEach-Object { $_.FullName })
                        $data = @{
                            packageId = $PackageId; version = $Version; source = $url; sha256 = $hash
                            entryCount = $zip.Entries.Count; assets = $assets
                            assetListingMayBeTruncated = $assets.Count -eq 100
                            evidence = 'Published archive/asset inventory only; NOT symbol or compile-probe API verification.'
                        }
                    } finally { $zip.Dispose() }
                } finally { Remove-Item -LiteralPath $temp -Recurse -Force }
            }
        }
    }
} catch {
    $notes.Add($_.Exception.Message)
    $exitCode = 1
    if ($steps.Count -and $steps[$steps.Count - 1].exitCode -ne 0) {
        $exitCode = $steps[$steps.Count - 1].exitCode
    }
}
$notes.Add('Baseline: 3 existing GOG naming warnings are known upstream noise when emitted. Report observed GOG warnings separately from other warnings/errors; counts may vary or be absent on incremental builds. Never suppress diagnostics or infer success from counts.')
[pscustomobject]@{
    operation = $Operation
    repositoryRoot = $script:RepositoryRoot
    callerCwd = (Get-Location).Path
    exitCode = $exitCode
    steps = @($steps.ToArray())
    data = $data
    notes = @($notes.ToArray())
} | ConvertTo-Json -Depth 10
exit $exitCode
