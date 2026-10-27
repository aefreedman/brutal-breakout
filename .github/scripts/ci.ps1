param(
    [Parameter(Mandatory)][string]$CrushKitRoot,
    [string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$CrushKitRoot = (Resolve-Path $CrushKitRoot).Path
function Check([string]$stage) { if ($LASTEXITCODE -ne 0) { throw "$stage failed (exit $LASTEXITCODE)." } }
$revision = [IO.File]::ReadAllText((Join-Path $root '.github/crushkit-revision')).Trim()
if ($revision -notmatch '^[0-9a-f]{40}$') { throw 'The CrushKit pin must be a full commit SHA.' }
$actual = git -C $CrushKitRoot rev-parse HEAD
Check 'Read kit revision'
if ($actual -ne $revision) { throw "CrushKit checkout must be at $revision (found $actual)." }
$source = @('-p:UseCrushKitProjects=true', "-p:CrushKitRoot=$CrushKitRoot")
Push-Location $root
try {
    dotnet restore BrutalBreakout.slnx --locked-mode @source
    Check 'Locked source restore'
    dotnet build BrutalBreakout.slnx -c $Configuration --no-restore @source
    Check 'Source build'
    foreach ($project in @(
        'tests/Runtime/Simulation/Simulation.Tests.csproj',
        'tests/Runtime/Gameplay/Gameplay.Tests.csproj',
        'tests/Editor/BrutalBreakout.Editor.Tests.csproj',
        'tests/Runtime/Input/BrutalBreakout.Input.Tests.csproj',
        'tests/Runtime/Audio/Audio.Tests.csproj',
        'tests/Runtime/Media/BrutalBreakout.Media.Tests.csproj',
        'tests/Performance/BrutalBreakout.Performance.Tests.csproj'
    )) {
        dotnet test $project -c $Configuration --no-build --no-restore @source
        Check "$project headless tests"
    }
} finally { Pop-Location }
