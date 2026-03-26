param(
    [string]$Configuration = "Release",
    [string]$CoverageRoot = "coverage"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$resolvedCoverageRoot = Join-Path $repoRoot $CoverageRoot

$projects = @(
    @{ Name = "core"; Path = "tests/CodexSessionManager.Core.Tests/CodexSessionManager.Core.Tests.csproj"; Include = "[CodexSessionManager.Core]*" },
    @{ Name = "storage"; Path = "tests/CodexSessionManager.Storage.Tests/CodexSessionManager.Storage.Tests.csproj"; Include = "[CodexSessionManager.Storage]*" },
    @{ Name = "app"; Path = "tests/CodexSessionManager.App.Tests/CodexSessionManager.App.Tests.csproj"; Include = "[CodexSessionManager.App]*" }
)

if (Test-Path $resolvedCoverageRoot) {
    Remove-Item -Recurse -Force $resolvedCoverageRoot
}

New-Item -ItemType Directory -Force -Path $resolvedCoverageRoot | Out-Null

dotnet restore CodexSessionManager.sln
dotnet build CodexSessionManager.sln --configuration $Configuration --no-restore

foreach ($project in $projects) {
    $projectCoverageDir = Join-Path $resolvedCoverageRoot $project.Name
    New-Item -ItemType Directory -Force -Path $projectCoverageDir | Out-Null
    $coverletOutput = Join-Path $projectCoverageDir "coverage"

    dotnet test $project.Path `
        --configuration $Configuration `
        --no-build `
        /p:CollectCoverage=true `
        /p:Include="$($project.Include)" `
        /p:ExcludeByFile="**/obj/**/*.g.cs" `
        /p:CoverletOutputFormat=cobertura `
        /p:CoverletOutput="$coverletOutput"
}
